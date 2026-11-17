using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

using Grasshopper.Kernel;

using Alpaca4d.Loads;

namespace Alpaca4d.Gh
{
    /// <summary>
    /// Load combinations added to the assembled model: each a name, a type and the load patterns that
    /// act together, each times a factor. Lists, a combination an item, so that one component adds
    /// them all; or one after another, a component each, each adding to the ones the model has. Run
    /// Analysis then solves every combination, and hands on one model that holds the results of each.
    /// </summary>
    public class LoadCombinationComponent : GH_Component
    {
        public LoadCombinationComponent()
          : base("Load Combination (Alpaca4d)", "Combination",
            "Adds load combinations to the assembled model: each the load patterns that act together, by their Name and times a factor - 1.35 G + 1.5 Q. " +
            "Lists add many at once, a combination an item; a second Load Combination after this one adds more.\n" +
            "Run Analysis solves each combination in an OpenSees run of its own, and hands on one model that holds the results of every one: " +
            "pick one by its name in the Combination input of Model View, Linear Buckling or Pushover. The Report lists them, and checks the steel members against the ULS, accidental and seismic ones.",
            "Alpaca4d", "05_Load")
        {
            this.Message = Alpaca4d.Gh.ComponentMessage.MyMessage(this);
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("AlpacaModel", "AlpacaModel",
                "The assembled model, from Assemble Model - or from another Load Combination - with its load patterns named in Load Pattern's Name input.", GH_ParamAccess.item);
            pManager.AddTextParameter("Name", "Name",
                "Each combination's name: ULS 1. The Report, and Model View's Combination input, call it by this name. Left empty, a combination is named by its formula.",
                GH_ParamAccess.list);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddTextParameter("Type", "Type",
                "What each combination is checked for: " + string.Join(", ", LoadCombination.KindNames) + ". " +
                "Left empty, ULS; one type alone is the type of them all, and a shorter list leaves the last ones the type of its last.",
                GH_ParamAccess.list);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddTextParameter("Combination", "Combination",
                "Each combination, an item each: the patterns and their factors, joined by +: 1.35 G + 1.5 Q + 0.9 W. " +
                "Each term is a factor and a Load Pattern's Name, whatever its case: 1.5 Q, 1.5*Q, or Q alone for 1. " +
                "Write a negative factor as + -1.5 W, as a pattern's name may hold a minus.",
                GH_ParamAccess.list);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("AlpacaModel", "AlpacaModel",
                "The model with the combinations added to any it had, for Run Analysis, or for another Load Combination.", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            Model model = null;
            var names = new List<string>();
            var types = new List<string>();
            var formulas = new List<string>();
            if (!DA.GetData(0, ref model)) return;
            DA.GetDataList(1, names);
            DA.GetDataList(2, types);
            if (!DA.GetDataList(3, formulas)) return;

            if (names.Count > formulas.Count)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    $"Name has {names.Count} items for {formulas.Count} combinations: the ones past them are left out.");
            if (types.Count > formulas.Count)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    $"Type has {types.Count} items for {formulas.Count} combinations: the ones past them are left out.");

            var combinations = new List<LoadCombination>();
            for (int i = 0; i < formulas.Count; i++)
            {
                string name = i < names.Count ? names[i] : "";
                string type = types.Count == 0 ? "" : types[Math.Min(i, types.Count - 1)];
                string which = string.IsNullOrWhiteSpace(name) ? $"Combination {i}" : name.Trim();

                if (!LoadCombination.TryKind(type, out var kind, out string problem))
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"{which}: {problem}.");
                    return;
                }
                var combination = LoadCombination.Parse(name, formulas[i], kind, out problem);
                if (combination == null)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"{which}: {problem}.");
                    return;
                }
                combinations.Add(combination);
            }

            var with = LoadCombination.AddTo(model, combinations, out string why);
            if (with == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, Capital(why) + ".");
                return;
            }

            DA.SetData(0, with);
        }

        private static string Capital(string text) =>
            string.IsNullOrEmpty(text) ? text : char.ToUpperInvariant(text[0]) + text.Substring(1);

        public override GH_Exposure Exposure => GH_Exposure.secondary;

        protected override Bitmap Icon => Alpaca4d.Gh.Properties.Resources.Load_Combination__Alpaca4d_;

        public override Guid ComponentGuid => new Guid("{6F2B9C4E-3D81-4A7F-B5E2-9C04D7A1E863}");
    }
}
