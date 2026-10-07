# Karamba3DToAlpaca4D

    ./run.sh

Karamba3DToAlpaca4D has two halves. `Alpaca4d.Karamba` reads a Karamba3D model into an
`ImportModel` and is the only code that knows Karamba's API. `ImportModelBuilder`, in Alpaca.Core,
turns that into Alpaca parts - elements, supports, masses, one load pattern per load case - and
knows nothing of Karamba.

`run.sh` checks the second half without Karamba3D or Rhino:

| check | what it pins down |
|---|---|
| section planning | a rectangle or pipe that matches Alpaca's own shape stays that shape; a 20% gap in J, a T-section or an unknown shape becomes a general elastic section with a warning; an angle (Iyz not zero) or a section without area is an error |
| axes | Alpaca's local y is Karamba's local z (the depth), so Karamba's Iyy is Alpaca's Izz and its Az gives Alpaca's alphaY |
| shear | a section without shear area writes `section Elastic` without alphaY and alphaZ, since OpenSees fails on zero |
| report | a hundred elements with one problem make one message, naming five |
| tolerance | the closest pair of nodes, which the node tolerance has to stay below |

The axis mapping was also checked in OpenSees: a cantilever along X and a column along Z, each
loaded on its strong and its weak axis, deflect P L^3 / 3 E I with Karamba's Iyy and Izz.

## In Grasshopper, against Karamba3D 3.1

Building whole models needs Rhino, and reading them needs Karamba3D 3.1, which runs on Windows
only. For each model below, convert it with Karamba3DToAlpaca4D and plug the Elements, Supports,
LoadPatterns and Tolerance into Assemble Model, which builds one model per load case from the tree.
Run a linear static analysis and compare the displacements and reactions with Karamba3D's Analyze
Th.I for each load case. They should agree to well under 1%.

| model | what it shows |
|---|---|
| cantilever, rectangular section, tip load in -Z, then in -Y | strong and weak axis are not swapped |
| the same cantilever with a column along Z | vertical members get the same axes as Karamba |
| beam turned by an orientation angle | Karamba's local axes come through (`localCoSys`) |
| portal frame, HEA from the table | an I-section with fillets becomes a general section with a warning, and is as stiff |
| portal frame with gravity, two load cases and a point load in one | one branch of LoadPatterns, so one model, per case; self-weight bends the beams between nodes |
| line load: global, local and projected, on an inclined beam | the three orientations |
| plate on four corners with a mesh load | ASDShellT3 and the mesh load's nodal loads; no load counted twice |
| point mass on a cantilever, eigen analysis | masses in kg, density from gamma and Karamba's g |
| a model with a hinge, a truss and a spring | each reported as an error, and no output unless AllowPartial |
| the portal frame with a beam of your own added, and a Uniform Excitation instead of Karamba's patterns | your elements join Karamba's nodes; a dynamic run on Karamba's masses |

The Report output lists, per load case, the total load applied. It should match the sum of the
reactions in Karamba3D.
