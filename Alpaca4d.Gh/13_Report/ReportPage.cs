using System;
using System.Drawing;
using System.Linq;

using Grasshopper.Kernel;
using Newtonsoft.Json.Linq;

using Alpaca4d.Report;
using Alpaca4d.UIWidgets;

namespace Alpaca4d.Gh
{
    /// <summary>
    /// The page of the report: its paper, which way round, its margins, and the title block drawn on
    /// every page. The content stays inside the margins; the title block goes in them.
    /// </summary>
    public class ReportPage : GH_ExtendableComponent
    {
        public ReportPage()
          : base("Report Page (Alpaca4d)", "ReportPage",
            "The page of the report: the paper - A4 to A0, Letter, Legal or a size of your own - which way round, the margins, and the " +
            "title block drawn on every page. The content stays inside the margins, so leave room in them for the title block.",
            "Alpaca4d", "13_Report")
        {
            this.Message = Alpaca4d.Gh.ComponentMessage.MyMessage(this);
        }

        private const string Custom = "Custom";

        private MenuDropDown _ddPaper, _ddOrientation;

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddNumberParameter("Width", "Width", "The paper's width in mm, with Paper set to Custom.", GH_ParamAccess.item, 210.0);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddNumberParameter("Height", "Height", "The paper's height in mm, with Paper set to Custom.", GH_ParamAccess.item, 297.0);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddNumberParameter("Top", "Top", "The top margin in mm: room for the top of the title block.", GH_ParamAccess.item, 24.0);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddNumberParameter("Bottom", "Bottom", "The bottom margin in mm: room for the bottom of the title block.", GH_ParamAccess.item, 22.0);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddNumberParameter("Left", "Left", "The left margin in mm.", GH_ParamAccess.item, 20.0);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddNumberParameter("Right", "Right", "The right margin in mm.", GH_ParamAccess.item, 20.0);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddGenericParameter("TitleBlock", "TitleBlock",
                "What is drawn on every page, from Report Title Block. Left empty, a plain header and footer: the title and the page number.", GH_ParamAccess.item);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddBooleanParameter("Numbered", "Numbered",
                "True to number the headings as they come - 1, 1.1, 1.1.1 - before their text; False for headings as they are typed.", GH_ParamAccess.item, true);
            pManager[pManager.ParamCount - 1].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Page", "Page", "The page, for the Page input of the Report.", GH_ParamAccess.item);
        }

        protected override void Setup(GH_ExtendableComponentAttributes attr)
        {
            var menu = new GH_ExtendableMenu(0, "Paper") { Name = "Paper", Header = "The paper, and which way round" };
            var panel = new MenuPanel(0, "paper_panel");
            _ddPaper = new MenuDropDown(0, "ReportPaper", "Paper") { VisibleItemCount = 8 };
            foreach (var name in PageSizes.Names.Concat(new[] { Custom }))
                _ddPaper.AddItem(name, name);
            _ddOrientation = new MenuDropDown(1, "ReportOrientation", "Orientation") { VisibleItemCount = 2 };
            _ddOrientation.AddItem("Portrait", "Portrait");
            _ddOrientation.AddItem("Landscape", "Landscape");
            panel.AddControl(new MenuStaticText { Text = "Paper" });
            panel.AddControl(_ddPaper);
            panel.AddControl(new MenuStaticText { Text = "Orientation" });
            panel.AddControl(_ddOrientation);
            menu.AddControl(panel);
            menu.Expand();
            attr.AddMenu(menu);
            attr.MinWidth = 150f;

            _ddPaper.ValueChanged += (s, e) => ExpireSolution(true);
            _ddOrientation.ValueChanged += (s, e) => ExpireSolution(true);
        }

        private static string Item(MenuDropDown dropdown) =>
            dropdown.Items[Math.Max(0, Math.Min(dropdown.Items.Count - 1, dropdown.Value))].name;

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            double width = 210, height = 297, top = 24, bottom = 22, left = 20, right = 20;
            DA.GetData(0, ref width);
            DA.GetData(1, ref height);
            DA.GetData(2, ref top);
            DA.GetData(3, ref bottom);
            DA.GetData(4, ref left);
            DA.GetData(5, ref right);

            string paper = Item(_ddPaper);
            var json = new JObject();
            if (paper == Custom)
            {
                if (!(width > 0) || !(height > 0))
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Width and Height must be more than 0 mm.");
                    return;
                }
                json["width"] = width;
                json["height"] = height;
            }
            else
            {
                json["page"] = paper;
            }
            json["orientation"] = Item(_ddOrientation).ToLowerInvariant();

            if (new[] { top, bottom, left, right }.Any(m => m < 0))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "A margin cannot be less than 0 mm.");
                return;
            }
            json["margins"] = new JObject { ["top"] = top, ["bottom"] = bottom, ["left"] = left, ["right"] = right };

            // The title block's own settings - the drawing, and the title and the rest it fills in - go with the page.
            object titleBlock = null;
            if (DA.GetData(6, ref titleBlock) && titleBlock != null)
            {
                var part = (titleBlock as GH_ReportPart)?.Value ?? (titleBlock as ReportPart);
                if (part == null && titleBlock is Grasshopper.Kernel.Types.GH_String text)
                {
                    var goo = new GH_ReportPart();
                    if (goo.CastFrom(text.Value)) part = goo.Value;
                }
                if (part == null)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "TitleBlock takes what Report Title Block hands out.");
                    return;
                }
                foreach (var property in part.Json.Properties())
                    json[property.Name] = property.Value.DeepClone();
            }

            bool numbered = true;
            DA.GetData(7, ref numbered);
            json["numbered"] = numbered;

            DA.SetData(0, new GH_ReportPart(new ReportPart { Json = json, IsPage = true }));
        }

        public override GH_Exposure Exposure => GH_Exposure.primary;

        protected override Bitmap Icon => Alpaca4d.Gh.Properties.Resources.Report_Page__Alpaca4d_;

        public override Guid ComponentGuid => new Guid("{457F5896-898C-4A1B-A0DE-93D0AA0B202A}");
    }
}
