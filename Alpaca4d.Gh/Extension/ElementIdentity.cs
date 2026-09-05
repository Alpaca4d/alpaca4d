namespace Alpaca4d.Gh
{
    /// <summary>
    /// The wording of the ElementId input on the element components, and of the filter inputs and
    /// outputs on the result components that read it back. One copy so the two halves cannot drift
    /// apart - a user reading the element component has to be told the same rule the result
    /// component applies.
    /// </summary>
    internal static class ElementIdentity
    {
        /// <summary>The ElementId input on an element component.</summary>
        public const string ElementIdInput =
            "Optional identifier for the element, used to pull its results out of a big model - " +
            "type it, or a pattern matching it, into the ElementId input of Beam Forces, Shell " +
            "Forces or Brick Stresses.\n" +
            "Free text, and yours to repeat or not. One identifier over a list of twenty curves " +
            "labels all twenty the same, and asking for it returns all twenty - which is how a " +
            "mesh works too, since every face becomes its own element. Give a list of twenty " +
            "identifiers instead and each element gets its own. Nothing is renumbered either way.";

        /// <summary>The single filter input on a result component.</summary>
        public const string Filter =
            "Report only some of the elements. Left empty every element is reported.\n" +
            "Takes either handle, in one list, in any mix - each entry is worked out on its own:\n" +
            "  MyBeam_3   an ElementId, as typed on the element component\n" +
            "  MyBeam*    a wildcard over ElementIds: \"*\" is any run of characters, \"?\" is one, " +
            "so \"MyBeam*\" finds MyBeam_1 through MyBeam_9 and \"Col_?\" finds Col_1 but not Col_12\n" +
            "  47         a tag - the number Assemble hands each element and OpenSees knows it by\n" +
            "  regex:...  a full regular expression, for what a wildcard cannot say, as in " +
            "\"regex:^(Col|Beam)_[0-9]+$\"\n" +
            "An ElementId shared by several elements reports all of them; a tag is unique, so it " +
            "is how one element is picked out of such a group, and how an element with no " +
            "ElementId at all is reached. A plain number is tried as both, in case that is what an " +
            "element was labelled. Matching ignores case.";

        /// <summary>The Tag output on a result component.</summary>
        public const string TagOutput =
            "The tag of each element reported, in the same order as the results, and the number " +
            "each result branch is keyed by.";

        /// <summary>The Element output on a result component.</summary>
        public const string ElementOutput =
            "The elements reported, in the same order as the results. Deconstruct them for the " +
            "geometry to draw the filtered results on, and for the ElementId each one matched by.";
    }
}
