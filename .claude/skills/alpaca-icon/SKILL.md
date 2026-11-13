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
  - **The base alpaca is the Gravity Load one** (user, 2026-11-12): start new alpaca drawings
    from its silhouette, in its original orange or in other colours. It is in `Assets/alpaca/`
    without the arrow: 24 px coloured, 24 px silhouette, 240 px smooth mask to scale from. The
    README there says how. `base_alpaca_logo.png` is its outline alone, transparent inside
    (user, 2026-11-12); `base_alpaca_logo-colourful.png` fills it with the logo's colours and is
    the alpaca of the 13_Report icons.
  - Everywhere except 07_Analysis, use the logo alpaca: `Alpaca4d.Gh/Resources/Logo.png`, with
    the head in rows 1-5, the neck in rows 6-10 and the body in rows 11-22, across x 4-21.
  - 07_Analysis uses the cartoon alpaca of Run Analysis, Natural Vibration and Linear Buckling:
    a yellow body and red neck over a grey ghost of the logo's outline.
  - Never cut the cartoon alpaca, by a drawing in front or by the frame (user, 2026-11-12): put
    the topic beside it and scale the alpaca down to fit.
  - Each analysis gets its own pose, so the icons can be told apart (user, 2026-11-12): Linear
    Buckling bows its legs under an arrow on its back, Pushover leans on slanted legs under two
    arrows, Footfall stands beside an 8 x 16 bare foot in grey #80868E (drawn pixel by pixel: shoe prints
    read as blobs), the alpaca scaled to 0.84 so none of it is cut. They share Pushover's head, snout to the right with an ear and an eye: a
    neck curled back into a round head looked rude, and the user turned it down.
    A new cartoon pose is drawn as shapes at 12x and scaled down with a
    box filter, which gives the soft outline; arrows and prints stay crisp.
  - The cartoon alpaca has a **black outline**, #000000 and 1.15 px wide at 1x, round the
    outside only, as Run Analysis and Natural Vibration have (user, 2026-11-12: the 0.85 px
    DarkTech one was too faint). This is the one place the icons use black; arrows and eyes stay
    DarkTech. The wider outline needs room: Pushover's head moved to (19.0, 5.0) and Buckling's
    to hy 5.0, so the ear and the snout keep off the frame. The soft-outline versions are kept as
    `icon-candidates/*-soft-outline.png`.
  - Change it by moving or recolouring whole pixels. Never warp the 1600 px `Assets/Logo.png`
    and scale it down: the neck smears and the outlines vanish.
- **The topic leads, the alpaca stays behind** (user, 2026-11-12). When an icon pairs the logo
  alpaca with the drawing of its topic, draw the topic large in front and let the alpaca peek out
  behind it, cleared one pixel round the drawing. The 13_Report icons: a 12 x 18 page on the left
  (x 1-12, rows 5-22), its paper 25 % white so the background shows through (user, 2026-11-12;
  fully transparent lost the grey lines on the component's grey), and behind it the colourful
  base alpaca (`Assets/alpaca/base_alpaca_logo-colourful.png`), 1 px left and one row shorter, so
  its head, neck and chest show.
  A page on the right with only the head peeking over it lost the alpaca.
- **A load arrow** narrows to a 1 px point in odd widths, rows 1-1-5-3-1. 2-4-2 reads as a
  cross.
- **No black**, but for the cartoon alpaca's outline. The dark of a new icon is DarkTech #2A2D31, Alpaca4d's dark grey (user,
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

The docs picture of the component shows its icon. It is redrawn with the component-images skill
before the next push, not now.
