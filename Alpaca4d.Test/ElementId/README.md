# Element identifiers and the result filter

Elements can be given an `ElementId` on the element components, and the element result
components (Beam Forces, Shell Forces, Brick Stresses) take `ElementId` and `Tag` inputs to
report part of a model instead of all of it.

## Two handles, deliberately different

| | what it is | unique? | who sets it |
| --- | --- | --- | --- |
| `Id` (shown as **Tag**) | the number Assemble hands out, and the one OpenSees knows the element by | always | Assemble |
| `ElementId` | free text typed on the element component | **no** | the user |

`ElementId` not being unique is the point, not a shortcoming. One identifier typed against a
list of twenty curves labels all twenty the same, and asking for it returns all twenty — which
is also how a mesh behaves, since every face becomes its own element. A list of twenty
identifiers instead gives each element its own. Nothing renumbers what was typed either way.

The tag is what separates elements inside such a group, and the only handle on an element
nobody labelled. It is also what the result branches are keyed by, so a branch still says which
element it belongs to when twenty of them share one `ElementId`.

## One input, and the code works out which handle you meant

Both go into the same `ElementId` filter, in any mix, and each term is classified on its own:

| term | read as |
| --- | --- |
| `MyBeam_3` | an ElementId |
| `MyBeam*`, `Col_?` | a wildcard over ElementIds |
| `47` | a tag — **and** an ElementId of `"47"` |
| `regex:^Col` | a regular expression over ElementIds |

A whole number is tried both ways rather than guessed at: nothing stops someone labelling an
element `"12"`, and there is no way to tell from the term which was meant. Trying both matches
more instead of guessing wrong, and a model that does not use numeric ElementIds — nearly all
of them — cannot tell the difference.

Only the canonical spelling of a positive whole number counts as a tag. `007` and `+7` parse as
7 but were far more likely typed as identifiers, so they stay patterns; so does `0`, since
Assemble numbers elements from 1. A `regex:` term is never read as a tag, whatever it spells.

## `MyBeam*` is a glob, not a regular expression

The obvious implementation hands the user's pattern straight to `Regex`. It does the opposite
of what they asked:

| pattern | as a regex it means | so it matches | and misses |
| --- | --- | --- | --- |
| `MyBeam*` | `MyBea` + zero or more `m` | `MyBea`, `MyBeam`, `MyBeamm` | `MyBeam_0`, `MyBeam_1` |
| `MyBeam*` as a glob | `MyBeam` + anything | `MyBeam_0`, `MyBeam_1`, `MyBeam` | `MyBea` |

Nobody typing `MyBeam*` means the first row. So a bare pattern is read as a glob — `*` is any
run of characters, `?` is one, everything else is escaped and the whole thing is anchored —
and `regex:` in front opts into a real regular expression for what a glob cannot say:

    MyBeam*                     glob
    Col_?                       glob: Col_1, not Col_12
    regex:^(Col|Beam)_[0-9]+$   regular expression

Escaping the rest is what keeps an identifier holding `.`, `+` or `(` matchable at all —
`L1.2*` finds `L1.2_0` and not `L1X2_0`.

## An ElementId has to survive Serialise → Deserialise

OpenSees has no command for an identifier, so it rides out as a comment the solver ignores:

    # alpaca:elementid 3 MyBeam
    element forceBeamColumn 3 3 4 3 ...

The tag is written alongside because it is the only thing tying the line to one element — the
identifier itself may well be shared. `TclReader` keeps comment lines beginning `alpaca:`
instead of dropping them with the rest, and applies them once the whole file has been walked:
the line sits in front of its element when Alpaca4d wrote the file, but anywhere at all once
someone has hand-edited it.

## Nodes get a different filter again — and the two components differ

Neither has a free-text identifier: nobody authors a node, they fall out of the element geometry
and Assemble numbers the unique points from 1. So no glob, no patterns. But the two components
have different things to offer the user:

| component | filter | why |
| --- | --- | --- |
| Nodal Displacements | `NodeTag` | it covers every node in the model, most of which the user never placed — the tag is the only handle |
| Reaction Forces | `PointPos` | it covers the supports, which the user *did* place and can point at — a tag is one they'd have to look up |

`PointPos` matches within the model's own `Tollerance`, which is exactly right: that is the
distance below which Assemble already treated two points as one node, so it's the distance at
which the user's point and the support's are the same place. A point picked off the screen never
lands on the same `double` twice, so an exact test would match nothing; a tolerance of zero — a
model deserialised from `.tcl` before one is set — falls back to the Assemble default rather
than comparing doubles for equality.

Empty means everything, as everywhere else. Two things the checks pin down for both:

- The order is the model's, not the order the tags or points were given in, so every output of a
  component stays in step with every other.
- A request that finds nothing is handed back rather than dropped. This matters most on Reaction
  Forces: a point with no support at it would otherwise vanish silently and read as a support
  carrying zero, which is a very different statement from "there is no support there".

## Running

    ./run.sh

Needs a built `Alpaca4d.Core`, a C# compiler and a net48 RhinoCommon, like the other checks
here.

**What it does not cover:** the round trip above. It goes through `Model.Assemble` and
`TclReader`, both of which build an `RTree`, and RhinoCommon's native library only loads inside
Rhino — so the checks run on a stub element and exercise the glob and the selection only.
Everything under test there reads nothing but `Id` and `ElementId`. The `.tcl` round trip has
to be checked in Rhino.
