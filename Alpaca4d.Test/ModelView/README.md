# Model View

    ./run.sh            # mono: menus, files, what each control does
    ./grasshopper.sh    # in a running Rhino 8: what it draws, the upgrades, timings

Model View draws the model and any of its results. It took over from the Model View that drew the
model only and from View Results_WIP, which are kept, hidden, in `Alpaca4d.Gh/Obsolete` so that
files saved with them still open; Upgrade Components swaps them for the new one.

## run.sh, in mono

The real component, built as Grasshopper builds it:

* **Results** has a checkbox for each result, in View Results' order, none ticked for the model
  alone; then the old Model View's Elements, Loads and Supports menus. The inputs are View Results'
  five in their order, then LoadPattern, so an upgraded View Results keeps its wires by place, and
  Range takes a list, one per result.
* Each choice shows only the controls it uses - no result its Colour by, each result ticked its own
  Component and the Unit of the component, *Beam forces* its diagram scale, *Shell stresses* its
  Layer, *Reactions* its style and arrow scale. Two results ticked show both; ticking *Shell forces*
  unticks *Shell stresses*, which colour the same faces. `ShowResult`, for scripts, ticks one alone.
* *Beam forces* picks its forces in a row of checkboxes, N Vy Vz T My Mz, N to start with; the
  force unit and the moment unit each show while one of their kind is ticked.
* The row itself, `MenuCheckBoxGroup`: one line high laid out in a row, a line per box in a column,
  carried on to the next line when the menu is too narrow, and what is ticked saved and read back.
* A file keeps the results ticked, each one's Component and Unit, the beam forces ticked, Colour by,
  the checkboxes and the sliders.
* **Every control is listened to once.** The old Model View and View Results listened to every
  control when their menus were made and again when a file was read into them, so every one opened
  from a file ran its solution twice for each click. Both the new component and the old ones are
  checked, after a file is opened.
* The controls that change only what is drawn over the model - ids, axes, releases, loads,
  supports, element edges, values and their sizes - redraw the viewport and run no solution.
* A file from before the loads were drawn to fit the model opens at Load scale 1: its scale was
  metres of arrow per kN.

A `GH_Document` cannot be made in mono, so the upgrades are checked in Rhino.

## grasshopper.sh, in Rhino

A plate of ASD shells with edge beams, supports and an area load, built from the real components,
assembled and analysed:

* No result on the assembled model: coloured meshes in **Geometry**, the shells as thick as their
  section, the beams as lines with Extruded off; a remark for Deformed shape and a warning for a
  result, which both need an analysed model - the model is drawn as it is.
* The results on the analysed model, one at a time: Info, Values, the deformed shape, the beam
  force diagrams, one reaction arrow per support, and the model alone on the deformed shape.
* Four results ticked together - Displacement, Beam forces, Shell stresses, Reactions: a line of
  Info each, a branch of Values and Colors for each but Beam forces, the shells coloured by their
  stress and the beams by the displacement, the diagrams and the arrows; Range given per result in
  the menu's order; a Legend drawing a legend per branch, each with its own title; and Shell forces
  ticked beside Shell stresses colouring the shells, with a remark for the one hidden.
* Every beam force at once: a line of Info each, forces in kN and moments in kN·m, a diagram
  each, no Values; a force unticked lets go of what was read for it; none ticked gives a remark.
* ElementId: what is left out is in **Geometry**, grey; the ids are written on what was kept.
* Show Supports and Show Loads leave the solution as it is and grow the clipping box.
* Every overlay over four deformed results, drawn into a viewport by a conduit, without an error.
* Every kind of load on the plate, each given element by element: one value a run of beams or
  group of shells, the point load's arrow 15% of the model's diagonal and the line and area loads'
  60% and 40% of it, twice as long at Load scale 2, the area load's sheet on the side it comes
  from, the self-weight a uniform load along each beam, as OpenSees is given it, drawn as a line
  load in its own colour, the line load's arrows spread along
  each run, an arc for a moment and a dot for a mass - and all of it drawn without an error.
* With Deformed shape ticked, the loads ride on the deformed shape: every arrow on a node moved as far
  as the node, none lost or gained.
* The old Model View and View Results_WIP upgraded: settings, wires, LP to LoadPattern, ElemIds to
  ElementId as text - and the upgraded View Results gives the same Info and Values as the old one.
* Timings of the old components against the new one on a plate of 3000 shells.

The two checks that draw into a viewport - every overlay drawn, and the timings of each frame -
need a Rhino with a document open, and say SKIP without one.

`LICENSE=.../data_august_28.bin` installs a licence first: the timing model is past the free limit,
and Model View needs one whatever the model.

## What the timings showed (2026-11-03, plate of 3000 shells and 120 beams)

| | old | new |
| --- | --- | --- |
| licence check, every solve | 15.9 ms | 0.1 ms |
| a result solved again, nothing new to read | 94 ms | 106 ms |
| a display toggle such as Element edges | that solution | a redraw, 0.1 ms |
| each frame with Element IDs | 211 ms | 108 ms |
| each frame with Local Axes | 490 ms | 279 ms |
| each frame with Show Loads | 606 ms | 1.1 ms |

The licence check listed the computer's network interfaces on every call, which takes 16 ms on a
Mac; `License` now keeps them, and looks again only when a check against the kept ones fails. The
menu's layout used to paint the canvas there and then, and the solution painted it again at its
end; it is now painted once, when the canvas next paints. The per-frame costs left are Rhino's
own: three thousand pieces of 3D text, or nine thousand arrow heads, every time the view moves.
