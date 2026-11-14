#!/usr/bin/env bash
# Iso-surface checks: that View Results cuts each face and beam into bands that cover it exactly
# and lie inside their band, and that the band edges it hands Legend are where its colours change.
#
# It reads the built .gha rather than the source, because ColourScale and IsoBands are internal
# to it. Needs a built Alpaca4d.Gh, a C# compiler and a net48 RhinoCommon.
set -e
cd "$(dirname "$0")"
GHA=${GHA:-../../Alpaca4d.Gh/bin/Release/net48/Alpaca4d.Gh.gha}
RHINO=${RHINO:-$(ls ~/.nuget/packages/rhinocommon/7.18.*/lib/net48/RhinoCommon.dll 2>/dev/null | head -1)}
[ -f "$GHA" ]   || { echo "build Alpaca4d.Gh first, or set GHA=..."; exit 1; }
[ -f "$RHINO" ] || { echo "set RHINO=/path/to/net48/RhinoCommon.dll"; exit 1; }

CSC=$(command -v csc || command -v mcs)
work=$(mktemp -d); trap 'rm -rf "$work"' EXIT
cp IsoSurfaces.cs "$work/"
# The whole build output, because the assembly resolves Grasshopper and Alpaca4d.Core at load.
cp "$(dirname "$GHA")"/*.dll "$(dirname "$GHA")"/*.gha "$work/" 2>/dev/null || true
cp "$RHINO" "$work/"

( cd "$work"
  "$CSC" -nologo -target:exe -out:IsoSurfaces.exe -r:RhinoCommon.dll IsoSurfaces.cs
  mono IsoSurfaces.exe Alpaca4d.Gh.gha )
