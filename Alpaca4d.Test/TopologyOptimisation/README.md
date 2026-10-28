# Topology Optimisation

    ./run.sh                         # without Rhino: the filter, the surface, OpenSees kept open, a small run
    ./run.sh bench cantilever        # top3d's cantilever, against reference.py
    ./run.sh bench simply-supported  # a simply supported beam, the same way
    ./run.sh bench cantilever-20k    # 19 200 bricks, for the time it takes
    ./run.sh bench bridge            # Tools/Examples/TopologyBridge.gh's model (bridge-<n>: n bricks per metre)
    ./grasshopper.sh                 # in a running Rhino 8 with its script server started (StartScriptServer)
    ./example.sh                     # Tools/Examples/TopologyBenchmarks.gh's definition, built and run there

Topology Optimisation finds where a solid needs its material: minimum compliance by SIMP, with the
optimality criteria update and a density filter, as top3d (Liu and Tovar 2014, "An efficient 3D
topology optimization code written in Matlab", SMO 50:1175-1196) does it. Every solid has a density
x and a modulus E = Emin + x^3 (E0 - Emin), with Emin a billionth of E0: removed material is never
cut out, so the mesh never changes and no node is ever left loose.

## Which elements

| Element | Solved as | Why |
|---|---|---|
| SSP Brick | `stdBrick`, OpenSees' eight point brick, on the same nodes | SSPbrick works out its stabilisation stiffness once, in `setDomain`, from the modulus it starts with (`SSPbrick::GetStab`). A modulus set afterwards reaches only the part of the stiffness that comes from the strain at its centre, so a void brick would stay stiff against every mode the stabilisation holds. `stdBrick` integrates at eight points, each with its own copy of the material, so it follows a new modulus entirely - and it is the brick top3d optimises with. |
| Ten Node Tetrahedron | `TenNodeTetrahedronStd`, as it is | Passes a modulus to its four integration points. Needs an OpenSees from master since 16 October 2026, as Run Analysis does. |
| Four Node Tetrahedron | `FourNodeTetrahedron`, as it is | The same; hidden from the toolbar, but old definitions still hold it. |

Every solid needs an Elastic Isotropic Material: the modulus is what is scaled.

## How it runs

One OpenSees stays open for the whole run (`LiveOpenSees`). The deck builds the model, sets up a
static analysis in one step solved by `Linear`, and ends in a loop that reads a line from standard
input, evaluates it and answers `ALPACA_DONE` or `ALPACA_FAILED <error>` on standard error (standard
output into a pipe is buffered). Each iteration (`TopologyAnalysis`):

1. writes every element's modulus to a file;
2. `alpacaSolve` reads it, `setParameter -val E -ele <tag> E` on each element - which every solid
   passes to all its integration points, and each element holds copies of its material of its own, so
   one material line serves all of them - then `reset` and `analyze 1`;
3. records the displacements and the element forces with binary recorders, `record`, and removes them;
4. the energy of an element is u_e . f_e, its nodal displacements against its resisting forces, which
   is u_e . K_e u_e for any linear element whatever its formulation.

The optimiser (`TopologyOptimiser`) is top3d step for step, with two generalisations: elements may
differ in size, so volumes and the filter count by element volume, and some may be kept solid (Keep).
top3d's bisection runs between 0 and 1e9, which fits its unit modulus and loads; here the
sensitivities are scaled to a largest ratio of one first, so that a model in kN and m bisects as well.

## What run.sh checks

* **Density filter**: on a grid of cubes the 19 neighbours and weights of top3d's H; a uniform field
  filters to itself; `Back` is the transpose of `Apply`; neighbours weigh by volume.
* **Density surface**: the iso-surface (`DensitySurface`, marching tetrahedra over node and centre
  densities, capped where the material meets the domain's edge) is closed - every edge in two
  triangles, once each way - for a solid domain (exactly its volume), a block inside it, a block
  touching its side, a random field, and tetrahedra; including nodes that sit exactly on the threshold.
* **The pieces of a design** (`SolidPieces`): a brick on its own is left out and the held block kept; a
  staircase of bricks meeting along edges is joined, one cell a step, with the denser of the two that
  join each step face to face, and a brick meeting the block at one corner in two steps; a block pinned
  at one node, or along one edge, or on rollers at both ends is left out, one pinned at three corners or
  pinned at one end and on rollers at the other held; a brick a beam stands on is kept; tetrahedra
  sharing a face are one piece, sharing an edge two.
* **OpenSees kept open**: a command answers; a failing one answers with its error and OpenSees stays
  open with its state; a deck that stops before its loop is refused with what OpenSees printed.
* **Element energies**: with uneven moduli the energies add up to F.u to 1e-9; halving every modulus
  doubles every energy; solving a design again gives the same energies; the solid cantilever is near
  beam theory.
* **A small run** (24 x 8 x 2, the column under the load kept): 30% of the volume, the kept elements at
  1, the compliance falling 19163 -> 3388 in 57 iterations, and the densities away from grey.
* **The design, analysed**: the small run's design at 0.5, its 13 staircase steps joined (131 elements),
  solid at full stiffness and void at a billionth: it carries the load, at a compliance of 5955 for the
  optimiser's 3388 - of the order, where without the joints its hinges make it a mechanism at 2.4e10.
  `./run.sh bench <name>` analyses its design the same way: the cantilever's comes out at 1798 for 2416,
  the simply supported beam's at 904 for 1203 and the bridge's at 0.0031 for 0.0043 - stiffer by a
  quarter, as the grey elements kept get their full stiffness - with one cell joined on the cantilever,
  nothing left out, and the pieces found in 10 to 20 ms.
* **Going on**: a run of 15 iterations taken on to 40 in a new OpenSees, from its design densities
  (`TopologyResult.Design`), is the run of 40 to the last bit, compliance and densities; it analyses only
  iterations 16 to 40, and taken on to where it already is, nothing.
* **Every iteration's design** (`DensityHistory`): frames read back to within half a byte (0.002), in
  any order, whole frames every 16th and changes between; a run of 12 keeps 13 frames, frame 0 the
  uniform start and frame k the design after iteration k; going on carries them on, leaving the first
  run's alone. `./run.sh bench <name>` prints how small the frames are: the cantilever's 201 in 92 KB
  (doubles 7.4 MB, bytes 942 KB, each byte frame deflated whole 342 KB), the tower's 44 in 19 KB.

## The benchmarks

`./run.sh bench <name>` runs one with the OpenSees given and writes `<name>_history.csv` and
`<name>_densities.csv` to `./results` (or `RESULTS`); then `reference.py`, when `PYTHON` has numpy and
scipy, runs top3d on the same problem with its own brick stiffness (2x2x2 Gauss), its own assembly and
SciPy's solver, and compares. `--plot` draws both designs as the mean density through the depth.
Results on an M2 Pro with /Applications/OpenSeesLatest-Mumps, 2026-10-26:

| | cantilever, top3d(60,20,4,0.3,3,1.5) | simply supported, 120 x 20 x 4 |
|---|---|---|
| bricks, unknowns | 4 800, 19 215 | 9 600, 38 115 |
| first compliance, against the reference | 7.7e-12 apart | 5.9e-12 apart |
| largest gap over the run | 2.1e-3 (iteration 68) | 2.2e-8 |
| final compliance, 200 iterations | 2415.8598 / 2415.8216 | 1203.02627 / 1203.02630 |
| final densities | mean 4.5e-4 apart | largest 8.1e-5 apart |
| analysis per iteration | 0.21 s | 0.43 s |

The first compliance agrees to round-off: OpenSees' `stdBrick` is top3d's brick. The cantilever's run
goes through a rearrangement around iteration 70 (the change jumps to 0.13) in which round-off between
two solvers grows for a while; both come out at the same design. Neither run reaches top3d's
tolerance of 0.01 within 200 iterations - top3d's own runs of these do not either.

**cantilever-20k**, 80 x 8 x 30 = 19 200 bricks and 67 800 unknowns: 0.94 s of analysis and 0.06 s of
update per iteration, 200 iterations in 3 min 20 s, OpenSees started once (0.3 s).

## What grasshopper.sh checks

In a running Rhino 8 (`RHINO_PIPE=rhinocode_remotepipe_<pid>` picks one), with this build of the plugin
loaded beside the installed one, a license copied from `LICENSE` (the user's by default, read only)
and the user folder pointed at the test folder:

* The small cantilever drawn as 384 eight vertex meshes through SSP Brick, Elastic Isotropic, Support,
  Point Load, Assemble Model and Topology Optimisation, with Keep naming the column under the load:
  the first compliance is the hand-written deck's to 1e-12, and the whole run is (57 iterations, to
  1e-11); Keep stays at 1; the surface is one closed mesh; the deck has `stdBrick` lines only and no
  result files are left. Run off keeps the result, and on again leaves a run that converged as it is.
  Iterations lowered to 20 starts again and stops at 20, saying Iterations can take it on; raised to
  200, the run goes on from the 21st - the first 20 iterations are the same objects - and converges
  where the run of all of them did. A model assembled again starts a new run.
* A model with Gravity warns that the self-weight stays on the emptied elements.
* Bricks propped by a truss: the component warns that the truss takes no part - it keeps its stiffness
  and its energy is not in Compliance - and optimises the bricks.
* 576 Ten Node Tetrahedra (OPENSEES_TET10, default /Applications/OpenSeesLatest-Mumps): the run, 30%
  of the volume, a closed surface, `TenNodeTetrahedronStd` in the deck; and on the OpenSees given for
  the bricks, when it is 3.8.0, the run ends with `TenNodeTetrahedron.SolverTooOld`'s explanation.

The component's run goes on a thread of its own, so the checks wait for it, then solve the component
alone, as the run does when it asks for a solution.

## What example.sh checks

`Tools/Examples/TopologyBenchmarks.gh` is written by its script, which `make.sh` runs without Rhino.
`ExampleRun.py` runs the same script's `build(doc)` inside Rhino 8, with its components made from this
build of the plugin, solves the definition, switches both Run toggles on with Iterations at 30
(`ITERATIONS`), and compares each problem's compliance at that iteration with the test bench's history
in `RESULTS`: 1.1e-11 and 2.4e-11 apart on 2026-10-27, in 16 s for both runs together. Then its Step
slider: 0 shows the uniform start, 10 a design between, and its end the latest again.

## The papers

* SIMP: Bendsøe 1989, "Optimal shape design as a material distribution problem", Structural Optimization
  1:193-202; Bendsøe and Sigmund 1999, "Material interpolation schemes in topology optimization", Archive
  of Applied Mechanics 69:635-654.
* The modified SIMP interpolation E = Emin + x^p (E0 - Emin), the optimality criteria update with its move
  limit and bisection, and the density filter as written in code: Sigmund 2001, "A 99 line topology
  optimization code written in Matlab", SMO 21:120-127; Andreassen, Clausen, Schevenels, Lazarov and
  Sigmund 2011, "Efficient topology optimization in MATLAB using 88 lines of code", SMO 43:1-16.
* The density filter itself: Bruns and Tortorelli 2001, CMAME 190:3443-3459; Bourdin 2001, "Filters in
  topology optimization", IJNME 50:2143-2158.
* In three dimensions, the code followed step for step: Liu and Tovar 2014, "An efficient 3D topology
  optimization code written in Matlab", SMO 50:1175-1196.
* The measure of non-discreteness 4x(1-x): Sigmund 2007, "Morphology-based black and white filters for
  topology optimization", SMO 33:401-424.
* The surface: marching tetrahedra, Doi and Koide 1991, IEICE Transactions E74:214-224.

Alpaca4d's own additions, not from these: element volumes in the filter and the volume constraint, so
unequal elements count by size; passive elements (Keep); the bisection on sensitivities scaled to one;
and the element energy taken as u_e . f_e from OpenSees' element forces rather than from an element
stiffness matrix of its own.
