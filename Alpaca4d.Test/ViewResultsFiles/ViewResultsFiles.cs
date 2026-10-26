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

        Console.WriteLine();
        Console.WriteLine(fails == 0 ? "all checks passed" : $"{fails} check(s) failed");
        return fails == 0 ? 0 : 1;
    }
}
