// The branches the element components put their elements on, in a running Rhino 8.
//
// Assemble Model takes its elements as a list, so elements on two branches make two models, and
// the one without the beams has none of the nodes its supports are at. ASD Shell and Spring Link
// used to put theirs on {0;0}, beside the beam components' {0}: beams with a shell, or with a
// link, wired straight into Assemble Model failed with "Support node ... is not part of the model".
// grasshopper.sh loads this build of the plugin beside the installed one and calls Run by
// reflection.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
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

    public static string Run(string work, string license)
    {
        folder = work;
        report = new StringBuilder();
        fails = 0;
        try
        {
            if (File.Exists(license))
                Alpaca4d.License.License.Install(license);

            Paths();
            Mixed("beams and a shell", withShell: true, withLink: false);
            Mixed("beams and a link", withShell: false, withLink: true);
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
        public readonly List<GH_Component> Components = new List<GH_Component>();

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
            Components.Add(component);
            return component;
        }

        public void Solve()
        {
            Document.Enabled = true;
            Document.NewSolution(true, GH_SolutionMode.Silent);
        }
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

    static GH_Curve Line(Point3d a, Point3d b) => new GH_Curve(new LineCurve(a, b));

    static GH_Plane At(Point3d p) => new GH_Plane(new Plane(p, Vector3d.XAxis, Vector3d.YAxis));

    static string Paths(GH_Component c) => string.Join(" ", c.Params.Output[0].VolatileData.Paths);

    static Mesh Quad(Point3d a, Point3d b, Point3d c, Point3d d)
    {
        var mesh = new Mesh();
        mesh.Vertices.Add(a); mesh.Vertices.Add(b); mesh.Vertices.Add(c); mesh.Vertices.Add(d);
        mesh.Faces.AddFace(0, 1, 2, 3);
        return mesh;
    }

    static GH_Component BeamSection(Doc doc)
    {
        var material = doc.Add<Alpaca4d.Gh.Uniaxial>("UniaxialElastic (Alpaca4d)");
        var section = doc.Add<Alpaca4d.Gh.RectangleCS>();
        Set(section, "Width", new GH_Number(0.3));
        Set(section, "Height", new GH_Number(0.5));
        Wire(material, "Material", section, "Material");
        return section;
    }

    static GH_Component ShellSection(Doc doc)
    {
        var material = doc.Add<SimplexGh.nD>("ElasticIsotropic (Alpaca4d)");
        Set(material, "E", new GH_Number(30000000.0));
        Set(material, "G", new GH_Number(15000000.0));
        Set(material, "ν", new GH_Number(0.2));
        var plate = doc.Add<Alpaca4d.Gh.PlateFiberSection>();
        Set(plate, "Thickness", new GH_Number(0.2));
        Wire(material, "Material", plate, "Material");
        return plate;
    }

    // ---------------------------------------------------------------- the checks

    /// <summary>Each element component on its own: where its elements land, one item in or several.</summary>
    static void Paths()
    {
        Section("Where each element component puts its elements");
        var doc = new Doc("paths");
        var section = BeamSection(doc);
        var plate = ShellSection(doc);
        var o = new Point3d(0, 0, 0); var x = new Point3d(3, 0, 0); var y = new Point3d(0, 3, 0); var xy = new Point3d(3, 3, 0);
        var x2 = new Point3d(6, 0, 0); var xy2 = new Point3d(6, 3, 0);

        var beams = doc.Add<Alpaca4d.Gh.BeamBase>("ForceBeamColumn (Alpaca4d)");
        Set(beams, "Line", Line(o, x), Line(o, y));
        Wire(section, "Section", beams, "Section");

        var shell = doc.Add<Alpaca4d.Gh.ASDShell>();
        Set(shell, "Mesh", new GH_Mesh(Quad(o, x, xy, y)));
        Wire(plate, "Section", shell, "Section");

        var twoFaces = Quad(o, x, xy, y);
        twoFaces.Vertices.Add(x2); twoFaces.Vertices.Add(xy2); twoFaces.Faces.AddFace(1, 4, 5, 2);
        var faces = doc.Add<Alpaca4d.Gh.ASDShell>();
        Set(faces, "Mesh", new GH_Mesh(twoFaces));
        Wire(plate, "Section", faces, "Section");

        var meshes = doc.Add<Alpaca4d.Gh.ASDShell>();
        Set(meshes, "Mesh", new GH_Mesh(Quad(o, x, xy, y)), new GH_Mesh(Quad(x, x2, xy2, xy)));
        Set(meshes, "ElementId", new GH_String("A"), new GH_String("B"));
        Wire(plate, "Section", meshes, "Section");

        var spring = doc.Add<Alpaca4d.Gh.Uniaxial>("UniaxialElastic (Alpaca4d)");
        var links = doc.Add<Alpaca4d.Gh.Link>();
        Set(links, "Line", new GH_Line(new Rhino.Geometry.Line(o, x)), new GH_Line(new Rhino.Geometry.Line(o, y)));
        Wire(spring, "Material", links, "Material");
        Set(links, "Direction", new GH_Integer(1));

        var grafted = doc.Add<Alpaca4d.Gh.ASDShell>();
        Set(grafted, "Mesh", new GH_Mesh(Quad(o, x, xy, y)), new GH_Mesh(Quad(x, x2, xy2, xy)));
        Wire(plate, "Section", grafted, "Section");
        grafted.Params.Output[0].DataMapping = GH_DataMapping.Graft;
        doc.Solve();

        Check(Paths(beams) == "{0}", $"two beams: {Paths(beams)}, the branch the shells and links have to match");
        Check(Paths(shell) == "{0}", $"a one-face mesh into ASD Shell: {Paths(shell)}");
        Check(Paths(faces) == "{0}" && faces.Params.Output[0].VolatileData.DataCount == 2, $"a two-face mesh: {Paths(faces)}, both shells");
        Check(Paths(meshes) == "{0}" && meshes.Params.Output[0].VolatileData.DataCount == 2, $"two meshes: {Paths(meshes)}, one list, as two lines make one list of beams");
        var ids = meshes.Params.Output[0].VolatileData.AllData(true).Select(g => ((Alpaca4d.Generic.IElement)((IGH_Goo)g).ScriptVariable()).ElementId).ToList();
        Check(ids.SequenceEqual(new[] { "A", "B" }), $"and each mesh still gets its own ElementId: {string.Join(", ", ids)}");
        Check(Paths(links) == "{0}" && links.Params.Output[0].VolatileData.DataCount == 2, $"two lines into Spring Link: {Paths(links)}");
        Check(grafted.Params.Output[0].VolatileData.PathCount == 2, $"a Graft on ASD Shell's output still grafts: {Paths(grafted)}");
    }

    /// <summary>A portal frame with a shell on top, or a link brace, wired straight into Assemble Model.</summary>
    static void Mixed(string what, bool withShell, bool withLink)
    {
        Section($"Assemble Model with {what}, nothing flattened");
        var doc = new Doc("mixed-" + what.Replace(' ', '-'));
        var section = BeamSection(doc);
        var footA = new Point3d(0, 0, 0); var kneeA = new Point3d(0, 0, 4);
        var footB = new Point3d(6, 0, 0); var kneeB = new Point3d(6, 0, 4);
        var frame = doc.Add<Alpaca4d.Gh.BeamBase>("ForceBeamColumn (Alpaca4d)");
        Set(frame, "Line", Line(footA, kneeA), Line(footB, kneeB), Line(kneeA, kneeB));
        Wire(section, "Section", frame, "Section");
        var elements = new List<GH_Component> { frame };
        var supports = new List<GH_Component>();

        var feet = doc.Add<Alpaca4d.Gh.Support>();
        Set(feet, "Position", At(footA), At(footB));
        supports.Add(feet);

        if (withShell)
        {
            var plate = ShellSection(doc);
            var shell = doc.Add<Alpaca4d.Gh.ASDShell>();
            Set(shell, "Mesh", new GH_Mesh(Quad(kneeA, kneeB, new Point3d(6, 3, 4), new Point3d(0, 3, 4))));
            Wire(plate, "Section", shell, "Section");
            elements.Add(shell);

            var corners = doc.Add<Alpaca4d.Gh.Support>();
            Set(corners, "Position", At(new Point3d(6, 3, 4)), At(new Point3d(0, 3, 4)));
            supports.Add(corners);
        }
        if (withLink)
        {
            var spring = doc.Add<Alpaca4d.Gh.Uniaxial>("UniaxialElastic (Alpaca4d)");
            var link = doc.Add<Alpaca4d.Gh.Link>();
            Set(link, "Line", new GH_Line(new Rhino.Geometry.Line(kneeA, footB)));
            Wire(spring, "Material", link, "Material");
            Set(link, "Direction", new GH_Integer(1));
            elements.Add(link);
        }

        var series = doc.Add<Alpaca4d.Gh.LinearTimeSeries>();
        var load = doc.Add<Alpaca4d.Gh.PointLoad>();
        Set(load, "Point", new GH_Point(kneeA));
        Set(load, "Force", new GH_Vector(new Vector3d(10, 0, 0)));
        var pattern = doc.Add<Alpaca4d.Gh.PatternBase>("PlainPattern (Alpaca4d)");
        Wire(series, "TimeSeries", pattern, "TimeSeries");
        In(pattern, "Loads").AddSource(load.Params.Output[0]);

        var assemble = doc.Add<Alpaca4d.Gh.AssembleModel>();
        foreach (var element in elements)
            In(assemble, "Elements").AddSource(element.Params.Output[0]);
        foreach (var support in supports)
            In(assemble, "Supports").AddSource(support.Params.Output[0]);
        In(assemble, "LoadPatterns").AddSource(pattern.Params.Output[0]);
        doc.Solve();

        var errors = doc.Components.SelectMany(c => c.RuntimeMessages(GH_RuntimeMessageLevel.Error).Select(m => $"{c.NickName}: {m}")).ToList();
        var models = Out(assemble, "AlpacaModel").VolatileData.AllData(true).Select(g => (Alpaca4d.Model)((IGH_Goo)g).ScriptVariable()).ToList();
        Check(errors.Count == 0, errors.Count == 0 ? "no component has an error" : string.Join(" | ", errors));
        // Three beams, and the shell or the link.
        Check(models.Count == 1 && models[0].Elements.Count == 4,
              $"one model, with all four elements in it ({models.Count} model(s), {models.FirstOrDefault()?.Elements.Count} elements; element paths {string.Join(", ", elements.Select(Paths))})");
    }
}
