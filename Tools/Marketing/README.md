# Marketing pictures

    ./run.sh                    # every scene
    ./run.sh bridge tower       # some of them

Makes pictures and animations of the examples to share: on social media, in a post, in an email.
They are made in Grasshopper, from the real definitions and the real components, and land in the
website repository next to this one, under `alpaca4d.github.io/marketing/<topic>/<case>/`: a folder
for each case a topic shows, such as `principal-stress-lines/deep-beam`. Each round replaces the
pictures of the round before.

Each scene opens an example on the canvas of a live Rhino 8, solves it or runs it, and saves:

* captures of the Rhino viewport on a light grey (251), with Alpaca4d's own previews: Model View's results
  and their Legend, the supports and loads Model View draws;
* the definition, as Grasshopper draws it;
* the frames of an animation: a topology optimisation run, a mode shape swinging.

`publish.sh` then trims the captures to what is drawn, puts each canvas beside its result as a
working screen would show them (`*-in-grasshopper.png`), and makes the frames into a GIF and an
MP4.

Each case folder in `marketing/` also has a `README.md` with its LinkedIn post: what to attach, the
post and its first comment in text boxes ready to paste, other openings and the numbers behind it.
`python3 social.py` gathers them all into one page, `out/social/index.html` with web-size copies of
the pictures in `out/social/media/`, to publish as an artifact: a card per post with copy buttons and
the path of every file on disk. Its `HOLD` and `NOTES` say which posts should wait, and why. The
page's Open folder and Show in Finder buttons need `python3 finder.py` running: a browser page cannot
open Finder, so they link to that helper on 127.0.0.1, which opens only paths inside `marketing/`.

## The scenes

| Scene | Example | Into `marketing/` |
| --- | --- | --- |
| `cow` | SpotTheCow | `solids/spot-the-cow` |
| `femur` | Femur, with principal stress lines through the bone | `solids/femur`, `principal-stress-lines/femur` |
| `dragon` | InigoTheDragon: modes 0, 1, 4 and 5, two of them moving | `solids/inigo-the-dragon` |
| `bricks` | BrickSolverSpeed, in Von Mises stress | `solids/brick-block` |
| `mixed` | MixedModelSpeed: frame, slab and bricks, displaced | `solids/mixed-model` |
| `bridge` | TopologyBridge: the run, the design, the design analysed | `topology-optimisation/bridge` |
| `tower` | TopologyTower, here: a cube on four corners, to 8% | `topology-optimisation/tower` |
| `benchmarks` | TopologyBenchmarks: top3d's cantilever and simply supported beam | `topology-optimisation/benchmarks` |
| `lines2d` | StressLinesBenchmarks: the shells against their closed forms, Kirsch's plate alone | `principal-stress-lines/benchmarks`, `principal-stress-lines/plate-with-hole` |
| `bridgelines` | TopologyBridge's solid domain, its stress lines over the design | `principal-stress-lines/bridge-domain`, `topology-optimisation/bridge` |
| `plates` | `StressLinesPlates.gh`, here: a cantilever wall and a deep beam in their own plane, a slab on four edges in bending, a barrel canopy cantilevering from its root; each with and without its supports and loads, the four together; the deep beam's load and a point load on the slab walking along, the wall's lines growing from their seed, and each plate spinning and walked through, as GIF and MP4 | `principal-stress-lines/` `cantilever-wall`, `deep-beam`, `slab`, `cantilever-shell`, and `overview` for the four together |
| `kangaroo` | `StressLinesShell.gh`, here: a net form-found by Kangaroo on four corners, analysed as a concrete shell under its own weight, its stress lines in its surface; from four sides; the form being found iteration by iteration with its lines, the shell spinning, walked through and its lines growing, as GIF and MP4 | `principal-stress-lines/form-found-shell` |
| `nerviflatslab` | `StressLinesNerviFlatSlab.gh`, here: the Nervi flat slab, a flat slab on a 5 m grid of columns and its bending lines, from above; from below with the lines as ribs, as Nervi built them, in the brand colours and in concrete; the lines growing, the hall spinning and walked through, as GIF and MP4 | `principal-stress-lines/nervi-flat-slab` |
| `nerviumbrella` | `StressLinesNerviUmbrella.gh`, here: the Nervi umbrella, after the Palazzo del Lavoro - a square slab cantilevering from one column, its bending lines from above; nine as a hall from below, the lines as ribs, in the brand colours and in concrete; the lines growing, the hall spinning and walked through, as GIF and MP4 | `principal-stress-lines/nervi-umbrella` |
| `musmeci` | `StressLinesMusmeciBridge.gh`, here: the Musmeci bridge, after the Basento viaduct - a membrane form-found by Kangaroo on paired footings over four spans, analysed as a concrete shell, its stress lines in its surface, a deck on its crowns; from three sides, the valley and under it; the form being found, the bridge spinning and walked through underneath, as GIF and MP4 | `principal-stress-lines/musmeci-bridge` |
| `cornerslab` `roundumbrella` `roundslab` `candela` `groinvault` `palazzetto` `isler` `hexshell` | The principal stress lines studies, below | `principal-stress-lines/<study>` |
| `twospan` | TwoSpanBeamOptimiser, a load on each span: the HEA element by element with the loads, the deformed shape extruded, the bending moment and the shear, each on its own; the beam deflecting as GIF and MP4; one HEA for the whole beam | `section-optimiser/two-span-beam` |
| `curvedbeam` | CurvedBeamOptimiser: the curved beam sized element by element within the IPEs, a colour for each section, from above at an angle and from the front | `section-optimiser/curved-beam` |
| `beamgrid` | BeamGridOptimiser: the beam grid sized element by element, a colour for each section, and its displacement with the Legend | `section-optimiser/beam-grid` |
| `howto` | `HowToPictures.gh`, here: the six kinds of support, a frame with its beam pinned at both ends, a box of shell walls under wind, a frame's first buckling mode, and a frame with a slab under every kind of load and under its own weight alone | `how-to/<picture>`, and a copy beside its guide in `doc/how-to/`; `model-view/loads/`, and a copy in `doc/basics/visualisation/model-view/` |
| `structures` | `TrussRoofHall.gh`, `SteelTower.gh`, `RoofCanopy.gh`, `VerticalTrussFacade.gh`, `BarrelVault.gh` and `HorizontalTrussFacade.gh` from `../Examples/Structures.py`: each one's loads, its deformed shape over it at rest, the GIF of it deforming, its canvas, and the tower's first mode | `structures/<slug>/`, and the example's pictures in `examples/<slug>/` |
| `earthquakes` | The five earthquake definitions from `../Examples/Structures.py`: each one's canvas, the still at the step it moves the most, and a frame every 0.1 s through the record, which publish.sh plays at ten frames a second. Every frame has its charts, drawn in the scene: the ground's acceleration, and how the points the geometry's `Watch` output marks move along the shaking, read with a Nodal Displacements of the scene's own (for the city, a bar for each roof, lowest building to tallest); publish.sh stacks them under the frames, and under the still as `<slug>-2.png` | `earthquakes/<slug>/`, and the stills and the MP4 in `examples/<slug>/` |

All of them take about 13 minutes on an M2 Pro, a quarter of it in `benchmarks`, which runs top3d's
two problems for 200 iterations each; `plates` and `kangaroo` take about two minutes and one.

A structure deforming - a GIF and an MP4 of it swinging from rest to its deformed shape, looping, and
the still at full deformation - comes from `Deforming` in `Shots.cs`: the deformation-pictures skill
says how to set up the definition and the scene.

Every topology optimisation gets a GIF and an MP4 of its run, design by design, and its final
design analysed in Von Mises stress: they are what gets shared most. Every principal stress lines
case gets a spin and a walk-through (`Turning` and `Walk` in `Shots.cs`), its lines in the brand
purple (the first family) and light blue (the second).

## The principal stress lines studies

Symmetric slabs and shells, each a definition of its own in `Studies/`, written by
`./make-studies.sh` from `StressLinesStudies.py`; their shapes - meshes, supports, seeds, a Kangaroo
form's lift - are in `StressLinesShapes.py`, plain Python, so that a shape can be tried outside
Rhino first. One scene, `StudyScene` in `Shots.cs`, pictures any of them, with where to stand in
`Studies`; `publish.sh`'s `study` publishes it.

| Scene | Study | Kind |
| --- | --- | --- |
| `cornerslab` | `corner-slab`: a slab 12 x 12 m on four corner columns | bending; ribs and columns |
| `roundumbrella` | `round-umbrella`: the Nervi round umbrella, a disc 16 m across built into one column | bending; ribs and a column |
| `roundslab` | `round-slab`: a disc 20 m across on eight columns in a ring | bending; ribs and columns |
| `candela` | `candela-umbrella`: four hypars on one column, after Candela, a hall of nine | membrane |
| `groinvault` | `groin-vault`: two barrels 12 m square crossing, on four corners | membrane |
| `palazzetto` | `palazzetto`: a dome 30 m across on sixteen footings, after Nervi's, form-found | membrane; Kangaroo |
| `isler` | `isler-shell`: a three-point shell 24 m a side, after Isler, form-found | membrane; Kangaroo |
| `hexshell` | `hexagonal-shell`: a hexagon 24 m across on its six corners, form-found | membrane; Kangaroo |

Each gets its lines from above, with and without its supports; from below, where its walk starts -
a slab's lines as ribs, in the brand colours and in concrete; from outside; its definition; a
Kangaroo form being found; a spin, a walk-through and its lines growing, as GIF and MP4.

To add a study: its shape in `StressLinesShapes.py` (`study` and `STUDIES`), `./make-studies.sh
<name>`, where to stand in `Studies` in `Shots.cs`, and a line in `publish.sh` and `ALL` in
`run.sh` and `publish.sh`. A symmetric shape has a point where the two principal stresses are the
same, at its middle: start the lines from a seed off it, and off its axes.

## What it needs

* Rhino 8 for Mac, with the Alpaca4d to picture installed in its Grasshopper. The pictures show
  that one, not this checkout. Building `Alpaca4d.Gh` installs it into
  `~/Library/Application Support/McNeel/Rhinoceros/MacPlugIns/Grasshopper/Libraries/Alpaca4d`:
  to picture a branch, build it there with Rhino closed, and put the earlier one back afterwards.
* A licence: the examples are past the free limit. `run.sh` installs
  `Alpaca4d.Utils/data_august_28.bin` when it is there; `LICENSE=/path/to/licence.bin` names
  another, and `LICENSE=` none, for a Rhino with a licence of its own.
* mono's `csc` and ffmpeg (`brew install mono ffmpeg`), python3.
* Kangaroo2, which Rhino 7 and 8 ship, for `kangaroo`.
* Grasshopper and RhinoCommon 7.18 from the NuGet cache, which a build of the plugin has already
  restored, to compile `Shots.cs` against.

`run.sh` starts a Rhino of its own and quits it at the end. A Rhino that is already open stops
it, because a second one started beside it runs no scripts: close it, or run `StartScriptServer`
in it and set `RHINO_ID` to its name in `rhinocode list`. `PUBLISH=0` keeps the captures in `out/`
for `./publish.sh <scene>` to make into pictures later, and `MARKETING=` points `publish.sh`
elsewhere.

## How it works

1. **run.sh** compiles `Shots.cs` into a DLL with a new name each time: a Rhino that loaded an
   earlier build would hand that back for the same name. It starts Rhino with its script server
   and has `rhinocode` run `Runner.cs`, which loads the DLL and starts the scenes.
2. **Shots.cs** runs the scenes one after another, a step at a time while Rhino is idle, so that
   Rhino keeps drawing and a topology optimisation can run on its own thread. It drives the
   installed plugin through Grasshopper alone: components by GUID, their menus by reflection.
   It writes `out/log.txt` as it goes, which `run.sh` prints, and `out/done.txt` at the end. A
   scene that fails is logged and skipped, and the others still run.
3. **publish.sh** and **post.py** make the captures into the pictures.

Nothing is saved into the examples. What a scene changes stays on the canvas while it runs:

* Custom Previews in the brand colours, fed by a swatch, where Grasshopper's pink would be the
  only colour;
* Weld Mesh, for a smooth optimised surface;
* Model View drawing the supports and loads, with every element left out by its ElementId and
  so greyed;
* a Legend, or a Run Analysis of the optimised design.

Two of Grasshopper's settings change for a run and are put back at its end: the mesh edges
preview, off, and a display mode of Rhino's, Shaded on light grey, which is deleted.

## Adding a scene

Write a method in `Shots.cs` that returns its steps: `Do` for a step done at once, `Until` for one
waited on, such as a run. Use the helpers there: `Open`, `Preview`, `Symbols`, `ShowResult`,
`AddLegend`, `Iterations`, `Analyse`, `Shoot`, `Aim`, `Canvas`. Then name the scene in three
places:

* the switch in `Run`;
* `ALL` in `run.sh`;
* `publish.sh`, with a function of the scene's name.

To learn how something reads before committing to it, run that scene alone.

## Known problems in the examples

* **The committed `Tools/Examples/Femur.gh` does not solve.** Three edge nodes of the
  tetrahedra have no support, and OpenSees does not converge. So the `femur` scene fails until
  the file is fixed.
* **`StressLinesBenchmarks.gh` has two Principal Stress Lines components on top of each other**
  in groups A5, B5 and C5. The scene moves them apart on the canvas.
* **A Rhino that run.sh started may not quit by itself** at the end (seen on 2026-11-04). run.sh
  then kills it after a minute, and the next Rhino can come up with a dialog that stops its
  script server. Start one Rhino with `open -na "Rhino 8" --args -nosplash
  -runscript="_StartScriptServer"` and give every run its `RHINO_ID`: run.sh leaves it open.
* **`rhinocode list` can stop showing a Rhino whose script server still answers** (seen after a long
  run, 2026-11-04): its pipe is still there, `$TMPDIR/CoreFxPipe_rhinocode_remotepipe_<pid>`, and a
  script sent with `--rhino rhinocode_remotepipe_<pid>` still runs. Try one before closing it.
* **Model View stops drawing at an area load of 0**, before the supports: `plates` unwires the
  slab's Mesh Load for its point-load shots rather than setting it to 0.
* **The dragon's deformation slider is saved as 0 to 0.8.** The scene opens it to -2 to 2, so
  that a mode can swing both ways.
