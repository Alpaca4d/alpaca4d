# View Results files

    ./run.sh

View Results' Component dropdown saves its choice as a position in its list. When σx - the direct
stress at each point of a beam's section, N, My and Mz together - joined the beam stresses, it went
in fourth, after the three stresses it is made of and where σmax used to be:

| position | before | now |
| --- | --- | --- |
| 3 | σmax | σx |
| 4 | σmin | σmax |
| 5 | τV | σmin |
| 6 | τT | τV |
| 7 | VonMises | τT |
| 8 | | VonMises |

So a definition saved before σx says 7 for Von Mises, which now reads τT. View Results marks the
files it saves (`BeamStressSigmaX`), and one opened without the mark has a beam stress Component
from 3 on moved one along.

## What is checked

The real component, saved and opened as a file is. An older file is made by taking the marks out
of what a newer one saved, which is all that tells the two apart:

- saved since: σx and Von Mises come back as themselves;
- saved before: σmax and Von Mises come back as themselves, one place further down the list;
  σMz, before where σx went in, and another family's Component - shell Von Mises - stay put;
- saved before the Result menu had its own order as well: the family is turned from its place in
  the enum into its place in the menu first, then the Component is moved.

Needs a Release build of `Alpaca4d.Gh`, mono, Grasshopper and RhinoCommon 7.18 from the NuGet cache,
and Rhino 7 for Mac for the Visual Basic runtime and the libraries Grasshopper loads - what
`ColourBy` needs.

## The deformation scale's range

A mode goes either way, so a natural vibration or buckling model wired in switches the Deformation
scale from 0.0 to 200.0 over to -2.00 to 2.00, and any other model switches it back.
Checked on the real component, with the model put where a solve puts it: the switch, a value
outside the new range brought back to 1, a value inside it kept, and a range of its own - set by
double-clicking the slider, or saved so in a file, as Inigo the Dragon's is - left alone.
