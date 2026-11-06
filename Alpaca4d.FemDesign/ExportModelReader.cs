using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

using Rhino.Geometry;

using Alpaca4d.BeamIntegration;
using Alpaca4d.Constraints;
using Alpaca4d.Element;
using Alpaca4d.Generic;
using Alpaca4d.Interop;
using Alpaca4d.Loads;
using Sections = Alpaca4d.Section;
using Materials = Alpaca4d.Material;

namespace Alpaca4d.FemDesign
{
    /// <summary>
    /// Reads an assembled Alpaca <see cref="Model"/> into an <see cref="ExportModel"/>. This is the only
    /// part of the export that touches Rhino curves and meshes; it copies, and leaves every decision about
    /// FEM-Design to <see cref="StruxmlWriter"/>. What has no place in an export model at all - solids, a
    /// dynamic excitation - is reported here.
    /// </summary>
    public static class ExportModelReader
    {
        public static ExportModel Read(Model model, ConversionReport report)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            if (report == null) throw new ArgumentNullException(nameof(report));

            var export = new ExportModel { Tolerance = model.Tollerance > 0 ? model.Tollerance : 0.01 };
            var materials = new Dictionary<object, int>(ReferenceComparer.Instance);
            var beamSections = new Dictionary<object, int>(ReferenceComparer.Instance);
            var shellSections = new Dictionary<object, int>(ReferenceComparer.Instance);
            var beams = new Dictionary<IBeam, int>(ReferenceComparer.Instance);
            var shells = new Dictionary<IShell, int>(ReferenceComparer.Instance);

            foreach (var element in model.Elements ?? new List<IElement>())
            {
                try
                {
                    switch (element)
                    {
                        case IBeam beam:
                            var exportBeam = ReadBeam(beam, export, materials, beamSections, report);
                            beams[beam] = export.Beams.Count;
                            export.Beams.Add(exportBeam);
                            break;

                        case IShell shell:
                            var exportShell = ReadShell(shell, export, materials, shellSections, report);
                            shells[shell] = export.Shells.Count;
                            export.Shells.Add(exportShell);
                            break;

                        case Link link:
                            var exportLink = ReadLink(link, report);
                            if (exportLink != null)
                                export.Links.Add(exportLink);
                            break;

                        case IBrick _:
                            report.Error("Solid elements have no FEM-Design counterpart here and are not exported.", Label(element));
                            break;

                        default:
                            report.Error($"{element.GetType().Name} elements are not exported.", Label(element));
                            break;
                    }
                }
                catch (InvalidOperationException e)
                {
                    report.Error($"An element that cannot be read is not exported: {e.Message}", Label(element));
                }
            }

            foreach (var support in model.Supports ?? new List<Support>())
            {
                var plane = support.Plane;
                export.Supports.Add(new ExportSupport
                {
                    Position = support.Pos,
                    XAxis = plane.XAxis,
                    YAxis = plane.YAxis,
                    Tx = support.Tx, Ty = support.Ty, Tz = support.Tz,
                    Rx = support.Rx, Ry = support.Ry, Rz = support.Rz,
                    Stiffness = support.IsSpring
                        ? Enumerable.Range(1, 6).Select(direction => support.SpringAlong(direction) is SupportSpring spring
                            ? spring.Stiffness ?? double.PositiveInfinity : 0.0).ToArray()
                        : null,
                    Description = support.Description,
                });

                if (support.IsOneWay)
                    report.Warning("A compression-only spring support is exported as a spring that holds both ways, and without its gap: " +
                                   "set it to compression only in FEM-Design.", support.Description.Replace(Environment.NewLine, ", "));
            }

            foreach (var constraint in model.Constraint ?? new List<IConstraint>())
            {
                switch (constraint)
                {
                    case RigidLink rigidLink: export.RigidLinks.Add(rigidLink); break;
                    case EqualDOF equalDof: export.EqualDofs.Add(equalDof); break;
                    case RigidDiaphragm diaphragm: export.RigidDiaphragms.Add(diaphragm); break;
                    default: report.Error($"{constraint.GetType().Name} constraints are not exported."); break;
                }
            }

            int patternNumber = 0;
            foreach (var pattern in model.LoadPatterns ?? new List<LoadPattern>())
            {
                patternNumber++;
                var loadCase = ReadLoadPattern(pattern, patternNumber, beams, shells, report);
                if (loadCase != null)
                    export.LoadCases.Add(loadCase);
            }

            export.Masses.AddRange(model.Mass ?? new List<MassLoad>());
            return export;
        }

        private static ExportBeam ReadBeam(IBeam beam, ExportModel export, Dictionary<object, int> materials,
            Dictionary<object, int> sections, ConversionReport report)
        {
            if (beam.Section == null)
                throw new InvalidOperationException("the beam has no section.");
            var curve = beam.Curve;
            var exportBeam = new ExportBeam
            {
                Id = (beam as IElement)?.Id ?? 0,
                Label = (beam as IElement)?.ElementId,
                Start = curve.PointAtStart,
                End = curve.PointAtEnd,
                LocalZ = beam.GeomTransf?.LocalZ ?? Vector3d.Zero,
                Section = SectionIndex(beam.Section, export, materials, sections),
                IsStraight = curve.IsLinear(),
            };

            if (beam is BeamWithHinges && beam.BeamIntegration is HingeRadauIntegration hinges)
            {
                exportBeam.ReleaseI = BeamWithHinges.ReleaseOf(hinges.SectionI, beam.Section);
                exportBeam.ReleaseJ = BeamWithHinges.ReleaseOf(hinges.SectionJ, beam.Section);
            }

            if (beam is Truss truss)
            {
                // A bar: free to turn at both ends, its twist held at one so that it does not spin.
                exportBeam.IsTruss = true;
                exportBeam.ReleaseI = new Release(true, true, true, true, false, false);
                exportBeam.ReleaseJ = new Release(true, true, true, false, false, false);

                if (truss.WorksIn != AxialBehaviour.Both)
                    report.Warning($"A {(truss.WorksIn == AxialBehaviour.Tension ? "tension" : "compression")}-only truss is exported as an ordinary " +
                                   "truss, which carries both: set its limit in FEM-Design.", exportBeam.Label ?? $"truss {exportBeam.Id}");
                if (truss.Prestress != 0.0)
                    report.Warning("A truss's prestress is not exported.", exportBeam.Label ?? $"truss {exportBeam.Id}");
            }

            return exportBeam;
        }

        private static int SectionIndex(IUniaxialSection section, ExportModel export,
            Dictionary<object, int> materials, Dictionary<object, int> sections)
        {
            if (sections.TryGetValue(section, out int index))
                return index;

            var exportSection = new ExportBeamSection
            {
                Material = MaterialIndex(section.Material, export, materials),
                Area = section.Area,
                Izz = section.Izz,
                Iyy = section.Iyy,
                J = section.J,
            };

            switch (section)
            {
                case Sections.RectangleCS rectangle:
                    exportSection.Name = rectangle.SectionName;
                    exportSection.Shape = ExportSectionShape.Rectangle;
                    exportSection.Width = rectangle.Width;
                    exportSection.Height = rectangle.Height;
                    break;
                case Sections.CircleCS circle:
                    exportSection.Name = circle.SectionName;
                    exportSection.Shape = ExportSectionShape.Circle;
                    exportSection.Diameter = circle.Diameter;
                    exportSection.Thickness = circle.Thickness;
                    break;
                case Sections.RectangleHollowCS hollow:
                    exportSection.Name = hollow.SectionName;
                    exportSection.Shape = ExportSectionShape.RectangleHollow;
                    exportSection.Width = hollow.Width;
                    exportSection.Height = hollow.Height;
                    exportSection.Web = hollow.Web;
                    exportSection.TopFlange = hollow.TopFlange;
                    exportSection.BottomFlange = hollow.BottomFlange;
                    break;
                case Sections.ISection i:
                    exportSection.Name = i.SectionName;
                    exportSection.Shape = ExportSectionShape.I;
                    exportSection.Height = i.Height;
                    exportSection.TopWidth = i.TopWidth;
                    exportSection.BottomWidth = i.BottomWidth;
                    exportSection.TopFlange = i.TopFlangeThickness;
                    exportSection.BottomFlange = i.BottomFlangeThickness;
                    exportSection.Web = i.Web;
                    break;
                case Sections.DoubleLAngleCS angles:
                    exportSection.Name = angles.SectionName;
                    exportSection.Shape = ExportSectionShape.DoubleAngle;
                    exportSection.Height = angles.Height;
                    exportSection.Width = angles.Width;
                    exportSection.Thickness = angles.Thickness;
                    exportSection.Gap = angles.Gap;
                    break;
                case Sections.ElasticSection elastic:
                    exportSection.Name = elastic.SectionName;
                    exportSection.Shape = ExportSectionShape.Generic;
                    break;
                default:
                    exportSection.Shape = ExportSectionShape.Generic;
                    break;
            }

            index = export.BeamSections.Count;
            export.BeamSections.Add(exportSection);
            sections[section] = index;
            return index;
        }

        private static ExportShell ReadShell(IShell shell, ExportModel export, Dictionary<object, int> materials,
            Dictionary<object, int> sections, ConversionReport report)
        {
            var section = shell.Section ?? throw new InvalidOperationException("the shell has no section.");
            int sectionIndex;
            if (!sections.TryGetValue(section, out sectionIndex))
            {
                IMultiDimensionMaterial material;
                int layers = 1;
                switch (section)
                {
                    case Sections.LayeredShellSection layered when layered.Layers.Count > 0:
                        // The thickest layer stands for the section; the writer reports the approximation.
                        material = layered.Layers.OrderByDescending(layer => layer.Thickness).First().Material;
                        layers = layered.Layers.Count;
                        break;
                    default:
                        material = section.Material;
                        break;
                }

                sectionIndex = export.ShellSections.Count;
                export.ShellSections.Add(new ExportShellSection
                {
                    Name = (section as Sections.PlateFiberSection)?.SectionName ?? (section as Sections.LayeredShellSection)?.SectionName,
                    Thickness = section.Thickness,
                    Material = MaterialIndex(material, export, materials),
                    Layers = layers,
                });
                sections[section] = sectionIndex;
            }

            var localX = shell switch
            {
                ASDShellQ4 q4 => q4.LocalX,
                ASDShellT3 t3 => t3.LocalX,
                _ => Vector3d.Zero,
            };

            return new ExportShell
            {
                Id = (shell as IElement)?.Id ?? 0,
                Label = (shell as IElement)?.ElementId,
                Vertices = shell.Mesh.Vertices.ToPoint3dArray(),
                Section = sectionIndex,
                LocalX = localX.IsValid ? localX : Vector3d.Zero,
            };
        }

        private static int MaterialIndex(object material, ExportModel export, Dictionary<object, int> materials)
        {
            if (material == null)
                throw new InvalidOperationException("its section has no material.");
            if (materials.TryGetValue(material, out int index))
                return index;

            var exportMaterial = new ExportMaterial();
            switch (material)
            {
                case Materials.UniaxialMaterialElastic elastic:
                    exportMaterial.Name = elastic.MatName;
                    exportMaterial.Grade = elastic.Grade?.Name;
                    exportMaterial.E = elastic.E;
                    exportMaterial.G = elastic.G;
                    exportMaterial.Nu = elastic.Nu;
                    exportMaterial.Rho = elastic.Rho ?? 0.0;
                    break;
                case Materials.ElasticIsotropicMaterial isotropic:
                    exportMaterial.Name = isotropic.MatName;
                    exportMaterial.E = isotropic.E;
                    exportMaterial.G = isotropic.G;
                    exportMaterial.Nu = isotropic.Nu;
                    exportMaterial.Rho = isotropic.Rho ?? 0.0;
                    break;
                case Materials.ElasticOrthotropicMaterial orthotropic:
                    exportMaterial.Name = orthotropic.MatName;
                    exportMaterial.IsOrthotropic = true;
                    exportMaterial.Ex = orthotropic.Ex;
                    exportMaterial.Ey = orthotropic.Ey;
                    exportMaterial.Ez = orthotropic.Ez;
                    exportMaterial.Gxy = orthotropic.Gxy;
                    exportMaterial.Gyz = orthotropic.Gyz;
                    exportMaterial.Gzx = orthotropic.Gzx;
                    exportMaterial.NuXy = orthotropic.NuXy;
                    exportMaterial.NuYz = orthotropic.NuYz;
                    exportMaterial.NuZx = orthotropic.NuZx;
                    exportMaterial.Rho = orthotropic.Rho ?? 0.0;
                    // What an isotropic reader would look at, in case one does.
                    exportMaterial.E = orthotropic.Ex;
                    exportMaterial.G = orthotropic.Gxy;
                    exportMaterial.Nu = orthotropic.NuXy;
                    break;
                case IUniaxialMaterial uniaxial:
                    exportMaterial.Grade = uniaxial.Grade?.Name;
                    exportMaterial.E = uniaxial.E;
                    exportMaterial.G = uniaxial.G;
                    exportMaterial.Nu = uniaxial.Nu;
                    exportMaterial.Rho = uniaxial.Rho ?? 0.0;
                    break;
                default:
                    throw new InvalidOperationException($"{material.GetType().Name} materials are not exported.");
            }

            index = export.Materials.Count;
            export.Materials.Add(exportMaterial);
            materials[material] = index;
            return index;
        }

        private static ExportLink ReadLink(Link link, ConversionReport report)
        {
            var exportLink = new ExportLink
            {
                Id = link.Id ?? 0,
                Start = link.Line.From,
                End = link.Line.To,
                LocalX = link.LocalX,
                LocalY = link.LocalY,
            };

            int count = Math.Min(link.Directions?.Count ?? 0, link.Materials?.Count ?? 0);
            for (int i = 0; i < count; i++)
            {
                int direction = link.Directions[i];
                if (direction < 1 || direction > 6)
                    continue;
                if (link.Materials[i] is Materials.UniaxialMaterialElastic spring)
                    exportLink.Stiffness[direction - 1] = spring.E;
                else
                {
                    report.Error($"A link's {link.Materials[i]?.GetType().Name} spring is not exported; only elastic ones are.", Label(link));
                    return null;
                }
            }

            return exportLink;
        }

        private static ExportLoadCase ReadLoadPattern(LoadPattern pattern, int number,
            Dictionary<IBeam, int> beams, Dictionary<IShell, int> shells, ConversionReport report)
        {
            string name = string.IsNullOrWhiteSpace(pattern.Name) ? $"pattern {number}" : pattern.Name;

            if (pattern.PatternType != PatternType.Plain)
            {
                report.Error("A dynamic excitation (Uniform Excitation) has no load case to go into and is not exported.", name);
                return null;
            }

            var series = pattern.TimeSeries;
            double seriesFactor = series?.CFactor ?? 1.0;
            if (series != null && series.Type != Alpaca4d.TimeSeries.TimeSeriesType.Constant && series.Type != Alpaca4d.TimeSeries.TimeSeriesType.Linear)
                report.Warning($"A {series.Type} time series has no FEM-Design counterpart; its loads are exported as they are at a factor of {seriesFactor:G4}.", name);

            var loadCase = new ExportLoadCase
            {
                Name = pattern.Name,
                Number = number,
                Factor = pattern.Factor * seriesFactor,
            };

            foreach (var load in pattern.Load ?? new List<ILoad>())
            {
                switch (load)
                {
                    case PointLoad pointLoad:
                        loadCase.PointLoads.Add(pointLoad);
                        break;

                    case LineLoad lineLoad:
                        // Assemble Model gives a load without an element to every beam its filter names, or to every beam.
                        if (lineLoad.Element == null)
                        {
                            foreach (var beam in lineLoad.AppliedTo(beams.Keys))
                                loadCase.BeamLoads.Add(new ExportBeamLoad { Beam = beams[beam], Force = lineLoad.GlobalForce });
                        }
                        else if (beams.TryGetValue(lineLoad.Element, out int beamIndex))
                        {
                            var force = lineLoad.IsLocal ? LocalToGlobal(lineLoad.Element, lineLoad.LocalForce) : lineLoad.GlobalForce;
                            loadCase.BeamLoads.Add(new ExportBeamLoad { Beam = beamIndex, Force = force });
                        }
                        else
                            report.Warning("A beam load on a beam that is not in the model is not exported.", name);
                        break;

                    case MeshLoad meshLoad:
                        if (meshLoad.Element == null)
                        {
                            foreach (var shell in meshLoad.AppliedTo(shells.Keys))
                                loadCase.ShellLoads.Add(new ExportShellLoad { Shell = shells[shell], Pressure = meshLoad.GlobalForce });
                        }
                        else if (shells.TryGetValue(meshLoad.Element, out int shellIndex))
                            loadCase.ShellLoads.Add(new ExportShellLoad { Shell = shellIndex, Pressure = meshLoad.GlobalForce });
                        else
                            report.Warning("A shell load on a shell that is not in the model is not exported.", name);
                        break;

                    case Gravity gravity:
                        loadCase.GravityFactor += gravity.Factor;
                        break;

                    default:
                        report.Error($"{load?.GetType().Name ?? "An empty"} load is not exported.", name);
                        break;
                }
            }

            return loadCase;
        }

        /// <summary>A force in a beam's local axes (x along it, y and z across), in global axes.</summary>
        private static Vector3d LocalToGlobal(IBeam beam, Vector3d local)
        {
            var x = beam.Curve.PointAtEnd - beam.Curve.PointAtStart;
            x.Unitize();
            var z = beam.GeomTransf.LocalZ;
            var y = Vector3d.CrossProduct(z, x);
            return local.X * x + local.Y * y + local.Z * z;
        }

        private static string Label(object element)
        {
            if (element is IElement e)
                return string.IsNullOrWhiteSpace(e.ElementId) ? $"element {e.Id}" : e.ElementId;
            return element?.GetType().Name;
        }

        /// <summary>No Alpaca section or material overrides Equals, but a dictionary keyed on them must not depend on it.</summary>
        private sealed class ReferenceComparer : IEqualityComparer<object>
        {
            public static readonly ReferenceComparer Instance = new ReferenceComparer();
            public new bool Equals(object x, object y) => ReferenceEquals(x, y);
            public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
        }
    }
}
