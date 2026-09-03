#!/usr/bin/env bash
# Replays an x3270 .trc host trace through b3270 and saves b3270's JSON output as a replay fixture.
# usage: tools/record-fixture.sh <trace.trc> <out.jsonl> [model, default 3279-2-E] [playback step, default e]
#
# The playback step is the command playback(1) reads from stdin to advance through the trace
# (see native/build-tmp/playback/src/*/Common/playback.c process_command()): "e" plays to EOF,
# "<n>r" steps <n> records. "e" plays the whole trace including any host pushes that in the
# original recording only followed a later client keystroke (playback doesn't require the
# client to actually send that keystroke before advancing) -- for a trace with multiple
# screens back to back, that can run past the point you want to capture. Pass an explicit step
# like "4r" to stop right after the first screen paint instead.
set -euo pipefail
TRACE=${1:?trace file}
OUT=${2:?output jsonl}
MODEL=${3:-3279-2-E}
STEP=${4:-e}
ROOT=$(cd "$(dirname "$0")/.." && pwd)
PLAYBACK="$ROOT/native/build-tmp/playback/playback"
[ -x "$PLAYBACK" ] || "$ROOT/native/build/build-playback.sh" > /dev/null
B3270=${LIZTERM_B3270_PATH:-}
[ -n "$B3270" ] || B3270=$(ls "$ROOT"/native/out/*/b3270 2>/dev/null | head -1)
[ -n "$B3270" ] || B3270=$(command -v b3270)
[ -n "$B3270" ] || { echo "no b3270 found; build it or set LIZTERM_B3270_PATH" >&2; exit 1; }
PORT=$((20000 + RANDOM % 10000))
( (sleep 3; echo "$STEP"; sleep 6; echo q) | "$PLAYBACK" -w -p "$PORT" "$TRACE" > /dev/null 2>&1 & )
sleep 0.5
( printf '%s\n' "{\"run\":{\"r-tag\":\"connect\",\"actions\":[{\"action\":\"Open\",\"args\":[\"127.0.0.1:$PORT\"]}]}}"
  sleep 10
  printf '%s\n' '{"run":{"r-tag":"quit","actions":[{"action":"Quit"}]}}' ) \
  | "$B3270" -json -utf8 -model "$MODEL" > "$OUT"
echo "Recorded $(wc -l < "$OUT") lines to $OUT"
