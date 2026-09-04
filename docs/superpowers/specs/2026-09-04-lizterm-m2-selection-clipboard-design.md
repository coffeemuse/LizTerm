# LizTerm Milestone 2, Plan 1: Selection and Clipboard

Date: 2026-09-04
Status: approved in discussion, pending written review
Parent: `2026-09-03-lizterm-v1-design.md` sections 6.2, 6.3, 6.5, and 6.6

## 1. Purpose

Give the session window mouse selection, Copy, Paste, and Select All, so a user can lift JCL or a
dataset name off a screen and put text back onto one. This is the first of the three Milestone 2
plans (selection and clipboard; IND$FILE transfer; polish bundle). It touches Core and App only.
The b3270 backend already implements `PasteTextAsync` over the hex-encoded, margin-aware
`PasteString` action and needs no change.

Decisions made in discussion on 2026-09-04:

- Gestures: click-drag selects a rectangle; double-click selects a word. No triple-click.
- Lifetime: a selection clears on any input sent to the host (key, typed text, paste, cursor move
  by click) and on any new mouse action (press, drag, Select All). It survives host screen updates.
- Appearance: one translucent overlay in a fixed muted blue painted over the selected cells. Host
  colors stay readable underneath and the cursor is drawn on top.
- Structure: layered by responsibility (approach B below), so every piece of logic is testable with
  the test style the repository already uses.

## 2. Approach

Three placements were considered. Everything in the screen control (least plumbing, but the view
model cannot gate Copy and Paste, clipboard behavior is testable only through the headless stub,
and clearing on host input needs the window to poke the control). Gesture logic in the view model
(gesture tests avoid the headless platform, but every pointer event round-trips through the
window and the control still needs capture and hit testing). The chosen layering:

- Core owns the region type and the text extraction, pure and BCL-only.
- A pure `SelectionGesture` class in App turns cell-level events into a region with no Avalonia
  types.
- `TerminalScreen` translates pointer events to cells, feeds the gesture, exposes the selection as
  a styled property, paints the overlay, and recognizes the platform Copy, Paste, and Select All
  hotkeys. It never touches the clipboard.
- `SessionViewModel` owns the three commands and the lifetime rule. Clipboard access goes through
  a two-method interface injected like the existing dispatch delegate.

The dependency rule of the parent spec holds: Core mentions neither Avalonia nor b3270; App names
`LizTerm.Backend.B3270` only in `SessionFactory`.

## 3. LizTerm.Core

### 3.1 ScreenRegion

A readonly record struct with inclusive, zero-based `Top`, `Left`, `Bottom`, `Right`.

- `FromCorners(row1, col1, row2, col2)` normalizes, so a drag that ends above or left of its anchor
  still yields `Top <= Bottom` and `Left <= Right`.
- `Full(rows, columns)` covers the whole grid.
- `Contains(row, column)` is inclusive on all edges.
- `Clamp(rows, columns)` trims a region to the grid and returns null when nothing remains.
- `Rows` and `Columns` are the inclusive extents.

### 3.2 ScreenSnapshot additions

- `GetText(ScreenRegion region)`: clamps the region to the snapshot, then returns one line per row,
  each trimmed of trailing spaces (U+0020 only), joined by `\n` with no trailing newline. A
  single-row region contains no newline. A region that clamps to nothing returns the empty string.
- `WordAt(int row, int column)`: returns the region covering the maximal run of non-space cells on
  that row containing the cell, or null when the cell is a space. Non-space is the whole rule:
  `SYS1.PROCLIB(IEFBR14)` and `JOB01234` come out in one piece.

Cells hold what b3270 renders, so a region over a field b3270 rendered blank, such as a password
field, copies blanks.

## 4. LizTerm.App

### 4.1 SelectionGesture (pure)

Lives in `src/LizTerm.App/Selection/SelectionGesture.cs`. Consumes cell coordinates, never pixels
or pointer types.

- `Press(row, column)`: records the anchor, clears the current region, marks no drag yet.
- `Move(row, column)`: with an anchor set, and either the cell differing from the anchor or a drag
  already in progress, marks the sequence as a drag and sets the region to
  `ScreenRegion.FromCorners(anchor, current)`. Moves that stay in the anchor cell before any drag
  do not start one, so pointer jitter never turns a click into a one-cell selection. Without an
  anchor it is ignored.
- `Release()`: clears the anchor and reports whether the sequence was a plain click (no drag) or a
  drag. A release without a press reports nothing.
- `DoubleClick(row, column, snapshot)`: sets the region to `snapshot.WordAt(row, column)`, which
  may be null.
- `Region` is the current region or null.

### 4.2 CellGeometry addition

`NearestCell(x, y, rows, columns)` maps any point, including points outside the grid, to the
nearest cell by clamping each axis. `HitTest` keeps returning null outside the grid. Pure math,
unit tested.

### 4.3 TerminalScreen changes

- `Selection`: a styled property of type `ScreenRegion?`, registered with two-way default binding
  and `AffectsRender`, so the window binds it directly to the view model.
- Pointer. With no snapshot, pointer input is ignored as today. A left press focuses the control,
  captures the pointer, and either calls
  `DoubleClick` when the click count is two or `Press` otherwise. Moves while captured hit-test
  with `NearestCell`, so dragging past the edge extends the selection to that edge, and update
  `Selection` from the gesture. Release ends the capture; a plain click raises `CellClicked` for
  the release cell, so cursor-on-click moves from press to release. Right and middle buttons do
  nothing.
- Render order: text runs, then one filled rectangle over the whole region in
  `Palette.Selection`, then the cursor, so the cursor stays crisp. `Palette.Selection` is a muted
  blue at roughly 40 percent opacity, defined once beside the existing brushes.
- Hotkeys. Before consulting `DefaultKeymap`, key-down reads the platform hotkey configuration
  from the control's top level and raises `CopyRequested`, `PasteRequested`, or
  `SelectAllRequested` when a gesture in the Copy, Paste, or Select All lists matches, marking the
  event handled. When the configuration is unavailable, Control with C, V, and A are the fallback.
  This gives Cmd on macOS and Ctrl elsewhere without platform code in LizTerm.
- Snapshot changes. When a new snapshot has a different row or column count, `Selection` is set
  to null. Same-size snapshots keep the selection; the region is coordinates, not content.

### 4.4 Clipboard abstraction

`ITextClipboard` in `src/LizTerm.App/Clipboard/`:

- `Task SetTextAsync(string text)`
- `Task<string?> GetTextAsync()` returning null when the clipboard holds no text.

`AvaloniaTextClipboard` wraps a `TopLevel` and resolves its `Clipboard` at call time through the
Avalonia 12 text extension methods (`SetTextAsync`, `TryGetTextAsync`). A missing clipboard reads
as empty and writes are dropped. Tests use a fake with a string field.

### 4.5 SessionViewModel changes

- Constructor gains `ITextClipboard clipboard` as a third argument.
- `Selection`: an observable `ScreenRegion?`, bound two-way to the control.
- `CopyCommand`: enabled when `Selection` and `Screen` are both non-null. Writes
  `Screen.GetText(Selection)` to the clipboard. The selection stays; copying is not host input.
- `PasteCommand`: enabled while `IsConnected`. Reads clipboard text; returns quietly on null or
  empty. Normalizes `\r\n` and bare `\r` to `\n`, sets `Selection` to null, then sends one
  `PasteTextAsync` call. b3270 treats a newline in a paste as a move to the next row at the paste
  margin, which gives block paste. No escaping is needed on this path because the backend
  hex-encodes the bytes.
- `SelectAllCommand`: enabled when `Screen` is non-null. Sets `Selection` to
  `ScreenRegion.Full(Screen.Rows, Screen.Columns)`.
- Lifetime rule in one place: `SendKeyAsync`, `TypeTextAsync`, the paste path, and
  `MoveCursorAsync` set `Selection` to null before sending.
- Command enablement follows the CommunityToolkit pattern: `IsConnected` and `Selection` notify the
  commands they gate.

### 4.6 SessionWindow and App changes

- Edit menu between File and Keys: Copy, Paste, separator, Select All, each bound to its command
  so enablement is automatic. The gesture text beside each item is taken from the platform hotkey
  configuration when the window opens.
- The control's three request events route to the three commands. `Selection` binds two-way.
- `App.OpenSession` creates the window first, then the view model with
  `new AvaloniaTextClipboard(window)`, then sets the data context. Nothing else in that method
  changes.

## 5. Error handling

- Clipboard read or write failure sets `ErrorMessage` to a plain "Could not read the clipboard" or
  "Could not copy" followed by the reason. Nothing fails silently.
- Paste while the keyboard is locked is rejected by b3270 and swallowed like any other rejected
  action; the status bar already shows the lock. Paste while disconnected cannot happen because the
  command is disabled.
- A double-click on a space selects nothing. Copy of a region that is all spaces writes empty
  lines, matching what the screen shows.
- Selection state is per window; two session windows never share one.

## 6. Testing

Core tests (plain facts):

- `ScreenRegion`: corners in any order normalize to one rectangle; `Contains` is inclusive on the
  edges; `Clamp` trims an overhanging region and returns null for one entirely outside; `Full`
  covers the grid.
- `GetText(region)`: trailing spaces trimmed per row, interior spaces kept, rows joined by `\n`
  with none at the end, overhanging region clamped, single-row region has no newline.
- `WordAt`: middle, first, and last cell of a word; a word touching the row edge; a dataset name
  with dots and parentheses kept whole; null on a space.

App pure tests (plain facts):

- `SelectionGesture`: press then release on one cell reports a click and no region; press, move,
  release reports a drag with the normalized region; moving back past the anchor flips the
  rectangle; a move without a press is ignored; a press clears an existing region.
- `CellGeometry.NearestCell`: inside points match `HitTest`; outside points clamp to the nearest
  edge cell.

Control tests (headless, `[AvaloniaFact]`):

- Mouse down, move, and up across cells sets `Selection` to the expected region and does not
  raise `CellClicked`.
- Down and up on one cell still raises `CellClicked`.
- Control with C, V, and A raise the three request events and reach neither the keymap nor text
  input.
- Replacing the snapshot with a different size clears the selection; the same size keeps it.
- Double-click selects the word when the headless platform reports a click count of two. If it
  does not, the test drives the gesture entry point directly and the real behavior is covered by
  the manual check below.

View model tests (plain facts with `FakeEmulatorSession` and a fake clipboard):

- Copy writes the trimmed region text and leaves the selection in place; disabled without a
  selection.
- Paste reads the clipboard, normalizes CRLF, records one `paste:` call, and clears the selection;
  disabled while disconnected; sends nothing when the clipboard is empty.
- Select All sets the full region for the current snapshot.
- Sending a key and typing text each clear the selection.
- A clipboard fake that throws produces a plain error message, not an unhandled exception.

Manual verification against the gateway (final task of the plan): drive the built app through the
Avalonia DevTools MCP with the isolated-HOME recipe from CLAUDE.md. Drag a selection on the login
screen and screenshot the overlay; copy it and read the clipboard back; paste two lines into the
User ID field with the demo account and confirm the second line lands at the paste margin; PF3
ends the session. The overlay's look and b3270's margin behavior are not asserted by automated
tests.

## 7. Out of scope

Cut; middle-click paste; keyboard-driven selection with Shift and arrows; Ctrl+Insert and
Shift+Insert; a selection that follows content across host updates; any change to the b3270
backend or to `IEmulatorSession`.
