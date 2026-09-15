# LizTerm: switching between open sessions

Date: 2026-09-15. Issue: #46. Status: approved in discussion on 2026-09-15; awaiting review of this text.

## 1. Purpose

One window is one session is one profile, and nothing in the app lists them. With eight sessions open among
dozens of other applications' windows — Word, Excel, Outlook, Safari, Finder — finding the right LizTerm window
means the Dock, `Cmd+\``, the taskbar or luck. #46 proposed a Window menu. Discussion on 2026-09-15 widened that
into three needs: **reach** (get back to LizTerm's sessions from anywhere), **identify** (tell eight sessions apart)
and, for some sessions, **keep in view**.

The last one comes from how the sessions are used. Robert described two kinds: **watch sessions** — an operator
console, IMON — that someone keeps an eye on while working elsewhere, and **work sessions**, the interactive ones
flipped between. A watch session must stay *visible*; no alert stands in for seeing the screen.

Decisions taken in discussion on 2026-09-15, each justified in the section named:

- **Separate windows stay; there are no tabs in this change** (§2). Watch sessions need windows of their own, and
  tabs would rework every menu binding. **Tabs may come later as a preference**, so the record of open sessions is
  keyed on sessions, not windows, and a future tabbed window plugs in behind one interface (§3).
- **One chord, Cmd+K on macOS and Ctrl+K on Windows and Linux, opens a session switcher** (§5, §6), handled the
  way Find's Cmd/Ctrl+F already is.
- **The switcher is a floating palette at the top centre of the session window, over a dimmed screen** (§5.1),
  not a bar docked where Find opens: a docked list covers the bottom rows, where hosts put command lines and
  messages.
- **Sessions are numbered 1-9 and 0 in the order they were opened; a digit jumps only while the filter is empty**
  (§5.2). Numbers that followed recent use would change meaning on every switch; profile names like `TK4` and
  `VM370` contain digits, so once a letter is typed, digits filter.
- **The switcher opens on the session you were last in**, so Cmd+K, Enter flips between two (§5.2).
- **A switcher row is the Sessions list's row** — FAVORITE star, name with chips, host, the note as an italic third
  line — plus a number, a connection mark and a flag (§5.3). **The filter matches name, host, tags and note**
  (§5.4).
- **A Window menu** (§7): Minimize and Zoom (macOS only), Keep on Top, Switch Session..., Bring All to Front, and
  the numbered sessions with a check mark on the one last in use.
- **A Dock menu on macOS** listing the same sessions and New Session... (§8), so one click from inside another
  application lands on the right session.
- **Keep on Top, per window**, is how a watch session stays in view (§7.3).

## 2. Scope

In: the session record (§3), menu labels (§4), the switcher (§5), its chord (§6), the Window menu (§7), the macOS
Dock menu (§8), and the App wiring (§9).

Out, each by decision on 2026-09-15:

- **Tabs.** Possible later, as a preference; §3's `ISessionHost` is the seam.
- **Remembered placement.** No per-profile window position, no reopening the sessions that were open at quit, no
  named layouts. Robert chose "nothing yet": arranging by hand is fine for now.
- **Attention signals.** No bell or disconnect badges, no activity marker, no watch-for-text. Keep on Top is the
  whole answer for watch sessions.
- **Further shortcuts.** No next/previous session chords (macOS already has `Cmd+\``, and the preselected previous
  session covers flipping between two), no global Cmd+1-9 (digits live inside the switcher), and no system-wide
  hotkey, which is intrusive and different on every platform. Every chord taken is a key the terminal loses.
- **Live thumbnails** of sessions, and **Vista-style per-session colour borders**.
- **Native macOS window tabbing.** Avalonia 12.1.2 forces it off (`WindowImpl.mm` sets
  `NSWindowTabbingModeDisallowed`) and exposes no setting.

What does not change: `LizTerm.Core`, the backend, `SessionFactory`, and the one-window-one-session-one-profile rule.

## 3. `SessionList`: the one record of open sessions

`src/LizTerm.App/Sessions/`. `App` owns one instance for the process, and it replaces `App._sessions` and
`App._lastActiveSession`.

```csharp
namespace LizTerm.App.Sessions;

/// <summary>Whatever shows a session: today a SessionWindow, later possibly a tab in a tabbed window.</summary>
public interface ISessionHost
{
    /// <summary>Restore if minimised, then activate — a tab host would select the tab first.</summary>
    void Bring();
    bool IsMinimized { get; }
    bool KeepOnTop { get; }
    event EventHandler? KeepOnTopChanged;
}

public sealed record SessionEntry(SessionViewModel Session, ProfileRow Summary, bool IsSaved, ISessionHost Host);

public sealed class SessionList
{
    public IReadOnlyList<SessionEntry> Entries { get; }   // opening order
    public int Count { get; }
    public SessionEntry? Current { get; }                 // most recently activated
    public SessionEntry? Previous { get; }                // the one before it
    public void Add(SessionEntry entry);
    public void Remove(SessionEntry entry);
    public void Activated(SessionEntry entry);
    public int? PositionOf(SessionEntry entry);           // 1-10, null past ten
    public void BringAllToFront();
    public event EventHandler? Changed;
}
```

- **Two orders.** `Entries` is opening order and gives the numbers; closing a session moves every later one up.
  A second, private list is use order: `Add` puts an entry at its end (a session that has never been activated is
  the least recent), `Activated` moves an entry to its front, `Remove` takes it out of both. `Current` and
  `Previous` are the first two of use order.
- **`Changed`** is raised after `Add`, `Remove`, an `Activated` that changes `Current`, a `PropertyChanged` for
  `Connection` on any entry's view model, and any host's `KeepOnTopChanged`. `Remove` detaches the entry's two
  subscriptions. Everything runs on the UI thread: `SessionViewModel` already marshals its backend events there,
  and hosts raise `KeepOnTopChanged` from UI code.
- **`BringAllToFront`** calls `Bring()` on each host in *reverse* use order, skipping minimised ones, so `Current`
  is raised last and ends in front. Activating in reverse use order replays the same use order, so `Previous` is
  unchanged afterwards and nothing needs suspending; the only entries that move are the skipped minimised ones,
  which end up behind every raised one.
- **Testable without a window.** `SessionList` names no window or control type; tests drive it with a fake host
  and `FakeEmulatorSession`.
- **The tabs seam.** Nothing that consumes `SessionList` — the switcher, the Window menu, the Dock menu — knows what
  a host is. A tabbed window would implement `Bring()` as "select the tab, then activate the window", and keep
  `KeepOnTop` as its window's state.

## 4. `SessionMenuLabel`: one session as one line of menu text

A pure static in `src/LizTerm.App/Sessions/`, used by the Window menu and the Dock menu alike:

`[access-keyed position][two spaces][★ ][name][ - host][ (Disconnected)]`

- **Position** is `PositionOf`: 1-9 print as `_1`-`_9`, and 10 prints as `_0`. The underscore makes the digit the
  item's access key in the in-window menu, so **Alt, W, 3** reaches session 3 on Windows and Linux with no new
  chord. Past ten there is no position and no leading spaces.
- **★ and a space** when the profile carries FAVORITE (`ProfileRow.IsFavorite`).
- **Every underscore in the name and host is doubled**, so a profile called `MVS_PROD` does not grow an access key.
- **` - host`** (the profile's `Host`, no port — the window title's own shape) only when the name does not already
  contain the host, compared ignoring case. An ad hoc session is named `host:port` or `LU@host:port`
  (`StartupArguments.Resolve`), and the title's `sdf.example:3270 - sdf.example` repetition is not copied.
- **` (Disconnected)`** when `Connection` is `ConnectionState.Disconnected`. Any other state prints nothing.

| Profile | State | Position | Label |
|---|---|---|---|
| `TSO` on `tk5.local`, FAVORITE | connected | 3 | `_3  ★ TSO - tk5.local` |
| `CICS` on `zxplore.example` | disconnected | 4 | `_4  CICS - zxplore.example (Disconnected)` |
| ad hoc `sdf.example:3270` | connected | 10 | `_0  sdf.example:3270` |
| `MVS_PROD` on `mvs.local` | connected | 11 | `MVS__PROD - mvs.local` |

## 5. The switcher

### 5.1 Look and placement

- An overlay on the session window's screen area: a translucent black layer dims the terminal, and a panel sits at
  the top centre, 88% of the screen area's width, capped at 520 px. Top to bottom: the filter box
  (placeholder "Switch to a session: type to filter"), the rows, and a one-line key hint.
- The rows scroll when they do not fit; the filter box and the hint stay put.
- The hint reads `1–9, 0 jump · ↑↓ move · ↵ switch · Esc close` while the filter is empty, and
  `↑↓ move · ↵ switch · Esc close · digits type into the filter now` once it is not.

### 5.2 Keys and pointer

- **Opening** (§6) clears the filter, lists every entry in opening order, selects `Previous` — or `Current` when it
  is the only session — and focuses the filter box.
- **A digit 1-9 or 0 while the filter is empty** brings the entry at that position and closes the switcher. A
  digit with no entry at its position does nothing.
- **Any other typing goes into the filter**, including a digit once the filter holds anything. Changing the filter
  selects the first matching row. Deleting back to empty makes digits jump again.
- **Up and Down** move the selection among the visible rows and stop at the ends.
- **Enter** brings the selected entry and closes. With no rows, it does nothing.
- **Escape** closes, whatever the filter holds.
- **Cmd/Ctrl+K while open** closes it.
- **A click on a row** brings that entry and closes; **a click on the dimmed area** closes.
- **The window deactivating** closes it.
- Choosing the current session only closes the switcher. Choosing a disconnected session brings its window like
  any other.
- **Closing always returns focus to the screen**, as Find's Escape does.
- **If `Changed` fires while open** (a session closes or connects elsewhere), the rows rebuild and the selection
  stays on the same entry if it is still listed, else moves to the first row.
- **Nothing typed while the switcher is open reaches the host** — not text, not a keymap chord such as Alt+1. This
  is Find's invariant and is tested the same way (§10).

### 5.3 A row

Left to right:

1. **Number badge**, 18 px: the position (`1`-`9`, `0`), solid while digits jump, a dashed outline once the filter
   holds text, and empty past ten.
2. **Connection mark**: `○` when disconnected, `●` otherwise. Shape carries it, not colour, and the flag (item 4) adds the word.
3. **The Sessions list's row**, shared (§5.5): the FAVORITE star in its 15 px gutter; the name, cut with an
   ellipsis, with up to three chips and the overflow chip on the right; the second line; and the note as a grey
   italic third line, cut with an ellipsis and complete in a tooltip, only when the profile has one. For a saved
   profile the second line is `host:port`; for an ad hoc session it is `Quick Connect`, since the name already
   carries the host.
4. **Flag**: the first that applies of `Disconnected`, `This window` (the window the switcher opened in) and
   `On top` (the entry's host has Keep on Top set). Words, so no flag depends on colour or on a glyph a platform's
   UI font might not draw.
5. **Selection mark**: `↵` on the selected row only, beside the highlight bar.

### 5.4 `SessionSwitcherViewModel`

`src/LizTerm.App/ViewModels/`, the shape of `FindViewModel`: its own lifetime, no window or control type, plain
`[Fact]` tests.

- Built over the process's `SessionList` and the entry of the window it belongs to.
- `IsOpen`, `Term`, `Rows` (the visible `SwitcherRow`s), `Selected`, `DigitsJump` (true while `Term` is empty).
- `Open()`, `Close()`, `MoveUp()`, `MoveDown()`; `TryDigit(char)` and `Choose()` return the `SessionEntry` to bring,
  or null, and the view calls `Host.Bring()` on it. The view model never brings a window itself.
- `SwitcherRow`: `Entry`, `Number` (`"1"`-`"9"`, `"0"` or null), `Summary` (the `ProfileRow`), `IsDisconnected`,
  `IsCurrent`, `IsOnTop`, and `Flag` (the text of §5.3's item 4, or null).
- **The filter** is `SwitcherFilter.Matches(SessionEntry, string term)`: an ordinal, case-insensitive substring test
  of the term against the name, the second line's `host:port`, every tag name, and the note. An empty term matches
  everything.

### 5.5 The view

- `Controls/SessionSwitcher.axaml`, a `UserControl` layered over the screen area in `SessionWindow`, visible with
  `IsOpen`. Its filter `TextBox` is named `SwitcherBox`.
- **Keys are handled on the tunnel** at the control, as Quick Connect's box does: Up, Down, Enter, Escape,
  Cmd/Ctrl+K, and digits while `DigitsJump`, each marked handled. Everything else reaches `SwitcherBox` as typing,
  and the screen never has focus while the switcher is open.
- **The window's clipboard handlers must learn the new text field.** `SessionWindow`'s Copy, Paste and Select All
  handlers check `FindBox.IsFocused` first, because a platform gesture with a text field focused would otherwise
  act on the terminal — Cmd+V typing into the host. They check `SwitcherBox.IsFocused` the same way.
- **The Sessions list's row template moves to `App.axaml`** as a `DataTemplate` resource,
  `ProfileSummaryTemplate` (`x:DataType="vm:ProfileRow"`), beside `TagChipTemplate` and for the same reason: the
  list and the switcher cannot draw one profile two ways. `ProfilePickerWindow`'s `ItemTemplate` references it.
  Its named parts (`StarGlyph`, `OverflowChip`, `NoteLine`) keep their names, and `ProfilePickerWindowTests`
  passing unchanged is the proof that the move changed nothing.
- **`ProfileRow` gains a second line.** A constructor parameter `isSaved` (default true) and a `SecondLine`
  property — `HostPort` when saved, `Quick Connect` otherwise — which the template binds instead of `HostPort`.
  `HostPort` stays for its other readers.

## 6. The chord

- **`TerminalScreen.TryHandlePlatformGesture`** matches `Key.K` with `hotkeys.CommandModifiers` (Cmd on macOS, Ctrl
  elsewhere) and raises `SwitcherRequested`, exactly beside Find. It runs before `Keymap`. `DefaultKeymap` binds no
  Ctrl+K, and a future user keymap (#18) cannot take Ctrl+K from the switcher — the standing Ctrl+F already has, to
  be written into the Keyboard notes the same way.
- **Window > Switch Session...** carries the chord too, set by `ShowPlatformGestures` as
  `new KeyGesture(Key.K, hotkeys.CommandModifiers)` on the native item and as `InputGesture` on the classic one —
  Find's arrangement. On macOS under Native or Both, the native gesture is an AppKit key equivalent that fires
  before the responder chain, so `TerminalScreen` never sees Cmd+K; under In the window the emptied native menu
  installs no key equivalent and `TerminalScreen` handles it. On Windows and Linux the classic gesture is
  display-only and `TerminalScreen` handles it. Either way it fires once.
- **The gesture rule gains an exception.** `src/LizTerm.App/CLAUDE.md` says no window menu item outside Edit
  carries a `Gesture`, because a gesture there takes a key from the terminal. A Cmd chord cannot be a 3270
  keystroke, so Window > Switch Session... (Cmd+K) and Window > Minimize (Cmd+M, §7.4) are named exceptions, and
  `NativeMenuTests.Only_the_edit_menu_carries_gestures` allows exactly those two.
- With focus in `FindBox` on Windows or Linux, Ctrl+K reaches the text box rather than the screen and does
  nothing; the menu item still works. With focus in `SwitcherBox`, §5.5's tunnel handler closes the switcher.

## 7. The Window menu

### 7.1 Declared items

`_Window`, between `_Keys` and `_Help`, declared in `SessionWindow.axaml` in both the `NativeMenu` and the classic
`Menu`, so the parity walk covers it:

| Item | Kind | Notes |
|---|---|---|
| `_Minimize` | Click | macOS only (§7.4); native `Gesture` Cmd+M |
| `_Zoom` | Click | macOS only (§7.4) |
| separator | | hidden with the two above |
| `_Keep on Top` | CheckBox, Click | §7.3 |
| `_Switch Session...` | Click | §6's chord |
| separator | | |
| `_Bring All to Front` | Click | `SessionList.BringAllToFront` |
| separator (`SessionsSeparator`) | | hidden when there are no session rows |
| session rows | generated | §7.2 |

Every item has a Click handler: the macOS exporter enables an item only with a `Command` or a click handler, and
the in-window fallback raises clicks only to handlers. Off macOS, `ApplyPlatformMenuRules` hides Minimize, Zoom and
their separator in both renderers.

### 7.2 Session rows

- **Built in code**, after `SessionsSeparator`, one per entry in opening order, in both menus from the same list:
  a `NativeMenuItem` and a `MenuItem`, each with `Header = SessionMenuLabel`, `ToggleType` CheckBox,
  `IsChecked = entry == SessionList.Current`, and a Click handler that calls `entry.Host.Bring()`.
- **Rebuilt on `Changed`**: remove every item after `SessionsSeparator`, add the new ones. The Window submenu's own
  `NativeMenu` instance is changed in place and never replaced, the rule #60 taught for the window's top-level menu.
  Under In the window the top-level items are stashed, but the stashed Window item is the same instance, so a refill
  brings back an up-to-date list.
- **The click handler puts every check mark back** from `SessionList.Current`, Keys > Insert's pattern: both
  renderers write `IsChecked` before the click arrives, and bringing the window that is already current raises no
  `Changed` to correct it.
- **Subscription follows the window's life:** `SessionWindow` subscribes to `Changed` in `Opened` and unsubscribes in
  `Closed`, for the reason the menu-style subscription does — a window built and never shown never raises `Closed`.
- `App` gives the window the process's `SessionList` and the window's own entry before `Show()`. A window given
  none, as most existing tests build, has no session rows and a hidden `SessionsSeparator`.

### 7.3 Keep on Top

- `SessionWindow` implements `ISessionHost`: `KeepOnTop` is `Topmost`, and `KeepOnTopChanged` is raised when
  `Topmost` changes.
- The item's check mark is set from `Topmost` by code-behind, and its Click handler flips `Topmost` and then puts
  the mark back, because both renderers toggle the mark before the click arrives.
- Per window, never saved: a new session window starts with it off.
- The flag shows in the switcher (§5.3) and raises `Changed`, so every other window's switcher is current.

### 7.4 Minimize and Zoom (macOS)

- **Minimize** sets `WindowState.Minimized`. Its native item carries `Gesture` Cmd+M; the classic item shows no
  `InputGesture`, since nothing but the native key equivalent dispatches Cmd+M and under In the window it does not
  work.
- **Zoom** switches `WindowState` between `Maximized` and `Normal`.
- **Bring** (`SessionWindow.Bring`, for every row, the switcher and the Dock menu) sets `WindowState.Normal` when
  minimised, then calls `Activate()`.

## 8. The Dock menu (macOS)

- `App` builds one `NativeMenu` and attaches it with `NativeDock.SetMenu(app, menu)`. Avalonia.Native exports a dock
  menu from the application (`SetupApplicationDockMenuExporter`), so it is independent of which window is in front.
- Its items: the session rows (§7.2's construction and labels, check mark on `Current`, click to `Bring`), a
  separator, and `New Session...`, which calls `ShowPicker()`. With no sessions open it holds `New Session...` alone.
  macOS appends its own Options, Show All Windows, Hide and Quit below; they are not ours and no test sees them.
- Rebuilt on `Changed`. The builder takes the platform as an argument, the shape `MenuStrategy.Resolve` uses, so a
  test on any OS can build it; `App` attaches it only when `OperatingSystem.IsMacOS()`.
- No Switch Session... here: the switcher opens inside a session window, and every session is already listed.
- Windows' taskbar and Linux task lists already list windows by title, so nothing is added there.

## 9. App wiring

- **`OpenSession`** loads the tag registry snapshot once and hands it to both the view model (as today) and a
  `new ProfileRow(profile, snapshot, isSaved: fromStore)`; builds the entry with the window as its host; `Add`s it;
  gives the window the list and its entry (§7.2); forwards the window's `Activated` to `SessionList.Activated`; and
  on `Closed` calls `Remove` before the `ShutdownPolicy.UserClosedLastWindow` test, which now reads
  `SessionList.Count`.
- **About and the startup update check** read `SessionList.Current` where they read `_lastActiveSession`. One
  deliberate difference: when the last-active session closes, the fallback becomes the next most recently *used*
  session rather than the most recently *opened*, which is the better answer to "the session the user was last in".
- **The Sessions list is not an entry.** File > New Session... and the Dock's New Session... raise it.
- **Threading**: all on the UI thread, per §3.

## 10. Testing

### 10.1 Pure (`[Fact]`)

- **`SessionListTests`**: opening-order positions; closing renumbers later entries; the tenth is position 10 and the
  eleventh has none; `Current` and `Previous` follow `Activated`; `Changed` fires on add, remove, a `Current`
  change, a `Connection` change and `KeepOnTopChanged`, and not after `Remove` for that entry's view model;
  `BringAllToFront` brings in reverse use order, skips minimised hosts, and leaves `Previous` unchanged.
- **`SessionSwitcherViewModelTests`**: opens on `Previous`, or `Current` when alone; a digit jumps only with an
  empty term; a digit after a letter joins the term; the filter matches name, host, a tag and a note, ignoring
  case; Up and Down stay within visible rows and stop at the ends; Enter with no rows returns null; a `Changed`
  while open keeps the selected entry or falls to the first row.
- **`SessionMenuLabelTests`**: the four rows of §4's table, plus `LU@host:port` without ` - host`.
- **`ProfileRowTests`**: `SecondLine` is `host:port` when saved and `Quick Connect` when not.

### 10.2 Headless (`[AvaloniaFact]`, `FakeEmulatorSession`)

- **The chord opens the switcher under every menu style**, a theory modelled on
  `NativeMenuTests.The_find_gesture_opens_the_bar_via_TerminalScreen`.
- **Nothing reaches the host**: with the switcher open, a digit, letters, Alt+1, Enter and Escape leave the fake
  session's recorded calls empty, and Escape leaves the screen focused.
- **Clipboard guards**: Paste with `SwitcherBox` focused pastes into the box and records no call on the session.
- **The Window menu** with three seeded entries: the parity walk passes; `Every_native_item_can_actually_be_activated`
  covers the generated rows; clicking a row brings that host; the check mark follows `Current` and survives a click
  on the current row; Keep on Top toggles `Topmost` and its mark; a `Remove` or a `Connection` change rebuilds the
  rows; with no list, `SessionsSeparator` is hidden.
- **`Only_the_edit_menu_carries_gestures`** allows exactly Window > Switch Session... and Window > Minimize.
- **Minimize, Zoom and their separator** are hidden for a window built with `isMacOS: false` and shown for `true`.
- **The Dock menu builder** lists the seeded sessions, a separator and New Session..., holds New Session... alone
  with none, and rebuilds after a `Remove`.
- **The Sessions list**: `ProfilePickerWindowTests` pass unchanged after the template moves.

### 10.3 Live checks, before release

The headless platform cannot show these; each is a manual step on the real app.

1. **Keep on Top floats above other applications' windows** on macOS while another app is frontmost, and on Linux
   under at least GNOME; note the result for window managers that ignore it.
2. **A Dock-menu session brings LizTerm forward** while another application is active, including a minimised one.
3. **Cmd+K fires once** under Native, In the window and Both on macOS, and never reaches the host.
4. **A doubled underscore** reads as one underscore in a macOS menu-bar item and the Dock menu.
5. **Alt, W, digit** reaches a session on Windows and Linux.
6. **Zoom** on macOS behaves as the green button's zoom does.

## 11. Documentation

Each fact in its one home (root `CLAUDE.md`):

- **`docs/user-guide.md`**: a new *Several sessions* section after *The session window* (the switcher, the Window
  menu, the Dock menu, Keep on Top), listed in the contents; Cmd/Ctrl+K in *Keyboard*; the Window menu in *Menus*.
- **`CHANGELOG.md`**: a new `## Unreleased` heading, which 0.6.0's bump consumed, with one entry.
- **`README.md`**: one bullet in the feature list.
- **`src/LizTerm.App/CLAUDE.md`**: a *Session switching* section — `SessionList` as the tabs seam, the rules for
  generated menu rows, the Dock menu, the `SwitcherBox` clipboard guard — and the Cmd-chord exception under
  *Gestures*; the Ctrl+K note beside Ctrl+F under *Keyboard*.
- **`tests/CLAUDE.md`**: how to seed a `SessionList` with fake hosts for the menu and switcher tests.
- **`docs/architecture.md`**: no change; the rule it states still holds.

## 12. Follow-ups, each needing Robert's OK

- A comment on #46 summarising these decisions and linking this spec; its body predates them.
- A new issue for tabs as a preference, recording §3's seam.
