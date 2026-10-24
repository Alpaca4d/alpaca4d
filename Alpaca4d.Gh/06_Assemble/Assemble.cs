using Grasshopper;
using Grasshopper.Kernel;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;

using Alpaca4d.Generic;
using Grasshopper.Kernel.Types;

namespace Alpaca4d.Gh
{
    public class AssembleModel : GH_Component
    {
        public AssembleModel()
          : base("AssembleModel (Alpaca4d)", "Assemble Model",
            "Assemble a Model",
            "Alpaca4d", "06_Assemble")
        {
            // Draw a Description Underneath the component
            this.Message = Alpaca4d.Gh.ComponentMessage.MyMessage(this);
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Elements", "Elements", "Every element in the model - beams, shells, bricks and tetrahedra together.", GH_ParamAccess.list);
            pManager.AddGenericParameter("Supports", "Supports", "The supports, from the Support component. A support only takes hold if it lands within Tolerance of a node.", GH_ParamAccess.list);
            pManager.AddGenericParameter("LoadPatterns", "LoadPatterns", "Load patterns and/or mass loads (mixed allowed)", GH_ParamAccess.list);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddGenericParameter("Constraints", "Constraints", "Rigid diaphragms and rigid links.", GH_ParamAccess.list);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddGenericParameter("Recorders", "Recorders", "What to write to the results file, from the MPCO Recorder component. Left empty, Run Analysis picks a recorder to suit the analysis type.", GH_ParamAccess.list);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddNumberParameter("Tolerance", "Tolerance", $"Distance below which two positions are treated as the same node [{Units.Length}]. It welds elements together and lands supports and loads on a node - too small and the model falls into pieces, too large and separate nodes merge.", GH_ParamAccess.item, DefaultTolerance);
            pManager[pManager.ParamCount - 1].Optional = true;
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.Register_GenericParam("AlpacaModel", "AlpacaModel", "The assembled model. Feed it to Run Analysis, to Natural Vibration Analysis, or to Model View to check it first.");
            pManager.Register_DoubleParam("Mass", "Mass", $"{Units.Mass}");
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object can be used to retrieve data from input parameters and 
        /// to store data in output parameters.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var element = new List<Alpaca4d.Generic.IElement>();
            var support = new List<Alpaca4d.Element.Support>();
            var loadPattern = new List<Alpaca4d.Loads.LoadPattern>();
            var constraint = new List<Alpaca4d.Generic.IConstraint>();
            var massLoad = new List<Alpaca4d.Loads.MassLoad>();
            var recorder = new List<Alpaca4d.Generic.IRecorder>();
            double tolerance = DefaultTolerance;

            if (!DA.GetDataList(0, element)) return;
            if (!DA.GetDataList(1, support)) return;

            // Accept both LoadPattern and MassLoad in input 2
            var rawLoads = new List<object>();
            DA.GetDataList(2, rawLoads);
            foreach (var raw in rawLoads)
            {
                object val = raw;
                if (val is GH_ObjectWrapper wrapper)
                {
                    val = wrapper.Value;
                }
                if (val is Alpaca4d.Loads.LoadPattern lp)
                {
                    loadPattern.Add(lp);
                }
                else if (val is Alpaca4d.Loads.MassLoad ml)
                {
                    massLoad.Add(ml);
                }
                else if (val != null)
                {
                    this.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Unsupported item at LoadPatterns input: {val.GetType().Name}");
                    return;
                }
            }
            DA.GetDataList(3, constraint);
            DA.GetDataList(4, recorder);
            DA.GetData(5, ref tolerance);

            // Zero welds nothing that is not bit-for-bit identical, and a negative radius finds
            // no node at all, so every element would fail to connect. Same fallback as TclReader
            // and NodeFilter; the negated test also catches NaN.
            if (!(tolerance > 0.0))
            {
                this.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"Tolerance must be greater than zero. {DefaultTolerance} {Units.Length} is used instead.");
                tolerance = DefaultTolerance;
            }

            WarnIfDocumentUnitsDiffer();

            var model = new Model(element, support, loadPattern, constraint, recorder);
            model.Mass = massLoad;

            model.Tollerance = tolerance;
            model.Assemble();
            WarnOfLoadsOnNothing(model);
            foreach (var warning in model.Warnings.Distinct())
                this.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, warning);

            // Finally assign the spiral to the output parameter.
            DA.SetData(0, model);
            DA.SetData(1, model.TotalMass);
        }

        private const double DefaultTolerance = 0.01;

        /// <summary>
        /// A load that names its elements by ElementId and names none of its kind goes on nothing,
        /// and the analysis runs without it. Said here, where the elements are known, the way the
        /// result components say so of a filter that finds nothing.
        /// </summary>
        private void WarnOfLoadsOnNothing(Model model)
        {
            var said = new HashSet<string>();
            foreach (var pattern in model.LoadPatterns)
            {
                foreach (var load in pattern.Load ?? new List<Alpaca4d.Generic.ILoad>())
                {
                    string message = null;
                    if (load is Alpaca4d.Loads.LineLoad line && line.Element == null && !string.IsNullOrWhiteSpace(line.Filter)
                        && line.AppliedTo(model.Beams).Count == 0)
                        message = OnNothing("Linear Load", line.Filter, "beam", model);
                    else if (load is Alpaca4d.Loads.MeshLoad mesh && mesh.Element == null && !string.IsNullOrWhiteSpace(mesh.Filter)
                        && mesh.AppliedTo(model.Shells).Count == 0)
                        message = OnNothing("Mesh Load", mesh.Filter, "shell", model);

                    if (message != null && said.Add(message))
                        this.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, message);
                }
            }
        }

        private static string OnNothing(string component, string filter, string kind, Model model)
        {
            int others = Alpaca4d.Result.ElementFilter.Named(filter, model.Elements).Count;
            if (others > 0)
                return $"{component} \"{filter}\" names {others} element{(others == 1 ? "" : "s")}, but no {kind}, so it loads nothing.";

            var why = Alpaca4d.Result.ElementFilter.Create(new[] { filter }, out _).Unmatched(model.Elements);
            return $"{component}: {(why.Count > 0 ? why[0] : $"\"{filter}\" matched nothing.")} It loads nothing.";
        }

        /// <summary>
        /// Alpaca4d never scales geometry: a coordinate is written to the deck as it stands and
        /// read as <see cref="Units.Length"/>, and so is the Tolerance. A document in any other
        /// unit gives a model at the wrong size with nothing to say so, so this says so. No
        /// document (Rhino.Compute, headless) means nothing to check.
        /// </summary>
        private void WarnIfDocumentUnitsDiffer()
        {
            var doc = Rhino.RhinoDoc.ActiveDoc;
            if (doc == null)
                return;

            var expected = Units.Length == LengthUnit.mm ? Rhino.UnitSystem.Millimeters : Rhino.UnitSystem.Meters;
            if (doc.ModelUnitSystem == expected)
                return;

            this.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                $"The Rhino document is in {doc.ModelUnitSystem}, but Alpaca4d reads every coordinate - and the " +
                $"Tolerance - as {expected} without converting. Switch the document units to {expected}, or " +
                $"the model will come out at the wrong size.");
        }


        /// <summary>
        /// The Exposure property controls where in the panel a component icon 
        /// will appear. There are seven possible locations (primary to septenary), 
        /// each of which can be combined with the GH_Exposure.obscure flag, which 
        /// ensures the component will only be visible on panel dropdowns.
        /// </summary>
        public override GH_Exposure Exposure => GH_Exposure.primary;

        /// <summary>
        /// Provides an Icon for every component that will be visible in the User Interface.
        /// Icons need to be 24x24 pixels.
        /// You can add image files to your project resources and access them like this:
        /// return Resources.IconForThisComponent;
        /// </summary>
        protected override System.Drawing.Bitmap Icon => Alpaca4d.Gh.Properties.Resources.Assemble_Model__Alpaca4d_;

        /// <summary>
        /// Each component must have a unique Guid to identify it. 
        /// It is vital this Guid doesn't change otherwise old ghx files 
        /// that use the old ID will partially fail during loading.
        /// </summary>
        public override Guid ComponentGuid => new Guid("B6978C42-7B88-4F1A-A5DF-85EF89F154F9");
    }
}