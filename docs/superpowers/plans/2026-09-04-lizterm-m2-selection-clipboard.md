# LizTerm Milestone 2, Plan 1: Selection and Clipboard Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Mouse selection (drag for a rectangle, double-click for a word) on the session screen, with Copy, Paste, and Select All in a new Edit menu and on the platform's standard hotkeys, so a user can lift text off a 3270 screen and paste text onto one.

**Architecture:** Layered by responsibility. Core gains a `ScreenRegion` value type and two pure `ScreenSnapshot` methods (region text, word at a cell). App gains a pure `SelectionGesture` state machine, a `Selection` property plus overlay and hotkey recognition on `TerminalScreen`, an `ITextClipboard` abstraction with an Avalonia implementation and a test fake, and Copy/Paste/SelectAll commands on `SessionViewModel` that also apply the lifetime rule (any input sent to the host clears the selection). The b3270 backend is untouched: `PasteTextAsync` already exists.

**Tech Stack:** .NET 10, Avalonia 12.1.2 (`TopLevel.Clipboard` with `ClipboardExtensions.SetTextAsync`/`TryGetTextAsync`, `PlatformHotkeyConfiguration.Copy/Paste/SelectAll`, `KeyGesture.Matches`, `PointerPressedEventArgs.ClickCount`, `IPointer.Capture`), CommunityToolkit.Mvvm 8.4.2, xunit.v3 3.2.2 in VSTest mode, Avalonia.Headless.XUnit 12.1.2.

**Spec:** `docs/superpowers/specs/2026-09-04-lizterm-m2-selection-clipboard-design.md` (parent: `docs/superpowers/specs/2026-09-03-lizterm-v1-design.md` sections 6.2, 6.3, 6.5, 6.6)

## Global Constraints

- Rows and columns are zero-based everywhere in Core and App. `ScreenRegion` edges are inclusive.
- `LizTerm.Core` references only the BCL and never mentions Avalonia or b3270 names. `LizTerm.App` names `LizTerm.Backend.B3270` only in `src/LizTerm.App/SessionFactory.cs`.
- No new packages. Avalonia stays at `12.1.2`, CommunityToolkit.Mvvm at `8.4.2`; versions live only in `Directory.Packages.props`.
- Gestures: click-drag selects a rectangle; double-click selects a word (the maximal run of non-space cells on the row); no triple-click.
- Lifetime: a selection clears on any input sent to the host (key, typed text, paste, cursor move by click) and on any new mouse press or Select All. It survives host screen updates of the same size and clears when the screen size changes.
- Appearance: one translucent overlay, `Palette.Selection` = ARGB `0x66, 0x60, 0x90, 0xE0` (muted blue, 40 percent opacity), painted after the text runs and before the cursor.
- Copy text: one line per row, trailing U+0020 trimmed, rows joined by `\n`, no trailing newline. Paste normalizes `\r\n` and bare `\r` to `\n` and sends one `PasteTextAsync` call.
- Hotkeys come from the visual's platform settings, `this.GetPlatformSettings()?.HotkeyConfiguration` (extension method in `Avalonia.VisualTree.VisualExtensions`, resolved through the visual's top level; Avalonia 12 has no `TopLevel.PlatformSettings` property), giving Cmd on macOS and Ctrl elsewhere; the fallback when it is unavailable is Ctrl with C, V, A. The screen control raises events; it never touches the clipboard.
- Out of scope: Cut, middle-click paste, Shift+arrow selection, any `IEmulatorSession` or backend change.
- User-visible strings are asserted exactly in tests: `"Could not copy: "` and `"Could not read the clipboard: "` followed by the exception message.
- Deviation from the spec's file path: the gesture class lives in `src/LizTerm.App/Mouse/` (namespace `LizTerm.App.Mouse`, parallel to the existing `LizTerm.App.Keyboard`) rather than `Selection/`, so no namespace shares the name of the control's `Selection` property.
- Run tests with `dotnet test <project> --filter "FullyQualifiedName~<Class>"`; run the full suite with `dotnet test LizTerm.slnx` before every commit. If a `LizTerm.Backend.B3270.Tests` test fails once under load, rerun before investigating (a known flake).
- Commit after every task with the message shown; use `Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>` as the last line of each commit message.

---

## File Structure

```
src/LizTerm.Core/Screen/ScreenRegion.cs                 NEW  inclusive zero-based rectangle, always normalized
src/LizTerm.Core/Screen/ScreenSnapshot.cs               MOD  GetText(ScreenRegion), WordAt(row, column)
src/LizTerm.App/Rendering/CellGeometry.cs               MOD  NearestCell (clamping hit test)
src/LizTerm.App/Rendering/Palette.cs                    MOD  Selection overlay brush
src/LizTerm.App/Mouse/SelectionGesture.cs               NEW  pure press/move/release/double-click state machine
src/LizTerm.App/Controls/TerminalScreen.cs              MOD  Selection property, pointer capture and drag, overlay, hotkey events
src/LizTerm.App/Clipboard/ITextClipboard.cs             NEW  two-method text clipboard interface
src/LizTerm.App/Clipboard/AvaloniaTextClipboard.cs      NEW  wraps TopLevel.Clipboard
src/LizTerm.App/ViewModels/SessionViewModel.cs          MOD  Selection, Copy/Paste/SelectAll commands, clear on host input
src/LizTerm.App/Views/SessionWindow.axaml(.cs)          MOD  Edit menu, event wiring, platform gesture text
src/LizTerm.App/App.axaml.cs                            MOD  window before view model; inject AvaloniaTextClipboard
tests/LizTerm.Core.Tests/Screen/ScreenRegionTests.cs    NEW
tests/LizTerm.Core.Tests/Screen/ScreenSnapshotTests.cs  MOD  region text and word tests
tests/LizTerm.App.Tests/Rendering/CellGeometryTests.cs  MOD  NearestCell test
tests/LizTerm.App.Tests/Mouse/SelectionGestureTests.cs  NEW
tests/LizTerm.App.Tests/Controls/TerminalScreenSelectionTests.cs  NEW  headless drag, click, double-click, hotkeys, size change
tests/LizTerm.App.Tests/Fakes/FakeTextClipboard.cs      NEW
tests/LizTerm.App.Tests/ViewModels/SessionViewModelTests.cs          MOD  Create() passes a fake clipboard
tests/LizTerm.App.Tests/ViewModels/SessionViewModelClipboardTests.cs NEW
CLAUDE.md                                               MOD  architecture notes for selection and clipboard
```

---

### Task 1: ScreenRegion value type (Core)

**Files:**
- Create: `src/LizTerm.Core/Screen/ScreenRegion.cs`
- Test: `tests/LizTerm.Core.Tests/Screen/ScreenRegionTests.cs`

**Interfaces:**
- Produces: `readonly record struct ScreenRegion` in `LizTerm.Core.Screen` with `int Top`, `int Left`, `int Bottom`, `int Right` (inclusive, zero-based, always `Top <= Bottom` and `Left <= Right`), `int Rows`, `int Columns`, `static ScreenRegion FromCorners(int row1, int column1, int row2, int column2)`, `static ScreenRegion Full(int rows, int columns)` (throws `ArgumentOutOfRangeException` when either is not positive), `bool Contains(int row, int column)`, `ScreenRegion? Clamp(int rows, int columns)` (null when nothing remains). Every later task uses `FromCorners` and `Full`.

- [ ] **Step 1: Write the failing tests**

`tests/LizTerm.Core.Tests/Screen/ScreenRegionTests.cs`:

```csharp
using LizTerm.Core.Screen;

namespace LizTerm.Core.Tests.Screen;

public class ScreenRegionTests
{
    [Fact]
    public void FromCorners_normalizes_any_corner_order()
    {
        var expected = ScreenRegion.FromCorners(2, 3, 5, 9);
        Assert.Equal(expected, ScreenRegion.FromCorners(5, 9, 2, 3));
        Assert.Equal(expected, ScreenRegion.FromCorners(2, 9, 5, 3));
        Assert.Equal(expected, ScreenRegion.FromCorners(5, 3, 2, 9));
        Assert.Equal((2, 3, 5, 9), (expected.Top, expected.Left, expected.Bottom, expected.Right));
        Assert.Equal(4, expected.Rows);
        Assert.Equal(7, expected.Columns);
    }

    [Fact]
    public void Contains_is_inclusive_on_all_edges()
    {
        var region = ScreenRegion.FromCorners(2, 3, 5, 9);
        Assert.True(region.Contains(2, 3));
        Assert.True(region.Contains(5, 9));
        Assert.True(region.Contains(3, 6));
        Assert.False(region.Contains(1, 3));
        Assert.False(region.Contains(6, 9));
        Assert.False(region.Contains(2, 2));
        Assert.False(region.Contains(5, 10));
    }

    [Fact]
    public void Clamp_trims_an_overhanging_region()
    {
        Assert.Equal(ScreenRegion.Full(24, 80), ScreenRegion.FromCorners(-2, -1, 30, 100).Clamp(24, 80));
        Assert.Equal(ScreenRegion.FromCorners(20, 70, 23, 79), ScreenRegion.FromCorners(20, 70, 40, 90).Clamp(24, 80));
        Assert.Equal(ScreenRegion.FromCorners(1, 1, 2, 2), ScreenRegion.FromCorners(1, 1, 2, 2).Clamp(24, 80));
    }

    [Fact]
    public void Clamp_returns_null_when_nothing_remains()
    {
        Assert.Null(ScreenRegion.FromCorners(24, 0, 30, 10).Clamp(24, 80));
        Assert.Null(ScreenRegion.FromCorners(0, 80, 5, 90).Clamp(24, 80));
    }

    [Fact]
    public void Full_covers_the_grid()
    {
        var full = ScreenRegion.Full(24, 80);
        Assert.Equal((0, 0, 23, 79), (full.Top, full.Left, full.Bottom, full.Right));
        Assert.Throws<ArgumentOutOfRangeException>(() => ScreenRegion.Full(0, 80));
        Assert.Throws<ArgumentOutOfRangeException>(() => ScreenRegion.Full(24, 0));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~ScreenRegionTests"`
Expected: build error, `ScreenRegion` does not exist.

- [ ] **Step 3: Write the implementation**

`src/LizTerm.Core/Screen/ScreenRegion.cs`:

```csharp
namespace LizTerm.Core.Screen;

/// <summary>An inclusive, zero-based rectangle of cells. Always normalized: Top &lt;= Bottom and Left &lt;= Right.
/// Coordinates, not content: after a host update the same region may cover different text.</summary>
public readonly record struct ScreenRegion
{
    public int Top { get; }
    public int Left { get; }
    public int Bottom { get; }
    public int Right { get; }

    private ScreenRegion(int top, int left, int bottom, int right)
    {
        Top = top;
        Left = left;
        Bottom = bottom;
        Right = right;
    }

    /// <summary>Builds the rectangle spanning two corners given in any order.</summary>
    public static ScreenRegion FromCorners(int row1, int column1, int row2, int column2) =>
        new(Math.Min(row1, row2), Math.Min(column1, column2), Math.Max(row1, row2), Math.Max(column1, column2));

    /// <summary>The whole rows x columns grid.</summary>
    public static ScreenRegion Full(int rows, int columns)
    {
        if (rows <= 0) throw new ArgumentOutOfRangeException(nameof(rows));
        if (columns <= 0) throw new ArgumentOutOfRangeException(nameof(columns));
        return new ScreenRegion(0, 0, rows - 1, columns - 1);
    }

    public int Rows => Bottom - Top + 1;
    public int Columns => Right - Left + 1;

    public bool Contains(int row, int column) =>
        row >= Top && row <= Bottom && column >= Left && column <= Right;

    /// <summary>Trims the region to a rows x columns grid; null when nothing remains.</summary>
    public ScreenRegion? Clamp(int rows, int columns)
    {
        var top = Math.Max(Top, 0);
        var left = Math.Max(Left, 0);
        var bottom = Math.Min(Bottom, rows - 1);
        var right = Math.Min(Right, columns - 1);
        if (top > bottom || left > right) return null;
        return new ScreenRegion(top, left, bottom, right);
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~ScreenRegionTests"`
Expected: 5 passed.

- [ ] **Step 5: Commit**

```bash
git add src/LizTerm.Core/Screen/ScreenRegion.cs tests/LizTerm.Core.Tests/Screen/ScreenRegionTests.cs
git commit -m "Add ScreenRegion, a normalized inclusive cell rectangle

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 2: Region text and word lookup on ScreenSnapshot (Core)

**Files:**
- Modify: `src/LizTerm.Core/Screen/ScreenSnapshot.cs` (add two methods after `ToText()`)
- Test: `tests/LizTerm.Core.Tests/Screen/ScreenSnapshotTests.cs` (append)

**Interfaces:**
- Consumes: `ScreenRegion` from Task 1; existing `ScreenSnapshot.GetText(int row, int column, int length)`, `Cell.Space`.
- Produces: `string ScreenSnapshot.GetText(ScreenRegion region)` and `ScreenRegion? ScreenSnapshot.WordAt(int row, int column)`. Task 4 uses `WordAt`; Task 8 uses `GetText(ScreenRegion)`.

- [ ] **Step 1: Write the failing tests**

Append to `tests/LizTerm.Core.Tests/Screen/ScreenSnapshotTests.cs`, inside the class:

```csharp
    /// <summary>A 32-column screen with the given rows written from column 0.</summary>
    private static ScreenSnapshot Screen(params string[] rows)
    {
        var buffer = new ScreenBuffer(rows.Length, 32);
        for (var r = 0; r < rows.Length; r++) buffer.SetText(r, 0, rows[r], null, null, null);
        return buffer.Snapshot();
    }

    [Fact]
    public void GetText_region_trims_trailing_spaces_and_joins_rows_with_newline()
    {
        var snap = Screen("ab  cd    ", "  ef      ", "          ");
        Assert.Equal("ab  cd\n  ef\n", snap.GetText(ScreenRegion.FromCorners(0, 0, 2, 9)));
    }

    [Fact]
    public void GetText_region_keeps_interior_spaces_and_a_single_row_has_no_newline()
    {
        var snap = Screen("SYS1.PROCLIB  JOB01234 ");
        Assert.Equal("1.PROCLIB  JOB", snap.GetText(ScreenRegion.FromCorners(0, 3, 0, 16)));
    }

    [Fact]
    public void GetText_region_is_clamped_to_the_snapshot()
    {
        var snap = Screen("abc", "def");
        Assert.Equal("bc\nef", snap.GetText(ScreenRegion.FromCorners(0, 1, 5, 40)));
        Assert.Equal("", snap.GetText(ScreenRegion.FromCorners(7, 0, 9, 3)));
    }

    [Fact]
    public void WordAt_returns_the_run_of_non_space_cells()
    {
        var snap = Screen("  SYS1.PROCLIB(IEFBR14) JOB01234");
        var word = ScreenRegion.FromCorners(0, 2, 0, 22);
        Assert.Equal(word, snap.WordAt(0, 7));    // middle
        Assert.Equal(word, snap.WordAt(0, 2));    // first cell
        Assert.Equal(word, snap.WordAt(0, 22));   // last cell
        Assert.Null(snap.WordAt(0, 23));          // the space between the words
        Assert.Null(snap.WordAt(0, 0));
    }

    [Fact]
    public void WordAt_stops_at_the_row_edges_and_rejects_cells_off_the_screen()
    {
        var snap = Screen("  SYS1.PROCLIB(IEFBR14) JOB01234");   // JOB01234 ends in the last column (31)
        Assert.Equal(ScreenRegion.FromCorners(0, 24, 0, 31), snap.WordAt(0, 26));
        var left = Screen("abc def");
        Assert.Equal(ScreenRegion.FromCorners(0, 0, 0, 2), left.WordAt(0, 1));
        Assert.Null(snap.WordAt(5, 0));
        Assert.Null(snap.WordAt(0, 32));
        Assert.Null(snap.WordAt(-1, 0));
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~ScreenSnapshotTests"`
Expected: build error, no overload of `GetText` takes a `ScreenRegion`, `WordAt` does not exist.

- [ ] **Step 3: Write the implementation**

Add to `src/LizTerm.Core/Screen/ScreenSnapshot.cs` after `ToText()`:

```csharp
    /// <summary>Text of a rectangular region: one line per row, trailing spaces trimmed, rows joined by '\n'
    /// with no trailing newline. The region is clamped to the screen; nothing left means "".</summary>
    public string GetText(ScreenRegion region)
    {
        if (region.Clamp(Rows, Columns) is not { } r) return "";
        var lines = new string[r.Rows];
        for (var row = r.Top; row <= r.Bottom; row++)
            lines[row - r.Top] = GetText(row, r.Left, r.Columns).TrimEnd(' ');
        return string.Join('\n', lines);
    }

    /// <summary>The maximal run of non-space cells on the row containing the cell, or null when the cell is a
    /// space or off the screen. Non-space is the whole rule, so SYS1.PROCLIB(IEFBR14) is one word.</summary>
    public ScreenRegion? WordAt(int row, int column)
    {
        if ((uint)row >= (uint)Rows || (uint)column >= (uint)Columns) return null;
        if (this[row, column].Character == Cell.Space) return null;
        var left = column;
        while (left > 0 && this[row, left - 1].Character != Cell.Space) left--;
        var right = column;
        while (right < Columns - 1 && this[row, right + 1].Character != Cell.Space) right++;
        return ScreenRegion.FromCorners(row, left, row, right);
    }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~ScreenSnapshotTests"`
Expected: 7 passed (2 existing + 5 new).

- [ ] **Step 5: Commit**

```bash
git add src/LizTerm.Core/Screen/ScreenSnapshot.cs tests/LizTerm.Core.Tests/Screen/ScreenSnapshotTests.cs
git commit -m "Add region text extraction and word lookup to ScreenSnapshot

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 3: NearestCell hit test and the selection brush (App rendering)

**Files:**
- Modify: `src/LizTerm.App/Rendering/CellGeometry.cs` (add one method after `HitTest`)
- Modify: `src/LizTerm.App/Rendering/Palette.cs` (add one field after `Background`)
- Test: `tests/LizTerm.App.Tests/Rendering/CellGeometryTests.cs` (append)

**Interfaces:**
- Produces: `(int Row, int Column)? CellGeometry.NearestCell(double x, double y, int rows, int columns)` (null only when the geometry is default or the grid is empty) and `static readonly IBrush Palette.Selection`. Task 5 uses both.

- [ ] **Step 1: Write the failing test**

Append to `tests/LizTerm.App.Tests/Rendering/CellGeometryTests.cs`, inside the class:

```csharp
    [Fact]
    public void NearestCell_matches_HitTest_inside_and_clamps_to_the_edge_outside()
    {
        var g = CellGeometry.Fit(800, 600, 24, 80, Advance, Line);
        Assert.Equal((3, 5), g.NearestCell(16 + 9.6 * 5 + 1, 69.6 + 19.2 * 3 + 1, 24, 80));
        Assert.Equal((11, 0), g.NearestCell(2, 69.6 + 19.2 * 11 + 5, 24, 80));   // left margin -> column 0
        Assert.Equal((0, 39), g.NearestCell(16 + 9.6 * 39 + 3, 5, 24, 80));      // top margin -> row 0
        Assert.Equal((23, 79), g.NearestCell(799, 599, 24, 80));                 // bottom-right margin
        Assert.Equal((0, 0), g.NearestCell(-50, -50, 24, 80));
        Assert.Null(default(CellGeometry).NearestCell(1, 1, 24, 80));
        Assert.Null(g.NearestCell(1, 1, 0, 80));
    }
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~CellGeometryTests"`
Expected: build error, `NearestCell` does not exist.

- [ ] **Step 3: Write the implementation**

Add to `src/LizTerm.App/Rendering/CellGeometry.cs` after `HitTest`:

```csharp
    /// <summary>Like <see cref="HitTest"/>, but a point outside the grid clamps to the nearest edge cell, so a drag
    /// that leaves the screen extends the selection to the edge. Null only without geometry or with an empty grid.</summary>
    public (int Row, int Column)? NearestCell(double x, double y, int rows, int columns)
    {
        if (CellWidth <= 0 || CellHeight <= 0 || rows <= 0 || columns <= 0) return null;
        var column = (int)Math.Floor((x - OriginX) / CellWidth);
        var row = (int)Math.Floor((y - OriginY) / CellHeight);
        return (Math.Clamp(row, 0, rows - 1), Math.Clamp(column, 0, columns - 1));
    }
```

Add to `src/LizTerm.App/Rendering/Palette.cs` directly after the `Background` field:

```csharp
    /// <summary>Overlay for the mouse selection: muted blue at 40% opacity so host colors stay readable under it.</summary>
    public static readonly IBrush Selection = new ImmutableSolidColorBrush(Color.FromArgb(0x66, 0x60, 0x90, 0xE0));
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~CellGeometryTests"`
Expected: 7 passed.

- [ ] **Step 5: Commit**

```bash
git add src/LizTerm.App/Rendering/CellGeometry.cs src/LizTerm.App/Rendering/Palette.cs tests/LizTerm.App.Tests/Rendering/CellGeometryTests.cs
git commit -m "Add a clamping NearestCell hit test and the selection overlay brush

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 4: SelectionGesture state machine (App, pure)

**Files:**
- Create: `src/LizTerm.App/Mouse/SelectionGesture.cs`
- Test: `tests/LizTerm.App.Tests/Mouse/SelectionGestureTests.cs`

**Interfaces:**
- Consumes: `ScreenRegion.FromCorners`, `ScreenSnapshot.WordAt`.
- Produces, in namespace `LizTerm.App.Mouse`: `enum ReleaseResult { None, Click, Drag }` and `sealed class SelectionGesture` with `ScreenRegion? Region { get; }`, `void Press(int row, int column)`, `void Move(int row, int column)`, `ReleaseResult Release()`, `void DoubleClick(int row, int column, ScreenSnapshot snapshot)`. Task 5 drives it from pointer events.

- [ ] **Step 1: Write the failing tests**

`tests/LizTerm.App.Tests/Mouse/SelectionGestureTests.cs`:

```csharp
using LizTerm.App.Mouse;
using LizTerm.Core.Screen;

namespace LizTerm.App.Tests.Mouse;

public class SelectionGestureTests
{
    [Fact]
    public void Press_and_release_on_one_cell_is_a_click_with_no_region()
    {
        var g = new SelectionGesture();
        g.Press(3, 4);
        Assert.Equal(ReleaseResult.Click, g.Release());
        Assert.Null(g.Region);
    }

    [Fact]
    public void Press_move_release_is_a_drag_with_a_normalized_region()
    {
        var g = new SelectionGesture();
        g.Press(5, 10);
        g.Move(5, 12);
        g.Move(2, 3);
        Assert.Equal(ScreenRegion.FromCorners(2, 3, 5, 10), g.Region);
        Assert.Equal(ReleaseResult.Drag, g.Release());
        Assert.Equal(ScreenRegion.FromCorners(2, 3, 5, 10), g.Region);
    }

    [Fact]
    public void Moving_back_to_the_anchor_after_a_drag_keeps_a_one_cell_region()
    {
        var g = new SelectionGesture();
        g.Press(1, 1);
        g.Move(1, 5);
        g.Move(1, 1);
        Assert.Equal(ScreenRegion.FromCorners(1, 1, 1, 1), g.Region);
        Assert.Equal(ReleaseResult.Drag, g.Release());
    }

    [Fact]
    public void Jitter_inside_the_anchor_cell_is_still_a_click()
    {
        var g = new SelectionGesture();
        g.Press(1, 1);
        g.Move(1, 1);
        Assert.Null(g.Region);
        Assert.Equal(ReleaseResult.Click, g.Release());
    }

    [Fact]
    public void Move_and_release_without_a_press_are_ignored()
    {
        var g = new SelectionGesture();
        g.Move(2, 2);
        Assert.Null(g.Region);
        Assert.Equal(ReleaseResult.None, g.Release());
    }

    [Fact]
    public void Press_clears_an_existing_region()
    {
        var g = new SelectionGesture();
        g.Press(0, 0);
        g.Move(2, 2);
        g.Release();
        g.Press(9, 9);
        Assert.Null(g.Region);
    }

    [Fact]
    public void DoubleClick_selects_the_word_or_nothing_and_is_not_a_click()
    {
        var buffer = new ScreenBuffer(2, 20);
        buffer.SetText(1, 3, "hello world", null, null, null);
        var snap = buffer.Snapshot();
        var g = new SelectionGesture();

        g.DoubleClick(1, 5, snap);
        Assert.Equal(ScreenRegion.FromCorners(1, 3, 1, 7), g.Region);
        Assert.Equal(ReleaseResult.None, g.Release());

        g.DoubleClick(1, 8, snap);
        Assert.Null(g.Region);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~SelectionGestureTests"`
Expected: build error, namespace `LizTerm.App.Mouse` does not exist.

- [ ] **Step 3: Write the implementation**

`src/LizTerm.App/Mouse/SelectionGesture.cs`:

```csharp
using LizTerm.Core.Screen;

namespace LizTerm.App.Mouse;

/// <summary>What a button release meant: nothing (no press seen), a plain click, or the end of a drag.</summary>
public enum ReleaseResult { None, Click, Drag }

/// <summary>Turns cell-level pointer events into a selection rectangle. Knows nothing about pixels or Avalonia;
/// the screen control hit-tests pointer positions to cells and feeds them here.</summary>
public sealed class SelectionGesture
{
    private (int Row, int Column)? _anchor;
    private bool _dragging;

    /// <summary>The current selection, or null.</summary>
    public ScreenRegion? Region { get; private set; }

    /// <summary>Left button pressed on a cell: becomes the anchor; any existing selection is dropped.</summary>
    public void Press(int row, int column)
    {
        _anchor = (row, column);
        _dragging = false;
        Region = null;
    }

    /// <summary>Pointer moved with the button held. A drag starts once the pointer leaves the anchor cell, so
    /// jitter inside that cell keeps a click a click.</summary>
    public void Move(int row, int column)
    {
        if (_anchor is not { } anchor) return;
        if (!_dragging && anchor == (row, column)) return;
        _dragging = true;
        Region = ScreenRegion.FromCorners(anchor.Row, anchor.Column, row, column);
    }

    /// <summary>Button released. The region is left as it is; the caller moves the cursor on a Click.</summary>
    public ReleaseResult Release()
    {
        if (_anchor is null) return ReleaseResult.None;
        var result = _dragging ? ReleaseResult.Drag : ReleaseResult.Click;
        _anchor = null;
        _dragging = false;
        return result;
    }

    /// <summary>Second press of a double-click: selects the word under the cell, or nothing on a space.
    /// Ends any press sequence so the following release is not a click.</summary>
    public void DoubleClick(int row, int column, ScreenSnapshot snapshot)
    {
        _anchor = null;
        _dragging = false;
        Region = snapshot.WordAt(row, column);
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~SelectionGestureTests"`
Expected: 7 passed.

- [ ] **Step 5: Commit**

```bash
git add src/LizTerm.App/Mouse/SelectionGesture.cs tests/LizTerm.App.Tests/Mouse/SelectionGestureTests.cs
git commit -m "Add the pure SelectionGesture state machine

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 5: Selection on TerminalScreen: property, drag, double-click, overlay

**Files:**
- Modify: `src/LizTerm.App/Controls/TerminalScreen.cs`
- Test: `tests/LizTerm.App.Tests/Controls/TerminalScreenSelectionTests.cs` (new file)

**Interfaces:**
- Consumes: `SelectionGesture`, `ReleaseResult` (Task 4); `CellGeometry.NearestCell`, `Palette.Selection` (Task 3); `ScreenRegion` (Task 1).
- Produces: `StyledProperty<ScreenRegion?> TerminalScreen.SelectionProperty` with CLR property `ScreenRegion? Selection { get; set; }` (default binding mode TwoWay), and `internal void PressAt((int Row, int Column) cell, int clickCount)` used by the double-click test fallback. Existing `CellClicked` now fires on release of a plain click. Task 9 binds `Selection`.

- [ ] **Step 1: Write the failing tests**

`tests/LizTerm.App.Tests/Controls/TerminalScreenSelectionTests.cs`:

```csharp
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using LizTerm.App.Controls;
using LizTerm.Core.Screen;

namespace LizTerm.App.Tests.Controls;

public class TerminalScreenSelectionTests
{
    private static (Window Window, TerminalScreen Screen) Show(ScreenSnapshot? snapshot = null)
    {
        var screen = new TerminalScreen { Snapshot = snapshot ?? ScreenSnapshot.Empty(24, 80) };
        var window = new Window { Width = 800, Height = 600, Content = screen };
        window.Show();
        screen.Focus();
        return (window, screen);
    }

    private static Avalonia.Point Center(TerminalScreen screen, int row, int column)
    {
        var rect = screen.LastGeometry.CellRect(row, column);
        return new Avalonia.Point(rect.Center.X, rect.Center.Y);
    }

    [AvaloniaFact]
    public void Drag_sets_a_normalized_selection_and_does_not_click()
    {
        var (window, screen) = Show();
        var clicked = false;
        screen.CellClicked += (_, _) => clicked = true;

        window.MouseDown(Center(screen, 5, 10), MouseButton.Left);
        window.MouseMove(Center(screen, 2, 3));
        window.MouseUp(Center(screen, 2, 3), MouseButton.Left);

        Assert.Equal(ScreenRegion.FromCorners(2, 3, 5, 10), screen.Selection);
        Assert.False(clicked);
    }

    [AvaloniaFact]
    public void Drag_past_the_edge_extends_the_selection_to_the_edge()
    {
        var (window, screen) = Show();
        window.MouseDown(Center(screen, 20, 70), MouseButton.Left);
        window.MouseMove(new Avalonia.Point(799, 599));
        window.MouseUp(new Avalonia.Point(799, 599), MouseButton.Left);
        Assert.Equal(ScreenRegion.FromCorners(20, 70, 23, 79), screen.Selection);
    }

    [AvaloniaFact]
    public void Click_moves_the_cursor_on_release_and_clears_the_selection()
    {
        var (window, screen) = Show();
        screen.Selection = ScreenRegion.FromCorners(0, 0, 1, 1);
        (int Row, int Column)? clicked = null;
        screen.CellClicked += (_, c) => clicked = c;

        window.MouseDown(Center(screen, 5, 12), MouseButton.Left);
        Assert.Null(clicked);
        window.MouseUp(Center(screen, 5, 12), MouseButton.Left);

        Assert.Equal((5, 12), clicked);
        Assert.Null(screen.Selection);
    }

    [AvaloniaFact]
    public void Double_click_selects_the_word_under_the_pointer()
    {
        var buffer = new ScreenBuffer(24, 80);
        buffer.SetText(3, 10, "SYS1.PROCLIB", null, null, null);
        var (window, screen) = Show(buffer.Snapshot());
        var point = Center(screen, 3, 14);

        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);

        Assert.Equal(ScreenRegion.FromCorners(3, 10, 3, 21), screen.Selection);
    }

    [AvaloniaFact]
    public void Snapshot_of_a_different_size_clears_the_selection_and_same_size_keeps_it()
    {
        var (_, screen) = Show();
        screen.Selection = ScreenRegion.FromCorners(1, 1, 2, 2);
        screen.Snapshot = ScreenSnapshot.Empty(24, 80);
        Assert.Equal(ScreenRegion.FromCorners(1, 1, 2, 2), screen.Selection);
        screen.Snapshot = ScreenSnapshot.Empty(43, 80);
        Assert.Null(screen.Selection);
    }

    [AvaloniaFact]
    public void Right_button_neither_selects_nor_clicks()
    {
        var (window, screen) = Show();
        var clicked = false;
        screen.CellClicked += (_, _) => clicked = true;
        window.MouseDown(Center(screen, 1, 1), MouseButton.Right);
        window.MouseMove(Center(screen, 4, 4));
        window.MouseUp(Center(screen, 4, 4), MouseButton.Right);
        Assert.Null(screen.Selection);
        Assert.False(clicked);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~TerminalScreenSelectionTests"`
Expected: build error, `TerminalScreen` has no `Selection` member.

- [ ] **Step 3: Write the implementation**

Edit `src/LizTerm.App/Controls/TerminalScreen.cs`.

Add usings at the top (keep the existing ones):

```csharp
using Avalonia.Data;
using LizTerm.App.Mouse;
```

Add the property after `DestructiveBackspaceProperty`:

```csharp
    /// <summary>The mouse selection, or null. Two-way by default so the window can bind it to the view model,
    /// which clears it whenever input is sent to the host.</summary>
    public static readonly StyledProperty<ScreenRegion?> SelectionProperty =
        AvaloniaProperty.Register<TerminalScreen, ScreenRegion?>(nameof(Selection), defaultBindingMode: BindingMode.TwoWay);
```

Add a field beside `_typeface`:

```csharp
    private readonly SelectionGesture _gesture = new();
```

Change the static constructor's first line so both properties invalidate rendering:

```csharp
        AffectsRender<TerminalScreen>(SnapshotProperty, SelectionProperty);
```

Add the CLR property after `DestructiveBackspace`:

```csharp
    public ScreenRegion? Selection
    {
        get => GetValue(SelectionProperty);
        set => SetValue(SelectionProperty, value);
    }
```

Replace the whole `OnPointerPressed` override with these three overrides plus the internal helper:

```csharp
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        var snapshot = Snapshot;
        if (snapshot is null) return;
        var position = e.GetPosition(this);
        if (LastGeometry.HitTest(position.X, position.Y, snapshot.Rows, snapshot.Columns) is not { } cell) return;
        PressAt(cell, e.ClickCount);
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    /// <summary>The cell-level half of a press, split out so tests can drive a double-click directly if the
    /// headless platform does not report click counts.</summary>
    internal void PressAt((int Row, int Column) cell, int clickCount)
    {
        var snapshot = Snapshot;
        if (snapshot is null) return;
        if (clickCount == 2)
            _gesture.DoubleClick(cell.Row, cell.Column, snapshot);
        else
            _gesture.Press(cell.Row, cell.Column);
        Selection = _gesture.Region;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!ReferenceEquals(e.Pointer.Captured, this)) return;
        var snapshot = Snapshot;
        if (snapshot is null) return;
        var position = e.GetPosition(this);
        if (LastGeometry.NearestCell(position.X, position.Y, snapshot.Rows, snapshot.Columns) is not { } cell) return;
        _gesture.Move(cell.Row, cell.Column);
        Selection = _gesture.Region;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (e.InitialPressMouseButton != MouseButton.Left) return;
        if (ReferenceEquals(e.Pointer.Captured, this)) e.Pointer.Capture(null);
        if (_gesture.Release() != ReleaseResult.Click) return;
        var snapshot = Snapshot;
        if (snapshot is null) return;
        var position = e.GetPosition(this);
        if (LastGeometry.NearestCell(position.X, position.Y, snapshot.Rows, snapshot.Columns) is { } cell)
        {
            CellClicked?.Invoke(this, cell);
            e.Handled = true;
        }
    }

    /// <summary>A screen of a different size makes the old coordinates meaningless; same size keeps them.</summary>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != SnapshotProperty || Selection is null) return;
        var (oldValue, newValue) = change.GetOldAndNewValue<ScreenSnapshot?>();
        if (oldValue is null || newValue is null || oldValue.Rows != newValue.Rows || oldValue.Columns != newValue.Columns)
            Selection = null;
    }
```

In `Render`, replace the single line `DrawCursor(context, snapshot, g);` with:

```csharp
        DrawSelection(context, snapshot, g);
        DrawCursor(context, snapshot, g);
```

Add the overlay method before `DrawCursor`:

```csharp
    private void DrawSelection(DrawingContext context, ScreenSnapshot snapshot, CellGeometry g)
    {
        if (Selection?.Clamp(snapshot.Rows, snapshot.Columns) is not { } region) return;
        var topLeft = g.CellRect(region.Top, region.Left);
        var bottomRight = g.CellRect(region.Bottom, region.Right);
        context.FillRectangle(Palette.Selection, new Rect(topLeft.TopLeft, bottomRight.BottomRight));
    }
```

- [ ] **Step 4: Run the control tests, old and new**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~TerminalScreen"`
Expected: all pass, including the existing `Click_raises_CellClicked_with_zero_based_cell` (it presses and releases, so the click now fires on release).

If only `Double_click_selects_the_word_under_the_pointer` fails with `Selection` null, the headless platform did not report a click count of two. Then replace that test's four mouse lines with `screen.PressAt((3, 14), clickCount: 2);`, rename it `Double_click_selects_the_word_under_the_pointer_via_PressAt`, and add the comment `// Headless MouseDown does not report ClickCount 2; the real path is checked manually in Task 10.` Rerun until green.

- [ ] **Step 5: Run the full suite and commit**

Run: `dotnet test LizTerm.slnx`
Expected: everything green (the integration project reports 1 skipped).

```bash
git add src/LizTerm.App/Controls/TerminalScreen.cs tests/LizTerm.App.Tests/Controls/TerminalScreenSelectionTests.cs
git commit -m "Add drag and double-click selection with an overlay to TerminalScreen

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 6: Clipboard hotkeys on TerminalScreen

**Files:**
- Modify: `src/LizTerm.App/Controls/TerminalScreen.cs`
- Test: `tests/LizTerm.App.Tests/Controls/TerminalScreenSelectionTests.cs` (append one test)

**Interfaces:**
- Produces: `event EventHandler? CopyRequested`, `event EventHandler? PasteRequested`, `event EventHandler? SelectAllRequested` on `TerminalScreen`. Task 9 routes them to commands.

- [ ] **Step 1: Write the failing test**

Append inside `TerminalScreenSelectionTests`:

```csharp
    [AvaloniaFact]
    public void Clipboard_hotkeys_raise_requests_and_bypass_the_keymap()
    {
        var (window, screen) = Show();
        var events = new List<string>();
        screen.CopyRequested += (_, _) => events.Add("copy");
        screen.PasteRequested += (_, _) => events.Add("paste");
        screen.SelectAllRequested += (_, _) => events.Add("select-all");
        screen.KeyRequested += (_, k) => events.Add("key:" + k);
        screen.TextEntered += (_, t) => events.Add("text:" + t);

        window.KeyPressQwerty(PhysicalKey.C, RawInputModifiers.Control);
        window.KeyPressQwerty(PhysicalKey.V, RawInputModifiers.Control);
        window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
        window.KeyPressQwerty(PhysicalKey.F3, RawInputModifiers.None);

        Assert.Equal(["copy", "paste", "select-all", "key:PF3"], events);
    }
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~Clipboard_hotkeys"`
Expected: build error, `CopyRequested` does not exist.

- [ ] **Step 3: Write the implementation**

In `src/LizTerm.App/Controls/TerminalScreen.cs`, add `using Avalonia.VisualTree;` to the usings, then add the events after `CellClicked`:

```csharp
    /// <summary>Raised for the platform's Copy, Paste, and Select All hotkeys. The control never touches the clipboard.</summary>
    public event EventHandler? CopyRequested;
    public event EventHandler? PasteRequested;
    public event EventHandler? SelectAllRequested;
```

Replace `OnKeyDown` with:

```csharp
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (TryHandleClipboardKey(e))
        {
            e.Handled = true;
            return;
        }
        if (DefaultKeymap.TryMap(e.Key, e.KeyModifiers, DestructiveBackspace, out var key))
        {
            KeyRequested?.Invoke(this, key);
            e.Handled = true;
            return;
        }
        base.OnKeyDown(e);
    }

    private bool TryHandleClipboardKey(KeyEventArgs e)
    {
        var hotkeys = this.GetPlatformSettings()?.HotkeyConfiguration;
        if (Matches(hotkeys?.Copy, e, Key.C)) { CopyRequested?.Invoke(this, EventArgs.Empty); return true; }
        if (Matches(hotkeys?.Paste, e, Key.V)) { PasteRequested?.Invoke(this, EventArgs.Empty); return true; }
        if (Matches(hotkeys?.SelectAll, e, Key.A)) { SelectAllRequested?.Invoke(this, EventArgs.Empty); return true; }
        return false;
    }

    /// <summary>The platform's gestures when available (Cmd on macOS, Ctrl elsewhere); Ctrl+key as the fallback.</summary>
    private static bool Matches(List<KeyGesture>? gestures, KeyEventArgs e, Key fallbackKey) =>
        gestures is { Count: > 0 }
            ? gestures.Any(gesture => gesture.Matches(e))
            : e.Key == fallbackKey && e.KeyModifiers == KeyModifiers.Control;
```

Note: on Windows the platform table also lists Ctrl+Insert for Copy and Shift+Insert for Paste, so those work there for free; Shift+Insert therefore no longer reaches the keymap's Insert mapping on Windows, which matches Windows convention.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~TerminalScreen"`
Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add src/LizTerm.App/Controls/TerminalScreen.cs tests/LizTerm.App.Tests/Controls/TerminalScreenSelectionTests.cs
git commit -m "Raise Copy, Paste, and Select All requests from the platform hotkeys

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 7: Clipboard abstraction and view model plumbing

**Files:**
- Create: `src/LizTerm.App/Clipboard/ITextClipboard.cs`, `src/LizTerm.App/Clipboard/AvaloniaTextClipboard.cs`, `tests/LizTerm.App.Tests/Fakes/FakeTextClipboard.cs`
- Modify: `src/LizTerm.App/ViewModels/SessionViewModel.cs` (constructor), `src/LizTerm.App/App.axaml.cs` (`OpenSession`), `tests/LizTerm.App.Tests/ViewModels/SessionViewModelTests.cs` (`Create`)

**Interfaces:**
- Produces: `interface ITextClipboard { Task SetTextAsync(string text); Task<string?> GetTextAsync(); }` in `LizTerm.App.Clipboard`; `sealed class AvaloniaTextClipboard(TopLevel topLevel) : ITextClipboard`; `sealed class FakeTextClipboard : ITextClipboard` with `string? Text { get; set; }` and `Exception? Exception { get; set; }`; `SessionViewModel(IEmulatorSession session, Action<Action> dispatch, ITextClipboard clipboard)`. Task 8 uses all of them.

- [ ] **Step 1: Write the interface, the Avalonia implementation, and the fake**

`src/LizTerm.App/Clipboard/ITextClipboard.cs`:

```csharp
namespace LizTerm.App.Clipboard;

/// <summary>Plain-text clipboard access, injected into the view model so tests can fake it.</summary>
public interface ITextClipboard
{
    Task SetTextAsync(string text);

    /// <summary>Null when the clipboard holds no text.</summary>
    Task<string?> GetTextAsync();
}
```

`src/LizTerm.App/Clipboard/AvaloniaTextClipboard.cs`:

```csharp
using Avalonia.Controls;
using Avalonia.Input.Platform;

namespace LizTerm.App.Clipboard;

/// <summary>Wraps a top level's clipboard, resolved at each call so the window need not be open yet.
/// A missing clipboard reads as empty and drops writes.</summary>
public sealed class AvaloniaTextClipboard(TopLevel topLevel) : ITextClipboard
{
    public Task SetTextAsync(string text) =>
        topLevel.Clipboard?.SetTextAsync(text) ?? Task.CompletedTask;

    public Task<string?> GetTextAsync() =>
        topLevel.Clipboard?.TryGetTextAsync() ?? Task.FromResult<string?>(null);
}
```

`tests/LizTerm.App.Tests/Fakes/FakeTextClipboard.cs`:

```csharp
using LizTerm.App.Clipboard;

namespace LizTerm.App.Tests.Fakes;

public sealed class FakeTextClipboard : ITextClipboard
{
    public string? Text { get; set; }

    /// <summary>When set, both operations fail with this exception.</summary>
    public Exception? Exception { get; set; }

    public Task SetTextAsync(string text)
    {
        if (Exception is not null) return Task.FromException(Exception);
        Text = text;
        return Task.CompletedTask;
    }

    public Task<string?> GetTextAsync() =>
        Exception is not null ? Task.FromException<string?>(Exception) : Task.FromResult(Text);
}
```

- [ ] **Step 2: Thread the clipboard through the view model constructor**

In `src/LizTerm.App/ViewModels/SessionViewModel.cs` add `using LizTerm.App.Clipboard;`, add the field after `_dispatch`:

```csharp
    private readonly ITextClipboard _clipboard;
```

and change the constructor signature and its first lines to:

```csharp
    /// <param name="dispatch">Marshals a callback onto the UI thread. Tests pass <c>a => a()</c>.</param>
    /// <param name="clipboard">Text clipboard; the app passes <see cref="AvaloniaTextClipboard"/>, tests a fake.</param>
    public SessionViewModel(IEmulatorSession session, Action<Action> dispatch, ITextClipboard clipboard)
    {
        _session = session;
        _dispatch = dispatch;
        _clipboard = clipboard;
```

In `src/LizTerm.App/App.axaml.cs` add `using LizTerm.App.Clipboard;` and replace the first two lines of `OpenSession` so the window exists before the view model:

```csharp
        var window = new SessionWindow();
        var viewModel = new SessionViewModel(SessionFactory.Create(profile), action => Dispatcher.UIThread.Post(action), new AvaloniaTextClipboard(window));
        window.DataContext = viewModel;
```

In `tests/LizTerm.App.Tests/ViewModels/SessionViewModelTests.cs` replace the `Create` helper with:

```csharp
    private static (SessionViewModel Vm, FakeEmulatorSession Session) Create()
    {
        var session = new FakeEmulatorSession();
        return (new SessionViewModel(session, action => action(), new FakeTextClipboard()), session);
    }
```

- [ ] **Step 3: Build and run the full suite**

Run: `dotnet test LizTerm.slnx`
Expected: everything green; no behavior changed yet.

- [ ] **Step 4: Commit**

```bash
git add src/LizTerm.App/Clipboard tests/LizTerm.App.Tests/Fakes/FakeTextClipboard.cs src/LizTerm.App/ViewModels/SessionViewModel.cs src/LizTerm.App/App.axaml.cs tests/LizTerm.App.Tests/ViewModels/SessionViewModelTests.cs
git commit -m "Add ITextClipboard with an Avalonia implementation and a test fake

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 8: Copy, Paste, Select All, and the lifetime rule in SessionViewModel

**Files:**
- Modify: `src/LizTerm.App/ViewModels/SessionViewModel.cs`
- Test: `tests/LizTerm.App.Tests/ViewModels/SessionViewModelClipboardTests.cs` (new file)

**Interfaces:**
- Consumes: `ITextClipboard`, `FakeTextClipboard` (Task 7); `ScreenRegion.Full`, `ScreenSnapshot.GetText(ScreenRegion)` (Tasks 1, 2); `FakeEmulatorSession` records `paste:<text>`.
- Produces on `SessionViewModel`: `ScreenRegion? Selection { get; set; }` (observable), `IAsyncRelayCommand CopyCommand`, `IAsyncRelayCommand PasteCommand`, `IRelayCommand SelectAllCommand`. Task 9 binds them.

- [ ] **Step 1: Write the failing tests**

`tests/LizTerm.App.Tests/ViewModels/SessionViewModelClipboardTests.cs`:

```csharp
using LizTerm.App.Tests.Fakes;
using LizTerm.App.ViewModels;
using LizTerm.Core.Screen;
using LizTerm.Core.Session;

namespace LizTerm.App.Tests.ViewModels;

public class SessionViewModelClipboardTests
{
    private static (SessionViewModel Vm, FakeEmulatorSession Session, FakeTextClipboard Clipboard) Create()
    {
        var session = new FakeEmulatorSession();
        var clipboard = new FakeTextClipboard();
        return (new SessionViewModel(session, action => action(), clipboard), session, clipboard);
    }

    private static ScreenSnapshot ScreenWith(string text, int row = 2, int column = 5)
    {
        var buffer = new ScreenBuffer(24, 80);
        buffer.SetText(row, column, text, null, null, null);
        return buffer.Snapshot();
    }

    [Fact]
    public async Task Copy_writes_the_trimmed_region_and_keeps_the_selection()
    {
        var (vm, session, clipboard) = Create();
        session.RaiseScreen(ScreenWith("hello   "));
        vm.Selection = ScreenRegion.FromCorners(2, 5, 3, 14);

        Assert.True(vm.CopyCommand.CanExecute(null));
        await vm.CopyCommand.ExecuteAsync(null);

        Assert.Equal("hello\n", clipboard.Text);
        Assert.Equal(ScreenRegion.FromCorners(2, 5, 3, 14), vm.Selection);
        Assert.Empty(session.Calls);
        Assert.Null(vm.ErrorMessage);
    }

    [Fact]
    public void Copy_is_enabled_only_with_a_selection()
    {
        var (vm, _, _) = Create();
        Assert.False(vm.CopyCommand.CanExecute(null));
        vm.Selection = ScreenRegion.FromCorners(0, 0, 0, 0);
        Assert.True(vm.CopyCommand.CanExecute(null));
        vm.Selection = null;
        Assert.False(vm.CopyCommand.CanExecute(null));
    }

    [Fact]
    public async Task Paste_normalizes_newlines_sends_one_paste_and_clears_the_selection()
    {
        var (vm, session, clipboard) = Create();
        session.RaiseConnection(ConnectionState.Connected3270);
        vm.Selection = ScreenRegion.FromCorners(0, 0, 0, 0);
        clipboard.Text = "line one\r\nline two\rline three";

        Assert.True(vm.PasteCommand.CanExecute(null));
        await vm.PasteCommand.ExecuteAsync(null);

        Assert.Equal(["paste:line one\nline two\nline three"], session.Calls);
        Assert.Null(vm.Selection);
    }

    [Fact]
    public async Task Paste_is_disabled_while_disconnected_and_sends_nothing_when_the_clipboard_is_empty()
    {
        var (vm, session, clipboard) = Create();
        Assert.False(vm.PasteCommand.CanExecute(null));

        session.RaiseConnection(ConnectionState.Connected3270);
        Assert.True(vm.PasteCommand.CanExecute(null));
        clipboard.Text = null;
        await vm.PasteCommand.ExecuteAsync(null);
        clipboard.Text = "";
        await vm.PasteCommand.ExecuteAsync(null);

        Assert.Empty(session.Calls);
        Assert.Null(vm.ErrorMessage);
    }

    [Fact]
    public void SelectAll_covers_the_current_screen()
    {
        var (vm, session, _) = Create();
        Assert.True(vm.SelectAllCommand.CanExecute(null));
        session.RaiseScreen(ScreenSnapshot.Empty(43, 80));
        vm.SelectAllCommand.Execute(null);
        Assert.Equal(ScreenRegion.Full(43, 80), vm.Selection);
    }

    [Fact]
    public async Task Input_sent_to_the_host_clears_the_selection()
    {
        var (vm, _, _) = Create();

        vm.Selection = ScreenRegion.FromCorners(1, 1, 2, 2);
        await vm.SendKeyCommand.ExecuteAsync(TerminalKey.Enter);
        Assert.Null(vm.Selection);

        vm.Selection = ScreenRegion.FromCorners(1, 1, 2, 2);
        await vm.TypeTextAsync("x");
        Assert.Null(vm.Selection);

        vm.Selection = ScreenRegion.FromCorners(1, 1, 2, 2);
        await vm.MoveCursorAsync(3, 3);
        Assert.Null(vm.Selection);
    }

    [Fact]
    public async Task Clipboard_failures_show_a_plain_message()
    {
        var (vm, session, clipboard) = Create();
        session.RaiseConnection(ConnectionState.Connected3270);
        vm.Selection = ScreenRegion.FromCorners(0, 0, 0, 0);
        clipboard.Exception = new InvalidOperationException("clipboard busy");

        await vm.CopyCommand.ExecuteAsync(null);
        Assert.Equal("Could not copy: clipboard busy", vm.ErrorMessage);

        await vm.PasteCommand.ExecuteAsync(null);
        Assert.Equal("Could not read the clipboard: clipboard busy", vm.ErrorMessage);
        Assert.Empty(session.Calls);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~SessionViewModelClipboardTests"`
Expected: build error, `Selection`, `CopyCommand`, `PasteCommand`, `SelectAllCommand` do not exist.

- [ ] **Step 3: Write the implementation**

In `src/LizTerm.App/ViewModels/SessionViewModel.cs`:

Replace the `_screen` and `_isConnected` observable fields with these annotated versions, and add `_selection`:

```csharp
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CopyCommand))]
    [NotifyCanExecuteChangedFor(nameof(SelectAllCommand))]
    private ScreenSnapshot? _screen;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PasteCommand))]
    private bool _isConnected;

    /// <summary>The mouse selection, bound two-way to the screen control. Cleared here whenever input goes to the host.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CopyCommand))]
    private ScreenRegion? _selection;
```

Replace `SendKeyAsync`, `TypeTextAsync`, and `MoveCursorAsync` with versions that apply the lifetime rule:

```csharp
    [RelayCommand]
    private Task SendKeyAsync(TerminalKey key)
    {
        Selection = null;
        return Guard(_session.SendKeyAsync(key));
    }

    public Task TypeTextAsync(string text)
    {
        Selection = null;
        return Guard(_session.TypeTextAsync(text));
    }

    public Task MoveCursorAsync(int row, int column)
    {
        Selection = null;
        return Guard(_session.MoveCursorAsync(row, column));
    }
```

Add the three commands after `MoveCursorAsync`:

```csharp
    private bool CanCopy => Selection is not null && Screen is not null;

    /// <summary>Copies the selection as trimmed lines. Copying is not host input, so the selection stays.</summary>
    [RelayCommand(CanExecute = nameof(CanCopy))]
    private async Task CopyAsync()
    {
        if (Selection is not { } region || Screen is not { } screen) return;
        try
        {
            await _clipboard.SetTextAsync(screen.GetText(region));
        }
        catch (Exception ex)
        {
            ErrorMessage = "Could not copy: " + ex.Message;
        }
    }

    /// <summary>One margin-aware paste of the clipboard text. b3270 moves to the next row at the paste margin on '\n'.</summary>
    [RelayCommand(CanExecute = nameof(IsConnected))]
    private async Task PasteAsync()
    {
        string? text;
        try
        {
            text = await _clipboard.GetTextAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = "Could not read the clipboard: " + ex.Message;
            return;
        }
        if (string.IsNullOrEmpty(text)) return;
        Selection = null;
        await Guard(_session.PasteTextAsync(text.Replace("\r\n", "\n").Replace('\r', '\n')));
    }

    private bool CanSelectAll => Screen is not null;

    [RelayCommand(CanExecute = nameof(CanSelectAll))]
    private void SelectAll()
    {
        if (Screen is { } screen) Selection = ScreenRegion.Full(screen.Rows, screen.Columns);
    }
```

- [ ] **Step 4: Run the view model tests, old and new**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~SessionViewModel"`
Expected: all pass (12 existing + 7 new).

- [ ] **Step 5: Run the full suite and commit**

Run: `dotnet test LizTerm.slnx`
Expected: everything green.

```bash
git add src/LizTerm.App/ViewModels/SessionViewModel.cs tests/LizTerm.App.Tests/ViewModels/SessionViewModelClipboardTests.cs
git commit -m "Add Copy, Paste, and Select All commands and clear the selection on host input

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 9: Edit menu, event wiring, and platform gesture text in SessionWindow

**Files:**
- Modify: `src/LizTerm.App/Views/SessionWindow.axaml`, `src/LizTerm.App/Views/SessionWindow.axaml.cs`

**Interfaces:**
- Consumes: `TerminalScreen.Selection`, `CopyRequested`, `PasteRequested`, `SelectAllRequested` (Tasks 5, 6); `SessionViewModel.Selection`, `CopyCommand`, `PasteCommand`, `SelectAllCommand` (Task 8).
- Produces: the user-visible Edit menu. No new code interfaces.

- [ ] **Step 1: Add the Edit menu and the two-way binding**

In `src/LizTerm.App/Views/SessionWindow.axaml`, insert this menu between the closing `</MenuItem>` of the File menu and `<MenuItem Header="_Keys">`:

```xml
      <MenuItem Header="_Edit">
        <MenuItem x:Name="CopyMenuItem" Header="_Copy" Command="{Binding CopyCommand}" />
        <MenuItem x:Name="PasteMenuItem" Header="_Paste" Command="{Binding PasteCommand}" />
        <Separator />
        <MenuItem x:Name="SelectAllMenuItem" Header="Select _All" Command="{Binding SelectAllCommand}" />
      </MenuItem>
```

Replace the `TerminalScreen` element at the bottom with:

```xml
    <controls:TerminalScreen x:Name="Screen"
                             Snapshot="{Binding Screen}"
                             Selection="{Binding Selection, Mode=TwoWay}"
                             DestructiveBackspace="{Binding Profile.DestructiveBackspace}" />
```

- [ ] **Step 2: Wire the events and the gesture text**

In `src/LizTerm.App/Views/SessionWindow.axaml.cs`, add `using Avalonia.VisualTree;` to the usings, then replace the constructor with:

```csharp
    public SessionWindow()
    {
        InitializeComponent();
        Screen.KeyRequested += (_, key) => _ = ViewModel?.SendKeyCommand.ExecuteAsync(key);
        Screen.TextEntered += (_, text) => _ = ViewModel?.TypeTextAsync(text);
        Screen.CellClicked += (_, cell) => _ = ViewModel?.MoveCursorAsync(cell.Row, cell.Column);
        Screen.CopyRequested += (_, _) => _ = ViewModel?.CopyCommand.ExecuteAsync(null);
        Screen.PasteRequested += (_, _) => _ = ViewModel?.PasteCommand.ExecuteAsync(null);
        Screen.SelectAllRequested += (_, _) => ViewModel?.SelectAllCommand.Execute(null);
        Opened += (_, _) =>
        {
            ShowPlatformGestures();
            Screen.Focus();
        };
    }

    /// <summary>Menu gesture text from the platform table, so macOS shows Cmd and the others show Ctrl.</summary>
    private void ShowPlatformGestures()
    {
        var hotkeys = this.GetPlatformSettings()?.HotkeyConfiguration;
        if (hotkeys is null) return;
        CopyMenuItem.InputGesture = hotkeys.Copy.FirstOrDefault();
        PasteMenuItem.InputGesture = hotkeys.Paste.FirstOrDefault();
        SelectAllMenuItem.InputGesture = hotkeys.SelectAll.FirstOrDefault();
    }
```

- [ ] **Step 3: Build and run the full suite**

Run: `dotnet build LizTerm.slnx` then `dotnet test LizTerm.slnx`
Expected: build succeeds with no XAML binding errors (compiled bindings are on, so a misspelled property fails the build); everything green.

- [ ] **Step 4: Commit**

```bash
git add src/LizTerm.App/Views/SessionWindow.axaml src/LizTerm.App/Views/SessionWindow.axaml.cs
git commit -m "Add the Edit menu and wire selection and clipboard requests to the view model

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 10: Manual verification against the gateway and documentation

**Files:**
- Modify: `CLAUDE.md` (App and Tests sections)

**Interfaces:** none; this task verifies the overlay's look, the real clipboard, the real hotkeys, and b3270's paste margin behavior, none of which the automated tests cover.

- [ ] **Step 1: Build and launch the app against the gateway with an isolated HOME**

The gateway profile lives in a temporary HOME, outside Robert's real config directory. Never type real credentials into the gateway; the paste check below uses only the demo account's user id, `claude`. The JSON fields are the camelCase names of `SessionProfile` (see `src/LizTerm.Core/Session/SessionProfile.cs`).

```bash
dotnet build src/LizTerm.App
S=$(mktemp -d); mkdir -p "$S/Library/Application Support/LizTerm/profiles"
cat > "$S/Library/Application Support/LizTerm/profiles/Gateway.json" <<'JSON'
{"name":"Gateway","host":"129.212.188.194","port":4270,"useTls":true,"verifyCertificate":false,"model":2,"extended":true,"codePage":"cp037","destructiveBackspace":false}
JSON
HOME="$S" LIZTERM_B3270_PATH=/opt/homebrew/bin/b3270 LIZTERM_WIRE_LOG="$S/wire.log" nohup src/LizTerm.App/bin/Debug/net10.0/LizTerm.App Gateway > "$S/app.log" 2>&1 &
echo $!
```

Keep the shell variable `S` for the later steps; the directory is deleted at the end of this task.

- [ ] **Step 2: Attach the Avalonia DevTools MCP and verify selection and copy**

Use the `avalonia_devtools` tools as documented in CLAUDE.md: `attach-to-app` with no arguments to list, then with `id` set to the pid printed above. Wait for the "TN3270 GATEWAY LOGIN" screen (`tree` shows the `SessionWindow`; the status bar text reads connected).

1. `screenshot` the window, then send Cmd+A through `input` (key press with the Meta modifier on the `TerminalScreen` node) and `screenshot` again. Expected: a translucent blue overlay over the whole screen with the text still readable in its host colors and the cursor visible on top.
2. Send Cmd+C through `input`, then in Bash run `pbpaste | head -5`. Expected: the first lines of the login screen, trailing spaces trimmed, one line per row.
3. If the `input` tool supports mouse down, move, and up (check its schema), drag from the middle of the "TN3270 GATEWAY LOGIN" title to a cell two rows down and screenshot: the overlay covers exactly that rectangle. If it supports only Click, skip this and rely on the headless drag tests.
4. Click on the User ID field with `input` Click and confirm through `props` on the `TerminalScreen` node that `Selection` is null (a plain click clears it).

- [ ] **Step 3: Verify paste and the margin behavior**

```bash
printf 'claude\nsecond' | pbcopy
```

Send Cmd+V through `input` with the cursor in the User ID field. Then:

```bash
grep -n "PasteString" "$S/wire.log" | tail -1
```

Expected: one `PasteString` action whose hex argument decodes to `claude\nsecond` (`636C617564650A7365636F6E64`). Then `screenshot`: `claude` appears in the User ID field; the second line lands on the next row at the same column, or is rejected by the host with a keyboard lock message in the status bar if that row is protected. Either result is b3270's margin behavior working; the check is that LizTerm sent exactly one paste with the newline intact.

Press PF3 through `input` (F3 key) to let the host drop the connection, then kill the app:

```bash
kill <pid>; rm -rf "$S"
```

- [ ] **Step 4: Record what was seen**

If any expectation above failed, stop and fix it in the task that owns the behavior (overlay: Task 5; hotkeys: Task 6; commands: Task 8; wiring: Task 9), rerun the suite, and repeat this task. Do not paper over a failure in this step.

- [ ] **Step 5: Update CLAUDE.md**

In the `### App (src/LizTerm.App)` section, add after the `TerminalScreen` bullet:

```markdown
- Mouse selection is a `ScreenRegion` (Core; inclusive, zero-based, always normalized). `SelectionGesture`
  (`Mouse/`) is the pure press/move/release/double-click state machine; `TerminalScreen` feeds it from pointer
  events, exposes `Selection` (two-way styled property), paints `Palette.Selection` over the region after the
  text and before the cursor, clears it when the screen size changes, and raises `CopyRequested`,
  `PasteRequested`, and `SelectAllRequested` from `GetPlatformSettings().HotkeyConfiguration` (Cmd on macOS, Ctrl
  elsewhere; Ctrl fallback). A plain click moves the cursor on release; a double-click selects the run of
  non-space cells. `SessionViewModel` owns Copy (trimmed rows joined by `\n`), Paste (CRLF normalized, one
  `PasteTextAsync`), and Select All, and nulls `Selection` on every path that sends input to the host. Clipboard
  access goes through `ITextClipboard` (`Clipboard/`), injected like the dispatch delegate; the app passes
  `AvaloniaTextClipboard(window)`, so `App.OpenSession` creates the window before the view model.
```

In the `### Tests` section, extend the App tests bullet with: `FakeTextClipboard` holds a `Text` string and an optional `Exception`; control tests drive drags with the headless `MouseDown`/`MouseMove`/`MouseUp` helpers and read `TerminalScreen.Selection` directly.

- [ ] **Step 6: Final full run and commit**

Run: `dotnet test LizTerm.slnx`
Expected: everything green.

```bash
git add CLAUDE.md
git commit -m "Document selection and clipboard in CLAUDE.md

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```
