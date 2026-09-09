#!/usr/bin/env bash
# This file is part of LizTerm.
# Copyright 2026 by CoffeeMuse
# SPDX-License-Identifier: BSD-3-Clause

# Cross-builds b3270.exe for win-x64 with the mingw-w64 toolchain.
# Runs INSIDE the pinned build container (build-windows-docker.sh); assumes that container's toolchain, the way
# build-macos.sh assumes Xcode command line tools and Homebrew.
set -euo pipefail
ROOT=$(cd "$(dirname "$0")/../.." && pwd)

# Unlike build-linux.sh there is no architecture to resolve and nothing to hand back to the wrapper: the target
# is win-x64 whatever this container is. The x3270 source knows only two Windows hosts -- Common/dirnames sets
# win64=x86_64-w64-mingw32 and win32=i686-w64-mingw32 -- and no aarch64 one, which is why win-arm64 ships this
# same binary under Windows 11's x64 emulation rather than a native build of its own.
RID=win-x64
HOST=x86_64-w64-mingw32
BUILD="$ROOT/native/build-tmp/$RID"
mkdir -p "$BUILD"
JOBS=$(nproc)

# There are no static prefixes here, and so no fetch-*.sh of our own and no .pin stamps. That is not an
# omission to be tidied up into symmetry with build-linux.sh later:
#   - OpenSSL: wb3270's LIBS line references SSLLIB, which is never defined anywhere in the x3270 tree and so
#     expands to empty. TLS on Windows is Schannel, reached through -lcrypt32 -lsecur32 on that same line.
#     There is no configure probe that can fail, which is why this script needs no equivalent of
#     build-linux.sh's LIBS="-ldl -pthread".
#   - expat: bundled upstream at extern/libexpat and built by the suite's own libexpat target. Linux needed a
#     pinned static build only because no distribution-independent libexpat exists there.
# The .pin machinery in build-linux.sh guards prefixes whose staleness has no tell. With no prefixes, it would
# guard nothing.
SRC=$("$ROOT/native/build/fetch-source.sh" "$BUILD/src")
cd "$SRC"

# --host is what puts configure into MODE=windows: it matches Common/dirnames' win64 and from there the build
# uses Makefile.windows.in, looks for ${host}-gcc, ${host}-gcc-ar and ${host}-windres, and descends into the w*
# directories. The component flags are build-linux.sh's, unchanged. Disabling x3270if and pr3287 is safe
# despite Makefile.windows.in listing them as b3270 prerequisites: configure never creates their
# subdirectories, so those rules are not reached.
./configure --host="$HOST" --enable-b3270 \
  --disable-x3270 --disable-c3270 --disable-s3270 --disable-tcl3270 \
  --disable-pr3287 --disable-x3270if --disable-mitm --disable-playback \
  > "$BUILD/configure.log" 2>&1
make -j"$JOBS" > "$BUILD/make.log" 2>&1

# obj/x86_64-w64-mingw32/b3270/, note: the object directory is b3270 even though the source directory is
# wb3270. sed rather than head for the same SIGPIPE-under-pipefail reason the Linux scripts document.
BIN=$(find obj -type f -name b3270.exe | sort | sed -n 1p)
[ -n "$BIN" ] || { echo "no b3270.exe under obj/ after a successful make" >&2; exit 1; }
OUT="$ROOT/native/out/$RID"
mkdir -p "$OUT"
cp "$BIN" "$OUT/b3270.exe"
# No chmod: Windows has no executable bit, and B3270Locator skips that check there. This is also why the CI
# artifact needs no chmod on the way back in, unlike the macOS and Linux engines.
"$ROOT/native/build/verify-windows.sh" "$OUT/b3270.exe"
