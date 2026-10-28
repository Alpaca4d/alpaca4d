#!/usr/bin/env bash
# Builds OpenSees 3.8.0 with `system Mumps` (multi-core) for macOS on Apple silicon, the way the
# Multi-core OpenSees workflow does, into <out>/bin/OpenSees and <out>/lib/tcl8.6/init.tcl.
#
#   Tools/OpenSees/build-mac.sh [work dir] [out dir]
#
# OPENSEES_REF picks another tag, branch or commit of OpenSees to patch, master for one:
#   OPENSEES_REF=d0ac447eb93cbb9f8c8f63e75261dcc10e5c0b61 Tools/OpenSees/build-mac.sh ~/opensees-mumps-master
# (a commit by its full hash: GitHub fetches no shortened one)
#
# Needs Homebrew's gcc (for gfortran), cmake, git, curl and python3. Everything else - conan,
# its packages, the sources - goes in the work dir (default ~/opensees-mumps), not in ~/.conan2.
set -euo pipefail

here=$(cd "$(dirname "$0")" && pwd)
work=${1:-$HOME/opensees-mumps}
out=${2:-$work/dist}

OPENSEES_REF=${OPENSEES_REF:-v3.8.0}
MUMPS_CMAKE_COMMIT=ec5f340acabb29dc71c6ffde93b58bdcd04af083
MUMPS_URL=https://mumps-solver.org/MUMPS_5.5.1.tar.gz
MUMPS_SHA256=1abff294fa47ee4cfd50dfd5c595942b72ebfcedce08142a75a99ab35014fa15

export MACOSX_DEPLOYMENT_TARGET=13.0      # the OpenSees Alpaca4d has used runs from macOS 13
export CMAKE_POLICY_VERSION_MINIMUM=3.5
export CONAN_HOME="$work/conan"
mkdir -p "$work/bin" "$out/bin" "$out/lib/tcl8.6"

# OpenSees' CMake asks `gfortran` for its static runtime, so the newest one goes on PATH by that name.
GCCV=$(ls "$(brew --prefix)/bin" | sed -n 's/^gfortran-\([0-9][0-9]*\)$/\1/p' | sort -n | tail -1)
[ -n "$GCCV" ] || { echo "no gfortran: brew install gcc"; exit 1; }
ln -sf "$(brew --prefix)/bin/gfortran-$GCCV" "$work/bin/gfortran"
export PATH="$work/bin:$PATH"

[ -x "$work/venv/bin/conan" ] || { python3 -m venv "$work/venv"; "$work/venv/bin/pip" install -q conan; }
conan="$work/venv/bin/conan"
"$conan" profile detect --force > /dev/null

# Some Command Line Tools installs leave a stale usr/include/c++/v1 that clang searches before the
# SDK's libc++, and then not even <string> is found. Point the C++ builds at the SDK's own copy.
profile="$work/conan-profile"
{ echo "include(default)"; echo "[conf]"; } > "$profile"
if ! printf '#include <string>\n' | clang++ -x c++ -std=c++17 -fsyntax-only - 2>/dev/null; then
  sdk=$(xcrun --show-sdk-path)
  echo "tools.build:cxxflags=[\"-nostdinc++\", \"-isystem\", \"$sdk/usr/include/c++/v1\"]" >> "$profile"
  echo "Command Line Tools libc++ headers are stale; using the SDK's"
fi

# --- OpenSees, patched -------------------------------------------------------------------
if [ ! -d "$work/opensees" ]; then
  # Fetched rather than cloned, so that a commit will do as well as a tag or a branch.
  git init -q "$work/opensees"
  git -C "$work/opensees" fetch -q --depth 1 https://github.com/OpenSees/OpenSees.git "$OPENSEES_REF"
  git -C "$work/opensees" checkout -q FETCH_HEAD
  git -C "$work/opensees" apply "$here/opensees-v3.8.0-mumps.patch"
fi

# --- MUMPS 5.5.1: sequential (no MPI), OpenMP, static ------------------------------------
if [ ! -f "$work/mumps-install/lib/libdmumps.a" ]; then
  rm -rf "$work/mumps" "$work/mumps-build"
  git clone https://github.com/OpenSees/mumps.git "$work/mumps"
  git -C "$work/mumps" checkout -q "$MUMPS_CMAKE_COMMIT"
  git -C "$work/mumps" apply "$here/mumps-openmp.patch"
  mkdir -p "$work/mumps-archive"
  curl -L --fail --retry 5 -o "$work/mumps-archive/mumps_src.tar.gz" "$MUMPS_URL"
  echo "$MUMPS_SHA256  $work/mumps-archive/mumps_src.tar.gz" | shasum -a 256 -c
  CC=gcc-$GCCV FC=gfortran-$GCCV cmake -S "$work/mumps" -B "$work/mumps-build" \
    -Dlocal="$work/mumps-archive" -Dparallel=OFF -Dopenmp=ON -Darith=d \
    -DBUILD_SHARED_LIBS=OFF -DBUILD_TESTING=OFF -DCMAKE_BUILD_TYPE=Release \
    -DCMAKE_INSTALL_PREFIX="$work/mumps-install"
  cmake --build "$work/mumps-build" -j "$(sysctl -n hw.ncpu)"
  cmake --install "$work/mumps-build"
fi

# --- OpenSees with system Mumps ----------------------------------------------------------
"$conan" install "$work/opensees" --output-folder="$work/opensees-build" --build missing \
  -s build_type=Release -s os.version=13.0 -pr:h "$profile" -pr:b "$profile"
rm -f "$work/opensees/CMakeUserPresets.json"
FC=gfortran CC=clang CXX=clang++ cmake -S "$work/opensees" -B "$work/opensees-build/cmake" \
  -DCMAKE_TOOLCHAIN_FILE="$work/opensees-build/build/Release/generators/conan_toolchain.cmake" \
  -DCMAKE_BUILD_TYPE=Release -DCMAKE_DISABLE_FIND_PACKAGE_MPI=ON -DMUMPS_SEQ_DIR="$work/mumps-install"
cmake --build "$work/opensees-build/cmake" --target OpenSees --parallel "$(sysctl -n hw.ncpu)"

cp "$work/opensees-build/cmake/OpenSees" "$out/bin/OpenSees"
cp "$(find "$CONAN_HOME" -path '*tcl8.6/init.tcl' | head -1)" "$out/lib/tcl8.6/init.tcl"

# Nothing but what macOS has: libgfortran and libgomp are linked in, not referenced.
otool -L "$out/bin/OpenSees"
if otool -L "$out/bin/OpenSees" | tail -n +2 | grep -v -E '^[[:space:]]*(/usr/lib/|/System/Library/)'; then
  echo "OpenSees needs libraries macOS does not have"; exit 1
fi
echo "Built $out/bin/OpenSees"
