using System;
using System.Collections.Generic;
using System.Linq;
using Alpaca4d.Interop;
using Karamba.CrossSections;
using Karamba.Elements;
using Karamba.Loads;
using Karamba.Loads.Beam;
using Karamba.Materials;
using KarambaModel = Karamba.Models.Model;
using Karamba.Utilities;
using KG = Karamba.Geometry;
using RG = Rhino.Geometry;

namespace Alpaca4d.Karamba3D
{
    /// <summary>
    /// Reads an assembled Karamba3D 3.1 model into an <see cref="ImportModel"/>.
    ///
    /// It reads what Karamba3D's assembly has already settled - merged nodes, loads attached to their
    /// elements, mesh loads turned into nodal and beam loads - rather than redoing any of it, and
    /// reports whatever it does not carry over instead of dropping it quietly.
    /// </summary>
    internal sealed class KarambaReader
    {
        private const string Karamba3D = "Karamba3D";

        private readonly KarambaModel _model;
        private readonly ImportModel _out;
        private readonly ConversionReport _report;

        private readonly Dictionary<FemMaterial, ImportMaterial> _materials = new Dictionary<FemMaterial, ImportMaterial>();
        private readonly Dictionary<CroSec, ImportBeamSection> _sections = new Dictionary<CroSec, ImportBeamSection>();

        /// <summary>Index in <see cref="ImportModel.Beams"/> of each Karamba element read as a beam.</summary>
        private readonly Dictionary<int, int> _beamOfElement = new Dictionary<int, int>();

        /// <summary>Karamba3D's local axes (x, y, z) of each element read as a beam.</summary>
        private readonly Dictionary<int, KG.Vector3[]> _axesOfElement = new Dictionary<int, KG.Vector3[]>();

        private readonly HashSet<string> _knownCases;
        private UnitConversion _kg;
        private double _gravity = 10.0;

        private KarambaReader(KarambaModel model)
        {
            _model = model;
            _out = new ImportModel { Source = "the Karamba3D model" };
            _report = _out.Report;

            var ordered = model.lcCombinationCollection?.OrderedLoadCaseIds?.ToList() ?? new List<string>();
            _knownCases = new HashSet<string>(ordered);
            _out.LoadCases.AddRange(ordered);
        }

        public static ImportModel Read(object value)
        {
            if (!(value is KarambaModel model))
                throw new ArgumentException($"Expected a Karamba3D model, not {value?.GetType().Name ?? "nothing"}.");

            var reader = new KarambaReader(model);
            reader.ReadAll();
            return reader._out;
        }

        private void ReadAll()
        {
            if (!ReadUnits())
                return;

            foreach (var node in _model.nodes)
                _out.Nodes.Add(Point(node.pos));

            for (int i = 0; i < _model.elems.Count; i++)
                ReadElement(i, _model.elems[i]);

            ReadSupports();
            ReadPointLoads();
            ReadMeshLoads();
            ReadGravity();
            ReadPointMasses();
            ReadWhatIsNotConverted();
        }

        #region units

        /// <summary>
        /// Karamba3D's base units can be changed in karamba.ini; Alpaca4d works in m, kN and kg. Masses
        /// go through Karamba's own kg conversion, the one its components display them with, so they
        /// come out in kg whatever Karamba holds them in.
        /// </summary>
        private bool ReadUnits()
        {
            var conversion = UnitsConversionFactory.Conv();
            string length = conversion.base_length?.unit;
            string force = conversion.base_force?.unit;
            _kg = conversion.kg();

            if (length != "m" || force != "kN" || _kg?.unit != "kg")
            {
                _report.Error($"The {Karamba3D} model is in {length ?? "?"} and {force ?? "?"}. Alpaca4d works in m and kN, and this converter does not " +
                              "convert units yet: set UnitsSystem = SI, UnitLength = m and UnitForce = kN in karamba.ini.");
                return false;
            }

            double gravity = 0.0;
            try { gravity = IniConfigData.IniConfig?.Gravity ?? 0.0; }
            catch (Exception) { /* fall back below */ }

            if (gravity > 0.0)
                _gravity = gravity;
            else
                _report.Remark($"{Karamba3D}'s acceleration of gravity could not be read, so its default of 10 m/s2 is used to turn specific weights into densities.");

            return true;
        }

        #endregion

        #region elements

        private void ReadElement(int index, ModelElement element)
        {
            string id = ElementLabel(index, element);

            if (!element.IsActive)
            {
                _report.Remark($"These elements are inactive in {Karamba3D}, which leaves them out of its analysis, and are left out", id);
                return;
            }

            // Most derived first: a ModelBeam is a ModelTruss, and a ModelShell a ModelMembrane.
            switch (element)
            {
                case ModelBeam beam:
                    ReadBeam(index, id, beam);
                    break;
                case ModelTruss _:
                    _report.Error("Trusses (beams without bending stiffness) are not converted yet: Alpaca4d has no truss element", id);
                    break;
                case ModelSpring _:
                    _report.Error("Springs are not converted yet", id);
                    break;
                case ModelShell shell:
                    ReadShell(id, shell);
                    break;
                case ModelMembrane _:
                    _report.Error("Membranes (shells without bending stiffness) are not converted yet", id);
                    break;
                default:
                    _report.Error($"{Karamba3D} elements of type {element.GetType().Name} are not converted", id);
                    break;
            }
        }

        private void ReadBeam(int index, string id, ModelBeam beam)
        {
            // Any joint at all, whatever its values: which of a joint's entries means "released" has
            // not been checked against Karamba3D yet, and a misread release is a silent error.
            if (beam.joint != null)
            {
                _report.Error("Beams with joints (hinges or springs at their ends) are not converted yet", id);
                return;
            }

            if (HasEccentricity(beam))
            {
                _report.Error("Beams with an eccentricity are not converted yet", id);
                return;
            }

            if (!(beam.crosec is CroSec_Beam crosec))
            {
                _report.Error($"Beams whose cross-section is not a beam section ({beam.crosec?.GetType().Name ?? "none"}) are not converted", id);
                return;
            }

            if (beam.node_inds.Count != 2)
            {
                _report.Error("Beams that do not join exactly two nodes are not converted", id);
                return;
            }

            if (Math.Abs(beam.nII) > 0.0)
                _report.Remark($"The initial normal force (NII) {Karamba3D} uses in second-order analysis is not carried over", id);

            var section = Section(crosec);
            if (section == null)
                return;

            var axes = beam.localCoSys(_model.nodes);

            _beamOfElement[index] = _out.Beams.Count;
            _axesOfElement[index] = axes;
            _out.Beams.Add(new ImportBeam
            {
                Id = id,
                NodeI = beam.node_inds[0],
                NodeJ = beam.node_inds[1],
                Section = section,
                LocalZ = Vector(axes[2]),
            });

            ReadBeamLoads(index, id, beam, axes);
        }

        private void ReadShell(string id, ModelShell shell)
        {
            if (HasEccentricity(shell))
            {
                _report.Error("Shells with an eccentricity are not converted yet", id);
                return;
            }

            if (!(shell.crosec is CroSec_Shell crosec))
            {
                _report.Error($"Shells whose cross-section is not a shell section ({shell.crosec?.GetType().Name ?? "none"}) are not converted", id);
                return;
            }

            var mesh = shell.mesh;
            var nodes = shell.node_inds;
            if (mesh == null || nodes == null || nodes.Count != mesh.Vertices.Count)
            {
                _report.Error($"Shells whose mesh vertices could not be matched to {Karamba3D}'s nodes are not converted", id);
                return;
            }

            _report.Remark($"{Karamba3D}'s shell triangles take no transverse shear deformation and Alpaca's ASDShellT3 does, so thick plates come out a little softer in Alpaca.");

            for (int face = 0; face < mesh.Faces.Count; face++)
            {
                var layers = crosec.elem_crosecs[face]?.Layers;
                if (layers == null || layers.Count == 0)
                {
                    _report.Error("Shells with a face that has no cross-section layer are not converted", id);
                    continue;
                }

                if (layers.Count > 1)
                {
                    _report.Error("Layered shell cross-sections are not converted yet", id);
                    continue;
                }

                var layer = layers[0];
                if (!layer.bending)
                {
                    _report.Error("Shell layers without bending stiffness (membranes) are not converted yet", id);
                    continue;
                }

                if (Math.Abs(layer.eccent) > 1e-9)
                {
                    _report.Error("Shell layers with an eccentricity are not converted yet", id);
                    continue;
                }

                var femMaterial = layer.material ?? crosec.material;
                if (femMaterial is FemMaterial_Orthotropic)
                {
                    _report.Error("Shells with an orthotropic material are not converted yet", id);
                    continue;
                }

                var material = Material(femMaterial, crosec.name);
                if (material == null)
                    continue;

                var f = mesh.Faces[face];
                AddShell(id, nodes[f.A], nodes[f.B], nodes[f.C], layer.height, material);

                if (f.IsQuad)
                {
                    AddShell(id, nodes[f.A], nodes[f.C], nodes[f.D], layer.height, material);
                    _report.Remark("Quadrilateral shell faces are split into two triangles for ASDShellT3", id);
                }
            }

            if (shell.Elem_loads != null && shell.Elem_loads.Any(load => !load.generated()))
                _report.Error("Loads on shell elements other than mesh loads are not converted yet", id);
        }

        private void AddShell(string id, int a, int b, int c, double thickness, ImportMaterial material)
        {
            _out.Shells.Add(new ImportShell { Id = id, NodeA = a, NodeB = b, NodeC = c, Thickness = thickness, Material = material });
        }

        private static bool HasEccentricity(ModelElement element) =>
            element.hasEccent
            || IsNonZero(element.ecce_loc)
            || IsNonZero(element.ecce_glo)
            || (element.crosec != null && IsNonZero(element.crosec.ecce_loc));

        #endregion

        #region sections and materials

        private ImportBeamSection Section(CroSec_Beam crosec)
        {
            if (_sections.TryGetValue(crosec, out var cached))
                return cached;

            var material = Material(crosec.material, crosec.name);
            if (material == null)
                return null;

            var section = new ImportBeamSection
            {
                Name = string.IsNullOrWhiteSpace(crosec.name) ? crosec.GetType().Name : crosec.name,
                Material = material,
                Shape = ImportSectionShape.General,
                ShapeName = crosec.GetType().Name.Replace("CroSec_", string.Empty),
                A = crosec.A,
                Ay = crosec.Ay,
                Az = crosec.Az,
                Iyy = crosec.Iyy,
                Izz = crosec.Izz,
                Iyz = crosec.Iyz,
                J = crosec.Ipp,
            };

            // Most derived first: a trapezoid is a box, and a box and a T are I-sections.
            switch (crosec)
            {
                case CroSec_Trapezoid trapezoid:
                    if (Same(trapezoid.uf_width, trapezoid.lf_width))
                    {
                        section.Shape = ImportSectionShape.Rectangle;
                        section.Height = trapezoid._height;
                        section.Width = trapezoid.uf_width;
                    }
                    else
                        section.ShapeName = "trapezoid";
                    break;

                case CroSec_Box box:
                    if (Same(box.uf_width, box.lf_width))
                    {
                        section.Shape = ImportSectionShape.RectangularHollow;
                        section.Height = box._height;
                        section.Width = box.uf_width;
                        section.Web = box.w_thick;
                        section.TopFlange = box.uf_thick;
                        section.BottomFlange = box.lf_thick;
                        section.FilletRadius = box.fillet_r;
                    }
                    else
                        section.ShapeName = "box with unequal flanges";
                    break;

                case CroSec_T _:
                    section.ShapeName = "T-section";
                    break;

                case CroSec_I i:
                    section.Shape = ImportSectionShape.I;
                    section.Height = i._height;
                    section.TopWidth = i.uf_width;
                    section.TopFlange = i.uf_thick;
                    section.BottomWidth = i.lf_width;
                    section.BottomFlange = i.lf_thick;
                    section.Web = i.w_thick;
                    section.FilletRadius = i.fillet_r;
                    break;

                case CroSec_Circle circle:
                    double diameter = circle.getHeight();
                    section.Shape = ImportSectionShape.Circle;
                    section.Diameter = diameter;
                    section.WallThickness = circle.thick > 0.0 && circle.thick < 0.5 * diameter ? circle.thick : 0.0;
                    break;
            }

            _sections[crosec] = section;
            return section;
        }

        private ImportMaterial Material(FemMaterial femMaterial, string usedBy)
        {
            if (femMaterial == null)
            {
                _report.Error("These cross-sections have no material and are not converted", usedBy);
                return null;
            }

            if (_materials.TryGetValue(femMaterial, out var cached))
                return cached;

            if (femMaterial is FemMaterial_Orthotropic)
                _report.Warning("These orthotropic materials are converted as isotropic, from their first Young's modulus and in-plane shear modulus", femMaterial.name);
            else if (femMaterial is FemMaterial_NonLin1D)
                _report.Remark("These materials have a nonlinear stress-strain curve in Karamba3D; only their elastic modulus is carried over", femMaterial.name);

            double specificWeight = femMaterial.gamma();
            var material = new ImportMaterial
            {
                Name = femMaterial.name,
                E = femMaterial.E(0),
                G = femMaterial.G12(),
                Nu = femMaterial.nue12(),
                SpecificWeight = specificWeight,
                // kN/m3 over m/s2 is t/m3, which is 1000 kg/m3: the mass Karamba3D gives the material.
                Density = specificWeight / _gravity * 1000.0,
            };

            _materials[femMaterial] = material;
            return material;
        }

        #endregion

        #region supports

        private void ReadSupports()
        {
            foreach (var support in _model.supports)
            {
                string node = $"node {support.node_ind}";

                bool springs = support.SupportTypes != null && support.SupportTypes.Any(type => type == Karamba.Supports.Support.SupportType.Flexible);
                if (springs)
                {
                    _report.Error("Spring supports are not converted yet; these supports are left out", node);
                    continue;
                }

                if (support._displacement != null && support._displacement.Any(value => Math.Abs(value) > 0.0))
                    _report.Error("Prescribed support displacements are not converted yet: these supports hold their node, but do not move it", node);

                var condition = support.Condition;
                RG.Plane? orientation = null;
                if (support.hasLocalCoosys && support.local_coosys != null)
                {
                    var plane = support.local_coosys;
                    orientation = new RG.Plane(Point(plane.Origin), Vector(plane.XAxis), Vector(plane.YAxis));
                }

                _out.Supports.Add(new ImportSupport
                {
                    Node = support.node_ind,
                    Orientation = orientation,
                    Tx = condition[0],
                    Ty = condition[1],
                    Tz = condition[2],
                    Rx = condition[3],
                    Ry = condition[4],
                    Rz = condition[5],
                });
            }
        }

        #endregion

        #region loads

        private void ReadPointLoads()
        {
            foreach (var load in _model.ploads)
            {
                // Point loads Karamba3D generated from a mesh load are read from the mesh load itself.
                if (load.generated())
                    continue;

                if (load.local)
                    _report.Remark("Point loads set to local follow their node's rotation in a large-deformation analysis only. They are applied in " +
                                   "global directions, which is the same thing in a linear analysis", $"node {load.node_ind}");

                _out.PointLoads.Add(new ImportPointLoad
                {
                    Case = Case(load.LcName),
                    Node = load.node_ind,
                    Force = Vector(load.force),
                    Moment = Vector(load.moment),
                });
            }
        }

        /// <summary>
        /// Mesh loads as Karamba3D distributed them: nodal loads and, where it was asked to, uniform
        /// loads on beams. Both come from the mesh load rather than from the elements and nodes they
        /// were attached to, so that none is counted twice.
        /// </summary>
        private void ReadMeshLoads()
        {
            foreach (var meshLoad in _model.mloads)
            {
                foreach (var load in meshLoad.pointLoads() ?? new List<Mesh_PointLoad>())
                {
                    _out.PointLoads.Add(new ImportPointLoad
                    {
                        Case = Case(string.IsNullOrWhiteSpace(load.LcName) ? meshLoad.LcName : load.LcName),
                        Node = load.node_ind,
                        Force = Vector(load.force),
                        Moment = Vector(load.moment),
                    });
                }

                foreach (var load in meshLoad.elementLoads() ?? new List<ElementLoad>())
                {
                    string lcName = string.IsNullOrWhiteSpace(load.LcName) ? meshLoad.LcName : load.LcName;
                    foreach (int element in Targets(load))
                    {
                        if (!_beamOfElement.TryGetValue(element, out int beam))
                        {
                            _report.Error("Beam loads generated from mesh loads act on elements that are not converted and are left out", ElementLabel(element, _model.elems[element]));
                            continue;
                        }
                        AddLineLoad(load, lcName, beam, _axesOfElement[element], ElementLabel(element, _model.elems[element]));
                    }
                }
            }
        }

        private void ReadBeamLoads(int index, string id, ModelBeam beam, KG.Vector3[] axes)
        {
            foreach (var load in beam.Elem_loads ?? new List<ElementLoad>())
            {
                // Loads generated from a mesh load are read from the mesh load (see ReadMeshLoads).
                if (load.generated())
                    continue;

                AddLineLoad(load, load.LcName, _beamOfElement[index], axes, id);
            }
        }

        private void AddLineLoad(ElementLoad load, string lcName, int beam, KG.Vector3[] axes, string id)
        {
            string where = $"{id} in {lcName}";

            if (!(load is DistributedForce force))
            {
                _report.Error($"{Describe(load)} on beams are not converted yet", where);
                return;
            }

            if (!TryUniform(force, out double value))
            {
                _report.Error("Line loads that vary along the beam or cover only part of it are not converted yet", where);
                return;
            }

            _out.LineLoads.Add(new ImportLineLoad
            {
                Case = Case(lcName),
                Beam = beam,
                Force = GlobalLineLoad(force.Direction, force.LoadOrientation, value, axes),
            });
        }

        /// <summary>
        /// The global load per metre of beam. Karamba3D holds the direction as a unit vector and the
        /// size separately. A local direction is in the beam's own axes; a projected load is per metre
        /// of the beam's projection onto a plane square to the load, which is |x × d| of a metre of beam.
        /// </summary>
        private static RG.Vector3d GlobalLineLoad(KG.Vector3 direction, LoadOrientation orientation, double value, KG.Vector3[] axes)
        {
            var d = Vector(direction);
            switch (orientation)
            {
                case LoadOrientation.local:
                    return value * (d.X * Vector(axes[0]) + d.Y * Vector(axes[1]) + d.Z * Vector(axes[2]));
                case LoadOrientation.proj:
                    var x = Vector(axes[0]);
                    x.Unitize();
                    var unit = d;
                    unit.Unitize();
                    return value * RG.Vector3d.CrossProduct(x, unit).Length * d;
                default:
                    return value * d;
            }
        }

        /// <summary>
        /// Whether the polyline load has one value over the whole beam. Karamba3D closes the polyline
        /// with a jump back to zero at the end, positions (0, 1, 1) and values (q, q, 0), so segments of
        /// no length are passed over.
        /// </summary>
        private static bool TryUniform(DistributedLoad load, out double value)
        {
            value = 0.0;
            var positions = load.Positions;
            var values = load.Values;
            if (positions == null || values == null || positions.Count != values.Count || positions.Count < 2)
                return false;

            double? uniform = null;
            double covered = 0.0;
            for (int k = 0; k + 1 < positions.Count; k++)
            {
                double length = positions[k + 1] - positions[k];
                if (length <= 1e-9)
                    continue;

                if (!Same(values[k], values[k + 1]))
                    return false;
                if (uniform.HasValue && !Same(uniform.Value, values[k]))
                    return false;

                uniform = values[k];
                covered += length;
            }

            if (!uniform.HasValue || Math.Abs(covered - 1.0) > 1e-6 || Math.Abs(positions.Min()) > 1e-6)
                return false;

            value = uniform.Value;
            return true;
        }

        private IEnumerable<int> Targets(ElementLoad load)
        {
            var targets = new HashSet<int>();
            foreach (var guid in load.ElementGuids ?? new List<Guid>())
                foreach (int index in _model.ElementInds(guid))
                    targets.Add(index);

            if (targets.Count == 0 && load.ElementIds != null && load.ElementIds.Count > 0)
                foreach (var element in _model.Elements(load.ElementIds))
                    targets.Add(element.ind);

            return targets;
        }

        private void ReadGravity()
        {
            foreach (var gravity in _model.gravities)
                _out.Gravities.Add(new ImportGravity { Case = Case(gravity.Key), Factor = Vector(gravity.Value.force) });
        }

        private void ReadPointMasses()
        {
            foreach (var mass in _model.pmass)
            {
                var direction = mass.MassDirection;
                bool everyDirection = Same(direction.X, direction.Y) && Same(direction.Y, direction.Z);
                if (!everyDirection)
                    _report.Warning("Point masses that act in some directions only are converted as acting in all three", $"node {mass.node_ind}");

                _out.PointMasses.Add(new ImportPointMass { Node = mass.node_ind, Mass = _kg.toUnit(mass.Mass) });
            }
        }

        private void ReadWhatIsNotConverted()
        {
            if (_model.pointDisplacements != null && _model.pointDisplacements.Count > 0)
                _report.Error("Point displacements (prescribed node displacements) are not converted yet");

            if (_model.jointLines != null && _model.jointLines.Count > 0)
                _report.Error("Line joints between shells are not converted yet");

            if (_model.lcCombinations != null && _model.lcCombinations.Count > 0)
                _report.Warning($"{Karamba3D}'s load-case combinations are not converted yet: there is one Alpaca model per load case, and combining them is up to you.");
        }

        #endregion

        #region helpers

        /// <summary>
        /// The load case a load belongs to, or null for one that acts in every case. Karamba3D 2.x took
        /// a missing name or a negative number for "every case"; a name the model lists as a load case
        /// is always that case.
        /// </summary>
        private string Case(string lcName)
        {
            if (lcName != null && _knownCases.Contains(lcName))
                return lcName;

            if (string.IsNullOrWhiteSpace(lcName) || (int.TryParse(lcName, out int number) && number < 0))
                return null;

            _knownCases.Add(lcName);
            _out.LoadCases.Add(lcName);
            return lcName;
        }

        private static string Describe(ElementLoad load)
        {
            switch (load)
            {
                case ConcentratedForce _: return "Concentrated forces";
                case ConcentratedMoment _: return "Concentrated moments";
                case DistributedMoment _: return "Distributed moments";
                case TemperatureLoad _: return "Temperature loads";
                case StrainLoad _: return "Initial strain loads";
                case Imperfection _: return "Imperfections";
                case TranslationalGap _:
                case RotationalGap _: return "Gaps";
                default: return $"{load.GetType().Name} loads";
            }
        }

        private static string ElementLabel(int index, ModelElement element) =>
            string.IsNullOrWhiteSpace(element.id) ? $"element {index}" : element.id;

        private static bool Same(double a, double b) => Math.Abs(a - b) <= 1e-9 * Math.Max(1.0, Math.Max(Math.Abs(a), Math.Abs(b)));

        private static bool IsNonZero(KG.Vector3 vector) => Math.Abs(vector.X) + Math.Abs(vector.Y) + Math.Abs(vector.Z) > 1e-12;

        private static RG.Point3d Point(KG.Point3 point) => new RG.Point3d(point.X, point.Y, point.Z);

        private static RG.Vector3d Vector(KG.Vector3 vector) => new RG.Vector3d(vector.X, vector.Y, vector.Z);

        #endregion
    }
}
