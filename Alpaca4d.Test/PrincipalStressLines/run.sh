#!/usr/bin/env bash
# Principal Stress Lines, in a running Rhino 8 with its script server started (StartScriptServer) and
# Grasshopper loaded. StressLinesCheck.cs measures the lines against closed-form solutions, first
# through PrincipalField and StressLineTracer, then through the component on models OpenSees solved.
# Needs a Release build of Alpaca4d.Gh (any GrasshopperLibrariesPath), a C# compiler and OpenSees.
# RHINO_ID=rhinocode_remotepipe_<pid> picks the Rhino when more than one runs a script server.
set -e
cd "$(dirname "$0")"
GHDIR=${GHDIR:-../../Alpaca4d.Gh/bin/Release/net48}
GH=${GH:-$(ls -d ~/.nuget/packages/grasshopper/7.18.*/lib/net48 2>/dev/null | head -1)}
RHINO=${RHINO:-$(ls ~/.nuget/packages/rhinocommon/7.18.*/lib/net48/RhinoCommon.dll 2>/dev/null | head -1)}
RHINOCODE=${RHINOCODE:-"/Applications/Rhino 8.app/Contents/Resources/bin/rhinocode"}
OPENSEES=${OPENSEES:-$(command -v OpenSees || echo /Applications/OpenSees3.8.0/bin/OpenSees)}
# Ten node tetrahedra need an OpenSees built from master since 16 October 2026.
OPENSEES_SOLIDS=${OPENSEES_SOLIDS:-/Applications/OpenSeesLatest-Mumps/bin/OpenSees}
# The license the checks run under, put in place before they start; PARTS picks 2d, 3d or both.
# It is not committed; a worktree finds it in the main checkout.
LICENSE=${LICENSE:-$(dirname "$(git rev-parse --path-format=absolute --git-common-dir)")/Alpaca4d.Utils/data_august_28.bin}
PARTS=${PARTS:-2d,3d}
[ -f "$GHDIR/Alpaca4d.Gh.gha" ] || { echo "build Alpaca4d.Gh in Release first, or set GHDIR=..."; exit 1; }
[ -d "$GH" ] || { echo "set GH=/path/to/Grasshopper 7 lib/net48"; exit 1; }
[ -f "$RHINO" ] || { echo "set RHINO=/path/to/net48/RhinoCommon.dll"; exit 1; }
[ -x "$OPENSEES" ] || { echo "set OPENSEES=/path/to/OpenSees"; exit 1; }
[ -x "$OPENSEES_SOLIDS" ] || { echo "set OPENSEES_SOLIDS=/path/to/an OpenSees with TenNodeTetrahedronStd"; exit 1; }

# A folder of its own each run: Rhino keeps what it loaded mapped. The plugin reads its settings from
# beside Alpaca4d.Core.dll, so the folder gets a settings.json naming the solver.
work=$(mktemp -d)
cp "$GHDIR"/*.dll "$GHDIR"/*.gha "$work/"
printf '{ "OpenSeesPath": "%s" }\n' "$OPENSEES" > "$work/settings.json"
cp StressLinesCheck.cs SolidStressLinesCheck.cs "$work/"
# A definition saved before Principal Stress Lines had its Solids unit, to open.
cp ../../Tools/Examples/Benchmarks/StressLinesBenchmarks.gh "$work/"

( cd "$work"
  cp "$GH"/Grasshopper.dll "$GH"/GH_IO.dll "$RHINO" .
  NETSTD=${NETSTD:-$(ls /Library/Frameworks/Mono.framework/Versions/Current/lib/mono/4.5/Facades/netstandard.dll /opt/homebrew/lib/mono/4.5/Facades/netstandard.dll 2>/dev/null | head -1)}
  NETSTD_REF=""
  [ -n "$NETSTD" ] && NETSTD_REF="-r:$NETSTD"
  csc -nologo -langversion:latest -target:library -out:StressLinesCheck.dll \
      -r:Grasshopper.dll -r:GH_IO.dll -r:RhinoCommon.dll -r:Alpaca4d.Gh.gha -r:Alpaca4d.Core.dll \
      -r:System.Drawing.dll -r:System.Windows.Forms.dll $NETSTD_REF StressLinesCheck.cs SolidStressLinesCheck.cs
  rm Grasshopper.dll GH_IO.dll RhinoCommon.dll )
sed -e "s|__WORK__|$work|" -e "s|__OPENSEES__|$OPENSEES|" -e "s|__OPENSEES_SOLIDS__|$OPENSEES_SOLIDS|" \
    -e "s|__LICENSE__|$LICENSE|" -e "s|__PARTS__|$PARTS|" RhinoRun.cs > "$work/RhinoRun.cs"

# rhinocode returns before the script has run, so wait for what it writes.
"$RHINOCODE" ${RHINO_ID:+--rhino "$RHINO_ID"} script "$work/RhinoRun.cs"
for _ in $(seq 1 600); do [ -f "$work/result.txt" ] && break; sleep 1; done
[ -f "$work/result.txt" ] || { echo "no result: a compile error is in the newest ~/.rhinocode/logs/rhinocode_<Rhino's pid>"; exit 1; }
cat "$work/result.txt"
if grep -q "\[FAIL\]" "$work/result.txt"; then exit 1; fi
