#!/usr/bin/env bash
# Design explorer: the study file, the design space and the .glb writer. Needs a built
# Alpaca4d.Core, a C# compiler (csc or mcs) and a net48 RhinoCommon. With node on the path the
# .glb is also put through the Khronos glTF validator.
set -e
cd "$(dirname "$0")"

CORE=${CORE:-../../Alpaca.Core/bin/Release/net48/Alpaca4d.Core.dll}
RHINO=${RHINO:-$(ls ~/.nuget/packages/rhinocommon/7.18.*/lib/net48/RhinoCommon.dll 2>/dev/null | head -1)}
JSON=${JSON:-$(ls ~/.nuget/packages/newtonsoft.json/13.*/lib/net45/Newtonsoft.Json.dll 2>/dev/null | head -1)}

[ -f "$CORE" ]  || { echo "build Alpaca.Core first, or set CORE=..."; exit 1; }
[ -f "$RHINO" ] || { echo "set RHINO=/path/to/net48/RhinoCommon.dll"; exit 1; }
[ -f "$JSON" ]  || { echo "set JSON=/path/to/Newtonsoft.Json.dll"; exit 1; }

CSC=$(command -v csc || command -v mcs)
work=$(mktemp -d); trap 'rm -rf "$work"' EXIT
cp Explorer.cs "$work/"
cp "$CORE" "$RHINO" "$JSON" "$work/"

( cd "$work"
  "$CSC" -nologo -target:exe -out:Explorer.exe -r:Alpaca4d.Core.dll -r:RhinoCommon.dll Explorer.cs
  mono Explorer.exe "$work/box.glb" )

if command -v npm >/dev/null; then
  echo
  echo "glTF validator"
  npm install --silent --no-audit --no-fund --prefix "$work/node" gltf-validator@2.0.0-dev.3.10 >/dev/null
  NODE_PATH="$work/node/node_modules" node -e '
    const fs = require("fs"), path = require("path"), validator = require("gltf-validator");
    (async () => {
      let errors = 0;
      for (const file of process.argv.slice(1)) {
        const r = (await validator.validateBytes(new Uint8Array(fs.readFileSync(file)))).issues;
        errors += r.numErrors;
        console.log(`  ${path.basename(file)}: ${r.numErrors} errors, ${r.numWarnings} warnings, ${r.numInfos} infos`);
        for (const m of r.messages.filter(m => m.severity < 2)) console.log(`    ${m.code}: ${m.message} (${m.pointer || ""})`);
      }
      process.exit(errors > 0 ? 1 : 0);
    })();' "$work/box.glb" "$work/labels.glb"
fi
