// The MPCO recorder: what it writes, and what the solver makes of it.
//
// Three things are being guarded:
//
//   1. Leaving the Recorders input of Assemble Model empty has to go on recording exactly what it
//      always did. The default recorder lines are compared, word for word, with the lines the
//      recorder wrote before it could be chosen by hand.
//
//   2. The MPCO Recorder component saves its tick boxes by position in Recorder.NodeResultTypes
//      and Recorder.ElementResultTypes. Reorder either list and every saved file ticks a different
//      result, with nothing to say so - so both are pinned here, and may only grow at the end.
//
//   3. The boxes are the results a result component reads, so each has to land where that component
//      looks for it. MPCO refuses the whole recorder over one nodal name it does not know, and skips
//      an element that does not answer to an element name without a word - so with every box
//      ticked, the solver has to accept the line, every nodal result has to come back, and each
//      element result has to land on the elements that are read for it. That includes a beam with
//      hinges, whose HingeRadau integration puts it in a section.force group of its own.
//
// "write" writes all.tcl out of model.tcl, the recorder lines and analysis.tcl; run.sh solves it;
// "read" checks the files that come back. Without OpenSees only the first two checks run.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using PureHDF;

using Alpaca4d;
using Alpaca4d.Generic;
using Alpaca4d.Result;

class RecorderTest
{
    static int fails = 0;

    static void Check(bool ok, string what)
    {
        if (!ok) fails++;
        Console.WriteLine($"  [{(ok ? "PASS" : "FAIL")}] {what}");
    }

    static string Words(string tcl) => Regex.Replace(tcl.Trim(), @"\s+", " ");

    static int Main(string[] args)
    {
        string mode = args.Length > 0 ? args[0] : "write";
        if (mode == "write") WriteDeck();
        else ReadFiles();

        Console.WriteLine();
        Console.WriteLine(fails == 0 ? "all recorder checks passed" : $"{fails} recorder check(s) failed");
        return fails == 0 ? 0 : 1;
    }

    // ---------------------------------------------------------------- the lines

    static void WriteDeck()
    {
        Console.WriteLine("\nThe recorders Run Analysis picks by itself, against the lines it wrote before");

        // Copied from the recorder as it was before it took a list of results: the same words,
        // in the same order. It wrote two spaces after -N and -E, which Tcl does not see.
        Check(Words(Recorder.MpcoStatic("recorder.mpco").WriteTcl()) ==
              "recorder mpco recorder.mpco -N displacement rotation reactionForce reactionMoment " +
              "-E stresses section.force section.fiber.stress", "static");
        Check(Words(Recorder.MpcoTransient("recorder.mpco").WriteTcl()) ==
              "recorder mpco recorder.mpco -N displacement rotation velocity angularVelocity acceleration " +
              "angularAcceleration reactionForce reactionMoment -E stresses section.force section.fiber.stress", "transient");
        Check(Words(Recorder.MpcoEigen("recorder_eigen.mpco").WriteTcl()) ==
              "recorder mpco recorder_eigen.mpco -N modesOfVibration modesOfVibrationRotational -E", "eigen");

        // Run Analysis now hands every recorder a full path, one file per branch it solves. Tcl
        // would split one with a space and eat its backslashes, so it goes in braces.
        Check(Words(Recorder.MpcoStatic("/Users/Jane Doe/AlpacaResults/recorder_0.mpco").WriteTcl())
              .StartsWith("recorder mpco {/Users/Jane Doe/AlpacaResults/recorder_0.mpco} -N "), "a path with a space, in braces");
        Check(Words(Recorder.MpcoStatic(@"C:\Users\Jane\AlpacaResults\recorder_0.mpco").WriteTcl())
              .StartsWith(@"recorder mpco {C:\Users\Jane\AlpacaResults\recorder_0.mpco} -N "), "a Windows path, in braces");

        Console.WriteLine("\nThe tick boxes, by the position they are saved at");

        Check(Recorder.NodeResultTypes.SequenceEqual(new[]
        {
            "displacement", "rotation", "velocity", "angularVelocity", "acceleration", "angularAcceleration",
            "reactionForce", "reactionMoment",
        }), "nodal results, in their saved order");
        Check(Recorder.ElementResultTypes.SequenceEqual(new[]
        {
            "stresses", "section.force", "section.fiber.stress",
        }), "element results, in their saved order");

        // The boxes start ticked at these, so they have to be boxes.
        Check(Recorder.TransientNodeResults.All(Recorder.NodeResultTypes.Contains), "the transient set is all tick boxes");
        Check(Recorder.DefaultElementResults.All(Recorder.ElementResultTypes.Contains), "the default element set is all tick boxes");

        Console.WriteLine("\nThe name a result component asks for, against the name on the tick box");

        foreach (ResultType type in Enum.GetValues(typeof(ResultType)))
        {
            string name = Read.RecorderName(type);
            bool known = Recorder.NodeResultTypes.Contains(name) ||
                         Recorder.MpcoEigen("x").NodeResults.Contains(name);
            Check(known, $"{type} is asked for as {name}");
        }

        // Every box ticked, and a second file with next to nothing in it for the reader to find
        // missing.
        var all = new Recorder("all.mpco", Recorder.NodeResultTypes, Recorder.ElementResultTypes);
        var some = new Recorder("some.mpco", new[] { "displacement" }, new[] { "section.force" });

        File.WriteAllText("all.tcl",
            File.ReadAllText("model.tcl") + all.WriteTcl() + some.WriteTcl() + File.ReadAllText("analysis.tcl"));
        Console.WriteLine("\n  wrote all.tcl");
    }

    // ---------------------------------------------------------------- the files

    const string Results = "/MODEL_STAGE[1]/RESULTS";

    /// <summary>displacement is DISPLACEMENT in the file, reactionForceIncludingInertia REACTION_FORCE_INCLUDING_INERTIA.</summary>
    static string GroupName(string recorderName) => Regex.Replace(recorderName, "([A-Z])", "_$1").ToUpperInvariant();

    /// <summary>A group is "74-ForceBeamColumn3d[1000:1:0]": class tag, class name, integration rule.</summary>
    static string ClassName(string group) => Regex.Match(group, @"^\d+-([^\[]+)").Groups[1].Value;

    static void ReadFiles()
    {
        if (!File.Exists("all.mpco"))
        {
            Console.WriteLine("\nNo recorder file, so the solver checks were skipped.\n" +
                              "Put OpenSees on PATH, or set OPENSEES=..., and run again.");
            return;
        }

        Console.WriteLine("\nEvery nodal result comes back");

        using (var file = H5File.OpenRead("all.mpco"))
        {
            foreach (var name in Recorder.NodeResultTypes)
                Check(file.LinkExists($"{Results}/ON_NODES/{GroupName(name)}"), name);

            Console.WriteLine("\nEach element result lands on the elements it is read for");

            // Brick Stresses reads the solids out of "stresses", Beam Forces the beams and Shell
            // Forces the shells out of "section.force", Shell Stresses the shells out of
            // "section.fiber.stress". A shell here is ASDShellQ4, ASDShellT3 and ShellDKGT; the two
            // beams sit on a fibre section, so they answer to section.fiber.stress as well.
            string[] beam = { "ForceBeamColumn3d" };
            string[] shells = { "ASDShellQ4", "ASDShellT3", "ShellDKGT" };
            string[] solids = { "SSPbrick", "FourNodeTetrahedron" };
            var expected = new Dictionary<string, string[]>
            {
                ["stresses"] = shells.Concat(solids).ToArray(),
                ["section.force"] = beam.Concat(shells).ToArray(),
                ["section.fiber.stress"] = beam.Concat(shells).ToArray(),
            };

            Check(expected.Keys.OrderBy(k => k).SequenceEqual(Recorder.ElementResultTypes.OrderBy(k => k)),
                  "every element result is in the table");

            foreach (var name in Recorder.ElementResultTypes)
            {
                string path = $"{Results}/ON_ELEMENTS/{name}";
                var got = file.LinkExists(path)
                    ? file.Group(path).Children().OfType<IH5Group>().Select(group => ClassName(group.Name)).Distinct().OrderBy(n => n).ToList()
                    : new List<string>();
                var want = expected.TryGetValue(name, out var classes) ? classes.OrderBy(n => n).ToList() : new List<string>();
                Check(got.SequenceEqual(want), $"{name}: {string.Join(", ", got)}");
            }

            // What Beam Forces reads: every group of section.force whose class is a ForceBeamColumn,
            // rows keyed by the element's ID. The hinged beam's HingeRadau points differ from the
            // plain beam's Lobatto ones, so MPCO gives it a group of its own - and both have to be
            // found for Beam With Hinges to report forces.
            var beamGroups = file.Group($"{Results}/ON_ELEMENTS/section.force").Children().OfType<IH5Group>()
                                 .Where(group => group.Name.Contains("ForceBeamColumn")).ToList();
            var beamIds = beamGroups.SelectMany(group => group.Dataset("ID").Read<int>()).OrderBy(id => id).ToList();
            Check(beamIds.SequenceEqual(new[] { 1, 8 }),
                  $"Beam Forces finds the plain beam 1 and the hinged beam 8 ({string.Join(", ", beamGroups.Select(group => group.Name))})");
        }

        Console.WriteLine("\nA file with next to nothing in it, as the result components see it");

        var model = new Model { Recorders = new List<IRecorder> { new Recorder { FileName = "some.mpco" } } };

        Check(Read.StepCount(model) == 2, $"two steps were written (read {Read.StepCount(model)})");
        Check(Read.Holds(model, ResultType.DISPLACEMENT), "it holds displacement");
        Check(!Read.Holds(model, ResultType.ROTATION), "it does not hold rotation");

        // Node 2 is the tip of the beam, the second row of the table.
        var tip = Read.NodalOutput(model, 1, ResultType.DISPLACEMENT, new List<int?> { 2 }).ToList();
        Check(tip.Count == 1 && tip[0].Z < 0, $"the beam tip went down (dz = {(tip.Count == 1 ? tip[0].Z : double.NaN):G4})");

        string message = null;
        try { Read.NodalOutput(model, 1, ResultType.ROTATION, new List<int?> { 2 }).ToList(); }
        catch (Exception e) { message = e.Message; }
        Check(message != null && message.Contains("\"rotation\"") && message.Contains("MPCO Recorder"),
              $"asking for rotation says which box to tick: {message}");
    }
}
