#!/usr/bin/env bash
# This file is part of LizTerm.
# Copyright 2026 by CoffeeMuse
# SPDX-License-Identifier: BSD-3-Clause

# Downloads and verifies the pinned x3270 source tarball, extracts it into $1, applies LizTerm's patches from
# native/patches, and prints the source directory.
# The pin is this file; shared-fetch-tarball.sh is the machinery.
set -euo pipefail
DEST=${1:?usage: fetch-source.sh <dest-dir>}
VERSION=4.5ga6
SHA256=06faf5ce883852258cc6a2a4da9fe5ce023e97d01e50625ff36f4a01ea703468
URL="https://downloads.sourceforge.net/project/x3270/x3270/$VERSION/suite3270-$VERSION-src.tgz"
# The tarball extracts to suite3270-4.5 (major.minor only), not to its own name.
SRC=$("$(dirname "$0")/shared-fetch-tarball.sh" "$URL" "$SHA256" "$DEST" "suite3270-4.5")

# shared-fetch-tarball.sh starts from a fresh tree every time, so each patch applies exactly once. Name order, in
# the C locale so it cannot vary by machine. A patch that no longer applies fails here rather than producing an
# engine without it. -F0 allows no fuzz, so a hunk is never applied against context that no longer matches; a hunk
# whose code merely moved still applies at its new line. -N and --batch mean patch never stops to ask. The callers
# read this script's stdout as the source directory, so patch's own output goes to stderr. See docs/engines.md,
# "Patches".
LC_ALL=C
shopt -s nullglob
for PATCH in "$(dirname "$0")"/../patches/*.patch; do
  echo "Applying $(basename "$PATCH")" >&2
  patch -p1 -N -F0 --batch -d "$SRC" < "$PATCH" >&2
done
echo "$SRC"
