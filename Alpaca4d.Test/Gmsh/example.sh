#!/usr/bin/env bash
# The two gmsh examples - Tools/Examples/Meshing/PlateWithHoles.py and Tools/Examples/Solids/Femur.py - built by
# their own build(doc) in a running Rhino 8 with its script server started (StartScriptServer), with this build of
# the plugin, and solved: the plate's largest stress against Heywood's formula, the femur's tetrahedra, analysis
# and modes. Needs a Release build of Alpaca4d.Gh, gmsh where Homebrew puts it, an OpenSees that knows
# TenNodeTetrahedronStd (OPENSEES, by default /Applications/OpenSeesLatest-Mumps/bin/OpenSees) and a license
# (LICENSE, copied, never written). RHINO_PIPE=rhinocode_remotepipe_<pid> picks one Rhino.
set -e
cd "$(dirname "$0")"
GHDIR=${GHDIR:-../../Alpaca4d.Gh/bin/Release/net48}
RHINOCODE=${RHINOCODE:-"/Applications/Rhino 8.app/Contents/Resources/bin/rhinocode"}
OPENSEES=${OPENSEES:-/Applications/OpenSeesLatest-Mumps/bin/OpenSees}
LICENSE=${LICENSE:-$(cd ../../Alpaca4d.Utils && pwd)/data_august_28.bin}
[ -f "$GHDIR/Alpaca4d.Gh.gha" ] || { echo "build Alpaca4d.Gh in Release first, or set GHDIR=..."; exit 1; }
[ -x "$OPENSEES" ] || { echo "set OPENSEES=/path/to/an OpenSees that knows TenNodeTetrahedronStd"; exit 1; }
[ -f "$LICENSE" ] || { echo "the examples have more than 50 elements and need a license: set LICENSE=/path/to/license.bin"; exit 1; }

work=$(mktemp -d)
cp "$GHDIR"/*.dll "$GHDIR"/*.gha "$work/"
cp "$LICENSE" "$work/license.bin"
sed -e "s|__WORK__|$work|" -e "s|__EXAMPLES__|$(cd ../../Tools/Examples && pwd)|" -e "s|__OPENSEES__|$OPENSEES|" -e "s|__PICTURES__|${PICTURES:-0}|" \
    ExampleRun.py > "$work/ExampleRun.py"

if [ -n "$RHINO_PIPE" ]; then
  "$RHINOCODE" --rhino "$RHINO_PIPE" script "$work/ExampleRun.py"
else
  "$RHINOCODE" script "$work/ExampleRun.py"
fi
for _ in $(seq 1 1800); do [ -f "$work/result.txt" ] && break; sleep 1; done
[ -f "$work/result.txt" ] || { cat "$work/progress.txt" 2>/dev/null; echo "no result: an error is in the newest ~/.rhinocode/logs/rhinocode_<Rhino's pid>"; exit 1; }
cat "$work/result.txt"
echo "(work folder: $work)"
if grep -q "\[FAIL\]" "$work/result.txt"; then exit 1; fi
