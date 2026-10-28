# Interaction domain

    ./run.sh
    OPENSEES=/Applications/OpenSeesLatest-Mumps/bin/OpenSees ./run.sh

Checks `Alpaca4d.Section.InteractionDomain`, which Interaction Domain runs, the checks Domain Check
makes with it, and `MomentCurvatureAnalysis`, which Moment Curvature runs against the same limits.
Every domain is built the way the component builds one: `MaterialCurves` has OpenSees trace each
material's stress-strain curve, and the ultimate strain planes are swept and integrated in C#.
Sections are made of point fibres, which need nothing of RhinoCommon but `Point3d`, so this runs
under mono; a patch's mesh vertices are stood in for by check points on the outline.

| case | against |
| --- | --- |
| 300 x 500 rectangle, C30/37 and B500 design values, 3 φ20 + 2 φ16 | pure compression fcd Ac + Es εc2 As and pure tension fyd As; 24 random ultimate planes imposed on a `zeroLengthSection` of the same fibres, N My Mz to 0.05; every point of the surface with a fibre at its limit, the bars at εud at N = 0 among them; Moment Curvature's own deck at N = -1000 and 0, read up to the first fibre at its limit, against the Mz = 0 cut |
| an L of two 250 x 800 legs | the same, the Moment Curvature deck's neutral axis turning as Mz stays zero |
| HEA 300 in S355 with its root fillets, εud = 5% | A, Wpl,y, Wpl,z against the tables; A fy, Wpl,y fy and Wpl,z fy (from below: the web sits in the elastic core about z); Mpl,y - N²/(4 tw fy) while the web carries N, where EN 1993-1-1 6.2.9.1 makes no reduction; the N-My cut |
| the same, εud = fy/E | Wel,y fy and Wel,z fy exactly, and N/Npl + My/Mel,y + Mz/Mel,z = 1 round a level; checked at its fibres rather than its outline, first yield comes out high |
| checks | N held and half the boundary moment: factor 2, at the boundary point; all three halved: proportional factor 2; N past the resistance: 0 with a note; no moment with N held: the factor goes on N; 1.2 times the boundary moment: utilisation 1.2 |
| bars wrapped in MinMax ±0.005 | the bound, just inside it, is the limit in place of εud |
| Moment Curvature on the rectangle, N = -1000, stopped at its ultimate strain | it stops itself, MaxPhi from the domain leaving it most of its increments; the ultimate moment against the domain's cut at 0°, 30° and 90°, the moment held to its direction, and at 180° and -90°, the other way about each, where the unequal bars give another moment; the concrete named as what crushes; first yield before the ultimate state; run on past it, the same ultimate state; an axial force past the resistance refused; Direction read as y, z or degrees |
| Moment Curvature on the L at 45° | the ultimate moment against the domain's cut, the neutral axis turning |
| Moment Curvature on the HEA 300, N = 0, εud = 5% | first yield at Wel,y fy and fy/E over half the depth, the secant stiffness E Iy; the ultimate state at εud over half the depth with Mpl,y; ductility εud / εy |

Concrete01 with its residual equal to its peak, fpcu = fpc, is Eurocode 2's parabola-rectangle (n =
2), and Steel01 with b = 0 is elastic-perfectly plastic, so these are the domains of EN 1992-1-1 6.1
and of EN 1993-1-1's plastic and elastic cross-section resistance.
