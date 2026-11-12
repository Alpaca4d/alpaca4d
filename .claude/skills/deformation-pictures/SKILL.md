---
name: deformation-pictures
description: Make the picture of a structure deforming - a looping GIF and MP4 of it swinging from rest to its deformed shape, and the still at full deformation - from an Alpaca4d definition in a live Rhino 8. Use it for the first picture of an example, a docs page or a post, and for mode shapes and buckling modes too.
---

# A structure deforming: the GIF, the MP4 and the still

The pictures are made by a scene of `Tools/Marketing` (its README says how to run one), with the
`Deforming` helper of `Shots.cs`. `Structures` and `TwoSpan` in `Shots.cs` are worked examples: `Structures` makes the six conventional
structures' GIFs from `Tools/Examples/Structures/Structures.py`, whose `deformed` sets up the view. Follow the gh-canvas-capture skill for everything
about capturing; this skill is what makes these pictures read well.

## 1. The definition

Build it with a script, as `Tools/Examples` does, so the pictures can be made again.

- **One Model View for the results**, on the analysed model:
  - `widget(view, "_ckDeformed").Active = True`, and `show_result(view, DISPLACEMENT, MAGNITUDE)`.
    The colours are Model View's own; leave them.
  - **Deformation scale**, `_slDeformScale`: the largest displacement should show as about 5 to 10%
    of the model's size. For a static result, work it out from the displacement the analysis gives.
    A mode or a buckling mode is already fitted to 8% of the diagonal, so leave it at 1.
  - **Extruded** stays on for beams, so the sections show.
  - **The loads on it**, for a static result: `_ckShowLoads` on, `_ckShowLoadValues` off and
    `_slLoadScale` at about 0.4, so that the arrows ride on the moving shape without hiding its
    colours. A mode has no loads.
  - `_ckWires` and `_ckReleases` off on a structure of thin members: the edges and the release
    symbols hide the colours.
- **A Legend** wired from that Model View's Values and Colours, titled with the quantity and its
  unit: `Displacement [mm]`. When the scale is not 1, put it in the caption, not the title:
  "deformation drawn 20 times".
- **The model at rest**, for anything that sways or buckles: a second Model View of the assembled
  model with `_ckExtruded` off, so that it draws as lines behind the moving shape. Give the material
  a light grey Colour and set the view's `_ddColourBy` to 2, by material, so that it stays faint.
  Leave it out for shells: drawn solid, a shell at rest hides the deformed one.
- **The supports and the loads** where they help: a Model View with `_ckShowSupports`,
  `_ckShowLoads`, `_ckShowLoadValues`.
- Nicknames on everything the scene must find: `Find<IGH_Component>("Deformed shape")`.

## 2. The scene

```csharp
Do("open", () => Open("MyExample.gh")),
Do("still", () =>
{
    ShowOnly(deformed, legend, atRest);
    doc.NewSolution(false);           // or the preview still shows what was shown before
    Shoot(Aim(Grown(Drawn(), 0.05), eye, Vector3d.ZAxis), "my-example-deformed");
}),
Do("deforming", () => Deforming(deformed, eye, "my-example-deforming-frames", 40, turn: 0)),
Do("close", Close),
```

- `Deforming` swings the deformation scale from 0 to its value and back, on a cosine, 40 frames by
  default, so the loop has no jump. It frames the view at full deformation, so nothing leaves the
  picture.
- **The colours fade with the swing, in every deforming animation, mode shapes and buckling modes
  too** (the user, 2026-11-07): at rest the model is all the colour of zero, at full deformation it
  has the result's own colours, and in between each value is coloured times the share of the swing.
  It is what Model View's Animate does. `Deforming` and `Vibrating` switch it on for their frames
  with `FadeWithSwing` and off after, so the still at full deformation is the same as before. A
  signed result (Uz, σx) turns round when a mode swings the other way; a size (|U|, Von Mises)
  does not. It needs an Alpaca4d build that has Model View's `_swingReach`, deployed before Rhino
  starts; with an older one the scene logs it and the colours stay full.
- `turn: 30` also turns the camera 30 degrees about the vertical and back as it deforms. A slow turn
  reads well for a tower, a vault or a grid; keep it at 0 for a beam or a frame seen from the front.
- **A buckling mode: never turn** (the user, 2026-11-07), and colour it by how far each point moves
  in it on a jet gradient, dark blue to dark red, fed to Model View's Colors: `BucklingFrames` in
  `Shots.cs` does both.
- **The eye**, the direction from the model to the camera:
  - from the front and a little above for a beam or a frame: `(0.35, -1, 0.3)`;
  - from a corner and above for a grid, a roof or a vault: `(-1, -1.3, 0.9)`;
  - from low on a corner for a tower: `(1, -1.2, 0.35)`.
- `ShowOnly` and then `doc.NewSolution(false)` before every shot after the first: without the
  solution the capture can come out blank.

## 3. Publishing

In `publish.sh`, a function named after the scene:

```bash
myexample() {
  local d=$M/<topic>/<case>
  mkdir -p "$d"
  trim myexample/my-example-deformed "$d/my-example-deformed.png" 0 12
  $P anim "$OUT/myexample/my-example-deforming-frames" "$d/my-example-deforming" 40 900 15 0
}
```

- `anim frames base margin width fps hold`: a GIF `width` wide and an MP4 twice that, both looping.
  With `hold` 0 there is no pause, which the cosine swing does not need. 40 frames at 15 fps is a
  2.7 s loop.
- A GIF over about 5 MB is too heavy for a page: make it narrower (700) or use fewer frames (30).
- Add the scene to `ALL` in `run.sh` and `publish.sh`, and a row to the README's table of scenes.

## 4. Where the pictures go

- `marketing/<topic>/<case>/`, as every picture of the work does: the still, the GIF, the MP4.
- The still is the **first picture** of an example (`examples/<slug>/<slug>.png`) or a docs page.
  The GIF can take its place on a page. The MP4 is for the website and for posts.

## Look

- The background is the light grey, 251, which `Shoot` sets. The legend, the supports and the loads
  use Alpaca4d's own drawing.
- Show one result at a time; nothing on the canvas side.
- Before publishing, look at the first frame, the middle one and the still: nothing cut off, the
  legend readable, the deformation clear but not absurd. The first frame should be all one colour,
  the colour of zero: if it is not, the colours did not fade.

## A time history

A transient analysis is shown by its steps, not by a scale: the `earthquakes` scene in `Shots.cs` steps
a `Step` slider wired to Model View, a frame every fifth step - 0.1 s of a record sampled at 0.02 s - and
`publish.sh` plays them at 10 fps, so that the video runs as the ground moved. Its still is the step the
structure moves the most, found by stepping through the record and reading Model View's Info.

- Give Model View a fixed **Range** over the whole record, or each frame's colours stretch to its own
  largest displacement and the legend jumps. Info then reports that Range, not what is drawn: take it
  off while searching for the worst step, as the scene does.
- Draw the elements' edges and the supports (`_ckWires`, `_ckShowSupports`, a Support scale of about 3
  on a bridge), and start the colour scale light: with black edges, a dark start makes everything that
  hardly moves black.
- No grey view at rest under a structure with slabs that moves sideways: the two slabs lie in one plane
  and flicker through each other.
- Every frame gets its charts, drawn in the scene with System.Drawing (it works in Rhino on a Mac): the
  ground's acceleration and the movement of the points the geometry's `Watch` output marks, read once
  with a Nodal Displacements added for it (History on, NodeTag the nearest nodes - expire the inputs
  after setting their data, or they keep the old). `post.py animcharts` stacks them under the frames,
  cut to what is drawn; `post.py stack` puts the worst step's under the still as `<slug>-2.png`.
- Set the legend smaller for the frames than for the still, which is captured larger than the screen.
- On a page use the MP4 in a `<video autoplay loop muted playsinline>`: ten seconds of shaking make too
  heavy a GIF. Keep the GIF, 900 px wide, for posts.

