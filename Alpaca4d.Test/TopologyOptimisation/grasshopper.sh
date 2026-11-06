#!/usr/bin/env bash
# Topology Optimisation through the Grasshopper components, in a running Rhino 8 with its script server
# started (StartScriptServer). GrasshopperChecks.cs builds definitions out of the real components,
# solves them, and checks the run against the hand-written deck of TopologyOptimisation.cs.
# Needs a Release build of Alpaca4d.Gh (any GrasshopperLibrariesPath), a C# compiler, an OpenSees (the
# one on PATH, or OPENSEES=...), a license (LICENSE, the user's by default; copied, never written) and,
# for the tetrahedra, an OpenSees that knows TenNodeTetrahedronStd (OPENSEES_TET10; empty skips them).
# RHINO_PIPE=rhinocode_remotepipe_<pid> picks one Rhino when several are running.
set -e
cd "$(dirname "$0")"
GHDIR=${GHDIR:-../../Alpaca4d.Gh/bin/Release/net48}
GH=${GH:-$(ls -d ~/.nuget/packages/grasshopper/7.18.*/lib/net48 2>/dev/null | head -1)}
RHINO=${RHINO:-$(ls ~/.nuget/packages/rhinocommon/7.18.*/lib/net48/RhinoCommon.dll 2>/dev/null | head -1)}
RHINOCODE=${RHINOCODE:-"/Applications/Rhino 8.app/Contents/Resources/bin/rhinocode"}
OPENSEES=${OPENSEES:-$(command -v OpenSees || true)}
TET10=${OPENSEES_TET10-/Applications/OpenSeesLatest-Mumps/bin/OpenSees}
LICENSE=${LICENSE:-"$HOME/Library/Application Support/Alpaca4d/license.bin"}
[ -f "$GHDIR/Alpaca4d.Gh.gha" ] || { echo "build Alpaca4d.Gh in Release first, or set GHDIR=..."; exit 1; }
[ -d "$GH" ] || { echo "set GH=/path/to/Grasshopper 7 lib/net48"; exit 1; }
[ -f "$RHINO" ] || { echo "set RHINO=/path/to/net48/RhinoCommon.dll"; exit 1; }
[ -x "$OPENSEES" ] || { echo "put OpenSees on PATH, or set OPENSEES=/path/to/OpenSees"; exit 1; }
[ -f "$LICENSE" ] || { echo "Topology Optimisation needs a license: set LICENSE=/path/to/license.bin"; exit 1; }
[ -z "$TET10" ] || [ -x "$TET10" ] || { echo "OPENSEES_TET10=$TET10 is not there; set it empty to skip the tetrahedra"; exit 1; }

# A folder of its own each run: Rhino keeps what it loaded mapped.
work=$(mktemp -d)
cp "$GHDIR"/*.dll "$GHDIR"/*.gha "$work/"
cp GrasshopperChecks.cs "$work/"
cp "$LICENSE" "$work/license.bin"

( cd "$work"
  cp "$GH"/Grasshopper.dll "$GH"/GH_IO.dll "$RHINO" .
  NETSTD=${NETSTD:-$(ls /Library/Frameworks/Mono.framework/Versions/Current/lib/mono/4.5/Facades/netstandard.dll /opt/homebrew/lib/mono/4.5/Facades/netstandard.dll 2>/dev/null | head -1)}
  NETSTD_REF=""
  [ -n "$NETSTD" ] && NETSTD_REF="-r:$NETSTD"
  csc -nologo -langversion:latest -target:library -out:GrasshopperCheck.dll \
      -r:Grasshopper.dll -r:GH_IO.dll -r:RhinoCommon.dll -r:Alpaca4d.Gh.gha -r:Alpaca4d.Core.dll \
      -r:System.Drawing.dll -r:System.Windows.Forms.dll $NETSTD_REF GrasshopperChecks.cs
  rm Grasshopper.dll GH_IO.dll RhinoCommon.dll )
sed -e "s|__WORK__|$work|" -e "s|__OPENSEES__|$OPENSEES|" -e "s|__TET10__|$TET10|" -e "s|__LICENSE__|$work/license.bin|" \
    GrasshopperRun.cs > "$work/GrasshopperRun.cs"

# rhinocode returns before the script has run, so wait for what it writes.
if [ -n "$RHINO_PIPE" ]; then
  "$RHINOCODE" --rhino "$RHINO_PIPE" script "$work/GrasshopperRun.cs"
else
  "$RHINOCODE" script "$work/GrasshopperRun.cs"
fi
for _ in $(seq 1 900); do [ -f "$work/result.txt" ] && break; sleep 1; done
[ -f "$work/result.txt" ] || { echo "no result: a compile error is in the newest ~/.rhinocode/logs/rhinocode_<Rhino's pid>"; exit 1; }
cat "$work/result.txt"
if grep -q "\[FAIL\]" "$work/result.txt"; then exit 1; fi
