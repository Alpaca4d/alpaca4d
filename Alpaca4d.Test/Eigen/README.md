# Eigenvalues and modes from OpenSees

    ./run.sh

Checks `Alpaca4d.Eigen`: the eigen command Natural Vibration appends to a model, and the reading
of its eigenvalues back out of what OpenSees prints.

OpenSees sends Tcl's `puts` to its error stream, where the eigen solvers write too. Natural
Vibration used to take every word after the banner for an eigenvalue, so anything a solver said
broke it - and `-fullGenLapack` says something on most models, about its workspace, a complex
eigenvalue or one it cannot determine: *"The input string 'FullGenEigenSolver::solve()' was not in a
correct format."* The eigenvalues now go on a line of their own, behind `ALPACA4D_EIGENVALUES`,
and only that line is read, the C way whatever the machine's locale.

The first part feeds `Eigen.Read` what OpenSees prints - with the solvers' warnings, round-off
below zero, Tcl's `Inf`, under an Italian locale - and nothing at all. The second, when OpenSees is
found, solves a steel cantilever with each solver:

| solver | expected |
| --- | --- |
| `-genBandArpack` | six modes, the first a little under Euler-Bernoulli's 3.342 Hz for the lumped mass |
| `-fullGenLapack` | asked for 36 modes when the 30 masses give 30, it prints warnings among the output; all 36 are still read, the first six those of Arpack |
| `-symmBandLapack` | no eigenvalues: it solves only the standard problem, without the mass, which is why Natural Vibration turns it away before running |

It also checks the size the modes are read at. OpenSees records them mass-normalised,
φᵀMφ = 1, so their size went with the mass of the model, and a heavy one needed a deformation
scale of hundreds; `Read.NodalOutput` now gives each mode its largest translation 1, as a buckling
mode has, and its rotations the same factor. `Read.ModeScale` is checked on a few small tables,
and the cantilever, recorded as Natural Vibration records it, at its own mass and ten thousand
times it: as recorded the tip moves 3.18 and 0.0318, as read 1 on both, turning 0.2761 rad per
unit it moves either way.

`OPENSEES=/path/to/OpenSees ./run.sh` picks the build; otherwise the first on the PATH, then the
newest under `/Applications`. Passes with 3.8.0 and with master of October 2026; the checks before the
modes passed with 3.5.0 too.
