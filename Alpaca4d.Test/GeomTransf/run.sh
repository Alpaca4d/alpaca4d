#!/usr/bin/env bash
# The words a beam's GeomTransf input takes, without Rhino.
#
# Two steps. GeomTransfCheck.exe checks GeomTransf.TryParseType and the line each type writes, and
# writes a deck around that line; OpenSees solves it and checks it against closed form. Needs a built
# Alpaca4d.Core, a C# compiler, a net48 RhinoCommon and OpenSees; without OpenSees only the first step
# runs. The component itself - a word or a Value List on its input - needs Grasshopper.
set -e
cd "$(dirname "$0")"
COREDIR=${COREDIR:-../../Alpaca.Core/bin/Release/net48}
CORE="$COREDIR/Alpaca4d.Core.dll"
RHINO=${RHINO:-$(ls ~/.nuget/packages/rhinocommon/7.18.*/lib/net48/RhinoCommon.dll 2>/dev/null | head -1)}
[ -f "$CORE" ]  || { echo "build Alpaca.Core first, or set COREDIR=..."; exit 1; }
[ -f "$RHINO" ] || { echo "set RHINO=/path/to/net48/RhinoCommon.dll"; exit 1; }

work=$(mktemp -d); trap 'rm -rf "$work"' EXIT
cp GeomTransfCheck.cs "$work/"
cp "$COREDIR"/*.dll "$work/" 2>/dev/null || true
cp "$RHINO" "$work/"

( cd "$work"
  NETSTD=${NETSTD:-$(ls /Library/Frameworks/Mono.framework/Versions/Current/lib/mono/4.5/Facades/netstandard.dll /opt/homebrew/lib/mono/4.5/Facades/netstandard.dll 2>/dev/null | head -1)}
  NETSTD_REF=""
  [ -n "$NETSTD" ] && NETSTD_REF="-r:$NETSTD"
  csc -nologo -langversion:latest -target:exe -out:GeomTransfCheck.exe -r:Alpaca4d.Core.dll -r:RhinoCommon.dll $NETSTD_REF GeomTransfCheck.cs
  mono GeomTransfCheck.exe .

  OPENSEES=${OPENSEES:-$(command -v OpenSees || echo /Applications/OpenSees3.8.0/bin/OpenSees)}
  if [ ! -x "$OPENSEES" ]; then
    echo
    echo "note: OpenSees not found, the deck was written but not solved"
    exit 0
  fi

  out=$("$OPENSEES" curl.tcl 2>&1) || { echo "$out" | tail -5; echo "  [FAIL] curl.tcl did not run"; exit 1; }
  echo
  echo "$out" | grep -E "^[A-Z]\. |\[(PASS|FAIL)\]" || true
  echo
  if echo "$out" | grep -q "\[FAIL\]"; then echo "solver checks failed"; exit 1; fi
  echo "all solver checks passed" )
