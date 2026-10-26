# Ten Node Tetrahedron

    ./run.sh          # without Rhino
    ./grasshopper.sh  # in a running Rhino 8 with its script server started (StartScriptServer)

The quadratic tetrahedron: four corners and a node at the middle of each edge. Its displacement is
quadratic and its strain linear, so it bends; the four node tetrahedron has one constant strain and
locks. Ten Node Tetrahedron takes the same four vertex mesh as Four Node Tetrahedron and adds the edge
nodes at the midpoints (`Utils.CleanTenNodeTetrahedron`).

## Which OpenSees

Alpaca4d writes `element TenNodeTetrahedronStd`, not `TenNodeTetrahedron`. Two reasons:

* **Std takes the edge nodes in the standard order** (VTK's, Abaqus'): node 5 on edge 1-2, 6 on 2-3,
  7 on 3-1, 8 on 1-4, 9 on 2-4, 10 on 3-4. Plain `TenNodeTetrahedron` has 9 and 10 the other way round
  (gmsh's). Deserialise reads both.
* **Only an OpenSees with the fixes knows it.** `TenNodeTetrahedronStd` came in with OpenSees pull
  request #1833 (José Abell, merged 16 October 2026), with the fixes the element needed. The
  `TenNodeTetrahedron` of 3.8.0 and earlier:

  | | 3.8.0 | master since #1833 |
  |---|---|---|
  | volume integrated | a sixth: `Jdet` is the volume, and the weight 1/24 is the one for 6V | all of it |
  | patch test, reactions at x = 0 (`patch.tcl`) | -0.0667 | **-0.4** (exact) |
  | cantilever of 60, tip (`cantilever.py`) | 2.411 | **0.4018** (beam theory 0.4024) |
  | `eleResponse $tag stresses`, a recorder of stresses | 24 numbers written into a Vector of 6: heap corruption, first Gauss point only | 4 points x 6 |
  | inertia load (`UniformExcitation`), mass residual | sized for 4 nodes | 10 |

  Stiffness, mass and self weight are all a sixth, so a model loaded only by its own weight deflects
  right and everything else is six times off. The Mumps build in /Applications/OpenSees3.8.0-Mumps is
  3.8.0 too.

  So the command name is the version check: such an OpenSees stops at the element line, and Run
  Analysis and Natural Vibration say why (`TenNodeTetrahedron.SolverTooOld`), where any other error in
  the deck only gets "OpenSees exited with an error". OpenSees itself exits 0 either way;
  `Model.RunOpenSees` reads the error off the log - see [DeckErrors](../DeckErrors/README.md).

## What runs where

**run.sh** compiles `TenNodeTetrahedron.cs` with mono. With an OpenSees that knows the element
(`OPENSEES_TET10`, default /Applications/OpenSeesLatest/bin/OpenSees) it first solves:

| deck | what it pins down |
|---|---|
| `patch.tcl` | every node on u = 1e-3 x: sigma = (1.2, 0.4, 0.4, 0, 0, 0) at every point and node, and the reactions of the whole volume |
| `bending.tcl` | every node on pure bending, which the element carries exactly: sigma11 = -E kappa z. Reading the four integration points back to the ten nodes gives -E kappa z at each node only if point k really is the one by corner k |
| `bodyforce.tcl` | a body force handed to the element: the reaction at each node is its share, -1/20 at a corner and 1/5 at an edge node, which is what Gravity Load now puts on the nodes |
| `cantilever.py` | a cantilever of 60 cells' worth of tetrahedra: ten node ones within 0.2% of beam theory, four node ones at 24% |

and the checks that need no solver: the edge order, the shares, the extrapolation, the frame of ten
nodes being the frame of the corners, and the message for an OpenSees that lacks the element - on its
real log when the OpenSees on PATH is one.

**grasshopper.sh** goes through the components: a cantilever 8 x 1 x 1 of 48 tetrahedra, as four
vertex meshes some of which are wound inside out, under its own weight - nD Elastic Isotropic, Ten Node
Tetrahedron, Support, Gravity Load, Plain Pattern, Assemble Model, Analysis Settings, Run Analysis,
Natural Vibration, Nodal Displacements, Reaction Forces, Brick Stresses and View Results' field read by
reflection. Tip deflection within 0.4% of beam theory with shear, reactions equal to the weight, the
root in tension on top, ten values per element for View Results, the first mode just under
Euler-Bernoulli's, and the deck read back by `TclReader` - as written and in plain TenNodeTetrahedron's
order - with every node where it was. Held at its mesh vertices only, Assemble Model warns about the
five edge nodes left free between them; run on 3.8.0, Run Analysis stops and says why. The checks point the plugin's user
folder at the test folder, so the user's settings are left alone; that folder has no license, which
keeps the model under 50 elements and Model View and View Results' drawing out.

## Choices made

* **Gravity** puts the consistent shares on the nodes, -1/20 of an element's weight on each corner and
  1/5 on each edge node: the load the element itself forms from a body force. A tenth on each node
  would load it the way no weight does. Corners being pushed up is invisible: gravity's nodal loads
  are written to the deck, not drawn.
* **Brick Stresses** gives one value per element, the mean of the four points, which is the stress at
  the centre. **View Results** paints the element's own linear field: the four points read back to
  the four corners exactly (`TenNodeTetrahedron.AtNodes`), an edge node the mean of its two.
* **The mesh** of the element carries all ten nodes as vertices, each face cut into four triangles at
  its edge nodes, so the deformed shape bends at the edge nodes and Model View shows them as lines.
* **Supports** hold the node at their point and nothing else. A face supported at its mesh vertices
  leaves its edge nodes free; Assemble Model warns, and the component's Points output has every node.
