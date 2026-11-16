#!/usr/bin/env bash
# The Report's templates through the real components, in a running Rhino 8 with its script server
# started (StartScriptServer): TemplateChecks.cs places them from the Templates menu, undoes and redoes
# them and writes the PDF. Needs a Release build of Alpaca4d.Gh (built with
# -p:GrasshopperLibrariesPath=<elsewhere> while Rhino runs), a C# compiler and OpenSees. The pictures
# borrow Rhino's active view: it opens an empty document if none is open.
set -e
cd "$(dirname "$0")"
GHDIR=${GHDIR:-../../Alpaca4d.Gh/bin/Release/net48}
GH=${GH:-$(ls -d ~/.nuget/packages/grasshopper/7.18.*/lib/net48 2>/dev/null | head -1)}
RHINO=${RHINO:-$(ls ~/.nuget/packages/rhinocommon/7.18.*/lib/net48/RhinoCommon.dll 2>/dev/null | head -1)}
RHINOCODE=${RHINOCODE:-"/Applications/Rhino 8.app/Contents/Resources/bin/rhinocode"}
OPENSEES=${OPENSEES:-$(command -v OpenSees || echo /Applications/OpenSees3.8.0/bin/OpenSees)}
LICENCE=${LICENCE:-../../Alpaca4d.Utils/data_august_28.bin}
# The Rhino to run in: the first with a script server, or PIPE=rhinocode_remotepipe_<pid>.
PIPE=${PIPE:-$("$RHINOCODE" list 2>/dev/null | awk '/rhinocode_remotepipe_/ { print $2; exit }')}
[ -f "$GHDIR/Alpaca4d.Gh.gha" ] || { echo "build Alpaca4d.Gh in Release first, or set GHDIR=..."; exit 1; }
[ -n "$PIPE" ] || { echo "no Rhino with its script server: run StartScriptServer in Rhino 8"; exit 1; }
[ -x "$OPENSEES" ] || { echo "set OPENSEES=/path/to/OpenSees"; exit 1; }

# A folder of its own each run: Rhino keeps what it loaded mapped.
work=$(mktemp -d)
cp "$GHDIR"/*.dll "$GHDIR"/*.gha "$work/"
cp "$GHDIR"/*.json "$work/" 2>/dev/null || true
[ -f "$LICENCE" ] && cp "$LICENCE" "$work/licence.bin"
printf '{ "OpenSeesPath": "%s" }\n' "$OPENSEES" > "$work/settings.json"
( cd "$work"
  cp "$GH"/Grasshopper.dll "$GH"/GH_IO.dll "$RHINO" .
  NETSTD=${NETSTD:-$(ls /Library/Frameworks/Mono.framework/Versions/Current/lib/mono/4.5/Facades/netstandard.dll /opt/homebrew/lib/mono/4.5/Facades/netstandard.dll 2>/dev/null | head -1)}
  csc -nologo -langversion:latest -target:library -out:TemplateCheck.dll \
      -r:Grasshopper.dll -r:GH_IO.dll -r:RhinoCommon.dll -r:Alpaca4d.Gh.gha -r:Alpaca4d.Core.dll \
      -r:System.Drawing.dll -r:System.Windows.Forms.dll ${NETSTD:+-r:$NETSTD} "$OLDPWD/TemplateChecks.cs"
  rm Grasshopper.dll GH_IO.dll RhinoCommon.dll )
sed -e "s|__WORK__|$work|" TemplatesRun.cs > "$work/TemplatesRun.cs"

# rhinocode returns before the script has run, so wait for what it writes. The screen saver stops
# the pictures: keep the display awake meanwhile.
"$RHINOCODE" --rhino "$PIPE" script "$work/TemplatesRun.cs"
caffeinate -d -w $$ &
for _ in $(seq 1 400); do [ -f "$work/result.txt" ] && break; sleep 1; done
[ -f "$work/result.txt" ] || { echo "no result in $work: a compile error is in the newest ~/.rhinocode/logs/rhinocode_<Rhino's pid>"; exit 1; }
cat "$work/result.txt"
echo "The PDF and the definition: $work"
grep -q "all template checks passed" "$work/result.txt"
