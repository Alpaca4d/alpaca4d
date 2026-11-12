using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;

using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;
using Newtonsoft.Json.Linq;
using Rhino.Geometry;

using Alpaca4d.UIWidgets;

namespace Alpaca4d.Gh
{
    /// <summary>
    /// Model View's menus as a report figure's settings, both ways: <see cref="Apply"/> sets a Model
    /// View's menus and inputs from a figure's JSON, as clicks would set them, and <see cref="Read"/>
    /// writes a Model View's menus out as that JSON. Report View hands out what Read writes, and the
    /// Report draws it with what Apply sets, so a figure comes out as its Report View showed it - and
    /// one written by hand in a template the same way.
    /// </summary>
    internal static class ModelViewSettings
    {
        /// <summary>The settings that are Model View's own, by the names a figure gives them.</summary>
        public static readonly string[] Names =
        {
            "step", "result", "results", "component", "forces", "unit", "deformed", "scale",
            "extruded", "edges", "nodeIds", "elementIds", "sectionNames", "localAxes", "axesSize", "releases", "releaseSize",
            "colourBy", "textSize", "values", "extremes", "isoSurfaces", "hinges", "hingeSize",
            "loads", "loadValues", "loadScale", "supports", "supportScale",
            "diagramScale", "arrowScale", "layer", "axes", "reactionStyle",
            "elements", "colours", "range", "loadPattern",
        };

        /// <summary>
        /// Sets <paramref name="view"/>'s menus as <paramref name="f"/> says, and its inputs to the model
        /// and to the step, elements, colours, range and load pattern it gives. Null when it went well,
        /// otherwise why: a setting that names something Model View does not have.
        /// </summary>
        public static string Apply(ModelView view, Alpaca4d.Model model, JObject f)
        {
            // The results: "results", a list of them as Report View's Results menu ticks them, or one, "result".
            var shownFamilies = new List<ResultFamily>();
            view.ShowResult(0, 0);
            var entries = Get(f, "results") is JArray ticked
                ? ticked.OfType<JObject>().ToList()
                : (!string.IsNullOrWhiteSpace(Text(f, "result")) && !IsNone(Text(f, "result")) ? new List<JObject> { f } : new List<JObject>());
            foreach (var entry in entries)
            {
                string problem = Tick(view, entry, out var family);
                if (problem != null) return problem;
                shownFamilies.Add(family);
            }

            // A mode, or a displacement, is a shape: drawn deformed unless the figure says not.
            bool modal = model.IsModal || model.IsBuckling;
            bool shape = modal || shownFamilies.Contains(ResultFamily.Displacement);
            view._ckDeformed.Active = Bool(f, "deformed") ?? shape;
            if (Number(f, "scale") is double scale)
                Slide(view._slDeformScale, scale);

            foreach (var (box, key) in Checks(view))
                if (Bool(f, key) is bool on) box.Active = on;

            foreach (var (slider, key) in Sliders(view))
                if (Number(f, key) is double value) Slide(slider, value);

            // What is written on the model - values, the largest and the smallest - as tall as a fortieth
            // of the model's size unless the figure says: the same in a picture of a stair as of a hall,
            // where Model View's own half a unit is huge on the one and lost on the other.
            if (Number(f, "textSize") == null && model.UniquePoints != null && model.UniquePoints.Count > 1)
            {
                double size = new BoundingBox(model.UniquePoints).Diagonal.Length / 40.0;
                if (size > 0.0) Slide(view._slTextSize, size);
            }

            foreach (var (dropdown, key) in Dropdowns(view))
            {
                var name = Text(f, key);
                if (name == null) continue;
                int at = dropdown.Items.FindIndex(e => Same(e.name, name) || Same(e.content, name));
                if (at < 0) return $"\"{name}\" is not one of {key}'s choices: {string.Join(", ", dropdown.Items.Select(e => e.name))}";
                dropdown.Value = at;
            }

            // The inputs: the model, the step, which elements, the colours, the range, the load pattern.
            var inputs = view.Params.Input;
            Persist((Param_GenericObject)inputs[0], new GH_ObjectWrapper(model));
            Persist((Param_Integer)inputs[1], new GH_Integer(Int(f, "step") ?? 0));

            if (Get(f, "elements") is JToken elements && elements.Type != JTokenType.Null)
                Persist((Param_String)inputs[2], (elements is JArray list ? list.Select(x => (string)x) : new[] { (string)elements }).Select(x => new GH_String(x)).ToArray());

            if (Get(f, "colours") is JArray colours)
            {
                var parsed = new List<GH_Colour>();
                foreach (var c in colours.Select(x => (string)x))
                {
                    try { parsed.Add(new GH_Colour(ColorTranslator.FromHtml(c))); }
                    catch { return $"\"{c}\" is not a colour: write it as #RRGGBB"; }
                }
                Persist((Param_Colour)inputs[3], parsed.ToArray());
            }

            if (Get(f, "range") is JArray range && range.Count == 2)
                Persist((Param_Interval)inputs[4], new GH_Interval(new Interval((double)range[0], (double)range[1])));

            if (Int(f, "loadPattern") is int pattern)
                Persist((Param_Integer)inputs[5], new GH_Integer(pattern));

            return null;
        }

        /// <summary>
        /// One result ticked, with its component - or for beam forces its forces - and its unit, as
        /// <paramref name="entry"/> names them: "result", "component" or "forces", "unit". Null when it
        /// went well, otherwise why.
        /// </summary>
        private static string Tick(ModelView view, JObject entry, out ResultFamily family)
        {
            family = ResultFamily.Displacement;
            var resultName = Text(entry, "result");
            int place = Array.FindIndex(ResultField.MenuOrder, x => Same(ResultField.FamilyNames[(int)x], resultName) || Same(x.ToString(), resultName));
            if (place < 0)
                return $"\"{resultName}\" is not a result of Model View: {string.Join(", ", ResultField.MenuOrder.Select(x => ResultField.FamilyNames[(int)x]))}";

            var shown = view._results[place];
            family = shown.Family;
            var names = ResultField.ComponentNames(shown.Family);

            int component = 0;
            var token = Get(entry, "component");
            if (token != null && token.Type != JTokenType.Null)
            {
                component = token.Type == JTokenType.Integer ? (int)token : Array.FindIndex(names, n => Same(n, (string)token));
                if (component < 0 || component >= names.Length)
                    return $"\"{token}\" is not a component of {ResultField.FamilyNames[(int)shown.Family]}: {string.Join(", ", names)}";
            }
            shown.Tick.Active = true;
            shown.Pick(component);

            // Beam forces: any of N, Vy, Vz, T, My, Mz together.
            if (Get(entry, "forces") is JArray forces && shown.Components != null)
            {
                for (int i = 0; i < shown.Components.Count; i++) shown.Components[i] = false;
                foreach (var force in forces.Select(x => (string)x))
                {
                    int i = Array.FindIndex(ResultField.BeamForceShortNames, n => Same(n, force));
                    if (i < 0) i = Array.FindIndex(names, n => Same(n, force));
                    if (i < 0) return $"\"{force}\" is not a beam force: {string.Join(", ", ResultField.BeamForceShortNames)}";
                    shown.Components[i] = true;
                }
            }

            var unit = Text(entry, "unit");
            if (unit != null && shown.Picked.Any())
            {
                var units = shown.UnitOf(shown.Picked.First());
                int at = units.Items.FindIndex(e => Same(e.name, unit) || Same(e.content, unit));
                if (at < 0) return $"\"{unit}\" is not a unit of {ResultField.FamilyNames[(int)shown.Family]}: {string.Join(", ", units.Items.Select(e => e.name))}";
                units.Value = at;
            }

            // Each quantity's unit, when the figure gives more than one: N in kN, My in kN·m.
            if (Get(entry, "units") is JObject each)
                foreach (var property in each.Properties())
                {
                    if (!Enum.TryParse(property.Name, true, out Quantity quantity)) return $"\"{property.Name}\" is not a quantity: {string.Join(", ", Enum.GetNames(typeof(Quantity)))}";
                    if (!(shown.Units[(int)quantity] is MenuDropDown units)) continue;
                    var wanted = (string)property.Value;
                    int at = units.Items.FindIndex(e => Same(e.name, wanted) || Same(e.content, wanted));
                    if (at < 0) return $"\"{wanted}\" is not a unit of {property.Name}: {string.Join(", ", units.Items.Select(e => e.name))}";
                    units.Value = at;
                }
            return null;
        }

        /// <summary>
        /// <paramref name="view"/>'s menus as a figure's settings: the first result ticked, with its
        /// component or forces and its unit, and the shape, the elements, the loads and the supports as
        /// the menus have them - only the sliders and menus that are in use.
        /// </summary>
        public static JObject Read(ModelView view)
        {
            var f = new JObject();
            var shown = view._results?.FirstOrDefault(r => r.Tick.Active);
            if (shown != null)
            {
                f["result"] = ResultField.FamilyNames[(int)shown.Family];
                var names = ResultField.ComponentNames(shown.Family);
                if (shown.Components != null)
                    f["forces"] = new JArray(shown.Components.Ticked.Select(i => ResultField.BeamForceShortNames[i]));
                else
                    f["component"] = names[Math.Max(0, Math.Min(names.Length - 1, shown.Component.Value))];

                if (shown.Picked.Any())
                {
                    var units = shown.UnitOf(shown.Picked.First());
                    if (units != null && units.Items.Count > 0)
                        f["unit"] = units.Items[Math.Max(0, Math.Min(units.Items.Count - 1, units.Value))].name;
                }

                switch (shown.Family)
                {
                    case ResultFamily.ShellStress: f["layer"] = Item(view._ddLayer); break;
                    case ResultFamily.BrickStress: f["axes"] = Item(view._ddAxes); break;
                    case ResultFamily.Reaction:
                        f["reactionStyle"] = Item(view._ddReaction);
                        f["arrowScale"] = view._slArrowScale.Value;
                        break;
                    case ResultFamily.BeamForce: f["diagramScale"] = view._slDiagramScale.Value; break;
                }
            }
            else
            {
                f["colourBy"] = Item(view._ddColourBy);
            }

            f["deformed"] = view._ckDeformed.Active;
            if (view._ckDeformed.Active)
                f["scale"] = view._slDeformScale.Value;

            foreach (var (box, key) in Checks(view))
                f[key] = box.Active;

            // A size only where what it sizes is drawn.
            if (view._ckLocalAxes.Active) f["axesSize"] = view._slAxesSize.Value;
            if (view._ckReleases.Active) f["releaseSize"] = view._slReleaseSize.Value;
            if (view._ckHinges.Active) f["hingeSize"] = view._slHingeSize.Value;
            if (view._ckShowLoads.Active || view._ckShowLoadValues.Active) f["loadScale"] = view._slLoadScale.Value;
            if (view._ckShowSupports.Active) f["supportScale"] = view._slSupportScale.Value;
            if (view._ckValues.Active || view._ckExtremes.Active || view._ckNodeIds.Active || view._ckElementIds.Active || view._ckSectionNames.Active || view._ckShowLoadValues.Active)
                f["textSize"] = view._slTextSize.Value;

            return f;
        }

        private static string Item(MenuDropDown dropdown) =>
            dropdown.Items.Count == 0 ? "" : dropdown.Items[Math.Max(0, Math.Min(dropdown.Items.Count - 1, dropdown.Value))].name;

        private static IEnumerable<(MenuCheckBox, string)> Checks(ModelView view) => new[]
        {
            (view._ckExtruded, "extruded"), (view._ckWires, "edges"), (view._ckNodeIds, "nodeIds"), (view._ckElementIds, "elementIds"),
            (view._ckSectionNames, "sectionNames"), (view._ckLocalAxes, "localAxes"), (view._ckReleases, "releases"),
            (view._ckValues, "values"), (view._ckExtremes, "extremes"), (view._ckBands, "isoSurfaces"), (view._ckHinges, "hinges"),
            (view._ckShowLoads, "loads"), (view._ckShowLoadValues, "loadValues"), (view._ckShowSupports, "supports"),
        };

        private static IEnumerable<(MenuSlider, string)> Sliders(ModelView view) => new[]
        {
            (view._slAxesSize, "axesSize"), (view._slReleaseSize, "releaseSize"), (view._slTextSize, "textSize"),
            (view._slHingeSize, "hingeSize"), (view._slLoadScale, "loadScale"), (view._slSupportScale, "supportScale"),
            (view._slDiagramScale, "diagramScale"), (view._slArrowScale, "arrowScale"),
        };

        private static IEnumerable<(MenuDropDown, string)> Dropdowns(ModelView view) => new[]
        {
            (view._ddColourBy, "colourBy"), (view._ddLayer, "layer"), (view._ddAxes, "axes"), (view._ddReaction, "reactionStyle"),
        };

        private static void Persist<T>(GH_PersistentParam<T> param, params T[] items) where T : class, IGH_Goo
        {
            param.PersistentData.Clear();
            foreach (var item in items)
                param.PersistentData.Append(item);
        }

        /// <summary>A slider set to a value, its range widened to take it if need be.</summary>
        private static void Slide(MenuSlider slider, double value)
        {
            if (value > slider.MaxValue) slider.MaxValue = value;
            if (value < slider.MinValue) slider.MinValue = value;
            slider.Value = value;
        }

        // ── Reading a figure's settings ────────────────────────────────────────────────────────

        public static JToken Get(JObject f, string key) =>
            f.Properties().FirstOrDefault(p => string.Equals(p.Name, key, StringComparison.OrdinalIgnoreCase))?.Value;

        public static string Text(JObject f, string key)
        {
            var t = Get(f, key);
            return t == null || t.Type == JTokenType.Null ? null : Convert.ToString(((JValue)t).Value, CultureInfo.InvariantCulture);
        }

        public static double? Number(JObject f, string key)
        {
            var t = Get(f, key);
            if (t == null || t.Type == JTokenType.Null) return null;
            if (t.Type == JTokenType.Float || t.Type == JTokenType.Integer) return (double)t;
            return double.TryParse((string)t, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : (double?)null;
        }

        public static int? Int(JObject f, string key) => Number(f, key) is double v ? (int)Math.Round(v) : (int?)null;

        public static bool? Bool(JObject f, string key)
        {
            var t = Get(f, key);
            if (t == null || t.Type == JTokenType.Null) return null;
            if (t.Type == JTokenType.Boolean) return (bool)t;
            var s = ((string)t ?? "").Trim().ToLowerInvariant();
            return s == "true" || s == "yes" || s == "on" ? true : s == "false" || s == "no" || s == "off" ? false : (bool?)null;
        }

        public static bool IsNone(string name) => Same(name, "none") || Same(name, "model");

        /// <summary>Two names the same, whatever their case and spaces: "beam forces" is "Beam forces" is "BeamForce".</summary>
        public static bool Same(string a, string b)
        {
            string Squeeze(string s) => new string((s ?? "").Where(c => !char.IsWhiteSpace(c) && c != '_').ToArray()).ToLowerInvariant();
            var x = Squeeze(a);
            var y = Squeeze(b);
            return x == y || x.TrimEnd('s') == y.TrimEnd('s');
        }
    }
}
