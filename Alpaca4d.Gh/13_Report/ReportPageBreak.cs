using System;
using System.Drawing;

using Grasshopper.Kernel;
using Newtonsoft.Json.Linq;

namespace Alpaca4d.Gh
{
    /// <summary>A page break in the report: what follows it in Content starts on a new page.</summary>
    public class ReportPageBreak : GH_Component
    {
        public ReportPageBreak()
          : base("Report Page Break (Alpaca4d)", "PageBreak",
            "A page break: what follows it in the Content of the Report starts on a new page.",
            "Alpaca4d", "13_Report")
        {
            this.Message = Alpaca4d.Gh.ComponentMessage.MyMessage(this);
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Break", "Break", "The page break, for the Content of the Report.", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            DA.SetData(0, new GH_ReportPart(new ReportPart { Json = new JObject { ["pagebreak"] = true } }));
        }

        public override GH_Exposure Exposure => GH_Exposure.secondary;

        protected override Bitmap Icon => Alpaca4d.Gh.Properties.Resources.Report_Page_Break__Alpaca4d_;

        public override Guid ComponentGuid => new Guid("{9505C286-9EA1-4075-A640-EBE74A4F5C8A}");
    }
}
