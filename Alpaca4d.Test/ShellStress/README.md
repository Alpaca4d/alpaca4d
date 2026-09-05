# Shell stresses through the thickness

## OpenSees calls two different things "stress"

This is the trap the whole feature exists to avoid.

| request | what comes back | units |
| --- | --- | --- |
| `section.force` | stress **resultants** — `getStressResultant()` | p [F/L], m [F·L/L], q [F/L] |
| `stresses` (element level) | the **same numbers again** — `ASDShellQ4::getResponse` case 2 loops `getStressResultant()` | identical |
| `section.fiber.stress` | **stress**, at stations through the depth | [F/L²] |

The first two are the same request under two names. The check here reads a real recorder file
and asserts they are bit-identical — which is why the old commented-out `ShellStresses`
component, which read `stresses`, would have been a duplicate of `Shell Forces` under a
misleading name rather than a new quantity.

Only the third is stress. Alpaca4d had been recording it all along and never reading it.

## The stations are not the same for the two sections

| section | stations | where |
| --- | --- | --- |
| `PlateFiber` | 5, Lobatto | ±1, ±0.6547, 0 of the half-thickness — **the first and last are the faces exactly**, the third the mid-surface exactly |
| `LayeredShell` | one per layer | the layer **centres** — the outermost is half a layer in from the face |

So `Top` and `Bottom` mean "outermost station reported", and only for a `PlateFiber` section is
that the face. A thick outer layer on a layered section will read low against a hand check; use
thin outer layers when the face value is what matters. An even layer count has no station at the
mid-surface at all, and the one just above is used.

The reader takes the layout from the file's own `META/MULTIPLICITY` and `META/NUM_COMPONENTS`
rather than assuming five, which is why a layered section of any depth reads with no special
case. Columns run gauss-major: gauss, then fibre, then component.

## What the checks establish

Run against recorder files this suite solves itself, so the numbers are the solver's own:

- A cantilever plate in pure bending reads **equal and opposite on its two faces and zero at
  mid-depth**. That single fact is why the layer has to be chosen rather than assumed.
- Those face stresses match `p/h ∓ 6m/h²` derived from the resultants in the same file, to about
  1.6e-9 relative. Not exact — that formula is the thin-plate idealisation and this is a Mindlin
  shell — but far tighter than any striding error could survive.
- A layered stack of soft/stiff/stiff/stiff/soft has its **outer layers carrying less than the
  ones inside them**, despite being further from the neutral axis, because they are a third the
  stiffness. A single-material section cannot show that, and it is the reason to reach for a
  layered one.
- The layered section writes the exact `section LayeredShell …` line OpenSees 3.5 accepted, sums
  its layer thicknesses, reports each distinct material once (Assemble writes one declaration per
  entry, so a symmetric stack must not ask for the same material five times), and refuses fewer
  than three layers rather than writing a deck that dies in the solver.

## Running

    ./run.sh

Needs a built `Alpaca4d.Core`, a C# compiler and a net48 RhinoCommon. OpenSees on `PATH` (or at
`$OPENSEES`) is optional: with it the two decks here are solved and the reader is checked against
real recorder files; without it only the section arithmetic runs and the rest is skipped with a
note.
