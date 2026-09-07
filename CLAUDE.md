# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

LizTerm is a cross-platform TN3270 client (.NET 10, Avalonia 12) for retro mainframe hobbyists. It does
not implement the 3270 data stream: it bundles `b3270` from the x3270 suite as a child process and speaks
its newline-delimited JSON protocol over stdin/stdout. The design spec is
`docs/superpowers/specs/2026-09-03-lizterm-v1-design.md`; the Milestone 1 plan (executed and merged) is
`docs/superpowers/plans/2026-09-03-lizterm-m1-walking-skeleton.md`, and its final section lists what
Milestones 2 and 3 are expected to cover. Read the spec before changing anything user-facing; Vista
TN3270 is the behavioral reference when the spec is silent.

## Commands

```bash
dotnet test LizTerm.slnx                       # full suite (~4 s warm, longer on a cold build); the live host tests skip themselves
dotnet test tests/LizTerm.Backend.B3270.Tests  # one project
dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~ProfileStoreTests"                      # one class
dotnet test tests/LizTerm.Backend.B3270.Tests --filter "FullyQualifiedName~ReplayTests.Ibmlink_help_screen_replays_to_expected_state"  # one test
dotnet build LizTerm.slnx
dotnet run --project src/LizTerm.App           # picker; append "-- <profile-name>" or "-- host[:port]" to skip it
```

Tests use xunit.v3 in VSTest mode, so `--filter` takes the usual `FullyQualifiedName~` syntax. There is
no separate lint or format step; `Nullable` and `ImplicitUsings` are on solution-wide via
`Directory.Build.props`, and every package version lives only in `Directory.Packages.props` (central
package management: `PackageReference` entries in csproj files carry no `Version`).
`dotnet build LizTerm.slnx --no-incremental 2>&1 | grep -c " warning "` is the zero-warning check to run before
calling anything done; an incremental build hides warnings from projects it does not recompile.

CI is two workflows under `.github/workflows`: `ci.yml` (job `test`, `ubuntu-latest`, pushes to `main`, every PR, and
dispatch: Release build with `-warnaserror`, then the suite with a 5 minute blame hang timeout) and `platforms.yml`
(pushes to `main`, dispatch, and PRs touching the workflow, `native/**`, `src/**`, `tests/**`, `global.json`, or the
`Directory.*.props` files: `engine-macos` on `macos-15` runs `build-macos.sh`, runs the suite with
`LIZTERM_REQUIRE_ENGINE=1`, and only then uploads `b3270-osx-arm64`, so a binary that links but cannot be spawned is
never published; `test-windows` runs the suite). Both jobs run the whole solution, which is why the path filter covers
all of `src/` and `tests/` rather than the native build alone. Runs that fail *or are cancelled* upload
`test-results-<os>`: `.trx`, plus `*.dmp` (a blame-hang kill writes a hang dump, not a sequence file) and any
`*Sequence*.xml`. `timeout-minutes` and `cancel-in-progress` both *cancel*, so those uploads are
`if: ${{ failure() || cancelled() }}` — plain `failure()` would drop the evidence on exactly those runs. `engine-macos`
caches the built engine (`native/out/osx-arm64`, keyed on every `native/build/*.sh`) as well as the source tarball, and
dumps `native/build-tmp/*/{configure,make}.log` on failure, because `build-macos.sh` redirects them out of the job log.

`ci.yml` deliberately does *not* carry a bare `push:` trigger: with `pull_request:` beside it, a PR head SHA gets two
check runs named `test` — one over the branch tip, one over the merge with `main` — and a required check cannot tell
them apart, so a PR could go green for a tree that does not build when merged. Branch protection on `main` requires
`test` only; the platform jobs are path-filtered on PRs and would never report on a docs-only PR, so a rule requiring
them directly would leave those waiting forever (the fix, when it matters, is a `platforms-gate` job with
`needs: [engine-macos, test-windows]` and `if: always()` that passes when they are skipped).

`global.json` pins the SDK to the 10.0.4xx band; supported builds stay on the current LTS. It rolls forward only
within that band: when an SDK update replaces it, bump `version`, never widen `rollForward` (the runner and the Mac
must share one analyzer set for `-warnaserror` to mean the same thing). Keep the pin on the *current* band — a stale
one means a fresh `.NET 10 SDK` install cannot run `dotnet` in this repo at all.

### The b3270 binary

The app and the integration test project copy `native/out/<host-rid>/b3270*` into their output as
`runtimes/<rid>/native/b3270`, but only if that directory exists **at build time**. The App test project inherits that
copy through its project reference to the App, so a built engine lands in its output too. Build it once with
`native/build/build-macos.sh` (needs Xcode CLT and Homebrew `openssl@3`; downloads and checksums x3270
4.5ga6, links OpenSSL statically, and `verify-macos.sh` fails the build if `otool -L` shows anything
outside `/usr/lib` or `/System/Library`), then rebuild the .NET projects. Without it, connecting throws
`BackendUnavailableException`; set `LIZTERM_B3270_PATH` to any b3270 4.2+ (a Homebrew x3270 install
works) as a development override. `native/cache`, `native/build-tmp`, and `native/out` are gitignored.

Environment variables: `LIZTERM_B3270_PATH` (override binary), `LIZTERM_WIRE_LOG` (append every protocol
line in both directions to this file; the fault message points users at Help > Wire Log). The same log can
be started from Help > Wire Log in a session window; files go to `<config>/logs/wire-<profile>-<timestamp>.log`,
and Show Wire Logs opens that folder. `LIZTERM_TEST_HOST`
(`host[:port]`, enables `tests/LizTerm.Integration.Tests`, whose five live tests otherwise skip; add
`LIZTERM_TEST_TLS=1` and `LIZTERM_TEST_VERIFY_CERT=0` for a TLS host with a self-signed certificate);
`LIZTERM_TEST_USER` and `LIZTERM_TEST_PASSWORD` additionally enable the IND$FILE round trip in the same project,
which logs on to TSO, sends and receives `LIZTERM.ITEST` under the user's prefix, and deletes it; without them that
test skips. The credentials are typed through the session, so a wire log of that run holds the password on its
outbound side and is never committed. On Robert's Mac the three live-lane variables are kept in
`~/.config/lizterm-test.env` (outside the repo); `source` it in the shell that runs `dotnet test` rather than
exporting values on a command line.
`LIZTERM_REQUIRE_ENGINE` (any non-blank value) makes the engine smoke test in the same project fail rather than skip when
the test output has no bundled b3270; CI sets it on the macOS job only.

### Avalonia Developer Tools MCP

`.mcp.json` declares the `avalonia_devtools` MCP server (`avdt mcp`, from the global dotnet tool
`AvaloniaUI.DeveloperTools`). Debug builds of the app reference `AvaloniaUI.DiagnosticsSupport` and call
`WithDeveloperTools()` in `Program.BuildAvaloniaApp`, so an app started with `dotnet run` can be inspected
through the server's `attach-to-app`, `tree`, `props`, `screenshot`, `input`, and `action` tools. Release builds
carry none of it, and the headless test builder never calls it. Every tool call is refused until
`AVALONIA_TOOLS_LICENSE_KEY` is present in the MCP server's environment. Put it in
`.claude/settings.local.json` under `env` (gitignored), never in `.mcp.json`: settings `env` is inherited by
MCP child processes, but `${VAR}` placeholders in `.mcp.json` expand only from Claude Code's startup
environment (the desktop app does not source `~/.zshrc`), so an `env` entry for the key in `.mcp.json`
overrides the inherited value with an empty string. Stdio MCP servers are never restarted mid-session, so a
new key takes effect on the next session.

Driving the app from a session (verified 2026-09-04): `dotnet build src/LizTerm.App`, then start it in the
background with `LIZTERM_B3270_PATH=/opt/homebrew/bin/b3270 nohup dotnet run --project src/LizTerm.App
--no-build &` (a fresh worktree has no `native/out`, so the override is required), then `attach-to-app` with
no arguments to list apps and again with `id` set to the pid. `tree` with no node returns the window roots;
a dialog opened by `input` Click appears there as a new root, but `search` does not find windows opened
after its first query, so re-list roots instead. Menu popups never appear as roots, but a menu item can still be reached: `input` Click on the top-level menu
header, then Click on the item (verified 2026-09-04 by opening the File Transfer dialog, which then appears as a
new root). To reach the picker and the profile editor, launch a second instance with no profile argument. `props` returns `bindingExpression` next to each value,
which is the quickest check that a control reached the view model; `IsEnabled` on a command-bound button
reads `True` even while the tree shows `:disabled`, so check `IsEffectivelyEnabled`. The app writes real
profiles to the per-OS config directory, so Cancel any editor dialog you drove rather than Save, and kill
the `dotnet run` pid when done. To connect to a real host without touching real profiles, seed a profile JSON
(camelCase fields) under `<scratch>/Library/Application Support/LizTerm/profiles/` and launch the built apphost
`src/LizTerm.App/bin/Debug/net10.0/LizTerm.App <profile-name>` with `HOME=<scratch>` (the apphost rather than
`dotnet run`, so the HOME override does not disturb the dotnet CLI). `.claude/settings.local.json` is gitignored, so confirm a new worktree has
its own copy before expecting the key to reach the server. The splash appears as a root for up to 2.5 s
before the picker or session window; wait for it to close before `tree`.

### Recording a replay fixture

`native/build/build-playback.sh` builds x3270's `playback` tool; `tools/record-fixture.sh <trace.trc>
<out.jsonl> [model] [playback-step]` replays an x3270 `.trc` host trace through b3270 and saves raw stdout.
`tools/wirelog-to-fixture.sh <wire.log> <out.jsonl>` turns a `LIZTERM_WIRE_LOG` file from a real session into
the same format (inbound lines only, prefix stripped); `gateway-login-tls.jsonl` was made that way and covers TLS,
plain `connected-3270`, and a host-initiated disconnect.
`indfile-tso-roundtrip.jsonl` is the inbound side of the live IND$FILE round trip against MVS/CE, trimmed to the two
transfers; `IndicationParserTests` asserts its `ft` sequence. `gateway-pinned-login.jsonl` was made the same way
from the pinning live test run alone (`LiveHostTests.A_pinned_certificate_verifies_and_a_decoy_pin_fails`), trimmed
to the first connection; it is `gateway-login-tls.jsonl` with `verified:true`.
Fixtures live in `tests/LizTerm.Backend.B3270.Tests/Fixtures/` and its README documents each one,
including why `ibmlink-help.jsonl` was recorded with step `4r` instead of play-to-EOF. Every field bug is
supposed to add a trimmed fixture.

## Architecture

### Dependency rule (enforced by convention, check it in review)

`LizTerm.Core` depends only on the BCL and never mentions Avalonia or b3270 names.
`LizTerm.Backend.B3270` depends on Core and is the only project that knows b3270 exists.
`LizTerm.App` depends on both, but names `LizTerm.Backend.B3270` in exactly one place:
`src/LizTerm.App/SessionFactory.cs`. Everything else in App talks to `IEmulatorSession`. This is what
lets the App tests run against `FakeEmulatorSession` and keeps a future managed engine possible.
The App *tests* name it in exactly one place too, `SessionFactoryTests.The_session_it_builds_verifies_against_the_system_trust_anchors`,
which casts the factory's result to `B3270Session` to read `TrustAnchors`: what the factory injects is the one
thing about the backend the App owns, and asserting it needs the concrete type. Every other App test stays on
`IEmulatorSession` and `FakeEmulatorSession`.

Each src project has `InternalsVisibleTo` for its own test project (Backend also for Integration).
`B3270Session.StartProcessAsync`, `RunAsync`, and `RunRawAsync` are internal and exercised directly by
the backend tests.

### Core model (src/LizTerm.Core)

- `ConnectionState` is declared in a convenient order, not b3270's: the fixtures go tcp-pending, telnet-pending,
  tls-pending, and back to telnet-pending. Group states with `IsConnected()` (safe: the Connected members are
  declared last) and `HasSocket()` (spelled out, because the states past the TCP connect are not contiguous);
  do not read the declaration order as a progression.
- Cells store what b3270 *renders* (foreground, background, `CellRendition` flags), not raw 3270 field
  attributes. Protected/numeric status is not modeled; b3270 enforces field rules and reports violations
  through the keyboard lock (`oerr protected` etc.).
- `ScreenBuffer` is the mutable grid the backend owns (single writer). `Snapshot()` clones it into an
  immutable `ScreenSnapshot` that carries the cursor, so screen and cursor never tear. The UI only ever
  sees snapshots.
- Rows and columns are **zero-based** everywhere in Core and App. b3270 reports them one-based; the
  conversion happens only in `B3270Session.ApplyScreen`. (b3270's `MoveCursor` action is already
  zero-origin, so `MoveCursorAsync` passes coordinates through unchanged.)
- `IEmulatorSession` is bound to one `SessionProfile` at construction; `ConnectAsync` takes no host.
  One window = one session = one profile.
- File transfer is one call: `TransferAsync(FileTransferRequest, IProgress<long>?, CancellationToken)` completes when
  the transfer ends and returns a `FileTransferResult` whose `Message` is the engine's or host's final text
  verbatim: success, failure, or cancel (b3270 reports a cancelled transfer as a failure reading "Transfer canceled
  by user", and a host failure that lands in the same moment keeps the host's text). The byte count travels only
  through the progress callback. It throws only for non-outcomes: `InvalidOperationException` (never started, or a
  transfer already running), `OperationCanceledException` (only a token already cancelled on entry), and
  `BackendUnavailableException` (the engine has died, or dies mid-transfer). `FileTransferRequest.Validate()`
  checks only what would be refused outright;
  fields that do not apply to the direction, mode, or host type are ignored downstream, never errors.
- `SessionProfile.PinnedCertificate` is a `CertificatePin` (SHA-256 fingerprint as colon-separated upper-case hex,
  subject, and the PEM chain leaf first) or null; `ConnectOptions(VerifyCertificate, Pin)` overrides either for one
  attempt. The effective rule is in `B3270Session.ConnectAsync`: verify off means no pin; otherwise the one-shot pin
  wins over the profile's; no pin means whatever `TrustAnchors` yields, not the engine's own default trust — for
  an App session that is the machine's root store, since `SessionFactory` injects `SystemTrustAnchors.Default`;
  the engine's own trust survives only on a bare `B3270Session` left on `TrustAnchors`' own default,
  `NoTrustAnchors`. `DestructiveBackspace` defaults to true (every
  x3270-family default keymap erases; the old belief that x3270 defaults to cursor-left came from the `BackSpace()`
  action's name), and the profile JSON writes every field, so a saved false survives. `LizTerm.Core.Security` holds
  `ICertificateFetcher` and `SslStreamCertificateFetcher` (one handshake that captures and accepts the chain,
  keeping only the leaf plus the host-sent extras from the chain policy's ExtraStore, never a root the chain
  engine supplied from the system store, 10 s bound, `IOException` "No TLS answer ..." on timeout),
  `CertificateReader` (fingerprint, PEM, and whether the chain is pinnable: .NET validates it with its own
  self-signed members as the only trust roots, which is what
  OpenSSL will do with the pin file), and `ITrustAnchorSource` with `SystemTrustAnchors` (the OS root store, both
  `LocalMachine` and `CurrentUser`, read once per instance and exported as a PEM; a store that will not open
  contributes nothing) and `NoTrustAnchors`, the default that leaves an engine on its own trust. They are BCL-only,
  so they live in Core and the integration lane can use them.
- Threading contract: a backend raises all events on one dedicated thread, in order, and knows nothing
  about UI threads. The App layer marshals.
- `ConnectAsync(ConnectOptions?, CancellationToken)`: a cancelled token ends the attempt with
  `OperationCanceledException` (the backend sends one `Disconnect`; b3270 then fails the pending Connect run,
  which is not reported) and leaves the session reusable; `ConnectOptions.VerifyCertificate` overrides the
  profile for one attempt; `ConnectionFailedException.CertificateVerificationFailed` marks the text b3270 sends
  for an unverifiable certificate. After disposal it throws `ObjectDisposedException` rather than starting a new
  engine, so a call that outlives its window cannot leave one running unowned (`FakeEmulatorSession` mirrors
  that). Every wait a cancel then depends on is bounded by `DisconnectTimeout`, so a
  wedged engine cannot hold the attempt open past its own cancellation. After a failed or cancelled Connect run,
  `ConnectAsync` waits for the `Disconnected` state, bounded by the same `DisconnectTimeout` (5 s), before
  throwing, the same wait `DisconnectAsync` uses; b3270 answers the run before it reports `not-connected`, and on the real gateway that
  report lags by up to a few seconds. `Engine` names the binary, its source (`Bundled`, `Override`, or `Unknown`
  when it was never located, which About renders as "not found" rather than borrowing a provenance), and after
  the hello its version. `WireLogPath`, `StartWireLog`, `StopWireLog` make the wire log a session capability
  that survives an engine restart. `AppPaths` owns the per-OS config root with `profiles` and `logs` beneath it.

### Backend (src/LizTerm.Backend.B3270)

`B3270Session` is the process host, protocol handler, and `IEmulatorSession` implementation in one class:

- `IB3270Process` abstracts the child; `B3270ChildProcess` is the real one (keeps a 50-line stderr tail
  for fault reports), `FakeB3270Process` in the tests is the other. `B3270Locator` resolves the binary
  (`LIZTERM_B3270_PATH`, then `runtimes/<rid>/native/`, then beside the app) and reports a missing
  executable bit distinctly.
- A background thread named `b3270-reader` reads stdout line by line, runs each through
  `IndicationParser.TryParse` (never throws; malformed lines are skipped, unknown message types become
  `UnknownIndication`) and dispatches the resulting `Indication` record. `screen-mode`, `erase`, and
  `screen` indications mutate the buffer and publish a snapshot; `oia` updates `KeyboardStatus`;
  `connection`/`tls` update state; `popup` and `ui-error` surface as `HostMessage`.
- Outbound: `RunOperation.Serialize(tag, actions)` writes a `{"run":{"r-tag":..,"actions":[..]}}` line
  under a write lock. Each tag maps to a `TaskCompletionSource` in `_pending`; the matching `run-result`
  completes it. `RunAsync` throws `EmulatorActionException` on failure; `RunRawAsync` returns the result
  so Connect can turn it into `ConnectionFailedException` instead. `DisconnectAsync` sends Disconnect and then
  waits for the `not-connected` state (or process end), capped by the internal `DisconnectTimeout` of 5 s; it
  sends nothing when already disconnected. That wait is factored into `WaitForDisconnectedAsync`, which
  `ConnectAsync` also calls after a failed or cancelled Connect run, so it waits for `not-connected` before
  throwing, unless the report never comes within `DisconnectTimeout` (5 s). `ConnectAsync` sets no deadline of
  its own on a connect that is going well — a plain connect to a TLS listener sits in `telnet-pending` forever,
  because b3270's Connect action never completes, and only the caller's token ends that. Everything a cancel
  depends on is bounded, though: `RunAsync` takes an optional timeout and token and drops its pending slot when
  it gives up (`Handle` ignores a run-result whose tag is gone); the `Set verifyHostCert` run observes the
  caller's token; the cancel's `Disconnect` run is capped by `DisconnectTimeout`; and the cancel gives the
  pending Connect run that same span to produce the "Connection failed" b3270 sends once the Disconnect lands,
  then gives up on it. The Disconnect task is awaited in a `finally`, not only where the Connect run returned
  normally, so an engine dying mid-cancel cannot leave it to land on the next attempt — the shape
  `TransferAsync` already uses for its own cancel.
- Startup waits for the `hello` indication (default 10 s) and rejects versions below
  `B3270Session.MinimumVersion` (4.2.0). Process death raises `Faulted` with the stderr tail, drops to
  `Disconnected`, and clears the process so a later `ConnectAsync` spawns a fresh one. `OnProcessEnded`
  runs on the raw reader thread and must never throw, and it ignores a process that is no longer `_process`:
  a start that fails (`TearDown`) clears the slot before killing the process, so the old reader thread cannot
  disturb a retried start, and only `DisposeAsync` sets `_shuttingDown`, which lasts for the session's life and
  does two jobs: it silences the fault report for the shutdown it is causing, and it closes the session to any
  later start, so `StartProcessAsync` throws `ObjectDisposedException` instead of spawning an engine nothing
  owns. It is set before the process slot is read, so a session disposed without ever being started is closed
  too.
  `TearDown` also covers a `process.Start` that throws (a binary deleted after the locator found it), so a retry
  spawns a fresh process instead of short-circuiting on a slot holding one that never started. `DisposeAsync`
  works from a snapshot of that slot and tolerates a kill or dispose failing, because the reader thread may
  already have torn the same process down; it closes the wire log in a `finally`, after the Quit exchange and on
  every path, including the early return when a fault has already cleared the slot.
- Protocol details that are easy to get wrong: `String()` interprets backslash escapes so literal
  backslashes are doubled; `PasteString` takes **hex-encoded UTF-8**, not text, and is margin-aware
  where `String` is not; certificate verification is a `Set verifyHostCert` action sent before `Connect`,
  not a host-string prefix; the host string is `[L:][lu@]host:port` with IPv6 hosts bracketed; the model
  argument is `3279-<n>[-E]`; the cursor is nested inside `screen` indications and `enabled:false` hides
  it while keeping its position.
- `TransferMapper` builds the `Transfer` action's `keyword=value` arguments and omits what b3270 would reject
  (`cr`/`remap` in binary mode, allocation keywords on receive or on non-TSO hosts) or ignore (`lrecl`/`blksize`
  without a `recfm`, space fields without `allocation`); receive adds `exist=replace` unless appending. b3270 does
  not answer the Transfer run until the transfer ends, so that run-result is the outcome; `ft` indications only
  feed progress (`running` with `bytes`) to the one in-flight `TransferContext`, and stray `ft` lines are dropped.
  Cancel is a `Transfer(Cancel)` run sent from the token's registration; the slot is held until b3270 has answered
  it, so a late cancel can never land on the next transfer, and the transfer's own result comes back verbatim (a
  success is still a success). Every action goes through `RequireProcess`: after the engine dies it throws
  `BackendUnavailableException` carrying the last fault, and `InvalidOperationException` ("The session has not
  been started.") is reserved for a session that was never started.
- Every connect sends one `Set(verifyHostCert,…,caFile,…,acceptHostname,…)` with all three explicit, so an attempt
  never inherits the previous one's trust settings (verified: empty values clear them in the same engine). What
  fills `caFile` is one rule: a pin in force means the pin file (its PEM verbatim, so a pin that lost its `pem` in a
  hand-edited profile writes an empty file and fails the connect rather than quietly widening to the anchors);
  otherwise, whenever the attempt verifies, the trust anchors `TrustAnchors` yields, written to
  `lizterm-roots-<guid>.pem`; and everything else — verification off, or a source with no anchors — means empty,
  because an empty *file* makes b3270 fail the connect with "CA database load … failed" rather than falling back.
  Do **not** gate the anchors on `Profile.UseTls`, however tempting the saved work looks: b3270 implements the
  TELNET START-TLS option, so a plain profile can be upgraded to TLS by the host mid-session, and that upgrade
  would then verify against the engine's own compiled-in directory — the nonexistent Homebrew path this milestone
  exists to stop relying on. (Two reviews have now proposed that gate; the cost it was chasing is already gone,
  because the anchors are read once per process and the file is written once per session.)
  `WriteCaFile(pem, kind)` writes both kinds, owner-only on Unix, because x3270 loads
  `caFile` in `sio_init` for each connection. Their lifetimes differ: a pin file is deleted in the `finally` once
  the Connect run has answered, while the roots file is the same public bytes every time and is written once by
  `RootsFile` and kept for the session, so a reconnect reuses it; `DisposeAsync` deletes it. `acceptHostname` is `any` only
  for a pin that is one self-signed certificate; a pin that also carries CA certificates makes each of them an
  OpenSSL trust anchor, so the engine's normal name check stays on to keep a certificate that CA issued for another
  host from verifying (`CertificateReader.CountCertificates` decides). `B3270Session.TrustAnchors` defaults to
  `NoTrustAnchors.Instance`, so only `SessionFactory` — which injects `SystemTrustAnchors.Default` — makes a
  session read the machine's store; a test that wants anchors supplies them. `LastCaFile` is the test seam (it was
  `LastPinFile` until two callers shared it). `WaitForDisconnectedAsync` awaits a completion source the connection
  state owns: completed while the connection is down, replaced by a fresh one in `SetConnectionState` when it
  comes up, so overlapping waiters share it and a waiter that gives up cannot orphan another.
- `WireLog` is the bug-report mechanism and the fixture recorder: one file, every line, both directions,
  timestamped. `WireLog.TryFromEnvironment(out error)` returns null when the variable is unset or the file
  cannot be opened; the session raises the open error once as a `HostMessage`. The log is a swappable field
  on the session, written under the write lock, and an outbound line is logged *before* the bytes go out: stdin
  auto-flushes, so b3270 can answer at once, and the reader thread logs inbound lines under the log's own lock
  rather than the write lock — logging afterwards let a run-result be written ahead of its run. `DisposeAsync`
  closes the log last, so the Quit and the engine's parting output are in the file. `B3270Locator.Find` returns a `B3270Location` with the source.

### App (src/LizTerm.App)

- `App.OpenSession` builds a `SessionViewModel` around `SessionFactory.Create(profile)`, shows a
  `SessionWindow`, and kicks off `ConnectCommand`. `ShutdownMode` is `OnExplicitShutdown`: closing the
  last session window reopens the profile picker; closing the picker with no sessions open quits.
- `SessionViewModel` takes an `Action<Action> dispatch` argument to marshal backend events onto the UI
  thread; the app passes `Dispatcher.UIThread.Post`, tests pass `a => a()`. Rejected actions
  (`EmulatorActionException`) are deliberately swallowed because b3270 already explains them through the
  keyboard lock; only unexpected and backend-unavailable errors set `ErrorMessage`. `SessionWindow` refocuses the
  screen after the error bar's Dismiss.
- `TerminalScreen` prepares each row as runs of identical style — rectangle, brushes, and shaped `FormattedText`
  — and keeps that list for as long as the snapshot instance and the `CellGeometry` are unchanged, so a blink
  phase flip (a full `InvalidateVisual` twice a second, for as long as anything blinks) redraws the prepared runs
  instead of re-segmenting and re-shaping every cell. A new snapshot or a new geometry rebuilds it; `RunPlanBuilds`
  is the test seam for that. It is a custom `Control` that draws those runs scaled to fit
  via `CellGeometry.Fit` (pure math, unit tested). It raises `KeyRequested`, `TextEntered`, and
  `CellClicked`; `SessionWindow` wires those to the view model. Key events go through the platform copy, paste,
  and select-all hotkeys first, then `Keymap.TryMap`, then `Keymap.TryText` (Ctrl+[ types `¬`, Ctrl+6 `¢`), then
  fall through to Avalonia's text input so dead keys and IMEs work. `Keymap` (`Keyboard/`) is an immutable table
  of `KeyChord(Key, Modifiers, Tap)` to `TerminalKey`, built by
  `DefaultKeymap.Create(destructiveBackspace)` (two cached instances) from Vista TN3270's defaults, cross-checked
  against wc3270 in the 3b spec's section 6.2: Escape is Attn, Shift+Escape SysReq, Pause and Ctrl+Escape Clear,
  Page Up/Down PF7/PF8, Alt+1 and Ctrl+Home/PageUp and Alt+2/3 the PA keys, Ctrl+F1..12 and Shift+F1..12 PF13..24,
  Shift+Enter Newline, Ctrl+R Reset. Vista's Ctrl+Insert for PA1 is not in the table: Avalonia's
  `PlatformHotkeyConfiguration` constructor puts Ctrl+Insert into Copy whatever the command modifier (the Meta-based
  macOS table included), and the screen checks the copy/paste/select-all gestures before the keymap, so Ctrl+Insert
  copies on every platform and PA1 is reached through Alt+1 or the Keys menu. A Left Ctrl tap is Reset and a Right
  Ctrl tap is Enter: `ModifierTapDetector` sees a Ctrl key down and the same key up with nothing between (`OnKeyUp`
  looks up `KeyChord.TapOf`); another key, a pointer press, a wheel turn, focus loss, and the window deactivating
  all reset it. `Keymap.With` is the seam for user remapping later; nothing else about remapping exists. The
  control's `DestructiveBackspace` property (default true, bound to the profile) picks which table.
  The screen's key, copy, paste, and select-all events call the view model's public methods (`SendKeyAsync`,
  `CopyAsync`, `PasteAsync`, `SelectAll`) directly, as `TextEntered` and `CellClicked` always did; each method
  carries its own guard, and a keystroke is never dropped for arriving while the previous one's round trip is
  still open. The `[RelayCommand]`s on the same methods serve the menus, which keep CommunityToolkit's default of
  disabling an async command while it runs.
- Mouse selection is a `ScreenRegion` (Core; inclusive, zero-based, always normalized). `SelectionGesture`
  (`Mouse/`) is the pure press/move/release/double-click state machine; `TerminalScreen` feeds it from pointer
  events, exposes `Selection` (two-way styled property), paints `Palette.Selection` over the region after the
  text and before the cursor, clears it when the screen size changes, and raises `CopyRequested`,
  `PasteRequested`, and `SelectAllRequested` from `GetPlatformSettings().HotkeyConfiguration` (Cmd on macOS, Ctrl
  elsewhere; Ctrl fallback). A plain click moves the cursor on release; a double-click selects the run of
  non-space cells. `SessionViewModel` owns Copy (trimmed rows joined by `\n`), Paste (CRLF normalized, one
  `PasteTextAsync`), and Select All, and nulls `Selection` on every path that sends input to the host. Clipboard
  access goes through `ITextClipboard` (`Clipboard/`), injected like the dispatch delegate; the app passes
  `AvaloniaTextClipboard(window)`, so `App.OpenSession` creates the window before the view model. `TerminalScreen`
  writes its own `Selection` with `SetCurrentValue` so a binding survives, and `OnPointerCaptureLost` ends a drag.
- File transfer: `SessionWindow`'s "File Transfer..." item (enabled while connected) opens `FileTransferWindow`
  modally with a `FileTransferViewModel` from `SessionViewModel.CreateTransfer(IFilePicker)`, which pre-fills it
  from `LastTransferRequest` (the last request started from that window; nothing goes to the profile). The view
  model has Form, Running, and Done phases in one window; Start validates through `TryBuildRequest`, then refuses
  a receive into an existing local file unless Append is on or the path is the one the OS Save dialog last
  returned (that dialog asks about overwriting; a typed or remembered path never did). Progress is marshalled
  through the dispatch delegate and Cancel cancels the token. The window's `Closing` defers to
  `FileTransferViewModel.TryClose`: the first close of a running transfer cancels it and keeps the window so
  the outcome shows, and a second close while the engine has still not answered lets the window go, because
  b3270 only aborts a running transfer on the host's next turn and a stalled host must not pin the dialog, the
  session window, and Quit behind it. Escape closes the dialog in every phase through the window's `OnKeyDown`, so
  it goes through `Closing` and `TryClose` like the Close button; the Running panel has no Close button, which is
  why it is not an `IsCancel` button. OS file dialogs go through `IFilePicker` (`Files/`), injected like the
  clipboard; `LocalFileNames` suggests the save name (member or last qualifier, VM `FN.FT`). `TransferLabels`
  labels the combo boxes. Avalonia propagates an owned dialog's `Closing` cancel to its owner, so the first
  close of the session window (or quit) while a transfer runs is refused the same way; a forced shutdown's
  `DisposeAsync` sends Quit and the pending run faults into the dialog's catch.
- `ProfileStore` keeps one JSON file per profile under the per-OS config directory and silently skips
  unreadable files.
- The IBM 3270 font is embedded as an Avalonia resource (`avares://LizTerm.App/Assets/Fonts#IBM 3270`)
  and also used for the status bar so it reads as one instrument. Status text comes from
  `StatusFormatter`; the padlock glyph is U+E0A2 because the font's true OIA glyphs are unencoded.
- `App` shows `SplashWindow` first (1 s minimum, 2.5 s maximum, click or key dismisses; timed on a `Stopwatch`
  started at `Opened`, so a slow cold start or a clock step cannot skip it), checks the engine through
  `SessionFactory.CheckBackend`, and runs a `StartupPlan` through `StartupGate`: `StartupErrorWindow` when the
  engine is missing, else the session for a resolved argument, else the picker. The gate fires once the splash has
  closed *and* the plan is known, in whichever order — a splash already past its maximum closes from inside
  `Show()`, so `Closed` is subscribed before it and a missed plan would strand the process with no window.
  `StartupArguments.Parse` records the argument as typed in `Argument` and, when the text also reads as a host,
  the ad hoc `[L:][Y:][lu@]host[:port]` fields beside it; it does not choose between the two, because the ad hoc
  forms overlap legal profile names (`CONS01@tk5`, `a:b`) and only `Resolve` has the saved list — an exact name
  match there always wins, including for an argument that is a usage error as a host. A `<letter>:` head counts
  as a prefix only when what follows could be a host at all, so `l:3270` is the host `l` on port 3270 rather than
  TLS to a host named `3270`, and a port section must be plain digits in 1..65535 (invariant, no sign, no
  surrounding space), so `mvs.local:abc` and `mvs.local:99999` are usage errors rather than hostnames that happen
  to contain a colon. A syntax error prints the usage line and opens the picker. `SessionViewModel` times out a connect after `ConnectTimeout` (30 s; the Disconnect item cancels a
  pending one; the timeout line states only what was observed — an open socket with no 3270 session — and offers
  TLS as a possibility, because b3270 reports nothing that tells a TLS listener apart from a host that accepted
  the socket and stopped talking, and confident TLS advice on a host that does not speak it makes things worse),
  offers connect-anyway through `ICertificatePrompt` (`Dialogs/`, injected like the clipboard, asked with a
  `CertificatePromptRequest`: reason lines, what the host presented (read by the injected `ICertificateFetcher` for
  TLS profiles only, under a fresh `ConnectTimeout` source), the previous pin, `CanPin`, and `CannotPinReason`;
  `CanPin` needs a saved TLS profile, a pinnable certificate, and a fingerprint that differs from the pin in force,
  which is what stops a rejected pin from being offered again; "Trust this certificate for this profile" pins: the
  profile is saved with the pin and verification on, `_pinOverride` carries it for the window's life because the
  session's profile is fixed, and Connect Anyway without it is one attempt with verification off; a changed
  certificate reopens the same window titled "Certificate changed" with both fingerprints; the editor shows a pinned
  profile's fingerprint with a Forget button, the only way back to default trust; the prompt and the save run after
  the connect's catch clauses, never inside one, so their own failures reach the error banner instead of faulting
  the command), and owns the Help menu's wire log toggle and `Engine` for `AboutWindow`. Wire log names gain `-2`,
  `-3` when two starts land in the same second (`SessionViewModel.UniquePath`). When `IsWireLogging` cannot
  start a log it marshals its own correction back to false through `dispatch` rather than assigning inline: a
  value corrected from inside its own change notification is invisible to the menu item's two-way binding, which
  is still writing target to source, so the item would keep a check mark for a log that never started and swallow
  the next click. `ShowWireLogsCommand` opens the folder through `IFolderOpener`.
  `TerminalScreen` blinks cells with the Blink rendition at a 750 ms phase, never below 500 ms, and asks
  `ScreenSnapshot.HasBlink` — computed once where the cells are already in hand — rather than rescanning the grid
  on every published screen.

### Tests

- Backend tests drive `FakeB3270Process`: `Emit(line)` queues stdout, `Exit(code)` ends it, and by
  default every incoming `r-tag` gets an automatic success `run-result`. A `Quit` run exits the process rather
  than being answered, as the real engine does, so `DisposeAsync` returns at once instead of waiting out its
  two-second timeout and killing the process — that wait alone was most of the backend project's runtime, so do
  not make `RunResponder` swallow Quit. Set `RunResponder` to control replies, `AutoInitialize = false` to
  suppress the canned `initialize` block, `FaultStart` to make `Start` throw, `FaultWrite` to make stdin writes
  throw, `FaultWaitForExit` to simulate a process that is already gone, and `BeforeWrite` to run a hook (a test
  can make the engine die part-way through a write) before each character reaches stdin. `ReplayTests` feeds a
  fixture through `Emit` and asserts on the resulting snapshot, cursor, and connection-state sequence.
  `FakeTrustAnchorSource` (`Pem`, `Calls`) stands in for `ITrustAnchorSource` in connect tests.
- App tests run on Avalonia's headless platform: `TestAppBuilder` is registered with
  `[assembly: AvaloniaTestApplication]`, control tests use `[AvaloniaFact]` and `KeyPressQwerty`,
  view-model tests use plain `[Fact]` with `FakeEmulatorSession`, which records calls as strings such as
  `key:PF3` and `move:3,9`. `FakeTextClipboard` holds a `Text` string and an optional `Exception`; control
  tests drive drags with the headless `MouseDown`/`MouseMove`/`MouseUp` helpers and read
  `TerminalScreen.Selection` directly. `FakeFilePicker` returns `Result` and records `open` / `save:<name>`;
  `FakeEmulatorSession.TransferAsync` records `transfer:<Direction>:<HostFile>`, keeps `LastTransferRequest`,
  exposes `TransferProgress` and `TransferToken`, and waits on `TransferCompletion` when set so a test can drive
  the Running phase.
- The integration project's `ScreenWaiter` and `TsoNavigator` drive a TSO logon to READY by screen text only
  (LOGON, PASSWORD, `***` pauses answered with Enter, menus left with PF3, READY = last non-blank line); extend
  them with text rules, never coordinates, when a host's screens differ. On MVS/CE the flow is a Hercules banner
  (Enter), the `===>` logon screen, the password prompt, then READY; the navigator waits 500 ms of screen quiet
  before keystrokes and before trusting READY, because IND$FILE typed into a half-repainted field fails with
  `INVALID COMMAND NAME SYNTAX`.
- Assertions on user-visible status strings (for example `"✕ Not connected"`) are exact; change
  `StatusFormatter` and its tests together.
- New fakes: `FakeCertificatePrompt` (`Decision`, `OnAsk`, `Calls`), `FakeFolderOpener`, and
  `FakeEmulatorSession`'s `ConnectCompletion`, `ConnectToken`, `connect:noverify`, `wirelog:start:<path>` /
  `wirelog:stop`, `WireLogException`, `Engine`. `FakeCertificateFetcher` (`Result`, `Exception`, `Calls` as
  `fetch:<host>:<port>`), `FakeCertificatePrompt.LastRequest`, and `FakeEmulatorSession` recording
  `connect:pin:<sha256>`. `Wait.UntilAsync(condition, what, timeout?)` lives once in `tests/Shared/Wait.cs`
  (namespace `LizTerm.Tests.Shared`), compiled into all three test projects by a `<Compile Include=... Link=...>`
  item and imported by a `<Using>` in each csproj, so its default timeout — 5 s, raised from 2 s for CI headroom —
  cannot be changed in one project and missed in another.
  `EnvironmentCollection` is a non-parallel xunit collection in the backend test project only, holding `WireLogTests`,
  which sets `LIZTERM_WIRE_LOG`. `SessionFactoryTests` no longer touches the environment: it calls the internal
  `SessionFactory.Create(profile, overridePath, baseDirectory)` seam with a bogus override and an empty temp directory,
  because a bundled engine in the App test output would otherwise satisfy the locator.
  `TestCertificates` in the Core tests makes self-signed and CA-signed certificates and re-imports through PKCS#12
  so macOS accepts the key for a loopback SslStream server; it stays `internal` and is compiled into the integration
  project by a `<Compile Include=... Link=...>` item (a source-level share, not a project reference, which is why no
  visibility change was needed). `CreateRoot` and `Serial` are the shared parts of `CaSigned`,
  `CaSignedServable(subject)` — whose leaf can serve TLS — and `Ca(subject)`, which is the root alone for a test
  that needs an anchor and no leaf. Two CAs meant to be unrelated must be given different subjects: OpenSSL looks an
  issuer up by subject name, so a shared name tests the wrong-signature path instead. The live tests carry a
  10 minute xunit timeout; `gateway-pinned-login.jsonl` replays a verified pinned connect.
- `EngineSmokeTests` (integration project) starts the **bundled** engine from the test output through `B3270Session`
  and quits; it resolves with `B3270Locator.Find(null, AppContext.BaseDirectory)` so `LIZTERM_B3270_PATH` can never
  satisfy it. It asserts the resolved path is under `B3270Locator.BundledDirectory` (`runtimes/<rid>/native`), which is
  what the csproj copy rule fills and the macOS job exists to prove; it does *not* assert `Engine.Source` or re-parse
  the version, because the constructor supplies the one and `StartProcessAsync` already enforces the other, so both
  would be unfalsifiable. Without a bundled engine it skips, unless `LIZTERM_REQUIRE_ENGINE` is set, when it fails.
  A binary that is *present* but did not resolve — the locator's not-executable arm, which is what a downloaded CI
  artifact looks like before `chmod +x` — always fails, whatever the variable says: that is a broken engine, not an
  absent one. `BundledEngine.Require()` is the one place that whole decision is spelled out, called by every test
  in the project that needs the engine. `EngineRequirement.Decide(found, present, variable)` is that gate, unit tested on its own, and
  `B3270Locator.Candidates` is how the test tells the two apart (`Find` throws the same type for both). On a Mac that
  has run `build-macos.sh` the test runs locally and runs against the copied binary.
