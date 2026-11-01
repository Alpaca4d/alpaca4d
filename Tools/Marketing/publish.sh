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

trim() { $P trim "$OUT/$1.png" "$2" 40 ${3:-0}; }
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

lines2d() {
  local d=$S/shells
  mkdir -p "$d"
  trim lines2d/shells-a "$d/cantilever.png"
  trim lines2d/shells-b "$d/slab-bending.png"
  trim lines2d/shells-c "$d/plate-with-hole.png"
  trim lines2d/shells-all "$d/all-three.png"
  canvas lines2d/shells-canvas "$d/grasshopper-all-three.png"
  canvas lines2d/shells-canvas-plate "$d/grasshopper-plate-with-hole.png"
  pair "$d/plate-with-hole-in-grasshopper.png" "$d/grasshopper-plate-with-hole.png" "$d/plate-with-hole.png"
}

bridgelines() {
  local d=$S/solids
  mkdir -p "$d"
  trim bridgelines/bridge-stress-lines-front "$d/bridge-domain.png"
  canvas bridgelines/bridge-stress-lines-canvas "$d/grasshopper-bridge-domain.png"
  pair "$d/bridge-domain-in-grasshopper.png" "$d/grasshopper-bridge-domain.png" "$d/bridge-domain.png"
  # The stress lines through the solid domain, over the design the optimisation finds in it.
  if [ -f "$T/bridge/bridge-front-clean.png" ]; then
    $P stack "$T/bridge/stress-lines-and-design.png" "$d/bridge-domain.png" "$T/bridge/bridge-front-clean.png" 60
  fi
}

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
  local d=$O/femur s=$S/solids
  mkdir -p "$d" "$s"
  trim femur/femur "$d/femur-displacement.png"
  canvas femur/femur-canvas "$d/grasshopper-definition.png"
  pair "$d/femur-in-grasshopper.png" "$d/grasshopper-definition.png" "$d/femur-displacement.png"
  trim femur/femur-stress-lines "$s/femur.png"
  trim femur/femur-stress-lines-front "$s/femur-front.png"
  trim femur/femur-stress-lines-whole "$s/femur-whole.png"
  canvas femur/femur-stress-lines-canvas "$s/grasshopper-femur.png"
  pair "$s/femur-in-grasshopper.png" "$s/grasshopper-femur.png" "$s/femur.png"
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

ALL="cow femur dragon bricks mixed bridge tower benchmarks lines2d bridgelines"
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
