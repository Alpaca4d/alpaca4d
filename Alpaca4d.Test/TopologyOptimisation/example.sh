#!/usr/bin/env bash
# Tools/Examples/Topology/TopologyBenchmarks.py's definition, built by its own build(doc) in a running Rhino 8 with
# its script server started (StartScriptServer), with this build of the plugin; both benchmarks run, each by SIMP,
# BESO and Multi-Material, for ITERATIONS (30), and SIMP's are checked against the test bench's histories at that
# iteration, when RESULTS holds them (./run.sh bench cantilever and simply-supported write them).
# PICTURES=1 runs them to their ends instead (ITERATIONS=0), checks SIMP's last compliance against the definition's
# panels, and draws each problem's three designs, from the front and in 3D, into the work folder it prints.
# Needs a Release build of Alpaca4d.Gh, an OpenSees (OPENSEES, or the one on PATH) and a license (LICENSE,
# the user's by default; copied, never written). RHINO_PIPE=rhinocode_remotepipe_<pid> picks one Rhino.
set -e
cd "$(dirname "$0")"
here=$(pwd)
GHDIR=${GHDIR:-../../Alpaca4d.Gh/bin/Release/net48}
RHINOCODE=${RHINOCODE:-"/Applications/Rhino 8.app/Contents/Resources/bin/rhinocode"}
OPENSEES=${OPENSEES:-$(command -v OpenSees || true)}
LICENSE=${LICENSE:-$(cd ../../Alpaca4d.Utils && pwd)/data_august_28.bin}
RESULTS=${RESULTS:-$here/results}
if [ "${PICTURES:-0}" = 1 ]; then ITERATIONS=${ITERATIONS:-0}; else ITERATIONS=${ITERATIONS:-30}; fi
[ -f "$GHDIR/Alpaca4d.Gh.gha" ] || { echo "build Alpaca4d.Gh in Release first, or set GHDIR=..."; exit 1; }
[ -x "$OPENSEES" ] || { echo "put OpenSees on PATH, or set OPENSEES=/path/to/OpenSees"; exit 1; }
[ -f "$LICENSE" ] || { echo "Topology Optimisation needs a license: set LICENSE=/path/to/license.bin"; exit 1; }

# A problem's compliance at ITERATIONS from its history, or 0 when there is none.
at() { [ -f "$RESULTS/$1_history.csv" ] && awk -F, -v n="$ITERATIONS" '$1 == n { print $2 }' "$RESULTS/$1_history.csv" | grep . || echo 0; }

work=$(mktemp -d)
cp "$GHDIR"/*.dll "$GHDIR"/*.gha "$work/"
cp "$LICENSE" "$work/license.bin"
sed -e "s|__WORK__|$work|" -e "s|__EXAMPLES__|$(cd ../../Tools/Examples/Topology && pwd)|" -e "s|__OPENSEES__|$OPENSEES|" \
    -e "s|__ITERATIONS__|$ITERATIONS|" -e "s|__PICTURES__|${PICTURES:-0}|" -e "s|__CANTILEVER__|$(at cantilever)|" -e "s|__SIMPLE__|$(at simply-supported)|" \
    ExampleRun.py > "$work/ExampleRun.py"

if [ -n "$RHINO_PIPE" ]; then
  "$RHINOCODE" --rhino "$RHINO_PIPE" script "$work/ExampleRun.py"
else
  "$RHINOCODE" script "$work/ExampleRun.py"
fi
for _ in $(seq 1 3000); do [ -f "$work/result.txt" ] && break; sleep 1; done
[ -f "$work/result.txt" ] || { cat "$work/progress.txt" 2>/dev/null; echo "no result: an error is in the newest ~/.rhinocode/logs/rhinocode_<Rhino's pid>"; exit 1; }
cat "$work/result.txt"
[ "${PICTURES:-0}" = 1 ] && echo "pictures in $work"
if grep -q "\[FAIL\]" "$work/result.txt"; then exit 1; fi
