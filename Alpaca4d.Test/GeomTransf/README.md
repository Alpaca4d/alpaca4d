# GeomTransf

    ./run.sh          # without Rhino
    ./grasshopper.sh  # in a running Rhino 8 with its script server started (StartScriptServer)

The Beam and WithHinges types of the beam component take their transformation as a word on
GeomTransf - **Linear** (the default), **PDelta** or **Corotational**, typed or from a Value List,
which fills itself - and build it on the beam's own line and local z. A transformation made in code
still goes in as before.

**run.sh** compiles `GeomTransfCheck.cs` with mono and runs it:

* the words `GeomTransf.TryParseType` takes: the three names in any case, `P-Delta`, `Corot`; and
  that nothing else passes;
* the `geomTransf` line each type writes;
* a deck for OpenSees around that line: a cantilever of 20 elastic beams bent by an end moment of
  2πEI/L. Linear, its tip rises πL and turns 2π, the small-displacement answer. Corotational, it
  curls into a full circle: the tip comes back to the root, turned 2π, and the middle node is the
  20-gon's diameter away.

**grasshopper.sh** builds definitions in code out of the real components and solves them in Rhino
(`GrasshopperChecks.cs`; `GHDIR=` a Release build, `RHINOPIPE=rhinocode_remotepipe_<pid>` to pick
the Rhino):

* Beam and WithHinges, each word and none: the element's transformation, on the same local z as an
  empty input gives;
* a transformation made in code goes in as it is, with its own local z;
* `Elastic` gives no element and one error naming the three words;
* a Value List wired to GeomTransf fills itself with the three, as a drop-down, and picking
  Corotational gives a Corotational beam, with no error or warning anywhere;
* Assemble Model writes `geomTransf Corotational` for it.
