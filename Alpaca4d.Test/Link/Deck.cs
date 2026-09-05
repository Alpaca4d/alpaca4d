// The decks the Link test leaves for OpenSees.
//
// Every line that comes from a class under test is written by that class - the element lines are
// Link.WriteTcl and ZeroLengthSpring.WriteTcl, the tie is EqualDOF.WriteTcl - so what the solver
// reads is the string Alpaca4d produces rather than a transcription of it. Everything around
// them, the nodes and the shells and the analysis, is scaffolding.
//
// The decks check themselves: each prints one PASS or FAIL line per number it knows the answer
// to, and run.sh looks for FAIL.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

using Rhino.Geometry;

using Alpaca4d.Constraints;
using Alpaca4d.Element;
using Alpaca4d.Generic;
using Alpaca4d.Material;

static class Deck
{
    static string N(double value)
    {
        return value.ToString("R", CultureInfo.InvariantCulture);
    }

    static UniaxialMaterialElastic Spring(int id, double k)
    {
        var material = new UniaxialMaterialElastic(null, k, k, 0.0, k, 0.0, null);
        material.Id = id;
        return material;
    }

    /// <summary>The tcl that checks one number, so every deck reports the same way.</summary>
    static string Expect(string what, string got, string wanted, double tolerance)
    {
        // The wanted value is a literal, so it is not bracketed - square brackets are Tcl's
        // command substitution, and "[0.01]" asks it to run a command called 0.01.
        return $"check \"{what}\" [{got}] {wanted} {N(tolerance)}\n";
    }

    const string Checker = @"
proc check {what got wanted tol} {
  set denom [expr {abs($wanted) > 1e-30 ? abs($wanted) : 1.0}]
  set err [expr {abs($got - $wanted) / $denom}]
  if {$err < $tol} {
    puts [format ""  \[PASS\] %-46s %14.8g"" $what $got]
  } else {
    puts [format ""  \[FAIL\] %-46s %14.8g  wanted %14.8g  (%.2g off)"" $what $got $wanted $err]
  }
}
proc solve {} {
  system FullGeneral
  numberer Plain
  constraints Transformation
  integrator LoadControl 1
  test NormDispIncr 1e-12 40 0
  algorithm Newton
  analysis Static
  return [analyze 1]
}
";

    /// <summary>
    /// Deck A. A link is the only thing holding a node, so each of its six springs can be read off
    /// one at a time.
    ///
    /// The frame is pinned down by handing the link Plane.WorldXY on a link that runs along global
    /// X, so local 1, 2 and 3 are global X, Y and Z and the answer does not depend on which way the
    /// default cross axis happens to point.
    ///
    /// Along the link the answer is P/k and nothing else. Across it there is more: the shear spring
    /// acts at mid-length, so the shear force also turns the node, and the far end moves by the
    /// spring stretch plus half the length times that rotation. Both terms are checked, because the
    /// second is the term that makes two offset shells act compositely and it would be easy to lose.
    /// </summary>
    public static void Directions(string path)
    {
        double[] k = { 1000.0, 500.0, 250.0, 111.0, 222.0, 333.0 };
        const double load = 10.0;

        var materials = k.Select((stiffness, index) => Spring(index + 1, stiffness)).ToList();

        var link = new Alpaca4d.Element.Link(
            new Line(new Point3d(0, 0, 0), new Point3d(1, 0, 0)),
            materials.Cast<IUniaxialMaterial>().ToList(),
            new List<int> { 1, 2, 3, 4, 5, 6 },
            Plane.WorldXY);
        link.Id = 1;
        link.INode = 1;
        link.JNode = 2;

        var deck = new StringBuilder();
        deck.Append("# Deck A - one twoNodeLink, its six springs read off one at a time.\n");
        deck.Append(Checker);
        deck.Append("puts \"\\nA. Link, one spring per direction\"\n");

        // Along the link: the spring stretch, and nothing else.
        deck.Append(Case(link, materials, "1 0 0 0 0 0", load));
        deck.Append(Expect("axial, P/k", "nodeDisp 2 1", N(load / k[0]), 1e-9));

        // Across it: the stretch plus half the length times the rotation the shear force causes.
        // Shear along local y bends about local z, and along local z bends about local y.
        deck.Append(Case(link, materials, "0 1 0 0 0 0", load));
        deck.Append(Expect("shear on local y, P/k + rotation", "nodeDisp 2 2",
                           N(load / k[1] + (load * 0.5 / k[5]) * 0.5), 1e-9));

        deck.Append(Case(link, materials, "0 0 1 0 0 0", load));
        deck.Append(Expect("shear on local z, P/k + rotation", "nodeDisp 2 3",
                           N(load / k[2] + (load * 0.5 / k[4]) * 0.5), 1e-9));

        // And the three rotational springs, read off a moment.
        deck.Append(Case(link, materials, "0 0 0 1 0 0", load));
        deck.Append(Expect("torsion, M/k", "nodeDisp 2 4", N(load / k[3]), 1e-9));

        File.WriteAllText(path, deck.ToString());
    }

    static string Case(Alpaca4d.Element.Link link, List<UniaxialMaterialElastic> materials, string load, double magnitude)
    {
        var deck = new StringBuilder();
        deck.Append("wipe\nmodel BasicBuilder -ndm 3 -ndf 6\n");
        deck.Append("node 1 0 0 0\nnode 2 1 0 0\n");
        deck.Append("fix 1 1 1 1 1 1 1\n");
        foreach (var material in materials) deck.Append(material.WriteTcl());
        deck.Append(link.WriteTcl());
        deck.Append("timeSeries Linear 1\n");
        deck.Append($"pattern Plain 1 1 {{ load 2 {string.Join(" ", load.Split(' ').Select(v => N(double.Parse(v, CultureInfo.InvariantCulture) * magnitude)))} }}\n");
        deck.Append("if {[solve] != 0} { puts \"  [FAIL] deck A did not solve\" }\n");

        return deck.ToString();
    }

    /// <summary>
    /// Deck B. A stiff cantilever whose built-in end is not built in at all but held by a spring to
    /// ground, so the whole tip movement is the spring giving way.
    ///
    /// The held node drops by P/k and turns by PL/k, and the tip picks up both: P/k + (PL/k)L.
    /// The beam is made stiff enough that its own bending is far below the tolerance.
    /// </summary>
    public static void SpringSupport(string path)
    {
        const double k = 1.0e6;
        const double load = -1000.0;
        const double length = 1.0;

        var material = Spring(1, k);
        var spring = new ZeroLengthSpring(new Point3d(0, 0, 0),
            Enumerable.Repeat((IUniaxialMaterial)material, 6).ToList(),
            new List<int> { 1, 2, 3, 4, 5, 6 });
        spring.Id = 2;
        spring.NodeId = 1;
        spring.GroundNodeId = 3;

        var deck = new StringBuilder();
        deck.Append("# Deck B - a cantilever held by a zeroLength spring instead of a support.\n");
        deck.Append(Checker);
        deck.Append("wipe\nmodel BasicBuilder -ndm 3 -ndf 6\n");
        deck.Append("node 1 0 0 0\nnode 2 1 0 0\n");
        deck.Append("geomTransf Linear 1 0 0 1\n");
        // Stiff enough that the beam's own bending is three orders below the spring's give.
        deck.Append("element elasticBeamColumn 1 1 2 1.0 2.1e14 8.0e13 1.0 1.0 1.0 1\n");
        deck.Append(material.WriteTcl());
        deck.Append(spring.WriteTcl());
        deck.Append("timeSeries Linear 1\n");
        deck.Append($"pattern Plain 1 1 {{ load 2 0 0 {N(load)} 0 0 0 }}\n");
        deck.Append("puts \"\\nB. Zero Length Spring holding a cantilever\"\n");
        deck.Append("if {[solve] != 0} { puts \"  [FAIL] deck B did not solve\" }\n");
        deck.Append(Expect("held node drops by P/k", "nodeDisp 1 3", N(load / k), 1e-6));
        deck.Append(Expect("held node turns by PL/k", "expr abs([nodeDisp 1 5])", N(Math.Abs(load * length / k)), 1e-6));
        deck.Append(Expect("tip picks up both", "nodeDisp 2 3", N(load / k + (load * length / k) * length), 1e-6));

        File.WriteAllText(path, deck.ToString());
    }

    /// <summary>
    /// Deck C. Two cantilevers of different span side by side, their tips tied in the vertical
    /// translation only. The load goes on one tip and both tips move together, so the two share it
    /// in proportion to their stiffness and the deflection is P over the sum of the two.
    ///
    /// The spans differ on purpose. A cantilever under a tip force turns by three halves of its
    /// deflection over its span whatever its section, so two equal spans would end up with equal
    /// tip rotations no matter what the tie did, and a check that they are free would pass without
    /// meaning anything. Different spans make the rotations genuinely different, and their ratio is
    /// the ratio of the spans - which is only true if the tie left the rotations alone.
    /// </summary>
    public static void Tie(string path)
    {
        const double e = 2.1e11;
        const double inertia = 1.0e-5;
        const double longSpan = 1.0;
        const double shortSpan = 0.6;
        const double load = -1000.0;

        double stiffLong = 3.0 * e * inertia / (longSpan * longSpan * longSpan);
        double stiffShort = 3.0 * e * inertia / (shortSpan * shortSpan * shortSpan);
        double together = load / (stiffLong + stiffShort);

        var tie = new EqualDOF(new Point3d(longSpan, 0, 0), new Point3d(shortSpan, 1, 0),
                               false, false, true, false, false, false);
        tie.MasterNodeId = 2;
        tie.SlaveNodeId = 4;

        var deck = new StringBuilder();
        deck.Append("# Deck C - two cantilevers of different span, tips tied in the vertical translation.\n");
        deck.Append(Checker);
        deck.Append("wipe\nmodel BasicBuilder -ndm 3 -ndf 6\n");
        deck.Append($"node 1 0 0 0\nnode 2 {N(longSpan)} 0 0\nnode 3 0 1 0\nnode 4 {N(shortSpan)} 1 0\n");
        deck.Append("fix 1 1 1 1 1 1 1\nfix 3 1 1 1 1 1 1\n");
        deck.Append("geomTransf Linear 1 0 0 1\n");
        deck.Append($"element elasticBeamColumn 1 1 2 1.0 {N(e)} 8.0e10 1.0 {N(inertia)} {N(inertia)} 1\n");
        deck.Append($"element elasticBeamColumn 2 3 4 1.0 {N(e)} 8.0e10 1.0 {N(inertia)} {N(inertia)} 1\n");
        deck.Append(tie.WriteTcl());
        deck.Append("timeSeries Linear 1\n");
        deck.Append($"pattern Plain 1 1 {{ load 2 0 0 {N(load)} 0 0 0 }}\n");
        deck.Append("puts \"\\nC. Equal DOF tying two cantilever tips\"\n");
        deck.Append("if {[solve] != 0} { puts \"  [FAIL] deck C did not solve\" }\n");
        deck.Append(Expect("loaded tip, P over the two stiffnesses", "nodeDisp 2 3", N(together), 1e-6));
        deck.Append(Expect("tied tip follows it exactly", "nodeDisp 4 3", N(together), 1e-9));
        // Only the vertical translation was tied, so each beam keeps its own tip rotation and the
        // two come out in the ratio of the spans.
        deck.Append(Expect("rotations left free, in the ratio of the spans",
                           "expr [nodeDisp 2 5] / [nodeDisp 4 5]", N(shortSpan / longSpan), 1e-6));

        File.WriteAllText(path, deck.ToString());
    }

    /// <summary>
    /// Deck D. Laminated glass, which is what these elements were added for: two panes of glass
    /// with an interlayer between them that is soft in shear, so the two panes slide over each
    /// other and the pair is stiffer than two loose panes and softer than one thick one.
    ///
    /// Two shells at their own mid-plane heights, one link per node pair carrying the interlayer:
    /// direction 1 normal to the panes and stiff, 2 and 3 the shear the interlayer really provides,
    /// k = G A / t over the area of glass that node speaks for.
    ///
    /// The whole model is swept over the interlayer's shear modulus and checked at both ends,
    /// because at both ends the answer is known and does not depend on the interlayer at all:
    ///
    ///   G to zero      the panes are loose. The same shell mesh, one pane carrying half the load,
    ///                  is the same structure, so the two have to agree to the last few digits.
    ///   G to infinity  the panes are one section. Plane sections stay plane across both, and the
    ///                  stiffness is the full composite one, 2(I + A d squared).
    ///
    /// Between them the deck only insists the answer moves the right way. That is where a real
    /// interlayer sits and no formula gives it exactly - which is the reason for modelling it.
    /// </summary>
    public static void Laminate(string path)
    {
        const double length = 1.0;          // cantilever span
        const double width = 0.1;           // strip width
        const int spans = 10;               // elements along the span
        const double glass = 0.006;         // one pane
        const double interlayer = 0.00152;  // PVB
        const double eGlass = 70.0e9;
        const double load = -10.0;          // total tip load

        double offset = 0.5 * (glass + interlayer);   // mid-plane of a pane, either side of the middle
        double dx = length / spans;

        // Node tags: bottom pane 1..2(spans+1), top pane after it, two rows across the width.
        int perPane = 2 * (spans + 1);
        Func<int, int, int, int> tag = (pane, station, row) => pane * perPane + row * (spans + 1) + station + 1;

        var deck = new StringBuilder();
        deck.Append("# Deck D - laminated glass: two shells joined by twoNodeLink shear springs.\n");
        deck.Append(Checker);
        deck.Append("puts \"\\nD. Laminated glass, two panes and an interlayer\"\n");

        // --- the parts of the deck that do not change over the sweep ---
        var nodes = new StringBuilder();
        for (int pane = 0; pane < 2; pane++)
        {
            double z = pane == 0 ? -offset : offset;
            for (int row = 0; row < 2; row++)
            {
                for (int station = 0; station <= spans; station++)
                    nodes.Append($"node {tag(pane, station, row)} {N(station * dx)} {N(row * width)} {N(z)}\n");
            }
        }

        var shells = new StringBuilder();
        int element = 1;
        for (int pane = 0; pane < 2; pane++)
        {
            for (int station = 0; station < spans; station++)
            {
                shells.Append($"element ASDShellQ4 {element++} {tag(pane, station, 0)} {tag(pane, station + 1, 0)}" +
                              $" {tag(pane, station + 1, 1)} {tag(pane, station, 1)} 1\n");
            }
        }

        var fixes = new StringBuilder();
        for (int pane = 0; pane < 2; pane++)
            for (int row = 0; row < 2; row++)
                fixes.Append($"fix {tag(pane, 0, row)} 1 1 1 1 1 1\n");

        // The glass either side of each node, which is the area its spring speaks for.
        Func<int, double> tributary = station =>
            (station == 0 || station == spans ? 0.5 * dx : dx) * 0.5 * width;

        // --- one run of the sweep ---
        Action<double, string> run = (shear, label) =>
        {
            deck.Append("wipe\nmodel BasicBuilder -ndm 3 -ndf 6\n");
            deck.Append(nodes);
            deck.Append($"nDMaterial ElasticIsotropic 1 {N(eGlass)} 0.23 2500\n");
            deck.Append($"section PlateFiber 1 1 {N(glass)}\n");
            deck.Append(shells);
            deck.Append(fixes);

            int springTag = 100;
            int elementTag = 1000;
            for (int row = 0; row < 2; row++)
            {
                for (int station = 0; station <= spans; station++)
                {
                    double area = tributary(station);
                    var slip = Spring(springTag++, shear * area / interlayer);
                    // Normal to the panes the interlayer is nearly incompressible next to its own
                    // shear, so that spring is only there to keep the two panes apart.
                    var normal = Spring(springTag++, 1000.0 * eGlass * area / interlayer);

                    var link = new Alpaca4d.Element.Link(
                        new Line(new Point3d(station * dx, row * width, -offset),
                                 new Point3d(station * dx, row * width, offset)),
                        new List<IUniaxialMaterial> { normal, slip, slip },
                        new List<int> { 1, 2, 3 });
                    link.Id = elementTag++;
                    link.INode = tag(0, station, row);
                    link.JNode = tag(1, station, row);

                    deck.Append(slip.WriteTcl());
                    deck.Append(normal.WriteTcl());
                    deck.Append(link.WriteTcl());
                }
            }

            deck.Append("timeSeries Linear 1\npattern Plain 1 1 {\n");
            for (int pane = 0; pane < 2; pane++)
                for (int row = 0; row < 2; row++)
                    deck.Append($"  load {tag(pane, spans, row)} 0 0 {N(load / 4.0)} 0 0 0\n");
            deck.Append("}\n");
            deck.Append($"if {{[solve] != 0}} {{ puts \"  [FAIL] laminate did not solve at {label}\" }}\n");
            deck.Append($"set d_{label} [expr abs([nodeDisp {tag(0, spans, 0)} 3])]\n");
        };

        run(1.0, "loose");         // G = 1 Pa, as good as nothing
        run(1.0067e6, "pvb");      // PVB at room temperature, a few minutes of load
        run(1.0e14, "rigid");      // as good as one section

        // The loose end, against the same mesh with one pane carrying half the load. Same shell,
        // same mesh, same everything - so this is the structure the loose laminate really is.
        deck.Append("wipe\nmodel BasicBuilder -ndm 3 -ndf 6\n");
        for (int row = 0; row < 2; row++)
            for (int station = 0; station <= spans; station++)
                deck.Append($"node {tag(0, station, row)} {N(station * dx)} {N(row * width)} 0\n");
        deck.Append($"nDMaterial ElasticIsotropic 1 {N(eGlass)} 0.23 2500\n");
        deck.Append($"section PlateFiber 1 1 {N(glass)}\n");
        for (int station = 0; station < spans; station++)
        {
            deck.Append($"element ASDShellQ4 {station + 1} {tag(0, station, 0)} {tag(0, station + 1, 0)}" +
                        $" {tag(0, station + 1, 1)} {tag(0, station, 1)} 1\n");
        }
        for (int row = 0; row < 2; row++)
            deck.Append($"fix {tag(0, 0, row)} 1 1 1 1 1 1\n");
        deck.Append("timeSeries Linear 1\npattern Plain 1 1 {\n");
        for (int row = 0; row < 2; row++)
            deck.Append($"  load {tag(0, spans, row)} 0 0 {N(load / 4.0)} 0 0 0\n");
        deck.Append("}\n");
        deck.Append("if {[solve] != 0} { puts \"  [FAIL] the single pane did not solve\" }\n");
        deck.Append($"set d_one [expr abs([nodeDisp {tag(0, spans, 0)} 3])]\n");

        // The rigid end, against full composite action: both panes bending about the middle.
        double own = width * glass * glass * glass / 12.0;
        double composite = 2.0 * (own + width * glass * offset * offset);
        double rigid = Math.Abs(load) * length * length * length / (3.0 * eGlass * composite);

        deck.Append("check \"loose end is two panes on their own\" $d_loose $d_one 1e-4\n");
        deck.Append($"check \"rigid end is one composite section\" $d_rigid {N(rigid)} 0.02\n");
        deck.Append("check \"PVB sits between the two\" [expr {$d_loose > $d_pvb && $d_pvb > $d_rigid}] 1 1e-9\n");
        deck.Append("puts [format \"         loose %.6f   PVB %.6f   rigid %.6f\" $d_loose $d_pvb $d_rigid]\n");
        deck.Append("puts [format \"         PVB is %.0f%% of the way from loose to rigid\" " +
                    "[expr {100.0*($d_loose-$d_pvb)/($d_loose-$d_rigid)}]]\n");

        File.WriteAllText(path, deck.ToString());
    }
}
