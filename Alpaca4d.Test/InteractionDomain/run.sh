#!/usr/bin/env bash
# Interaction Domain, against hand calculations, OpenSees' own fibre section, Moment Curvature's deck
# and EN 1993-1-1's plastic and elastic resistances.
# Needs a built Alpaca4d.Core, a C# compiler (mcs), a net48 RhinoCommon and OpenSees.
set -e
cd "$(dirname "$0")"
COREDIR=${COREDIR:-../../Alpaca.Core/bin/Release/net48}
CORE="$COREDIR/Alpaca4d.Core.dll"
RHINO=${RHINO:-$(ls ~/.nuget/packages/rhinocommon/7.18.*/lib/net48/RhinoCommon.dll 2>/dev/null | head -1)}
OPENSEES=${OPENSEES:-$(command -v OpenSees || ls /Applications/OpenSees*/bin/OpenSees 2>/dev/null | tail -1)}
[ -f "$CORE" ]     || { echo "build Alpaca.Core first, or set COREDIR=..."; exit 1; }
[ -f "$RHINO" ]    || { echo "set RHINO=/path/to/net48/RhinoCommon.dll"; exit 1; }
[ -n "$OPENSEES" ] || { echo "no OpenSees found; set OPENSEES=/path/to/OpenSees"; exit 1; }

work=$(mktemp -d); trap 'rm -rf "$work"' EXIT
cp InteractionDomainCheck.cs "$work/"
cp "$COREDIR"/*.dll "$work/" 2>/dev/null || true
cp "$RHINO" "$work/"

( cd "$work"
  NETSTD=${NETSTD:-$(ls /Library/Frameworks/Mono.framework/Versions/Current/lib/mono/4.5/Facades/netstandard.dll 2>/dev/null | head -1)}
  NETSTD_REF=""
  [ -n "$NETSTD" ] && NETSTD_REF="-r:$NETSTD"
  mcs -target:exe -out:InteractionDomainCheck.exe -r:Alpaca4d.Core.dll -r:RhinoCommon.dll $NETSTD_REF InteractionDomainCheck.cs
  mono InteractionDomainCheck.exe "$OPENSEES" )
