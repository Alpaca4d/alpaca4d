#!/usr/bin/env bash
# The section optimiser: the order, choosing, the groups and families, and the rounds on recorder files
# OpenSees solves; then a simply supported beam in 20 elements, sized with every round solved afresh.
# Needs a built Alpaca4d.Core, a C# compiler and a net48 RhinoCommon. OpenSees on PATH, or the one the
# plugin ships, is optional: without it only the rules are checked, not the rounds.
set -e
cd "$(dirname "$0")"
COREDIR=${COREDIR:-../../Alpaca.Core/bin/Release/net48}
CORE="$COREDIR/Alpaca4d.Core.dll"
RHINO=${RHINO:-$(ls ~/.nuget/packages/rhinocommon/7.18.*/lib/net48/RhinoCommon.dll 2>/dev/null | head -1)}
[ -f "$CORE" ]  || { echo "build Alpaca.Core first, or set COREDIR=..."; exit 1; }
[ -f "$RHINO" ] || { echo "set RHINO=/path/to/net48/RhinoCommon.dll"; exit 1; }

work=$(mktemp -d); trap 'rm -rf "$work"' EXIT
cp SectionOptimiser.cs SimpleBeam.cs optimiser.tcl "$work/"
# PureHDF, Newtonsoft and the rest come from the build output.
cp "$COREDIR"/*.dll "$work/" 2>/dev/null || true
cp "$RHINO" "$work/"

( cd "$work"
  OPENSEES=${OPENSEES:-$(command -v OpenSees)}
  if [ -x "$OPENSEES" ]; then
    for load in heavy light; do
      "$OPENSEES" optimiser.tcl $load > /dev/null 2>&1 || echo "warning: optimiser.tcl $load did not solve"
    done
  else
    echo "note: OpenSees not found, the rounds will be skipped"
  fi

  # PureHDF is a netstandard2.0 assembly, so mcs needs the facade to resolve its types.
  NETSTD=${NETSTD:-$(ls /Library/Frameworks/Mono.framework/Versions/Current/lib/mono/4.5/Facades/netstandard.dll 2>/dev/null | head -1)}
  NETSTD_REF=""
  [ -n "$NETSTD" ] && NETSTD_REF="-r:$NETSTD"
  mcs -target:exe -out:SectionOptimiser.exe -r:Alpaca4d.Core.dll -r:Newtonsoft.Json.dll -r:RhinoCommon.dll -r:System.Drawing.dll $NETSTD_REF SectionOptimiser.cs
  mono SectionOptimiser.exe .

  # A simply supported beam in 20 elements, every round solved afresh.
  if [ -x "$OPENSEES" ]; then
    mcs -target:exe -out:SimpleBeam.exe -r:Alpaca4d.Core.dll -r:Newtonsoft.Json.dll -r:RhinoCommon.dll -r:System.Drawing.dll $NETSTD_REF SimpleBeam.cs
    mono SimpleBeam.exe "$OPENSEES" "$(pwd)"
  fi )
