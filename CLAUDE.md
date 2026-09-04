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
dotnet test LizTerm.slnx                       # full suite (~15 s cold); integration test skips itself
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

### The b3270 binary

The app and the integration test project copy `native/out/<host-rid>/b3270*` into their output as
`runtimes/<rid>/native/b3270`, but only if that directory exists **at build time**. Build it once with
`native/build/build-macos.sh` (needs Xcode CLT and Homebrew `openssl@3`; downloads and checksums x3270
4.5ga6, links OpenSSL statically, and `verify-macos.sh` fails the build if `otool -L` shows anything
outside `/usr/lib` or `/System/Library`), then rebuild the .NET projects. Without it, connecting throws
`BackendUnavailableException`; set `LIZTERM_B3270_PATH` to any b3270 4.2+ (a Homebrew x3270 install
works) as a development override. `native/cache`, `native/build-tmp`, and `native/out` are gitignored.

Environment variables: `LIZTERM_B3270_PATH` (override binary), `LIZTERM_WIRE_LOG` (append every protocol
line in both directions to this file; the fault message tells users to set it), `LIZTERM_TEST_HOST`
(`host[:port]`, enables `tests/LizTerm.Integration.Tests`, which otherwise reports one skipped test; add
`LIZTERM_TEST_TLS=1` and `LIZTERM_TEST_VERIFY_CERT=0` for a TLS host with a self-signed certificate).

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
after its first query, so re-list roots instead. Menu popups never appear as roots, so menus cannot be driven through the
inspector; to reach the picker and the profile editor, launch a second instance with no profile argument. `props` returns `bindingExpression` next to each value,
which is the quickest check that a control reached the view model; `IsEnabled` on a command-bound button
reads `True` even while the tree shows `:disabled`, so check `IsEffectivelyEnabled`. The app writes real
profiles to the per-OS config directory, so Cancel any editor dialog you drove rather than Save, and kill
the `dotnet run` pid when done. To connect to a real host without touching real profiles, seed a profile JSON
(camelCase fields) under `<scratch>/Library/Application Support/LizTerm/profiles/` and launch the built apphost
`src/LizTerm.App/bin/Debug/net10.0/LizTerm.App <profile-name>` with `HOME=<scratch>` (the apphost rather than
`dotnet run`, so the HOME override does not disturb the dotnet CLI). `.claude/settings.local.json` is gitignored, so confirm a new worktree has
its own copy before expecting the key to reach the server.

### Recording a replay fixture

`native/build/build-playback.sh` builds x3270's `playback` tool; `tools/record-fixture.sh <trace.trc>
<out.jsonl> [model] [playback-step]` replays an x3270 `.trc` host trace through b3270 and saves raw stdout.
`tools/wirelog-to-fixture.sh <wire.log> <out.jsonl>` turns a `LIZTERM_WIRE_LOG` file from a real session into
the same format (inbound lines only, prefix stripped); `gateway-login-tls.jsonl` was made that way and covers TLS,
plain `connected-3270`, and a host-initiated disconnect.
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

Each src project has `InternalsVisibleTo` for its own test project (Backend also for Integration).
`B3270Session.StartProcessAsync`, `RunAsync`, and `RunRawAsync` are internal and exercised directly by
the backend tests.

### Core model (src/LizTerm.Core)

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
- Threading contract: a backend raises all events on one dedicated thread, in order, and knows nothing
  about UI threads. The App layer marshals.

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
  sends nothing when already disconnected. `ConnectAsync` has no timeout of its own: a plain connect to a TLS
  listener sits in `telnet-pending` forever because b3270's Connect action never completes.
- Startup waits for the `hello` indication (default 10 s) and rejects versions below
  `B3270Session.MinimumVersion` (4.2.0). Process death raises `Faulted` with the stderr tail, drops to
  `Disconnected`, and clears the process so a later `ConnectAsync` spawns a fresh one. `OnProcessEnded`
  runs on the raw reader thread and must never throw.
- Protocol details that are easy to get wrong: `String()` interprets backslash escapes so literal
  backslashes are doubled; `PasteString` takes **hex-encoded UTF-8**, not text, and is margin-aware
  where `String` is not; certificate verification is a `Set verifyHostCert` action sent before `Connect`,
  not a host-string prefix; the host string is `[L:][lu@]host:port` with IPv6 hosts bracketed; the model
  argument is `3279-<n>[-E]`; the cursor is nested inside `screen` indications and `enabled:false` hides
  it while keeping its position.
- `WireLog` is the bug-report mechanism and the fixture recorder: one file, every line, both directions,
  timestamped. `WireLog.FromEnvironment()` returns null when the variable is unset or the file cannot be
  opened, and the open error is surfaced once as a `HostMessage`.

### App (src/LizTerm.App)

- `App.OpenSession` builds a `SessionViewModel` around `SessionFactory.Create(profile)`, shows a
  `SessionWindow`, and kicks off `ConnectCommand`. `ShutdownMode` is `OnExplicitShutdown`: closing the
  last session window reopens the profile picker; closing the picker with no sessions open quits.
- `SessionViewModel` takes an `Action<Action> dispatch` argument to marshal backend events onto the UI
  thread; the app passes `Dispatcher.UIThread.Post`, tests pass `a => a()`. Rejected actions
  (`EmulatorActionException`) are deliberately swallowed because b3270 already explains them through the
  keyboard lock; only unexpected and backend-unavailable errors set `ErrorMessage`.
- `TerminalScreen` is a custom `Control` that draws each row as runs of identical style, scaled to fit
  via `CellGeometry.Fit` (pure math, unit tested). It raises `KeyRequested`, `TextEntered`, and
  `CellClicked`; `SessionWindow` wires those to the view model. Key events go through `DefaultKeymap`
  first; anything unmapped falls through to Avalonia's text input so dead keys and IMEs work. Backspace maps
  to b3270's non-destructive `BackSpace` unless the profile's `DestructiveBackspace` is on, in which case the
  keymap emits `TerminalKey.Erase`; the control's `DestructiveBackspace` property carries that choice.
- Mouse selection is a `ScreenRegion` (Core; inclusive, zero-based, always normalized). `SelectionGesture`
  (`Mouse/`) is the pure press/move/release/double-click state machine; `TerminalScreen` feeds it from pointer
  events, exposes `Selection` (two-way styled property), paints `Palette.Selection` over the region after the
  text and before the cursor, clears it when the screen size changes, and raises `CopyRequested`,
  `PasteRequested`, and `SelectAllRequested` from `PlatformSettings.HotkeyConfiguration` (Cmd on macOS, Ctrl
  elsewhere; Ctrl fallback). A plain click moves the cursor on release; a double-click selects the run of
  non-space cells. `SessionViewModel` owns Copy (trimmed rows joined by `\n`), Paste (CRLF normalized, one
  `PasteTextAsync`), and Select All, and nulls `Selection` on every path that sends input to the host. Clipboard
  access goes through `ITextClipboard` (`Clipboard/`), injected like the dispatch delegate; the app passes
  `AvaloniaTextClipboard(window)`, so `App.OpenSession` creates the window before the view model.
- `StartupArguments.Parse` decides between a saved profile name, `host[:port]`, and `[ipv6]:port` from
  the first command-line argument. `ProfileStore` keeps one JSON file per profile under the per-OS config
  directory and silently skips unreadable files.
- The IBM 3270 font is embedded as an Avalonia resource (`avares://LizTerm.App/Assets/Fonts#IBM 3270`)
  and also used for the status bar so it reads as one instrument. Status text comes from
  `StatusFormatter`; the padlock glyph is U+E0A2 because the font's true OIA glyphs are unencoded.

### Tests

- Backend tests drive `FakeB3270Process`: `Emit(line)` queues stdout, `Exit(code)` ends it, and by
  default every incoming `r-tag` gets an automatic success `run-result`. Set `RunResponder` to control
  replies, `AutoInitialize = false` to suppress the canned `initialize` block. `ReplayTests` feeds a
  fixture through `Emit` and asserts on the resulting snapshot, cursor, and connection-state sequence.
- App tests run on Avalonia's headless platform: `TestAppBuilder` is registered with
  `[assembly: AvaloniaTestApplication]`, control tests use `[AvaloniaFact]` and `KeyPressQwerty`,
  view-model tests use plain `[Fact]` with `FakeEmulatorSession`, which records calls as strings such as
  `key:PF3` and `move:3,9`. `FakeTextClipboard` holds a `Text` string and an optional `Exception`; control
  tests drive drags with the headless `MouseDown`/`MouseMove`/`MouseUp` helpers and read
  `TerminalScreen.Selection` directly.
- Assertions on user-visible status strings (for example `"✕ Not connected"`) are exact; change
  `StatusFormatter` and its tests together.
