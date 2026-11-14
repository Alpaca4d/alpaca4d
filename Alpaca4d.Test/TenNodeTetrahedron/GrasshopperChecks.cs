// The ten node tetrahedron through the Grasshopper components themselves, in a running Rhino 8.
//
// A cantilever 8 x 1 x 1 of 48 tetrahedra, each handed to Ten Node Tetrahedron as the four vertex
// mesh Rhino gives - some of them wound inside out on purpose - with the nD Elastic Isotropic
// material, Support, Gravity Load, Assemble Model, Run Analysis and Natural Vibration, read back
// through Nodal Displacements, Reaction Forces, Brick Stresses and View Results' field. Its own
// weight is what loads it, so the shares of the weight on the edge nodes are under test too.
// grasshopper.sh loads this build of the plugin beside the installed one and calls Run by reflection.
//
// Run points Application.UserFolder at the test folder before anything reads it, so the solver
// path it sets is written there and not over the user's settings. The folder has no license,
// which keeps the model at 48 elements, under the free limit, and View Results' drawing out.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
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

    // Concrete-like, nu = 0 so that beam theory has nothing to say about Poisson: kN, m, kg/m3.
    const double E = 3.0e7, Rho = 2500.0, L = 8.0;

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

    public static string Run(string work, string openSees, string oldOpenSees)
    {
        folder = work;
        report = new StringBuilder();
        fails = 0;
        try
        {
            Alpaca4d.Application.UserFolder = work;
            Alpaca4d.AlpacaSettings.OpenSeesPath = openSees;

            OneElement();
            Cantilever();
            CornersOnly();
            if (oldOpenSees != null)
                Refused(oldOpenSees, openSees);
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
            Document.FilePath = Path.Combine(folder, name + ".gh");
        }

        public T Add<T>(string unit = null) where T : GH_Component, new()
        {
            var component = new T();
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

    static List<T> All<T>(IGH_Param p) => p.VolatileData.AllData(true).Select(g => (T)g.ScriptVariable()).ToList();

    static void NoErrors(Doc doc, string what)
    {
        var errors = doc.Messages(GH_RuntimeMessageLevel.Error).ToList();
        Check(errors.Count == 0, $"{what}: no errors{(errors.Count == 0 ? "" : " - " + string.Join(" | ", errors))}");
    }

    // ---------------------------------------------------------------- the model

    /// <summary>
    /// The cells of the cantilever cut into six tetrahedra each, as four vertex meshes - the faces
    /// of every other one wound inside out, and its vertices in no particular order, as a mesher
    /// or a user might hand them over.
    /// </summary>
    static List<Mesh> Tetrahedra()
    {
        var cube = new[] { (0, 0, 0), (1, 0, 0), (1, 1, 0), (0, 1, 0), (0, 0, 1), (1, 0, 1), (1, 1, 1), (0, 1, 1) };
        var six = new[] { new[] { 0, 1, 2, 6 }, new[] { 0, 2, 3, 6 }, new[] { 0, 3, 7, 6 }, new[] { 0, 7, 4, 6 }, new[] { 0, 4, 5, 6 }, new[] { 0, 5, 1, 6 } };

        var meshes = new List<Mesh>();
        for (int i = 0; i < (int)L; i++)
            foreach (var t in six)
            {
                var mesh = new Mesh();
                var order = meshes.Count % 2 == 0 ? t : new[] { t[2], t[0], t[3], t[1] };
                foreach (var q in order)
                    mesh.Vertices.Add(i + cube[q].Item1, cube[q].Item2, cube[q].Item3);
                mesh.Faces.AddFace(0, 1, 2);
                mesh.Faces.AddFace(0, 1, 3);
                mesh.Faces.AddFace(1, 2, 3);
                mesh.Faces.AddFace(0, 2, 3);
                meshes.Add(mesh);
            }
        return meshes;
    }

    static GH_Component Material(Doc doc)
    {
        var material = doc.Add<SimplexGh.nD>("ElasticIsotropic (Alpaca4d)");
        Set(material, "E", new GH_Number(E));
        Set(material, "G", new GH_Number(E / 2.0));
        Set(material, "ν", new GH_Number(0.0));
        Set(material, "Rho", new GH_Number(Rho));
        return material;
    }

    static GH_Component Elements(Doc doc, GH_Component material, IEnumerable<Mesh> meshes)
    {
        var tetrahedron = doc.Add<Alpaca4d.Gh.TenNodeTetrahedron>();
        Set(tetrahedron, "Mesh", meshes.Select(m => new GH_Mesh(m)).ToArray());
        Wire(material, "Material", tetrahedron, "Material");
        return tetrahedron;
    }

    static GH_Component Supports(Doc doc, IEnumerable<Point3d> points)
    {
        var support = doc.Add<Alpaca4d.Gh.Support>();
        Set(support, "Position", points.Select(p => new GH_Plane(new Plane(p, Vector3d.XAxis, Vector3d.YAxis))).ToArray());
        return support;
    }

    static (GH_Component assemble, GH_Component run) Analyse(Doc doc, GH_Component elements, GH_Component supports)
    {
        var gravity = doc.Add<Alpaca4d.Gh.GravityLoad>();
        var series = doc.Add<Alpaca4d.Gh.LinearTimeSeries>();
        var pattern = doc.Add<Alpaca4d.Gh.PatternBase>("PlainPattern (Alpaca4d)");
        Wire(series, "TimeSeries", pattern, "TimeSeries");
        In(pattern, "Loads").AddSource(gravity.Params.Output[0]);

        var assemble = doc.Add<Alpaca4d.Gh.AssembleModel>();
        Wire(elements, "Element", assemble, "Elements");
        Wire(supports, "Support", assemble, "Supports");
        Wire(pattern, pattern.Params.Output[0].Name, assemble, "LoadPatterns");

        var run = doc.Add<Alpaca4d.Gh.RunAnalysis>();
        Wire(assemble, "AlpacaModel", run, "AlpacaModel");
        var settings = doc.Add<Alpaca4d.Gh.AnalysisSettings>();
        Wire(settings, "Settings", run, "Settings");
        return (assemble, run);
    }

    static GH_Component Reader<T>(Doc doc, GH_Component from) where T : GH_Component, new()
    {
        var reader = doc.Add<T>();
        Wire(from, "AlpacaModel", reader, "AlpacaModel");
        return reader;
    }

    /// <summary>The points at x = 0: every corner there and the middle of every edge between two of them.</summary>
    static List<Point3d> FixedEnd(bool withEdgeNodes)
    {
        var points = new List<Point3d> { new Point3d(0, 0, 0), new Point3d(0, 1, 0), new Point3d(0, 0, 1), new Point3d(0, 1, 1) };
        if (withEdgeNodes)
            points.AddRange(new[] { new Point3d(0, 0.5, 0), new Point3d(0, 1, 0.5), new Point3d(0, 0.5, 1), new Point3d(0, 0, 0.5), new Point3d(0, 0.5, 0.5) });
        return points;
    }

    // ---------------------------------------------------------------- the checks

    static void OneElement()
    {
        Section("One tetrahedron, from a four vertex mesh wound inside out");

        var doc = new Doc("one");
        var mesh = new Mesh();
        foreach (var p in new[] { new Point3d(0, 0, 0), new Point3d(0, 1, 0), new Point3d(1, 0, 0), new Point3d(0, 0, 1) })
            mesh.Vertices.Add(p);
        mesh.Faces.AddFace(0, 1, 2);
        mesh.Faces.AddFace(0, 1, 3);
        mesh.Faces.AddFace(1, 2, 3);
        mesh.Faces.AddFace(0, 2, 3);
        var tetrahedron = Elements(doc, Material(doc), new[] { mesh });
        doc.Solve();
        NoErrors(doc, "Ten Node Tetrahedron");

        var points = All<Point3d>(Out(tetrahedron, "Points"));
        Check(points.Count == 10, $"Points has ten nodes ({points.Count})");
        var corners = points.Take(4).ToList();
        double jacobian = (corners[1] - corners[0]) * Vector3d.CrossProduct(corners[2] - corners[0], corners[3] - corners[0]);
        Check(jacobian > 0, $"its corners come back the right way out (Jacobian {jacobian:G4})");
        var edges = new[] { (0, 1), (1, 2), (2, 0), (0, 3), (1, 3), (2, 3) };
        Check(edges.Select((e, k) => points[4 + k].DistanceTo(0.5 * (corners[e.Item1] + corners[e.Item2]))).All(d => d < 1e-12),
              "then the middles of edges 1-2, 2-3, 3-1, 1-4, 2-4, 3-4");

        var element = Out(tetrahedron, "Element").VolatileData.AllData(true).Single().ScriptVariable() as Alpaca4d.Element.TenNodeTetrahedron;
        Check(element != null && element.Mesh.Faces.Count == 16 && element.Mesh.IsClosed, "the element's mesh is closed, sixteen triangles");
        Check(element != null && Math.Abs(Alpaca4d.Utils.MeshVolume(element.Mesh) - 1.0 / 6.0) < 1e-12,
              $"and encloses the tetrahedron's volume, outward ({(element == null ? double.NaN : Alpaca4d.Utils.MeshVolume(element.Mesh)):G6})");
    }

    static void Cantilever()
    {
        Section("A cantilever 8 x 1 x 1 of 48 ten node tetrahedra under its own weight");

        var doc = new Doc("cantilever");
        var elements = Elements(doc, Material(doc), Tetrahedra());
        var (assemble, run) = Analyse(doc, elements, Supports(doc, FixedEnd(withEdgeNodes: true)));
        var displacements = Reader<Alpaca4d.Gh.NodalDisplacement>(doc, run);
        var reactions = Reader<Alpaca4d.Gh.ReactionForce>(doc, run);
        var stresses = Reader<Alpaca4d.Gh.BrickStress>(doc, run);
        var vibration = doc.Add<Alpaca4d.Gh.NaturalVibration>("NaturalVibration");
        Wire(assemble, "AlpacaModel", vibration, "AlpacaModel");
        doc.Solve();

        NoErrors(doc, "every component");
        var warnings = doc.Messages(GH_RuntimeMessageLevel.Warning).ToList();
        Check(warnings.Count == 0, $"no warnings{(warnings.Count == 0 ? "" : " - " + string.Join(" | ", warnings))}");

        // Beam theory with shear: q L^4 / 8 E I + q L^2 / 2 (5/6) G A, q the weight per metre in kN.
        double q = Rho * 9.81 / 1000.0, inertia = 1.0 / 12.0;
        double tip = q * Math.Pow(L, 4) / (8 * E * inertia) + q * L * L / (2 * (5.0 / 6.0) * (E / 2.0));

        var positions = All<Point3d>(Out(displacements, "Position"));
        var moved = All<Vector3d>(Out(displacements, "Displacement"));
        var atTip = positions.Select((p, i) => (p, i)).Where(x => Math.Abs(x.p.X - L) < 1e-9).Select(x => moved[x.i].Z).ToList();
        Check(atTip.Count == 9, $"nine nodes at the free end: four corners, five edge nodes ({atTip.Count})");
        double got = -atTip.Average();
        Check(Near(got, tip, 0.01), $"tip deflection {got:G6} against beam theory's {tip:G6} ({(got - tip) / tip:P2})");

        var forces = All<Vector3d>(Out(reactions, "ReactionForce"));
        double weight = q * L;
        Check(forces.Count == 9, $"nine supported nodes ({forces.Count})");
        Check(Near(forces.Sum(f => f.Z), weight, 1e-9), $"the reactions add up to the weight: {forces.Sum(f => f.Z):G10} for {weight:G10}");

        var sigma11 = All<double>(Out(stresses, "Sigma11"));
        var solids = All<object>(Out(stresses, "Element")).OfType<Alpaca4d.Generic.IBrick>().ToList();
        Check(sigma11.Count == 48 && solids.All(s => s.ElementClass == Alpaca4d.Element.ElementClass.TenNodeTetrahedron),
              $"Brick Stresses reads 48 ten node tetrahedra ({sigma11.Count})");
        var rootTop = solids.Select((s, i) => (s, i)).Where(x => x.s.LocalPlane.Origin.X < 1 && x.s.LocalPlane.Origin.Z > 0.5).Select(x => sigma11[x.i]).ToList();
        var rootBottom = solids.Select((s, i) => (s, i)).Where(x => x.s.LocalPlane.Origin.X < 1 && x.s.LocalPlane.Origin.Z < 0.5).Select(x => sigma11[x.i]).ToList();
        Check(rootTop.Count > 0 && rootTop.All(s => s > 0) && rootBottom.All(s => s < 0),
              $"at the root the top is in tension and the bottom in compression ({rootTop.Average():G4} / {rootBottom.Average():G4})");

        // View Results' field: ten values per element, the stress at each node.
        var model = (Alpaca4d.Model)Out(run, "AlpacaModel").VolatileData.AllData(true).Single().ScriptVariable();
        var assembly = typeof(Alpaca4d.Gh.RunAnalysis).Assembly;
        var familyType = assembly.GetType("Alpaca4d.Gh.ResultFamily");
        var read = assembly.GetType("Alpaca4d.Gh.ResultField").GetMethod("Read", BindingFlags.Public | BindingFlags.Static);
        var field = read.Invoke(null, new object[] { model, Enum.Parse(familyType, "BrickStress"), 0, 0, 0, false });
        var byElement = (Dictionary<int, List<double>>)field.GetType().GetField("ByElement").GetValue(field);
        Check(byElement.Count == 48 && byElement.Values.All(v => v.Count == 10), "View Results colours each element by the stress at its ten nodes");
        var first = solids[0];
        double mean = byElement[first.Id.Value].Take(4).Average();
        Check(Near(mean, sigma11[0], 1e-9), $"whose corners average to Brick Stresses' value ({mean:G6} / {sigma11[0]:G6})");

        // The first mode of a cantilever, with the frequency beam theory gives without shear or
        // rotary inertia, which a beam this stocky (L/h = 8) sits a little below.
        var frequencies = All<double>(Out(vibration, "Frequencies"));
        double euler = 1.8751 * 1.8751 / (2 * Math.PI * L * L) * Math.Sqrt(E * inertia / (Rho / 1000.0));
        Check(frequencies.Count == 1 && frequencies[0] < euler && frequencies[0] > 0.95 * euler,
              $"Natural Vibration's first mode {(frequencies.Count > 0 ? frequencies[0] : double.NaN):G5} Hz, just under Euler-Bernoulli's {euler:G5} Hz");

        Section("The deck read back");

        var assembled = (Alpaca4d.Model)Out(assemble, "AlpacaModel").VolatileData.AllData(true).Single().ScriptVariable();
        ReadBack(assembled, assembled.Tcl, "as written, TenNodeTetrahedronStd");

        // The same deck in plain TenNodeTetrahedron's order, nodes 9 and 10 swapped: what a deck
        // from gmsh or by hand would hold.
        var gmsh = assembled.Tcl.Select(chunk => string.Join("\n", chunk.Split('\n').Select(line =>
        {
            var t = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (t.Length < 14 || t[0] != "element" || t[1] != "TenNodeTetrahedronStd") return line;
            t[1] = "TenNodeTetrahedron";
            (t[11], t[12]) = (t[12], t[11]);
            return string.Join(" ", t);
        }))).ToList();
        ReadBack(assembled, gmsh, "in plain TenNodeTetrahedron's order");
    }

    static void ReadBack(Alpaca4d.Model written, List<string> deck, string what)
    {
        var reader = Alpaca4d.TclReader.ReadText(deck);
        var read = reader.Model.Bricks;
        Check(reader.Warnings.Count == 0 && read.Count == written.Bricks.Count && read.All(b => b is Alpaca4d.Element.TenNodeTetrahedron),
              $"{what}: {read.Count} ten node tetrahedra{(reader.Warnings.Count == 0 ? "" : " - " + string.Join(" | ", reader.Warnings))}");

        double worst = 0.0;
        for (int i = 0; i < Math.Min(read.Count, written.Bricks.Count); i++)
            for (int v = 0; v < 10; v++)
                worst = Math.Max(worst, ((Point3d)read[i].Mesh.Vertices[v]).DistanceTo(written.Bricks[i].Mesh.Vertices[v]));
        Check(worst < 1e-9, $"{what}: every node where it was written ({worst:G3})");
    }

    static void CornersOnly()
    {
        Section("The same cantilever held at its mesh vertices only");

        var doc = new Doc("corners");
        var elements = Elements(doc, Material(doc), Tetrahedra());
        var (assemble, _) = Analyse(doc, elements, Supports(doc, FixedEnd(withEdgeNodes: false)));
        doc.Solve();

        var said = assemble.RuntimeMessages(GH_RuntimeMessageLevel.Warning).ToList();
        Check(said.Any(w => w.StartsWith("5 edge nodes of ten node tetrahedra")),
              $"Assemble Model says the five edge nodes between them are free: {said.FirstOrDefault()?.Split('.')[0]}");
    }

    static void Refused(string oldOpenSees, string openSees)
    {
        Section("Run on an OpenSees without the fixes");

        Alpaca4d.AlpacaSettings.OpenSeesPath = oldOpenSees;
        try
        {
            var doc = new Doc("refused");
            var elements = Elements(doc, Material(doc), Tetrahedra().Take(6));
            var (_, run) = Analyse(doc, elements, Supports(doc, FixedEnd(withEdgeNodes: true)));
            doc.Solve();

            var errors = run.RuntimeMessages(GH_RuntimeMessageLevel.Error).ToList();
            Check(errors.Any(e => e.Contains("six times too soft")), $"Run Analysis stops and says why: {errors.FirstOrDefault()?.Split('.')[0]}");
            Check(!Out(run, "AlpacaModel").VolatileData.AllData(true).Any(), "and hands on no model");
        }
        finally
        {
            Alpaca4d.AlpacaSettings.OpenSeesPath = openSees;
        }
    }
}
