using System.Collections.Generic;
using System.Drawing;

using Alpaca4d.UIWidgets;

namespace Alpaca4d.Gh
{
    /// <summary>
    /// The controls of Report View's menus, made one way in every topic, so that how they belong
    /// together reads the same everywhere:
    /// <list type="bullet">
    /// <item>what the picture draws - a result, the supports - a square box before its name, at the
    /// menu's left edge;</item>
    /// <item>what goes with it - its component, its unit, its table - in a frame under it, a little in;</item>
    /// <item>in the frame, a grey caption over each choice, the choices round boxes lined up in a grid,
    /// one of them ticked; and round boxes for what can be ticked or not, as a table.</item>
    /// </list>
    /// No drop-down: every choice is in sight, and picked with one click.
    /// </summary>
    internal static class ReportWidgets
    {
        private static readonly Color CaptionGrey = Color.FromArgb(90, 90, 90);

        /// <summary>Something the picture draws, ticked or not: a square box before its name.</summary>
        public static MenuCheckBoxGroup Tick(int index, string id, string label, bool on, string tooltip = null)
        {
            var tick = new MenuCheckBoxGroup(index, id) { Direction = MenuCheckBoxGroup.LayoutDirection.Horizontal };
            tick.Add(label, on, tooltip);
            return tick;
        }

        /// <summary>Something that goes with what is drawn, ticked or not - its table, its values: a round box before its name.</summary>
        public static MenuCheckBoxGroup Option(int index, string id, string label, bool on, string tooltip = null)
        {
            var option = new MenuCheckBoxGroup(index, id) { Direction = MenuCheckBoxGroup.LayoutDirection.Horizontal, Round = true };
            option.Add(label, on, tooltip);
            return option;
        }

        /// <summary>
        /// One choice among <paramref name="items"/>, round boxes lined up in a grid, <paramref name="start"/>
        /// ticked to start with: so many columns, or 0 for as many as there is room for.
        /// </summary>
        public static MenuCheckBoxGroup Choice(int index, string id, IEnumerable<string> items, int start = 0, int columns = 0, IList<string> tooltips = null)
        {
            var choice = new MenuCheckBoxGroup(index, id)
            {
                Direction = MenuCheckBoxGroup.LayoutDirection.Grid,
                Round = true,
                Single = true,
                Columns = columns
            };
            int i = 0;
            foreach (var item in items)
            {
                choice.Add(item, i == start, tooltips != null && i < tooltips.Count ? tooltips[i] : null);
                i++;
            }
            return choice;
        }

        /// <summary>The frame of what goes with the control above it.</summary>
        public static MenuGroupBox Box(int index, string id) => new MenuGroupBox(index, id);

        /// <summary>A grey caption over a choice in a frame: Component, Unit.</summary>
        public static MenuStaticText Caption(string text) => new MenuStaticText { Text = text, Colour = CaptionGrey };

        /// <summary>The name of what a frame under it is about, when it is not ticked itself: Colour by, Mode shapes.</summary>
        public static MenuStaticText Heading(string text) => new MenuStaticText { Text = text };
    }
}
