// Truss and Spring Support, checked inside a running Rhino 8: Assemble finds nodes through
// RhinoCommon, a support reads its plane through it, and a truss's line load needs its curve.
// rhino.sh compiles this with TrussCheck.cs into one library and calls Rhino(...) by reflection.
//
// Each model is built the way the components build it, assembled, solved by OpenSees, and read
// back through Read.ForceBeamColumn and Read.NodalOutput - the readers Beam Forces and Reaction
// Force use.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

using Rhino.Geometry;

using Alpaca4d;
using Alpaca4d.Element;
using Alpaca4d.Generic;
using Alpaca4d.Loads;
using Alpaca4d.Result;

public partial class TrussCheck
{
    static string folder, openSees;

    /// <summary>Everything that needs Rhino; what it prints, PASS and FAIL lines included.</summary>
    public static string Rhino(string work, string solver)
    {
        folder = work;
        openSees = solver;
        fails = 0;

        var printed = new StringWriter();
        var console = Console.Out;
        Console.SetOut(printed);
        try
        {
            SpringSupportTcl();

            Section("Decks for OpenSees");
            Deck.Springs(Path.Combine(folder, "spring.tcl"));
            Deck.Bearing(Path.Combine(folder, "bearing.tcl"));
            Deck.Gap(Path.Combine(folder, "gap.tcl"));
            Console.WriteLine("  [ .. ] spring, bearing, gap written");

            LineLoads();
            PanelAssembled();
            TrussMeetsBeam();
            SupportsOnTrussNodes();
            BearingAssembled();
            SpringsHeldRigidly();
            ExportedToFemDesign();
        }
        catch (Exception e)
        {
            Check(false, $"no exception: {e}");
        }
        finally
        {
            Console.SetOut(console);
        }

        printed.WriteLine();
        printed.WriteLine(fails == 0 ? "all rhino checks passed" : $"{fails} rhino check(s) failed");
        return printed.ToString();
    }

    // ---------------------------------------------------------------- building

    static Truss Bar(Point3d a, Point3d b, double area, AxialBehaviour worksIn = AxialBehaviour.Both, double prestress = 0.0)
    {
        var line = new LineCurve(a, b);
        return new Truss(line, Bar(area), Utils.DefaultLocalZ(line), worksIn, prestress);
    }

    static ForceBeamColumn Beam(Point3d a, Point3d b, double width = 0.2, double height = 0.4)
    {
        var line = new LineCurve(a, b);
        var section = new Alpaca4d.Section.RectangleCS("beam", width, height, Steel());
        return new ForceBeamColumn(line, section, new GeomTransf(GeomTransfType.Linear, line, Utils.DefaultLocalZ(line)));
    }

    static Support Held(Point3d at, bool tx, bool ty, bool tz, bool rx, bool ry, bool rz) => new Support(at, tx, ty, tz, rx, ry, rz);

    static LoadPattern Pattern(params ILoad[] loads) =>
        new LoadPattern(PatternType.Plain, new Alpaca4d.TimeSeries.Linear(1.0), loads.ToList());

    static PointLoad Force(Point3d at, Vector3d force) => new PointLoad(at, force, Vector3d.Zero, new Alpaca4d.TimeSeries.Linear(1.0));

    static Model Assembled(List<IElement> elements, List<Support> supports, params LoadPattern[] patterns)
    {
        var model = new Model(elements, supports, patterns.ToList(), new List<IConstraint>(), new List<IRecorder>()) { Tollerance = 0.01 };
        model.Assemble();
        return model;
    }

    /// <summary>Solves an assembled model in ten Newton steps, the way Run Analysis would with Newton picked.</summary>
    static bool Solve(Model model, string name, out string output)
    {
        var recorder = Recorder.MpcoStatic(Path.Combine(folder, name + ".mpco"));
        model.Recorders = new List<IRecorder> { recorder };
        model.Tcl.Add(recorder.WriteTcl());
        model.Tcl.Add("system BandGeneral\nnumberer RCM\nconstraints Transformation\ntest NormDispIncr 1e-10 50\n" +
                      "algorithm Newton\nintegrator LoadControl 0.1\nanalysis Static\n" +
                      "if {[analyze 10] != 0} { puts \"ANALYSIS FAILED\" }\n");
        model.Tcl.Add("wipe");
        model.FileName = Path.Combine(folder, name + ".tcl");
        model.Serialise();

        using (var process = new Process())
        {
            process.StartInfo.FileName = openSees;
            process.StartInfo.Arguments = $"\"{model.FileName}\"";
            process.StartInfo.WorkingDirectory = folder;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.Start();
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();
            output = stdout.Result + stderr;
            bool solved = process.ExitCode == 0 && !output.Contains("ANALYSIS FAILED") && !output.Contains("already constrained");
            if (!solved)
                Console.WriteLine("        " + string.Join("\n        ", output.Split('\n').Where(line => line.Trim().Length > 0).Reverse().Take(8).Reverse()));
            return solved;
        }
    }

    /// <summary>Each node's fixed degrees of freedom, and whether any was fixed twice - OpenSees refuses that.</summary>
    static Dictionary<int, List<int>> Fixes(Model model, out bool twice)
    {
        var fixes = new Dictionary<int, List<int>>();
        twice = false;
        foreach (var tokens in model.Tcl.SelectMany(entry => Lines(entry)).Where(t => t[0] == "fix"))
        {
            int node = int.Parse(tokens[1]);
            if (!fixes.ContainsKey(node)) fixes[node] = new List<int>();
            for (int d = 2; d < tokens.Length; d++)
                if (tokens[d] == "1")
                {
                    if (fixes[node].Contains(d - 1)) twice = true;
                    fixes[node].Add(d - 1);
                }
        }
        return fixes;
    }

    static int NodeAt(Model model, Point3d point) => model.NodeTagNear(point, out _);

    /// <summary>The reaction a support's row of Reaction Force shows: read where the support's fix is, in global axes.</summary>
    static Vector3d Reaction(Model model, Support support)
    {
        int step = Read.StepCount(model) - 1;
        return Read.NodalOutput(model, step, ResultType.REACTION_FORCE, new List<int?> { support.AuxiliaryNodeId ?? support.Id }).Single();
    }

    // ---------------------------------------------------------------- the checks

    static void LineLoads()
    {
        Section("A line load on a truss, put on its ends");
        {
            var level = Bar(new Point3d(0, 0, 0), new Point3d(4, 0, 0), 0.01);
            var sloped = Bar(new Point3d(10, 0, 0), new Point3d(13, 0, 4), 0.01);
            // Pinned at one end and free along X at the other, so that each bar has something to solve.
            var supports = new List<Support>
            {
                Held(new Point3d(0, 0, 0), true, true, true, false, false, false),
                Held(new Point3d(4, 0, 0), false, true, true, false, false, false),
                Held(new Point3d(10, 0, 0), true, true, true, false, false, false),
                Held(new Point3d(13, 0, 4), false, true, true, false, false, false),
            };
            var ts = new Alpaca4d.TimeSeries.Linear(1.0);
            var model = Assembled(new List<IElement> { level, sloped }, supports,
                Pattern(new LineLoad(level, new Vector3d(0, 0, -3), ts), new LineLoad(sloped, new Vector3d(0, 0, -2), ts)));

            var pattern = string.Join("", model.Tcl.Skip(model.LoadPatternTclStart));
            var loads = Lines(pattern).Where(t => t[0] == "load").ToList();
            Check(!pattern.Contains("eleLoad"), "no element load on a truss: it has no bending to carry one");
            Check(loads.Count == 4, $"two point loads a bar ({loads.Count})");
            Check(loads.Where(t => t[1] == level.INode.ToString() || t[1] == level.JNode.ToString()).All(t => Near(Num(t[4]), -6.0)),
                  "half of w L at each end of the level bar, 6 kN");
            Check(Near(loads.Where(t => t[1] == sloped.INode.ToString() || t[1] == sloped.JNode.ToString()).Sum(t => Num(t[4])), -10.0),
                  "w times the sloped bar's own length, 5 m, in all");

            Check(Solve(model, "lineload", out var log), "it solves");
            Check(Near(Reaction(model, supports[0]).Z, 6.0, 1e-6) && Near(Reaction(model, supports[1]).Z, 6.0, 1e-6),
                  $"and the supports take 6 kN each ({Reaction(model, supports[0]).Z:G6})");
            Check(Near(Reaction(model, supports[2]).Z + Reaction(model, supports[3]).Z, 10.0, 1e-6),
                  "and 10 kN in all under the sloped one");
        }
    }

    static void PanelAssembled()
    {
        Section("An X-braced panel of trusses, assembled");
        {
            Point3d p1 = new Point3d(0, 0, 0), p2 = new Point3d(4, 0, 0), p3 = new Point3d(4, 0, 3), p4 = new Point3d(0, 0, 3);
            var bars = new List<IElement>
            {
                Bar(p1, p2, 0.01), Bar(p2, p3, 0.01), Bar(p3, p4, 0.01), Bar(p4, p1, 0.01),
                Bar(p1, p3, 0.001, AxialBehaviour.Tension), Bar(p2, p4, 0.001, AxialBehaviour.Tension),
            };
            var supports = new List<Support>
            {
                Held(p1, true, true, true, false, false, false),
                Held(p2, false, true, true, false, false, false),
                Held(p3, false, true, false, false, false, false),
                Held(p4, false, true, false, false, false, false),
            };
            var model = Assembled(bars, supports, Pattern(Force(p4, new Vector3d(10, 0, 0))));

            Check(model.Tcl.SelectMany(e => Lines(e)).Count(t => t[0] == "element" && t[1] == "corotTruss") == 6, "six corotTruss elements");
            var fixes = Fixes(model, out bool twice);
            Check(!twice, "no degree of freedom fixed twice");
            Check(new[] { p1, p2, p3, p4 }.All(p => new[] { 4, 5, 6 }.All(fixes[NodeAt(model, p)].Contains)),
                  "every node only trusses reach has its rotations fixed");
            Check(fixes[NodeAt(model, p1)].Count == 6, "the pinned support's node: its translations, and the rotations beside them");
            Check(model.HasOneWay, "tension-only bars: the model is one way, for Run Analysis's warning");

            Check(Solve(model, "panel-assembled", out var log), "it solves");
            var n = Read.ForceBeamColumn(model, Read.StepCount(model) - 1).n;
            Check(Near(n[4][0], 12.5, 1e-3), $"Beam Forces: the tensioned diagonal carries 12.5 kN ({n[4][0]:G6})");
            Check(n[5][0] == 0.0, "the other diagonal is slack");
            Check(Near(Reaction(model, supports[0]).X, -10.0, 1e-6), "Reaction Force: the pinned support holds the 10 kN");
        }
    }

    static void TrussMeetsBeam()
    {
        Section("A cantilever propped by a truss");
        {
            Point3d root = new Point3d(0, 0, 0), tip = new Point3d(3, 0, 0), ground = new Point3d(3, 0, -2);
            var beam = Beam(root, tip);
            var prop = Bar(tip, ground, 2e-5);
            var supports = new List<Support>
            {
                Held(root, true, true, true, true, true, true),
                Held(ground, true, true, true, false, false, false),
            };
            var model = Assembled(new List<IElement> { beam, prop }, supports, Pattern(Force(tip, new Vector3d(0, 0, -10))));

            var fixes = Fixes(model, out bool twice);
            Check(!twice, "no degree of freedom fixed twice");
            Check(!fixes.ContainsKey(NodeAt(model, tip)), "the tip, where the beam holds the rotations, is left alone");
            Check(new[] { 1, 2, 3, 4, 5, 6 }.All(fixes[NodeAt(model, ground)].Contains), "the foot, only the truss reaches it, gets its rotations fixed");

            Check(Solve(model, "propped", out var log), "it solves");
            var read = Read.ForceBeamColumn(model, Read.StepCount(model) - 1);
            // The section is Timoshenko's, as every Alpaca4d rectangle is: shear bends it too.
            var section = beam.Section;
            double kb = 1.0 / (27.0 / (3.0 * E * section.Izz) + 3.0 / (section.Material.G * section.Area * section.AlphaY));
            double kt = E * 2e-5 / 2.0;
            Check(Near(read.n[1][0], -10.0 * kt / (kt + kb), 1e-3), $"the prop carries its share by stiffness ({read.n[1][0]:G6} against {-10.0 * kt / (kt + kb):G6})");
            Check(read.n[0].Count == 5 && read.n[1].Count == 2, "the beam's sections and the truss's ends, in one read");
        }
    }

    static void SupportsOnTrussNodes()
    {
        Section("Supports on nodes only trusses reach");
        {
            Point3d a = new Point3d(0, 0, 0), b = new Point3d(4, 0, 0), c = new Point3d(2, 0, 2);
            var bars = new List<IElement> { Bar(a, b, 0.01), Bar(b, c, 0.01), Bar(c, a, 0.01) };

            var skew = new Plane(b, new Vector3d(Math.Cos(0.5), Math.Sin(0.5), 0), new Vector3d(-Math.Sin(0.5), Math.Cos(0.5), 0));
            var supports = new List<Support>
            {
                Held(a, true, true, true, true, true, true),
                new Support(skew, true, true, true, false, false, false),
                Held(c, false, true, false, false, false, false),
            };
            var model = Assembled(bars, supports, Pattern(Force(c, new Vector3d(5, 0, -5))));

            var fixes = Fixes(model, out bool twice);
            Check(!twice, "no degree of freedom fixed twice");
            Check(model.Tcl.SelectMany(e => Lines(e)).Count(t => t[0] == "fix" && t[1] == NodeAt(model, a).ToString()) == 1,
                  "a fixed support already holds the rotations: nothing more is written there");
            Check(new[] { 4, 5, 6 }.All(fixes[NodeAt(model, b)].Contains) && supports[1].AuxiliaryNodeId.HasValue,
                  "a skewed one holds through a node of its own, so the rotations are fixed on the truss node");
            Check(Solve(model, "skewed", out var log), "and OpenSees takes it");
        }
    }

    static Model Bearing(SpringBehaviour behaviour, out Support spring, out Support[] rigid)
    {
        Point3d a = new Point3d(0, 0, 0), b = new Point3d(4, 0, 0), c = new Point3d(8, 0, 0);
        // Slender, so that its shear deformation leaves the textbook reactions alone.
        var first = Beam(a, b, 0.05, 0.1);
        var second = Beam(b, c, 0.05, 0.1);
        rigid = new[]
        {
            Held(a, true, true, true, true, false, true),
            Held(b, false, true, true, true, false, true),
            Held(c, false, true, false, true, false, true),
        };
        spring = Support.WithSprings(new Plane(c, Vector3d.XAxis, Vector3d.YAxis), new[] { new SupportSpring(3, null, behaviour) });
        var supports = rigid.Concat(new[] { spring }).ToList();
        return Assembled(new List<IElement> { first, second }, supports,
            Pattern(new LineLoad(first, new Vector3d(0, 0, -10), new Alpaca4d.TimeSeries.Linear(1.0))));
    }

    static void BearingAssembled()
    {
        Section("A continuous beam on a compression-only bearing, assembled");
        {
            var model = Bearing(SpringBehaviour.CompressionOnly, out var spring, out var rigid);
            var fixes = Fixes(model, out bool twice);

            Check(model.Warnings.Count == 0, "no warning: the fix beside the bearing holds other directions");
            Check(!twice, "no degree of freedom fixed twice");
            Check(spring.AuxiliaryNodeId.HasValue && fixes[spring.AuxiliaryNodeId.Value].Count == 6, "the bearing has a fixed node of its own");
            var text = string.Join("", model.Tcl);
            Check(text.Contains($"uniaxialMaterial ENT {spring.SpringMaterials[0].Id} "), "a rigid ENT spring");
            Check(Lines(text).Any(t => t[0] == "element" && t[1] == "zeroLength" && t[3] == spring.AuxiliaryNodeId.ToString() && t[4] == spring.Id.ToString()),
                  "and a zeroLength from that node to the beam's");
            Check(model.HasOneWay, "one way, for Run Analysis's warning");

            Check(Solve(model, "bearing-one-way", out var log), "it solves");
            Check(Reaction(model, spring).Z == 0.0, $"the end lifts off: its row in Reaction Force is zero ({Reaction(model, spring).Z:G6})");
            Check(Near(Reaction(model, rigid[0]).Z, 20.0, 1e-6) && Near(Reaction(model, rigid[1]).Z, 20.0, 1e-6),
                  "and the loaded span is simply supported, wL/2 = 20 kN each");
            Check(Near(Reaction(model, rigid[2]).Z, 0.0, 1e-6), "the fix on the same node takes nothing vertical");

            var both = Bearing(SpringBehaviour.Both, out var springBoth, out var rigidBoth);
            Check(!both.HasOneWay, "a two-way spring is not one way");
            Check(Solve(both, "bearing-two-way", out log), "the same with a two-way spring solves");
            Check(Near(Reaction(both, springBoth).Z, -2.5, 1e-3), $"which holds the end down, -wL/16 ({Reaction(both, springBoth).Z:G6})");
        }
    }

    static void SpringsHeldRigidly()
    {
        Section("A spring along an axis a rigid support already holds");
        {
            Point3d a = new Point3d(0, 0, 0), b = new Point3d(3, 0, 0);
            var supports = new List<Support>
            {
                Held(a, true, true, true, true, true, true),
                Held(b, true, true, true, false, false, false),
                Support.WithSprings(new Plane(b, Vector3d.XAxis, Vector3d.YAxis), new[] { new SupportSpring(3, 1000.0) }),
            };
            var model = Assembled(new List<IElement> { Beam(a, b) }, supports);
            Check(model.Warnings.Count == 1 && model.Warnings[0].Contains("does nothing"), $"Assemble Model says so: {model.Warnings.FirstOrDefault()}");
        }
    }

    static void ExportedToFemDesign()
    {
        Section("Read for FEM-Design");
        {
            Point3d p1 = new Point3d(0, 0, 0), p2 = new Point3d(4, 0, 0), p3 = new Point3d(2, 0, 2);
            var bars = new List<IElement> { Bar(p1, p2, 0.01), Bar(p2, p3, 0.01, AxialBehaviour.Tension), Bar(p3, p1, 0.01, AxialBehaviour.Both, 10.0) };
            var supports = new List<Support>
            {
                Held(p1, true, true, true, false, false, false),
                Support.WithSprings(new Plane(p2, Vector3d.XAxis, Vector3d.YAxis), new[] { new SupportSpring(3, null, SpringBehaviour.CompressionOnly), new SupportSpring(2, 500.0) }),
            };
            var model = Assembled(bars, supports);

            var report = new Alpaca4d.Interop.ConversionReport();
            var export = Alpaca4d.FemDesign.ExportModelReader.Read(model, report);
            Check(export.Beams.All(b => b.IsTruss), "every truss goes out as a truss");
            Check(export.Beams.All(b => !b.ReleaseI.Ry && !b.ReleaseI.Rz && !b.ReleaseJ.Ry && !b.ReleaseJ.Rz && b.ReleaseI.Rx && !b.ReleaseJ.Rx),
                  "pinned at both ends, its twist held at one");
            var lines = report.Lines();
            Check(lines.Any(l => l.Contains("tension-only truss")), "the tension-only one says it carries both there");
            Check(lines.Any(l => l.Contains("prestress is not exported")), "the prestressed one says its prestress is left behind");
            var spring = export.Supports[1];
            Check(spring.Stiffness != null && double.IsPositiveInfinity(spring.Stiffness[2]) && spring.Stiffness[1] == 500.0 && spring.Stiffness[0] == 0.0,
                  "the spring support: rigid along Z, 500 along Y, free along X");
            Check(lines.Any(l => l.Contains("compression-only spring support")), "and says it holds both ways there");
            Check(export.Supports[0].Stiffness == null, "an ordinary support keeps its six states");
        }
    }
}
