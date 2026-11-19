using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using System;
using System.Linq;
using System.Collections.Generic;

using Alpaca4d.Generic;
using Alpaca4d.Result;

namespace Alpaca4d.Gh
{
    public class ShellStresses : GH_Component
    {
        public ShellStresses()
          : base("Shell Stresses (Alpaca4d)", "Shell Stresses",
            "Reads the true stresses through the thickness of every shell element of an analysed " +
            "model - the in-plane components, the transverse shears and the Von Mises equivalent.\n" +
            "Not the same as Shell Forces, which reports stress resultants - forces and moments per " +
            "unit width, the whole thickness collapsed into eight numbers. These are stresses in " +
            "force over area, at the top, mid and bottom of the section.\n" +
            "A branch reads {element; layer}, layer 0 top, 1 middle, 2 bottom, holding one value " +
            "per integration point of the element.",
            "Alpaca4d", "08_NumericalOutput")
        {
            // Draw a Description Underneath the component
            this.Message = Alpaca4d.Gh.ComponentMessage.MyMessage(this);
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("AlpacaModel", "AlpacaModel", "The analysed model, from the AlpacaModel output of Run Analysis. Results are read out of the recorder file it points at.", GH_ParamAccess.item);
            pManager.AddBooleanParameter("History", "History",
                "Read every step: each path gets the step in front, {step; element; layer}. Step is then ignored.",
                GH_ParamAccess.item, false);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddIntegerParameter("Step", "Step", "Which recorded step to read.", GH_ParamAccess.item, 0);
            pManager[pManager.ParamCount - 1].Optional = true;
            _filterInput = pManager.ParamCount;
            pManager.AddTextParameter("ElementId", "ElementId", ElementIdentity.Filter, GH_ParamAccess.list);
            pManager[pManager.ParamCount - 1].Optional = true;
            _combinationInput = pManager.ParamCount;
            pManager.AddParameter(ResultCombinations.Input());
        }

        /// <summary>Where the ElementId filter sits in the input list.</summary>
        private int _filterInput;

        /// <summary>Where the Combination input sits: last, so that saved files keep the others in place.</summary>
        private int _combinationInput;

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.Register_DoubleParam("Sigma11", "σ₁₁", $"Direct stress along the section's local 1 axis [{Units.Force}/{Units.Length}²]");
            pManager.Register_DoubleParam("Sigma22", "σ₂₂", $"Direct stress along the section's local 2 axis [{Units.Force}/{Units.Length}²]");
            pManager.Register_DoubleParam("Sigma12", "σ₁₂", $"In-plane shear stress [{Units.Force}/{Units.Length}²]");
            pManager.Register_DoubleParam("Sigma23", "σ₂₃", $"Transverse shear stress in the local 2-3 plane [{Units.Force}/{Units.Length}²]");
            pManager.Register_DoubleParam("Sigma31", "σ₃₁", $"Transverse shear stress in the local 3-1 plane [{Units.Force}/{Units.Length}²]");
            pManager.Register_DoubleParam("VonMises", "VonMises", $"Von Mises equivalent stress, from the five components above [{Units.Force}/{Units.Length}²]");
            pManager.Register_StringParam("Layer", "Layer", "Which layer each branch is: Top, Middle or Bottom, in that order. Top is the face on the side local axis 3 points to - the blue arrow of Model View's Local Axes - which is not up on a shell whose mesh faces down.");
            pManager.Register_GenericParam("Element", "Element", ElementIdentity.ElementOutput);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var alpacaModel = new Alpaca4d.Model();
            if (!DA.GetData(0, ref alpacaModel)) return;
            if (!AnalysedModel.Check(this, alpacaModel)) return;
            ResultCombinations.Read(this, DA, _combinationInput, alpacaModel, (model, outputs) => Read(DA, model, outputs));
        }

        /// <summary>The stresses of one analysed model - the model itself, or one of its load combinations.</summary>
        private void Read(IGH_DataAccess DA, Alpaca4d.Model alpacaModel, ResultCombinations outputs)
        {
            bool history = false;
            int step = 0;
            DA.GetData(1, ref history);
            DA.GetData(2, ref step);

            var filter = ElementFilterInput.Read(DA, _filterInput, this);

            var steps = HistorySteps.Of(alpacaModel, history, step, this);
            if (steps == null) return;

            var shells = alpacaModel.Shells;
            var kept = ElementFilterInput.Select(filter, shells, alpacaModel, "shells", this);
            var keptShells = kept.Select(i => shells[i]).ToList();

            var trees = Enumerable.Range(0, 6).Select(_ => new DataTree<double>()).ToArray();

            foreach (int current in steps)
            {
                List<Alpaca4d.Result.Read.ShellFibreStress> all;
                try
                {
                    all = Alpaca4d.Result.Read.ShellFibreStresses(alpacaModel, current);
                }
                catch (Exception ex)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
                    return;
                }

                // Grouped by element, then by the station through the thickness. The reader hands
                // them back bottom to top, one set per integration point of the element.
                var byElement = all.GroupBy(x => x.ElementId)
                                   .ToDictionary(g => g.Key, g => g.ToList());

                foreach (var shell in keptShells)
                {
                    if (shell.Id == null || !byElement.TryGetValue(shell.Id.Value, out var entries))
                        continue;

                    int fibreCount = entries.Select(x => x.Fibre).Distinct().Count();
                    if (fibreCount == 0)
                        continue;

                    int[] wanted = Alpaca4d.Result.Read.LayerFibres(fibreCount);

                    for (int layer = 0; layer < wanted.Length; layer++)
                    {
                        var atLayer = entries.Where(x => x.Fibre == wanted[layer])
                                             .OrderBy(x => x.GaussPoint)
                                             .ToList();

                        var path = history
                            ? new GH_Path(current, shell.Id.Value, layer)
                            : new GH_Path(shell.Id.Value, layer);

                        trees[0].AddRange(atLayer.Select(x => x.S11), path);
                        trees[1].AddRange(atLayer.Select(x => x.S22), path);
                        trees[2].AddRange(atLayer.Select(x => x.S12), path);
                        trees[3].AddRange(atLayer.Select(x => x.S23), path);
                        trees[4].AddRange(atLayer.Select(x => x.S31), path);
                        trees[5].AddRange(atLayer.Select(x => x.VonMises), path);
                    }
                }
            }

            for (int i = 0; i < trees.Length; i++)
                outputs.Tree(i, trees[i]);

            outputs.Same(6, Alpaca4d.Result.Read.LayerNames);
            outputs.Same(7, keptShells);
        }

        public override GH_Exposure Exposure => GH_Exposure.tertiary;

        protected override System.Drawing.Bitmap Icon => Alpaca4d.Gh.Properties.Resources.Shell_Stresses__Alpaca4d_;

        public override Guid ComponentGuid => new Guid("{9B38E0D7-5A40-528F-A0E0-404AB291CE01}");
    }
}
