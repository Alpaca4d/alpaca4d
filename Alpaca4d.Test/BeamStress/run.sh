#!/usr/bin/env bash
# Beam stress recovery checks.
# Needs a built Alpaca4d.Core, a C# compiler and a net48 RhinoCommon. OpenSees on PATH is
# optional: with it fibre.tcl and mpco.tcl are solved, the recovered direct stresses are checked
# against the fibre stresses the solver integrated, and Read.BeamStresses is run on a real recorder
# file. Without it only the section arithmetic runs.
set -e
cd "$(dirname "$0")"
COREDIR=${COREDIR:-../../Alpaca.Core/bin/Release/net48}
CORE="$COREDIR/Alpaca4d.Core.dll"
RHINO=${RHINO:-$(ls ~/.nuget/packages/rhinocommon/7.18.*/lib/net48/RhinoCommon.dll 2>/dev/null | head -1)}
[ -f "$CORE" ]  || { echo "build Alpaca.Core first, or set CORE=..."; exit 1; }
[ -f "$RHINO" ] || { echo "set RHINO=/path/to/net48/RhinoCommon.dll"; exit 1; }

work=$(mktemp -d); trap 'rm -rf "$work"' EXIT
cp BeamStress.cs fibre.tcl mpco.tcl "$work/"
# PureHDF and its dependencies come from the build output; the reader needs them at run time.
cp "$COREDIR"/*.dll "$work/" 2>/dev/null || true
cp "$RHINO" "$work/"

( cd "$work"
  OPENSEES=${OPENSEES:-$(command -v OpenSees || echo /Applications/OpenSees3.5.0/bin/OpenSees)}
  if [ -x "$OPENSEES" ]; then
    "$OPENSEES" fibre.tcl > /dev/null 2>&1 || echo "warning: fibre.tcl did not solve"
    "$OPENSEES" mpco.tcl  > /dev/null 2>&1 || echo "warning: mpco.tcl did not solve"
  else
    echo "note: OpenSees not found, the checks against the solver will be skipped"
  fi

  # PureHDF is a netstandard2.0 assembly, so mcs needs the facade to resolve its types.
  NETSTD=${NETSTD:-$(ls /Library/Frameworks/Mono.framework/Versions/Current/lib/mono/4.5/Facades/netstandard.dll 2>/dev/null | head -1)}
  NETSTD_REF=""
  [ -n "$NETSTD" ] && NETSTD_REF="-r:$NETSTD"
  mcs -target:exe -out:BeamStress.exe -r:Alpaca4d.Core.dll -r:RhinoCommon.dll -r:System.Drawing.dll $NETSTD_REF BeamStress.cs
  mono BeamStress.exe . )
