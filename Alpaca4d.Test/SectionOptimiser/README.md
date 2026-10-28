# Section optimiser

    ./run.sh

`Section Optimiser` sizes beams from lists of sections. A `Design Group` names its beams by
ElementId, the way the result filters do (`Col*`, `Beam_3`, a tag, `regex:`), and gives the
sections they may have; it gives all its beams one section, so that a member drawn as several beams
ends up as it will be built. A beam in no group takes the family of the section it starts with in
Section Library (see below), and has its own section of it. The optimiser's own `ElementId` input,
in the same terms, says which beams take part: left empty, every beam. A beam it leaves out keeps
its section, whatever group it is in, and is still analysed and checked. Then, round by round:

1. analyse the model;
2. check every beam taking part with every section of its list on the forces it carried, with the
   EN 1993-1-1 check of `Utilisation`, the worst of the steps checked governing;
3. put a design group on the first section of its list that keeps every one of its beams at or
   below `MaxUtilisation`, and a beam in a family on the first that keeps it there. When none
   does, use the one with the lowest utilisation;
4. rebuild the model with the new sections (`Model.WithBeamSections`) and go back to 1, until a
   round changes nothing or `Iterations` analyses have been made.

Analysing again is the point. A new section changes the self-weight and, in a statically
indeterminate structure, where the forces go, so a beam sized on one round's forces may not hold on
the next.

## The order of the list

The sections are tried in the order the user gives them, and never sorted. The user decides what
comes first: the lightest, the cheapest, the one in stock. A section given twice counts where it
first comes.

## Families

`SteelCatalogue.FamilyOf` knows a section by its kind and its name, as Section Library names it, in
any case. An I-section's family is its series, the letters its name starts with: IPE, HEA, HEB or
HEM. A CHS's family is every CHS, an RHS's every RHS, and a pair of angles' every pair, with the gap
of the one given. The family's sections are made of the starting section's material, smallest area
first (the order Section Library's All gives), with the starting section itself in its place, so a
beam already on the right one does not count as changed. Each beam has its own section of the
family. A section the catalogue does not have, such as a welded I, a rectangle or an Elastic
Section, has no family, and its beam keeps its place: a remark, or a warning when `ElementId` picked
it.

Families are long (205 CHS, 246 RHS), so a beam's sections are checked only as far as the first that
passes, and a design group drops a section at the first beam it fails. Each check is kept for the
rest of the round.

## Screening and parallel checking

Before a candidate's whole check, `SteelCheck.LowerBound` works out axial force, shear and bending
against the section's full plastic resistance, with nothing reducing it: no torsion, no shear
interaction, no elastic modulus for Class 3, and no buckling. The whole check makes each of those
against a resistance no larger, so its `Max` is never below the bound. A candidate whose bound is
above `MaxUtilisation` fails without its whole check. Only the search for the first that passes
uses the bound: when none passes, the nearest is found from whole checks, so the choice is the same.
Beams are then checked on several threads; each beam's walk is its own, and the steel sections they
share are made once, under a lock.

On the 20-element beam (10 cores, mono), the checking takes 44.7 ms with neither, 12.5 ms with
screening alone, 13.9 ms in parallel alone and 8.0 ms with both, out of about 130 ms per analysis.
372 whole checks become 60. The test also runs every rounds case four ways, with screening and
parallel checking on and off, and gets the same sections, utilisations and iterations each time. It
also checks the bound against the whole check on 2,000 random sets of forces over every family: it
is never above, and it rules out about four in five of the candidates that fail.

## Coming round again

A beam can move back up its list as well as down it, so a design can go round in a circle. A light
section draws more force onto its beam, fails, and the beam moves on to a heavy one. The heavy one
sheds the force, and the light one would do again. When a round proposes a design analysed before
(other than the current one), the rounds after it only let a beam stay where it is on its list or
move on down it. Every round then either changes nothing or moves a beam on, so the rounds end. Some
beams may be left further down their list than they need to be, which their `Utilisation` shows,
and the component says so.

## Analysis

Every iteration is a static analysis in one step of load control, solved as Analysis Settings
solves one by default (Transformation constraints, RCM numbering, the EnergyIncr test). The
component asks only for the system of equations and the algorithm, in its folded Analysis menu, and
checks the one recorded step. `SectionSizing.Run` itself checks the steps `SizingOptions.Steps`
names, or the last recorded one, and every one of a transient analysis; the test's recorder files
are solved once per load case, because MPCO numbers its steps on from one analysis to the next
within one OpenSees run.

## What the test covers

- **The order**: kept as given, each section once.
- **Choosing**: the first at or below the target, exactly at the target passing, the nearest when
  none passes, -1 (keep) when none could be checked, a floor, and a group's one section judged by
  its worst beam, where a section one beam could not be checked with is not one for the group.
- **Grouping**: wildcards and tags; a beam in two groups, a group with no ElementIds (errors); a
  group with no sections or no beam (warnings).
- **Families**: an HEA200's family is the 24 HEAs, of its material, smallest area first, with
  itself in its place; a lower-case name is found; a welded I and an Elastic Section have none; the
  CHS are all 205; a pair of angles keeps its gap. Design groups come first, then one family list per
  starting section, each beam on its own; a beam with no family is a remark, or a warning when
  ElementId picked it. A beam's list is checked only as far as the first that passes.
- **The bound**: never above the whole check, on random forces over IPE, HEA, HEB, HEM, CHS, RHS
  and pairs of angles; and screening and parallel checking leave every rounds case unchanged.
- **Taking part**: only the beams `ElementId` picks; a group with none of them is a remark; a beam in
  two groups is no error when it is left out; a beam picked in no group is a warning; an empty
  filter is every beam in a group. In the rounds, a beam left out keeps its section and is still
  checked, and a group is sized on the beams that take part; with no design group at all, two
  cantilevers starting as HEA1000s go to the first HEAs that hold them.
- **The rounds** on two recorder files OpenSees solves here, heavy and light, for two 3 m
  cantilevers. They are statically determinate, so each round reads whichever file the test picks
  for the design. Sized each on its own, as a family's beams are: with fixed forces the second round
  only confirms the first; a list given as I240, I400, I300 puts the heavier-loaded beam on the
  I400, though the I300 would hold it; a lower threshold means sections further down the list. A
  design group puts both beams on the section the worse one needs; Iterations 1 stops unconverged. Then a design that comes round: the light section brings the heavy forces and
  the heavy one the light, which ends on the heavy section in three rounds, the last two onward
  only.

## A simply supported beam

`SimpleBeam.cs` sizes an 8 m simply supported beam, drawn as 20 elements, under 20 kN/m and its
own weight, in S355, starting as IPE300 with no design group, so within the IPE family. Unlike the
rounds above, every round is a real analysis: the test writes the design as an OpenSees deck, each
element with its own section and its self-weight from its own area, solves it and reads it back.
Only the writing of the deck stands in for Assemble.

| case | midspan | ends | mass (IPE300 throughout: 326 kg) |
| --- | --- | --- | --- |
| top flange held (L_LT = 0.4 m) | IPE270 at 0.994, bending | IPE160 at 0.731 | 234 kg |
| no lateral restraint (L_LT = 8 m) | IPE450 at 0.926, LTB | IPE270 at 0.780 | 509 kg |
| one design group, held | IPE270 at 0.995 | IPE270 at 0.231, shear | 276 kg |

By hand, M = (20 + 0.36) 8² / 8 = 163 kNm at midspan, against Wpl,y fy = 461 cm³ x 355 N/mm² =
164 kNm for Alpaca4d's IPE270, which has no root radius. Each case converges in two rounds.

Sizing each element on its own suits bending and shear, which are local. Lateral-torsional buckling
belongs to the member: with L_LT longer than an element, each element is checked on its own peak
moment taken as uniform over L_LT, so the ends come out lighter than the member they belong to could
really be. For a member drawn as several elements, a design group, which gives it one section, is
the honest choice.

The beams are stubs and the model is never assembled, because Assemble needs the native Rhino
library. `SectionSizing.Run` takes the rebuild as an argument for that reason. `Model.WithBeamSections` itself - new beams for the changed sections, line loads moved onto
them, hinges rebuilt - is only exercised inside Rhino.
