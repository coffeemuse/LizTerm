#!/usr/bin/env bash
# Builds b3270 for Linux inside the pinned container. This is the only script CI or a developer calls; it needs
# Docker on the host and nothing else.
#
# With no arguments it runs the build. With arguments it runs those inside the same image instead, which is how
# CI exercises the gate — against the built binary and against a fixture per arm, in one invocation — without a
# second wrapper.
set -euo pipefail
ROOT=$(cd "$(dirname "$0")/../.." && pwd)
. "$(dirname "$0")/linux-image.sh"
. "$(dirname "$0")/shared-sha256.sh"

# binutils is for the readelf the gate needs; findutils and diffutils are configure's, perl-core is OpenSSL's.
# python3 is x3270's: its configure refuses to run without one ("Can't find Python using 'python3'") because the
# build generates several C sources with Python scripts.
PACKAGES="gcc make perl-core diffutils findutils tar binutils python3"

if [ "$#" -eq 0 ]; then
  COMMAND="native/build/build-linux.sh"
else
  COMMAND=$(printf '%q ' "$@")
fi

# The container writes as root. Root-owned files under native/ break the actions/cache save and the artifact
# upload that follow it in CI, and a later local build. The trap covers the failure paths too, so a broken
# build does not leave a tree only root can rebuild in. On Docker Desktop the bind mount is already owned by
# the calling user and the chown is a harmless no-op.
HOST_UID=$(id -u)
HOST_GID=$(id -g)

# The toolchain is a layer, not a step. dnf install used to run inside a fresh container on every invocation
# with nothing about it cached -- twice on the critical path of a CI run, once more for every local rebuild, and
# the single biggest source of the x64 variance timeout-minutes still carries headroom for. It now goes into an
# image derived from the pinned base, built once and reused. The tag carries the base digest and the package
# list, so bumping either builds a new image instead of silently reusing the old one, and the pin stays the only
# thing this build trusts.
IMAGE_TAG="lizterm-linux-build:$(printf '%s\n%s\n' "$LIZTERM_LINUX_IMAGE" "$PACKAGES" | sha256_stdin | cut -c1-16)"
if ! docker image inspect "$IMAGE_TAG" >/dev/null 2>&1; then
  echo "Building $IMAGE_TAG from $LIZTERM_LINUX_IMAGE" >&2
  # A Dockerfile on stdin with "-" as the context: nothing from the repo is sent to the daemon.
  docker build -t "$IMAGE_TAG" - <<DOCKERFILE
FROM $LIZTERM_LINUX_IMAGE
RUN dnf install -y $PACKAGES && dnf clean all
DOCKERFILE
fi

docker run --rm -v "$ROOT:/src" -w /src "$IMAGE_TAG" bash -c "
set -euo pipefail
trap 'chown -R $HOST_UID:$HOST_GID native/out native/build-tmp native/cache 2>/dev/null || true' EXIT
$COMMAND
"

# The build path gets the whole gate. CI calls this script directly as well, because on a cache hit the
# docker run above never happens and this is the only thing between a stale cached binary and an upload.
#
# The RID comes from the file build-linux.sh just wrote, never from the host's `uname -m`: the two disagree
# whenever Docker's platform is not the host's — DOCKER_DEFAULT_PLATFORM=linux/amd64 on an arm64 Mac builds
# native/out/linux-x64 inside the container while the host still reads arm64 — and a host-derived path would
# send the start check somewhere this build never wrote, failing a good build on docker's bare "no such file".
if [ "$#" -eq 0 ]; then
  RID_FILE="$ROOT/native/build-tmp/rid"
  [ -f "$RID_FILE" ] || { echo "$RID_FILE is missing: the build recorded no RID" >&2; exit 1; }
  "$(dirname "$0")/verify-linux-start.sh" "$ROOT/native/out/$(cat "$RID_FILE")/b3270"
fi
