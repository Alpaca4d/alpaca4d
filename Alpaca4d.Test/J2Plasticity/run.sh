#!/usr/bin/env bash
# J2Plasticity checks: what Alpaca4d writes and reads back for a metal that yields, and what
# OpenSees does with it on a brick, on ASD shells and on ten node tetrahedra.
# Needs a built Alpaca4d.Core, a C# compiler and a net48 RhinoCommon. The brick and shell decks run
# on OPENSEES (default OpenSees on PATH: export PATH=/Applications/OpenSees3.8.0/bin:$PATH), the
# tetrahedron decks on OPENSEES_TET10 (default /Applications/OpenSeesLatest-Mumps/bin/OpenSees).
set -e
cd "$(dirname "$0")"
COREDIR=${COREDIR:-../../Alpaca.Core/bin/Release/net48}
CORE="$COREDIR/Alpaca4d.Core.dll"
RHINO=${RHINO:-$(ls ~/.nuget/packages/rhinocommon/7.18.*/lib/net48/RhinoCommon.dll 2>/dev/null | head -1)}
[ -f "$CORE" ]  || { echo "build Alpaca.Core first, or set COREDIR=..."; exit 1; }
[ -f "$RHINO" ] || { echo "set RHINO=/path/to/net48/RhinoCommon.dll"; exit 1; }

work=$(mktemp -d); trap 'rm -rf "$work"' EXIT
cp J2Plasticity.cs ../../Tools/Examples/PlasticityPlateHole.txt "$work/"
cp "$COREDIR"/*.dll "$work/" 2>/dev/null || true
cp "$RHINO" "$work/"

( cd "$work"
  NETSTD=${NETSTD:-$(ls /Library/Frameworks/Mono.framework/Versions/Current/lib/mono/4.5/Facades/netstandard.dll 2>/dev/null | head -1)}
  NETSTD_REF=""
  [ -n "$NETSTD" ] && NETSTD_REF="-r:$NETSTD"
  mcs -target:exe -out:J2Plasticity.exe -r:Alpaca4d.Core.dll -r:RhinoCommon.dll -r:System.Drawing.dll $NETSTD_REF J2Plasticity.cs
  mono J2Plasticity.exe . )
