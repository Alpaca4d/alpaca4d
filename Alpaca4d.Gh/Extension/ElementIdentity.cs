namespace Alpaca4d.Gh
{
    /// <summary>
    /// The wording of the ElementId input on the element components, and of the filter input and
    /// outputs on the result components that read it back. One copy so the two halves cannot drift
    /// apart.
    ///
    /// Kept to a couple of lines each. A parameter description is a reminder on hover, not the
    /// manual - the rules behind these live in ElementFilter and in Alpaca4d.Test/ElementId.
    /// </summary>
    internal static class ElementIdentity
    {
        /// <summary>The ElementId input on an element component.</summary>
        public const string ElementIdInput =
            "Optional name for the element, used to pull its results out later.\n" +
            "Free text, and yours to repeat: one name over twenty curves labels all twenty, and " +
            "asking for it returns all twenty. Give a list to name them apart.";

        /// <summary>The single filter input on a result component.</summary>
        public const string Filter =
            "Report only some of the elements. Left empty every element is reported.\n" +
            "Takes an ElementId (MyBeam_3), a wildcard over them (MyBeam*, Col_?), an element tag " +
            "(47), or a regular expression (regex:^Col), mixed freely in one list.";

        /// <summary>The Tag output on a result component.</summary>
        public const string TagOutput =
            "The tag of each element reported, in the same order as the results, and the number " +
            "each result branch is keyed by.";

        /// <summary>The Element output on a result component.</summary>
        public const string ElementOutput =
            "The elements reported, in the same order as the results. Deconstruct them for the " +
            "geometry to draw the results on.";
    }
}
