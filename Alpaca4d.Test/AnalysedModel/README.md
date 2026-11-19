# Analysed model

    ./grasshopper.sh  # in a running Rhino 8 with its script server started (StartScriptServer)
                      # and Grasshopper loaded; LICENSE=... for Section Optimiser

An analysed model's deck ends in `wipe`, so Natural Vibration and Linear Buckling refused one, and
the modes or the buckling of a Section Optimiser's last design took putting beams on its Section
output and assembling them again. Run Analysis, Natural Vibration, Linear Buckling and Section
Optimiser now keep the model they were given (`Model.BeforeAnalysis`), and `Model.Unanalysed()`
gives it back, which Natural Vibration and Linear Buckling solve.

`GrasshopperChecks.cs` builds, out of the real components, a steel column 4 m tall in four beams,
IPE200 in S235, fixed at its foot, pushed down 200 kN and sideways 10 kN at its head, and checks:

* Natural Vibration and Linear Buckling after Run Analysis give the frequencies and load factors
  they give straight after Assemble Model;
* after Section Optimiser, which leaves the IPE200 (buckling lengths 8 m), they give those of its
  Section output on new beams, assembled again - the workaround - and not those of the IPE200;
* Natural Vibration after Linear Buckling (beams split in 4) solves the beams whole, and Linear
  Buckling after Natural Vibration gives the same load factors;
* a model marked analysed with nothing to go back to is refused by both, with the message;
* with a second pattern and load combinations, Linear Buckling and Pushover take the one named,
  and stop with an error listing them when none is;
* load combinations add up. A table, four columns and two beams in IPE200 under a slab of eight
  ASD shells, carries DL (self-weight, a line load on every beam, a load on every shell) and LL (a
  point load with a moment, a push sideways, a load on every shell). `DL + LL`,
  `1.35 DL + 1.5 LL` and `DL + -1 LL` give what `DL` and `LL` give, added with their factors, and
  `DL + LL` what the model without combinations gives. That holds for every node's displacement,
  rotation and reaction, the beams' forces at every station and the shells' forces, to 1e-6 of
  the largest value (it comes out near 1e-11). Each combination's reactions balance its loads;
* the result components read those combinations. Nodal Displacements, Beam Forces, Beam
  Stresses, Reaction Forces, Shell Forces, Shell Stresses, Node Forces and Utilisation, with
  Combination empty, give a branch per combination, its place first (`{combination}`,
  `{combination; tag}`, `{combination; step; tag}` with History), each holding what Core reads
  for it. Positions, elements and the like stay one flat list, except on Node Forces, whose
  outputs all share one tree. `ll` and `4` pick LL and Opposite on their own places, a name the
  model lacks is an error listing its names, and a model without combinations reads as before;
* a file keeps each parameter's description as it was saved: opened, or pasted in, a component's
  parameters take this version's descriptions (a switcher's those of its type), and another
  plugin's components are left alone.

Section Optimiser needs a licence: give it with `LICENSE=`.
