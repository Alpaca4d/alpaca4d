using System;
using System.Collections.Generic;
using System.Linq;

using Rhino.Geometry;

using Alpaca4d.Constraints;
using StruSoft.Interop.StruXml.Data;

namespace Alpaca4d.FemDesign
{
    /// <summary>Supports, springs and constraints: all of them point supports and connections in FEM-Design.</summary>
    public sealed partial class StruxmlWriter
    {
        private int supportCount, connectionCount, diaphragmCount;

        private void WriteSupports()
        {
            foreach (var support in model.Supports)
            {
                // The stiffness along and about each axis: a spring support's own, or rigid where an
                // ordinary one holds.
                var k = support.Stiffness?.Select(value => double.IsPositiveInfinity(value) ? Rigid : value).ToArray()
                        ?? new[] { support.Tx, support.Ty, support.Tz, support.Rx, support.Ry, support.Rz }.Select(held => held ? Rigid : 0.0).ToArray();

                if (!k.Any(value => value > 0.0))
                {
                    report.Remark("A support that holds nothing is not exported.", support.Description);
                    continue;
                }

                // The restraints are read in the support's own axes, as in Alpaca.
                var springs = Springs(k[0], k[1], k[2], k[3], k[4], k[5]);
                var fdSupport = Point_support_type.Create(P(support.Position), V(support.XAxis), V(support.YAxis), springs, name: NextName("S"));
                Add(fdSupport, fdSupport.Guid);
                supportCount++;
            }
        }

        private void WriteLinks()
        {
            foreach (var link in model.Links)
            {
                const double max = 1e15;
                if (link.Stiffness.Any(k => k > max))
                    report.Warning($"FEM-Design's connections are at most {max:G} stiff; a stiffer spring was brought down to it.", $"link {link.Id}");
                var k = link.Stiffness.Select(value => Math.Min(Math.Max(value, 0.0), max)).ToArray();
                Connect(link.Start, link.End, link.LocalX, link.LocalY, Springs(k[0], k[1], k[2], k[3], k[4], k[5]), $"link {link.Id}");
            }
        }

        private void WriteConstraints()
        {
            foreach (var link in model.RigidLinks)
            {
                // A "bar" rigid link ties the translations only; a "beam" one the rotations too.
                double r = link.Type == RigidLinkType.beam ? Rigid : 0;
                ConnectTied(link.RetainedNode, link.ConstrainedNode, Springs(Rigid, Rigid, Rigid, r, r, r), "A rigid link");
            }

            foreach (var tie in model.EqualDofs)
            {
                ConnectTied(tie.MasterNode, tie.SlaveNode, Springs(
                    tie.Dof_x ? Rigid : 0, tie.Dof_y ? Rigid : 0, tie.Dof_z ? Rigid : 0,
                    tie.Dof_xx ? Rigid : 0, tie.Dof_yy ? Rigid : 0, tie.Dof_zz ? Rigid : 0), "An Equal DOF");
            }

            foreach (var diaphragm in model.RigidDiaphragms)
                WriteDiaphragm(diaphragm);
        }

        /// <summary>A connection in global axes between two nodes Alpaca ties together.</summary>
        private void ConnectTied(Point3d a, Point3d b, List<Stiffness_record> springs, string what)
        {
            if (a.DistanceTo(b) <= model.Tolerance)
            {
                // FEM-Design joins whatever meets at a point, so there is nothing left to tie or to release.
                report.Warning($"{what} between two nodes at one point is not exported: FEM-Design joins elements that meet at a point in every direction. " +
                               "Release them at the element's end instead.", $"({a.X:0.###}, {a.Y:0.###}, {a.Z:0.###})");
                return;
            }
            Connect(a, b, Vector3d.XAxis, Vector3d.YAxis, springs, what);
        }

        private void Connect(Point3d a, Point3d b, Vector3d localX, Vector3d localY, List<Stiffness_record> springs, string what)
        {
            try
            {
                var connection = Connected_points_type.Create(P(a), P(b), V(localX), V(localY), springs, name: NextName("CP"));
                Add(connection, connection.Guid);
                connectionCount++;
            }
            catch (ArgumentException e)
            {
                report.Error($"A connection FEM-Design does not accept is not exported: {e.Message}", what);
            }
        }

        /// <summary>
        /// A rigid diaphragm, as a FEM-Design diaphragm over the outline round its nodes. FEM-Design makes
        /// everything in that outline rigid in its plane, which takes in whatever else lies there too.
        /// </summary>
        private void WriteDiaphragm(RigidDiaphragm diaphragm)
        {
            var points = (diaphragm.SlaveNodes ?? new List<Point3d>()).ToList();
            if (diaphragm.MasterNode is Point3d master && master.IsValid)
                points.Add(master);

            var normal = diaphragm.Dir == 1 ? Vector3d.XAxis : diaphragm.Dir == 2 ? Vector3d.YAxis : Vector3d.ZAxis;
            var u = diaphragm.Dir == 1 ? Vector3d.YAxis : Vector3d.XAxis;
            var v = Vector3d.CrossProduct(normal, u);
            double level = points.Count > 0 ? points.Average(p => new Vector3d(p) * normal) : 0.0;

            const string name = "A rigid diaphragm";
            if (points.Any(p => Math.Abs(new Vector3d(p) * normal - level) > model.Tolerance))
                report.Warning("A rigid diaphragm whose nodes are not in one plane is exported in the plane through their average.", name);

            var hull = ConvexHull(points.Select(p => (new Vector3d(p) * u, new Vector3d(p) * v)).Distinct().ToList());
            if (hull.Count < 3)
            {
                report.Error("A rigid diaphragm whose nodes do not span an area is not exported.", name);
                return;
            }

            // Alpaca gives the retained node a node of its own; FEM-Design's diaphragm has none.
            if (diaphragm.MasterNode is Point3d retained && retained.IsValid
                && (model.LoadCases.SelectMany(c => c.PointLoads).Any(l => l.Pos.DistanceTo(retained) <= model.Tolerance)
                    || model.Masses.Any(m => m.Pos.DistanceTo(retained) <= model.Tolerance)))
                report.Warning("A load or mass on a rigid diaphragm's retained node is exported at that point, but FEM-Design's diaphragm has no node there: " +
                               "unless an element passes through it, FEM-Design has nothing to put it on. Put it on the diaphragm's nodes instead.",
                               $"({retained.X:0.###}, {retained.Y:0.###}, {retained.Z:0.###})");

            var outline = hull.Select(q => P(Point3d.Origin + q.Item1 * u + q.Item2 * v + level * normal)).ToList();
            var fdDiaphragm = Diaphragm_type.Create(Region_type.FromPointLoops(outline), NextName("FS"));
            Add(fdDiaphragm, fdDiaphragm.Guid);
            diaphragmCount++;
            report.Remark("A rigid diaphragm is exported as a FEM-Design diaphragm over the convex outline round its nodes: " +
                          "everything inside that outline, in its plane, is rigid in the plane.", name);
        }

        /// <summary>The convex hull of points in a plane, anticlockwise (Andrew's monotone chain).</summary>
        private static List<(double, double)> ConvexHull(List<(double X, double Y)> points)
        {
            var sorted = points.OrderBy(p => p.X).ThenBy(p => p.Y).ToList();
            if (sorted.Count < 3)
                return sorted.Select(p => (p.X, p.Y)).ToList();

            double Cross((double X, double Y) o, (double X, double Y) a, (double X, double Y) b) => (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);

            var hull = new List<(double X, double Y)>();
            foreach (var pass in new[] { sorted, Enumerable.Reverse(sorted).ToList() })
            {
                int start = hull.Count;
                foreach (var p in pass)
                {
                    while (hull.Count >= start + 2 && Cross(hull[hull.Count - 2], hull[hull.Count - 1], p) <= 1e-12)
                        hull.RemoveAt(hull.Count - 1);
                    hull.Add(p);
                }
                hull.RemoveAt(hull.Count - 1);
            }
            return hull.Select(p => (p.X, p.Y)).ToList();
        }

        /// <summary>One spring record, the same in tension and in compression [kN/m, kNm/rad].</summary>
        private static List<Stiffness_record> Springs(double kx, double ky, double kz, double cx, double cy, double cz)
            => new List<Stiffness_record> { Stiffness_record.Create(kx, kx, ky, ky, kz, kz, cx, cx, cy, cy, cz, cz) };
    }
}
