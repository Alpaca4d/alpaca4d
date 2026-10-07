using Grasshopper;
using Grasshopper.Kernel;
using Rhino.Geometry;
using System;
using System.Linq;
using System.Collections.Generic;


using Alpaca4d.Generic;
using Alpaca4d.Result;

namespace Alpaca4d.Gh
{
    /// <summary>
    /// Retired. The report is now outputs of Natural Vibration Analysis, in its Modal Report menu,
    /// since it is written by that analysis and read by nothing else. Kept hidden so definitions
    /// that already place it still open.
    /// </summary>
    [Obsolete]
    public class ModalAnalysisReport : GH_Component
    {
        public ModalAnalysisReport()
          : base("Modal Analysis Report (Alpaca4d)", "Modal Analysis Report",
            "Splits the report written by a Natural Vibration Analysis into its sections: eigenvalues, " +
            "total and free mass, centre of mass, modal participation factors, participating masses " +
            "and their ratios.\n" +
            "Every output is one section of the report as text, ready for a panel. The cumulative " +
            "ratio is the one to check against a code threshold such as 90%.",
            "Alpaca4d", "08_NumericalOutput")
        {
            // Draw a Description Underneath the component
            this.Message = Alpaca4d.Gh.ComponentMessage.MyMessage(this);
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("AlpacaModel", "AlpacaModel", "A model that has been through the Natural Vibration Analysis. The report is read from the file that analysis writes.", GH_ParamAccess.item);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.Register_GenericParam("EigenValueAnalysis", "EigenValueAnalysis", "Section 2 of the report, as text: eigenvalue, frequency and period per mode.");
            pManager.Register_GenericParam("TotalMassOfStructure", "TotalMassOfStructure", "Section 3 of the report, as text: the mass of the whole model, per direction.");
            pManager.Register_GenericParam("TotalFreeMass", "TotalFreeMass", "Section 4 of the report, as text: the mass on unrestrained degrees of freedom, which is the mass the modes can actually move.");
            pManager.Register_GenericParam("CenterOfMass", "CenterOfMass", "Section 5 of the report, as text: where the mass of the model sits.");
            pManager.Register_GenericParam("ModalParticipationFactors", "ModalParticipationFactors", "Section 6 of the report, as text: the participation factor of each mode, per direction.");
            pManager.Register_GenericParam("ModalParticipationMasses", "ModalParticipationMasses", "Section 7 of the report, as text: how much mass each mode moves, per direction.");
            pManager.Register_GenericParam("ModalParticipationMasses_Cumulative", "ModalParticipationMasses_Cumulative", "Section 8 of the report, as text: participating mass summed over the modes up to each one.");
            pManager.Register_GenericParam("ModalParticipationMassesRatio(%)", "ModalParticipationMassesRatio(%)", "Section 9 of the report, as text: participating mass as a percentage of the total, per mode and direction.");
            pManager.Register_GenericParam("ModalParticipationMassesRatio(%)_Cumulative", "ModalParticipationMassesRatio(%)_Cumulative", "Section 10 of the report, as text: the percentages of section 9 summed over the modes up to each one. This is the column a code check against a 90% threshold reads.");
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object can be used to retrieve data from input parameters and 
        /// to store data in output parameters.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var alpacaModel = new Alpaca4d.Model();

            if (!DA.GetData(0, ref alpacaModel)) return;


            // The same reading Natural Vibration's Modal Report menu does: each section found by
            // its number, so one missing does not move the rest onto the wrong outputs.
            var sections = Alpaca4d.Eigen.ReportSections(System.IO.File.ReadAllLines(alpacaModel.ModalAnalysisReportFile));
            for (int i = 0; i < sections.Count; i++)
                DA.SetDataList(i, sections[i]);
        }


        /// <summary>
        /// The Exposure property controls where in the panel a component icon 
        /// will appear. There are seven possible locations (primary to septenary), 
        /// each of which can be combined with the GH_Exposure.obscure flag, which 
        /// ensures the component will only be visible on panel dropdowns.
        /// </summary>
        public override GH_Exposure Exposure => GH_Exposure.hidden;

        /// <summary>
        /// Provides an Icon for every component that will be visible in the User Interface.
        /// Icons need to be 24x24 pixels.
        /// You can add image files to your project resources and access them like this:
        /// return Resources.IconForThisComponent;
        /// </summary>
        protected override System.Drawing.Bitmap Icon => Alpaca4d.Gh.Properties.Resources.Modal_Analysis_Report__Alpaca4d_;

        /// <summary>
        /// Each component must have a unique Guid to identify it. 
        /// It is vital this Guid doesn't change otherwise old ghx files 
        /// that use the old ID will partially fail during loading.
        /// </summary>
        public override Guid ComponentGuid => new Guid("{7904934E-0E8F-499E-8BF6-7D1A7D4DA538}");
    }
}