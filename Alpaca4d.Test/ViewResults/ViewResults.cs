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

    /// <summary>One of the rules the menu decides what to show by.</summary>
    static bool Asks(string rule, string family)
    {
        var method = Field.GetMethod(rule, BindingFlags.Public | BindingFlags.Static);
        return (bool)method.Invoke(null, new[] { Enum.Parse(Family, family) });
    }

    static string[] ComponentNames(string family)
    {
        var method = Field.GetMethod("ComponentNames", BindingFlags.Public | BindingFlags.Static);
        return (string[])method.Invoke(null, new[] { Enum.Parse(Family, family) });
    }

    static bool Near(double a, double b)
    {
        return Math.Abs(a - b) <= 1e-12 * Math.Max(Math.Abs(a), Math.Abs(b));
    }

    /// <summary>
    /// The Unit dropdown. Every result comes out of the solver in kN and m, and View Results turns
    /// it into the unit picked before anything is drawn - so a wrong factor here is a deflection a
    /// thousand times off, written on the model, in Info and in Values, with nothing looking wrong.
    /// </summary>
    static void Units(Assembly assembly)
    {
        var quantityType = assembly.GetType("Alpaca4d.Gh.Quantity", true);
        var units = assembly.GetType("Alpaca4d.Gh.ResultUnits", true);
        var unitType = assembly.GetType("Alpaca4d.Gh.ResultUnit", true);

        Func<string, object> Q = name => Enum.Parse(quantityType, name);
        Func<string, object, object> Call = (name, quantity) =>
            units.GetMethod(name, BindingFlags.Public | BindingFlags.Static).Invoke(null, new[] { quantity });
        Func<object, string> SymbolOf = unit => (string)unitType.GetField("Symbol").GetValue(unit);
        Func<object, double> SizeOf = unit => (double)unitType.GetField("InSI").GetValue(unit);
        Func<string, string[]> Symbols = quantity => ((Array)Call("Of", Q(quantity))).Cast<object>().Select(SymbolOf).ToArray();
        Func<string, string, double> Factor = (quantity, symbol) =>
        {
            int index = Array.IndexOf(Symbols(quantity), symbol);
            return (double)units.GetMethod("Factor").Invoke(null, new object[] { Q(quantity), index });
        };
        Func<string, int, string> SymbolAt = (quantity, index) => Symbols(quantity)[index];

        var quantityOf = Field.GetMethod("QuantityOf", BindingFlags.Public | BindingFlags.Static);
        Func<string, int, string> Of = (family, component) =>
            quantityOf.Invoke(null, new object[] { Enum.Parse(Family, family), component }).ToString();

        Section("Each component is offered the units of what it is");
        {
            for (int i = 0; i < 4; i++)
                Check(Of("Displacement", i) == "Length", $"Displacement {ComponentNames("Displacement")[i]} is a length");

            var beam = ComponentNames("BeamForce");
            for (int i = 0; i < beam.Length; i++)
            {
                string expected = i < 3 ? "Force" : "Moment";
                Check(Of("BeamForce", i) == expected, $"beam {beam[i]} is a {expected.ToLower()}");
            }

            // Per unit width: the three membrane forces and the two shears are a force per length,
            // the three bending moments in the middle a moment per length.
            var shell = ComponentNames("ShellForce");
            for (int i = 0; i < shell.Length; i++)
            {
                string expected = shell[i].StartsWith("m") ? "MomentPerLength" : "ForcePerLength";
                Check(Of("ShellForce", i) == expected, $"shell {shell[i]} is a {expected}");
            }

            foreach (var family in new[] { "BeamStress", "ShellStress", "BrickStress" })
                for (int i = 0; i < ComponentNames(family).Length; i++)
                    Check(Of(family, i) == "Stress", $"{family} {ComponentNames(family)[i]} is a stress");

            var reaction = ComponentNames("Reaction");
            for (int i = 0; i < reaction.Length; i++)
            {
                string expected = i < 4 ? "Force" : "Moment";
                Check(Of("Reaction", i) == expected, $"reaction {reaction[i]} is a {expected.ToLower()}");
            }
        }

        Section("The units offered, smallest first");
        {
            // Saved as a position in the list, so a unit already there never moves: a new one goes
            // on the end, or every saved file opens one unit along.
            Check(Symbols("Length").SequenceEqual(new[] { "mm", "cm", "m" }), "length: mm, cm, m");
            Check(Symbols("Force").SequenceEqual(new[] { "N", "kN", "MN" }), "force: N, kN, MN");
            Check(Symbols("Moment").SequenceEqual(new[] { "N·mm", "N·m", "kN·cm", "kN·m", "MN·m" }), "moment: N·mm, N·m, kN·cm, kN·m, MN·m");
            Check(Symbols("Stress").SequenceEqual(new[] { "kPa", "MPa", "kN/cm²" }), "stress: kPa, MPa, kN/cm²");
            Check(Symbols("ForcePerLength").SequenceEqual(new[] { "N/m", "kN/m", "MN/m" }), "force per length: N/m, kN/m, MN/m");
            Check(Symbols("MomentPerLength").SequenceEqual(new[] { "N·m/m", "kN·m/m", "MN·m/m" }), "moment per length: N·m/m, kN·m/m, MN·m/m");

            foreach (var quantity in Enum.GetNames(quantityType))
            {
                var sizes = ((Array)Call("Of", Q(quantity))).Cast<object>().Select(SizeOf).ToArray();
                bool ascending = Enumerable.Range(1, sizes.Length - 1).All(i => sizes[i] > sizes[i - 1]);
                Check(sizes.Length > 1 && sizes[0] > 0.0 && ascending, $"{quantity}: more than one, each bigger than the last");
            }
        }

        Section("The factor from the solver's kN and m");
        {
            Check(Near(Factor("Length", "mm"), 1000.0), "1 m is 1000 mm");
            Check(Near(Factor("Length", "cm"), 100.0), "1 m is 100 cm");
            Check(Near(Factor("Length", "m"), 1.0), "1 m is 1 m");

            Check(Near(Factor("Force", "N"), 1000.0), "1 kN is 1000 N");
            Check(Near(Factor("Force", "MN"), 1e-3), "1 kN is 0.001 MN");

            Check(Near(Factor("Moment", "N·mm"), 1e6), "1 kN·m is 10⁶ N·mm");
            Check(Near(Factor("Moment", "N·m"), 1000.0), "1 kN·m is 1000 N·m");
            Check(Near(Factor("Moment", "kN·cm"), 100.0), "1 kN·m is 100 kN·cm");
            Check(Near(Factor("Moment", "MN·m"), 1e-3), "1 kN·m is 0.001 MN·m");

            // The one that matters most: a steel stress. 235 N/mm² is 235 000 kN/m² in the solver.
            Check(Near(235000.0 * Factor("Stress", "MPa"), 235.0), "235 000 kN/m² is 235 MPa");
            Check(Near(235000.0 * Factor("Stress", "kN/cm²"), 23.5), "235 000 kN/m² is 23.5 kN/cm²");
            Check(Near(Factor("Stress", "kPa"), 1.0), "1 kN/m² is 1 kPa");

            Check(Near(Factor("ForcePerLength", "N/m"), 1000.0), "1 kN/m is 1000 N/m");
            Check(Near(Factor("ForcePerLength", "MN/m"), 1e-3), "1 kN/m is 0.001 MN/m");

            Check(Near(Factor("MomentPerLength", "N·m/m"), 1000.0), "1 kN·m/m is 1000 N·m/m");
            Check(Near(Factor("MomentPerLength", "MN·m/m"), 1e-3), "1 kN·m/m is 0.001 MN·m/m");

            // Beam stresses used to be turned into MPa by Core's ModelStress and are now turned by
            // the Unit, which starts at MPa - so the two have to agree, or View Results and Beam
            // Stresses stop reading the same numbers.
            var modelStress = AppDomain.CurrentDomain.GetAssemblies()
                                       .Select(x => x.GetType("Alpaca4d.ModelStress", false))
                                       .FirstOrDefault(x => x != null);
            if (modelStress != null)
            {
                double perMPa = (double)modelStress.GetProperty("PerMPa").GetValue(null);
                Check(Near(Factor("Stress", "MPa"), 1.0 / perMPa), "MPa agrees with the ModelStress Beam Stresses uses");
            }
            else
            {
                Check(false, "Core's ModelStress could be found to compare MPa against");
            }
        }

        Section("A new View Results starts in an engineer's units, an old file in the solver's");
        {
            Func<string, string> Default = quantity => SymbolAt(quantity, (int)Call("Default", Q(quantity)));
            Func<string, string> Model = quantity => SymbolAt(quantity, (int)Call("ModelUnit", Q(quantity)));

            Check(Default("Length") == "mm", "a deflection in mm");
            Check(Default("Force") == "kN" && Default("Moment") == "kN·m", "forces in kN, moments in kN·m");
            Check(Default("Stress") == "MPa", "a stress in MPa");
            Check(Default("ForcePerLength") == "kN/m" && Default("MomentPerLength") == "kN·m/m", "shell forces in kN/m and kN·m/m");

            // What a file saved before the Unit existed is put back in, so a Range wired into it is
            // still read in the unit it was typed in.
            Check(Model("Length") == "m" && Model("Force") == "kN" && Model("Moment") == "kN·m"
                  && Model("Stress") == "kPa" && Model("ForcePerLength") == "kN/m" && Model("MomentPerLength") == "kN·m/m",
                  "the solver's own: m, kN, kN·m, kPa, kN/m, kN·m/m");

            foreach (var quantity in Enum.GetNames(quantityType))
                Check(Near(Factor(quantity, Model(quantity)), 1.0), $"{quantity}: the solver's own unit leaves the numbers alone");
        }

        Section("Scaling a field scales every value, and the reaction vectors with them");
        {
            var field = Activator.CreateInstance(Field, true);
            var byNode = new Dictionary<int, double> { { 1, 0.012 }, { 2, -0.003 } };
            var byElement = new Dictionary<int, List<double>> { { 7, new List<double> { 1.0, 2.0 } } };
            var reactions = new List<Tuple<Rhino.Geometry.Plane, Rhino.Geometry.Vector3d, double>>
            {
                Tuple.Create(Rhino.Geometry.Plane.WorldXY, new Rhino.Geometry.Vector3d(1, 2, 3), 3.0)
            };
            Field.GetField("ByNode").SetValue(field, byNode);
            Field.GetField("ByElement").SetValue(field, byElement);
            Field.GetField("Reactions").SetValue(field, reactions);

            Field.GetMethod("Scale").Invoke(field, new object[] { 1000.0 });

            var node = (Dictionary<int, double>)Field.GetField("ByNode").GetValue(field);
            var element = (Dictionary<int, List<double>>)Field.GetField("ByElement").GetValue(field);
            var reaction = ((List<Tuple<Rhino.Geometry.Plane, Rhino.Geometry.Vector3d, double>>)Field.GetField("Reactions").GetValue(field))[0];

            Check(Near(node[1], 12.0) && Near(node[2], -3.0), "a displacement of 0.012 m reads 12, of -0.003 m reads -3");
            Check(Near(element[7][0], 1000.0) && Near(element[7][1], 2000.0), "every integration point of an element");
            Check(Near(reaction.Item3, 3000.0) && Near(reaction.Item2.Z, 3000.0), "a reaction's value and the vector its arrows are drawn from");

            // The readers' lists are left as they were: a scaled field is a new one.
            Check(Near(byNode[1], 0.012) && Near(byElement[7][0], 1.0) && Near(reactions[0].Item3, 3.0),
                  "and what it was scaled from is left alone");
        }
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

            // The dropdown saves its choice as a position, so a family added later goes on the end.
            // Slipped in beside the beam forces, it would reopen every saved file one family along.
            Check(names[6] == "Beam stresses" && families[6] == "BeamStress", "6 is the beam stresses, added last");
        }

        Section("The Result menu offers every family once, beams before shells before solids");
        {
            // The menu has an order of its own, and its position is turned into a family through
            // it - so it must name every family, each once, or a family would be unreachable.
            var order = ((Array)Field.GetField("MenuOrder", BindingFlags.Public | BindingFlags.Static).GetValue(null))
                        .Cast<object>().Select(x => x.ToString()).ToArray();
            var families = Enum.GetNames(Family);

            Check(order.Length == families.Length && order.Distinct().Count() == families.Length
                  && families.All(order.Contains), "every family, each once");

            var expected = new[] { "Displacement", "BeamForce", "BeamStress", "ShellForce", "ShellStress", "BrickStress", "Reaction" };
            Check(order.SequenceEqual(expected), "Displacement, beam forces, beam stresses, shell forces, shell stresses, brick stresses, reactions");
        }

        Section("Beam forces, against what Beam Forces View offers");
        {
            // Read.ForceBeamColumn returns (n, mz, vy, my, vz, t). The menu is not in that order.
            var expected = new[] { "N", "Vy", "Vz", "Torsion", "My", "Mz" };
            Check(ComponentNames("BeamForce").SequenceEqual(expected),
                  "N, Vy, Vz, Torsion, My, Mz - the order Beam Forces View uses");
        }

        Section("Beam stresses, against what Beam Stresses offers");
        {
            // ResultField picks each one off the BeamStress struct by position in this list, so the
            // list has to match the component's outputs or σMy draws as σMz - with σx, which only
            // View Results draws, after its three parts.
            var expected = new[] { "σN", "σMy", "σMz", "σx", "σmax", "σmin", "τV", "τT", "VonMises" };
            Check(ComponentNames("BeamStress").SequenceEqual(expected),
                  "σN, σMy, σMz, σx, σmax, σmin, τV, τT, VonMises - Beam Stresses' outputs, σx after its parts");

            // Files saved before σx are moved one along from here, so this is the place it went in.
            int sigmaX = (int)Field.GetField("SigmaX", BindingFlags.Public | BindingFlags.Static).GetValue(null);
            Check(sigmaX == 3 && expected[sigmaX] == "σx", "σx is fourth, where σmax was");
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

            Check(Asks("HasLayers", "ShellStress"), "and it is the one family that reads through a thickness");
        }

        Section("Each control is offered only where it does something");
        {
            // One component covering six kinds of result has more knobs than any one of them
            // needs. These are the rules the menu hides controls by, and a rule that drifted would
            // put a layer dropdown on a reaction - which is what the menu looked like before.
            var everything = Enum.GetNames(Family);

            foreach (var family in everything)
            {
                bool layers = Asks("HasLayers", family);
                Check(layers == (family == "ShellStress"),
                      $"{family}: layer {(layers ? "offered" : "hidden")}");
            }

            foreach (var family in everything)
            {
                // A field painted on an element is as big as the element. A diagram and an arrow
                // stick out into space, and how far is a choice.
                bool scale = Asks("HasScale", family);
                Check(scale == (family == "BeamForce" || family == "Reaction"),
                      $"{family}: diagram scale {(scale ? "offered" : "hidden")}");
            }

            foreach (var family in everything)
            {
                bool style = Asks("HasReactionStyle", family);
                Check(style == (family == "Reaction"),
                      $"{family}: reaction style {(style ? "offered" : "hidden")}");
            }

            foreach (var family in everything)
            {
                // Only a solid stress can be read in a frame other than the global one. A beam
                // force is already in the beam's axes and a shell stress in the shell's, and a
                // displacement and a reaction are vectors with nowhere else to go.
                bool axes = Asks("HasAxes", family);
                Check(axes == (family == "BrickStress"),
                      $"{family}: axes choice {(axes ? "offered" : "hidden")}");
            }
        }

        Section("Solid stresses, against what Brick Stresses View offers");
        {
            // Index notation, because the frame is the Axes dropdown's to choose: the solver
            // reports in the global axes, and Axes can turn the six components into the element's
            // own, taken from its node numbering. Naming them sigma-xx would be right for only one
            // of the two settings.
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

        Units(assembly);

        Console.WriteLine();
        Console.WriteLine(fails == 0 ? "all checks passed" : $"{fails} check(s) failed");
        Environment.Exit(fails == 0 ? 0 : 1);
    }
}
