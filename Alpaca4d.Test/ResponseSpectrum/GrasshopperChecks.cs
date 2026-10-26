// Design Spectrum, Mass from Loads and Response Spectrum Analysis through the Grasshopper components
// themselves, in a running Rhino 8.
//
// Each definition is built in code out of the real components and solved the way the canvas solves it.
// Design Spectrum is checked unit by unit. A frame - four columns, four beams, a slab on top - is loaded
// with a linear, a mesh and a point load, and Mass from Loads makes their weight into mass, which
// Assemble Model has to count and add up node by node. The frame is then shaken along X and Y, and
// Nodal Displacements, Reaction Forces and Beam Forces read the result cases back. grasshopper.sh loads
// this build of the plugin beside the installed one and calls Run by reflection.
using System;
using System.Collections.Generic;
using System.Globalization;
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

    public static string Run(string work)
    {
        folder = work;
        report = new StringBuilder();
        fails = 0;
        try
        {
            Spectra();
            FrameShaken();
            FrameWarnings();
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
            // A folder each: the analysis components name their files by run, not by component, so two in one
            // folder would write over each other's results.
            Directory.CreateDirectory(Path.Combine(folder, name));
            Document.FilePath = Path.Combine(folder, name, name + ".gh");
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

        public IEnumerable<string> Messages(GH_RuntimeMessageLevel level, params GH_Component[] except) =>
            components.Where(c => !except.Contains(c)).SelectMany(c => c.RuntimeMessages(level).Select(m => $"{c.NickName}: {m}"));
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

    static void SetAny(GH_Component c, string input, params IGH_Goo[] values)
    {
        var param = In(c, input);
        var data = (GH_Structure<IGH_Goo>)param.GetType().GetProperty("PersistentData").GetValue(param);
        data.Clear();
        foreach (var value in values)
            data.Append(value);
    }

    static void Wire(GH_Component from, string output, GH_Component to, string input) =>
        In(to, input).AddSource(Out(from, output));

    static GH_Curve Line(double x0, double y0, double z0, double x1, double y1, double z1) =>
        new GH_Curve(new LineCurve(new Point3d(x0, y0, z0), new Point3d(x1, y1, z1)));

    static GH_Point Pt(double x, double y, double z) => new GH_Point(new Point3d(x, y, z));

    static List<double> Numbers(IGH_Param p) =>
        p.VolatileData.AllData(true).Select(g => g is GH_Number n ? n.Value : Convert.ToDouble(g.ScriptVariable())).ToList();

    static List<string> Texts(IGH_Param p) =>
        p.VolatileData.AllData(true).Select(g => g.ToString()).ToList();

    static List<Vector3d> Vectors(IGH_Param p) =>
        p.VolatileData.AllData(true).Select(g => ((GH_Vector)g).Value).ToList();

    static object Value(IGH_Param p) =>
        p.VolatileData.AllData(true).Select(g => g.ScriptVariable()).FirstOrDefault();

    static void NoErrors(Doc doc, string what, params GH_Component[] except)
    {
        var errors = doc.Messages(GH_RuntimeMessageLevel.Error, except).ToList();
        Check(errors.Count == 0, errors.Count == 0 ? $"{what}: no component has an error" : $"{what}: {string.Join(" | ", errors)}");
    }

    static bool Says(GH_Component c, GH_RuntimeMessageLevel level, string text) =>
        c.RuntimeMessages(level).Any(m => m.Contains(text));

    // ---------------------------------------------------------------- Design Spectrum

    static void Spectra()
    {
        Section("Design Spectrum, unit by unit");
        var doc = new Doc("spectra");

        var generic = doc.Add<Alpaca4d.Gh.DesignSpectrum>();
        Set(generic, "agR", new GH_Number(0.25));

        var ntc = doc.Add<Alpaca4d.Gh.DesignSpectrum>("Design Spectrum NTC 2018 (Alpaca4d)");
        Set(ntc, "ag", new GH_Number(0.18));
        Set(ntc, "Fo", new GH_Number(2.45));
        Set(ntc, "Tc*", new GH_Number(0.30));
        Set(ntc, "Soil", new GH_String("C"));

        var custom = doc.Add<Alpaca4d.Gh.DesignSpectrum>("Design Spectrum Custom (Alpaca4d)");
        Set(custom, "Period", new GH_Number(0.1), new GH_Number(0.5), new GH_Number(2.0));
        Set(custom, "Acceleration", new GH_Number(0.4), new GH_Number(0.8), new GH_Number(0.2));

        var wrong = doc.Add<Alpaca4d.Gh.DesignSpectrum>();
        Set(wrong, "agR", new GH_Number(0.25));
        Set(wrong, "Ground", new GH_String("S1"));

        doc.Solve();

        Check(generic.Message.StartsWith("Generic"), $"a new Design Spectrum is the generic one: {generic.Message.Replace("\n", " ")}");
        Check(Value(Out(generic, "Spectrum")) is Alpaca4d.Seismic.Ec8Spectrum, "and makes an EN 1998-1 spectrum");
        Check(Near(Numbers(Out(generic, "Acceleration")).Max(), 0.75, 1e-12), $"its plateau, ground B by default: 2.5 x 0.25 x 1.2 = 0.75 g ({Numbers(Out(generic, "Acceleration")).Max()})");
        Check(Texts(Out(generic, "Parameters")).Contains("S = 1.2"), "Parameters gives S = 1.2");
        var curve = Value(Out(generic, "Curve")) as Curve;
        Check(curve != null && Near(curve.PointAtEnd.X, 4.0, 1e-12), "Curve is drawn out to 4 s");

        Check(ntc.Message.StartsWith("NTC 2018"), $"the NTC unit says so: {ntc.Message.Replace("\n", " ")}");
        Check(Near(Numbers(Out(ntc, "Acceleration")).Max(), 0.6330114, 1e-9), $"NTC 2018, soil C: plateau ag S η Fo = 0.6330 g ({Numbers(Out(ntc, "Acceleration")).Max():0.######})");
        Check(Texts(Out(ntc, "Parameters")).Contains("SS = 1.4354"), "Parameters gives SS = 1.4354");

        Check(Near(Numbers(Out(custom, "Acceleration")).Max(), 0.8, 1e-12), "Custom: its points, straight between them");
        Check(Says(wrong, GH_RuntimeMessageLevel.Error, "A, B, C, D, E"), "ground S1 is an error that names A to E");
        Check(!Out(wrong, "Spectrum").VolatileData.AllData(true).Any(), "and gives no spectrum");
    }

    // ---------------------------------------------------------------- a frame

    const double W = 6.0, D = 4.0, H = 3.0;

    /// <summary>What a frame definition hands on: its model assembled with and without the masses of its loads, and its spectra.</summary>
    sealed class Frame
    {
        public GH_Component Bare, Massed, Permanent, Variable, Generic, Ntc;
    }

    /// <summary>
    /// Four columns, four beams named "beam" and a slab, fixed at the foot; 10 kN/m on the beams, 2 kN/m² on the
    /// slab and 20 kN at one corner, in a load pattern. Mass from Loads makes the beams' and the slab's load mass
    /// in full and the point load's at ψ2 = 0.3, given through a pattern of its own.
    /// </summary>
    static Frame BuildFrame(Doc doc)
    {
        var material = doc.Add<Alpaca4d.Gh.Uniaxial>("UniaxialElastic (Alpaca4d)");
        var section = doc.Add<Alpaca4d.Gh.RectangleCS>();
        Set(section, "Width", new GH_Number(0.3));
        Set(section, "Height", new GH_Number(0.3));
        Wire(material, "Material", section, "Material");

        var columns = doc.Add<Alpaca4d.Gh.BeamBase>("ForceBeamColumn (Alpaca4d)");
        Set(columns, "Line", Line(0, 0, 0, 0, 0, H), Line(W, 0, 0, W, 0, H), Line(W, D, 0, W, D, H), Line(0, D, 0, 0, D, H));
        Wire(section, "Section", columns, "Section");

        var beams = doc.Add<Alpaca4d.Gh.BeamBase>("ForceBeamColumn (Alpaca4d)");
        Set(beams, "Line", Line(0, 0, H, W, 0, H), Line(W, 0, H, W, D, H), Line(W, D, H, 0, D, H), Line(0, D, H, 0, 0, H));
        Set(beams, "ElementId", new GH_String("beam"));
        Wire(section, "Section", beams, "Section");

        var nd = doc.Add<SimplexGh.nD>("ElasticIsotropic (Alpaca4d)");
        var plate = doc.Add<Alpaca4d.Gh.PlateFiberSection>();
        Set(plate, "Thickness", new GH_Number(0.2));
        Wire(nd, "Material", plate, "Material");
        var slab = doc.Add<Alpaca4d.Gh.ASDShell>();
        var mesh = new Mesh();
        mesh.Vertices.Add(0, 0, H); mesh.Vertices.Add(W, 0, H); mesh.Vertices.Add(W, D, H); mesh.Vertices.Add(0, D, H);
        mesh.Faces.AddFace(0, 1, 2, 3);
        Set(slab, "Mesh", new GH_Mesh(mesh));
        Wire(plate, "Section", slab, "Section");

        var supports = new[] { Pt(0, 0, 0), Pt(W, 0, 0), Pt(W, D, 0), Pt(0, D, 0) }.Select(at =>
        {
            var support = doc.Add<Alpaca4d.Gh.Support>();
            Set(support, "Position", new GH_Plane(new Plane(at.Value, Vector3d.XAxis, Vector3d.YAxis)));
            foreach (var name in new[] { "Tx", "Ty", "Tz", "Rx", "Ry", "Rz" })
                Set(support, name, new GH_Boolean(true));
            return support;
        }).ToList();

        var line = doc.Add<Alpaca4d.Gh.LinearLoad>();
        SetAny(line, "Beam|Id", new GH_String("beam"));
        Set(line, "Force", new GH_Vector(new Vector3d(0, 0, -10)));
        var area = doc.Add<Alpaca4d.Gh.MeshLoad>();
        Set(area, "Force", new GH_Vector(new Vector3d(0, 0, -2)));
        var point = doc.Add<Alpaca4d.Gh.PointLoad>();
        // One at every corner, so the mass stays symmetric and the twist keeps out of the sway modes.
        Set(point, "Point", Pt(0, 0, H), Pt(W, 0, H), Pt(W, D, H), Pt(0, D, H));
        Set(point, "Force", new GH_Vector(new Vector3d(0, 0, -20)));

        var pattern = doc.Add<Alpaca4d.Gh.PatternBase>("PlainPattern (Alpaca4d)");
        foreach (var load in new GH_Component[] { line, area, point })
            In(pattern, "Loads").AddSource(load.Params.Output[0]);

        var frame = new Frame();
        frame.Permanent = doc.Add<Alpaca4d.Gh.MassFromLoadsComponent>();
        In(frame.Permanent, "Loads").AddSource(line.Params.Output[0]);
        In(frame.Permanent, "Loads").AddSource(area.Params.Output[0]);
        var variablePattern = doc.Add<Alpaca4d.Gh.PatternBase>("PlainPattern (Alpaca4d)");
        In(variablePattern, "Loads").AddSource(point.Params.Output[0]);
        frame.Variable = doc.Add<Alpaca4d.Gh.MassFromLoadsComponent>();
        In(frame.Variable, "Loads").AddSource(variablePattern.Params.Output[0]);
        Set(frame.Variable, "Factor", new GH_Number(0.3));

        GH_Component Assemble(bool withMass)
        {
            var assemble = doc.Add<Alpaca4d.Gh.AssembleModel>();
            // ASD Shell hands its shells on a branch of their own; flattened, as on the canvas.
            In(assemble, "Elements").DataMapping = GH_DataMapping.Flatten;
            foreach (var element in new GH_Component[] { columns, beams, slab })
                In(assemble, "Elements").AddSource(element.Params.Output[0]);
            foreach (var support in supports)
                In(assemble, "Supports").AddSource(support.Params.Output[0]);
            In(assemble, "LoadPatterns").AddSource(pattern.Params.Output[0]);
            if (withMass)
            {
                In(assemble, "LoadPatterns").AddSource(frame.Permanent.Params.Output[0]);
                In(assemble, "LoadPatterns").AddSource(frame.Variable.Params.Output[0]);
            }
            return assemble;
        }
        frame.Bare = Assemble(false);
        frame.Massed = Assemble(true);

        frame.Generic = doc.Add<Alpaca4d.Gh.DesignSpectrum>();
        Set(frame.Generic, "agR", new GH_Number(0.25));
        Set(frame.Generic, "q", new GH_Number(1.5));
        frame.Ntc = doc.Add<Alpaca4d.Gh.DesignSpectrum>("Design Spectrum NTC 2018 (Alpaca4d)");
        Set(frame.Ntc, "ag", new GH_Number(0.18));
        Set(frame.Ntc, "Fo", new GH_Number(2.45));
        Set(frame.Ntc, "Tc*", new GH_Number(0.30));
        return frame;
    }

    static GH_Component Spectrum(Doc doc, Frame frame, int modes, string x = null, string y = null, string z = null, string solver = null)
    {
        var rsa = doc.Add<Alpaca4d.Gh.ResponseSpectrumAnalysis>();
        Wire(frame.Massed, "AlpacaModel", rsa, "AlpacaModel");
        foreach (var (axis, which) in new[] { ("X", x), ("Y", y), ("Z", z) })
            if (which != null)
                Wire(which == "ntc" ? frame.Ntc : frame.Generic, "Spectrum", rsa, "Spectrum" + axis);
        Set(rsa, "Vibration Modes", new GH_Integer(modes));
        if (solver != null)
            Set(rsa, "Solver", new GH_String(solver));
        return rsa;
    }

    static void FrameShaken()
    {
        Section("A frame with a slab: Mass from Loads, then Response Spectrum Analysis");
        var doc = new Doc("frame");
        var frame = BuildFrame(doc);

        // Four modes: X, Y, the twist and one more. ARPACK gives at most six of a frame with its mass on four nodes.
        var rsa = Spectrum(doc, frame, 4, x: "generic", y: "ntc");

        GH_Component Reader<T>(int step, bool history = false) where T : GH_Component, new()
        {
            var reader = doc.Add<T>();
            Wire(rsa, "AlpacaModel", reader, "AlpacaModel");
            Set(reader, "Step", new GH_Integer(step));
            if (history)
                Set(reader, "History", new GH_Boolean(true));
            return reader;
        }
        var reactionsE = Reader<Alpaca4d.Gh.ReactionForce>(0);
        var reactionsX = Reader<Alpaca4d.Gh.ReactionForce>(1);
        var reactionsY = Reader<Alpaca4d.Gh.ReactionForce>(2);
        var displacements = Reader<Alpaca4d.Gh.NodalDisplacement>(1);
        var history = Reader<Alpaca4d.Gh.NodalDisplacement>(0, history: true);
        var forces = Reader<Alpaca4d.Gh.BeamForce>(1);
        var tooFar = Reader<Alpaca4d.Gh.NodalDisplacement>(3);

        // The gravity loads solved statically beside it, in the same document, off the same model: each
        // analysis keeps its own files, so neither reads the other's results.
        var settings = doc.Add<Alpaca4d.Gh.AnalysisSettings>();
        var run = doc.Add<Alpaca4d.Gh.RunAnalysis>();
        Wire(frame.Massed, "AlpacaModel", run, "AlpacaModel");
        Wire(settings, "Settings", run, "Settings");
        var staticReactions = doc.Add<Alpaca4d.Gh.ReactionForce>();
        Wire(run, "AlpacaModel", staticReactions, "AlpacaModel");

        doc.Solve();

        // ------------------------------------------------ the mass
        double g = 9.81;
        double fromLoads = ((10.0 * (2 * W + 2 * D) + 2.0 * W * D) + 0.3 * 4 * 20.0) / g * 1000.0;
        double bareMass = Numbers(Out(frame.Bare, "Mass")).Single();
        double massedMass = Numbers(Out(frame.Massed, "Mass")).Single();
        Check(Near(massedMass - bareMass, fromLoads, 1e-9),
              $"Assemble's Mass grows by the loads' weight over g: 200 kN of beams, 48 of slab and 0.3 x 4 x 20 kN ({massedMass - bareMass:0.##} kg, want {fromLoads:0.##})");
        Check(!frame.Permanent.RuntimeMessages(GH_RuntimeMessageLevel.Warning).Any() && !frame.Variable.RuntimeMessages(GH_RuntimeMessageLevel.Warning).Any(),
              "Mass from Loads has nothing to warn about");

        var model = (Alpaca4d.Model)Value(Out(frame.Massed, "AlpacaModel"));
        var massLines = model.Tcl.Where(l => l.StartsWith("mass ")).Select(l => l.Trim().Split(' ')).ToList();
        Check(massLines.Count == 4, $"one mass line per node: the four top corners ({massLines.Count})");
        var corner = model.Nodes.Single(n => n.Pos.DistanceTo(new Point3d(0, 0, H)) < 1e-9);
        var cornerLine = massLines.SingleOrDefault(tokens => tokens[1] == corner.Id.ToString());
        double cornerWant = (10.0 * (W + D) / 2.0 + 2.0 * W * D / 4.0 + 0.3 * 20.0) / g;
        Check(cornerLine != null && Near(double.Parse(cornerLine[2], CultureInfo.InvariantCulture), cornerWant, 1e-9),
              $"a corner: half of two beams, a quarter of the slab and 0.3 x 20 kN, added up ({cornerLine?[2]} t, want {cornerWant:0.######})");
        Check(cornerLine != null && cornerLine[2] == cornerLine[3] && cornerLine[3] == cornerLine[4], "the same along X, Y and Z");

        // ------------------------------------------------ the analysis
        NoErrors(doc, "Design Spectrum, Mass from Loads, Assemble, Response Spectrum and the readers, bar the one meant to fail", tooFar);
        Check(string.Join(" ", Texts(Out(rsa, "Cases"))) == "E Ex Ey", $"Cases: {string.Join(" ", Texts(Out(rsa, "Cases")))}");
        var periods = Numbers(Out(rsa, "Period"));
        Check(periods.Count == 4 && periods.Zip(periods.Skip(1), (a, b) => a >= b).All(x => x) && periods.Last() > 0.0,
              $"four periods, longest first ({string.Join(", ", periods.Select(t => t.ToString("0.####")))} s)");
        var ratios = Numbers(Out(rsa, "MassRatio"));
        Check(ratios.Count == 3 && ratios[0] > 90.0 && ratios[1] > 90.0, $"the modes move most of the mass along X and Y ({string.Join(", ", ratios.Select(r => r.ToString("0.#")))} %)");
        Check(!rsa.RuntimeMessages(GH_RuntimeMessageLevel.Warning).Any(), "and Response Spectrum Analysis has nothing to warn about " +
              string.Join(" | ", rsa.RuntimeMessages(GH_RuntimeMessageLevel.Warning)));
        Check(Says(rsa, GH_RuntimeMessageLevel.Remark, "0 is E, 1 is Ex, 2 is Ey"), "its remark says which step is which");

        // Symmetric frame: under Ex the four columns sway alike, so their peaks add up to the base shear.
        var shear = Numbers(Out(rsa, "BaseShear"));
        var rx = Vectors(Out(reactionsX, "ReactionForce"));
        var ry = Vectors(Out(reactionsY, "ReactionForce"));
        Check(shear.Count == 2 && shear.All(v => v > 0.0), $"BaseShear, one per direction: {string.Join(", ", shear.Select(v => v.ToString("0.##")))} kN");
        if (shear.Count == 2 && rx.Count == 4 && ry.Count == 4)
        {
            Check(Near(rx.Sum(v => v.X), shear[0], 1e-4), $"Ex: the four supports' peaks add up to the base shear along X ({rx.Sum(v => v.X):0.###} kN against {shear[0]:0.###})");
            Check(Near(ry.Sum(v => v.Y), shear[1], 1e-4), $"Ey: and along Y ({ry.Sum(v => v.Y):0.###} kN against {shear[1]:0.###})");

            var re = Vectors(Out(reactionsE, "ReactionForce"));
            bool rule = re.Count == 4 && Enumerable.Range(0, 4).All(i =>
                Near(re[i].X, Math.Max(rx[i].X + 0.3 * ry[i].X, 0.3 * rx[i].X + ry[i].X), 1e-9) &&
                Near(re[i].Y, Math.Max(rx[i].Y + 0.3 * ry[i].Y, 0.3 * rx[i].Y + ry[i].Y), 1e-9));
            Check(rule, "E: every support's reaction is the 30% rule over Ex and Ey");
            Check(rx.All(v => v.X >= 0.0 && v.Y >= 0.0 && v.Z >= 0.0), "every value is a peak, zero or more");
        }
        else
            Check(false, $"two base shears and four reactions each ({shear.Count}, {rx.Count}, {ry.Count})");

        var top = Vectors(Out(displacements, "Displacement"));
        Check(top.Count > 0 && top.Max(v => v.X) > 0.0, $"Nodal Displacements at Ex: the top sways {top.DefaultIfEmpty().Max(v => v.X) * 1000:0.##} mm along X");
        Check(Out(history, "Displacement").VolatileData.PathCount == 3, $"History reads the three cases ({Out(history, "Displacement").VolatileData.PathCount} branches)");
        var moments = Numbers(Out(forces, "My")).Concat(Numbers(Out(forces, "Mz"))).ToList();
        Check(moments.Count > 0 && moments.Max() > 0.0 && moments.All(m => m >= 0.0), $"Beam Forces at Ex: moments, peaks without sign (up to {moments.DefaultIfEmpty(0).Max():0.##} kNm)");
        Check(Says(tooFar, GH_RuntimeMessageLevel.Error, "0 E, 1 Ex, 2 Ey"), "Step 3 is an error that lists the steps there are");

        // ------------------------------------------------ a static analysis beside it
        var staticModel = (Alpaca4d.Model)Value(Out(run, "AlpacaModel"));
        var spectrumModel = (Alpaca4d.Model)Value(Out(rsa, "AlpacaModel"));
        Check(staticModel != null && spectrumModel != null &&
              staticModel.Recorders.First().FileName != spectrumModel.Recorders.First().FileName,
              $"Run Analysis and Response Spectrum Analysis write different files ({Path.GetFileName(staticModel?.Recorders.First().FileName)}, " +
              $"{Path.GetFileName(spectrumModel?.Recorders.First().FileName)})");
        var gravity = Vectors(Out(staticReactions, "ReactionForce"));
        double weight = 10.0 * (2 * W + 2 * D) + 2.0 * W * D + 4 * 20.0;
        Check(gravity.Count == 4 && Near(gravity.Sum(v => v.Z), weight, 1e-6),
              $"the static run's supports carry the loads, 200 + 48 + 80 kN ({gravity.Sum(v => v.Z):0.###} kN)");
        // Read again once both have solved: had the static run written over the spectrum's file, these would
        // be the static reactions, with no shear along X at all.
        reactionsX.ExpireSolution(false);
        doc.Document.NewSolution(false, GH_SolutionMode.Silent);
        var again = Vectors(Out(reactionsX, "ReactionForce"));
        Check(again.Count == 4 && Near(again.Sum(v => v.X), shear[0], 1e-4),
              "and the spectrum's readers still read the spectrum: the peaks along X still add up to its base shear");
    }

    static void FrameWarnings()
    {
        Section("The same frame, asked the wrong things");
        var doc = new Doc("warnings");
        var frame = BuildFrame(doc);

        var one = Spectrum(doc, frame, 1, x: "ntc", z: "ntc");
        var tooMany = Spectrum(doc, frame, 12, x: "generic");
        var lapack = Spectrum(doc, frame, 12, x: "generic", solver: "-fullGenLapack");

        // A model Run Analysis has been through is no model to shake.
        var settings = doc.Add<Alpaca4d.Gh.AnalysisSettings>();
        var run = doc.Add<Alpaca4d.Gh.RunAnalysis>();
        Wire(frame.Massed, "AlpacaModel", run, "AlpacaModel");
        Wire(settings, "Settings", run, "Settings");
        var afterRun = doc.Add<Alpaca4d.Gh.ResponseSpectrumAnalysis>();
        Wire(run, "AlpacaModel", afterRun, "AlpacaModel");
        Wire(frame.Generic, "Spectrum", afterRun, "SpectrumX");

        doc.Solve();

        var onePeriod = Numbers(Out(one, "Period"));
        Check(onePeriod.Count == 1 && onePeriod[0] > 0.0 && !double.IsInfinity(onePeriod[0]),
              $"one mode on ARPACK: a real period, not the zero eigenvalue ARPACK gives when asked for one ({onePeriod.FirstOrDefault():0.####} s)");
        Check(Says(one, GH_RuntimeMessageLevel.Warning, "NTC 2018 asks for 85%"), "and a warning that NTC 2018 asks for 85% of the mass");
        Check(Says(one, GH_RuntimeMessageLevel.Warning, "SpectrumZ is a horizontal spectrum"), "and that SpectrumZ was given a horizontal spectrum");
        Check(Says(tooMany, GH_RuntimeMessageLevel.Error, "-fullGenLapack"), "twelve modes on ARPACK, of a frame with twelve masses' worth: an error that says to ask for fewer or use -fullGenLapack");
        Check(Numbers(Out(lapack, "Period")).Count == 12 && !lapack.RuntimeMessages(GH_RuntimeMessageLevel.Error).Any(), "and -fullGenLapack finds all twelve");
        Check(Says(afterRun, GH_RuntimeMessageLevel.Error, "already been analysed"), "fed from Run Analysis: an error that says to take Assemble's model");
    }
}
