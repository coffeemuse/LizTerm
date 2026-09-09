#!/usr/bin/env bash
# This file is part of LizTerm.
# Copyright 2026 by CoffeeMuse
# SPDX-License-Identifier: BSD-3-Clause

# Cross-builds b3270.exe for win-x64 inside the pinned container. This is the only script CI or a developer
# calls; it needs Docker on the host and nothing else.
#
# With no arguments it runs the build. With arguments it runs those inside the same image instead, which is how
# CI exercises the gate -- against the built binary and against a fixture -- without a second wrapper.
set -euo pipefail
ROOT=$(cd "$(dirname "$0")/../.." && pwd)
. "$(dirname "$0")/windows-image.sh"
. "$(dirname "$0")/shared-sha256.sh"

# gcc-mingw-w64-x86-64 is the cross compiler configure looks for as x86_64-w64-mingw32-gcc, and
# binutils-mingw-w64-x86-64 supplies the matching windres it also requires and the objdump verify-windows.sh
# reads the import table with -- named explicitly rather than left to come in as a dependency, because the gate
# breaking on a dependency change would be a confusing failure. curl and ca-certificates are
# shared-fetch-tarball.sh's, which runs in here: debian:12-slim ships neither. python3 is x3270's -- its
# configure refuses to run without one ("Can't find Python using 'python3'") because the build generates
# several C sources with Python scripts.
PACKAGES="gcc-mingw-w64-x86-64 binutils-mingw-w64-x86-64 make python3 curl ca-certificates"

if [ "$#" -eq 0 ]; then
  COMMAND="native/build/build-windows.sh"
else
  COMMAND=$(printf '%q ' "$@")
fi

# The container writes as root. Root-owned files under native/ break the actions/cache save and the artifact
# upload that follow it in CI, and a later local build. The trap covers the failure paths too, so a broken
# build does not leave a tree only root can rebuild in.
HOST_UID=$(id -u)
HOST_GID=$(id -g)

# The toolchain is a layer, not a step, for the reason build-linux-docker.sh gives: apt-get install inside a
# fresh container on every invocation is pure waste on the critical path. The tag carries the base digest and
# the package list, so bumping either builds a new image instead of silently reusing the old one.
IMAGE_TAG="lizterm-windows-build:$(printf '%s\n%s\n' "$LIZTERM_WINDOWS_IMAGE" "$PACKAGES" | sha256_stdin | cut -c1-16)"
if ! docker image inspect "$IMAGE_TAG" >/dev/null 2>&1; then
  echo "Building $IMAGE_TAG from $LIZTERM_WINDOWS_IMAGE" >&2
  # A Dockerfile on stdin with "-" as the context: nothing from the repo is sent to the daemon.
  docker build -t "$IMAGE_TAG" - <<DOCKERFILE
FROM $LIZTERM_WINDOWS_IMAGE
RUN apt-get update \
 && apt-get install -y --no-install-recommends $PACKAGES \
 && rm -rf /var/lib/apt/lists/*
DOCKERFILE
fi

docker run --rm -v "$ROOT:/src" -w /src "$IMAGE_TAG" bash -c "
set -euo pipefail
trap 'chown -R $HOST_UID:$HOST_GID native/out native/build-tmp native/cache 2>/dev/null || true' EXIT
$COMMAND
"

# There is deliberately no post-run step here, and its absence is the one real difference from
# build-linux-docker.sh. That script reads back the RID the container resolved and then runs a host-side start
# check, because Docker's platform and the host's can disagree and because a Linux host can run a Linux binary.
# Neither applies: the target here is win-x64 whatever the container is, so the path is known before the
# container starts; and nothing on this machine can run a PE binary, so the start check lives in the
# test-windows CI job instead. See section 4.2 of the spec.
