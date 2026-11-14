using System.Globalization;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;

using Alpaca4d.Result;

namespace Alpaca4d.Gh
{
    /// <summary>
    /// The first input of Linear Load and Mesh Load, saying what the load goes on: the elements,
    /// wired in, or text naming them by ElementId the way the result filters do. A load can then
    /// follow a name typed on the element component rather than a wire from it.
    ///
    /// One item still makes one load, as when the input took only elements. A list input would let
    /// several names make one load, but it would also put every force on every element, where a
    /// list of beams beside a list of forces now gives each beam its own.
    /// </summary>
    internal static class LoadTarget
    {
        /// <summary>The input's description; <paramref name="elements"/> is "beams" or "shells".</summary>
        public static string Description(string elements) =>
            $"The {elements} to load: the elements themselves, or text naming them as the result filters do - " +
            "an ElementId (Slab_1), a wildcard (Slab*), an element tag (47) or regex:^Slab.\n" +
            $"Each item makes one load, so two names that match the same element load it twice. Left empty, all the {elements} are loaded.";

        /// <summary>
        /// What arrived at input <paramref name="index"/>: an element, which comes back in
        /// <paramref name="element"/>, text or a number, which comes back as the filter, or nothing,
        /// which leaves both null and the load on every element. Anything else, and text that will
        /// not compile, is an error on the component and false.
        /// </summary>
        public static bool Read<T>(GH_Component component, IGH_DataAccess DA, int index, string from,
            out T element, out string filter) where T : class
        {
            element = null;
            filter = null;

            IGH_Goo goo = null;
            if (!DA.GetData(index, ref goo) || goo == null)
                return true;

            var value = goo.ScriptVariable();
            switch (value)
            {
                case T found:
                    element = found;
                    return true;

                case string text:
                    if (string.IsNullOrWhiteSpace(text))
                        return true;
                    ElementFilter.Create(new[] { text }, out var badTerms);
                    if (badTerms.Count > 0)
                    {
                        component.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, badTerms[0]);
                        return false;
                    }
                    filter = text.Trim();
                    return true;

                // A tag from a slider or a panel of numbers, read as the same text typed.
                case int tag:
                    filter = tag.ToString(CultureInfo.InvariantCulture);
                    return true;
                case double number:
                    filter = number.ToString(CultureInfo.InvariantCulture);
                    return true;
            }

            var input = component.Params.Input[index].Name;
            component.AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                $"{input} takes {from}, or text naming them by ElementId. Given: {value?.GetType().Name ?? goo.TypeName}.");
            return false;
        }

        /// <summary>
        /// Grasshopper restores an input's name and description from the file, so one saved before
        /// the input took names would go on calling it <paramref name="oldName"/> and describing the
        /// elements alone. Called from the component's Read, once the file is read.
        /// </summary>
        public static void Rename(IGH_Param input, string oldName, string name, string description)
        {
            if (input.Name != oldName)
                return;

            input.Name = name;
            input.NickName = name;
            input.Description = description;
        }
    }
}
