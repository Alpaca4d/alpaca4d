# View Results

    ./run.sh

`View Results_WIP` is one component in place of the five that each draw one kind of result. It
offers a **Result** and a **Component** in two dropdowns, filters the elements in the same language
the numerical components already use, and ghosts what the filter left out.

## What is checked here

The dropdown wiring, and nothing else.

Every reader hands its quantities back in an order of its own, and none of those orders is the
order the names are offered in - `Read.ForceBeamColumn` returns `(n, mz, vy, my, vz, t)` against a
menu reading N, Vy, Vz, Torsion, My, Mz. Getting that wrong is the worst bug this component can
have: nothing throws, nothing looks broken, and someone reads a shear diagram labelled as a moment.
So each family's list is pinned against the component that was already drawing those quantities.

The test loads the built `.gha` and reaches `ResultField` by reflection, because it is internal to
that assembly.

## What is not checked here

Everything that needs a viewport, and everything that needs a recorder file. Reading results goes
through PureHDF and a model that has been through `Assemble`, which needs an `RTree` - whose native
library only loads inside Rhino. So *what a value is* is checked here, and *what it looks like* has
to be looked at in Rhino:

* the ghosting, and that a filtered-out element is still visible behind the ones being read
* the deformed toggle, and that a stress reads on the shape it belongs to
* the beam force diagrams standing off along the beam's local z
* the reaction arrows, their direction and their scaling
* that the dropdown selections survive a save and a reload

## Notes

* **The Layer dropdown cannot be hidden.** The widget library has no per-control visibility, so
  Layer is on screen for all six families and only means anything for Shell stresses. A Layer left
  on something other than Top puts a remark on the component when it is being ignored, rather than
  letting it read as being in force.
* **The range fits what is on screen, not the model.** A filter narrowed to one beam is a question
  about that beam, and a gradient stretched over a maximum somewhere else would paint it flat.
* **Diagrams take the same gradient as everything else** rather than a fixed pair for positive and
  negative, so one Legend reads for whatever is drawn.
