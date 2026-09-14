#!/usr/bin/env bash
# This file is part of LizTerm.
# Copyright 2026 by CoffeeMuse
# SPDX-License-Identifier: BSD-3-Clause

# Fails unless the binary carries every patch in native/patches, each recognised by its marker: a string only the
# patched source puts into the binary. A new patch adds its marker to MARKERS. The backend reads the same marker at
# run time (src/LizTerm.Backend.B3270/Process/EnginePatches.cs), and a test holds the two to the same name.
#
# It reads the file's bytes and never runs it, so it covers the arm64 and Windows engines a runner cannot execute.
# grep reads the file itself rather than a pipe: grep -q exits on its first match, and a writer killed by that
# SIGPIPE would fail the script under pipefail.
set -euo pipefail
BIN=${1:?usage: shared-verify-patches.sh <binary>}
[ -f "$BIN" ] || { echo "ERROR: $BIN does not exist" >&2; exit 1; }
# b3270-transfer-commandprefix.patch: the Transfer action's new keyword. A stock 4.5ga6 b3270 contains
# OtherOptions twice and CommandPrefix not at all.
MARKERS=(CommandPrefix)
for MARKER in "${MARKERS[@]}"; do
  if ! LC_ALL=C grep -a -q -e "$MARKER" "$BIN"; then
    echo "ERROR: $BIN does not carry LizTerm's patch marker '$MARKER'; it was built without native/patches applied." >&2
    exit 1
  fi
done
echo "OK: $BIN carries LizTerm's patches (${MARKERS[*]})"
