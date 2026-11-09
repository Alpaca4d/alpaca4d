#!/usr/bin/env bash
# 3D printing of fresh concrete: the printed bricks and their ages, the fresh concrete and the
# joint band, and a printed wall buckling where Suiker (2018) says it does.
# Needs a built Alpaca4d.Core, a C# compiler (mcs), a net48 RhinoCommon (Point3d only, no Rhino)
# and an OpenSees that prints its matrices sparse (newer than 3.8.0), for the buckling.
#
#   ./run.sh                                        the checks
#   ./run.sh --cylinder 2 2 48 1 10 20 30 40        a table for the Wolfs cylinder: bricks across,
#                                                   up, round, round sides, then layers printed
set -e
cd "$(dirname "$0")"
CORE=${CORE:-../../Alpaca.Core/bin/Release/net48/Alpaca4d.Core.dll}
RHINO=${RHINO:-$(ls ~/.nuget/packages/rhinocommon/7.18.*/lib/net48/RhinoCommon.dll 2>/dev/null | head -1)}
OPENSEES=${OPENSEES:-/Applications/OpenSeesLatest-Mumps/bin/OpenSees}
[ -f "$CORE" ] || { echo "build Alpaca.Core first, or set CORE=..."; exit 1; }
[ -f "$RHINO" ] || { echo "set RHINO=/path/to/net48/RhinoCommon.dll"; exit 1; }
[ -f "$OPENSEES" ] || { echo "no OpenSees at $OPENSEES; set OPENSEES=/path/to/OpenSees (the buckling checks are skipped)"; OPENSEES=""; }

work=$(mktemp -d); trap 'rm -rf "$work"' EXIT
cp PrintingCheck.cs Printing.cs "$CORE" "$RHINO" "$work/"
( cd "$work"
  mcs -target:exe -out:PrintingCheck.exe -r:Alpaca4d.Core.dll -r:RhinoCommon.dll PrintingCheck.cs Printing.cs
  mono PrintingCheck.exe $OPENSEES "$@" )
