#!/usr/bin/env bash
# The gmsh meshers (Alpaca.Core/Meshing) under mono, with the gmsh on this machine: GMSH, or the
# first gmsh on PATH, or Homebrew's. Needs a built Alpaca4d.Core, a C# compiler and a net48 RhinoCommon.
set -e
cd "$(dirname "$0")"
COREDIR=${COREDIR:-../../Alpaca.Core/bin/Release/net48}
CORE="$COREDIR/Alpaca4d.Core.dll"
RHINO=${RHINO:-$(ls ~/.nuget/packages/rhinocommon/7.18.*/lib/net48/RhinoCommon.dll 2>/dev/null | head -1)}
GMSH=${GMSH:-$(command -v gmsh || echo /opt/homebrew/bin/gmsh)}
[ -f "$CORE" ]  || { echo "build Alpaca.Core first, or set COREDIR=..."; exit 1; }
[ -f "$RHINO" ] || { echo "set RHINO=/path/to/net48/RhinoCommon.dll"; exit 1; }
[ -x "$GMSH" ]  || { echo "no gmsh: brew install gmsh, or set GMSH=/path/to/gmsh"; exit 1; }

work=$(mktemp -d); trap 'rm -rf "$work"' EXIT
cp GmshChecks.cs "$work/"
cp "$COREDIR"/*.dll "$work/" 2>/dev/null || true
cp "$RHINO" "$work/"

( cd "$work"
  NETSTD=${NETSTD:-$(ls /Library/Frameworks/Mono.framework/Versions/Current/lib/mono/4.5/Facades/netstandard.dll 2>/dev/null | head -1)}
  NETSTD_REF=""
  [ -n "$NETSTD" ] && NETSTD_REF="-r:$NETSTD"
  mcs -target:exe -out:GmshChecks.exe -r:Alpaca4d.Core.dll -r:RhinoCommon.dll $NETSTD_REF GmshChecks.cs
  mono GmshChecks.exe "$GMSH" )
