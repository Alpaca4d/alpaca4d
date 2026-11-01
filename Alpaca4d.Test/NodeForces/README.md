# Node Forces

    ./run.sh          # without Rhino; with OpenSees on PATH, or OPENSEES=..., the solved joint too
    ./grasshopper.sh  # in a running Rhino 8 with its script server started (StartScriptServer)
                      # and Grasshopper loaded: the component itself. LICENSE=... for the grillage

Node Forces reads the forces every beam and truss brings into a node. The working is
`Alpaca.Core/Result/NodeForces.cs`: `NodeGraph` files every beam end under its node, and
`NodeForces` turns the section forces at that end into what the beam puts on the node.

## run.sh

`NodeForces.cs` runs under mono, against Core only.

**The graph.** A Warren girder built by hand: the beams at a node in the model's order, which end
each is, the nodes a beam meets and not a bare one, a link counted at both its nodes, a point found
within tolerance and not beyond it. Then a grillage of 180,600 beams, to time it: the graph builds in
about 30 ms and every node's beams are walked in about 5 ms.

**The signs**, on cases worked by hand: a cantilever's fixed end and tip, a bar in tension at each
end, a beam along y with its local z squared off, torsion at a J end.

**A joint solved by OpenSees.** Five members meet at one node: Newton-Cotes beams with uniform
loads, one with three sections, a HingeRadau beam released at the joint with a point load on it, and
a truss. Recorded as Alpaca4d records them, MPCO with `section.force` and `axialForce`, and read back
through `Read.ForceBeamColumn`, over two steps.

| what | how close |
| --- | --- |
| every member end against OpenSees' `globalForce` | 1e-5: the text recorder prints six figures |
| N, Vy, Vz, Mx, My, Mz at every beam end against OpenSees' `localForce`, in the beam's own axes | 1e-5 |
| the members at the joint against its load | 1e-11 |
| the member at each support against its reaction | 1e-5 |

Reading only the beams at a node gives them bit for bit and leaves the others empty, for beams and
for a truss.

**The same joint on sections without shear**, Elastic sections given no shear factors, its point load
made uniform. Every beam reads all its sections, and the shears worked out from the moments meet the
same table to the same figures.

**A grillage solved twice into one file**, 840 beams numbered from 1 and then from 1001: the four
beams at the middle, read row by row, are what reading every beam gives, and hold up the load. The
second run would find none of its beams if the element tags kept from the first were used.

## What was found

* **The end sections are the end forces.** Newton-Cotes and HingeRadau both put a section at each
  end, and a force-based element's section forces are in equilibrium with its end forces and its
  element loads. On the face looking along +x they act as (N, -Vy, Vz) and (Mx, My, Mz), the shear
  along y turned round because OpenSees takes Vy = dMz/dx.
* **A truss pulls along where it has moved to.** `corotTruss` is corotational, so its force turns
  with the bar. Along the undeformed axis the joint here missed balance by 4e-5 kN at full load,
  enough to notice and far more in a cable that sags. Node Forces reads the displacements and turns
  trusses with them. Beams are written with a Linear transformation and keep their axes.
* **The recorder file, not the graph, is what takes the time.** On a grillage of 45,300 beams the
  graph builds in 5-10 ms and a node's beams are found in 0.01 ms, but reading every beam's forces
  took 230 ms. Of that, 80 ms went on the ID dataset alone, the element tag of each row: PureHDF
  reads 45,300 tags as slowly as the 1.4 million forces beside them. `Read.ForceBeamColumn` now
  keeps the tags per file, and given `only` reads just those rows. One node then reads in 10 ms,
  every beam in 150 ms.
* **An Elastic section without shear factors records four forces per section**, P, Mz, My and T,
  not six - an Elastic section with AlphaY or AlphaZ 0 or less, or one read back from such a deck.
  `Read.ForceBeamColumn` took six, and read a cantilever's five sections as three, with the wrong
  values in them. It now reads where each force sits off the group's META/COMPONENTS, and works
  the missing shears out as the slopes of the moments: a quadratic through the sections, which a
  force-based beam's moments under uniform load are. A point load would put a kink in them, but
  Alpaca4d puts none on a beam.
* **ASD Shell and Spring Link put their elements on the path {0;0}**, where the beam components use
  {0}, so beams with either, wired into Assemble Model without flattening, made a model per branch.
  Fixed, and checked in `../ElementPaths`.

## grasshopper.sh

`GrasshopperChecks.cs` builds the models out of the real components and reads them with Node
Forces:

* a Warren girder of trusses, read at a bottom node by a point a few mm off it and again by its tag,
  against the method of joints, and every node at once; a point off every node and an unknown tag
  are warned about;
* two cantilevers with their local axes turned by the ZAxis input, one along x with z 30° off
  vertical and one standing with z 60° round from x: at the fixed end Vy = -F·y, Vz = F·z,
  My = -L F·z, Mz = L F·y, at the tip the same shears and no moment, and in global axes the load
  and its moment;
* a cantilever on an Elastic Section with AlphaY and AlphaZ 0, under a line load and a tip load:
  Beam Forces reads its five sections, Vy is the load beyond each one, and Node Forces balances the
  reaction;
* a portal frame of beams with a line load and a push at a knee: the knees balance, the foot
  balances Reaction Forces, N to Mz match Beam Forces' end values to the bit, History gives
  `{step; tag}`;
* the portal with a link brace: a remark names the knee the link
  meets, and the other knee still balances;
* a grillage of 60 x 60 bays, 7,320 beams, timed: Node Forces took 20 ms over one node and 180 ms
  over every node, Beam Forces 470 ms over every beam.

The tree keeps its branches in path order, so they come in tag order whatever order the points were
given in. The checks find each branch by its Position.
