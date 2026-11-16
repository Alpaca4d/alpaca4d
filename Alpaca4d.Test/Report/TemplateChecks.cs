// The Report's templates through the real components, in a running Rhino 8. templates.sh loads this
// build of the plugin beside the installed one and calls Start by reflection, from an idle moment.
//
// A two-storey steel frame analysed three ways - static, natural vibration, linear buckling - goes
// into a Report; then the Templates menu: the report For the models given, its chapters fitted to the
// analyses and its components laid out left of the Report without overlapping; a chapter more into
// the Merge in front of Content; undo and redo; the static chapters' pictures each with its table;
// and the PDF written from what the template placed, with nothing in the Log.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Special;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

public static class TemplateCheck
{
    static StringBuilder report = new StringBuilder();
    static int fails;
    static string folder;
    static Assembly gha;
    static Dictionary<Guid, Type> types;
    const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    static void Check(bool ok, string what)
    {
        if (!ok) fails++;
        report.AppendLine($"  [{(ok ? "PASS" : "FAIL")}] {what}");
    }

    static GH_Document doc;
    static GH_Component reportComponent;
    static GH_BooleanToggle runToggle;
    static int waited;

    static GH_Component Make(string guid)
    {
        var c = (GH_Component)Activator.CreateInstance(types[new Guid(guid)]);
        c.CreateAttributes();
        doc.AddObject(c, false);
        return c;
    }

    static void Set(IGH_Param p, params IGH_Goo[] values)
    {
        var data = (IGH_Structure)p.GetType().GetProperty("PersistentData").GetValue(p);
        data.GetType().GetMethod("Clear").Invoke(data, null);
        var append = data.GetType().GetMethods().First(m => m.Name == "Append" && m.GetParameters().Length == 1);
        foreach (var v in values) append.Invoke(data, new object[] { v });
    }

    static IEnumerable<(Point3d, Point3d)> Split(Point3d a, Point3d b, int n) =>
        Enumerable.Range(0, n).Select(i => (a + (b - a) * i / n, a + (b - a) * (i + 1) / n));

    public static string Start(string work)
    {
        folder = work;
        try
        {
            gha = AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == "Alpaca4d.Gh" && Path.GetDirectoryName(a.Location) == work);
            var core = AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == "Alpaca4d.Core" && Path.GetDirectoryName(a.Location) == work);
            core.GetType("Alpaca4d.License.License").GetMethod("Install", Any, null, new[] { typeof(string) }, null)
                ?.Invoke(null, new object[] { Path.Combine(work, "licence.bin") });

            types = new Dictionary<Guid, Type>();
            foreach (var t in gha.GetTypes().Where(t => typeof(GH_Component).IsAssignableFrom(t) && !t.IsAbstract && t.GetConstructor(Type.EmptyTypes) != null))
            {
                try { var o = (GH_Component)Activator.CreateInstance(t); types[o.ComponentGuid] = t; } catch { }
            }

            doc = new GH_Document();
            doc.FilePath = Path.Combine(work, "Templates.gh");

            // The frame: 6 x 4 m, two storeys of 3.5 m, each member in two.
            var corners = new[] { new Point3d(0, 0, 0), new Point3d(6, 0, 0), new Point3d(6, 4, 0), new Point3d(0, 4, 0) };
            var cols = new List<GH_Curve>(); var beams = new List<GH_Curve>();
            for (int s = 0; s < 2; s++)
            {
                foreach (var c in corners)
                    foreach (var (a, b) in Split(c + new Vector3d(0, 0, 3.5 * s), c + new Vector3d(0, 0, 3.5 * (s + 1)), 2))
                        cols.Add(new GH_Curve(new LineCurve(a, b)));
                for (int k = 0; k < 4; k++)
                    foreach (var (a, b) in Split(corners[k] + new Vector3d(0, 0, 3.5 * (s + 1)), corners[(k + 1) % 4] + new Vector3d(0, 0, 3.5 * (s + 1)), 2))
                        beams.Add(new GH_Curve(new LineCurve(a, b)));
            }
            var steel = Make("F50A7B47-2E73-4B57-BF7A-DB6D7EAC91D6");
            Set(steel.Params.Input[0], new GH_String("S355"));
            var elements = new List<GH_Component>();
            foreach (var (profile, curves) in new[] { ("HEB200", cols), ("IPE300", beams) })
            {
                var section = Make("0F82C248-4B58-4C25-9F85-6B7F7C4696C0");
                Set(section.Params.Input[0], new GH_String(profile));
                section.Params.Input[1].AddSource(steel.Params.Output[0]);
                var beam = Make("A1B2C3D4-E5F6-7A8B-9C0D-E1F2A3B4C5D6");
                Set(beam.Params.Input[0], curves.Cast<IGH_Goo>().ToArray());
                beam.Params.Input[1].AddSource(section.Params.Output[0]);
                elements.Add(beam);
            }
            var fixedFeet = Make("C4E1D7A2-58B6-4F03-8E2A-1B9F6D3A7C05");
            Set(fixedFeet.Params.Input[0], corners.Select(p => (IGH_Goo)new GH_Plane(new Plane(p, Vector3d.XAxis, Vector3d.YAxis))).ToArray());
            var weight = Make("4B6FA7FD-9388-4699-B374-B94F94583A53");
            var roof = Make("069017B3-4A2F-40FE-87BA-126100908BC8");
            Set(roof.Params.Input[0], corners.Select(p => (IGH_Goo)new GH_Point(p + new Vector3d(0, 0, 7))).ToArray());
            Set(roof.Params.Input[1], new GH_Vector(new Vector3d(10, 0, -80)));
            var pattern = Make("C4E8F9A2-B5D3-4E6F-A7B8-9C0D1E2F3A4B");
            pattern.Params.Input[1].DataMapping = GH_DataMapping.Flatten;
            pattern.Params.Input[1].AddSource(weight.Params.Output[0]);
            pattern.Params.Input[1].AddSource(roof.Params.Output[0]);
            var assemble = Make("B6978C42-7B88-4F1A-A5DF-85EF89F154F9");
            foreach (var e in elements) assemble.Params.Input[0].AddSource(e.Params.Output[0]);
            assemble.Params.Input[0].DataMapping = GH_DataMapping.Flatten;
            assemble.Params.Input[1].AddSource(fixedFeet.Params.Output[0]);
            assemble.Params.Input[2].AddSource(pattern.Params.Output[0]);
            var settings = Make("9AE62757-69CC-4023-92EB-E8E1AA164AD0");
            var run = Make("8C1F5A3E-2D74-4B96-A0E8-7F3B19D6C452");
            run.Params.Input[0].AddSource(assemble.Params.Output[0]);
            run.Params.Input[1].AddSource(settings.Params.Output[0]);
            var vibration = Make("3B7D2E95-6A41-4C8F-9E1D-5F0A8C27B6D3");
            vibration.Params.Input[0].AddSource(assemble.Params.Output[0]);
            Set(vibration.Params.Input[1], new GH_Integer(6));
            var buckling = Make("6E2B9C41-8D57-4A3F-B1E0-7C94D2A6F815");
            buckling.Params.Input[0].AddSource(assemble.Params.Output[0]);
            Set(buckling.Params.Input[1], new GH_Integer(4));

            reportComponent = Make("4D6285D0-F64A-4CF1-A9A6-B62EDC615D7D");
            var models = reportComponent.Params.Input[0];
            models.DataMapping = GH_DataMapping.Flatten;
            foreach (var m in new[] { assemble, run, vibration, buckling }) models.AddSource(m.Params.Output[0]);
            runToggle = new GH_BooleanToggle { Value = false };
            runToggle.CreateAttributes();
            doc.AddObject(runToggle, false);
            reportComponent.Params.Input[4].AddSource(runToggle);
            Set(reportComponent.Params.Input[3], new GH_String(Path.Combine(work, "Templates.pdf")));
            // Somewhere on the canvas, as it would be: right of the model.
            reportComponent.Attributes.Pivot = new System.Drawing.PointF(2000, 400);
            reportComponent.Attributes.ExpireLayout();
            reportComponent.Attributes.PerformLayout();

            doc.Enabled = true;
            doc.NewSolution(true, GH_SolutionMode.Silent);
            foreach (var c in new[] { assemble, run, vibration, buckling })
                Check(!c.RuntimeMessages(GH_RuntimeMessageLevel.Error).Any(), $"{c.NickName} solved: {string.Join(" | ", c.RuntimeMessages(GH_RuntimeMessageLevel.Error))}");

            var templates = gha.GetType("Alpaca4d.Gh.ReportTemplates");
            var given = (System.Collections.IList)reportComponent.GetType().GetProperty("LastModels", Any).GetValue(reportComponent);
            Check(given.Count == 4, $"the Report kept the 4 models it was given ({given.Count})");

            // The menu.
            var menu = new ToolStripDropDownMenu();
            templates.GetMethod("Fill", Any).Invoke(null, new object[] { menu.Items, reportComponent });
            var items = menu.Items.OfType<ToolStripMenuItem>().ToList();
            report.AppendLine("  menu: " + string.Join(" / ", items.Select(i => (i.Enabled ? "" : "(off) ") + i.Text.Trim())));
            var fitted = items.First(i => i.Text == "For the models given");
            Check(fitted.Enabled, "For the models given is offered: " + fitted.ToolTipText);
            Check(fitted.ToolTipText.Contains("Member forces") && fitted.ToolTipText.Contains("Natural vibration") && fitted.ToolTipText.Contains("Stability")
                  && !fitted.ToolTipText.Contains("Shell forces") && !fitted.ToolTipText.Contains("Footfall"), "fitted to a frame with static, vibration and buckling");

            int before = doc.ObjectCount;
            var content = reportComponent.Params.Input[2];
            fitted.PerformClick();
            int afterFitted = doc.ObjectCount;
            var merge = content.Sources.Count == 1 ? content.Sources[0].Attributes.GetTopLevel.DocObject as GH_Component : null;
            Check(merge != null && merge.NickName == "Report content", $"Content wired to the Merge Report content ({merge?.NickName})");
            Check(merge != null && merge.Params.Input.Count(p => p.SourceCount == 1) == 7, $"7 chapters into it, in {merge?.Params.Input.Count} inputs");
            var groups = doc.Objects.OfType<GH_Group>().Select(g => g.NickName).ToList();
            Check(groups.SequenceEqual(new[] { "Title page", "The model", "Static analysis", "Member forces", "Natural vibration", "Stability", "Conclusions" }), "a group a chapter: " + string.Join(", ", groups));
            var added = doc.Objects.Skip(before).Where(o => !(o is GH_Group)).ToList();
            Check(added.All(o => o.Attributes.Selected), "what was placed is selected");
            var box = added.Select(o => o.Attributes.Bounds).Aggregate(System.Drawing.RectangleF.Union);
            Check(box.Right <= reportComponent.Attributes.Bounds.Left, $"placed left of the Report: {box} vs {reportComponent.Attributes.Bounds}");
            int overlaps = 0;
            for (int i = 0; i < added.Count; i++)
                for (int j = i + 1; j < added.Count; j++)
                    if (added[i].Attributes.Bounds.IntersectsWith(added[j].Attributes.Bounds)) { overlaps++; report.AppendLine($"    overlap {added[i].Name} / {added[j].Name}"); }
            Check(overlaps == 0, $"no two placed objects overlap ({overlaps})");
            var texts = added.OfType<GH_Panel>().ToList();
            Check(texts.All(p => p.Recipients.Count == 1), $"each of the {texts.Count} panels feeds its Report Text");

            // A chapter more: into the Merge, as its last input; then undone.
            items.First(i => i.Text.Trim() == "Footfall").PerformClick();
            Check(merge.Params.Input.Count(p => p.SourceCount == 1) == 8 && merge.Params.Input[7].SourceCount == 1 && content.SourceCount == 1, $"Footfall went into the Merge's 8th input ({merge.Params.Input.Count} inputs)");
            doc.Undo();
            var mergeNow = content.Sources.Count == 1 ? content.Sources[0].Attributes.GetTopLevel.DocObject as GH_Component : null;
            Check(doc.ObjectCount == afterFitted && mergeNow != null && mergeNow.Params.Input.Count(p => p.SourceCount == 1) == 7,
                  $"undo takes the chapter away: {doc.ObjectCount} objects (was {afterFitted}), merge inputs {mergeNow?.Params.Input.Count}");
            doc.Undo();
            Check(doc.ObjectCount == before && content.SourceCount == 0, $"undo again takes the report away: {doc.ObjectCount} objects (was {before}), Content sources {content.SourceCount}");
            doc.UndoServer.PerformRedo();
            Check(doc.ObjectCount == afterFitted && content.SourceCount == 1, $"redo brings it back: {doc.ObjectCount} objects, Content sources {content.SourceCount}");

            // The static chapters one at a time too, the shells' and solids' among them: what each view hands out.
            foreach (var chapter in new[] { "Shell forces", "Solid stresses" })
                items.First(i => i.Text.Trim() == chapter).PerformClick();
            doc.NewSolution(false, GH_SolutionMode.Silent);
            var figures = doc.Objects.OfType<GH_Component>().Where(c => c.Name == "Report View (Alpaca4d)")
                .SelectMany(c => c.Params.Output[0].VolatileData.AllData(true).Select(g => g.ToString())).ToList();
            File.WriteAllText(Path.Combine(work, "figures.json"), string.Join("\n----\n", figures));
            string Q(string t) => "\"" + t + "\"";
            string Figure(string result, string component) => figures.FirstOrDefault(f => f.Contains(Q("result") + ": " + Q(result)) && f.Contains(component)) ?? "";
            foreach (var (result, component) in new[] { ("Displacement", "Magnitude"), ("Reactions", "Force"), ("Beam forces", Q("N")), ("Beam forces", Q("Mz")), ("Shell forces", "mxx"), ("Shell forces", "myy"), ("Brick stresses", "VonMises") })
            {
                var f = Figure(result, component);
                Check(f.Contains(Q("table") + ": true"), $"{result} {component}: a picture with its table");
            }
            // Each view laid out as one placed by hand: its menus composed, so that none of its inputs is left at
            // the canvas' origin with the component stretched out to it - as the Static analysis views were.
            foreach (var v in doc.Objects.OfType<GH_Component>().Where(c => c.Name == "Report View (Alpaca4d)"))
            {
                var b = v.Attributes.Bounds;
                var away = v.Params.Input.Concat(v.Params.Output).Where(p => p.Attributes.Bounds.X < b.Left || p.Attributes.Bounds.X > b.Right).Select(p => p.Name).ToList();
                Check(b.Width < 300 && away.Count == 0, $"Report View {v.InstanceGuid.ToString().Substring(0, 4)} laid out as by hand: {b.Width:F0} x {b.Height:F0}{(away.Count > 0 ? ", outside it: " + string.Join(", ", away) : "")}");
            }
            doc.Undo();
            doc.Undo();
            Check(doc.ObjectCount == afterFitted, "the two chapters undone");
            doc.NewSolution(false, GH_SolutionMode.Silent);
            foreach (var c in doc.Objects.OfType<GH_Component>().Where(c => c.Category == "Alpaca4d" && c.SubCategory == "13_Report"))
                foreach (var level in new[] { GH_RuntimeMessageLevel.Error, GH_RuntimeMessageLevel.Warning })
                    foreach (var m in c.RuntimeMessages(level))
                    { Check(false, $"{c.Name} {level}: {m}"); }
            var template = reportComponent.Params.Output[2].VolatileData.AllData(true).FirstOrDefault()?.ToString() ?? "";
            File.WriteAllText(Path.Combine(work, "template.json"), template);
            Check(template.Contains("Static analysis") && template.Contains("Conclusions") && template.Contains("Structural analysis report"), $"the Template output holds the chapters ({template.Length} chars)");

            // Write the PDF: Run on; the Report writes when Rhino is idle.
            runToggle.Value = true;
            runToggle.ExpireSolution(false);
            doc.NewSolution(false, GH_SolutionMode.Silent);
            Rhino.RhinoApp.Idle += Wait;
            return null;
        }
        catch (Exception e)
        {
            Check(false, "no exception: " + e);
            return Finish();
        }
    }

    static DateTime last = DateTime.MinValue;

    static void Wait(object s, EventArgs e)
    {
        if ((DateTime.Now - last).TotalSeconds < 1) return;
        last = DateTime.Now;
        waited++;
        var file = reportComponent.Params.Output[0].VolatileData.AllData(true).FirstOrDefault()?.ToString();
        if (string.IsNullOrEmpty(file) && waited < 240) return;
        Rhino.RhinoApp.Idle -= Wait;
        try
        {
            Check(file != null && File.Exists(file), "the PDF is written: " + file);
            foreach (var level in new[] { GH_RuntimeMessageLevel.Error, GH_RuntimeMessageLevel.Warning, GH_RuntimeMessageLevel.Remark })
                foreach (var m in reportComponent.RuntimeMessages(level)) report.AppendLine($"  Report {level}: {m}");
            var log = reportComponent.Params.Output[1].VolatileData.AllData(true).Select(g => g.ToString()).ToList();
            Check(log.Count == 0, "the Log is empty: " + string.Join(" | ", log));

            // Run off again and a chapter more: the Template output is what Content says now, not what the run wrote.
            runToggle.Value = false;
            runToggle.ExpireSolution(false);
            doc.NewSolution(false, GH_SolutionMode.Silent);
            var more = new ToolStripDropDownMenu();
            gha.GetType("Alpaca4d.Gh.ReportTemplates").GetMethod("Fill", Any).Invoke(null, new object[] { more.Items, reportComponent });
            var whole = more.Items.OfType<ToolStripMenuItem>().Select(i => i.Text.Trim()).TakeWhile(t => t != "Chapter").ToList();
            Check(whole.Contains("Static") && !whole.Contains("Static analysis"), "the whole reports are named apart from the chapters: " + string.Join(", ", whole));
            more.Items.OfType<ToolStripMenuItem>().Last(i => i.Text.Trim() == "Conclusions").PerformClick();
            doc.NewSolution(false, GH_SolutionMode.Silent);
            var now = reportComponent.Params.Output[2].VolatileData.AllData(true).FirstOrDefault()?.ToString() ?? "";
            int conclusions = System.Text.RegularExpressions.Regex.Matches(now, "Write the conclusions here").Count;
            Check(conclusions == 2, $"with Run off after a run, the Template output follows Content: the conclusions twice ({conclusions})");
            if (file != null && File.Exists(file))
            {
                var pdf = File.ReadAllText(file, Encoding.GetEncoding("ISO-8859-1"));
                report.AppendLine($"  pages: {System.Text.RegularExpressions.Regex.Matches(pdf, @"/Type\s*/Page[^s]").Count}");
            }
            var pictures = Path.Combine(Path.GetDirectoryName(file ?? folder), Path.GetFileNameWithoutExtension(file ?? "x") + " pictures");
            if (Directory.Exists(pictures)) report.AppendLine($"  pictures: {Directory.GetFiles(pictures).Length}");
            new GH_DocumentIO(doc).SaveQuiet(Path.Combine(folder, "Templates.gh"));
        }
        catch (Exception ex) { Check(false, "no exception after the run: " + ex); }
        File.WriteAllText(Path.Combine(folder, "result.txt"), Finish());
    }

    static string Finish()
    {
        report.AppendLine(fails == 0 ? "all template checks passed" : $"{fails} template check(s) failed");
        return report.ToString();
    }
}
