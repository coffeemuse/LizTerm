# LizTerm.Backend.B3270

Notes for working in this project, the only one that knows b3270 exists. The interface it implements is described
in `src/LizTerm.Core/CLAUDE.md`; how the engine binary is built and located is in `docs/engines.md`.

`B3270Session` is the process host, the protocol handler, and the `IEmulatorSession` implementation in one class.
`StartProcessAsync`, `RunAsync` and `RunRawAsync` are internal and exercised directly by the backend tests
(`InternalsVisibleTo` covers the backend tests and the integration tests).

## Process

- `IB3270Process` abstracts the child. `B3270ChildProcess` is the real one and keeps a 50-line stderr tail for fault
  reports; `FakeB3270Process` in the tests is the other.
- `B3270Locator.Find` returns a `B3270Location` with its source, resolving `LIZTERM_B3270_PATH`, then
  `runtimes/<rid>/native/`, then beside the app. It throws the same exception type for "nothing found" and "found but
  not executable"; `B3270Locator.Candidates` is how callers tell the two apart. Its not-found message ends by telling
  users to reinstall LizTerm; the environment variable is for development only.
- Startup waits for the `hello` indication (default 10 s) and rejects versions below `MinimumVersion` (4.2.0).
- Process death drops to `Disconnected`, sets `_fault` and clears the process slot, **then** drains `_pending`,
  **then** raises `Faulted` with the stderr tail — in that order, so the fault is the session's whole state before
  anyone hears of it and a later `ConnectAsync` spawns a fresh process. `RunAsync` re-reads the slot after
  registering its tag and fails itself if the slot moved; that pairs with the drain-after-clear order to leave no
  window in which a run can register and never be answered. The old order (drain, raise, clear) let a caller
  reacting to `Faulted` register a run nobody would complete; CI hung for the 5-minute blame timeout on it on
  2026-09-13 (`A_run_started_from_inside_the_fault_handler_fails_with_the_fault_instead_of_hanging` guards it).
- `OnProcessEnded` runs on the raw reader thread and must never throw. It ignores a process that is no longer
  `_process`: a failed start (`TearDown`) clears the slot *before* killing the process, so the old reader thread
  cannot disturb a retried start. `TearDown` also covers a `process.Start` that throws (a binary deleted after the
  locator found it), so a retry spawns a fresh process instead of short-circuiting on a slot holding one that never
  started.
- Only `DisposeAsync` sets `_shuttingDown`, which lasts for the session's life and does two jobs: it silences the
  fault report for the shutdown it is causing, and it closes the session to any later start, so `StartProcessAsync`
  throws `ObjectDisposedException` instead of spawning an engine nothing owns. It is set before the process slot is
  read, so a session disposed without ever being started is closed too.
- `DisposeAsync` works from a snapshot of the slot and tolerates a kill or dispose failing, because the reader
  thread may already have torn the same process down. It closes the wire log in a `finally` — after the Quit
  exchange, on every path, including the early return when a fault has already cleared the slot.

## Protocol

- A background thread named `b3270-reader` reads stdout line by line and runs each line through
  `IndicationParser.TryParse`, which never throws: malformed lines are skipped, and unknown message types become
  `UnknownIndication`. `screen-mode`, `erase` and `screen` mutate the buffer and publish a snapshot; `oia` updates
  `KeyboardStatus`; `connection` and `tls` update state; `popup` and `ui-error` surface as `HostMessage`; `bell`
  raises `BellRang`, which carries nothing.
- Rows and columns arrive one-based and are converted to zero-based only in `ApplyScreen`. b3270's `MoveCursor`
  action is already zero-origin, so `MoveCursorAsync` passes coordinates through unchanged.
- Outbound, `RunOperation.Serialize(tag, actions)` writes a `{"run":{"r-tag":..,"actions":[..]}}` line under a write
  lock. Each tag maps to a `TaskCompletionSource` in `_pending`, and the matching `run-result` completes it.
  `RunAsync` throws `EmulatorActionException` on failure; `RunRawAsync` returns the result, so Connect can turn it
  into `ConnectionFailedException` instead. `RunAsync` takes an optional timeout and token and drops its pending slot
  when it gives up; `Handle` ignores a `run-result` whose tag is gone.
- Every action goes through `RequireProcess`. After the engine dies it throws `BackendUnavailableException` carrying
  the last fault; `InvalidOperationException` ("The session has not been started.") is reserved for a session that
  was never started.

Easy to get wrong:

- `String()` interprets backslash escapes, so literal backslashes are doubled.
- `PasteString` takes **hex-encoded UTF-8**, not text, and is margin-aware where `String` is not.
- Certificate verification is a `Set verifyHostCert` action sent before `Connect`, not a host-string prefix.
- The host string is `[L:][lu@]host:port`, with IPv6 hosts bracketed. The model argument is `3279-<n>[-E]`.
- The cursor is nested inside `screen` indications, and `enabled:false` hides it while keeping its position.

## Connect and disconnect

- `DisconnectAsync` sends `Disconnect`, then waits for `not-connected` (or process end), capped by
  `DisconnectTimeout` (5 s); it sends nothing when already disconnected. That wait is `WaitForDisconnectedAsync`,
  which `ConnectAsync` also calls after a failed or cancelled Connect run. It awaits a completion source owned by the
  connection state: completed while the connection is down, and replaced by a fresh one in `SetConnectionState` when
  it comes up, so overlapping waiters share it and one that gives up cannot orphan another.
- `ConnectAsync` sets no deadline of its own on a connect that is going well. A plain connect to a TLS listener sits
  in `telnet-pending` forever, because b3270's Connect action never completes; only the caller's token ends it.
- Everything a cancel depends on is bounded. The `Set verifyHostCert` run observes the caller's token. The cancel
  sends one `Disconnect`, capped by `DisconnectTimeout`; b3270 then fails the pending Connect run with "Connection
  failed" (not reported to the caller), and the cancel gives that the same span before giving up on it. The
  Disconnect task is awaited in a `finally`, not only where the Connect run returned normally, so an engine dying
  mid-cancel cannot leave it to land on the next attempt — the same shape `TransferAsync` uses for its own cancel.

## TLS, pins and trust anchors

**Which rule applies** (`ConnectAsync`): verification off means no pin. Otherwise a one-shot `ConnectOptions.Pin`
wins over the profile's, and with no pin at all, verification uses whatever `TrustAnchors` yields. `TrustAnchors`
defaults to `NoTrustAnchors.Instance`, which leaves the engine on its own compiled-in trust; only `SessionFactory`
injects `SystemTrustAnchors.Default` (the machine's root store). A test that wants anchors supplies them.

**Gate on the engine's capabilities, never on the profile or platform.** Every connect gates `verifyHostCert`,
`caFile` and `acceptHostname` independently on the toggle names the engine's own `tls-hello` indication reported at
startup (parsed alongside `hello`, from the same `initialize` block; see `EffectiveTlsOptions` and
`SupportsTlsOption`) — never on `Profile.UseTls`, the OS, or the provider string.

- An engine whose `tls-hello` omits `caFile` — Schannel does: 4.5ga6's `Common/Win32/sio_schannel.c` reports only
  `clientCert`, `tlsMinProtocol` and `tlsMaxProtocol` beyond the TLS-required set — gets a `Set` with
  `verifyHostCert` and `acceptHostname` only, and verifies against the platform certificate store. `DecideCaFile`
  reads no anchors and writes no file for it: nothing would point the engine at one, `CanPinCertificates` is false,
  and the anchor read costs about 210 ms.
- A pin in force, profile or one-shot, against such an engine throws `ConnectionFailedException` **before** the
  `Set` or the `Connect` is written, rather than silently connecting against the platform store while the caller
  still believes their pin holds. That is the one case this gate treats as a hard failure rather than a quiet,
  capability-appropriate downgrade.
- Every toggle that *is* sent is sent explicitly on every connect, so an attempt never inherits the previous one's
  trust settings (empty values clear them in the same engine).

**What fills `caFile`**, when the engine supports it, is one rule:

1. A pin in force means the pin file, its PEM written verbatim. A pin that lost its `pem` in a hand-edited profile
   therefore writes an empty file and fails the connect, rather than quietly widening to the anchors.
2. Otherwise, whenever the attempt verifies, the anchors `TrustAnchors` yields, written to
   `lizterm-roots-<guid>.pem`.
3. Everything else — verification off, or a source with no anchors — sends an empty value. Never an empty *file*
   in this case: that makes b3270 fail the connect with "CA database load … failed" instead of falling back.

**Do not gate the anchors on `Profile.UseTls`.** b3270 implements TELNET START-TLS, so a host can upgrade a plain
profile to TLS mid-session, and that upgrade would then verify against the engine's compiled-in directory — a
Homebrew path that does not exist on users' machines, which is exactly what injecting anchors exists to avoid. The
cost the gate would save is already gone: the anchors are read once per process, and the file is written once per
session. Reviews have proposed this gate twice; it stays rejected.

**Both CA-file kinds live for the whole session.** `WriteCaFile(pem, kind)` writes either, owner-only on Unix, and
`SessionCaFile` manages both the same way: written on first use, reused by every later connect whose PEM matches (a
changed one-shot pin gets its own file and the superseded one is deleted), kept for the session, and removed by
`DisposeAsync`. Do not delete the pin file once the Connect run answers: x3270's `finish_connect` (4.5ga6
`Common/telnet.c:568-579`) runs `sio_init`, which loads `caFile`, for **every** connection, including the engine's
own auto-reconnect attempts (#28), and a load failure fails that connection — so a pinned TLS profile could never
reconnect. Both files hold public certificates, so a shorter life buys nothing. `LastCaFile` is the test seam.

**`acceptHostname`** is `any` only for a pin that is a single self-signed certificate. A pin that also carries CA
certificates makes each of them an OpenSSL trust anchor, so the engine's normal name check stays on to stop a
certificate that CA issued for another host from verifying (`CertificateReader.CountCertificates` decides).

## File transfer

- `TransferMapper` builds the `Transfer` action's `keyword=value` arguments. It omits what b3270 would reject
  (`cr`/`remap` in binary mode; allocation keywords on receive or on non-TSO hosts) or ignore (`lrecl`/`blksize`
  without a `recfm`; space fields without `allocation`). A receive adds `exist=replace` unless appending.
- ISPF is TSO with a prefix: `TransferMapper` sends `host=tso`, then `commandprefix=TSO`, then every TSO keyword. Only
  LizTerm's patched engine knows `commandprefix` (`native/patches`, `docs/engines.md`), and b3270 silently ignores a
  keyword it does not know, so an unpatched engine would type a bare IND$FILE into the ISPF line and time out.
  `TransferAsync` therefore checks the engine binary for `EnginePatches.CommandPrefixMarker` before an ISPF transfer
  (read once per session; no engine path, or an unreadable file, counts as patched) and without it returns
  `EnginePatches.MissingCommandPrefixMessage` as a failed result, sending nothing. TSO, VM and CICS never read the
  file.
- b3270 does not answer the Transfer run until the transfer ends, so that `run-result` is the outcome. `ft`
  indications only feed progress (`running` with `bytes`) to the one in-flight `TransferContext`; stray `ft` lines
  are dropped.
- Cancel is a `Transfer(Cancel)` run sent from the token's registration. The slot is held until b3270 has answered
  it, so a late cancel can never land on the next transfer, and the transfer's own result comes back verbatim (a
  success is still a success).

## Wire log

`WireLog` is both the bug-report mechanism and the fixture recorder: one file, every line, both directions,
timestamped.

- `WireLog.TryFromEnvironment(out error)` returns null when `LIZTERM_WIRE_LOG` is unset or the file cannot be
  opened; the session raises the open error once, as a `HostMessage`.
- The log is a swappable field on the session. Outbound lines are logged under the write lock **before** the bytes
  go out: stdin auto-flushes, so b3270 can answer at once, and logging afterwards once let a `run-result` be written
  ahead of its run. The reader thread logs inbound lines under the log's own lock, not the write lock.
- `DisposeAsync` closes the log last, so the Quit and the engine's parting output are in the file.
