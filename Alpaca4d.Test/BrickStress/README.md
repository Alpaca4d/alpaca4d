# Solid elements

## A solid wound the wrong way round is one OpenSees solves

This is the trap the whole check exists to avoid.

`SSPbrick` and `FourNodeTetrahedron` both form their volume element as a Gauss weight times the
determinant of the Jacobian, and neither looks at its sign. `FourNodeTetrahedron::shp3d` divides
the shape function derivatives by `Jdet` and hands back whatever comes out; `SSPbrick::GetStab`
inverts the Jacobian without checking it. So an element whose nodes arrive in the wrong order is
not rejected — it gets a **negative stiffness**, and the analysis converges:

| the same unit cube, E = 1000, pulled to a uniform 10 | `ux` | `σxx` |
| --- | --- | --- |
| nodes 1–4 on the near face, 5–8 on the far one | 0.01 | 10 |
| nodes 5–8 given first | **−0.01** | **−10** |

Both runs return `analyze -> 0`. Nothing downstream can tell one from the other. The same holds
for the tetrahedron: any odd permutation of its four nodes flips the sign, any even one does not.

`Utils.CleanHexahedron` and `Utils.CleanTetrahedron` are the only thing standing in the way, so
what they decide is checked here against the solver's own Jacobian.

## The ray this replaced asked about the units, not the element

The winding used to be settled by firing a ray of a fixed **1000 units** along the first face
normal and counting mesh crossings, after nudging its start back by a fixed **0.001**. Both
numbers are absolute, so both are really questions about what the model is drawn in:

- a solid deeper than 1000 units in the direction of the ray never reaches its far face, so an
  inward normal counts one crossing and reads as outward — and the element comes out reversed;
- a solid thinner than 0.001 has its start point nudged clean through, so the ray misses the
  element altogether.

`CheckHexahedron` asks the solver's question instead, and measures the answer against the
element's own size. The suite runs cubes from 0.0002 to 100000, and a unit cube a million units
from the origin.

## What the corner check catches, and what it must not

The determinant is checked at the centre **and at all eight corners**, because the two disagree:

| brick | centre | worst corner | verdict |
| --- | --- | --- | --- |
| a quarter turn between the two faces | 0.0625 | 0.125 | **accepted** — distorted, not folded; its mid-height section is a diamond |
| the far face wound the other way | 0 | −0.125 | **turned away** — edges 1–5 and 3–7 cross |

The second is what a mesh series whose meshes disagree about winding produces, and its centre
reads exactly zero — so a centre-only check would have let it through.

## The recorder writes an ID dataset, and it is not decorative

`MPCORecorder` groups its rows by element class and writes, beside `DATA`, an `ID` dataset giving
the element tag of each row. Reading rows by position only agrees with the model while Alpaca4d's
tags happen to ascend in list order, which is a property of `Assemble` rather than of the format.
`mixed.tcl` interleaves the tags across the two classes — tetrahedra 10 and 30, bricks 20 and 40 —
so a positional read would be visibly wrong.

The group name is scanned for rather than spelled out. `121-SSPbrick[400:0:0]` ends in a header
index that counts up when one class produces two different response layouts, so hard-coding it
silently drops every element that lands in `[400:0:1]`.

## The stresses are in the global axes

There is no local axis to set, and this is worth stating plainly because the component used to
say the opposite:

- `element SSPbrick $tag $n1..$n8 $matTag <$b1 $b2 $b3>` — ten integers and three optional body
  forces, and nothing else;
- `element FourNodeTetrahedron $tag $n1..$n4 $matTag <$b1 $b2 $b3> <-doInitDisp>` — the same;
- `nDMaterial ElasticOrthotropic $tag $Ex $Ey $Ez $vxy $vyz $vzx $Gxy $Gyz $Gzx <$rho>` — no
  orientation either.

Both elements build their strain from global nodal displacements and hand it straight to the nD
material, so the material's own directions **are** the world X, Y and Z. `ortho.tcl` pulls one
cube of `Ex = 1000, Ey = 4000` along each axis in turn and gets 0.010 and 0.0025 — the material
does not turn with the element, because there is nothing for it to turn with. To align an
orthotropic material with a part, rotate the model.

The six components are `sigma11, sigma22, sigma33, sigma12, sigma23, sigma13`, which is the order
`FourNodeTetrahedron::setResponse` names them in and the order a three dimensional nD material
returns them in. They are σxx, σyy, σzz, σxy, σyz, σzx.

## What the checks establish

Run against recorder files this suite solves itself, so the numbers are the solver's own:

- every ordering a mesh can arrive in normalises to a positive Jacobian, and OpenSees returns the
  same correct answer for all of them;
- a folded brick and a flat one are turned away, while a sheared one, a quarter-turned one and one
  a fifth of a millimetre thick are accepted;
- a file whose classes carry interleaved tags reads back in the model's order, not the file's;
- an element or a step the file does not hold is named rather than read past;
- an orthotropic material in a brick follows the global axes.

## Running it

```
./run.sh
```

Needs a built `Alpaca.Core`, `mcs` and a net48 `RhinoCommon`. OpenSees on PATH is optional: with
it the three decks here are solved first and the reader checks run, without it only the geometry
checks do. Those need no native RhinoCommon, because `Point3d` and `Vector3d` are managed types
and only `Mesh` is not.
