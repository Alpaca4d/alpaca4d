using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using Rhino.Geometry;

using StruSoft.Interop.StruXml.Data;

namespace Alpaca4d.FemDesign
{
    public sealed partial class StruxmlWriter
    {
        /// <summary>A section in the model, with the complex section a beam or column refers to.</summary>
        private sealed class BarSection
        {
            public Section_type Section;
            public Complex_section_type Complex;
        }

        private readonly Dictionary<int, BarSection> barSections = new Dictionary<int, BarSection>();
        private readonly Dictionary<string, Complex_section_type> complexSections = new Dictionary<string, Complex_section_type>();
        private readonly Dictionary<string, Section_type> customSections = new Dictionary<string, Section_type>();

        /// <summary>
        /// The FEM-Design section of an Alpaca one, added to the model the first time it is asked for;
        /// null for a section known only by A, I and J, which has no outline to draw.
        /// </summary>
        private BarSection SectionOf(int index, MaterialFamily family)
        {
            if (barSections.TryGetValue(index, out var barSection))
                return barSection;

            var section = model.BeamSections[index];
            if (section.Shape == ExportSectionShape.Generic)
                return null;

            var fdSection = LibrarySection(section, family) ?? CustomSection(section, family);
            Add(fdSection, fdSection.Guid);

            if (!complexSections.TryGetValue(fdSection.Guid, out var complex))
            {
                complex = Complex_section_type.Create(fdSection.Guid);
                complexSections[fdSection.Guid] = complex;
                Add(complex, complex.Guid);
            }

            barSection = new BarSection { Section = fdSection, Complex = complex };
            barSections[index] = barSection;
            return barSection;
        }

        // ---------------------------------------------------------------- FEM-Design's library

        /// <summary>
        /// FEM-Design's profile for a catalogue section, so it can be designed: an I by its name, which
        /// Alpaca's Section Library gives it; tubes, hollow rectangles, and concrete and timber rectangles
        /// and circles by their dimensions, which is what FEM-Design names them by. Null when there is none.
        /// </summary>
        private Section_type LibrarySection(ExportBeamSection section, MaterialFamily family)
        {
            Section_type match = null;
            switch (section.Shape)
            {
                case ExportSectionShape.I when family == MaterialFamily.Steel && !string.IsNullOrWhiteSpace(section.Name):
                    match = NamedProfile(section, Math.Max(section.TopWidth, section.BottomWidth), section.Height);
                    break;

                case ExportSectionShape.Circle when family == MaterialFamily.Steel && IsHollow(section):
                    // Cold-formed first: the sizes of Alpaca's catalogue are those of EN 10219.
                    match = SizedProfile("Steel sections", new[] { "KCKR", "CHS" },
                        size => size.Length == 2 && Same(size[0], section.Diameter) && Same(size[1], section.Thickness));
                    break;

                case ExportSectionShape.RectangleHollow when family == MaterialFamily.Steel
                                                           && Equal(section.Web, section.TopFlange) && Equal(section.Web, section.BottomFlange):
                    // FEM-Design names them height x width x wall.
                    match = SizedProfile("Steel sections", new[] { "KKR", "VKR" },
                        size => size.Length == 3 && Same(size[0], section.Height) && Same(size[1], section.Width) && Same(size[2], section.Web));
                    break;

                case ExportSectionShape.Rectangle when family == MaterialFamily.Concrete:
                    // Width x height, and a square by its side.
                    match = SizedProfile("Concrete sections", new[] { "Rectangle" },
                                size => size.Length == 2 && Same(size[0], section.Width) && Same(size[1], section.Height))
                         ?? SizedProfile("Concrete sections", new[] { "Square" },
                                size => size.Length == 1 && Same(size[0], section.Width) && Same(size[0], section.Height));
                    break;

                case ExportSectionShape.Rectangle when family == MaterialFamily.Timber:
                    match = SizedProfile("Timber sections", new[] { "Rectangle" },
                        size => size.Length == 2 && Same(size[0], section.Width) && Same(size[1], section.Height));
                    break;

                case ExportSectionShape.Circle when family == MaterialFamily.Concrete && !IsHollow(section):
                    match = SizedProfile("Concrete sections", new[] { "Circle" },
                        size => size.Length == 1 && Same(size[0], section.Diameter));
                    break;
            }

            if (match != null)
                report.Remark("Exported as FEM-Design's library profile of the same size, so it can be designed there. " +
                              "Its properties take in root and corner radii, so they differ a little from Alpaca's.", match.Name);
            return match;
        }

        /// <summary>The steel profile called by the section's name, if its height and width are the section's.</summary>
        private Section_type NamedProfile(ExportBeamSection section, double width, double height)
        {
            Section_type match;
            try
            {
                match = SectionDatabase.SteelType.GetSectionByName(section.Name);
            }
            catch (Exception e) when (e is KeyNotFoundException || e is InvalidOperationException || e is ArgumentException)
            {
                return null;
            }

            var (libraryWidth, libraryHeight) = Extent(match);
            if (Math.Abs(libraryWidth - width) > 0.01 * width || Math.Abs(libraryHeight - height) > 0.01 * height)
            {
                report.Warning($"FEM-Design's {match.Name} is {Mm(libraryHeight)} x {Mm(libraryWidth)} mm, not the {Mm(height)} x {Mm(width)} mm of the section of that name; " +
                               "it is exported from its outline instead.", section.Name);
                return null;
            }
            return match;
        }

        /// <summary>The first library section of one of <paramref name="types"/>, in order, whose size fits.</summary>
        private static Section_type SizedProfile(string code, string[] types, Func<double[], bool> fits)
        {
            foreach (var type in types)
            {
                foreach (var section in SectionDatabase.All)
                {
                    if (section.Fd_name_code == code && section.Fd_name_type == type && Size(section.Fd_name_size) is double[] size && fits(size))
                        return section;
                }
            }
            return null;
        }

        /// <summary>The numbers in a library size such as "60.3-4.0", "40x20x2" or "D 120", in mm; null when it has other text.</summary>
        private static double[] Size(string size)
        {
            if (string.IsNullOrWhiteSpace(size))
                return null;
            var parts = size.Trim().TrimStart('D', ' ').Split(new[] { 'x', '-' }, StringSplitOptions.RemoveEmptyEntries);
            var numbers = new double[parts.Length];
            for (int i = 0; i < parts.Length; i++)
                if (!double.TryParse(parts[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out numbers[i]))
                    return null;
            return numbers;
        }

        /// <summary>Whether a library dimension in mm is an Alpaca one in m.</summary>
        private static bool Same(double mm, double metres) => Math.Abs(mm - metres * 1000.0) <= 0.01;

        /// <summary>Whether two Alpaca dimensions are the same, to a thousandth of a millimetre.</summary>
        private static bool Equal(double a, double b) => Math.Abs(a - b) <= 1e-6;

        private static bool IsHollow(ExportBeamSection circle) => circle.Thickness > 0 && circle.Thickness < circle.Diameter / 2.0;

        /// <summary>The width and height of a section's outline [m].</summary>
        private static (double Width, double Height) Extent(Section_type section)
        {
            double minX = double.MaxValue, maxX = double.MinValue, minY = double.MaxValue, maxY = double.MinValue;
            foreach (var edge in section.Region_group.SelectMany(r => r.Contour).SelectMany(c => c.Edge))
            {
                double r = edge.Type == Edgetype.Circle ? edge.Radius ?? 0.0 : 0.0;
                foreach (var p in edge.Point)
                {
                    minX = Math.Min(minX, p.X - r); maxX = Math.Max(maxX, p.X + r);
                    minY = Math.Min(minY, p.Y - r); maxY = Math.Max(maxY, p.Y + r);
                }
            }
            return (maxX - minX, maxY - minY);
        }

        // ---------------------------------------------------------------- outlines

        /// <summary>
        /// A section drawn from its outline, from which FEM-Design works out the properties. FEM-Design
        /// draws a section in its local y-z plane with z along the depth, which is Alpaca's local y, so the
        /// outline goes in as Alpaca draws it, mirrored across the depth.
        /// </summary>
        private Section_type CustomSection(ExportBeamSection section, MaterialFamily family)
        {
            string type, size;
            var regions = new List<Region_type>();
            double w = section.Width, h = section.Height;

            switch (section.Shape)
            {
                case ExportSectionShape.Rectangle:
                    type = "Rectangle";
                    size = $"{Mm(w)}x{Mm(h)}";
                    regions.Add(Region(Rectangle(-w / 2, -h / 2, w / 2, h / 2)));
                    break;

                case ExportSectionShape.Circle:
                    double radius = section.Diameter / 2.0;
                    if (IsHollow(section))
                    {
                        type = "Tube";
                        size = $"{Mm(section.Diameter)}x{Mm(section.Thickness)}";
                        regions.Add(Region_type.FromContours(Circle(radius, outer: true), new[] { Circle(radius - section.Thickness, outer: false) }));
                    }
                    else
                    {
                        type = "Circle";
                        size = Mm(section.Diameter);
                        regions.Add(Region_type.FromContours(Circle(radius, outer: true)));
                    }
                    break;

                case ExportSectionShape.RectangleHollow:
                    type = "Hollow rectangle";
                    size = $"{Mm(h)}x{Mm(w)}x{Mm(section.Web)}";
                    if (!Equal(section.TopFlange, section.Web) || !Equal(section.BottomFlange, section.Web))
                        size += $"x{Mm(section.TopFlange)}x{Mm(section.BottomFlange)}";
                    regions.Add(Region(Rectangle(-w / 2, -h / 2, w / 2, h / 2),
                                       Rectangle(-w / 2 + section.Web, -h / 2 + section.BottomFlange, w / 2 - section.Web, h / 2 - section.TopFlange)));
                    break;

                case ExportSectionShape.I:
                    type = "I";
                    size = $"{Mm(h)}x{Mm(Math.Max(section.TopWidth, section.BottomWidth))}x{Mm(section.Web)}x{Mm(section.TopFlange)}";
                    if (!Equal(section.TopWidth, section.BottomWidth) || !Equal(section.TopFlange, section.BottomFlange))
                        size = $"{Mm(h)}x{Mm(section.TopWidth)}x{Mm(section.TopFlange)}x{Mm(section.BottomWidth)}x{Mm(section.BottomFlange)}x{Mm(section.Web)}";
                    regions.Add(Region(IShape(section)));
                    break;

                case ExportSectionShape.DoubleAngle:
                    type = "Double angle";
                    size = $"{Mm(h)}x{Mm(w)}x{Mm(section.Thickness)}-{Mm(section.Gap)}";
                    foreach (var side in new[] { 1.0, -1.0 })
                        regions.Add(Region(Angle(section, side)));
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(section), section.Shape, "A generic section has no outline.");
            }

            var material = family switch
            {
                MaterialFamily.Steel => section.Shape switch
                {
                    ExportSectionShape.I => Fd_mat_type.SteelWelded,
                    ExportSectionShape.RectangleHollow => Fd_mat_type.SteelColdWorked,
                    ExportSectionShape.Circle when IsHollow(section) => Fd_mat_type.SteelColdWorked,
                    _ => Fd_mat_type.SteelRolled,
                },
                MaterialFamily.Concrete => Fd_mat_type.Concrete,
                MaterialFamily.Timber => Fd_mat_type.Timber,
                _ => Fd_mat_type.Undefined,
            };

            // Alpaca makes a section per element when asked to; FEM-Design needs it once.
            string key = $"{type}|{size}|{material}";
            if (customSections.TryGetValue(key, out var existing))
                return existing;

            var created = Section_type.Create(regions, material, "Alpaca", type, size);
            customSections[key] = created;
            return created;
        }

        /// <summary>Corners from (x0, y0) to (x1, y1), in Alpaca's section plane: x along local z, y along local y.</summary>
        private static List<Point3d> Rectangle(double x0, double y0, double x1, double y1)
            => new List<Point3d> { new Point3d(x0, y0, 0), new Point3d(x1, y0, 0), new Point3d(x1, y1, 0), new Point3d(x0, y1, 0) };

        /// <summary>The outline Alpaca's ISection draws.</summary>
        private static List<Point3d> IShape(ExportBeamSection s)
        {
            double h = s.Height / 2, web = s.Web / 2, top = s.TopWidth / 2, bottom = s.BottomWidth / 2;
            return new List<Point3d>
            {
                new Point3d(web, -h + s.BottomFlange, 0), new Point3d(web, h - s.TopFlange, 0),
                new Point3d(top, h - s.TopFlange, 0), new Point3d(top, h, 0),
                new Point3d(-top, h, 0), new Point3d(-top, h - s.TopFlange, 0),
                new Point3d(-web, h - s.TopFlange, 0), new Point3d(-web, -h + s.BottomFlange, 0),
                new Point3d(-bottom, -h + s.BottomFlange, 0), new Point3d(-bottom, -h, 0),
                new Point3d(bottom, -h, 0), new Point3d(bottom, -h + s.BottomFlange, 0),
            };
        }

        /// <summary>One angle of Alpaca's DoubleLAngleCS: the one on the positive side of the gap, or its mirror.</summary>
        private static List<Point3d> Angle(ExportBeamSection s, double side)
        {
            double g = s.Gap / 2, h = s.Height / 2, t = s.Thickness;
            return new List<Point3d>
            {
                new Point3d(side * g, -h, 0), new Point3d(side * g, h, 0),
                new Point3d(side * (g + t), h, 0), new Point3d(side * (g + t), -h + t, 0),
                new Point3d(side * (g + s.Width), -h + t, 0), new Point3d(side * (g + s.Width), -h, 0),
            };
        }

        /// <summary>
        /// A region of FEM-Design's section plane from outlines in Alpaca's: mirrored across the depth, the
        /// outer one wound anticlockwise and the holes clockwise, as FEM-Design's own sections are.
        /// </summary>
        private static Region_type Region(List<Point3d> outer, params List<Point3d>[] holes)
        {
            List<Point_type_3d> InSectionPlane(List<Point3d> outline, bool anticlockwise)
            {
                var mirrored = outline.Select(p => new Point3d(-p.X, p.Y, 0)).ToList();
                if (SignedArea(mirrored) > 0 != anticlockwise)
                    mirrored.Reverse();
                return mirrored.Select(P).ToList();
            }

            return Region_type.FromPointLoops(InSectionPlane(outer, true), holes.Select(hole => (IEnumerable<Point_type_3d>)InSectionPlane(hole, false)));
        }

        private static double SignedArea(IList<Point3d> polygon)
        {
            double area = 0.0;
            for (int i = 0; i < polygon.Count; i++)
            {
                var a = polygon[i];
                var b = polygon[(i + 1) % polygon.Count];
                area += a.X * b.Y - b.X * a.Y;
            }
            return area / 2.0;
        }

        /// <summary>A circle about the section's origin; a hole's turns the other way, as in FEM-Design's own tubes.</summary>
        private static Contour_type Circle(double radius, bool outer)
        {
            var edge = new Edge_type
            {
                Type = Edgetype.Circle,
                Radius = radius,
                Normal = new Point_type_3d { X = 0, Y = 0, Z = outer ? 1 : -1 },
            };
            edge.Point.Add(new Point_type_3d { X = 0, Y = 0, Z = 0 });
            return Contour_type.FromEdges(new[] { edge });
        }
    }
}
