#!/usr/bin/env bash
# A deck that stops on a Tcl error has to come back from Model.RunOpenSees as a failure, though
# OpenSees exits 0 then. Needs a built Alpaca4d.Core, a C# compiler (mcs) and a net48 RhinoCommon.
# The decks run on every OpenSees found - OPENSEES, the one on PATH and /Applications/OpenSees*/ -
# and without one only the reading of the trace is checked.
set -e
cd "$(dirname "$0")"
COREDIR=${COREDIR:-../../Alpaca.Core/bin/Release/net48}
CORE="$COREDIR/Alpaca4d.Core.dll"
RHINO=${RHINO:-$(ls ~/.nuget/packages/rhinocommon/7.18.*/lib/net48/RhinoCommon.dll 2>/dev/null | head -1)}
[ -f "$CORE" ]  || { echo "build Alpaca.Core first, or set COREDIR=..."; exit 1; }
[ -f "$RHINO" ] || { echo "set RHINO=/path/to/net48/RhinoCommon.dll"; exit 1; }

solvers=()
for s in ${OPENSEES:-} $(command -v OpenSees || true) /Applications/OpenSees*/bin/OpenSees; do
  [ -x "$s" ] || continue
  s=$(cd "$(dirname "$s")" && pwd)/$(basename "$s")
  [[ " ${solvers[*]} " == *" $s "* ]] || solvers+=("$s")
done

work=$(mktemp -d); trap 'rm -rf "$work"' EXIT
cp DeckErrors.cs "$work/"
cp "$COREDIR"/*.dll "$work/" 2>/dev/null || true
cp "$RHINO" "$work/"

( cd "$work"
  NETSTD=${NETSTD:-$(ls /Library/Frameworks/Mono.framework/Versions/Current/lib/mono/4.5/Facades/netstandard.dll 2>/dev/null | head -1)}
  NETSTD_REF=""
  [ -n "$NETSTD" ] && NETSTD_REF="-r:$NETSTD"
  mcs -target:exe -out:DeckErrors.exe -r:Alpaca4d.Core.dll -r:RhinoCommon.dll $NETSTD_REF DeckErrors.cs
  mono DeckErrors.exe "${solvers[@]}" )
