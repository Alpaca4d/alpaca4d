// Solid elements: which way round they have to be wound, and how their stresses are read back.
//
// Two things are being guarded here.
//
//   1. A solid wound the wrong way round is one OpenSees solves rather than rejects. Both
//      SSPbrick and FourNodeTetrahedron form their volume element as a Gauss weight times the
//      determinant of the Jacobian and never look at its sign, so an element whose nodes arrive
//      in the wrong order gets a negative stiffness: the analysis converges, and the
//      displacements and stresses come back with the wrong sign. Nothing downstream can tell
//      that from a real answer. Utils.CleanHexahedron and Utils.CleanTetrahedron are the only
//      thing standing in the way, so what they decide is checked here against the solver's own
//      Jacobian, and the decks next to this file put the decision to OpenSees.
//
//   2. The recorder groups its rows by element class and writes an ID dataset saying which
//      element each row belongs to. Reading rows positionally instead only works while the
//      model's order and the file's happen to agree, which is a coincidence of how Alpaca4d
//      hands out tags rather than anything the format promises. The reader is checked against a
//      file whose two solid classes carry interleaved tags.
//
// The stresses are in the global axes. Neither element has a frame of its own - both build their
// strain from global nodal displacements and hand it straight to the nD material - and an nD
// material has no orientation argument either. The orthotropic deck here is what establishes it.
//
// The recorder files come from running OpenSees on the decks next to this file, so the numbers
// are the solver's own. Without OpenSees on PATH the reader checks are skipped and only the
// geometry checks run; those need no solver, and no RhinoCommon native library either, because
// Point3d and Vector3d are managed types.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;

using Rhino.Geometry;

using Alpaca4d;
using Alpaca4d.Element;
using Alpaca4d.Generic;
using Alpaca4d.Result;

/// <summary>
/// A solid that is nothing but a tag and a class. The reader only ever asks an element for its
/// Id, so this keeps the reader checks clear of Mesh, which needs a native library that a plain
/// mono or dotnet run has no way to load.
/// </summary>
class TaggedSolid : IBrick
{
    public int? Id { get; set; }
    public string ElementId { get; set; }
    public Mesh Mesh { get; set; }
    public IMultiDimensionMaterial Material { get; set; }
    public ElementType Type => ElementType.Brick;
    public ElementClass ElementClass { get; set; }
    public List<int?> IndexNodes { get; set; }
    public Color Color { get; set; }
    public int Ndf => 3;
    public void SetTags() { }
    public void SetTopologyRTree(Model model) { }
    public string WriteTcl() => "";
}

class BrickStressTest
{
    static int fails = 0;

    static void Check(bool ok, string what)
    {
        if (!ok) fails++;
        Console.WriteLine($"  [{(ok ? "PASS" : "FAIL")}] {what}");
    }

    static void Close(double got, double want, string what)
    {
        bool ok = Math.Abs(got - want) <= 1e-6 * Math.Max(1.0, Math.Abs(want));
        if (!ok) fails++;
        Console.WriteLine($"  [{(ok ? "PASS" : "FAIL")}] {what}: got {got:G8}, want {want:G8}");
    }

    // ------------------------------------------------------------------ the geometry helpers

    // Private, because nothing outside Utils has any business forming a Jacobian; reached here
    // rather than made public so that the test can say what the shipped code does, not what a
    // copy of it does.
    static readonly MethodInfo HexJacobian =
        typeof(Utils).GetMethod("HexahedronJacobian", BindingFlags.NonPublic | BindingFlags.Static);
    static readonly MethodInfo TetJacobian =
        typeof(Utils).GetMethod("TetrahedronJacobian", BindingFlags.NonPublic | BindingFlags.Static);
    static readonly MethodInfo CheckHex =
        typeof(Utils).GetMethod("CheckHexahedron", BindingFlags.NonPublic | BindingFlags.Static);

    static double Hex(IList<Point3d> n, double xi = 0, double eta = 0, double zeta = 0)
        => (double)HexJacobian.Invoke(null, new object[] { n, xi, eta, zeta });

    static double Tet(IList<Point3d> n)
        => (double)TetJacobian.Invoke(null, new object[] { n });

    static bool Accepted(IList<Point3d> n)
    {
        try { CheckHex.Invoke(null, new object[] { n }); return true; }
        catch (TargetInvocationException) { return false; }
    }

    /// <summary>Both faces reversed together: the flip CleanHexahedron applies.</summary>
    static List<Point3d> Flip(IList<Point3d> n)
        => new List<Point3d> { n[3], n[2], n[1], n[0], n[7], n[6], n[5], n[4] };

    static List<Point3d> Cube(double s) => new List<Point3d>
    {
        new Point3d(0,0,0), new Point3d(s,0,0), new Point3d(s,s,0), new Point3d(0,s,0),
        new Point3d(0,0,s), new Point3d(s,0,s), new Point3d(s,s,s), new Point3d(0,s,s),
    };

    static void JacobianTests()
    {
        Console.WriteLine("\nThe sign of the Jacobian, and the flip that fixes it\n");

        // Every size, because the ray this replaced was a fixed 1000 units long and a fixed 0.001
        // nudge: a solid deeper than the one or thinner than the other answered backwards.
        foreach (double s in new[] { 2e-4, 0.001, 1.0, 1500.0, 3000.0, 100000.0 })
        {
            Check(Hex(Cube(s)) > 0, $"a cube of {s:G6} in OpenSees order reads positive");
            Check(Hex(Flip(Cube(s))) < 0, $"a cube of {s:G6} reversed reads negative");
            Check(Hex(Flip(Flip(Cube(s)))) > 0, $"a cube of {s:G6} flipped twice reads positive again");
        }

        var far = Cube(1.0).Select(p => p + new Vector3d(1e6, -5e5, 3e5)).ToList();
        Check(Hex(far) > 0, "a unit cube a million units from the origin reads positive");

        Console.WriteLine("\nWhat is accepted and what is turned away\n");

        Check(Accepted(Cube(1.0)), "a unit cube is accepted");
        Check(Accepted(Cube(1500.0)), "a cube of 1500 is accepted - the old ray could not cross it");
        Check(!Accepted(Flip(Cube(1.0))), "a reversed cube is turned away");

        var sheared = Cube(1.0);
        for (int i = 4; i < 8; i++) sheared[i] = sheared[i] + new Vector3d(0.7, 0.4, 0.0);
        Check(Accepted(sheared), "a sheared brick is accepted");

        var thin = Cube(1.0);
        for (int i = 4; i < 8; i++) thin[i] = new Point3d(thin[i].X, thin[i].Y, 2e-4);
        Check(Accepted(thin), "a brick a fifth of a millimetre thick is accepted");
        Check(!Accepted(Flip(thin)), "and the same brick reversed is turned away");

        // A quarter turn between the two faces is a distorted brick but a real one: its mid height
        // section is a diamond, not a crossing. Turning it away would be wrong.
        var quarterTurn = new List<Point3d>
        {
            new Point3d(0,0,0), new Point3d(1,0,0), new Point3d(1,1,0), new Point3d(0,1,0),
            new Point3d(1,0,1), new Point3d(1,1,1), new Point3d(0,1,1), new Point3d(0,0,1),
        };
        Check(Accepted(quarterTurn), "a brick with a quarter turn in it is accepted");

        // The far face wound the other way, which is what a mesh series whose meshes disagree
        // about winding produces. Edges 1-5 and 3-7 cross and the brick folds through itself; the
        // centre reads zero, so only looking at the corners catches it.
        var bowtie = new List<Point3d>
        {
            new Point3d(0,0,0), new Point3d(1,0,0), new Point3d(1,1,0), new Point3d(0,1,0),
            new Point3d(0,0,1), new Point3d(0,1,1), new Point3d(1,1,1), new Point3d(1,0,1),
        };
        Check(!Accepted(bowtie), "a brick whose far face is wound the other way is turned away");
        Check(Math.Abs(Hex(bowtie)) < 1e-12, "and it is the corners that catch it - its centre reads zero");

        var flat = Cube(1.0);
        for (int i = 4; i < 8; i++) flat[i] = new Point3d(flat[i].X, flat[i].Y, 0.0);
        Check(!Accepted(flat), "a brick with no thickness is turned away");

        Console.WriteLine("\nThe tetrahedron\n");

        var tet = new List<Point3d>
        {
            new Point3d(0,0,0), new Point3d(1,0,0), new Point3d(0,1,0), new Point3d(0,0,1)
        };
        Check(Tet(tet) > 0, "a tetrahedron in OpenSees order reads positive");
        Check(Tet(new List<Point3d> { tet[0], tet[2], tet[1], tet[3] }) < 0,
              "two nodes swapped reads negative, which is the swap CleanTetrahedron undoes");
        Close(Tet(tet), 1.0, "the value is six times the signed volume");
        Check(Math.Abs(Tet(new List<Point3d>
        {
            new Point3d(0,0,0), new Point3d(1,0,0), new Point3d(0,1,0), new Point3d(1,1,0)
        })) < 1e-12, "four points on one plane read zero");
    }

    // ------------------------------------------------------------------------- the reader

    static Model ModelOf(string file, params (int tag, ElementClass cls)[] solids)
    {
        return new Model
        {
            Recorders = new List<IRecorder> { new Recorder { FileName = file } },
            Bricks = solids.Select(s => (IBrick)new TaggedSolid { Id = s.tag, ElementClass = s.cls }).ToList(),
        };
    }

    /// <summary>
    /// mixed.tcl holds two tetrahedra tagged 10 and 30 and two SSP bricks tagged 20 and 40, each
    /// under a different tension, with the tags interleaved across the two classes on purpose:
    /// the recorder writes one dataset per class, so the file's rows and the model's order are
    /// only ever going to agree by luck.
    /// </summary>
    static void ReaderTests(string file)
    {
        Console.WriteLine("\nReading a file whose two solid classes carry interleaved tags\n");

        var model = ModelOf(file,
            (10, ElementClass.FourNodeTetrahedron), (20, ElementClass.SSPBrick),
            (30, ElementClass.FourNodeTetrahedron), (40, ElementClass.SSPBrick));

        var tet = Read.TetrahedronStress(model, 0);
        var ssp = Read.SSPBrickStress(model, 0);

        Close(tet.Item1[0], 600.0, "tetrahedron 10");
        Close(tet.Item1[1], 2400.0, "tetrahedron 30");
        Close(ssp.Item1[0], 10.0, "SSP brick 20");
        Close(ssp.Item1[1], 40.0, "SSP brick 40");

        Console.WriteLine("\nThe model's order is what comes back, not the file's\n");

        var reversed = ModelOf(file,
            (30, ElementClass.FourNodeTetrahedron), (10, ElementClass.FourNodeTetrahedron),
            (40, ElementClass.SSPBrick), (20, ElementClass.SSPBrick));

        Close(Read.TetrahedronStress(reversed, 0).Item1[0], 2400.0, "tetrahedron 30 listed first");
        Close(Read.SSPBrickStress(reversed, 0).Item1[0], 40.0, "SSP brick 40 listed first");

        Console.WriteLine("\nWhat is said when the file cannot answer\n");

        var onlyBricks = ModelOf(file, (20, ElementClass.SSPBrick));
        Check(Read.TetrahedronStress(onlyBricks, 0).Item1.Count == 0,
              "a model with no tetrahedra reads back an empty list rather than throwing");

        try
        {
            Read.SSPBrickStress(ModelOf(file, (99, ElementClass.SSPBrick)), 0);
            Check(false, "an element the file does not hold is named");
        }
        catch (Exception e)
        {
            Check(e.Message.Contains("99"), $"an element the file does not hold is named: {e.Message.Split('.')[0]}");
        }

        try
        {
            Read.SSPBrickStress(model, 7);
            Check(false, "a step the file does not hold is named");
        }
        catch (Exception e)
        {
            Check(e.Message.Contains("STEP_7"), $"a step the file does not hold is named: {e.Message}");
        }
    }

    /// <summary>
    /// one.tcl is a unit cube of E = 1000 and nu = 0 pulled to a uniform 10 along global X, so
    /// only the first component is nonzero. That is what pins the component order down: the file
    /// carries sigma11, sigma22, sigma33, sigma12, sigma23, sigma13, and calling the first of them
    /// anything but the global X direct stress would show up here.
    /// </summary>
    static void ComponentOrderTests(string file)
    {
        Console.WriteLine("\nThe component order, on a cube pulled along global X\n");

        var s = Read.SSPBrickStress(ModelOf(file, (1, ElementClass.SSPBrick)), 0);

        Close(s.Item1[0], 10.0, "sigma_xx");
        Check(new[] { s.Item2[0], s.Item3[0], s.Item4[0], s.Item5[0], s.Item6[0] }.All(v => Math.Abs(v) < 1e-9),
              "the other five components are zero");
    }

    /// <summary>
    /// ortho.tcl pulls the same cube of an ElasticOrthotropic material along X and then along Y,
    /// with Ex of 1000 and Ey of 4000. If the material had a frame of its own the two would read
    /// alike; they do not, which is what "a solid has no local axes" means.
    /// </summary>
    static void OrthotropicTests(string file)
    {
        Console.WriteLine("\nAn orthotropic material in a brick follows the global axes\n");

        var lines = File.ReadAllLines(file);

        Close(Extension(lines, "X"), 0.010, "pulled along global X, Ex of 1000 gives an extension of");
        Close(Extension(lines, "Y"), 0.0025, "pulled along global Y, Ey of 4000 gives an extension of");
    }

    /// <summary>One "X = ..." or "Y = ..." line of what ortho.tcl wrote.</summary>
    static double Extension(string[] lines, string axis)
    {
        return double.Parse(lines.First(l => l.StartsWith(axis)).Split('=')[1].Trim(),
                            System.Globalization.CultureInfo.InvariantCulture);
    }

    static void Main(string[] args)
    {
        Console.WriteLine("Solid elements: winding, and reading their stresses back");

        JacobianTests();

        string mixed = args.Length > 0 ? args[0] : "mixed.mpco";
        string one = args.Length > 1 ? args[1] : "one.mpco";
        string ortho = args.Length > 2 ? args[2] : "ortho.txt";

        if (File.Exists(mixed))
        {
            ReaderTests(mixed);
            if (File.Exists(one)) ComponentOrderTests(one);
            if (File.Exists(ortho)) OrthotropicTests(ortho);
        }
        else
        {
            Console.WriteLine("\nNo recorder file, so the reader checks were skipped.\n" +
                              "Put OpenSees on PATH and run run.sh, which solves the decks here first.");
        }

        Console.WriteLine(fails == 0 ? "\nAll checks passed." : $"\n{fails} check(s) FAILED.");
        Environment.Exit(fails == 0 ? 0 : 1);
    }
}
