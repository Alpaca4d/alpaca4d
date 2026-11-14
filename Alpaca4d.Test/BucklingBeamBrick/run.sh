#!/usr/bin/env bash
# Linear Buckling Analysis, beams against bricks: the same structures drawn both ways.
# Needs a built Alpaca4d.Core, a C# compiler (mcs) and an OpenSees that prints its matrices sparse
# (newer than 3.8.0): the finer brick meshes are past the 4000 equations of a dense print.
#
#   ./run.sh                     every structure
#   ./run.sh columns arch        some: columns, portal, arch, lateral
set -e
cd "$(dirname "$0")"
CORE=${CORE:-../../Alpaca.Core/bin/Release/net48/Alpaca4d.Core.dll}
OPENSEES=${OPENSEES:-/Applications/OpenSeesLatest-Mumps/bin/OpenSees}
[ -f "$CORE" ] || { echo "build Alpaca.Core first, or set CORE=..."; exit 1; }
[ -f "$OPENSEES" ] || { echo "no OpenSees at $OPENSEES; set OPENSEES=/path/to/OpenSees"; exit 1; }

work=$(mktemp -d); trap 'rm -rf "$work"' EXIT
cp BeamBrick.cs "$CORE" "$work/"
( cd "$work"
  mcs -target:exe -out:BeamBrick.exe -r:Alpaca4d.Core.dll BeamBrick.cs
  mono BeamBrick.exe "$OPENSEES" "$@" )
