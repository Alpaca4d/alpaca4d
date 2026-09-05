#!/usr/bin/env bash
# Link, Zero Length Spring and Equal DOF checks.
#
# Two halves. The first checks the tcl the three classes write; it needs a built Alpaca4d.Core,
# a C# compiler and a net48 RhinoCommon, and always runs. The second solves the decks that same
# code writes and needs OpenSees on PATH; without it the decks are written and left unsolved.
set -e
cd "$(dirname "$0")"
CORE=${CORE:-../../Alpaca.Core/bin/Release/net48/Alpaca4d.Core.dll}
RHINO=${RHINO:-$(ls ~/.nuget/packages/rhinocommon/7.18.*/lib/net48/RhinoCommon.dll 2>/dev/null | head -1)}
[ -f "$CORE" ]  || { echo "build Alpaca.Core first, or set CORE=..."; exit 1; }
[ -f "$RHINO" ] || { echo "set RHINO=/path/to/net48/RhinoCommon.dll"; exit 1; }

work=$(mktemp -d); trap 'rm -rf "$work"' EXIT
cp Link.cs Deck.cs "$work/"; cp "$CORE" "$RHINO" "$work/"

( cd "$work"
  mcs -target:exe -out:Link.exe -r:Alpaca4d.Core.dll -r:RhinoCommon.dll Link.cs Deck.cs
  mono Link.exe .

  OPENSEES=${OPENSEES:-$(command -v OpenSees || echo /Applications/OpenSees3.5.0/bin/OpenSees)}
  if [ ! -x "$OPENSEES" ]; then
    echo
    echo "note: OpenSees not found, the decks were written but not solved"
    exit 0
  fi

  fails=0
  for deck in link spring equaldof laminate joins; do
    # OpenSees says nothing about an unreadable deck other than through its exit code, so both
    # the printed FAILs and a non-zero exit are worth catching.
    out=$("$OPENSEES" "$deck.tcl" 2>&1) || { echo "$out"; echo "  [FAIL] $deck.tcl did not run"; fails=1; continue; }
    echo "$out" | sed -n '/^[A-E]\./,$p'
    echo "$out" | grep -q "\[FAIL\]" && fails=1
  done

  echo
  [ "$fails" -eq 0 ] && echo "all solver checks passed" || { echo "solver checks failed"; exit 1; }
)
