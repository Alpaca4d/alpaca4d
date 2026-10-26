#!/usr/bin/env bash
# Response spectrum analysis, without Rhino: the spectra, the combinations, and a run through OpenSees
# read back through Read. Needs a built Alpaca4d.Core, a C# compiler, a net48 RhinoCommon and OpenSees;
# without OpenSees the run is skipped and the rest still checked.
set -e
cd "$(dirname "$0")"
COREDIR=${COREDIR:-../../Alpaca.Core/bin/Release/net48}
CORE="$COREDIR/Alpaca4d.Core.dll"
RHINO=${RHINO:-$(ls ~/.nuget/packages/rhinocommon/7.18.*/lib/net48/RhinoCommon.dll 2>/dev/null | head -1)}
[ -f "$CORE" ]  || { echo "build Alpaca.Core first, or set COREDIR=..."; exit 1; }
[ -f "$RHINO" ] || { echo "set RHINO=/path/to/net48/RhinoCommon.dll"; exit 1; }
OPENSEES=${OPENSEES:-$(command -v OpenSees || echo /Applications/OpenSees3.8.0/bin/OpenSees)}

work=$(mktemp -d); trap 'rm -rf "$work"' EXIT
cp SpectrumCheck.cs "$work/"
# PureHDF and its dependencies come from the build output; the reader needs them at run time.
cp "$COREDIR"/*.dll "$work/" 2>/dev/null || true
cp "$RHINO" "$work/"

( cd "$work"
  # PureHDF is a netstandard2.0 assembly, so the compiler needs the facade to resolve its types.
  NETSTD=${NETSTD:-$(ls /Library/Frameworks/Mono.framework/Versions/Current/lib/mono/4.5/Facades/netstandard.dll /opt/homebrew/lib/mono/4.5/Facades/netstandard.dll 2>/dev/null | head -1)}
  NETSTD_REF=""
  [ -n "$NETSTD" ] && NETSTD_REF="-r:$NETSTD"
  csc -nologo -langversion:latest -target:exe -out:SpectrumCheck.exe -r:Alpaca4d.Core.dll -r:RhinoCommon.dll -r:System.Drawing.dll $NETSTD_REF SpectrumCheck.cs
  mono SpectrumCheck.exe "$OPENSEES" )
