#!/usr/bin/env bash
# This file is part of LizTerm.
# Copyright 2026 by CoffeeMuse
# SPDX-License-Identifier: BSD-3-Clause

# Fails if the binary is not a 64-bit PE executable, or if it imports a DLL outside the Windows system set.
# Runs INSIDE the build container (it needs that container's mingw objdump).
set -euo pipefail
BIN=${1:?usage: verify-windows.sh <binary.exe>}
OBJDUMP=${OBJDUMP:-x86_64-w64-mingw32-objdump}

# 1. The right machine. A 32-bit build is a plausible accident -- i686-w64-mingw32 is a target the same source
# tree supports and the same configure would accept -- and it would produce a binary that runs on the developer's
# Windows box and refuses to load in a win-x64 .NET process. Nothing below would notice: a 32-bit build imports
# the same system DLLs.
ARCH=$("$OBJDUMP" -f "$BIN" | sed -n 's/^architecture: *\([^,]*\).*/\1/p')
if [ "$ARCH" != "i386:x86-64" ]; then
  echo "ERROR: $BIN is $ARCH, not i386:x86-64. Was --host=x86_64-w64-mingw32 passed to configure?" >&2
  exit 1
fi

# 2. Every imported DLL must be one Windows ships. This is verify-linux.sh's check 1 spelled for PE: a
# libwinpthread-1.dll or libgcc_s_seh-1.dll here means the engine will not start on a machine without the
# MinGW runtime beside it, and anything cryptographic would mean the build found a library it should not have.
# Matched case-insensitively because mingw emits WINSPOOL.DRV, Secur32.dll and comdlg32.dll in one binary, and
# a case-sensitive list is a trap for whoever extends it. winspool.drv is why the pattern is not just "\.dll".
ALLOWED='^(advapi32\.dll|crypt32\.dll|gdi32\.dll|kernel32\.dll|shell32\.dll|secur32\.dll|user32\.dll|winspool\.drv|ws2_32\.dll|comdlg32\.dll|msvcrt\.dll)$'
IMPORTS=$("$OBJDUMP" -p "$BIN" | awk '/DLL Name:/ {print tolower($3)}' | sort -u)
if [ -z "$IMPORTS" ]; then
  echo "ERROR: $BIN imports no DLLs at all. Is it a PE executable with an import table?" >&2
  exit 1
fi
BAD=$(printf '%s\n' "$IMPORTS" | grep -v -E "$ALLOWED" || true)
if [ -n "$BAD" ]; then
  echo "ERROR: $BIN imports DLLs outside the Windows system set:" >&2
  printf '%s\n' "$BAD" >&2
  exit 1
fi

# There is deliberately no analogue of verify-linux.sh's check 2 (the highest imported glibc symbol version):
# Windows import tables carry no symbol versioning, and the API surface b3270 uses predates every Windows
# version LizTerm supports.

# 3. crypt32.dll and secur32.dll must BOTH be among the imports. Common/Win32/sio_schannel.c is what references
# the Schannel APIs -- AcquireCredentialsHandle and the rest of SSPI come from secur32.dll, with crypt32.dll
# supplying the certificate-store calls beside them -- so an engine built without it stops importing them,
# secur32.dll above all. Check 2 above is a permit list, not a require list: it is satisfied by an import table
# missing both of these just as completely as by one that has them, because a b3270.exe with no Schannel simply
# imports fewer of the DLLs already on the allowlist. That is this project's hardest-won lesson, restated for
# PE: a link-only gate gets *happier* without TLS, and Windows is the one platform whose own build never calls
# shared-verify-tls.sh to catch it locally the way build-macos.sh and build-linux.sh do.
#
# This check is a proxy, not the real thing, and cannot be more than that: an import can prove only that the
# object code implementing Schannel was linked in, never that the provider initialises at runtime. The check
# that can prove that -- reading the actual "TLS provider: ..." line the binary prints -- needs a machine that
# can run a PE executable, which this container is not; it still runs, unconditionally, before anything is
# published: shared-verify-tls.sh, called from the test-windows job once a Windows runner has the binary. What
# this arm buys is catching the same class of mistake here, on every build, rather than only in the one CI job
# with a Windows machine to ask.
MISSING=""
printf '%s\n' "$IMPORTS" | grep -qxF 'crypt32.dll' || MISSING="$MISSING crypt32.dll"
printf '%s\n' "$IMPORTS" | grep -qxF 'secur32.dll' || MISSING="$MISSING secur32.dll"
if [ -n "$MISSING" ]; then
  echo "ERROR: $BIN does not import:$MISSING -- this binary has no Schannel TLS." >&2
  exit 1
fi

# 4. The engine must carry LizTerm's patches (native/patches). Last, so every arm above keeps the reject fixture it
# was built for: engines.yml proves this one with a copy of the real engine whose marker has been defaced.
"$(dirname "$0")/shared-verify-patches.sh" "$BIN"

echo "OK: $BIN is a 64-bit PE importing only Windows system DLLs, Schannel included, carrying LizTerm's patches"
printf '%s\n' "$IMPORTS"
