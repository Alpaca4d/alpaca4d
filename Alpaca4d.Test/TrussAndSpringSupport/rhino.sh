#!/usr/bin/env bash
# Truss and Spring Support, in a running Rhino 8 with its script server started (StartScriptServer):
# what needs RhinoCommon's native code - Assemble, a support's plane, a truss's curve. RhinoChecks.cs
# builds and assembles models there, solves them with OpenSees and reads them back with Alpaca4d's own
# readers; it also writes the spring support decks, which OpenSees solves here afterwards.
# Needs a built Alpaca4d.Core and Alpaca4d.FemDesign, a C# compiler and OpenSees.
set -e
cd "$(dirname "$0")"
COREDIR=${COREDIR:-../../Alpaca.Core/bin/Release/net48}
FDDIR=${FDDIR:-../../Alpaca4d.FemDesign/bin/Release/net48}
RHINO=${RHINO:-$(ls ~/.nuget/packages/rhinocommon/7.18.*/lib/net48/RhinoCommon.dll 2>/dev/null | head -1)}
RHINOCODE=${RHINOCODE:-"/Applications/Rhino 8.app/Contents/Resources/bin/rhinocode"}
OPENSEES=${OPENSEES:-$(command -v OpenSees || echo /Applications/OpenSees3.8.0/bin/OpenSees)}
[ -f "$COREDIR/Alpaca4d.Core.dll" ] || { echo "build Alpaca.Core first, or set COREDIR=..."; exit 1; }
[ -f "$FDDIR/Alpaca4d.FemDesign.dll" ] || { echo "build Alpaca4d.FemDesign first, or set FDDIR=..."; exit 1; }
[ -f "$RHINO" ] || { echo "set RHINO=/path/to/net48/RhinoCommon.dll"; exit 1; }
[ -x "$OPENSEES" ] || { echo "set OPENSEES=/path/to/OpenSees"; exit 1; }

# A folder of its own each run: Rhino keeps the DLLs it loaded mapped, and overwriting them breaks it.
work=$(mktemp -d)
cp "$COREDIR"/*.dll "$FDDIR"/*.dll "$work/"
cp "$RHINO" TrussCheck.cs RhinoChecks.cs "$work/"

( cd "$work"
  NETSTD=${NETSTD:-$(ls /Library/Frameworks/Mono.framework/Versions/Current/lib/mono/4.5/Facades/netstandard.dll /opt/homebrew/lib/mono/4.5/Facades/netstandard.dll 2>/dev/null | head -1)}
  NETSTD_REF=""
  [ -n "$NETSTD" ] && NETSTD_REF="-r:$NETSTD"
  csc -nologo -langversion:latest -target:library -out:TrussCheck.dll \
      -r:Alpaca4d.Core.dll -r:Alpaca4d.FemDesign.dll -r:RhinoCommon.dll -r:System.Drawing.dll $NETSTD_REF \
      TrussCheck.cs RhinoChecks.cs
  rm RhinoCommon.dll )
sed -e "s|__WORK__|$work|" -e "s|__OPENSEES__|$OPENSEES|" RhinoRun.cs > "$work/RhinoRun.cs"

# rhinocode returns before the script has run, so wait for what it writes.
"$RHINOCODE" script "$work/RhinoRun.cs"
for _ in $(seq 1 300); do [ -f "$work/result.txt" ] && break; sleep 1; done
[ -f "$work/result.txt" ] || { echo "no result: a compile error is in the newest ~/.rhinocode/logs/rhinocode_<Rhino's pid>"; exit 1; }
cat "$work/result.txt"
fails=0
if grep -q "\[FAIL\]" "$work/result.txt"; then fails=1; fi

( cd "$work"
  for deck in spring bearing gap; do
    out=$("$OPENSEES" "$deck.tcl" 2>&1) || { echo "$out" | tail -5; echo "  [FAIL] $deck.tcl did not run"; exit 1; }
    echo
    echo "$out" | sed -n '/^[A-Z]\. /,$p' | grep -E "^[A-Z]\. |\[(PASS|FAIL)\]" || true
    if echo "$out" | grep -q "\[FAIL\]"; then exit 1; fi
  done ) || fails=1

echo
[ "$fails" -eq 0 ] && echo "all rhino and spring solver checks passed" || { echo "checks failed"; exit 1; }
