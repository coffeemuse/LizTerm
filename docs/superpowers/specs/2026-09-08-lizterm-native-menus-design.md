# LizTerm: Native Menus and the macOS Application Menu

Date: 2026-09-08
Status: approved in discussion 2026-09-08; not yet implemented
Parent: `2026-09-03-lizterm-v1-design.md` sections 6.1, 6.2 and 6.8; issue #14
Supersedes: `2026-09-05-lizterm-m2-polish-design.md` section 11's deferral of "an About item in the picker or
the macOS application menu"

## 1. Purpose

LizTerm draws its menu bar inside the window on every platform. On macOS that is wrong twice over: the menu
belongs in the system menu bar, and the application menu — About, Quit — is a place the app does not use at
all. With only the profile picker open, which is the app's startup state and the state it returns to when the
last session window closes, macOS shows no menu bar worth the name.

This plan moves the menu definition to Avalonia's `NativeMenu`, adds the application menu, and names the
`Application` so the app menu and system dialogs say `LizTerm`. It changes no behaviour that reaches the host:
the 3270 input path is what this plan is most careful *not* to disturb, for the reason section 2 measured.

It touches `LizTerm.App` only. Core and the b3270 backend are untouched.

## 2. What the two spikes measured

Both were run on 2026-09-08 against the real app on macOS 15 (arm64, Avalonia 12.1.2) and reverted. Their
findings are the reason this design has the shape it does, so they are recorded before the design rather than
as an appendix.

### 2.1 A native menu gesture takes the keystroke before the screen exists

A throwaway `NativeMenu` on `SessionWindow` carried two items, `Gesture="Cmd+C"` and `Gesture="F1"`. Each of
the window's own paths logged when it fired:

```
08:15:04.515  SCREEN key PF1      ← F1 injected through Avalonia (AppKit bypassed) — the control
08:17:04.812  MENU F1 clicked     ← physical F1
08:17:17.425  MENU Cmd+C clicked  ← physical Cmd+C
08:17:22.365  SCREEN key PF2      ← physical F2, which no menu item claims
```

A physical press of a claimed key fired the menu item and **`TerminalScreen` never saw it** — no `SCREEN` line
accompanies either `MENU` line. The same F1 injected at the Avalonia level, where AppKit is out of the loop,
reached the screen and mapped to PF1 as usual; and F2, claimed by nothing, went straight through. So it is the
claim that swallows the key, not a general breakage.

This is not a race in Avalonia's dispatch order that could be reordered. `libAvaloniaNative.dylib` exports
`setKeyEquivalent:`, `setKeyEquivalentModifierMask:` and `menuHasKeyEquivalent:forEvent:target:action:`: a
`NativeMenuItem` gesture is a real AppKit key equivalent, and `NSApplication.sendEvent:` offers a key-down
event to the main menu before the key window's responder chain runs. `TerminalScreen` cannot win a contest it
is not invited to.

**Consequence, and the hardest rule in this plan: no gesture on any Keys menu item, ever.** `Gesture="F1"` on
a PF1 item would produce a 3270 client that cannot send PF1 to the host, silently, with nothing in the wire
log. The same reasoning bars gestures from File and Help.

### 2.2 NativeMenuItem bindings resolve under the headless platform

The headless platform exports no native menu, so whether the App test project could reach menu items at all
was an open question — and `NativeMenuItem` is not in the visual tree, so the existing `FindControl<MenuItem>`
approach cannot reach it. Three throwaway `[AvaloniaFact]`s answered it:

```
Spike_the_menu_is_reachable_at_all                          ✓  NativeMenu.GetMenu(window) → File, Help
Spike_isenabled_binding_resolves                            ✓  IsEnabled tracks IsConnected
Spike_twoway_ischecked_binding_resolves_in_both_directions  ✓  IsChecked ↔ IsWireLogging, both ways
```

The `NativeMenu` object tree is a live binding target under headless, including two-way write-back. That is
what the wire-log toggle depends on, and it is why section 6 can keep binding-level coverage rather than
retreating to view-model assertions.

## 3. Approach: two renderers behind one strategy

Avalonia offers no way to render a `NativeMenu` as a classic in-window `Menu`; `NativeMenuBar` is the only
bridge, and its in-window rendering is far less travelled than `Menu`'s. There is no Windows or Linux GUI
available to this project to look at, and CI has no GUI at all — so shipping `NativeMenuBar` as the default on
those two platforms would mean replacing a menu that works with one nobody has seen.

So `SessionWindow` carries **both renderers over one native definition**, and a `MenuStrategy` picks between
them per platform, with an environment override. The default is native on macOS — where section 2.1 watched it
work — and classic on Windows and Linux. Nothing unproven is the default anywhere.

This is explicitly a staging device, not an end state. Section 8 records what has to be true before the
classic menu is deleted.

Two alternatives were considered and rejected. A permanent per-platform split (macOS native, others classic
forever) means two menu definitions maintained indefinitely, and every future menu item silently missing from
some platforms if it is added to only one. A single menu model built in C# and projected into both renderers
has no drift at all and would make the menu a properly testable unit, but it moves the menu out of XAML,
against the style used everywhere else in this app, and it is the largest of the three changes.

## 4. LizTerm.App changes

### 4.1 MenuStrategy

New, `Menus/MenuStrategy.cs`:

```csharp
internal static class MenuStrategy
{
    public const string Variable = "LIZTERM_MENU";

    public static bool UseNativeMenu =>
        Decide(Environment.GetEnvironmentVariable(Variable), OperatingSystem.IsMacOS());

    /// <summary>The whole decision in one pure function, so it can be tested without the environment
    /// or an operating system, the way EngineRequirement.Decide is.</summary>
    public static bool Decide(string? variable, bool isMacOS) => variable?.Trim().ToLowerInvariant() switch
    {
        "native" => true,
        "classic" => false,
        _ => isMacOS,
    };
}
```

An unrecognised or blank value falls back to the platform default rather than failing: this is a development
escape hatch, and a typo that stops the app from drawing a menu bar would be a worse outcome than one that
quietly draws the usual one. `Decide` is a pure function taking both inputs for the same reason
`EngineRequirement.Decide(found, present, variable)` is: the gate is unit tested on its own, on any machine,
for every combination, rather than only for whichever one the test happens to run on.

The variable joins the documented set in `README.md` and `CLAUDE.md`.

### 4.2 App.axaml: the name and the application menu

`Name="LizTerm"` on the `Application`, and an application-level `NativeMenu`:

```xml
<NativeMenu.Menu>
  <NativeMenu>
    <NativeMenuItem Header="About LizTerm" Click="OnAboutClick" />
    <NativeMenuItem Header="Quit LizTerm" Gesture="Cmd+Q" Click="OnQuitClick" />
  </NativeMenu>
</NativeMenu.Menu>
```

This hangs off the application rather than a window, which is what gives the picker a menu bar on macOS and
what closes the issue's complaint about the app's startup state.

`Cmd+Q` is the one gesture outside the Edit menu in this whole plan, and it is deliberate. The application
menu is macOS-only by construction; `Keymap` claims no Meta chord at all (its table is Escape, Pause, function
keys, Alt and Ctrl chords, and the modifier taps), so nothing about `Cmd+Q` can reach the swallowing failure of
section 2.1. Omitting it would be the riskier choice, since a replaced application menu may not inherit
AppKit's own Quit item.

`OnQuitClick` calls the existing public `App.Quit()` (`App.axaml.cs:144`), which is already the one spelling of
shutdown under `ShutdownMode.OnExplicitShutdown`.

### 4.3 One spelling of About

About is currently reachable only from a session window's Help menu, where `SessionWindow.OnAboutClick` builds
`AboutWindow(AppVersion.Current, vm.Engine, SessionFactory.OverrideOrigin)`. The application menu adds a second
caller that may have no session at all, so both go through one new method on `App`:

```csharp
public async Task ShowAboutAsync(Window? preferredOwner)
```

- **Owner.** `preferredOwner` when the caller has one — `SessionWindow` passes `this`, so a session's own Help
  item stays modal to that session even if another window is active. The application menu passes `null`, and
  the owner is then the active window. If no window is active at all, About is shown ownerless rather than not
  at all.
- **Engine.** If the resolved owner is a `SessionWindow`, its view model's `Engine` — so About describes the
  session being looked at, version and all. Otherwise `SessionFactory.CheckBackend()`, which locates the binary
  without starting it and therefore reports no version. `CheckBackend` *throws*
  `BackendUnavailableException` when the engine is missing or not executable, so that is caught and turned into
  `EngineInfo` with `EngineSource.Unknown`. No new user-facing wording is invented for any of this:
  `StatusFormatter.Engine` already renders `Unknown` as "b3270, not found" and a null version as
  "b3270, not started" (`StatusFormatter.cs:88`), which are exactly the two session-less cases.
- **Failure.** A session window keeps today's behaviour of reporting a failed open through its error banner.
  From the application menu there is no banner to use, so a failure there is swallowed rather than crashing the
  app — About is not worth taking a process down for.

`SessionWindow.OnAboutClick` becomes a call to this method, keeping its existing `Screen.Focus()` afterwards.

### 4.4 SessionWindow: two renderers, one definition

`SessionWindow.axaml` keeps today's `<Menu DockPanel.Dock="Top">` **unchanged**, named `ClassicMenu`, and gains
beside it a `<NativeMenuBar DockPanel.Dock="Top" x:Name="NativeBar" />` plus a window-level `NativeMenu.Menu`
holding File / Edit / Keys / Help — the same items, headers and bindings the classic menu has.

The code-behind sets `IsVisible` on the two renderers from `MenuStrategy.UseNativeMenu` in the constructor.
Hiding `ClassicMenu` under the native strategy is required, not tidiness: the first spike installed a
`NativeMenu` while the classic `<Menu>` was still present and macOS drew **both**, an in-window bar beneath a
system bar.

`NativeMenuBar` hides itself on macOS, so under the native strategy on macOS both in-window renderers are
invisible and the definition reaches the system menu bar; on Windows and Linux under the same strategy
`NativeMenuBar` draws it in-window. Under the classic strategy the window-level `NativeMenu` still exists but
nothing renders it, which is harmless — and section 6's once-only test is what proves it is also inert.

### 4.5 Gestures

Only the Edit menu, and only Copy, Paste and Select All. They are assigned in code from
`GetPlatformSettings().HotkeyConfiguration`, exactly where `ShowPlatformGestures()`
(`SessionWindow.axaml.cs:29`) already reads them to set `InputGesture` on the classic items today, so macOS
shows ⌘ and the other platforms Ctrl from one table rather than a hardcoded modifier.

They activate `Click` handlers calling `ViewModel.CopyAsync()`, `PasteAsync()` and `SelectAll()` **directly**,
never the `[RelayCommand]`s. This matters and is easy to get backwards. The screen's own events already call
those methods rather than the commands, deliberately: each method carries its own guard, and a keystroke must
never be dropped for arriving while the previous one's round trip is still open. The `[RelayCommand]`s keep
CommunityToolkit's default of disabling an async command while it runs, which is safe for a menu *click* — a
slow human action — but not for a keystroke. Section 2.1 showed that on macOS a gestured Edit item is
activated by the keyboard, so binding those gestures to the commands would quietly reintroduce dropped
copies and pastes on exactly the path the screen was written to protect.

Nothing on File, Keys or Help carries a gesture. Under the classic strategy `ShowPlatformGestures()` continues
unchanged, so neither renderer loses its shortcut hints.

### 4.6 Where About appears

About must appear exactly once per platform, in the place that platform expects it: the application menu on
macOS, Help on Windows and Linux. Quit exists only in the application menu.

`MenuStrategy` gains a second pure function beside `Decide`:

```csharp
/// <summary>macOS puts About in the application menu, so the Help item must not also carry one.</summary>
public static bool AboutInHelpMenu(bool isMacOS) => !isMacOS;
```

`SessionWindow`'s code-behind sets `IsVisible` on the Help ▸ About item from
`MenuStrategy.AboutInHelpMenu(OperatingSystem.IsMacOS())`, in the same constructor step that sets the two
renderers' visibility — one place where the platform is consulted, rather than a static reached from XAML.

**Both** Help menus get this, the classic one included. It would be tempting to skip the classic menu on the
grounds that macOS uses the native one, but that is false in exactly one reachable configuration:
`LIZTERM_MENU=classic` on macOS, which is a development escape hatch someone will use precisely when native
menus are misbehaving. There the classic menu *does* render in-window on macOS while the application menu
still supplies its own About — the duplicate this section exists to prevent. One shared rule applied to both
renderers costs a line and has no such hole.

Testability is bounded and worth stating plainly. `AboutInHelpMenu` and `Decide` are pure and tested
exhaustively for both platform values on any machine. The *wiring* — that the item's visibility actually
follows the function — is asserted by a window test on whatever platform it runs, which in CI is Linux, and so
covers the visible-in-Help branch only. The macOS branch of the wiring is verified by running the app on the
Mac, which section 8 requires anyway. No mutable platform static is introduced to close that gap: it would
make every menu test order-dependent, and this repository confines that hazard to one non-parallel collection
for environment variables rather than spreading it.

### 4.7 ProfilePickerWindow

Unchanged. On macOS it inherits the application menu, which is the whole of its complaint in the issue. On
Windows and Linux it keeps its button-only layout — giving it a `NativeMenuBar` would hand those two platforms
an in-window menu bar the picker has never had, in order to fix a problem only macOS has.

## 5. Error handling

Nothing here introduces a new failure mode that reaches the host or the engine. Three cases:

- A missing or non-executable engine when About is opened from the application menu: reported as "b3270, not
  found" through the existing `StatusFormatter.Engine` wording, never as an exception.
- `AboutWindow` failing to open from a session window: today's error banner, unchanged. From the application
  menu: swallowed, per 4.3.
- An unrecognised `LIZTERM_MENU` value: the platform default, per 4.1.

## 6. Testing

All in `tests/LizTerm.App.Tests`, on the headless platform, per section 2.2.

- **`MenuStrategyTests`** (plain `[Fact]`): `Decide` for `native`, `classic`, mixed case, surrounding
  whitespace, `null`, empty, and an unrecognised value, each against both `isMacOS` values, plus
  `AboutInHelpMenu` for both. Fifteen or so cases, no environment touched, no OS branch — the point of the
  pure functions.
- **`NativeMenuTests`** (`[AvaloniaFact]`): a `NativeMenuItem Item(Window, string top, string child)` helper
  walking `NativeMenu.GetMenu(window)` by header, then the same claims the classic tests make — the structure
  of the four top-level menus, File Transfer enabled only while connected, and the wire-log item's
  `ToggleType` and its two-way binding to `IsWireLogging` in both directions. Plus the About wiring, which on
  a CI machine covers the visible-in-Help branch only, per 4.6.
- **The five existing `FindControl<MenuItem>` assertions in `SessionWindowTests` stay exactly as they are.**
  While both menus exist, both are covered; when the classic menu is deleted, its tests go with it. This is the
  one place where the staging device costs duplicated test code, and it is the intended cost.
- **`Ctrl+C through the window fires copy exactly once`**, run under **both** strategies. This is the test that
  earns its keep. On macOS section 2.1 proved the OS takes the key before `TerminalScreen`, so there is no
  double-fire; on Windows and Linux the gesture reaches `NativeMenuBar` instead, and whether that dispatches as
  well as displays is the one behaviour this machine cannot check. A double dispatch would paste twice, which
  is a real bug rather than a cosmetic one. It is reachable headlessly, so it becomes a test that runs on every
  CI push instead of a caveat in a document.

The wire-log toggle's existing recorded behaviour — a value corrected inside its own change notification being
invisible to the two-way binding, which is why `SessionViewModel` marshals its correction through `dispatch` —
must survive the move. The two-way assertion in `NativeMenuTests` is what holds that, and section 2.2 confirmed
the binding direction it depends on actually resolves under headless.

## 7. Out of scope

Preferences and a preferences window (issue #19; there is no preferences UI to put in the application menu
yet). The Dock, `Info.plist`, the `.app` bundle and anything else about packaging — that is plan 3e. The
picker gaining a menu bar on Windows and Linux. A user-editable keymap (issue #18). Making `Dup` and
`FieldMark` reachable (issue #16). Deleting the classic menu, which is section 8's follow-up rather than this
plan's work.

## 8. Before the default can flip, and the follow-up

The classic menu exists to be deleted. Two questions have to be answered by looking at a real Windows and a
real Linux machine with `LIZTERM_MENU=native`, neither of which is reachable from this project's CI:

1. **Mnemonics.** Today's headers are `_File`, `_Copy`. Whether `NativeMenuBar` renders the underscore as an
   Alt-mnemonic or prints it literally decides whether they survive; dropping them is a real regression for
   Windows and Linux keyboard users, not a cosmetic one.
2. **Appearance.** Whether `NativeMenuBar`'s in-window rendering is presentable next to `Menu`'s. The tests in
   section 6 cover structure and behaviour; they cannot say it looks right.

A third question is macOS-only and answerable here, on the day the application menu first runs: whether
supplying an application-level `NativeMenu` costs the AppKit-supplied items a Mac user expects beside Quit
(Hide, Hide Others, Services). If it does, they are added explicitly.

A fourth question does not belong on this list any more. Section 6 flagged whether `NativeMenuBar` dispatches
a gesture as well as displaying it as "the one behaviour this machine cannot check," implying it too would
need a real Windows or Linux machine to close. It did not: see section 9, deviation 1. Decompiling Avalonia
itself settled it without any hardware section 6 couldn't reach, and in the safe direction. It is answered,
closed, and is not part of what the follow-up below still needs to verify.

When this plan lands, a follow-up issue records the flip: verify the two questions above, change
`MenuStrategy`'s default, delete `ClassicMenu` and its tests, and retire `LIZTERM_MENU`. Filed at landing
rather than later, so a staging device does not quietly become a permanent duplicate.

## 9. Deviations from this spec (as-built)

Rulings made during code review and execution, recorded here rather than edited into the sections above:

1. **The double-dispatch question section 6 and section 8 both treated as needing a real Windows or Linux
   machine is answered, in the safe direction, without one.** Code review of Task 6 (commit `f227fe3`) flagged
   `The_paste_hotkey_reaches_the_host_exactly_once` as theatre: it asserted a real thing (one `paste:claude`
   call under both `InlineData` strategies) but its docstring claimed to guard something it could not —
   whether `NativeMenuBar` dispatches a gesture as well as displaying it, which section 6 called "the one
   behaviour this machine cannot check." Decompiling the pinned Avalonia 12.1.2 `Avalonia.Controls.dll`
   settled that question directly: `NativeMenuBarPresenter.CreateContainerForNativeItem` — the in-window
   fallback bar Avalonia builds anywhere a real AppKit `NSMenu` isn't exported, which covers headless and also
   a real windowed Windows or Linux GUI alike — binds `NativeMenuItem.Gesture` only to
   `MenuItem.InputGestureProperty`. Avalonia's own XML doc on that property says so outright: "Setting this
   property does not cause the input gesture to be handled by the menu item, it simply displays the gesture
   text." `MenuItem.OnKeyDown` and `MenuBase.OnKeyDown` are both empty method bodies, so no key reaches a menu
   item through them either. The only property that wires a gesture to real dispatch is `MenuItem.HotKey`
   (backed by `HotKeyManager`), which this plan never assigns — Task 6 assigns `Gesture` only. So for both
   `InlineData(true)` and `InlineData(false)`, the only path that can ever produce `paste:claude` is
   `TerminalScreen.TryHandleClipboardKey`; there was never a second dispatch path for the test to catch, on any
   CI configuration this repo runs. This is why section 8 no longer lists dispatch behaviour among what the
   real-hardware follow-up still needs to check.
2. **The once-only test was re-aimed, not deleted, once its claim outran what it could prove.** Its assertion
   was already correct and worth keeping — one `paste:claude` call under a native menu with gestures assigned
   is real coverage against a future change that switches `Gesture` to `MenuItem.HotKey` (which does dispatch)
   and against a regression in `TerminalScreen`'s own clipboard routing. What was false was the docstring's
   framing of it as a guard against AppKit intercepting the keystroke, which deviation 1 shows it never could
   be and never needed to be. It was renamed to
   `The_paste_hotkey_reaches_the_host_exactly_once_via_TerminalScreen_under_headless` and its docstring rewritten
   to state what it guards (regressions in dispatch wiring and in `TerminalScreen`'s routing) and what it does
   not (AppKit's real key-equivalent interception, which needs a live macOS GUI session and is not exercised by
   any test in this repo).
