# mvsMF Browser (PR 2 of 3) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give a session window with an mvsMF URL a **File > mvsMF Browser...** window that lists datasets and members, downloads (several at once), uploads with pre-flight checks, deletes members, signs in once per session and offers to pin an untrusted certificate; and give the profile editor an **mvsMF (Preview)** group.

**Architecture:** The App names the mvsMF backend in one place, `HostFileServiceFactory`. A session-scoped `HostFileAccess` (credential holder + session certificate pin) is attached to the session window by `App.OpenSession`. Each browser window gets a `HostFileConnection` (service + certificate-trust retry) and a `MvsmfBrowserViewModel`. The browser is owned by its session window, shown without blocking it (`ShowAbove`), follows its Keep on Top, and closes with it.

**Tech Stack:** .NET 10, Avalonia 12.1.2, CommunityToolkit.Mvvm 8.4, xunit.v3 + Avalonia.Headless.XUnit.

**Spec:** `docs/superpowers/specs/2026-09-16-lizterm-mvsmf-dataset-browser-design.md` — read §3.2, §3.3, §3.4, §4, §5, §6, §7. PR 1 (`docs/superpowers/plans/2026-09-16-mvsmf-backend.md`, PR #133) built `LizTerm.Core.HostFiles` and `LizTerm.Backend.Mvsmf`; read `src/LizTerm.Backend.Mvsmf/CLAUDE.md` and the `Host files` section of `src/LizTerm.Core/CLAUDE.md`.

## Global Constraints

- Every hand-written `.cs`, `.axaml` and `.sh` file starts with the three licence lines (`This file is part of LizTerm.` / `Copyright 2026 by CoffeeMuse` / `SPDX-License-Identifier: BSD-3-Clause`), in the file's comment syntax, before the root element in `.axaml`.
- `LizTerm.Core` never names mvsMF, Avalonia or b3270. `LizTerm.App` names the mvsMF backend (`LizTerm.Backend.Mvsmf`) only in `src/LizTerm.App/HostFileServiceFactory.cs`; the App tests name it only in `tests/LizTerm.App.Tests/HostFileServiceFactoryTests.cs`.
- Every modal dialog opens through `ShowDialogAbove` (`ModalDialogsTests` enforces it); the browser, which does not block its owner, opens through the new `ShowAbove`.
- Colour never carries meaning alone: every success/failure/warning line starts with a mark and words (`✓`, `✗`, `⚠`, `⟳`, `–`).
- No menu item outside Edit gets a `Gesture`/`InputGesture`; **mvsMF Browser...** has no shortcut.
- View models marshal through the injected `Action<Action> dispatch`; tests pass `a => a()`.
- Credentials never appear in messages, logs, `ToString()` or files; `HostCredentials` is only held by `CredentialHolder`.
- No new NuGet packages. Zero warnings: `dotnet build LizTerm.slnx --no-incremental 2>&1 | grep -c " warning "` prints `0`.
- Menu header text is exactly `mvsMF _Browser...` in both menus. Window title is exactly `mvsMF Browser — {profile name} (Preview)`.
- Commits end with exactly `Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>`.

## Prerequisites

- Branch `claude/mvsmf-browser`, created from `claude/mvsmf-backend` (PR #133), already exists in this worktree.
- No live host is needed; nothing in this plan runs the live lane.

## File Structure

| File | Responsibility |
|---|---|
| `src/LizTerm.Core/Session/SessionProfile.cs` | + `HostFilesUrl`, `HostFilesUserid`, `HostFilesPinnedCertificate` |
| `src/LizTerm.Core/Profiles/PinMerge.cs` | + `ResolveHostFiles`, `Apply` |
| `src/LizTerm.Core/Profiles/ProfileStore.cs` | drop a malformed mvsMF pin on load |
| `src/LizTerm.App/Files/IFilePicker.cs`, `AvaloniaFilePicker.cs` | + several files, + folder |
| `src/LizTerm.App/Dialogs/ICredentialPrompt.cs`, `AvaloniaCredentialPrompt.cs` | sign-in prompt seam |
| `src/LizTerm.App/Views/SignInWindow.axaml(.cs)` | sign-in dialog |
| `src/LizTerm.App/Dialogs/ModalDialogs.cs` | + `ShowAbove` for owned non-blocking windows |
| `src/LizTerm.App/HostFiles/CredentialHolder.cs` | one sign-in per session, serialised prompts |
| `src/LizTerm.App/HostFileServiceFactory.cs` | the one place naming the mvsMF backend |
| `src/LizTerm.App/HostFiles/HostFileMessages.cs` | error kind → plain words |
| `src/LizTerm.App/HostFiles/HostFileAccess.cs` | session-scoped: URL, holder, session pin |
| `src/LizTerm.App/HostFiles/HostFileConnection.cs` | browser-scoped: service, certificate trust + retry |
| `src/LizTerm.App/ViewModels/ConfirmationRequest.cs` | inline confirmation strip state |
| `src/LizTerm.App/ViewModels/MvsmfBrowserRows.cs` | `DatasetRow`, `MemberRow`, `UploadRow` |
| `src/LizTerm.App/ViewModels/MvsmfBrowserViewModel.cs` (+ `.Downloads.cs`, `.Uploads.cs`, `.Delete.cs`) | the browser |
| `src/LizTerm.App/Views/MvsmfBrowserWindow.axaml(.cs)` | the browser window |
| `src/LizTerm.App/Views/SessionWindow.axaml(.cs)`, `App.axaml.cs`, `ViewModels/SessionViewModel.cs` | menu items, wiring |
| `src/LizTerm.App/ViewModels/ProfileEditorViewModel.cs`, `ProfileEdit.cs`, `Views/ProfileEditorWindow.axaml(.cs)`, `ViewModels/ProfilePickerViewModel.cs` | editor group |
| `tests/LizTerm.App.Tests/Fakes/FakeCredentialPrompt.cs`, `FakeHostFileService.cs`, `FakeFilePicker.cs` | fakes |
| docs: `src/LizTerm.App/CLAUDE.md`, `tests/CLAUDE.md`, `CLAUDE.md`, `docs/architecture.md`, `docs/mvsmf-compatibility.md`, `src/LizTerm.Backend.Mvsmf/CLAUDE.md` | notes |

---

### Task 1: Profile fields and pin merge (Core)

**Files:**
- Modify: `src/LizTerm.Core/Session/SessionProfile.cs`, `src/LizTerm.Core/Profiles/PinMerge.cs`, `src/LizTerm.Core/Profiles/ProfileStore.cs:67-68`
- Test: `tests/LizTerm.Core.Tests/Profiles/PinMergeTests.cs`, `tests/LizTerm.Core.Tests/Profiles/ProfileStoreTests.cs`

**Interfaces:**
- Produces: `SessionProfile.HostFilesUrl` (`string?`, default null), `SessionProfile.HostFilesUserid` (`string?`), `SessionProfile.HostFilesPinnedCertificate` (`CertificatePin?`); `PinMerge.ResolveHostFiles(SessionProfile edited, SessionProfile? onDisk, bool pinCleared) : CertificatePin?`; `PinMerge.Apply(SessionProfile edited, SessionProfile? onDisk, bool pinCleared, bool hostFilesPinCleared) : SessionProfile`.

The REST pin belongs to the URL it was taken from, as the 3270 pin belongs to host and port. The fields use Core's
host-neutral `HostFiles` vocabulary, not the product name: PR 1 made "Core never names mvsMF" a checked rule (the tag
check greps Core's `.cs` files), and `SessionProfile` is Core. The App's editor and browser say "mvsMF" to the user.

- [ ] **Step 1: Write the failing tests**

Append to `tests/LizTerm.Core.Tests/Profiles/PinMergeTests.cs` (inside the class):

```csharp
    private static SessionProfile Rest(string? url = "http://mvs:8080/zosmf", CertificatePin? pin = null) =>
        new() { Name = "MVS", Host = "mvs", HostFilesUrl = url, HostFilesPinnedCertificate = pin };

    [Fact]
    public void A_rest_pin_written_since_the_editor_opened_is_carried_forward() =>
        Assert.Equal(Fresh, PinMerge.ResolveHostFiles(Rest(pin: null), Rest(pin: Fresh), pinCleared: false));

    [Fact]
    public void Forget_clears_the_rest_pin_even_when_disk_still_has_one() =>
        Assert.Null(PinMerge.ResolveHostFiles(Rest(pin: null), Rest(pin: Fresh), pinCleared: true));

    [Fact]
    public void Repointing_the_rest_url_drops_its_pin() =>
        Assert.Null(PinMerge.ResolveHostFiles(Rest(url: "https://proxy/zosmf"), Rest(pin: Fresh), pinCleared: false));

    [Fact]
    public void A_rest_url_differing_only_in_case_or_a_trailing_slash_keeps_its_pin() =>
        Assert.Equal(Fresh, PinMerge.ResolveHostFiles(Rest(url: "HTTP://MVS:8080/zosmf/"), Rest(pin: Fresh), pinCleared: false));

    [Fact]
    public void Without_a_file_the_editors_rest_pin_stands() =>
        Assert.Equal(Stale, PinMerge.ResolveHostFiles(Rest(pin: Stale), onDisk: null, pinCleared: false));

    [Fact]
    public void Apply_resolves_both_pins_independently()
    {
        var edited = new SessionProfile { Name = "MVS", Host = "mvs", Port = 3270, HostFilesUrl = "http://mvs:8080/zosmf" };
        var onDisk = edited with { PinnedCertificate = Fresh, HostFilesPinnedCertificate = Stale };

        var merged = PinMerge.Apply(edited, onDisk, pinCleared: true, hostFilesPinCleared: false);

        Assert.Null(merged.PinnedCertificate);
        Assert.Equal(Stale, merged.HostFilesPinnedCertificate);
        Assert.Equal(edited with { HostFilesPinnedCertificate = Stale }, merged);
    }
```

Append to `tests/LizTerm.Core.Tests/Profiles/ProfileStoreTests.cs` (inside the class; `_dir` is the class's temp directory field, as the neighbouring tests use it):

```csharp
    [Fact]
    public void The_host_files_fields_round_trip_and_default_to_null()
    {
        var store = new ProfileStore(_dir);
        var pin = new CertificatePin("AA:BB", "CN=proxy", "-----BEGIN CERTIFICATE-----\nAA==\n-----END CERTIFICATE-----\n");
        var full = new SessionProfile { Name = "rest", Host = "mvs", HostFilesUrl = "https://proxy/zosmf", HostFilesUserid = "IBMUSER", HostFilesPinnedCertificate = pin };
        var plain = new SessionProfile { Name = "plain", Host = "mvs" };
        store.Save(full);
        store.Save(plain);

        Assert.Equal(full, store.Load("rest"));
        Assert.Equal(plain, store.Load("plain"));
        var text = File.ReadAllText(Directory.GetFiles(_dir).Single(f => File.ReadAllText(f).Contains("\"plain\"")));
        Assert.Contains(""hostFilesUrl": null", text);
        Assert.Contains("\"hostFilesPinnedCertificate\": null", text);
    }

    [Fact]
    public void A_file_without_host_files_fields_reads_them_as_null()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "old.json"), """{"name":"old","host":"h"}""");
        var loaded = new ProfileStore(_dir).Load("old")!;
        Assert.Null(loaded.HostFilesUrl);
        Assert.Null(loaded.HostFilesUserid);
        Assert.Null(loaded.HostFilesPinnedCertificate);
    }

    [Fact]
    public void A_host_files_pin_missing_its_pem_or_fingerprint_is_dropped_on_load()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "bad.json"), """{"name":"bad","host":"h","hostFilesUrl":"http://h/zosmf","hostFilesPinnedCertificate":{"sha256":"AA","subject":"CN=x","pem":""}}""");
        var loaded = new ProfileStore(_dir).Load("bad")!;
        Assert.Null(loaded.HostFilesPinnedCertificate);
        Assert.Equal("http://h/zosmf", loaded.HostFilesUrl);
    }
```

If `ProfileStoreTests` does not already import `LizTerm.Core.Session`, add the using.

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~PinMergeTests|FullyQualifiedName~ProfileStoreTests"`
Expected: build FAILS (`HostFilesUrl` not found).

- [ ] **Step 3: Implement**

In `SessionProfile.cs`, append three parameters after `string? Note = null` and document them in the summary's `<param>` list:

```csharp
    string? Note = null,
    string? HostFilesUrl = null,
    string? HostFilesUserid = null,
    CertificatePin? HostFilesPinnedCertificate = null);
```

```csharp
/// <param name="HostFilesUrl">The z/OSMF REST base URL (normalised, e.g. <c>http://host:8080/zosmf</c>) that opens the
/// dataset browser for this profile, or null for none.</param>
/// <param name="HostFilesUserid">The userid the REST sign-in prompt starts with, or null. Never a password: the
/// password is asked for once per session and held in memory only.</param>
/// <param name="HostFilesPinnedCertificate">The certificate trusted for <paramref name="HostFilesUrl"/> when it is https,
/// independent of <paramref name="PinnedCertificate"/>, which belongs to the 3270 host and port.</param>
```

In `PinMerge.cs`, add below `Resolve`:

```csharp
    /// <summary>The REST pin's twin of <see cref="Resolve"/>: the same three cases, keyed on the REST URL instead of
    /// the host and port. URLs compare ignoring case and a trailing slash, the two differences the editor's own
    /// normalisation can leave.</summary>
    public static CertificatePin? ResolveHostFiles(SessionProfile edited, SessionProfile? onDisk, bool pinCleared)
    {
        if (pinCleared) return null;
        if (onDisk?.HostFilesPinnedCertificate is not { } stored) return edited.HostFilesPinnedCertificate;
        return SameUrl(edited.HostFilesUrl, onDisk.HostFilesUrl) ? stored : null;
    }

    /// <summary>Both merges applied to what the editor produced: the one call every editor save site makes.</summary>
    public static SessionProfile Apply(SessionProfile edited, SessionProfile? onDisk, bool pinCleared, bool hostFilesPinCleared) =>
        edited with
        {
            PinnedCertificate = Resolve(edited, onDisk, pinCleared),
            HostFilesPinnedCertificate = ResolveHostFiles(edited, onDisk, hostFilesPinCleared),
        };

    internal static bool SameUrl(string? first, string? second) =>
        string.Equals(first?.Trim().TrimEnd('/'), second?.Trim().TrimEnd('/'), StringComparison.OrdinalIgnoreCase);
```

In `ProfileStore.cs`, after the existing pin repair (line 67-68), add:

```csharp
        if (profile.HostFilesPinnedCertificate is { } restPin && (string.IsNullOrWhiteSpace(restPin.Pem) || string.IsNullOrWhiteSpace(restPin.Sha256)))
            profile = profile with { HostFilesPinnedCertificate = null };
```

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test tests/LizTerm.Core.Tests`
Expected: PASS (all).

- [ ] **Step 5: Commit**

```bash
git add src/LizTerm.Core tests/LizTerm.Core.Tests
git commit -m "Add the mvsMF URL, userid and pin to session profiles

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 2: Pick several files, and a folder

**Files:**
- Modify: `src/LizTerm.App/Files/IFilePicker.cs`, `src/LizTerm.App/Files/AvaloniaFilePicker.cs`, `tests/LizTerm.App.Tests/Fakes/FakeFilePicker.cs`
- Test: `tests/LizTerm.App.Tests/Files/FakeFilePickerTests.cs` (new)

**Interfaces:**
- Produces: `IFilePicker.PickFilesToSendAsync(string title) : Task<IReadOnlyList<string>>` (empty when cancelled); `IFilePicker.PickFolderAsync(string title) : Task<string?>`. `FakeFilePicker.Results` (`IReadOnlyList<string>`, default empty), `FakeFilePicker.FolderResult` (`string?`), calls logged as `"open-many:<title>"` and `"folder:<title>"`.

- [ ] **Step 1: Write the failing test**

`tests/LizTerm.App.Tests/Files/FakeFilePickerTests.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.App.Tests.Fakes;

namespace LizTerm.App.Tests.Files;

/// <summary>The fake's contract for the two pickers the mvsMF Browser adds; the Avalonia side is a thin call into
/// the platform's storage provider and has no headless test.</summary>
public class FakeFilePickerTests
{
    [Fact]
    public async Task Several_files_and_a_folder_come_back_as_set_and_are_logged()
    {
        var picker = new FakeFilePicker { Results = ["/a.jcl", "/b.jcl"], FolderResult = "/out" };

        Assert.Equal(new[] { "/a.jcl", "/b.jcl" }, await picker.PickFilesToSendAsync("Upload to X"));
        Assert.Equal("/out", await picker.PickFolderAsync("Download to"));
        Assert.Equal(new[] { "open-many:Upload to X", "folder:Download to" }, picker.Calls);
    }

    [Fact]
    public async Task The_exception_fails_the_new_pickers_too()
    {
        var picker = new FakeFilePicker { Exception = new IOException("no dialog") };
        await Assert.ThrowsAsync<IOException>(() => picker.PickFilesToSendAsync("t"));
        await Assert.ThrowsAsync<IOException>(() => picker.PickFolderAsync("t"));
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~FakeFilePickerTests"`
Expected: build FAILS (`Results` not found).

- [ ] **Step 3: Implement**

`IFilePicker.cs` — add inside the interface:

```csharp
    /// <summary>OS Open dialog allowing several files. Empty when cancelled; files with no local path are left out.</summary>
    Task<IReadOnlyList<string>> PickFilesToSendAsync(string title);

    /// <summary>OS folder chooser. Null when cancelled or when the choice has no local path.</summary>
    Task<string?> PickFolderAsync(string title);
```

`AvaloniaFilePicker.cs` — add inside the class:

```csharp
    public async Task<IReadOnlyList<string>> PickFilesToSendAsync(string title)
    {
        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = true,
        });
        return [.. files.Select(f => f.TryGetLocalPath()).OfType<string>()];
    }

    public async Task<string?> PickFolderAsync(string title)
    {
        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
        });
        return folders.Count == 0 ? null : folders[0].TryGetLocalPath();
    }
```

`FakeFilePicker.cs` — add:

```csharp
    /// <summary>What the several-files dialog returns; empty plays a cancelled dialog.</summary>
    public IReadOnlyList<string> Results { get; set; } = [];

    /// <summary>What the folder dialog returns; null plays a cancelled dialog.</summary>
    public string? FolderResult { get; set; }

    public Task<IReadOnlyList<string>> PickFilesToSendAsync(string title)
    {
        Calls.Add("open-many:" + title);
        return Exception is not null ? Task.FromException<IReadOnlyList<string>>(Exception) : Task.FromResult(Results);
    }

    public Task<string?> PickFolderAsync(string title)
    {
        Calls.Add("folder:" + title);
        return Exception is not null ? Task.FromException<string?>(Exception) : Task.FromResult(FolderResult);
    }
```

Also extend the class's `Calls` doc comment to list the two new entries.

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~FakeFilePickerTests|FullyQualifiedName~FileTransfer"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/LizTerm.App/Files tests/LizTerm.App.Tests
git commit -m "Let the file picker choose several files or a folder

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 3: Sign-in prompt

**Files:**
- Create: `src/LizTerm.App/Dialogs/ICredentialPrompt.cs`, `src/LizTerm.App/Dialogs/AvaloniaCredentialPrompt.cs`, `src/LizTerm.App/Views/SignInWindow.axaml`, `src/LizTerm.App/Views/SignInWindow.axaml.cs`, `tests/LizTerm.App.Tests/Fakes/FakeCredentialPrompt.cs`
- Test: `tests/LizTerm.App.Tests/Views/SignInWindowTests.cs`

**Interfaces:**
- Consumes: `LizTerm.Core.HostFiles.HostCredentials(string userid, string password)`.
- Produces: `ICredentialPrompt.AskAsync(CredentialPromptRequest request) : Task<HostCredentials?>` (null = cancelled); `CredentialPromptRequest(string ProfileName, string Url, string? Userid, bool IsRetry)`; `AvaloniaCredentialPrompt(Window owner)`; `SignInWindow(CredentialPromptRequest)` with named controls `HostText`, `RetryText`, `UseridBox`, `PasswordBox`, `MissingText`, `SignInButton`, `CancelButton`, and `internal void SignIn()`; `FakeCredentialPrompt` with `Answers` (`Queue<HostCredentials?>`), `Answer` (used when the queue is empty), `Calls` (`"ask:<userid>:<IsRetry>"`), `LastRequest`, `Gate` (`TaskCompletionSource?` awaited before answering), `AskCount`.

- [ ] **Step 1: Write the fake and the failing tests**

`tests/LizTerm.App.Tests/Fakes/FakeCredentialPrompt.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.App.Dialogs;
using LizTerm.Core.HostFiles;

namespace LizTerm.App.Tests.Fakes;

public sealed class FakeCredentialPrompt : ICredentialPrompt
{
    private readonly object _lock = new();
    private int _askCount;

    /// <summary>Answers in order; when empty, <see cref="Answer"/>. A null answer plays Cancel.</summary>
    public Queue<HostCredentials?> Answers { get; } = new();
    public HostCredentials? Answer { get; set; } = new("MVSCE02", "pw");
    /// <summary>"ask:&lt;userid&gt;:&lt;IsRetry&gt;" per call.</summary>
    public List<string> Calls { get; } = [];
    public CredentialPromptRequest? LastRequest { get; private set; }
    /// <summary>When set, every ask waits for it: a test holds a prompt open to see what else happens meanwhile.</summary>
    public TaskCompletionSource? Gate { get; set; }
    public int AskCount => Volatile.Read(ref _askCount);

    public async Task<HostCredentials?> AskAsync(CredentialPromptRequest request)
    {
        Interlocked.Increment(ref _askCount);
        lock (_lock)
        {
            Calls.Add($"ask:{request.Userid}:{request.IsRetry}");
            LastRequest = request;
        }
        if (Gate is { } gate) await gate.Task;
        lock (_lock) return Answers.Count > 0 ? Answers.Dequeue() : Answer;
    }
}
```

`tests/LizTerm.App.Tests/Views/SignInWindowTests.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using LizTerm.App.Dialogs;
using LizTerm.App.Views;
using LizTerm.Core.HostFiles;

namespace LizTerm.App.Tests.Views;

public class SignInWindowTests
{
    private static SignInWindow Show(string? userid = "MVSCE02", bool retry = false)
    {
        var window = new SignInWindow(new CredentialPromptRequest("MVS/CE", "http://mvs:8080/zosmf", userid, retry));
        window.Show();
        return window;
    }

    [AvaloniaFact]
    public void Shows_the_profile_and_url_and_prefills_the_userid()
    {
        var window = Show();
        Assert.Equal("MVS/CE · http://mvs:8080/zosmf", window.FindControl<TextBlock>("HostText")!.Text);
        Assert.Equal("MVSCE02", window.FindControl<TextBox>("UseridBox")!.Text);
        Assert.False(window.FindControl<TextBlock>("RetryText")!.IsVisible);
        Assert.Equal('•', window.FindControl<TextBox>("PasswordBox")!.PasswordChar);
        Assert.Equal("Sign in to mvsMF", window.Title);
    }

    [AvaloniaFact]
    public void A_retry_says_the_host_refused_with_a_mark_and_words()
    {
        var text = Show(retry: true).FindControl<TextBlock>("RetryText")!;
        Assert.True(text.IsVisible);
        Assert.Equal("✗ The host rejected the userid or password. Try again.", text.Text);
    }

    [AvaloniaFact]
    public async Task Sign_in_returns_the_upper_cased_userid_and_the_password()
    {
        var owner = new Window();
        owner.Show();
        var window = new SignInWindow(new CredentialPromptRequest("MVS/CE", "http://mvs/zosmf", null, false));
        var result = window.ShowDialogAbove<HostCredentials?>(owner);
        window.FindControl<TextBox>("UseridBox")!.Text = " ibmuser ";
        window.FindControl<TextBox>("PasswordBox")!.Text = "secret";

        window.SignIn();

        var credentials = await result;
        Assert.Equal("IBMUSER", credentials!.Userid);
        Assert.Equal("secret", credentials.Password);
    }

    [AvaloniaFact]
    public void A_blank_userid_or_password_keeps_the_window_open_and_says_why()
    {
        var window = Show(userid: null);
        window.FindControl<TextBox>("PasswordBox")!.Text = "secret";

        window.SignIn();

        Assert.True(window.IsVisible);
        Assert.True(window.FindControl<TextBlock>("MissingText")!.IsVisible);
        Assert.Equal("Enter the userid and the password.", window.FindControl<TextBlock>("MissingText")!.Text);
    }

    [AvaloniaFact]
    public async Task Cancel_and_the_title_bar_both_answer_null()
    {
        var owner = new Window();
        owner.Show();
        var cancelled = new SignInWindow(new CredentialPromptRequest("p", "u", "U", false));
        var first = cancelled.ShowDialogAbove<HostCredentials?>(owner);
        cancelled.FindControl<Button>("CancelButton")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Null(await first);

        var closed = new SignInWindow(new CredentialPromptRequest("p", "u", "U", false));
        var second = closed.ShowDialogAbove<HostCredentials?>(owner);
        closed.Close();
        Assert.Null(await second);
    }

    [AvaloniaFact]
    public async Task The_avalonia_prompt_opens_the_window_over_its_owner_and_maps_close_to_null()
    {
        var owner = new Window();
        owner.Show();
        var asking = new AvaloniaCredentialPrompt(owner).AskAsync(new CredentialPromptRequest("p", "u", "U", false));

        var dialog = Assert.IsType<SignInWindow>(Assert.Single(owner.OwnedWindows));
        dialog.Close();

        Assert.Null(await asking);
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~SignInWindowTests"`
Expected: build FAILS (`ICredentialPrompt` not found).

- [ ] **Step 3: Implement**

`src/LizTerm.App/Dialogs/ICredentialPrompt.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.Core.HostFiles;

namespace LizTerm.App.Dialogs;

/// <summary>Asks for the REST sign-in. Injected like <see cref="ICertificatePrompt"/> so tests answer without a
/// window. Null means the user cancelled.</summary>
public interface ICredentialPrompt
{
    Task<HostCredentials?> AskAsync(CredentialPromptRequest request);
}

/// <param name="ProfileName">Whose sign-in this is.</param>
/// <param name="Url">The REST base URL, shown so the user knows which host is asking.</param>
/// <param name="Userid">The userid to start with, or null.</param>
/// <param name="IsRetry">The host has just refused the previous answer.</param>
public sealed record CredentialPromptRequest(string ProfileName, string Url, string? Userid, bool IsRetry);
```

`src/LizTerm.App/Dialogs/AvaloniaCredentialPrompt.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Controls;
using LizTerm.App.Views;
using LizTerm.Core.HostFiles;

namespace LizTerm.App.Dialogs;

/// <summary>Opens <see cref="SignInWindow"/> modally over its owner, which is the mvsMF Browser or the profile
/// editor. Closing the window any way but Sign In answers null.</summary>
public sealed class AvaloniaCredentialPrompt(Window owner) : ICredentialPrompt
{
    public Task<HostCredentials?> AskAsync(CredentialPromptRequest request) =>
        new SignInWindow(request).ShowDialogAbove<HostCredentials?>(owner);
}
```

`src/LizTerm.App/Views/SignInWindow.axaml`:

```xml
<!--
  This file is part of LizTerm.
  Copyright 2026 by CoffeeMuse
  SPDX-License-Identifier: BSD-3-Clause
-->
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        x:Class="LizTerm.App.Views.SignInWindow"
        Title="Sign in to mvsMF" Width="420" SizeToContent="Height" CanResize="False"
        WindowStartupLocation="CenterOwner">
  <StackPanel Margin="16" Spacing="10">
    <TextBlock x:Name="HostText" FontWeight="SemiBold" TextWrapping="Wrap" />
    <TextBlock x:Name="RetryText" Foreground="#FF8080" TextWrapping="Wrap"
               Text="✗ The host rejected the userid or password. Try again." />
    <Grid ColumnDefinitions="80,*" RowDefinitions="Auto,Auto" RowSpacing="8">
      <TextBlock Grid.Row="0" Grid.Column="0" Text="Userid" VerticalAlignment="Center" />
      <TextBox Grid.Row="0" Grid.Column="1" x:Name="UseridBox" MaxLength="8" />
      <TextBlock Grid.Row="1" Grid.Column="0" Text="Password" VerticalAlignment="Center" />
      <TextBox Grid.Row="1" Grid.Column="1" x:Name="PasswordBox" PasswordChar="•" />
    </Grid>
    <TextBlock x:Name="MissingText" Foreground="#FF8080" IsVisible="False" TextWrapping="Wrap" />
    <TextBlock Foreground="#A0A0A0" FontSize="12" TextWrapping="Wrap"
               Text="Kept in memory until this session window closes. Never saved to disk." />
    <StackPanel Orientation="Horizontal" HorizontalAlignment="Right" Spacing="8">
      <Button x:Name="CancelButton" Content="Cancel" IsCancel="True" Click="OnCancelClick" />
      <Button x:Name="SignInButton" Content="Sign In" IsDefault="True" Click="OnSignInClick" />
    </StackPanel>
  </StackPanel>
</Window>
```

`src/LizTerm.App/Views/SignInWindow.axaml.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Controls;
using Avalonia.Interactivity;
using LizTerm.App.Dialogs;
using LizTerm.Core.HostFiles;

namespace LizTerm.App.Views;

/// <summary>The REST sign-in (spec §3.2). Cancel and the title bar both answer null. The password lives only in the
/// text box and the <see cref="HostCredentials"/> this returns.</summary>
public partial class SignInWindow : Window
{
    /// <summary>Design-time only.</summary>
    public SignInWindow() : this(new CredentialPromptRequest("MVS/CE", "http://mvs.example:8080/zosmf", "MVSCE02", true)) { }

    public SignInWindow(CredentialPromptRequest request)
    {
        InitializeComponent();
        HostText.Text = $"{request.ProfileName} · {request.Url}";
        RetryText.IsVisible = request.IsRetry;
        UseridBox.Text = request.Userid ?? "";
        // The first box the user has to type in takes the keyboard.
        Opened += (_, _) => (string.IsNullOrEmpty(UseridBox.Text) ? UseridBox : PasswordBox).Focus();
    }

    internal void SignIn()
    {
        var userid = (UseridBox.Text ?? "").Trim().ToUpperInvariant();
        var password = PasswordBox.Text ?? "";
        if (userid.Length == 0 || password.Length == 0)
        {
            MissingText.Text = "Enter the userid and the password.";
            MissingText.IsVisible = true;
            return;
        }
        Close(new HostCredentials(userid, password));
    }

    private void OnSignInClick(object? sender, RoutedEventArgs e) => SignIn();

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(null);
}
```

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~SignInWindowTests|FullyQualifiedName~ModalDialogsTests"`
Expected: PASS. (If `Grid.RowSpacing` is not available in this Avalonia version, remove it and put `Margin="0,0,0,8"` on the first row's controls.)

- [ ] **Step 5: Commit**

```bash
git add src/LizTerm.App/Dialogs src/LizTerm.App/Views/SignInWindow.axaml src/LizTerm.App/Views/SignInWindow.axaml.cs tests/LizTerm.App.Tests
git commit -m "Add the mvsMF sign-in prompt

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 4: One sign-in per session (`CredentialHolder`)

**Files:**
- Create: `src/LizTerm.App/HostFiles/CredentialHolder.cs`
- Test: `tests/LizTerm.App.Tests/HostFiles/CredentialHolderTests.cs`

**Interfaces:**
- Consumes: `ICredentialPrompt`, `CredentialPromptRequest` (Task 3); Core `HostCredentialProvider`, `HostCredentialRequest(bool IsRetry, HostCredentials? Rejected = null)`.
- Produces: `CredentialHolder(string profileName, string url, string? userid)` with `ProviderFor(ICredentialPrompt prompt) : HostCredentialProvider`, `HasCredentials : bool`, `Forget() : void`.

Rules (spec §3.2 and the Core provider contract): prompts are serialised; a pair is reused until the host refuses it; on a retry the user is asked again only if the refused pair is still the current one, otherwise the newer pair is answered; the userid last entered prefills the next prompt; a cancelled prompt answers null and keeps nothing.

- [ ] **Step 1: Write the failing tests**

`tests/LizTerm.App.Tests/HostFiles/CredentialHolderTests.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.App.HostFiles;
using LizTerm.App.Tests.Fakes;
using LizTerm.Core.HostFiles;

namespace LizTerm.App.Tests.HostFiles;

public class CredentialHolderTests
{
    private static readonly HostCredentialRequest First = new(false);

    private static (CredentialHolder Holder, FakeCredentialPrompt Prompt, HostCredentialProvider Provider) Create(string? userid = "MVSCE02")
    {
        var holder = new CredentialHolder("MVS/CE", "http://mvs:8080/zosmf", userid);
        var prompt = new FakeCredentialPrompt();
        return (holder, prompt, holder.ProviderFor(prompt));
    }

    [Fact]
    public async Task Asks_once_and_answers_the_same_pair_afterwards()
    {
        var (holder, prompt, provider) = Create();

        var first = await provider(First, CancellationToken.None);
        var second = await provider(First, CancellationToken.None);

        Assert.Same(first, second);
        Assert.Equal(1, prompt.AskCount);
        Assert.True(holder.HasCredentials);
        Assert.Equal(new CredentialPromptRequest("MVS/CE", "http://mvs:8080/zosmf", "MVSCE02", false), prompt.LastRequest);
    }

    [Fact]
    public async Task A_refusal_of_the_current_pair_asks_again_as_a_retry()
    {
        var (_, prompt, provider) = Create();
        var wrong = new HostCredentials("MVSCE02", "wrong");
        var right = new HostCredentials("MVSCE02", "right");
        prompt.Answers.Enqueue(wrong);
        prompt.Answers.Enqueue(right);

        var first = await provider(First, CancellationToken.None);
        var retried = await provider(new HostCredentialRequest(true, first), CancellationToken.None);

        Assert.Same(wrong, first);
        Assert.Same(right, retried);
        Assert.Equal(new[] { "ask:MVSCE02:False", "ask:MVSCE02:True" }, prompt.Calls);
    }

    [Fact]
    public async Task A_refusal_of_a_pair_already_replaced_answers_the_newer_pair_without_asking()
    {
        var (_, prompt, provider) = Create();
        var old = new HostCredentials("MVSCE02", "old");
        var newer = new HostCredentials("MVSCE02", "new");
        prompt.Answers.Enqueue(old);
        prompt.Answers.Enqueue(newer);
        var first = await provider(First, CancellationToken.None);
        var replaced = await provider(new HostCredentialRequest(true, first), CancellationToken.None);

        var late = await provider(new HostCredentialRequest(true, old), CancellationToken.None);

        Assert.Same(newer, replaced);
        Assert.Same(newer, late);
        Assert.Equal(2, prompt.AskCount);
    }

    [Fact]
    public async Task Concurrent_first_requests_share_one_prompt()
    {
        var (_, prompt, provider) = Create();
        prompt.Gate = new TaskCompletionSource();

        var a = provider(First, CancellationToken.None).AsTask();
        var b = provider(First, CancellationToken.None).AsTask();
        await Wait.UntilAsync(() => prompt.AskCount == 1, "the first prompt");
        prompt.Gate.SetResult();
        var results = await Task.WhenAll(a, b);

        Assert.Same(results[0], results[1]);
        Assert.Equal(1, prompt.AskCount);
    }

    [Fact]
    public async Task Concurrent_refusals_of_the_same_pair_prompt_once()
    {
        var (_, prompt, provider) = Create();
        var refused = await provider(First, CancellationToken.None);
        prompt.Answer = new HostCredentials("MVSCE02", "fixed");
        prompt.Gate = new TaskCompletionSource();

        var a = provider(new HostCredentialRequest(true, refused), CancellationToken.None).AsTask();
        var b = provider(new HostCredentialRequest(true, refused), CancellationToken.None).AsTask();
        await Wait.UntilAsync(() => prompt.AskCount == 2, "the retry prompt");
        prompt.Gate.SetResult();
        var results = await Task.WhenAll(a, b);

        Assert.Same(results[0], results[1]);
        Assert.Equal(2, prompt.AskCount);
    }

    [Fact]
    public async Task A_cancelled_prompt_answers_null_and_keeps_nothing()
    {
        var (holder, prompt, provider) = Create();
        prompt.Answer = null;

        Assert.Null(await provider(First, CancellationToken.None));
        Assert.False(holder.HasCredentials);
    }

    [Fact]
    public async Task The_userid_typed_last_prefills_the_next_prompt()
    {
        var (holder, prompt, provider) = Create(userid: null);
        prompt.Answer = new HostCredentials("IBMUSER", "pw");
        await provider(First, CancellationToken.None);
        holder.Forget();

        await provider(First, CancellationToken.None);

        Assert.Equal(new[] { "ask::False", "ask:IBMUSER:False" }, prompt.Calls);
    }

    [Fact]
    public async Task Forget_drops_the_pair()
    {
        var (holder, prompt, provider) = Create();
        await provider(First, CancellationToken.None);

        holder.Forget();

        Assert.False(holder.HasCredentials);
        await provider(First, CancellationToken.None);
        Assert.Equal(2, prompt.AskCount);
    }

    [Fact]
    public async Task A_cancelled_wait_throws_before_asking()
    {
        var (_, prompt, provider) = Create();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider(First, cancelled.Token).AsTask());
        Assert.Equal(0, prompt.AskCount);
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~CredentialHolderTests"`
Expected: build FAILS (`CredentialHolder` not found).

- [ ] **Step 3: Implement**

`src/LizTerm.App/HostFiles/CredentialHolder.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.App.Dialogs;
using LizTerm.Core.HostFiles;

namespace LizTerm.App.HostFiles;

/// <summary>The REST sign-in for one session window (spec §3.2): asked once, held in memory, forgotten when the
/// window closes. The only store of the password. Prompts are serialised, so parallel operations that start
/// together, or are refused together, show one prompt between them (the provider contract in Core).</summary>
public sealed class CredentialHolder(string profileName, string url, string? userid)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _lock = new();
    private HostCredentials? _current;
    private string? _lastUserid = userid;

    public bool HasCredentials
    {
        get { lock (_lock) return _current is not null; }
    }

    /// <summary>A provider that asks through <paramref name="prompt"/>, which belongs to the window the operation
    /// runs in. Every provider made here shares this holder's pair.</summary>
    public HostCredentialProvider ProviderFor(ICredentialPrompt prompt) => (request, token) => GetAsync(prompt, request, token);

    public void Forget()
    {
        lock (_lock) _current = null;
    }

    private async ValueTask<HostCredentials?> GetAsync(ICredentialPrompt prompt, HostCredentialRequest request, CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try
        {
            string? prefill;
            lock (_lock)
            {
                var refusedIsCurrent = request.IsRetry && (request.Rejected is null || ReferenceEquals(_current, request.Rejected));
                if (_current is { } current && !refusedIsCurrent) return current;
                _current = null;
                prefill = _lastUserid;
            }
            var answer = await prompt.AskAsync(new CredentialPromptRequest(profileName, url, prefill, request.IsRetry));
            if (answer is null) return null;
            lock (_lock)
            {
                _current = answer;
                _lastUserid = answer.Userid;
            }
            return answer;
        }
        finally
        {
            _gate.Release();
        }
    }
}
```

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~CredentialHolderTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/LizTerm.App/HostFiles tests/LizTerm.App.Tests/HostFiles
git commit -m "Hold one mvsMF sign-in per session and serialise its prompts

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 5: Backend factory, error wording and the fake service

**Files:**
- Modify: `src/LizTerm.App/LizTerm.App.csproj` (reference the new backend)
- Create: `src/LizTerm.App/HostFileServiceFactory.cs`, `src/LizTerm.App/HostFiles/HostFileMessages.cs`, `tests/LizTerm.App.Tests/Fakes/FakeHostFileService.cs`
- Test: `tests/LizTerm.App.Tests/HostFileServiceFactoryTests.cs`, `tests/LizTerm.App.Tests/HostFiles/HostFileMessagesTests.cs`, `tests/LizTerm.App.Tests/HostFiles/FakeHostFileServiceTests.cs`

**Interfaces:**
- Consumes: `CredentialHolder` (Task 4), `ICredentialPrompt` (Task 3); backend `MvsmfOptions`, `MvsmfFileService`; Core `IHostFileService`, `HostFileException`, `HostServerInfo`, `CertificatePin`.
- Produces:
  - `namespace LizTerm.App`: `public delegate Task<HostServerInfo> HostFileTester(string profileName, Uri url, string? userid, CertificatePin? pin, CancellationToken cancellationToken);`
  - `HostFileServiceFactory.TryNormalizeUrl(string? text, out Uri? url, out string? error) : bool`, `HostFileServiceFactory.Create(Uri baseUrl, CertificatePin? pin, HostCredentialProvider credentials) : IHostFileService`, `HostFileServiceFactory.CreateTester(ICredentialPrompt prompt) : HostFileTester`.
  - `HostFileMessages.Describe(Exception ex) : string`, `HostFileMessages.DescribeUploadFailure(Exception ex) : string`.
  - Test fake `FakeHostFileService` (see its source).

- [ ] **Step 1: Reference the backend**

In `src/LizTerm.App/LizTerm.App.csproj`, beside the existing `ProjectReference` to `LizTerm.Backend.B3270`, add:

```xml
    <ProjectReference Include="../LizTerm.Backend.Mvsmf/LizTerm.Backend.Mvsmf.csproj" />
```

- [ ] **Step 2: Write the fake and the failing tests**

`tests/LizTerm.App.Tests/Fakes/FakeHostFileService.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.Core.HostFiles;

namespace LizTerm.App.Tests.Fakes;

/// <summary>An in-memory mvsMF for the App tests. Every call is logged as "op:target" — list:&lt;pattern&gt;,
/// members:&lt;dsn&gt;, readtext:&lt;path&gt;, readbinary:&lt;path&gt;, writetext:&lt;path&gt;:&lt;lines&gt;,
/// writebinary:&lt;path&gt;, delete:&lt;path&gt;, info — and a <see cref="Failures"/> entry under the same key (without
/// the line count) makes that call throw.</summary>
public sealed class FakeHostFileService : IHostFileService
{
    private readonly object _lock = new();
    private int _running;

    public List<HostFileEntry> Datasets { get; } = [];
    /// <summary>Dataset name → member names, in host order.</summary>
    public Dictionary<string, List<string>> Members { get; } = [];
    /// <summary>HostPath.ToString() → records.</summary>
    public Dictionary<string, List<string>> Text { get; } = [];
    public Dictionary<string, byte[]> Binary { get; } = [];
    public Dictionary<string, Exception> Failures { get; } = [];
    public HostServerInfo Info { get; set; } = new("mvsMF", "1.0.0-dev", "MVS 3.8j");
    /// <summary>When set, every call waits for it (and for its token) after being logged.</summary>
    public TaskCompletionSource? Gate { get; set; }
    public List<string> Calls { get; } = [];
    public int MaxConcurrent { get; private set; }
    public bool Disposed { get; private set; }

    public void AddDataset(string name, string dsorg = "PO", string recfm = "FB", int lrecl = 80, int blksize = 19040, params string[] members)
    {
        Datasets.Add(new HostFileEntry(name, HostFileEntryKind.Dataset, new DatasetAttributes(dsorg, recfm, lrecl, blksize, "PUB000")));
        if (dsorg == "PO") Members[name] = [.. members];
    }

    public string[] CallsSnapshot()
    {
        lock (_lock) return [.. Calls];
    }

    public async Task<HostServerInfo> GetServerInfoAsync(CancellationToken cancellationToken = default)
    {
        await EnterAsync("info", "info", cancellationToken);
        try { return Info; }
        finally { Leave(); }
    }

    public async Task<IReadOnlyList<HostFileEntry>> ListDatasetsAsync(string pattern, CancellationToken cancellationToken = default)
    {
        await EnterAsync($"list:{pattern}", $"list:{pattern}", cancellationToken);
        try { lock (_lock) return [.. Datasets]; }
        finally { Leave(); }
    }

    public async Task<IReadOnlyList<HostFileEntry>> ListMembersAsync(HostPath dataset, CancellationToken cancellationToken = default)
    {
        await EnterAsync($"members:{dataset}", $"members:{dataset}", cancellationToken);
        try
        {
            lock (_lock)
                return Members.TryGetValue(dataset.Dataset, out var names)
                    ? [.. names.Select(n => new HostFileEntry(n, HostFileEntryKind.Member))]
                    : [];
        }
        finally { Leave(); }
    }

    public async Task<IReadOnlyList<string>> ReadTextAsync(HostPath path, IProgress<long>? progress = null, CancellationToken cancellationToken = default)
    {
        await EnterAsync($"readtext:{path}", $"readtext:{path}", cancellationToken);
        try
        {
            List<string> lines;
            lock (_lock)
                lines = Text.TryGetValue(path.ToString(), out var found) ? [.. found] : throw Missing(path);
            progress?.Report(lines.Sum(l => l.Length + 1));
            return lines;
        }
        finally { Leave(); }
    }

    public async Task<long> ReadBinaryAsync(HostPath path, Stream destination, IProgress<long>? progress = null, CancellationToken cancellationToken = default)
    {
        await EnterAsync($"readbinary:{path}", $"readbinary:{path}", cancellationToken);
        try
        {
            byte[] bytes;
            lock (_lock) bytes = Binary.TryGetValue(path.ToString(), out var found) ? found : throw Missing(path);
            await destination.WriteAsync(bytes, cancellationToken);
            progress?.Report(bytes.Length);
            return bytes.Length;
        }
        finally { Leave(); }
    }

    public async Task WriteTextAsync(HostPath path, IReadOnlyList<string> lines, CancellationToken cancellationToken = default)
    {
        await EnterAsync($"writetext:{path}:{lines.Count}", $"writetext:{path}", cancellationToken);
        try
        {
            lock (_lock)
            {
                Text[path.ToString()] = [.. lines];
                AddMember(path);
            }
        }
        finally { Leave(); }
    }

    public async Task WriteBinaryAsync(HostPath path, Stream source, CancellationToken cancellationToken = default)
    {
        await EnterAsync($"writebinary:{path}", $"writebinary:{path}", cancellationToken);
        try
        {
            using var copy = new MemoryStream();
            await source.CopyToAsync(copy, cancellationToken);
            lock (_lock)
            {
                Binary[path.ToString()] = copy.ToArray();
                AddMember(path);
            }
        }
        finally { Leave(); }
    }

    public async Task DeleteAsync(HostPath path, CancellationToken cancellationToken = default)
    {
        await EnterAsync($"delete:{path}", $"delete:{path}", cancellationToken);
        try
        {
            lock (_lock)
            {
                if (path.Member is { } member && Members.TryGetValue(path.Dataset, out var names)) names.Remove(member);
                Text.Remove(path.ToString());
                Binary.Remove(path.ToString());
            }
        }
        finally { Leave(); }
    }

    public void Dispose() => Disposed = true;

    private void AddMember(HostPath path)
    {
        if (path.Member is { } member && Members.TryGetValue(path.Dataset, out var names) && !names.Contains(member))
            names.Add(member);
    }

    private static HostFileException Missing(HostPath path) =>
        new(HostFileErrorKind.CannotOpen, $"{path}: not found, not authorized, or cannot be opened.", 3);

    private async Task EnterAsync(string call, string failureKey, CancellationToken token)
    {
        Exception? failure;
        lock (_lock)
        {
            Calls.Add(call);
            _running++;
            MaxConcurrent = Math.Max(MaxConcurrent, _running);
            Failures.TryGetValue(failureKey, out failure);
        }
        try
        {
            if (Gate is { } gate) await gate.Task.WaitAsync(token);
            token.ThrowIfCancellationRequested();
            if (failure is not null) throw failure;
        }
        catch
        {
            Leave();
            throw;
        }
    }

    private void Leave()
    {
        lock (_lock) _running--;
    }
}
```

`tests/LizTerm.App.Tests/HostFiles/FakeHostFileServiceTests.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.App.Tests.Fakes;
using LizTerm.Core.HostFiles;

namespace LizTerm.App.Tests.HostFiles;

/// <summary>The fake is what every browser test stands on, so its own contract is pinned.</summary>
public class FakeHostFileServiceTests
{
    [Fact]
    public async Task Writes_add_members_and_deletes_remove_them()
    {
        var host = new FakeHostFileService();
        host.AddDataset("A.CNTL", members: ["ONE"]);
        var two = HostPath.ForMember("A.CNTL", "TWO");

        await host.WriteTextAsync(two, ["X"]);
        Assert.Equal(new[] { "ONE", "TWO" }, host.Members["A.CNTL"]);
        Assert.Equal(new[] { "X" }, await host.ReadTextAsync(two));

        await host.DeleteAsync(two);
        Assert.Equal(new[] { "ONE" }, host.Members["A.CNTL"]);
        var ex = await Assert.ThrowsAsync<HostFileException>(() => host.ReadTextAsync(two));
        Assert.Equal(HostFileErrorKind.CannotOpen, ex.Kind);
        Assert.Equal(new[] { "writetext:A.CNTL(TWO):1", "readtext:A.CNTL(TWO)", "delete:A.CNTL(TWO)", "readtext:A.CNTL(TWO)" }, host.CallsSnapshot());
    }

    [Fact]
    public async Task A_failure_is_thrown_by_the_matching_call()
    {
        var host = new FakeHostFileService();
        host.Failures["list:SYS1.**"] = new HostFileException(HostFileErrorKind.Unreachable, "down");
        await Assert.ThrowsAsync<HostFileException>(() => host.ListDatasetsAsync("SYS1.**"));
        Assert.Empty(await host.ListDatasetsAsync("OTHER.**"));
    }

    [Fact]
    public async Task The_gate_holds_calls_and_counts_how_many_ran_at_once()
    {
        var host = new FakeHostFileService { Gate = new TaskCompletionSource() };
        host.Text["A.B"] = ["x"];
        var a = host.ReadTextAsync(HostPath.ForDataset("A.B"));
        var b = host.ReadTextAsync(HostPath.ForDataset("A.B"));
        host.Gate.SetResult();
        await Task.WhenAll(a, b);
        Assert.Equal(2, host.MaxConcurrent);
    }
}
```

`tests/LizTerm.App.Tests/HostFiles/HostFileMessagesTests.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.App.HostFiles;
using LizTerm.Core.HostFiles;

namespace LizTerm.App.Tests.HostFiles;

public class HostFileMessagesTests
{
    [Theory]
    [InlineData(HostFileErrorKind.NotFound, null, null, "X(Y): not found.", "Not found.")]
    [InlineData(HostFileErrorKind.CannotOpen, 3, null, "x", "Not found, not authorized, or cannot be opened.")]
    [InlineData(HostFileErrorKind.NotAuthorized, 0, null, "x", "Not authorized.")]
    [InlineData(HostFileErrorKind.InvalidRequest, 1, "Dataset or member name too long", "x", "The host refused the request: Dataset or member name too long")]
    [InlineData(HostFileErrorKind.InvalidRequest, null, null, "x", "The host refused the request.")]
    [InlineData(HostFileErrorKind.Unauthenticated, null, null, "Sign-in was cancelled.", "Sign-in was cancelled.")]
    [InlineData(HostFileErrorKind.CertificateRejected, null, null, "x", "The host's certificate is not trusted.")]
    [InlineData(HostFileErrorKind.Unreachable, null, null, "Dataset list: cannot reach the host (refused).", "Dataset list: cannot reach the host (refused).")]
    [InlineData(HostFileErrorKind.ServerError, 7, null, "x", "Server error (reason 7).")]
    [InlineData(HostFileErrorKind.ServerError, null, null, "x", "Server error.")]
    public void Describes_each_kind_in_plain_words(HostFileErrorKind kind, int? reason, string? server, string message, string expected) =>
        Assert.Equal(expected, HostFileMessages.Describe(new HostFileException(kind, message, reason, server)));

    [Fact]
    public void Local_and_other_failures_are_described_too()
    {
        Assert.Equal("Local file: disk full", HostFileMessages.Describe(new IOException("disk full")));
        Assert.Equal("Local file: denied", HostFileMessages.Describe(new UnauthorizedAccessException("denied")));
        Assert.Equal("Cancelled.", HostFileMessages.Describe(new OperationCanceledException()));
        Assert.Equal("boom", HostFileMessages.Describe(new InvalidOperationException("boom")));
    }

    [Fact]
    public void An_upload_that_failed_mid_write_warns_about_a_partial_member()
    {
        Assert.Equal("Server error (reason 3). The member may be partly written.",
            HostFileMessages.DescribeUploadFailure(new HostFileException(HostFileErrorKind.ServerError, "x", 3)));
        Assert.Equal("x: gone The member may be partly written.",
            HostFileMessages.DescribeUploadFailure(new HostFileException(HostFileErrorKind.Unreachable, "x: gone")));
        Assert.Equal("Not authorized.",
            HostFileMessages.DescribeUploadFailure(new HostFileException(HostFileErrorKind.NotAuthorized, "x")));
    }
}
```

`tests/LizTerm.App.Tests/HostFileServiceFactoryTests.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Net;
using System.Net.Sockets;
using LizTerm.App.Tests.Fakes;
using LizTerm.Backend.Mvsmf;
using LizTerm.Core.HostFiles;

namespace LizTerm.App.Tests;

/// <summary>The one App test that names the mvsMF backend (tests/CLAUDE.md): what the factory builds is the one
/// thing about that backend the App owns.</summary>
public class HostFileServiceFactoryTests
{
    private static ValueTask<HostCredentials?> Anyone(HostCredentialRequest request, CancellationToken token) =>
        ValueTask.FromResult<HostCredentials?>(new HostCredentials("U", "p"));

    [Fact]
    public void Create_builds_the_mvsmf_service()
    {
        using var service = HostFileServiceFactory.Create(new Uri("http://mvs.test:8080/zosmf"), null, Anyone);
        Assert.IsType<MvsmfFileService>(service);
    }

    [Fact]
    public void A_url_with_credentials_is_refused_both_ways()
    {
        Assert.False(HostFileServiceFactory.TryNormalizeUrl("http://u:p@mvs.test", out var url, out var error));
        Assert.Null(url);
        Assert.Equal("Leave the userid and password out of the URL.", error);
        Assert.Throws<ArgumentException>(() => HostFileServiceFactory.Create(new Uri("http://u:p@mvs.test/zosmf"), null, Anyone));
    }

    [Fact]
    public void The_url_gets_its_zosmf_path()
    {
        Assert.True(HostFileServiceFactory.TryNormalizeUrl("http://mvs.test:8080", out var url, out _));
        Assert.Equal("http://mvs.test:8080/zosmf", url!.ToString());
    }

    [Fact]
    public async Task The_tester_signs_in_through_the_prompt_and_reports_a_host_it_cannot_reach()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        var prompt = new FakeCredentialPrompt();

        var tester = HostFileServiceFactory.CreateTester(prompt);
        var ex = await Assert.ThrowsAsync<HostFileException>(() =>
            tester("MVS/CE", new Uri($"http://127.0.0.1:{port}/zosmf"), "MVSCE02", null, TestContext.Current.CancellationToken));

        Assert.Equal(HostFileErrorKind.Unreachable, ex.Kind);
        Assert.Equal(new[] { "ask:MVSCE02:False" }, prompt.Calls);
        Assert.Equal("MVS/CE", prompt.LastRequest!.ProfileName);
    }
}
```

- [ ] **Step 3: Run to verify they fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~HostFileServiceFactoryTests|FullyQualifiedName~HostFileMessagesTests|FullyQualifiedName~FakeHostFileServiceTests"`
Expected: build FAILS (`HostFileServiceFactory` not found).

- [ ] **Step 4: Implement**

`src/LizTerm.App/HostFileServiceFactory.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.App.Dialogs;
using LizTerm.App.HostFiles;
using LizTerm.Backend.Mvsmf;
using LizTerm.Core.HostFiles;
using LizTerm.Core.Session;

namespace LizTerm.App;

/// <summary>A one-shot connection check for the profile editor: signs in through a prompt, asks the host what it is,
/// and forgets the sign-in.</summary>
public delegate Task<HostServerInfo> HostFileTester(string profileName, Uri url, string? userid, CertificatePin? pin, CancellationToken cancellationToken);

/// <summary>The only place the app names the mvsMF backend, as <see cref="SessionFactory"/> is for b3270. Everything
/// else in App talks to <see cref="IHostFileService"/>.</summary>
public static class HostFileServiceFactory
{
    /// <summary>Reads a URL as typed: <c>/zosmf</c> added to an empty path, credentials and queries refused.</summary>
    public static bool TryNormalizeUrl(string? text, out Uri? url, out string? error) =>
        MvsmfOptions.TryNormalizeBaseUrl(text, out url, out error);

    /// <exception cref="ArgumentException">The URL is not a usable base (see <see cref="TryNormalizeUrl"/>).</exception>
    public static IHostFileService Create(Uri baseUrl, CertificatePin? pin, HostCredentialProvider credentials) =>
        new MvsmfFileService(new MvsmfOptions(baseUrl, pin), credentials);

    public static HostFileTester CreateTester(ICredentialPrompt prompt) => async (profileName, url, userid, pin, token) =>
    {
        var holder = new CredentialHolder(profileName, url.ToString(), userid);
        using var service = Create(url, pin, holder.ProviderFor(prompt));
        return await service.GetServerInfoAsync(token);
    };
}
```

`src/LizTerm.App/HostFiles/HostFileMessages.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.Core.HostFiles;

namespace LizTerm.App.HostFiles;

/// <summary>What the browser and the profile editor say about a failure (spec §4). The backend's own message is
/// used where it carries the detail the user needs: which sign-in failed, what could not be reached.</summary>
public static class HostFileMessages
{
    public static string Describe(Exception ex) => ex switch
    {
        HostFileException host => host.Kind switch
        {
            HostFileErrorKind.NotFound => "Not found.",
            HostFileErrorKind.CannotOpen => "Not found, not authorized, or cannot be opened.",
            HostFileErrorKind.NotAuthorized => "Not authorized.",
            HostFileErrorKind.InvalidRequest => host.ServerMessage is { Length: > 0 } said
                ? $"The host refused the request: {said}"
                : "The host refused the request.",
            HostFileErrorKind.Unauthenticated => host.Message,
            HostFileErrorKind.CertificateRejected => "The host's certificate is not trusted.",
            HostFileErrorKind.Unreachable => host.Message,
            _ => host.Reason is { } reason ? $"Server error (reason {reason})." : "Server error.",
        },
        IOException or UnauthorizedAccessException => $"Local file: {ex.Message}",
        OperationCanceledException => "Cancelled.",
        _ => ex.Message,
    };

    /// <summary>The host does not roll back a failed write (spec §5.3), so a failure that can happen mid-write says so.</summary>
    public static string DescribeUploadFailure(Exception ex) =>
        ex is HostFileException { Kind: HostFileErrorKind.ServerError or HostFileErrorKind.Unreachable }
            ? Describe(ex) + " The member may be partly written."
            : Describe(ex);
}
```

- [ ] **Step 5: Run to verify they pass**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~HostFileServiceFactoryTests|FullyQualifiedName~HostFileMessagesTests|FullyQualifiedName~FakeHostFileServiceTests"`
Expected: PASS. Then `dotnet build LizTerm.slnx --no-incremental 2>&1 | grep -c " warning "` → `0`.

- [ ] **Step 6: Commit**

```bash
git add src/LizTerm.App tests/LizTerm.App.Tests
git commit -m "Name the mvsMF backend once in the app and describe its failures

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 6: Session access and browser connection

**Files:**
- Create: `src/LizTerm.App/HostFiles/HostFileAccess.cs`, `src/LizTerm.App/HostFiles/HostFileConnection.cs`
- Test: `tests/LizTerm.App.Tests/HostFiles/HostFileAccessTests.cs`, `tests/LizTerm.App.Tests/HostFiles/HostFileConnectionTests.cs`

**Interfaces:**
- Consumes: `CredentialHolder` (Task 4), `HostFileServiceFactory.TryNormalizeUrl` (Task 5), `ICredentialPrompt`, `ICertificatePrompt`, `CertificatePromptRequest(string Host, IReadOnlyList<string> Reason, PresentedCertificate? Presented, string? FetchError, CertificatePin? Previous, bool CanPin, string? CannotPinReason)`, `CertificateDecision(bool ConnectAnyway, bool Remember)`; Core `PresentedCertificate(string Sha256, string Subject, string Pem, bool Pinnable, string? NotPinnableReason)`.
- Produces:
  - `public delegate IHostFileService HostFileServiceCreator(Uri baseUrl, CertificatePin? pin, HostCredentialProvider credentials);` (namespace `LizTerm.App.HostFiles`; `HostFileServiceFactory.Create` matches it)
  - `HostFileAccess(SessionProfile profile, HostFileServiceCreator create, Action<CertificatePin>? savePin)` with `ProfileName`, `Userid`, `Url` (`Uri?`), `UrlError` (`string?`), `Credentials` (`CredentialHolder`), `Pin` (`CertificatePin?`), `CanRememberPin`, `Connect(ICredentialPrompt credentials, ICertificatePrompt? certificates) : HostFileConnection`, `Forget()`.
  - `HostFileConnection : IDisposable` with `RunAsync<T>(Func<IHostFileService, Task<T>> operation) : Task<T>`, `RunAsync(Func<IHostFileService, Task> operation) : Task`.

Spec §3.2–§3.4. `HostFileAccess` lives as long as the session window: the sign-in and any certificate the user accepted for this session. `HostFileConnection` lives as long as one browser window: the service, and the one-retry certificate flow. **Connect Anyway** trusts the presented certificate for this session; **Remember** also stores it in the profile (saved profiles only).

- [ ] **Step 1: Write the failing tests**

`tests/LizTerm.App.Tests/HostFiles/HostFileAccessTests.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.App.HostFiles;
using LizTerm.App.Tests.Fakes;
using LizTerm.Core.Session;

namespace LizTerm.App.Tests.HostFiles;

public class HostFileAccessTests
{
    private static readonly CertificatePin Pin = new("AA:BB", "CN=proxy", "pem");

    [Fact]
    public void Reads_the_profile_and_normalises_the_url()
    {
        var access = new HostFileAccess(
            new SessionProfile { Name = "MVS/CE", Host = "mvs", HostFilesUrl = "http://mvs:8080", HostFilesUserid = "MVSCE02", HostFilesPinnedCertificate = Pin },
            (_, _, _) => new FakeHostFileService(), savePin: null);

        Assert.Equal("MVS/CE", access.ProfileName);
        Assert.Equal("MVSCE02", access.Userid);
        Assert.Equal("http://mvs:8080/zosmf", access.Url!.ToString());
        Assert.Null(access.UrlError);
        Assert.Equal(Pin, access.Pin);
        Assert.False(access.CanRememberPin);
    }

    [Fact]
    public void An_unusable_url_is_reported_and_cannot_connect()
    {
        var access = new HostFileAccess(new SessionProfile { Name = "x", Host = "h", HostFilesUrl = "ftp://h" },
            (_, _, _) => new FakeHostFileService(), savePin: null);

        Assert.Null(access.Url);
        Assert.Equal("Enter an http:// or https:// URL.", access.UrlError);
        var ex = Assert.Throws<InvalidOperationException>(() => access.Connect(new FakeCredentialPrompt(), null));
        Assert.Equal("Enter an http:// or https:// URL.", ex.Message);
    }

    [Fact]
    public async Task Forget_drops_the_sign_in()
    {
        var access = new HostFileAccess(new SessionProfile { Name = "x", Host = "h", HostFilesUrl = "http://h" },
            (_, _, _) => new FakeHostFileService(), savePin: null);
        await access.Credentials.ProviderFor(new FakeCredentialPrompt())(new(false), CancellationToken.None);
        Assert.True(access.Credentials.HasCredentials);

        access.Forget();

        Assert.False(access.Credentials.HasCredentials);
    }
}
```

`tests/LizTerm.App.Tests/HostFiles/HostFileConnectionTests.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.App.Dialogs;
using LizTerm.App.HostFiles;
using LizTerm.App.Tests.Fakes;
using LizTerm.Core.HostFiles;
using LizTerm.Core.Security;
using LizTerm.Core.Session;

namespace LizTerm.App.Tests.HostFiles;

public class HostFileConnectionTests
{
    private static readonly PresentedCertificate Presented = new("11:22", "CN=proxy", "-----BEGIN CERTIFICATE-----", true, null);
    private static readonly CertificatePin Accepted = new("11:22", "CN=proxy", "-----BEGIN CERTIFICATE-----");

    private sealed class Host
    {
        public List<(CertificatePin? Pin, FakeHostFileService Service)> Created { get; } = [];
        public List<CertificatePin> Saved { get; } = [];
        public HostCredentialProvider? Provider { get; private set; }
        public TaskCompletionSource? FirstGate { get; set; }

        /// <summary>A service made without a pin refuses the certificate; one made with a pin answers.</summary>
        public IHostFileService Create(Uri url, CertificatePin? pin, HostCredentialProvider provider)
        {
            Provider = provider;
            var service = new FakeHostFileService();
            if (pin is null)
            {
                service.Failures["info"] = new HostFileException(HostFileErrorKind.CertificateRejected, "Server information: the host's certificate is not trusted.", certificate: Presented);
                service.Gate = FirstGate;
            }
            lock (Created) Created.Add((pin, service));
            return service;
        }

        public HostFileAccess Access(bool saved = true, CertificatePin? profilePin = null) => new(
            new SessionProfile { Name = "MVS", Host = "proxy", HostFilesUrl = "https://proxy/zosmf", HostFilesPinnedCertificate = profilePin },
            Create, saved ? Saved.Add : null);
    }

    private static Task<HostServerInfo> Info(HostFileConnection connection) => connection.RunAsync(s => s.GetServerInfoAsync());

    [Fact]
    public async Task Connect_anyway_retries_with_the_presented_certificate_for_this_session()
    {
        var host = new Host();
        var access = host.Access();
        var prompt = new FakeCertificatePrompt { Decision = new CertificateDecision(true, false) };
        using var connection = access.Connect(new FakeCredentialPrompt(), prompt);

        var info = await Info(connection);

        Assert.Equal("1.0.0-dev", info.ProductVersion);
        Assert.Equal(new CertificatePin?[] { null, Accepted }, host.Created.Select(c => c.Pin));
        Assert.Equal(Accepted, access.Pin);
        Assert.Empty(host.Saved);
        var request = prompt.LastRequest!;
        Assert.Equal("proxy", request.Host);
        Assert.Equal(new[] { "The mvsMF host's certificate is not trusted." }, request.Reason);
        Assert.Same(Presented, request.Presented);
        Assert.True(request.CanPin);
        Assert.Null(request.CannotPinReason);
    }

    [Fact]
    public async Task Remember_stores_the_pin_in_a_saved_profile()
    {
        var host = new Host();
        using var connection = host.Access().Connect(new FakeCredentialPrompt(), new FakeCertificatePrompt { Decision = new CertificateDecision(true, true) });

        await Info(connection);

        Assert.Equal(new[] { Accepted }, host.Saved);
    }

    [Fact]
    public async Task An_unsaved_profile_is_not_offered_the_pin()
    {
        var host = new Host();
        var prompt = new FakeCertificatePrompt { Decision = new CertificateDecision(true, true) };
        using var connection = host.Access(saved: false).Connect(new FakeCredentialPrompt(), prompt);

        await Info(connection);

        Assert.False(prompt.LastRequest!.CanPin);
        Assert.Null(prompt.LastRequest.CannotPinReason);
        Assert.Empty(host.Saved);
    }

    [Fact]
    public async Task A_certificate_that_cannot_be_pinned_says_why()
    {
        var host = new Host();
        var prompt = new FakeCertificatePrompt();
        var unpinnable = Presented with { Pinnable = false, NotPinnableReason = "The chain is missing its root" };
        var access = new HostFileAccess(new SessionProfile { Name = "MVS", Host = "p", HostFilesUrl = "https://proxy/zosmf" },
            (url, pin, provider) =>
            {
                var service = new FakeHostFileService();
                service.Failures["info"] = new HostFileException(HostFileErrorKind.CertificateRejected, "x", certificate: unpinnable);
                return service;
            }, host.Saved.Add);
        using var connection = access.Connect(new FakeCredentialPrompt(), prompt);

        await Assert.ThrowsAsync<HostFileException>(() => Info(connection));

        Assert.False(prompt.LastRequest!.CanPin);
        Assert.Equal("The chain is missing its root", prompt.LastRequest.CannotPinReason);
    }

    [Fact]
    public async Task Declining_keeps_the_refusal()
    {
        var host = new Host();
        using var connection = host.Access().Connect(new FakeCredentialPrompt(), new FakeCertificatePrompt());

        var ex = await Assert.ThrowsAsync<HostFileException>(() => Info(connection));

        Assert.Equal(HostFileErrorKind.CertificateRejected, ex.Kind);
        Assert.Single(host.Created);
    }

    [Fact]
    public async Task Without_a_certificate_prompt_the_refusal_stands()
    {
        var host = new Host();
        using var connection = host.Access().Connect(new FakeCredentialPrompt(), certificates: null);
        await Assert.ThrowsAsync<HostFileException>(() => Info(connection));
    }

    [Fact]
    public async Task The_profile_pin_is_used_from_the_start()
    {
        var host = new Host();
        var old = new CertificatePin("99:99", "CN=old", "pem");
        using var connection = host.Access(profilePin: old).Connect(new FakeCredentialPrompt(), new FakeCertificatePrompt());

        await Info(connection);

        Assert.Equal(new CertificatePin?[] { old }, host.Created.Select(c => c.Pin));
    }

    [Fact]
    public async Task A_pin_accepted_in_another_browser_window_is_used_without_asking()
    {
        var host = new Host();
        var access = host.Access();
        var prompt = new FakeCertificatePrompt { Decision = new CertificateDecision(true, false) };
        using var first = access.Connect(new FakeCredentialPrompt(), prompt);
        using var second = access.Connect(new FakeCredentialPrompt(), prompt);

        await Info(first);
        await Info(second);

        Assert.Single(prompt.Calls);
        Assert.Equal(Accepted, host.Created[^1].Pin);
    }

    [Fact]
    public async Task Refusals_that_arrive_together_ask_once()
    {
        var host = new Host { FirstGate = new TaskCompletionSource() };
        var prompt = new FakeCertificatePrompt { Decision = new CertificateDecision(true, false) };
        using var connection = host.Access().Connect(new FakeCredentialPrompt(), prompt);

        var a = Info(connection);
        var b = Info(connection);
        host.FirstGate.SetResult();
        await Task.WhenAll(a, b);

        Assert.Single(prompt.Calls);
    }

    [Fact]
    public async Task The_service_signs_in_through_the_session_holder()
    {
        var host = new Host();
        var access = host.Access();
        var credentials = new FakeCredentialPrompt();
        using var connection = access.Connect(credentials, null);

        var answer = await host.Provider!(new HostCredentialRequest(false), CancellationToken.None);

        Assert.Equal("MVSCE02", answer!.Userid);
        Assert.True(access.Credentials.HasCredentials);
        Assert.Equal("MVS", credentials.LastRequest!.ProfileName);
    }

    [Fact]
    public async Task Dispose_disposes_every_service_it_made_and_refuses_further_work()
    {
        var host = new Host();
        var connection = host.Access().Connect(new FakeCredentialPrompt(), new FakeCertificatePrompt { Decision = new CertificateDecision(true, false) });
        await Info(connection);

        connection.Dispose();

        Assert.All(host.Created, c => Assert.True(c.Service.Disposed));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => Info(connection));
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~HostFileAccessTests|FullyQualifiedName~HostFileConnectionTests"`
Expected: build FAILS (`HostFileAccess` not found).

- [ ] **Step 3: Implement**

`src/LizTerm.App/HostFiles/HostFileAccess.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.App.Dialogs;
using LizTerm.Core.HostFiles;
using LizTerm.Core.Session;

namespace LizTerm.App.HostFiles;

/// <summary>Builds the service for one URL and pin. <see cref="HostFileServiceFactory.Create"/> in the app; a fake in
/// tests.</summary>
public delegate IHostFileService HostFileServiceCreator(Uri baseUrl, CertificatePin? pin, HostCredentialProvider credentials);

/// <summary>A session window's REST side (spec §3.3): the URL from its profile, the sign-in, and the certificate the
/// user trusts for it. Lives as long as the session window; each browser window connects through it.</summary>
public sealed class HostFileAccess
{
    private readonly HostFileServiceCreator _create;
    private readonly Action<CertificatePin>? _savePin;
    private readonly object _lock = new();
    private CertificatePin? _pin;

    /// <param name="savePin">Stores a remembered pin in the profile file; null for a profile that has no file.</param>
    public HostFileAccess(SessionProfile profile, HostFileServiceCreator create, Action<CertificatePin>? savePin)
    {
        _create = create;
        _savePin = savePin;
        ProfileName = profile.Name;
        Userid = profile.HostFilesUserid;
        if (HostFileServiceFactory.TryNormalizeUrl(profile.HostFilesUrl, out var url, out var error)) Url = url;
        else UrlError = error;
        _pin = profile.HostFilesPinnedCertificate;
        Credentials = new CredentialHolder(profile.Name, Url?.ToString() ?? profile.HostFilesUrl ?? "", profile.HostFilesUserid);
    }

    public string ProfileName { get; }
    public string? Userid { get; }
    /// <summary>Null when the profile's URL is not usable; <see cref="UrlError"/> says why.</summary>
    public Uri? Url { get; }
    public string? UrlError { get; }
    public CredentialHolder Credentials { get; }
    public bool CanRememberPin => _savePin is not null;

    /// <summary>The certificate trusted for this session: the profile's pin, or one the user accepted since.</summary>
    public CertificatePin? Pin
    {
        get { lock (_lock) return _pin; }
    }

    /// <summary>A connection for one browser window, whose prompts are that window's.</summary>
    /// <exception cref="InvalidOperationException">The profile's URL is not usable.</exception>
    public HostFileConnection Connect(ICredentialPrompt credentials, ICertificatePrompt? certificates) =>
        Url is { } url
            ? new HostFileConnection(this, url, Credentials.ProviderFor(credentials), certificates)
            : throw new InvalidOperationException(UrlError);

    public void Forget() => Credentials.Forget();

    internal IHostFileService CreateService(Uri url, CertificatePin? pin, HostCredentialProvider credentials) =>
        _create(url, pin, credentials);

    internal void AcceptPin(CertificatePin pin, bool remember)
    {
        lock (_lock) _pin = pin;
        if (remember) _savePin?.Invoke(pin);
    }
}
```

`src/LizTerm.App/HostFiles/HostFileConnection.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.App.Dialogs;
using LizTerm.Core.HostFiles;
using LizTerm.Core.Security;
using LizTerm.Core.Session;

namespace LizTerm.App.HostFiles;

/// <summary>One browser window's service (spec §3.4). An operation refused for an untrusted certificate asks the
/// certificate prompt once; Connect Anyway trusts that certificate for the session (Remember also stores it) and the
/// operation runs once more. Refusals that arrive together share one prompt, and a certificate another window of
/// the same session accepted is picked up without asking.</summary>
public sealed class HostFileConnection : IDisposable
{
    private const string NotTrusted = "The mvsMF host's certificate is not trusted.";

    private readonly HostFileAccess _access;
    private readonly Uri _url;
    private readonly HostCredentialProvider _credentials;
    private readonly ICertificatePrompt? _certificates;
    private readonly SemaphoreSlim _trust = new(1, 1);
    private readonly object _lock = new();
    private readonly List<IHostFileService> _made = [];
    private IHostFileService _service;
    private CertificatePin? _servicePin;
    private bool _disposed;

    internal HostFileConnection(HostFileAccess access, Uri url, HostCredentialProvider credentials, ICertificatePrompt? certificates)
    {
        _access = access;
        _url = url;
        _credentials = credentials;
        _certificates = certificates;
        _servicePin = access.Pin;
        _service = access.CreateService(url, _servicePin, credentials);
        _made.Add(_service);
    }

    public async Task<T> RunAsync<T>(Func<IHostFileService, Task<T>> operation)
    {
        var service = Current();
        try
        {
            return await operation(service);
        }
        catch (HostFileException ex) when (ex.Kind == HostFileErrorKind.CertificateRejected && ex.Certificate is { } presented)
        {
            if (!await TrustAsync(presented, service)) throw;
            return await operation(Current());
        }
    }

    public Task RunAsync(Func<IHostFileService, Task> operation) => RunAsync(async service =>
    {
        await operation(service);
        return true;
    });

    public void Dispose()
    {
        IHostFileService[] made;
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            made = [.. _made];
            _made.Clear();
        }
        foreach (var service in made) service.Dispose();
    }

    /// <summary>The service for the session's current pin, remade when another window changed it. A replaced
    /// service is kept until Dispose, since an operation may still be using it.</summary>
    private IHostFileService Current()
    {
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var pin = _access.Pin;
            if (!Equals(pin, _servicePin)) Replace(pin);
            return _service;
        }
    }

    private void Replace(CertificatePin? pin)
    {
        _servicePin = pin;
        _service = _access.CreateService(_url, pin, _credentials);
        _made.Add(_service);
    }

    private async Task<bool> TrustAsync(PresentedCertificate presented, IHostFileService refused)
    {
        await _trust.WaitAsync();
        try
        {
            lock (_lock)
            {
                // Answered already, by an operation that was refused at the same time or by another window.
                if (!ReferenceEquals(_service, refused) || !Equals(_access.Pin, _servicePin)) return true;
            }
            if (_certificates is null) return false;
            var canPin = _access.CanRememberPin && presented.Pinnable;
            var cannotPin = _access.CanRememberPin && !presented.Pinnable
                ? presented.NotPinnableReason ?? "This certificate cannot be pinned."
                : null;
            var decision = await _certificates.AskAsync(new CertificatePromptRequest(
                _url.Host, [NotTrusted], presented, null, _access.Pin, canPin, cannotPin));
            if (!decision.ConnectAnyway) return false;
            var pin = new CertificatePin(presented.Sha256, presented.Subject, presented.Pem);
            _access.AcceptPin(pin, canPin && decision.Remember);
            lock (_lock)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                Replace(pin);
            }
            return true;
        }
        finally
        {
            _trust.Release();
        }
    }
}
```

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~HostFileAccessTests|FullyQualifiedName~HostFileConnectionTests"`
Expected: PASS. (`The_profile_pin_is_used_from_the_start`: the service made with a pin answers, so no prompt runs and one service exists.)

- [ ] **Step 5: Commit**

```bash
git add src/LizTerm.App/HostFiles tests/LizTerm.App.Tests/HostFiles
git commit -m "Hold the mvsMF sign-in and trusted certificate per session

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 7: Owned windows that do not block (`ShowAbove`)

**Files:**
- Modify: `src/LizTerm.App/Dialogs/ModalDialogs.cs`
- Test: `tests/LizTerm.App.Tests/Views/ModalDialogsTests.cs`

**Interfaces:**
- Produces: `ModalDialogs.ShowAbove(this Window window, Window owner) : void` — shows `window` owned by `owner`, takes the owner's `Topmost` and follows it until `window` closes. Closing the owner closes the window (Avalonia's owned-window behaviour).

- [ ] **Step 1: Write the failing tests**

Add to `tests/LizTerm.App.Tests/Views/ModalDialogsTests.cs` (inside the class; add `using LizTerm.App.Dialogs;` and `using Avalonia.Controls;` if missing):

```csharp
    [AvaloniaFact]
    public void An_owned_window_takes_and_follows_its_owners_keep_on_top_until_it_closes()
    {
        var owner = new Window { Topmost = true };
        owner.Show();
        var child = new Window();

        child.ShowAbove(owner);

        Assert.Same(owner, child.Owner);
        Assert.Contains(child, owner.OwnedWindows);
        Assert.True(child.Topmost);
        owner.Topmost = false;
        Assert.False(child.Topmost);

        child.Close();
        owner.Topmost = true;
        Assert.False(child.Topmost);
        owner.Close();
    }

    [AvaloniaFact]
    public void An_owned_window_closes_with_its_owner()
    {
        var owner = new Window();
        owner.Show();
        var child = new Window();
        var closed = false;
        child.Closed += (_, _) => closed = true;
        child.ShowAbove(owner);

        owner.Close();

        Assert.True(closed);
    }
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~ModalDialogsTests"`
Expected: build FAILS (`ShowAbove` not found).

- [ ] **Step 3: Implement**

In `ModalDialogs.cs`, extend the class summary with: "<see cref="ShowAbove"/> is the same rule for a window that belongs to one session window but does not block it, the mvsMF Browser." and add:

```csharp
    /// <summary>Shows <paramref name="window"/> owned by <paramref name="owner"/> without blocking it. Owned, it
    /// stays above the owner and closes with it; like a dialog, it takes the owner's Keep on Top and follows it
    /// while open, for the reason given on the class.</summary>
    public static void ShowAbove(this Window window, Window owner)
    {
        void Follow(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property == Window.TopmostProperty) window.Topmost = owner.Topmost;
        }

        window.Topmost = owner.Topmost;
        owner.PropertyChanged += Follow;
        window.Closed += (_, _) => owner.PropertyChanged -= Follow;
        window.Show(owner);
    }
```

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~ModalDialogsTests"`
Expected: PASS (the repo-wide `Every_dialog_in_the_app_opens_through_ShowDialogAbove` scan still passes: `window.Show(owner)` is not `ShowDialog`).

- [ ] **Step 5: Commit**

```bash
git add src/LizTerm.App/Dialogs/ModalDialogs.cs tests/LizTerm.App.Tests/Views/ModalDialogsTests.cs
git commit -m "Show an owned non-blocking window above its owner

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 8: Browser view model — listing, members, mode, errors

**Files:**
- Create: `src/LizTerm.App/ViewModels/ConfirmationRequest.cs`, `src/LizTerm.App/ViewModels/MvsmfBrowserRows.cs`, `src/LizTerm.App/ViewModels/MvsmfBrowserViewModel.cs`
- Create: `tests/LizTerm.App.Tests/ViewModels/BrowserTestHost.cs`
- Test: `tests/LizTerm.App.Tests/ViewModels/MvsmfBrowserViewModelTests.cs`

**Interfaces:**
- Consumes: `HostFileAccess`, `HostFileConnection` (Task 6), `HostFileMessages` (Task 5), `IFilePicker` (Task 2); Core `HostPath`, `HostFileEntry`, `DatasetAttributes`, `HostTransferMode`, `RecordFormatFamily`.
- Produces (later tasks add partial files to the same class):
  - `ConfirmChoice { Cancel, Primary, Secondary }`; `ConfirmOutcome(ConfirmChoice Choice, bool ApplyToAll)`; `ConfirmationRequest(string message, string primaryLabel, string? secondaryLabel = null, bool offersApplyToAll = false)` with `Message`, `PrimaryLabel`, `SecondaryLabel`, `HasSecondary`, `OffersApplyToAll`, `ApplyToAll` (observable), `Answer : Task<ConfirmOutcome>`, `PrimaryCommand`, `SecondaryCommand`, `CancelCommand`.
  - `DatasetRow(HostFileEntry entry)` with `Name`, `Attributes`, `Dsorg`, `Recfm`, `Lrecl`, `IsSupported`, `IsPartitioned`, `IsSequential`, `DisplayName`, `Path`.
  - `MemberRow(string dataset, string name)` with `Name`, `Path`, observable `Status`.
  - `MvsmfBrowserViewModel(HostFileAccess access, HostFileConnection connection, IFilePicker picker, Action<Action> dispatch, Func<Task>? openGuide = null) : ObservableObject, IDisposable` with: `Title`, `Datasets`, `Members`, `VisibleMembers`, `Filter`, `MemberFilter`, `SelectedDataset`, `Mode`, `IsTextMode`, `IsBinaryMode`, `TrimTrailingBlanks`, `VerifyUploads`, `ExpandTabs`, `IsBusy`, `IsIdle`, `StatusText`, `ErrorText`, `HasError`, `CanRetry`, `Confirmation`, `HasConfirmation`, `ShowMembers`, `ShowSequentialNote`, `ShowChooseHint`, `ChooseHint`, `MembersHeader`, `ShowPaddingNote`, `SelectedMembers`, `SetSelectedMembers(IEnumerable<MemberRow>)`, `ListCommand`, `RetryCommand`, `CancelCommand`, `OpenGuideCommand`, `Dispose()`; and, for the partial files, the private members `RunExclusiveAsync(Func<CancellationToken, Task> work, Func<Task>? retry = null)`, `AskAsync(ConfirmationRequest)`, `TryPickAsync<T>(Func<Task<T>>)`, `LoadMembersCoreAsync(DatasetRow row, CancellationToken token)`, `Plural(int count, string noun)`, `NotifyCommands()` (later tasks add their commands' `NotifyCanExecuteChanged()` calls to it).
  - Test helper `BrowserTestHost.Create(...)` and `BrowserTestHost.Standard(FakeHostFileService)`.

Spec §4. One operation runs at a time (`IsBusy`); only a download batch runs two transfers at once (Task 9). Results are set straight after `await` (the UI thread's context brings continuations back); only progress reports, which arrive on backend threads, go through `dispatch`. A connection problem (cannot reach, sign-in, certificate) shows in a banner with **Retry**; any other failure is the status line. Selecting a dataset preselects Binary for `RECFM=U` and Text otherwise.

- [ ] **Step 1: Write the test host and the failing tests**

`tests/LizTerm.App.Tests/ViewModels/BrowserTestHost.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.App.HostFiles;
using LizTerm.App.Tests.Fakes;
using LizTerm.App.ViewModels;
using LizTerm.Core.Session;

namespace LizTerm.App.Tests.ViewModels;

internal sealed record BrowserTestHost(
    MvsmfBrowserViewModel Vm,
    FakeHostFileService Host,
    FakeFilePicker Picker,
    FakeCertificatePrompt Certificates,
    FakeCredentialPrompt Credentials,
    HostFileAccess Access)
{
    public int GuideOpened { get; set; }

    public static BrowserTestHost Create(string? userid = "MVSCE02", Action<FakeHostFileService>? seed = null)
    {
        var host = new FakeHostFileService();
        (seed ?? Standard)(host);
        var access = new HostFileAccess(
            new SessionProfile { Name = "MVS/CE", Host = "mvs", HostFilesUrl = "http://mvs:8080", HostFilesUserid = userid },
            (_, _, _) => host, savePin: null);
        var credentials = new FakeCredentialPrompt();
        var certificates = new FakeCertificatePrompt();
        var picker = new FakeFilePicker();
        BrowserTestHost? made = null;
        var vm = new MvsmfBrowserViewModel(access, access.Connect(credentials, certificates), picker, action => action(),
            () =>
            {
                made!.GuideOpened++;
                return Task.CompletedTask;
            });
        made = new BrowserTestHost(vm, host, picker, certificates, credentials, access);
        return made;
    }

    /// <summary>A PDS of three members, a load library, a sequential dataset and one the preview cannot open.</summary>
    public static void Standard(FakeHostFileService host)
    {
        host.AddDataset("MVSCE02.CNTL", members: ["ALLOC", "COMPILE", "HELLO"]);
        host.AddDataset("MVSCE02.LOAD", recfm: "U", lrecl: 0, blksize: 19069, members: ["PROG"]);
        host.AddDataset("MVSCE02.UFSHOME", dsorg: "PS", recfm: "U", lrecl: 0, blksize: 4096);
        host.AddDataset("MVSCE02.DB", dsorg: "DA", recfm: "F", lrecl: 4096, blksize: 4096);
    }

    public async Task ListAsync() => await Vm.ListCommand.ExecuteAsync(null);

    public async Task ChooseAsync(string dataset)
    {
        if (Vm.Datasets.Count == 0) await ListAsync();
        Vm.SelectedDataset = Vm.Datasets.Single(d => d.Name == dataset);
        await Wait.UntilAsync(() => !Vm.IsBusy, "the member list");
    }

    public void Select(params string[] members) =>
        Vm.SetSelectedMembers(Vm.Members.Where(m => members.Contains(m.Name)));
}
```

`tests/LizTerm.App.Tests/ViewModels/MvsmfBrowserViewModelTests.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.App.ViewModels;
using LizTerm.Core.HostFiles;

namespace LizTerm.App.Tests.ViewModels;

public class MvsmfBrowserViewModelTests
{
    [Fact]
    public void The_title_and_filter_come_from_the_profile()
    {
        var t = BrowserTestHost.Create();
        Assert.Equal("mvsMF Browser — MVS/CE (Preview)", t.Vm.Title);
        Assert.Equal("MVSCE02.**", t.Vm.Filter);
        Assert.Equal("", BrowserTestHost.Create(userid: null).Vm.Filter);
        Assert.True(t.Vm.ShowChooseHint);
        Assert.Equal("Choose a dataset on the left.", t.Vm.ChooseHint);
    }

    [Fact]
    public async Task Listing_fills_the_datasets_and_marks_what_cannot_be_opened()
    {
        var t = BrowserTestHost.Create();
        t.Vm.Filter = " mvsce02.** ";

        await t.ListAsync();

        Assert.Equal(new[] { "MVSCE02.CNTL", "MVSCE02.LOAD", "MVSCE02.UFSHOME", "MVSCE02.DB (not supported)" }, t.Vm.Datasets.Select(d => d.DisplayName));
        Assert.Equal("4 datasets", t.Vm.StatusText);
        Assert.Equal(new[] { "list:MVSCE02.**" }, t.Host.CallsSnapshot());
        var cntl = t.Vm.Datasets[0];
        Assert.Equal(("PO", "FB", "80"), (cntl.Dsorg, cntl.Recfm, cntl.Lrecl));
        Assert.False(t.Vm.IsBusy);
    }

    [Fact]
    public async Task A_bad_filter_is_refused_before_asking_the_host()
    {
        var t = BrowserTestHost.Create();
        t.Vm.Filter = "MVSCE02.A B";

        await t.ListAsync();

        Assert.Equal("✗ A filter cannot contain ' '.", t.Vm.StatusText);
        Assert.Empty(t.Host.CallsSnapshot());
    }

    [Fact]
    public async Task Choosing_a_pds_lists_its_members()
    {
        var t = BrowserTestHost.Create();

        await t.ChooseAsync("MVSCE02.CNTL");

        Assert.Equal(new[] { "ALLOC", "COMPILE", "HELLO" }, t.Vm.Members.Select(m => m.Name));
        Assert.Equal(new[] { "ALLOC", "COMPILE", "HELLO" }, t.Vm.VisibleMembers.Select(m => m.Name));
        Assert.Equal("MVSCE02.CNTL · 3 members", t.Vm.MembersHeader);
        Assert.Equal("MVSCE02.CNTL · 3 members", t.Vm.StatusText);
        Assert.True(t.Vm.ShowMembers);
        Assert.False(t.Vm.ShowSequentialNote);
        Assert.False(t.Vm.ShowChooseHint);
        Assert.True(t.Vm.IsTextMode);
        Assert.Equal(HostPath.ForMember("MVSCE02.CNTL", "HELLO"), t.Vm.Members[2].Path);
    }

    [Fact]
    public async Task A_load_library_preselects_binary_and_a_text_library_text()
    {
        var t = BrowserTestHost.Create();

        await t.ChooseAsync("MVSCE02.LOAD");
        Assert.True(t.Vm.IsBinaryMode);
        Assert.False(t.Vm.ShowPaddingNote);

        await t.ChooseAsync("MVSCE02.CNTL");
        Assert.True(t.Vm.IsTextMode);
        t.Vm.IsBinaryMode = true;
        Assert.Equal(HostTransferMode.Binary, t.Vm.Mode);
        Assert.True(t.Vm.ShowPaddingNote);
    }

    [Fact]
    public async Task A_sequential_dataset_shows_its_note_and_lists_no_members()
    {
        var t = BrowserTestHost.Create();

        await t.ChooseAsync("MVSCE02.UFSHOME");

        Assert.True(t.Vm.ShowSequentialNote);
        Assert.False(t.Vm.ShowMembers);
        Assert.Empty(t.Vm.Members);
        Assert.DoesNotContain(t.Host.CallsSnapshot(), c => c.StartsWith("members:"));
    }

    [Fact]
    public async Task A_dataset_the_preview_cannot_open_says_so()
    {
        var t = BrowserTestHost.Create();

        await t.ChooseAsync("MVSCE02.DB");

        Assert.True(t.Vm.ShowChooseHint);
        Assert.Equal("MVSCE02.DB cannot be opened in this release (DSORG DA).", t.Vm.ChooseHint);
        Assert.DoesNotContain(t.Host.CallsSnapshot(), c => c.StartsWith("members:"));
    }

    [Fact]
    public async Task The_member_filter_narrows_the_visible_list()
    {
        var t = BrowserTestHost.Create();
        await t.ChooseAsync("MVSCE02.CNTL");

        t.Vm.MemberFilter = "co";

        Assert.Equal(new[] { "COMPILE" }, t.Vm.VisibleMembers.Select(m => m.Name));
        t.Vm.MemberFilter = "";
        Assert.Equal(3, t.Vm.VisibleMembers.Count);
    }

    [Fact]
    public async Task A_connection_failure_shows_the_banner_and_retry_runs_the_listing_again()
    {
        var t = BrowserTestHost.Create();
        t.Host.Failures["list:MVSCE02.**"] = new HostFileException(HostFileErrorKind.Unreachable, "Dataset list: cannot reach the host (refused).");

        await t.ListAsync();

        Assert.True(t.Vm.HasError);
        Assert.Equal("Dataset list: cannot reach the host (refused).", t.Vm.ErrorText);
        Assert.True(t.Vm.CanRetry);
        t.Host.Failures.Clear();
        await t.Vm.RetryCommand.ExecuteAsync(null);
        Assert.False(t.Vm.HasError);
        Assert.Equal(4, t.Vm.Datasets.Count);
    }

    [Fact]
    public async Task Other_failures_go_to_the_status_line()
    {
        var t = BrowserTestHost.Create();
        t.Host.Failures["members:MVSCE02.CNTL"] = new HostFileException(HostFileErrorKind.CannotOpen, "x", 3);

        await t.ChooseAsync("MVSCE02.CNTL");

        Assert.False(t.Vm.HasError);
        Assert.Equal("✗ Not found, not authorized, or cannot be opened.", t.Vm.StatusText);
    }

    [Fact]
    public async Task Busy_blocks_another_listing_and_cancel_ends_it()
    {
        var t = BrowserTestHost.Create();
        t.Host.Gate = new TaskCompletionSource();

        var listing = t.Vm.ListCommand.ExecuteAsync(null);
        await Wait.UntilAsync(() => t.Host.CallsSnapshot().Length == 1, "the listing to start");
        Assert.True(t.Vm.IsBusy);
        Assert.False(t.Vm.IsIdle);
        Assert.False(t.Vm.ListCommand.CanExecute(null));
        Assert.True(t.Vm.CancelCommand.CanExecute(null));

        t.Vm.CancelCommand.Execute(null);
        await listing;

        Assert.Equal("– Cancelled.", t.Vm.StatusText);
        Assert.False(t.Vm.IsBusy);
        Assert.True(t.Vm.ListCommand.CanExecute(null));
    }

    [Fact]
    public async Task Selecting_members_is_remembered_and_changing_dataset_clears_it()
    {
        var t = BrowserTestHost.Create();
        await t.ChooseAsync("MVSCE02.CNTL");

        t.Select("ALLOC", "HELLO");
        Assert.Equal(new[] { "ALLOC", "HELLO" }, t.Vm.SelectedMembers.Select(m => m.Name));

        await t.ChooseAsync("MVSCE02.LOAD");
        Assert.Empty(t.Vm.SelectedMembers);
    }

    [Fact]
    public async Task The_guide_link_opens_the_user_guide()
    {
        var t = BrowserTestHost.Create();
        await t.Vm.OpenGuideCommand.ExecuteAsync(null);
        Assert.Equal(1, t.GuideOpened);
    }

    [Fact]
    public async Task A_confirmation_answers_its_awaiter()
    {
        var request = new ConfirmationRequest("Replace?", "Replace", "Skip", offersApplyToAll: true);
        Assert.True(request.HasSecondary);
        request.ApplyToAll = true;

        request.SecondaryCommand.Execute(null);

        Assert.Equal(new ConfirmOutcome(ConfirmChoice.Secondary, true), await request.Answer);
    }

    [Fact]
    public void Dispose_cancels_and_releases_the_connection()
    {
        var t = BrowserTestHost.Create();
        t.Vm.Dispose();
        Assert.True(t.Host.Disposed);
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~MvsmfBrowserViewModelTests"`
Expected: build FAILS (`MvsmfBrowserViewModel` not found).

- [ ] **Step 3: Implement**

`src/LizTerm.App/ViewModels/ConfirmationRequest.cs`:

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
/// operation that asked. The first answer wins.</summary>
public sealed partial class ConfirmationRequest(string message, string primaryLabel, string? secondaryLabel = null, bool offersApplyToAll = false)
    : ObservableObject
{
    private readonly TaskCompletionSource<ConfirmOutcome> _answer = new();

    public string Message { get; } = message;
    public string PrimaryLabel { get; } = primaryLabel;
    public string? SecondaryLabel { get; } = secondaryLabel;
    public bool HasSecondary => SecondaryLabel is not null;
    public bool OffersApplyToAll { get; } = offersApplyToAll;

    [ObservableProperty] private bool _applyToAll;

    public Task<ConfirmOutcome> Answer => _answer.Task;

    [RelayCommand]
    private void Primary() => _answer.TrySetResult(new ConfirmOutcome(ConfirmChoice.Primary, ApplyToAll));

    [RelayCommand]
    private void Secondary() => _answer.TrySetResult(new ConfirmOutcome(ConfirmChoice.Secondary, ApplyToAll));

    [RelayCommand]
    private void Cancel() => _answer.TrySetResult(new ConfirmOutcome(ConfirmChoice.Cancel, false));
}
```

`src/LizTerm.App/ViewModels/MvsmfBrowserRows.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using LizTerm.Core.HostFiles;

namespace LizTerm.App.ViewModels;

/// <summary>One dataset in the browser's left pane. A name the naming rules refuse (a host can list anything) is
/// treated like a dataset organisation the preview cannot open.</summary>
public sealed class DatasetRow(HostFileEntry entry)
{
    private static readonly DatasetAttributes Unknown = new(null, null, null, null, null);

    public string Name => entry.Name;
    public DatasetAttributes Attributes => entry.Attributes ?? Unknown;
    public string Dsorg => Attributes.Dsorg ?? "";
    public string Recfm => Attributes.Recfm ?? "";
    public string Lrecl => Attributes.Lrecl?.ToString(CultureInfo.InvariantCulture) ?? "";
    public bool IsSupported => Attributes.IsSupported && HostPath.DatasetNameError(Name) is null;
    public bool IsPartitioned => IsSupported && Attributes.IsPartitioned;
    public bool IsSequential => IsSupported && Attributes.IsSequential;
    /// <summary>Dimmed in the list as well; the words carry the meaning, not the dimming.</summary>
    public string DisplayName => IsSupported ? Name : $"{Name} (not supported)";
    /// <summary>Only for a supported row.</summary>
    public HostPath Path => HostPath.ForDataset(Name);
}

/// <summary>One member in the right pane. <see cref="Status"/> is the per-member result of the last batch, always
/// a mark and words.</summary>
public sealed partial class MemberRow(string dataset, string name) : ObservableObject
{
    public string Name { get; } = name;
    public HostPath Path { get; } = HostPath.ForMember(dataset, name);

    [ObservableProperty] private string _status = "";
}
```

`src/LizTerm.App/ViewModels/MvsmfBrowserViewModel.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LizTerm.App.Files;
using LizTerm.App.HostFiles;
using LizTerm.Core.HostFiles;

namespace LizTerm.App.ViewModels;

/// <summary>The mvsMF Browser (spec §4): datasets on the left, members of the chosen PDS on the right, and the
/// transfers in the bottom bar. One operation at a time; see the partial files for downloads, uploads and delete.</summary>
public sealed partial class MvsmfBrowserViewModel : ObservableObject, IDisposable
{
    private readonly HostFileAccess _access;
    private readonly HostFileConnection _connection;
    private readonly IFilePicker _picker;
    private readonly Action<Action> _dispatch;
    private readonly Func<Task>? _openGuide;
    private CancellationTokenSource? _cts;
    private Func<Task>? _retry;
    private List<MemberRow> _selectedMembers = [];
    private bool _disposed;

    public MvsmfBrowserViewModel(HostFileAccess access, HostFileConnection connection, IFilePicker picker,
        Action<Action> dispatch, Func<Task>? openGuide = null)
    {
        _access = access;
        _connection = connection;
        _picker = picker;
        _dispatch = dispatch;
        _openGuide = openGuide;
        _filter = access.Userid is { Length: > 0 } userid ? userid + ".**" : "";
    }

    public string Title => $"mvsMF Browser — {_access.ProfileName} (Preview)";

    public ObservableCollection<DatasetRow> Datasets { get; } = [];
    public ObservableCollection<MemberRow> Members { get; } = [];
    public ObservableCollection<MemberRow> VisibleMembers { get; } = [];

    [ObservableProperty] private string _filter;
    [ObservableProperty] private string _memberFilter = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowMembers), nameof(ShowSequentialNote), nameof(ShowChooseHint), nameof(ChooseHint),
        nameof(MembersHeader), nameof(ShowPaddingNote))]
    private DatasetRow? _selectedDataset;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTextMode), nameof(IsBinaryMode), nameof(ShowPaddingNote))]
    private HostTransferMode _mode = HostTransferMode.Text;

    [ObservableProperty] private bool _trimTrailingBlanks = true;
    [ObservableProperty] private bool _verifyUploads = true;
    [ObservableProperty] private bool _expandTabs = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
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

    public bool ShowMembers => SelectedDataset is { IsPartitioned: true };
    public bool ShowSequentialNote => SelectedDataset is { IsSequential: true };
    public bool ShowChooseHint => SelectedDataset is not { IsSupported: true };

    public string ChooseHint => SelectedDataset is { IsSupported: false } dataset
        ? $"{dataset.Name} cannot be opened in this release (DSORG {(dataset.Dsorg.Length > 0 ? dataset.Dsorg : "unknown")})."
        : "Choose a dataset on the left.";

    public string MembersHeader => SelectedDataset is { IsPartitioned: true } dataset
        ? $"{dataset.Name} · {Plural(Members.Count, "member")}"
        : "";

    /// <summary>Binary transfers to fixed-length records are padded to whole records (compatibility log,
    /// binary-fixed-padding), so the bar says so while it applies.</summary>
    public bool ShowPaddingNote => IsBinaryMode && SelectedDataset?.Attributes.RecordFormat == RecordFormatFamily.Fixed;

    public IReadOnlyList<MemberRow> SelectedMembers => _selectedMembers;

    /// <summary>The window pushes the member list's selection here; a list box's own selected items are not bindable
    /// both ways in a way the tests can drive.</summary>
    public void SetSelectedMembers(IEnumerable<MemberRow> members)
    {
        _selectedMembers = [.. members];
        OnPropertyChanged(nameof(SelectedMembers));
        NotifyCommands();
    }

    partial void OnSelectedDatasetChanged(DatasetRow? value)
    {
        if (value is { IsSupported: true }) Mode = value.Attributes.RecordFormat == RecordFormatFamily.Undefined ? HostTransferMode.Binary : HostTransferMode.Text;
        _ = LoadMembersAsync(value);
    }

    partial void OnMemberFilterChanged(string value) => RefreshVisibleMembers();

    partial void OnIsBusyChanged(bool value) => NotifyCommands();

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private Task ListAsync() => RunExclusiveAsync(ListCoreAsync, () => ListAsync());

    private async Task ListCoreAsync(CancellationToken token)
    {
        if (HostPath.DatasetPatternError(Filter) is { } problem)
        {
            StatusText = "✗ " + problem;
            return;
        }
        var pattern = Filter.Trim().ToUpperInvariant();
        StatusText = $"⟳ Listing {pattern}…";
        var entries = await _connection.RunAsync(service => service.ListDatasetsAsync(pattern, token));
        SelectedDataset = null;
        Datasets.Clear();
        foreach (var entry in entries) Datasets.Add(new DatasetRow(entry));
        StatusText = Plural(entries.Count, "dataset");
    }

    private Task LoadMembersAsync(DatasetRow? row)
    {
        ClearMembers();
        if (row is not { IsPartitioned: true }) return Task.CompletedTask;
        return RunExclusiveAsync(token => LoadMembersCoreAsync(row, token), () => LoadMembersAsync(SelectedDataset));
    }

    /// <summary>Also used inside other operations (after an upload or a delete), which already hold the busy flag.</summary>
    private async Task LoadMembersCoreAsync(DatasetRow row, CancellationToken token)
    {
        StatusText = $"⟳ Listing members of {row.Name}…";
        var entries = await _connection.RunAsync(service => service.ListMembersAsync(row.Path, token));
        if (!ReferenceEquals(SelectedDataset, row)) return;
        ClearMembers();
        foreach (var entry in entries)
        {
            if (HostPath.MemberNameError(entry.Name) is null) Members.Add(new MemberRow(row.Name, entry.Name));
        }
        RefreshVisibleMembers();
        OnPropertyChanged(nameof(MembersHeader));
        StatusText = MembersHeader;
    }

    private void ClearMembers()
    {
        Members.Clear();
        VisibleMembers.Clear();
        SetSelectedMembers([]);
        OnPropertyChanged(nameof(MembersHeader));
    }

    private void RefreshVisibleMembers()
    {
        VisibleMembers.Clear();
        var filter = MemberFilter.Trim();
        foreach (var member in Members)
        {
            if (filter.Length == 0 || member.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)) VisibleMembers.Add(member);
        }
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

    [RelayCommand]
    private async Task OpenGuideAsync()
    {
        if (_openGuide is not null) await _openGuide();
    }

    /// <summary>Runs one operation with the busy flag, its own cancellation, and the failure rules in the class
    /// summary. A second operation while one runs is ignored; the commands are disabled anyway.</summary>
    private async Task RunExclusiveAsync(Func<CancellationToken, Task> work, Func<Task>? retry = null)
    {
        if (IsBusy || _disposed) return;
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
        catch (HostFileException ex) when (ex.Kind is HostFileErrorKind.Unreachable or HostFileErrorKind.Unauthenticated or HostFileErrorKind.CertificateRejected)
        {
            _retry = retry;
            StatusText = "";
            ErrorText = HostFileMessages.Describe(ex);
        }
        catch (Exception ex)
        {
            StatusText = "✗ " + HostFileMessages.Describe(ex);
        }
        finally
        {
            _cts = null;
            IsBusy = false;
            OnPropertyChanged(nameof(CanRetry));
        }
    }

    private async Task<ConfirmOutcome> AskAsync(ConfirmationRequest request)
    {
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

    /// <summary>A file dialog that fails to open is a status line, not a failed operation.</summary>
    private async Task<T?> TryPickAsync<T>(Func<Task<T>> pick)
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

    private static string Plural(int count, string noun) => count == 1 ? $"1 {noun}" : $"{count} {noun}s";

    /// <summary>Every command whose CanExecute reads the busy flag, the dataset or the selection. The transfer tasks
    /// add their commands here as they create them.</summary>
    private void NotifyCommands()
    {
        ListCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts?.Cancel();
        Confirmation?.CancelCommand.Execute(null);
        _connection.Dispose();
    }
}
```

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~MvsmfBrowserViewModelTests"`
Expected: PASS. If `Busy_blocks…` hangs, check that `FakeHostFileService.EnterAsync` waits with the token (`gate.Task.WaitAsync(token)`).

- [ ] **Step 5: Commit**

```bash
git add src/LizTerm.App/ViewModels tests/LizTerm.App.Tests/ViewModels
git commit -m "Add the mvsMF Browser view model's listing and member panes

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 9: Browser downloads

**Files:**
- Create: `src/LizTerm.App/ViewModels/MvsmfBrowserViewModel.Downloads.cs`
- Modify: `src/LizTerm.App/ViewModels/MvsmfBrowserViewModel.cs` (`NotifyCommands` gains `DownloadCommand.NotifyCanExecuteChanged();`)
- Test: `tests/LizTerm.App.Tests/ViewModels/MvsmfBrowserDownloadTests.cs`

**Interfaces:**
- Consumes: Task 8's private members; `HostFileTransfer.DownloadAsync(IHostFileService, HostPath, string destinationFile, DownloadOptions, IProgress<long>?, CancellationToken) : Task<long>`; `DownloadOptions(HostTransferMode Mode, bool TrimTrailingBlanks = true, string? LineEnding = null)`; `IFilePicker.PickSaveLocationAsync(string suggestedFileName, string title, IReadOnlyList<SaveFormat>? formats = null)`, `IFilePicker.PickFolderAsync(string title)`.
- Produces: `MvsmfBrowserViewModel.DownloadCommand`, `MvsmfBrowserViewModel.ParallelDownloads = 2`.

Spec §4 (Download). One item (a sequential dataset, or one member) uses the save dialog, which asks about replacing on its own; several members use a folder, ask Replace / Skip (apply to all) for files already there before starting, and run two at a time. Text files get `.txt`, binary files no extension. Row statuses: `⟳ Waiting`, `⟳ Running`, `⟳ Running · N bytes`, `✓ Done · N bytes`, `✗ Failed: …`, `– Skipped: the file exists`, `– Cancelled`. Byte counts use invariant `N0` formatting.

- [ ] **Step 1: Write the failing tests**

`tests/LizTerm.App.Tests/ViewModels/MvsmfBrowserDownloadTests.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Globalization;
using System.Text;
using LizTerm.Core.HostFiles;

namespace LizTerm.App.Tests.ViewModels;

public sealed class MvsmfBrowserDownloadTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("lizterm-browser-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private string Local(string name) => Path.Combine(_folder, name);

    private static async Task<BrowserTestHost> ChosenAsync(string dataset = "MVSCE02.CNTL")
    {
        var t = BrowserTestHost.Create();
        foreach (var member in new[] { "ALLOC", "COMPILE", "HELLO" })
            t.Host.Text[$"MVSCE02.CNTL({member})"] = [$"//{member} JOB   ", "//STEP EXEC PGM=IEFBR14"];
        t.Host.Binary["MVSCE02.UFSHOME"] = [0x61, 0x61, 0x00];
        await t.ChooseAsync(dataset);
        return t;
    }

    [Fact]
    public async Task One_member_downloads_through_the_save_dialog_as_text()
    {
        var t = await ChosenAsync();
        t.Select("HELLO");
        t.Picker.Result = Local("hello.txt");

        await t.Vm.DownloadCommand.ExecuteAsync(null);

        Assert.Equal(new[] { "save:HELLO.txt" }, t.Picker.Calls);
        var expected = "//HELLO JOB" + Environment.NewLine + "//STEP EXEC PGM=IEFBR14" + Environment.NewLine;
        Assert.Equal(expected, await File.ReadAllTextAsync(Local("hello.txt"), TestContext.Current.CancellationToken));
        var bytes = Encoding.UTF8.GetByteCount(expected);
        Assert.Equal($"✓ Done · {bytes.ToString("N0", CultureInfo.InvariantCulture)} bytes", t.Vm.Members.Single(m => m.Name == "HELLO").Status);
        Assert.Equal($"✓ Downloaded MVSCE02.CNTL(HELLO) to {Local("hello.txt")}.", t.Vm.StatusText);
    }

    [Fact]
    public async Task Trailing_blanks_can_be_kept()
    {
        var t = await ChosenAsync();
        t.Select("HELLO");
        t.Vm.TrimTrailingBlanks = false;
        t.Picker.Result = Local("hello.txt");

        await t.Vm.DownloadCommand.ExecuteAsync(null);

        Assert.StartsWith("//HELLO JOB   " + Environment.NewLine, await File.ReadAllTextAsync(Local("hello.txt"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_sequential_dataset_downloads_itself_and_binary_gets_no_extension()
    {
        var t = await ChosenAsync("MVSCE02.UFSHOME");
        t.Picker.Result = Local("UFSHOME");

        await t.Vm.DownloadCommand.ExecuteAsync(null);

        Assert.Equal(new[] { "save:UFSHOME" }, t.Picker.Calls);
        Assert.Equal(new byte[] { 0x61, 0x61, 0x00 }, await File.ReadAllBytesAsync(Local("UFSHOME"), TestContext.Current.CancellationToken));
        Assert.Equal($"✓ Downloaded MVSCE02.UFSHOME to {Local("UFSHOME")}.", t.Vm.StatusText);
    }

    [Fact]
    public async Task A_cancelled_save_dialog_downloads_nothing()
    {
        var t = await ChosenAsync();
        t.Select("HELLO");

        await t.Vm.DownloadCommand.ExecuteAsync(null);

        Assert.DoesNotContain(t.Host.CallsSnapshot(), c => c.StartsWith("readtext:"));
        Assert.Empty(Directory.GetFiles(_folder));
    }

    [Fact]
    public async Task A_file_dialog_that_cannot_open_is_a_status_line()
    {
        var t = await ChosenAsync();
        t.Select("HELLO");
        t.Picker.Exception = new InvalidOperationException("no dialog");

        await t.Vm.DownloadCommand.ExecuteAsync(null);

        Assert.Equal("✗ Could not open the file dialog: no dialog", t.Vm.StatusText);
    }

    [Fact]
    public async Task Several_members_go_to_a_folder_two_at_a_time()
    {
        var t = await ChosenAsync();
        t.Select("ALLOC", "COMPILE", "HELLO");
        t.Picker.FolderResult = _folder;
        t.Host.Gate = new TaskCompletionSource();

        var download = t.Vm.DownloadCommand.ExecuteAsync(null);
        await Wait.UntilAsync(() => t.Host.CallsSnapshot().Count(c => c.StartsWith("readtext:")) == 2, "two downloads to start");
        Assert.Equal(new[] { "⟳ Running", "⟳ Running", "⟳ Waiting" }, t.Vm.Members.Select(m => m.Status));
        t.Host.Gate.SetResult();
        await download;

        Assert.Equal(2, t.Host.MaxConcurrent);
        Assert.Equal(new[] { "folder:Download 3 members of MVSCE02.CNTL" }, t.Picker.Calls);
        Assert.All(t.Vm.Members, m => Assert.StartsWith("✓ Done · ", m.Status));
        Assert.Equal(new[] { "ALLOC.txt", "COMPILE.txt", "HELLO.txt" }, Directory.GetFiles(_folder).Select(Path.GetFileName).Order());
        Assert.Equal($"✓ Downloaded 3 of 3 members to {_folder}.", t.Vm.StatusText);
    }

    [Fact]
    public async Task Files_already_there_ask_replace_or_skip_once_for_all()
    {
        var t = await ChosenAsync();
        t.Select("ALLOC", "COMPILE", "HELLO");
        t.Picker.FolderResult = _folder;
        await File.WriteAllTextAsync(Local("ALLOC.txt"), "mine", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Local("COMPILE.txt"), "mine", TestContext.Current.CancellationToken);

        var download = t.Vm.DownloadCommand.ExecuteAsync(null);
        await Wait.UntilAsync(() => t.Vm.Confirmation is not null, "the replace question");
        var question = t.Vm.Confirmation!;
        Assert.Equal($"ALLOC.txt already exists in {_folder}.", question.Message);
        Assert.Equal(("Replace", "Skip", true), (question.PrimaryLabel, question.SecondaryLabel, question.OffersApplyToAll));
        question.ApplyToAll = true;
        question.SecondaryCommand.Execute(null);
        await download;

        Assert.Equal(new[] { "– Skipped: the file exists", "– Skipped: the file exists" }, t.Vm.Members.Take(2).Select(m => m.Status));
        Assert.StartsWith("✓ Done", t.Vm.Members[2].Status);
        Assert.Equal("mine", await File.ReadAllTextAsync(Local("ALLOC.txt"), TestContext.Current.CancellationToken));
        Assert.Equal($"⚠ Downloaded 1 of 3 members to {_folder}.", t.Vm.StatusText);
        Assert.False(t.Vm.HasConfirmation);
    }

    [Fact]
    public async Task Replace_overwrites_and_cancel_at_the_question_downloads_nothing()
    {
        var t = await ChosenAsync();
        t.Select("ALLOC", "HELLO");
        t.Picker.FolderResult = _folder;
        await File.WriteAllTextAsync(Local("ALLOC.txt"), "mine", TestContext.Current.CancellationToken);

        var first = t.Vm.DownloadCommand.ExecuteAsync(null);
        await Wait.UntilAsync(() => t.Vm.Confirmation is not null, "the replace question");
        t.Vm.Confirmation!.CancelCommand.Execute(null);
        await first;
        Assert.Equal("– Download cancelled.", t.Vm.StatusText);
        Assert.DoesNotContain(t.Host.CallsSnapshot(), c => c.StartsWith("readtext:"));

        var second = t.Vm.DownloadCommand.ExecuteAsync(null);
        await Wait.UntilAsync(() => t.Vm.Confirmation is not null, "the replace question again");
        t.Vm.Confirmation!.PrimaryCommand.Execute(null);
        await second;
        Assert.StartsWith("//ALLOC JOB", await File.ReadAllTextAsync(Local("ALLOC.txt"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_failed_member_is_marked_and_the_others_finish()
    {
        var t = await ChosenAsync();
        t.Select("ALLOC", "COMPILE", "HELLO");
        t.Picker.FolderResult = _folder;
        t.Host.Failures["readtext:MVSCE02.CNTL(COMPILE)"] = new HostFileException(HostFileErrorKind.CannotOpen, "x", 3);

        await t.Vm.DownloadCommand.ExecuteAsync(null);

        Assert.Equal("✗ Failed: Not found, not authorized, or cannot be opened.", t.Vm.Members[1].Status);
        Assert.Equal($"⚠ Downloaded 2 of 3 members to {_folder}.", t.Vm.StatusText);
        Assert.False(File.Exists(Local("COMPILE.txt")));
    }

    [Fact]
    public async Task Cancel_stops_running_and_waiting_downloads()
    {
        var t = await ChosenAsync();
        t.Select("ALLOC", "COMPILE", "HELLO");
        t.Picker.FolderResult = _folder;
        t.Host.Gate = new TaskCompletionSource();

        var download = t.Vm.DownloadCommand.ExecuteAsync(null);
        await Wait.UntilAsync(() => t.Host.CallsSnapshot().Count(c => c.StartsWith("readtext:")) == 2, "two downloads to start");
        t.Vm.CancelCommand.Execute(null);
        await download;

        Assert.All(t.Vm.Members, m => Assert.Equal("– Cancelled", m.Status));
        Assert.Equal("– Download cancelled.", t.Vm.StatusText);
        Assert.Empty(Directory.GetFiles(_folder));
        Assert.False(t.Vm.IsBusy);
    }

    [Fact]
    public async Task Download_needs_a_member_in_a_pds_but_not_in_a_sequential_dataset()
    {
        var t = await ChosenAsync();
        Assert.False(t.Vm.DownloadCommand.CanExecute(null));
        t.Select("HELLO");
        Assert.True(t.Vm.DownloadCommand.CanExecute(null));

        await t.ChooseAsync("MVSCE02.UFSHOME");
        Assert.True(t.Vm.DownloadCommand.CanExecute(null));
        await t.ChooseAsync("MVSCE02.DB");
        Assert.False(t.Vm.DownloadCommand.CanExecute(null));
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~MvsmfBrowserDownloadTests"`
Expected: build FAILS (`DownloadCommand` not found).

- [ ] **Step 3: Implement**

`src/LizTerm.App/ViewModels/MvsmfBrowserViewModel.Downloads.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using LizTerm.App.HostFiles;
using LizTerm.Core.HostFiles;

namespace LizTerm.App.ViewModels;

public sealed partial class MvsmfBrowserViewModel
{
    public const int ParallelDownloads = 2;

    private bool CanDownload =>
        !IsBusy && SelectedDataset is { IsSupported: true } dataset && (dataset.IsSequential || _selectedMembers.Count > 0);

    private string Extension => Mode == HostTransferMode.Text ? ".txt" : "";

    [RelayCommand(CanExecute = nameof(CanDownload))]
    private Task DownloadAsync() => RunExclusiveAsync(DownloadCoreAsync);

    private async Task DownloadCoreAsync(CancellationToken token)
    {
        var dataset = SelectedDataset!;
        var options = new DownloadOptions(Mode, TrimTrailingBlanks);
        if (dataset.IsSequential || _selectedMembers.Count == 1)
        {
            var row = dataset.IsSequential ? null : _selectedMembers[0];
            var path = row?.Path ?? dataset.Path;
            var suggested = (path.Member ?? path.Dataset[(path.Dataset.LastIndexOf('.') + 1)..]) + Extension;
            var file = await TryPickAsync(() => _picker.PickSaveLocationAsync(suggested, $"Download {path}"));
            if (file is null) return;
            if (await DownloadOneAsync(path, file, options, row, token)) StatusText = $"✓ Downloaded {path} to {file}.";
            else if (row is not null) StatusText = row.Status;
            return;
        }

        var members = _selectedMembers.ToList();
        var folder = await TryPickAsync(() => _picker.PickFolderAsync($"Download {members.Count} members of {dataset.Name}"));
        if (folder is null) return;
        foreach (var member in members) member.Status = "";

        var plan = new List<(MemberRow Row, string File)>();
        bool? replaceAll = null;
        foreach (var member in members)
        {
            var file = Path.Combine(folder, member.Name + Extension);
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
            plan.Add((member, file));
        }

        using var slots = new SemaphoreSlim(ParallelDownloads);
        var results = await Task.WhenAll(plan.Select(async item =>
        {
            try
            {
                await slots.WaitAsync(token);
            }
            catch (OperationCanceledException)
            {
                item.Row.Status = "– Cancelled";
                return false;
            }
            try
            {
                return await DownloadOneAsync(item.Row.Path, item.File, options, item.Row, token);
            }
            finally
            {
                slots.Release();
            }
        }));

        if (token.IsCancellationRequested)
        {
            StatusText = "– Download cancelled.";
            return;
        }
        var done = results.Count(ok => ok);
        StatusText = $"{(done == members.Count ? "✓" : "⚠")} Downloaded {done} of {Plural(members.Count, "member")} to {folder}.";
    }

    /// <summary>One transfer, reported on the member's row, or on the status line for a sequential dataset.</summary>
    private async Task<bool> DownloadOneAsync(HostPath path, string file, DownloadOptions options, MemberRow? row, CancellationToken token)
    {
        void Show(string text)
        {
            if (row is not null) row.Status = text;
            else StatusText = text;
        }

        var progress = new RowProgress(_dispatch, bytes => Show($"⟳ Running · {Bytes(bytes)} bytes"));
        Show("⟳ Running");
        try
        {
            var written = await _connection.RunAsync(service => HostFileTransfer.DownloadAsync(service, path, file, options, progress, token));
            progress.Close();
            Show($"✓ Done · {Bytes(written)} bytes");
            return true;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            progress.Close();
            Show("– Cancelled");
            return false;
        }
        catch (Exception ex)
        {
            progress.Close();
            Show("✗ Failed: " + HostFileMessages.Describe(ex));
            return false;
        }
    }

    private static string Bytes(long count) => count.ToString("N0", CultureInfo.InvariantCulture);

    /// <summary>Progress arrives on a backend thread and goes through the dispatcher; once the transfer's result is
    /// shown, a report still in the queue must not overwrite it.</summary>
    private sealed class RowProgress(Action<Action> dispatch, Action<long> show) : IProgress<long>
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

In `MvsmfBrowserViewModel.cs`, add `DownloadCommand.NotifyCanExecuteChanged();` to `NotifyCommands()`.

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~MvsmfBrowser"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/LizTerm.App/ViewModels tests/LizTerm.App.Tests/ViewModels
git commit -m "Download mvsMF members and datasets, two at a time into a folder

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 10: Browser uploads

**Files:**
- Modify: `src/LizTerm.App/ViewModels/MvsmfBrowserRows.cs` (add `UploadRow`), `src/LizTerm.App/ViewModels/MvsmfBrowserViewModel.cs` (`NotifyCommands` gains `UploadCommand.NotifyCanExecuteChanged(); StartUploadCommand.NotifyCanExecuteChanged(); CloseReviewCommand.NotifyCanExecuteChanged();`)
- Create: `src/LizTerm.App/ViewModels/MvsmfBrowserViewModel.Uploads.cs`
- Test: `tests/LizTerm.App.Tests/ViewModels/MvsmfBrowserUploadTests.cs`

**Interfaces:**
- Consumes: Task 8's private members; Core `HostFileTransfer.CheckTextFile(string, DatasetAttributes, TextUploadOptions?)`, `HostFileTransfer.UploadTextAsync(IHostFileService, HostPath, TextUploadResult, bool verify, CancellationToken) : Task<UploadOutcome>`, `HostFileTransfer.UploadBinaryAsync(IHostFileService, HostPath, string, CancellationToken)`, `UploadOutcome(UploadVerification Verification, int? DiffersAtLine)`, `TextUploadOptions(bool ExpandTabs = true, int TabWidth = 8)`; `HostFileMessages.DescribeUploadFailure`; `IFilePicker.PickFilesToSendAsync(string)`, `IFilePicker.PickFileToSendAsync()`.
- Produces: `UploadRow(string localPath)` with `LocalPath`, `FileName`, `MemberName` (editable), `NameProblem`, `Check`, `ReadProblem`, `Problems`, `IsBlocked`, `Status`; VM `Uploads`, `IsReviewingUpload`, `UploadFinished`, `ReviewMessage`, `UploadHeader`, `UploadCommand`, `StartUploadCommand`, `CloseReviewCommand`.

Spec §4 (Upload) and §5.2–§5.3. Into a PDS: pick files, review (member name from the file name up to the first dot, upper-cased, editable; text checks shown), then send one at a time. Row statuses: `⟳ Waiting`, `⟳ Sending`, `✓ Uploaded and verified`, `✓ Uploaded`, `⚠ Uploaded, but the host copy differs at line N`, `✗ Failed: …`, `– Not sent`, `– Skipped: the member exists`, `– Cancelled`. Into a sequential dataset: one file, confirm the replacement, check, send.

- [ ] **Step 1: Write the failing tests**

`tests/LizTerm.App.Tests/ViewModels/MvsmfBrowserUploadTests.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.Core.HostFiles;

namespace LizTerm.App.Tests.ViewModels;

public sealed class MvsmfBrowserUploadTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("lizterm-upload-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private string Write(string name, string text)
    {
        var path = Path.Combine(_folder, name);
        File.WriteAllText(path, text);
        return path;
    }

    private async Task<BrowserTestHost> ReviewAsync(params string[] files)
    {
        var t = BrowserTestHost.Create();
        await t.ChooseAsync("MVSCE02.CNTL");
        t.Picker.Results = files;
        await t.Vm.UploadCommand.ExecuteAsync(null);
        return t;
    }

    private static async Task StartAsync(BrowserTestHost t) => await t.Vm.StartUploadCommand.ExecuteAsync(null);

    [Fact]
    public async Task Choosing_files_opens_the_review_with_names_and_checks()
    {
        var t = await ReviewAsync(
            Write("hello.jcl", "//HELLO JOB\n"),
            Write("long.jcl", new string('X', 81) + "\n"),
            Write("tabs.jcl", "A\tB\n"),
            Write("bad-name.txt", "x\n"));

        Assert.True(t.Vm.IsReviewingUpload);
        Assert.Equal("Upload to MVSCE02.CNTL", t.Vm.UploadHeader);
        Assert.Equal(new[] { "open-many:Upload to MVSCE02.CNTL" }, t.Picker.Calls);
        Assert.Equal(new[] { "HELLO", "LONG", "TABS", "BAD-NAME" }, t.Vm.Uploads.Select(u => u.MemberName));
        Assert.Equal("", t.Vm.Uploads[0].Problems);
        Assert.Equal("✗ Line 1 is 81 characters; the limit is 80.", t.Vm.Uploads[1].Problems);
        Assert.True(t.Vm.Uploads[1].IsBlocked);
        Assert.Equal("⚠ 1 line contains tab characters.", t.Vm.Uploads[2].Problems);
        Assert.False(t.Vm.Uploads[2].IsBlocked);
        Assert.Equal("✗ A member name cannot contain '-'.", t.Vm.Uploads[3].NameProblem);
        Assert.False(t.Vm.UploadCommand.CanExecute(null));
        Assert.True(t.Vm.StartUploadCommand.CanExecute(null));
    }

    [Fact]
    public async Task Bad_names_stop_the_upload_until_fixed_and_blocked_files_are_not_sent()
    {
        var t = await ReviewAsync(
            Write("hello.jcl", "//HELLO JOB\n\n//END\n"),
            Write("long.jcl", new string('X', 81)),
            Write("bad-name.txt", "x\n"));

        await StartAsync(t);
        Assert.Equal("✗ Fix the member names marked ✗ first.", t.Vm.ReviewMessage);
        Assert.DoesNotContain(t.Host.CallsSnapshot(), c => c.StartsWith("write"));

        t.Vm.Uploads[2].MemberName = "badname";
        await StartAsync(t);

        Assert.Null(t.Vm.ReviewMessage);
        Assert.Equal(new[] { "✓ Uploaded and verified", "– Not sent", "✓ Uploaded and verified" }, t.Vm.Uploads.Select(u => u.Status));
        Assert.Equal(new[] { "//HELLO JOB", "", "//END" }, t.Host.Text["MVSCE02.CNTL(HELLO)"]);
        Assert.Contains("writetext:MVSCE02.CNTL(BADNAME):1", t.Host.CallsSnapshot());
        Assert.Equal("⚠ Uploaded 2 of 3 files to MVSCE02.CNTL.", t.Vm.StatusText);
        Assert.True(t.Vm.UploadFinished);
        Assert.False(t.Vm.StartUploadCommand.CanExecute(null));
        Assert.Contains(t.Vm.Members, m => m.Name == "BADNAME");
    }

    [Fact]
    public async Task Two_files_for_one_member_are_refused()
    {
        var t = await ReviewAsync(Write("a.jcl", "x\n"), Write("a.txt", "y\n"));

        await StartAsync(t);

        Assert.Equal("✗ Two files would become member A.", t.Vm.ReviewMessage);
        Assert.DoesNotContain(t.Host.CallsSnapshot(), c => c.StartsWith("write"));
    }

    [Fact]
    public async Task An_existing_member_asks_replace_or_skip()
    {
        var t = await ReviewAsync(Write("alloc.jcl", "new\n"), Write("compile.jcl", "new\n"));
        t.Host.Text["MVSCE02.CNTL(ALLOC)"] = ["old"];

        var upload = t.Vm.StartUploadCommand.ExecuteAsync(null);
        await Wait.UntilAsync(() => t.Vm.Confirmation is not null, "the replace question");
        var question = t.Vm.Confirmation!;
        Assert.Equal("Member ALLOC already exists in MVSCE02.CNTL.", question.Message);
        Assert.Equal(("Replace", "Skip", true), (question.PrimaryLabel, question.SecondaryLabel, question.OffersApplyToAll));
        question.SecondaryCommand.Execute(null);
        await Wait.UntilAsync(() => t.Vm.Confirmation is not null && !ReferenceEquals(t.Vm.Confirmation, question), "the second question");
        t.Vm.Confirmation!.PrimaryCommand.Execute(null);
        await upload;

        Assert.Equal("– Skipped: the member exists", t.Vm.Uploads[0].Status);
        Assert.Equal(new[] { "old" }, t.Host.Text["MVSCE02.CNTL(ALLOC)"]);
        Assert.Equal("✓ Uploaded and verified", t.Vm.Uploads[1].Status);
    }

    [Fact]
    public async Task Tabs_are_expanded_unless_turned_off()
    {
        var t = await ReviewAsync(Write("tabs.jcl", "A\tB\n"));
        t.Vm.ExpandTabs = false;
        Assert.Equal(new[] { "A\tB" }, t.Vm.Uploads[0].Check!.Lines);
        t.Vm.ExpandTabs = true;

        await StartAsync(t);

        Assert.Equal(new[] { "A       B" }, t.Host.Text["MVSCE02.CNTL(TABS)"]);
    }

    [Fact]
    public async Task Without_verification_nothing_is_read_back()
    {
        var t = await ReviewAsync(Write("hello.jcl", "x\n"));
        t.Vm.VerifyUploads = false;

        await StartAsync(t);

        Assert.Equal("✓ Uploaded", t.Vm.Uploads[0].Status);
        Assert.DoesNotContain(t.Host.CallsSnapshot(), c => c.StartsWith("readtext:"));
        Assert.Equal("✓ Uploaded 1 of 1 file to MVSCE02.CNTL.", t.Vm.StatusText);
    }

    [Fact]
    public async Task A_failure_mid_write_warns_about_a_partial_member()
    {
        var t = await ReviewAsync(Write("hello.jcl", "x\n"));
        t.Host.Failures["writetext:MVSCE02.CNTL(HELLO)"] = new HostFileException(HostFileErrorKind.ServerError, "x", 3);

        await StartAsync(t);

        Assert.Equal("✗ Failed: Server error (reason 3). The member may be partly written.", t.Vm.Uploads[0].Status);
    }

    [Fact]
    public async Task Binary_uploads_send_the_bytes_unchecked()
    {
        var t = BrowserTestHost.Create();
        await t.ChooseAsync("MVSCE02.LOAD");
        var file = Path.Combine(_folder, "prog2.bin");
        await File.WriteAllBytesAsync(file, [1, 2, 3], TestContext.Current.CancellationToken);
        t.Picker.Results = [file];
        await t.Vm.UploadCommand.ExecuteAsync(null);
        Assert.Null(t.Vm.Uploads[0].Check);

        await StartAsync(t);

        Assert.Equal(new byte[] { 1, 2, 3 }, t.Host.Binary["MVSCE02.LOAD(PROG2)"]);
        Assert.Equal("✓ Uploaded", t.Vm.Uploads[0].Status);
    }

    [Fact]
    public async Task Cancel_stops_the_file_being_sent_and_the_rest()
    {
        var t = await ReviewAsync(Write("one.jcl", "x\n"), Write("two.jcl", "y\n"));
        t.Host.Gate = new TaskCompletionSource();

        var upload = t.Vm.StartUploadCommand.ExecuteAsync(null);
        await Wait.UntilAsync(() => t.Host.CallsSnapshot().Any(c => c.StartsWith("writetext:")), "the first upload");
        t.Vm.CancelCommand.Execute(null);
        await upload;

        Assert.Equal(new[] { "– Cancelled", "– Cancelled" }, t.Vm.Uploads.Select(u => u.Status));
        Assert.Equal("– Upload cancelled.", t.Vm.StatusText);
    }

    [Fact]
    public async Task Closing_the_review_clears_it()
    {
        var t = await ReviewAsync(Write("hello.jcl", "x\n"));

        t.Vm.CloseReviewCommand.Execute(null);

        Assert.False(t.Vm.IsReviewingUpload);
        Assert.Empty(t.Vm.Uploads);
        Assert.True(t.Vm.UploadCommand.CanExecute(null));
    }

    [Fact]
    public async Task A_sequential_dataset_asks_before_its_contents_are_replaced()
    {
        var t = BrowserTestHost.Create();
        await t.ChooseAsync("MVSCE02.UFSHOME");
        var file = Path.Combine(_folder, "data.bin");
        await File.WriteAllBytesAsync(file, [9], TestContext.Current.CancellationToken);
        t.Picker.Result = file;

        var declined = t.Vm.UploadCommand.ExecuteAsync(null);
        await Wait.UntilAsync(() => t.Vm.Confirmation is not null, "the replace question");
        Assert.Equal("Replace the contents of MVSCE02.UFSHOME with data.bin?", t.Vm.Confirmation!.Message);
        Assert.Equal("Replace", t.Vm.Confirmation.PrimaryLabel);
        Assert.False(t.Vm.Confirmation.HasSecondary);
        t.Vm.Confirmation.CancelCommand.Execute(null);
        await declined;
        Assert.Equal("– Upload cancelled.", t.Vm.StatusText);

        var accepted = t.Vm.UploadCommand.ExecuteAsync(null);
        await Wait.UntilAsync(() => t.Vm.Confirmation is not null, "the replace question again");
        t.Vm.Confirmation!.PrimaryCommand.Execute(null);
        await accepted;
        Assert.Equal(new byte[] { 9 }, t.Host.Binary["MVSCE02.UFSHOME"]);
        Assert.Equal($"✓ Uploaded data.bin to MVSCE02.UFSHOME.", t.Vm.StatusText);
        Assert.False(t.Vm.IsReviewingUpload);
    }

    [Fact]
    public async Task A_sequential_text_upload_is_checked_first()
    {
        var t = BrowserTestHost.Create();
        await t.ChooseAsync("MVSCE02.UFSHOME");
        t.Vm.IsTextMode = true;
        t.Picker.Result = Write("wide.txt", new string('X', 5000));

        var upload = t.Vm.UploadCommand.ExecuteAsync(null);
        await Wait.UntilAsync(() => t.Vm.Confirmation is not null, "the replace question");
        t.Vm.Confirmation!.PrimaryCommand.Execute(null);
        await upload;

        Assert.Equal("✗ Not sent: Line 1 is 5000 characters; the limit is 4096.", t.Vm.StatusText);
        Assert.DoesNotContain(t.Host.CallsSnapshot(), c => c.StartsWith("write"));
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~MvsmfBrowserUploadTests"`
Expected: build FAILS (`UploadCommand` not found).

- [ ] **Step 3: Implement**

Append to `src/LizTerm.App/ViewModels/MvsmfBrowserRows.cs`:

```csharp
/// <summary>One local file in the upload review. The member name starts as the file name up to its first dot,
/// upper-cased, and stays editable; <see cref="IsBlocked"/> rows are not sent.</summary>
public sealed partial class UploadRow : ObservableObject
{
    public UploadRow(string localPath)
    {
        LocalPath = localPath;
        FileName = System.IO.Path.GetFileName(localPath);
        _memberName = FileName.Split('.')[0].ToUpperInvariant();
    }

    public string LocalPath { get; }
    public string FileName { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NameProblem), nameof(IsBlocked))]
    private string _memberName;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Problems), nameof(IsBlocked))]
    private TextUploadResult? _check;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Problems), nameof(IsBlocked))]
    private string? _readProblem;

    [ObservableProperty] private string _status = "";

    public string UploadName => MemberName.Trim().ToUpperInvariant();

    public string? NameProblem => HostPath.MemberNameError(MemberName) is { } error ? "✗ " + error : null;

    public string Problems
    {
        get
        {
            var lines = new List<string>();
            if (ReadProblem is not null) lines.Add("✗ " + ReadProblem);
            if (Check is not null)
            {
                lines.AddRange(Check.Errors.Select(problem => "✗ " + problem.Message));
                lines.AddRange(Check.Warnings.Select(problem => "⚠ " + problem.Message));
            }
            return string.Join("\n", lines);
        }
    }

    public bool IsBlocked => NameProblem is not null || ReadProblem is not null || Check is { CanUpload: false };
}
```

`src/LizTerm.App/ViewModels/MvsmfBrowserViewModel.Uploads.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LizTerm.App.HostFiles;
using LizTerm.Core.HostFiles;

namespace LizTerm.App.ViewModels;

public sealed partial class MvsmfBrowserViewModel
{
    public ObservableCollection<UploadRow> Uploads { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UploadHeader))]
    private bool _isReviewingUpload;

    [ObservableProperty] private bool _uploadFinished;
    [ObservableProperty] private string? _reviewMessage;

    public string UploadHeader => SelectedDataset is { } dataset ? $"Upload to {dataset.Name}" : "";

    private bool CanUpload => !IsBusy && !IsReviewingUpload && SelectedDataset is { IsSupported: true };
    private bool CanStartUpload => !IsBusy && IsReviewingUpload && !UploadFinished;
    private bool CanCloseReview => !IsBusy && IsReviewingUpload;

    partial void OnIsReviewingUploadChanged(bool value) => NotifyCommands();

    partial void OnUploadFinishedChanged(bool value) => NotifyCommands();

    partial void OnExpandTabsChanged(bool value) => RecheckIfReviewing();

    partial void OnModeChanged(HostTransferMode value) => RecheckIfReviewing();

    [RelayCommand(CanExecute = nameof(CanUpload))]
    private async Task UploadAsync()
    {
        var dataset = SelectedDataset!;
        if (dataset.IsSequential)
        {
            await RunExclusiveAsync(token => UploadSequentialAsync(dataset, token));
            return;
        }
        var files = await TryPickAsync(() => _picker.PickFilesToSendAsync($"Upload to {dataset.Name}"));
        if (files is not { Count: > 0 }) return;
        Uploads.Clear();
        foreach (var file in files) Uploads.Add(new UploadRow(file));
        ReviewMessage = null;
        UploadFinished = false;
        IsReviewingUpload = true;
        Recheck(dataset);
    }

    [RelayCommand(CanExecute = nameof(CanStartUpload))]
    private Task StartUploadAsync() => RunExclusiveAsync(StartUploadCoreAsync);

    [RelayCommand(CanExecute = nameof(CanCloseReview))]
    private void CloseReview()
    {
        IsReviewingUpload = false;
        UploadFinished = false;
        ReviewMessage = null;
        Uploads.Clear();
    }

    private void RecheckIfReviewing()
    {
        if (IsReviewingUpload && !UploadFinished && SelectedDataset is { } dataset) Recheck(dataset);
    }

    private void Recheck(DatasetRow dataset)
    {
        foreach (var row in Uploads)
        {
            row.ReadProblem = null;
            row.Check = null;
            if (Mode != HostTransferMode.Text) continue;
            try
            {
                row.Check = HostFileTransfer.CheckTextFile(row.LocalPath, dataset.Attributes, new TextUploadOptions(ExpandTabs));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                row.ReadProblem = "Local file: " + ex.Message;
            }
        }
    }

    private async Task StartUploadCoreAsync(CancellationToken token)
    {
        var dataset = SelectedDataset!;
        Recheck(dataset);
        if (Uploads.Any(row => row.NameProblem is not null))
        {
            ReviewMessage = "✗ Fix the member names marked ✗ first.";
            return;
        }
        if (Uploads.GroupBy(row => row.UploadName).FirstOrDefault(group => group.Count() > 1) is { } clash)
        {
            ReviewMessage = $"✗ Two files would become member {clash.Key}.";
            return;
        }
        ReviewMessage = null;
        foreach (var row in Uploads) row.Status = "";

        var existing = Members.Select(member => member.Name).ToHashSet(StringComparer.Ordinal);
        var plan = new List<UploadRow>();
        bool? replaceAll = null;
        foreach (var row in Uploads)
        {
            if (row.IsBlocked)
            {
                row.Status = "– Not sent";
                continue;
            }
            if (existing.Contains(row.UploadName))
            {
                var replace = replaceAll;
                if (replace is null)
                {
                    var answer = await AskAsync(new ConfirmationRequest(
                        $"Member {row.UploadName} already exists in {dataset.Name}.", "Replace", "Skip", offersApplyToAll: true));
                    if (answer.Choice == ConfirmChoice.Cancel)
                    {
                        foreach (var each in Uploads) each.Status = "";
                        StatusText = "– Upload cancelled.";
                        return;
                    }
                    replace = answer.Choice == ConfirmChoice.Primary;
                    if (answer.ApplyToAll) replaceAll = replace;
                }
                if (replace == false)
                {
                    row.Status = "– Skipped: the member exists";
                    continue;
                }
            }
            row.Status = "⟳ Waiting";
            plan.Add(row);
        }

        var sent = 0;
        foreach (var row in plan)
        {
            if (token.IsCancellationRequested)
            {
                row.Status = "– Cancelled";
                continue;
            }
            row.Status = "⟳ Sending";
            var path = dataset.Path.WithMember(row.UploadName);
            try
            {
                if (Mode == HostTransferMode.Text)
                {
                    var check = row.Check!;
                    var outcome = await _connection.RunAsync(service => HostFileTransfer.UploadTextAsync(service, path, check, VerifyUploads, token));
                    row.Status = Describe(outcome);
                }
                else
                {
                    await _connection.RunAsync(service => HostFileTransfer.UploadBinaryAsync(service, path, row.LocalPath, token));
                    row.Status = "✓ Uploaded";
                }
                sent++;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                row.Status = "– Cancelled";
            }
            catch (Exception ex)
            {
                row.Status = "✗ Failed: " + HostFileMessages.DescribeUploadFailure(ex);
            }
        }

        UploadFinished = true;
        if (token.IsCancellationRequested)
        {
            StatusText = "– Upload cancelled.";
            return;
        }
        await LoadMembersCoreAsync(dataset, token);
        StatusText = $"{(sent == Uploads.Count ? "✓" : "⚠")} Uploaded {sent} of {Plural(Uploads.Count, "file")} to {dataset.Name}.";
    }

    private async Task UploadSequentialAsync(DatasetRow dataset, CancellationToken token)
    {
        var file = await TryPickAsync(() => _picker.PickFileToSendAsync());
        if (file is null) return;
        var name = Path.GetFileName(file);
        var answer = await AskAsync(new ConfirmationRequest($"Replace the contents of {dataset.Name} with {name}?", "Replace"));
        if (answer.Choice != ConfirmChoice.Primary)
        {
            StatusText = "– Upload cancelled.";
            return;
        }
        try
        {
            if (Mode == HostTransferMode.Text)
            {
                var check = HostFileTransfer.CheckTextFile(file, dataset.Attributes, new TextUploadOptions(ExpandTabs));
                if (!check.CanUpload)
                {
                    StatusText = "✗ Not sent: " + check.Errors[0].Message;
                    return;
                }
                var outcome = await _connection.RunAsync(service => HostFileTransfer.UploadTextAsync(service, dataset.Path, check, VerifyUploads, token));
                StatusText = outcome.Verification == UploadVerification.Differs
                    ? $"{Describe(outcome)} — {dataset.Name}"
                    : $"✓ Uploaded {name} to {dataset.Name}.";
            }
            else
            {
                await _connection.RunAsync(service => HostFileTransfer.UploadBinaryAsync(service, dataset.Path, file, token));
                StatusText = $"✓ Uploaded {name} to {dataset.Name}.";
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            StatusText = "✗ Failed: " + HostFileMessages.DescribeUploadFailure(ex);
        }
    }

    private static string Describe(UploadOutcome outcome) => outcome.Verification switch
    {
        UploadVerification.Matches => "✓ Uploaded and verified",
        UploadVerification.NotChecked => "✓ Uploaded",
        _ => $"⚠ Uploaded, but the host copy differs at line {outcome.DiffersAtLine}",
    };
}
```

In `MvsmfBrowserViewModel.cs`:
- add `UploadCommand.NotifyCanExecuteChanged(); StartUploadCommand.NotifyCanExecuteChanged(); CloseReviewCommand.NotifyCanExecuteChanged();` to `NotifyCommands()`;
- in `OnSelectedDatasetChanged`, before `_ = LoadMembersAsync(value);`, add `if (IsReviewingUpload) CloseReview();` (a review belongs to the dataset it was opened on) — the window disables the dataset list while a review is open, so this is a guard;
- add `nameof(UploadHeader)` to `_selectedDataset`'s `NotifyPropertyChangedFor` list.

Note on `OnModeChanged`: `Mode`'s generated partial is declared once, here in the uploads file; do not add another `OnModeChanged` elsewhere.

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~MvsmfBrowser"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/LizTerm.App/ViewModels tests/LizTerm.App.Tests/ViewModels
git commit -m "Upload files to mvsMF after a review of names and text checks

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 11: Browser member delete

**Files:**
- Create: `src/LizTerm.App/ViewModels/MvsmfBrowserViewModel.Delete.cs`
- Modify: `src/LizTerm.App/ViewModels/MvsmfBrowserViewModel.cs` (`NotifyCommands` gains `DeleteCommand.NotifyCanExecuteChanged();`)
- Test: `tests/LizTerm.App.Tests/ViewModels/MvsmfBrowserDeleteTests.cs`

**Interfaces:**
- Consumes: Task 8's private members; `IHostFileService.DeleteAsync(HostPath, CancellationToken)`.
- Produces: `MvsmfBrowserViewModel.DeleteCommand`.

Spec §4 (Delete): members only; the confirmation names them (at most five, then "and N more") and its button says **Delete N members**; the member list is fetched again afterwards, so the result is a summary line.

- [ ] **Step 1: Write the failing tests**

`tests/LizTerm.App.Tests/ViewModels/MvsmfBrowserDeleteTests.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.Core.HostFiles;

namespace LizTerm.App.Tests.ViewModels;

public class MvsmfBrowserDeleteTests
{
    private static async Task<BrowserTestHost> AskedAsync(BrowserTestHost t, Task running)
    {
        await Wait.UntilAsync(() => t.Vm.Confirmation is not null || running.IsCompleted, "the delete question");
        return t;
    }

    [Fact]
    public async Task Delete_names_the_members_and_removes_them_after_a_yes()
    {
        var t = BrowserTestHost.Create();
        await t.ChooseAsync("MVSCE02.CNTL");
        t.Select("ALLOC", "HELLO");

        var deleting = t.Vm.DeleteCommand.ExecuteAsync(null);
        await AskedAsync(t, deleting);
        var question = t.Vm.Confirmation!;
        Assert.Equal("Delete ALLOC, HELLO from MVSCE02.CNTL? This cannot be undone.", question.Message);
        Assert.Equal("Delete 2 members", question.PrimaryLabel);
        Assert.False(question.HasSecondary);
        question.PrimaryCommand.Execute(null);
        await deleting;

        Assert.Equal(new[] { "COMPILE" }, t.Vm.Members.Select(m => m.Name));
        Assert.Contains("delete:MVSCE02.CNTL(ALLOC)", t.Host.CallsSnapshot());
        Assert.Equal("✓ Deleted 2 of 2 members.", t.Vm.StatusText);
    }

    [Fact]
    public async Task One_member_says_one_member()
    {
        var t = BrowserTestHost.Create();
        await t.ChooseAsync("MVSCE02.CNTL");
        t.Select("HELLO");

        var deleting = t.Vm.DeleteCommand.ExecuteAsync(null);
        await AskedAsync(t, deleting);
        Assert.Equal("Delete 1 member", t.Vm.Confirmation!.PrimaryLabel);
        t.Vm.Confirmation.PrimaryCommand.Execute(null);
        await deleting;
        Assert.Equal("✓ Deleted 1 of 1 member.", t.Vm.StatusText);
    }

    [Fact]
    public async Task Many_members_are_named_up_to_five()
    {
        var t = BrowserTestHost.Create(seed: host => host.AddDataset("A.CNTL", members: ["M1", "M2", "M3", "M4", "M5", "M6", "M7"]));
        t.Vm.Filter = "A.**";
        await t.ChooseAsync("A.CNTL");
        t.Select("M1", "M2", "M3", "M4", "M5", "M6", "M7");

        var deleting = t.Vm.DeleteCommand.ExecuteAsync(null);
        await AskedAsync(t, deleting);

        Assert.Equal("Delete M1, M2, M3, M4, M5 and 2 more from A.CNTL? This cannot be undone.", t.Vm.Confirmation!.Message);
        t.Vm.Confirmation.CancelCommand.Execute(null);
        await deleting;
    }

    [Fact]
    public async Task Cancel_deletes_nothing()
    {
        var t = BrowserTestHost.Create();
        await t.ChooseAsync("MVSCE02.CNTL");
        t.Select("HELLO");

        var deleting = t.Vm.DeleteCommand.ExecuteAsync(null);
        await AskedAsync(t, deleting);
        t.Vm.Confirmation!.CancelCommand.Execute(null);
        await deleting;

        Assert.DoesNotContain(t.Host.CallsSnapshot(), c => c.StartsWith("delete:"));
        Assert.Equal(3, t.Vm.Members.Count);
    }

    [Fact]
    public async Task A_failed_delete_is_reported_and_the_rest_go()
    {
        var t = BrowserTestHost.Create();
        await t.ChooseAsync("MVSCE02.CNTL");
        t.Select("ALLOC", "HELLO");
        t.Host.Failures["delete:MVSCE02.CNTL(ALLOC)"] = new HostFileException(HostFileErrorKind.NotFound, "x", 5);

        var deleting = t.Vm.DeleteCommand.ExecuteAsync(null);
        await AskedAsync(t, deleting);
        t.Vm.Confirmation!.PrimaryCommand.Execute(null);
        await deleting;

        Assert.Equal("⚠ Deleted 1 of 2 members. ✗ ALLOC: Not found.", t.Vm.StatusText);
        Assert.Equal(new[] { "ALLOC", "COMPILE" }, t.Vm.Members.Select(m => m.Name));
    }

    [Fact]
    public async Task Delete_needs_selected_members_of_a_pds()
    {
        var t = BrowserTestHost.Create();
        await t.ChooseAsync("MVSCE02.CNTL");
        Assert.False(t.Vm.DeleteCommand.CanExecute(null));
        t.Select("HELLO");
        Assert.True(t.Vm.DeleteCommand.CanExecute(null));
        await t.ChooseAsync("MVSCE02.UFSHOME");
        Assert.False(t.Vm.DeleteCommand.CanExecute(null));
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~MvsmfBrowserDeleteTests"`
Expected: build FAILS (`DeleteCommand` not found).

- [ ] **Step 3: Implement**

`src/LizTerm.App/ViewModels/MvsmfBrowserViewModel.Delete.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using CommunityToolkit.Mvvm.Input;
using LizTerm.App.HostFiles;

namespace LizTerm.App.ViewModels;

public sealed partial class MvsmfBrowserViewModel
{
    private const int NamesInQuestion = 5;

    private bool CanDelete => !IsBusy && !IsReviewingUpload && SelectedDataset is { IsPartitioned: true } && _selectedMembers.Count > 0;

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private Task DeleteAsync() => RunExclusiveAsync(DeleteCoreAsync);

    private async Task DeleteCoreAsync(CancellationToken token)
    {
        var dataset = SelectedDataset!;
        var members = _selectedMembers.ToList();
        var names = string.Join(", ", members.Take(NamesInQuestion).Select(member => member.Name));
        if (members.Count > NamesInQuestion) names += $" and {members.Count - NamesInQuestion} more";
        var label = members.Count == 1 ? "Delete 1 member" : $"Delete {members.Count} members";
        var answer = await AskAsync(new ConfirmationRequest($"Delete {names} from {dataset.Name}? This cannot be undone.", label));
        if (answer.Choice != ConfirmChoice.Primary) return;

        var deleted = 0;
        var failures = new List<string>();
        foreach (var member in members)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                await _connection.RunAsync(service => service.DeleteAsync(member.Path, token));
                deleted++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failures.Add($"{member.Name}: {HostFileMessages.Describe(ex)}");
            }
        }
        await LoadMembersCoreAsync(dataset, token);
        StatusText = failures.Count == 0
            ? $"✓ Deleted {deleted} of {Plural(members.Count, "member")}."
            : $"⚠ Deleted {deleted} of {Plural(members.Count, "member")}. ✗ {string.Join("; ", failures)}";
    }
}
```

Add `DeleteCommand.NotifyCanExecuteChanged();` to `NotifyCommands()`.

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~MvsmfBrowser"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/LizTerm.App/ViewModels tests/LizTerm.App.Tests/ViewModels
git commit -m "Delete mvsMF members after a confirmation

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 12: The browser window

**Files:**
- Create: `src/LizTerm.App/Views/MvsmfBrowserWindow.axaml`, `src/LizTerm.App/Views/MvsmfBrowserWindow.axaml.cs`, `src/LizTerm.App/ViewModels/MvsmfBrowserConverters.cs`
- Modify: `src/LizTerm.App/ViewModels/MvsmfBrowserViewModel.Uploads.cs` (two view properties, below)
- Test: `tests/LizTerm.App.Tests/Views/MvsmfBrowserWindowTests.cs`

**Interfaces:**
- Consumes: `MvsmfBrowserViewModel` and its rows (Tasks 8–11), `BrowserTestHost` (Task 8).
- Produces: `MvsmfBrowserWindow` with named controls `PreviewStrip`, `GuideLink`, `FilterBox`, `ListButton`, `ErrorBanner`, `RetryButton`, `DatasetList`, `MemberPane`, `MemberHeader`, `MemberFilterBox`, `MemberList`, `SequentialNote`, `ChooseHint`, `ReviewPane`, `UploadList`, `ExpandTabsBox`, `StartUploadButton`, `CloseReviewButton`, `ConfirmationStrip`, `ApplyToAllBox`, `ConfirmCancelButton`, `ConfirmSecondaryButton`, `ConfirmPrimaryButton`, `TextModeButton`, `BinaryModeButton`, `TrimBox`, `VerifyBox`, `DownloadButton`, `UploadButton`, `DeleteButton`, `CancelButton`, `StatusLine`, `PaddingNote`, `Progress`.

Layout per the approved mockup (spec §4, ISPF-style capital column headers). The window is resizable (it is a working window, not a dialog), never refuses to close (closing cancels and disposes the view model), lists on open when the filter is not empty, and maps keys: Enter in the filter lists; Enter in the member list downloads; Delete in the member list deletes; Escape answers a pending question with Cancel, otherwise cancels a running operation, otherwise closes.

- [ ] **Step 1: Write the failing tests**

`tests/LizTerm.App.Tests/Views/MvsmfBrowserWindowTests.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using LizTerm.App.Tests.ViewModels;
using LizTerm.App.ViewModels;
using LizTerm.App.Views;

namespace LizTerm.App.Tests.Views;

public class MvsmfBrowserWindowTests
{
    private static (MvsmfBrowserWindow Window, BrowserTestHost T) Show(string? userid = "MVSCE02")
    {
        var t = BrowserTestHost.Create(userid);
        var window = new MvsmfBrowserWindow { DataContext = t.Vm };
        window.Show();
        window.Activate();
        return (window, t);
    }

    private static T Named<T>(Window window, string name) where T : Control => window.FindControl<T>(name)!;

    [AvaloniaFact]
    public async Task Opens_with_the_preview_strip_and_lists_the_users_datasets()
    {
        var (window, t) = Show();

        await Wait.UntilAsync(() => t.Vm.Datasets.Count == 4, "the first listing");

        Assert.Equal("mvsMF Browser — MVS/CE (Preview)", window.Title);
        Assert.True(Named<Border>(window, "PreviewStrip").IsVisible);
        Assert.StartsWith("⚠ Feature preview.", Named<Border>(window, "PreviewStrip").GetLogicalDescendants().OfType<TextBlock>().First().Text);
        Assert.Equal("MVSCE02.**", Named<TextBox>(window, "FilterBox").Text);
        Assert.Equal(4, Named<ListBox>(window, "DatasetList").ItemCount);
        Assert.True(Named<TextBlock>(window, "ChooseHint").IsVisible);
        Assert.True(window.CanResize);
    }

    [AvaloniaFact]
    public void Without_a_userid_nothing_is_listed_on_open()
    {
        var (_, t) = Show(userid: null);
        Assert.Empty(t.Host.CallsSnapshot());
    }

    [AvaloniaFact]
    public void The_column_headers_are_ispf_style_capitals()
    {
        var (window, _) = Show(userid: null);
        var headers = window.GetLogicalDescendants().OfType<TextBlock>().Select(b => b.Text).ToHashSet();
        foreach (var header in new[] { "NAME", "DSORG", "RECFM", "LRECL", "MEMBER", "STATUS" })
            Assert.Contains(header, headers);
    }

    [AvaloniaFact]
    public async Task Choosing_a_pds_shows_its_members_and_selection_reaches_the_view_model()
    {
        var (window, t) = Show();
        await Wait.UntilAsync(() => t.Vm.Datasets.Count == 4, "the first listing");

        Named<ListBox>(window, "DatasetList").SelectedIndex = 0;
        await Wait.UntilAsync(() => t.Vm.Members.Count == 3, "the members");
        var members = Named<ListBox>(window, "MemberList");
        members.SelectedItems!.Add(t.Vm.VisibleMembers[0]);
        members.SelectedItems.Add(t.Vm.VisibleMembers[2]);

        Assert.True(Named<Control>(window, "MemberPane").IsVisible);
        Assert.Equal("MVSCE02.CNTL · 3 members", Named<TextBlock>(window, "MemberHeader").Text);
        Assert.Equal(new[] { "ALLOC", "HELLO" }, t.Vm.SelectedMembers.Select(m => m.Name));
        Assert.True(Named<Button>(window, "DownloadButton").IsEffectivelyEnabled);
        Assert.True(Named<Button>(window, "DeleteButton").IsEffectivelyEnabled);
    }

    [AvaloniaFact]
    public async Task A_question_shows_in_the_strip_with_its_own_labels()
    {
        var (window, t) = Show();
        await Wait.UntilAsync(() => t.Vm.Datasets.Count == 4, "the first listing");
        t.Vm.SelectedDataset = t.Vm.Datasets[0];
        await Wait.UntilAsync(() => !t.Vm.IsBusy, "the members");
        t.Select("HELLO");

        _ = t.Vm.DeleteCommand.ExecuteAsync(null);
        await Wait.UntilAsync(() => t.Vm.HasConfirmation, "the question");

        Assert.True(Named<Border>(window, "ConfirmationStrip").IsVisible);
        Assert.Equal("Delete 1 member", Named<Button>(window, "ConfirmPrimaryButton").Content);
        Assert.False(Named<Button>(window, "ConfirmSecondaryButton").IsVisible);
        Assert.False(Named<CheckBox>(window, "ApplyToAllBox").IsVisible);

        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        await Wait.UntilAsync(() => !t.Vm.HasConfirmation, "the question to be cancelled");
        Assert.True(window.IsVisible);
    }

    [AvaloniaFact]
    public async Task The_error_banner_shows_with_a_mark_and_retry()
    {
        var (window, t) = Show(userid: null);
        t.Vm.Filter = "MVSCE02.**";
        t.Host.Failures["list:MVSCE02.**"] = new LizTerm.Core.HostFiles.HostFileException(LizTerm.Core.HostFiles.HostFileErrorKind.Unreachable, "Dataset list: cannot reach the host (refused).");

        await t.Vm.ListCommand.ExecuteAsync(null);

        Assert.True(Named<Border>(window, "ErrorBanner").IsVisible);
        Assert.Contains(window.FindControl<Border>("ErrorBanner")!.GetLogicalDescendants().OfType<TextBlock>(),
            b => b.Text == "✗ Dataset list: cannot reach the host (refused).");
        Assert.True(Named<Button>(window, "RetryButton").IsVisible);
    }

    [AvaloniaFact]
    public async Task The_upload_review_replaces_the_member_pane()
    {
        var (window, t) = Show();
        await Wait.UntilAsync(() => t.Vm.Datasets.Count == 4, "the first listing");
        t.Vm.SelectedDataset = t.Vm.Datasets[0];
        await Wait.UntilAsync(() => !t.Vm.IsBusy, "the members");
        var file = Path.Combine(Path.GetTempPath(), $"lizterm-{Guid.NewGuid():N}.jcl");
        await File.WriteAllTextAsync(file, "x\n");
        try
        {
            t.Picker.Results = [file];
            await t.Vm.UploadCommand.ExecuteAsync(null);

            Assert.True(Named<Control>(window, "ReviewPane").IsVisible);
            Assert.False(Named<Control>(window, "MemberPane").IsVisible);
            Assert.Equal(1, Named<ItemsControl>(window, "UploadList").ItemCount);
            Assert.False(Named<ListBox>(window, "DatasetList").IsEffectivelyEnabled);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [AvaloniaFact]
    public async Task Binary_mode_shows_the_padding_note()
    {
        var (window, t) = Show();
        await Wait.UntilAsync(() => t.Vm.Datasets.Count == 4, "the first listing");
        t.Vm.SelectedDataset = t.Vm.Datasets[0];
        await Wait.UntilAsync(() => !t.Vm.IsBusy, "the members");

        Named<RadioButton>(window, "BinaryModeButton").IsChecked = true;

        Assert.True(t.Vm.IsBinaryMode);
        Assert.True(Named<TextBlock>(window, "PaddingNote").IsVisible);
        Assert.Equal("⚠ Binary transfers to fixed-length datasets are padded to whole records.", Named<TextBlock>(window, "PaddingNote").Text);
    }

    [AvaloniaFact]
    public async Task Closing_cancels_and_releases_the_connection()
    {
        var (window, t) = Show();
        await Wait.UntilAsync(() => t.Vm.Datasets.Count == 4, "the first listing");

        window.Close();

        Assert.True(t.Host.Disposed);
    }

    [AvaloniaFact]
    public async Task Enter_in_the_filter_box_lists_again()
    {
        var (window, t) = Show();
        await Wait.UntilAsync(() => t.Vm.Datasets.Count == 4, "the first listing");
        var box = Named<TextBox>(window, "FilterBox");
        box.Focus();

        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);

        await Wait.UntilAsync(() => t.Host.CallsSnapshot().Count(c => c.StartsWith("list:")) == 2, "a second listing");
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~MvsmfBrowserWindowTests"`
Expected: build FAILS (`MvsmfBrowserWindow` not found).

- [ ] **Step 3: Implement**

`src/LizTerm.App/Views/MvsmfBrowserWindow.axaml`:

```xml
<!--
  This file is part of LizTerm.
  Copyright 2026 by CoffeeMuse
  SPDX-License-Identifier: BSD-3-Clause
-->
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:vm="using:LizTerm.App.ViewModels"
        x:Class="LizTerm.App.Views.MvsmfBrowserWindow"
        x:DataType="vm:MvsmfBrowserViewModel"
        Title="{Binding Title}"
        Width="880" Height="560" MinWidth="640" MinHeight="400"
        WindowStartupLocation="CenterOwner">
  <Window.Styles>
    <Style Selector="TextBlock.header">
      <Setter Property="FontWeight" Value="SemiBold" />
      <Setter Property="Foreground" Value="#A0A0A0" />
      <Setter Property="FontSize" Value="12" />
    </Style>
    <Style Selector="TextBlock.cell">
      <Setter Property="FontFamily" Value="Menlo, Consolas, monospace" />
    </Style>
    <Style Selector="Button.link">
      <Setter Property="Background" Value="Transparent" />
      <Setter Property="BorderThickness" Value="0" />
      <Setter Property="Padding" Value="0" />
      <Setter Property="Foreground" Value="#80B0FF" />
    </Style>
  </Window.Styles>

  <DockPanel>
    <Border x:Name="PreviewStrip" DockPanel.Dock="Top" Background="#3A3320" Padding="12,5">
      <StackPanel Orientation="Horizontal" Spacing="6">
        <TextBlock Text="⚠ Feature preview. mvsMF access is new and still being refined." VerticalAlignment="Center" />
        <Button x:Name="GuideLink" Classes="link" Command="{Binding OpenGuideCommand}">
          <TextBlock Text="What to expect…" TextDecorations="Underline" />
        </Button>
      </StackPanel>
    </Border>

    <Grid DockPanel.Dock="Top" ColumnDefinitions="Auto,*,Auto" Margin="12,8">
      <TextBlock Text="Filter" VerticalAlignment="Center" Margin="0,0,8,0" />
      <TextBox Grid.Column="1" x:Name="FilterBox" Text="{Binding Filter}" FontFamily="Menlo, Consolas, monospace"
               Watermark="HLQ.** — for example MVSCE02.**" IsEnabled="{Binding IsIdle}" />
      <Button Grid.Column="2" x:Name="ListButton" Content="List" Margin="8,0,0,0" Command="{Binding ListCommand}" />
    </Grid>

    <Border x:Name="ErrorBanner" DockPanel.Dock="Top" Background="#402020" Padding="12,5" IsVisible="{Binding HasError}">
      <DockPanel>
        <Button x:Name="RetryButton" DockPanel.Dock="Right" Content="Retry" Command="{Binding RetryCommand}"
                IsVisible="{Binding CanRetry}" />
        <TextBlock Text="{Binding ErrorText, StringFormat='✗ {0}'}" TextWrapping="Wrap" VerticalAlignment="Center" />
      </DockPanel>
    </Border>

    <Border x:Name="ConfirmationStrip" DockPanel.Dock="Bottom" Background="#2A3340" Padding="12,6"
            IsVisible="{Binding HasConfirmation}">
      <DockPanel>
        <StackPanel DockPanel.Dock="Right" Orientation="Horizontal" Spacing="8">
          <CheckBox x:Name="ApplyToAllBox" Content="Apply to all" IsChecked="{Binding Confirmation.ApplyToAll}"
                    IsVisible="{Binding Confirmation.OffersApplyToAll, FallbackValue=False}" />
          <Button x:Name="ConfirmCancelButton" Content="Cancel" Command="{Binding Confirmation.CancelCommand}" />
          <Button x:Name="ConfirmSecondaryButton" Content="{Binding Confirmation.SecondaryLabel}"
                  Command="{Binding Confirmation.SecondaryCommand}"
                  IsVisible="{Binding Confirmation.HasSecondary, FallbackValue=False}" />
          <Button x:Name="ConfirmPrimaryButton" Content="{Binding Confirmation.PrimaryLabel}"
                  Command="{Binding Confirmation.PrimaryCommand}" />
        </StackPanel>
        <TextBlock Text="{Binding Confirmation.Message}" TextWrapping="Wrap" VerticalAlignment="Center" />
      </DockPanel>
    </Border>

    <Border DockPanel.Dock="Bottom" Background="#1C1E22" Padding="12,8" BorderBrush="#33373D" BorderThickness="0,1,0,0">
      <StackPanel Spacing="6">
        <WrapPanel ItemSpacing="8" LineSpacing="6">
          <TextBlock Text="Mode" VerticalAlignment="Center" />
          <RadioButton x:Name="TextModeButton" GroupName="Mode" Content="Text" IsChecked="{Binding IsTextMode}" IsEnabled="{Binding IsIdle}" />
          <RadioButton x:Name="BinaryModeButton" GroupName="Mode" Content="Binary" IsChecked="{Binding IsBinaryMode}" IsEnabled="{Binding IsIdle}" />
          <CheckBox x:Name="TrimBox" Content="Trim trailing blanks" IsChecked="{Binding TrimTrailingBlanks}" IsEnabled="{Binding IsTextMode}" />
          <CheckBox x:Name="VerifyBox" Content="Verify after upload" IsChecked="{Binding VerifyUploads}" IsEnabled="{Binding IsTextMode}" />
          <Button x:Name="DownloadButton" Content="Download…" Command="{Binding DownloadCommand}" />
          <Button x:Name="UploadButton" Content="Upload…" Command="{Binding UploadCommand}" />
          <Button x:Name="DeleteButton" Content="Delete…" Command="{Binding DeleteCommand}" />
          <Button x:Name="CancelButton" Content="Cancel" Command="{Binding CancelCommand}" IsVisible="{Binding IsBusy}" />
        </WrapPanel>
        <TextBlock x:Name="PaddingNote" Foreground="#FFC080" IsVisible="{Binding ShowPaddingNote}"
                   Text="⚠ Binary transfers to fixed-length datasets are padded to whole records." />
        <DockPanel>
          <ProgressBar x:Name="Progress" DockPanel.Dock="Right" Width="160" IsIndeterminate="True" IsVisible="{Binding IsBusy}" />
          <TextBlock x:Name="StatusLine" Text="{Binding StatusText}" TextWrapping="Wrap" VerticalAlignment="Center" />
        </DockPanel>
      </StackPanel>
    </Border>

    <Grid ColumnDefinitions="*,4,*" Margin="12,0,12,8">
      <DockPanel Grid.Column="0">
        <Grid DockPanel.Dock="Top" ColumnDefinitions="*,56,56,56" Margin="8,4">
          <TextBlock Classes="header" Text="NAME" />
          <TextBlock Classes="header" Grid.Column="1" Text="DSORG" />
          <TextBlock Classes="header" Grid.Column="2" Text="RECFM" />
          <TextBlock Classes="header" Grid.Column="3" Text="LRECL" />
        </Grid>
        <ListBox x:Name="DatasetList" ItemsSource="{Binding Datasets}" SelectedItem="{Binding SelectedDataset}"
                 SelectionMode="Single" IsEnabled="{Binding CanChooseDataset}">
          <ListBox.ItemTemplate>
            <DataTemplate x:DataType="vm:DatasetRow">
              <Grid ColumnDefinitions="*,56,56,56" Opacity="{Binding IsSupported, Converter={x:Static vm:MvsmfBrowserConverters.DimUnlessTrue}}">
                <TextBlock Classes="cell" Text="{Binding DisplayName}" TextTrimming="CharacterEllipsis" />
                <TextBlock Classes="cell" Grid.Column="1" Text="{Binding Dsorg}" />
                <TextBlock Classes="cell" Grid.Column="2" Text="{Binding Recfm}" />
                <TextBlock Classes="cell" Grid.Column="3" Text="{Binding Lrecl}" />
              </Grid>
            </DataTemplate>
          </ListBox.ItemTemplate>
        </ListBox>
      </DockPanel>

      <GridSplitter Grid.Column="1" ResizeDirection="Columns" Background="#33373D" />

      <Panel Grid.Column="2">
        <TextBlock x:Name="ChooseHint" Text="{Binding ChooseHint}" TextWrapping="Wrap" Margin="12"
                   Foreground="#A0A0A0" IsVisible="{Binding ShowChooseHint}" />
        <TextBlock x:Name="SequentialNote" Margin="12" TextWrapping="Wrap"
                   Text="Sequential dataset: Download and Upload act on the dataset itself."
                   IsVisible="{Binding ShowSequentialNote}" />

        <DockPanel x:Name="MemberPane" IsVisible="{Binding ShowMemberPane}">
          <DockPanel DockPanel.Dock="Top" Margin="8,4">
            <TextBox x:Name="MemberFilterBox" DockPanel.Dock="Right" Width="140" Text="{Binding MemberFilter}"
                     Watermark="Filter members" />
            <TextBlock x:Name="MemberHeader" Text="{Binding MembersHeader}" FontWeight="SemiBold" VerticalAlignment="Center" />
          </DockPanel>
          <Grid DockPanel.Dock="Top" ColumnDefinitions="110,*" Margin="8,4">
            <TextBlock Classes="header" Text="MEMBER" />
            <TextBlock Classes="header" Grid.Column="1" Text="STATUS" />
          </Grid>
          <ListBox x:Name="MemberList" ItemsSource="{Binding VisibleMembers}" SelectionMode="Multiple"
                   IsEnabled="{Binding IsIdle}">
            <ListBox.ItemTemplate>
              <DataTemplate x:DataType="vm:MemberRow">
                <Grid ColumnDefinitions="110,*">
                  <TextBlock Classes="cell" Text="{Binding Name}" />
                  <TextBlock Grid.Column="1" Text="{Binding Status}" TextTrimming="CharacterEllipsis" />
                </Grid>
              </DataTemplate>
            </ListBox.ItemTemplate>
          </ListBox>
        </DockPanel>

        <DockPanel x:Name="ReviewPane" IsVisible="{Binding IsReviewingUpload}">
          <TextBlock DockPanel.Dock="Top" Text="{Binding UploadHeader}" FontWeight="SemiBold" Margin="8,4" />
          <StackPanel DockPanel.Dock="Bottom" Spacing="6" Margin="8">
            <CheckBox x:Name="ExpandTabsBox" Content="Expand tabs (every 8 columns)" IsChecked="{Binding ExpandTabs}"
                      IsEnabled="{Binding IsTextMode}" />
            <TextBlock Text="{Binding ReviewMessage}" Foreground="#FF8080" TextWrapping="Wrap"
                       IsVisible="{Binding ReviewMessage, Converter={x:Static ObjectConverters.IsNotNull}}" />
            <StackPanel Orientation="Horizontal" HorizontalAlignment="Right" Spacing="8">
              <Button x:Name="CloseReviewButton" Content="Close" Command="{Binding CloseReviewCommand}" />
              <Button x:Name="StartUploadButton" Content="Upload" Command="{Binding StartUploadCommand}" />
            </StackPanel>
          </StackPanel>
          <ScrollViewer>
            <ItemsControl x:Name="UploadList" ItemsSource="{Binding Uploads}" Margin="8,0">
              <ItemsControl.ItemTemplate>
                <DataTemplate x:DataType="vm:UploadRow">
                  <Border BorderBrush="#33373D" BorderThickness="0,0,0,1" Padding="0,6">
                    <StackPanel Spacing="4">
                      <DockPanel>
                        <TextBox DockPanel.Dock="Right" Width="110" Text="{Binding MemberName}" MaxLength="8"
                                 FontFamily="Menlo, Consolas, monospace" />
                        <TextBlock Text="{Binding FileName}" VerticalAlignment="Center" TextTrimming="CharacterEllipsis" />
                      </DockPanel>
                      <TextBlock Text="{Binding NameProblem}" Foreground="#FF8080"
                                 IsVisible="{Binding NameProblem, Converter={x:Static ObjectConverters.IsNotNull}}" />
                      <TextBlock Text="{Binding Problems}" TextWrapping="Wrap" FontSize="12"
                                 IsVisible="{Binding Problems, Converter={x:Static StringConverters.IsNotNullOrEmpty}}" />
                      <TextBlock Text="{Binding Status}" FontSize="12"
                                 IsVisible="{Binding Status, Converter={x:Static StringConverters.IsNotNullOrEmpty}}" />
                    </StackPanel>
                  </Border>
                </DataTemplate>
              </ItemsControl.ItemTemplate>
            </ItemsControl>
          </ScrollViewer>
        </DockPanel>
      </Panel>
    </Grid>
  </DockPanel>
</Window>
```

Also create `src/LizTerm.App/ViewModels/MvsmfBrowserConverters.cs` (the dimming converter the dataset template uses):

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Data.Converters;

namespace LizTerm.App.ViewModels;

public static class MvsmfBrowserConverters
{
    /// <summary>A row the preview cannot open is drawn at half strength; its name also says so in words.</summary>
    public static readonly IValueConverter DimUnlessTrue =
        new FuncValueConverter<bool, double>(supported => supported ? 1.0 : 0.5);
}
```

(`WrapPanel.ItemSpacing`/`LineSpacing` exist in Avalonia 11.1+; if the compiler rejects them, drop both and give each child `Margin="0,0,8,6"`.)

Add to `src/LizTerm.App/ViewModels/MvsmfBrowserViewModel.Uploads.cs` the two view properties the XAML binds, and their notifications:

```csharp
    /// <summary>The member list gives way to the upload review.</summary>
    public bool ShowMemberPane => ShowMembers && !IsReviewingUpload;

    /// <summary>The dataset list is fixed while an operation runs or a review is open: the review and the member
    /// list both belong to the chosen dataset.</summary>
    public bool CanChooseDataset => !IsBusy && !IsReviewingUpload;
```

— add `nameof(ShowMemberPane), nameof(CanChooseDataset)` to `_isReviewingUpload`'s `NotifyPropertyChangedFor`; in `MvsmfBrowserViewModel.cs` add `nameof(ShowMemberPane)` to `_selectedDataset`'s list and `nameof(CanChooseDataset)` to `_isBusy`'s list.

`src/LizTerm.App/Views/MvsmfBrowserWindow.axaml.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using LizTerm.App.ViewModels;

namespace LizTerm.App.Views;

/// <summary>The mvsMF Browser (spec §4). Owned by its session window and shown with ShowAbove; it never refuses to
/// close — closing cancels what runs and releases the connection.</summary>
public partial class MvsmfBrowserWindow : Window
{
    public MvsmfBrowserWindow()
    {
        InitializeComponent();
        MemberList.SelectionChanged += (_, _) =>
            ViewModel?.SetSelectedMembers(MemberList.SelectedItems?.OfType<MemberRow>() ?? []);
        AddHandler(KeyDownEvent, OnKeyDownTunnel, RoutingStrategies.Tunnel);
        Opened += (_, _) =>
        {
            FilterBox.Focus();
            if (ViewModel is { Filter.Length: > 0 } vm && vm.Datasets.Count == 0) _ = vm.ListCommand.ExecuteAsync(null);
        };
    }

    private MvsmfBrowserViewModel? ViewModel => DataContext as MvsmfBrowserViewModel;

    protected override void OnClosed(EventArgs e)
    {
        ViewModel?.Dispose();
        base.OnClosed(e);
    }

    private void OnKeyDownTunnel(object? sender, KeyEventArgs e)
    {
        if (ViewModel is not { } vm) return;
        switch (e.Key)
        {
            case Key.Escape:
                e.Handled = true;
                if (vm.Confirmation is { } question) question.CancelCommand.Execute(null);
                else if (vm.IsBusy) vm.CancelCommand.Execute(null);
                else Close();
                break;
            case Key.Enter when FilterBox.IsFocused:
                e.Handled = true;
                if (vm.ListCommand.CanExecute(null)) _ = vm.ListCommand.ExecuteAsync(null);
                break;
            case Key.Enter when MemberList.IsKeyboardFocusWithin:
                e.Handled = true;
                if (vm.DownloadCommand.CanExecute(null)) _ = vm.DownloadCommand.ExecuteAsync(null);
                break;
            case Key.Delete when MemberList.IsKeyboardFocusWithin:
                e.Handled = true;
                if (vm.DeleteCommand.CanExecute(null)) _ = vm.DeleteCommand.ExecuteAsync(null);
                break;
        }
    }
}
```

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~MvsmfBrowserWindowTests"`
Expected: PASS. Headless pitfalls to check if one fails: `window.KeyPress` needs the window to be active/focused (call `window.Activate()` first if keys do not arrive); `ListBox.SelectedItems` must be non-null for `SelectionMode="Multiple"`; `IsEffectivelyEnabled` reads the command's `CanExecute`.

- [ ] **Step 5: Commit**

```bash
git add src/LizTerm.App/Views/MvsmfBrowserWindow.axaml src/LizTerm.App/Views/MvsmfBrowserWindow.axaml.cs src/LizTerm.App/ViewModels/MvsmfBrowserConverters.cs tests/LizTerm.App.Tests/Views/MvsmfBrowserWindowTests.cs
git commit -m "Add the mvsMF Browser window

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 13: Session window and app wiring

**Files:**
- Modify: `src/LizTerm.App/Views/SessionWindow.axaml` (two menu items), `src/LizTerm.App/Views/SessionWindow.axaml.cs`, `src/LizTerm.App/ViewModels/SessionViewModel.cs` (`CreateMvsmfBrowser`), `src/LizTerm.App/App.axaml.cs` (`OpenSession`, `WriteHostFilesPinBack`)
- Test: `tests/LizTerm.App.Tests/Views/SessionWindowMvsmfTests.cs`

**Interfaces:**
- Consumes: `HostFileAccess`, `HostFileConnection` (Task 6), `HostFileServiceFactory.Create` (Task 5), `AvaloniaCredentialPrompt` (Task 3), `ShowAbove` (Task 7), `MvsmfBrowserViewModel` (Tasks 8–11), `MvsmfBrowserWindow` (Task 12), existing `AvaloniaCertificatePrompt`, `AvaloniaFilePicker`, `MenuLookup`, `ProfileStore.Update(SessionProfile fallback, Func<SessionProfile, SessionProfile> change)`.
- Produces: `SessionWindow.AttachHostFiles(HostFileAccess access)` (internal), `SessionWindow.MvsmfBrowser` (internal, `MvsmfBrowserWindow?`), classic `MvsmfBrowserMenuItem`; `SessionViewModel.CreateMvsmfBrowser(HostFileAccess access, HostFileConnection connection, IFilePicker picker) : MvsmfBrowserViewModel`.

Spec §3.3 as revised on 2026-09-16: **File > mvsMF Browser...** (header `mvsMF _Browser...`, no shortcut) appears in both menus only for a profile with a REST URL; it opens one browser per session window, owned by it and shown with `ShowAbove`, and choosing it again fronts that browser. It does not need the 3270 connection. Closing the session window closes the browser and forgets the sign-in. A saved profile can remember a certificate pin; an ad hoc one cannot.

- [ ] **Step 1: Write the failing tests**

`tests/LizTerm.App.Tests/Views/SessionWindowMvsmfTests.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Controls;
using Avalonia.Controls.Platform;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using LizTerm.App.HostFiles;
using LizTerm.App.Menus;
using LizTerm.App.Tests.Fakes;
using LizTerm.App.Tests.ViewModels;
using LizTerm.App.ViewModels;
using LizTerm.App.Views;
using LizTerm.Core.HostFiles;
using LizTerm.Core.Session;
using LizTerm.Core.Settings;

namespace LizTerm.App.Tests.Views;

public class SessionWindowMvsmfTests
{
    private sealed record Shown(SessionWindow Window, SessionViewModel Vm, FakeHostFileService Host, HostFileAccess? Access);

    private static Shown Show(string? url = "http://mvs:8080", MenuStyle style = MenuStyle.InWindow, FakeUriOpener? opener = null)
    {
        var session = new FakeEmulatorSession();
        var vm = new SessionViewModel(session, action => action(), new FakeTextClipboard(), uriOpener: opener);
        var window = new SessionWindow(style, isMacOS: true) { DataContext = vm };
        var host = new FakeHostFileService();
        BrowserTestHost.Standard(host);
        HostFileAccess? access = null;
        if (url is not null)
        {
            // No userid, so the browser does not list on open and no sign-in window appears in these tests.
            access = new HostFileAccess(new SessionProfile { Name = "MVS/CE", Host = "mvs", HostFilesUrl = url }, (_, _, _) => host, savePin: null);
            window.AttachHostFiles(access);
        }
        window.Show();
        return new Shown(window, vm, host, access);
    }

    private static MenuItem Classic(SessionWindow window) => window.FindControl<MenuItem>("MvsmfBrowserMenuItem")!;

    private static NativeMenuItem Native(SessionWindow window) =>
        MenuLookup.Item(NativeMenu.GetMenu(window), "_File", "mvsMF _Browser...")!;

    private static void Click(MenuItem item) => item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

    [AvaloniaFact]
    public void Without_a_rest_url_neither_menu_shows_the_item()
    {
        var shown = Show(url: null);
        Assert.False(Classic(shown.Window).IsVisible);
        Assert.False(Native(Show(url: null, style: MenuStyle.Native).Window).IsVisible);
    }

    [AvaloniaFact]
    public void With_a_rest_url_both_menus_show_it_without_a_shortcut()
    {
        var classic = Show().Window;
        Assert.True(Classic(classic).IsVisible);
        Assert.Null(Classic(classic).InputGesture);

        var native = Native(Show(style: MenuStyle.Native).Window);
        Assert.True(native.IsVisible);
        Assert.True(native.HasClickHandlers);
        Assert.Null(native.Gesture);
    }

    [AvaloniaFact]
    public void Choosing_it_opens_one_browser_over_the_session_window_and_again_fronts_it()
    {
        var shown = Show();

        Click(Classic(shown.Window));

        var browser = Assert.IsType<MvsmfBrowserWindow>(Assert.Single(shown.Window.OwnedWindows));
        var vm = Assert.IsType<MvsmfBrowserViewModel>(browser.DataContext);
        Assert.Equal("mvsMF Browser — MVS/CE (Preview)", vm.Title);
        Assert.Same(browser, shown.Window.MvsmfBrowser);

        Click(Classic(shown.Window));

        Assert.Same(browser, Assert.Single(shown.Window.OwnedWindows));
    }

    [AvaloniaFact]
    public void The_native_item_opens_it_too()
    {
        var shown = Show(style: MenuStyle.Native);

        ((INativeMenuItemExporterEventsImplBridge)Native(shown.Window)).RaiseClicked();

        Assert.IsType<MvsmfBrowserWindow>(Assert.Single(shown.Window.OwnedWindows));
    }

    [AvaloniaFact]
    public void The_browser_follows_the_session_windows_keep_on_top()
    {
        var shown = Show();
        Click(Classic(shown.Window));
        var browser = shown.Window.MvsmfBrowser!;
        Assert.False(browser.Topmost);

        Click(shown.Window.FindControl<MenuItem>("KeepOnTopMenuItem")!);

        Assert.True(shown.Window.Topmost);
        Assert.True(browser.Topmost);
    }

    [AvaloniaFact]
    public void Closing_the_browser_lets_the_menu_open_a_fresh_one()
    {
        var shown = Show();
        Click(Classic(shown.Window));
        var first = shown.Window.MvsmfBrowser!;

        first.Close();
        Assert.Null(shown.Window.MvsmfBrowser);
        Assert.True(shown.Host.Disposed);
        Click(Classic(shown.Window));

        Assert.NotSame(first, shown.Window.MvsmfBrowser);
        Assert.NotNull(shown.Window.MvsmfBrowser);
    }

    [AvaloniaFact]
    public async Task Closing_the_session_window_closes_the_browser_and_forgets_the_sign_in()
    {
        var shown = Show();
        await shown.Access!.Credentials.ProviderFor(new FakeCredentialPrompt())(new HostCredentialRequest(false), CancellationToken.None);
        Click(Classic(shown.Window));
        var browser = shown.Window.MvsmfBrowser!;
        var closed = false;
        browser.Closed += (_, _) => closed = true;

        shown.Window.Close();

        Assert.True(closed);
        Assert.False(shown.Access.Credentials.HasCredentials);
    }

    [AvaloniaFact]
    public void An_unusable_url_is_reported_in_the_banner()
    {
        var shown = Show(url: "ftp://mvs");

        Click(Classic(shown.Window));

        Assert.Empty(shown.Window.OwnedWindows);
        Assert.Equal("The profile's mvsMF URL cannot be used: Enter an http:// or https:// URL.", shown.Vm.ErrorMessage);
    }

    [AvaloniaFact]
    public async Task The_browser_opens_the_user_guide_through_the_session()
    {
        var opener = new FakeUriOpener();
        var shown = Show(opener: opener);
        Click(Classic(shown.Window));
        var vm = (MvsmfBrowserViewModel)shown.Window.MvsmfBrowser!.DataContext!;

        await vm.OpenGuideCommand.ExecuteAsync(null);

        Assert.Single(opener.Opened);
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~SessionWindowMvsmfTests"`
Expected: build FAILS (`AttachHostFiles` not found).

- [ ] **Step 3: Implement**

`SessionWindow.axaml` — in the native File menu, directly after the `IND$FILE _Transfer...` item:

```xml
            <NativeMenuItem Header="mvsMF _Browser..." Click="OnMvsmfBrowserClickNative" IsVisible="False" />
```

and in the classic File menu, directly after `FileTransferMenuItem`:

```xml
        <MenuItem x:Name="MvsmfBrowserMenuItem" Header="mvsMF _Browser..." Click="OnMvsmfBrowserClick" IsVisible="False" />
```

`SessionWindow.axaml.cs`:
- add usings `LizTerm.App.HostFiles`;
- add fields beside the other held native items:

```csharp
    /// <summary>Held, like Minimize, so AttachHostFiles can show it whether or not the native menu is exported.</summary>
    private readonly NativeMenuItem _nativeMvsmfBrowser;
    private HostFileAccess? _hostFiles;
```

- in the constructor, after `_nativeSessionsSeparator = …;`:

```csharp
        _nativeMvsmfBrowser = MenuLookup.Item(NativeMenu.GetMenu(this), "_File", "mvsMF _Browser...")!;
```

- add, near `ShowFileTransferAsync`:

```csharp
    /// <summary>The open mvsMF Browser, if any; one per session window.</summary>
    internal MvsmfBrowserWindow? MvsmfBrowser { get; private set; }

    /// <summary>App calls this for a profile with a REST URL (spec §3.3): the item appears in both menus and the
    /// window holds the session's sign-in until it closes.</summary>
    internal void AttachHostFiles(HostFileAccess access)
    {
        _hostFiles = access;
        _nativeMvsmfBrowser.IsVisible = true;
        MvsmfBrowserMenuItem.IsVisible = true;
    }

    private void OnMvsmfBrowserClick(object? sender, RoutedEventArgs e) => ShowMvsmfBrowser();
    private void OnMvsmfBrowserClickNative(object? sender, EventArgs e) => ShowMvsmfBrowser();

    /// <summary>Fronts the open browser, or opens one owned by this window without blocking it. The 3270
    /// connection is not needed. The prompts and pickers belong to the browser, so they open over it.</summary>
    private void ShowMvsmfBrowser()
    {
        if (MvsmfBrowser is { } open)
        {
            open.Activate();
            return;
        }
        if (_hostFiles is not { } access || ViewModel is not { } vm) return;
        if (access.Url is null)
        {
            vm.ErrorMessage = $"The profile's mvsMF URL cannot be used: {access.UrlError}";
            return;
        }
        HostFileConnection? connection = null;
        try
        {
            var browser = new MvsmfBrowserWindow();
            connection = access.Connect(new AvaloniaCredentialPrompt(browser), new AvaloniaCertificatePrompt(browser));
            browser.DataContext = vm.CreateMvsmfBrowser(access, connection, new AvaloniaFilePicker(browser));
            browser.Closed += (_, _) =>
            {
                if (ReferenceEquals(MvsmfBrowser, browser)) MvsmfBrowser = null;
            };
            MvsmfBrowser = browser;
            browser.ShowAbove(this);
        }
        catch (Exception ex)
        {
            connection?.Dispose();
            MvsmfBrowser = null;
            vm.ErrorMessage = "Could not open the mvsMF Browser: " + ex.Message;
        }
    }
```

- in `OnClosed`, before `base.OnClosed(e);`:

```csharp
        // The browser is owned and would close with the window anyway; closing it here first means its connection
        // is released before the sign-in it uses is forgotten.
        MvsmfBrowser?.Close();
        _hostFiles?.Forget();
```

`SessionViewModel.cs` — add below `CreateTransfer` (add `using LizTerm.App.HostFiles;` if missing):

```csharp
    /// <summary>Builds the mvsMF Browser's view model around this session's dispatcher, with Help's user guide
    /// behind its "What to expect…" link.</summary>
    public MvsmfBrowserViewModel CreateMvsmfBrowser(HostFileAccess access, HostFileConnection connection, IFilePicker picker) =>
        new(access, connection, picker, _dispatch, ShowUserGuideAsync);
```

`App.axaml.cs` — in `OpenSession`, after `window.AttachSessions(_sessions, entry);` (add `using LizTerm.App.HostFiles;`):

```csharp
        if (!string.IsNullOrWhiteSpace(profile.HostFilesUrl))
        {
            // A saved profile can keep a certificate the user trusts; an ad hoc one has nowhere to put it.
            window.AttachHostFiles(new HostFileAccess(profile, HostFileServiceFactory.Create,
                fromStore ? pin => WriteHostFilesPinBack(store, profile, pin) : null));
        }
```

and beside `WritePinBack`:

```csharp
    /// <summary>The REST pin's write-back, merged into the profile as it is on disk now, for the reason
    /// <see cref="WritePinBack"/> gives.</summary>
    private static void WriteHostFilesPinBack(ProfileStore store, SessionProfile profile, CertificatePin pin) =>
        store.Update(profile, current => current with { HostFilesPinnedCertificate = pin });
```

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~SessionWindow|FullyQualifiedName~NativeMenu|FullyQualifiedName~ModalDialogs|FullyQualifiedName~Menu"`
Expected: PASS — including the existing native-menu invariants (every native item has a handler; the two menus carry the same headers) with the new item in both.

- [ ] **Step 5: Commit**

```bash
git add src/LizTerm.App tests/LizTerm.App.Tests/Views/SessionWindowMvsmfTests.cs
git commit -m "Open the mvsMF Browser from the session window's File menu

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 14: The profile editor's mvsMF group

**Files:**
- Modify: `src/LizTerm.App/ViewModels/ProfileEditorViewModel.cs`, `src/LizTerm.App/ViewModels/ProfileEdit.cs`, `src/LizTerm.App/Views/ProfileEditorWindow.axaml`, `src/LizTerm.App/Views/ProfileEditorWindow.axaml.cs`, `src/LizTerm.App/ViewModels/ProfilePickerViewModel.cs:233`, `src/LizTerm.App/App.axaml.cs` (the session window's editor callback)
- Test: `tests/LizTerm.App.Tests/ViewModels/ProfileEditorMvsmfTests.cs`, `tests/LizTerm.App.Tests/Views/ProfileEditorWindowTests.cs`, `tests/LizTerm.App.Tests/ViewModels/ProfileViewModelsTests.cs`

**Interfaces:**
- Consumes: `HostFileTester`, `HostFileServiceFactory.TryNormalizeUrl`, `HostFileServiceFactory.CreateTester` (Task 5), `HostFileMessages.Describe` (Task 5), `AvaloniaCredentialPrompt` (Task 3), `PinMerge.Apply` (Task 1), Core `HostServerInfo`, `HostFileException`.
- Produces: `ProfileEdit(SessionProfile Profile, bool PinCleared, bool HostFilesPinCleared = false)`; `ProfileEditorViewModel(SessionProfile? existing, HostFileTester? tester = null)` with `MvsmfUrl`, `MvsmfUserid`, `MvsmfPinnedCertificate`, `HasMvsmfPin`, `MvsmfPinText`, `MvsmfPinCleared`, `ForgetMvsmfPinCommand`, `MvsmfTestResult`, `IsTestingMvsmf`, `TestMvsmfCommand`; window controls `MvsmfUrlBox`, `MvsmfUseridBox`, `MvsmfPinPanel`, `MvsmfPinText`, `MvsmfForgetButton`, `MvsmfTestButton`, `MvsmfTestResultText`.

Spec §6. URL and userid are optional; a blank URL saves none (and no REST pin). The URL is normalised on save (`/zosmf` added to an empty path). The userid is upper-cased, 1–8 characters, letters/digits/`# $ @`, starting with a letter or `# $ @`. The REST pin belongs to the URL, like the 3270 pin to host and port: editing the URL hides it, restoring it (ignoring case and a trailing slash) brings it back, and Forget clears it. **Test** signs in through a prompt over the editor, asks the host what it is, and forgets the sign-in; it has its own result line, so it never touches the shared validation message.

- [ ] **Step 1: Write the failing tests**

`tests/LizTerm.App.Tests/ViewModels/ProfileEditorMvsmfTests.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.App.ViewModels;
using LizTerm.Core.HostFiles;
using LizTerm.Core.Session;

namespace LizTerm.App.Tests.ViewModels;

public class ProfileEditorMvsmfTests
{
    private static readonly CertificatePin Pin = new("AA:BB", "CN=proxy", "pem");

    private static SessionProfile Rest(string? url = "http://mvs:8080/zosmf", string? userid = "MVSCE02", CertificatePin? pin = null) =>
        new() { Name = "MVS/CE", Host = "mvs", Port = 3270, HostFilesUrl = url, HostFilesUserid = userid, HostFilesPinnedCertificate = pin };

    private sealed class Tester
    {
        public List<(string Name, Uri Url, string? Userid, CertificatePin? Pin)> Calls { get; } = [];
        public Exception? Failure { get; set; }
        public TaskCompletionSource? Gate { get; set; }

        public async Task<HostServerInfo> TestAsync(string name, Uri url, string? userid, CertificatePin? pin, CancellationToken token)
        {
            Calls.Add((name, url, userid, pin));
            if (Gate is { } gate) await gate.Task;
            if (Failure is not null) throw Failure;
            return new HostServerInfo("mvsMF", "1.0.0-dev", "MVS 3.8j");
        }
    }

    [Fact]
    public void An_existing_profile_round_trips_its_rest_fields()
    {
        var original = Rest(pin: Pin);
        var vm = new ProfileEditorViewModel(original);

        Assert.Equal("http://mvs:8080/zosmf", vm.MvsmfUrl);
        Assert.Equal("MVSCE02", vm.MvsmfUserid);
        Assert.True(vm.HasMvsmfPin);
        Assert.Equal("Pinned certificate: SHA-256 AA:BB (CN=proxy)", vm.MvsmfPinText);
        Assert.Equal(original, vm.TryBuild());
    }

    [Fact]
    public void The_url_is_normalised_and_the_userid_upper_cased_on_save()
    {
        var vm = new ProfileEditorViewModel(Rest(url: null, userid: null)) { MvsmfUrl = " http://mvs:8080 ", MvsmfUserid = " ibmuser " };

        var built = vm.TryBuild()!;

        Assert.Equal("http://mvs:8080/zosmf", built.HostFilesUrl);
        Assert.Equal("IBMUSER", built.HostFilesUserid);
    }

    [Fact]
    public void A_blank_url_saves_none_and_no_rest_pin()
    {
        var vm = new ProfileEditorViewModel(Rest(pin: Pin)) { MvsmfUrl = "  ", MvsmfUserid = "" };

        var built = vm.TryBuild()!;

        Assert.Null(built.HostFilesUrl);
        Assert.Null(built.HostFilesUserid);
        Assert.Null(built.HostFilesPinnedCertificate);
    }

    [Theory]
    [InlineData("ftp://mvs", "MVSCE02", "mvsMF URL: Enter an http:// or https:// URL.")]
    [InlineData("http://u:p@mvs", "MVSCE02", "mvsMF URL: Leave the userid and password out of the URL.")]
    [InlineData("http://mvs", "TOOLONGID", "The mvsMF userid must be 1 to 8 letters, digits or # $ @, starting with a letter or # $ @.")]
    [InlineData("http://mvs", "1ABC", "The mvsMF userid must be 1 to 8 letters, digits or # $ @, starting with a letter or # $ @.")]
    [InlineData("http://mvs", "AB-C", "The mvsMF userid must be 1 to 8 letters, digits or # $ @, starting with a letter or # $ @.")]
    public void Bad_rest_fields_are_refused_on_save(string url, string userid, string expected)
    {
        var vm = new ProfileEditorViewModel(Rest()) { MvsmfUrl = url, MvsmfUserid = userid };

        Assert.Null(vm.TryBuild());
        Assert.Equal(expected, vm.ValidationMessage);
    }

    [Fact]
    public void Editing_the_url_hides_the_pin_and_restoring_it_brings_it_back()
    {
        var vm = new ProfileEditorViewModel(Rest(pin: Pin));

        vm.MvsmfUrl = "https://proxy/zosmf";
        Assert.False(vm.HasMvsmfPin);
        Assert.Null(vm.TryBuild()!.HostFilesPinnedCertificate);

        vm.MvsmfUrl = "HTTP://MVS:8080/zosmf/";
        Assert.True(vm.HasMvsmfPin);
    }

    [Fact]
    public void Forget_clears_the_rest_pin_and_says_so()
    {
        var vm = new ProfileEditorViewModel(Rest(pin: Pin));

        vm.ForgetMvsmfPinCommand.Execute(null);

        Assert.False(vm.HasMvsmfPin);
        Assert.True(vm.MvsmfPinCleared);
        Assert.False(vm.PinCleared);
        vm.MvsmfUrl = "http://mvs:8080/zosmf";
        Assert.False(vm.HasMvsmfPin);
    }

    [Fact]
    public async Task Test_reports_what_the_host_is()
    {
        var tester = new Tester();
        var vm = new ProfileEditorViewModel(Rest(url: null, pin: null), tester.TestAsync) { MvsmfUrl = "http://mvs:8080", MvsmfUserid = "mvsce02" };

        await vm.TestMvsmfCommand.ExecuteAsync(null);

        Assert.Equal("✓ Connected: mvsMF 1.0.0-dev on MVS 3.8j", vm.MvsmfTestResult);
        var call = Assert.Single(tester.Calls);
        Assert.Equal(("MVS/CE", "http://mvs:8080/zosmf", "MVSCE02"), (call.Name, call.Url.ToString(), call.Userid));
        Assert.Null(vm.ValidationMessage);
    }

    [Fact]
    public async Task Test_uses_the_rest_pin_the_editor_shows()
    {
        var tester = new Tester();
        var vm = new ProfileEditorViewModel(Rest(pin: Pin), tester.TestAsync);

        await vm.TestMvsmfCommand.ExecuteAsync(null);

        Assert.Equal(Pin, tester.Calls.Single().Pin);
    }

    [Fact]
    public async Task Test_without_a_url_or_with_a_bad_one_says_so_without_asking()
    {
        var tester = new Tester();
        var vm = new ProfileEditorViewModel(Rest(url: null), tester.TestAsync);

        await vm.TestMvsmfCommand.ExecuteAsync(null);
        Assert.Equal("✗ Enter the mvsMF URL first.", vm.MvsmfTestResult);

        vm.MvsmfUrl = "ftp://mvs";
        await vm.TestMvsmfCommand.ExecuteAsync(null);
        Assert.Equal("✗ mvsMF URL: Enter an http:// or https:// URL.", vm.MvsmfTestResult);
        Assert.Empty(tester.Calls);
        Assert.Null(vm.ValidationMessage);
    }

    [Theory]
    [InlineData(HostFileErrorKind.Unauthenticated, "The host rejected the userid or password.", "✗ The host rejected the userid or password.")]
    [InlineData(HostFileErrorKind.Unreachable, "Server information: cannot reach the host (refused).", "✗ Server information: cannot reach the host (refused).")]
    [InlineData(HostFileErrorKind.CertificateRejected, "x", "✗ The host's certificate is not trusted. Open the mvsMF Browser from a session to review it.")]
    public async Task Test_failures_are_reported_in_words(HostFileErrorKind kind, string message, string expected)
    {
        var tester = new Tester { Failure = new HostFileException(kind, message) };
        var vm = new ProfileEditorViewModel(Rest(), tester.TestAsync);

        await vm.TestMvsmfCommand.ExecuteAsync(null);

        Assert.Equal(expected, vm.MvsmfTestResult);
        Assert.False(vm.IsTestingMvsmf);
    }

    [Fact]
    public async Task Test_is_busy_while_it_runs_and_editing_the_url_clears_the_result()
    {
        var tester = new Tester { Gate = new TaskCompletionSource() };
        var vm = new ProfileEditorViewModel(Rest(), tester.TestAsync);

        var testing = vm.TestMvsmfCommand.ExecuteAsync(null);
        Assert.True(vm.IsTestingMvsmf);
        Assert.Equal("⟳ Testing…", vm.MvsmfTestResult);
        Assert.False(vm.TestMvsmfCommand.CanExecute(null));
        tester.Gate.SetResult();
        await testing;
        Assert.True(vm.TestMvsmfCommand.CanExecute(null));

        vm.MvsmfUrl = "http://other";
        Assert.Null(vm.MvsmfTestResult);
    }

    [Fact]
    public void Without_a_tester_there_is_no_test()
    {
        var vm = new ProfileEditorViewModel(Rest());
        Assert.False(vm.TestMvsmfCommand.CanExecute(null));
    }
}
```

Add to `tests/LizTerm.App.Tests/Views/ProfileEditorWindowTests.cs` (inside the class):

```csharp
    [AvaloniaFact]
    public void The_mvsmf_group_shows_the_profiles_values_and_its_pin()
    {
        var window = new ProfileEditorWindow(new SessionProfile
        {
            Name = "MVS/CE", Host = "mvs", HostFilesUrl = "http://mvs:8080/zosmf", HostFilesUserid = "MVSCE02",
            HostFilesPinnedCertificate = new CertificatePin("AA:BB", "CN=proxy", "pem"),
        });
        window.Show();

        Assert.Equal("http://mvs:8080/zosmf", window.FindControl<TextBox>("MvsmfUrlBox")!.Text);
        Assert.Equal("MVSCE02", window.FindControl<TextBox>("MvsmfUseridBox")!.Text);
        Assert.True(window.FindControl<StackPanel>("MvsmfPinPanel")!.IsVisible);
        Assert.Equal("Pinned certificate: SHA-256 AA:BB (CN=proxy)", window.FindControl<TextBlock>("MvsmfPinText")!.Text);
        Assert.True(window.FindControl<Button>("MvsmfTestButton")!.IsEffectivelyEnabled);
        window.FindControl<Button>("MvsmfForgetButton")!.Command!.Execute(null);
        Assert.False(window.FindControl<StackPanel>("MvsmfPinPanel")!.IsVisible);
    }

    [AvaloniaFact]
    public void A_profile_without_mvsmf_shows_an_empty_group()
    {
        var window = new ProfileEditorWindow(new SessionProfile { Name = "p", Host = "h" });
        window.Show();

        Assert.Equal("", window.FindControl<TextBox>("MvsmfUrlBox")!.Text);
        Assert.False(window.FindControl<StackPanel>("MvsmfPinPanel")!.IsVisible);
    }
```

Add to `tests/LizTerm.App.Tests/ViewModels/ProfileViewModelsTests.cs`, beside `Editing_a_stale_profile_does_not_drop_a_pin_written_since`:

```csharp
    [Fact]
    public async Task Editing_a_stale_profile_does_not_drop_a_rest_pin_written_since()
    {
        _store.Save(new SessionProfile { Name = "MVS", Host = "mvs", Port = 3270, HostFilesUrl = "http://mvs:8080/zosmf" });
        var pin = new CertificatePin("CC:DD", "CN=proxy", "pem");

        var picker = new ProfilePickerViewModel(_store, (_, _) => { },
            existing =>
            {
                // Stands in for a browser window remembering a certificate while the editor is open.
                _store.Update(existing!, p => p with { HostFilesPinnedCertificate = pin });
                return Task.FromResult<ProfileEdit?>(new ProfileEdit(existing!, PinCleared: false));
            },
            () => { });

        picker.SelectedRow = picker.VisibleRows.Single();
        await picker.EditCommand.ExecuteAsync(null);

        Assert.Equal(pin, _store.Load("MVS")!.HostFilesPinnedCertificate);
    }

    [Fact]
    public async Task Forget_still_clears_a_rest_pin_the_file_has()
    {
        _store.Save(new SessionProfile { Name = "MVS", Host = "mvs", HostFilesUrl = "http://mvs/zosmf", HostFilesPinnedCertificate = new CertificatePin("CC:DD", "CN=proxy", "pem") });

        var picker = new ProfilePickerViewModel(_store, (_, _) => { },
            existing => Task.FromResult<ProfileEdit?>(new ProfileEdit(existing! with { HostFilesPinnedCertificate = null }, PinCleared: false, HostFilesPinCleared: true)),
            () => { });

        picker.SelectedRow = picker.VisibleRows.Single();
        await picker.EditCommand.ExecuteAsync(null);

        Assert.Null(_store.Load("MVS")!.HostFilesPinnedCertificate);
    }
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~ProfileEditor|FullyQualifiedName~ProfileViewModelsTests"`
Expected: build FAILS (`MvsmfUrl` not found).

- [ ] **Step 3: Implement**

`ProfileEdit.cs`:

```csharp
/// <summary>What the profile editor hands back. The profile alone is not enough: the editor expresses "no pin"
/// three different ways (never had one, the user pressed Forget, the host or port was repointed) and only it
/// knows which, while only the picker can see the file. <see cref="LizTerm.Core.Profiles.PinMerge"/> resolves
/// the two, for the 3270 pin and the REST pin alike.</summary>
public sealed record ProfileEdit(SessionProfile Profile, bool PinCleared, bool HostFilesPinCleared = false);
```

`ProfileEditorViewModel.cs` — add usings `LizTerm.App.HostFiles` and `LizTerm.Core.HostFiles`; then:

1. Fields and properties (below the 3270 pin block):

```csharp
    [ObservableProperty] private string _mvsmfUrl = "";
    [ObservableProperty] private string _mvsmfUserid = "";

    /// <summary>The REST pin, which belongs to the REST URL as the 3270 pin belongs to host and port.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMvsmfPin), nameof(MvsmfPinText))]
    private CertificatePin? _mvsmfPinnedCertificate;
    private CertificatePin? _mvsmfPinnedFor;
    private readonly string? _mvsmfPinnedUrl;

    public bool HasMvsmfPin => MvsmfPinnedCertificate is not null;
    public string? MvsmfPinText =>
        MvsmfPinnedCertificate is { } pin ? $"Pinned certificate: SHA-256 {pin.Sha256} ({pin.Subject})" : null;

    /// <summary>The Test button's own line, kept apart from <see cref="ValidationMessage"/>, which only Save writes.</summary>
    [ObservableProperty] private string? _mvsmfTestResult;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(TestMvsmfCommand))]
    private bool _isTestingMvsmf;

    private readonly HostFileTester? _tester;

    public bool MvsmfPinCleared { get; private set; }
```

2. Constructor: change the signature to `public ProfileEditorViewModel(SessionProfile? existing, HostFileTester? tester = null)`, set `_tester = tester;` as its first statement, and after `_pinnedPort = existing.Port;` add:

```csharp
        _mvsmfUrl = existing.HostFilesUrl ?? "";
        _mvsmfUserid = existing.HostFilesUserid ?? "";
        _mvsmfPinnedCertificate = existing.HostFilesPinnedCertificate;
        _mvsmfPinnedFor = existing.HostFilesPinnedCertificate;
        _mvsmfPinnedUrl = existing.HostFilesUrl;
```

3. Behaviour:

```csharp
    partial void OnMvsmfUrlChanged(string value)
    {
        MvsmfPinnedCertificate = SameRestUrl(value, _mvsmfPinnedUrl) ? _mvsmfPinnedFor : null;
        MvsmfTestResult = null;
    }

    private static bool SameRestUrl(string? first, string? second) =>
        string.Equals(first?.Trim().TrimEnd('/'), second?.Trim().TrimEnd('/'), StringComparison.OrdinalIgnoreCase);

    [RelayCommand]
    private void ForgetMvsmfPin()
    {
        _mvsmfPinnedFor = null;
        MvsmfPinnedCertificate = null;
        MvsmfPinCleared = true;
    }

    private bool CanTestMvsmf => _tester is not null && !IsTestingMvsmf;

    [RelayCommand(CanExecute = nameof(CanTestMvsmf))]
    private async Task TestMvsmfAsync()
    {
        if (!TryReadMvsmf(out var url, out var userid, out var problem))
        {
            MvsmfTestResult = "✗ " + problem;
            return;
        }
        if (url is null)
        {
            MvsmfTestResult = "✗ Enter the mvsMF URL first.";
            return;
        }
        IsTestingMvsmf = true;
        MvsmfTestResult = "⟳ Testing…";
        try
        {
            var name = string.IsNullOrWhiteSpace(Name) ? "This profile" : Name.Trim();
            var info = await _tester!(name, url, userid, MvsmfPinnedCertificate, CancellationToken.None);
            MvsmfTestResult = $"✓ Connected: {info.Product} {info.ProductVersion} on {info.SystemVersion}";
        }
        catch (HostFileException ex) when (ex.Kind == HostFileErrorKind.CertificateRejected)
        {
            MvsmfTestResult = "✗ The host's certificate is not trusted. Open the mvsMF Browser from a session to review it.";
        }
        catch (Exception ex)
        {
            MvsmfTestResult = "✗ " + HostFileMessages.Describe(ex);
        }
        finally
        {
            IsTestingMvsmf = false;
        }
    }

    /// <summary>The REST URL (null when blank) and userid as Save and Test read them.</summary>
    private bool TryReadMvsmf(out Uri? url, out string? userid, out string? problem)
    {
        url = null;
        userid = null;
        problem = null;
        if (!string.IsNullOrWhiteSpace(MvsmfUrl) && !HostFileServiceFactory.TryNormalizeUrl(MvsmfUrl, out url, out var error))
        {
            problem = "mvsMF URL: " + error;
            return false;
        }
        var typed = MvsmfUserid.Trim().ToUpperInvariant();
        if (typed.Length > 0 && (typed.Length > 8 || !IsUseridStart(typed[0]) || typed.Any(c => !IsUseridStart(c) && !char.IsAsciiDigit(c))))
        {
            problem = "The mvsMF userid must be 1 to 8 letters, digits or # $ @, starting with a letter or # $ @.";
            return false;
        }
        userid = typed.Length > 0 ? typed : null;
        return true;
    }

    private static bool IsUseridStart(char c) => char.IsAsciiLetterUpper(c) || c is '#' or '$' or '@';
```

4. In `TryBuild`, immediately before `SetValidation(null);`:

```csharp
        if (!TryReadMvsmf(out var restUrl, out var restUserid, out var restProblem))
        {
            SetValidation(restProblem);
            return null;
        }
```

and add to the object initializer after `Note = …,`:

```csharp
            HostFilesUrl = restUrl?.ToString(),
            HostFilesUserid = restUserid,
            HostFilesPinnedCertificate = restUrl is null ? null : MvsmfPinnedCertificate,
```

`ProfileEditorWindow.axaml` — change `RowDefinitions` to thirteen `Auto`s and add, after the Note row (row 11):

```xml
      <TextBlock Grid.Row="12" Grid.Column="0" Text="mvsMF (Preview)" VerticalAlignment="Top" Margin="0,6,0,0" TextWrapping="Wrap" />
      <StackPanel Grid.Row="12" Grid.Column="1" Spacing="4">
        <TextBox x:Name="MvsmfUrlBox" Text="{Binding MvsmfUrl}" Watermark="http://host:8080 (optional)" />
        <TextBlock Foreground="#A0A0A0" FontSize="12" TextWrapping="Wrap"
                   Text="The z/OSMF REST address for File &gt; mvsMF Browser. /zosmf is added when the path is empty; use https:// through a TLS proxy." />
        <StackPanel Orientation="Horizontal" Spacing="8">
          <TextBlock Text="Userid" VerticalAlignment="Center" />
          <TextBox x:Name="MvsmfUseridBox" Text="{Binding MvsmfUserid}" Width="120" MaxLength="8" Watermark="optional" />
          <TextBlock Text="prefills the sign-in" Foreground="#A0A0A0" FontSize="12" VerticalAlignment="Center" />
        </StackPanel>
        <StackPanel x:Name="MvsmfPinPanel" Orientation="Horizontal" Spacing="8" IsVisible="{Binding HasMvsmfPin}">
          <TextBlock x:Name="MvsmfPinText" Text="{Binding MvsmfPinText}" FontSize="12" TextWrapping="Wrap" MaxWidth="260" />
          <Button x:Name="MvsmfForgetButton" Content="Forget" Command="{Binding ForgetMvsmfPinCommand}" />
        </StackPanel>
        <StackPanel Orientation="Horizontal" Spacing="8">
          <Button x:Name="MvsmfTestButton" Content="Test" Command="{Binding TestMvsmfCommand}" />
          <TextBlock x:Name="MvsmfTestResultText" Text="{Binding MvsmfTestResult}" TextWrapping="Wrap" MaxWidth="260"
                     VerticalAlignment="Center" />
        </StackPanel>
      </StackPanel>
```

`ProfileEditorWindow.axaml.cs` — add `using LizTerm.App.Dialogs;`, build the view model with the tester, and pass the second flag on save:

```csharp
        DataContext = new ProfileEditorViewModel(existing, HostFileServiceFactory.CreateTester(new AvaloniaCredentialPrompt(this)));
```

```csharp
            Close(new ProfileEdit(profile, vm.PinCleared, vm.MvsmfPinCleared));
```

`ProfilePickerViewModel.cs:233` — replace the merge line with:

```csharp
        var merged = PinMerge.Apply(edit.Profile, onDisk, edit.PinCleared, edit.HostFilesPinCleared);
```

`App.axaml.cs` — in the session window's editor callback, replace the `store.Save(edit.Profile with { … });` statement with:

```csharp
                store.Save(PinMerge.Apply(edit.Profile, store.Load(edit.Profile.Name), edit.PinCleared, edit.HostFilesPinCleared));
```

and extend the comment above it: "…dropping a pin they never saw in this editor is not — the REST pin included."

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~ProfileEditor|FullyQualifiedName~ProfileViewModelsTests|FullyQualifiedName~ProfilePicker"`
Expected: PASS (existing editor and picker tests unchanged).

- [ ] **Step 5: Commit**

```bash
git add src/LizTerm.App tests/LizTerm.App.Tests
git commit -m "Add the mvsMF group to the profile editor

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 15: Notes, compatibility log and final verification

**Files:**
- Modify: `src/LizTerm.App/CLAUDE.md`, `tests/CLAUDE.md`, `CLAUDE.md`, `docs/architecture.md`, `docs/mvsmf-compatibility.md`, `src/LizTerm.Backend.Mvsmf/CLAUDE.md`, `src/LizTerm.Core/CLAUDE.md`

**Interfaces:**
- Consumes: everything above.

Docs only; the user guide and changelog are PR 3. Read each file before editing and keep its style and ~118-column wrap.

- [ ] **Step 1: App notes**

In `src/LizTerm.App/CLAUDE.md`, add a section `## mvsMF Browser` (place it after the file-transfer notes):

```markdown
## mvsMF Browser

- `HostFileServiceFactory` is the only place the app names `LizTerm.Backend.Mvsmf` (as `SessionFactory` is for
  b3270); it also builds the profile editor's one-shot `HostFileTester`.
- `App.OpenSession` attaches a `HostFileAccess` to a session window whose profile has a `HostFilesUrl`. The access
  lives as long as the window: the `CredentialHolder` (the only store of the REST password; prompts serialised,
  re-asked only when the refused pair is still current) and the certificate trusted for this session. A saved
  profile's remembered pin is written back through `ProfileStore.Update`, like the 3270 pin.
- **File > mvsMF Browser...** (`mvsMF _Browser...`, no shortcut: the menu rule) is hidden until `AttachHostFiles`
  shows both the classic item and the held native item. It does not need the 3270 connection.
- The browser is the app's first owned window that does not block its owner: `ModalDialogs.ShowAbove` shows it
  owned (it closes with the session window) and makes it follow the owner's Keep on Top. One per session window
  (`SessionWindow.MvsmfBrowser`); the menu item fronts an open one. It is not in the Window menu (spec §3.3).
- Each browser window gets its own `HostFileConnection`, whose prompts open over the browser. An operation refused
  for an untrusted certificate asks once; Connect Anyway trusts the certificate for the session (other browser
  windows of that session pick it up), Remember also stores it.
- `MvsmfBrowserViewModel` runs one operation at a time (`RunExclusiveAsync`); only a download batch runs two
  transfers at once. Results are set after `await` on the UI context; progress goes through `dispatch` and a
  closed `RowProgress` drops late reports. Connection problems (cannot reach, sign-in, certificate) are the red
  banner with Retry; everything else is the status line. Questions are an inline `ConfirmationRequest` strip.
- The window never refuses to close: closing cancels and disposes the view model, which disposes the connection.
- Every status the browser shows starts with a mark and words (`✓ ✗ ⚠ ⟳ –`), per the colour rule.
- The profile editor's mvsMF group has its own result line (`MvsmfTestResult`), never `ValidationMessage`. The REST
  pin follows the URL as the 3270 pin follows host and port; `PinMerge.Apply` resolves both at every editor save.
```

- [ ] **Step 2: Test notes**

In `tests/CLAUDE.md`:
1. In the App tests section, extend the fakes paragraph with: "`FakeCredentialPrompt` (`Answers` queue, `Answer`, `Calls` as `ask:<userid>:<IsRetry>`, `LastRequest`, `Gate`, `AskCount`); `FakeHostFileService` (an in-memory mvsMF: `AddDataset`, `Members`, `Text`, `Binary`, `Failures` keyed like its `Calls` — `list:`, `members:`, `readtext:`, `readbinary:`, `writetext:`, `writebinary:`, `delete:`, `info` — a `Gate`, `MaxConcurrent`, `Disposed`); `FakeFilePicker` also answers several files (`Results`) and a folder (`FolderResult`)."
2. Add: "`BrowserTestHost` (`ViewModels/`) builds an `MvsmfBrowserViewModel` over a `FakeHostFileService` seeded by `Standard` (a PDS of three members, a load library, a sequential dataset and a `DA` dataset); `ChooseAsync` lists and selects, `Select` sets the member selection the window would push."
3. In the rule about naming the backend once, add: "Likewise `HostFileServiceFactoryTests` is the one App test that names `LizTerm.Backend.Mvsmf`."

- [ ] **Step 3: Rules and architecture**

In `CLAUDE.md`, in the dependency rule, after "`LizTerm.App` names the b3270 backend in exactly one place, `src/LizTerm.App/SessionFactory.cs`;" add "and the mvsMF backend in exactly one place, `src/LizTerm.App/HostFileServiceFactory.cs`;".

In `docs/architecture.md`, update the App row (or paragraph) to say the App also opens the mvsMF Browser through `HostFileServiceFactory`, and change the `LizTerm.Backend.Mvsmf` row's "the dataset browser, which arrives in a later PR" wording to present tense ("used by the mvsMF Browser"). Re-wrap the line the PR 1 review found over-long (line 19) to the file's width.

In `src/LizTerm.Core/CLAUDE.md`, in the Profiles section, add: "`HostFilesUrl`, `HostFilesUserid` and `HostFilesPinnedCertificate` are the REST side of a profile (host-neutral names; the App calls it mvsMF). The REST pin is repaired on load like the 3270 pin, and `PinMerge.ResolveHostFiles`/`PinMerge.Apply` merge it keyed on the URL (case and a trailing slash ignored)."

- [ ] **Step 4: Compatibility log and backend notes**

In `docs/mvsmf-compatibility.md`, now that the browser exists, reword the "LizTerm:" lines that described it in the future tense:
- `dataset-list-ignores-start`: "The browser has no Load more row; the whole list is shown."
- `binary-fixed-padding`: "The browser's bottom bar says so while Binary is chosen for a fixed-length dataset."
- `dslevel-is-a-prefix`: "The browser shows everything the filter matches, as z/OSMF would."

In `src/LizTerm.Backend.Mvsmf/CLAUDE.md`:
- change "The App's credential holder (next PR) will be the only store." to "The App's `CredentialHolder` is the only store.";
- in the credentials/URL bullet, say that `MvsmfOptions`' constructor refuses a URL with credentials, a query or a fragment, as `TryNormalizeBaseUrl` does;
- re-wrap the TLS bullet the PR 1 review found at ~202 columns to the file's width.

Also re-wrap the spec's §3.4 line the PR 1 review found over-long, in `docs/superpowers/specs/2026-09-16-lizterm-mvsmf-dataset-browser-design.md`.

- [ ] **Step 5: Full verification**

Run:

```bash
dotnet build LizTerm.slnx --no-incremental 2>&1 | grep -c " warning "
dotnet test LizTerm.slnx
grep -rn "LizTerm.Backend.Mvsmf" src/LizTerm.App --include='*.cs' --include='*.csproj'
grep -rln "LizTerm.Backend.Mvsmf" tests/LizTerm.App.Tests --include='*.cs'
grep -rniE 'mvsmf' src/LizTerm.Core --include='*.cs' && echo "Core code names mvsMF" || true
for tag in $(grep -rhoE 'mvsMF-compat: [a-z0-9-]+' src/LizTerm.Backend.Mvsmf | sed 's/.*: //' | sort -u); do
  grep -q "### \`$tag\`" docs/mvsmf-compatibility.md || echo "no log entry: $tag"
done
```

Expected: `0`; every test passes or skips; the first grep prints only `src/LizTerm.App/HostFileServiceFactory.cs` and the csproj line; the second prints only `tests/LizTerm.App.Tests/HostFileServiceFactoryTests.cs`; the Core grep prints nothing; the tag loop prints nothing.

- [ ] **Step 6: Commit**

```bash
git add CLAUDE.md docs src/LizTerm.App/CLAUDE.md src/LizTerm.Core/CLAUDE.md src/LizTerm.Backend.Mvsmf/CLAUDE.md tests/CLAUDE.md
git commit -m "Document the mvsMF Browser for developers

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

- [ ] **Step 7: Hand back**

Do not push. PR 2 is opened only when Robert asks, against `claude/mvsmf-backend` (or `main` once PR #133 has merged), and before that Robert does a hands-on pass in the app against his host.
