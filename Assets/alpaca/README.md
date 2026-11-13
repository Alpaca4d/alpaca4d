# The base alpaca

The alpaca of the Gravity Load icon (`Alpaca4d.Gh/Resources/Gravity Load (Alpaca4d).png`), with
its load arrow taken out. It is the alpaca to start new icons from (2026-11-12).

| File | What it is |
| --- | --- |
| `gravity-alpaca.png` | 24 px, in its original colours: orange #FC8E39, black outline, brown shading |
| `gravity-alpaca-silhouette.png` | 24 px, the same shape in black; the alpha is the coverage |
| `gravity-alpaca-silhouette-240.png` | 240 px, the shape smoothed, to scale from |
| `base_alpaca_logo.png` | 24 px, the black outline alone with a transparent inside: the orange is taken out and the brown blends are turned back into see-through black |
| `base_alpaca_logo-colourful.png` | 24 px, that outline filled with the Alpaca4d logo's colours (blue tail, green, purple, orange and red neck, white snout and hooves), used in the 13_Report icons |
| `preview.png` | the first three beside the Gravity Load icon |
| `extract.py` | makes the first three files from the Gravity Load icon |
| `colourful.py` | makes the two `base_alpaca_logo` files from `gravity-alpaca.png` and `Assets/Logo.png` |

How the colours are mapped: each pixel's colour is unmixed into black and orange. The orange
share is then refilled with the logo's colour at that spot. The logo is mapped onto this shape by
a thin-plate spline through 15 matching points: ear, snout, throat, neck base, chest, rump, tail,
belly and hooves. Each pixel takes the logo colour most of its sub-samples land on, so the logo's
thin facet lines don't show.

In the 13_Report icons the colourful alpaca stands 1 px further left, so its snout clears column
23. Its row 11 is taken out so it fits rows 1-22, and everything within one pixel of the page is
cleared.

How the arrow came out:

- The arrow, its dark edge and its white halo covered the band x 7-14 of the body, and the
  arrowhead hid the belly.
- That band is redrawn by hand in the icon's own colours. The legs are x 4-7 and x 13-15, the
  belly line is row 18, and the back's outline rises from row 12 to row 10.
- Everything outside the band is Gravity Load's own pixels.

Using it:

- The alpaca fills the whole 24 px and touches row 23 and column 23. The icons keep one clear
  pixel round the edge, so scale it down first.
- To scale it, box-filter the 240 px mask down to the size you want. For example, at 0.85 the
  mask becomes 204 px, which you shrink to 20 px with `Image.BOX`. Then colour it: fill orange,
  with a black edge one pixel wide.
- The 240 px mask boxed back to 24 px is within 3 of the original alpha on average (0-255).
- `Tab.png`, the window icon of the licence and other dialogs, is a black silhouette of a close
  sibling of this alpaca, about one pixel apart in places. It was used only as a check.
