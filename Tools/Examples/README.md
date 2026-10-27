# Examples

    ./make.sh                                   # FootbridgeStudy.gh, next to its script
    ./make.sh path/to/Script.py [out.gh]         # any script with a build(doc), next to it
    ./make.sh SteelCantilever.py                # the Utilisation example
    ./make.sh ColumnDomain.py                   # the Interaction Domain example
    ./make.sh CurvedBeamOptimiser.py            # the Section Optimiser example
    ./make.sh BeamGridOptimiser.py              # the beam grid, supports from corners to centre
    ./make.sh BrickSolverSpeed.py               # the solver speed test, a block of bricks
    ./make.sh TopologyBridge.py                 # the Topology Optimisation example
    ./make.sh TopologyBenchmarks.py             # Topology Optimisation's two benchmarks, against top3d
    ./make.sh SpotTheCow.py                     # tetrahedra: a concrete cow under its own weight
    ./make.sh Femur.py                          # tetrahedra: a femur under the force of the hip
    ./make.sh InigoTheDragon.py                 # tetrahedra: a concrete dragon's modes

Each example is a Python script that builds a Grasshopper definition from the real Grasshopper
and Alpaca4d components: it places them, wires them, names and groups them. `make.sh` writes it
straight to a `.gh` file, without Rhino. The same script also runs inside Rhino, where it opens
the definition on the canvas instead: in Rhino 8 open it in the ScriptEditor and press Run, in
Rhino 7 use `_RunPythonScript`.

| Example | Builds |
| --- | --- |
| `FootbridgeStudy.py` | The Explorer example: a steel truss footbridge analysed with Alpaca4d, and a design study of 108 designs over its span, depth, panels and chord section. |
| `SteelCantilever.py` | The Utilisation example: a 4 m steel cantilever checked to EN 1993-1-1, with the buckling lengths a cantilever needs and the Design menu open on the report. |
| `ColumnDomain.py` | The Interaction Domain example: a 400 x 600 reinforced concrete column's N-My-Mz domain, three combinations checked against it, and the domain drawn with Domain 3D, N-M Diagram and My-Mz Diagram beside the section. |
| `CurvedBeamOptimiser.py` | The Section Optimiser example: a 12 m beam curving in a vertical plane, built in at its start, held up at two more points and ending in a cantilever with a point load near its tip, in 30 elements, IPE300 to start. Sized element by element within the IPE family, and as one section for the whole beam from a design group. |
| `BeamGridOptimiser.py` | A Section Optimiser example: a 20 m square grid of beams on four pinned supports, with a node load everywhere and its own weight. A slider walks the supports from the corners towards the centre, from a slab spanning between them to one cantilevering out, and every element is sized on its own within its family. |
| `MixedModelSpeed.py` | A speed test of Assemble Model: a shell slab on steel beams and columns, cut at the slab's nodes, on a brick raft, with point, line, mesh and gravity loads in one pattern. One slider takes it from a hundred elements to forty thousand. |
| `BrickSolverSpeed.py` | A speed test of the solver: an 8 x 8 x 6 m concrete block of SSP bricks, pinned along its bottom, with four column loads and its own weight. One slider takes it from 1 500 unknowns to 151 000, and a System list solves it with Mumps on several cores or with BandSPD or UmfPack on one (`Tools/OpenSees`). |
| `TopologyBridge.py` | The Topology Optimisation example: a 16 x 2 x 4 m steel block of SSP Bricks pinned along both ends of its bottom, its top layer a deck kept solid through Keep under 10 kN/m2. With a fifth of the rest it finds an arch rising into the deck, with a column near each end; Bricks per metre takes it from 128 bricks to 8 192. |
| `TopologyBenchmarks.py` | Topology Optimisation's two benchmarks in top3d's units and settings, each with its own Run toggle: top3d's 60 x 4 x 20 cantilever and a 120 x 4 x 20 simply supported beam, with panels giving the compliance the test bench and top3d reach after 200 iterations. `Alpaca4d.Test/TopologyOptimisation/example.sh` builds and runs it in Rhino. |
| `SpotTheCow.py` | A tetrahedron example: Spot the cow, a solid concrete cow 2.4 m long standing on its hooves under its own weight, in 5 030 tetrahedra, with the vertical stress drawn over it. |
| `Femur.py` | A tetrahedron example on a bone: the Standardised Femur, held at the knee with the force of the hip on its head, in 3 813 tetrahedra, drawn deformed. |
| `InigoTheDragon.py` | The natural vibration example on tetrahedra: Inigo the dragon, 5.5 m of concrete on its two feet, in 10 903 tetrahedra. A slider picks which of its first 12 modes View Results draws; it opens on both wings flapping together. |
| `../../Alpaca4d.Test/Optimisation/NonLinearOptimisationCases.py` | The eight test cases of Non-Linear Optimisation, each with a known answer. |

A script works with make.sh when it makes its objects through `emit()`, which uses `make_object`
when it is set, and has a `build(doc)` that adds them to `doc` and runs nothing at import.

## Tetrahedra

Grasshopper has no tetrahedral mesher, so the tetrahedron examples read theirs from a `.txt` next
to them - the nodes, one `x y z` a line, then four node numbers per tetrahedron - and keep them in the
definition. A `*Mesh.py` script made each `.txt` with [gmsh](https://gmsh.info) (`brew install
gmsh`), from a model it downloads, through `TetMesh.py`:

| Script | Writes | From | Needs besides gmsh |
| --- | --- | --- | --- |
| `SpotTheCowMesh.py` | `SpotTheCow.txt` | Spot, Keenan Crane, CC0 | nothing |
| `FemurMesh.py` | `Femur.txt` | the Standardised Femur, Marco Viceconti, University of Sheffield, CC BY 4.0, [doi 10.15131/shef.data.3839766.v1](https://doi.org/10.15131/shef.data.3839766.v1) | numpy, scipy, scikit-image |
| `InigoTheDragonMesh.py` | `InigoTheDragon.txt` | Inigo the Dragon, Keenan Crane, CC0 | numpy, scipy, scikit-image, pymeshlab |

The cow fills its own surface. The femur's IGES does not close into a solid, and the dragon's wings
and fingers are thinner than an element, so those two are rebuilt on a grid first - a signed distance,
marching cubes, smoothed - and the dragon's wings thickened by 9 cm; each script's docstring says how.
Run one with Python 3, then `make.sh` on its example. The three solve with Ten Node Tetrahedron,
which needs an OpenSees built from the master branch since 16 October 2026.

## What it needs

* A Release build of `Alpaca4d.Gh`.
* mono with libgdiplus (`brew install mono`).
* Rhino 7 for Mac, for the IronPython it ships, the Visual Basic runtime Grasshopper needs and
  Grasshopper's component libraries - curve, maths, vector, transform, surface and mesh, and
  Galapagos. Point `R7` elsewhere if it is not in `/Applications`.
* Grasshopper and RhinoCommon 7.18 from the NuGet cache, which the build has already restored.

## How it works

Grasshopper's own document cannot be made outside Rhino: it needs Rhino's native core. Its
components can, and they can save themselves.

1. **GhHost.cs** runs IronPython in mono with Grasshopper, GH_IO and RhinoCommon loaded. Grasshopper
   refers to two Windows-only assemblies, its script editor and Eto's Windows platform; IronPython
   reads every type of an assembly it loads, so the host answers for those two with empty ones.
2. **WriteGh.py** makes every object the component libraries hold, to know each one's GUID, and
   hands the example script those objects and a document to collect them in.
3. The example's `build(doc)` makes, wires and lays out the definition as it does on the canvas.
   Its grid layout measures each object once Grasshopper has laid it out, so the sizes are the
   components' own.
4. **WriteGh.py** then saves the objects as Grasshopper saves a document: a header, the plug-ins
   used, and each object's own serialisation, framed so the file opens on the whole definition. It
   names any two objects that cover one another, as a check on the layout. Components with menus
   are left out of that check: mono measures their menus' text too wide, and Rhino lays them out
   again when it opens the file.

Alpaca4d's switcher components (ForceBeamColumn, Load Pattern) write a diagnostic to Rhino's
command line when they save, which is not there outside Rhino; WriteGh.py clears the parent the
diagnostic checks for, and the component sets it back as it saves.

Six examples are in the docs of the website repository: under Examples, as
`doc/examples/steel-cantilever`, `footbridge-study` and `non-linear-optimisation`; under Ten Node
Tetrahedron, as `doc/basics/elements/ten-node-tetrahedron/spot-the-cow`, `femur` and
`inigo-the-dragon`. After writing a `.gh` again, copy it there under that name.
