using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Newtonsoft.Json.Linq;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;

namespace Alpaca4d.Gh
{
    /// <summary>
    /// The title block: your office's frame - its lines, its texts and its logo - drawn on every page
    /// of the report. Kept in a Rhino file or a Grasshopper definition, drawn as it is on the page, and
    /// loaded as it is: the texts keep their place, size and justification, and may hold {page},
    /// {pages}, {title} and {date}.
    /// </summary>
    public class ReportTitleBlock : GH_Component
    {
        public ReportTitleBlock()
          : base("Report Title Block (Alpaca4d)", "TitleBlock",
            "The title block: your frame's lines, texts and logo, drawn on every page of the report, in the margins of Report Page.\n" +
            "Keep it in a Rhino file or a Grasshopper definition, drawn as it is on the page: at 1:1, the page's bottom left corner at the world " +
            "origin - 210 x 297 mm for A4 portrait - and load it as it is. Texts are Rhino text objects or text dots: each keeps its place, its " +
            "height and its justification, and may hold {page}, {pages}, {title} and {date}.",
            "Alpaca4d", "13_Report")
        {
            this.Message = Alpaca4d.Gh.ComponentMessage.MyMessage(this);
        }

        private const double LineSpacing = 1.6;

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddCurveParameter("Curves", "Curves", "The title block's lines: the border, the rules, the boxes.", GH_ParamAccess.list);
            pManager.AddGenericParameter("Text", "Text",
                "The title block's texts: Rhino text objects or text dots, referenced or made in Grasshopper. Each keeps its place, its height and " +
                "its justification. A text may hold {page}, {pages}, {title} and {date}.", GH_ParamAccess.list);
            pManager.AddTextParameter("Logo", "Logo", "A picture file, a PNG or a JPEG: your office's logo.", GH_ParamAccess.item);
            pManager.AddCurveParameter("Logo Frame", "Logo Frame", "The rectangle the logo fills, as it is on the page.", GH_ParamAccess.item);
            for (int i = 0; i < pManager.ParamCount; i++)
                pManager[i].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("TitleBlock", "TitleBlock", "The title block, for the TitleBlock input of Report Page.", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // Drawn as it is on the page: the world origin is the page's bottom left corner, in mm.
            double mm;
            try { mm = RhinoMath.UnitScale(RhinoDoc.ActiveDoc?.ModelUnitSystem ?? UnitSystem.Millimeters, UnitSystem.Millimeters); }
            catch { mm = 1.0; }
            JArray At(Point3d p) => new JArray(Math.Round(p.X * mm, 4), Math.Round(p.Y * mm, 4));

            var items = new JArray();

            var curves = new List<Curve>();
            DA.GetDataList(0, curves);
            foreach (var curve in curves.Where(c => c != null))
            {
                var points = Points(curve, 0.05 / mm);
                if (points != null)
                    items.Add(new JObject { ["line"] = new JArray(points.Select(At)), ["colour"] = "#2A2D31", ["width"] = 0.5 });
            }

            var texts = new List<IGH_Goo>();
            DA.GetDataList(1, texts);
            int unplaced = 0, turned = 0;
            foreach (var goo in texts.Where(t => t != null))
            {
                switch (goo.ScriptVariable())
                {
                    case TextEntity text:
                        if (Math.Abs(text.TextRotationRadians) > 1e-6 || Math.Abs(text.Plane.ZAxis.Z) < 0.999) turned++;
                        AddText(items, text, mm, At);
                        break;
                    case TextDot dot:
                        items.Add(new JObject { ["text"] = dot.Text, ["at"] = At(dot.Point), ["size"] = 3.0, ["align"] = "centre" });
                        break;
                    default:
                        unplaced++;
                        break;
                }
            }
            if (unplaced > 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"{unplaced} of the texts have no place on the page: give Rhino text objects or text dots, which carry where they are.");
            if (turned > 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"{turned} of the texts are turned: they are written level.");

            string logo = null;
            Curve frame = null;
            if (DA.GetData(2, ref logo) && !string.IsNullOrWhiteSpace(logo))
            {
                if (!DA.GetData(3, ref frame) || frame == null)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Give the logo a Logo Frame: the rectangle it fills on the page.");
                }
                else
                {
                    var box = frame.GetBoundingBox(true);
                    items.Add(new JObject
                    {
                        ["image"] = logo.Trim().Trim('"'),
                        ["at"] = At(box.Min),
                        ["size"] = new JArray((box.Max.X - box.Min.X) * mm, (box.Max.Y - box.Min.Y) * mm)
                    });
                }
            }

            var json = new JObject { ["titleBlock"] = new JObject { ["items"] = items } };
            DA.SetData(0, new GH_ReportPart(new ReportPart { Json = json, IsPage = true }));
        }

        /// <summary>
        /// A Rhino text, a line of it at a time, each line's baseline where the text's justification puts
        /// it: Rhino gives the height of the capitals, the page the size of the type, a little larger.
        /// </summary>
        private static void AddText(JArray items, TextEntity text, double mm, Func<Point3d, JArray> at)
        {
            var lines = (text.PlainText ?? "").Replace("\r\n", "\n").Split('\n');
            // The height as it is set, not times the dimension style's model space scale: a title block is
            // drawn at 1:1, and a style's scale - 0.15 in some templates - would make its texts specks.
            double height = text.TextHeight;
            double spacing = LineSpacing * height;
            double block = height + (lines.Length - 1) * spacing;
            var origin = text.Plane.Origin;

            // The first line's baseline.
            double first;
            switch (text.TextVerticalAlignment)
            {
                case TextVerticalAlignment.Top: first = origin.Y - height; break;
                case TextVerticalAlignment.MiddleOfTop: first = origin.Y - height / 2.0; break;
                case TextVerticalAlignment.BottomOfTop: first = origin.Y; break;
                case TextVerticalAlignment.Middle: first = origin.Y + block / 2.0 - height; break;
                case TextVerticalAlignment.MiddleOfBottom: first = origin.Y - height / 2.0 + (lines.Length - 1) * spacing; break;
                default: first = origin.Y + (lines.Length - 1) * spacing; break;
            }

            string align = text.TextHorizontalAlignment == TextHorizontalAlignment.Center ? "centre"
                : text.TextHorizontalAlignment == TextHorizontalAlignment.Right ? "right" : "left";
            // Bold by its font, or made bold all through, as Rhino's Bold button does.
            bool bold = (text.Font?.Bold ?? false) || text.IsAllBold();

            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].Trim().Length == 0) continue;
                items.Add(new JObject
                {
                    ["text"] = lines[i],
                    ["at"] = at(new Point3d(origin.X, first - i * spacing, 0.0)),
                    ["size"] = height * mm / 0.72,
                    ["align"] = align,
                    ["bold"] = bold
                });
            }
        }

        /// <summary>A curve as the points of a polyline close to it; null for one that has none.</summary>
        private static List<Point3d> Points(Curve curve, double tolerance)
        {
            if (curve.TryGetPolyline(out Polyline polyline)) return polyline.ToList();
            var approximated = curve.ToPolyline(tolerance, RhinoMath.ToRadians(2.0), 0.0, 0.0);
            return approximated != null && approximated.TryGetPolyline(out polyline) ? polyline.ToList() : null;
        }

        public override GH_Exposure Exposure => GH_Exposure.primary;

        protected override Bitmap Icon => Alpaca4d.Gh.Properties.Resources.Report_Title_Block__Alpaca4d_;

        public override Guid ComponentGuid => new Guid("{4DE6A7F1-8FC7-41DF-8FDE-3B6584EF094E}");
    }
}
