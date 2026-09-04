# Replay fixtures

Each `.jsonl` file is raw b3270 standard output (one JSON indication per line). Two recorders exist:
`tools/record-fixture.sh` replays an x3270 `.trc` host trace through b3270 4.5ga6, and
`tools/wirelog-to-fixture.sh` trims a `LIZTERM_WIRE_LOG` file from a real session down to the lines b3270 sent.
The traces come from the x3270 source distribution (BSD-3-Clause, Copyright Paul Mattes).

- `ibmlink-help.jsonl`: `s3270/Test/ibmlink_help.trc`, model 3279-2-E, recorded with playback step
  `4r` (four "step record" commands, not the default "e"/play-to-EOF -- this trace's full session
  has three screens back to back, and playback's `e` will push all of them without waiting for a
  real client keystroke, overshooting past the first screen). A full 24x80 IBMLink welcome screen
  with highlighted/selectable regions; cursor ends at row 21, column 13 (1-based). The host
  negotiates TN3270E from the start of the session, so the connection never passes through the
  plain `connected-3270` state -- it settles on `connected-unbound`/`connected-tn3270e`.
- `gateway-login-tls.jsonl`: wire log of a real LizTerm session on 2026-09-04 against a hobbyist TN3270
  gateway over TLS 1.3 with a self-signed certificate (`verifyHostCert` off), model 3279-2-E. The client
  connected, received the 24x80 "TN3270 GATEWAY LOGIN" screen with the cursor in the User ID field
  (row 23, column 17, 1-based), pressed Tab (cursor to the Password field, column 46), then PF3, which
  makes that host drop the line. Covers what `ibmlink-help.jsonl` does not: the `tls` indication with
  certificate text, the plain `connected-3270` state (this host never negotiates TN3270E), and a
  host-initiated disconnect (`tls` reverts to insecure, then `not-connected`). The server's address was
  replaced with `gateway.test` before committing; nothing else was edited.
