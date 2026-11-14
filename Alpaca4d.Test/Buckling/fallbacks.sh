#!/usr/bin/env bash
# The ways a deck printed sparse can be solved, on an OpenSees built with Mumps (see Tools/OpenSees):
# run.sh on it as it is; told it has no Mumps, as an official OpenSees newer than 3.8.0 answers;
# and with its first, then its second, Mumps solve made to fail - the unloaded and the loaded
# step - which SuperLU must then solve again.
#   ./fallbacks.sh [/path/to/OpenSees-with-Mumps]
set -e
cd "$(dirname "$0")"
REAL=${1:-/Applications/OpenSeesLatest-Mumps/bin/OpenSees}
[ -x "$REAL" ] || { echo "no OpenSees at $REAL"; exit 1; }
work=$(mktemp -d); trap 'rm -rf "$work"' EXIT

# An OpenSees that runs the Tcl in $work/<name>.tcl before the deck it is given.
wrap() {
    printf '%s\n' "$2" > "$work/$1.tcl"
    cat > "$work/$1" <<EOF
#!/usr/bin/env bash
d="\$1"; w="\${d%.tcl}_$1.tcl"
{ cat "$work/$1.tcl"; printf 'source {%s}\n' "\$d"; } > "\$w"
exec "$REAL" "\$w"
EOF
    chmod +x "$work/$1"
}

wrap no-mumps 'rename system alpaca4dRealSystem
proc system {args} {
    if {[lindex $args 0] eq "Mumps"} { error "WARNING system Mumps is unknown or not installed" }
    uplevel 1 [list alpaca4dRealSystem {*}$args]
}'

fails='rename system alpaca4dRealSystem
set alpaca4dOnMumps 0
set alpaca4dMumpsSolves 0
proc system {args} {
    global alpaca4dOnMumps
    set alpaca4dOnMumps [expr {[lindex $args 0] eq "Mumps"}]
    uplevel 1 [list alpaca4dRealSystem {*}$args]
}
rename analyze alpaca4dRealAnalyze
proc analyze {args} {
    global alpaca4dOnMumps alpaca4dMumpsSolves
    if {$alpaca4dOnMumps && [incr alpaca4dMumpsSolves] == FAILING} { return -3 }
    uplevel 1 [list alpaca4dRealAnalyze {*}$args]
}'
wrap mumps-fails-1 "${fails/FAILING/1}"
wrap mumps-fails-2 "${fails/FAILING/2}"

OPENSEES="$REAL $work/no-mumps $work/mumps-fails-1 $work/mumps-fails-2" ./run.sh
