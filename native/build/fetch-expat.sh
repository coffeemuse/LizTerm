#!/usr/bin/env bash
# Downloads and verifies the pinned expat source tarball, extracts it into $1.
#
# b3270 requires libexpat and there is no --disable option for it (b3270/configure.in: AC_SEARCH_LIBS on
# XML_ParserCreate, then a hard AC_MSG_ERROR if expat.h is missing). macOS gets it from /usr/lib, which
# verify-macos.sh allows because it is part of that OS. Linux has no equivalent guarantee — libexpat belongs to
# a separate project, not to glibc — so it is built here from source and linked statically, exactly as OpenSSL
# is, rather than becoming a runtime dependency the gate would have to be widened to permit.
#
# A sibling of fetch-openssl.sh and fetch-source.sh for the same reason they are siblings of each other: the pin
# is the point, and it should be visible in the file that owns it.
set -euo pipefail
DEST=${1:?usage: fetch-expat.sh <dest-dir>}
ROOT=$(cd "$(dirname "$0")/../.." && pwd)
VERSION=2.8.4
# Verified identical from the GitHub release and the SourceForge mirror before it was recorded here.
SHA256=b8ece2437692dad44d851c4532723390a5a330990007706be9c8d2b90d294f36
# libexpat tags releases R_2_8_4 for 2.8.4, so the tag is derived rather than written out twice.
URL="https://github.com/libexpat/libexpat/releases/download/R_${VERSION//./_}/expat-$VERSION.tar.gz"
CACHE="$ROOT/native/cache"
TGZ="$CACHE/expat-$VERSION.tar.gz"
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
echo "$DEST/expat-$VERSION"
