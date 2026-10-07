using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Rhino.Geometry;
using Alpaca4d.Element;
using Alpaca4d.Generic;
using Alpaca4d.Loads;
using Alpaca4d.Material;
using Alpaca4d.Section;

namespace Alpaca4d.Interop
{
    /// <summary>The pieces of an imported model, for Assemble Model to put together with the user's own.</summary>
    public class ImportParts
    {
        public List<IElement> Elements { get; } = new List<IElement>();
        public List<Support> Supports { get; } = new List<Support>();
        public List<MassLoad> Masses { get; } = new List<MassLoad>();

        /// <summary>One per load case, all acting on <see cref="Elements"/>.</summary>
        public List<LoadPattern> LoadPatterns { get; } = new List<LoadPattern>();

        /// <summary>The load case of each pattern, in the same order.</summary>
        public List<string> LoadCases { get; } = new List<string>();

        /// <summary>A node tolerance that keeps the source's nodes apart, for Assemble Model's Tolerance.</summary>
        public double Tolerance { get; set; } = ImportModelBuilder.MaxNodeTolerance;

        public ConversionReport Report { get; set; }
    }

    /// <summary>
    /// Turns an <see cref="ImportModel"/> into the parts of an Alpaca model - elements, supports,
    /// masses and one load pattern per load case - for the user to assemble with their own.
    ///
    /// The load cases stay apart, one pattern each, because an Alpaca model is one OpenSees deck solved
    /// once, with every load pattern in it acting at the same time: which of them act together is the
    /// user's choice, made at Assemble Model.
    /// </summary>
    public static class ImportModelBuilder
    {
        /// <summary>How far an Alpaca shape's area and second moments may drift from the source's before a general section is used instead.</summary>
        public const double PropertyTolerance = 0.01;

        /// <summary>The same for the torsion constant, whose formulas differ more between programs.</summary>
        public const double TorsionTolerance = 0.05;

        /// <summary>Upper bound on the node tolerance handed to the Alpaca model [m].</summary>
        public const double MaxNodeTolerance = 0.001;

        /// <summary>
        /// The parts of the source: one set of elements, supports and masses, and one load pattern per
        /// load case acting on those same elements. Nothing is assembled; Assemble Model does that, with
        /// whatever the user adds.
        /// </summary>
        public static ImportParts BuildParts(ImportModel source, IList<string> requestedCases = null, bool allowPartial = false)
        {
            var report = source.Report;
            var parts = new ImportParts { Report = report };

            var cases = SelectCases(source, requestedCases, report);
            var plan = MakePlan(source, report);

            if (plan.ElementCount == 0)
            {
                report.Error($"Nothing in {source.Source} could be built as an Alpaca element.");
                return parts;
            }

            if (report.HasErrors && !allowPartial)
                return parts;

            var structure = BuildStructure(source, plan);
            parts.Elements.AddRange(structure.Elements);
            parts.Supports.AddRange(structure.Supports);
            parts.Masses.AddRange(structure.Masses);
            parts.Tolerance = plan.Tolerance;

            // A pattern for every case, empty ones included, so that the patterns and their names
            // line up with the load cases the user asked for.
            foreach (var loadCase in cases)
            {
                parts.LoadPatterns.Add(BuildPattern(source, plan, loadCase, structure.Beams, report));
                parts.LoadCases.Add(loadCase);
            }

            return parts;
        }

        #region load cases

        /// <summary>The load cases to build patterns for: those asked for, or every one.</summary>
        private static List<string> SelectCases(ImportModel source, IList<string> requested, ConversionReport report)
        {
            var wanted = (requested ?? new List<string>())
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name.Trim())
                .Distinct()
                .ToList();

            if (wanted.Count == 0)
                return new List<string>(source.LoadCases);

            var known = new HashSet<string>(source.LoadCases);
            foreach (var name in wanted.Where(name => !known.Contains(name)))
            {
                string available = source.LoadCases.Count > 0 ? string.Join(", ", source.LoadCases) : "none";
                report.Error($"These load cases are not in {source.Source}, whose load cases are: {available}", name);
            }

            return wanted.Where(known.Contains).ToList();
        }

        private static bool ActsIn(ImportLoad load, string loadCase) => load.Case == null || load.Case == loadCase;

        #endregion

        #region plan

        /// <summary>What can be built, decided once for every load case.</summary>
        private sealed class Plan
        {
            public Dictionary<ImportBeamSection, ImportSectionShape?> SectionShapes { get; } = new Dictionary<ImportBeamSection, ImportSectionShape?>();
            public bool[] BeamOk { get; set; }
            public bool[] ShellOk { get; set; }
            public bool[] SupportOk { get; set; }
            public HashSet<int> UsedNodes { get; } = new HashSet<int>();
            public double Tolerance { get; set; } = MaxNodeTolerance;
            public int ElementCount => BeamOk.Count(ok => ok) + ShellOk.Count(ok => ok);
        }

        private static Plan MakePlan(ImportModel source, ConversionReport report)
        {
            var plan = new Plan
            {
                BeamOk = new bool[source.Beams.Count],
                ShellOk = new bool[source.Shells.Count],
                SupportOk = new bool[source.Supports.Count],
            };

            for (int i = 0; i < source.Beams.Count; i++)
                plan.BeamOk[i] = PlanBeam(source, source.Beams[i], plan, report);

            if (source.Beams.Any())
                report.Remark("Beam axes follow Alpaca's convention, with local y along the depth of the section: Alpaca's y is the source's z " +
                              "and Alpaca's z is minus the source's y. So the source's Vz and My are Alpaca's Vy and Mz.");

            var checkedShellMaterials = new HashSet<ImportMaterial>();
            for (int i = 0; i < source.Shells.Count; i++)
                plan.ShellOk[i] = PlanShell(source, source.Shells[i], checkedShellMaterials, report);

            for (int i = 0; i < source.Beams.Count; i++)
            {
                if (!plan.BeamOk[i]) continue;
                plan.UsedNodes.Add(source.Beams[i].NodeI);
                plan.UsedNodes.Add(source.Beams[i].NodeJ);
            }
            for (int i = 0; i < source.Shells.Count; i++)
            {
                if (!plan.ShellOk[i]) continue;
                plan.UsedNodes.Add(source.Shells[i].NodeA);
                plan.UsedNodes.Add(source.Shells[i].NodeB);
                plan.UsedNodes.Add(source.Shells[i].NodeC);
            }

            for (int i = 0; i < source.Supports.Count; i++)
                plan.SupportOk[i] = PlanSupport(source.Supports[i], plan, report);

            foreach (var mass in source.PointMasses.Where(mass => !plan.UsedNodes.Contains(mass.Node)))
                report.Error("These point masses sit on nodes that no converted element reaches and are left out: Alpaca makes nodes only where elements meet", NodeLabel(mass.Node));

            foreach (var load in source.PointLoads.Where(load => !plan.UsedNodes.Contains(load.Node)))
                report.Error("These point loads act on nodes that no converted element reaches and are left out: Alpaca makes nodes only where elements meet", $"{NodeLabel(load.Node)} in {CaseLabel(load.Case)}");

            foreach (var load in source.LineLoads.Where(load => load.Beam < 0 || load.Beam >= plan.BeamOk.Length || !plan.BeamOk[load.Beam]))
                report.Error("These line loads act on beams that could not be converted and are left out", $"{BeamLabel(source, load.Beam)} in {CaseLabel(load.Case)}");

            plan.Tolerance = NodeTolerance(source, plan, report);
            return plan;
        }

        private static bool PlanBeam(ImportModel source, ImportBeam beam, Plan plan, ConversionReport report)
        {
            string label = beam.Id ?? "(no id)";

            if (!ValidNode(source, beam.NodeI) || !ValidNode(source, beam.NodeJ) || beam.NodeI == beam.NodeJ)
            {
                report.Error("These beams do not join two distinct nodes and are left out", label);
                return false;
            }

            var axis = source.Nodes[beam.NodeJ] - source.Nodes[beam.NodeI];
            if (!(axis.Length > 1e-9))
            {
                report.Error("These beams have no length and are left out", label);
                return false;
            }

            if (!(AlpacaLocalZ(axis, beam.LocalZ).Length > 1e-6))
            {
                report.Error("These beams have a local z axis along the beam itself, which orients nothing, and are left out", label);
                return false;
            }

            if (beam.Section == null)
            {
                report.Error("These beams have no cross-section and are left out", label);
                return false;
            }

            if (!plan.SectionShapes.TryGetValue(beam.Section, out var shape))
            {
                shape = PlanSection(beam.Section, report);
                plan.SectionShapes[beam.Section] = shape;
            }

            if (shape == null)
            {
                report.Error("These beams have a cross-section Alpaca cannot build and are left out", label);
                return false;
            }

            return true;
        }

        /// <summary>
        /// The Alpaca shape to build a beam section as, or null when it cannot be built at all.
        ///
        /// A shape Alpaca knows is only kept when Alpaca's own properties for it - computed from the
        /// dimensions, without the source's fillets or tables - agree with the source's. Otherwise
        /// the section goes in as a general elastic section with the source's numbers, so the beam is
        /// exactly as stiff as in the source, and the difference is reported.
        /// </summary>
        public static ImportSectionShape? PlanSection(ImportBeamSection section, ConversionReport report)
        {
            string label = SectionLabel(section);

            if (section.Material == null || !(section.Material.E > 0.0) || !(section.Material.G > 0.0))
            {
                report.Error("These beam sections have a material without a positive E and G and cannot be built", label);
                return null;
            }

            if (!(section.A > 0.0) || !(section.Iyy > 0.0) || !(section.Izz > 0.0) || !(section.J > 0.0))
            {
                report.Error("These beam sections have a zero, negative or missing area, second moment of area or torsion constant and cannot be built", label);
                return null;
            }

            if (Math.Abs(section.Iyz) > 1e-3 * Math.Sqrt(section.Iyy * section.Izz))
            {
                report.Error("These beam sections have principal axes turned away from the local axes (Iyz is not zero, as in an angle). " +
                             "Alpaca's elastic section has no product of inertia, so they cannot be built yet", label);
                return null;
            }

            if (!(section.Ay > 0.0) || !(section.Az > 0.0))
                report.Remark("These beam sections have no shear area, so their beams take no shear deformation, as in the source", label);

            if (section.Shape == ImportSectionShape.General)
            {
                report.Warning("These beam sections have no Alpaca shape, so a general elastic section with the source's properties is used. " +
                               "The beams are as stiff and as heavy as in the source, but Alpaca cannot draw the section or run the steel check on it",
                               $"{label} ({section.ShapeName ?? "unknown shape"})");
                return ImportSectionShape.General;
            }

            IUniaxialSection candidate;
            List<string> differences;
            try
            {
                candidate = CreateSection(section, section.Shape, ProbeMaterial);
                differences = Differences(section, candidate);
            }
            catch (Exception ex)
            {
                report.Warning($"Alpaca could not build these sections as its own {ShapeLabel(section.Shape)}, so a general elastic section with the source's properties is used",
                               $"{label}: {ex.Message}");
                return ImportSectionShape.General;
            }

            if (differences.Count == 0)
                return section.Shape;

            string cause = section.FilletRadius > 0.0
                ? $"; Alpaca's shape has no root radius, the source's is {section.FilletRadius * 1000.0:0.#} mm"
                : string.Empty;

            report.Warning($"These sections differ from Alpaca's own shape of the same dimensions by more than {PropertyTolerance * 100:0}% " +
                           $"({TorsionTolerance * 100:0}% for J), so a general elastic section with the source's properties is used: the beams are as stiff " +
                           "as in the source, but Alpaca cannot draw the section or run the steel check on it",
                           $"{label}: {string.Join(", ", differences)}{cause}");
            return ImportSectionShape.General;
        }

        /// <summary>A material only used to build a section and read its properties back.</summary>
        private static UniaxialMaterialElastic ProbeMaterial => new UniaxialMaterialElastic("probe", 1.0, 1.0, 0.0, 1.0, 0.0);

        /// <summary>
        /// Where the candidate's properties stray from the source's, in the source's axes. Alpaca's
        /// Izz is about its z, which is minus the source's y, so it is compared with the source's Iyy.
        /// </summary>
        private static List<string> Differences(ImportBeamSection source, IUniaxialSection candidate)
        {
            var differences = new List<string>();
            Compare("A", candidate.Area, source.A, PropertyTolerance, differences);
            Compare("Iyy", candidate.Izz, source.Iyy, PropertyTolerance, differences);
            Compare("Izz", candidate.Iyy, source.Izz, PropertyTolerance, differences);
            Compare("J", candidate.J, source.J, TorsionTolerance, differences);
            return differences;
        }

        private static void Compare(string name, double alpaca, double source, double tolerance, List<string> differences)
        {
            double relative = (alpaca - source) / source;
            if (double.IsNaN(relative) || Math.Abs(relative) > tolerance)
                differences.Add(string.Format(CultureInfo.InvariantCulture, "{0} {1:+0.#%;-0.#%}", name, relative));
        }

        private static bool PlanShell(ImportModel source, ImportShell shell, HashSet<ImportMaterial> checkedMaterials, ConversionReport report)
        {
            string label = shell.Id ?? "(no id)";
            var nodes = new[] { shell.NodeA, shell.NodeB, shell.NodeC };

            if (nodes.Any(node => !ValidNode(source, node)) || nodes.Distinct().Count() != 3)
            {
                report.Error("These shell elements have a face that does not join three distinct nodes; the face is left out", label);
                return false;
            }

            if (!(TriangleArea(source, shell) > 1e-12))
            {
                report.Error("These shell elements have a face with no area; the face is left out", label);
                return false;
            }

            if (!(shell.Thickness > 0.0))
            {
                report.Error("These shell elements have a face without a positive thickness; the face is left out", label);
                return false;
            }

            var material = shell.Material;
            if (material == null || !(material.E > 0.0) || !(material.Nu > -1.0) || !(material.Nu < 0.5))
            {
                report.Error("These shell elements have a material without a positive E and a Poisson's ratio between -1 and 0.5; they are left out", label);
                return false;
            }

            if (checkedMaterials.Add(material) && material.G > 0.0)
            {
                double implied = material.E / (2.0 * (1.0 + material.Nu));
                if (Math.Abs(implied - material.G) > 0.01 * material.G)
                    report.Warning("Alpaca's shell material takes E and Poisson's ratio only, so its shear modulus is E / (2 (1 + nu)) rather than the source's G",
                                   $"{material.Name}: G {material.G:G4} kN/m2 in the source, {implied:G4} kN/m2 in Alpaca");
            }

            return true;
        }

        private static bool PlanSupport(ImportSupport support, Plan plan, ConversionReport report)
        {
            if (!(support.Tx || support.Ty || support.Tz || support.Rx || support.Ry || support.Rz))
            {
                report.Remark("These supports hold nothing and are left out", NodeLabel(support.Node));
                return false;
            }

            if (!plan.UsedNodes.Contains(support.Node))
            {
                report.Error("These supports sit on nodes that no converted element reaches and are left out: Alpaca makes nodes only where elements meet", NodeLabel(support.Node));
                return false;
            }

            if (support.Orientation is Plane plane && !IsAxisAligned(plane))
                report.Warning("These supports have turned axes. Alpaca holds a turned support through a very stiff spring on an extra node rather than " +
                               "a rigid constraint, so the support yields a very little", NodeLabel(support.Node));

            return true;
        }

        /// <summary>
        /// A node tolerance small enough that no two of the source's nodes fall within it of each
        /// other: the source has already decided which nodes are one, and Alpaca must not merge more.
        /// </summary>
        private static double NodeTolerance(ImportModel source, Plan plan, ConversionReport report)
        {
            var points = plan.UsedNodes.OrderBy(node => node).Select(node => source.Nodes[node]).ToList();
            var (distance, i, j) = ClosestPair(points);

            if (distance < 1e-6)
            {
                var used = plan.UsedNodes.OrderBy(node => node).ToList();
                report.Error("Two nodes of the source lie on top of each other. Alpaca keeps one node per position, so the elements on them would be joined there",
                             $"{NodeLabel(used[i])} and {NodeLabel(used[j])}");
                return MaxNodeTolerance;
            }

            return Math.Min(MaxNodeTolerance, 0.25 * distance);
        }

        /// <summary>The smallest distance between two of the points and which two, or infinity for fewer than two.</summary>
        public static (double distance, int i, int j) ClosestPair(IList<Point3d> points)
        {
            var order = Enumerable.Range(0, points.Count).OrderBy(index => points[index].X).ToList();
            double best = double.PositiveInfinity;
            int bestI = -1, bestJ = -1;

            for (int a = 0; a < order.Count; a++)
            {
                var p = points[order[a]];
                for (int b = a + 1; b < order.Count; b++)
                {
                    var q = points[order[b]];
                    if (q.X - p.X >= best)
                        break;

                    double d = p.DistanceTo(q);
                    if (d < best)
                    {
                        best = d;
                        bestI = Math.Min(order[a], order[b]);
                        bestJ = Math.Max(order[a], order[b]);
                    }
                }
            }

            return (best, bestI, bestJ);
        }

        #endregion

        #region build

        /// <summary>The elements, supports and masses, with the beams also by their index in the source, for the line loads to find.</summary>
        private sealed class Structure
        {
            public List<IElement> Elements { get; } = new List<IElement>();
            public IBeam[] Beams { get; set; }
            public List<Support> Supports { get; } = new List<Support>();
            public List<MassLoad> Masses { get; } = new List<MassLoad>();
        }

        private static Structure BuildStructure(ImportModel source, Plan plan)
        {
            var beamMaterials = new Dictionary<ImportMaterial, UniaxialMaterialElastic>();
            var shellMaterials = new Dictionary<ImportMaterial, ElasticIsotropicMaterial>();
            var beamSections = new Dictionary<ImportBeamSection, IUniaxialSection>();
            var shellSections = new Dictionary<(ImportMaterial, double), PlateFiberSection>();

            var elements = new List<IElement>();
            var beams = new IBeam[source.Beams.Count];

            for (int i = 0; i < source.Beams.Count; i++)
            {
                if (!plan.BeamOk[i]) continue;

                var item = source.Beams[i];
                var start = source.Nodes[item.NodeI];
                var end = source.Nodes[item.NodeJ];

                if (!beamSections.TryGetValue(item.Section, out var section))
                {
                    var material = BeamMaterial(item.Section.Material, beamMaterials);
                    section = CreateSection(item.Section, plan.SectionShapes[item.Section].Value, material);
                    beamSections[item.Section] = section;
                }

                var curve = new LineCurve(start, end);
                var geomTransf = new GeomTransf(GeomTransfType.Linear, curve, AlpacaLocalZ(end - start, item.LocalZ));
                var beam = new ForceBeamColumn(curve, section, geomTransf) { ElementId = item.Id };

                beams[i] = beam;
                elements.Add(beam);
            }

            for (int i = 0; i < source.Shells.Count; i++)
            {
                if (!plan.ShellOk[i]) continue;

                var item = source.Shells[i];
                var key = (item.Material, Math.Round(item.Thickness, 9));
                if (!shellSections.TryGetValue(key, out var section))
                {
                    var material = ShellMaterial(item.Material, shellMaterials);
                    string name = string.Format(CultureInfo.InvariantCulture, "{0} t={1:0.###} mm", item.Material.Name, item.Thickness * 1000.0);
                    section = new PlateFiberSection(name, item.Thickness, material);
                    shellSections[key] = section;
                }

                var mesh = new Mesh();
                mesh.Vertices.Add(source.Nodes[item.NodeA]);
                mesh.Vertices.Add(source.Nodes[item.NodeB]);
                mesh.Vertices.Add(source.Nodes[item.NodeC]);
                mesh.Faces.AddFace(0, 1, 2);

                elements.Add(new ASDShellT3(mesh, section) { ElementId = item.Id });
            }

            var supports = new List<Support>();
            for (int i = 0; i < source.Supports.Count; i++)
            {
                if (!plan.SupportOk[i]) continue;

                var item = source.Supports[i];
                var position = source.Nodes[item.Node];
                supports.Add(item.Orientation is Plane plane
                    ? new Support(new Plane(position, plane.XAxis, plane.YAxis), item.Tx, item.Ty, item.Tz, item.Rx, item.Ry, item.Rz)
                    : new Support(position, item.Tx, item.Ty, item.Tz, item.Rx, item.Ry, item.Rz));
            }

            var masses = source.PointMasses
                .Where(mass => plan.UsedNodes.Contains(mass.Node))
                .Select(mass => new MassLoad(source.Nodes[mass.Node], new Vector3d(mass.Mass, mass.Mass, mass.Mass), Vector3d.Zero))
                .ToList();

            var structure = new Structure { Beams = beams };
            structure.Elements.AddRange(elements);
            structure.Supports.AddRange(supports);
            structure.Masses.AddRange(masses);
            return structure;
        }

        /// <summary>One load case as a plain pattern with a constant time series: the loads at full size throughout.</summary>
        private static LoadPattern BuildPattern(ImportModel source, Plan plan, string loadCase, IBeam[] beams, ConversionReport report)
        {
            var loads = CaseLoads(source, plan, loadCase, beams, out var total);

            report.Remark("Total applied load per load case, to compare with the sum of the source's reactions",
                          string.Format(CultureInfo.InvariantCulture, "{0}: ({1:0.###}, {2:0.###}, {3:0.###}) kN", loadCase, total.X, total.Y, total.Z));

            return new LoadPattern(PatternType.Plain, TimeSeries.Constant.Default(), loads, 1.0) { Name = loadCase };
        }

        private static List<ILoad> CaseLoads(ImportModel source, Plan plan, string loadCase, IBeam[] beams, out Vector3d total)
        {
            var timeSeries = TimeSeries.Constant.Default();
            var loads = new List<ILoad>();
            total = Vector3d.Zero;

            foreach (var load in source.PointLoads.Where(load => ActsIn(load, loadCase) && plan.UsedNodes.Contains(load.Node)))
            {
                loads.Add(new Loads.PointLoad(source.Nodes[load.Node], load.Force, load.Moment, timeSeries));
                total += load.Force;
            }

            foreach (var load in source.LineLoads.Where(load => ActsIn(load, loadCase)))
            {
                if (load.Beam < 0 || load.Beam >= beams.Length || beams[load.Beam] == null)
                    continue;

                // LineLoad takes a global vector and works out the local components from the beam's axes.
                loads.Add(new LineLoad(beams[load.Beam], load.Force, timeSeries));
                total += load.Force * BeamLength(source, load.Beam);
            }

            foreach (var gravity in source.Gravities.Where(gravity => ActsIn(gravity, loadCase)))
            {
                // Self-weight as loads in its own right, from the specific weight: a beam's as a line
                // load, so that it bends the span between nodes, and a shell face's as a third of its
                // weight at each corner. Alpaca's own Gravity load is not used, as it pulls along -Z
                // only, puts a beam's weight at its two ends and takes g as 9.81.
                for (int i = 0; i < source.Beams.Count; i++)
                {
                    if (beams[i] == null) continue;

                    var section = source.Beams[i].Section;
                    var weight = gravity.Factor * (section.Material.SpecificWeight * section.A);
                    if (weight.IsZero) continue;

                    loads.Add(new LineLoad(beams[i], weight, timeSeries));
                    total += weight * BeamLength(source, i);
                }

                for (int i = 0; i < source.Shells.Count; i++)
                {
                    if (!plan.ShellOk[i]) continue;

                    var shell = source.Shells[i];
                    var share = gravity.Factor * (shell.Material.SpecificWeight * shell.Thickness * TriangleArea(source, shell) / 3.0);
                    if (share.IsZero) continue;

                    foreach (var node in new[] { shell.NodeA, shell.NodeB, shell.NodeC })
                    {
                        loads.Add(new Loads.PointLoad(source.Nodes[node], share, Vector3d.Zero, timeSeries));
                        total += share;
                    }
                }
            }

            return loads;
        }

        /// <summary>
        /// The Alpaca section for a source section. Alpaca's axes are the source's turned about the
        /// beam axis so that Alpaca's y runs along the depth: Alpaca's Izz, about its z, is the
        /// source's Iyy, and its shear factor along y belongs to the source's shear area along z.
        /// </summary>
        public static IUniaxialSection CreateSection(ImportBeamSection section, ImportSectionShape shape, UniaxialMaterialElastic material)
        {
            switch (shape)
            {
                case ImportSectionShape.Rectangle:
                    return new RectangleCS(section.Name, section.Width, section.Height, material);
                case ImportSectionShape.RectangularHollow:
                    return new RectangleHollowCS(section.Name, section.Width, section.Height, section.Web, section.TopFlange, section.BottomFlange, material);
                case ImportSectionShape.Circle:
                    return new CircleCS(section.Name, section.Diameter, section.WallThickness, material);
                case ImportSectionShape.I:
                    return new Alpaca4d.Section.ISection(section.Name, section.Height, section.TopWidth, section.TopFlange,
                                                         section.BottomWidth, section.BottomFlange, section.Web, material);
                default:
                    double alphaY = section.Az > 0.0 ? section.Az / section.A : 0.0;
                    double alphaZ = section.Ay > 0.0 ? section.Ay / section.A : 0.0;
                    return new ElasticSection(section.Name, section.A, section.Iyy, section.Izz, section.J, alphaY, alphaZ, material);
            }
        }

        private static UniaxialMaterialElastic BeamMaterial(ImportMaterial material, Dictionary<ImportMaterial, UniaxialMaterialElastic> cache)
        {
            if (!cache.TryGetValue(material, out var built))
            {
                built = new UniaxialMaterialElastic(material.Name, material.E, material.E, 0.0, material.G, material.Nu, material.Density);
                cache[material] = built;
            }
            return built;
        }

        private static ElasticIsotropicMaterial ShellMaterial(ImportMaterial material, Dictionary<ImportMaterial, ElasticIsotropicMaterial> cache)
        {
            if (!cache.TryGetValue(material, out var built))
            {
                double g = material.E / (2.0 * (1.0 + material.Nu));
                built = new ElasticIsotropicMaterial(material.Name, material.E, g, material.Nu, material.Density);
                cache[material] = built;
            }
            return built;
        }

        #endregion

        #region helpers

        /// <summary>
        /// Alpaca's local z for a beam along <paramref name="axis"/> whose source local z - the depth
        /// of its section - is <paramref name="sourceZ"/>: x × z, which is minus the source's y, so
        /// that Alpaca's y = z × x comes out along the source's z.
        /// </summary>
        public static Vector3d AlpacaLocalZ(Vector3d axis, Vector3d sourceZ)
        {
            var x = Unit(axis);
            if (x.IsZero)
                return Vector3d.Zero;

            return Unit(Vector3d.CrossProduct(x, sourceZ));
        }

        /// <summary>The vector at unit length, or zero for one too short to have a direction. Plain arithmetic, unlike Vector3d.Unitize, which needs Rhino's native library.</summary>
        private static Vector3d Unit(Vector3d vector)
        {
            double length = Math.Sqrt(vector.X * vector.X + vector.Y * vector.Y + vector.Z * vector.Z);
            return length > 1e-12 ? vector / length : Vector3d.Zero;
        }

        private static bool IsAxisAligned(Plane plane)
        {
            const double tolerance = 1e-9;
            return plane.XAxis.EpsilonEquals(Vector3d.XAxis, tolerance)
                && plane.YAxis.EpsilonEquals(Vector3d.YAxis, tolerance)
                && plane.ZAxis.EpsilonEquals(Vector3d.ZAxis, tolerance);
        }

        private static bool ValidNode(ImportModel source, int node) => node >= 0 && node < source.Nodes.Count;

        private static double BeamLength(ImportModel source, int beam) =>
            source.Nodes[source.Beams[beam].NodeI].DistanceTo(source.Nodes[source.Beams[beam].NodeJ]);

        private static double TriangleArea(ImportModel source, ImportShell shell)
        {
            var a = source.Nodes[shell.NodeA];
            var b = source.Nodes[shell.NodeB];
            var c = source.Nodes[shell.NodeC];
            return 0.5 * Vector3d.CrossProduct(b - a, c - a).Length;
        }

        private static string SectionLabel(ImportBeamSection section) => string.IsNullOrWhiteSpace(section.Name) ? "(unnamed section)" : section.Name;

        private static string ShapeLabel(ImportSectionShape shape)
        {
            switch (shape)
            {
                case ImportSectionShape.Rectangle: return "rectangle";
                case ImportSectionShape.RectangularHollow: return "rectangular hollow section";
                case ImportSectionShape.Circle: return "circle";
                case ImportSectionShape.I: return "I-section";
                default: return "section";
            }
        }

        private static string NodeLabel(int node) => $"node {node}";

        private static string BeamLabel(ImportModel source, int beam) =>
            beam >= 0 && beam < source.Beams.Count ? source.Beams[beam].Id ?? $"beam {beam}" : $"beam {beam}";

        private static string CaseLabel(string loadCase) => loadCase == null ? "every load case" : $"load case {loadCase}";

        #endregion
    }
}
