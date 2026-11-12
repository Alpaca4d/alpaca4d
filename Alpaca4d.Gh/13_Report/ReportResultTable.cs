using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

using Grasshopper.Kernel;
using Newtonsoft.Json.Linq;

using Alpaca4d.Report;
using Alpaca4d.UIWidgets;

namespace Alpaca4d.Gh
{
    /// <summary>
    /// One of the tables Alpaca4d makes of a model: what it is made of, and what an analysis found -
    /// the displacements and reactions, the natural frequencies and participating masses, the buckling
    /// factors - read out of the model wired to it when the report is written.
    /// </summary>
    public class ReportResultTable : GH_ExtendableComponent
    {
        public ReportResultTable()
          : base("Report Result Table (Alpaca4d)", "ResultTable",
            "One of the tables Alpaca4d makes of a model, picked from the menu: the model's summary, sections, materials and supports; a static " +
            "analysis's displacements and reactions; a natural vibration analysis's frequencies and participating masses; a buckling analysis's " +
            "load factors; a footfall analysis's worst response factors. It holds no model: the Report makes it of its own model of that kind, from its AlpacaModel input.",
            "Alpaca4d", "13_Report")
        {
            this.Message = Alpaca4d.Gh.ComponentMessage.MyMessage(this);
        }

        /// <summary>The tables, by their name in the menu, the name a template gives each, and the kind of model it needs.</summary>
        private static readonly (string Menu, string Name, string Kind)[] Tables =
        {
            ("Model summary", "model.summary", null),
            ("Beam sections", "model.sections", null),
            ("Beam materials", "model.materials", null),
            ("Supports", "model.supports", null),
            ("Displacements", "static.displacements", "static"),
            ("Support reactions", "static.reactions", "static"),
            ("Natural frequencies", "vibration.modes", "vibration"),
            ("Participating mass", "vibration.massRatios", "vibration"),
            ("Buckling factors", "buckling.factors", "buckling"),
            ("Footfall response", "footfall.response", "footfall"),
        };

        private MenuDropDown _ddTable;

        /// <summary>
        /// A checkbox for each thing each table can show, by table: only the picked table's are shown,
        /// so that the menu closes up around them, and each table keeps its own.
        /// </summary>
        private readonly Dictionary<string, List<(string Option, MenuCheckBox Box)>> _columns = new Dictionary<string, List<(string, MenuCheckBox)>>();

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("Modes", "Modes", "For a table of modes: which - all, a range such as 1-6, or 1,3,5. Left empty, all. For Footfall response, how many nodes, the worst first: 10 if left empty.", GH_ParamAccess.item);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddTextParameter("Caption", "Caption", "Written above the table in place of its own caption.", GH_ParamAccess.item);
            pManager[pManager.ParamCount - 1].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Table", "Table", "The table, for the Content of the Report.", GH_ParamAccess.item);
        }

        protected override void Setup(GH_ExtendableComponentAttributes attr)
        {
            var menu = new GH_ExtendableMenu(0, "Table") { Name = "Table", Header = "Which table" };
            var panel = new MenuPanel(0, "table_panel");
            _ddTable = new MenuDropDown(0, "ReportResultTable", "Table") { VisibleItemCount = Tables.Length };
            foreach (var table in Tables)
                _ddTable.AddItem(table.Menu, table.Menu);
            panel.AddControl(_ddTable);
            menu.AddControl(panel);
            menu.Expand();
            attr.AddMenu(menu);

            // What the table shows: a checkbox per column, or per row such as a sum, or the note.
            var shown = new GH_ExtendableMenu(1, "Columns") { Name = "Columns", Header = "What the table shows" };
            var boxes = new MenuPanel(1, "columns_panel");
            int index = 0;
            foreach (var table in Tables)
            {
                var list = new List<(string, MenuCheckBox)>();
                foreach (var option in ReportTables.Options.TryGetValue(table.Name, out var options) ? options : new string[0])
                {
                    var box = new MenuCheckBox(index++, $"ResultTable_{table.Name}_{option}", option) { Active = true };
                    box.ValueChanged += (s, e) => ExpireSolution(true);
                    boxes.AddControl(box);
                    list.Add((option, box));
                }
                _columns[table.Name] = list;
            }
            shown.AddControl(boxes);
            shown.Expand();
            attr.AddMenu(shown);

            attr.MinWidth = 160f;
            _ddTable.ValueChanged += (s, e) =>
            {
                UpdateVisibility();
                ExpireSolution(true);
            };
            UpdateVisibility();
        }

        private (string Menu, string Name, string Kind) Picked() => Tables[Math.Max(0, Math.Min(Tables.Length - 1, _ddTable?.Value ?? 0))];

        /// <summary>Only the picked table's checkboxes are shown.</summary>
        private void UpdateVisibility()
        {
            var picked = Picked().Name;
            foreach (var entry in _columns)
                foreach (var (_, box) in entry.Value)
                    box.Visible = entry.Key == picked;
            Attributes?.ExpireLayout();
            Grasshopper.Instances.ActiveCanvas?.Invalidate();
        }

        protected override void OnComponentLoaded()
        {
            base.OnComponentLoaded();
            UpdateVisibility();
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var table = Picked();
            var json = new JObject { ["table"] = table.Name };

            // Only when something is left out: a table that shows everything shows what a later Alpaca4d adds too.
            if (_columns.TryGetValue(table.Name, out var boxes) && boxes.Any(b => !b.Box.Active))
                json["columns"] = new JArray(boxes.Where(b => b.Box.Active).Select(b => b.Option));
            string modes = null, caption = null;
            if (DA.GetData(0, ref modes) && !string.IsNullOrWhiteSpace(modes)) json["modes"] = modes.Trim();
            if (DA.GetData(1, ref caption) && !string.IsNullOrWhiteSpace(caption)) json["caption"] = caption;

            DA.SetData(0, new GH_ReportPart(new ReportPart { Json = json }));
        }

        public override GH_Exposure Exposure => GH_Exposure.tertiary;

        protected override Bitmap Icon => Alpaca4d.Gh.Properties.Resources.Report_Result_Table__Alpaca4d_;

        public override Guid ComponentGuid => new Guid("{2EB069A1-3335-436E-92FD-0F86FAA75487}");
    }
}
