#!/usr/bin/env bash
# This file is part of LizTerm.
# Copyright 2026 by CoffeeMuse
# SPDX-License-Identifier: BSD-3-Clause

# Builds b3270 for the container's architecture against a statically built OpenSSL and expat.
# Runs INSIDE the pinned build container (build-linux-docker.sh); assumes that container's toolchain, the way
# build-macos.sh assumes Xcode command line tools and Homebrew.
set -euo pipefail
ROOT=$(cd "$(dirname "$0")/../.." && pwd)
ARCH=$(uname -m)
case "$ARCH" in
  x86_64)  RID=linux-x64 ;;
  aarch64) RID=linux-arm64 ;;
  *) echo "unsupported arch $ARCH" >&2; exit 1 ;;
esac
BUILD="$ROOT/native/build-tmp/$RID"
mkdir -p "$BUILD"
# The RID the *container* resolved, for build-linux-docker.sh to read back rather than re-derive. It cannot
# derive this itself: with DOCKER_DEFAULT_PLATFORM=linux/amd64 (or an amd64 default in Docker Desktop) an
# arm64 Mac gets an x86_64 container, and a host-side `uname -m` would name a path this build never wrote.
printf '%s\n' "$RID" > "$ROOT/native/build-tmp/rid"
JOBS=$(nproc)

# Each static prefix is stamped with the SHA-256 of the fetch script that filled it *and* of this script, and
# the guards below require the stamp as well as the archive. Bumping a pin rewrites its fetcher and changing a
# configure line rewrites this file, so either way a prefix built from the previous inputs stops matching and
# is rebuilt. Without the stamp, a rebuild that reuses an existing native/build-tmp sees the archive, skips
# the block — and because the fetch is inside the block, silently links the old library and never verifies
# the new checksum either. CI never hits this (it caches native/out, never native/build-tmp), but the CVE
# story for both pins is "a one-line edit in the fetcher", and locally that has to be true. A stale OpenSSL at
# least shows up in `b3270 --version`; a stale expat has no tell at all.
#
# This script goes into the stamp whole rather than only its configure lines. The flags that shape a prefix
# live here, not in the fetcher, so a stamp covering the fetcher alone answers the CVE bump and misses "I
# dropped no-shared and rebuilt" — the same silent-wrong-library failure by the other door. Hashing the file
# needs no discipline from whoever edits those flags next; the cost is that editing these comments rebuilds
# both prefixes too, which is the safe direction for a guard whose other outcome has no tell.
. "$ROOT/native/build/shared-sha256.sh"
# Contents, not names: the paths here are absolute, and hashing them would restamp every prefix whenever the
# checkout moves. SELF rather than $0 because step 3 cd's into the source tree, and a relative $0 read after
# that would be a stamp of nothing.
SELF=$(cd "$(dirname "$0")" && pwd)/$(basename "$0")
stamp_of() { cat "$1" "$SELF" | sha256_stdin; }
stale() { [ ! -f "$1" ] || [ "$(cat "$2/.pin" 2>/dev/null || true)" != "$3" ]; }

# One shape for both static prefixes, so the .pin rule, the log redirection, the rm -rf ordering and the
# sentinel choice cannot drift apart between them: only the configure line and the comment explaining it
# differ, which is what those comments are actually about.
# build_static <log-and-src-name> <fetcher> <prefix> <sentinel-archive> <make-install-target> <configure...>
build_static() {
  local name=$1 fetcher=$2 prefix=$3 sentinel=$4 install=$5
  shift 5
  local pin src
  pin=$(stamp_of "$ROOT/native/build/$fetcher")
  stale "$prefix/lib/$sentinel" "$prefix" "$pin" || return 0
  src=$("$ROOT/native/build/$fetcher" "$BUILD/$name-src")
  rm -rf "$prefix"
  ( cd "$src" && "$@" && make -j"$JOBS" && make "$install" ) > "$BUILD/$name.log" 2>&1
  printf '%s\n' "$pin" > "$prefix/.pin"
}

# 1. OpenSSL, static only. --libdir=lib is not cosmetic: OpenSSL installs to lib64 on x86_64 by default, and
# pinning it makes the staged layout identical on both architectures and matches what x3270 expects. Because
# no-shared leaves no .so in the prefix at all, the macOS script's "stage only the archives" trick is
# unnecessary here — there is nothing else for the linker to find.
STAGE="$BUILD/openssl-static"
build_static openssl fetch-openssl.sh "$STAGE" libssl.a install_sw \
  ./config no-shared no-tests no-docs --prefix="$STAGE" --libdir=lib

# 2. expat, static only, for the same reason and by the same method. b3270 requires it and offers no way to
# build without it, and unlike macOS — where it lives in /usr/lib and verify-macos.sh allows it as part of that
# OS — Linux has no distribution-independent libexpat. Linking it dynamically would put libexpat.so.1 in the
# gate's dependency list, which is not part of the glibc runtime; static is the answer that keeps the gate's
# claim literally true rather than widening it. --disable-shared leaves only libexpat.a in the prefix, so -lexpat
# below can resolve to nothing else (the image ships /usr/lib64/libexpat.so.1, which -lexpat does not match, and
# expat-devel is deliberately not in the container's PACKAGES).
EXPAT="$BUILD/expat-static"
build_static expat fetch-expat.sh "$EXPAT" libexpat.a install \
  ./configure --disable-shared --without-docbook --without-examples --without-tests \
    --prefix="$EXPAT" --libdir="$EXPAT/lib"

# 3. b3270 against both, with the macOS script's component flags. x3270's configure keeps the CPPFLAGS and
# LDFLAGS it is given and appends OpenSSL's own -I/-L to them (lib/configure.in saves them as orig_CPPFLAGS and
# orig_LDFLAGS), so pointing them at the expat prefix here does not disturb --with-openssl.
#
# LIBS="-ldl -pthread" is REQUIRED, not a hedge. It was once believed to be one, on the evidence that removing
# it still built and still passed the gate. It does — with "checking for CRYPTO_malloc in -lcrypto... no" and
# "configure: WARNING: Disabling TLS -- missing OpenSSL libraries" buried in configure.log, and a b3270 that
# reports "TLS provider: None". Static libcrypto needs -ldl and -pthread at link time, so without them the
# AC_CHECK_LIB probe fails to link and configure concludes OpenSSL is unavailable. The resulting binary is
# smaller and links *fewer* libraries, so checks 1 and 2 of the gate liked it better than the real thing.
# That is why verify-linux.sh's check 3 exists. Verified on arm64, 2026-09-07.
SRC=$("$ROOT/native/build/fetch-source.sh" "$BUILD/src")
cd "$SRC"
./configure --enable-b3270 \
  --disable-x3270 --disable-c3270 --disable-s3270 --disable-tcl3270 \
  --disable-pr3287 --disable-x3270if --disable-mitm --disable-playback \
  --with-openssl="$STAGE" \
  CPPFLAGS="-I$EXPAT/include" LDFLAGS="-L$EXPAT/lib" \
  LIBS="-ldl -pthread" > "$BUILD/configure.log" 2>&1
make -j"$JOBS" > "$BUILD/make.log" 2>&1
# GNU find, so -perm -u+x rather than the macOS script's BSD -perm +111. sed rather than head for the same
# SIGPIPE-under-pipefail reason as verify-linux.sh.
BIN=$(find obj -type f -name b3270 -perm -u+x | sort | sed -n 1p)
OUT="$ROOT/native/out/$RID"
mkdir -p "$OUT"
cp "$BIN" "$OUT/b3270"
chmod +x "$OUT/b3270"
"$ROOT/native/build/verify-linux.sh" "$OUT/b3270"
