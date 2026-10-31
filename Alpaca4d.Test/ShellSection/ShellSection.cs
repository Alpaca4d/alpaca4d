// Shell Section: the forces through a cut drawn across shells.
//
// Two kinds of check.
//
//   1. Fields made up here, put at the Gauss points the way the solver would, and read back at
//      points of a cut. A linear or bilinear field has to come back exactly, in any frame, through
//      quads and triangles, smoothed or not. No solver needed.
//
//   2. Plates and walls OpenSees solves, cut every which way. The total across a cut is the load on
//      the side x points to - statics, whatever the mesh - so a cantilever cut anywhere has to
//      carry its tip load and the moment of it. The same cut on a mesh whose faces point the other
//      way, or cut into triangles, or smoothed, has to give the same totals.
//
// Without OpenSees (on PATH, or OPENSEES=...) the second half is skipped.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

using Rhino.Geometry;

using Alpaca4d;
using Alpaca4d.Generic;
using Alpaca4d.Result;

class ShellSectionTest
{
    static int fails = 0;

    static void Check(bool ok, string what)
    {
        if (!ok) fails++;
        Console.WriteLine($"  [{(ok ? "PASS" : "FAIL")}] {what}");
    }

    static void Close(double got, double want, double tol, string what)
    {
        bool ok = Math.Abs(got - want) <= tol * Math.Max(1.0, Math.Abs(want));
        if (!ok) fails++;
        Console.WriteLine($"  [{(ok ? "PASS" : "FAIL")}] {what}: got {got:G6}, want {want:G6}");
    }

    static void CloseVector(Vector3d got, Vector3d want, double tol, string what)
    {
        bool ok = (got - want).Length <= tol * Math.Max(1.0, want.Length);
        if (!ok) fails++;
        Console.WriteLine($"  [{(ok ? "PASS" : "FAIL")}] {what}: got ({got.X:G5}, {got.Y:G5}, {got.Z:G5}), want ({want.X:G5}, {want.Y:G5}, {want.Z:G5})");
    }

    // ---------------------------------------------------------------- a mesh

    /// <summary>A grid of shells, its loads and supports, and the deck that solves it.</summary>
    class Mesh
    {
        public List<Point3d> Nodes = new List<Point3d>();
        public List<int[]> Elements = new List<int[]>();
        public List<int> Fixed = new List<int>();
        public List<Tuple<int, Vector3d>> Loads = new List<Tuple<int, Vector3d>>();
        public double E = 30000000.0, Nu = 0.0, H = 0.2;

        /// <summary>
        /// nx by ny cells over the unit square, mapped to the model by <paramref name="map"/>.
        /// Corners anticlockwise in (u, v), unless <paramref name="flip"/> says otherwise for a cell.
        /// </summary>
        public static Mesh Grid(int nx, int ny, Func<double, double, Point3d> map, bool triangles = false,
                                Func<int, int, bool> flip = null)
        {
            var mesh = new Mesh();
            for (int j = 0; j <= ny; j++)
                for (int i = 0; i <= nx; i++)
                    mesh.Nodes.Add(map(i / (double)nx, j / (double)ny));

            Func<int, int, int> tag = (i, j) => j * (nx + 1) + i + 1;

            for (int j = 0; j < ny; j++)
                for (int i = 0; i < nx; i++)
                {
                    int a = tag(i, j), b = tag(i + 1, j), c = tag(i + 1, j + 1), d = tag(i, j + 1);
                    bool turned = flip != null && flip(i, j);

                    if (!triangles)
                    {
                        mesh.Elements.Add(turned ? new[] { a, d, c, b } : new[] { a, b, c, d });
                    }
                    else if ((i + j) % 2 == 0)
                    {
                        mesh.Elements.Add(turned ? new[] { a, c, b } : new[] { a, b, c });
                        mesh.Elements.Add(turned ? new[] { a, d, c } : new[] { a, c, d });
                    }
                    else
                    {
                        mesh.Elements.Add(turned ? new[] { a, d, b } : new[] { a, b, d });
                        mesh.Elements.Add(turned ? new[] { b, d, c } : new[] { b, c, d });
                    }
                }

            return mesh;
        }

        public string Tcl(string recorder)
        {
            var w = new StringWriter();
            w.WriteLine("wipe");
            w.WriteLine("model basic -ndm 3 -ndf 6");
            for (int i = 0; i < Nodes.Count; i++)
                w.WriteLine($"node {i + 1} {Nodes[i].X:R} {Nodes[i].Y:R} {Nodes[i].Z:R}");
            foreach (int n in Fixed)
                w.WriteLine($"fix {n} 1 1 1 1 1 1");
            w.WriteLine($"section ElasticMembranePlateSection 1 {E:R} {Nu:R} {H:R} 0.0");
            for (int e = 0; e < Elements.Count; e++)
            {
                var el = Elements[e];
                string type = el.Length == 4 ? "ASDShellQ4" : "ASDShellT3";
                w.WriteLine($"element {type} {e + 1} {string.Join(" ", el)} 1");
            }
            w.WriteLine($"recorder mpco {recorder} -E section.force");
            w.WriteLine("timeSeries Linear 1");
            w.WriteLine("pattern Plain 1 1 {");
            foreach (var load in Loads)
                w.WriteLine($"  load {load.Item1} {load.Item2.X:R} {load.Item2.Y:R} {load.Item2.Z:R} 0 0 0");
            w.WriteLine("}");
            w.WriteLine("constraints Transformation");
            w.WriteLine("numberer RCM");
            w.WriteLine("system UmfPack");
            w.WriteLine("test NormDispIncr 1e-10 10");
            w.WriteLine("algorithm Linear");
            w.WriteLine("integrator LoadControl 1");
            w.WriteLine("analysis Static");
            w.WriteLine("analyze 1");
            w.WriteLine("wipe");
            return w.ToString();
        }

        public List<SectionFace> Faces(Dictionary<int, double[][]> forces)
        {
            var faces = new List<SectionFace>();
            for (int e = 0; e < Elements.Count; e++)
            {
                var el = Elements[e];
                faces.Add(SectionFace.Of(e + 1, el.Select(n => Nodes[n - 1]).ToList(), el, Vector3d.Zero, forces[e + 1], H));
            }
            return faces;
        }
    }

    static string OpenSees;
    static string Work;

    /// <summary>The deck solved, and its shell forces by tag; null without OpenSees.</summary>
    static Dictionary<int, double[][]> Solve(Mesh mesh, string name)
    {
        if (OpenSees == null) return null;

        string deck = Path.Combine(Work, name + ".tcl");
        File.WriteAllText(deck, mesh.Tcl(name + ".mpco"));

        var run = new ProcessStartInfo(OpenSees, name + ".tcl")
        {
            WorkingDirectory = Work,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using (var process = Process.Start(run))
        {
            process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();
            process.WaitForExit();
        }

        var model = new Model { Recorders = new List<IRecorder> { new Recorder { FileName = Path.Combine(Work, name + ".mpco") } } };
        return Read.ShellForcesByTag(model, 0);
    }

    // ---------------------------------------------------------------- made-up fields

    /// <summary>Where the solver puts its Gauss points on a face, in its own order.</summary>
    static List<Point3d> GaussPoints(IList<Point3d> nodes)
    {
        if (nodes.Count == 3)
            return new List<Point3d>
            {
                (nodes[1] + nodes[2]) / 2.0,
                (nodes[0] + nodes[2]) / 2.0,
                (nodes[0] + nodes[1]) / 2.0,
            };

        double g = 1.0 / Math.Sqrt(3.0);
        double[] s = { -g, g, g, -g };
        double[] t = { -g, -g, g, g };
        var points = new List<Point3d>();
        for (int p = 0; p < 4; p++)
        {
            var at = Point3d.Origin;
            double[] n =
            {
                0.25 * (1 - s[p]) * (1 - t[p]), 0.25 * (1 + s[p]) * (1 - t[p]),
                0.25 * (1 + s[p]) * (1 + t[p]), 0.25 * (1 - s[p]) * (1 + t[p]),
            };
            for (int i = 0; i < 4; i++) at += nodes[i] * n[i];
            points.Add(at);
        }
        return points;
    }

    /// <summary>
    /// A field given as global tensors - membrane force and moment as 2 × 2 in global x and y, shear
    /// as a vector - turned into the eight local numbers of each face at its Gauss points.
    /// </summary>
    static List<SectionFace> Synthetic(Mesh mesh, Func<Point3d, double[]> field)
    {
        var faces = new List<SectionFace>();
        for (int e = 0; e < mesh.Elements.Count; e++)
        {
            var el = mesh.Elements[e];
            var nodes = el.Select(n => mesh.Nodes[n - 1]).ToList();
            var face = SectionFace.Of(e + 1, nodes, el, Vector3d.Zero, null);

            face.Gauss = GaussPoints(nodes).Select(p =>
            {
                // nxx, nyy, nxy, mxx, myy, mxy, qx, qy - global, the plate lying in global x-y.
                var g = field(p);
                return Local(g, face.X, face.Y, face.Z);
            }).ToArray();

            faces.Add(face);
        }
        return faces;
    }

    static double[] Local(double[] g, Vector3d x, Vector3d y, Vector3d z)
    {
        Func<double, double, double, Vector3d, Vector3d, double> along = (xx, yy, xy, a, b) =>
            a.X * (xx * b.X + xy * b.Y) + a.Y * (xy * b.X + yy * b.Y);

        // Moments and shears are against global +z; a face whose normal points down sees them turned.
        double sign = z.Z >= 0.0 ? 1.0 : -1.0;

        return new[]
        {
            along(g[0], g[1], g[2], x, x), along(g[0], g[1], g[2], y, y), along(g[0], g[1], g[2], x, y),
            sign * along(g[3], g[4], g[5], x, x), sign * along(g[3], g[4], g[5], y, y), sign * along(g[3], g[4], g[5], x, y),
            sign * (g[6] * x.X + g[7] * x.Y), sign * (g[6] * y.X + g[7] * y.Y),
        };
    }

    static void SyntheticTests()
    {
        Console.WriteLine("\nMade-up fields come back exactly\n");

        // A bilinear field on a rectangle is bilinear in the natural coordinates too, so a quad
        // holds it exactly; a linear one is all a triangle can hold. Each component its own field.
        Func<Point3d, double[]> bilinear = p => new[]
        {
            1 + 2 * p.X + 3 * p.Y + 0.5 * p.X * p.Y,
            -2 + p.X - p.Y,
            0.3 + 0.2 * p.X * p.Y,
            4 - p.X + 2 * p.Y + 0.25 * p.X * p.Y,
            1 + p.Y,
            -0.5 + 0.1 * p.X,
            2 + 0.7 * p.X - 0.2 * p.Y,
            -1 + 0.4 * p.Y,
        };
        Func<Point3d, double[]> linear = p => new[]
        {
            1 + 2 * p.X + 3 * p.Y, -2 + p.X - p.Y, 0.3 + 0.2 * p.X, 4 - p.X + 2 * p.Y,
            1 + p.Y, -0.5 + 0.1 * p.X, 2 + 0.7 * p.X - 0.2 * p.Y, -1 + 0.4 * p.Y,
        };

        Func<double, double, Point3d> square = (u, v) => new Point3d(4 * u, 2 * v, 0);

        var cases = new[]
        {
            Tuple.Create("quads", Mesh.Grid(4, 2, square), bilinear),
            Tuple.Create("quads, every other one facing down", Mesh.Grid(4, 2, square, false, (i, j) => (i + j) % 2 == 1), bilinear),
            Tuple.Create("triangles", Mesh.Grid(4, 2, square, true), linear),
            Tuple.Create("triangles, some facing down", Mesh.Grid(4, 2, square, true, (i, j) => i == 1), linear),
        };

        // A bent cut with corners inside faces and ends beyond the mesh, swept down onto it.
        var curve = new List<Point3d> { new Point3d(0.3, -1, 1), new Point3d(1.7, 0.9, 1), new Point3d(3.1, 0.4, 1), new Point3d(3.6, 3, 1) };

        foreach (var c in cases)
        {
            foreach (bool smooth in new[] { false, true })
            {
                var section = new ShellSection(Synthetic(c.Item2, c.Item3), smooth);
                var cut = section.Cut(curve, -Vector3d.ZAxis);

                double worst = 0.0;
                int count = 0;
                foreach (var run in cut.Runs)
                    foreach (var s in run.Stations)
                    {
                        var g = c.Item3(s.Point);
                        // What the cut should read: the tensors against its own axes, the
                        // moments and shears against its z, which here points down.
                        var want = Local(g, s.X, s.Y, s.Z);
                        double[] got = { s.Pxx, s.Pxy, s.Vxz, s.Mxx, s.Mxy };
                        double[] expected = { want[0], want[2], want[6], want[3], want[5] };
                        for (int k = 0; k < 5; k++)
                            worst = Math.Max(worst, Math.Abs(got[k] - expected[k]));
                        count++;
                    }

                Check(cut.Runs.Count == 1, $"{c.Item1}{(smooth ? ", smoothed" : "")}: one unbroken run, got {cut.Runs.Count}");
                Check(count > 0 && worst < 1e-9, $"{c.Item1}{(smooth ? ", smoothed" : "")}: {count} stations exact, worst error {worst:G3}");

                // Every station's z points the way the sweep does, so moments are read against
                // the side the direction points to, whichever way the faces face.
                Check(cut.Runs.SelectMany(r => r.Stations).All(s => s.Z * -Vector3d.ZAxis > 0.999),
                      $"{c.Item1}{(smooth ? ", smoothed" : "")}: z points the way the curve was swept");
            }
        }

        // The ends of the cut are where it leaves the mesh: y = 0 and y = 2 on the first and last
        // segments, which is a length worked out by hand.
        var plain = new ShellSection(Synthetic(Mesh.Grid(4, 2, square), bilinear), false);
        var bent = plain.Cut(curve, Vector3d.ZAxis);
        var p0 = new Point3d(0.3 + 1.4 * (1.0 / 1.9), 0, 0);
        var p1 = new Point3d(1.7, 0.9, 0);
        var p2 = new Point3d(3.1, 0.4, 0);
        var p3 = new Point3d(3.1 + 0.5 * (1.6 / 2.6), 2, 0);
        Close(bent.Length, p0.DistanceTo(p1) + p1.DistanceTo(p2) + p2.DistanceTo(p3), 1e-12, "the cut is clipped where it leaves the mesh");
        Check(bent.Runs[0].Cut.First.DistanceTo(p0) < 1e-12 && bent.Runs[0].Cut.Last.DistanceTo(p3) < 1e-12,
              "and runs the way the curve does");

        // A cut along a line of the mesh: two faces share every edge of it, and it is counted once.
        var onEdge = plain.Cut(new List<Point3d> { new Point3d(2, -1, 0), new Point3d(2, 3, 0) }, Vector3d.ZAxis);
        Check(onEdge.Runs.Count == 1, $"a cut along the mesh's lines is one run, got {onEdge.Runs.Count}");
        Close(onEdge.Length, 2.0, 1e-12, "and counted once");

        // Through the corners diagonally: from corner to corner, with nothing between.
        var diagonal = plain.Cut(new List<Point3d> { new Point3d(0, 0, 0), new Point3d(2, 2, 0) }, Vector3d.ZAxis);
        Check(diagonal.Runs.Count == 1, $"a cut through the corners is one run, got {diagonal.Runs.Count}");
        Close(diagonal.Length, 2.0 * Math.Sqrt(2.0), 1e-12, "and as long as the diagonal");

        // Along the edge of the mesh, where only one side has a face.
        var border = plain.Cut(new List<Point3d> { new Point3d(-1, 0, 0), new Point3d(5, 0, 0) }, Vector3d.ZAxis);
        Close(border.Length, 4.0, 1e-12, "a cut along the mesh's border finds the faces on its one side");
        var borderBack = plain.Cut(new List<Point3d> { new Point3d(5, 0, 0), new Point3d(-1, 0, 0) }, Vector3d.ZAxis);
        Close(borderBack.Length, 4.0, 1e-12, "drawn the other way too");

        // A curve that misses.
        Check(plain.Cut(new List<Point3d> { new Point3d(10, 0, 0), new Point3d(12, 0, 0) }, Vector3d.ZAxis).Runs.Count == 0,
              "a curve beside the mesh cuts nothing");

        // A uniform tension across a straight cut adds up to the tension times the length, along x.
        var uniform = new ShellSection(Synthetic(Mesh.Grid(4, 2, square), p => new[] { 5.0, 0, 0, 0, 0, 0, 0, 0 }), false);
        var across = uniform.Cut(new List<Point3d> { new Point3d(1.3, -1, 0), new Point3d(1.3, 3, 0) }, Vector3d.ZAxis);
        Close(across.N, 10.0, 1e-12, "N is the tension times the length");
        CloseVector(across.Axes.XAxis, Vector3d.XAxis, 1e-12, "x is square to the cut, to the left looking along the sweep");
        Close(across.Mz, 0.0, 1e-12, "and about the centre of the cut, a uniform tension has no moment");
    }

    // ---------------------------------------------------------------- against OpenSees

    /// <summary>
    /// A cantilever plate 4 by 1, fixed along x = 0, with 10 down on the tip spread as a line load.
    /// </summary>
    static Mesh Cantilever(int nx, int ny, double nu, bool triangles = false, Func<int, int, bool> flip = null)
    {
        var mesh = Mesh.Grid(nx, ny, (u, v) => new Point3d(4 * u, 1 * v, 0), triangles, flip);
        mesh.Nu = nu;
        for (int j = 0; j <= ny; j++)
        {
            mesh.Fixed.Add(j * (nx + 1) + 1);
            double share = (j == 0 || j == ny ? 0.5 : 1.0) / ny;
            mesh.Loads.Add(Tuple.Create(j * (nx + 1) + nx + 1, new Vector3d(0, 0, -10.0 * share)));
        }
        return mesh;
    }

    /// <summary>The moment of the tip load about a point, and the tip load: what the tip side carries.</summary>
    static Vector3d TipMoment(Point3d about) => Vector3d.CrossProduct(new Point3d(4, 0.5, 0) - about, new Vector3d(0, 0, -10));

    static void CantileverTests()
    {
        Console.WriteLine("\nA cantilever plate carries its tip load across any cut\n");

        var straight = new List<Point3d> { new Point3d(1.3, -0.5, 0), new Point3d(1.3, 1.5, 0) };

        // How close the smoothed totals come to statics. A displacement model's forces are not in
        // equilibrium point by point, so only a field it holds exactly - the linear moment of a
        // strip without Poisson's ratio, on quads - adds up to the load exactly. With Poisson's
        // ratio the free edges carry the twist and shear of a plate, which two elements across
        // the width catch to a couple of per cent.
        //
        // Triangles are as good for the moments and rough for the transverse shears: MITC3's
        // differ by a quarter between the points of one element, and carry a spurious shear along
        // the strip as well, which a cut square to the strip does not see and a diagonal one
        // does - 55% too much on this mesh. So the diagonal is not asked of them.
        var meshes = new[]
        {
            Tuple.Create("quads", Cantilever(8, 2, 0.0), 1e-6),
            Tuple.Create("quads, Poisson 0.3", Cantilever(8, 2, 0.3), 3e-2),
            Tuple.Create("quads, half facing down", Cantilever(8, 2, 0.3, false, (i, j) => (i + j) % 2 == 1), 3e-2),
            Tuple.Create("triangles", Cantilever(8, 2, 0.3, true), 1e-1),
            Tuple.Create("triangles, some facing down", Cantilever(8, 2, 0.3, true, (i, j) => j == 0), 1e-1),
        };

        var totals = new Dictionary<string, SectionCut>();

        foreach (var m in meshes)
        {
            string name = m.Item1;
            double tol = m.Item3;

            var forces = Solve(m.Item2, "cantilever" + totals.Count);
            if (forces == null) return;
            Check(forces.Count == m.Item2.Elements.Count, $"{name}: forces read for every shell, {forces.Count}");
            var faces = m.Item2.Faces(forces);

            var section = new ShellSection(faces, true);

            // Swept up: z up, x towards the tip. The tip side pulls down and hogs. The zeros are
            // measured against the moment, 27, rather than against 1.
            var cut = section.Cut(straight, Vector3d.ZAxis);
            totals[name] = cut;
            Close(cut.Length, 1.0, 1e-9, $"{name}: the cut spans the plate");
            Close(cut.Vz, -10.0, tol, $"{name}: Vz is the tip load");
            Close(cut.My, 27.0, tol, $"{name}: My is the tip load times 2.7, top in tension");
            Close(cut.N / 27.0, 0.0, tol, $"{name}: no N");
            Close(cut.Vy / 27.0, 0.0, tol, $"{name}: no Vy");
            Close(cut.Mx / 27.0, 0.0, tol, $"{name}: no Mx");
            Close(cut.Mz / 27.0, 0.0, tol, $"{name}: no Mz");

            // Swept down: z down, so the hogging moment puts the -z side in tension; x is now
            // to the root, so the force is the root's on the tip, upwards - down along z.
            var down = section.Cut(straight, -Vector3d.ZAxis);
            Close(down.My, -cut.My, 1e-12, $"{name}, swept down: My turns with z");
            Close(down.Vz, cut.Vz, 1e-12, $"{name}, swept down: Vz does not");

            // On a line of the mesh.
            var onLine = section.Cut(new List<Point3d> { new Point3d(2, -0.5, 0), new Point3d(2, 1.5, 0) }, Vector3d.ZAxis);
            Close(onLine.Length, 1.0, 1e-9, $"{name}: a cut along a line of the mesh is counted once");
            Close(onLine.My, 20.0, tol, $"{name}: and carries the moment there");

            // Bent, with a corner inside a face and an end beyond the plate: the moment of the tip
            // load about the centre of the cut, whatever shape the cut takes.
            var bent = section.Cut(new List<Point3d> { new Point3d(1.3, -0.5, 0), new Point3d(1.3, 0.3, 0), new Point3d(2.1, 1.5, 0) }, Vector3d.ZAxis);
            CloseVector(bent.Force, new Vector3d(0, 0, -10), tol, $"{name}: a bent cut carries the tip load");
            CloseVector(bent.Moment, TipMoment(bent.Axes.Origin), tol, $"{name}: and its moment about the cut's centre");

            // Through the corners, diagonally.
            if (!name.StartsWith("triangles"))
            {
                var diagonal = section.Cut(new List<Point3d> { new Point3d(0.5, 0, 0), new Point3d(1.5, 1, 0) }, Vector3d.ZAxis);
                CloseVector(diagonal.Force, new Vector3d(0, 0, -10), tol, $"{name}: a diagonal cut through the corners carries the tip load");
                CloseVector(diagonal.Moment, TipMoment(diagonal.Axes.Origin), tol, $"{name}: and its moment");
            }
            else
            {
                // Only the moments, which do not lean on the shears.
                Close(cut.My, 27.0, 1e-2, $"{name}: My, from the moments alone, to 1%");
            }

            // Left to the normal of the nearest face, the sweep finds the plate whichever way that
            // face points, and the totals of a cut are the same as swept that way by hand.
            var near = section.NormalNear(straight[0]);
            var auto = section.Cut(straight, near);
            var byHand = section.Cut(straight, near.Z > 0 ? Vector3d.ZAxis : -Vector3d.ZAxis);
            Close(auto.My, byHand.My, 1e-12, $"{name}: swept along the nearest normal, as by hand");

            // Unsmoothed, each stretch reads its own element. ASDShellQ4 gives one moment over the
            // whole element, its value at the centre: 27.5 between x = 1 and 1.5, and on a line of
            // the mesh the element on the side x points to, 17.5 between 2 and 2.5.
            if (name == "quads")
            {
                var own = new ShellSection(faces, false);
                var stations = own.Cut(straight, Vector3d.ZAxis).Runs.SelectMany(r => r.Stations).ToList();
                Close(stations.Max(s => Math.Abs(s.Mxx - 27.5)), 0.0, 1e-6, $"{name}, unsmoothed: mxx is the element's own 27.5 at every station");
                Close(stations.Max(s => Math.Abs(s.Vxz + 10.0)), 0.0, 1e-6, $"{name}, unsmoothed: vxz is -10 at every station");
                var line = own.Cut(new List<Point3d> { new Point3d(2, -0.5, 0), new Point3d(2, 1.5, 0) }, Vector3d.ZAxis);
                Close(line.My, 17.5, 1e-6, $"{name}, unsmoothed: on a line of the mesh, the element x points into");

                stations = cut.Runs.SelectMany(r => r.Stations).ToList();
                Close(stations.Max(s => Math.Abs(s.Mxx - 27.0)), 0.0, 1e-6, $"{name}, smoothed: mxx is 27 at every station");
            }

            // As recorded, a triangle's shears are Stenberg's factor too large: sqrt(0.04 / 0.09),
            // 2/3, on these, whose longest side is 0.707 and thickness 0.2.
            if (name == "triangles")
            {
                var mesh = m.Item2;
                var raw = mesh.Elements.Select((el, e) =>
                    SectionFace.Of(e + 1, el.Select(n => mesh.Nodes[n - 1]).ToList(), el, Vector3d.Zero, forces[e + 1])).ToList();
                var rawCut = new ShellSection(raw, true).Cut(straight, Vector3d.ZAxis);
                Close(rawCut.Vz / cut.Vz, 1.5, 1e-9, $"{name}: the recorded shear is 1.5 times the one in equilibrium");
                Close(rawCut.My, cut.My, 1e-12, $"{name}: and the moments are left alone");
            }
        }

        // The same plate, its faces turned round: the same answer, to the solver's precision.
        Close(totals["quads, half facing down"].My, totals["quads, Poisson 0.3"].My, 1e-9,
              "faces facing down change nothing: My");
        Close(totals["quads, half facing down"].Vz, totals["quads, Poisson 0.3"].Vz, 1e-9,
              "faces facing down change nothing: Vz");
        Close(totals["triangles, some facing down"].My, totals["triangles"].My, 1e-9,
              "triangles facing down change nothing: My");
    }

    /// <summary>
    /// A wall 4 wide and 3 high in the x-z plane, fixed along its foot, with 20 along +x and 30
    /// down spread along its top.
    /// </summary>
    static void WallTests()
    {
        Console.WriteLine("\nA wall carries what is on top of it across a horizontal cut\n");

        int nx = 8, nz = 6;
        var wall = Mesh.Grid(nx, nz, (u, v) => new Point3d(4 * u, 0, 3 * v));
        wall.Nu = 0.2;
        for (int i = 0; i <= nx; i++)
        {
            wall.Fixed.Add(i + 1);
            double share = (i == 0 || i == nx ? 0.5 : 1.0) / nx;
            wall.Loads.Add(Tuple.Create(nz * (nx + 1) + i + 1, new Vector3d(20.0 * share, 0, -30.0 * share)));
        }

        var forces = Solve(wall, "wall");
        if (forces == null) return;
        var faces = wall.Faces(forces);

        var section = new ShellSection(faces, true);
        foreach (double z in new[] { 1.0, 1.7 })
        {
            string name = $"cut at z = {z}";

            // Swept along +y, square to the wall: x points up, to the part above.
            var cut = section.Cut(new List<Point3d> { new Point3d(-1, 0, z), new Point3d(5, 0, z) }, Vector3d.YAxis);
            Close(cut.Length, 4.0, 1e-9, $"{name}: the cut spans the wall");
            CloseVector(cut.Axes.XAxis, Vector3d.ZAxis, 1e-9, $"{name}: x points up, to the part above");
            Close(cut.N, -30.0, 1e-3, $"{name}: N is the load on top, in compression");
            Close(cut.Vy, 20.0, 1e-3, $"{name}: Vy is the push along the top");
            Close(cut.Mz, 20.0 * (3.0 - z), 1e-3, $"{name}: Mz is the push times its height above the cut, +x side in compression");
            Close(cut.Vz, 0.0, 1e-9, $"{name}: nothing out of the wall's plane");
            Close(cut.My, 0.0, 1e-9, $"{name}: no My");
        }

        // From a line drawn in plan above the wall and swept down: the wall cut top to bottom.
        var plan = section.Cut(new List<Point3d> { new Point3d(2.25, -1, 5), new Point3d(2.25, 1, 5) }, -Vector3d.ZAxis);
        Check(plan.Runs.Count == 1, $"swept down from a plan line, the wall is cut in one run, got {plan.Runs.Count}");
        Close(plan.Length, 3.0, 1e-9, "top to bottom");
        CloseVector(plan.Runs[0].Stations[0].Y, -Vector3d.ZAxis, 1e-9, "running the way it was swept");
        CloseVector(plan.Axes.XAxis, -Vector3d.XAxis, 1e-9, "x to the left of the curve, looking down");
    }

    static int Main(string[] args)
    {
        Work = Path.Combine(Path.GetTempPath(), "shellsection-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Work);

        OpenSees = args.Length > 0 && File.Exists(args[0]) ? args[0] : null;
        if (OpenSees == null)
            Console.WriteLine("note: OpenSees not given, the solved checks are skipped");

        SyntheticTests();
        CantileverTests();
        WallTests();

        try { Directory.Delete(Work, true); } catch { }

        Console.WriteLine(fails == 0 ? "\nall passed" : $"\n{fails} failed");
        return fails == 0 ? 0 : 1;
    }
}
