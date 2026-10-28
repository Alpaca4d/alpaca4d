#!/usr/bin/env bash
# Draws every Alpaca4d component in the NodePen style and puts the images on their docs pages.
#
#     ./run.sh                    # all components, into ../../../alpaca4d.github.io/doc
#     DOCS=/path/to/doc ./run.sh
#
# Needs a Release build of Alpaca4d.Gh, mono with libgdiplus, python3, node and npm, and the
# network (Playwright's Chromium on the first run, NodePen's fonts from Google Fonts every run).
# The component data is read from the built plugin with Grasshopper's own types, so the images
# always show the inputs, outputs, menus and icons the plugin really has.
set -euo pipefail
cd "$(dirname "$0")"

GHA=${GHA:-../../Alpaca4d.Gh/bin/Release/net48/Alpaca4d.Gh.gha}
DOCS=${DOCS:-../../../alpaca4d.github.io/doc}
GH=${GH:-$(ls -d ~/.nuget/packages/grasshopper/7.18.*/lib/net48 2>/dev/null | head -1)}
RHINO=${RHINO:-$(ls ~/.nuget/packages/rhinocommon/7.18.*/lib/net48/RhinoCommon.dll 2>/dev/null | head -1)}
# Grasshopper needs the Visual Basic runtime, which Homebrew's mono does not ship; Rhino 7 for Mac does.
VB=${VB:-$(ls "/Applications/Rhino 7.app/Contents/Frameworks/RhCore.framework/Versions/A/Frameworks/Mono64Rhino.framework/Versions/"*/Resources/lib/mono/gac/Microsoft.VisualBasic/10.0.0.0__b03f5f7f11d50a3a/Microsoft.VisualBasic.dll 2>/dev/null | head -1)}

[ -f "$GHA" ]   || { echo "build Alpaca4d.Gh in Release first, or set GHA=..."; exit 1; }
[ -d "$DOCS" ]  || { echo "set DOCS=/path/to/alpaca4d.github.io/doc"; exit 1; }
[ -d "$GH" ]    || { echo "set GH=/path/to/Grasshopper 7 lib/net48"; exit 1; }
[ -f "$RHINO" ] || { echo "set RHINO=/path/to/net48/RhinoCommon.dll"; exit 1; }
[ -f "$VB" ]    || { echo "set VB=/path/to/Microsoft.VisualBasic.dll (10.0.0.0, a runtime one, not a reference assembly)"; exit 1; }

work=$(mktemp -d); trap 'rm -rf "$work"' EXIT
cp "$(dirname "$GHA")"/*.dll "$GHA" "$GH"/*.dll "$RHINO" "$VB" "$work/"
cp Export.cs "$work/"
export DYLD_FALLBACK_LIBRARY_PATH=${DYLD_FALLBACK_LIBRARY_PATH:-/opt/homebrew/lib:/usr/local/lib}
# docs.py imports nodepen.py; no __pycache__ left next to them.
export PYTHONDONTWRITEBYTECODE=1

echo "Reading the components"
( cd "$work"
  mcs -nologo -r:Grasshopper.dll -r:GH_IO.dll -r:RhinoCommon.dll -r:System.Drawing.dll -r:System.Core.dll Export.cs
  mono Export.exe "$(basename "$GHA")" "$work/data" 2>&1 | grep -v "Carbon driver" )

echo "Drawing them"
python3 nodepen.py "$work/data/components.json" "$work/svg" > /dev/null

echo "Rendering"
npm install --silent --no-audit --no-fund --prefix "$work/node" playwright@1.48.2 > /dev/null
( cd "$work/node" && npx --yes playwright install chromium > /dev/null )
# Run from next to its node_modules: ES modules do not look anywhere else for playwright.
cp render.mjs "$work/node/"
rm -f "$DOCS/.gitbook/assets/components/"*.png
node "$work/node/render.mjs" "$work/svg" "$DOCS/.gitbook/assets/components"

echo "Placing them in the docs"
python3 docs.py "$work/data/components.json" "$DOCS"
