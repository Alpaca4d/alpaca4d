using System;
using System.Collections.Generic;
using System.Linq;

using Rhino.Geometry;

using Alpaca4d.Element;
using StruSoft.Interop.StruXml.Data;

namespace Alpaca4d.FemDesign
{
    public sealed partial class StruxmlWriter
    {
        private enum BarKind { Beam, Column, Truss, Fictitious }

        /// <summary>Where a beam ended up, which its loads need: FEM-Design puts a line load on the bar under it.</summary>
        private sealed class WrittenBar
        {
            public BarKind Kind;
            public Point3d Start;
            public Point3d End;
        }

        /// <summary>One per beam of the export model; null for a beam that was not exported.</summary>
        private WrittenBar[] bars = new WrittenBar[0];

        private void WriteBars()
        {
            var loaded = new HashSet<int>(model.LoadCases.SelectMany(c => c.BeamLoads).Where(l => l.Force.Length > 0).Select(l => l.Beam));

            bars = new WrittenBar[model.Beams.Count];
            for (int i = 0; i < model.Beams.Count; i++)
            {
                var beam = model.Beams[i];
                try
                {
                    bars[i] = WriteBar(beam, loaded.Contains(i));
                }
                catch (ArgumentException e)
                {
                    report.Error($"A beam FEM-Design does not accept is not exported: {e.Message}", Describe(beam.Id, beam.Label));
                }
            }
        }

        private WrittenBar WriteBar(ExportBeam beam, bool loaded)
        {
            string label = Describe(beam.Id, beam.Label);
            var start = beam.Start;
            var end = beam.End;
            var axis = Unit(end - start);
            if (axis.IsZero)
            {
                report.Error("A beam of no length is not exported.", label);
                return null;
            }
            if (!beam.IsStraight)
                report.Warning("FEM-Design's bars are straight; a curved beam is exported as the line between its ends.", label);

            // FEM-Design's local z runs along the depth of the section, which is Alpaca's local y, so its
            // local y is Alpaca's local z turned back.
            var localY = SquareTo(-beam.LocalZ, axis);
            if (localY.IsZero)
                localY = DefaultLocalY(axis);

            var releaseI = beam.ReleaseI;
            var releaseJ = beam.ReleaseJ;
            var section = model.BeamSections[beam.Section];

            BarKind kind;
            if (section.Shape == ExportSectionShape.Generic)
                kind = BarKind.Fictitious;
            else if (IsPinned(releaseI) && IsPinned(releaseJ) && (!releaseI.Rx || !releaseJ.Rx) && !loaded)
                kind = BarKind.Truss;
            else if (IsColumn(end - start))
                kind = BarKind.Column;
            else
                kind = BarKind.Beam;

            if (beam.IsTruss && loaded && kind != BarKind.Fictitious)
                report.Warning("A truss with a line load on it is exported as a beam pinned at both ends: a FEM-Design truss takes no " +
                               "line load. Alpaca puts that load on the truss's end nodes instead.", label);

            if (kind == BarKind.Column && axis.Z < 0)
            {
                // A FEM-Design column runs upwards. Turning it round, and its local y with it, keeps local z
                // - the depth - where it was, so the section only mirrors across the depth, which none of
                // Alpaca's sections notices.
                (start, end) = (end, start);
                (releaseI, releaseJ) = (releaseJ, releaseI);
                localY = -localY;
            }

            var connectivity = Connectivity(releaseI, releaseJ);

            switch (kind)
            {
                case BarKind.Fictitious:
                    WriteFictitiousBar(beam, section, start, end, localY, connectivity, loaded);
                    break;

                case BarKind.Truss:
                {
                    var material = MaterialOf(section.Material, out var family);
                    var barSection = SectionOf(beam.Section, family);
                    var truss = Bar_type.CreateTruss(P(start), P(end), material.Guid, barSection.Section.Guid, V(localY),
                        name: NextName("T", beam.Label));
                    Add(truss, truss.Guid);
                    break;
                }

                default:
                {
                    var material = MaterialOf(section.Material, out var family);
                    var barSection = SectionOf(beam.Section, family);
                    var bar = kind == BarKind.Column
                        ? Bar_type.CreateColumn(P(start), P(end), material.Guid, barSection.Complex.Guid, V(localY), connectivity, name: NextName("C", beam.Label))
                        : Bar_type.CreateBeam(P(start), P(end), material.Guid, barSection.Complex.Guid, V(localY), connectivity, name: NextName("B", beam.Label));
                    Add(bar, bar.Guid);
                    break;
                }
            }

            return new WrittenBar { Kind = kind, Start = start, End = end };
        }

        /// <summary>
        /// A beam whose section is known only by A, I and J, as a fictitious bar of the same stiffness. It
        /// needs neither a material nor a section; its mass goes in as a mass per length.
        /// </summary>
        private void WriteFictitiousBar(ExportBeam beam, ExportBeamSection section, Point3d start, Point3d end, Vector3d localY,
            List<Connectivity_type> connectivity, bool loaded)
        {
            string label = Describe(beam.Id, beam.Label);
            var material = model.Materials[section.Material];
            double g = material.G > 0 ? material.G : material.E / (2.0 * (1.0 + material.Nu));

            // FEM-Design's I1 is about its local y, which is Alpaca's local z: the strong axis.
            double Stiffness(double value, string what)
            {
                const double min = 1e-5, max = 1e15;
                if (value >= min && value <= max)
                    return value;
                report.Warning($"FEM-Design's fictitious bars take a stiffness between {min:G} and {max:G}; {what} = {value:G4} was brought within it.", label);
                return Math.Min(Math.Max(value, min), max);
            }

            var bar = Virtual_bar_type.CreateNormal(
                new Edge_type(P(start), P(end)), V(localY),
                ae: Stiffness(material.E * section.Area, "EA"),
                itG: Stiffness(g * section.J, "GJ"),
                i1E: Stiffness(material.E * section.Izz, "EI strong"),
                i2E: Stiffness(material.E * section.Iyy, "EI weak"),
                unitMass: Math.Max(section.Area * material.Rho / 1000.0, 0.0),
                connectivity: connectivity,
                name: NextName("BF", beam.Label));
            Add(bar, bar.Guid);

            report.Remark("A section known only by A, I and J has no outline for FEM-Design to work from; its beam is exported as a fictitious bar of the same stiffness and mass, which FEM-Design does not design.",
                string.IsNullOrWhiteSpace(section.Name) ? label : section.Name);
            if (loaded)
                report.Warning("A beam load on a fictitious bar is exported as a line load along it; check FEM-Design applies it.", label);
        }

        /// <summary>
        /// FEM-Design's end releases for Alpaca's, null for a bar rigid at both ends. The axes swap as the
        /// local axes do: FEM-Design's y is Alpaca's z and its z Alpaca's y.
        /// </summary>
        private static List<Connectivity_type> Connectivity(Release releaseI, Release releaseJ)
        {
            if (IsRigid(releaseI) && IsRigid(releaseJ))
                return null;

            Connectivity_type End(Release r) => r == null
                ? Connectivity_type.CreateFixed()
                : Connectivity_type.Create(mX: r.Tx, mY: r.Tz, mZ: r.Ty, rX: r.Rx, rY: r.Rz, rZ: r.Ry);

            return new List<Connectivity_type> { End(releaseI), End(releaseJ) };
        }

        private static bool IsRigid(Release r) => r == null || (r.Tx && r.Ty && r.Tz && r.Rx && r.Ry && r.Rz);

        /// <summary>Free to turn in bending and held in every translation: a pin.</summary>
        private static bool IsPinned(Release r) => r != null && !r.Ry && !r.Rz && r.Tx && r.Ty && r.Tz;

        /// <summary>
        /// Vertical enough for FEM-Design to take as a column: it refuses one whose ends are 1e-6 m or more
        /// apart in X or in Y, so this asks for a tenth of that.
        /// </summary>
        private static bool IsColumn(Vector3d line) => Math.Abs(line.X) < 1e-7 && Math.Abs(line.Y) < 1e-7;

        private static bool IsVertical(Vector3d unitAxis) => Math.Sqrt(unitAxis.X * unitAxis.X + unitAxis.Y * unitAxis.Y) <= 1e-9;

        /// <summary>FEM-Design's own default local y: horizontal, or global X for a vertical bar pointing up.</summary>
        private static Vector3d DefaultLocalY(Vector3d unitAxis)
        {
            return Unit(Vector3d.CrossProduct(IsVertical(unitAxis) ? Vector3d.YAxis : Vector3d.ZAxis, unitAxis));
        }
    }
}
