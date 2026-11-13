#!/usr/bin/env bash
# Tools/Examples/Design/FibreSections.py, its three cases built by build(doc) in a running Rhino 8 with its script
# server started (StartScriptServer), with this build of the plugin: each is solved and every Moment Curvature and
# Interaction Domain in it reported.
# Needs a Release build of Alpaca4d.Gh, an OpenSees (OPENSEES, or the one on PATH) and a license (LICENSE,
# the user's by default; copied, never written). RHINO_PIPE=rhinocode_remotepipe_<pid> picks one Rhino.
set -e
cd "$(dirname "$0")"
GHDIR=${GHDIR:-../../Alpaca4d.Gh/bin/Release/net48}
RHINOCODE=${RHINOCODE:-"/Applications/Rhino 8.app/Contents/Resources/bin/rhinocode"}
OPENSEES=${OPENSEES:-$(command -v OpenSees || true)}
LICENSE=${LICENSE:-$(cd ../../Alpaca4d.Utils && pwd)/data_august_28.bin}
[ -f "$GHDIR/Alpaca4d.Gh.gha" ] || { echo "build Alpaca4d.Gh in Release first, or set GHDIR=..."; exit 1; }
[ -x "$OPENSEES" ] || { echo "put OpenSees on PATH, or set OPENSEES=/path/to/OpenSees"; exit 1; }
[ -f "$LICENSE" ] || { echo "the sections have more than 50 fibres and need a license: set LICENSE=/path/to/license.bin"; exit 1; }

work=$(mktemp -d)
cp "$GHDIR"/*.dll "$GHDIR"/*.gha "$work/"
cp "$LICENSE" "$work/license.bin"
sed -e "s|__WORK__|$work|" -e "s|__EXAMPLES__|$(cd ../../Tools/Examples/Design && pwd)|" -e "s|__OPENSEES__|$OPENSEES|" \
    ExampleRun.py > "$work/ExampleRun.py"

if [ -n "$RHINO_PIPE" ]; then
  "$RHINOCODE" --rhino "$RHINO_PIPE" script "$work/ExampleRun.py"
else
  "$RHINOCODE" script "$work/ExampleRun.py"
fi
for _ in $(seq 1 900); do [ -f "$work/result.txt" ] && break; sleep 1; done
[ -f "$work/result.txt" ] || { echo "no result: an error is in the newest ~/.rhinocode/logs/rhinocode_<Rhino's pid>"; exit 1; }
cat "$work/result.txt"
if grep -q "\[FAIL\]" "$work/result.txt"; then exit 1; fi
