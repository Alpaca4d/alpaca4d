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
    public class BrickStress : GH_Component
    {
        public BrickStress()
          : base("Brick Stresses (Alpaca4d)", "Brick Stresses",
            "Reads the stress state of every solid element of an analysed model - the six components " +
            "of the stress tensor plus the Von Mises equivalent stress.\n" +
            "One value per element, in its local axes. An SSP Brick and a Four Node Tetrahedron, " +
            "the only two solid types, each have one integration point.\n" +
            "Values come tetrahedra first, then SSP bricks - Element says which is which. Give " +
            "ElementId to read part of a big model.",
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
            pManager.AddGenericParameter("AlpacaModel", "AlpacaModel", "The analysed model, from the AlpacaModel output of Run Analysis. Results are read out of the recorder file it points at.", GH_ParamAccess.item);
            pManager.AddBooleanParameter("History", "History",
                "Read every recorded step instead of one. Each output then becomes a tree with " +
                "one branch per step, {step}, holding that step's value per element. Step is ignored.",
                GH_ParamAccess.item, false);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddIntegerParameter("Step", "Step", "Which recorded step to read.", GH_ParamAccess.item, 0);
            pManager[pManager.ParamCount - 1].Optional = true;
            _filterInput = pManager.ParamCount;
            pManager.AddTextParameter("ElementId", "ElementId", ElementIdentity.Filter, GH_ParamAccess.list);
            pManager[pManager.ParamCount - 1].Optional = true;
        }

        /// <summary>Where the ElementId filter sits in the input list.</summary>
        private int _filterInput;

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.Register_GenericParam("Sigma11", "σ₁₁", $"Direct stress along the element's local 1 axis [{Units.Force}/{Units.Length}²]");
            pManager.Register_GenericParam("Sigma22", "σ₂₂", $"Direct stress along the element's local 2 axis [{Units.Force}/{Units.Length}²]");
            pManager.Register_GenericParam("Sigma33", "σ₃₃", $"Direct stress along the element's local 3 axis [{Units.Force}/{Units.Length}²]");
            pManager.Register_GenericParam("Sigma12", "σ₁₂", $"Shear stress in the local 1-2 plane [{Units.Force}/{Units.Length}²]");
            pManager.Register_GenericParam("Sigma23", "σ₂₃", $"Shear stress in the local 2-3 plane [{Units.Force}/{Units.Length}²]");
            pManager.Register_GenericParam("Sigma13", "σ₁₃", $"Shear stress in the local 1-3 plane [{Units.Force}/{Units.Length}²]");
            pManager.Register_DoubleParam("VonMises", "VonMises", $"Von Mises equivalent stress, derived from the six components above [{Units.Force}/{Units.Length}²]");
            pManager.Register_GenericParam("Element", "Element", ElementIdentity.ElementOutput);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object can be used to retrieve data from input parameters and 
        /// to store data in output parameters.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var alpacaModel = new Alpaca4d.Model();
            bool history = false;
            int step = 0;

            if (!DA.GetData(0, ref alpacaModel)) return;
            DA.GetData(1, ref history);
            DA.GetData(2, ref step);

            var filter = ElementFilterInput.Read(DA, _filterInput, this);

            var steps = HistorySteps.Of(alpacaModel, history, step, this);
            if (steps == null) return;

            var bricks = RecordedOrder(alpacaModel);
            var kept = ElementFilterInput.Select(filter, bricks, alpacaModel, "solid elements", this);

            // Six components plus von Mises, in the output order.
            var outputs = Enumerable.Range(0, 7).Select(_ => new DataTree<double>()).ToArray();

            foreach (int current in steps)
            {
                var stresses = StressesAt(alpacaModel, current);
                for (int i = 0; i < outputs.Length; i++)
                    outputs[i].AddRange(ElementFilterInput.Slice(stresses[i], kept), new Grasshopper.Kernel.Data.GH_Path(current));
            }

            // Finally assign the spiral to the output parameter.
            for (int i = 0; i < outputs.Length; i++)
            {
                // A single step keeps the flat list it has always been; a history is a tree
                // with one branch per step.
                if (history)
                    DA.SetDataTree(i, outputs[i]);
                else
                    DA.SetDataList(i, outputs[i].AllData());
            }

            // The only thing saying which element each value belongs to: unlike the beam and
            // shell components these outputs are flat lists, with no branch path to carry a tag.
            DA.SetDataList(7, kept.Select(i => bricks[i]));
        }

        /// <summary>
        /// The solid elements in the order <see cref="StressesAt"/> returns their stresses: every
        /// tetrahedron first, then every SSP brick.
        ///
        /// Not the order the model holds them in. The recorder writes one dataset per element
        /// class, and StressesAt concatenates the two, so a model mixing the two kinds reports
        /// them regrouped by kind rather than interleaved as assembled. Building the matching
        /// element list here is what makes the Element output honest about which value belongs
        /// to which element, and what the filter selects against.
        /// </summary>
        private static List<Alpaca4d.Generic.IBrick> RecordedOrder(Alpaca4d.Model alpacaModel)
        {
            var tetrahedra = alpacaModel.Bricks.Where(x => x.ElementClass == Element.ElementClass.FourNodeTetrahedron);
            var sspBricks = alpacaModel.Bricks.Where(x => x.ElementClass == Element.ElementClass.SSPBrick);

            return tetrahedra.Concat(sspBricks).ToList();
        }

        /// <summary>
        /// The six stress components and von Mises at one step, in the order the outputs are
        /// registered, each holding one value per brick.
        /// </summary>
        private static List<double>[] StressesAt(Alpaca4d.Model alpacaModel, int step)
        {
            List<double> tetraSigma11 = new List<double>();
            List<double> tetraSigma22 = new List<double>();
            List<double> tetraSigma33 = new List<double>();
            List<double> tetraSigma12 = new List<double>();
            List<double> tetraSigma23 = new List<double>();
            List<double> tetraSigma13 = new List<double>();

            List<double> sspSigma11 = new List<double>();
            List<double> sspSigma22 = new List<double>();
            List<double> sspSigma33 = new List<double>();
            List<double> sspSigma12 = new List<double>();
            List<double> sspSigma23 = new List<double>();
            List<double> sspSigma13 = new List<double>();

            if (alpacaModel.HasTetrahedron)
            {
                (tetraSigma11, tetraSigma22, tetraSigma33, tetraSigma12, tetraSigma23, tetraSigma13) = Alpaca4d.Result.Read.TetrahedronStress(alpacaModel, step);
            }
            if (alpacaModel.HasSSpBrick)
            {
                (sspSigma11, sspSigma22, sspSigma33, sspSigma12, sspSigma23, sspSigma13) = Alpaca4d.Result.Read.SSPBrickStress(alpacaModel, step);
            }


            // Tetrahedra then SSP bricks, which is the order RecordedOrder lists the elements in
            // and the order the Element output reports. These used to carry an
            // ".OrderBy(i => ids)", which sorted every value by the same whole list of IDs and so
            // could not reorder anything; the concatenation below is what the order has always
            // really been.
            var sigma11 = tetraSigma11.Concat(sspSigma11).ToList();
            var sigma22 = tetraSigma22.Concat(sspSigma22).ToList();
            var sigma33 = tetraSigma33.Concat(sspSigma33).ToList();
            var sigma12 = tetraSigma12.Concat(sspSigma12).ToList();
            var sigma23 = tetraSigma23.Concat(sspSigma23).ToList();
            var sigma13 = tetraSigma13.Concat(sspSigma13).ToList();

            // Calculate Con Mises stress

            List<double> vonMises = new List<double>();
            
            for (int i = 0; i < sigma11.Count(); i++) 
            {
                double _vonMises = Math.Sqrt(
                0.5 * ((sigma11[i] - sigma22[i]) * (sigma11[i] - sigma22[i]) +
                       (sigma22[i] - sigma33[i]) * (sigma22[i] - sigma33[i]) +
                       (sigma33[i] - sigma11[i]) * (sigma33[i] - sigma11[i]) +
                       6.0 * (sigma12[i] * sigma12[i] + sigma23[i] * sigma23[i] + sigma13[i] * sigma13[i]))
                );

                vonMises.Add(_vonMises);
            }

            return new[] { sigma11, sigma22, sigma33, sigma12, sigma23, sigma13, vonMises };
        }


        /// <summary>
        /// The Exposure property controls where in the panel a component icon 
        /// will appear. There are seven possible locations (primary to septenary), 
        /// each of which can be combined with the GH_Exposure.obscure flag, which 
        /// ensures the component will only be visible on panel dropdowns.
        /// </summary>
        public override GH_Exposure Exposure => GH_Exposure.quarternary;

        protected override System.Drawing.Bitmap Icon => Alpaca4d.Gh.Properties.Resources.Brick_Stresses__Alpaca4d_;

        public override Guid ComponentGuid => new Guid("{DF03EC57-E1FA-4A3D-82BB-7F65526CF0B6}");
    }
}