# mvsMF manage slice, PR 2 (the browser) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give the mvsMF Browser New…, Rename… and Delete… for datasets, Rename… for members, and a warning before an upload replaces a member that changed on the host since it was downloaded, all on the contract PR 1 merged.

**Architecture:** The browser view model gains two partial files beside its existing slices: `Manage` (rename a member or a dataset, delete a dataset) and `Create` (the New dataset form, a view model of its own shown in the right pane where the upload review goes). `ConfirmationRequest` learns to carry a text box, so a rename asks in the existing strip. An `EtagMemory` on the session's `HostFileAccess` keeps the stamp of every member this window downloaded or wrote; an upload that replaces such a member sends the stamp as `ifMatch`, and a `Conflict` is a second question. Nothing below `LizTerm.App` changes. This is PR 2 of the manage slice; PR 1 (#150) built the contract and the backend.

**Tech Stack:** .NET 10, C# latest, Avalonia 12 with CommunityToolkit.Mvvm, xunit.v3 with `Avalonia.Headless.XUnit`.

**Spec:** `docs/superpowers/specs/2026-09-19-mvsmf-manage-slice-design.md` (read §3, §4.4, §4.5, §5, §7 and §10 before starting; §10 says what PR 1 actually built and overrides §4.1's listing).

## Global Constraints

- Every hand-written `.cs` and `.axaml` file starts with the three licence lines (before the root element in `.axaml`): `This file is part of LizTerm.` / `Copyright 2026 by CoffeeMuse` / `SPDX-License-Identifier: BSD-3-Clause`. A new file needs them; `RepositoryHeadersTests` fails the suite for a missing one.
- **Dependency rule.** `LizTerm.App` names the mvsMF backend only in `src/LizTerm.App/HostFileServiceFactory.cs`; every file this plan touches talks to `IHostFileService`, `HostPath`, `DatasetAllocation` and `HostFileException` from Core. No App file or App test may mention `MvsmfFileService`, `mvsMF-compat` or a fixture.
- **The stamp is opaque.** The browser stores an `ETag` value as the host gave it and hands it back as `ifMatch` unchanged; it never parses, trims or compares it beyond what `EtagMemory` does (a dictionary lookup).
- **Marks.** Every status line, row status, review message and form message that reports an outcome, a warning or progress starts with one of `✓ ✗ ⚠ ⟳ –` and words; colour never carries meaning alone.
- **One operation at a time.** Every host operation runs through `RunExclusiveAsync`; a connection failure (`IsConnectionFailure`) is the banner with Retry, anything else is a status line, a row status or the form's message.
- **The spec's wording is the wording.** Question texts, button labels and status lines are quoted in this plan from §4.4 and §5.2; use them verbatim, including the spaced en dash after `–` and the trailing full stops.
- Zero warnings: `dotnet build LizTerm.slnx --no-incremental 2>&1 | grep -c " warning "` prints `0`.
- Commits end with `Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>`.

## Prerequisites

- Work on the branch `claude/mvsmf-manage-browser` (this worktree is on it, at `c054474` over `main` at `01094e8`). The spec's §10 and this plan are committed to it.
- No task needs the live host. The hands-on pass before the PR merges does (see "Before the PR" at the end).
- Run the App tests with `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~<Class>"`; the whole suite with `dotnet test LizTerm.slnx`.

## How the browser is built today (read this before any task)

- `src/LizTerm.App/ViewModels/MvsmfBrowserViewModel.cs` is the core: `Datasets`, `SelectedDataset` (two-way bound to the dataset list), `Members`/`VisibleMembers`, `_selectedMembers` (pushed by the window through `SetSelectedMembers`), `IsBusy`, `StatusText`, `ErrorText`/`_retry` (the banner), `Confirmation` (the strip), `RunExclusiveAsync(work, retry, describe)`, `AskAsync(request)`, `ListCoreAsync(token)`, `LoadMembersAsync(row)` and `NotifyCommands()`. The partial files `Downloads`, `Uploads`, `Delete` and `Paging` each add one slice; `Paging` owns `LoadMembersCoreAsync(row, token)`, `_listedPattern`, `_memberPattern` and `HasMoreMembers`.
- `RunExclusiveAsync` **ignores a second operation while one runs** (`if (IsBusy || _disposed) return;`). Setting `SelectedDataset` from inside a running operation therefore starts no member load of its own: `OnSelectedDatasetChanged` calls `LoadMembersAsync`, which clears the list and then hands `RunExclusiveAsync` a load that is dropped. Task 4 adds `SelectAsync` for that case.
- A connection failure part-way through a delete swaps the retry to what is left (`DeleteMembersAsync`'s `remaining` closure). Task 4 generalises that shape: an operation whose second half is a listing swaps its retry to the listing once the host has done the first half.
- The window (`Views/MvsmfBrowserWindow.axaml(.cs)`) remembers where the keyboard was when `IsBusy` turns on and restores it at `Loaded` priority when the operation ends; a question moves the focus to its Cancel button. Escape goes: question, running operation, upload review, window.
- Tests: `tests/LizTerm.App.Tests/ViewModels/BrowserTestHost.cs` builds a view model over `Fakes/FakeHostFileService.cs` (an in-memory host that logs `op:target` calls, fails under `Failures[key]`, holds every call behind `Gate`, stamps writes `stamp-N` and answers a stale `ifMatch` with `Conflict`). `Standard` seeds `MVSCE02.CNTL` (PO, FB 80, members ALLOC COMPILE HELLO), `MVSCE02.LOAD` (PO, U 0 19069, member PROG), `MVSCE02.UFSHOME` (PS, U 0 4096) and `MVSCE02.DB` (DA, not supported). `ChooseAsync(name)` lists and selects; `Select(names)` sets the member selection. Window tests are `[AvaloniaFact]`s in `Views/MvsmfBrowserWindowTests.cs` over a real headless window; `Wait.UntilAsync(condition, what)` polls.

## File Structure

| File | Responsibility |
|---|---|
| `src/LizTerm.App/HostFiles/EtagMemory.cs` | **New.** Path → stamp, thread-safe; `Remember`, `TryGet`, `Forget`, `ForgetUnder`, `Move` |
| `src/LizTerm.App/HostFiles/HostFileAccess.cs` | `Etags`, one memory per session window |
| `src/LizTerm.App/ViewModels/ConfirmationRequest.cs` | Optional input: `HasInput`, `Input`, `InputLabel`, `InputProblem`, `CanAnswerPrimary` |
| `src/LizTerm.App/ViewModels/MvsmfBrowserViewModel.Manage.cs` | **New.** Rename member, rename dataset, delete dataset; `SelectAsync`, `ShowAfterChangeAsync`, `RunThenListAsync`; `SelectMemberRequested` |
| `src/LizTerm.App/ViewModels/NewDatasetFormViewModel.cs` | **New.** The form's fields, per-field problems, `Allocation`, `CanCreate` |
| `src/LizTerm.App/ViewModels/MvsmfBrowserViewModel.Create.cs` | **New.** `IsCreating`, `Form`, `NewDatasetCommand`, `CreateCommand`, `CloseFormCommand` |
| `src/LizTerm.App/ViewModels/MvsmfBrowserViewModel.cs` | `!IsCreating` in the pane and list rules; `NotifyCommands` additions; watches the form |
| `src/LizTerm.App/ViewModels/MvsmfBrowserViewModel.Delete.cs` | Forgets a deleted member's stamp; the loop is guarded on `HostPathKind.Member` |
| `src/LizTerm.App/ViewModels/MvsmfBrowserViewModel.Downloads.cs` | Remembers a downloaded stamp |
| `src/LizTerm.App/ViewModels/MvsmfBrowserViewModel.Uploads.cs` | Sends `ifMatch` for a replaced member; the conflict question; remembers the write's stamp |
| `src/LizTerm.App/ViewModels/MvsmfBrowserViewModel.Paging.cs` | `!IsCreating` in the Load more rules |
| `src/LizTerm.App/Views/MvsmfBrowserWindow.axaml` | The strip's text box; the dataset pane's button row; the bottom bar's Rename…; the create pane |
| `src/LizTerm.App/Views/MvsmfBrowserWindow.axaml.cs` | Focus and Enter for the text box; the renamed member's selection; Escape order; the form's focus |
| `tests/LizTerm.App.Tests/HostFiles/EtagMemoryTests.cs` | **New.** |
| `tests/LizTerm.App.Tests/ViewModels/ConfirmationRequestTests.cs` | **New.** |
| `tests/LizTerm.App.Tests/ViewModels/MvsmfBrowserEtagTests.cs` | **New.** §7.5's ETag list |
| `tests/LizTerm.App.Tests/ViewModels/MvsmfBrowserManageTests.cs` | **New.** §7.5's rename and delete-dataset list |
| `tests/LizTerm.App.Tests/ViewModels/NewDatasetFormViewModelTests.cs` | **New.** |
| `tests/LizTerm.App.Tests/ViewModels/MvsmfBrowserCreateTests.cs` | **New.** §7.5's create list |
| `tests/LizTerm.App.Tests/Views/MvsmfBrowserWindowTests.cs` | The strip's text box, the buttons' enablement, the create pane, Escape, focus |
| `docs/user-guide.md`, `CHANGELOG.md`, `src/LizTerm.App/CLAUDE.md`, `tests/CLAUDE.md` | §7.6 |

---

### Task 1: `EtagMemory` on the session's access; the fake narrows dataset listings

**Files:**
- Create: `src/LizTerm.App/HostFiles/EtagMemory.cs`
- Modify: `src/LizTerm.App/HostFiles/HostFileAccess.cs` (a property after `CanRememberPin`)
- Modify: `tests/LizTerm.App.Tests/Fakes/FakeHostFileService.cs` (`ListDatasetsAsync`, `PageOf`'s caller)
- Test: `tests/LizTerm.App.Tests/HostFiles/EtagMemoryTests.cs` (new), `tests/LizTerm.App.Tests/HostFiles/FakeHostFileServiceTests.cs` (one test)

**Interfaces:**
- Consumes: `HostPath` (`Kind`, `Dataset`, `Member`, `ToString()` = `DSN` or `DSN(MEMBER)`).
- Produces: `EtagMemory` with `void Remember(HostPath path, string? etag)`, `string? TryGet(HostPath path)`, `void Forget(HostPath path)`, `void ForgetUnder(HostPath dataset)`, `void Move(HostPath from, HostPath to)`, `int Count`; `HostFileAccess.Etags` (`EtagMemory`, one per access). Tasks 3, 4 and 6 use `_access.Etags`. The fake's `ListDatasetsAsync` now returns only the datasets its pattern matches (`**` any qualifiers, `*` within a qualifier, `%` one character, case ignored), which Tasks 4 and 6 need for "not shown by the filter". Every existing test lists with a pattern its seeds match (`MVSCE02.**` over `Standard`/`Large`, `A.**` over `A.CNTL`), so nothing else moves.

- [ ] **Step 1: Write the failing tests**

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.App.HostFiles;
using LizTerm.App.Tests.Fakes;
using LizTerm.Core.HostFiles;
using LizTerm.Core.Session;

namespace LizTerm.App.Tests.HostFiles;

public class EtagMemoryTests
{
    private static readonly HostPath Hello = HostPath.ForMember("MVSCE02.CNTL", "HELLO");
    private static readonly HostPath Alloc = HostPath.ForMember("MVSCE02.CNTL", "ALLOC");
    private static readonly HostPath Cntl = HostPath.ForDataset("MVSCE02.CNTL");

    [Fact]
    public void Remembers_a_stamp_by_path_exactly_as_given()
    {
        var memory = new EtagMemory();

        memory.Remember(Hello, "W/\"347617DA000000A0\"");

        Assert.Equal("W/\"347617DA000000A0\"", memory.TryGet(Hello));
        Assert.Null(memory.TryGet(Alloc));
        Assert.Equal(1, memory.Count);
    }

    [Fact]
    public void Remembering_null_forgets()
    {
        var memory = new EtagMemory();
        memory.Remember(Hello, "a");

        memory.Remember(Hello, null);

        Assert.Null(memory.TryGet(Hello));
        Assert.Equal(0, memory.Count);
    }

    [Fact]
    public void Forget_drops_one_path()
    {
        var memory = new EtagMemory();
        memory.Remember(Hello, "a");
        memory.Remember(Alloc, "b");

        memory.Forget(Hello);

        Assert.Null(memory.TryGet(Hello));
        Assert.Equal("b", memory.TryGet(Alloc));
    }

    [Fact]
    public void ForgetUnder_drops_the_dataset_and_its_members_but_not_a_longer_name()
    {
        var memory = new EtagMemory();
        memory.Remember(Cntl, "ds");
        memory.Remember(Hello, "a");
        memory.Remember(HostPath.ForDataset("MVSCE02.CNTL2"), "other");
        memory.Remember(HostPath.ForMember("MVSCE02.CNTL2", "HELLO"), "other-member");

        memory.ForgetUnder(Cntl);

        Assert.Null(memory.TryGet(Cntl));
        Assert.Null(memory.TryGet(Hello));
        Assert.Equal("other", memory.TryGet(HostPath.ForDataset("MVSCE02.CNTL2")));
        Assert.Equal("other-member", memory.TryGet(HostPath.ForMember("MVSCE02.CNTL2", "HELLO")));
    }

    [Fact]
    public void Move_carries_a_members_stamp_to_its_new_name()
    {
        var memory = new EtagMemory();
        memory.Remember(Hello, "a");

        memory.Move(Hello, HostPath.ForMember("MVSCE02.CNTL", "HELLO2"));

        Assert.Null(memory.TryGet(Hello));
        Assert.Equal("a", memory.TryGet(HostPath.ForMember("MVSCE02.CNTL", "HELLO2")));
    }

    [Fact]
    public void Move_of_a_dataset_carries_everything_under_it()
    {
        var memory = new EtagMemory();
        memory.Remember(Cntl, "ds");
        memory.Remember(Hello, "a");
        memory.Remember(HostPath.ForMember("MVSCE02.CNTL2", "HELLO"), "other");

        memory.Move(Cntl, HostPath.ForDataset("MVSCE02.JCL"));

        Assert.Null(memory.TryGet(Cntl));
        Assert.Null(memory.TryGet(Hello));
        Assert.Equal("ds", memory.TryGet(HostPath.ForDataset("MVSCE02.JCL")));
        Assert.Equal("a", memory.TryGet(HostPath.ForMember("MVSCE02.JCL", "HELLO")));
        Assert.Equal("other", memory.TryGet(HostPath.ForMember("MVSCE02.CNTL2", "HELLO")));
    }

    [Fact]
    public void Move_of_a_path_with_no_stamp_does_nothing()
    {
        var memory = new EtagMemory();

        memory.Move(Hello, Alloc);

        Assert.Equal(0, memory.Count);
    }

    [Fact]
    public void An_access_owns_one_memory()
    {
        var access = new HostFileAccess(
            new SessionProfile { Name = "MVS/CE", Host = "mvs", HostFilesUrl = "http://mvs:8080" },
            (_, _, _) => new FakeHostFileService(), savePin: null);

        Assert.Same(access.Etags, access.Etags);
        Assert.Equal(0, access.Etags.Count);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~EtagMemoryTests"`
Expected: build error, `EtagMemory` not found.

- [ ] **Step 3: Write `EtagMemory`**

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.Core.HostFiles;

namespace LizTerm.App.HostFiles;

/// <summary>The stamps (<c>ETag</c> values) of the members and datasets this session window has downloaded or
/// written, keyed by <see cref="HostPath.ToString"/> (spec §5.1). An upload that replaces a remembered member sends
/// its stamp, so a change on the host since the download is caught. One per <see cref="HostFileAccess"/>, so per
/// host by construction. Thread-safe: a download batch remembers from two transfers at once.</summary>
public sealed class EtagMemory
{
    private readonly Dictionary<string, string> _stamps = new(StringComparer.Ordinal);
    private readonly object _lock = new();

    /// <summary>Keeps <paramref name="etag"/> for the path, as given; null (a host that sent none) forgets it.</summary>
    public void Remember(HostPath path, string? etag)
    {
        lock (_lock)
        {
            if (etag is null) _stamps.Remove(path.ToString());
            else _stamps[path.ToString()] = etag;
        }
    }

    public string? TryGet(HostPath path)
    {
        lock (_lock) return _stamps.GetValueOrDefault(path.ToString());
    }

    public void Forget(HostPath path)
    {
        lock (_lock) _stamps.Remove(path.ToString());
    }

    /// <summary>Drops the dataset's own entry and every member's: a deleted dataset.</summary>
    public void ForgetUnder(HostPath dataset)
    {
        lock (_lock)
        {
            foreach (var key in KeysUnder(dataset.Dataset)) _stamps.Remove(key);
        }
    }

    /// <summary>A rename: a member's entry moves to its new name; a dataset's moves with everything under it.</summary>
    public void Move(HostPath from, HostPath to)
    {
        lock (_lock)
        {
            if (from.Kind == HostPathKind.Member)
            {
                if (_stamps.Remove(from.ToString(), out var stamp)) _stamps[to.ToString()] = stamp;
                return;
            }
            foreach (var key in KeysUnder(from.Dataset))
            {
                var stamp = _stamps[key];
                _stamps.Remove(key);
                _stamps[to.Dataset + key[from.Dataset.Length..]] = stamp;
            }
        }
    }

    public int Count
    {
        get { lock (_lock) return _stamps.Count; }
    }

    /// <summary>The dataset's own key and its members' (<c>NAME</c> and <c>NAME(…)</c>), never a longer name's.
    /// Materialised, since the callers remove while they walk.</summary>
    private List<string> KeysUnder(string dataset) =>
        _stamps.Keys.Where(key => key == dataset || key.StartsWith(dataset + "(", StringComparison.Ordinal)).ToList();
}
```

- [ ] **Step 4: Add the property to `HostFileAccess`**

After `public bool CanRememberPin => _savePin is not null;` in `src/LizTerm.App/HostFiles/HostFileAccess.cs`:

```csharp
    /// <summary>The stamps of what this session's browser windows have downloaded or written (spec §5.1).</summary>
    public EtagMemory Etags { get; } = new();
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~EtagMemoryTests"`
Expected: 8 passed.

- [ ] **Step 6: Write the failing fake test**

Add to `tests/LizTerm.App.Tests/HostFiles/FakeHostFileServiceTests.cs`:

```csharp
    [Fact]
    public async Task A_dataset_listing_is_narrowed_by_its_pattern()
    {
        var token = TestContext.Current.CancellationToken;
        var host = new FakeHostFileService();
        host.AddDataset("A.CNTL");
        host.AddDataset("A.JCL");
        host.AddDataset("A.X.CNTL");
        host.AddDataset("B.CNTL");

        Assert.Equal(new[] { "A.CNTL", "A.JCL", "A.X.CNTL" }, (await host.ListDatasetsAsync("A.**", HostListRequest.All, token)).Entries.Select(e => e.Name));
        Assert.Equal(new[] { "A.CNTL", "A.JCL" }, (await host.ListDatasetsAsync("a.*", HostListRequest.All, token)).Entries.Select(e => e.Name));
        Assert.Equal(new[] { "A.CNTL", "B.CNTL" }, (await host.ListDatasetsAsync("%.CNTL", HostListRequest.All, token)).Entries.Select(e => e.Name));
        Assert.Empty((await host.ListDatasetsAsync("OTHER.**", HostListRequest.All, token)).Entries);
    }
```

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~FakeHostFileServiceTests.A_dataset_listing_is_narrowed"`
Expected: FAIL, the first listing returns all four.

- [ ] **Step 7: The fake narrows by the dataset pattern**

In `tests/LizTerm.App.Tests/Fakes/FakeHostFileService.cs`, `ListDatasetsAsync`, replace
`lock (_lock) return PageOf(Datasets, request with { NamePattern = null });` with
`lock (_lock) return PageOf(Datasets.Where(DatasetPattern(pattern).IsMatch), request with { NamePattern = null });`
where `DatasetPattern` is a new private static method beside `PageOf`:

```csharp
    /// <summary>A dataset pattern as the host reads it: <c>**</c> any run of qualifiers, <c>*</c> any run within a
    /// qualifier, <c>%</c> one character; case ignored.</summary>
    private static Func<HostFileEntry, bool> DatasetPattern(string pattern)
    {
        var regex = new Regex("^" + Regex.Escape(pattern.Trim().ToUpperInvariant())
            .Replace("\\*\\*", "").Replace("\\*", "[^.]*").Replace("", ".*").Replace("%", "[^.]") + "$");
        return entry => regex.IsMatch(entry.Name);
    }
```

Update the class summary's first sentence to say `list:<pattern>` returns the datasets the pattern matches.

- [ ] **Step 8: Run the fake tests and the whole App suite**

Run: `dotnet test tests/LizTerm.App.Tests`
Expected: all pass, `A_dataset_listing_is_narrowed_by_its_pattern` included.

- [ ] **Step 9: Commit**

```bash
git add src/LizTerm.App/HostFiles/EtagMemory.cs src/LizTerm.App/HostFiles/HostFileAccess.cs tests/LizTerm.App.Tests/HostFiles/EtagMemoryTests.cs tests/LizTerm.App.Tests/Fakes/FakeHostFileService.cs tests/LizTerm.App.Tests/HostFiles/FakeHostFileServiceTests.cs
git commit -m "Browser: a memory of the stamps this session downloaded or wrote; the fake narrows dataset listings

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 2: A question with a text box

**Files:**
- Modify: `src/LizTerm.App/ViewModels/ConfirmationRequest.cs` (whole file below)
- Modify: `src/LizTerm.App/Views/MvsmfBrowserWindow.axaml` (the `ConfirmationStrip` border)
- Modify: `src/LizTerm.App/Views/MvsmfBrowserWindow.axaml.cs` (`OnViewModelPropertyChanged`, `OnKeyDownTunnel`)
- Test: `tests/LizTerm.App.Tests/ViewModels/ConfirmationRequestTests.cs` (new), `tests/LizTerm.App.Tests/Views/MvsmfBrowserWindowTests.cs` (two tests)

**Interfaces:**
- Consumes: nothing new.
- Produces: `ConfirmationRequest(string message, string primaryLabel, string? secondaryLabel = null, bool offersApplyToAll = false, string? input = null, Func<string, string?>? inputRule = null, string inputLabel = "")`; `bool HasInput`; `string Input` (observable, prefilled with `input`); `string InputLabel`; `string? InputProblem` (the rule's sentence, without a mark); `bool CanAnswerPrimary`; `PrimaryCommand` refuses while `CanAnswerPrimary` is false. Existing callers (`new ConfirmationRequest(message, label)`, `new ConfirmationRequest(message, "Replace", "Skip", offersApplyToAll: true)`) compile unchanged. The window's text box is `ConfirmInputBox`; the problem line is `ConfirmInputProblem`; the label is `ConfirmInputLabel`.

- [ ] **Step 1: Write the failing view-model tests**

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.ComponentModel;
using LizTerm.App.ViewModels;
using LizTerm.Core.HostFiles;

namespace LizTerm.App.Tests.ViewModels;

public class ConfirmationRequestTests
{
    [Fact]
    public void A_plain_question_has_no_input_and_its_primary_is_always_allowed()
    {
        var question = new ConfirmationRequest("Delete HELLO from MVSCE02.CNTL? This cannot be undone.", "Delete 1 member");

        Assert.False(question.HasInput);
        Assert.Equal("", question.Input);
        Assert.Null(question.InputProblem);
        Assert.True(question.CanAnswerPrimary);
        Assert.True(question.PrimaryCommand.CanExecute(null));

        question.PrimaryCommand.Execute(null);

        Assert.Equal(ConfirmChoice.Primary, question.Answer.Result.Choice);
    }

    [Fact]
    public void An_input_question_starts_prefilled_and_cannot_be_answered_until_the_name_changes()
    {
        var question = new ConfirmationRequest("Rename HELLO in MVSCE02.CNTL to:", "Rename",
            input: "HELLO", inputRule: HostPath.MemberNameError);

        Assert.True(question.HasInput);
        Assert.Equal("HELLO", question.Input);
        Assert.Equal("", question.InputLabel);
        Assert.Null(question.InputProblem);
        Assert.False(question.CanAnswerPrimary);
        Assert.False(question.PrimaryCommand.CanExecute(null));

        question.Input = " hello ";
        Assert.False(question.CanAnswerPrimary);

        question.Input = "HELLO2";
        Assert.True(question.CanAnswerPrimary);
        Assert.True(question.PrimaryCommand.CanExecute(null));
    }

    [Fact]
    public void A_name_the_rule_refuses_is_the_problem_and_blocks_the_primary()
    {
        var question = new ConfirmationRequest("Rename HELLO in MVSCE02.CNTL to:", "Rename",
            input: "HELLO", inputRule: HostPath.MemberNameError);

        question.Input = "TOO-LONG-NAME";

        Assert.Equal("A member name is at most 8 characters.", question.InputProblem);
        Assert.False(question.CanAnswerPrimary);

        question.Input = "";
        Assert.Equal("Enter a member name.", question.InputProblem);
    }

    [Fact]
    public void The_primary_is_refused_while_it_cannot_be_answered()
    {
        var question = new ConfirmationRequest("Rename HELLO in MVSCE02.CNTL to:", "Rename",
            input: "HELLO", inputRule: HostPath.MemberNameError);

        question.PrimaryCommand.Execute(null);
        Assert.False(question.Answer.IsCompleted);

        question.Input = "HELLO2";
        question.PrimaryCommand.Execute(null);
        Assert.Equal(ConfirmChoice.Primary, question.Answer.Result.Choice);
        Assert.Equal("HELLO2", question.Input);
    }

    [Fact]
    public void Cancel_does_not_need_a_changed_name()
    {
        var cancelled = new ConfirmationRequest("Rename HELLO in MVSCE02.CNTL to:", "Rename",
            input: "HELLO", inputRule: HostPath.MemberNameError);
        cancelled.CancelCommand.Execute(null);
        Assert.Equal(ConfirmChoice.Cancel, cancelled.Answer.Result.Choice);
    }

    [Fact]
    public void Typing_notifies_the_problem_and_the_primary()
    {
        var question = new ConfirmationRequest("Rename HELLO in MVSCE02.CNTL to:", "Rename",
            input: "HELLO", inputRule: HostPath.MemberNameError);
        var changed = new List<string?>();
        question.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        var canExecuteChanged = 0;
        question.PrimaryCommand.CanExecuteChanged += (_, _) => canExecuteChanged++;

        question.Input = "HELLO2";

        Assert.Contains(nameof(ConfirmationRequest.InputProblem), changed);
        Assert.Contains(nameof(ConfirmationRequest.CanAnswerPrimary), changed);
        Assert.Equal(1, canExecuteChanged);
    }
}
```

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~ConfirmationRequestTests"`
Expected: build error (no `input` parameter, no `HasInput`).

- [ ] **Step 3: Rewrite `ConfirmationRequest`**

Replace the whole of `src/LizTerm.App/ViewModels/ConfirmationRequest.cs` (keep the licence header) with:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace LizTerm.App.ViewModels;

public enum ConfirmChoice { Cancel, Primary, Secondary }

public sealed record ConfirmOutcome(ConfirmChoice Choice, bool ApplyToAll);

/// <summary>A question an inline confirmation strip is waiting on — Manage Tags' pattern, but awaited by the
/// operation that asked. The first answer wins. With <paramref name="input"/> the strip shows a text box (a
/// rename): prefilled, checked by <paramref name="inputRule"/> (null for an acceptable value), and the primary is
/// allowed only for an acceptable value that differs from the original, ignoring case and surrounding blanks. The
/// asker reads the answer from <see cref="Input"/>.</summary>
public sealed partial class ConfirmationRequest(string message, string primaryLabel, string? secondaryLabel = null,
    bool offersApplyToAll = false, string? input = null, Func<string, string?>? inputRule = null, string inputLabel = "")
    : ObservableObject
{
    private readonly TaskCompletionSource<ConfirmOutcome> _answer = new();
    private readonly string _original = (input ?? "").Trim();

    public string Message { get; } = message;
    public string PrimaryLabel { get; } = primaryLabel;
    public string? SecondaryLabel { get; } = secondaryLabel;
    public bool HasSecondary => SecondaryLabel is not null;
    public bool OffersApplyToAll { get; } = offersApplyToAll;
    public bool HasInput { get; } = input is not null;
    public string InputLabel { get; } = inputLabel;

    [ObservableProperty] private bool _applyToAll;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InputProblem), nameof(CanAnswerPrimary))]
    [NotifyCanExecuteChangedFor(nameof(PrimaryCommand))]
    private string _input = input ?? "";

    /// <summary>Why the input is not acceptable, in the asker's words, or null.</summary>
    public string? InputProblem => HasInput ? inputRule?.Invoke(Input) : null;

    /// <summary>A plain question can always be answered; an input question needs an acceptable, changed value.</summary>
    public bool CanAnswerPrimary =>
        !HasInput || (InputProblem is null && !string.Equals(Input.Trim(), _original, StringComparison.OrdinalIgnoreCase));

    public Task<ConfirmOutcome> Answer => _answer.Task;

    [RelayCommand(CanExecute = nameof(CanAnswerPrimary))]
    private void Primary()
    {
        // Enter in the text box reaches here through Execute, which does not consult CanExecute.
        if (CanAnswerPrimary) _answer.TrySetResult(new ConfirmOutcome(ConfirmChoice.Primary, ApplyToAll));
    }

    [RelayCommand]
    private void Secondary() => _answer.TrySetResult(new ConfirmOutcome(ConfirmChoice.Secondary, ApplyToAll));

    [RelayCommand]
    private void Cancel() => _answer.TrySetResult(new ConfirmOutcome(ConfirmChoice.Cancel, false));
}
```

- [ ] **Step 4: Run the view-model tests to verify they pass**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~ConfirmationRequestTests"`
Expected: 6 passed. Then `dotnet test tests/LizTerm.App.Tests` to confirm nothing else moved (the existing delete, upload and download questions still work).

- [ ] **Step 5: Write the failing window tests**

Add to `tests/LizTerm.App.Tests/Views/MvsmfBrowserWindowTests.cs` (it already imports `LizTerm.App.ViewModels`; add `using LizTerm.Core.HostFiles;`):

```csharp
    [AvaloniaFact]
    public async Task An_input_question_shows_a_text_box_focused_and_selected_and_enter_answers_it()
    {
        var (window, t) = Show(userid: null);
        var question = new ConfirmationRequest("Rename HELLO in MVSCE02.CNTL to:", "Rename",
            input: "HELLO", inputRule: HostPath.MemberNameError);

        t.Vm.Confirmation = question;

        var box = Named<TextBox>(window, "ConfirmInputBox");
        await Wait.UntilAsync(() => box.IsFocused, "the focus in the text box");
        Assert.True(box.IsVisible);
        Assert.Equal("HELLO", box.Text);
        Assert.Equal("HELLO", box.SelectedText);
        Assert.False(Named<TextBlock>(window, "ConfirmInputLabel").IsVisible);
        Assert.False(Named<TextBlock>(window, "ConfirmInputProblem").IsVisible);
        Assert.False(Named<Button>(window, "ConfirmPrimaryButton").IsEffectivelyEnabled);

        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        Assert.False(question.Answer.IsCompleted);

        box.Text = "BAD-NAME";
        Dispatcher.UIThread.RunJobs();
        Assert.True(Named<TextBlock>(window, "ConfirmInputProblem").IsVisible);
        Assert.Equal("✗ A member name cannot contain '-'.", Named<TextBlock>(window, "ConfirmInputProblem").Text);

        box.Text = "HELLO2";
        Dispatcher.UIThread.RunJobs();
        Assert.True(Named<Button>(window, "ConfirmPrimaryButton").IsEffectivelyEnabled);
        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);

        Assert.Equal(ConfirmChoice.Primary, (await question.Answer).Choice);
        Assert.Equal("HELLO2", question.Input);
        t.Vm.Confirmation = null;
    }

    [AvaloniaFact]
    public async Task A_plain_question_hides_the_text_box()
    {
        var (window, t) = Show(userid: null);

        t.Vm.Confirmation = new ConfirmationRequest("Delete HELLO from MVSCE02.CNTL? This cannot be undone.", "Delete 1 member");

        await Wait.UntilAsync(() => Named<Button>(window, "ConfirmCancelButton").IsFocused, "the focus on Cancel");
        Assert.False(Named<TextBox>(window, "ConfirmInputBox").IsVisible);
        t.Vm.Confirmation = null;
    }
```

- [ ] **Step 6: Run them to verify they fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~MvsmfBrowserWindowTests.An_input_question|FullyQualifiedName~MvsmfBrowserWindowTests.A_plain_question_hides"`
Expected: FAIL, `ConfirmInputBox` not found (`Named` returns null and throws).

- [ ] **Step 7: Add the text box to the strip**

In `src/LizTerm.App/Views/MvsmfBrowserWindow.axaml`, replace the `ConfirmationStrip` border's inner `DockPanel` with:

```xml
      <DockPanel>
        <StackPanel DockPanel.Dock="Right" Orientation="Horizontal" Spacing="8" VerticalAlignment="Top">
          <CheckBox x:Name="ApplyToAllBox" Content="Apply to all" IsChecked="{Binding Confirmation.ApplyToAll}"
                    IsVisible="{Binding Confirmation.OffersApplyToAll, FallbackValue=False}" />
          <Button x:Name="ConfirmCancelButton" Content="Cancel" Command="{Binding Confirmation.CancelCommand}" />
          <Button x:Name="ConfirmSecondaryButton" Content="{Binding Confirmation.SecondaryLabel}"
                  Command="{Binding Confirmation.SecondaryCommand}"
                  IsVisible="{Binding Confirmation.HasSecondary, FallbackValue=False}" />
          <Button x:Name="ConfirmPrimaryButton" Content="{Binding Confirmation.PrimaryLabel}"
                  Command="{Binding Confirmation.PrimaryCommand}" />
        </StackPanel>
        <StackPanel Spacing="4" Margin="0,0,8,0">
          <DockPanel>
            <TextBox x:Name="ConfirmInputBox" DockPanel.Dock="Right" Width="240" MaxLength="44"
                     FontFamily="Menlo, Consolas, monospace" Text="{Binding Confirmation.Input}"
                     IsVisible="{Binding Confirmation.HasInput, FallbackValue=False}" Margin="8,0,0,0" />
            <TextBlock x:Name="ConfirmInputLabel" DockPanel.Dock="Right" Text="{Binding Confirmation.InputLabel}"
                       VerticalAlignment="Center" Margin="8,0,0,0"
                       IsVisible="{Binding Confirmation.InputLabel, Converter={x:Static StringConverters.IsNotNullOrEmpty}, FallbackValue=False}" />
            <TextBlock Text="{Binding Confirmation.Message}" TextWrapping="Wrap" VerticalAlignment="Center" />
          </DockPanel>
          <TextBlock x:Name="ConfirmInputProblem" Text="{Binding Confirmation.InputProblem, StringFormat='✗ {0}'}"
                     Foreground="#FF8080" TextWrapping="Wrap"
                     IsVisible="{Binding Confirmation.InputProblem, Converter={x:Static ObjectConverters.IsNotNull}, FallbackValue=False}" />
        </StackPanel>
      </DockPanel>
```

- [ ] **Step 8: Focus the box and answer on Enter**

In `src/LizTerm.App/Views/MvsmfBrowserWindow.axaml.cs`, replace the `HasConfirmation when vm.HasConfirmation` case of `OnViewModelPropertyChanged`:

```csharp
            case nameof(MvsmfBrowserViewModel.HasConfirmation) when vm.HasConfirmation:
                // An input question takes the keyboard to its box, with the old name selected so typing replaces it;
                // any other to Cancel, the safe answer.
                Dispatcher.UIThread.Post(() =>
                {
                    if (_watched is not { HasConfirmation: true, Confirmation: { } question }) return;
                    if (question.HasInput)
                    {
                        ConfirmInputBox.Focus();
                        ConfirmInputBox.SelectAll();
                    }
                    else ConfirmCancelButton.Focus();
                }, DispatcherPriority.Loaded);
                break;
```

And add, as the first `Key.Enter` case in `OnKeyDownTunnel` (before `Key.Enter when FilterBox.IsFocused`):

```csharp
            case Key.Enter when vm.Confirmation is { HasInput: true } question && ConfirmInputBox.IsKeyboardFocusWithin:
                e.Handled = true;
                if (question.PrimaryCommand.CanExecute(null)) question.PrimaryCommand.Execute(null);
                break;
```

Update the method's summary comment: "A question takes the keyboard to its Cancel button, the safe answer, or to its text box when it has one, …".

- [ ] **Step 9: Run the window tests and the whole App suite**

Run: `dotnet test tests/LizTerm.App.Tests`
Expected: all pass, including the existing `A_question_takes_the_focus_to_its_cancel_button`.

- [ ] **Step 10: Commit**

```bash
git add src/LizTerm.App/ViewModels/ConfirmationRequest.cs src/LizTerm.App/Views/MvsmfBrowserWindow.axaml src/LizTerm.App/Views/MvsmfBrowserWindow.axaml.cs tests/LizTerm.App.Tests/ViewModels/ConfirmationRequestTests.cs tests/LizTerm.App.Tests/Views/MvsmfBrowserWindowTests.cs
git commit -m "Browser: a question can carry a text box, for a rename

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 3: The stamp through download, upload and delete

**Files:**
- Modify: `src/LizTerm.App/ViewModels/MvsmfBrowserViewModel.Downloads.cs` (`DownloadOneAsync`)
- Modify: `src/LizTerm.App/ViewModels/MvsmfBrowserViewModel.Uploads.cs` (`StartUploadCoreAsync`'s send loop, `UploadSequentialAsync`)
- Modify: `src/LizTerm.App/ViewModels/MvsmfBrowserViewModel.Delete.cs` (`DeleteCoreAsync`)
- Test: `tests/LizTerm.App.Tests/ViewModels/MvsmfBrowserEtagTests.cs` (new)

**Interfaces:**
- Consumes: `_access.Etags` (Task 1); `HostFileTransfer.DownloadAsync` → `DownloadResult(BytesWritten, Etag)`; `HostFileTransfer.UploadTextAsync(service, path, checkedText, verify, string? ifMatch = null, CancellationToken cancellationToken = default)` → `UploadOutcome(Verification, DiffersAtLine, Etag)`; `HostFileTransfer.UploadBinaryAsync(service, path, sourceFile, string? ifMatch = null, CancellationToken cancellationToken = default)` → `Task<string?>`; `HostFileErrorKind.Conflict`.
- Produces: the conflict question `NAME changed on the host since you downloaded it.` with `Replace anyway` / `Skip` (no Apply to all); the row status `– Skipped: changed on the host`; for a sequential dataset the same question with `Replace anyway` only.

- [ ] **Step 1: Write the failing tests**

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.Core.HostFiles;

namespace LizTerm.App.Tests.ViewModels;

/// <summary>Spec §5: a member downloaded or written in this window is replaced only if it has not changed on the
/// host since; anything else gets today's behaviour.</summary>
public sealed class MvsmfBrowserEtagTests : IDisposable
{
    private static readonly HostPath Hello = HostPath.ForMember("MVSCE02.CNTL", "HELLO");
    private readonly string _folder = Directory.CreateTempSubdirectory("lizterm-etag-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private string Local(string name) => Path.Combine(_folder, name);

    private string Write(string name, string text)
    {
        var path = Local(name);
        File.WriteAllText(path, text);
        return path;
    }

    /// <summary>CNTL with HELLO's text and a host stamp on it.</summary>
    private static async Task<BrowserTestHost> ChosenAsync()
    {
        var t = BrowserTestHost.Create();
        t.Host.Text["MVSCE02.CNTL(HELLO)"] = ["old"];
        t.Host.Etags["MVSCE02.CNTL(HELLO)"] = "seed-1";
        t.Host.Binary["MVSCE02.UFSHOME"] = [1, 2, 3];
        t.Host.Etags["MVSCE02.UFSHOME"] = "seed-2";
        await t.ChooseAsync("MVSCE02.CNTL");
        return t;
    }

    private async Task<BrowserTestHost> DownloadedAsync()
    {
        var t = await ChosenAsync();
        t.Select("HELLO");
        t.Picker.Result = Local("hello.txt");
        await t.Vm.DownloadCommand.ExecuteAsync(null);
        Assert.Equal("seed-1", t.Access.Etags.TryGet(Hello));
        return t;
    }

    private Task<BrowserTestHost> ReviewingAsync(BrowserTestHost t, string file) => ReviewingAsync(t, [file]);

    private static async Task<BrowserTestHost> ReviewingAsync(BrowserTestHost t, string[] files)
    {
        t.Picker.Results = files;
        await t.Vm.UploadCommand.ExecuteAsync(null);
        return t;
    }

    private static async Task<ConfirmationRequest> AskedAsync(BrowserTestHost t, string message)
    {
        await Wait.UntilAsync(() => t.Vm.Confirmation is { } q && q.Message == message, $"the question {message}");
        return t.Vm.Confirmation!;
    }

    [Fact]
    public async Task A_download_remembers_the_members_stamp()
    {
        var t = await DownloadedAsync();

        Assert.Equal("seed-1", t.Access.Etags.TryGet(Hello));
        Assert.Equal(1, t.Access.Etags.Count);
    }

    [Fact]
    public async Task A_failed_download_remembers_nothing()
    {
        var t = await ChosenAsync();
        t.Select("HELLO");
        t.Picker.Result = Local("hello.txt");
        t.Host.Failures["readtext:MVSCE02.CNTL(HELLO)"] = new HostFileException(HostFileErrorKind.ServerError, "x", 3);

        await t.Vm.DownloadCommand.ExecuteAsync(null);

        Assert.Null(t.Access.Etags.TryGet(Hello));
    }

    [Fact]
    public async Task Replacing_a_downloaded_member_sends_its_stamp_and_keeps_the_new_one()
    {
        var t = await ReviewingAsync(await DownloadedAsync(), Write("hello.jcl", "new\n"));

        var upload = t.Vm.StartUploadCommand.ExecuteAsync(null);
        (await AskedAsync(t, "Member HELLO already exists in MVSCE02.CNTL.")).PrimaryCommand.Execute(null);
        await upload;

        Assert.Equal(new[] { "seed-1" }, t.Host.IfMatches);
        Assert.Equal("✓ Uploaded and verified", t.Vm.Uploads[0].Status);
        Assert.Equal(t.Host.Etags["MVSCE02.CNTL(HELLO)"], t.Access.Etags.TryGet(Hello));
        Assert.StartsWith("stamp-", t.Access.Etags.TryGet(Hello));
    }

    [Fact]
    public async Task A_new_member_sends_no_stamp()
    {
        var t = await ReviewingAsync(await DownloadedAsync(), Write("newmem.jcl", "x\n"));

        await t.Vm.StartUploadCommand.ExecuteAsync(null);

        Assert.Equal(new string?[] { null }, t.Host.IfMatches);
        Assert.Equal(t.Host.Etags["MVSCE02.CNTL(NEWMEM)"], t.Access.Etags.TryGet(HostPath.ForMember("MVSCE02.CNTL", "NEWMEM")));
    }

    [Fact]
    public async Task A_member_never_downloaded_is_replaced_without_a_stamp()
    {
        var t = await ReviewingAsync(await ChosenAsync(), Write("hello.jcl", "new\n"));

        var upload = t.Vm.StartUploadCommand.ExecuteAsync(null);
        (await AskedAsync(t, "Member HELLO already exists in MVSCE02.CNTL.")).PrimaryCommand.Execute(null);
        await upload;

        Assert.Equal(new string?[] { null }, t.Host.IfMatches);
        Assert.Equal(new[] { "new" }, t.Host.Text["MVSCE02.CNTL(HELLO)"]);
    }

    [Fact]
    public async Task A_change_since_the_download_asks_and_replace_anyway_sends_again_without_a_stamp()
    {
        var t = await ReviewingAsync(await DownloadedAsync(), Write("hello.jcl", "new\n"));
        t.Host.Etags["MVSCE02.CNTL(HELLO)"] = "seed-9";

        var upload = t.Vm.StartUploadCommand.ExecuteAsync(null);
        (await AskedAsync(t, "Member HELLO already exists in MVSCE02.CNTL.")).PrimaryCommand.Execute(null);
        var conflict = await AskedAsync(t, "HELLO changed on the host since you downloaded it.");
        Assert.Equal(("Replace anyway", "Skip", false), (conflict.PrimaryLabel, conflict.SecondaryLabel, conflict.OffersApplyToAll));
        conflict.PrimaryCommand.Execute(null);
        await upload;

        Assert.Equal(new string?[] { "seed-1", null }, t.Host.IfMatches);
        Assert.Equal(new[] { "new" }, t.Host.Text["MVSCE02.CNTL(HELLO)"]);
        Assert.Equal("✓ Uploaded and verified", t.Vm.Uploads[0].Status);
        Assert.Equal(t.Host.Etags["MVSCE02.CNTL(HELLO)"], t.Access.Etags.TryGet(Hello));
        Assert.Equal("✓ Uploaded 1 of 1 file to MVSCE02.CNTL.", t.Vm.StatusText);
    }

    [Fact]
    public async Task Skip_leaves_the_changed_member_alone()
    {
        var t = await ReviewingAsync(await DownloadedAsync(), [Write("hello.jcl", "new\n"), Write("other.jcl", "y\n")]);
        t.Host.Etags["MVSCE02.CNTL(HELLO)"] = "seed-9";

        var upload = t.Vm.StartUploadCommand.ExecuteAsync(null);
        (await AskedAsync(t, "Member HELLO already exists in MVSCE02.CNTL.")).PrimaryCommand.Execute(null);
        (await AskedAsync(t, "HELLO changed on the host since you downloaded it.")).SecondaryCommand.Execute(null);
        await upload;

        Assert.Equal("– Skipped: changed on the host", t.Vm.Uploads[0].Status);
        Assert.Equal(new[] { "old" }, t.Host.Text["MVSCE02.CNTL(HELLO)"]);
        Assert.Equal("seed-1", t.Access.Etags.TryGet(Hello));
        Assert.Equal("✓ Uploaded and verified", t.Vm.Uploads[1].Status);
        Assert.Equal("⚠ Uploaded 1 of 2 files to MVSCE02.CNTL.", t.Vm.StatusText);
    }

    [Fact]
    public async Task Cancel_on_the_conflict_stops_the_batch()
    {
        var t = await ReviewingAsync(await DownloadedAsync(), [Write("hello.jcl", "new\n"), Write("other.jcl", "y\n")]);
        t.Host.Etags["MVSCE02.CNTL(HELLO)"] = "seed-9";

        var upload = t.Vm.StartUploadCommand.ExecuteAsync(null);
        (await AskedAsync(t, "Member HELLO already exists in MVSCE02.CNTL.")).PrimaryCommand.Execute(null);
        (await AskedAsync(t, "HELLO changed on the host since you downloaded it.")).CancelCommand.Execute(null);
        await upload;

        Assert.Equal("– Cancelled", t.Vm.Uploads[0].Status);
        Assert.Equal("– Cancelled", t.Vm.Uploads[1].Status);
        Assert.DoesNotContain("writetext:MVSCE02.CNTL(OTHER):1", t.Host.CallsSnapshot());
        Assert.Equal("– Upload cancelled.", t.Vm.StatusText);
        Assert.True(t.Vm.UploadFinished);
    }

    [Fact]
    public async Task A_sequential_dataset_is_checked_the_same_way()
    {
        var t = await ChosenAsync();
        await t.ChooseAsync("MVSCE02.UFSHOME");
        t.Picker.Result = Local("UFSHOME");
        await t.Vm.DownloadCommand.ExecuteAsync(null);
        Assert.Equal("seed-2", t.Access.Etags.TryGet(HostPath.ForDataset("MVSCE02.UFSHOME")));
        t.Host.Etags["MVSCE02.UFSHOME"] = "seed-9";
        t.Picker.Result = Write("new.bin", "abc");

        var upload = t.Vm.UploadCommand.ExecuteAsync(null);
        (await AskedAsync(t, "Replace the contents of MVSCE02.UFSHOME with new.bin?")).PrimaryCommand.Execute(null);
        var conflict = await AskedAsync(t, "MVSCE02.UFSHOME changed on the host since you downloaded it.");
        Assert.Equal(("Replace anyway", false), (conflict.PrimaryLabel, conflict.HasSecondary));
        conflict.PrimaryCommand.Execute(null);
        await upload;

        Assert.Equal(new string?[] { "seed-2", null }, t.Host.IfMatches);
        Assert.Equal("✓ Uploaded new.bin to MVSCE02.UFSHOME.", t.Vm.StatusText);
        Assert.Equal(t.Host.Etags["MVSCE02.UFSHOME"], t.Access.Etags.TryGet(HostPath.ForDataset("MVSCE02.UFSHOME")));
    }

    [Fact]
    public async Task Cancel_on_a_sequential_conflict_sends_nothing_more()
    {
        var t = await ChosenAsync();
        await t.ChooseAsync("MVSCE02.UFSHOME");
        t.Picker.Result = Local("UFSHOME");
        await t.Vm.DownloadCommand.ExecuteAsync(null);
        t.Host.Etags["MVSCE02.UFSHOME"] = "seed-9";
        t.Picker.Result = Write("new.bin", "abc");

        var upload = t.Vm.UploadCommand.ExecuteAsync(null);
        (await AskedAsync(t, "Replace the contents of MVSCE02.UFSHOME with new.bin?")).PrimaryCommand.Execute(null);
        (await AskedAsync(t, "MVSCE02.UFSHOME changed on the host since you downloaded it.")).CancelCommand.Execute(null);
        await upload;

        Assert.Equal(new string?[] { "seed-2" }, t.Host.IfMatches);
        Assert.Equal(new byte[] { 1, 2, 3 }, t.Host.Binary["MVSCE02.UFSHOME"]);
        Assert.Equal("– Upload cancelled.", t.Vm.StatusText);
    }

    [Fact]
    public async Task Deleting_a_member_forgets_its_stamp()
    {
        var t = await DownloadedAsync();
        t.Select("HELLO");

        var deleting = t.Vm.DeleteCommand.ExecuteAsync(null);
        (await AskedAsync(t, "Delete HELLO from MVSCE02.CNTL? This cannot be undone.")).PrimaryCommand.Execute(null);
        await deleting;

        Assert.Null(t.Access.Etags.TryGet(Hello));
    }

    [Fact]
    public async Task A_delete_that_fails_keeps_the_stamp()
    {
        var t = await DownloadedAsync();
        t.Select("HELLO");
        t.Host.Failures["delete:MVSCE02.CNTL(HELLO)"] = new HostFileException(HostFileErrorKind.NotAuthorized, "x", 6);

        var deleting = t.Vm.DeleteCommand.ExecuteAsync(null);
        (await AskedAsync(t, "Delete HELLO from MVSCE02.CNTL? This cannot be undone.")).PrimaryCommand.Execute(null);
        await deleting;

        Assert.Equal("seed-1", t.Access.Etags.TryGet(Hello));
    }
}
```

Note: `BrowserTestHost` is a record with an `Access` member, so `t.Access.Etags` works once Task 1 is in. The `AskedAsync` helper waits for a question **with the given message**, so the two questions of a conflict test are told apart. The sequential test's download suggests `UFSHOME` as the file name (the last qualifier, no extension in binary mode), which `Local("UFSHOME")` answers.

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~MvsmfBrowserEtagTests"`
Expected: `A_download_remembers_the_members_stamp` fails (memory empty); the upload tests fail on `IfMatches` (nothing sent) or time out waiting for the conflict question; the delete tests: `Deleting_a_member_forgets_its_stamp` fails.

- [ ] **Step 3: Downloads remember**

In `src/LizTerm.App/ViewModels/MvsmfBrowserViewModel.Downloads.cs`, `DownloadOneAsync`, after `var result = await _connection.RunAsync(...)`:

```csharp
            // The file is in place, so this is the copy the stamp describes. A cancelled or failed download remembers
            // nothing (spec §5.2).
            _access.Etags.Remember(path, result.Etag);
```

- [ ] **Step 4: The PDS upload sends the stamp and asks on a conflict**

In `src/LizTerm.App/ViewModels/MvsmfBrowserViewModel.Uploads.cs`, `StartUploadCoreAsync`: change `var plan = new List<UploadRow>();` to `var plan = new List<(UploadRow Row, bool Replaces)>();`, and `plan.Add(row);` to `plan.Add((row, existing.Contains(row.UploadName)));`. Then replace everything from `var sent = Uploads.Count - pending.Count;` to the end of the method with:

```csharp
        var sent = Uploads.Count - pending.Count;
        var cancelled = false;
        foreach (var (row, replaces) in plan)
        {
            if (token.IsCancellationRequested || cancelled)
            {
                row.Status = "– Cancelled";
                continue;
            }
            row.Status = "⟳ Sending";
            var started = false;
            var path = dataset.Path.WithMember(row.UploadName);
            // Only a member being replaced is checked against its stamp (spec §5.2); a member this window never
            // downloaded or wrote has none, and is replaced as before.
            var ifMatch = replaces ? _access.Etags.TryGet(path) : null;

            async Task SendAsync(string? stamp)
            {
                if (mode == HostTransferMode.Text)
                {
                    var check = row.Check!;
                    var outcome = await _connection.RunAsync(service =>
                    {
                        started = true;
                        return HostFileTransfer.UploadTextAsync(service, path, check, verify, stamp, token);
                    });
                    _access.Etags.Remember(path, outcome.Etag);
                    row.Status = Describe(outcome);
                    row.HostCopyDiffers = outcome.Verification == UploadVerification.Differs;
                }
                else
                {
                    var etag = await _connection.RunAsync(service =>
                    {
                        started = true;
                        return HostFileTransfer.UploadBinaryAsync(service, path, row.LocalPath, stamp, token);
                    });
                    _access.Etags.Remember(path, etag);
                    row.Status = "✓ Uploaded";
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
                    // The host checks the stamp before it writes, so nothing was written. Asked per member, with no
                    // Apply to all: a conflict is rare (spec §5.3).
                    var answer = await AskAsync(new ConfirmationRequest(
                        $"{row.UploadName} changed on the host since you downloaded it.", "Replace anyway", "Skip"));
                    switch (answer.Choice)
                    {
                        case ConfirmChoice.Primary:
                            await SendAsync(null);
                            break;
                        case ConfirmChoice.Secondary:
                            row.Status = "– Skipped: changed on the host";
                            continue;
                        default:
                            cancelled = true;
                            row.Status = "– Cancelled";
                            continue;
                    }
                }
                row.Sent = true;
                sent++;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                // A write the cancel interrupted may have reached the host in part.
                row.Status = started ? "– Cancelled: the member may be partly written" : "– Cancelled";
            }
            catch (HostFileException ex) when (IsConnectionFailure(ex))
            {
                // Stop here and let the banner offer Retry. The review stays open and unfinished, so Retry (or Start)
                // can run it again; rows already sent are not sent again.
                foreach (var each in plan.SkipWhile(each => !ReferenceEquals(each.Row, row))) each.Row.Status = "– Stopped";
                row.Status = "– Stopped: the member may be partly written";
                _uploadStoppedMidWrite = true;
                throw;
            }
            catch (Exception ex)
            {
                row.Status = "✗ Failed: " + HostFileMessages.DescribeUploadFailure(ex);
            }
        }

        UploadFinished = true;
        if (token.IsCancellationRequested || cancelled)
        {
            StatusText = "– Upload cancelled.";
            return;
        }
        await LoadMembersCoreAsync(dataset, token);
        var clean = sent == Uploads.Count && !Uploads.Any(row => row.HostCopyDiffers);
        StatusText = $"{(clean ? "✓" : "⚠")} Uploaded {sent} of {Plural(Uploads.Count, "file")} to {dataset.Name}.";
    }
```

(`continue` out of a `catch` block is legal C#; the second `SendAsync` runs inside the catch, so its own failures still reach the outer `catch` clauses.)

- [ ] **Step 5: The sequential upload does the same**

In the same file, `UploadSequentialAsync`, replace the final `try { … } catch (Exception ex) when (…) { … }` block with:

```csharp
        async Task SendAsync(string? stamp)
        {
            if (check is not null)
            {
                var outcome = await _connection.RunAsync(service => HostFileTransfer.UploadTextAsync(service, dataset.Path, check, verify, stamp, token));
                _access.Etags.Remember(dataset.Path, outcome.Etag);
                StatusText = outcome.Verification == UploadVerification.Differs
                    ? $"{Describe(outcome)} — {dataset.Name}"
                    : $"✓ Uploaded {name} to {dataset.Name}.";
            }
            else
            {
                var etag = await _connection.RunAsync(service => HostFileTransfer.UploadBinaryAsync(service, dataset.Path, file, stamp, token));
                _access.Etags.Remember(dataset.Path, etag);
                StatusText = $"✓ Uploaded {name} to {dataset.Name}.";
            }
        }

        try
        {
            try
            {
                await SendAsync(_access.Etags.TryGet(dataset.Path));
            }
            catch (HostFileException ex) when (ex.Kind == HostFileErrorKind.Conflict)
            {
                // One file, one dataset: there is nothing to skip to, so the question offers Replace anyway or Cancel.
                var answer = await AskAsync(new ConfirmationRequest($"{dataset.Name} changed on the host since you downloaded it.", "Replace anyway"));
                if (answer.Choice != ConfirmChoice.Primary)
                {
                    StatusText = "– Upload cancelled.";
                    return;
                }
                await SendAsync(null);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException && !(ex is HostFileException host && IsConnectionFailure(host)))
        {
            StatusText = "✗ Failed: " + HostFileMessages.DescribeUploadFailure(ex, dataset: true);
        }
    }
```

- [ ] **Step 6: A member delete forgets, and the loop is guarded**

In `src/LizTerm.App/ViewModels/MvsmfBrowserViewModel.Delete.cs`, `DeleteCoreAsync`, replace the inner `try` of the loop body:

```csharp
                try
                {
                    // MemberRow.Path is built by HostPath.ForMember, so this can only be a member; the check stands
                    // because DeleteAsync now takes a dataset too, and a dataset must never go through this loop.
                    if (member.Path.Kind != HostPathKind.Member) throw new InvalidOperationException($"{member.Path} is not a member.");
                    await _connection.RunAsync(service => service.DeleteAsync(member.Path, token));
                    _access.Etags.Forget(member.Path);
                    deleted++;
                }
```

- [ ] **Step 7: Run the new tests, then the App suite**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~MvsmfBrowserEtagTests"`
Expected: 12 passed.
Run: `dotnet test tests/LizTerm.App.Tests`
Expected: all pass (`MvsmfBrowserUploadTests`, `MvsmfBrowserDownloadTests`, `MvsmfBrowserDeleteTests` unchanged in outcome).

- [ ] **Step 8: Commit**

```bash
git add src/LizTerm.App/ViewModels/MvsmfBrowserViewModel.Downloads.cs src/LizTerm.App/ViewModels/MvsmfBrowserViewModel.Uploads.cs src/LizTerm.App/ViewModels/MvsmfBrowserViewModel.Delete.cs tests/LizTerm.App.Tests/ViewModels/MvsmfBrowserEtagTests.cs
git commit -m "Browser: a replaced member is checked against the stamp of the copy downloaded here

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 4: Rename a member or a dataset, delete a dataset

**Files:**
- Create: `src/LizTerm.App/ViewModels/MvsmfBrowserViewModel.Manage.cs`
- Modify: `src/LizTerm.App/ViewModels/MvsmfBrowserViewModel.cs` (`NotifyCommands`)
- Modify: `src/LizTerm.App/Views/MvsmfBrowserWindow.axaml` (the dataset pane's button row, the bottom bar's Rename…)
- Modify: `src/LizTerm.App/Views/MvsmfBrowserWindow.axaml.cs` (the renamed member's selection)
- Test: `tests/LizTerm.App.Tests/ViewModels/MvsmfBrowserManageTests.cs` (new), `tests/LizTerm.App.Tests/Views/MvsmfBrowserWindowTests.cs` (two tests)

**Interfaces:**
- Consumes: `ConfirmationRequest` with input (Task 2); `_access.Etags.Move` / `ForgetUnder` (Task 1); `IHostFileService.RenameAsync(HostPath from, string newName, CancellationToken)`, `DeleteAsync(HostPath path, CancellationToken)`; `ListCoreAsync`, `LoadMembersCoreAsync`, `_listedPattern`, `_memberPattern`, `HasMoreMembers`, `Plural`.
- Produces: commands `RenameMemberCommand`, `RenameDatasetCommand`, `DeleteDatasetCommand` with `CanRenameMember`, `CanRenameDataset`, `CanDeleteDataset`; `event Action<MemberRow>? SelectMemberRequested`; helpers `SelectAsync(DatasetRow, CancellationToken)`, `ShowAfterChangeAsync(string name, string what, CancellationToken)`, `RunThenListAsync(work, retryAll)`, `ListAgainAsync(name, what)` for Task 6. Window controls `RenameDatasetButton`, `DeleteDatasetButton` in `DatasetButtons` (a `WrapPanel`), `RenameMemberButton`. Task 6 adds `&& !IsCreating` to the three rules; write them without it here.

- [ ] **Step 1: Write the failing view-model tests**

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.App.Tests.Fakes;
using LizTerm.App.ViewModels;
using LizTerm.Core.HostFiles;

namespace LizTerm.App.Tests.ViewModels;

public class MvsmfBrowserManageTests
{
    private static async Task<ConfirmationRequest> AskedAsync(BrowserTestHost t, Task running)
    {
        await Wait.UntilAsync(() => t.Vm.Confirmation is not null || running.IsCompleted, "the question");
        Assert.False(running.IsCompleted, "the operation ended without asking: " + t.Vm.StatusText + " " + t.Vm.ErrorText);
        return t.Vm.Confirmation!;
    }

    private static void Answer(ConfirmationRequest question, string input)
    {
        question.Input = input;
        Assert.True(question.CanAnswerPrimary, question.InputProblem);
        question.PrimaryCommand.Execute(null);
    }

    // ---- member rename ----

    [Fact]
    public async Task Rename_member_asks_with_the_old_name_and_renames_on_the_host()
    {
        var t = BrowserTestHost.Create();
        await t.ChooseAsync("MVSCE02.CNTL");
        t.Select("HELLO");
        t.Access.Etags.Remember(HostPath.ForMember("MVSCE02.CNTL", "HELLO"), "a");
        var selected = new List<IReadOnlyList<MemberRow>>();
        t.Vm.SelectMemberRequested += row => selected.Add([row]);

        var renaming = t.Vm.RenameMemberCommand.ExecuteAsync(null);
        var question = await AskedAsync(t, renaming);
        Assert.Equal("Rename HELLO in MVSCE02.CNTL to:", question.Message);
        Assert.Equal("Rename", question.PrimaryLabel);
        Assert.False(question.HasSecondary);
        Assert.Equal("HELLO", question.Input);
        Answer(question, "hello2");
        await renaming;

        Assert.Contains("rename:MVSCE02.CNTL(HELLO):HELLO2", t.Host.CallsSnapshot());
        Assert.Equal(new[] { "ALLOC", "COMPILE", "HELLO2" }, t.Vm.Members.Select(m => m.Name));
        Assert.Equal(new[] { "HELLO2" }, t.Vm.SelectedMembers.Select(m => m.Name));
        Assert.Equal(new[] { "HELLO2" }, selected.Single().Select(m => m.Name));
        Assert.Equal("✓ Renamed HELLO to HELLO2.", t.Vm.StatusText);
        Assert.Null(t.Access.Etags.TryGet(HostPath.ForMember("MVSCE02.CNTL", "HELLO")));
        Assert.Equal("a", t.Access.Etags.TryGet(HostPath.ForMember("MVSCE02.CNTL", "HELLO2")));
    }

    [Fact]
    public async Task Rename_member_cancelled_renames_nothing()
    {
        var t = BrowserTestHost.Create();
        await t.ChooseAsync("MVSCE02.CNTL");
        t.Select("HELLO");

        var renaming = t.Vm.RenameMemberCommand.ExecuteAsync(null);
        (await AskedAsync(t, renaming)).CancelCommand.Execute(null);
        await renaming;

        Assert.DoesNotContain(t.Host.CallsSnapshot(), c => c.StartsWith("rename:"));
        Assert.Equal("– Rename cancelled.", t.Vm.StatusText);
    }

    [Fact]
    public async Task A_member_that_went_meanwhile_reloads_the_list()
    {
        var t = BrowserTestHost.Create();
        await t.ChooseAsync("MVSCE02.CNTL");
        t.Select("HELLO");
        t.Host.Members["MVSCE02.CNTL"].Remove("HELLO");

        var renaming = t.Vm.RenameMemberCommand.ExecuteAsync(null);
        Answer(await AskedAsync(t, renaming), "HELLO2");
        await renaming;

        Assert.Equal(new[] { "ALLOC", "COMPILE" }, t.Vm.Members.Select(m => m.Name));
        Assert.Equal("✗ HELLO: Not found.", t.Vm.StatusText);
    }

    [Fact]
    public async Task A_member_renamed_onto_an_existing_name_is_the_hosts_sentence()
    {
        var t = BrowserTestHost.Create();
        await t.ChooseAsync("MVSCE02.CNTL");
        t.Select("HELLO");

        var renaming = t.Vm.RenameMemberCommand.ExecuteAsync(null);
        Answer(await AskedAsync(t, renaming), "ALLOC");
        await renaming;

        Assert.Equal("✗ Rename MVSCE02.CNTL(HELLO) to ALLOC: a member of that name already exists.", t.Vm.StatusText);
        Assert.Equal(new[] { "ALLOC", "COMPILE", "HELLO" }, t.Vm.Members.Select(m => m.Name));
    }

    [Fact]
    public async Task A_connection_failure_on_a_member_rename_offers_retry_which_asks_again()
    {
        var t = BrowserTestHost.Create();
        await t.ChooseAsync("MVSCE02.CNTL");
        t.Select("HELLO");
        t.Host.Failures["rename:MVSCE02.CNTL(HELLO):HELLO2"] =
            new HostFileException(HostFileErrorKind.Unreachable, "MVSCE02.CNTL(HELLO): cannot reach the host (refused).");

        var renaming = t.Vm.RenameMemberCommand.ExecuteAsync(null);
        Answer(await AskedAsync(t, renaming), "HELLO2");
        await renaming;
        Assert.True(t.Vm.CanRetry);

        t.Host.Failures.Clear();
        var retrying = t.Vm.RetryCommand.ExecuteAsync(null);
        var question = await AskedAsync(t, retrying);
        Assert.Equal("Rename HELLO in MVSCE02.CNTL to:", question.Message);
        Answer(question, "HELLO2");
        await retrying;

        Assert.False(t.Vm.HasError);
        Assert.Equal("✓ Renamed HELLO to HELLO2.", t.Vm.StatusText);
    }

    [Fact]
    public async Task Rename_member_needs_exactly_one_member_of_a_pds()
    {
        var t = BrowserTestHost.Create();
        await t.ChooseAsync("MVSCE02.CNTL");
        Assert.False(t.Vm.RenameMemberCommand.CanExecute(null));
        t.Select("HELLO");
        Assert.True(t.Vm.RenameMemberCommand.CanExecute(null));
        t.Select("HELLO", "ALLOC");
        Assert.False(t.Vm.RenameMemberCommand.CanExecute(null));
        await t.ChooseAsync("MVSCE02.UFSHOME");
        Assert.False(t.Vm.RenameMemberCommand.CanExecute(null));
    }

    // ---- dataset rename ----

    [Fact]
    public async Task Rename_dataset_relists_and_chooses_the_new_name()
    {
        var t = BrowserTestHost.Create();
        await t.ChooseAsync("MVSCE02.CNTL");
        t.Access.Etags.Remember(HostPath.ForMember("MVSCE02.CNTL", "HELLO"), "a");

        var renaming = t.Vm.RenameDatasetCommand.ExecuteAsync(null);
        var question = await AskedAsync(t, renaming);
        Assert.Equal("Rename MVSCE02.CNTL to:", question.Message);
        Assert.Equal("MVSCE02.CNTL", question.Input);
        Answer(question, "mvsce02.jcl");
        await renaming;

        Assert.Contains("rename:MVSCE02.CNTL:MVSCE02.JCL", t.Host.CallsSnapshot());
        Assert.Equal("MVSCE02.JCL", t.Vm.SelectedDataset?.Name);
        Assert.DoesNotContain(t.Vm.Datasets, d => d.Name == "MVSCE02.CNTL");
        Assert.Equal(new[] { "ALLOC", "COMPILE", "HELLO" }, t.Vm.Members.Select(m => m.Name));
        Assert.Equal("✓ Renamed MVSCE02.CNTL to MVSCE02.JCL.", t.Vm.StatusText);
        Assert.Equal("a", t.Access.Etags.TryGet(HostPath.ForMember("MVSCE02.JCL", "HELLO")));
        Assert.Null(t.Access.Etags.TryGet(HostPath.ForMember("MVSCE02.CNTL", "HELLO")));
    }

    [Fact]
    public async Task Rename_dataset_outside_the_filter_says_so()
    {
        var t = BrowserTestHost.Create();
        await t.ChooseAsync("MVSCE02.UFSHOME");

        var renaming = t.Vm.RenameDatasetCommand.ExecuteAsync(null);
        Answer(await AskedAsync(t, renaming), "OTHER.UFSHOME");
        await renaming;

        Assert.Null(t.Vm.SelectedDataset);
        Assert.Equal("✓ Renamed MVSCE02.UFSHOME to OTHER.UFSHOME (not shown by the filter MVSCE02.**).", t.Vm.StatusText);
    }

    [Fact]
    public async Task Rename_dataset_onto_an_existing_name_is_the_hosts_server_error()
    {
        var t = BrowserTestHost.Create();
        await t.ChooseAsync("MVSCE02.CNTL");

        var renaming = t.Vm.RenameDatasetCommand.ExecuteAsync(null);
        Answer(await AskedAsync(t, renaming), "MVSCE02.LOAD");
        await renaming;

        Assert.Equal("✗ Server error: Rename operation failed (reason 8).", t.Vm.StatusText);
        Assert.Equal("MVSCE02.CNTL", t.Vm.SelectedDataset?.Name);
    }

    [Fact]
    public async Task A_listing_that_fails_after_a_rename_retries_only_the_listing()
    {
        var t = BrowserTestHost.Create();
        await t.ChooseAsync("MVSCE02.CNTL");
        t.Host.Failures["list:MVSCE02.**"] = new HostFileException(HostFileErrorKind.Unreachable, "cannot reach the host (refused).");

        var renaming = t.Vm.RenameDatasetCommand.ExecuteAsync(null);
        Answer(await AskedAsync(t, renaming), "MVSCE02.JCL");
        await renaming;
        Assert.True(t.Vm.CanRetry);

        t.Host.Failures.Clear();
        await t.Vm.RetryCommand.ExecuteAsync(null);

        Assert.False(t.Vm.HasError);
        Assert.Equal(1, t.Host.CallsSnapshot().Count(c => c.StartsWith("rename:")));
        Assert.Equal("MVSCE02.JCL", t.Vm.SelectedDataset?.Name);
        Assert.Equal("✓ Renamed MVSCE02.CNTL to MVSCE02.JCL.", t.Vm.StatusText);
    }

    [Fact]
    public async Task Rename_and_delete_dataset_need_a_selected_dataset_with_an_acceptable_name()
    {
        var t = BrowserTestHost.Create(seed: host =>
        {
            BrowserTestHost.Standard(host);
            host.Datasets.Add(new HostFileEntry("MVSCE02.BAD..NAME", HostFileEntryKind.Dataset, new DatasetAttributes("PO", "FB", 80, 3120, "PUB000")));
        });
        await t.ListAsync();
        Assert.False(t.Vm.RenameDatasetCommand.CanExecute(null));
        Assert.False(t.Vm.DeleteDatasetCommand.CanExecute(null));

        await t.ChooseAsync("MVSCE02.DB");
        Assert.True(t.Vm.RenameDatasetCommand.CanExecute(null));
        Assert.True(t.Vm.DeleteDatasetCommand.CanExecute(null));

        t.Vm.SelectedDataset = t.Vm.Datasets.Single(d => d.Name == "MVSCE02.BAD..NAME");
        Assert.False(t.Vm.RenameDatasetCommand.CanExecute(null));
        Assert.False(t.Vm.DeleteDatasetCommand.CanExecute(null));
    }

    // ---- dataset delete ----

    [Fact]
    public async Task Delete_dataset_names_the_pds_with_its_member_count()
    {
        var t = BrowserTestHost.Create();
        await t.ChooseAsync("MVSCE02.CNTL");
        t.Access.Etags.Remember(HostPath.ForMember("MVSCE02.CNTL", "HELLO"), "a");
        t.Access.Etags.Remember(HostPath.ForMember("MVSCE02.LOAD", "PROG"), "b");

        var deleting = t.Vm.DeleteDatasetCommand.ExecuteAsync(null);
        var question = await AskedAsync(t, deleting);
        Assert.Equal("Delete MVSCE02.CNTL, a partitioned dataset with 3 members? This cannot be undone.", question.Message);
        Assert.Equal("Delete MVSCE02.CNTL", question.PrimaryLabel);
        Assert.False(question.HasInput);
        question.PrimaryCommand.Execute(null);
        await deleting;

        Assert.Contains("delete:MVSCE02.CNTL", t.Host.CallsSnapshot());
        Assert.DoesNotContain(t.Vm.Datasets, d => d.Name == "MVSCE02.CNTL");
        Assert.Null(t.Vm.SelectedDataset);
        Assert.Empty(t.Vm.Members);
        Assert.Equal("✓ Deleted MVSCE02.CNTL.", t.Vm.StatusText);
        Assert.Null(t.Access.Etags.TryGet(HostPath.ForMember("MVSCE02.CNTL", "HELLO")));
        Assert.Equal("b", t.Access.Etags.TryGet(HostPath.ForMember("MVSCE02.LOAD", "PROG")));
    }

    [Fact]
    public async Task Delete_dataset_says_plus_while_the_host_has_more_members()
    {
        var t = BrowserTestHost.Create(seed: BrowserTestHost.Large, pageSize: 2);
        await t.ChooseAsync("MVSCE02.BIG");
        Assert.True(t.Vm.HasMoreMembers);

        var deleting = t.Vm.DeleteDatasetCommand.ExecuteAsync(null);
        var question = await AskedAsync(t, deleting);
        Assert.Equal("Delete MVSCE02.BIG, a partitioned dataset with 2+ members? This cannot be undone.", question.Message);
        question.CancelCommand.Execute(null);
        await deleting;
    }

    [Fact]
    public async Task Delete_dataset_words_a_sequential_and_an_unsupported_dataset()
    {
        var t = BrowserTestHost.Create();
        await t.ChooseAsync("MVSCE02.UFSHOME");
        var deleting = t.Vm.DeleteDatasetCommand.ExecuteAsync(null);
        var question = await AskedAsync(t, deleting);
        Assert.Equal("Delete MVSCE02.UFSHOME, a sequential dataset? This cannot be undone.", question.Message);
        question.CancelCommand.Execute(null);
        await deleting;
        Assert.Equal("– Delete cancelled.", t.Vm.StatusText);

        await t.ChooseAsync("MVSCE02.DB");
        deleting = t.Vm.DeleteDatasetCommand.ExecuteAsync(null);
        question = await AskedAsync(t, deleting);
        Assert.Equal("Delete MVSCE02.DB? This cannot be undone.", question.Message);
        question.PrimaryCommand.Execute(null);
        await deleting;
        Assert.Equal("✓ Deleted MVSCE02.DB.", t.Vm.StatusText);
        Assert.Equal(3, t.Vm.Datasets.Count);
    }

    [Fact]
    public async Task A_dataset_delete_that_fails_keeps_the_row()
    {
        var t = BrowserTestHost.Create();
        await t.ChooseAsync("MVSCE02.CNTL");
        t.Host.Failures["delete:MVSCE02.CNTL"] = new HostFileException(HostFileErrorKind.NotAuthorized, "x", 6);

        var deleting = t.Vm.DeleteDatasetCommand.ExecuteAsync(null);
        (await AskedAsync(t, deleting)).PrimaryCommand.Execute(null);
        await deleting;

        Assert.Equal("✗ Not authorized.", t.Vm.StatusText);
        Assert.Equal("MVSCE02.CNTL", t.Vm.SelectedDataset?.Name);
        Assert.Equal(4, t.Vm.Datasets.Count);
    }
}
```

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~MvsmfBrowserManageTests"`
Expected: build error (no `RenameMemberCommand`).

- [ ] **Step 3: Write the Manage slice**

`src/LizTerm.App/ViewModels/MvsmfBrowserViewModel.Manage.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using CommunityToolkit.Mvvm.Input;
using LizTerm.App.HostFiles;
using LizTerm.Core.HostFiles;

namespace LizTerm.App.ViewModels;

/// <summary>Rename a member or a dataset and delete a dataset (spec §4.4). A rename asks in the strip with a text
/// box; the ETag memory follows every change.</summary>
public sealed partial class MvsmfBrowserViewModel
{
    /// <summary>Raised when an operation wants one member selected (a renamed member under its new name). The window
    /// applies it to its list box, which pushes it back through <see cref="SetSelectedMembers"/>.</summary>
    public event Action<MemberRow>? SelectMemberRequested;

    /// <summary>A dataset the pane's Rename… and Delete… can act on: listed under a name the rules accept. Its
    /// organisation does not matter, since neither operation opens it.</summary>
    private bool CanManageDataset =>
        !IsBusy && !IsReviewingUpload && SelectedDataset is { } dataset && HostPath.DatasetNameError(dataset.Name) is null;

    private bool CanRenameDataset => CanManageDataset;
    private bool CanDeleteDataset => CanManageDataset;
    private bool CanRenameMember => !IsBusy && !IsReviewingUpload && SelectedDataset is { IsPartitioned: true } && _selectedMembers.Count == 1;

    // ---- member rename ----

    [RelayCommand(CanExecute = nameof(CanRenameMember))]
    private Task RenameMemberAsync() =>
        SelectedDataset is { } dataset && _selectedMembers is [var member] ? RenameMemberAsync(dataset, member) : Task.CompletedTask;

    /// <summary>A retry asks again about the same member: the failure changed nothing.</summary>
    private Task RenameMemberAsync(DatasetRow dataset, MemberRow member) =>
        RunExclusiveAsync(token => RenameMemberCoreAsync(dataset, member, token), () => RenameMemberAsync(dataset, member));

    private async Task RenameMemberCoreAsync(DatasetRow dataset, MemberRow member, CancellationToken token)
    {
        var question = new ConfirmationRequest($"Rename {member.Name} in {dataset.Name} to:", "Rename",
            input: member.Name, inputRule: HostPath.MemberNameError);
        var answer = await AskAsync(question);
        if (answer.Choice != ConfirmChoice.Primary)
        {
            StatusText = "– Rename cancelled.";
            return;
        }
        // The rule passed, so WithMember cannot refuse; it folds the answer (blanks, case) the way the host reads it.
        var to = dataset.Path.WithMember(question.Input);
        StatusText = $"⟳ Renaming {member.Name} to {to.Member}…";
        try
        {
            await _connection.RunAsync(service => service.RenameAsync(member.Path, to.Member!, token));
        }
        catch (HostFileException ex) when (ex.Kind == HostFileErrorKind.NotFound)
        {
            // The member went meanwhile (another user, a job), so the list on screen is stale.
            await LoadMembersCoreAsync(dataset, token);
            StatusText = $"✗ {member.Name}: {HostFileMessages.Describe(ex)}";
            return;
        }
        _access.Etags.Move(member.Path, to);
        await LoadMembersCoreAsync(dataset, token);
        if (ReferenceEquals(SelectedDataset, dataset) && VisibleMembers.FirstOrDefault(row => row.Name == to.Member) is { } renamed)
        {
            SetSelectedMembers([renamed]);
            SelectMemberRequested?.Invoke(renamed);
        }
        StatusText = $"✓ Renamed {member.Name} to {to.Member}.";
    }

    // ---- dataset rename ----

    [RelayCommand(CanExecute = nameof(CanRenameDataset))]
    private Task RenameDatasetAsync() => SelectedDataset is { } dataset ? RenameDatasetAsync(dataset) : Task.CompletedTask;

    private Task RenameDatasetAsync(DatasetRow dataset) =>
        RunThenListAsync((retryWith, token) => RenameDatasetCoreAsync(dataset, retryWith, token), () => RenameDatasetAsync(dataset));

    private async Task RenameDatasetCoreAsync(DatasetRow dataset, Action<Func<Task>> retryWith, CancellationToken token)
    {
        var question = new ConfirmationRequest($"Rename {dataset.Name} to:", "Rename",
            input: dataset.Name, inputRule: HostPath.DatasetNameError);
        var answer = await AskAsync(question);
        if (answer.Choice != ConfirmChoice.Primary)
        {
            StatusText = "– Rename cancelled.";
            return;
        }
        var from = HostPath.ForDataset(dataset.Name);
        var to = HostPath.ForDataset(question.Input);
        StatusText = $"⟳ Renaming {from} to {to}…";
        await _connection.RunAsync(service => service.RenameAsync(from, to.Dataset, token));
        _access.Etags.Move(from, to);
        var what = $"Renamed {from} to {to}";
        retryWith(() => ListAgainAsync(to.Dataset, what));
        await ShowAfterChangeAsync(to.Dataset, what, token);
    }

    // ---- dataset delete ----

    [RelayCommand(CanExecute = nameof(CanDeleteDataset))]
    private Task DeleteDatasetAsync() => SelectedDataset is { } dataset ? DeleteDatasetAsync(dataset) : Task.CompletedTask;

    private Task DeleteDatasetAsync(DatasetRow dataset) =>
        RunExclusiveAsync(token => DeleteDatasetCoreAsync(dataset, token), () => DeleteDatasetAsync(dataset));

    private async Task DeleteDatasetCoreAsync(DatasetRow dataset, CancellationToken token)
    {
        var answer = await AskAsync(new ConfirmationRequest($"Delete {DescribeForDelete(dataset)}? This cannot be undone.", $"Delete {dataset.Name}"));
        if (answer.Choice != ConfirmChoice.Primary)
        {
            StatusText = "– Delete cancelled.";
            return;
        }
        var path = HostPath.ForDataset(dataset.Name);
        StatusText = $"⟳ Deleting {path}…";
        await _connection.RunAsync(service => service.DeleteAsync(path, token));
        _access.Etags.ForgetUnder(path);
        if (ReferenceEquals(SelectedDataset, dataset)) SelectedDataset = null;
        Datasets.Remove(dataset);
        StatusText = $"✓ Deleted {dataset.Name}.";
    }

    /// <summary>The name, and what it is: a partitioned dataset with its loaded member count (a plus while the host
    /// has more, or while a host-side member filter is in force, since the count is then of matches), a sequential
    /// dataset, or the bare name for an organisation the browser cannot open.</summary>
    private string DescribeForDelete(DatasetRow dataset)
    {
        if (dataset.IsPartitioned && ReferenceEquals(SelectedDataset, dataset))
        {
            var count = HasMoreMembers || _memberPattern is not null ? $"{Members.Count}+ members" : Plural(Members.Count, "member");
            return $"{dataset.Name}, a partitioned dataset with {count}";
        }
        return dataset.IsSequential ? $"{dataset.Name}, a sequential dataset" : dataset.Name;
    }

    // ---- shared ----

    /// <summary>An operation whose second half is a listing (a dataset rename, a create): once the host has done the
    /// first half, a connection failure in the listing must retry only the listing, never ask the host to do the
    /// first half again. The work calls <c>retryWith</c> with the listing's retry at that point.</summary>
    private Task RunThenListAsync(Func<Action<Func<Task>>, CancellationToken, Task> work, Func<Task> retryAll)
    {
        var retry = retryAll;
        return RunExclusiveAsync(token => work(next => retry = next, token), () => retry());
    }

    private Task ListAgainAsync(string name, string what) =>
        RunExclusiveAsync(token => ShowAfterChangeAsync(name, what, token), () => ListAgainAsync(name, what));

    /// <summary>After a dataset is renamed or created: the filter is listed again, the dataset is chosen if the
    /// listing shows it, and the status line says what happened, adding that the filter hides it when it does.</summary>
    private async Task ShowAfterChangeAsync(string name, string what, CancellationToken token)
    {
        await ListCoreAsync(token);
        if (Datasets.FirstOrDefault(row => row.Name == name) is { } row)
        {
            await SelectAsync(row, token);
            StatusText = $"✓ {what}.";
        }
        else StatusText = $"✓ {what} (not shown by the filter {_listedPattern}).";
    }

    /// <summary>Chooses <paramref name="row"/> from inside a running operation. Setting SelectedDataset starts a
    /// member load of its own only when nothing runs (RunExclusiveAsync ignores a second operation), so the load is
    /// awaited here.</summary>
    private async Task SelectAsync(DatasetRow row, CancellationToken token)
    {
        SelectedDataset = row;
        if (row.IsPartitioned) await LoadMembersCoreAsync(row, token);
    }
}
```

- [ ] **Step 4: Notify the new commands**

In `src/LizTerm.App/ViewModels/MvsmfBrowserViewModel.cs`, `NotifyCommands()`, add after `DeleteCommand.NotifyCanExecuteChanged();`:

```csharp
        RenameMemberCommand.NotifyCanExecuteChanged();
        RenameDatasetCommand.NotifyCanExecuteChanged();
        DeleteDatasetCommand.NotifyCanExecuteChanged();
```

Also extend the class summary's last sentence: "see the partial files for downloads, uploads, delete, manage and create."

- [ ] **Step 5: Run the view-model tests**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~MvsmfBrowserManageTests"`
Expected: 15 passed. If `Rename_dataset_relists_and_chooses_the_new_name` finds `Members` empty, `SelectAsync` is not awaiting the load; if `A_listing_that_fails_after_a_rename_retries_only_the_listing` counts two renames, `retryWith` was not called before `ShowAfterChangeAsync`.

- [ ] **Step 6: Write the failing window tests**

Add to `tests/LizTerm.App.Tests/Views/MvsmfBrowserWindowTests.cs`:

```csharp
    [AvaloniaFact]
    public async Task The_manage_buttons_follow_the_selection()
    {
        var (window, t) = Show();
        await Wait.UntilAsync(() => t.Vm.Datasets.Count == 4, "the first listing");
        var rename = Named<Button>(window, "RenameDatasetButton");
        var delete = Named<Button>(window, "DeleteDatasetButton");
        var renameMember = Named<Button>(window, "RenameMemberButton");
        Assert.Equal("Rename…", rename.Content);
        Assert.Equal("Delete…", delete.Content);
        Assert.Equal("Rename…", renameMember.Content);
        Assert.False(rename.IsEffectivelyEnabled);
        Assert.False(delete.IsEffectivelyEnabled);
        Assert.False(renameMember.IsEffectivelyEnabled);

        await t.ChooseAsync("MVSCE02.DB");
        Dispatcher.UIThread.RunJobs();
        Assert.True(rename.IsEffectivelyEnabled);
        Assert.True(delete.IsEffectivelyEnabled);
        Assert.False(renameMember.IsEffectivelyEnabled);

        await t.ChooseAsync("MVSCE02.CNTL");
        var members = Named<ListBox>(window, "MemberList");
        members.SelectedItems!.Add(t.Vm.VisibleMembers[0]);
        Dispatcher.UIThread.RunJobs();
        Assert.True(renameMember.IsEffectivelyEnabled);
        members.SelectedItems!.Add(t.Vm.VisibleMembers[1]);
        Dispatcher.UIThread.RunJobs();
        Assert.False(renameMember.IsEffectivelyEnabled);
    }

    [AvaloniaFact]
    public async Task A_renamed_member_is_selected_in_the_list()
    {
        var (window, t) = Show();
        await Wait.UntilAsync(() => t.Vm.Datasets.Count == 4, "the first listing");
        await t.ChooseAsync("MVSCE02.CNTL");
        var members = Named<ListBox>(window, "MemberList");
        members.SelectedItems!.Add(t.Vm.VisibleMembers[2]);
        Dispatcher.UIThread.RunJobs();

        var renaming = t.Vm.RenameMemberCommand.ExecuteAsync(null);
        await Wait.UntilAsync(() => t.Vm.HasConfirmation, "the question");
        t.Vm.Confirmation!.Input = "HELLO2";
        t.Vm.Confirmation.PrimaryCommand.Execute(null);
        await renaming;
        window.UpdateLayout();

        Assert.Equal(new[] { "HELLO2" }, members.SelectedItems!.OfType<MemberRow>().Select(m => m.Name));
        Assert.Equal(new[] { "HELLO2" }, t.Vm.SelectedMembers.Select(m => m.Name));
    }
```

- [ ] **Step 7: Run them to verify they fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~MvsmfBrowserWindowTests.The_manage_buttons|FullyQualifiedName~MvsmfBrowserWindowTests.A_renamed_member"`
Expected: FAIL, `RenameDatasetButton` not found; the second test finds no selection.

- [ ] **Step 8: The buttons and the selection**

In `src/LizTerm.App/Views/MvsmfBrowserWindow.axaml`, in the dataset pane's `DockPanel`, after the `LoadMoreDatasetsButton` (which stays the lowest) and before the `DatasetList`, add:

```xml
        <WrapPanel x:Name="DatasetButtons" DockPanel.Dock="Bottom" ItemSpacing="8" LineSpacing="6" Margin="8,4">
          <Button x:Name="RenameDatasetButton" Content="Rename…" Command="{Binding RenameDatasetCommand}" />
          <Button x:Name="DeleteDatasetButton" Content="Delete…" Command="{Binding DeleteDatasetCommand}" />
        </WrapPanel>
```

In the bottom bar's `WrapPanel`, between `UploadButton` and `DeleteButton`:

```xml
          <Button x:Name="RenameMemberButton" Content="Rename…" Command="{Binding RenameMemberCommand}" />
```

In `MvsmfBrowserWindow.axaml.cs`, `OnDataContextChanged`:

```csharp
    protected override void OnDataContextChanged(EventArgs e)
    {
        if (_watched is not null)
        {
            _watched.PropertyChanged -= OnViewModelPropertyChanged;
            _watched.SelectMemberRequested -= SelectMember;
        }
        _watched = ViewModel;
        if (_watched is not null)
        {
            _watched.PropertyChanged += OnViewModelPropertyChanged;
            _watched.SelectMemberRequested += SelectMember;
        }
        base.OnDataContextChanged(e);
    }

    /// <summary>The list box is the selection's owner; setting its SelectedItem replaces the selection with the one
    /// row, and its SelectionChanged pushes that back to the view model.</summary>
    private void SelectMember(MemberRow row) => MemberList.SelectedItem = row;
```

and in `OnClosed`, beside the `PropertyChanged` unsubscribe: `if (_watched is not null) _watched.SelectMemberRequested -= SelectMember;` (fold both into one `if`).

- [ ] **Step 9: Run the window tests and the App suite**

Run: `dotnet test tests/LizTerm.App.Tests`
Expected: all pass.

- [ ] **Step 10: Commit**

```bash
git add src/LizTerm.App/ViewModels/MvsmfBrowserViewModel.Manage.cs src/LizTerm.App/ViewModels/MvsmfBrowserViewModel.cs src/LizTerm.App/Views/MvsmfBrowserWindow.axaml src/LizTerm.App/Views/MvsmfBrowserWindow.axaml.cs tests/LizTerm.App.Tests/ViewModels/MvsmfBrowserManageTests.cs tests/LizTerm.App.Tests/Views/MvsmfBrowserWindowTests.cs
git commit -m "Browser: rename a member or a dataset, delete a dataset

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 5: The New dataset form's view model

**Files:**
- Create: `src/LizTerm.App/ViewModels/NewDatasetFormViewModel.cs`
- Test: `tests/LizTerm.App.Tests/ViewModels/NewDatasetFormViewModelTests.cs`

**Interfaces:**
- Consumes: `HostPath.DatasetNameError`, `DatasetAllocation` (`Problems()` → `IReadOnlyDictionary<AllocationField, string>`), `DatasetAttributes`.
- Produces: `NewDatasetFormViewModel` with observable strings `Name`, `Recfm`, `Lrecl`, `Blksize`, `Primary`, `Secondary`, `DirectoryBlocks`, `string? Message`; bools `IsPartitioned` (observable) / `IsSequential` (derived, settable), `IsTracks` (observable) / `IsCylinders` (derived, settable); `string? NameProblem`, `RecfmProblem`, `LreclProblem`, `BlksizeProblem`, `PrimaryProblem`, `SecondaryProblem`, `DirectoryBlocksProblem` (each `✗ ` and a sentence, or null); `bool CanCreate`; `DatasetAllocation Allocation`; `void PrefillFrom(DatasetAttributes attributes)`. Every change of a field raises the seven problems, `CanCreate`, `IsSequential` and `IsCylinders`.

- [ ] **Step 1: Write the failing tests**

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.App.ViewModels;
using LizTerm.Core.HostFiles;

namespace LizTerm.App.Tests.ViewModels;

public class NewDatasetFormViewModelTests
{
    private static NewDatasetFormViewModel Filled(string name = "MVSCE02.NEW")
    {
        var form = new NewDatasetFormViewModel { Name = name };
        return form;
    }

    [Fact]
    public void Starts_as_an_fb80_library_with_the_default_space_and_no_name()
    {
        var form = new NewDatasetFormViewModel();

        Assert.Equal(("", true, "FB", "80", "3120"), (form.Name, form.IsPartitioned, form.Recfm, form.Lrecl, form.Blksize));
        Assert.Equal((true, "5", "5", "20"), (form.IsTracks, form.Primary, form.Secondary, form.DirectoryBlocks));
        Assert.Equal("✗ Enter a dataset name.", form.NameProblem);
        Assert.Null(form.RecfmProblem);
        Assert.False(form.CanCreate);
    }

    [Fact]
    public void A_full_form_can_create_and_folds_what_it_sends()
    {
        var form = Filled(" mvsce02.new ");
        form.Recfm = " fb ";
        form.IsCylinders = true;

        Assert.True(form.CanCreate);
        var allocation = form.Allocation;
        Assert.Equal(DatasetOrganization.Partitioned, allocation.Organization);
        Assert.Equal("FB", allocation.FoldedRecfm);
        Assert.Equal((80, 3120, SpaceUnit.Cylinders, 5, 5, 20), (allocation.Lrecl, allocation.Blksize, allocation.Unit, allocation.Primary, allocation.Secondary, allocation.DirectoryBlocks));
        Assert.False(form.IsTracks);
    }

    [Fact]
    public void Each_field_shows_its_own_problem()
    {
        var form = Filled();

        form.Recfm = "X";
        Assert.Equal("✗ A record format starts with F, V or U.", form.RecfmProblem);
        form.Recfm = "FB";

        form.Lrecl = "abc";
        Assert.Equal("✗ Enter a whole number.", form.LreclProblem);
        form.Lrecl = "0";
        Assert.Equal("✗ LRECL must be between 1 and 32760.", form.LreclProblem);
        form.Lrecl = "80";

        form.Blksize = "-1";
        Assert.Equal("✗ Enter a whole number.", form.BlksizeProblem);
        form.Blksize = "0";
        Assert.Equal("✗ BLKSIZE must be between 1 and 32760.", form.BlksizeProblem);
        form.Blksize = "3120";

        form.Primary = "0";
        Assert.Equal("✗ Primary space must be at least 1.", form.PrimaryProblem);
        form.Primary = "5";

        form.Secondary = "x";
        Assert.Equal("✗ Enter a whole number.", form.SecondaryProblem);
        form.Secondary = "0";
        Assert.Null(form.SecondaryProblem);

        form.DirectoryBlocks = "0";
        Assert.Equal("✗ A partitioned dataset needs at least 1 directory block.", form.DirectoryBlocksProblem);
        Assert.False(form.CanCreate);
        form.DirectoryBlocks = "20";
        Assert.True(form.CanCreate);
    }

    [Fact]
    public void A_sequential_dataset_ignores_the_directory_blocks()
    {
        var form = Filled();
        form.DirectoryBlocks = "x";
        Assert.NotNull(form.DirectoryBlocksProblem);

        form.IsSequential = true;

        Assert.False(form.IsPartitioned);
        Assert.Null(form.DirectoryBlocksProblem);
        Assert.True(form.CanCreate);
        Assert.Equal(DatasetOrganization.Sequential, form.Allocation.Organization);
    }

    [Fact]
    public void Undefined_length_records_allow_lrecl_0()
    {
        var form = Filled();
        form.Recfm = "U";
        form.Lrecl = "0";

        Assert.Null(form.LreclProblem);
        Assert.True(form.CanCreate);
    }

    [Fact]
    public void A_bad_name_is_the_name_problem()
    {
        var form = Filled("MVSCE02.TOOLONGQUALIFIER");

        Assert.Equal("✗ Qualifier 'TOOLONGQUALIFIER' is longer than 8 characters.", form.NameProblem);
        Assert.False(form.CanCreate);
    }

    [Fact]
    public void Prefill_takes_the_type_and_dcb_from_a_dataset()
    {
        var form = Filled();

        form.PrefillFrom(new DatasetAttributes("PS", "U", 0, 19069, "PUB000"));

        Assert.Equal((false, "U", "0", "19069"), (form.IsPartitioned, form.Recfm, form.Lrecl, form.Blksize));
        Assert.Equal(("5", "5", "20"), (form.Primary, form.Secondary, form.DirectoryBlocks));
    }

    [Fact]
    public void Prefill_leaves_what_the_listing_did_not_say()
    {
        var form = Filled();

        form.PrefillFrom(new DatasetAttributes("DA", null, null, 4096, null));

        Assert.Equal((true, "FB", "80", "4096"), (form.IsPartitioned, form.Recfm, form.Lrecl, form.Blksize));
    }

    [Fact]
    public void A_change_notifies_every_problem_and_can_create()
    {
        var form = Filled();
        var changed = new List<string?>();
        form.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        form.Recfm = "V";

        foreach (var name in new[] { nameof(form.RecfmProblem), nameof(form.LreclProblem), nameof(form.CanCreate) })
            Assert.Contains(name, changed);
        changed.Clear();
        form.IsPartitioned = false;
        Assert.Contains(nameof(form.IsSequential), changed);
        Assert.Contains(nameof(form.DirectoryBlocksProblem), changed);
    }
}
```

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~NewDatasetFormViewModelTests"`
Expected: build error.

- [ ] **Step 3: Write the form's view model**

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using LizTerm.Core.HostFiles;

namespace LizTerm.App.ViewModels;

/// <summary>The New dataset form (spec §4.4): every field a string bound to a text box, two radio pairs, and a
/// problem line per field from <see cref="HostPath.DatasetNameError"/> and <see cref="DatasetAllocation.Problems"/>.
/// One instance lives as long as the browser window, so the space values it was last sent with are kept.</summary>
public sealed partial class NewDatasetFormViewModel : ObservableObject
{
    private static readonly string[] Derived =
    [
        nameof(IsSequential), nameof(IsCylinders), nameof(NameProblem), nameof(RecfmProblem), nameof(LreclProblem),
        nameof(BlksizeProblem), nameof(PrimaryProblem), nameof(SecondaryProblem), nameof(DirectoryBlocksProblem), nameof(CanCreate),
    ];

    [ObservableProperty] private string _name = "";
    [ObservableProperty] private bool _isPartitioned = true;
    [ObservableProperty] private string _recfm = "FB";
    [ObservableProperty] private string _lrecl = "80";
    [ObservableProperty] private string _blksize = "3120";
    [ObservableProperty] private bool _isTracks = true;
    [ObservableProperty] private string _primary = "5";
    [ObservableProperty] private string _secondary = "5";
    [ObservableProperty] private string _directoryBlocks = "20";

    /// <summary>The host's answer to the last Create, with its mark, or null. Not a field: it does not recheck.</summary>
    [ObservableProperty] private string? _message;

    public bool IsSequential
    {
        get => !IsPartitioned;
        set { if (value) IsPartitioned = false; }
    }

    public bool IsCylinders
    {
        get => !IsTracks;
        set { if (value) IsTracks = false; }
    }

    /// <summary>Type, RECFM, LRECL and BLKSIZE from a listed dataset, for a new one like it; a field the listing left
    /// out keeps its value.</summary>
    public void PrefillFrom(DatasetAttributes attributes)
    {
        if (attributes.IsPartitioned) IsPartitioned = true;
        else if (attributes.IsSequential) IsPartitioned = false;
        if (attributes.Recfm is { Length: > 0 } recfm) Recfm = recfm;
        if (attributes.Lrecl is { } lrecl) Lrecl = lrecl.ToString(CultureInfo.InvariantCulture);
        if (attributes.Blksize is { } blksize) Blksize = blksize.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>What Create sends. A numeric field that is not a whole number is sent as -1, which every rule
    /// refuses, and its own problem line wins (<see cref="NumberProblem"/>).</summary>
    public DatasetAllocation Allocation => new(
        IsPartitioned ? DatasetOrganization.Partitioned : DatasetOrganization.Sequential,
        Recfm, Number(Lrecl), Number(Blksize), IsTracks ? SpaceUnit.Tracks : SpaceUnit.Cylinders,
        Number(Primary), Number(Secondary), Number(DirectoryBlocks));

    public string? NameProblem => Mark(HostPath.DatasetNameError(Name));
    public string? RecfmProblem => Mark(Problems.GetValueOrDefault(AllocationField.Recfm));
    public string? LreclProblem => Mark(NumberProblem(Lrecl) ?? Problems.GetValueOrDefault(AllocationField.Lrecl));
    public string? BlksizeProblem => Mark(NumberProblem(Blksize) ?? Problems.GetValueOrDefault(AllocationField.Blksize));
    public string? PrimaryProblem => Mark(NumberProblem(Primary) ?? Problems.GetValueOrDefault(AllocationField.Primary));
    public string? SecondaryProblem => Mark(NumberProblem(Secondary) ?? Problems.GetValueOrDefault(AllocationField.Secondary));
    public string? DirectoryBlocksProblem =>
        IsPartitioned ? Mark(NumberProblem(DirectoryBlocks) ?? Problems.GetValueOrDefault(AllocationField.DirectoryBlocks)) : null;

    public bool CanCreate =>
        NameProblem is null && RecfmProblem is null && LreclProblem is null && BlksizeProblem is null
        && PrimaryProblem is null && SecondaryProblem is null && DirectoryBlocksProblem is null;

    private IReadOnlyDictionary<AllocationField, string> Problems => Allocation.Problems();

    private static string? Mark(string? problem) => problem is null ? null : "✗ " + problem;

    private static int Number(string text) => TryNumber(text, out var value) ? value : -1;

    private static string? NumberProblem(string text) => TryNumber(text, out _) ? null : "Enter a whole number.";

    /// <summary>Digits only: no sign, no blanks inside, no grouping.</summary>
    private static bool TryNumber(string text, out int value) =>
        int.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out value);

    /// <summary>Every field feeds several problems (RECFM decides LRECL's range, the type decides whether directory
    /// blocks count), so a change of any field raises them all.</summary>
    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is null || e.PropertyName == nameof(Message) || Derived.Contains(e.PropertyName)) return;
        foreach (var name in Derived) base.OnPropertyChanged(new PropertyChangedEventArgs(name));
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~NewDatasetFormViewModelTests"`
Expected: 9 passed.

- [ ] **Step 5: Commit**

```bash
git add src/LizTerm.App/ViewModels/NewDatasetFormViewModel.cs tests/LizTerm.App.Tests/ViewModels/NewDatasetFormViewModelTests.cs
git commit -m "Browser: the New dataset form's view model

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 6: New dataset in the browser

**Files:**
- Create: `src/LizTerm.App/ViewModels/MvsmfBrowserViewModel.Create.cs`
- Modify: `src/LizTerm.App/ViewModels/MvsmfBrowserViewModel.cs` (constructor, `ShowChooseHint`, `ShowSequentialNote`, `NotifyCommands`), `MvsmfBrowserViewModel.Uploads.cs` (`ShowMemberPane`, `CanChooseDataset`, `CanUpload`), `MvsmfBrowserViewModel.Downloads.cs` (`CanDownload`), `MvsmfBrowserViewModel.Delete.cs` (`CanDelete`), `MvsmfBrowserViewModel.Paging.cs` (`CanLoadMoreDatasets`, `CanLoadMoreMembers`), `MvsmfBrowserViewModel.Manage.cs` (`CanManageDataset`, `CanRenameMember`)
- Modify: `src/LizTerm.App/Views/MvsmfBrowserWindow.axaml` (`NewDatasetButton`, the create pane), `MvsmfBrowserWindow.axaml.cs` (Escape order, the form's focus, `RememberFocus`)
- Test: `tests/LizTerm.App.Tests/ViewModels/MvsmfBrowserCreateTests.cs` (new), `tests/LizTerm.App.Tests/Views/MvsmfBrowserWindowTests.cs` (three tests)

**Interfaces:**
- Consumes: `NewDatasetFormViewModel` (Task 5); `RunThenListAsync`, `ListAgainAsync`, `ShowAfterChangeAsync` (Task 4); `IHostFileService.CreateDatasetAsync(HostPath dataset, DatasetAllocation allocation, CancellationToken)`; `HostFileErrorKind.CannotAllocate`, `InvalidRequest`.
- Produces: `bool IsCreating` (observable), `NewDatasetFormViewModel Form`, `NewDatasetCommand` (`CanNewDataset`), `CreateCommand` (`CanCreate`), `CloseFormCommand` (`CanCloseForm`). Window controls `NewDatasetButton`, `CreatePane`, `NewNameBox`, `CreateButton`, `CloseFormButton`, `CreateMessage`.

- [ ] **Step 1: Write the failing view-model tests**

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.Core.HostFiles;

namespace LizTerm.App.Tests.ViewModels;

public class MvsmfBrowserCreateTests
{
    private static async Task<BrowserTestHost> OpenedAsync(string? chosen = "MVSCE02.CNTL")
    {
        var t = BrowserTestHost.Create();
        if (chosen is null) await t.ListAsync();
        else await t.ChooseAsync(chosen);
        t.Vm.NewDatasetCommand.Execute(null);
        Assert.True(t.Vm.IsCreating);
        return t;
    }

    [Fact]
    public async Task New_prefills_from_the_chosen_dataset_and_the_filters_first_qualifier()
    {
        var t = await OpenedAsync();
        var form = t.Vm.Form;

        Assert.Equal(("MVSCE02.", true, "FB", "80", "19040"), (form.Name, form.IsPartitioned, form.Recfm, form.Lrecl, form.Blksize));
        Assert.Equal(("5", "5", "20"), (form.Primary, form.Secondary, form.DirectoryBlocks));
        Assert.Null(form.Message);
        Assert.False(t.Vm.ShowMemberPane);
        Assert.False(t.Vm.CanChooseDataset);
        Assert.False(t.Vm.ShowChooseHint);
        Assert.False(t.Vm.CreateCommand.CanExecute(null));
        Assert.True(t.Vm.CloseFormCommand.CanExecute(null));
    }

    [Fact]
    public async Task New_from_a_load_library_prefills_undefined_length_records()
    {
        var t = await OpenedAsync("MVSCE02.LOAD");

        Assert.Equal(("U", "0", "19069"), (t.Vm.Form.Recfm, t.Vm.Form.Lrecl, t.Vm.Form.Blksize));
    }

    [Fact]
    public async Task New_with_nothing_chosen_keeps_the_defaults_and_names_from_the_filter()
    {
        var t = await OpenedAsync(chosen: null);
        Assert.Equal(("MVSCE02.", true, "FB", "80", "3120"), (t.Vm.Form.Name, t.Vm.Form.IsPartitioned, t.Vm.Form.Recfm, t.Vm.Form.Lrecl, t.Vm.Form.Blksize));

        t.Vm.CloseFormCommand.Execute(null);
        t.Vm.Filter = "**";
        t.Vm.NewDatasetCommand.Execute(null);
        Assert.Equal("", t.Vm.Form.Name);

        t.Vm.CloseFormCommand.Execute(null);
        t.Vm.Filter = " sys1.* ";
        t.Vm.NewDatasetCommand.Execute(null);
        Assert.Equal("SYS1.", t.Vm.Form.Name);
    }

    [Fact]
    public async Task Every_other_operation_is_off_while_the_form_is_open()
    {
        var t = await OpenedAsync();
        t.Select("HELLO");

        Assert.False(t.Vm.NewDatasetCommand.CanExecute(null));
        Assert.False(t.Vm.ListCommand.CanExecute(null));
        Assert.False(t.Vm.DownloadCommand.CanExecute(null));
        Assert.False(t.Vm.UploadCommand.CanExecute(null));
        Assert.False(t.Vm.DeleteCommand.CanExecute(null));
        Assert.False(t.Vm.RenameMemberCommand.CanExecute(null));
        Assert.False(t.Vm.RenameDatasetCommand.CanExecute(null));
        Assert.False(t.Vm.DeleteDatasetCommand.CanExecute(null));

        t.Vm.CloseFormCommand.Execute(null);

        Assert.False(t.Vm.IsCreating);
        Assert.True(t.Vm.ShowMemberPane);
        Assert.True(t.Vm.NewDatasetCommand.CanExecute(null));
        Assert.True(t.Vm.DownloadCommand.CanExecute(null));
    }

    [Fact]
    public async Task Create_sends_the_form_closes_it_and_chooses_the_new_dataset()
    {
        var t = await OpenedAsync();
        t.Vm.Form.Name = "mvsce02.new";
        t.Vm.Form.Primary = "10";
        Assert.True(t.Vm.CreateCommand.CanExecute(null));

        await t.Vm.CreateCommand.ExecuteAsync(null);

        Assert.Contains("create:MVSCE02.NEW", t.Host.CallsSnapshot());
        var created = t.Host.Datasets.Single(d => d.Name == "MVSCE02.NEW");
        Assert.Equal(("PO", "FB", 80, 19040), (created.Attributes!.Dsorg, created.Attributes.Recfm, created.Attributes.Lrecl, created.Attributes.Blksize));
        Assert.False(t.Vm.IsCreating);
        Assert.Equal("MVSCE02.NEW", t.Vm.SelectedDataset?.Name);
        Assert.Contains("members:MVSCE02.NEW", t.Host.CallsSnapshot());
        Assert.Equal("✓ Created MVSCE02.NEW.", t.Vm.StatusText);
    }

    [Fact]
    public async Task The_space_values_are_kept_for_the_next_form()
    {
        var t = await OpenedAsync();
        t.Vm.Form.Name = "MVSCE02.NEW";
        t.Vm.Form.Primary = "10";
        t.Vm.Form.Secondary = "2";
        t.Vm.Form.DirectoryBlocks = "30";
        t.Vm.Form.IsCylinders = true;
        await t.Vm.CreateCommand.ExecuteAsync(null);

        t.Vm.NewDatasetCommand.Execute(null);

        Assert.Equal(("10", "2", "30", true), (t.Vm.Form.Primary, t.Vm.Form.Secondary, t.Vm.Form.DirectoryBlocks, t.Vm.Form.IsCylinders));
        Assert.Equal("MVSCE02.", t.Vm.Form.Name);
    }

    [Fact]
    public async Task Create_outside_the_filter_says_so()
    {
        var t = await OpenedAsync();
        t.Vm.Form.Name = "OTHER.NEW";

        await t.Vm.CreateCommand.ExecuteAsync(null);

        Assert.False(t.Vm.IsCreating);
        Assert.Null(t.Vm.SelectedDataset);
        Assert.Equal("✓ Created OTHER.NEW (not shown by the filter MVSCE02.**).", t.Vm.StatusText);
    }

    [Fact]
    public async Task An_allocation_the_host_refuses_stays_in_the_form()
    {
        var t = await OpenedAsync();
        t.Vm.Form.Name = "MVSCE02.CNTL";

        await t.Vm.CreateCommand.ExecuteAsync(null);

        Assert.True(t.Vm.IsCreating);
        Assert.Equal("✗ The host could not allocate it: it may already exist, there may be no space, or you may not be authorized.", t.Vm.Form.Message);
        Assert.Equal("", t.Vm.StatusText);
        Assert.False(t.Vm.HasError);

        t.Vm.Form.Name = "MVSCE02.NEW";
        await t.Vm.CreateCommand.ExecuteAsync(null);
        Assert.False(t.Vm.IsCreating);
        Assert.Null(t.Vm.Form.Message);
    }

    [Fact]
    public async Task A_request_the_host_rejects_stays_in_the_form_with_its_words()
    {
        var t = await OpenedAsync();
        t.Vm.Form.Name = "MVSCE02.NEW";
        t.Host.Failures["create:MVSCE02.NEW"] = new HostFileException(HostFileErrorKind.InvalidRequest, "x", 3, "Invalid or missing allocation parameters");

        await t.Vm.CreateCommand.ExecuteAsync(null);

        Assert.True(t.Vm.IsCreating);
        Assert.Equal("✗ The host refused the request: Invalid or missing allocation parameters", t.Vm.Form.Message);
    }

    [Fact]
    public async Task A_connection_failure_offers_retry_which_sends_the_form_again()
    {
        var t = await OpenedAsync();
        t.Vm.Form.Name = "MVSCE02.NEW";
        t.Host.Failures["create:MVSCE02.NEW"] = new HostFileException(HostFileErrorKind.Unreachable, "MVSCE02.NEW: cannot reach the host (refused).");

        await t.Vm.CreateCommand.ExecuteAsync(null);
        Assert.True(t.Vm.CanRetry);
        Assert.True(t.Vm.IsCreating);

        t.Host.Failures.Clear();
        t.Vm.Form.Primary = "7";
        await t.Vm.RetryCommand.ExecuteAsync(null);

        Assert.False(t.Vm.HasError);
        Assert.Equal(2, t.Host.CallsSnapshot().Count(c => c == "create:MVSCE02.NEW"));
        Assert.False(t.Vm.IsCreating);
        Assert.Equal("✓ Created MVSCE02.NEW.", t.Vm.StatusText);
    }

    [Fact]
    public async Task A_listing_that_fails_after_a_create_retries_only_the_listing()
    {
        var t = await OpenedAsync();
        t.Vm.Form.Name = "MVSCE02.NEW";
        t.Host.Failures["list:MVSCE02.**"] = new HostFileException(HostFileErrorKind.Unreachable, "cannot reach the host (refused).");

        await t.Vm.CreateCommand.ExecuteAsync(null);
        Assert.True(t.Vm.CanRetry);
        Assert.False(t.Vm.IsCreating);

        t.Host.Failures.Clear();
        await t.Vm.RetryCommand.ExecuteAsync(null);

        Assert.Equal(1, t.Host.CallsSnapshot().Count(c => c == "create:MVSCE02.NEW"));
        Assert.Equal("MVSCE02.NEW", t.Vm.SelectedDataset?.Name);
        Assert.Equal("✓ Created MVSCE02.NEW.", t.Vm.StatusText);
    }

    [Fact]
    public async Task Create_is_off_while_a_field_has_a_problem()
    {
        var t = await OpenedAsync();
        t.Vm.Form.Name = "MVSCE02.NEW";
        var changed = 0;
        t.Vm.CreateCommand.CanExecuteChanged += (_, _) => changed++;

        t.Vm.Form.Lrecl = "x";
        Assert.False(t.Vm.CreateCommand.CanExecute(null));
        Assert.True(changed > 0);

        t.Vm.Form.Lrecl = "80";
        Assert.True(t.Vm.CreateCommand.CanExecute(null));
    }
}
```

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~MvsmfBrowserCreateTests"`
Expected: build error (no `NewDatasetCommand`).

- [ ] **Step 3: Write the Create slice**

`src/LizTerm.App/ViewModels/MvsmfBrowserViewModel.Create.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LizTerm.App.HostFiles;
using LizTerm.Core.HostFiles;

namespace LizTerm.App.ViewModels;

/// <summary>New dataset (spec §4.4): a form in the right pane, where the upload review goes, sent as an explicit
/// allocation. The form is one instance for the life of the window, so the space it was last sent with is kept.</summary>
public sealed partial class MvsmfBrowserViewModel
{
    public NewDatasetFormViewModel Form { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowMemberPane), nameof(CanChooseDataset), nameof(ShowChooseHint), nameof(ShowSequentialNote))]
    private bool _isCreating;

    private bool CanNewDataset => !IsBusy && !IsReviewingUpload && !IsCreating;
    private bool CanCreate => !IsBusy && IsCreating && Form.CanCreate;
    private bool CanCloseForm => !IsBusy && IsCreating;

    partial void OnIsCreatingChanged(bool value) => NotifyCommands();

    /// <summary>Called once, by the constructor: the Create button follows the form's own rules.</summary>
    private void WatchForm() => Form.PropertyChanged += OnFormPropertyChanged;

    private void OnFormPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NewDatasetFormViewModel.CanCreate)) CreateCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Opens the form: type and DCB from the chosen dataset, the name from the filter's first qualifier, the
    /// space as it was last sent.</summary>
    [RelayCommand(CanExecute = nameof(CanNewDataset))]
    private void NewDataset()
    {
        Form.Message = null;
        Form.Name = FirstQualifier(Filter) is { } hlq ? hlq + "." : "";
        if (SelectedDataset is { } dataset) Form.PrefillFrom(dataset.Attributes);
        IsCreating = true;
    }

    /// <summary>The filter's first qualifier when it is a plain one (<c>MVSCE02</c> of <c>MVSCE02.**</c>), else null.</summary>
    private static string? FirstQualifier(string filter)
    {
        var first = filter.Trim().ToUpperInvariant().Split('.')[0];
        return first.Length > 0 && !first.Contains('*') && !first.Contains('%') ? first : null;
    }

    [RelayCommand(CanExecute = nameof(CanCloseForm))]
    private void CloseForm() => HideForm();

    private void HideForm()
    {
        IsCreating = false;
        Form.Message = null;
    }

    /// <summary>A retry sends the form again, as it now reads; once the host has created the dataset, only the
    /// listing is retried.</summary>
    [RelayCommand(CanExecute = nameof(CanCreate))]
    private Task CreateAsync() => RunThenListAsync(CreateCoreAsync, () => CreateAsync());

    private async Task CreateCoreAsync(Action<Func<Task>> retryWith, CancellationToken token)
    {
        if (!IsCreating || !Form.CanCreate) return;
        var path = HostPath.ForDataset(Form.Name);
        var allocation = Form.Allocation;
        Form.Message = null;
        StatusText = $"⟳ Creating {path}…";
        try
        {
            await _connection.RunAsync(service => service.CreateDatasetAsync(path, allocation, token));
        }
        catch (HostFileException ex) when (ex.Kind is HostFileErrorKind.CannotAllocate or HostFileErrorKind.InvalidRequest)
        {
            // The values can be corrected and sent again, so the answer stays with them. The host cannot say which
            // value it disliked (compatibility log, create-failure-is-one-500), and the message says as much.
            Form.Message = "✗ " + HostFileMessages.Describe(ex);
            StatusText = "";
            return;
        }
        var what = $"Created {path}";
        retryWith(() => ListAgainAsync(path.Dataset, what));
        HideForm();
        await ShowAfterChangeAsync(path.Dataset, what, token);
    }
}
```

The comment names a compatibility-log tag as prose, which is allowed: it is not a `// mvsMF-compat:` marker and names no backend type.

- [ ] **Step 4: Wire the core and the other slices**

In `MvsmfBrowserViewModel.cs`:
- constructor: add `WatchForm();` as the last line.
- `ShowSequentialNote`: `SelectedDataset is { IsSequential: true } && !IsCreating`.
- `ShowChooseHint`: `SelectedDataset is not { IsSupported: true } && !IsCreating`.
- `NotifyCommands()`: add `NewDatasetCommand.NotifyCanExecuteChanged(); CreateCommand.NotifyCanExecuteChanged(); CloseFormCommand.NotifyCanExecuteChanged();`.

In `MvsmfBrowserViewModel.Uploads.cs`:
- `ShowMemberPane => ShowMembers && !IsReviewingUpload && !IsCreating;` and update its summary: "The member list gives way to the upload review or the New dataset form."
- `CanChooseDataset => !IsBusy && !IsReviewingUpload && !IsCreating;` and its summary: "…or a review or the form is open…".
- `CanUpload`: add `&& !IsCreating`.

`CanDownload` (Downloads.cs), `CanDelete` (Delete.cs), `CanLoadMoreDatasets` and `CanLoadMoreMembers` (Paging.cs), `CanManageDataset` and `CanRenameMember` (Manage.cs): add `&& !IsCreating` after `!IsReviewingUpload` (for `CanDownload`, after `!IsBusy`).

- [ ] **Step 5: Run the view-model tests**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~MvsmfBrowserCreateTests"`
Expected: 12 passed. Then `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~MvsmfBrowser"` to see the other browser suites still pass.

- [ ] **Step 6: Write the failing window tests**

Add to `tests/LizTerm.App.Tests/Views/MvsmfBrowserWindowTests.cs`:

```csharp
    [AvaloniaFact]
    public async Task New_opens_the_form_in_the_right_pane_with_the_focus_in_the_name_box()
    {
        var (window, t) = Show();
        await Wait.UntilAsync(() => t.Vm.Datasets.Count == 4, "the first listing");
        await t.ChooseAsync("MVSCE02.CNTL");
        var newButton = Named<Button>(window, "NewDatasetButton");
        Assert.Equal("New…", newButton.Content);
        Assert.True(newButton.IsEffectivelyEnabled);

        newButton.Command!.Execute(null);
        Dispatcher.UIThread.RunJobs();

        Assert.True(Named<DockPanel>(window, "CreatePane").IsVisible);
        Assert.False(Named<DockPanel>(window, "MemberPane").IsVisible);
        Assert.False(Named<ListBox>(window, "DatasetList").IsEffectivelyEnabled);
        Assert.False(newButton.IsEffectivelyEnabled);
        var name = Named<TextBox>(window, "NewNameBox");
        await Wait.UntilAsync(() => name.IsFocused, "the focus in the name box");
        Assert.Equal("MVSCE02.", name.Text);
        Assert.False(Named<Button>(window, "CreateButton").IsEffectivelyEnabled);

        name.Text = "MVSCE02.NEW";
        Dispatcher.UIThread.RunJobs();
        Assert.True(Named<Button>(window, "CreateButton").IsEffectivelyEnabled);
    }

    [AvaloniaFact]
    public async Task Escape_closes_the_form_before_the_window()
    {
        var (window, t) = Show();
        await Wait.UntilAsync(() => t.Vm.Datasets.Count == 4, "the first listing");
        t.Vm.NewDatasetCommand.Execute(null);
        Assert.True(t.Vm.IsCreating);

        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);

        Assert.False(t.Vm.IsCreating);
        Assert.True(window.IsVisible);
        Assert.True(Named<DockPanel>(window, "MemberPane").IsVisible || Named<TextBlock>(window, "ChooseHint").IsVisible);
    }

    [AvaloniaFact]
    public async Task Focus_returns_to_the_form_after_a_refused_create()
    {
        var (window, t) = Show();
        await Wait.UntilAsync(() => t.Vm.Datasets.Count == 4, "the first listing");
        t.Vm.NewDatasetCommand.Execute(null);
        t.Vm.Form.Name = "MVSCE02.CNTL";
        Dispatcher.UIThread.RunJobs();
        var name = Named<TextBox>(window, "NewNameBox");
        await Wait.UntilAsync(() => name.IsFocused, "the focus in the name box");

        await t.Vm.CreateCommand.ExecuteAsync(null);

        Assert.True(t.Vm.IsCreating);
        Assert.True(Named<TextBlock>(window, "CreateMessage").IsVisible);
        await Wait.UntilAsync(() => name.IsFocused, "the focus back in the name box");
    }
```

- [ ] **Step 7: Run them to verify they fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~MvsmfBrowserWindowTests.New_opens|FullyQualifiedName~MvsmfBrowserWindowTests.Escape_closes_the_form|FullyQualifiedName~MvsmfBrowserWindowTests.Focus_returns_to_the_form"`
Expected: FAIL, `NewDatasetButton` not found; Escape closes the window.

- [ ] **Step 8: The button, the pane, Escape and the focus**

In `MvsmfBrowserWindow.axaml`, make `NewDatasetButton` the first child of `DatasetButtons`:

```xml
          <Button x:Name="NewDatasetButton" Content="New…" Command="{Binding NewDatasetCommand}" />
```

In the right pane's `Panel`, after `ReviewPane`, add:

```xml
        <DockPanel x:Name="CreatePane" IsVisible="{Binding IsCreating}">
          <TextBlock DockPanel.Dock="Top" Text="New dataset" FontWeight="SemiBold" Margin="8,4" />
          <StackPanel DockPanel.Dock="Bottom" Spacing="6" Margin="8">
            <TextBlock x:Name="CreateMessage" Text="{Binding Form.Message}" Foreground="#FF8080" TextWrapping="Wrap"
                       IsVisible="{Binding Form.Message, Converter={x:Static ObjectConverters.IsNotNull}}" />
            <StackPanel Orientation="Horizontal" HorizontalAlignment="Right" Spacing="8">
              <Button x:Name="CloseFormButton" Content="Close" Command="{Binding CloseFormCommand}" />
              <Button x:Name="CreateButton" Content="Create" Command="{Binding CreateCommand}" />
            </StackPanel>
          </StackPanel>
          <ScrollViewer>
            <StackPanel Margin="8,0" Spacing="4" IsEnabled="{Binding IsIdle}">
              <StackPanel.Styles>
                <Style Selector="TextBlock.problem">
                  <Setter Property="Foreground" Value="#FF8080" />
                  <Setter Property="TextWrapping" Value="Wrap" />
                  <Setter Property="FontSize" Value="12" />
                </Style>
                <Style Selector="TextBox">
                  <Setter Property="FontFamily" Value="Menlo, Consolas, monospace" />
                </Style>
              </StackPanel.Styles>
              <TextBlock Text="Name" />
              <TextBox x:Name="NewNameBox" Text="{Binding Form.Name}" MaxLength="44" />
              <TextBlock Classes="problem" Text="{Binding Form.NameProblem}" IsVisible="{Binding Form.NameProblem, Converter={x:Static ObjectConverters.IsNotNull}}" />
              <TextBlock Text="Type" Margin="0,6,0,0" />
              <StackPanel Orientation="Horizontal" Spacing="12">
                <RadioButton x:Name="PartitionedButton" GroupName="NewType" Content="Partitioned (PDS)" IsChecked="{Binding Form.IsPartitioned}" />
                <RadioButton x:Name="SequentialButton" GroupName="NewType" Content="Sequential" IsChecked="{Binding Form.IsSequential}" />
              </StackPanel>
              <Grid ColumnDefinitions="*,8,*,8,*" Margin="0,6,0,0">
                <StackPanel Grid.Column="0" Spacing="4">
                  <TextBlock Text="RECFM" />
                  <TextBox x:Name="NewRecfmBox" Text="{Binding Form.Recfm}" MaxLength="4" />
                  <TextBlock Classes="problem" Text="{Binding Form.RecfmProblem}" IsVisible="{Binding Form.RecfmProblem, Converter={x:Static ObjectConverters.IsNotNull}}" />
                </StackPanel>
                <StackPanel Grid.Column="2" Spacing="4">
                  <TextBlock Text="LRECL" />
                  <TextBox x:Name="NewLreclBox" Text="{Binding Form.Lrecl}" MaxLength="5" />
                  <TextBlock Classes="problem" Text="{Binding Form.LreclProblem}" IsVisible="{Binding Form.LreclProblem, Converter={x:Static ObjectConverters.IsNotNull}}" />
                </StackPanel>
                <StackPanel Grid.Column="4" Spacing="4">
                  <TextBlock Text="BLKSIZE" />
                  <TextBox x:Name="NewBlksizeBox" Text="{Binding Form.Blksize}" MaxLength="5" />
                  <TextBlock Classes="problem" Text="{Binding Form.BlksizeProblem}" IsVisible="{Binding Form.BlksizeProblem, Converter={x:Static ObjectConverters.IsNotNull}}" />
                </StackPanel>
              </Grid>
              <TextBlock Text="Space" Margin="0,6,0,0" />
              <StackPanel Orientation="Horizontal" Spacing="12">
                <RadioButton x:Name="TracksButton" GroupName="NewSpace" Content="Tracks" IsChecked="{Binding Form.IsTracks}" />
                <RadioButton x:Name="CylindersButton" GroupName="NewSpace" Content="Cylinders" IsChecked="{Binding Form.IsCylinders}" />
              </StackPanel>
              <Grid ColumnDefinitions="*,8,*,8,*">
                <StackPanel Grid.Column="0" Spacing="4">
                  <TextBlock Text="Primary" />
                  <TextBox x:Name="NewPrimaryBox" Text="{Binding Form.Primary}" MaxLength="5" />
                  <TextBlock Classes="problem" Text="{Binding Form.PrimaryProblem}" IsVisible="{Binding Form.PrimaryProblem, Converter={x:Static ObjectConverters.IsNotNull}}" />
                </StackPanel>
                <StackPanel Grid.Column="2" Spacing="4">
                  <TextBlock Text="Secondary" />
                  <TextBox x:Name="NewSecondaryBox" Text="{Binding Form.Secondary}" MaxLength="5" />
                  <TextBlock Classes="problem" Text="{Binding Form.SecondaryProblem}" IsVisible="{Binding Form.SecondaryProblem, Converter={x:Static ObjectConverters.IsNotNull}}" />
                </StackPanel>
                <StackPanel Grid.Column="4" Spacing="4" IsEnabled="{Binding Form.IsPartitioned}">
                  <TextBlock Text="Directory blocks" />
                  <TextBox x:Name="NewDirectoryBlocksBox" Text="{Binding Form.DirectoryBlocks}" MaxLength="5" />
                  <TextBlock Classes="problem" Text="{Binding Form.DirectoryBlocksProblem}" IsVisible="{Binding Form.DirectoryBlocksProblem, Converter={x:Static ObjectConverters.IsNotNull}}" />
                </StackPanel>
              </Grid>
            </StackPanel>
          </ScrollViewer>
        </DockPanel>
```

In `MvsmfBrowserWindow.axaml.cs`:

- Escape, in `OnKeyDownTunnel`: after `else if (vm.IsReviewingUpload) vm.CloseReviewCommand.Execute(null);` add `else if (vm.IsCreating) vm.CloseFormCommand.Execute(null);`.
- The form's focus: in `OnViewModelPropertyChanged`, add a case:

```csharp
            case nameof(MvsmfBrowserViewModel.IsCreating) when vm.IsCreating:
                // The form's first box, like the filter box when the window opens; posted because the pane is
                // still hidden when the notification arrives.
                Dispatcher.UIThread.Post(() =>
                {
                    if (_watched is { IsCreating: true }) NewNameBox.Focus();
                }, DispatcherPriority.Loaded);
                break;
```

- `RememberFocus`: the keyboard may be in the form or on a button when an operation starts, so remember any focused control that is not a list row. Replace the `if (FilterBox.IsKeyboardFocusWithin) …` chain with:

```csharp
        if (FocusedList() is { } list)
        {
            _focusBefore = list;
            if (FocusManager?.GetFocusedElement() is Control focused
                && focused.FindAncestorOfType<ListBoxItem>(includeSelf: true) is { } container)
            {
                _focusedItemBefore = list.ItemFromContainer(container);
                _focusedIndexBefore = list.IndexFromContainer(container);
            }
        }
        else if (FocusManager?.GetFocusedElement() is Control other) _focusBefore = other;
```

`PostRestoreFocus` already calls `target.Focus()` for a non-list target, and a target the operation hid (a closed form's box) refuses the focus, which is the right outcome. Update the summary comment on `OnViewModelPropertyChanged` accordingly ("remembers where the keyboard was — the filter box, a list's row, or any other control, the form's boxes included").

- [ ] **Step 9: Run the window tests and the whole App suite**

Run: `dotnet test tests/LizTerm.App.Tests`
Expected: all pass, the earlier focus tests included.

- [ ] **Step 10: Commit**

```bash
git add src/LizTerm.App/ViewModels/MvsmfBrowserViewModel.Create.cs src/LizTerm.App/ViewModels/MvsmfBrowserViewModel.cs src/LizTerm.App/ViewModels/MvsmfBrowserViewModel.Uploads.cs src/LizTerm.App/ViewModels/MvsmfBrowserViewModel.Downloads.cs src/LizTerm.App/ViewModels/MvsmfBrowserViewModel.Delete.cs src/LizTerm.App/ViewModels/MvsmfBrowserViewModel.Paging.cs src/LizTerm.App/ViewModels/MvsmfBrowserViewModel.Manage.cs src/LizTerm.App/Views/MvsmfBrowserWindow.axaml src/LizTerm.App/Views/MvsmfBrowserWindow.axaml.cs tests/LizTerm.App.Tests/ViewModels/MvsmfBrowserCreateTests.cs tests/LizTerm.App.Tests/Views/MvsmfBrowserWindowTests.cs
git commit -m "Browser: New dataset, a form in the right pane sent as an explicit allocation

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 7: Documentation and the final checks

**Files:**
- Modify: `docs/user-guide.md` (the mvsMF Browser section, the known-limitations line)
- Modify: `CHANGELOG.md` (the mvsMF entry under `## Unreleased`)
- Modify: `src/LizTerm.App/CLAUDE.md` (the mvsMF Browser section)
- Modify: `tests/CLAUDE.md` (the `BrowserTestHost` paragraph)

**Interfaces:** none; the docs follow the code as built in Tasks 1 to 6. Read each file's section before editing it, and keep its voice.

- [ ] **Step 1: The user guide**

In `docs/user-guide.md`:

1. In **Browsing**, the keyboard paragraph: change "else closes an upload review, else closes the window." to "else closes an upload review or the New dataset form, else closes the window."

2. In **Uploading**, after the paragraph that ends "…is marked **⚠ Uploaded, but the host copy differs at line N**.", add:

```markdown
If you downloaded a member in this window and it has changed on the host since, the browser says so before
replacing it: **Replace anyway** writes your copy over the change, **Skip** leaves the member alone. The check
covers only members downloaded or uploaded through this browser window; anything else is replaced as before, and
a sequential dataset is checked the same way.
```

3. Replace the **Deleting** section with:

```markdown
### Deleting

**Delete…** in the bottom bar deletes the selected members, after a question that names them (**Delete 3
members**). A deleted member cannot be recovered. If you cancel part-way, the browser lists the members again and
says how many were deleted. **Delete…** under the dataset list deletes a whole dataset; see
[Managing datasets](#managing-datasets).
```

4. After **Deleting**, before **When something goes wrong**, add:

```markdown
### Managing datasets

The buttons under the dataset list create, rename and delete datasets.

**New…** opens a form in the right pane. With a dataset selected, the form starts from its type, RECFM, LRECL and
BLKSIZE, and the name starts with your filter's first qualifier, so a new library like the one selected is a name
and **Create** away. Space is in tracks or cylinders, a primary and a secondary amount, and directory blocks for a
partitioned dataset; the form keeps the space you last used while the window is open. A value the host cannot take
is marked **✗** under its box until you fix it.

mvsMF cannot say why an allocation failed: a name that already exists, no room on the volume and a missing
authorization all come back as the same **The host could not allocate it** message, which stays in the form so you
can change the values and try again. A record layout the host rejects (a BLKSIZE that is not a multiple of a
fixed LRECL, say) fails the same way.

**Rename…** under the dataset list renames the selected dataset, and **Rename…** in the bottom bar renames the
selected member. Both ask for the new name in the strip at the bottom of the window, where **Rename** is enabled
once the name is different and acceptable, and **Enter** confirms. A renamed dataset your filter does not show is
reported in the status line. The host refuses to rename a member onto a name that already exists; renaming a
dataset onto an existing name fails with a server error.

**Delete…** under the dataset list deletes the selected dataset, after a question that names it and, for a
partitioned dataset, says how many members it has. This cannot be undone.
```

5. In **Known limitations**, change the mvsMF bullet's second sentence to: "It can't submit jobs or browse the z/OS UNIX file system."

- [ ] **Step 2: The changelog**

In `CHANGELOG.md`, under `## Unreleased`, the **mvsMF Browser (feature preview)** entry: after "…the member filter is the host's work." add: "It can create, rename and delete datasets and members, and warns before replacing a member that changed on the host since you downloaded it."

- [ ] **Step 3: `src/LizTerm.App/CLAUDE.md`**

In the "mvsMF Browser" section, edit and add bullets so they follow the code:

- The `MvsmfBrowserViewModel` bullet ("runs one operation at a time…"): add "A question with a text box (`ConfirmationRequest` with `input` and an `inputRule`) is how a rename asks; its primary is allowed only for an acceptable, changed value, and `Primary()` checks that itself because the window's Enter goes through `Execute`."
- After the **Delete** bullet, add:

```markdown
- **Manage** (`Manage.cs`): Rename… and Delete… under the dataset list act on any listed dataset whose name the
  rules accept, opened or not; Rename… in the bottom bar needs exactly one selected member. A member rename
  reloads the list and selects the new name through `SelectMemberRequested`, which the window applies to its list
  box (the list box owns the selection, so the view model never sets it directly); a `NotFound` means the member
  went meanwhile, so the list is reloaded before the status line. A dataset rename or a create lists the filter
  again and chooses the new name if it shows (`ShowAfterChangeAsync`); `SelectAsync` exists because setting
  `SelectedDataset` inside a running operation starts no member load of its own (`RunExclusiveAsync` ignores a
  second operation). `RunThenListAsync` swaps the retry to the listing once the host has done the first half, so
  Retry after a failed listing never renames or creates twice. The delete-dataset question carries the loaded
  member count, with a plus while the host has more or a host-side member filter is in force.
- **Create** (`Create.cs`, `NewDatasetFormViewModel`): the form is one instance per window, so the space values it
  was last sent with are kept; opening it prefills type and DCB from the chosen dataset and the name from the
  filter's first qualifier. Every field is a string; a numeric field that is not a whole number is checked as
  `-1` and its own "Enter a whole number." wins. A `CannotAllocate` or `InvalidRequest` stays in `Form.Message`
  with the form open; a connection failure's Retry sends the form as it now reads. While the form is open every
  other operation is off (`!IsCreating` in each rule) and the right pane shows the form in place of the member
  list, the review, the hint and the sequential note.
- **ETag memory** (`HostFileAccess.Etags`, `EtagMemory`): a download remembers the stamp once the file is in
  place; a write remembers the write's stamp; a member delete forgets, a dataset delete forgets everything under
  it, a rename moves. An upload sends the stamp as `ifMatch` only for a member it is replacing; a `Conflict` is
  the question `NAME changed on the host since you downloaded it.` with Replace anyway / Skip (Cancel stops the
  batch; a sequential dataset offers Replace anyway or Cancel), and Replace anyway sends again with no stamp. A
  member never downloaded or written here sends nothing and gets the old behaviour.
```

- The Escape sentence in the keyboard bullet: "Escape cancels a question, else the running operation, else an open upload review or the New dataset form, else closes the window." Add: "A question with a text box takes the focus to the box with the old name selected, and Enter in it is the primary."
- The **Focus survives an operation** bullet: "the window remembers whether the filter box, the dataset list or the member list had the focus" becomes "the window remembers which control had the focus — a list's row with its index, or any other control (the filter box, the form's boxes, a button)". Add: "Opening the form focuses its name box, posted at `Loaded` priority like a question's."

- [ ] **Step 4: `tests/CLAUDE.md`**

In the `FakeHostFileService` paragraph, change "`Calls` records `list:<pattern>`, …" to say that a `list:<pattern>` returns only the datasets the pattern matches (`**` any qualifiers, `*` within a qualifier, `%` one character, case ignored), so a test that wants a dataset "not shown by the filter" names it under another first qualifier. In the `BrowserTestHost` paragraph, add: "`Access` is the `HostFileAccess`, so `Access.Etags` reads the stamp memory. A window test can put a question up without an operation by setting `Vm.Confirmation` directly."

- [ ] **Step 5: Run everything**

```bash
dotnet build LizTerm.slnx --no-incremental 2>&1 | grep -c " warning "
```
Expected: `0`.

```bash
dotnet test LizTerm.slnx
```
Expected: every project green; the live-host tests skip themselves. `RepositoryHeadersTests` and `UserGuideTests` pass.

- [ ] **Step 6: Commit**

```bash
git add docs/user-guide.md CHANGELOG.md src/LizTerm.App/CLAUDE.md tests/CLAUDE.md
git commit -m "Docs: managing datasets in the browser, the conflict question, the notes follow the code

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

## Before the PR

Not tasks for an implementer; the controller does these after the final review.

1. The live lane still passes (it exercises the backend the browser now calls): in one shell,
   `export LIZTERM_MVSMF_URL=http://10.42.37.209:8080/zosmf LIZTERM_MVSMF_USER=IBMUSER LIZTERM_MVSMF_PASSWORD="$(awk '{print $6}' ~/.mvsmf-netrc)" LIZTERM_MVSMF_SCRATCH_PDS=MVSCE02.CNTL` then `dotnet test tests/LizTerm.Integration.Tests --filter "FullyQualifiedName~LiveMvsmfTests"`; 7 pass. Never echo the password.
2. The hands-on pass against MVS/CE (spec §7, last line): with the app on the MVS/CE profile, under `IBMUSER.LIZITEST.*`: New… a PDS from `MVSCE02.CNTL` and a sequential dataset; Create with a name that exists (the message stays in the form); Rename… a member to an existing name, to a new name, and a dataset in and out of the filter; download a member, change it on the host (`curl -sS --netrc-file ~/.mvsmf-netrc -X PUT --data-binary 'changed' "$LIZTERM_MVSMF_URL/restfiles/ds/<dsn>(<member>)"`, no JSON content type), upload it and see the conflict question, Replace anyway, then Skip; Delete… the scratch datasets. Afterwards `curl -sS --netrc-file ~/.mvsmf-netrc "$LIZTERM_MVSMF_URL/restfiles/ds?dslevel=IBMUSER.LIZITEST.**"` lists nothing.
3. Push, open the PR against `main`, and after it merges: the status comment on #17.
