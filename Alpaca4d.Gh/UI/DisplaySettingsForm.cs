using System;
using System.Collections.Generic;
using Eto.Drawing;
using Eto.Forms;

namespace Alpaca4d.UI
{
    /// <summary>
    /// Settings ▸ Display: the colours Model View and View Results draw their text and loads in.
    ///
    /// Global rather than an input on each component, because it is a preference about the
    /// viewport, not about a model: white ids that read on a dark background vanish on Rhino's
    /// default grey one, and that is true of every definition at once. A colour applies as soon as
    /// it is picked and is kept in settings.json for the next session.
    /// </summary>
    public class DisplaySettingsForm : Form
    {
        private static DisplaySettingsForm openForm;

        private readonly Dictionary<DisplayItem, ColorPicker> _pickers = new Dictionary<DisplayItem, ColorPicker>();

        public DisplaySettingsForm()
        {
            Title = "Alpaca4d - Display";
            Maximizable = false;
            Minimizable = false;
            Resizable = false;
            Topmost = true;
            Padding = new Padding(20);

            var layout = new DynamicLayout { Spacing = new Size(12, 8) };

            layout.AddRow(Heading("Model View"));
            Row(layout, DisplayItem.NodeIds, "Node IDs");
            Row(layout, DisplayItem.ElementIds, "Element IDs");
            Row(layout, DisplayItem.SectionNames, "Section names");
            Row(layout, DisplayItem.PointLoads, "Point loads");
            Row(layout, DisplayItem.LineLoads, "Line loads");
            Row(layout, DisplayItem.AreaLoads, "Area loads");

            layout.AddRow(Heading("View Results"));
            Row(layout, DisplayItem.Values, "Values");

            var reset = new Button { Text = "Reset to defaults" };
            reset.Click += (sender, e) =>
            {
                AlpacaSettings.ResetColours();
                foreach (var entry in _pickers)
                    entry.Value.Value = ToEto(AlpacaSettings.Colour(entry.Key));
                Redraw();
            };

            var close = new Button { Text = "Close" };
            close.Click += (sender, e) => Close();

            layout.AddRow(null);
            layout.AddSeparateRow(reset, null, close);

            Content = layout;
        }

        private static Label Heading(string text)
        {
            return new Label { Text = text, Font = SystemFonts.Bold() };
        }

        private void Row(DynamicLayout layout, DisplayItem item, string text)
        {
            var picker = new ColorPicker { Value = ToEto(AlpacaSettings.Colour(item)) };
            picker.ValueChanged += (sender, e) =>
            {
                AlpacaSettings.SetColour(item, ToDrawing(picker.Value));
                Redraw();
            };

            _pickers[item] = picker;
            layout.AddRow(new Label { Text = text, VerticalAlignment = VerticalAlignment.Center }, picker);
        }

        /// <summary>The components read the colours as they draw, so a redraw is all it takes.</summary>
        private static void Redraw()
        {
            Rhino.RhinoDoc.ActiveDoc?.Views?.Redraw();
        }

        private static Color ToEto(System.Drawing.Color colour)
        {
            return Color.FromArgb(colour.R, colour.G, colour.B);
        }

        private static System.Drawing.Color ToDrawing(Color colour)
        {
            return System.Drawing.Color.FromArgb(colour.Rb, colour.Gb, colour.Bb);
        }

        public static void ShowForm()
        {
            if (openForm != null && !openForm.IsDisposed)
            {
                openForm.BringToFront();
                return;
            }

            openForm = new DisplaySettingsForm();
            openForm.Closed += (sender, e) => openForm = null;
            openForm.Show();
        }
    }
}
