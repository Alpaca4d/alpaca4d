using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Parameters;

using Alpaca4d.UIWidgets;

namespace Alpaca4d.Gh
{
    /// <summary>
    /// The Combination input of the result components, and where their outputs go.
    ///
    /// A model without load combinations is read as it always was, its outputs set straight through.
    /// One that holds them - Run Analysis after Load Combination - is read once for each combination
    /// asked for, every one when Combination is empty, and each output becomes a tree with the
    /// combination's place in the model in front of its path: {combination} for what was a list,
    /// {combination; tag} for what was {tag}, {combination; step; tag} with History on. The place, not
    /// the order asked in, so a combination keeps its branch whichever others are read beside it.
    ///
    /// What is the same for every combination - where the nodes are, the elements, the supports'
    /// planes - stays the one flat list it is without combinations, as History leaves it, and
    /// Grasshopper pairs that one branch with every branch of the results.
    /// </summary>
    internal sealed class ResultCombinations
    {
        public const string Name = "Combination";

        public const string Description =
            "Load combinations to read, by name or place. Empty reads every one, a branch each: {combination; …}.";

        /// <summary>The Combination input, to add last of the component's inputs so that saved files keep theirs in place.</summary>
        public static IGH_Param Input()
        {
            return new Param_String
            {
                Name = Name,
                NickName = Name,
                Description = Description,
                Access = GH_ParamAccess.list,
                Optional = true,
            };
        }

        /// <summary>The Combination input on a switcher's evaluation unit, last of its inputs.</summary>
        public static void Register(EvaluationUnit unit)
        {
            unit.RegisterInputParam(new Param_String(), Name, Name, Description, GH_ParamAccess.list);
            unit.Inputs[unit.Inputs.Count - 1].Parameter.Optional = true;
        }

        private readonly IGH_DataAccess _da;
        private readonly SortedDictionary<int, DataTree<object>> _trees = new SortedDictionary<int, DataTree<object>>();
        private readonly SortedDictionary<int, List<object>> _same = new SortedDictionary<int, List<object>>();

        /// <summary>The place of the combination being read, or -1 for a model without combinations, whose outputs go straight through.</summary>
        private int _place = -1;

        private ResultCombinations(IGH_DataAccess da)
        {
            _da = da;
        }

        /// <summary>
        /// Reads <paramref name="model"/> with <paramref name="read"/>: once, as it always was, when it
        /// holds no load combinations; otherwise once for each combination the Combination input asks
        /// for - every one that solved, when it is empty - each into its own branches. A combination
        /// asked for that the model has not, or that did not solve, is an error naming it; the others
        /// are still read.
        /// </summary>
        public static void Read(GH_Component component, IGH_DataAccess DA, int input, Model model, Action<Model, ResultCombinations> read)
        {
            var asked = new List<string>();
            if (input >= 0 && input < component.Params.Input.Count)
                DA.GetDataList(input, asked);
            asked = asked.Where(text => !string.IsNullOrWhiteSpace(text)).Select(text => text.Trim()).ToList();

            var outputs = new ResultCombinations(DA);
            var held = model.CombinationResults;
            if (held == null || held.Count == 0)
            {
                if (asked.Count > 0)
                {
                    model.OfCombination(asked[0], out string problem);
                    component.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, Sentence(problem));
                    return;
                }
                read(model, outputs);
                return;
            }

            var places = new SortedSet<int>();
            if (asked.Count == 0)
            {
                for (int k = 0; k < held.Count; k++)
                    if (held[k].CombinationFailure == null)
                        places.Add(k);
                var failed = held.Where(one => one.CombinationFailure != null).Select(one => one.Combination?.Name).ToList();
                if (failed.Count > 0)
                    component.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                        $"{string.Join(", ", failed)} did not solve, so {(failed.Count == 1 ? "it has" : "they have")} no branch.");
            }
            else
            {
                foreach (var name in asked)
                {
                    var found = model.OfCombination(name, out string problem);
                    if (found == null)
                        component.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, Sentence(problem));
                    else
                        places.Add(held.IndexOf(found));
                }
            }

            foreach (int place in places)
            {
                outputs._place = place;
                read(held[place], outputs);
            }
            outputs.Flush();

            if (places.Count > 0)
                component.AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    "A branch per load combination, its place first: " +
                    string.Join(", ", places.Select(k => $"{{{k}}} {held[k].Combination?.Name}")) + ".");
        }

        private static string Sentence(string problem) =>
            string.IsNullOrEmpty(problem) ? "" : char.ToUpperInvariant(problem[0]) + problem.Substring(1) + ".";

        /// <summary>A result as a tree: its paths, with the combination's place in front of each.</summary>
        public void Tree<T>(int output, DataTree<T> tree)
        {
            if (_place < 0)
            {
                _da.SetDataTree(output, tree);
                return;
            }
            var gathered = Gathered(output);
            for (int i = 0; i < tree.BranchCount; i++)
            {
                var path = new GH_Path(new[] { _place }.Concat(tree.Paths[i].Indices).ToArray());
                gathered.EnsurePath(path);
                gathered.AddRange(tree.Branches[i].Cast<object>(), path);
            }
        }

        /// <summary>A result as a list: a branch {combination} of its own.</summary>
        public void List(int output, IEnumerable items)
        {
            if (_place < 0)
            {
                _da.SetDataList(output, items);
                return;
            }
            var path = new GH_Path(_place);
            var gathered = Gathered(output);
            gathered.EnsurePath(path);
            gathered.AddRange(items.Cast<object>(), path);
        }

        /// <summary>A list that is the same for every combination - positions, elements: the one flat list it is without them.</summary>
        public void Same(int output, IEnumerable items)
        {
            if (_place < 0)
            {
                _da.SetDataList(output, items);
                return;
            }
            if (!_same.ContainsKey(output))
                _same[output] = items.Cast<object>().ToList();
        }

        private DataTree<object> Gathered(int output)
        {
            if (!_trees.TryGetValue(output, out var tree))
                _trees[output] = tree = new DataTree<object>();
            return tree;
        }

        private void Flush()
        {
            foreach (var pair in _same)
                _da.SetDataList(pair.Key, pair.Value);
            foreach (var pair in _trees)
                _da.SetDataTree(pair.Key, pair.Value);
        }
    }
}
