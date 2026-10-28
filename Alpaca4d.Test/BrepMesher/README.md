# Mesh Brep

    ./run.sh
    CORE=/path/to/Alpaca4d.Core.dll ./run.sh

Checks `Alpaca4d.BrepMesher`, which Mesh Brep runs. It needs Rhino's QuadRemesh, so it runs inside
a running Rhino 8, through `rhinocode`: start Rhino's script server first (`StartScriptServer`).
Each run copies the Core build to a fresh folder, because Rhino keeps a DLL it has loaded mapped.

Every case checks the same things, and some add their own:

- the mesh is valid and in one piece;
- every edge of the Brep - naked, or between faces - is a line of mesh edges from end to end,
  lying on it to 1e-7, and each of those mesh edges has as many faces as the Brep edge: two
  between two faces, three where a wall meets a slab;
- every point on the Brep has a node exactly on it, and a point off it is warned of;
- no face is turned against the Brep face under it, and no quad corner is under 40 degrees;
- no warnings but the ones expected.

| case | adds |
| --- | --- |
| two faces joined, with points | a point inside each face, one on the edge between them, one on a naked edge, one on a corner, one off the Brep, two 7.6 cm apart |
| box | a closed Brep; points on a face and on another |
| plate with a hole, points on its edge | a closed edge; two points on it |
| vault running into a flat | curved faces, a smooth edge between faces |
| wall under a slab, also at L = 0.37 | three faces on an edge, the slab one face split by the merge |
| box of walls with one across, under a slab | walls meeting at corners and in a T; a point on the wall across |
| round wall under a slab | a closed edge three faces share, with very different node counts on each |
| wall stopping inside a slab | refused, saying what to split |
| edge length in the wrong units | refused before QuadRemesh runs |

What it caught on the way:

- QuadRemesh meshes each part of a Brep that hangs together across ordinary edges as a block, and
  where three faces share an edge each block gets its own line of nodes along it. Those lines
  needn't have the same count, and they coincide along some stretches and not others, so welding
  whatever happened to coincide left lines that were partly one and partly two, and pairing them
  up collapsed quads. The mesher now keeps every Brep face's nodes apart until each line along an
  edge has been brought to the same count, then welds them one to one.
- On the round wall QuadRemesh put 10 nodes round the disk inside the circle, 18 on the wall and 27
  on the slab outside. A corner's nodes had been looked for within half a quad of it, and the
  disk's nearest was further, so its line was one short at the start and welded out of step.
- Point3d's hash XORs the coordinates, which on a regular grid crowds nearly every node into a
  handful of buckets: keying a dictionary on it made welding a 17 000-quad mesh take half a second.
