#!/usr/bin/env bash
# View Results files: that a definition saved before σx was offered among the beam stresses opens
# on the stress it was saved showing, and that one saved since keeps its choice.
#
# Builds the real component in mono, as Tools/Examples/make.sh does, so it needs what that needs:
# a Release build of Alpaca4d.Gh, mono, Grasshopper and RhinoCommon 7.18 from the NuGet cache and
# Rhino 7 for Mac for the Visual Basic runtime and the libraries Grasshopper loads.
set -e
cd "$(dirname "$0")"
GHA=${GHA:-../../Alpaca4d.Gh/bin/Release/net48/Alpaca4d.Gh.gha}
GH=${GH:-$(ls -d ~/.nuget/packages/grasshopper/7.18.*/lib/net48 2>/dev/null | head -1)}
RHINO=${RHINO:-$(ls ~/.nuget/packages/rhinocommon/7.18.*/lib/net48/RhinoCommon.dll 2>/dev/null | head -1)}
R7=${R7:-"/Applications/Rhino 7.app/Contents/Frameworks/RhCore.framework/Versions/A"}
VB=${VB:-$(ls "$R7/Frameworks/Mono64Rhino.framework/Versions/"*/Resources/lib/mono/gac/Microsoft.VisualBasic/10.0.0.0__b03f5f7f11d50a3a/Microsoft.VisualBasic.dll 2>/dev/null | head -1)}

[ -f "$GHA" ]   || { echo "build Alpaca4d.Gh in Release first, or set GHA=..."; exit 1; }
[ -d "$GH" ]    || { echo "set GH=/path/to/Grasshopper 7 lib/net48"; exit 1; }
[ -f "$RHINO" ] || { echo "set RHINO=/path/to/net48/RhinoCommon.dll"; exit 1; }
[ -f "$VB" ]    || { echo "set VB=/path/to/Microsoft.VisualBasic.dll (10.0.0.0, a runtime one)"; exit 1; }

work=$(mktemp -d); trap 'rm -rf "$work"' EXIT
cp ViewResultsFiles.cs "$work/"
cp "$(dirname "$GHA")"/*.dll "$GH"/*.dll "$RHINO" "$VB" "$work/"
# Under the name mono looks for it by.
cp "$GHA" "$work/Alpaca4d.Gh.dll"
cp "$R7"/Resources/Newtonsoft.Json.Rhino.dll "$R7"/Resources/Rhino.UI.dll "$R7"/Resources/Eto.dll \
   "$R7"/Resources/ManagedPlugIns/GrasshopperPlugin.rhp/GH_Util.dll "$work/"
# Grasshopper draws its parameters with GDI+, even off screen.
export DYLD_FALLBACK_LIBRARY_PATH=${DYLD_FALLBACK_LIBRARY_PATH:-/opt/homebrew/lib:/usr/local/lib}

( cd "$work"
  mcs -nologo -target:exe -out:ViewResultsFiles.exe -r:Alpaca4d.Core.dll -r:Alpaca4d.Gh.dll -r:Grasshopper.dll -r:GH_IO.dll \
      -r:RhinoCommon.dll -r:System.Drawing.dll -r:System.Windows.Forms.dll ViewResultsFiles.cs
  mono ViewResultsFiles.exe 2>&1 | grep -v "Carbon driver" )
