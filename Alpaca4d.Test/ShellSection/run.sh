#!/usr/bin/env bash
# Shell Section: the forces through a cut drawn across shells.
# Needs a built Alpaca.Core (Release), a C# compiler and a net48 RhinoCommon. OpenSees on PATH, or
# OPENSEES=..., is optional: with it plates and walls are solved and their cuts checked against
# statics, without it only the made-up fields are.
set -e
cd "$(dirname "$0")"
COREDIR=${COREDIR:-../../Alpaca.Core/bin/Release/net48}
CORE="$COREDIR/Alpaca4d.Core.dll"
RHINO=${RHINO:-$(ls ~/.nuget/packages/rhinocommon/7.18.*/lib/net48/RhinoCommon.dll 2>/dev/null | head -1)}
[ -f "$CORE" ]  || { echo "build Alpaca.Core first, or set COREDIR=..."; exit 1; }
[ -f "$RHINO" ] || { echo "set RHINO=/path/to/net48/RhinoCommon.dll"; exit 1; }
OPENSEES=${OPENSEES:-$(command -v OpenSees || echo /Applications/OpenSees3.8.0/bin/OpenSees)}
[ -x "$OPENSEES" ] || OPENSEES=""

work=$(mktemp -d); trap 'rm -rf "$work"' EXIT
cp ShellSection.cs "$work/"
# PureHDF and its dependencies come from the build output; the reader needs them at run time.
cp "$COREDIR"/*.dll "$work/" 2>/dev/null || true
cp "$RHINO" "$work/"

( cd "$work"
  # PureHDF is a netstandard2.0 assembly, so mcs needs the facade to resolve its types.
  NETSTD=${NETSTD:-$(ls /Library/Frameworks/Mono.framework/Versions/Current/lib/mono/4.5/Facades/netstandard.dll 2>/dev/null | head -1)}
  NETSTD_REF=""
  [ -n "$NETSTD" ] && NETSTD_REF="-r:$NETSTD"
  mcs -target:exe -out:ShellSection.exe -r:Alpaca4d.Core.dll -r:RhinoCommon.dll -r:PureHDF.dll $NETSTD_REF ShellSection.cs
  mono ShellSection.exe $OPENSEES )
