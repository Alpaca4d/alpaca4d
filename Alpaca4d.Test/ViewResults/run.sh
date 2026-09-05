#!/usr/bin/env bash
# View Results dropdown-mapping checks.
#
# The component turns a pair of dropdown indices into a call on one of the result readers, and
# every reader returns its quantities in an order of its own. This pins that mapping against the
# components that were already drawing the same quantities.
#
# It reads the built .gha rather than the source, because ResultField is internal to it. Needs a
# built Alpaca4d.Gh, a C# compiler and a net48 RhinoCommon.
set -e
cd "$(dirname "$0")"
GHA=${GHA:-../../Alpaca4d.Gh/bin/Release/net48/Alpaca4d.Gh.gha}
RHINO=${RHINO:-$(ls ~/.nuget/packages/rhinocommon/7.18.*/lib/net48/RhinoCommon.dll 2>/dev/null | head -1)}
[ -f "$GHA" ]   || { echo "build Alpaca4d.Gh first, or set GHA=..."; exit 1; }
[ -f "$RHINO" ] || { echo "set RHINO=/path/to/net48/RhinoCommon.dll"; exit 1; }

work=$(mktemp -d); trap 'rm -rf "$work"' EXIT
cp ViewResults.cs "$work/"
# The whole build output, because the assembly resolves Grasshopper and Alpaca4d.Core at load.
cp "$(dirname "$GHA")"/*.dll "$(dirname "$GHA")"/*.gha "$work/" 2>/dev/null || true
cp "$RHINO" "$work/" 2>/dev/null || true

( cd "$work"
  mcs -target:exe -out:ViewResults.exe -r:RhinoCommon.dll ViewResults.cs
  mono ViewResults.exe Alpaca4d.Gh.gha )
