# Footfall

    ./run.sh
    OPENSEES=/Applications/OpenSeesLatest-Mumps/bin/OpenSees ./run.sh
    PIPE=rhinocode_remotepipe_<pid> ./grasshopper.sh
    PIPE=rhinocode_remotepipe_<pid> ./plate.sh

Checks `Alpaca4d.Footfall`, which Footfall Analysis runs - SCI P354's response of a floor or a stair to
a person walking, by self and by full excitation - and `Read.MassNormalisedModes`, which reads the
modes it works from as OpenSees recorded them. Every formula is written out again in the check, from
P354 (revised 2009, with the January 2009 corrections), rather than called from the code under test
(`ByHand.cs`, which `PlateCheck.cs` shares): the steady state as P354's full time history, Equation 31
with the phases of Equation 32, sampled over a pace.

| part | against |
| --- | --- |
| tables and factors | Table 3.1 at 2 Hz: 0.4578, 0.0978, 0.0784, 0.0700; Table 3.2 for stairs, 1.1 and 0.22, two harmonics; Wb, Wg and Wd (Equations 21 to 23); the build-up factor 1 - e^(-2π ζ N), 1 with no footsteps (Equation 37); the impulse 60 fp^1.43 / fn^1.3 Q / 700, 185 N s at 2.2 Hz on 1 Hz (Equation 18); the magnification 1 / 2ζ on resonance (Equation 30) |
| worked example D.1 | SCI's own office floor: 9.30 Hz, 10 226.8 kg, 4.68%, Wg. P354's simplified Equation 50 gives R = 9.48; the general method, with the fourth harmonic on the mode, gives Equation 50 with α₄ = 0.0791 in place of 0.1, and under 2% more for the other harmonics |
| worked example D.2 | a lightweight floor: 13.6 Hz, 1 181.26 kg, Wg, 2.2 Hz. P354's Equation 60 gives R = 39.99, the peak over √2; undamped, the exact rms over one pace is the same less the part of a cycle that does not fit in it, 0.9951 |
| by hand | the steady state of one and of two modes against the time history of Equation 31, the modes with their phases (Equation 32), times ρ, and no more than Equation 36, which adds them without, with the walker on the node; the transient of one mode and of three, against Equation 34 sampled at 200 000 points and Simpson's rule |
| the modes' phases | two modes either side of a harmonic, φₑ φᵣ + for one and - for the other: as the time history, which adds them, over three times Equation 36, which takes one from the other; D e^(iφ) out of phase below the harmonic, in phase above, of magnitude D |
| full excitation | six nodes, four made-up modes: the walker alone on the response node is self excitation to the last bit; self excitation at some nodes is as at all of them; walking at A shakes B as walking at B shakes A; each response node keeps its worst walker, found against every walker tried one by one, never less than self excitation, and somewhere elsewhere; a node that does not move does not respond; the transient with signed shares by hand; no response nodes given reads at the walker's |
| where | each point takes its nearest node, and the farthest distance; the nodes of the shells by their place in Nodes, none without shells |
| which modes | the fundamental is the lowest mode moving mostly vertically, not a sideways one below it; a 7 Hz floor is low-frequency, steady state to 12 Hz, transient to 14 Hz, R the greater; an 11.5 Hz floor is high-frequency, R the transient alone; a stair is checked for its steady state only (SCI AD 406), against Table 3.2 by hand, and above its cut-off over the modes up to twice the fundamental |
| a simply supported beam in OpenSees | 10 m, 0.5 t/m lumped at 20 nodes, moving in X-Z. Its four modes read mass-normalised: the first at π/2 √(EI / m L⁴) = 3.943 Hz, √(2 / m L) = 0.6325 at midspan, a modal mass of m L / 2 = 2.5 t, sin 45° of it at quarter span. Self excitation with P354's defaults: mode 1 alone in both responses, the steady state and the transient at midspan as by hand, R = 54.65 at 1.975 Hz from the steady state, nothing at the supports. Full excitation at quarter span, the walker anywhere: the worst walker at midspan, R the midspan's times φ(L/4) / φ(L/2) = sin 45°, where self excitation gives its square |

The modes are found by MODE_0, MODE_1 ... in the recorder file: MODE_0 is the first, as Nodal
Displacements' Step 0 is.

`OPENSEES=/path/to/OpenSees ./run.sh` picks the build; otherwise the first on the PATH, then the
newest under `/Applications`. Without OpenSees the beam is skipped. Passes with 3.8.0.

`grasshopper.sh` runs the real component in a running Rhino 8 (`StartScriptServer`), with the
Alpaca4d Grasshopper loaded: build Alpaca4d.Gh into the live Grasshopper folder first, Rhino closed.
It opens the three stair examples of `Tools/Examples`, saved before Walk, Feel and Full excitation:
each comes back on Self excitation with its values and wires, walks on the nodes of its shells and
reports its largest R (15.91, 15.81 and 696.85). Beside it a Full excitation component gets the same
model and settings and a few Walk and Feel points: a result per Feel point, each on its own node, every
walker on a Walk node, none less than self excitation where it is walked on, the walker alone on the
worst node giving self excitation's R there. Saved and opened again, it keeps its unit, points, wires
and values. `PIPE=` names the Rhino when more than one runs.

`plate.sh` checks the whole chain on shells, in a running Rhino 8 (`StartScriptServer`). It builds the
beam above again as a plate strip out of the real components: ElasticIsotropic, Plate Fiber Section,
ASD Shell, Support, Assemble Model, Natural Vibration and Footfall Analysis. Build Alpaca4d.Gh in
Release anywhere (`GHDIR=`); the live Grasshopper folder is not needed, as this build is loaded beside
the installed one. It installs the test licence first (`LICENSE=` to name another file).

| model | what it pins down |
| --- | --- |
| the beam as a steel strip, 10 x 1.8 m, 100 mm, ν = 0, 20 x 4 ASD shells, 0.5 t/m from its density, held along X, Y and Z on its short edges; P354's defaults, 401 paces, 3% | b t³ / 12 is the beam's I, and with ν = 0 the plate's D b is E I exactly. Assemble's Mass is 5 t; the first frequency is π/2 √(EI / m L⁴) within 1%, the next past 12 Hz, so the first mode counts alone. Self excitation walks on the 105 nodes of the shells. R is the same all across midspan and is the largest there, as by hand (`ByHand.cs`) with the strip's own frequency and φ² = 2 / m L, and within 1% of the beam's R by hand. The supports do not respond, and quarter span gives sin² 45° of midspan. Full excitation felt at quarter span finds the worst walker at midspan, with R the midspan's times sin 45° |
| the same strip, half its mass from its density and half from a mesh load in a Load Pattern named "Finishes", which Mass from Loads names | ASD shells lump their mass a quarter to each corner, as Mass from Loads lumps a mesh load, so the model is the same one: the same Mass, the same frequencies to 1e-6, the same R at every node and in full excitation |

Last run 2026-11-12, all 17 checks passed: 3.946 Hz against the beam's 3.943 Hz, R = 54.72 at midspan
against 54.72 by hand and 54.65 for the beam, at 1.977 Hz.
