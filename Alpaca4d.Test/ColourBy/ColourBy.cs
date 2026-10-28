// Colours for sections and materials, and Model View's Colour by.
//
// Two halves. The first is ElementColours, which decides what colour each element is drawn in when
// Model View colours by section or by material: which sections count as the same one, which colour
// wins when several were given, and that the ones given none are told apart.
//
// The second is the Colour input of the Section and Material Libraries, and what it does to files.
// Grasshopper reads a component's inputs by position, so adding one to a component that is already
// in people's definitions is the risky part: a file saved before it has one input fewer, and the
// Section Library adds a Gap input of its own for double angles, which used to be dropped, wires
// and all, every time a file was opened. So the components are saved and opened again here, in
// every layout a file can have, and must come back with each input in its place, its data with
// it, and no complaint from Grasshopper.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;

using Alpaca4d;
using Alpaca4d.Element;
using Alpaca4d.Generic;
using Alpaca4d.Material;
using Alpaca4d.Visualisation;
using GH_IO.Serialization;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;

class ColourByTest
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

    static int Main(string[] args)
    {
        string example = args[0];

        Palette();
        BySection();
        ByMaterial();
        SectionLibraryFiles(example);
        MaterialLibraryFiles(example);
        ColourInputs();
        SavedBeforeColour();
        foreach (var file in args.Skip(1))
            RealFiles(file);
        ModelViewFiles(example);

        Console.WriteLine();
        Console.WriteLine(fails == 0 ? "All checks passed." : $"{fails} check(s) failed.");
        return fails == 0 ? 0 : 1;
    }

    // ── ElementColours ─────────────────────────────────────────────────────────────────────────

    static UniaxialMaterialElastic Steel(string name = null) =>
        new UniaxialMaterialElastic(name, 210e6, 210e6, 0.0, 81e6, 0.3, 7850);

    static Alpaca4d.Section.ISection I(string name, IUniaxialMaterial material) =>
        new Alpaca4d.Section.ISection(name, 0.2, 0.1, 0.0085, 0.1, 0.0085, 0.0056, material);

    static Alpaca4d.Section.ElasticSection Elastic(double area, IUniaxialMaterial material) =>
        new Alpaca4d.Section.ElasticSection(null, area, 1e-5, 1e-6, 1e-7, 0.5, 0.5, material);

    static IBeam Beam(IUniaxialSection section) => new ForceBeamColumn(null, section, null);

    /// <summary>A solid without the eight-vertex mesh its constructor insists on, which needs Rhino.</summary>
    static IBrick Brick(IMultiDimensionMaterial material)
    {
        var brick = (SSPbrick)FormatterServices.GetUninitializedObject(typeof(SSPbrick));
        brick.Material = material;
        brick.Color = Colors.DefaultBrick;
        return brick;
    }

    static Model ModelOf(IEnumerable<IBeam> beams, IEnumerable<IShell> shells = null, IEnumerable<IBrick> bricks = null)
    {
        return new Model
        {
            Beams = beams.ToList(),
            Shells = (shells ?? new IShell[0]).ToList(),
            Bricks = (bricks ?? new IBrick[0]).ToList(),
        };
    }

    static void Palette()
    {
        Section("The colours picked for sections and materials given none");

        var first = Enumerable.Range(0, 30).Select(i => Colors.Categorical(i).ToArgb()).ToList();
        Check(first.Distinct().Count() == 30, "the first thirty are all different");
        Check(Colors.Categorical(30).ToArgb() == Colors.Categorical(0).ToArgb(), "and the thirty-first starts again");
        Check(Colors.Categorical(-1).ToArgb() == Colors.Categorical(29).ToArgb(), "a negative index does not throw");
        Check(first.All(argb => Color.FromArgb(argb).A == 255), "all opaque, as the viewport draws them");
    }

    static void BySection()
    {
        Section("By section");

        var steel = Steel("S355");
        var ipeA = I("IPE200", steel);
        var ipeB = I("IPE200", steel);          // a second Section Library set to the same profile
        var hea = I("HEA200", steel);
        var beams = new[] { Beam(ipeA), Beam(hea), Beam(ipeB), Beam(ipeA) };
        beams[1].Color = Color.Magenta;

        var own = new ElementColours(ModelOf(beams), ColourBy.Element);
        Check(beams.All(beam => own.Of(beam) == beam.Color), "Element keeps each element's own colour");

        var colours = new ElementColours(ModelOf(beams), ColourBy.Section);
        Check(colours.Of(beams[0]) == colours.Of(beams[2]) && colours.Of(beams[0]) == colours.Of(beams[3]),
              "two sections of the same name are one colour, though they are two objects");
        Check(colours.Of(beams[0]) != colours.Of(beams[1]), "IPE200 and HEA200 are told apart");
        Check(colours.Of(beams[0]) == Colors.Categorical(0) && colours.Of(beams[1]) == Colors.Categorical(1),
              "picked in the order the model meets them");

        ipeB.Color = Color.Gold;
        colours = new ElementColours(ModelOf(beams), ColourBy.Section);
        Check(colours.Of(beams[0]) == Color.Gold && colours.Of(beams[3]) == Color.Gold,
              "a colour given to one IPE200 colours every IPE200");
        Check(colours.Of(beams[1]) == Colors.Categorical(0),
              "and the next section given none takes the first picked colour, not the second");

        var a = Elastic(0.01, Steel());
        var b = Elastic(0.01, Steel());
        var c = Elastic(0.02, Steel());
        var unnamed = new[] { Beam(a), Beam(b), Beam(c) };
        colours = new ElementColours(ModelOf(unnamed), ColourBy.Section);
        Check(colours.Of(unnamed[0]) == colours.Of(unnamed[1]), "two unnamed sections that write the same Tcl are one colour");
        Check(colours.Of(unnamed[0]) != colours.Of(unnamed[2]), "and an unnamed section of another area is not");

        var nd = new ElasticIsotropicMaterial("C30/37", 33e6, 13.75e6, 0.2, 2500) { Color = Color.SlateGray };
        var shell = new ASDShellQ4(null, new Alpaca4d.Section.PlateFiberSection("Slab", 0.2, nd));
        var brick = Brick(nd);
        colours = new ElementColours(ModelOf(new IBeam[0], new[] { shell }, new[] { brick }), ColourBy.Section);
        Check(colours.Of(shell) == Colors.Categorical(0), "a shell goes by its section");
        Check(colours.Of(brick) == Color.SlateGray, "a solid, which has no section, goes by its material");
    }

    static void ByMaterial()
    {
        Section("By material");

        // Section Library falls back on UniaxialMaterialElastic.Steel, a new unnamed one each time.
        var beams = new[]
        {
            Beam(I("IPE200", UniaxialMaterialElastic.Steel)),
            Beam(I("HEA200", UniaxialMaterialElastic.Steel)),
            Beam(I("IPE300", Steel("S355"))),
            Beam(I("IPE400", Steel("S355"))),
        };

        var colours = new ElementColours(ModelOf(beams), ColourBy.Material);
        Check(colours.Of(beams[0]) == colours.Of(beams[1]),
              "the default steel of two Section Libraries is one material");
        Check(colours.Of(beams[2]) == colours.Of(beams[3]), "two S355 from two Material Libraries are one material");
        Check(colours.Of(beams[0]) != colours.Of(beams[2]), "the default steel and S355 are told apart");

        beams[3].Section.Material.Color = Color.Teal;
        colours = new ElementColours(ModelOf(beams), ColourBy.Material);
        Check(colours.Of(beams[2]) == Color.Teal, "a colour given to a material colours every element of it");

        var shell = new ASDShellQ4(null, new Alpaca4d.Section.PlateFiberSection(
            "Slab", 0.2, new ElasticIsotropicMaterial("C30/37", 33e6, 13.75e6, 0.2, 2500)));
        colours = new ElementColours(ModelOf(new IBeam[0], new[] { shell }), ColourBy.Material);
        Check(colours.Of(shell) == Colors.Categorical(0), "a shell goes by its section's material");

        var empty = new ForceBeamColumn(null, null, null) { Color = Color.Orchid };
        colours = new ElementColours(ModelOf(new IBeam[] { empty }), ColourBy.Material);
        Check(colours.Of(empty) == Color.Orchid, "an element with nothing to go by keeps its own colour");
    }

    // ── Files ──────────────────────────────────────────────────────────────────────────────────

    static readonly Guid SectionLibraryId = new Guid("0F82C248-4B58-4C25-9F85-6B7F7C4696C0");
    static readonly Guid MaterialLibraryId = new Guid("F50A7B47-2E73-4B57-BF7A-DB6D7EAC91D6");
    static readonly Guid ModelViewId = new Guid("5EDB6364-D458-43D6-81E9-324DECA1FEA6");

    static T Make<T>() where T : GH_Component, new() => (T)Make(typeof(T));

    static GH_Component Make(Type type)
    {
        var component = (GH_Component)Activator.CreateInstance(type);
        component.CreateAttributes();
        Detach(component);
        return component;
    }

    /// <summary>
    /// A switcher - Uniaxial, nD - takes its unit's inputs off before it saves or opens, and says
    /// "This should not happen" on Rhino's command line about any that still has a parent. Here
    /// they all have one, and Rhino's command line is native code mono has not got, so the parents
    /// are let go of first. Nothing that is saved depends on them.
    /// </summary>
    static void Detach(GH_Component component)
    {
        if (!(component is Alpaca4d.UIWidgets.GH_SwitcherComponent switcher)) return;
        foreach (var plug in switcher.EvalUnits.SelectMany(unit => unit.Inputs.Concat(unit.Outputs)))
            if (plug.Parameter.Attributes != null)
                plug.Parameter.Attributes.Parent = null;
    }

    /// <summary>What the inputs are called, in order.</summary>
    static string Inputs(GH_Component component) => string.Join(", ", component.Params.Input.Select(p => p.Name));

    /// <summary>The component saved and opened again in a new one, as a file is - and what Grasshopper said.</summary>
    static T SaveAndOpen<T>(T component, out List<string> messages) where T : GH_Component
    {
        var archive = new GH_Archive();
        archive.CreateNewRoot(false);
        Detach(component);
        component.Write(archive.GetRootNode.CreateChunk("Container"));
        var bytes = archive.Serialize_Binary();

        var opened = new GH_Archive();
        opened.Deserialize_Binary(bytes);
        return (T)Open(component.GetType(), opened.GetRootNode.FindChunk("Container"), opened, out messages);
    }

    static T Open<T>(GH_IReader container, GH_Archive archive, out List<string> messages) where T : GH_Component, new() =>
        (T)Open(typeof(T), container, archive, out messages);

    static GH_Component Open(Type type, GH_IReader container, GH_Archive archive, out List<string> messages)
    {
        // The archive has no list of messages until something is said.
        int before = archive.Messages?.Count ?? 0;
        var component = Make(type);
        bool read = component.Read(container);
        messages = (archive.Messages ?? new List<GH_Message>()).Skip(before).Select(m => m.Message).ToList();
        if (!read) messages.Add("Read returned false");
        return component;
    }

    /// <summary>The Container chunk of the first object of <paramref name="id"/> in a saved definition.</summary>
    static GH_IReader Stored(GH_Archive archive, Guid id)
    {
        var objects = archive.GetRootNode.FindChunk("Definition").FindChunk("DefinitionObjects");
        int count = objects.GetInt32("ObjectCount");
        for (int i = 0; i < count; i++)
        {
            var obj = objects.FindChunk("Object", i);
            if (obj.GetGuid("GUID") == id) return obj.FindChunk("Container");
        }
        return null;
    }

    static IGH_Param Input(GH_Component component, string name) => component.Params.Input.FirstOrDefault(p => p.Name == name);

    /// <summary>The one value an input holds when nothing is wired to it, in place of what it had.</summary>
    static void SetStored<T>(IGH_Param param, T value) where T : class, IGH_Goo
    {
        var persistent = (GH_PersistentParam<T>)param;
        persistent.PersistentData.Clear();
        persistent.PersistentData.Append(value);
    }

    static T Persistent<T>(IGH_Param param) where T : class, IGH_Goo =>
        (param as GH_PersistentParam<T>)?.PersistentData.AllData(true).OfType<T>().FirstOrDefault();

    static void SetFamily(Alpaca4d.Gh.SectionLibrary library, string family)
    {
        var type = typeof(Alpaca4d.Gh.SectionLibrary);
        var drop = (Alpaca4d.UIWidgets.MenuDropDown)type.GetField("familyDrop", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(library);
        drop.Value = drop.FindIndex(family);
        type.GetMethod("UpdateGapParameterVisibility", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(library, null);

        // What a solution would have remembered, and what the file saves the family by.
        type.GetField("storedFamily", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(library, family);
    }

    static void SectionLibraryFiles(string example)
    {
        Section("Section Library, saved and opened");

        var fresh = Make<Alpaca4d.Gh.SectionLibrary>();
        Check(Inputs(fresh) == "Section Name, Material, Colour", "a new one has a Colour input: " + Inputs(fresh));

        var archive = new GH_Archive();
        archive.ReadFromFile(example);
        var stored = Stored(archive, SectionLibraryId);
        var old = Open<Alpaca4d.Gh.SectionLibrary>(stored, archive, out var messages);
        Check(Inputs(old) == "Section Name, Material, Colour", "one from a file saved before Colour opens with it: " + Inputs(old));
        Check(messages.Count == 0, "with nothing for Grasshopper to complain about" + Said(messages));
        Check(Input(old, "Material").InstanceGuid == stored.FindChunk("param_input", 1).GetGuid("InstanceGuid"),
              "and Material read from Material's place, so its wire finds it");

        var given = Make<Alpaca4d.Gh.SectionLibrary>();
        SetStored(Input(given, "Colour"), new GH_Colour(Color.Gold));
        var back = SaveAndOpen(given, out messages);
        Check(Persistent<GH_Colour>(Input(back, "Colour"))?.Value.ToArgb() == Color.Gold.ToArgb(), "a colour set on the input is kept");
        Check(messages.Count == 0, "and opens without complaint" + Said(messages));

        var angles = Make<Alpaca4d.Gh.SectionLibrary>();
        SetFamily(angles, "2L");
        Check(Inputs(angles) == "Section Name, Material, Gap, Colour", "2L adds Gap after Material: " + Inputs(angles));
        SetStored(Input(angles, "Gap"), new GH_Number(0.025));
        SetStored(Input(angles, "Colour"), new GH_Colour(Color.Gold));
        back = SaveAndOpen(angles, out messages);
        Check(Inputs(back) == "Section Name, Material, Gap, Colour", "and it opens with the same inputs: " + Inputs(back));
        Check(Input(back, "Gap").InstanceGuid == Input(angles, "Gap").InstanceGuid,
              "Gap is the one saved, not a new one - so its wires come back with it");
        Check(Persistent<GH_Number>(Input(back, "Gap"))?.Value == 0.025, "Gap keeps its value");
        Check(Persistent<GH_Colour>(Input(back, "Colour"))?.Value.ToArgb() == Color.Gold.ToArgb(), "Colour keeps its value");
        Check(messages.Count == 0, "without complaint" + Said(messages));

        var oldAngles = Make<Alpaca4d.Gh.SectionLibrary>();
        SetFamily(oldAngles, "2L");
        oldAngles.Params.UnregisterInputParameter(Input(oldAngles, "Colour"));
        SetStored(Input(oldAngles, "Gap"), new GH_Number(0.03));
        back = SaveAndOpen(oldAngles, out messages);
        Check(Inputs(back) == "Section Name, Material, Gap, Colour", "a 2L saved before Colour gets it after Gap: " + Inputs(back));
        Check(Persistent<GH_Number>(Input(back, "Gap"))?.Value == 0.03, "and keeps its Gap");
        Check(messages.Count == 0, "without complaint" + Said(messages));
    }

    static void MaterialLibraryFiles(string example)
    {
        Section("Material Library, saved and opened");

        var fresh = Make<Alpaca4d.Gh.MaterialLibraryElastic>();
        Check(Inputs(fresh) == "Material Name, DatabasePath, Colour", "a new one has a Colour input: " + Inputs(fresh));

        var archive = new GH_Archive();
        archive.ReadFromFile(example);
        var old = Open<Alpaca4d.Gh.MaterialLibraryElastic>(Stored(archive, MaterialLibraryId), archive, out var messages);
        Check(Inputs(old) == "Material Name, DatabasePath, Colour", "one from a file saved before Colour opens with it: " + Inputs(old));
        Check(messages.Count == 0, "with nothing for Grasshopper to complain about" + Said(messages));

        var given = Make<Alpaca4d.Gh.MaterialLibraryElastic>();
        SetStored(Input(given, "Colour"), new GH_Colour(Color.Teal));
        var back = SaveAndOpen(given, out messages);
        Check(Persistent<GH_Colour>(Input(back, "Colour"))?.Value.ToArgb() == Color.Teal.ToArgb(), "a colour set on the input is kept");
        Check(messages.Count == 0, "and opens without complaint" + Said(messages));
    }

    // ── Every component with a Colour input ────────────────────────────────────────────────────

    /// <summary>The components that build a section or a material and so have a Colour input, by GUID.</summary>
    static readonly Dictionary<Guid, Type> Coloured = new[]
    {
        typeof(Alpaca4d.Gh.RectangleCS), typeof(Alpaca4d.Gh.RectangleHollowCS), typeof(Alpaca4d.Gh.CircleCS),
        typeof(Alpaca4d.Gh.ISection), typeof(Alpaca4d.Gh.DoubleLAngleCS), typeof(Alpaca4d.Gh.ElasticSection),
        typeof(Alpaca4d.Gh.PlateFiberSection), typeof(Alpaca4d.Gh.LayeredShellSection),
        typeof(Alpaca4d.Gh.SectionLibrary), typeof(Alpaca4d.Gh.MaterialLibraryElastic),
        typeof(Alpaca4d.Gh.Uniaxial), typeof(SimplexGh.nD),
    }.ToDictionary(type => Make(type).ComponentGuid, type => type);

    /// <summary>The units of a switcher - Uniaxial, nD - that build a material, by name.</summary>
    static readonly string[] MaterialUnits =
    {
        "UniaxialElastic (Alpaca4d)", "ElasticIsotropic (Alpaca4d)", "ElasticOrthotropic (Alpaca4d)",
    };

    /// <summary>The inputs the user wires to: a switcher's are its active unit's.</summary>
    static List<IGH_Param> Wired(GH_Component component)
    {
        if (component is Alpaca4d.UIWidgets.GH_SwitcherComponent switcher)
            return switcher.ActiveUnit?.Inputs.Select(plug => plug.Parameter).ToList() ?? new List<IGH_Param>();
        return component.Params.Input.ToList();
    }

    static void ColourInputs()
    {
        Section("Every section and material component has a Colour input");

        foreach (var type in Coloured.Values)
        {
            var component = Make(type);
            if (component is Alpaca4d.UIWidgets.GH_SwitcherComponent switcher)
            {
                // Read off each unit rather than switched to: switching writes to Rhino's command line.
                foreach (var unit in switcher.EvalUnits.Where(u => MaterialUnits.Contains(u.Name)))
                {
                    var inputs = unit.Inputs.Select(plug => plug.Parameter.Name).ToList();
                    Check(inputs.Last() == "Colour", $"{type.Name} ▸ {unit.Name}, last: " + string.Join(", ", inputs));
                }
            }
            else
            {
                Check(Wired(component).Last().Name == "Colour", $"{type.Name}, last: " + string.Join(", ", Wired(component).Select(p => p.Name)));
            }
        }
    }

    static void SavedBeforeColour()
    {
        Section("Each one, saved as it was before Colour and opened again");

        foreach (var type in Coloured.Values.Where(t => t != typeof(Alpaca4d.Gh.SectionLibrary)))
        {
            var component = Make(type);
            var colour = Wired(component).Last();
            if (component is Alpaca4d.UIWidgets.GH_SwitcherComponent switcher)
            {
                component.Params.UnregisterInputParameter(colour);
                switcher.ActiveUnit.Inputs.RemoveAll(plug => plug.Parameter == colour);
            }
            else
            {
                component.Params.UnregisterInputParameter(colour);
            }

            // A value of its own on the second input, to see it come back.
            var second = Wired(component)[1];
            if (second is Param_Number number) SetStored(number, new GH_Number(0.123));

            var saved = Wired(component).Select(p => (Name: p.Name, InstanceGuid: p.InstanceGuid)).ToList();
            var back = SaveAndOpen(component, out var messages);
            var opened = Wired(back);

            Check(opened.Select(p => p.Name).SequenceEqual(saved.Select(p => p.Name).Concat(new[] { "Colour" })),
                  $"{type.Name} opens with Colour last: " + string.Join(", ", opened.Select(p => p.Name)));
            Check(saved.All(p => opened.Any(o => o.Name == p.Name && o.InstanceGuid == p.InstanceGuid)),
                  $"{type.Name}'s other inputs are the ones saved, so their wires find them");
            if (second is Param_Number)
                Check(Persistent<GH_Number>(opened[1])?.Value == 0.123, $"{type.Name}'s {second.Name} keeps its value");
            Check(messages.Count == 0, $"{type.Name} opens without complaint" + Said(messages));
        }
    }

    /// <summary>
    /// Every section and material component in a definition saved before Colour - files people
    /// have, not ones made here - opened as Grasshopper opens them.
    /// </summary>
    static void RealFiles(string file)
    {
        Section("Opened from " + System.IO.Path.GetFileName(file));

        var archive = new GH_Archive();
        archive.ReadFromFile(file);
        var objects = archive.GetRootNode.FindChunk("Definition").FindChunk("DefinitionObjects");
        int count = objects.GetInt32("ObjectCount");
        for (int i = 0; i < count; i++)
        {
            var obj = objects.FindChunk("Object", i);
            if (!Coloured.TryGetValue(obj.GetGuid("GUID"), out var type)) continue;

            var container = obj.FindChunk("Container");
            var component = Open(type, container, archive, out var messages);
            var opened = Wired(component);
            var stored = StoredInputs(container, component);

            // By id rather than name: an input renamed since - ν was v - is still the one its wires point at.
            Check(messages.Count == 0 && opened.Any(p => p.Name == "Colour")
                  && stored.All(p => opened.Any(o => o.InstanceGuid == p.InstanceGuid)),
                  $"{type.Name}: Colour added, and its {stored.Count} saved inputs where they were" + Said(messages)
                  + (stored.All(p => opened.Any(o => o.InstanceGuid == p.InstanceGuid)) ? "" : " - opened with " + string.Join(", ", opened.Select(o => o.Name))));
        }
    }

    /// <summary>The names and ids of the inputs a component was saved with.</summary>
    static List<(string Name, Guid InstanceGuid)> StoredInputs(GH_IReader container, GH_Component component)
    {
        var found = new List<(string, Guid)>();
        if (component is Alpaca4d.UIWidgets.GH_SwitcherComponent switcher)
        {
            var units = container.FindChunk("EvalUnits");
            for (int u = 0; units.ChunkExists("unit", u); u++)
            {
                var unit = units.FindChunk("unit", u);
                if (unit.GetString("name") != container.FindChunk("ActiveUnit")?.GetString("unitname")) continue;
                var inputs = unit.FindChunk("params").FindChunk("input");
                for (int i = 0; inputs.ChunkExists("p", i); i++)
                    found.Add((inputs.FindChunk("p", i).GetString("Name"), inputs.FindChunk("p", i).GetGuid("InstanceGuid")));
            }
            return found;
        }

        for (int i = 0; container.ChunkExists("param_input", i); i++)
            found.Add((container.FindChunk("param_input", i).GetString("Name"), container.FindChunk("param_input", i).GetGuid("InstanceGuid")));
        return found;
    }

    static void ModelViewFiles(string example)
    {
        Section("Model View, saved and opened");

        var archive = new GH_Archive();
        archive.ReadFromFile(example);
        var old = Open<Alpaca4d.Gh.ModelView>(Stored(archive, ModelViewId), archive, out var messages);
        Check(ColourByOf(old) == 0, "one from a file saved before Colour by colours by element");
        Check(messages.Count == 0, "with nothing for Grasshopper to complain about" + Said(messages));

        Check(string.Join(", ", old.Params.Output.Select(p => p.Name)) == "AlpacaModel",
              "and without the Info output it was saved with");
        Check(!CheckBox(old, "_ckShowLoadValues").Active, "Show Values starts off");

        var section = Make<Alpaca4d.Gh.ModelView>();
        DropDown(section).Value = (int)ColourBy.Material;
        CheckBox(section, "_ckShowLoadValues").Active = true;
        var back = SaveAndOpen(section, out messages);
        Check(ColourByOf(back) == (int)ColourBy.Material, "Colour by is kept");
        Check(CheckBox(back, "_ckShowLoadValues").Active, "Show Values is kept");
    }

    static Alpaca4d.UIWidgets.MenuCheckBox CheckBox(Alpaca4d.Gh.ModelView view, string field) =>
        (Alpaca4d.UIWidgets.MenuCheckBox)typeof(Alpaca4d.Gh.ModelView)
            .GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(view);

    static Alpaca4d.UIWidgets.MenuDropDown DropDown(Alpaca4d.Gh.ModelView view) =>
        (Alpaca4d.UIWidgets.MenuDropDown)typeof(Alpaca4d.Gh.ModelView)
            .GetField("_ddColourBy", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(view);

    static int ColourByOf(Alpaca4d.Gh.ModelView view) => DropDown(view).Value;

    static string Said(List<string> messages) => messages.Count == 0 ? "" : ": " + string.Join(" | ", messages);
}
