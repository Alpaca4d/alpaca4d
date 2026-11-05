#!/usr/bin/env bash
# The speed test: the three topology methods timed on the Topology Bridge block, the beam-column joint and the
# truss node at several sizes, one run at a time, each with an OpenSees of its own; then Multi-Material with steel
# alone, to tell the cost of the second material from that of MMA and its projection. About an hour and a half on
# an M2 Pro. speed.py then prints the tables. Same needs as run.sh; RESULTS= as there. The summary starts afresh.
set -eo pipefail
cd "$(dirname "$0")"
export RESULTS=${RESULTS:-$(pwd)/results}
mkdir -p "$RESULTS"
rm -f "$RESULTS/speed_summary.csv"
for args in "bridge 2,3,4,5" "joint 10,20" "truss 10,20,30" "bridge 3 multi1" "truss 20 multi1"; do
  echo "== speed $args, load average $(sysctl -n vm.loadavg 2>/dev/null || uptime)"
  ./run.sh bench speed $args 2>&1 | grep -v "warning CS\|Compilation succeeded"
done
python3 speed.py "$RESULTS/speed_summary.csv"
