#!/usr/bin/env bash
# This file is part of LizTerm.
# Copyright 2026 by CoffeeMuse
# SPDX-License-Identifier: BSD-3-Clause

# The one place that knows how to take a SHA-256 in this directory. Sourced, not executed, like linux-image.sh.
#
# The Linux build container has coreutils' sha256sum and may not have shasum (a perl script); macOS has shasum
# and not sha256sum. Every script under native/build that hashes anything runs on both, so the choice is made
# here once instead of being written out again in each of them.
sha256_stdin() {
  if command -v sha256sum >/dev/null 2>&1; then sha256sum; else shasum -a 256; fi | awk '{print $1}'
}
# Reads "<hash>  <path>" lines on stdin, as the fetchers produce, and fails if the file does not match.
sha256_check() {
  if command -v sha256sum >/dev/null 2>&1; then sha256sum -c -; else shasum -a 256 -c -; fi
}
