# Tests

Notes for working under `tests/`. Commands, the four test lanes and the environment variables are in
`docs/development.md`.

- Every hand-written `.cs`, `.axaml` and `.sh` file needs the three-line licence header (see the root `CLAUDE.md`).
- Assertions on user-visible status strings (for example `"✕ Not connected"`) are exact; change `StatusFormatter`
  and its tests together.
- Every bug found in the field should add a trimmed replay fixture, documented in the fixtures README
  (`docs/development.md#replay-fixtures`).
- `Wait.UntilAsync(condition, what, timeout?)` lives once, in `tests/Shared/Wait.cs` (namespace
  `LizTerm.Tests.Shared`), compiled into the backend, App and integration test projects by a linked
  `<Compile Include=...>` item and imported by a `<Using>` in each of those csproj files, so its 5 s default cannot
  drift between projects.
- Each src project has `InternalsVisibleTo` for its own test project (the backend also for the integration tests).

## Backend tests

- `FakeB3270Process`: `Emit(line)` queues stdout, and `Exit(code)` ends the process. By default every incoming
  `r-tag` gets an automatic success `run-result`. A `Quit` run exits the process rather than being answered, as the
  real engine does, so `DisposeAsync` returns at once instead of waiting out its two-second timeout and killing the
  process. That wait alone was most of the project's runtime, so do not make `RunResponder` swallow Quit.
- Its other knobs: `RunResponder` controls replies; `AutoInitialize = false` suppresses the canned `initialize`
  block; `FaultStart` makes `Start` throw; `FaultWrite` makes stdin writes throw; `FaultWaitForExit` simulates a
  process that is already gone; and `BeforeWrite` runs a hook before each character reaches stdin (so a test can
  make the engine die part-way through a write).
- `ReplayTests` feeds a fixture through `Emit` and asserts on the resulting snapshot, cursor and connection-state
  sequence.
- `FakeTrustAnchorSource` (`Pem`, `Calls`) stands in for `ITrustAnchorSource` in connect tests.
- `EnvironmentCollection` is a non-parallel xunit collection holding `WireLogTests`, which sets `LIZTERM_WIRE_LOG`.

## App tests

- They run on Avalonia's headless platform: `TestAppBuilder` is registered with
  `[assembly: AvaloniaTestApplication]`. Control tests use `[AvaloniaFact]` and `KeyPressQwerty`, drive drags with
  the headless `MouseDown`/`MouseMove`/`MouseUp` helpers, and read `TerminalScreen.Selection` directly. View-model
  tests use plain `[Fact]` with `FakeEmulatorSession`. A view model built without a `SettingsViewModel` gets an
  in-memory one; a test that needs a failing save points a `SettingsStore` at a temp file holding `not json`.
- Drive the Preferences radios by raising `Button.ClickEvent`; assigning `IsChecked` only proves the one-way
  binding renders.
- `KeymapHintsTests` format every expectation with an explicit `KeyGestureFormatInfo` (Avalonia's common key names,
  the default modifier words): the headless platform registers a format of its own, and what it says is not ours to
  assert. Keypad buttons are driven by raising `Button.ClickEvent`, or by a headless mouse press at the button's
  centre (after `window.UpdateLayout()`) when focus or the modifier-tap rule is the question — a raised
  `ClickEvent` always reaches the click handler, so it can never see a press that never becomes a click.
- `FakeEmulatorSession` records calls as strings: `key:PF3`, `move:3,9`, `connect:noverify`, `connect:pin:<sha256>`,
  `wirelog:start:<path>`, `wirelog:stop`, `transfer:<Direction>:<HostFile>`. It also exposes `ConnectCompletion`,
  `ConnectToken`, `WireLogException`, `Engine`, `LastTransferRequest`, `TransferProgress`, `TransferToken`, and
  `TransferCompletion`, which it waits on when set so a test can drive the transfer dialog's Running phase.
  Its `Raise*` methods (`RaiseScreen`, `RaiseStatus`, `RaiseConnection`, `RaiseFault`, `RaiseHostMessage`,
  `RaiseBell`) fire the corresponding events.
- Other fakes: `FakeTextClipboard` (a `Text` string and an optional `Exception`); `FakeFilePicker` (returns
  `Result`, records `open` and `save:<name>`); `FakeCertificatePrompt` (`Decision`, `OnAsk`, `Calls`,
  `LastRequest`); `FakeFolderOpener`; `FakeUriOpener` (`Opened`, the list of URLs and paths handed to it; `Result`;
  an optional `Exception`); `FakeCertificateFetcher` (`Result`, `Exception`, `Calls` as
  `fetch:<host>:<port>`); `FakeBellRinger` (`Rings`, the list of `BellSound` values asked for, `Available`, what
  `CanRing` answers, and an optional `Exception`).
- Drive native menu items through `((INativeMenuItemExporterEventsImplBridge)item).RaiseClicked()`; the menu notes in
  `src/LizTerm.App/CLAUDE.md` say why.
- The picker's row menu is a classic `ContextMenu` on each `ListBoxItem`. Open it with a headless right
  `MouseDown`/`MouseUp` at the row's centre (after `window.UpdateLayout()`), or without a pointer by raising
  `new ContextRequestedEventArgs()` on the container — what the context-menu key does, and the only way to open it
  on a row that is not the selection. Find it by `IsOpen` across the containers rather than through the one
  clicked: `Show()` posts the window's activation and the first headless input flushes it, so a `Reload` runs
  inside that click and, if the store changed, recycles the containers. Raise `MenuItem.ClickEvent` on an entry to
  fire its command; a raised click bypasses the interaction handler's enabled gate, so assert
  `IsEffectivelyEnabled` for greying. A headless press never activates a window, so the reload-versus-press
  ordering of a real platform is not something these tests can see.
- The App tests name the backend in exactly one place:
  `SessionFactoryTests.The_session_it_builds_verifies_against_the_system_trust_anchors` casts the factory's result to
  `B3270Session` to read `TrustAnchors`, because what the factory injects is the one thing about the backend the App
  owns, and asserting it needs the concrete type. Every other App test stays on `IEmulatorSession`.
- `SessionFactoryTests` never touches the environment. It calls the internal
  `SessionFactory.Create(profile, overridePath, baseDirectory)` seam with a bogus override and an empty temp
  directory, because a bundled engine in the App test output would otherwise satisfy the locator.

## Core tests

- `TestCertificates` makes self-signed and CA-signed certificates and re-imports them through PKCS#12 so macOS
  accepts the key for a loopback `SslStream` server. It stays `internal` and is compiled into the integration
  project by a linked `<Compile Include=...>` item — a source-level share, not a project reference. `CreateRoot` and
  `Serial` are the shared parts of `CaSigned`, `CaSignedServable(subject)` (whose leaf can serve TLS) and
  `Ca(subject)` (the root alone, for a test that needs an anchor and no leaf). **Two CAs meant to be unrelated must
  be given different subjects**: OpenSSL looks an issuer up by subject name, so a shared name tests the
  wrong-signature path instead.
- `RepositoryHeadersTests` (`Repository/`) walks the tree and fails for a file missing its licence header. It lives
  in Core.Tests because that project builds on every run, so a new file is caught locally rather than in CI. The rule
  is the pure `LicenseHeader.IsPresent`, unit tested to *reject* an absent header, the wrong licence, someone else's
  copyright, the lines out of order, and a header pushed past the first eight lines — a guard that cannot fail is not
  a guard. The walk also asserts each scanned root contributed at least one file, so a root matching nothing cannot
  pass without reading anything. `LicenseHeader.Root` finds the repository from its own `[CallerFilePath]`, a
  compile-time path, so it resolves from `bin/` and on a runner alike, and it throws rather than skipping when it
  cannot. `LicenseHeaderTests` needs its real header for a second reason: its fixtures put an SPDX line inside the
  eight-line window, and without a real header the file would pass on its own test data.
- `UserGuideAssetTests` (`Documentation/`) lives in Core.Tests for the same reason `RepositoryHeadersTests` does:
  the project builds on every run, so an edited `docs/user-guide.md` whose bundled HTML was not regenerated is
  caught locally rather than in CI. It also holds the guide to what `UserGuideHtml`, the test-only converter, can
  render.
- `SettingsStoreTests` uses a temp directory per test like `ProfileStoreTests`; `SettingsLayersTests` needs no disk.

## Integration tests

- `EngineSmokeTests` starts the **bundled** engine from the test output through `B3270Session` and quits. It resolves
  with `B3270Locator.Find(null, AppContext.BaseDirectory)`, so `LIZTERM_B3270_PATH` can never satisfy it, and asserts
  the resolved path is under `B3270Locator.BundledDirectory` (`runtimes/<rid>/native`), which is what the csproj copy
  rule fills. It does *not* assert `Engine.Source` or re-parse the version: the constructor supplies the one and
  `StartProcessAsync` already enforces the other, so both would be unfalsifiable.
- Without a bundled engine it skips, unless `LIZTERM_REQUIRE_ENGINE` is set, when it fails. A binary that is
  *present* but did not resolve — the locator's not-executable arm, which is what a downloaded CI artifact looks like
  before `chmod +x` — always fails, whatever the variable says: that is a broken engine, not an absent one.
  `BundledEngine.Require()` is the one place that decision is spelled out, called by every test in the project that
  needs the engine; `EngineRequirement.Decide(found, present, variable)` is the gate, unit tested on its own; and
  `B3270Locator.Candidates` is how the test tells found from present (`Find` throws the same type for both).
- The live tests carry a 10-minute xunit timeout. `gateway-pinned-login.jsonl` replays a verified pinned connect.
- `ScreenWaiter` and `TsoNavigator` drive a TSO logon to READY by screen text only: LOGON, PASSWORD, `***` pauses
  answered with Enter, menus left with PF3, READY = the last non-blank line. Extend them with text rules, never
  coordinates, when a host's screens differ. On MVS/CE the flow is a Hercules banner (Enter), the `===>` logon
  screen, the password prompt, then READY. The navigator waits for 500 ms of screen quiet before keystrokes and before
  trusting READY, because IND$FILE typed into a half-repainted field fails with `INVALID COMMAND NAME SYNTAX`.
- On Robert's Mac the live-lane variables are kept in `~/.config/lizterm-test.env`, outside the repo; `source` it in
  the shell that runs `dotnet test` rather than exporting values on a command line. A wire log of the IND$FILE run
  holds the password on its outbound side and is never committed.
