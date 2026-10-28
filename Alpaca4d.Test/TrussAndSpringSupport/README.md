# Truss and Spring Support

    ./run.sh          # without Rhino
    ./rhino.sh        # in a running Rhino 8 with its script server started (StartScriptServer)
    ./grasshopper.sh  # the same Rhino, with Grasshopper loaded: the components themselves

Two new components and what they write:

| | OpenSees | what it is |
|---|---|---|
| **Truss** | `corotTruss` | a bar, axial force only: both ways, tension only (`Elastic E 0 0`) or compression only (`ENT`), with an optional prestress (`InitStressMaterial`) |
| **Spring Support** | `zeroLength` from a fixed node of its own | Elastic springs along and about a plane's axes, or Compression Only along its Z (`ENT`, or `ElasticPPGap` with a gap) |

## What runs where

RhinoCommon cannot make a curve or test a plane outside Rhino, so the checks are split.

**run.sh** compiles `TrussCheck.cs` with mono and runs it three times over:

* `write` checks the Tcl a `Truss` and a `SupportSpring` write, and the linear stand-ins Linear Buckling
  puts in for one-way materials, then writes decks around that same Tcl.
* OpenSees solves the decks, which check themselves against closed form:

  | deck | what it pins down |
  |---|---|
  | `panel.tcl` | an X-braced panel: a tension-only diagonal carries H / cos alone and the other goes slack, each way; compression only the mirror image; two-way bars share it |
  | `cable.tcl` | a prestressed cable: unloaded it carries its prestress; under a point load its force is prestress plus stretch and the node is in equilibrium where it has moved to |
  | `mass.tcl` | the mass `-rho` gives: omega squared of a bar is EA/L over half its mass |
  | `mixed.tcl` | a cantilever propped by a truss, for the reader |

* `read` reads the results files back through `Read.ForceBeamColumn`: trusses alone, a beam and a truss
  in one file, and a file with no `axialForce` in it, which says which box to tick.

**rhino.sh** compiles `TrussCheck.cs` and `RhinoChecks.cs` into one library and runs it in Rhino through
`RhinoRun.cs`. There it checks a spring support's own Tcl and writes three more decks for OpenSees,
solved afterwards:

| deck | what it pins down |
|---|---|
| `spring.tcl` | a spring under a cantilever tip, a rotational spring at a column base beside a fix on the same node, and a compression-only spring turned over to hold in tension; the reaction is read on the spring's own node |
| `bearing.tcl` | a two-span beam on a rigid end spring: two-way, the textbook -wL/16; compression only, the end lifts and the loaded span is simply supported |
| `gap.tcl` | a cantilever tip a gap above a stiff spring: free until it lands, then (P + kc g)/(kb + kc) |

and it builds models the way the components do and goes through `Model.Assemble`, OpenSees and the
readers Beam Forces and Reaction Force use:

* a line load on a truss becomes two point loads, half of w L each, with no element load;
* every node only trusses reach has its rotations fixed - all three, less any a support there already
  fixes - and no degree of freedom is fixed twice, which OpenSees refuses;
* a node a beam reaches is left alone, a skewed support's node gets the rotations while its own node
  carries the fix;
* a compression-only bearing lifts off and its row in Reaction Force reads zero, the fix on the same
  node takes nothing vertical;
* a spring along an axis a rigid support already holds is reported by Assemble Model;
* the FEM-Design reader makes a truss a pinned bar and a spring support a support of stiffnesses,
  and says what it leaves behind (one-way behaviour, prestress, gaps).

`../FemDesignExport` checks what FEM-Design receives from those.

**grasshopper.sh** goes through the components themselves. `GrasshopperChecks.cs` builds Grasshopper
documents in code out of the real components - Uniaxial, RectangleCS, the beam switcher on its Truss
and ForceBeamColumn types, Support, Spring Support, Point Load, Linear Load, Linear Time Series, Load
Pattern, Assemble Model, Analysis Settings, Run Analysis - solves them the way the canvas does, and
checks what Beam Forces, Reaction Forces and Nodal Displacements put out, against closed form, and every
component's runtime messages: the X-braced panel, the prestressed cable, a line load on a truss, the
two-span beam on a compression-only and on a stiff Elastic Spring Support, a spring along an axis a
Support holds, the Linear algorithm, and an energy test picked on purpose. It loads this build of the
plugin into a load context of its own, beside the installed one, so Rhino need not be restarted. View
Results and Model View are left out: they need a license, and the test folder has none.

This is what found the one real fault so far: with the default Analysis Settings a compression-only
Spring Support never let go. A rigid one-way spring starts stiff, so the first Newton iteration barely
moves the node, and the default EnergyIncr test passed it while the spring was still pulling; the
reaction stayed on the support's node as an unbalanced force. Run Analysis now swaps a default test
that ignores the unbalance for NormDispAndUnbalance when the model has one-way bars or supports, and
warns when such a test was picked by hand.

## Rhino already running Alpaca4d

When Grasshopper has loaded the installed Alpaca4d, its `Alpaca4d.Core` sits in the default load
context, and a reference by name binds to that one. `Assembly.LoadFile` does not help: it gives each file
a context of its own, so the test library's reference falls through to the default. `RhinoRun.cs` loads
every DLL of the run into one `AssemblyLoadContext` instead - made through reflection, because the
script compiler does not reference `System.Runtime.Loader` - and there they find each other first.

## Learned on the way

* OpenSees' `Elastic` takes its tension stiffness at zero strain. An Elastic with nothing in tension and
  something in compression therefore starts with no stiffness, and the first Newton iteration of a
  structure that needs it fails. `ENT` is stiff at zero strain: it is what a compression-only bar and
  support use.
* `ElasticPPGap` with an open gap has no initial stiffness either. That is the point of a gap, so the
  structure has to hold the node until it closes; the component says so.
* `fix` refuses a degree of freedom already fixed (`cannot add as node already constrained in that dof`),
  which is why the rotations of a truss-only node are fixed less what a support there holds.
* Only `corotTruss` answers to `axialForce` among the elements Alpaca4d writes, so recording it costs
  nothing in a model without trusses.
