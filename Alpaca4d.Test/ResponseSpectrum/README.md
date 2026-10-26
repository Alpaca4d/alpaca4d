# Response spectrum analysis

    ./run.sh          # without Rhino
    ./grasshopper.sh  # in a running Rhino 8 with its script server started (StartScriptServer) and Grasshopper loaded

Three new components and the reading that goes with them:

| | what it is |
|---|---|
| **Design Spectrum** | a switcher, one unit per code: Generic (EN 1998-1, recommended values, the default), NTC 2018, Custom |
| **Mass from Loads** | the weight of loads as mass, with a factor (ψ2); Assemble Model lumps it onto the nodes the loads land on |
| **Response Spectrum Analysis** | `eigen`, `modalProperties`, then OpenSees' `responseSpectrumAnalysis` mode by mode, at Sa = 1 |

OpenSees records one step per mode and direction and combines nothing. `SpectrumCases`, carried on the
model the analysis hands on, combines every quantity as `Read` reads it - CQC or SRSS over the modes, each
mode scaled by the spectrum at its own period, then 30% or SRSS over the directions - so every result
component reads the analysis without knowing it is one. Its steps are E, then Ex, Ey, Ez.

## run.sh

`SpectrumCheck.cs`, compiled with mono:

1. EN 1998-1 and NTC 2018 spectra against values worked out by hand from the codes' expressions - elastic,
   design, vertical, other damping - and what is refused: ground S1, agR in m/s², damping as a ratio.
2. Custom spectra, and the sampled curve.
3. The CQC correlation against NTC 2018 [7.3.4] and Der Kiureghian's form, and the combinations.
4. The deck: one mode on ARPACK is solved as two (ARPACK hands back a zero for one), and the message when
   ARPACK runs short of masses.
5. A run: a column of three storeys, its two sway modes close and turned 30° off the axes, so each answers
   both directions and CQC is 22% above SRSS. Each mode's base reaction at Sa = 1 is the mass it moves in
   the modal report; the combined base reaction read through `Read` is the CQC of Meff·Sa; a beam moment is
   the CQC of the modes' own moments; E is the 30% rule over Ex and Ey.

## grasshopper.sh

`GrasshopperChecks.cs` builds definitions out of the real components and solves them headless, as the
Truss suite does:

* Design Spectrum, each unit, and its error for a ground type it does not know.
* A frame - four columns, four beams, a slab - loaded on its beams, its slab and its corners. Mass from Loads
  turns the loads into mass, and Assemble Model has to count it and add it up per node: one `mass` line per
  node. Two masses on one node used to leave only the last, because `mass` sets a node's mass rather than
  adding to it.
* The frame shaken along X (generic) and Y (NTC 2018), read through Reaction Forces, Nodal Displacements and
  Beam Forces: the mass being symmetric, the supports' peaks add up to BaseShear in each direction.
* A static Run Analysis beside the spectrum analysis, on the same model: each keeps its own files.
* What should warn or fail: one mode, too few for NTC's 85%; a horizontal spectrum on Z; twelve modes on
  ARPACK of a frame with twelve degrees of freedom carrying mass; a model fed from Run Analysis.
