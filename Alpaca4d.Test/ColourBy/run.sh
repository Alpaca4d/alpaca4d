#!/usr/bin/env bash
# Colour by checks: which colour Model View draws each element in by section and by material, and
# that the Section Library, Material Library and Model View open from files saved before their
# Colour input and Colour by dropdown, and keep them through a save.
#
# Builds the real components in mono, as Tools/Examples/make.sh does to write the examples, so it
# needs what that needs: a Release build of Alpaca4d.Gh, mono, Grasshopper and RhinoCommon 7.18 from
# the NuGet cache and Rhino 7 for Mac for the Visual Basic runtime and the libraries Grasshopper
# loads. The old file is Tools/Examples/SteelCantilever.gh.
set -e
cd "$(dirname "$0")"
GHA=${GHA:-../../Alpaca4d.Gh/bin/Release/net48/Alpaca4d.Gh.gha}
GH=${GH:-$(ls -d ~/.nuget/packages/grasshopper/7.18.*/lib/net48 2>/dev/null | head -1)}
RHINO=${RHINO:-$(ls ~/.nuget/packages/rhinocommon/7.18.*/lib/net48/RhinoCommon.dll 2>/dev/null | head -1)}
R7=${R7:-"/Applications/Rhino 7.app/Contents/Frameworks/RhCore.framework/Versions/A"}
VB=${VB:-$(ls "$R7/Frameworks/Mono64Rhino.framework/Versions/"*/Resources/lib/mono/gac/Microsoft.VisualBasic/10.0.0.0__b03f5f7f11d50a3a/Microsoft.VisualBasic.dll 2>/dev/null | head -1)}
EXAMPLE=${EXAMPLE:-$(cd ../../Tools/Examples && pwd)/SteelCantilever.gh}
# Definitions saved before the Colour inputs, whose section and material components are opened as
# well: the examples here, and the docs' own when the website repository sits next to this one.
DOCS=${DOCS:-../../../alpaca4d.github.io/doc}
OLD=("$EXAMPLE" "$(cd ../../Tools/Examples && pwd)/MixedModelSpeed.gh")
for gh in assets/beam.gh examples/earthquake/earthquake.gh examples/connection-design/connection-design.gh; do
  [ -f "$DOCS/$gh" ] && OLD+=("$(cd "$(dirname "$DOCS/$gh")" && pwd)/$(basename "$gh")")
done

[ -f "$GHA" ]     || { echo "build Alpaca4d.Gh in Release first, or set GHA=..."; exit 1; }
[ -d "$GH" ]      || { echo "set GH=/path/to/Grasshopper 7 lib/net48"; exit 1; }
[ -f "$RHINO" ]   || { echo "set RHINO=/path/to/net48/RhinoCommon.dll"; exit 1; }
[ -f "$VB" ]      || { echo "set VB=/path/to/Microsoft.VisualBasic.dll (10.0.0.0, a runtime one)"; exit 1; }
[ -f "$EXAMPLE" ] || { echo "set EXAMPLE=/path/to/a .gh with a Section Library, a Material Library and a Model View"; exit 1; }

work=$(mktemp -d); trap 'rm -rf "$work"' EXIT
cp ColourBy.cs "$work/"
cp "$(dirname "$GHA")"/*.dll "$GH"/*.dll "$RHINO" "$VB" "$work/"
# Under the name mono looks for it by.
cp "$GHA" "$work/Alpaca4d.Gh.dll"
cp "$R7"/Resources/Newtonsoft.Json.Rhino.dll "$R7"/Resources/Rhino.UI.dll "$R7"/Resources/Eto.dll \
   "$R7"/Resources/ManagedPlugIns/GrasshopperPlugin.rhp/GH_Util.dll "$work/"
# Grasshopper draws its parameters with GDI+, even off screen.
export DYLD_FALLBACK_LIBRARY_PATH=${DYLD_FALLBACK_LIBRARY_PATH:-/opt/homebrew/lib:/usr/local/lib}

( cd "$work"
  mcs -nologo -target:exe -out:ColourBy.exe -r:Alpaca4d.Core.dll -r:Alpaca4d.Gh.dll -r:Grasshopper.dll -r:GH_IO.dll \
      -r:RhinoCommon.dll -r:System.Drawing.dll -r:System.Windows.Forms.dll ColourBy.cs
  mono ColourBy.exe "$EXAMPLE" "${OLD[@]}" 2>&1 | grep -v "Carbon driver" )
