#!/usr/bin/env bash
# Builds b3270 for the host architecture against static OpenSSL archives.
# Requires: Xcode command line tools, Homebrew openssl@3 (or OPENSSL_PREFIX).
set -euo pipefail
ROOT=$(cd "$(dirname "$0")/../.." && pwd)
ARCH=$(uname -m)                     # arm64 or x86_64
case "$ARCH" in
  arm64)  RID=osx-arm64 ;;
  x86_64) RID=osx-x64 ;;
  *) echo "unsupported arch $ARCH" >&2; exit 1 ;;
esac
BUILD="$ROOT/native/build-tmp/$RID"
SRC=$("$ROOT/native/build/fetch-source.sh" "$BUILD/src")
OPENSSL_PREFIX=${OPENSSL_PREFIX:-$(brew --prefix openssl@3)}
# Stage a directory that contains ONLY static archives so the linker cannot pick dylibs.
STAGE="$BUILD/openssl-static"
rm -rf "$STAGE"
mkdir -p "$STAGE/lib"
ln -s "$OPENSSL_PREFIX/include" "$STAGE/include"
cp "$OPENSSL_PREFIX/lib/libssl.a" "$OPENSSL_PREFIX/lib/libcrypto.a" "$STAGE/lib/"
cd "$SRC"
./configure --enable-b3270 \
  --disable-x3270 --disable-c3270 --disable-s3270 --disable-tcl3270 \
  --disable-pr3287 --disable-x3270if --disable-mitm --disable-playback \
  --with-openssl="$STAGE" > "$BUILD/configure.log" 2>&1
make -j"$(sysctl -n hw.ncpu)" > "$BUILD/make.log" 2>&1
BIN=$(find obj -type f -name b3270 -perm +111 | head -1)
OUT="$ROOT/native/out/$RID"
mkdir -p "$OUT"
cp "$BIN" "$OUT/b3270"
chmod +x "$OUT/b3270"
"$ROOT/native/build/verify-macos.sh" "$OUT/b3270"
