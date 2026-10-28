#!/usr/bin/env bash
# Solver and algorithm benchmark. Needs python3 and an OpenSees binary.
# Set OPENSEES to point at one, e.g. the official one:
#   OPENSEES=/Applications/OpenSees3.8.0/bin/OpenSees ./run.sh
# With an OpenSees built with Mumps (Tools/OpenSees), set OPENSEES_WITHOUT_MUMPS to an official
# one as well, so Auto's fallback is run too, and MULTICORE=1 to time small and large models
# on one core and on all of them (multicore.py; very large ones take it a while).
set -e
cd "$(dirname "$0")"
export OPENSEES=${OPENSEES:-OpenSees}
command -v "$OPENSEES" >/dev/null 2>&1 || [ -x "$OPENSEES" ] || {
  echo "set OPENSEES=/path/to/OpenSees"; exit 1; }

# What Alpaca.Core writes for Mumps and Auto, run on the OpenSees given (needs a built Core).
CORE=${CORE:-../../Alpaca.Core/bin/Release/net48/Alpaca4d.Core.dll}
RHINO=${RHINO:-$(ls ~/.nuget/packages/rhinocommon/7.18.*/lib/net48/RhinoCommon.dll 2>/dev/null | head -1)}
if [ -f "$CORE" ] && [ -f "$RHINO" ] && command -v mcs >/dev/null 2>&1; then
  work=$(mktemp -d); trap 'rm -rf "$work"' EXIT
  cp SystemChoice.cs "$CORE" "$RHINO" "$work/"
  ( cd "$work"
    mcs -target:exe -out:SystemChoice.exe -r:Alpaca4d.Core.dll -r:RhinoCommon.dll SystemChoice.cs
    mono SystemChoice.exe )
  echo
else
  echo "Alpaca.Core, RhinoCommon or mcs not found: SystemChoice.cs skipped"
fi

python3 correctness.py     # which solvers can be trusted at all
echo
python3 bench.py           # and of those, which is quickest
if [ -n "$MULTICORE" ]; then
  echo
  python3 multicore.py --tiers "small,large" --threads 2,4
fi
