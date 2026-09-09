#!/usr/bin/env bash
# This file is part of LizTerm.
# Copyright 2026 by CoffeeMuse
# SPDX-License-Identifier: BSD-3-Clause

# Turns a LIZTERM_WIRE_LOG file into a replay fixture: keeps only the lines b3270 sent (the "<" direction)
# and strips the timestamp and direction prefix, leaving raw b3270 standard output, one JSON indication
# per line, which is what tests/LizTerm.Backend.B3270.Tests/ReplayTests.cs feeds to FakeB3270Process.
# usage: tools/wirelog-to-fixture.sh <wire.log> <out.jsonl>
set -euo pipefail
if [ $# -ne 2 ]; then
  echo "usage: $0 <wire.log> <out.jsonl>" >&2
  exit 2
fi
sed -n 's/^[0-9][0-9:.TZ-]* < //p' "$1" > "$2"
echo "$(wc -l < "$2" | tr -d ' ') indications written to $2"
