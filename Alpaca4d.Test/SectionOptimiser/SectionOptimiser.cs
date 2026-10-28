// The section optimiser (Alpaca4d.Design.SectionSizing).
//
// The rules on their own - the order a list is tried in, which section a beam goes to, which beams a
// group holds - and then the rounds, run on recorder files OpenSees solves here. The beams are stubs and the
// model is never assembled, since assembling needs the native Rhino library: each round reads one of
// two files the test picks, so it decides what forces a design gets. The steel check itself is
// Alpaca4d.Test/SteelCheck's; here the expected sections come from the same check, one candidate at a
// time, so what is tested is how the optimiser chooses and when it stops.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Alpaca4d;
using Alpaca4d.Design;
using Alpaca4d.Generic;
using Alpaca4d.Material;
using Alpaca4d.Result;
using Alpaca4d.Section;
using ISection = Alpaca4d.Section.ISection;

class SectionOptimiserTest
{
    static int fails = 0;

    static void Check(bool ok, string what)
    {
        if (!ok) fails++;
        Console.WriteLine($"  [{(ok ? "PASS" : "FAIL")}] {what}");
    }

    static void Section(string title) => Console.WriteLine($"\n{title}");

    static UniaxialMaterialElastic Steel(string grade, double? rho = 7850)
    {
        return new UniaxialMaterialElastic(grade, 210e6, 210e6, 0.0, 80.77e6, 0.3, rho) { Grade = SteelGrade.Find(grade) };
    }

    // IPE-like sections, lightest first: name, h, b, tf, tw.
    static readonly List<IUniaxialSection> Catalogue = new List<IUniaxialSection>
    {
        new ISection("I120", 0.120, 0.064, 0.0063, 0.064, 0.0063, 0.0044, Steel("S355")),
        new ISection("I160", 0.160, 0.082, 0.0074, 0.082, 0.0074, 0.0050, Steel("S355")),
        new ISection("I200", 0.200, 0.100, 0.0085, 0.100, 0.0085, 0.0056, Steel("S355")),
        new ISection("I240", 0.240, 0.120, 0.0098, 0.120, 0.0098, 0.0062, Steel("S355")),
        new ISection("I300", 0.300, 0.150, 0.0107, 0.150, 0.0107, 0.0071, Steel("S355")),
        new ISection("I360", 0.360, 0.170, 0.0127, 0.170, 0.0127, 0.0080, Steel("S355")),
        new ISection("I400", 0.400, 0.180, 0.0135, 0.180, 0.0135, 0.0086, Steel("S355")),
    };

    class StubBeam : IBeam
    {
        public Rhino.Geometry.Curve Curve { get; set; }
        public Alpaca4d.Element.GeomTransf GeomTransf { get; set; }
        public IUniaxialSection Section { get; set; }
        public IIntegration BeamIntegration { get; set; }
        public int? INode { get; set; }
        public int? JNode { get; set; }
        public System.Drawing.Color Color { get; set; }
        public Alpaca4d.Element.ElementType Type => Alpaca4d.Element.ElementType.Beam;
        public int? Id { get; set; }
        public string ElementId { get; set; }
        public void SetTags() { }
        public void SetTopologyRTree(Model model) { }
        public string WriteTcl() => "";
        public IBeam WithSection(IUniaxialSection section) =>
            new StubBeam { Id = Id, ElementId = ElementId, Section = section, BeamIntegration = new Alpaca4d.BeamIntegration.NewtonContes(section, 5) };
    }

    static StubBeam Beam(int id, string elementId, IUniaxialSection section) =>
        new StubBeam { Id = id, ElementId = elementId, Section = section, BeamIntegration = new Alpaca4d.BeamIntegration.NewtonContes(section, 5) };

    // ---------------------------------------------------------------- the rules on their own

    static void Ordering()
    {
        Section("The order given");

        var a = new ElasticSection("a", 0.010, 1e-5, 1e-5, 1e-6, 0.5, 0.5, Steel("S355"));
        var b = new ElasticSection("b", 0.004, 1e-5, 1e-5, 1e-6, 0.5, 0.5, Steel("S355"));
        var c = new ElasticSection("c", 0.007, 1e-5, 1e-5, 1e-6, 0.5, 0.5, Steel("S355"));

        var order = SectionSizing.InOrder(new IUniaxialSection[] { a, c, b, a, null });
        Check(order.Select(x => x.ToString()).SequenceEqual(new[] { "a", "c", "b" }),
              $"kept as given, never by weight, each once where it first comes, nulls dropped: {string.Join(", ", order)}");
    }

    static void Choosing()
    {
        Section("Which section a beam goes to");

        var group = new SizingGroup { Design = new DesignGroup(), Sections = Catalogue.Take(3).ToList() };
        Func<double?[][], double, int[], int[]> pick = (table, target, floor) => SectionSizing.Choose(group, table, target, floor);

        Check(pick(new[] { new double?[] { 1.4, 0.9, 0.5 } }, 1.0, null)[0] == 1, "the first at or below the target");
        Check(pick(new[] { new double?[] { 1.4, 0.9, 0.5 } }, 0.8, null)[0] == 2, "a lower target, one further down the list");
        Check(pick(new[] { new double?[] { 1.0, 0.9, 0.5 } }, 1.0, null)[0] == 0, "exactly at the target passes");
        Check(pick(new[] { new double?[] { null, 1.3, 1.2 } }, 1.0, null)[0] == 2, "none passing: the one nearest, and unchecked ones are never picked");
        Check(pick(new[] { new double?[] { null, null, null } }, 1.0, null)[0] == -1, "none checked: -1, the beam keeps its section");
        Check(pick(new[] { new double?[] { 0.5, 0.4, 0.3 } }, 1.0, new[] { 2 })[0] == 2, "a floor keeps a beam from moving back up the list");

        var separate = pick(new[] { new double?[] { 0.9, 0.6, 0.3 }, new double?[] { 1.3, 0.95, 0.5 } }, 1.0, null);
        Check(separate[0] == 0 && separate[1] == 1, "each beam its own");

        group.Uniform = true;
        var uniform = pick(new[] { new double?[] { 0.9, 0.6, 0.3 }, new double?[] { 1.3, 0.95, 0.5 } }, 1.0, null);
        Check(uniform[0] == 1 && uniform[1] == 1, "uniform: one section, the one the worst beam needs");
        var gap = pick(new[] { new double?[] { 0.9, null, 0.3 }, new double?[] { 1.3, 0.95, 0.5 } }, 1.0, null);
        Check(gap[0] == 2 && gap[1] == 2, "uniform: a section one beam could not be checked with is not one for the group");
        var floored = pick(new[] { new double?[] { 0.9, 0.6, 0.3 }, new double?[] { 0.9, 0.6, 0.3 } }, 1.0, new[] { 0, 2 });
        Check(floored[0] == 2 && floored[1] == 2, "uniform: the highest floor holds for all");

        // Asked for as needed: nothing after the first that passes.
        group.Uniform = false;
        var asked = new List<int>();
        Func<int, int, double?> counted = (b, c) => { asked.Add(c); return new double?[] { 1.4, 0.9, 0.5 }[c]; };
        SectionSizing.Choose(group, 1, counted, 1.0);
        Check(asked.SequenceEqual(new[] { 0, 1 }), $"each beam is checked down its list only as far as the first that passes: {string.Join(", ", asked)}");
        group.Uniform = true;
        asked.Clear();
        Func<int, int, double?> both = (b, c) => { asked.Add(10 * b + c); return new[] { new double?[] { 0.9, 0.6, 0.3 }, new double?[] { 1.3, 0.95, 0.5 } }[b][c]; };
        var once = SectionSizing.Choose(group, 2, both, 1.0);
        Check(once[0] == 1 && asked.SequenceEqual(new[] { 0, 10, 1, 11 }), $"uniform: a section is dropped at the first beam it fails: {string.Join(", ", asked)}");
    }

    static void Families()
    {
        Section("Families of the catalogue");

        var hea200 = SteelCatalogue.Create("I", "HEA200", Steel("S275"), 0.0);
        var family = SteelCatalogue.FamilyOf(hea200);
        Check(family != null && family.Name == "HEA" && family.Sections.Count == 24, $"an HEA200's family is the 24 HEAs: {family?.Name}, {family?.Sections.Count}");
        Check(family.Sections.All(x => x.ToString().StartsWith("HEA") && x.Material == hea200.Material), "all HEAs, of its material");
        Check(family.Sections.Contains(hea200) && family.Sections.Count(x => x.ToString() == "HEA200") == 1, "with the HEA200 itself in its place");
        var areas = family.Sections.Select(x => SteelCatalogue.PlateArea("I", SteelCatalogue.FindEntry("I", x.ToString()))).ToList();
        Check(areas.Zip(areas.Skip(1), (a, b) => a <= b).All(x => x) && family.Sections[0].ToString() == "HEA100", "smallest area first, from the HEA100");

        var custom = new ISection("ipe300", 0.3, 0.15, 0.0107, 0.15, 0.0107, 0.0071, Steel("S355"));
        Check(SteelCatalogue.FamilyOf(custom)?.Name == "IPE", "a section is known by its kind and name, whatever the case");
        Check(SteelCatalogue.FamilyOf(new ISection("Welded 600", 0.6, 0.3, 0.02, 0.3, 0.02, 0.01, Steel("S355"))) == null, "a name the catalogue does not have: no family");
        Check(SteelCatalogue.FamilyOf(Plain[0]) == null, "nor has an Elastic Section");

        var chs = SteelCatalogue.FamilyOf(SteelCatalogue.Create("O", "26.9x2", Steel("S355"), 0.0));
        Check(chs != null && chs.Name == "CHS" && chs.Sections.Count == 205, $"a CHS's family is every CHS: {chs?.Sections.Count}");
        var angles = SteelCatalogue.FamilyOf(SteelCatalogue.Create("2L", "L50x50x5", Steel("S355"), 0.02));
        Check(angles != null && angles.Sections.All(x => ((DoubleLAngleCS)x).Gap == 0.02), "a pair of angles' family keeps its gap");

        Section("Beams in no group, sized within their family");

        var hea300 = SteelCatalogue.Create("I", "HEA300", Steel("S275"), 0.0);
        var beams = new List<IBeam> { Beam(1, "Col_1", hea200), Beam(2, "Col_2", hea200), Beam(3, "Beam_1", hea300), Beam(4, "Beam_2", custom), Beam(5, "Odd", Plain[0]) };
        var columns = new DesignGroup { Name = "Columns", ElementIds = new List<string> { "Col*" }, Sections = { hea300 } };
        var errors = new List<string>();
        var warnings = new List<string>();
        var remarks = new List<string>();
        var groups = SectionSizing.Resolve(beams, new[] { columns }, errors, warnings, null, remarks, SteelCatalogue.FamilyOf);
        Check(groups.Count == 3 && groups[0].Beams.SequenceEqual(new[] { 0, 1 }), "a design group comes first");
        Check(groups[0].Uniform && !groups[1].Uniform && !groups[2].Uniform, "a design group gives its beams one section; within a family each has its own");
        Check(groups[1].Label == "HEA family" && groups[1].Beams.SequenceEqual(new[] { 2 }) && groups[1].Sections.Count == 24, "an HEA300 in no group, among the HEAs");
        Check(groups[2].Label == "IPE family" && groups[2].Beams.SequenceEqual(new[] { 3 }), "an IPE among the IPEs");
        Check(errors.Count == 0 && warnings.Count == 0 && remarks.Count == 1 && remarks[0].Contains("element 5") && remarks[0].Contains("no family"),
              $"a section with no family keeps its place, which is a remark: {string.Join(" | ", remarks)}");

        remarks.Clear();
        groups = SectionSizing.Resolve(beams, new DesignGroup[0], errors, warnings, null, remarks, SteelCatalogue.FamilyOf);
        Check(groups.Count == 3 && groups[0].Beams.SequenceEqual(new[] { 0, 1 }) && groups[0].Label == "HEA family" && groups[1].Beams.SequenceEqual(new[] { 2 }),
              "with no design groups at all, every beam with a family takes part; beams sharing a section share a list");
        Check(!groups[0].Uniform, "but each has its own section of it");

        groups = SectionSizing.Resolve(beams, new DesignGroup[0], errors, warnings, Only("Beam_1", "Odd"), remarks, SteelCatalogue.FamilyOf);
        Check(groups.Count == 1 && groups[0].Beams.SequenceEqual(new[] { 2 }), "ElementId picks out of them");
        Check(warnings.Count == 1 && warnings[0].Contains("element 5"), $"and a beam it picks with no family is a warning: {warnings.FirstOrDefault()}");
    }

    // Sections for the rules on their own, with nothing to check.
    static readonly List<IUniaxialSection> Plain = Enumerable.Range(1, 4)
        .Select(k => (IUniaxialSection)new ElasticSection($"E{k}", 0.001 * k, 1e-5, 1e-5, 1e-6, 0.5, 0.5, Steel("S355")))
        .ToList();

    static void Grouping()
    {
        Section("Which beams a group holds");

        var beams = new List<IBeam>
        {
            Beam(1, "Col_1", Plain[0]), Beam(2, "Col_2", Plain[0]),
            Beam(3, "Beam_1", Plain[0]), Beam(4, "Beam_2", Plain[0]), Beam(5, null, Plain[0]),
        };

        var columns = new DesignGroup { Name = "Columns", ElementIds = new List<string> { "Col*" }, Sections = Plain.ToList() };
        var girders = new DesignGroup { Name = "Girders", ElementIds = new List<string> { "Beam_?", "5" }, Sections = { Plain[3], Plain[1] } };

        var errors = new List<string>();
        var warnings = new List<string>();
        var groups = SectionSizing.Resolve(beams, new[] { columns, girders }, errors, warnings);
        Check(errors.Count == 0 && warnings.Count == 0, "no problems");
        Check(groups.Count == 2 && groups[0].Beams.SequenceEqual(new[] { 0, 1 }), "a wildcard over ElementIds");
        Check(groups[1].Beams.SequenceEqual(new[] { 2, 3, 4 }), "and a tag, for a beam with no ElementId");
        Check(groups[1].Sections[0] == Plain[3] && groups[1].Sections[1] == Plain[1], "the sections in the order given, the heavier first");

        errors.Clear(); warnings.Clear();
        var overlap = new DesignGroup { Name = "Ones", ElementIds = new List<string> { "*_1" }, Sections = Plain.ToList() };
        SectionSizing.Resolve(beams, new[] { columns, girders, overlap }, errors, warnings);
        Check(errors.Count == 1 && errors[0].Contains("element 1, 3"), $"a beam in two groups is an error, naming it: {errors.FirstOrDefault()}");

        errors.Clear(); warnings.Clear();
        var empty = new DesignGroup { Name = "Empty", ElementIds = new List<string> { "Col*" } };
        var none = new DesignGroup { Name = "None", ElementIds = new List<string> { "Brace*" }, Sections = Plain.ToList() };
        var nameless = new DesignGroup { Name = "Nameless", Sections = Plain.ToList() };
        groups = SectionSizing.Resolve(beams, new[] { empty, none, nameless }, errors, warnings);
        Check(groups.Count == 0, "none of them holds anything");
        Check(warnings.Count == 2 && warnings.Any(w => w.Contains("\"Empty\" has no sections")) && warnings.Any(w => w.Contains("\"None\" matches no beam")),
              $"a group with no sections, or no beam, is a warning: {string.Join(" | ", warnings)}");
        Check(errors.Count == 1 && errors[0].Contains("\"Nameless\" names no beams"), $"a group with no ElementIds is an error: {errors.FirstOrDefault()}");

        Section("Which beams take part");

        var remarks = new List<string>();
        errors.Clear(); warnings.Clear();
        groups = SectionSizing.Resolve(beams, new[] { columns, girders }, errors, warnings, Only("Col_1"), remarks);
        Check(groups.Count == 1 && groups[0].Beams.SequenceEqual(new[] { 0 }), "only the beams picked, Col_1 out of the columns");
        Check(errors.Count == 0 && warnings.Count == 0 && remarks.Count == 1 && remarks[0].Contains("\"Girders\" holds none of the beams to optimise"),
              $"a group with none of them takes no part, and says so as a remark: {string.Join(" | ", remarks)}");

        remarks.Clear(); errors.Clear(); warnings.Clear();
        groups = SectionSizing.Resolve(beams, new[] { columns, girders, overlap }, errors, warnings, Only("Col_2", "Beam_2"), remarks);
        Check(errors.Count == 0, "a beam in two groups is no error when it is left out");
        Check(groups.Count == 2 && groups[0].Beams.SequenceEqual(new[] { 1 }) && groups[1].Beams.SequenceEqual(new[] { 3 }),
              "Col_2 with the columns, Beam_2 with the girders");
        Check(remarks.Count == 1 && remarks[0].Contains("\"Ones\""), "and the group of the beams left out takes no part");

        remarks.Clear(); errors.Clear(); warnings.Clear();
        groups = SectionSizing.Resolve(beams, new[] { columns }, errors, warnings, Only("Col*", "5"), remarks);
        Check(groups.Count == 1 && groups[0].Beams.SequenceEqual(new[] { 0, 1 }), "a wildcard and a tag");
        Check(warnings.Count == 1 && warnings[0].Contains("in no design group") && warnings[0].Contains("element 5"),
              $"a beam picked that is in no group has nowhere to go, which is a warning: {warnings.FirstOrDefault()}");

        remarks.Clear(); errors.Clear(); warnings.Clear();
        groups = SectionSizing.Resolve(beams, new[] { columns, girders }, errors, warnings, Only(), remarks);
        Check(groups.Count == 2 && groups[0].Beams.Count == 2 && groups[1].Beams.Count == 3 && warnings.Count == 0 && remarks.Count == 0,
              "an empty filter: every beam in a group, and beam 5 is not warned about");
    }

    static ElementFilter Only(params string[] terms)
    {
        List<string> bad;
        return ElementFilter.Create(terms, out bad);
    }

    // ---------------------------------------------------------------- the lower bound

    static void Bounds()
    {
        Section("The lower bound is never above the check");

        var random = new Random(20261020);
        var material = Steel("S355");
        var grade = (SteelGrade)DesignGrades.Of(material);
        var starts = new[]
        {
            SteelCatalogue.Create("I", "IPE300", material, 0.0), SteelCatalogue.Create("I", "HEA300", material, 0.0),
            SteelCatalogue.Create("I", "HEB300", material, 0.0), SteelCatalogue.Create("I", "HEM300", material, 0.0),
            SteelCatalogue.Create("O", "26.9x2", material, 0.0), SteelCatalogue.Create("[]", "40x20x2", material, 0.0),
            SteelCatalogue.Create("2L", "L20x20x3", material, 0.01),
        };

        int cases = 0, wrong = 0, failing = 0, caught = 0;
        double worstGap = double.NegativeInfinity;
        foreach (var start in starts)
        {
            var family = SteelCatalogue.FamilyOf(start).Sections;
            for (int k = 0; k < family.Count; k += 3)
            {
                var section = SteelSection.Of(family[k], grade, Fabrication.Rolled);
                if (section == null) continue;

                double fy = section.Fy;
                for (int n = 0; n < 12; n++)
                {
                    double gammaM0 = n % 3 == 0 ? 1.1 : 1.0;
                    // Each action a random share of its own plastic resistance, some of them none at all.
                    Func<double, double> share = scale => random.NextDouble() < 0.3 ? 0.0 : (random.NextDouble() * 3.0 - 1.5) * scale;
                    double pN = share(1.0), pVy = share(1.0), pVz = share(1.0), pMy = share(1.0), pMz = share(1.0);
                    var forces = new List<SectionForces>();
                    for (int station = 0; station < 5; station++)
                    {
                        double along = random.NextDouble();
                        forces.Add(new SectionForces(
                            pN * section.A * fy,
                            pVy * along * section.AvY * fy / Math.Sqrt(3.0),
                            pVz * along * section.AvZ * fy / Math.Sqrt(3.0),
                            random.NextDouble() < 0.2 ? 0.02 * section.WplZ * fy * random.NextDouble() : 0.0,
                            pMy * along * section.WplY * fy,
                            pMz * (1.0 - along) * section.WplZ * fy));
                    }
                    var member = new SteelMember(1.0 + 7.0 * random.NextDouble());
                    var max = SteelCheck.Run(section, forces, member, gammaM0, 1.0, null, false).Max;
                    if (!max.HasValue) continue;

                    double bound = SteelCheck.LowerBound(section, forces, gammaM0);
                    cases++;
                    worstGap = Math.Max(worstGap, bound - max.Value);
                    if (bound > max.Value * (1.0 + 1e-12) + 1e-12) wrong++;
                    if (max.Value > 1.0)
                    {
                        failing++;
                        if (bound > 1.0) caught++;
                    }
                }
            }
        }

        Check(cases > 1000 && wrong == 0, $"{cases} random sets of forces on IPEs, HEAs, HEBs, HEMs, CHSs, RHSs and pairs of angles: never above, by {worstGap:G3} at most");
        Check(caught > failing / 2, $"and it rules out {caught} of the {failing} that fail the whole check, without it");
    }

    // ---------------------------------------------------------------- the rounds

    static Model Design(string file, IReadOnlyList<IUniaxialSection> sections)
    {
        return new Model
        {
            Recorders = new List<IRecorder> { new Recorder { FileName = file } },
            Beams = sections.Select((s, i) => (IBeam)Beam(i + 1, $"Cantilever_{i + 1}", s)).ToList(),
            IsStatic = true,
        };
    }

    static readonly DesignSettings Settings = new DesignSettings { Report = false };
    static readonly SteelMember[] Members = { new SteelMember(3.0), new SteelMember(3.0) };

    /// <summary>The first of the list that keeps beam <paramref name="i"/> at or below the target under the file's forces, one by one.</summary>
    static IUniaxialSection First(string file, int i, double target, IReadOnlyList<IUniaxialSection> candidates = null)
    {
        var model = Design(file, new[] { Catalogue[0], Catalogue[0] });
        var forces = Utilisations.Forces(model, 0);
        foreach (var section in candidates ?? Catalogue)
        {
            var max = Utilisations.Check(model.Beams[i], section, forces[i], Members[i], Settings, null).Max;
            if (max.HasValue && max.Value <= target)
                return section;
        }
        return null;
    }

    /// <summary>The design, reading its forces from <paramref name="file"/>.</summary>
    static Model Always(Model design, string file)
    {
        design.Recorders = new List<IRecorder> { new Recorder { FileName = file } };
        return design;
    }

    /// <summary>
    /// The rounds from <paramref name="start"/>. A design group gives its beams one section; with
    /// <paramref name="each"/> its beams are taken each on their own instead, as the beams of a family
    /// are, to test how a beam is sized by itself.
    /// </summary>
    static SizingResult Size(Model start, DesignGroup group, double target, Func<Model, Model> analyse, int maxRounds = 10, ElementFilter only = null,
                             Func<IUniaxialSection, SectionFamily> family = null, bool each = false,
                             bool screen = true, bool parallel = true)
    {
        var groups = SectionSizing.Resolve(start.Beams, group == null ? new DesignGroup[0] : new[] { group }, new List<string>(), new List<string>(), only, null, family);
        if (each)
            foreach (var g in groups)
                g.Uniform = false;
        var options = new SizingOptions { Target = target, MaxRounds = maxRounds, Design = Settings, Members = Members, Screen = screen, Parallel = parallel };
        // The file the rebuilt design is given does not matter: every analyse picks its own.
        return SectionSizing.Run(start, groups, options, analyse, null, (original, sections) => Design(start.Recorders[0].FileName, sections));
    }

    static void Rounds(string folder)
    {
        Section("The rounds, on recorder files OpenSees solved");

        string heavy = Path.Combine(folder, "heavy.mpco");
        string light = Path.Combine(folder, "light.mpco");
        if (!File.Exists(heavy) || !File.Exists(light))
        {
            Console.WriteLine("  (skipped: optimiser.tcl was not solved - no OpenSees)");
            return;
        }

        var catalogue = new DesignGroup { Name = "Cantilevers", ElementIds = new List<string> { "Cantilever_*" }, Sections = Catalogue.ToList() };
        var start = Design(heavy, new[] { Catalogue[6], Catalogue[6] });

        // Each beam its own, as a family's are: forces that do not change, so the second round only
        // confirms the first.
        var one = First(heavy, 0, 1.0);
        var two = First(heavy, 1, 1.0);
        Check(one != null && two != null && Catalogue.IndexOf(one) > Catalogue.IndexOf(two),
              $"(the heavier-loaded cantilever needs the heavier section: {one}, {two})");

        var result = Size(start, catalogue, 1.0, design => Always(design, heavy), each: true);
        Check(result.Sections[0] == one && result.Sections[1] == two, $"each beam on the first section of the list that holds: {result.Sections[0]}, {result.Sections[1]}");
        Check(result.Converged && result.Rounds.Count == 2, $"two rounds, the second changing nothing: {result.Rounds.Count}, converged {result.Converged}");
        Check(result.Rounds[0].Changed == 2 && result.Rounds[1].Changed == 0, "both moved in the first round, from the I400 they started on");
        Check(result.Utilisation.All(u => u.HasValue && u.Value <= 1.0), $"both at or below 1: {string.Join(", ", result.Utilisation.Select(u => u?.ToString("0.###")))}");
        Check(result.Rounds[0].Failing == 0 && result.Rounds[0].MaxUtilisation < result.Rounds[1].MaxUtilisation,
              "the first round's I400s pass, and lighter sections work harder");
        Check(result.Steps.SequenceEqual(new[] { 0 }), "the last step of a static analysis, the only one");

        // History hands the rounds out as they are: a Panel shows the line, Deconstruct Anything the
        // public fields and properties it reflects on.
        var round = result.Rounds[0];
        Check(round.ToString().StartsWith("Iteration 1: mass ") && round.ToString().Contains("sized above 1"),
              $"a round reads as its line: {round}");
        var members = typeof(SizingRound).GetFields().Select(f => f.Name)
                        .Concat(typeof(SizingRound).GetProperties().Select(p => p.Name)).ToList();
        Check(new[] { "Iteration", "Mass", "MaxUtilisation", "Target", "Failing", "Changed" }.All(members.Contains),
              "and splits into its values: " + string.Join(", ", members));
        Check(result.Governing.All(g => !string.IsNullOrEmpty(g) && !g.StartsWith("Not checked")), $"with the check that governs: {string.Join(", ", result.Governing)}");

        // The order is the user's: a list with the I400 before the I300 puts beam 1 on the I400, though
        // the I300 holds it and weighs less.
        var mixed = new List<IUniaxialSection> { Catalogue[3], Catalogue[6], Catalogue[4] };
        var oneMixed = First(heavy, 0, 1.0, mixed);
        var twoMixed = First(heavy, 1, 1.0, mixed);
        Check(oneMixed == Catalogue[6] && twoMixed == Catalogue[3], $"(in the order I240, I400, I300, beam 1 first holds on {oneMixed}, beam 2 on {twoMixed})");
        var ordered = Size(start, new DesignGroup { ElementIds = catalogue.ElementIds, Sections = mixed }, 1.0, design => Always(design, heavy), each: true);
        Check(ordered.Sections[0] == Catalogue[6] && ordered.Sections[1] == Catalogue[3],
              $"the list is tried in the order given, not by weight: {ordered.Sections[0]}, {ordered.Sections[1]}");

        // A lower target.
        var oneAt08 = First(heavy, 0, 0.8);
        var lower = Size(start, catalogue, 0.8, design => Always(design, heavy), each: true);
        Check(lower.Sections[0] == oneAt08 && Catalogue.IndexOf(oneAt08) >= Catalogue.IndexOf(one), $"Threshold 0.8: {lower.Sections[0]}");

        // A design group: the one section the worse beam needs.
        var same = Size(start, catalogue, 1.0, design => Always(design, heavy));
        Check(same.Sections[0] == one && same.Sections[1] == one, $"a design group: both on {one}: {same.Sections[0]}, {same.Sections[1]}");

        // None in the list holds: the nearest, and the beam is left above the target.
        var small = new DesignGroup { ElementIds = catalogue.ElementIds, Sections = Catalogue.Take(2).ToList() };
        var scarce = Size(start, small, 1.0, design => Always(design, heavy));
        Check(scarce.Sections[0] == Catalogue[1] && scarce.Utilisation[0] > 1.0, $"nothing holds beam 1: the nearest, {scarce.Sections[0]}, at {scarce.Utilisation[0]:0.###}");
        Check(scarce.Converged, "which is where it stays");

        // One round only: the design analysed, and what it would go to next not yet taken.
        var once = Size(start, catalogue, 1.0, design => Always(design, heavy), 1);
        Check(!once.Converged && once.Rounds.Count == 1 && once.Sections[0] == Catalogue[6], "MaxRounds 1: the starting design, not converged");

        // Only beam 2 takes part: beam 1 keeps the I400 it has, though it is in the group.
        var part = Size(start, catalogue, 1.0, design => Always(design, heavy), 10, Only("Cantilever_2"));
        Check(part.Sections[0] == Catalogue[6] && part.Sections[1] == two, $"ElementId Cantilever_2: {part.Sections[0]} kept, {part.Sections[1]} for beam 2");
        Check(part.Group[0] == -1 && part.Group[1] == 0 && part.Utilisation[0].HasValue, "beam 1 takes no part, and is still checked");

        // No design group: each cantilever is sized within the family of the HEA it starts with.
        var hea = SteelCatalogue.FamilyOf(SteelCatalogue.Create("I", "HEA400", Steel("S355"), 0.0));
        var fromHea = Design(heavy, new[] { hea.Sections.Last(), hea.Sections.Last() });
        var oneHea = First(heavy, 0, 1.0, hea.Sections);
        var twoHea = First(heavy, 1, 1.0, hea.Sections);
        var byFamily = Size(fromHea, null, 1.0, design => Always(design, heavy), 10, null, SteelCatalogue.FamilyOf);
        // The optimiser makes the family afresh, so the sections are told apart by name.
        Check(byFamily.Converged && byFamily.Sections[0].ToString() == oneHea.ToString() && byFamily.Sections[1].ToString() == twoHea.ToString(),
              $"no design group: from the HEA1000 each goes to the first HEA that holds: {byFamily.Sections[0]}, {byFamily.Sections[1]}");
        Check(byFamily.Sections.All(x => x.ToString().StartsWith("HEA")) && byFamily.Utilisation.All(u => u <= 1.0), "never out of the HEAs");

        // A design that comes round again. The light section brings the heavy forces, under which it
        // fails; the heavy one brings light forces, under which the light one would do. Starting from
        // the heavy one: light, heavy - seen before - and from then on onward only, so heavy, and done.
        var lightOne = First(light, 0, 1.0);
        var heavyOne = First(heavy, 0, 1.0);
        Check(lightOne != null && Catalogue.IndexOf(lightOne) < Catalogue.IndexOf(heavyOne), $"(the light forces need {lightOne}, the heavy {heavyOne})");
        var pair = new DesignGroup { ElementIds = new List<string> { "Cantilever_1" }, Sections = { lightOne, heavyOne } };
        var from = Design(heavy, new[] { heavyOne, Catalogue[6] });
        var circle = Size(from, pair, 1.0, design => Always(design, design.Beams[0].Section == lightOne ? heavy : light));
        Check(circle.Oscillated, "it sees the design come round");
        Check(circle.Converged && circle.Sections[0] == heavyOne, $"and ends on the heavy section, which holds either way: {circle.Sections[0]}");
        Check(circle.Rounds.Count == 3 && !circle.Rounds[0].OnwardOnly && circle.Rounds[2].OnwardOnly,
              $"in three rounds, the last onward only: {string.Join(" | ", circle.Rounds.Select(r => $"{r.Iteration}:{r.Changed}{(r.OnwardOnly ? "^" : "")}"))}");
        Check(circle.Sections[1] == Catalogue[6], "the beam in no group keeps its section");
        Check(circle.Group[0] == 0 && circle.Group[1] == -1, "and the groups say so");

        Section("Screening and parallel checking change how fast, never what");

        var scenarios = new List<KeyValuePair<string, Func<bool, bool, SizingResult>>>
        {
            new KeyValuePair<string, Func<bool, bool, SizingResult>>("each beam its own",
                (sc, pa) => Size(start, catalogue, 1.0, design => Always(design, heavy), each: true, screen: sc, parallel: pa)),
            new KeyValuePair<string, Func<bool, bool, SizingResult>>("a design group",
                (sc, pa) => Size(start, catalogue, 1.0, design => Always(design, heavy), screen: sc, parallel: pa)),
            new KeyValuePair<string, Func<bool, bool, SizingResult>>("a list in its own order",
                (sc, pa) => Size(start, new DesignGroup { ElementIds = catalogue.ElementIds, Sections = mixed }, 1.0, design => Always(design, heavy), each: true, screen: sc, parallel: pa)),
            new KeyValuePair<string, Func<bool, bool, SizingResult>>("a list nothing in passes",
                (sc, pa) => Size(start, small, 1.0, design => Always(design, heavy), each: true, screen: sc, parallel: pa)),
            new KeyValuePair<string, Func<bool, bool, SizingResult>>("Threshold 0.8",
                (sc, pa) => Size(start, catalogue, 0.8, design => Always(design, heavy), each: true, screen: sc, parallel: pa)),
            new KeyValuePair<string, Func<bool, bool, SizingResult>>("the HEA family",
                (sc, pa) => Size(fromHea, null, 1.0, design => Always(design, heavy), 10, null, SteelCatalogue.FamilyOf, screen: sc, parallel: pa)),
            new KeyValuePair<string, Func<bool, bool, SizingResult>>("a design that comes round",
                (sc, pa) => Size(from, pair, 1.0, design => Always(design, design.Beams[0].Section == lightOne ? heavy : light), screen: sc, parallel: pa)),
        };

        foreach (var scenario in scenarios)
        {
            var plain = scenario.Value(false, false);
            bool alike = true;
            foreach (var flags in new[] { new[] { true, false }, new[] { false, true }, new[] { true, true } })
            {
                var other = scenario.Value(flags[0], flags[1]);
                alike &= other.Sections.Select(x => x.ToString()).SequenceEqual(plain.Sections.Select(x => x.ToString()))
                        && other.Utilisation.SequenceEqual(plain.Utilisation)
                        && other.Rounds.Count == plain.Rounds.Count && other.Converged == plain.Converged
                        && other.Rounds.Select(r => r.Changed).SequenceEqual(plain.Rounds.Select(r => r.Changed));
            }
            var fast = scenario.Value(true, true);
            int checksPlain = plain.Rounds.Sum(r => r.Checks), checksFast = fast.Rounds.Sum(r => r.Checks), screenedFast = fast.Rounds.Sum(r => r.Screened);
            Check(alike, $"{scenario.Key}: the same sections, utilisations and iterations either way; " +
                        $"{checksPlain} whole checks without screening, {checksFast} with, {screenedFast} ruled out on the bound");
        }
    }

    static void Main(string[] args)
    {
        string folder = args.Length > 0 ? args[0] : ".";

        Ordering();
        Choosing();
        Grouping();
        Families();
        Bounds();
        Rounds(folder);

        Console.WriteLine(fails == 0 ? "\nAll passed." : $"\n{fails} failed.");
        Environment.Exit(fails == 0 ? 0 : 1);
    }
}
