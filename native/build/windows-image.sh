#!/usr/bin/env bash
# This file is part of LizTerm.
# Copyright 2026 by CoffeeMuse
# SPDX-License-Identifier: BSD-3-Clause

# The pinned Windows cross-build base, sourced (not executed) by build-windows-docker.sh.
#
# Unlike linux-image.sh, this image is a *build host* and nothing more. AlmaLinux 8 was chosen because its
# glibc 2.28 is the floor LizTerm's Linux users inherit -- the image and the product share an ABI. A PE binary
# shares nothing with the container it was cross-compiled in, so no property of this image reaches a user, and
# the choice is free to be "the distribution with the mingw-w64 toolchain we want". Reusing the AlmaLinux pin
# here would mean an older EPEL toolchain picked for a reason that does not apply.
#
# Pinned by digest anyway, for the reason linux-image.sh is: a floating tag can raise the toolchain silently,
# and the first anyone would know is a binary that imports a DLL the gate rejects -- or, worse, one it does not.
#
# It must be the multi-architecture *index* digest, not one platform manifest's, so this builds on an arm64 Mac
# and an x64 runner alike. `docker buildx imagetools inspect debian:12-slim --format '{{.Manifest.Digest}}'`
# gives the index digest; this one was resolved on 2026-09-07.
#
# It is a *.sh under native/build, so hashFiles('native/build/*windows*.sh') covers it: changing the image
# invalidates the CI engine cache and forces a real build and a fresh gate run. It is a separate file from
# linux-image.sh, rather than one shared list, because the two cache keys are separate on purpose -- a Debian
# bump cannot change a Linux or macOS binary, and sharing the file would force cold rebuilds that cannot differ.
LIZTERM_WINDOWS_IMAGE=debian:12-slim@sha256:88200866dfff7ea7f5cbcb6ec7c8a701889efe6fe859fe64d6990e4b07ea4171
