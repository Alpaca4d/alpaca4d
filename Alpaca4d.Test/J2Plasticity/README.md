# J2Plasticity

    export PATH=/Applications/OpenSees3.8.0/bin:$PATH
    ./run.sh

A metal that yields: OpenSees' `J2Plasticity`, von Mises plasticity with linear isotropic hardening,
in the nD unit of the same name. The suite checks what Alpaca4d writes and reads back for it, then
puts Alpaca4d's own material and section lines into decks it writes and runs them.

## What Alpaca4d writes

    nDMaterial J2Plasticity 1 175000000 80769230.7692308 355000 355000 0.0 0
    nDMaterial PlateFromPlaneStress 2 1 80769230.7692308
    # alpaca:density 1 7850

* **K and G**, from E and ν: the command takes those.
* **Fy twice and delta 0**: no saturation term, so the hardening is H alone.
* **A plate form for shells**, under its own tag, which Layered Shell and Plate Fiber sections
  name. OpenSees' own plate form of J2Plasticity, J2PlateFiber, holds sigma_zz = 0 to an absolute
  1e-8 and stops converging on a triangular ASD shell soon after yield, printing "More than 25
  iterations" tens of thousands of times. Wrapped around the plane stress form it runs to the end
  with no warnings; its transverse shear stays elastic.
* **The density as a comment.** J2Plasticity takes none, so in OpenSees these elements have no
  mass. Gravity Load reads the density off the material in Alpaca4d, so self-weight is right;
  Natural Vibration and a time history in Run Analysis warn. Deserialise reads the comment back.

**Units.** In N and m J2Plasticity does not yield at all: one brick pulled to three times Fy
stays elastic, with OpenSees 3.8.0 and master alike. In kN and m, which Alpaca4d always writes, and
in N and mm, it holds at Fy.

## What OpenSees does with it

| deck | what it pins down |
|---|---|
| `brick.tcl` | one SSP brick pulled to 0.005: E times the strain below Fy, Fy past it |
| `strip_*.tcl` | a clamped strip 1 m x 0.1 m x 10 mm pushed at its tip to 0.6 m: quads and triangles through a 10-layer Layered Shell, quads through a Plate Fiber section. It levels off at 1.11 (quads) and 1.18 (triangles) of the plastic load from beam theory: the clamped root holds the strip across its width. Plate Fiber reads 1.01: its five Lobatto points take a fully plastic section 9% low. No iteration warnings. |
| `beam_tet10.tcl` | a clamped beam 1 m x 0.1 m x 0.1 m of ten node tetrahedra, two cells through the depth: within 2% of beam theory with shear before yield, level at 1.16 of the plastic load by L/10 |
| `beam_ssp.tcl` | the same beam of SSP bricks, six through the depth: 128 kN at L/10 against 103, and still rising 6.6%. SSPbrick sets its hourglass stiffness once from the elastic tangent (SSPbrick::GetStab), so past first yield it is too strong. Assemble remarks on it. |

The tetrahedra need an OpenSees that knows `TenNodeTetrahedronStd` (OPENSEES_TET10, default
/Applications/OpenSeesLatest-Mumps/bin/OpenSees).

## Why not ASDPlasticMaterial3D

Its von Mises model yields at the same stress and gives the same curve, but whatever
`tangent_type` it is given (Elastic, Continuum, Numerical_Algorithmic_FirstOrder or SecondOrder),
the global Newton converges as with an elastic tangent: 16 iterations a step at first yield, 114 by
the time the tetrahedron beam levels off, where J2Plasticity takes 3. Its documentation also names
the options `method` and `tangent_operator`; the parser reads `integration_method` and
`tangent_type`, and silently ignores the others.
