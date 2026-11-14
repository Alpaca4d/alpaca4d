# 3D printing

    ./run.sh                                       # the checks, through Core
    ./run.sh --cylinder 1 2 40 1 30 40 44 48       # a table for the TU/e cylinder: bricks across,
                                                   # up, round, round sides, then layers printed

Checks the solids' and joint bands' stress stiffness in `Alpaca4d.Buckling` on 3D printed walls. The
bricks, their ages, the fresh concrete and the joint band come from `Printing.cs` - `PrintedLayers`,
`FreshConcrete`, `SurfaceConnection` - the C# the printed cylinder example's GhPython components were
written from (`Tools/Examples/PrintedCylinderLayers.py`, `PrintingConcrete.py`,
`PrintingJoint.py`); they are not part of the plugin. The decks are written as Assemble writes
them, and need an OpenSees that prints its matrices sparse.

| case | against |
| --- | --- |
| Wolfs, Bos and Salet's mortar | their fit, E = 0.078 + 0.0012 t MPa, 0.186 at 90 min; levelling off: the same start, the final stiffness |
| a joint band's material | the concrete when given no stiffness; Ez = kn t, G = ks t; positive definite when soft |
| bricks along a straight path and a 48-gon | their volume, Jacobians, base, print time, ages, the nozzle half way round; round sides against a rectangle with half circles |
| a free wall printed with and without curing | Suiker's (2018) equation for it, solved in the test by a beam of 400 elements: his closed form is a fit, 3 % high at ξ = 0.53 |
| the wall with joint bands | bonded when as stiff as the concrete; a stack on slipping joints at Haringx's load, P = (√(S² + 4 P_E S) − S) / 2 |

`python3 structure.py` checks the layers of the printed structure example
(`Tools/Examples/PrintedStructureLayers.py`), without Rhino, on sections made in the test: an upright
cylinder (the cylinder's 80 bricks a layer), a leaning one, a cone, a flaring one, two rings merging, a
lobed column twisting (the same bricks every layer: its nodes turn with it rather than bunch), a
ring opening, a ring with a 10 mm slit (printed closed: the filament fills it), a narrow rectangle with
sharp corners and a pointed ellipse; and a tube bending over to 45 degrees, cut square to its axis
(the layers leaning with it: the same 48 bricks every layer, round the bend 6.3 mm thick on its inside
and 13.8 mm on its outside, Height (1 ± r / bend), the nozzle tilting) and cut flat (its slanted rim open
arcs). No brick may fold through itself (a corner whose Jacobian is not positive, which SSP Brick
refuses), and all must be joined to the print bed. The pointed ellipse is the one that folds when the
inside of a tight turn is not drawn in.

The example itself is run on the canvas by the `printing` scene of `Tools/Marketing`, which logs its
load factor every third layer: 1.649 at 30 layers and 0.978 at 48, as `--cylinder 1 2 40 1` gives. The
printed structure's four shapes are run by the `printedstructure`, `printedtwisted`, `printedwall` and
`printeddome` scenes: the branching shell at 1.85 at 14 layers, lowest at 1.005 at 20.3, 1.02 at its
last; the column, the wall and the dome reaching 1 at 53.3, 27.6 and 27.4 layers. Core gives the same on
the component's own cut (the shell 1.714 at 14.29 layers, as on the canvas).
