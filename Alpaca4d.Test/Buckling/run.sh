#!/usr/bin/env bash
# Linear Buckling Analysis, against closed-form cases.
# Needs a built Alpaca4d.Core, a C# compiler (mcs) and OpenSees. Every OpenSees given is run in
# turn: OPENSEES="/path/a /path/b" ./run.sh. A build that prints matrices sparse (newer than 3.8.0)
# also runs the Penalty cross-checks of the tied nodes.
set -e
cd "$(dirname "$0")"
CORE=${CORE:-../../Alpaca.Core/bin/Release/net48/Alpaca4d.Core.dll}
[ -f "$CORE" ] || { echo "build Alpaca.Core first, or set CORE=..."; exit 1; }
OPENSEES=${OPENSEES:-$(command -v OpenSees || ls /Applications/OpenSees*/bin/OpenSees 2>/dev/null | tail -1)}
[ -n "$OPENSEES" ] || { echo "no OpenSees found; set OPENSEES=/path/to/OpenSees"; exit 1; }

work=$(mktemp -d); trap 'rm -rf "$work"' EXIT
cp BucklingCheck.cs "$CORE" "$work/"
( cd "$work"
  mcs -target:exe -out:BucklingCheck.exe -r:Alpaca4d.Core.dll BucklingCheck.cs
  mono BucklingCheck.exe $OPENSEES )
