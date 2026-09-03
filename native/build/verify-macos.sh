#!/usr/bin/env bash
# Fails if the binary links anything outside macOS system libraries.
set -euo pipefail
BIN=${1:?usage: verify-macos.sh <binary>}
BAD=$(otool -L "$BIN" | tail -n +2 | awk '{print $1}' | grep -v -E '^(/usr/lib/|/System/Library/)' || true)
if [ -n "$BAD" ]; then
  echo "ERROR: $BIN has non-system dynamic dependencies:" >&2
  echo "$BAD" >&2
  exit 1
fi
echo "OK: $BIN links only system libraries"
otool -L "$BIN"
"$BIN" --version | head -3
