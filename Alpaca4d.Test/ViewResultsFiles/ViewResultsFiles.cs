// View Results, saved and opened.
//
// The Component dropdown saves its choice as a position in its list, and σx went into the beam
// stresses' list fourth, where σmax was. So a file saved before it says 7 for Von Mises, which is
// now τT, and View Results moves such a Component one along on load - only for beam stresses, only
// from σmax on, and only for a file saved without the mark a newer one carries.
//
// The real component is saved and opened, as a file is. An older file is made by taking the marks
// out of what a newer one saved, which is all that tells the two apart.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using GH_IO.Serialization;
using Grasshopper.Kernel;

class ViewResultsFilesTest
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

    static Alpaca4d.UIWidgets.MenuDropDown DropDown(Alpaca4d.Gh.ViewResults view, string field) =>
        (Alpaca4d.UIWidgets.MenuDropDown)typeof(Alpaca4d.Gh.ViewResults)
            .GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(view);

    static Alpaca4d.UIWidgets.MenuSlider Slider(Alpaca4d.Gh.ViewResults view, string field) =>
        (Alpaca4d.UIWidgets.MenuSlider)typeof(Alpaca4d.Gh.ViewResults)
            .GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(view);

    /// <summary>What a solve does with the model wired in before it reads anything from it.</summary>
    static void Wire(Alpaca4d.Gh.ViewResults view, Alpaca4d.Model model)
    {
        typeof(Alpaca4d.Gh.ViewResults).GetField("_model", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(view, model);
        typeof(Alpaca4d.Gh.ViewResults).GetMethod("FitScaleRange", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(view, null);
    }

    static Alpaca4d.Gh.ViewResults Make()
    {
        var view = new Alpaca4d.Gh.ViewResults();
        view.CreateAttributes();
        return view;
    }

    /// <summary>What the Component dropdown shows.</summary>
    static string Shown(Alpaca4d.Gh.ViewResults view)
    {
        var component = DropDown(view, "_ddComponent");
        return component.Items[component.Value].content;
    }

    static string Family(Alpaca4d.Gh.ViewResults view)
    {
        var family = DropDown(view, "_ddFamily");
        return family.Items[family.Value].content;
    }

    /// <summary>
    /// A View Results showing <paramref name="family"/> at <paramref name="component"/>, saved and
    /// opened in a new one - with the marks named in <paramref name="older"/> taken out first, as a
    /// file saved before them would be without them.
    /// </summary>
    static Alpaca4d.Gh.ViewResults SaveAndOpen(int family, int component, params string[] older)
    {
        var view = Make();
        DropDown(view, "_ddFamily").Value = family;
        DropDown(view, "_ddComponent").Value = component;

        var archive = new GH_Archive();
        archive.CreateNewRoot(false);
        var container = archive.GetRootNode.CreateChunk("Container");
        view.Write(container);
        foreach (var mark in older)
            Check(((GH_Chunk)container).RemoveItem(mark), $"(the mark {mark} is taken out)");
        var bytes = archive.Serialize_Binary();

        var opened = new GH_Archive();
        opened.Deserialize_Binary(bytes);
        var back = Make();
        Check(back.Read(opened.GetRootNode.FindChunk("Container")), "(opened)");
        return back;
    }

    static int Main(string[] args)
    {
        // The Result menu's own order: Displacement, Beam forces, Beam stresses, Shell forces,
        // Shell stresses, Brick stresses, Reactions.
        const int beamStressInMenu = 2, shellStressInMenu = 4;

        Section("Saved with σx among the beam stresses");
        {
            var view = SaveAndOpen(beamStressInMenu, 3);
            Check(Family(view) == "Beam stresses" && Shown(view) == "σx", $"σx stays σx ({Shown(view)})");

            view = SaveAndOpen(beamStressInMenu, 8);
            Check(Shown(view) == "VonMises", $"Von Mises stays Von Mises ({Shown(view)})");
        }

        Section("Saved before σx");
        {
            // σmax was 3 and Von Mises 7.
            var view = SaveAndOpen(beamStressInMenu, 3, "BeamStressSigmaX");
            Check(Shown(view) == "σmax", $"σmax, which was fourth, opens as σmax ({Shown(view)})");

            view = SaveAndOpen(beamStressInMenu, 7, "BeamStressSigmaX");
            Check(Shown(view) == "VonMises", $"Von Mises, which was eighth, opens as Von Mises ({Shown(view)})");

            view = SaveAndOpen(beamStressInMenu, 2, "BeamStressSigmaX");
            Check(Shown(view) == "σMz", $"σMz, before where σx went in, is left where it was ({Shown(view)})");

            view = SaveAndOpen(shellStressInMenu, 5, "BeamStressSigmaX");
            Check(Family(view) == "Shell stresses" && Shown(view) == "VonMises",
                  $"another family's Component is left alone: shell Von Mises ({Shown(view)})");
        }

        Section("Saved before the Result menu had an order of its own, and before σx");
        {
            // Then the dropdown held the family's place in the enum, where beam stresses are 6.
            var view = SaveAndOpen(6, 7, "BeamStressSigmaX", "ResultMenuOrder", "ResultUnits");
            Check(Family(view) == "Beam stresses", $"the family comes back as beam stresses ({Family(view)})");
            Check(Shown(view) == "VonMises", $"and Von Mises as Von Mises ({Shown(view)})");
        }

        Section("The deformation scale's range follows the model wired in");
        {
            var view = Make();
            var scale = Slider(view, "_slDeformScale");
            Func<string> Range = () => $"{scale.MinValue} to {scale.MaxValue} at {scale.Value}";
            Check(scale.MinValue == 0.0 && scale.MaxValue == 200.0, $"a new one runs from 0 to 200 ({Range()})");

            scale.Value = 50.0;
            Wire(view, new Alpaca4d.Model { IsModal = true });
            Check(scale.MinValue == -2.0 && scale.MaxValue == 2.0 && scale.Value == 1.0 && scale.NumDecimals == 2,
                  $"a natural vibration model makes it -2.00 to 2.00, at 1 rather than an out-of-range 50 ({Range()}, {scale.NumDecimals} decimals)");

            scale.Value = -1.25;
            Wire(view, new Alpaca4d.Model { IsModal = true });
            Check(scale.Value == -1.25, $"wired in again, it stays where it was put ({Range()})");

            Wire(view, new Alpaca4d.Model());
            Check(scale.MinValue == 0.0 && scale.MaxValue == 200.0 && scale.Value == 1.0 && scale.NumDecimals == 1,
                  $"any other model takes it back to 0.0 to 200.0 ({Range()}, {scale.NumDecimals} decimal)");

            Wire(view, new Alpaca4d.Model { IsAnalysed = true, IsStatic = true, IsBuckling = true });
            Check(scale.MinValue == -2.0 && scale.MaxValue == 2.0 && scale.Value == 1.0 && scale.NumDecimals == 2,
                  $"so does a buckling model ({Range()}, {scale.NumDecimals} decimals)");

            Wire(view, new Alpaca4d.Model { IsAnalysed = true, IsStatic = true });
            Check(scale.MinValue == 0.0 && scale.MaxValue == 200.0, $"and a static one takes it back ({Range()})");

            // A range of its own, as Inigo the Dragon gives it, or as double-clicking the slider does.
            scale.MaxValue = 0.8;
            scale.Value = 0.4;
            Wire(view, new Alpaca4d.Model { IsModal = true });
            Check(scale.MinValue == 0.0 && scale.MaxValue == 0.8 && scale.Value == 0.4, $"a range of its own is kept ({Range()})");
        }

        Console.WriteLine();
        Console.WriteLine(fails == 0 ? "all checks passed" : $"{fails} check(s) failed");
        return fails == 0 ? 0 : 1;
    }
}
