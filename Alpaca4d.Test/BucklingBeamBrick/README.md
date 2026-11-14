# Linear buckling, beams against bricks

    ./run.sh                     # every structure, about 2½ minutes
    ./run.sh columns lateral     # some of them: columns, portal, arch, lateral

The same structures drawn twice and run through `Buckling.Analyse` as Linear Buckling Analysis runs
them. Drawn as beams, OpenSees gives their stress stiffness: every beam is made PDelta. Drawn as SSP
bricks, `Buckling.SolidStiffness` adds theirs in C# from the stresses OpenSees reports. The two have
nothing in common but OpenSees' material stiffness, so where they meet, both are right.

Each model is run twice and extrapolated as its error goes, h²: the beams at 8 and 16 pieces a
member, the bricks on a mesh and on that mesh halved every way (the finer one is listed). The
first buckled shapes are compared too, by the modal assurance criterion: each brick node is
counted at the nearest beam node, and the bricks' movement there is the mean of theirs, the
movement of the section. The decks are laid out as Assemble writes them. The finer meshes have up
to 21 000 equations, past the 4000 a dense print allows, so this needs an OpenSees newer than
3.8.0 (by default `/Applications/OpenSeesLatest-Mumps`).

| structure | against |
| --- | --- |
| a cantilever column, 0.1 x 0.2 m, 2 m; pinned-pinned, 4 m; fixed-pinned, 3 m | Euler, with the shear of a Timoshenko column (Engesser) |
| a stocky cantilever, 0.2 x 0.3 m, 0.8 m: shear takes 4 % off Euler | the same |
| a cantilever under its own weight, 4 m | Greenhill, (qL)_cr = 7.837 EI / L² |
| a portal frame, fixed feet, 3 x 4 m | the sway alignment chart, and the beams with their corners as points and as rigid |
| a two-hinged circular arch, R = 5 m over 120°, under its own weight | the beams alone: no closed form for a vertical load |
| a deep narrow cantilever, 0.02 x 0.2 m, 2 m, a load on its end | Vlasov's theory, warping held at the root (solved in the test) |
| the same beam on forks under equal end moments | M_cr = π/L √(EI_z GJ (1 + π² EI_w / GJ L²)) |

## Results

2026-11-08, OpenSees master with Mumps as built in `/Applications/OpenSeesLatest-Mumps`, on an M2
Pro: the times are the bricks' finer mesh, start to end. Load factors: for the
columns, the frame and the narrow cantilever, loads in kN; for the narrow beam on forks, an end
moment in kNm.

| structure | closed form | beams | bricks | bricks / beams | bricks / closed form | MAC | bricks, equations, s |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Cantilever column, 0.1 x 0.2 m, 2 m, a load on top | 2155.5 | 2155.5 | 2167.1 | +0.54 % | +0.54 % | 1.000 | 1280, 5400, 5 |
| Pinned-pinned column, 0.1 x 0.2 m, 4 m | 2155.5 | 2155.5 | 2154.8 | -0.03 % | -0.03 % | 1.000 | 2560, 10906, 10 |
| Fixed-pinned column, 0.1 x 0.2 m, 3 m | 7806.4 | 7801.6 | 7821.6 | +0.26 % | +0.20 % | 1.000 | 1920, 8090, 8 |
| Stocky cantilever, 0.2 x 0.3 m, 0.8 m | 155681.1 | 155681.0 | 156804.9 | +0.72 % | +0.72 % | 1.000 | 1536, 5616, 8 |
| Cantilever under its own weight, 0.1 x 0.2 m, 4 m | 273.00 | 272.84 | 273.93 | +0.40 % | +0.34 % | 1.000 | 2560, 10800, 10 |
| Portal frame, fixed feet, 3 x 4 m, 0.1 x 0.3 m members, a load on each head | 3989.4 | 3978.3 (centre lines); 4182.7 (rigid corners) | 4045.8 | +1.70 % | +1.41 % | 1.000 | 4896, 21315, 19 |
| Two-hinged arch, radius 5 m over 120 degrees, 0.1 x 0.3 m, its own weight | - | 346.86 | 346.86 | +0.00 % | - | 1.000 | 4608, 20223, 18 |
| Deep narrow cantilever, 0.02 x 0.2 m, 2 m, a load down on its end (lateral-torsional) | 35.456 | no mode | 35.531 | - | +0.21 % | - | 5120, 20400, 23 |
| Deep narrow beam on forks, 0.02 x 0.2 m, 2 m, equal end moments (lateral-torsional) | 52.957 | no mode | 52.772 | - | -0.35 % | - | 5120, 20618, 24 |

All 31 checks pass. The frame's closed form is the alignment chart for its centre lines.

## What it found

- **Columns.** Bricks and beams agree, and both agree with Euler and Greenhill. The bricks come out
  0.3 - 0.7 % stiffer where the foot is clamped - every node of it held, which also stops the
  foot contracting and swelling with Poisson's ratio, as the Simple Brick benchmark of the docs
  shows for a cantilever's deflection. Pinned at both ends, with no clamp, they meet to 0.03 %.
  With the cantilever's foot free to spread (only its z held, and one line each way), the
  cantilever's +0.54 % drops to +0.10 %. The stocky cantilever, L / b = 4, sits between its two
  feet: +0.72 % clamped, -0.78 % free to spread, its section warping in shear at the foot or not.
- **The portal frame.** On their centre lines the beams meet at points; the bricks' corners are
  solid, and their columns stand 2.95 m clear. With rigid corners (each member stopping at the
  corner's face, a rigid link to its middle) the beams are 5.1 % stiffer than on centre lines. The
  bricks fall a third of the way between: a solid corner is not rigid, the members' bending running
  on into it - which is why frame programs take a rigid zone of less than the whole joint.
- **The arch.** The same load factor to 0.00 %, and the same antisymmetric shape.
- **Lateral-torsional buckling.** Beams find nothing to buckle: PDelta softens a beam by its axial
  force alone, and a beam bent by a load across it has none. This is the limit the docs' Linear
  Buckling page gives (*beams buckle in bending only*). Bricks find it: the narrow beam on forks
  within 0.4 % of the textbook M_cr, the narrow cantilever within 0.3 % of Vlasov's theory. Not
  of Timoshenko and Gere's 4.013 √(EI_z GJ) / L², which the bricks are 5 % above: a root held
  whole holds its warping, and on a cantilever under an end load, where the moment and the twist's
  change are greatest at the root, that adds 5 % even on a narrow rectangle (I_w = d³t³/144).
  Vlasov's theory with warping left free gives the 4.013 back, and is checked to.
- **Speed, and what solves.** The arch's bricks first took ten times as long as the other meshes
  of their size, 200 s against 20. The buckling deck numbered the equations with `numberer Plain`
  and solved them with SuperLU (`system SparseGeneral`), which OpenSees runs with no ordering of
  its own (`-permSpec` is not read): it factorised in the order the nodes came. So every system
  that can hand its matrix over, and every numberer, was run on the same decks (2026-11-08, M2
  Pro). The two solves, in s, and the most memory OpenSees took:

  | deck | equations | Plain, SuperLU | RCM, SuperLU | AMD, SuperLU | UmfPack | Mumps |
  | --- | --- | --- | --- | --- | --- | --- |
  | the arch, numbered along it | 20 223 | 183 (3.0 GB) | 0.5 | 0.5 | 0.5 | 0.3 |
  | the arch, numbered at random | 20 223 | 602 (4.2 GB) | 0.6 | 0.6 | 0.6 | 0.5 |
  | a column of 12 x 12 x 120 bricks | 60 840 | over 600 | 11.6 | 15.4 | 5.0 | 1.7 |
  | the same, numbered at random | 60 840 | | 17.2 | 22.9 | 7.7 | 2.2 |
  | a column of 16 x 16 x 160 bricks | 138 720 | | 82 (4.2 GB) | 74 (3.4 GB) | 20; numbered by AMD, out of memory | 4.8 (2.6 GB) |
  | the same, numbered at random | 138 720 | | 106 | 150 | | 5.8 |
  | a building frame, 10 x 10 bays, 10 storeys, 8 pieces a member | 150 480 | | 90 (6.4 GB) | 0.8 (0.4 GB) | 1.0 | 0.8 |

  The deck now numbers with AMD: RCM was as quick on the solids and a hundred times slower on the
  frame. It solves with Mumps where the OpenSees has it, with SuperLU where it has not, and with
  SuperLU again where Mumps fails (`Alpaca4d.Test/Buckling/fallbacks.sh` tries each way). Not with
  UmfPack, which ran out of memory on the big column once AMD had numbered it. Mumps keeps its
  matrix to itself, so the deck puts SuperLU's system in its place to print one, and Mumps back
  after. The matrices printed are the same whatever solved, to 1e-13, and the factors to 1e-9 -
  to 1.5e-7 on the arch of beams, the roundoff of its axial forces. The whole benchmark went from
  9 minutes to 2½.
- **Printing is now the slow part.** OpenSees's `FileStream` flushes after each piece of text,
  three to an entry of the matrix: printing K_mat and K_mat − K_geo took 14 s of the arch's 15 s in
  OpenSees, 48 s on the column of 60 840 equations, 113 s on that of 138 720. Fetched with
  `printA -sparse 1 -ret` and written from Tcl through a buffered channel, the two columns' took 8
  and 17 s, and to 17 digits instead of 16 - but Tcl holds every entry as an object while it
  writes, 3 GB more at 138 720 equations. Not done.
