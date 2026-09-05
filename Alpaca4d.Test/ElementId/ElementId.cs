// Element identifiers and the result filter that reads them back.
//
// An element carries two handles, and the whole feature turns on keeping them apart:
//
//   Id         the tag Assemble hands out and OpenSees knows the element by. Always unique,
//              never authored, and what the result branches are keyed by.
//   ElementId  free text the user types on the element component. Theirs to repeat or not -
//              one identifier over twenty curves labels all twenty the same, and asking for it
//              returns all twenty. Nothing renumbers it.
//
// Both are typed into one filter input, which works out on its own what each term is: a whole
// number is tried as a tag and as an ElementId, anything else is a pattern over the ElementIds.
//
// What is worth guarding is the pattern matching. The obvious implementation hands the user's
// pattern straight to Regex, where "MyBeam*" means "MyBea" followed by any number of "m"s - it
// would match "MyBea" and miss "MyBeam_1", the exact opposite of what someone typing it is
// asking for.
//
// The checks run on a stub element rather than a real one. Everything under test reads nothing
// but Id and ElementId, and a real element would drag in Rhino geometry, whose native library
// only loads inside Rhino.
//
// The other half of the feature, an ElementId surviving Serialise and Deserialise through the
// "# alpaca:elementid" comment, cannot be checked here for that reason: it goes through
// Model.Assemble and TclReader, both of which need an RTree. Check that one in Rhino.
using System;
using System.Collections.Generic;
using System.Linq;

using Rhino.Geometry;

using Alpaca4d;
using Alpaca4d.Element;
using Alpaca4d.Generic;
using Alpaca4d.Result;

/// <summary>
/// An element that is nothing but its two identifiers, which is all the filter ever looks at.
/// </summary>
class StubElement : IElement
{
    public ElementType Type => ElementType.Beam;
    public int? Id { get; set; }
    public string ElementId { get; set; }

    public void SetTags() { }
    public void SetTopologyRTree(Model model) { }
    public string WriteTcl() => string.Empty;
}

class ElementIdTest
{
    static int fails = 0;

    static void Check(bool ok, string what)
    {
        if (!ok) fails++;
        Console.WriteLine($"  [{(ok ? "PASS" : "FAIL")}] {what}");
    }

    /// <summary>One element per identifier given, tagged 1..n the way Assemble tags them.</summary>
    static List<IElement> Elements(params string[] elementIds)
    {
        return elementIds.Select((id, i) => (IElement)new StubElement { Id = i + 1, ElementId = id }).ToList();
    }

    // ---------------------------------------------------------------- the glob

    static bool Matches(string pattern, string elementId)
    {
        var filter = ElementFilter.Create(new[] { pattern }, out var bad);
        Check(bad.Count == 0, $"\"{pattern}\" compiles");

        return filter.Matches(new StubElement { ElementId = elementId });
    }

    static void GlobTests()
    {
        Console.WriteLine("\nElementId patterns are globs, not regular expressions\n");

        Check(Matches("MyBeam*", "MyBeam_0"), "\"MyBeam*\" finds MyBeam_0");
        Check(Matches("MyBeam*", "MyBeam_12"), "\"MyBeam*\" finds MyBeam_12");
        Check(Matches("MyBeam*", "MyBeam"), "\"MyBeam*\" finds MyBeam itself");
        Check(!Matches("MyBeam*", "OtherBeam_0"), "\"MyBeam*\" leaves OtherBeam_0 alone");

        // The whole reason the pattern is not handed to Regex as written. As a regex "MyBeam*"
        // is "MyBea" + zero or more "m", so it would match this and miss everything above.
        Check(!Matches("MyBeam*", "MyBea"), "\"MyBeam*\" does NOT match MyBea, as a raw regex would");

        Check(Matches("MyBeam", "MyBeam"), "a bare identifier matches itself");
        Check(!Matches("MyBeam", "MyBeam_0"), "a bare identifier is exact - it does not match MyBeam_0");
        Check(!Matches("Beam", "MyBeam"), "a bare identifier is anchored - it does not match a substring");

        Check(Matches("Col_?", "Col_1"), "\"Col_?\" finds Col_1");
        Check(!Matches("Col_?", "Col_12"), "\"Col_?\" is one character - it leaves Col_12 alone");

        Check(Matches("mybeam*", "MyBeam_0"), "matching ignores case");

        // An identifier is free text, so anything a user can type has to be matchable rather
        // than read as a regex operator.
        Check(Matches("Slab+A", "Slab+A"), "\"+\" in an identifier is matched literally");
        Check(Matches("L1.2*", "L1.2_0"), "\".\" in a pattern is matched literally");
        Check(!Matches("L1.2*", "L1X2_0"), "\".\" does not stand for any character");

        // Grasshopper panels arrive with whatever whitespace the user left in them.
        Check(Matches("  MyBeam  ", "MyBeam"), "a padded pattern is trimmed");
        Check(Matches("MyBeam", "  MyBeam  "), "and so is a padded identifier");

        Check(Matches("regex:^(Col|Beam)_[0-9]+$", "Beam_7"), "\"regex:\" hands the rest to Regex");
        Check(!Matches("regex:^(Col|Beam)_[0-9]+$", "Slab_7"), "the regex is still anchored by its own ^$");
        Check(Matches("REGEX:^Col", "Col_1"), "the regex: prefix ignores case");

        // An element nobody labelled can only be reached by tag.
        Check(!Matches("*", null), "\"*\" does not match an element with no ElementId");
        Check(!Matches("*", "   "), "nor one whose ElementId is nothing but spaces");

        var broken = ElementFilter.Create(new[] { "regex:(unclosed" }, out var bad);
        Check(bad.Count == 1, "a regex that will not compile is reported, not thrown");
        Check(broken.MatchesEverything, "and is dropped, leaving nothing asked for");
    }

    // ---------------------------------------------------------------- repeats are the point

    static void SharedIdentifierTests()
    {
        Console.WriteLine("\nAn ElementId is the user's to repeat, and nothing renumbers it\n");

        // One identifier typed once against a list of curves: every element gets it as typed.
        var group = Elements("MyBeam", "MyBeam", "MyBeam", "Roof", null);

        Check(group.Take(3).All(e => e.ElementId == "MyBeam"),
              "three elements given one identifier all keep it, unsuffixed");

        var all = ElementFilter.Create(new[] { "MyBeam" }, out _);
        Check(all.SelectIndices(group).SequenceEqual(new[] { 0, 1, 2 }),
              "and asking for it by name returns all three");

        var glob = ElementFilter.Create(new[] { "MyBeam*" }, out _);
        Check(glob.SelectIndices(group).SequenceEqual(new[] { 0, 1, 2 }),
              "as does the wildcard");

        // A list of identifiers against the same curves: each element gets its own.
        var apart = Elements("MyBeam_0", "MyBeam_1", "MyBeam_2");
        var one = ElementFilter.Create(new[] { "MyBeam_1" }, out _);
        Check(one.SelectIndices(apart).SequenceEqual(new[] { 1 }),
              "identifiers given one per element pick out one element");
        Check(glob.SelectIndices(apart).Count == 3, "and the wildcard still gathers the group");

        // The tag is what separates elements that deliberately share an identifier.
        var single = ElementFilter.Create(new[] { "2" }, out _);
        Check(single.SelectIndices(group).SequenceEqual(new[] { 1 }),
              "a tag picks one element out of a group sharing an identifier");
    }

    // ---------------------------------------------------------------- ElementId and Tag together

    static void SelectionTests()
    {
        Console.WriteLine("\nOne input takes both handles, OR-ed, and empty means everything\n");

        var model = Elements("MyBeam", "MyBeam", "Roof", null);

        var everything = ElementFilter.Create(new List<string>(), out _);
        Check(everything.MatchesEverything, "both inputs empty is the default");
        Check(everything.SelectIndices(model).Count == 4, "and reports every element");

        var byTag = ElementFilter.Create(new[] { "2", "4" }, out _);
        Check(byTag.SelectIndices(model).SequenceEqual(new[] { 1, 3 }),
              "a term that reads as a number is tried as a tag: 2 and 4 select the 2nd and 4th");

        var both = ElementFilter.Create(new[] { "Roof", "4" }, out _);
        Check(both.SelectIndices(model).SequenceEqual(new[] { 2, 3 }),
              "identifiers and tags mix freely in the one list");

        var order = ElementFilter.Create(new[] { "4", "1" }, out _);
        Check(order.SelectIndices(model).SequenceEqual(new[] { 0, 3 }),
              "the order is the model's, not the order the tags were typed in");

        // A pattern and a tag landing on the same element must not report it twice - the results
        // sliced by these indices would be duplicated with it.
        var overlap = ElementFilter.Create(new[] { "MyBeam", "1" }, out _);
        Check(overlap.SelectIndices(model).SequenceEqual(new[] { 0, 1 }),
              "an element matched by both a tag and an ElementId is reported once");

        // Empty outputs otherwise look exactly like an analysis that produced nothing.
        var missing = ElementFilter.Create(new[] { "Nope*", "99" }, out _);
        Check(missing.SelectIndices(model).Count == 0, "a filter matching nothing selects nothing");

        var problems = missing.Unmatched(model);
        Check(problems.Count == 2, $"and says so, once per cause: got {problems.Count}");
        foreach (var problem in problems)
            Console.WriteLine($"         \"{problem}\"");

        var unlabelled = ElementFilter.Create(new[] { "Anything*" }, out _);
        var noneAtAll = unlabelled.Unmatched(new List<IElement> { new StubElement { Id = 1 } });
        Check(noneAtAll.Count == 1 && noneAtAll[0].Contains("ElementId input"),
              "a model with no identifiers at all is told where they come from");

        // A group sharing an identifier is listed once, not once per element.
        var listed = ElementFilter.Create(new[] { "Nope" }, out _);
        var message = listed.Unmatched(model)[0];
        Check(message.Contains("MyBeam") && message.IndexOf("MyBeam") == message.LastIndexOf("MyBeam"),
              $"the ElementIds it offers are listed once each: \"{message}\"");
    }

    // ------------------------------------------------- one input, two kinds of term

    static void TermKindTests()
    {
        Console.WriteLine("\nOne input works out what each term is\n");

        // Tag 1 is labelled MyBeam, tag 2 is labelled "12", tag 3 has no ElementId at all.
        var model = Elements("MyBeam", "12", null);

        var byTag = ElementFilter.Create(new[] { "3" }, out _);
        Check(byTag.SelectIndices(model).SequenceEqual(new[] { 2 }),
              "a whole number reaches an element that has no ElementId, by tag");

        var byText = ElementFilter.Create(new[] { "MyBeam" }, out _);
        Check(byText.SelectIndices(model).SequenceEqual(new[] { 0 }),
              "anything else is matched against the ElementIds");

        // "12" is both a tag nobody used and an ElementId somebody did. Guessing one would be
        // wrong half the time, so a numeric term is tried as both and matches whatever is there.
        var ambiguous = ElementFilter.Create(new[] { "12" }, out _);
        Check(ambiguous.SelectIndices(model).SequenceEqual(new[] { 1 }),
              "a number that is nobody's tag still finds the element labelled with it");

        // And when both exist, both come back.
        var twelveElements = Enumerable.Range(1, 12)
                                       .Select(i => (IElement)new StubElement { Id = i, ElementId = i == 3 ? "12" : null })
                                       .ToList();
        var bothWays = ElementFilter.Create(new[] { "12" }, out _);
        Check(bothWays.SelectIndices(twelveElements).SequenceEqual(new[] { 2, 11 }),
              "a number matches both the element labelled \"12\" and the element tagged 12");

        // Spellings that parse as a number but were far more likely typed as an identifier.
        var padded = Elements("007");
        Check(ElementFilter.Create(new[] { "007" }, out _).SelectIndices(padded).Count == 1,
              "\"007\" is an ElementId, matched as typed");
        Check(ElementFilter.Create(new[] { "7" }, out _).SelectIndices(padded).Count == 0,
              "and is not reached by the tag 7 it would parse as");

        // Assemble numbers from 1, so nothing can be tag 0.
        var zero = Elements("0");
        Check(ElementFilter.Create(new[] { "0" }, out _).SelectIndices(zero).Count == 1,
              "\"0\" is an ElementId, not a tag");

        var negative = ElementFilter.Create(new[] { "-3" }, out var bad);
        Check(bad.Count == 0 && negative.SelectIndices(model).Count == 0,
              "a negative number is a pattern, matches nothing here, and is not an error");

        // A regex is never read as a tag, whatever it spells.
        var numericRegex = ElementFilter.Create(new[] { "regex:^3$" }, out _);
        Check(numericRegex.SelectIndices(model).Count == 0,
              "a regex is matched against ElementIds only, never against tags");
    }

    // ---------------------------------------------------------------- nodes

    static void NodeFilterTests()
    {
        Console.WriteLine("\nNodal Displacements filters by tag - nodes have no other handle\n");

        // Assemble numbers the unique points it finds from 1, so a node's tag is its position
        // in the model's node list plus one.
        var nodes = Enumerable.Range(1, 6).Select(i => (int?)i).ToList();

        var all = NodeFilter.Select(new List<int>(), nodes, out var noneMissing);
        Check(all.SequenceEqual(Enumerable.Range(0, 6)), "an empty request is every node, not none");
        Check(noneMissing.Count == 0, "and nothing is missing");

        var some = NodeFilter.Select(new[] { 2, 5 }, nodes, out _);
        Check(some.SequenceEqual(new[] { 1, 4 }), "tags select the positions to read");

        var reversed = NodeFilter.Select(new[] { 5, 2 }, nodes, out _);
        Check(reversed.SequenceEqual(new[] { 1, 4 }),
              "the order is the model's, not the order the tags were typed in");

        var repeated = NodeFilter.Select(new[] { 3, 3, 3 }, nodes, out _);
        Check(repeated.SequenceEqual(new[] { 2 }), "a tag asked for twice is reported once");

        var partly = NodeFilter.Select(new[] { 2, 99 }, nodes, out var missing);
        Check(partly.SequenceEqual(new[] { 1 }), "the tags that are there are still reported");
        Check(missing.SequenceEqual(new[] { 99 }), "and the one that is not is handed back");

        var nonsense = NodeFilter.Select(new[] { 0, -4 }, nodes, out var alsoMissing);
        Check(nonsense.Count == 6 && alsoMissing.Count == 0,
              "tags below 1 are dropped, so a request of nothing but those is still every node");

        var supportNodes = new List<int?> { 1, 5, 9 };
        Check(NodeFilter.Preview(supportNodes) == "1, 5, 9",
              $"the tags there are can be listed: \"{NodeFilter.Preview(supportNodes)}\"");

        var many = Enumerable.Range(1, 40).Select(i => (int?)i).ToList();
        Check(NodeFilter.Preview(many).EndsWith("(40 in all)"),
              $"a long list is cut short: \"{NodeFilter.Preview(many)}\"");
    }

    // ---------------------------------------------------------------- supports, by point

    static void SupportPointTests()
    {
        Console.WriteLine("\nSupports filter by where they are, within the model's tolerance\n");

        // Three supports along X, as a Support component would have placed them.
        var supports = new List<Point3d>
        {
            new Point3d(0, 0, 0),
            new Point3d(5, 0, 0),
            new Point3d(10, 0, 0),
        };
        const double tolerance = 0.01;

        var all = NodeFilter.SelectByPoint(new List<Point3d>(), supports, tolerance, out var noneMissed);
        Check(all.SequenceEqual(new[] { 0, 1, 2 }), "an empty request is every support, not none");
        Check(noneMissed.Count == 0, "and nothing is missed");

        var exact = NodeFilter.SelectByPoint(new[] { new Point3d(5, 0, 0) }, supports, tolerance, out _);
        Check(exact.SequenceEqual(new[] { 1 }), "the point of a support finds that support");

        // The whole reason there is a tolerance: a point picked off the screen, or rebuilt from
        // the same geometry through a different route, never lands on the exact same double.
        var near = NodeFilter.SelectByPoint(new[] { new Point3d(5, 0, 0.005) }, supports, tolerance, out _);
        Check(near.SequenceEqual(new[] { 1 }), "and so does one within the tolerance of it");

        var far = NodeFilter.SelectByPoint(new[] { new Point3d(5, 0, 0.5) }, supports, tolerance, out var missed);
        Check(far.Count == 0, "a point beyond the tolerance finds nothing");
        Check(missed.Count == 1, "and is handed back, rather than dropped as a support carrying zero");

        var order = NodeFilter.SelectByPoint(
            new[] { new Point3d(10, 0, 0), new Point3d(0, 0, 0) }, supports, tolerance, out _);
        Check(order.SequenceEqual(new[] { 0, 2 }),
              "the order is the model's, not the order the points were given in");

        var twice = NodeFilter.SelectByPoint(
            new[] { new Point3d(5, 0, 0), new Point3d(5, 0, 0.001) }, supports, tolerance, out _);
        Check(twice.SequenceEqual(new[] { 1 }), "two points landing on one support report it once");

        // Coincident supports are unusual but legal, and both carry a reaction.
        var coincident = new List<Point3d> { new Point3d(0, 0, 0), new Point3d(0, 0, 0) };
        var both = NodeFilter.SelectByPoint(new[] { new Point3d(0, 0, 0) }, coincident, tolerance, out _);
        Check(both.SequenceEqual(new[] { 0, 1 }), "one point landing on two supports reports both");

        // A model deserialised from a .tcl carries no tolerance until one is given.
        var zeroTol = NodeFilter.SelectByPoint(new[] { new Point3d(5, 0, 0) }, supports, 0.0, out _);
        Check(zeroTol.SequenceEqual(new[] { 1 }),
              "a tolerance of zero falls back to a default rather than testing doubles for equality");

        var partial = NodeFilter.SelectByPoint(
            new[] { new Point3d(0, 0, 0), new Point3d(99, 0, 0) }, supports, tolerance, out var someMissed);
        Check(partial.SequenceEqual(new[] { 0 }), "the points that are there are still reported");
        Check(someMissed.Count == 1, "and only the one that is not is complained about");
    }

    static void Main()
    {
        Console.WriteLine("Element identifiers, node tags and the result filters");

        GlobTests();
        TermKindTests();
        SharedIdentifierTests();
        SelectionTests();
        NodeFilterTests();
        SupportPointTests();

        Console.WriteLine(fails == 0 ? "\nAll checks passed." : $"\n{fails} check(s) FAILED.");
        Environment.Exit(fails == 0 ? 0 : 1);
    }
}
