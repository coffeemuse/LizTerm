#!/usr/bin/env bash
# Builds b3270 for Linux inside the pinned container. This is the only script CI or a developer calls; it needs
# Docker on the host and nothing else.
#
# With no arguments it runs the build. With arguments it runs those inside the same image instead, which is how
# the gate's negative fixtures are checked without a second wrapper.
set -euo pipefail
ROOT=$(cd "$(dirname "$0")/../.." && pwd)
. "$(dirname "$0")/linux-image.sh"

# binutils is for the readelf the gate needs; findutils and diffutils are configure's, perl-core is OpenSSL's.
PACKAGES="gcc make perl-core diffutils findutils tar binutils"

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
