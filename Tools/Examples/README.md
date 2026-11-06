# Examples

    ./make.sh                                   # FootbridgeStudy.gh, next to its script
    ./make.sh path/to/Script.py [out.gh]         # any script with a build(doc), next to it
    ./make.sh SteelCantilever.py                # the Utilisation example
    ./make.sh ColumnDomain.py                   # the Interaction Domain example
    ./make.sh CurvedBeamOptimiser.py            # the Section Optimiser example
    ./make.sh BeamGridOptimiser.py              # the beam grid, supports from corners to centre
    ./make.sh TwoSpanBeamOptimiser.py           # a 30 m curved beam over two spans, sized from HEAs
    ./make.sh TwistedRing.py                    # a twisted ring truss's geometry, in a Python component
    ./make.sh BrickSolverSpeed.py               # the solver speed test, a block of bricks
    ./make.sh TopologyBridge.py                 # the Topology Optimisation example
    ./make.sh TopologyBenchmarks.py             # Topology Optimisation's two benchmarks, against top3d
    ./make.sh MultiMaterialBridge.py            # the Multi-Material Topology example
    ./make.sh BesoBridge.py                     # the BESO Topology example
    NODE_CASE=truss ./make.sh TopologyNodes.py TopologyTrussNode.gh          # a space-truss node, three ways
    NODE_CASE=joint ./make.sh TopologyNodes.py TopologyBeamColumnJoint.gh    # a beam-column joint, three ways
    ./make.sh SpotTheCow.py                     # tetrahedra: a concrete cow under its own weight
    ./make.sh Femur.py                          # tetrahedra: a femur under the force of the hip
    ./make.sh InigoTheDragon.py                 # tetrahedra: a concrete dragon's modes
    ./make.sh PlaygroundRiver.py                # the Playground's first lesson, a plank over a river
    RIVER_START=1 ./make.sh PlaygroundRiver.py PlaygroundRiverStart.gh   # its start, for the docs lesson
    ./make.sh PlaygroundCliff.py                # the Playground's second lesson, a balcony over a cliff
    START=1 ./make.sh PlaygroundCliff.py PlaygroundCliffStart.gh   # its start, for the docs lesson
    ./make.sh PlaygroundRoof.py                 # the Playground's third lesson, a rafter under snow
    START=1 ./make.sh PlaygroundRoof.py PlaygroundRoofStart.gh   # its start, for the docs lesson
    ./make.sh PlaygroundBarn.py                 # the Playground's fourth lesson, a portal frame in the wind
    START=1 ./make.sh PlaygroundBarn.py PlaygroundBarnStart.gh   # its start, for the docs lesson
    ./make.sh PlaygroundValley.py               # the Playground's fifth lesson, a truss bridge over the gorge
    START=1 ./make.sh PlaygroundValley.py PlaygroundValleyStart.gh   # its start, for the docs lesson
    ./make.sh PlaygroundMarket.py               # the Playground's sixth lesson, a market stall's shelf
    START=1 ./make.sh PlaygroundMarket.py PlaygroundMarketStart.gh   # its start, for the docs lesson
    ./make.sh PlaygroundTower.py                # the Playground's Watchtower, a steel leg that must not buckle
    START=1 ./make.sh PlaygroundTower.py PlaygroundTowerStart.gh   # its start, for the docs lesson
    ./make.sh PlaygroundFootbridge.py                # the Playground's eighth lesson, a footbridge that bounces
    START=1 ./make.sh PlaygroundFootbridge.py PlaygroundFootbridgeStart.gh   # its start, for the docs lesson
    ./make.sh PlaygroundWall.py                 # the Playground's ninth lesson, a wall with a window
    START=1 ./make.sh PlaygroundWall.py PlaygroundWallStart.gh   # its start, for the docs lesson
    ./make.sh PlaygroundSummit.py               # the Playground's last lesson, a concrete bridge carved by Topology Optimisation
    START=1 ./make.sh PlaygroundSummit.py PlaygroundSummitStart.gh   # its start, for the docs lesson
    STRUCTURE=hall ./make.sh Structures.py TrussRoofHall.gh                  # six conventional structures,
    STRUCTURE=tower ./make.sh Structures.py SteelTower.gh                    #   one definition each
    STRUCTURE=canopy ./make.sh Structures.py RoofCanopy.gh
    STRUCTURE=facade ./make.sh Structures.py VerticalTrussFacade.gh
    STRUCTURE=vault ./make.sh Structures.py BarrelVault.gh
    STRUCTURE=windtruss ./make.sh Structures.py HorizontalTrussFacade.gh
    STRUCTURE=girder ./make.sh Structures.py GirderBridgeEarthquake.gh       # five structures shaken by
    STRUCTURE=cablestayed ./make.sh Structures.py CableStayedBridgeEarthquake.gh   #   El Centro 1940
    STRUCTURE=arch ./make.sh Structures.py ArchBridgeEarthquake.gh
    STRUCTURE=building ./make.sh Structures.py BuildingEarthquake.gh
    STRUCTURE=city ./make.sh Structures.py EarthquakeCity.gh

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
| `TwoSpanBeamOptimiser.py` | A Section Optimiser example: a curve about 30 m long in a vertical plane, shattered at a point Evaluate Curve places along it, each segment divided into elements of about 25 cm. Built in at both ends and pinned between the segments, HEA300 in S235 to start, under its own weight and a point load a slider moves along the curve to the closest node. One design group sizes it from a panel of HEAs, element by element as it opens (its Uniform toggle gives the whole beam one), with the HEA named where it changes and a second Model View drawing the deformed shape. |
| `TwistedRing.py` | The geometry of a twisted ring truss, for an exercise on the Section Optimiser and multi-objective design: fourteen sliders and a list - the ring's radii, wave and swell, the profile and its twist, the slices and the bracing (Diagonal, Zigzag, Checkerboard, Cross or K) - into one GhPython component, whose code is `TwistedRingComponent.py`. It gives the chords, the frames and the bracing, each joined into polylines (explode them before making elements), and the nodes, the member and node counts and the total length. GhPython opens in Rhino 7 and 8, and the code runs in Rhino 8's Python 3 component too; the supports, loads and Alpaca4d model are left to the student. |
| `Structures.py` | Six conventional structures, a definition each, their geometry from sliders in a GhPython component (`StructuresGeometry.py`): a steel hall roofed by Pratt trusses under snow, a steel tower with a stair under wind and its modes, a canopy of T-shaped frames under snow lying deeper on one side, a glass wall stiffened by vertical trusses, a concrete barrel vault under wind along each strip's normal, and a glass facade held by horizontal wind trusses. Loads typed in kN/m², times each member's width, as line loads. And five shaken by El Centro 1940 in a transient analysis - a girder bridge (a slab on two girders, piers of two columns), a cable-stayed bridge, an arch bridge of two braced ribs leaning in, an office building, and a tiny city of sixteen buildings from two storeys to twenty, rectangles, Ls, a tower on a podium, penthouses and balconies (`CITY` lists them) - the record in a GhPython component, a Uniform Excitation pattern, Newmark, Rayleigh damping and a Step slider; their views draw the edges and the supports, and their geometry's `Watch` output marks the points Tools/Marketing's charts follow. The website's examples of the same names. |
| `MixedModelSpeed.py` | A speed test of Assemble Model: a shell slab on steel beams and columns, cut at the slab's nodes, on a brick raft, with point, line, mesh and gravity loads in one pattern. One slider takes it from a hundred elements to forty thousand. |
| `BrickSolverSpeed.py` | A speed test of the solver: an 8 x 8 x 6 m concrete block of SSP bricks, pinned along its bottom, with four column loads and its own weight. One slider takes it from 1 500 unknowns to 151 000, and a System list solves it with Mumps on several cores or with BandSPD or UmfPack on one (`Tools/OpenSees`). |
| `TopologyBridge.py` | The Topology Optimisation example: a 16 x 2 x 4 m steel block of SSP Bricks pinned along both ends of its bottom, its top layer a deck kept solid through Keep under 10 kN/m2. With a fifth of the rest it finds an arch rising into the deck, with a column near each end; Bricks per metre takes it from 128 bricks to 8 192. |
| `StressLinesBenchmarks.py` | Principal Stress Lines against three closed-form solutions, each solved by OpenSees: Timoshenko and Goodier's cantilever loaded by its exact end tractions, Navier's simply supported slab (bending lines, with a toggle that turns a third of its shells over) and Kirsch's plate with a hole. Drawn side by side in Rhino's Top view: single lines through six seeds with the exact trajectories dashed over them, and fields of lines. Custom Preview cannot be made outside Rhino, so make.sh cannot write this one: run the script in Rhino and save what it opens. `Alpaca4d.Test/PrincipalStressLines` measures the same lines. |
| `TopologyBenchmarks.py` | Topology Optimisation's two benchmarks in top3d's units and settings, each with its own Run toggle: top3d's 60 x 4 x 20 cantilever and a 120 x 4 x 20 simply supported beam, with panels giving the compliance the test bench and top3d reach after 200 iterations. `Alpaca4d.Test/TopologyOptimisation/example.sh` builds and runs it in Rhino. |
| `MultiMaterialBridge.py` | The Multi-Material Topology example: TopologyBridge's block in steel (E = 210 GPa) and concrete (E = 33 GPa), at most 5% and 15% of the block below the deck. The concrete goes into two legs from the supports to the deck, the steel into an arch slung under it: 265 iterations, under a minute at 3 bricks per metre. `Alpaca4d.Test/TopologyMethods/example.sh` builds and runs it in Rhino. |
| `BesoBridge.py` | The BESO Topology example: TopologyBridge's block carved down to a fifth of its volume below the deck, every brick solid or void, Step going through every volume on the way: two raking legs into the deck with a column near each end, 105 iterations, under 20 s. Built and run by the same `example.sh`. |
| `TopologyNodes.py` | A node where members meet, in space, found by all three topology optimisations side by side: a 1 m cube of bricks with member stubs kept solid, loaded or held at their ends. `NODE_CASE=truss` writes `TopologyTrussNode.gh`, nine members of a space truss (chords pulled and pushed, a post loaded, four diagonals held); `NODE_CASE=joint` writes `TopologyBeamColumnJoint.gh`, a column and four beams with their shears and unbalanced hogging moments. `Alpaca4d.Test/TopologyMethods/nodes.sh` builds and runs both in Rhino. |
| `SpotTheCow.py` | A tetrahedron example: Spot the cow, a solid concrete cow 2.4 m long standing on its hooves under its own weight, in 5 030 tetrahedra, with the vertical stress drawn over it. |
| `Femur.py` | A tetrahedron example on a bone: the Standardised Femur, held at the knee with the force of the hip on its head, in 3 813 tetrahedra, drawn deformed. |
| `InigoTheDragon.py` | The natural vibration example on tetrahedra: Inigo the dragon, 5.5 m of concrete on its two feet, in 10 903 tetrahedra. A slider picks which of its first 12 modes Model View draws; it opens on both wings flapping together. |
| `PlaygroundRiver.py` | The River, the first lesson of the website's Playground and of the docs' Learn: a C24 plank 300 x 50 mm across 4 m, a pin and a roller, the alpaca's 0.6 kN at mid-span, drawn deformed - 24.4 mm, as by hand. One group per round of the lesson. The Playground draws its snippets from it (`assets/site/playground/river.gh` and the `river-definition.json` Tools/GhCanvas reads from it, in the website) and hands it out at the end; the docs lesson has it as `doc/learn/01-river/river.gh`, and `RIVER_START=1` writes `river-start.gh`, the line and the section only. Copy it to all three places when it changes. |
| `PlaygroundCliff.py` | The Cliff, the second lesson of the website's Playground and of the docs' Learn: the River's C24 plank 300 x 50 mm built into a cliff and sticking out 4 m, the alpaca's 0.6 kN at its tip, drawn deformed true to scale and read with Nodal Displacements - 390 mm, 16 times the River's. One group per round of the lesson. The Playground draws its snippets from it (`assets/site/playground/cliff.gh` and the `cliff-definition.json` Tools/GhCanvas reads from it, in the website) and hands it out at the end; the docs lesson has it as `doc/learn/02-cliff/cliff.gh`, and `START=1` writes `cliff-start.gh`, the balcony only. Copy it to all three places when it changes. |
| `PlaygroundRoof.py` | The Snowy roof, the third lesson of the website's Playground and of the docs' Learn: a C24 rafter of a flat roof, 75 mm wide, 4 m on a pin and a roller, under 1.5 kN/m² of snow at 0.6 m spacing (0.9 kN/m) and its own weight from Gravity Load, in one Load Pattern; 150 mm deep it sags 14.3 mm, under L/250 = 16 mm, as by hand. A "The rafter" group as the River left off, then one group per round of the lesson. The Playground draws its snippets from it (`assets/site/playground/roof.gh` and the `roof-definition.json` Tools/GhCanvas reads from it) and hands it out at the end; the docs lesson has it as `doc/learn/03-snowy-roof/roof.gh`, and `START=1` writes `roof-start.gh`, the rafter and its section only, 100 mm deep. Copy it to all three places when it changes. |
| `PlaygroundBarn.py` | The Barn, the fourth lesson of the website's Playground and of the docs' Learn: a C24 portal frame, posts 4 m tall set in concrete, a 6 m beam bolted across them (WithHinges, a Hinge Release freeing Rz at both knees), 200 x 300 mm, 3 kN of wind at the top of a post - 6.8 mm of sway, as by hand. One group per round of the lesson. The Playground draws its snippets from it (`assets/site/playground/barn.gh` and the `barn-definition.json` Tools/GhCanvas reads from it, in the website) and hands it out at the end; the docs lesson has it as `doc/learn/04-barn/barn.gh`, and `START=1` writes `barn-start.gh`, the frame's lines and the section only. Copy it to all three places when it changes. |
| `PlaygroundValley.py` | The Valley, the fifth lesson of the website's Playground and of the docs' Learn: a Warren truss 12 m long in six 2 m panels, 2 m deep, of S355 tubes 100 x 5 mm as Truss bars, a pin, a roller and every other joint held sideways (Ty), the alpaca's 0.6 kN on the deck at mid-span, drawn with N - 0.90 kN in the top chord, 0.75 kN in the bottom chord, 0.335 kN in the diagonals, as by hand. One group per round of the lesson. The Playground draws its snippets from it (`assets/site/playground/valley.gh` and the `valley-definition.json` Tools/GhCanvas reads from it, in the website) and hands it out at the end; the docs lesson has it as `doc/learn/05-valley/valley.gh`, and `START=1` writes `valley-start.gh`, the deck and the triangles only. Copy it to all three places when it changes. |
| `PlaygroundMarket.py` | The Market, the sixth lesson of the website's Playground and of the docs' Learn: the River's C24 plank, 4 m long, on a pin and a roller 3 m apart, a 1 kN crate between them and the alpaca's 0.6 kN on the metre that sticks out - reactions 0.3 and 1.3 kN, Mz +0.45 kNm under the crate and -0.6 kNm over trestle B. One group per round of the lesson. The Playground draws its snippets from it (`assets/site/playground/market.gh` and the `market-definition.json` Tools/GhCanvas reads from it, in the website) and hands it out at the end; the docs lesson has it as `doc/learn/06-market/market.gh`, and `START=1` writes `market-start.gh`, the stall alone, solved. Copy it to all three places when it changes. |
| `PlaygroundTower.py` | The Watchtower, lesson 7 of the website's Playground and of the docs' Learn: one leg of a steel watchtower, a CHS 60.3 x 3.2 tube in S235, 6 m tall, pinned at its foot and held sideways by the bracing at mid-height and the platform at its head, 8 kN on it; Linear Buckling with Divisions 8 gives 6.83, Euler 6.76 by hand. One group per round of the lesson. The Playground draws its snippets from it (`assets/site/playground/tower.gh` and its `tower-definition.json`, in the website) and hands it out at the end; the docs lesson has it as `doc/learn/07-watchtower/tower.gh`, and `START=1` writes `tower-start.gh`, the line and the tube only. Copy it to all three places when it changes. |
| `PlaygroundFootbridge.py` | The Footbridge, lesson 8 of the website's Playground and of the docs' Learn: a C24 deck 1.2 m x 120 mm across 10 m on a pin and a roller, a 60 kg alpaca on every node as a Mass Point, Natural Vibration - 1.94 Hz, as by hand - and a pier at mid-span that makes it 7.75 Hz. One group per round of the lesson. The Playground draws its snippets from it (`assets/site/playground/footbridge.gh` and the `footbridge-definition.json` Tools/GhCanvas reads from it, in the website) and hands it out at the end; the docs lesson has it as `doc/learn/08-footbridge/footbridge.gh`, and `START=1` writes `footbridge-start.gh`, the deck, span and abutments only. Copy it to all three places when it changes. |
| `PlaygroundWall.py` | The Wall with a window, lesson 9 of the website's Playground and of the docs' Learn: a concrete wall 5.5 x 4 m, 200 mm thick, with a window 1.5 m square, on its foundation, with a floor of 50 kN on each of 12 points along its top - 0.75 MPa beside the window, as by hand - and its principal stress lines. One group per round of the lesson. The Playground draws its snippets from it (`assets/site/playground/wall.gh` and the `wall-definition.json` Tools/GhCanvas reads from it) and hands it out at the end; the docs lesson has it as `doc/learn/09-wall-with-a-window/wall.gh`, and `START=1` writes `wall-start.gh`, the wall meshed into shells only. Mesh Brep needs Rhino to solve it. Copy it to all three places when it changes. |
| `PlaygroundSummit.py` | The Summit bridge, the last lesson of the website's Playground and of the docs' Learn: a C30/37 block 12 x 4 x 1.2 m in 30 x 10 bricks across the chasm, pinned at both ends of its bottom, 5 kN/m2 on its deck, which Keep holds solid. Topology Optimisation keeps a quarter of the rest and finds an arch with a hanger near each quarter: 40 iterations, about a second, 300 bricks, under the 500 it takes without a license. One group per round of the lesson, and an eighth, disabled, that analyses the design by Von Mises (Run Analysis needs a license past 50 elements). The Playground draws its snippets from it (`assets/site/playground/summit.gh` and the `summit-definition.json` Tools/GhCanvas reads from it, in the website) and hands it out at the end; the docs lesson has it as `doc/learn/10-summit-bridge/summit.gh`, and `START=1` writes `summit-start.gh`: the block, the rocks and the walkway. Copy it to all three places when it changes. |
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

Six examples are published from the website repository: on the website's Examples, as
`examples/steel-cantilever`, `footbridge-study` and `non-linear-optimisation`; in the docs, under
Ten Node Tetrahedron, as `doc/basics/elements/ten-node-tetrahedron/spot-the-cow`, `femur` and
`inigo-the-dragon`. After writing a `.gh` again, copy it there under that name.
