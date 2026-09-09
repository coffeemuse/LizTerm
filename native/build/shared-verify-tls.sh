#!/usr/bin/env bash
# Fails if the binary does not report the expected TLS provider (OpenSSL unless a second argument says
# otherwise), and prints the first three banner lines when it does. Shared by verify-linux.sh,
# verify-macos.sh and the test-windows CI job: the failure it catches is x3270's, not any one platform's,
# which is why Windows joins it by naming its provider rather than by getting a gate of its own.
#
# Nothing about what a binary *links* notices this, and a link-only gate gets *happier* without TLS: x3270's
# configure probes OpenSSL by linking a test program, and when that probe fails it prints one warning, builds a
# b3270 reporting "TLS provider: None", and that binary links fewer libraries and passes the dependency checks
# with room to spare. Measured on Linux, 2026-09-07: dropping LIBS="-ldl -pthread" from build-linux.sh's
# configure line does exactly this, and the gate passed the result. LizTerm without TLS cannot reach a TLS host
# at all, and plan 3b's whole trust story would have nothing to verify with, so a binary that links beautifully
# and cannot do TLS is a worse outcome than one that fails to link.
set -euo pipefail
BIN=${1:?usage: shared-verify-tls.sh <binary> [expected-provider]}
# Defaults to OpenSSL so verify-linux.sh and verify-macos.sh, which pass one argument, are unchanged.
# Windows is why this is a parameter at all: its provider is Schannel, and the binary is correct.
EXPECTED=${2:-OpenSSL}

# 2>&1 because b3270 writes its whole banner to stderr. Captured rather than piped so that the exit status is
# available on its own: an engine that cannot start at all — wrong architecture, a truncated copy, a missing
# loader — is a different fault from one built without TLS, and reporting it as the latter sends the reader off
# to edit a configure line that is not the problem.
if VERSION=$("$BIN" --version 2>&1); then STATUS=0; else STATUS=$?; fi
if [ "$STATUS" -ne 0 ]; then
  echo "ERROR: $BIN --version exited $STATUS. The engine did not run; this is not a TLS fault." >&2
  printf '%s\n' "$VERSION" | sed -n '1,5p' >&2
  exit 1
fi

case "$VERSION" in
  *"TLS provider: $EXPECTED"*) ;;
  *)
    echo "ERROR: $BIN does not report the expected TLS provider ($EXPECTED):" >&2
    printf '%s\n' "$VERSION" | sed -n '1,3p' >&2
    if [ "$EXPECTED" = "OpenSSL" ]; then
      echo "x3270's configure disables TLS when its -lcrypto link probe fails. On Linux that probe needs the" >&2
      echo "LIBS on build-linux.sh's configure line (see step 3 there); on macOS it needs a usable static OpenSSL" >&2
      echo "build in the prefix build-macos.sh stages from the pinned tarball fetch-openssl.sh names." >&2
    else
      echo "The Windows build reaches Schannel through -lcrypt32 -lsecur32 on wb3270's LIBS line, with no" >&2
      echo "configure probe to fail. A provider of None here means the source tree changed shape; a provider of" >&2
      echo "OpenSSL means the build found a cryptographic library it should not have." >&2
    fi
    exit 1 ;;
esac

# sed, not head: head closes the pipe once it has its lines, GNU coreutils SIGPIPEs the writer for it, and
# under pipefail that 141 becomes this script's exit status — a passing binary reported as a failed gate.
printf '%s\n' "$VERSION" | sed -n '1,3p'
