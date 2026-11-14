#!/usr/bin/env bash
# Truss and Spring Support, without Rhino.
#
# Three steps. TrussCheck.exe write checks the Tcl the classes write and writes the decks around it;
# OpenSees solves the decks, which check themselves against closed form; TrussCheck.exe read reads the
# results files back through Read.ForceBeamColumn. Needs a built Alpaca4d.Core, a C# compiler, a net48
# RhinoCommon and OpenSees; without OpenSees only the first step runs. What needs Rhino is rhino.sh.
set -e
cd "$(dirname "$0")"
COREDIR=${COREDIR:-../../Alpaca.Core/bin/Release/net48}
CORE="$COREDIR/Alpaca4d.Core.dll"
RHINO=${RHINO:-$(ls ~/.nuget/packages/rhinocommon/7.18.*/lib/net48/RhinoCommon.dll 2>/dev/null | head -1)}
[ -f "$CORE" ]  || { echo "build Alpaca.Core first, or set COREDIR=..."; exit 1; }
[ -f "$RHINO" ] || { echo "set RHINO=/path/to/net48/RhinoCommon.dll"; exit 1; }

work=$(mktemp -d); trap 'rm -rf "$work"' EXIT
cp TrussCheck.cs "$work/"
# PureHDF and its dependencies come from the build output; the reader needs them at run time.
cp "$COREDIR"/*.dll "$work/" 2>/dev/null || true
cp "$RHINO" "$work/"

( cd "$work"
  # PureHDF is a netstandard2.0 assembly, so the compiler needs the facade to resolve its types.
  NETSTD=${NETSTD:-$(ls /Library/Frameworks/Mono.framework/Versions/Current/lib/mono/4.5/Facades/netstandard.dll /opt/homebrew/lib/mono/4.5/Facades/netstandard.dll 2>/dev/null | head -1)}
  NETSTD_REF=""
  [ -n "$NETSTD" ] && NETSTD_REF="-r:$NETSTD"
  csc -nologo -langversion:latest -target:exe -out:TrussCheck.exe -r:Alpaca4d.Core.dll -r:RhinoCommon.dll -r:System.Drawing.dll $NETSTD_REF TrussCheck.cs
  mono TrussCheck.exe write .

  OPENSEES=${OPENSEES:-$(command -v OpenSees || echo /Applications/OpenSees3.8.0/bin/OpenSees)}
  if [ ! -x "$OPENSEES" ]; then
    echo
    echo "note: OpenSees not found, the decks were written but not solved"
    exit 0
  fi

  fails=0
  for deck in panel cable mass mixed; do
    # OpenSees says nothing about an unreadable deck other than through its exit code, so both
    # the printed FAILs and a non-zero exit are worth catching.
    out=$("$OPENSEES" "$deck.tcl" 2>&1) || { echo "$out" | tail -5; echo "  [FAIL] $deck.tcl did not run"; fails=1; continue; }
    echo
    echo "$out" | sed -n '/^[A-Z]\. /,$p' | grep -E "^[A-Z]\. |\[(PASS|FAIL)\]" || true
    echo "$out" | grep -q "\[FAIL\]" && fails=1
  done
  echo
  [ "$fails" -eq 0 ] && echo "all solver checks passed" || { echo "solver checks failed"; exit 1; }

  mono TrussCheck.exe read . )
