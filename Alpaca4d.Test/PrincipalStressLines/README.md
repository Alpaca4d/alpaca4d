# Principal Stress Lines

    ./run.sh
    RHINO_ID=rhinocode_remotepipe_<pid> GHDIR=/path/to/Release/net48 ./run.sh
    PARTS=3d ./run.sh                     # only the solids; PARTS=2d only the shells

Checks `PrincipalField` and `StressLineTracer`, which Principal Stress Lines runs, `SolidPrincipalField`
and `SolidStressLineTracer`, which it runs on Solids, and the component itself, in both its units. It needs RhinoCommon's meshes and a solved Grasshopper definition, so it runs inside a
running Rhino 8, through `rhinocode`: start Rhino's script server first (`StartScriptServer`) and
load Grasshopper. Each run copies the build to a fresh folder, because Rhino keeps a DLL it has
loaded mapped. The checks run under the license in `Alpaca4d.Utils/data_august_28.bin` of the main
checkout (`LICENSE=` for another), and the solids need an OpenSees with Ten Node Tetrahedra
(`OPENSEES_SOLIDS=`, `/Applications/OpenSeesLatest-Mumps` by default).

Every line is measured against a closed-form solution of elasticity:

| solution | what it is | mesh |
| --- | --- | --- |
| Timoshenko and Goodier's cantilever | 8 x 2, end load spread as a parabola: exact in 2D | 32 x 8 quads, and the same split into triangles |
| Kirsch's plate with a hole | a hole of radius 1 in an infinite plate pulled one way, seen through a 10 x 10 window | 64 x 20 quads round the hole |
| Navier's simply supported plate | 10 x 10, uniform load, bending | 20 x 20 quads, and triangles |

A line passes when its segments follow the exact principal direction at their middles to under 1° on
average and 3° for 95% of them (0.5° and 1.5° for the plate with a hole). The distance from the exact
trajectory through the same seed must stay under 5% of the beam's depth, the hole's diameter or
the plate's side. Where the two principal values come within 2% of the largest difference, the
direction is not unique, so those stretches are left out of both measures. No line may turn back
on itself.

| case | adds |
| --- | --- |
| the three solutions, fed their exact nodal values | the field and tracer alone |
| a third of the plate's faces turned over | read the other way up to agree with the rest; every face turned over is read from below |
| a closed box of shells | no edge for a line to stop at |
| two faces without area | left out and counted |
| the plate drawn in millimetres | the step taken from the mesh; a line crosses the plate in milliseconds |
| field mode on the plate, with and without Test Dist. | no line turns back; with Test Dist. none comes closer than that to one drawn before it |
| the cantilever through OpenSees and the component | held against rigid-body motion only, the exact tractions at both ends |
| the plate with one node lifted 5 cm | the four quads round it warped well past 0.01 |
| a third of the plate's shells turned over, through the component | the Remark that says so |
| Shells and Solids, one component | a new one opens on Shells; switching to Solids keeps SL1 and SL2 and adds SL3, and back; one saved on Solids reads back on Solids; and the example `Tools/Examples/Benchmarks/StressLinesBenchmarks.gh`, saved before Solids, opens on Shells with its wires on SL1 and SL2 |

What it caught on the way. The component was a port of LilyPad's code, and a benchmark against
these solutions found the following in it:

- **Accuracy.** Its directions came from each face's own strain at the point. On four-node faces
  that carries a spurious shear wherever the face bends, which turned the lines by 2° on average
  on the cantilever, and by 4° on triangles. Taking the strain at each face's centre, averaging it
  into the nodes and interpolating it brought that to 0.3-0.5°.
- **Speed.** It found a point's place in a face by a 16-step search, 66 µs per look-up; the field
  now looks up in under 1 µs. In field mode the plate went from 4.1 s to 41 ms and the
  cantilever from 3.9 s to 0.2 s.
- **Lines that would not stop.** Where the moment vanished, at a simply supported edge's middle,
  a line turned back and ran on along a neighbouring one, drawing it twice. Lines now stop where
  the direction turns more than 45° in a step.
- **Blank or failed output.** One quad warped more than 0.01 blanked the whole output. A closed
  set of shells threw. Bending lines on faces facing different ways came out scrambled.
- **Units and Test Dist.** In millimetres the default step of 0.3 cut every line at 6 m, after
  100 s. The Test Dist. check stopped lines erratically.
- **Two faults in the new tracer, found while writing this.** A point that the mesh put on its edge
  sat there only to single precision, so an edge test tighter than that let a line creep along
  forever. And seeds off the mesh were tested for spacing before being moved onto it, so the same
  line was traced again and again.

## Solids

`SolidStressLinesCheck.cs` does the same for the Solids unit, against two closed forms of
three dimensional elasticity, both checked to be in equilibrium first:

| solution | what it is | meshes |
| --- | --- | --- |
| a twisted and pulled bar | shear K r round the axis and a pull along it: the largest and smallest principal stresses lie at a fixed angle on each cylinder round the axis, so their lines are helices; the middle one, nought, points straight out | 8 x 8 x 24 bricks, 6 x 6 x 18 cells of six ten node tetrahedra |
| Boussinesq | a point load on a half-space, seen through a block below it | 16 x 16 x 10 bricks, 8 x 8 x 5 cells of six ten node tetrahedra |
| Timoshenko and Goodier's cantilever | 8 long, 2 deep, 0.5 wide, an end load: plane stress, exact in three dimensions with Poisson's ratio nought | 32 x 8 x 2 bricks, 16 x 4 x 2 cells of six ten node tetrahedra |
| Timoshenko and Goodier's simply supported beam | 10 long, 2 deep, 0.5 wide, a uniform load on top: the same | 40 x 8 x 2 bricks, 20 x 4 x 2 cells of six ten node tetrahedra |

A line passes on the same measures as above, each family's weak stretches measured against its own
largest gap. The helices must also stay on their cylinder. Under Boussinesq's load the two smaller
principal stresses differ by at most 1.2% of the largest principal difference anywhere in the block,
so their directions turn on a difference no mesh resolves; only the third family is held to limits
there, and the first two are reported.

| case | adds |
| --- | --- |
| the eigenvalues of 2000 tensors, repeated ones among them | the 3 x 3 solver |
| the two solutions, fed their exact stresses at the nodes | the field and tracer alone, on bricks, ten node and four node tetrahedra |
| Boussinesq with one stress per brick, at its centre | what an SSP Brick gives |
| a block with a notch | a line stops at the notch's face, and does not jump it; a seed in the notch draws nothing |
| a tetrahedron with no volume | left out and counted |
| field mode on the bar, with and without Test Dist. | with it, no line comes closer than that to one drawn before it |
| both solutions through OpenSees and the component | the exact tractions on all six faces of the block, held against rigid-body motion only |
| both beams through OpenSees and the component, twice | under the exact tractions on every face; and as drawn, of steel - the cantilever clamped with an even end load, the beam on its bottom edges under an even load on top - measured only more than a depth from the supports and the loads; and a field of lines through each |

Writing them found two faults, both in both tracers. Each Runge-Kutta step was stretched to a full
step, which carried a curving line outwards a little each time, 3% off its helix over a few turns;
steps now take the average as it is. And a step that crossed a place where its family changes
direction - in a beam, where the stress across the width, nought, comes level with an in-plane one -
blended the two, and the line ended with a segment up to 70 degrees out; a line now ends before a
step within which the field turns more than 45 degrees.
