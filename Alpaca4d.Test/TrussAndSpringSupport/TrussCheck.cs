// Truss and Spring Support, checked without Rhino.
//
//   write  checks the Tcl Truss, SupportSpring and Support write, and the buckling deck's linear
//          stand-ins for one-way materials, then writes decks around that same Tcl for OpenSees to
//          solve. Each deck checks itself against closed form and prints PASS or FAIL.
//   read   reads the results files those decks wrote back through Read.ForceBeamColumn, the reader
//          Beam Forces and View Results use.
//
// A truss here has no curve: RhinoCommon cannot make one outside Rhino, and nothing it writes needs
// one. What does need Rhino - a support's plane, a truss's curve, Assemble's node search - is checked
// by rhino.sh, which runs RhinoChecks.cs in a running Rhino 8.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

using Rhino.Geometry;

using Alpaca4d;
using Alpaca4d.Element;
using Alpaca4d.Generic;
using Alpaca4d.Material;
using Alpaca4d.Result;

public partial class TrussCheck
{
    static int fails = 0;

    static void Check(bool ok, string what)
    {
        if (!ok) fails++;
        Console.WriteLine($"  [{(ok ? "PASS" : "FAIL")}] {what}");
    }

    static void Section(string title)
    {
        Console.WriteLine();
        Console.WriteLine(title);
    }

    static bool Near(double a, double b, double tol = 1e-9) => Math.Abs(a - b) <= tol * Math.Max(1.0, Math.Abs(b));

    static string Failed(Action action)
    {
        try { action(); return null; }
        catch (Exception e) { return e.Message; }
    }

    static double Num(string token) => double.Parse(token, CultureInfo.InvariantCulture);

    static string[] Tokens(string line) => line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

    static List<string[]> Lines(string tcl) =>
        tcl.Split('\n').Select(line => Tokens(line)).Where(tokens => tokens.Length > 0).ToList();

    public const double E = 2.1e8;     // kN/m²
    public const double Rho = 7850.0;  // kg/m³

    public static UniaxialMaterialElastic Steel() => new UniaxialMaterialElastic("S355", E, E, 0.0, 8.1e7, 0.3, Rho);

    public static Alpaca4d.Section.ElasticSection Bar(double area) =>
        new Alpaca4d.Section.ElasticSection("bar", area, 1e-6, 1e-6, 1e-6, 1.0, 1.0, Steel());

    /// <summary>A plane on the world axes at a point, built field by field: Plane's constructors are native.</summary>
    public static Plane At(double x, double y, double z, bool upsideDown = false)
    {
        var plane = Plane.WorldXY;
        plane.Origin = new Point3d(x, y, z);
        if (upsideDown)
        {
            plane.YAxis = -Vector3d.YAxis;
            plane.ZAxis = -Vector3d.ZAxis;
        }
        return plane;
    }

    public static Truss Make(int id, int i, int j, double area, AxialBehaviour worksIn, double prestress = 0.0) =>
        new Truss(null, Bar(area), Vector3d.ZAxis, worksIn, prestress) { Id = id, INode = i, JNode = j };

    static void Main(string[] args)
    {
        var mode = args.Length > 0 ? args[0] : "write";
        var folder = args.Length > 1 ? args[1] : ".";

        if (mode == "read")
            ReadBack(folder);
        else
        {
            TrussTcl();
            SpringMaterials();
            BucklingStandIns();

            Section("Decks for OpenSees");
            Deck.Panels(Path.Combine(folder, "panel.tcl"));
            Deck.Cable(Path.Combine(folder, "cable.tcl"));
            Deck.Mass(Path.Combine(folder, "mass.tcl"));
            Deck.Mixed(Path.Combine(folder, "mixed.tcl"));
            Console.WriteLine("  [ .. ] panel, cable, mass, mixed written");
        }

        Console.WriteLine();
        Console.WriteLine(fails == 0 ? $"all {mode} checks passed" : $"{fails} {mode} check(s) failed");
        Environment.Exit(fails == 0 ? 0 : 1);
    }

    static void TrussTcl()
    {
        Section("Truss - what it writes");
        {
            var both = Make(7, 3, 4, 0.001, AxialBehaviour.Both);
            var lines = Lines(both.WriteTcl());
            Console.WriteLine("        " + both.WriteTcl().Replace("\n", "\n        ").TrimEnd());

            Check(lines.Count == 2, "a material and the element, nothing else");
            Check(lines[0].Length == 6 && lines[0][1] == "Elastic" && Near(Num(lines[0][3]), E) && Num(lines[0][4]) == 0.0 && Near(Num(lines[0][5]), E),
                  "both ways: Elastic, as stiff in compression as in tension");
            var element = lines[1];
            Check(element[0] == "element" && element[1] == "corotTruss", "always the corotational truss");
            Check(element[2] == "7" && element[3] == "3" && element[4] == "4", "tag and both nodes, in order");
            Check(Near(Num(element[5]), 0.001), "the section's area");
            Check(element[6] == lines[0][2], "the material it has just written");
            Check(element[7] == "-rho" && Near(Num(element[8]), ModelMass.FromKg(0.001 * Rho)), "its mass per length, in the model's mass unit");

            var tension = Lines(Make(1, 1, 2, 0.001, AxialBehaviour.Tension).WriteTcl());
            Check(tension[0][1] == "Elastic" && Near(Num(tension[0][3]), E) && Num(tension[0][5]) == 0.0,
                  "tension only: Elastic with nothing in compression, stiff at zero strain");

            var compression = Lines(Make(1, 1, 2, 0.001, AxialBehaviour.Compression).WriteTcl());
            Check(compression[0][1] == "ENT" && Near(Num(compression[0][3]), E),
                  "compression only: ENT, which is stiff at zero strain, not an Elastic with nothing in tension");

            var cable = Make(2, 1, 2, 1e-4, AxialBehaviour.Tension, 50.0);
            var prestressed = Lines(cable.WriteTcl());
            Check(prestressed.Count == 3 && prestressed[1][1] == "InitStressMaterial", "a prestress wraps the material in InitStressMaterial");
            Check(prestressed[1][3] == prestressed[0][2] && Near(Num(prestressed[1][4]), 50.0 / 1e-4),
                  "around the bar's own material, at the prestress over the area");
            Check(prestressed[2][6] == prestressed[1][2], "and the element uses the wrapped one");

            Check(Lines(Make(1, 1, 2, 0.001, AxialBehaviour.Both).WriteTcl())[0][2] != Lines(Make(2, 1, 2, 0.001, AxialBehaviour.Both).WriteTcl())[0][2],
                  "each bar its own material tag");
        }

        Section("Truss - what it refuses");
        {
            var slackInCompression = Make(1, 1, 2, 0.001, AxialBehaviour.Tension, -5.0);
            Check(slackInCompression.Problem() != null && Failed(() => slackInCompression.WriteTcl()) != null,
                  "a tension-only bar prestressed in compression");
            var pulledStrut = Make(1, 1, 2, 0.001, AxialBehaviour.Compression, 5.0);
            Check(pulledStrut.Problem() != null && Failed(() => pulledStrut.WriteTcl()) != null,
                  "a compression-only bar prestressed in tension");
            Check(Make(1, 1, 2, 0.001, AxialBehaviour.Both, -5.0).Problem() == null &&
                  Make(1, 1, 2, 0.001, AxialBehaviour.Compression, -5.0).Problem() == null,
                  "but a strut may start in compression, and a two-way bar either way");
            Check(Make(1, 1, 2, 0.0, AxialBehaviour.Both).Problem() != null, "a section of no area");
            Check(Failed(() => slackInCompression.ToString()) == null && slackInCompression.ToString().Contains("tension-only"),
                  "and a Panel shows what is wrong rather than throwing");
        }

        Section("Truss - as a beam to the rest of Alpaca4d");
        {
            var truss = Make(3, 1, 2, 0.002, AxialBehaviour.Tension, 20.0);
            truss.ElementId = "bracing";
            var resized = truss.WithSection(Bar(0.004)) as Truss;
            Check(resized != null && resized.WorksIn == AxialBehaviour.Tension && resized.Prestress == 20.0 && resized.ElementId == "bracing",
                  "WithSection, which Section Optimiser uses, keeps a truss a truss");
            Check(truss.Type == ElementType.Beam, "it goes where beams go");
            Check(truss.BeamIntegration.SectionLocations(3.0).Count == 2, "and reports at its two ends");
        }
    }

    static void SpringMaterials()
    {
        Section("Support Spring - its material");
        {
            var elastic = Lines(new SupportSpring(3, 1000.0).Material(1e9).WriteTcl())[0];
            Check(elastic[1] == "Elastic" && Num(elastic[3]) == 1000.0 && Num(elastic[5]) == 1000.0, "both ways: Elastic, the same each way");

            var bearing = Lines(new SupportSpring(3, 1000.0, SpringBehaviour.CompressionOnly).Material(1e9).WriteTcl())[0];
            Check(bearing[1] == "ENT" && Num(bearing[3]) == 1000.0, "compression only: ENT");

            var gap = Lines(new SupportSpring(3, 1000.0, SpringBehaviour.CompressionOnly, 0.5).Material(1e9).WriteTcl())[0];
            Check(gap[1] == "ElasticPPGap" && Num(gap[3]) == 1000.0 && Num(gap[4]) <= -1e20 && Num(gap[5]) == -0.5,
                  "with a gap: ElasticPPGap, closing in compression, never yielding");

            var rigid = Lines(new SupportSpring(3, null, SpringBehaviour.CompressionOnly).Material(4.2e9).WriteTcl())[0];
            Check(Num(rigid[3]) == 4.2e9, "no stiffness given: the rigid one Assemble hands it");

            Check(Failed(() => new SupportSpring(7, 1.0)) != null, "a direction outside 1-6");
            Check(Failed(() => new SupportSpring(3, 0.0)) != null, "a stiffness of zero - leave the direction out instead");
            Check(Failed(() => new SupportSpring(3, 1.0, SpringBehaviour.CompressionOnly, -0.1)) != null, "a negative gap");
            Check(Failed(() => new SupportSpring(3, 1.0, SpringBehaviour.Both, 0.1)) != null, "a gap on a two-way spring");
            Check(new SupportSpring(3, 1.0, SpringBehaviour.CompressionOnly).IsOneWay && !new SupportSpring(3, 1.0).IsOneWay,
                  "one way, for the analysis warning");
        }
    }

    /// <summary>A spring support's own Tcl. Rhino only: a support reads its plane through Plane.IsValid, which is native.</summary>
    static void SpringSupportTcl()
    {
        Section("Spring support - the support");
        {
            var support = Support.WithSprings(At(3, 0, 0), new[] { new SupportSpring(3, 500.0, SpringBehaviour.CompressionOnly) });

            Check(support.IsSpring && !support.Tx && !support.Ty && !support.Tz && !support.Rx && !support.Ry && !support.Rz,
                  "holds nothing rigidly");
            Check(support.Preset == null, "is not the Free preset, which its six states would otherwise match");
            Check(support.Description.StartsWith("Spring support") && support.Description.Contains("compression only"), "says what it is");
            Check(support.Geometry is string tag && tag == "comp z", "and Model View draws it as a tag");
            Check(support.IsOneWay, "one way");

            support.ndf = 6;
            Check(support.NeedsSpring, "always goes through a node of its own, skewed or not");
            Check(support.RestrainedDirections.SequenceEqual(new[] { 3 }), "acting in its springs' directions only");

            support.Id = 2;
            support.AuxiliaryNodeId = 10;
            support.SpringElementId = 20;
            support.SpringMaterials = support.RestrainedDirections.Select(d => support.SpringAlong(d).Material(1e9)).ToList();
            var tcl = support.WriteTcl();
            Console.WriteLine("        " + tcl.Replace("\n", "\n        ").TrimEnd());
            var lines = Lines(tcl);
            Check(lines.Any(l => l[0] == "uniaxialMaterial" && l[1] == "ENT"), "writes its spring's material");
            Check(lines.Any(l => string.Join(" ", l) == "node 10 3 0 0"), "a node of its own, where it stands");
            Check(lines.Any(l => string.Join(" ", l) == "fix 10 1 1 1 1 1 1"), "fixed outright");
            var zeroLength = lines.First(l => l[0] == "element");
            Check(string.Join(" ", zeroLength.Take(5)) == "element zeroLength 20 10 2", "a zeroLength from that node to the support's");
            Check(string.Join(" ", zeroLength.Skip(5).Take(4)) == $"-mat {support.SpringMaterials[0].Id} -dir 3", "with the spring's material in its direction");
            Check(!lines.Any(l => l[0] == "fix" && l[1] == "2"), "and never fixes the support's node itself");

            var mixed = Support.WithSprings(Plane.WorldXY, new[] { new SupportSpring(1, 1.0), new SupportSpring(5, 1.0) });
            mixed.ndf = 3;
            Check(mixed.RestrainedDirections.SequenceEqual(new[] { 1 }), "a solid's node has no rotation to spring");

            Check(Failed(() => Support.WithSprings(Plane.WorldXY, new[] { new SupportSpring(3, 1.0), new SupportSpring(3, 2.0) })) != null,
                  "two springs in one direction");
        }
    }

    static void BucklingStandIns()
    {
        Section("Linear Buckling - one-way materials made linear");
        {
            var tcl = new List<string>
            {
                "model BasicBuilder -ndm 3 -ndf 6\n",
                "uniaxialMaterial ENT 5 1000\n",
                "uniaxialMaterial Elastic 6 1000 0.0 0.0\n",
                "uniaxialMaterial ElasticPPGap 7 1000 -1e+30 -0.5\n",
                "uniaxialMaterial Elastic 9 1000 0.0 1000\n",
                "uniaxialMaterial InitStressMaterial 10 6 500\n",
            };
            var deck = Buckling.Rewrite(tcl, -1, 1);
            var text = string.Join("", deck.Model);
            Check(text.Contains("uniaxialMaterial Elastic 5 1000\n"), "ENT holds both ways, as stiff as it is at zero strain");
            Check(text.Contains("uniaxialMaterial Elastic 6 1000 0.0 1000\n"), "a tension-only Elastic too");
            Check(text.Contains("uniaxialMaterial Elastic 7 0.0\n"), "an open gap holds nothing");
            Check(text.Contains("uniaxialMaterial Elastic 9 1000 0.0 1000\n") && text.Contains("InitStressMaterial 10 6 500"),
                  "and the rest is left alone");
            Check(deck.OneWayMaterials == 3, $"three counted, for the remark (counted {deck.OneWayMaterials})");
        }
    }

    static void ReadBack(string folder)
    {
        Section("Read.ForceBeamColumn - trusses only");
        {
            // The panel deck's first case: tension-only diagonals, pushed along +X at the top.
            var beams = Deck.PanelBars(AxialBehaviour.Tension).Cast<IBeam>().ToList();
            var model = new Model
            {
                Recorders = new List<IRecorder> { new Recorder { FileName = Path.Combine(folder, "panel.mpco") } },
                Beams = beams,
            };
            int last = Read.StepCount(model) - 1;
            var read = Read.ForceBeamColumn(model, last);

            Check(read.n.Count == 6 && read.n.All(n => n.Count == 2), "two stations a bar, one bar a row");
            Check(Near(read.n[4][0], 12.5, 1e-3) && read.n[4][0] == read.n[4][1], $"the tensioned diagonal carries H / cos (got {read.n[4][0]:G6})");
            Check(read.n[5].All(n => n == 0.0), "the other one is slack");
            Check(read.mz.Concat(read.my).Concat(read.vy).Concat(read.vz).Concat(read.t).All(list => list.All(v => v == 0.0)),
                  "and no bar has a shear, a moment or a twist");
        }

        Section("Read.ForceBeamColumn - a beam and a truss");
        {
            var (beam, truss) = Deck.MixedElements();
            var model = new Model
            {
                Recorders = new List<IRecorder> { new Recorder { FileName = Path.Combine(folder, "mixed.mpco") } },
                Beams = new List<IBeam> { beam, truss },
            };
            var read = Read.ForceBeamColumn(model, Read.StepCount(model) - 1);
            var solver = File.ReadAllText(Path.Combine(folder, "mixed-truss.txt")).Trim().Split(' ').Select(Num).ToArray();

            Check(read.n[0].Count == 5, "the beam's five sections, from section.force");
            Check(read.n[1].Count == 2 && Near(read.n[1][0], solver[0], 1e-9), $"the truss's force from axialForce, as the solver gives it ({solver[0]:G6})");
            // Its local z is global Z, so the tip load bends it about local y: shear Vz.
            Check(Near(Math.Abs(read.vz[0][0]) + Math.Abs(read.n[1][0]), solver[1], 1e-6),
                  $"and between them they carry the 10 kN: {Math.Abs(read.vz[0][0]):G6} in the beam, {Math.Abs(read.n[1][0]):G6} in the prop");
        }

        Section("Read.ForceBeamColumn - a file without truss forces");
        {
            var model = new Model
            {
                Recorders = new List<IRecorder> { new Recorder { FileName = Path.Combine(folder, "no-axial.mpco") } },
                Beams = Deck.PanelBars(AxialBehaviour.Tension).Cast<IBeam>().ToList(),
            };
            var message = Failed(() => Read.ForceBeamColumn(model, 0));
            Check(message != null && message.Contains("axialForce"), "says which box to tick in the MPCO Recorder");
        }
    }
}

/// <summary>
/// Decks around the Tcl the classes write. Nodes, fixes, beams and loads are written out here; every
/// truss line and every spring support comes from WriteTcl.
/// </summary>
static class Deck
{
    const string Header =
        "proc check {what ok} { if {$ok} { puts \"  \\[PASS\\] $what\" } else { puts \"  \\[FAIL\\] $what\" } }\n" +
        "proc near {a b tol} { expr {abs($a - $b) <= $tol * max(1.0, abs($b))} }\n";

    const string Newton =
        "system BandGeneral\nnumberer RCM\nconstraints Plain\ntest NormDispIncr 1e-12 50\nalgorithm Newton\n";

    static string Puts(string text) => $"puts \"{text}\"\n";

    static void Write(string path, string text) => File.WriteAllText(path, Header + text);

    // ---------------------------------------------------------------- the X-braced panel

    /// <summary>
    /// A 4 x 3 panel in XZ: four two-way bars round it and two diagonals working as asked, tags 1-4
    /// round it and 5 (1 to 3), 6 (2 to 4) across it.
    /// </summary>
    public static List<Truss> PanelBars(AxialBehaviour diagonals)
    {
        return new List<Truss>
        {
            TrussCheck.Make(1, 1, 2, 0.01, AxialBehaviour.Both),
            TrussCheck.Make(2, 2, 3, 0.01, AxialBehaviour.Both),
            TrussCheck.Make(3, 3, 4, 0.01, AxialBehaviour.Both),
            TrussCheck.Make(4, 4, 1, 0.01, AxialBehaviour.Both),
            TrussCheck.Make(5, 1, 3, 0.001, diagonals),
            TrussCheck.Make(6, 2, 4, 0.001, diagonals),
        };
    }

    static string Panel(AxialBehaviour diagonals, double h, string recorder = "")
    {
        var tcl = new StringBuilder("wipe\nmodel BasicBuilder -ndm 3 -ndf 6\n");
        tcl.Append("node 1 0 0 0\nnode 2 4 0 0\nnode 3 4 0 3\nnode 4 0 0 3\n");
        // Pinned, a roller, and the top held out of the panel's plane; every rotation fixed, as
        // Assemble fixes them at a node only trusses reach.
        tcl.Append("fix 1 1 1 1 1 1 1\nfix 2 0 1 1 1 1 1\nfix 3 0 1 0 1 1 1\nfix 4 0 1 0 1 1 1\n");
        foreach (var bar in PanelBars(diagonals))
            tcl.Append(bar.WriteTcl());
        tcl.Append(recorder);
        tcl.Append($"timeSeries Linear 1\npattern Plain 1 1 {{\n\tload 4 {h} 0 0 0 0 0\n}}\n");
        tcl.Append(Newton + "integrator LoadControl 0.1\nanalysis Static\n");
        tcl.Append("check \"it solves\" [expr {[analyze 10] == 0}]\n");
        tcl.Append("set n5 [lindex [eleResponse 5 axialForce] 0]\nset n6 [lindex [eleResponse 6 axialForce] 0]\n");
        return tcl.ToString();
    }

    public static void Panels(string path)
    {
        var recorder = $"recorder mpco {Path.GetFileNameWithoutExtension(path)}.mpco -N displacement reactionForce -E section.force axialForce\n";
        var noAxial = "recorder mpco no-axial.mpco -N displacement -E section.force\n";
        var text = new StringBuilder();

        text.Append(Puts("A. An X-braced panel, H = 10 kN at the top: each diagonal carries H / cos = 12.5 kN alone"));
        text.Append(Panel(AxialBehaviour.Tension, 10.0, recorder + noAxial));
        text.Append("check \"tension only, pushed one way: the diagonal it pulls carries 12.5 ($n5)\" [near $n5 12.5 1e-3]\n");
        text.Append("check \"and the other one is slack ($n6)\" [expr {$n6 == 0.0}]\n");

        text.Append(Panel(AxialBehaviour.Tension, -10.0));
        text.Append("check \"pushed the other way, the other diagonal ($n6)\" [near $n6 12.5 1e-3]\n");
        text.Append("check \"and the first is slack ($n5)\" [expr {$n5 == 0.0}]\n");

        text.Append(Panel(AxialBehaviour.Compression, 10.0));
        text.Append("check \"compression only: the diagonal it pushes carries -12.5 ($n6)\" [near $n6 -12.5 1e-3]\n");
        text.Append("check \"and the other lets go ($n5)\" [expr {$n5 == 0.0}]\n");

        text.Append(Panel(AxialBehaviour.Both, 10.0));
        text.Append("check \"both ways: the two share it, n5 - n6 = 12.5 ([expr {$n5 - $n6}])\" [near [expr {$n5 - $n6}] 12.5 1e-3]\n");
        text.Append("check \"one in tension, one in compression\" [expr {$n5 > 0.0 && $n6 < 0.0}]\n");

        Write(path, text.ToString());
    }

    // ---------------------------------------------------------------- the prestressed cable

    public static void Cable(string path)
    {
        // Two bars of 5 m between fixed ends, 50 kN of prestress, a point load at the middle.
        const double area = 1e-4, prestress = 50.0, half = 5.0;
        double ea = TrussCheck.E * area;

        string Model(double load) =>
            "wipe\nmodel BasicBuilder -ndm 3 -ndf 6\nnode 1 0 0 0\nnode 2 5 0 0\nnode 3 10 0 0\n" +
            "fix 1 1 1 1 1 1 1\nfix 3 1 1 1 1 1 1\nfix 2 0 1 0 1 1 1\n" +
            TrussCheck.Make(1, 1, 2, area, AxialBehaviour.Tension, prestress).WriteTcl() +
            TrussCheck.Make(2, 2, 3, area, AxialBehaviour.Tension, prestress).WriteTcl() +
            $"timeSeries Linear 1\npattern Plain 1 1 {{\n\tload 2 0 0 {-load} 0 0 0\n}}\n" +
            Newton + "integrator LoadControl 0.05\nanalysis Static\n" +
            "check \"it solves\" [expr {[analyze 20] == 0}]\n" +
            "reactions\n";

        var text = new StringBuilder();
        text.Append(Puts("B. A cable prestressed to 50 kN between two fixed ends"));
        text.Append(Model(0.0));
        text.Append("set n [lindex [eleResponse 1 axialForce] 0]\n");
        text.Append($"check \"unloaded, it carries its prestress ($n)\" [near $n {prestress} 1e-9]\n");
        text.Append($"check \"and its ends hold it ([nodeReaction 1 1])\" [near [nodeReaction 1 1] {-prestress} 1e-9]\n");

        text.Append(Model(2.0));
        text.Append("set d [expr {-[nodeDisp 2 3]}]\nset ln [expr {sqrt(25.0 + $d * $d)}]\n");
        text.Append("set n [lindex [eleResponse 1 axialForce] 0]\n");
        text.Append($"check \"2 kN at the middle: it sags ($d m), held by its prestress\" [expr {{$d > 0.0}}]\n");
        text.Append($"check \"its force is the prestress and its stretch ($n)\" [near $n [expr {{{prestress} + {ea} * ($ln - {half}) / {half}}}] 1e-6]\n");
        text.Append("check \"and the middle node is in equilibrium where it has moved to\" [near [expr {2.0 * $n * $d / $ln}] 2.0 1e-6]\n");
        text.Append($"check \"close to the taut-string sag F a / 2P = 0.1 m\" [near $d 0.1 0.05]\n");
        Write(path, text.ToString());
    }

    // ---------------------------------------------------------------- the mass

    public static void Mass(string path)
    {
        // A bar fixed at one end and free along itself at the other: half its mass at the free end.
        const double area = 0.01, length = 2.0;
        double k = TrussCheck.E * area / length;
        double m = ModelMass.FromKg(TrussCheck.Rho * area) * length / 2.0;

        var text = new StringBuilder();
        text.Append(Puts("C. The mass a truss writes: a bar fixed at one end, free to stretch at the other"));
        text.Append("wipe\nmodel BasicBuilder -ndm 3 -ndf 6\nnode 1 0 0 0\nnode 2 2 0 0\nfix 1 1 1 1 1 1 1\nfix 2 0 1 1 1 1 1\n");
        text.Append(TrussCheck.Make(1, 1, 2, area, AxialBehaviour.Both).WriteTcl());
        text.Append("set lambda [lindex [eigen -fullGenLapack 1] 0]\n");
        text.Append($"check \"omega squared is EA/L over half its mass ($lambda)\" [near $lambda {k / m} 1e-9]\n");
        Write(path, text.ToString());
    }

    // ---------------------------------------------------------------- springs

    const double I = 1e-5, A = 0.01, G = 8.1e7, J = 1e-5;
    static double EI => TrussCheck.E * I;

    /// <summary>A spring support on a node, its tags handed out the way Assemble hands them out.</summary>
    static string Spring(Support support, int node, int auxiliary, int element, double rigid = 1e12)
    {
        support.ndf = 6;
        support.Id = node;
        support.AuxiliaryNodeId = auxiliary;
        support.SpringElementId = element;
        support.SpringMaterials = support.RestrainedDirections.Select(d => support.SpringAlong(d).Material(rigid)).ToList();
        return support.WriteTcl();
    }

    static Support At(double x, double y, double z, params SupportSpring[] springs) =>
        Support.WithSprings(TrussCheck.At(x, y, z), springs);

    /// <summary>A cantilever along X, 3 m, fixed at node 1, its tip node 2 held in the XZ plane.</summary>
    static string Cantilever =>
        "wipe\nmodel BasicBuilder -ndm 3 -ndf 6\nnode 1 0 0 0\nnode 2 3 0 0\nfix 1 1 1 1 1 1 1\nfix 2 0 1 0 1 0 1\n" +
        "geomTransf Linear 1 0 0 1\n" +
        $"element elasticBeamColumn 1 1 2 {A} {TrussCheck.E} {G} {J} {I} {I} 1\n";

    static string Load(int node, double fz, double fx = 0.0) =>
        $"timeSeries Linear 1\npattern Plain 1 1 {{\n\tload {node} {fx} 0 {fz} 0 0 0\n}}\n";

    static string Solve(int steps) =>
        Newton + $"integrator LoadControl {1.0 / steps}\nanalysis Static\ncheck \"it solves\" [expr {{[analyze {steps}] == 0}}]\nreactions\n";

    public static void Springs(string path)
    {
        double kb = 3.0 * EI / 27.0;
        var text = new StringBuilder();

        text.Append(Puts("D. A cantilever tip on a spring as stiff as the cantilever: 10 kN down, half each"));
        text.Append(Cantilever + Spring(At(3, 0, 0, new SupportSpring(3, kb)), 2, 3, 2) + Load(2, -10.0) + Solve(1));
        text.Append($"check \"the tip drops P / (k + 3EI/L^3) ([nodeDisp 2 3])\" [near [nodeDisp 2 3] {-10.0 / (2 * kb)} 1e-9]\n");
        text.Append("check \"the reaction is read on the spring's node, pushing up 5 kN ([nodeReaction 3 3])\" [near [nodeReaction 3 3] 5.0 1e-9]\n");
        text.Append("check \"the tip's own node takes none of it ([nodeReaction 2 3])\" [near [nodeReaction 2 3] 0.0 1e-9]\n");

        double kr = 3.0 * EI / 3.0;
        text.Append(Puts("E. A column pinned at its base, a rotational spring holding it, 1 kN across its top"));
        text.Append("wipe\nmodel BasicBuilder -ndm 3 -ndf 6\nnode 1 0 0 0\nnode 2 0 0 3\nfix 1 1 1 1 1 0 1\nfix 2 0 1 0 1 0 1\n" +
                    "geomTransf Linear 1 1 0 0\n" +
                    $"element elasticBeamColumn 1 1 2 {A} {TrussCheck.E} {G} {J} {I} {I} 1\n" +
                    Spring(At(0, 0, 0, new SupportSpring(5, kr)), 1, 3, 2) + Load(2, 0.0, 1.0) + Solve(1));
        text.Append($"check \"the top moves PL^3/3EI + PL^2/k ([nodeDisp 2 1])\" [near [nodeDisp 2 1] {27.0 / (3 * EI) + 9.0 / kr} 1e-9]\n");
        text.Append("check \"the spring takes the base moment, 3 kNm ([nodeReaction 3 5])\" [near [expr {abs([nodeReaction 3 5])}] 3.0 1e-9]\n");
        text.Append("check \"beside a fix on the same node, which takes the shear ([nodeReaction 1 1])\" [near [nodeReaction 1 1] -1.0 1e-9]\n");

        text.Append(Puts("F. Turned over - its plane's Z pointing down - a compression-only spring holds in tension"));
        var flipped = TrussCheck.At(3, 0, 0, upsideDown: true);
        Support Tie() => Support.WithSprings(flipped, new[] { new SupportSpring(3, kb, SpringBehaviour.CompressionOnly) });
        text.Append(Cantilever + Spring(Tie(), 2, 3, 2) + Load(2, 10.0) + Solve(5));
        text.Append($"check \"pulled up, it holds: half each ([nodeDisp 2 3])\" [near [nodeDisp 2 3] {10.0 / (2 * kb)} 1e-9]\n");
        text.Append(Cantilever + Spring(Tie(), 2, 3, 2) + Load(2, -10.0) + Solve(5));
        text.Append($"check \"pushed down, it lets go ([nodeDisp 2 3])\" [near [nodeDisp 2 3] {-10.0 / kb} 1e-9]\n");
        text.Append("check \"and carries nothing ([nodeReaction 3 3])\" [expr {[nodeReaction 3 3] == 0.0}]\n");

        Write(path, text.ToString());
    }

    public static void Bearing(string path)
    {
        // Two spans of 4 m, the first loaded with 10 kN/m. Pinned at 1, a roller at 2, and at 3 a fix
        // holding it in its plane beside a spring support holding it down.
        const double w = 10.0, span = 4.0, inertia = 1e-4;
        double rigid = 1e6 * 12.0 * TrussCheck.E * inertia / (span * span * span);

        string Model(SpringBehaviour behaviour) =>
            "wipe\nmodel BasicBuilder -ndm 3 -ndf 6\nnode 1 0 0 0\nnode 2 4 0 0\nnode 3 8 0 0\n" +
            "fix 1 1 1 1 1 0 1\nfix 2 0 1 1 1 0 1\nfix 3 0 1 0 1 0 1\n" +
            "geomTransf Linear 1 0 0 1\n" +
            $"element elasticBeamColumn 1 1 2 {A} {TrussCheck.E} {G} {J} {inertia} {inertia} 1\n" +
            $"element elasticBeamColumn 2 2 3 {A} {TrussCheck.E} {G} {J} {inertia} {inertia} 1\n" +
            Spring(At(8, 0, 0, new SupportSpring(3, null, behaviour)), 3, 4, 3, rigid) +
            $"timeSeries Linear 1\npattern Plain 1 1 {{\n\teleLoad -ele 1 -type -beamUniform 0 {-w}\n}}\n" +
            Solve(10);

        var text = new StringBuilder();
        text.Append(Puts("G. A continuous beam, the end of its unloaded span on a rigid spring support"));
        text.Append(Model(SpringBehaviour.Both));
        text.Append($"check \"both ways: the end is pulled down, -wL/16 ([nodeReaction 4 3])\" [near [nodeReaction 4 3] {-w * span / 16} 1e-4]\n");
        text.Append($"check \"7wL/16 at the first support ([nodeReaction 1 3])\" [near [nodeReaction 1 3] {7 * w * span / 16} 1e-4]\n");
        text.Append($"check \"5wL/8 at the middle one ([nodeReaction 2 3])\" [near [nodeReaction 2 3] {5 * w * span / 8} 1e-4]\n");

        text.Append(Model(SpringBehaviour.CompressionOnly));
        text.Append("check \"compression only: the end lifts and the bearing carries nothing ([nodeReaction 4 3])\" [expr {[nodeReaction 4 3] == 0.0}]\n");
        text.Append("check \"so the end rises ([nodeDisp 3 3])\" [expr {[nodeDisp 3 3] > 0.0}]\n");
        text.Append($"check \"and the loaded span is simply supported, wL/2 each ([nodeReaction 1 3], [nodeReaction 2 3])\" " +
                    $"[expr {{[near [nodeReaction 1 3] {w * span / 2} 1e-9] && [near [nodeReaction 2 3] {w * span / 2} 1e-9]}}]\n");
        text.Append("check \"the fix beside the spring holds it in its plane and takes no vertical load ([nodeReaction 3 3])\" [near [nodeReaction 3 3] 0.0 1e-9]\n");
        Write(path, text.ToString());
    }

    public static void Gap(string path)
    {
        // A cantilever tip 1 cm above a spring ten times as stiff as the cantilever.
        double kb = 3.0 * EI / 27.0, kc = 10.0 * kb, gap = 0.01;
        string Model(double load) =>
            Cantilever + Spring(At(3, 0, 0, new SupportSpring(3, kc, SpringBehaviour.CompressionOnly, gap)), 2, 3, 2) +
            Load(2, -load) + Solve(20);

        var text = new StringBuilder();
        text.Append(Puts("H. A cantilever tip a gap of 1 cm above a stiff spring"));
        double small = 0.5 * gap * kb;
        text.Append(Model(small));
        text.Append($"check \"a load that moves it half the gap: free ([nodeDisp 2 3])\" [near [nodeDisp 2 3] {-small / kb} 1e-9]\n");
        text.Append("check \"and the spring carries nothing ([nodeReaction 3 3])\" [expr {[nodeReaction 3 3] == 0.0}]\n");

        double large = 2.0 * gap * kb;
        double drop = (large + kc * gap) / (kb + kc);
        text.Append(Model(large));
        text.Append($"check \"twice as much: it lands, and drops (P + kc g)/(kb + kc) ([nodeDisp 2 3])\" [near [nodeDisp 2 3] {-drop} 1e-9]\n");
        text.Append($"check \"the spring carries kc (d - g) ([nodeReaction 3 3])\" [near [nodeReaction 3 3] {kc * (drop - gap)} 1e-9]\n");
        Write(path, text.ToString());
    }

    // ---------------------------------------------------------------- a beam and a truss, for the reader

    public static (ForceBeamColumn beam, Truss truss) MixedElements()
    {
        var material = TrussCheck.Steel();
        var section = new Alpaca4d.Section.ElasticSection("beam", A, I, I, J, 1.0, 1.0, material);
        var beam = new ForceBeamColumn(null, section, new GeomTransf(GeomTransfType.Linear, null, Vector3d.ZAxis)) { Id = 1, INode = 1, JNode = 2 };
        beam.SetTags();
        var truss = TrussCheck.Make(2, 2, 3, 2e-5, AxialBehaviour.Both);
        return (beam, truss);
    }

    public static void Mixed(string path)
    {
        // A cantilever whose tip is propped by a bar down to the ground; 10 kN down at the tip.
        var (beam, truss) = MixedElements();
        var text = new StringBuilder();
        text.Append(Puts("I. A cantilever propped at its tip by a truss, for the reader"));
        text.Append("wipe\nmodel BasicBuilder -ndm 3 -ndf 6\nnode 1 0 0 0\nnode 2 3 0 0\nnode 3 3 0 -2\n" +
                    "fix 1 1 1 1 1 1 1\nfix 2 0 1 0 1 0 1\nfix 3 1 1 1 1 1 1\n");
        text.Append(beam.Section.Material.WriteTcl() + beam.Section.WriteTcl() + beam.WriteTcl() + truss.WriteTcl());
        text.Append($"recorder mpco {Path.GetFileNameWithoutExtension(path)}.mpco -N displacement -E section.force axialForce\n");
        text.Append(Load(2, -10.0) + Solve(2));
        text.Append("set f [open mixed-truss.txt w]; puts $f \"[lindex [eleResponse 2 axialForce] 0] 10.0\"; close $f\n");
        text.Append("check \"the prop is pushed ([lindex [eleResponse 2 axialForce] 0])\" [expr {[lindex [eleResponse 2 axialForce] 0] < 0.0}]\n");
        Write(path, text.ToString());
    }
}
