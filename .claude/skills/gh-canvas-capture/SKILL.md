---
name: gh-canvas-capture
description: Make pictures of Grasshopper definitions or single Alpaca4d components from the canvas, and light-grey-background Rhino viewport captures of Alpaca4d models and results, from a script in a live Rhino 8. Use it for docs, example and marketing pictures.
---

# Capture the Grasshopper canvas and the Rhino viewport

Run the capture scripts as the run-in-rhino skill says. Keep the working files (definitions,
scripts, raw captures) in the scratchpad.
- Finished pictures go only where the work needs them: beside a docs page, in an example's folder.
  Copies in `marketing/` only when the user asks, with `/marketing-content`.
- **Animations are GIFs only, no MP4** (user, 2026-11-13).
- **`Tools/Marketing/run.sh` makes the pictures of the examples.** Its README lists the
  scenes and how to add one, and its `Shots.cs` holds working code for everything below. For new
  work on an example, add or extend a scene there rather than writing a script from nothing.

## Definitions written by Tools/Examples/make.sh

- **Lay them out at positive canvas coordinates**, for example `grid.arrange(300, 300, ...)`.
  `GenerateHiResImage` cuts the left or top of a region with a negative origin.
- **Stack components with `gap=30`.** mono measures them shorter than Rhino does, and the name
  tab gets covered.
- **Give panels a colour**, `p.Properties.Colour = Color.FromArgb(255, 255, 250, 90)`. Otherwise
  Rhino draws them see-through with white text.
- **Panel display:** `Multiline = False` makes each line an item of its own, and
  `DrawIndices` / `DrawPaths` False shows the bare values.

## The canvas

- **`GH_Canvas.GenerateHiResImage(Rectangle region, GH_ImageSettings, out Size)`** works on a Mac.
  - It ignores `settings.Folder`: it writes tiles named `column;row.png` to a new temp folder and
    returns their paths.
  - Export a generous region, then trim the picture to the drawn pixels.
- **Single components:** open from file a make.sh row of unwired components. A `GH_Document` built
  in the script exports blank.
- **To draw them grey rather than orange:** with the document disabled, call
  `ExpireSolution(false)` and `ClearRuntimeMessages()` on each component, and `ClearData()` and
  `ClearRuntimeMessages()` on its parameters.
- **To cut one export into components,** split at the blank column strips between them. Sample y
  sparsely: `GetPixel` is slow at 3x.

## The viewport

- **Background: a light grey, 251, not white** (the user's choice for marketing pictures; `post.py`
  keeps it when it trims):
  - `DisplayModeDescription.CopyDisplayMode` of Shaded, `FillMode = SolidColor`, `SetFill(251 grey)`,
    `UpdateDisplayMode`;
  - capture with `view.CaptureToBitmap(size, mode)`, because ViewCapture lags the display mode;
  - `DeleteDisplayMode` afterwards.
- **Frame on the points of the output geometry**, not on a bounding box: an oblique view comes out
  small. For a top view, `FitPoints` needs Y as the up direction.
- **Grasshopper previews** show only for the canvas document, and only with the editor visible.
- **Menu widgets** have no names, so find them by field name. Setting `Value` does not fire
  `ValueChanged`, so invoke the delegate yourself.
- **A fibre section** reads poorly from its own preview. For its capture only:
  - draw the meshes shaded light grey in `PreDrawObjects`;
  - draw each bar as a disc of its real diameter in `DrawForeground`;
  - unhook both in `finally`.
- **A conduit stays enabled for good.** Restart Rhino before the next capture run, or the old
  run's geometry shows underneath.
- **Viewport captures hang while the screen is locked** (seen 2026-11-07: a batch stopped at its
  next capture when the screen locked). Before a long run, check that the screen will stay
  unlocked; after a stall, quit that Rhino and run the rest of the scenes again.
- **Licensed components** refuse without a licence in that Rhino, and open the License window.
