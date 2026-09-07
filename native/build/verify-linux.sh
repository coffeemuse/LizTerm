#!/usr/bin/env bash
# Fails if the binary links anything outside the glibc runtime, or if it imports a glibc symbol newer than the
# floor. Runs INSIDE the build container (it needs that container's ldd and readelf).
set -euo pipefail
BIN=${1:?usage: verify-linux.sh <binary>}
FLOOR=2.28

# 1. Every dynamic dependency must be part of the glibc runtime, present on any system we support. This is
# verify-macos.sh's check, spelled for glibc: an libssl.so here would mean the engine picked up a shared
# OpenSSL and will not start on a machine without that exact one.
# The leading-whitespace grep excludes ldd's own diagnostics, not just dependency lines: when a binary imports
# a glibc symbol version this container's glibc does not have, ldd still lists every resolved object (each on
# its own tab-indented line, "not found" ones included) but also prints an unindented "version `GLIBC_x.y' not
# found" line ahead of them, on stdout, not stderr. Without this filter that line's first field becomes the
# binary's own path with a colon stuck on, which matches no allowed name and gets reported here as a phantom
# dependency — masking the real, actionable problem that check 2 below exists to name.
# libutil and libanl are on this list because b3270 genuinely needs them: x3270's configure resolves forkpty to
# -lutil and getaddrinfo_a to -lanl. Both are glibc's own — `rpm -qf /usr/lib64/lib{util,anl}.so.1` on the floor
# image answers glibc-2.28 for each — and they are separate shared objects only up to glibc 2.33; 2.34 folded
# them into libc.so.6 along with libdl, libpthread and librt, which are already here for the same reason. So
# allowing them does not widen the check beyond "the glibc runtime": a system with libc.so.6 has them.
ALLOWED='^(linux-vdso|libc|libm|libdl|libpthread|librt|libresolv|libutil|libanl|libgcc_s|ld-linux.*)\.so'
BAD=$(ldd "$BIN" | grep -E '^[[:space:]]' | awk '{print $1}' | sed 's|.*/||' | grep -v -E "$ALLOWED" || true)
if [ -n "$BAD" ]; then
  echo "ERROR: $BIN has dynamic dependencies outside the glibc runtime:" >&2
  echo "$BAD" >&2
  exit 1
fi

# 2. ldd alone is not a portability claim: a binary built on Ubuntu 24.04 links only these too, and still
# cannot start on RHEL 9. The highest glibc symbol version it imports is what actually decides.
# The || true is load-bearing: grep exits 1 when it matches nothing, and under pipefail that would abort
# the script here instead of reaching the empty check below, which is the branch with the useful message.
HIGHEST=$(readelf --dyn-syms --wide "$BIN" | grep -o 'GLIBC_[0-9][0-9.]*' | sed 's/GLIBC_//' | sort -V | tail -1 || true)
if [ -z "$HIGHEST" ]; then
  echo "ERROR: $BIN imports no versioned glibc symbols. Is it dynamically linked against glibc at all?" >&2
  exit 1
fi
if [ "$(printf '%s\n%s\n' "$FLOOR" "$HIGHEST" | sort -V | tail -1)" != "$FLOOR" ]; then
  echo "ERROR: $BIN needs glibc $HIGHEST, above the $FLOOR floor." >&2
  echo "Was it built outside the pinned container? See native/build/linux-image.sh." >&2
  exit 1
fi

echo "OK: $BIN links only the glibc runtime and needs no more than glibc $HIGHEST (floor $FLOOR)"
ldd "$BIN"
# 2>&1 because b3270 writes its whole banner to stderr: without it sed filters an empty stream and a dozen
# unfiltered lines reach the log anyway.
# sed, not head: head closes the pipe after three lines, GNU coreutils SIGPIPEs the writer for it, and under
# pipefail that 141 becomes this script's exit status — a passing binary reported as a failed gate. That trap
# is why the redirection cannot simply be bolted onto a head.
"$BIN" --version 2>&1 | sed -n '1,3p'
