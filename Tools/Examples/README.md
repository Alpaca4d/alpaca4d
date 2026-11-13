# Examples

Each example is a Python script that builds a Grasshopper definition from the real Grasshopper
and Alpaca4d components: it places them, wires them, names and groups them. `make.sh` writes it
straight to a `.gh` file next to the script, without Rhino. The same script also runs inside Rhino,
where it opens the definition on the canvas instead: in Rhino 8 open it in the ScriptEditor and
press Run, in Rhino 7 use `_RunPythonScript`.

The examples are in a folder each by topic; `make.sh` and what it needs (`GhHost.cs`, `WriteGh.py`)
are here at the top.

| Folder | What is in it |
| --- | --- |
| `Playground/` | The ten lessons of the website's Playground and the docs' Learn, each with its start (`*Start.gh`) |
| `Structures/` | `Structures.py`'s definitions - the conventional structures, the earthquakes, the footbridge's and the tank's modes, the response spectrum sticks, the compressed shell, the pushover building - and the footfall stairs, the reciprocal vault, the high-tension tower and the multi-storey building, which use its helpers |
| `Design/` | Section and member design: the Utilisation, Interaction Domain and Moment Curvature examples, and the tree column's cast node in shells (Connection Design) |
| `Optimisation/` | The Section Optimiser examples, the Explorer's footbridge study and the twisted ring |
| `Topology/` | Topology Optimisation, Multi-Material Topology and BESO, and the nodes in space |
| `Solids/` | Ten node tetrahedra - the cow, the femur, the Rebellious Slave - and the scripts that mesh them; a vault of bricks from Mesh Loft and Mesh Series to Brick |
| `Plasticity/` | Steel that yields, J2Plasticity: the beam with holes and four more |
| `Printing/` | 3D printing in fresh concrete: the cylinder and the shapes printed along their sections |
| `Scripting/` | Custom Code and Deconstruct: the Custom Script and Deconstruct Anything examples |
| `Benchmarks/` | The speed tests of Assemble and the solver, and the principal stress lines against closed forms |
| `Report/` | The Report test, and five example reports to share |

Run `make.sh` from here, with the script's folder. An `out.gh` given as a bare name goes next to the
script too:

    ./make.sh                                   # Optimisation/FootbridgeStudy.gh, next to its script
    ./make.sh Folder/Script.py [out.gh]         # any script with a build(doc), next to it
    ./make.sh ../../Alpaca4d.Test/Optimisation/NonLinearOptimisationCases.py

    # Playground/
    ./make.sh Playground/PlaygroundRiver.py     # the Playground's first lesson, a plank over a river
    RIVER_START=1 ./make.sh Playground/PlaygroundRiver.py PlaygroundRiverStart.gh   # its start, for the docs lesson
    ./make.sh Playground/PlaygroundCliff.py     # the Playground's second lesson, a balcony over a cliff
    START=1 ./make.sh Playground/PlaygroundCliff.py PlaygroundCliffStart.gh   # its start, for the docs lesson
    ./make.sh Playground/PlaygroundRoof.py      # the Playground's third lesson, a rafter under snow
    START=1 ./make.sh Playground/PlaygroundRoof.py PlaygroundRoofStart.gh   # its start, for the docs lesson
    ./make.sh Playground/PlaygroundBarn.py      # the Playground's fourth lesson, a portal frame in the wind
    START=1 ./make.sh Playground/PlaygroundBarn.py PlaygroundBarnStart.gh   # its start, for the docs lesson
    ./make.sh Playground/PlaygroundValley.py    # the Playground's fifth lesson, a truss bridge over the gorge
    START=1 ./make.sh Playground/PlaygroundValley.py PlaygroundValleyStart.gh   # its start, for the docs lesson
    ./make.sh Playground/PlaygroundMarket.py    # the Playground's sixth lesson, a market stall's shelf
    START=1 ./make.sh Playground/PlaygroundMarket.py PlaygroundMarketStart.gh   # its start, for the docs lesson
    ./make.sh Playground/PlaygroundTower.py     # the Playground's Watchtower, a steel leg that must not buckle
    START=1 ./make.sh Playground/PlaygroundTower.py PlaygroundTowerStart.gh   # its start, for the docs lesson
    ./make.sh Playground/PlaygroundFootbridge.py  # the Playground's eighth lesson, a footbridge that bounces
    START=1 ./make.sh Playground/PlaygroundFootbridge.py PlaygroundFootbridgeStart.gh   # its start, for the docs lesson
    ./make.sh Playground/PlaygroundWall.py      # the Playground's ninth lesson, a wall with a window
    START=1 ./make.sh Playground/PlaygroundWall.py PlaygroundWallStart.gh   # its start, for the docs lesson
    ./make.sh Playground/PlaygroundSummit.py    # the Playground's last lesson, a concrete bridge carved by Topology Optimisation
    START=1 ./make.sh Playground/PlaygroundSummit.py PlaygroundSummitStart.gh   # its start, for the docs lesson

    # Structures/
    STRUCTURE=hall ./make.sh Structures/Structures.py TrussRoofHall.gh       # six conventional structures,
    STRUCTURE=tower ./make.sh Structures/Structures.py SteelTower.gh         #   one definition each
    STRUCTURE=canopy ./make.sh Structures/Structures.py RoofCanopy.gh
    STRUCTURE=facade ./make.sh Structures/Structures.py VerticalTrussFacade.gh
    STRUCTURE=vault ./make.sh Structures/Structures.py BarrelVault.gh
    STRUCTURE=windtruss ./make.sh Structures/Structures.py HorizontalTrussFacade.gh
    STRUCTURE=girder ./make.sh Structures/Structures.py GirderBridgeEarthquake.gh       # five structures shaken by
    STRUCTURE=cablestayed ./make.sh Structures/Structures.py CableStayedBridgeEarthquake.gh   #   El Centro 1940
    STRUCTURE=arch ./make.sh Structures/Structures.py ArchBridgeEarthquake.gh
    STRUCTURE=building ./make.sh Structures/Structures.py BuildingEarthquake.gh
    STRUCTURE=city ./make.sh Structures/Structures.py EarthquakeCity.gh
    STRUCTURE=footbridge ./make.sh Structures/Structures.py FootbridgeModes.gh          # a footbridge's natural modes
    STRUCTURE=sticks ./make.sh Structures/Structures.py ResponseSpectrumSticks.gh       # thirty sticks draw El Centro's spectrum
    STRUCTURE=shell ./make.sh Structures/Structures.py CompressedShell.gh    # a shell form-found by Kangaroo
    STRUCTURE=tank ./make.sh Structures/Structures.py TankModes.gh           # a steel tank's first hundred modes
    STRUCTURE=twisting ./make.sh Structures/Structures.py TwistingTowerEarthquake.gh      # four buildings that are not
    STRUCTURE=leaning ./make.sh Structures/Structures.py LeaningTowersEarthquake.gh       #   straight, shaken by
    STRUCTURE=curving ./make.sh Structures/Structures.py CurvingTowerEarthquake.gh        #   El Centro 1940
    STRUCTURE=crescent ./make.sh Structures/Structures.py CrescentEarthquake.gh
    STRUCTURE=bundled ./make.sh Structures/Structures.py BundledTubeEarthquake.gh         # five towers of 100 storeys
    STRUCTURE=braced ./make.sh Structures/Structures.py BracedTubeEarthquake.gh           #   and more, each after a
    STRUCTURE=buttressed ./make.sh Structures/Structures.py ButtressedCoreEarthquake.gh   #   famous one's structure
    STRUCTURE=outrigger ./make.sh Structures/Structures.py OutriggerTowerEarthquake.gh
    STRUCTURE=diagrid ./make.sh Structures/Structures.py DiagridTowerEarthquake.gh
    STRUCTURE=pushover ./make.sh Structures/Structures.py PushoverBuilding.gh         # an irregular concrete building pushed over
    ./make.sh Structures/ReciprocalVault.py     # a reciprocal vault of plywood planks pressed down until it gives way
    ./make.sh Structures/HighTensionTower.py    # a steel lattice pylon under its wires and the wind
    ./make.sh Structures/MultiStoreyBuilding.py # a concrete frame of five storeys with slabs, and its modes
    ./make.sh Structures/FootfallStair.py       # footfall on a U-shaped steel stair with two landings (SCI P354)
    STAIR=helical ./make.sh Structures/FootfallStair.py FootfallHelicalStair.gh     # a helical stair, all shells, curved parapets
    STAIR=straight ./make.sh Structures/FootfallStair.py FootfallStraightStair.gh   # 36 steps in a line, held only at its ends

    # Design/
    ./make.sh Design/SteelCantilever.py         # the Utilisation example
    ./make.sh Design/ColumnDomain.py            # the Interaction Domain example
    ./make.sh Design/MomentCurvature.py         # the Moment Curvature example
    ./make.sh Design/ConnectionDesign.py        # a tree column's cast steel node in shells

    # Optimisation/
    ./make.sh Optimisation/FootbridgeStudy.py         # the Explorer example, as ./make.sh alone - but see FootbridgeStudy.py below
    ./make.sh Optimisation/CurvedBeamOptimiser.py     # the Section Optimiser example
    ./make.sh Optimisation/BeamGridOptimiser.py       # the beam grid, supports from corners to centre
    ./make.sh Optimisation/TwoSpanBeamOptimiser.py    # a 30 m curved beam over two spans, sized from HEAs
    ./make.sh Optimisation/TwistedRing.py             # a twisted ring truss's geometry, in a Python component

    # Topology/
    ./make.sh Topology/TopologyBridge.py        # the Topology Optimisation example
    ./make.sh Topology/TopologyBenchmarks.py    # Topology Optimisation's two benchmarks, against top3d
    ./make.sh Topology/MultiMaterialBridge.py   # the Multi-Material Topology example
    ./make.sh Topology/BesoBridge.py            # the BESO Topology example
    NODE_CASE=truss ./make.sh Topology/TopologyNodes.py TopologyTrussNode.gh          # a space-truss node, three ways
    NODE_CASE=joint ./make.sh Topology/TopologyNodes.py TopologyBeamColumnJoint.gh    # a beam-column joint, three ways

    # Solids/
    ./make.sh Solids/SpotTheCow.py              # tetrahedra: a concrete cow under its own weight
    ./make.sh Solids/Femur.py                   # tetrahedra: a femur under the force of the hip - but see Femur.py below
    ./make.sh Solids/MeshLoftBricks.py          # a vault of bricks from polylines: Mesh Loft and Mesh Series to Brick
    ./make.sh Solids/RebelliousSlave.py         # tetrahedra: Michelangelo's Rebellious Slave, its weight and modes

    # Plasticity/
    # PerforatedBeam.py runs in Rhino 8 only (its curve's colours come from Custom Preview); SAVE=<path>
    #   saves it: PerforatedBeam.gh, a steel beam with holes pushed past first yield (J2Plasticity)
    # Plasticity.py runs in Rhino 8 only too; CASE=tjoint|yieldlines|platehole|pipe and SAVE=<path>:
    #   PlasticityTJoint.gh, PlasticityYieldLines.gh, PlasticityPlateHole.gh, PlasticityPipe.gh
    #   (CASE=... NO_PREVIEW=1 ./make.sh Plasticity/Plasticity.py out.gh writes one without its Custom Previews, to check)

    # Printing/
    ./make.sh Printing/PrintedCylinder.py       # a 3D printed cylinder of fresh concrete, buckling as it is printed
    # PrintedStructure.py runs in Rhino 8 only; SHAPE=branching|twisted|wall|dome|leaning picks the shape and
    #   SAVE=<path> saves it: PrintedStructure.gh, PrintedTwistedColumn.gh, PrintedWavyWall.gh, PrintedDome.gh,
    #   PrintedLeaningColumn.gh

    # Scripting/
    ./make.sh Scripting/CustomScript.py         # an OpenSees Tcl script run from Grasshopper, its output read back
    ./make.sh Scripting/DeconstructAnything.py  # Deconstruct opening a beam, its section, a load and the model

    # Benchmarks/
    ./make.sh Benchmarks/BrickSolverSpeed.py    # the solver speed test, a block of bricks
    ./make.sh Benchmarks/MixedModelSpeed.py     # the speed test of Assemble Model
    # StressLinesBenchmarks.py runs in Rhino only (Custom Preview): run it there and save what it opens

    # Report/
    ./make.sh Report/ReportFrame.py             # a steel frame written up as a PDF by the Report component
    CASE=beams ./make.sh Report/ReportCases.py ReportBeams.gh     # five example reports, one of each kind of
    CASE=shells ./make.sh Report/ReportCases.py ReportShells.gh   # element: beams, shells, bricks, beams and
    CASE=bricks ./make.sh Report/ReportCases.py ReportBricks.gh   # shells, and a stair of beams and shells
    CASE=mixed ./make.sh Report/ReportCases.py ReportMixed.gh
    CASE=stair ./make.sh Report/ReportCases.py ReportStair.gh

## What each one builds

### Playground/

| Example | Builds |
| --- | --- |
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

### Structures/

| Example | Builds |
| --- | --- |
| `Structures.py` | Six conventional structures, a definition each, their geometry from sliders in a GhPython component (`StructuresGeometry.py`): a steel hall roofed by Pratt trusses under snow, a steel tower with a stair under wind and its modes, a canopy of T-shaped frames under snow lying deeper on one side, a glass wall stiffened by vertical trusses, a concrete barrel vault under wind along each strip's normal, and a glass facade held by horizontal wind trusses. Loads typed in kN/m², times each member's width, as line loads. And five shaken by El Centro 1940 in a transient analysis - a girder bridge (a slab on two girders, piers of two columns), a cable-stayed bridge, an arch bridge of two braced ribs leaning in, an office building, and a tiny city of sixteen buildings from two storeys to twenty, rectangles, Ls, a tower on a podium, penthouses and balconies (`CITY` lists them) - the record in a GhPython component, a Uniform Excitation pattern, Newmark, Rayleigh damping and a Step slider; their views draw the edges and the supports, and their geometry's `Watch` output marks the points Tools/Marketing's charts follow. A footbridge's natural modes (`footbridge`): a concrete deck on a steel box girder, Box depth a slider, and its first six modes in iso-surfaces, for the marketing posts. Thirty sticks that draw a response spectrum (`sticks`): steel tubes from 1 m to 10 m with a mass on top, each a model of its own on a branch of the trees - a period from Natural Vibration, Rayleigh damping of 5% at that frequency, El Centro in 0.005 s steps - and a GhPython component that turns the most each top moves into Sd and PSa and draws the spectrum behind them. A compressed shell (`shell`): a cross of four legs form-found with Kangaroo2 (Refine, Length(Line) shortening to nothing, Load up, Anchor, Zombie Solver), then a 150 mm concrete shell under its own weight, in iso-surfaces. A concrete tank's first hundred modes (`tank`): a cylinder 4 m across and 5 m tall, its wall 200 mm of concrete as ASD Shells, built in round its base; a Series of steps into Model View's Step solves every mode, and its Geometry output, moved onto a 10 x 10 grid by the geometry's `Grid`, is drawn by a Mesh parameter (Model View draws only its last branch), with each mode's number and frequency under it in Text Tag 3D. Four buildings that are not straight, shaken by El Centro, their geometry from a frame on a grid whose every point is placed by a function of its floor (`_mapped`): a concrete tower twisting a quarter turn (`twisting`), two steel towers leaning over a street, 26 and 20 storeys (`leaning`), a round steel tower bowing out and back (`curving`) and a concrete block curved in plan (`crescent`), each with a slab at every floor and only the displacements recorded. Five towers of 100 storeys and more, after famous ones' structures, every floor a Rigid Diaphragm driven from one of its nodes with its mass as Mass Points at its nodes, 2% damping and all 31 s of the record (20 s for the buttressed core), drawn without their elements' edges, which would turn thousands of thin members grey: a bundled tube (`bundled`, Willis Tower), a braced tube (`braced`, John Hancock Center), a buttressed core of concrete walls and a spire (`buttressed`, Burj Khalifa), a braced core with super columns, outriggers and a tuned mass damper - a truss rod, a Link of elastic Uniaxial materials with Eta for its dashpot, and a Mass Point (`outrigger`, Taipei 101) - and a diagrid round a concrete core (`diagrid`, Guangzhou IFC). The website's examples `natural-vibration` (the tank), `response-spectrum` (the sticks) and `compressed-shell` (the shell), and the nine earthquakes `twisting-tower-earthquake`, `leaning-towers-earthquake`, `curving-tower-earthquake`, `crescent-earthquake`, `bundled-tube-earthquake`, `braced-tube-earthquake`, `buttressed-core-earthquake`, `outrigger-tower-earthquake` and `diagrid-tower-earthquake`. An irregular concrete building pushed over (`pushover`): an L in plan, three cells of a grid of two bays by two, a tall ground storey and a third storey set back over one cell, every member a ForceBeamColumn on a Fiber Section of Concrete01 and ReinforcingSteel bars (`fibre_section`: a Rectangle 3Pt meshed into a Patch, a Fiber Point a bar, GJ a cracked section's), its own weight and the floors on a linear time series, and Pushover Analysis pushing the top storey's roof to Roof drift % of the height along X with a load on every node that grows with its height; Model View draws it with its plastic hinges, a panel lists them, and a GhPython component draws the capacity curve beside it and hands Model View the step of the Point on the curve slider; the website's example `irregular-building-pushover`. |
| `ReciprocalVault.py` | A reciprocal vault of plywood planks pressed down until it gives way. Its geometry from sliders in a GhPython component (`ReciprocalVaultGeometry.py`, plain Python up to its GhPython functions, so it runs outside Rhino too): a pinwheel pattern drawn flat on a grid of Cell, four planks turning about a Fan-wide opening round every node, each resting on the next, the vault unrolled; wrapped round an arch of Span and Rise; then the planks' ends moved, crossing by crossing, until every crossing is one Plank thickness deep along the line square to both axes (the feet held), and each plank cut into pieces at its crossings. Each plank a chain of ForceBeamColumns on a Fiber Section of Steel01 as birch plywood (9 000 MPa, yielding at 25 MPa, b 0.01) in 6 x 8 fibres, GJ of a flat plank; a second GhPython component (`corotational`) gives each piece a Corotational GeomTransf, which no Grasshopper component makes (P-Delta finds half the strength). Each crossing a Spring Link from the plank under to the plank on top: 1e5 kN/m along the bolt, 4 000 kN/m across it (EN 1995's K_u for an 8 mm bolt), 20 kN m/rad for the planks turning against each other, free about the bolt - Uniaxial Elastic with Eneg given too, which otherwise stays 210 000 000. The feet pinned; its own weight by a Gravity Load of Density / 7 850 (Steel01 weighs as steel); Pushover Analysis adding its weight again, a Point Load a node, down (Direction Z) or sideways (X, Y), the crown controlled; Model View draws it as it moves with its hinges, and a GhPython component the capacity curve in times its weight or in g. Pressed down it peaks at 2.36 times its weight at 222 mm and gives way. The website's example `reciprocal-vault-pushover`. |
| `FootfallStair.py` | The Footfall Analysis examples, a definition each (`STAIR`), their geometry from sliders in a GhPython component (`FootfallStairGeometry.py`). `u`: a U-shaped steel stair between floors 4.2 m apart - two long flights and a short one, two quarter landings - each flight between two stringers, Rectangle Hollow CS 100 mm wide and Stringer depth deep, straight through the middle of every tread's ends with half a rise up or down to the landings, its open treads steel plates as ASD Shells fixed to them there; each landing a plate on four 200 x 100 x 8 edge beams with a 139.7 x 8 post under its free corner. `helical`: a helical stair of shells only - a ribbon, the helicoid, climbing Height in Risers over Turn degrees between two parapets, cylindrical bands Parapet height tall that share its edge nodes and carry it, built in at both ends; the steps drawn over the ribbon (`StepMeshes`), for the map only. `straight`: 36 steps in three flights with two landings in a line, held only along the stringers' end edges - two plate stringers Stringer depth deep, plate treads sharing their middle end nodes with them and tied at their corners by Rigid Links (without them the thin stringers flap sideways), plate landings. In each: the mass in use (P354), the steel's own and Mesh Loads of the finishes and of the imposed load on the plates people walk on, in Load Patterns named "Finishes" and "Imposed" (each Name typed in a panel), which two Mass from Loads name - "Finishes" at 1, "Imposed" at Imposed as mass, 0.1; Natural Vibration by ARPACK on Mumps (12 or 15 modes: past twice the fundamental, short of the treads' own modes, which ARPACK stalls on); Footfall Analysis with P354's stair coefficients (Table 3.2), 1.2 to 4.5 Hz, 0.5% damping, the 12 Hz cut-off, Footsteps the stair's risers, its Frequency data menu open; Limit R, 32. `Response map` (`response_map`) colours where people walk - the treads and landings, or the helical stair's steps from the ribbon's nodes under them - in even bands, pale green to orange over 0 to the largest, and when past Limit R the limit a band edge, reds over it, for a Mesh parameter, a Legend titled with the limit and the largest, a Text Tag and a dark pin on the worst node; one Model View draws the stair, another the mode Mode picks in blues (never the response's colours), `Mode title` (`mode_title`) putting its number and frequency in its legend. Both GhPython icons are in `Assets/icon-candidates` (`footfall-stair-geometry.png`, `footfall-response-map.png`). The website's examples `footfall-stair`, `footfall-helical-stair` and `footfall-straight-stair`. |
| `HighTensionTower.py` | A steel lattice pylon 42 m tall, its geometry from sliders in a GhPython component (`HighTensionTowerGeometry.py`): four CHS legs tapering from an 8 m base to a 2.4 m waist, a straight shaft above, two pairs of arms 7 m long for four conductors and a shorter pair for the earth wire, X-bracing and struts as trusses, pinned feet. Loads: own weight, the wires' weight and wind at each arm tip (the earth wire at half), and wind on the windward legs as a line load. Drawn deformed, coloured by displacement, and its axial forces as diagrams in a second Model View. The website's High-tension tower example. |
| `MultiStoreyBuilding.py` | A reinforced concrete frame of 5 x 3 bays and five storeys, its geometry from sliders in a GhPython component (`MultiStoreyBuildingGeometry.py`): 400 mm square columns, 300 x 500 beams split at the slab's mesh, 200 mm ASD Shell slabs, columns built in. Finishes and imposed loads as Mesh Loads on the slabs, wind on the windward columns, own weight; a Natural Vibration Analysis of six modes. Drawn deformed, and in its first mode. The website's Multi-storey building example. |

### Design/

| Example | Builds |
| --- | --- |
| `SteelCantilever.py` | The Utilisation example: a 4 m steel cantilever checked to EN 1993-1-1, with the buckling lengths a cantilever needs and the Design menu open on the report. |
| `MomentCurvature.py` | The Moment Curvature example: a 300 x 500 reinforced concrete section, Concrete01 in 240 fibres and phi 24 bars of ReinforcingSteel, eight along the bottom and three along the top, under 400 kN of compression, bent until the first fibre reaches its ultimate strain; Moment Curvature Diagram draws the curve beside the section, and panels give first yield, the ultimate state, the ductility and the cracked stiffness. |
| `ColumnDomain.py` | The Interaction Domain example: a 400 x 600 reinforced concrete column's N-My-Mz domain, three combinations checked against it, and the domain drawn with Domain 3D, N-M Diagram and My-Mz Diagram beside the section. |
| `ConnectionDesign.py` | A tree column's cast steel node in shells: a sphere with a trunk tube and three branches welded on, joined and opened at the tube ends in a GhPython component (`ConnectionDesignGeometry.py`), meshed in quads by Mesh Brep, ASD Shells of S355 25 mm thick. The trunk's end built in, each branch's end loaded by the roof it carries, shared over its nodes by a second GhPython component. Drawn deformed and coloured by Von Mises stress. The website's Connection design example. |

### Optimisation/

| Example | Builds |
| --- | --- |
| `FootbridgeStudy.py` | The Explorer example: a steel truss footbridge analysed with Alpaca4d, and a design study of 108 designs over its span, depth, panels and chord section. FootbridgeStudy.gh is not what it writes: the beam stresses' view and Legend were added in Grasshopper (1f65287), so do not write it over - upgrade it in Grasshopper instead. |
| `CurvedBeamOptimiser.py` | The Section Optimiser example: a 12 m beam curving in a vertical plane, built in at its start, held up at two more points and ending in a cantilever with a point load near its tip, in 30 elements, IPE300 to start. Sized element by element within the IPE family, and as one section for the whole beam from a design group. |
| `BeamGridOptimiser.py` | A Section Optimiser example: a 20 m square grid of beams on four pinned supports, with a node load everywhere and its own weight. A slider walks the supports from the corners towards the centre, from a slab spanning between them to one cantilevering out, and every element is sized on its own within its family - rectangular hollow sections, RHS 300x200x10 to start, over 40 iterations: joined rigidly at every node, the beams twist, and an open section such as an HEA cannot take the torsion at any size. |
| `TwoSpanBeamOptimiser.py` | A Section Optimiser example: a curve about 30 m long in a vertical plane, shattered at a point Evaluate Curve places along it, each segment divided into elements of about 25 cm. Built in at both ends and pinned between the segments, HEA300 in S235 to start, under its own weight and a point load a slider moves along the curve to the closest node. One design group sizes it from a panel of HEAs, element by element as it opens (its Uniform toggle gives the whole beam one), with the HEA named where it changes and a second Model View drawing the deformed shape. |
| `TwistedRing.py` | The geometry of a twisted ring truss, for an exercise on the Section Optimiser and multi-objective design: fourteen sliders and a list - the ring's radii, wave and swell, the profile and its twist, the slices and the bracing (Diagonal, Zigzag, Checkerboard, Cross or K) - into one GhPython component, whose code is `TwistedRingComponent.py`. It gives the chords, the frames and the bracing, each joined into polylines (explode them before making elements), and the nodes, the member and node counts and the total length. GhPython opens in Rhino 7 and 8, and the code runs in Rhino 8's Python 3 component too; the supports, loads and Alpaca4d model are left to the student. |

### Topology/

| Example | Builds |
| --- | --- |
| `TopologyBridge.py` | The Topology Optimisation example: a 16 x 2 x 4 m steel block of SSP Bricks pinned along both ends of its bottom, its top layer a deck kept solid through Keep under 10 kN/m2. With a fifth of the rest it finds an arch rising into the deck, with a column near each end; Bricks per metre takes it from 128 bricks to 8 192. |
| `TopologyBenchmarks.py` | Topology Optimisation's two benchmarks in top3d's units and settings, each with its own Run toggle: top3d's 60 x 4 x 20 cantilever and a 120 x 4 x 20 simply supported beam, with panels giving the compliance the test bench and top3d reach after 200 iterations. `Alpaca4d.Test/TopologyOptimisation/example.sh` builds and runs it in Rhino. |
| `MultiMaterialBridge.py` | The Multi-Material Topology example: TopologyBridge's block in steel (E = 210 GPa) and concrete (E = 33 GPa), at most 5% and 15% of the block below the deck. The concrete goes into two legs from the supports to the deck, the steel into an arch slung under it: 265 iterations, under a minute at 3 bricks per metre. `Alpaca4d.Test/TopologyMethods/example.sh` builds and runs it in Rhino. |
| `BesoBridge.py` | The BESO Topology example: TopologyBridge's block carved down to a fifth of its volume below the deck, every brick solid or void, Step going through every volume on the way: two raking legs into the deck with a column near each end, 105 iterations, under 20 s. Built and run by the same `example.sh`. |
| `TopologyNodes.py` | A node where members meet, in space, found by all three topology optimisations side by side: a 1 m cube of bricks with member stubs kept solid, loaded or held at their ends. `NODE_CASE=truss` writes `TopologyTrussNode.gh`, nine members of a space truss (chords pulled and pushed, a post loaded, four diagonals held); `NODE_CASE=joint` writes `TopologyBeamColumnJoint.gh`, a column and four beams with their shears and unbalanced hogging moments. `Alpaca4d.Test/TopologyMethods/nodes.sh` builds and runs both in Rhino. |

### Solids/

| Example | Builds |
| --- | --- |
| `SpotTheCow.py` | A tetrahedron example: Spot the cow, a solid concrete cow 2.4 m long standing on its hooves under its own weight, in 5 030 tetrahedra, with the vertical stress drawn over it. |
| `Femur.py` | A tetrahedron example on a bone: the Standardised Femur, held at the knee with the force of the hip on its head, in 3 813 tetrahedra, drawn deformed. Femur.gh is not what it writes: Assemble's Tolerance of 0.1 mm, without which three edge nodes at the knee are left unsupported and OpenSees does not converge, and the modes were set in Grasshopper (d3c2004), so do not write it over - upgrade it in Grasshopper instead. |
| `RebelliousSlave.py` | A tetrahedron example in marble: Michelangelo's Rebellious Slave, 2.13 m and 875 kg on its plinth, in 28 119 tetrahedra, held under the plinth, its vertical stress under its own weight drawn in iso-surfaces, and its first six modes, a slider picking which a second Model View draws. Assemble's Tolerance is 0.1 mm: the default 1 cm would weld the middle nodes of its 2.5 cm edges to their corners. |
| `MeshLoftBricks.py` | A concrete barrel vault in bricks, from polylines: a GhPython component (`MeshLoftBricksGeometry.py`) draws Layers + 1 surfaces through its thickness, a branch of polylines each; Mesh Loft lofts each into a mesh, Mesh Series to Brick makes a layer of SSP Bricks between each two. Both springings held, its own weight; drawn deformed and coloured by Von Mises stress. Five layers of bricks through its 400 mm to start with (1 440 bricks), the slider up to 10. The website's Mesh Loft - Mesh Series to Bricks example. |

### Plasticity/

| Example | Builds |
| --- | --- |
| `PerforatedBeam.py` | The plasticity example: a simply supported S355 beam 2.4 m long, 300 mm deep and 80 mm thick with six round holes 180 mm across, in 7 478 ten node tetrahedra of J2Plasticity (the nD component's unit), resting on its end faces (Z and Y; one node at midspan holds X), 740 kN on a 100 mm strip across the top at midspan in 20 steps of Load Control with Newton. Model View draws the Von Mises stress in iso-surfaces of Alpaca4d's colours, light blue to purple for the elastic range and orange at the top where the steel yields, with a Step slider; a GhPython component (`PerforatedBeamCurve.py`, its icon from `Assets/icon-candidates`) reads Nodal Displacements and Brick Stresses at every step and draws the load-deflection curve beside the beam, blue while elastic and orange from first yield, through Custom Preview - so make.sh cannot write it: run the script in Rhino 8 with SAVE set. |
| `Plasticity.py` | Four more examples of steel that yields, J2Plasticity S355, a definition each (`CASE`), pushed past first yield in 20 steps of Load Control on a Linear Time Series with Newton, the Von Mises stress in Model View's brand-coloured iso-surfaces (orange: yielding) and the load-displacement curve from a GhPython component (`PlasticityCurve.py`, which follows a node along a direction and groups shell stresses by step) through Custom Preview: a pipe T-joint as ASD Shells (`tjoint`: brace 150 x 8 pushed into a chord 300 x 10, both ends held, its top 60 mm elastic as an end plate; 640 kN; EN 1993-1-8 gives 388 kN); a square plate 1 x 1 m x 25 mm on four edges under a pressure (`yieldlines`: Mesh Load, 1 300 kN/m2; yield-line theory 1 331); a plate 400 x 200 x 10 with a hole 60 mm across pulled in its plane (`platehole`: every node held in Z, 500 kN; net section 497); and a quarter of a thick pipe, bore 200 and outside 400 mm, in plane strain as Ten Node Tetrahedra (`pipe`: the bore's pressure as six node triangles share it, 280 MPa; collapse 284.1). Every support picks its nodes with an Expression of their coordinates, no node on two, thresholds written with nine figures (`%g` writes 1.799999 as 1.8, and the T-joint's second end lost its support). Custom Preview cannot be made outside Rhino: run it in Rhino 8 with SAVE set. |

### Printing/

| Example | Builds |
| --- | --- |
| `PrintedCylinder.py` | The 3D printing example: the cylinder TU/e printed (Wolfs, Bos and Salet 2018) - a path of radius 250 mm, layers 40 x 10 mm round at the sides, 5 m/min - as far as a Time slider has printed it, under its own weight, the base held on the print bed. Three GhPython components do the printing, their code in `PrintedCylinderLayers.py` (the bricks and their ages), `PrintingConcrete.py` (the stiffness by age) and `PrintingJoint.py` (a joint band's material, marked for Linear Buckling), each with its icon from `Assets/icon-candidates`. Linear Buckling gives the factor on the weight: it falls as the wall grows and reaches 1 at about 47 layers (the lab prints fell at 25 to 31, by buckling and yielding together). |
| `PrintedStructure.py` | 3D printed shapes, printed along their own sections, one definition each (`SHAPE`). The earlier voxel example's branching shell (`branching`, `PrintedStructure.gh`) - three legs at its foot, two above, one at the top, open at its crown, 0.36 m tall, from `PrintedStructure.3dm` - comes down to a load factor of 1.00 at 20 of its 36 layers, as its last two legs are about to merge, and stays within 5% of it to the end. A twisted column (`twisted`) of six lobes round a radius of 160 mm, turning 60 degrees over 0.6 m; a free-standing wavy wall (`wall`) 1 m long and 0.4 m tall in waves 250 mm long and 50 mm deep; a beehive dome (`dome`) 0.6 m across, leaning in to an opening 0.31 m across at 0.39 m: their surfaces lofted, extruded and revolved in the script. A leaning column (`leaning`, `PrintedLeaningColumn.gh`), a tube 0.3 m across rising 0.2 m, bending over to 45 degrees and on 0.3 m, is printed along its axis, the Path: with a Path, Printed Layers cuts the shape square to it every Height along it, as a printer that tilts its nozzle lays it, each layer as thick at each point as the next section is from it, and gives the nozzle's Axis; unplugged, the column is printed flat. Printed Layers (`PrintedStructureLayers.py`) carries each section's nodes up to the next, so that the layers follow the wall node for node: evenly, turned as the section turned, where a section takes a whole loop of the one below; to their closest points where legs merge or split, merging nodes that meet and filling gaps; closes a section whose ends are nearer than the filament is wide; and draws in the inside of a tight turn so that no brick folds. Fresh Concrete and Surface Connection as the cylinder's; Linear Buckling and Run Analysis on its own weight. The surfaces need Rhino's own geometry code, so make.sh cannot write these: run the script in Rhino 8. |

### Scripting/

| Example | Builds |
| --- | --- |
| `CustomScript.py` | A 2D portal frame written in OpenSees Tcl in a panel - HEB300 columns 4 m tall, an IPE400 beam 6 m long, 20 kN/m on the beam and 30 kN sideways, ten load steps - run through Custom Code with no model and Run Analysis set not to use its settings; the script's `puts` lines - the sway, the base shear, the reactions - read back from the log. The website's Custom Script example. |
| `DeconstructAnything.py` | A steel cantilever, an IPE300 4 m long with 10 kN at its tip, opened up by Deconstruct: the beam, then its section and the section's material, the load and the assembled model, then the model's nodes, each Deconstruct's outputs made from the type it is given. Panels show the area, the second moments, E, the density and the nodes. The website's Deconstruct Anything example, whose page keeps its simple picture of three Deconstructs (publish.sh leaves it). |

### Benchmarks/

| Example | Builds |
| --- | --- |
| `MixedModelSpeed.py` | A speed test of Assemble Model: a shell slab on steel beams and columns, cut at the slab's nodes, on a brick raft, with point, line, mesh and gravity loads in one pattern. One slider takes it from a hundred elements to forty thousand. |
| `BrickSolverSpeed.py` | A speed test of the solver: an 8 x 8 x 6 m concrete block of SSP bricks, pinned along its bottom, with four column loads and its own weight. One slider takes it from 1 500 unknowns to 151 000, and a System list solves it with Mumps on several cores or with BandSPD or UmfPack on one (`Tools/OpenSees`). |
| `StressLinesBenchmarks.py` | Principal Stress Lines against three closed-form solutions, each solved by OpenSees: Timoshenko and Goodier's cantilever loaded by its exact end tractions, Navier's simply supported slab (bending lines, with a toggle that turns a third of its shells over) and Kirsch's plate with a hole. Drawn side by side in Rhino's Top view: single lines through six seeds with the exact trajectories dashed over them, and fields of lines. Custom Preview cannot be made outside Rhino, so make.sh cannot write this one: run the script in Rhino and save what it opens. `Alpaca4d.Test/PrincipalStressLines` measures the same lines. |

### Report/

| Example | Builds |
| --- | --- |
| `ReportFrame.py` | The Report test: a two-storey steel frame, 6 x 4 m on plan and 3.5 m a storey, HEB200 columns and IPE300 and IPE240 beams in S355, every member in three pieces (48 elements); static, natural vibration (6 modes), linear buckling (4 modes) and footfall (people walking on the floors) analyses, their models wired into the Report; and the report components writing them up as a PDF - an A4 Report Page with a title block of its own, Report Texts whose headings the report numbers (1, 1.1, 1.1.1), Report Result Tables of the model, a Report Table of the loads, and Report Views by topic (Model, Static analysis, Natural vibration, Stability, Footfall), a picture for each component ticked, each with its largest and smallest value written on it and its table after it - through a Merge into the Report, with no Model View on the canvas. Set the toggle to True to write it beside the .gh, then click Open report. |
| `ReportCases.py` | Five example reports to share, each a definition of its own, CASE one of: `beams` (`ReportBeams.gh`, Structures.py's truss roof hall: static, natural vibration and linear buckling), `shells` (`ReportShells.gh`, its barrel vault: the same three), `bricks` (`ReportBricks.gh`, MeshLoftBricks.py's vault of bricks: static and natural vibration), `mixed` (`ReportMixed.gh`, its footbridge, a deck of shells on a box girder of beams: static under its own weight and a crowd, and natural vibration) and `stair` (`ReportStair.gh`, FootfallStair.py's U stair, beams and shells: static, natural vibration and footfall). Each builds its structure with its own example's builder, adds the loads and analyses it lacks beside it, and the report in columns, a chapter each. The PDFs, their pages and the canvases are in the docs repository's `marketing/report/<case>/`. |

### Elsewhere

| Example | Builds |
| --- | --- |
| `../../Alpaca4d.Test/Optimisation/NonLinearOptimisationCases.py` | The eight test cases of Non-Linear Optimisation, each with a known answer. |

A script works with make.sh when it makes its objects through `emit()`, which uses `make_object`
when it is set, and has a `build(doc)` that adds them to `doc` and runs nothing at import.

## Tetrahedra

Grasshopper has no tetrahedral mesher, so the tetrahedron examples read theirs from a `.txt` next
to them - the nodes, one `x y z` a line, then four node numbers per tetrahedron - and keep them in the
definition. A `*Mesh.py` script made each `.txt` with [gmsh](https://gmsh.info) (`brew install
gmsh`), from a model it downloads, through `Solids/TetMesh.py` for the solids:

| Script | Writes | From | Needs besides gmsh |
| --- | --- | --- | --- |
| `Solids/SpotTheCowMesh.py` | `SpotTheCow.txt` | Spot, Keenan Crane, CC0 | nothing |
| `Solids/FemurMesh.py` | `Femur.txt` | the Standardised Femur, Marco Viceconti, University of Sheffield, CC BY 4.0, [doi 10.15131/shef.data.3839766.v1](https://doi.org/10.15131/shef.data.3839766.v1) | numpy, scipy, scikit-image |
| `Solids/RebelliousSlaveMesh.py` | `RebelliousSlave.txt` | Michelangelo's Rebellious Slave, SMK's scan of its plaster cast KAS86, public domain ([open.smk.dk](https://open.smk.dk/artwork/image/KAS86)) | numpy, pymeshlab |
| `Plasticity/PerforatedBeamMesh.py` | `PerforatedBeam.txt` | a box less six cylinders, in gmsh's OpenCASCADE kernel, finer round the holes | gmsh's Python module |
| `Plasticity/PlasticityMesh.py` | `PlasticityTJoint.txt`, `PlasticityYieldLines.txt`, `PlasticityPlateHole.txt`, `PlasticityPipe.txt` | two tubes' walls fragmented where they meet, by part (chord, brace, brace end); a 40 x 40 grid; a rectangle less a disk with a node at each short edge's middle; a quarter ring - all in gmsh's OpenCASCADE kernel, shells as faces with a part, the pipe as tetrahedra | gmsh's Python module |

The cow fills its own surface. The femur's IGES does not close into a solid, so it is rebuilt on a
grid first - a signed distance, marching cubes, smoothed. The slave's scan is remeshed to the element
size, finer below the knees, and filled as it is; each script's docstring says how.
Run one with Python 3 from its folder, which is where the `.txt` lands, then `make.sh` on its example. The three solve with Ten Node Tetrahedron,
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
`rebellious-slave`. After writing a `.gh` again, copy it there under that name.
