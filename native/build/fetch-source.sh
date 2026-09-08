#!/usr/bin/env bash
# Downloads and verifies the pinned x3270 source tarball, extracts it into $1.
# The pin is this file; shared-fetch-tarball.sh is the machinery.
set -euo pipefail
DEST=${1:?usage: fetch-source.sh <dest-dir>}
VERSION=4.5ga6
SHA256=06faf5ce883852258cc6a2a4da9fe5ce023e97d01e50625ff36f4a01ea703468
URL="https://downloads.sourceforge.net/project/x3270/x3270/$VERSION/suite3270-$VERSION-src.tgz"
# The tarball extracts to suite3270-4.5 (major.minor only), not to its own name.
exec "$(dirname "$0")/shared-fetch-tarball.sh" "$URL" "$SHA256" "$DEST" "suite3270-4.5"
