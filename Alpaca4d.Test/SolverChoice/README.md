# Solver choice

Picks the two defaults on **Analysis Settings** that decide how long a run takes: `System`,
the storage and factorisation scheme, and `Algorithm`, the iteration scheme. Both used to be
chosen without measurement, and both were wrong.

| Setting | Was | Then | Is | Why |
| --- | --- | --- | --- | --- |
| `System` | `SparseSPD` | `BandSPD` | `Auto`: `Mumps` where OpenSees has it, else `BandSPD` | `BandSPD` was the fastest of the nine single-core solvers, and `SparseSPD` returns wrong answers; `Mumps` is quicker still, on several cores - see [Multi-core](#multi-core) |
| `Algorithm` | `ModifiedNewton` | `Newton` | `Newton` | `ModifiedNewton` fails outright once anything yields |

## Correctness comes first

`correctness.py` sweeps a chain of trusses whose tip displacement is `P n L / EA` exactly,
across small degree-of-freedom counts:

```
  BandGen          ..........
  BandSPD          ..........
  ProfileSPD       ..........
  SuperLU          ..........
  UmfPack          ..........
  SparseSYM        X.X.X.....
  SparseSPD        X.X.X.....
  SparseGeneral    ..........
  FullGeneral      ..........
  free DOF:        1 2 3 4 5 6 8 10 20 40
```

`SparseSPD` — the shipped default — diverges to `inf`/`nan` at 1, 3 and 5 free DOF on a
problem with a closed-form answer. It also **fails on a framed building with a rigid
diaphragm at each floor**, which is not an edge case at all: `analyze` returns −3 while
`BandSPD`, `UmfPack`, `BandGen`, `SuperLU`, `SparseGeneral`, `ProfileSPD` and `FullGeneral`
all return the same displacement to 13 significant figures.

That is an OpenSees defect rather than an Alpaca4d one, but it landed on exactly the small
models a new user builds first.

## Then speed

`bench.py`, on a 5×5×5-bay frame, a 30×30 shell slab and a 12³ brick block, timing the whole
`analyze` including assembly, averaged over ten solves:

| system | frame | shell | brick |
| --- | --- | --- | --- |
| **BandSPD** | **5.4 ms** | **309 ms** | **210 ms** |
| UmfPack | 21.0 | 400 | 525 |
| SparseSPD *(old default)* | 21.2 | 420 | 1500 |
| BandGen | 9.0 | 515 | 1011 |
| FullGeneral | 14.0 | 1004 | 1333 |
| ProfileSPD | 28.9 | 745 | 5906 |
| SuperLU | 28.8 | 1054 | 3662 |
| SparseGeneral | 28.7 | 1056 | 3664 |

End to end, old defaults against new, same answers to 12 significant figures:

| model | `SparseSPD` + `ModifiedNewton` | `BandSPD` + `Newton` |
| --- | --- | --- |
| frame | 22.6 ms | **6.4 ms** |
| shell | 428 ms | **318 ms** |
| brick | 1499 ms | **230 ms** |

`BandSPD` needs a symmetric positive-definite matrix, which is what a supported linear or
mildly nonlinear structural model gives. It was checked against `UmfPack` under both
constraint handlers Alpaca4d offers, with rigid diaphragms, and on corotational shells at
large displacement, and agreed every time. On a genuinely unstable model every solver,
`BandSPD` included, returns −3 and says so — nothing fails silently. `UmfPack` is the
fallback worth reaching for if a model ever refuses to solve, being a general sparse solver
that assumes nothing about the matrix.

## Why `Newton` and not the faster `Linear`

On elastic models every algorithm agrees, and `Linear` is about 20 % quicker (258 ms against
310 ms on the shell) because it does one solve and never checks a residual. On a fibre-section
cantilever pushed past yield:

| algorithm | result | tip displacement |
| --- | --- | --- |
| `Newton` | converged | 24.7958 |
| `NewtonLineSearch`, `SecantNewton`, `BFGS`, `Broyden` | converged | 24.7958 |
| `Linear` | **reports success** | **24.7944** — the unconverged first guess |
| `ModifiedNewton` *(old default)* | **failed** | 0.0616 |
| `KrylovNewton` | failed | 0.0616 |

`ModifiedNewton` holds the initial tangent, so once a section yields it cannot recover inside
the iteration limit. `Linear` is the more dangerous of the two: it returns a wrong answer and
reports success. `Newton` re-forms the tangent every iteration and is correct in both regimes,
which is worth 20 % on the models where the other two happen to agree with it.

Pick `Linear` deliberately when the model is elastic throughout and the run is long enough for
20 % to matter.

## Multi-core

`BandSPD` was chosen on models of up to a few thousand degrees of freedom, and there it is
right. On large ones it is not: its band grows with the model's width, and `multicore.py` times
it against `UmfPack` and against `Mumps` - a multifrontal sparse solver built into OpenSees
(`Tools/OpenSees`) that factorises on several cores - on small, large and very large frames,
slabs and solids. Apple M2 Pro, 10 cores, Newton, answers checked against `UmfPack`:

| model | DOF | `BandSPD` | `UmfPack` | `Mumps`, 1 core | `Mumps`, all cores |
| --- | ---: | ---: | ---: | ---: | ---: |
| frame 5 | 1 080 | 2 ms | 7 ms | 5 ms | 8 ms |
| shell 30 | 5 046 | 51 ms | 73 ms | 59 ms | 60 ms |
| brick 8 | 1 944 | 14 ms | 32 ms | 16 ms | 21 ms |
| frame 15 | 23 040 | 0.47 s | 0.80 s | 0.23 s | 0.20 s |
| shell 120 | 84 966 | 4.07 s | 3.18 s | 2.15 s | 1.89 s |
| brick 24 | 45 000 | 17.0 s | 5.89 s | 1.14 s | 0.92 s |
| frame 25 | 101 400 | 13.1 s | **fails** | 4.57 s | 3.47 s |
| shell 200 | 237 606 | 49.0 s | 12.8 s | 6.60 s | 5.40 s |
| brick 36 | 147 852 | **> 6 GB** | **fails** | 6.36 s | 4.71 s |

`Mumps` is as quick as `BandSPD` on a small model and 2 to 18 times quicker on a large one, in
less memory (on one core 1.1 GB against 2.6 for frame 25, 1.2 against 5.0 for shell 200). It is
right where it has to be: exact on every truss chain of
`correctness.py`, the same tip as every other solver on a fibre cantilever past yield, and it
solves a P-Delta column at 1.5 times its Euler load - an indefinite matrix - on which `BandSPD`
stops.

Would `UmfPack` have been the better single-core default? It is quicker than `BandSPD` on large
slabs and solids, but in OpenSees it runs out of memory on the 100 000-DOF frame and solid
(UMFPACK's numeric phase returns -1, on the official OpenSees too), where `BandSPD` is slow but
solves. So `Auto` falls back to `BandSPD` on an OpenSees without `Mumps`, and says so in the log.

On a Mac the extra cores take off at most a quarter of the one-core time on a large model
(12-26 % above), and cost a few milliseconds on a small one:
Accelerate runs the dense kernels on the matrix coprocessor, which a cluster of cores shares. On Windows MKL runs them on the cores themselves;
the *Multi-core OpenSees* workflow measures it there.

## Running it

    OPENSEES=/Applications/OpenSees3.8.0/bin/OpenSees ./run.sh

With the OpenSees from `Tools/OpenSees` as `OPENSEES`, set `OPENSEES_WITHOUT_MUMPS` to an
official one as well and `SystemChoice.cs` checks Auto's fallback on it; `MULTICORE=1` adds
`multicore.py` on small and large models (`python3 multicore.py --tiers "very large"` for the
rest, which takes a while and up to 6 GB).

Not wired into `Alpaca4d.sln` — it drives an external OpenSees, so it is a benchmark you run,
not part of the build. The single-core timings above the Multi-core section are from the
bundled macOS x86_64 binary of the time; the ranking is worth re-checking against the Windows
build before it is treated as settled there.
