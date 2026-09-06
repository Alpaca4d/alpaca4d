// The dropdown wiring behind View Results.
//
// The component offers a Result and a Component in two dropdowns, and turns the pair into a call
// on one of the readers. Every one of those readers hands back a tuple in an order of its own, and
// none of those orders is the order the names are offered in - Read.ForceBeamColumn returns
// (n, mz, vy, my, vz, t) against a menu reading N, Vy, Vz, Torsion, My, Mz.
//
// Getting that wrong is the worst kind of bug this component can have: nothing throws, nothing
// looks broken, and the user reads a shear diagram labelled as a moment. So the mapping is pinned
// here against the components that were already drawing these quantities, one by one.
//
// Only the mapping is checked. Reading an actual recorder file goes through PureHDF and a model
// that has been through Assemble, which needs an RTree - so what a value is, is checked here, and
// what it looks like has to be looked at in Rhino.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

class ViewResultsTest
{
    static int fails = 0;

    static void Check(bool ok, string what)
    {
        if (!ok) fails++;
        Console.WriteLine($"  [{(ok ? "PASS" : "FAIL")}] {what}");
    }

    static void Section(string title)
    {
        Console.WriteLine();
        Console.WriteLine(title);
    }

    /// <summary>ResultField is internal to the Grasshopper assembly, so it is reached by reflection.</summary>
    static Type Field;
    static Type Family;

    static string[] Names(string field)
    {
        return (string[])Field.GetField(field, BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public)
                              .GetValue(null);
    }

    static string[] ComponentNames(string family)
    {
        var method = Field.GetMethod("ComponentNames", BindingFlags.Public | BindingFlags.Static);
        return (string[])method.Invoke(null, new[] { Enum.Parse(Family, family) });
    }

    static void Main(string[] args)
    {
        var assembly = Assembly.LoadFrom(args.Length > 0 ? args[0] : "Alpaca4d.gha");
        Field = assembly.GetType("Alpaca4d.Gh.ResultField", true);
        Family = assembly.GetType("Alpaca4d.Gh.ResultFamily", true);

        Section("The Result dropdown covers every family, in order");
        {
            var families = Enum.GetNames(Family);
            var names = Names("FamilyNames");

            Check(names.Length == families.Length,
                  $"one name per family ({names.Length} names, {families.Length} families)");

            // The component turns the dropdown index straight into a ResultFamily, so the two
            // lists have to be in step or every family draws the one below it.
            Check(names[0] == "Displacement" && families[0] == "Displacement", "0 is Displacement");
            Check(names[1] == "Beam forces" && families[1] == "BeamForce", "1 is the beam forces");
            Check(names[2] == "Shell forces" && families[2] == "ShellForce", "2 is the shell forces");
            Check(names[3] == "Shell stresses" && families[3] == "ShellStress", "3 is the shell stresses");
            Check(names[4] == "Brick stresses" && families[4] == "BrickStress", "4 is the solid stresses");
            Check(names[5] == "Reactions" && families[5] == "Reaction", "5 is the reactions");
        }

        Section("Beam forces, against what Beam Forces View offers");
        {
            // Read.ForceBeamColumn returns (n, mz, vy, my, vz, t). The menu is not in that order.
            var expected = new[] { "N", "Vy", "Vz", "Torsion", "My", "Mz" };
            Check(ComponentNames("BeamForce").SequenceEqual(expected),
                  "N, Vy, Vz, Torsion, My, Mz - the order Beam Forces View uses");
        }

        Section("Shell forces, against what Shell Forces View offers");
        {
            // These the reader does return in the offered order, which is why they are taken
            // straight off the tuple.
            var expected = new[] { "fxx", "fyy", "fxy", "mxx", "myy", "mxy", "vxz", "vyz" };
            Check(ComponentNames("ShellForce").SequenceEqual(expected),
                  "the three membrane, the three bending, then the two transverse shears");
        }

        Section("Shell stresses, against what Shell Stresses View offers");
        {
            var expected = new[] { "σ11", "σ22", "σ12", "σ23", "σ31", "VonMises" };
            Check(ComponentNames("ShellStress").SequenceEqual(expected),
                  "the five plane stress components and the equivalent");

            var layers = (bool)Field.GetMethod("HasLayers", BindingFlags.Public | BindingFlags.Static)
                                    .Invoke(null, new[] { Enum.Parse(Family, "ShellStress") });
            Check(layers, "and it is the one family that reads through a thickness");

            foreach (var other in new[] { "Displacement", "BeamForce", "ShellForce", "BrickStress", "Reaction" })
            {
                var has = (bool)Field.GetMethod("HasLayers", BindingFlags.Public | BindingFlags.Static)
                                     .Invoke(null, new[] { Enum.Parse(Family, other) });
                if (has) { fails++; Console.WriteLine($"  [FAIL] {other} should not ask for a layer"); }
            }
        }

        Section("Solid stresses, against what Brick Stresses View offers");
        {
            // Index notation and the element's own axes. Named sigma-xx it would read as the
            // global ones, which is not what the reader reports.
            var expected = new[] { "σ11", "σ22", "σ33", "σ12", "σ23", "σ13", "VonMises" };
            Check(ComponentNames("BrickStress").SequenceEqual(expected),
                  "three direct, three shear, then the equivalent computed from them");
        }

        Section("Reactions");
        {
            var expected = new[] { "Force", "Fx", "Fy", "Fz", "Moment", "Mx", "My", "Mz" };
            Check(ComponentNames("Reaction").SequenceEqual(expected),
                  "the force and its three parts, then the moment and its three");

            // The first four read REACTION_FORCE and the last four REACTION_MOMENT, and the two
            // halves have to line up component for component or Mx reads off Fy.
            Check(expected.Take(4).Skip(1).SequenceEqual(new[] { "Fx", "Fy", "Fz" })
                  && expected.Skip(5).SequenceEqual(new[] { "Mx", "My", "Mz" }),
                  "and the moment half is the force half again, in the same order");
        }

        Section("Displacement");
        {
            var expected = new[] { "Magnitude", "Ux", "Uy", "Uz" };
            Check(ComponentNames("Displacement").SequenceEqual(expected),
                  "magnitude first, so the default shows the whole movement");
        }

        Section("Beam diagrams take the colour that belongs to the force");
        {
            // A diagram is read by its sign before its size, and two diagrams on one screen have
            // to be tellable apart - which the displacement gradient cannot do. The palette pairs
            // a force with its moment (N with Torsion, Vy with My, Vz with Mz), so the three that
            // have to differ are the three forces.
            var colour = Field.GetMethod("BeamForceColour", BindingFlags.Public | BindingFlags.Static);

            Func<int, double, object> Of = (component, value) => colour.Invoke(null, new object[] { component, value });

            var names = new[] { "N", "Vy", "Vz", "Torsion", "My", "Mz" };
            for (int i = 0; i < 6; i++)
            {
                Check(!Of(i, 1.0).Equals(Of(i, -1.0)),
                      $"{names[i]} reads one colour in tension and another in compression");
            }

            Check(!Of(0, 1.0).Equals(Of(1, 1.0)) && !Of(1, 1.0).Equals(Of(2, 1.0)) && !Of(0, 1.0).Equals(Of(2, 1.0)),
                  "N, Vy and Vz are three different colours");

            // The old behaviour: one gradient for everything, so every diagram came out the same.
            Check(!Of(0, 1.0).Equals(System.Drawing.Color.Gray), "and none of them is the fallback grey");
        }

        Section("Reactions can be drawn more than one way");
        {
            var styles = Names("ReactionStyles");

            Check(styles.Length == 3, $"three ways to draw one ({styles.Length})");
            Check(styles[0] == "Selected component",
                  "and the first is the one the Component dropdown names, which is what was wrong before");
        }

        Section("Every family offers something");
        {
            foreach (var family in Enum.GetNames(Family))
            {
                var names = ComponentNames(family);
                Check(names.Length > 0 && names.All(x => !string.IsNullOrWhiteSpace(x)),
                      $"{family} has {names.Length} components, all named");
            }
        }

        Console.WriteLine();
        Console.WriteLine(fails == 0 ? "all checks passed" : $"{fails} check(s) failed");
        Environment.Exit(fails == 0 ? 0 : 1);
    }
}
