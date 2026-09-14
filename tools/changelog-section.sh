#!/usr/bin/env bash
# This file is part of LizTerm.
# Copyright 2026 by CoffeeMuse
# SPDX-License-Identifier: BSD-3-Clause

# Prints one release's entries from CHANGELOG.md: the lines under a heading reading exactly "## <version>", up to
# the next "## " heading, without the blank lines around them. Fails, saying why, when there is no such heading or
# nothing under it. release.yml's version job runs it to hold back a tag whose entries are missing, and its release
# job runs it again to copy them into the release notes.
# usage: tools/changelog-section.sh <version> [changelog]
set -euo pipefail
if [ $# -lt 1 ] || [ $# -gt 2 ]; then
  echo "usage: $0 <version> [changelog]" >&2
  exit 2
fi
version=$1
changelog=${2:-CHANGELOG.md}
if [ ! -f "$changelog" ]; then
  echo "$changelog does not exist" >&2
  exit 1
fi

section=$(awk -v heading="## $version" '
  $0 == heading { found = 1; next }
  found && /^## / { exit }
  found && !started && /^[[:space:]]*$/ { next }
  found { started = 1; print }
' "$changelog")

if [ -z "$section" ]; then
  echo "$changelog has no entries under a \"## $version\" heading" >&2
  exit 1
fi
printf '%s\n' "$section"
