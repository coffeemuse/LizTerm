#!/usr/bin/env bash
# This file is part of LizTerm.
# Copyright 2026 by CoffeeMuse
# SPDX-License-Identifier: BSD-3-Clause

# Builds x3270's playback tool (replays .trc host traces over a socket) into native/build-tmp/playback/.
set -euo pipefail
ROOT=$(cd "$(dirname "$0")/../.." && pwd)
BUILD="$ROOT/native/build-tmp/playback"
SRC=$("$ROOT/native/build/fetch-source.sh" "$BUILD/src")
cd "$SRC"
./configure --enable-playback --disable-x3270 --disable-c3270 --disable-s3270 --disable-b3270 \
  --disable-tcl3270 --disable-pr3287 --disable-x3270if --disable-mitm > "$BUILD/configure.log" 2>&1
# The top-level configure only adds "lib" to $subdirs when one of x3270/c3270/s3270/b3270/
# tcl3270/pr3287 is enabled, so with everything but playback disabled it never configures
# lib/3270 and lib/32xx even though playback links against them. Configure lib/ directly.
(cd lib && ./configure) >> "$BUILD/configure.log" 2>&1
make -j4 > "$BUILD/make.log" 2>&1
BIN=$(find obj -type f -name playback -perm +111 | head -1)
cp "$BIN" "$BUILD/playback"
echo "$BUILD/playback"
echo "Traces are under $SRC/*/Test/*.trc"
