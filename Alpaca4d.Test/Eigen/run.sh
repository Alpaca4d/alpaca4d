#!/usr/bin/env bash
# The eigenvalues Natural Vibration reads back from OpenSees.
# Needs a built Alpaca4d.Core and a C# compiler (mcs). OpenSees is optional: with it a cantilever is
# solved by each of the three eigen solvers and read back, without it only the reading is checked.
set -e
cd "$(dirname "$0")"
CORE=${CORE:-../../Alpaca.Core/bin/Release/net48/Alpaca4d.Core.dll}
[ -f "$CORE" ] || { echo "build Alpaca.Core first, or set CORE=..."; exit 1; }
OPENSEES=${OPENSEES:-$(command -v OpenSees || ls /Applications/OpenSees*/bin/OpenSees 2>/dev/null | tail -1)}

work=$(mktemp -d); trap 'rm -rf "$work"' EXIT
cp EigenCheck.cs "$CORE" "$work/"
( cd "$work"
  mcs -target:exe -out:EigenCheck.exe -r:Alpaca4d.Core.dll EigenCheck.cs
  mono EigenCheck.exe "$OPENSEES" )
