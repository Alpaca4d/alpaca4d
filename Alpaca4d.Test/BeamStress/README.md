# Beam stresses

    ./run.sh

`Beam Stresses` recovers the stresses in a beam from the six section forces OpenSees reports, by
elastic beam theory. Nothing about it depends on the material: it is what a linear elastic section
of that shape carries under those forces. Material checks are a separate step.

## What is reported

| output | what it is |
| --- | --- |
| `σN` | N / A, tension positive |
| `σMy`, `σMz` | the largest bending stress each moment causes on its own |
| `σmax`, `σmin` | the extreme direct stress anywhere on the section, N, My and Mz together |
| `τV` | peak shear from Vy and Vz |
| `τT` | peak shear from torsion |
| `VonMises` | √(σ² + 3τ²), σ the larger of σmax and σmin in size, τ = τV + τT |

Each is the worst the section sees, not the value at one point, and **Von Mises is an upper bound**:
it puts the worst σ with the worst τ, which rarely meet - bending peaks at the corners, where a free
surface carries no shear, and shear peaks on the neutral axis, where bending is zero. Conservative
on short deep members, close to exact on slender ones.

## Direct stress

σ = N/A − Mz·y/Iz + My·z/Iy, with y and z measured from the centroid. That is the convention
`FiberSection3d` strains its fibres by, so a positive Mz compresses the +y side. It is checked
against the solver rather than assumed: `fibre.tcl` loads two elastic fibre sections every way at
once, and the stress recovered at a corner fibre matches the stress OpenSees integrated there to
within the fibre discretisation, a few hundredths of a percent.

Both sections are lopsided on purpose - an I with unequal flanges and a pair of angles - because
a recovery that bent the wrong way, or measured from the middle of the drawing rather than from the
centroid, would still pass on anything symmetric.

Axes follow the drawing. Model View lays a section's curves on a plane whose X is the beam's local
z and whose Y is its local y, so a section drawn tall is tall in local y.

## Shear

| shape | τ from V | τ from T |
| --- | --- | --- |
| rectangle | Jourawski, 1.5·V/A | Saint-Venant, Roark's fit (within 0.5% of the tables) |
| round bar, tube | Jourawski across a diameter: 4V/3A solid, → 2V/A thin | T·R / Ip |
| box | Jourawski through both walls | Bredt, T / (2·Am·t), thinnest wall |
| I | Jourawski: mid-web for Vy, each flange outstand at the web for Vz | T·tmax / J, J = Σ b·t³/3 |
| two angles | Jourawski: both long legs for Vy, the short leg's root for Vz | T·t / J, open section |

τV = √(τVy² + τVz²). Where the two peak at one point - the middle of a rectangle, the side of a
tube - they are at right angles there.

The Jourawski search works on the rectangles every polygonal shape is made of, cut by cut and
**piece by piece**: a cut just outside the web of an I goes through both flanges, and beyond it lie
two separate outstands whose shear each has nowhere to go but back across its own flange. Lumping
them would average a deep flange with a shallow one.

The two angles are tied across the gap by a link with no area. A double angle only works as one
member because battens or packing plates make the angles bend together, and those carry shear
between them; without the tie, a cut near the tip of a short leg read the whole angle's shear going
through the tip - 29% too high on the 100 × 75 × 8 checked here. The link takes no stress of its
own: what a batten carries depends on the batten spacing, which the section does not know.

OpenSees does not resolve shear over a beam section, so these are checked against the formulas
only, worked out independently of the rectangles.

## Sections with no shape

An Elastic Section is a list of properties with no geometry behind it. Its beams give σN and
nothing else; the component leaves their other branches empty and says so.

## `ISection.J` is the polar moment

`ISection` hands the solver J = Iy + Iz, which is the torsion constant of a circle - hundreds of
times too stiff for an I (IPE 300: 8,600 cm⁴ against about 16 cm⁴). τT here uses the proper
J = Σ b·t³/3, so it is the right stress for the torque the analysis reports; the torque itself came
out of an over-stiff member, which is a matter for the section, not for this.

## Benchmarks

### Published worked examples

From R.C. Hibbeler, *Mechanics of Materials*, 8th ed. (Pearson, 2010), instructor's solutions manual,
chapters 6, 7 and 8. Each problem is set up in its own units and has to agree with the printed answer
to every digit printed. Hibbeler writes σ = −Mz·y/Iz + My·z/Iy with y up the depth - OpenSees'
convention - so his moments go in unchanged.

| problem | section | what is checked | printed |
| --- | --- | --- | --- |
| 7-2 | I 340 deep, flanges 200 × 20, web 20 | I, τmax under V = 20 kN | 0.2501·10⁻³ m⁴, 3.459 MPa |
| 7-7 | I 310 deep, flanges 200 × 30, web 25 | I, τmax under V = 30 kN | 268.652·10⁻⁶ m⁴, 4.62 MPa |
| 7-4 | T, flange 12 × 3 in., web 4 × 6 in. | centroid, I, τmax under V = 12 kip | 3.30 in., 390.60 in⁴, 0.499 ksi |
| 6-117 | I 170 deep, flanges 200 × 10, web 10 | Iz, Iy, σmax and σmin under Mz and My together | 7.60 MPa |
| 8-35 | I 7 in. deep, flanges 4 × 0.5 in., web 0.5 in. | I, σ at the top fibre | 51.33 in⁴, −9.41 ksi |
| 6-118 | 300 × 600 block with an off-centre hole (a box with unequal walls) | centroid, Iz, Iy, σmax, σmin | 0.2893 m, 126 / −131 MPa |
| 7-20 | round bar, r = 2 in. | τmax under V = 30 kip | 3.18 ksi |
| 6-121 | round bar 30 mm, bent both ways | σmax from the resultant moment | 161 MPa |
| 8-36 | round bar 10 mm, N + Vy + T + Mz | σ at the extreme fibre, τ from torsion | −215.43 MPa, 101.86 MPa |
| 8-63/64 | pipe 6 in. × 0.25 in., N + Vy + T + My + Mz | A, I, σ at two points, τ where shear and torsion meet | −125, −17.7, 67.2 ksi |
| 8-65/66 | pipe 2 in. × 0.25 in., Vy + Vz + T + My + Mz | I, σ at two points, τ from Vy, Vz and T separately | 605, −466, 62.17, 35.89, 483.91 psi |
| 8-57/58 | round bar 2 in., T + two shears | τ from each, and τ bounding both published points | 4.584, 0.2546, 0.2122 ksi |

All agree. Where a published value is the stress at a named point rather than the section's worst,
the point is checked with `NormalStressAt` and the reported extreme is checked to bound it.

### Exact elasticity

[sectionproperties](https://github.com/robbievanleeuwen/section-properties) 3.10.2 solves the
Saint-Venant warping function over a finite element mesh - the exact stresses in a prismatic bar,
not beam theory. Steel, ν = 0.3. `fe_check.py` regenerates the numbers.

| | exact | Alpaca4d | |
| --- | --- | --- | --- |
| I 340 × 200, σ under Mxx, Myy, and everything at once | 6.798 / 37.22 / 21.385 MPa | same | to 10⁻⁶ |
| round bar, σ under two moments | 50.939 MPa | 50.930 MPa | the FE circle is a 128-gon |
| I, τ from Vy at mid-web | 3.4588 MPa | 3.4591 MPa | |
| I, τ from T on the faces | 10.559 MPa | 10.714 MPa | 1.5% safe: J = Σbt³/3 leaves out the junction |
| I, Von Mises, worst point | 21.959 MPa | 23.57 MPa | upper bound, 7% high here |
| round bar, tube, CHS: τ from T | 5.094, 1.091, 3.273 MPa | 5.093, 1.078, 3.243 MPa | within 1.2% |
| **round bar, τ from V** | 1.764 MPa | 1.698 MPa | **exact 3.9% higher** |
| **tube 200 × 20, τ from V** | 1.927 MPa | 1.754 MPa | **exact 9.9% higher** |
| **CHS 168.3 × 8, τ from V** | 5.156 MPa | 4.956 MPa | **exact 4.0% higher** |

Direct stress and torsion agree. **Shear from a shear force does not, on round sections**, and that
is beam theory, not a bug: Jourawski takes the shear as uniform across a cut, and on a circle it is
not. The exact peak depends on Poisson's ratio, so it cannot be had without the material. Hoogenboom
(TU Delft, *Shear stiffness and maximum shear stress of tubular members*) found the same 200 × 20 tube
"10% larger than predicted" by FE, and proposes τmax = (2 + t/r)·V/A for tubes. The numbers above
are what EN 1993-1-1 6.2.6(4) and every textbook use; that they read up to 10% low on thick tubes is
pinned by the checks, so a change of formula shows.

On the I, the exact solution's own peak shear sits in the sharp corners between web and flange,
where it is singular and grows with the mesh (4.53 → 4.55 MPa from Vy as the mesh is halved). Real
sections have root radii there. Away from the corners, the exact stress is beam theory's.

### OpenSees fibre sections

`fibre.tcl` builds elastic fibre sections, loads them with N, My and Mz together, and compares the
stress OpenSees integrated in each fibre with the stress recovered from the section forces it
reported: four corner fibres each on the lopsided I and the angles, and **every fibre** of a round
bar (1728), a tube (432) and an IPE 300 (1440). Worst difference 0.09%, 0.06% and 0.02% of the peak -
the same size as the fibre mesh's own error in I, which the check prints beside it.

## What is checked

- every shape against its textbook formula, including the unequal I (Vz takes the wider flange's
  own value, not an average) and the angles with and without a gap;
- twelve published worked examples, six on I and T sections and six on bars and pipes;
- exact elasticity from sectionproperties, with the known gap in shear on round sections pinned;
- the recovered direct stress against OpenSees fibre stresses, at corners of two lopsided sections
  and at every fibre of a bar, a tube and an IPE 300;
- `Read.BeamStresses` on a real MPCO file written the way Alpaca4d writes a beam, with a different
  value on every force so that a force read into the wrong slot reads as the wrong stress;
- that an Elastic Section beam is marked as having no shape and still reads σN.

Needs a built `Alpaca4d.Core`, a C# compiler and a net48 RhinoCommon. OpenSees on `PATH` (or at
`$OPENSEES`) is optional: without it only the arithmetic runs and the rest is skipped with a note.
