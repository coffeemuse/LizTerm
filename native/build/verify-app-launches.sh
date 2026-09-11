#!/usr/bin/env bash
# This file is part of LizTerm.
# Copyright 2026 by CoffeeMuse
# SPDX-License-Identifier: BSD-3-Clause

# Fails if the packaged app does not survive a few real seconds after being launched the way a user launches
# it. Nothing else in this pipeline performs this check, and the gap is structural, not an oversight:
# verify-bundled-engine.sh and shared-verify-tls.sh both prove the *engine* is right and starts, but b3270 is
# a child process LizTerm spawns, never something it dlopen's, so it is immune to an entire class of failure.
# A packaged macOS build once passed codesign --verify --deep --strict and every other gate in this pipeline,
# then refused to launch for every user: Parcel ad-hoc signed the packaged executable with the hardened
# runtime while signing the bundled libSkiaSharp/libHarfBuzzSharp/libAvaloniaNative dylibs plain ad-hoc, the
# hardened runtime's library validation then refused to dlopen a sibling with no matching Team ID, and
# Avalonia died during Skia initialisation before a window ever opened. It was found by launching the app by
# hand. This script is that manual check, automated: it starts the real, shipped executable -- no headless
# platform, no self-test flag, because either one would skip past Skia initialisation, the exact place the
# bug was -- and proves it is still running a few seconds later. See docs/ci-and-release.md, "macOS signing
# and notarization", for the full incident.
set -euo pipefail
DIR=${1:?usage: verify-app-launches.sh <extracted-package-dir> <rid>}
RID=${2:?usage: verify-app-launches.sh <extracted-package-dir> <rid>}

# The app's assembly name is LizTerm.App -- LizTerm.App.csproj sets no AssemblyName -- and PublishSingleFile
# means one file with no extra platform stem. Parcel's GeneralSettings.PackageName (docs/ci-and-release.md,
# "Packaging with Parcel") renames the outer .app bundle, the Dock icon and the DMG, never the executable nested inside
# Contents/MacOS; measured by unzipping a packed bundle and reading it directly, so this stays LizTerm.App on
# every platform except the one where Windows itself demands the extension.
case "$RID" in
  osx-arm64|osx-x64|linux-x64|linux-arm64) NAME=LizTerm.App ;;
  win-x64|win-arm64)                       NAME=LizTerm.App.exe ;;
  *) echo "ERROR: unknown rid $RID" >&2; exit 1 ;;
esac

# Searched at any depth, the same reason verify-bundled-engine.sh searches at any depth: this runs against an
# EXTRACTED PACKAGE, and only the macOS shape nests the executable, under LizTerm.app/Contents/MacOS/. Exactly
# one match is required -- zero means nothing shipped, and several means the package holds more than one copy.
FOUND=$(find "$DIR" -type f -name "$NAME" 2>/dev/null | sort)
COUNT=$(printf '%s' "$FOUND" | grep -c . || true)
if [ "$COUNT" -eq 0 ]; then
  echo "ERROR: no packaged executable named $NAME under $DIR" >&2; exit 1
elif [ "$COUNT" -gt 1 ]; then
  echo "ERROR: $COUNT packaged executables named $NAME under $DIR, expected 1:" >&2; printf '%s\n' "$FOUND" >&2; exit 1
fi
BIN=$FOUND

# Windows has no executable bit to lose. Everywhere else, restore it defensively -- release.yml already does
# this for the downloaded engine artifact before it is published, for the same reason: an archive round trip
# is what a lost bit costs, and without this a lost bit would surface here as a confusing "Permission denied"
# instead of the launch failure the rest of this script exists to diagnose.
case "$RID" in
  win-*) ;;
  *) chmod +x "$BIN" ;;
esac

OUT=$(mktemp)
ERR=$(mktemp)
trap 'rm -f "$OUT" "$ERR"' EXIT

# Launched from the executable's own directory, the way a double-click, a Dock icon, or a Start Menu shortcut
# launches it -- not that anything today depends on the current directory (B3270Locator and the embedded
# AvaloniaResource assets both resolve off AppContext.BaseDirectory), but a real launch is the point, so this
# does not take a shortcut a real launch would not take either.
BINDIR=$(dirname "$BIN")
BINNAME=$(basename "$BIN")
cd "$BINDIR"
"./$BINNAME" >"$OUT" 2>"$ERR" &
PID=$!
cd - >/dev/null

# The crash this gate exists to catch took about a second. A tight polling loop measures nothing -- it would
# just as happily pass a process still in the middle of dying -- so this sleeps a few real seconds, ample
# beside that failure's timing, and then makes exactly one liveness check.
SURVIVAL_SECONDS=5
sleep "$SURVIVAL_SECONDS"

if kill -0 "$PID" 2>/dev/null; then
  # Still running after a real few seconds: kill it so the job does not hang on a GUI event loop that a
  # human, not CI, is meant to close.
  kill "$PID" 2>/dev/null || true
  sleep 1
  kill -9 "$PID" 2>/dev/null || true
  wait "$PID" 2>/dev/null || true
  echo "OK: $BIN was still running $SURVIVAL_SECONDS s after launch"
  exit 0
fi

# It is already gone. wait reaps it and reports how, the same shell built-in the "still running" branch above
# uses after its own kill.
if wait "$PID" 2>/dev/null; then STATUS=0; else STATUS=$?; fi
echo "ERROR: $BIN did not survive $SURVIVAL_SECONDS s after launch (exit $STATUS). Captured stderr:" >&2
cat "$ERR" >&2
if [ -s "$OUT" ]; then
  echo "ERROR: captured stdout:" >&2
  cat "$OUT" >&2
fi
exit 1
