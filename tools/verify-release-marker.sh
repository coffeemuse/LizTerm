#!/usr/bin/env bash
# This file is part of LizTerm.
# Copyright 2026 by CoffeeMuse
# SPDX-License-Identifier: BSD-3-Clause
#
# Fails unless the assembly a publish step just built carries exactly the release marker it should: present on a
# release, absent on anything else. About reads that marker, so a release that lacks it would call itself a
# development build and a test build that carries one would pass for a release, and the manual pass cannot catch
# either -- it runs on rehearsal artifacts. See docs/ci-and-release.md, "Release builds and test builds".
#
# Usage: tools/verify-release-marker.sh <rid> <version> release|test
#
# It reads `bin/`, the ordinary build output `parcel pack --no-build` consumes, because the `publish/` tree is
# single-file and keeps the assembly inside the host. The target framework is globbed rather than spelled, so a
# TFM bump does not silently turn this into a check of nothing.
set -euo pipefail

rid=${1:?usage: verify-release-marker.sh <rid> <version> release|test}
version=${2:?usage: verify-release-marker.sh <rid> <version> release|test}
expected=${3:?usage: verify-release-marker.sh <rid> <version> release|test}

case "$expected" in
  release|test) ;;
  *) echo "expected must be 'release' or 'test', not '$expected'" >&2; exit 1 ;;
esac

dll=$(find src/LizTerm.App/bin/Release -type f -path "*/$rid/LizTerm.App.dll" | head -1 || true)
[ -n "$dll" ] || { echo "no LizTerm.App.dll built for $rid to read the release marker from" >&2; exit 1; }

# A positive control first: absence proves nothing about a file this check failed to recognise, so the assembly
# must be one built for this run before "the marker is not in it" is allowed to mean anything.
grep -aqF -- "$version" "$dll" || {
  echo "$dll does not carry version $version, so it is not this run's build" >&2
  exit 1
}

# The literal AppVersion.ReleaseMarkerKey, which the SDK writes into the assembly's AssemblyMetadata blob.
if grep -aqF -- 'LizTerm.ReleaseBuild' "$dll"; then found=release; else found=test; fi

if [ "$found" != "$expected" ]; then
  echo "expected a $expected build for $rid, but $dll is marked as a $found build" >&2
  echo "a release is published with -p:LizTermReleaseBuild=true and nothing else is" >&2
  exit 1
fi

if [ "$found" = "release" ]; then
  echo "$rid: release build at version $version, so About shows the bare version"
else
  echo "$rid: test build at version $version, so About shows -DEV"
fi
