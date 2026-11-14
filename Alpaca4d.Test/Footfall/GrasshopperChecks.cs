// Footfall Analysis in Grasshopper, in a running Rhino 8, with the plugin Grasshopper has loaded.
//
// Each stair example of Tools/Examples, saved before Walk, Feel and Full excitation were added, is
// opened and solved as it is: its Footfall Analysis must come back on its Self excitation unit with
// every value and wire it had, walk on the nodes of the shells and report its largest R. Beside it a
// Full excitation component is added, given the same model and settings, a few Walk and Feel points,
// and solved: no Feel point may shake less than self excitation shakes its node, the walker must be on
// a Walk node, and the walker alone on a node must give that node's self excitation. Saved and opened
// again, it must still be Full excitation, with its points and its wires.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

public static class FootfallGrasshopperCheck
{
    static readonly Guid Footfall = new Guid("7E4C1A92-3D58-4B6F-A0E7-2C9F5B81D364");

    static StringBuilder report;
    static int fails;

    static void Check(bool ok, string what)
    {
        if (!ok) fails++;
        report.AppendLine($"  [{(ok ? "PASS" : "FAIL")}] {what}");
    }

    public static string Run(string work)
    {
        report = new StringBuilder();
        fails = 0;
        if (Instances.ComponentServer.EmitObject(Footfall) == null)
            return "  [FAIL] Grasshopper has no Footfall Analysis: build Alpaca4d.Gh into the live Grasshopper folder, Rhino closed";
        foreach (var file in Directory.GetFiles(work, "Footfall*.gh").Where(f => !f.EndsWith("-full.gh")).OrderBy(f => f))
        {
            report.AppendLine();
            report.AppendLine(Path.GetFileName(file));
            try
            {
                Example(file);
            }
            catch (Exception e)
            {
                Check(false, "no exception: " + e);
            }
        }
        report.AppendLine();
        report.AppendLine(fails == 0 ? "all grasshopper checks passed" : $"{fails} grasshopper check(s) failed");
        return report.ToString();
    }

    static IGH_Param In(IGH_Component c, string name) =>
        c.Params.Input.FirstOrDefault(p => p.Name == name) ?? throw new Exception($"no input {name}: {string.Join(", ", c.Params.Input.Select(p => p.Name))}");

    static IGH_Param Out(IGH_Component c, string name) =>
        c.Params.Output.FirstOrDefault(p => p.Name == name) ?? throw new Exception($"no output {name}: {string.Join(", ", c.Params.Output.Select(p => p.Name))}");

    static string Unit(IGH_Component c)
    {
        var unit = c.GetType().GetProperty("ActiveUnit").GetValue(c);
        return (string)unit?.GetType().GetProperty("Name").GetValue(unit);
    }

    static void Switch(IGH_Component c, string unit) =>
        c.GetType().GetMethod("SwitchUnit", new[] { typeof(string), typeof(bool), typeof(bool) }).Invoke(c, new object[] { unit, false, false });

    static List<double> Numbers(IGH_Param p) => p.VolatileData.AllData(true).Select(g => ((GH_Number)g).Value).ToList();
    static List<Point3d> Points(IGH_Param p) => p.VolatileData.AllData(true).Select(g => ((GH_Point)g).Value).ToList();
    static string Text(IGH_Param p) => string.Join("\n", p.VolatileData.AllData(true).Select(g => ((GH_String)g).Value));

    static string Key(Point3d p) => $"{p.X:0.######} {p.Y:0.######} {p.Z:0.######}";

    static IEnumerable<string> Messages(IGH_Component c, GH_RuntimeMessageLevel level) => c.RuntimeMessages(level);

    static GH_Document Open(string file)
    {
        var io = new GH_DocumentIO();
        if (!io.Open(file))
            throw new Exception("could not open " + file);
        var doc = io.Document;
        doc.FilePath = file;
        doc.Enabled = true;
        return doc;
    }

    /// <summary>Gives <paramref name="to"/>'s input what <paramref name="from"/>'s of the same name has: its wires, or the value it read.</summary>
    static void Copy(IGH_Component from, IGH_Component to, string name)
    {
        var source = In(from, name);
        var target = In(to, name);
        foreach (var wire in source.Sources)
            target.AddSource(wire);
        if (source.SourceCount > 0) return;

        var value = source.VolatileData.AllData(true).FirstOrDefault();
        switch (target)
        {
            case Param_Number number when value is GH_Number n:
                number.PersistentData.Clear(); number.PersistentData.Append(new GH_Number(n.Value)); break;
            case Param_Integer integer when value is GH_Integer i:
                integer.PersistentData.Clear(); integer.PersistentData.Append(new GH_Integer(i.Value)); break;
            case Param_String text when value is GH_String s:
                text.PersistentData.Clear(); text.PersistentData.Append(new GH_String(s.Value)); break;
        }
    }

    static void SetPoints(IGH_Component c, string name, IEnumerable<Point3d> points)
    {
        var param = (Param_Point)In(c, name);
        param.PersistentData.Clear();
        foreach (var p in points)
            param.PersistentData.Append(new GH_Point(p));
        param.ExpireSolution(false);
    }

    static void Example(string file)
    {
        var doc = Open(file);
        var self = doc.Objects.OfType<IGH_Component>().Single(c => c.ComponentGuid == Footfall);

        Check(Unit(self) == "SelfExcitation", $"opens on Self excitation ({Unit(self)})");
        Check(In(self, "Walk").SourceCount == 0 && In(self, "AlpacaModel").SourceCount == 1 && In(self, "Footsteps").SourceCount == 1,
              "Walk is new and empty; the model and the footsteps keep their wires");

        doc.NewSolution(true, GH_SolutionMode.Silent);

        var errors = Messages(self, GH_RuntimeMessageLevel.Error).Concat(Messages(self, GH_RuntimeMessageLevel.Warning)).ToList();
        Check(errors.Count == 0, "no errors or warnings" + (errors.Count == 0 ? "" : ": " + string.Join(" | ", errors)));
        string selfReport = Text(Out(self, "Report"));
        Check(selfReport.Contains("stairs (P354 Table 3.2)"), "keeps its menu's values: stairs");
        Check(selfReport.Contains("Walk: none given, so the"), "walks on the nodes of the shells");

        var factors = Numbers(Out(self, "Response Factor"));
        var points = Points(Out(self, "Point"));
        Check(factors.Count > 0 && factors.Count == points.Count && factors.Count == Numbers(Out(self, "Critical Frequency")).Count,
              $"a response factor and a point for each of {factors.Count} nodes");
        int worst = factors.IndexOf(factors.Max());
        report.AppendLine("    " + string.Join("\n    ", selfReport.Trim().Split('\n')));

        var selfAt = new Dictionary<string, double>();
        for (int i = 0; i < points.Count; i++)
            selfAt[Key(points[i])] = factors[i];

        // Full excitation beside it: the same model and settings, a walker on every 7th node and on the
        // worst, the response read at every 11th.
        var full = (IGH_Component)Instances.ComponentServer.EmitObject(Footfall);
        full.CreateAttributes();
        Switch(full, "FullExcitation");
        doc.AddObject(full, false);
        Check(Unit(full) == "FullExcitation" && full.Params.Input.Any(p => p.Name == "Feel") && full.Params.Output.Any(p => p.Name == "Walker Point"),
              "Full excitation has Feel and Walker Point");
        foreach (var name in new[] { "AlpacaModel", "Footsteps", "Walker Mass", "Frequency Weighting", "Activity", "Minimum Frequency",
                                     "Maximum Frequency", "Frequency Steps", "Cut Off", "Damping" })
            Copy(self, full, name);

        var walk = points.Where((p, i) => i % 7 == 0).Append(points[worst]).ToList();
        var feel = points.Where((p, i) => i % 11 == 3).ToList();
        SetPoints(full, "Walk", walk);
        SetPoints(full, "Feel", feel);
        full.ExpireSolution(false);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        doc.NewSolution(false, GH_SolutionMode.Silent);
        clock.Stop();

        var fullErrors = Messages(full, GH_RuntimeMessageLevel.Error).Concat(Messages(full, GH_RuntimeMessageLevel.Warning)).ToList();
        Check(fullErrors.Count == 0, "full: no errors or warnings" + (fullErrors.Count == 0 ? "" : ": " + string.Join(" | ", fullErrors)));
        var fullFactors = Numbers(Out(full, "Response Factor"));
        var fullPoints = Points(Out(full, "Point"));
        var walkers = Points(Out(full, "Walker Point"));
        Check(fullFactors.Count == feel.Count && fullPoints.Count == feel.Count && walkers.Count == feel.Count,
              $"full: a result for each of the {feel.Count} Feel points, with {walk.Count} Walk points, in {clock.Elapsed.TotalSeconds:0.##} s");
        Check(fullPoints.Select(Key).SequenceEqual(feel.Select(Key)), "full: each Feel point on its own node, in order");
        var walkKeys = new HashSet<string>(walk.Select(Key));
        Check(walkers.All(w => walkKeys.Contains(Key(w))), "full: every walker on a Walk node");
        Check(Enumerable.Range(0, feel.Count).All(i => fullFactors[i] >= selfAt[Key(fullPoints[i])] - 1e-9 ||
                                                       !walkKeys.Contains(Key(fullPoints[i]))),
              "full: a Feel point that is also walked on shakes no less than in self excitation");
        string fullReport = Text(Out(full, "Report"));
        Check(fullReport.StartsWith("Full excitation") && fullReport.Contains("with the walker at node"), "full: the report says where the walker is");
        report.AppendLine("    " + string.Join("\n    ", fullReport.Trim().Split('\n').Where(l => l.StartsWith("Walk") || l.StartsWith("Feel") || l.StartsWith("The walker") || l.StartsWith("Largest"))));

        // The walker alone on the worst node, read there: self excitation's largest R.
        SetPoints(full, "Walk", new[] { points[worst] });
        SetPoints(full, "Feel", new Point3d[0]);
        full.ExpireSolution(false);
        doc.NewSolution(false, GH_SolutionMode.Silent);
        var alone = Numbers(Out(full, "Response Factor"));
        Check(alone.Count == 1 && Math.Abs(alone[0] - factors[worst]) <= 1e-9 * factors[worst] &&
              Text(Out(full, "Report")).Contains("Feel: none given, so the response is read at the Walk nodes."),
              $"full, one Walk point and no Feel: self excitation's R there, {factors[worst]:0.##}" +
              (alone.Count == 1 ? $", got {alone[0]:0.##}" : $", got {alone.Count} values"));

        // Saved and opened again: still Full excitation, with its point and its wires.
        string saved = Path.ChangeExtension(file, null) + "-full.gh";
        SetPoints(full, "Walk", walk);
        SetPoints(full, "Feel", feel);
        var io = new GH_DocumentIO(doc);
        Check(io.SaveQuiet(saved), "saved");
        var again = Open(saved);
        var reopened = again.Objects.OfType<IGH_Component>().Where(c => c.ComponentGuid == Footfall).ToList();
        var back = reopened.FirstOrDefault(c => Unit(c) == "FullExcitation");
        Check(reopened.Count == 2 && reopened.Count(c => Unit(c) == "SelfExcitation") == 1 && back != null, "opened again: one of each unit");
        if (back != null)
            Check(((Param_Point)In(back, "Walk")).PersistentData.DataCount == walk.Count &&
                  ((Param_Point)In(back, "Feel")).PersistentData.DataCount == feel.Count &&
                  In(back, "AlpacaModel").SourceCount == 1 && In(back, "Footsteps").SourceCount == 1 &&
                  ((Param_String)In(back, "Activity")).PersistentData.AllData(true).OfType<GH_String>().Single().Value == "Stairs",
                  "opened again: Full excitation keeps its points, its wires and its values");
    }
}
