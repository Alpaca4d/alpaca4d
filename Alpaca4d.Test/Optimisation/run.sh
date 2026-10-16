#!/usr/bin/env bash
# The optimisation algorithms, NLopt's derivative-free ones ported to C#. Needs a C# compiler
# (csc or mcs) and mono; the sources are compiled in, so Alpaca.Core need not be built.
#
# Set NLOPT to an NLopt shared library to compare the port with NLopt point by point; see
# README.md for how to build one that can be compared to the last bit.
#
#   ./run.sh              every algorithm
#   ./run.sh Cobyla       one, by its name in OptimisationAlgorithm
set -e
cd "$(dirname "$0")"

CSC=$(command -v csc || command -v mcs)
work=$(mktemp -d); trap 'rm -rf "$work"' EXIT

"$CSC" -nologo -target:exe -optimize+ -out:"$work/Optimisation.exe" \
  Optimisation.cs Reference.cs $(find ../../Alpaca.Core/Optimisation -name '*.cs')
mono "$work/Optimisation.exe" "$@"
