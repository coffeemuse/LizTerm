# mvsMF USS tab, PR 2 (the tab) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Put a **USS** tab in the mvsMF Access window, beside **Datasets**, that browses the host's UNIX file system one directory at a time and lists, creates, deletes, downloads, uploads and views under it, on the contract PR 1 (#183) merged.

**Architecture:** The browser's operation runner (busy flag, cancel, status line, error banner with Retry, confirmation strip) moves out of `MvsmfBrowserViewModel` into one shared `BrowserOperations`; the dataset view model forwards its old properties and commands to it, so the window's bindings and the existing tests do not change. A new `UssBrowserViewModel` runs on the same runner, so one operation at a time holds across both tabs and every result lands on the one status line. The download batch (progress, `.part` file, two at a time, the connection-failure stop) moves into a shared `BrowserTransfers` helper both view models use. The window gains a `TabControl`; the Datasets tab keeps its filter row and panes as they are, the USS tab has a path row and two `BrowserPane`s. This is PR 2 of #176.

**Tech Stack:** .NET 10, C# latest, Avalonia 12 (headless in tests), CommunityToolkit.Mvvm source generators, xunit.v3.

**Spec:** `docs/superpowers/specs/2026-09-22-mvsmf-uss-tab-design.md` (read §3, §4.4, §4.5, §4.6, §4.7, §5 App.Tests and §7 before starting; §4.1 to §4.3 and §6 are PR 1, merged).

**What PR 1 and its review left for this PR (read `docs/superpowers/plans/2026-09-22-mvsmf-uss-backend.md`'s header and the memory of #183 for the reasons):**
- `HostPathKind.Unix`; `HostPath.ForUnix`, `TryParse` (trims, reads a leading `/`), `Parent`, `Name`, `Child(name)` (refuses an empty name and one with `/`), `UnixPathError`, `MaxUnixPathLength` (251). `Dataset` and `Member` are null on a UNIX path. A name from a listing is joined with `Child`, never through `TryParse`, since a UNIX name may begin or end with a blank.
- `HostFileEntryKind.Directory`, `File` and `Other` (a link, device, FIFO or socket: listed, never read, written or descended into); `HostFileEntry.Unix` is `UnixFileAttributes(long Size, DateTimeOffset? Modified)`.
- `HostFileListing.Truncated`: the host cut the listing and offers no continuation. `IsComplete` reads it.
- `IHostFileService.ListDirectoryAsync(HostPath, HostListRequest, CancellationToken)` (one level, the host's order, `NotFound` for a missing path, `InvalidRequest` for a file) and `CreateDirectoryAsync(HostPath, CancellationToken)` (`AlreadyExists`, `NotFound` for a missing parent). Reads, writes and deletes take a UNIX path; a delete of a directory is recursive; the backend and the fake refuse to delete `/`. `RenameAsync` and the dataset verbs throw `ArgumentException` for a UNIX path: that is a bug in the caller, not a host outcome, and no `catch (HostFileException)` sees it.
- `HostFileLimits.MaxUnixFileBytes` (1 MiB, measured); `HostFileTransfer.CheckUnixTextFile(file, maxBytes = …, options)`, `BinaryUploadProblem(file, maxBytes = …) → string?` ("The file is N bytes; the host holds at most M."), and `UploadTextAsync`/`UploadBinaryAsync` refuse an over-cap UNIX upload with `InvalidOperationException` before sending. A UNIX verify compares exactly (trailing blanks are data).
- The App fake (`tests/LizTerm.App.Tests/Fakes/FakeHostFileService.cs`): `Directories` (the root always), `AddDirectory(path)`, `AddFile(path, lines…)`, `AddBinaryFile(path, bytes)`; `listdir:<path>` lists subdirectories then files, each in ordinal order; `mkdir:<path>`; the read, write and delete keys as for members. The real host's order is its own, so the view model sorts (§4.6 below), and nothing relies on the fake's order.
- `EtagMemory.ForgetUnder(path)` and `Move` walk UNIX directories.
- A `FileTooLarge` check message does not name the file; the App composes the name in.

## Global Constraints

- Every hand-written `.cs` and `.axaml` file starts with the three licence lines (`This file is part of LizTerm.` / `Copyright 2026 by CoffeeMuse` / `SPDX-License-Identifier: BSD-3-Clause`; in `.axaml` inside a comment before the root element). `RepositoryHeadersTests` fails the suite for a missing one.
- **Dependency rule.** `LizTerm.App` names the mvsMF backend only in `src/LizTerm.App/HostFileServiceFactory.cs`; everything else, and every App test, talks to `IHostFileService`. Nothing in App names `MvsmfFileService` beyond that file.
- **One operation at a time across both tabs**, through the one `BrowserOperations`. Results are set after `await` on the UI context; progress goes through `dispatch`; a closed `RowProgress` drops late reports.
- **Colour never carries meaning alone**: every status line and row status starts with a mark (`✓`, `✗`, `⚠`, `⟳`, `–`) and words.
- **Verbs are text with three Unicode marks only** (`↻ Refresh`, `⇣ Download…`, `⇡ Upload…`); no icon package.
- **The window's public surface for tests:** the dataset view model keeps `IsBusy`, `IsIdle`, `StatusText`, `ErrorText`, `HasError`, `CanRetry`, `Confirmation` (settable), `HasConfirmation`, `RetryCommand` and `CancelCommand` under those names; every existing App test stays green without edits, except where a task says otherwise.
- **The USS tab's Transfer drop-down holds Text, Binary and Verify after upload only**; downloads never trim trailing blanks; uploads never expand tabs.
- **Nothing ever deletes `/`**; the USS tab's rows are children of the current directory, so the root is never a row, and Up is off at the root.
- Zero warnings: `dotnet build LizTerm.slnx --no-incremental 2>&1 | grep -c " warning "` prints `0` (CI builds with `-warnaserror`; xUnit analyzers count).
- The user guide's bundled HTML must be regenerated after `docs/user-guide.md` changes: `LIZTERM_UPDATE_DOCS=1 dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~UserGuideAssetTests"`.
- Commits end with `Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>`.
- Run every command from the worktree root `/Users/robert/ClaudeSandbox/LizTerm/.claude/worktrees/uss-mvsmf-access-support-342b1a`, on the branch `claude/uss-tab-pr2-176` (off `main` at `ac1f39c`).

## File Structure

| File | Responsibility |
|---|---|
| `src/LizTerm.App/ViewModels/BrowserOperations.cs` | **New.** The runner: busy, cancel, status, banner, Retry, the confirmation strip, the idle signal, `IsConnectionFailure` |
| `src/LizTerm.App/ViewModels/BrowserTransfers.cs` | **New.** `RowProgress` and the download batch, shared by both view models |
| `src/LizTerm.App/ViewModels/MvsmfBrowserViewModel.cs` and its partials | Forward to `BrowserOperations`; downloads through `BrowserTransfers`; owns `Uss` |
| `src/LizTerm.App/ViewModels/UssBrowserRows.cs` | **New.** `DirectoryRow`, `FileRow` |
| `src/LizTerm.App/ViewModels/UssBrowserViewModel.cs` | **New.** Path row, listing, descend, Up, Refresh, footers, New…, Delete… |
| `src/LizTerm.App/ViewModels/UssBrowserViewModel.Transfers.cs` | **New.** Download…, Upload…, View, the transfer drop-down |
| `src/LizTerm.App/HostFiles/HostFileMessages.cs` | `DescribeUnixUploadFailure` |
| `src/LizTerm.App/Views/MvsmfBrowserWindow.axaml` / `.axaml.cs` | The `TabControl`, the path row, the two USS panes, keys, menus, focus, the shared viewer |
| `src/LizTerm.App/CLAUDE.md`, `tests/CLAUDE.md` | Notes |
| `tests/LizTerm.App.Tests/ViewModels/BrowserOperationsTests.cs` | **New.** The runner alone |
| `tests/LizTerm.App.Tests/ViewModels/UssTestHost.cs` | **New.** A `UssBrowserViewModel` over the fake, seeded with a small tree |
| `tests/LizTerm.App.Tests/ViewModels/UssBrowser{Listing,Manage,Transfer}Tests.cs` | **New.** View-model tests |
| `tests/LizTerm.App.Tests/Views/MvsmfBrowserWindowUssTests.cs` | **New.** Window tests for the tab |
| `tests/LizTerm.App.Tests/HostFiles/HostFileMessagesTests.cs` | One test for the new sentence |
| `docs/user-guide.md` (+ its bundled HTML), `CHANGELOG.md` | The **USS** subsection; the Unreleased line |

## Task graph

Task 1 (runner) and Task 2 (transfer helper) are refactors under the existing suite, which is their witness. Tasks 3 to 5 build `UssBrowserViewModel` against the fake with no window. Task 6 is the window. Task 7 is docs. Task 8 verifies, opens the PR and hands Robert the hands-on checklist. Every task keeps the whole solution green and warning-free.

---

### Task 1: `BrowserOperations` — the runner, extracted

**Files:**
- Create: `src/LizTerm.App/ViewModels/BrowserOperations.cs`
- Modify: `src/LizTerm.App/ViewModels/MvsmfBrowserViewModel.cs`, `src/LizTerm.App/ViewModels/MvsmfBrowserViewModel.Downloads.cs` (`IsConnectionFailure`), `src/LizTerm.App/ViewModels/MvsmfBrowserViewModel.Paging.cs` (`WhenIdle`, `SignalIdle`, `_disposed`)
- Create: `tests/LizTerm.App.Tests/ViewModels/BrowserOperationsTests.cs`

**Interfaces:**
- Produces: `public sealed partial class BrowserOperations : ObservableObject, IDisposable` with `bool IsBusy`, `bool IsIdle`, `string StatusText` (get/set), `string? ErrorText` (get/set), `bool HasError`, `bool CanRetry`, `ConfirmationRequest? Confirmation` (get/set), `bool HasConfirmation`, `bool IsDisposed`, `IAsyncRelayCommand RetryCommand`, `IRelayCommand CancelCommand`, `Task RunExclusiveAsync(Func<CancellationToken, Task> work, Func<Task>? retry = null, Func<Exception, string>? describe = null)`, `Task<ConfirmOutcome> AskAsync(ConfirmationRequest request)`, `void DropRetry()`, `void WarnWhenIdle(string message)`, `internal Task? WhenIdle()`, `static bool IsConnectionFailure(HostFileException ex)`. On `MvsmfBrowserViewModel`: `public BrowserOperations Ops { get; }`, and the old property and command names forwarding to it.

- [ ] **Step 1: Write the failing tests**

Create `tests/LizTerm.App.Tests/ViewModels/BrowserOperationsTests.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.App.ViewModels;
using LizTerm.Core.HostFiles;

namespace LizTerm.App.Tests.ViewModels;

public class BrowserOperationsTests
{
    [Fact]
    public async Task One_operation_at_a_time_and_a_second_is_ignored()
    {
        var ops = new BrowserOperations();
        var gate = new TaskCompletionSource();
        var busySeen = new List<bool>();
        ops.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(BrowserOperations.IsBusy)) busySeen.Add(ops.IsBusy); };

        var first = ops.RunExclusiveAsync(async _ => { await gate.Task; ops.StatusText = "✓ First done."; });
        var ran = false;
        await ops.RunExclusiveAsync(_ => { ran = true; return Task.CompletedTask; });

        Assert.True(ops.IsBusy);
        Assert.False(ops.IsIdle);
        Assert.False(ran);
        Assert.True(ops.CancelCommand.CanExecute(null));
        gate.SetResult();
        await first;
        Assert.False(ops.IsBusy);
        Assert.Equal(new[] { true, false }, busySeen);
        Assert.Equal("✓ First done.", ops.StatusText);
        Assert.False(ops.CancelCommand.CanExecute(null));
    }

    [Fact]
    public async Task A_connection_failure_is_the_banner_with_retry_and_retry_runs_again()
    {
        var ops = new BrowserOperations();
        var attempts = 0;
        Task Attempt() => ops.RunExclusiveAsync(_ =>
        {
            attempts++;
            if (attempts == 1) throw new HostFileException(HostFileErrorKind.Unreachable, "X: cannot reach the host.");
            ops.StatusText = "✓ Listed.";
            return Task.CompletedTask;
        }, Attempt);

        await Attempt();

        Assert.True(ops.HasError);
        Assert.True(ops.CanRetry);
        Assert.Equal("X: cannot reach the host.", ops.ErrorText);
        Assert.Equal("", ops.StatusText);
        await ops.RetryCommand.ExecuteAsync(null);
        Assert.False(ops.HasError);
        Assert.False(ops.CanRetry);
        Assert.Equal("✓ Listed.", ops.StatusText);
        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task Describe_words_the_banner_and_other_failures_are_the_status_line()
    {
        var ops = new BrowserOperations();

        await ops.RunExclusiveAsync(_ => throw new HostFileException(HostFileErrorKind.Unauthenticated, "Sign-in: refused."), describe: ex => "Custom: " + ex.Message);
        Assert.Equal("Custom: Sign-in: refused.", ops.ErrorText);

        await ops.RunExclusiveAsync(_ => throw new HostFileException(HostFileErrorKind.NotFound, "X: not found."));
        Assert.Null(ops.ErrorText);
        Assert.Equal("✗ Not found.", ops.StatusText);
    }

    [Fact]
    public async Task Cancel_stops_the_operation_and_answers_its_question()
    {
        var ops = new BrowserOperations();
        var running = ops.RunExclusiveAsync(async token =>
        {
            var answer = await ops.AskAsync(new ConfirmationRequest("Sure?", "Yes"));
            Assert.Equal(ConfirmChoice.Cancel, answer.Choice);
            await Task.Delay(Timeout.Infinite, token);
        });
        await Wait.UntilAsync(() => ops.HasConfirmation, "the question");

        ops.CancelCommand.Execute(null);
        await running;

        Assert.False(ops.HasConfirmation);
        Assert.Equal("– Cancelled.", ops.StatusText);
        Assert.False(ops.IsBusy);
    }

    [Fact]
    public async Task A_warning_waits_for_the_operation_to_end_or_lands_at_once()
    {
        var ops = new BrowserOperations();
        ops.WarnWhenIdle("The pin was not saved.");
        Assert.Equal("⚠ The pin was not saved.", ops.StatusText);

        var gate = new TaskCompletionSource();
        var running = ops.RunExclusiveAsync(async _ => { await gate.Task; ops.StatusText = "✓ Done."; });
        ops.WarnWhenIdle("Later.");
        Assert.Equal("⚠ The pin was not saved.", ops.StatusText);
        gate.SetResult();
        await running;
        Assert.Equal("⚠ Later.", ops.StatusText);
    }

    [Fact]
    public async Task When_idle_is_null_while_nothing_runs_and_completes_when_the_operation_ends()
    {
        var ops = new BrowserOperations();
        Assert.Null(ops.WhenIdle());
        var gate = new TaskCompletionSource();
        var running = ops.RunExclusiveAsync(async _ => await gate.Task);
        var idle = ops.WhenIdle();
        Assert.NotNull(idle);
        Assert.False(idle!.IsCompleted);
        gate.SetResult();
        await running;
        await idle;
    }

    [Fact]
    public async Task Disposed_runs_nothing_and_answers_cancel()
    {
        var ops = new BrowserOperations();
        var question = new ConfirmationRequest("Sure?", "Yes");
        var asked = ops.AskAsync(question);
        ops.Dispose();
        Assert.True(ops.IsDisposed);
        Assert.Equal(ConfirmChoice.Cancel, (await asked).Choice);
        var ran = false;
        await ops.RunExclusiveAsync(_ => { ran = true; return Task.CompletedTask; });
        Assert.False(ran);
        Assert.Equal(ConfirmChoice.Cancel, (await ops.AskAsync(new ConfirmationRequest("Again?", "Yes"))).Choice);
    }

    [Theory]
    [InlineData(HostFileErrorKind.Unreachable, true)]
    [InlineData(HostFileErrorKind.Unauthenticated, true)]
    [InlineData(HostFileErrorKind.CertificateRejected, true)]
    [InlineData(HostFileErrorKind.Unsupported, true)]
    [InlineData(HostFileErrorKind.NotFound, false)]
    [InlineData(HostFileErrorKind.ServerError, false)]
    public void Connection_failures_are_the_four_kinds(HostFileErrorKind kind, bool expected) =>
        Assert.Equal(expected, BrowserOperations.IsConnectionFailure(new HostFileException(kind, "x")));
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~BrowserOperationsTests" 2>&1 | grep -E "error CS|Passed!|Failed!" | head -5`
Expected: build errors naming `BrowserOperations`.

- [ ] **Step 3: Create the runner**

Create `src/LizTerm.App/ViewModels/BrowserOperations.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LizTerm.App.HostFiles;
using LizTerm.Core.HostFiles;

namespace LizTerm.App.ViewModels;

/// <summary>The one operation at a time the mvsMF Access window runs (USS spec §4.4): the busy flag and its
/// cancellation, the status line, the error banner with Retry, and the confirmation strip. Shared by the tabs' view
/// models, so an operation started on either holds the whole window and its result lands on the one status line.
/// It raises property changes for the window's bindings; the owners forward them or notify their commands.
/// Connection failures (<see cref="IsConnectionFailure"/>) are the red banner with Retry; everything else is the
/// status line. A closed window (<see cref="Dispose"/>) runs nothing more and answers every question Cancel.</summary>
public sealed partial class BrowserOperations : ObservableObject, IDisposable
{
    private CancellationTokenSource? _cts;
    private Func<Task>? _retry;
    private string? _pinSaveWarning;
    private readonly object _idleLock = new();
    private TaskCompletionSource? _idle;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private bool _isBusy;

    [ObservableProperty] private string _statusText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError), nameof(CanRetry))]
    private string? _errorText;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasConfirmation))]
    private ConfirmationRequest? _confirmation;

    public bool IsIdle => !IsBusy;
    public bool HasError => ErrorText is not null;
    public bool CanRetry => HasError && _retry is not null;
    public bool HasConfirmation => Confirmation is not null;
    public bool IsDisposed { get; private set; }

    partial void OnIsBusyChanged(bool value)
    {
        if (!value) SignalIdle();
    }

    /// <summary>Runs one operation with the busy flag, its own cancellation, and the failure rules in the class
    /// summary. A second operation while one runs is ignored; the commands are disabled anyway.
    /// <paramref name="describe"/> words the banner; the default is <see cref="HostFileMessages.Describe"/>.</summary>
    public async Task RunExclusiveAsync(Func<CancellationToken, Task> work, Func<Task>? retry = null,
        Func<Exception, string>? describe = null)
    {
        if (IsBusy || IsDisposed) return;
        using var cts = new CancellationTokenSource();
        _cts = cts;
        IsBusy = true;
        ErrorText = null;
        _retry = null;
        try
        {
            await work(cts.Token);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            StatusText = "– Cancelled.";
        }
        catch (HostFileException ex) when (IsConnectionFailure(ex))
        {
            _retry = retry;
            StatusText = "";
            ErrorText = (describe ?? HostFileMessages.Describe)(ex);
        }
        catch (Exception ex)
        {
            StatusText = "✗ " + HostFileMessages.Describe(ex);
        }
        finally
        {
            _cts = null;
            if (_pinSaveWarning is { } warning)
            {
                _pinSaveWarning = null;
                StatusText = "⚠ " + warning;
            }
            IsBusy = false;
            OnPropertyChanged(nameof(CanRetry));
        }
    }

    /// <summary>A closed window has no one to ask, so the answer is Cancel.</summary>
    public async Task<ConfirmOutcome> AskAsync(ConfirmationRequest request)
    {
        if (IsDisposed) return new ConfirmOutcome(ConfirmChoice.Cancel, false);
        Confirmation = request;
        try
        {
            return await request.Answer;
        }
        finally
        {
            Confirmation = null;
        }
    }

    /// <summary>Drops a pending Retry with its banner: the operation it belongs to has been closed away from.</summary>
    public void DropRetry()
    {
        _retry = null;
        ErrorText = null;
        OnPropertyChanged(nameof(CanRetry));
    }

    /// <summary>A warning for the status line: at once when nothing runs, else once the running operation ends,
    /// so it replaces that operation's own line rather than being overwritten by it (the pin save).</summary>
    public void WarnWhenIdle(string message)
    {
        if (IsBusy) _pinSaveWarning = message;
        else StatusText = "⚠ " + message;
    }

    [RelayCommand]
    private async Task RetryAsync()
    {
        var retry = _retry;
        _retry = null;
        ErrorText = null;
        if (retry is not null) await retry();
    }

    [RelayCommand(CanExecute = nameof(IsBusy))]
    private void Cancel()
    {
        StatusText = "⟳ Cancelling…";
        _cts?.Cancel();
        Confirmation?.CancelCommand.Execute(null);
    }

    /// <summary>Null while nothing runs; otherwise a task that completes when the running operation ends, so a
    /// waiting filter never polls.</summary>
    internal Task? WhenIdle()
    {
        lock (_idleLock)
        {
            if (!IsBusy) return null;
            return (_idle ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).Task;
        }
    }

    private void SignalIdle()
    {
        lock (_idleLock)
        {
            _idle?.TrySetResult();
            _idle = null;
        }
    }

    /// <summary>The failures that stop a whole operation and earn the banner: the host cannot be reached, the
    /// sign-in failed, the certificate was refused, or the host is not one this release supports.</summary>
    public static bool IsConnectionFailure(HostFileException ex) =>
        ex.Kind is HostFileErrorKind.Unreachable or HostFileErrorKind.Unauthenticated or HostFileErrorKind.CertificateRejected
            or HostFileErrorKind.Unsupported;

    public void Dispose()
    {
        if (IsDisposed) return;
        IsDisposed = true;
        _cts?.Cancel();
        Confirmation?.CancelCommand.Execute(null);
    }
}
```

- [ ] **Step 4: Run the runner's tests**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~BrowserOperationsTests" 2>&1 | grep -E "error CS|Passed!|Failed!" | head -5`
Expected: `Passed!` 13 tests (the Theory counts as six).

- [ ] **Step 5: Make the dataset view model forward to it**

In `src/LizTerm.App/ViewModels/MvsmfBrowserViewModel.cs`:

Remove the fields `_cts`, `_retry`, `_disposed`, `_pinSaveWarning`; the `[ObservableProperty]` declarations of `_isBusy` (with its `NotifyPropertyChangedFor`), `_statusText`, `_errorText` and `_confirmation`; the members `IsIdle`, `HasError`, `CanRetry`, `HasConfirmation`, `partial void OnIsBusyChanged`, `RetryAsync`, `Cancel`, `RunExclusiveAsync`, `AskAsync`, `DropRetry`. Add `using System.ComponentModel;` and, in their place:

```csharp
    /// <summary>The runner both tabs share (USS spec §4.4). The window binds to this view model's forwarding
    /// properties below, so its bindings and the older tests see the same names as before the extraction.</summary>
    public BrowserOperations Ops { get; }

    public bool IsBusy => Ops.IsBusy;
    public bool IsIdle => Ops.IsIdle;
    public string StatusText { get => Ops.StatusText; set => Ops.StatusText = value; }
    public string? ErrorText { get => Ops.ErrorText; set => Ops.ErrorText = value; }
    public bool HasError => Ops.HasError;
    public bool CanRetry => Ops.CanRetry;
    public ConfirmationRequest? Confirmation { get => Ops.Confirmation; set => Ops.Confirmation = value; }
    public bool HasConfirmation => Ops.HasConfirmation;
    public IAsyncRelayCommand RetryCommand => Ops.RetryCommand;
    public IRelayCommand CancelCommand => Ops.CancelCommand;

    /// <summary>Re-raises the runner's changes under this view model's names. IsBusy first notifies the commands,
    /// as the generated partial hook did before the extraction, then the property; IsIdle carries
    /// CanChooseDataset with it, as its NotifyPropertyChangedFor did.</summary>
    private void OnOperationsChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(BrowserOperations.IsBusy):
                NotifyCommands();
                OnPropertyChanged(nameof(IsBusy));
                break;
            case nameof(BrowserOperations.IsIdle):
                OnPropertyChanged(nameof(IsIdle));
                OnPropertyChanged(nameof(CanChooseDataset));
                break;
            case { } name:
                OnPropertyChanged(name);
                break;
        }
    }

    private Task RunExclusiveAsync(Func<CancellationToken, Task> work, Func<Task>? retry = null, Func<Exception, string>? describe = null) =>
        Ops.RunExclusiveAsync(work, retry, describe);

    private Task<ConfirmOutcome> AskAsync(ConfirmationRequest request) => Ops.AskAsync(request);

    private void DropRetry() => Ops.DropRetry();
```

In the constructor, before `_access.PinSaveFailed += OnPinSaveFailed;`, add `Ops = new BrowserOperations();` and `Ops.PropertyChanged += OnOperationsChanged;`. Replace `OnPinSaveFailed` with:

```csharp
    /// <summary>The operation that accepted the pin goes on; the warning replaces its status line when it ends.</summary>
    private void OnPinSaveFailed(object? sender, string message) => _dispatch(() =>
    {
        if (!Ops.IsDisposed) Ops.WarnWhenIdle(message);
    });
```

In `NotifyCommands`, delete the line `CancelCommand.NotifyCanExecuteChanged();` (the runner notifies its own). Replace `Dispose` with:

```csharp
    public void Dispose()
    {
        if (Ops.IsDisposed) return;
        _access.PinSaveFailed -= OnPinSaveFailed;
        _filterDebounce?.Cancel();
        Ops.Dispose();
        _connection.Dispose();
    }
```

In `MvsmfBrowserViewModel.Downloads.cs`, replace the `IsConnectionFailure` method with `private static bool IsConnectionFailure(HostFileException ex) => BrowserOperations.IsConnectionFailure(ex);`. In `MvsmfBrowserViewModel.Paging.cs`, delete `_idleLock`, `_idle`, `WhenIdle` and `SignalIdle`, replace the one call `WhenIdle()` with `Ops.WhenIdle()`, and `_disposed` (line ~207) with `Ops.IsDisposed`.

- [ ] **Step 6: Build and run the whole App test project**

Run: `dotnet build LizTerm.slnx --no-incremental 2>&1 | grep -E " warning | error " | head; dotnet test tests/LizTerm.App.Tests 2>&1 | grep -E "Passed!|Failed!|\[FAIL\]" | head -10`
Expected: no warning or error lines; `Passed!` with every test (1,805 before this task plus 13). Every existing browser and window test is the witness that the forwarding preserved the order of notifications; a failure there is a forwarding bug, not a test to edit.

- [ ] **Step 7: Commit**

```bash
git add src/LizTerm.App/ViewModels/BrowserOperations.cs src/LizTerm.App/ViewModels/MvsmfBrowserViewModel.cs src/LizTerm.App/ViewModels/MvsmfBrowserViewModel.Downloads.cs src/LizTerm.App/ViewModels/MvsmfBrowserViewModel.Paging.cs tests/LizTerm.App.Tests/ViewModels/BrowserOperationsTests.cs
git commit -m "mvsMF Access: the operation runner is its own object (#176)

BrowserOperations holds the busy flag, cancel, the status line, the
banner with Retry, the confirmation strip and the idle signal; the
dataset view model forwards to it under its old names, so the window
and the tests are unchanged. The USS tab's view model will share it.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 2: `BrowserTransfers` — the download batch, shared

**Files:**
- Create: `src/LizTerm.App/ViewModels/BrowserTransfers.cs`
- Modify: `src/LizTerm.App/ViewModels/MvsmfBrowserViewModel.Downloads.cs`, `src/LizTerm.App/ViewModels/MvsmfBrowserViewModel.View.cs`

**Interfaces:**
- Consumes: Task 1's `BrowserOperations.IsConnectionFailure`.
- Produces: `internal static class BrowserTransfers` with `const int ParallelDownloads = 2`; `Task<bool> DownloadOneAsync(HostFileConnection connection, EtagMemory etags, Action<Action> dispatch, HostPath path, string file, DownloadOptions options, Action<string> show, CancellationToken token, CancellationToken userToken)`; `Task<int> DownloadManyAsync(HostFileConnection connection, EtagMemory etags, Action<Action> dispatch, IReadOnlyList<DownloadItem> plan, DownloadOptions options, CancellationToken token)`; `sealed record DownloadItem(HostPath Path, string File, Action<string> Show)`; `string Bytes(long count)`; `sealed class RowProgress(Action<Action> dispatch, Action<long> show) : IProgress<long>` with `Close()`.

- [ ] **Step 1: Create the helper**

Create `src/LizTerm.App/ViewModels/BrowserTransfers.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Globalization;
using System.Runtime.ExceptionServices;
using LizTerm.App.HostFiles;
using LizTerm.Core.HostFiles;

namespace LizTerm.App.ViewModels;

/// <summary>One item of a download batch: where it comes from, where it goes, and where its words go (a row's
/// status, or the status line).</summary>
internal sealed record DownloadItem(HostPath Path, string File, Action<string> Show);

/// <summary>The download side both tabs share: one transfer reported where its caller says, and the batch of
/// them, two at a time, stopped as one by a connection failure. Knows nothing about rows or panes.</summary>
internal static class BrowserTransfers
{
    public const int ParallelDownloads = 2;

    /// <summary>One transfer. <paramref name="token"/> stops it; <paramref name="userToken"/> says whether the user
    /// asked for that, as opposed to a batch stopped by another item's connection failure. The stamp is remembered
    /// only once the file is in place (spec §5.2). A connection failure is rethrown for the banner; any other failure
    /// is the item's own words and false.</summary>
    public static async Task<bool> DownloadOneAsync(HostFileConnection connection, EtagMemory etags, Action<Action> dispatch,
        HostPath path, string file, DownloadOptions options, Action<string> show, CancellationToken token, CancellationToken userToken)
    {
        var progress = new RowProgress(dispatch, bytes => show($"⟳ Running · {Bytes(bytes)} bytes"));
        show("⟳ Running");
        try
        {
            var result = await connection.RunAsync(service => HostFileTransfer.DownloadAsync(service, path, file, options, progress, token));
            etags.Remember(path, result.Etag);
            progress.Close();
            show($"✓ Done · {Bytes(result.BytesWritten)} bytes");
            return true;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            progress.Close();
            show(userToken.IsCancellationRequested ? "– Cancelled" : "– Stopped");
            return false;
        }
        catch (HostFileException ex) when (BrowserOperations.IsConnectionFailure(ex))
        {
            progress.Close();
            show("– Stopped");
            throw;
        }
        catch (Exception ex)
        {
            progress.Close();
            show("✗ Failed: " + HostFileMessages.Describe(ex));
            return false;
        }
    }

    /// <summary>The batch: <see cref="ParallelDownloads"/> at a time. A connection failure stops the whole batch
    /// through a linked source and is rethrown once, so the banner offers Retry; the user's own cancel is still told
    /// apart through <paramref name="token"/>. Returns how many items finished.</summary>
    public static async Task<int> DownloadManyAsync(HostFileConnection connection, EtagMemory etags, Action<Action> dispatch,
        IReadOnlyList<DownloadItem> plan, DownloadOptions options, CancellationToken token)
    {
        using var batch = CancellationTokenSource.CreateLinkedTokenSource(token);
        ExceptionDispatchInfo? connectionFailure = null;
        using var slots = new SemaphoreSlim(ParallelDownloads);
        var results = await Task.WhenAll(plan.Select(async item =>
        {
            try
            {
                await slots.WaitAsync(batch.Token);
            }
            catch (OperationCanceledException)
            {
                item.Show(token.IsCancellationRequested ? "– Cancelled" : "– Stopped");
                return false;
            }
            try
            {
                return await DownloadOneAsync(connection, etags, dispatch, item.Path, item.File, options, item.Show, batch.Token, token);
            }
            catch (HostFileException ex) when (BrowserOperations.IsConnectionFailure(ex))
            {
                Interlocked.CompareExchange(ref connectionFailure, ExceptionDispatchInfo.Capture(ex), null);
                batch.Cancel();
                return false;
            }
            finally
            {
                slots.Release();
            }
        }));
        if (!token.IsCancellationRequested) connectionFailure?.Throw();
        return results.Count(ok => ok);
    }

    public static string Bytes(long count) => count.ToString("N0", CultureInfo.InvariantCulture);

    /// <summary>Progress arrives on a backend thread and goes through the dispatcher; once the transfer's result is
    /// shown, a report still in the queue must not overwrite it.</summary>
    public sealed class RowProgress(Action<Action> dispatch, Action<long> show) : IProgress<long>
    {
        private volatile bool _closed;

        public void Close() => _closed = true;

        public void Report(long value) => dispatch(() =>
        {
            if (!_closed) show(value);
        });
    }
}
```

- [ ] **Step 2: Use it from the dataset view model**

In `src/LizTerm.App/ViewModels/MvsmfBrowserViewModel.Downloads.cs`: delete `ParallelDownloads`, `DownloadOneAsync`, `Bytes` and the nested `RowProgress` class, and the `using System.Runtime.ExceptionServices;` and `using System.Globalization;` lines they needed. Replace the single-file branch's call with:

```csharp
            if (await BrowserTransfers.DownloadOneAsync(_connection, _access.Etags, _dispatch, path, file, options,
                    text => { if (row is not null) row.Status = text; else StatusText = text; }, token, token))
                StatusText = $"✓ Downloaded {path} to {file}.";
            else if (row is not null) StatusText = row.Status;
            return;
```

and replace everything from `var plan = new List<(MemberRow Row, string File)>();` to the line `connectionFailure?.Throw();` inclusive with:

```csharp
        var plan = new List<DownloadItem>();
        bool? replaceAll = null;
        foreach (var member in members)
        {
            var file = Path.Combine(folder, member.Name + extension);
            if (File.Exists(file))
            {
                var replace = replaceAll;
                if (replace is null)
                {
                    var answer = await AskAsync(new ConfirmationRequest(
                        $"{Path.GetFileName(file)} already exists in {folder}.", "Replace", "Skip", offersApplyToAll: true));
                    if (answer.Choice == ConfirmChoice.Cancel)
                    {
                        foreach (var row in members) row.Status = "";
                        StatusText = "– Download cancelled.";
                        return;
                    }
                    replace = answer.Choice == ConfirmChoice.Primary;
                    if (answer.ApplyToAll) replaceAll = replace;
                }
                if (replace == false)
                {
                    member.Status = "– Skipped: the file exists";
                    continue;
                }
            }
            member.Status = "⟳ Waiting";
            var target = member;
            plan.Add(new DownloadItem(member.Path, file, text => target.Status = text));
        }

        var done = await BrowserTransfers.DownloadManyAsync(_connection, _access.Etags, _dispatch, plan, options, token);
        if (token.IsCancellationRequested)
        {
            StatusText = "– Download cancelled.";
            return;
        }
```

so the method ends with the existing `StatusText = $"{(done == members.Count ? "✓" : "⚠")} Downloaded {done} of {Plural(members.Count, "member")} to {folder}.";` (delete the old `var done = results.Count(ok => ok);` line). `IsConnectionFailure` stays as the one-line forwarder Task 1 left, since the delete and upload partials use it.

In `src/LizTerm.App/ViewModels/MvsmfBrowserViewModel.View.cs`, change `new RowProgress(_dispatch, bytes => StatusText = $"⟳ Reading {path} · {Bytes(bytes)} bytes")` to `new BrowserTransfers.RowProgress(_dispatch, bytes => StatusText = $"⟳ Reading {path} · {BrowserTransfers.Bytes(bytes)} bytes")`.

- [ ] **Step 3: Build and run the download and view tests, then the project**

Run: `dotnet build LizTerm.slnx --no-incremental 2>&1 | grep -E " warning | error " | head; dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~MvsmfBrowserDownloadTests|FullyQualifiedName~MvsmfBrowserViewTests" 2>&1 | grep -E "Passed!|Failed!|\[FAIL\]"; dotnet test tests/LizTerm.App.Tests 2>&1 | grep -E "Passed!|Failed!"`
Expected: no warning or error lines; both filtered runs `Passed!`; the project `Passed!`. The download tests (a batch stopped by a connection failure says `– Stopped` on the rows and offers Retry; a user cancel says `– Cancelled`; the folder question with Apply to all) are the witness that the batch moved intact.

- [ ] **Step 4: Commit**

```bash
git add src/LizTerm.App/ViewModels/BrowserTransfers.cs src/LizTerm.App/ViewModels/MvsmfBrowserViewModel.Downloads.cs src/LizTerm.App/ViewModels/MvsmfBrowserViewModel.View.cs
git commit -m "mvsMF Access: the download batch is a shared helper (#176)

BrowserTransfers carries one transfer and the two-at-a-time batch with
its connection-failure stop, so the USS tab can download the way the
Members pane does.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 3: `UssBrowserViewModel` — the path row, listing, descend, Up, Refresh

**Files:**
- Create: `src/LizTerm.App/ViewModels/UssBrowserRows.cs`, `src/LizTerm.App/ViewModels/UssBrowserViewModel.cs`
- Create: `tests/LizTerm.App.Tests/ViewModels/UssTestHost.cs`, `tests/LizTerm.App.Tests/ViewModels/UssBrowserListingTests.cs`

**Interfaces:**
- Consumes: Task 1's `BrowserOperations`.
- Produces: `DirectoryRow(HostPath parent, HostFileEntry entry)` with `Name`, `HostPath? Path`, `Modified`, `IsUsable`, `DisplayName`; `FileRow(HostPath parent, HostFileEntry entry)` with `Name`, `HostPath? Path`, `IsFile`, `Size`, `Modified`, `DisplayName`, observable `Status`; `UssBrowserViewModel(BrowserOperations ops, HostFileAccess access, HostFileConnection connection, IFilePicker picker, Action<Action> dispatch)` with `static string StartPath(string? userid)`, `ObservableCollection<DirectoryRow> Directories`, `ObservableCollection<FileRow> Files`, observable `string Path`, `HostPath? Current`, `DirectoryRow? SelectedDirectory`, `bool Truncated`, `bool IsBusy`, `bool HasCurrent`, `string FilesTitle`, `string DirectoriesFooter`, `string FilesFooter`, `IReadOnlyList<FileRow> SelectedFiles`, `void SetSelectedFiles(IEnumerable<FileRow>)`, `event Action<IReadOnlyList<FileRow>>? SelectFilesRequested`, commands `GoCommand`, `UpCommand`, `OpenDirectoryCommand`, `RefreshCommand`, `Task EnsureListedAsync()`, `internal Task<bool> ListCoreAsync(string text, bool keepSelection, CancellationToken token)`, `internal static HostPath? ChildOrNull(HostPath parent, string name)`, `internal static string FormatModified(DateTimeOffset? when)`, `private void NotifyCommands()` (later tasks add their commands to it), `private static string Counted(int count, string one, string many)`; `UssTestHost.Create(userid = "IBMUSER", seed = null)` with `Vm`, `Ops`, `Host`, `Picker`, `Access`, `Standard(host)`, `ListAsync(path)`, `Select(names…)`, `AskedAsync(running)`.

- [ ] **Step 1: Write the test host and the failing tests**

Create `tests/LizTerm.App.Tests/ViewModels/UssTestHost.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.App.HostFiles;
using LizTerm.App.Tests.Fakes;
using LizTerm.App.ViewModels;
using LizTerm.Core.Session;

namespace LizTerm.App.Tests.ViewModels;

/// <summary>A <see cref="UssBrowserViewModel"/> over the fake host, on its own runner, seeded by
/// <see cref="Standard"/>: the home directory <c>/u/ibmuser</c> holding <c>notes</c> (a subdirectory
/// <c>drafts</c>, two text files and a binary one) and <c>old</c>, beside <c>/tmp</c> and <c>/u/mvsce02</c>.</summary>
internal sealed record UssTestHost(
    UssBrowserViewModel Vm,
    BrowserOperations Ops,
    FakeHostFileService Host,
    FakeFilePicker Picker,
    HostFileAccess Access)
{
    public static UssTestHost Create(string? userid = "IBMUSER", Action<FakeHostFileService>? seed = null)
    {
        var host = new FakeHostFileService();
        (seed ?? Standard)(host);
        var access = new HostFileAccess(
            new SessionProfile { Name = "MVS/CE", Host = "mvs", HostFilesUrl = "http://mvs:8080", HostFilesUserid = userid },
            (_, _, _) => host, savePin: null);
        var ops = new BrowserOperations();
        var picker = new FakeFilePicker();
        var vm = new UssBrowserViewModel(ops, access, access.Connect(new FakeCredentialPrompt(), new FakeCertificatePrompt()), picker, action => action());
        return new UssTestHost(vm, ops, host, picker, access);
    }

    public static void Standard(FakeHostFileService host)
    {
        host.AddDirectory("/tmp");
        host.AddDirectory("/u/mvsce02");
        host.AddDirectory("/u/ibmuser/old");
        host.AddDirectory("/u/ibmuser/notes/drafts");
        host.AddFile("/u/ibmuser/notes/README.txt", "hello", "world");
        host.AddFile("/u/ibmuser/notes/todo.md", "- x");
        host.AddBinaryFile("/u/ibmuser/notes/data.bin", [1, 2, 3]);
    }

    /// <summary>Types <paramref name="path"/> into the path row and presses Go.</summary>
    public async Task ListAsync(string path)
    {
        Vm.Path = path;
        await Vm.GoCommand.ExecuteAsync(null);
    }

    /// <summary>Sets the file selection the window would push.</summary>
    public void Select(params string[] files) => Vm.SetSelectedFiles(Vm.Files.Where(f => files.Contains(f.Name)));

    /// <summary>Waits for the running operation to put its question up, or to end without one.</summary>
    public async Task<UssTestHost> AskedAsync(Task running)
    {
        await Wait.UntilAsync(() => Ops.Confirmation is not null || running.IsCompleted, "the question");
        return this;
    }
}
```

Create `tests/LizTerm.App.Tests/ViewModels/UssBrowserListingTests.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.App.ViewModels;
using LizTerm.Core.HostFiles;

namespace LizTerm.App.Tests.ViewModels;

public class UssBrowserListingTests
{
    [Theory]
    [InlineData("IBMUSER", "/u/ibmuser")]
    [InlineData(" Mvsce02 ", "/u/mvsce02")]
    [InlineData(null, "/")]
    [InlineData("", "/")]
    [InlineData("a\tb", "/")]
    public void The_start_path_is_the_home_directory_in_lower_case_or_the_root(string? userid, string expected) =>
        Assert.Equal(expected, UssBrowserViewModel.StartPath(userid));

    [Fact]
    public async Task The_first_show_lists_the_start_path_once()
    {
        var t = UssTestHost.Create();
        Assert.Equal("/u/ibmuser", t.Vm.Path);
        Assert.Null(t.Vm.Current);
        Assert.Equal("", t.Vm.DirectoriesFooter);
        Assert.Equal("Files", t.Vm.FilesTitle);

        await t.Vm.EnsureListedAsync();
        await t.Vm.EnsureListedAsync();

        Assert.Equal("/u/ibmuser", t.Vm.Current!.UnixPath);
        Assert.Equal(new[] { "notes", "old" }, t.Vm.Directories.Select(d => d.Name));
        Assert.Empty(t.Vm.Files);
        Assert.Equal("2 directories · none selected", t.Vm.DirectoriesFooter);
        Assert.Equal("No files", t.Vm.FilesFooter);
        Assert.Equal("/u/ibmuser", t.Vm.FilesTitle);
        Assert.Equal("✓ Listed /u/ibmuser · 2 directories, 0 files.", t.Ops.StatusText);
        Assert.Equal(1, t.Host.CallsSnapshot().Count(c => c == "listdir:/u/ibmuser"));
    }

    [Fact]
    public async Task Go_lists_a_typed_path_and_writes_it_back_trimmed()
    {
        var t = UssTestHost.Create();

        await t.ListAsync("  /u/ibmuser/notes ");

        Assert.Equal("/u/ibmuser/notes", t.Vm.Path);
        Assert.Equal("/u/ibmuser/notes", t.Vm.Current!.UnixPath);
        Assert.Equal(new[] { "drafts" }, t.Vm.Directories.Select(d => d.Name));
        Assert.Equal(new[] { "README.txt", "data.bin", "todo.md" }, t.Vm.Files.Select(f => f.Name));
        Assert.Equal("12", t.Vm.Files[0].Size);
        Assert.Equal("3", t.Vm.Files[1].Size);
        Assert.All(t.Vm.Files, f => Assert.True(f.IsFile));
        Assert.Equal("/u/ibmuser/notes/README.txt", t.Vm.Files[0].Path!.UnixPath);
        Assert.Equal("1 directory · none selected", t.Vm.DirectoriesFooter);
        Assert.Equal("3 files · none selected", t.Vm.FilesFooter);
        Assert.Equal("✓ Listed /u/ibmuser/notes · 1 directory, 3 files.", t.Ops.StatusText);
    }

    [Fact]
    public async Task Rows_are_sorted_by_name_whatever_order_the_host_used()
    {
        var t = UssTestHost.Create();
        await t.ListAsync("/u/ibmuser/notes");
        var sorted = t.Vm.Files.Select(f => f.Name).ToList();
        Assert.Equal(sorted.OrderBy(n => n, StringComparer.Ordinal), sorted);
    }

    [Fact]
    public async Task Opening_a_directory_descends_and_up_climbs_until_the_root()
    {
        var t = UssTestHost.Create();
        await t.Vm.EnsureListedAsync();
        t.Vm.SelectedDirectory = t.Vm.Directories.Single(d => d.Name == "notes");
        Assert.True(t.Vm.OpenDirectoryCommand.CanExecute(null));

        await t.Vm.OpenDirectoryCommand.ExecuteAsync(null);
        Assert.Equal("/u/ibmuser/notes", t.Vm.Current!.UnixPath);
        Assert.Null(t.Vm.SelectedDirectory);

        await t.Vm.UpCommand.ExecuteAsync(null);
        Assert.Equal("/u/ibmuser", t.Vm.Current!.UnixPath);
        await t.Vm.UpCommand.ExecuteAsync(null);
        await t.Vm.UpCommand.ExecuteAsync(null);
        Assert.Equal("/", t.Vm.Current!.UnixPath);
        Assert.Equal(new[] { "tmp", "u" }, t.Vm.Directories.Select(d => d.Name));
        Assert.False(t.Vm.UpCommand.CanExecute(null));
        Assert.False(t.Vm.OpenDirectoryCommand.CanExecute(null));
    }

    [Theory]
    [InlineData("u/ibmuser", "✗ A path must start with '/'.")]
    [InlineData("MVSCE02.CNTL", "✗ A path must start with '/'.")]
    [InlineData("", "✗ Enter a path.")]
    [InlineData("/u/../x", "✗ A path cannot contain a '.' or '..' segment.")]
    public async Task A_path_the_rules_refuse_is_a_status_line_and_no_request(string typed, string expected)
    {
        var t = UssTestHost.Create();
        await t.Vm.EnsureListedAsync();
        var calls = t.Host.CallsSnapshot().Length;

        await t.ListAsync(typed);

        Assert.Equal(expected, t.Ops.StatusText);
        Assert.Equal("/u/ibmuser", t.Vm.Current!.UnixPath);
        Assert.Equal(typed, t.Vm.Path);
        Assert.Equal(calls, t.Host.CallsSnapshot().Length);
    }

    [Fact]
    public async Task A_missing_path_keeps_the_last_listing_and_the_typed_text()
    {
        var t = UssTestHost.Create();
        await t.Vm.EnsureListedAsync();

        await t.ListAsync("/u/nobody");

        Assert.Equal("✗ File not found: /u/nobody", t.Ops.StatusText);
        Assert.Equal("/u/nobody", t.Vm.Path);
        Assert.Equal("/u/ibmuser", t.Vm.Current!.UnixPath);
        Assert.Equal(new[] { "notes", "old" }, t.Vm.Directories.Select(d => d.Name));
        Assert.Null(t.Ops.ErrorText);
    }

    [Fact]
    public async Task A_file_path_is_refused_by_the_host_and_reported()
    {
        var t = UssTestHost.Create();
        await t.ListAsync("/u/ibmuser/notes/todo.md");
        Assert.StartsWith("✗ /u/ibmuser/notes/todo.md: The host refused the request", t.Ops.StatusText);
        Assert.Null(t.Vm.Current);
    }

    [Fact]
    public async Task A_connection_failure_is_the_banner_and_retry_lists()
    {
        var t = UssTestHost.Create();
        t.Host.Failures["listdir:/u/ibmuser"] = new HostFileException(HostFileErrorKind.Unreachable, "/u/ibmuser: cannot reach the host.");

        await t.Vm.EnsureListedAsync();

        Assert.True(t.Ops.HasError);
        Assert.True(t.Ops.CanRetry);
        Assert.Null(t.Vm.Current);
        t.Host.Failures.Clear();
        await t.Ops.RetryCommand.ExecuteAsync(null);
        Assert.False(t.Ops.HasError);
        Assert.Equal("/u/ibmuser", t.Vm.Current!.UnixPath);
    }

    [Fact]
    public async Task Refresh_lists_again_and_keeps_the_selection_by_name()
    {
        var t = UssTestHost.Create();
        await t.ListAsync("/u/ibmuser/notes");
        t.Vm.SelectedDirectory = t.Vm.Directories[0];
        t.Select("todo.md", "data.bin");
        IReadOnlyList<FileRow>? requested = null;
        t.Vm.SelectFilesRequested += rows => requested = rows;
        t.Host.AddFile("/u/ibmuser/notes/new.txt", "n");

        await t.Vm.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(new[] { "README.txt", "data.bin", "new.txt", "todo.md" }, t.Vm.Files.Select(f => f.Name));
        Assert.Equal("drafts", t.Vm.SelectedDirectory!.Name);
        Assert.Equal(new[] { "data.bin", "todo.md" }, requested!.Select(f => f.Name));
        Assert.Equal(new[] { "data.bin", "todo.md" }, t.Vm.SelectedFiles.Select(f => f.Name));
        Assert.Equal("4 files · 2 selected", t.Vm.FilesFooter);
        Assert.Equal("1 directory · 1 selected", t.Vm.DirectoriesFooter);
    }

    [Fact]
    public async Task The_commands_are_off_while_an_operation_runs()
    {
        var t = UssTestHost.Create();
        t.Host.Gate = new TaskCompletionSource();
        var listing = t.Vm.EnsureListedAsync();
        await Wait.UntilAsync(() => t.Vm.IsBusy, "the listing to start");

        Assert.False(t.Vm.GoCommand.CanExecute(null));
        Assert.False(t.Vm.RefreshCommand.CanExecute(null));
        Assert.False(t.Vm.UpCommand.CanExecute(null));
        t.Host.Gate.SetResult();
        await listing;
        Assert.True(t.Vm.GoCommand.CanExecute(null));
        Assert.True(t.Vm.RefreshCommand.CanExecute(null));
    }

    [Fact]
    public void A_row_that_is_not_a_regular_file_or_whose_name_the_rules_refuse_says_so()
    {
        var parent = HostPath.ForUnix("/u/ibmuser");
        var link = new FileRow(parent, new HostFileEntry("link", HostFileEntryKind.Other, Unix: new UnixFileAttributes(0, null)));
        Assert.False(link.IsFile);
        Assert.Equal("link (not a file)", link.DisplayName);
        Assert.Equal("/u/ibmuser/link", link.Path!.UnixPath);

        var bad = new FileRow(parent, new HostFileEntry("a\tb", HostFileEntryKind.File, Unix: new UnixFileAttributes(5, null)));
        Assert.False(bad.IsFile);
        Assert.Null(bad.Path);
        Assert.Equal("a\tb (not usable)", bad.DisplayName);

        var directory = new DirectoryRow(parent, new HostFileEntry("a\tb", HostFileEntryKind.Directory, Unix: new UnixFileAttributes(128, null)));
        Assert.False(directory.IsUsable);
        Assert.Equal("a\tb (not usable)", directory.DisplayName);

        var when = new DateTimeOffset(2026, 9, 22, 9, 12, 0, TimeSpan.Zero);
        var file = new FileRow(parent, new HostFileEntry("f", HostFileEntryKind.File, Unix: new UnixFileAttributes(1204, when)));
        Assert.Equal("1,204", file.Size);
        Assert.Equal(when.ToLocalTime().ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture), file.Modified);
        Assert.Equal("", new FileRow(parent, new HostFileEntry("g", HostFileEntryKind.File)).Modified);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~UssBrowserListingTests" 2>&1 | grep -E "error CS|Passed!|Failed!" | head -5`
Expected: build errors naming `UssBrowserViewModel`, `FileRow`, `DirectoryRow`.

- [ ] **Step 3: The rows**

Create `src/LizTerm.App/ViewModels/UssBrowserRows.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using LizTerm.Core.HostFiles;

namespace LizTerm.App.ViewModels;

/// <summary>One subdirectory of the current directory (USS spec §4.6). <see cref="Path"/> is null for a name the
/// rules refuse (a host can list anything); such a row is shown, dimmed and marked, and cannot be opened or deleted.
/// The words carry the meaning, not the dimming.</summary>
public sealed class DirectoryRow(HostPath parent, HostFileEntry entry)
{
    public string Name { get; } = entry.Name;
    public HostPath? Path { get; } = UssBrowserViewModel.ChildOrNull(parent, entry.Name);
    public string Modified { get; } = UssBrowserViewModel.FormatModified(entry.Unix?.Modified);
    public bool IsUsable => Path is not null;
    public string DisplayName => IsUsable ? Name : $"{Name} (not usable)";
}

/// <summary>One entry of the current directory that is not a subdirectory. A regular file can be downloaded,
/// uploaded over, viewed and deleted; an entry of another kind (<see cref="HostFileEntryKind.Other"/>: a link, a
/// device) or one whose name the rules refuse is listed, dimmed and marked, and the verbs stay off for it.
/// <see cref="Status"/> is the per-file result of the last batch, always a mark and words.</summary>
public sealed partial class FileRow : ObservableObject
{
    public FileRow(HostPath parent, HostFileEntry entry)
    {
        Name = entry.Name;
        Path = UssBrowserViewModel.ChildOrNull(parent, entry.Name);
        IsFile = entry.Kind == HostFileEntryKind.File && Path is not null;
        Size = entry.Unix is { } unix ? unix.Size.ToString("N0", CultureInfo.InvariantCulture) : "";
        Modified = UssBrowserViewModel.FormatModified(entry.Unix?.Modified);
        DisplayName = entry.Kind != HostFileEntryKind.File ? $"{Name} (not a file)"
            : Path is null ? $"{Name} (not usable)"
            : Name;
    }

    public string Name { get; }
    public HostPath? Path { get; }
    public bool IsFile { get; }
    public string Size { get; }
    public string Modified { get; }
    public string DisplayName { get; }

    [ObservableProperty] private string _status = "";
}
```

- [ ] **Step 4: The view model's core**

Create `src/LizTerm.App/ViewModels/UssBrowserViewModel.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LizTerm.App.Files;
using LizTerm.App.HostFiles;
using LizTerm.Core.HostFiles;

namespace LizTerm.App.ViewModels;

/// <summary>The USS tab (USS spec §4.6): one directory at a time, its subdirectories in the left pane and its files
/// in the right, anchored on the path row. Runs on the window's shared <see cref="BrowserOperations"/>, so one
/// operation at a time holds across both tabs and every result lands on the one status line. New… and Delete… are
/// in the Manage partial, Download, Upload and View in the Transfers partial.</summary>
public sealed partial class UssBrowserViewModel : ObservableObject
{
    private readonly BrowserOperations _ops;
    private readonly HostFileAccess _access;
    private readonly HostFileConnection _connection;
    private readonly IFilePicker _picker;
    private readonly Action<Action> _dispatch;
    private List<FileRow> _selectedFiles = [];
    private bool _listed;

    public UssBrowserViewModel(BrowserOperations ops, HostFileAccess access, HostFileConnection connection, IFilePicker picker,
        Action<Action> dispatch)
    {
        _ops = ops;
        _access = access;
        _connection = connection;
        _picker = picker;
        _dispatch = dispatch;
        _path = StartPath(access.Userid);
        _ops.PropertyChanged += OnOperationsChanged;
    }

    /// <summary>Where the tab starts: the user's home, <c>/u/&lt;userid&gt;</c> in lower case, or the root when there
    /// is no userid or the name it makes is one the rules refuse (USS spec §3).</summary>
    public static string StartPath(string? userid)
    {
        var name = (userid ?? "").Trim().ToLowerInvariant();
        var home = "/u/" + name;
        return name.Length > 0 && HostPath.UnixPathError(home) is null ? home : "/";
    }

    public ObservableCollection<DirectoryRow> Directories { get; } = [];
    public ObservableCollection<FileRow> Files { get; } = [];

    /// <summary>The path row's text. Go lists it; a listing that lands writes the listed path back, trimmed.</summary>
    [ObservableProperty] private string _path;

    /// <summary>The directory the panes show; null until a listing has landed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCurrent), nameof(FilesTitle))]
    private HostPath? _current;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DirectoriesFooter))]
    private DirectoryRow? _selectedDirectory;

    /// <summary>The host cut the listing short and offers no way to continue it (USS spec §3).</summary>
    [ObservableProperty] private bool _truncated;

    public bool IsBusy => _ops.IsBusy;
    public bool HasCurrent => Current is not null;

    /// <summary>The Files pane's title: the current path, or "Files" before the first listing.</summary>
    public string FilesTitle => Current?.UnixPath ?? "Files";

    /// <summary>Empty until a listing has landed, so an unlisted directory never reads as an empty one.</summary>
    public string DirectoriesFooter => !_listed ? "" : Directories.Count == 0 ? "No directories"
        : $"{Counted(Directories.Count, "directory", "directories")} · {(SelectedDirectory is null ? "none" : "1")} selected";

    public string FilesFooter => !_listed ? "" : Files.Count == 0 ? "No files"
        : $"{Counted(Files.Count, "file", "files")} · {(_selectedFiles.Count == 0 ? "none" : _selectedFiles.Count.ToString(CultureInfo.InvariantCulture))} selected";

    public IReadOnlyList<FileRow> SelectedFiles => _selectedFiles;

    /// <summary>Raised when an operation wants these file rows selected (a refresh keeping the selection). The
    /// window applies it to its list box, which pushes it back through <see cref="SetSelectedFiles"/>.</summary>
    public event Action<IReadOnlyList<FileRow>>? SelectFilesRequested;

    /// <summary>The window pushes the file list's selection here, as the Members pane does.</summary>
    public void SetSelectedFiles(IEnumerable<FileRow> files)
    {
        _selectedFiles = [.. files];
        OnPropertyChanged(nameof(SelectedFiles));
        OnPropertyChanged(nameof(FilesFooter));
        NotifyCommands();
    }

    partial void OnSelectedDirectoryChanged(DirectoryRow? value) => NotifyCommands();

    private void OnOperationsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(BrowserOperations.IsBusy)) return;
        OnPropertyChanged(nameof(IsBusy));
        NotifyCommands();
    }

    private bool CanGo => !IsBusy;
    private bool CanUp => !IsBusy && Current?.Parent is not null;
    private bool CanOpenDirectory => !IsBusy && SelectedDirectory is { Path: not null };
    private bool CanRefresh => !IsBusy && Current is not null;

    [RelayCommand(CanExecute = nameof(CanGo))]
    private Task GoAsync() => ListPathAsync(Path);

    [RelayCommand(CanExecute = nameof(CanUp))]
    private Task UpAsync() => Current?.Parent is { } parent ? ListPathAsync(parent.UnixPath!) : Task.CompletedTask;

    [RelayCommand(CanExecute = nameof(CanOpenDirectory))]
    private Task OpenDirectoryAsync() => SelectedDirectory is { Path: { } path } ? ListPathAsync(path.UnixPath!) : Task.CompletedTask;

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private Task RefreshAsync() => Current is { } current ? ListPathAsync(current.UnixPath!, keepSelection: true) : Task.CompletedTask;

    /// <summary>The first time the tab is shown: lists the start path, once, so a dataset-only user pays nothing.</summary>
    public Task EnsureListedAsync() => _listed || IsBusy ? Task.CompletedTask : ListPathAsync(Path);

    private Task ListPathAsync(string text, bool keepSelection = false) =>
        _ops.RunExclusiveAsync(token => ListCoreAsync(text, keepSelection, token), () => ListPathAsync(text, keepSelection));

    /// <summary>False when the listing was refused (a path the rules reject, one the host does not have, one that
    /// is a file), with the reason on the status line, the box keeping its text and the panes their last listing,
    /// so an operation that lists on the way to something else knows to stop there.</summary>
    internal async Task<bool> ListCoreAsync(string text, bool keepSelection, CancellationToken token)
    {
        if (!HostPath.TryParse(text, out var parsed, out var problem) || parsed!.Kind != HostPathKind.Unix)
        {
            _ops.StatusText = "✗ " + (parsed is null ? problem : "A path must start with '/'.");
            return false;
        }
        var target = parsed;
        _ops.StatusText = $"⟳ Listing {target}…";
        HostFileListing listing;
        try
        {
            listing = await _connection.RunAsync(service => service.ListDirectoryAsync(target, HostListRequest.All, token));
        }
        catch (HostFileException ex) when (ex.Kind is HostFileErrorKind.NotFound or HostFileErrorKind.InvalidRequest)
        {
            _ops.StatusText = ex.Kind == HostFileErrorKind.NotFound
                ? $"✗ File not found: {target}"
                : $"✗ {target}: {HostFileMessages.Describe(ex)}";
            return false;
        }
        Fill(target, listing, keepSelection);
        return true;
    }

    /// <summary>Fills the panes from a listing, sorted by name (ordinal, as the host compares): the host lists in
    /// its own order. A selection kept by name survives a refresh; the file rows go back through the window.</summary>
    private void Fill(HostPath target, HostFileListing listing, bool keepSelection)
    {
        var keptDirectory = keepSelection ? SelectedDirectory?.Name : null;
        var keptFiles = keepSelection ? _selectedFiles.Select(f => f.Name).ToHashSet(StringComparer.Ordinal) : [];
        Current = target;
        Path = target.UnixPath!;
        Directories.Clear();
        Files.Clear();
        foreach (var entry in listing.Entries.OrderBy(e => e.Name, StringComparer.Ordinal))
        {
            if (entry.Kind == HostFileEntryKind.Directory) Directories.Add(new DirectoryRow(target, entry));
            else Files.Add(new FileRow(target, entry));
        }
        Truncated = listing.Truncated;
        _listed = true;
        SelectedDirectory = Directories.FirstOrDefault(d => d.Name == keptDirectory);
        var kept = Files.Where(f => keptFiles.Contains(f.Name)).ToList();
        SetSelectedFiles(kept);
        if (kept.Count > 0) SelectFilesRequested?.Invoke(kept);
        OnPropertyChanged(nameof(DirectoriesFooter));
        _ops.StatusText = $"✓ Listed {target} · {Counted(Directories.Count, "directory", "directories")}, {Counted(Files.Count, "file", "files")}"
            + (Truncated ? " · more on the host" : "") + ".";
    }

    /// <summary>The entry's path under its directory, or null for a name the rules refuse: a host can list anything.</summary>
    internal static HostPath? ChildOrNull(HostPath parent, string name)
    {
        try
        {
            return parent.Child(name);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>The MODIFIED column: local time to the minute, or empty when the host gave none.</summary>
    internal static string FormatModified(DateTimeOffset? when) =>
        when?.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? "";

    private static string Counted(int count, string one, string many) => count == 1 ? $"1 {one}" : $"{count} {many}";

    /// <summary>Every command whose CanExecute reads the busy flag or a selection. The other partials add theirs.</summary>
    private void NotifyCommands()
    {
        GoCommand.NotifyCanExecuteChanged();
        UpCommand.NotifyCanExecuteChanged();
        OpenDirectoryCommand.NotifyCanExecuteChanged();
        RefreshCommand.NotifyCanExecuteChanged();
    }
}
```

- [ ] **Step 5: Run the tests**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~UssBrowserListingTests" 2>&1 | grep -E "error CS|Passed!|Failed!|\[FAIL\]" | head -8`
Expected: `Passed!`, 20 tests. Then `dotnet build LizTerm.slnx --no-incremental 2>&1 | grep -c " warning "` prints `0`.

- [ ] **Step 6: Commit**

```bash
git add src/LizTerm.App/ViewModels/UssBrowserRows.cs src/LizTerm.App/ViewModels/UssBrowserViewModel.cs tests/LizTerm.App.Tests/ViewModels/UssTestHost.cs tests/LizTerm.App.Tests/ViewModels/UssBrowserListingTests.cs
git commit -m "USS tab view model: the path row, listing, descend, Up and Refresh (#176)

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 4: New… and Delete… on the USS tab

**Files:**
- Create: `src/LizTerm.App/ViewModels/UssBrowserViewModel.Manage.cs`
- Modify: `src/LizTerm.App/ViewModels/BrowserOperations.cs` (`RunThenListAsync`), `src/LizTerm.App/ViewModels/MvsmfBrowserViewModel.Manage.cs` (forward `RunThenListAsync`), `src/LizTerm.App/ViewModels/UssBrowserViewModel.cs` (`NotifyCommands`)
- Create: `tests/LizTerm.App.Tests/ViewModels/UssBrowserManageTests.cs`

**Interfaces:**
- Consumes: Task 3's view model, rows and test host; `BrowserOperations.AskAsync`, `RunExclusiveAsync`.
- Produces: on `BrowserOperations`, `public Task RunThenListAsync(Func<Action<Func<Task>>, CancellationToken, Task> work, Func<Task> retryAll)`; on `UssBrowserViewModel`, commands `NewDirectoryCommand`, `DeleteDirectoryCommand`, `DeleteFilesCommand`, `internal string? NewNameProblem(string name)`, `private const int NamesInQuestion = 5`.

Rulings folded in: a deleted directory's row is dropped and no re-listing follows (as a dataset delete); the verbs stay off for a row that is not a regular file, deleting included.

- [ ] **Step 1: Write the failing tests**

Create `tests/LizTerm.App.Tests/ViewModels/UssBrowserManageTests.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.App.ViewModels;
using LizTerm.Core.HostFiles;

namespace LizTerm.App.Tests.ViewModels;

public class UssBrowserManageTests
{
    [Fact]
    public async Task New_asks_for_a_name_checks_it_creates_and_selects_the_directory()
    {
        var t = UssTestHost.Create();
        await t.Vm.EnsureListedAsync();

        var creating = t.Vm.NewDirectoryCommand.ExecuteAsync(null);
        await t.AskedAsync(creating);
        var question = t.Ops.Confirmation!;
        Assert.Equal("New directory in /u/ibmuser:", question.Message);
        Assert.Equal("Create", question.PrimaryLabel);
        Assert.True(question.HasInput);
        Assert.False(question.CanAnswerPrimary);
        Assert.Equal("Enter a name.", question.InputProblem);
        question.Input = "a/b";
        Assert.Equal("A name cannot contain '/'.", question.InputProblem);
        question.Input = "..";
        Assert.Equal("A name cannot be '.' or '..'.", question.InputProblem);
        question.Input = "notes";
        Assert.Equal("notes already exists in /u/ibmuser.", question.InputProblem);
        question.Input = "a\tb";
        Assert.Equal("A path cannot contain control characters.", question.InputProblem);
        question.Input = " drafts2 ";
        Assert.Null(question.InputProblem);
        Assert.True(question.CanAnswerPrimary);
        question.PrimaryCommand.Execute(null);
        await creating;

        Assert.Contains("mkdir:/u/ibmuser/drafts2", t.Host.CallsSnapshot());
        Assert.Equal(new[] { "drafts2", "notes", "old" }, t.Vm.Directories.Select(d => d.Name));
        Assert.Equal("drafts2", t.Vm.SelectedDirectory!.Name);
        Assert.Equal("✓ Created /u/ibmuser/drafts2.", t.Ops.StatusText);
    }

    [Fact]
    public async Task New_cancelled_creates_nothing()
    {
        var t = UssTestHost.Create();
        await t.Vm.EnsureListedAsync();
        var creating = t.Vm.NewDirectoryCommand.ExecuteAsync(null);
        await t.AskedAsync(creating);
        t.Ops.Confirmation!.CancelCommand.Execute(null);
        await creating;
        Assert.Equal("– Create cancelled.", t.Ops.StatusText);
        Assert.DoesNotContain(t.Host.CallsSnapshot(), c => c.StartsWith("mkdir:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_name_the_host_already_has_is_reported_and_the_list_refreshed()
    {
        var t = UssTestHost.Create();
        await t.Vm.EnsureListedAsync();
        t.Host.AddDirectory("/u/ibmuser/late");
        var creating = t.Vm.NewDirectoryCommand.ExecuteAsync(null);
        await t.AskedAsync(creating);
        t.Ops.Confirmation!.Input = "late";
        t.Ops.Confirmation.PrimaryCommand.Execute(null);
        await creating;

        Assert.Equal("✗ /u/ibmuser/late: a file or directory of that name already exists.", t.Ops.StatusText);
        Assert.Contains(t.Vm.Directories, d => d.Name == "late");
        Assert.Null(t.Ops.ErrorText);
    }

    [Fact]
    public async Task New_is_off_before_a_listing_and_needs_the_current_directory()
    {
        var t = UssTestHost.Create();
        Assert.False(t.Vm.NewDirectoryCommand.CanExecute(null));
        await t.Vm.EnsureListedAsync();
        Assert.True(t.Vm.NewDirectoryCommand.CanExecute(null));
    }

    [Fact]
    public async Task Delete_directory_asks_names_it_and_drops_the_row()
    {
        var t = UssTestHost.Create();
        await t.Vm.EnsureListedAsync();
        t.Access.Etags.Remember(HostPath.ForUnix("/u/ibmuser/old/x.txt"), "stamp-1");
        t.Access.Etags.Remember(HostPath.ForUnix("/u/ibmuser/notes/todo.md"), "stamp-2");
        t.Vm.SelectedDirectory = t.Vm.Directories.Single(d => d.Name == "old");
        Assert.True(t.Vm.DeleteDirectoryCommand.CanExecute(null));

        var deleting = t.Vm.DeleteDirectoryCommand.ExecuteAsync(null);
        await t.AskedAsync(deleting);
        Assert.Equal("Delete directory old and everything in it? This cannot be undone.", t.Ops.Confirmation!.Message);
        Assert.Equal("Delete old", t.Ops.Confirmation.PrimaryLabel);
        t.Ops.Confirmation.PrimaryCommand.Execute(null);
        await deleting;

        Assert.Contains("delete:/u/ibmuser/old", t.Host.CallsSnapshot());
        Assert.Equal(new[] { "notes" }, t.Vm.Directories.Select(d => d.Name));
        Assert.Null(t.Vm.SelectedDirectory);
        Assert.Equal("1 directory · none selected", t.Vm.DirectoriesFooter);
        Assert.Equal("✓ Deleted /u/ibmuser/old.", t.Ops.StatusText);
        Assert.Equal(1, t.Access.Etags.Count);
        Assert.DoesNotContain("/u/ibmuser/old", t.Host.Directories);
    }

    [Fact]
    public async Task Delete_directory_gone_meanwhile_drops_the_row_with_the_reason()
    {
        var t = UssTestHost.Create();
        await t.Vm.EnsureListedAsync();
        t.Host.Directories.Remove("/u/ibmuser/old");
        t.Vm.SelectedDirectory = t.Vm.Directories.Single(d => d.Name == "old");
        var deleting = t.Vm.DeleteDirectoryCommand.ExecuteAsync(null);
        await t.AskedAsync(deleting);
        t.Ops.Confirmation!.PrimaryCommand.Execute(null);
        await deleting;
        Assert.Equal("✗ /u/ibmuser/old: Not found.", t.Ops.StatusText);
        Assert.DoesNotContain(t.Vm.Directories, d => d.Name == "old");
    }

    [Fact]
    public async Task Delete_files_names_up_to_five_and_removes_them()
    {
        var t = UssTestHost.Create(seed: host =>
        {
            UssTestHost.Standard(host);
            for (var i = 1; i <= 7; i++) host.AddFile($"/u/ibmuser/old/f{i}.txt", "x");
        });
        await t.ListAsync("/u/ibmuser/old");
        t.Select("f1.txt", "f2.txt", "f3.txt", "f4.txt", "f5.txt", "f6.txt", "f7.txt");
        Assert.True(t.Vm.DeleteFilesCommand.CanExecute(null));

        var deleting = t.Vm.DeleteFilesCommand.ExecuteAsync(null);
        await t.AskedAsync(deleting);
        Assert.Equal("Delete f1.txt, f2.txt, f3.txt, f4.txt, f5.txt and 2 more from /u/ibmuser/old? This cannot be undone.", t.Ops.Confirmation!.Message);
        Assert.Equal("Delete 7 files", t.Ops.Confirmation.PrimaryLabel);
        t.Ops.Confirmation.PrimaryCommand.Execute(null);
        await deleting;

        Assert.Empty(t.Vm.Files);
        Assert.Equal("No files", t.Vm.FilesFooter);
        Assert.Equal("✓ Deleted 7 of 7 files.", t.Ops.StatusText);
        Assert.Equal(7, t.Host.CallsSnapshot().Count(c => c.StartsWith("delete:/u/ibmuser/old/", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Delete_files_reports_a_failure_and_goes_on()
    {
        var t = UssTestHost.Create();
        await t.ListAsync("/u/ibmuser/notes");
        t.Select("README.txt", "todo.md");
        t.Host.Failures["delete:/u/ibmuser/notes/README.txt"] = new HostFileException(HostFileErrorKind.ServerError, "/u/ibmuser/notes/README.txt: server error (HTTP 500).");

        var deleting = t.Vm.DeleteFilesCommand.ExecuteAsync(null);
        await t.AskedAsync(deleting);
        Assert.Equal("Delete 2 files", t.Ops.Confirmation!.PrimaryLabel);
        t.Ops.Confirmation.PrimaryCommand.Execute(null);
        await deleting;

        Assert.Equal(new[] { "README.txt", "data.bin" }, t.Vm.Files.Select(f => f.Name));
        Assert.Equal("⚠ Deleted 1 of 2 files. ✗ README.txt: Server error.", t.Ops.StatusText);
    }

    [Fact]
    public async Task Delete_files_stopped_by_a_connection_failure_offers_retry_for_the_rest()
    {
        var t = UssTestHost.Create();
        await t.ListAsync("/u/ibmuser/notes");
        t.Select("README.txt", "todo.md");
        t.Host.Failures["delete:/u/ibmuser/notes/todo.md"] = new HostFileException(HostFileErrorKind.Unreachable, "todo.md: cannot reach the host.");
        var deleting = t.Vm.DeleteFilesCommand.ExecuteAsync(null);
        await t.AskedAsync(deleting);
        t.Ops.Confirmation!.PrimaryCommand.Execute(null);
        await deleting;

        Assert.True(t.Ops.CanRetry);
        Assert.DoesNotContain(t.Vm.Files, f => f.Name == "README.txt");
        Assert.Contains(t.Vm.Files, f => f.Name == "todo.md");
        t.Host.Failures.Clear();
        var retrying = t.Ops.RetryCommand.ExecuteAsync(null);
        await t.AskedAsync(retrying);
        Assert.Equal("Delete todo.md from /u/ibmuser/notes? This cannot be undone.", t.Ops.Confirmation!.Message);
        t.Ops.Confirmation.PrimaryCommand.Execute(null);
        await retrying;
        Assert.Equal(new[] { "data.bin" }, t.Vm.Files.Select(f => f.Name));
        Assert.Equal("✓ Deleted 1 of 1 file.", t.Ops.StatusText);
    }

    [Fact]
    public async Task Delete_files_is_off_for_a_row_that_is_not_a_regular_file()
    {
        var t = UssTestHost.Create();
        await t.ListAsync("/u/ibmuser/notes");
        var link = new FileRow(t.Vm.Current!, new HostFileEntry("link", HostFileEntryKind.Other, Unix: new UnixFileAttributes(0, null)));
        t.Vm.SetSelectedFiles([t.Vm.Files[0], link]);
        Assert.False(t.Vm.DeleteFilesCommand.CanExecute(null));
        t.Select("todo.md");
        Assert.True(t.Vm.DeleteFilesCommand.CanExecute(null));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~UssBrowserManageTests" 2>&1 | grep -E "error CS|Passed!|Failed!" | head -5`
Expected: build errors naming `NewDirectoryCommand`, `DeleteDirectoryCommand`, `DeleteFilesCommand`.

- [ ] **Step 3: `RunThenListAsync` moves to the runner**

In `src/LizTerm.App/ViewModels/BrowserOperations.cs`, after `RunExclusiveAsync`, add:

```csharp
    /// <summary>An operation whose second half is a listing (a create, a rename): once the host has done the first
    /// half, a connection failure in the listing must retry only the listing, never ask the host to do the first
    /// half again. The work calls <c>retryWith</c> with the listing's retry at that point.</summary>
    public Task RunThenListAsync(Func<Action<Func<Task>>, CancellationToken, Task> work, Func<Task> retryAll)
    {
        var retry = retryAll;
        return RunExclusiveAsync(token => work(next => retry = next, token), () => retry());
    }
```

In `src/LizTerm.App/ViewModels/MvsmfBrowserViewModel.Manage.cs`, replace the `RunThenListAsync` method body with a forwarder:

```csharp
    private Task RunThenListAsync(Func<Action<Func<Task>>, CancellationToken, Task> work, Func<Task> retryAll) =>
        Ops.RunThenListAsync(work, retryAll);
```

keeping its doc comment's first sentence ("An operation whose second half is a listing…: see `BrowserOperations.RunThenListAsync`.").

- [ ] **Step 4: The Manage partial**

Create `src/LizTerm.App/ViewModels/UssBrowserViewModel.Manage.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using CommunityToolkit.Mvvm.Input;
using LizTerm.App.HostFiles;
using LizTerm.Core.HostFiles;

namespace LizTerm.App.ViewModels;

/// <summary>New… and Delete… (USS spec §4.6): a directory is created under the current one from a name asked for
/// in the strip; a directory is deleted with everything in it after one question; files are deleted one by one
/// after one question naming up to five. The stamp memory follows every change.</summary>
public sealed partial class UssBrowserViewModel
{
    private const int NamesInQuestion = 5;

    private bool CanNewDirectory => !IsBusy && Current is not null;
    private bool CanDeleteDirectory => !IsBusy && SelectedDirectory is { Path: not null };
    private bool CanDeleteFiles => !IsBusy && _selectedFiles.Count > 0 && _selectedFiles.All(f => f.IsFile);

    /// <summary>Why <paramref name="name"/> cannot be a new directory under the current one, or null: one segment
    /// (no <c>/</c>, not <c>.</c> or <c>..</c>), not a name already listed, and a path the rules accept. Surrounding
    /// blanks are ignored, as the strip's box is typed into.</summary>
    internal string? NewNameProblem(string name)
    {
        var trimmed = name.Trim();
        if (trimmed.Length == 0) return "Enter a name.";
        if (trimmed.Contains('/')) return "A name cannot contain '/'.";
        if (trimmed is "." or "..") return "A name cannot be '.' or '..'.";
        if (Current is not { } current) return "Choose a directory first.";
        if (Directories.Any(d => d.Name == trimmed) || Files.Any(f => f.Name == trimmed)) return $"{trimmed} already exists in {current}.";
        return HostPath.UnixPathError(current.UnixPath == "/" ? "/" + trimmed : current.UnixPath + "/" + trimmed);
    }

    // ---- new directory ----

    [RelayCommand(CanExecute = nameof(CanNewDirectory))]
    private Task NewDirectoryAsync() => Current is { } current ? NewDirectoryAsync(current) : Task.CompletedTask;

    /// <summary>A retry before the host has created asks again; once it has, only the listing is retried.</summary>
    private Task NewDirectoryAsync(HostPath parent) =>
        _ops.RunThenListAsync((retryWith, token) => NewDirectoryCoreAsync(parent, retryWith, token), () => NewDirectoryAsync(parent));

    private async Task NewDirectoryCoreAsync(HostPath parent, Action<Func<Task>> retryWith, CancellationToken token)
    {
        var question = new ConfirmationRequest($"New directory in {parent}:", "Create", input: "", inputRule: NewNameProblem);
        var answer = await _ops.AskAsync(question);
        if (answer.Choice != ConfirmChoice.Primary)
        {
            _ops.StatusText = "– Create cancelled.";
            return;
        }
        // The rule passed, so Child cannot refuse.
        var path = parent.Child(question.Input.Trim());
        _ops.StatusText = $"⟳ Creating {path}…";
        try
        {
            await _connection.RunAsync(service => service.CreateDirectoryAsync(path, token));
        }
        catch (HostFileException ex) when (ex.Kind == HostFileErrorKind.AlreadyExists)
        {
            // The host has it (another user, a job), so the listing on screen is stale: listed again, then the
            // reason, which the listing's own line must not hide.
            await ListCoreAsync(parent.UnixPath!, keepSelection: true, token);
            _ops.StatusText = "✗ " + HostFileMessages.Describe(ex);
            return;
        }
        var what = $"Created {path}";
        retryWith(() => ShowCreatedAsync(parent, path, what));
        await ShowCreatedAsync(parent, path, what, token);
    }

    private Task ShowCreatedAsync(HostPath parent, HostPath created, string what) =>
        _ops.RunExclusiveAsync(token => ShowCreatedAsync(parent, created, what, token), () => ShowCreatedAsync(parent, created, what));

    /// <summary>The second half of a create, and its own retry: the listing, with the new directory selected. A
    /// listing the host refuses leaves its reason after the fact of the create.</summary>
    private async Task ShowCreatedAsync(HostPath parent, HostPath created, string what, CancellationToken token)
    {
        if (!await ListCoreAsync(parent.UnixPath!, keepSelection: false, token))
        {
            _ops.StatusText = $"⚠ {what}. The list was not refreshed: {_ops.StatusText.TrimStart('✗').Trim()}";
            return;
        }
        SelectedDirectory = Directories.FirstOrDefault(d => d.Name == created.Name);
        _ops.StatusText = $"✓ {what}.";
    }

    // ---- delete a directory ----

    [RelayCommand(CanExecute = nameof(CanDeleteDirectory))]
    private Task DeleteDirectoryAsync() => SelectedDirectory is { Path: not null } row ? DeleteDirectoryAsync(row) : Task.CompletedTask;

    private Task DeleteDirectoryAsync(DirectoryRow row) =>
        _ops.RunExclusiveAsync(token => DeleteDirectoryCoreAsync(row, token), () => DeleteDirectoryAsync(row));

    private async Task DeleteDirectoryCoreAsync(DirectoryRow row, CancellationToken token)
    {
        var path = row.Path!;
        var answer = await _ops.AskAsync(new ConfirmationRequest(
            $"Delete directory {row.Name} and everything in it? This cannot be undone.", $"Delete {row.Name}"));
        if (answer.Choice != ConfirmChoice.Primary)
        {
            _ops.StatusText = "– Delete cancelled.";
            return;
        }
        _ops.StatusText = $"⟳ Deleting {path}…";
        try
        {
            await _connection.RunAsync(service => service.DeleteAsync(path, token));
        }
        catch (HostFileException ex) when (ex.Kind == HostFileErrorKind.NotFound)
        {
            // Gone already (another user, a job): the row is stale either way.
            _access.Etags.ForgetUnder(path);
            DropDirectory(row);
            _ops.StatusText = $"✗ {path}: {HostFileMessages.Describe(ex)}";
            return;
        }
        _access.Etags.ForgetUnder(path);
        DropDirectory(row);
        _ops.StatusText = $"✓ Deleted {path}.";
    }

    /// <summary>Takes a row off the list without asking the host, so nothing on screen names what the host no
    /// longer has.</summary>
    private void DropDirectory(DirectoryRow row)
    {
        if (ReferenceEquals(SelectedDirectory, row)) SelectedDirectory = null;
        Directories.Remove(row);
        OnPropertyChanged(nameof(DirectoriesFooter));
    }

    // ---- delete files ----

    [RelayCommand(CanExecute = nameof(CanDeleteFiles))]
    private Task DeleteFilesAsync() => Current is { } current ? DeleteFilesAsync(current, [.. _selectedFiles]) : Task.CompletedTask;

    /// <summary>A retry deletes the files the connection failure left, not the selection: the failure clears it.
    /// Once every file has gone, only the listing is left to retry.</summary>
    private Task DeleteFilesAsync(HostPath directory, IReadOnlyList<FileRow> files)
    {
        var remaining = files;
        return _ops.RunExclusiveAsync(
            token => DeleteFilesCoreAsync(directory, files, left => remaining = left, token),
            () => DeleteFilesAsync(directory, remaining));
    }

    private async Task DeleteFilesCoreAsync(HostPath directory, IReadOnlyList<FileRow> files,
        Action<IReadOnlyList<FileRow>> stoppedAt, CancellationToken token)
    {
        if (files.Count == 0)
        {
            await ListCoreAsync(directory.UnixPath!, keepSelection: false, token);
            return;
        }
        var names = string.Join(", ", files.Take(NamesInQuestion).Select(file => file.Name));
        if (files.Count > NamesInQuestion) names += $" and {files.Count - NamesInQuestion} more";
        var label = $"Delete {Counted(files.Count, "file", "files")}";
        var answer = await _ops.AskAsync(new ConfirmationRequest($"Delete {names} from {directory}? This cannot be undone.", label));
        if (answer.Choice != ConfirmChoice.Primary)
        {
            _ops.StatusText = "– Delete cancelled.";
            return;
        }

        var deleted = 0;
        var failures = new List<string>();
        try
        {
            for (var i = 0; i < files.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                var file = files[i];
                var path = file.Path!;
                try
                {
                    await _connection.RunAsync(service => service.DeleteAsync(path, token));
                    _access.Etags.Forget(path);
                    deleted++;
                    DropFile(file);
                }
                catch (HostFileException ex) when (BrowserOperations.IsConnectionFailure(ex))
                {
                    // The rest would fail the same way: let the banner offer Retry for this file and the ones after it.
                    stoppedAt([.. files.Skip(i)]);
                    throw;
                }
                catch (HostFileException ex) when (ex.Kind == HostFileErrorKind.NotFound)
                {
                    // Gone already: the row is stale either way.
                    _access.Etags.Forget(path);
                    DropFile(file);
                    failures.Add($"{file.Name}: {HostFileMessages.Describe(ex)}");
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    failures.Add($"{file.Name}: {HostFileMessages.Describe(ex)}");
                }
            }
            stoppedAt([]);
            await ListCoreAsync(directory.UnixPath!, keepSelection: false, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // A delete cannot be undone, so the list and the line show what went before the cancel. The list is the
            // truth: a request the cancel interrupted may still have deleted its file on the host.
            await RefreshAfterDeleteAsync(directory);
            _ops.StatusText = $"– Delete cancelled: deleted {deleted} of {Counted(files.Count, "file", "files")}.";
            return;
        }
        _ops.StatusText = failures.Count == 0
            ? $"✓ Deleted {deleted} of {Counted(files.Count, "file", "files")}."
            : $"⚠ Deleted {deleted} of {Counted(files.Count, "file", "files")}. ✗ {string.Join("; ", failures)}";
    }

    private void DropFile(FileRow row)
    {
        Files.Remove(row);
        SetSelectedFiles(_selectedFiles.Where(file => !ReferenceEquals(file, row)));
    }

    /// <summary>A refresh on the way out of a stopped delete: its own failure would only hide the reason for the
    /// stop, so it is ignored.</summary>
    private async Task RefreshAfterDeleteAsync(HostPath directory)
    {
        try
        {
            await ListCoreAsync(directory.UnixPath!, keepSelection: false, CancellationToken.None);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
        }
    }
}
```

In `src/LizTerm.App/ViewModels/UssBrowserViewModel.cs`'s `NotifyCommands`, add:

```csharp
        NewDirectoryCommand.NotifyCanExecuteChanged();
        DeleteDirectoryCommand.NotifyCanExecuteChanged();
        DeleteFilesCommand.NotifyCanExecuteChanged();
```

- [ ] **Step 5: Run the tests**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~UssBrowserManageTests|FullyQualifiedName~MvsmfBrowserManageTests|FullyQualifiedName~MvsmfBrowserCreateTests" 2>&1 | grep -E "error CS|Passed!|Failed!|\[FAIL\]" | head -8; dotnet build LizTerm.slnx --no-incremental 2>&1 | grep -c " warning "`
Expected: `Passed!` (the dataset manage and create tests prove the forwarded `RunThenListAsync` still works), then `0`.

- [ ] **Step 6: Commit**

```bash
git add src/LizTerm.App/ViewModels/UssBrowserViewModel.Manage.cs src/LizTerm.App/ViewModels/UssBrowserViewModel.cs src/LizTerm.App/ViewModels/BrowserOperations.cs src/LizTerm.App/ViewModels/MvsmfBrowserViewModel.Manage.cs tests/LizTerm.App.Tests/ViewModels/UssBrowserManageTests.cs
git commit -m "USS tab view model: New… and Delete… (#176)

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 5: Download…, Upload…, View and the transfer drop-down on the USS tab

**Files:**
- Create: `src/LizTerm.App/ViewModels/UssBrowserViewModel.Transfers.cs`
- Modify: `src/LizTerm.App/ViewModels/BrowserOperations.cs` (`TryPickAsync`), `src/LizTerm.App/ViewModels/MvsmfBrowserViewModel.cs` (forward `TryPickAsync`), `src/LizTerm.App/ViewModels/UssBrowserViewModel.cs` (`NotifyCommands`), `src/LizTerm.App/HostFiles/HostFileMessages.cs`
- Create: `tests/LizTerm.App.Tests/ViewModels/UssBrowserTransferTests.cs`
- Modify: `tests/LizTerm.App.Tests/HostFiles/HostFileMessagesTests.cs`

**Interfaces:**
- Consumes: Task 2's `BrowserTransfers`, Task 3's view model and rows, Task 4's `Counted` and `ListCoreAsync`.
- Produces: on `BrowserOperations`, `public Task<T?> TryPickAsync<T>(Func<Task<T>> pick)`; on `HostFileMessages`, `public static string DescribeUnixUploadFailure(Exception ex)`; on `UssBrowserViewModel`, observable `HostTransferMode Mode`, `bool IsTextMode`, `bool IsBinaryMode`, `string TransferModeLabel`, `bool VerifyUploads`, `MvsmfViewerViewModel? Viewer`, `string ViewGestureText`, `string ViewHint`, commands `DownloadCommand`, `UploadCommand`, `ViewCommand`.

- [ ] **Step 1: Write the failing tests**

Append to `tests/LizTerm.App.Tests/HostFiles/HostFileMessagesTests.cs`, inside the class:

```csharp
    [Fact]
    public void A_unix_upload_failure_that_can_happen_mid_write_says_the_file_may_be_partly_written()
    {
        Assert.Equal("Server error. The file may be partly written.",
            HostFileMessages.DescribeUnixUploadFailure(new HostFileException(HostFileErrorKind.ServerError, "x")));
        Assert.Equal("Changed on the host since you downloaded it.",
            HostFileMessages.DescribeUnixUploadFailure(new HostFileException(HostFileErrorKind.Conflict, "x")));
        Assert.Equal("The file is 2 bytes; the host holds at most 1.",
            HostFileMessages.DescribeUnixUploadFailure(new InvalidOperationException("The file is 2 bytes; the host holds at most 1.")));
    }
```

Create `tests/LizTerm.App.Tests/ViewModels/UssBrowserTransferTests.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Text;
using LizTerm.App.ViewModels;
using LizTerm.Core.HostFiles;

namespace LizTerm.App.Tests.ViewModels;

public class UssBrowserTransferTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("lizterm-uss-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string Local(string name, byte[] bytes)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private string Local(string name, string text) => Local(name, Encoding.UTF8.GetBytes(text));

    private static async Task<UssTestHost> NotesAsync()
    {
        var t = UssTestHost.Create();
        await t.ListAsync("/u/ibmuser/notes");
        return t;
    }

    [Fact]
    public async Task One_file_downloads_to_the_chosen_name_keeping_trailing_blanks_and_remembers_the_stamp()
    {
        var t = await NotesAsync();
        t.Host.Text["/u/ibmuser/notes/README.txt"] = ["hello  ", "world"];
        t.Host.Etags["/u/ibmuser/notes/README.txt"] = "stamp-7";
        t.Select("README.txt");
        var target = Path.Combine(_dir, "README.txt");
        t.Picker.Result = target;

        await t.Vm.DownloadCommand.ExecuteAsync(null);

        Assert.Equal("save:README.txt", t.Picker.Calls.Single());
        Assert.Equal("hello  " + Environment.NewLine + "world" + Environment.NewLine, await File.ReadAllTextAsync(target));
        Assert.Equal($"✓ Downloaded /u/ibmuser/notes/README.txt to {target}.", t.Ops.StatusText);
        Assert.StartsWith("✓ Done · ", t.Vm.Files.Single(f => f.Name == "README.txt").Status);
        Assert.Equal("stamp-7", t.Access.Etags.TryGet(HostPath.ForUnix("/u/ibmuser/notes/README.txt")));
        Assert.Contains("readtext:/u/ibmuser/notes/README.txt", t.Host.CallsSnapshot());
    }

    [Fact]
    public async Task Binary_mode_downloads_the_bytes()
    {
        var t = await NotesAsync();
        t.Vm.IsBinaryMode = true;
        Assert.Equal("Transfer: Binary", t.Vm.TransferModeLabel);
        t.Select("data.bin");
        var target = Path.Combine(_dir, "data.bin");
        t.Picker.Result = target;

        await t.Vm.DownloadCommand.ExecuteAsync(null);

        Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(target));
        Assert.Contains("readbinary:/u/ibmuser/notes/data.bin", t.Host.CallsSnapshot());
    }

    [Fact]
    public async Task Several_files_download_into_a_folder_and_an_existing_one_asks()
    {
        var t = await NotesAsync();
        File.WriteAllText(Path.Combine(_dir, "todo.md"), "old");
        t.Select("README.txt", "todo.md");
        t.Picker.FolderResult = _dir;

        var downloading = t.Vm.DownloadCommand.ExecuteAsync(null);
        await t.AskedAsync(downloading);
        Assert.Equal($"todo.md already exists in {_dir}.", t.Ops.Confirmation!.Message);
        Assert.Equal("Replace", t.Ops.Confirmation.PrimaryLabel);
        Assert.Equal("Skip", t.Ops.Confirmation.SecondaryLabel);
        t.Ops.Confirmation.SecondaryCommand.Execute(null);
        await downloading;

        Assert.Equal("folder:Download 2 files from /u/ibmuser/notes", t.Picker.Calls.Single());
        Assert.Equal("– Skipped: the file exists", t.Vm.Files.Single(f => f.Name == "todo.md").Status);
        Assert.Equal("✓ Done · 12 bytes", t.Vm.Files.Single(f => f.Name == "README.txt").Status);
        Assert.Equal("old", File.ReadAllText(Path.Combine(_dir, "todo.md")));
        Assert.Equal($"⚠ Downloaded 1 of 2 files to {_dir}.", t.Ops.StatusText);
    }

    [Fact]
    public async Task Download_is_off_without_a_regular_file_selected_and_a_cancelled_picker_does_nothing()
    {
        var t = await NotesAsync();
        Assert.False(t.Vm.DownloadCommand.CanExecute(null));
        t.Select("todo.md");
        Assert.True(t.Vm.DownloadCommand.CanExecute(null));
        t.Picker.Result = null;
        var before = t.Ops.StatusText;
        await t.Vm.DownloadCommand.ExecuteAsync(null);
        Assert.Equal(before, t.Ops.StatusText);
        Assert.DoesNotContain(t.Host.CallsSnapshot(), c => c.StartsWith("readtext:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Upload_sends_new_files_under_their_own_names_and_names_the_refused_ones()
    {
        var t = await NotesAsync();
        t.Picker.Results = [Local("new.txt", "a\tb\nc  \n"), Local("bad.bin", [0xFF, 0xFE, 0x00])];

        await t.Vm.UploadCommand.ExecuteAsync(null);

        Assert.Equal("open-many:Upload to /u/ibmuser/notes", t.Picker.Calls.Single());
        Assert.Equal(new[] { "a\tb", "c  " }, t.Host.Text["/u/ibmuser/notes/new.txt"]);
        Assert.Contains("writetext:/u/ibmuser/notes/new.txt:2", t.Host.CallsSnapshot());
        Assert.DoesNotContain(t.Host.CallsSnapshot(), c => c.Contains("bad.bin"));
        Assert.Equal("✓ Uploaded and verified", t.Vm.Files.Single(f => f.Name == "new.txt").Status);
        Assert.Equal("⚠ Uploaded 1 of 2 files to /u/ibmuser/notes. ✗ Not sent: bad.bin: The file is not UTF-8 text. Choose Binary to send its bytes unchanged.", t.Ops.StatusText);
        Assert.NotNull(t.Access.Etags.TryGet(HostPath.ForUnix("/u/ibmuser/notes/new.txt")));
    }

    [Fact]
    public async Task Upload_over_an_existing_file_asks_and_skip_leaves_it()
    {
        var t = await NotesAsync();
        t.Picker.Results = [Local("README.txt", "replaced\n"), Local("todo.md", "replaced\n")];

        var uploading = t.Vm.UploadCommand.ExecuteAsync(null);
        await t.AskedAsync(uploading);
        Assert.Equal("README.txt already exists in /u/ibmuser/notes.", t.Ops.Confirmation!.Message);
        Assert.True(t.Ops.Confirmation.OffersApplyToAll);
        t.Ops.Confirmation.PrimaryCommand.Execute(null);
        await t.AskedAsync(uploading);
        Assert.Equal("todo.md already exists in /u/ibmuser/notes.", t.Ops.Confirmation!.Message);
        t.Ops.Confirmation.SecondaryCommand.Execute(null);
        await uploading;

        Assert.Equal(new[] { "replaced" }, t.Host.Text["/u/ibmuser/notes/README.txt"]);
        Assert.Equal(new[] { "- x" }, t.Host.Text["/u/ibmuser/notes/todo.md"]);
        Assert.Equal("– Skipped: the file exists", t.Vm.Files.Single(f => f.Name == "todo.md").Status);
        Assert.Equal("✓ Uploaded and verified", t.Vm.Files.Single(f => f.Name == "README.txt").Status);
        Assert.Equal("⚠ Uploaded 1 of 2 files to /u/ibmuser/notes.", t.Ops.StatusText);
    }

    [Fact]
    public async Task A_remembered_stamp_goes_out_and_a_conflict_asks()
    {
        var t = await NotesAsync();
        var path = HostPath.ForUnix("/u/ibmuser/notes/README.txt");
        t.Host.Etags["/u/ibmuser/notes/README.txt"] = "stamp-1";
        t.Access.Etags.Remember(path, "stale");
        t.Picker.Results = [Local("README.txt", "replaced\n")];

        var uploading = t.Vm.UploadCommand.ExecuteAsync(null);
        await t.AskedAsync(uploading);
        t.Ops.Confirmation!.PrimaryCommand.Execute(null);
        await t.AskedAsync(uploading);
        Assert.Equal("README.txt changed on the host since you downloaded it.", t.Ops.Confirmation!.Message);
        Assert.Equal("Replace anyway", t.Ops.Confirmation.PrimaryLabel);
        t.Ops.Confirmation.PrimaryCommand.Execute(null);
        await uploading;

        Assert.Equal(new string?[] { "stale", null }, t.Host.IfMatches);
        Assert.Equal(new[] { "replaced" }, t.Host.Text["/u/ibmuser/notes/README.txt"]);
        Assert.Equal("✓ Uploaded 1 of 1 file to /u/ibmuser/notes.", t.Ops.StatusText);
        Assert.Equal(t.Host.Etags["/u/ibmuser/notes/README.txt"], t.Access.Etags.TryGet(path));
    }

    [Fact]
    public async Task A_file_over_the_cap_is_refused_before_any_request()
    {
        var t = await NotesAsync();
        t.Vm.IsBinaryMode = true;
        t.Picker.Results = [Local("big.bin", new byte[HostFileLimits.MaxUnixFileBytes + 1])];

        await t.Vm.UploadCommand.ExecuteAsync(null);

        Assert.DoesNotContain(t.Host.CallsSnapshot(), c => c.StartsWith("writebinary:", StringComparison.Ordinal));
        Assert.Equal("⚠ Uploaded 0 of 1 file to /u/ibmuser/notes. ✗ Not sent: big.bin: The file is 1,048,577 bytes; the host holds at most 1,048,576.", t.Ops.StatusText);
    }

    [Fact]
    public async Task Verify_off_says_uploaded_and_a_differing_copy_is_a_warning()
    {
        var t = await NotesAsync();
        t.Vm.VerifyUploads = false;
        t.Picker.Results = [Local("one.txt", "x\n")];
        await t.Vm.UploadCommand.ExecuteAsync(null);
        Assert.Equal("✓ Uploaded", t.Vm.Files.Single(f => f.Name == "one.txt").Status);

        t.Vm.VerifyUploads = true;
        t.Host.StoreTransform = (_, lines) => [.. lines.Select(l => l + "!")];
        t.Picker.Results = [Local("two.txt", "y\n")];
        await t.Vm.UploadCommand.ExecuteAsync(null);
        Assert.Equal("⚠ Uploaded, but the host copy differs at line 1", t.Vm.Files.Single(f => f.Name == "two.txt").Status);
        Assert.Equal("⚠ Uploaded 1 of 1 file to /u/ibmuser/notes.", t.Ops.StatusText);
    }

    [Fact]
    public async Task An_upload_stopped_by_a_connection_failure_offers_retry_for_the_rest()
    {
        var t = await NotesAsync();
        t.Host.Failures["writetext:/u/ibmuser/notes/b.txt"] = new HostFileException(HostFileErrorKind.Unreachable, "b.txt: cannot reach the host.");
        t.Picker.Results = [Local("a.txt", "a\n"), Local("b.txt", "b\n"), Local("c.txt", "c\n")];

        await t.Vm.UploadCommand.ExecuteAsync(null);

        Assert.True(t.Ops.CanRetry);
        Assert.Equal("b.txt: cannot reach the host. The file may be partly written.", t.Ops.ErrorText);
        Assert.True(t.Host.Text.ContainsKey("/u/ibmuser/notes/a.txt"));
        t.Host.Failures.Clear();
        await t.Ops.RetryCommand.ExecuteAsync(null);
        Assert.True(t.Host.Text.ContainsKey("/u/ibmuser/notes/b.txt"));
        Assert.True(t.Host.Text.ContainsKey("/u/ibmuser/notes/c.txt"));
        Assert.Equal(1, t.Host.CallsSnapshot().Count(c => c == "writetext:/u/ibmuser/notes/a.txt:1"));
        Assert.Equal("✓ Uploaded 2 of 2 files to /u/ibmuser/notes.", t.Ops.StatusText);
    }

    [Fact]
    public async Task View_reads_one_file_as_text_without_a_stamp()
    {
        var t = await NotesAsync();
        Assert.False(t.Vm.ViewCommand.CanExecute(null));
        t.Select("README.txt", "todo.md");
        Assert.False(t.Vm.ViewCommand.CanExecute(null));
        t.Select("README.txt");
        Assert.True(t.Vm.ViewCommand.CanExecute(null));
        t.Vm.ViewGestureText = "⌘⏎";
        Assert.Equal("View the selected file (⌘⏎)", t.Vm.ViewHint);

        await t.Vm.ViewCommand.ExecuteAsync(null);

        Assert.Equal("/u/ibmuser/notes/README.txt", t.Vm.Viewer!.Path);
        Assert.Equal("hello\nworld", t.Vm.Viewer.Text);
        Assert.False(t.Vm.Viewer.TrimmedTrailingBlanks);
        Assert.Equal("✓ Read /u/ibmuser/notes/README.txt · 2 lines.", t.Ops.StatusText);
        Assert.Empty(t.Host.EtagRequests);
        Assert.Equal("", t.Vm.Files.Single(f => f.Name == "README.txt").Status);
    }
}
```

If `MvsmfViewerViewModel.Text` joins lines differently from `"hello\nworld"`, read `src/LizTerm.App/ViewModels/MvsmfViewerViewModel.cs` and assert the join it uses; do not change the viewer.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~UssBrowserTransferTests|FullyQualifiedName~HostFileMessagesTests" 2>&1 | grep -E "error CS|Passed!|Failed!" | head -5`
Expected: build errors naming `DownloadCommand`, `UploadCommand`, `ViewCommand`, `DescribeUnixUploadFailure`.

- [ ] **Step 3: The picker guard moves to the runner; the message**

In `src/LizTerm.App/ViewModels/BrowserOperations.cs`, after `WarnWhenIdle`, add:

```csharp
    /// <summary>A file dialog that fails to open is a status line, not a failed operation.</summary>
    public async Task<T?> TryPickAsync<T>(Func<Task<T>> pick)
    {
        try
        {
            return await pick();
        }
        catch (Exception ex)
        {
            StatusText = "✗ Could not open the file dialog: " + ex.Message;
            return default;
        }
    }
```

In `src/LizTerm.App/ViewModels/MvsmfBrowserViewModel.cs`, replace the `TryPickAsync` method with `private Task<T?> TryPickAsync<T>(Func<Task<T>> pick) => Ops.TryPickAsync(pick);` (keep its one-line summary).

In `src/LizTerm.App/HostFiles/HostFileMessages.cs`, after the two `DescribeUploadFailure` overloads, add:

```csharp
    /// <summary>As <see cref="DescribeUploadFailure(Exception)"/>, for a UNIX file.</summary>
    public static string DescribeUnixUploadFailure(Exception ex) =>
        ex is HostFileException { Kind: HostFileErrorKind.ServerError or HostFileErrorKind.Unreachable }
            ? Describe(ex) + " The file may be partly written."
            : Describe(ex);
```

- [ ] **Step 4: The Transfers partial**

Create `src/LizTerm.App/ViewModels/UssBrowserViewModel.Transfers.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LizTerm.App.HostFiles;
using LizTerm.Core.HostFiles;

namespace LizTerm.App.ViewModels;

/// <summary>Download…, Upload…, View and the transfer drop-down (USS spec §4.6). A download keeps trailing blanks
/// and an upload keeps tabs: a UNIX file is a byte stream, not records. Each file goes under its own name, case
/// kept; a name already listed asks before it is replaced, a remembered stamp goes out with the write, and a file
/// past the host's cap is refused here, before any request. View reads one file as text, always.</summary>
public sealed partial class UssBrowserViewModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTextMode), nameof(IsBinaryMode), nameof(TransferModeLabel))]
    private HostTransferMode _mode = HostTransferMode.Text;

    [ObservableProperty] private bool _verifyUploads = true;

    /// <summary>The last View's state, null until one has been read; the window opens or reuses its viewer on it.</summary>
    [ObservableProperty] private MvsmfViewerViewModel? _viewer;

    /// <summary>How this platform writes the View gesture; the window sets it once.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ViewHint))]
    private string _viewGestureText = "";

    private bool _uploadStoppedMidWrite;

    public bool IsTextMode
    {
        get => Mode == HostTransferMode.Text;
        set { if (value) Mode = HostTransferMode.Text; }
    }

    public bool IsBinaryMode
    {
        get => Mode == HostTransferMode.Binary;
        set { if (value) Mode = HostTransferMode.Binary; }
    }

    public string TransferModeLabel => IsBinaryMode ? "Transfer: Binary" : "Transfer: Text";

    public string ViewHint => ViewGestureText.Length > 0 ? $"View the selected file ({ViewGestureText})" : "View the selected file";

    private bool AllRegularFiles => _selectedFiles.Count > 0 && _selectedFiles.All(file => file.IsFile);
    private bool CanDownload => !IsBusy && AllRegularFiles;
    private bool CanUpload => !IsBusy && Current is not null;
    private bool CanView => !IsBusy && _selectedFiles is [{ IsFile: true }];

    // ---- download ----

    [RelayCommand(CanExecute = nameof(CanDownload))]
    private Task DownloadAsync() => _ops.RunExclusiveAsync(DownloadCoreAsync, () => DownloadAsync());

    private async Task DownloadCoreAsync(CancellationToken token)
    {
        var files = _selectedFiles.ToList();
        var options = new DownloadOptions(Mode, TrimTrailingBlanks: false);
        if (files is [var only])
        {
            var path = only.Path!;
            var file = await _ops.TryPickAsync(() => _picker.PickSaveLocationAsync(only.Name, $"Download {path}"));
            if (file is null) return;
            if (await BrowserTransfers.DownloadOneAsync(_connection, _access.Etags, _dispatch, path, file, options, text => only.Status = text, token, token))
                _ops.StatusText = $"✓ Downloaded {path} to {file}.";
            else _ops.StatusText = only.Status;
            return;
        }

        var folder = await _ops.TryPickAsync(() => _picker.PickFolderAsync($"Download {files.Count} files from {Current}"));
        if (folder is null) return;
        foreach (var row in files) row.Status = "";

        var plan = new List<DownloadItem>();
        bool? replaceAll = null;
        foreach (var row in files)
        {
            var file = System.IO.Path.Combine(folder, row.Name);
            if (File.Exists(file))
            {
                var replace = replaceAll;
                if (replace is null)
                {
                    var answer = await _ops.AskAsync(new ConfirmationRequest($"{row.Name} already exists in {folder}.", "Replace", "Skip", offersApplyToAll: true));
                    if (answer.Choice == ConfirmChoice.Cancel)
                    {
                        foreach (var each in files) each.Status = "";
                        _ops.StatusText = "– Download cancelled.";
                        return;
                    }
                    replace = answer.Choice == ConfirmChoice.Primary;
                    if (answer.ApplyToAll) replaceAll = replace;
                }
                if (replace == false)
                {
                    row.Status = "– Skipped: the file exists";
                    continue;
                }
            }
            row.Status = "⟳ Waiting";
            var target = row;
            plan.Add(new DownloadItem(row.Path!, file, text => target.Status = text));
        }

        var done = await BrowserTransfers.DownloadManyAsync(_connection, _access.Etags, _dispatch, plan, options, token);
        if (token.IsCancellationRequested)
        {
            _ops.StatusText = "– Download cancelled.";
            return;
        }
        _ops.StatusText = $"{(done == files.Count ? "✓" : "⚠")} Downloaded {done} of {Counted(files.Count, "file", "files")} to {folder}.";
    }

    // ---- upload ----

    [RelayCommand(CanExecute = nameof(CanUpload))]
    private async Task UploadAsync()
    {
        if (Current is not { } directory) return;
        var files = await _ops.TryPickAsync(() => _picker.PickFilesToSendAsync($"Upload to {directory}"));
        if (files is not { Count: > 0 }) return;
        await UploadFilesAsync(directory, files);
    }

    /// <summary>A retry sends the files a connection failure left, never the ones already sent.</summary>
    private Task UploadFilesAsync(HostPath directory, IReadOnlyList<string> files)
    {
        var remaining = files;
        return _ops.RunExclusiveAsync(
            token => UploadCoreAsync(directory, files, left => remaining = left, token),
            () => UploadFilesAsync(directory, remaining),
            ex => _uploadStoppedMidWrite ? HostFileMessages.DescribeUnixUploadFailure(ex) : HostFileMessages.Describe(ex));
    }

    /// <summary>One local file on its way up: its name on the host and its check when Text.</summary>
    private sealed record Pending(string LocalPath, string Name, HostPath Path, TextUploadResult? Check);

    private async Task UploadCoreAsync(HostPath directory, IReadOnlyList<string> files, Action<IReadOnlyList<string>> stoppedAt, CancellationToken token)
    {
        _uploadStoppedMidWrite = false;
        var mode = Mode;
        var verify = VerifyUploads;

        // Every file is checked before anything is sent or asked, so a file that cannot go is never asked about.
        var refused = new List<string>();
        var plan = new List<Pending>();
        foreach (var local in files)
        {
            var name = System.IO.Path.GetFileName(local);
            HostPath path;
            try
            {
                path = directory.Child(name);
            }
            catch (ArgumentException ex)
            {
                refused.Add($"{name}: {ex.Message}");
                continue;
            }
            TextUploadResult? check = null;
            try
            {
                if (mode == HostTransferMode.Text)
                {
                    check = HostFileTransfer.CheckUnixTextFile(local, options: new TextUploadOptions(ExpandTabs: false));
                    if (!check.CanUpload)
                    {
                        refused.Add($"{name}: {check.Errors[0].Message}");
                        continue;
                    }
                }
                else if (HostFileTransfer.BinaryUploadProblem(local) is { } problem)
                {
                    refused.Add($"{name}: {problem}");
                    continue;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                refused.Add($"{name}: Local file: {ex.Message}");
                continue;
            }
            plan.Add(new Pending(local, name, path, check));
        }

        // The rows on screen may be stale, so the directory is listed again before a name is called existing.
        if (!await ListCoreAsync(directory.UnixPath!, keepSelection: true, token)) return;
        var results = new Dictionary<string, string>(StringComparer.Ordinal);
        void Show(Pending item, string text)
        {
            results[item.Name] = text;
            if (Files.FirstOrDefault(file => file.Name == item.Name) is { } row) row.Status = text;
        }

        var sending = new List<Pending>();
        bool? replaceAll = null;
        foreach (var item in plan)
        {
            if (Files.Any(file => file.Name == item.Name))
            {
                var replace = replaceAll;
                if (replace is null)
                {
                    var answer = await _ops.AskAsync(new ConfirmationRequest($"{item.Name} already exists in {directory}.", "Replace", "Skip", offersApplyToAll: true));
                    if (answer.Choice == ConfirmChoice.Cancel)
                    {
                        _ops.StatusText = "– Upload cancelled.";
                        return;
                    }
                    replace = answer.Choice == ConfirmChoice.Primary;
                    if (answer.ApplyToAll) replaceAll = replace;
                }
                if (replace == false)
                {
                    Show(item, "– Skipped: the file exists");
                    continue;
                }
            }
            sending.Add(item);
        }

        var sent = 0;
        var differs = false;
        var cancelled = false;
        for (var i = 0; i < sending.Count; i++)
        {
            var item = sending[i];
            if (token.IsCancellationRequested || cancelled)
            {
                Show(item, "– Cancelled");
                continue;
            }
            Show(item, "⟳ Sending");
            var started = false;
            // Only a file being replaced is checked against its stamp; one this window never downloaded has none.
            var ifMatch = Files.Any(file => file.Name == item.Name) ? _access.Etags.TryGet(item.Path) : null;

            async Task SendAsync(string? stamp)
            {
                started = false;
                if (item.Check is { } check)
                {
                    var outcome = await _connection.RunAsync(service =>
                    {
                        started = true;
                        return HostFileTransfer.UploadTextAsync(service, item.Path, check, verify, stamp, token,
                            written: etag => _access.Etags.Remember(item.Path, etag));
                    });
                    Show(item, outcome.Verification switch
                    {
                        UploadVerification.Matches => "✓ Uploaded and verified",
                        UploadVerification.NotChecked => "✓ Uploaded",
                        _ => $"⚠ Uploaded, but the host copy differs at line {outcome.DiffersAtLine}",
                    });
                    differs |= outcome.Verification == UploadVerification.Differs;
                }
                else
                {
                    var etag = await _connection.RunAsync(service =>
                    {
                        started = true;
                        return HostFileTransfer.UploadBinaryAsync(service, item.Path, item.LocalPath, stamp, token);
                    });
                    _access.Etags.Remember(item.Path, etag);
                    Show(item, "✓ Uploaded");
                }
            }

            try
            {
                try
                {
                    await SendAsync(ifMatch);
                }
                catch (HostFileException ex) when (ex.Kind == HostFileErrorKind.Conflict)
                {
                    // The host checks the stamp before it writes, so nothing was written.
                    var answer = await _ops.AskAsync(new ConfirmationRequest($"{item.Name} changed on the host since you downloaded it.", "Replace anyway", "Skip"));
                    switch (answer.Choice)
                    {
                        case ConfirmChoice.Primary:
                            await SendAsync(null);
                            break;
                        case ConfirmChoice.Secondary:
                            Show(item, "– Skipped: changed on the host");
                            continue;
                        default:
                            cancelled = true;
                            Show(item, "– Cancelled");
                            continue;
                    }
                }
                sent++;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                Show(item, started ? "– Cancelled: the file may be partly written" : "– Cancelled");
            }
            catch (HostFileException ex) when (BrowserOperations.IsConnectionFailure(ex))
            {
                foreach (var each in sending.Skip(i + 1)) Show(each, "– Stopped");
                Show(item, "– Stopped: the file may be partly written");
                _uploadStoppedMidWrite = true;
                stoppedAt([.. sending.Skip(i).Select(each => each.LocalPath)]);
                throw;
            }
            catch (Exception ex)
            {
                Show(item, "✗ Failed: " + HostFileMessages.DescribeUnixUploadFailure(ex));
            }
        }
        stoppedAt([]);

        if (token.IsCancellationRequested || cancelled)
        {
            await RefreshQuietlyAsync(directory);
            ApplyResults(results);
            _ops.StatusText = "– Upload cancelled.";
            return;
        }
        // The listing gives the new files their rows; each row then shows what happened to it.
        await ListCoreAsync(directory.UnixPath!, keepSelection: true, token);
        ApplyResults(results);
        var clean = refused.Count == 0 && sent == plan.Count && !differs;
        var summary = $"{(clean ? "✓" : "⚠")} Uploaded {sent} of {Counted(files.Count, "file", "files")} to {directory}.";
        if (refused.Count > 0)
        {
            summary += " ✗ Not sent: " + string.Join("; ", refused.Take(NamesInQuestion));
            if (refused.Count > NamesInQuestion) summary += $" and {refused.Count - NamesInQuestion} more";
        }
        _ops.StatusText = summary;
    }

    private void ApplyResults(Dictionary<string, string> results)
    {
        foreach (var row in Files)
        {
            if (results.TryGetValue(row.Name, out var text)) row.Status = text;
        }
    }

    // ---- view ----

    [RelayCommand(CanExecute = nameof(CanView))]
    private Task ViewAsync() => _ops.RunExclusiveAsync(ViewCoreAsync, () => ViewAsync());

    private async Task ViewCoreAsync(CancellationToken token)
    {
        if (_selectedFiles is not [{ IsFile: true, Path: { } path }]) return;
        var progress = new BrowserTransfers.RowProgress(_dispatch, bytes => _ops.StatusText = $"⟳ Reading {path} · {BrowserTransfers.Bytes(bytes)} bytes");
        _ops.StatusText = $"⟳ Reading {path}…";
        HostTextRead read;
        try
        {
            // withEtag: false — a view can never write the content back, and a stamp costs the host a second pass.
            read = await _connection.RunAsync(service => service.ReadTextAsync(path, progress, withEtag: false, token));
        }
        finally
        {
            progress.Close();
        }
        // The result is written before the viewer opens: a window that cannot be shown puts its own message here.
        _ops.StatusText = $"✓ Read {path} · {Counted(read.Lines.Count, "line", "lines")}.";
        Viewer = new MvsmfViewerViewModel(path.ToString(), read.Lines, trimTrailingBlanks: false);
    }
}
```

In `src/LizTerm.App/ViewModels/UssBrowserViewModel.cs`'s `NotifyCommands`, add:

```csharp
        DownloadCommand.NotifyCanExecuteChanged();
        UploadCommand.NotifyCanExecuteChanged();
        ViewCommand.NotifyCanExecuteChanged();
```

`RefreshAfterDeleteAsync` (Task 4) serves the cancelled upload too: rename it `RefreshQuietlyAsync` in the Manage partial and at its two call sites, and reword its summary to "A refresh on the way out of a stopped delete or upload".

- [ ] **Step 5: Run the tests**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~UssBrowser|FullyQualifiedName~HostFileMessagesTests|FullyQualifiedName~MvsmfBrowserUploadTests|FullyQualifiedName~MvsmfBrowserDownloadTests" 2>&1 | grep -E "error CS|Passed!|Failed!|\[FAIL\]" | head -8; dotnet build LizTerm.slnx --no-incremental 2>&1 | grep -c " warning "`
Expected: `Passed!`, then `0`.

- [ ] **Step 6: Commit**

```bash
git add src/LizTerm.App/ViewModels tests/LizTerm.App.Tests/ViewModels/UssBrowserTransferTests.cs src/LizTerm.App/HostFiles/HostFileMessages.cs tests/LizTerm.App.Tests/HostFiles/HostFileMessagesTests.cs
git commit -m "USS tab view model: Download, Upload, View and the transfer drop-down (#176)

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 6: The window — the `TabControl`, the path row, the two USS panes, keys and menus

**Files:**
- Modify: `src/LizTerm.App/Views/MvsmfBrowserWindow.axaml`, `src/LizTerm.App/Views/MvsmfBrowserWindow.axaml.cs`, `src/LizTerm.App/ViewModels/MvsmfBrowserViewModel.cs` (`Uss`)
- Create: `tests/LizTerm.App.Tests/Views/MvsmfBrowserWindowUssTests.cs`

**Interfaces:**
- Consumes: Tasks 3 to 5's `UssBrowserViewModel` (its commands, `SelectFilesRequested`, `Viewer`, `ViewGestureText`, `EnsureListedAsync`, `SetSelectedFiles`), `MvsmfBrowserConverters.DimUnlessTrue`, `BrowserPane`.
- Produces: `MvsmfBrowserViewModel.Uss` (`UssBrowserViewModel`, built in the constructor on the same runner, access, connection, picker and dispatch); named controls `Tabs`, `DatasetsTab`, `UssTab`, `PathBox`, `GoButton`, `UpButton`, `DirectoriesPane`, `FilesPane`, `NewDirectoryButton`, `DeleteDirectoryButton`, `UssRefreshButton`, `DirectoryList`, `DirectoryMenu` (+ `DirectoryMenuNew`, `DirectoryMenuDelete`, `DirectoryMenuRefresh`), `UssViewButton`, `UssDownloadButton`, `UssUploadButton`, `DeleteFilesButton`, `UssTransferButton`, `UssTextModeItem`, `UssBinaryModeItem`, `UssVerifyItem`, `FileList`, `FileMenu` (+ `FileMenuView`, `FileMenuDownload`, `FileMenuUpload`, `FileMenuDelete`); `internal bool IsUssTab` on the window.

- [ ] **Step 1: Write the failing tests**

Create `tests/LizTerm.App.Tests/Views/MvsmfBrowserWindowUssTests.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using LizTerm.App.Controls;
using LizTerm.App.Tests.Fakes;
using LizTerm.App.Tests.ViewModels;
using LizTerm.App.ViewModels;
using LizTerm.App.Views;

namespace LizTerm.App.Tests.Views;

public class MvsmfBrowserWindowUssTests
{
    /// <summary>The window over a host seeded with the standard datasets and the standard UNIX tree, the USS tab in
    /// front and its start path (/u/mvsce02, the profile's userid) listed.</summary>
    private static async Task<(MvsmfBrowserWindow Window, BrowserTestHost T)> UssAsync()
    {
        var t = BrowserTestHost.Create(seed: host =>
        {
            BrowserTestHost.Standard(host);
            UssTestHost.Standard(host);
        });
        var window = new MvsmfBrowserWindow { DataContext = t.Vm };
        window.Show();
        window.Activate();
        await Wait.UntilAsync(() => t.Vm.Datasets.Count == 4, "the first dataset listing");
        Named<TabControl>(window, "Tabs").SelectedIndex = 1;
        window.UpdateLayout();
        await Wait.UntilAsync(() => t.Vm.Uss.Current is not null && !t.Vm.IsBusy, "the USS start listing");
        return (window, t);
    }

    private static async Task ListAsync(BrowserTestHost t, string path)
    {
        t.Vm.Uss.Path = path;
        await t.Vm.Uss.GoCommand.ExecuteAsync(null);
    }

    private static T Named<T>(Window window, string name) where T : Control => window.FindControl<T>(name)!;

    private static RawInputModifiers Command(MvsmfBrowserWindow window) =>
        window.RefreshGesture.KeyModifiers == KeyModifiers.Meta ? RawInputModifiers.Meta : RawInputModifiers.Control;

    [AvaloniaFact]
    public async Task The_uss_tab_lists_the_start_path_when_first_shown_and_only_then()
    {
        var t = BrowserTestHost.Create(seed: host =>
        {
            BrowserTestHost.Standard(host);
            UssTestHost.Standard(host);
        });
        var window = new MvsmfBrowserWindow { DataContext = t.Vm };
        window.Show();
        await Wait.UntilAsync(() => t.Vm.Datasets.Count == 4, "the first dataset listing");
        Assert.DoesNotContain(t.Host.CallsSnapshot(), c => c.StartsWith("listdir:", StringComparison.Ordinal));
        Assert.False(window.IsUssTab);

        Named<TabControl>(window, "Tabs").SelectedIndex = 1;
        window.UpdateLayout();
        await Wait.UntilAsync(() => t.Vm.Uss.Current is not null, "the USS start listing");

        Assert.True(window.IsUssTab);
        Assert.Equal("/u/mvsce02", Named<TextBox>(window, "PathBox").Text);
        Assert.Equal("/u/mvsce02", Named<BrowserPane>(window, "FilesPane").Title);
        Assert.Equal("Directories", Named<BrowserPane>(window, "DirectoriesPane").Title);
        Assert.Equal("No directories", Named<BrowserPane>(window, "DirectoriesPane").FooterText);
        Assert.Equal("✓ Listed /u/mvsce02 · 0 directories, 0 files.", Named<TextBlock>(window, "StatusLine").Text);
    }

    [AvaloniaFact]
    public async Task Enter_in_the_path_box_lists_and_the_lists_fill()
    {
        var (window, t) = await UssAsync();
        var box = Named<TextBox>(window, "PathBox");
        box.Text = "/u/ibmuser/notes";
        box.Focus();

        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        await Wait.UntilAsync(() => t.Vm.Uss.Current?.UnixPath == "/u/ibmuser/notes" && !t.Vm.IsBusy, "the listing");
        window.UpdateLayout();

        Assert.Equal(1, Named<ListBox>(window, "DirectoryList").ItemCount);
        Assert.Equal(3, Named<ListBox>(window, "FileList").ItemCount);
        Assert.Equal("/u/ibmuser/notes", Named<BrowserPane>(window, "FilesPane").Title);
        Assert.Equal("3 files · none selected", Named<BrowserPane>(window, "FilesPane").FooterText);
    }

    [AvaloniaFact]
    public async Task Enter_on_a_directory_row_descends_and_up_climbs()
    {
        var (window, t) = await UssAsync();
        await ListAsync(t, "/u/ibmuser");
        window.UpdateLayout();
        var list = Named<ListBox>(window, "DirectoryList");
        list.SelectedIndex = 0;
        list.ContainerFromIndex(0)!.Focus();

        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        await Wait.UntilAsync(() => t.Vm.Uss.Current?.UnixPath == "/u/ibmuser/notes" && !t.Vm.IsBusy, "the descent");

        Named<Button>(window, "UpButton").Command!.Execute(null);
        await Wait.UntilAsync(() => t.Vm.Uss.Current?.UnixPath == "/u/ibmuser" && !t.Vm.IsBusy, "the climb");
        Assert.Equal("/u/ibmuser", Named<TextBox>(window, "PathBox").Text);
    }

    [AvaloniaFact]
    public async Task The_command_r_gesture_refreshes_the_uss_listing_not_the_datasets()
    {
        var (window, t) = await UssAsync();
        var lists = t.Host.CallsSnapshot().Count(c => c.StartsWith("list:", StringComparison.Ordinal));
        var listdirs = t.Host.CallsSnapshot().Count(c => c.StartsWith("listdir:", StringComparison.Ordinal));

        window.KeyPress(Key.R, Command(window), PhysicalKey.R, null);
        await Wait.UntilAsync(() => t.Host.CallsSnapshot().Count(c => c.StartsWith("listdir:", StringComparison.Ordinal)) == listdirs + 1 && !t.Vm.IsBusy, "the refresh");

        Assert.Equal(lists, t.Host.CallsSnapshot().Count(c => c.StartsWith("list:", StringComparison.Ordinal)));
    }

    [AvaloniaFact]
    public async Task Delete_on_the_file_list_asks_in_the_strip_and_escape_cancels()
    {
        var (window, t) = await UssAsync();
        await ListAsync(t, "/u/ibmuser/notes");
        window.UpdateLayout();
        var list = Named<ListBox>(window, "FileList");
        list.SelectedIndex = 0;
        list.ContainerFromIndex(0)!.Focus();
        await Wait.UntilAsync(() => t.Vm.Uss.SelectedFiles.Count == 1, "the pushed selection");

        window.KeyPress(Key.Delete, RawInputModifiers.None, PhysicalKey.Delete, null);
        await Wait.UntilAsync(() => t.Vm.HasConfirmation, "the question");

        Assert.True(Named<Border>(window, "ConfirmationStrip").IsVisible);
        Assert.Equal("Delete README.txt from /u/ibmuser/notes? This cannot be undone.", t.Vm.Confirmation!.Message);
        Assert.Equal("Delete 1 file", Named<Button>(window, "ConfirmPrimaryButton").Content);
        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        await Wait.UntilAsync(() => !t.Vm.IsBusy, "the cancel");
        Assert.Equal("– Delete cancelled.", t.Vm.StatusText);
        Assert.Equal(3, t.Vm.Uss.Files.Count);
    }

    [AvaloniaFact]
    public async Task A_uss_result_reaches_the_status_line_while_the_datasets_tab_is_in_front()
    {
        var (window, t) = await UssAsync();
        Named<TabControl>(window, "Tabs").SelectedIndex = 0;
        window.UpdateLayout();
        Assert.False(window.IsUssTab);

        await ListAsync(t, "/u/ibmuser");

        Assert.Equal("✓ Listed /u/ibmuser · 2 directories, 0 files.", Named<TextBlock>(window, "StatusLine").Text);
        Assert.True(Named<Button>(window, "ListButton").IsEffectivelyEnabled);
    }

    [AvaloniaFact]
    public async Task The_view_gesture_on_a_file_opens_the_shared_viewer()
    {
        var (window, t) = await UssAsync();
        await ListAsync(t, "/u/ibmuser/notes");
        window.UpdateLayout();
        var list = Named<ListBox>(window, "FileList");
        list.SelectedIndex = 0;
        list.ContainerFromIndex(0)!.Focus();
        await Wait.UntilAsync(() => t.Vm.Uss.SelectedFiles.Count == 1, "the pushed selection");
        var button = Named<Button>(window, "UssViewButton");
        Assert.True(ToolTip.GetShowOnDisabled(button));
        Assert.Contains(window.ViewGesture.ToString("p", null), (string)ToolTip.GetTip(button)!);

        window.KeyPress(Key.Enter, Command(window), PhysicalKey.Enter, null);

        await Wait.UntilAsync(() => window.ViewerWindow is { IsVisible: true }, "the viewer window");
        Assert.Equal("/u/ibmuser/notes/README.txt — mvsMF Access", window.ViewerWindow!.Title);
    }

    [AvaloniaFact]
    public async Task The_transfer_drop_down_holds_text_binary_and_verify_only()
    {
        var (window, t) = await UssAsync();
        Assert.True(Named<MenuItem>(window, "UssTextModeItem").IsChecked);
        Assert.True(Named<MenuItem>(window, "UssVerifyItem").IsChecked);
        Assert.Null(window.FindControl<MenuItem>("UssTrimItem"));
        Assert.Null(window.FindControl<CheckBox>("UssExpandTabsBox"));

        Named<MenuItem>(window, "UssBinaryModeItem").IsChecked = true;
        Assert.Equal(Core.HostFiles.HostTransferMode.Binary, t.Vm.Uss.Mode);
        Assert.Equal("Transfer: Binary", (string)Named<DropDownButton>(window, "UssTransferButton").Content!);
        Named<MenuItem>(window, "UssVerifyItem").IsChecked = false;
        Assert.False(t.Vm.Uss.VerifyUploads);
    }

    [AvaloniaFact]
    public async Task The_strip_input_takes_a_directory_name_and_creates_it()
    {
        var (window, t) = await UssAsync();
        await ListAsync(t, "/u/ibmuser");
        window.UpdateLayout();

        window.KeyPress(Key.N, Command(window), PhysicalKey.N, null);
        await Wait.UntilAsync(() => t.Vm.HasConfirmation, "the name question");
        Assert.Equal(Core.HostFiles.HostPath.MaxUnixPathLength, Named<TextBox>(window, "ConfirmInputBox").MaxLength);
        Named<TextBox>(window, "ConfirmInputBox").Text = "drafts2";
        Named<TextBox>(window, "ConfirmInputBox").Focus();
        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        await Wait.UntilAsync(() => !t.Vm.IsBusy && !t.Vm.HasConfirmation, "the create");

        Assert.Contains("mkdir:/u/ibmuser/drafts2", t.Host.CallsSnapshot());
        Assert.Equal("drafts2", t.Vm.Uss.SelectedDirectory!.Name);
        Assert.Equal("✓ Created /u/ibmuser/drafts2.", t.Vm.StatusText);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~MvsmfBrowserWindowUssTests" 2>&1 | grep -E "error CS|Passed!|Failed!" | head -5`
Expected: build errors naming `Uss` and `IsUssTab`.

- [ ] **Step 3: The dataset view model owns the USS one**

In `src/LizTerm.App/ViewModels/MvsmfBrowserViewModel.cs`, after `public BrowserOperations Ops { get; }`, add:

```csharp
    /// <summary>The USS tab's view model, on the same runner (USS spec §4.4).</summary>
    public UssBrowserViewModel Uss { get; }
```

and in the constructor, after `Ops.PropertyChanged += OnOperationsChanged;`: `Uss = new UssBrowserViewModel(Ops, access, connection, picker, dispatch);`.

- [ ] **Step 4: The window's layout**

In `src/LizTerm.App/Views/MvsmfBrowserWindow.axaml`:

1. Delete the filter `Grid` (the one holding `FilterBox` and `ListButton`) from its place under the preview strip.
2. Replace the panes `<Grid ColumnDefinitions="*,4,*" Margin="12,0,12,8">` … `</Grid>` (the last child of the `DockPanel`) with a `TabControl` whose Datasets tab holds that filter row and the panes, and whose USS tab holds the new controls:

```xml
    <TabControl x:Name="Tabs" Margin="12,0,12,8">
      <TabItem x:Name="DatasetsTab" Header="Datasets">
        <DockPanel>
          <Grid DockPanel.Dock="Top" ColumnDefinitions="Auto,*,Auto" Margin="0,8">
            <TextBlock Text="Filter" VerticalAlignment="Center" Margin="0,0,8,0" />
            <TextBox Grid.Column="1" x:Name="FilterBox" Text="{Binding Filter}" FontFamily="Menlo, Consolas, monospace"
                     PlaceholderText="HLQ.** — for example MVSCE02.**" IsEnabled="{Binding CanChooseDataset}" />
            <Button Grid.Column="2" x:Name="ListButton" Content="List" Margin="8,0,0,0" Command="{Binding ListCommand}" />
          </Grid>
          <Grid ColumnDefinitions="*,4,*">
            <!-- the existing DatasetsPane, GridSplitter and MembersPane, unchanged -->
          </Grid>
        </DockPanel>
      </TabItem>
      <TabItem x:Name="UssTab" Header="USS">
        <!-- The tab's content binds to the USS view model; the Panel carries the context, the DockPanel the compiled
             binding type, so the DataContext binding itself resolves against the window's view model. -->
        <Panel DataContext="{Binding Uss}">
          <DockPanel x:DataType="vm:UssBrowserViewModel">
            <Grid DockPanel.Dock="Top" ColumnDefinitions="Auto,*,Auto,Auto" Margin="0,8">
              <TextBlock Text="Path" VerticalAlignment="Center" Margin="0,0,8,0" />
              <TextBox Grid.Column="1" x:Name="PathBox" Text="{Binding Path}" FontFamily="Menlo, Consolas, monospace"
                       PlaceholderText="/u/userid" IsEnabled="{Binding !IsBusy}" />
              <Button Grid.Column="2" x:Name="GoButton" Content="Go" Margin="8,0,0,0" Command="{Binding GoCommand}" />
              <Button Grid.Column="3" x:Name="UpButton" Content="↑ Up" Margin="8,0,0,0" Command="{Binding UpCommand}"
                      ToolTip.Tip="Up one directory" />
            </Grid>
            <Grid ColumnDefinitions="*,4,1.35*">
              <controls:BrowserPane x:Name="DirectoriesPane" Grid.Column="0" Title="Directories" FooterText="{Binding DirectoriesFooter}">
                <controls:BrowserPane.Toolbar>
                  <WrapPanel x:Name="DirectoryToolbar" ItemSpacing="6" LineSpacing="4">
                    <Button x:Name="NewDirectoryButton" Classes="pane-verb" Content="New…" Command="{Binding NewDirectoryCommand}" />
                    <Button x:Name="DeleteDirectoryButton" Classes="pane-verb" Content="Delete…" Command="{Binding DeleteDirectoryCommand}"
                            ToolTip.Tip="Delete the selected directory and everything in it (Delete)" />
                    <Button x:Name="UssRefreshButton" Classes="pane-verb" Content="↻ Refresh" Command="{Binding RefreshCommand}" />
                  </WrapPanel>
                </controls:BrowserPane.Toolbar>
                <controls:BrowserPane.Body>
                  <DockPanel>
                    <Grid DockPanel.Dock="Top" ColumnDefinitions="*,120" Margin="8,4">
                      <TextBlock Classes="header" Text="NAME" />
                      <TextBlock Classes="header" Grid.Column="1" Text="MODIFIED" />
                    </Grid>
                    <ListBox x:Name="DirectoryList" ItemsSource="{Binding Directories}" SelectedItem="{Binding SelectedDirectory}"
                             SelectionMode="Single" IsEnabled="{Binding !IsBusy}">
                      <ListBox.ContextMenu>
                        <ContextMenu x:Name="DirectoryMenu">
                          <MenuItem x:Name="DirectoryMenuOpen" Header="Open" Command="{Binding OpenDirectoryCommand}" InputGesture="Enter" />
                          <MenuItem x:Name="DirectoryMenuNew" Header="New…" Command="{Binding NewDirectoryCommand}" />
                          <MenuItem x:Name="DirectoryMenuDelete" Header="Delete…" Command="{Binding DeleteDirectoryCommand}" InputGesture="Delete" />
                          <Separator />
                          <MenuItem x:Name="DirectoryMenuRefresh" Header="Refresh" Command="{Binding RefreshCommand}" />
                        </ContextMenu>
                      </ListBox.ContextMenu>
                      <ListBox.ItemTemplate>
                        <DataTemplate x:DataType="vm:DirectoryRow">
                          <Grid ColumnDefinitions="*,120" Opacity="{Binding IsUsable, Converter={x:Static vm:MvsmfBrowserConverters.DimUnlessTrue}}">
                            <TextBlock Classes="cell" Text="{Binding DisplayName}" TextTrimming="CharacterEllipsis" />
                            <TextBlock Classes="cell" Grid.Column="1" Text="{Binding Modified}" />
                          </Grid>
                        </DataTemplate>
                      </ListBox.ItemTemplate>
                    </ListBox>
                  </DockPanel>
                </controls:BrowserPane.Body>
              </controls:BrowserPane>

              <GridSplitter Grid.Column="1" ResizeDirection="Columns" Background="#33373D" />

              <controls:BrowserPane x:Name="FilesPane" Grid.Column="2" Title="{Binding FilesTitle}" FooterText="{Binding FilesFooter}">
                <controls:BrowserPane.Toolbar>
                  <DockPanel>
                    <DropDownButton x:Name="UssTransferButton" DockPanel.Dock="Right" Classes="pane-verb" Content="{Binding TransferModeLabel}"
                                    Margin="8,0,0,0" VerticalAlignment="Top" IsEnabled="{Binding !IsBusy}">
                      <DropDownButton.Flyout>
                        <MenuFlyout>
                          <MenuItem x:Name="UssTextModeItem" Header="Text" ToggleType="Radio" GroupName="UssTransferMode"
                                    IsChecked="{Binding IsTextMode, Mode=TwoWay}" />
                          <MenuItem x:Name="UssBinaryModeItem" Header="Binary" ToggleType="Radio" GroupName="UssTransferMode"
                                    IsChecked="{Binding IsBinaryMode, Mode=TwoWay}" />
                          <Separator />
                          <MenuItem x:Name="UssVerifyItem" Header="Verify after upload" ToggleType="CheckBox"
                                    IsChecked="{Binding VerifyUploads, Mode=TwoWay}" />
                        </MenuFlyout>
                      </DropDownButton.Flyout>
                    </DropDownButton>
                    <WrapPanel x:Name="FileToolbar" ItemSpacing="6" LineSpacing="4">
                      <Button x:Name="UssViewButton" Classes="pane-verb" Content="View" Command="{Binding ViewCommand}"
                              ToolTip.Tip="{Binding ViewHint}" ToolTip.ShowOnDisabled="True" />
                      <Button x:Name="UssDownloadButton" Classes="pane-verb" Content="⇣ Download…" Command="{Binding DownloadCommand}" />
                      <Button x:Name="UssUploadButton" Classes="pane-verb" Content="⇡ Upload…" Command="{Binding UploadCommand}" />
                      <Button x:Name="DeleteFilesButton" Classes="pane-verb" Content="Delete…" Command="{Binding DeleteFilesCommand}"
                              ToolTip.Tip="Delete the selected files (Delete)" />
                    </WrapPanel>
                  </DockPanel>
                </controls:BrowserPane.Toolbar>
                <controls:BrowserPane.Body>
                  <DockPanel>
                    <Grid DockPanel.Dock="Top" ColumnDefinitions="*,80,120" Margin="8,4">
                      <TextBlock Classes="header" Text="NAME" />
                      <TextBlock Classes="header" Grid.Column="1" Text="SIZE" HorizontalAlignment="Right" />
                      <TextBlock Classes="header" Grid.Column="2" Text="MODIFIED" Margin="12,0,0,0" />
                    </Grid>
                    <ListBox x:Name="FileList" ItemsSource="{Binding Files}" SelectionMode="Multiple" IsEnabled="{Binding !IsBusy}">
                      <ListBox.ContextMenu>
                        <ContextMenu x:Name="FileMenu">
                          <MenuItem x:Name="FileMenuView" Header="View" Command="{Binding ViewCommand}" />
                          <MenuItem x:Name="FileMenuDownload" Header="Download…" Command="{Binding DownloadCommand}" InputGesture="Enter" />
                          <MenuItem x:Name="FileMenuUpload" Header="Upload…" Command="{Binding UploadCommand}" />
                          <MenuItem x:Name="FileMenuDelete" Header="Delete…" Command="{Binding DeleteFilesCommand}" InputGesture="Delete" />
                        </ContextMenu>
                      </ListBox.ContextMenu>
                      <ListBox.ItemTemplate>
                        <DataTemplate x:DataType="vm:FileRow">
                          <!-- A row with a result shows it where its size and time were: the result is what the
                               user is waiting for, and the columns come back with the next listing. -->
                          <Grid ColumnDefinitions="*,80,120" Opacity="{Binding IsFile, Converter={x:Static vm:MvsmfBrowserConverters.DimUnlessTrue}}">
                            <TextBlock Classes="cell" Text="{Binding DisplayName}" TextTrimming="CharacterEllipsis" />
                            <TextBlock Classes="cell" Grid.Column="1" Text="{Binding Size}" HorizontalAlignment="Right"
                                       IsVisible="{Binding Status, Converter={x:Static StringConverters.IsNullOrEmpty}}" />
                            <TextBlock Classes="cell" Grid.Column="2" Text="{Binding Modified}" Margin="12,0,0,0"
                                       IsVisible="{Binding Status, Converter={x:Static StringConverters.IsNullOrEmpty}}" />
                            <TextBlock Grid.Column="1" Grid.ColumnSpan="2" Text="{Binding Status}" TextTrimming="CharacterEllipsis"
                                       IsVisible="{Binding Status, Converter={x:Static StringConverters.IsNotNullOrEmpty}}" />
                          </Grid>
                        </DataTemplate>
                      </ListBox.ItemTemplate>
                    </ListBox>
                  </DockPanel>
                </controls:BrowserPane.Body>
              </controls:BrowserPane>
            </Grid>
          </DockPanel>
        </Panel>
      </TabItem>
    </TabControl>
```

Keep the preview strip, the error banner, the confirmation strip and the bottom bar exactly where they are: they are outside the `TabControl` and serve both tabs. Change `ConfirmInputBox`'s `MaxLength="44"` to `MaxLength="251"` and add a comment beside it: `<!-- A UNIX path's length (HostPath.MaxUnixPathLength); each question's rule bounds what it accepts. -->`.

- [ ] **Step 5: The code-behind**

In `src/LizTerm.App/Views/MvsmfBrowserWindow.axaml.cs`:

Add `using LizTerm.Core.HostFiles;`. In the constructor, after `MemberList.AddHandler(InputElement.DoubleTappedEvent, OnMemberDoubleTapped);`, add:

```csharp
        FileList.SelectionChanged += (_, _) => PushSelectedFiles();
        DirectoryList.AddHandler(InputElement.DoubleTappedEvent, OnDirectoryDoubleTapped);
        FileList.AddHandler(InputElement.DoubleTappedEvent, OnFileDoubleTapped);
        DirectoryMenuRefresh.InputGesture = RefreshGesture;
        DirectoryMenuNew.InputGesture = NewDatasetGesture;
        FileMenuView.InputGesture = ViewGesture;
        ToolTip.SetTip(UssRefreshButton, $"List this directory again ({RefreshGesture.ToString("p", null)})");
        ToolTip.SetTip(NewDirectoryButton, $"Create a directory here ({NewDatasetGesture.ToString("p", null)})");
        Tabs.SelectionChanged += OnTabChanged;
```

Add the members:

```csharp
    /// <summary>Whether the USS tab is in front: the keys and the focus fallback go to its controls then.</summary>
    internal bool IsUssTab => ReferenceEquals(Tabs.SelectedItem, UssTab);

    /// <summary>The USS tab lists its start path the first time it is shown, never at window open (USS spec §4.5).</summary>
    private void OnTabChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!IsUssTab || ViewModel is not { } vm) return;
        _ = vm.Uss.EnsureListedAsync();
    }

    /// <summary>A double-click on a directory row opens it, as Enter does; on a file row it downloads. On a row
    /// only: a double-click on the empty part of a list selects nothing and must do nothing.</summary>
    private void OnDirectoryDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (ViewModel is not { } vm) return;
        if ((e.Source as Visual)?.FindAncestorOfType<ListBoxItem>(includeSelf: true) is null) return;
        if (vm.Uss.OpenDirectoryCommand.CanExecute(null)) _ = vm.Uss.OpenDirectoryCommand.ExecuteAsync(null);
    }

    private void OnFileDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (ViewModel is not { } vm) return;
        if ((e.Source as Visual)?.FindAncestorOfType<ListBoxItem>(includeSelf: true) is null) return;
        if (vm.Uss.DownloadCommand.CanExecute(null)) _ = vm.Uss.DownloadCommand.ExecuteAsync(null);
    }

    /// <summary>The file list is the selection's owner, as the member list is (PushSelectedMembers).</summary>
    private void PushSelectedFiles()
    {
        if (ViewModel is not { } vm) return;
        vm.Uss.SetSelectedFiles(FileList.SelectedItems?.OfType<FileRow>() ?? []);
    }

    /// <summary>A refresh keeping the selection: the list box takes the rows and pushes them back.</summary>
    private void SelectFiles(IReadOnlyList<FileRow> rows)
    {
        if (FileList.SelectedItems is not { } selected) return;
        selected.Clear();
        foreach (var row in rows) selected.Add(row);
    }

    private void OnUssPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_watched is not { } vm) return;
        if (e.PropertyName == nameof(UssBrowserViewModel.Viewer) && vm.Uss.Viewer is { } viewer) ShowViewer(viewer);
    }

    /// <summary>The USS tab's keys (USS spec §4.5), mirroring the Datasets tab's: handled only while it is in
    /// front, and only the keys it owns; Escape's ladder is shared and stays in the caller.</summary>
    private bool HandleUssKey(MvsmfBrowserViewModel vm, KeyEventArgs e)
    {
        var uss = vm.Uss;
        switch (e.Key)
        {
            case Key.R when e.KeyModifiers == RefreshGesture.KeyModifiers:
                if (uss.RefreshCommand.CanExecute(null)) _ = uss.RefreshCommand.ExecuteAsync(null);
                return true;
            case Key.N when e.KeyModifiers == NewDatasetGesture.KeyModifiers:
                if (uss.NewDirectoryCommand.CanExecute(null)) _ = uss.NewDirectoryCommand.ExecuteAsync(null);
                return true;
            case Key.Delete or Key.Back when DirectoryList.IsKeyboardFocusWithin:
                if (uss.DeleteDirectoryCommand.CanExecute(null)) _ = uss.DeleteDirectoryCommand.ExecuteAsync(null);
                return true;
            case Key.Delete or Key.Back when FileList.IsKeyboardFocusWithin:
                if (uss.DeleteFilesCommand.CanExecute(null)) _ = uss.DeleteFilesCommand.ExecuteAsync(null);
                return true;
            case Key.Enter when e.KeyModifiers == ViewGesture.KeyModifiers && FileList.IsKeyboardFocusWithin:
                if (uss.ViewCommand.CanExecute(null)) _ = uss.ViewCommand.ExecuteAsync(null);
                return true;
            case Key.Enter when e.KeyModifiers == KeyModifiers.None && PathBox.IsFocused:
                if (uss.GoCommand.CanExecute(null)) _ = uss.GoCommand.ExecuteAsync(null);
                return true;
            case Key.Enter when e.KeyModifiers == KeyModifiers.None && DirectoryList.IsKeyboardFocusWithin:
                if (uss.OpenDirectoryCommand.CanExecute(null)) _ = uss.OpenDirectoryCommand.ExecuteAsync(null);
                return true;
            case Key.Enter when e.KeyModifiers == KeyModifiers.None && FileList.IsKeyboardFocusWithin:
                if (uss.DownloadCommand.CanExecute(null)) _ = uss.DownloadCommand.ExecuteAsync(null);
                return true;
            default:
                return false;
        }
    }
```

In `OnDataContextChanged`, beside the existing subscriptions, unsubscribe and subscribe the USS ones:

```csharp
            _watched.Uss.PropertyChanged -= OnUssPropertyChanged;
            _watched.Uss.SelectFilesRequested -= SelectFiles;
```
```csharp
            _watched.Uss.PropertyChanged += OnUssPropertyChanged;
            _watched.Uss.SelectFilesRequested += SelectFiles;
            _watched.Uss.ViewGestureText = ViewGesture.ToString("p", null);
```

and the same two unsubscriptions in `OnClosed`. In `OnKeyDownTunnel`, right after `if (ViewModel is not { } vm) return;`, add:

```csharp
        // The confirmation strip's Enter and Escape are the window's whichever tab is in front, so they stay below;
        // the USS tab's own keys are taken here first, and a key it does not own falls through to the shared cases.
        if (IsUssTab && vm.Confirmation is null && HandleUssKey(vm, e))
        {
            e.Handled = true;
            return;
        }
```

In `FocusedList`, return the USS lists too:

```csharp
    private ListBox? FocusedList() =>
        DatasetList.IsKeyboardFocusWithin ? DatasetList
        : MemberList.IsKeyboardFocusWithin ? MemberList
        : DirectoryList.IsKeyboardFocusWithin ? DirectoryList
        : FileList.IsKeyboardFocusWithin ? FileList
        : null;
```

In `PostRestoreFocus`, change `if (!target.IsEffectivelyVisible) target = FilterBox;` to `if (!target.IsEffectivelyVisible) target = IsUssTab ? PathBox : FilterBox;`, and in the `IsCreating` case's fallback `FilterBox.Focus();` to `(IsUssTab ? PathBox : FilterBox).Focus();`.

- [ ] **Step 6: Run the window tests, then the whole App project**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~MvsmfBrowserWindowUssTests|FullyQualifiedName~MvsmfBrowserWindowTests" 2>&1 | grep -E "error|Passed!|Failed!|\[FAIL\]" | head -12; dotnet test tests/LizTerm.App.Tests 2>&1 | grep -E "Passed!|Failed!"; dotnet build LizTerm.slnx --no-incremental 2>&1 | grep -c " warning "`
Expected: `Passed!` for both classes (the existing window tests prove the Datasets tab lost nothing by moving into a `TabItem`: a `TabControl` hosts only the selected tab's content, and the Datasets tab is selected first, so `FindControl` and the visual-tree walks there still work), the project `Passed!`, then `0`. If a headless test cannot find a USS control after selecting the tab, the tests/CLAUDE.md note applies: a `TabControl` hosts only the selected tab's content, so select the tab and `UpdateLayout()` before reaching through the visual tree; `FindControl` by name reaches an unselected tab's controls regardless.

- [ ] **Step 7: Commit**

```bash
git add src/LizTerm.App/Views/MvsmfBrowserWindow.axaml src/LizTerm.App/Views/MvsmfBrowserWindow.axaml.cs src/LizTerm.App/ViewModels/MvsmfBrowserViewModel.cs tests/LizTerm.App.Tests/Views/MvsmfBrowserWindowUssTests.cs
git commit -m "mvsMF Access: the USS tab (#176)

A TabControl with Datasets and USS; the USS tab has a path row, a
Directories pane and a Files pane, its keys, context menus and
transfer drop-down, over the window's one status line, banner and
confirmation strip.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 7: Docs — the user guide, the changelog, the notes

**Files:**
- Modify: `docs/user-guide.md` (and its bundled HTML, regenerated), `CHANGELOG.md`, `src/LizTerm.App/CLAUDE.md`, `tests/CLAUDE.md`

**Interfaces:**
- Consumes: everything Tasks 1 to 6 built; the words below match the controls and messages they made.

- [ ] **Step 1: The user guide**

In `docs/user-guide.md`, at the start of the "### Browsing" subsection, before "Type a dataset pattern…", add the paragraph:

```markdown
The window has two tabs. **Datasets**, described here, browses datasets and members; **USS** browses the host's UNIX
file system (see [USS](#uss)). The status line at the bottom, the **Cancel** button and the questions asked in the
strip above it serve whichever tab is in front, and one operation runs at a time across both.
```

After the "### Managing datasets" subsection and before "### When something goes wrong", add:

```markdown
### USS

The **USS** tab browses the host's UNIX file system one directory at a time. The **Path** box names the current
directory; it starts at your home directory, `/u/` followed by your userid in lower case, the first time you open the
tab. Type a path and choose **Go** (or press **Enter**) to list it, or **↑ Up** to go to the directory above. A path
the host does not have leaves the box as you typed it and the panes as they were, with the reason in the status
line.

The **Directories** pane on the left lists the subdirectories with their **NAME** and **MODIFIED** time. **Enter** or a
double-click on one opens it. Its toolbar has **New…**, which asks for a name in the strip at the bottom and
creates the directory here, **Delete…**, which deletes the selected directory *and everything in it* after a question,
and **↻ Refresh**.

The pane on the right, titled with the current path, lists the files with their **NAME**, **SIZE** and **MODIFIED**
time, and you can select several at once. Its toolbar has **View**, **⇣ Download…**, **⇡ Upload…** and **Delete…**,
and a **Transfer** drop-down with **Text** and **Binary** and **Verify after upload**. An entry that is not a
regular file (a link, say) is listed as **(not a file)**, and nothing can be done with it.

**Download…** saves the selected files under their own names, asking before it replaces a file you already have.
**Upload…** sends the files you choose into the current directory under their own names; a file that already exists
there is replaced after a question, and a file this window downloaded or uploaded earlier is checked against the
host's copy first, so a change made on the host meanwhile gets a question rather than being overwritten. **View**
opens one file read-only, as it does a member.

A UNIX file is a stream of bytes, not records, so the **Text** transfer keeps tabs and trailing blanks as they are
in both directions, and **Verify after upload** compares exactly. The host holds at most 1 MiB in one file: a
larger file is refused before anything is sent, and the status line says so. mvsMF cannot rename or move a UNIX
file, so there is no **Rename…** on this tab.

Each list has a right-click menu with the same actions, and the keys are the Datasets tab's: **Enter** or a
double-click on a file downloads it, **Delete** or **Backspace** deletes what is selected in the focused list,
**Cmd+R** (macOS) or **Ctrl+R** refreshes, **Cmd+N** or **Ctrl+N** creates a directory, **Cmd+Enter** or
**Ctrl+Enter** views the selected file, and **Escape** cancels a question, then a running operation, then closes
the window.
```

Then regenerate the bundled HTML and confirm the asset test is green:

```bash
LIZTERM_UPDATE_DOCS=1 dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~UserGuideAssetTests" 2>&1 | grep -E "Passed!|Failed!"
git status --short
```

Expected: `Passed!`, and `git status` shows `docs/user-guide.md` and the regenerated HTML asset (the path `UserGuideAssetTests` names) modified.

- [ ] **Step 2: The changelog**

In `CHANGELOG.md`, under `## Unreleased`, as the first bullet:

```markdown
- **mvsMF Access has a USS tab** for the host's UNIX file system: browse a directory at a time, create and delete
  directories, download, upload, view and delete files ([#176](https://github.com/coffeemuse/LizTerm/issues/176)).
```

(one to three lines, no blank line before the next bullet: the changelog's style).

- [ ] **Step 3: The App notes**

In `src/LizTerm.App/CLAUDE.md`, in the "## mvsMF Access" section, after the bullet that begins "`MvsmfBrowserViewModel` runs one operation at a time", add:

```markdown
- **The runner is `BrowserOperations`** (USS spec §4.4): the busy flag and its cancellation, the status line, the
  banner with Retry, the confirmation strip, `WhenIdle` and `TryPickAsync`. `MvsmfBrowserViewModel` forwards its
  old properties and commands to it under their old names, re-raising the runner's changes as its own (IsBusy
  notifies the commands first, then the property, as the generated hook did), so the window's bindings never
  changed. `UssBrowserViewModel` runs on the same instance, which is what makes one operation at a time hold
  across both tabs and puts every result on the one status line. `BrowserTransfers` is the download side both
  share: one transfer reported where its caller says, and the two-at-a-time batch with its connection-failure stop.
- **The USS tab** (`UssBrowserViewModel`, three partials): `Path` is the box, `Current` the listed directory (null
  until a listing lands), `Directories` and `Files` its rows, sorted by name here because the host lists in its own
  order. `ListCoreAsync` is the one listing: a path the rules refuse or the host does not have is a status line, and
  the box keeps its text while the panes keep the last good listing. `EnsureListedAsync` runs on the tab's first
  show. `DirectoryRow.Path`/`FileRow.Path` are null for a name the rules refuse and `FileRow.IsFile` is false for
  anything but a regular file; every verb needs usable rows, and Delete never sees the root because the rows are
  the current directory's children. Uploads check every file first (`CheckUnixTextFile` with tabs kept,
  `BinaryUploadProblem`), list the directory again before calling a name existing, and put each file's result on
  its row once the listing after the batch has given new files theirs; the refused ones are named on the status
  line, never as rows. `Mode` and `VerifyUploads` are the tab's own; trailing blanks are never trimmed and tabs
  never expanded. The window's `TabControl` hosts the Datasets tab's filter row and panes unchanged; the USS tab's
  keys go through `HandleUssKey` first while that tab is in front and no question is up, and Escape's ladder is
  shared. The viewer window is shared: either tab's `Viewer` opens or reuses it.
```

- [ ] **Step 4: The test notes**

In `tests/CLAUDE.md`, after the `BrowserTestHost` bullet, add:

```markdown
- `UssTestHost` (`ViewModels/`) builds a `UssBrowserViewModel` on its own `BrowserOperations` over a fake seeded by
  `Standard` (`/u/ibmuser` with `notes` — `drafts`, `README.txt`, `todo.md`, `data.bin` — and `old`, beside `/tmp`
  and `/u/mvsce02`); `ListAsync(path)` types and presses Go, `Select` pushes a file selection, `AskedAsync` waits for
  a question. The window tests for the tab (`MvsmfBrowserWindowUssTests`) seed the fake with both `Standard`s and
  select the tab (`Tabs.SelectedIndex = 1` then `UpdateLayout()`) before reaching its controls through the visual
  tree, since a `TabControl` hosts only the selected tab's content; the profile's userid there is `MVSCE02`, so the
  start path is `/u/mvsce02`.
```

- [ ] **Step 5: Verify and commit**

Run: `dotnet build LizTerm.slnx --no-incremental 2>&1 | grep -c " warning "; dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~UserGuideAssetTests|FullyQualifiedName~RepositoryHeadersTests" 2>&1 | grep -E "Passed!|Failed!"`
Expected: `0`, then `Passed!`.

```bash
git add docs/user-guide.md CHANGELOG.md src/LizTerm.App/CLAUDE.md tests/CLAUDE.md
git add -A docs/
git commit -m "Docs: the USS tab (#176)

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

(`git add -A docs/` picks up the regenerated guide HTML wherever the asset test writes it.)

---

### Task 8: Verification, the pull request, and the hands-on checklist

**Files:** none new.

- [ ] **Step 1: Warnings, the suite, the tree**

```bash
dotnet build LizTerm.slnx --no-incremental 2>&1 | grep -c " warning "
dotnet test LizTerm.slnx 2>&1 | grep -E "Passed!|Failed!|error" | head -12
git status --short
grep -rn "MvsmfFileService" src/LizTerm.App --include='*.cs' | grep -v HostFileServiceFactory.cs | wc -l
```

Expected: `0`; every project `Passed!`; a clean tree; `0` (the dependency rule).

- [ ] **Step 2: Review the branch as a whole**

```bash
git log --oneline main..HEAD
git log main..HEAD --format='%h %(trailers:key=Co-Authored-By)'
git diff main..HEAD --stat | tail -1
```

Expected: the plan commit and seven task commits, each with the `Claude Fable 5.1` trailer.

- [ ] **Step 3: Push and open the PR**

```bash
git push -u origin claude/uss-tab-pr2-176
gh pr create --base main --title "mvsMF USS support, PR 2: the USS tab (#176)" --body "$(cat <<'EOF'
Closes #176. The USS tab in the mvsMF Access window, on the Core contract and backend PR #183 merged.

- **A `TabControl` with Datasets and USS.** The Datasets tab keeps its filter row and panes as they were; the USS tab has a path row (Path, Go, ↑ Up), a Directories pane (New…, Delete…, ↻ Refresh) and a Files pane (View, ⇣ Download…, ⇡ Upload…, Delete…, and a Transfer drop-down holding Text, Binary and Verify after upload). The status line, progress, Cancel, the banner and the confirmation strip stay on the window and serve both tabs.
- **One operation at a time across both tabs**: the runner (`BrowserOperations`) moved out of the dataset view model, which forwards to it under its old names, so the window's bindings and every existing test are unchanged; `UssBrowserViewModel` runs on the same instance. The download batch is a shared `BrowserTransfers`.
- Navigation is one level at a time: Enter or a double-click on a directory descends, Up climbs, a typed path lists; a path the host does not have keeps the box and the panes as they were with the reason on the status line. Rows are sorted by name here, since the host lists in its own order.
- Uploads check every file first (the 1 MiB cap, UTF-8, tabs kept), list the directory again before calling a name existing, ask before replacing, send a remembered stamp and turn a 412 into the Replace anyway / Skip question; refused files are named on the status line. Downloads keep trailing blanks. View opens the shared viewer.
- An entry that is not a regular file is listed as "(not a file)" and every verb stays off for it; the root is never a row, so nothing can ask to delete it.
- User guide **USS** subsection, CHANGELOG line, notes in the App and tests CLAUDE.md files.

Spec: `docs/superpowers/specs/2026-09-22-mvsmf-uss-tab-design.md`. Plan: `docs/superpowers/plans/2026-09-22-mvsmf-uss-tab.md`.

**Hands-on pass against MVS/CE is the merge gate** (the checklist is in the plan's Task 8).

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
```

Expected: the PR URL. If the create fails with a 5xx on the body, create it with a one-line body and set the description with `gh api -X PATCH repos/coffeemuse/LizTerm/issues/<n> -f body=@<file>`.

- [ ] **Step 4: Hand Robert the hands-on checklist**

The recipe for driving the app against MVS/CE is in the memory file for the manage slice (seed a profile with `hostFilesUrl` `http://10.42.37.209:8080/zosmf` and `hostFilesUserid` `IBMUSER` under a scratch `HOME`, launch with `LIZTERM_B3270_PATH=/opt/homebrew/bin/b3270`, File > mvsMF Access…). Seed a few files first with `curl --netrc-file ~/.mvsmf-netrc` (a text file with a tab and trailing blanks, a small binary, a subdirectory) under `/u/ibmuser`, and keep every file small: the host's UFS did not return space after PR 1's probes. The checks, each a line in the report to Robert:

1. Open mvsMF Access: the Datasets tab is in front and lists as before; the USS tab lists `/u/ibmuser` only when first clicked.
2. Type `/` and Go; descend into `u` with Enter and with a double-click; Up back to `/`; Up is off at `/`.
3. Type `/u/nobody` and Go: the box keeps the text, the panes keep `/`, the status line says the file was not found.
4. New… a directory, with a name that exists (refused in the strip), then a new one; it is created and selected.
5. Upload the seeded text file's local copy over itself (the replace question), then a second time after changing it on the host with curl (the 412 question, Replace anyway), then a file over 1 MiB (refused, named on the status line, nothing sent).
6. Download one file to a name, and two into a folder with one already there (the Replace/Skip question); compare the text one with `diff`, the binary one with `cmp`.
7. View the text file: the viewer opens, and a second View from the Datasets tab reuses the same window.
8. Delete two files with Delete key and the question; delete the new directory with Delete… and the question.
9. Start a slow listing on the USS tab, switch to the Datasets tab: the status line and Cancel still show it; the Datasets pane's List is off until it ends.
10. Escape: with a question up, cancels it; with an operation running, cancels it; otherwise closes the window.
11. Cmd+R, Cmd+N, Cmd+Enter on the USS tab act on the USS panes; Cmd+R on the Datasets tab still refreshes datasets.
12. Remove everything seeded; confirm `/u/ibmuser` lists nothing new.

- [ ] **Step 5: Report**

Tell Robert: the PR URL, the suite and warning results, that the hands-on checklist is in the plan's Task 8, and that #176 closes on merge.
