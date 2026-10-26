# Multi-core OpenSees

OpenSees 3.8.0 with one thing added: `system Mumps` in the ordinary, single-process
`OpenSees` executable. MUMPS is a multifrontal sparse direct solver; built sequential (no MPI)
with OpenMP, it factorises the stiffness matrix on several cores inside the one OpenSees process
Alpaca4d already starts. The deck, the recorders and the MPCO file are the same as ever, and the
user installs nothing besides OpenSees itself.

    system Mumps -matrixType 1 -numThreads 4     ;# symmetric, four cores
    system Mumps -matrixType 1 -numThreads 0     ;# every core

Alpaca4d writes this for System **Mumps**, and for **Auto**, the default (see
`Alpaca.Core/Analysis/Analysis.cs`, `SystemEquation`). On an OpenSees without it, the official one
for instance, the deck says so in the log and solves with BandSPD: it never stops on it.

## Why not OpenSeesSP / OpenSeesMP

Those are the OpenSees that solve on N cores upstream, and they need MPI: an MPI runtime installed
on every user's machine, `mpiexec -np N` to start them, and an MPCO file per process
(`*.part-0.mpco`, ...) that Alpaca4d's reader would have to merge. OpenSeesMP runs several
analyses side by side rather than one faster. Threads in one process need none of that.

## Files

| file | what it is |
| --- | --- |
| `opensees-v3.8.0-mumps.patch` | the OpenSees changes, against tag `v3.8.0` |
| `mumps-openmp.patch` | lets the MUMPS CMake build (OpenSees/mumps) use OpenMP without MPI |
| `build-mac.sh` | builds it on a Mac (Apple silicon) |
| `build-windows.bat` | builds it on a Windows machine with VS 2022 and Intel oneAPI (not run yet) |
| `../../.github/workflows/opensees-multicore.yml` | builds and tests both on GitHub Actions |

What the OpenSees patch changes:

- **CMake**: `-DMUMPS_SEQ_DIR=<MUMPS install>` compiles the MUMPS solver into the `OpenSees`
  target and links the static MUMPS, its OpenMP runtime (libgomp on a Mac, linked in; Intel's
  libiomp5md on Windows, a DLL that sits beside OpenSees.exe) and the LAPACK
  OpenSees already uses (Accelerate, MKL).
- **`system Mumps`** compiles without MPI, and takes `-numThreads N` (MUMPS's `ICNTL(16)`).
- **MumpsSolver**: the sequential communicator; the controls set *after* MUMPS's `JOB=-1`, which
  resets them all - upstream set them before, so `-ICNTL7` and `-ICNTL14` never took effect;
  tree parallelism (`KEEP(401)`, the leaves of the elimination tree on separate threads) switched
  on whenever more than one thread is allowed - MUMPS 5.5 leaves it off, 5.6 turned it on by
  default; a failed factorisation no longer marked as factored; indices put back on every error
  path; the debug prints removed.
- **MumpsSOE**: each column's rows sorted once and found by binary search when assembling
  (it was an insertion sort and a linear search); a leak.

And the MUMPS one: OpenSees/mumps only enables OpenMP inside its MPI branch, so a sequential
build silently had none. The patch enables it for Fortran alone - MUMPS's C files make no OpenMP
calls, and MSVC's `/openmp` would pull in a second runtime (vcomp) beside Intel's.

## Building

**GitHub Actions** - push a branch with `Tools/OpenSees/**` changed, or run *Multi-core OpenSees*
from the Actions tab. Each platform is built, checked (`SolverChoice/correctness.py`, then
`multicore.py` on small and large models) and uploaded as an artifact laid out like
an OpenSees install: `bin/` and `lib/tcl8.6/init.tcl`. The Windows job also
fails if `OpenSees.exe` asks for any DLL but Windows' own and `libiomp5md.dll`; the Mac one if
`OpenSees` needs anything macOS does not have.

**A Mac** - `Tools/OpenSees/build-mac.sh [work dir] [out dir]`. About 20 minutes the first time.
Homebrew's libgfortran and libgomp are built for the macOS Homebrew runs on, so the linker warns
that they are newer than the macOS 13 the executable targets. OpenSees' own CMake links the
gfortran runtime from Homebrew in the same way. What they add to the executable's imports are
long-standing libSystem calls (`getentropy`, `posix_memalign`, `pthread_*`, ...), nothing newer
than macOS 13 has.

**A Windows machine** - `Tools\OpenSees\build-windows.bat [work dir] [out dir]`, from the
*x64 Native Tools Command Prompt for VS 2022* with Intel oneAPI (oneMKL, ifx).

## How much quicker

`Alpaca4d.Test/SolverChoice/multicore.py`: one static step with Newton, as Run Analysis does, every
answer checked against UmfPack's (or one-core Mumps' where UmfPack fails). Apple M2 Pro, 10 cores,
macOS 26:

| model | DOF | BandSPD | UmfPack | Mumps, 1 core | Mumps, all cores |
| --- | ---: | ---: | ---: | ---: | ---: |
| frame 5×5×5 | 1 080 | 2 ms | 7 ms | 5 ms | 8 ms |
| shell 30×30 | 5 046 | 51 ms | 73 ms | 59 ms | 60 ms |
| brick 8³ | 1 944 | 14 ms | 32 ms | 16 ms | 21 ms |
| frame 15×15×15 | 23 040 | 0.47 s | 0.80 s | 0.23 s | 0.20 s |
| shell 120×120 | 84 966 | 4.07 s | 3.18 s | 2.15 s | 1.89 s |
| brick 24³ | 45 000 | 17.0 s | 5.89 s | 1.14 s | 0.92 s |
| frame 25×25×25 | 101 400 | 13.1 s | **fails** | 4.57 s | 3.47 s |
| shell 200×200 | 237 606 | 49.0 s | 12.8 s | 6.60 s | 5.40 s |
| brick 36³ | 147 852 | **> 6 GB** | **fails** | 6.36 s | 4.71 s |

- On a small model everything is a few milliseconds, and Mumps is as quick as BandSPD.
- On a large one Mumps is 2 to 18 times quicker than BandSPD, and needs less memory: on one core
  1.1 GB against 2.6 for frame 25, 1.2 against 5.0 for shell 200. Every core takes a little more
  (1.8 and 1.6 GB).
- UmfPack, in OpenSees, runs out of memory on the 100 000-DOF frame and the solid (UMFPACK's
  numeric phase returns -1, on the official OpenSees too), which is why an OpenSees without Mumps
  falls back to BandSPD and not to UmfPack.
- On a Mac most of the gain is MUMPS itself: Accelerate runs its dense kernels on the matrix
  coprocessor, shared by a cluster of cores, so every core takes off at most a quarter of the
  one-core time on a large model (12-26 % above), and a few milliseconds go on starting the
  threads on a small one.

The Windows build, timed by the workflow on GitHub's windows-2022 runner (4 virtual cores), with
every answer the same as UmfPack's:

| model | DOF | BandSPD | UmfPack | Mumps, 1 core | Mumps, all cores |
| --- | ---: | ---: | ---: | ---: | ---: |
| frame 5×5×5 | 1 080 | 7 ms | 17 ms | 11 ms | 43 ms |
| shell 30×30 | 5 046 | 135 ms | 174 ms | 132 ms | 160 ms |
| brick 8³ | 1 944 | 40 ms | 62 ms | 38 ms | 51 ms |
| frame 15×15×15 | 23 040 | 1.01 s | 1.06 s | 0.55 s | 0.45 s |
| shell 120×120 | 84 966 | 11.6 s | 6.4 s | 4.4 s | 4.2 s |
| brick 24³ | 45 000 | 30.9 s | 9.1 s | 3.0 s | 2.4 s |

The same picture as on the Mac: 2 to 13 times quicker than BandSPD on a large model. On the
runner's four virtual cores the threads take off up to a quarter, and starting them costs about
35 ms on a small model; a desktop with more physical cores is still to be measured.

Mumps with `-matrixType 1` factorises LDLᵀ without pivoting. It gave the exact answer on the
truss chains of `correctness.py` (1 to 40 DOF, where SparseSPD fails), the same tip as every other
solver on a fibre cantilever pushed past yield, and solved a P-Delta column at 1.5 times its Euler
load - an indefinite matrix - where BandSPD stops (analyze returns -3).

## Getting it to users

Alpaca4d does not ship OpenSees: the user installs it and points Alpaca4d at it (Alpaca4d →
Settings → Set OpenSees Executable), and this repository keeps no OpenSees. Build this one, or
take the workflow's artifact, and install it beside the official one, in `/Applications/OpenSees3.8.0-Mumps/{bin,lib}` on a Mac
say, keeping `bin` and `lib` side by side. Upstream would be better: the patch is small, and with
it the official OpenSees would have `system Mumps` for everyone.
