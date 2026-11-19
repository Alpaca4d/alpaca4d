# Buckling lengths

The buckling lengths the steel check works out from the model where the user gives none
(`Alpaca.Core/Design/BucklingLengths.cs`).

- `run.sh` (mono, no Rhino, no OpenSees): the structures the method was sketched on, meshed at
  0.5 m, against lengths worked out by hand: a floor grid, a two-storey frame braced in its plane,
  a portal unbraced and braced, a roof of purlins with and without a bracing bay, a beam with an
  overhang, an arch. Then the local axes (columns turned, a column turned halfway up, L_LT about the
  weak axis) and the time a 10-storey frame of about 25 000 beams takes, braced and unbraced.
- `grasshopper.sh` (a running Rhino 8 with `StartScriptServer`): a braced two-storey frame built out
  of the real components, its lengths through `BucklingLengths.Of(Model)`, and Model View's Buckling
  menu: Info, Values, the marks and labels, and pictures of the viewport about each axis,
  `buckling-about-z.png` and `buckling-about-y.png`, when the Rhino has a document open (one started
  with `open -na "Rhino 8" --args -runscript="_StartScriptServer"` has). `LICENSE=` names a licence
  file (the frame is past the free limit), `OUT=` a folder to keep the pictures in.
