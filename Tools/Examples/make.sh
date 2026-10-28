#!/usr/bin/env bash
# Writes the example definitions to .gh files straight from their scripts, without Rhino.
#
#     ./make.sh                                   # FootbridgeStudy.gh, next to its script
#     ./make.sh path/to/Script.py [out.gh]         # any script with a build(doc), next to it
#     ./make.sh ../../Alpaca4d.Test/Optimisation/NonLinearOptimisationCases.py
#
# The script builds its definition from the real Grasshopper and Alpaca4d components, loaded in
# mono with the IronPython that Rhino 7 ships; WriteGh.py saves it as Grasshopper saves a file.
# Needs a Release build of Alpaca4d.Gh, mono, Rhino 7 for Mac (for IronPython, the Visual Basic
# runtime and Grasshopper's component libraries) and Grasshopper and RhinoCommon 7.18 from the
# NuGet cache, which the build has already restored.
set -euo pipefail
# The script and the output, from where make.sh was called.
SCRIPT=${1:-$(dirname "$0")/FootbridgeStudy.py}
[ -f "$SCRIPT" ] || { echo "no script $SCRIPT"; exit 1; }
script_dir=$(cd "$(dirname "$SCRIPT")" && pwd)
module=$(basename "$SCRIPT" .py)
OUT=${2:-$script_dir/$module.gh}
case $OUT in /*) ;; *) OUT=$(pwd)/$OUT ;; esac
cd "$(dirname "$0")"
GHA=${GHA:-../../Alpaca4d.Gh/bin/Release/net48/Alpaca4d.Gh.gha}
GH=${GH:-$(ls -d ~/.nuget/packages/grasshopper/7.18.*/lib/net48 2>/dev/null | head -1)}
RHINO=${RHINO:-$(ls ~/.nuget/packages/rhinocommon/7.18.*/lib/net48/RhinoCommon.dll 2>/dev/null | head -1)}
R7=${R7:-"/Applications/Rhino 7.app/Contents/Frameworks/RhCore.framework/Versions/A"}
PY="$R7/Resources/ManagedPlugIns/RhinoDLR_Python.rhp"
COMPONENTS="$R7/Resources/ManagedPlugIns/GrasshopperPlugin.rhp/Components"
VB=${VB:-$(ls "$R7/Frameworks/Mono64Rhino.framework/Versions/"*/Resources/lib/mono/gac/Microsoft.VisualBasic/10.0.0.0__b03f5f7f11d50a3a/Microsoft.VisualBasic.dll 2>/dev/null | head -1)}

[ -f "$GHA" ]              || { echo "build Alpaca4d.Gh in Release first, or set GHA=..."; exit 1; }
[ -d "$GH" ]               || { echo "set GH=/path/to/Grasshopper 7 lib/net48"; exit 1; }
[ -f "$RHINO" ]            || { echo "set RHINO=/path/to/net48/RhinoCommon.dll"; exit 1; }
[ -f "$PY/IronPython.dll" ] || { echo "IronPython not found: set R7=/path/to/Rhino 7.app/Contents/Frameworks/RhCore.framework/Versions/A"; exit 1; }
[ -f "$VB" ]               || { echo "set VB=/path/to/Microsoft.VisualBasic.dll (10.0.0.0, a runtime one)"; exit 1; }

work=$(mktemp -d); trap 'rm -rf "$work"' EXIT
cp "$(dirname "$GHA")"/*.dll "$GHA" "$GH"/*.dll "$RHINO" "$VB" "$work/"
cp "$PY"/IronPython.dll "$PY"/IronPython.Modules.dll "$PY"/Microsoft.Dynamic.dll "$PY"/Microsoft.Scripting.dll "$work/"
# IronPython reads every type of an assembly it loads, so Grasshopper's dependencies must resolve too.
cp "$R7"/Resources/Mono.Cecil*.dll "$R7"/Resources/Newtonsoft.Json.Rhino.dll "$R7"/Resources/Rhino.UI.dll \
   "$R7"/Resources/Yak.Core.dll "$R7"/Resources/Eto.dll "$R7"/Resources/ManagedPlugIns/GrasshopperPlugin.rhp/GH_Util.dll "$work/"
for gha in CurveComponents MathComponents VectorComponents XformComponents GalapagosComponents SurfaceComponents; do cp "$COMPONENTS/$gha.gha" "$work/"; done
cp GhHost.cs WriteGh.py "$work/"
export DYLD_FALLBACK_LIBRARY_PATH=${DYLD_FALLBACK_LIBRARY_PATH:-/opt/homebrew/lib:/usr/local/lib}

( cd "$work"
  mcs -nologo -r:IronPython.dll -r:Microsoft.Scripting.dll -r:Microsoft.Dynamic.dll -r:System.Drawing.dll GhHost.cs
  mono GhHost.exe WriteGh.py "$script_dir" "$module" "$OUT" \
    "$work/CurveComponents.gha" "$work/MathComponents.gha" "$work/VectorComponents.gha" \
    "$work/XformComponents.gha" "$work/GalapagosComponents.gha" "$work/SurfaceComponents.gha" "$work/Grasshopper.dll" \
    "$work/$(basename "$GHA")" 2>&1 | grep -v "Carbon driver" )
