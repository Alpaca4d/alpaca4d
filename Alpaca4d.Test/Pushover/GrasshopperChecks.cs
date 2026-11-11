// Pushover Analysis and fibre sections on beams, through the Grasshopper components themselves, in a
// running Rhino 8.
//
// Each model is a Grasshopper document built in code out of the real components - Steel01, Patch, Fiber
// Section, the beam switcher, Support, Point Load, Gravity Load, Linear Time Series, Load Pattern,
// Assemble Model, Pushover Analysis - solved the way the canvas solves it, and read back through
// Reaction Forces, Beam Forces and Model View at the steps the curve names. The column is the one
// PushoverCheck.cs pushes by hand, now with a patch for its section, its own weight and P-Delta from the
// component. grasshopper.sh loads this build of the plugin beside the installed one and calls Run by
// reflection.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

    static bool Near(double a, double b, double tol) => Math.Abs(a - b) <= tol * Math.Max(1.0, Math.Abs(b));

    public static string Run(string work)
    {
        folder = work;
        report = new StringBuilder();
        fails = 0;
        try
        {
            Column();
            Refused();
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

    static GH_Curve Line(double x0, double y0, double z0, double x1, double y1, double z1) =>
        new GH_Curve(new LineCurve(new Point3d(x0, y0, z0), new Point3d(x1, y1, z1)));

    static GH_Point Pt(double x, double y, double z) => new GH_Point(new Point3d(x, y, z));

    static List<double> Numbers(GH_Component c, string output) =>
        Out(c, output).VolatileData.AllData(true).Select(g => g is GH_Number n ? n.Value : Convert.ToDouble(g.ScriptVariable())).ToList();

    static List<int> Integers(GH_Component c, string output) =>
        Out(c, output).VolatileData.AllData(true).Select(g => ((GH_Integer)g).Value).ToList();

    static void NoErrors(Doc doc, string what)
    {
        var errors = doc.Messages(GH_RuntimeMessageLevel.Error).ToList();
        Check(errors.Count == 0, errors.Count == 0 ? $"{what}: no component has an error" : $"{what}: {string.Join(" | ", errors)}");
    }

    // ---------------------------------------------------------------- the column

    const double Fy = 355000.0, E = 210000000.0, L = 3.0, Depth = 0.3, Width = 0.2, P = 500.0;

    /// <summary>A 300 x 200 patch of Steel01, b = 0.001, 300 along the drawing's x: the beam's local y.</summary>
    static GH_Component FibreSection(Doc doc, double x, double y, double b = 0.001)
    {
        var steel = doc.Add<Alpaca4d.Gh.Steel01>();
        Set(steel, "b", new GH_Number(b));
        var mesh = Mesh.CreateFromPlane(new Plane(new Point3d(x, y, 0), Vector3d.ZAxis),
                                        new Interval(-Depth / 2, Depth / 2), new Interval(-Width / 2, Width / 2), 12, 8);
        var patch = doc.Add<Alpaca4d.Gh.Patch>();
        Set(patch, "Mesh", new GH_Mesh(mesh));
        Wire(steel, "Material", patch, "Material");
        var section = doc.Add<Alpaca4d.Gh.FiberSection>();
        Wire(patch, "Patch", section, "Patch");
        return section;
    }

    /// <summary>
    /// The column on <paramref name="section"/>: vertical, 3 m, fixed at the bottom, 500 kN on top and its
    /// own weight, pushed along X - its local y, the 300 side - to 0.15 m.
    /// </summary>
    static (GH_Component assemble, GH_Component push) Column(Doc doc, GH_Component section, bool pDelta, bool lateral)
    {
        var column = doc.Add<Alpaca4d.Gh.BeamBase>("ForceBeamColumn (Alpaca4d)");
        Set(column, "Line", Line(0, 0, 0, 0, 0, L));
        Wire(section, "FiberSection", column, "Section");

        var support = doc.Add<Alpaca4d.Gh.Support>();
        Set(support, "Position", new GH_Plane(Plane.WorldXY));
        foreach (var dof in new[] { "Tx", "Ty", "Tz", "Rx", "Ry", "Rz" })
            Set(support, dof, new GH_Boolean(true));

        var top = doc.Add<Alpaca4d.Gh.PointLoad>();
        Set(top, "Point", Pt(0, 0, L));
        Set(top, "Force", new GH_Vector(new Vector3d(0, 0, -P)));
        var weight = doc.Add<Alpaca4d.Gh.GravityLoad>();

        var series = doc.Add<Alpaca4d.Gh.LinearTimeSeries>();
        var pattern = doc.Add<Alpaca4d.Gh.PatternBase>("PlainPattern (Alpaca4d)");
        Wire(series, "TimeSeries", pattern, "TimeSeries");
        In(pattern, "Loads").AddSource(top.Params.Output[0]);
        In(pattern, "Loads").AddSource(weight.Params.Output[0]);

        var assemble = doc.Add<Alpaca4d.Gh.AssembleModel>();
        In(assemble, "Elements").AddSource(column.Params.Output[0]);
        In(assemble, "Supports").AddSource(support.Params.Output[0]);
        In(assemble, "LoadPatterns").AddSource(pattern.Params.Output[0]);

        var push = doc.Add<Alpaca4d.Gh.PushoverAnalysis>();
        Wire(assemble, "AlpacaModel", push, "AlpacaModel");
        Set(push, "Control Point", Pt(0, 0, L));
        Set(push, "Direction", new GH_String("X"));
        Set(push, "Target", new GH_Number(0.15));
        Set(push, "Steps", new GH_Integer(60));
        Set(push, "P-Delta", new GH_Boolean(pDelta));
        if (lateral)
        {
            var load = doc.Add<Alpaca4d.Gh.PointLoad>();
            Set(load, "Point", Pt(0, 0, L));
            Set(load, "Force", new GH_Vector(new Vector3d(2, 0, 0)));
            Wire(load, "Load", push, "Lateral Loads");
        }
        return (assemble, push);
    }

    static GH_Component Reader<T>(Doc doc, GH_Component push, int step) where T : GH_Component, new()
    {
        var reader = doc.Add<T>();
        Wire(push, "AlpacaModel", reader, "AlpacaModel");
        Set(reader, "Step", new GH_Integer(step));
        return reader;
    }

    static void Column()
    {
        Section("A steel column on a 300 x 200 patch, 500 kN and its own weight held, pushed 0.15 m along X");

        var doc = new Doc("column");
        var section = FibreSection(doc, 0, 0);
        var (assemble, push) = Column(doc, section, pDelta: true, lateral: false);
        doc.Solve();
        NoErrors(doc, "the definition");

        var fibre = (Alpaca4d.Section.FiberSection)Out(section, "FiberSection").VolatileData.AllData(true).Single().ScriptVariable();
        Check(fibre.Curves.Count == 1, $"the section's outline is the patch's: {fibre.Curves.Count} curve");
        var box = fibre.Curves[0].GetBoundingBox(true);
        // Model View lays a section's x along local z and its y along local y: the 300 side comes out along y.
        Check(Near(box.Max.Y - box.Min.Y, Depth, 1e-9) && Near(box.Max.X - box.Min.X, Width, 1e-9),
              $"turned for Model View: {box.Max.X - box.Min.X:0.###} across local z, {box.Max.Y - box.Min.Y:0.###} across local y");

        var d = Numbers(push, "Displacement");
        var v = Numbers(push, "Base Shear");
        var steps = Integers(push, "Step");
        var warnings = push.RuntimeMessages(GH_RuntimeMessageLevel.Warning).ToList();
        var remarks = push.RuntimeMessages(GH_RuntimeMessageLevel.Remark).ToList();
        Check(warnings.Count == 0, warnings.Count == 0 ? "Pushover Analysis has nothing to warn about" : "warnings: " + string.Join(" | ", warnings));
        Check(remarks.Any(r => r.StartsWith("Peak base shear")), "its remark gives the peak: " + remarks.FirstOrDefault(r => r.StartsWith("Peak")));
        Check(d.Count == 61 && Near(d.Last(), 0.15, 1e-9) && d[0] == 0.0, $"61 points from 0 to 0.15 m ({d.Count}, {d.LastOrDefault():G6})");
        Check(steps.Count == 61 && steps[0] == 9 && steps.Last() == 69, $"steps 9 to 69: the model's own loads in 10 ({steps.FirstOrDefault()}..{steps.LastOrDefault()})");

        double mp = Fy * Width * Depth * Depth / 4.0;
        // The fibres sit at the middle of 12 strips through the depth, each without its own b t³ / 12.
        double ei = E * Width * Depth * Depth * Depth / 12.0 * (1.0 - 1.0 / 144.0);
        double k = 3 * ei / (L * L * L) - P / L;
        Check(Near(v[1] / d[1], k, 0.003), $"the first slope is 3 E I / L³ less P / L: {v[1] / d[1]:G6} against {k:G6}");
        double end = (mp - P * 0.15) / L;
        Check(v.Last() >= 0.995 * end && v.Last() <= 1.03 * end,
              $"at 0.15 m the base shear is (Mp - P Δ) / L and a little hardening: {v.Last():G6} against {end:G6} kN");
        int peak = v.IndexOf(v.Max());
        Check(peak < v.Count - 1 && v.Max() > v.Last(), $"and its peak came before, P-Delta taking it down: {v.Max():G6} kN at {d[peak]:G4} m");

        // The model's own loads, at the first point: 500 kN and the column's weight, steel at 7 850 kg/m3.
        var before = Reader<Alpaca4d.Gh.ReactionForce>(doc, push, steps[0]);
        var after = Reader<Alpaca4d.Gh.ReactionForce>(doc, push, steps.Last());
        var forces = Reader<Alpaca4d.Gh.BeamForce>(doc, push, steps.Last());
        var view = Reader<Alpaca4d.Gh.ModelView>(doc, push, steps.Last());
        doc.Solve();
        NoErrors(doc, "the readers");

        double weight = 7850.0 * Width * Depth * 9.81 / 1000.0 * L;
        var r0 = ((GH_Vector)Out(before, "ReactionForce").VolatileData.AllData(true).Single()).Value;
        Check(Near(r0.Z, P + weight, 1e-6), $"Reaction Forces at the first point: 500 kN and the column's weight, {weight:0.###} kN ({r0.Z:G8})");
        Check(Math.Abs(r0.X) < 1e-6, $"and nothing sideways yet ({r0.X:G3})");
        var r1 = ((GH_Vector)Out(after, "ReactionForce").VolatileData.AllData(true).Single()).Value;
        Check(Near(-r1.X, v.Last(), 1e-6), $"at the last point the reaction is the base shear ({-r1.X:G8} against {v.Last():G8})");

        var mz = Numbers(forces, "Mz");
        double baseMoment = v.Last() * L + P * 0.15;
        Check(mz.Count > 0 && Near(Math.Abs(mz[0]), baseMoment, 0.01),
              $"Beam Forces: the base moment is V L + P Δ, within 1% for the column's weight ({(mz.Count > 0 ? mz[0] : double.NaN):G6} against {baseMoment:G6})");

        var geometry = Out(view, "Geometry").VolatileData.AllData(true).Count();
        Check(geometry > 0, $"Model View draws the last point ({geometry} pieces)");

        // The first plastic hinge: the column's foot, when its outermost fibre reaches fy / E under the
        // axial force held there, (fy - N / A) W_el = M at the base. A cantilever's base moment is
        // 3 E I Δ / L² with P-Delta as without, so Δ = M L² / (3 E I).
        var hinges = Out(push, "Hinges").VolatileData.AllData(true).Select(g => g.ToString()).ToList();
        var yieldsAt = Integers(push, "Yields At");
        var hingeAt = Out(push, "Hinge Points").VolatileData.AllData(true).Select(g => ((GH_Point)g).Value).ToList();
        double n = P + weight;
        double firstYield = (Fy - n / (Width * Depth)) * Width * Depth * Depth / 6.0 * L * L / (3.0 * ei);
        int point = (int)Math.Ceiling(firstYield / 0.0025 - 1e-9);
        Check(hinges.Count > 0 && hingeAt[0].DistanceTo(Point3d.Origin) < 1e-9 && hinges[0].Contains("I end"),
              $"the first hinge is the column's foot: {(hinges.Count > 0 ? hinges[0] : "none")}");
        Check(yieldsAt.Count > 0 && yieldsAt[0] == point,
              $"it yields at point {(yieldsAt.Count > 0 ? yieldsAt[0] : -1)}, the first past Δ = {firstYield * 1000:0.0} mm: point {point}");
        Check(yieldsAt.SequenceEqual(yieldsAt.OrderBy(x => x)), "the hinges come in the order they form");
        Check(hinges.All(h => h.Contains("ductility ") && h.Contains(" by the end")), "each names the ductility it reaches by the end");

        // The ductility each has reached at each point of the curve: a branch a hinge, as long as the curve,
        // 0 before it yields, at least 1 from the point it does, and never coming down.
        var tree = Out(push, "Ductility").VolatileData;
        bool shaped = tree.PathCount == hinges.Count && tree.Paths.All(path => tree.get_Branch(path).Count == d.Count);
        Check(shaped, $"Ductility: a branch a hinge ({tree.PathCount} against {hinges.Count}), a value a point of the curve ({d.Count})");
        var ductility = tree.Paths.Select(path => tree.get_Branch(path).Cast<GH_Number>().Select(x => x.Value).ToList()).ToList();
        bool zeroBefore = true, oneFrom = true, neverDown = true;
        for (int h = 0; h < ductility.Count && h < yieldsAt.Count; h++)
        {
            var series = ductility[h];
            for (int j = 0; j < series.Count; j++)
            {
                if (j < yieldsAt[h] && series[j] != 0.0) zeroBefore = false;
                if (j >= yieldsAt[h] && series[j] < 1.0) oneFrom = false;
                if (j > 0 && series[j] < series[j - 1]) neverDown = false;
            }
        }
        Check(zeroBefore && oneFrom && neverDown, $"0 before each yields ({zeroBefore}), at least 1 from then ({oneFrom}), never coming down ({neverDown})");
        double footDuctility = ductility.Count > 0 ? ductility[0].Last() : double.NaN;
        Check(footDuctility > 1.0, $"the foot reaches {footDuctility:0.###} times its yield strain by 0.15 m");

        // Model View's own hinges: none yielded the point before, the foot at that point.
        var hingeView = Reader<Alpaca4d.Gh.ModelView>(doc, push, steps[point - 1]);
        var tick = typeof(Alpaca4d.Gh.ModelView).GetField("_ckHinges", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
        var hingeBox = tick.GetValue(hingeView);
        hingeBox.GetType().GetProperty("Active").SetValue(hingeBox, true);
        hingeView.ExpireSolution(false);
        doc.Solve();
        var field = typeof(Alpaca4d.Gh.ModelView).GetField("_hinges", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        List<Alpaca4d.Result.HingeStation> Stations() =>
            ((System.Collections.IEnumerable)field.GetValue(hingeView) ?? new object[0]).Cast<Alpaca4d.Result.HingeStation>().ToList();
        // A ball for every station that has yielded by the step drawn.
        int Yielded() => Stations().Count(h => h.Reached != Alpaca4d.Result.HingeState.Elastic);
        int beforeYield = Yielded();
        Set(hingeView, "Step", new GH_Integer(steps[point]));
        doc.Solve();
        int atYield = Yielded();
        Check(beforeYield == 0 && atYield >= 1, $"Model View, Plastic hinges: none the point before, {atYield} at it");

        // At the last point, a ball for every hinge Pushover Analysis lists - they stay - and the foot's coloured by the
        // ductility it lists for it.
        Set(hingeView, "Step", new GH_Integer(steps.Last()));
        doc.Solve();
        int atEnd = Yielded();
        Check(atEnd == hinges.Count, $"at the last point a ball for each of the {hinges.Count} hinges: {atEnd}");
        var foot = Stations().Where(h => h.Reached != Alpaca4d.Result.HingeState.Elastic).OrderByDescending(h => h.Ductility).FirstOrDefault();
        var footStation = Stations().FirstOrDefault(h => h.Position.DistanceTo(Point3d.Origin) < 1e-9 && h.Along <= 1e-9);
        double drawn = footStation?.Ductility ?? double.NaN;
        Check(Near(drawn, footDuctility, 1e-9), $"the foot's ductility drawn is the one listed: {drawn:G8} against {footDuctility:G8}");
        Check(foot != null && Near(foot.Ductility, footDuctility, 1e-9), "and it is the largest, as a cantilever's foot is");
        Check(!hingeView.RuntimeMessages(GH_RuntimeMessageLevel.Warning).Any() && !hingeView.RuntimeMessages(GH_RuntimeMessageLevel.Remark).Any(m => m.Contains("Plastic")),
              "and it has nothing to say about them");

        Section("The same, drawn away from the origin, with a lateral load given, and without P-Delta");
        var other = new Doc("column-moved");
        var moved = FibreSection(other, 7.0, -4.0);
        var (_, pushMoved) = Column(other, moved, pDelta: true, lateral: true);
        var flat = FibreSection(other, 0, 0);
        var (_, pushFlat) = Column(other, flat, pDelta: false, lateral: false);
        other.Solve();
        NoErrors(other, "the definitions");

        var vMoved = Numbers(pushMoved, "Base Shear");
        Check(vMoved.Count == v.Count && vMoved.Zip(v, (a, b) => Near(a, b, 1e-6)).All(x => x),
              "a section drawn at (7, -4) and a 2 kN load at the top give the same curve: the beam runs through the centroid");
        var factor = Numbers(pushMoved, "Load Factor");
        Check(Near(factor.Last() * 2.0, vMoved.Last(), 1e-6), $"the load factor is on the 2 kN load: {factor.Last():G6} x 2 = {vMoved.Last():G6}");

        var vFlat = Numbers(pushFlat, "Base Shear");
        double gap = vFlat.Last() - v.Last();
        Check(Near(gap, P * 0.15 / L, 0.06), $"without P-Delta the base shear is P Δ / L = 25 kN higher at 0.15 m ({gap:G4})");
        Check(pushFlat.RuntimeMessages(GH_RuntimeMessageLevel.Remark).Any(r => r.Contains("P-Delta is off")), "and it says P-Delta is off");
    }

    static void Refused()
    {
        Section("What a fibre section is refused by, and what Pushover Analysis warns of");
        var doc = new Doc("refused");
        var section = FibreSection(doc, 0, 0, b: 0.0);

        var hinged = doc.Add<Alpaca4d.Gh.BeamBase>("WithHinges (Alpaca4d)");
        Set(hinged, "Line", Line(0, 0, 0, 3, 0, 0));
        Wire(section, "FiberSection", hinged, "Section");
        var truss = doc.Add<Alpaca4d.Gh.BeamBase>("Truss (Alpaca4d)");
        Set(truss, "Line", Line(0, 0, 0, 3, 0, 0));
        Wire(section, "FiberSection", truss, "Section");

        var (_, push) = Column(doc, section, pDelta: true, lateral: false);
        Set(push, "Steps", new GH_Integer(20));
        doc.Solve();

        Check(hinged.RuntimeMessages(GH_RuntimeMessageLevel.Error).Any(m => m.Contains("fibre section")), "WithHinges refuses it: " + hinged.RuntimeMessages(GH_RuntimeMessageLevel.Error).FirstOrDefault());
        Check(truss.RuntimeMessages(GH_RuntimeMessageLevel.Error).Any(m => m.Contains("fibre section")), "a Truss refuses it: " + truss.RuntimeMessages(GH_RuntimeMessageLevel.Error).FirstOrDefault());
        Check(push.RuntimeMessages(GH_RuntimeMessageLevel.Warning).Any(m => m.Contains("b = 0")), "Pushover Analysis warns of Steel01 with b = 0");
    }
}
