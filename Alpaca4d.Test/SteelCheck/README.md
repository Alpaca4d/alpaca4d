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
| `BucklingY`, `BucklingZ` | 6.3.1 | N against χ A fy/γM1, with L_cr about each axis |
| `LTB` | 6.3.2 | M against χLT W fy/γM1 for each piece between lateral restraints, M_cr on the member's own diagram |
| `InteractionY`, `InteractionZ` | 6.3.3, Annex B | (6.61) and (6.62), named by the axis their χ is about: (6.61) is `InteractionZ` for an I |

**Not covered yet**, and said so in every report and on the component: torsional and
torsional-flexural buckling (6.3.1.4), warping torsion, shear buckling (EN 1993-1-5) and Class 4
sections. 6.3.3 is for doubly symmetric sections: an unequal I or a pair of angles in compression and
bending is left without an interaction, and without `Max`.

## Lateral-torsional buckling

`CriticalMoment` works out M_cr on the diagram the member carries: the classical energy solution
(Timoshenko and Gere; Trahair) that LTBeam solves. Lateral deflection and twist are each a sine
series between fork supports, Wagner's term covers monosymmetry, and a load-height term puts the load
where `LoadPosition` says. A forceBeamColumn's moments between its nodes are a parabola under a
uniform load, whatever the material, so a least squares parabola through the integration sections
lands on them. With M quadratic every integral is closed-form, and eight terms hold M_cr to 0.01% of
thirty.

`SteelMember.LateralTorsionalLength` splits the member into equal pieces no longer than it, each
checked on its own part of the diagram. Longer than the member, it stands for an unrestrained length
the member is only part of, whose diagram cannot be known from this member, so the peak is taken as
uniform over all of it. χLT is by 6.3.2.3 for a rolled or equivalent welded I (λ̄LT,0 = 0.4, β = 0.75,
f = 1) and by 6.3.2.2 for the rest. 6.3.2.2(4) lets `LTB` ignore buckling at low M_Ed / M_cr, but
6.3.3 takes χLT from the curve regardless, because compression lowers M_cr.

`It` is worked out from the plates (`BeamSectionStress.TorsionConstant`), never from `ISection.J`,
which hands the solver Iy + Iz.

## 6.3.3

Annex B (method 2). Table B.2 for open sections, B.1 for closed ones, and Cm from Table B.3 on the
member's own diagram. Cm is 1 when the buckling length about that axis is longer than the member,
since the diagram that matters is not this member's to tell, and 0.9 when `Sway` is set. Applied to
members with compression and bending, and to members bent about both axes without compression. With
no bending it is 6.3.1, and it reads zero.

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
ISection to the digits printed. Box walls are classified on h − 3t, as the section tables do for hot-finished tubes.

## Benchmarks

The rules on their own, each set up in the example's own units and held to the digits printed:

| source | section | checked |
| --- | --- | --- |
| JRC 2014 *Design of steel buildings with worked examples*, Simões, Example 1 | HEB 340, S355, column 4.335 m | ε, web and flange c/t and class, N_c,Rd 6067 kN, λ̄y 0.39, λ̄z 0.75, curves b/c, χz 0.69, N_b,z,Rd 4186.2 kN |
| ibid., Example 2 | IPE 400, S355, beam | c/t and class, M_pl,Rd 464.0 kNm, V_pl,Rd 875.0 kN, Av 42.69 cm² from 6.2.6(3), no shear buckling |
| Gardner & Nethercot, *Designers' Guide to EN 1993-1-1*, Example 6.6 | UKB 457x191x98, S275 (fy 265) | c/t and class, N_pl,Rd 3312.5 kN, M_pl,y,Rd 590.95 kNm, M_N,y,Rd 425.3 kNm |
| Structville 2022, biaxial bending | UKC 254x254x89, S275 | M_N,y,Rd 182.1, M_N,z,Rd 132.6 kNm, β 2.5, (6.41) 0.240, χz 0.783 |
| Designers' Guide, Example 6.7 | CHS 244.5 x 10, S355, column 4 m | **end to end through Alpaca4d's own section** - a tube's properties follow exactly from its dimensions: A, I, class 1, N_c,Rd 2615 kN, N_cr 6572 kN, λ̄ 0.63, χ 0.88, N_b,Rd 2297 kN, utilisation 0.71 |
| ECCS No. 119, Worked Example 5, as SOFiSTiK verification DCE-EN24 (2024) | IPE 500, S235, 3.75 m between forks | N_cr,z 3157 kN, λ̄z 0.927, χz 0.644, M_cr,0 895 kNm, **C1 1.194 of a −100 / 199 / −100 kNm diagram against SOFiSTiK's eigenvalue analysis**, λ̄LT 0.695, χLT 0.787 by 6.3.2.2 |
| Schneider, *Bautabellen für Ingenieure* 21st ed., Example 8-41, as SOFiSTiK DCE-EN29 (2024) | HEA 200, S235, 8 m column held at mid-length, top-flange load | λ̄y 1.029, χy 0.58, λ̄z 0.855, χz 0.63, N_cr,z 1736 kN, λ̄LT 0.676, χLT 0.88 by 6.3.2.3, Cmy 0.95, CmLT 0.80, kyy 1.292, kzy 0.936, (6.61) 0.96, (6.62) 0.79 |

The Schneider column also runs **end to end through Alpaca4d's own HEA 200**. The diagram gives Cmy
and CmLT as the book has them, and C1 = 1.336 at the shear centre against the book's ζ = 1.35.
(6.61) reads 1.00 against 0.96 because the HEA 200 has no root radii, so A is 5% low. M_cr on the
top flange is 256 kNm against the book's 220.9. The book's load-height term is DIN 18800's fixed
0.5 zp whatever the diagram, while in each half most of the moment comes from the reaction rather
than the load. The test holds both interactions to 6%.

M_cr is held to the closed forms - uniform moment, doubly and singly symmetric - and to the C1 and C2
of ENV 1993-1-1 Annex F (uniform load 1.127, ψ = 0.5 1.323, and the three-factor formula on the top
flange). Then Alpaca4d's shapes against hand formulas:

- A, Wpl, Wel, Av, the web's share of Wpl, a, the plastic neutral axis of an unequal I;
- buckling curves for rolled and welded, hot-finished and cold-formed;
- It, Iw, the shear centre and β;
- classification under bending, compression, N + M and low compression.

Members are worked by hand:

- bending, 6.30 under high shear, 6.36 with axial force, torsion with 6.2.7(9);
- flexural buckling, the 0.04 N_cr rule, Class 4;
- LTB under uniform moment in closed form, longer than the member, restrained, at three load
  heights, for a box by the general case;
- the monosymmetric I both ways round;
- 6.3.3 for an HE 300 B with Cm, sway and a longer buckling length.

Last is the whole chain: an MPCO file written the way Alpaca4d writes beams, read and checked, with a
steel I, a steel box in S275 and a glulam beam that is reported and left unchecked.

Needs a built `Alpaca4d.Core`, a C# compiler and a net48 RhinoCommon. OpenSees on `PATH` (or at
`$OPENSEES`) is optional: without it the whole-chain check is skipped.

The Grasshopper component itself needs Rhino to run and is not exercised here.
