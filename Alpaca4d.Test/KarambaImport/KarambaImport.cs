// Checks the Karamba-free half of Karamba3DToAlpaca4D: how ImportModelBuilder decides on a beam
// section, turns the source's axes into Alpaca's, groups its messages and picks a node tolerance.
// Building whole models needs Rhino running (curves, meshes, RTrees), so that half is checked in
// Grasshopper against Karamba3D itself - see README.md.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Rhino.Geometry;
using Alpaca4d.Interop;
using Alpaca4d.Material;
using Alpaca4d.Section;

class KarambaImport
{
    const double E = 2.1e8, G = 8.1e7;   // kN/m2
    static int fails = 0;

    static void Check(string what, bool ok, string detail = "")
    {
        Console.WriteLine($"  [{(ok ? "PASS" : "FAIL")}] {what}{(detail.Length > 0 ? "  " + detail : "")}");
        if (!ok) fails++;
    }

    static bool Near(double a, double b, double tol = 1e-9) => Math.Abs(a - b) <= tol * Math.Max(1.0, Math.Abs(b));

    static ImportMaterial Steel() =>
        new ImportMaterial { Name = "S235", E = E, G = G, Nu = E / (2 * G) - 1, SpecificWeight = 78.5, Density = 7850 };

    /// <summary>A rectangle as Karamba3D describes it: depth h along local z, width b along local y.</summary>
    static ImportBeamSection Rectangle(double b, double h)
    {
        // J with Alpaca's own formula, so that only the deliberate changes below make a difference.
        double a = Math.Max(b, h), c = Math.Min(b, h);
        double j = a * c * c * c / (3.0 + 4.1 * Math.Pow(c / a, 1.5));
        return new ImportBeamSection
        {
            Name = "R" + (b * 1000) + "x" + (h * 1000),
            Material = Steel(),
            Shape = ImportSectionShape.Rectangle,
            ShapeName = "Trapezoid",
            Width = b, Height = h,
            A = b * h, Ay = b * h * 5 / 6, Az = b * h * 5 / 6,
            Iyy = b * h * h * h / 12, Izz = h * b * b * b / 12, J = j,
        };
    }

    static void Main()
    {
        Console.WriteLine("Karamba3DToAlpaca4D: section planning, axes, report, tolerance\n");

        // --- a rectangle the same in both programs keeps its shape --------------------------
        var report = new ConversionReport();
        var rect = Rectangle(0.2, 0.4);
        var shape = ImportModelBuilder.PlanSection(rect, report);
        Check("matching rectangle stays a rectangle", shape == ImportSectionShape.Rectangle, $"got {shape}");
        Check("... with nothing to report but a remark at most", !report.HasErrors && report.Entries.Count == 0, string.Join(" | ", report.Lines()));

        // Alpaca's rectangle has its Height along local y: its Izz, about z, is Karamba's Iyy.
        var material = new UniaxialMaterialElastic("steel", E, E, 0.0, G, 0.3, 7850);
        var alpacaRect = (RectangleCS)ImportModelBuilder.CreateSection(rect, ImportSectionShape.Rectangle, material);
        Check("Alpaca Izz is Karamba's Iyy (strong axis)", Near(alpacaRect.Izz, rect.Iyy), $"{alpacaRect.Izz:G6} vs {rect.Iyy:G6}");
        Check("Alpaca Iyy is Karamba's Izz", Near(alpacaRect.Iyy, rect.Izz), $"{alpacaRect.Iyy:G6} vs {rect.Izz:G6}");

        // --- a torsion constant 20% off sends it to a general section, with a warning --------
        report = new ConversionReport();
        var offJ = Rectangle(0.2, 0.4);
        offJ.J *= 1.2;
        shape = ImportModelBuilder.PlanSection(offJ, report);
        Check("J off by 20% falls back to a general section", shape == ImportSectionShape.General, $"got {shape}");
        Check("... with a warning naming J", report.Entries.Count == 1 && report.Entries[0].Level == ReportLevel.Warning && report.Entries[0].Message.Contains("J -16.7%"),
              report.Lines().Count > 0 ? report.Lines()[0] : "(no message)");

        // --- a hollow circle -----------------------------------------------------------------
        report = new ConversionReport();
        double d = 0.2, t = 0.01, di = d - 2 * t;
        double iCircle = Math.PI * (Math.Pow(d, 4) - Math.Pow(di, 4)) / 64;
        var pipe = new ImportBeamSection
        {
            Name = "CHS200x10", Material = Steel(), Shape = ImportSectionShape.Circle, ShapeName = "Circle",
            Diameter = d, WallThickness = t,
            A = Math.PI * (d * d - di * di) / 4, Ay = 0.003, Az = 0.003, Iyy = iCircle, Izz = iCircle, J = 2 * iCircle,
        };
        shape = ImportModelBuilder.PlanSection(pipe, report);
        Check("matching pipe stays a circle", shape == ImportSectionShape.Circle, $"got {shape}  {string.Join(" | ", report.Lines())}");

        // --- a shape Alpaca does not have: general section, warning, Karamba's numbers ---------
        report = new ConversionReport();
        var tee = new ImportBeamSection
        {
            Name = "T200", Material = Steel(), Shape = ImportSectionShape.General, ShapeName = "T-section",
            A = 0.004, Ay = 0.0015, Az = 0.002, Iyy = 3e-5, Izz = 1e-5, J = 2e-7,
        };
        shape = ImportModelBuilder.PlanSection(tee, report);
        Check("T-section becomes a general section", shape == ImportSectionShape.General);
        Check("... with a warning", report.Entries.Count == 1 && report.Entries[0].Level == ReportLevel.Warning, string.Join(" | ", report.Lines()));

        // section Elastic tag E A Iz Iy G J alphaY alphaZ, in Alpaca's axes.
        var general = (ElasticSection)ImportModelBuilder.CreateSection(tee, ImportSectionShape.General, material);
        general.Id = 1;
        string[] tcl = general.WriteTcl().Trim().Split(' ');
        Check("Elastic Iz is Karamba's Iyy", Near(double.Parse(tcl[5], CultureInfo.InvariantCulture), tee.Iyy), general.WriteTcl().Trim());
        Check("Elastic Iy is Karamba's Izz", Near(double.Parse(tcl[6], CultureInfo.InvariantCulture), tee.Izz));
        Check("alphaY belongs to Karamba's Az", Near(double.Parse(tcl[9], CultureInfo.InvariantCulture), tee.Az / tee.A));
        Check("alphaZ belongs to Karamba's Ay", Near(double.Parse(tcl[10], CultureInfo.InvariantCulture), tee.Ay / tee.A));

        // --- no shear area: no shear factors in the deck, rather than zero ---------------------
        report = new ConversionReport();
        tee.Ay = 0;
        tee.Az = 0;
        ImportModelBuilder.PlanSection(tee, report);
        var noShear = (ElasticSection)ImportModelBuilder.CreateSection(tee, ImportSectionShape.General, material);
        noShear.Id = 2;
        string noShearLine = noShear.WriteTcl().Trim();
        Check("zero shear area writes no alphas (OpenSees fails on 0)", noShearLine.Split(' ').Length == 9, noShearLine);
        Check("... and says so in a remark", report.Entries.Any(entry => entry.Level == ReportLevel.Remark && entry.Text.Contains("no shear area")));

        // --- what cannot be built ---------------------------------------------------------------
        report = new ConversionReport();
        var angle = new ImportBeamSection
        {
            Name = "L100", Material = Steel(), Shape = ImportSectionShape.General, ShapeName = "L",
            A = 0.0019, Ay = 0.001, Az = 0.001, Iyy = 1.8e-6, Izz = 1.8e-6, Iyz = 1.0e-6, J = 6e-9,
        };
        Check("an angle (Iyz not zero) cannot be built", ImportModelBuilder.PlanSection(angle, report) == null && report.HasErrors);

        report = new ConversionReport();
        var empty = Rectangle(0.2, 0.4);
        empty.A = 0;
        Check("a section with no area cannot be built", ImportModelBuilder.PlanSection(empty, report) == null && report.HasErrors);

        // --- axes: Alpaca's z is x cross Karamba's z, so Alpaca's y is Karamba's z ----------------
        var zFloor = ImportModelBuilder.AlpacaLocalZ(new Vector3d(5, 0, 0), new Vector3d(0, 0, 1));
        Check("floor beam along X: Alpaca z = (0, -1, 0)", zFloor.EpsilonEquals(new Vector3d(0, -1, 0), 1e-12), zFloor.ToString());
        var yFloor = Vector3d.CrossProduct(zFloor, new Vector3d(1, 0, 0));
        Check("... so Alpaca y = z x x is up, Karamba's z", yFloor.EpsilonEquals(new Vector3d(0, 0, 1), 1e-12), yFloor.ToString());
        // Karamba3D 2.2 gave a column along +Z the local z (-1, 0, 0).
        var zColumn = ImportModelBuilder.AlpacaLocalZ(new Vector3d(0, 0, 3), new Vector3d(-1, 0, 0));
        var yColumn = Vector3d.CrossProduct(zColumn, new Vector3d(0, 0, 1));
        Check("column along Z: Alpaca y is Karamba's z", yColumn.EpsilonEquals(new Vector3d(-1, 0, 0), 1e-12), yColumn.ToString());
        Check("local z along the beam gives no axis", ImportModelBuilder.AlpacaLocalZ(new Vector3d(0, 0, 3), new Vector3d(0, 0, 1)).IsZero);

        // --- one message per problem, naming a few of the places ----------------------------------
        report = new ConversionReport();
        for (int i = 1; i <= 7; i++)
            report.Error("Trusses are not converted yet", "T" + i);
        report.Error("Trusses are not converted yet", "T1");
        report.Remark("A remark");
        Check("seven trusses make one message", report.Entries.Count == 2, report.Entries.Count.ToString());
        Check("... naming five and counting the rest", report.Entries[0].Message == "Trusses are not converted yet (7: T1, T2, T3, T4, T5 and 2 more)", report.Entries[0].Message);
        Check("errors come first in the lines", report.Lines()[0].StartsWith("Error:"), report.Lines()[0]);

        // --- node tolerance --------------------------------------------------------------------------
        var points = new List<Point3d> { new Point3d(0, 0, 0), new Point3d(5, 0, 0), new Point3d(5, 0.006, 0), new Point3d(10, 0, 0) };
        var pair = ImportModelBuilder.ClosestPair(points);
        Check("closest pair found", Near(pair.Item1, 0.006) && pair.Item2 == 1 && pair.Item3 == 2, $"{pair.Item1} between {pair.Item2} and {pair.Item3}");

        Console.WriteLine();
        Console.WriteLine(fails == 0 ? "all checks passed" : $"{fails} check(s) failed");
        Environment.Exit(fails == 0 ? 0 : 1);
    }
}
