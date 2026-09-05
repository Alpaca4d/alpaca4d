// Shell stresses through the thickness, and the layered section that makes them interesting.
//
// Two things are being guarded, and both come from the same confusion:
//
//   1. OpenSees calls two different things "stress". The element level "stresses" response and
//      "section.force" both return getStressResultant() - forces and moments per unit width, the
//      whole thickness collapsed into eight numbers. Only "section.fiber.stress" is stress in
//      force over area. Reading the wrong one gives numbers that look plausible and are out by a
//      factor of the thickness. The check below reads a real recorder file and asserts the first
//      two are bit-identical, which is what makes the third necessary.
//
//   2. The stations through the thickness are not the same for the two shell sections. A
//      PlateFiber section integrates over five Lobatto points, whose first and last land exactly
//      on the faces. A LayeredShell section reports its layer centres, which do not. Top and
//      Bottom mean "outermost station reported", and only the first of those is the face.
//
// The recorder files are produced by running OpenSees on the two decks next to this file, so the
// numbers are the solver's own rather than something restated here. Without OpenSees on PATH the
// reader checks are skipped and only the section arithmetic runs.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using PureHDF;

using Alpaca4d;
using Alpaca4d.Generic;
using Alpaca4d.Material;
using Alpaca4d.Result;
using Alpaca4d.Section;

class ShellStressTest
{
    static int fails = 0;

    static void Check(bool ok, string what)
    {
        if (!ok) fails++;
        Console.WriteLine($"  [{(ok ? "PASS" : "FAIL")}] {what}");
    }

    /// <summary>
    /// Compared with a tolerance that is relative once the numbers are big, because a stress of
    /// several thousand derived through 6*m/h^2 cannot land within an absolute 1e-6 of the same
    /// stress the solver integrated - the two arrive by different arithmetic.
    /// </summary>
    static void Close(double got, double want, double tol, string what)
    {
        bool ok = Math.Abs(got - want) <= tol * Math.Max(1.0, Math.Abs(want));
        if (!ok) fails++;
        Console.WriteLine($"  [{(ok ? "PASS" : "FAIL")}] {what}: got {got:G6}, want {want:G6}");
    }

    static Model ModelReading(string file)
    {
        return new Model { Recorders = new List<IRecorder> { new Recorder { FileName = file } } };
    }

    // ---------------------------------------------------------------- which station is which

    static void LayerTests()
    {
        Console.WriteLine("\nTop is the last station, bottom the first\n");

        var five = Read.LayerFibres(5);
        Check(five.SequenceEqual(new[] { 4, 2, 0 }),
              $"five stations give top 4, middle 2, bottom 0: got {string.Join(",", five)}");

        // A PlateFiber section is always five, but a LayeredShell is however many layers were
        // given, and OpenSees allows any number from three up.
        Check(Read.LayerFibres(3).SequenceEqual(new[] { 2, 1, 0 }), "three stations still have a real middle");
        Check(Read.LayerFibres(9).SequenceEqual(new[] { 8, 4, 0 }), "nine stations pick the middle one");

        // An even count has no station at the mid-surface; the one above it is used, which is the
        // closest thing that exists.
        Check(Read.LayerFibres(4).SequenceEqual(new[] { 3, 2, 0 }), "an even count has no exact middle");

        Check(Read.LayerFibres(0).Length == 0, "no stations, nothing to pick");
        Check(Read.LayerNames.SequenceEqual(new[] { "Top", "Middle", "Bottom" }),
              "the names are in the same order as the stations");
    }

    // ---------------------------------------------------------------- the layered section

    static void LayeredSectionTests()
    {
        Console.WriteLine("\nA layered section is a stack, written bottom face first\n");

        var concrete = new ElasticIsotropicMaterial("concrete", 30000000.0, 12500000.0, 0.2, 2500.0) { Id = 1 };
        var steel = new ElasticIsotropicMaterial("steel", 210000000.0, 80000000.0, 0.3, 7850.0) { Id = 2 };

        var stack = new LayeredShellSection("deck", new List<ShellLayer>
        {
            new ShellLayer(steel, 0.02),
            new ShellLayer(concrete, 0.05),
            new ShellLayer(concrete, 0.06),
            new ShellLayer(concrete, 0.05),
            new ShellLayer(steel, 0.02),
        })
        { Id = 1 };

        // The exact line OpenSees 3.5 was fed and accepted while these checks were written.
        Check(stack.WriteTcl().TrimEnd() == "section LayeredShell 1 5 2 0.02 1 0.05 1 0.06 1 0.05 2 0.02",
              $"the deck line is what OpenSees expects: {stack.WriteTcl().TrimEnd()}");

        Close(stack.Thickness, 0.2, 1e-12, "thickness is the sum of the layers");

        // Assemble writes one material declaration per entry, so a symmetric stack naming the same
        // material five times must not ask for it five times.
        Check(stack.Materials.Count() == 2, $"distinct materials, not one per layer: got {stack.Materials.Count()}");

        Check(stack.Material == steel, "the single-material property answers with the bottom layer");

        // Scaling keeps the proportions, which is what makes Thickness settable at all.
        stack.Thickness = 0.4;
        Close(stack.Thickness, 0.4, 1e-12, "setting the thickness scales the stack");
        Close(stack.Layers[0].Thickness, 0.04, 1e-12, "and every layer with it");
        stack.Thickness = 0.2;

        var uniform = LayeredShellSection.Uniform("slab", 0.2, 5, concrete);
        uniform.Id = 2;
        Check(uniform.Layers.Count == 5, "a uniform stack has the layers asked for");
        Close(uniform.Thickness, 0.2, 1e-12, "and the thickness asked for");
        Check(uniform.Layers.All(l => Math.Abs(l.Thickness - 0.04) < 1e-12), "split equally");

        // OpenSees refuses fewer than three and says so; better to say so here than to write a
        // deck that dies in the solver.
        var tooFew = new LayeredShellSection("bad", new List<ShellLayer>
        {
            new ShellLayer(steel, 0.1), new ShellLayer(steel, 0.1),
        })
        { Id = 3 };

        bool refused = false;
        try { tooFew.WriteTcl(); } catch (Exception) { refused = true; }
        Check(refused, $"fewer than {LayeredShellSection.MinLayers} layers is refused, not written");

        Check(LayeredShellSection.Uniform("x", 0.2, 1, concrete).Layers.Count == LayeredShellSection.MinLayers,
              "and a uniform stack asked for too few is raised to the minimum");
    }

    // ---------------------------------------------------------------- against a real solve

    static void ReaderTests(string plate, string layered)
    {
        Console.WriteLine("\nRead back from a recorder OpenSees actually wrote\n");

        var model = ModelReading(plate);
        var all = Read.ShellFibreStresses(model, 0);

        Check(all.Count == 40, $"2 elements x 4 gauss x 5 stations: got {all.Count}");
        Check(all.Select(x => x.ElementId).Distinct().Count() == 2, "both elements came back");
        Check(all.Select(x => x.GaussPoint).Distinct().Count() == 4, "four integration points each");
        Check(all.Select(x => x.Fibre).Distinct().Count() == 5, "five stations through the thickness");

        var gp = all.Where(x => x.ElementId == 1 && x.GaussPoint == 0).OrderBy(x => x.Fibre).ToList();
        var layers = Read.LayerFibres(5);
        double top = gp[layers[0]].S11, middle = gp[layers[1]].S11, bottom = gp[layers[2]].S11;

        // A plate in pure bending: the two faces are equal and opposite, the mid-surface is zero.
        // This is the whole reason the layer has to be chosen rather than assumed.
        Close(middle, 0.0, 1e-6, "the mid-surface carries no direct stress in pure bending");
        Close(top, -bottom, 1e-6, "and the two faces are equal and opposite");
        Check(Math.Abs(top) > 1.0, $"with something actually there to read: |top| = {Math.Abs(top):G6}");

        // Against the resultants the same file holds: sigma = p/h -/+ 6m/h^2. Not exact - that is
        // the thin plate idealisation and this is a Mindlin shell - so the two agree to about
        // 1.6e-9 relative rather than to the last bit. Tight enough by a wide margin: a reader that
        // mis-strided the columns would be out by a whole component, not by a billionth.
        double h = 0.2;
        var resultants = ResultantsAt(plate, elementRow: 0, gauss: 0);
        double p11 = resultants[0], m11 = resultants[3];
        Close(bottom, p11 / h - 6.0 * m11 / (h * h), 1e-7, "bottom matches p/h - 6m/h^2 from the resultants");
        Close(top, p11 / h + 6.0 * m11 / (h * h), 1e-7, "top matches p/h + 6m/h^2");

        // Von Mises of a uniaxial state is the magnitude of that stress. Here s22 and s12 are not
        // quite zero, so only the order is checked - but zero would mean the formula never ran.
        Check(gp[layers[0]].VonMises > 0.0, "von Mises is computed from the five components");

        // The reader takes its layout from the file's own META, so a section with a different
        // number of stations needs no change to read.
        if (layered != null)
        {
            var layeredAll = Read.ShellFibreStresses(ModelReading(layered), 0);
            Check(layeredAll.Count == 40, $"a layered section reads with no special case: got {layeredAll.Count}");

            var lgp = layeredAll.Where(x => x.ElementId == 1 && x.GaussPoint == 0).OrderBy(x => x.Fibre).ToList();
            Close(lgp[2].S11, 0.0, 1e-6, "its mid-layer is still the neutral axis");

            // The stack is soft-stiff-stiff-stiff-soft. The outer layers are further from the
            // neutral axis but a third the stiffness, so they carry LESS than the ones inside
            // them - which is exactly what a layered section is for, and what a single-material
            // section cannot show.
            Check(Math.Abs(lgp[4].S11) < Math.Abs(lgp[3].S11),
                  $"a soft outer layer carries less than the stiff one inside it: " +
                  $"|{lgp[4].S11:G4}| < |{lgp[3].S11:G4}|");
        }
    }

    /// <summary>
    /// The eight stress resultants at one Gauss point, straight out of the "stresses" group -
    /// and, on the way, the proof that "stresses" is not stress.
    /// </summary>
    static double[] ResultantsAt(string file, int elementRow, int gauss)
    {
        // Both groups are read raw and compared. They are the same request under two names, and
        // the reason Shell Stresses could not simply be Shell Forces renamed.
        var fromStresses = RawRow(file, "stresses", elementRow);
        var fromSectionForce = RawRow(file, "section.force", elementRow);

        bool identical = fromStresses != null && fromSectionForce != null &&
                         fromStresses.Length == fromSectionForce.Length &&
                         fromStresses.Zip(fromSectionForce, (a, b) => a == b).All(x => x);

        Check(identical, "\"stresses\" and \"section.force\" are the same numbers - both are resultants");

        return fromStresses.Skip(gauss * 8).Take(8).ToArray();
    }

    static double[] RawRow(string file, string group, int row)
    {
        // The block form rather than a using declaration: mcs, which builds these checks, is
        // older than the C# 8 syntax the rest of the project uses.
        using (var h5 = PureHDF.H5File.OpenRead(Path.GetFullPath(file)))
        {
            string b = $"/MODEL_STAGE[1]/RESULTS/ON_ELEMENTS/{group}";
            if (!h5.LinkExists(b)) return null;

            foreach (var g in h5.Group(b).Children().OfType<PureHDF.IH5Group>())
            {
                if (!g.LinkExists("DATA")) continue;
                var ds = g.Group("DATA").Dataset("STEP_0");
                long rows = (long)ds.Space.Dimensions[0];
                long cols = (long)ds.Space.Dimensions[1];
                var data = ds.Read<double>().ToArray2D(rows, cols);

                var outp = new double[cols];
                for (int c = 0; c < cols; c++) outp[c] = data[row, c];
                return outp;
            }

            return null;
        }
    }

    static void Main(string[] args)
    {
        Console.WriteLine("Shell stresses through the thickness, and the layered section");

        LayerTests();
        LayeredSectionTests();

        string plate = args.Length > 0 ? args[0] : "plate.mpco";
        string layered = args.Length > 1 ? args[1] : "layered.mpco";

        if (File.Exists(plate))
            ReaderTests(plate, File.Exists(layered) ? layered : null);
        else
            Console.WriteLine("\nNo recorder file, so the reader checks were skipped.\n" +
                              "Put OpenSees on PATH and run run.sh, which solves the two decks here first.");

        Console.WriteLine(fails == 0 ? "\nAll checks passed." : $"\n{fails} check(s) FAILED.");
        Environment.Exit(fails == 0 ? 0 : 1);
    }
}
