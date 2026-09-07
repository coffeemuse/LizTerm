#!/usr/bin/env bash
# Downloads and verifies the pinned x3270 source tarball, extracts it into $1.
set -euo pipefail
DEST=${1:?usage: fetch-source.sh <dest-dir>}
ROOT=$(cd "$(dirname "$0")/../.." && pwd)
VERSION=4.5ga6
SHA256=06faf5ce883852258cc6a2a4da9fe5ce023e97d01e50625ff36f4a01ea703468
URL="https://downloads.sourceforge.net/project/x3270/x3270/$VERSION/suite3270-$VERSION-src.tgz"
CACHE="$ROOT/native/cache"
TGZ="$CACHE/suite3270-$VERSION-src.tgz"
# Runs inside the Linux build container as well as on macOS; take whichever checksum tool is present.
checksum() { if command -v sha256sum >/dev/null 2>&1; then sha256sum "$@"; else shasum -a 256 "$@"; fi; }
mkdir -p "$CACHE"
if [ ! -f "$TGZ" ]; then
  echo "Downloading $URL" >&2
  curl -fsSL -o "$TGZ" "$URL"
fi
echo "$SHA256  $TGZ" | checksum -c - >&2
rm -rf "$DEST"
mkdir -p "$DEST"
tar xzf "$TGZ" -C "$DEST"
# The tarball extracts to suite3270-4.5 (major.minor only).
echo "$DEST/suite3270-4.5"
