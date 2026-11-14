// Node Forces through the Grasshopper components themselves, in a running Rhino 8.
//
// Each model is a Grasshopper document built in code out of the real components - Uniaxial,
// RectangleCS, the beam switcher on its Truss or ForceBeamColumn type, Link, Support, Point Load,
// Linear Load, Linear Time Series, Load Pattern, Assemble Model, Analysis Settings, Run Analysis -
// solved the way the canvas solves it, and read back through Node Forces beside Beam Forces and
// Reaction Forces. grasshopper.sh loads this build of the plugin beside the installed one and calls
// Run by reflection.
using System;
using System.Collections.Generic;
using System.Diagnostics;
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

    static bool Near(double a, double b, double tol) => Math.Abs(a - b) <= tol * Math.Max(1.0, Math.Abs(b));

    public static string Run(string work, string license)
    {
        folder = work;
        report = new StringBuilder();
        fails = 0;
        try
        {
            if (File.Exists(license))
                Alpaca4d.License.License.Install(license);

            Girder();
            RotatedAxes();
            NoShearSection();
            Portal();
            PortalWithLink();
            Grillage(60);
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

    static GH_Curve Line(Point3d from, Point3d to) => new GH_Curve(new LineCurve(from, to));

    static GH_Point Pt(double x, double y, double z) => new GH_Point(new Point3d(x, y, z));

    static List<double> Numbers(IGH_Param p, GH_Path path) =>
        p.VolatileData.get_Branch(path).Cast<object>().Select(o => o is GH_Number n ? n.Value : Convert.ToDouble(((IGH_Goo)o).ScriptVariable())).ToList();

    static List<Vector3d> Vectors(IGH_Param p, GH_Path path) =>
        p.VolatileData.get_Branch(path).Cast<object>().Select(o => ((GH_Vector)o).Value).ToList();

    static List<Alpaca4d.Generic.IBeam> Members(IGH_Param p, GH_Path path) =>
        p.VolatileData.get_Branch(path).Cast<object>().Select(o => (Alpaca4d.Generic.IBeam)((IGH_Goo)o).ScriptVariable()).ToList();

    static Vector3d Sum(IEnumerable<Vector3d> vectors) => vectors.Aggregate(Vector3d.Zero, (sum, v) => sum + v);

    /// <summary>A material and a w x h rectangle of it, the section every model here uses.</summary>
    static GH_Component Section(Doc doc, double width, double height)
    {
        var material = doc.Add<Alpaca4d.Gh.Uniaxial>("UniaxialElastic (Alpaca4d)");
        var section = doc.Add<Alpaca4d.Gh.RectangleCS>();
        Set(section, "Width", new GH_Number(width));
        Set(section, "Height", new GH_Number(height));
        Wire(material, "Material", section, "Material");
        return section;
    }

    static GH_Component Support(Doc doc, IEnumerable<Point3d> at, bool tx, bool ty, bool tz, bool rx, bool ry, bool rz)
    {
        var support = doc.Add<Alpaca4d.Gh.Support>();
        Set(support, "Position", at.Select(p => new GH_Plane(new Plane(p, Vector3d.XAxis, Vector3d.YAxis))).ToArray());
        var states = new[] { tx, ty, tz, rx, ry, rz };
        var names = new[] { "Tx", "Ty", "Tz", "Rx", "Ry", "Rz" };
        for (int i = 0; i < 6; i++)
            Set(support, names[i], new GH_Boolean(states[i]));
        return support;
    }

    static GH_Component PointLoad(Doc doc, IEnumerable<Point3d> at, Vector3d force)
    {
        var load = doc.Add<Alpaca4d.Gh.PointLoad>();
        Set(load, "Point", at.Select(p => new GH_Point(p)).ToArray());
        Set(load, "Force", new GH_Vector(force));
        return load;
    }

    /// <summary>Loads into a plain pattern on a linear time series, a model assembled, settings, and a run.</summary>
    static GH_Component Analyse(Doc doc, IEnumerable<GH_Component> elements, IEnumerable<GH_Component> supports, IEnumerable<GH_Component> loads)
    {
        var series = doc.Add<Alpaca4d.Gh.LinearTimeSeries>();
        var pattern = doc.Add<Alpaca4d.Gh.PatternBase>("PlainPattern (Alpaca4d)");
        Wire(series, "TimeSeries", pattern, "TimeSeries");
        foreach (var load in loads)
            In(pattern, "Loads").AddSource(load.Params.Output[0]);

        var assemble = doc.Add<Alpaca4d.Gh.AssembleModel>();
        foreach (var element in elements)
            In(assemble, "Elements").AddSource(element.Params.Output[0]);
        foreach (var support in supports)
            In(assemble, "Supports").AddSource(support.Params.Output[0]);
        In(assemble, "LoadPatterns").AddSource(pattern.Params.Output[0]);

        var settings = doc.Add<Alpaca4d.Gh.AnalysisSettings>();
        var run = doc.Add<Alpaca4d.Gh.RunAnalysis>();
        Wire(assemble, "AlpacaModel", run, "AlpacaModel");
        Wire(settings, "Settings", run, "Settings");
        return run;
    }

    static T Reader<T>(Doc doc, GH_Component run) where T : GH_Component, new()
    {
        var reader = doc.Add<T>();
        Wire(run, "AlpacaModel", reader, "AlpacaModel");
        return reader;
    }

    static void NoErrors(Doc doc, string what)
    {
        var errors = doc.Messages(GH_RuntimeMessageLevel.Error).ToList();
        Check(errors.Count == 0, errors.Count == 0 ? $"{what}: no component has an error" : $"{what}: {string.Join(" | ", errors)}");
    }

    static bool Joins(Alpaca4d.Generic.IBeam beam, Point3d a, Point3d b) =>
        (beam.Curve.PointAtStart.DistanceTo(a) < 1e-6 && beam.Curve.PointAtEnd.DistanceTo(b) < 1e-6) ||
        (beam.Curve.PointAtStart.DistanceTo(b) < 1e-6 && beam.Curve.PointAtEnd.DistanceTo(a) < 1e-6);

    // ---------------------------------------------------------------- the models

    /// <summary>
    /// A Warren girder of trusses, 12 m in six bays of 2 m, 1.5 m deep, simply supported, with
    /// 10 kN hung from each inner bottom node. The joint at x = 4 against the method of joints: the
    /// chords carry the moment over the depth, the diagonals the shear over sin θ.
    /// </summary>
    static void Girder()
    {
        Section("A Warren girder of trusses, 10 kN at each inner bottom node, read at the bottom node x = 4");
        var doc = new Doc("girder");
        var section = Section(doc, 0.1, 0.1);
        const double P = 10.0, h = 1.5;

        var bottom = Enumerable.Range(0, 7).Select(k => new Point3d(2 * k, 0, 0)).ToList();
        var top = Enumerable.Range(0, 6).Select(k => new Point3d(2 * k + 1, 0, h)).ToList();
        var lines = new List<GH_Curve>();
        for (int k = 0; k < 6; k++) lines.Add(Line(bottom[k], bottom[k + 1]));
        for (int k = 0; k < 5; k++) lines.Add(Line(top[k], top[k + 1]));
        for (int k = 0; k < 6; k++) { lines.Add(Line(bottom[k], top[k])); lines.Add(Line(top[k], bottom[k + 1])); }

        var bars = doc.Add<Alpaca4d.Gh.BeamBase>("Truss (Alpaca4d)");
        Set(bars, "Line", lines.ToArray());
        Wire(section, "Section", bars, "Section");

        // Pinned at x = 0, a roller at x = 12, and every other node held out of the plane.
        var held = bottom.Skip(1).Take(5).Concat(top).ToList();
        var supports = new[]
        {
            Support(doc, new[] { bottom[0] }, true, true, true, false, false, false),
            Support(doc, new[] { bottom[6] }, false, true, true, false, false, false),
            Support(doc, held, false, true, false, false, false, false),
        };
        var load = PointLoad(doc, bottom.Skip(1).Take(5), new Vector3d(0, 0, -P));
        var run = Analyse(doc, new[] { bars }, supports, new[] { load });

        var byPoint = Reader<Alpaca4d.Gh.NodeForce>(doc, run);
        Set(byPoint, "Point", new GH_Point(bottom[2] + new Vector3d(0.004, 0, -0.003)));
        var everyNode = Reader<Alpaca4d.Gh.NodeForce>(doc, run);
        var missing = Reader<Alpaca4d.Gh.NodeForce>(doc, run);
        Set(missing, "Point", Pt(4, 0, 0.5));
        Set(missing, "NodeTag", new GH_Integer(999));
        doc.Solve();

        NoErrors(doc, "the whole definition");
        Check(!byPoint.RuntimeMessages(GH_RuntimeMessageLevel.Warning).Any() && !byPoint.RuntimeMessages(GH_RuntimeMessageLevel.Remark).Any(),
              "Node Forces by point has nothing to say");

        var n = Out(byPoint, "N");
        var paths = n.VolatileData.Paths.ToList();
        Check(paths.Count == 1, $"a point a few mm off the node picks it: one branch ({paths.Count})");
        if (paths.Count != 1) return;
        var path = paths[0];
        int tag = path.Indices[0];

        var members = Members(Out(byPoint, "Element"), path);
        var axial = Numbers(n, path);
        var forces = Vectors(Out(byPoint, "Force"), path);
        Check(members.Count == 4 && axial.Count == 4 && forces.Count == 4, $"four members meet at the node ({members.Count})");

        // Trusses are corotational, so the girder's sag moves the forces off the linear closed form
        // by about 1e-4: a few parts in ten thousand, not an error.
        double sin = h / Math.Sqrt(1 + h * h);
        Func<Point3d, double> axialTo = other => axial[members.FindIndex(beam => Joins(beam, bottom[2], other))];
        double leftChord = axialTo(bottom[1]), rightChord = axialTo(bottom[3]), upLeft = axialTo(top[1]), upRight = axialTo(top[2]);
        Check(Near(leftChord, 6.5 * P / h, 1e-3), $"left chord: M(3) / h = 6.5 P / 1.5 = {6.5 * P / h:G6} kN in tension ({leftChord:G6})");
        Check(Near(rightChord, 8.5 * P / h, 1e-3), $"right chord: M(5) / h = 8.5 P / 1.5 = {8.5 * P / h:G6} kN in tension ({rightChord:G6})");
        Check(Near(upLeft, 1.5 * P / sin, 1e-3), $"diagonal up to x = 3: V / sin θ = 1.5 P / {sin:G4} = {1.5 * P / sin:G6} kN in tension ({upLeft:G6})");
        Check(Near(upRight, -0.5 * P / sin, 1e-3), $"diagonal up to x = 5: 0.5 P / sin θ = {0.5 * P / sin:G6} kN in compression ({upRight:G6})");

        // Exactly, though, the joint balances: the bars pull along where they have moved to.
        var sum = Sum(forces);
        Check((sum - new Vector3d(0, 0, P)).Length < 1e-6, $"the four forces on the node add up to the 10 kN hung from it, the other way ({sum})");
        var towards = forces[members.FindIndex(beam => Joins(beam, bottom[2], bottom[3]))];
        Check(towards.X > 0 && Math.Abs(towards.Y) < 1e-9 && Math.Abs(towards.Z) < 1e-3 * towards.X,
              $"the right chord, in tension, pulls the node along +x, turned a little by the sag ({towards})");
        Check(Vectors(Out(byPoint, "Moment"), path).All(m => m.Length == 0.0), "trusses bring no moment");

        var position = Out(byPoint, "Position").VolatileData.get_Branch(path).Cast<GH_Point>().Single().Value;
        Check(position.DistanceTo(bottom[2]) < 1e-9, "Position is the node itself, not the point picked");

        // The same node by its tag.
        var byTag = Reader<Alpaca4d.Gh.NodeForce>(doc, run);
        Set(byTag, "NodeTag", new GH_Integer(tag));
        byTag.ExpireSolution(false);
        doc.Solve();
        var tagged = Numbers(Out(byTag, "N"), path);
        Check(tagged.SequenceEqual(axial), $"NodeTag {tag} gives the same branch as the point");

        var all = Out(everyNode, "N").VolatileData;
        Check(all.PathCount == 13 && all.DataCount == 2 * lines.Count,
              $"left empty, every node: 13 branches holding both ends of all {lines.Count} bars ({all.PathCount}, {all.DataCount})");
        Check(all.Paths.Select(p => p.Indices[0]).SequenceEqual(all.Paths.Select(p => p.Indices[0]).OrderBy(t => t)), "in tag order");

        var warnings = missing.RuntimeMessages(GH_RuntimeMessageLevel.Warning).ToList();
        Check(warnings.Any(m => m.Contains("No beam or truss ends within")), "a point off every node is warned about");
        Check(warnings.Any(m => m.Contains("no node 999")), "and so is a tag the model does not have");
        var missingData = Out(missing, "N").VolatileData;
        Check(missingData.DataCount == 0, $"and neither gives a branch ({missingData.PathCount} branches: {string.Join(" ", missingData.Paths)}; {string.Join(" | ", warnings)})");
    }

    /// <summary>
    /// Two cantilevers whose local axes are turned by the ZAxis input of the beam component: one
    /// along x with z leaning 30° off vertical, one standing up with z 60° round from x. Each fixed
    /// at its start and loaded at its tip by F. With x along the beam, z as given and y = z × x, the
    /// fixed end carries Vy = -F·y, Vz = F·z, My = -L F·z and Mz = L F·y in Beam Forces' signs, the
    /// tip the same shears and no moment - so a force read about the wrong axis, or with the wrong
    /// sign, shows.
    /// </summary>
    static void RotatedAxes()
    {
        Section("Cantilevers with their local axes turned by ZAxis, against closed form");
        var doc = new Doc("rotated");
        var section = Section(doc, 0.2, 0.4);

        double a = Math.PI / 6, b = Math.PI / 3;
        var cases = new[]
        {
            (name: "along x, z 30° off vertical", root: new Point3d(0, 0, 0), tip: new Point3d(3, 0, 0),
             z: new Vector3d(0, Math.Sin(a), Math.Cos(a)), load: new Vector3d(0, 0, -10)),
            (name: "standing, z 60° round from x", root: new Point3d(10, 0, 0), tip: new Point3d(10, 0, 4),
             z: new Vector3d(Math.Cos(b), Math.Sin(b), 0), load: new Vector3d(5, 0, 0)),
        };

        var beams = new List<GH_Component>();
        var loads = new List<GH_Component>();
        foreach (var c in cases)
        {
            var beam = doc.Add<Alpaca4d.Gh.BeamBase>("ForceBeamColumn (Alpaca4d)");
            Set(beam, "Line", Line(c.root, c.tip));
            Set(beam, "ZAxis", new GH_Vector(c.z));
            Wire(section, "Section", beam, "Section");
            beams.Add(beam);
            loads.Add(PointLoad(doc, new[] { c.tip }, c.load));
        }
        var supports = new[] { Support(doc, cases.Select(c => c.root), true, true, true, true, true, true) };
        var run = Analyse(doc, beams, supports, loads);
        var nodeForces = Reader<Alpaca4d.Gh.NodeForce>(doc, run);
        Set(nodeForces, "Point", cases.SelectMany(c => new[] { new GH_Point(c.root), new GH_Point(c.tip) }).ToArray());
        doc.Solve();
        NoErrors(doc, "the definition");

        var paths = Out(nodeForces, "N").VolatileData.Paths.ToList();
        Func<Point3d, GH_Path> branchAt = node => paths.Single(p =>
            Out(nodeForces, "Position").VolatileData.get_Branch(p).Cast<GH_Point>().Single().Value.DistanceTo(node) < 1e-9);
        Func<string, GH_Path, double> one = (name, path) => Numbers(Out(nodeForces, name), path).Single();

        foreach (var c in cases)
        {
            var x = c.tip - c.root;
            double length = x.Length;
            x /= length;
            var y = Vector3d.CrossProduct(c.z, x);
            double fy = c.load * y, fz = c.load * c.z;

            var member = Members(Out(nodeForces, "Element"), branchAt(c.root)).Single();
            Check((member.GeomTransf.LocalZ - c.z).Length < 1e-12, $"{c.name}: the beam's local z is the ZAxis given");

            var root = branchAt(c.root);
            var got = new[] { one("N", root), one("Vy", root), one("Vz", root), one("Mx", root), one("My", root), one("Mz", root) };
            var want = new[] { 0.0, -fy, fz, 0.0, -length * fz, length * fy };
            Check(got.Zip(want, (g, w) => Math.Abs(g - w)).Max() < 1e-6,
                  $"{c.name}, fixed end: N Vy Vz Mx My Mz = {string.Join(" ", got.Select(v => v.ToString("G5")))}, want {string.Join(" ", want.Select(v => v.ToString("G5")))}");

            var tip = branchAt(c.tip);
            var gotTip = new[] { one("Vy", tip), one("Vz", tip), one("My", tip), one("Mz", tip) };
            var wantTip = new[] { -fy, fz, 0.0, 0.0 };
            Check(gotTip.Zip(wantTip, (g, w) => Math.Abs(g - w)).Max() < 1e-6,
                  $"{c.name}, tip: Vy Vz My Mz = {string.Join(" ", gotTip.Select(v => v.ToString("G5")))}, want {string.Join(" ", wantTip.Select(v => v.ToString("G5")))}");

            var force = Vectors(Out(nodeForces, "Force"), root).Single();
            var moment = Vectors(Out(nodeForces, "Moment"), root).Single();
            var wantMoment = Vector3d.CrossProduct(c.tip - c.root, c.load);
            Check((force - c.load).Length < 1e-6 && (moment - wantMoment).Length < 1e-6,
                  $"{c.name}, fixed end in global axes: the load {c.load} and its moment {wantMoment} ({force}, {moment})");
            Check((Vectors(Out(nodeForces, "Force"), tip).Single() + c.load).Length < 1e-6, $"{c.name}, tip: the beam holds the load up");
        }
    }

    /// <summary>
    /// A cantilever on an Elastic Section with AlphaY and AlphaZ 0, which OpenSees builds without
    /// shear and whose sections record P, Mz, My and T only, under a uniform load and a tip load.
    /// Beam Forces read its five sections as three, with the wrong forces in them; now they are five,
    /// the shears worked out from the moments, and Node Forces balances the reaction with them.
    /// </summary>
    static void NoShearSection()
    {
        Section("A cantilever on an Elastic Section without shear factors, through Beam Forces and Node Forces");
        var doc = new Doc("no-shear");
        var material = doc.Add<Alpaca4d.Gh.Uniaxial>("UniaxialElastic (Alpaca4d)");
        var section = doc.Add<Alpaca4d.Gh.ElasticSection>();
        Set(section, "Area", new GH_Number(0.01));
        Set(section, "Izz", new GH_Number(2e-4));
        Set(section, "Iyy", new GH_Number(1e-4));
        Set(section, "J", new GH_Number(5e-5));
        Set(section, "AlphaY", new GH_Number(0.0));
        Set(section, "AlphaZ", new GH_Number(0.0));
        Wire(material, "Material", section, "Material");

        const double L = 4.0, P = 10.0, w = 3.0;
        var root = new Point3d(0, 0, 0); var tip = new Point3d(L, 0, 0);
        var beam = doc.Add<Alpaca4d.Gh.BeamBase>("ForceBeamColumn (Alpaca4d)");
        Set(beam, "Line", Line(root, tip));
        Wire(section, "Section", beam, "Section");
        var supports = new[] { Support(doc, new[] { root }, true, true, true, true, true, true) };
        var tipLoad = PointLoad(doc, new[] { tip }, new Vector3d(0, 0, -P));
        var lineLoad = doc.Add<Alpaca4d.Gh.LinearLoad>();
        In(lineLoad, lineLoad.Params.Input[0].Name).AddSource(Out(beam, "Element"));
        Set(lineLoad, "Force", new GH_Vector(new Vector3d(0, 0, -w)));
        var run = Analyse(doc, new[] { beam }, supports, new[] { tipLoad, lineLoad });
        var beamForces = Reader<Alpaca4d.Gh.BeamForce>(doc, run);
        var nodeForces = Reader<Alpaca4d.Gh.NodeForce>(doc, run);
        Set(nodeForces, "Point", new GH_Point(root));
        var reactions = Reader<Alpaca4d.Gh.ReactionForce>(doc, run);
        doc.Solve();
        NoErrors(doc, "the definition");

        var path = Out(beamForces, "My").VolatileData.Paths.Single();
        var my = Numbers(Out(beamForces, "My"), path);
        var vz = Numbers(Out(beamForces, "Vz"), path);
        Check(my.Count == 5 && vz.Count == 5, $"Beam Forces reads all five sections ({my.Count})");

        // Local z of a beam along x left without a ZAxis is horizontal and local y up, so the load down
        // -Z is along -y: the shear Vy and the moment Mz carry it, Vz and My are zero.
        var vy = Numbers(Out(beamForces, "Vy"), path);
        var mz = Numbers(Out(beamForces, "Mz"), path);
        var local = Members(Out(nodeForces, "Element"), Out(nodeForces, "Element").VolatileData.Paths.Single()).Single();
        var y = local.GeomTransf.LocalY;
        Check(Math.Abs(y.Z) > 0.99, $"the beam's local y is vertical ({y})");
        double s = Math.Sign(y.Z);
        bool shears = Enumerable.Range(0, 5).All(k =>
        {
            double x = L * k / 4.0;
            double want = s * (P + w * (L - x));               // Vy = dMz/dx: a load towards -y reads positive
            return Math.Abs(vy[k] - want) < 1e-6 * Math.Max(1.0, Math.Abs(want));
        });
        Check(shears, $"Vy, worked out from the moments, is the load beyond each section: {string.Join(" ", vy.Select(v => v.ToString("G6")))}");
        Check(Math.Abs(mz[0] - s * -(P * L + w * L * L / 2)) < 1e-6 * (P * L + w * L * L / 2),
              $"Mz at the root is the cantilever's moment, {P * L + w * L * L / 2:G6} kN m ({mz[0]:G6})");
        Check(vz.All(v => Math.Abs(v) < 1e-9) && my.All(v => Math.Abs(v) < 1e-9), "Vz and My are zero");

        var nodePath = Out(nodeForces, "Force").VolatileData.Paths.Single();
        var force = Vectors(Out(nodeForces, "Force"), nodePath).Single();
        var moment = Vectors(Out(nodeForces, "Moment"), nodePath).Single();
        var reaction = Out(reactions, "ReactionForce").VolatileData.AllData(true).Select(g => ((GH_Vector)g).Value).Single();
        var reactionMoment = Out(reactions, "ReactionMoment").VolatileData.AllData(true).Select(g => ((GH_Vector)g).Value).Single();
        Check((force + reaction).Length < 1e-6 && (moment + reactionMoment).Length < 1e-6 && Math.Abs(force.Z + P + w * L) < 1e-6,
              $"Node Forces at the root: the beam puts the {P + w * L:G4} kN of load on the support, which Reaction Forces holds back ({force}, {reaction})");
    }

    /// <summary>
    /// A portal frame of beams, fixed at its feet, 10 kN/m down its 6 m beam and 5 kN along x at
    /// its left knee. The knees and a foot against Beam Forces, Reaction Forces and statics.
    /// </summary>
    static void Portal()
    {
        Section("A portal frame of beams, 10 kN/m on its beam and 5 kN across at its left knee");
        var doc = new Doc("portal");
        var section = Section(doc, 0.3, 0.5);

        var footA = new Point3d(0, 0, 0); var kneeA = new Point3d(0, 0, 4);
        var footB = new Point3d(6, 0, 0); var kneeB = new Point3d(6, 0, 4);
        var columns = doc.Add<Alpaca4d.Gh.BeamBase>("ForceBeamColumn (Alpaca4d)");
        Set(columns, "Line", Line(footA, kneeA), Line(footB, kneeB));
        Wire(section, "Section", columns, "Section");
        var beam = doc.Add<Alpaca4d.Gh.BeamBase>("ForceBeamColumn (Alpaca4d)");
        Set(beam, "Line", Line(kneeA, kneeB));
        Wire(section, "Section", beam, "Section");

        var supports = new[] { Support(doc, new[] { footA, footB }, true, true, true, true, true, true) };
        var lineLoad = doc.Add<Alpaca4d.Gh.LinearLoad>();
        In(lineLoad, lineLoad.Params.Input[0].Name).AddSource(Out(beam, "Element"));
        Set(lineLoad, "Force", new GH_Vector(new Vector3d(0, 0, -10)));
        var push = PointLoad(doc, new[] { kneeA }, new Vector3d(5, 0, 0));
        var run = Analyse(doc, new[] { columns, beam }, supports, new[] { lineLoad, push });

        var nodeForces = Reader<Alpaca4d.Gh.NodeForce>(doc, run);
        Set(nodeForces, "Point", new GH_Point(kneeA), new GH_Point(kneeB), new GH_Point(footA));
        var history = Reader<Alpaca4d.Gh.NodeForce>(doc, run);
        Set(history, "Point", new GH_Point(kneeA));
        Set(history, "History", new GH_Boolean(true));
        var beamForces = Reader<Alpaca4d.Gh.BeamForce>(doc, run);
        var reactions = Reader<Alpaca4d.Gh.ReactionForce>(doc, run);
        doc.Solve();
        NoErrors(doc, "the definition");

        // A tree keeps its branches in the order of their paths, so they come in tag order whatever
        // order the points were given in. Each is found by where its node is.
        var paths = Out(nodeForces, "N").VolatileData.Paths.ToList();
        Check(paths.Count == 3, $"three points, three branches ({paths.Count})");
        if (paths.Count != 3) return;
        Check(paths.Select(p => p.Indices[0]).SequenceEqual(paths.Select(p => p.Indices[0]).OrderBy(t => t)), "in tag order");

        Func<Point3d, GH_Path> branchAt = node => paths.Single(p =>
            Out(nodeForces, "Position").VolatileData.get_Branch(p).Cast<GH_Point>().Single().Value.DistanceTo(node) < 1e-9);
        var atA = branchAt(kneeA); var atB = branchAt(kneeB); var atFoot = branchAt(footA);
        var forceA = Sum(Vectors(Out(nodeForces, "Force"), atA));
        var momentA = Sum(Vectors(Out(nodeForces, "Moment"), atA));
        Check((forceA + new Vector3d(5, 0, 0)).Length < 1e-4 && momentA.Length < 1e-4,
              $"left knee: the column and the beam balance the 5 kN, and their moments cancel ({forceA}, {momentA})");
        var forceB = Sum(Vectors(Out(nodeForces, "Force"), atB));
        var momentB = Sum(Vectors(Out(nodeForces, "Moment"), atB));
        Check(forceB.Length < 1e-4 && momentB.Length < 1e-4, $"right knee, unloaded: they add up to nothing ({forceB}, {momentB})");

        var beamAtB = Members(Out(nodeForces, "Element"), atB).FindIndex(m => Joins(m, kneeA, kneeB));
        var shearIntoB = Vectors(Out(nodeForces, "Force"), atB)[beamAtB];
        Check(shearIntoB.Z < -30.0 && shearIntoB.Z > -36.0,
              $"the beam pushes down on the right knee with half its 60 kN, and a little more from the push towards it ({shearIntoB.Z:G5} kN)");

        // Against Beam Forces: the same numbers as the end of each member's own branch.
        var names = new[] { "N", "Vy", "Vz", "Mx", "My", "Mz" };
        bool same = true;
        foreach (var path in new[] { atA, atB, atFoot })
        {
            var members = Members(Out(nodeForces, "Element"), path);
            var node = Out(nodeForces, "Position").VolatileData.get_Branch(path).Cast<GH_Point>().Single().Value;
            for (int k = 0; k < members.Count; k++)
            {
                var own = new GH_Path(members[k].Id.Value);
                bool atJ = members[k].Curve.PointAtEnd.DistanceTo(node) < 1e-6;
                foreach (var name in names)
                {
                    var along = Numbers(Out(beamForces, name), own);
                    same &= Numbers(Out(nodeForces, name), path)[k] == along[atJ ? along.Count - 1 : 0];
                }
            }
        }
        Check(same, "N to Mz at every member end are Beam Forces' first or last values for that member, to the bit");

        var reactionAt = Out(reactions, "SupportPosition").VolatileData.AllData(true).Select(g => ((GH_Plane)g).Value.Origin).ToList()
                         .FindIndex(p => p.DistanceTo(footA) < 1e-9);
        var reactionForce = Out(reactions, "ReactionForce").VolatileData.AllData(true).Select(g => ((GH_Vector)g).Value).ToList()[reactionAt];
        var reactionMoment = Out(reactions, "ReactionMoment").VolatileData.AllData(true).Select(g => ((GH_Vector)g).Value).ToList()[reactionAt];
        var footForce = Sum(Vectors(Out(nodeForces, "Force"), atFoot));
        var footMoment = Sum(Vectors(Out(nodeForces, "Moment"), atFoot));
        Check((footForce + reactionForce).Length < 1e-4 && (footMoment + reactionMoment).Length < 1e-4,
              $"left foot: what the column puts on it is what Reaction Forces says the support holds back ({footForce} against {reactionForce})");

        var historyPaths = Out(history, "Force").VolatileData.Paths.ToList();
        Check(historyPaths.Count >= 1 && historyPaths.All(p => p.Length == 2 && p.Indices[1] == atA.Indices[0]),
              $"History: every branch is {{step; tag}} ({string.Join(" ", historyPaths)})");
        Check(Out(history, "Position").VolatileData.Paths.SequenceEqual(historyPaths), "and Position has the same tree, so the two pair up");
    }

    /// <summary>The same frame with a link from the left knee to the right foot: the members alone no longer balance there.</summary>
    static void PortalWithLink()
    {
        Section("The portal with a link brace from its left knee to its right foot");
        var doc = new Doc("portal-link");
        var section = Section(doc, 0.3, 0.5);

        var footA = new Point3d(0, 0, 0); var kneeA = new Point3d(0, 0, 4);
        var footB = new Point3d(6, 0, 0); var kneeB = new Point3d(6, 0, 4);
        var frame = doc.Add<Alpaca4d.Gh.BeamBase>("ForceBeamColumn (Alpaca4d)");
        Set(frame, "Line", Line(footA, kneeA), Line(footB, kneeB), Line(kneeA, kneeB));
        Wire(section, "Section", frame, "Section");

        var spring = doc.Add<Alpaca4d.Gh.Uniaxial>("UniaxialElastic (Alpaca4d)");
        var link = doc.Add<Alpaca4d.Gh.Link>();
        Set(link, "Line", new GH_Line(new Rhino.Geometry.Line(kneeA, footB)));
        Wire(spring, "Material", link, "Material");
        Set(link, "Direction", new GH_Integer(1));

        var supports = new[] { Support(doc, new[] { footA, footB }, true, true, true, true, true, true) };
        var push = PointLoad(doc, new[] { kneeA }, new Vector3d(50, 0, 0));
        var run = Analyse(doc, new GH_Component[] { frame, link }, supports, new[] { push });

        var nodeForces = Reader<Alpaca4d.Gh.NodeForce>(doc, run);
        Set(nodeForces, "Point", new GH_Point(kneeA), new GH_Point(kneeB));
        doc.Solve();
        NoErrors(doc, "the definition");

        var paths = Out(nodeForces, "N").VolatileData.Paths.ToList();
        Func<Point3d, GH_Path> branchAt = node => paths.Single(p =>
            Out(nodeForces, "Position").VolatileData.get_Branch(p).Cast<GH_Point>().Single().Value.DistanceTo(node) < 1e-9);
        var atKnee = branchAt(kneeA);

        var remarks = nodeForces.RuntimeMessages(GH_RuntimeMessageLevel.Remark).ToList();
        Check(remarks.Count == 1 && remarks[0].StartsWith($"Node {atKnee.Indices[0]} also meets"),
              $"a remark names the knee the link meets, and only that one: {string.Join(" | ", remarks)}");
        var sum = Sum(Vectors(Out(nodeForces, "Force"), atKnee)) + new Vector3d(50, 0, 0);
        Check(sum.Length > 1.0, $"and there the two members alone are short of the load by what the link carries ({sum.Length:G4} kN)");
        var other = Sum(Vectors(Out(nodeForces, "Force"), branchAt(kneeB)));
        Check(other.Length < 1e-4, $"the other knee, which the link does not meet, still balances ({other.Length:G2})");
    }

    /// <summary>
    /// A grillage of n by n bays of beams, fixed at its corners, a point load in its middle: how long
    /// Node Forces takes over one node and over all of them, beside Beam Forces reading every beam.
    /// </summary>
    static void Grillage(int n)
    {
        Section($"Time: a grillage of {n} x {n} bays of beams");
        var doc = new Doc("grillage");
        var section = Section(doc, 0.2, 0.4);

        var lines = new List<GH_Curve>();
        for (int j = 0; j <= n; j++)
            for (int i = 0; i < n; i++)
            {
                lines.Add(Line(new Point3d(i, j, 0), new Point3d(i + 1, j, 0)));
                lines.Add(Line(new Point3d(j, i, 0), new Point3d(j, i + 1, 0)));
            }
        var beams = doc.Add<Alpaca4d.Gh.BeamBase>("ForceBeamColumn (Alpaca4d)");
        Set(beams, "Line", lines.ToArray());
        Wire(section, "Section", beams, "Section");

        var corners = new[] { new Point3d(0, 0, 0), new Point3d(n, 0, 0), new Point3d(0, n, 0), new Point3d(n, n, 0) };
        var supports = new[] { Support(doc, corners, true, true, true, true, true, true) };
        var middle = new Point3d(n / 2, n / 2, 0);
        var load = PointLoad(doc, new[] { middle }, new Vector3d(0, 0, -100));
        var run = Analyse(doc, new[] { beams }, supports, new[] { load });

        var one = Reader<Alpaca4d.Gh.NodeForce>(doc, run);
        Set(one, "Point", new GH_Point(middle));
        var all = Reader<Alpaca4d.Gh.NodeForce>(doc, run);
        var beamForces = Reader<Alpaca4d.Gh.BeamForce>(doc, run);

        var watch = Stopwatch.StartNew();
        doc.Solve();
        long total = watch.ElapsedMilliseconds;
        NoErrors(doc, "the definition");

        var middlePath = Out(one, "Force").VolatileData.Paths.Single();
        var sum = Sum(Vectors(Out(one, "Force"), middlePath));
        Check((sum - new Vector3d(0, 0, 100)).Length < 1e-3, $"the four beams at the middle hold up the 100 kN ({sum})");
        Check(Out(all, "N").VolatileData.DataCount == 2 * lines.Count, $"every node: both ends of all {lines.Count} beams");

        report.AppendLine($"         {lines.Count} beams, {(n + 1) * (n + 1)} nodes; the whole definition solved in {total} ms");
        report.AppendLine($"         Node Forces, one node:   {one.ProcessorTime.TotalMilliseconds:F0} ms");
        report.AppendLine($"         Node Forces, every node: {all.ProcessorTime.TotalMilliseconds:F0} ms");
        report.AppendLine($"         Beam Forces, every beam: {beamForces.ProcessorTime.TotalMilliseconds:F0} ms");
    }
}
