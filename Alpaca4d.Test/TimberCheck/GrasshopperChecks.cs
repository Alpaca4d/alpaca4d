// The timber check through the Grasshopper components themselves, in a running Rhino 8.
//
// A C24 cantilever built in code out of the real components - Material Library Elastic typed "C24",
// Rectangle Section, ForceBeamColumn, Support, Point Load, three Load Patterns with their Duration
// menus set to Permanent, Medium-term and Not set, Assemble Model, Load Combination with a ULS, a
// permanent-only ULS and an Accidental combination, Analysis Settings, Run Analysis - solved the way
// the canvas solves it and read back through Utilisation on its Timber type, once as it comes and
// once with ServiceClass 3 and γM 1.5, and on its Steel type, which leaves the timber beam alone.
// k_mod and γM are checked combination by combination, through the ratios of the utilisations,
// which do not depend on the section's axes, through the kmod output and through the Report. The
// two types' inputs, outputs and icons, and a saved Utilisation of each type read back, are checked too.
// Load Pattern's Duration menu is written and read back the way a file saves and opens it.
// grasshopper.sh loads this build of the plugin beside the installed one and calls Run by reflection.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

public static class GrasshopperCheck
{
    static StringBuilder report;
    static int fails;
    static string folder;

    static void Check(bool ok, string what)
    {
        if (!ok) fails++;
        report.AppendLine($"  [{(ok ? "PASS" : "FAIL")}] {what}");
    }

    static void Section(string title)
    {
        report.AppendLine();
        report.AppendLine(title);
    }

    static bool Near(double a, double b, double tol) => Math.Abs(a - b) <= tol * Math.Max(1e-12, Math.Abs(b));

    public static string Run(string work)
    {
        folder = work;
        report = new StringBuilder();
        fails = 0;
        try
        {
            DurationMenu();
            Cantilever();
            Optimiser();
        }
        catch (Exception e)
        {
            Check(false, "no exception: " + e);
        }
        report.AppendLine();
        report.AppendLine(fails == 0 ? "all grasshopper checks passed" : $"{fails} grasshopper check(s) failed");
        return report.ToString();
    }

    // ---------------------------------------------------------------- building a document

    sealed class Doc
    {
        public readonly GH_Document Document = new GH_Document();
        readonly List<GH_Component> components = new List<GH_Component>();

        public Doc(string name)
        {
            // Run Analysis writes its deck and results beside the document.
            Document.FilePath = Path.Combine(folder, name + ".gh");
        }

        public T Add<T>(string unit = null) where T : GH_Component, new() => Place(new T(), unit);

        public T Place<T>(T component, string unit = null) where T : GH_Component
        {
            if (component.Attributes == null)
                component.CreateAttributes();
            if (unit != null)
                component.GetType().GetMethod("SwitchUnit", new[] { typeof(string), typeof(bool), typeof(bool) }).Invoke(component, new object[] { unit, false, false });
            Document.AddObject(component, false);
            components.Add(component);
            return component;
        }

        public void Solve()
        {
            Document.Enabled = true;
            Document.NewSolution(true, GH_SolutionMode.Silent);
        }

        public IEnumerable<string> Messages(GH_RuntimeMessageLevel level) =>
            components.SelectMany(c => c.RuntimeMessages(level).Select(m => $"{c.NickName}: {m}"));
    }

    static IGH_Param In(GH_Component c, string name) =>
        c.Params.Input.FirstOrDefault(p => p.Name == name || p.NickName == name)
        ?? throw new Exception($"{c.Name} has no input {name} ({string.Join(", ", c.Params.Input.Select(p => p.NickName))})");

    static IGH_Param Out(GH_Component c, string name) =>
        c.Params.Output.FirstOrDefault(p => p.Name == name || p.NickName == name)
        ?? throw new Exception($"{c.Name} has no output {name}");

    static void Set<T>(GH_Component c, string input, params T[] values) where T : class, IGH_Goo
    {
        var param = (GH_PersistentParam<T>)In(c, input);
        param.PersistentData.Clear();
        foreach (var value in values)
            param.PersistentData.Append(value);
    }

    static void Wire(GH_Component from, string output, GH_Component to, string input) =>
        In(to, input).AddSource(Out(from, output));

    static GH_Point Pt(double x, double y, double z) => new GH_Point(new Point3d(x, y, z));

    /// <summary>Each branch of an output, by the combination's place, its items as they script.</summary>
    static Dictionary<int, List<object>> Branches(GH_Component c, string output)
    {
        var data = Out(c, output).VolatileData;
        return Enumerable.Range(0, data.PathCount)
            .ToDictionary(i => data.Paths[i].Indices[0], i => data.get_Branch(i).Cast<IGH_Goo>().Select(goo => goo?.ScriptVariable()).ToList());
    }

    static double Number(Dictionary<int, List<object>> branches, int place) => Convert.ToDouble(branches[place][0]);

    static void NoErrors(Doc doc, string what)
    {
        var errors = doc.Messages(GH_RuntimeMessageLevel.Error).Concat(doc.Messages(GH_RuntimeMessageLevel.Warning)).ToList();
        Check(errors.Count == 0, errors.Count == 0 ? $"{what}: no component has an error or a warning" : $"{what}: {string.Join(" | ", errors)}");
    }

    // ---------------------------------------------------------------- Load Pattern's Duration menu

    /// <summary>The drop-down of a Load Pattern's PlainPattern unit.</summary>
    static Alpaca4d.UIWidgets.MenuDropDown Duration(GH_Component pattern)
    {
        var subcomponents = (System.Collections.IList)typeof(Alpaca4d.Gh.PatternBase)
            .GetField("_subcomponents", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(pattern);
        var plain = subcomponents.Cast<object>().First(s => s.GetType().Name == "PatternPlain");
        return (Alpaca4d.UIWidgets.MenuDropDown)plain.GetType().GetField("_duration", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(plain);
    }

    static GH_Component Pattern(Doc doc, string name, int duration, GH_Component load)
    {
        var pattern = doc.Add<Alpaca4d.Gh.PatternBase>("PlainPattern (Alpaca4d)");
        Set(pattern, "Name", new GH_String(name));
        Wire(load, "Load", pattern, "Loads");
        Duration(pattern).Value = duration;
        return pattern;
    }

    static GH_Component Load(Doc doc, double fz)
    {
        var load = doc.Add<Alpaca4d.Gh.PointLoad>();
        Set(load, "Point", Pt(3, 0, 0));
        Set(load, "Force", new GH_Vector(new Vector3d(0, 0, fz)));
        return load;
    }

    /// <summary>A Deconstruct Anything with an output for each public field and property of <paramref name="type"/>, and the class's.</summary>
    static GH_Component Deconstructor(Doc doc, Type type)
    {
        var deconstruct = doc.Add<Alpaca4d.Gh.DeconstructAnything>();
        int count = type.GetFields(BindingFlags.Instance | BindingFlags.Public).Length + type.GetProperties(BindingFlags.Instance | BindingFlags.Public).Length + 1;
        while (deconstruct.Params.Output.Count < count)
            deconstruct.Params.RegisterOutputParam(new Param_GenericObject());
        deconstruct.Params.OnParametersChanged();
        return deconstruct;
    }

    static Alpaca4d.Loads.LoadPattern Made(GH_Component pattern) =>
        (Alpaca4d.Loads.LoadPattern)Out(pattern, "LoadPattern").VolatileData.AllData(true).First().ScriptVariable();

    static void DurationMenu()
    {
        Section("Load Pattern: the Duration menu");
        var doc = new Doc("duration");
        var load = Load(doc, -1);
        var pattern = Pattern(doc, "S", 0, load);

        var menu = Duration(pattern);
        Check(menu != null && menu.Items.Count == 6, $"a drop-down of six: Not set and the five classes ({menu?.Items.Count})");
        Check(string.Join(", ", menu.Items.Select(i => i.name)) == "Not set, Permanent, Long-term, Medium-term, Short-term, Instantaneous",
              "in EN 1995-1-1's order: " + string.Join(", ", menu.Items.Select(i => i.name)));

        var before = pattern.Attributes.Bounds.Height;
        pattern.Attributes.ExpireLayout();
        pattern.Attributes.PerformLayout();
        Check(pattern.Attributes.Bounds.Height > 0, $"the component lays out with the menu folded ({pattern.Attributes.Bounds.Height:F0} px high)");

        doc.Solve();
        Check(Made(pattern).Duration == null, "Not set: the pattern has no duration");
        Duration(pattern).Value = 4;
        pattern.ExpireSolution(false);
        doc.Solve();
        Check(Made(pattern).Duration == Alpaca4d.Loads.LoadDuration.ShortTerm, $"Short-term chosen: the pattern is short-term ({Made(pattern).Duration})");
        NoErrors(doc, "the pattern");

        // Saved and opened: the component written to a chunk and read into a new one, as a file does.
        var chunk = new GH_IO.Serialization.GH_LooseChunk("pattern");
        pattern.Write(chunk);
        var bytes = chunk.Serialize_Binary();
        var read = new GH_IO.Serialization.GH_LooseChunk("pattern");
        read.Deserialize_Binary(bytes);

        var again = new Doc("duration-again");
        var opened = new Alpaca4d.Gh.PatternBase();
        opened.CreateAttributes();
        Check(opened.Read(read), "the saved pattern reads back");
        again.Place(opened);
        var load2 = Load(again, -1);
        In(opened, "Loads").AddSource(Out(load2, "Load"));
        again.Solve();
        Check(Duration(opened).Value == 4, $"opened: the menu shows Short-term ({Duration(opened).Items[Duration(opened).Value].name})");
        Check(Made(opened).Duration == Alpaca4d.Loads.LoadDuration.ShortTerm, "and the pattern it makes is short-term");
        Check(Made(opened).Name == "S", "with its name");
        doc.Document.Enabled = false;
        again.Document.Enabled = false;
    }

    // ---------------------------------------------------------------- Section Optimiser, on timber

    /// <summary>One Section Optimiser for timber as for steel: a C24 cantilever sized from a Design Group of rectangles.</summary>
    static void Optimiser()
    {
        Section("Section Optimiser: a C24 cantilever, 3 m, 3.5 kN medium-term at its tip, from five rectangles");
        var doc = new Doc("optimiser");
        var material = doc.Add<Alpaca4d.Gh.MaterialLibraryElastic>();
        Set(material, "Material Name", new GH_String("C24"));

        GH_Component Rectangle(double w, double h)
        {
            var s = doc.Add<Alpaca4d.Gh.RectangleCS>();
            Set(s, "Width", new GH_Number(w));
            Set(s, "Height", new GH_Number(h));
            Wire(material, "Material", s, "Material");
            return s;
        }
        var startSection = Rectangle(0.1, 0.4);
        var beam = doc.Add<Alpaca4d.Gh.BeamBase>("ForceBeamColumn (Alpaca4d)");
        Set(beam, "Line", new GH_Curve(new LineCurve(new Point3d(0, 0, 0), new Point3d(3, 0, 0))));
        Wire(startSection, "Section", beam, "Section");
        Set(beam, "ElementId", new GH_String("Joist"));
        var support = doc.Add<Alpaca4d.Gh.Support>();
        Set(support, "Position", new GH_Plane(new Plane(Point3d.Origin, Vector3d.XAxis, Vector3d.YAxis)));
        foreach (var dof in new[] { "Tx", "Ty", "Tz", "Rx", "Ry", "Rz" })
            Set(support, dof, new GH_Boolean(true));
        var q = Pattern(doc, "Q", 3, Load(doc, -3.5));
        var assemble = doc.Add<Alpaca4d.Gh.AssembleModel>();
        Wire(beam, "Element", assemble, "Elements");
        Wire(support, "Support", assemble, "Supports");
        Wire(q, "LoadPattern", assemble, "LoadPatterns");

        var group = doc.Add<Alpaca4d.Gh.DesignGroupComponent>();
        Set(group, "Name", new GH_String("Joists"));
        Set(group, "ElementId", new GH_String("Joist"));
        foreach (var h in new[] { 0.16, 0.2, 0.24, 0.28, 0.32 })
            In(group, "Sections").AddSource(Out(Rectangle(0.1, h), "Section"));

        var optimiser = doc.Add<Alpaca4d.Gh.SectionOptimiser>();
        Wire(assemble, "AlpacaModel", optimiser, "AlpacaModel");
        Wire(group, "DesignGroup", optimiser, "DesignGroups");
        Set(optimiser, "LengthLT", new GH_Number(0.5));
        Check(optimiser.Params.Input.Any(p => p.Name == "ServiceClass") && optimiser.Params.Input.Any(p => p.Name == "γM"),
              "one Section Optimiser, with ServiceClass and γM among its inputs");

        doc.Solve();
        var messages = doc.Messages(GH_RuntimeMessageLevel.Error).Concat(doc.Messages(GH_RuntimeMessageLevel.Warning)).ToList();
        Check(messages.Count == 0, messages.Count == 0 ? "no error or warning: Design Group takes timber sections" : string.Join(" | ", messages));

        var section = Out(optimiser, "Section").VolatileData.AllData(true).First().ScriptVariable() as Alpaca4d.Section.RectangleCS;
        var utilisation = Convert.ToDouble(Out(optimiser, "Utilisation").VolatileData.AllData(true).First().ScriptVariable());
        var governing = Out(optimiser, "Governing").VolatileData.AllData(true).First().ToString();
        report.AppendLine($"  sized to {section?.Width * 1000:0} x {section?.Height * 1000:0}, utilisation {utilisation:0.000}, governed by {governing}");

        // M = 3 × 3.5 = 10.5 kNm at the fixed end; f_m,d = 0.8 × 24 / 1.3 with k_h 1 from 150 mm:
        // the depth that holds is the first whose W reaches 10.5 / 14.77 MPa, with k_h below 150 mm.
        double fmd = 0.8 * 24000 / 1.3;
        double Needed(double h) => 10.5 / (0.1 * h * h / 6) / (fmd * Math.Min(Math.Pow(Math.Max(150 / (h * 1000), 1.0), 0.2), 1.3));
        double first = new[] { 0.16, 0.2, 0.24, 0.28, 0.32 }.First(h => Needed(h) <= 1.0);
        Check(section != null && Math.Abs(section.Height - first) < 1e-9, $"the first depth whose bending holds: {first * 1000:0} mm");
        Check(Math.Abs(utilisation - Needed(first)) < 1e-6 && governing == "BendingZ", $"at its bending utilisation, {Needed(first):0.000}, k_mod 0.8 from Q's Duration");
        doc.Document.Enabled = false;
    }

    // ---------------------------------------------------------------- Utilisation's two types

    const string TimberUnit = "UtilisationTimber";

    static Alpaca4d.UIWidgets.EvaluationUnit Unit(GH_Component c) => ((Alpaca4d.UIWidgets.GH_SwitcherComponent)c).ActiveUnit;

    static string Names(IEnumerable<IGH_Param> ps) => string.Join(", ", ps.Select(p => p.Name));

    /// <summary>Each type's inputs, outputs, icon and message, and each read back as a file would.</summary>
    static void Types(GH_Component steel, GH_Component timber)
    {
        Section("Utilisation: a Steel type and a Timber type");
        Check(Unit(steel).Name == "Utilisation" && Unit(steel).DisplayName == "Steel", $"placed as it comes: Steel, under the one unit's old name ({Unit(steel).Name})");
        Check(Unit(timber).DisplayName == "Timber", "switched: Timber");
        Check(steel.Message.StartsWith("Steel") && timber.Message.StartsWith("Timber"), $"each says which below it: {steel.Message.Split('\n')[0]}, {timber.Message.Split('\n')[0]}");
        Check(Unit(steel).Icon != null && Unit(timber).Icon != null && !ReferenceEquals(Unit(steel).Icon, Unit(timber).Icon),
              "each has its icon");
        Check(Unit(timber).Icon.Width == 24 && Unit(timber).Icon.Height == 24, "the timber icon is 24 x 24");

        report.AppendLine("  Steel inputs:  " + Names(steel.Params.Input));
        report.AppendLine("  Timber inputs: " + Names(timber.Params.Input));
        report.AppendLine("  Timber outputs: " + Names(timber.Params.Output));
        var timberIn = timber.Params.Input.Select(p => p.Name).ToList();
        Check(!timberIn.Intersect(new[] { "Fabrication", "γM0", "γM1", "Sway" }).Any(), "Timber has none of steel's Fabrication, γM0, γM1, Sway");
        Check(new[] { "ServiceClass", "γM", "Combination", "LengthLT", "Load" }.All(timberIn.Contains), "and has ServiceClass, γM, Combination, LengthLT, Load");
        Check(!steel.Params.Input.Any(p => p.Name == "ServiceClass" || p.Name == "γM"), "Steel has no ServiceClass or γM");
        Check(timber.Params.Output.Any(p => p.Name == "kmod") && !timber.Params.Output.Any(p => p.Name == "Class"), "Timber gives kmod and no Class");
        Check(steel.Params.Output.Any(p => p.Name == "Class") && !steel.Params.Output.Any(p => p.Name == "kmod"), "Steel gives Class and no kmod");
        var tooLong = timber.Params.Input.Where(p => p.Description.Length > 120).Select(p => $"{p.Name} {p.Description.Length}").ToList();
        Check(tooLong.Count == 0, "Timber's input descriptions are 120 characters at most" + (tooLong.Count > 0 ? ": " + string.Join(", ", tooLong) : ""));

        // Saved and opened.
        foreach (var (component, name) in new[] { (steel, "Utilisation"), (timber, TimberUnit) })
        {
            var chunk = new GH_IO.Serialization.GH_LooseChunk("utilisation");
            component.Write(chunk);
            var read = new GH_IO.Serialization.GH_LooseChunk("utilisation");
            read.Deserialize_Binary(chunk.Serialize_Binary());
            var opened = new Alpaca4d.Gh.Utilisation();
            opened.CreateAttributes();
            Check(opened.Read(read) && Unit(opened).Name == name, $"saved on {Unit(component).DisplayName}, opened on {Unit(opened).DisplayName}");
        }
        var timberChunk = new GH_IO.Serialization.GH_LooseChunk("utilisation");
        Set(timber, "ServiceClass", new GH_Integer(2));
        timber.Write(timberChunk);
        var timberRead = new GH_IO.Serialization.GH_LooseChunk("utilisation");
        timberRead.Deserialize_Binary(timberChunk.Serialize_Binary());
        var reopened = new Alpaca4d.Gh.Utilisation();
        reopened.CreateAttributes();
        reopened.Read(timberRead);
        var kept = ((GH_PersistentParam<GH_Integer>)In(reopened, "ServiceClass")).PersistentData.AllData(true).Select(g => ((GH_Integer)g).Value).ToList();
        Check(kept.SequenceEqual(new[] { 2 }), $"and a ServiceClass set on the Timber type is kept ({string.Join(", ", kept)})");
        ((GH_PersistentParam<GH_Integer>)In(timber, "ServiceClass")).PersistentData.Clear();
    }

    // ---------------------------------------------------------------- a C24 cantilever

    static void Cantilever()
    {
        Section("A C24 cantilever, 3 m, 200 x 100: G permanent, Q medium-term, S not set");
        var doc = new Doc("cantilever");

        var material = doc.Add<Alpaca4d.Gh.MaterialLibraryElastic>();
        Set(material, "Material Name", new GH_String("C24"));
        var section = doc.Add<Alpaca4d.Gh.RectangleCS>();
        Set(section, "Width", new GH_Number(0.1));
        Set(section, "Height", new GH_Number(0.2));
        Wire(material, "Material", section, "Material");

        var beam = doc.Add<Alpaca4d.Gh.BeamBase>("ForceBeamColumn (Alpaca4d)");
        Set(beam, "Line", new GH_Curve(new LineCurve(new Point3d(0, 0, 0), new Point3d(3, 0, 0))));
        Wire(section, "Section", beam, "Section");

        var support = doc.Add<Alpaca4d.Gh.Support>();
        Set(support, "Position", new GH_Plane(new Plane(Point3d.Origin, Vector3d.XAxis, Vector3d.YAxis)));
        foreach (var dof in new[] { "Tx", "Ty", "Tz", "Rx", "Ry", "Rz" })
            Set(support, dof, new GH_Boolean(true));

        // Tip loads: G 1 kN, Q 2 kN, S 0.5 kN, down. Duration index: 0 Not set, 1 Permanent, 3 Medium-term.
        var g = Pattern(doc, "G", 1, Load(doc, -1));
        var q = Pattern(doc, "Q", 3, Load(doc, -2));
        var s = Pattern(doc, "S", 0, Load(doc, -0.5));

        var assemble = doc.Add<Alpaca4d.Gh.AssembleModel>();
        Wire(beam, "Element", assemble, "Elements");
        Wire(support, "Support", assemble, "Supports");
        foreach (var p in new[] { g, q, s })
            In(assemble, "LoadPatterns").AddSource(Out(p, "LoadPattern"));

        var combination = doc.Add<Alpaca4d.Gh.LoadCombinationComponent>();
        Wire(assemble, "AlpacaModel", combination, "AlpacaModel");
        Set(combination, "Name", new GH_String("ULS 1"), new GH_String("ULS 2"), new GH_String("ACC"));
        Set(combination, "Type", new GH_String("ULS"), new GH_String("ULS"), new GH_String("Accidental"));
        Set(combination, "Combination", new GH_String("1.35 G + 1.5 Q"), new GH_String("1.35 G"), new GH_String("G + Q"));

        var settings = doc.Add<Alpaca4d.Gh.AnalysisSettings>();
        var run = doc.Add<Alpaca4d.Gh.RunAnalysis>();
        Wire(combination, "AlpacaModel", run, "AlpacaModel");
        Wire(settings, "Settings", run, "Settings");

        var plain = doc.Add<Alpaca4d.Gh.Utilisation>(TimberUnit);
        Wire(run, "AlpacaModel", plain, "AlpacaModel");
        Set(plain, "Report", new GH_Boolean(true));

        var given = doc.Add<Alpaca4d.Gh.Utilisation>(TimberUnit);
        Wire(run, "AlpacaModel", given, "AlpacaModel");

        var steel = doc.Add<Alpaca4d.Gh.Utilisation>();
        Wire(run, "AlpacaModel", steel, "AlpacaModel");

        // Deconstruct Anything on the Report, then on its Checks. Its outputs are made beforehand, one
        // per public field and property and one for the class, as it makes them after its first solve.
        var deconstruct = Deconstructor(doc, typeof(Alpaca4d.Design.TimberReport));
        Wire(plain, "Report", deconstruct, "object");
        Set(given, "ServiceClass", new GH_Integer(3));
        Set(given, "γM", new GH_Number(1.5));
        Set(given, "Report", new GH_Boolean(true));

        Types(steel, plain);

        doc.Solve();
        var errors = doc.Messages(GH_RuntimeMessageLevel.Error).ToList();
        Check(errors.Count == 0, errors.Count == 0 ? "no component has an error" : string.Join(" | ", errors));
        var warnings = doc.Messages(GH_RuntimeMessageLevel.Warning).ToList();
        Check(warnings.Count == 0, warnings.Count == 0 ? "and none a warning" : string.Join(" | ", warnings));

        var type = Branches(plain, "Type");
        Check(type.Values.SelectMany(v => v).All(t => (string)t == "Timber"), "Type: Timber, from Material Library Elastic's C24");

        var reports = Branches(plain, "Report");
        Check(reports.Values.All(r => r[0] is Alpaca4d.Design.TimberReport), "Report: a TimberReport per combination");
        string Report(int place) => reports[place][0].ToString();
        report.AppendLine("  ULS 1 report, first lines:");
        foreach (var line in Report(0).Split('\n').Take(4))
            report.AppendLine("    " + line.TrimEnd());
        Check(Report(0).Contains("k_mod = 0.8: service class 1, medium-term loads (set by Q)"), "ULS 1 = 1.35 G + 1.5 Q: k_mod 0.8, medium-term, set by Q");
        Check(Report(1).Contains("k_mod = 0.6: service class 1, permanent loads (set by G)"), "ULS 2 = 1.35 G: k_mod 0.6, permanent, set by G");
        Check(Report(2).Contains("k_mod = 0.8") && Report(2).Contains("γM = 1 (Table 2.3, accidental combination)"), "ACC = G + Q: k_mod 0.8, γM 1.0");
        Check(Report(0).Contains("γM = 1.3 (Table 2.3, solid timber)"), "ULS 1: γM 1.3 for solid timber");

        // M at the fixed end: 3 m times the tip load. The ratios of the utilisations hold whichever
        // axis the load bends about: u = M / (W k_h f_m,k k_mod / γM).
        double m1 = 3 * (1.35 * 1 + 1.5 * 2), m2 = 3 * 1.35, m3 = 3 * (1 + 2);
        var bendingY = Branches(plain, "BendingY");
        var bendingZ = Branches(plain, "BendingZ");
        double Bending(int place) => Math.Max(Number(bendingY, place), Number(bendingZ, place));
        double u1 = Bending(0), u2 = Bending(1), u3 = Bending(2);
        report.AppendLine($"  bending: ULS 1 {u1:F4}, ULS 2 {u2:F4}, ACC {u3:F4}");
        Check(Near(u1 / u2, (m1 / 0.8) / (m2 / 0.6), 1e-6), $"ULS 1 / ULS 2 = (M1 / 0.8) / (M2 / 0.6): {u1 / u2:F4}");
        Check(Near(u3 / u2, (m3 / 0.8 * 1.0) / (m2 / 0.6 * 1.3), 1e-6), $"ACC / ULS 2 = (M3 / 0.8 × 1.0) / (M2 / 0.6 × 1.3): {u3 / u2:F4}");
        bool strong = Number(bendingZ, 0) > Number(bendingY, 0);
        double w = strong ? 0.1 * 0.2 * 0.2 / 6 : 0.2 * 0.1 * 0.1 / 6;
        double kh = strong ? 1.0 : Math.Pow(1.5, 0.2);
        Check(Near(u1, m1 / w / (0.8 / 1.3 * kh * 24000), 1e-6), $"ULS 1 by hand, bent about {(strong ? "z, across the 200 mm Height" : "y, across the 100 mm Width")}: {u1:F4}");

        // ServiceClass 3 and γM 1.5 given: k_mod 0.65 for medium-term, 0.5 for permanent; γM 1.5 even when accidental.
        var bendingY2 = Branches(given, "BendingY");
        var bendingZ2 = Branches(given, "BendingZ");
        double Given(int place) => Math.Max(Number(bendingY2, place), Number(bendingZ2, place));
        Check(Near(Given(0) / u1, (0.8 / 1.3) / (0.65 / 1.5), 1e-6), $"service class 3, γM 1.5: ULS 1 × {Given(0) / u1:F4}");
        Check(Near(Given(1) / u2, (0.6 / 1.3) / (0.5 / 1.5), 1e-6), $"ULS 2 × {Given(1) / u2:F4}");
        Check(Near(Given(2) / u3, (0.8 / 1.0) / (0.65 / 1.5), 1e-6), $"ACC × {Given(2) / u3:F4}: the γM given, not 1.0");

        var remarks = plain.RuntimeMessages(GH_RuntimeMessageLevel.Remark).ToList();
        foreach (var remark in remarks)
            report.AppendLine("  remark: " + remark);
        Check(remarks.Any(r => r.StartsWith("k_mod from the shortest load duration") && r.Contains("service class 1")),
              "a remark says how k_mod was found, in service class 1");
        Check(remarks.Any(r => r.StartsWith("Load pattern(s) S have no Duration")), "and names S, which has no duration");

        var kmod = Branches(plain, "kmod");
        Check(Number(kmod, 0) == 0.8 && Number(kmod, 1) == 0.6 && Number(kmod, 2) == 0.8,
              $"kmod: {Number(kmod, 0)}, {Number(kmod, 1)}, {Number(kmod, 2)} for ULS 1, ULS 2, ACC");
        Check(Number(Branches(given, "kmod"), 0) == 0.65, "and 0.65 in service class 3");

        // The Report through Deconstruct Anything, and its Checks through another.
        var kmodField = Branches(deconstruct, "Kmod");
        Check(kmodField.Count == 3 && Number(kmodField, 0) == 0.8 && Number(kmodField, 1) == 0.6 && Number(kmodField, 2) == 0.8,
              $"Deconstruct Anything on Report: Kmod {string.Join(", ", kmodField.Keys.Select(k => Number(kmodField, k)))}");
        Check((string)Branches(deconstruct, "GammaMSource")[2][0] == "Table 2.3, accidental combination", "and GammaMSource of ACC: " + Branches(deconstruct, "GammaMSource")[2][0]);
        var checks = Deconstructor(doc, typeof(Alpaca4d.Design.TimberReportCheck));
        In(checks, "object").AddSource(Out(deconstruct, "Checks"));
        doc.Solve();
        var names = Out(checks, "Name").VolatileData.AllData(true).Select(g => g.ToString()).ToList();
        var values = Out(checks, "Value").VolatileData.AllData(true).ToList();
        report.AppendLine($"  Checks, deconstructed: {names.Count} names, {values.Count} values; the first: {string.Join(", ", names.Take(12))}");
        Check(names.Count == 36 && names.Take(12).SequenceEqual(new[] { "Axial", "ShearY", "ShearZ", "Torsion", "BendingY", "BendingZ", "Combined", "BucklingY", "BucklingZ", "LTB", "InteractionY", "InteractionZ" }),
              "and on its Checks: twelve per combination, Axial to InteractionZ");
        var clauses = Out(checks, "Clause").VolatileData.AllData(true).Select(g => g.ToString()).ToList();
        Check(clauses[4] == "6.1.6" && clauses[9].StartsWith("6.3.3"), $"each with its clause: BendingY {clauses[4]}, LTB {clauses[9]}");
        var deconstructWarnings = deconstruct.RuntimeMessages(GH_RuntimeMessageLevel.Warning).Concat(checks.RuntimeMessages(GH_RuntimeMessageLevel.Warning)).ToList();
        Check(deconstructWarnings.Count == 0, deconstructWarnings.Count == 0 ? "Deconstruct Anything: no warning" : string.Join(" | ", deconstructWarnings));

        // The Steel type on the same model: the timber beam is not its to check.
        Check(Branches(steel, "Type").Values.SelectMany(v => v).All(x => (string)x == "Timber"), "Steel type: the beam's Type is Timber");
        Check(Branches(steel, "Max").Values.SelectMany(v => v).All(x => x == null), "and its Max is empty");
        var steelRemarks = steel.RuntimeMessages(GH_RuntimeMessageLevel.Remark).ToList();
        Check(steelRemarks.Any(r => r.StartsWith("1 beam(s) are timber, left to the Timber type")),
              "with a remark: " + steelRemarks.FirstOrDefault(r => r.Contains("timber")));
        Check(!steel.RuntimeMessages(GH_RuntimeMessageLevel.Warning).Any(), "and no warning");
        Check(!given.RuntimeMessages(GH_RuntimeMessageLevel.Remark).Any(r => r.Contains("in service class 1")),
              "the one with ServiceClass given does not say service class 1");
        doc.Document.Enabled = false;
    }
}
