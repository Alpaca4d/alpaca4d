# Documentation benchmarks

    "/Applications/Rhino 8.app/Contents/Resources/bin/rhinocode" script benchmarks.py
    python3 tables.py

Rebuilds the benchmarks of the documentation - `doc/benchmark/` in the alpaca4d.github.io
repository: Simple Beam, Simple Shell and Simple Brick - from scratch, and the numbers in their
tables.

`benchmarks.py` runs inside a running Rhino 8. It builds each benchmark as a Grasshopper definition
out of Alpaca4d's own components, plus Grasshopper's geometry, mesh and maths (Line SDL, Divide
Curve, Shatter, End Points, Mesh Plane, Series, Move, List Item, Deconstruct Mesh, Bounds); saves it,
solves it, and reads the results back. `tables.py` is plain Python: it turns those results into the
tables the pages quote.

| benchmark | page | what | reference |
| --- | --- | --- | --- |
| `beam-simply`, `beam-cantilever` | Simple Beam | uniform load: shear, moment, deflection | beam theory, with Timoshenko's shear term |
| `beam-vibration` | Simple Beam | cantilever bar, first three bending frequencies | Euler-Bernoulli |
| `plate-pressure` | Simple Shell | ASDEA ST6, centre deflection | Navier's series |
| `plate-vibration` | Simple Shell | NAFEMS FV16, six frequencies | NAFEMS, and `fv16_reference.py` |
| `brick-cantilever` | Simple Brick | SSP bricks, end load, tip deflection | Timoshenko beam |
| `solid-plate-vibration` | Simple Brick | NAFEMS FV52, ten modes | NAFEMS |

Everything goes to `<temp>/alpaca4d-benchmark/` - on a Mac, `$TMPDIR/alpaca4d-benchmark/` of the
Rhino process - so nothing lands in a repository:

| file | what |
| --- | --- |
| `Alpaca4d_Benchmark_*.gh` | the definitions, built with the defaults: what `doc/.gitbook/assets/` publishes |
| `variants/` | the same definitions with another mesh, ν or support, for the tables' extra rows |
| `results.json` | every value read, and the reference it is compared with |

By default it runs what the pages quote: every benchmark, plus the variants their tables show. A
`run.txt` in that folder narrows it - one entry per line, each a name from the table above, with
changes after a colon: `plate-vibration:n=32`, `brick-cantilever:nu=0`,
`solid-plate-vibration:n=16,layers=4`, `plate-pressure:support=soft`. Results are added to
`results.json`, so a narrowed run keeps the others. Nothing is put on the canvas, and Rhino's working
folder, which Run Analysis moves, is put back. A failure lands in `error.txt`, or in the benchmark's
entry of `results.json`.

Grasshopper needs the Alpaca4d plug-in loaded and a licence, since most of the models are over the
free element limit, and Run Analysis the OpenSees set in its settings. The script needs no
third-party components; a definition that holds some can stop Rhino on a missing-plug-in dialog, so
do not point this kind of script at one.

## Why the references are what they are

**Beams.** The textbook deflections are Euler-Bernoulli. Alpaca4d's elastic section includes shear
deformation (a rectangle's shear area is 5/6 of its area), so its beam is a Timoshenko beam: it
matches the textbook deflection plus the shear term ql²/(8GκA) - ql²/(2GκA) for the cantilever - to
six figures. In vibration the mass is lumped at the nodes, so the frequencies come out a little low
and rise as the elements get shorter - towards the shear-deformable bar's, which sit 0.02%, 0.17% and
0.40% below Euler-Bernoulli's for the first three modes.

**Plate under pressure.** The edges are held the way plate theory holds them: uz and the rotation
about the edge's in-plane normal. Leaving that rotation free - `support=soft` - gives the "soft"
support of a Mindlin shell, whose edge boundary layer a fine mesh resolves; the deflection then
converges above plate theory.

**FV16.** NAFEMS's targets are 1950s Ritz solutions (Young, Barton; Leissa 1973 for mode 6), upper
bounds up to 1% high. `fv16_reference.py` converges the same Kirchhoff problem by Rayleigh-Ritz
(needs numpy and scipy); its values are hard-coded in `tables.py` and agree with Sukhoterin et al.,
*American Journal of Applied Sciences* 13 (12), 2016.

**Brick cantilever.** Every node of the fixed face is held, which also stops the face's Poisson
contraction: about 1.4% stiffer than beam theory at ν = 0.3, 0.09% at ν = 0.

**FV52.** uz = 0 along the edges of the bottom face, nothing else, on NAFEMS's own 8 × 8 × 3 mesh
(the Abaqus benchmark input `nfv52i8f.inp` has the same). A line support in a solid softens without
limit as the mesh is refined, so the targets belong to that mesh. The three in-plane rigid-body
modes are left free, as NAFEMS has them, and come out at about 10⁻⁵ Hz.
