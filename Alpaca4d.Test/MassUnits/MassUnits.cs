// Locks the kg -> solver mass-unit conversion on every path that reaches a Tcl deck,
// and emits the exact lines Alpaca.Core writes so the eigen benchmark can consume them.
using System;
using System.Globalization;
using System.IO;
using Rhino.Geometry;
using Alpaca4d;
using Alpaca4d.Material;
using Alpaca4d.Loads;
using Alpaca4d.Element;
using Alpaca4d.Section;

class MassUnits
{
    const double E = 2.1e8, NU = 0.0, RHO = 7850.0;      // kN/m2, -, kg/m3
    const double G = E / (2.0 * (1.0 + NU));
    static int fails = 0;

    static void Check(string what, double got, double want)
    {
        bool ok = Math.Abs(got - want) <= 1e-9 * Math.Max(1.0, Math.Abs(want));
        Console.WriteLine($"  [{(ok ? "PASS" : "FAIL")}] {what,-46} {got:G10}  (want {want:G10})");
        if (!ok) fails++;
    }
    static double Field(string line, int i) =>
        double.Parse(line.Trim().Split(' ')[i], CultureInfo.InvariantCulture);

    static void Main()
    {
        Console.WriteLine("Mass units, straight out of Alpaca.Core\n");

        Check("ModelMass.FromKg(1000 kg)", ModelMass.FromKg(1000.0), 1.0);
        Check("ModelMass round trip", ModelMass.ToKg(ModelMass.FromKg(7850.0)), 7850.0);

        // --- nD isotropic: what shells, bricks and tets read their density from ---
        var iso = new ElasticIsotropicMaterial("steel", E, G, NU, RHO) { Id = 1 };
        string isoLine = iso.WriteTcl().Trim();
        Console.WriteLine($"\n  {isoLine}");
        Check("nDMaterial ElasticIsotropic rho", Field(isoLine, 5), 7.85);

        // --- nD orthotropic ---
        var ortho = new ElasticOrthotropicMaterial("t", E, E, E, G, G, G, NU, NU, NU, RHO) { Id = 2 };
        string orthoLine = ortho.WriteTcl().Trim();
        Check("nDMaterial ElasticOrthotropic rho", Field(orthoLine, 12), 7.85);

        // --- point mass ---
        var pt = new MassLoad(Point3d.Origin, new Vector3d(1000, 1000, 1000), new Vector3d(40, 40, 40))
                 { Id = 3, Ndf = 6 };
        string massLine = pt.WriteTcl().Trim();
        Console.WriteLine($"  {massLine}");
        Check("mass, translational (1000 kg)", Field(massLine, 2), 1.0);
        Check("mass, rotational (40 kg m2)", Field(massLine, 5), 0.04);

        // --- a node carries no mass of its own -------------------------------
        // Both "node -mass" and "mass" land on Node::setMass, which assigns rather than
        // accumulates, and every node is written before any mass - so a "-mass" here would
        // be silently overwritten by the MassLoad at the same node. Concentrated mass has
        // exactly one route, and this keeps it that way.
        string nodeLine = new Node(new Point3d(1, 2, 3)) { Id = 7, Ndf = 6 }.WriteTcl().Trim();
        Console.WriteLine($"  {nodeLine}");
        bool clean = !nodeLine.Contains("-mass");
        Console.WriteLine($"  [{(clean ? "PASS" : "FAIL")}] node line carries no mass of its own");
        if (!clean) fails++;

        // --- two masses on one node add up ------------------------------------
        // "mass" sets a node's mass, so Assemble writes one line per node with the masses on it
        // summed: two Mass Points on one node used to keep only the last. The nodes are put in
        // by hand, as Assemble would, so this runs without Rhino.
        var model = new Model();
        model.NodeIndexSixNDF = new NodeIndex(0.001);
        model.NodeIndexSixNDF.Add(new Point3d(0, 0, 0));
        model.NodeIndexSixNDF.Add(new Point3d(4, 0, 0));
        model.UniquePointsSixNDF = model.NodeIndexSixNDF.Points;
        model.Mass.Add(new MassLoad(new Point3d(4, 0, 0), new Vector3d(60, 60, 60), Vector3d.Zero));
        model.Mass.Add(new MassLoad(new Point3d(4, 0, 0.0005), new Vector3d(80, 80, 80), new Vector3d(1, 2, 3)));
        model.Mass.Add(new MassLoad(new Point3d(0, 0, 0), new Vector3d(100, 100, 100), Vector3d.Zero));
        typeof(Model).GetMethod("WriteMasses", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                     .Invoke(model, null);
        Console.WriteLine($"  {string.Join("  ", model.Tcl).Replace("\n", "  ").Trim()}");
        Check("mass lines, one per node", model.Tcl.Count, 2);
        Check("node 1: the 100 kg alone", Field(model.Tcl[0], 2), 0.1);
        Check("node 2: 60 + 80 kg", Field(model.Tcl[1], 2), 0.14);
        Check("node 2: rotational terms added too", Field(model.Tcl[1], 7), 0.003);

        // --- beam mass per unit length, the path that was already right ---
        double area = 0.4 * 0.1;
        Check("beam -mass for A.rho", ModelMass.FromKg(area * RHO), 0.314);

        // --- the section lines the eigen benchmark will use, also from Alpaca.Core ---
        var uni = new UniaxialMaterialElastic("steel", E, E, 0.0, G, NU, RHO) { Id = 10 };
        var rect = new RectangleCS("rect", 0.4, 0.1, uni) { Id = 11 };
        string rectLine = rect.WriteTcl().Trim();
        var plate = new PlateFiberSection("plate", 0.1, iso) { Id = 12 };
        string plateLine = plate.WriteTcl().Trim();
        Console.WriteLine($"  {rectLine}");
        Console.WriteLine($"  {plateLine}");
        Check("RectangleCS area", Field(rectLine, 4), 0.04);
        Check("RectangleCS Izz (weak)", Field(rectLine, 5), 0.4 * Math.Pow(0.1, 3) / 12.0);

        // Hand the exact lines to the eigen benchmark.
        File.WriteAllLines("alpaca_lines.txt", new[] {
            "iso "   + isoLine,
            "ortho " + orthoLine,
            "mass "  + massLine,
            "rect "  + rectLine,
            "plate " + plateLine,
            "beammass " + ModelMass.FromKg(0.4 * 0.1 * RHO).ToString("R", CultureInfo.InvariantCulture),
        });

        Console.WriteLine(fails == 0 ? "\nALL PASS" : $"\n{fails} FAILED");
        Environment.Exit(fails == 0 ? 0 : 1);
    }
}
