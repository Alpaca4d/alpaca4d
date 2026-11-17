using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

using Grasshopper.Kernel;
using Newtonsoft.Json.Linq;

namespace Alpaca4d.Gh
{
    /// <summary>
    /// The report's revisions, as a calculation sent out for checking has them: a row per issue, its
    /// letter, date, what changed, and who wrote, checked and approved it. A table of the report's
    /// own, not numbered among the tables of the calculation.
    /// </summary>
    public class ReportRevisions : GH_Component
    {
        public ReportRevisions()
          : base("Report Revisions (Alpaca4d)", "Revisions",
            "The report's revisions: a row per issue, with its letter, date, what changed, and who wrote, checked and approved it. " +
            "Each input a list, an item per issue, oldest first: a shorter list leaves its last cells empty.\n" +
            "Put it in the Content of the Report after the title, where a calculation's issue record goes. It is not numbered among the tables.",
            "Alpaca4d", "13_Report")
        {
            this.Message = Alpaca4d.Gh.ComponentMessage.MyMessage(this);
        }

        /// <summary>The columns, as the inputs and the table's header name them.</summary>
        private static readonly string[] Columns = { "Revision", "Date", "Description", "By", "Checked", "Approved" };

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("Revision", "Rev", "Each issue's letter or number: A, B, C, or P01, C01.", GH_ParamAccess.list);
            pManager.AddTextParameter("Date", "Date", "When each was issued.", GH_ParamAccess.list);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddTextParameter("Description", "Description", "What each issue is, or what changed in it: First issue, For checking, Beams resized.", GH_ParamAccess.list);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddTextParameter("By", "By", "Who wrote each.", GH_ParamAccess.list);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddTextParameter("Checked", "Checked", "Who checked each.", GH_ParamAccess.list);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddTextParameter("Approved", "Approved", "Who approved each. Left empty everywhere, the column is left out.", GH_ParamAccess.list);
            pManager[pManager.ParamCount - 1].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Table", "Table", "The table of revisions, for the Content of the Report.", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var columns = new List<List<string>>();
            for (int i = 0; i < Columns.Length; i++)
            {
                var values = new List<string>();
                DA.GetDataList(i, values);
                columns.Add(values.Select(v => v ?? "").ToList());
            }
            int rows = columns[0].Count;
            if (rows == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Give each issue its Revision.");
                return;
            }
            for (int i = 1; i < Columns.Length; i++)
                if (columns[i].Count > rows)
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"{Params.Input[i].Name} has {columns[i].Count} items for {rows} revisions: the ones past them are left out.");

            // A column no one filled in is left out, but for the first three.
            var kept = Enumerable.Range(0, Columns.Length).Where(c => c < 3 || columns[c].Any(v => v.Trim().Length > 0)).ToList();
            var table = new JObject
            {
                ["caption"] = "Revisions",
                ["numbered"] = false,
                ["header"] = new JArray(kept.Select(c => Columns[c])),
                ["align"] = new JArray(kept.Select(_ => "left")),
                ["rows"] = new JArray(Enumerable.Range(0, rows).Select(r =>
                    new JArray(kept.Select(c => r < columns[c].Count ? columns[c][r] : ""))))
            };

            DA.SetData(0, new GH_ReportPart(new ReportPart { Json = new JObject { ["table"] = table } }));
        }

        public override GH_Exposure Exposure => GH_Exposure.secondary;

        protected override Bitmap Icon => Alpaca4d.Gh.Properties.Resources.Report_Revisions__Alpaca4d_;

        public override Guid ComponentGuid => new Guid("{B3E14A72-5C9D-4F08-8E61-2A7D90C4F5B3}");
    }
}
