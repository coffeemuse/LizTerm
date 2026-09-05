# LizTerm Milestone 2, Plan 3a: Polish Bundle (Features)

Date: 2026-09-05
Status: approved 2026-09-05 and implemented on branch claude/project-status-next-234e6b; this is the as-built spec
Parent: `2026-09-03-lizterm-v1-design.md` sections 5.5, 6.1, 6.2, 6.3, 6.8, and 7

## 1. Purpose

Close out the user-facing promises of the v1 design that Milestones 1 and 2 have not yet built, and
fix the two connection defects found while testing them. This is the third of the three Milestone 2
plans and is delivered as two specs and two pull requests: this one (3a) carries everything a user
can see; a follow-on (3b) carries the parked backend follow-ups, dialog nits, test-coverage gaps,
live-lane hardening, and the keymap cross-check. Section 10 lists what 3b owns.

In scope for 3a:

- A connect timeout, and cancelling a connect in progress. Today a plain connect to a TLS port
  sits in `telnet-pending` forever because b3270's Connect action never completes.
- TLS, and unverified TLS, for the ad hoc `host[:port]` command-line form.
- The certificate connect-anyway prompt, with "always allow" saved to the profile.
- The Help menu: a live Wire Log toggle, a Show Wire Logs item, and About.
- Engine provenance in About: which b3270 is in use and where it came from.
- The splash window, and the startup check that shows an error instead of the picker when the
  engine is missing.
- Blinking cells in the screen control.

Decisions made in discussion on 2026-09-05:

- Two specs, two PRs (3a features, 3b hardening), so each stays reviewable.
- The wire log is a live toggle for the current session, not a preference for future sessions: a
  user who has just hit a problem turns it on and reproduces without reopening the window.
- The certificate override reaches the backend as a one-shot `ConnectOptions` argument; the
  profile bound to the session stays immutable (alternatives considered in section 3).
- LizTerm's own license is still undecided; About shows version and third-party notices only.
- The splash mark is text in the 3270 font for now, laid out so an image (Liz, the cat the client
  is named for, beside a 122-key keyboard) replaces one element later.
- Blink is paced for photosensitive safety (section 8).

## 2. Facts established by probing b3270 4.5ga6 on 2026-09-05

Both probes ran against the TLS test gateway (self-signed certificate) with the Homebrew b3270 in
JSON mode.

1. A verify-on connect to a self-signed certificate fails the Connect run itself. The run-result
   is `success:false` with three text lines: `Connection failed:`, `TLS: Host certificate
   verification failed:`, and the OpenSSL reason, for example `self-signed certificate (18)`. A
   `not-connected` connection indication follows. No popup or other indication carries the reason.
2. b3270 accepts a `Disconnect` run while a `Connect` run is pending. The Disconnect run succeeds
   at once, the Connect run then fails with the single line `Connection failed`, the state drops to
   `not-connected`, and the same process connects again cleanly afterwards. Cancelling a connect
   therefore needs no process restart.

Fact 1 means the certificate prompt hangs off the existing `ConnectionFailedException` with no new
indication parsing. Fact 2 means a connect timeout is one Disconnect run.

## 3. Core changes (`LizTerm.Core`)

Core stays free of Avalonia and b3270 names. Additions:

```csharp
/// <summary>One-shot choices for a single connect attempt. Null fields mean "as the profile says".</summary>
public sealed record ConnectOptions(bool? VerifyCertificate = null);

public enum EngineSource { Bundled, Override }

/// <summary>Which emulator engine binary a session uses. Version is null until the engine has started.</summary>
public sealed record EngineInfo(string Name, string? Version, string Path, EngineSource Source);
```

`ConnectionFailedException` gains `bool CertificateVerificationFailed { get; }` (constructor
parameter, default false).

`IEmulatorSession` changes:

```csharp
Task ConnectAsync(ConnectOptions? options = null, CancellationToken cancellationToken = default);

/// <summary>The engine binary in use; Version fills in once the engine has started.</summary>
EngineInfo Engine { get; }

/// <summary>Path of the active wire log, or null. Every protocol line in both directions is appended there.</summary>
string? WireLogPath { get; }
/// <summary>Starts logging to path (appending). Throws IOException if the file cannot be opened and
/// InvalidOperationException if a log is already active.</summary>
void StartWireLog(string path);
/// <summary>Stops and closes the active log; does nothing when none is active.</summary>
void StopWireLog();
```

Two alternatives to `ConnectOptions` were considered and rejected. A replaceable profile
(`UpdateProfile` while disconnected) would also allow editing a profile from the session window
later, but model and code page are b3270 command-line arguments, so the backend would need to
compare start-time fields and restart the process, and every binding on `Profile` would need change
notification. Rebuilding the session from a modified profile would re-subscribe every event and
break the parent spec's one window, one session rule. `ConnectOptions` changes nothing else.

`ConnectAsync` honors its token for the whole attempt: a cancelled token makes the attempt end with
`OperationCanceledException` and leaves the session disconnected and reusable. The existing single
call site (`SessionViewModel`) and the fake are updated; the token parameter keeps its position as
the last argument so `ConnectAsync(cancellationToken: ct)` still reads naturally.

A new static `AppPaths` in `LizTerm.Core.Profiles` owns the per-OS configuration root
(`~/Library/Application Support/LizTerm` on macOS, `%APPDATA%\LizTerm` on Windows,
`$XDG_CONFIG_HOME/LizTerm` or `~/.config/LizTerm` elsewhere) and exposes `ProfilesDirectory()` and
`LogsDirectory()`. `ProfileStore.DefaultDirectory()` becomes a call to `AppPaths.ProfilesDirectory()`
so the existing behavior is unchanged.

## 4. Backend changes (`LizTerm.Backend.B3270`)

### 4.1 Cancellable connect

`ConnectAsync(options, token)`:

1. `StartProcessAsync(token)` as today.
2. `Set verifyHostCert` with `options?.VerifyCertificate ?? Profile.VerifyCertificate`.
3. If the token is already cancelled, throw before sending Connect.
4. Send the Connect run and register on the token: the registration sends one `Disconnect` run
   through `RunRawAsync` and ignores its result (the process may be gone). The registration is
   disposed when the Connect run completes.
5. When the Connect run fails and the token is cancelled, throw `OperationCanceledException(token)`;
   the "Connection failed" text is the cancel's own consequence, not an error to report. When the
   run fails and the token is not cancelled, throw `ConnectionFailedException` as today, with
   `CertificateVerificationFailed` true when any text line starts with the constant
   `"TLS: Host certificate verification failed"` (kept as `B3270Session.CertificateFailurePrefix`).

Engine death during a pending connect keeps its current behavior: the pending run faults with
`BackendUnavailableException`.

### 4.2 Engine info

`B3270Locator.Find` returns `B3270Location(string Path, EngineSource Source)`: the environment
override is `Override`; the `runtimes/<rid>/native` copy and a binary beside the app are both
`Bundled`, which is also what a macOS bundle's contents will be. The `B3270Session` constructor
gains a `B3270Location` parameter next to the process factory, so `Engine` is answerable before any
process runs, and exposes `Engine` as `EngineInfo("b3270", null, path, source)` until the hello arrives, then with
`Version` set to `"<version> (<build>)"` from the hello. A restart after a fault keeps the last
version.

### 4.3 Wire log as a session capability

`_wireLog` becomes a mutable field. Writes on the outbound side already happen under the write
lock; the reader thread takes a volatile read of the field per line. `StartWireLog` opens the file
(append) and installs the log under the write lock, throwing `InvalidOperationException` if one is
active; `StopWireLog` swaps it out and disposes it. A session constructed with a log from the
environment starts with `WireLogPath` set to that path; stopping closes it like any other. Session
disposal disposes the active log.

`WireLog.LastOpenError` (a static, on the parking list since Milestone 1) is removed.
`WireLog.FromEnvironment()` becomes `WireLog.TryFromEnvironment(out string? error)`; the session
constructor takes the pair and raises the once-only `HostMessage` warning from its own instance
state. `WireLog` gains a `Path` property.

`SessionFactory` (the only App file that names the backend) creates the session from
`B3270Locator.Find()` and `WireLog.TryFromEnvironment`, and gains
`static EngineInfo CheckBackend()` which runs the locator and lets `BackendUnavailableException`
propagate; `App` catches it at startup (section 7).

### 4.4 Certificate-failure fixture

`tools/wirelog-to-fixture.sh` produces `gateway-cert-failure.jsonl` from a verify-on connect to
the self-signed gateway. A `ReplayTests` case feeds it and asserts the connection-state sequence
(`TcpPending`, `TelnetPending`, `TlsPending`, `Disconnected`) and that `ConnectAsync` throws with
the flag set. The Fixtures README describes it.

## 5. Connection behavior in the app (`LizTerm.App`)

### 5.1 Timeout and cancel

`SessionViewModel.ConnectTimeout` is a static `TimeSpan` (30 seconds; tests set it shorter). Each
Connect creates a `CancellationTokenSource` with that timeout and keeps it in `_connectCts` until
the attempt ends. `DisconnectCommand` stays enabled while connecting: when `_connectCts` is live it
cancels it instead of calling `DisconnectAsync`. Outcomes:

- User cancel: no message; the status bar already reads "✕ Not connected".
- Timeout: `ErrorMessage` = `"Connection to {host}:{port} timed out after 30 seconds."`, followed
  by `" The host may require TLS. Enable it in the profile."` when the profile has `UseTls` false
  and the attempt reached `TelnetPending` (the view model records the furthest state seen during
  the attempt). Timeout and user cancel are told apart by `_connectCts.Token.IsCancellationRequested`
  set by the timer versus a `_connectCancelledByUser` flag set by the command.
- Other failures: unchanged.

The exact strings live in `StatusFormatter` (`ConnectTimeout(profile, reachedTelnet)`) and are
asserted exactly, like every other status string.

### 5.2 Command-line prefixes

`StartupArguments.Parse` accepts x3270's full ad hoc host syntax, `[L:][Y:][lu@]host[:port]`:

- Prefixes ahead of the host: `L:` turns TLS on, `Y:` turns certificate verification off, in
  either order, each at most once. A hostname cannot contain a colon, so any leading `<letter>:` is
  a prefix; a letter other than L or Y is an error.
- An LU name ahead of the host, ended by `@`: it goes to the ad hoc profile's `LuName` verbatim.
  x3270 accepts a comma-separated list there and tries the names in order, which is how a Hercules
  operator console or another special-purpose terminal group is reached; the profile, the editor's
  LU name field, and `HostStringBuilder` already pass such a list through unchanged, so the command
  line does the same and nothing validates the names. The first `@` ends the LU part; an empty LU
  part (`@host`) is an error.
- Then the host, bracketed when IPv6, and the optional port. With `L:` and no port the port is 992,
  b3270's own TLS default; otherwise 23.

On error the app writes one usage line to stderr
(`Usage: LizTerm [profile | [L:][Y:][lu@]host[:port] | [L:][Y:][lu@][ipv6][:port]]`) and opens the
picker. The resulting record gains `bool UseTls`, `bool VerifyCertificate`, and `string? LuName`;
`Resolve` copies them to the ad hoc profile. The ad hoc profile's `Name` is the address without
prefixes, `host:port` or `lu@host:port`, so two ad hoc windows to the same host through different
LU names are told apart by title.

### 5.3 Certificate prompt

`ICertificatePrompt` in `src/LizTerm.App/Dialogs/`:

```csharp
public interface ICertificatePrompt
{
    /// <summary>Asks whether to connect without verifying the host certificate.</summary>
    Task<CertificateDecision> AskAsync(string host, IReadOnlyList<string> reason, bool canRemember);
}
public sealed record CertificateDecision(bool ConnectAnyway, bool Remember);
```

`SessionViewModel` takes an `ICertificatePrompt` and an optional `Action<SessionProfile>? saveProfile`
after the clipboard. `App.OpenSession` passes `AvaloniaCertificatePrompt(window)` and, for a profile
that came from the store, `_store.Save`; ad hoc profiles pass null. Flow in `ConnectAsync`:

1. `ConnectionFailedException` with `CertificateVerificationFailed` and no override active: call the
   prompt with `Profile.Host`, `ex.Lines` minus the leading `Connection failed:` line, and
   `saveProfile is not null`.
2. `ConnectAnyway` false: `ErrorMessage = ex.Message`, as today.
3. `ConnectAnyway` true: connect again with `new ConnectOptions(VerifyCertificate: false)` under a
   fresh timeout source. `Remember` true additionally calls `saveProfile(Profile with
   { VerifyCertificate = false })` and sets `_verifyOverride = false`, so every later Connect from
   this window passes the override and does not ask again. Without Remember the override applies to
   this attempt only; a later Connect from the menu asks again ("once", per the parent spec).
4. The second attempt cannot fail on verification, so the prompt cannot loop; any other failure
   shows as usual.

`CertificateWindow` (modal, owner = session window): title "Certificate not verified"; body
"{host} presented a certificate that could not be verified:" followed by the reason lines in the
3270 font; a checkbox "Always allow for this profile (stops checking its certificate)" visible only when `canRemember`; buttons
"Connect Anyway" and "Cancel", with Cancel the default and Escape mapped to it. Nothing about the
certificate is stored beyond the profile flag.

## 6. Help menu, wire log, About

### 6.1 Menu

Session window only. `Help` holds `Wire Log` (a check item bound to `IsWireLogging`),
`Show Wire Logs...`, a separator, and `About LizTerm...`. The picker has no menu and gets none.

### 6.2 Wire log toggle

`SessionViewModel`:

- `IsWireLogging` reflects `_session.WireLogPath is not null` and is set from the check item.
  Turning it on calls `StartWireLog(AppPaths.LogsDirectory()/wire-<profile>-<yyyyMMdd-HHmmss>.log)`,
  creating the directory first; `<profile>` is the profile name with characters outside
  `[A-Za-z0-9._-]` replaced by `_`. An `IOException` sets `ErrorMessage` ("Could not open the wire
  log: ...") and leaves the item unchecked. Turning it off calls `StopWireLog()`.
- `WireLogText` is the status-bar indicator: `"● wire log"` while active, empty otherwise, from
  `StatusFormatter.WireLog(bool)`. It occupies a new column in the status bar grid.
- `ShowWireLogsCommand` creates `AppPaths.LogsDirectory()` if missing and opens it through the
  window's `Launcher.LaunchDirectoryInfoAsync`, behind an `IFolderOpener` interface in `Files/`
  (`Task<bool> OpenAsync(string directory)`) injected like the file picker, with a fake for tests. A failure to launch sets `ErrorMessage` with the path so the user can open it by hand.

`StatusFormatter.Fault` changes its closing advice from naming the environment variable to
`"Turn on Help > Wire Log and reproduce to capture a log."`; the test for that string changes with it.
The environment variable remains a developer override and is still documented in CLAUDE.md.

### 6.3 About

`AboutWindow` (modal from the Help menu): "LizTerm" in the 3270 font, `Version {version}`, an engine
line, and a read-only scrollable text box of third-party notices.

- The version is `AssemblyInformationalVersionAttribute` of the App assembly, trimmed of any `+hash`
  suffix. `Directory.Build.props` gains `<Version>0.3.0</Version>`; Milestone 3's release process
  owns it from then on.
- The engine line comes from `StatusFormatter.Engine(EngineInfo info, string overrideOrigin)`:
  `"b3270 4.5.6 (b3270 v4.5ga6 ...), bundled"` when `Source` is `Bundled`;
  `"b3270 4.5.6 (...), from LIZTERM_B3270_PATH: /opt/homebrew/bin/b3270"` for `Override`, where
  `overrideOrigin` names where the override came from (today always the environment variable; a
  future preference passes its own name); `"b3270, not started, bundled"` or the override form with
  `not started` in place of the version when `Version` is null. The path is shown on a second line
  for both sources.
- The notices are `THIRD-PARTY-NOTICES.txt` at the repository root, embedded as an Avalonia
  resource, containing the x3270 BSD-3 notice (the text b3270 prints in its hello) and the 3270 font
  license already in `Assets/Fonts/LICENSE-3270font.txt`. No LizTerm license line.

## 7. Splash and startup

`App.OnFrameworkInitializationCompleted` shows `SplashWindow` first: borderless
(`SystemDecorations="None"`), centered, fixed 480×300, black. Then, still on the UI thread, it
loads the profile store and calls `SessionFactory.CheckBackend()`, catching
`BackendUnavailableException` into a message.

Timing is a pure class `SplashTiming(TimeSpan minimum, TimeSpan maximum)` with defaults 1 s and
2.5 s: `CloseAt(shownAt, dismissRequestedAt?)` returns `max(dismissRequestedAt, shownAt + minimum)`
when a click or key was seen and `shownAt + maximum` otherwise. The window records the first pointer
press or key down and closes at that instant, using a `DispatcherTimer` for the remainder.

What opens when the splash closes is decided by a pure `StartupPlan.Decide(backendError, args,
profiles)` returning one of `ShowError(message)`, `OpenSession(profile)`, `OpenPicker`:

- `backendError` set: `ShowError`. A `StartupErrorWindow` shows the message (the locator's text,
  which already lists the paths it looked in and the `chmod` hint) and a Quit button; closing it quits.
- Otherwise the argument resolves to a profile: `OpenSession`; a prefix error from section 5.2 or an
  unknown profile name is `OpenPicker` after the stderr usage line (unknown profile name already
  falls through to the picker today).
- Otherwise `OpenPicker`.

Nothing else opens until the splash has closed, so no window renders under it.

The mark is a named `ContentControl` (`SplashMark`) whose content today is a `StackPanel` with
"LizTerm" at 72 px in the 3270 font in the palette's green and the version beneath at 16 px. A
comment in the XAML names `avares://LizTerm.App/Assets/Splash/liz.png` as the image that replaces
the panel's content, at the same 480×300 window size, so the swap is one element.

## 8. Blink

`TerminalScreen` gains a `DispatcherTimer` with a 750 ms interval. It runs only while the current
snapshot has at least one cell with `CellRendition.Blink`; a snapshot without any stops it, and so
does leaving the visual tree. Each tick flips a hidden flag and invalidates. In the hidden phase
`DrawRun` skips the text and underline of a run whose rendition has Blink and still paints the
background. The cursor never blinks.

Photosensitive safety: WCAG 2.3.1 forbids content that flashes more than three times per second.
A 750 ms phase is two flashes every three seconds (0.67 Hz), the change is confined to the cells the
host marks, and the background stays, so the luminance change is small. Do not shorten this
interval below 500 ms (1 Hz) without revisiting that reasoning.

`internal bool BlinkTimerRunning` is exposed for the headless tests.

## 9. Testing

Backend (`FakeB3270Process`):

- Cancel during a pending Connect sends `Disconnect`, the Connect's failure becomes
  `OperationCanceledException`, and a later Connect works on the same process.
- A token cancelled before Connect throws without sending Connect.
- The certificate line sets `CertificateVerificationFailed`; other failures leave it false.
- `ConnectOptions(VerifyCertificate: false)` sends `Set verifyHostCert false` for a profile that
  verifies; null falls back to the profile.
- `Engine` reports name, path, and source before start and the hello's version after.
- Wire log: start writes both directions to the file, `WireLogPath` reports it, stop closes it,
  start while active throws, an unopenable path throws `IOException`, an environment log is active
  from construction, and the open-error warning is raised exactly once (the parked test).
- Replay of `gateway-cert-failure.jsonl`.
- `B3270Locator` returns `Override` for the environment path and `Bundled` for the other two.

Core: `AppPaths` per-OS roots (the OS branches that can run on the host), `SplashTiming`,
`StartupPlan`, `StartupArguments` syntax (`L:`, `Y:`, `Y:L:`, duplicate prefix, unknown letter,
default port 992 with `L:`, `lu@host`, `lu1,lu2@host:port`, `L:lu@[ipv6]:port`, empty LU part,
the profile name for each form), `ConnectOptions` defaults.

App view model (`FakeEmulatorSession`, which records `connect` / `connect:noverify`, honors the token
by faulting a pending connect with `OperationCanceledException` when `ConnectCompletion` is set,
and implements the wire-log members and `Engine`; plus `FakeCertificatePrompt` with a scripted
decision and `FakeFolderOpener`):

- Timeout message with and without the TLS hint; user cancel through Disconnect is silent;
  Disconnect while connected still disconnects.
- Prompt declined shows the failure; accepted reconnects with `connect:noverify`; Remember saves the
  profile with `VerifyCertificate` false and a later Connect passes the override without asking;
  ad hoc profile (null save) passes `canRemember` false; a session created with `_verifyOverride`
  never prompts.
- Wire log toggle round trip, `WireLogText`, the `IOException` path, and Show Wire Logs invoking
  the opener with the logs directory.
- `StatusFormatter` strings: timeout, engine line for each source and for a null version, wire log
  indicator, the new fault advice.

App headless: blink timer runs exactly when a Blink cell is present and stops on the next snapshot
without one; `SplashWindow` shows the version text; `AboutWindow` shows the version and engine line;
`CertificateWindow` hides the checkbox when `canRemember` is false and returns Cancel on Escape.

Integration (skips without `LIZTERM_TEST_HOST`): with `LIZTERM_TEST_TLS=1` and verification on, a
connect to the gateway throws with `CertificateVerificationFailed` true; with a 2 second timeout, a
plain connect to the TLS port throws `OperationCanceledException` and the session can still connect
with TLS afterwards.

## 10. Deferred to plan 3b

- Backend: `B3270Session.DisposeAsync` catch should use `_process?.Kill()`.
- Transfer dialog: an `IsCancel` button so Escape closes it; comment that send progress can exceed
  `TotalBytes` when CRLF is added.
- Session window: refocus the screen after the error bar's Dismiss; use `SetCurrentValue` for the
  control's own `Selection` writes; add the `OnPointerCaptureLost` reset; a window helper that checks
  `CanExecute` before executing commands from hotkeys.
- Tests: `TransferMapper` Binary + allocation on TSO send and exhaustive switches;
  `FileTransferViewModel` `PropertyChanged` for derived flags; `TransferLabels` null and
  `ConvertBack`; Cancel disables after the first click; `OnFileTransferClick` catch path;
  triple-click via `PressAt(cell, 3)`.
- Live lane: `TsoNavigator` documentation versus its `===>` / "hit ENTER" keys; re-check the gate
  predicate after the quiet wait; `LogoffAsync` diagnostic on timeout; `ScreenWaiter` timer and
  clock; an xunit timeout on the live tests.
- Keymap cross-check against wc3270 and Vista TN3270 defaults, as a table in the spec and any
  resulting mapping changes.
- Certificate fingerprint pinning so "always allow" means this certificate (re-prompt when it
  changes).
- Hoist `WaitUntilAsync` into a shared backend test helper.
- The `_disconnected` single slot under overlapping direct API callers.
- A port range check in `StartupArguments`.
- Isolate `SessionFactoryTests`' environment-variable mutation.
- Test-coverage gaps in Tasks 1, 3, 4, 5, 6, 9, 10, 11, 12, 16; `DisposeAsync` stopping the log
  before `Quit`; same-second wire-log names; `AboutWindow` fixed height; `_shownAt` at construction;
  unbracketed ad hoc IPv6 names.

## 11. Out of scope

A preferences UI (the future "engine path" preference is anticipated only by `EngineSource`), an
About item in the picker or the macOS application menu, a per-user blink or animation switch, an
image asset for the splash, and any change to the profile editor.

## 12. Deviations from this spec (as-built)

Rulings the executor made while implementing sections 3 through 9, recorded here rather than edited
into the sections above so the discussion in those sections still reads as it was approved:

1. `SessionViewModel.ConnectTimeout` (section 5.1) is an instance property with a 30 s default, not
   a static, so parallel test classes cannot race on it.
2. `StartupPlan.OpenSession` (section 7) carries `FromStore` in addition to the profile, so the app
   knows whether the certificate override the connect flow discovers can be saved back to a stored
   profile or only offered ad hoc.
3. The About engine line (section 6.3) ends in `, from LIZTERM_B3270_PATH` (the override's origin)
   with no path appended; the path is shown on its own second line (`EnginePathText`) for both
   sources, rather than inline after a colon as the section's example string showed. Bundled reads
   `, bundled`.
4. The integration timeout test (section 9) reuses one `B3270Session` for both connect attempts
   instead of switching to TLS afterwards, because a session is bound to one profile for its life
   (section 3); the test proves the session survives a cancelled connect by attempting twice, not
   by attempting once and then reconnecting differently.
5. `SessionFactory.Create` catches the locator's `BackendUnavailableException`, records
   `B3270Location.Unknown`, and hands the session a process factory that rethrows it, so a missing
   engine is reported by the first connect (as it was before this plan) rather than crashing
   `App.OpenSession`. `SessionFactory.CheckBackend` at startup (section 7) is the user-facing gate
   that shows `StartupErrorWindow`; a profile opened directly still fails at connect time if the
   engine has gone missing since startup.
6. `B3270Session.StartWireLog` wraps `UnauthorizedAccessException`, `ArgumentException`,
   `NotSupportedException`, and `IOException` subclasses into a plain `IOException`, so
   `SessionViewModel`'s catch (section 6.2) only needs to handle the one exception type.
7. The cancel's `Disconnect` run (section 4.2) is held on the connect attempt and awaited before the
   attempt ends, rather than fired and forgotten: a cancel that races a successful Connect still
   disconnects and reports cancellation instead of leaving a connected session the caller believes
   it cancelled.
8. Avalonia 12.1.2 has no `SystemDecorations` enum; the splash (section 7) uses
   `WindowDecorations="None"` for the borderless window instead.
9. The `Launcher.LaunchDirectoryInfoAsync` call in `ShowWireLogsCommand` (section 6.2) needs
   `using Avalonia.Platform.Storage;`, which the section's description omitted.
10. The splash (section 7) stops its timer on `Closed`; its dismiss (click/key) and self-close
    (timeout) paths are both tested headlessly rather than only the timing math.
11. `FakeEmulatorSession.Profile` (section 9) is settable, not fixed at construction, so tests can
    swap the bound profile without building a new fake.
12. Live pass on 2026-09-05 with the Avalonia DevTools inspector against the TLS gateway: the
    certificate prompt renders and works end to end (verify on, failure, verify off, connected), the
    ad hoc profile hides the checkbox, the status bar shows the TLS state and the wire log indicator,
    Help > Wire Log shows checked and toggles off, About opens with the version, engine line, and
    notices. Not yet seen on screen: the splash's rendered appearance (it opened and closed before
    the inspector attached) and a rejected wire-log toggle through the menu; the display was off for
    the retry.
13. After a failed or cancelled Connect run, `ConnectAsync` waits for the `Disconnected` state,
    bounded by the backend's `DisconnectTimeout` (5 s), before throwing, the same wait
    `DisconnectAsync` uses; b3270 answers the run before it reports `not-connected`, and on the real
    gateway that report lags by up to a few seconds.
14. `StatusFormatter.ConnectTimeout` takes the timeout as a parameter (a consequence of deviation 1);
    `WireLog.TryFromEnvironment` also catches `ArgumentException`; the splash is `Topmost` and not
    shown in the taskbar; `SessionViewModel.WireLogDirectory` is a public settable seam for tests.
15. The certificate checkbox reads "Always allow for this profile (stops checking its certificate)";
    the notices file also carries the Avalonia and CommunityToolkit.Mvvm MIT notices.
