using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

using Grasshopper.Kernel;
using Newtonsoft.Json.Linq;

using Alpaca4d.UIWidgets;

namespace Alpaca4d.Gh
{
    /// <summary>
    /// Text for the report, in a style picked from its menu: the title and subtitle set large, a
    /// heading, a subheading or a sub-subheading - numbered by the report as they come, 1, 1.1, 1.1.1 -
    /// paragraphs, or a note in small grey type.
    /// </summary>
    public class ReportText : GH_ExtendableComponent
    {
        public ReportText()
          : base("Report Text (Alpaca4d)", "ReportText",
            "Text for the report, in a style from the menu:\n" +
            "Title - the first text set large, the second under it, and the date: the report's title, its {title};\n" +
            "Heading, Subheading and Sub-subheading - a chapter, a section in it and a part of that, numbered by the report as they come: 1, 1.1, 1.1.1. " +
            "Type the text alone: the number is the report's;\n" +
            "Paragraph - each text a paragraph of its own;\n" +
            "Note - small grey type.\n" +
            "Any text may hold placeholders, such as {model.mass:N0}, {vibration.frequency[1]:F2} or {buckling.factor[1]:F2}.",
            "Alpaca4d", "13_Report")
        {
            this.Message = Alpaca4d.Gh.ComponentMessage.MyMessage(this);
        }

        private static readonly string[] Styles = { "Title", "Heading", "Subheading", "Sub-subheading", "Paragraph", "Note" };

        private static readonly int ParagraphStyle = Array.IndexOf(Styles, "Paragraph");

        private MenuDropDown _ddStyle;

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("Text", "Text", "The text: a paragraph each, or for a Title the title and then the subtitle.", GH_ParamAccess.list);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Text", "Text", "The text, for the Content of the Report.", GH_ParamAccess.item);
        }

        protected override void Setup(GH_ExtendableComponentAttributes attr)
        {
            var menu = new GH_ExtendableMenu(0, "Style") { Name = "Style", Header = "How the text is set" };
            var panel = new MenuPanel(0, "style_panel");
            _ddStyle = new MenuDropDown(0, "ReportTextStyle", "Style") { VisibleItemCount = Styles.Length };
            foreach (var style in Styles)
                _ddStyle.AddItem(style, style);
            _ddStyle.Value = ParagraphStyle;
            panel.AddControl(_ddStyle);
            menu.AddControl(panel);
            menu.Expand();
            attr.AddMenu(menu);
            attr.MinWidth = 130f;
            _ddStyle.ValueChanged += (s, e) => ExpireSolution(true);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var texts = new List<string>();
            if (!DA.GetDataList(0, texts)) return;
            texts = texts.Select(t => t ?? "").ToList();

            string style = Styles[Math.Max(0, Math.Min(Styles.Length - 1, _ddStyle?.Value ?? ParagraphStyle))];
            JObject json;
            switch (style)
            {
                case "Title":
                    json = new JObject { ["title"] = texts.FirstOrDefault() ?? "", ["subtitle"] = string.Join("\n", texts.Skip(1)) };
                    break;
                case "Heading":
                    json = new JObject { ["heading"] = string.Join(" ", texts) };
                    break;
                case "Subheading":
                    json = new JObject { ["subheading"] = string.Join(" ", texts) };
                    break;
                case "Sub-subheading":
                    json = new JObject { ["subsubheading"] = string.Join(" ", texts) };
                    break;
                case "Note":
                    json = new JObject { ["note"] = string.Join("\n", texts) };
                    break;
                default:
                    json = new JObject { ["text"] = texts.Count == 1 ? (JToken)texts[0] : new JArray(texts) };
                    break;
            }

            DA.SetData(0, new GH_ReportPart(new ReportPart { Json = json }));
        }

        public override GH_Exposure Exposure => GH_Exposure.secondary;

        protected override Bitmap Icon => Alpaca4d.Gh.Properties.Resources.Report_Text__Alpaca4d_;

        public override Guid ComponentGuid => new Guid("{8E5443D2-9B7D-417D-B2BF-C0CFB264150B}");
    }
}
