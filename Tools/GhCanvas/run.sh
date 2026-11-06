#!/usr/bin/env bash
# Reads the examples' Grasshopper definitions for the website's interactive previews: beside each
# examples/<slug>/<slug>.gh of the website repository, <slug>-definition.json.
#
#     ./run.sh                          # every example
#     ./run.sh path/to/a.gh ...         # some definitions
#     DOCS=/path/to/alpaca4d.github.io ./run.sh
#
# Needs mono with libgdiplus, a Release build of Alpaca4d.Gh (for its icons and the names its
# components have now), Grasshopper and RhinoCommon 7.18 from the NuGet cache, and Rhino 7 for Mac
# for Grasshopper's own components' icons. Run it again whenever an example's .gh changes.
set -euo pipefail
here=$(cd "$(dirname "$0")" && pwd)
DOCS=${DOCS:-$here/../../../alpaca4d.github.io}
GHA=${GHA:-$here/../../Alpaca4d.Gh/bin/Release/net48/Alpaca4d.Gh.gha}
GH=${GH:-$(ls -d ~/.nuget/packages/grasshopper/7.18.*/lib/net48 2>/dev/null | head -1)}
RHINO=${RHINO:-$(ls ~/.nuget/packages/rhinocommon/7.18.*/lib/net48/RhinoCommon.dll 2>/dev/null | head -1)}
ETO=${ETO:-$(dirname "$RHINO")/Eto.dll}
R7=${R7:-"/Applications/Rhino 7.app/Contents/Frameworks/RhCore.framework/Versions/A"}
COMPONENTS="$R7/Resources/ManagedPlugIns/GrasshopperPlugin.rhp/Components"
VB=${VB:-$(ls "$R7/Frameworks/Mono64Rhino.framework/Versions/"*/Resources/lib/mono/gac/Microsoft.VisualBasic/10.0.0.0__b03f5f7f11d50a3a/Microsoft.VisualBasic.dll 2>/dev/null | head -1)}

[ -f "$GHA" ]   || { echo "build Alpaca4d.Gh in Release first, or set GHA=..."; exit 1; }
[ -d "$GH" ]    || { echo "set GH=/path/to/Grasshopper 7 lib/net48"; exit 1; }
[ -f "$RHINO" ] || { echo "set RHINO=/path/to/net48/RhinoCommon.dll"; exit 1; }
[ -d "$DOCS/examples" ] || { echo "set DOCS=/path/to/alpaca4d.github.io"; exit 1; }

if [ $# -gt 0 ]; then files=("$@"); else files=("$DOCS"/examples/*/*.gh); fi
abs=(); for f in "${files[@]}"; do abs+=("$(cd "$(dirname "$f")" && pwd)/$(basename "$f")"); done

work=$(mktemp -d); trap 'rm -rf "$work"' EXIT
cp "$(dirname "$GHA")"/*.dll "$GHA" "$GH"/*.dll "$RHINO" "$work/"
[ -f "$ETO" ] && cp "$ETO" "$work/"
[ -n "$VB" ] && [ -f "$VB" ] && cp "$VB" "$work/"
# Grasshopper's own components, for their icons; Grasshopper.dll has the sliders, panels and the rest.
icons=("$work/$(basename "$GHA")" "$work/Grasshopper.dll")
if [ -d "$COMPONENTS" ]; then
  for gha in "$COMPONENTS"/*.gha; do cp "$gha" "$work/"; icons+=("$work/$(basename "$gha")"); done
  cp "$COMPONENTS"/*.dll "$work/" 2>/dev/null || true
fi
cp "$here/Read.cs" "$work/"
export DYLD_FALLBACK_LIBRARY_PATH=${DYLD_FALLBACK_LIBRARY_PATH:-/opt/homebrew/lib:/usr/local/lib}
( cd "$work"
  mcs -nologo -r:Grasshopper.dll -r:GH_IO.dll -r:RhinoCommon.dll -r:System.Drawing.dll -r:System.Core.dll Read.cs
  mono Read.exe "${icons[@]}" -- "${abs[@]}" 2>&1 | grep -v "Carbon driver" )
