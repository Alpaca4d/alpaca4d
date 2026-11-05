# Marketing pictures

    ./run.sh                    # every scene
    ./run.sh bridge tower       # some of them

Makes pictures and animations of the examples to share: on social media, in a post, in an email.
They are made in Grasshopper, from the real definitions and the real components, and land in the
website repository next to this one, under `alpaca4d.github.io/marketing/<topic>/`. Each round
replaces the pictures of the round before.

Each scene opens an example on the canvas of a live Rhino 8, solves it or runs it, and saves:

* captures of the Rhino viewport on white, with Alpaca4d's own previews: Model View's results and
  their Legend, the supports and loads Model View draws;
* the definition, as Grasshopper draws it;
* the frames of an animation: a topology optimisation run, a mode shape swinging.

`publish.sh` then trims the captures to what is drawn, puts each canvas beside its result as a
working screen would show them (`*-in-grasshopper.png`), and makes the frames into a GIF and an
MP4.

## The scenes

| Scene | Example | Into `marketing/` |
| --- | --- | --- |
| `cow` | SpotTheCow | `solids/spot-the-cow` |
| `femur` | Femur, with principal stress lines through the bone | `solids/femur`, `principal-stress-lines/solids` |
| `dragon` | InigoTheDragon: modes 0, 1, 4 and 5, two of them moving | `solids/inigo-the-dragon` |
| `bricks` | BrickSolverSpeed, in Von Mises stress | `solids/brick-block` |
| `mixed` | MixedModelSpeed: frame, slab and bricks, displaced | `solids/mixed-model` |
| `bridge` | TopologyBridge: the run, the design, the design analysed | `topology-optimisation/bridge` |
| `tower` | TopologyTower, here: a cube on four corners, to 8% | `topology-optimisation/tower` |
| `benchmarks` | TopologyBenchmarks: top3d's cantilever and simply supported beam | `topology-optimisation/benchmarks` |
| `lines2d` | StressLinesBenchmarks: the shells | `principal-stress-lines/shells` |
| `bridgelines` | TopologyBridge's solid domain, its stress lines over the design | `principal-stress-lines/solids`, `topology-optimisation/bridge` |
| `twospan` | TwoSpanBeamOptimiser, a load on each span: the HEA element by element with the loads, the deformed shape extruded, the bending moment and the shear, each on its own; the beam deflecting as GIF and MP4; one HEA for the whole beam | `section-optimiser/two-span-beam` |

All of them take about 10 minutes on an M2 Pro, a third of it in `benchmarks`, which runs top3d's
two problems for 200 iterations each.

Every topology optimisation gets a GIF and an MP4 of its run, design by design, and its final
design analysed in Von Mises stress: they are what gets shared most.

## What it needs

* Rhino 8 for Mac, with the Alpaca4d to picture installed in its Grasshopper. The pictures show
  that one, not this checkout. Building `Alpaca4d.Gh` installs it into
  `~/Library/Application Support/McNeel/Rhinoceros/MacPlugIns/Grasshopper/Libraries/Alpaca4d`:
  to picture a branch, build it there with Rhino closed, and put the earlier one back afterwards.
* A licence: the examples are past the free limit. `run.sh` installs
  `Alpaca4d.Utils/data_august_28.bin` when it is there; `LICENSE=/path/to/licence.bin` names
  another, and `LICENSE=` none, for a Rhino with a licence of its own.
* mono's `csc` and ffmpeg (`brew install mono ffmpeg`), python3.
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
preview, off, and a display mode of Rhino's, Shaded on white, which is deleted.

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
* **The dragon's deformation slider is saved as 0 to 0.8.** The scene opens it to -2 to 2, so
  that a mode can swing both ways.
