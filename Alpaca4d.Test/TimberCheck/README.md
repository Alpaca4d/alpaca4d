# Timber utilisation, EN 1995-1-1

    ./run.sh

`Utilisation` checks every beam against the design code of its material. For timber - a material
whose grade is an EN 338 class (C14-C50, D30-D70) or an EN 14080 glulam class (GL20h-GL32h,
GL20c-GL32c), from Material Library Elastic or named after one - that is EN 1995-1-1, recommended
values throughout:

| output | clause | what |
| --- | --- | --- |
| `Axial` | 6.1.2, 6.1.4 | σ_t,0,d against f_t,0,d (with k_h), or σ_c,0,d against f_c,0,d |
| `ShearY`, `ShearZ` | 6.1.7 | 1.5 V / (k_cr A), 4V / (3 k_cr A) for a circle, against f_v,d; k_cr 0.67 |
| `Torsion` | 6.1.8 | St. Venant τ against k_shape f_v,d |
| `BendingY`, `BendingZ` | 6.1.6 | σ_m against f_m,d, k_h by the depth each moment bends across |
| `Combined` | 6.1.6, 6.2.3, 6.2.4 | (6.11)-(6.12) in bending, (6.17)-(6.18) with tension, (6.19)-(6.20) with compression; k_m 0.7 for a rectangle |
| `BucklingY`, `BucklingZ` | 6.3.2 | σ_c,0,d against k_c f_c,0,d, L_cr about each axis |
| `InteractionY`, `InteractionZ` | 6.3.2 | (6.23) and (6.24); left to (6.19)-(6.20) when λ_rel ≤ 0.3 about both axes |
| `LTB` | 6.3.3 | (6.33), and (6.35) with compression, for each piece between lateral restraints |

`f_d = k_mod f_k / γM` (2.14). k_mod comes from Table 3.1, from the member's service class and the
duration of the loads: the shortest of the load patterns that act (3.1.3(2)), each pattern's set in
Load Pattern's Duration menu, permanent when not set. γM is Table 2.3's, 1.3 for solid timber, 1.25
for glulam and 1.0 in an accidental combination, unless one is given. k_h is (3.1) for solid timber
(150 mm, ρ_k ≤ 700 kg/m³) and (3.2) for glulam (600 mm).

Sections: a solid rectangle (Rectangle Section) or a solid circle (Circle Section). Mz bends across
the rectangle's Height, My across its Width.

## Lateral-torsional buckling

σ_m,crit is M_cr / W "according to the classical theory of stability, with 5-percentile stiffness
values" (6.3.3(2)): M_cr from `CriticalMoment`, the energy solution the steel check uses, on the
member's own moment diagram, with E_0,05 and G_0,05, fork supports and the load at the height Load
gives. For a uniform moment it is (6.32) exactly; for a uniform load it is within 1.4% of Table 6.1's
l_ef = 0.9 l, and on the top face within 5% of 0.9 l + 2h. G_0,05 is tabulated for glulam (540
N/mm²) and G_mean E_0,05 / E_0,mean for solid timber.

## What the tests check

- **Rules**: Table 3.1 and 2.3, k_h at, below and past its caps, k_c, k_crit at each branch,
  k_shape, torsion stresses of a square, a strip, a 2:1 rectangle and a circle.
- **Grades**: the table's EN 338:2016 and EN 14080 values, name lookup, a material named after a
  class.
- **Sections and members**: every output against the same expressions written out by hand in the
  test, not taken from `Ec5.cs`.
- **Durations**: the shortest pattern, a pattern scaled to nothing, one with no duration, through
  `LoadCombination.Apply`, and γM 1.0 in an accidental combination.
- **Whole chain**: `timber.tcl`, three cantilevers OpenSees solves, read back from the MPCO file.

## Not covered

Compression and tension across the grain (6.1.5: bearing at supports and under point loads), shear
and torsion together, notched, curved and tapered members (6.4, 6.5), system strength k_sys (6.6,
taken as 1), connections, fire, and the serviceability checks (deflection with k_def, vibration).
