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
            "A name to find the element's results by. Several elements can share one.";

        /// <summary>The single filter input on a result component.</summary>
        public const string Filter =
            "Only these elements: ElementIds, wildcards (Col_*), tags (47) or regex:^Col. Empty reads all.";

        /// <summary>The Element output on a result component.</summary>
        public const string ElementOutput =
            "The elements reported, in the same order as the results. Deconstruct them for the " +
            "tag and for the geometry to draw the results on.";
    }
}
