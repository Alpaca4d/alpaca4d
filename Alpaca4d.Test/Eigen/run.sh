#!/usr/bin/env bash
# The eigenvalues Natural Vibration reads back from OpenSees, and the size of the modes it reads.
# Needs a built Alpaca4d.Core, a C# compiler (mcs) and a net48 RhinoCommon. OpenSees is optional:
# with it a cantilever is solved by each of the three eigen solvers and read back, without it only
# the reading is checked.
set -e
cd "$(dirname "$0")"
COREDIR=${COREDIR:-../../Alpaca.Core/bin/Release/net48}
CORE="$COREDIR/Alpaca4d.Core.dll"
RHINO=${RHINO:-$(ls ~/.nuget/packages/rhinocommon/7.18.*/lib/net48/RhinoCommon.dll 2>/dev/null | head -1)}
[ -f "$CORE" ]  || { echo "build Alpaca.Core first, or set COREDIR=..."; exit 1; }
[ -f "$RHINO" ] || { echo "set RHINO=/path/to/net48/RhinoCommon.dll"; exit 1; }
OPENSEES=${OPENSEES:-$(command -v OpenSees || ls /Applications/OpenSees*/bin/OpenSees 2>/dev/null | tail -1)}

work=$(mktemp -d); trap 'rm -rf "$work"' EXIT
cp EigenCheck.cs "$work/"
# PureHDF and its dependencies come from the build output; the modes are read with them.
cp "$COREDIR"/*.dll "$RHINO" "$work/"
( cd "$work"
  # PureHDF is a netstandard2.0 assembly, so mcs needs the facade to resolve its types.
  NETSTD=${NETSTD:-$(ls /Library/Frameworks/Mono.framework/Versions/Current/lib/mono/4.5/Facades/netstandard.dll 2>/dev/null | head -1)}
  NETSTD_REF=""
  [ -n "$NETSTD" ] && NETSTD_REF="-r:$NETSTD"
  mcs -target:exe -out:EigenCheck.exe -r:Alpaca4d.Core.dll -r:RhinoCommon.dll -r:PureHDF.dll $NETSTD_REF EigenCheck.cs
  mono EigenCheck.exe "$OPENSEES" )
