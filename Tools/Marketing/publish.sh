#!/usr/bin/env bash
# Makes the captures run.sh left in out/<scene> into the marketing pictures: trimmed to what is drawn,
# the canvas beside the result, and the topology optimisation runs as GIF and MP4.
#
#     ./publish.sh                  # every scene with captures in out/
#     ./publish.sh bridge tower
#
# Writes into ../../../alpaca4d.github.io/marketing, the website repository next to this one, or into
# MARKETING=/path/to/marketing, over what an earlier round put there.
set -euo pipefail
cd "$(dirname "$0")"
OUT=${OUT:-out}
M=${MARKETING:-../../../alpaca4d.github.io/marketing}
[ -d "$M" ] || { echo "set MARKETING=/path/to/alpaca4d.github.io/marketing"; exit 1; }
export PYTHONDONTWRITEBYTECODE=1
P="python3 post.py"

trim() { $P trim "$OUT/$1.png" "$2" 40 ${3:-0} ${4:-0}; }
canvas() { cp "$OUT/$1.png" "$2"; echo "canvas $1 -> $2"; }
pair() { $P pair "$1" "$2" "$3" 1300; }

# Frames from the first that differs from frame 0 - a run starts with nothing above the threshold -
# renumbered from 0, keeping $2 frames of the empty start; prints the folder they are in.
frames() {
  local src=$OUT/$1 dst first i=0 skip=0
  dst=$(mktemp -d)
  first=$(md5 -q "$src/000.png")
  for f in "$src"/*.png; do
    if [ "$(md5 -q "$f")" = "$first" ]; then
      skip=$((skip + 1)); [ $skip -gt "${2:-0}" ] && continue
    fi
    cp "$f" "$dst/$(printf %03d $i).png"; i=$((i + 1))
  done
  echo "$dst"
}

# A run as base.gif and base.mp4, from its frames.
run_anim() { local d; d=$(frames "$1" 2); $P anim "$d" "$2" 40 900 10; rm -rf "$d"; }

T=$M/topology-optimisation
S=$M/principal-stress-lines
O=$M/solids

# ---------------------------------------------------------------- topology optimisation

bridge() {
  local d=$T/bridge
  mkdir -p "$d"
  trim bridge/bridge "$d/bridge.png"
  trim bridge/bridge-clean "$d/bridge-clean.png"
  trim bridge/bridge-front "$d/bridge-front.png"
  trim bridge/bridge-front-clean "$d/bridge-front-clean.png"
  trim bridge/bridge-below "$d/bridge-from-below.png"
  trim bridge/bridge-analysed "$d/bridge-analysed.png"
  trim bridge/bridge-analysed-clean "$d/bridge-analysed-clean.png"
  canvas bridge/bridge-canvas "$d/grasshopper-definition.png"
  canvas bridge/bridge-analysed-canvas "$d/grasshopper-definition-analysed.png"
  pair "$d/bridge-in-grasshopper.png" "$d/grasshopper-definition.png" "$d/bridge-clean.png"
  pair "$d/bridge-analysed-in-grasshopper.png" "$d/grasshopper-definition-analysed.png" "$d/bridge-analysed.png"
  run_anim bridge/bridge-frames "$d/bridge-iterations"
}

tower() {
  local d=$T/tower
  mkdir -p "$d"
  trim tower/tower "$d/tower.png"
  trim tower/tower-clean "$d/tower-clean.png"
  trim tower/tower-below "$d/tower-from-below.png"
  trim tower/tower-analysed "$d/tower-analysed.png"
  trim tower/tower-analysed-clean "$d/tower-analysed-clean.png"
  canvas tower/tower-canvas "$d/grasshopper-definition.png"
  canvas tower/tower-analysed-canvas "$d/grasshopper-definition-analysed.png"
  pair "$d/tower-in-grasshopper.png" "$d/grasshopper-definition.png" "$d/tower-clean.png"
  pair "$d/tower-analysed-in-grasshopper.png" "$d/grasshopper-definition-analysed.png" "$d/tower-analysed.png"
  run_anim tower/tower-frames "$d/tower-iterations"
}

benchmarks() {
  local d=$T/benchmarks b
  mkdir -p "$d"
  canvas benchmarks/benchmarks-canvas "$d/grasshopper-definition.png"
  canvas benchmarks/benchmarks-analysed-canvas "$d/grasshopper-definition-analysed.png"
  for b in cantilever simply-supported; do
    trim benchmarks/$b "$d/$b.png"
    trim benchmarks/$b-clean "$d/$b-clean.png"
    trim benchmarks/$b-analysed "$d/$b-analysed.png"
    trim benchmarks/$b-analysed-clean "$d/$b-analysed-clean.png"
    run_anim benchmarks/$b-frames "$d/$b-iterations"
  done
}

# ---------------------------------------------------------------- principal stress lines

# Every case's spin and walk-through, from out/<scene>/<name>-spin-frames and -walk-through-frames:
# both loops of a kind, the spin round and the walk there. 800 px keeps a busy GIF under 10 MB; the
# MP4 is made at 1600.
spin_walk() {
  $P anim "$OUT/$1/$2-spin-frames" "$3/$2-spin" 20 800 15 0
  $P anim "$OUT/$1/$2-walk-through-frames" "$3/$2-walk-through" 0 800 12 1
}

# The three shells against their closed forms, in benchmarks; Kirsch's plate also in a folder of its own.
lines2d() {
  local b=$S/benchmarks d=$S/plate-with-hole
  mkdir -p "$b" "$d"
  trim lines2d/shells-a "$b/shells-cantilever.png"
  trim lines2d/shells-b "$b/shells-slab-bending.png"
  trim lines2d/shells-all "$b/shells-all-three.png"
  canvas lines2d/shells-canvas "$b/grasshopper-shells.png"
  trim lines2d/shells-c "$d/plate-with-hole.png"
  canvas lines2d/shells-canvas-plate "$d/grasshopper-definition.png"
  pair "$d/plate-with-hole-in-grasshopper.png" "$d/grasshopper-definition.png" "$d/plate-with-hole.png"
}

bridgelines() {
  local d=$S/bridge-domain
  mkdir -p "$d"
  trim bridgelines/bridge-stress-lines-front "$d/bridge-domain.png"
  canvas bridgelines/bridge-stress-lines-canvas "$d/grasshopper-definition.png"
  pair "$d/bridge-domain-in-grasshopper.png" "$d/grasshopper-definition.png" "$d/bridge-domain.png"
  # The stress lines through the solid domain, over the design the optimisation finds in it.
  if [ -f "$T/bridge/bridge-front-clean.png" ]; then
    $P stack "$T/bridge/stress-lines-and-design.png" "$d/bridge-domain.png" "$T/bridge/bridge-front-clean.png" 60
  fi
}

# The four plates, a folder each: alone, with and without its supports and loads, beside its part of
# the definition; the loads walking, the lines growing, and each spinning and walked through, as GIF
# and MP4. The four together, and the whole definition, in overview.
plates() {
  local plate key n
  for plate in cantilever-wall:a deep-beam:b slab:c cantilever-shell:d; do
    key=${plate#*:}; plate=${plate%:*}
    mkdir -p "$S/$plate"
    canvas plates/plate-$key-canvas "$S/$plate/grasshopper-definition.png"
  done
  for n in cantilever-wall cantilever-wall-clean; do trim plates/$n "$S/cantilever-wall/$n.png" 0 12; done
  for n in deep-beam deep-beam-clean; do trim plates/$n "$S/deep-beam/$n.png" 0 12; done
  for n in slab slab-top slab-clean slab-point-load slab-point-load-top; do trim plates/$n "$S/slab/$n.png" 0 12; done
  for n in cantilever-shell cantilever-shell-tip cantilever-shell-clean; do trim plates/$n "$S/cantilever-shell/$n.png" 0 12; done
  for plate in cantilever-wall deep-beam slab cantilever-shell; do
    pair "$S/$plate/$plate-in-grasshopper.png" "$S/$plate/grasshopper-definition.png" "$S/$plate/$plate.png"
  done
  mkdir -p "$S/overview"
  trim plates/all-four "$S/overview/all-four.png" 0 12
  canvas plates/plates-canvas "$S/overview/grasshopper-definition.png"
  # A load walking there and back is a loop: no frames to hold.
  $P anim "$OUT/plates/deep-beam-walking-frames" "$S/deep-beam/deep-beam-load-walking" 40 900 8 0
  $P anim "$OUT/plates/slab-walking-frames" "$S/slab/slab-load-walking" 40 900 8 0
  $P anim "$OUT/plates/cantilever-wall-growing-frames" "$S/cantilever-wall/cantilever-wall-growing" 40 900 12 2
  for plate in cantilever-wall deep-beam slab cantilever-shell; do
    spin_walk plates $plate "$S/$plate"
  done
}

# The shell Kangaroo found: four views, the definition, the form being found, the shell spinning,
# walked through and its lines growing.
kangaroo() {
  local d=$S/form-found-shell n
  mkdir -p "$d"
  for n in shell shell-low shell-above shell-top shell-clean; do trim kangaroo/$n "$d/$n.png" 0 12; done
  canvas kangaroo/shell-canvas "$d/grasshopper-definition.png"
  pair "$d/shell-in-grasshopper.png" "$d/grasshopper-definition.png" "$d/shell.png"
  $P anim "$OUT/kangaroo/shell-form-finding-frames" "$d/shell-form-finding" 40 900 10 3
  spin_walk kangaroo shell "$d"
  $P anim "$OUT/kangaroo/shell-growing-frames" "$d/shell-growing" 40 900 12 2
}

# The Nervi flat slab, on columns: from above, from below with its lines as ribs, the definition, the
# lines growing and a walk under it.
nerviflatslab() {
  local d=$S/nervi-flat-slab n
  mkdir -p "$d"
  for n in nervi-flat-slab nervi-flat-slab-top nervi-flat-slab-top-clean nervi-flat-slab-below nervi-flat-slab-below-concrete \
           nervi-flat-slab-below-corner nervi-flat-slab-below-corner-concrete; do
    trim nerviflatslab/$n "$d/$n.png" 0 12
  done
  canvas nerviflatslab/nervi-flat-slab-canvas "$d/grasshopper-definition.png"
  pair "$d/nervi-flat-slab-in-grasshopper.png" "$d/grasshopper-definition.png" "$d/nervi-flat-slab-top.png"
  $P anim "$OUT/nerviflatslab/nervi-flat-slab-growing-frames" "$d/nervi-flat-slab-growing" 40 900 12 2
  spin_walk nerviflatslab nervi-flat-slab "$d"
}

# The Nervi umbrella: one from above, nine as a hall from below, the definition, the lines growing,
# the hall spinning and walked through.
nerviumbrella() {
  local d=$S/nervi-umbrella n
  mkdir -p "$d"
  for n in nervi-umbrella nervi-umbrella-top nervi-umbrella-top-clean nervi-umbrella-below nervi-umbrella-below-concrete \
           nervi-umbrella-hall nervi-umbrella-hall-concrete; do
    trim nerviumbrella/$n "$d/$n.png" 0 12
  done
  canvas nerviumbrella/nervi-umbrella-canvas "$d/grasshopper-definition.png"
  pair "$d/nervi-umbrella-in-grasshopper.png" "$d/grasshopper-definition.png" "$d/nervi-umbrella-top.png"
  $P anim "$OUT/nerviumbrella/nervi-umbrella-growing-frames" "$d/nervi-umbrella-growing" 40 900 12 2
  spin_walk nerviumbrella nervi-umbrella "$d"
}

# The Musmeci bridge: from three sides, from the valley and from under it, the definition, the form
# being found, the bridge spinning and walked through underneath.
musmeci() {
  local d=$S/musmeci-bridge n
  mkdir -p "$d"
  for n in musmeci-bridge musmeci-bridge-elevation musmeci-bridge-clean musmeci-bridge-from-the-valley \
           musmeci-bridge-below musmeci-bridge-top; do
    trim musmeci/$n "$d/$n.png" 0 12
  done
  canvas musmeci/musmeci-bridge-canvas "$d/grasshopper-definition.png"
  pair "$d/musmeci-bridge-in-grasshopper.png" "$d/grasshopper-definition.png" "$d/musmeci-bridge.png"
  $P anim "$OUT/musmeci/musmeci-bridge-form-finding-frames" "$d/musmeci-bridge-form-finding" 40 900 10 3
  spin_walk musmeci musmeci-bridge "$d"
}

# A study of StressLinesStudies.py, its scene $1 into marketing/principal-stress-lines/$2: every
# picture its scene took, its definition beside its lines, its lines growing, the form being found
# for a Kangaroo one, a spin and a walk-through.
study() {
  local scene=$1 name=$2 d=$S/$2 f n
  mkdir -p "$d"
  for f in "$OUT/$scene"/*.png; do
    n=$(basename "$f" .png)
    case $n in
      *-canvas) canvas $scene/$n "$d/grasshopper-definition.png" ;;
      *) trim $scene/$n "$d/$n.png" 0 12 ;;
    esac
  done
  pair "$d/$name-in-grasshopper.png" "$d/grasshopper-definition.png" "$d/$name-top.png"
  [ -d "$OUT/$scene/$name-form-finding-frames" ] && $P anim "$OUT/$scene/$name-form-finding-frames" "$d/$name-form-finding" 40 900 10 3
  $P anim "$OUT/$scene/$name-growing-frames" "$d/$name-growing" 40 900 12 2
  spin_walk $scene $name "$d"
}
cornerslab() { study cornerslab corner-slab; }
roundumbrella() { study roundumbrella round-umbrella; }
roundslab() { study roundslab round-slab; }
candela() { study candela candela-umbrella; }
groinvault() { study groinvault groin-vault; }
palazzetto() { study palazzetto palazzetto; }
isler() { study isler isler-shell; }
hexshell() { study hexshell hexagonal-shell; }

# ---------------------------------------------------------------- solids

cow() {
  local d=$O/spot-the-cow
  mkdir -p "$d"
  trim cow/cow "$d/spot-the-cow.png"
  trim cow/cow-2 "$d/spot-the-cow-side.png"
  canvas cow/cow-canvas "$d/grasshopper-definition.png"
  pair "$d/spot-the-cow-in-grasshopper.png" "$d/grasshopper-definition.png" "$d/spot-the-cow.png"
}

femur() {
  local d=$O/femur s=$S/femur
  mkdir -p "$d" "$s"
  trim femur/femur "$d/femur-displacement.png"
  canvas femur/femur-canvas "$d/grasshopper-definition.png"
  pair "$d/femur-in-grasshopper.png" "$d/grasshopper-definition.png" "$d/femur-displacement.png"
  trim femur/femur-stress-lines "$s/femur.png"
  trim femur/femur-stress-lines-front "$s/femur-front.png"
  trim femur/femur-stress-lines-whole "$s/femur-whole.png"
  canvas femur/femur-stress-lines-canvas "$s/grasshopper-definition.png"
  pair "$s/femur-in-grasshopper.png" "$s/grasshopper-definition.png" "$s/femur.png"
}

bricks() {
  local d=$O/brick-block
  mkdir -p "$d"
  trim bricks/brick-block "$d/brick-block.png"
  canvas bricks/brick-block-canvas "$d/grasshopper-definition.png"
  pair "$d/brick-block-in-grasshopper.png" "$d/grasshopper-definition.png" "$d/brick-block.png"
}

mixed() {
  local d=$O/mixed-model
  mkdir -p "$d"
  trim mixed/mixed-model "$d/mixed-model.png"
  canvas mixed/mixed-model-canvas "$d/grasshopper-definition.png"
  pair "$d/mixed-model-in-grasshopper.png" "$d/grasshopper-definition.png" "$d/mixed-model.png"
}

# ---------------------------------------------------------------- section optimiser

# The load arrow is a thin line: trim grows what is drawn for its box, or it would be cut off.
twospan() {
  local d=$M/section-optimiser/two-span-beam f
  mkdir -p "$d"
  for f in sections sections-front deflection deflection-front moment shear uniform-front; do
    trim twospan/two-span-beam-$f "$d/two-span-beam-$f.png" 0 12
  done
  $P stack "$d/two-span-beam-moment-and-shear.png" "$d/two-span-beam-moment.png" "$d/two-span-beam-shear.png"
  canvas twospan/two-span-beam-canvas "$d/grasshopper-definition.png"
  pair "$d/two-span-beam-in-grasshopper.png" "$d/grasshopper-definition.png" "$d/two-span-beam-sections.png"
  # Deflecting and back to rest: a loop, with no frames to skip or hold.
  $P anim "$OUT/twospan/two-span-beam-deflecting-frames" "$d/two-span-beam-deflecting" 40 900 15 0
}

curvedbeam() {
  local d=$M/section-optimiser/curved-beam f
  mkdir -p "$d"
  for f in sections sections-front; do
    trim curvedbeam/curved-beam-$f "$d/curved-beam-$f.png" 0 12
  done
  canvas curvedbeam/curved-beam-canvas "$d/grasshopper-definition.png"
  pair "$d/curved-beam-in-grasshopper.png" "$d/grasshopper-definition.png" "$d/curved-beam-sections.png"
  # On two supports, the load, then the roller, walking along the beam and back, the beam sized again at every
  # node, its caption under each frame: a case of its own each, for a post of its own.
  local c
  for c in moving-load moving-support; do
    mkdir -p "$d-$c"
    $P animcharts "$OUT/curvedbeam/curved-beam-$c-frames" "$OUT/curvedbeam/curved-beam-$c-captions" "$d-$c/curved-beam-$c" 40 900 8 0 3 0.82
  done
}

beamgrid() {
  local d=$M/section-optimiser/beam-grid f
  mkdir -p "$d"
  for f in sections displacement; do
    trim beamgrid/beam-grid-$f "$d/beam-grid-$f.png" 0 12
  done
  canvas beamgrid/beam-grid-canvas "$d/grasshopper-definition.png"
  pair "$d/beam-grid-in-grasshopper.png" "$d/grasshopper-definition.png" "$d/beam-grid-sections.png"
  # The supports walking in from the corners and back, the grid sized again at each, a second a stop.
  mkdir -p "$d-moving-supports"
  $P animcharts "$OUT/beamgrid/beam-grid-moving-supports-frames" "$OUT/beamgrid/beam-grid-moving-supports-captions" \
    "$d-moving-supports/beam-grid-moving-supports" 40 900 10 0 0 0.82
}

# The docs' how-to guides: a folder per picture, and a copy beside its guide in the docs.
howto() {
  local f d docs=$M/../doc/how-to
  for f in supports releases wind buckling; do
    d=$M/how-to/$f
    mkdir -p "$d"
    trim howto/$f "$d/$f.png" 0 12
  done
  cp "$M/how-to/supports/supports.png" "$docs/supports-and-releases/supports.png"
  cp "$M/how-to/releases/releases.png" "$docs/supports-and-releases/releases.png"
  cp "$M/how-to/wind/wind.png" "$docs/loading/wind.png"
  cp "$M/how-to/buckling/buckling.png" "$docs/analysis/buckling.png"
  # Model View's page: every kind of load on one frame, and its own weight alone.
  d=$M/model-view/loads
  mkdir -p "$d"
  trim howto/loads "$d/loads.png" 0 12
  trim howto/own-weight "$d/own-weight.png" 0 12
  cp "$d/loads.png" "$d/own-weight.png" "$M/../doc/basics/visualisation/model-view/"
}

# The six conventional structures: each one's loads, deformed shape and GIF, its canvas beside its
# deformed shape, and the tower's first mode - and the example's pictures in examples/<slug>/.
# One of the structures, captured by scene: its loads, its deformed shape, the GIF of it deforming,
# its first mode if it has one, its canvas; and the example's pictures.
structure() {
  local scene=$1 slug=$2 d e
  d=$M/structures/$slug
  e=$M/../examples/$slug
  mkdir -p "$d" "$e"
  trim $scene/$slug-loads "$d/$slug-loads.png" 0 12
  trim $scene/$slug-deformed "$d/$slug-deformed.png" 0 12
  $P anim "$OUT/$scene/$slug-deforming-frames" "$d/$slug-deforming" 40 900 15 0
  if [ -d "$OUT/$scene/$slug-mode-frames" ]; then
    trim $scene/$slug-mode "$d/$slug-mode.png" 0 12
    $P anim "$OUT/$scene/$slug-mode-frames" "$d/$slug-mode" 40 900 15 0
  fi
  canvas $scene/$slug-canvas "$d/grasshopper-definition.png"
  # The example's page: the deformed shape first, the loads second, and the GIFs.
  cp "$d/$slug-deformed.png" "$e/$slug.png"
  cp "$d/$slug-loads.png" "$e/$slug-2.png"
  cp "$d/$slug-deforming.gif" "$e/$slug-deforming.gif"
  [ -f "$d/$slug-mode.gif" ] && cp "$d/$slug-mode.gif" "$e/$slug-mode.gif"
  return 0
}

structures() {
  local slug
  for slug in truss-roof-hall steel-tower roof-canopy vertical-truss-facade barrel-vault horizontal-truss-facade compressed-shell; do
    structure structures $slug
  done
}

# The five earthquakes: each one's still at its worst, the MP4 of it shaking in real time - ten frames a
# second, a frame every 0.1 s - and a lighter GIF, its canvas, and the example's pictures.
earthquakes() {
  local slug d e
  for slug in girder-bridge-earthquake cable-stayed-bridge-earthquake arch-bridge-earthquake building-earthquake earthquake-city; do
    d=$M/earthquakes/$slug
    e=$M/../examples/$slug
    mkdir -p "$d" "$e"
    trim earthquakes/$slug "$d/$slug.png" 0 12
    # The still again, its charts under it, and the frames, each with its charts.
    $P stack "$d/$slug-2.png" "$d/$slug.png" "$OUT/earthquakes/$slug-chart.png" 0
    $P animcharts "$OUT/earthquakes/$slug-frames" "$OUT/earthquakes/$slug-charts" "$d/$slug-shaking" 40 900 10 0
    canvas earthquakes/$slug-canvas "$d/grasshopper-definition.png"
    cp "$d/$slug.png" "$d/$slug-2.png" "$d/$slug-shaking.mp4" "$e/"
  done
}

# The printed cylinder: the still of it buckling, the ages at 30 layers, its printing as GIF and MP4 with
# the load factor's chart under each frame, the buckling mode swinging, its canvas, and the example's pictures.
printing() {
  local d=$M/3d-printing/printed-cylinder e=$M/../examples/printed-cylinder
  mkdir -p "$d" "$e"
  trim printing/printed-cylinder-buckled "$d/printed-cylinder-buckled.png" 0 12
  trim printing/printed-cylinder-ages "$d/printed-cylinder-ages.png" 0 12
  $P stack "$d/printed-cylinder-buckled-chart.png" "$d/printed-cylinder-buckled.png" "$OUT/printing/printed-cylinder-chart.png" 0
  # Eight frames a layer: the MP4 has them all, 24 a second; the GIF, for posts, every third at 8, 600 px
  # wide, so that it stays light with every brick's edges drawn.
  $P animcharts "$OUT/printing/printed-cylinder-printing-frames" "$OUT/printing/printed-cylinder-printing-charts" "$d/printed-cylinder-printing" 40 900 24 2
  local half; half=$(mktemp -d)
  mkdir -p "$half/frames" "$half/charts"
  local i=0 f
  for f in "$OUT"/printing/printed-cylinder-printing-frames/*.png; do
    if [ $(( $(basename "$f" .png | sed 's/^0*//;s/^$/0/') % 3 )) = 2 ]; then
      cp "$f" "$half/frames/$(printf %03d $i).png"
      cp "$OUT/printing/printed-cylinder-printing-charts/$(basename "$f")" "$half/charts/$(printf %03d $i).png"
      i=$((i + 1))
    fi
  done
  $P animcharts "$half/frames" "$half/charts" "$half/printing" 40 600 8 2
  cp "$half/printing.gif" "$d/printed-cylinder-printing.gif"
  rm -rf "$half"
  $P anim "$OUT/printing/printed-cylinder-buckling-frames" "$d/printed-cylinder-buckling" 40 600 15 0
  canvas printing/printed-cylinder-canvas "$d/grasshopper-definition.png"
  cp "$d/printed-cylinder-buckled-chart.png" "$e/printed-cylinder.png"
  cp "$d/printed-cylinder-ages.png" "$e/printed-cylinder-2.png"
  $P pagemp4 "$d/printed-cylinder-printing.mp4" "$e/printed-cylinder-printing.mp4"
  # The buckling mode as a GIF on the page, lighter than the one for posts.
  $P pagegif "$OUT/printing/printed-cylinder-buckling-frames" "$e/printed-cylinder-buckling.gif"
}

# A shape of Tools/Examples/PrintedStructure.py printed along its own sections, as printing() does for
# the cylinder: the whole print, a frame for every 2.5 s of printing or more; the ages at its end; the
# settling and the buckling mode where the load factor reaches 1. printedshape scene prefix case example
# stem: the pictures go to marketing/3d-printing/<case>, and to examples/3d-printed-structure/ as <stem>*.
printedshape() {
  local scene=$1 p=$2 d=$M/3d-printing/$3 e=$M/../examples/3d-printed-structure stem=$4 s=$OUT/$1
  mkdir -p "$d" "$e"
  trim "$scene/$p-buckled" "$d/$p-buckled.png" 0 12
  trim "$scene/$p-ages" "$d/$p-ages.png" 0 12
  trim "$scene/$p-settling" "$d/$p-settling.png" 0 12
  $P stack "$d/$p-buckled-chart.png" "$d/$p-buckled.png" "$s/$p-chart.png" 0
  $P animcharts "$s/$p-printing-frames" "$s/$p-printing-charts" "$d/$p-printing" 40 900 12 2
  # The GIF, for posts: every third frame at 6 a second, 600 px wide, light with every brick's edges drawn.
  local third; third=$(mktemp -d)
  mkdir -p "$third/frames" "$third/charts"
  local i=0 f
  for f in "$s/$p-printing-frames"/*.png; do
    if [ $(( $(basename "$f" .png | sed 's/^0*//;s/^$/0/') % 3 )) = 2 ]; then
      cp "$f" "$third/frames/$(printf %03d $i).png"
      cp "$s/$p-printing-charts/$(basename "$f")" "$third/charts/$(printf %03d $i).png"
      i=$((i + 1))
    fi
  done
  $P animcharts "$third/frames" "$third/charts" "$third/printing" 40 600 6 2
  cp "$third/printing.gif" "$d/$p-printing.gif"
  rm -rf "$third"
  $P anim "$s/$p-buckling-frames" "$d/$p-buckling" 40 600 15 0
  canvas "$scene/$p-canvas" "$d/grasshopper-definition.png"
  cp "$d/$p-buckled-chart.png" "$e/$stem.png"
  $P pagemp4 "$d/$p-printing.mp4" "$e/$stem-printing.mp4"
  $P pagegif "$s/$p-buckling-frames" "$e/$stem-buckling.gif"
}

# The branching shell of the earlier voxel example: the example's own pictures, under its old name.
printedstructure() {
  printedshape printedstructure printed-structure printed-structure 3d-printed-structure
  local e=$M/../examples/3d-printed-structure
  cp "$M/3d-printing/printed-structure/printed-structure-ages.png" "$e/3d-printed-structure-2.png"
  cp "$M/3d-printing/printed-structure/printed-structure-settling.png" "$e/3d-printed-structure-3.png"
}
# The other shapes, each a section of the example's page.
printedtwisted() { printedshape printedtwisted twisted-column twisted-column twisted-column; }
printedwall() { printedshape printedwall wavy-wall wavy-wall wavy-wall; }
printeddome() { printedshape printeddome printed-dome dome dome; }
# The leaning column printed along its axis, the nozzle tilting as it bends over; and printed in flat layers,
# its Path unplugged, after which the two go side by side at the same moment - for the page as well.
printedleaning() { printedshape printedleaning leaning-column leaning-column leaning-column; }
printedleaningflat() {
  local d=$M/3d-printing/leaning-column e=$M/../examples/3d-printed-structure a=$OUT/printedleaning b=$OUT/printedleaningflat
  printedshape printedleaningflat leaning-column-flat leaning-column leaning-column-flat
  # The canvas is the column's own, its Path plugged in; on the page, of the flat print only its chart.
  canvas printedleaning/leaning-column-canvas "$d/grasshopper-definition.png"
  rm -f "$e/leaning-column-flat-printing.mp4" "$e/leaning-column-flat-buckling.gif"
  local ea eb
  ea=$(awk '$1 != "critical" { t = $1 } END { print t }' "$a/leaning-column-factors.txt")
  eb=$(awk '$1 != "critical" { t = $1 } END { print t }' "$b/leaning-column-flat-factors.txt")
  $P sidebyside "$a/leaning-column-printing-frames" "$a/leaning-column-printing-charts" "$ea" "$a/leaning-column-label.png" \
    "$b/leaning-column-flat-printing-frames" "$b/leaning-column-flat-printing-charts" "$eb" "$b/leaning-column-flat-label.png" \
    "$d/leaning-column-both-printing" 1200 12 2
  $P pagemp4 "$d/leaning-column-both-printing.mp4" "$e/leaning-column-both-printing.mp4" 1200
}

# The buckling modes alone, after the printingbuckling scene made their frames again: the GIFs and MP4s for
# posts, and the lighter GIFs on the pages. The flat leaning column has none on the page.
printingbuckling() {
  local x=$M/../examples scene p c stem
  $P anim "$OUT/printing/printed-cylinder-buckling-frames" "$M/3d-printing/printed-cylinder/printed-cylinder-buckling" 40 600 15 0
  $P pagegif "$OUT/printing/printed-cylinder-buckling-frames" "$x/printed-cylinder/printed-cylinder-buckling.gif"
  # Scene, prefix, case and page stem, four words each; not a heredoc read line by line, whose lines ffmpeg
  # would read as its own input.
  set -- printedstructure printed-structure printed-structure 3d-printed-structure \
         printedtwisted twisted-column twisted-column twisted-column \
         printedwall wavy-wall wavy-wall wavy-wall \
         printeddome printed-dome dome dome \
         printedleaning leaning-column leaning-column leaning-column \
         printedleaningflat leaning-column-flat leaning-column -
  while [ $# -ge 4 ]; do
    scene=$1 p=$2 c=$3 stem=$4
    shift 4
    $P anim "$OUT/$scene/$p-buckling-frames" "$M/3d-printing/$c/$p-buckling" 40 600 15 0
    [ "$stem" = - ] || $P pagegif "$OUT/$scene/$p-buckling-frames" "$x/3d-printed-structure/$stem-buckling.gif"
  done
}

# A footbridge's first three modes, each a still and a loop of it swinging, its canvas, and the
# picture of the docs' how-to guide on natural frequencies.
footbridge() {
  local d=$M/structures/footbridge m n
  mkdir -p "$d"
  for m in 0 1 2; do
    n=$((m + 1))
    trim footbridge/footbridge-mode-$m "$d/footbridge-mode-$n.png" 0 12
    $P anim "$OUT/footbridge/footbridge-mode-$m-frames" "$d/footbridge-mode-$n" 40 900 15 0
  done
  canvas footbridge/footbridge-canvas "$d/grasshopper-definition.png"
  cp "$d/footbridge-mode-1.png" "$M/../doc/how-to/analysis/natural-frequencies.png"
}

# A tank's first hundred modes: the close look at the grid, the whole grid receding, the grid from
# above with each mode's number and frequency, the close look swinging, its canvas, and the natural
# vibration example's pictures.
tank() {
  local d=$M/structures/tank-modes e=$M/../examples/natural-vibration
  mkdir -p "$d" "$e"
  trim tank/tank-modes "$d/tank-modes.png" 0 12
  trim tank/tank-modes-edges "$d/tank-modes-edges.png" 0 12
  trim tank/tank-modes-field "$d/tank-modes-field.png" 0 12
  trim tank/tank-modes-grid "$d/tank-modes-grid.png" 0 12
  $P anim "$OUT/tank/tank-modes-frames" "$d/tank-modes-swinging" 40 900 15 0
  canvas tank/tank-canvas "$d/grasshopper-definition.png"
  # The page's pictures 2400 px wide, in 256 colours: the modes' smooth colours make the full ones 3 to 7 MB.
  local f
  for f in "tank-modes:natural-vibration" "tank-modes-grid:natural-vibration-2"; do
    ffmpeg -y -loglevel error -i "$d/${f%%:*}.png" \
      -vf "scale=2400:-2:flags=lanczos,split[a][b];[a]palettegen=max_colors=256:stats_mode=full[p];[b][p]paletteuse=dither=sierra2_4a" \
      "$e/${f##*:}.png"
  done
  cp "$d/tank-modes-swinging.mp4" "$e/natural-vibration-swinging.mp4"
}

# The sticks: the still with the spectrum behind them, again with its charts under it, the MP4 of
# them shaking in real time with theirs, the canvas, and the example's pictures.
spectrum() {
  local d=$M/earthquakes/response-spectrum e=$M/../examples/response-spectrum
  mkdir -p "$d" "$e"
  trim spectrum/response-spectrum "$d/response-spectrum.png" 0 12
  $P stack "$d/response-spectrum-2.png" "$d/response-spectrum.png" "$OUT/spectrum/response-spectrum-chart.png" 0
  $P animcharts "$OUT/spectrum/response-spectrum-frames" "$OUT/spectrum/response-spectrum-charts" "$d/response-spectrum-shaking" 40 900 10 0
  canvas spectrum/spectrum-canvas "$d/grasshopper-definition.png"
  cp "$d/response-spectrum.png" "$d/response-spectrum-2.png" "$d/response-spectrum-shaking.mp4" "$e/"
}

# The compressed shell, from the structures' scene of its own.
shell() { structure shell compressed-shell; }

# The Rebellious Slave: its vertical stress from in front and from its left, its first two modes as
# stills and loops, its canvas, and the pictures of its section of the Ten Node Tetrahedron page.
slave() {
  local d=$O/rebellious-slave t=$M/../doc/basics/elements/ten-node-tetrahedron/rebellious-slave m n
  mkdir -p "$d" "$t"
  trim slave/slave-stress "$d/rebellious-slave.png" 0 12
  trim slave/slave-stress-left "$d/rebellious-slave-left.png" 0 12
  for m in 0 1; do
    n=$((m + 1))
    trim slave/slave-mode-$m "$d/mode-$n.png" 0 12
    $P anim "$OUT/slave/slave-mode-$m-frames" "$d/mode-$n" 40 800 15 0
    cp "$d/mode-$n.gif" "$t/rebellious-slave-mode-$n.gif"
  done
  canvas slave/slave-canvas "$d/grasshopper-definition.png"
  pair "$d/rebellious-slave-in-grasshopper.png" "$d/grasshopper-definition.png" "$d/rebellious-slave.png"
  cp "$d/rebellious-slave.png" "$t/rebellious-slave.png"
  cp "$d/rebellious-slave-left.png" "$t/rebellious-slave-2.png"
}

# Moment curvature: the section beside its diagram, from above, its canvas, and the example's picture.
# The buildings that are not straight and the towers of a hundred storeys and more, shaken by El Centro:
# as the earthquakes, but a tall one's charts stand beside it rather than under it. $1 is the scene they
# were captured in, the rest their slugs.
quakes() {
  local scene=$1 slug d e
  shift
  for slug in "$@"; do
    d=$M/earthquakes/$slug
    e=$M/../examples/$slug
    mkdir -p "$d" "$e"
    trim $scene/$slug "$d/$slug.png" 0 12
    case $slug in
      leaning-*|crescent-*)
        $P stack "$d/$slug-2.png" "$d/$slug.png" "$OUT/$scene/$slug-chart.png" 0
        $P animcharts "$OUT/$scene/$slug-frames" "$OUT/$scene/$slug-charts" "$d/$slug-shaking" 40 900 10 0 ;;
      *)
        $P pair "$d/$slug-2.png" "$d/$slug.png" "$OUT/$scene/$slug-chart.png" 1400
        $P besidecharts "$OUT/$scene/$slug-frames" "$OUT/$scene/$slug-charts" "$d/$slug-shaking" 40 900 10 0 ;;
    esac
    canvas $scene/$slug-canvas "$d/grasshopper-definition.png"
    cp "$d/$slug.png" "$d/$slug-2.png" "$d/$slug-shaking.mp4" "$e/"
  done
}
QUAKE_BUILDINGS="twisting-tower-earthquake leaning-towers-earthquake curving-tower-earthquake crescent-earthquake"
QUAKE_TOWERS="bundled-tube-earthquake braced-tube-earthquake buttressed-core-earthquake outrigger-tower-earthquake diagrid-tower-earthquake"
quakebuildings() { quakes quakebuildings $QUAKE_BUILDINGS; }
quaketowers() { quakes quaketowers $QUAKE_TOWERS; }
twisting() { quakes twisting twisting-tower-earthquake; }
leaning() { quakes leaning leaning-towers-earthquake; }
curving() { quakes curving curving-tower-earthquake; }
crescent() { quakes crescent crescent-earthquake; }
bundled() { quakes bundled bundled-tube-earthquake; }
braced() { quakes braced braced-tube-earthquake; }
buttressed() { quakes buttressed buttressed-core-earthquake; }
outrigger() { quakes outrigger outrigger-tower-earthquake; }
diagrid() { quakes diagrid diagrid-tower-earthquake; }

momentcurvature() {
  local d=$M/domain-moment-curvature/moment-curvature e=$M/../examples/moment-curvature
  mkdir -p "$d" "$e"
  trim momentcurvature/moment-curvature "$d/moment-curvature.png" 0 12
  canvas momentcurvature/moment-curvature-canvas "$d/grasshopper-definition.png"
  pair "$d/moment-curvature-in-grasshopper.png" "$d/grasshopper-definition.png" "$d/moment-curvature.png"
  cp "$d/moment-curvature.png" "$e/moment-curvature.png"
}

ALL="cow femur slave bricks mixed bridge tower benchmarks lines2d bridgelines twospan curvedbeam beamgrid howto structures earthquakes plates kangaroo nerviflatslab nerviumbrella musmeci cornerslab roundumbrella roundslab candela groinvault palazzetto isler hexshell printing printedstructure printedtwisted printedwall printeddome printedleaning printedleaningflat footbridge spectrum shell momentcurvature tank quakebuildings quaketowers twisting leaning curving crescent bundled braced buttressed outrigger diagrid"
scenes=${*:-}
if [ -z "$scenes" ]; then
  for scene in $ALL; do [ -d "$OUT/$scene" ] && scenes="$scenes $scene"; done
fi
# Run only when named: the buckling modes alone of the 3D printing scenes.
EXTRA="printingbuckling"
for scene in $scenes; do
  case " $ALL $EXTRA " in *" $scene "*) ;; *) echo "no scene $scene; the scenes are: $ALL"; exit 1 ;; esac
  echo "=== $scene"
  $scene
done
echo "Published into $M"
