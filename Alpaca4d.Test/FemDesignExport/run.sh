#!/usr/bin/env bash
# Alpaca4DToFEM-Design without Rhino. Needs a built Alpaca4d.FemDesign, a C# compiler (csc) and mono.
set -e
cd "$(dirname "$0")"

OUT=${OUT:-../../Alpaca4d.FemDesign/bin/Release/net48}
[ -f "$OUT/Alpaca4d.FemDesign.dll" ] || { echo "build Alpaca4d.FemDesign first (dotnet build -c Release), or set OUT=..."; exit 1; }

work=$(mktemp -d); trap 'rm -rf "$work"' EXIT
cp FemDesignExport.cs "$work/"
cp "$OUT"/*.dll "$work/"

( cd "$work"
  csc -nologo -langversion:latest -target:exe -out:FemDesignExport.exe \
      -r:Alpaca4d.Core.dll -r:Alpaca4d.FemDesign.dll -r:FemDesign.Communication.dll -r:RhinoCommon.dll \
      -r:System.Xml.dll -r:System.Xml.Linq.dll -r:System.Drawing.dll \
      FemDesignExport.cs
  mono FemDesignExport.exe )
