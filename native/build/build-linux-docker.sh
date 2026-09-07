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

docker run --rm -v "$ROOT:/src" -w /src "$LIZTERM_LINUX_IMAGE" bash -c "
set -euo pipefail
trap 'chown -R $HOST_UID:$HOST_GID native/out native/build-tmp native/cache 2>/dev/null || true' EXIT
dnf install -y $PACKAGES > /tmp/dnf.log 2>&1 || { cat /tmp/dnf.log >&2; exit 1; }
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
