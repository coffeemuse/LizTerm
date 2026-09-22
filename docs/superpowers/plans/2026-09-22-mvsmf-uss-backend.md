# mvsMF USS tab, PR 1 (Core and backend) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give `IHostFileService` a UNIX path kind, a directory listing and a directory create, and make the read, write and delete verbs take a UNIX path, implemented and tested for mvsMF's `/zosmf/restfiles/fs` routes, so PR 2 can build the USS tab on it.

**Architecture:** Core's `HostPath` gains `HostPathKind.Unix` with its own rule, `HostFileEntry` gains `Directory` and `File` kinds carrying `UnixFileAttributes`, `HostFileListing` gains `Truncated`, and `IHostFileService` gains `ListDirectoryAsync` and `CreateDirectoryAsync`. A 64 KB cap (`HostFileLimits.MaxUnixFileBytes`) is enforced locally by `TextUploadCheck.RunForUnixFile` and `HostFileTransfer.BinaryUploadProblem`. `MvsmfFileService` routes a UNIX path to `restfiles/fs/<escaped path>`, lists with `GET restfiles/fs?path=`, creates with a JSON `POST`, deletes with `X-IBM-Option: recursive`, and refuses rename. `MvsmfErrors` learns the USS answers. Both fakes play the new contract. This is PR 1 of #176; PR 2 is the tab.

**Tech Stack:** .NET 10, C# latest, `System.Net.Http`, `System.Text.Json` source generation, xunit.v3, curl, `gh`.

**Spec:** `docs/superpowers/specs/2026-09-22-mvsmf-uss-tab-design.md` (read §2, §3, §4.1, §4.2, §4.3, §5 and §6 before starting; §4.4 to §4.7 and §7 are PR 2).

**Refinements from the spec, decided while planning (the spec's intent holds; the names changed):**
- The attribute record is `UnixFileAttributes`, not `FileAttributes`: `System.IO.FileAttributes` is in every file through `ImplicitUsings`, and the clash would force a qualified name everywhere. The entry's parameter is `Unix`.
- The byte-stream text check is `TextUploadCheck.RunForUnixFile(file, maxBytes, options)` and the file wrapper `HostFileTransfer.CheckUnixTextFile(sourceFile, maxBytes, options)`, rather than overloads of `Run` and `CheckTextFile`: an overload taking `long?` beside one taking `DatasetAttributes` is ambiguous on a `null` argument.
- The binary cap is `HostFileTransfer.BinaryUploadProblem(sourceFile, maxBytes)`, a pure check returning the message or null, rather than an `UploadBinaryAsync` that throws: it is checked before any request, in the same place the text check runs, and the App composes the row's words from it.
- `HostPath.Dataset` becomes `string?` (null on a UNIX path), as the spec says. The ten places that read it today all sit in code that already knows the path is a dataset or member; Task 1 lists each and adds `!`.

## Global Constraints

- Every hand-written `.cs`, `.axaml` and `.sh` file starts with the three licence lines (after the shebang in a script, before the root element in `.axaml`): `This file is part of LizTerm.` / `Copyright 2026 by CoffeeMuse` / `SPDX-License-Identifier: BSD-3-Clause`. A new file needs them; `RepositoryHeadersTests` fails the suite for a missing one.
- **Dependency rule.** `LizTerm.Core` depends on the BCL only and never names mvsMF, Avalonia or b3270, not even in comments ("UNIX path" and "the host" are the words). `LizTerm.Backend.Mvsmf` depends on Core only and is the only project that knows mvsMF exists. `LizTerm.App` names the mvsMF backend only in `src/LizTerm.App/HostFileServiceFactory.cs`; every other App file and every App test talks to `IHostFileService`.
- **Never send `Content-Type: application/json` on a write.** mvsMF treats that `PUT` as a rename or a utility request. After this PR, `RenameAsync` (datasets) and `CreateDirectoryAsync` (a `POST`, not a `PUT`) are the only methods that send it.
- **The stamp is opaque.** LizTerm echoes an `ETag` value back as `If-Match` as the host gave it, and never parses or compares it beyond string equality.
- **Every mvsMF workaround or recorded behaviour carries `// mvsMF-compat: <tag>`** matching an entry in `docs/mvsmf-compatibility.md`, and a test named after the tag pins it. The tags this PR adds: `uss-limits`, `uss-list-no-continuation`, `uss-create-errors-400`, `uss-stat-for-file-path`; `uss-owner-blank` is log only.
- Fixtures are recorded exchanges, binary-exact; only `tools/record-mvsmf-fixture.sh` writes them, and `FixtureTests` rejects a header block holding a credential or an unredacted token.
- Zero warnings: `dotnet build LizTerm.slnx --no-incremental 2>&1 | grep -c " warning "` prints `0`.
- Commits end with `Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>`.
- Run every command from the worktree root `/Users/robert/ClaudeSandbox/LizTerm/.claude/worktrees/uss-mvsmf-access-support-342b1a`.

## Prerequisites

- Work on the branch `claude/uss-mvsmf-access-support-342b1a` (this worktree is on it, at `8b4a036` over `main` at `abbc541`). The spec and this plan are committed to it.
- Tasks 3 and 6 need the live host, mvsMF 1.1.0 at `http://10.42.37.209:8080/zosmf`, credentials in `~/.mvsmf-netrc` (user `IBMUSER`). Set the variables in the recording/live shell without echoing the password:

  ```bash
  export LIZTERM_MVSMF_URL=http://10.42.37.209:8080/zosmf
  export LIZTERM_MVSMF_USER=IBMUSER
  export LIZTERM_MVSMF_PASSWORD=$(awk '{print $6}' ~/.mvsmf-netrc)
  export LIZTERM_MVSMF_SCRATCH_PDS=MVSCE02.CNTL
  export LIZTERM_MVSMF_SCRATCH_DIR=/u/ibmuser
  ```

  If `~/.mvsmf-netrc` is missing, stop and ask.
- The local mvsMF source is at `/Users/robert/mvslovers/mvsmf` (commit `cf4d6d5`, 1.1.1-dev). `docs/endpoints/uss/{list,get,put,create,delete}.md` and `src/ussapi.c` (`ussListHandler` near line 475, `uss_stat_file` near line 360, `ussGetHandler` 618, `ussPutHandler` 862, `ussCreateHandler` 1082, `ussDeleteHandler` 1298) document what Task 4 maps.

## What the host does (source `cf4d6d5`; probed 2026-09-22; recorded in Task 3)

| Request | Response |
|---|---|
| `GET restfiles/fs?path=/u` | 200 `{"items":[{"name":"ibmuser","mode":"drwxr-xr-x","size":128,"user":"IBMUSER","group":"USER","links":2,"mtime":"2026-09-17T19:57:07Z","inode":2}, …],"returnedRows":3,"totalRows":3,"JSONversion":1}`; `user` and `group` are `""` on most entries; no `.` or `..` |
| the same with `X-IBM-Max-Items: 1` | 200 with one item and `"moreRows": true`; without the header the host stops at 1,000; `0` means all; there is no `start=` |
| `GET restfiles/fs?path=/nonexistent` | 404 `{"rc":8,"category":6,"reason":1,"message":"File not found"}` |
| `GET restfiles/fs?path=<a file>` | 200 with one item whose `name` is the full path (a stat) |
| `GET restfiles/fs/<file>` (`X-IBM-Data-Type` text or binary, `X-IBM-Return-Etag`) | 200, the bytes; `ETag` of the stored bytes when asked (the same for text and binary) |
| `GET restfiles/fs/u` (a directory) | 400 `{"rc":8,"category":2,"reason":1,"message":"Is a directory"}` |
| `GET restfiles/fs/<missing file>` | 404 category 6 reason 1 |
| `PUT restfiles/fs/<path>` with `text/plain` or `application/octet-stream` | 204, creating the file; `ETag` of the state written with `X-IBM-Return-Etag: true`; 412 reason 10 on a stale `If-Match`; 500 category 10 "Incomplete write to file" past 64 KB, after writing what fit |
| `POST restfiles/fs/<path>` `{"type":"directory"}` | 201, no body; 400 category 4 reason 1 "File or directory already exists" on an existing name; a missing parent as Task 3 records it |
| `DELETE restfiles/fs/<path>` with `X-IBM-Option: recursive` | 204; 404 category 6 reason 1 when nothing is there |

## File Structure

| File | Responsibility |
|---|---|
| `src/LizTerm.Core/HostFiles/HostPath.cs` | `HostPathKind.Unix`, `ForUnix`, `UnixPathError`, `UnixPath`, `Parent`, `Name`, `Child`, `MaxUnixPathLength` |
| `src/LizTerm.Core/HostFiles/HostFileEntry.cs` | `Directory` and `File` kinds, `UnixFileAttributes` |
| `src/LizTerm.Core/HostFiles/HostListRequest.cs` | `HostFileListing.Truncated` |
| `src/LizTerm.Core/HostFiles/HostFileLimits.cs` | **New.** `MaxUnixFileBytes` |
| `src/LizTerm.Core/HostFiles/IHostFileService.cs` | `ListDirectoryAsync`, `CreateDirectoryAsync`, the widened verbs' doc comments |
| `src/LizTerm.Core/HostFiles/TextUploadCheck.cs` | `RunForUnixFile`, `FileTooLarge` |
| `src/LizTerm.Core/HostFiles/HostFileTransfer.cs` | `CheckUnixTextFile`, `BinaryUploadProblem` |
| `src/LizTerm.Core/CLAUDE.md` | the UNIX path and the cap |
| `src/LizTerm.Backend.Mvsmf/MvsmfJson.cs` | `MvsmfUnixEntry`, `MvsmfUnixList`, `MvsmfUnixCreate` |
| `src/LizTerm.Backend.Mvsmf/MvsmfErrors.cs` | `AlreadyExists` for the USS create, the sentence for it |
| `src/LizTerm.Backend.Mvsmf/MvsmfFileService.cs` | `ListDirectoryAsync`, `CreateDirectoryAsync`, `Route`, `EscapeUnixPath`, recursive delete, rename refused |
| `src/LizTerm.Backend.Mvsmf/CLAUDE.md` | the USS routes |
| `src/LizTerm.App/ViewModels/MvsmfBrowserViewModel.{Manage,Create,Downloads}.cs`, `src/LizTerm.App/HostFiles/EtagMemory.cs` | `!` on `Dataset` where the kind is known; behaviour unchanged |
| `tests/LizTerm.Core.Tests/HostFiles/{HostPathTests,TextUploadCheckTests,HostFileTransferTests,FakeHostFileService}.cs` | Core rules, the cap, the Core fake |
| `tests/LizTerm.Backend.Mvsmf.Tests/Fixtures/uss-*.http` + `README.md` | the eighteen recordings |
| `tests/LizTerm.Backend.Mvsmf.Tests/{MvsmfUnixTests,MvsmfErrorsTests}.cs` | backend tests |
| `tests/LizTerm.App.Tests/Fakes/FakeHostFileService.cs`, `tests/LizTerm.App.Tests/HostFiles/FakeHostFileServiceTests.cs`, `tests/CLAUDE.md` | the App fake |
| `tests/LizTerm.Integration.Tests/LiveMvsmfTests.cs`, `docs/development.md` | the live round trip |
| `docs/mvsmf-compatibility.md` | the five entries, the tested-build table |

## Task graph and the red boundary

Task 1 is additive and keeps the whole solution green. Task 2 adds two members to the interface, so after it **`LizTerm.Backend.Mvsmf` does not compile** until Task 4; both fakes get their real (Core.Tests) or stub (App.Tests) implementations inside Task 2, so Core.Tests and App.Tests stay green throughout. Task 3 records fixtures and touches no code. Task 5 replaces the App fake's stubs. From Task 4 on, every task keeps the whole solution green. Each task's verification step names the project it can build.

---

### Task 1: Core — the UNIX path kind

**Files:**
- Modify: `src/LizTerm.Core/HostFiles/HostPath.cs`
- Modify: `src/LizTerm.App/ViewModels/MvsmfBrowserViewModel.Manage.cs:109,122,123`, `src/LizTerm.App/ViewModels/MvsmfBrowserViewModel.Create.cs:103,105`, `src/LizTerm.App/ViewModels/MvsmfBrowserViewModel.Downloads.cs:34`, `src/LizTerm.App/HostFiles/EtagMemory.cs:43,62,63,68`, `src/LizTerm.Backend.Mvsmf/MvsmfFileService.cs:285-288,360-361`
- Test: `tests/LizTerm.Core.Tests/HostFiles/HostPathTests.cs`

**Interfaces:**
- Produces: `HostPathKind.Unix`; `HostPath.ForUnix(string path)`; `HostPath.UnixPathError(string path) → string?`; `HostPath.MaxUnixPathLength = 251`; `string? HostPath.UnixPath`; `HostPath? HostPath.Parent`; `string HostPath.Name`; `HostPath HostPath.Child(string name)`; `HostPath.Dataset` is now `string?`; `TryParse` reads a leading `/` as a UNIX path.

- [ ] **Step 1: Write the failing tests**

Append to `tests/LizTerm.Core.Tests/HostFiles/HostPathTests.cs`, inside the class:

```csharp
    [Fact]
    public void A_unix_path_keeps_its_case_and_has_no_dataset()
    {
        var path = HostPath.ForUnix("  /u/IBMUSER/Notes.txt ");
        Assert.Equal(HostPathKind.Unix, path.Kind);
        Assert.Equal("/u/IBMUSER/Notes.txt", path.UnixPath);
        Assert.Null(path.Dataset);
        Assert.Null(path.Member);
        Assert.Equal("/u/IBMUSER/Notes.txt", path.ToString());
        Assert.Equal("Notes.txt", path.Name);
        Assert.Equal(path, HostPath.ForUnix("/u/IBMUSER/Notes.txt"));
        Assert.NotEqual(path, HostPath.ForUnix("/u/ibmuser/notes.txt"));
    }

    [Fact]
    public void A_unix_path_walks_up_to_the_root_and_down_to_a_child()
    {
        var notes = HostPath.ForUnix("/u/ibmuser/notes");
        Assert.Equal("/u/ibmuser", notes.Parent!.UnixPath);
        Assert.Equal("/u", notes.Parent.Parent!.UnixPath);
        Assert.Equal("/", notes.Parent.Parent.Parent!.UnixPath);
        Assert.Null(notes.Parent.Parent.Parent.Parent);
        Assert.Equal("/", HostPath.ForUnix("/").Name);
        Assert.Equal("/u/ibmuser/notes/drafts", notes.Child("drafts").UnixPath);
        Assert.Equal("/tmp", HostPath.ForUnix("/").Child("tmp").UnixPath);
        Assert.Equal("drafts", notes.Child("drafts").Name);
        Assert.Null(HostPath.ForDataset("SYS1.PROCLIB").Parent);
        Assert.Equal("SYS1.PROCLIB", HostPath.ForDataset("SYS1.PROCLIB").Name);
        Assert.Equal("JES2", HostPath.ForMember("SYS1.PROCLIB", "JES2").Name);
    }

    [Theory]
    [InlineData("a/b", "A name cannot contain '/'.")]
    [InlineData("..", "A path cannot contain a '.' or '..' segment.")]
    [InlineData("", "Enter a name.")]
    public void A_child_name_is_one_segment(string name, string expected)
    {
        var ex = Assert.Throws<ArgumentException>(() => HostPath.ForUnix("/u").Child(name));
        Assert.Equal(expected, ex.Message);
    }

    [Fact]
    public void A_dataset_has_no_children_and_a_unix_path_no_members()
    {
        Assert.Throws<InvalidOperationException>(() => HostPath.ForDataset("SYS1.PROCLIB").Child("x"));
        Assert.Throws<InvalidOperationException>(() => HostPath.ForUnix("/u").WithMember("X"));
    }

    [Theory]
    [InlineData("/", "/")]
    [InlineData(" /u/ibmuser ", "/u/ibmuser")]
    [InlineData("/u/ibmuser/hello world#1.txt", "/u/ibmuser/hello world#1.txt")]
    public void Parses_a_leading_slash_as_a_unix_path(string text, string expected)
    {
        Assert.True(HostPath.TryParse(text, out var path, out var error), error);
        Assert.Equal(HostPathKind.Unix, path!.Kind);
        Assert.Equal(expected, path.UnixPath);
    }

    [Theory]
    [InlineData("", "Enter a path.")]
    [InlineData("u/ibmuser", "A path must start with '/'.")]
    [InlineData("/u//x", "A path cannot have an empty segment.")]
    [InlineData("/u/ibmuser/", "A path cannot end with '/'.")]
    [InlineData("/u/./x", "A path cannot contain a '.' or '..' segment.")]
    [InlineData("/u/../x", "A path cannot contain a '.' or '..' segment.")]
    [InlineData("/u/a\tb", "A path cannot contain control characters.")]
    public void Refuses_a_bad_unix_path_with_a_reason(string text, string expected)
    {
        Assert.Equal(expected, HostPath.UnixPathError(text));
        var ex = Assert.Throws<ArgumentException>(() => HostPath.ForUnix(text));
        Assert.Equal(expected, ex.Message);
    }

    [Fact]
    public void A_unix_path_is_at_most_251_characters()
    {
        var longest = "/" + new string('a', 250);
        Assert.Null(HostPath.UnixPathError(longest));
        Assert.Equal("A path is at most 251 characters.", HostPath.UnixPathError(longest + "b"));
        Assert.Equal(251, HostPath.MaxUnixPathLength);
    }

    [Fact]
    public void A_bad_unix_path_fails_try_parse_with_the_same_reason()
    {
        Assert.False(HostPath.TryParse("/u/../x", out var path, out var error));
        Assert.Null(path);
        Assert.Equal("A path cannot contain a '.' or '..' segment.", error);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~HostPathTests" 2>&1 | grep -E "error CS|Passed!|Failed!" | head -5`
Expected: build errors naming `Unix`, `ForUnix`, `UnixPath`, `Parent`, `Child`.

- [ ] **Step 3: Implement the UNIX kind**

Replace `src/LizTerm.Core/HostFiles/HostPath.cs` from the enum down to and including `TryParse` with:

```csharp
public enum HostPathKind { Dataset, Member, Unix }

/// <summary>Where a file lives on the host: a dataset, a member of a partitioned one, or a UNIX path. Dataset and
/// member names are trimmed, folded to upper case and checked against the MVS naming rules when the path is made; a
/// UNIX path is trimmed, kept in its case and checked against <see cref="UnixPathError"/>. A path that exists is
/// always one the host could accept.</summary>
public sealed record HostPath
{
    public const int MaxDatasetLength = 44;
    public const int MaxMemberLength = 8;
    /// <summary>One under the length at which the host's path buffer overflows.</summary>
    public const int MaxUnixPathLength = 251;
    private const int MaxQualifierLength = 8;

    private HostPath(HostPathKind kind, string? dataset, string? member, string? unixPath)
    {
        Kind = kind;
        Dataset = dataset;
        Member = member;
        UnixPath = unixPath;
    }

    public HostPathKind Kind { get; }
    /// <summary>The dataset name; null for a UNIX path.</summary>
    public string? Dataset { get; }
    /// <summary>The member name; null for a dataset or a UNIX path.</summary>
    public string? Member { get; }
    /// <summary>The absolute, <c>/</c>-separated path; null for a dataset or member.</summary>
    public string? UnixPath { get; }

    /// <exception cref="ArgumentException">The name breaks a naming rule; the message says which.</exception>
    public static HostPath ForDataset(string dataset) => new(HostPathKind.Dataset, Checked(dataset, DatasetNameError), null, null);

    /// <exception cref="ArgumentException">Either name breaks a naming rule; the message says which.</exception>
    public static HostPath ForMember(string dataset, string member) =>
        new(HostPathKind.Member, Checked(dataset, DatasetNameError), Checked(member, MemberNameError), null);

    /// <exception cref="ArgumentException">The path breaks a rule (<see cref="UnixPathError"/>); the message says which.</exception>
    public static HostPath ForUnix(string path) =>
        UnixPathError(path) is { } error ? throw new ArgumentException(error) : new HostPath(HostPathKind.Unix, null, null, path.Trim());

    /// <exception cref="InvalidOperationException">This is a UNIX path.</exception>
    public HostPath WithMember(string member) => Kind == HostPathKind.Unix
        ? throw new InvalidOperationException("A UNIX path has no members.")
        : ForMember(Dataset!, member);

    /// <summary>The directory above a UNIX path; null at the root, and for a dataset or member.</summary>
    public HostPath? Parent
    {
        get
        {
            if (UnixPath is null || UnixPath == "/") return null;
            var cut = UnixPath.LastIndexOf('/');
            return ForUnix(cut == 0 ? "/" : UnixPath[..cut]);
        }
    }

    /// <summary>The last segment of a UNIX path (<c>/</c> at the root), the member name, or the dataset name.</summary>
    public string Name => Kind switch
    {
        HostPathKind.Unix => UnixPath == "/" ? "/" : UnixPath![(UnixPath.LastIndexOf('/') + 1)..],
        HostPathKind.Member => Member!,
        _ => Dataset!,
    };

    /// <summary>The entry <paramref name="name"/> under this UNIX directory.</summary>
    /// <exception cref="InvalidOperationException">This is a dataset or member.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is not one acceptable segment; the message says why.</exception>
    public HostPath Child(string name)
    {
        if (Kind != HostPathKind.Unix) throw new InvalidOperationException("Only a UNIX path has children.");
        if (name.Length == 0) throw new ArgumentException("Enter a name.");
        if (name.Contains('/')) throw new ArgumentException("A name cannot contain '/'.");
        return ForUnix(UnixPath == "/" ? "/" + name : UnixPath + "/" + name);
    }

    /// <summary>Reads <c>DSN</c>, <c>DSN(MEMBER)</c>, or a path starting with <c>/</c>.</summary>
    public static bool TryParse(string? text, out HostPath? path, out string? error)
    {
        path = null;
        var trimmed = (text ?? "").Trim();
        try
        {
            var open = trimmed.IndexOf('(');
            if (trimmed.StartsWith('/'))
            {
                path = ForUnix(trimmed);
            }
            else if (open < 0)
            {
                path = ForDataset(trimmed);
            }
            else
            {
                if (!trimmed.EndsWith(')')) throw new ArgumentException("A member name must end with ')'.");
                path = ForMember(trimmed[..open], trimmed[(open + 1)..^1]);
            }
            error = null;
            return true;
        }
        catch (ArgumentException ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>Why <paramref name="path"/> is not a UNIX path the host could take, or null when it is one: it must be
    /// absolute, hold no empty, <c>.</c> or <c>..</c> segment, no control character, not end in <c>/</c> (except the
    /// root itself), and be at most <see cref="MaxUnixPathLength"/> characters. Surrounding blanks are ignored; case
    /// is kept.</summary>
    public static string? UnixPathError(string path)
    {
        var trimmed = (path ?? "").Trim();
        if (trimmed.Length == 0) return "Enter a path.";
        if (trimmed[0] != '/') return "A path must start with '/'.";
        if (trimmed.Length > MaxUnixPathLength) return $"A path is at most {MaxUnixPathLength} characters.";
        foreach (var c in trimmed)
        {
            if (char.IsControl(c)) return "A path cannot contain control characters.";
        }
        if (trimmed == "/") return null;
        if (trimmed.EndsWith('/')) return "A path cannot end with '/'.";
        foreach (var segment in trimmed[1..].Split('/'))
        {
            if (segment.Length == 0) return "A path cannot have an empty segment.";
            if (segment is "." or "..") return "A path cannot contain a '.' or '..' segment.";
        }
        return null;
    }
```

Then change `ToString` to:

```csharp
    public override string ToString() => Kind == HostPathKind.Unix ? UnixPath! : Member is null ? Dataset! : $"{Dataset}({Member})";
```

Leave `DatasetNameError`, `MemberNameError`, `DatasetPatternError`, `MemberPatternError`, `MemberPatternMatches`, `Fold`, `IsNameStart` and `Checked` as they are.

- [ ] **Step 4: Run the Core tests**

Run: `dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~HostPathTests" 2>&1 | grep -E "error CS|Passed!|Failed!" | head -5`
Expected: `Passed!` with every HostPath test green (the existing ones included: `""` still reads "Enter a dataset name.", since it has no leading slash).

- [ ] **Step 5: Silence the nullable warnings where the kind is already known**

`Dataset` is now `string?`. In each place below the path was made by `ForDataset`/`ForMember`, or its kind was checked a line earlier, so a `!` is the truthful fix:

- `src/LizTerm.App/ViewModels/MvsmfBrowserViewModel.Manage.cs` lines 109, 122, 123: `to.Dataset` → `to.Dataset!` (three places).
- `src/LizTerm.App/ViewModels/MvsmfBrowserViewModel.Create.cs` lines 103, 105: `path.Dataset` → `path.Dataset!` (two places).
- `src/LizTerm.App/ViewModels/MvsmfBrowserViewModel.Downloads.cs` line 34: `path.Dataset[(path.Dataset.LastIndexOf('.') + 1)..]` → `path.Dataset![(path.Dataset.LastIndexOf('.') + 1)..]`.
- `src/LizTerm.App/HostFiles/EtagMemory.cs` line 43: `dataset.Dataset` → `dataset.Dataset!`; line 62: `from.Dataset` → `from.Dataset!`; line 63: `to.Dataset` → `to.Dataset!`; line 68: `to.Dataset + key[from.Dataset.Length..]` → `to.Dataset + key[from.Dataset!.Length..]`.
- `src/LizTerm.Backend.Mvsmf/MvsmfFileService.cs` `RenameAsync` (line 285): `HostPath.ForMember(from.Dataset, newName)` → `HostPath.ForMember(from.Dataset!, newName)`; line 288: `new MvsmfRenameSource(from.Dataset, from.Member)` → `new MvsmfRenameSource(from.Dataset!, from.Member)`; `DatasetPath` (lines 360-361): `EscapeName(path.Dataset)` → `EscapeName(path.Dataset!)` in both arms.

Then build and count warnings:

Run: `dotnet build LizTerm.slnx --no-incremental 2>&1 | grep " warning " | head; dotnet build LizTerm.slnx --no-incremental 2>&1 | grep -c " warning "`
Expected: no warning lines, then `0`. If a `CS8604`/`CS8602` remains, it names the line; add the `!` there (it will be another read of `Dataset` in dataset-only code).

- [ ] **Step 6: Run the whole suite**

Run: `dotnet test LizTerm.slnx 2>&1 | grep -E "Passed!|Failed!|error" | head -10`
Expected: every project `Passed!` (the live tests skip themselves).

- [ ] **Step 7: Commit**

```bash
git add src/LizTerm.Core/HostFiles/HostPath.cs tests/LizTerm.Core.Tests/HostFiles/HostPathTests.cs src/LizTerm.App src/LizTerm.Backend.Mvsmf/MvsmfFileService.cs
git commit -m "HostPath: a UNIX path kind (#176)

ForUnix, UnixPathError (absolute, no empty or dot segments, no control
characters, at most 251 characters), Parent, Name and Child; TryParse
reads a leading slash. Dataset is null on a UNIX path, so the reads of
it in dataset-only code say so with a bang.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 2: Core — entries, the listing flag, the contract, the cap and the checks

**Files:**
- Modify: `src/LizTerm.Core/HostFiles/HostFileEntry.cs`, `src/LizTerm.Core/HostFiles/HostListRequest.cs`, `src/LizTerm.Core/HostFiles/IHostFileService.cs`, `src/LizTerm.Core/HostFiles/TextUploadCheck.cs`, `src/LizTerm.Core/HostFiles/HostFileTransfer.cs`, `src/LizTerm.Core/CLAUDE.md`
- Create: `src/LizTerm.Core/HostFiles/HostFileLimits.cs`
- Modify: `tests/LizTerm.Core.Tests/HostFiles/FakeHostFileService.cs`, `tests/LizTerm.App.Tests/Fakes/FakeHostFileService.cs` (stubs only; Task 5 fills them)
- Test: `tests/LizTerm.Core.Tests/HostFiles/TextUploadCheckTests.cs`, `tests/LizTerm.Core.Tests/HostFiles/HostFileTransferTests.cs`

**Interfaces:**
- Consumes: Task 1's `HostPathKind.Unix`.
- Produces: `HostFileEntryKind.Directory`, `HostFileEntryKind.File`; `UnixFileAttributes(long Size, DateTimeOffset? Modified)`; `HostFileEntry(string Name, HostFileEntryKind Kind, DatasetAttributes? Attributes = null, UnixFileAttributes? Unix = null)`; `HostFileListing(IReadOnlyList<HostFileEntry> Entries, string? Continuation, bool Truncated = false)` with `IsComplete => Continuation is null && !Truncated`; `HostFileLimits.MaxUnixFileBytes = 65_536L`; `IHostFileService.ListDirectoryAsync(HostPath directory, HostListRequest request, CancellationToken)` and `CreateDirectoryAsync(HostPath directory, CancellationToken)`; `TextUploadProblemKind.FileTooLarge`; `TextUploadCheck.RunForUnixFile(ReadOnlySpan<byte> file, long? maxBytes = null, TextUploadOptions? options = null)`; `HostFileTransfer.CheckUnixTextFile(string sourceFile, long? maxBytes = null, TextUploadOptions? options = null)`; `HostFileTransfer.BinaryUploadProblem(string sourceFile, long maxBytes) → string?`.

- [ ] **Step 1: Write the failing tests**

Append to `tests/LizTerm.Core.Tests/HostFiles/TextUploadCheckTests.cs`, inside the class:

```csharp
    [Fact]
    public void A_unix_file_has_no_record_length_and_keeps_its_tabs()
    {
        var result = TextUploadCheck.RunForUnixFile(Encoding.UTF8.GetBytes("A\tB\n" + new string('x', 500) + "\n"), maxBytes: null, new TextUploadOptions(ExpandTabs: false));
        Assert.True(result.CanUpload);
        Assert.Equal("A\tB", result.Lines[0]);
        Assert.Equal(500, result.Lines[1].Length);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void A_unix_file_still_refuses_characters_outside_latin1()
    {
        var result = TextUploadCheck.RunForUnixFile(Encoding.UTF8.GetBytes("café\n€\n"));
        Assert.False(result.CanUpload);
        var problem = Assert.Single(result.Errors);
        Assert.Equal(TextUploadProblemKind.UnsupportedCharacter, problem.Kind);
        Assert.Equal(2, problem.Line);
    }

    [Fact]
    public void A_unix_file_over_the_cap_is_too_large_counting_latin1_bytes_and_line_feeds()
    {
        var ok = TextUploadCheck.RunForUnixFile(Encoding.UTF8.GetBytes("ab\ncd\n"), maxBytes: 6);
        Assert.True(ok.CanUpload);

        var over = TextUploadCheck.RunForUnixFile(Encoding.UTF8.GetBytes("ab\ncd\n"), maxBytes: 5);
        Assert.False(over.CanUpload);
        var problem = Assert.Single(over.Errors);
        Assert.Equal(TextUploadProblemKind.FileTooLarge, problem.Kind);
        Assert.Equal(0, problem.Line);
        Assert.Equal("The file is 6 bytes; the host holds at most 5.", problem.Message);
    }

    [Fact]
    public void The_cap_counts_expanded_tabs_and_formats_thousands()
    {
        var result = TextUploadCheck.RunForUnixFile(Encoding.UTF8.GetBytes("\tx\n"), maxBytes: 8, new TextUploadOptions(ExpandTabs: true));
        Assert.False(result.CanUpload);
        Assert.Equal("The file is 10 bytes; the host holds at most 8.", Assert.Single(result.Errors).Message);

        var big = TextUploadCheck.RunForUnixFile(Encoding.UTF8.GetBytes(new string('x', 70_000) + "\n"), maxBytes: HostFileLimits.MaxUnixFileBytes);
        Assert.Equal("The file is 70,001 bytes; the host holds at most 65,536.", Assert.Single(big.Errors).Message);
    }

    [Fact]
    public void A_dataset_check_still_warns_about_tabs_it_did_not_expand()
    {
        var result = Run("A\tB\n", options: new TextUploadOptions(ExpandTabs: false));
        Assert.Equal(TextUploadProblemKind.TabsPresent, Assert.Single(result.Warnings).Kind);
    }
```

Append to `tests/LizTerm.Core.Tests/HostFiles/HostFileTransferTests.cs`, inside the class:

```csharp
    [Fact]
    public void A_binary_upload_over_the_cap_is_named_before_any_request()
    {
        var file = Path.Combine(Path.GetTempPath(), $"liz-{Guid.NewGuid():N}.bin");
        try
        {
            File.WriteAllBytes(file, new byte[10]);
            Assert.Null(HostFileTransfer.BinaryUploadProblem(file, 10));
            Assert.Equal("The file is 10 bytes; the host holds at most 9.", HostFileTransfer.BinaryUploadProblem(file, 9));
            File.WriteAllBytes(file, new byte[HostFileLimits.MaxUnixFileBytes + 1]);
            Assert.Equal("The file is 65,537 bytes; the host holds at most 65,536.", HostFileTransfer.BinaryUploadProblem(file, HostFileLimits.MaxUnixFileBytes));
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void A_unix_text_file_is_checked_without_a_record_length()
    {
        var file = Path.Combine(Path.GetTempPath(), $"liz-{Guid.NewGuid():N}.txt");
        try
        {
            File.WriteAllText(file, new string('x', 200) + "\n");
            Assert.True(HostFileTransfer.CheckUnixTextFile(file).CanUpload);
            Assert.False(HostFileTransfer.CheckUnixTextFile(file, maxBytes: 100).CanUpload);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void A_listing_cut_short_without_a_continuation_is_not_complete()
    {
        Assert.True(new HostFileListing([], null).IsComplete);
        Assert.False(new HostFileListing([], null, Truncated: true).IsComplete);
        Assert.False(new HostFileListing([], "X").IsComplete);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~TextUploadCheckTests|FullyQualifiedName~HostFileTransferTests" 2>&1 | grep -E "error CS|Passed!|Failed!" | head -5`
Expected: build errors naming `RunForUnixFile`, `FileTooLarge`, `HostFileLimits`, `BinaryUploadProblem`, `CheckUnixTextFile`, `Truncated`.

- [ ] **Step 3: The entry kinds and the listing flag**

In `src/LizTerm.Core/HostFiles/HostFileEntry.cs` replace the enum, the entry record and its comment with:

```csharp
public enum HostFileEntryKind { Dataset, Member, Directory, File }

public enum RecordFormatFamily { Unknown, Fixed, Variable, Undefined }

/// <summary>What a directory listing says about an entry: its size in bytes (a directory's is whatever the host
/// reports) and when it last changed, null when the host does not say.</summary>
public sealed record UnixFileAttributes(long Size, DateTimeOffset? Modified);

/// <summary>One row of a host listing. <paramref name="Attributes"/> is set for datasets only; <paramref name="Unix"/>
/// for directories and files only.</summary>
public sealed record HostFileEntry(string Name, HostFileEntryKind Kind, DatasetAttributes? Attributes = null, UnixFileAttributes? Unix = null);
```

In `src/LizTerm.Core/HostFiles/HostListRequest.cs` replace `HostFileListing` with:

```csharp
/// <summary>One page of a listing. A null <paramref name="Continuation"/> means there is nothing to continue with;
/// otherwise passing it back in the next <see cref="HostListRequest"/> fetches the entries after these.
/// <paramref name="Truncated"/> says the host cut the list short and offers no way to continue it (a directory
/// listing); it is never set on a dataset or member listing.</summary>
public sealed record HostFileListing(IReadOnlyList<HostFileEntry> Entries, string? Continuation, bool Truncated = false)
{
    public bool IsComplete => Continuation is null && !Truncated;
}
```

- [ ] **Step 4: The cap**

Create `src/LizTerm.Core/HostFiles/HostFileLimits.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

namespace LizTerm.Core.HostFiles;

/// <summary>Sizes the host cannot take, checked here before a request is made, since the host writes what fits and
/// only then fails.</summary>
public static class HostFileLimits
{
    /// <summary>The most a UNIX file can hold on the tested host, whose file system keeps a file in direct blocks
    /// only. The backend's compatibility log records where this comes from; raise it when the host grows.</summary>
    public const long MaxUnixFileBytes = 65_536;
}
```

- [ ] **Step 5: The contract**

In `src/LizTerm.Core/HostFiles/IHostFileService.cs`:

Replace the interface's summary with:

```csharp
/// <summary>File access to one host outside the 3270 session. Every call may run concurrently with the others.
/// Host outcomes other than success throw <see cref="HostFileException"/>; a cancelled token throws
/// <see cref="OperationCanceledException"/>. Text crosses this interface as .NET strings, one per record or line, so
/// the wire encoding is the implementation's business. A <see cref="HostPath"/> of the wrong kind for a verb
/// (a UNIX path to a dataset verb, a dataset to a directory verb) is an <see cref="ArgumentException"/> before any
/// request.</summary>
```

After `ListMembersAsync`, add:

```csharp
    /// <summary>The entries of one directory, directories and files together, in the host's order, each a
    /// <see cref="HostFileEntryKind.Directory"/> or <see cref="HostFileEntryKind.File"/> with
    /// <see cref="HostFileEntry.Unix"/> set. A missing path is <see cref="HostFileErrorKind.NotFound"/>; a path that
    /// is a file is <see cref="HostFileErrorKind.InvalidRequest"/>. <paramref name="request"/>'s <c>MaxItems</c>
    /// caps the answer; a host that cut the list short says so with <see cref="HostFileListing.Truncated"/> and
    /// hands back no continuation, and a request carrying one is an <see cref="ArgumentException"/>.</summary>
    Task<HostFileListing> ListDirectoryAsync(HostPath directory, HostListRequest request, CancellationToken cancellationToken = default);
```

Change `ReadTextAsync`'s summary to start `/// <summary>The records or lines as strings, trailing blanks as the host sent them.`, and `WriteTextAsync`'s to start `/// <summary>Replaces the dataset's, member's or file's records, creating a member or file that does not exist.`. Add to `WriteTextAsync`'s summary, before `Returns the stamp`: `A UNIX file's parent directory must exist (<see cref="HostFileErrorKind.NotFound"/> otherwise).`

After `CreateDatasetAsync`, add:

```csharp
    /// <summary>Creates one directory under an existing parent. An existing name is
    /// <see cref="HostFileErrorKind.AlreadyExists"/>; a missing parent is <see cref="HostFileErrorKind.NotFound"/>.</summary>
    Task CreateDirectoryAsync(HostPath directory, CancellationToken cancellationToken = default);
```

Change `RenameAsync`'s `<exception>` line to `/// <exception cref="ArgumentException"><paramref name="newName"/> fails the naming rules, or <paramref name="from"/> is a UNIX path: no host this release supports can rename one.</exception>`, and `DeleteAsync`'s summary to `/// <summary>Deletes a member, a whole dataset with everything in it, a file, or a directory with everything under it.</summary>`.

- [ ] **Step 6: The text check**

In `src/LizTerm.Core/HostFiles/TextUploadCheck.cs`:

Change the enum to `public enum TextUploadProblemKind { InvalidEncoding, UnsupportedCharacter, LineTooLong, TabsPresent, FileTooLarge }`.

Replace the `Run` method's signature and its first lines (down to `var limit = target.UsableLineLength;`) with:

```csharp
    /// <exception cref="ArgumentOutOfRangeException"><see cref="TextUploadOptions.TabWidth"/> is less than 1.</exception>
    public static TextUploadResult Run(ReadOnlySpan<byte> file, DatasetAttributes target, TextUploadOptions? options = null) =>
        Run(file, target.UsableLineLength, null, warnTabs: true, options);

    /// <summary>For a byte-stream target: no record length, tabs kept without a warning when not expanded (the host
    /// stores them), and with <paramref name="maxBytes"/> the bytes the lines will take on the host, one per
    /// character plus one per line ending, counted against it as <see cref="TextUploadProblemKind.FileTooLarge"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><see cref="TextUploadOptions.TabWidth"/> is less than 1.</exception>
    public static TextUploadResult RunForUnixFile(ReadOnlySpan<byte> file, long? maxBytes = null, TextUploadOptions? options = null) =>
        Run(file, null, maxBytes, warnTabs: false, options);

    private static TextUploadResult Run(ReadOnlySpan<byte> file, int? lineLimit, long? maxBytes, bool warnTabs, TextUploadOptions? options)
    {
        options ??= new TextUploadOptions();
        ArgumentOutOfRangeException.ThrowIfLessThan(options.TabWidth, 1, nameof(options));
        if (file.StartsWith(Utf8Bom)) file = file[Utf8Bom.Length..];

        string text;
        try
        {
            text = StrictUtf8.GetString(file);
        }
        catch (DecoderFallbackException)
        {
            return new TextUploadResult(
                [],
                [new TextUploadProblem(TextUploadProblemKind.InvalidEncoding, 0, "The file is not UTF-8 text. Choose Binary to send its bytes unchanged.")],
                []);
        }

        var lines = SplitLines(text);
        var tooLong = new Reporter(TextUploadProblemKind.LineTooLong);
        var unsupported = new Reporter(TextUploadProblemKind.UnsupportedCharacter);
        var tabLines = new List<int>();
        var limit = lineLimit;
```

Then replace the method's tail, from `var warnings = new List<TextUploadProblem>();` to the closing `}` of the method, with:

```csharp
        var warnings = new List<TextUploadProblem>();
        if (warnTabs && tabLines.Count > 0)
        {
            warnings.Add(new TextUploadProblem(TextUploadProblemKind.TabsPresent, tabLines[0],
                tabLines.Count == 1 ? "1 line contains tab characters." : $"{tabLines.Count} lines contain tab characters."));
        }
        var errors = new List<TextUploadProblem>([.. tooLong.All(), .. unsupported.All()]);
        if (maxBytes is long cap)
        {
            long bytes = 0;
            foreach (var line in lines) bytes += line.Length + 1;
            if (bytes > cap) errors.Add(new TextUploadProblem(TextUploadProblemKind.FileTooLarge, 0, TooLarge(bytes, cap)));
        }
        return new TextUploadResult(lines, errors, warnings);
    }

    /// <summary>The one sentence for a file past the host's size, shared with the binary check.</summary>
    internal static string TooLarge(long bytes, long cap) =>
        $"The file is {bytes.ToString("N0", CultureInfo.InvariantCulture)} bytes; the host holds at most {cap.ToString("N0", CultureInfo.InvariantCulture)}.";
```

Add `using System.Globalization;` at the top if it is not there. The loop body between those two edits (tabs, runes, line length) stays exactly as it is.

- [ ] **Step 7: The transfer helpers**

In `src/LizTerm.Core/HostFiles/HostFileTransfer.cs`, after `CheckTextFile`, add:

```csharp
    /// <summary>Reads <paramref name="sourceFile"/> and runs <see cref="TextUploadCheck.RunForUnixFile"/> on it.</summary>
    /// <exception cref="IOException">The local file could not be read; not a <see cref="HostFileException"/>.</exception>
    /// <exception cref="UnauthorizedAccessException">The local file is not readable.</exception>
    public static TextUploadResult CheckUnixTextFile(string sourceFile, long? maxBytes = null, TextUploadOptions? options = null) =>
        TextUploadCheck.RunForUnixFile(File.ReadAllBytes(sourceFile), maxBytes, options);

    /// <summary>Why <paramref name="sourceFile"/> cannot be sent as bytes to a host holding at most
    /// <paramref name="maxBytes"/>, or null when it can: checked before any request, since the host would write what
    /// fits and only then fail.</summary>
    /// <exception cref="IOException">The local file could not be examined; not a <see cref="HostFileException"/>.</exception>
    public static string? BinaryUploadProblem(string sourceFile, long maxBytes)
    {
        var length = new FileInfo(sourceFile).Length;
        return length > maxBytes ? TextUploadCheck.TooLarge(length, maxBytes) : null;
    }
```

- [ ] **Step 8: The two fakes compile**

In `tests/LizTerm.Core.Tests/HostFiles/FakeHostFileService.cs`, after `ListMembersAsync`, add:

```csharp
    public Task<HostFileListing> ListDirectoryAsync(HostPath directory, HostListRequest request, CancellationToken cancellationToken = default)
    {
        Calls.Add($"listdir:{directory}");
        return Task.FromResult(new HostFileListing([], null));
    }
```

and after `CreateDatasetAsync`:

```csharp
    public Task CreateDirectoryAsync(HostPath directory, CancellationToken cancellationToken = default)
    {
        Calls.Add($"mkdir:{directory}");
        return Task.CompletedTask;
    }
```

In `tests/LizTerm.App.Tests/Fakes/FakeHostFileService.cs`, after `ListMembersAsync`, add the stubs Task 5 replaces:

```csharp
    public Task<HostFileListing> ListDirectoryAsync(HostPath directory, HostListRequest request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Task 5 of the USS backend plan fills this in.");

    public Task CreateDirectoryAsync(HostPath directory, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Task 5 of the USS backend plan fills this in.");
```

- [ ] **Step 9: Run the Core and App tests**

Run: `dotnet test tests/LizTerm.Core.Tests 2>&1 | grep -E "error CS|Passed!|Failed!" | head -5; dotnet test tests/LizTerm.App.Tests 2>&1 | grep -E "error CS|Passed!|Failed!" | head -5`
Expected: both `Passed!`. (`dotnet test LizTerm.slnx` would fail to build `LizTerm.Backend.Mvsmf`, which does not yet implement the two verbs: the red boundary, closed in Task 4.) The App test project references the backend only through `HostFileServiceFactory`, so if App.Tests fails to build because of the backend, run `dotnet build src/LizTerm.Backend.Mvsmf 2>&1 | grep -c "error CS"` to confirm the errors are exactly the two missing interface members, note it, and move on: Task 4 closes it.

- [ ] **Step 10: Core notes**

In `src/LizTerm.Core/CLAUDE.md`, under "Host files", change the `HostPath` bullet to:

```markdown
- `HostPath` folds dataset and member names to upper case and checks the MVS rules when it is made, and keeps a UNIX
  path (`HostPathKind.Unix`, `ForUnix`) in its case under `UnixPathError` (absolute, no empty or `.`/`..` segment,
  no control character, at most `MaxUnixPathLength` characters), so a `HostPath` is always a name the host could
  accept. `Dataset` and `Member` are null on a UNIX path, `UnixPath` on the others; `Parent`, `Name` and `Child`
  walk a UNIX path. `DatasetPatternError` is the filter rule.
```

and add after the `TextUploadCheck` bullet:

```markdown
- **The 64 KB cap.** `HostFileLimits.MaxUnixFileBytes` is the most a UNIX file can hold on the tested host, and it
  is enforced here, never left to the host, which writes what fits and then fails: `TextUploadCheck.RunForUnixFile`
  counts the lines' bytes (one per character, one per line ending) as `FileTooLarge`, and
  `HostFileTransfer.BinaryUploadProblem` compares a file's length. A UNIX target has no record length, and its tabs
  are neither warned about nor expanded unless asked.
- `IHostFileService.ListDirectoryAsync` lists one directory (`Directory` and `File` entries carrying
  `UnixFileAttributes`); a host that cuts it short sets `HostFileListing.Truncated`, which `IsComplete` reads, and
  hands back no continuation. `CreateDirectoryAsync` makes one directory under an existing parent. The read, write
  and delete verbs take a UNIX path unchanged; a dataset verb given one, or `RenameAsync` given one, throws
  `ArgumentException` before any request.
```

- [ ] **Step 11: Commit**

```bash
git add src/LizTerm.Core tests/LizTerm.Core.Tests/HostFiles tests/LizTerm.App.Tests/Fakes/FakeHostFileService.cs
git commit -m "Core: directory entries, ListDirectoryAsync, CreateDirectoryAsync and the 64 KB cap (#176)

UnixFileAttributes on Directory and File entries, Truncated on a listing
the host cut short, two verbs on the contract, HostFileLimits with the
cap, RunForUnixFile and BinaryUploadProblem to enforce it before a
request. The Core fake plays the verbs; the App fake stubs them.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 3: The fixtures

Eighteen exchanges recorded from the host under `/u/ibmuser/liztest-fix`, a directory this task creates and removes. Every path appears only in the request line, so nothing host-specific lands in a fixture body except the stat's full path, which is the point of that fixture.

**Files:**
- Create: `tests/LizTerm.Backend.Mvsmf.Tests/Fixtures/uss-{list-root,list-empty,list-missing,list-truncated,stat-file,read-text,read-binary,read-missing,read-directory,write-204,write-etag-204,write-412,write-too-large,mkdir-201,mkdir-exists,mkdir-no-parent,delete-204,delete-missing}.http`
- Modify: `tests/LizTerm.Backend.Mvsmf.Tests/Fixtures/README.md`

**Interfaces:**
- Produces: the fixtures Task 4 loads by name.

- [ ] **Step 1: Confirm nothing is left from an earlier run**

With the environment set (Prerequisites):

```bash
curl -sS --netrc-file ~/.mvsmf-netrc -w '\nHTTP %{http_code}\n' "$LIZTERM_MVSMF_URL/restfiles/fs?path=/u/ibmuser"
```

Expected: `{"items":[],…}` and `HTTP 200`. If `liztest-fix` is listed, remove it first: `curl -sS --netrc-file ~/.mvsmf-netrc -X DELETE -H 'X-IBM-Option: recursive' "$LIZTERM_MVSMF_URL/restfiles/fs/u/ibmuser/liztest-fix"`.

- [ ] **Step 2: Record the listings that need no scratch directory**

```bash
R=tools/record-mvsmf-fixture.sh
$R uss-list-root GET 'restfiles/fs?path=/'
$R uss-list-empty GET 'restfiles/fs?path=/u/ibmuser'
$R uss-list-missing GET 'restfiles/fs?path=/u/nobody'
$R uss-list-truncated GET 'restfiles/fs?path=/u' -H 'X-IBM-Max-Items: 1'
$R uss-read-directory GET 'restfiles/fs/u' -H 'X-IBM-Data-Type: text'
$R uss-mkdir-no-parent POST 'restfiles/fs/u/nobody/child' -H 'Content-Type: application/json' --data-binary '{"type":"directory"}'
```

Expected first lines: `uss-list-root: HTTP/1.1 200 OK`, `uss-list-empty: 200`, `uss-list-missing: HTTP/1.1 404 Not Found`, `uss-list-truncated: 200`, `uss-read-directory: HTTP/1.1 400 Bad Request`, and `uss-mkdir-no-parent` whatever the host answers (the docs say 400, `uss_open_rc`'s table may say 404): **write its status and body down**, Task 4 maps it.

- [ ] **Step 3: Record the create, the writes and the reads**

```bash
$R uss-mkdir-201 POST 'restfiles/fs/u/ibmuser/liztest-fix' -H 'Content-Type: application/json' --data-binary '{"type":"directory"}'
$R uss-mkdir-exists POST 'restfiles/fs/u/ibmuser/liztest-fix' -H 'Content-Type: application/json' --data-binary '{"type":"directory"}'
printf 'hello\n\n\xac end\n' > /tmp/liz-hello.txt
$R uss-write-204 PUT 'restfiles/fs/u/ibmuser/liztest-fix/hello.txt' -H 'Content-Type: text/plain' -H 'X-IBM-Data-Type: text' --data-binary @/tmp/liz-hello.txt
$R uss-write-etag-204 PUT 'restfiles/fs/u/ibmuser/liztest-fix/hello.txt' -H 'Content-Type: text/plain' -H 'X-IBM-Data-Type: text' -H 'X-IBM-Return-Etag: true' --data-binary @/tmp/liz-hello.txt
$R uss-read-text GET 'restfiles/fs/u/ibmuser/liztest-fix/hello.txt' -H 'X-IBM-Data-Type: text' -H 'X-IBM-Return-Etag: true'
$R uss-read-binary GET 'restfiles/fs/u/ibmuser/liztest-fix/hello.txt' -H 'X-IBM-Data-Type: binary' -H 'X-IBM-Return-Etag: true'
$R uss-write-412 PUT 'restfiles/fs/u/ibmuser/liztest-fix/hello.txt' -H 'Content-Type: text/plain' -H 'X-IBM-Data-Type: text' -H 'If-Match: 0000000000000000' --data-binary @/tmp/liz-hello.txt
$R uss-read-missing GET 'restfiles/fs/u/ibmuser/liztest-fix/nope.txt' -H 'X-IBM-Data-Type: text'
$R uss-stat-file GET 'restfiles/fs?path=/u/ibmuser/liztest-fix/hello.txt'
```

Expected: `uss-mkdir-201: HTTP/1.1 201 Created`, `uss-mkdir-exists: HTTP/1.1 400 Bad Request`, `uss-write-204: HTTP/1.1 204 No Content`, `uss-write-etag-204: 204`, `uss-read-text: 200`, `uss-read-binary: 200`, `uss-write-412: HTTP/1.1 412 Precondition Failed`, `uss-read-missing: 404`, `uss-stat-file: 200`.

- [ ] **Step 4: Record the oversize write and the deletes**

```bash
head -c 70000 /dev/zero | tr '\0' 'x' > /tmp/liz-big.bin
$R uss-write-too-large PUT 'restfiles/fs/u/ibmuser/liztest-fix/big.bin' -H 'Content-Type: application/octet-stream' -H 'X-IBM-Data-Type: binary' --data-binary @/tmp/liz-big.bin
curl -sS --netrc-file ~/.mvsmf-netrc "$LIZTERM_MVSMF_URL/restfiles/fs?path=/u/ibmuser/liztest-fix" | grep -A2 '"name": "big.bin"'
$R uss-delete-204 DELETE 'restfiles/fs/u/ibmuser/liztest-fix' -H 'X-IBM-Option: recursive'
$R uss-delete-missing DELETE 'restfiles/fs/u/ibmuser/liztest-fix' -H 'X-IBM-Option: recursive'
rm /tmp/liz-hello.txt /tmp/liz-big.bin
```

Expected: `uss-write-too-large: HTTP/1.1 500 Internal Server Error`; the `grep` shows `big.bin` listed with a size under 70000 (the host kept what fit: write that size into the README row); `uss-delete-204: 204`; `uss-delete-missing: 404`. Then repeat Step 1's listing: `/u/ibmuser` must be empty again.

- [ ] **Step 5: Check the recordings**

```bash
cd tests/LizTerm.Backend.Mvsmf.Tests/Fixtures
grep -l '^ETag:' uss-*.http
grep -h '^ETag:' uss-read-text.http uss-read-binary.http uss-write-etag-204.http
tail -c 90 uss-list-missing.http; echo
tail -c 90 uss-read-directory.http; echo
tail -c 90 uss-mkdir-exists.http; echo
tail -c 120 uss-mkdir-no-parent.http; echo
tail -c 90 uss-write-412.http; echo
tail -c 90 uss-write-too-large.http; echo
tail -c 200 uss-stat-file.http; echo
tail -c 6 uss-read-text.http | od -c
cd -
```

Expected: `grep -l` lists exactly `uss-read-binary.http`, `uss-read-text.http` and `uss-write-etag-204.http`, and the three `ETag:` values are the **same** 16 hex digits (the stamp is over the stored bytes); `uss-list-missing` ends `{"rc":8,"category":6,"reason":1,"message":"File not found"}`; `uss-read-directory` ends `"category":2,"reason":1,"message":"Is a directory"}`; `uss-mkdir-exists` ends `"category":4,"reason":1,"message":"File or directory already exists"}`; `uss-write-412` has `"reason":10`; `uss-write-too-large` has `"category":10` and `"Incomplete write to file"`; `uss-stat-file` has one item whose `"name"` is `"/u/ibmuser/liztest-fix/hello.txt"`; `uss-read-text` ends in the bytes `\xac` ` ` `e` `n` `d` `\n` (the host sent `¬` as Latin-1, one byte). If any body differs in shape, stop and report it: Task 4's mapping depends on it.

- [ ] **Step 6: Update the fixtures README**

In `tests/LizTerm.Backend.Mvsmf.Tests/Fixtures/README.md`, change the "Recorded 2026-09-18" sentence to "Recorded 2026-09-18 (`uss-*` on 2026-09-22) from an MVS/CE host running mvsMF 1.1.0 (`zosmf_full_version`)." and after the `delete-ds-missing` row add:

```markdown
| `uss-list-root` | `GET restfiles/fs?path=/` — `tmp`, `u`, `www`; `user` and `group` empty |
| `uss-list-empty` | `GET restfiles/fs?path=/u/ibmuser` — no items |
| `uss-list-missing` | `GET restfiles/fs?path=/u/nobody` — 404, category 6, reason 1 |
| `uss-list-truncated` | `GET restfiles/fs?path=/u` with `X-IBM-Max-Items: 1` — one of three, `moreRows: true` |
| `uss-stat-file` | `GET restfiles/fs?path=<a file>` — 200 with one item naming the full path |
| `uss-read-text` | `GET restfiles/fs/u/ibmuser/liztest-fix/hello.txt`, text, with `X-IBM-Return-Etag: true` |
| `uss-read-binary` | the same, binary — the same `ETag` |
| `uss-read-missing` | `GET` of a file that is not there — 404, reason 1 |
| `uss-read-directory` | `GET restfiles/fs/u` — 400, category 2, reason 1, "Is a directory" |
| `uss-write-204` | `PUT` of `hello.txt` |
| `uss-write-etag-204` | the same with `X-IBM-Return-Etag: true` — 204 with `ETag` |
| `uss-write-412` | the same with a stale `If-Match` — 412, reason 10 |
| `uss-write-too-large` | `PUT` of 70,000 bytes — 500, category 10, "Incomplete write to file", after storing <size> bytes |
| `uss-mkdir-201` | `POST restfiles/fs/u/ibmuser/liztest-fix` with `{"type":"directory"}` — 201 |
| `uss-mkdir-exists` | the same again — 400, category 4, reason 1, "File or directory already exists" |
| `uss-mkdir-no-parent` | `POST restfiles/fs/u/nobody/child` — <status as recorded> |
| `uss-delete-204` | `DELETE` of the directory with `X-IBM-Option: recursive` — 204 |
| `uss-delete-missing` | the same again — 404, reason 1 |
```

Fill `<size>` and `<status as recorded>` from Steps 2 and 4.

- [ ] **Step 7: Commit**

```bash
git add tests/LizTerm.Backend.Mvsmf.Tests/Fixtures
git commit -m "Record the mvsMF USS fixtures (#176)

Eighteen exchanges from the 1.1.0 host: the listings (root, empty,
missing, truncated, a file's stat), reads (text, binary, missing, a
directory), writes (plain, stamped, a stale If-Match, past 64 KB), the
directory create and its two failures, and the recursive delete.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 4: Backend — the USS routes

**Files:**
- Modify: `src/LizTerm.Backend.Mvsmf/MvsmfJson.cs`, `src/LizTerm.Backend.Mvsmf/MvsmfErrors.cs`, `src/LizTerm.Backend.Mvsmf/MvsmfFileService.cs`, `src/LizTerm.Backend.Mvsmf/CLAUDE.md`
- Create: `tests/LizTerm.Backend.Mvsmf.Tests/MvsmfUnixTests.cs`
- Modify: `tests/LizTerm.Backend.Mvsmf.Tests/MvsmfErrorsTests.cs`

**Interfaces:**
- Consumes: Task 2's contract and entry types; Task 3's fixtures.
- Produces: `MvsmfFileService.ListDirectoryAsync`, `CreateDirectoryAsync`, UNIX routing in `ReadTextAsync`, `ReadBinaryAsync`, `WriteTextAsync`, `WriteBinaryAsync`, `DeleteAsync`; `internal static string EscapeUnixPath(string path)`; `MvsmfErrors.Classify` returning `AlreadyExists` for the USS create.

- [ ] **Step 1: Write the failing tests**

Create `tests/LizTerm.Backend.Mvsmf.Tests/MvsmfUnixTests.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Net;
using System.Text;
using LizTerm.Core.HostFiles;

namespace LizTerm.Backend.Mvsmf.Tests;

public class MvsmfUnixTests
{
    private static readonly HostPath Home = HostPath.ForUnix("/u/ibmuser");
    private static readonly HostPath Fix = HostPath.ForUnix("/u/ibmuser/liztest-fix");
    private static readonly HostPath Hello = HostPath.ForUnix("/u/ibmuser/liztest-fix/hello.txt");

    private static MvsmfFileService Service(RecordedHandler handler) =>
        new(handler, MvsmfAuthTests.Base, MvsmfAuthTests.Providing([], new HostCredentials("IBMUSER", "pw")));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_directory_listing_reads_kinds_sizes_and_times()
    {
        var handler = new RecordedHandler().Then("login-200").Then("uss-list-root");
        using var service = Service(handler);

        var listing = await service.ListDirectoryAsync(HostPath.ForUnix("/"), HostListRequest.All, Ct);

        Assert.True(listing.IsComplete);
        Assert.False(listing.Truncated);
        Assert.Equal(new[] { "tmp", "u", "www" }, listing.Entries.Select(e => e.Name));
        Assert.All(listing.Entries, e => Assert.Equal(HostFileEntryKind.Directory, e.Kind));
        Assert.All(listing.Entries, e => Assert.Null(e.Attributes));
        var u = listing.Entries[1].Unix!;
        Assert.Equal(128, u.Size);
        Assert.Equal(new DateTimeOffset(2026, 9, 17, 20, 36, 34, TimeSpan.Zero), u.Modified);
        var request = handler.Requests[1];
        Assert.Equal("/zosmf/restfiles/fs?path=/", request.Uri.PathAndQuery);
        Assert.Equal("0", request.Headers["X-IBM-Max-Items"]);
    }

    [Fact]
    public async Task An_empty_directory_lists_nothing()
    {
        using var service = Service(new RecordedHandler().Then("login-200").Then("uss-list-empty"));

        var listing = await service.ListDirectoryAsync(Home, HostListRequest.All, Ct);

        Assert.Empty(listing.Entries);
        Assert.True(listing.IsComplete);
    }

    [Fact]
    public async Task A_file_in_a_listing_is_a_file_entry()
    {
        var handler = new RecordedHandler().Then("login-200").Then(HttpStatusCode.OK,
            """{"items":[{"name":"notes.txt","mode":"-rw-r--r--","size":1204,"user":"","group":"","links":1,"mtime":"2026-09-22T09:12:00Z","inode":9},{"name":"drafts","mode":"drwxr-xr-x","size":128,"user":"","group":"","links":2,"mtime":"not a date","inode":10}],"returnedRows":2,"totalRows":2,"JSONversion":1}""");
        using var service = Service(handler);

        var entries = (await service.ListDirectoryAsync(Home, HostListRequest.All, Ct)).Entries;

        Assert.Equal(HostFileEntryKind.File, entries[0].Kind);
        Assert.Equal(1204, entries[0].Unix!.Size);
        Assert.Equal(HostFileEntryKind.Directory, entries[1].Kind);
        Assert.Null(entries[1].Unix!.Modified);
    }

    [Fact]
    public async Task Uss_list_no_continuation_so_a_cut_listing_is_truncated_and_a_continuation_is_refused()
    {
        var handler = new RecordedHandler().Then("login-200").Then("uss-list-truncated");
        using var service = Service(handler);

        var listing = await service.ListDirectoryAsync(HostPath.ForUnix("/u"), new HostListRequest(MaxItems: 1), Ct);

        Assert.Single(listing.Entries);
        Assert.True(listing.Truncated);
        Assert.False(listing.IsComplete);
        Assert.Null(listing.Continuation);
        Assert.Equal("1", handler.Requests[1].Headers["X-IBM-Max-Items"]);
        await Assert.ThrowsAsync<ArgumentException>(() => service.ListDirectoryAsync(HostPath.ForUnix("/u"), new HostListRequest(MaxItems: 1, Continuation: "ibmuser"), Ct));
    }

    [Fact]
    public async Task A_missing_directory_is_not_found()
    {
        using var service = Service(new RecordedHandler().Then("login-200").Then("uss-list-missing"));

        var ex = await Assert.ThrowsAsync<HostFileException>(() => service.ListDirectoryAsync(HostPath.ForUnix("/u/nobody"), HostListRequest.All, Ct));

        Assert.Equal(HostFileErrorKind.NotFound, ex.Kind);
        Assert.Equal("/u/nobody: not found.", ex.Message);
    }

    [Fact]
    public async Task Uss_stat_for_file_path_so_listing_a_file_is_an_invalid_request()
    {
        using var service = Service(new RecordedHandler().Then("login-200").Then("uss-stat-file"));

        var ex = await Assert.ThrowsAsync<HostFileException>(() => service.ListDirectoryAsync(Hello, HostListRequest.All, Ct));

        Assert.Equal(HostFileErrorKind.InvalidRequest, ex.Kind);
        Assert.Equal("/u/ibmuser/liztest-fix/hello.txt: is a file, not a directory.", ex.Message);
    }

    [Fact]
    public async Task A_text_read_routes_to_fs_and_keeps_latin1_with_the_stamp_when_asked()
    {
        var handler = new RecordedHandler().Then("login-200").Then("uss-read-text");
        using var service = Service(handler);

        var read = await service.ReadTextAsync(Hello, withEtag: true, cancellationToken: Ct);

        Assert.Equal(new[] { "hello", "", "¬ end" }, read.Lines);
        Assert.Matches("^[0-9A-Fa-f]{16}$", read.Etag);
        var request = handler.Requests[1];
        Assert.Equal("/zosmf/restfiles/fs/u/ibmuser/liztest-fix/hello.txt", request.Uri.PathAndQuery);
        Assert.Equal("text", request.DataType);
        Assert.Equal("true", request.Headers["X-IBM-Return-Etag"]);
    }

    [Fact]
    public async Task A_binary_read_gives_the_same_stamp_as_the_text_read()
    {
        using var service = Service(new RecordedHandler().Then("login-200").Then("uss-read-text").Then("uss-read-binary"));
        var text = await service.ReadTextAsync(Hello, withEtag: true, cancellationToken: Ct);
        using var bytes = new MemoryStream();

        var binary = await service.ReadBinaryAsync(Hello, bytes, withEtag: true, cancellationToken: Ct);

        Assert.Equal(text.Etag, binary.Etag);
        Assert.Equal(bytes.Length, binary.Bytes);
        Assert.True(binary.Bytes > 0);
    }

    [Fact]
    public async Task A_missing_file_is_not_found_and_a_directory_is_an_invalid_request()
    {
        using var service = Service(new RecordedHandler().Then("login-200").Then("uss-read-missing").Then("uss-read-directory"));

        var missing = await Assert.ThrowsAsync<HostFileException>(() => service.ReadTextAsync(Fix.Child("nope.txt"), cancellationToken: Ct));
        var directory = await Assert.ThrowsAsync<HostFileException>(() => service.ReadTextAsync(HostPath.ForUnix("/u"), cancellationToken: Ct));

        Assert.Equal(HostFileErrorKind.NotFound, missing.Kind);
        Assert.Equal(HostFileErrorKind.InvalidRequest, directory.Kind);
        Assert.Equal("Is a directory", directory.ServerMessage);
    }

    [Fact]
    public async Task A_text_write_puts_latin1_lines_to_fs_and_returns_the_stamp()
    {
        var handler = new RecordedHandler().Then("login-200").Then("uss-write-etag-204");
        using var service = Service(handler);

        var etag = await service.WriteTextAsync(Hello, ["hello", "", "¬ end"], cancellationToken: Ct);

        Assert.Matches("^[0-9A-Fa-f]{16}$", etag);
        var request = handler.Requests[1];
        Assert.Equal(HttpMethod.Put, request.Method);
        Assert.Equal("/zosmf/restfiles/fs/u/ibmuser/liztest-fix/hello.txt", request.Uri.PathAndQuery);
        Assert.Equal("text/plain", request.ContentType);
        Assert.Equal("text", request.DataType);
        Assert.Equal(new byte[] { (byte)'h', (byte)'e', (byte)'l', (byte)'l', (byte)'o', (byte)'\n', (byte)'\n', 0xAC, (byte)' ', (byte)'e', (byte)'n', (byte)'d', (byte)'\n' }, request.Body);
        Assert.Equal("true", request.Headers["X-IBM-Return-Etag"]);
    }

    [Fact]
    public async Task A_binary_write_sends_octet_stream_and_if_match_verbatim()
    {
        var handler = new RecordedHandler().Then("login-200").Then("uss-write-204");
        using var service = Service(handler);

        await service.WriteBinaryAsync(Fix.Child("data.bin"), new MemoryStream([1, 2, 3]), ifMatch: "ABCDEF0123456789", Ct);

        var request = handler.Requests[1];
        Assert.Equal("/zosmf/restfiles/fs/u/ibmuser/liztest-fix/data.bin", request.Uri.PathAndQuery);
        Assert.Equal("application/octet-stream", request.ContentType);
        Assert.Equal("binary", request.DataType);
        Assert.Equal(new byte[] { 1, 2, 3 }, request.Body);
        Assert.Equal("ABCDEF0123456789", request.Headers["If-Match"]);
    }

    [Fact]
    public async Task A_stale_stamp_is_a_conflict()
    {
        using var service = Service(new RecordedHandler().Then("login-200").Then("uss-write-412"));

        var ex = await Assert.ThrowsAsync<HostFileException>(() => service.WriteTextAsync(Hello, ["x"], ifMatch: "0000000000000000", Ct));

        Assert.Equal(HostFileErrorKind.Conflict, ex.Kind);
        Assert.Equal(10, ex.Reason);
    }

    [Fact]
    public async Task Uss_limits_so_a_write_past_the_cap_is_the_hosts_server_error()
    {
        using var service = Service(new RecordedHandler().Then("login-200").Then("uss-write-too-large"));

        var ex = await Assert.ThrowsAsync<HostFileException>(() => service.WriteBinaryAsync(Fix.Child("big.bin"), new MemoryStream(new byte[70_000]), cancellationToken: Ct));

        Assert.Equal(HostFileErrorKind.ServerError, ex.Kind);
        Assert.Equal("Incomplete write to file", ex.ServerMessage);
        Assert.Equal(65_536, HostFileLimits.MaxUnixFileBytes);
    }

    [Fact]
    public async Task A_directory_is_created_with_a_json_post()
    {
        var handler = new RecordedHandler().Then("login-200").Then("uss-mkdir-201");
        using var service = Service(handler);

        await service.CreateDirectoryAsync(Fix, Ct);

        var request = handler.Requests[1];
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/zosmf/restfiles/fs/u/ibmuser/liztest-fix", request.Uri.PathAndQuery);
        Assert.Equal("application/json", request.ContentType);
        Assert.Equal("""{"type":"directory"}""", request.BodyText);
    }

    [Fact]
    public async Task Uss_create_errors_400_so_an_existing_name_already_exists()
    {
        using var service = Service(new RecordedHandler().Then("login-200").Then("uss-mkdir-exists"));

        var ex = await Assert.ThrowsAsync<HostFileException>(() => service.CreateDirectoryAsync(Fix, Ct));

        Assert.Equal(HostFileErrorKind.AlreadyExists, ex.Kind);
        Assert.Equal("/u/ibmuser/liztest-fix: a file or directory of that name already exists.", ex.Message);
    }

    [Fact]
    public async Task Creating_under_a_missing_parent_is_not_found()
    {
        using var service = Service(new RecordedHandler().Then("login-200").Then("uss-mkdir-no-parent"));

        var ex = await Assert.ThrowsAsync<HostFileException>(() => service.CreateDirectoryAsync(HostPath.ForUnix("/u/nobody/child"), Ct));

        Assert.Equal(HostFileErrorKind.NotFound, ex.Kind);
    }

    [Fact]
    public async Task A_delete_sends_recursive_for_every_unix_path()
    {
        var handler = new RecordedHandler().Then("login-200").Then("uss-delete-204").Then("uss-delete-missing");
        using var service = Service(handler);

        await service.DeleteAsync(Fix, Ct);
        var gone = await Assert.ThrowsAsync<HostFileException>(() => service.DeleteAsync(Fix, Ct));

        var request = handler.Requests[1];
        Assert.Equal(HttpMethod.Delete, request.Method);
        Assert.Equal("/zosmf/restfiles/fs/u/ibmuser/liztest-fix", request.Uri.PathAndQuery);
        Assert.Equal("recursive", request.Headers["X-IBM-Option"]);
        Assert.Equal(HostFileErrorKind.NotFound, gone.Kind);
    }

    [Fact]
    public async Task A_dataset_delete_sends_no_option()
    {
        var handler = new RecordedHandler().Then("login-200").Then("delete-204");
        using var service = Service(handler);

        await service.DeleteAsync(HostPath.ForMember("MVSCE02.CNTL", "NEWMEM"), Ct);

        Assert.DoesNotContain("X-IBM-Option", handler.Requests[1].HeaderNames, StringComparer.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("/u/ibmuser/hello world#1.txt", "/u/ibmuser/hello%20world%231.txt")]
    [InlineData("/u/ibmuser/100%.txt", "/u/ibmuser/100%25.txt")]
    [InlineData("/u/ibmuser/a?b&c=d", "/u/ibmuser/a%3Fb%26c%3Dd")]
    [InlineData("/u/ibmuser/Notes-1_2.txt~", "/u/ibmuser/Notes-1_2.txt~")]
    [InlineData("/café", "/caf%C3%A9")]
    public void A_unix_path_is_escaped_segment_by_segment(string path, string expected) =>
        Assert.Equal(expected, MvsmfFileService.EscapeUnixPath(path));

    [Fact]
    public async Task A_listing_query_carries_the_escaped_path()
    {
        var handler = new RecordedHandler().Then("login-200").Then("uss-list-empty");
        using var service = Service(handler);

        await service.ListDirectoryAsync(HostPath.ForUnix("/u/ibmuser/my notes"), HostListRequest.All, Ct);

        Assert.Equal("/zosmf/restfiles/fs?path=/u/ibmuser/my%20notes", handler.Requests[1].Uri.PathAndQuery);
    }

    [Fact]
    public async Task The_dataset_verbs_refuse_a_unix_path_and_rename_refuses_it_before_any_request()
    {
        var handler = new RecordedHandler().Then("login-200");
        using var service = Service(handler);

        await Assert.ThrowsAsync<ArgumentException>(() => service.ListMembersAsync(Home, HostListRequest.All, Ct));
        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateDatasetAsync(Home,
            new DatasetAllocation(DatasetOrganization.Sequential, "FB", 80, 3120, SpaceUnit.Tracks, 1, 1, 0), Ct));
        var rename = await Assert.ThrowsAsync<ArgumentException>(() => service.RenameAsync(Hello, "other.txt", Ct));
        await Assert.ThrowsAsync<ArgumentException>(() => service.ListDirectoryAsync(HostPath.ForDataset("SYS1.PROCLIB"), HostListRequest.All, Ct));
        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateDirectoryAsync(HostPath.ForDataset("SYS1.PROCLIB"), Ct));

        Assert.StartsWith("The host cannot rename a UNIX file.", rename.Message);
        Assert.Empty(handler.Requests);
    }
}
```

Check `DatasetAllocation`'s constructor order against `src/LizTerm.Core/HostFiles/DatasetAllocation.cs` before relying on the `CreateDatasetAsync` line; the live test in `LiveMvsmfTests.cs` uses `new DatasetAllocation(DatasetOrganization.Partitioned, "FB", 80, 3120, SpaceUnit.Tracks, 1, 1, 1)`, so the shape above matches it.

In `tests/LizTerm.Backend.Mvsmf.Tests/MvsmfErrorsTests.cs`, add two rows to `Classifies_by_reason_before_status`:

```csharp
    [InlineData(HttpStatusCode.NotFound, 6, 8, 1, HostFileErrorKind.NotFound)]
    [InlineData(HttpStatusCode.BadRequest, 2, 8, 1, HostFileErrorKind.InvalidRequest)]
```

and a new test:

```csharp
    [Fact]
    public void Uss_create_errors_400_so_already_exists_is_told_by_its_message()
    {
        Assert.Equal(HostFileErrorKind.AlreadyExists, MvsmfErrors.Classify(HttpStatusCode.BadRequest, 4, 8, 1, "File or directory already exists"));
        Assert.Equal(HostFileErrorKind.InvalidRequest, MvsmfErrors.Classify(HttpStatusCode.BadRequest, 4, 8, 1, "Path name too long"));
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/LizTerm.Backend.Mvsmf.Tests --filter "FullyQualifiedName~MvsmfUnixTests" 2>&1 | grep -E "error CS|Passed!|Failed!" | head -5`
Expected: build errors: the backend does not implement `ListDirectoryAsync` and `CreateDirectoryAsync`, and `EscapeUnixPath` does not exist.

- [ ] **Step 3: The JSON types**

In `src/LizTerm.Backend.Mvsmf/MvsmfJson.cs`, after `MvsmfRenameSource`, add:

```csharp
/// <summary>One entry of a file system listing. <c>size</c> is a number on mvsMF; read leniently in case a host
/// quotes it. <c>user</c>, <c>group</c>, <c>links</c> and <c>inode</c> are not read: nothing shows them.</summary>
internal sealed record MvsmfUnixEntry(
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("mode")] string? Mode,
    [property: JsonPropertyName("size"), JsonConverter(typeof(LenientStringConverter))] string? Size,
    [property: JsonPropertyName("mtime")] string? Mtime);

internal sealed record MvsmfUnixList(
    [property: JsonPropertyName("items")] List<MvsmfUnixEntry>? Items,
    [property: JsonPropertyName("moreRows")] bool? MoreRows);

/// <summary>A create's body on the file system route: <c>directory</c> is the only type LizTerm sends.</summary>
internal sealed record MvsmfUnixCreate([property: JsonPropertyName("type")] string Type);
```

and two attributes on the context:

```csharp
[JsonSerializable(typeof(MvsmfUnixList))]
[JsonSerializable(typeof(MvsmfUnixCreate))]
```

- [ ] **Step 4: The error mapping**

In `src/LizTerm.Backend.Mvsmf/MvsmfErrors.cs`:

Add a constant: `private const int UnixCategory = 4;` is misleading (4 is the security category the host reuses), so instead add to `Classify`, just before `if (status == HttpStatusCode.BadRequest) return HostFileErrorKind.InvalidRequest;`:

```csharp
        // mvsMF-compat: uss-create-errors-400 — a file system create onto an existing name is 400, category 4 (the
        // security category, reused), reason 1, told from the route's other 400s only by its message.
        if (status == HttpStatusCode.BadRequest && message?.Contains("already exists", StringComparison.OrdinalIgnoreCase) == true)
            return HostFileErrorKind.AlreadyExists;
```

Change the `AlreadyExists` arm of `Describe` to:

```csharp
        HostFileErrorKind.AlreadyExists => error?.Category == SecurityCategory
            ? $"{what}: a file or directory of that name already exists."
            : $"{what}: a member of that name already exists.",
```

- [ ] **Step 5: The service**

In `src/LizTerm.Backend.Mvsmf/MvsmfFileService.cs`:

After `ListMembersAsync`, add:

```csharp
    public async Task<HostFileListing> ListDirectoryAsync(HostPath directory, HostListRequest request, CancellationToken cancellationToken = default)
    {
        if (directory.Kind != HostPathKind.Unix) throw new ArgumentException("Only a UNIX path names a directory.", nameof(directory));
        // mvsMF-compat: uss-list-no-continuation — the file system listing has X-IBM-Max-Items and moreRows but no
        // start=, so a cut listing cannot be continued: a continuation is refused here, and a cut is reported as
        // Truncated. Without the header the host stops at 1,000 entries, and 0 asks for all, so it is always sent.
        if (request.Continuation is not null) throw new ArgumentException("A directory listing cannot be continued.", nameof(request));
        var what = directory.ToString();
        using var idle = new IdleTimeout(_idle, cancellationToken);
        using var response = await SendAsync(() =>
        {
            var message = new HttpRequestMessage(HttpMethod.Get, Url($"restfiles/fs?path={EscapeUnixPath(directory.UnixPath!)}"));
            message.Headers.Add("X-IBM-Max-Items", Math.Max(request.MaxItems, 0).ToString(CultureInfo.InvariantCulture));
            return message;
        }, what, idle, cancellationToken);
        var list = await ReadJsonAsync(response, MvsmfJsonContext.Default.MvsmfUnixList, what, idle, cancellationToken);
        var items = (list.Items ?? []).Where(i => !string.IsNullOrWhiteSpace(i.Name)).ToList();
        // mvsMF-compat: uss-stat-for-file-path — a listing of a file path answers 200 with one item naming the full
        // path (a stat), never an error; LizTerm reads that shape as "not a directory".
        if (items.Count == 1 && items[0].Name!.StartsWith('/'))
            throw new HostFileException(HostFileErrorKind.InvalidRequest, $"{what}: is a file, not a directory.");
        var entries = items.Select(ToUnixEntry).ToList();
        if (request.MaxItems > 0 && entries.Count > request.MaxItems) entries.RemoveRange(request.MaxItems, entries.Count - request.MaxItems);
        return new HostFileListing(entries, null, Truncated: list.MoreRows == true);
    }

    private static HostFileEntry ToUnixEntry(MvsmfUnixEntry item) => new(
        item.Name!.Trim(),
        item.Mode is { Length: > 0 } mode && mode[0] == 'd' ? HostFileEntryKind.Directory : HostFileEntryKind.File,
        Unix: new UnixFileAttributes(
            long.TryParse(item.Size, NumberStyles.None, CultureInfo.InvariantCulture, out var size) ? size : 0,
            DateTimeOffset.TryParse(item.Mtime, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var modified) ? modified : null));
```

After `CreateDatasetAsync`, add:

```csharp
    public async Task CreateDirectoryAsync(HostPath directory, CancellationToken cancellationToken = default)
    {
        if (directory.Kind != HostPathKind.Unix) throw new ArgumentException("Only a UNIX path names a directory.", nameof(directory));
        var what = directory.ToString();
        var body = JsonSerializer.SerializeToUtf8Bytes(new MvsmfUnixCreate("directory"), MvsmfJsonContext.Default.MvsmfUnixCreate);
        using var idle = new IdleTimeout(_idle, cancellationToken);
        // mvsMF-compat: uss-create-errors-400 — an existing name is 400 "File or directory already exists", which
        // MvsmfErrors maps to AlreadyExists by the message; a missing parent is the host's not-found answer.
        using var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Post, Url(Route(directory)))
        {
            Content = new ByteArrayContent(body) { Headers = { ContentType = new MediaTypeHeaderValue(JsonContentType) } },
        }, what, idle, cancellationToken);
    }
```

In `RenameAsync`, as its first line:

```csharp
        if (from.Kind == HostPathKind.Unix) throw new ArgumentException("The host cannot rename a UNIX file.", nameof(from));
```

Replace `DeleteAsync` with:

```csharp
    public async Task DeleteAsync(HostPath path, CancellationToken cancellationToken = default)
    {
        var what = path.ToString();
        using var idle = new IdleTimeout(_idle, cancellationToken);
        using var response = await SendAsync(() =>
        {
            var request = new HttpRequestMessage(HttpMethod.Delete, Url(Route(path)));
            // A directory with anything in it needs X-IBM-Option: recursive; it is sent for every UNIX path, so a
            // delete never fails for being non-empty, and the App's question owns that fact.
            if (path.Kind == HostPathKind.Unix) request.Headers.Add("X-IBM-Option", "recursive");
            return request;
        }, what, idle, cancellationToken);
    }
```

In `Get` change `Url(DatasetPath(path))` to `Url(Route(path))`; in `PutAsync` the same. After `DatasetPath`, add:

```csharp
    /// <summary>The route for a path of any kind: <c>restfiles/fs/&lt;path&gt;</c> for a UNIX path, else
    /// <see cref="DatasetPath"/>.</summary>
    // mvsMF-compat: uss-limits — a UNIX file holds at most HostFileLimits.MaxUnixFileBytes; the host writes what
    // fits and only then answers 500, so Core checks the size before any write reaches this route.
    private static string Route(HostPath path) =>
        path.Kind == HostPathKind.Unix ? "restfiles/fs" + EscapeUnixPath(path.UnixPath!) : DatasetPath(path);

    /// <summary>A UNIX path for a URL: <c>/</c> and the RFC 3986 unreserved characters kept, everything else
    /// percent-encoded from UTF-8, so a name with a space, <c>#</c>, <c>%</c> or <c>?</c> round-trips (the host
    /// percent-decodes the path and the query alike).</summary>
    internal static string EscapeUnixPath(string path)
    {
        var builder = new StringBuilder(path.Length);
        foreach (var b in Encoding.UTF8.GetBytes(path))
        {
            var c = (char)b;
            if (char.IsAsciiLetterOrDigit(c) || c is '/' or '-' or '.' or '_' or '~') builder.Append(c);
            else builder.Append('%').Append(b.ToString("X2", CultureInfo.InvariantCulture));
        }
        return builder.ToString();
    }
```

- [ ] **Step 6: Run the backend tests**

Run: `dotnet test tests/LizTerm.Backend.Mvsmf.Tests 2>&1 | grep -E "error CS|Passed!|Failed!|\[FAIL\]" | head -10`
Expected: `Passed!`, every test in the project. If `Creating_under_a_missing_parent_is_not_found` fails because the host recorded a 400 in Task 3, the recorded body decides: change the test's expected kind to `HostFileErrorKind.InvalidRequest`, and in Task 7 word the `uss-create-errors-400` entry accordingly (the spec's §4.1 promise of `NotFound` for a missing parent then needs the sentence "on the tested build a missing parent is reported as an invalid request" in the entry and in `IHostFileService`'s doc comment).

- [ ] **Step 7: Build the whole solution and count warnings**

Run: `dotnet build LizTerm.slnx --no-incremental 2>&1 | grep -c " warning "; dotnet test LizTerm.slnx 2>&1 | grep -E "Passed!|Failed!|error" | head -10`
Expected: `0`, then every project `Passed!`. The red boundary is closed.

- [ ] **Step 8: Backend notes**

In `src/LizTerm.Backend.Mvsmf/CLAUDE.md`, after the "A create posts the allocation as JSON" bullet, add:

```markdown
- **The file system routes.** A `HostPathKind.Unix` path goes to `restfiles/fs/<path>` through `Route`, escaped
  by `EscapeUnixPath` (`/` and the unreserved characters kept, everything else percent-encoded), and the read,
  write and stamp code is the dataset side's: the text body is Latin-1 both ways here too (the host translates to
  and from IBM-1047 rather than the dataset routes' code page, which changes nothing on the wire), and a text and
  a binary read give the same `ETag`. `ListDirectoryAsync` sends `GET restfiles/fs?path=<escaped>` with
  `X-IBM-Max-Items` always (`0` = all; without it the host stops at 1,000), reads `mode`'s first character for
  the kind, and reports a cut listing as `Truncated` with no continuation, since the route has no `start=`
  (`uss-list-no-continuation`); a file path answers a one-item stat listing, read as `InvalidRequest`
  (`uss-stat-for-file-path`). `CreateDirectoryAsync` is the one other request that sends `application/json`, a
  `POST` of `{"type":"directory"}`; an existing name is 400 "File or directory already exists", mapped to
  `AlreadyExists` by its message (`uss-create-errors-400`). `DeleteAsync` sends `X-IBM-Option: recursive` on every
  UNIX path. `RenameAsync` refuses a UNIX path: the host has no rename. The 64 KB cap (`uss-limits`) is Core's
  `HostFileLimits.MaxUnixFileBytes`, checked before a request; a write past it is the host's 500 "Incomplete
  write to file" after it stored what fit.
```

- [ ] **Step 9: Commit**

```bash
git add src/LizTerm.Backend.Mvsmf tests/LizTerm.Backend.Mvsmf.Tests/MvsmfUnixTests.cs tests/LizTerm.Backend.Mvsmf.Tests/MvsmfErrorsTests.cs
git commit -m "mvsMF backend: the file system routes (#176)

ListDirectoryAsync over GET restfiles/fs?path= (X-IBM-Max-Items always,
a cut listing is Truncated, a file's stat is InvalidRequest),
CreateDirectoryAsync as a JSON POST (an existing name is AlreadyExists
by its message), reads, writes and deletes routed to restfiles/fs with
the path escaped segment by segment, recursive on every delete, and
RenameAsync refusing a UNIX path.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 5: App — the fake host plays the file system

**Files:**
- Modify: `tests/LizTerm.App.Tests/Fakes/FakeHostFileService.cs`, `tests/CLAUDE.md`
- Test: `tests/LizTerm.App.Tests/HostFiles/FakeHostFileServiceTests.cs`

**Interfaces:**
- Consumes: Task 2's contract.
- Produces: on the App fake, `HashSet<string> Directories` (seeded with `/`), `AddDirectory(string path)`, `AddFile(string path, params string[] lines)`, `AddBinaryFile(string path, byte[] bytes)`; `Calls` entries `listdir:<path>` and `mkdir:<path>`; the read, write and delete keys already in use, applied to UNIX paths.

- [ ] **Step 1: Write the failing tests**

Append to `tests/LizTerm.App.Tests/HostFiles/FakeHostFileServiceTests.cs`, inside the class (match the file's existing helper for making the fake; the tests below construct it directly):

```csharp
    [Fact]
    public async Task A_directory_lists_its_subdirectories_then_its_files_with_sizes()
    {
        var host = new FakeHostFileService();
        host.AddDirectory("/u/ibmuser/notes/drafts");
        host.AddFile("/u/ibmuser/notes/b.txt", "hello", "world");
        host.AddBinaryFile("/u/ibmuser/notes/a.bin", [1, 2, 3]);
        var ct = TestContext.Current.CancellationToken;

        var listing = await host.ListDirectoryAsync(HostPath.ForUnix("/u/ibmuser/notes"), HostListRequest.All, ct);

        Assert.Equal(new[] { "drafts", "a.bin", "b.txt" }, listing.Entries.Select(e => e.Name));
        Assert.Equal(new[] { HostFileEntryKind.Directory, HostFileEntryKind.File, HostFileEntryKind.File }, listing.Entries.Select(e => e.Kind));
        Assert.Equal(3, listing.Entries[1].Unix!.Size);
        Assert.Equal(12, listing.Entries[2].Unix!.Size);
        Assert.True(listing.IsComplete);
        Assert.Contains("listdir:/u/ibmuser/notes", host.Calls);
        Assert.Equal(new[] { "ibmuser" }, (await host.ListDirectoryAsync(HostPath.ForUnix("/u"), HostListRequest.All, ct)).Entries.Select(e => e.Name));
        Assert.Equal(new[] { "u" }, (await host.ListDirectoryAsync(HostPath.ForUnix("/"), HostListRequest.All, ct)).Entries.Select(e => e.Name));
    }

    [Fact]
    public async Task A_cut_listing_is_truncated_and_a_continuation_is_refused()
    {
        var host = new FakeHostFileService();
        host.AddFile("/u/a.txt", "a");
        host.AddFile("/u/b.txt", "b");
        var ct = TestContext.Current.CancellationToken;

        var listing = await host.ListDirectoryAsync(HostPath.ForUnix("/u"), new HostListRequest(MaxItems: 1), ct);

        Assert.Single(listing.Entries);
        Assert.True(listing.Truncated);
        Assert.Null(listing.Continuation);
        await Assert.ThrowsAsync<ArgumentException>(() => host.ListDirectoryAsync(HostPath.ForUnix("/u"), new HostListRequest(MaxItems: 1, Continuation: "a.txt"), ct));
    }

    [Fact]
    public async Task Listing_a_missing_path_is_not_found_and_a_file_is_an_invalid_request()
    {
        var host = new FakeHostFileService();
        host.AddFile("/u/a.txt", "a");
        var ct = TestContext.Current.CancellationToken;

        var missing = await Assert.ThrowsAsync<HostFileException>(() => host.ListDirectoryAsync(HostPath.ForUnix("/nope"), HostListRequest.All, ct));
        var file = await Assert.ThrowsAsync<HostFileException>(() => host.ListDirectoryAsync(HostPath.ForUnix("/u/a.txt"), HostListRequest.All, ct));

        Assert.Equal(HostFileErrorKind.NotFound, missing.Kind);
        Assert.Equal("/nope: not found.", missing.Message);
        Assert.Equal(1, missing.Reason);
        Assert.Equal(HostFileErrorKind.InvalidRequest, file.Kind);
    }

    [Fact]
    public async Task A_directory_is_created_under_an_existing_parent_only_and_once()
    {
        var host = new FakeHostFileService();
        host.AddDirectory("/u/ibmuser");
        var ct = TestContext.Current.CancellationToken;

        await host.CreateDirectoryAsync(HostPath.ForUnix("/u/ibmuser/notes"), ct);
        var again = await Assert.ThrowsAsync<HostFileException>(() => host.CreateDirectoryAsync(HostPath.ForUnix("/u/ibmuser/notes"), ct));
        var orphan = await Assert.ThrowsAsync<HostFileException>(() => host.CreateDirectoryAsync(HostPath.ForUnix("/u/nobody/notes"), ct));

        Assert.Contains("/u/ibmuser/notes", host.Directories);
        Assert.Contains("mkdir:/u/ibmuser/notes", host.Calls);
        Assert.Equal(HostFileErrorKind.AlreadyExists, again.Kind);
        Assert.Equal("/u/ibmuser/notes: a file or directory of that name already exists.", again.Message);
        Assert.Equal(HostFileErrorKind.NotFound, orphan.Kind);
        await Assert.ThrowsAsync<ArgumentException>(() => host.CreateDirectoryAsync(HostPath.ForDataset("A.B"), ct));
    }

    [Fact]
    public async Task Unix_files_are_written_read_stamped_and_deleted_by_path()
    {
        var host = new FakeHostFileService();
        host.AddDirectory("/u/ibmuser");
        var file = HostPath.ForUnix("/u/ibmuser/a.txt");
        var ct = TestContext.Current.CancellationToken;

        var stamp = await host.WriteTextAsync(file, ["one"], cancellationToken: ct);
        var read = await host.ReadTextAsync(file, withEtag: true, cancellationToken: ct);
        var conflict = await Assert.ThrowsAsync<HostFileException>(() => host.WriteTextAsync(file, ["two"], ifMatch: "stale", ct));
        var orphan = await Assert.ThrowsAsync<HostFileException>(() => host.WriteTextAsync(HostPath.ForUnix("/u/nobody/a.txt"), ["x"], cancellationToken: ct));
        await host.DeleteAsync(file, ct);
        var gone = await Assert.ThrowsAsync<HostFileException>(() => host.ReadTextAsync(file, cancellationToken: ct));

        Assert.Equal(new[] { "one" }, read.Lines);
        Assert.Equal(stamp, read.Etag);
        Assert.Equal(HostFileErrorKind.Conflict, conflict.Kind);
        Assert.Equal(HostFileErrorKind.NotFound, orphan.Kind);
        Assert.Equal(HostFileErrorKind.NotFound, gone.Kind);
        Assert.Contains("writetext:/u/ibmuser/a.txt:1", host.Calls);
        Assert.Contains("delete:/u/ibmuser/a.txt", host.Calls);
    }

    [Fact]
    public async Task Deleting_a_directory_takes_everything_under_it_and_rename_is_refused()
    {
        var host = new FakeHostFileService();
        host.AddFile("/u/ibmuser/notes/drafts/x.txt", "x");
        host.AddBinaryFile("/u/ibmuser/notes/y.bin", [1]);
        host.AddFile("/u/ibmuser/other.txt", "o");
        var ct = TestContext.Current.CancellationToken;

        await host.DeleteAsync(HostPath.ForUnix("/u/ibmuser/notes"), ct);
        var gone = await Assert.ThrowsAsync<HostFileException>(() => host.DeleteAsync(HostPath.ForUnix("/u/ibmuser/notes"), ct));

        Assert.DoesNotContain("/u/ibmuser/notes", host.Directories);
        Assert.DoesNotContain("/u/ibmuser/notes/drafts", host.Directories);
        Assert.Empty(host.Text.Keys.Where(k => k.StartsWith("/u/ibmuser/notes", StringComparison.Ordinal)));
        Assert.Empty(host.Binary.Keys.Where(k => k.StartsWith("/u/ibmuser/notes", StringComparison.Ordinal)));
        Assert.True(host.Text.ContainsKey("/u/ibmuser/other.txt"));
        Assert.Equal(HostFileErrorKind.NotFound, gone.Kind);
        await Assert.ThrowsAsync<ArgumentException>(() => host.RenameAsync(HostPath.ForUnix("/u/ibmuser/other.txt"), "z.txt", ct));
        Assert.DoesNotContain(host.Calls, c => c.StartsWith("rename:", StringComparison.Ordinal));
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~FakeHostFileServiceTests" 2>&1 | grep -E "error CS|Passed!|Failed!" | head -5`
Expected: build errors naming `AddDirectory`, `AddFile`, `AddBinaryFile`, `Directories`.

- [ ] **Step 3: Implement the fake's file system**

In `tests/LizTerm.App.Tests/Fakes/FakeHostFileService.cs`:

Extend the class summary's list of calls with `listdir:<path>` and `mkdir:<path>`, and add after `public Dictionary<string, byte[]> Binary { get; } = [];`:

```csharp
    /// <summary>Every UNIX directory that exists, the root always; files are the <see cref="Text"/> and
    /// <see cref="Binary"/> keys that start with <c>/</c>.</summary>
    public HashSet<string> Directories { get; } = ["/"];
```

After `AddDataset`, add:

```csharp
    /// <summary>Makes <paramref name="path"/> and every directory above it exist.</summary>
    public void AddDirectory(string path)
    {
        for (var current = HostPath.ForUnix(path); current is not null; current = current.Parent) Directories.Add(current.UnixPath!);
    }

    public void AddFile(string path, params string[] lines)
    {
        AddDirectory(HostPath.ForUnix(path).Parent!.UnixPath!);
        Text[path] = [.. lines];
    }

    public void AddBinaryFile(string path, byte[] bytes)
    {
        AddDirectory(HostPath.ForUnix(path).Parent!.UnixPath!);
        Binary[path] = bytes;
    }
```

Replace the two `NotSupportedException` stubs with:

```csharp
    public async Task<HostFileListing> ListDirectoryAsync(HostPath directory, HostListRequest request, CancellationToken cancellationToken = default)
    {
        if (directory.Kind != HostPathKind.Unix) throw new ArgumentException("Only a UNIX path names a directory.", nameof(directory));
        if (request.Continuation is not null) throw new ArgumentException("A directory listing cannot be continued.", nameof(request));
        await EnterAsync($"listdir:{directory}", $"listdir:{directory}", cancellationToken);
        try
        {
            lock (_lock)
            {
                var path = directory.UnixPath!;
                if (!Directories.Contains(path))
                {
                    throw Text.ContainsKey(path) || Binary.ContainsKey(path)
                        ? new HostFileException(HostFileErrorKind.InvalidRequest, $"{directory}: is a file, not a directory.")
                        : Missing(directory);
                }
                ListRequests.Add(request);
                var entries = Directories.Where(d => d != "/" && ParentOf(d) == path).Order(StringComparer.Ordinal)
                    .Select(d => new HostFileEntry(NameOf(d), HostFileEntryKind.Directory, Unix: new UnixFileAttributes(128, null)))
                    .Concat(Text.Keys.Concat(Binary.Keys).Distinct().Where(k => k.StartsWith('/') && ParentOf(k) == path).Order(StringComparer.Ordinal)
                        .Select(k => new HostFileEntry(NameOf(k), HostFileEntryKind.File, Unix: new UnixFileAttributes(SizeOf(k), null))))
                    .ToList();
                var truncated = request.MaxItems > 0 && entries.Count > request.MaxItems;
                if (truncated) entries.RemoveRange(request.MaxItems, entries.Count - request.MaxItems);
                return new HostFileListing(entries, null, truncated);
            }
        }
        finally { Leave(); }
    }

    public async Task CreateDirectoryAsync(HostPath directory, CancellationToken cancellationToken = default)
    {
        if (directory.Kind != HostPathKind.Unix) throw new ArgumentException("Only a UNIX path names a directory.", nameof(directory));
        await EnterAsync($"mkdir:{directory}", $"mkdir:{directory}", cancellationToken);
        try
        {
            lock (_lock)
            {
                var path = directory.UnixPath!;
                if (Directories.Contains(path) || Text.ContainsKey(path) || Binary.ContainsKey(path))
                    throw new HostFileException(HostFileErrorKind.AlreadyExists, $"{directory}: a file or directory of that name already exists.", 1, "File or directory already exists");
                if (!Directories.Contains(ParentOf(path))) throw Missing(directory.Parent!);
                Directories.Add(path);
            }
        }
        finally { Leave(); }
    }
```

In `WriteTextAsync` and `WriteBinaryAsync`, inside the `lock`, before `CheckStamp(path, ifMatch);`, add:

```csharp
                RequireParent(path);
```

In `RenameAsync`, as its first line:

```csharp
        if (from.Kind == HostPathKind.Unix) throw new ArgumentException("The host cannot rename a UNIX file.", nameof(from));
```

In `DeleteAsync`, replace the `lock` body with:

```csharp
            lock (_lock)
            {
                // Nothing to remove is the host's 404 (Missing: reason 5 for a member, 4 for a dataset, 1 for a path).
                if (path.Kind == HostPathKind.Unix)
                {
                    var unix = path.UnixPath!;
                    var prefix = unix == "/" ? "/" : unix + "/";
                    var wasDirectory = Directories.Remove(unix);
                    if (wasDirectory)
                    {
                        Directories.RemoveWhere(d => d.StartsWith(prefix, StringComparison.Ordinal));
                        foreach (var key in Text.Keys.Concat(Binary.Keys).Concat(Etags.Keys).Distinct().Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList()) Forget(key);
                    }
                    else if (!Forget(unix)) throw Missing(path);
                }
                else if (path.Member is { } member)
                {
                    var listed = Members.TryGetValue(path.Dataset!, out var names) && names.Remove(member);
                    var held = Forget(path.ToString());
                    if (!listed && !held) throw Missing(path);
                }
                else
                {
                    var listed = Datasets.RemoveAll(d => d.Name == path.Dataset) > 0;
                    listed |= Members.Remove(path.Dataset!);
                    var keys = KeysUnder(path.Dataset!);
                    foreach (var key in keys) Forget(key);
                    if (!listed && keys.Count == 0) throw Missing(path);
                }
            }
```

Replace `Missing` with:

```csharp
    private static HostFileException Missing(HostPath path) =>
        new(HostFileErrorKind.NotFound, $"{path}: not found.", path.Kind == HostPathKind.Unix ? 1 : path.Member is null ? 4 : 5);
```

and add, beside it:

```csharp
    /// <summary>A UNIX file can only be written under a directory that exists, as the host answers not found.</summary>
    private void RequireParent(HostPath path)
    {
        if (path.Kind == HostPathKind.Unix && !Directories.Contains(ParentOf(path.UnixPath!))) throw Missing(path.Parent!);
    }

    private static string ParentOf(string path)
    {
        var cut = path.LastIndexOf('/');
        return cut == 0 ? "/" : path[..cut];
    }

    private static string NameOf(string path) => path[(path.LastIndexOf('/') + 1)..];

    private long SizeOf(string key) => Text.TryGetValue(key, out var lines) ? lines.Sum(l => l.Length + 1) : Binary[key].Length;
```

`AddMember` is unaffected (`path.Member` is null on a UNIX path). In `AddMember` and everywhere else the fake reads `path.Dataset` in the dataset arms (`RenameMember`, `RenameDataset`, `KeysUnder` callers, `ListMembersAsync`), add `!` where the compiler asks; each such read is inside a branch that has already established the kind.

- [ ] **Step 4: Run the App tests**

Run: `dotnet test tests/LizTerm.App.Tests 2>&1 | grep -E "error CS|Passed!|Failed!|\[FAIL\]" | head -10`
Expected: `Passed!`, every test in the project (the existing browser tests never touch a UNIX path).

- [ ] **Step 5: Test notes**

In `tests/CLAUDE.md`, in the `FakeHostFileService` (`Fakes/`) bullet, after the sentence ending "throws `ArgumentException` before it is logged, as the backend does.", add:

```markdown
  It also plays the file system: `Directories` (the root always) and `AddDirectory`/`AddFile`/`AddBinaryFile`
  seed a tree, `listdir:<path>` lists a directory's subdirectories then its files (each in ordinal order, sizes
  from the content), cut to `MaxItems` with `Truncated` and never a continuation, `NotFound` for a missing path
  and `InvalidRequest` for a file; `mkdir:<path>` needs an existing parent and refuses an existing name as
  `AlreadyExists`; a write under a missing parent is `NotFound`; `delete:<path>` on a directory takes everything
  under it; and a rename of a UNIX path throws `ArgumentException` unlogged, as the backend does.
```

In the "Core tests" `FakeHostFileService` bullet, change "create and rename are logged only" to "create, rename, `listdir` and `mkdir` are logged only".

- [ ] **Step 6: Commit**

```bash
git add tests/LizTerm.App.Tests/Fakes/FakeHostFileService.cs tests/LizTerm.App.Tests/HostFiles/FakeHostFileServiceTests.cs tests/CLAUDE.md
git commit -m "App fake: a file system, for the USS tab's tests (#176)

Directories plus AddDirectory/AddFile/AddBinaryFile; listdir, mkdir,
reads, writes and a recursive delete on UNIX paths, failing the way
the host does.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 6: The live round trip

**Files:**
- Modify: `tests/LizTerm.Integration.Tests/LiveMvsmfTests.cs`, `docs/development.md`

**Interfaces:**
- Consumes: Task 4's backend; `LIZTERM_MVSMF_SCRATCH_DIR` (optional; default `/u/<user, lower case>`).

- [ ] **Step 1: Write the live test**

In `tests/LizTerm.Integration.Tests/LiveMvsmfTests.cs`, extend the class summary with "…and, under `LIZTERM_MVSMF_SCRATCH_DIR` (default `/u/<user>`), creates and removes a directory named `liztest-<hhmmss>`.", and add after `Creates_renames_and_deletes_a_scratch_dataset_with_etag_checks`:

```csharp
    /// <summary>Create, write, list, read, stamp and remove a scratch directory in the user's home; the directory is
    /// removed on the way out whatever happened.</summary>
    [Fact(Timeout = LiveTimeout)]
    public async Task Round_trips_a_scratch_unix_directory()
    {
        var live = Require();
        var ct = TestContext.Current.CancellationToken;
        using var service = Connect(live);
        var configured = Environment.GetEnvironmentVariable("LIZTERM_MVSMF_SCRATCH_DIR");
        var parent = HostPath.ForUnix(string.IsNullOrWhiteSpace(configured) ? $"/u/{live.Credentials.Userid.ToLowerInvariant()}" : configured);
        var scratch = parent.Child($"liztest-{DateTime.UtcNow.ToString("HHmmss", System.Globalization.CultureInfo.InvariantCulture)}");
        var big = Path.Combine(Path.GetTempPath(), $"lizitest-{Guid.NewGuid():N}.bin");
        try
        {
            await service.CreateDirectoryAsync(scratch, ct);
            var again = await Assert.ThrowsAsync<HostFileException>(() => service.CreateDirectoryAsync(scratch, ct));
            Assert.Equal(HostFileErrorKind.AlreadyExists, again.Kind);
            Assert.Contains((await service.ListDirectoryAsync(parent, HostListRequest.All, ct)).Entries,
                e => e.Name == scratch.Name && e.Kind == HostFileEntryKind.Directory);

            var text = scratch.Child("hello world#1.txt");
            var first = await service.WriteTextAsync(text, ["hello", "", "¬ end", "\tTABBED"], cancellationToken: ct);
            Assert.NotNull(first);
            var read = await service.ReadTextAsync(text, withEtag: true, cancellationToken: ct);
            Assert.Equal(new[] { "hello", "", "¬ end", "\tTABBED" }, read.Lines);
            Assert.Equal(first, read.Etag);
            Assert.Null((await service.ReadTextAsync(text, cancellationToken: ct)).Etag);

            var binary = scratch.Child("data.bin");
            var bytes = Enumerable.Range(0, 300).Select(i => (byte)i).ToArray();
            await service.WriteBinaryAsync(binary, new MemoryStream(bytes), cancellationToken: ct);
            using var back = new MemoryStream();
            var binaryRead = await service.ReadBinaryAsync(binary, back, withEtag: true, cancellationToken: ct);
            Assert.Equal(bytes, back.ToArray());
            Assert.Equal(300, binaryRead.Bytes);

            var listing = await service.ListDirectoryAsync(scratch, HostListRequest.All, ct);
            Assert.True(listing.IsComplete);
            Assert.Equal(new[] { "data.bin", "hello world#1.txt" }, listing.Entries.Select(e => e.Name).Order(StringComparer.Ordinal));
            Assert.All(listing.Entries, e => Assert.Equal(HostFileEntryKind.File, e.Kind));
            Assert.Equal(300, listing.Entries.Single(e => e.Name == "data.bin").Unix!.Size);
            Assert.NotNull(listing.Entries.Single(e => e.Name == "data.bin").Unix!.Modified);

            var second = await service.WriteTextAsync(text, ["changed"], ifMatch: read.Etag, ct);
            Assert.NotNull(second);
            Assert.NotEqual(first, second);
            var conflict = await Assert.ThrowsAsync<HostFileException>(() => service.WriteTextAsync(text, ["x"], ifMatch: first, ct));
            Assert.Equal(HostFileErrorKind.Conflict, conflict.Kind);
            Assert.Equal(new[] { "changed" }, (await service.ReadTextAsync(text, cancellationToken: ct)).Lines);

            var aFile = await Assert.ThrowsAsync<HostFileException>(() => service.ListDirectoryAsync(text, HostListRequest.All, ct));
            Assert.Equal(HostFileErrorKind.InvalidRequest, aFile.Kind);
            var missing = await Assert.ThrowsAsync<HostFileException>(() => service.ListDirectoryAsync(scratch.Child("nope"), HostListRequest.All, ct));
            Assert.Equal(HostFileErrorKind.NotFound, missing.Kind);
            var directory = await Assert.ThrowsAsync<HostFileException>(() => service.ReadTextAsync(scratch, cancellationToken: ct));
            Assert.Equal(HostFileErrorKind.InvalidRequest, directory.Kind);

            await File.WriteAllBytesAsync(big, new byte[HostFileLimits.MaxUnixFileBytes + 1], ct);
            Assert.Equal("The file is 65,537 bytes; the host holds at most 65,536.", HostFileTransfer.BinaryUploadProblem(big, HostFileLimits.MaxUnixFileBytes));

            await service.DeleteAsync(scratch, ct);
            Assert.DoesNotContain((await service.ListDirectoryAsync(parent, HostListRequest.All, ct)).Entries, e => e.Name == scratch.Name);
            var gone = await Assert.ThrowsAsync<HostFileException>(() => service.DeleteAsync(scratch, ct));
            Assert.Equal(HostFileErrorKind.NotFound, gone.Kind);
        }
        finally
        {
            File.Delete(big);
            try
            {
                await service.DeleteAsync(scratch, CancellationToken.None);
            }
            catch (HostFileException)
            {
                // Not there, which is the expected case.
            }
        }
    }
```

- [ ] **Step 2: Run it against the host**

With the environment set (Prerequisites), in one shell:

```bash
dotnet test tests/LizTerm.Integration.Tests --filter "FullyQualifiedName~LiveMvsmfTests" 2>&1 | grep -E "Passed!|Failed!|\[FAIL\]|error" | head -10
```

Expected: `Passed!` with 8 tests (the seven that existed and this one). If the new test fails on the `hello world#1.txt` round trip with a 404, the host does not percent-decode the query or the path as `EscapeUnixPath` assumes: stop and report the failing request line, since that becomes a compatibility entry and a change to the escaping, not something to paper over. Whatever happens, confirm the host is clean:

```bash
curl -sS --netrc-file ~/.mvsmf-netrc "$LIZTERM_MVSMF_URL/restfiles/fs?path=/u/ibmuser" | grep -c liztest
```

Expected: `0`.

- [ ] **Step 3: Document the variable**

In `docs/development.md`, in the environment-variable table after the `LIZTERM_MVSMF_SCRATCH_PDS` row, add:

```markdown
| `LIZTERM_MVSMF_SCRATCH_DIR` | Optional: a UNIX directory the live mvsMF tests may create `liztest-<hhmmss>` under and delete it from. Defaults to `/u/<LIZTERM_MVSMF_USER in lower case>`. |
```

and in "Live mvsMF tests", after "rename the member and the dataset, and delete it;", insert "create a directory under `LIZTERM_MVSMF_SCRATCH_DIR`, write, list, read and stamp a text and a binary file in it, and remove it;".

- [ ] **Step 4: Commit**

```bash
git add tests/LizTerm.Integration.Tests/LiveMvsmfTests.cs docs/development.md
git commit -m "Live mvsMF lane: a UNIX directory round trip (#176)

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 7: The compatibility log

**Files:**
- Modify: `docs/mvsmf-compatibility.md`

**Interfaces:**
- Consumes: every `// mvsMF-compat:` tag Tasks 2 and 4 added: `uss-limits`, `uss-list-no-continuation`, `uss-create-errors-400`, `uss-stat-for-file-path`.

- [ ] **Step 1: The tested-build table**

In the "Tested build" table, change the Host row to `| Host | MVS/CE, HTTPD, probed 2026-09-18; the manage operations recorded 2026-09-19; the file system routes probed and recorded 2026-09-22 |`.

- [ ] **Step 2: Replace the `uss-limits` entry**

Replace the whole `### \`uss-limits\` (log only, for later)` entry with:

```markdown
### `uss-limits`

- **Source:** `docs/endpoints/uss/put.md` and `CHANGELOG.md`: a UNIX file is capped at 64 KB by UFSD's
  direct-block layout; text is IBM-1047 on disk.
- **Observed (2026-09-22):** a `PUT` of 70,000 bytes answers 500 `{"category":10,"reason":1,"message":"Incomplete
  write to file"}` **after storing what fit** (`uss-write-too-large`; the listing afterwards shows the fragment),
  so the host's answer comes too late to prevent a partial file. The text translation changes nothing on the
  wire: the body is Latin-1 both ways, as on the dataset routes (`text-body-is-latin1`).
- **LizTerm:** `HostFileLimits.MaxUnixFileBytes` (65,536) is checked before any upload request:
  `TextUploadCheck.RunForUnixFile` counts the lines' Latin-1 bytes plus one per line ending, and
  `HostFileTransfer.BinaryUploadProblem` the file's length; a file over the cap is refused with its size and the
  limit, and nothing is sent. Raise the constant when UFSD grows.
```

- [ ] **Step 3: Add the four new entries**

After the `uss-limits` entry, add:

```markdown
### `uss-list-no-continuation`

- **Docs and source:** `GET restfiles/fs?path=` takes `X-IBM-Max-Items` (default 1,000; `0` = unlimited) and
  answers `moreRows: true` when it cut the list, but `ussListHandler` reads no `start=`: the route cannot be
  continued, unlike the dataset and member listings (`paging`).
- **Observed (2026-09-22):** `X-IBM-Max-Items: 1` on `/u` answers one of three with `moreRows: true`
  (`uss-list-truncated`); the same request with `start=ibmuser` answers the same page.
- **LizTerm:** `ListDirectoryAsync` always sends `X-IBM-Max-Items` (`0` for the whole directory, which is what
  mvsMF Access asks for), refuses a `HostListRequest` carrying a continuation, and reports a cut listing as
  `HostFileListing.Truncated` with no continuation, so the window says "more on the host" and offers no Load more.

### `uss-stat-for-file-path`

- **Docs and source:** a listing whose `path` names a file answers 200 with one item whose `name` is the full path,
  the stat shape real z/OSMF uses.
- **Observed (2026-09-22):** `uss-stat-file`.
- **LizTerm:** `ListDirectoryAsync` reads a one-item answer whose name starts with `/` as "is a file, not a
  directory" (`HostFileErrorKind.InvalidRequest`), since a caller listing a directory never wants a stat.

### `uss-create-errors-400`

- **Docs and source:** `POST restfiles/fs/{path}` with `{"type":"directory"}` answers 400 for an existing name,
  for a missing parent and for a path too long, all category 2 reason 1 in the docs; the source sends the existing
  name as category 4 (the security category, reused), reason 1, "File or directory already exists".
- **Observed (2026-09-22):** `uss-mkdir-exists` is 400 category 4 reason 1; a missing parent
  (`uss-mkdir-no-parent`) is <status and body as recorded in Task 3>.
- **LizTerm:** a 400 whose message says "already exists" is `AlreadyExists`, whose sentence for a file system path
  reads "a file or directory of that name already exists"; the missing parent is mapped by its status as the
  host sent it.

### `uss-owner-blank` (log only)

- **Docs:** each listing item carries `user` and `group`, the owner and group names.
- **Observed (2026-09-22):** both are `""` on every entry of `/`, `/u/mvsce01` and `/u/mvsce02`, and on `/tmp`
  and `/www`; only `/u/ibmuser` carries `IBMUSER` and `USER`.
- **LizTerm:** neither field is read or shown; the file pane has no owner or permissions column.
```

Fill the `uss-mkdir-no-parent` sentence from Task 3's recording, and in the `text-body-is-latin1` entry's **LizTerm** line add the sentence "The file system routes behave the same, and go through the same code."

- [ ] **Step 4: Check every tag has an entry, a code comment and a test**

```bash
for tag in uss-limits uss-list-no-continuation uss-create-errors-400 uss-stat-for-file-path; do
  printf '%s: code %s, doc %s\n' "$tag" \
    "$(grep -rl "mvsMF-compat: $tag" src | wc -l | tr -d ' ')" \
    "$(grep -c "^### \`$tag\`" docs/mvsmf-compatibility.md)"
done
grep -c 'public async Task Uss_limits_so_\|public async Task Uss_list_no_continuation_so_\|public async Task Uss_stat_for_file_path_so_\|public async Task Uss_create_errors_400_so_' tests/LizTerm.Backend.Mvsmf.Tests/MvsmfUnixTests.cs
grep -c 'public void Uss_create_errors_400_so_' tests/LizTerm.Backend.Mvsmf.Tests/MvsmfErrorsTests.cs
```

Expected: every tag line shows `code 1` or more and `doc 1`; then `4`, then `1`. (`uss-limits`'s code tag sits above `Route` in `MvsmfFileService.cs`, since Core never names mvsMF.)

- [ ] **Step 5: Commit**

```bash
git add docs/mvsmf-compatibility.md src/LizTerm.Backend.Mvsmf/MvsmfFileService.cs
git commit -m "Compatibility log: the file system routes (#176)

uss-limits becomes a code entry; uss-list-no-continuation,
uss-stat-for-file-path, uss-create-errors-400 and the log-only
uss-owner-blank are new.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 8: Full verification and the pull request

**Files:** none new.

- [ ] **Step 1: Headers, warnings, the suite**

```bash
dotnet build LizTerm.slnx --no-incremental 2>&1 | grep -c " warning "
dotnet test LizTerm.slnx 2>&1 | grep -E "Passed!|Failed!|error" | head -12
git status --short
```

Expected: `0`; every project `Passed!` (Core.Tests includes `RepositoryHeadersTests`, so a missing licence header fails here); a clean tree.

- [ ] **Step 2: The live lane once more on the final tree**

With the environment set (Prerequisites):

```bash
dotnet test tests/LizTerm.Integration.Tests --filter "FullyQualifiedName~LiveMvsmfTests" 2>&1 | grep -E "Passed!|Failed!" | head -3
curl -sS --netrc-file ~/.mvsmf-netrc "$LIZTERM_MVSMF_URL/restfiles/fs?path=/u/ibmuser" | grep -c liztest
```

Expected: `Passed!` (8 tests), then `0`.

- [ ] **Step 3: Review the branch as a whole**

```bash
git log --oneline main..HEAD
git diff main..HEAD --stat
git log main..HEAD --format='%h %(trailers:key=Co-Authored-By)'
```

Expected: the spec, the plan and seven task commits, each with the `Claude Fable 5.1` trailer. Read `git diff main..HEAD -- src/LizTerm.Core` once through for any mention of mvsMF, Avalonia or b3270 (there must be none).

- [ ] **Step 4: Push and open the PR**

```bash
git push -u origin claude/uss-mvsmf-access-support-342b1a
gh pr create --base main --title "mvsMF USS support, PR 1: Core and backend (#176)" --body "$(cat <<'EOF'
Part of #176. The Core contract and the mvsMF backend for the host's UNIX file system; the USS tab itself is PR 2.

- `HostPath` gains `HostPathKind.Unix` (`ForUnix`, `UnixPathError`, `Parent`, `Name`, `Child`); `TryParse` reads a leading `/`. `Dataset` is null on a UNIX path.
- `HostFileEntry` gains `Directory` and `File` kinds with `UnixFileAttributes` (size, modified); `HostFileListing.Truncated` reports a listing the host cut short with nothing to continue it.
- `IHostFileService` gains `ListDirectoryAsync` and `CreateDirectoryAsync`; the read, write and delete verbs take a UNIX path; the dataset verbs and `RenameAsync` refuse one before any request (mvsMF has no USS rename).
- The 64 KB cap is `HostFileLimits.MaxUnixFileBytes`, enforced before a request by `TextUploadCheck.RunForUnixFile` and `HostFileTransfer.BinaryUploadProblem`, since the host writes what fits and only then answers 500.
- `MvsmfFileService` routes UNIX paths to `restfiles/fs/<escaped path>`, lists with `X-IBM-Max-Items` always sent, creates with a JSON `POST`, deletes with `X-IBM-Option: recursive`.
- Eighteen fixtures recorded from MVS/CE (mvsMF 1.1.0); compat entries `uss-limits` (now a code entry), `uss-list-no-continuation`, `uss-stat-for-file-path`, `uss-create-errors-400`, `uss-owner-blank`.
- Live lane: a scratch-directory round trip under `LIZTERM_MVSMF_SCRATCH_DIR` (default `/u/<user>`), 8/8 green against MVS/CE, host left clean.
- Both fakes play the file system; the App's behaviour is unchanged.

Spec: `docs/superpowers/specs/2026-09-22-mvsmf-uss-tab-design.md`. Plan: `docs/superpowers/plans/2026-09-22-mvsmf-uss-backend.md`.

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
```

Expected: the PR URL. If the create fails with a 5xx on the body (this happened on 2026-09-13), create it with a one-line body and set the description with `gh api -X PATCH repos/coffeemuse/LizTerm/issues/<n> -f body=@<file>`.

- [ ] **Step 5: Report**

Tell Robert: the PR URL, the live-lane result, the `uss-mkdir-no-parent` status the host actually gave (it decides one sentence in the spec's §4.1), anything the fixtures showed that differs from the spec's §2 table, and that PR 2 (the tab) plans from this branch once it merges.
