#!/usr/bin/env bash
# Element name and result-filter checks. Needs a built Alpaca4d.Core, a C# compiler and a net48 RhinoCommon.
set -e
cd "$(dirname "$0")"
CORE=${CORE:-../../Alpaca.Core/bin/Release/net48/Alpaca4d.Core.dll}
RHINO=${RHINO:-$(ls ~/.nuget/packages/rhinocommon/7.18.*/lib/net48/RhinoCommon.dll 2>/dev/null | head -1)}
[ -f "$CORE" ]  || { echo "build Alpaca.Core first, or set CORE=..."; exit 1; }
[ -f "$RHINO" ] || { echo "set RHINO=/path/to/net48/RhinoCommon.dll"; exit 1; }
work=$(mktemp -d); trap 'rm -rf "$work"' EXIT
cp ElementId.cs "$work/"; cp "$CORE" "$RHINO" "$work/"
( cd "$work"
  mcs -target:exe -out:ElementId.exe -r:Alpaca4d.Core.dll -r:RhinoCommon.dll ElementId.cs
  mono ElementId.exe )
