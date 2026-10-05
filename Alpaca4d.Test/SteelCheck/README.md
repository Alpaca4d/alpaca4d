# Steel utilisation, EN 1993-1-1

    ./run.sh

`Utilisation` checks every beam against the design code of its material. For steel that is
EN 1993-1-1, recommended values throughout (γM0 = γM1 = 1.0, η = 1.0):

| output | clause | what |
| --- | --- | --- |
| `Class` | 5.5, Table 5.2 | worst along the member |
| `Axial` | 6.2.3, 6.2.4 | N against A fy/γM0 |
| `ShearY`, `ShearZ` | 6.2.6, 6.2.7(9) | V against Av (fy/√3)/γM0, reduced for torsion |
| `Torsion` | 6.2.7 | St. Venant τ against fy/√3 |
| `BendingY`, `BendingZ` | 6.2.5, 6.2.8 | M against Wpl or Wel fy/γM0, reduced for shear past 0.5 V_pl |
| `Combined` | 6.2.9, 6.2.10 | N + My + Mz with shear: M_N,Rd and (6.41) for Class 1-2, σx ≤ fy for Class 3 |
| `BucklingY`, `BucklingZ` | 6.3.1 | N against χ A fy/γM1, L_cr the same about both axes |

**Not covered yet**, and said so in every report and on the component: lateral-torsional buckling
(6.3.2), bending with compression as a member (6.3.3), torsional and torsional-flexural buckling
(6.3.1.4), warping torsion, shear buckling (EN 1993-1-5) and Class 4 sections. A member in
compression and bending is not verified by this alone.

## Where the grade comes from

`IUniaxialMaterial.Grade`. Material Library Elastic stamps it from its JSON - built in or custom -
and a material made by hand is recognised by its name: "S355", "S355JR", "s355 j2" all find S355.
Anything else is `Unknown` and left unchecked, with a warning. fy follows Table 3.1, including the
lower values for plates thicker than 40 mm, now in `steel_properties.json` as `fy_40_80`.

## Axes

Alpaca4d's, as everywhere else: local y along the depth as drawn, local z across it. For an I that
makes **bending about z the major axis** - the standard's y-y - and `ShearY` the shear along the web.
`BendingZ` and `BucklingZ` are the major axis of an I; `BendingY` and `BucklingY` the minor.

## Classification

Every part is classified for the stress it actually carries at each integration section, so the
class can change along a member and each section is checked in its own:

- an outstand anywhere in compression takes the compression limits (9ε, 10ε, 14ε), the lowest;
- an internal part takes α from the plastic distribution - the share on the compression side of the
  plastic neutral axis, moved by the axial force - and ψ from the elastic one. Where the plastic α
  would undersell the compression (a little bending and a lot of axial force), the elastic share is
  used;
- a tube by d/t, a pair of angles by each leg as an outstand plus the angle limits of sheet 3.

**5.5.2(9).** A part that fails Class 3 gets a second chance: Class 3 after all if it meets the
Class 3 limit with ε raised by √(fy/γM0 / σcom,Ed). Without it, every section of an IPE 300 in S355
where the moment passes through zero and a little axial force acts would read as Class 4 - its web in
pure compression has c/t = 39.2 against 42ε = 34.2. **5.5.2(10)** forbids the second chance for
member buckling, which is classified by Table 5.2 alone; a section Class 4 there is not checked for
buckling unless N_Ed ≤ 0.04 N_cr lets buckling be ignored (6.3.1.2(4)).

A check that applies but cannot be made - buckling of a Class 4 member - leaves **`Max` empty** and
`Governing` saying which checks are missing. A largest value over the checks that were made would
read as a verdict on the member.

## Sharp corners

Alpaca4d's I-sections have no root radius and its hollow sections no corner radius, and the check
uses the section the analysis used. Against a rolled catalogue section that makes A and Wpl a few
percent low (IPE 300: Wpl,y 602 cm³ against 628) and a web's c longer (278.6 mm against 248.6), all
on the safe side, and it is why no catalogue worked example can be run end to end through an
ISection. Box walls are classified on h − 3t, as the section tables do for hot-finished tubes.

## Benchmarks

The rules on their own, each set up in the example's own units and held to the digits printed:

| source | section | checked |
| --- | --- | --- |
| JRC 2014 *Design of steel buildings with worked examples*, Simões, Example 1 | HEB 340, S355, column 4.335 m | ε, web and flange c/t and class, N_c,Rd 6067 kN, λ̄y 0.39, λ̄z 0.75, curves b/c, χz 0.69, N_b,z,Rd 4186.2 kN |
| ibid., Example 2 | IPE 400, S355, beam | c/t and class, M_pl,Rd 464.0 kNm, V_pl,Rd 875.0 kN, Av 42.69 cm² from 6.2.6(3), no shear buckling |
| Gardner & Nethercot, *Designers' Guide to EN 1993-1-1*, Example 6.6 | UKB 457x191x98, S275 (fy 265) | c/t and class, N_pl,Rd 3312.5 kN, M_pl,y,Rd 590.95 kNm, M_N,y,Rd 425.3 kNm |
| Structville 2022, biaxial bending | UKC 254x254x89, S275 | M_N,y,Rd 182.1, M_N,z,Rd 132.6 kNm, β 2.5, (6.41) 0.240, χz 0.783 |
| Designers' Guide, Example 6.7 | CHS 244.5 x 10, S355, column 4 m | **end to end through Alpaca4d's own section** - a tube's properties follow exactly from its dimensions: A, I, class 1, N_c,Rd 2615 kN, N_cr 6572 kN, λ̄ 0.63, χ 0.88, N_b,Rd 2297 kN, utilisation 0.71 |

Then Alpaca4d's shapes against hand formulas (A, Wpl, Wel, Av, the web's share of Wpl, a, the plastic
neutral axis of an unequal I, buckling curves for rolled and welded, hot-finished and cold-formed),
classification under bending, compression, N + M and low compression, members worked by hand
(bending, 6.30 under high shear, 6.36 with axial force, torsion with 6.2.7(9), flexural buckling, the
0.04 N_cr rule, Class 4), and the whole chain - an MPCO file written the way Alpaca4d writes beams,
read and checked - with a steel I, a steel box in S275 and a glulam beam that is reported and left
unchecked.

Needs a built `Alpaca4d.Core`, a C# compiler and a net48 RhinoCommon. OpenSees on `PATH` (or at
`$OPENSEES`) is optional: without it the whole-chain check is skipped.

The Grasshopper component itself needs Rhino to run and is not exercised here.
