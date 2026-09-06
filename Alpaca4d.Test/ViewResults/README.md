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
* **Diagrams take the colour that belongs to the force**, not the gradient. A diagram is read by
  its sign before its size, and two diagrams on one screen have to be tellable apart. The palette
  pairs a force with its moment - N with Torsion, Vy with My, Vz with Mz - which is fine, because
  those two never share a diagram.
* **Ghosting is not optional.** The point of filtering here rather than upstream is that the part
  being read stays in the model around it; a filter that hid the rest would be the same as wiring
  a smaller model in.
* **Only menus with controls of their own.** A menu carrying nothing but an input plug reads as a
  heading that does nothing, which is what the first cut did with its Colour menu - so Step,
  ElementId, Colors and Range are ordinary inputs on the body.
* **Animating re-solves twenty times a second**, so the recorder read is cached against the
  question that produced it - family, component, layer and step. Only the scale changes between
  frames.

## The reaction direction

The first cut always drew the resultant, whichever component was chosen. On a model with any
horizontal reaction that means picking Fz and getting an arrow off at an angle: the right number in
the Values output and the wrong picture. An arrow that says Fz now runs along z and nowhere else,
and carries the sign.

The sense was checked against OpenSees rather than assumed. A cantilever pushed down with 1000
reports `nodeReaction` = `(0, 0, +1000, 0, -1000, 0)` - the support's action on the structure - so
an arrow drawn outwards from the support points up under a downward load, which is the picture an
engineer expects.
