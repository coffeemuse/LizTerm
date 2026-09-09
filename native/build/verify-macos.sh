#!/usr/bin/env bash
# This file is part of LizTerm.
# Copyright 2026 by CoffeeMuse
# SPDX-License-Identifier: BSD-3-Clause

# Fails if the binary links anything outside macOS system libraries, or if it was built without TLS.
set -euo pipefail
BIN=${1:?usage: verify-macos.sh <binary>}
# Runs first, so a wrong-architecture binary is rejected by a message naming the mistake rather than by an
# opaque failure downstream. This is plan 3d's machine-type check on the platform that now cross-compiles:
# an x86_64 b3270 links exactly the same system libraries an arm64 one does, so the otool scan below cannot
# see the difference -- exactly as the Windows import allowlist could not see a 32-bit PE.
# Defaults to the host's architecture so the single-argument callers this script already has keep working.
EXPECTED=${2:-$(uname -m)}
ACTUAL=$(lipo -archs "$BIN")
if [ "$ACTUAL" != "$EXPECTED" ]; then
  echo "ERROR: $BIN is $ACTUAL, expected $EXPECTED" >&2
  exit 1
fi
BAD=$(otool -L "$BIN" | tail -n +2 | awk '{print $1}' | grep -v -E '^(/usr/lib/|/System/Library/)' || true)
if [ -n "$BAD" ]; then
  echo "ERROR: $BIN has non-system dynamic dependencies:" >&2
  echo "$BAD" >&2
  exit 1
fi
# The same check the Linux gate makes, for the same reason: build-macos.sh runs x3270's configure against the
# static OpenSSL archive it builds from the pinned tarball fetch-openssl.sh names, and if that probe ever fails
# it yields a "TLS provider: None" binary that links FEWER libraries and so passes the otool check above more
# comfortably than the real engine -- which engine-macos would then publish as b3270-osx-arm64 or b3270-osx-x64.
# This also replaces the old `"$BIN" --version | head -3` tail, which was inert (b3270 writes its banner to
# stderr) and could not simply gain a 2>&1 while keeping head: that would have exposed the
# SIGPIPE-under-pipefail trap the Linux scripts document. The shared script captures instead of piping.
BANNER=$("$(dirname "$0")/shared-verify-tls.sh" "$BIN")
echo "OK: $BIN is $ACTUAL, links only system libraries and has TLS"
otool -L "$BIN"
printf '%s\n' "$BANNER"
