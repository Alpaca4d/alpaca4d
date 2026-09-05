using Grasshopper.Kernel;
using System.Collections.Generic;
using System.Linq;

using Alpaca4d.Generic;
using Alpaca4d.Result;

namespace Alpaca4d.Gh
{
    /// <summary>
    /// The shared half of the filter on the element result components: reading the input back,
     /// and saying out loud when a filter asked for something the model does not hold.
    ///
    /// The filtering itself lives in <see cref="Alpaca4d.Result.ElementFilter"/>. This is only the
    /// Grasshopper side of it, kept in one place so the three components cannot drift apart in the
    /// order of their parameters or in what they warn about.
    /// </summary>
    internal static class ElementFilterInput
    {
        // The ElementId parameter is registered by each component rather than from here:
        // GH_InputParamManager is protected, so only a GH_Component can hand one out. The wording
        // of it and of the two outputs is shared through ElementIdentity instead, and all three go
        // at the end of their lists - a Grasshopper file remembers its wires by parameter
        // position, so a parameter inserted anywhere else would silently move every wire after it
        // in every definition anyone has already saved.

        /// <summary>
        /// Reads the input at <paramref name="index"/> into a filter, reporting any term that will
        /// not compile on the component.
        /// </summary>
        public static ElementFilter Read(IGH_DataAccess DA, int index, GH_Component component)
        {
            var terms = new List<string>();

            // A definition saved before this input existed restores the parameter list it was
            // saved with, so the index asked for here may not be there to read. A missing input is
            // the same as an empty one - every element is reported - and is worth no message: the
            // user did not ask for a filter.
            if (component.Params.Input.Count > index)
                DA.GetDataList(index, terms);

            var filter = ElementFilter.Create(terms, out var badTerms);

            foreach (var problem in badTerms)
                component.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, problem);

            return filter;
        }

        /// <summary>
        /// The positions of <paramref name="elements"/> to report, with a warning on the component
        /// when a filter that was asked for matched nothing here.
        ///
        /// Worth a warning because the alternative is unreadable: a filter matching nothing gives
        /// empty outputs, which look exactly like an analysis that produced nothing. The two cases
        /// are told apart - the ElementId or tag is nowhere in the model, or it is in the model but
        /// on an element of a kind this component does not read, which is what happens when a
        /// shell's ElementId is typed into Beam Forces.
        /// </summary>
        /// <param name="what">What this component reads, for the message: "beams", "shells", ...</param>
        public static List<int> Select<T>(ElementFilter filter, IReadOnlyList<T> elements,
                                          Alpaca4d.Model model, string what, GH_Component component)
            where T : IElement
        {
            var kept = filter.SelectIndices(elements);

            if (kept.Count > 0 || filter.MatchesEverything)
                return kept;

            var problems = filter.Unmatched(model.Elements);

            if (problems.Count > 0)
            {
                foreach (var problem in problems)
                    component.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, problem);
            }
            else
            {
                component.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"The filter matched elements of this model, but none of them are {what}, " +
                    $"so there is nothing for this component to report. It reads {what} only.");
            }

            return kept;
        }

        /// <summary>
        /// Picks the entries at <paramref name="indices"/> out of a per-element list, which is how
        /// every output of a filtered component is cut down to the elements being reported.
        /// </summary>
        public static List<TItem> Slice<TItem>(List<TItem> perElement, List<int> indices)
        {
            return indices.Select(i => perElement[i]).ToList();
        }
    }
}
