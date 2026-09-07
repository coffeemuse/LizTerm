#!/usr/bin/env bash
# Downloads and verifies the pinned OpenSSL source tarball, extracts it into $1.
# Deliberately a sibling of fetch-source.sh rather than a generalisation of it: the pin is the point, and it
# should be visible in the file that owns it. Bumping the version here is a one-line edit that invalidates the
# CI caches on its own, because their keys hash these scripts.
set -euo pipefail
DEST=${1:?usage: fetch-openssl.sh <dest-dir>}
ROOT=$(cd "$(dirname "$0")/../.." && pwd)
VERSION=3.5.8
SHA256=a8f84a39918ec6415ce765d9b429d313ba97b8143169c172e734b9514464f5b2
URL="https://github.com/openssl/openssl/releases/download/openssl-$VERSION/openssl-$VERSION.tar.gz"
CACHE="$ROOT/native/cache"
TGZ="$CACHE/openssl-$VERSION.tar.gz"
# This script runs inside the build container, where shasum (a perl script) may be absent, and on macOS, where
# sha256sum is. Take whichever is there.
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
echo "$DEST/openssl-$VERSION"
