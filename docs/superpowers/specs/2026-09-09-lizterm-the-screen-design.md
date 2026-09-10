# LizTerm: the screen — crosshair, find, and capture

Date: 2026-09-09. Parent spec: `2026-09-03-lizterm-v1-design.md`. Predecessor:
`2026-09-09-lizterm-v031-fix-and-polish-design.md` (v0.3.1, merged to `main` as e852a96 earlier the same day).
Status: approved in discussion on 2026-09-09; awaiting review of this text.

## 1. Purpose

Three tier-1 issues, all about the screen as a thing you look at rather than type into: #27 (a crosshair ruler
that follows the cursor), #26 (find on screen), and #25 (screen capture to a file and to the clipboard).

They are one milestone because they share a mechanism. Two of the three paint an overlay on `TerminalScreen`,
and all three are read-only views of a `ScreenSnapshot` that the engine has no part in producing. Nothing here
adds a member to `IEmulatorSession`, and nothing here touches the profile schema, the profile editor, the
picker, or `ProfileStore`.

Vista TN3270's feature list is the behavioural reference for all three, as the v1 design spec directs where the
spec is silent.

Decisions taken in the brainstorm on 2026-09-09, each of which the relevant section justifies in full:

- **Find moves the host cursor** on Enter (§5.4), rather than being a read-only highlight.
- **Find recomputes on every repaint** and re-anchors the current match by position (§5.5).
- **The crosshair is a per-window View menu setting with four states**, not persisted and not a profile field
  (§4.2). Persisting it belongs with #19.
- **Capture is rendered by us, not by b3270's `PrintText`** (§3.1). This is the decision that reshapes #25
  most, and it is a reversal of what the issue proposes.

## 2. The overlay rule, which all three obey

`TerminalScreen.Render` (`src/LizTerm.App/Controls/TerminalScreen.cs:308`) draws a background, then a *run
plan* — each row segmented into runs of identical style, with its text already shaped — held for as long as
the snapshot instance and the `CellGeometry` are unchanged (`EnsureRunPlan`, `:365`). `RunPlanBuilds` (`:63`,
incremented at `:386`) is the test seam that proves a repaint reused it.

**Every overlay this milestone adds is drawn after the run plan and never enters it.** Folding a find
highlight or a crosshair into `RunVisual` (`:359`) would re-segment and re-shape every cell on every keystroke
in the find box — and again twice a second, for as long as anything on the screen carries `Blink`, because a
blink phase flip is already a full `InvalidateVisual` (`:71`).

`DrawSelection` (`:411`) is the precedent and the shape to copy: read a `ScreenRegion`, `Clamp` it to the
snapshot, turn it into a rectangle through `CellGeometry.CellRect` (`Rendering/CellGeometry.cs:46`), fill it
with a translucent brush from `Palette`.

The resulting draw order in `Render`:

```
background → run plan → crosshair → selection → find matches → current find match → cursor
```

The crosshair is the lowest of the overlays because it is the dimmest and the most persistent; find sits above the
selection because it is the more transient of the two and the one the user is actively driving; the cursor
stays on top, as it is today.

`AffectsRender<TerminalScreen>` (`:49`) currently registers `SnapshotProperty` and `SelectionProperty`. The
three new styled properties join it. **No new invalidation source is added** — in particular the crosshair
needs none, because the cursor moves only on a published snapshot, which already invalidates.

## 3. #25: screen capture

### 3.1 Rendered by us, not by the engine — a reversal of the issue

#25 is written around b3270's `PrintText`, which is registered unconditionally in `Common/print_screen.c`,
is in 4.5ga6's `Query(Actions)` output, and can emit both plain text and a styled HTML table. The issue's
framing is "the engine we already ship has all of them" and "it costs us nothing".

It does cost something, and the cost is in the wrong place:

- It adds a member to `IEmulatorSession`, which `B3270Session`, `FakeEmulatorSession` and every App test that
  stands on the interface would carry forever, for a feature that needs no engine at all.
- It only works while connected. A user who wants to keep the screen that just showed an error — the exact
  moment capture is most wanted — may be looking at a session the host has already dropped.
- It returns *b3270's* colours, not LizTerm's. `Palette` (`src/LizTerm.App/Rendering/Palette.cs`) is a
  deliberate scheme, and a capture that does not match the window it was taken from is a bug report waiting to
  be filed.
- `PrintText()` with neither `string` nor `file` **prints** — on Unix by running `printTextCommand` through a
  shell from a child process whose stdout we own. That trap exists for as long as the code path does.

We already hold everything needed. `ScreenSnapshot` carries per-cell foreground, background and
`CellRendition` — it is what the renderer draws from sixty times a second — and `Palette.ColorOf(HostColor)`
maps colour to RGB.

**So capture is a pure formatter over the snapshot, in the App layer.** No engine call, no interface change,
no backend work, no new fake behaviour, and it works while disconnected.

The App layer rather than Core because the colours live in `Palette`, which is Avalonia-typed and therefore
App-only under the dependency rule. Pushing the RGB table down into Core to make the formatter Core-legal is
a larger change than this milestone earns, and it would leave `Palette` as a thin forwarder.

### 3.2 The two formats

**Plain text is already done.** `ScreenSnapshot.ToText()` (`src/LizTerm.Core/Screen/ScreenSnapshot.cs:53`)
joins `RowText` for every row with `\n`. Capture calls it.

**HTML** is a new formatter, `src/LizTerm.App/Capture/ScreenHtml.cs`, emitting a `<pre>` of one `<span>` per
run of identically-styled cells:

```html
<span style="color:#50FF50;background:#000000;font-weight:bold;text-decoration:underline">READY</span>
```

- The run segmentation is the same rule the renderer uses: equal foreground, background and rendition. The
  two **share the predicate** rather than each holding a copy — `Cell.SameStyleAs` lives in Core, and both
  `TerminalScreen.EnsureRunPlan` and the formatter call it, so they cannot disagree about where a run ends.

  (An earlier draft of this section had the formatter duplicate the renderer's private `SameStyle`, justified
  by `RunVisual` holding shaped `FormattedText` and Avalonia brushes. That reasoning was wrong: the predicate
  compares only `Foreground`, `Background` and `Rendition`, all Core types, and names no Avalonia type at all
  — only what *consumes* its result differs. Corrected during Task 2's review, which also removes the need for
  a test comparing run counts both ways: agreement is now true by construction.)
- `Reverse` swaps foreground and background, `Highlight` becomes `font-weight:bold` (the renderer's own
  35%-toward-white blend is a display trick that would read as washed-out text in a browser), `Underline`
  becomes `text-decoration:underline`.
- `Blink` is **not** emitted. There is no non-annoying HTML for it, and a captured screen is a still.
  Recorded here so its absence is not read as an oversight.
- Text is HTML-escaped. A 3270 screen can hold `<`, `>` and `&`, and an unescaped capture pasted into a forum
  post is both wrong and an injection vector for whoever renders it.

### 3.3 Surfaces

**File → Save Screen As…** — through the existing `IFilePicker`. Format is chosen from the returned path's
extension: `.html`/`.htm` → HTML, anything else → text. We write the bytes ourselves rather than handing a
path to the engine, for the reason `FileTransferViewModel` already reasons about — the OS Save dialog has
just made a promise about overwriting, and only we can keep it.

The picker seam follows `CreateTransfer(IFilePicker)` (`SessionViewModel.cs:220`) exactly: the window
constructs `new AvaloniaFilePicker(this)` and passes it in, so tests use `FakeFilePicker`.

`IFilePicker.PickSaveLocationAsync` (`Files/IFilePicker.cs`) hardcodes `Title = "Save received file as"` in
its Avalonia implementation (`Files/AvaloniaFilePicker.cs:28`), which is transfer wording. **Add a `title`
parameter** rather than a near-duplicate second method; the one existing caller and `FakeFilePicker` are
updated with it.

The suggested filename reuses the convention `WireLogFileName` established (`SessionViewModel.cs:145`): the
profile name sanitised to ASCII alphanumerics plus `._-`, a `yyyyMMdd-HHmmss` stamp, and the extension —
`screen-TK5-20260909-142233.txt`. The sanitiser is extracted so the two callers cannot drift.

**Edit → Copy Screen as HTML** — through the existing `ITextClipboard`. No dialog. This is the cheapest
useful thing in the issue and the one that actually reaches a bug report.

Both are enabled whenever there is a screen to capture (`Screen is not null`), not on `IsConnected` — that is
the whole point of not using the engine.

## 4. #27: the crosshair ruler

### 4.1 Not the engine's toggle

b3270 registers a `CROSSHAIR` toggle and round-trips it happily. **We do not use it.** b3270 has no display,
so the setting is inert on its side; routing a display preference through a child process to have it handed
back would make the crosshair unavailable while disconnected and gain nothing. #27 says this and it is right.

### 4.2 Shape

`CrosshairMode { None, Horizontal, Vertical, Both }` in `src/LizTerm.App/Rendering/`, and a matching styled
property on `TerminalScreen`. Vista offers all three shapes; horizontal-only is the one that earns its keep on
a wide SDSF panel, where the problem is tracking a row across 132 columns.

`DrawCrosshair` is two `FillRectangle` calls at most, from `snapshot.Cursor` through `CellGeometry`: the
horizontal bar spans the full width at the cursor's row, the vertical the full height at its column.

**It follows a hidden cursor.** `enabled:false` in a `screen` indication hides the cursor while keeping its
position (`CursorPosition.Visible`), and `DrawCursor` (`:419`) returns early on it. The crosshair does not:
its job is column alignment, not showing where input will land, and Vista draws its ruler regardless.

One new `Palette.Crosshair` brush, translucent enough that host text reads through it, beside
`Palette.Selection` (`Rendering/Palette.cs:17`). **The colour is not user-choosable.** Vista lets the user
pick it; we should not until #19 gives a preference somewhere to live, and inventing a home for one setting is
how a preferences system gets built by accident.

### 4.3 Where the setting lives

A **View** menu on the session window, per window, not persisted. Four items, one per mode.

The state itself is a `CrosshairMode` property on `SessionViewModel`, bound to `TerminalScreen.Crosshair` in
XAML and to each menu item's `IsChecked`. Holding it in the window's code-behind instead would leave the
native items with nothing to bind `IsChecked` to and force the check marks to be driven imperatively, which is
exactly the shape trap 2 in §6 exists to avoid. It is view-model state in the ordinary sense — per window,
owned by the window's data context, and testable with a plain `[Fact]`.

Persisting it as a `SessionProfile` field was considered and rejected for this milestone: it is a *display*
preference rather than a *connection* one, so the profile is the wrong home for it on the merits, and #19 (a
preferences window) is where the right home gets built. A per-window setting that resets on relaunch is a
small daily annoyance, and it is the honest cost of not pre-empting #19.

## 5. #26: find on screen

### 5.1 Searching is pure and lives in Core

`src/LizTerm.Core/Screen/ScreenSearch.cs`, BCL-only and therefore Core-legal:

```csharp
public static IReadOnlyList<ScreenRegion> Find(ScreenSnapshot snapshot, string term)
```

- Case-insensitive substring, matched per row against `RowText` (`ScreenSnapshot.cs:51`), in reading order:
  row ascending, then column.
- **No wrapping across row boundaries.** A 3270 screen is a grid of independent rows, not reflowed text; a
  match spanning a row edge is almost always two unrelated fields that happen to abut.
- Overlapping matches are not returned twice: the scan resumes after the end of each match.
- An empty or whitespace-only term returns nothing rather than every position.
- **A match cannot begin or end mid-character**, by construction rather than by rejection. `Cell.Character` is
  a `Rune` and a DBCS character occupies two cells, the second carrying `CellRendition.RightHalf`
  (`src/LizTerm.Core/Screen/CellRendition.cs`, `RightHalf = 2048`). So the search does not run over
  `RowText`: it first folds each row into a sequence of one entry per *character*, each remembering its first
  and last column, and matches against that. A half-cell match is then not a case to reject — it has no
  representation.

  This also avoids a real indexing bug. `RowText` is built by appending `Rune.ToString()` per cell
  (`ScreenSnapshot.cs:42`), which is one UTF-16 char for BMP runes and two otherwise, so a string index into
  it is not a column index in general. Searching the cells directly keeps the mapping exact.

`ScreenRegion` is already the right return type — inclusive, zero-based, always normalized — and is already
what `TerminalScreen` knows how to paint.

### 5.2 Find state is its own view model

`src/LizTerm.App/ViewModels/FindViewModel.cs`, not more weight on `SessionViewModel`.

`SessionViewModel` is 550-odd lines and owns the session, the connection lifecycle, the certificate prompt,
the clipboard, the wire log and the transfer factory. Find is a coherent unit with its own lifetime — it opens,
it holds a term and a match list and a current index, it closes — and it is testable on its own with plain
`[Fact]` tests, since it touches no Avalonia type.

Its dependencies are two, both injected the way this codebase already injects seams:

- `Func<int, int, Task> moveCursor` — `SessionViewModel.MoveCursorAsync` (`:458`) in the app, a recorder in
  tests.
- Snapshots pushed in, not subscribed to: `SessionViewModel.ApplyScreen` gains one line calling
  `Find.OnScreen(snapshot)`. Keeping the subscription in one place preserves the rule that all backend events
  are marshalled through `dispatch` exactly once.

`SessionViewModel` therefore grows exactly four things across this whole milestone: a `Find` property, one line
in `ApplyScreen`, the capture commands from §3.3, and the `CrosshairMode` property from §4.3. Everything else
lands in new files or in `TerminalScreen`.

### 5.3 The gesture has to be built, not read

**Avalonia 12.1.2's `PlatformHotkeyConfiguration` has no `Find`.** Verified against the pinned assembly
(`~/.nuget/packages/avalonia/12.1.2/ref/net10.0/Avalonia.Base.dll`), whose property getters are exactly
`get_Copy`, `get_Paste`, `get_SelectAll`, `get_OpenContextMenu` and `get_CommandModifiers` — there is no
`get_Find`. So the three existing gestures can be read from the platform (`TerminalScreen.cs:197-200`) and
this one cannot.

Build it from the platform's own modifier instead of hardcoding a key:

```csharp
new KeyGesture(Key.F, hotkeys.CommandModifiers)
```

That yields Cmd+F on macOS and Ctrl+F elsewhere, derived rather than assumed. It is checked in
`TryHandlePlatformGesture` — renamed from `TryHandleClipboardKey` when this arm was added, since it is no
longer only about the clipboard — which already runs before `Keymap.TryMap`, so the find gesture,
like copy and paste, takes precedence over the terminal keymap.

**Ctrl+F is free.** `DefaultKeymap`'s complete set of Control chords is Ctrl+Enter (Enter), Ctrl+R (Reset),
Ctrl+Escape (Clear), Ctrl+F1..F12 (PF13-24), Ctrl+Home (PA2), Ctrl+PageUp (PA3), Ctrl+`[` (`¬`) and Ctrl+6
(`¢`). Ctrl+F appears in none of them, so nothing is taken away from the terminal on any platform.

### 5.4 Enter moves the host cursor

Enter advances to the next match **and** calls `MoveCursorAsync(match.Top, match.Left)`; Shift+Enter goes
back. Both wrap.

This makes find a navigation tool rather than a highlighter — find a dataset name in an ISPF member list, then
type beside it — and it is defensible against the "find should be read-only" objection on three counts:

- `MoveCursor` sends no AID. It is not host *input* in the sense that matters; it is the same operation a
  plain mouse click already performs on release.
- `SessionViewModel.MoveCursorAsync` (`:458`) already nulls `Selection`, so the existing rule that moving the
  cursor clears the selection applies to find for free, with no new code and no new exception.
- A locked keyboard makes b3270 refuse the move, raising `EmulatorActionException`, which `Guard` already
  swallows deliberately because the keyboard lock has explained itself in the OIA. So it degrades quietly
  rather than putting a banner in front of the user mid-search.

### 5.5 Recompute on repaint, re-anchor by position

A 3270 screen repaints constantly — every keystroke echo is a new snapshot. Clearing matches the way
`Selection` is cleared would make the highlight vanish almost immediately and read as broken.

So `OnScreen` re-runs the search against each new snapshot. The cost is a substring scan of 1,920 to 5,676
cells, done where snapshots are already marshalled to the UI thread and **never on the render path**.

The current match is re-anchored by position: if a match in the new list begins at the same (row, column) as
the old current match, that becomes the new current index; otherwise it resets to the first. So a repaint that
leaves your match where it was does not move you, and one that rewrites the screen underneath you starts over
rather than pointing at something arbitrary. This rule is a pure function and is tested as one.

**A reset to the first match counts as unvisited.** Enter moves the host cursor, so find tracks whether the
cursor has actually been moved to the match now highlighted — that is what makes typing highlight match 1
without sending a `MoveCursor` per character, and the first Enter land *on* it rather than past it. A re-anchor
that keeps your place must preserve that flag, but a re-anchor that falls back has put you on a match the
cursor has never visited, and must clear it. Missing this reproduces the same skip-a-match bug through a
repaint that the flag exists to prevent for typing; it was found in review and both directions are now tested.

### 5.6 The bar

A `Border` docked `Bottom` in `SessionWindow.axaml`'s `DockPanel`, declared after the error bar so it renders
directly beneath the screen and above both the error bar and the status bar. Not a modal dialog: a dialog
would take focus off the thing being searched and cover it.

It holds a `TextBox`, a match count (`3 of 7`, or `No matches`), previous/next buttons and a close button.
Its `IsVisible` binds to `Find.IsOpen`.

- Opening focuses the text box; while it has focus the terminal does not receive keys, which is correct.
- Typing re-runs the search on every change.
- Escape closes the bar and returns focus through `Screen.Focus()`, the same path the error bar's Dismiss
  already uses (`SessionWindow.axaml.cs:158`).
- Closing clears the match list, so no stale highlight survives the bar.

Matches paint as a distinct `Palette.FindMatch`; the current match as `Palette.FindCurrent`, distinct again.

## 6. Menus, and the three traps

A new **View** menu holding the crosshair's four states, plus **File → Save Screen As…**, **Edit → Copy Screen
as HTML** and **Edit → Find…**. Both menus, native and classic, in the conventional order File, Edit, View,
Keys, Help.

Find goes in **Edit** specifically. CLAUDE.md's rule is that no menu item outside Edit ever carries a
`Gesture`, because a `NativeMenuItem` gesture is an AppKit key equivalent that `NSApplication.sendEvent:`
dispatches ahead of the key window's responder chain — `Gesture="F1"` would silently swallow PF1. Edit is the
one menu with an established, safe route for this: `ShowPlatformGestures` (`SessionWindow.axaml.cs:130`)
assigns `InputGesture` on the classic items and `Gesture` on the native ones, via
`MenuLookup.Required(menu, "_Edit", child)`. Find's gesture is installed there, from §5.3's constructed
`KeyGesture`.

Three documented traps this has to clear:

1. **Every native item needs a `Command` or a `Click`.** Avalonia's macOS exporter validates each item with
   `(Command != null || HasClickHandlers) && IsEnabled`, so an item carrying only a binding is greyed out on
   macOS and inert everywhere. `NativeMenuTests.Every_native_item_can_actually_be_activated` is the guard.
2. **A `NativeMenuItem` never toggles itself.** `RaiseClicked` raises Click and executes Command and never
   touches `IsChecked`. The native crosshair items therefore use the `Mode=OneWay` binding plus `Click` handler
   shape that Wire Log already uses (`SessionWindow.axaml:82`), not a two-way binding.

   (As shipped, the classic items use that same OneWay-plus-Click shape too, not the two-way, handler-free one
   this paragraph originally described. Wire Log's classic item can stay two-way and handler-free because
   `DefaultMenuInteractionHandler.Click` toggling a `MenuItem`'s `IsChecked` *before* raising Click is exactly
   right for one independent bool. The crosshair is four radio items sharing one `CrosshairMode` property, and a
   two-way `IsChecked` binding on any of them would need `CrosshairModeConverter.ConvertBack` to turn a bare
   `true` back into a specific mode — which it cannot: a `bool` says nothing about which of the other three
   should become `false`, or which mode a click's own `false` should fall back to, so `ConvertBack` throws rather
   than guess. Both menus' crosshair items therefore need the `Click` handler to set `Crosshair` explicitly, with
   `Mode=OneWay` so it is the resulting property change, not the click, that writes every item's check mark back
   — the three corrections to unchecked included. Two other spec deviations on this branch were recorded this
   same way; this one was missed until the final review.)
3. **`ToggleType="Radio"` on a `NativeMenuItem` is unverified.** `ToggleType` exists and `CheckBox` is proven
   in this app; whether the macOS exporter renders `Radio` as a radio group is not. It cannot be checked
   before a menu exists to check it on, so **the plan's View menu task carries the observation** — on a real
   GUI session, at the point the four items first exist.

   The *fallback is decided here, in advance*, so the task is an observation rather than a decision: if
   `Radio` does not render as a group, the items become `CheckBox` and the Click handlers keep exactly one
   checked. That is behaviourally identical either way, because the handlers must set the checks themselves
   regardless — trap 2 means a `NativeMenuItem` never toggles itself. `Radio` is therefore a presentation
   preference, and nothing else in this milestone depends on which way it goes.

`NativeMenuTests.The_window_menu_has_the_same_four_top_level_menus_as_the_classic_one` (`:74`) becomes five and
is renamed.

## 7. Testing and verification

Unit and headless coverage, all of which runs in the ordinary suite:

- `ScreenSearch` — plain `[Fact]` in Core.Tests: ordering, case-insensitivity, overlap, empty term, no
  cross-row match, and the DBCS half-cell rejection.
- `ScreenHtml` — plain `[Fact]` in App.Tests: run segmentation agreeing with `SameStyle`, reverse video,
  escaping of `<`, `>` and `&`, and the deliberate absence of blink.
- `FindViewModel` — plain `[Fact]`: navigation and wrap, the re-anchor rule under a changed snapshot, the
  cursor move on Enter recorded through the injected delegate, and matches cleared on close.
- `TerminalScreen` — `[AvaloniaFact]`: each crosshair mode draws what it should and `None` draws nothing; the
  crosshair follows a hidden cursor; and **`RunPlanBuilds` does not increase** when only the crosshair mode or
  the match list changes. That last one is the guard for §2 and is the reason `RunPlanBuilds` exists.
- `NativeMenuTests` — parity across the new View menu, activation of every new native item, and the crosshair
  items' OneWay-plus-Click behaviour driven through `RaiseClicked`, which is the one entry point both real
  renderers use.
- `SessionViewModel` — capture commands against `FakeFilePicker` and `FakeTextClipboard`, including that both
  are available while disconnected.

Manual pass on macOS against a live TN3270 host, which is what CI cannot answer:

- The crosshair renders legibly over real host text in all four modes, and `Radio` renders as intended in the
  real macOS menu bar (§6, trap 3).
- Cmd+F opens the bar, Enter walks matches and moves the host cursor on a real ISPF panel, and Escape returns
  focus so the next keystroke reaches the terminal.
- A captured `.html` opened in a browser matches the window it was taken from.

Zero warnings on `dotnet build LizTerm.slnx --no-incremental`, per CLAUDE.md.

## 8. Out of scope

- **Printing (#25's part 3).** The engine's print resources differ by platform — Windows drives GDI through
  seven `printText*` resources, everything else shells out through a single `printTextCommand` — so "Print…"
  is a materially different feature on each, and the platform split wants deciding on its own evidence.
  Sections 3.1-3.3 deliberately leave the engine out of capture entirely, which does not prejudge that
  decision.
- **Persisting the crosshair (#19).** §4.3.
- **A user-choosable crosshair or find colour (#19).** §4.2.
- **Find across a scrollback or history.** LizTerm has no scrollback; find is over the current snapshot, which
  is the whole of what a 3270 screen is.
- **Regular expressions in find.** Substring first. A regex box is a different feature with a different error
  surface, and nothing about this design forecloses it.
- **The engine's `CROSSHAIR` toggle.** §4.1 — recorded because "b3270 has a crosshair" is the obvious wrong
  conclusion to draw from its settings list.
