// The parts of the design explorer that do not need Rhino: the study file, the design space, and
// the .glb writer.
//
// The study file is what the web explorer reads, so its columns, its quoting and its numbers are
// the contract between the two. The design space decides which designs a study runs; getting its
// order wrong would quietly split a study between two machines with designs missing or run twice.
// The .glb is checked here for its layout, and by the glTF validator when run.sh can reach one.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Alpaca4d.Explorer;

class ExplorerTest
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

    static bool Throws(Action action)
    {
        try { action(); return false; }
        catch (Exception) { return true; }
    }

    static void Main(string[] args)
    {
        Values();
        Csv();
        Study();
        Space();
        Glb(args.Length > 0 ? args[0] : null);

        Console.WriteLine();
        Console.WriteLine(fails == 0 ? "All checks passed." : $"{fails} check(s) failed.");
        Environment.Exit(fails == 0 ? 0 : 1);
    }

    static void Values()
    {
        Section("Numbers are written the invariant way");
        Check(ExplorerValue.Format(0.1 + 0.2) == "0.3", "0.1 + 0.2 is written 0.3");
        Check(ExplorerValue.Format(1.5) == "1.5", "1.5 has a point, whatever the machine's culture");
        Check(ExplorerValue.Format(3.50m) == "3.5", "a decimal slider's trailing zero goes");
        Check(ExplorerValue.Format(double.NaN) == "", "NaN is left empty, which the explorer reads as no data");
        Check(ExplorerValue.Format(double.PositiveInfinity) == "", "so is infinity");
        Check(ExplorerValue.Format(-12000000.0) == "-12000000", "a large whole number stays whole");
    }

    static void Csv()
    {
        Section("CSV lines");
        Check(StudyFile.Line(new[] { "a", "b" }) == "a,b\n", "plain cells are written as they are");
        Check(StudyFile.Line(new[] { "a,b", "say \"hi\"" }) == "\"a,b\",\"say \"\"hi\"\"\"\n", "commas and quotes are quoted");
        Check(StudyFile.Line(new[] { "two\nlines" }) == "two lines\n", "a line break inside a cell becomes a space");
        var cells = StudyFile.ParseLine("\"a,b\",\"say \"\"hi\"\"\",,c");
        Check(cells.SequenceEqual(new[] { "a,b", "say \"hi\"", "", "c" }), "a quoted line reads back cell for cell");

        Section("Columns");
        var one = StudyFile.ColumnsFor(new[] { "Span [m]" }, new[] { "Mass [t]" }, new[] { "Perspective" }, true);
        Check(one.SequenceEqual(new[] { "in:Span [m]", "out:Mass [t]", "img", "threeD" }), "one image is the column img");
        var two = StudyFile.ColumnsFor(new[] { "Span" }, new string[0], new[] { "Top", "Front" }, false);
        Check(two.SequenceEqual(new[] { "in:Span", "img:Top", "img:Front" }), "several images are img:View each");
        var none = StudyFile.ColumnsFor(new[] { "Span" }, new string[0], null, false);
        Check(none.SequenceEqual(new[] { "in:Span" }), "no images, no image column");

        Section("File names");
        Check(StudyFile.FileStem(new[] { "20", "1.5", "HEA 200" }) == "20_1.5_HEA-200", "values joined, a space made safe");
        Check(StudyFile.FileStem(new[] { "a/b", "c:d" }) == "a-b_c-d", "path characters do not reach the file system");
        Check(StudyFile.FileStem(new string[0]) == "design", "a design with no inputs still gets a name");
        var longA = Enumerable.Repeat("123456789", 12).Concat(new[] { "A" }).ToList();
        var longB = Enumerable.Repeat("123456789", 12).Concat(new[] { "B" }).ToList();
        string stemA = StudyFile.FileStem(longA), stemB = StudyFile.FileStem(longB);
        Check(stemA.Length <= 80, $"a long name is cut ({stemA.Length} characters)");
        Check(stemA != stemB, "two long names that differ only at the end stay apart");
    }

    static void Study()
    {
        string folder = Path.Combine(Path.GetTempPath(), "alpaca-explorer-test-" + Guid.NewGuid().ToString("N"));
        string outside = Path.Combine(Path.GetTempPath(), "alpaca-explorer-outside-" + Guid.NewGuid().ToString("N") + ".png");
        try
        {
            Section("A study grows, and never twice");
            var columns = StudyFile.ColumnsFor(new[] { "Span", "Section" }, new[] { "Mass" }, new[] { "Perspective" }, true);
            var study = StudyFile.Open(folder);
            Check(study.Columns == null && study.Count == 0, "a new folder has no study yet");

            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "20_HEA200.png"), "image");
            File.WriteAllText(Path.Combine(folder, "20_HEA200.glb"), "model");
            File.WriteAllText(Path.Combine(folder, "notes.txt"), "the user's own file");
            File.WriteAllText(outside, "not the study's");

            Check(study.Append(columns, new[] { "20", "HEA200" }, new[] { "20", "HEA200", "1.25", "20_HEA200.png", "20_HEA200.glb" }), "the first design is written");
            Check(!study.Append(columns, new[] { "20", "HEA200" }, new[] { "20", "HEA200", "9.99", "x.png", "x.glb" }), "the same inputs again are not");
            Check(study.Append(columns, new[] { "25", "HE A,300" }, new[] { "25", "HE A,300", "2", "../" + Path.GetFileName(outside), "" }), "a second design is");
            Check(study.Count == 2, "two designs recorded");

            var lines = File.ReadAllLines(study.DataPath);
            Check(lines.Length == 3 && lines[0] == "in:Span,in:Section,out:Mass,img,threeD", "data.csv has the header once and a line per design");
            Check(lines[2].StartsWith("25,\"HE A,300\","), "a value with a comma is quoted on disk");
            Check(File.ReadAllBytes(study.DataPath)[0] == (byte)'i', "no byte-order mark in front of the first column");

            var again = StudyFile.Open(folder);
            Check(again.Count == 2 && again.Contains(new[] { "25", "HE A,300" }), "reopened, the study knows what it holds");
            Check(again.Accepts(columns), "and takes more designs of the same kind");

            var other = StudyFile.ColumnsFor(new[] { "Span" }, new[] { "Mass" }, new[] { "Perspective" }, true);
            Check(!again.Accepts(other), "it refuses designs with other columns");
            Check(Throws(() => again.Append(other, new[] { "30" }, new[] { "30", "1", "a.png", "a.glb" })), "and says so rather than writing a misaligned row");
            Check(Throws(() => again.Append(columns, new[] { "30", "x" }, new[] { "30" })), "a row of the wrong length is refused too");

            Section("Two writers on one folder");
            var first = StudyFile.Open(folder);
            var second = StudyFile.Open(folder);
            Check(first.Append(columns, new[] { "30", "HEA240" }, new[] { "30", "HEA240", "3", "30_HEA240.png", "" }), "one writer adds a design");
            Check(!second.Append(columns, new[] { "30", "HEA240" }, new[] { "30", "HEA240", "3", "30_HEA240.png", "" }), "the other sees it on disk and does not add it again");
            Check(second.Append(columns, new[] { "35", "HEA240" }, new[] { "35", "HEA240", "4", "35_HEA240.png", "" }), "and adds its own");
            var lines2 = File.ReadAllLines(first.DataPath);
            Check(lines2.Count(l => l.StartsWith("in:")) == 1 && lines2.Length == 5, $"one header, four designs ({lines2.Length} lines)");

            Section("Designs whose names would clash get files of their own");
            Func<string, IEnumerable<string>> files = stem => new[] { stem + ".png" };
            Check(first.StemFor(new[] { "40", "HEA240" }, files) == "40_HEA240", "a new name is used as it is");
            Check(first.StemFor(new[] { "30", "hea240" }, files) != "30_hea240", "one differing only by case from a file in use is hashed, as Windows and macOS ignore case");
            Check(first.StemFor(new[] { "20", " HEA200" }, files).StartsWith("20_HEA200_"), "so is one that becomes an existing name once made safe");

            Section("Starting over removes the study's files, and only those");
            int deleted = again.Clear();
            Check(!File.Exists(again.DataPath), "data.csv is gone");
            Check(!File.Exists(Path.Combine(folder, "20_HEA200.png")) && !File.Exists(Path.Combine(folder, "20_HEA200.glb")), "so are the files it named");
            Check(File.Exists(Path.Combine(folder, "notes.txt")), "a file the study did not name stays");
            Check(File.Exists(outside), "a file outside the folder stays, whatever a row says");
            Check(deleted == 3 && again.Count == 0 && again.Columns == null, $"three files removed ({deleted}) and the study is empty");
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
            if (File.Exists(outside)) File.Delete(outside);
        }
    }

    static void Space()
    {
        Section("Every combination, first input fastest");
        var space = new DesignSpace(new[] { 2, 3 });
        Check(space.Total == 6 && space.Count == 6, "two by three is six designs");
        var all = space.Enumerate().Select(c => string.Join(",", c)).ToList();
        Check(all.SequenceEqual(new[] { "0,0", "1,0", "0,1", "1,1", "0,2", "1,2" }), "in Colibri's order: " + string.Join(" ", all));

        Section("Divisions");
        Check(DesignSpace.Spread(11, 3, 0).SequenceEqual(new[] { 0, 5, 10 }), "three of eleven are the ends and the middle");
        Check(DesignSpace.Spread(5, 0, 3).Count == 5, "0 takes all of them");
        Check(DesignSpace.Spread(5, 1, 3).SequenceEqual(new[] { 3 }), "1 takes the one the input is on");
        Check(DesignSpace.Spread(4, 9, 0).Count == 4, "more than there are takes all of them");
        var divided = new DesignSpace(new[] { 11, 4 }, new[] { 0, 2 }, new[] { 3, 1 });
        Check(divided.Total == 44 && divided.Selected == 3 && divided.Count == 3, "3 of 11 by the current 1 of 4 is 3 designs out of 44");
        Check(divided.Enumerate().All(c => c[1] == 2), "and every one keeps the second input where it is");
        var mismatched = new DesignSpace(new[] { 3, 3 }, null, new[] { 2 });
        Check(mismatched.DivisionsIgnored && mismatched.Count == 9, "divisions for some inputs only are not used, and say so");

        Section("Ranges split a study between machines");
        var first = new DesignSpace(new[] { 10, 10 }, null, null, new[] { new KeyValuePair<double, double>(0, 49) });
        var second = new DesignSpace(new[] { 10, 10 }, null, null, new[] { new KeyValuePair<double, double>(50, 99) });
        var both = first.Enumerate().Concat(second.Enumerate()).Select(c => string.Join(",", c)).ToList();
        var whole = new DesignSpace(new[] { 10, 10 }).Enumerate().Select(c => string.Join(",", c)).ToList();
        Check(first.Count == 50 && second.Count == 50, "0 to 49 and 50 to 99 are fifty designs each");
        Check(both.SequenceEqual(whole), "and together they are the whole study, once each, in order");
        var odd = new DesignSpace(new[] { 10 }, null, null, new[]
        {
            new KeyValuePair<double, double>(8, 2),
            new KeyValuePair<double, double>(5, 20),
            new KeyValuePair<double, double>(-3, 0.5),
        });
        Check(odd.Count == 9 && odd.Ranges.Count == 2, $"reversed, overlapping and out of range ranges are tidied ({odd.Count} designs)");
        Check(string.Join(",", odd.Nth(1)) == "2", "the second design run is number 2");

        Section("Spaces too large to count");
        var huge = new DesignSpace(new[] { 1000001, 1000001, 1000001, 1000001 }, null, null, new[] { new KeyValuePair<double, double>(0, 9) });
        Check(huge.Overflows && huge.Count == 10, "a range of a space too large to count still runs");
        Check(new DesignSpace(new[] { 3, 0 }).Count == 0, "an input with nothing to step through makes no designs");
        var fine = new DesignSpace(new[] { 100000001, 3 }, null, null, new[] { new KeyValuePair<double, double>(100000000, 100000002) });
        Check(fine.Selected == 300000003 && string.Join(",", fine.Nth(2)) == "1,1", "a slider with a hundred million steps is counted, not listed");
    }

    static void Glb(string path)
    {
        Section("A .glb of a coloured box and its edges");
        var writer = new GlbWriter();
        var positions = new List<float>();
        var normals = new List<float>();
        var colours = new List<float>();
        var indices = new List<int>();

        // One face of a unit box in Rhino's frame, Z up, in millimetres.
        double mm = 0.001;
        GlbWriter.Point(0, 0, 0, mm, positions);
        GlbWriter.Point(1000, 0, 0, mm, positions);
        GlbWriter.Point(1000, 1000, 0, mm, positions);
        GlbWriter.Point(0, 1000, 0, mm, positions);
        for (int i = 0; i < 4; i++)
        {
            GlbWriter.Normal(0, 0, 5, normals);
            GlbWriter.Colour(254, 0, 0, colours);
        }
        indices.AddRange(new[] { 0, 1, 2, 0, 2, 3 });
        writer.AddTriangles(positions, normals, colours, indices);
        writer.AddLines(positions, colours, new[] { 0, 1, 1, 2, 2, 3, 3, 0 });
        writer.AddPoints(positions, colours);
        writer.AddLabel("N1", 0, 0, 0, mm, 42, 45, 49, true);
        writer.AddLabel("Deck, level +3.50", 500, 500, 0, mm, 254, 0, 0, false);
        writer.AddLabel("   ", 0, 0, 0, mm, 0, 0, 0, true);

        Check(positions[1] == 0 && positions[4] == 0 && Math.Abs(positions[8] + 1f) < 1e-6, "Rhino's Y becomes glTF's -Z, in metres");
        Check(normals[1] == 1f, "a Z-up normal becomes Y-up, unit length");
        Check(Math.Abs(GlbWriter.ToLinear(128) - 0.2158605) < 1e-5, "sRGB 128 is 0.216 linear");
        Check(Throws(() => writer.AddTriangles(positions, null, null, new[] { 0, 1, 9 })), "an index past the vertices is refused");
        Check(Throws(() => new GlbWriter().ToArray()), "an empty model is not written");

        byte[] glb = writer.ToArray();
        Check(BitConverter.ToUInt32(glb, 0) == 0x46546C67 && BitConverter.ToUInt32(glb, 4) == 2, "starts glTF, version 2");
        Check(BitConverter.ToUInt32(glb, 8) == glb.Length, "the header gives the file's length");
        int jsonLength = BitConverter.ToInt32(glb, 12);
        Check(jsonLength % 4 == 0 && BitConverter.ToUInt32(glb, 16) == 0x4E4F534A, "the JSON chunk comes first, padded to four bytes");
        string json = Encoding.UTF8.GetString(glb, 20, jsonLength);
        Check(json.Contains("\"COLOR_0\"") && json.Contains("\"mode\":1") && json.Contains("\"mode\":4"), "with coloured triangles and lines");
        Check(BitConverter.ToUInt32(glb, 20 + jsonLength + 4) == 0x004E4942, "then the binary chunk");
        Check(json.Contains("\"mode\":0"), "points are a points primitive");
        Check(json.Contains("\"alpaca4d\":{\"labels\":[") && json.Contains("\"text\":\"N1\"") && json.Contains("\"kind\":\"dot\""),
            "labels are in the scene's extras, a dot marked as one");
        Check(json.Contains("\"color\":\"#fe0000\"") && json.Contains("\"kind\":\"text\""), "with its colour, and text marked as text");
        Check(json.Contains("[0.5,0.0,-0.5]") || json.Contains("[0.5,0,-0.5]"), "at its point, in glTF's frame and metres");
        Check(!json.Contains("\"text\":\"\""), "a label with no text is left out");

        Section("A model of labels only");
        var labelsOnly = new GlbWriter();
        labelsOnly.AddLabel("A", 0, 0, 0, 1, 0, 0, 0, true);
        byte[] onlyLabels = labelsOnly.ToArray();
        int onlyJson = BitConverter.ToInt32(onlyLabels, 12);
        string onlyText = Encoding.UTF8.GetString(onlyLabels, 20, onlyJson);
        Check(!labelsOnly.IsEmpty && onlyLabels.Length == 20 + onlyJson, "has no binary chunk, as there is no geometry");
        Check(!onlyText.Contains("\"meshes\"") && !onlyText.Contains("\"buffers\""), "and no empty meshes or buffers");

        if (path != null)
        {
            File.WriteAllBytes(path, glb);
            File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(path), "labels.glb"), onlyLabels);
            Console.WriteLine($"  wrote {path} and labels.glb for the glTF validator");
        }
    }
}
