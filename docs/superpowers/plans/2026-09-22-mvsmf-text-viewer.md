# mvsMF Access text viewer Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A **View** verb in mvsMF Access's Members pane that opens an owned, read-only window showing a member's
(or a sequential dataset's) records as text, with a line-number gutter and find.

**Architecture:** `MvsmfBrowserViewModel` does the read through its existing `RunExclusiveAsync` — so progress,
Cancel, the error banner, Retry, the sign-in prompt and the certificate prompt are the ones already built — and
hands finished lines to a `MvsmfViewerViewModel`. That view model makes no host call and names no Avalonia type.
`MvsmfViewerWindow` presents it, owned by the browser window through `ModalDialogs.ShowAbove`, one at a time,
reused for a later View.

**Tech Stack:** .NET 10, C# 13, Avalonia 12.1.2, CommunityToolkit.Mvvm (`[ObservableProperty]`, `[RelayCommand]`),
xunit.v3 in VSTest mode, Avalonia.Headless.XUnit for window tests.

**Spec:** `docs/superpowers/specs/2026-09-22-mvsmf-text-viewer-design.md` — read it alongside this plan.

## Global Constraints

- **Licence headers.** Every new `.cs` and `.axaml` file starts with three lines in its comment syntax:
  `This file is part of LizTerm.`, `Copyright 2026 by CoffeeMuse`, `SPDX-License-Identifier: BSD-3-Clause`.
  In `.axaml` they go in an XML comment before the root element. `RepositoryHeadersTests` fails the suite without
  them.
- **Dependency rule.** `LizTerm.Core` never mentions Avalonia, b3270 or mvsMF. `LizTerm.App` talks to
  `IHostFileService`, never to `LizTerm.Backend.Mvsmf` (only `HostFileServiceFactory.cs` names it). Nothing in
  this plan changes that.
- **Zero warnings.** CI builds with `-warnaserror`. Before calling the work done:
  `dotnet build LizTerm.slnx --no-incremental 2>&1 | grep -c " warning "` must print `0`.
- **Nullable and ImplicitUsings are on** solution-wide; package versions live only in `Directory.Packages.props`.
- **Every status that reports an outcome, a warning or progress starts with a mark and words** (`✓ ✗ ⚠ ⟳ –`).
  Plain counts carry no mark. Colour never carries meaning alone.
- **Assertions on user-visible strings are exact.** Copy the strings in this plan character for character,
  including the `·` separators and the `…` ellipsis.
- **Commit after every task.** End each commit message with
  `Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>`.
- **Run the suite** with `dotnet test LizTerm.slnx` (about 4 s warm); a single project with
  `dotnet test tests/LizTerm.App.Tests`; a single test with
  `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~MvsmfViewerViewModelTests"`.

## File Structure

| File | Responsibility |
|---|---|
| `src/LizTerm.Core/HostFiles/HostFileTransfer.cs` (modify) | `DownloadOptions.Format(line)` — the one home for the trailing-blank rule |
| `src/LizTerm.App/ViewModels/MvsmfViewerViewModel.cs` (create) | The viewer's whole state: text, line numbers, the cap, the footer, find. No host call, no Avalonia type |
| `src/LizTerm.App/ViewModels/MvsmfBrowserViewModel.View.cs` (create) | The View verb: `CanView`, `ViewHint`, `ViewCommand`, the read, the `Viewer` property |
| `src/LizTerm.App/ViewModels/MvsmfBrowserViewModel.cs` (modify) | `ViewCommand` in `NotifyCommands()`, `ViewHint` on the `SelectedDataset` notifications |
| `src/LizTerm.App/Views/MvsmfViewerWindow.axaml(.cs)` (create) | The window: find row, gutter, text area, footer; gutter scroll sync, scroll-to-match, keys |
| `src/LizTerm.App/Views/MvsmfBrowserWindow.axaml(.cs)` (modify) | The View button and menu item, the gesture, the sequential note, opening and reusing the viewer |
| `tests/LizTerm.Core.Tests/HostFiles/HostFileTransferTests.cs` (modify) | `Format` both ways |
| `tests/LizTerm.App.Tests/ViewModels/MvsmfViewerViewModelTests.cs` (create) | Text, cap, footer, line numbers, find |
| `tests/LizTerm.App.Tests/ViewModels/MvsmfBrowserViewTests.cs` (create) | The verb's rules and the read |
| `tests/LizTerm.App.Tests/Views/MvsmfViewerWindowTests.cs` (create) | The window's own behaviour |
| `tests/LizTerm.App.Tests/Views/MvsmfBrowserWindowTests.cs` (modify) | The button, the menu, the gesture, opening and reusing the viewer |
| `docs/user-guide.md`, `CHANGELOG.md`, `src/LizTerm.App/CLAUDE.md` (modify) | The three documentation homes |

---

### Task 1: Core — one home for the trailing-blank rule

**Files:**
- Modify: `src/LizTerm.Core/HostFiles/HostFileTransfer.cs` (the `DownloadOptions` record at the top, and
  `FormatText` near the bottom)
- Test: `tests/LizTerm.Core.Tests/HostFiles/HostFileTransferTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `DownloadOptions.Format(string line) → string`. Task 2 calls it; `FormatText` calls it too, so the
  file a download writes and the text the viewer shows trim identically.

- [ ] **Step 1: Write the failing tests**

Add to `tests/LizTerm.Core.Tests/HostFiles/HostFileTransferTests.cs`, beside the existing `FormatText` test:

```csharp
    [Fact]
    public void Format_trims_trailing_blanks_when_the_option_is_on() =>
        Assert.Equal("//HELLO JOB", new DownloadOptions(HostTransferMode.Text).Format("//HELLO JOB   "));

    [Fact]
    public void Format_keeps_them_when_it_is_off() =>
        Assert.Equal("//HELLO JOB   ",
            new DownloadOptions(HostTransferMode.Text, TrimTrailingBlanks: false).Format("//HELLO JOB   "));

    [Fact]
    public void Format_leaves_leading_blanks_and_tabs_alone() =>
        Assert.Equal("\t  indented\t", new DownloadOptions(HostTransferMode.Text).Format("\t  indented\t  "));
```

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~HostFileTransferTests.Format"`
Expected: FAIL — the build errors with `'DownloadOptions' does not contain a definition for 'Format'`.

- [ ] **Step 3: Implement**

In `src/LizTerm.Core/HostFiles/HostFileTransfer.cs`, give the record a body:

```csharp
public sealed record DownloadOptions(HostTransferMode Mode, bool TrimTrailingBlanks = true, string? LineEnding = null)
{
    /// <summary>One record as it should read locally. The one home for the trimming rule: the file a download
    /// writes (<see cref="HostFileTransfer.FormatText"/>) and the text the viewer shows both go through it, so
    /// they can never disagree. Only blanks: a tab is content the host sent.</summary>
    public string Format(string line) => TrimTrailingBlanks ? line.TrimEnd(' ') : line;
}
```

and change `FormatText`'s loop body to use it:

```csharp
        foreach (var line in lines) text.Append(options.Format(line)).Append(ending);
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~HostFileTransferTests"`
Expected: PASS, every test in the class, including the existing `FormatText` ones.

- [ ] **Step 5: Commit**

```bash
git add src/LizTerm.Core/HostFiles/HostFileTransfer.cs tests/LizTerm.Core.Tests/HostFiles/HostFileTransferTests.cs
git commit -m "$(cat <<'EOF'
Core: one home for the trailing-blank rule

DownloadOptions.Format is what FormatText now calls, and what the mvsMF
viewer will call, so the file a download writes and the text on screen
cannot drift apart.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 2: The viewer's state — text, the cap, line numbers, the footer

**Files:**
- Create: `src/LizTerm.App/ViewModels/MvsmfViewerViewModel.cs`
- Test: `tests/LizTerm.App.Tests/ViewModels/MvsmfViewerViewModelTests.cs`

**Interfaces:**
- Consumes: `DownloadOptions.Format` (Task 1).
- Produces: `MvsmfViewerViewModel(string path, IReadOnlyList<string> lines, bool trimTrailingBlanks)` with
  `Path`, `Title`, `Text`, `LineCount`, `LineNumbers`, `IsTruncated`, `FooterText`, `ShowLineNumbers` (settable,
  default true) and the constant `MaxLines = 10_000`. Task 3 adds find to the same class; Task 4 constructs it;
  Tasks 5 and 6 bind it.

- [ ] **Step 1: Write the failing tests**

Create `tests/LizTerm.App.Tests/ViewModels/MvsmfViewerViewModelTests.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.App.ViewModels;

namespace LizTerm.App.Tests.ViewModels;

public sealed class MvsmfViewerViewModelTests
{
    private static MvsmfViewerViewModel Viewer(params string[] lines) =>
        new("MVSCE02.CNTL(HELLO)", lines, trimTrailingBlanks: true);

    [Fact]
    public void The_title_names_the_path()
    {
        Assert.Equal("MVSCE02.CNTL(HELLO) — mvsMF Access", Viewer("//HELLO JOB").Title);
        Assert.Equal("MVSCE02.CNTL(HELLO)", Viewer("//HELLO JOB").Path);
    }

    [Fact]
    public void The_text_is_the_records_one_to_a_line_with_trailing_blanks_trimmed()
    {
        var viewer = Viewer("//HELLO JOB   ", "//STEP EXEC PGM=IEFBR14");

        Assert.Equal("//HELLO JOB\n//STEP EXEC PGM=IEFBR14", viewer.Text);
        Assert.Equal(2, viewer.LineCount);
        Assert.False(viewer.IsTruncated);
    }

    [Fact]
    public void Trailing_blanks_can_be_kept()
    {
        var viewer = new MvsmfViewerViewModel("MVSCE02.NOTES", ["//HELLO JOB   "], trimTrailingBlanks: false);

        Assert.Equal("//HELLO JOB   ", viewer.Text);
        Assert.Equal("1 line", viewer.FooterText);
    }

    [Fact]
    public void The_footer_counts_the_lines_and_says_what_was_done_to_them()
    {
        Assert.Equal("1 line · trailing blanks trimmed", Viewer("A").FooterText);
        Assert.Equal("2 lines · trailing blanks trimmed", Viewer("A", "B").FooterText);
    }

    [Fact]
    public void An_empty_member_is_no_lines_and_no_numbers()
    {
        var viewer = Viewer();

        Assert.Equal("", viewer.Text);
        Assert.Equal("", viewer.LineNumbers);
        Assert.Equal("0 lines · trailing blanks trimmed", viewer.FooterText);
    }

    [Fact]
    public void The_gutter_numbers_every_line_shown()
    {
        Assert.Equal("1\n2\n3", Viewer("A", "B", "C").LineNumbers);
        Assert.True(Viewer("A").ShowLineNumbers);
    }

    [Fact]
    public void Past_the_cap_the_first_lines_are_shown_and_the_footer_says_so()
    {
        var lines = Enumerable.Range(1, MvsmfViewerViewModel.MaxLines + 1).Select(n => $"LINE {n}").ToArray();

        var viewer = Viewer(lines);

        Assert.True(viewer.IsTruncated);
        Assert.Equal(MvsmfViewerViewModel.MaxLines, viewer.LineCount);
        Assert.EndsWith("LINE 10000", viewer.Text);
        Assert.DoesNotContain("LINE 10001", viewer.Text);
        Assert.Equal("⚠ Showing the first 10,000 lines of 10,001. Download the member to read it all.",
            viewer.FooterText);
    }
}
```

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~MvsmfViewerViewModelTests"`
Expected: FAIL — the build errors with `The type or namespace name 'MvsmfViewerViewModel' could not be found`.

- [ ] **Step 3: Implement**

Create `src/LizTerm.App/ViewModels/MvsmfViewerViewModel.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using LizTerm.Core.HostFiles;

namespace LizTerm.App.ViewModels;

/// <summary>The read-only text viewer (viewer spec §6). It is handed finished lines: no connection, no host call
/// and no Avalonia type, so every rule here is assertable with a plain [Fact]. One instance per View; the window
/// showing it is reused.</summary>
public sealed partial class MvsmfViewerViewModel : ObservableObject
{
    /// <summary>The most lines the text box is asked to lay out. mvsMF has no ranged read, so the whole member
    /// arrives whatever this is; the cap is only what Avalonia's TextBox, which builds one layout for the lot,
    /// is asked to draw (spec §6.4).</summary>
    public const int MaxLines = 10_000;

    private readonly int _totalLines;

    public MvsmfViewerViewModel(string path, IReadOnlyList<string> lines, bool trimTrailingBlanks)
    {
        Path = path;
        _totalLines = lines.Count;
        TrimmedTrailingBlanks = trimTrailingBlanks;
        // Text mode is the only mode a viewer has; the option that matters is the trimming, and Format is the
        // rule a download writes to a file with (Core's HostFileTransfer).
        var options = new DownloadOptions(HostTransferMode.Text, trimTrailingBlanks);
        LineCount = Math.Min(_totalLines, MaxLines);
        Text = string.Join("\n", lines.Take(MaxLines).Select(options.Format));
        LineNumbers = string.Join("\n", Enumerable.Range(1, LineCount).Select(Count));
    }

    /// <summary>The host path as display text, never a HostPath: a USS file uses this window unchanged (spec §12).</summary>
    public string Path { get; }

    public string Title => $"{Path} — mvsMF Access";

    public string Text { get; }

    /// <summary>The lines shown, which the cap may make fewer than the host sent.</summary>
    public int LineCount { get; }

    /// <summary>The gutter's contents: one number per line of <see cref="Text"/>, in the same order.</summary>
    public string LineNumbers { get; }

    public bool TrimmedTrailingBlanks { get; }

    public bool IsTruncated => _totalLines > LineCount;

    public string FooterText => IsTruncated
        ? $"⚠ Showing the first {Count(MaxLines)} lines of {Count(_totalLines)}. Download the member to read it all."
        : (LineCount == 1 ? "1 line" : $"{Count(LineCount)} lines")
          + (TrimmedTrailingBlanks ? " · trailing blanks trimmed" : "");

    [ObservableProperty] private bool _showLineNumbers = true;

    private static string Count(int value) => value.ToString("N0", CultureInfo.InvariantCulture);
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~MvsmfViewerViewModelTests"`
Expected: PASS, seven tests.

- [ ] **Step 5: Commit**

```bash
git add src/LizTerm.App/ViewModels/MvsmfViewerViewModel.cs tests/LizTerm.App.Tests/ViewModels/MvsmfViewerViewModelTests.cs
git commit -m "$(cat <<'EOF'
mvsMF Access: the text viewer's state

Finished lines in, text and a gutter out, capped at 10,000 lines because
Avalonia's TextBox lays out the whole of what it is given. No host call and
no Avalonia type, so it is all plain [Fact].

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 3: Find in the viewer

**Files:**
- Modify: `src/LizTerm.App/ViewModels/MvsmfViewerViewModel.cs`
- Test: `tests/LizTerm.App.Tests/ViewModels/MvsmfViewerViewModelTests.cs`

**Interfaces:**
- Consumes: `Text` (Task 2).
- Produces: `Term` (settable), `Matches` (`IReadOnlyList<int>`, start offsets into `Text`), `CurrentIndex`,
  `MatchStart` (`int?`), `MatchLength` (`int`), `MatchLine` (`int?`, the 0-based line of the current match),
  `CountText`, `FindNextCommand`, `FindPreviousCommand`. Task 5's window binds and follows them.

- [ ] **Step 1: Write the failing tests**

Add to `tests/LizTerm.App.Tests/ViewModels/MvsmfViewerViewModelTests.cs`:

```csharp
    private static MvsmfViewerViewModel Jcl() =>
        Viewer("//HELLO JOB", "//STEP EXEC PGM=IEFBR14", "//SYSIN DD *", "hello again");

    [Fact]
    public void An_empty_term_finds_nothing_and_says_nothing()
    {
        var viewer = Jcl();

        Assert.Empty(viewer.Matches);
        Assert.Equal(-1, viewer.CurrentIndex);
        Assert.Null(viewer.MatchStart);
        Assert.Equal("", viewer.CountText);
    }

    [Fact]
    public void A_term_finds_every_match_ignoring_case_and_lands_on_the_first()
    {
        var viewer = Jcl();

        viewer.Term = "HELLO";

        Assert.Equal(2, viewer.Matches.Count);
        Assert.Equal(0, viewer.CurrentIndex);
        Assert.Equal(2, viewer.MatchStart);
        Assert.Equal(5, viewer.MatchLength);
        Assert.Equal(0, viewer.MatchLine);
        Assert.Equal("1 of 2", viewer.CountText);
    }

    [Fact]
    public void Next_and_previous_step_through_the_matches_and_wrap()
    {
        var viewer = Jcl();
        viewer.Term = "hello";

        viewer.FindNextCommand.Execute(null);
        Assert.Equal("2 of 2", viewer.CountText);
        Assert.Equal(3, viewer.MatchLine);

        viewer.FindNextCommand.Execute(null);
        Assert.Equal("1 of 2", viewer.CountText);

        viewer.FindPreviousCommand.Execute(null);
        Assert.Equal("2 of 2", viewer.CountText);
    }

    [Fact]
    public void A_term_with_no_match_says_so_and_steps_nowhere()
    {
        var viewer = Jcl();

        viewer.Term = "COBOL";

        Assert.Empty(viewer.Matches);
        Assert.Equal("No matches", viewer.CountText);
        Assert.Null(viewer.MatchStart);
        viewer.FindNextCommand.Execute(null);
        Assert.Equal("No matches", viewer.CountText);
    }

    [Fact]
    public void Clearing_the_term_clears_the_matches()
    {
        var viewer = Jcl();
        viewer.Term = "HELLO";

        viewer.Term = "";

        Assert.Empty(viewer.Matches);
        Assert.Equal("", viewer.CountText);
    }

    [Fact]
    public void Matches_do_not_overlap()
    {
        var viewer = Viewer("AAAA");

        viewer.Term = "AA";

        Assert.Equal(new[] { 0, 2 }, viewer.Matches);
    }

    [Fact]
    public void Find_searches_only_what_is_shown()
    {
        var lines = Enumerable.Range(1, MvsmfViewerViewModel.MaxLines + 1).Select(n => $"LINE {n}").ToArray();
        var viewer = Viewer(lines);

        viewer.Term = "LINE 10001";

        Assert.Equal("No matches", viewer.CountText);
    }
```

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~MvsmfViewerViewModelTests"`
Expected: FAIL — the build errors with `does not contain a definition for 'Term'`.

- [ ] **Step 3: Implement**

Add to `src/LizTerm.App/ViewModels/MvsmfViewerViewModel.cs`, inside the class, after `ShowLineNumbers`, and add
`using CommunityToolkit.Mvvm.Input;` to the usings:

```csharp
    /// <summary>The find term. Typing recomputes the matches and lands on the first; the window scrolls to it.
    /// FindViewModel's shape without its screen types — that one searches a ScreenSnapshot.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CountText), nameof(MatchLength))]
    private string _term = "";

    /// <summary>Where each match starts in <see cref="Text"/>, in order, never overlapping.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CountText), nameof(MatchStart), nameof(MatchLine))]
    private IReadOnlyList<int> _matches = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CountText), nameof(MatchStart), nameof(MatchLine))]
    private int _currentIndex = -1;

    public int? MatchStart => CurrentIndex >= 0 && CurrentIndex < Matches.Count ? Matches[CurrentIndex] : null;

    public int MatchLength => Term.Length;

    /// <summary>The 0-based line the current match starts on, so the window can scroll by line rather than by
    /// caret: the text box is not focused while the user types in the find box.</summary>
    public int? MatchLine => MatchStart is { } start ? Text.Take(start).Count(c => c == '\n') : null;

    public string CountText => Term.Length == 0 ? ""
        : Matches.Count == 0 ? "No matches"
        : $"{(CurrentIndex + 1).ToString(CultureInfo.InvariantCulture)} of {Count(Matches.Count)}";

    [RelayCommand]
    private void FindNext() => Step(1);

    [RelayCommand]
    private void FindPrevious() => Step(-1);

    /// <summary>Wraps, so the last match's Next is the first.</summary>
    private void Step(int by)
    {
        if (Matches.Count == 0) return;
        CurrentIndex = ((CurrentIndex + by) % Matches.Count + Matches.Count) % Matches.Count;
    }

    partial void OnTermChanged(string value)
    {
        Matches = FindAll(Text, value);
        CurrentIndex = Matches.Count > 0 ? 0 : -1;
    }

    private static IReadOnlyList<int> FindAll(string text, string term)
    {
        if (term.Length == 0) return [];
        var found = new List<int>();
        for (var at = 0; at <= text.Length - term.Length;)
        {
            var next = text.IndexOf(term, at, StringComparison.OrdinalIgnoreCase);
            if (next < 0) break;
            found.Add(next);
            at = next + term.Length;
        }
        return found;
    }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~MvsmfViewerViewModelTests"`
Expected: PASS, fourteen tests.

- [ ] **Step 5: Commit**

```bash
git add src/LizTerm.App/ViewModels/MvsmfViewerViewModel.cs tests/LizTerm.App.Tests/ViewModels/MvsmfViewerViewModelTests.cs
git commit -m "$(cat <<'EOF'
mvsMF Access: find in the text viewer

Case-insensitive, non-overlapping, wrapping both ways, and it reports the
match's line so the window can scroll by line instead of by caret — the text
box is not focused while the user types in the find box.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 4: The View verb and the read

**Files:**
- Create: `src/LizTerm.App/ViewModels/MvsmfBrowserViewModel.View.cs`
- Modify: `src/LizTerm.App/ViewModels/MvsmfBrowserViewModel.cs` (the `SelectedDataset` notification list; the
  `NotifyCommands()` body)
- Test: `tests/LizTerm.App.Tests/ViewModels/MvsmfBrowserViewTests.cs`

**Interfaces:**
- Consumes: `MvsmfViewerViewModel(path, lines, trimTrailingBlanks)` (Task 2); the existing private members
  `RunExclusiveAsync`, `_connection.RunAsync`, `RowProgress`, `Bytes`, `Plural`, `_selectedMembers`,
  `SelectedDataset`, `TrimTrailingBlanks`, `StatusText`.
- Produces: `ViewCommand` (an `IAsyncRelayCommand`), `ViewHint` (string), `ViewGestureText` (settable string) and
  `Viewer` (`MvsmfViewerViewModel?`, `[ObservableProperty]`). Tasks 5 and 6 consume all four.

- [ ] **Step 1: Write the failing tests**

Create `tests/LizTerm.App.Tests/ViewModels/MvsmfBrowserViewTests.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.Core.HostFiles;

namespace LizTerm.App.Tests.ViewModels;

public sealed class MvsmfBrowserViewTests
{
    /// <summary>The standard seed plus a sequential dataset whose records are text: the standard one
    /// (MVSCE02.UFSHOME) is RECFM U, which View is off for.</summary>
    private static async Task<BrowserTestHost> ChosenAsync(string dataset = "MVSCE02.CNTL")
    {
        var t = BrowserTestHost.Create(seed: host =>
        {
            BrowserTestHost.Standard(host);
            host.AddDataset("MVSCE02.NOTES", dsorg: "PS", recfm: "FB", lrecl: 80, blksize: 3120);
        });
        t.Host.Text["MVSCE02.CNTL(HELLO)"] = ["//HELLO JOB   ", "//STEP EXEC PGM=IEFBR14"];
        t.Host.Text["MVSCE02.NOTES"] = ["Notes on the batch run."];
        t.Host.Text["MVSCE02.LOAD(PROG)"] = ["not really text"];
        await t.ChooseAsync(dataset);
        return t;
    }

    [Fact]
    public async Task View_needs_exactly_one_member()
    {
        var t = await ChosenAsync();
        Assert.False(t.Vm.ViewCommand.CanExecute(null));

        t.Select("HELLO");
        Assert.True(t.Vm.ViewCommand.CanExecute(null));

        t.Select("HELLO", "ALLOC");
        Assert.False(t.Vm.ViewCommand.CanExecute(null));
    }

    [Fact]
    public async Task Viewing_a_member_reads_it_as_text_and_reports_the_lines()
    {
        var t = await ChosenAsync();
        t.Select("HELLO");

        await t.Vm.ViewCommand.ExecuteAsync(null);

        Assert.Contains("readtext:MVSCE02.CNTL(HELLO)", t.Host.CallsSnapshot());
        Assert.Equal("MVSCE02.CNTL(HELLO)", t.Vm.Viewer!.Path);
        Assert.Equal("//HELLO JOB\n//STEP EXEC PGM=IEFBR14", t.Vm.Viewer.Text);
        Assert.Equal("✓ Read MVSCE02.CNTL(HELLO) · 2 lines.", t.Vm.StatusText);
        Assert.Equal("", t.Vm.Members.Single(m => m.Name == "HELLO").Status);
    }

    [Fact]
    public async Task A_sequential_dataset_views_itself()
    {
        var t = await ChosenAsync("MVSCE02.NOTES");

        Assert.True(t.Vm.ViewCommand.CanExecute(null));
        await t.Vm.ViewCommand.ExecuteAsync(null);

        Assert.Contains("readtext:MVSCE02.NOTES", t.Host.CallsSnapshot());
        Assert.Equal("MVSCE02.NOTES", t.Vm.Viewer!.Path);
        Assert.Equal("✓ Read MVSCE02.NOTES · 1 line.", t.Vm.StatusText);
    }

    [Fact]
    public async Task View_is_off_for_a_dataset_whose_records_are_not_text_and_says_why()
    {
        var t = await ChosenAsync("MVSCE02.LOAD");
        t.Select("PROG");

        Assert.False(t.Vm.ViewCommand.CanExecute(null));
        Assert.Equal("MVSCE02.LOAD holds undefined-length records, which are not text.", t.Vm.ViewHint);
    }

    [Fact]
    public async Task The_hint_names_the_gesture_and_the_kind_of_target()
    {
        var t = await ChosenAsync();
        t.Vm.ViewGestureText = "Ctrl+Enter";
        Assert.Equal("View the selected member (Ctrl+Enter)", t.Vm.ViewHint);

        await t.ChooseAsync("MVSCE02.NOTES");
        Assert.Equal("View this dataset (Ctrl+Enter)", t.Vm.ViewHint);
    }

    [Fact]
    public async Task View_reads_text_even_while_the_transfer_mode_is_binary()
    {
        var t = await ChosenAsync();
        t.Select("HELLO");
        t.Vm.Mode = HostTransferMode.Binary;

        await t.Vm.ViewCommand.ExecuteAsync(null);

        Assert.Contains("readtext:MVSCE02.CNTL(HELLO)", t.Host.CallsSnapshot());
        Assert.DoesNotContain(t.Host.CallsSnapshot(), c => c.StartsWith("readbinary:"));
    }

    [Fact]
    public async Task View_honours_trim_trailing_blanks()
    {
        var t = await ChosenAsync();
        t.Select("HELLO");
        t.Vm.TrimTrailingBlanks = false;

        await t.Vm.ViewCommand.ExecuteAsync(null);

        Assert.StartsWith("//HELLO JOB   \n", t.Vm.Viewer!.Text);
    }

    [Fact]
    public async Task View_asks_for_no_stamp_so_the_memory_is_left_alone()
    {
        var t = await ChosenAsync();
        t.Host.Etags["MVSCE02.CNTL(HELLO)"] = "stamp-1";
        t.Select("HELLO");

        await t.Vm.ViewCommand.ExecuteAsync(null);

        Assert.Null(t.Access.Etags.TryGet(HostPath.ForMember("MVSCE02.CNTL", "HELLO")));
    }

    [Fact]
    public async Task A_connection_failure_banners_with_retry_and_opens_no_viewer()
    {
        var t = await ChosenAsync();
        t.Select("HELLO");
        t.Host.Failures["readtext:MVSCE02.CNTL(HELLO)"] =
            new HostFileException(HostFileErrorKind.Unreachable, "cannot reach the host (refused).");

        await t.Vm.ViewCommand.ExecuteAsync(null);

        Assert.True(t.Vm.HasError);
        Assert.True(t.Vm.CanRetry);
        Assert.Null(t.Vm.Viewer);
    }

    [Fact]
    public async Task A_read_that_fails_for_anything_else_is_the_status_line_and_opens_no_viewer()
    {
        var t = await ChosenAsync();
        t.Select("HELLO");
        t.Host.Failures["readtext:MVSCE02.CNTL(HELLO)"] =
            new HostFileException(HostFileErrorKind.CannotOpen, "x", 3);

        await t.Vm.ViewCommand.ExecuteAsync(null);

        Assert.False(t.Vm.HasError);
        Assert.StartsWith("✗ ", t.Vm.StatusText);
        Assert.Null(t.Vm.Viewer);
    }
}
```

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~MvsmfBrowserViewTests"`
Expected: FAIL — the build errors with `does not contain a definition for 'ViewCommand'`.

- [ ] **Step 3: Implement**

Create `src/LizTerm.App/ViewModels/MvsmfBrowserViewModel.View.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LizTerm.Core.HostFiles;

namespace LizTerm.App.ViewModels;

/// <summary>View (viewer spec §3, §4): one member, or a sequential dataset, read as text and handed to the
/// viewer window. Always Text, whatever the transfer drop-down says — viewing is reading a document — and the
/// transfer options contribute only the trimming.</summary>
public sealed partial class MvsmfBrowserViewModel
{
    /// <summary>The last View's state, null until one has been read. The window opens its viewer on the first one
    /// and reuses that window for each later one; a failed or cancelled read sets none.</summary>
    [ObservableProperty] private MvsmfViewerViewModel? _viewer;

    /// <summary>How this platform writes the View gesture (⌘⏎ or Ctrl+Enter). The window sets it once from the
    /// platform's hotkey configuration; the view model has no way to ask, and its tests pass their own.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ViewHint))]
    private string _viewGestureText = "";

    /// <summary>Exactly one member, unlike Download, which takes many: a viewer shows one document. A dataset
    /// whose records are undefined-length is a load library, and its members are object code.</summary>
    private bool CanView =>
        !IsBusy && !IsCreating && SelectedDataset is { IsSupported: true } dataset
        && dataset.Attributes.RecordFormat != RecordFormatFamily.Undefined
        && (dataset.IsSequential || _selectedMembers.Count == 1);

    /// <summary>The View button's tooltip, shown on the disabled button too (ToolTip.ShowOnDisabled), so the one
    /// state the user cannot work out from the selection says why it is off.</summary>
    public string ViewHint
    {
        get
        {
            if (SelectedDataset is { IsSupported: true, Attributes.RecordFormat: RecordFormatFamily.Undefined } notText)
                return $"{notText.Name} holds undefined-length records, which are not text.";
            var verb = SelectedDataset is { IsSequential: true } ? "View this dataset" : "View the selected member";
            return ViewGestureText.Length > 0 ? $"{verb} ({ViewGestureText})" : verb;
        }
    }

    [RelayCommand(CanExecute = nameof(CanView))]
    private Task ViewAsync() => RunExclusiveAsync(ViewCoreAsync, () => ViewAsync());

    private async Task ViewCoreAsync(CancellationToken token)
    {
        var dataset = SelectedDataset!;
        var path = dataset.IsSequential ? dataset.Path : _selectedMembers[0].Path;
        var progress = new RowProgress(_dispatch, bytes => StatusText = $"⟳ Reading {path} · {Bytes(bytes)} bytes");
        StatusText = $"⟳ Reading {path}…";
        // withEtag: false — a view can never write the content back, and a stamp costs the host a second pass over
        // it (browser spec §5.2). The stamp memory is left exactly as it was: a view is not a download.
        var read = await _connection.RunAsync(service => service.ReadTextAsync(path, progress, withEtag: false, token));
        progress.Close();
        // Progress and the result go on the status line, never on the member's row: a row saying "Done · n bytes"
        // after a view would read like a transfer that happened.
        Viewer = new MvsmfViewerViewModel(path.ToString(), read.Lines, TrimTrailingBlanks);
        StatusText = $"✓ Read {path} · {Plural(read.Lines.Count, "line")}.";
    }
}
```

In `src/LizTerm.App/ViewModels/MvsmfBrowserViewModel.cs`, add `nameof(ViewHint)` to the `SelectedDataset`
attribute so the tooltip follows the selection:

```csharp
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowMembers), nameof(ShowSequentialNote), nameof(ShowChooseHint), nameof(ChooseHint),
        nameof(MembersTitle), nameof(DatasetsFooter), nameof(MembersFooter), nameof(UploadHeader), nameof(ShowMemberPane),
        nameof(ViewHint))]
    private DatasetRow? _selectedDataset;
```

and add the command to `NotifyCommands()`, beside `DownloadCommand`:

```csharp
        ViewCommand.NotifyCanExecuteChanged();
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~MvsmfBrowserViewTests"`
Expected: PASS, ten tests.

Then run the whole App project to catch anything the new notification broke:
Run: `dotnet test tests/LizTerm.App.Tests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/LizTerm.App/ViewModels/MvsmfBrowserViewModel.View.cs src/LizTerm.App/ViewModels/MvsmfBrowserViewModel.cs tests/LizTerm.App.Tests/ViewModels/MvsmfBrowserViewTests.cs
git commit -m "$(cat <<'EOF'
mvsMF Access: the View verb and its read

One member, or a sequential dataset, read as text through the browser's own
RunExclusiveAsync, so progress, Cancel, the banner and Retry are the ones
already built. It asks for no ETag: a view can never write the content back,
and a stamp costs the host a second pass.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 5: The viewer window

**Files:**
- Create: `src/LizTerm.App/Views/MvsmfViewerWindow.axaml`
- Create: `src/LizTerm.App/Views/MvsmfViewerWindow.axaml.cs`
- Test: `tests/LizTerm.App.Tests/Views/MvsmfViewerWindowTests.cs`

**Interfaces:**
- Consumes: every property and command of `MvsmfViewerViewModel` (Tasks 2 and 3).
- Produces: `MvsmfViewerWindow`, a `Window` whose `DataContext` is an `MvsmfViewerViewModel`, with named controls
  `FindBox`, `MatchCount`, `PreviousButton`, `NextButton`, `LineNumbersBox`, `GutterScroller`, `Gutter`,
  `TextArea`, `FooterLine`. Task 6 shows it with `ShowAbove`.

- [ ] **Step 1: Write the failing tests**

Create `tests/LizTerm.App.Tests/Views/MvsmfViewerWindowTests.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using LizTerm.App.ViewModels;
using LizTerm.App.Views;

namespace LizTerm.App.Tests.Views;

public class MvsmfViewerWindowTests
{
    private static (MvsmfViewerWindow Window, MvsmfViewerViewModel Vm) Show(params string[] lines)
    {
        var vm = new MvsmfViewerViewModel("MVSCE02.CNTL(HELLO)", lines.Length > 0 ? lines : ["//HELLO JOB"],
            trimTrailingBlanks: true);
        var window = new MvsmfViewerWindow { DataContext = vm };
        window.Show();
        window.Activate();
        window.UpdateLayout();
        return (window, vm);
    }

    private static T Named<T>(Window window, string name) where T : Control => window.FindControl<T>(name)!;

    [AvaloniaFact]
    public void It_shows_the_text_the_numbers_and_the_footer()
    {
        var (window, vm) = Show("//HELLO JOB", "//STEP EXEC PGM=IEFBR14");

        Assert.Equal("MVSCE02.CNTL(HELLO) — mvsMF Access", window.Title);
        Assert.Equal(vm.Text, Named<TextBox>(window, "TextArea").Text);
        Assert.True(Named<TextBox>(window, "TextArea").IsReadOnly);
        Assert.Equal("1\n2", Named<TextBlock>(window, "Gutter").Text);
        Assert.Equal("2 lines · trailing blanks trimmed", Named<TextBlock>(window, "FooterLine").Text);
    }

    [AvaloniaFact]
    public void The_toggle_hides_the_gutter()
    {
        var (window, vm) = Show();
        Assert.True(Named<ScrollViewer>(window, "GutterScroller").IsVisible);

        vm.ShowLineNumbers = false;
        window.UpdateLayout();

        Assert.False(Named<ScrollViewer>(window, "GutterScroller").IsVisible);
    }

    [AvaloniaFact]
    public void A_match_is_selected_in_the_text()
    {
        var (window, vm) = Show("//HELLO JOB", "//STEP EXEC PGM=IEFBR14", "//SYSIN DD *", "hello again");

        vm.Term = "hello";
        window.UpdateLayout();

        var text = Named<TextBox>(window, "TextArea");
        Assert.Equal(2, text.SelectionStart);
        Assert.Equal(7, text.SelectionEnd);

        vm.FindNextCommand.Execute(null);
        window.UpdateLayout();
        Assert.Equal(vm.MatchStart, text.SelectionStart);
        Assert.Equal(vm.MatchStart + 5, text.SelectionEnd);
    }

    [AvaloniaFact]
    public void Enter_and_shift_enter_step_the_matches()
    {
        var (window, vm) = Show("//HELLO JOB", "hello again");
        vm.Term = "hello";

        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Assert.Equal("2 of 2", vm.CountText);

        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.Shift);
        Assert.Equal("1 of 2", vm.CountText);
    }

    [AvaloniaFact]
    public void Escape_closes_it()
    {
        var (window, _) = Show();

        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);

        Assert.False(window.IsVisible);
    }

    [AvaloniaFact]
    public void A_new_view_model_replaces_the_text_and_the_title()
    {
        var (window, _) = Show("//HELLO JOB");

        window.DataContext = new MvsmfViewerViewModel("MVSCE02.CNTL(ALLOC)", ["//ALLOC JOB"], trimTrailingBlanks: true);
        window.UpdateLayout();

        Assert.Equal("MVSCE02.CNTL(ALLOC) — mvsMF Access", window.Title);
        Assert.Equal("//ALLOC JOB", Named<TextBox>(window, "TextArea").Text);
    }

    [AvaloniaFact]
    public void The_line_number_choice_survives_a_second_view()
    {
        var (window, vm) = Show("//HELLO JOB");
        vm.ShowLineNumbers = false;

        var second = new MvsmfViewerViewModel("MVSCE02.CNTL(ALLOC)", ["//ALLOC JOB"], trimTrailingBlanks: true);
        window.DataContext = second;
        window.UpdateLayout();

        Assert.False(second.ShowLineNumbers);
        Assert.False(Named<ScrollViewer>(window, "GutterScroller").IsVisible);
    }
}
```

`KeyPressQwerty` is the headless helper the other window tests use; it is already imported through the test
project's `<Using>` items (`Avalonia.Headless`). If the compiler cannot see it, add
`using Avalonia.Headless;` to the file.

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~MvsmfViewerWindowTests"`
Expected: FAIL — the build errors with `The type or namespace name 'MvsmfViewerWindow' could not be found`.

- [ ] **Step 3: Implement the window**

Create `src/LizTerm.App/Views/MvsmfViewerWindow.axaml`:

```xml
<!--
  This file is part of LizTerm.
  Copyright 2026 by CoffeeMuse
  SPDX-License-Identifier: BSD-3-Clause
-->
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:vm="using:LizTerm.App.ViewModels"
        x:Class="LizTerm.App.Views.MvsmfViewerWindow"
        x:DataType="vm:MvsmfViewerViewModel"
        Title="{Binding Title}"
        Width="720" Height="560" MinWidth="420" MinHeight="300"
        WindowStartupLocation="CenterOwner">
  <DockPanel>
    <Grid DockPanel.Dock="Top" ColumnDefinitions="Auto,*,Auto,Auto,Auto,Auto" Margin="12,8">
      <TextBlock Text="Find" VerticalAlignment="Center" Margin="0,0,8,0" />
      <TextBox Grid.Column="1" x:Name="FindBox" Text="{Binding Term}" />
      <TextBlock Grid.Column="2" x:Name="MatchCount" Text="{Binding CountText}" VerticalAlignment="Center"
                 MinWidth="80" Margin="8,0" Foreground="#A0A0A0" />
      <Button Grid.Column="3" x:Name="PreviousButton" Content="‹" Command="{Binding FindPreviousCommand}"
              ToolTip.Tip="Previous match (Shift+Enter)" />
      <Button Grid.Column="4" x:Name="NextButton" Content="›" Command="{Binding FindNextCommand}"
              Margin="4,0,0,0" ToolTip.Tip="Next match (Enter)" />
      <CheckBox Grid.Column="5" x:Name="LineNumbersBox" Content="Line numbers" Margin="12,0,0,0"
                IsChecked="{Binding ShowLineNumbers}" />
    </Grid>

    <Border DockPanel.Dock="Bottom" Background="#1C1E22" Padding="12,6" BorderBrush="#33373D" BorderThickness="0,1,0,0">
      <TextBlock x:Name="FooterLine" Text="{Binding FooterText}" TextWrapping="Wrap" />
    </Border>

    <!-- The gutter and the text scroll together: the code-behind follows the text box's own scroll viewer, and
         both are pinned to the same line height, so a number always sits beside its record. -->
    <Grid ColumnDefinitions="Auto,*" Margin="12,0,12,8">
      <ScrollViewer x:Name="GutterScroller" VerticalScrollBarVisibility="Hidden" HorizontalScrollBarVisibility="Disabled"
                    BorderBrush="#33373D" BorderThickness="0,0,1,0" Padding="0,0,6,0"
                    IsVisible="{Binding ShowLineNumbers}">
        <TextBlock x:Name="Gutter" Text="{Binding LineNumbers}" TextAlignment="Right" Foreground="#A0A0A0"
                   FontFamily="Menlo, Consolas, monospace" FontSize="13" />
      </ScrollViewer>
      <TextBox Grid.Column="1" x:Name="TextArea" Text="{Binding Text, Mode=OneWay}" IsReadOnly="True"
               AcceptsReturn="True" TextWrapping="NoWrap" FontFamily="Menlo, Consolas, monospace" FontSize="13"
               BorderThickness="0" Background="Transparent" Padding="6,0,0,0" />
    </Grid>
  </DockPanel>
</Window>
```

Create `src/LizTerm.App/Views/MvsmfViewerWindow.axaml.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using LizTerm.App.ViewModels;

namespace LizTerm.App.Views;

/// <summary>The read-only text viewer (viewer spec §6), owned by the mvsMF Access window and shown with
/// ShowAbove, so it stays above it, follows its Keep on Top and closes with it. One per browser window: a second
/// View replaces this DataContext rather than opening another window. It never refuses to close and holds nothing
/// to release — the view model is finished lines.</summary>
public partial class MvsmfViewerWindow : Window
{
    /// <summary>Pinned on the text box and the gutter together, so a line number sits beside its record and a
    /// scroll by line index is exact.</summary>
    private const double TextLineHeight = 18;

    private MvsmfViewerViewModel? _watched;
    private ScrollViewer? _scroller;
    private KeyModifiers _findModifiers = KeyModifiers.Control;

    public MvsmfViewerWindow()
    {
        InitializeComponent();
        TextArea.LineHeight = TextLineHeight;
        Gutter.LineHeight = TextLineHeight;
        // The platform's command modifier (Cmd on macOS, Ctrl elsewhere), as the browser window does for its own.
        _findModifiers = this.GetPlatformSettings()?.HotkeyConfiguration.CommandModifiers ?? KeyModifiers.Control;
        AddHandler(KeyDownEvent, OnKeyDownTunnel, RoutingStrategies.Tunnel);
        // The text box scrolls itself; the gutter has no scrollbars and is driven from that one.
        TextArea.TemplateApplied += (_, e) =>
        {
            _scroller = e.NameScope.Find<ScrollViewer>("PART_ScrollViewer")
                ?? TextArea.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
            if (_scroller is not null) _scroller.ScrollChanged += (_, _) => SyncGutter();
        };
        Opened += (_, _) => FindBox.Focus();
    }

    private void SyncGutter()
    {
        if (_scroller is not null) GutterScroller.Offset = new Vector(0, _scroller.Offset.Y);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        // The gutter is the window's dressing, not the document's: a second View into this window keeps whatever
        // the user last chose. A viewer that was closed and opened again starts with it on.
        var lineNumbers = _watched?.ShowLineNumbers;
        if (_watched is not null) _watched.PropertyChanged -= OnViewModelPropertyChanged;
        _watched = DataContext as MvsmfViewerViewModel;
        if (_watched is not null)
        {
            if (lineNumbers is { } chosen) _watched.ShowLineNumbers = chosen;
            _watched.PropertyChanged += OnViewModelPropertyChanged;
        }
        // A second View is another document: it starts at the top, not where the last one was left.
        if (_scroller is not null) _scroller.Offset = default;
        GutterScroller.Offset = default;
        base.OnDataContextChanged(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_watched is not null) _watched.PropertyChanged -= OnViewModelPropertyChanged;
        _watched = null;
        base.OnClosed(e);
    }

    /// <summary>The current match is selected in the text and scrolled to. Scrolling goes by line index times the
    /// pinned line height rather than through CaretIndex, because the text box does not have the focus while the
    /// user is typing in the find box.</summary>
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_watched is not { } vm) return;
        if (e.PropertyName is not nameof(MvsmfViewerViewModel.MatchStart)) return;
        if (vm.MatchStart is not { } start)
        {
            TextArea.SelectionEnd = TextArea.SelectionStart;
            return;
        }
        TextArea.SelectionStart = start;
        TextArea.SelectionEnd = start + vm.MatchLength;
        if (vm.MatchLine is { } line) ScrollToLine(line);
    }

    /// <summary>Leaves the view alone while the line is already in it; otherwise puts it a third of the way down,
    /// so the lines around it are readable. The horizontal offset is the user's.</summary>
    private void ScrollToLine(int line)
    {
        if (_scroller is null) return;
        var top = line * TextLineHeight;
        var viewport = _scroller.Viewport.Height;
        if (top >= _scroller.Offset.Y && top + TextLineHeight <= _scroller.Offset.Y + viewport) return;
        var furthest = Math.Max(0, _scroller.Extent.Height - viewport);
        _scroller.Offset = new Vector(_scroller.Offset.X, Math.Clamp(top - viewport / 3, 0, furthest));
    }

    /// <summary>Escape closes; the command modifier with F goes to the find box; Enter and Shift+Enter step the
    /// matches from anywhere but a button, which has its own Enter.</summary>
    private void OnKeyDownTunnel(object? sender, KeyEventArgs e)
    {
        if (_watched is not { } vm) return;
        switch (e.Key)
        {
            case Key.Escape:
                e.Handled = true;
                Close();
                break;
            case Key.F when e.KeyModifiers == _findModifiers:
                e.Handled = true;
                FindBox.Focus();
                FindBox.SelectAll();
                break;
            case Key.Enter when FocusManager?.GetFocusedElement() is not Button:
                e.Handled = true;
                var command = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? vm.FindPreviousCommand : vm.FindNextCommand;
                if (command.CanExecute(null)) command.Execute(null);
                break;
        }
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~MvsmfViewerWindowTests"`
Expected: PASS, seven tests.

If `A_match_is_selected_in_the_text` fails because `MatchStart` was not raised, check that `CurrentIndex` and
`Matches` in Task 3 both carry `[NotifyPropertyChangedFor(nameof(MatchStart), ...)]`.

- [ ] **Step 5: Commit**

```bash
git add src/LizTerm.App/Views/MvsmfViewerWindow.axaml src/LizTerm.App/Views/MvsmfViewerWindow.axaml.cs tests/LizTerm.App.Tests/Views/MvsmfViewerWindowTests.cs
git commit -m "$(cat <<'EOF'
mvsMF Access: the text viewer window

A read-only text box with a line-number gutter that follows its scrolling,
a find row above and the line count below. Escape closes it, the command
modifier with F goes to the find box, Enter steps the matches.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 6: The verb in the browser window

**Files:**
- Modify: `src/LizTerm.App/Views/MvsmfBrowserWindow.axaml` (the `MemberToolbar`, the `MemberMenu`, the
  `SequentialNote` text)
- Modify: `src/LizTerm.App/Views/MvsmfBrowserWindow.axaml.cs` (the gesture, the tooltip's gesture text, the key
  tunnel, opening and reusing the viewer)
- Test: `tests/LizTerm.App.Tests/Views/MvsmfBrowserWindowTests.cs`

**Interfaces:**
- Consumes: `ViewCommand`, `ViewHint`, `ViewGestureText`, `Viewer` (Task 4); `MvsmfViewerWindow` (Task 5);
  `ModalDialogs.ShowAbove` (existing).
- Produces: `MvsmfBrowserWindow.ViewGesture` (`KeyGesture`) and `MvsmfBrowserWindow.ViewerWindow`
  (`MvsmfViewerWindow?`), both `internal`, for the tests and the close path.

- [ ] **Step 1: Write the failing tests**

Add to `tests/LizTerm.App.Tests/Views/MvsmfBrowserWindowTests.cs`:

```csharp
    private static async Task<(MvsmfBrowserWindow Window, BrowserTestHost T)> ViewableAsync()
    {
        var (window, t) = Show(seed: host =>
        {
            BrowserTestHost.Standard(host);
            host.AddDataset("MVSCE02.NOTES", dsorg: "PS", recfm: "FB", lrecl: 80, blksize: 3120);
        });
        t.Host.Text["MVSCE02.CNTL(HELLO)"] = ["//HELLO JOB"];
        t.Host.Text["MVSCE02.CNTL(ALLOC)"] = ["//ALLOC JOB"];
        await Wait.UntilAsync(() => t.Vm.Datasets.Count == 5, "the first listing");
        await t.ChooseAsync("MVSCE02.CNTL");
        return (window, t);
    }

    [AvaloniaFact]
    public async Task The_view_button_explains_itself_even_when_it_is_off()
    {
        var (window, t) = await ViewableAsync();
        var button = Named<Button>(window, "ViewButton");

        Assert.Equal(t.Vm.ViewHint, ToolTip.GetTip(button));
        Assert.True(ToolTip.GetShowOnDisabled(button));
        Assert.Contains(window.ViewGesture.ToString("p", null), t.Vm.ViewHint);

        await t.ChooseAsync("MVSCE02.LOAD");
        Assert.False(button.IsEffectivelyEnabled);
        Assert.Equal("MVSCE02.LOAD holds undefined-length records, which are not text.", t.Vm.ViewHint);
    }

    [AvaloniaFact]
    public async Task The_command_gesture_views_and_plain_enter_still_downloads()
    {
        var (window, t) = await ViewableAsync();
        t.Select("HELLO");
        Named<ListBox>(window, "MemberList").Focus();
        var modifiers = window.ViewGesture.KeyModifiers == KeyModifiers.Meta
            ? RawInputModifiers.Meta : RawInputModifiers.Control;

        window.KeyPress(Key.Enter, modifiers, PhysicalKey.Enter, null);

        await Wait.UntilAsync(() => window.ViewerWindow is { IsVisible: true }, "the viewer window");
        Assert.Equal("MVSCE02.CNTL(HELLO) — mvsMF Access", window.ViewerWindow!.Title);
        Assert.DoesNotContain(t.Picker.Calls, c => c.StartsWith("save:"));
    }

    [AvaloniaFact]
    public async Task Plain_enter_in_the_member_list_still_downloads()
    {
        var (window, t) = await ViewableAsync();
        t.Select("HELLO");
        Named<ListBox>(window, "MemberList").Focus();

        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);

        await Wait.UntilAsync(() => t.Picker.Calls.Contains("save:HELLO.txt"), "the save dialog");
        Assert.Null(window.ViewerWindow);
    }

    [AvaloniaFact]
    public async Task A_second_view_reuses_the_one_window()
    {
        var (window, t) = await ViewableAsync();
        t.Select("HELLO");
        await t.Vm.ViewCommand.ExecuteAsync(null);
        await Wait.UntilAsync(() => window.ViewerWindow is { IsVisible: true }, "the viewer window");
        var first = window.ViewerWindow!;

        t.Select("ALLOC");
        await t.Vm.ViewCommand.ExecuteAsync(null);

        Assert.Same(first, window.ViewerWindow);
        Assert.Equal("MVSCE02.CNTL(ALLOC) — mvsMF Access", window.ViewerWindow!.Title);
    }

    [AvaloniaFact]
    public async Task The_viewer_closes_with_the_browser_window()
    {
        var (window, t) = await ViewableAsync();
        t.Select("HELLO");
        await t.Vm.ViewCommand.ExecuteAsync(null);
        await Wait.UntilAsync(() => window.ViewerWindow is { IsVisible: true }, "the viewer window");
        var viewer = window.ViewerWindow!;

        window.Close();

        Assert.False(viewer.IsVisible);
    }

    [AvaloniaFact]
    public async Task The_sequential_note_names_every_verb_that_acts_on_the_dataset()
    {
        var (window, t) = await ViewableAsync();

        await t.ChooseAsync("MVSCE02.NOTES");

        Assert.Equal("Sequential dataset: View, Download and Upload act on the dataset itself.",
            Named<TextBlock>(window, "SequentialNote").Text);
    }
```

and extend the member-menu half of `The_context_menus_bind_the_same_commands_as_the_toolbars` with:

```csharp
            Assert.Same(t.Vm.ViewCommand, items["MemberMenuView"].Command);
            Assert.Equal(window.ViewGesture, items["MemberMenuView"].InputGesture);
```

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~MvsmfBrowserWindowTests"`
Expected: FAIL — the build errors with `does not contain a definition for 'ViewGesture'`.

- [ ] **Step 3: Implement the XAML**

In `src/LizTerm.App/Views/MvsmfBrowserWindow.axaml`, add the button first in `MemberToolbar`:

```xml
                <Button x:Name="ViewButton" Classes="pane-verb" Content="View" Command="{Binding ViewCommand}"
                        ToolTip.Tip="{Binding ViewHint}" ToolTip.ShowOnDisabled="True" />
```

add the item first in `MemberMenu`:

```xml
                    <MenuItem x:Name="MemberMenuView" Header="View" Command="{Binding ViewCommand}" />
```

and change `SequentialNote`'s text:

```xml
            <TextBlock x:Name="SequentialNote" Margin="12" TextWrapping="Wrap"
                       Text="Sequential dataset: View, Download and Upload act on the dataset itself."
                       IsVisible="{Binding ShowSequentialNote}" />
```

- [ ] **Step 4: Implement the code-behind**

In `src/LizTerm.App/Views/MvsmfBrowserWindow.axaml.cs`:

Beside `NewDatasetGesture`, add

```csharp
    /// <summary>Cmd+Enter on macOS, Ctrl+Enter elsewhere. Plain Enter stays Download, so this case is matched
    /// first in the key tunnel.</summary>
    internal KeyGesture ViewGesture { get; private set; } = new(Key.Enter, KeyModifiers.Control);
```

in the constructor, beside the other two gestures:

```csharp
        ViewGesture = new KeyGesture(Key.Enter, modifiers);
        MemberMenuView.InputGesture = ViewGesture;
```

in `OnDataContextChanged`, inside the `if (_watched is not null)` block that subscribes:

```csharp
            _watched.ViewGestureText = ViewGesture.ToString("p", null);
```

in `OnViewModelPropertyChanged`'s switch, a new case:

```csharp
            case nameof(MvsmfBrowserViewModel.Viewer) when vm.Viewer is { } viewer:
                ShowViewer(viewer);
                break;
```

in `OnKeyDownTunnel`'s switch, **before** the existing `case Key.Enter when FilterBox.IsFocused`:

```csharp
            case Key.Enter when e.KeyModifiers == ViewGesture.KeyModifiers && MemberList.IsKeyboardFocusWithin:
                e.Handled = true;
                if (vm.ViewCommand.CanExecute(null)) _ = vm.ViewCommand.ExecuteAsync(null);
                break;
```

and the viewer's own window field and opener, beside `NewDatasetDialog` and `ShowNewDatasetAsync`:

```csharp
    /// <summary>The open text viewer, if any (viewer spec §6.1); for the tests and for reuse.</summary>
    internal MvsmfViewerWindow? ViewerWindow { get; private set; }

    /// <summary>One viewer per browser window: a later View replaces its contents and fronts it. Owned and
    /// non-blocking, so the browser stays usable behind it and the viewer closes with it. A window that cannot be
    /// shown is a status line, as the New dataset dialog is.</summary>
    private void ShowViewer(MvsmfViewerViewModel viewer)
    {
        if (ViewerWindow is { } open)
        {
            open.DataContext = viewer;
            open.Activate();
            return;
        }
        var window = new MvsmfViewerWindow { DataContext = viewer };
        ViewerWindow = window;
        window.Closed += (_, _) => { if (ReferenceEquals(ViewerWindow, window)) ViewerWindow = null; };
        try
        {
            window.ShowAbove(this);
        }
        catch (Exception ex)
        {
            ViewerWindow = null;
            if (_watched is { } vm) vm.StatusText = "✗ Could not open the viewer window: " + ex.Message;
        }
    }
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~MvsmfBrowserWindowTests"`
Expected: PASS, every test in the class.

Then the whole suite:
Run: `dotnet test LizTerm.slnx`
Expected: PASS. `ModalDialogsTests` must stay green — the viewer goes through `ShowAbove`, which is the rule it
enforces.

- [ ] **Step 6: Commit**

```bash
git add src/LizTerm.App/Views/MvsmfBrowserWindow.axaml src/LizTerm.App/Views/MvsmfBrowserWindow.axaml.cs tests/LizTerm.App.Tests/Views/MvsmfBrowserWindowTests.cs
git commit -m "$(cat <<'EOF'
mvsMF Access: View on the toolbar, the menu and the keyboard

The Members pane's first verb, its context menu's first item and Cmd+Enter
in the member list, all one command instance. Plain Enter is still Download.
The greyed button says why it is off, through ToolTip.ShowOnDisabled.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 7: Documentation, the cap measurement, and the zero-warning check

**Files:**
- Modify: `docs/user-guide.md` (the mvsMF Access section)
- Modify: `CHANGELOG.md` (under `## Unreleased`)
- Modify: `src/LizTerm.App/CLAUDE.md` (the mvsMF Access section)
- Possibly modify: `src/LizTerm.App/ViewModels/MvsmfViewerViewModel.cs` (the `MaxLines` constant)

**Interfaces:**
- Consumes: everything built in Tasks 1–6.
- Produces: nothing code depends on.

- [ ] **Step 1: Measure the cap**

Build a member big enough to hit the cap and time the window. With a live host to hand, view a large member; with
none, add a throwaway `[AvaloniaFact]` locally that shows `MvsmfViewerWindow` over a 10,000-line view model and
time `window.UpdateLayout()`:

```csharp
    [AvaloniaFact]
    public void Measure()
    {
        var lines = Enumerable.Range(1, MvsmfViewerViewModel.MaxLines).Select(n => new string('X', 80)).ToArray();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var (window, _) = Show(lines);
        window.UpdateLayout();
        Assert.Fail($"{clock.ElapsedMilliseconds} ms");
    }
```

If it is over about a second, lower `MvsmfViewerViewModel.MaxLines` (5,000, then 2,000) until it is not, and
update the expected string in `Past_the_cap_the_first_lines_are_shown_and_the_footer_says_so` to match. **Delete
the measuring test before committing.**

- [ ] **Step 2: Write the user guide section**

In `docs/user-guide.md`, in the mvsMF Access section, after the downloading subsection, add:

```markdown
### Viewing a member

**View** in the Members pane opens a window showing the member as text, without downloading it. It acts on one
member at a time; with a sequential dataset chosen it shows the dataset. Enter and a double-click still mean
Download, and **Cmd+Enter** (**Ctrl+Enter** on Windows and Linux) is View.

The text is read the way a Text download reads it, so **Trim trailing blanks** applies here too. The viewer is
read-only: nothing you do in it changes the member. Select and copy work as they do anywhere else, **Find**
searches what is shown, and **Line numbers** turns the gutter off if you would rather copy without it.

View is off for a dataset whose records are undefined-length — a load library holds object code, not text — and
the button says so when you point at it.

A very long member is shown as far as its first 10,000 lines, and the footer says how many there are in all.
Download it to read the rest.
```

- [ ] **Step 3: Write the changelog entry**

In `CHANGELOG.md`, under `## Unreleased`, above the existing entry:

```markdown
- **mvsMF Access can show a member without downloading it.** **View** in the Members pane opens a read-only window
  with the member's text, with find and line numbers; a sequential dataset shows itself
  ([#163](https://github.com/coffeemuse/LizTerm/issues/163)).
```

- [ ] **Step 4: Write the implementation notes**

In `src/LizTerm.App/CLAUDE.md`, in the mvsMF Access section, after the **ETag memory** bullet, add:

```markdown
- **View** (`View.cs`, `MvsmfViewerViewModel`, `MvsmfViewerWindow`): one member, or a sequential dataset, read
  through `RunExclusiveAsync` like any other operation, so the banner, Retry, Cancel and the prompts are the ones
  already there. Always `ReadTextAsync`, whatever `Mode` says — viewing is reading a document — and `withEtag:
  false`, so the stamp memory is untouched: a view is not a download. Progress and the result go on the status
  line, never on the member's row, which would otherwise read like a transfer. `CanView` wants exactly one member
  (Download takes many) and refuses a dataset whose `RecordFormat` is `Undefined`; `ViewHint` carries that reason
  and is shown on the disabled button through `ToolTip.ShowOnDisabled`, with the gesture text handed to the view
  model by the window (`ViewGestureText`), since a view model cannot ask the platform. ⌘⏎/Ctrl+Enter is matched
  before the plain-Enter case in the key tunnel, which reads `e.Key` alone and would otherwise download.
- The viewer window is owned and non-blocking (`ShowAbove`), one per browser window
  (`MvsmfBrowserWindow.ViewerWindow`): a second View sets its `DataContext` and fronts it. `MvsmfViewerViewModel`
  holds finished lines and no Avalonia type; it caps what it lays out at `MaxLines`, because Avalonia's `TextBox`
  builds one layout for the whole text, and the footer says so when it bit. The line-number gutter is a
  `TextBlock` in a scrollbar-less `ScrollViewer` driven from the text box's own `PART_ScrollViewer`, with the line
  height pinned on both from `TextLineHeight`; find scrolls by line index times that height rather than through
  `CaretIndex`, because the text box does not hold the focus while the user types in the find box.
```

- [ ] **Step 5: Run the whole suite and the zero-warning check**

```bash
dotnet test LizTerm.slnx
```
Expected: PASS, with the live-host tests skipping themselves.

```bash
dotnet build LizTerm.slnx --no-incremental 2>&1 | grep -c " warning "
```
Expected: `0`.

- [ ] **Step 6: Commit**

```bash
git add docs/user-guide.md CHANGELOG.md src/LizTerm.App/CLAUDE.md src/LizTerm.App/ViewModels/MvsmfViewerViewModel.cs
git commit -m "$(cat <<'EOF'
Docs: viewing a member in mvsMF Access

The user guide's new subsection, the changelog entry for #163, and the
implementation notes for the read's rules, the one-window rule, the gutter
sync and the cap.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
EOF
)"
```

---

## Hands-on pass (not automated)

Two things the headless tests cannot see. Run the app against MVS/CE (`docs/development.md` has the recipe; the
live host and password are in the developer's own notes) and check:

1. **The gutter tracks the text** while scrolling with the wheel, the scrollbar and the keyboard, at both ends of
   a member longer than the window.
2. **Find scrolls to the match** in a member longer than the window, and the match is visible rather than at the
   very bottom edge.

Also worth a look while there: an 80-column member at the default window width needs no horizontal scrolling, and
a member of one line looks right.
