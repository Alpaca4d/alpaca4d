// Deck errors: OpenSees exits 0 when a deck stops on a Tcl error, as when it runs to its end, so
// Model.RunOpenSees reads Tcl's trace of the error off the log and hands on the 1 OpenSees meant.
// Every deck here runs through Model.RunOpenSees, on each OpenSees given. See README.md.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Alpaca4d;
using Alpaca4d.Material;
using Alpaca4d.Section;
using Rhino.Geometry;

class DeckErrors
{
    static int fails = 0;

    static void Check(string what, bool ok, string detail = "")
    {
        Console.WriteLine($"  [{(ok ? "PASS" : "FAIL")}] {what}{(detail == "" ? "" : "  " + detail)}");
        if (!ok) fails++;
    }

    // ------------------------------------------------------------------ the trace, without OpenSees

    // Each as OpenSees 3.8.0 printed it, banner left out.
    const string UnknownCommand = "invalid command name \"nonsense\"\n    while executing\n\"nonsense 1 2 3\"\n    (file \"bad.tcl\" line 5)";
    const string OpenSeesError = "checking library: OPS_NotAnElement\nERROR -- element of type NotAnElement not known\n\n" +
                                 "    while executing\n\"element NotAnElement 1 1 2 3 4 5\"\n    (file \"unknown.tcl\" line 3)";
    const string InSourcedFile = "    while executing\n\"node 3 1 0\"\n    (file \"part.tcl\" line 2)\n    invoked from within\n" +
                                 "\"source part.tcl\"\n    (file \"main.tcl\" line 3)";
    const string InProcedure = "    while executing\n\"uniaxialMaterial Nope 1 2\"\n    (procedure \"build\" line 3)\n    invoked from within\n" +
                               "\"build\"\n    (file \"proc.tcl\" line 6)";
    const string LongPath = "invalid command name \"nonsense\"\n    while executing\n\"nonsense\"\n" +
                            "    (file \"/private/tmp/a_very_long_folder_name_to_see_whether_tcl_truncates_the_path_of_the_file_in_its_trace/and_another_level_of_fol...\" line 3)";
    const string Windows = "invalid command name \"nonsense\"\r\n    while executing\r\n\"nonsense\"\r\n" +
                           "    (file \"C:\\Users\\me\\Documents\\Alpaca4d\\AlpacaModel.tcl\" line 12)\r\n";
    const string Diverged = "WARNING: CTestNormDispIncr::test() - failed to converge \nafter: 10 iterations  current Norm: 0.99\n" +
                            "StaticAnalysis::analyze() - the Algorithm failed at step: 0 with domain at load factor 1\n" +
                            "OpenSees > analyze failed, returned: -3 error flag\nALPACA_ANALYZE_RESULT -3";

    static void Traces()
    {
        Console.WriteLine("The trace of an error, read off the log");
        Check("an unknown command", Model.StoppedOnTclError(UnknownCommand));
        Check("an OpenSees command that refuses, with its own message", Model.StoppedOnTclError(OpenSeesError));
        Check("in a sourced file", Model.StoppedOnTclError(InSourcedFile));
        Check("in a procedure", Model.StoppedOnTclError(InProcedure));
        Check("a path too long for Tcl, cut short with ...", Model.StoppedOnTclError(LongPath));
        Check("a Windows path, CRLF line ends", Model.StoppedOnTclError(Windows));
        Check("not an analysis that did not converge: analyze returns, the deck goes on", !Model.StoppedOnTclError(Diverged));
        Check("not the same words printed by the deck at the start of a line", !Model.StoppedOnTclError("(file \"bad.tcl\" line 5)"));
        Check("not an empty log, nor none", !Model.StoppedOnTclError("") && !Model.StoppedOnTclError(null));
    }

    // ------------------------------------------------------------------ decks, through RunOpenSees

    const string Cantilever = @"wipe
model BasicBuilder -ndm 3 -ndf 6
node 1 0 0 0
node 2 1 0 0
fix 1 1 1 1 1 1 1
geomTransf Linear 1 0 0 1
element elasticBeamColumn 1 1 2 0.01 2.1e8 8.0769e7 1.406e-5 8.3333e-6 8.3333e-6 1
";

    const string Static = @"pattern Plain 1 Linear { load 2 0 0 -1 0 0 0 }
system BandGeneral
numberer Plain
constraints Plain
integrator LoadControl 1.0
test NormDispIncr 1e-8 10
algorithm Newton
analysis Static
set alpacaAnalyzeOk [analyze 1]
puts ""ALPACA_ANALYZE_RESULT $alpacaAnalyzeOk""
wipe
";

    // A spring that yields at 0.01 under a force of 1: no step converges.
    const string Yielding = @"wipe
model BasicBuilder -ndm 1 -ndf 1
node 1 0
node 2 1
fix 1 1
uniaxialMaterial ElasticPP 1 1.0 0.01
element zeroLength 1 1 2 -mat 1 -dir 1
pattern Plain 1 Linear { load 2 1.0 }
system BandGeneral
numberer Plain
constraints Plain
integrator LoadControl 1.0
test NormDispIncr 1e-8 10
algorithm Newton
analysis Static
set alpacaAnalyzeOk [analyze 1]
puts ""ALPACA_ANALYZE_RESULT $alpacaAnalyzeOk""
wipe
";

    static (string output, string error, int exitCode) Run(string folder, string name, string deck)
    {
        string path = Path.Combine(folder, name);
        File.WriteAllText(path, deck);
        return new Model { FileName = path }.RunOpenSees();
    }

    /// <summary>Why Run Analysis fails a run, or null: Alpaca4d.Gh.AnalysisOutcome, compiled in from its source.</summary>
    static string Verdict((string output, string error, int exitCode) run) =>
        Alpaca4d.Gh.AnalysisOutcome.Failure(run.output, run.error, run.exitCode);

    /// <summary>What OpenSees itself exits with, beside what RunOpenSees hands on.</summary>
    static int Raw(string openSees, string path)
    {
        var start = new System.Diagnostics.ProcessStartInfo(openSees, "\"" + path + "\"")
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        using (var p = System.Diagnostics.Process.Start(start))
        {
            var stdout = p.StandardOutput.ReadToEndAsync();
            p.StandardError.ReadToEnd();
            p.WaitForExit();
            stdout.Wait();
            return p.ExitCode;
        }
    }

    static void Decks(string openSees, string folder)
    {
        Console.WriteLine($"Decks through Model.RunOpenSees, on {openSees}");
        AlpacaSettings.OpenSeesPath = openSees;

        var solved = Run(folder, "solves.tcl", Cantilever + Static);
        Check("a deck that solves: 0", solved.exitCode == 0 && solved.error.Contains("ALPACA_ANALYZE_RESULT 0"), $"exit {solved.exitCode}");

        var diverged = Run(folder, "diverges.tcl", Yielding);
        Check("a deck whose analysis does not converge runs to its end: 0, and says -3 itself",
              diverged.exitCode == 0 && diverged.error.Contains("ALPACA_ANALYZE_RESULT -3"), $"exit {diverged.exitCode}");

        var unknown = Run(folder, "unknown_command.tcl", Cantilever + "nonsense 1 2 3\n" + Static);
        int raw = Raw(openSees, Path.Combine(folder, "unknown_command.tcl"));
        Check("a command Tcl does not know: 1", unknown.exitCode == 1 && !unknown.error.Contains("ALPACA_ANALYZE_RESULT"),
              $"exit {unknown.exitCode}, OpenSees itself {raw}");

        // What Run Analysis and Section Optimiser make of the three: OpenSees prints the result line
        // after analyze to its error stream, which was not read for it.
        Check("Run Analysis takes the deck that solves for solved", Verdict(solved) == null, Verdict(solved) ?? "");
        Check("Run Analysis says the one that does not converge did not",
              (Verdict(diverged) ?? "").Contains("did not converge"), Verdict(diverged) ?? "taken for solved");
        Check("Run Analysis says OpenSees stopped on the unknown command",
              (Verdict(unknown) ?? "").Contains("exited with an error"), Verdict(unknown) ?? "taken for solved");

        var element = Run(folder, "unknown_element.tcl", Cantilever + "element NotAnElement 2 1 2 1\n" + Static);
        Check("an element OpenSees does not know: 1", element.exitCode == 1, $"exit {element.exitCode}");

        var material = Run(folder, "missing_material.tcl", Cantilever.Replace("element elasticBeamColumn 1 1 2 0.01 2.1e8 8.0769e7 1.406e-5 8.3333e-6 8.3333e-6 1",
                                                                              "element truss 1 1 2 0.01 7") + Static);
        Check("a material never defined: 1", material.exitCode == 1, $"exit {material.exitCode}");

        File.WriteAllText(Path.Combine(folder, "part.tcl"), "node 3 2 0 0\nnode 4 3 0\n");
        var sourced = Run(folder, "sources.tcl", Cantilever + $"source {{{Path.Combine(folder, "part.tcl")}}}\n" + Static);
        Check("an error in a file the deck sources: 1", sourced.exitCode == 1, $"exit {sourced.exitCode}");

        var procedure = Run(folder, "procedure.tcl", "proc build {} {\n  model BasicBuilder -ndm 3 -ndf 3\n  uniaxialMaterial Nope 1 2\n}\nbuild\n");
        Check("an error in a procedure: 1", procedure.exitCode == 1, $"exit {procedure.exitCode}");

        var caught = Run(folder, "caught.tcl", Cantilever + "catch {nonsense}\n" + Static);
        Check("an error the deck catches itself, as Alpaca's own decks do: 0", caught.exitCode == 0 && caught.error.Contains("ALPACA_ANALYZE_RESULT 0"),
              $"exit {caught.exitCode}");

        var exit3 = Run(folder, "exit3.tcl", "model BasicBuilder -ndm 3 -ndf 3\nexit 3\n");
        Check("a deck that exits with a code of its own keeps it", exit3.exitCode == 3, $"exit {exit3.exitCode}");

        string deep = Path.Combine(folder, new string('a', 90), "a folder with spaces " + new string('b', 60));
        Directory.CreateDirectory(deep);
        var far = Run(deep, "AlpacaModel.tcl", Cantilever + "nonsense\n");
        Check("a deck down a path longer than Tcl prints, with spaces: 1", far.exitCode == 1, $"exit {far.exitCode}, {Path.Combine(deep, "AlpacaModel.tcl").Length} characters");
    }

    // ------------------------------------------------------------------ Moment Curvature

    /// <summary>Runs a deck of Moment Curvature's; <paramref name="broken"/> puts an error at the top of the last one.</summary>
    static (string output, string error, int exitCode) RunMomentCurvature(string deck, bool broken)
    {
        if (broken && Path.GetFileName(deck) == MomentCurvatureAnalysis.DeckFileName)
            File.WriteAllText(deck, "nonsense\n" + File.ReadAllText(deck));
        return new Model { FileName = deck }.RunOpenSees();
    }

    static void MomentCurvature(string folder)
    {
        Console.WriteLine("Moment Curvature, which read a non-zero exit as the axial step failing");

        // 300 x 500 mm, C30/37 and B500 design values, kN and m: coarse, as only the deck matters.
        var concrete = new Concrete01("C30/37 design", -17000, -17000, -0.002, -0.0035, false);
        var steel = new Steel01("B500 design", 434783, 200e6, 0.0);
        var layout = new SectionLayout();
        var fibres = new List<PointFiber>();
        double b = 0.30, h = 0.50;
        int ny = 6, nz = 10;
        for (int i = 0; i < ny; i++)
            for (int j = 0; j < nz; j++)
            {
                double y = -b / 2 + (i + 0.5) * b / ny, z = -h / 2 + (j + 0.5) * h / nz, area = b * h / (ny * nz);
                layout.AddFibre(y, z, area, concrete);
                fibres.Add(new PointFiber(new Point3d(y, z, 0), area, concrete));
            }
        foreach (var y in new[] { -0.1, 0.1 })
            foreach (var z in new[] { -0.2, 0.2 })
            {
                double area = Math.PI * 0.016 * 0.016 / 4;
                layout.AddBar(y, z, area, steel);
                fibres.Add(new PointFiber(new Point3d(y, z, 0), area, steel));
            }
        var section = new FiberSection(fibres, new List<Layer>(), new List<Patch>(), 1e6);

        // Its recorders write under the folder it runs in, which is OpenSees' working directory.
        try
        {
            var r = MomentCurvatureAnalysis.Run(section, layout, -500, 0, 20, null, new DomainSettings(), true,
                                                folder, folder, "mc_materials", deck => RunMomentCurvature(deck, false));
            Check("the deck as written solves", r.N.Count > 0, $"{r.N.Count} increments");
        }
        catch (Exception ex)
        {
            Check("the deck as written solves", false, ex.Message);
        }

        string message = null;
        try
        {
            MomentCurvatureAnalysis.Run(section, layout, -500, 0, 20, null, new DomainSettings(), true,
                                        folder, folder, "mc_materials", deck => RunMomentCurvature(deck, true));
        }
        catch (InvalidOperationException ex)
        {
            message = ex.Message;
        }
        Check("a deck that stops on an error says so, not that the axial step failed",
              message != null && message.Contains("stopped on an error") && !message.Contains("axial"), message ?? "no exception");
    }

    static int Main(string[] args)
    {
        string folder = Directory.GetCurrentDirectory();

        // The plugin's settings, the OpenSees path among them, are kept here and not in the user's.
        Application.UserFolder = folder;
        Application.OpenSeesTimeout = TimeSpan.FromMinutes(2);

        Traces();
        foreach (var openSees in args.Where(File.Exists))
            Decks(openSees, folder);
        if (args.Length > 0 && File.Exists(args[0]))
        {
            AlpacaSettings.OpenSeesPath = args[0];
            MomentCurvature(folder);
        }
        if (!args.Any(File.Exists))
            Console.WriteLine("note: no OpenSees given, only the trace is checked");

        Console.WriteLine(fails == 0 ? "All passed." : $"{fails} failed.");
        return fails == 0 ? 0 : 1;
    }
}
