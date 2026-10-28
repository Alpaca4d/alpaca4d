using System.Drawing;

namespace Alpaca4d
{
    /// <summary>
    /// The Alpaca4d palette, and the colour each kind of element is drawn in before anyone
    /// picks one.
    ///
    /// The six brand colours are read off the logo. They live here, in the core, rather than
    /// beside the Grasshopper UI because the elements themselves carry a colour and are
    /// built in the core - a default that only existed in a component would leave every
    /// element made any other way with no colour at all. Alpaca4d.UI.Palette takes its brand
    /// colours from here so the values are written down once.
    ///
    /// These are opaque. Meshes are drawn through VertexColors and DrawMeshFalseColors and
    /// beams as plain curves, neither of which honours an alpha; the UI palette applies its
    /// own transparency where transparency actually reaches the viewport.
    /// </summary>
    public partial class Colors
    {
        public static Color AlpacaRed { get { return Color.FromArgb(254, 0, 0); } }
        public static Color AlpacaOrange { get { return Color.FromArgb(235, 108, 63); } }
        public static Color AlpacaPurple { get { return Color.FromArgb(158, 53, 218); } }
        public static Color AlpacaLightGreen { get { return Color.FromArgb(175, 201, 48); } }
        public static Color AlpacaLightBlue { get { return Color.FromArgb(66, 136, 247); } }
        public static Color AlpacaDarkBlue { get { return Color.FromArgb(1, 30, 254); } }

        public static Color DarkTech { get { return Color.FromArgb(42, 45, 49); } }
        public static Color LightGrey { get { return Color.FromArgb(244, 244, 244); } }

        // The elements are given colours that separate them the way the model reads: one per
        // family - line, surface, solid - and the two beams apart from each other, since a
        // beam with hinges behaves differently enough to be worth spotting on the canvas.
        //
        // Red and purple are deliberately left out. Red is already the positive end of the
        // force diagrams and the colour of a load arrow, so keeping the geometry off it stops
        // results from clashing with the thing they are drawn on.

        /// <summary>Ordinary beams - the commonest element, and the baseline everything else reads against.</summary>
        public static Color DefaultBeam { get { return AlpacaDarkBlue; } }

        /// <summary>Beams carrying end hinges, set apart from a plain beam at a glance.</summary>
        public static Color DefaultBeamWithHinges { get { return AlpacaOrange; } }

        /// <summary>Shells of every flavour - ASDShellQ4, ASDShellT3, ShellDKGT.</summary>
        public static Color DefaultShell { get { return AlpacaLightBlue; } }

        /// <summary>Solids - SSPbrick and the four-node tetrahedron.</summary>
        public static Color DefaultBrick { get { return AlpacaLightGreen; } }

        // When Model View colours the elements by section or by material, each section or material
        // the user gave no colour takes one from this list, in the order the model meets them. They
        // are Tableau's ten, chosen to be told apart from one another, with its red moved to the end
        // so that a model of a few sections stays off the colour of loads and positive forces.
        private static readonly Color[] CategoricalColours =
        {
            Color.FromArgb(78, 121, 167),   // blue
            Color.FromArgb(242, 142, 43),   // orange
            Color.FromArgb(118, 183, 178),  // teal
            Color.FromArgb(89, 161, 79),    // green
            Color.FromArgb(237, 201, 72),   // yellow
            Color.FromArgb(176, 122, 161),  // purple
            Color.FromArgb(255, 157, 167),  // pink
            Color.FromArgb(156, 117, 95),   // brown
            Color.FromArgb(186, 176, 172),  // grey
            Color.FromArgb(225, 87, 89),    // red
        };

        /// <summary>
        /// The <paramref name="index"/>th of a run of colours meant to be told apart: the ten above,
        /// then the same ten darker, then lighter, and from the thirty-first round again.
        /// </summary>
        public static Color Categorical(int index)
        {
            int count = CategoricalColours.Length;
            int i = ((index % (3 * count)) + 3 * count) % (3 * count);
            var colour = CategoricalColours[i % count];

            switch (i / count)
            {
                case 1: return Mix(colour, Color.Black, 0.35);
                case 2: return Mix(colour, Color.White, 0.45);
                default: return colour;
            }
        }

        /// <summary><paramref name="a"/> moved the fraction <paramref name="t"/> of the way to <paramref name="b"/>.</summary>
        private static Color Mix(Color a, Color b, double t)
        {
            return Color.FromArgb(
                (int)System.Math.Round(a.R + (b.R - a.R) * t),
                (int)System.Math.Round(a.G + (b.G - a.G) * t),
                (int)System.Math.Round(a.B + (b.B - a.B) * t));
        }
    }
}
