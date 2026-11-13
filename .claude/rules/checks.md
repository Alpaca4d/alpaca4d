# Checks

Keep checks to what changed (user, 2026-11-13, to save tokens):

- Run the tests of the area you touched (for example `Alpaca4d.Test/Footfall`), not every suite.
- Full sweeps only when asked: every test suite, every public example solved in Rhino, benchmark
  runs such as the footfall plate.
- A live-Rhino check only when mono tests cannot show it (native geometry, a solved definition), on
  the one definition that shows the change.
- Read long output through `tail` or a `grep` for the failures, not whole.
