using System.Collections.Generic;
using System.Linq;

using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;

namespace Alpaca4d.Gh
{
    /// <summary>
    /// The Element output of a component that makes several elements out of one input item - ASD
    /// Shell a face of the mesh each, Spring Link a link per line - put back on the branch the input
    /// came from.
    ///
    /// Grasshopper puts a list set by a component that has item inputs on a branch of its own, one
    /// more index on the input's path: a mesh on {0} gives its shells on {0;0}, the next mesh on
    /// {0;1}. A beam component sets one beam an item, so its beams stay on {0}. Wired together into
    /// Assemble Model, whose Elements input takes a list, the two then make a model per branch, and
    /// the one with only the shells, or only the links, has none of the nodes its supports are at.
    /// With the index taken off again, the elements of every item of a branch are one list on that
    /// branch, as the beams are.
    /// </summary>
    internal static class ElementOutput
    {
        /// <summary>
        /// Takes the last index off every branch of output <paramref name="index"/>, merging the
        /// branches that then share a path. Left as it is when a branch has no such index to lose:
        /// when what is left is not a branch of any input, Grasshopper did not add one.
        /// </summary>
        public static void OntoInputBranches(GH_Component component, int index)
        {
            if (!(component.Params.Output[index] is GH_Param<IGH_Goo> output))
                return;

            // By their text: a GH_Path compares as equal to another, but does not hash as one.
            var inputPaths = new HashSet<string>(component.Params.Input.SelectMany(input => input.VolatileData.Paths).Select(path => path.ToString()));
            var data = output.VolatileData;
            var merged = new GH_Structure<IGH_Goo>();

            foreach (var path in data.Paths)
            {
                if (path.Length < 2 || !inputPaths.Contains(path.CullElement().ToString()))
                    return;

                merged.AppendRange(data.get_Branch(path).Cast<IGH_Goo>(), path.CullElement());
            }

            output.ClearData();
            output.AddVolatileDataTree(merged);
        }
    }
}
