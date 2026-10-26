#!/usr/bin/env bash
# Ten node tetrahedron checks: the numbers Alpaca4d keeps about the element against what OpenSees
# does with it, the stress reader on its four integration points, and the OpenSees it refuses.
# Needs a built Alpaca4d.Core, a C# compiler and a net48 RhinoCommon. The decks need an OpenSees
# that knows TenNodeTetrahedronStd - OPENSEES_TET10, or /Applications/OpenSeesLatest/bin/OpenSees;
# 3.8.0 and earlier do not. Without one, only the checks that need no solver run. An OpenSees on
# PATH that does not know the element is run too, for the error Run Analysis has to explain.
set -e
cd "$(dirname "$0")"
COREDIR=${COREDIR:-../../Alpaca.Core/bin/Release/net48}
CORE="$COREDIR/Alpaca4d.Core.dll"
RHINO=${RHINO:-$(ls ~/.nuget/packages/rhinocommon/7.18.*/lib/net48/RhinoCommon.dll 2>/dev/null | head -1)}
[ -f "$CORE" ]  || { echo "build Alpaca.Core first, or set COREDIR=..."; exit 1; }
[ -f "$RHINO" ] || { echo "set RHINO=/path/to/net48/RhinoCommon.dll"; exit 1; }

work=$(mktemp -d); trap 'rm -rf "$work"' EXIT
cp TenNodeTetrahedron.cs element.tcl patch.tcl bending.tcl bodyforce.tcl cantilever.py "$work/"
cp "$COREDIR"/*.dll "$work/" 2>/dev/null || true
cp "$RHINO" "$work/"

knows() { "$1" patch.tcl > /dev/null 2>&1 && [ -f patch.mpco ]; }

( cd "$work"
  OPENSEES=${OPENSEES_TET10:-/Applications/OpenSeesLatest/bin/OpenSees}
  if [ -x "$OPENSEES" ] && knows "$OPENSEES"; then
    "$OPENSEES" bending.tcl   > /dev/null 2>&1 || echo "warning: bending.tcl did not solve"
    "$OPENSEES" bodyforce.tcl > /dev/null 2>&1 || echo "warning: bodyforce.tcl did not solve"
    python3 cantilever.py tet10 10 1 1 cantilever10.tcl
    python3 cantilever.py tet4 10 1 1 cantilever4.tcl
    "$OPENSEES" cantilever10.tcl 2>&1 | grep TIP_UZ > cantilever10.txt || echo "warning: cantilever10.tcl did not solve"
    "$OPENSEES" cantilever4.tcl  2>&1 | grep TIP_UZ > cantilever4.txt  || echo "warning: cantilever4.tcl did not solve"
  else
    echo "note: no OpenSees that knows TenNodeTetrahedronStd (set OPENSEES_TET10), the decks are skipped"
    rm -f patch.mpco
  fi

  # Exit code 0 either way: OpenSees reports a deck that stops at an unknown command, and exits 0.
  OLD=$(command -v OpenSees || true)
  if [ -n "$OLD" ] && { "$OLD" patch.tcl > old.log 2>&1; grep -q "TenNodeTetrahedronStd not known" old.log; }; then
    echo "the OpenSees on PATH ($OLD) does not know the element; its log is checked"
  else
    rm -f old.log
  fi

  NETSTD=${NETSTD:-$(ls /Library/Frameworks/Mono.framework/Versions/Current/lib/mono/4.5/Facades/netstandard.dll 2>/dev/null | head -1)}
  NETSTD_REF=""
  [ -n "$NETSTD" ] && NETSTD_REF="-r:$NETSTD"
  mcs -target:exe -out:TenNodeTetrahedron.exe -r:Alpaca4d.Core.dll -r:RhinoCommon.dll -r:PureHDF.dll -r:System.Drawing.dll $NETSTD_REF TenNodeTetrahedron.cs
  mono TenNodeTetrahedron.exe . )
