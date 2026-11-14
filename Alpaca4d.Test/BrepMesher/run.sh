#!/usr/bin/env bash
# Mesh Brep's mesher, in a running Rhino 8 with its script server started (StartScriptServer).
# Needs a built Alpaca4d.Core. Exits non-zero if any check fails.
set -e
cd "$(dirname "$0")"
CORE=${CORE:-../../Alpaca.Core/bin/Debug/net48/Alpaca4d.Core.dll}
RHINOCODE=${RHINOCODE:-"/Applications/Rhino 8.app/Contents/Resources/bin/rhinocode"}
[ -f "$CORE" ] || { echo "build Alpaca.Core first, or set CORE=..."; exit 1; }

# A folder of its own each run: Rhino keeps the DLL it loaded mapped, and overwriting that breaks it.
work=$(mktemp -d)
cp "$CORE" "$work/Alpaca4d.Core.dll"
sed -e "s|__CORE__|$work/Alpaca4d.Core.dll|" -e "s|__OUT__|$work/result.txt|" BrepMesherCheck.cs > "$work/BrepMesherCheck.cs"

# rhinocode returns before the script has run, so wait for what it writes.
"$RHINOCODE" script "$work/BrepMesherCheck.cs"
for _ in $(seq 1 300); do [ -f "$work/result.txt" ] && break; sleep 1; done
[ -f "$work/result.txt" ] || { echo "no result: a compile error is in the newest ~/.rhinocode/logs/rhinocode_<Rhino's pid>"; exit 1; }
cat "$work/result.txt"
! grep -q "^FAIL" "$work/result.txt"
