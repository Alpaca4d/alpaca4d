using System;
using System.Collections.Generic;
using System.Linq;

using Grasshopper.Kernel;
using Newtonsoft.Json.Linq;

using Alpaca4d.UIWidgets;

namespace Alpaca4d.Gh
{
    /// <summary>
    /// The Results menu of a topic of Report View: the results Model View draws, a square box each, and
    /// under each one ticked, in a frame of its own, what goes with it - its components, as many as are
    /// ticked, a picture each; its unit; what the result alone has (the diagram scale, the shell layer,
    /// the brick axes, how reactions are drawn); and a round Table box: the table of what each picture
    /// draws, written after it. With them the deformed shape and its scale, iso-surfaces, values and,
    /// for a pushover, plastic hinges, which every picture has.
    /// <see cref="Pictures"/> lists the pictures asked for, one per component ticked, and
    /// <see cref="WriteShared"/> what they all have; the Report draws each with Model View.
    /// </summary>
    internal sealed class ReportResultsMenu
    {
        /// <summary>A result's controls: its box, and the frame under it.</summary>
        internal sealed class Result
        {
            public ResultFamily Family;
            public MenuCheckBoxGroup Tick;
            public MenuGroupBox Box;

            /// <summary>The components drawn, as many as are ticked: a picture each.</summary>
            public MenuCheckBoxGroup Components;

            /// <summary>One per kind of quantity, by Quantity, null for the kinds the result has none of; only those of the components picked shown.</summary>
            public MenuCheckBoxGroup[] Units;

            public MenuStaticText[] UnitCaptions;

            public MenuCheckBoxGroup Table;

            public bool On => Tick[0];

            /// <summary>The components drawn, by their place in the result's list.</summary>
            public IEnumerable<int> Picked => Components.Ticked;

            public IEnumerable<Quantity> Quantities => Picked.Select(c => ResultField.QuantityOf(Family, c)).Distinct();

            public MenuCheckBoxGroup UnitOf(int component) => Units[(int)ResultField.QuantityOf(Family, component)];

            /// <summary>One component picked alone, as a click picks it.</summary>
            public void Pick(int component) => Components.TickOnly(component);
        }

        public readonly Result[] Results;

        /// <summary>Each result's Table box, in the order of <see cref="Results"/>.</summary>
        public readonly MenuCheckBoxGroup[] Tables;

        private readonly MenuCheckBoxGroup _ckDeformed, _ckBands, _ckValues, _ckExtremes, _ckHinges;
        private readonly MenuSlider _slScale, _slDiagram, _slArrow, _slHinge;
        private readonly MenuCheckBoxGroup _chLayer, _chAxes, _chReaction;
        private readonly MenuGroupBox _bxDeformed, _bxHinges;
        private readonly Func<GH_DocumentObject> _owner;

        // Saved by their place in the panel or frame they are in, each kind of widget numbered apart.
        private const int TickBase = 200, BoxBase = 10;
        private const int UnitBase = 10, ExtraIndex = 20, TableIndex = 30;

        /// <param name="unit">The topic's unit, which the menu is added to.</param>
        /// <param name="id">The menu's own name, different in each topic.</param>
        /// <param name="hinges">Whether the menu offers plastic hinges, as a pushover's does.</param>
        /// <param name="owner">The component, to solve again when a control changes.</param>
        public ReportResultsMenu(EvaluationUnit unit, string id, bool hinges, Func<GH_DocumentObject> owner)
        {
            _owner = owner;
            var menu = new GH_ExtendableMenu(1, id) { Name = "Results", Header = "The results to draw, one or more, as Model View draws them, and the table of each" };
            var panel = new MenuPanel(1, id + "_panel");

            // The deformed shape, and its scale under it.
            _ckDeformed = ReportWidgets.Tick(TickBase + 10, id + "Deformed", "Deformed shape", true, "The model drawn deformed");
            _bxDeformed = ReportWidgets.Box(0, id + "DeformedBox");
            _slScale = new MenuSlider(0, id + "DeformScale", 0.0, 200.0, 1.0, 1);
            _bxDeformed.AddControl(ReportWidgets.Caption("Scale"));
            _bxDeformed.AddControl(_slScale);
            panel.AddControl(_ckDeformed);
            panel.AddControl(_bxDeformed);

            _slDiagram = new MenuSlider(0, id + "DiagramScale", 0.0, 20.0, 1.0, 2);
            _chLayer = ReportWidgets.Choice(ExtraIndex, id + "Layer", Alpaca4d.Result.Read.LayerNames);
            _chAxes = ReportWidgets.Choice(ExtraIndex, id + "Axes", ResultField.StressAxes);
            _chReaction = ReportWidgets.Choice(ExtraIndex, id + "ReactionStyle", ResultField.ReactionStyles, 0, 1);
            _slArrow = new MenuSlider(0, id + "ArrowScale", 0.0, 20.0, 1.0, 2);

            // Each result: its box, and under it, framed, what goes with it.
            Results = ResultField.MenuOrder.Select((family, r) => Make(family, r, id)).ToArray();
            Tables = Results.Select(r => r.Table).ToArray();
            foreach (var result in Results)
            {
                panel.AddControl(result.Tick);
                panel.AddControl(result.Box);
            }
            Results[0].Tick[0] = true;

            _ckBands = ReportWidgets.Tick(TickBase + 20, id + "IsoSurfaces", "Iso-surfaces", false, "Bands of one colour each, rather than a smooth gradient");
            _ckValues = ReportWidgets.Tick(TickBase + 21, id + "Values", "Show values", false, "The values written on the model");
            _ckExtremes = ReportWidgets.Tick(TickBase + 23, id + "Extremes", "Max and min", true,
                "The largest and the smallest value written where they are, with a dot, beside the legend's gradient");
            panel.AddControl(_ckBands);
            panel.AddControl(_ckValues);
            panel.AddControl(_ckExtremes);

            if (hinges)
            {
                _ckHinges = ReportWidgets.Tick(TickBase + 22, id + "Hinges", "Plastic hinges", true, "Where the beams have yielded, coloured by how far");
                _bxHinges = ReportWidgets.Box(BoxBase + 20, id + "HingesBox");
                _slHinge = new MenuSlider(0, id + "HingeSize", 0.1, 5.0, 1.0, 1);
                _bxHinges.AddControl(ReportWidgets.Caption("Size"));
                _bxHinges.AddControl(_slHinge);
                panel.AddControl(_ckHinges);
                panel.AddControl(_bxHinges);
            }

            menu.AddControl(panel);
            menu.Expand();
            unit.AddMenu(menu);

            foreach (var result in Results)
            {
                Listen(result.Tick);
                Listen(result.Components);
                foreach (var units in result.Units.Where(x => x != null)) Listen(units);
                Listen(result.Table);
            }
            foreach (var widget in new GH_Attr_Widget[] { _ckDeformed, _slScale, _slDiagram, _chLayer, _chAxes, _chReaction, _slArrow, _ckBands, _ckValues, _ckExtremes, _ckHinges, _slHinge })
                Listen(widget);

            UpdateVisibility();
        }

        /// <summary>A result's box and its frame: the component, the units, what it alone has, its table.</summary>
        private Result Make(ResultFamily family, int place, string id)
        {
            var names = ResultField.ComponentNames(family);
            var result = new Result
            {
                Family = family,
                Tick = ReportWidgets.Tick(TickBase + place, id + "Show" + family, ResultField.FamilyNames[(int)family], false),
                Box = ReportWidgets.Box(BoxBase + place, id + family + "Box"),
                Units = new MenuCheckBoxGroup[Enum.GetValues(typeof(Quantity)).Length],
                UnitCaptions = new MenuStaticText[Enum.GetValues(typeof(Quantity)).Length]
            };
            var box = result.Box;

            // As many as are ticked, a picture each; the first to start with. Beam forces by their short names.
            bool beams = family == ResultFamily.BeamForce;
            result.Components = new MenuCheckBoxGroup(0, id + family + "Components")
            {
                Direction = MenuCheckBoxGroup.LayoutDirection.Grid,
                Round = true,
                Columns = ComponentColumns(family)
            };
            for (int i = 0; i < names.Length; i++)
                result.Components.Add(beams ? ResultField.BeamForceShortNames[i] : names[i], i == 0, names[i] + ", a picture of its own");
            box.AddControl(ReportWidgets.Caption(beams ? "Forces, a picture each" : "Components, a picture each"));
            box.AddControl(result.Components);

            var quantities = Enumerable.Range(0, names.Length).Select(i => ResultField.QuantityOf(family, i)).Distinct().ToList();
            foreach (var quantity in quantities)
            {
                var units = ResultUnits.Of(quantity);
                var choice = ReportWidgets.Choice(UnitBase + (int)quantity, id + family + "Unit" + quantity,
                    units.Select(u => u.Symbol), ResultUnits.Default(quantity), 0, units.Select(u => u.Caption).ToList());
                var caption = ReportWidgets.Caption(quantities.Count == 1 ? "Unit" : UnitName(quantity));
                result.Units[(int)quantity] = choice;
                result.UnitCaptions[(int)quantity] = caption;
                box.AddControl(caption);
                box.AddControl(choice);
            }

            switch (family)
            {
                case ResultFamily.BeamForce:
                    box.AddControl(ReportWidgets.Caption("Diagram scale"));
                    box.AddControl(_slDiagram);
                    break;
                case ResultFamily.ShellStress:
                    box.AddControl(ReportWidgets.Caption("Layer through the thickness"));
                    box.AddControl(_chLayer);
                    break;
                case ResultFamily.BrickStress:
                    box.AddControl(ReportWidgets.Caption("Read in"));
                    box.AddControl(_chAxes);
                    break;
                case ResultFamily.Reaction:
                    box.AddControl(ReportWidgets.Caption("Drawn as"));
                    box.AddControl(_chReaction);
                    box.AddControl(ReportWidgets.Caption("Arrow scale"));
                    box.AddControl(_slArrow);
                    break;
            }

            result.Table = ReportWidgets.Option(TableIndex, id + "Table" + family, "Table", false,
                "The table of what is drawn of " + ResultField.FamilyNames[(int)family].ToLowerInvariant() + ", written after the picture");
            box.AddControl(result.Table);
            return result;
        }

        /// <summary>
        /// The columns a result's components are lined up in, so that each row is of a kind: the
        /// reactions' forces over their moments, the shell forces' membrane forces, moments and shears.
        /// </summary>
        private static int ComponentColumns(ResultFamily family)
        {
            switch (family)
            {
                case ResultFamily.Displacement:
                case ResultFamily.Reaction:
                    return 4;
                default:
                    return 3;
            }
        }

        private static string UnitName(Quantity quantity)
        {
            switch (quantity)
            {
                case Quantity.Force:
                case Quantity.ForcePerLength:
                    return "Force unit";
                case Quantity.Moment:
                case Quantity.MomentPerLength:
                    return "Moment unit";
                default:
                    return "Unit";
            }
        }

        private void Listen(GH_Attr_Widget widget)
        {
            switch (widget)
            {
                case MenuCheckBoxGroup group: group.ValueChanged += (s, e) => Changed(); break;
                case MenuSlider slider: slider.ValueChanged += (s, e) => Changed(); break;
            }
        }

        private void Changed()
        {
            UpdateVisibility();
            _owner()?.ExpireSolution(true);
        }

        private bool IsTicked(ResultFamily family) => Results.Any(r => r.Family == family && r.On);

        /// <summary>A result's frame only while it is ticked, and in it only the units of the components picked.</summary>
        public void UpdateVisibility()
        {
            foreach (var result in Results)
            {
                result.Box.Visible = result.On;
                var used = result.Quantities.ToList();
                for (int q = 0; q < result.Units.Length; q++)
                {
                    if (result.Units[q] == null) continue;
                    bool shown = used.Contains((Quantity)q);
                    result.Units[q].Visible = shown;
                    result.UnitCaptions[q].Visible = shown;
                }
            }
            _ckBands.Visible = Results.Any(r => r.On && r.Family != ResultFamily.BeamForce);
            _ckValues.Visible = Results.Any(r => r.On);
            _ckExtremes.Visible = Results.Any(r => r.On);
            _bxDeformed.Visible = _ckDeformed[0];
            if (_bxHinges != null) _bxHinges.Visible = _ckHinges[0];

            _owner()?.Attributes?.ExpireLayout();
            Grasshopper.Instances.ActiveCanvas?.Invalidate();
        }

        /// <summary>A picture the menu asks for: one component of one result, as a figure's "results" entry.</summary>
        internal sealed class Picture
        {
            public JObject Entry;
            public string Result, Component, Unit;
        }

        /// <summary>
        /// The pictures asked for, in the menu's order: one for each component ticked of each result
        /// ticked, each in the unit picked for its kind of quantity, each with its table when the
        /// result's Table is ticked.
        /// </summary>
        public List<Picture> Pictures()
        {
            var pictures = new List<Picture>();
            foreach (var result in Results.Where(r => r.On))
            {
                string family = ResultField.FamilyNames[(int)result.Family];
                var names = ResultField.ComponentNames(result.Family);
                foreach (int c in result.Picked)
                {
                    var entry = new JObject { ["result"] = family };
                    if (result.Family == ResultFamily.BeamForce)
                        entry["forces"] = new JArray(ResultField.BeamForceShortNames[c]);
                    else
                        entry["component"] = names[c];
                    string unit = result.UnitOf(c)?.Picked ?? "";
                    if (unit.Length > 0) entry["unit"] = unit;
                    if (result.Table[0]) entry["table"] = true;
                    pictures.Add(new Picture
                    {
                        Entry = entry,
                        Result = family,
                        Component = result.Family == ResultFamily.BeamForce ? ResultField.BeamForceShortNames[c] : names[c],
                        Unit = unit
                    });
                }
            }
            return pictures;
        }

        /// <summary>The results ticked with no component ticked under them: they draw nothing.</summary>
        public IEnumerable<string> Empty() =>
            Results.Where(r => r.On && !r.Picked.Any()).Select(r => ResultField.FamilyNames[(int)r.Family]);

        /// <summary>What every picture has: the deformed shape and its scale, and the settings of the results ticked.</summary>
        public void WriteShared(JObject figure)
        {
            figure["deformed"] = _ckDeformed[0];
            if (_ckDeformed[0]) figure["scale"] = _slScale.Value;
            if (IsTicked(ResultFamily.BeamForce)) figure["diagramScale"] = _slDiagram.Value;
            if (IsTicked(ResultFamily.ShellStress)) figure["layer"] = _chLayer.Picked;
            if (IsTicked(ResultFamily.BrickStress)) figure["axes"] = _chAxes.Picked;
            if (IsTicked(ResultFamily.Reaction))
            {
                figure["reactionStyle"] = _chReaction.Picked;
                figure["arrowScale"] = _slArrow.Value;
            }
            figure["isoSurfaces"] = _ckBands[0];
            figure["values"] = _ckValues[0];
            figure["extremes"] = _ckExtremes[0];
            if (_ckHinges != null)
            {
                figure["hinges"] = _ckHinges[0];
                if (_ckHinges[0]) figure["hingeSize"] = _slHinge.Value;
            }
        }

        /// <summary>
        /// A caption for one picture: {result}, {component} and {unit} in <paramref name="caption"/> put
        /// in. The Report fills in the rest, {step} and the like.
        /// </summary>
        public static string Caption(string caption, Picture picture) => caption?
            .Replace("{result}", picture.Result)
            .Replace("{component}", picture.Component)
            .Replace("{unit}", picture.Unit);
    }
}
