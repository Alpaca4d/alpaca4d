using System;
using System.Linq;

using Grasshopper.Kernel;

using Alpaca4d.Loads;

namespace Alpaca4d.Gh
{
    /// <summary>
    /// For an analysis that loads the model once - Linear Buckling, Pushover - the loads it takes.
    /// The user gives them; nothing is picked for them. A model without load combinations is loaded
    /// with all its patterns together, each at its own Factor. A model with combinations is loaded
    /// with the one its Combination input names, and no other: left empty, it is an error that lists
    /// them (<see cref="LoadCombination.Apply"/>).
    /// </summary>
    internal static class CombinationChoice
    {
        /// <summary>The input's description, for the analysis that <paramref name="does"/> - "buckles it", say.</summary>
        public static string Description(string does) =>
            $"The load combination the analysis {does} under, for a model that has them, from Load Combination: its name - ULS 2 - or its place from 0. " +
            "A model with combinations needs one named here. A model without them takes all its load patterns together, each at its own Factor.";

        /// <summary>
        /// <paramref name="model"/> loaded with the combination <paramref name="asked"/> names. The model
        /// itself when it has no combinations and none is asked, with a remark when more than one
        /// pattern then acts. Null, after an error on <paramref name="component"/>, when it has
        /// combinations and none is named, or none of that name, or none at all to pick from.
        /// </summary>
        public static Model Apply(GH_Component component, Model model, string asked, string verb)
        {
            asked = (asked ?? "").Trim();
            var combinations = model.Combinations;
            if (combinations == null || combinations.Count == 0)
            {
                if (asked.Length > 0)
                {
                    component.AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                        $"The model has no load combinations to pick \"{asked}\" from: add them with Load Combination, after Assemble Model.");
                    return null;
                }
                var patterns = model.LoadPatterns
                    .Where(p => p.PatternType == PatternType.Plain)
                    .Select((p, i) => string.IsNullOrWhiteSpace(p.Name) ? $"pattern {i}" : p.Name.Trim())
                    .ToList();
                if (patterns.Count > 1)
                    component.AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                        $"{List(patterns)} act together, each at its own Factor. To {verb} under one combination of them, add Load Combination after Assemble Model.");
                return model;
            }

            string names = string.Join(", ", combinations.Select(c => c.Name));
            if (asked.Length == 0)
            {
                component.AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    $"The model has load combinations: name the one to {verb} under in Combination - {names}.");
                return null;
            }

            var chosen = combinations.FirstOrDefault(c => string.Equals(c.Name.Trim(), asked, StringComparison.OrdinalIgnoreCase));
            if (chosen == null && int.TryParse(asked, out int place) && place >= 0 && place < combinations.Count)
                chosen = combinations[place];
            if (chosen == null)
            {
                component.AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    $"The model has no load combination \"{asked}\": it has {names}, numbered from 0 in that order.");
                return null;
            }

            var loaded = chosen.Apply(model, out string problem);
            if (loaded == null)
                component.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"{chosen.Name}: {problem}.");
            return loaded;
        }

        /// <summary>"G, Q and W".</summary>
        private static string List(System.Collections.Generic.IList<string> items) =>
            items.Count == 1 ? items[0] : string.Join(", ", items.Take(items.Count - 1)) + " and " + items.Last();
    }
}
