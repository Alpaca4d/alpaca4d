// Model View, the one that draws the model and its results, without Rhino.
//
// The real component, built in mono as Grasshopper builds it: its menus, which controls show for
// which choice, what a file keeps, and what each control does when it changes. What it draws, and
// the upgrades from the old Model View and View Results_WIP, need Rhino - a GH_Document will not
// even be made without it - and are checked there by grasshopper.sh.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using GH_IO.Serialization;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;

using Alpaca4d.UIWidgets;

#pragma warning disable CS0612, CS0618

class ModelViewFilesTest
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

    const BindingFlags Fields = BindingFlags.NonPublic | BindingFlags.Instance;

    // Kinds of quantity, by their place in Quantity, which is internal to the plugin.
    const int Force = 1, Moment = 2, Stress = 3;

    static T Field<T>(object component, string name) => (T)component.GetType().GetField(name, Fields).GetValue(component);

    /// <summary>A public field of one of the plugin's internal classes.</summary>
    static T Member<T>(object o, string name) => (T)o.GetType().GetField(name).GetValue(o);

    static T Make<T>() where T : GH_Component, new()
    {
        var component = new T();
        component.CreateAttributes();
        return component;
    }

    /// <summary>The component saved and opened again, as a file is: attributes made first, then read.</summary>
    static T SaveAndOpen<T>(T component) where T : GH_Component, new()
    {
        var archive = new GH_Archive();
        archive.CreateNewRoot(false);
        var container = archive.GetRootNode.CreateChunk("Container");
        component.Write(container);
        var bytes = archive.Serialize_Binary();

        var opened = new GH_Archive();
        opened.Deserialize_Binary(bytes);
        var back = Make<T>();
        Check(back.Read(opened.GetRootNode.FindChunk("Container")), "(opened)");
        return back;
    }

    static string Entry(MenuDropDown dropdown) =>
        dropdown.Value >= 0 && dropdown.Value < dropdown.Items.Count ? dropdown.Items[dropdown.Value].content : "(none)";

    /// <summary>What a control calls when it changes, by method name.</summary>
    static string[] Listeners(GH_Attr_Widget widget)
    {
        var field = widget.GetType().GetField("ValueChanged", Fields);
        var handler = (Delegate)field?.GetValue(widget);
        return handler == null ? new string[0] : handler.GetInvocationList().Select(x => x.Method.Name).ToArray();
    }

    /// <summary>Every control a component holds, by field name: in a field of its own, or under a result in the Results menu.</summary>
    static IEnumerable<(string Name, GH_Attr_Widget Widget)> Controls(GH_Component component)
    {
        foreach (var field in component.GetType().GetFields(Fields))
        {
            var value = field.GetValue(component);
            if (value is MenuDropDown[] dropdowns)
            {
                for (int i = 0; i < dropdowns.Length; i++)
                    yield return ($"{field.Name}[{i}]", dropdowns[i]);
            }
            else if (value is Array results && field.Name == "_results")
            {
                foreach (var result in results)
                {
                    var name = Member<MenuCheckBox>(result, "Tick").Tag;
                    yield return ($"{name} tick", Member<MenuCheckBox>(result, "Tick"));
                    yield return ($"{name} component", (GH_Attr_Widget)Member<MenuDropDown>(result, "Component") ?? Member<MenuCheckBoxGroup>(result, "Components"));
                    var units = Member<MenuDropDown[]>(result, "Units");
                    for (int i = 0; i < units.Length; i++)
                        if (units[i] != null)
                            yield return ($"{name} unit {i}", units[i]);
                }
            }
            else if (value is GH_Attr_Widget widget && !(widget is MenuStaticText) && !(widget is GH_ExtendableMenu))
            {
                yield return (field.Name, widget);
            }
        }
    }

    /// <summary>A result's controls in the Results menu, by its name there.</summary>
    static object Result(GH_Component view, string name)
    {
        foreach (var result in Field<Array>(view, "_results"))
            if (Member<MenuCheckBox>(result, "Tick").Tag == name)
                return result;
        throw new Exception("no result " + name);
    }

    static MenuCheckBox Tick(GH_Component view, string name) => Member<MenuCheckBox>(Result(view, name), "Tick");

    static MenuDropDown Component(GH_Component view, string name) => Member<MenuDropDown>(Result(view, name), "Component");

    static MenuCheckBoxGroup Forces(GH_Component view) => Member<MenuCheckBoxGroup>(Result(view, "Beam forces"), "Components");

    /// <summary>A group's boxes ticked, by label.</summary>
    static string TickedLabels(MenuCheckBoxGroup group) => string.Join(",", group.Ticked.Select(i => group.Labels[i]));

    static MenuDropDown Unit(GH_Component view, string name, int quantity) => Member<MenuDropDown[]>(Result(view, name), "Units")[quantity];

    /// <summary>A result ticked or unticked by a click, as the menu ticks it.</summary>
    static void Click(GH_Component view, string name, bool on)
    {
        var tick = Tick(view, name);
        tick.Active = on;
        view.GetType().GetMethod("OnResultTicked", Fields).Invoke(view, new object[] { tick, EventArgs.Empty });
    }

    static void Visible(GH_Component view, bool expected, params object[] widgets)
    {
        foreach (var w in widgets)
        {
            var widget = w as GH_Attr_Widget ?? Field<GH_Attr_Widget>(view, (string)w);
            if (widget.Visible != expected)
            {
                Check(false, $"{Name(w)} should be {(expected ? "shown" : "hidden")}");
                return;
            }
        }
        Check(true, $"{string.Join(", ", widgets.Select(Name))} {(expected ? "shown" : "hidden")}");
    }

    static string Name(object w) => w is string s ? s : w is MenuDropDown d ? $"{Entry(d)} dropdown" : w is MenuCheckBoxGroup g ? $"{string.Join(" ", g.Labels)} row" : w.GetType().Name;

    static int Main(string[] args)
    {
        Section("A new Model View");
        {
            var view = Make<Alpaca4d.Gh.ModelView>();
            var results = Field<Array>(view, "_results").Cast<object>().ToList();
            Check(string.Join(", ", results.Select(r => Member<MenuCheckBox>(r, "Tick").Tag)) ==
                  "Displacement, Beam forces, Beam stresses, Shell forces, Shell stresses, Brick stresses, Reactions",
                  "Results has a checkbox for each result, in View Results' order");
            Check(results.All(r => !Member<MenuCheckBox>(r, "Tick").Active), "and none is ticked, for the model alone");
            Check(string.Join(", ", view.Params.Input.Select(p => p.Name)) == "AlpacaModel, Step, ElementId, Colors, Range, LoadPattern",
                  "inputs: View Results' five in their order, then LoadPattern");
            Check(view.Params.Input[4].Access == GH_ParamAccess.list, "Range takes a list, one per result");
            Check(string.Join(", ", view.Params.Output.Select(p => p.Name)) == "AlpacaModel, Geometry, Info, Values, Colors",
                  "outputs: the model through, the geometry drawn, and View Results' three");
            Check(Field<MenuCheckBox>(view, "_ckExtruded").Active && Field<MenuCheckBox>(view, "_ckWires").Active
                  && Field<MenuCheckBox>(view, "_ckReleases").Active,
                  "Extruded, Element edges and End Releases start on");

            var menus = Field<GH_MenuCollection>(view.Attributes, "_collection").Menus.Select(m => m.Name).ToList();
            Check(string.Join(", ", menus) == "Results, Elements, Loads, Supports", $"menus: Results, then the old Model View's three ({string.Join(", ", menus)})");

            Check(view.ComponentGuid != new Guid("5EDB6364-D458-43D6-81E9-324DECA1FEA6")
                  && view.ComponentGuid != new Guid("4D8A0F26-3B71-4E59-9C84-A2F70D51B6E3"),
                  "a GUID of its own, so old files open on the old components");
            Check(view.Exposure == GH_Exposure.primary, "on the toolbar");
            Check(new Alpaca4d.Gh.ModelView_obsolete().Exposure == GH_Exposure.hidden
                  && new Alpaca4d.Gh.ViewResults_obsolete().Exposure == GH_Exposure.hidden,
                  "the old Model View and View Results are off it");
        }

        Section("Only the controls the choice uses");
        {
            var view = Make<Alpaca4d.Gh.ModelView>();
            var update = view.GetType().GetMethod("UpdateVisibility", Fields);

            Console.WriteLine("  No result:");
            Visible(view, true, "_ddColourBy");
            Visible(view, false, Component(view, "Displacement"), Forces(view), "_ckBands", "_ckValues", "_ddLayer", "_slDiagramScale");

            Click(view, "Beam forces", true);
            Console.WriteLine("  Beam forces:");
            Check(Member<MenuDropDown>(Result(view, "Beam forces"), "Component") == null
                  && string.Join(" ", Forces(view).Labels) == "N Vy Vz T My Mz"
                  && Forces(view).Direction == MenuCheckBoxGroup.LayoutDirection.Horizontal,
                  "its forces are a row of checkboxes, N Vy Vz T My Mz, rather than a dropdown");
            Check(TickedLabels(Forces(view)) == "N", "N ticked to start with");
            Visible(view, true, Forces(view), Unit(view, "Beam forces", Force), "_slDiagramScale", "_ckValues");
            Visible(view, false, Unit(view, "Beam forces", Moment), "_ddColourBy", "_ckBands", "_ddLayer", "_slArrowScale");

            Forces(view)[4] = true;
            Forces(view)[5] = true;
            update.Invoke(view, null);
            Console.WriteLine("  N, My and Mz ticked:");
            Visible(view, true, Unit(view, "Beam forces", Force), Unit(view, "Beam forces", Moment));
            Forces(view)[0] = false;
            update.Invoke(view, null);
            Console.WriteLine("  My and Mz:");
            Visible(view, true, Unit(view, "Beam forces", Moment));
            Visible(view, false, Unit(view, "Beam forces", Force));
            Check(Listeners(Forces(view)).SequenceEqual(new[] { "OnWidgetChanged" }), "a click on the row runs the solution again, once");

            Click(view, "Shell stresses", true);
            Console.WriteLine("  Beam forces and Shell stresses:");
            Check(Tick(view, "Beam forces").Active && Tick(view, "Shell stresses").Active, "both ticked");
            Visible(view, true, Forces(view), "_slDiagramScale", Component(view, "Shell stresses"), Unit(view, "Shell stresses", Stress), "_ddLayer", "_ckBands");

            Click(view, "Shell forces", true);
            Console.WriteLine("  Shell forces:");
            Check(!Tick(view, "Shell stresses").Active && Tick(view, "Beam forces").Active,
                  "ticking Shell forces unticks Shell stresses, which colour the same faces, and leaves Beam forces");
            Visible(view, false, Component(view, "Shell stresses"), "_ddLayer");

            Click(view, "Reactions", true);
            Visible(view, true, "_ddReaction", "_slArrowScale");

            foreach (var name in new[] { "Beam forces", "Shell forces", "Reactions" })
                Click(view, name, false);
            Visible(view, true, "_ddColourBy");
            Visible(view, false, "_ckValues", "_slArrowScale");

            Visible(view, false, "_slTextSize");
            Field<MenuCheckBox>(view, "_ckNodeIds").Active = true;
            update.Invoke(view, null);
            Visible(view, true, "_slTextSize");

            view.GetType().GetMethod("ShowResult", Fields).Invoke(view, new object[] { 6, 6 });
            Check(Tick(view, "Brick stresses").Active && Field<Array>(view, "_results").Cast<object>().Count(r => Member<MenuCheckBox>(r, "Tick").Active) == 1
                  && Entry(Component(view, "Brick stresses")) == "VonMises",
                  "ShowResult, for scripts, ticks one result alone at a component");
            view.GetType().GetMethod("ShowResult", Fields).Invoke(view, new object[] { 2, 1 });
            Check(Tick(view, "Beam forces").Active && TickedLabels(Forces(view)) == "Vy", "and one beam force alone");
        }

        Section("A row of checkboxes");
        {
            // The widget itself: laid out at a width, as a menu panel lays it out.
            Func<MenuCheckBoxGroup.LayoutDirection, MenuCheckBoxGroup> make = direction =>
            {
                var group = new MenuCheckBoxGroup(0, "forces") { Direction = direction };
                foreach (var label in new[] { "N", "Vy", "Vz", "T", "My", "Mz" })
                    group.Add(label);
                return group;
            };
            Func<MenuCheckBoxGroup, float, float> height = (group, width) =>
            {
                group.UpdateBounds(new System.Drawing.PointF(0, 0), width);
                group.Layout();
                return group.Height;
            };

            var row = make(MenuCheckBoxGroup.LayoutDirection.Horizontal);
            var column = make(MenuCheckBoxGroup.LayoutDirection.Vertical);
            float one = height(row, 400f);
            float six = height(column, 400f);
            float narrow = height(row, 2 * row.ComputeMinSize().Width + 5f);
            Check(one > 0 && Math.Abs(six - (6 * one + 5 * 5f)) < 0.5f, $"a row is one line high, a column six ({one:F0} and {six:F0} px)");
            Check(Math.Abs(narrow - (3 * one + 2 * 5f)) < 0.5f, $"a row too narrow for six goes on in the next line: two to a line, three lines ({narrow:F0} px)");
            Check(row.ComputeMinSize().Width < column.ComputeMinSize().Width + 1f && row.ComputeMinSize().Width > 0,
                  "and asks for no more width than one box, so it never widens the menu");

            row[1] = true;
            row[4] = true;
            var archive = new GH_Archive();
            archive.CreateNewRoot(false);
            row.Write(archive.GetRootNode);
            var back = make(MenuCheckBoxGroup.LayoutDirection.Horizontal);
            back.Read(archive.GetRootNode);
            Check(TickedLabels(back) == "Vy,My", "the boxes ticked are saved and read back");
            var empty = new GH_Archive();
            empty.CreateNewRoot(false);
            var untouched = make(MenuCheckBoxGroup.LayoutDirection.Horizontal);
            untouched[0] = true;
            Check(untouched.Read(empty.GetRootNode) && TickedLabels(untouched) == "N", "a file with nothing for the row leaves it as it was");
        }

        Section("Saved and opened");
        {
            var view = Make<Alpaca4d.Gh.ModelView>();
            Tick(view, "Reactions").Active = true;
            Component(view, "Reactions").Value = 3;
            Tick(view, "Beam stresses").Active = true;
            Component(view, "Beam stresses").Value = 8;
            Unit(view, "Beam stresses", Stress).Value = 0;
            Field<MenuDropDown>(view, "_ddColourBy").Value = 2;
            Field<MenuCheckBox>(view, "_ckShowSupports").Active = true;
            Field<MenuCheckBox>(view, "_ckExtruded").Active = false;
            Field<MenuSlider>(view, "_slLoadScale").Value = 4.5;
            Field<MenuSlider>(view, "_slArrowScale").Value = 3.25;
            Tick(view, "Beam forces").Active = true;
            Forces(view).TickOnly(2);
            Forces(view)[5] = true;

            var back = SaveAndOpen(view);
            Check(TickedLabels(Forces(back)) == "Vz,Mz", $"the beam forces ticked are kept ({TickedLabels(Forces(back))})");
            Check(Tick(back, "Reactions").Active && Tick(back, "Beam stresses").Active && !Tick(back, "Displacement").Active,
                  "the results ticked are kept");
            Check(Entry(Component(back, "Reactions")) == "Fz" && Entry(Component(back, "Beam stresses")) == "VonMises", "and each one's component");
            Check(Unit(back, "Beam stresses", Stress).Value == 0, "and its unit");
            Check(Field<MenuDropDown>(back, "_ddColourBy").Value == 2, "and Colour by");
            Check(Field<MenuCheckBox>(back, "_ckShowSupports").Active && !Field<MenuCheckBox>(back, "_ckExtruded").Active,
                  "and the checkboxes");
            Check(Field<MenuSlider>(back, "_slLoadScale").Value == 4.5 && Field<MenuSlider>(back, "_slArrowScale").Value == 3.25, "and the sliders");
            Visible(back, true, Component(back, "Reactions"), "_slArrowScale");
            Visible(back, false, "_ddColourBy");
        }

        Section("A file from before the loads were drawn to fit the model");
        {
            // Its Load scale is in metres of arrow per kN, and would now shrink or stretch the fit.
            var view = Make<Alpaca4d.Gh.ModelView>();
            Field<MenuSlider>(view, "_slLoadScale").Value = 4.5;
            var archive = new GH_Archive();
            archive.CreateNewRoot(false);
            var container = (GH_Chunk)archive.GetRootNode.CreateChunk("Container");
            view.Write(container);
            Check(container.RemoveItem("FittedLoads"), "(written as an older file)");

            var opened = new GH_Archive();
            opened.Deserialize_Binary(archive.Serialize_Binary());
            var back = Make<Alpaca4d.Gh.ModelView>();
            back.Read(opened.GetRootNode.FindChunk("Container"));
            Check(Field<MenuSlider>(back, "_slLoadScale").Value == 1.0, $"opens at Load scale 1 ({Field<MenuSlider>(back, "_slLoadScale").Value})");
        }

        Section("What a control does when it changes");
        {
            // A control listened to twice runs the solution twice for every change. The old Model
            // View and View Results listened once when their menus were made and again when a file
            // was read into them, so every one opened from a file did.
            var redraw = new HashSet<string>
            {
                "_ckWires", "_ckValues", "_slTextSize", "_ckNodeIds", "_ckElementIds", "_ckSectionNames", "_ckLocalAxes",
                "_slAxesSize", "_ckReleases", "_slReleaseSize", "_ckShowLoads", "_ckShowLoadValues", "_slLoadScale",
                "_ckShowSupports", "_slSupportScale"
            };

            foreach (var pair in new[] { Tuple.Create("new", Make<Alpaca4d.Gh.ModelView>()), Tuple.Create("opened", SaveAndOpen(Make<Alpaca4d.Gh.ModelView>())) })
            {
                string label = pair.Item1;
                var view = pair.Item2;
                var controls = Controls(view).ToList();
                var wrong = controls
                    .Select(c => (Name: c.Name, Listeners: Listeners(c.Widget)))
                    .Where(c => c.Listeners.Length != 1
                                || (redraw.Contains(c.Name) ? c.Listeners[0] != "OnDisplayChanged" : c.Listeners[0] == "OnDisplayChanged"))
                    .Select(c => $"{c.Name}: {string.Join("+", c.Listeners)}")
                    .ToList();
                Check(wrong.Count == 0, $"{label}: every one of {controls.Count} controls listened to once, the ones drawn over the model by a redraw alone" +
                      (wrong.Count == 0 ? "" : " - " + string.Join(", ", wrong)));
            }

            foreach (var pair in new[]
                     {
                         Tuple.Create("old Model View", (GH_Component)SaveAndOpen(Make<Alpaca4d.Gh.ModelView_obsolete>())),
                         Tuple.Create("View Results_WIP", (GH_Component)SaveAndOpen(Make<Alpaca4d.Gh.ViewResults_obsolete>()))
                     })
            {
                string label = pair.Item1;
                var old = pair.Item2;
                var twice = Controls(old).Where(c => Listeners(c.Widget).Length > 1).Select(c => c.Name).ToList();
                Check(twice.Count == 0, $"{label}, opened: no control listened to twice" + (twice.Count == 0 ? "" : " - " + string.Join(", ", twice)));
            }
        }

        Console.WriteLine();
        Console.WriteLine(fails == 0 ? "all checks passed" : $"{fails} check(s) failed");
        return fails == 0 ? 0 : 1;
    }
}
