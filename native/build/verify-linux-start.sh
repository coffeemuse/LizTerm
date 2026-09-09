#!/usr/bin/env bash
# This file is part of LizTerm.
# Copyright 2026 by CoffeeMuse
# SPDX-License-Identifier: BSD-3-Clause

# Starts the built binary in a bare container from the pinned floor image: no toolchain, no build leftovers,
# nothing the build put there. Starting on the floor OS with nothing else present is the portability claim
# itself, and it is the one check verify-linux.sh cannot make — that script runs inside a container already
# and cannot start another.
set -euo pipefail
BIN=${1:?usage: verify-linux-start.sh <binary>}
DIR=$(cd "$(dirname "$BIN")" && pwd)
FILE=$(basename "$BIN")
. "$(dirname "$0")/linux-image.sh"
# 2>&1 because b3270 writes its banner to stderr; without it sed filters an empty stream. sed, not head: see
# verify-linux.sh — head plus pipefail turns a passing check into exit 141.
docker run --rm -v "$DIR:/engine:ro" "$LIZTERM_LINUX_IMAGE" "/engine/$FILE" --version 2>&1 | sed -n '1,3p'
echo "OK: $BIN starts on $LIZTERM_LINUX_IMAGE with no build tools present"
