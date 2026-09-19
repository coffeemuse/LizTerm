# mvsMF manage slice, PR 1 (Core and backend) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give `IHostFileService` create, rename and dataset delete, and carry the host's ETag through reads and writes, implemented and tested for mvsMF, so PR 2 can build the browser's manage operations and conflict detection on it.

**Architecture:** Core gains a `DatasetAllocation` record with its own local rules, `HostTextRead` / `HostBinaryRead` results carrying the stamp, three error kinds (`Conflict`, `AlreadyExists`, `CannotAllocate`), and three operations on the interface. `MvsmfFileService` posts the allocation as JSON, sends the one JSON `PUT` that is a rename, deletes a dataset path, asks for `ETag` on every read and write and sends `If-Match` when given. `MvsmfErrors` classifies the three new answers. The App's fake host learns the same, and the App compiles against the new return types with its behaviour unchanged. This is PR 1 of the manage slice; PR 2 is the browser.

**Tech Stack:** .NET 10, C# latest, `System.Net.Http`, `System.Text.Json` source generation, xunit.v3, curl, `gh`.

**Spec:** `docs/superpowers/specs/2026-09-19-mvsmf-manage-slice-design.md` (read §2, §3, §4.1, §4.2, §4.3, §6 and §8 before starting; §4.4, §4.5, §5 and §7 are PR 2).

## Global Constraints

- Every hand-written `.cs`, `.axaml` and `.sh` file starts with the three licence lines (after the shebang in a script, before the root element in `.axaml`): `This file is part of LizTerm.` / `Copyright 2026 by CoffeeMuse` / `SPDX-License-Identifier: BSD-3-Clause`. A new file needs them; `RepositoryHeadersTests` fails the suite for a missing one.
- **Dependency rule.** `LizTerm.Core` depends on the BCL only and never names mvsMF, Avalonia or b3270, not even in comments. `LizTerm.Backend.Mvsmf` depends on Core only and is the only project that knows mvsMF exists. `LizTerm.App` names the mvsMF backend only in `src/LizTerm.App/HostFileServiceFactory.cs`; every other App file and every App test talks to `IHostFileService`.
- **Never send `Content-Type: application/json` on a write.** mvsMF treats that `PUT` as a rename. After this PR, `RenameAsync` is the one method that sends it, on purpose (compat tag `put-json-is-rename`).
- **The stamp is opaque.** LizTerm echoes an `ETag` value back as `If-Match` as the host gave it, and never parses or compares it beyond string equality.
- **Every mvsMF workaround or recorded behaviour carries `// mvsMF-compat: <tag>`** matching an entry in `docs/mvsmf-compatibility.md`, and a test named after the tag pins it.
- Fixtures are recorded exchanges, binary-exact; only `tools/record-mvsmf-fixture.sh` writes them, and `FixtureTests` rejects a header block holding a credential or an unredacted token.
- Zero warnings: `dotnet build LizTerm.slnx --no-incremental 2>&1 | grep -c " warning "` prints `0`.
- Commits end with `Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>`.

## Prerequisites

- Work on the branch `claude/manage-slice-rename-delete-create-7a2f96` (this worktree is on it, at `9554be7` over `main` at `4210b53`). The spec and this plan are committed to it.
- Tasks 2 and 5 need the live host, mvsMF 1.1.0 at `http://10.42.37.209:8080/zosmf`, credentials in `~/.mvsmf-netrc` (user `IBMUSER`). Set the four variables in the recording/live shell without echoing the password:

  ```bash
  export LIZTERM_MVSMF_URL=http://10.42.37.209:8080/zosmf
  export LIZTERM_MVSMF_USER=IBMUSER
  export LIZTERM_MVSMF_PASSWORD=$(awk '{print $6}' ~/.mvsmf-netrc)
  export LIZTERM_MVSMF_SCRATCH_PDS=MVSCE02.CNTL
  ```

  If `~/.mvsmf-netrc` is missing, stop and ask.
- The local mvsMF source is at `/Users/robert/mvslovers/mvsmf` (commit `cf4d6d5`, 1.1.1-dev). `docs/endpoints/datasets/{create,delete,put,members-put,members-get}.md` and `src/dsapi.c` (`process_rename` near line 3175, `datasetCreateHandler` near line 3300) document what Task 3 maps.

## What the host does (source `cf4d6d5`; recorded in Task 2)

| Request | Response |
|---|---|
| `POST restfiles/ds/{name}` with `{"dsorg":"PO","recfm":"FB","lrecl":80,"blksize":3120,"alcunit":"TRK","primary":1,"secondary":1,"dirblk":2}` | 201 |
| the same again (the name exists), no space, a bad DCB, or no authority | 500 `{"category":8,"rc":900,"reason":7,"message":"Dynamic allocation Error"}` |
| `POST` with a field missing and no `like` | 400 category 6 reason 3 "Invalid or missing allocation parameters" |
| `DELETE restfiles/ds/{name}` | 204; 404 category 6 reason 4 when not cataloged; 500 category 6 reason 11 when the scratch failed |
| `PUT restfiles/ds/{dsn}({new})`, `Content-Type: application/json`, `{"request":"rename","from-dataset":{"dsn":"{dsn}","member":"{old}"}}` | 204; 404 reason 5 when the old member is missing; 400 category 6 reason 7 "Rename target already exists"; 500 reason 8 otherwise |
| `PUT restfiles/ds/{new}`, the same content type, `{"request":"rename","from-dataset":{"dsn":"{old}"}}` | 204; 404 reason 4 when the old name is not cataloged; 500 reason 8 otherwise, including a new name that exists |
| `GET` with `X-IBM-Return-Etag: true` | 200 with `ETag: <16 hex digits>`, unquoted; no `ETag` without the header |
| `PUT` with `X-IBM-Return-Etag: true` | 204 with the `ETag` of the member as written |
| `PUT` with a stale `If-Match` | 412 `{"category":6,"rc":8,"reason":10,"message":"The resource was modified since the supplied ETag was created"}`, nothing written |

## File Structure

| File | Responsibility |
|---|---|
| `src/LizTerm.Core/HostFiles/DatasetAllocation.cs` | **New.** `DatasetOrganization`, `SpaceUnit`, `AllocationField`, `DatasetAllocation` with `Problems()` and `RecfmError` |
| `src/LizTerm.Core/HostFiles/HostRead.cs` | **New.** `HostTextRead`, `HostBinaryRead` |
| `src/LizTerm.Core/HostFiles/HostFileException.cs` | `Conflict`, `AlreadyExists`, `CannotAllocate` |
| `src/LizTerm.Core/HostFiles/IHostFileService.cs` | `CreateDatasetAsync`, `RenameAsync`, dataset `DeleteAsync`, stamp-carrying reads and writes |
| `src/LizTerm.Core/HostFiles/HostFileTransfer.cs` | `DownloadResult`, `UploadOutcome.Etag`, `ifMatch` on the uploads |
| `src/LizTerm.Core/CLAUDE.md` | the allocation rules and the stamp |
| `src/LizTerm.Backend.Mvsmf/MvsmfJson.cs` | `MvsmfAllocation`, `MvsmfRename`, `MvsmfRenameSource` |
| `src/LizTerm.Backend.Mvsmf/MvsmfErrors.cs` | the three new classifications and their sentences |
| `src/LizTerm.Backend.Mvsmf/MvsmfFileService.cs` | the three operations, `ETag` on reads and writes, `If-Match` |
| `src/LizTerm.Backend.Mvsmf/CLAUDE.md` | the rename content type, the stamp |
| `src/LizTerm.App/ViewModels/MvsmfBrowserViewModel.{Downloads,Uploads}.cs` | compile against the new return types; behaviour unchanged |
| `tests/LizTerm.Core.Tests/HostFiles/{DatasetAllocationTests,HostFileTransferTests,FakeHostFileService}.cs` | Core rules, the stamp through the transfer, the Core fake |
| `tests/LizTerm.Backend.Mvsmf.Tests/Fixtures/*.http` + `README.md` | the eleven recordings |
| `tests/LizTerm.Backend.Mvsmf.Tests/{MvsmfCreateTests,MvsmfRenameTests,MvsmfDeleteTests,MvsmfEtagTests,MvsmfErrorsTests,MvsmfWriteTests}.cs` | backend tests |
| `tests/LizTerm.App.Tests/Fakes/FakeHostFileService.cs`, `tests/LizTerm.App.Tests/HostFiles/FakeHostFileServiceTests.cs`, `tests/CLAUDE.md` | the App fake |
| `tests/LizTerm.Integration.Tests/LiveMvsmfTests.cs`, `docs/development.md` | the live round trip |
| `docs/mvsmf-compatibility.md` | `etag`, `create-failure-is-one-500`, `rename-target-exists-400`, `put-json-is-rename` |

## Task graph and the red boundary

Task 1 changes the interface's read and write signatures, so after it **only `LizTerm.Core` and `LizTerm.Core.Tests` build**; the backend, the App and their tests do not compile again until Task 3 (backend) and Task 4 (App and its fake). Task 2 records fixtures and touches no code. From Task 4 on, every task keeps the whole solution green. Each task's verification step names the project it can build.

---

### Task 1: Core — the allocation record, the stamp, the three operations

**Files:**
- Create: `src/LizTerm.Core/HostFiles/DatasetAllocation.cs`
- Create: `src/LizTerm.Core/HostFiles/HostRead.cs`
- Create: `tests/LizTerm.Core.Tests/HostFiles/DatasetAllocationTests.cs`
- Modify: `src/LizTerm.Core/HostFiles/HostFileException.cs`
- Modify: `src/LizTerm.Core/HostFiles/IHostFileService.cs`
- Modify: `src/LizTerm.Core/HostFiles/HostFileTransfer.cs`
- Modify: `tests/LizTerm.Core.Tests/HostFiles/FakeHostFileService.cs`
- Modify: `tests/LizTerm.Core.Tests/HostFiles/HostFileTransferTests.cs`
- Modify: `src/LizTerm.Core/CLAUDE.md`

**Interfaces:**
- Produces, in `LizTerm.Core.HostFiles`:
  - `enum DatasetOrganization { Sequential, Partitioned }`, `enum SpaceUnit { Tracks, Cylinders }`, `enum AllocationField { Recfm, Lrecl, Blksize, Primary, Secondary, DirectoryBlocks }`.
  - `sealed record DatasetAllocation(DatasetOrganization Organization, string Recfm, int Lrecl, int Blksize, SpaceUnit Unit, int Primary, int Secondary, int DirectoryBlocks)` with `string FoldedRecfm`, `bool IsUndefinedLength`, `IReadOnlyDictionary<AllocationField, string> Problems()`, `bool IsValid`, `static string? RecfmError(string recfm)`, `const int MaxRecordLength = 32760`.
  - `sealed record HostTextRead(IReadOnlyList<string> Lines, string? Etag)`, `sealed record HostBinaryRead(long Bytes, string? Etag)`.
  - `HostFileErrorKind.Conflict`, `.AlreadyExists`, `.CannotAllocate`.
  - `IHostFileService`: `Task CreateDatasetAsync(HostPath dataset, DatasetAllocation allocation, CancellationToken ct = default)`; `Task RenameAsync(HostPath from, string newName, CancellationToken ct = default)`; `Task DeleteAsync(HostPath path, CancellationToken ct = default)` (member or dataset); `Task<HostTextRead> ReadTextAsync(HostPath, IProgress<long>?, CancellationToken)`; `Task<HostBinaryRead> ReadBinaryAsync(HostPath, Stream, IProgress<long>?, CancellationToken)`; `Task<string?> WriteTextAsync(HostPath, IReadOnlyList<string>, string? ifMatch = null, CancellationToken ct = default)`; `Task<string?> WriteBinaryAsync(HostPath, Stream, string? ifMatch = null, CancellationToken ct = default)`.
  - `HostFileTransfer`: `sealed record DownloadResult(long BytesWritten, string? Etag)`; `Task<DownloadResult> DownloadAsync(...)` (same parameters as today); `sealed record UploadOutcome(UploadVerification Verification, int? DiffersAtLine, string? Etag)` (the static `NotChecked` and `Matches` instances are removed); `Task<UploadOutcome> UploadTextAsync(IHostFileService service, HostPath path, TextUploadResult checkedText, bool verify, string? ifMatch = null, CancellationToken cancellationToken = default)`; `Task<string?> UploadBinaryAsync(IHostFileService service, HostPath path, string sourceFile, string? ifMatch = null, CancellationToken cancellationToken = default)`.

- [ ] **Step 1: Write the failing allocation tests**

Create `tests/LizTerm.Core.Tests/HostFiles/DatasetAllocationTests.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.Core.HostFiles;

namespace LizTerm.Core.Tests.HostFiles;

public class DatasetAllocationTests
{
    private static readonly DatasetAllocation Pds = new(DatasetOrganization.Partitioned, "FB", 80, 3120, SpaceUnit.Tracks, 5, 5, 20);

    [Fact]
    public void A_sound_allocation_has_no_problems()
    {
        Assert.Empty(Pds.Problems());
        Assert.True(Pds.IsValid);
        Assert.Empty((Pds with { Organization = DatasetOrganization.Sequential, DirectoryBlocks = 0 }).Problems());
        Assert.Empty((Pds with { Recfm = " u ", Lrecl = 0, Blksize = 19069 }).Problems());
    }

    [Theory]
    [InlineData("FB", null)]
    [InlineData("fba", null)]
    [InlineData("VBS", null)]
    [InlineData("U", null)]
    [InlineData("", "Enter a record format.")]
    [InlineData("D", "A record format starts with F, V or U.")]
    [InlineData("FX", "A record format cannot contain 'X'.")]
    [InlineData("FBB", "A record format cannot repeat 'B'.")]
    [InlineData("UBSAMB", "A record format cannot repeat 'B'.")]
    public void Recfm_is_a_first_letter_then_modifiers_at_most_once(string recfm, string? expected) =>
        Assert.Equal(expected, DatasetAllocation.RecfmError(recfm));

    [Fact]
    public void Recfm_is_folded()
    {
        Assert.Equal("FBA", (Pds with { Recfm = " fba " }).FoldedRecfm);
        Assert.True((Pds with { Recfm = "u" }).IsUndefinedLength);
        Assert.False(Pds.IsUndefinedLength);
    }

    [Theory]
    [InlineData("FB", 0, AllocationField.Lrecl, "LRECL must be between 1 and 32760.")]
    [InlineData("FB", 32761, AllocationField.Lrecl, "LRECL must be between 1 and 32760.")]
    [InlineData("U", -1, AllocationField.Lrecl, "LRECL must be between 0 and 32760 for undefined-length records.")]
    public void Lrecl_has_a_range_that_depends_on_the_format(string recfm, int lrecl, AllocationField field, string expected) =>
        Assert.Equal(expected, (Pds with { Recfm = recfm, Lrecl = lrecl }).Problems()[field]);

    [Fact]
    public void Each_field_reports_its_own_problem()
    {
        var problems = (Pds with { Recfm = "Q", Blksize = 0, Primary = 0, Secondary = -1, DirectoryBlocks = 0 }).Problems();

        Assert.Equal("A record format starts with F, V or U.", problems[AllocationField.Recfm]);
        Assert.Equal("BLKSIZE must be between 1 and 32760.", problems[AllocationField.Blksize]);
        Assert.Equal("Primary space must be at least 1.", problems[AllocationField.Primary]);
        Assert.Equal("Secondary space cannot be negative.", problems[AllocationField.Secondary]);
        Assert.Equal("A partitioned dataset needs at least 1 directory block.", problems[AllocationField.DirectoryBlocks]);
        Assert.Equal(5, problems.Count);
    }

    [Fact]
    public void Directory_blocks_are_ignored_for_a_sequential_dataset() =>
        Assert.Empty((Pds with { Organization = DatasetOrganization.Sequential, DirectoryBlocks = -3 }).Problems());
}
```

- [ ] **Step 2: Run it to see it fail**

Run: `dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~DatasetAllocationTests" 2>&1 | grep -E "error|Passed!|Failed!" | head -5`
Expected: build errors naming `DatasetAllocation`.

- [ ] **Step 3: Write the allocation record**

Create `src/LizTerm.Core/HostFiles/DatasetAllocation.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

namespace LizTerm.Core.HostFiles;

public enum DatasetOrganization { Sequential, Partitioned }

public enum SpaceUnit { Tracks, Cylinders }

/// <summary>The fields of a <see cref="DatasetAllocation"/> that can fail its local rules, so a form can mark each.</summary>
public enum AllocationField { Recfm, Lrecl, Blksize, Primary, Secondary, DirectoryBlocks }

/// <summary>What <see cref="IHostFileService.CreateDatasetAsync"/> sends. <see cref="Problems"/> holds the local
/// rules: the ranges a host cannot take at all. The DCB combinations a host may still refuse (a block size that
/// is not a multiple of a fixed record length, say) are the host's to judge.</summary>
public sealed record DatasetAllocation(
    DatasetOrganization Organization,
    string Recfm,
    int Lrecl,
    int Blksize,
    SpaceUnit Unit,
    int Primary,
    int Secondary,
    int DirectoryBlocks)
{
    public const int MaxRecordLength = 32760;

    /// <summary>The record format as it is sent: trimmed, upper case.</summary>
    public string FoldedRecfm => Recfm.Trim().ToUpperInvariant();

    /// <summary>RECFM=U, whose LRECL may be 0.</summary>
    public bool IsUndefinedLength => FoldedRecfm.StartsWith('U');

    public bool IsValid => Problems().Count == 0;

    /// <summary>One sentence per field that fails a local rule; empty when every field passes.</summary>
    public IReadOnlyDictionary<AllocationField, string> Problems()
    {
        var problems = new Dictionary<AllocationField, string>();
        if (RecfmError(Recfm) is { } recfm) problems[AllocationField.Recfm] = recfm;
        if (IsUndefinedLength)
        {
            if (Lrecl < 0 || Lrecl > MaxRecordLength)
                problems[AllocationField.Lrecl] = $"LRECL must be between 0 and {MaxRecordLength} for undefined-length records.";
        }
        else if (Lrecl < 1 || Lrecl > MaxRecordLength)
        {
            problems[AllocationField.Lrecl] = $"LRECL must be between 1 and {MaxRecordLength}.";
        }
        if (Blksize < 1 || Blksize > MaxRecordLength) problems[AllocationField.Blksize] = $"BLKSIZE must be between 1 and {MaxRecordLength}.";
        if (Primary < 1) problems[AllocationField.Primary] = "Primary space must be at least 1.";
        if (Secondary < 0) problems[AllocationField.Secondary] = "Secondary space cannot be negative.";
        if (Organization == DatasetOrganization.Partitioned && DirectoryBlocks < 1)
            problems[AllocationField.DirectoryBlocks] = "A partitioned dataset needs at least 1 directory block.";
        return problems;
    }

    /// <summary>Why <paramref name="recfm"/> is not a record format, or null: a first letter of F, V or U, then any
    /// of B, S, A and M, each at most once.</summary>
    public static string? RecfmError(string recfm)
    {
        var folded = (recfm ?? "").Trim().ToUpperInvariant();
        if (folded.Length == 0) return "Enter a record format.";
        if (folded[0] is not ('F' or 'V' or 'U')) return "A record format starts with F, V or U.";
        var seen = new HashSet<char>();
        foreach (var c in folded.AsSpan(1))
        {
            if (c is not ('B' or 'S' or 'A' or 'M')) return $"A record format cannot contain '{c}'.";
            if (!seen.Add(c)) return $"A record format cannot repeat '{c}'.";
        }
        return null;
    }
}
```

- [ ] **Step 4: Run the allocation tests**

Run: `dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~DatasetAllocationTests" 2>&1 | grep -E "Passed!|Failed!"`
Expected: `Passed!` (7 tests: 1 + 9 rows + 1 + 3 rows + 1 + 1 = 16 cases).

- [ ] **Step 5: Add the read results and the error kinds**

Create `src/LizTerm.Core/HostFiles/HostRead.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

namespace LizTerm.Core.HostFiles;

/// <summary>A text read: the records as lines, and the host's stamp of the content (<paramref name="Etag"/>), null
/// when the host sent none. The stamp is opaque: it is only ever handed back as the <c>ifMatch</c> of a later
/// write, so a write happens only if the content is still what was read.</summary>
public sealed record HostTextRead(IReadOnlyList<string> Lines, string? Etag);

/// <summary>A binary read: the bytes copied, and the host's stamp, as for <see cref="HostTextRead"/>.</summary>
public sealed record HostBinaryRead(long Bytes, string? Etag);
```

In `src/LizTerm.Core/HostFiles/HostFileException.cs`, add three members to `HostFileErrorKind` after `ServerError`:

```csharp
    /// <summary>No connection, a dropped connection, or no data for too long.</summary>
    Unreachable,
    /// <summary>A write's <c>ifMatch</c> no longer matched: the target changed since it was read. Nothing was
    /// written.</summary>
    Conflict,
    /// <summary>A rename onto a member that already exists.</summary>
    AlreadyExists,
    /// <summary>A create the host could not allocate: the name may exist, there may be no space, or the caller
    /// may not be authorized. Some hosts cannot say which.</summary>
    CannotAllocate,
```

- [ ] **Step 6: Change the interface**

Replace the read, write and delete members of `src/LizTerm.Core/HostFiles/IHostFileService.cs` (from `ReadTextAsync` through `DeleteAsync`) with:

```csharp
    /// <summary>The records as lines, trailing blanks as the host sent them, with the host's stamp of the content
    /// when it gives one. <paramref name="progress"/> reports bytes received.</summary>
    Task<HostTextRead> ReadTextAsync(HostPath path, IProgress<long>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>Copies the record bytes to <paramref name="destination"/>; returns the byte count and the stamp.</summary>
    Task<HostBinaryRead> ReadBinaryAsync(HostPath path, Stream destination, IProgress<long>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>Replaces the dataset's or member's records, creating a member that does not exist. Every line must
    /// already have passed <see cref="TextUploadCheck"/>. With <paramref name="ifMatch"/>, the stamp an earlier
    /// read or write returned, the write happens only while the target still holds that content; otherwise
    /// <see cref="HostFileErrorKind.Conflict"/> and nothing is written. Returns the stamp of the target as written,
    /// null when the host gives none. A failure without a conflict may leave the target partly written.</summary>
    Task<string?> WriteTextAsync(HostPath path, IReadOnlyList<string> lines, string? ifMatch = null, CancellationToken cancellationToken = default);

    /// <summary>As <see cref="WriteTextAsync"/>, for bytes.</summary>
    Task<string?> WriteBinaryAsync(HostPath path, Stream source, string? ifMatch = null, CancellationToken cancellationToken = default);

    /// <summary>Allocates a new dataset. <paramref name="allocation"/> must pass its own <see cref="DatasetAllocation.Problems"/>.
    /// A name the host could not allocate, for whatever reason it can name, is <see cref="HostFileErrorKind.CannotAllocate"/>.</summary>
    Task CreateDatasetAsync(HostPath dataset, DatasetAllocation allocation, CancellationToken cancellationToken = default);

    /// <summary>Renames a member within its library (<paramref name="newName"/> is the new member name) or a
    /// dataset (<paramref name="newName"/> is the new dataset name). A missing source is
    /// <see cref="HostFileErrorKind.NotFound"/>; a member name already in use is <see cref="HostFileErrorKind.AlreadyExists"/>.</summary>
    /// <exception cref="ArgumentException"><paramref name="newName"/> fails the naming rules.</exception>
    Task RenameAsync(HostPath from, string newName, CancellationToken cancellationToken = default);

    /// <summary>Deletes a member, or a whole dataset with everything in it.</summary>
    Task DeleteAsync(HostPath path, CancellationToken cancellationToken = default);
```

- [ ] **Step 7: Write the failing transfer tests**

In `tests/LizTerm.Core.Tests/HostFiles/HostFileTransferTests.cs`, change the four asserts that compare a whole outcome or a bare count:

- In `A_text_download_trims_trailing_blanks_and_uses_the_line_ending_asked_for`: `Assert.Equal(24, written);` → `Assert.Equal(24, written.BytesWritten);`
- In `A_binary_download_copies_the_bytes`: `Assert.Equal(3, written);` → `Assert.Equal(3, written.BytesWritten);`
- In `A_verified_upload_matches_when_the_host_pads_records`: `Assert.Equal(UploadOutcome.Matches, outcome);` → `Assert.Equal(UploadVerification.Matches, outcome.Verification);`
- In `A_verified_upload_reports_the_first_line_that_differs`: `Assert.Equal(new UploadOutcome(UploadVerification.Differs, 2), outcome);` → `Assert.Equal((UploadVerification.Differs, 2), (outcome.Verification, outcome.DiffersAtLine));`
- In `An_unverified_upload_does_not_read_back`: `Assert.Equal(UploadOutcome.NotChecked, outcome);` → `Assert.Equal(UploadVerification.NotChecked, outcome.Verification);`

Then add these tests to the class:

```csharp
    [Fact]
    public async Task A_download_returns_the_stamp_the_host_gave()
    {
        _host.Text[Jes2.ToString()] = ["A"];
        _host.Etags[Jes2.ToString()] = "stamp-7";

        var result = await HostFileTransfer.DownloadAsync(_host, Jes2, Local("a.txt"),
            new DownloadOptions(HostTransferMode.Text), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("stamp-7", result.Etag);
    }

    [Fact]
    public async Task A_download_of_an_unstamped_member_returns_no_stamp()
    {
        _host.Binary[Jes2.ToString()] = [1];

        var result = await HostFileTransfer.DownloadAsync(_host, Jes2, Local("a.bin"),
            new DownloadOptions(HostTransferMode.Binary), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Null(result.Etag);
    }

    [Fact]
    public async Task An_upload_passes_the_stamp_on_and_returns_the_new_one()
    {
        var checkedText = HostFileTransfer.CheckTextFile(await WriteLocal("up.jcl", "//A JOB\n"), Fb80);

        var outcome = await HostFileTransfer.UploadTextAsync(_host, Jes2, checkedText, verify: false, ifMatch: "stamp-1", TestContext.Current.CancellationToken);
        var binary = await HostFileTransfer.UploadBinaryAsync(_host, Jes2, await WriteLocal("up.bin", "x"), ifMatch: "stamp-2", TestContext.Current.CancellationToken);

        Assert.Equal(new[] { "stamp-1", "stamp-2" }, _host.IfMatches);
        Assert.Equal("write-1", outcome.Etag);
        Assert.Equal("write-2", binary);
    }

    [Fact]
    public async Task A_verified_upload_keeps_the_write_stamp_not_the_read_back_one()
    {
        var checkedText = HostFileTransfer.CheckTextFile(await WriteLocal("up.jcl", "//A JOB\n"), Fb80);

        var outcome = await HostFileTransfer.UploadTextAsync(_host, Jes2, checkedText, verify: true, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("write-1", outcome.Etag);
        Assert.Equal(new[] { "writetext:SYS1.PROCLIB(JES2):1", "readtext:SYS1.PROCLIB(JES2)" }, _host.Calls);
    }
```

`WriteLocal` already exists in the class (it writes a string to a file under `_directory` and returns the path).

- [ ] **Step 8: Update the Core fake**

Replace the read, write and delete methods of `tests/LizTerm.Core.Tests/HostFiles/FakeHostFileService.cs` and add the stamp state. The whole class becomes:

```csharp
/// <summary>An in-memory host keyed by <see cref="HostPath.ToString"/>. Every write stamps its target
/// <c>write-N</c> (<see cref="Etags"/>) and records the <c>ifMatch</c> it was given (<see cref="IfMatches"/>).</summary>
internal sealed class FakeHostFileService : IHostFileService
{
    private int _writes;

    public Dictionary<string, List<string>> Text { get; } = [];
    public Dictionary<string, byte[]> Binary { get; } = [];
    public Dictionary<string, string> Etags { get; } = [];
    public List<string?> IfMatches { get; } = [];
    public List<string> Calls { get; } = [];

    /// <summary>Applied to what a text write stores, to play a host that alters data.</summary>
    public Func<IReadOnlyList<string>, List<string>> StoreTransform { get; set; } = lines => [.. lines];

    /// <summary>Thrown by reads after <see cref="BytesBeforeFailure"/> bytes have been written.</summary>
    public Exception? ReadFailure { get; set; }
    public int BytesBeforeFailure { get; set; }

    public Task<HostServerInfo> GetServerInfoAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new HostServerInfo("fake", "1", "test"));

    public Task<HostFileListing> ListDatasetsAsync(string pattern, HostListRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult(new HostFileListing([], null));

    public Task<HostFileListing> ListMembersAsync(HostPath dataset, HostListRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult(new HostFileListing([], null));

    public Task<HostTextRead> ReadTextAsync(HostPath path, IProgress<long>? progress = null, CancellationToken cancellationToken = default)
    {
        Calls.Add($"readtext:{path}");
        cancellationToken.ThrowIfCancellationRequested();
        if (ReadFailure is not null) throw ReadFailure;
        var lines = Text[path.ToString()];
        progress?.Report(lines.Sum(l => l.Length + 1));
        return Task.FromResult(new HostTextRead(lines, Etags.GetValueOrDefault(path.ToString())));
    }

    public async Task<HostBinaryRead> ReadBinaryAsync(HostPath path, Stream destination, IProgress<long>? progress = null, CancellationToken cancellationToken = default)
    {
        Calls.Add($"readbinary:{path}");
        if (ReadFailure is not null)
        {
            await destination.WriteAsync(new byte[BytesBeforeFailure], cancellationToken);
            throw ReadFailure;
        }
        var bytes = Binary[path.ToString()];
        await destination.WriteAsync(bytes, cancellationToken);
        progress?.Report(bytes.Length);
        return new HostBinaryRead(bytes.Length, Etags.GetValueOrDefault(path.ToString()));
    }

    public Task<string?> WriteTextAsync(HostPath path, IReadOnlyList<string> lines, string? ifMatch = null, CancellationToken cancellationToken = default)
    {
        Calls.Add($"writetext:{path}:{lines.Count}");
        IfMatches.Add(ifMatch);
        Text[path.ToString()] = StoreTransform(lines);
        return Task.FromResult<string?>(Stamp(path));
    }

    public async Task<string?> WriteBinaryAsync(HostPath path, Stream source, string? ifMatch = null, CancellationToken cancellationToken = default)
    {
        Calls.Add($"writebinary:{path}");
        IfMatches.Add(ifMatch);
        using var copy = new MemoryStream();
        await source.CopyToAsync(copy, cancellationToken);
        Binary[path.ToString()] = copy.ToArray();
        return Stamp(path);
    }

    public Task CreateDatasetAsync(HostPath dataset, DatasetAllocation allocation, CancellationToken cancellationToken = default)
    {
        Calls.Add($"create:{dataset}");
        return Task.CompletedTask;
    }

    public Task RenameAsync(HostPath from, string newName, CancellationToken cancellationToken = default)
    {
        Calls.Add($"rename:{from}:{newName}");
        return Task.CompletedTask;
    }

    public Task DeleteAsync(HostPath path, CancellationToken cancellationToken = default)
    {
        Calls.Add($"delete:{path}");
        Text.Remove(path.ToString());
        Binary.Remove(path.ToString());
        Etags.Remove(path.ToString());
        return Task.CompletedTask;
    }

    public void Dispose() => Calls.Add("dispose");

    private string Stamp(HostPath path) => Etags[path.ToString()] = $"write-{++_writes}";
}
```

- [ ] **Step 9: Run the transfer tests to see the new ones fail**

Run: `dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~HostFileTransferTests" 2>&1 | grep -E "error|Passed!|Failed!" | head -5`
Expected: build errors in `HostFileTransfer.cs` (its calls no longer match the interface).

- [ ] **Step 10: Carry the stamp through the transfer**

In `src/LizTerm.Core/HostFiles/HostFileTransfer.cs`:

Replace the `UploadOutcome` record with:

```csharp
/// <param name="DiffersAtLine">1-based, set when <paramref name="Verification"/> is Differs.</param>
/// <param name="Etag">The host's stamp of the target as written, for the next write's <c>ifMatch</c>; null when
/// the host gives none.</param>
public sealed record UploadOutcome(UploadVerification Verification, int? DiffersAtLine, string? Etag);

/// <param name="BytesWritten">The bytes written locally.</param>
/// <param name="Etag">The host's stamp of what was read, for a later write's <c>ifMatch</c>; null when the host
/// gives none.</param>
public sealed record DownloadResult(long BytesWritten, string? Etag);
```

In `DownloadAsync`, change the return type to `Task<DownloadResult>`, the `<returns>` line to `/// <returns>The bytes written locally and the host's stamp.</returns>`, and the body of the `try` to:

```csharp
            long written;
            string? etag;
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                if (options.Mode == HostTransferMode.Binary)
                {
                    var read = await service.ReadBinaryAsync(path, stream, progress, cancellationToken);
                    (written, etag) = (read.Bytes, read.Etag);
                }
                else
                {
                    var read = await service.ReadTextAsync(path, progress, cancellationToken);
                    var bytes = Utf8NoMark.GetBytes(FormatText(read.Lines, options));
                    await stream.WriteAsync(bytes, cancellationToken);
                    (written, etag) = (bytes.Length, read.Etag);
                }
            }
            File.Move(temporary, full, overwrite: true);
            return new DownloadResult(written, etag);
```

Replace `UploadTextAsync` and `UploadBinaryAsync` with:

```csharp
    /// <summary>Sends text that passed its check and, with <paramref name="verify"/>, reads it back and compares.
    /// <paramref name="ifMatch"/> is the stamp the target must still hold (see <see cref="IHostFileService.WriteTextAsync"/>).
    /// The outcome carries the write's stamp, not the read-back's: the write's is the one the host promises for
    /// the next <c>ifMatch</c>.</summary>
    /// <exception cref="InvalidOperationException">The check found errors.</exception>
    public static async Task<UploadOutcome> UploadTextAsync(IHostFileService service, HostPath path,
        TextUploadResult checkedText, bool verify, string? ifMatch = null, CancellationToken cancellationToken = default)
    {
        if (!checkedText.CanUpload) throw new InvalidOperationException("The text did not pass its upload check.");
        var etag = await service.WriteTextAsync(path, checkedText.Lines, ifMatch, cancellationToken);
        if (!verify) return new UploadOutcome(UploadVerification.NotChecked, null, etag);
        var stored = await service.ReadTextAsync(path, null, cancellationToken);
        return FirstDifference(checkedText.Lines, stored.Lines) is { } line
            ? new UploadOutcome(UploadVerification.Differs, line, etag)
            : new UploadOutcome(UploadVerification.Matches, null, etag);
    }

    /// <summary>Sends <paramref name="sourceFile"/> as it is; returns the write's stamp.</summary>
    /// <exception cref="IOException">The local file could not be read; not a <see cref="HostFileException"/>.</exception>
    /// <exception cref="UnauthorizedAccessException">The local file is not readable.</exception>
    public static async Task<string?> UploadBinaryAsync(IHostFileService service, HostPath path, string sourceFile,
        string? ifMatch = null, CancellationToken cancellationToken = default)
    {
        await using var source = File.OpenRead(sourceFile);
        return await service.WriteBinaryAsync(path, source, ifMatch, cancellationToken);
    }
```

In the existing test `A_binary_upload_sends_the_file_bytes`, the call `HostFileTransfer.UploadBinaryAsync(_host, Jes2, source, TestContext.Current.CancellationToken)` now lands the token on `ifMatch`; change it to `HostFileTransfer.UploadBinaryAsync(_host, Jes2, source, cancellationToken: TestContext.Current.CancellationToken)`. Likewise every `UploadTextAsync(..., verify: true, TestContext.Current.CancellationToken)` call in that file (there are five) becomes `verify: true, cancellationToken: TestContext.Current.CancellationToken` (or `verify: false, cancellationToken: ...`).

- [ ] **Step 11: Run the Core tests**

Run: `dotnet test tests/LizTerm.Core.Tests 2>&1 | grep -E "Passed!|Failed!"`
Expected: `Passed!`. (`RepositoryHeadersTests` covers the two new files.)

Run: `dotnet build src/LizTerm.Core --no-incremental 2>&1 | grep -c " warning "` (expect `0`).

- [ ] **Step 12: Record the rules in the Core notes**

In `src/LizTerm.Core/CLAUDE.md`, under `## Host files`, after the `HostFileTransfer.DownloadAsync` bullet, add:

```markdown
- `DatasetAllocation` is what a create sends. `Problems()` holds the local rules only (a record format of F, V or
  U plus B, S, A, M at most once each; LRECL 1–32760, or 0–32760 for U; BLKSIZE 1–32760; primary ≥ 1; secondary
  ≥ 0; a partitioned dataset needs a directory block); every DCB combination beyond that is the host's to judge,
  and it answers every allocation failure the same way (`HostFileErrorKind.CannotAllocate`).
- **The stamp.** A read returns the host's stamp of the content (`HostTextRead.Etag`, `HostBinaryRead.Etag`) and a
  write takes one as `ifMatch` and returns the stamp of what it wrote. It is opaque: never parsed, only echoed. A
  write whose `ifMatch` no longer holds is `HostFileErrorKind.Conflict` and writes nothing. `HostFileTransfer`
  carries it through: `DownloadResult.Etag`, `UploadOutcome.Etag` (the write's stamp, not the read-back's).
- `RenameAsync` renames a member within its library or a dataset; `DeleteAsync` takes a member or a whole dataset.
```

- [ ] **Step 13: Commit**

```bash
git add src/LizTerm.Core tests/LizTerm.Core.Tests
git commit -m "Core: dataset allocation, rename, dataset delete, and the ETag on reads and writes

IHostFileService gains CreateDatasetAsync, RenameAsync and a DeleteAsync
that takes a dataset; reads return the host's stamp and writes take an
ifMatch and return the new one. DatasetAllocation holds the local rules.
The backend and the App follow in the next commits.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 2: The fixtures

Eleven exchanges recorded from the host. The scratch names sit under the recording user's own HLQ (`IBMUSER`), as the live test's will. Every name below appears only in the request, so nothing host-specific lands in a fixture.

**Files:**
- Create: `tests/LizTerm.Backend.Mvsmf.Tests/Fixtures/{create-201,create-dynalloc-500,write-etag-204,read-etag,write-412,rename-member-204,rename-member-missing,rename-member-exists,rename-ds-204,delete-ds-204,delete-ds-missing}.http`
- Modify: `tests/LizTerm.Backend.Mvsmf.Tests/Fixtures/README.md`

**Interfaces:**
- Produces: the fixtures Task 3 loads by name.

- [ ] **Step 1: Confirm nothing is left from an earlier run**

With the environment set (Prerequisites), from the repository root:

```bash
curl -sS --netrc-file ~/.mvsmf-netrc -o /dev/null -w '%{http_code}\n' "$LIZTERM_MVSMF_URL/restfiles/ds?dslevel=IBMUSER.LIZITEST.**"
curl -sS --netrc-file ~/.mvsmf-netrc "$LIZTERM_MVSMF_URL/restfiles/ds?dslevel=IBMUSER.LIZITEST.**"; echo
```

Expected: `200` and `{"items":[],"returnedRows":0,"JSONversion":1}`. If a `IBMUSER.LIZITEST.*` dataset is listed, delete it first: `curl -sS --netrc-file ~/.mvsmf-netrc -X DELETE "$LIZTERM_MVSMF_URL/restfiles/ds/<name>"`.

- [ ] **Step 2: Record create, the duplicate create, and the stamped write and read**

```bash
R=tools/record-mvsmf-fixture.sh
printf '{"dsorg":"PO","recfm":"FB","lrecl":80,"blksize":3120,"alcunit":"TRK","primary":1,"secondary":1,"dirblk":2}' > /tmp/liz-alloc.json
$R create-201 POST restfiles/ds/IBMUSER.LIZITEST.FIX -H 'Content-Type: application/json' --data-binary @/tmp/liz-alloc.json
$R create-dynalloc-500 POST restfiles/ds/IBMUSER.LIZITEST.FIX -H 'Content-Type: application/json' --data-binary @/tmp/liz-alloc.json
printf '//ONE JOB (ACCT),LIZTERM\n//* recorded fixture\n' > /tmp/liz-one.txt
$R write-etag-204 PUT 'restfiles/ds/IBMUSER.LIZITEST.FIX(ONE)' -H 'Content-Type: text/plain' -H 'X-IBM-Data-Type: text' -H 'X-IBM-Return-Etag: true' --data-binary @/tmp/liz-one.txt
$R read-etag GET 'restfiles/ds/IBMUSER.LIZITEST.FIX(ONE)' -H 'X-IBM-Data-Type: text' -H 'X-IBM-Return-Etag: true'
$R write-412 PUT 'restfiles/ds/IBMUSER.LIZITEST.FIX(ONE)' -H 'Content-Type: text/plain' -H 'X-IBM-Data-Type: text' -H 'If-Match: 0000000000000000' --data-binary @/tmp/liz-one.txt
```

Expected first lines: `create-201: HTTP/1.1 201 Created`, `create-dynalloc-500: HTTP/1.1 500 Internal Server Error`, `write-etag-204: HTTP/1.1 204 No Content`, `read-etag: HTTP/1.1 200 OK`, `write-412: HTTP/1.1 412 Precondition Failed`.

- [ ] **Step 3: Record the member renames**

```bash
printf '{"request":"rename","from-dataset":{"dsn":"IBMUSER.LIZITEST.FIX","member":"ONE"}}' > /tmp/liz-ren.json
$R rename-member-204 PUT 'restfiles/ds/IBMUSER.LIZITEST.FIX(TWO)' -H 'Content-Type: application/json' --data-binary @/tmp/liz-ren.json
$R rename-member-missing PUT 'restfiles/ds/IBMUSER.LIZITEST.FIX(TWO)' -H 'Content-Type: application/json' --data-binary @/tmp/liz-ren.json
curl -sS --netrc-file ~/.mvsmf-netrc -o /dev/null -w '%{http_code}\n' -X PUT -H 'Content-Type: text/plain' -H 'X-IBM-Data-Type: text' --data-binary @/tmp/liz-one.txt "$LIZTERM_MVSMF_URL/restfiles/ds/IBMUSER.LIZITEST.FIX(ONE)"
$R rename-member-exists PUT 'restfiles/ds/IBMUSER.LIZITEST.FIX(TWO)' -H 'Content-Type: application/json' --data-binary @/tmp/liz-ren.json
```

Expected: `rename-member-204: HTTP/1.1 204 No Content`, `rename-member-missing: HTTP/1.1 404 Not Found`, the bare curl prints `204` (ONE written again, no fixture), `rename-member-exists: HTTP/1.1 400 Bad Request`.

- [ ] **Step 4: Record the dataset rename and the deletes**

```bash
printf '{"request":"rename","from-dataset":{"dsn":"IBMUSER.LIZITEST.FIX"}}' > /tmp/liz-rends.json
$R rename-ds-204 PUT restfiles/ds/IBMUSER.LIZITEST.FIX2 -H 'Content-Type: application/json' --data-binary @/tmp/liz-rends.json
$R delete-ds-204 DELETE restfiles/ds/IBMUSER.LIZITEST.FIX2
$R delete-ds-missing DELETE restfiles/ds/IBMUSER.LIZITEST.FIX2
rm /tmp/liz-alloc.json /tmp/liz-one.txt /tmp/liz-ren.json /tmp/liz-rends.json
```

Expected: `rename-ds-204: HTTP/1.1 204 No Content`, `delete-ds-204: HTTP/1.1 204 No Content`, `delete-ds-missing: HTTP/1.1 404 Not Found`. Then repeat Step 1's listing: it must be empty again.

- [ ] **Step 5: Check the recordings**

```bash
cd tests/LizTerm.Backend.Mvsmf.Tests/Fixtures
grep -l '^ETag:' *.http
tail -c 120 create-dynalloc-500.http; echo
tail -c 120 write-412.http; echo
tail -c 100 rename-member-exists.http; echo
tail -c 100 rename-member-missing.http; echo
tail -c 100 delete-ds-missing.http; echo
cd -
```

Expected: `grep` lists exactly `read-etag.http` and `write-etag-204.http`, and each `ETag:` value is 16 hex digits with no quotes; `create-dynalloc-500` ends `{"category":8,"rc":900,"reason":7,"message":"Dynamic allocation Error"}`; `write-412` ends with `"reason":10` and "The resource was modified since the supplied ETag was created"; `rename-member-exists` ends with `"reason":7` and "Rename target already exists"; `rename-member-missing` ends with `"reason":5`; `delete-ds-missing` ends with `"reason":4`. If any body differs in shape, stop and report it: Task 3's mapping depends on it.

- [ ] **Step 6: Update the fixtures README**

In `tests/LizTerm.Backend.Mvsmf.Tests/Fixtures/README.md`, after the `delete-missing` row, add:

```markdown
| `create-201` | `POST restfiles/ds/<hlq>.LIZITEST.FIX`, an FB 80 PDS of 1 track and 2 directory blocks |
| `create-dynalloc-500` | the same `POST` again — 500, category 8, rc 900, reason 7, the one answer for every allocation failure |
| `write-etag-204` | `PUT` of a member with `X-IBM-Return-Etag: true` — 204 with `ETag` |
| `read-etag` | `GET` of that member, text, with `X-IBM-Return-Etag: true` — 200 with the same `ETag` |
| `write-412` | `PUT` of that member with a stale `If-Match` — 412, reason 10, nothing written |
| `rename-member-204` | `PUT …(TWO)` with the JSON rename body naming `ONE` — 204 |
| `rename-member-missing` | the same again, `ONE` gone — 404, reason 5 |
| `rename-member-exists` | the same with `ONE` written again, `TWO` still there — 400, reason 7 |
| `rename-ds-204` | `PUT restfiles/ds/<hlq>.LIZITEST.FIX2` with the JSON rename body naming the old dataset — 204 |
| `delete-ds-204` | `DELETE` of that dataset — 204 |
| `delete-ds-missing` | the same again — 404, reason 4 |
```

- [ ] **Step 7: Confirm the fixture guard still holds**

Run: `dotnet test tests/LizTerm.Backend.Mvsmf.Tests --filter "FullyQualifiedName~FixtureTests" 2>&1 | grep -E "error|Passed!|Failed!" | head -3`
Expected: a build error, because Task 1 changed the interface and the backend does not compile yet. That is the red boundary; the guard runs in Task 3 Step 12. Move on.

- [ ] **Step 8: Commit**

```bash
git add tests/LizTerm.Backend.Mvsmf.Tests/Fixtures
git commit -m "Record the mvsMF create, rename, dataset delete and ETag fixtures

Eleven exchanges from the 1.1.0 host: the 201 and the one 500 of a
create, both member rename failures, the dataset rename, the dataset
delete and its 404, and the ETag on a read, a write and a stale
If-Match.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 3: Backend — the three operations and the stamp

**Files:**
- Modify: `src/LizTerm.Backend.Mvsmf/MvsmfJson.cs`
- Modify: `src/LizTerm.Backend.Mvsmf/MvsmfErrors.cs`
- Modify: `src/LizTerm.Backend.Mvsmf/MvsmfFileService.cs`
- Modify: `src/LizTerm.Backend.Mvsmf/CLAUDE.md`
- Create: `tests/LizTerm.Backend.Mvsmf.Tests/{MvsmfCreateTests,MvsmfRenameTests,MvsmfDeleteTests,MvsmfEtagTests}.cs`
- Modify: `tests/LizTerm.Backend.Mvsmf.Tests/{MvsmfErrorsTests,MvsmfWriteTests,MvsmfReadTests}.cs`

**Interfaces:**
- Consumes: Task 1's contract; Task 2's fixtures.
- Produces: `MvsmfFileService` implementing every member; `internal static string? MvsmfFileService.EtagOf(HttpResponseMessage)`; `MvsmfErrors.Classify` mapping 412 → `Conflict`, category 8 rc 900 → `CannotAllocate`, 400 category 6 reason 7 → `AlreadyExists`.

- [ ] **Step 1: Write the failing error-mapping tests**

In `tests/LizTerm.Backend.Mvsmf.Tests/MvsmfErrorsTests.cs`, in `Classifies_by_reason_before_status`, change the row `[InlineData(HttpStatusCode.InternalServerError, 8, 900, 7, HostFileErrorKind.ServerError)]` to `HostFileErrorKind.CannotAllocate` and add these rows after it:

```csharp
    [InlineData(HttpStatusCode.PreconditionFailed, 6, 8, 10, HostFileErrorKind.Conflict)]
    [InlineData(HttpStatusCode.PreconditionFailed, null, null, null, HostFileErrorKind.Conflict)]
    [InlineData(HttpStatusCode.BadRequest, 6, 8, 7, HostFileErrorKind.AlreadyExists)]
    [InlineData(HttpStatusCode.BadRequest, 6, 8, 3, HostFileErrorKind.InvalidRequest)]
    [InlineData(HttpStatusCode.InternalServerError, 6, 8, 8, HostFileErrorKind.ServerError)]
    [InlineData(HttpStatusCode.InternalServerError, 6, 8, 11, HostFileErrorKind.ServerError)]
```

Add these tests to the class (names carry the compat tags):

```csharp
    [Fact]
    public async Task Create_failure_is_one_500_so_it_names_every_cause()
    {
        using var response = Fixture.Load("create-dynalloc-500");
        var body = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);

        var ex = MvsmfErrors.FromResponse(response.StatusCode, body, "IBMUSER.LIZITEST.FIX");

        Assert.Equal(HostFileErrorKind.CannotAllocate, ex.Kind);
        Assert.Equal(7, ex.Reason);
        Assert.Equal("Dynamic allocation Error", ex.ServerMessage);
        Assert.Equal("IBMUSER.LIZITEST.FIX: the host could not allocate it (it may already exist, there may be no space, or you may not be authorized).", ex.Message);
    }

    [Fact]
    public async Task Rename_target_exists_400_is_already_exists()
    {
        using var response = Fixture.Load("rename-member-exists");
        var body = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);

        var ex = MvsmfErrors.FromResponse(response.StatusCode, body, "Rename A.B(ONE) to TWO");

        Assert.Equal(HostFileErrorKind.AlreadyExists, ex.Kind);
        Assert.Equal(7, ex.Reason);
        Assert.Equal("Rename A.B(ONE) to TWO: a member of that name already exists.", ex.Message);
    }

    [Fact]
    public async Task A_stale_if_match_is_a_conflict()
    {
        using var response = Fixture.Load("write-412");
        var body = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);

        var ex = MvsmfErrors.FromResponse(response.StatusCode, body, "A.B(ONE)");

        Assert.Equal(HostFileErrorKind.Conflict, ex.Kind);
        Assert.Equal(10, ex.Reason);
        Assert.Equal("A.B(ONE): changed on the host since it was read.", ex.Message);
    }

    [Fact]
    public async Task A_dataset_renamed_onto_an_existing_name_is_the_hosts_500()
    {
        // No fixture: IDCAMS ALTER refuses it and the host answers reason 8 like any other rename failure.
        var body = "{\"rc\":8,\"category\":6,\"reason\":8,\"message\":\"Rename operation failed\"}"u8.ToArray();

        var ex = MvsmfErrors.FromResponse(HttpStatusCode.InternalServerError, body, "Rename A.B to A.C");

        Assert.Equal(HostFileErrorKind.ServerError, ex.Kind);
        Assert.Equal("Rename A.B to A.C: Rename operation failed (reason 8).", ex.Message);
    }
```

- [ ] **Step 2: Map the three answers**

In `src/LizTerm.Backend.Mvsmf/MvsmfErrors.cs`, add after the `SecurityCategory` constant:

```csharp
    private const int AllocationCategory = 8;
    private const int AllocationRc = 900;
    private const int RenameTargetExistsReason = 7;
```

In `Classify`, insert after the `NotAuthorized` rule for `status == HttpStatusCode.Forbidden` and before the `BadRequest` rule:

```csharp
        // mvsMF-compat: create-failure-is-one-500 — every allocation failure (the name exists, no space, a DCB the
        // volume cannot hold, no authority) is the same 500, category 8, rc 900, byte for byte what z/OSMF sends.
        if (category == AllocationCategory && rc == AllocationRc) return HostFileErrorKind.CannotAllocate;
        // mvsMF-compat: etag — a stale If-Match is 412 reason 10 and nothing is written.
        if (status == HttpStatusCode.PreconditionFailed) return HostFileErrorKind.Conflict;
        // mvsMF-compat: rename-target-exists-400 — a member rename onto an existing name is 400 reason 7.
        if (status == HttpStatusCode.BadRequest && category == DatasetCategory && reason == RenameTargetExistsReason)
            return HostFileErrorKind.AlreadyExists;
```

In `Describe`, add three arms before the `InvalidRequest` one:

```csharp
        HostFileErrorKind.CannotAllocate => $"{what}: the host could not allocate it (it may already exist, there may be no space, or you may not be authorized).",
        HostFileErrorKind.Conflict => $"{what}: changed on the host since it was read.",
        HostFileErrorKind.AlreadyExists => $"{what}: a member of that name already exists.",
```

- [ ] **Step 3: Run the error tests**

Run: `dotnet build src/LizTerm.Backend.Mvsmf 2>&1 | grep -E "error" | head -5`
Expected: errors in `MvsmfFileService.cs` only (it does not implement the interface yet). The error tests run at Step 12.

- [ ] **Step 4: Add the JSON shapes**

In `src/LizTerm.Backend.Mvsmf/MvsmfJson.cs`, add after `MvsmfError`:

```csharp
/// <summary>A create's body. <c>dirblk</c> is sent for a partitioned dataset only; the host ignores it otherwise,
/// and leaving it out keeps the sequential body what the docs show.</summary>
internal sealed record MvsmfAllocation(
    [property: JsonPropertyName("dsorg")] string Dsorg,
    [property: JsonPropertyName("recfm")] string Recfm,
    [property: JsonPropertyName("lrecl")] int Lrecl,
    [property: JsonPropertyName("blksize")] int Blksize,
    [property: JsonPropertyName("alcunit")] string Alcunit,
    [property: JsonPropertyName("primary")] int Primary,
    [property: JsonPropertyName("secondary")] int Secondary,
    [property: JsonPropertyName("dirblk"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Dirblk);

/// <summary>A rename's body: the old name; the new one is in the URL. <c>member</c> is absent for a dataset.</summary>
internal sealed record MvsmfRename(
    [property: JsonPropertyName("request")] string Request,
    [property: JsonPropertyName("from-dataset")] MvsmfRenameSource FromDataset);

internal sealed record MvsmfRenameSource(
    [property: JsonPropertyName("dsn")] string Dsn,
    [property: JsonPropertyName("member"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Member);
```

and two attributes on the context:

```csharp
[JsonSerializable(typeof(MvsmfAllocation))]
[JsonSerializable(typeof(MvsmfRename))]
```

- [ ] **Step 5: Write the failing create tests**

Create `tests/LizTerm.Backend.Mvsmf.Tests/MvsmfCreateTests.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.Core.HostFiles;

namespace LizTerm.Backend.Mvsmf.Tests;

public class MvsmfCreateTests
{
    private static readonly HostPath NewPds = HostPath.ForDataset("IBMUSER.LIZITEST.FIX");
    private static readonly DatasetAllocation Pds = new(DatasetOrganization.Partitioned, "fb", 80, 3120, SpaceUnit.Tracks, 1, 1, 2);

    private static MvsmfFileService Service(RecordedHandler handler) =>
        new(handler, MvsmfAuthTests.Base, MvsmfAuthTests.Providing([], new HostCredentials("MVSCE02", "pw")));

    [Fact]
    public async Task A_create_posts_the_allocation_as_json()
    {
        var handler = new RecordedHandler().Then("login-200").Then("create-201");
        using var service = Service(handler);

        await service.CreateDatasetAsync(NewPds, Pds, TestContext.Current.CancellationToken);

        var request = Assert.Single(handler.Requests, r => r.Method == HttpMethod.Post && r.Uri.AbsolutePath.Contains("restfiles"));
        Assert.Equal("/zosmf/restfiles/ds/IBMUSER.LIZITEST.FIX", request.Uri.PathAndQuery);
        Assert.Equal("application/json", request.ContentType);
        Assert.Equal("{\"dsorg\":\"PO\",\"recfm\":\"FB\",\"lrecl\":80,\"blksize\":3120,\"alcunit\":\"TRK\",\"primary\":1,\"secondary\":1,\"dirblk\":2}", request.BodyText);
    }

    [Fact]
    public async Task A_sequential_create_sends_no_directory_blocks_and_cylinders_when_asked()
    {
        var handler = new RecordedHandler().Then("login-200").Then("create-201");
        using var service = Service(handler);

        await service.CreateDatasetAsync(NewPds, Pds with { Organization = DatasetOrganization.Sequential, Unit = SpaceUnit.Cylinders, DirectoryBlocks = 0 }, TestContext.Current.CancellationToken);

        Assert.Equal("{\"dsorg\":\"PS\",\"recfm\":\"FB\",\"lrecl\":80,\"blksize\":3120,\"alcunit\":\"CYL\",\"primary\":1,\"secondary\":1}", handler.Requests[1].BodyText);
    }

    [Fact]
    public async Task Create_failure_is_one_500_reported_as_cannot_allocate()
    {
        using var service = Service(new RecordedHandler().Then("login-200").Then("create-dynalloc-500"));

        var ex = await Assert.ThrowsAsync<HostFileException>(() => service.CreateDatasetAsync(NewPds, Pds, TestContext.Current.CancellationToken));

        Assert.Equal(HostFileErrorKind.CannotAllocate, ex.Kind);
        Assert.StartsWith("IBMUSER.LIZITEST.FIX: the host could not allocate it", ex.Message);
    }

    [Fact]
    public async Task An_allocation_that_fails_its_own_rules_is_refused_before_sending()
    {
        var handler = new RecordedHandler();
        using var service = Service(handler);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.CreateDatasetAsync(NewPds, Pds with { Primary = 0 }, TestContext.Current.CancellationToken));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task A_member_path_cannot_be_created()
    {
        var handler = new RecordedHandler();
        using var service = Service(handler);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.CreateDatasetAsync(HostPath.ForMember("A.B", "C"), Pds, TestContext.Current.CancellationToken));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task The_repeat_after_a_401_posts_the_same_body()
    {
        var handler = new RecordedHandler()
            .Then("login-200").Then(System.Net.HttpStatusCode.Unauthorized).Then("login-200").Then("create-201");
        using var service = Service(handler);

        await service.CreateDatasetAsync(NewPds, Pds, TestContext.Current.CancellationToken);

        var posts = handler.Requests.Where(r => r.Method == HttpMethod.Post && r.Uri.AbsolutePath.Contains("restfiles")).ToList();
        Assert.Equal(2, posts.Count);
        Assert.Equal(posts[0].Body, posts[1].Body);
    }
}
```

- [ ] **Step 6: Write the failing rename tests**

Create `tests/LizTerm.Backend.Mvsmf.Tests/MvsmfRenameTests.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.Core.HostFiles;

namespace LizTerm.Backend.Mvsmf.Tests;

public class MvsmfRenameTests
{
    private static readonly HostPath One = HostPath.ForMember("IBMUSER.LIZITEST.FIX", "ONE");
    private static readonly HostPath Fix = HostPath.ForDataset("IBMUSER.LIZITEST.FIX");

    private static MvsmfFileService Service(RecordedHandler handler) =>
        new(handler, MvsmfAuthTests.Base, MvsmfAuthTests.Providing([], new HostCredentials("MVSCE02", "pw")));

    [Fact]
    public async Task Put_json_is_rename_so_a_member_rename_puts_json_to_the_new_name()
    {
        var handler = new RecordedHandler().Then("login-200").Then("rename-member-204");
        using var service = Service(handler);

        await service.RenameAsync(One, "two", TestContext.Current.CancellationToken);

        var request = Assert.Single(handler.Requests, r => r.Method == HttpMethod.Put);
        Assert.Equal("/zosmf/restfiles/ds/IBMUSER.LIZITEST.FIX(TWO)", request.Uri.PathAndQuery);
        Assert.Equal("application/json", request.ContentType);
        Assert.Equal("{\"request\":\"rename\",\"from-dataset\":{\"dsn\":\"IBMUSER.LIZITEST.FIX\",\"member\":\"ONE\"}}", request.BodyText);
        Assert.DoesNotContain("X-IBM-Data-Type", request.HeaderNames, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_dataset_rename_names_only_the_old_dataset()
    {
        var handler = new RecordedHandler().Then("login-200").Then("rename-ds-204");
        using var service = Service(handler);

        await service.RenameAsync(Fix, "ibmuser.lizitest.fix2", TestContext.Current.CancellationToken);

        var request = Assert.Single(handler.Requests, r => r.Method == HttpMethod.Put);
        Assert.Equal("/zosmf/restfiles/ds/IBMUSER.LIZITEST.FIX2", request.Uri.PathAndQuery);
        Assert.Equal("{\"request\":\"rename\",\"from-dataset\":{\"dsn\":\"IBMUSER.LIZITEST.FIX\"}}", request.BodyText);
    }

    [Fact]
    public async Task Renaming_a_missing_member_is_not_found()
    {
        using var service = Service(new RecordedHandler().Then("login-200").Then("rename-member-missing"));

        var ex = await Assert.ThrowsAsync<HostFileException>(() => service.RenameAsync(One, "TWO", TestContext.Current.CancellationToken));

        Assert.Equal(HostFileErrorKind.NotFound, ex.Kind);
        Assert.Equal("Rename IBMUSER.LIZITEST.FIX(ONE) to TWO: not found.", ex.Message);
    }

    [Fact]
    public async Task Rename_target_exists_400_is_already_exists()
    {
        using var service = Service(new RecordedHandler().Then("login-200").Then("rename-member-exists"));

        var ex = await Assert.ThrowsAsync<HostFileException>(() => service.RenameAsync(One, "TWO", TestContext.Current.CancellationToken));

        Assert.Equal(HostFileErrorKind.AlreadyExists, ex.Kind);
        Assert.Equal("Rename IBMUSER.LIZITEST.FIX(ONE) to TWO: a member of that name already exists.", ex.Message);
    }

    [Theory]
    [InlineData("TOOLONGNAME")]
    [InlineData("")]
    [InlineData("1BAD")]
    public async Task A_new_member_name_the_rules_refuse_is_never_sent(string newName)
    {
        var handler = new RecordedHandler();
        using var service = Service(handler);

        await Assert.ThrowsAsync<ArgumentException>(() => service.RenameAsync(One, newName, TestContext.Current.CancellationToken));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task A_new_dataset_name_the_rules_refuse_is_never_sent()
    {
        var handler = new RecordedHandler();
        using var service = Service(handler);

        await Assert.ThrowsAsync<ArgumentException>(() => service.RenameAsync(Fix, "A.B(C)", TestContext.Current.CancellationToken));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task The_repeat_after_a_401_sends_the_same_body()
    {
        var handler = new RecordedHandler()
            .Then("login-200").Then(System.Net.HttpStatusCode.Unauthorized).Then("login-200").Then("rename-member-204");
        using var service = Service(handler);

        await service.RenameAsync(One, "TWO", TestContext.Current.CancellationToken);

        var puts = handler.Requests.Where(r => r.Method == HttpMethod.Put).ToList();
        Assert.Equal(2, puts.Count);
        Assert.Equal(puts[0].Body, puts[1].Body);
    }
}
```

- [ ] **Step 7: Write the failing delete tests, and retire the "never deleted" one**

In `tests/LizTerm.Backend.Mvsmf.Tests/MvsmfWriteTests.cs`, delete the test `A_whole_dataset_is_never_deleted` (the last one in the file) and move `A_member_is_deleted` and `Deleting_a_missing_member_is_not_found` into the new file below (delete them from `MvsmfWriteTests.cs`). Create `tests/LizTerm.Backend.Mvsmf.Tests/MvsmfDeleteTests.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.Core.HostFiles;

namespace LizTerm.Backend.Mvsmf.Tests;

public class MvsmfDeleteTests
{
    private static readonly HostPath NewMember = HostPath.ForMember("MVSCE02.CNTL", "NEWMEM");
    private static readonly HostPath Fix2 = HostPath.ForDataset("IBMUSER.LIZITEST.FIX2");

    private static MvsmfFileService Service(RecordedHandler handler) =>
        new(handler, MvsmfAuthTests.Base, MvsmfAuthTests.Providing([], new HostCredentials("MVSCE02", "pw")));

    [Fact]
    public async Task A_member_is_deleted()
    {
        var handler = new RecordedHandler().Then("login-200").Then("delete-204");
        using var service = Service(handler);

        await service.DeleteAsync(NewMember, TestContext.Current.CancellationToken);

        var request = Assert.Single(handler.Requests, r => r.Method == HttpMethod.Delete);
        Assert.Equal("/zosmf/restfiles/ds/MVSCE02.CNTL(NEWMEM)", request.Uri.PathAndQuery);
    }

    [Fact]
    public async Task Deleting_a_missing_member_is_not_found()
    {
        using var service = Service(new RecordedHandler().Then("login-200").Then("delete-missing"));

        var ex = await Assert.ThrowsAsync<HostFileException>(() => service.DeleteAsync(NewMember, TestContext.Current.CancellationToken));

        Assert.Equal(HostFileErrorKind.NotFound, ex.Kind);
        Assert.Equal("MVSCE02.CNTL(NEWMEM): not found.", ex.Message);
    }

    [Fact]
    public async Task A_whole_dataset_is_deleted()
    {
        var handler = new RecordedHandler().Then("login-200").Then("delete-ds-204");
        using var service = Service(handler);

        await service.DeleteAsync(Fix2, TestContext.Current.CancellationToken);

        var request = Assert.Single(handler.Requests, r => r.Method == HttpMethod.Delete);
        Assert.Equal("/zosmf/restfiles/ds/IBMUSER.LIZITEST.FIX2", request.Uri.PathAndQuery);
    }

    [Fact]
    public async Task Deleting_a_missing_dataset_is_not_found()
    {
        using var service = Service(new RecordedHandler().Then("login-200").Then("delete-ds-missing"));

        var ex = await Assert.ThrowsAsync<HostFileException>(() => service.DeleteAsync(Fix2, TestContext.Current.CancellationToken));

        Assert.Equal(HostFileErrorKind.NotFound, ex.Kind);
        Assert.Equal(4, ex.Reason);
        Assert.Equal("IBMUSER.LIZITEST.FIX2: not found.", ex.Message);
    }
}
```

- [ ] **Step 8: Write the failing ETag tests**

Create `tests/LizTerm.Backend.Mvsmf.Tests/MvsmfEtagTests.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Net;
using LizTerm.Core.HostFiles;

namespace LizTerm.Backend.Mvsmf.Tests;

/// <summary>Pins the <c>etag</c> compatibility entry: the stamp is asked for on every read and write, echoed back
/// verbatim as If-Match, and a 412 is a conflict.</summary>
public class MvsmfEtagTests
{
    private static readonly HostPath One = HostPath.ForMember("IBMUSER.LIZITEST.FIX", "ONE");

    private static MvsmfFileService Service(RecordedHandler handler) =>
        new(handler, MvsmfAuthTests.Base, MvsmfAuthTests.Providing([], new HostCredentials("MVSCE02", "pw")));

    private static string RecordedEtag(string fixture)
    {
        using var response = Fixture.Load(fixture);
        return response.Headers.GetValues("ETag").Single();
    }

    [Fact]
    public async Task Etag_is_asked_for_on_a_text_read_and_returned()
    {
        var handler = new RecordedHandler().Then("login-200").Then("read-etag");
        using var service = Service(handler);

        var read = await service.ReadTextAsync(One, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("true", handler.Requests[1].Headers["X-IBM-Return-Etag"]);
        Assert.Equal(RecordedEtag("read-etag"), read.Etag);
        Assert.Matches("^[0-9A-Fa-f]{16}$", read.Etag!);
        Assert.StartsWith("//ONE JOB", read.Lines[0]);
    }

    [Fact]
    public async Task Etag_is_asked_for_on_a_binary_read_and_returned()
    {
        var handler = new RecordedHandler().Then("login-200").Then("read-etag");
        using var service = Service(handler);

        var read = await service.ReadBinaryAsync(One, new MemoryStream(), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("true", handler.Requests[1].Headers["X-IBM-Return-Etag"]);
        Assert.Equal(RecordedEtag("read-etag"), read.Etag);
    }

    [Fact]
    public async Task A_read_without_a_stamp_returns_null()
    {
        using var service = Service(new RecordedHandler().Then("login-200").Then("read-text-jes2"));

        var read = await service.ReadTextAsync(HostPath.ForMember("SYS1.PROCLIB", "JES2"), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Null(read.Etag);
    }

    [Fact]
    public async Task Etag_is_asked_for_on_a_write_and_the_new_stamp_returned()
    {
        var handler = new RecordedHandler().Then("login-200").Then("write-etag-204").Then("write-etag-204");
        using var service = Service(handler);

        var text = await service.WriteTextAsync(One, ["//ONE JOB"], cancellationToken: TestContext.Current.CancellationToken);
        var binary = await service.WriteBinaryAsync(One, new MemoryStream([1]), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(RecordedEtag("write-etag-204"), text);
        Assert.Equal(RecordedEtag("write-etag-204"), binary);
        Assert.Equal("true", handler.Requests[1].Headers["X-IBM-Return-Etag"]);
        Assert.DoesNotContain("If-Match", handler.Requests[1].HeaderNames, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task If_match_is_sent_as_given()
    {
        var handler = new RecordedHandler().Then("login-200").Then("write-etag-204");
        using var service = Service(handler);

        await service.WriteTextAsync(One, ["A"], ifMatch: "7F3A00000000BEEF", TestContext.Current.CancellationToken);

        Assert.Equal("7F3A00000000BEEF", handler.Requests[1].Headers["If-Match"]);
    }

    [Fact]
    public async Task A_stale_if_match_is_a_conflict_and_the_write_is_not_repeated()
    {
        var handler = new RecordedHandler().Then("login-200").Then("write-412");
        using var service = Service(handler);

        var ex = await Assert.ThrowsAsync<HostFileException>(() =>
            service.WriteTextAsync(One, ["A"], ifMatch: "0000000000000000", TestContext.Current.CancellationToken));

        Assert.Equal(HostFileErrorKind.Conflict, ex.Kind);
        Assert.Equal("IBMUSER.LIZITEST.FIX(ONE): changed on the host since it was read.", ex.Message);
        Assert.Single(handler.Requests, r => r.Method == HttpMethod.Put);
    }

    [Theory]
    [InlineData("7F3A", "7F3A")]
    [InlineData("\"7F3A\"", "7F3A")]
    [InlineData("W/\"7F3A\"", "7F3A")]
    [InlineData("  7F3A ", "7F3A")]
    [InlineData("", null)]
    [InlineData("\"\"", null)]
    public void The_stamp_is_taken_from_the_header_as_bare_text(string header, string? expected)
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Headers.TryAddWithoutValidation("ETag", header);

        Assert.Equal(expected, MvsmfFileService.EtagOf(response));
    }

    [Fact]
    public void No_header_is_no_stamp()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK);
        Assert.Null(MvsmfFileService.EtagOf(response));
    }
}
```

- [ ] **Step 9: Fix the existing read and write tests for the new shapes**

In `tests/LizTerm.Backend.Mvsmf.Tests/MvsmfReadTests.cs`:
- `var lines = await service.ReadTextAsync(Jes2, ...)` → `var lines = (await service.ReadTextAsync(Jes2, cancellationToken: TestContext.Current.CancellationToken)).Lines;`
- The two `Assert.Equal(new[] { … }, await service.ReadTextAsync(Jes2, …))` become `Assert.Equal(new[] { … }, (await service.ReadTextAsync(Jes2, cancellationToken: TestContext.Current.CancellationToken)).Lines);`
- `var count = await service.ReadBinaryAsync(Jes2, destination, progress, …)` → `var count = (await service.ReadBinaryAsync(Jes2, destination, progress, TestContext.Current.CancellationToken)).Bytes;`
- Any other `ReadTextAsync`/`ReadBinaryAsync` result used as a list or a number: take `.Lines` / `.Bytes`. Calls whose result is discarded or that only assert a throw are unchanged.

In `tests/LizTerm.Backend.Mvsmf.Tests/MvsmfWriteTests.cs`, every `WriteTextAsync(NewMember, [...], TestContext.Current.CancellationToken)` and `WriteBinaryAsync(NewMember, stream, TestContext.Current.CancellationToken)` now lands the token on `ifMatch`: change each to `cancellationToken: TestContext.Current.CancellationToken` (seven calls plus the two inside `The_repeat_after_a_401_sends_the_same_body`, and the one in `Put_json_is_rename_so_no_write_ever_sends_json`). Also delete the `System.Net` `using` if the file no longer needs it (it still does: `HttpStatusCode.Unauthorized`).

- [ ] **Step 10: Implement the backend**

In `src/LizTerm.Backend.Mvsmf/MvsmfFileService.cs`:

Add constants after `SessionCookie`:

```csharp
    private const string ReturnEtagHeader = "X-IBM-Return-Etag";
    private const string JsonContentType = "application/json";
```

Replace `ReadTextAsync` and `ReadBinaryAsync` with:

```csharp
    public async Task<HostTextRead> ReadTextAsync(HostPath path, IProgress<long>? progress = null, CancellationToken cancellationToken = default)
    {
        var what = path.ToString();
        using var idle = new IdleTimeout(_idle, cancellationToken);
        using var response = await SendAsync(() => Get(path, "text"), what, idle, cancellationToken);
        using var body = new MemoryStream();
        await CopyBodyAsync(response, body, idle, progress, what, cancellationToken);
        // mvsMF-compat: text-read-keeps-trailing-blanks — fixed records arrive padded; HostFileTransfer trims them.
        return new HostTextRead(SplitRecords(body.GetBuffer().AsSpan(0, (int)body.Length)), EtagOf(response));
    }

    public async Task<HostBinaryRead> ReadBinaryAsync(HostPath path, Stream destination, IProgress<long>? progress = null, CancellationToken cancellationToken = default)
    {
        var what = path.ToString();
        using var idle = new IdleTimeout(_idle, cancellationToken);
        using var response = await SendAsync(() => Get(path, "binary"), what, idle, cancellationToken);
        var bytes = await CopyBodyAsync(response, destination, idle, progress, what, cancellationToken);
        return new HostBinaryRead(bytes, EtagOf(response));
    }

    private HttpRequestMessage Get(HostPath path, string dataType)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, Url(DatasetPath(path)));
        request.Headers.Add("X-IBM-Data-Type", dataType);
        // mvsMF-compat: etag — the stamp comes only when asked for; it costs the host a second pass over the member.
        request.Headers.Add(ReturnEtagHeader, "true");
        return request;
    }

    /// <summary>The <c>ETag</c> header's value as bare text: quotes and a weak-validator prefix removed, blanks
    /// trimmed, null when absent or empty. Never parsed beyond that: it is echoed back as <c>If-Match</c>.</summary>
    internal static string? EtagOf(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("ETag", out var values)) return null;
        var value = values.FirstOrDefault()?.Trim() ?? "";
        if (value.StartsWith("W/", StringComparison.OrdinalIgnoreCase)) value = value[2..].Trim();
        value = value.Trim('"').Trim();
        return value.Length > 0 ? value : null;
    }
```

Replace `WriteTextAsync`, `WriteBinaryAsync`, `DeleteAsync` and `PutAsync` with:

```csharp
    public Task<string?> WriteTextAsync(HostPath path, IReadOnlyList<string> lines, string? ifMatch = null, CancellationToken cancellationToken = default) =>
        PutAsync(path, EncodeText(lines), "text", "text/plain", ifMatch, cancellationToken);

    public async Task<string?> WriteBinaryAsync(HostPath path, Stream source, string? ifMatch = null, CancellationToken cancellationToken = default)
    {
        // Held in memory so the repeat after a 401 can send the same bytes.
        using var copy = new MemoryStream();
        await source.CopyToAsync(copy, cancellationToken);
        return await PutAsync(path, copy.ToArray(), "binary", "application/octet-stream", ifMatch, cancellationToken);
    }

    public async Task CreateDatasetAsync(HostPath dataset, DatasetAllocation allocation, CancellationToken cancellationToken = default)
    {
        if (dataset.Kind != HostPathKind.Dataset) throw new ArgumentException("Only a dataset can be created.", nameof(dataset));
        if (allocation.Problems() is { Count: > 0 } problems)
            throw new ArgumentException(string.Join(" ", problems.Values), nameof(allocation));
        var what = dataset.ToString();
        var partitioned = allocation.Organization == DatasetOrganization.Partitioned;
        var body = JsonSerializer.SerializeToUtf8Bytes(new MvsmfAllocation(
            partitioned ? "PO" : "PS",
            allocation.FoldedRecfm,
            allocation.Lrecl,
            allocation.Blksize,
            allocation.Unit == SpaceUnit.Cylinders ? "CYL" : "TRK",
            allocation.Primary,
            allocation.Secondary,
            partitioned ? allocation.DirectoryBlocks : null), MvsmfJsonContext.Default.MvsmfAllocation);
        using var idle = new IdleTimeout(_idle, cancellationToken);
        // mvsMF-compat: create-failure-is-one-500 — the host answers every allocation failure the same way;
        // MvsmfErrors maps it to CannotAllocate, whose sentence names the three possible causes.
        using var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Post, Url(DatasetPath(dataset)))
        {
            Content = new ByteArrayContent(body) { Headers = { ContentType = new MediaTypeHeaderValue(JsonContentType) } },
        }, what, idle, cancellationToken);
    }

    public async Task RenameAsync(HostPath from, string newName, CancellationToken cancellationToken = default)
    {
        // ForMember and ForDataset fold the name and throw ArgumentException for one the rules refuse, so nothing
        // the host would fold differently is ever sent.
        var target = from.Kind == HostPathKind.Member ? HostPath.ForMember(from.Dataset, newName) : HostPath.ForDataset(newName);
        var what = $"Rename {from} to {(from.Kind == HostPathKind.Member ? target.Member : target.Dataset)}";
        var body = JsonSerializer.SerializeToUtf8Bytes(
            new MvsmfRename("rename", new MvsmfRenameSource(from.Dataset, from.Member)), MvsmfJsonContext.Default.MvsmfRename);
        using var idle = new IdleTimeout(_idle, cancellationToken);
        // mvsMF-compat: put-json-is-rename — this is the one PUT that sends application/json, and it is a rename
        // on purpose: the new name is the URL, the old one the body. A write never sends this content type.
        // mvsMF-compat: rename-target-exists-400 — a member rename onto an existing name is 400 reason 7.
        using var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Put, Url(DatasetPath(target)))
        {
            Content = new ByteArrayContent(body) { Headers = { ContentType = new MediaTypeHeaderValue(JsonContentType) } },
        }, what, idle, cancellationToken);
    }

    public async Task DeleteAsync(HostPath path, CancellationToken cancellationToken = default)
    {
        var what = path.ToString();
        using var idle = new IdleTimeout(_idle, cancellationToken);
        using var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Delete, Url(DatasetPath(path))), what, idle, cancellationToken);
    }

    private async Task<string?> PutAsync(HostPath path, byte[] body, string dataType, string contentType, string? ifMatch, CancellationToken cancellationToken)
    {
        var what = path.ToString();
        using var idle = new IdleTimeout(_idle, cancellationToken);
        using var response = await SendAsync(() =>
        {
            var request = new HttpRequestMessage(HttpMethod.Put, Url(DatasetPath(path)))
            {
                // mvsMF-compat: put-json-is-rename — Content-Type application/json turns a PUT into a rename, so a
                // write only ever sends text/plain or application/octet-stream.
                Content = new ByteArrayContent(body) { Headers = { ContentType = new MediaTypeHeaderValue(contentType) } },
            };
            request.Headers.Add("X-IBM-Data-Type", dataType);
            // mvsMF-compat: etag — the stamp of the member as written is the one the next If-Match must carry (the
            // pre-save stamp fails), so every write asks for it. If-Match goes as the host gave it, unquoted.
            request.Headers.Add(ReturnEtagHeader, "true");
            if (ifMatch is { Length: > 0 }) request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
            return request;
        }, what, idle, cancellationToken);
        return EtagOf(response);
    }
```

- [ ] **Step 11: Build the backend and its tests with zero warnings**

Run: `dotnet build tests/LizTerm.Backend.Mvsmf.Tests --no-incremental 2>&1 | grep -E " error |warning" | head`
Expected: no output.

- [ ] **Step 12: Run the backend tests**

Run: `dotnet test tests/LizTerm.Backend.Mvsmf.Tests 2>&1 | grep -E "Passed!|Failed!"`
Expected: `Passed!`. If `Etag_is_asked_for_on_a_text_read_and_returned` fails on `Headers["X-IBM-Return-Etag"]`, check the header name's case in `RecordedRequest.Headers` (it is case-insensitive) and that `Get` adds it.

- [ ] **Step 13: Record the rules in the backend notes**

In `src/LizTerm.Backend.Mvsmf/CLAUDE.md`, replace the bullet `- **Never send \`Content-Type: application/json\` on a \`PUT\`.** mvsMF treats it as a rename.` with:

```markdown
- **A `PUT` with `Content-Type: application/json` is a rename, never a write.** `RenameAsync` is the one method
  that sends it: the new name is the URL, the old one the `{"request":"rename","from-dataset":{…}}` body, `member`
  present for a member rename and absent for a dataset. Writes send only `text/plain` or `application/octet-stream`
  (`put-json-is-rename`). A member rename onto an existing name is 400 reason 7 (`AlreadyExists`,
  `rename-target-exists-400`); a dataset rename onto one is the host's 500 reason 8, a server error quoting it.
- **The stamp (`etag`).** Every read and write sends `X-IBM-Return-Etag: true` and returns the `ETag` header as
  bare text (`EtagOf`: quotes and `W/` stripped, never parsed further). A write's `ifMatch` goes out as
  `If-Match` verbatim, and a 412 is `Conflict`. The stamp of the member as written is the PUT's answer, not the
  pre-save one, so every write asks for it.
- **A create posts the allocation as JSON** (`MvsmfAllocation`: `dsorg` `PS`/`PO`, `recfm` folded, `alcunit`
  `TRK`/`CYL`, `dirblk` only for `PO`) after `DatasetAllocation.Problems()` passes. The host answers every
  allocation failure with the same 500, category 8, rc 900 (`create-failure-is-one-500`), mapped to
  `CannotAllocate`. `DeleteAsync` takes a dataset path as well as a member.
```

In the same file's "Classify errors" bullet, after `MvsmfErrors` is the one place that mapping lives.`, add: `A 412 is \`Conflict\`; category 8 with rc 900 is \`CannotAllocate\`; a 400 in category 6 with reason 7 is \`AlreadyExists\`.`

- [ ] **Step 14: Commit**

```bash
git add src/LizTerm.Backend.Mvsmf tests/LizTerm.Backend.Mvsmf.Tests
git commit -m "mvsMF backend: create, rename, dataset delete, and the ETag on reads and writes

A create posts the allocation as JSON and the host's one 500 is
CannotAllocate. A rename is the one JSON PUT. Reads and writes ask for
the stamp; a write sends If-Match as given and a 412 is Conflict.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 4: App — the fake host, and the App compiled against the new contract

The browser's behaviour does not change in this PR: it never sends a stamp and never calls the three operations. The App compiles against the new return types, and the fake every App test stands on learns the new contract so PR 2 can test against it.

**Files:**
- Modify: `tests/LizTerm.App.Tests/Fakes/FakeHostFileService.cs`
- Modify: `tests/LizTerm.App.Tests/HostFiles/FakeHostFileServiceTests.cs`
- Modify: `src/LizTerm.App/ViewModels/MvsmfBrowserViewModel.Downloads.cs:136-138`
- Modify: `src/LizTerm.App/ViewModels/MvsmfBrowserViewModel.Uploads.cs:203,213,294,301`
- Modify: `tests/LizTerm.Integration.Tests/LiveMvsmfTests.cs:110,112` (the existing round trip's call and assert)
- Modify: `tests/CLAUDE.md`

**Interfaces:**
- Produces (the App fake): `Dictionary<string, string> Etags` (path → stamp); `List<string?> IfMatches`; every write stamps its path `stamp-N` and returns it; a write whose `ifMatch` is not the path's current stamp throws `Conflict`; `CreateDatasetAsync` logs `create:<dsn>`, throws `CannotAllocate` for a name already in `Datasets`, else adds a `HostFileEntry` with the allocation's DSORG/RECFM/LRECL/BLKSIZE and, for a PDS, an empty `Members` list; `RenameAsync` logs `rename:<from>:<new>`, moves the member (or the dataset with its members, contents and stamps), throwing `NotFound` for a missing source and `AlreadyExists` for a member target that exists; `DeleteAsync` of a dataset logs `delete:<dsn>` and removes it with everything under it. `Failures` keys: `create:<dsn>`, `rename:<from>:<new>`, `delete:<dsn>`.

- [ ] **Step 1: Write the failing fake tests**

Add to `tests/LizTerm.App.Tests/HostFiles/FakeHostFileServiceTests.cs`:

```csharp
    [Fact]
    public async Task Writes_stamp_their_target_and_a_stale_stamp_is_a_conflict()
    {
        var token = TestContext.Current.CancellationToken;
        var host = new FakeHostFileService();
        host.AddDataset("A.CNTL");
        var one = HostPath.ForMember("A.CNTL", "ONE");

        var first = await host.WriteTextAsync(one, ["X"], cancellationToken: token);
        var read = await host.ReadTextAsync(one, cancellationToken: token);
        var second = await host.WriteTextAsync(one, ["Y"], ifMatch: first, token);

        Assert.Equal("stamp-1", first);
        Assert.Equal("stamp-1", read.Etag);
        Assert.Equal("stamp-2", second);
        var ex = await Assert.ThrowsAsync<HostFileException>(() => host.WriteTextAsync(one, ["Z"], ifMatch: first, token));
        Assert.Equal(HostFileErrorKind.Conflict, ex.Kind);
        Assert.Equal(new[] { "Y" }, host.Text["A.CNTL(ONE)"]);
        Assert.Equal(new string?[] { null, "stamp-1", "stamp-1" }, host.IfMatches);
    }

    [Fact]
    public async Task A_create_adds_the_dataset_and_a_second_create_cannot_allocate()
    {
        var token = TestContext.Current.CancellationToken;
        var host = new FakeHostFileService();
        var pds = HostPath.ForDataset("A.NEW");
        var allocation = new DatasetAllocation(DatasetOrganization.Partitioned, "FB", 80, 3120, SpaceUnit.Tracks, 1, 1, 2);

        await host.CreateDatasetAsync(pds, allocation, token);

        var entry = Assert.Single(host.Datasets);
        Assert.Equal("A.NEW", entry.Name);
        Assert.Equal("PO", entry.Attributes!.Dsorg);
        Assert.Equal("FB", entry.Attributes.Recfm);
        Assert.Equal(80, entry.Attributes.Lrecl);
        Assert.Equal(3120, entry.Attributes.Blksize);
        Assert.Empty(host.Members["A.NEW"]);
        var ex = await Assert.ThrowsAsync<HostFileException>(() => host.CreateDatasetAsync(pds, allocation, token));
        Assert.Equal(HostFileErrorKind.CannotAllocate, ex.Kind);
        Assert.Equal(new[] { "create:A.NEW", "create:A.NEW" }, host.CallsSnapshot());
    }

    [Fact]
    public async Task A_member_rename_moves_the_member_its_content_and_its_stamp()
    {
        var token = TestContext.Current.CancellationToken;
        var host = new FakeHostFileService();
        host.AddDataset("A.CNTL", members: ["ONE", "TWO"]);
        var one = HostPath.ForMember("A.CNTL", "ONE");
        await host.WriteTextAsync(one, ["X"], cancellationToken: token);

        await host.RenameAsync(one, "THREE", token);

        Assert.Equal(new[] { "THREE", "TWO" }, host.Members["A.CNTL"]);
        Assert.Equal(new[] { "X" }, host.Text["A.CNTL(THREE)"]);
        Assert.Equal("stamp-1", host.Etags["A.CNTL(THREE)"]);
        Assert.False(host.Text.ContainsKey("A.CNTL(ONE)"));
        var missing = await Assert.ThrowsAsync<HostFileException>(() => host.RenameAsync(one, "FOUR", token));
        Assert.Equal(HostFileErrorKind.NotFound, missing.Kind);
        var exists = await Assert.ThrowsAsync<HostFileException>(() => host.RenameAsync(HostPath.ForMember("A.CNTL", "THREE"), "TWO", token));
        Assert.Equal(HostFileErrorKind.AlreadyExists, exists.Kind);
        Assert.Contains("rename:A.CNTL(ONE):THREE", host.CallsSnapshot());
    }

    [Fact]
    public async Task A_dataset_rename_and_delete_carry_everything_under_it()
    {
        var token = TestContext.Current.CancellationToken;
        var host = new FakeHostFileService();
        host.AddDataset("A.CNTL", members: ["ONE"]);
        await host.WriteTextAsync(HostPath.ForMember("A.CNTL", "ONE"), ["X"], cancellationToken: token);

        await host.RenameAsync(HostPath.ForDataset("A.CNTL"), "A.JCL", token);

        Assert.Equal("A.JCL", Assert.Single(host.Datasets).Name);
        Assert.Equal(new[] { "ONE" }, host.Members["A.JCL"]);
        Assert.Equal(new[] { "X" }, host.Text["A.JCL(ONE)"]);
        Assert.Equal("stamp-1", host.Etags["A.JCL(ONE)"]);
        Assert.False(host.Members.ContainsKey("A.CNTL"));

        await host.DeleteAsync(HostPath.ForDataset("A.JCL"), token);

        Assert.Empty(host.Datasets);
        Assert.Empty(host.Members);
        Assert.Empty(host.Text);
        Assert.Empty(host.Etags);
        var gone = await Assert.ThrowsAsync<HostFileException>(() => host.RenameAsync(HostPath.ForDataset("A.JCL"), "A.X", token));
        Assert.Equal(HostFileErrorKind.NotFound, gone.Kind);
    }
```

In the existing `Writes_add_members_and_deletes_remove_them`, change `await host.WriteTextAsync(two, ["X"], token);` to `await host.WriteTextAsync(two, ["X"], cancellationToken: token);` and `Assert.Equal(new[] { "X" }, await host.ReadTextAsync(two, cancellationToken: token));` to `Assert.Equal(new[] { "X" }, (await host.ReadTextAsync(two, cancellationToken: token)).Lines);`. In `A_store_transform_alters_what_a_text_write_keeps`, `await host.WriteTextAsync(one, ["X", "Y"], token);` → `cancellationToken: token`.

- [ ] **Step 2: Update the App fake**

In `tests/LizTerm.App.Tests/Fakes/FakeHostFileService.cs`:

Update the class summary's call list to `… writebinary:<path>, delete:<path>, create:<dsn>, rename:<from>:<new>, info, signout …`. Add fields and properties after `Binary`:

```csharp
    /// <summary>HostPath.ToString() → the stamp of the last write, <c>stamp-N</c>; a read returns it and a write
    /// with another <c>ifMatch</c> is a conflict.</summary>
    public Dictionary<string, string> Etags { get; } = [];
    /// <summary>Every write's <c>ifMatch</c>, in order.</summary>
    public List<string?> IfMatches { get; } = [];
    private int _writes;
```

Replace `ReadTextAsync`, `ReadBinaryAsync`, `WriteTextAsync`, `WriteBinaryAsync` and `DeleteAsync` with:

```csharp
    public async Task<HostTextRead> ReadTextAsync(HostPath path, IProgress<long>? progress = null, CancellationToken cancellationToken = default)
    {
        await EnterAsync($"readtext:{path}", $"readtext:{path}", cancellationToken);
        try
        {
            List<string> lines;
            string? etag;
            lock (_lock)
            {
                lines = Text.TryGetValue(path.ToString(), out var found) ? [.. found] : throw Missing(path);
                etag = Etags.GetValueOrDefault(path.ToString());
            }
            progress?.Report(lines.Sum(l => l.Length + 1));
            return new HostTextRead(lines, etag);
        }
        finally { Leave(); }
    }

    public async Task<HostBinaryRead> ReadBinaryAsync(HostPath path, Stream destination, IProgress<long>? progress = null, CancellationToken cancellationToken = default)
    {
        await EnterAsync($"readbinary:{path}", $"readbinary:{path}", cancellationToken);
        try
        {
            byte[] bytes;
            string? etag;
            lock (_lock)
            {
                bytes = Binary.TryGetValue(path.ToString(), out var found) ? found : throw Missing(path);
                etag = Etags.GetValueOrDefault(path.ToString());
            }
            await destination.WriteAsync(bytes, cancellationToken);
            progress?.Report(bytes.Length);
            return new HostBinaryRead(bytes.Length, etag);
        }
        finally { Leave(); }
    }

    public async Task<string?> WriteTextAsync(HostPath path, IReadOnlyList<string> lines, string? ifMatch = null, CancellationToken cancellationToken = default)
    {
        await EnterAsync($"writetext:{path}:{lines.Count}", $"writetext:{path}", cancellationToken);
        try
        {
            lock (_lock)
            {
                CheckStamp(path, ifMatch);
                Text[path.ToString()] = StoreTransform?.Invoke(path.ToString(), lines) ?? [.. lines];
                AddMember(path);
                return Stamp(path);
            }
        }
        finally { Leave(); }
    }

    public async Task<string?> WriteBinaryAsync(HostPath path, Stream source, string? ifMatch = null, CancellationToken cancellationToken = default)
    {
        await EnterAsync($"writebinary:{path}", $"writebinary:{path}", cancellationToken);
        try
        {
            using var copy = new MemoryStream();
            await source.CopyToAsync(copy, cancellationToken);
            lock (_lock)
            {
                CheckStamp(path, ifMatch);
                Binary[path.ToString()] = copy.ToArray();
                AddMember(path);
                return Stamp(path);
            }
        }
        finally { Leave(); }
    }

    public async Task CreateDatasetAsync(HostPath dataset, DatasetAllocation allocation, CancellationToken cancellationToken = default)
    {
        await EnterAsync($"create:{dataset}", $"create:{dataset}", cancellationToken);
        try
        {
            lock (_lock)
            {
                if (Datasets.Any(d => d.Name == dataset.Dataset))
                    throw new HostFileException(HostFileErrorKind.CannotAllocate, $"{dataset}: the host could not allocate it (it may already exist, there may be no space, or you may not be authorized).", 7, "Dynamic allocation Error");
                var partitioned = allocation.Organization == DatasetOrganization.Partitioned;
                Datasets.Add(new HostFileEntry(dataset.Dataset, HostFileEntryKind.Dataset,
                    new DatasetAttributes(partitioned ? "PO" : "PS", allocation.FoldedRecfm, allocation.Lrecl, allocation.Blksize, "PUB000")));
                if (partitioned) Members[dataset.Dataset] = [];
            }
        }
        finally { Leave(); }
    }

    public async Task RenameAsync(HostPath from, string newName, CancellationToken cancellationToken = default)
    {
        await EnterAsync($"rename:{from}:{newName}", $"rename:{from}:{newName}", cancellationToken);
        try
        {
            lock (_lock)
            {
                if (from.Member is { } member) RenameMember(from, member, HostPath.ForMember(from.Dataset, newName));
                else RenameDataset(from, HostPath.ForDataset(newName));
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
                if (path.Member is { } member)
                {
                    if (Members.TryGetValue(path.Dataset, out var names)) names.Remove(member);
                    Forget(path.ToString());
                }
                else
                {
                    Datasets.RemoveAll(d => d.Name == path.Dataset);
                    Members.Remove(path.Dataset);
                    foreach (var key in KeysUnder(path.Dataset)) Forget(key);
                }
            }
        }
        finally { Leave(); }
    }
```

Add these private helpers before `AddMember`:

```csharp
    /// <summary>A write's ifMatch must be the path's current stamp; a stamp the fake never issued, or an old one,
    /// is a conflict, as the host answers 412.</summary>
    private void CheckStamp(HostPath path, string? ifMatch)
    {
        if (ifMatch is null) return;
        if (Etags.GetValueOrDefault(path.ToString()) != ifMatch)
            throw new HostFileException(HostFileErrorKind.Conflict, $"{path}: changed on the host since it was read.", 10);
    }

    private string Stamp(HostPath path) => Etags[path.ToString()] = $"stamp-{++_writes}";

    private void RenameMember(HostPath from, string member, HostPath to)
    {
        if (!Members.TryGetValue(from.Dataset, out var names) || !names.Contains(member)) throw Missing(from);
        if (names.Contains(to.Member!))
            throw new HostFileException(HostFileErrorKind.AlreadyExists, $"Rename {from} to {to.Member}: a member of that name already exists.", 7);
        names[names.IndexOf(member)] = to.Member!;
        Move(from.ToString(), to.ToString());
    }

    private void RenameDataset(HostPath from, HostPath to)
    {
        var index = Datasets.FindIndex(d => d.Name == from.Dataset);
        if (index < 0) throw new HostFileException(HostFileErrorKind.NotFound, $"Rename {from} to {to.Dataset}: not found.", 4);
        Datasets[index] = Datasets[index] with { Name = to.Dataset };
        if (Members.Remove(from.Dataset, out var names)) Members[to.Dataset] = names;
        // KeysUnder includes the dataset's own key (a sequential dataset's content), so one loop moves everything.
        foreach (var key in KeysUnder(from.Dataset)) Move(key, to.Dataset + key[from.Dataset.Length..]);
    }

    /// <summary>Every content or stamp key that is the dataset itself or one of its members.</summary>
    private List<string> KeysUnder(string dataset) =>
        Text.Keys.Concat(Binary.Keys).Concat(Etags.Keys).Distinct()
            .Where(key => key == dataset || key.StartsWith(dataset + "(", StringComparison.Ordinal)).ToList();

    private void Move(string from, string to)
    {
        if (Text.Remove(from, out var text)) Text[to] = text;
        if (Binary.Remove(from, out var bytes)) Binary[to] = bytes;
        if (Etags.Remove(from, out var etag)) Etags[to] = etag;
    }

    private void Forget(string key)
    {
        Text.Remove(key);
        Binary.Remove(key);
        Etags.Remove(key);
    }
```

- [ ] **Step 3: Compile the App against the new contract**

In `src/LizTerm.App/ViewModels/MvsmfBrowserViewModel.Downloads.cs`, in `DownloadOneAsync`:

```csharp
            var result = await _connection.RunAsync(service => HostFileTransfer.DownloadAsync(service, path, file, options, progress, token));
            progress.Close();
            Show($"✓ Done · {Bytes(result.BytesWritten)} bytes");
            return true;
```

In `src/LizTerm.App/ViewModels/MvsmfBrowserViewModel.Uploads.cs`, the four transfer calls:

- line 203: `return HostFileTransfer.UploadTextAsync(service, path, check, verify, cancellationToken: token);`
- line 213: `return HostFileTransfer.UploadBinaryAsync(service, path, row.LocalPath, cancellationToken: token);`
- line 294: `var outcome = await _connection.RunAsync(service => HostFileTransfer.UploadTextAsync(service, dataset.Path, check, verify, cancellationToken: token));`
- line 301: `await _connection.RunAsync(service => HostFileTransfer.UploadBinaryAsync(service, dataset.Path, file, cancellationToken: token));`

Nothing else in App calls the changed members (`grep -rn "ReadTextAsync\|ReadBinaryAsync\|WriteTextAsync\|WriteBinaryAsync" src/LizTerm.App` lists no other file).

The integration tests are in the solution build too, so fix the existing live test now (its new test is Task 5). In `tests/LizTerm.Integration.Tests/LiveMvsmfTests.cs`, `Round_trips_a_scratch_member_and_deletes_it`:
- `var outcome = await HostFileTransfer.UploadTextAsync(service, path, checkedText, verify: true, ct);` → `verify: true, cancellationToken: ct);`
- `Assert.Equal(UploadOutcome.Matches, outcome);` → `Assert.Equal(UploadVerification.Matches, outcome.Verification);`

- [ ] **Step 4: Build the whole solution with zero warnings**

Run: `dotnet build LizTerm.slnx --no-incremental 2>&1 | grep -E " error |warning" | head`
Expected: no output. If the `HostFileConnection.RunAsync` overload for a `Task<string?>` result is ambiguous at line 213 or 301, the fix is the same as the existing `Task<UploadOutcome>` call: `RunAsync` already has a generic overload, so a `string?` result resolves; report anything else.

- [ ] **Step 5: Run the App and Core tests**

Run: `dotnet test tests/LizTerm.App.Tests 2>&1 | grep -E "Passed!|Failed!"` (expect `Passed!`; every existing browser test is unchanged in behaviour).
Run: `dotnet test tests/LizTerm.Core.Tests 2>&1 | grep -E "Passed!|Failed!"` (expect `Passed!`).

- [ ] **Step 6: Record the fake's contract in the test notes**

In `tests/CLAUDE.md`, in the `FakeHostFileService` (`Fakes/`) bullet, after the sentence ending `makes that call throw.`, add:

```markdown
  It also plays the manage contract: every write stamps its path `stamp-N` (`Etags`, returned by reads and
  writes; `IfMatches` keeps each write's `ifMatch`), and a write whose `ifMatch` is not the current stamp is
  `Conflict`; `create:<dsn>` adds a dataset with the allocation's attributes (`CannotAllocate` when the name
  exists); `rename:<from>:<new>` moves a member with its content and stamp (`NotFound`, `AlreadyExists`) or a
  dataset with everything under it; `delete:<dsn>` removes a dataset with everything under it.
```

In the Core.Tests fake's bullet (`FakeHostFileService` (`HostFiles/`)), append: `Every write stamps its path \`write-N\` (\`Etags\`) and records its \`ifMatch\` (\`IfMatches\`); create and rename are logged only.`

- [ ] **Step 7: Commit**

```bash
git add src/LizTerm.App tests/LizTerm.App.Tests tests/LizTerm.Integration.Tests tests/CLAUDE.md
git commit -m "App: compile against the manage contract; the fake host learns it

The browser's behaviour is unchanged. The fake stamps every write,
answers a stale ifMatch with Conflict, and plays create, rename and
dataset delete for the browser tests to come.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 5: The live round trip

**Files:**
- Modify: `tests/LizTerm.Integration.Tests/LiveMvsmfTests.cs`
- Modify: `docs/development.md:177-183` (the "Live mvsMF tests" paragraph) and the `LIZTERM_MVSMF_SCRATCH_PDS` row near line 147

**Interfaces:**
- Consumes: Task 3's backend.

- [ ] **Step 1: Confirm the existing live test already compiles**

Task 4 changed `Round_trips_a_scratch_member_and_deletes_it` for the new upload signature and outcome. Run `dotnet build tests/LizTerm.Integration.Tests 2>&1 | grep -c " error "` and expect `0` before adding to the file.

- [ ] **Step 2: Add the round trip**

Add to `LiveMvsmfTests`, after `Round_trips_a_scratch_member_and_deletes_it`:

```csharp
    /// <summary>Create, stamp, rename and delete under the test user's own HLQ, which RAKF grants in full; both
    /// names are deleted on the way out whatever happened.</summary>
    [Fact(Timeout = LiveTimeout)]
    public async Task Creates_renames_and_deletes_a_scratch_dataset_with_etag_checks()
    {
        var live = Require();
        var ct = TestContext.Current.CancellationToken;
        using var service = Connect(live);
        var suffix = DateTime.UtcNow.ToString("HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        var created = HostPath.ForDataset($"{live.Credentials.Userid}.LIZITEST.T{suffix}");
        var renamed = HostPath.ForDataset($"{live.Credentials.Userid}.LIZITEST.R{suffix}");
        try
        {
            await service.CreateDatasetAsync(created,
                new DatasetAllocation(DatasetOrganization.Partitioned, "FB", 80, 3120, SpaceUnit.Tracks, 1, 1, 1), ct);
            var listed = Assert.Single((await service.ListDatasetsAsync(created.Dataset, HostListRequest.All, ct)).Entries);
            Assert.Equal("PO", listed.Attributes!.Dsorg);
            Assert.Equal("FB", listed.Attributes.Recfm);
            Assert.Equal(80, listed.Attributes.Lrecl);

            var member = created.WithMember("ONE");
            var first = await service.WriteTextAsync(member, ["//ONE JOB (ACCT),LIZTERM"], cancellationToken: ct);
            Assert.NotNull(first);
            var read = await service.ReadTextAsync(member, cancellationToken: ct);
            Assert.Equal(first, read.Etag);

            var second = await service.WriteTextAsync(member, ["//ONE JOB (ACCT),LIZTERM", "//*"], ifMatch: read.Etag, ct);
            Assert.NotNull(second);
            Assert.NotEqual(first, second);
            var conflict = await Assert.ThrowsAsync<HostFileException>(() => service.WriteTextAsync(member, ["//X"], ifMatch: first, ct));
            Assert.Equal(HostFileErrorKind.Conflict, conflict.Kind);
            Assert.Equal(2, (await service.ReadTextAsync(member, cancellationToken: ct)).Lines.Count);

            await service.RenameAsync(member, "TWO", ct);
            Assert.Equal(new[] { "TWO" }, (await service.ListMembersAsync(created, HostListRequest.All, ct)).Entries.Select(e => e.Name));
            var exists = await Assert.ThrowsAsync<HostFileException>(() => service.RenameAsync(created.WithMember("TWO"), "TWO", ct));
            Assert.Equal(HostFileErrorKind.AlreadyExists, exists.Kind);

            await service.RenameAsync(created, renamed.Dataset, ct);
            Assert.Empty((await service.ListDatasetsAsync(created.Dataset, HostListRequest.All, ct)).Entries);
            Assert.Equal(new[] { "TWO" }, (await service.ListMembersAsync(renamed, HostListRequest.All, ct)).Entries.Select(e => e.Name));

            await service.DeleteAsync(renamed, ct);
            Assert.Empty((await service.ListDatasetsAsync(renamed.Dataset, HostListRequest.All, ct)).Entries);
            var gone = await Assert.ThrowsAsync<HostFileException>(() => service.DeleteAsync(renamed, ct));
            Assert.Equal(HostFileErrorKind.NotFound, gone.Kind);
        }
        finally
        {
            foreach (var name in new[] { created, renamed })
            {
                try
                {
                    await service.DeleteAsync(name, CancellationToken.None);
                }
                catch (HostFileException)
                {
                    // Not there, which is the expected case.
                }
            }
        }
    }
```

Check `HostPath.WithMember` exists (`grep -n "WithMember" src/LizTerm.Core/HostFiles/HostPath.cs`); the upload slice uses it, so it does.

- [ ] **Step 3: Update the class summary and the development docs**

Change the class's `<summary>` first sentence to: `Runs only when LIZTERM_MVSMF_URL, LIZTERM_MVSMF_USER, LIZTERM_MVSMF_PASSWORD and LIZTERM_MVSMF_SCRATCH_PDS are all set. Writes, verifies and deletes the member LIZITEST in the scratch PDS, and allocates, renames and deletes datasets named &lt;user&gt;.LIZITEST.T&lt;hhmmss&gt; and .R&lt;hhmmss&gt;.`

In `docs/development.md`, in the variables table, change the `LIZTERM_MVSMF_SCRATCH_PDS` row's description to: `A PDS the live mvsMF tests may write the member \`LIZITEST\` into and delete it from. The tests also allocate and delete datasets named \`<LIZTERM_MVSMF_USER>.LIZITEST.*\`, under the user's own high-level qualifier.` And in the "Live mvsMF tests" paragraph, after `delete it, sign out and check the host refuses the ended session;`, insert: `create a small PDS under the user's own qualifier, write a member, check the host's stamp (a stale one is refused), rename the member and the dataset, and delete it;`.

- [ ] **Step 4: Run the live lane**

In the shell with the four variables:

Run: `dotnet test tests/LizTerm.Integration.Tests --filter "FullyQualifiedName~LiveMvsmfTests" 2>&1 | grep -E "Passed!|Failed!|Skipped|error"`
Expected: `Passed!` with 7 tests, 0 skipped. If the new test fails on `Assert.Equal(first, read.Etag)`, the host's write stamp and read stamp differ: stop and report the two values, since the compatibility log entry (Task 6) asserts they match.

Then confirm nothing is left: `curl -sS --netrc-file ~/.mvsmf-netrc "$LIZTERM_MVSMF_URL/restfiles/ds?dslevel=IBMUSER.LIZITEST.**"; echo` prints `{"items":[],"returnedRows":0,"JSONversion":1}`.

- [ ] **Step 5: Commit**

```bash
git add tests/LizTerm.Integration.Tests/LiveMvsmfTests.cs docs/development.md
git commit -m "Live test: create, stamp, rename and delete a scratch dataset

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 6: The compatibility log

**Files:**
- Modify: `docs/mvsmf-compatibility.md`

- [ ] **Step 1: Rewrite `etag-unused` as `etag`**

Replace the whole `### \`etag-unused\` (log only)` entry with:

```markdown
### `etag`

- **Docs and source:** `X-IBM-Return-Etag: true` returns an `ETag` on a read and on a write (none is sent without
  the header); `If-Match` on a write is checked before the member is opened, and a mismatch is 412
  `{"category":6,"rc":8,"reason":10,"message":"The resource was modified since the supplied ETag was created"}`
  with nothing written. The stamp of the member *as written* is what the PUT answers, and it is what the next
  `If-Match` must carry: the pre-save stamp fails, because the write normalises what it stores.
- **Observed (2026-09-19):** 1.1.0 does all of it. The stamp is 16 hex digits, unquoted, the same for a text and
  a binary read, and a read after a write answers the write's stamp (`read-etag`, `write-etag-204`, `write-412`;
  the live round trip checks the write-then-read equality).
- **LizTerm:** every read and write asks for the stamp and hands it back as bare text (quotes and `W/` stripped,
  never parsed further); a write sends the caller's `ifMatch` as `If-Match` verbatim, and a 412 is
  `HostFileErrorKind.Conflict`. The browser's use of it is in the user guide.
```

- [ ] **Step 2: Add the two log-only entries**

After the `etag` entry, add:

```markdown
### `create-failure-is-one-500` (log only)

- **Source and observed:** `POST restfiles/ds/{name}` answers every allocation failure — the name already exists,
  no space, a DCB the volume cannot hold, no authority — with the same
  `500 {"category":8,"rc":900,"reason":7,"message":"Dynamic allocation Error"}`, byte for byte what real z/OSMF
  sends (mvsMF #317, #329; `create-dynalloc-500` is the "already exists" case). A missing field is 400 reason 3.
- **LizTerm:** reports it as `CannotAllocate`, whose sentence names the three causes, since the host cannot.

### `rename-target-exists-400` (log only)

- **Source and observed:** a member rename (`PUT …({new})` with the JSON rename body) onto a name that exists is
  400 reason 7 "Rename target already exists" (`rename-member-exists`); a missing source is 404 reason 5. A
  *dataset* rename onto an existing name is not checked first: IDCAMS ALTER refuses it and the host answers 500
  reason 8 "Rename operation failed", the same as any other rename failure.
- **LizTerm:** the member case is `AlreadyExists`; the dataset case is a server error quoting the host.
```

- [ ] **Step 3: Reword `put-json-is-rename`**

Replace the entry's `**LizTerm:**` line with: `- **LizTerm:** \`RenameAsync\` is the one method that sends \`application/json\` on a \`PUT\`, on purpose; writes send only \`text/plain\` or \`application/octet-stream\`, pinned by \`Put_json_is_rename_so_no_write_ever_sends_json\`.`

- [ ] **Step 4: Point the "Tested build" table at the recording date**

In the table at the top, change the `Host` row to `| Host | MVS/CE, HTTPD, probed 2026-09-18; the manage operations recorded 2026-09-19 |`.

- [ ] **Step 5: Confirm every tag has code and a test**

Run:

```bash
for tag in etag create-failure-is-one-500 rename-target-exists-400 put-json-is-rename; do
  printf '%s: code %s, tests %s\n' "$tag" "$(grep -rl "mvsMF-compat: $tag" src | wc -l | tr -d ' ')" "$(grep -rli "$(echo $tag | tr '-' '_')" tests/LizTerm.Backend.Mvsmf.Tests/*.cs | wc -l | tr -d ' ')"
done
grep -rn "etag-unused" src docs tests
```

Expected: each tag shows at least one code file and one test file (the log-only entries are referenced from the backend on purpose, at the create and rename methods and in `MvsmfErrors`), and the last `grep` prints nothing.

- [ ] **Step 6: Commit**

```bash
git add docs/mvsmf-compatibility.md
git commit -m "Compatibility log: etag honoured, the one create 500, the rename-target 400

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 7: Full verification and the pull request

**Files:** none new.

- [ ] **Step 1: Zero warnings and the full suite**

Run: `dotnet build LizTerm.slnx --no-incremental 2>&1 | grep -c " warning "` (expect `0`).
Run: `dotnet test LizTerm.slnx 2>&1 | grep -E "Passed!|Failed!"` (expect every project `Passed!`; the live tests skip without the variables).

- [ ] **Step 2: The live lane once more, on the final tree**

In the shell with the four variables:
Run: `dotnet test tests/LizTerm.Integration.Tests --filter "FullyQualifiedName~LiveMvsmfTests" 2>&1 | grep -E "Passed!|Failed!|Skipped"`
Expected: `Passed!`, 7 tests, 0 skipped.

- [ ] **Step 3: Dependency, header and tag checks**

Run: `grep -rn "mvsMF\|Mvsmf\|z/OSMF\|zosmf\|LtpaToken\|If-Match\|X-IBM" src/LizTerm.Core --include='*.cs'` (expect no output; Core names no product and no header).
Run: `dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~RepositoryHeadersTests" 2>&1 | grep -E "Passed!|Failed!"` (expect `Passed!`).
Run: `git grep -n "UploadOutcome.Matches\|UploadOutcome.NotChecked\|NotSupportedException" src tests` (expect no output; the statics and the dataset-delete refusal are gone).
Run: `git grep -n "application/json" src/LizTerm.Backend.Mvsmf/MvsmfFileService.cs` (expect the `JsonContentType` constant and its two uses in `CreateDatasetAsync` and `RenameAsync` only).

- [ ] **Step 4: Push and open the PR**

Follow `superpowers:finishing-a-development-branch`. Push `claude/manage-slice-rename-delete-create-7a2f96` and open a PR against `main` titled `mvsMF: create, rename, dataset delete and the ETag, backend and contract` with a body that says: this is PR 1 of the manage slice on #17 (link the spec); the three operations and the stamp on `IHostFileService`; `DatasetAllocation` and its local rules; the backend's JSON create, JSON-PUT rename, dataset delete, `X-IBM-Return-Etag` on every read and write and `If-Match` verbatim; the three error kinds and how mvsMF answers each (one 500 for every allocation failure; 400 reason 7 for a member rename onto an existing name; 412 for a stale stamp); the eleven fixtures; the live round trip; the compatibility log entries; and that the browser's behaviour is unchanged until PR 2. End the body with `🤖 Generated with [Claude Code](https://claude.com/claude-code)`.

- [ ] **Step 5: Hand over**

Report the PR URL. The status comment on #17 is posted after both PRs merge.

## Self-review notes

- Spec §4.1 (Core contract): Task 1, with `DatasetAllocation.Problems` returning a per-field dictionary (`AllocationField`) rather than a flat list, so PR 2's form can mark each box; the `RecfmError` rule is the spec's (first letter F, V or U, then B, S, A, M at most once each). `HostFileTransfer` carries the stamp (`DownloadResult`, `UploadOutcome.Etag`), and the two static outcomes are removed because an outcome now carries the stamp.
- Spec §4.2 (backend): Task 3. `EtagOf` strips quotes and `W/` and stops there. `If-Match` is added with `TryAddWithoutValidation`, since the host's unquoted form is not a valid `EntityTagHeaderValue`. The rename's `what` is `Rename FROM to NEW`, so the `AlreadyExists` and `NotFound` sentences name both ends.
- Spec §4.3 (compatibility log): Task 6, with the tags in code at the create, the rename, the read and write paths and in `MvsmfErrors` (Task 3), and tests named after them (`Create_failure_is_one_500…`, `Rename_target_exists_400…`, `Etag_is_asked_for…`, `Put_json_is_rename…`).
- Spec §6 (PR 1 list): items 1–7 map to Tasks 1, 3, 2, 6, 5, 1/3/4 (notes), 4. The fixture names are the spec's eleven.
- Spec §8 (testing): Core rules and the stamp (Task 1), recorded backend (Task 3), live (Task 5), App fake (Task 4).
- Red boundary: after Task 1 only Core builds; Task 2 is fixtures only; Task 3 restores the backend; Task 4 restores the App. Stated in the task graph and in each task's build step.
- Deviation from spec, recorded: the Core fake logs create and rename without modelling them (Core has no listing to model against); the App fake models both, since the browser tests need them. The spec's "a counter per write" stamp is `stamp-N` in the App fake and `write-N` in the Core fake.
- Decision recorded: `CreateDatasetAsync` refuses an allocation that fails its own rules with `ArgumentException` before sending, as `ListDatasetsAsync` refuses a bad pattern; the browser (PR 2) validates first, so the throw is a guard, not a message.
- Not in this PR: the browser's buttons, form, strip input, `EtagMemory`, `HostFileMessages` for the three kinds, the user guide and changelog (all PR 2).
