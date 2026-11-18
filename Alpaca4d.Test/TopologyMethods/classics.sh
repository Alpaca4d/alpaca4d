#!/usr/bin/env bash
# Tools/Examples/Topology/TopologyClassics' cantilever and L-bracket in a running Rhino 8, with this build of the plugin:
# ClassicsRun.py builds each definition, runs all its optimisations at once, and checks them against ./run.sh bench
# classic's runs of the same problems, which have to be in RESULTS first:
#
#     ./run.sh bench classic cantilever 40 0.4 1.5
#     ./run.sh bench classic lbracket 60 0.4 1.5 simp3 beso multi
#     ./classics.sh                     PICTURES=1 also draws the designs side by side and keeps the work folder
#
# Needs a Release build of Alpaca4d.Gh, an OpenSees (on PATH, or OPENSEES=...) and a license (LICENSE): the problems
# have more than 500 elements. RHINO_PIPE=rhinocode_remotepipe_<pid> picks one Rhino when several are running.
set -e
cd "$(dirname "$0")"
here=$(pwd)
GHDIR=${GHDIR:-../../Alpaca4d.Gh/bin/Release/net48}
RHINOCODE=${RHINOCODE:-"/Applications/Rhino 8.app/Contents/Resources/bin/rhinocode"}
OPENSEES=${OPENSEES:-$(command -v OpenSees || true)}
LICENSE=${LICENSE:-$(cd ../../Alpaca4d.Utils && pwd)/data_august_28.bin}
RESULTS=${RESULTS:-$here/results}
[ -f "$GHDIR/Alpaca4d.Gh.gha" ] || { echo "build Alpaca4d.Gh in Release first, or set GHDIR=..."; exit 1; }
[ -x "$OPENSEES" ] || { echo "put OpenSees on PATH, or set OPENSEES=/path/to/OpenSees"; exit 1; }
[ -f "$LICENSE" ] || { echo "the problems have more than 500 elements and need a license: set LICENSE=/path/to/license.bin"; exit 1; }
for f in classic-cantilever-40_summary.csv classic-lbracket-60_summary.csv; do
  [ -f "$RESULTS/$f" ] || { echo "no $RESULTS/$f: run the benches above first"; exit 1; }
done

work=$(mktemp -d)
cp "$GHDIR"/*.dll "$GHDIR"/*.gha "$work/"
cp "$LICENSE" "$work/license.bin"
cp "$RESULTS"/classic-cantilever-40_summary.csv "$RESULTS"/classic-lbracket-60_summary.csv "$work/"
sed -e "s|__WORK__|$work|" -e "s|__EXAMPLES__|$(cd ../../Tools/Examples/Topology && pwd)|" -e "s|__OPENSEES__|$OPENSEES|" \
    -e "s|__PICTURES__|${PICTURES:-0}|" ClassicsRun.py > "$work/ClassicsRun.py"

if [ -n "$RHINO_PIPE" ]; then
  "$RHINOCODE" --rhino "$RHINO_PIPE" script "$work/ClassicsRun.py"
else
  "$RHINOCODE" script "$work/ClassicsRun.py"
fi
for _ in $(seq 1 3600); do [ -f "$work/result.txt" ] && break; sleep 1; done
[ -f "$work/result.txt" ] || { cat "$work/progress.txt" 2>/dev/null; echo "no result: an error is in the newest ~/.rhinocode/logs/rhinocode_<Rhino's pid>"; exit 1; }
cat "$work/result.txt"
[ "${PICTURES:-0}" = 1 ] && echo "pictures and densities in $work"
if grep -q "\[FAIL\]" "$work/result.txt"; then exit 1; fi
