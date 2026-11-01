# Analysis files

    ./grasshopper.sh  # in a running Rhino 8 with its script server started (StartScriptServer)
                      # and Grasshopper loaded

The analysis components write their deck and recorder into `AlpacaResults`, next to the .gh file,
through `AnalysisFiles`. Their names said only which run of the component wrote them, so two Run
Analysis components in one document both wrote `recorder_0.mpco` and `AlpacaModel_0.tcl`. A result
component reads the recorder its model points at whenever it solves, so after the second analysis
the first model's results, read again, were the second's, with no message. The names now carry the
start of the component's InstanceGuid as well: `recorder_f51ab6ac_0.mpco`.

`GrasshopperChecks.cs` checks, out of the real components, two cantilevers alike but for their tip
load, 10 kN and 30 kN, each with a Run Analysis and a Nodal Displacement of its own:

* the two write different recorders and decks, named after their own components, in
  `AlpacaResults`;
* the 30 kN tip deflects three times as far as the 10 kN one;
* the 10 kN cantilever's deflection read again, alone, is still its own, and still is after the
  30 kN one has been analysed again. With the shared names it read 0.0009858, the 30 kN tip's.

Run against the build before the fix (`GHDIR=` its folder), the file checks and both read-again
checks fail.
