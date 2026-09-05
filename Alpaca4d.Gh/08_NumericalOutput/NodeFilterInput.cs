using Grasshopper.Kernel;
using System.Collections.Generic;
using System.Linq;

namespace Alpaca4d.Gh
{
    /// <summary>
    /// The shared half of the NodeTag filter on the nodal result components.
    ///
    /// Simpler than the element one on purpose: a node has no free-text identifier to match. Nobody
    /// authors a node - they fall out of the element geometry, and Assemble numbers the unique
    /// points it finds - so the tag is the only handle there is, and a plain list of numbers is the
    /// whole filter. No glob, no patterns.
    /// </summary>
    internal static class NodeFilterInput
    {
        /// <summary>The NodeTag input on Nodal Displacements.</summary>
        public const string NodeTagFilter =
            "Report only these nodes, by tag - the number Assemble hands each node and the number " +
            "OpenSees knows it by. Left empty every node is reported, and then a node's tag is its " +
            "place in the output, counting from one.\n" +
            "Nodes are numbered by Assemble from the element geometry rather than authored, so the " +
            "Position output is the way to find the tag of a node you are interested in: read the " +
            "whole model once and look for the point you want.";

        /// <summary>The PointPos input on Reaction Forces.</summary>
        public const string SupportPointFilter =
            "Report only the supports at these points. Left empty every support is reported.\n" +
            "Points rather than node tags because a support is something you placed yourself and " +
            "can point at - feed it the same points you gave the Support component. A point counts " +
            "as being at a support when it is within the model's tolerance of it, the same distance " +
            "Assemble used to decide two points were one node.\n" +
            "A point with no support at it is reported as a warning rather than read as zero: a " +
            "node with nothing holding it has no reaction, and an empty output would otherwise " +
            "look the same as one that is genuinely unrestrained.";

        /// <summary>
        /// The NodeTag output on Reaction Forces. Nodal Displacements needs no such output - its
        /// tags are either exactly what was typed into NodeTag or, unfiltered, one to the number
        /// of nodes in order. A support's node is neither: which nodes are held is a fact about
        /// the model that nothing else on the component gives back.
        /// </summary>
        public const string NodeTagOutput =
            "The node each support reported sits on, in the same order as the results. What to " +
            "type into NodeTag above to come back to one of them.";

        /// <summary>The Position output on Nodal Displacements.</summary>
        public const string PositionOutput =
            "Where each node reported sits, undeformed, in the same order as the results below.\n" +
            "Filtered, this is the only thing tying the results to the model - the model's own " +
            "node list no longer lines up with them.";

        /// <summary>
        /// Reads the requested tags. An unplugged input, or one saved before this input existed,
        /// gives an empty list - which every caller reads as "all of them".
        /// </summary>
        public static List<int> Read(IGH_DataAccess DA, int index, GH_Component component)
        {
            var tags = new List<int>();

            // A definition saved before this input existed restores the parameter list it was
            // saved with, so the index asked for here may not be there to read.
            if (component.Params.Input.Count > index)
                DA.GetDataList(index, tags);

            return tags;
        }

        /// <summary>
        /// Reads the requested points. As with the tags, an unplugged input - or one saved before
        /// this input existed - gives an empty list, which the caller reads as "all of them".
        /// </summary>
        public static List<Rhino.Geometry.Point3d> ReadPoints(IGH_DataAccess DA, int index, GH_Component component)
        {
            var points = new List<Rhino.Geometry.Point3d>();

            if (component.Params.Input.Count > index)
                DA.GetDataList(index, points);

            return points;
        }

        /// <summary>
        /// The positions to report, and the requested tags that are not in the model. Thin wrapper
        /// over <see cref="Alpaca4d.Result.NodeFilter.Select"/>, where the rule itself lives.
        /// </summary>
        public static List<int> Select(List<int> requested, IReadOnlyList<int?> availableTags, out List<int> missing)
        {
            return Alpaca4d.Result.NodeFilter.Select(requested, availableTags, out missing);
        }

        /// <summary>
        /// Picks the entries at <paramref name="indices"/> out of a per-node list, which is how
        /// every output of a filtered component is cut down to the nodes being reported.
        /// </summary>
        public static List<TItem> Slice<TItem>(IReadOnlyList<TItem> perNode, List<int> indices)
        {
            return indices.Select(i => perNode[i]).ToList();
        }

        /// <summary>A short list of what is actually there, so the user can see what to ask for.</summary>
        public static string Preview(IEnumerable<int?> tags)
        {
            return Alpaca4d.Result.NodeFilter.Preview(tags);
        }
    }
}
