# Mass from Loads

    ./grasshopper.sh  # in a running Rhino 8 with its script server started (StartScriptServer)

Mass from Loads makes the weight of loads into mass: the downward part of a point, beam or shell load,
times a factor, over g, on every axis, on the nodes the load lands on. The loads are given themselves,
or by the Name of the Load Pattern that holds them, which Assemble Model looks up among its patterns.
Assemble adds the mass to the Mass Points node by node and writes one `mass` line per node.

`GrasshopperChecks.cs` builds every model out of the real components, solves it as the canvas does,
and runs in Rhino through `GrasshopperRun.cs`, because RhinoCommon cannot make a curve or a mesh outside
it. It installs the test licence first (`LICENSE=` to name another file); `PIPE=` picks the Rhino when
more than one is open. Build Alpaca4d.Gh in Release anywhere (`GHDIR=`) - the live Grasshopper folder is
not needed.

| model | what it pins down |
|---|---|
| a steel beam, 8 m, 300 x 300, 16 elements, simply supported, 5 kN/m, at Factor 1 | Assemble's Mass is the steel plus 5 kN/m over g; one `mass` line per node, a whole element's share inside and half at the ends; every frequency falls by √(m / (m + w/g)) exactly, since the load's mass lies where the steel's does; the first is π/2L² √(EI/m) within 1%; assembled again by `WithBeamSections` (Section Optimiser) the mass is the same, not doubled |
| a concrete slab, 6 x 4 m, 200 mm, 12 x 8 ASD shells, simply supported, 1.5 kN/m² finishes at Factor 1 and 3 kN/m² imposed at 0.1 (SCI P354) | Assemble's Mass; one `mass` line per node, an inner node carrying a whole cell; the first frequency falls by √(m / (m + q/g)) within 0.2% and is the plate's π/2 (1/a² + 1/b²) √(D/m) within 3%; Footfall Analysis reads both (its R is printed, not checked) |
| Mass from Loads alone | Gravity Load is left out with a remark; a load pushing up weighs nothing, and with nothing left there is a warning and no output; a slanted load keeps its downward part, with a remark; a negative Factor is an error |
| the beam, its loads in Load Patterns named "Imposed" (5 kN/m) and "Finishes" (2 kN/m and a Gravity Load), Mass from Loads naming "imposed" at 0.1 and " Finishes " at 1 | Load Pattern carries its Name; the mass is the steel plus 0.1 x 5 and 1 x 2 kN/m over g, whatever the case and spaces of the names; the same `mass` lines as the loads wired in; the Gravity Load in "Finishes" left out with a remark on Assemble; a name no pattern has is a warning on Assemble listing the names there are, and makes no mass |

Each section writes its name to `progress.txt` in the work folder as it starts, so a run that stalls
says where. A component made from Grasshopper's installed plugin (`ComponentServer.EmitObject`) in this
script deadlocked Rhino; whether a file saved before Load Pattern had a Name still opens was checked
instead by opening `Tools/Examples/Structures/TrussRoofHall.gh` with the new plugin installed: its
patterns kept their Factors (1.35, 1.5) and wires, with Name empty.

Last run 2026-11-12, all 27 checks passed.
