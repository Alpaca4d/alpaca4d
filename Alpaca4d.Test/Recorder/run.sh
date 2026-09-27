#!/usr/bin/env bash
# MPCO recorder checks: the lines it writes, the tick boxes it saves, and what the solver records.
# Needs a built Alpaca4d.Core, a C# compiler and a net48 RhinoCommon. OpenSees on PATH is
# optional: with it the deck here is solved with every result ticked and the file is checked,
# without it only the recorder lines and the tick-box order are.
set -e
cd "$(dirname "$0")"
COREDIR=${COREDIR:-../../Alpaca.Core/bin/Release/net48}
CORE="$COREDIR/Alpaca4d.Core.dll"
RHINO=${RHINO:-$(ls ~/.nuget/packages/rhinocommon/7.18.*/lib/net48/RhinoCommon.dll 2>/dev/null | head -1)}
[ -f "$CORE" ]  || { echo "build Alpaca.Core first, or set COREDIR=..."; exit 1; }
[ -f "$RHINO" ] || { echo "set RHINO=/path/to/net48/RhinoCommon.dll"; exit 1; }

work=$(mktemp -d); trap 'rm -rf "$work"' EXIT
cp Recorder.cs model.tcl analysis.tcl "$work/"
# PureHDF and its dependencies come from the build output; the reader needs them at run time.
cp "$COREDIR"/*.dll "$work/" 2>/dev/null || true
cp "$RHINO" "$work/"

( cd "$work"
  # PureHDF is a netstandard2.0 assembly, so mcs needs the facade to resolve its types.
  NETSTD=${NETSTD:-$(ls /Library/Frameworks/Mono.framework/Versions/Current/lib/mono/4.5/Facades/netstandard.dll 2>/dev/null | head -1)}
  NETSTD_REF=""
  [ -n "$NETSTD" ] && NETSTD_REF="-r:$NETSTD"
  mcs -target:exe -out:Recorder.exe -r:Alpaca4d.Core.dll -r:RhinoCommon.dll -r:PureHDF.dll $NETSTD_REF Recorder.cs
  mono Recorder.exe write

  OPENSEES=${OPENSEES:-$(command -v OpenSees || echo /Applications/OpenSees3.5.0/bin/OpenSees)}
  if [ -x "$OPENSEES" ]; then
    # A nodal name MPCO does not know stops the deck with an error, so the exit code is itself
    # the check that every one on the tick boxes is a name it knows.
    if ! out=$("$OPENSEES" all.tcl 2>&1); then
      echo "$out" | tail -20
      echo "  [FAIL] all.tcl did not solve"
      exit 1
    fi
  else
    echo "note: OpenSees not found, the solver checks will be skipped"
  fi

  mono Recorder.exe read )
