#!/usr/bin/env bash
# Builds b3270 for one macOS architecture against a statically built OpenSSL.
# Requires: Xcode command line tools. Homebrew is NOT required -- OpenSSL comes from the tarball
# fetch-openssl.sh pins, the same one build-linux.sh uses.
# usage: build-macos.sh [osx-arm64|osx-x64]   (default: the host's architecture)
set -euo pipefail
ROOT=$(cd "$(dirname "$0")/../.." && pwd)
case "$(uname -m)" in
  arm64)  HOST_RID=osx-arm64 ;;
  x86_64) HOST_RID=osx-x64 ;;
  *) echo "unsupported arch $(uname -m)" >&2; exit 1 ;;
esac
RID=${1:-$HOST_RID}
case "$RID" in
  osx-arm64) ARCH=arm64;  TRIPLE=aarch64-apple-darwin; OPENSSL_TARGET=darwin64-arm64-cc ;;
  osx-x64)   ARCH=x86_64; TRIPLE=x86_64-apple-darwin;  OPENSSL_TARGET=darwin64-x86_64-cc ;;
  *) echo "usage: build-macos.sh [osx-arm64|osx-x64]" >&2; exit 1 ;;
esac
BUILD="$ROOT/native/build-tmp/$RID"
mkdir -p "$BUILD"
JOBS=$(sysctl -n hw.ncpu)

# The .pin stamp machinery build-linux.sh documents at length, for the same reason and with the same rule:
# a prefix is reused only when both the archive and a matching stamp are there. A prefix has two owners --
# the fetcher holds the version and the checksum, this script holds the configure flags that shape what is
# built from them -- so the stamp covers both files, and this script goes into it whole rather than only its
# configure lines. Editing these comments rebuilds the prefix, which is the safe direction for a guard whose
# other outcome (silently linking the old library, and skipping the new checksum too) has no tell.
. "$ROOT/native/build/shared-sha256.sh"
# SELF rather than $0: step 3 cd's into the source tree, and a relative $0 read after that stamps nothing.
SELF=$(cd "$(dirname "$0")" && pwd)/$(basename "$0")
PIN=$(cat "$ROOT/native/build/fetch-openssl.sh" "$SELF" | sha256_stdin)

# 1. OpenSSL, static only, into a per-architecture prefix. --libdir=lib because OpenSSL installs to lib64 on
# x86_64 by default and x3270 expects lib; no-shared leaves no dylib in the prefix at all, which is what the
# old Homebrew staging directory existed to fake.
STAGE="$BUILD/openssl-static"
if [ ! -f "$STAGE/lib/libssl.a" ] || [ "$(cat "$STAGE/.pin" 2>/dev/null || true)" != "$PIN" ]; then
  SSL_SRC=$("$ROOT/native/build/fetch-openssl.sh" "$BUILD/openssl-src")
  rm -rf "$STAGE"
  ( cd "$SSL_SRC" \
    && ./Configure "$OPENSSL_TARGET" no-shared no-tests no-docs --prefix="$STAGE" --libdir=lib \
    && make -j"$JOBS" \
    && make install_sw ) > "$BUILD/openssl.log" 2>&1
  printf '%s\n' "$PIN" > "$STAGE/.pin"
fi

# 2. b3270. expat is NOT built here: macOS ships libexpat in /usr/lib, which verify-macos.sh allows as part
# of the OS. That is the difference from build-linux.sh, where no distribution-independent libexpat exists
# and a dynamic one would land in the gate's rejection list.
#
# --host is passed even for a native build so both architectures take the same configure path, and so a cross
# build does not depend on the host being able to RUN an x86_64 test binary -- autoconf uses cross defaults
# instead of Rosetta, which may not be installed on a runner.
SRC=$("$ROOT/native/build/fetch-source.sh" "$BUILD/src")
cd "$SRC"
./configure --enable-b3270 \
  --host="$TRIPLE" \
  --disable-x3270 --disable-c3270 --disable-s3270 --disable-tcl3270 \
  --disable-pr3287 --disable-x3270if --disable-mitm --disable-playback \
  --with-openssl="$STAGE" \
  CC="clang -arch $ARCH" \
  > "$BUILD/configure.log" 2>&1
make -j"$JOBS" > "$BUILD/make.log" 2>&1
# BSD -perm +111 here, GNU -perm -u+x in build-linux.sh. sort, so the pick is deterministic when the tree
# holds more than one match; sed rather than head, because head closes the pipe after its line, find takes
# SIGPIPE for it, and under pipefail that 141 becomes this script's exit status -- a good build aborted with
# no message. Same reasoning as build-linux.sh.
BIN=$(find obj -type f -name b3270 -perm +111 | sort | sed -n 1p)
OUT="$ROOT/native/out/$RID"
mkdir -p "$OUT"
cp "$BIN" "$OUT/b3270"
chmod +x "$OUT/b3270"
"$ROOT/native/build/verify-macos.sh" "$OUT/b3270" "$ARCH"
