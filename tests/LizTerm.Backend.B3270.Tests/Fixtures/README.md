# Replay fixtures

Each `.jsonl` file is raw b3270 standard output (one JSON indication per line), recorded with
`tools/record-fixture.sh` by replaying an x3270 test trace through b3270 4.5ga6.
The traces come from the x3270 source distribution (BSD-3-Clause, Copyright Paul Mattes).

- `ibmlink-help.jsonl`: `s3270/Test/ibmlink_help.trc`, model 3279-2-E, recorded with playback step
  `4r` (four "step record" commands, not the default "e"/play-to-EOF -- this trace's full session
  has three screens back to back, and playback's `e` will push all of them without waiting for a
  real client keystroke, overshooting past the first screen). A full 24x80 IBMLink welcome screen
  with highlighted/selectable regions; cursor ends at row 21, column 13 (1-based). The host
  negotiates TN3270E from the start of the session, so the connection never passes through the
  plain `connected-3270` state -- it settles on `connected-unbound`/`connected-tn3270e`.
