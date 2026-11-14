#!/usr/bin/env bash
# Topology optimisation: the density filter, the OpenSees kept open, the element energies and a small
# optimisation. Needs a built Alpaca4d.Core, a C# compiler, a net48 RhinoCommon and an OpenSees: the
# one on PATH, or OPENSEES=/path/to/OpenSees.
#
#     ./run.sh                          the checks
#     ./run.sh bench cantilever         top3d's cantilever, then reference.py on it when numpy is there
#     ./run.sh bench simply-supported   the simply supported beam, the same way
#     ./run.sh bench cantilever-20k     a cantilever of 19 200 bricks, for the time it takes
#     ./run.sh bench bridge             Tools/Examples/TopologyBridge.gh as it opens (bridge-<n>: n bricks per metre)
#
# A benchmark writes its history and densities to ./results (or RESULTS=...), which reference.py reads.
set -e
cd "$(dirname "$0")"
COREDIR=${COREDIR:-../../Alpaca.Core/bin/Release/net48}
CORE="$COREDIR/Alpaca4d.Core.dll"
RHINO=${RHINO:-$(ls ~/.nuget/packages/rhinocommon/7.18.*/lib/net48/RhinoCommon.dll 2>/dev/null | head -1)}
OPENSEES=${OPENSEES:-$(command -v OpenSees || true)}
[ -f "$CORE" ]  || { echo "build Alpaca.Core first, or set COREDIR=..."; exit 1; }
[ -f "$RHINO" ] || { echo "set RHINO=/path/to/net48/RhinoCommon.dll"; exit 1; }
[ -x "$OPENSEES" ] || { echo "put OpenSees on PATH, or set OPENSEES=/path/to/OpenSees"; exit 1; }

here=$(pwd)
results=${RESULTS:-$here/results}
work=$(mktemp -d); trap 'rm -rf "$work"' EXIT
cp TopologyOptimisation.cs "$work/"
cp "$COREDIR"/*.dll "$work/" 2>/dev/null || true
cp "$RHINO" "$work/"

NETSTD=${NETSTD:-$(ls /Library/Frameworks/Mono.framework/Versions/Current/lib/mono/4.5/Facades/netstandard.dll 2>/dev/null | head -1)}
NETSTD_REF=""
[ -n "$NETSTD" ] && NETSTD_REF="-r:$NETSTD"

( cd "$work"
  mcs -target:exe -out:TopologyOptimisation.exe -r:Alpaca4d.Core.dll -r:RhinoCommon.dll -r:System.IO.Compression.dll $NETSTD_REF TopologyOptimisation.cs
  if [ "$1" = "bench" ]; then
    mono TopologyOptimisation.exe "$OPENSEES" "$results" bench "$2" ${3:-}
  else
    mono TopologyOptimisation.exe "$OPENSEES" "$work/files"
  fi )

if [ "$1" = "bench" ] && { [ "$2" = "cantilever" ] || [ "$2" = "simply-supported" ]; }; then
  PYTHON=${PYTHON:-python3}
  if "$PYTHON" -c "import numpy, scipy" 2>/dev/null; then
    "$PYTHON" reference.py "$2" "$results"
  else
    echo "note: $PYTHON has no numpy and scipy, so reference.py was not run (set PYTHON=...)"
  fi
fi
