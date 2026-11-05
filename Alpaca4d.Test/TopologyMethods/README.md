# Topology methods: MMA, multi-material topology optimisation and BESO

    ./run.sh                                   # the checks
    ./run.sh bench zheng 3                     # Zheng et al.'s 3D cantilever, 3 materials (or 2, or 5)
    ./run.sh bench zheng 3 60 30 15 5 auto     # ... with its grid, filter radius, and the component's objective scale
    ./run.sh bench beso-3d                     # BESO on a 40 x 20 x 4 cantilever, then reference.py on it
    ./run.sh bench beso-cantilever             # esoL's clamped cantilever, one brick deep, then reference.py
    ./run.sh bench bridge                      # TopologyBridge's block by SIMP, BESO and single-material MMA
    ./run.sh bench grasshopper                 # what ../TopologyOptimisation/GrasshopperChecks.cs expects of the new components
    ./run.sh bench node truss 20               # a space-truss node in a 1 m cube of 20 bricks a side, by all three methods
    ./run.sh bench node joint 20               # a beam-column joint, the same way
    ./example.sh                               # Tools/Examples/MultiMaterialBridge.py and BesoBridge.py, built and run in Rhino
    ./nodes.sh                                 # Tools/Examples/TopologyNodes.py's two definitions, against the node benches
    ./speed.sh                                 # the speed test: every method timed on the bridge, the joint and the truss node

Needs a built Alpaca4d.Core (Release), mono's `mcs`, a net48 RhinoCommon and an OpenSees on PATH (or
`OPENSEES=`). `reference.py` needs a Python with numpy and scipy (`PYTHON=`). The checks of the components
themselves are in `../TopologyOptimisation/grasshopper.sh`, with Topology Optimisation's.

## What the checks check

* **MMA** (`Alpaca.Core/Optimisation/Topology/Mma.cs`, written from Svanberg's note "MMA and GCMMA - two
  methods for nonlinear optimization", 2007): the note's small test problem, every iterate to the four
  decimals of its table, ending at f0 = 8.7702.
* **The projection and the interpolation**: the tanh projection keeps 0, 1/2 and 1 and its slope is its
  derivative; the sharpness doubles every 50 iterations from 1 to 32.
* **The derivatives** of the compliance and the volumes by every design density, through OpenSees, against
  central differences: about 1e-7 apart, at a soft projection and a sharp one, three materials.
* **A short multi-material run**: each material at its share, the compliance down, most elements one material
  or void, and going on from 25 iterations to 40 the same as 40 at once, to the last digit.
* **A BESO run**: solid or void throughout, each design of the volume its update aimed at, converged by
  esoL's test, kept elements solid, and going on the same as one run.
* **A BESO breakdown**: TopologyBridge's block at 2 bricks per metre with a 2 m filter cuts a member through
  and never mends it; the run settles on what is left and says it broke (`BrokeAfter`), not converged. The
  same block with the default filter converges.
* **A BESO breakdown in two steps**: the beam-column joint at 10 bricks a side, whose compliance rises 4.3 times at
  iteration 109, under Breakdown, then eighty million times at 110, and comes back only to eight times what it was. The
  guard measures against the lowest of the five designs before, so it says the joint broke after iteration 108, the last
  whole design; measured against the design just before the big jump, it had looked mended.

## The benchmarks

Results on an M2 Pro with /Applications/OpenSeesLatest-Mumps, 2026-11-04.

**Zheng, Yi, Peng and Yoon (2024)**, their 3D cantilever on the grid of their Appendix B, 60 x 30 x 15
bricks, half the volume shared equally among the materials, filter radius 5, 300 iterations, the objective
unscaled as theirs:

| Materials | E | Compliance | Paper | Apart |
| --- | --- | --- | --- | --- |
| 2 | 1, 5 | 437.50 | 432.94 | +1.1% |
| 3 | 1, 2, 5 | 433.19 | 433.05 | +0.03% |
| 5 | 1, 2, 3, 4, 5 | 400.98 | 400.24 | +0.2% |

The paper's text gives 80 x 40 x 20 bricks, its code 60 x 30 x 15; the code's grid is the one that gives
its numbers. A run is about 16 minutes in mono (1.6 s of analysis and 1 to 3 s of MMA per iteration, more
with more materials). Run twice, a run gives the same numbers to the last digit.

**BESO against reference.py**, esoL.m in numpy on the same bricks, its own stiffness and SciPy's solver:

* esoL's clamped cantilever, `esoL(100,50,0.6,0.02,6,2)` one brick deep: 39 iterations both, the same
  compliance to 2e-11 every iteration, all 5 000 elements the same at the end, the design of the review's
  Fig. 60.
* A 40 x 20 x 4 cantilever to 40%: the same to 1e-12 for 80 iterations; at iteration 81 an element on the
  threshold goes the other way, and the two end 0.06% apart with 3 192 of 3 200 elements the same.

**TopologyBridge's block** at 3 bricks per metre, a fifth of the block below the deck:

| Method | Iterations | Compliance |
| --- | --- | --- |
| Topology Optimisation (SIMP, OC) | 77 | 0.00427, of a grey design |
| BESO, default filter (2 elements) | 102 | 0.00338 |
| MMA with projection, steel alone | 260 | 0.00288 |
| Multi-material, 5% steel and 15% concrete | 265 | 0.01056 |

BESO's filter averages the kept elements' energy too, as esoL averages every element's. That holds a load
pad to the structure: left out, the Grasshopper checks' small cantilever loses the kept column under its
load. It also draws material towards a kept deck in bending, and a filter much wider than two elements then
starves a member far from it until it is cut through: at 3 elements, 1 m, the bridge broke apart, which is
why the default is 2.

**Nodes in space**, Tools/Examples/TopologyNodes.py's two definitions at 20 bricks per metre (`bench node`, and
`nodes.sh` in Rhino, 2026-11-05):

| | Truss node, deck / Grasshopper | Beam-column joint, deck / Grasshopper |
| --- | --- | --- |
| Bricks, kept | 8 576, 576 | 9 024, 1 024 |
| Topology Optimisation, 12% / 20% | 200 its, 0.13365 / 200, 0.13365 | 160, 0.35793 / 160, 0.35793 |
| BESO Topology | 121, 0.090849 / 115, 0.090609 | 92, 0.27886 / 94, 0.27999 |
| Multi-Material Topology | 260, 0.15844 / 260, 0.15845 | 264, 0.67624 / 265, 0.67544 |

The definitions' first compliances are the decks' to 2e-7, not to round-off: Rhino keeps a mesh's vertices in
single precision, so Mesh Plane puts the nodes at 0.4000000059604645 where the deck has 0.4. The runs then
part by a few iterations; Topology Optimisation on the truss node runs to its 200 without meeting top3d's
test. The truss node's chords pull and push unequally (450 and 250 kN, 300 and 150): with equal forces a
chord pair needs only a bar between them, joined to nothing, which Solid and AlpacaModel then leave out as
a piece the supports do not hold. The truss node uses steel and aluminium (4% and 8%), the joint steel and
concrete (6% and 14%); each method puts 20 to 30% more material on the joint's +x side, where the beam's
moment is 150 kNm against 80, and the joint comes out symmetric along y, where the beams balance.

**Speed**, `./speed.sh` (2026-11-05, the M2 Pro's 10 cores, OpenSeesLatest-Mumps): each run alone, with an OpenSees of
its own started inside the clock; SIMP allowed 200 iterations, BESO and Multi-Material 300. Each cell is the whole run,
its iterations, and in brackets when its compliance stayed within 1% of where it ended. The bridge at 8 192 bricks is a
repeat: its first SIMP and BESO runs met a busy machine, a solve 0.71 and 0.45 s against 0.41 s. `speed.py` prints the
full tables, start-up, solves, updates and the rest apart, and points out runs like those.

| Model | Bricks | A solve | SIMP | BESO | Multi-Material |
| --- | --- | --- | --- | --- | --- |
| Bridge block | 1 024 | 0.05 s | 2.6 s, 44 (1.9 s) | 4.9 s, 92 (4.9 s) | 18.9 s, 267 (17.7 s) |
| | 3 456 | 0.16 s | 13.4 s, 77 (5.3 s) | 16.7 s, 102 (16.3 s) | 59.3 s, 263 (53.7 s) |
| | 8 192 | 0.41 s | 48.9 s, 109 (14.0 s) | 36.3 s, 90 (32.3 s) | 141 s, 260 (136 s) |
| | 16 000 | 0.82 s | 108 s, 123 (26.2 s) | 78.0 s, 95 (69.1 s) | 293 s, 260 (284 s) |
| Truss node | 1 072 | 0.06 s | 4.6 s, 70 (2.1 s) | 13.9 s, broke after 76 | 24.2 s, 270 (22.2 s) |
| | 8 576 | 0.46 s | 98.7 s, 200 (25.5 s) | 55.7 s, 121 (48.8 s) | 159 s, 260 (152 s) |
| | 28 944 | 2.0 s | 435 s, 200 (90.6 s) | 241 s, 119 (215 s) | 638 s, 260 (616 s) |
| Beam-column joint | 1 128 | 0.06 s | 3.6 s, 54 (1.9 s) | 7.6 s, 123, see below | 21.8 s, 260 (20.8 s) |
| | 9 024 | 0.49 s | 83.9 s, 160 (11.5 s) | 45.4 s, 92 (39.5 s) | 181 s, 264 (172 s) |

* A solve takes as long whichever method asks for it - within 3% on every model - so the methods differ in how many
  iterations they make and in their updates. OpenSees took 87% of the 50 minutes.
* SIMP is within 1% of its end after 21 to 51 iterations at every size - on the larger models a quarter of its run or
  less; the rest settles the densities. On the truss node it runs to its 200 without meeting top3d's test.
* BESO's update is next to nothing (6 ms at 29 000 bricks) and it makes 90 to 123 iterations: from about 8 000 bricks
  it is the quickest. Its design is there only near the end, as the volume comes down 2% an iteration.
* Multi-Material makes 260 iterations, the sharpening of its projection, and MMA takes a sixth to a third of its time
  (0.33 s an iteration at 29 000 bricks, two materials). With steel alone the update is less than half as long (6.0 s against
  14.8 s on the 3 456-brick bridge, 12.2 against 34.9 on the 8 576-brick node) and the run 17 to 18% shorter: the
  iterations make it slow, not the second material.
* BESO at 10 bricks a side: on the truss node it cuts a leg through after iteration 76 and says so. On the joint the
  compliance rose 4.3 times at iteration 109 (0.366 to 1.58), then a million times at 110, and came back to 2.9; the
  run reported converged, because the breakdown guard measured the mending against 1.58, the compliance after the
  first rise. The guard now measures against the lowest of the five before, and the joint broke after iteration 108.
