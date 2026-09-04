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
- `indfile-tso-roundtrip.jsonl`: inbound lines of a real LizTerm session on 2026-09-04 against an MVS 3.8j (MVS/CE)
  host over plain TN3270, model 3279-2-E, trimmed to the two IND$FILE transfers of the integration lane's
  round trip: a text-mode send of a five-line file to `LIZTERM.ITEST` from the TSO READY prompt, the READY that
  follows, and a text-mode receive of the same dataset. Shows the `oia` `file-transfer` lock, the `ft` sequence
  `awaiting`, `running` (with `bytes`), `complete` (with `success` and `text`), and the `run-result` of each
  Transfer run, which b3270 sends only when the transfer ends and which carries the same text as `complete`.
  The logon and logoff that surround the transfers were cut, so no credentials and no host address are in the
  file. The observed logon flow on this host: the Hercules TN3270 server answers the connection with a banner
  of its own ("CLEAR the screen or hit ENTER", plus help text that mentions the word "logon") and holds it
  until a key is pressed; ENTER then brings up the MVS/CE splash screen whose only input field is
  `TSO Logon ===>`; `LOGON <user>` is answered with `ENTER CURRENT PASSWORD FOR <user>-`; the password is
  answered with the "WELCOME TO MVS COMMUNITY EDITION" banner ending in `READY`, with no menu and no `***`
  pause in between. TSO keeps writing for a moment after `READY` appears, and IND$FILE typed into a
  half-painted screen reaches the host as garbage (`INVALID COMMAND NAME SYNTAX`), so the integration lane
  waits for the screen to stand still before it treats `READY` as a prompt.
