#!/usr/bin/env bash
# This file is part of LizTerm.
# Copyright 2026 by CoffeeMuse
# SPDX-License-Identifier: BSD-3-Clause
#
# Records one mvsMF exchange as a test fixture in tests/LizTerm.Backend.Mvsmf.Tests/Fixtures/<name>.http: the
# response headers with CRs removed (Date, Jobname, Jobid and Node dropped; the LtpaToken2 cookie's value replaced
# by <token>), a blank line, then the body bytes untouched. The body is never passed through tr, because binary
# records can hold 0x0D.
#
# Needs LIZTERM_MVSMF_URL (the base, ending in /zosmf), LIZTERM_MVSMF_USER and LIZTERM_MVSMF_PASSWORD. The
# credentials reach curl through a netrc on a file descriptor, never the command line. MVSMF_BAD_PASSWORD=1 sends a
# wrong password instead, to record a 401.
#
#   tools/record-mvsmf-fixture.sh <name> <METHOD> <path-under-zosmf> [extra curl args...]
set -euo pipefail

if [[ $# -lt 3 ]]; then
  echo "usage: $0 <name> <METHOD> <path-under-zosmf> [curl args...]" >&2
  exit 2
fi
name=$1 method=$2 path=$3
shift 3
: "${LIZTERM_MVSMF_URL:?set LIZTERM_MVSMF_URL}" "${LIZTERM_MVSMF_USER:?set LIZTERM_MVSMF_USER}" "${LIZTERM_MVSMF_PASSWORD:?set LIZTERM_MVSMF_PASSWORD}"

out="$(cd "$(dirname "$0")/.." && pwd)/tests/LizTerm.Backend.Mvsmf.Tests/Fixtures/$name.http"
host=$(printf '%s' "$LIZTERM_MVSMF_URL" | sed -E 's#^[A-Za-z]+://([^/:]+).*#\1#')
password=$LIZTERM_MVSMF_PASSWORD
if [[ -n "${MVSMF_BAD_PASSWORD:-}" ]]; then password=not-the-password; fi

tmp=$(mktemp -d)
trap 'rm -rf "$tmp"' EXIT
touch "$tmp/body"
curl -sS --netrc-file <(printf 'machine %s login %s password %s\n' "$host" "$LIZTERM_MVSMF_USER" "$password") \
  -X "$method" -D "$tmp/headers" -o "$tmp/body" "$@" "${LIZTERM_MVSMF_URL%/}/$path"
{ tr -d '\r' < "$tmp/headers" | grep -viE '^(date|jobname|jobid|node):' | sed -E 's/^(Set-Cookie: LtpaToken2=)[^;]*/\1<token>/I'; cat "$tmp/body"; } > "$out"
echo "$name: $(head -1 "$out")"
