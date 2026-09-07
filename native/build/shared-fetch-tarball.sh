#!/usr/bin/env bash
# Downloads a pinned source tarball into native/cache (once), verifies its SHA-256, extracts it into <dest>,
# and prints the extracted directory.
#
# The fetch-*.sh scripts are the pins: each owns a version, a checksum and a URL, and delegates the machinery
# to this. Keeping the pin visible in the file that owns it is why those scripts are siblings rather than one
# generalisation, and that survives here — what they no longer each carry is a copy of the download, verify and
# extract body, which had already needed one synchronised edit across all three.
set -euo pipefail
URL=${1:?usage: shared-fetch-tarball.sh <url> <sha256> <dest-dir> <extracted-subdir>}
SHA256=${2:?usage: shared-fetch-tarball.sh <url> <sha256> <dest-dir> <extracted-subdir>}
DEST=${3:?usage: shared-fetch-tarball.sh <url> <sha256> <dest-dir> <extracted-subdir>}
SUBDIR=${4:?usage: shared-fetch-tarball.sh <url> <sha256> <dest-dir> <extracted-subdir>}
ROOT=$(cd "$(dirname "$0")/../.." && pwd)
. "$(dirname "$0")/shared-sha256.sh"
CACHE="$ROOT/native/cache"
# The cache name comes from the URL, which is what every caller already named its cache file, so bumping a pin
# lands on a new file rather than reusing the previous version's.
TGZ="$CACHE/$(basename "$URL")"
mkdir -p "$CACHE"
if [ ! -f "$TGZ" ]; then
  echo "Downloading $URL" >&2
  curl -fsSL -o "$TGZ" "$URL"
fi
echo "$SHA256  $TGZ" | sha256_check >&2
rm -rf "$DEST"
mkdir -p "$DEST"
tar xzf "$TGZ" -C "$DEST"
echo "$DEST/$SUBDIR"
