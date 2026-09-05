# Spring Link, Zero Length Spring and Equal DOF

    ./run.sh

Three ways of joining things that were not joined before:

| | OpenSees | what it is |
|---|---|---|
| **Spring Link** | `twoNodeLink` | a spring between two nodes that are apart |
| **Zero Length Spring** | `zeroLength` | a spring between a node and the ground |
| **Equal DOF** | `equalDOF` | a tie between chosen degrees of freedom of two nodes |

The first two carry one uniaxial material per direction, and only the directions listed carry
anything at all. That is the point of them: a link on direction 1 alone pulls along its own axis
and resists nothing else, which is a bolt; a spring on direction 3 alone props a node up and lets
it slide and turn, which is a bearing pad.

## What runs

`Link.cs` checks the tcl each class writes, on objects whose node tags are set by hand the way
`Model.Assemble` would set them. `Deck.cs` then writes four complete decks around that same tcl,
and `run.sh` hands them to OpenSees. Every element line, every `fix`, every `equalDOF` in those
decks comes out of `WriteTcl`, so what the solver reads is the string Alpaca4d produces rather
than a transcription of it. The decks check themselves against closed form:

| deck | what it pins down |
|---|---|
| `link.tcl` | each of a link's six springs, read off one at a time |
| `spring.tcl` | a cantilever whose built-in end is a spring, `P/k` and `PL/k` and the tip that picks up both |
| `equaldof.tcl` | two cantilevers of different span sharing a tip translation and keeping their own rotations |
| `laminate.tcl` | laminated glass: two shells and an interlayer, swept over its shear modulus |
| `joins.tcl` | what separates a rigid link, an equalDOF and a stiff spring link |

Without OpenSees on PATH the decks are written and left unsolved, and the tcl checks still run.

## Three ways of joining two nodes

`joins.tcl` exists because the component descriptions now tell the user which to reach for, and
that advice ought to rest on a measurement. A cantilever tip both drops and turns; a second node a
metre away is joined to it by one of the four, and the question is whether that node picks up the
tip rotation times the metre as well:

| join | carries the offset |
|---|---|
| Rigid Link, `beam` | yes - a rigid body, which is what it says |
| Rigid Link, `bar` | no - translations only |
| **Equal DOF, all six** | **no** |
| Spring Link, very stiff | yes, through its own shear springs at mid-length |

The third is the one worth a test. Tying all six degrees of freedom reads like a rigid connection
and is not one: `equalDOF` copies each degree of freedom on its own, and copying every component is
not the same as moving as a body once there is a distance between the two nodes.

So the three are not alternatives. A **Rigid Link** is an exact constraint for a join with no give -
no stiffness to choose and nothing for the solver to condition around. A **Spring Link** is an
element with a real stiffness per direction, for a join that flexes or that is stiff some ways and
free others. **Equal DOF** copies components and ignores the geometry, which is what a shear key, a
slider, or a pair of faces tied for symmetry actually needs.

## What a self-referential constraint does

Alpaca4d puts one node at each point, so two points closer together than the model tolerance are
one node - and a constraint drawn between them ends up with both ends on it. Neither OpenSees
command says anything useful:

* `equalDOF n n` is accepted and solved without complaint, and the answer is wrong. In the case
  measured it came out 5.3 times too stiff.
* `rigidLink beam n n` is worse: `FATAL FullGenLinSOE::getX - vectX == 0`, and the process dies.

Both are caught in `SetTopologyRTree` now, with a message saying why. Neither guard is checked here
because both live behind an `RTree`; the behaviour they prevent is recorded above so nobody removes
them wondering what they were for.

## The laminate deck

This is what the link was added for. Two panes of 6 mm glass at their own mid-plane heights, one
`twoNodeLink` per node pair carrying the interlayer - direction 1 normal to the panes and stiff,
2 and 3 the shear the interlayer really provides, `k = G A / t` over the glass that node speaks
for.

It is checked at both ends of a sweep over the interlayer's shear modulus, because at both ends
the answer is known and owes nothing to the interlayer:

* **G to zero** - the panes are loose. The same shell mesh with one pane carrying half the load is
  the same structure, and the two agree to four figures.
* **G to infinity** - the panes are one section, and the stiffness is the full composite
  `2(I + A d²)`.

Between them the deck only insists the answer moves the right way. That is where a real interlayer
sits, and no formula gives it exactly - which is the reason for modelling it rather than reaching
for an effective thickness. For 6 mm / 1.52 mm PVB / 6 mm over a 1 m cantilever it lands 87% of
the way from loose to rigid.

## What is not checked here

* **Assemble.** Finding the node under a link's end, allocating the ground node, collecting the
  spring materials so they are declared before the elements - all of it goes through
  `Model.Assemble`, which needs an `RTree`, whose native library only loads inside Rhino. The tag
  allocator is checked here because it is plain arithmetic; the rest has to be run in Rhino.
* **The reaction at a spring support.** Reaction Forces is built around `Support` objects, and a
  Zero Length Spring reacts against a ground node of its own, which is not one. The reaction is in
  the recorder file under that node's tag; nothing reads it out yet.
* **The round trip.** `TclReader` does not read `element twoNodeLink` or `element zeroLength` back.
  It says so in its warnings rather than dropping them quietly, but a model with springs in it does
  not survive Serialise and Deserialise intact.

## Notes on `twoNodeLink`, learned the hard way

* With no `-orient` at all it does not warn, it exits the process: *invalid orientation vectors*.
  One is always written.
* Given six numbers it takes the first three as the local x and prints a warning for every element
  saying it is using them instead of the nodes - even when the two agree exactly. Given three it
  takes x from the nodes and the three as the y hint; the frame comes out identical and nothing is
  printed. So the three-number form is used whenever the frame runs along the link, which is nearly
  always.
* Its shear springs act at mid-length by default, so a shear force on a link of finite length also
  turns its nodes. That is not an error to be tuned away - it is what makes two offset shells act
  compositely - and `link.tcl` measures it.
* A local x handed to it on `-orient` really is used, even though it warns that it is doing exactly
  that. `link.tcl` checks it, because the Plane input tells the user that the world XY plane is how
  the directions become global.
