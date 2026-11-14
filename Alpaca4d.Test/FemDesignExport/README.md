# Alpaca4DToFEM-Design

    ./run.sh

Alpaca4DToFEM-Design has two halves. `ExportModelReader` reads an assembled Alpaca model into an
`ExportModel`: points, vectors and properties, no Rhino curves or meshes. `StruxmlWriter` turns that
into a FEM-Design model, and makes every decision about how an Alpaca object becomes a FEM-Design
one.

`run.sh` checks the second half without Rhino. It needs a Release build of `Alpaca4d.FemDesign`,
`csc` and mono. Each model is written, checked against FEM-Design's schema, saved as `.struxml`,
read back, and inspected as the XML FEM-Design reads:

| check | what it pins down |
|---|---|
| portal frame | S355 and HEA200 become FEM-Design's S 355 and HE-A 200; a column drawn downwards is turned upwards with its depth kept along the same axis; FEM-Design's local z is Alpaca's local y, the depth; labels become names (Column.1), unlabelled bars B.1 with part B.1.1; a support keeps its plane's axes and holds exactly what Alpaca's does |
| trusses | a Beam With Hinges pinned at both ends and unloaded becomes a truss, which refers to the section itself and not to a complex section; loaded, it stays a beam hinged at both ends, with torsion released where Alpaca releases it; Alpaca's Ry release is FEM-Design's r_z |
| materials and sections | C30_37 and C24 become FEM-Design's C30/37 and C24, warning that FEM-Design's C30/37 has another E than Alpaca's and remarking that its C24 is heavier; concrete and timber rectangles and steel tubes and hollow rectangles are matched to FEM-Design's library by size, cold-formed first; an HEA200 of other dimensions is drawn from its outline, with a warning; a double angle is two regions; an unnamed material is custom with Alpaca's E, G and mass; an A-I-J section makes a fictitious bar with EA, both EI, GJ and its mass per length |
| shells | 8 quads of a floor with a bay missing, some wound the other way, make one plate with a hole; three wall panels under one edge make one wall, and the floor keeps the two points the panels meet it at; a warped quad becomes two triangles, with a warning; a pressure on three bays is one surface load over their outline |
| shell edge cases | bays that touch at a corner - an outline FEM-Design could not take - make one plate per element, with a warning, and never a plate over a missing bay; a duplicate element goes in once, with a warning, and its loads still count; a section without thickness is an error, and its pressure goes nowhere |
| connections and masses | a spring link keeps its stiffness by direction; a bar rigid link ties translations only; an Equal DOF between two nodes at one point is not exported, with a warning; a rigid diaphragm covers the hull of its nodes; masses are in tonnes, one that differs by direction is reported, and so is one on a diaphragm's retained node, which FEM-Design has no node for |
| loads | a load case per load pattern with loads, named from the pattern and made unique, then a dead-load case for gravity; a point load's force and moment are two loads; two loads on one beam are one; characters FEM-Design's names never hold are replaced; the combination Alpaca has each pattern's factor, and gravity's factor times its pattern's on the self-weight |
| countries | S355 and C30/37 come from the library of each national annex the export offers |

## In Rhino and FEM-Design

The first half needs Rhino, and opening the files needs FEM-Design, which runs on Windows. Build an
Alpaca model, plug it into Alpaca4DToFEM-Design, set Save to true and open the `.struxml` in
FEM-Design. Check, against Alpaca:

- the bars, their sections and their local axes, columns included;
- the trusses and the end releases of beams with hinges;
- the plates and walls, joined to each other and to the bars;
- the supports and connections;
- the load cases and the combination Alpaca: a linear analysis of it should give Alpaca's
  displacements and reactions, to within the differences the report gives - library materials and
  profiles are FEM-Design's own, and its self-weight comes from its own densities.

Not yet checked in FEM-Design: custom materials (`custom` with E, ν and G the same in all three
entries), custom sections with no material family (`fd-mat` of -1), and line loads on fictitious
bars.
