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
