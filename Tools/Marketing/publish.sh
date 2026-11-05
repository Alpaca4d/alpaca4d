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

dragon() {
  local d=$O/inigo-the-dragon m
  mkdir -p "$d"
  for m in 0 1 4 5; do trim dragon/dragon-mode-$m "$d/mode-$m.png"; done
  canvas dragon/dragon-canvas "$d/grasshopper-definition.png"
  pair "$d/inigo-in-grasshopper.png" "$d/grasshopper-definition.png" "$d/mode-4.png"
  # A mode swings through its rest and back: a loop, with no frames to skip or hold.
  $P anim "$OUT/dragon/dragon-mode-4-frames" "$d/mode-4-wings" 40 800 15 0
  $P anim "$OUT/dragon/dragon-mode-0-frames" "$d/mode-0-sway" 40 800 15 0
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

ALL="cow femur dragon bricks mixed bridge tower benchmarks lines2d bridgelines twospan plates kangaroo nerviflatslab nerviumbrella musmeci cornerslab roundumbrella roundslab candela groinvault palazzetto isler hexshell"
scenes=${*:-}
if [ -z "$scenes" ]; then
  for scene in $ALL; do [ -d "$OUT/$scene" ] && scenes="$scenes $scene"; done
fi
for scene in $scenes; do
  case " $ALL " in *" $scene "*) ;; *) echo "no scene $scene; the scenes are: $ALL"; exit 1 ;; esac
  echo "=== $scene"
  $scene
done
echo "Published into $M"
