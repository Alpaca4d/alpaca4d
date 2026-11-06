// Checks the FEM-Design export without Rhino: models are built as an ExportModel - what
// ExportModelReader reads out of an assembled Alpaca model - written by StruxmlWriter, checked against
// FEM-Design's schema, saved, read back, and inspected as the XML FEM-Design reads. See README.md.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Rhino.Geometry;
using Alpaca4d.Constraints;
using Alpaca4d.Element;
using Alpaca4d.FemDesign;
using Alpaca4d.Interop;
using Alpaca4d.Loads;
using StruSoft.Interop.StruXml.Data;

class FemDesignExportCheck
{
    static readonly XNamespace Fd = "urn:strusoft";
    static int fails = 0;

    static void Check(string what, bool ok, string detail = "")
    {
        Console.WriteLine($"  [{(ok ? "PASS" : "FAIL")}] {what}{(detail.Length > 0 ? "  " + detail : "")}");
        if (!ok) fails++;
    }

    static bool Near(double a, double b, double tol = 1e-9) => Math.Abs(a - b) <= tol * Math.Max(1.0, Math.Abs(b));

    static double D(XElement e, string attribute) => double.Parse((string)e.Attribute(attribute), CultureInfo.InvariantCulture);

    static Vector3d Vec(XElement e) => new Vector3d(D(e, "x"), D(e, "y"), D(e, "z"));

    static bool Same(Vector3d a, Vector3d b) => (a - b).Length < 1e-9;

    // ------------------------------------------------------------------ model building

    static ExportMaterial Steel(string grade = "S355") => new ExportMaterial { Name = grade, Grade = grade, E = 2.1e8, G = 8.077e7, Nu = 0.3, Rho = 7850 };

    static int AddMaterial(ExportModel m, ExportMaterial material) { m.Materials.Add(material); return m.Materials.Count - 1; }

    static int AddSection(ExportModel m, ExportBeamSection section) { m.BeamSections.Add(section); return m.BeamSections.Count - 1; }

    static ExportBeamSection Hea200(int material) => new ExportBeamSection
    {
        Name = "HEA200", Shape = ExportSectionShape.I, Material = material,
        Height = 0.19, TopWidth = 0.2, BottomWidth = 0.2, TopFlange = 0.01, BottomFlange = 0.01, Web = 0.0065,
        Area = 5.13e-3, Izz = 3.69e-5, Iyy = 1.34e-5, J = 2.1e-7,
    };

    /// <summary>A beam with Alpaca's default axes: local y up for a level beam, along global X for a vertical one.</summary>
    static ExportBeam Beam(ExportModel m, Point3d a, Point3d b, int section, Release i = null, Release j = null, string label = null)
    {
        var x = Unit(b - a);
        bool vertical = Math.Abs(x.X) < 1e-12 && Math.Abs(x.Y) < 1e-12;
        var y = Unit(vertical ? Vector3d.XAxis : Vector3d.ZAxis - x * (Vector3d.ZAxis * x));
        var z = Vector3d.CrossProduct(x, y);
        var beam = new ExportBeam { Id = m.Beams.Count + 1, Label = label, Start = a, End = b, LocalZ = z, Section = section, ReleaseI = i, ReleaseJ = j };
        m.Beams.Add(beam);
        return beam;
    }

    /// <summary>RhinoCommon's Unitize needs Rhino running.</summary>
    static Vector3d Unit(Vector3d v) => v / v.Length;

    static Release Pin(bool torsion) => new Release(true, true, true, torsion, false, false);

    static ExportShell Shell(ExportModel m, int section, params Point3d[] corners)
    {
        var shell = new ExportShell { Id = 1000 + m.Shells.Count, Vertices = corners, Section = section };
        m.Shells.Add(shell);
        return shell;
    }

    static ExportSupport Fixed(Point3d p) => new ExportSupport { Position = p, Tx = true, Ty = true, Tz = true, Rx = true, Ry = true, Rz = true };

    // ------------------------------------------------------------------ writing and reading back

    static string folder = Path.Combine(Path.GetTempPath(), "alpaca-fd-export-" + Guid.NewGuid().ToString("N").Substring(0, 8));

    /// <summary>Writes the model, saves it, reads it back as FEM-Design would, and returns its XML.</summary>
    static (XElement Root, FemDesignExportResult Result) Export(string name, ExportModel model, Eurocodetype country = Eurocodetype.Common)
    {
        var report = new ConversionReport();
        var result = StruxmlWriter.Write(model, country, report);
        Check($"{name}: follows FEM-Design's schema", result.Database.ValidateXmlAgainstSchema(out var errors), string.Join(" | ", errors.Take(3)));
        var path = result.Save(Path.Combine(folder, name));
        Check($"{name}: saved as .struxml and read back", path.EndsWith(".struxml") && Database.Deserialize(path) != null, path);
        return (XElement.Parse(result.Database.SerializeToString()), result);
    }

    static IEnumerable<XElement> All(XElement root, string name) => root.Descendants(Fd + name);

    static XElement ByGuid(XElement root, string element, string guid) => All(root, element).FirstOrDefault(e => (string)e.Attribute("guid") == guid);

    static string Messages(FemDesignExportResult result) => string.Join(" | ", result.Report.Lines());

    // ------------------------------------------------------------------ checks

    static void Main()
    {
        Console.WriteLine("Alpaca4DToFEM-Design: materials, sections, bars, shells, supports, loads\n");
        Directory.CreateDirectory(folder);
        try
        {
            Frame();
            Trusses();
            AlpacaTrussesAndSpringSupports();
            Sections();
            Shells();
            ShellEdgeCases();
            Connections();
            Loads();
            Countries();
        }
        finally
        {
            Directory.Delete(folder, true);
        }
        Console.WriteLine();
        Console.WriteLine(fails == 0 ? "all checks passed" : $"{fails} check(s) failed");
        Environment.Exit(fails == 0 ? 0 : 1);
    }

    /// <summary>A portal: library steel and profile, columns up and down, axes, supports.</summary>
    static void Frame()
    {
        Console.WriteLine("portal frame");
        var m = new ExportModel();
        int steel = AddMaterial(m, Steel());
        int hea = AddSection(m, Hea200(steel));
        Beam(m, new Point3d(0, 0, 0), new Point3d(0, 0, 4), hea, label: "Column");
        Beam(m, new Point3d(6, 0, 4), new Point3d(6, 0, 0), hea, label: "Column");   // drawn downwards
        Beam(m, new Point3d(0, 0, 4), new Point3d(6, 0, 4), hea);
        m.Supports.Add(Fixed(new Point3d(0, 0, 0)));
        m.Supports.Add(new ExportSupport { Position = new Point3d(6, 0, 0), XAxis = Unit(new Vector3d(1, 1, 0)), YAxis = Unit(new Vector3d(-1, 1, 0)), Tx = true, Ty = true, Tz = true });

        var (root, result) = Export("frame", m);
        var bars = All(root, "bar").ToList();
        var columns = bars.Where(b => (string)b.Attribute("type") == "column").ToList();
        var beams = bars.Where(b => (string)b.Attribute("type") == "beam").ToList();
        Check("two columns and a beam", columns.Count == 2 && beams.Count == 1, $"got {bars.Count} bars");
        Check("columns keep the label as their name", columns.Select(c => (string)c.Attribute("name")).OrderBy(n => n).SequenceEqual(new[] { "Column.1", "Column.2" }),
            string.Join(", ", columns.Select(c => (string)c.Attribute("name"))));
        Check("a beam without a label is B.1, its part B.1.1", (string)beams[0].Attribute("name") == "B.1" && (string)beams[0].Element(Fd + "bar_part").Attribute("name") == "B.1.1");

        foreach (var column in columns)
        {
            var points = column.Descendants(Fd + "curve").Elements(Fd + "point").ToList();
            Check($"{column.Attribute("name")} runs upwards", D(points[1], "z") > D(points[0], "z"));
            // Alpaca's local y - the depth - is global X on a vertical member: FEM-Design's local z is too.
            var localY = Vec(column.Descendants(Fd + "local-y").First());
            var localZ = Vector3d.CrossProduct(new Vector3d(0, 0, 1), localY);
            Check($"{column.Attribute("name")} has its depth along X", Same(localZ, Vector3d.XAxis), $"local y {localY}");
        }
        var beamY = Vec(beams[0].Descendants(Fd + "local-y").First());
        Check("the beam's depth is vertical (local y horizontal)", Same(Vector3d.CrossProduct(Vector3d.XAxis, beamY), Vector3d.ZAxis), $"local y {beamY}");

        var material = All(root, "material").Single();
        Check("S355 is FEM-Design's S 355", (string)material.Attribute("name") == "S 355" && material.Element(Fd + "steel") != null);
        var section = All(root, "section").Single(s => s.Parent.Name == Fd + "sections");
        Check("HEA200 is FEM-Design's HE-A 200", (string)section.Attribute("name") == "Steel sections, HE-A, 200");
        var complex = All(root, "complex_section").Single();
        Check("bars use it through one complex section",
            bars.All(b => (string)b.Element(Fd + "bar_part").Attribute("complex_section") == (string)complex.Attribute("guid")));

        var supports = All(root, "point_support").ToList();
        Check("two supports", supports.Count == 2);
        var skewed = supports[1];
        Check("a skewed support keeps its plane's axes",
            Same(Vec(skewed.Element(Fd + "group").Element(Fd + "local_x")), new Vector3d(1, 1, 0) / Math.Sqrt(2)), skewed.ToString().Substring(0, Math.Min(300, skewed.ToString().Length)));
        var springs = skewed.Descendants(Fd + "springs").First();
        Check("a pinned support holds the translations and frees the rotations",
            D(springs, "Kx_pos") == 1e10 && D(springs, "Kz_neg") == 1e10 && D(springs, "Cx_pos") == 0 && D(springs, "Cz_neg") == 0, springs.ToString());
        Check("nothing to report but remarks", !result.Report.HasErrors && result.Report.Entries.All(e => e.Level == ReportLevel.Remark), Messages(result));
    }

    /// <summary>Pinned and unloaded is a truss; pinned and loaded a beam with releases; releases swap axes.</summary>
    static void Trusses()
    {
        Console.WriteLine("\ntrusses and releases");
        var m = new ExportModel();
        int steel = AddMaterial(m, Steel());
        int hea = AddSection(m, Hea200(steel));
        Beam(m, new Point3d(0, 0, 0), new Point3d(4, 0, 3), hea, Pin(false), Pin(true));
        Beam(m, new Point3d(0, 0, 3), new Point3d(4, 0, 3), hea, Pin(false), Pin(true));
        // Alpaca's Ry is about local y, its weak axis: FEM-Design's local z.
        Beam(m, new Point3d(0, 2, 3), new Point3d(4, 2, 3), hea, null, new Release(true, true, true, true, false, true));
        var live = new ExportLoadCase { Name = "Live" };
        live.BeamLoads.Add(new ExportBeamLoad { Beam = 1, Force = new Vector3d(0, 0, -5) });
        m.LoadCases.Add(live);

        var (root, result) = Export("trusses", m);
        var bars = All(root, "bar").ToList();
        var truss = bars.SingleOrDefault(b => (string)b.Attribute("type") == "truss");
        Check("the unloaded pinned member is a truss", truss != null && bars.Count == 3);
        var section = All(root, "section").Single(s => s.Parent.Name == Fd + "sections");
        Check("... referring to the section itself, not a complex section",
            truss != null && (string)truss.Element(Fd + "bar_part").Attribute("complex_section") == (string)section.Attribute("guid"));

        var loaded = bars.Single(b => (string)b.Attribute("name") == "B.1");
        var ends = loaded.Element(Fd + "bar_part").Elements(Fd + "connectivity").ToList();
        bool Free(XElement end, string dof) => !(bool?)end.Attribute(dof) ?? true;
        Check("the loaded pinned member is a beam hinged at both ends",
            ends.Count == 2 && ends.All(e => Free(e, "r_y") && Free(e, "r_z")) && !Free(ends[0], "m_x"), string.Join(" ", ends));
        Check("... torsion released at the end Alpaca releases it", Free(ends[0], "r_x") && !Free(ends[1], "r_x"));

        var weak = bars.Single(b => (string)b.Attribute("name") == "B.2").Element(Fd + "bar_part").Elements(Fd + "connectivity").ToList();
        Check("Alpaca's Ry release is FEM-Design's r_z", weak.Count == 2 && !Free(weak[0], "r_z") && Free(weak[1], "r_z") && !Free(weak[1], "r_y"), string.Join(" ", weak));
    }

    /// <summary>
    /// What ExportModelReader makes of Alpaca's Truss and Spring Support (checked in Rhino, see
    /// TrussAndSpringSupport/rhino.sh) as FEM-Design receives it: a truss unless a line load is on it, and a
    /// point support with the spring's stiffness.
    /// </summary>
    static void AlpacaTrussesAndSpringSupports()
    {
        Console.WriteLine("\nAlpaca trusses and spring supports");
        var m = new ExportModel();
        int steel = AddMaterial(m, Steel());
        int hea = AddSection(m, Hea200(steel));
        var bar = Beam(m, new Point3d(0, 0, 0), new Point3d(4, 0, 3), hea, Pin(true), Pin(false), "bracing");
        bar.IsTruss = true;
        var loadedBar = Beam(m, new Point3d(0, 0, 3), new Point3d(4, 0, 3), hea, Pin(true), Pin(false), "chord");
        loadedBar.IsTruss = true;
        var live = new ExportLoadCase { Name = "Live" };
        live.BeamLoads.Add(new ExportBeamLoad { Beam = 1, Force = new Vector3d(0, 0, -2) });
        m.LoadCases.Add(live);

        m.Supports.Add(new ExportSupport { Position = new Point3d(0, 0, 0), Stiffness = new[] { 0.0, 500.0, double.PositiveInfinity, 0.0, 0.0, 0.0 }, Description = "spring" });
        m.Supports.Add(new ExportSupport { Position = new Point3d(4, 0, 0), Stiffness = new double[6], Description = "nothing" });
        m.Supports.Add(Fixed(new Point3d(4, 0, 3)));

        var (root, result) = Export("alpaca-trusses", m);
        var bars = All(root, "bar").ToList();
        Check("an Alpaca truss is a FEM-Design truss", bars.Count(b => (string)b.Attribute("type") == "truss") == 1, string.Join(", ", bars.Select(b => (string)b.Attribute("name") + " " + (string)b.Attribute("type"))));
        Check("one with a line load on it is a beam pinned at both ends", bars.Count(b => (string)b.Attribute("type") == "beam") == 1);
        Check("and the report says why", Messages(result).Contains("A truss with a line load"), Messages(result));

        var supports = All(root, "point_support").ToList();
        Check("a support of springs, and the fixed one; the one holding nothing is left out", supports.Count == 2);
        var springs = supports[0].Descendants(Fd + "springs").First();
        Check("its stiffness: free along X, 500 along Y, rigid along Z, free to turn",
            D(springs, "Kx_pos") == 0 && D(springs, "Ky_pos") == 500 && D(springs, "Ky_neg") == 500 && D(springs, "Kz_pos") == 1e10 &&
            D(springs, "Cx_pos") == 0 && D(springs, "Cz_neg") == 0, springs.ToString());
    }

    /// <summary>Library profiles by name or size, outlines for the rest, fictitious bars for A-I-J, custom materials.</summary>
    static void Sections()
    {
        Console.WriteLine("\nmaterials and sections");
        var m = new ExportModel();
        int steel = AddMaterial(m, Steel());
        // As Material Library (Alpaca4d) makes them, from Alpaca's concrete and timber databases.
        int concrete = AddMaterial(m, new ExportMaterial { Name = "C30_37", E = 3.0e7, G = 1.6e7, Nu = 0.2, Rho = 2500 });
        int timber = AddMaterial(m, new ExportMaterial { Name = "C24", E = 1.05e7, G = 6.9e5, Nu = 0.3, Rho = 410 });
        int plastic = AddMaterial(m, new ExportMaterial { Name = null, E = 3e6, G = 1.1e6, Nu = 0.36, Rho = 1400 });

        var sections = new[]
        {
            new ExportBeamSection { Shape = ExportSectionShape.Rectangle, Material = concrete, Width = 0.2, Height = 0.4 },
            new ExportBeamSection { Shape = ExportSectionShape.Rectangle, Material = timber, Width = 0.09, Height = 0.225 },
            new ExportBeamSection { Name = "48.3x3", Shape = ExportSectionShape.Circle, Material = steel, Diameter = 0.0483, Thickness = 0.003 },
            new ExportBeamSection { Name = "40x20x2", Shape = ExportSectionShape.RectangleHollow, Material = steel, Height = 0.04, Width = 0.02, Web = 0.002, TopFlange = 0.002, BottomFlange = 0.002 },
            new ExportBeamSection { Name = "HEA200", Shape = ExportSectionShape.I, Material = steel, Height = 0.3, TopWidth = 0.2, BottomWidth = 0.2, TopFlange = 0.01, BottomFlange = 0.01, Web = 0.0065 },
            new ExportBeamSection { Shape = ExportSectionShape.DoubleAngle, Material = steel, Height = 0.06, Width = 0.06, Thickness = 0.006, Gap = 0.01 },
            new ExportBeamSection { Shape = ExportSectionShape.Rectangle, Material = plastic, Width = 0.05, Height = 0.1 },
            new ExportBeamSection { Shape = ExportSectionShape.Generic, Material = steel, Area = 0.01, Izz = 2e-4, Iyy = 5e-5, J = 1e-5 },
        };
        for (int i = 0; i < sections.Length; i++)
            Beam(m, new Point3d(0, i, 0), new Point3d(5, i, 0), AddSection(m, sections[i]));

        var (root, result) = Export("sections", m);
        var library = All(root, "section").Where(s => s.Parent.Name == Fd + "sections").Select(s => (string)s.Attribute("name")).ToList();
        Check("concrete 200x400 is FEM-Design's", library.Contains("Concrete sections, Rectangle, 200x400"), string.Join("; ", library));
        Check("timber 90x225 is FEM-Design's", library.Contains("Timber sections, Rectangle, 90x225"));
        Check("tube 48.3x3 is FEM-Design's cold-formed KCKR 48.3-3", library.Contains("Steel sections, KCKR, 48.3-3"));
        Check("hollow 40x20x2 is FEM-Design's cold-formed KKR 40x20x2", library.Contains("Steel sections, KKR, 40x20x2"));
        Check("an HEA200 of other dimensions is drawn, not taken from the library", library.Contains("Alpaca, I, 300x200x6.5x10") && !library.Contains("Steel sections, HE-A, 200"));
        Check("... with a warning", result.Report.Entries.Any(e => e.Level == ReportLevel.Warning && e.Message.Contains("HE-A")), Messages(result));

        var angles = All(root, "section").Single(s => s.Parent.Name == Fd + "sections" && ((string)s.Attribute("name")).StartsWith("Alpaca, Double angle"));
        Check("a double angle is drawn as two regions", angles.Descendants(Fd + "region").Count() == 2);
        var drawnI = All(root, "section").Single(s => s.Parent.Name == Fd + "sections" && ((string)s.Attribute("name")).StartsWith("Alpaca, I,"));
        var corners = drawnI.Descendants(Fd + "point").Select(p => (X: D(p, "x"), Y: D(p, "y"))).Distinct().ToList();
        Check("the drawn I is 300 deep along its y and 200 wide along its x",
            Near(corners.Max(c => c.Y) - corners.Min(c => c.Y), 0.3) && Near(corners.Max(c => c.X) - corners.Min(c => c.X), 0.2), $"{corners.Count} corners");
        Check("... as a welded steel section", (string)drawnI.Attribute("fd-mat") == "2");

        var materials = All(root, "material").ToList();
        Check("C30_37 is FEM-Design's C30/37, C24 its timber", materials.Any(x => (string)x.Attribute("name") == "C30/37" && x.Element(Fd + "concrete") != null)
                                                              && materials.Any(x => (string)x.Attribute("name") == "C24" && x.Element(Fd + "timber") != null));
        var custom = materials.Single(x => x.Element(Fd + "custom") != null).Element(Fd + "custom");
        Check("an unnamed material is a custom one with Alpaca's E, G and mass",
            D(custom, "E_0") == 3e6 && D(custom, "E_2") == 3e6 && D(custom, "G_1") == 1.1e6 && Near(D(custom, "mass"), 1.4) && (string)custom.Parent.Attribute("name") == "Alpaca material 4");

        var fictitious = All(root, "virtual_bar").SingleOrDefault();
        Check("an A-I-J section makes a fictitious bar", fictitious != null && All(root, "bar").Count() == 7, $"{All(root, "bar").Count()} bars");
        if (fictitious != null)
            Check("... with EA, the strong EI as I1E, the weak as I2E, GJ and its mass per length",
                Near(D(fictitious, "AE"), 2.1e6) && Near(D(fictitious, "I1E"), 4.2e4) && Near(D(fictitious, "I2E"), 1.05e4)
                && Near(D(fictitious, "ItG"), 807.7) && Near(D(fictitious, "unit_mass"), 0.0785), fictitious.ToString().Substring(0, 300));
        Check("FEM-Design's C30/37 having another E than Alpaca's is a warning",
            result.Report.Entries.Any(e => e.Level == ReportLevel.Warning && e.Message.Contains("C30/37") && e.Message.Contains("33000")), Messages(result));
        Check("its C24 weighing more is a remark",
            result.Report.Entries.Any(e => e.Level == ReportLevel.Remark && e.Message.Contains("C24") && e.Message.Contains("420")));
    }

    /// <summary>Coplanar faces merge into plates and walls, holes are kept, shared edges keep their points.</summary>
    static void Shells()
    {
        Console.WriteLine("\nshells");
        var m = new ExportModel();
        int concrete = AddMaterial(m, new ExportMaterial { Name = "C30_37", E = 3.3e7, G = 1.375e7, Nu = 0.2, Rho = 2500 });
        m.ShellSections.Add(new ExportShellSection { Thickness = 0.2, Material = concrete });

        // A 3 x 3 floor at z = 3 with the middle bay left out, half of it wound the other way.
        for (int i = 0; i < 3; i++)
            for (int j = 0; j < 3; j++)
            {
                if (i == 1 && j == 1) continue;
                var c = new[] { new Point3d(i, j, 3), new Point3d(i + 1, j, 3), new Point3d(i + 1, j + 1, 3), new Point3d(i, j + 1, 3) };
                Shell(m, 0, (i + j) % 2 == 0 ? c : c.Reverse().ToArray());
            }
        // A wall under the edge y = 0, in three panels.
        for (int i = 0; i < 3; i++)
            Shell(m, 0, new Point3d(i, 0, 0), new Point3d(i + 1, 0, 0), new Point3d(i + 1, 0, 3), new Point3d(i, 0, 3));
        // A warped quad, off to one side.
        Shell(m, 0, new Point3d(10, 0, 0), new Point3d(11, 0, 0), new Point3d(11, 1, 0.2), new Point3d(10, 1, 0));
        // Pressure on the floor's first row of bays.
        var live = new ExportLoadCase { Name = "Live" };
        for (int s = 0; s < 3; s++)
            live.ShellLoads.Add(new ExportShellLoad { Shell = s, Pressure = new Vector3d(0, 0, -2) });
        m.LoadCases.Add(live);

        var (root, result) = Export("shells", m);
        var slabs = All(root, "slab").ToList();
        var plates = slabs.Where(s => (string)s.Attribute("type") == "plate").ToList();
        var walls = slabs.Where(s => (string)s.Attribute("type") == "wall").ToList();
        Check("the floor is one plate, the wall one wall, the warped quad two plates", plates.Count == 3 && walls.Count == 1, $"{plates.Count} plates, {walls.Count} walls");

        var floor = plates.FirstOrDefault(p => p.Descendants(Fd + "contour").Count() == 2);
        Check("the floor keeps the missing bay as a hole", floor != null);
        if (floor != null)
        {
            var contours = floor.Descendants(Fd + "contour").ToList();
            // Outline: the corners, plus the wall's panel joints along y = 0, which FEM-Design needs to join them.
            Check("its outline keeps only its corners and the points the wall joins it at", contours[0].Elements(Fd + "edge").Count() == 6, $"{contours[0].Elements(Fd + "edge").Count()} edges");
            Check("its hole is the bay's four edges", contours[1].Elements(Fd + "edge").Count() == 4);
            var localZ = Vector3d.CrossProduct(Vec(floor.Descendants(Fd + "local_x").First()), Vec(floor.Descendants(Fd + "local_y").First()));
            Check("its normal points up", Same(localZ, Vector3d.ZAxis), localZ.ToString());
        }
        Check("the warped quad is reported", result.Report.Entries.Any(e => e.Message.Contains("warped")), Messages(result));

        var loads = All(root, "surface_load").ToList();
        Check("the pressure on three bays is one surface load", loads.Count == 1, $"{loads.Count}");
        if (loads.Count == 1)
        {
            var edges = loads[0].Descendants(Fd + "edge").Count();
            Check("... over their outline, a rectangle", edges >= 4, $"{edges} edges");
            Check("... of 2 kN/m² downwards", D(loads[0].Element(Fd + "load"), "val") == 2 && Same(Vec(loads[0].Element(Fd + "direction")), -Vector3d.ZAxis));
        }
    }

    /// <summary>Openings that touch at a corner, duplicate elements, a section without thickness.</summary>
    static void ShellEdgeCases()
    {
        Console.WriteLine("\nshell edge cases");
        var m = new ExportModel();
        int concrete = AddMaterial(m, new ExportMaterial { Name = "C30_37", E = 3.0e7, G = 1.6e7, Nu = 0.2, Rho = 2500 });
        m.ShellSections.Add(new ExportShellSection { Thickness = 0.2, Material = concrete });
        m.ShellSections.Add(new ExportShellSection { Thickness = 0.0, Material = concrete });

        // A 3 x 3 floor without bays (1,1) and (2,2), which touch at (2,2); the first face starts there.
        Shell(m, 0, new Point3d(2, 2, 0), new Point3d(2, 3, 0), new Point3d(1, 3, 0), new Point3d(1, 2, 0));
        for (int i = 0; i < 3; i++)
            for (int j = 0; j < 3; j++)
                if (!(i == 1 && j == 1) && !(i == 2 && j == 2) && !(i == 1 && j == 2))
                    Shell(m, 0, new Point3d(i, j, 0), new Point3d(i + 1, j, 0), new Point3d(i + 1, j + 1, 0), new Point3d(i, j + 1, 0));
        // Two elements on the same corners and a neighbour, off to one side, at z = 5.
        Shell(m, 0, new Point3d(0, 0, 5), new Point3d(1, 0, 5), new Point3d(1, 1, 5), new Point3d(0, 1, 5));
        Shell(m, 0, new Point3d(0, 0, 5), new Point3d(1, 0, 5), new Point3d(1, 1, 5), new Point3d(0, 1, 5));
        Shell(m, 0, new Point3d(1, 0, 5), new Point3d(2, 0, 5), new Point3d(2, 1, 5), new Point3d(1, 1, 5));
        // A section without thickness.
        Shell(m, 1, new Point3d(10, 0, 0), new Point3d(11, 0, 0), new Point3d(11, 1, 0), new Point3d(10, 1, 0));
        // Pressure on all of them, as a load without an element gives.
        var live = new ExportLoadCase { Name = "Live" };
        for (int s = 0; s < m.Shells.Count; s++)
            live.ShellLoads.Add(new ExportShellLoad { Shell = s, Pressure = new Vector3d(0, 0, -1) });
        m.LoadCases.Add(live);

        var (root, result) = Export("shell-edge-cases", m);
        var plates = All(root, "slab").ToList();
        var level0 = plates.Where(p => p.Descendants(Fd + "point").All(x => D(x, "z") == 0)).ToList();
        var level5 = plates.Where(p => p.Descendants(Fd + "point").All(x => D(x, "z") == 5)).ToList();
        Check("bays touching at a corner make one plate per element, never one that covers a bay", level0.Count == 7 && level0.All(p => p.Descendants(Fd + "contour").Count() == 1),
            $"{level0.Count} plates at z = 0");
        Check("... with a warning", result.Report.Entries.Any(e => e.Level == ReportLevel.Warning && e.Message.Contains("touches itself")), Messages(result));
        Check("a duplicate element goes in once, its neighbour with it in one plate", level5.Count == 1 && level5[0].Descendants(Fd + "edge").Count() == 4, $"{level5.Count} plates at z = 5");
        Check("... with a warning", result.Report.Entries.Any(e => e.Level == ReportLevel.Warning && e.Message.Contains("same corners")));
        Check("a section without thickness is an error, not a plate", plates.Count == 8 && result.Report.Entries.Any(e => e.Level == ReportLevel.Error && e.Message.Contains("thickness")));

        var loads = All(root, "surface_load").ToList();
        double area = loads.Sum(l =>
        {
            var pts = l.Descendants(Fd + "edge").Select(e => e.Element(Fd + "point")).Select(x => (X: D(x, "x"), Y: D(x, "y"))).ToList();
            double a = 0;
            for (int i = 0; i < pts.Count; i++) a += pts[i].X * pts[(i + 1) % pts.Count].Y - pts[(i + 1) % pts.Count].X * pts[i].Y;
            return Math.Abs(a / 2);
        });
        Check("the pressure covers the 7 + 2 bays loaded, not the missing ones", Near(area, 9.0), $"{loads.Count} loads over {area} m2");
    }

    /// <summary>Springs, rigid links, Equal DOF, diaphragms and masses.</summary>
    static void Connections()
    {
        Console.WriteLine("\nconnections and masses");
        var m = new ExportModel();
        var link = new ExportLink { Id = 1, Start = new Point3d(0, 0, 0), End = new Point3d(1, 0, 0), LocalX = Vector3d.XAxis, LocalY = Vector3d.YAxis };
        link.Stiffness[0] = 5000; link.Stiffness[5] = 200;
        m.Links.Add(link);
        m.RigidLinks.Add(new RigidLink(new Point3d(0, 0, 0), new Point3d(0, 0, 1), RigidLinkType.bar));
        m.EqualDofs.Add(new EqualDOF(new Point3d(2, 0, 0), new Point3d(2, 0, 0.001), true, true, true, false, false, false));
        m.RigidDiaphragms.Add(new RigidDiaphragm(new List<Point3d> { new Point3d(0, 0, 3), new Point3d(4, 0, 3), new Point3d(4, 3, 3), new Point3d(0, 3, 3), new Point3d(2, 1, 3) }));
        m.Masses.Add(new MassLoad(new Point3d(0, 0, 0), new Vector3d(500, 500, 500), Vector3d.Zero));
        m.Masses.Add(new MassLoad(new Point3d(2, 1.4, 3), new Vector3d(800, 800, 800), Vector3d.Zero));   // the diaphragm's retained node, at its nodes' centroid
        m.Masses.Add(new MassLoad(new Point3d(1, 0, 0), new Vector3d(500, 0, 0), Vector3d.Zero));

        var (root, result) = Export("connections", m);
        var connections = All(root, "connected_points").ToList();
        Check("a spring and a rigid link become two connections; Equal DOF at one point does not", connections.Count == 2, $"{connections.Count}");
        if (connections.Count == 2)
        {
            var spring = connections[0].Descendants(Fd + "springs").First();
            Check("the spring has its stiffness along x and about z, nothing else",
                D(spring, "Kx_pos") == 5000 && D(spring, "Ky_pos") == 0 && D(spring, "Cz_neg") == 200 && D(spring, "Cx_pos") == 0, spring.ToString());
            var bar = connections[1].Descendants(Fd + "springs").First();
            Check("a bar rigid link ties translations, not rotations", D(bar, "Kz_pos") == 1e10 && D(bar, "Cx_pos") == 0, bar.ToString());
        }
        Check("Equal DOF at one point is reported", result.Report.Entries.Any(e => e.Level == ReportLevel.Warning && e.Message.Contains("Equal DOF")), Messages(result));

        var diaphragm = All(root, "diaphragm").SingleOrDefault();
        Check("the diaphragm covers the hull of its nodes, leaving out the inner one", diaphragm != null && diaphragm.Descendants(Fd + "edge").Count() == 4);
        var masses = All(root, "mass").ToList();
        Check("masses are in tonnes", masses.Select(x => D(x, "value")).OrderBy(x => x).SequenceEqual(new[] { 0.5, 0.5, 0.8 }), string.Join(" ", masses.Select(x => (string)x.Attribute("value"))));
        Check("a mass on a diaphragm's retained node is reported", result.Report.Entries.Any(e => e.Message.Contains("retained node")), Messages(result));
        Check("a mass along one direction only is reported", result.Report.Entries.Any(e => e.Message.Contains("every direction")));
    }

    /// <summary>One load case per pattern, gravity as a dead load, one combination with the factors.</summary>
    static void Loads()
    {
        Console.WriteLine("\nloads");
        var m = new ExportModel();
        int steel = AddMaterial(m, Steel());
        int hea = AddSection(m, Hea200(steel));
        Beam(m, new Point3d(0, 0, 0), new Point3d(6, 0, 0), hea);
        Beam(m, new Point3d(6, 0, 0), new Point3d(12, 0, 0), hea);

        var live = new ExportLoadCase { Name = "Live", Factor = 1.5 };
        live.PointLoads.Add(new PointLoad(new Point3d(6, 0, 0), new Vector3d(0, 0, -10), new Vector3d(0, 3, 0), null));
        live.BeamLoads.Add(new ExportBeamLoad { Beam = 0, Force = new Vector3d(0, 0, -2) });
        live.BeamLoads.Add(new ExportBeamLoad { Beam = 0, Force = new Vector3d(0, 0, -3) });
        m.LoadCases.Add(live);
        m.LoadCases.Add(new ExportLoadCase { Factor = 1.0, GravityFactor = 1.35 });
        var wind = new ExportLoadCase { Name = "Live", Factor = 0.9 };   // a second pattern called Live
        var gusts = new ExportLoadCase { Name = "Wind @ 0deg <gusts>", Factor = 1.0 };
        gusts.PointLoads.Add(new PointLoad(new Point3d(12, 0, 0), new Vector3d(1, 0, 0), Vector3d.Zero, null));
        wind.BeamLoads.Add(new ExportBeamLoad { Beam = 1, Force = new Vector3d(0, 1, 0) });
        m.LoadCases.Add(wind);
        m.LoadCases.Add(gusts);
        m.LoadCases.Add(new ExportLoadCase { Name = "Empty" });

        var (root, result) = Export("loads", m);
        var cases = root.Element(Fd + "entities").Element(Fd + "loads").Elements(Fd + "load_case").ToList();
        var names = cases.Select(c => (string)c.Attribute("name")).ToList();
        Check("a load case per pattern with loads, then the self-weight", names.SequenceEqual(new[] { "Live", "Live (2)", "Wind _ 0deg _gusts_", "Self-weight" }), string.Join(", ", names));
        Check("... the self-weight a dead-load case", (string)cases.Last().Attribute("type") == "dead_load");
        Check("the result lists them", result.LoadCases.SequenceEqual(names));

        var points = All(root, "point_load").ToList();
        Check("a point load's force and moment are two loads", points.Count == 3 && points.Any(p => (string)p.Attribute("load_type") == "moment"), $"{points.Count}");
        var lines = All(root, "line_load").ToList();
        Check("two loads on one beam are one line load of their sum", lines.Count == 2 && lines.Any(l => l.Elements(Fd + "load").All(x => D(x, "val") == 5)), $"{lines.Count}");

        var combination = All(root, "load_combination").SingleOrDefault();
        var factors = combination?.Elements(Fd + "load_case").Select(c => (Guid: (string)c.Attribute("guid"), Gamma: D(c, "gamma"))).ToList();
        Check("one combination, Alpaca", combination != null && (string)combination.Attribute("name") == "Alpaca");
        if (factors != null)
            Check("... with the patterns' factors, and gravity's factor times its pattern's on the self-weight",
                factors.Select(f => f.Gamma).SequenceEqual(new[] { 1.5, 0.9, 1.0, 1.35 })
                && factors.Select(f => f.Guid).SequenceEqual(cases.Select(c => (string)c.Attribute("guid"))), string.Join(", ", factors.Select(f => f.Gamma)));
        Check("the empty pattern is remarked", result.Report.Entries.Any(e => e.Message.Contains("without loads")));
    }

    /// <summary>Each country's library, and what the component accepts for one.</summary>
    static void Countries()
    {
        Console.WriteLine("\ncountries");
        foreach (var code in new[] { "common", "S", "N", "DK", "FIN", "H", "LV" })
        {
            FemDesignExport.TryParseCountry(code, out var country);
            var m = new ExportModel();
            int steel = AddMaterial(m, Steel());
            int concrete = AddMaterial(m, new ExportMaterial { Name = "C30_37", E = 3.3e7, G = 1.375e7, Nu = 0.2, Rho = 2500 });
            Beam(m, new Point3d(0, 0, 0), new Point3d(5, 0, 0), AddSection(m, Hea200(steel)));
            Beam(m, new Point3d(0, 1, 0), new Point3d(5, 1, 0), AddSection(m, new ExportBeamSection { Shape = ExportSectionShape.Rectangle, Material = concrete, Width = 0.3, Height = 0.5 }));
            var (root, _) = Export("country-" + code, m, country);
            var materials = All(root, "material").ToList();
            Check($"{code}: S355 and C30/37 come from the library", materials.Count == 2 && materials.All(x => x.Element(Fd + "custom") == null),
                string.Join(", ", materials.Select(x => (string)x.Attribute("name"))));
        }
        Check("an unknown country is refused", !FemDesignExport.TryParseCountry("GB", out _));
        Check("no country is common", FemDesignExport.TryParseCountry("", out var none) && none == Eurocodetype.Common);
    }
}
