# LizTerm.App

Notes for working in this project, the Avalonia UI. It names `LizTerm.Backend.B3270` only in `SessionFactory.cs`;
everything else talks to `IEmulatorSession` (see `src/LizTerm.Core/CLAUDE.md`). The csproj's engine-copy rules
(`LizTermTargetRid`, `LizTermEngineRid`) are documented in `docs/engines.md`; read it before touching them.

**Never rename the assembly.** Avalonia resource URIs are keyed on it (`avares://LizTerm.App/...` for the terminal
font, the window icons and the licence text), and a rename breaks every one of them at run time, not build time.
The name users see on macOS comes from `LizTerm.parcel`'s `GeneralSettings.PackageName` instead.

## Startup and shutdown

- `App` shows `SplashWindow` first: at least 1 s, at most 2.5 s, dismissed by a click or key, and timed on a
  `Stopwatch` started at `Opened` so a slow cold start or a clock step cannot skip it. Meanwhile it checks the engine
  through `SessionFactory.CheckBackend` and runs a `StartupPlan` through `StartupGate`: `StartupErrorWindow` when the
  engine is missing, else the session for a resolved argument, else the picker. The gate fires once the splash has
  closed *and* the plan is known, in either order. A splash already past its maximum closes from inside `Show()`, so
  `Closed` is subscribed before `Show()` is called; a missed signal would strand the process with no window.
- `StartupArguments.Parse` records the argument as typed in `Argument` and, when it also reads as a host, the ad hoc
  `[L:][Y:][lu@]host[:port]` fields beside it. It does not choose between the two: the ad hoc forms overlap legal
  profile names (`CONS01@tk5`, `a:b`), and only `Resolve` has the saved list, where an exact name match always wins —
  even for an argument that is a usage error as a host. A `<letter>:` head counts as a prefix only when what follows
  could be a host, so `l:3270` is host `l` on port 3270. A port must be plain digits in 1..65535 (no sign, no
  surrounding space), so `mvs.local:abc` and `mvs.local:99999` are usage errors rather than hostnames containing a
  colon. A syntax error prints the usage line and opens the picker.
- `App.OpenSession` creates the `SessionWindow` first (the clipboard adapter needs it), then a `SessionViewModel`
  around `SessionFactory.Create(profile)`, shows the window, and starts `ConnectCommand`.
- `ShutdownMode` is `OnExplicitShutdown`: closing the last session window reopens the picker, and closing the picker
  with no sessions open quits. **Both are gated on `ShutdownPolicy`** (`Startup/`), because neither may happen while
  the app is on its way out. `ClassicDesktopStyleApplicationLifetime.DoShutdown` closes every owner-less window and
  then cancels the shutdown if any window remains, so a picker opened from inside that close *cancels the shutdown
  that caused it*, and the app refuses to quit. `App.Quit()` sets `_quitting` for its own path, but the paths
  Avalonia drives bypass that flag: the macOS application menu's Quit calls `TryShutdown(0)`, and so does an OS
  shutdown. So each window's `Closing` records `ShutdownPolicy.IsShutdown(e.CloseReason)` (`ApplicationShutdown` or
  `OSShutdown` — both, never just one), and `Closed` asks `UserClosedLastWindow` before acting. It is read in
  `Closing` because `Closed` carries no reason. A close that the owned File Transfer dialog refuses never reaches
  `Closing` at all (`ShouldCancelClose` asks the children first), and the next attempt overwrites the flag, so it
  always describes the close that is actually finishing. The picker uses the same test for the mirror-image failure:
  a shutdown that closed it would otherwise answer with `Quit()` → `Shutdown()`, a second `DoShutdown` re-entered
  inside the first.

## Session view model

- `SessionViewModel` takes an `Action<Action> dispatch` to marshal backend events onto the UI thread; the app passes
  `Dispatcher.UIThread.Post`, tests pass `a => a()`. Clipboard, file dialogs, the certificate prompt and folder
  opening are injected the same way: `ITextClipboard` (`AvaloniaTextClipboard(window)`), `IFilePicker`,
  `ICertificatePrompt`, `IFolderOpener`, `IUriOpener` (`Files/`; Help's project links and the bundled user guide,
  beside `IFolderOpener`). `Documentation/UserGuide.cs` reads the guide as an embedded resource
  (`avares://LizTerm.App/Assets/Docs/user-guide.html`) and writes it out before `IUriOpener.OpenFileAsync` opens it.
  The process's `SettingsViewModel` is injected the same way and defaults to an in-memory one, so no
  view-model test touches the settings file; its `SaveFailed` lands in `ErrorMessage`.
  `IBellRinger` (`Bell/`; the app's one `SystemBellRinger`, `FakeBellRinger` in tests) is injected after it; null
  (tests) means the flash is the whole bell. An optional `BellThrottle` comes last, so a test that needs two
  admitted bells drives an explicit clock instead of sleeping.
- Rejected actions (`EmulatorActionException`) are deliberately swallowed, because b3270 already explains them
  through the keyboard lock. Only unexpected and backend-unavailable errors set `ErrorMessage`. `SessionWindow`
  refocuses the screen after the error bar's Dismiss.
- A connect times out after `ConnectTimeout` (30 s), and the Disconnect item cancels a pending one. The timeout
  message states only what was observed — an open socket with no 3270 session — and offers TLS as a *possibility*:
  b3270 reports nothing that tells a TLS listener apart from a host that accepted the socket and went quiet, and
  confident TLS advice to someone whose host does not speak it makes things worse.

### Certificate prompt

- A certificate failure offers connect-anyway through `ICertificatePrompt` (`Dialogs/`), asked with a
  `CertificatePromptRequest`: the reason lines; what the host presented, read by the injected `ICertificateFetcher`
  for TLS profiles only, under a fresh `ConnectTimeout` source; the previous pin; `CanPin`; and `CannotPinReason`.
- `CanPin` needs a TLS profile, an engine that can pin (`CanPinCertificates`), a pinnable certificate, and a
  fingerprint that differs from the pin in force — which is what stops a rejected pin from being offered again.
- `CanPin` is deliberately **not** gated on the profile being a saved one. A pin lives in `_pinOverride` for the
  window's life whether or not a file stands behind the session. Gating on a saved profile made Quick Connect's
  headline case (#29, an ad hoc connection as the start of a profile) impossible: an ad hoc TLS session got no pin
  option and no `CannotPinReason`, so File > Save as Profile produced a profile that failed verification on every
  later connect. What a saved profile changes is only whether the accepted pin is *also* written back:
  `_saveProfile` is null for an ad hoc session, and the write-back is `?.Invoke`.
- "Trust this certificate for this profile" pins: the profile is saved with the pin and verification on, and
  `_pinOverride` carries the pin for the window's life because the session's profile is fixed. Connect Anyway
  without it is one attempt with verification off. A changed certificate reopens the same window titled
  "Certificate changed", with both fingerprints. The profile editor shows a pinned profile's fingerprint with a
  Forget button, the only way back to default trust.
- The prompt and the save run *after* the connect's catch clauses, never inside one, so their own failures reach the
  error banner instead of faulting the command.

### Wire log, About and the engine

- The view model owns the Help menu's wire log toggle. Logs go to `<config>/logs/wire-<profile>-<timestamp>.log`,
  and names gain `-2`, `-3` when two starts land in the same second (`UniquePath`). `StatusFormatter`'s fault text
  points users at Help > Wire Log. When `IsWireLogging` cannot start a log, it marshals its own correction back to false
  through `dispatch` rather than assigning inline: a value corrected from inside its own change notification is
  invisible to the menu item's two-way binding, which is still writing target to source, so the item would keep a
  check mark for a log that never started and swallow the next click. `ShowWireLogsCommand` opens the folder through
  `IFolderOpener`.
- `App.ShowAboutAsync` is the one route to About, for a session's Help item and the macOS application menu alike. It
  holds one dialog at a time in `_about`; a second request activates it instead of stacking, because the macOS menu
  bar stays live over a modal dialog and a second About would be owned by the first.
- Which engine About names is `App.AboutEngine`, not "whatever window is in front": the owner's session if it has
  one, else the last session the user was in (`_lastActiveSession`, tracked on `Activated`), and only then the
  located binary. Resolving from the owner alone would show no version whenever a dialog or the picker was on top of
  a running session. `ActiveWindow` skips `SplashWindow`, which closes itself on a timer and would take an owned
  About with it.
- `SessionFactory.Refused` uses `B3270Locator.Candidates` to tell a missing engine from one that is present but not
  executable, so a file that exists keeps its own source, and About and the status bar call it bundled or name the
  override instead of calling it missing. `CheckBackendOrUnknown` and `Create` both go through it, so they cannot
  disagree about the same binary.
- **`EngineInfo.Path` reaches no UI.** It is carried to start the process and to keep those two entry points
  describing one binary identically; `StatusFormatter.Engine` has never emitted it, and About stopped showing it in
  #75, because every platform ships an engine `engines.yml` built and `verify-bundled-engine.sh` gated. The file to
  `chmod` for a present-but-unusable engine is named by `B3270Locator.Find`'s own exception, which reaches the user
  through `StartupErrorWindow` and the session's error banner. Do not restore a path line to About to name it.

### Settings and Preferences

- `SettingsViewModel` is the app-wide settings as one live object, created once by `App` (`App.Settings`, lazy
  with `??=` like `_store`) and shared by every session window and the Preferences window; property change
  notification on it is the whole live-propagation mechanism. A setter applies the change in memory, raises
  the change, then writes through `SettingsStore.Update` with the same change function — the in-memory value
  always wins. A failed save sets `LastSaveError` (the window shows it) and raises `SaveFailed` (every session
  banner shows it; the event fires on every failure, where an unchanged property text would not re-notify a
  dismissed banner).
- `TerminalScreen.BlinkEnabled` (bound to `Settings.Blink`) stops the blink timer and clears the hidden phase;
  the snapshot still says what the host asked for.
- **The bell.** `IEmulatorSession.BellRang` is marshalled like the other events, then `SessionViewModel.OnBell`
  runs: disposed check; return if neither output has anything to do, so a bell nobody could perceive does not
  start the interval; one `BellThrottle` (`BellInterval`, 500 ms, one gate for both outputs, refused bells are
  dropped not queued); then `Settings.VisualBell` raises the view model's own `BellRang` (the window calls
  `TerminalScreen.Flash()`, a 120 ms `Palette.BellFlash` overlay painted exactly while the control's one-shot
  timer runs) and `Settings.BellSound` other than `None` calls the ringer. "None means silence" is the view
  model's rule; the ringer only knows how to make sounds. `IBellRinger.CanRing` is the one home of which sounds
  this platform can make: the view model treats a refused sound as `None`, and `App` passes the same answer to
  `PreferencesWindow`, which disables the radio and says why. `SystemBellRinger` is the App's only P/Invoke
  (`NSBeep`, `MessageBeep`) and answers no on Linux. A ringer that throws is reported once and not asked again
  until the sound setting changes. The bell's sound radios are one-way check marks plus Click handlers over
  `BellSoundConverter`, the Crosshair shape; both converters are subclasses of `EnumIsConverter<TEnum>`, which
  holds the rule.
- **The release check (#107).** `App._releaseChecker` (`GitHubReleaseChecker.Create()`) is the process's one
  checker. `CheckForUpdatesOnStartupAsync()` fires once from `Execute`, after a session or the picker has actually
  opened — never after `ShowError`, never when opening either one threw — and stays silent unless
  `Settings.CheckForUpdatesAutomatically` is on (read before the request and again after it), the check finds a
  newer release, and that release is not `Settings.SkippedUpdateVersion`
  (`UpdateNotificationPolicy.ShouldShowAutomatically`). Nobody asked for that result, so its owner is the last
  session or the picker, never a dialog in front of them. Help's own `CheckForUpdatesManuallyAsync(Window?)` always
  shows a result, ignoring any skip, and brings a result already on screen forward before asking GitHub again.
  Checks that overlap share the request still out (`_checking`). Both funnel through `ShowUpdateCheckResultAsync`,
  one `UpdateCheckWindow` at a time (`_updateCheck`, the `_about`/`_preferences` shape) — except that its owner can
  close during the request, so an owner no longer visible falls back to `ActiveWindow()`, and a show that throws
  gives the slot back. The dialog's Download opens the page through `LinkOpening` over an `AvaloniaUriOpener` built
  on the dialog itself, the same rule and wording as Help's links, and `UpdateChecker` only lets a page under
  `ProjectLinks.Releases` through, since the platform launcher opens any scheme. Remind Me Later, not Download, is
  the default button and first tab stop: the automatic check can open over a terminal the user is typing into.
  Skip writes `SkippedUpdateVersion`. Each public entry point has an internal overload taking the checker and the
  `SettingsViewModel` explicitly, `ShowPreferences(SettingsViewModel)`'s shape, so tests never touch the network or
  the real settings file.
- `App.ShowPreferences` is the one route to `PreferencesWindow`: modeless, unowned, one at a time in
  `_preferences` the way About is in `_about`. The internal overload taking a `SettingsViewModel` is the test seam.
  The window's crosshair radios are one-way check marks plus Click handlers, exactly the View menu's shape.
- **The window is four tabs** — General, Display, Bell, Window — on a `TabControl` named `Tabs`, each tab a
  `StackPanel` of rows, each row its own `Grid` with a 120 px label column (the profile editor's shape). A short
  enum is one horizontal row of radios; only the menu bar keeps a stack, because its labels are sentences. A new
  setting joins the tab it belongs to (#78's theme and cursor go in Display); a new area is a new tab — General
  itself is the most recent (#107's Updates toggle), and #79's logging and #18's keyboard are next. The size is
  fixed rather than `SizeToContent`, because only the selected tab is measured and a window sized
  to its content would change height on every tab switch; the Window tab is the tallest and sets the height.
  `FindControl` reaches a control on an unselected tab (the name scope is the window's), so the tests never select
  a tab first.
- The Preferences **Keypad** row is a two-way `KeypadBox` for `Settings.Keypad` plus the two dock radios over
  `KeypadDockConverter`, the Crosshair shape. Both settings are also on the View > Keypad submenu (#71); the row
  stays because Preferences is where a user goes looking for settings, and both doors write the same properties.
  Under the radios, a two-way `KeypadPfKeysBox` for `Settings.KeypadPfKeys` (#105) is the one keypad setting with
  no menu item, on purpose: it is set once, where showing the keypad is flipped while working (around a screenshot,
  say), so it stays out of View > Keypad.

## Terminal screen (`Controls/TerminalScreen.cs`)

- A custom `Control` that draws each row as runs of identical style, segmented by `Cell.SameStyleAs` — each run a
  rectangle, brushes and shaped `FormattedText` — scaled to fit via `CellGeometry.Fit` (pure math, unit tested).
- The run list is cached for as long as the snapshot instance and the `CellGeometry` are unchanged, so a blink phase
  flip (a full `InvalidateVisual` twice a second, for as long as anything blinks) redraws prepared runs instead of
  re-segmenting and re-shaping every cell. A new snapshot or geometry rebuilds it; `RunPlanBuilds` is the test seam.
- `OiaFontSize` is the status bar's font size, the cells' own, and it is set from `ArrangeOverride` only, never from
  `Render`. The bar's height is part of what the screen fits into, so following the cells can feed back;
  `StatusBarFont` follows that echo like any change and holds only a bounce — the cells returning, inside the same
  layout pass (ended by `LayoutUpdated`), to the size the bar just left, a fit with no consistent answer — and keeps
  holding it on later repaints at the same fit, so a host screen update never flips the bar. Everything else is
  followed, one size included: cell sizes are whole pixels, and holding any one-size move left the bar a size off
  the screen. `TerminalScreenLayoutTests` walks a real bar through every height and fails if the bar sits off a size
  that would have settled.
- Blink uses a 750 ms phase, never below 500 ms, and asks `ScreenSnapshot.HasBlink` rather than rescanning the grid.
- **Overlays are painted, never folded into the run plan.** `Selection`, `Crosshair`, and `FindMatches` with
  `CurrentMatch` are styled properties painted in `Render` after `EnsureRunPlan`'s cached runs. A crosshair mode or a
  match list is recomputed on every host repaint and must not cost a re-segmentation each time;
  `TerminalScreenCrosshairTests` and `TerminalScreenFindTests` assert that changing them leaves `RunPlanBuilds`
  unchanged. `CrosshairGeometry.Rects` and `FindMatchGeometry.Rect` are pure helpers, asserted on rectangles rather
  than pixels.
- The crosshair is an app-wide setting (`SettingsViewModel.Crosshair`, remembered across sessions) and deliberately
  does not use b3270's own `CROSSHAIR` toggle: the engine has no display, and routing a display preference through a
  child process would only make the crosshair unavailable while disconnected.
- It raises `KeyRequested`, `TextEntered` and `CellClicked`, which `SessionWindow` wires to the view model.

### Keyboard

- Key events go through `TryHandlePlatformGesture` first, then `Keymap.TryMap`, then `Keymap.TryText` (Ctrl+[ types
  `¬`, Ctrl+6 `¢`), then fall through to Avalonia's text input, so dead keys and IMEs work.
- `TryHandlePlatformGesture` checks the platform's copy, paste, select-all **and Find** hotkeys, ahead of the keymap.
  Do not add a Ctrl+F chord to `DefaultKeymap`: it would silently shadow Find on Windows and Linux, where the classic
  menu makes this control the only dispatch path for it.
- `Keymap` (`Keyboard/`) is an immutable table of `KeyChord(Key, Modifiers, Tap)` to `TerminalKey`, built by
  `DefaultKeymap.Create(destructiveBackspace)` (two cached instances) from Vista TN3270's defaults, cross-checked
  against wc3270 in the M2 hardening spec, section 6.2. `docs/user-guide.md` has the full table; keep it in step.
  `Keymap.With` is the seam for future user remapping, and nothing else about remapping exists. The control's
  `DestructiveBackspace` property (default true, bound to the profile) picks the table.
- Vista's Ctrl+Insert for PA1 is not in the table: Avalonia's `PlatformHotkeyConfiguration` puts Ctrl+Insert into
  Copy on every platform, the Meta-based macOS table included, and platform gestures are checked first. PA1 is
  reached through Alt+1 or the Keys menu.
- A Left Ctrl tap is Reset and a Right Ctrl tap is Enter. `ModifierTapDetector` sees a Ctrl key go down and the same
  key come up with nothing between (`OnKeyUp` looks up `KeyChord.TapOf`); another key, a pointer press, a wheel turn,
  focus loss and the window deactivating all reset it.
- The screen's key, copy, paste and select-all events call the view model's public methods (`SendKeyAsync`,
  `CopyAsync`, `PasteAsync`, `SelectAll`) directly, as `TextEntered` and `CellClicked` do. Each method carries its own
  guard, and a keystroke is never dropped for arriving while the previous one's round trip is still open. The
  `[RelayCommand]`s on the same methods serve the menus, which keep CommunityToolkit's default of disabling an async
  command while it runs.

### Mouse and clipboard

- `SelectionGesture` (`Mouse/`) is the pure press/move/release/double-click state machine. `TerminalScreen` feeds it
  pointer events, exposes `Selection` (two-way), paints `Palette.Selection` after the text and before the cursor, and
  clears the selection when the screen size changes. It writes its own `Selection` with `SetCurrentValue` so a
  binding survives, and `OnPointerCaptureLost` ends a drag.
- A plain click moves the cursor on release; a double-click selects the run of non-space cells.
- `CopyRequested`, `PasteRequested` and `SelectAllRequested` come from `GetPlatformSettings().HotkeyConfiguration`
  (Cmd on macOS, Ctrl elsewhere, Ctrl as the fallback).
- The view model owns Copy (trimmed rows joined by `\n`), Paste (CRLF normalized, one `PasteTextAsync`) and Select
  All, and nulls `Selection` on every path that sends input to the host.

## Keypad (`Controls/Keypad.axaml`)

- A `UserControl` that mirrors `TerminalScreen`'s contract: it raises `KeyRequested` and knows nothing about view
  models. Its buttons are built from `KeypadLayout.Banks` (three banks of twelve; `KeypadKey` is a label plus a
  `TerminalKey`), so no button is written by hand. `Dock` (`KeypadDock`) lays the same `UniformGrid`
  out bank-per-row at the bottom or bank-per-column on the right; `Keypad.DockPanelDock` is the converter the
  window's `DockPanel.Dock` binding uses, two bindings to one setting because the grid's shape is the control's and
  its edge of the window is the window's. The window binds `IsVisible` to `Settings.Keypad`, `ShowPfKeys` to
  `Settings.KeypadPfKeys` and `IsEnabled` to `IsConnected`. `NativeMenuTests.Every_key_on_the_Keys_menu_is_on_the_keypad` holds the menu to a subset of the
  table.
- **`Build` runs on the first show, not in the constructor**, because the keypad is off by default and a window that
  never shows it should build no buttons and format no tooltips: `OnAttachedToVisualTree` when already visible, and
  the `IsVisible` change otherwise. Everything after that point reads `_banks`, one list of buttons per bank —
  `Relayout` walks the real banks rather than indexing a flat list by `BankSize`, which only `KeypadLayout`'s table
  promises. With `ShowPfKeys` off it leaves out every bank made only of PF keys, judged by the buttons' keys rather
  than their position, and keeps those buttons for when it is turned back on. `Relayout` also owns the border's `VerticalAlignment` (`Top` on the right), never the control's: how a
  host aligns this control is the host's.
- **Every button is `Focusable = false`**, so a click never moves the keyboard off the screen; the window still calls
  `Screen.Focus()` after each key as the guarantee. A click goes to `SendKeyAsync`, never the command (see Keyboard
  above for why). Ending a modifier tap is *not* the keypad's job: it is a rule about the window, and
  `SessionWindow`'s one tunnelled `PointerPressed` handler calls `TerminalScreen.CancelTap()` for every press the
  screen does not see. Hooking it to the keypad's `Click` instead missed the presses that never become one — the
  border's padding, the margins between buttons, a button the pointer leaves before releasing — and each of those
  sent a spurious Enter on the Ctrl release.
- Tooltips come from `KeymapHints.Describe` (`Keyboard/`), the reverse of a `Keymap`: chords ordered unmodified
  first, then by modifier, function keys ahead within a group, taps last; ordinary chords through Avalonia's
  `KeyGesture.ToString("p", format)`, taps worded by hand. The overload taking a `Keymap` and one `TerminalKey`
  scans the table; the one taking the chords themselves is what the control uses, over a single `ToLookup`, so 36
  tooltips are one pass and not 36. The control passes a null format, the platform's registration (glyphs on macOS,
  words elsewhere); tests pass an explicit `KeyGestureFormatInfo`. The control's `Keymap` property is the #18 hook,
  nothing binds it yet, and a null from that future binding leaves the tooltips as they are rather than throwing.

## Menus

One `NativeMenu` per window, plus an application-level one in `App.axaml` holding **About and Preferences**, which
is what gives the picker a menu bar on macOS. Each window's menu is rendered natively, by the classic in-window
`<Menu>`, or by both at once — `MenuStyle` says which. Both renderers are permanent (#70).

### Style

- `MenuStyle` (`src/LizTerm.Core/Settings/`) is `Auto | Native | InWindow | Both`, saved in `AppSettings`. `Auto`
  is what an untouched file reads as and is never acted on: `MenuStrategy.Resolve` turns it into `Native` on macOS
  and `InWindow` elsewhere. `Resolve`, `FromVariable`, `MenuStyleChoosable`, `AboutInExportedHelpMenu`,
  `PreferencesInExportedEditMenu` and `PreferencesGesture` are pure and take the platform as an argument, so every
  combination is testable anywhere.
- **The native menu is a macOS feature and nothing else**, and that is Avalonia's shape rather than work not yet
  done: macOS is the only platform with a native menu exporter. Win32 has none and Linux's `DBusMenuExporter`
  hands the menu to whatever global-menu registrar the desktop runs, so off macOS "native" is either a second
  in-window bar beside the one that already works or a bar the desktop moved. Decided 2026-09-12; revisit through
  #22 only if Avalonia's support grows. Do not reintroduce a way to reach `Native` or `Both` off macOS.
- **`Resolve` answers `InWindow` for everything off macOS**, and `MenuStyleChoosable` is the same rule seen from
  Preferences — a style offered where `Resolve` ignores it, or honoured where Preferences hides the row, is the
  bug. `Both` off macOS is two bars stacked (both renderers draw in-window there, both docked `Top`) and `Native`
  is the unreviewed renderer or a global-menu registrar; a settings file carried from a Mac, or hand-edited, is
  how those states would otherwise be reached with no control to leave them. `Resolve` is also where a value that
  is not a member at all is normalised: a settings file is text, `JsonStringEnumConverter` accepts integers, and
  `"menuStyle": 9` reads back as `(MenuStyle)9` rather than falling to `Auto` the way an unknown *name* does.
  `ApplyMenuStyle` throws for anything unresolved rather than rendering it, since it names no renderer.
- **`LIZTERM_MENU` seeds, it does not override.** `App` resolves it once through `MenuStrategy.FromVariable` and
  calls `SettingsViewModel.SeedMenuStyle`, which changes the value in memory and notifies but never writes. The
  Preferences radios then work normally for the rest of the session, and a change there saves. The seed cannot
  reach the file later either: `SettingsStore.Update` applies each change to the record it re-reads from disk, so a
  save carries only the key the user changed. `A_seeded_menu_style_applies_in_memory_and_never_reaches_the_file`
  guards that. The `MenuStyle` setter carries the one exception to the unchanged-value guard every other setter
  has: while a seed is in force an *equal* value still writes, because the seeded style is the one the radio
  already shows, and clicking it is the user asking for it to become their preference.
- **The style is live.** `SessionWindow` follows `SettingsViewModel.MenuStyle` from its `Opened` handler (not from
  `OnDataContextChanged`, which only re-points the field: a window built and never shown never raises `Closed`
  either, so a subscription taken there would outlive it), and applies only *changes* — the constructor's argument
  outranks the data context, because the App tests build a window in a named style and hand it a view model whose
  `Settings` is its own in-memory instance reading `Auto`. `ApplyMenuStyle` is
  therefore re-entrant, and `_stashedMenuItems` is what makes `InWindow` a state a window can leave: emptying the
  declared menu removes the items and nulls their `Parent`, so the same objects are kept and added back. Back to
  the *same* instance, always — see the `#60` note below.
- `SessionWindow`'s parameterless constructor takes the platform default and nothing else; `App` passes the user's
  style explicitly. A window that read `App.Settings` itself would open the real settings file from every headless
  test that builds one, because the tests run the real `App` and `App.Settings` is lazy.
- In the pinned Avalonia 12.1.2, the in-window rendering binds `NativeMenuItem.Gesture` only to
  `MenuItem.InputGesture`, which is display-only; `MenuItem.OnKeyDown` and `MenuBase.OnKeyDown` are empty, and only
  `MenuItem.HotKey` dispatches. So the fallback bar shows a shortcut but never fires it, and double dispatch is
  impossible on Windows and Linux by construction. What is still unreviewed there is mnemonics and appearance.
- **`InWindow` empties the declared `NativeMenu`**, removing its items from the end one at a time. Hiding
  `NativeMenuBar` detaches nothing: the window's own `ITopLevelNativeMenuExporter` exports `NativeMenu.Menu`
  directly, and `NativeMenuBar` only consumes the same property. Left populated, `InWindow` on macOS would still
  install the AppKit key equivalents and draw the system bar beside the in-window one, and on Linux the default
  style would still hand the menu to a global-menu registrar (Plasma's Application Menu applet, Unity). `Both` is
  precisely that un-suppressed state, asked for on purpose.
- **Never detach it** with `NativeMenu.SetMenu(this, null)`, and never replace it with a new empty `NativeMenu`
  (#60). Avalonia 12.1.2's macOS `AvaloniaNativeMenuExporter` binds its native proxy to the first `NativeMenu`
  instance a window is given, and `Update` throws "The menu being updated does not match" for any other instance, so
  every macOS launch with `LIZTERM_MENU=classic` fell to `StartupErrorWindow`. Emptying the same instance removes and
  disposes every native item, and an item-less NSMenu installs no key equivalent. Linux's `DBusMenuExporter` sees the
  same thing either way, and Win32 has no exporter.
- `SessionWindow.ExportedMenu` is the style-aware accessor every native lookup goes through: null under `InWindow`,
  since the emptied menu is still attached and `MenuLookup.Required` would throw for a present menu lacking an item.
  `ShowPlatformGestures` therefore finds no native Edit item and no-ops there, while the classic `InputGesture`
  assignments still run. A refill has to re-run both it and `ApplyPlatformMenuRules`, since the items come back
  never having had either applied.
- Headless tests cannot tell emptying from replacing, since both pass the attached-property check, so the other half
  of that guard is launching with `LIZTERM_MENU=classic` on a Mac: a window opens, and a bare F1 reaches the host as
  `PF(1)`. That was last checked with a key injected through the DevTools MCP, which enters downstream of
  `NSApplication.sendEvent:`, so a real keyboard press is still the proof that no key equivalent survives.

### The application menu

It declares About and Preferences, and no Quit, on purpose. `AvaloniaNativeMenuExporter.SetMenu` appends AppKit's
standard block (Services, Hide, Hide Others, Show All, and Quit with Cmd+Q) unless
`MacOSPlatformOptions.DisableDefaultApplicationMenuItems` is set, which `Program.cs` does not do. A declared Quit
shipped a second Cmd+Q item, and Avalonia's is the one this app wants: it calls `TryShutdown(0)`, which a running
IND$FILE transfer correctly refuses, where ours forced `Shutdown()`.
Do not set `DisableDefaultApplicationMenuItems` to "own" the block; that means re-implementing Services, Hide, Hide
Others and Show All to get back what is already free.

Because the application menu puts About and Preferences in the system menu bar, `ApplyPlatformMenuRules` hides the
*exported* window menu's copies on macOS, so neither is listed twice in that bar. The in-window menu is not that bar
and keeps both, with their separators, in every style and on every platform (#103). An end user who picked **Inside
the window** stopped looking at the system menu bar and found no visible way back to Preferences, because the
in-window copy used to be hidden too. Do not hide the in-window copies again to match the exported ones.

macOS also appends Start Dictation and Emoji & Symbols to any menu titled **Edit**. Both are harmless — they reach
the host through the text input `TerminalScreen` already handles, and Ctrl+Cmd+Space collides with nothing in
`DefaultKeymap` — but, like the application menu's block, they are invisible to the parity guard, which walks the
*declared* menu. Expect macOS to show more Edit items than any test asserts.

### Gestures

**No window menu item outside Edit ever carries a `Gesture`.** On macOS a `NativeMenuItem` gesture becomes an AppKit
key equivalent that `NSApplication.sendEvent:` dispatches before the key window's responder chain, so `Gesture="F1"`
would silently swallow PF1 — `TerminalScreen` would never see the key. So View (Crosshair and Keypad), File > Save
Screen As... and Edit > Copy Screen as HTML carry none. **The one exception is Preferences... on the application
menu**, with Cmd-comma (settings spec §5.4): the application menu exists only on macOS, `DefaultKeymap` binds no Cmd
chord, and Edit's own Cmd+C, V, A and F are already key equivalents of exactly this class.
`NativeMenuTests.The_application_menu_carries_cmd_comma_on_preferences_and_nothing_else` holds it to that one. The
in-window Edit > Preferences... *names* the same chord on macOS through `MenuItem.InputGesture`
(`MenuStrategy.PreferencesGesture`), which dispatches nothing. The application menu's key equivalent stays the one
handler, and it works under every style because the application menu is there under every style. The exported
Edit > Preferences... is hidden on macOS and carries no `Gesture`, so it installs no second key equivalent.

- Edit's Cmd/Ctrl+C, V and A come from `GetPlatformSettings().HotkeyConfiguration` and activate `CopyAsync`,
  `PasteAsync` and `SelectAll` directly, never the `[RelayCommand]`s, which disable while running.
- Edit > Find... is the one other Edit item with a gesture, because Edit is the menu with an established safe route for
  one: `ShowPlatformGestures` builds it as `new KeyGesture(Key.F, hotkeys.CommandModifiers)`, since
  `PlatformHotkeyConfiguration` has no Find to read.
- View > **Crosshair** is a submenu of four radio items rather than four items directly under View, because
  "Horizontal" and "Vertical" sitting under View read as window tiling. On macOS `ToggleType="Radio"` marks the chosen
  item with a bullet, not a tick; that is AppKit's own radio mark, not a bug.

### Wiring rules

- **Every native item needs a `Command` or a `Click` handler**, whatever else it carries. The macOS exporter enables
  an `NSMenuItem` only when `(Command != null || HasClickHandlers) && IsEnabled`
  (`__MicroComIAvnMenuItemProxy.UpdateAction`), and the in-window fallback gates `RaiseClicked` on `HasClickHandlers`
  alone, so an item carrying only a binding is greyed out on macOS and inert everywhere.
  `NativeMenuTests.Every_native_item_can_actually_be_activated` guards this; the parity test cannot, since a classic
  `MenuItem` with the same null `Command` works fine.
- `MenuItem.Click` and `NativeMenuItem.Click` have different delegate shapes, so each shared action is two one-line
  handlers over one method.
- A Click-driven native item must bind its own `IsEnabled`, since it gets none of the greying a command's
  `CanExecute` gives the classic item. Edit binds `CanCopy`, `IsConnected`, `CanSelectAll` and `CanFind`, all public on
  `SessionViewModel` for exactly this. The first three are also the classic commands' `CanExecute`; `CanFind` gates a
  Click-based item on *both* menus, since opening the bar means focusing `FindBox`, a window-level concern no
  `[RelayCommand]` can reach. Either way both menus read one property and cannot drift. It matters because on macOS
  these items are key equivalents, and an enabled one is an offer the app cannot honour. Binding it is safe:
  `NativeMenuItem` overwrites `IsEnabled` only when its `Command` changes, and these carry none.
- **Wire Log is the one item whose two menus differ on purpose.** A `NativeMenuItem` never toggles itself
  (`RaiseClicked` raises Click and executes Command, and never touches `IsChecked`), so the native item is
  `Mode=OneWay` plus `OnWireLogClickNative`, which flips `IsWireLogging` and lets the binding carry the new state back
  to the check mark — including a correction to false. The classic item stays `TwoWay` with no handler, because
  `DefaultMenuInteractionHandler.Click` toggles a `MenuItem`'s `IsChecked` *before* raising Click. That ordering is
  also why OneWay is required rather than tidy: the in-window fallback runs the same handler over a `MenuItem` bound
  two-way to the `NativeMenuItem`, so with a TwoWay binding to the view model there would be two toggles and the
  click would do nothing.
- **Check for Updates... follows About and Preferences' wiring, not `OpenLinkCommand`'s** (#107): a `Click`
  handler on both menus, `SessionWindow.CheckForUpdatesAsync`, which reaches `App.CheckForUpdatesManuallyAsync`
  with `this` as the dialog's owner. `OpenLinkCommand` has no way to pass a window along, which this item needs
  and a plain Help link does not.
- View > Keypad is a **submenu** on *both* menus, the Crosshair's structure: a `Show the Keypad` check box
  (`ToggleKeypad`, flipping `Settings.Keypad`), a separator, then `At the Bottom` and `On the Right` radios over
  `KeypadDockConverter` (`SetKeypadDock`, writing `Settings.KeypadDock`). Every one is a one-way `IsChecked` plus a
  Click handler, so Wire Log stays the only item whose two menus differ. What it cannot copy from the Crosshair is
  the single radio group: these are two settings and not one enum (keypad spec §2.1 — a `Hidden` member would
  forget the dock every time the keypad was hidden), and the separator is that seam rather than decoration. The
  menu writes the same `SettingsViewModel` properties Preferences does, so the dock is saved to `settings.json`
  whichever door changed it; the menu holds no state of its own (#71). The headers are shorter than Preferences'
  ("At the Bottom" against "At the bottom of the window") because the submenu's own name is the missing context.
- **No top-level Keypad check box survives beside the submenu**, and that was the open question in #71 rather than
  an oversight: show/hide costs a hover it did not cost before. Robert chose the single submenu on 2026-09-12 over
  keeping the toggle in View with a separate dock submenu. Two check boxes for one setting in the same menu is the
  worse trade — Preferences already keeps a second door open for it — and the keypad is a thing you leave on rather
  than flick. If the extra step ever grates, the answer is a `Gesture`-free reachability fix, not a duplicate item.
- In tests, drive native items through `((INativeMenuItemExporterEventsImplBridge)item).RaiseClicked()`, the one
  entry point both real renderers use. Assigning `IsChecked` instead only proves a binding round-trips.
- `MenuLookup` (`Menus/`) is how code-behind and tests find a `NativeMenuItem`, which `FindControl` cannot reach.
  `NativeMenuItemSeparator` derives from `NativeMenuItem`, so an `OfType` walk sees the dividers too. `x:Name` does
  not compile on a `NativeMenuItem` (AVLN2000: it is not a `StyledElement`), which is why lookups are by header
  string.
- Code-behind uses `MenuLookup.Required`, not `Item`: it returns null when the *menu* is absent (the deliberate state
  under classic) and throws when a present menu lacks the item. A header renamed in both menus at once keeps the
  parity guard green, so a silent null would leave About duplicated on macOS or the Edit key equivalents quietly
  gone.
- Hiding an item at the end of a menu means hiding its separator too (`MenuLookup.SeparatorAbove`): nothing
  collapses a trailing divider, and the exporter honours `IsVisible` on a separator because `NativeMenuItemSeparator`
  derives from `NativeMenuItem`. Only the exported menu hides anything this way now; the classic `AboutSeparator` and
  `PreferencesSeparator` stay named so the tests can assert they are shown (#103).

## Find

- `FindViewModel` (`ViewModels/`) is separate from `SessionViewModel`, which already owns the session, connection
  lifecycle, certificate prompt, clipboard, wire log and transfer factory. Find has its own lifetime (it opens, holds
  a term and a match list, and closes) and names no Avalonia type, so every rule on it is a plain `[Fact]`. The scan
  itself is `ScreenSearch.Find` in Core.
- `OnScreen` re-runs the search against every new snapshot rather than clearing the matches the way `Selection`
  clears: a 3270 screen repaints on every keystroke echo, and clearing would make the highlight vanish and read as
  broken. It re-anchors the current match by position (the match now starting where the old one did, else the first,
  else none), so a repaint that leaves your place alone does not move you.
- `_visited` tracks whether the cursor has actually been moved to the highlighted match, so typing highlights match
  1 without a `MoveCursor` per keystroke, and the first Enter lands on it rather than skipping past it. A reset to the
  first match — from opening, from a changed term, or from a re-anchor that fell back — always counts as unvisited;
  miss one of those and the skip-a-match bug comes back.
- The find bar is a docked `Border`, not a modal dialog, so the screen being searched stays visible behind it. Its
  `TextBox` (`FindBox`) binds `Find.Term` two-way, and its own `OnFindBoxKeyDown` routes Enter to `NextAsync`,
  Shift+Enter to `PreviousAsync`, and Escape to closing the bar and refocusing the screen. **Typing in the box must
  never reach `Keymap` or the host** — the highest-consequence invariant of the feature.

## The session picker's tags

- `ProfileRow` (`ViewModels/`) is what the picker's `ListBox` binds, not `SessionProfile`: a chip needs a
  colour, and a profile carries only tag *names* — `TagRegistry` owns the colours. Resolving that inside a
  `DataTemplate` would mean a multi-binding against the registry or static mutable state. `Profiles` still holds
  the store's own `SessionProfile` instances and is still what `QuickConnect` resolves against, because its
  `ReferenceEquals` check decides whether an accepted certificate pin may be written back to a file; `VisibleRows`
  is the filtered projection and nothing else reads it.
- **Clearing an `ObservableCollection` bound to a `SelectingItemsControl`'s `SelectedItem` nulls the source
  property, whatever its declared type says.** `RebuildScopes` clears `Scopes`, the bound `ComboBox` nulls its own
  selection, and the two-way binding writes that `null` straight into `SelectedScope` — which then fires
  `OnSelectedScopeChanged` and runs the whole filter against a null scope. It cost two separate crashes: one
  unprotected read in `RebuildScopes`, and one in `InScope` that Avalonia's own `TwoWay` exception handling
  swallowed into a transient `DataValidationErrors` state on the control, so every test stayed green while an
  exception was thrown on every window activation. Read what you need *before* the `Clear`, and guard a reader
  with a property pattern (`is not { TagName: { } tag }`) rather than `?.`, which warns under `-warnaserror` on a
  field the generator declares non-nullable.
- Reconciliation — registering a tag name the registry does not know — lives in `ProfilePickerViewModel.Reload`,
  never in `ProfileStore.LoadAll`, because a load must not write. It is what gives a profile copied from another
  machine local colours, and it saves `tags.json` only when `TagRegistry.Register` reports something changed:
  `Reload` runs on every window activation, so an unconditional save would rewrite the file constantly. It also
  re-reads `tags.json` every time, because Manage Tags (#88) writes the file while the picker waits behind the
  dialog: a registry kept from construction would recolour renamed tags and write deleted ones back.
- **A click that activates the picker reloads it, and a reload that finds a change recycles the row containers.**
  `Reload` returns early when `LoadAll` equals `Profiles` — value equality all the way down (`SessionProfile`,
  `TagSet`, `CertificatePin`) — and `tags.json` equals the registry it holds (a recolour changes no profile, so
  the registry has to be part of the test), so the usual activation click leaves every `ProfileRow` and `ListBoxItem` in place
  and a right-click that brings the picker forward opens its menu on the row it aimed at. When the store DID
  change while the picker was inactive, the activation reload removes every container synchronously and the
  layout that re-realises them is deferred, so on a platform where activation precedes the press (Windows, in
  `WndProc` order) that first right-click opens no menu, and a list whose order changed can put a different row
  under the pointer. That is the residual of #89; a second right-click works.
- The row menu (#89) is a `ContextMenu` set on `ListBoxItem` by a style — a `<Template>` setter, so each container
  builds its own — whose entries bind the picker's commands through `$parent[ListBox]` with the row as
  `CommandParameter`, so an entry acts on the row the menu belongs to whether a right click (which selects on the
  press) or the keyboard opened it. `Connect` and `Edit` take an optional row and fall back to the selection for
  the buttons and the double-tap; greying comes from `CanExecute`, so a test asserts `IsEffectivelyEnabled`. On
  macOS the window turns a Control-click into the `ContextRequested` a right button raises
  (`OnListPointerReleased`): Avalonia.Native delivers it as a plain left press, and Apple keyboards have no Menu
  key, the only gesture in `PlatformHotkeyConfiguration.OpenContextMenu`. The headless recipe for driving the
  menu is in `tests/CLAUDE.md`.
- **Manage Tags** (`Views/ManageTagsWindow`, #88) is modal over the picker. Its swatches are `Button`s, and a
  window style on `ContentPresenter#PART_ContentPresenter` keeps each one's own colour on `:pointerover` and
  `:pressed`, which Fluent's `Button` theme would otherwise swap for a grey. **That setter must read the colour
  through `$parent[Button].Background`**: a `/template/` style outside a `ControlTemplate` may use neither
  `TemplateBinding` nor a `TemplatedParent` source, and a plain `{Binding Brush}` resolves against the presenter's
  DataContext, which is its `Content` — null for a swatch. The first version did exactly that, and the 2026-09-13
  in-app pass found two symptoms of one cause: a hovered swatch drew as a hole, and a pointer click never
  recoloured, because `Button` hit-tests the release and a presenter with a null background is nothing to hit.
  Keyboard Enter still worked, and so did a test that raised `ClickEvent` directly, which is why
  `ManageTagsWindowTests` now presses and releases the mouse at the swatch's centre. Done is `IsCancel` but not
  `IsDefault`, so Enter in the name box renames instead of closing the window. The failure line sits under the
  panel, not in it, so it survives a change of selection.

## Quick Connect's recent hosts

- The picker has two zones: the saved sessions (heading, filter, list, button column) and a Quick Connect footer
  under a divider. The outer layout is a `Grid`, not a `DockPanel`, because document order is tab order and the
  filter must come first; the filter row is a `Grid` for the same reason.
- `QuickConnectBox` is an editable `ComboBox` over `ProfilePickerViewModel.RecentEntries`. The history is
  `RecentHosts` (Core; newest first, unique ignoring case, at most 10, kept as typed) in `recent-hosts.json` through
  `RecentHostsStore`, a history rather than a preference and so not a key in `settings.json`. Only an **ad hoc**
  connect records (`fromStore` false); a saved profile's name never does, and neither does text that opened nothing.
  The file is loaded once, in the constructor rather than `Reload`, since the picker is its only writer, and
  `RecentEntries` changes by single inserts and removes rather than a rebuild, because a Reset makes a bound
  ComboBox drop its selection. Even a single remove empties the box when it takes the selected item, and **typing a
  remembered host selects it** (the editable ComboBox matches its text against the items), so every change goes
  through `KeepingText`, which puts `QuickConnectText` back.
- **Its keys are handled on the tunnel** (`AddHandler(..., RoutingStrategies.Tunnel)`), because the ComboBox marks
  keys handled itself. Enter always connects what is in the box and closes the list: highlighting an entry already
  put its text there, and in Avalonia 12.1.2 the editable ComboBox never closes its own list on Enter. Delete forgets
  the highlighted entry only while the list is open: the focused `ComboBoxItem`'s entry, else the selection an
  unmodified Up or Down last moved to (`_arrowedTo`, read by a bubbling `handledEventsToo` handler and cleared by
  any other key or the list closing). Never `SelectedItem` alone, which typed text sets too. With the list closed,
  Delete is text editing.
- Each entry's **×** (`ForgetButton`) is a non-focusable `Button` binding `RemoveRecentHostCommand` through
  `$parent[ComboBox]`. The `Button` handles the press, so a press on it neither picks the entry nor closes the list;
  `ProfilePickerWindowTests` proves that with a real pointer, since a raised `ClickEvent` never runs a bound
  `Command`. Emptying the list closes the drop-down.

## The profile inside the session (#93)

- `SessionViewModel` carries the profile as the window shows it — `HostPort`, `Note` (trimmed or null),
  `Chips` — built once in the constructor from the profile and the `TagRegistry` the App passes (`tags:`; null
  draws every chip grey). Both are snapshots at open, like the profile itself: a Manage Tags recolour reaches
  the next window, not this one. `TagChip.For` holds the chip rules (FAVORITE never a chip, uppercase, the
  registry's colour) for `ProfileRow` and the session alike, and `App.axaml`'s `TagChipTemplate` and
  `TagChipStrip` resources hold the look, so the picker, the status bar and the banner cannot drift.
- **The connect banner** (`NoteBanner`) shows on the *edge* into a connected state (`_wasConnected`), never on
  the level: b3270 reports Connected3270 and then ConnectedTn3270E for one arrival, and the second must not undo
  the keystroke that dismissed it. It shows itself only when the profile has a note or a chip — name and host
  alone are the title — and `ShowBanner()` (the status bar's `NoteIcon`, a Click handler so a raised
  `ClickEvent` reaches it) shows it for any profile. `SendKeyAsync`, `TypeTextAsync` and `MoveCursorAsync`
  clear it, which is every path a keystroke, typed text, a keypad button or a screen click takes to the host.
  No Dismiss button, and no timer.
- The chips in the status bar (`StatusChips`) are the one part behind `Settings.ShowTagsInStatusBar`, default
  off. They lead the bar with the icon and are the first thing clipped when the window is narrow; the state
  text keeps its place.

## Screen capture

- Two formats over one `ScreenSnapshot`: plain text via `ScreenSnapshot.ToText()` (Core), and `ScreenHtml.Render`
  (`Capture/`). HTML lives in App because it renders in `Palette`'s colours, and `Palette` is Avalonia-typed. It
  deliberately does not use b3270's `PrintText(html)`: that would add a member to `IEmulatorSession` for a feature
  that needs no engine, would only work while connected, and would emit the engine's colours rather than the ones
  the user is looking at.
- `Render` emits one `<pre>` of `<span>` runs segmented by `Cell.SameStyleAs`, the renderer's own predicate.
  `RenderDocument` wraps the same fragment with `<meta charset="utf-8">`, for files only: a saved `.html` has nothing
  else to declare its encoding, and the keymap types non-ASCII characters (`¬`, `¢`) that would come back as
  mojibake. Copy Screen as HTML always uses the bare `Render` fragment, since it is pasted into a document that has
  its own encoding.
- `CanCaptureScreen` (a screen exists) gates both capture items, deliberately not `IsConnected`: the moment a capture
  is most wanted is often just after the host dropped the session.
- `SaveScreenAsync` (File > Save Screen As...) picks the format from the extension the Save dialog returned,
  case-insensitively (`.html`/`.htm` → HTML via `RenderDocument`, anything else → text), and writes the bytes
  itself, because the dialog has just made a promise about overwriting that only the caller can keep.
- `IFilePicker.PickSaveLocationAsync` takes a `title`, since its two callers (a received transfer and a screen
  capture) save different things, and an optional `IReadOnlyList<SaveFormat>` that fills the dialog's own File Format
  popup. The extension still decides the format; the popup makes that decision visible and gets the extension
  appended. `SaveFormat` (`Files/`) is ours rather than Avalonia's `FilePickerFileType`, so `IFilePicker` stays
  Avalonia-free and `FakeFilePicker` stays trivial. `AvaloniaFilePicker` leaves `FileTypeChoices` unset when the list
  is null or empty, since the platforms disagree about what an empty one means, and "no popup" is what a received
  file (which can be anything) wants. The capture's first format must match `ScreenFileName`'s extension: the dialog
  opens on the first entry, and a popup contradicting the filename beside it is worse than none.

## File transfer

- File > IND$FILE Transfer... (enabled while connected) opens `FileTransferWindow` modally, with a
  `FileTransferViewModel` from `SessionViewModel.CreateTransfer(IFilePicker)`, pre-filled from `LastTransferRequest`
  (the last request started from that window; nothing goes to the profile).
- One window, three phases: Form, Running, Done. Start validates through `TryBuildRequest`, then refuses a receive
  into an existing local file unless Append is on or the path is the one the Save dialog last returned (that dialog
  asked about overwriting; a typed or remembered path never did). Progress is marshalled through `dispatch`; Cancel
  cancels the token.
- The window's `Closing` defers to `FileTransferViewModel.TryClose`. The first close of a running transfer cancels
  it and keeps the window, so the outcome shows; a second close while the engine still has not answered lets the
  window go, because b3270 only aborts a running transfer on the host's next turn, and a stalled host must not pin the
  dialog, the session window, and Quit behind it.
- Escape closes the dialog in every phase through the window's `OnKeyDown`, so it goes through `Closing` and
  `TryClose` like the Close button. The Running panel has no Close button, which is why none is an `IsCancel` button.
- Avalonia propagates an owned dialog's `Closing` cancel to its owner, so closing the session window, or quitting,
  while a transfer runs is refused the same way. A forced shutdown's `DisposeAsync` sends Quit, and the pending run
  faults into the dialog's catch.
- `LocalFileNames` suggests the save name (member or last qualifier; VM `FN.FT`); `TransferLabels` labels the combo
  boxes.

## Everything else

- `ProfileStore` keeps one JSON file per profile under `AppPaths`' profiles directory and silently skips unreadable
  files.
- The IBM 3270 font is embedded (`avares://LizTerm.App/Assets/Fonts#IBM 3270`) and used for the status bar too, so it
  reads as one instrument. The bar is x3270's Operator Information Area: `StatusFormatter` builds its fields from
  `OiaGlyphs`, the font's own OIA symbols at the private-use block `tools/patch-3270-oia-font.py` gives them
  (`docs/development.md`, "The 3270 font"); the padlock is the font's Powerline one, U+E0A2. The words go on
  tooltips. Assertions on status strings are exact, so change `StatusFormatter` and its tests together.
- `Assets/Icons/` holds the app icon in the three shapes packaging needs (`lizterm.icns`, `lizterm.ico`,
  `lizterm.png`, referenced from `LizTerm.parcel`). The csproj's `<AvaloniaResource Include="Assets\**" />` already
  covers a new or replaced one.
- `AppLicense` is the one spelling of LizTerm's licence. `Notice` is the one-line credit on the splash and in About,
  deliberately ASCII (`Copyright 2026 by CoffeeMuse - BSD-3-Clause`) because the splash renders it in the 3270 font,
  whose coverage is not general. `All` is `LICENSE` followed by `THIRD-PARTY-NOTICES.txt` — both `AvaloniaResource`
  entries pointing up at the repository root — shown in About under the heading **Licenses**. Embedding `LICENSE` is
  what satisfies BSD-3-Clause clause 2 for a binary distribution: no archive or installer the release produces
  carries a licence file beside the binary, so About is the only place LizTerm's terms reach a user. `All` is read on
  first use rather than in a static initializer, so touching `Notice` during startup never pulls in the asset loader.
  The M2 polish spec (§6.3) predates the licence decision and says About shows third-party notices only; that is
  superseded, so do not restore the old `AboutWindowTests` `DoesNotContain` assertion.

## Driving the app with the Avalonia DevTools MCP

`.mcp.json` declares the `avalonia_devtools` server (`avdt mcp`, from the global dotnet tool
`AvaloniaUI.DeveloperTools`). Debug builds reference `AvaloniaUI.DiagnosticsSupport` and call `WithDeveloperTools()`
in `Program.BuildAvaloniaApp`; Release builds carry none of it, and the headless test builder never calls it.

- Every tool call is refused until `AVALONIA_TOOLS_LICENSE_KEY` is in the MCP server's environment. Put it in
  `.claude/settings.local.json` under `env` (gitignored; confirm a new worktree has its own copy), never in
  `.mcp.json`: settings `env` is inherited by MCP child processes, but `${VAR}` placeholders in `.mcp.json` expand
  only from Claude Code's startup environment (the desktop app does not source `~/.zshrc`), so an `env` entry for the
  key there overrides the inherited value with an empty string. Stdio MCP servers are never restarted mid-session,
  so a new key takes effect in the next session.
- To start the app: `dotnet build src/LizTerm.App`, then
  `LIZTERM_B3270_PATH=/opt/homebrew/bin/b3270 nohup dotnet run --project src/LizTerm.App --no-build &` (a fresh
  worktree has no `native/out`, so the override is required). Call `attach-to-app` with no arguments to list apps,
  then again with `id` set to the pid. The splash is a root for up to 2.5 s; wait for it to close before `tree`.
- `tree` with no node returns the window roots. A dialog opened by `input` Click appears there as a new root, but
  `search` does not find windows opened after its first query, so re-list the roots instead. Menu popups never appear
  as roots, but an item can still be reached: `input` Click on the top-level menu header, then Click on the item.
- **On macOS the menu you drive is `ClassicMenu`, and the reliable recipe is to launch with `LIZTERM_MENU=classic`
  so the bar is really shown.** It is populated either way, and an earlier measurement (2026-09-12, View >
  Crosshair > None) reported a hidden bar driving exactly like a shown one. That did not reproduce later the same
  day while building #71: under the default native strategy, with the bar hidden, Click on the View header, on a
  submenu header and on a leaf all answered `handled:false` and moved nothing — including the same Crosshair path,
  run as a control. Relaunching the identical build under `LIZTERM_MENU=classic` answered `handled:true` at every
  step and the settings changed. Both runs were `nohup`-launched and neither app was frontmost, so focus is not the
  difference; hit-testing a hidden control is the likely one. Until someone reconciles the two, launch classic.
- **A submenu materialises its children only when its popup opens.** Under `LIZTERM_MENU=classic`, `tree` on
  View > Keypad returns nothing until you Click the View header and then the Keypad header; re-read the tree after
  that and the leaves are there, with new node IDs. So the sequence for a nested item is header, header, re-read,
  leaf — not a single Click on an ID captured earlier.
- **`NativeMenuBar` is never the way in.** Its items are generated inside the control's template, so `search`
  returns the bar alone and `tree` on the bar returns `[]` — true even when it is rendering, which on macOS takes
  forcing `NativeMenuBarPresenter.IsVisible` (#70). For the same reason the application menu's About and
  Preferences are out of reach on macOS. Their in-window copies are ordinary `MenuItem`s in every style (#103):
  under `LIZTERM_MENU=classic`, clicking the Edit header and then `PreferencesMenuItem` opened `PreferencesWindow`
  (checked 2026-09-14).
- To reach the picker and the profile editor, launch a second instance with no profile argument.
- `props` returns `bindingExpression` beside each value, the quickest check that a control reached the view model.
  `IsEnabled` on a command-bound button reads `True` even while the tree shows `:disabled`, so check
  `IsEffectivelyEnabled`.
- The app writes real profiles to the per-OS config directory, so Cancel any editor dialog you drove rather than
  Save, and kill the `dotnet run` pid when done. To connect to a real host without touching real profiles, seed a
  profile JSON (camelCase fields) under `<scratch>/Library/Application Support/LizTerm/profiles/` and launch the
  built apphost `src/LizTerm.App/bin/Debug/net10.0/LizTerm.App <profile-name>` with `HOME=<scratch>` — the apphost
  rather than `dotnet run`, so the HOME override does not disturb the dotnet CLI.
