# Shell Section

    ./run.sh          # without Rhino; with OpenSees on PATH, or OPENSEES=..., the solved checks too
    ./grasshopper.sh  # in a running Rhino 8 with its script server started (StartScriptServer)
                      # and Grasshopper loaded: the component itself

Shell Section cuts shells along a curve swept along a direction, and reads the forces through the
cut: per length at points of it, and in total. The working is `Alpaca.Core/Result/ShellSection.cs`.

## run.sh

`ShellSection.cs` runs under mono, against Core only.

**Made-up fields.** A field is put at the Gauss points the way the solver puts its own: at ±1/√3
on a quad, in the order of its corners, and at the middle of each side on a triangle, point *i*
facing corner *i*. Read back at points of a bent cut, a bilinear field on quads and a linear one
on triangles come back to 1e-14, in any frame, smoothed or not, with some faces facing down. The
geometry is checked as well: a cut along the lines of a mesh is counted once, one through its
corners runs from corner to corner, one along its border finds the faces on its one side, and the
ends of a cut are where it leaves the mesh.

**Solved by OpenSees.** The decks are written by the test, solved, and read back with
`Read.ShellForcesByTag`. The total across a cut is the load on the side *x* points to, so a
cantilever plate cut anywhere carries its tip load and its moment, and a wall cut across carries
what is on top of it.

| model | how close the smoothed totals come |
| --- | --- |
| quads, Poisson 0 | 1e-6: the linear moment of a strip is held exactly |
| quads, Poisson 0.3 | 3%: the free edges carry a plate's twist and shear |
| triangles, Poisson 0.3 | 10%, moments 1%: MITC3's shears are rough |
| wall of quads | 0.1% |

The same plate with some faces turned round gives the same totals to 1e-9.

## What the elements give

Found while writing these checks, and worth knowing before reading a number.

* **ASDShellQ4's moments are one per element.** Its four Gauss points hold the same moment, the
  value at the element's centre. Unsmoothed, a cut reads steps; smoothed, a linear moment is read
  exactly away from the edges.
* **ASDShellT3's recorded shears are too large.** The element scales its shear by Stenberg's
  factor, sqrt(h² / (h² + 0.1 L²)) with L its longest side, on the strain it hands the section and
  again on the shear it takes back (ASDShellT3::calculateAll). The recorder reads the section in
  between, so the recorded shear is the one in equilibrium divided by the factor: 1.5 times too large
  on the triangles here. `SectionFace.Of` scales it back when it is given the thickness. Shell Forces
  and View Results show it as recorded.
* **MITC3's shears differ by a quarter between the points of one triangle**, and carry a spurious
  part along the cut that a diagonal cut picks up: 55% too much Vz on this mesh. The diagonal is not
  asked of triangles.

## grasshopper.sh

`GrasshopperChecks.cs` builds the cantilever out of the real components, solves it and cuts it with
Shell Section twice in one solution: the totals against statics, the tree of the outputs, the axes,
a diagram drawn, the unsmoothed steps, a sweep down turning My, and the warning for a curve that
misses. Then the same plate in triangles, whose Vz must be the scaled-back one. `LICENSE=...` installs
a licence first.
