namespace Alpaca4d.Gh
{
    /// <summary>
    /// The wording of the ElementId input, shared by every element component. One copy so they
    /// cannot drift apart, and so that the result components reading these identifiers back can be
    /// worded from the same place.
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

    }
}
