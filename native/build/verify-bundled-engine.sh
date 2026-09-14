#!/usr/bin/env bash
# This file is part of LizTerm.
# Copyright 2026 by CoffeeMuse
# SPDX-License-Identifier: BSD-3-Clause

# Fails if a publish tree's bundled engine is missing, not executable, or the wrong machine type.
#
# The wrong-machine-type arm is the load-bearing one. release.yml downloads five engines and fans them into
# six publish trees; a matrix wiring slip puts a real, working, gate-passed engine into the wrong tree, and
# every other check in the pipeline passes it. It is caught here or it is caught by a user.
#
# Machine type is read from the file header rather than with lipo/file/objdump: this one script runs on macOS,
# Linux and Windows runners and those tools are not all present on all three. od is POSIX and is on every one,
# Git Bash included.
set -euo pipefail
DIR=${1:?usage: verify-bundled-engine.sh <publish-dir> <rid>}
RID=${2:?usage: verify-bundled-engine.sh <publish-dir> <rid>}

# The engine's RID, which is the target RID except for win-arm64. Plan 3d settled that there is no aarch64
# Windows host in x3270 4.5ga6, so win-arm64 ships the win-x64 engine under Windows 11's emulation. This
# mapping is also spelled in src/LizTerm.App/LizTerm.App.csproj, which does the copy; the two must agree, and
# Task 3's negative case is what proves they do.
case "$RID" in
  win-arm64) ENGINE_RID=win-x64 ;;
  *)         ENGINE_RID=$RID ;;
esac

case "$ENGINE_RID" in
  osx-arm64)   NAME=b3270;     EXPECTED=arm64 ;;
  osx-x64)     NAME=b3270;     EXPECTED=x86_64 ;;
  linux-arm64) NAME=b3270;     EXPECTED=aarch64 ;;
  linux-x64)   NAME=b3270;     EXPECTED=x86_64 ;;
  win-x64)     NAME=b3270.exe; EXPECTED=x86_64 ;;
  *) echo "ERROR: unknown rid $RID" >&2; exit 1 ;;
esac

# Searched at any depth, not just directly under $DIR. This gate runs against two shapes: a plain publish
# tree, where the path is $DIR/runtimes/<rid>/native/, and an EXTRACTED PACKAGE, where the macOS layout nests
# it under LizTerm.app/Contents/MacOS/. Exactly one match is required -- zero means nothing shipped, and
# several means the package holds engines it should not.
FOUND=$(find "$DIR" -type f -path "*/runtimes/$RID/native/$NAME" 2>/dev/null | sort)
COUNT=$(printf '%s' "$FOUND" | grep -c . || true)
if [ "$COUNT" -eq 0 ]; then
  echo "ERROR: no bundled engine at $DIR/**/runtimes/$RID/native/$NAME" >&2; exit 1
elif [ "$COUNT" -gt 1 ]; then
  echo "ERROR: $COUNT bundled engines under $DIR, expected 1:" >&2; printf '%s\n' "$FOUND" >&2; exit 1
fi
BIN=$FOUND

# Windows has no executable bit; everywhere else, a missing one is what an artifact round trip costs, and the
# app would report it only when the user first tries to connect.
case "$ENGINE_RID" in
  win-*) ;;
  *) [ -x "$BIN" ] || { echo "ERROR: $BIN is not executable" >&2; exit 1; } ;;
esac

# Reads <count> bytes at <offset> as lower-case hex with no spaces or newlines.
bytes_at() { od -An -tx1 -j "$1" -N "$2" "$BIN" | tr -d ' \n'; }
# Reads <count> little-endian bytes at <offset> as a decimal number.
le_at() {
  local hex rev="" i
  hex=$(bytes_at "$1" "$2")
  for (( i = ${#hex} - 2; i >= 0; i -= 2 )); do rev="$rev${hex:$i:2}"; done
  echo $(( 16#$rev ))
}

MAGIC=$(bytes_at 0 4)
case "$MAGIC" in
  cffaedfe)   # Mach-O 64-bit little-endian: cputype is 4 bytes at offset 4.
    case "$(le_at 4 4)" in
      16777223) ACTUAL=x86_64 ;;   # CPU_TYPE_X86_64 0x01000007
      16777228) ACTUAL=arm64 ;;    # CPU_TYPE_ARM64  0x0100000c
      *) ACTUAL="mach-o cputype $(le_at 4 4)" ;;
    esac ;;
  7f454c46*)  # ELF: e_machine is 2 bytes at offset 18.
    case "$(le_at 18 2)" in
      62)  ACTUAL=x86_64 ;;        # EM_X86_64
      183) ACTUAL=aarch64 ;;       # EM_AARCH64
      *) ACTUAL="elf e_machine $(le_at 18 2)" ;;
    esac ;;
  4d5a*)      # PE: e_lfanew is 4 bytes at 0x3c; Machine is 2 bytes at e_lfanew+4.
    PE=$(le_at 60 4)
    case "$(le_at $(( PE + 4 )) 2)" in
      34404) ACTUAL=x86_64 ;;      # IMAGE_FILE_MACHINE_AMD64 0x8664
      43620) ACTUAL=arm64 ;;       # IMAGE_FILE_MACHINE_ARM64 0xaa64
      *) ACTUAL="pe machine $(le_at $(( PE + 4 )) 2)" ;;
    esac ;;
  *) ACTUAL="unrecognised header $MAGIC" ;;
esac

if [ "$ACTUAL" != "$EXPECTED" ]; then
  echo "ERROR: $BIN is $ACTUAL but $RID needs $EXPECTED" >&2
  exit 1
fi

# The engine inside the archive must still be the patched one. engines.yml's gates check each engine as it is
# built; this checks the copy every archive carries, on all six RIDs, win-arm64 included.
"$(dirname "$0")/shared-verify-patches.sh" "$BIN"
echo "OK: $BIN is $ACTUAL, correct for $RID, and carries LizTerm's patches"
