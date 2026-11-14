---
name: alpaca-icon
description: Draw or change a 24 px Alpaca4d Grasshopper component icon (pixel art in Alpaca4d.Gh/Resources), or add a new icon to the plugin's resources. Holds the style rules the user has set for icons.
---

# Draw an Alpaca4d icon

## Rules the user set

- **24 x 24 PNG, whole pixels, no anti-aliasing.** The exception is the cartoon alpaca of the
  analysis tab, which keeps its soft outline.
- **Always leave a pixel around.**
  - Rows 0 and 23 and columns 0 and 23 hold no pixel, not even a faint shadow. Check with a
    script that counts the non-transparent pixels on the border.
  - A badge or chart added to a drawing keeps one clear pixel between itself and the drawing.
- **The alpaca.**
  - Everywhere except 07_Analysis, use the logo alpaca: `Alpaca4d.Gh/Resources/Logo.png`, with
    the head in rows 1-5, the neck in rows 6-10 and the body in rows 11-22, across x 4-21.
  - 07_Analysis uses the cartoon alpaca of Run Analysis, Natural Vibration and Linear Buckling:
    a yellow body and red neck over a grey ghost of the logo's outline.
  - Change it by moving or recolouring whole pixels. Never warp the 1600 px `Assets/Logo.png`
    and scale it down: the neck smears and the outlines vanish.
- **A load arrow** narrows to a 1 px point in odd widths, rows 1-1-5-3-1. 2-4-2 reads as a
  cross.
- **No black.** The dark of a new icon is DarkTech #2A2D31, Alpaca4d's dark grey (user,
  2026-11-01). Take the other colours from the brand palette in `Alpaca.Core/Utils/BrandColors.cs`:
  AlpacaLightBlue #4288F7, AlpacaDarkBlue #011EFE, AlpacaOrange #EB6C3F, AlpacaPurple #9E35DA,
  AlpacaLightGreen #AFC930. Node Forces gives each force its own brand colour and draws its
  moment as a curved arrow: the user turned down red arrows, then all-blue ones.
- **A moment** is a 1 px arc with a filled right-angle head at its end. **A 45° arrow** has a
  filled head three pixels on a side, its right angle at the tip; four reads as a block beside a
  5-3-1 head.
- **Colours in older icons**, left as they are unless the user names the icon: dark #1E1E1E,
  blue #3B4CC0 shaded #1F2A80, red #ED0B2B, hooves #7D7D7D.
- **Node dots** are a 3 x 3 DarkTech ring with a white centre.
- **Only touch the icons the user names.** A toolbar-wide redraw was stopped on 2026-10-23.

## Working

- **Draw at 10x, then judge at 1x** on a sheet beside the toolbar neighbours. Show the user that
  sheet.
- **Keep the drafts** in `Assets/icon-candidates/<component>-<idea>.png`. That folder is tracked.
- **No Pillow here.** python3 has none: write PNGs with `zlib` and `struct`, or make a scratch
  venv with Pillow.

## A new icon in the plugin

1. Save it as `Alpaca4d.Gh/Resources/<Name> (Alpaca4d).png`.
2. In `Alpaca4d.Gh/Alpaca4d.Gh.csproj`, add an `<EmbeddedResource Include="Resources\<Name> (Alpaca4d).png">`
   with `CopyToOutputDirectory` Never, like its neighbours.
3. In `Alpaca4d.Gh/Properties/Resources.resx`, add a `<data name="<Name>__Alpaca4d_">`, the
   ResXFileRef of its neighbours. Spaces and brackets become `_`, so `Support (Alpaca4d)` is
   `Support__Alpaca4d_`.
4. In `Properties/Resources.Designer.cs`, add the matching property.
5. In the component, set
   `protected override System.Drawing.Bitmap Icon => Alpaca4d.Gh.Properties.Resources.<Name>__Alpaca4d_;`.

The docs picture of the component shows its icon. Redraw it afterwards with the
component-images skill.
