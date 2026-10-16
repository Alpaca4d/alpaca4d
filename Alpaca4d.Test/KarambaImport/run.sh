#!/usr/bin/env bash
# Karamba to Alpaca: the Karamba-free half. Needs a built Alpaca4d.Core, a C# compiler (csc or mcs)
# and a net48 RhinoCommon. Karamba3D is not needed.
set -e
cd "$(dirname "$0")"

CORE=${CORE:-../../Alpaca.Core/bin/Release/net48/Alpaca4d.Core.dll}
RHINO=${RHINO:-$(ls ~/.nuget/packages/rhinocommon/7.18.*/lib/net48/RhinoCommon.dll 2>/dev/null | head -1)}

[ -f "$CORE" ]  || { echo "build Alpaca.Core first, or set CORE=..."; exit 1; }
[ -f "$RHINO" ] || { echo "set RHINO=/path/to/net48/RhinoCommon.dll"; exit 1; }

CSC=$(command -v csc || command -v mcs)
work=$(mktemp -d); trap 'rm -rf "$work"' EXIT
cp KarambaImport.cs "$work/"
cp "$CORE" "$RHINO" "$work/"

( cd "$work"
  "$CSC" -nologo -target:exe -out:KarambaImport.exe -r:Alpaca4d.Core.dll -r:RhinoCommon.dll KarambaImport.cs
  mono KarambaImport.exe )
