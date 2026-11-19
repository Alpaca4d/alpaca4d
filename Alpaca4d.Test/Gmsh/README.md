# gmsh meshers

    ./run.sh                                     # mono, the gmsh on this machine
    RHINO_PIPE=rhinocode_remotepipe_<pid> ./grasshopper.sh
    RHINO_PIPE=rhinocode_remotepipe_<pid> LICENSE=/path/to/license.bin ./example.sh
    PICTURES=1 ... ./example.sh                  # the examples' pictures too, into the work folder it prints

Checks `Alpaca.Core/Meshing` - Mesh Solid, Mesh Surface and Mesh Refinement - which run
[gmsh](https://gmsh.info) as a program of its own. gmsh is the one Homebrew installs (`brew install
gmsh`), or `GMSH=/path/to/gmsh`.

**run.sh**, under mono: the geometry is made by gmsh itself - boxes and plates written to STEP and
STL - and the questions the meshers ask Rhino are answered from the boxes' planes. It checks:

- the `.msh` reader, and finding gmsh and its version;
- a box: the tetrahedra fill it, near the size asked, none badly shaped; Face Nodes has a branch per
  face, each with the face's corner nodes and the middles of the tetrahedra's edges on it;
- a column stub on a footing: one mesh, each keeping its own size, sharing every node of the
  interface; two overlapping boxes: the overlap goes to the first;
- inclusion points inside, on a face and on an edge: each is a node, exactly;
- a refinement at a corner: small elements near it, back to size beyond its reach;
- a Brep that comes out of STEP as faces, closed into a volume;
- a closed STL: remeshed at the size asked, cut into patches at its sharp edges, a point inside held
  and one on its surface pulled onto a node;
- a plate with two holes and a plate beside it, quads: covering them, a node at each point, a line of
  nodes along an inclusion line and round an inclusion circle, every element turned the way the
  surface faces, the two plates joined; the same plate in triangles, turned over;
- a plate refined along a hole's edge; a closed box as shells under a slab, joined along their lines;
- Escape, as the stop function: gmsh is killed at once.

**grasshopper.sh**, in a running Rhino 8, through the components themselves: Breps and meshes written
by Rhino and read back at their own size, gmsh's faces matched to Rhino's, a cylinder's round face,
a closed Rhino mesh, the tetrahedra taken by Ten Node Tetrahedron with every Face Node among its
nodes, a plate with holes, points, a line and a refinement, the same plate turned over as triangles,
a free-form NURBS surface with two holes cut along projected curves, points on it, an isocurve and
a projected circle to follow and its hole edges refined (`PICTURES=1` draws it), a closed box as
shells, and what Mesh Solid refuses.

**example.sh** builds the two examples by their own `build(doc)` with this build, and solves them:
`Tools/Examples/Meshing/SlabWithOpenings.py` - quads, a node on each column, the reactions against
the area times the load - and `Tools/Examples/Solids/Femur.py` - its tetrahedra, analysis and modes.

Found while writing them (gmsh 4.15):

- gmsh's Netgen optimiser ends gmsh with a bus error when a point is held inside a volume. It is left
  off; HXT, the 3D algorithm, shapes the tetrahedra well without it (worst 0.38 on the femur, against
  0.06 with plain Delaunay).
- `Geometry.OCCTargetUnit` acts on import only: set before `ShapeFromFile`. The STEP files are written
  and read in millimetres, so the numbers come through as they are.
- Rhino writes a free line to STEP as an unbounded `LINE`, which gmsh meshes until it runs out of
  memory. Inclusion curves go to gmsh as lines and splines instead.
- `BrepFace.NormalAt` already turns the normal over for a reversed face.
- gmsh pairs triangles into quads down to a quality of 0.01 (`Mesh.RecombineMinimumQuality`): beside a
  curve on a free-form surface that let a quad with a corner of almost 180 degrees through (0.007 by
  Alpaca's measure). At 0.2, with `Mesh.Smoothing = 5`, the worst there is 0.42.
- A hole Rhino cuts in a free-form surface has its edge within the model tolerance of the surface, not
  on it: the nodes along it are up to 0.13 mm off the NURBS surface at 1 mm tolerance.
- In a Rhino runner, load the DLLs before the `.gha`: Grasshopper looks into a `.gha` as soon as it is
  loaded, and one loaded before its Core binds to the installed one.
