#!/usr/bin/env bash
# Tools/Examples/Topology/TopologyNodes.py's two definitions - the space-truss node and the beam-column joint - each built
# by its build(doc) in a running Rhino 8 with its script server started (StartScriptServer), with this build of
# the plugin. All three optimisations run on each, and their first compliance has to be what ./run.sh bench node
# gives on its own deck - the proof that the definition models the members, the loads and the supports as meant.
# Then each design is analysed as group 8 does.
# Needs a Release build of Alpaca4d.Gh, an OpenSees (OPENSEES, or the one on PATH), a license (LICENSE, the
# user's by default; copied, never written), and RESULTS holding ./run.sh bench node truss 20 and joint 20's
# node-<case>-20_summary.csv. CASES="truss joint" (both, the default) picks the definitions. RHINO_PIPE=
# rhinocode_remotepipe_<pid> picks one Rhino.
set -e
cd "$(dirname "$0")"
here=$(pwd)
GHDIR=${GHDIR:-../../Alpaca4d.Gh/bin/Release/net48}
RHINOCODE=${RHINOCODE:-"/Applications/Rhino 8.app/Contents/Resources/bin/rhinocode"}
OPENSEES=${OPENSEES:-$(command -v OpenSees || true)}
LICENSE=${LICENSE:-"$HOME/Library/Application Support/Alpaca4d/license.bin"}
RESULTS=${RESULTS:-$here/results}
CASES=${CASES:-truss joint}
[ -f "$GHDIR/Alpaca4d.Gh.gha" ] || { echo "build Alpaca4d.Gh in Release first, or set GHDIR=..."; exit 1; }
[ -x "$OPENSEES" ] || { echo "put OpenSees on PATH, or set OPENSEES=/path/to/OpenSees"; exit 1; }
[ -f "$LICENSE" ] || { echo "the nodes have 8 000 bricks and need a license: set LICENSE=/path/to/license.bin"; exit 1; }
for c in truss joint; do
  [ -f "$RESULTS/node-$c-20_summary.csv" ] || { echo "no $RESULTS/node-$c-20_summary.csv: ./run.sh bench node $c 20 first"; exit 1; }
done

work=$(mktemp -d)
cp "$GHDIR"/*.dll "$GHDIR"/*.gha "$work/"
cp "$LICENSE" "$work/license.bin"
cp "$RESULTS"/node-truss-20_summary.csv "$RESULTS"/node-joint-20_summary.csv "$work/"
sed -e "s|__WORK__|$work|" -e "s|__EXAMPLES__|$(cd ../../Tools/Examples/Topology && pwd)|" -e "s|__OPENSEES__|$OPENSEES|" \
    -e "s|__CASES__|$CASES|" NodesRun.py > "$work/NodesRun.py"

if [ -n "$RHINO_PIPE" ]; then
  "$RHINOCODE" --rhino "$RHINO_PIPE" script "$work/NodesRun.py"
else
  "$RHINOCODE" script "$work/NodesRun.py"
fi
for _ in $(seq 1 3600); do [ -f "$work/result.txt" ] && break; sleep 1; done
[ -f "$work/result.txt" ] || { cat "$work/progress.txt" 2>/dev/null; echo "no result: an error is in the newest ~/.rhinocode/logs/rhinocode_<Rhino's pid>"; exit 1; }
cat "$work/result.txt"
if grep -q "\[FAIL\]" "$work/result.txt"; then exit 1; fi
