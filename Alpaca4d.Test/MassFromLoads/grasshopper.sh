#!/usr/bin/env bash
# Mass from Loads through the Grasshopper components, in a running Rhino 8 with its script
# server started (StartScriptServer) and Grasshopper loaded. GrasshopperChecks.cs builds a beam and a
# slab out of the real components, with and without Mass from Loads, and checks Assemble Model's Mass,
# the deck's mass lines and Natural Vibration's frequencies against closed form.
# Needs a Release build of Alpaca4d.Gh (any GrasshopperLibrariesPath), a C# compiler and OpenSees.
# LICENSE=... names the licence file installed first (the test licence by default); PIPE=rhinocode_remotepipe_<pid>
# picks the Rhino to run in when more than one is open.
set -e
cd "$(dirname "$0")"
GHDIR=${GHDIR:-../../Alpaca4d.Gh/bin/Release/net48}
GH=${GH:-$(ls -d ~/.nuget/packages/grasshopper/7.18.*/lib/net48 2>/dev/null | head -1)}
RHINO=${RHINO:-$(ls ~/.nuget/packages/rhinocommon/7.18.*/lib/net48/RhinoCommon.dll 2>/dev/null | head -1)}
RHINOCODE=${RHINOCODE:-"/Applications/Rhino 8.app/Contents/Resources/bin/rhinocode"}
LICENSE=${LICENSE:-$(cd ../../Alpaca4d.Utils && pwd)/data_august_28.bin}
OPENSEES=${OPENSEES:-$(command -v OpenSees || echo /Applications/OpenSees3.8.0/bin/OpenSees)}
[ -f "$GHDIR/Alpaca4d.Gh.gha" ] || { echo "build Alpaca4d.Gh in Release first, or set GHDIR=..."; exit 1; }
[ -d "$GH" ] || { echo "set GH=/path/to/Grasshopper 7 lib/net48"; exit 1; }
[ -f "$RHINO" ] || { echo "set RHINO=/path/to/net48/RhinoCommon.dll"; exit 1; }
[ -x "$OPENSEES" ] || { echo "set OPENSEES=/path/to/OpenSees"; exit 1; }

# A folder of its own each run: Rhino keeps what it loaded mapped. The plugin reads its settings from
# beside Alpaca4d.Core.dll, so the folder gets a settings.json naming the solver.
work=$(mktemp -d)
cp "$GHDIR"/*.dll "$GHDIR"/*.gha "$work/"
cp "$GHDIR"/*.json "$GHDIR"/*.bin "$work/" 2>/dev/null || true
printf '{ "OpenSeesPath": "%s" }\n' "$OPENSEES" > "$work/settings.json"
cp GrasshopperChecks.cs "$work/"

( cd "$work"
  cp "$GH"/Grasshopper.dll "$GH"/GH_IO.dll "$RHINO" .
  NETSTD=${NETSTD:-$(ls /Library/Frameworks/Mono.framework/Versions/Current/lib/mono/4.5/Facades/netstandard.dll /opt/homebrew/lib/mono/4.5/Facades/netstandard.dll 2>/dev/null | head -1)}
  NETSTD_REF=""
  [ -n "$NETSTD" ] && NETSTD_REF="-r:$NETSTD"
  csc -nologo -langversion:latest -target:library -out:GrasshopperCheck.dll \
      -r:Grasshopper.dll -r:GH_IO.dll -r:RhinoCommon.dll -r:Alpaca4d.Gh.gha -r:Alpaca4d.Core.dll \
      -r:System.Drawing.dll -r:System.Windows.Forms.dll $NETSTD_REF GrasshopperChecks.cs
  rm Grasshopper.dll GH_IO.dll RhinoCommon.dll )
sed -e "s|__WORK__|$work|" -e "s|__LICENSE__|$LICENSE|" GrasshopperRun.cs > "$work/GrasshopperRun.cs"

# rhinocode returns before the script has run, so wait for what it writes.
PIPE=${PIPE:+--rhino $PIPE}
"$RHINOCODE" $PIPE script "$work/GrasshopperRun.cs"
for _ in $(seq 1 600); do [ -f "$work/result.txt" ] && break; sleep 1; done
[ -f "$work/result.txt" ] || { echo "no result: a compile error is in the newest ~/.rhinocode/logs/rhinocode_<Rhino's pid>"; exit 1; }
cat "$work/result.txt"
if grep -q "\[FAIL\]" "$work/result.txt"; then exit 1; fi
