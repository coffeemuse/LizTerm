#!/usr/bin/env bash
# Fails if the binary links anything outside macOS system libraries, or if it was built without TLS.
set -euo pipefail
BIN=${1:?usage: verify-macos.sh <binary>}
BAD=$(otool -L "$BIN" | tail -n +2 | awk '{print $1}' | grep -v -E '^(/usr/lib/|/System/Library/)' || true)
if [ -n "$BAD" ]; then
  echo "ERROR: $BIN has non-system dynamic dependencies:" >&2
  echo "$BAD" >&2
  exit 1
fi
# The same check the Linux gate makes, for the same reason: build-macos.sh runs x3270's configure against the
# static archives it stages from Homebrew's openssl@3, and if that probe ever fails it yields a "TLS provider:
# None" binary that links FEWER libraries and so passes the otool check above more comfortably than the real
# engine -- which engine-macos would then publish as b3270-osx-arm64.
# This also replaces the old `"$BIN" --version | head -3` tail, which was inert (b3270 writes its banner to
# stderr) and could not simply gain a 2>&1 while keeping head: that would have exposed the
# SIGPIPE-under-pipefail trap the Linux scripts document. The shared script captures instead of piping.
BANNER=$("$(dirname "$0")/shared-verify-tls.sh" "$BIN")
echo "OK: $BIN links only system libraries and has TLS"
otool -L "$BIN"
printf '%s\n' "$BANNER"
