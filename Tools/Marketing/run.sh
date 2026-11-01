#!/usr/bin/env bash
# Makes the marketing pictures of the examples in Grasshopper, in a Rhino 8 of its own, and puts
# them in the website repository's marketing folder.
#
#     ./run.sh                      # every scene
#     ./run.sh bridge tower         # some of them
#     PUBLISH=0 ./run.sh cow        # the captures only, in out/; ./publish.sh cow later
#
# Scenes: cow femur dragon bricks mixed (solids), bridge tower benchmarks (topology optimisation),
# lines2d bridgelines (principal stress lines). See README.md.
#
# Starts Rhino 8 and quits it at the end. With a Rhino already open, it runs in that one instead, if
# it runs the script server: set RHINO_ID to its name in `rhinocode list`. The pictures show the
# Alpaca4d installed in that Rhino. LICENSE names a licence file to install first (the examples are
# past the free limit); by default Alpaca4d.Utils/data_august_28.bin, when it is there.
set -euo pipefail
cd "$(dirname "$0")"
here=$(pwd)
repo=$(cd ../.. && pwd)

ALL="cow femur dragon bricks mixed bridge tower benchmarks lines2d bridgelines"
SCENES=${*:-$ALL}
OUT=${OUT:-$here/out}
GH=${GH:-$(ls -d ~/.nuget/packages/grasshopper/7.18.*/lib/net48 2>/dev/null | head -1)}
RHINO=${RHINO:-$(ls ~/.nuget/packages/rhinocommon/7.18.*/lib/net48/RhinoCommon.dll 2>/dev/null | head -1)}
NETSTD=${NETSTD:-$(ls /Library/Frameworks/Mono.framework/Versions/Current/lib/mono/4.5/Facades/netstandard.dll \
                      /opt/homebrew/lib/mono/4.5/Facades/netstandard.dll 2>/dev/null | head -1)}
LICENSE=${LICENSE-$repo/Alpaca4d.Utils/data_august_28.bin}
[ -f "$LICENSE" ] || LICENSE=
RHINOCODE="/Applications/Rhino 8.app/Contents/Resources/bin/rhinocode"

for scene in $SCENES; do
  case " $ALL " in *" $scene "*) ;; *) echo "no scene $scene; the scenes are: $ALL"; exit 1 ;; esac
done
[ -d "$GH" ]        || { echo "set GH=/path/to/Grasshopper 7 lib/net48"; exit 1; }
[ -f "$RHINO" ]     || { echo "set RHINO=/path/to/net48/RhinoCommon.dll"; exit 1; }
[ -x "$RHINOCODE" ] || { echo "Rhino 8 is not in /Applications"; exit 1; }
command -v csc > /dev/null    || { echo "needs mono's csc: brew install mono"; exit 1; }
command -v ffmpeg > /dev/null || { echo "needs ffmpeg: brew install ffmpeg"; exit 1; }
case $here in *" "*) echo "rhinocode cannot run a script from a path with spaces: move the repository"; exit 1 ;; esac

# A new name for each build: a Rhino that loaded an earlier one would hand that back for the same name.
build=$(mktemp -d)
pid=
# The Rhino this script started, closed however it ends. The scenes close it themselves when they are
# done; it is killed only when they did not.
quit_rhino() {
  [ -n "$pid" ] && kill -0 "$pid" 2> /dev/null || return 0
  echo "Quitting Rhino"
  for _ in $(seq 1 "${1:-60}"); do kill -0 "$pid" 2> /dev/null || return 0; sleep 1; done
  kill "$pid" 2> /dev/null || true
}
trap 'quit_rhino; rm -rf "$build"' EXIT
name=Shots_$(date +%Y%m%d%H%M%S)
echo "Building $name"
csc -nologo -langversion:latest -nowarn:1701,1702 -target:library -out:"$build/$name.dll" \
    -r:"$GH/Grasshopper.dll" -r:"$GH/GH_IO.dll" -r:"$RHINO" -r:System.Drawing.dll -r:System.Windows.Forms.dll \
    ${NETSTD:+-r:"$NETSTD"} Shots.cs | grep -v "warning CS" || true
[ -f "$build/$name.dll" ] || { echo "Shots.cs did not compile"; exit 1; }

if [ -z "${RHINO_ID:-}" ]; then
  # A second Rhino started beside an open one runs no scripts.
  if pgrep -x Rhinoceros > /dev/null; then
    echo "A Rhino is open. Close it, or run StartScriptServer in it and set RHINO_ID to its name in"
    echo "\`\"$RHINOCODE\" list\`."
    exit 1
  fi
  # Now and then Rhino starts without running StartScriptServer: it is closed and started again.
  for attempt in 1 2 3; do
    echo "Starting Rhino 8"
    open -na "Rhino 8" --args -nosplash -runscript="_StartScriptServer"
    ready=
    for _ in $(seq 1 120); do
      pid=$(pgrep -x Rhinoceros | head -1 || true)
      if [ -n "$pid" ] && "$RHINOCODE" list 2>/dev/null | grep -q "rhinocode_remotepipe_$pid"; then ready=1; break; fi
      sleep 1
    done
    [ -n "$pid" ] || { echo "Rhino did not start"; exit 1; }
    RHINO_ID=rhinocode_remotepipe_$pid
    [ -n "$ready" ] && break
    echo "Rhino started without its script server"
    quit_rhino 0
    [ "$attempt" = 3 ] && exit 1
  done
  # Its script server answers before Rhino has finished starting.
  sleep 5
fi

mkdir -p "$OUT"
rm -f "$OUT/log.txt" "$OUT/done.txt"
for scene in $SCENES; do rm -rf "${OUT:?}/$scene"; done
sed -e "s|__DLL__|$build/$name.dll|" -e "s|__SCENES__|$(echo $SCENES | tr ' ' ',')|" -e "s|__OUT__|$OUT|g" \
    -e "s|__REPO__|$repo|" -e "s|__LICENSE__|$LICENSE|" -e "s|__QUIT__|${pid:+1}|" Runner.cs > "$build/run.cs"
echo "Running $SCENES in $RHINO_ID"
# A script sent while Rhino is still busy starting is dropped ("Post timeout"); the scenes start
# their log at once, so one not there after 20 s is sent again.
for _ in 1 2 3 4 5; do
  "$RHINOCODE" --rhino "$RHINO_ID" script "$build/run.cs" 2>&1 | grep -v -E "^\s+at |Post timeout" || true
  for _ in $(seq 1 20); do [ -f "$OUT/log.txt" ] && break 2; sleep 1; done
  echo "Rhino did not take the script; sending it again"
done
[ -f "$OUT/log.txt" ] || { echo "Rhino takes no scripts: is its script server running?"; exit 1; }

# The log, as the scenes write it, until they are done.
shown=0
for _ in $(seq 1 ${WAIT:-7200}); do
  if [ -f "$OUT/log.txt" ]; then
    lines=$(wc -l < "$OUT/log.txt" | tr -d " ")
    [ "$lines" -gt "$shown" ] && { tail -n +$((shown + 1)) "$OUT/log.txt" | head -n $((lines - shown)); shown=$lines; }
  fi
  [ -f "$OUT/done.txt" ] && break
  [ -z "$pid" ] || kill -0 "$pid" 2> /dev/null || { echo "Rhino quit"; break; }
  sleep 1
done
[ -f "$OUT/log.txt" ] && tail -n +$((shown + 1)) "$OUT/log.txt"
done=$(cat "$OUT/done.txt" 2> /dev/null || echo "failed: all, no done.txt")
echo "$done"
quit_rhino

case $done in failed:*) failed=" ${done#failed: } " ;; *) failed=" " ;; esac
[ "${PUBLISH:-1}" = 1 ] || exit 0
publish=
for scene in $SCENES; do case $failed in *" $scene "*|*" all "*) ;; *) publish="$publish $scene" ;; esac; done
[ -z "$publish" ] || ./publish.sh $publish
[ "$failed" = " " ]
