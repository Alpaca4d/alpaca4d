// Model View through the Grasshopper components themselves, in a running Rhino 8.
//
// A plate of ASD shells with edge beams, supports and an area load, built in code out of the real
// components, assembled and analysed. Model View draws it on its own and with its results, and what
// it hands out as Geometry, Info and Values is looked at; so is a change that needs only a redraw.
// The old Model View and View Results_WIP are put through Upgrade Components, and the upgraded
// component is solved to show what the old one showed. Last, the old components are timed against
// the new one on a plate of 3000 shells. grasshopper.sh loads this build of the plugin beside the
// installed one and calls Run by reflection.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;
using Rhino.Display;
using Rhino.Geometry;

using Alpaca4d;
using Alpaca4d.UIWidgets;

#pragma warning disable CS0612, CS0618

public static class GrasshopperCheck
{
    static StringBuilder report;
    static int fails;
    static string folder;

    const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

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

    static void Line(string text) => report.AppendLine(text);

    public static string Run(string work, string license)
    {
        folder = work;
        report = new StringBuilder();
        fails = 0;
        try
        {
            if (!string.IsNullOrEmpty(license))
                Alpaca4d.License.License.Install(license);

            var small = new Plate("small", 12, 10);
            ModelAlone(small);
            Results(small);
            Together(small);
            AllForces(small);
            Filtered(small);
            DisplayOnly(small);
            Drawn(small);
            LoadsDrawn(small);
            LoadsByPattern(small);
            LoadsDeformed(small);
            UpgradeModelView(small);
            UpgradeViewResults(small);

            Timings(new Plate("large", 60, 50));
        }
        catch (Exception e)
        {
            Check(false, "no exception: " + e);
        }
        report.AppendLine();
        report.AppendLine(fails == 0 ? "all grasshopper checks passed" : $"{fails} grasshopper check(s) failed");
        return report.ToString();
    }

    // ---------------------------------------------------------------- the model

    /// <summary>
    /// A plate of nx by ny ASD quads, 12 by 10 m and 0.25 m thick, with edge beams along y = 0 and
    /// y = 10, held along x = 0 and x = 12 and loaded 5 kN/m² down. Assembled and analysed.
    /// </summary>
    sealed class Plate
    {
        public readonly GH_Document Document = new GH_Document();
        public readonly GH_Component Assemble, Analysis;
        public readonly GH_Component ShellElements, BeamElements, SupportsMade, AreaPattern;
        public readonly int Shells, Beams, Supports;

        public Plate(string name, int nx, int ny)
        {
            // Run Analysis writes its deck and results beside the document.
            Document.FilePath = Path.Combine(folder, name + ".gh");

            double lx = 12, ly = 10;
            var mesh = new Mesh();
            for (int j = 0; j <= ny; j++)
                for (int i = 0; i <= nx; i++)
                    mesh.Vertices.Add(lx * i / nx, ly * j / ny, 0.0);
            for (int j = 0; j < ny; j++)
                for (int i = 0; i < nx; i++)
                {
                    int a = j * (nx + 1) + i;
                    mesh.Faces.AddFace(a, a + 1, a + nx + 2, a + nx + 1);
                }

            var material = Add<SimplexGh.nD>("ElasticIsotropic (Alpaca4d)");
            Set(material, "E", new GH_Number(30000000.0));
            Set(material, "G", new GH_Number(12500000.0));
            Set(material, "ν", new GH_Number(0.2));
            Set(material, "Rho", new GH_Number(0.0));
            var section = Add<Alpaca4d.Gh.PlateFiberSection>();
            Set(section, "Thickness", new GH_Number(0.25));
            Wire(material, "Material", section, "Material");
            var shell = Add<Alpaca4d.Gh.ASDShell>();
            Set(shell, "Mesh", new GH_Mesh(mesh));
            Wire(section, "Section", shell, "Section");

            var steel = Add<Alpaca4d.Gh.Uniaxial>("UniaxialElastic (Alpaca4d)");
            var rectangle = Add<Alpaca4d.Gh.RectangleCS>();
            Set(rectangle, "Width", new GH_Number(0.3));
            Set(rectangle, "Height", new GH_Number(0.5));
            Wire(steel, "Material", rectangle, "Material");
            var beams = Add<Alpaca4d.Gh.BeamBase>("ForceBeamColumn (Alpaca4d)");
            var lines = new List<GH_Curve>();
            foreach (double y in new[] { 0.0, ly })
                for (int i = 0; i < nx; i++)
                    lines.Add(new GH_Curve(new LineCurve(new Point3d(lx * i / nx, y, 0), new Point3d(lx * (i + 1) / nx, y, 0))));
            Set(beams, "Line", lines.ToArray());
            Wire(rectangle, "Section", beams, "Section");

            var support = Add<Alpaca4d.Gh.Support>();
            var planes = new List<GH_Plane>();
            foreach (double x in new[] { 0.0, lx })
                for (int j = 0; j <= ny; j++)
                    planes.Add(new GH_Plane(new Plane(new Point3d(x, ly * j / ny, 0), Vector3d.XAxis, Vector3d.YAxis)));
            Set(support, "Position", planes.ToArray());

            var load = Add<Alpaca4d.Gh.MeshLoad>();
            In(load, "Shell|Id").AddSource(Out(shell, "Element"));
            Set(load, "Force", new GH_Vector(new Vector3d(0, 0, -5)));

            var series = Add<Alpaca4d.Gh.LinearTimeSeries>();
            var pattern = Add<Alpaca4d.Gh.PatternBase>("PlainPattern (Alpaca4d)");
            Wire(series, "TimeSeries", pattern, "TimeSeries");
            In(pattern, "Loads").AddSource(load.Params.Output[0]);

            Assemble = Add<Alpaca4d.Gh.AssembleModel>();
            Wire(shell, "Element", Assemble, "Elements");
            Wire(beams, "Element", Assemble, "Elements");
            Wire(support, "Support", Assemble, "Supports");
            In(Assemble, "LoadPatterns").AddSource(pattern.Params.Output[0]);

            var settings = Add<Alpaca4d.Gh.AnalysisSettings>();
            Analysis = Add<Alpaca4d.Gh.RunAnalysis>();
            Wire(Assemble, "AlpacaModel", Analysis, "AlpacaModel");
            Wire(settings, "Settings", Analysis, "Settings");

            ShellElements = shell;
            BeamElements = beams;
            SupportsMade = support;
            AreaPattern = pattern;
            Shells = nx * ny;
            Beams = 2 * nx;
            Supports = 2 * (ny + 1);
            Solve();
        }

        public T Add<T>(string unit = null) where T : GH_Component, new()
        {
            var component = new T();
            component.CreateAttributes();
            if (unit != null)
                component.GetType().GetMethod("SwitchUnit", new[] { typeof(string), typeof(bool), typeof(bool) }).Invoke(component, new object[] { unit, false, false });
            Document.AddObject(component, false);
            return component;
        }

        /// <summary>A Model View on the analysed model, or on the assembled one.</summary>
        public Alpaca4d.Gh.ModelView View(bool analysed = true)
        {
            var view = Add<Alpaca4d.Gh.ModelView>();
            Wire(analysed ? Analysis : Assemble, "AlpacaModel", view, "AlpacaModel");
            return view;
        }

        public void Solve()
        {
            Document.Enabled = true;
            Document.NewSolution(true, GH_SolutionMode.Silent);
        }

        /// <summary>One component expired and solved again, as a change on the canvas does.</summary>
        public void Solve(GH_Component component)
        {
            component.ExpireSolution(false);
            Document.NewSolution(false, GH_SolutionMode.Silent);
        }
    }

    // ---------------------------------------------------------------- helpers

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

        // Read again on the next solution, as one typed in on the canvas is.
        param.ExpireSolution(false);
    }

    static void Wire(GH_Component from, string output, GH_Component to, string input) =>
        In(to, input).AddSource(Out(from, output));

    static T Field<T>(object component, string name)
    {
        for (var type = component.GetType(); type != null; type = type.BaseType)
        {
            var field = type.GetField(name, Any);
            if (field != null) return (T)field.GetValue(component);
        }
        throw new Exception($"{component.GetType().Name} has no field {name}");
    }

    static object Call(object component, string name, params object[] args) =>
        component.GetType().GetMethod(name, Any).Invoke(component, args);

    /// <summary>
    /// One result drawn alone, at a component, or with <paramref name="entry"/> null, none: Model
    /// View's checkboxes, or View Results' dropdown.
    /// </summary>
    static void Show(GH_Component view, string entry, string component = null)
    {
        if (view is Alpaca4d.Gh.ModelView)
        {
            foreach (var result in Field<Array>(view, "_results"))
                Member<MenuCheckBox>(result, "Tick").Active = false;
            if (entry != null)
                Tick(view, entry, true, component);
            Call(view, "UpdateVisibility");
            return;
        }

        var show = Field<MenuDropDown>(view, "_ddFamily");
        show.Value = show.Items.FindIndex(x => x.content == entry);
        if (show.Value < 0) throw new Exception($"no {entry} under Result");
        Call(view, "SyncComponentList");
        if (component != null)
        {
            var dropdown = Field<MenuDropDown>(view, "_ddComponent");
            dropdown.Value = dropdown.Items.FindIndex(x => x.content == component);
        }
        Call(view, "UpdateVisibility");
    }

    /// <summary>One of Model View's results ticked or unticked, leaving the rest as they are, at a component.</summary>
    static void Tick(GH_Component view, string entry, bool on, string component = null)
    {
        var result = Result(view, entry);
        Member<MenuCheckBox>(result, "Tick").Active = on;
        if (component != null)
        {
            // Beam forces have a row of checkboxes, the rest a dropdown.
            var row = Member<MenuCheckBoxGroup>(result, "Components");
            var dropdown = Member<MenuDropDown>(result, "Component");
            int index = row != null ? row.Labels.ToList().IndexOf(component) : dropdown.Items.FindIndex(x => x.content == component);
            if (index < 0) throw new Exception($"no {component} under {entry}");
            if (row != null) row.TickOnly(index);
            else dropdown.Value = index;
        }
        Call(view, "UpdateVisibility");
    }

    /// <summary>A result's controls in Model View's Results menu, by its name there.</summary>
    static object Result(GH_Component view, string entry)
    {
        foreach (var result in Field<Array>(view, "_results"))
            if (Member<MenuCheckBox>(result, "Tick").Tag == entry)
                return result;
        throw new Exception($"no {entry} under Results");
    }

    static bool Ticked(GH_Component view, string entry) => Member<MenuCheckBox>(Result(view, entry), "Tick").Active;

    /// <summary>A public field of one of the plugin's internal classes.</summary>
    static T Member<T>(object o, string name) => (T)o.GetType().GetField(name).GetValue(o);

    /// <summary>The values of an output, a list per branch.</summary>
    static List<List<double>> Branches(GH_Component c, string output)
    {
        var data = Out(c, output).VolatileData;
        return data.Paths.Select(path => data.get_Branch(path).Cast<GH_Number>().Where(x => x != null).Select(x => x.Value).ToList()).ToList();
    }

    static List<string> Messages(GH_Component c, GH_RuntimeMessageLevel level) => c.RuntimeMessages(level).ToList();

    static string Text(GH_Component c, string output) =>
        Out(c, output).VolatileData.AllData(true).Select(x => x.ToString()).FirstOrDefault() ?? "";

    static List<string> Texts(GH_Component c, string output) =>
        Out(c, output).VolatileData.AllData(true).Select(x => x.ToString()).ToList();

    static List<double> Numbers(GH_Component c, string output) =>
        Out(c, output).VolatileData.AllData(true).Select(x => ((GH_Number)x).Value).ToList();

    static List<GeometryBase> Geometry(GH_Component c) =>
        Out(c, "Geometry").VolatileData.AllData(true).Select(x => GH_Convert.ToGeometryBase(x)).Where(x => x != null).ToList();

    static bool Coloured(Mesh mesh) => mesh.VertexColors.Count == mesh.Vertices.Count && mesh.Vertices.Count > 0;

    static double Low(GH_Component c) => Geometry(c).Select(g => g.GetBoundingBox(false).Min.Z).DefaultIfEmpty(0.0).Min();

    static string Said(List<string> messages) => messages.Count == 0 ? "" : ": " + string.Join(" | ", messages);

    // ---------------------------------------------------------------- checks

    static void ModelAlone(Plate plate)
    {
        Section("No result ticked, on the assembled model");
        var view = plate.View(analysed: false);
        plate.Solve(view);

        var errors = Messages(view, GH_RuntimeMessageLevel.Error).Concat(Messages(view, GH_RuntimeMessageLevel.Warning)).ToList();
        Check(errors.Count == 0, "no errors or warnings" + Said(errors));
        Check(Text(view, "Info") == "Model - coloured by Element", $"Info says what is drawn ({Text(view, "Info")})");
        Check(Out(view, "AlpacaModel").VolatileData.DataCount == 1, "the model is passed through");

        var geometry = Geometry(view);
        var meshes = geometry.OfType<Mesh>().ToList();
        Check(meshes.Count == 2 && meshes.All(Coloured), $"Geometry: the shells and the extruded beams, each a coloured mesh ({meshes.Count} meshes)");
        Check(geometry.OfType<Curve>().Count() == 0, "and no lines, as every beam has a section to extrude");

        // Two offset halves per shell, eight vertices to a face of four.
        int shellVertices = meshes.Max(m => m.Vertices.Count);
        Check(shellVertices >= plate.Shells * 8, $"the shells drawn as thick as their section ({shellVertices} vertices for {plate.Shells} shells)");

        Field<MenuCheckBox>(view, "_ckExtruded").Active = false;
        plate.Solve(view);
        geometry = Geometry(view);
        Check(geometry.OfType<Curve>().Count() == plate.Beams, $"Extruded off: the beams as lines ({geometry.OfType<Curve>().Count()})");
        Field<MenuCheckBox>(view, "_ckExtruded").Active = true;

        Field<MenuCheckBox>(view, "_ckDeformed").Active = true;
        plate.Solve(view);
        Check(Messages(view, GH_RuntimeMessageLevel.Remark).Any(m => m.Contains("analysed model")),
              "Deformed shape on a model not analysed: a remark, and the model drawn as built");
        Field<MenuCheckBox>(view, "_ckDeformed").Active = false;

        Show(view, "Displacement");
        plate.Solve(view);
        Check(Messages(view, GH_RuntimeMessageLevel.Warning).Any(m => m.Contains("analysed model")),
              "a result on a model not analysed: a warning to analyse it or untick it" + Said(Messages(view, GH_RuntimeMessageLevel.Warning)));
        Check(Geometry(view).OfType<Mesh>().Count() == 2 && Text(view, "Info") == "Model - coloured by Element",
              $"and the model drawn as it is ({Text(view, "Info")})");
        Show(view, null);
    }

    static void Results(Plate plate)
    {
        Section("Results, on the analysed model");
        var view = plate.View();

        Show(view, "Displacement", "Uz");
        plate.Solve(view);
        var errors = Messages(view, GH_RuntimeMessageLevel.Error).Concat(Messages(view, GH_RuntimeMessageLevel.Warning)).ToList();
        Check(errors.Count == 0, "Displacement: no errors or warnings" + Said(errors));
        Check(Text(view, "Info").StartsWith("Displacement - Uz [mm]"), $"Info ({Text(view, "Info")})");
        var values = Numbers(view, "Values");
        Check(values.Count == Out(view, "Colors").VolatileData.DataCount && values.Count > 1 && values.Min() < 0.0,
              $"Values: one per colour, down to the sag ({values.Count}, {values.Min():G4} to {values.Max():G4})");
        var flat = Geometry(view).OfType<Mesh>().ToList();
        Check(flat.Count >= 1 && flat.All(Coloured), "Geometry: coloured meshes");

        // The plate sags, so deformed - a hundred times over - what is drawn reaches lower.
        double flatLow = Low(view);
        Field<MenuCheckBox>(view, "_ckDeformed").Active = true;
        Field<MenuSlider>(view, "_slDeformScale").Value = 100.0;
        plate.Solve(view);
        double low = Low(view);
        Check(low < flatLow - 0.01, $"Deformed shape: drawn sagging ({flatLow:F3} m at the lowest flat, {low:F3} m deformed)");
        Field<MenuCheckBox>(view, "_ckDeformed").Active = false;

        Show(view, "Beam forces", "My");
        plate.Solve(view);
        errors = Messages(view, GH_RuntimeMessageLevel.Error).ToList();
        Check(errors.Count == 0 && Text(view, "Info").StartsWith("Beam forces - My"), $"Beam forces: {Text(view, "Info")}" + Said(errors));
        Check(Geometry(view).OfType<Mesh>().Count() == 3, "Geometry: the shells and beams plain, and the diagrams");

        Show(view, "Shell forces", "mxx");
        plate.Solve(view);
        Check(Messages(view, GH_RuntimeMessageLevel.Error).Count == 0 && Text(view, "Info").StartsWith("Shell forces - mxx"), $"Shell forces: {Text(view, "Info")}");

        Show(view, "Reactions", "Fz");
        plate.Solve(view);
        var arrows = Geometry(view).OfType<Curve>().Count();
        Check(arrows == plate.Supports, $"Reactions: an arrow from each support, as a line ({arrows} of {plate.Supports})");

        Show(view, null);
        plate.Solve(view);
        flatLow = Low(view);
        Field<MenuCheckBox>(view, "_ckDeformed").Active = true;
        plate.Solve(view);
        low = Low(view);
        Check(Messages(view, GH_RuntimeMessageLevel.Remark).Count == 0 && low < flatLow - 0.01,
              $"No result on the analysed model: drawn on the deformed shape when asked ({flatLow:F3} m flat, {low:F3} m deformed)");
        Field<MenuCheckBox>(view, "_ckDeformed").Active = false;
    }

    static void Together(Plate plate)
    {
        Section("Several results ticked together");
        var view = plate.View();
        Tick(view, "Displacement", true, "Uz");
        Tick(view, "Beam forces", true, "My");
        Tick(view, "Shell stresses", true, "VonMises");
        Tick(view, "Reactions", true, "Fz");
        plate.Solve(view);

        var problems = Messages(view, GH_RuntimeMessageLevel.Error).Concat(Messages(view, GH_RuntimeMessageLevel.Warning))
                                                                      .Concat(Messages(view, GH_RuntimeMessageLevel.Remark)).ToList();
        Check(problems.Count == 0, "no errors, warnings or remarks" + Said(problems));

        var info = Texts(view, "Info");
        Check(info.Count == 4 && info[0].StartsWith("Displacement - Uz") && info[1].StartsWith("Beam forces - My")
              && info[2].StartsWith("Shell stresses - VonMises") && info[3].StartsWith("Reactions - Fz"),
              "Info: a line per result, in the order of the menu" + Environment.NewLine + "      " + string.Join(Environment.NewLine + "      ", info));

        var values = Branches(view, "Values");
        var colours = Out(view, "Colors").VolatileData;
        Check(values.Count == 3 && values.All(b => b.Count > 1) && colours.PathCount == 3,
              $"Values and Colors: a branch for each result coloured by the gradient, beam forces left out ({values.Count}, {colours.PathCount})");
        Check(values.Count == 3 && values[0].Min() < 0.0 && values[1].Min() >= 0.0,
              "the first the sag, the second the Von Mises stress, each over its own range");

        // Each element in one colour: the shells take their stress, the beams the displacement.
        var shown = Field<System.Collections.IList>(view, "_shown").Cast<object>().ToList();
        int ColouredBy(string family) => shown.Where(s => s.GetType().GetField("Family").GetValue(s).ToString() == family)
                                            .Select(s => ((System.Collections.ICollection)s.GetType().GetField("Elements").GetValue(s)).Count)
                                            .FirstOrDefault();
        Check(ColouredBy("ShellStress") == plate.Shells && ColouredBy("Displacement") == plate.Beams,
              $"the shells coloured by their stress ({ColouredBy("ShellStress")} of {plate.Shells}), the beams by the displacement ({ColouredBy("Displacement")} of {plate.Beams})");

        var geometry = Geometry(view);
        Check(geometry.OfType<Mesh>().Count() == 3 && geometry.OfType<Mesh>().All(m => m.VertexColors.Count == m.Vertices.Count),
              "Geometry: the coloured shells, the coloured beams and the diagrams");
        Check(geometry.OfType<Curve>().Count() == plate.Supports, $"and an arrow from each support ({geometry.OfType<Curve>().Count()} of {plate.Supports})");

        Set(view, "Range", new GH_Interval(new Interval(-20.0, 0.0)), new GH_Interval(new Interval(0.0, 3000.0)));
        plate.Solve(view);
        values = Branches(view, "Values");
        Check(values.Count == 3 && values[0].First() == -20.0 && values[0].Last() == 0.0 && values[1].First() == 0.0 && values[1].Last() == 3000.0
              && values[2].Last() != 3000.0,
              "Range: the first interval to the displacement, the second to the stress, the reactions left to fit (" + string.Join(" / ", values.Select(b => string.Join(",", b.Select(x => x.ToString("G4"))))) + ")");

        // A Legend fed Values and Colors draws a legend per result, each with its own title.
        var legend = plate.Add<Alpaca4d.Gh.Legend>();
        try
        {
            Wire(view, "Values", legend, "Values");
            Wire(view, "Colors", legend, "Colors");
            Set(legend, "Title", new GH_String("Uz [mm]"), new GH_String("Von Mises [MPa]"), new GH_String("Fz [kN]"));
            plate.Solve();
            var conduit = Field<object>(legend, "_conduit");
            var entries = Field<System.Collections.IEnumerable>(conduit, "_entries").Cast<object>()
                .Select(e => Member<string>(e, "Title") + ": " + string.Join(",", Member<List<string>>(e, "Labels").Take(1))).ToList();
            Check(string.Join(" | ", entries) == "Uz [mm]: -20 | Von Mises [MPa]: 0 | Fz [kN]: 19.24",
                  "Legend: a legend per result, each with its title and its own scale (" + string.Join(" | ", entries) + ")");
        }
        finally
        {
            // A Legend draws for as long as its conduit is on, document or not.
            plate.Document.RemoveObject(legend, false);
            var conduit = Field<object>(legend, "_conduit") as DisplayConduit;
            if (conduit != null) conduit.Enabled = false;
        }

        // Shell forces and shell stresses colour the same faces. The menu unticks one; ticked both
        // all the same, the first in the menu colours them and the other says why it is missing.
        Set<GH_Interval>(view, "Range");
        Tick(view, "Shell forces", true, "mxx");
        plate.Solve(view);
        var remarks = Messages(view, GH_RuntimeMessageLevel.Remark);
        Check(remarks.Any(m => m.StartsWith("Shell stresses colour nothing")), "Shell forces and Shell stresses both ticked: a remark for the one hidden" + Said(remarks));

        Show(view, null);
        plate.Solve(view);
        Check(Texts(view, "Info").Count == 1 && Branches(view, "Values").All(b => b.Count == 0), "unticked again: the model, and no Values");
    }

    static void AllForces(Plate plate)
    {
        Section("Every beam force at once");
        var view = plate.View();
        Tick(view, "Beam forces", true, "Mz");
        plate.Solve(view);
        int one = Field<Mesh>(view, "_diagramsJoined")?.Faces.Count ?? 0;

        var row = Member<MenuCheckBoxGroup>(Result(view, "Beam forces"), "Components");
        for (int i = 0; i < row.Count; i++) row[i] = true;
        Call(view, "UpdateVisibility");
        plate.Solve(view);

        var errors = Messages(view, GH_RuntimeMessageLevel.Error).Concat(Messages(view, GH_RuntimeMessageLevel.Warning)).ToList();
        Check(errors.Count == 0, "no errors or warnings" + Said(errors));
        var info = Texts(view, "Info");
        Check(string.Join(",", info.Select(x => x.Split(' ')[3])) == "N,Vy,Vz,Torsion,My,Mz",
              "Info: a line per force, in the order of the row" + Environment.NewLine + "      " + string.Join(Environment.NewLine + "      ", info));
        Check(info.All(x => x.Contains("[kN]") || x.Contains("[kN·m]")) && info[0].Contains("[kN]") && info[5].Contains("[kN·m]"),
              "forces in kN, moments in kN·m");
        int six = Field<Mesh>(view, "_diagramsJoined")?.Faces.Count ?? 0;
        Check(six > 3 * one, $"a diagram for each: {six} diagram faces, against {one} for Mz alone");
        Check(Branches(view, "Values").All(b => b.Count == 0), "and no Values, as diagrams take the colours of their force");

        row.TickOnly(4);
        row[5] = true;
        plate.Solve(view);
        Check(Texts(view, "Info").Count == 2 && Field<System.Collections.IDictionary>(view, "_fieldCache").Count == 2,
              "unticked, a force lets go of what was read for it (My and Mz kept)");

        for (int i = 0; i < row.Count; i++) row[i] = false;
        plate.Solve(view);
        Check(Messages(view, GH_RuntimeMessageLevel.Remark).Any(m => m.Contains("none of its components ticked")),
              "none ticked: a remark to tick one" + Said(Messages(view, GH_RuntimeMessageLevel.Remark)));
    }

    static void Filtered(Plate plate)
    {
        Section("ElementId");
        var view = plate.View();
        Set(view, "ElementId", new GH_String("1"), new GH_String("2"));
        plate.Solve(view);

        var ghost = System.Drawing.Color.FromArgb(150, 150, 150).ToArgb();
        var meshes = Geometry(view).OfType<Mesh>().ToList();
        Check(meshes.Any(m => m.VertexColors.Cast<System.Drawing.Color>().All(c => c.ToArgb() == ghost)),
              "the elements left out are in Geometry, grey");
        Check(meshes.Any(m => m.VertexColors.Cast<System.Drawing.Color>().Any(c => c.ToArgb() != ghost)),
              "the two kept are in their colour");

        Field<MenuCheckBox>(view, "_ckElementIds").Active = true;
        var labels = (System.Collections.ICollection)Call(view, "ElementLabels");
        Check(labels.Count == 2, $"Element IDs are written on the two kept ({labels.Count})");
    }

    static void DisplayOnly(Plate plate)
    {
        Section("What only changes what is drawn over the model");
        var view = plate.View();
        plate.Solve(view);
        var box = view.ClippingBox;
        var solved = view.ProcessorTime;

        Field<MenuCheckBox>(view, "_ckShowSupports").Active = true;
        Field<MenuSlider>(view, "_slSupportScale").Value = 10.0;
        Call(view, "OnDisplayChanged", null, EventArgs.Empty);
        Check(view.Phase == GH_SolutionPhase.Computed, $"Show Supports leaves the solution as it is ({view.Phase})");
        Check(view.ClippingBox.Diagonal.Length > box.Diagonal.Length,
              $"and the extent grows round the supports ({box.Diagonal.Length:F2} to {view.ClippingBox.Diagonal.Length:F2})");

        Field<MenuCheckBox>(view, "_ckShowLoads").Active = true;
        Field<MenuSlider>(view, "_slLoadScale").Value = 0.5;
        Call(view, "OnDisplayChanged", null, EventArgs.Empty);
        var loads = Field<object>(view, "_loads");
        Check(loads != null && view.Phase == GH_SolutionPhase.Computed, "Show Loads too, its arrows made once for the scale");
        Field<MenuSlider>(view, "_slLoadScale").Value = 2.0;
        Call(view, "OnDisplayChanged", null, EventArgs.Empty);
        Check(!ReferenceEquals(loads, Field<object>(view, "_loads")), "and again at a new scale");
    }

    /// <summary>
    /// Every overlay drawn into a real viewport, by a conduit drawing the component as Grasshopper's
    /// preview does, so that a draw that throws is caught here rather than in front of someone.
    /// </summary>
    static void Drawn(Plate plate)
    {
        Section("Drawn into a viewport");
        var view = plate.View();
        Show(view, "Shell forces", "mxx");
        Tick(view, "Beam forces", true, "Vz");
        Tick(view, "Reactions", true, "Force");
        Tick(view, "Displacement", true);
        foreach (var name in new[] { "_ckValues", "_ckNodeIds", "_ckElementIds", "_ckSectionNames", "_ckLocalAxes", "_ckReleases",
                                     "_ckShowLoads", "_ckShowLoadValues", "_ckShowSupports", "_ckDeformed" })
            Field<MenuCheckBox>(view, name).Active = true;
        plate.Solve(view);

        var rhino = Rhino.RhinoDoc.ActiveDoc?.Views?.ActiveView;
        if (rhino == null)
        {
            Line("  [SKIP] this Rhino has no document open, so no viewport to draw into");
            return;
        }

        var conduit = new Drawer(plate.Document) { Enabled = true };
        try
        {
            conduit.Draw.Add(view);
            rhino.CaptureToBitmap(rhino.Size)?.Dispose();
            Check(conduit.Frames > 0 && conduit.Failure == null, "every overlay over four results at once, deformed, drawn without an error" +
                  (conduit.Failure == null ? "" : ": " + conduit.Failure));
        }
        finally
        {
            conduit.Enabled = false;
        }
    }

    /// <summary>
    /// With Deformed shape ticked, the loads ride on the deformed shape: the area load's arrows end
    /// where the plate's corners went, each moved as far as its node, and the edge beams' self-weight
    /// with them.
    /// </summary>
    static void LoadsDeformed(Plate plate)
    {
        Section("Loads on the deformed shape");
        var view = plate.View();
        Field<MenuCheckBox>(view, "_ckShowLoads").Active = true;
        plate.Solve(view);
        var before = Field<List<Tuple<Line, Color>>>(Call(view, "Loads"), "_arrows").Select(a => a.Item1).ToList();

        Field<MenuCheckBox>(view, "_ckDeformed").Active = true;
        Field<MenuSlider>(view, "_slDeformScale").Value = 50.0;
        plate.Solve(view);
        var after = Field<List<Tuple<Line, Color>>>(Call(view, "Loads"), "_arrows").Select(a => a.Item1).ToList();
        var moved = Field<Dictionary<int, Vector3d>>(view, "_drawnDisplacement");
        var model = Field<Alpaca4d.Model>(view, "_model");

        Check(moved != null && moved.Values.Any(v => v.Length > 1e-6), "the deformed shape's displacements reach the loads");
        Check(after.Count == before.Count, $"the same arrows, as many as on the shape as built ({after.Count} and {before.Count})");
        int onNodes = 0, wrong = 0;
        for (int i = 0; i < Math.Min(before.Count, after.Count); i++)
        {
            int tag = model.NodeTagNear(before[i].To, out _);
            if (tag == 0) continue;
            onNodes++;
            var expected = moved.TryGetValue(tag, out var by) ? by : Vector3d.Zero;
            if ((after[i].To - before[i].To - expected).Length > 1e-9 || Math.Abs(after[i].Length - before[i].Length) > 1e-9) wrong++;
        }
        Check(onNodes > 0 && wrong == 0, $"each arrow ending on a node moved as far as the node, as long as it was ({onNodes} on nodes, {wrong} not)");
        double far = after.Zip(before, (a, b) => a.To.DistanceTo(b.To)).Max();
        Check(far > 0.0, $"and the loads moved with the plate, up to {far:F3} m at deformation scale 50");
    }

    /// <summary>
    /// Every kind of load on the plate, each given element by element as a list wired into a load
    /// component makes it, drawn to fit the model: once a run of beams or group of shells, the
    /// largest arrow a share of the model's size, the area load's sheet on the side it comes from,
    /// the self-weight as the loads OpenSees is given, along the beams.
    /// </summary>
    static void LoadsDrawn(Plate plate)
    {
        Section("Loads drawn to fit the model");

        var line = plate.Add<Alpaca4d.Gh.LinearLoad>();
        In(line, "Beam|Id").AddSource(Out(plate.BeamElements, "Element"));
        Set(line, "Force", new GH_Vector(new Vector3d(0, 0, -10)));
        var point = plate.Add<Alpaca4d.Gh.PointLoad>();
        Set(point, "Point", new GH_Point(new Point3d(6, 5, 0)));
        Set(point, "Force", new GH_Vector(new Vector3d(0, 0, -50)));
        Set(point, "Moment", new GH_Vector(new Vector3d(0, 20, 0)));
        var gravity = plate.Add<Alpaca4d.Gh.GravityLoad>();
        var mass = plate.Add<Alpaca4d.Gh.MassPointLoad>();
        Set(mass, "Point", new GH_Point(new Point3d(0, 0, 0)));
        Set(mass, "TransMass", new GH_Vector(new Vector3d(500, 500, 500)));

        var pattern = plate.Add<Alpaca4d.Gh.PatternBase>("PlainPattern (Alpaca4d)");
        foreach (var load in new GH_Component[] { line, point, gravity })
            In(pattern, "Loads").AddSource(load.Params.Output[0]);

        var assemble = plate.Add<Alpaca4d.Gh.AssembleModel>();
        Wire(plate.ShellElements, "Element", assemble, "Elements");
        Wire(plate.BeamElements, "Element", assemble, "Elements");
        Wire(plate.SupportsMade, "Support", assemble, "Supports");
        foreach (var source in new[] { plate.AreaPattern, pattern, mass })
            In(assemble, "LoadPatterns").AddSource(source.Params.Output[0]);

        var view = plate.Add<Alpaca4d.Gh.ModelView>();
        Wire(assemble, "AlpacaModel", view, "AlpacaModel");
        Field<MenuCheckBox>(view, "_ckShowLoads").Active = true;
        Field<MenuCheckBox>(view, "_ckShowLoadValues").Active = true;
        plate.Solve();

        var loads = Call(view, "Loads");
        var values = ((System.Collections.IEnumerable)Field<object>(loads, "_values")).Cast<object>()
            .Select(v => (string)v.GetType().GetField("Item1").GetValue(v)).ToList();
        var arrows = Field<List<Tuple<Line, Color>>>(loads, "_arrows");
        int Count(string text) => values.Count(v => v == text);

        Check(Count("5 kN/m²") == 1, $"the area load given shell by shell is written once ({Count("5 kN/m²")} times, {plate.Shells} shells)");
        Check(Count("10 kN/m") == 2, $"the line load given beam by beam once a run of beams, two edges ({Count("10 kN/m")} times, {plate.Beams} beams)");
        Check(Count("50 kN") == 1 && Count("20 kN·m") == 1, "the point load's force and moment once each");
        Check(Count("500 kg") == 1, $"the mass ({Count("500 kg")} times)");
        Check(Field<System.Collections.ICollection>(loads, "_moments").Count == 1 && Field<System.Collections.ICollection>(loads, "_masses").Count == 1,
              "an arc for the moment, a dot for the mass");

        double size = Math.Sqrt(12 * 12 + 10 * 10);
        double largest = arrows.Max(a => a.Item1.Length);
        Check(Math.Abs(largest - 0.15 * size) < 1e-6, $"the largest arrow, the point load's, is 15% of the model's diagonal at Load scale 1 ({largest:F3} m of {size:F2} m)");
        double Longest(DisplayItem item) => arrows.Where(a => a.Item2.ToArgb() == AlpacaSettings.Colour(item).ToArgb()).Max(a => a.Item1.Length);
        Check(Math.Abs(Longest(DisplayItem.LineLoads) - 0.6 * largest) < 1e-6 && Math.Abs(Longest(DisplayItem.AreaLoads) - 0.4 * largest) < 1e-6,
              $"the line load's 60% of that and the area load's 40% ({Longest(DisplayItem.LineLoads):F3} and {Longest(DisplayItem.AreaLoads):F3} m)");

        var sheets = Field<Mesh>(loads, "_sheets").GetBoundingBox(true);
        Check(sheets.Min.Z > 0.0, $"the sheet of a load down on the plate stands over it, where the load comes from (from z = {sheets.Min.Z:F3})");

        var weight = AlpacaSettings.Colour(DisplayItem.Gravity).ToArgb();
        var model = Field<Alpaca4d.Model>(view, "_model");
        var applied = model.CreateGravityLoad(new Alpaca4d.Loads.Gravity(1.0, null));
        // The plate's shells weigh nothing (Rho 0): the weight is the edge beams', along them.
        var uniform = applied.OfType<Alpaca4d.Loads.LineLoad>().ToList();
        double perMetre = 0.3 * 0.5 * 7850 * 9.81 / 1000.0;
        Check(uniform.Count == plate.Beams && applied.OfType<Alpaca4d.Loads.PointLoad>().All(load => load.Force.Length == 0.0) && uniform.All(l => Math.Abs(l.GlobalForce.Z + perMetre) < 1e-9 && l.GlobalForce.X == 0.0 && l.GlobalForce.Y == 0.0),
              $"the self-weight goes to OpenSees as a uniform load down along each beam, {perMetre:F2} kN/m ({uniform.Count} line loads, and {applied.Count - uniform.Count} point loads of nothing on the shells' nodes)");
        // Twice a beam: the Linear Load of 10 kN/m on every beam, and the self-weight.
        int written = string.Join("", model.Tcl).Split('\n').Count(line => line.TrimStart().StartsWith("eleLoad") && line.Contains("-beamUniform"));
        Check(written == 2 * plate.Beams, $"and is written as eleLoad -beamUniform ({written} of them, {plate.Beams} beams)");

        var own = arrows.Where(a => a.Item2.ToArgb() == weight).ToList();
        var ownTails = Field<List<Tuple<Line, Color>>>(loads, "_tails").Where(t => t.Item2.ToArgb() == weight).ToList();
        int Written(string text) => ((System.Collections.IEnumerable)Field<object>(loads, "_values")).Cast<object>()
            .Count(v => ((Color)v.GetType().GetField("Item2").GetValue(v)).ToArgb() == weight && (string)v.GetType().GetField("Item1").GetValue(v) == text);
        string value = perMetre.ToString("G4", System.Globalization.CultureInfo.InvariantCulture) + " kN/m";
        Check(own.Count > 2 && own.All(a => a.Item1.Direction.Z < 0.0 && (Math.Abs(a.Item1.To.Y) < 1e-9 || Math.Abs(a.Item1.To.Y - 10.0) < 1e-9)) && ownTails.Count == plate.Beams && Written(value) == 1,
              $"drawn along the beams as a line load, in the Gravity colour, its value {value} written once for both edges ({own.Count} arrows, {ownTails.Count} tails, written {Written(value)} times)");
        Check(Math.Abs(own.Max(a => a.Item1.Length) - 0.5 * 0.6 * largest) < 1e-6, $"its arrows half as long as the line load's ({own.Max(a => a.Item1.Length):F3} m)");

        var along = AlpacaSettings.Colour(DisplayItem.LineLoads).ToArgb();
        var tips = arrows.Where(a => a.Item2.ToArgb() == along).Select(a => a.Item1.To).ToList();
        double closest = tips.SelectMany((p, i) => tips.Skip(i + 1).Select(q => p.DistanceTo(q))).Min();
        Check(tips.Count < 7 * plate.Beams && closest >= 0.6 * 0.05 * size - 1e-9,
              $"the line load's arrows spread over each run, {tips.Count} for {plate.Beams} beams, none closer than {closest:F2} m");

        Field<MenuSlider>(view, "_slLoadScale").Value = 2.0;
        Call(view, "OnDisplayChanged", null, EventArgs.Empty);
        double doubled = Field<List<Tuple<Line, Color>>>(Call(view, "Loads"), "_arrows").Max(a => a.Item1.Length);
        Check(Math.Abs(doubled - 2.0 * largest) < 1e-6, $"Load scale 2 makes it twice as long ({doubled:F3} m)");

        var rhino = Rhino.RhinoDoc.ActiveDoc?.Views?.ActiveView;
        if (rhino == null)
        {
            Line("  [SKIP] this Rhino has no document open, so no viewport to draw into");
            return;
        }

        var conduit = new Drawer(plate.Document) { Enabled = true };
        try
        {
            conduit.Draw.Add(view);
            rhino.CaptureToBitmap(rhino.Size)?.Dispose();
            Check(conduit.Frames > 0 && conduit.Failure == null, "and all of it drawn without an error" +
                  (conduit.Failure == null ? "" : ": " + conduit.Failure));
        }
        finally
        {
            conduit.Enabled = false;
        }
    }

    /// <summary>
    /// LoadPattern picks the loads drawn by a pattern's Name, whatever its case, by its Id, or by
    /// the Load Pattern wired in; a name no pattern has draws nothing and says which there are.
    /// </summary>
    static void LoadsByPattern(Plate plate)
    {
        Section("Loads of one pattern, by name or Id");

        var point = plate.Add<Alpaca4d.Gh.PointLoad>();
        Set(point, "Point", new GH_Point(new Point3d(6, 5, 0)));
        Set(point, "Force", new GH_Vector(new Vector3d(0, 0, -50)));
        var dead = plate.Add<Alpaca4d.Gh.PatternBase>("PlainPattern (Alpaca4d)");
        In(dead, "Loads").AddSource(point.Params.Output[0]);
        Set(dead, "Name", new GH_String("Dead"));

        var line = plate.Add<Alpaca4d.Gh.LinearLoad>();
        In(line, "Beam|Id").AddSource(Out(plate.BeamElements, "Element"));
        Set(line, "Force", new GH_Vector(new Vector3d(0, 0, -10)));
        var wind = plate.Add<Alpaca4d.Gh.PatternBase>("PlainPattern (Alpaca4d)");
        In(wind, "Loads").AddSource(line.Params.Output[0]);
        Set(wind, "Name", new GH_String("Wind"));

        var assemble = plate.Add<Alpaca4d.Gh.AssembleModel>();
        Wire(plate.ShellElements, "Element", assemble, "Elements");
        Wire(plate.BeamElements, "Element", assemble, "Elements");
        Wire(plate.SupportsMade, "Support", assemble, "Supports");
        foreach (var source in new[] { dead, wind })
            In(assemble, "LoadPatterns").AddSource(source.Params.Output[0]);

        var view = plate.Add<Alpaca4d.Gh.ModelView>();
        Wire(assemble, "AlpacaModel", view, "AlpacaModel");
        Field<MenuCheckBox>(view, "_ckShowLoads").Active = true;
        Field<MenuCheckBox>(view, "_ckShowLoadValues").Active = true;

        string Drawn()
        {
            plate.Solve();
            var values = ((System.Collections.IEnumerable)Field<object>(Call(view, "Loads"), "_values")).Cast<object>()
                .Select(v => (string)v.GetType().GetField("Item1").GetValue(v)).Distinct().OrderBy(v => v);
            return string.Join(" + ", values);
        }

        Check(Drawn() == "10 kN/m + 50 kN", $"with LoadPattern empty, both patterns' loads ({Drawn()})");

        Set<IGH_Goo>(view, "LoadPattern", new GH_String(" dead "));
        Check(Drawn() == "50 kN", $"\" dead \" draws the Dead pattern alone, whatever the case and the spaces ({Drawn()})");

        int windId = Field<Alpaca4d.Model>(view, "_model").LoadPatterns.Single(p => p.Name == "Wind").Id.Value;
        Set<IGH_Goo>(view, "LoadPattern", new GH_Integer(windId));
        Check(Drawn() == "10 kN/m", $"Wind's Id, {windId}, as a number draws Wind alone ({Drawn()})");
        Set<IGH_Goo>(view, "LoadPattern", new GH_String(windId.ToString()));
        Check(Drawn() == "10 kN/m", $"and as text too ({Drawn()})");

        ((GH_PersistentParam<IGH_Goo>)In(view, "LoadPattern")).PersistentData.Clear();
        In(view, "LoadPattern").AddSource(dead.Params.Output[0]);
        In(view, "LoadPattern").ExpireSolution(false);
        Check(Drawn() == "50 kN", $"the Dead Load Pattern wired in draws Dead alone ({Drawn()})");
        In(view, "LoadPattern").RemoveAllSources();

        Set<IGH_Goo>(view, "LoadPattern", new GH_String("Snow"));
        var drawn = Drawn();
        var warnings = Messages(view, GH_RuntimeMessageLevel.Warning);
        Check(drawn == "" && warnings.Any(m => m.Contains("\"Snow\"") && m.Contains("\"Dead\"") && m.Contains("\"Wind\"")),
              "a name no pattern has draws no loads, with a warning naming the patterns there are" + Said(warnings));
    }

    static void UpgradeModelView(Plate plate)
    {
        Section("The old Model View, upgraded");
        var old = plate.Add<Alpaca4d.Gh.ModelView_obsolete>();
        Wire(plate.Assemble, "AlpacaModel", old, "AlpacaModel");

        Field<MenuDropDown>(old, "_ddColourBy").Value = 1;
        Field<MenuCheckBox>(old, "_ckNodeIds").Active = true;
        Field<MenuCheckBox>(old, "_ckExtruded").Active = false;
        Field<MenuCheckBox>(old, "_ckShowLoads").Active = true;
        Field<MenuCheckBox>(old, "_ckShowSupports").Active = true;
        Field<MenuSlider>(old, "_slSupportScale").Value = 3.0;
        Field<MenuSlider>(old, "_slTextSize").Value = 1.25;
        ((GH_PersistentParam<GH_Integer>)old.Params.Input[1]).PersistentData.Append(new GH_Integer(2));
        var ids = (GH_PersistentParam<GH_Integer>)old.Params.Input[2];
        ids.PersistentData.Append(new GH_Integer(4));
        ids.PersistentData.Append(new GH_Integer(17));

        var after = new Param_GenericObject();
        after.CreateAttributes();
        plate.Document.AddObject(after, false);
        after.AddSource(old.Params.Output[0]);

        var upgraded = new Alpaca4d.Gh.ModelViewUpgrader().Upgrade(old, plate.Document) as Alpaca4d.Gh.ModelView;
        Check(upgraded != null, "it is swapped for the new Model View");
        if (upgraded == null) return;

        Check(plate.Document.Objects.Contains(upgraded) && !plate.Document.Objects.Contains(old), "in the document, in place of the old one");
        Check(Field<Array>(upgraded, "_results").Cast<object>().All(r => !Member<MenuCheckBox>(r, "Tick").Active), "showing the model, no result ticked");
        Check(Field<MenuDropDown>(upgraded, "_ddColourBy").Value == 1, "coloured as it was");
        Check(Field<MenuCheckBox>(upgraded, "_ckNodeIds").Active && !Field<MenuCheckBox>(upgraded, "_ckExtruded").Active
              && Field<MenuCheckBox>(upgraded, "_ckShowLoads").Active && Field<MenuCheckBox>(upgraded, "_ckShowSupports").Active,
              "with its checkboxes");
        Check(Field<MenuSlider>(upgraded, "_slSupportScale").Value == 3.0 && Field<MenuSlider>(upgraded, "_slTextSize").Value == 1.25,
              "and its sliders");
        Check(In(upgraded, "AlpacaModel").Sources.Any(s => s.Attributes.GetTopLevel.DocObject == plate.Assemble), "AlpacaModel keeps its wire");
        var lp = ((GH_PersistentParam<IGH_Goo>)In(upgraded, "LoadPattern")).PersistentData.AllData(true).Select(x => x.ScriptVariable());
        Check(string.Join(",", lp) == "2", "LP's value goes to LoadPattern");
        var tags = ((GH_PersistentParam<GH_String>)In(upgraded, "ElementId")).PersistentData.AllData(true).Select(x => ((GH_String)x).Value);
        Check(string.Join(",", tags) == "4,17", $"ElemIds' tags go to ElementId as text ({string.Join(",", tags)})");
        Check(after.Sources.Contains(Out(upgraded, "AlpacaModel")), "what took the model from it takes it from the new one");

        plate.Solve(upgraded);
        var errors = Messages(upgraded, GH_RuntimeMessageLevel.Error).Concat(Messages(upgraded, GH_RuntimeMessageLevel.Warning)).ToList();
        Check(errors.Count == 0 && Text(upgraded, "Info") == "Model - coloured by Section", $"and it draws the model by section ({Text(upgraded, "Info")})" + Said(errors));
    }

    static void UpgradeViewResults(Plate plate)
    {
        Section("View Results_WIP, upgraded");
        var old = plate.Add<Alpaca4d.Gh.ViewResults_obsolete>();
        Wire(plate.Analysis, "AlpacaModel", old, "AlpacaModel");
        Show(old, "Shell stresses", "VonMises");
        Field<MenuDropDown>(old, "_ddLayer").Value = 2;
        Field<MenuDropDown[]>(old, "_ddUnits")[3].Value = 0;
        Field<MenuCheckBox>(old, "_ckDeformed").Active = true;
        var scale = Field<MenuSlider>(old, "_slDeformScale");
        scale.MaxValue = 40.0;
        scale.Value = 12.5;
        Field<MenuCheckBox>(old, "_ckBands").Active = true;
        Field<MenuCheckBox>(old, "_ckValues").Active = true;
        Set(old, "Range", new GH_Interval(new Interval(0.0, 2000.0)));

        var info = new Param_String();
        info.CreateAttributes();
        plate.Document.AddObject(info, false);
        info.AddSource(Out(old, "Info"));
        var values = new Param_Number();
        values.CreateAttributes();
        plate.Document.AddObject(values, false);
        values.AddSource(Out(old, "Values"));

        plate.Solve(old);
        string oldInfo = Text(old, "Info");
        var oldValues = Numbers(old, "Values");

        var upgraded = new Alpaca4d.Gh.ViewResultsUpgrader().Upgrade(old, plate.Document) as Alpaca4d.Gh.ModelView;
        Check(upgraded != null, "it is swapped for Model View");
        if (upgraded == null) return;

        Check(Ticked(upgraded, "Shell stresses") && Field<Array>(upgraded, "_results").Cast<object>().Count(r => Member<MenuCheckBox>(r, "Tick").Active) == 1,
              "the result it showed ticked, alone");
        Check(Field<MenuDropDown>(upgraded, "_ddLayer").Value == 2 && Member<MenuDropDown[]>(Result(upgraded, "Shell stresses"), "Units")[3].Value == 0,
              "at the layer and in the unit it had");
        var newScale = Field<MenuSlider>(upgraded, "_slDeformScale");
        Check(Field<MenuCheckBox>(upgraded, "_ckDeformed").Active && newScale.MaxValue == 40.0 && newScale.Value == 12.5,
              $"deformed at its scale, over its range ({newScale.MinValue} to {newScale.MaxValue} at {newScale.Value})");
        Check(Field<MenuCheckBox>(upgraded, "_ckBands").Active && Field<MenuCheckBox>(upgraded, "_ckValues").Active
              && !Field<MenuCheckBox>(upgraded, "_ckExtruded").Active && !Field<MenuCheckBox>(upgraded, "_ckReleases").Active,
              "iso-surfaces and values on, Extruded and End Releases off, as it was");
        Check(info.Sources.Contains(Out(upgraded, "Info")) && values.Sources.Contains(Out(upgraded, "Values")), "Info and Values keep their wires");

        plate.Solve(upgraded);
        Check(Text(upgraded, "Info") == oldInfo, $"it says what the old one said ({Text(upgraded, "Info")})");
        Check(string.Join(",", Numbers(upgraded, "Values").Select(x => x.ToString("G6"))) == string.Join(",", oldValues.Select(x => x.ToString("G6"))),
              $"and gives the same Values ({oldValues.Count})");
    }

    // ---------------------------------------------------------------- timings

    /// <summary>Draws components into each frame as Grasshopper's preview would, timing them.</summary>
    sealed class Drawer : DisplayConduit
    {
        public readonly List<GH_Component> Draw = new List<GH_Component>();
        public int Frames;
        public double Ms;
        public string Failure;
        readonly GH_Document document;

        public Drawer(GH_Document document) { this.document = document; }

        protected override void PostDrawObjects(DrawEventArgs e)
        {
            if (Draw.Count == 0) return;
            Frames++;
            var ctor = typeof(GH_PreviewArgs).GetConstructors(Any).First(c => c.GetParameters().Length == 9);
            var material = new DisplayMaterial(System.Drawing.Color.Red);
            var args = (IGH_PreviewArgs)ctor.Invoke(new object[] { document, e.Display, e.Viewport, 1, System.Drawing.Color.Black,
                System.Drawing.Color.Green, material, material, MeshingParameters.Default });
            var sw = Stopwatch.StartNew();
            try
            {
                foreach (var c in Draw)
                {
                    c.DrawViewportMeshes(args);
                    c.DrawViewportWires(args);
                }
            }
            catch (Exception ex)
            {
                Failure = Failure ?? ex.ToString();
            }
            Ms += sw.Elapsed.TotalMilliseconds;
        }
    }

    static double Median(Func<double> run, int n)
    {
        var runs = Enumerable.Range(0, n).Select(_ => run()).OrderBy(x => x).ToList();
        return runs[runs.Count / 2];
    }

    /// <summary>A change on the canvas: the component expired and the document solved as the canvas solves it.</summary>
    static double Canvas(Plate plate, GH_Component c)
    {
        var sw = Stopwatch.StartNew();
        c.ExpireSolution(false);
        plate.Document.NewSolution(false, GH_SolutionMode.Default);
        return sw.Elapsed.TotalMilliseconds;
    }

    static double PerFrame(Drawer conduit, GH_Component c, int frames)
    {
        conduit.Draw.Clear();
        conduit.Draw.Add(c);
        var rhino = Rhino.RhinoDoc.ActiveDoc.Views.ActiveView;
        rhino.CaptureToBitmap(rhino.Size)?.Dispose();
        conduit.Frames = 0;
        conduit.Ms = 0;
        for (int i = 0; i < frames; i++) rhino.CaptureToBitmap(rhino.Size)?.Dispose();
        conduit.Draw.Clear();
        return conduit.Ms / Math.Max(1, conduit.Frames);
    }

    static void Timings(Plate plate)
    {
        Section($"Timings, old against new, on a plate of {plate.Shells} shells and {plate.Beams} beams (medians, ms)");

        Alpaca4d.License.License.ValidateFeature(null);
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 50; i++) Alpaca4d.License.License.ValidateFeature(null);
        Line($"  licence check, each solve: {sw.Elapsed.TotalMilliseconds / 50:F2} (was 15.9 before the network addresses were kept)");

        var oldResults = plate.Add<Alpaca4d.Gh.ViewResults_obsolete>();
        Wire(plate.Analysis, "AlpacaModel", oldResults, "AlpacaModel");
        var oldModel = plate.Add<Alpaca4d.Gh.ModelView_obsolete>();
        Wire(plate.Analysis, "AlpacaModel", oldModel, "AlpacaModel");
        var view = plate.View();
        Show(view, "Displacement");
        // Beams as lines in both, as View Results drew them by default.
        Field<MenuCheckBox>(view, "_ckExtruded").Active = false;
        plate.Solve();

        double before = Median(() => Canvas(plate, oldResults), 9);
        double after = Median(() => Canvas(plate, view), 9);
        Line($"  a result solved again with nothing new to read:   View Results {before,7:F1}   Model View {after,7:F1}");

        double toggle = Median(() =>
        {
            var watch = Stopwatch.StartNew();
            var wires = Field<MenuCheckBox>(view, "_ckWires");
            wires.Active = !wires.Active;
            Call(view, "OnDisplayChanged", null, EventArgs.Empty);
            return watch.Elapsed.TotalMilliseconds;
        }, 9);
        Line($"  ticking Element edges:                            View Results {before,7:F1}   Model View {toggle,7:F1} (a redraw, no solution)");
        Check(toggle < before, "a display change costs less than the solution it used to run");
        double visibility = Median(() => { var w = Stopwatch.StartNew(); Call(view, "UpdateVisibility"); return w.Elapsed.TotalMilliseconds; }, 9);
        double extent = Median(() => { var w = Stopwatch.StartNew(); Call(view, "Extent"); return w.Elapsed.TotalMilliseconds; }, 9);
        Line($"    of which the menu laid out again and the canvas drawn {visibility:F1}, the extent {extent:F1}");
        Field<MenuCheckBox>(view, "_ckWires").Active = true;

        Show(view, null);
        plate.Solve(view);
        if (Rhino.RhinoDoc.ActiveDoc?.Views?.ActiveView == null)
        {
            Line("  [SKIP] each frame drawn: this Rhino has no document open, so no viewport to draw into");
            return;
        }

        var conduit = new Drawer(plate.Document) { Enabled = true };
        try
        {
            foreach (var overlay in new[] { "_ckElementIds", "_ckLocalAxes", "_ckShowLoads" })
            {
                Field<MenuCheckBox>(oldModel, overlay).Active = true;
                Field<MenuCheckBox>(view, overlay).Active = true;
                Call(view, "OnDisplayChanged", null, EventArgs.Empty);
                double was = PerFrame(conduit, oldModel, 5);
                double now = PerFrame(conduit, view, 5);
                Line($"  each frame drawn with {overlay.Substring(3),-11}:         old Model View {was,5:F1}   Model View {now,5:F1}");
                Check(now < was, $"{overlay.Substring(3)} drawn faster each frame");
                Field<MenuCheckBox>(oldModel, overlay).Active = false;
                Field<MenuCheckBox>(view, overlay).Active = false;
            }
            Check(conduit.Failure == null, "drawn without an error" + (conduit.Failure == null ? "" : ": " + conduit.Failure));
        }
        finally
        {
            conduit.Enabled = false;
            Rhino.RhinoDoc.ActiveDoc.Views.Redraw();
        }
    }
}
