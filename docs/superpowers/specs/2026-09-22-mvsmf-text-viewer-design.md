# LizTerm: mvsMF Access — a read-only text viewer

Design for #163, agreed 2026-09-22. It follows the dataset browser spec of 2026-09-16
(`2026-09-16-lizterm-mvsmf-dataset-browser-design.md`, "the browser spec" below) and the pane-pattern spec of
2026-09-19 (`2026-09-19-mvsmf-access-pane-pattern-design.md`, "the pane-pattern spec"), whose §10 parked
"in-app viewing of member text". This is that item. Part of #17.

## 1. Purpose

Reading a document that lives on the mainframe — a README member, a `$$$DOC`, notes in a sequential dataset —
means downloading it and opening the file. That is three dialogs and a file on disk for something the user only
wanted to look at, and it leaves a copy behind.

This slice adds a **View** verb to the Members pane that opens a small owned window with the member's (or a
sequential dataset's) records as read-only text, fetched in Text mode with the transfer options in force. Enter
and double-click still mean Download; View is its own verb.

It adds no host call: `IHostFileService.ReadTextAsync` already returns the records as lines. Nothing writes to the
host, nothing writes to disk.

## 2. Decisions

- **Its own window, not a third pane.** Half of an 880-pixel window is too narrow for 80-column records, and the
  pane-pattern spec reserves body-swapping for a surface read *while an operation runs* (the upload review). An
  owned, non-blocking window shown with `ModalDialogs.ShowAbove` — the rule mvsMF Access itself follows — keeps
  the browser usable behind it and closes with it.
- **One viewer at a time, reused.** A second View replaces its contents and fronts it. Comparing two members side
  by side is a real want, but it costs a window per member, a lifetime to manage and state to test, for a slice
  whose job is "let me read this". A later release can lift the limit without changing anything below.
- **The browser does the read; the viewer only presents.** The read runs through `RunExclusiveAsync` on
  `MvsmfBrowserViewModel`, so progress, Cancel, the error banner, Retry, the sign-in prompt and the certificate
  prompt are the ones already built and tested. `MvsmfViewerViewModel` holds no connection, makes no host call and
  names no Avalonia type, so every rule in it is assertable with a plain `[Fact]`.
- **Always Text, whatever the transfer drop-down says.** Viewing is reading a document; the mode radio is about
  transfers. Only **Trim trailing blanks** carries over, because it decides what the text on screen actually is.
- **No stamp is asked for.** `withEtag: false`: a view can never write, and a stamp costs the host a second pass
  over the content (browser spec §5.2, manage spec §10). The ETag memory is left exactly as it was — a view is
  not a download.
- **Off for undefined-length records.** A load library (RECFM=U) is picked up as Binary today and its members are
  object code. View greys there and says why, rather than rendering mojibake. Everything else — fixed,
  variable, and a listing that does not say — is offered.
- **No Download button in the viewer, no wrap toggle.** Download is one click away in the pane the viewer opened
  from, and records are fixed-width, so wrapping them is not a reading aid. Line numbers and Find earn their place
  (§6.2, §6.3); these two did not.

## 3. The verb

`MvsmfBrowserViewModel.View.cs`, a new partial beside `Downloads.cs`.

```csharp
private bool CanView =>
    !IsBusy && !IsCreating && SelectedDataset is { IsSupported: true } dataset
    && dataset.Attributes.RecordFormat != RecordFormatFamily.Undefined
    && (dataset.IsSequential || _selectedMembers.Count == 1);
```

Exactly one member, unlike Download, which takes many: a viewer shows one document.

- **Toolbar**: `View` first in the Members pane's `MemberToolbar`, before `⇣ Download…`. No ellipsis — it asks
  nothing, it opens a window.
- **Context menu**: `View` first in `MemberMenu`, with the gesture below, above `Download…`.
- **Keyboard**: the platform's command modifier with Enter (⌘⏎ on macOS, Ctrl+Enter elsewhere) in the member
  list, taken in `OnKeyDownTunnel` *before* the existing plain-Enter case, which reads `e.Key` alone and would
  otherwise download. It joins `RefreshGesture` and `NewDatasetGesture` as a `ViewGesture` built from
  `HotkeyConfiguration.CommandModifiers`.
- **Tooltip**: bound to `ViewHint`, with `ToolTip.ShowOnDisabled="True"` so the greyed button still explains
  itself. `ViewHint` is `View the selected member ({gesture})`, or `View this dataset ({gesture})` for a
  sequential dataset, or, when the chosen dataset's record format is Undefined,
  `{NAME} holds undefined-length records, which are not text.` No other disabled state needs words: "select one
  member" is plain from the selection.
- **The sequential note** becomes `Sequential dataset: View, Download and Upload act on the dataset itself.`
- `ViewCommand` joins `NotifyCommands()`. `ViewHint` is raised with the properties `SelectedDataset` already
  notifies.

## 4. The read

```csharp
private async Task ViewCoreAsync(CancellationToken token)
```

run as `RunExclusiveAsync(ViewCoreAsync, () => ViewAsync())`, so a connection failure lands in the banner with
Retry and everything else on the status line, per the rules in `MvsmfBrowserViewModel`'s class summary.

1. `path` is the one selected member, or the sequential dataset itself.
2. `StatusText = $"⟳ Reading {path}…"`, with byte progress through the existing `RowProgress`
   (`⟳ Reading {path} · {bytes} bytes`).
3. `await _connection.RunAsync(service => service.ReadTextAsync(path, progress, withEtag: false, token))`.
4. `Viewer = new MvsmfViewerViewModel(path.ToString(), read.Lines, TrimTrailingBlanks)` — the view model takes
   the path as display text, not as a `HostPath`, so a USS file can use it unchanged (§12).
5. `StatusText = $"✓ Read {path} · {n} lines."`

Progress and results go on the **status line, never on the member's row**: a row that said `✓ Done · 4,096 bytes`
after a view would read like a transfer that happened. Rows keep the results of the last batch.

A cancelled or failed view sets no `Viewer` and opens no window; `RunExclusiveAsync`'s own arms word it
(`– Cancelled.`, the banner, or `✗ …`).

## 5. Core: one home for the trim rule

`DownloadOptions` gains

```csharp
public string Format(string line) => TrimTrailingBlanks ? line.TrimEnd(' ') : line;
```

and `HostFileTransfer.FormatText` calls it, so the rule a download writes to a file and the rule the viewer shows
on screen are one line of code, not two copies. `MvsmfViewerViewModel` takes the flag and applies the same rule
through a `DownloadOptions` it builds for the purpose.

This is the only change under `src/LizTerm.Core` and the only change outside `LizTerm.App`.

## 6. The viewer

### 6.1 Shape

`src/LizTerm.App/Views/MvsmfViewerWindow.axaml(.cs)` and
`src/LizTerm.App/ViewModels/MvsmfViewerViewModel.cs`.

- **Title**: `MvsmfViewerViewModel.Title`, `{path} — mvsMF Access`, for example
  `MVSCE02.JCL(README) — mvsMF Access`.
- **Size**: 720×560, `MinWidth` 420, `MinHeight` 300, `WindowStartupLocation="CenterOwner"`. 720 holds 80
  columns of the browser's monospace stack plus the gutter.
- **No preview strip.** It opens only from the window that carries one (pane-pattern spec §4.1), and a second
  yellow bar on a document window is noise.
- **Layout** (`DockPanel`): the find row docked top; the footer docked bottom; the text and its gutter fill.
- **Owned and non-blocking**: `ShowAbove(browserWindow)`, so it takes the owner's Keep on Top, stays above it and
  closes with it. `MvsmfBrowserWindow` already sets `ClosingBehavior = WindowClosingBehavior.OwnerWindowOnly`, so
  the viewer can never veto the browser's close; the viewer never refuses to close either.
- The browser window holds it as `internal MvsmfViewerWindow? ViewerWindow { get; private set; }`, cleared on its
  `Closed`, the way `NewDatasetDialog` is held. `MvsmfBrowserViewModel.Viewer` is an `[ObservableProperty]`; the
  window opens the viewer on the first non-null value and, for a later one, sets `DataContext` and calls
  `Activate()`.

### 6.2 The text and the gutter

A read-only `TextBox`: `IsReadOnly`, `AcceptsReturn`, `TextWrapping="NoWrap"`, the browser's
`Menlo, Consolas, monospace`, with an explicit `LineHeight`. It brings selection, ⌘A and ⌘C with it and needs no
code.

Beside it, a line-number gutter: a right-aligned `TextBlock` of `1\n2\n3…` in the same font and the same
`LineHeight`, inside a `ScrollViewer` with no scrollbars. The window takes the text box's inner `ScrollViewer`
(its templated `PART_ScrollViewer`, found once the template is applied) and follows its vertical offset on
`ScrollChanged`, so the numbers track the text exactly. The gutter is dimmed (`#A0A0A0`) and separated by a
one-pixel `#33373D` rule, as the panes are.

**Line numbers toggle**: `ShowLineNumbers`, a `CheckBox` at the right of the find row, on by default. It is the
viewer view model's state, and the window carries it across a second View (§7); it is not a saved preference — Preferences has no
mvsMF display group and this does not earn one.

### 6.3 Find

The find row is always visible; there is nothing to open or close. `MvsmfViewerViewModel` holds the state, in
`FindViewModel`'s shape without its screen types (that one searches a `ScreenSnapshot` and is not reusable):

- `Term` — typing recomputes.
- `Matches` — the start offsets into `Text`, case-insensitive, `StringComparison.OrdinalIgnoreCase`, non-overlapping.
- `CurrentIndex`, `CountText` (`""` for an empty term, `No matches`, else `3 of 17`).
- `FindNextCommand`, `FindPreviousCommand` — both wrap; a fresh term lands on its first match in the text, which
  the window then scrolls to.

The window follows `CurrentIndex`: it sets the text box's `SelectionStart`/`SelectionEnd` to the match, and
scrolls by **line index × `LineHeight`** on the scroller it already holds for the gutter, putting the match a
third of the way down when it is outside the visible band. Scrolling by line rather than by `CaretIndex` keeps it
deterministic on an unfocused text box, and the horizontal offset is left alone.

Find searches **what is shown** — trimmed, and capped by §6.4, which the footer says.

Keys in the viewer: ⌘F/Ctrl+F focuses the find box, Enter and Shift+Enter step the matches, Escape closes the
window. ⌘A and ⌘C are the text box's own.

### 6.4 The cap

mvsMF has no ranged read (browser spec §2), so the whole member arrives either way; the cap is only what the text
box is asked to lay out, and Avalonia's `TextBox` builds one layout for the lot.

`MvsmfViewerViewModel.MaxLines = 10_000`. Past it the text is the first `MaxLines` lines and the footer reads

> `⚠ Showing the first 10,000 lines of 24,318. Download the member to read it all.`

The constant is measured against a real large member during the build, and lowered if the viewer takes more than
about a second to appear on the development machine; only the number in the note changes with it.

### 6.5 The footer

`412 lines · trailing blanks trimmed`, or `412 lines` with the option off, and the §6.4 note in its place when
the content was capped. Plain counts carry no mark; the capped note is a `⚠` with words, per the colour rule.

## 7. What a second View does

`Viewer` is replaced. The window keeps `ShowLineNumbers`; `Term`, `Matches` and `CurrentIndex` start empty,
because a term found in one member means nothing in the next. The window is fronted with `Activate()`, not
reopened.

Closing the viewer leaves `Viewer` set and clears the window's own field; the next View opens a fresh window.

## 8. Tests

| Layer | What is pinned |
|---|---|
| Core | `DownloadOptions.Format` trims only with the option on; `FormatText` still formats as its existing tests say |
| App, browser view model (`MvsmfBrowserViewTests`, over `BrowserTestHost`) | View off until exactly one member is selected, and off for two; on for a sequential dataset with none; off with its reason for an undefined-format dataset; reads Text while the mode is Binary; honours and ignores Trim trailing blanks; the status line's reading and read lines; the stamp memory untouched; a connection failure banners with Retry and opens no viewer; a cancel opens no viewer |
| App, viewer view model (`MvsmfViewerViewModelTests`) | the text is the lines joined and trimmed; the footer's words both ways; find counts, steps, wraps, ignores case and reports no matches; a term that is cleared clears the matches; the cap truncates and says so |
| App, window (`MvsmfBrowserWindowTests`, `MvsmfViewerWindowTests`) | View joins `The_context_menus_bind_the_same_commands_as_the_toolbars`; the command gesture in the member list views and plain Enter still downloads; the viewer opens, a second View reuses the one window, Escape closes it, it closes with the browser window; the toggle hides the gutter; the gutter's numbers follow the text |

## 9. Docs

| Fact | Home |
|---|---|
| Viewing a member, the cap, what View does not do | `docs/user-guide.md`, the mvsMF Access section, a "Viewing a member" subsection after downloading |
| The release note | `CHANGELOG.md` under `## Unreleased` |
| The viewer's mechanics: the read's rules, the one-window rule, the gutter sync, the cap | `src/LizTerm.App/CLAUDE.md`, the mvsMF Access section |
| This design | here |

## 10. Out of scope

Editing and anything that writes to the host; binary and undefined-format datasets; a Download button in the
viewer; wrapping; several viewers at once; a saved preference for line numbers; reload; syntax colouring; print;
jobs, USS and console services, which stay on #17's list.

## 11. PR shape

**One PR**: Core's `Format`, the verb, the viewer, the tests and the docs. It is one cohesive surface and splitting
it would put a command with no window in the first half.

## 12. How USS slots in

A USS file pane (pane-pattern spec §11) gets the same verb against the same viewer: `MvsmfViewerViewModel` takes
display text and lines, never a `HostPath`, so a USS file's text opens the same window with its path in the title
and nothing in §6 changes.
