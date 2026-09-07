#!/usr/bin/env bash
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
JOBS=$(nproc)

# 1. OpenSSL, static only. --libdir=lib is not cosmetic: OpenSSL installs to lib64 on x86_64 by default, and
# pinning it makes the staged layout identical on both architectures and matches what x3270 expects. Because
# no-shared leaves no .so in the prefix at all, the macOS script's "stage only the archives" trick is
# unnecessary here — there is nothing else for the linker to find.
STAGE="$BUILD/openssl-static"
if [ ! -f "$STAGE/lib/libssl.a" ]; then
  SSL_SRC=$("$ROOT/native/build/fetch-openssl.sh" "$BUILD/openssl-src")
  rm -rf "$STAGE"
  ( cd "$SSL_SRC" \
    && ./config no-shared no-tests no-docs --prefix="$STAGE" --libdir=lib \
    && make -j"$JOBS" \
    && make install_sw ) > "$BUILD/openssl.log" 2>&1
fi

# 2. expat, static only, for the same reason and by the same method. b3270 requires it and offers no way to
# build without it, and unlike macOS — where it lives in /usr/lib and verify-macos.sh allows it as part of that
# OS — Linux has no distribution-independent libexpat. Linking it dynamically would put libexpat.so.1 in the
# gate's dependency list, which is not part of the glibc runtime; static is the answer that keeps the gate's
# claim literally true rather than widening it. --disable-shared leaves only libexpat.a in the prefix, so -lexpat
# below can resolve to nothing else (the image ships /usr/lib64/libexpat.so.1, which -lexpat does not match, and
# expat-devel is deliberately not in the container's PACKAGES).
EXPAT="$BUILD/expat-static"
if [ ! -f "$EXPAT/lib/libexpat.a" ]; then
  EXPAT_SRC=$("$ROOT/native/build/fetch-expat.sh" "$BUILD/expat-src")
  rm -rf "$EXPAT"
  ( cd "$EXPAT_SRC" \
    && ./configure --disable-shared --without-docbook --without-examples --without-tests \
         --prefix="$EXPAT" --libdir="$EXPAT/lib" \
    && make -j"$JOBS" \
    && make install ) > "$BUILD/expat.log" 2>&1
fi

# 3. b3270 against both, with the macOS script's component flags. x3270's configure keeps the CPPFLAGS and
# LDFLAGS it is given and appends OpenSSL's own -I/-L to them (lib/configure.in saves them as orig_CPPFLAGS and
# orig_LDFLAGS), so pointing them at the expat prefix here does not disturb --with-openssl.
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
