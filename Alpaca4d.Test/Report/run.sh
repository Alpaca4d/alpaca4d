#!/usr/bin/env bash
# The report's template, tables and page layout, without Rhino: the figures are drawn by a stand-in.
# Needs a built Alpaca4d.Core, a C# compiler (mcs) and a net48 RhinoCommon.
set -e
cd "$(dirname "$0")"
COREDIR=${COREDIR:-../../Alpaca.Core/bin/Release/net48}
CORE="$COREDIR/Alpaca4d.Core.dll"
RHINO=${RHINO:-$(ls ~/.nuget/packages/rhinocommon/7.18.*/lib/net48/RhinoCommon.dll 2>/dev/null | head -1)}
[ -f "$CORE" ]  || { echo "build Alpaca.Core first, or set COREDIR=..."; exit 1; }
[ -f "$RHINO" ] || { echo "set RHINO=/path/to/net48/RhinoCommon.dll"; exit 1; }

work=$(mktemp -d); trap 'rm -rf "$work"' EXIT
cp ReportCheck.cs "$work/"
cp "$COREDIR"/*.dll "$RHINO" "$work/"
( cd "$work"
  NETSTD=${NETSTD:-$(ls /Library/Frameworks/Mono.framework/Versions/Current/lib/mono/4.5/Facades/netstandard.dll 2>/dev/null | head -1)}
  NETSTD_REF=""
  [ -n "$NETSTD" ] && NETSTD_REF="-r:$NETSTD"
  mcs -nologo -target:exe -out:ReportCheck.exe -r:Alpaca4d.Core.dll -r:RhinoCommon.dll -r:Newtonsoft.Json.dll -r:System.Drawing.dll $NETSTD_REF ReportCheck.cs
  mono ReportCheck.exe )
