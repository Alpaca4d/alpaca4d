# Optimisation

    ./run.sh                 every algorithm
    ./run.sh Cobyla          one, by its name in OptimisationAlgorithm
    NLOPT=/path/to/libnlopt.dylib ./run.sh

Non-Linear Optimisation runs NLopt's derivative-free algorithms, ported to C# in
`Alpaca.Core/Optimisation` so that Alpaca4d needs no native library on Windows or on Mac. A
port is only worth the name if it does what NLopt does, so that is what is checked here. The
sources are compiled straight into the test, so neither Alpaca.Core nor Rhino is needed: only
`csc` (or `mcs`) and `mono`.

## What is checked here

* **The parts the algorithms share** - NLopt's random numbers, its sort and its red-black tree.
  3000 draws of `urand`, `iurand` and `nrand` from a seed, and the order the sort leaves equal
  keys in, are compared with NLopt's own when `NLOPT` is set.
* **What NLopt does around the algorithms** - an algorithm that takes no constraints refusing
  them, a start outside the bounds refused, variables whose bounds are equal never moved,
  NLopt's default first step.
* **Each algorithm against NLopt** (with `NLOPT` set) - every case is run through the port and
  through NLopt, and must evaluate the same points in the same order, end with the same result,
  the same best point and the same number of evaluations: Rosenbrock, a tilted bowl in 4D,
  Branin, Rastrigin in 3D, a pinned variable, maximising, a stop value, a stop from inside the
  objective, uneven first steps, absolute and relative tolerances, other seeds and populations,
  and NLopt's tutorial problem with its two constraints for COBYLA and ISRES.
* **Each algorithm on its own** - that it finds minima that are known, maximises, stops after as
  many evaluations as it is allowed, stops when asked from the objective and still returns the
  best it found, stops at a stop value and at a time limit, meets constraints, and never
  evaluates a point outside the bounds, which a slider could not take.

Where a check allows for an algorithm, it is because NLopt's own does the same: COBYLA gets only
to 5e-4 on Rosenbrock in 3000 evaluations, CRS2-LM evaluates its whole first population before it
looks at the stop flag, BOBYQA solves the bowl before 50 evaluations.

## Comparing with NLopt to the last bit

Two things decide whether NLopt and the port agree to the last bit rather than nearly:

* **The sort.** NLopt hands its sort to the C library's `qsort_r` on macOS and Linux, and uses its
  own copy elsewhere; they leave equal keys in different orders. The port has NLopt's own copy,
  so build NLopt to use it too.
* **Fused multiply-add.** Clang fuses `a * b + c` on Apple silicon unless told not to; C# does
  not. Build with `-ffp-contract=off`.

A build that does both, from NLopt's source (the port follows NLopt 2.11.0):

    git clone --depth 1 https://github.com/stevengj/nlopt.git
    mkdir nlopt-build && cd nlopt-build
    cmake ../nlopt -DCMAKE_BUILD_TYPE=Release -DCMAKE_C_FLAGS="-ffp-contract=off" \
      -DNLOPT_CXX=OFF -DNLOPT_PYTHON=OFF -DNLOPT_OCTAVE=OFF -DNLOPT_MATLAB=OFF \
      -DNLOPT_GUILE=OFF -DNLOPT_SWIG=OFF -DNLOPT_TESTS=OFF -DNLOPT_FORTRAN=OFF \
      -DBUILD_SHARED_LIBS=ON -DCMAKE_INSTALL_PREFIX=$PWD/../nlopt-install
    sed -i '' 's|^#define HAVE_QSORT_R|/* #undef HAVE_QSORT_R */|' nlopt_config.h
    make -j8 && make install
    NLOPT=$PWD/../nlopt-install/lib/libnlopt.dylib ../Alpaca4d/Alpaca4d.Test/Optimisation/run.sh

On Linux drop the `''` after `sed -i`; on Windows NLopt already uses its own sort, and MSVC does
not fuse by default. The library has to match mono's architecture.

## Test cases in Grasshopper

`NonLinearOptimisationCases.py` builds a Grasshopper document of eight cases, each with a known
answer, its optimiser already linked and set to the algorithm it is for, and a panel saying what
to expect. In Rhino 8 open it in the ScriptEditor and press Run (or `_-RunPythonScript` it); in
Rhino 7 use `_RunPythonScript`. Save the document it opens to keep the cases.

| case | algorithm | expect |
| --- | --- | --- |
| 1 Rosenbrock | BOBYQA | 0 at (1, 1) |
| 2 Branin | DIRECT-L | 0.3979 at one of three points |
| 3 A hill, maximised | Nelder-Mead | 5 at (2, -1) |
| 4 NLopt's tutorial, two constraints | COBYLA | 0.5443 at (0.3333, 0.2963) |
| 5 Outside the unit circle, one constraint | ISRES | about -1.414 at about (-0.707, -0.707) |
| 6 Rastrigin | CRS2-LM | 0 at (0, 0) |
| 7 A gene pool of 12 | Sbplx | 0, every gene at 0.3 |
| 8 Three set-ups to refuse or warn about | - | a warning, and two refusals |

## What is not checked here

Everything that needs Grasshopper running. Check these in Rhino 8, on Windows and on Mac:

* **Non-Linear Optimisation** is on the Alpaca4d tab under 12_Optimisation, in a group of its
  own after the Explorer components. Dragging from the Variables grip to a slider links it;
  Shift adds one, Ctrl removes one; right-click Variables for every slider or the selected ones.
  A Galapagos gene pool links too, one variable per gene.
* Dragging from Objective to a Number parameter on its own, and to the output of a component,
  links it; a component's input does not. Constraints link the same way, several at once.
* The optimiser is Alpaca light green with dark labels, lighter with a dark outline when
  selected (Grasshopper's own selection green would not show on it), and orange while it runs.
  Selected, it boxes what it is linked to: variables in light blue, the objective in purple, the
  constraints in orange. The links, the algorithm and the settings survive a save and reopen.
* Double-click opens the window. Start, on a definition that takes a moment to solve: Rhino stays
  responsive, the canvas and the viewport follow every half second, the chart fills in, and
  Stop, Esc on the canvas and Esc in the window each stop the run after the design being solved.
  The sliders end on the best design, and one Undo puts them back where they were.
* Each of the seven algorithms runs to its end; CRS2-LM and ISRES give the same run twice with
  the same seed. COBYLA and ISRES meet a constraint such as `utilisation - 1`; the others refuse
  to start with constraints linked, and say why.
* An objective with no value, or with a list, stops the run with a message naming the slider
  values that caused it. Deleting a linked slider while running stops the run.
* A slider with few decimals under a local algorithm brings up the warning, and repeated designs
  are counted as such rather than solved again. *Copy history* gives a CSV of every design.
* Without a license the links and settings can be made, but Start refuses with the license
  message; with one added from the License window, Start runs.
