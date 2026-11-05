#!/usr/bin/env bash
# Topology methods: MMA, multi-material topology optimisation (Zheng et al. 2024) and BESO (Xia et al.
# 2018). Needs a built Alpaca4d.Core, a C# compiler, a net48 RhinoCommon and an OpenSees: the one on PATH,
# or OPENSEES=/path/to/OpenSees.
#
#     ./run.sh                                   the checks
#     ./run.sh bench zheng 3                     Zheng et al.'s 3D cantilever with 3 materials (2 or 5 too)
#     ./run.sh bench zheng 3 60 30 15 5 auto     ... its grid, filter radius, and the component's objective scale
#     ./run.sh bench beso-3d                     BESO on a 40 x 20 x 4 cantilever, then reference.py on it
#     ./run.sh bench beso-cantilever             esoL's clamped cantilever, one element deep, then reference.py
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
cp TopologyMethods.cs "$work/"
cp "$COREDIR"/*.dll "$work/" 2>/dev/null || true
cp "$RHINO" "$work/"

NETSTD=${NETSTD:-$(ls /Library/Frameworks/Mono.framework/Versions/Current/lib/mono/4.5/Facades/netstandard.dll 2>/dev/null | head -1)}
NETSTD_REF=""
[ -n "$NETSTD" ] && NETSTD_REF="-r:$NETSTD"

( cd "$work"
  mcs -target:exe -out:TopologyMethods.exe -r:Alpaca4d.Core.dll -r:RhinoCommon.dll -r:System.IO.Compression.dll $NETSTD_REF TopologyMethods.cs
  if [ "$1" = "bench" ]; then
    mono TopologyMethods.exe "$OPENSEES" "$results" bench "${@:2}"
  else
    mono TopologyMethods.exe "$OPENSEES" "$work/files"
  fi )

if [ "$1" = "bench" ] && { [ "$2" = "beso-3d" ] || [ "$2" = "beso-cantilever" ]; }; then
  PYTHON=${PYTHON:-python3}
  if "$PYTHON" -c "import numpy, scipy" 2>/dev/null; then
    "$PYTHON" reference.py "$2" "$results" "${@:3}"
  else
    echo "note: $PYTHON has no numpy and scipy, so reference.py was not run (set PYTHON=...)"
  fi
fi
