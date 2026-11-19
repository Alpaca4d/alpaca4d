// The GeomTransf input of the beam component, through the component itself, in a running Rhino 8.
//
// Each check builds a Grasshopper document in code out of the real components - Uniaxial, RectangleCS,
// the beam switcher on its ForceBeamColumn and WithHinges types, a Value List, Assemble Model - solves
// it the way the canvas does, and looks at the element it gives: its transformation's type and local
// z, and every runtime message. grasshopper.sh loads this build of the plugin beside the installed one
// and calls Run by reflection.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Special;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using Alpaca4d.Element;

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

    public static string Run(string work)
    {
        folder = work;
        report = new StringBuilder();
        fails = 0;
        try
        {
            Words("ForceBeamColumn (Alpaca4d)");
            Words("WithHinges (Alpaca4d)");
            MadeInCode();
            NotAWord();
            ValueList();
            Assembled();
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
        readonly List<IGH_ActiveObject> objects = new List<IGH_ActiveObject>();

        public Doc(string name)
        {
            Document.FilePath = Path.Combine(folder, name + ".gh");
        }

        public T Add<T>(string unit = null) where T : GH_Component, new()
        {
            var component = new T();
            component.CreateAttributes();
            if (unit != null)
                component.GetType().GetMethod("SwitchUnit", new[] { typeof(string), typeof(bool), typeof(bool) }).Invoke(component, new object[] { unit, false, false });
            Document.AddObject(component, false);
            objects.Add(component);
            return component;
        }

        public void AddObject(IGH_ActiveObject obj)
        {
            obj.CreateAttributes();
            Document.AddObject(obj, false);
            objects.Add(obj);
        }

        public void Solve()
        {
            Document.Enabled = true;
            Document.NewSolution(true, GH_SolutionMode.Silent);
        }

        public List<string> Messages(GH_RuntimeMessageLevel level) =>
            objects.SelectMany(c => c.RuntimeMessages(level).Select(m => $"{c.NickName}: {m}")).ToList();
    }

    static IGH_Param In(GH_Component c, string name) =>
        c.Params.Input.FirstOrDefault(p => p.Name == name || p.NickName == name)
        ?? throw new Exception($"{c.Name} has no input {name} ({string.Join(", ", c.Params.Input.Select(p => p.NickName))})");

    static void Set<T>(GH_Component c, string input, params T[] values) where T : class, IGH_Goo
    {
        var param = (GH_PersistentParam<T>)In(c, input);
        param.PersistentData.Clear();
        foreach (var value in values)
            param.PersistentData.Append(value);
    }

    static void SetAny(GH_Component c, string input, params IGH_Goo[] values)
    {
        var param = In(c, input);
        var data = (GH_Structure<IGH_Goo>)param.GetType().GetProperty("PersistentData").GetValue(param);
        data.Clear();
        foreach (var value in values)
            data.Append(value);
    }

    static readonly LineCurve Span = new LineCurve(new Point3d(0, 0, 0), new Point3d(10, 0, 0));

    /// <summary>A beam of the given type on a 10 m line along X, on a 0.1 m square of the default material.</summary>
    static GH_Component Beam(Doc doc, string unit)
    {
        var material = doc.Add<Alpaca4d.Gh.Uniaxial>("UniaxialElastic (Alpaca4d)");
        var section = doc.Add<Alpaca4d.Gh.RectangleCS>();
        Set(section, "Width", new GH_Number(0.1));
        Set(section, "Height", new GH_Number(0.1));
        In(section, "Material").AddSource(material.Params.Output[0]);

        var beam = doc.Add<Alpaca4d.Gh.BeamBase>(unit);
        Set(beam, "Line", new GH_Curve(Span.DuplicateCurve()));
        In(beam, "Section").AddSource(section.Params.Output[0]);
        return beam;
    }

    /// <summary>The transformation of the one element the beam gave, or null if it gave none.</summary>
    static GeomTransf Transf(GH_Component beam)
    {
        var goo = beam.Params.Output[0].VolatileData.AllData(true).FirstOrDefault();
        var element = goo?.ScriptVariable();
        return element is ForceBeamColumn f ? f.GeomTransf : element is BeamWithHinges h ? h.GeomTransf : null;
    }

    static string Short(string unit) => unit.Replace(" (Alpaca4d)", "");

    // ---------------------------------------------------------------- the checks

    static void Words(string unit)
    {
        Section($"{Short(unit)}: the word on GeomTransf");
        Vector3d? plainZ = null;
        var cases = new (string word, GeomTransfType type)[]
        {
            (null, GeomTransfType.Linear), ("Linear", GeomTransfType.Linear), ("PDelta", GeomTransfType.PDelta),
            ("p-delta", GeomTransfType.PDelta), ("Corotational", GeomTransfType.Corotational), ("corot", GeomTransfType.Corotational),
        };
        foreach (var (word, type) in cases)
        {
            var doc = new Doc($"{Short(unit)}-{word ?? "empty"}");
            var beam = Beam(doc, unit);
            if (word != null)
                SetAny(beam, "GeomTransf", new GH_String(word));
            doc.Solve();
            var transf = Transf(beam);
            var errors = doc.Messages(GH_RuntimeMessageLevel.Error).Concat(doc.Messages(GH_RuntimeMessageLevel.Warning)).ToList();
            if (word == null && transf != null)
                plainZ = transf.LocalZ;
            Check(transf != null && transf.Type == type && errors.Count == 0,
                  $"{(word == null ? "empty" : $"\"{word}\"")}: {transf?.Type.ToString() ?? "no element"}{(errors.Count > 0 ? " | " + string.Join(" | ", errors) : "")}");
            if (word != null && transf != null && plainZ != null)
                Check((transf.LocalZ - plainZ.Value).Length < 1e-12, $"  on the beam's own local z, as empty gives it ({transf.LocalZ})");
        }
    }

    static void MadeInCode()
    {
        Section("A transformation made in code still goes in as it is");
        var doc = new Doc("made-in-code");
        var beam = Beam(doc, "ForceBeamColumn (Alpaca4d)");
        var made = new GeomTransf(GeomTransfType.Corotational, Span, Vector3d.YAxis);
        SetAny(beam, "GeomTransf", new GH_ObjectWrapper(made));
        doc.Solve();
        var transf = Transf(beam);
        Check(transf != null && transf.Type == GeomTransfType.Corotational && (transf.LocalZ - Vector3d.YAxis).Length < 1e-12 &&
              doc.Messages(GH_RuntimeMessageLevel.Error).Count == 0,
              $"Corotational with its own local z, (0,1,0): {transf?.Type.ToString() ?? "no element"} {transf?.LocalZ}");
    }

    static void NotAWord()
    {
        Section("Anything else is an error that names the three");
        var doc = new Doc("not-a-word");
        var beam = Beam(doc, "ForceBeamColumn (Alpaca4d)");
        SetAny(beam, "GeomTransf", new GH_String("Elastic"));
        doc.Solve();
        var errors = doc.Messages(GH_RuntimeMessageLevel.Error);
        Check(Transf(beam) == null, "no element");
        Check(errors.Count == 1 && errors[0].Contains("Linear, PDelta or Corotational"), $"the error: {string.Join(" | ", errors)}");
    }

    static void ValueList()
    {
        Section("A Value List on GeomTransf fills itself");
        var doc = new Doc("value-list");
        var beam = Beam(doc, "ForceBeamColumn (Alpaca4d)");
        var list = new GH_ValueList();
        doc.AddObject(list);
        In(beam, "GeomTransf").AddSource(list);
        doc.Solve();
        doc.Solve();
        var names = list.ListItems.Select(i => i.Name).ToList();
        Check(names.SequenceEqual(new[] { "Linear", "PDelta", "Corotational" }), $"its items: {string.Join(", ", names)}");
        Check(list.ListMode == GH_ValueListMode.DropDown, $"as a drop-down ({list.ListMode})");

        list.SelectItem(2);
        doc.Solve();
        var transf = Transf(beam);
        var messages = doc.Messages(GH_RuntimeMessageLevel.Error).Concat(doc.Messages(GH_RuntimeMessageLevel.Warning)).ToList();
        Check(transf != null && transf.Type == GeomTransfType.Corotational, $"Corotational picked: {transf?.Type.ToString() ?? "no element"}");
        Check(messages.Count == 0, messages.Count == 0 ? "no component has an error or a warning" : string.Join(" | ", messages));
    }

    static void Assembled()
    {
        Section("Assembled, the model writes it for OpenSees");
        var doc = new Doc("assembled");
        var beam = Beam(doc, "ForceBeamColumn (Alpaca4d)");
        SetAny(beam, "GeomTransf", new GH_String("Corotational"));
        var support = doc.Add<Alpaca4d.Gh.Support>();
        Set(support, "Position", new GH_Plane(Plane.WorldXY));
        foreach (var name in new[] { "Tx", "Ty", "Tz", "Rx", "Ry", "Rz" })
            Set(support, name, new GH_Boolean(true));
        var assemble = doc.Add<Alpaca4d.Gh.AssembleModel>();
        In(assemble, "Elements").AddSource(beam.Params.Output[0]);
        In(assemble, "Supports").AddSource(support.Params.Output[0]);
        doc.Solve();
        var goo = assemble.Params.Output[0].VolatileData.AllData(true).FirstOrDefault();
        var model = goo?.ScriptVariable() as Alpaca4d.Model;
        var lines = model?.Tcl.SelectMany(t => t.Split('\n')).Where(l => l.StartsWith("geomTransf")).ToList() ?? new List<string>();
        var why = model == null ? $"no model ({goo?.GetType().Name ?? "nothing"}; {string.Join(" | ", doc.Messages(GH_RuntimeMessageLevel.Error).Concat(doc.Messages(GH_RuntimeMessageLevel.Warning)))})"
                : lines.Count == 0 ? $"no geomTransf line in {model.Tcl.Count} pieces of Tcl" : string.Join(" | ", lines);
        Check(lines.Count == 1 && lines[0].StartsWith("geomTransf Corotational "), why);
    }
}
