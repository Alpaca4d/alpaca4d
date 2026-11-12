#!/usr/bin/env bash
# Footfall Analysis through Grasshopper, in a running Rhino 8 with its script server started
# (StartScriptServer). GrasshopperChecks.cs opens the stair examples of Tools/Examples, solves them,
# adds Full excitation beside them and saves and opens them again.
#
# It uses the Alpaca4d Grasshopper loads, so build Alpaca4d.Gh into the live Grasshopper folder first,
# with Rhino closed. Natural Vibration solves with the OpenSees of the user's settings.json.
set -e
cd "$(dirname "$0")"
GH=${GH:-$(ls -d ~/.nuget/packages/grasshopper/7.18.*/lib/net48 2>/dev/null | head -1)}
RHINO=${RHINO:-$(ls ~/.nuget/packages/rhinocommon/7.18.*/lib/net48/RhinoCommon.dll 2>/dev/null | head -1)}
RHINOCODE=${RHINOCODE:-"/Applications/Rhino 8.app/Contents/Resources/bin/rhinocode"}
[ -d "$GH" ] || { echo "set GH=/path/to/Grasshopper 7 lib/net48"; exit 1; }
[ -f "$RHINO" ] || { echo "set RHINO=/path/to/net48/RhinoCommon.dll"; exit 1; }

# A folder of its own each run: Rhino keeps what it loaded mapped, and Natural Vibration writes beside
# each definition.
work=$(mktemp -d)
cp ../../Tools/Examples/Structures/FootfallStair.gh ../../Tools/Examples/Structures/FootfallHelicalStair.gh ../../Tools/Examples/Structures/FootfallStraightStair.gh "$work/"
cp GrasshopperChecks.cs "$work/"
( cd "$work"
  cp "$GH"/Grasshopper.dll "$GH"/GH_IO.dll "$RHINO" .
  NETSTD=${NETSTD:-$(ls /Library/Frameworks/Mono.framework/Versions/Current/lib/mono/4.5/Facades/netstandard.dll /opt/homebrew/lib/mono/4.5/Facades/netstandard.dll 2>/dev/null | head -1)}
  NETSTD_REF=""
  [ -n "$NETSTD" ] && NETSTD_REF="-r:$NETSTD"
  csc -nologo -langversion:latest -target:library -out:FootfallGrasshopperCheck.dll \
      -r:Grasshopper.dll -r:GH_IO.dll -r:RhinoCommon.dll -r:System.Drawing.dll -r:System.Windows.Forms.dll $NETSTD_REF GrasshopperChecks.cs
  rm Grasshopper.dll GH_IO.dll RhinoCommon.dll )
sed -e "s|__WORK__|$work|" GrasshopperRun.cs > "$work/GrasshopperRun.cs"

# rhinocode returns before the script has run, so wait for what it writes.
PIPE=${PIPE:+--rhino $PIPE}
"$RHINOCODE" $PIPE script "$work/GrasshopperRun.cs"
for _ in $(seq 1 900); do [ -f "$work/result.txt" ] && break; sleep 1; done
[ -f "$work/result.txt" ] || { echo "no result: a compile error is in the newest ~/.rhinocode/logs/rhinocode_<Rhino's pid>"; exit 1; }
cat "$work/result.txt"
if grep -q "\[FAIL\]" "$work/result.txt"; then exit 1; fi
