#!/usr/bin/env bash
# Downloads and verifies the pinned OpenSSL source tarball, extracts it into $1.
# Deliberately a sibling of fetch-source.sh rather than folded into it: the pin is the point, and it should be
# visible in the file that owns it. Bumping the version here is a one-line edit that invalidates the CI caches
# on its own, because their keys hash these scripts. shared-fetch-tarball.sh is the machinery.
set -euo pipefail
DEST=${1:?usage: fetch-openssl.sh <dest-dir>}
VERSION=3.5.8
SHA256=a8f84a39918ec6415ce765d9b429d313ba97b8143169c172e734b9514464f5b2
URL="https://github.com/openssl/openssl/releases/download/openssl-$VERSION/openssl-$VERSION.tar.gz"
exec "$(dirname "$0")/shared-fetch-tarball.sh" "$URL" "$SHA256" "$DEST" "openssl-$VERSION"
