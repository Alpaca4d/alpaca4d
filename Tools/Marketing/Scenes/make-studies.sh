#!/usr/bin/env bash
# Writes every study of StressLinesStudies.py, or those named, into Studies/<name>.gh, with make.sh.
#
#     ./make-studies.sh                     # all of them
#     ./make-studies.sh groin-vault         # one
set -euo pipefail
cd "$(dirname "$0")"
export PYTHONDONTWRITEBYTECODE=1
mkdir -p Studies
studies=${*:-$(python3 -c 'import StressLinesShapes as s; print(" ".join(s.STUDIES))')}
for study in $studies; do
  STUDY=$study ../../Examples/make.sh StressLinesStudies.py "Studies/$study.gh" | grep -v "^$"
done
