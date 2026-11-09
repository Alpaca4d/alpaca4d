# Pushover

    ./run.sh                    # Core under mono, against hand calculations
    ./grasshopper.sh            # the real components, in a running Rhino 8 with its script server
    OPENSEES=/Applications/OpenSeesLatest-Mumps/bin/OpenSees ./run.sh

Checks `Alpaca4d.Pushover`, which Pushover Analysis runs - the deck that holds the model's loads and
scales a lateral pattern under displacement control, and the reader of the capacity curve it writes -
and `FiberSection` as a beam section. `run.sh` writes each column's deck by hand around
`Pushover.AnalysisTcl`, so it needs nothing of RhinoCommon but `Point3d`: its sections are point
fibres. `grasshopper.sh` builds the same column out of the components, on a patch, and reads it back
through Reaction Forces, Beam Forces and Model View, and checks the plastic hinges the push records -
Pushover Analysis' Hinges and Model View's Plastic hinges - against first yield by hand.

The column is 3 m, fixed at the bottom, pushed along local y at the top: V = M / L at the base.

| case | against |
| --- | --- |
| a fibre section drawn at (1, -2) | area, centroid, Izz and Iyy about it; Steel01's E0 and 7 850 kg/m3 for a material with no density; G J = GJ; on a beam the fibres written about the centroid, for Moment Curvature where they were drawn |
| 300 x 500 C25/30 with four B450C bars | E the concrete's 2 fc / εc0, the bars counted Es / Ec times, the mass 2 500 and 7 850 kg/m3 |
| free tags, P-Delta | a tag past every pattern and time series; Linear made PDelta, Corotational left |
| two flanges of Steel01, b = 1e-4, Linear, no axial force | 101 points, steps 9 to 109; first slope 3 E I / L³ exactly; the plateau Mp / L, 0.15% above for the hardening; base shear = load factor on 1 kN |
| the same, PDelta, 500 kN held | first slope 3 E I / L³ - P / L; at 0.15 m (Mp(N) - P Δ) / L, Mp(N) with one flange at -fy and the other carrying the rest of N; the peak before |
| a negative target | to -0.15 m, base shear and load factor negative |
| flanges in MinMax ±1% | the peak Mp / L, the base shear down to nothing once they break |
| two flanges, b = 0 | the base section has no bending stiffness the moment it yields: the push stops at the first step OpenSees says it could not invert, the curve cut there, little printed; Steel01 b = 0 named for the warning |
| 12 x 12 fibres, b = 0.001, to 0.3 m | nothing lost; the base shear rising all the way to Mp / L, 2% above for the hardening |
| in Grasshopper: a 300 x 200 patch, 500 kN and its weight, PDelta from the component | the outline turned for Model View; 61 points; first slope 3 E I (1 - 1/12²) / L³ - P / L; at 0.15 m (Mp - P Δ) / L; Reaction Forces at the first point 500 kN and 13.86 kN of steel; at the last the base shear; Beam Forces' base moment V L + P Δ; Model View draws it |
| plastic hinges, in Grasshopper | the first is the column's foot, I end, at point 14: the first past Δ = (fy - N / A) W_el L² / (3 E I) = 33.2 mm; the Hinges in the order they form; Model View's Plastic hinges: none at point 13, the foot at 14 |
| the same drawn at (7, -4) with a 2 kN lateral load; P-Delta off | the same curve, the load factor on 2 kN; P Δ / L = 25 kN more without P-Delta, and a remark |
| refused | WithHinges and Truss refuse a fibre section; Pushover Analysis warns of b = 0 |

## What it found

**Steel01 with b = 0 on a fibre section goes wrong without a word.** A force-based beam inverts its
sections' stiffness, and a section whose fibres have all yielded with nothing to harden has none.
OpenSees prints `could not invert flexibility` and carries on with garbage. Worse, a few steps before
it says anything, the base shear is already climbing past Mp / L: on the two flanges at point 23,
and on the 12 x 12 section near 0.14 m, 17% over, with no message at all. Pushover Analysis warns of
Steel01 with b = 0 before it runs, and stops at the first step that prints the message. Steel01's
default b is 0.01.

**What OpenSees prints can be enormous.** In that state every element iteration of every attempt
prints the message: 7.8 million lines, 3 min 40 s on the master build, until the deck stopped
trying. The deck sends opserr to a file with `logFile -noEcho`, reads back in Tcl what each attempt
printed, and stops at once; `Pushover.Read` streams the file, running repeats together.
