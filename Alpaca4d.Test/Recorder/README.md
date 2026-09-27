# MPCO recorder

    ./run.sh

What Run Analysis writes to the results file. Left alone, it picks a set to suit the analysis
type; an **MPCO Recorder** on the Recorders input of Assemble Model picks it by hand instead, one
tick box per result.

The boxes are only the results a result component reads back. MPCO records much more (Rayleigh
and unbalanced forces, element end forces, strains, and so on), but nothing in Alpaca4d would read
it, so it is not offered. A generic HDF5 reader would be the time to add more boxes, at the end of
the lists.

## What runs

| check | why |
| --- | --- |
| the default lines | Leaving Recorders empty has to record what it always did. The static, transient and eigen lines are compared word for word with the ones the recorder wrote before it took a list. |
| the tick-box order | The component saves its ticks by position in `Recorder.NodeResultTypes` and `ElementResultTypes`. Reordering either would tick a different result in every saved file, with no sign that it had, so both lists are fixed here and can only grow at the end. |
| every nodal result | MPCO refuses the whole recorder over one nodal name it does not know. `all.tcl` ticks all eight, and the solver has to accept it and write a group for each. |
| every element result | MPCO skips an element that does not answer to a name, with no warning. The file has to show each result on the elements a component reads it for. |
| beams with and without hinges | HingeRadau integration puts a hinged beam in a `section.force` group of its own. Beam Forces has to find both groups. |
| a file with little in it | `Read.Holds` finds what is missing, and asking for it anyway says which box to tick instead of failing inside HDF5. |

`model.tcl` holds one element of every kind Alpaca4d writes, each held up separately. `Recorder.cs`
adds the recorder lines through `Recorder.WriteTcl`, so what the solver reads is the string
Alpaca4d produces. Then comes `analysis.tcl`.

## Which box feeds which component

| box | read by | from |
| --- | --- | --- |
| `displacement`, `rotation` | Nodal Displacements, View Results, Principal Stress Lines | every node |
| `velocity`, `angularVelocity`, `acceleration`, `angularAcceleration` | Nodal Displacements, after a transient analysis | every node |
| `reactionForce`, `reactionMoment` | Reaction Forces, View Results | supported nodes |
| `stresses` | Brick Stresses | bricks and tetrahedra |
| `section.force` | Beam Forces, Shell Forces | every forceBeamColumn, hinged or not, and every shell |
| `section.fiber.stress` | Shell Stresses | plate fibre and layered shell sections |

The same file was checked on OpenSees 3.5.0, 3.7.1 (the one bundled with Alpaca4d) and 3.8.0.

## Not a tick box, and why

- **Every n-th step (`-T nsteps`).** MPCO names each step it writes after the solver's commit
  count, so recording every tenth step gives `STEP_0`, `STEP_10`, `STEP_20`. The result
  components read `STEP_0`, `STEP_1`, `STEP_2`, so they would find nothing after the first. This
  can be added once the readers look steps up by position.
- **Mode shapes.** Only an eigen analysis writes them, and Natural Vibration Analysis records them
  itself.
