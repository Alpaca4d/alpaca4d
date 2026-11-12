using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;

using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;
using Newtonsoft.Json.Linq;

namespace Alpaca4d.Gh
{
    /// <summary>
    /// A table of your own for the report: a caption, a header and a unit per column, and the values,
    /// a column per input. Zoom in on the component to add or take away columns.
    /// </summary>
    public class ReportTable : GH_Component, IGH_VariableParameterComponent
    {
        public ReportTable()
          : base("Report Table (Alpaca4d)", "ReportTable",
            "A table of your own for the report: a caption, a header and a unit per column, and the values, a column per input - " +
            "zoom in on the component to add or take away columns. Numbers are written with Decimals decimals and lined up on the right.",
            "Alpaca4d", "13_Report")
        {
            this.Message = Alpaca4d.Gh.ComponentMessage.MyMessage(this);
        }

        /// <summary>The inputs that are always there; the columns follow them.</summary>
        private const int FirstColumn = 5;

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("Caption", "Caption", "Written above the table, after its number.", GH_ParamAccess.item);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddTextParameter("Headers", "Headers", "A header per column. Left empty, the column inputs' names.", GH_ParamAccess.list);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddTextParameter("Units", "Units", "A unit per column, written after its header in brackets: Hz for f [Hz]. Empty for none.", GH_ParamAccess.list);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddIntegerParameter("Decimals", "Decimals", "How many decimals a number is written with; -1 for as it is.", GH_ParamAccess.item, 2);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddTextParameter("Note", "Note", "A line of small print under the table.", GH_ParamAccess.item);
            pManager[pManager.ParamCount - 1].Optional = true;
            for (int i = 0; i < 3; i++)
                pManager.AddParameter(NewColumn());
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Table", "Table", "The table, for the Content of the Report.", GH_ParamAccess.item);
        }

        private static IGH_Param NewColumn() => new Param_GenericObject
        {
            Name = "Column",
            NickName = "C",
            Description = "A column's values, top to bottom: numbers or text.",
            Access = GH_ParamAccess.list,
            Optional = true
        };

        public bool CanInsertParameter(GH_ParameterSide side, int index) => side == GH_ParameterSide.Input && index >= FirstColumn;

        public bool CanRemoveParameter(GH_ParameterSide side, int index) => side == GH_ParameterSide.Input && index >= FirstColumn && Params.Input.Count > FirstColumn + 1;

        public IGH_Param CreateParameter(GH_ParameterSide side, int index) => NewColumn();

        public bool DestroyParameter(GH_ParameterSide side, int index) => true;

        public void VariableParameterMaintenance()
        {
            for (int i = FirstColumn; i < Params.Input.Count; i++)
            {
                var input = Params.Input[i];
                int number = i - FirstColumn + 1;
                input.Name = $"Column {number}";
                // A nickname the user gave stays: it is the column's header when Headers is empty.
                if (string.IsNullOrEmpty(input.NickName) || input.NickName == "C" || input.NickName.StartsWith("C") && int.TryParse(input.NickName.Substring(1), out _))
                    input.NickName = $"C{number}";
                input.Description = "A column's values, top to bottom: numbers or text.";
                input.Access = GH_ParamAccess.list;
                input.Optional = true;
            }
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            string caption = null, note = null;
            var headers = new List<string>();
            var units = new List<string>();
            int decimals = 2;
            DA.GetData(0, ref caption);
            DA.GetDataList(1, headers);
            DA.GetDataList(2, units);
            DA.GetData(3, ref decimals);
            DA.GetData(4, ref note);

            var columns = new List<List<string>>();
            for (int i = FirstColumn; i < Params.Input.Count; i++)
            {
                var values = new List<IGH_Goo>();
                DA.GetDataList(i, values);
                columns.Add(values.Select(v => Cell(v, decimals)).ToList());
            }

            int count = columns.Count;
            int rows = columns.Select(c => c.Count).DefaultIfEmpty(0).Max();
            if (rows == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No values: give each column input a list of them.");
                return;
            }

            var header = Enumerable.Range(0, count).Select(c => c < headers.Count ? headers[c] ?? "" : Params.Input[FirstColumn + c].NickName).ToList();
            var json = new JObject
            {
                ["table"] = new JObject
                {
                    ["caption"] = caption ?? "",
                    ["header"] = new JArray(header),
                    ["units"] = new JArray(Enumerable.Range(0, count).Select(c => c < units.Count ? units[c] ?? "" : "")),
                    ["rows"] = new JArray(Enumerable.Range(0, rows).Select(r => new JArray(columns.Select(c => r < c.Count ? c[r] : "")))),
                    ["note"] = note ?? ""
                }
            };

            DA.SetData(0, new GH_ReportPart(new ReportPart { Json = json }));
        }

        /// <summary>A value as the table writes it: a number to so many decimals, anything else as its text.</summary>
        private static string Cell(IGH_Goo goo, int decimals)
        {
            switch (goo)
            {
                case null: return "";
                case GH_Integer i: return i.Value.ToString(CultureInfo.InvariantCulture);
                case GH_Number n: return decimals >= 0 ? n.Value.ToString("F" + decimals, CultureInfo.InvariantCulture) : n.Value.ToString("G6", CultureInfo.InvariantCulture);
                case GH_String s:
                    return decimals >= 0 && double.TryParse(s.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) && s.Value.IndexOfAny(new[] { '.', 'e', 'E' }) >= 0
                        ? v.ToString("F" + decimals, CultureInfo.InvariantCulture)
                        : s.Value;
                case GH_Boolean b: return b.Value ? "yes" : "no";
                default: return goo.ToString();
            }
        }

        public override GH_Exposure Exposure => GH_Exposure.secondary;

        protected override Bitmap Icon => Alpaca4d.Gh.Properties.Resources.Report_Table__Alpaca4d_;

        public override Guid ComponentGuid => new Guid("{4945F608-9760-4287-93DC-DA48FFCD57B3}");
    }
}
