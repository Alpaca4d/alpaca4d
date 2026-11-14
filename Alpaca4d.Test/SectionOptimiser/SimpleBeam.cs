// A simply supported 8 m beam, drawn as 20 elements, under 20 kN/m and its own weight, sized by the
// section optimiser from the IPE300 it starts as.
//
// Unlike SectionOptimiser.cs, every round is a real analysis: the design the optimiser proposes is
// written out as an OpenSees deck - each element with its own section, its self-weight from its own
// area - solved, and its section forces read back. What stands in for Alpaca4d is only the writing of
// the deck, which Assemble does inside Rhino. Units kN and m.
//
//     mono SimpleBeam.exe <OpenSees> <folder>
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

using Alpaca4d;
using Alpaca4d.Design;
using Alpaca4d.Generic;
using Alpaca4d.Material;
using Alpaca4d.Section;

class SimpleBeamDemo
{
    const double Span = 8.0, Load = 20.0, Rho = 7850.0;
    const int Count = 20;

    static int fails = 0;
    static string openSees, folder;
    static int decks = 0;

    static void Check(bool ok, string what)
    {
        if (!ok) fails++;
        Console.WriteLine($"  [{(ok ? "PASS" : "FAIL")}] {what}");
    }

    static string N(double x) => x.ToString("R", CultureInfo.InvariantCulture);

    class StubBeam : IBeam
    {
        public Rhino.Geometry.Curve Curve { get; set; }
        public Alpaca4d.Element.GeomTransf GeomTransf { get; set; }
        public IUniaxialSection Section { get; set; }
        public IIntegration BeamIntegration { get; set; }
        public int? INode { get; set; }
        public int? JNode { get; set; }
        public System.Drawing.Color Color { get; set; }
        public Alpaca4d.Element.ElementType Type => Alpaca4d.Element.ElementType.Beam;
        public int? Id { get; set; }
        public string ElementId { get; set; }
        public void SetTags() { }
        public void SetTopologyRTree(Model model) { }
        public string WriteTcl() => "";
        public IBeam WithSection(IUniaxialSection section) =>
            new StubBeam { Id = Id, ElementId = ElementId, Section = section, BeamIntegration = new Alpaca4d.BeamIntegration.NewtonContes(section, 5) };
    }

    static Model Design(IReadOnlyList<IUniaxialSection> sections)
    {
        return new Model
        {
            Beams = sections.Select((s, i) => (IBeam)new StubBeam
            {
                Id = i + 1,
                ElementId = $"Beam_{i + 1}",
                Section = s,
                BeamIntegration = new Alpaca4d.BeamIntegration.NewtonContes(s, 5),
            }).ToList(),
            IsStatic = true,
        };
    }

    static SteelSection Steel(IUniaxialSection section) =>
        SteelSection.Of(section, (SteelGrade)DesignGrades.Of(section.Material), Fabrication.Rolled);

    /// <summary>
    /// Writes the design as a deck, solves it and points the model at its recorder. The beam runs
    /// along x; vecxz (0, -1, 0) puts local y up, so the load and the self-weight act along -y and the
    /// I bends about local z, its major axis, as Alpaca4d's I-sections have their depth along local y.
    /// </summary>
    static Model Analyse(Model design)
    {
        string recorder = Path.Combine(folder, $"beam_{decks}.mpco");
        string deck = Path.Combine(folder, $"beam_{decks}.tcl");
        decks++;

        var tcl = new StringBuilder();
        tcl.AppendLine("wipe");
        tcl.AppendLine("model BasicBuilder -ndm 3 -ndf 6");
        for (int i = 0; i <= Count; i++)
            tcl.AppendLine($"node {i + 1} {N(Span * i / Count)} 0.0 0.0");
        tcl.AppendLine("fix 1 1 1 1 1 0 0");
        tcl.AppendLine($"fix {Count + 1} 0 1 1 1 0 0");

        var weights = new double[Count];
        for (int e = 0; e < Count; e++)
        {
            var s = Steel(design.Beams[e].Section);
            weights[e] = s.A * Rho * 9.81 / 1000.0;
            tcl.AppendLine($"section Elastic {e + 1} {N(s.E)} {N(s.A)} {N(s.Iz)} {N(s.Iy)} {N(s.G)} {N(s.It)} {N(s.AvY / s.A)} {N(s.AvZ / s.A)}");
            tcl.AppendLine($"geomTransf Linear {e + 1} 0 -1 0");
            tcl.AppendLine($"element forceBeamColumn {e + 1} {e + 1} {e + 2} {e + 1} NewtonCotes {e + 1} 5");
        }

        tcl.AppendLine("timeSeries Linear 1");
        tcl.AppendLine("pattern Plain 1 1 {");
        for (int e = 0; e < Count; e++)
            tcl.AppendLine($"    eleLoad -ele {e + 1} -type -beamUniform {N(-(Load + weights[e]))} 0.0");
        tcl.AppendLine("}");
        tcl.AppendLine($"recorder mpco \"{recorder}\" -E section.force");
        tcl.AppendLine("system BandGeneral");
        tcl.AppendLine("numberer RCM");
        tcl.AppendLine("constraints Plain");
        tcl.AppendLine("test NormDispIncr 1.0e-12 20");
        tcl.AppendLine("algorithm Newton");
        tcl.AppendLine("integrator LoadControl 1.0");
        tcl.AppendLine("analysis Static");
        tcl.AppendLine("analyze 1");
        tcl.AppendLine("wipe");
        File.WriteAllText(deck, tcl.ToString());

        using (var process = Process.Start(new ProcessStartInfo(openSees, $"\"{deck}\"")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        }))
        {
            process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0 || !File.Exists(recorder))
                throw new Exception($"OpenSees did not solve {deck}");
        }

        design.Recorders = new List<IRecorder> { new Recorder { FileName = recorder } };
        return design;
    }

    static double Mass(IEnumerable<IUniaxialSection> sections) => sections.Sum(s => Steel(s).A * Rho * Span / Count);

    static SizingResult Size(string title, IReadOnlyList<DesignGroup> groups, double lengthLT, out List<string> messages,
                             bool fast = true, bool quiet = false)
    {
        var material = new UniaxialMaterialElastic("S355", 210e6, 210e6, 0.0, 80.77e6, 0.3, Rho) { Grade = SteelGrade.Find("S355") };
        var start = SteelCatalogue.Create("I", "IPE300", material, 0.0);
        var model = Design(Enumerable.Repeat(start, Count).ToList());

        messages = new List<string>();
        var errors = new List<string>();
        var resolved = SectionSizing.Resolve(model.Beams, groups.Select(g => Ipes(g, start)).ToList(), errors, messages, null, messages, SteelCatalogue.FamilyOf);
        if (errors.Count > 0) throw new Exception(string.Join("\n", errors));

        // Each element is a piece of one 8 m member: it buckles over the whole span, and is held
        // against lateral-torsional buckling every lengthLT.
        var members = Enumerable.Range(0, Count).Select(_ => new SteelMember(Span / Count)
        {
            BucklingLengthY = Span,
            BucklingLengthZ = Span,
            LateralTorsionalLength = lengthLT,
        }).ToList();

        var options = new SizingOptions { Target = 1.0, MaxRounds = 10, Members = members, Design = new DesignSettings { Report = false },
                                          Screen = fast, Parallel = fast };
        var result = SectionSizing.Run(model, resolved, options, Analyse, null, (original, sections) => Design(sections));
        if (quiet)
            return result;

        Console.WriteLine($"\n{title}");
        foreach (var round in result.Rounds)
            Console.WriteLine($"  round {round.Iteration}: largest utilisation {round.MaxUtilisation:0.000}, {round.Changed} element(s) changed{(round.OnwardOnly ? ", onward only" : "")}; " +
                              $"analysis {round.AnalysisSeconds * 1000:0} ms, checks {round.CheckSeconds * 1000:0} ms ({round.Checks} whole, {round.Screened} ruled out on the bound)");
        Console.WriteLine($"  converged: {result.Converged}, {Mass(result.Sections):0} kg against {Mass(Enumerable.Repeat(start, Count)):0} kg as IPE300 throughout");
        Console.WriteLine("  element   x mid [m]   section   utilisation   governing");
        for (int e = 0; e < Count; e++)
            Console.WriteLine($"  {e + 1,7}   {Span * (e + 0.5) / Count,9:0.0}   {result.Sections[e],-7}   {result.Utilisation[e],11:0.000}   {result.Governing[e]}");
        return result;
    }

    /// <summary>A group with no sections of its own takes the IPE family, so the one-section case needs no list typed out.</summary>
    static DesignGroup Ipes(DesignGroup group, IUniaxialSection start)
    {
        if (group.Sections.Count == 0)
            group.Sections = SteelCatalogue.FamilyOf(start).Sections;
        return group;
    }

    static int Rank(IUniaxialSection section) => int.Parse(section.ToString().Substring(3), CultureInfo.InvariantCulture);

    static void Main(string[] args)
    {
        openSees = args[0];
        folder = args.Length > 1 ? args[1] : ".";

        // By hand: M = (q + g) L² / 8 at midspan, with an IPE270's 0.35 kN/m about 163 kNm, against
        // Wpl,y fy = 484 cm³ x 355 N/mm² = 172 kNm for a rolled IPE270 - a little less for Alpaca4d's,
        // which has no root radius. So the middle wants an IPE270 or an IPE300, and the ends, where
        // the moment falls to nothing, far less: there the shear, (q + g) L / 2 = 81 kN, governs.
        double qMid = Load + 0.35;
        Console.WriteLine($"Simply supported, {Span} m in {Count} elements, {Load} kN/m and self-weight, S355, starting as IPE300.");
        Console.WriteLine($"By hand: M at midspan about {qMid * Span * Span / 8:0} kNm, V at the supports about {qMid * Span / 2:0} kN.");

        List<string> messages;
        var held = Size("Top flange held against lateral-torsional buckling (L_LT = each element's own 0.4 m)", new DesignGroup[0], Span / Count, out messages);
        Check(held.Converged, "converges");
        Check(held.Sections.All(s => s.ToString().StartsWith("IPE")), "never out of the IPEs");
        Check(held.Utilisation.All(u => u.HasValue && u.Value <= 1.0), "every element at or below 1");
        Check(Enumerable.Range(0, Count).All(e => held.Sections[e].ToString() == held.Sections[Count - 1 - e].ToString()), "symmetric about midspan");
        int middle = Rank(held.Sections[Count / 2]);
        Check(middle == 270 || middle == 300, $"midspan on an IPE270 or IPE300, as by hand: {held.Sections[Count / 2]}");
        Check(Rank(held.Sections[0]) < middle, $"the ends lighter: {held.Sections[0]}");

        // The same without screening or parallel checking: what they save, and that they change nothing.
        var slow = Size("", new DesignGroup[0], Span / Count, out messages, fast: false, quiet: true);
        Check(slow.Sections.Select(x => x.ToString()).SequenceEqual(held.Sections.Select(x => x.ToString())) && slow.Utilisation.SequenceEqual(held.Utilisation),
              "the same sections and utilisations without screening and parallel checking");
        // Timed once both have run, so that neither pays for the first compile of the check.
        var slowAgain = Size("", new DesignGroup[0], Span / Count, out messages, fast: false, quiet: true);
        var fastAgain = Size("", new DesignGroup[0], Span / Count, out messages, fast: true, quiet: true);
        Console.WriteLine($"  without them: {slowAgain.Rounds.Sum(r => r.Checks)} whole checks, {slowAgain.Rounds.Sum(r => r.CheckSeconds) * 1000:0.0} ms checking; " +
                          $"with them: {fastAgain.Rounds.Sum(r => r.Checks)} whole checks, {fastAgain.Rounds.Sum(r => r.CheckSeconds) * 1000:0.0} ms");

        var free = Size("No lateral restraint between the supports (L_LT = 8 m, the peak moment taken over all of it)", new DesignGroup[0], Span, out messages);
        Check(free.Converged && free.Utilisation.All(u => u <= 1.0), "converges, every element at or below 1");
        Check(Rank(free.Sections[Count / 2]) > middle, $"lateral-torsional buckling asks for more at midspan: {free.Sections[Count / 2]}");

        var whole = new DesignGroup { Name = "Beam", ElementIds = new List<string> { "Beam_*" } };
        var one = Size("One section for all 20 elements (a design group), top flange held", new[] { whole }, Span / Count, out messages);
        Check(one.Converged && one.Sections.All(s => s == one.Sections[0]), $"one section throughout: {one.Sections[0]}");
        Check(Rank(one.Sections[0]) == middle, "the one midspan needs");

        Console.WriteLine(fails == 0 ? "\nAll passed." : $"\n{fails} failed.");
        Environment.Exit(fails == 0 ? 0 : 1);
    }
}
