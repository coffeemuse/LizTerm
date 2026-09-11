#!/usr/bin/env bash
# This file is part of LizTerm.
# Copyright 2026 by CoffeeMuse
# SPDX-License-Identifier: BSD-3-Clause

# Fails unless a packaged macOS app bundle or disk image is signed by the project's Developer ID team,
# notarized by Apple, and carries its notarization ticket stapled. release.yml runs it on the app inside every
# macOS ZIP and on every DMG, for both RIDs: it executes nothing, so osx-x64 is covered on an arm64 runner.
#
# Three checks, none of which implies another -- each was measured on macOS 26.6 while this was designed (see
# docs/ci-and-release.md, "macOS signing and notarization"):
#
# 1. Team ID. Every Mach-O file in the bundle carries the team's identifier. codesign --verify --deep --strict
#    passes a bundle with an ad hoc sibling library, and an ad hoc sibling is exactly what once stopped every
#    packaged build from launching. A disk image is checked by the certificate its signature names instead:
#    Parcel's signer leaves a disk image's TeamIdentifier unset (measured on the v0.4.1 rehearsal), so for a
#    .dmg the team is the one in parentheses at the end of the signing certificate's name.
# 2. Stapled. xcrun stapler validate. Without a stapled ticket a first launch needs Apple's servers, and a user
#    who is offline is refused. spctl cannot see this: it looks the ticket up online, and reports
#    source=Notarized Developer ID for a bundle whose ticket has been deleted.
# 3. Notarized. spctl must report "accepted" AND source=Notarized Developer ID. A file with no quarantine flag
#    -- which is every file on a CI runner -- is "accepted" with source=Developer ID when it is signed but was
#    never notarized, so a check for "accepted" alone cannot fail on the likeliest regression: Parcel skipping
#    notarization because a credential is missing or misnamed. A failure prints only spctl's verdict and
#    source lines: its origin= line names the certificate's holder, and CI logs are public.
#
# Every check runs and every failure is reported, rather than stopping at the first, so one run shows each way
# a package is wrong -- and so release.yml's "The notarization gate can fail" step can require each check to
# fire by its own message. Exit 1 means a check failed; exit 2 means the arguments were wrong.
set -uo pipefail
if [ "$#" -ne 2 ]; then
  echo "usage: verify-notarized.sh <app-or-dmg> <team-id>" >&2
  exit 2
fi
# A bundle path typed with a trailing slash is still the bundle.
TARGET=${1%/}
TEAM=$2

case "$TARGET" in
  *.app) KIND=app ;;
  *.dmg) KIND=dmg ;;
  *) echo "ERROR: $TARGET is neither a .app bundle nor a .dmg" >&2; exit 2 ;;
esac
if [ ! -e "$TARGET" ]; then
  echo "ERROR: $TARGET does not exist" >&2
  exit 2
fi

FAILED=0
fail() {
  echo "ERROR: $*" >&2
  FAILED=1
}

# codesign -dv reports on stderr. An ad hoc signature says TeamIdentifier=not set; an unsigned file has no
# TeamIdentifier line at all, which comes back empty and is reported as 'none'.
team_of() {
  codesign -dv "$1" 2>&1 | sed -n 's/^TeamIdentifier=//p'
}

# 1. Team ID
if [ "$KIND" = app ]; then
  MACHO=0
  while IFS= read -r -d '' f; do
    # Captured and matched, not piped into grep -q: grep -q exits at its first match and SIGPIPEs the writer,
    # which pipefail turns into a failure -- the trap engines.yml's gate proofs record.
    description=$(file -b "$f")
    case "$description" in
      *Mach-O*) ;;
      *) continue ;;
    esac
    MACHO=$((MACHO + 1))
    team=$(team_of "$f")
    if [ "$team" != "$TEAM" ]; then
      fail "Team ID check: ${f#"$TARGET"/} is signed by team '${team:-none}', not $TEAM"
    fi
  done < <(find "$TARGET" -type f -print0)
  if [ "$MACHO" -eq 0 ]; then
    fail "Team ID check: no Mach-O file under $TARGET"
  fi
else
  # The leaf certificate is the first Authority line, and its name ends in "(<team id>)". Only the team ID is
  # ever printed: the rest of the name identifies the certificate's holder.
  details=$(codesign -dvv "$TARGET" 2>&1)
  leaf=$(printf '%s\n' "$details" | sed -n 's/^Authority=//p' | sed -n 1p)
  if [ -z "$leaf" ]; then
    fail "Team ID check: $TARGET carries no signing certificate (ad hoc or unsigned)"
  elif [ "$leaf" = "${leaf%"($TEAM)"}" ]; then
    fail "Team ID check: $TARGET is signed by a certificate that does not name team $TEAM"
  fi
fi

# 2. Stapled
if ! stapled=$(xcrun stapler validate "$TARGET" 2>&1); then
  # stapler's last line can be its "Processing: <path>" banner rather than the reason (an ad hoc copy of a
  # stapled app prints nothing else), so the banner is skipped.
  reason=$(printf '%s\n' "$stapled" | grep -v '^Processing:' | tail -n 1)
  fail "stapling check: xcrun stapler validate rejects $TARGET: ${reason:-no reason given}"
fi

# 3. Notarized
if [ "$KIND" = app ]; then
  assessment=$(spctl -a -vvv -t exec "$TARGET" 2>&1)
else
  assessment=$(spctl -a -vvv -t open --context context:primary-signature "$TARGET" 2>&1)
fi
case "$assessment" in
  *": accepted"*"source=Notarized Developer ID"*) ;;
  *)
    verdict=$(printf '%s\n' "$assessment" | grep -E ': (accepted|rejected)|^source=' | tr '\n' ' ')
    fail "notarization check: spctl does not report an accepted, notarized $KIND: ${verdict:-no verdict}"
    ;;
esac

if [ "$FAILED" -ne 0 ]; then
  exit 1
fi
echo "OK: $TARGET is signed by team $TEAM, notarized, and stapled"
