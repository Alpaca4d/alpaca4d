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
* a model marked analysed with nothing to go back to is refused by both, with the message.

Section Optimiser needs a licence: give it with `LICENSE=`.
