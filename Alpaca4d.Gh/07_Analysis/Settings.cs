using Grasshopper;
using Grasshopper.Kernel;
using Rhino.Geometry;
using System;
using System.Collections.Generic;

using Grasshopper.Kernel.Special;
using System.Linq;
using Alpaca4d;
using Alpaca4d.Generic;

namespace Alpaca4d.Gh
{
    public class AnalysisSettings : GH_Component
    {
        public AnalysisSettings()
          : base("Analysis Settings (Alpaca4d)", "Analysis Settings",
            "Collects everything OpenSees needs in order to solve - constraint handler, numberer, " +
            "system of equations, convergence test, solution algorithm, integrator and stepping - " +
            "into the one object Run Analysis takes.\n" +
            "Every input has a default, so an unconnected component already describes a valid linear " +
            "static analysis. For a dynamic one set AnalysisType to Transient, and give it a Newmark " +
            "or Central Difference Integrator, an Analysis Step with Dt, and a Damping.",
            "Alpaca4d", "07_Analysis")
        {
            // Draw a Description Underneath the component
            this.Message = Alpaca4d.Gh.ComponentMessage.MyMessage(this);
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Damping", "Damping", "Rayleigh damping, from the Damping component. Only a transient analysis uses it.", GH_ParamAccess.item);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddTextParameter("Constraint", "Constraint", "Connect a 'ValueList'\nPlain, Transformation", GH_ParamAccess.item, "Transformation");
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddTextParameter("Numberer", "Numberer", "Connect a 'ValueList'\nRCM, AMD, Plain", GH_ParamAccess.item, "RCM");
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddTextParameter("System", "System",
                "How the system of equations is stored and solved. Connect a 'ValueList'.\n" +
                "BandSPD is the default, fastest of the nine on every model measured. Reach for " +
                "UmfPack if a model refuses to solve - it assumes nothing about the matrix.\n" +
                "BandGen, BandSPD, ProfileSPD, SuperLU, UmfPack, SparseSYM, SparseSPD, SparseGeneral, FullGeneral",
                GH_ParamAccess.item, "BandSPD");
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddGenericParameter("Test", "Test", "Convergence test - what counts as solved, and how many iterations to allow. From the Test component. Defaults to EnergyIncr.", GH_ParamAccess.item);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddTextParameter("Algorithm", "Algorithm",
                "How each step is iterated to equilibrium. Connect a 'ValueList'.\n" +
                "Newton is the default: about 20% slower than Linear on an elastic model, and what " +
                "lets a yielding one converge at all. Linear does one solve with no iteration - on a " +
                "nonlinear model it reports success and returns the unconverged first guess.\n" +
                "Linear, Newton, NewtonLineSearch, ModifiedNewton, KrylovNewton, SecantNewton, BFGS, Broyden",
                GH_ParamAccess.item, "Newton");
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddGenericParameter("Integrator", "Integrator", "How to advance from one step to the next, from the Integrator component. Load Control for a static analysis, Newmark or Central Difference for a transient one. Defaults to Load Control with a step of 1.", GH_ParamAccess.item);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddTextParameter("AnalysisType", "AnalysisType", "Connect a 'ValueList'\nStatic, Transient", GH_ParamAccess.item, "Static");
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddGenericParameter("AnalysisSteps", "AnalysisSteps", "How many increments to solve, from the Analysis Step component. A transient analysis needs one with Dt set. Defaults to a single increment.", GH_ParamAccess.item);
            pManager[pManager.ParamCount - 1].Optional = true;
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.Register_GenericParam("Settings", "Settings", "The settings. Feed them to the Settings input of Run Analysis.");
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object can be used to retrieve data from input parameters and 
        /// to store data in output parameters.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            Alpaca4d.Damping damping = null;
            DA.GetData(0, ref damping);

            ///
            var constraint = "Transformation";
            DA.GetData(1, ref constraint);
            var constraintObj = new Constraint(constraint);

            
            ///
            var numberer = "RCM";
            DA.GetData(2, ref numberer);
            var numbererObj = new Numberer(numberer);

            ///
            var system = "BandSPD";
            DA.GetData(3, ref system);
            var systemObj = new Alpaca4d.SystemEquation(system);

            ///
            var test = Alpaca4d.Test.EnergyIncr();
            DA.GetData(4, ref test);

            ///
            var algorithm = "Newton";
            DA.GetData(5, ref algorithm);
            var algorithmObj = new Algorithm(algorithm);

            ///
            var integrator = Alpaca4d.Integrator.LoadControl(1);
            DA.GetData(6, ref integrator);

            ///
            var analysisType = "Static";
            DA.GetData(7, ref analysisType);
            var analysisObj = new Analysis(analysisType);


            var analysisStep = new Alpaca4d.AnalysisStep(1) as dynamic;
            DA.GetData(8, ref analysisStep);

            try
            {
                if(analysisStep.Value != null)
                {
                    if (analysisStep.Value.GetType() == typeof(string))
                    {
                        var step = (string)analysisStep.Value;
                        analysisStep = new Alpaca4d.AnalysisStep(step);
                    }
                    else if (analysisStep.Value.GetType() == typeof(int) || analysisStep.Value.GetType() == typeof(double))
                    {
                        var step = (int)analysisStep.Value;
                        analysisStep = new Alpaca4d.AnalysisStep(step);
                    }
                    else
                    {
                        analysisStep = analysisStep.Value;
                    }
                }
            }
            catch
            {
                // it is using the default value
            }

            if(analysisObj.Type == Analysis.AnalysisType.Transient)
            {
                if(((Alpaca4d.AnalysisStep)analysisStep).Dt  == null)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Transient analysis requires an analysis step with Dt specified");
                    return;
                }
            }
            
            var settings = new Settings(constraintObj, numbererObj, systemObj, test, algorithmObj, integrator, analysisObj, analysisStep, damping);

            // Finally assign the spiral to the output parameter.
            DA.SetData(0, settings);
        }

        protected override void BeforeSolveInstance()
        {
            List<string> resultTypes;
            
            resultTypes = Enum.GetNames(typeof(Constraint.ConstraintType)).ToList();
            ValueListUtils.UpdateValueLists(this, 1, resultTypes, null);

            resultTypes = Enum.GetNames(typeof(Alpaca4d.Numberer.NumbererType)).ToList();
            ValueListUtils.UpdateValueLists(this, 2, resultTypes, null);

            resultTypes = Enum.GetNames(typeof(Alpaca4d.SystemEquation.SystemType)).ToList();
            ValueListUtils.UpdateValueLists(this, 3, resultTypes, null);

            resultTypes = Enum.GetNames(typeof(Alpaca4d.Algorithm.AlgorithmType)).ToList();
            ValueListUtils.UpdateValueLists(this, 5, resultTypes, null);

            resultTypes = Enum.GetNames(typeof(Alpaca4d.Analysis.AnalysisType)).ToList();
            ValueListUtils.UpdateValueLists(this, 7, resultTypes, null);
        }



        /// <summary>
        /// The Exposure property controls where in the panel a component icon 
        /// will appear. There are seven possible locations (primary to septenary), 
        /// each of which can be combined with the GH_Exposure.obscure flag, which 
        /// ensures the component will only be visible on panel dropdowns.
        /// </summary>
        public override GH_Exposure Exposure => GH_Exposure.secondary;

        /// <summary>
        /// Provides an Icon for every component that will be visible in the User Interface.
        /// Icons need to be 24x24 pixels.
        /// You can add image files to your project resources and access them like this:
        /// return Resources.IconForThisComponent;
        /// </summary>
        protected override System.Drawing.Bitmap Icon => Alpaca4d.Gh.Properties.Resources.Analysis_settings__Alpaca4d_;

        /// <summary>
        /// Each component must have a unique Guid to identify it. 
        /// It is vital this Guid doesn't change otherwise old ghx files 
        /// that use the old ID will partially fail during loading.
        /// </summary>
        public override Guid ComponentGuid => new Guid("9AE62757-69CC-4023-92EB-E8E1AA164AD0");
    }
}