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
    public class BeamStresses : GH_Component
    {
        public BeamStresses()
          : base("Beam Stresses (Alpaca4d)", "Beam Stresses",
            "Reads the stresses along every beam element of an analysed model - axial, bending, shear " +
            "and torsion, and the Von Mises equivalent - recovered from the beam forces with elastic " +
            "beam theory. They depend on the section's shape, not its material.\n" +
            "Each value is the worst the section sees: σmax and σmin at the worst corner, τ at its peak. " +
            "Von Mises puts the worst σ together with the worst τ, which is an upper bound - they rarely " +
            "sit at the same point.\n" +
            "One branch per element, keyed by its tag, holding the values at the integration sections " +
            "from the I end to the J end, as Beam Forces does. An Elastic Section has no shape, so its " +
            "beams give σN only.\n" +
            "Stresses in MPa (N/mm²), the unit section tables and design codes use.",
            "Alpaca4d", "08_NumericalOutput")
        {
            // Draw a Description Underneath the component
            this.Message = Alpaca4d.Gh.ComponentMessage.MyMessage(this);
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("AlpacaModel", "AlpacaModel", "The analysed model, from the AlpacaModel output of Run Analysis. Results are read out of the recorder file it points at.", GH_ParamAccess.item);
            pManager.AddBooleanParameter("History", "History",
                "Read every recorded step instead of one. The outputs then carry a step index in " +
                "front of the element's tag, so a branch reads {step; tag}. Step is ignored.",
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

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            // N/mm² rather than the model's kN/m² - the unit every steel or timber strength is
            // tabulated in, so the numbers can be read against a grade without a factor of 1000.
            const string stress = "[MPa]";

            pManager.Register_DoubleParam("SigmaN", "σN", $"Axial stress N/A, positive in tension {stress}");
            pManager.Register_DoubleParam("SigmaMy", "σMy", $"Largest bending stress from My, at the fibre furthest along local z {stress}");
            pManager.Register_DoubleParam("SigmaMz", "σMz", $"Largest bending stress from Mz, at the fibre furthest along local y {stress}");
            pManager.Register_DoubleParam("SigmaMax", "σmax", $"Largest direct stress on the section, N, My and Mz together {stress}");
            pManager.Register_DoubleParam("SigmaMin", "σmin", $"Smallest - most compressive - direct stress on the section {stress}");
            pManager.Register_DoubleParam("TauV", "τV", $"Peak shear stress from Vy and Vz {stress}");
            pManager.Register_DoubleParam("TauT", "τT", $"Peak shear stress from torsion {stress}");
            pManager.Register_DoubleParam("VonMises", "VonMises", $"√(σ² + 3τ²), σ the larger of σmax and σmin in size and τ = τV + τT {stress}");
            pManager.Register_GenericParam("Element", "Element", ElementIdentity.ElementOutput);
        }

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

            // Read.BeamStresses gives one entry per beam in this same order.
            var beams = alpacaModel.Beams;
            var kept = ElementFilterInput.Select(filter, beams, alpacaModel, "beams", this);

            var outputs = Enumerable.Range(0, 8).Select(_ => new DataTree<double>()).ToArray();
            var shapeless = new SortedSet<int>();

            foreach (int current in steps)
            {
                List<List<BeamStress>> all;
                try
                {
                    all = Alpaca4d.Result.Read.BeamStresses(alpacaModel, current);
                }
                catch (Exception ex)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
                    return;
                }

                foreach (int i in kept)
                {
                    var beam = beams[i];
                    if (beam.Id == null || i >= all.Count)
                        continue;

                    var path = history ? new GH_Path(current, beam.Id.Value) : new GH_Path(beam.Id.Value);
                    var stresses = all[i];

                    outputs[0].AddRange(stresses.Select(x => MPa(x.SigmaN)), path);

                    // A section with no shape has only N/A to give. Its branches are still made, empty,
                    // so every output keeps one branch per beam and they line up with Element.
                    if (stresses.Count > 0 && !stresses[0].HasShape)
                    {
                        shapeless.Add(beam.Id.Value);
                        for (int k = 1; k < outputs.Length; k++)
                            outputs[k].EnsurePath(path);
                        continue;
                    }

                    outputs[1].AddRange(stresses.Select(x => MPa(x.SigmaMy)), path);
                    outputs[2].AddRange(stresses.Select(x => MPa(x.SigmaMz)), path);
                    outputs[3].AddRange(stresses.Select(x => MPa(x.SigmaMax)), path);
                    outputs[4].AddRange(stresses.Select(x => MPa(x.SigmaMin)), path);
                    outputs[5].AddRange(stresses.Select(x => MPa(x.TauV)), path);
                    outputs[6].AddRange(stresses.Select(x => MPa(x.TauT)), path);
                    outputs[7].AddRange(stresses.Select(x => MPa(x.VonMises)), path);
                }
            }

            if (shapeless.Count > 0)
            {
                var tags = string.Join(", ", shapeless.Take(10)) + (shapeless.Count > 10 ? ", ..." : "");
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    $"{shapeless.Count} beam(s) use an Elastic Section, which has properties but no shape, so " +
                    $"only σN can be worked out for them (element {tags}). Their other branches are empty.");
            }

            for (int i = 0; i < outputs.Length; i++)
                DA.SetDataTree(i, outputs[i]);

            DA.SetDataList(8, kept.Select(i => beams[i]));
        }

        private static double MPa(double stress) => ModelStress.ToMPa(stress);

        public override GH_Exposure Exposure => GH_Exposure.secondary;

        protected override System.Drawing.Bitmap Icon => Alpaca4d.Gh.Properties.Resources.Beam_Stresses__Alpaca4d_;

        public override Guid ComponentGuid => new Guid("{D7AB9781-1379-4608-9130-448C8476F498}");
    }
}
