// Topology Optimisation through the Grasshopper components themselves, in a running Rhino 8.
//
// The small cantilever of TopologyOptimisation.cs - 24 x 8 x 2 unit bricks, held at x = 0, loaded at the
// bottom of its free end, the column under the load kept solid - drawn as eight vertex meshes and put
// through SSP Brick, Elastic Isotropic, Support, Point Load, Assemble Model and Topology Optimisation. Its
// first compliance has to be the one the hand-written deck gives, and its run the same. Then a model
// with its own weight, for the warning, and a block of Ten Node Tetrahedra.
//
// Run points Application.UserFolder at the test folder, so the solver path it sets lands there and not
// in the user's settings, and reads the license from LICENSE (the user's own, read only): Topology
// Optimisation needs one.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;

using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

public static class GrasshopperCheck
{
    static StringBuilder report;
    static int fails;
    static string folder;

    // The OpenSees the bricks were solved with, when it is not the tetrahedra's: one that may not know them.
    static string oldOpenSees;

    // What TopologyOptimisation.cs's small run gives on its own deck.
    const double FirstCompliance = 19163.152951324864, LastCompliance = 3387.5529089455385;
    const int Iterations = 57;

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

    public static string Run(string work, string openSees, string openSeesTet10, string license)
    {
        folder = work;
        report = new StringBuilder();
        fails = 0;
        try
        {
            Alpaca4d.Application.UserFolder = work;
            Alpaca4d.AlpacaSettings.OpenSeesPath = openSees;
            if (!string.IsNullOrEmpty(license))
                Alpaca4d.License.License.licenseLocation = license;

            SmallCantilever();
            SelfWeight();
            MixedModel();
            if (!string.IsNullOrEmpty(openSeesTet10))
            {
                oldOpenSees = openSees == openSeesTet10 ? null : openSees;
                Alpaca4d.AlpacaSettings.OpenSeesPath = openSeesTet10;
                Tetrahedra();
            }
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

        /// <summary>
        /// Solves the document: everything, or only what was expired - as the run asks for, so that the
        /// model upstream stays the one the run was started on.
        /// </summary>
        public void Solve(bool everything = true)
        {
            Document.Enabled = true;
            Document.NewSolution(everything, GH_SolutionMode.Silent);
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

    /// <summary>An input changed as on the canvas: the parameter expires, and with it the component.</summary>
    static void Change<T>(GH_Component c, string input, T value) where T : class, IGH_Goo
    {
        Set(c, input, value);
        In(c, input).ExpireSolution(false);
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

    static Mesh Box(double x, double y, double z)
    {
        var mesh = new Mesh();
        foreach (var (dx, dy, dz) in new[] { (0, 0, 0), (1, 0, 0), (1, 1, 0), (0, 1, 0), (0, 0, 1), (1, 0, 1), (1, 1, 1), (0, 1, 1) })
            mesh.Vertices.Add(x + dx, y + dy, z + dz);
        mesh.Faces.AddFace(0, 3, 2, 1);
        mesh.Faces.AddFace(4, 5, 6, 7);
        mesh.Faces.AddFace(0, 1, 5, 4);
        mesh.Faces.AddFace(1, 2, 6, 5);
        mesh.Faces.AddFace(2, 3, 7, 6);
        mesh.Faces.AddFace(3, 0, 4, 7);
        return mesh;
    }

    static GH_Component Material(Doc doc, double rho = 0.0)
    {
        var material = doc.Add<SimplexGh.nD>("ElasticIsotropic (Alpaca4d)");
        Set(material, "E", new GH_Number(1.0));
        Set(material, "G", new GH_Number(1.0 / 2.6));
        Set(material, "ν", new GH_Number(0.3));
        Set(material, "Rho", new GH_Number(rho));
        return material;
    }

    static GH_Component Supports(Doc doc, IEnumerable<Point3d> points)
    {
        var support = doc.Add<Alpaca4d.Gh.Support>();
        Set(support, "Position", points.Select(p => new GH_Plane(new Plane(p, Vector3d.XAxis, Vector3d.YAxis))).ToArray());
        return support;
    }

    /// <summary>Point loads of -1 in z at the points, in a plain pattern, and Assemble Model over it all.</summary>
    static GH_Component Assemble(Doc doc, GH_Component elements, GH_Component supports, IEnumerable<Point3d> loaded, bool gravity = false)
    {
        var load = doc.Add<Alpaca4d.Gh.PointLoad>();
        var points = loaded.ToList();
        Set(load, "Point", points.Select(p => new GH_Point(p)).ToArray());
        Set(load, "Force", points.Select(_ => new GH_Vector(new Vector3d(0, 0, -1))).ToArray());

        var series = doc.Add<Alpaca4d.Gh.LinearTimeSeries>();
        var pattern = doc.Add<Alpaca4d.Gh.PatternBase>("PlainPattern (Alpaca4d)");
        Wire(series, "TimeSeries", pattern, "TimeSeries");
        In(pattern, "Loads").AddSource(load.Params.Output[0]);
        if (gravity)
            In(pattern, "Loads").AddSource(doc.Add<Alpaca4d.Gh.GravityLoad>().Params.Output[0]);

        var assemble = doc.Add<Alpaca4d.Gh.AssembleModel>();
        Wire(elements, "Element", assemble, "Elements");
        Wire(supports, "Support", assemble, "Supports");
        Wire(pattern, pattern.Params.Output[0].Name, assemble, "LoadPatterns");
        return assemble;
    }

    /// <summary>Topology Optimisation's sound as it is, and set to <paramref name="to"/>: the checks run silent.</summary>
    static string Sound(GH_Component topology, string to = "Off")
    {
        var property = topology.GetType().GetProperty("Sound", BindingFlags.NonPublic | BindingFlags.Instance);
        string was = property.GetValue(topology).ToString();
        property.SetValue(topology, Enum.Parse(property.PropertyType, to));
        return was;
    }

    static GH_Component Topology(Doc doc, GH_Component assemble, int iterations, bool run = true)
    {
        var topology = doc.Add<Alpaca4d.Gh.TopologyOptimisation>("TopologyOptimisation");
        string sound = Sound(topology);
        if (sound != "MountainKing")
            Check(false, $"a new Topology Optimisation plays In the Hall of the Mountain King (it plays {sound})");
        Wire(assemble, "AlpacaModel", topology, "AlpacaModel");
        Set(topology, "VolumeFraction", new GH_Number(0.3));
        Set(topology, "Iterations", new GH_Integer(iterations));
        Set(topology, "FilterRadius", new GH_Number(1.5));
        Set(topology, "Run", new GH_Boolean(run));
        return topology;
    }

    /// <summary>Whether the component's run is going.</summary>
    static bool Running(GH_Component topology)
    {
        var run = topology.GetType().GetProperty("Run", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(topology);
        return run != null && (bool)run.GetType().GetProperty("IsRunning").GetValue(run);
    }

    /// <summary>Waits for the component's run, which goes on a thread of its own, then solves again to read it.</summary>
    static bool Finish(Doc doc, GH_Component topology, TimeSpan limit)
    {
        var runProperty = topology.GetType().GetProperty("Run", BindingFlags.NonPublic | BindingFlags.Instance);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (clock.Elapsed < limit)
        {
            var run = runProperty.GetValue(topology);
            if (run != null && !(bool)run.GetType().GetProperty("IsRunning").GetValue(run))
            {
                topology.ExpireSolution(false);
                doc.Solve(everything: false);
                return true;
            }
            Thread.Sleep(200);
        }
        return false;
    }

    // ---------------------------------------------------------------- the checks

    static void SmallCantilever()
    {
        Section("The small cantilever, 24 x 8 x 2 SSP Bricks, through the components");

        var doc = new Doc("cantilever");
        var meshes = new List<Mesh>();
        var ids = new List<string>();
        for (int k = 0; k < 8; k++)
            for (int j = 0; j < 2; j++)
                for (int i = 0; i < 24; i++)
                {
                    meshes.Add(Box(i, j, k));
                    ids.Add(i == 23 && k <= 1 ? "UnderLoad" : "Domain");
                }

        var bricks = doc.Add<Alpaca4d.Gh.SSPbrick>();
        Set(bricks, "Mesh", meshes.Select(m => new GH_Mesh(m)).ToArray());
        Set(bricks, "ElementId", ids.Select(id => new GH_String(id)).ToArray());
        Wire(Material(doc), "Material", bricks, "Material");

        var fixedEnd = new List<Point3d>();
        for (int k = 0; k <= 8; k++)
            for (int j = 0; j <= 2; j++)
                fixedEnd.Add(new Point3d(0, j, k));
        var tip = Enumerable.Range(0, 3).Select(j => new Point3d(24, j, 0));
        var assemble = Assemble(doc, bricks, Supports(doc, fixedEnd), tip);

        var topology = Topology(doc, assemble, 200);
        Set(topology, "Keep", new GH_String("UnderLoad"));
        doc.Solve();
        NoErrors(doc, "every component, as the run starts");
        Check(Finish(doc, topology, TimeSpan.FromMinutes(5)), "the run finishes");
        NoErrors(doc, "every component, once it has");

        var compliance = All<double>(Out(topology, "Compliance"));
        var volume = All<double>(Out(topology, "Volume"));
        Check(compliance.Count > 0 && Math.Abs(compliance[0] / FirstCompliance - 1.0) < 1e-9,
            $"the first compliance is the hand-written deck's: {(compliance.Count > 0 ? compliance[0] : double.NaN):R} for {FirstCompliance:R}");
        Check(compliance.Count == Iterations && Math.Abs(compliance.Last() / LastCompliance - 1.0) < 1e-6,
            $"and so is the run: {compliance.Count} iterations to {(compliance.Count > 0 ? compliance.Last() : double.NaN):R}, for {Iterations} to {LastCompliance:R}");
        Check(All<bool>(Out(topology, "Converged")).SingleOrDefault(), "Converged is true");

        var density = All<double>(Out(topology, "Density"));
        var elements = All<object>(Out(topology, "Element")).OfType<Alpaca4d.Generic.IBrick>().ToList();
        Check(density.Count == 384 && elements.Count == 384, $"a density for each of the 384 bricks ({density.Count})");
        var kept = elements.Select((e, n) => (e, n)).Where(x => x.e.ElementId == "UnderLoad").Select(x => density[x.n]).ToList();
        Check(kept.Count == 4 && kept.All(d => d == 1.0), "the four bricks Keep names stay solid");
        double designed = elements.Select((e, n) => (e, n)).Where(x => x.e.ElementId != "UnderLoad").Average(x => density[x.n]);
        Check(Math.Abs(designed - 0.3) < 2e-3, $"and the others keep 30% of their volume ({designed:0.0000})");

        var solid = All<object>(Out(topology, "Solid")).Count;
        Check(solid == density.Count(d => d >= 0.5), $"Solid has the {solid} bricks at 0.5 or more");

        var surface = All<Mesh>(Out(topology, "Surface")).SingleOrDefault();
        Check(surface != null && surface.Faces.Count > 0 && surface.IsClosed, $"the surface is one closed mesh ({surface?.Faces.Count} faces)");
        // On a grid this coarse a member is two bricks thick, all filter band, and the surface at 0.5 runs
        // inside it: about 70% of the material. On top3d's 60 x 20 x 4 it is 1421.5 for 1440 (./run.sh bench).
        double enclosed = surface != null ? surface.Volume() : 0.0;
        double material = density.Sum();
        Check(enclosed > 0.6 * material && enclosed < 1.1 * material,
            $"enclosing most of the material ({enclosed:0.0} for {material:0.0}; lean on so coarse a grid)");

        string deck = Directory.GetFiles(Path.Combine(folder, "AlpacaResults"), "Topology_*.tcl").OrderBy(File.GetLastWriteTime).Last();
        var lines = File.ReadAllLines(deck);
        Check(lines.Count(l => l.StartsWith("element stdBrick ")) == 384 && !lines.Any(l => l.StartsWith("element SSPbrick")),
            "the deck solves every SSP Brick as a stdBrick");
        Check(!Directory.GetFiles(Path.Combine(folder, "AlpacaResults"), "Topology_*.bin").Any(), "and leaves no result files behind");

        // Run off and on again: a run that converged has nothing left to do.
        Change(topology, "Run", new GH_Boolean(false));
        doc.Solve(everything: false);
        Check(All<double>(Out(topology, "Compliance")).Count == Iterations &&
              !topology.RuntimeMessages(GH_RuntimeMessageLevel.Warning).Any(), "switching Run off keeps the result, without a word");
        Change(topology, "Run", new GH_Boolean(true));
        doc.Solve(everything: false);
        Check(All<double>(Out(topology, "Compliance")).Count == Iterations && !Running(topology),
            "on again, a run that converged stays as it is");

        // Fewer iterations than have run: the design after fewer is not kept, so it starts again.
        Change(topology, "Iterations", new GH_Integer(20));
        doc.Solve(everything: false);
        Check(Finish(doc, topology, TimeSpan.FromMinutes(5)), "Iterations lowered to 20: a run from the beginning finishes");
        var twenty = All<double>(Out(topology, "Compliance"));
        var twentyHistory = All<object>(Out(topology, "History"));
        Check(twenty.Count == 20 && Math.Abs(twenty[19] / compliance[19] - 1.0) < 1e-12 && !All<bool>(Out(topology, "Converged")).Single(),
            $"20 iterations, the 20th as the first run's ({(twenty.Count == 20 ? twenty[19] : double.NaN):R}), not converged");
        var warned = topology.RuntimeMessages(GH_RuntimeMessageLevel.Warning).ToList();
        Check(warned.Any(w => w.Contains("raise Iterations to go on")), $"and it says Iterations can take it on ({string.Join("; ", warned)})");

        // More iterations: it goes on from the 20th, and comes to what one run of all of them comes to.
        Change(topology, "Iterations", new GH_Integer(200));
        doc.Solve(everything: false);
        var resumed = All<object>(Out(topology, "History"));
        Check(Running(topology) && resumed.Count == 20 && resumed.Zip(twentyHistory, ReferenceEquals).All(same => same),
            "Iterations raised to 200: the run goes on, with the first 20 iterations as they were");
        Check(Finish(doc, topology, TimeSpan.FromMinutes(5)), "and finishes");
        var all = All<double>(Out(topology, "Compliance"));
        var allHistory = All<object>(Out(topology, "History"));
        Check(all.Count == Iterations && allHistory.Take(20).Zip(twentyHistory, ReferenceEquals).All(same => same) &&
              Math.Abs(all.Last() / LastCompliance - 1.0) < 1e-9 && All<bool>(Out(topology, "Converged")).Single(),
            $"converging after {all.Count} iterations at {(all.Count > 0 ? all.Last() : double.NaN):R}, as the run of all of them did");

        // Assembled again, the model is a new one, and the run that goes with it starts afresh.
        doc.Solve();
        Check(All<double>(Out(topology, "Compliance")).Count < Iterations, "a model assembled again starts a new run");
        Check(Finish(doc, topology, TimeSpan.FromMinutes(5)) && All<double>(Out(topology, "Compliance")).Count == Iterations,
            "which comes to the same design");
    }

    static void SelfWeight()
    {
        Section("A model with its own weight, not run");

        var doc = new Doc("weight");
        var bricks = doc.Add<Alpaca4d.Gh.SSPbrick>();
        Set(bricks, "Mesh", Enumerable.Range(0, 4).Select(i => new GH_Mesh(Box(i, 0, 0))).ToArray());
        Wire(Material(doc, 2500.0), "Material", bricks, "Material");
        var supports = Supports(doc, new[] { new Point3d(0, 0, 0), new Point3d(0, 1, 0), new Point3d(0, 0, 1), new Point3d(0, 1, 1) });
        var assemble = Assemble(doc, bricks, supports, new[] { new Point3d(4, 0, 0) }, gravity: true);
        var topology = Topology(doc, assemble, 10, run: false);
        doc.Solve();

        var warnings = topology.RuntimeMessages(GH_RuntimeMessageLevel.Warning).ToList();
        Check(warnings.Any(w => w.Contains("self-weight")), "it warns that the self-weight stays on the emptied elements");
        Check(warnings.Any(w => w.Contains("Set Run to true")), "and that Run starts it");
        Check(All<double>(Out(topology, "Density")).Count == 0, "and puts nothing out until it runs");
    }

    static void MixedModel()
    {
        Section("Bricks propped by a truss: a model with an element that is not a solid");

        var doc = new Doc("mixed");
        var bricks = doc.Add<Alpaca4d.Gh.SSPbrick>();
        var boxes = new List<Mesh>();
        for (int k = 0; k < 2; k++)
            for (int i = 0; i < 6; i++)
                boxes.Add(Box(i, 0, k));
        Set(bricks, "Mesh", boxes.Select(m => new GH_Mesh(m)).ToArray());
        Wire(Material(doc), "Material", bricks, "Material");

        // A bar from the bottom of the free end down to a support of its own.
        var material = doc.Add<Alpaca4d.Gh.Uniaxial>("UniaxialElastic (Alpaca4d)");
        var section = doc.Add<Alpaca4d.Gh.RectangleCS>();
        Set(section, "Width", new GH_Number(0.1));
        Set(section, "Height", new GH_Number(0.1));
        Wire(material, "Material", section, "Material");
        var prop = doc.Add<Alpaca4d.Gh.BeamBase>("Truss (Alpaca4d)");
        Set(prop, "Line", new GH_Curve(new LineCurve(new Point3d(6, 0, 0), new Point3d(6, 0, -2))));
        Wire(section, "Section", prop, "Section");

        var fixedEnd = new List<Point3d>();
        for (int k = 0; k <= 2; k++)
            for (int j = 0; j <= 1; j++)
                fixedEnd.Add(new Point3d(0, j, k));
        fixedEnd.Add(new Point3d(6, 0, -2));
        var assemble = Assemble(doc, bricks, Supports(doc, fixedEnd), new[] { new Point3d(6, 1, 2) });
        In(assemble, "Elements").AddSource(Out(prop, "Element"));

        var topology = Topology(doc, assemble, 10);
        doc.Solve();
        var warnings = topology.RuntimeMessages(GH_RuntimeMessageLevel.Warning).ToList();
        Check(warnings.Any(w => w.Contains("also has 1 truss") && w.Contains("Only solids are optimised")),
            $"it warns that the truss takes no part{(warnings.Count == 0 ? ": no warning" : ": " + warnings[0])}");
        Check(Finish(doc, topology, TimeSpan.FromMinutes(2)), "the run finishes");
        NoErrors(doc, "every component");
        var density = All<double>(Out(topology, "Density"));
        var compliance = All<double>(Out(topology, "Compliance"));
        bool converged = All<bool>(Out(topology, "Converged")).SingleOrDefault();
        Check(density.Count == 12 && compliance.Count > 1 && (compliance.Count == 10 || converged),
            $"optimising the 12 bricks: {compliance.Count} iterations, converged {converged}, {density.Count} densities");
        Check(compliance.Count > 1 && compliance.Last() < compliance.First(),
            $"the compliance of the bricks falls, {compliance.FirstOrDefault():G4} to {compliance.LastOrDefault():G4}");
    }

    /// <summary>The cubes of a block cut into six tetrahedra each, as four vertex meshes.</summary>
    static List<Mesh> TetrahedronBlock(int nx, int ny, int nz)
    {
        var cube = new[] { (0, 0, 0), (1, 0, 0), (1, 1, 0), (0, 1, 0), (0, 0, 1), (1, 0, 1), (1, 1, 1), (0, 1, 1) };
        var six = new[] { new[] { 0, 1, 2, 6 }, new[] { 0, 2, 3, 6 }, new[] { 0, 3, 7, 6 }, new[] { 0, 7, 4, 6 }, new[] { 0, 4, 5, 6 }, new[] { 0, 5, 1, 6 } };
        var meshes = new List<Mesh>();
        for (int k = 0; k < nz; k++)
            for (int j = 0; j < ny; j++)
                for (int i = 0; i < nx; i++)
                    foreach (var t in six)
                    {
                        var mesh = new Mesh();
                        foreach (var q in t)
                            mesh.Vertices.Add(i + cube[q].Item1, j + cube[q].Item2, k + cube[q].Item3);
                        mesh.Faces.AddFace(0, 1, 2);
                        mesh.Faces.AddFace(0, 1, 3);
                        mesh.Faces.AddFace(1, 2, 3);
                        mesh.Faces.AddFace(0, 2, 3);
                        meshes.Add(mesh);
                    }
        return meshes;
    }

    static void Tetrahedra()
    {
        Section("A cantilever of 576 Ten Node Tetrahedra, 12 x 2 x 4 cubes");

        var doc = new Doc("tetrahedra");
        var tetrahedra = doc.Add<Alpaca4d.Gh.TenNodeTetrahedron>();
        Set(tetrahedra, "Mesh", TetrahedronBlock(12, 2, 4).Select(m => new GH_Mesh(m)).ToArray());
        Wire(Material(doc), "Material", tetrahedra, "Material");

        // Every node of the end at x = 0: corners and edge nodes, which lie on a half-unit grid there.
        var fixedEnd = new List<Point3d>();
        for (int k = 0; k <= 8; k++)
            for (int j = 0; j <= 4; j++)
                fixedEnd.Add(new Point3d(0, 0.5 * j, 0.5 * k));
        var tip = Enumerable.Range(0, 3).Select(j => new Point3d(12, j, 0));
        var assemble = Assemble(doc, tetrahedra, Supports(doc, fixedEnd), tip);

        var topology = Topology(doc, assemble, 30);
        doc.Solve();
        Check(Finish(doc, topology, TimeSpan.FromMinutes(5)), "the run finishes");
        NoErrors(doc, "every component");

        var compliance = All<double>(Out(topology, "Compliance"));
        Check(compliance.Count == 30 || All<bool>(Out(topology, "Converged")).SingleOrDefault(), $"it runs its iterations ({compliance.Count})");
        Check(compliance.Count > 1 && compliance.Last() < 0.5 * compliance.First(),
            $"the compliance falls from {compliance.FirstOrDefault():0.0} to {compliance.LastOrDefault():0.0}");
        var volume = All<double>(Out(topology, "Volume"));
        Check(volume.Count > 0 && Math.Abs(volume.Last() - 0.3) < 2e-3, $"keeping 30% of the volume ({volume.LastOrDefault():0.0000})");
        var surface = All<Mesh>(Out(topology, "Surface")).SingleOrDefault();
        Check(surface != null && surface.Faces.Count > 0 && surface.IsClosed, $"the surface is one closed mesh ({surface?.Faces.Count} faces)");

        string deck = Directory.GetFiles(Path.Combine(folder, "AlpacaResults"), "Topology_*.tcl").OrderBy(File.GetLastWriteTime).Last();
        Check(File.ReadAllLines(deck).Count(l => l.StartsWith("element TenNodeTetrahedronStd ")) == 576, "the tetrahedra are solved as they are");

        // An OpenSees from before the element's fixes stops at its first line, and says why.
        if (!string.IsNullOrEmpty(oldOpenSees))
        {
            Alpaca4d.AlpacaSettings.OpenSeesPath = oldOpenSees;
            Change(topology, "Iterations", new GH_Integer(5));
            doc.Solve(everything: false);
            Check(Finish(doc, topology, TimeSpan.FromMinutes(2)), "on an OpenSees without TenNodeTetrahedronStd the run ends");
            var errors = topology.RuntimeMessages(GH_RuntimeMessageLevel.Error).ToList();
            var run = topology.GetType().GetProperty("Run", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(topology);
            string failure = (string)run?.GetType().GetProperty("Failure").GetValue(run);
            Check(errors.Any(e => e.Contains("does not know the Ten Node Tetrahedron")),
                $"with the reason{(errors.Count == 0 ? $": no error (the run's failure: {failure ?? "none"}; compliances {All<double>(Out(topology, "Compliance")).Count})" : ": " + errors[0])}");
        }
    }
}
