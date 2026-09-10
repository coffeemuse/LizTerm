# LizTerm: the screen — crosshair, find, and capture — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give the terminal screen a crosshair ruler that follows the cursor (#27), a find bar that searches it and moves the host cursor to a match (#26), and capture to a file or the clipboard as text or HTML (#25).

**Architecture:** No new subsystems and no engine involvement. One pure search function in `LizTerm.Core`; an HTML formatter, a pure crosshair geometry and one new view model in `LizTerm.App`; and three new overlay properties on `TerminalScreen`, drawn after the cached run plan and never folded into it. `IEmulatorSession` is not touched, so no backend or fake changes are needed anywhere in this plan.

**Tech Stack:** .NET 10, Avalonia 12.1.2, CommunityToolkit.Mvvm, xunit.v3 (VSTest mode), Avalonia headless test platform.

**Spec:** `docs/superpowers/specs/2026-09-09-lizterm-the-screen-design.md`

## Global Constraints

- **Dependency rule.** `LizTerm.Core` depends only on the BCL and never mentions Avalonia or b3270 names. `LizTerm.App` names `LizTerm.Backend.B3270` in exactly one place: `src/LizTerm.App/SessionFactory.cs`. Do not add a second. This is why `ScreenSearch` is in Core and the HTML formatter is not — the formatter needs `Palette`, which is Avalonia-typed.
- **License header.** Every hand-written `.cs` and `.axaml` file under `src/` and `tests/` carries three lines: `This file is part of LizTerm.`, `Copyright 2026 by CoffeeMuse`, `SPDX-License-Identifier: BSD-3-Clause`, in that file's comment syntax, within the first eight lines (before the root element in an `.axaml`). `RepositoryHeadersTests` fails the suite otherwise.
- **Zero warnings.** `dotnet build LizTerm.slnx --no-incremental 2>&1 | grep -c " warning "` must print `0` before anything is called done. An incremental build hides warnings from projects it did not recompile.
- **Overlays never enter the run plan.** `TerminalScreen` caches a per-row list of shaped style runs for as long as the snapshot instance and `CellGeometry` are unchanged. Every overlay in this plan is drawn in `Render` *after* `EnsureRunPlan`, from a styled property, and never influences `BuildRun`. `RunPlanBuilds` is the guard and two tasks assert on it.
- **No `Gesture` on any menu item outside Edit.** A `NativeMenuItem` gesture becomes an AppKit key equivalent dispatched before the key window's responder chain, which silently swallows the keystroke from `TerminalScreen`. This is why Find lives in Edit.
- **Both menus change together.** `SessionWindow.axaml` declares a `NativeMenu` and a classic `<Menu>`; `NativeMenuTests.The_native_menu_matches_the_classic_menu_item_for_item` compares them header for header, separator for separator, and Command for Command outside Edit.
- **Every native item needs a `Command` or a `Click` handler.** Avalonia's macOS exporter validates `(Command != null || HasClickHandlers) && IsEnabled`; an item with only a binding is greyed out on macOS and inert everywhere. `NativeMenuTests.Every_native_item_can_actually_be_activated` is the guard.
- **A `NativeMenuItem` never toggles itself.** `RaiseClicked` raises Click and executes Command and never touches `IsChecked`. Native check/radio items use `Mode=OneWay` plus a Click handler that sets the state; classic items stay `TwoWay` and handler-free, because `DefaultMenuInteractionHandler.Click` toggles a `MenuItem` *before* raising Click.
- **Rows and columns are zero-based** everywhere in Core and App.
- **Test commands.** Full suite `dotnet test LizTerm.slnx`; one class `dotnet test tests/<project> --filter "FullyQualifiedName~<ClassName>"`.

---

## File Structure

**Created**

| File | Responsibility |
|---|---|
| `src/LizTerm.Core/Screen/ScreenSearch.cs` | The pure screen search: term to a list of `ScreenRegion`, DBCS-safe. |
| `src/LizTerm.App/Capture/ScreenHtml.cs` | A snapshot rendered as styled HTML, using `Palette`'s colours. |
| `src/LizTerm.App/Files/SafeFileName.cs` | The filename sanitiser the wire log and a capture both use. |
| `src/LizTerm.App/Rendering/CrosshairMode.cs` | `None / Horizontal / Vertical / Both`. |
| `src/LizTerm.App/Rendering/CrosshairGeometry.cs` | Where the crosshair's bars go. Pure math, like `CellGeometry.Fit`. |
| `src/LizTerm.App/ViewModels/CrosshairModeConverter.cs` | "Is the crosshair in this mode?", for a menu item's `IsChecked`. |
| `src/LizTerm.App/ViewModels/FindViewModel.cs` | Find state: term, matches, current index, open/close, navigation. |
| `tests/LizTerm.Core.Tests/Screen/ScreenSearchTests.cs` | Search semantics. |
| `tests/LizTerm.App.Tests/Capture/ScreenHtmlTests.cs` | HTML shape, escaping, renditions. |
| `tests/LizTerm.App.Tests/Rendering/CrosshairGeometryTests.cs` | Bar rectangles for every mode and every edge case. |
| `tests/LizTerm.App.Tests/Controls/TerminalScreenCrosshairTests.cs` | Crosshair plumbing and the run-plan guard. |
| `tests/LizTerm.App.Tests/Controls/TerminalScreenFindTests.cs` | Match overlay, clamping, and the run-plan guard. |
| `tests/LizTerm.App.Tests/ViewModels/FindViewModelTests.cs` | Navigation, re-anchoring, cursor moves. |
| `tests/LizTerm.App.Tests/ViewModels/SessionViewModelCaptureTests.cs` | Save and copy commands, and the suggested name. |

**Modified**

| File | Change |
|---|---|
| `src/LizTerm.App/Rendering/Palette.cs` | Three overlay brushes: `Crosshair`, `FindMatch`, `FindCurrent`. |
| `src/LizTerm.App/Controls/TerminalScreen.cs` | `Crosshair`, `FindMatches`, `CurrentMatch` properties; `DrawCrosshair`, `DrawFindMatches`; `FindRequested` event and its gesture. |
| `src/LizTerm.App/Files/IFilePicker.cs` | `PickSaveLocationAsync` gains a `title`. |
| `src/LizTerm.App/Files/AvaloniaFilePicker.cs` | Uses the passed title. |
| `src/LizTerm.App/ViewModels/FileTransferViewModel.cs` | Passes the transfer title at its one call site. |
| `src/LizTerm.App/ViewModels/SessionViewModel.cs` | `Find`, `Crosshair`, `SaveScreenAsync`, `CopyScreenAsHtmlAsync`; one line in `ApplyScreen`; `WireLogFileName` uses the shared sanitiser. |
| `src/LizTerm.App/Views/SessionWindow.axaml` | View menu; three new items; the find bar. |
| `src/LizTerm.App/Views/SessionWindow.axaml.cs` | Crosshair Click handlers, capture handlers, find focus and key handling, Find's gesture. |
| `tests/LizTerm.App.Tests/Fakes/FakeFilePicker.cs` | Matches the new signature. |
| `tests/LizTerm.App.Tests/Views/NativeMenuTests.cs` | Five top-level menus; View menu parity and activation. |

---

## Task 1: The screen search (#26, part 1)

**Files:**
- Create: `src/LizTerm.Core/Screen/ScreenSearch.cs`
- Test: `tests/LizTerm.Core.Tests/Screen/ScreenSearchTests.cs`

**Interfaces:**
- Consumes: `ScreenSnapshot`, `ScreenRegion`, `Cell`, `CellRendition` — all existing in `LizTerm.Core.Screen`.
- Produces: `public static IReadOnlyList<ScreenRegion> ScreenSearch.Find(ScreenSnapshot snapshot, string term)`. Task 6 is the only consumer.

**Why it is written this way.** The obvious implementation searches `snapshot.RowText(row)` with `string.IndexOf`. Do not do that. `RowText` appends `Rune.ToString()` per cell, which is one UTF-16 char for BMP runes and two otherwise, so a string index into it is not a column index in general. It also cannot express the rule that a match may not start or end on half of a DBCS character. Folding each row into one entry per *character* — remembering the first and last column of each — fixes both at once, and makes the half-cell rule true by construction rather than by a rejection check.

- [ ] **Step 1: Write the failing test**

Create `tests/LizTerm.Core.Tests/Screen/ScreenSearchTests.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.Core.Screen;

namespace LizTerm.Core.Tests.Screen;

public class ScreenSearchTests
{
    private static ScreenSnapshot Screen(params string[] rows)
    {
        var buffer = new ScreenBuffer(rows.Length, 40);
        for (var r = 0; r < rows.Length; r++)
            buffer.SetText(r, 0, rows[r], null, null, null);
        return buffer.Snapshot();
    }

    [Fact]
    public void It_finds_a_term_in_reading_order()
    {
        var screen = Screen("READY", "the ready prompt");

        var matches = ScreenSearch.Find(screen, "ready");

        Assert.Equal(
            [ScreenRegion.FromCorners(0, 0, 0, 4), ScreenRegion.FromCorners(1, 4, 1, 8)],
            matches);
    }

    [Fact]
    public void It_is_case_insensitive()
    {
        Assert.Single(ScreenSearch.Find(Screen("SYS1.PROCLIB"), "proclib"));
    }

    [Fact]
    public void It_finds_every_occurrence_on_one_row()
    {
        var matches = ScreenSearch.Find(Screen("ab ab ab"), "ab");

        Assert.Equal([0, 3, 6], matches.Select(m => m.Left));
        Assert.All(matches, m => Assert.Equal(0, m.Top));
    }

    /// <summary>Non-overlapping: the scan resumes after the end of each match, so "aaaa" holds two "aa", not
    /// three. Overlapping hits would double-count the match counter for no gain a user could use.</summary>
    [Fact]
    public void Matches_do_not_overlap()
    {
        Assert.Equal([0, 2], ScreenSearch.Find(Screen("aaaa"), "aa").Select(m => m.Left));
    }

    /// <summary>A 3270 screen is a grid of independent rows, not reflowed text. A term spanning a row edge is
    /// almost always two unrelated fields that happen to abut.</summary>
    [Fact]
    public void A_term_does_not_match_across_a_row_boundary()
    {
        var buffer = new ScreenBuffer(2, 4);
        buffer.SetText(0, 0, "ab", null, null, null);
        buffer.SetText(1, 0, "cd", null, null, null);

        Assert.Empty(ScreenSearch.Find(buffer.Snapshot(), "bc"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_term_matches_nothing(string term)
    {
        Assert.Empty(ScreenSearch.Find(Screen("READY"), term));
    }

    [Fact]
    public void A_term_longer_than_the_row_matches_nothing()
    {
        var buffer = new ScreenBuffer(1, 3);
        buffer.SetText(0, 0, "abc", null, null, null);

        Assert.Empty(ScreenSearch.Find(buffer.Snapshot(), "abcd"));
    }

    /// <summary>A DBCS character occupies two cells, the second carrying RightHalf. The search folds a row into
    /// one entry per character, so a match reports the full span of the characters it covers and can never
    /// begin or end on half of one.</summary>
    [Fact]
    public void A_match_spans_whole_wide_characters()
    {
        var buffer = new ScreenBuffer(1, 6);
        buffer.SetText(0, 0, "a", null, null, null);
        buffer.SetText(0, 1, "中", null, null, CellRendition.Wide | CellRendition.LeftHalf);
        buffer.SetText(0, 2, "中", null, null, CellRendition.Wide | CellRendition.RightHalf);
        buffer.SetText(0, 3, "b", null, null, null);

        var matches = ScreenSearch.Find(buffer.Snapshot(), "a中b");

        var match = Assert.Single(matches);
        Assert.Equal(ScreenRegion.FromCorners(0, 0, 0, 3), match);
    }

    /// <summary>The right half alone is not a character, so a term cannot begin on it.</summary>
    [Fact]
    public void A_match_cannot_begin_on_the_right_half_of_a_wide_character()
    {
        var buffer = new ScreenBuffer(1, 4);
        buffer.SetText(0, 0, "中", null, null, CellRendition.Wide | CellRendition.LeftHalf);
        buffer.SetText(0, 1, "中", null, null, CellRendition.Wide | CellRendition.RightHalf);
        buffer.SetText(0, 2, "x", null, null, null);

        var match = Assert.Single(ScreenSearch.Find(buffer.Snapshot(), "中x"));
        Assert.Equal(0, match.Left);
        Assert.Equal(2, match.Right);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~ScreenSearchTests"`

Expected: FAIL to compile — `The name 'ScreenSearch' does not exist in the current context`.

- [ ] **Step 3: Write the implementation**

Create `src/LizTerm.Core/Screen/ScreenSearch.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Text;

namespace LizTerm.Core.Screen;

/// <summary>Finding a term on one screen. Pure and snapshot-scoped: a snapshot is immutable, so a search needs
/// no locking and cannot tear, and matches computed here are coordinates that the caller re-runs against the
/// next snapshot rather than trying to keep alive.</summary>
public static class ScreenSearch
{
    /// <summary>Every occurrence of <paramref name="term"/>, case-insensitively, in reading order: row
    /// ascending, then column. Matches never overlap and never span a row boundary. A blank term matches
    /// nothing rather than every position.</summary>
    public static IReadOnlyList<ScreenRegion> Find(ScreenSnapshot snapshot, string term)
    {
        if (string.IsNullOrWhiteSpace(term)) return [];

        var needle = term.EnumerateRunes().Select(Rune.ToLowerInvariant).ToArray();
        var matches = new List<ScreenRegion>();

        // One entry per character rather than per cell, so a column index is exact and a DBCS character is
        // indivisible. Reused across rows to keep this allocation-light on a 43x132 screen.
        var runes = new List<Rune>(snapshot.Columns);
        var starts = new List<int>(snapshot.Columns);
        var ends = new List<int>(snapshot.Columns);

        for (var row = 0; row < snapshot.Rows; row++)
        {
            runes.Clear();
            starts.Clear();
            ends.Clear();

            for (var column = 0; column < snapshot.Columns; column++)
            {
                var cell = snapshot[row, column];

                // The trailing half of the character before it: widen that character instead of adding one.
                if (cell.Rendition.HasFlag(CellRendition.RightHalf) && runes.Count > 0)
                {
                    ends[^1] = column;
                    continue;
                }

                runes.Add(Rune.ToLowerInvariant(cell.Character));
                starts.Add(column);
                ends.Add(column);
            }

            for (var i = 0; i + needle.Length <= runes.Count; i++)
            {
                var hit = true;
                for (var j = 0; j < needle.Length; j++)
                {
                    if (runes[i + j] == needle[j]) continue;
                    hit = false;
                    break;
                }
                if (!hit) continue;

                matches.Add(ScreenRegion.FromCorners(row, starts[i], row, ends[i + needle.Length - 1]));
                i += needle.Length - 1;
            }
        }

        return matches;
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~ScreenSearchTests"`

Expected: PASS, 9 tests.

- [ ] **Step 5: Commit**

```bash
git add src/LizTerm.Core/Screen/ScreenSearch.cs tests/LizTerm.Core.Tests/Screen/ScreenSearchTests.cs
git commit -m "Add the pure screen search (#26)"
```

---

## Task 2: The HTML capture formatter (#25, part 1)

**Files:**
- Create: `src/LizTerm.App/Capture/ScreenHtml.cs`
- Test: `tests/LizTerm.App.Tests/Capture/ScreenHtmlTests.cs`

**Interfaces:**
- Consumes: `ScreenSnapshot`, `Cell`, `CellRendition`, `HostColor` (Core); `Palette.ColorOf(HostColor)` returning `Avalonia.Media.Color` (`src/LizTerm.App/Rendering/Palette.cs`).
- Produces: `public static string ScreenHtml.Render(ScreenSnapshot snapshot)`. Task 3 is the only consumer.

**Why the App layer.** The colours live in `Palette`, which is Avalonia-typed and therefore App-only under the dependency rule. Plain text needs no formatter at all — `ScreenSnapshot.ToText()` already exists and Task 3 calls it directly.

**Rules this encodes, from spec §3.2:**
- Runs are segmented by the same rule the renderer uses: equal foreground, background and rendition.
- `Reverse` swaps foreground and background.
- `Highlight` becomes `font-weight:bold`. The renderer's own 35%-toward-white blend is a display trick that reads as washed-out text in a browser.
- `Underline` becomes `text-decoration:underline`.
- `Blink` is deliberately **not** emitted. A captured screen is a still, and there is no non-annoying HTML for it.
- Text is HTML-escaped. A 3270 screen can hold `<`, `>` and `&`, and an unescaped capture pasted into a forum post is both wrong and an injection vector for whoever renders it.

- [ ] **Step 1: Write the failing test**

Create `tests/LizTerm.App.Tests/Capture/ScreenHtmlTests.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.App.Capture;
using LizTerm.Core.Screen;

namespace LizTerm.App.Tests.Capture;

public class ScreenHtmlTests
{
    private static ScreenSnapshot One(string text, HostColor? foreground = null,
        HostColor? background = null, CellRendition? rendition = null)
    {
        var buffer = new ScreenBuffer(1, 20);
        buffer.SetText(0, 0, text, foreground, background, rendition);
        return buffer.Snapshot();
    }

    [Fact]
    public void It_wraps_the_screen_in_a_pre_block()
    {
        var html = ScreenHtml.Render(One("READY"));

        Assert.StartsWith("<pre", html);
        Assert.EndsWith("</pre>", html);
    }

    [Fact]
    public void A_run_carries_its_foreground_as_a_hex_colour()
    {
        var html = ScreenHtml.Render(One("READY", HostColor.Green));

        Assert.Contains("color:#50FF50", html);
        Assert.Contains("READY", html);
    }

    [Fact]
    public void Highlight_becomes_bold_rather_than_a_lightened_colour()
    {
        var html = ScreenHtml.Render(One("HOT", HostColor.Green, null, CellRendition.Highlight));

        Assert.Contains("font-weight:bold", html);
        Assert.Contains("color:#50FF50", html);
    }

    [Fact]
    public void Underline_becomes_a_text_decoration()
    {
        Assert.Contains("text-decoration:underline",
            ScreenHtml.Render(One("X", null, null, CellRendition.Underline)));
    }

    /// <summary>Reverse video swaps the two colours, exactly as the renderer does.</summary>
    [Fact]
    public void Reverse_swaps_the_colours()
    {
        var html = ScreenHtml.Render(One("X", HostColor.Green, HostColor.Red, CellRendition.Reverse));

        Assert.Contains("color:#FF5050", html);
        Assert.Contains("background:#50FF50", html);
    }

    /// <summary>A still image has no blink. Asserted so its absence reads as a decision rather than an
    /// oversight.</summary>
    [Fact]
    public void Blink_is_not_emitted()
    {
        var html = ScreenHtml.Render(One("X", null, null, CellRendition.Blink));

        Assert.DoesNotContain("blink", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Markup_characters_on_the_screen_are_escaped()
    {
        var html = ScreenHtml.Render(One("<b>&</b>"));

        Assert.Contains("&lt;b&gt;&amp;&lt;/b&gt;", html);
        Assert.DoesNotContain("<b>", html);
    }

    /// <summary>The same segmentation rule the renderer uses: equal foreground, background and rendition. Three
    /// differently-coloured stretches are three spans, not one and not nine.</summary>
    [Fact]
    public void Adjacent_differing_styles_become_separate_spans()
    {
        var buffer = new ScreenBuffer(1, 20);
        buffer.SetText(0, 0, "aaa", HostColor.Green, null, null);
        buffer.SetText(0, 3, "bbb", HostColor.Red, null, null);
        buffer.SetText(0, 6, "ccc", HostColor.Green, null, null);

        var html = ScreenHtml.Render(buffer.Snapshot());

        // Three coloured runs plus the trailing blank run to the end of the row.
        Assert.Equal(4, html.Split("<span").Length - 1);
    }

    [Fact]
    public void Every_row_becomes_its_own_line()
    {
        var buffer = new ScreenBuffer(3, 5);
        buffer.SetText(0, 0, "one", null, null, null);
        buffer.SetText(1, 0, "two", null, null, null);
        buffer.SetText(2, 0, "six", null, null, null);

        var html = ScreenHtml.Render(buffer.Snapshot());

        Assert.Equal(2, html.Split('\n').Length - 1);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~ScreenHtmlTests"`

Expected: FAIL to compile — `The type or namespace name 'Capture' does not exist`.

- [ ] **Step 3: Write the implementation**

Create `src/LizTerm.App/Capture/ScreenHtml.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Text;
using LizTerm.App.Rendering;
using LizTerm.Core.Screen;

namespace LizTerm.App.Capture;

/// <summary>A snapshot rendered as styled HTML, in LizTerm's own colours.
///
/// Deliberately not b3270's PrintText(html): that would add a member to IEmulatorSession for a feature needing
/// no engine, only work while connected, and emit the engine's colours rather than the ones the user is looking
/// at. See spec section 3.1.</summary>
public static class ScreenHtml
{
    /// <summary>One `pre` block, one line per row, one `span` per run of identically-styled cells.</summary>
    public static string Render(ScreenSnapshot snapshot)
    {
        var sb = new StringBuilder();
        sb.Append("<pre style=\"font-family:monospace;background:#000000;padding:8px\">");

        for (var row = 0; row < snapshot.Rows; row++)
        {
            if (row > 0) sb.Append('\n');
            var cells = snapshot.Row(row);
            var column = 0;
            while (column < cells.Length)
            {
                var start = column;
                var style = cells[column];
                while (column < cells.Length && SameStyle(cells[column], style)) column++;
                AppendRun(sb, snapshot.GetText(row, start, column - start), style);
            }
        }

        sb.Append("</pre>");
        return sb.ToString();
    }

    /// <summary>The renderer's rule, repeated rather than shared: RunVisual holds shaped FormattedText and
    /// Avalonia brushes, which HTML has no use for. ScreenHtmlTests asserts the two agree on run count.</summary>
    private static bool SameStyle(in Cell a, in Cell b) =>
        a.Foreground == b.Foreground && a.Background == b.Background && a.Rendition == b.Rendition;

    private static void AppendRun(StringBuilder sb, string text, in Cell style)
    {
        var reverse = style.Rendition.HasFlag(CellRendition.Reverse);
        var foreground = Resolve(reverse ? style.Background : style.Foreground, HostColor.NeutralWhite);
        var background = Resolve(reverse ? style.Foreground : style.Background, HostColor.NeutralBlack);

        sb.Append("<span style=\"color:").Append(Hex(foreground))
          .Append(";background:").Append(Hex(background));

        // Bold, not the renderer's 35%-toward-white blend: that is a phosphor trick, and in a browser it reads
        // as washed-out text rather than as emphasis.
        if (style.Rendition.HasFlag(CellRendition.Highlight)) sb.Append(";font-weight:bold");
        if (style.Rendition.HasFlag(CellRendition.Underline)) sb.Append(";text-decoration:underline");

        // Blink is deliberately absent: a captured screen is a still.
        sb.Append("\">");
        Escape(sb, text);
        sb.Append("</span>");
    }

    private static HostColor Resolve(HostColor color, HostColor fallback) =>
        color == HostColor.Default ? fallback : color;

    private static string Hex(HostColor color)
    {
        var c = Palette.ColorOf(color);
        return $"#{c.R:X2}{c.G:X2}{c.B:X2}";
    }

    private static void Escape(StringBuilder sb, string text)
    {
        foreach (var ch in text)
        {
            switch (ch)
            {
                case '&': sb.Append("&amp;"); break;
                case '<': sb.Append("&lt;"); break;
                case '>': sb.Append("&gt;"); break;
                default: sb.Append(ch); break;
            }
        }
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~ScreenHtmlTests"`

Expected: PASS, 9 tests.

- [ ] **Step 5: Commit**

```bash
git add src/LizTerm.App/Capture/ScreenHtml.cs tests/LizTerm.App.Tests/Capture/ScreenHtmlTests.cs
git commit -m "Render a snapshot as styled HTML, in LizTerm's own colours (#25)"
```

---

## Task 3: Capture reaches the view model and the menus (#25, part 2)

**Files:**
- Create: `src/LizTerm.App/Files/SafeFileName.cs`
- Create: `tests/LizTerm.App.Tests/ViewModels/SessionViewModelCaptureTests.cs`
- Modify: `src/LizTerm.App/Files/IFilePicker.cs`, `src/LizTerm.App/Files/AvaloniaFilePicker.cs:24-32`
- Modify: `src/LizTerm.App/ViewModels/FileTransferViewModel.cs:150`
- Modify: `src/LizTerm.App/ViewModels/SessionViewModel.cs` (`WireLogFileName` at `:145`; new members)
- Modify: `src/LizTerm.App/Views/SessionWindow.axaml`, `src/LizTerm.App/Views/SessionWindow.axaml.cs`
- Modify: `tests/LizTerm.App.Tests/Fakes/FakeFilePicker.cs`
- Modify: `tests/LizTerm.App.Tests/Views/NativeMenuTests.cs`

**Interfaces:**
- Consumes: `ScreenHtml.Render(ScreenSnapshot)` (Task 2); `ScreenSnapshot.ToText()`; `IFilePicker`; `ITextClipboard`.
- Produces, on `SessionViewModel`:
  - `public bool CanCaptureScreen` — `Screen is not null`.
  - `public Task SaveScreenAsync(IFilePicker picker)` — Click-driven from both menus.
  - `public Task CopyScreenAsHtmlAsync()` — plus the generated `CopyScreenAsHtmlCommand`.
  - `public static string ScreenFileName(string profileName, DateTime now, string extension)`.
- Produces: `public static string SafeFileName.Of(string name)`.

**Why a `title` parameter rather than a second method.** `AvaloniaFilePicker.PickSaveLocationAsync` hardcodes `Title = "Save received file as"`, which is transfer wording that would read as a bug on a screen capture. A near-duplicate second method would let the two dialogs drift; one parameter cannot.

**Both commands are enabled while disconnected.** That is the point of not using the engine — the moment a capture is most wanted is often a session the host has just dropped. Gate on `Screen is not null`, never on `IsConnected`.

- [ ] **Step 1: Write the failing test**

Create `tests/LizTerm.App.Tests/ViewModels/SessionViewModelCaptureTests.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.App.Tests.Fakes;
using LizTerm.App.ViewModels;
using LizTerm.Core.Screen;
using LizTerm.Core.Session;

namespace LizTerm.App.Tests.ViewModels;

public class SessionViewModelCaptureTests
{
    private static (SessionViewModel Vm, FakeEmulatorSession Session, FakeTextClipboard Clipboard) Build()
    {
        var session = new FakeEmulatorSession();
        var buffer = new ScreenBuffer(24, 80);
        buffer.SetText(2, 3, "READY", HostColor.Green, null, null);
        session.CurrentScreen = buffer.Snapshot();
        var clipboard = new FakeTextClipboard();
        return (new SessionViewModel(session, action => action(), clipboard), session, clipboard);
    }

    [Fact]
    public void The_suggested_name_carries_the_profile_the_stamp_and_the_extension()
    {
        var name = SessionViewModel.ScreenFileName("TK5", new DateTime(2026, 9, 9, 14, 22, 33), "txt");

        Assert.Equal("screen-TK5-20260909-142233.txt", name);
    }

    [Fact]
    public void A_profile_name_with_path_characters_is_made_safe()
    {
        var name = SessionViewModel.ScreenFileName("a/b c", new DateTime(2026, 9, 9, 1, 2, 3), "html");

        Assert.Equal("screen-a_b_c-20260909-010203.html", name);
    }

    [Fact]
    public async Task Saving_writes_plain_text_for_a_txt_path()
    {
        var (vm, _, _) = Build();
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".txt");
        var picker = new FakeFilePicker { Result = path };

        await vm.SaveScreenAsync(picker);

        var written = await File.ReadAllTextAsync(path);
        Assert.Contains("READY", written);
        Assert.DoesNotContain("<span", written);
        File.Delete(path);
    }

    [Fact]
    public async Task Saving_writes_html_for_an_html_path()
    {
        var (vm, _, _) = Build();
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".html");
        var picker = new FakeFilePicker { Result = path };

        await vm.SaveScreenAsync(picker);

        var written = await File.ReadAllTextAsync(path);
        Assert.Contains("<span", written);
        Assert.Contains("READY", written);
        File.Delete(path);
    }

    [Fact]
    public async Task A_cancelled_save_dialog_writes_nothing_and_reports_nothing()
    {
        var (vm, _, _) = Build();
        var picker = new FakeFilePicker { Result = null };

        await vm.SaveScreenAsync(picker);

        Assert.Null(vm.ErrorMessage);
    }

    [Fact]
    public async Task A_failing_save_reports_through_the_error_banner()
    {
        var (vm, _, _) = Build();
        var picker = new FakeFilePicker { Result = Path.Combine(Path.GetTempPath(), "no-such-dir", "x.txt") };

        await vm.SaveScreenAsync(picker);

        Assert.NotNull(vm.ErrorMessage);
        Assert.Contains("Could not save the screen", vm.ErrorMessage);
    }

    [Fact]
    public async Task Copying_puts_html_on_the_clipboard()
    {
        var (vm, _, clipboard) = Build();

        await vm.CopyScreenAsHtmlAsync();

        Assert.NotNull(clipboard.Text);
        Assert.Contains("<span", clipboard.Text);
        Assert.Contains("READY", clipboard.Text);
    }

    /// <summary>The whole reason capture does not go through b3270: the moment you most want to keep a screen
    /// is often one the host has just dropped.</summary>
    [Fact]
    public async Task Capture_works_while_disconnected()
    {
        var (vm, session, clipboard) = Build();
        session.RaiseConnection(ConnectionState.Disconnected);

        Assert.False(vm.IsConnected);
        Assert.True(vm.CanCaptureScreen);

        await vm.CopyScreenAsHtmlAsync();
        Assert.NotNull(clipboard.Text);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~SessionViewModelCaptureTests"`

Expected: FAIL to compile — `'SessionViewModel' does not contain a definition for 'ScreenFileName'`.

- [ ] **Step 3: Add the shared sanitiser**

Create `src/LizTerm.App/Files/SafeFileName.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

namespace LizTerm.App.Files;

/// <summary>Turning a profile name into something safe to put in a filename. One spelling, because two callers
/// build names from the same profile name — the wire log and a screen capture — and a profile name is free
/// text that can hold a path separator.</summary>
public static class SafeFileName
{
    /// <summary>ASCII letters and digits plus <c>. _ -</c> survive; everything else becomes an underscore.</summary>
    public static string Of(string name) =>
        new(name.Select(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-' ? c : '_').ToArray());
}
```

- [ ] **Step 4: Give the save dialog a title**

In `src/LizTerm.App/Files/IFilePicker.cs`, change the save member to:

```csharp
    /// <summary>OS Save dialog, which asks before overwriting an existing file. Null when cancelled. The title
    /// is the caller's because the two callers save different things: a received file, and a screen capture.</summary>
    Task<string?> PickSaveLocationAsync(string suggestedFileName, string title);
```

In `src/LizTerm.App/Files/AvaloniaFilePicker.cs`, replace the method body's signature and `Title`:

```csharp
    public async Task<string?> PickSaveLocationAsync(string suggestedFileName, string title)
    {
        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedFileName,
        });
        return file?.TryGetLocalPath();
    }
```

In `src/LizTerm.App/ViewModels/FileTransferViewModel.cs:150`, pass the transfer's own title:

```csharp
                : await _picker.PickSaveLocationAsync(LocalFileNames.Suggest(HostFile, HostType), "Save received file as");
```

In `tests/LizTerm.App.Tests/Fakes/FakeFilePicker.cs`, match the signature — the recorded call is unchanged, so no existing assertion moves:

```csharp
    public Task<string?> PickSaveLocationAsync(string suggestedFileName, string title)
    {
        Calls.Add("save:" + suggestedFileName);
        return Exception is not null ? Task.FromException<string?>(Exception) : Task.FromResult(Result);
    }
```

- [ ] **Step 5: Add the capture members to the view model**

In `src/LizTerm.App/ViewModels/SessionViewModel.cs`, add `using LizTerm.App.Capture;` and `using LizTerm.App.Files;` if absent, then replace the body of `WireLogFileName` (`:145`) to use the shared sanitiser and add the capture members beside it:

```csharp
    public static string WireLogFileName(string profileName, DateTime now) =>
        $"wire-{SafeFileName.Of(profileName)}-{now:yyyyMMdd-HHmmss}.log";

    /// <summary>The name the Save dialog opens on. Same shape as a wire log's, so the two files a user might
    /// keep from one session sort together.</summary>
    public static string ScreenFileName(string profileName, DateTime now, string extension) =>
        $"screen-{SafeFileName.Of(profileName)}-{now:yyyyMMdd-HHmmss}.{extension}";
```

Then, beside the clipboard members (after `SelectAll`, around `:512`):

```csharp
    /// <summary>There is a screen to capture. Deliberately not IsConnected: capture needs no engine, and the
    /// moment it is most wanted is often a session the host has just dropped (spec 3.1).</summary>
    public bool CanCaptureScreen => Screen is not null;

    /// <summary>File &gt; Save Screen As... The format follows the extension the OS dialog returned; we write
    /// the bytes rather than handing a path to anything else, because the dialog has just made a promise about
    /// overwriting and only we can keep it.</summary>
    public async Task SaveScreenAsync(IFilePicker picker)
    {
        if (Screen is not { } screen) return;
        try
        {
            var suggested = ScreenFileName(Profile.Name, DateTime.Now, "txt");
            if (await picker.PickSaveLocationAsync(suggested, "Save screen as") is not { } path) return;

            var html = Path.GetExtension(path).Equals(".html", StringComparison.OrdinalIgnoreCase)
                       || Path.GetExtension(path).Equals(".htm", StringComparison.OrdinalIgnoreCase);
            await File.WriteAllTextAsync(path, html ? ScreenHtml.Render(screen) : screen.ToText());
        }
        catch (Exception ex)
        {
            ErrorMessage = "Could not save the screen: " + ex.Message;
        }
    }

    /// <summary>Edit &gt; Copy Screen as HTML. The cheapest useful capture and the one that reaches a bug
    /// report.</summary>
    [RelayCommand(CanExecute = nameof(CanCaptureScreen))]
    public async Task CopyScreenAsHtmlAsync()
    {
        if (Screen is not { } screen) return;
        try
        {
            await _clipboard.SetTextAsync(ScreenHtml.Render(screen));
        }
        catch (Exception ex)
        {
            ErrorMessage = "Could not copy the screen: " + ex.Message;
        }
    }
```

Add `[NotifyPropertyChangedFor(nameof(CanCaptureScreen))]` and `[NotifyCanExecuteChangedFor(nameof(CopyScreenAsHtmlCommand))]` to the existing `_screen` field's attribute list (`:47-52`), so both follow the first published screen:

```csharp
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CopyCommand))]
    [NotifyCanExecuteChangedFor(nameof(SelectAllCommand))]
    [NotifyCanExecuteChangedFor(nameof(CopyScreenAsHtmlCommand))]
    [NotifyPropertyChangedFor(nameof(CanCopy))]
    [NotifyPropertyChangedFor(nameof(CanSelectAll))]
    [NotifyPropertyChangedFor(nameof(CanCaptureScreen))]
    private ScreenSnapshot? _screen;
```

- [ ] **Step 6: Run the view-model test to verify it passes**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~SessionViewModelCaptureTests"`

Expected: PASS, 8 tests.

- [ ] **Step 7: Add both menu items, in both menus**

In `src/LizTerm.App/Views/SessionWindow.axaml`, native File menu — after the `IND$FILE _Transfer...` item and before the separator that precedes `C_lose`:

```xml
            <NativeMenuItem Header="_Save Screen As..." Click="OnSaveScreenClickNative" IsEnabled="{Binding CanCaptureScreen}" />
```

Native Edit menu — after `Select _All`:

```xml
            <NativeMenuItemSeparator />
            <NativeMenuItem Header="Copy Screen as _HTML" Click="OnCopyScreenClickNative" IsEnabled="{Binding CanCaptureScreen}" />
```

Classic File menu — same position:

```xml
        <MenuItem Header="_Save Screen As..." Click="OnSaveScreenClick" IsEnabled="{Binding CanCaptureScreen}" />
```

Classic Edit menu — after `Select _All`:

```xml
        <Separator />
        <MenuItem Header="Copy Screen as _HTML" Command="{Binding CopyScreenAsHtmlCommand}" />
```

- [ ] **Step 8: Add the four handlers**

In `src/LizTerm.App/Views/SessionWindow.axaml.cs`, beside the existing transfer handlers:

```csharp
    // MenuItem.Click and NativeMenuItem.Click have different delegate shapes, so each shared action is two
    // one-line handlers over one method.
    private void OnSaveScreenClick(object? sender, RoutedEventArgs e) => _ = SaveScreenAsync();
    private void OnSaveScreenClickNative(object? sender, EventArgs e) => _ = SaveScreenAsync();

    private async Task SaveScreenAsync()
    {
        if (ViewModel is not { } vm) return;
        await vm.SaveScreenAsync(new AvaloniaFilePicker(this));
        Screen.Focus();
    }

    // Native only: the classic item binds CopyScreenAsHtmlCommand. This calls the method rather than the
    // command for the reason the Edit menu's other native items do — a command disables while it runs.
    private void OnCopyScreenClickNative(object? sender, EventArgs e) => _ = ViewModel?.CopyScreenAsHtmlAsync();
```

- [ ] **Step 9: Update the menu guards**

In `tests/LizTerm.App.Tests/Views/NativeMenuTests.cs`, append:

```csharp
    /// <summary>Capture needs no engine, so both items stay live with the session down. Gating them on
    /// IsConnected would take them away at the moment they are most wanted.</summary>
    [AvaloniaFact]
    public void The_capture_items_stay_enabled_while_disconnected()
    {
        var (window, _, session, _) = Show();
        session.RaiseConnection(ConnectionState.Disconnected);

        Assert.True(Item(window, "_File", "_Save Screen As...").IsEnabled);
        Assert.True(Item(window, "_Edit", "Copy Screen as _HTML").IsEnabled);
    }
```

- [ ] **Step 10: Run the App suite to verify the parity and activation guards still pass**

Run: `dotnet test tests/LizTerm.App.Tests`

Expected: PASS. `The_native_menu_matches_the_classic_menu_item_for_item` and `Every_native_item_can_actually_be_activated` both cover the four new items; a separator added to one menu and not the other fails the first.

- [ ] **Step 11: Commit**

```bash
git add src/LizTerm.App tests/LizTerm.App.Tests
git commit -m "Save the screen to a file and copy it as HTML (#25)"
```

---

## Task 4: The crosshair renders (#27, part 1)

**Files:**
- Create: `src/LizTerm.App/Rendering/CrosshairMode.cs`, `src/LizTerm.App/Rendering/CrosshairGeometry.cs`
- Create: `tests/LizTerm.App.Tests/Rendering/CrosshairGeometryTests.cs`, `tests/LizTerm.App.Tests/Controls/TerminalScreenCrosshairTests.cs`
- Modify: `src/LizTerm.App/Rendering/Palette.cs`, `src/LizTerm.App/Controls/TerminalScreen.cs`

**Interfaces:**
- Consumes: `CellGeometry` (`Rendering/CellGeometry.cs`), `CursorPosition`, `ScreenSnapshot`.
- Produces:
  - `public enum CrosshairMode { None, Horizontal, Vertical, Both }`.
  - `public static (Rect? Horizontal, Rect? Vertical) CrosshairGeometry.Rects(CrosshairMode mode, CursorPosition cursor, CellGeometry geometry, int rows, int columns)`.
  - `public CrosshairMode TerminalScreen.Crosshair` (styled property). Task 5 binds it.

**Why the geometry is a separate pure function.** `CellGeometry.Fit` is the precedent — pure math, unit tested, with the control doing nothing but draw what it returns. Asserting on rectangles is precise where asserting on rendered pixels is fragile, and it keeps `TerminalScreen`'s new code to two `FillRectangle` calls.

- [ ] **Step 1: Write the failing geometry test**

Create `tests/LizTerm.App.Tests/Rendering/CrosshairGeometryTests.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia;
using LizTerm.App.Rendering;
using LizTerm.Core.Screen;

namespace LizTerm.App.Tests.Rendering;

public class CrosshairGeometryTests
{
    // 10-wide, 20-high cells at the origin: a 24x80 screen is 800x480.
    private static readonly CellGeometry Geometry = new(10, 20, 16, 0, 0);

    private static (Rect? Horizontal, Rect? Vertical) At(CrosshairMode mode, int row, int column, bool visible = true) =>
        CrosshairGeometry.Rects(mode, new CursorPosition(row, column, visible), Geometry, 24, 80);

    [Fact]
    public void None_draws_nothing()
    {
        var (horizontal, vertical) = At(CrosshairMode.None, 5, 12);

        Assert.Null(horizontal);
        Assert.Null(vertical);
    }

    [Fact]
    public void Horizontal_spans_the_full_width_at_the_cursor_row()
    {
        var (horizontal, vertical) = At(CrosshairMode.Horizontal, 5, 12);

        Assert.Equal(new Rect(0, 100, 800, 20), horizontal);
        Assert.Null(vertical);
    }

    [Fact]
    public void Vertical_spans_the_full_height_at_the_cursor_column()
    {
        var (horizontal, vertical) = At(CrosshairMode.Vertical, 5, 12);

        Assert.Null(horizontal);
        Assert.Equal(new Rect(120, 0, 10, 480), vertical);
    }

    [Fact]
    public void Both_draws_both()
    {
        var (horizontal, vertical) = At(CrosshairMode.Both, 5, 12);

        Assert.Equal(new Rect(0, 100, 800, 20), horizontal);
        Assert.Equal(new Rect(120, 0, 10, 480), vertical);
    }

    /// <summary>The ruler's job is column alignment, not showing where input will land, so a hidden cursor
    /// still carries one. Vista draws its ruler regardless. See spec section 4.2.</summary>
    [Fact]
    public void It_follows_a_hidden_cursor()
    {
        var (horizontal, vertical) = At(CrosshairMode.Both, 5, 12, visible: false);

        Assert.NotNull(horizontal);
        Assert.NotNull(vertical);
    }

    /// <summary>A cursor past the edge of the grid is a screen that has just resized under a stale snapshot.
    /// Draw nothing rather than a bar off the side.</summary>
    [Theory]
    [InlineData(24, 12)]
    [InlineData(5, 80)]
    public void A_cursor_outside_the_grid_draws_nothing(int row, int column)
    {
        var (horizontal, vertical) = At(CrosshairMode.Both, row, column);

        Assert.Null(horizontal);
        Assert.Null(vertical);
    }

    [Fact]
    public void An_unmeasured_geometry_draws_nothing()
    {
        var rects = CrosshairGeometry.Rects(CrosshairMode.Both, new CursorPosition(1, 1, true), default, 24, 80);

        Assert.Null(rects.Horizontal);
        Assert.Null(rects.Vertical);
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~CrosshairGeometryTests"`

Expected: FAIL to compile — `The name 'CrosshairGeometry' does not exist`.

- [ ] **Step 3: Write the mode and the geometry**

Create `src/LizTerm.App/Rendering/CrosshairMode.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

namespace LizTerm.App.Rendering;

/// <summary>Which ruler lines follow the cursor. Vista offers all three shapes; horizontal alone is the one
/// that earns its keep on a wide panel, where the problem is tracking one row across 132 columns.</summary>
public enum CrosshairMode
{
    None,
    Horizontal,
    Vertical,
    Both,
}
```

Create `src/LizTerm.App/Rendering/CrosshairGeometry.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia;
using LizTerm.Core.Screen;

namespace LizTerm.App.Rendering;

/// <summary>Where the crosshair's bars go. Pure math with no Avalonia rendering in it, the same shape as
/// CellGeometry.Fit, so the rule is asserted on rectangles rather than on pixels.</summary>
public static class CrosshairGeometry
{
    /// <summary>The horizontal and vertical bars for <paramref name="mode"/>, either of which may be null.
    /// A hidden cursor still gets a crosshair; a cursor outside the grid, or a geometry that has not been
    /// measured yet, gets none.</summary>
    public static (Rect? Horizontal, Rect? Vertical) Rects(
        CrosshairMode mode, CursorPosition cursor, CellGeometry geometry, int rows, int columns)
    {
        if (mode == CrosshairMode.None) return (null, null);
        if (geometry.CellWidth <= 0 || geometry.CellHeight <= 0) return (null, null);
        if ((uint)cursor.Row >= (uint)rows || (uint)cursor.Column >= (uint)columns) return (null, null);

        var cell = geometry.CellRect(cursor.Row, cursor.Column);
        var width = columns * geometry.CellWidth;
        var height = rows * geometry.CellHeight;

        var horizontal = mode is CrosshairMode.Horizontal or CrosshairMode.Both
            ? new Rect(geometry.OriginX, cell.Y, width, geometry.CellHeight)
            : (Rect?)null;

        var vertical = mode is CrosshairMode.Vertical or CrosshairMode.Both
            ? new Rect(cell.X, geometry.OriginY, geometry.CellWidth, height)
            : (Rect?)null;

        return (horizontal, vertical);
    }
}
```

- [ ] **Step 4: Run it to verify it passes**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~CrosshairGeometryTests"`

Expected: PASS, 8 tests.

- [ ] **Step 5: Write the failing control test**

Create `tests/LizTerm.App.Tests/Controls/TerminalScreenCrosshairTests.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using LizTerm.App.Controls;
using LizTerm.App.Rendering;
using LizTerm.Core.Screen;

namespace LizTerm.App.Tests.Controls;

public class TerminalScreenCrosshairTests
{
    private static (Window Window, TerminalScreen Screen) Show()
    {
        var screen = new TerminalScreen { Snapshot = ScreenSnapshot.Empty(24, 80) };
        var window = new Window { Width = 800, Height = 600, Content = screen };
        window.Show();
        return (window, screen);
    }

    [AvaloniaFact]
    public void It_defaults_to_none()
    {
        var (_, screen) = Show();

        Assert.Equal(CrosshairMode.None, screen.Crosshair);
    }

    /// <summary>The guard for the overlay rule (spec section 2). The crosshair is an overlay drawn after the
    /// cached run plan; changing it must not re-segment and re-shape every cell on the screen.</summary>
    [AvaloniaFact]
    public void Changing_the_crosshair_does_not_rebuild_the_run_plan()
    {
        var (_, screen) = Show();
        screen.Measure(new Avalonia.Size(800, 600));
        screen.Arrange(new Avalonia.Rect(0, 0, 800, 600));
        var before = screen.RunPlanBuilds;
        Assert.True(before > 0, "the first render should have built a run plan");

        foreach (var mode in new[] { CrosshairMode.Horizontal, CrosshairMode.Vertical, CrosshairMode.Both })
        {
            screen.Crosshair = mode;
            screen.Measure(new Avalonia.Size(800, 600));
            screen.Arrange(new Avalonia.Rect(0, 0, 800, 600));
        }

        Assert.Equal(before, screen.RunPlanBuilds);
    }
}
```

- [ ] **Step 6: Run it to verify it fails**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~TerminalScreenCrosshairTests"`

Expected: FAIL to compile — `'TerminalScreen' does not contain a definition for 'Crosshair'`.

- [ ] **Step 7: Add the brush and the property**

In `src/LizTerm.App/Rendering/Palette.cs`, after the `Selection` brush (`:17`):

```csharp
    /// <summary>The crosshair ruler. Dimmer than Selection because it is on screen continuously rather than
    /// for as long as a drag lasts, and host text has to stay readable straight through it.</summary>
    public static readonly IBrush Crosshair = new ImmutableSolidColorBrush(Color.FromArgb(0x30, 0xE0, 0xE0, 0x60));
```

In `src/LizTerm.App/Controls/TerminalScreen.cs`, add `using LizTerm.App.Rendering;` if absent, then after `SelectionProperty` (`:34-35`):

```csharp
    /// <summary>Which crosshair lines follow the cursor, per window. b3270's own CROSSHAIR toggle is
    /// deliberately unused: the engine has no display, so routing a display preference through a child process
    /// to have it handed back would only make the crosshair unavailable while disconnected (spec 4.1).</summary>
    public static readonly StyledProperty<CrosshairMode> CrosshairProperty =
        AvaloniaProperty.Register<TerminalScreen, CrosshairMode>(nameof(Crosshair));
```

Beside the other property accessors (after `Selection`, `:122-126`):

```csharp
    public CrosshairMode Crosshair
    {
        get => GetValue(CrosshairProperty);
        set => SetValue(CrosshairProperty, value);
    }
```

In the static constructor (`:49`), add it to `AffectsRender`:

```csharp
        AffectsRender<TerminalScreen>(SnapshotProperty, SelectionProperty, CrosshairProperty);
```

- [ ] **Step 8: Draw it**

In `src/LizTerm.App/Controls/TerminalScreen.cs`, in `Render` (`:308`), insert the crosshair call between the run loop and `DrawSelection` (`:331`):

```csharp
        DrawCrosshair(context, snapshot, g);
        DrawSelection(context, snapshot, g);
        DrawCursor(context, snapshot, g);
```

And add the method beside `DrawSelection` (`:411`):

```csharp
    private void DrawCrosshair(DrawingContext context, ScreenSnapshot snapshot, CellGeometry g)
    {
        var (horizontal, vertical) = CrosshairGeometry.Rects(Crosshair, snapshot.Cursor, g, snapshot.Rows, snapshot.Columns);
        if (horizontal is { } h) context.FillRectangle(Palette.Crosshair, h);
        if (vertical is { } v) context.FillRectangle(Palette.Crosshair, v);
    }
```

- [ ] **Step 9: Run both test classes to verify they pass**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~Crosshair"`

Expected: PASS, 10 tests.

- [ ] **Step 10: Commit**

```bash
git add src/LizTerm.App tests/LizTerm.App.Tests
git commit -m "Draw a crosshair ruler that follows the cursor (#27)"
```

---

## Task 5: The View menu drives the crosshair (#27, part 2)

**Files:**
- Modify: `src/LizTerm.App/ViewModels/SessionViewModel.cs`
- Modify: `src/LizTerm.App/Views/SessionWindow.axaml`, `src/LizTerm.App/Views/SessionWindow.axaml.cs`
- Modify: `tests/LizTerm.App.Tests/Views/NativeMenuTests.cs`

**Interfaces:**
- Consumes: `CrosshairMode`, `TerminalScreen.Crosshair` (Task 4).
- Produces: `public CrosshairMode Crosshair` on `SessionViewModel` (an `[ObservableProperty]` over `_crosshair`), bound to the control and to each menu item's `IsChecked`.

**Why the state is on the view model.** Holding it in the window's code-behind would leave the native items with nothing to bind `IsChecked` to, forcing the check marks to be driven imperatively — exactly the shape the trap below exists to avoid. It is view-model state in the ordinary sense: per window, owned by the window's data context, and assertable with a plain `[Fact]`.

**The trap.** A `NativeMenuItem` never toggles itself: `RaiseClicked` raises Click and executes Command and never touches `IsChecked`. So the native items bind `IsChecked` **`Mode=OneWay`** and carry a Click handler that sets `vm.Crosshair`, letting the binding carry the new state back to every check mark — including the three corrections to false. The classic items stay `TwoWay` and handler-free, because `DefaultMenuInteractionHandler.Click` toggles a `MenuItem`'s `IsChecked` *before* raising Click.

**`ToggleType="Radio"` is unverified on the macOS exporter** (`CheckBox` is proven here; `Radio` is not). Step 8 is the observation, on a real GUI session, now that four items exist to observe. The fallback is decided in advance and is behaviourally identical: if `Radio` does not render as a group, change the four `ToggleType` values to `CheckBox` and change nothing else — the handlers already set every check themselves.

- [ ] **Step 1: Write the failing test**

In `tests/LizTerm.App.Tests/Views/NativeMenuTests.cs`, change the top-level menu assertion (`:74-81`) to expect five and append the View menu tests:

```csharp
    [AvaloniaFact]
    public void The_window_menu_has_the_same_five_top_level_menus_as_the_classic_one()
    {
        var (window, _, _, _) = Show();

        var headers = NativeMenu.GetMenu(window)!.Items.OfType<NativeMenuItem>().Select(i => i.Header!).ToArray();
        Assert.Equal(["_File", "_Edit", "_View", "_Keys", "_Help"], headers);
    }

    /// <summary>A NativeMenuItem never toggles itself — RaiseClicked raises Click and executes Command and
    /// never touches IsChecked — so the handler sets the view model and the OneWay bindings carry every check
    /// mark, the three corrections to false included. Driven through RaiseClicked because that is the one entry
    /// point both real renderers use; assigning IsChecked instead would only prove a binding round-trips.</summary>
    [AvaloniaFact]
    public void Choosing_a_crosshair_mode_checks_exactly_that_item()
    {
        var (window, vm, _, _) = Show();
        var items = new[] { "_None", "_Horizontal", "_Vertical", "_Both" }
            .Select(header => Item(window, "_View", header)).ToArray();

        ((INativeMenuItemExporterEventsImplBridge)items[2]).RaiseClicked();

        Assert.Equal(CrosshairMode.Vertical, vm.Crosshair);
        Assert.Equal([false, false, true, false], items.Select(i => i.IsChecked));
    }

    [AvaloniaFact]
    public void The_crosshair_reaches_the_terminal_screen()
    {
        var (window, vm, _, _) = Show();

        vm.Crosshair = CrosshairMode.Both;

        Assert.Equal(CrosshairMode.Both, window.FindControl<TerminalScreen>("Screen")!.Crosshair);
    }
```

Add the usings this needs to the top of the file:

```csharp
using Avalonia.Controls.Platform;
using LizTerm.App.Controls;
using LizTerm.App.Rendering;
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~NativeMenuTests"`

Expected: FAIL — the five-menu assertion fails with four headers, and the crosshair tests fail to compile on `vm.Crosshair`.

- [ ] **Step 3: Add the view-model property**

In `src/LizTerm.App/ViewModels/SessionViewModel.cs`, add `using LizTerm.App.Rendering;` and, beside the other display state (after `_screen`, around `:52`):

```csharp
    /// <summary>Which crosshair lines follow the cursor, for this window only. Not a profile field: it is a
    /// display preference, and a home for those is #19's job rather than something to invent here (spec 4.3).
    /// </summary>
    [ObservableProperty] private CrosshairMode _crosshair;
```

- [ ] **Step 4: Add the View menu to both menus**

In `src/LizTerm.App/Views/SessionWindow.axaml`, insert a native View menu between the Edit and Keys menus:

```xml
      <NativeMenuItem Header="_View">
        <NativeMenuItem.Menu>
          <NativeMenu>
            <!-- OneWay plus a Click handler, not a TwoWay binding: a NativeMenuItem never toggles itself, so
                 the handler sets the view model and these bindings carry every check mark back — including the
                 three that have to go false. No Gesture: outside Edit, a native gesture is an AppKit key
                 equivalent dispatched ahead of the responder chain, which would take the key from the terminal. -->
            <NativeMenuItem Header="_None" ToggleType="Radio" Click="OnCrosshairNoneClickNative"
                            IsChecked="{Binding Crosshair, Converter={x:Static vm:CrosshairModeConverter.Instance}, ConverterParameter=None, Mode=OneWay}" />
            <NativeMenuItem Header="_Horizontal" ToggleType="Radio" Click="OnCrosshairHorizontalClickNative"
                            IsChecked="{Binding Crosshair, Converter={x:Static vm:CrosshairModeConverter.Instance}, ConverterParameter=Horizontal, Mode=OneWay}" />
            <NativeMenuItem Header="_Vertical" ToggleType="Radio" Click="OnCrosshairVerticalClickNative"
                            IsChecked="{Binding Crosshair, Converter={x:Static vm:CrosshairModeConverter.Instance}, ConverterParameter=Vertical, Mode=OneWay}" />
            <NativeMenuItem Header="_Both" ToggleType="Radio" Click="OnCrosshairBothClickNative"
                            IsChecked="{Binding Crosshair, Converter={x:Static vm:CrosshairModeConverter.Instance}, ConverterParameter=Both, Mode=OneWay}" />
          </NativeMenu>
        </NativeMenuItem.Menu>
      </NativeMenuItem>
```

And the classic View menu, in the same position between Edit and Keys:

```xml
      <MenuItem Header="_View">
        <MenuItem Header="_None" ToggleType="Radio" GroupName="Crosshair" Click="OnCrosshairNoneClick"
                  IsChecked="{Binding Crosshair, Converter={x:Static vm:CrosshairModeConverter.Instance}, ConverterParameter=None, Mode=OneWay}" />
        <MenuItem Header="_Horizontal" ToggleType="Radio" GroupName="Crosshair" Click="OnCrosshairHorizontalClick"
                  IsChecked="{Binding Crosshair, Converter={x:Static vm:CrosshairModeConverter.Instance}, ConverterParameter=Horizontal, Mode=OneWay}" />
        <MenuItem Header="_Vertical" ToggleType="Radio" GroupName="Crosshair" Click="OnCrosshairVerticalClick"
                  IsChecked="{Binding Crosshair, Converter={x:Static vm:CrosshairModeConverter.Instance}, ConverterParameter=Vertical, Mode=OneWay}" />
        <MenuItem Header="_Both" ToggleType="Radio" GroupName="Crosshair" Click="OnCrosshairBothClick"
                  IsChecked="{Binding Crosshair, Converter={x:Static vm:CrosshairModeConverter.Instance}, ConverterParameter=Both, Mode=OneWay}" />
      </MenuItem>
```

Note the classic items here are **not** the usual TwoWay-and-handler-free shape. A radio *group* of four cannot be expressed as four independent two-way bools over one enum — the interaction handler would set one true without setting the others false. OneWay-plus-Click makes both menus agree, and `GroupName` gives the classic ones their radio behaviour.

Bind the `vm` namespace at the top of the file if it is not already declared:

```xml
        xmlns:vm="using:LizTerm.App.ViewModels"
```

Also bind the control's property, on the `TerminalScreen` element at the bottom of the file:

```xml
    <controls:TerminalScreen x:Name="Screen"
                             Snapshot="{Binding Screen}"
                             Selection="{Binding Selection, Mode=TwoWay}"
                             Crosshair="{Binding Crosshair}"
                             DestructiveBackspace="{Binding Profile.DestructiveBackspace}" />
```

- [ ] **Step 5: Add the converter**

Create `src/LizTerm.App/ViewModels/CrosshairModeConverter.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Globalization;
using Avalonia.Data.Converters;
using LizTerm.App.Rendering;

namespace LizTerm.App.ViewModels;

/// <summary>"Is the crosshair in this mode?", for a menu item's IsChecked. One-way only: a radio group of four
/// cannot be driven by four independent two-way bools over one enum, because setting one true would leave the
/// other three true as well. The Click handlers write the enum; these bindings render it.</summary>
public sealed class CrosshairModeConverter : IValueConverter
{
    public static readonly CrosshairModeConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is CrosshairMode mode
        && parameter is string name
        && Enum.TryParse<CrosshairMode>(name, out var wanted)
        && mode == wanted;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("Crosshair menu items are one-way; the Click handlers set the mode.");
}
```

- [ ] **Step 6: Add the eight handlers**

In `src/LizTerm.App/Views/SessionWindow.axaml.cs`:

```csharp
    // Two one-line handlers per mode: MenuItem.Click and NativeMenuItem.Click have different delegate shapes.
    private void OnCrosshairNoneClick(object? sender, RoutedEventArgs e) => SetCrosshair(CrosshairMode.None);
    private void OnCrosshairNoneClickNative(object? sender, EventArgs e) => SetCrosshair(CrosshairMode.None);
    private void OnCrosshairHorizontalClick(object? sender, RoutedEventArgs e) => SetCrosshair(CrosshairMode.Horizontal);
    private void OnCrosshairHorizontalClickNative(object? sender, EventArgs e) => SetCrosshair(CrosshairMode.Horizontal);
    private void OnCrosshairVerticalClick(object? sender, RoutedEventArgs e) => SetCrosshair(CrosshairMode.Vertical);
    private void OnCrosshairVerticalClickNative(object? sender, EventArgs e) => SetCrosshair(CrosshairMode.Vertical);
    private void OnCrosshairBothClick(object? sender, RoutedEventArgs e) => SetCrosshair(CrosshairMode.Both);
    private void OnCrosshairBothClickNative(object? sender, EventArgs e) => SetCrosshair(CrosshairMode.Both);

    private void SetCrosshair(CrosshairMode mode)
    {
        if (ViewModel is { } vm) vm.Crosshair = mode;
    }
```

Add `using LizTerm.App.Rendering;` at the top of the file.

- [ ] **Step 7: Run the App suite to verify it passes**

Run: `dotnet test tests/LizTerm.App.Tests`

Expected: PASS. The parity guard now walks five menus; `Every_native_item_can_actually_be_activated` covers the four new View items, each of which has a Click handler.

- [ ] **Step 8: Observe how `Radio` renders on macOS**

```bash
dotnet build src/LizTerm.App
LIZTERM_B3270_PATH=/opt/homebrew/bin/b3270 nohup dotnet run --project src/LizTerm.App --no-build &
```

Open a session, then the **View** menu in the macOS menu bar. Check that the four items form a radio group with exactly one marked, and that choosing one moves the mark.

If they render as a radio group: change nothing. If they render as check marks or not at all: change the four native `ToggleType="Radio"` values to `ToggleType="CheckBox"` and leave everything else alone — the handlers already set every check. Record which way it went in a comment above the four native items.

Kill the `dotnet run` pid when done.

- [ ] **Step 9: Commit**

```bash
git add src/LizTerm.App tests/LizTerm.App.Tests
git commit -m "Offer the crosshair from a View menu (#27)"
```

---

## Task 6: The find view model (#26, part 2)

**Files:**
- Create: `src/LizTerm.App/ViewModels/FindViewModel.cs`
- Test: `tests/LizTerm.App.Tests/ViewModels/FindViewModelTests.cs`

**Interfaces:**
- Consumes: `ScreenSearch.Find(ScreenSnapshot, string)` (Task 1); `ScreenSnapshot`; `ScreenRegion`.
- Produces:
  - `public FindViewModel(Func<int, int, Task> moveCursor)`.
  - `public bool IsOpen`, `public string Term`, `public IReadOnlyList<ScreenRegion> Matches`, `public int CurrentIndex`, `public ScreenRegion? CurrentMatch`, `public string CountText`.
  - `public void OnScreen(ScreenSnapshot)`, `public void Open()`, `public void Close()`, `public Task NextAsync()`, `public Task PreviousAsync()`.
  - `internal static int Reanchor(IReadOnlyList<ScreenRegion> matches, ScreenRegion? previous)`.

**The `_visited` flag, which is the subtle part.** Typing must not move the host cursor — that would send a `MoveCursor` on every character typed into the box. So typing sets `CurrentIndex` to 0 for *highlighting* only, and the first Enter must move to match 0 rather than skipping to match 1. `_visited` records whether the cursor has been moved to the current match: it is cleared when the term changes or the bar opens, and left alone by a repaint recompute, so a repaint that keeps your place does not make Enter re-visit it.

- [ ] **Step 1: Write the failing test**

Create `tests/LizTerm.App.Tests/ViewModels/FindViewModelTests.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.App.ViewModels;
using LizTerm.Core.Screen;

namespace LizTerm.App.Tests.ViewModels;

public class FindViewModelTests
{
    private static ScreenSnapshot Screen(params string[] rows)
    {
        var buffer = new ScreenBuffer(Math.Max(rows.Length, 1), 40);
        for (var r = 0; r < rows.Length; r++)
            buffer.SetText(r, 0, rows[r], null, null, null);
        return buffer.Snapshot();
    }

    private static (FindViewModel Find, List<(int Row, int Column)> Moves) Build(ScreenSnapshot? screen = null)
    {
        var moves = new List<(int, int)>();
        var find = new FindViewModel((row, column) => { moves.Add((row, column)); return Task.CompletedTask; });
        find.OnScreen(screen ?? Screen("ab", "ab", "ab"));
        return (find, moves);
    }

    [Fact]
    public void It_starts_closed_with_no_matches()
    {
        var (find, _) = Build();

        Assert.False(find.IsOpen);
        Assert.Empty(find.Matches);
        Assert.Null(find.CurrentMatch);
    }

    [Fact]
    public void Typing_a_term_finds_matches_and_selects_the_first_without_moving_the_cursor()
    {
        var (find, moves) = Build();
        find.Open();

        find.Term = "ab";

        Assert.Equal(3, find.Matches.Count);
        Assert.Equal(0, find.CurrentIndex);
        Assert.Empty(moves);
        Assert.Equal("1 of 3", find.CountText);
    }

    /// <summary>The first Enter goes to the match already highlighted, rather than skipping past it.</summary>
    [Fact]
    public async Task The_first_next_moves_to_the_first_match()
    {
        var (find, moves) = Build();
        find.Open();
        find.Term = "ab";

        await find.NextAsync();

        Assert.Equal(0, find.CurrentIndex);
        Assert.Equal([(0, 0)], moves);
    }

    [Fact]
    public async Task Next_walks_forward_and_wraps()
    {
        var (find, moves) = Build();
        find.Open();
        find.Term = "ab";

        await find.NextAsync();
        await find.NextAsync();
        await find.NextAsync();
        await find.NextAsync();

        Assert.Equal([(0, 0), (1, 0), (2, 0), (0, 0)], moves);
        Assert.Equal("1 of 3", find.CountText);
    }

    [Fact]
    public async Task Previous_walks_back_and_wraps()
    {
        var (find, moves) = Build();
        find.Open();
        find.Term = "ab";

        await find.NextAsync();
        await find.PreviousAsync();

        Assert.Equal([(0, 0), (2, 0)], moves);
    }

    [Fact]
    public void No_matches_reports_so_and_moves_nothing()
    {
        var (find, moves) = Build();
        find.Open();

        find.Term = "zz";

        Assert.Empty(find.Matches);
        Assert.Equal(-1, find.CurrentIndex);
        Assert.Equal("No matches", find.CountText);
        Assert.Empty(moves);
    }

    [Fact]
    public void A_blank_term_reports_nothing_at_all()
    {
        var (find, _) = Build();
        find.Open();

        find.Term = "";

        Assert.Equal("", find.CountText);
    }

    /// <summary>A 3270 screen repaints on every keystroke echo. Clearing matches the way Selection clears would
    /// make the highlight vanish immediately and read as broken (spec 5.5).</summary>
    [Fact]
    public void A_repaint_recomputes_the_matches()
    {
        var (find, _) = Build();
        find.Open();
        find.Term = "ab";

        find.OnScreen(Screen("ab", "ab", "ab", "ab"));

        Assert.Equal(4, find.Matches.Count);
    }

    /// <summary>Re-anchored by position: a repaint that leaves your match where it was does not move you.</summary>
    [Fact]
    public async Task A_repaint_keeps_the_current_match_when_it_is_still_there()
    {
        var (find, _) = Build();
        find.Open();
        find.Term = "ab";
        await find.NextAsync();
        await find.NextAsync();
        Assert.Equal(1, find.CurrentIndex);

        find.OnScreen(Screen("ab", "ab", "ab", "ab"));

        Assert.Equal(1, find.CurrentIndex);
    }

    /// <summary>And a repaint that rewrites the screen underneath you starts over rather than pointing at
    /// something arbitrary.</summary>
    [Fact]
    public async Task A_repaint_that_removes_the_current_match_resets_to_the_first()
    {
        var (find, _) = Build();
        find.Open();
        find.Term = "ab";
        await find.NextAsync();
        await find.NextAsync();
        Assert.Equal(1, find.CurrentIndex);

        find.OnScreen(Screen("xx", "xx", "ab"));

        Assert.Equal(0, find.CurrentIndex);
        Assert.Single(find.Matches);
    }

    /// <summary>Changing the term is a new search, so the next Enter visits its first match rather than
    /// advancing past it.</summary>
    [Fact]
    public async Task Changing_the_term_starts_the_walk_again()
    {
        var (find, moves) = Build(Screen("ab cd", "ab cd"));
        find.Open();
        find.Term = "ab";
        await find.NextAsync();
        await find.NextAsync();

        find.Term = "cd";
        await find.NextAsync();

        Assert.Equal((0, 3), moves[^1]);
    }

    [Fact]
    public void Closing_clears_the_matches_so_no_stale_highlight_survives()
    {
        var (find, _) = Build();
        find.Open();
        find.Term = "ab";

        find.Close();

        Assert.False(find.IsOpen);
        Assert.Empty(find.Matches);
        Assert.Null(find.CurrentMatch);
    }

    /// <summary>A closed bar does no work: a session with the bar shut must not pay a search on every repaint.
    /// </summary>
    [Fact]
    public void A_closed_bar_does_not_search_on_a_repaint()
    {
        var (find, _) = Build();
        find.Term = "ab";

        find.OnScreen(Screen("ab", "ab"));

        Assert.Empty(find.Matches);
    }

    [Fact]
    public void Reanchor_prefers_the_previous_position_then_falls_back_to_the_first()
    {
        var matches = new[]
        {
            ScreenRegion.FromCorners(0, 0, 0, 1),
            ScreenRegion.FromCorners(4, 7, 4, 8),
        };

        Assert.Equal(1, FindViewModel.Reanchor(matches, ScreenRegion.FromCorners(4, 7, 4, 8)));
        Assert.Equal(0, FindViewModel.Reanchor(matches, ScreenRegion.FromCorners(9, 9, 9, 9)));
        Assert.Equal(0, FindViewModel.Reanchor(matches, null));
        Assert.Equal(-1, FindViewModel.Reanchor([], ScreenRegion.FromCorners(0, 0, 0, 1)));
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~FindViewModelTests"`

Expected: FAIL to compile — `The type or namespace name 'FindViewModel' could not be found`.

- [ ] **Step 3: Write the implementation**

Create `src/LizTerm.App/ViewModels/FindViewModel.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using CommunityToolkit.Mvvm.ComponentModel;
using LizTerm.Core.Screen;

namespace LizTerm.App.ViewModels;

/// <summary>The find bar's state, for one session window.
///
/// Its own view model rather than more weight on SessionViewModel, which already owns the session, the
/// connection lifecycle, the certificate prompt, the clipboard, the wire log and the transfer factory. Find has
/// its own lifetime — it opens, it holds a term and a match list, it closes — and it names no Avalonia type, so
/// every rule here is assertable with a plain [Fact].</summary>
public sealed partial class FindViewModel : ObservableObject
{
    private readonly Func<int, int, Task> _moveCursor;
    private ScreenSnapshot? _snapshot;

    /// <summary>Whether the host cursor has been moved to the current match. Typing must not move it — that
    /// would send a MoveCursor per character — so a fresh search highlights match 0 without visiting it, and
    /// the first Enter visits it instead of skipping to match 1. A repaint recompute leaves this alone.</summary>
    private bool _visited;

    public FindViewModel(Func<int, int, Task> moveCursor) => _moveCursor = moveCursor;

    [ObservableProperty] private bool _isOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CountText))]
    private string _term = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CountText))]
    [NotifyPropertyChangedFor(nameof(CurrentMatch))]
    private IReadOnlyList<ScreenRegion> _matches = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CountText))]
    [NotifyPropertyChangedFor(nameof(CurrentMatch))]
    private int _currentIndex = -1;

    /// <summary>The match the user is on, painted distinctly from the rest.</summary>
    public ScreenRegion? CurrentMatch =>
        CurrentIndex >= 0 && CurrentIndex < Matches.Count ? Matches[CurrentIndex] : null;

    public string CountText =>
        string.IsNullOrWhiteSpace(Term) ? ""
        : Matches.Count == 0 ? "No matches"
        : $"{CurrentIndex + 1} of {Matches.Count}";

    partial void OnTermChanged(string value)
    {
        _visited = false;
        Refresh();
    }

    /// <summary>A new snapshot. Matches recompute rather than clear: a search term stays meaningful where a
    /// selected rectangle does not, and a 3270 screen repaints on every keystroke echo (spec 5.5). Called from
    /// SessionViewModel.ApplyScreen, so this is already on the UI thread and never on the render path.</summary>
    public void OnScreen(ScreenSnapshot snapshot)
    {
        _snapshot = snapshot;
        Refresh();
    }

    public void Open()
    {
        _visited = false;
        IsOpen = true;
        Refresh();
    }

    public void Close()
    {
        IsOpen = false;
        Matches = [];
        CurrentIndex = -1;
    }

    public Task NextAsync() => MoveAsync(1);

    public Task PreviousAsync() => MoveAsync(-1);

    /// <summary>The one place matches are computed, so "closed means no work and no stale highlight" is a
    /// single rule rather than a condition repeated at each caller. A shut bar must not pay a search on every
    /// host repaint.</summary>
    private void Refresh()
    {
        if (!IsOpen)
        {
            Matches = [];
            CurrentIndex = -1;
            return;
        }

        var previous = CurrentMatch;
        IReadOnlyList<ScreenRegion> matches = _snapshot is null ? [] : ScreenSearch.Find(_snapshot, Term);
        Matches = matches;
        CurrentIndex = Reanchor(matches, previous);
    }

    /// <summary>Which match to be on after a recompute: the one starting where the last one did, else the
    /// first, else none. Pure, so the rule is tested without a screen.</summary>
    internal static int Reanchor(IReadOnlyList<ScreenRegion> matches, ScreenRegion? previous)
    {
        if (matches.Count == 0) return -1;
        if (previous is { } anchor)
        {
            for (var i = 0; i < matches.Count; i++)
                if (matches[i].Top == anchor.Top && matches[i].Left == anchor.Left)
                    return i;
        }
        return 0;
    }

    private Task MoveAsync(int delta)
    {
        if (Matches.Count == 0) return Task.CompletedTask;

        if (_visited)
            CurrentIndex = ((CurrentIndex + delta) % Matches.Count + Matches.Count) % Matches.Count;
        _visited = true;

        var match = Matches[CurrentIndex];
        return _moveCursor(match.Top, match.Left);
    }
}
```

- [ ] **Step 4: Run it to verify it passes**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~FindViewModelTests"`

Expected: PASS, 14 tests.

- [ ] **Step 5: Commit**

```bash
git add src/LizTerm.App/ViewModels/FindViewModel.cs tests/LizTerm.App.Tests/ViewModels/FindViewModelTests.cs
git commit -m "Add the find view model (#26)"
```

---

## Task 7: Find matches paint (#26, part 3)

**Files:**
- Create: `tests/LizTerm.App.Tests/Controls/TerminalScreenFindTests.cs`
- Modify: `src/LizTerm.App/Rendering/Palette.cs`, `src/LizTerm.App/Controls/TerminalScreen.cs`

**Interfaces:**
- Consumes: `ScreenRegion`, `CellGeometry`, `Palette`.
- Produces: `public IReadOnlyList<ScreenRegion>? TerminalScreen.FindMatches` and `public ScreenRegion? TerminalScreen.CurrentMatch`, both styled properties. Task 8 binds them.

- [ ] **Step 1: Write the failing test**

Create `tests/LizTerm.App.Tests/Controls/TerminalScreenFindTests.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using LizTerm.App.Controls;
using LizTerm.Core.Screen;

namespace LizTerm.App.Tests.Controls;

public class TerminalScreenFindTests
{
    private static (Window Window, TerminalScreen Screen) Show()
    {
        var screen = new TerminalScreen { Snapshot = ScreenSnapshot.Empty(24, 80) };
        var window = new Window { Width = 800, Height = 600, Content = screen };
        window.Show();
        return (window, screen);
    }

    private static void Repaint(TerminalScreen screen)
    {
        screen.Measure(new Avalonia.Size(800, 600));
        screen.Arrange(new Avalonia.Rect(0, 0, 800, 600));
    }

    [AvaloniaFact]
    public void It_starts_with_no_matches()
    {
        var (_, screen) = Show();

        Assert.Null(screen.FindMatches);
        Assert.Null(screen.CurrentMatch);
    }

    /// <summary>The guard for the overlay rule (spec section 2). Matches are recomputed on every host repaint,
    /// so this is the hottest overlay in the app: if it re-segmented and re-shaped every cell it would do so on
    /// every keystroke echo.</summary>
    [AvaloniaFact]
    public void Setting_matches_does_not_rebuild_the_run_plan()
    {
        var (_, screen) = Show();
        Repaint(screen);
        var before = screen.RunPlanBuilds;
        Assert.True(before > 0, "the first render should have built a run plan");

        screen.FindMatches = [ScreenRegion.FromCorners(1, 1, 1, 4), ScreenRegion.FromCorners(2, 0, 2, 3)];
        screen.CurrentMatch = ScreenRegion.FromCorners(2, 0, 2, 3);
        Repaint(screen);

        Assert.Equal(before, screen.RunPlanBuilds);
    }

    /// <summary>A match list computed against a larger screen outlives it by one repaint whenever the host
    /// changes screen size. Clamping is what keeps that from throwing.</summary>
    [AvaloniaFact]
    public void Matches_outside_the_screen_are_clamped_rather_than_thrown_on()
    {
        var (_, screen) = Show();
        screen.FindMatches = [ScreenRegion.FromCorners(90, 90, 95, 99)];

        var thrown = Record.Exception(() => Repaint(screen));

        Assert.Null(thrown);
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~TerminalScreenFindTests"`

Expected: FAIL to compile — `'TerminalScreen' does not contain a definition for 'FindMatches'`.

- [ ] **Step 3: Add the brushes**

In `src/LizTerm.App/Rendering/Palette.cs`, after the `Crosshair` brush added in Task 4:

```csharp
    /// <summary>A find match, and the one the user is on. Amber rather than Selection's blue so a search over a
    /// selected region stays readable, and the current match is opaque enough to pick out of a screenful.</summary>
    public static readonly IBrush FindMatch = new ImmutableSolidColorBrush(Color.FromArgb(0x66, 0xE0, 0xA0, 0x20));
    public static readonly IBrush FindCurrent = new ImmutableSolidColorBrush(Color.FromArgb(0xAA, 0xFF, 0xD0, 0x40));
```

- [ ] **Step 4: Add the properties**

In `src/LizTerm.App/Controls/TerminalScreen.cs`, after `CrosshairProperty`:

```csharp
    /// <summary>Every find match on the current screen, or null. Recomputed by FindViewModel against each new
    /// snapshot and pushed in; the control only paints them.</summary>
    public static readonly StyledProperty<IReadOnlyList<ScreenRegion>?> FindMatchesProperty =
        AvaloniaProperty.Register<TerminalScreen, IReadOnlyList<ScreenRegion>?>(nameof(FindMatches));

    /// <summary>The match the user is on, painted distinctly from the others.</summary>
    public static readonly StyledProperty<ScreenRegion?> CurrentMatchProperty =
        AvaloniaProperty.Register<TerminalScreen, ScreenRegion?>(nameof(CurrentMatch));
```

And the accessors, beside `Crosshair`:

```csharp
    public IReadOnlyList<ScreenRegion>? FindMatches
    {
        get => GetValue(FindMatchesProperty);
        set => SetValue(FindMatchesProperty, value);
    }

    public ScreenRegion? CurrentMatch
    {
        get => GetValue(CurrentMatchProperty);
        set => SetValue(CurrentMatchProperty, value);
    }
```

And extend `AffectsRender` in the static constructor:

```csharp
        AffectsRender<TerminalScreen>(SnapshotProperty, SelectionProperty, CrosshairProperty,
            FindMatchesProperty, CurrentMatchProperty);
```

- [ ] **Step 5: Draw them**

In `Render`, insert the call between `DrawSelection` and `DrawCursor`, giving the final order from spec §2:

```csharp
        DrawCrosshair(context, snapshot, g);
        DrawSelection(context, snapshot, g);
        DrawFindMatches(context, snapshot, g);
        DrawCursor(context, snapshot, g);
```

And add the method beside `DrawSelection`:

```csharp
    /// <summary>An overlay, exactly as the selection is — never folded into the run plan. Matches arrive
    /// already recomputed for this snapshot, so a stale region here means only that the host has just resized
    /// the screen; Clamp answers that rather than throwing.</summary>
    private void DrawFindMatches(DrawingContext context, ScreenSnapshot snapshot, CellGeometry g)
    {
        if (FindMatches is not { Count: > 0 } matches) return;
        var current = CurrentMatch;

        foreach (var match in matches)
        {
            if (match.Clamp(snapshot.Rows, snapshot.Columns) is not { } region) continue;
            var topLeft = g.CellRect(region.Top, region.Left);
            var bottomRight = g.CellRect(region.Bottom, region.Right);
            var brush = match == current ? Palette.FindCurrent : Palette.FindMatch;
            context.FillRectangle(brush, new Rect(topLeft.TopLeft, bottomRight.BottomRight));
        }
    }
```

- [ ] **Step 6: Run it to verify it passes**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~TerminalScreenFindTests"`

Expected: PASS, 3 tests.

- [ ] **Step 7: Commit**

```bash
git add src/LizTerm.App tests/LizTerm.App.Tests
git commit -m "Paint find matches as an overlay (#26)"
```

---

## Task 8: The find bar and its gesture (#26, part 4)

**Files:**
- Modify: `src/LizTerm.App/Controls/TerminalScreen.cs` (`TryHandleClipboardKey` at `:195`, the events at `:134-137`)
- Modify: `src/LizTerm.App/ViewModels/SessionViewModel.cs` (constructor; `ApplyScreen` at `:233-238`)
- Modify: `src/LizTerm.App/Views/SessionWindow.axaml`, `src/LizTerm.App/Views/SessionWindow.axaml.cs` (`ShowPlatformGestures` at `:130`)
- Modify: `tests/LizTerm.App.Tests/Views/NativeMenuTests.cs`

**Interfaces:**
- Consumes: `FindViewModel` (Task 6); `TerminalScreen.FindMatches`/`CurrentMatch` (Task 7).
- Produces: `public event EventHandler? TerminalScreen.FindRequested`; `public FindViewModel SessionViewModel.Find`; `public bool SessionViewModel.CanFind`.

**The gesture has to be built, not read.** Avalonia 12.1.2's `PlatformHotkeyConfiguration` exposes `Copy`, `Paste`, `SelectAll`, `OpenContextMenu` and `CommandModifiers` — and no `Find`. Verified against `~/.nuget/packages/avalonia/12.1.2/ref/net10.0/Avalonia.Base.dll`. So it is built from `CommandModifiers`, which still derives Cmd-on-macOS / Ctrl-elsewhere from the platform rather than hardcoding it. `Ctrl+F` is free: `DefaultKeymap`'s complete set of Control chords is Ctrl+Enter, Ctrl+R, Ctrl+Escape, Ctrl+F1..F12, Ctrl+Home, Ctrl+PageUp, Ctrl+`[` and Ctrl+6.

**Find lives in Edit** because Edit is the one menu with a safe, established route for a `Gesture`: `ShowPlatformGestures` assigns it there, and everywhere else a native gesture is an AppKit key equivalent that would take the key from the terminal for good.

- [ ] **Step 1: Write the failing test**

In `tests/LizTerm.App.Tests/Views/NativeMenuTests.cs`, append:

```csharp
    [AvaloniaFact]
    public void The_edit_menu_offers_find()
    {
        var (window, _, _, _) = Show();

        Assert.True(Item(window, "_Edit", "_Find...").IsEnabled);
    }

    /// <summary>Cmd+F on macOS, Ctrl+F elsewhere, built from the platform's CommandModifiers because
    /// PlatformHotkeyConfiguration carries no Find of its own (spec 5.3).</summary>
    [AvaloniaFact]
    public void Find_carries_the_platform_gesture()
    {
        var (window, _, _, _) = Show();
        // The same extension the production code uses; TopLevel.PlatformSettings is an explicit interface
        // implementation in 12.1.2 and is not reachable as a plain property.
        var expected = window.GetPlatformSettings()!.HotkeyConfiguration.CommandModifiers;

        var gesture = Item(window, "_Edit", "_Find...").Gesture;

        Assert.NotNull(gesture);
        Assert.Equal(Key.F, gesture!.Key);
        Assert.Equal(expected, gesture.KeyModifiers);
    }

    [AvaloniaFact]
    public void The_find_bar_opens_from_the_menu_and_focuses_its_box()
    {
        var (window, vm, _, _) = Show();

        ((INativeMenuItemExporterEventsImplBridge)Item(window, "_Edit", "_Find...")).RaiseClicked();

        Assert.True(vm.Find.IsOpen);
        Assert.True(window.FindControl<TextBox>("FindBox")!.IsFocused);
    }

    /// <summary>Escape closes the bar and hands focus back, the same path the error bar's Dismiss uses.</summary>
    [AvaloniaFact]
    public void Escape_closes_the_find_bar_and_returns_focus_to_the_screen()
    {
        var (window, vm, _, _) = Show();
        ((INativeMenuItemExporterEventsImplBridge)Item(window, "_Edit", "_Find...")).RaiseClicked();

        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);

        Assert.False(vm.Find.IsOpen);
        Assert.True(window.FindControl<TerminalScreen>("Screen")!.IsFocused);
    }

    /// <summary>Matches reach the control, so the overlay has something to paint.</summary>
    [AvaloniaFact]
    public void Find_matches_reach_the_terminal_screen()
    {
        var (window, vm, _, _) = Show();
        vm.Find.Open();

        vm.Find.Term = "hello";

        var screen = window.FindControl<TerminalScreen>("Screen")!;
        Assert.Equal(vm.Find.Matches, screen.FindMatches);
        Assert.Equal(vm.Find.CurrentMatch, screen.CurrentMatch);
    }
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~NativeMenuTests"`

Expected: FAIL — `no native menu item _Edit > _Find...`.

- [ ] **Step 3: Raise the gesture from the control**

In `src/LizTerm.App/Controls/TerminalScreen.cs`, add the event beside the other three (`:134-137`):

```csharp
    /// <summary>Raised for the platform's Find gesture. Checked here, ahead of Keymap.TryMap, for the same
    /// reason copy and paste are: the window owns what happens, the control owns only the keystroke.</summary>
    public event EventHandler? FindRequested;
```

Rename `TryHandleClipboardKey` to `TryHandlePlatformGesture` — it is no longer only about the clipboard — and add the find arm after the existing three:

```csharp
    private bool TryHandlePlatformGesture(KeyEventArgs e)
    {
        var hotkeys = this.GetPlatformSettings()?.HotkeyConfiguration;
        if (Matches(hotkeys?.Copy, e, Key.C)) { CopyRequested?.Invoke(this, EventArgs.Empty); return true; }
        if (Matches(hotkeys?.Paste, e, Key.V)) { PasteRequested?.Invoke(this, EventArgs.Empty); return true; }
        if (Matches(hotkeys?.SelectAll, e, Key.A)) { SelectAllRequested?.Invoke(this, EventArgs.Empty); return true; }

        // PlatformHotkeyConfiguration carries no Find, so this one is built rather than read. CommandModifiers
        // still supplies Cmd on macOS and Ctrl elsewhere, so nothing here is hardcoded per platform.
        if (e.Key == Key.F && e.KeyModifiers == (hotkeys?.CommandModifiers ?? KeyModifiers.Control))
        {
            FindRequested?.Invoke(this, EventArgs.Empty);
            return true;
        }
        return false;
    }
```

Update its one call site in `OnKeyDown` (`:145`) to the new name.

- [ ] **Step 4: Give the session view model its find state**

In `src/LizTerm.App/ViewModels/SessionViewModel.cs`, add to the constructor, after the event handlers are wired and before `ApplyScreen(session.CurrentScreen)` is called (`:134`):

```csharp
        Find = new FindViewModel(MoveCursorAsync);
```

Add the property beside `Profile` and `Title`:

```csharp
    /// <summary>Find state for this window. Its own view model: see FindViewModel's own summary.</summary>
    public FindViewModel Find { get; }

    /// <summary>There is a screen to search. Like CanCaptureScreen, deliberately not IsConnected — find reads
    /// the snapshot and never needs an engine.</summary>
    public bool CanFind => Screen is not null;
```

Add `[NotifyPropertyChangedFor(nameof(CanFind))]` to the `_screen` field's attributes.

And in `ApplyScreen` (`:233`), push the new snapshot into it:

```csharp
    private void ApplyScreen(ScreenSnapshot snapshot)
    {
        if (_disposed) return;
        Screen = snapshot;
        CursorText = StatusFormatter.Cursor(snapshot.Cursor);
        Find.OnScreen(snapshot);
    }
```

- [ ] **Step 5: Add the menu item and the bar**

In `src/LizTerm.App/Views/SessionWindow.axaml`, native Edit menu — after `Copy Screen as _HTML`:

```xml
            <NativeMenuItemSeparator />
            <NativeMenuItem Header="_Find..." Click="OnFindClickNative" IsEnabled="{Binding CanFind}" />
```

Classic Edit menu — the same position:

```xml
        <Separator />
        <MenuItem x:Name="FindMenuItem" Header="_Find..." Click="OnFindClick" IsEnabled="{Binding CanFind}" />
```

Then the bar itself, declared **after** the error-message `Border` and **before** the `TerminalScreen`, so it stacks directly beneath the screen and above both the error and status bars:

```xml
    <Border DockPanel.Dock="Bottom" Background="#202020" Padding="10,6"
            IsVisible="{Binding Find.IsOpen}">
      <DockPanel>
        <Button DockPanel.Dock="Right" Content="Close" Click="OnFindCloseClick" Margin="4,0,0,0" />
        <Button DockPanel.Dock="Right" Content="Next" Click="OnFindNextClick" Margin="4,0,0,0" />
        <Button DockPanel.Dock="Right" Content="Previous" Click="OnFindPreviousClick" Margin="4,0,0,0" />
        <TextBlock DockPanel.Dock="Right" Text="{Binding Find.CountText}" Foreground="#C0C0C0"
                   VerticalAlignment="Center" Margin="10,0" MinWidth="80" />
        <TextBox x:Name="FindBox" Text="{Binding Find.Term, Mode=TwoWay}" KeyDown="OnFindBoxKeyDown"
                 Watermark="Find on screen" />
      </DockPanel>
    </Border>
```

And bind the two overlay properties on the `TerminalScreen` element:

```xml
    <controls:TerminalScreen x:Name="Screen"
                             Snapshot="{Binding Screen}"
                             Selection="{Binding Selection, Mode=TwoWay}"
                             Crosshair="{Binding Crosshair}"
                             FindMatches="{Binding Find.Matches}"
                             CurrentMatch="{Binding Find.CurrentMatch}"
                             DestructiveBackspace="{Binding Profile.DestructiveBackspace}" />
```

- [ ] **Step 6: Wire the window**

In `src/LizTerm.App/Views/SessionWindow.axaml.cs`, subscribe beside the other screen events (`:29`):

```csharp
        Screen.FindRequested += (_, _) => ShowFind();
```

Register Find's gesture inside `ShowPlatformGestures` (`:130`), after the three existing ones:

```csharp
        // Built rather than read: PlatformHotkeyConfiguration has no Find. CommandModifiers still gives Cmd on
        // macOS and Ctrl elsewhere.
        var find = new KeyGesture(Key.F, hotkeys.CommandModifiers);
        FindMenuItem.InputGesture = find;
        Gesture(menu, "_Find...", find);
```

And add the handlers:

```csharp
    private void OnFindClick(object? sender, RoutedEventArgs e) => ShowFind();
    private void OnFindClickNative(object? sender, EventArgs e) => ShowFind();

    /// <summary>Opens the bar and puts the caret in it. Focus is the whole point of a docked bar over a modal
    /// dialog: the screen being searched stays visible behind it.</summary>
    private void ShowFind()
    {
        if (ViewModel is not { } vm) return;
        vm.Find.Open();
        FindBox.Focus();
        FindBox.SelectAll();
    }

    private void CloseFind()
    {
        ViewModel?.Find.Close();
        Screen.Focus();
    }

    private void OnFindCloseClick(object? sender, RoutedEventArgs e) => CloseFind();
    private void OnFindNextClick(object? sender, RoutedEventArgs e) => _ = ViewModel?.Find.NextAsync();
    private void OnFindPreviousClick(object? sender, RoutedEventArgs e) => _ = ViewModel?.Find.PreviousAsync();

    /// <summary>Enter walks forward, Shift+Enter back, Escape closes. Handled on the box rather than on the
    /// window so a keystroke meant for the terminal is never taken while the bar is shut.</summary>
    private void OnFindBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (ViewModel is not { } vm) return;
        switch (e.Key)
        {
            case Key.Escape:
                CloseFind();
                e.Handled = true;
                break;
            case Key.Enter:
                _ = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? vm.Find.PreviousAsync() : vm.Find.NextAsync();
                e.Handled = true;
                break;
        }
    }
```

- [ ] **Step 7: Run the App suite to verify it passes**

Run: `dotnet test tests/LizTerm.App.Tests`

Expected: PASS. The parity guard covers the new Edit items and their separators in both menus.

- [ ] **Step 8: Commit**

```bash
git add src/LizTerm.App tests/LizTerm.App.Tests
git commit -m "Add the find bar, and reach it with the platform's Find gesture (#26)"
```

---

## Task 9: Full verification

**Files:** none changed unless a check fails.

- [ ] **Step 1: Run the whole suite**

Run: `dotnet test LizTerm.slnx`

Expected: PASS. Live-host tests in `LizTerm.Integration.Tests` skip themselves without `LIZTERM_TEST_HOST`.

- [ ] **Step 2: Check for warnings**

Run: `dotnet build LizTerm.slnx --no-incremental 2>&1 | grep -c " warning "`

Expected: `0`. An incremental build hides warnings from projects it did not recompile, so `--no-incremental` is the check that counts.

- [ ] **Step 3: Confirm the license headers**

Run: `dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~RepositoryHeadersTests"`

Expected: PASS. Every `.cs` and `.axaml` file created in this plan carries the three-line header.

- [ ] **Step 4: Manual pass on macOS against a live host**

CI cannot answer any of these. Build and run against a real TN3270 host:

```bash
dotnet build src/LizTerm.App
```

Then launch a session and check, in order:

1. **Crosshair.** All four View modes render, and host text stays readable through the bars. Confirm the crosshair still shows on a screen where the cursor is hidden.
2. **`Radio` rendering.** Already observed in Task 5 step 8; confirm the fallback, if one was taken, still shows exactly one mark.
3. **Find.** `Cmd+F` opens the bar and focuses it. Typing highlights matches and the count reads correctly. Enter walks matches and **moves the host cursor** on a real ISPF panel; Shift+Enter walks back; both wrap. Escape closes the bar and the next keystroke reaches the terminal.
4. **Find under repaint.** With the bar open and matches showing, type into the host screen. Matches recompute and the highlight does not vanish.
5. **Capture.** `File > Save Screen As...` with a `.txt` name, and again with a `.html` name. Open the HTML in a browser: colours, bold and underline match the window it was taken from. `Edit > Copy Screen as HTML` and paste into a browser or editor.
6. **Capture while disconnected.** Disconnect the session, then confirm both capture items are still enabled and still work.

- [ ] **Step 5: Commit anything the manual pass corrected**

```bash
git add -A
git commit -m "Correct <what the manual pass found>"
```

If the manual pass found nothing, skip this step.

---

## Notes for the executor

- **The dependency rule is why `ScreenSearch` is in Core and `ScreenHtml` is not.** Core names no Avalonia type; the HTML formatter needs `Palette`'s colours, which are `Avalonia.Media.Color`. Do not "tidy" this by moving `Palette`'s table into Core — that is a larger change than this milestone earns and would leave `Palette` a thin forwarder.
- **Do not fold any overlay into the run plan.** Two tasks assert `RunPlanBuilds` does not move when an overlay property changes. If you find yourself passing a match list or a crosshair mode into `BuildRun`, stop: that re-shapes every cell on the screen on every keystroke in the find box, and again twice a second while anything blinks.
- **Both menus change together, always.** `The_native_menu_matches_the_classic_menu_item_for_item` compares headers, separator positions and Commands. A separator added to one menu and not the other fails it, and that is the guard doing its job.
- **`ToggleType="Radio"` may not survive contact with the macOS exporter.** The fallback is decided in advance (Task 5, step 8) and is a one-word change in four places. Do not redesign the View menu around it.
- **The engine is not involved anywhere in this plan.** If a task seems to need `IEmulatorSession`, `B3270Session` or `FakeEmulatorSession` to change, re-read spec §3.1 — that is the decision being undone.
