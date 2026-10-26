# Deck errors

    ./run.sh

**OpenSees exits 0 when a deck stops on a Tcl error**, just as when the deck runs to its end: a
command it does not know, an element type it lacks, a material that was never defined. Every build
tried does - 3.8.0, the 3.8.0 Mumps build and master. Its `main` (SRC/tcl/tclMain.cpp) prints Tcl's
trace of the error and sets the exit code to 1, then never uses it: it evaluates a buffer that still
holds the argument count instead of `exit 1`, and then `quit`, which exits 0.

So Run Analysis took any such deck for one that solved, with no results behind it, and Section
Optimiser took the run for a solved one too. `Model.RunOpenSees` now reads the trace off the error
stream (`Model.StoppedOnTclError`) and hands on the 1 OpenSees meant; every component that runs
OpenSees goes through it.

## The trace

Tcl ends it on the line of the deck that stopped, with one such line for every file and procedure it
was called from:

    invalid command name "nonsense"
        while executing
    "nonsense 1 2 3"
        (file "AlpacaModel.tcl" line 5)

A path longer than 150 characters is cut short with `...` inside the quotes. Only Tcl prints these
lines, and only for an error nothing caught - `catch {system Mumps ...}` in Alpaca's own decks prints
nothing. A deck that runs into `exit 3` keeps its 3, and one whose `analyze` does not converge still
exits 0: analyze returns its error flag to the deck, which prints `ALPACA_ANALYZE_RESULT`.

## What runs

| | |
|---|---|
| the trace | read off logs as OpenSees printed them: an unknown command, an OpenSees command that refuses, in a sourced file, in a procedure, a path cut short, a Windows path with CRLF line ends - and not off a log that did not converge, nor the same words printed by a deck at the start of a line |
| decks | on every OpenSees found (`OPENSEES`, the one on PATH, /Applications/OpenSees*/), through `Model.RunOpenSees`: one that solves and one that does not converge come back 0; an unknown command, an unknown element, an undefined material, an error in a sourced file or a procedure, and one down a 260 character path with spaces come back 1, while OpenSees itself exits 0; a caught error 0, `exit 3` 3 |
| Run Analysis' verdict | `AnalysisOutcome`, compiled in from its source, on the logs of three of those decks: the one that solves is solved, the one that does not converge says so - OpenSees prints the result line after analyze to its error stream, where it was not looked for - and the unknown command says OpenSees stopped |
| Moment Curvature | which read any non-zero exit as its axial step failing: its deck with an error put at the top says it stopped on an error |

The checks point the plugin's user folder at the folder they run in, so the solver path they set
lands in its settings.json and the user's is left alone.
