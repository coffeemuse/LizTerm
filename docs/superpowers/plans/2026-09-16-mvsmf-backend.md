# mvsMF backend (PR 1 of 3) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a host-neutral file-access interface to Core and a new `LizTerm.Backend.Mvsmf` project that implements it against mvsMF, with recorded-exchange tests, live tests, and the compatibility log.

**Architecture:** `LizTerm.Core.HostFiles` holds the interface (`IHostFileService`), the path and entry types, credentials callback, errors, the upload pre-flight (`TextUploadCheck`) and the file side of transfers (`HostFileTransfer`). `LizTerm.Backend.Mvsmf` implements the interface on an `HttpClient` and is the only place that knows mvsMF, including every workaround for the pre-release build. No App changes in this PR.

**Tech Stack:** .NET 10, C# latest, `System.Net.Http` (`SocketsHttpHandler`), `System.Text.Json` source generation, xunit.v3.

**Spec:** `docs/superpowers/specs/2026-09-16-lizterm-mvsmf-dataset-browser-design.md` (read §2, §3.1, §3.4, §3.5, §5, §7, §9 before starting).

## Global Constraints

- Every hand-written `.cs` and `.sh` file starts with the three licence lines (after the shebang in a script):
  `This file is part of LizTerm.` / `Copyright 2026 by CoffeeMuse` / `SPDX-License-Identifier: BSD-3-Clause`.
- `LizTerm.Core` depends on the BCL only and never names mvsMF, Avalonia or b3270 — not even in comments.
- `LizTerm.Backend.Mvsmf` depends on Core only and never references `LizTerm.Backend.B3270` (and vice versa).
- Package versions live only in `Directory.Packages.props`; `PackageReference` entries carry no `Version`. This PR adds no packages.
- Every mvsMF workaround in backend code carries `// mvsMF-compat: <tag>` and has a matching entry in `docs/mvsmf-compatibility.md`; a backend test named after the tag (dashes become underscores, first letter capitalised) pins it. The one exception is `text-write-truncates-silently`, which Core's `TextUploadCheck` tests pin, because Core cannot name mvsMF.
- Credentials never appear in logs, exception messages or `ToString()`, and never on a process command line.
- Text crosses `IHostFileService` as .NET strings; the mvsMF wire encoding is ISO-8859-1 both ways.
- Never send `Content-Type: application/json` on an mvsMF `PUT` (it turns the write into a rename).
- Timeouts: 10 s to connect, 30 s without data, no overall timeout.
- Zero warnings: `dotnet build LizTerm.slnx --no-incremental 2>&1 | grep -c " warning "` prints `0`.
- Commits end with `Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>`.

## Prerequisites

- `docs/development.md`'s live-test variables land here with the tests that read them (Task 11), rather than in PR 3
  as spec §9 lists, so the documentation never trails the code.
- Work on a branch `claude/mvsmf-backend` created from `claude/issue-17-discussion-e2ca93` (which carries the spec and this plan).
- Tasks 5 and 11 need the live host. Robert adds these to `~/.config/lizterm-test.env` (outside the repo) and the
  shell running those steps does `source ~/.config/lizterm-test.env`:
  `LIZTERM_MVSMF_URL=http://10.42.37.209:8080/zosmf`, `LIZTERM_MVSMF_USER`, `LIZTERM_MVSMF_PASSWORD`,
  `LIZTERM_MVSMF_SCRATCH_PDS=MVSCE02.CNTL`. Never echo the password. If the variables are missing, stop and ask.

## File Structure

| File | Responsibility |
|---|---|
| `src/LizTerm.Core/HostFiles/HostPath.cs` | Dataset / member path, name rules, filter rule |
| `src/LizTerm.Core/HostFiles/HostFileEntry.cs` | List entries, dataset attributes, record-format family |
| `src/LizTerm.Core/HostFiles/HostTransferMode.cs` | Text / Binary |
| `src/LizTerm.Core/HostFiles/HostCredentials.cs` | Credentials, request, provider delegate |
| `src/LizTerm.Core/HostFiles/HostFileException.cs` | Error kinds and exception |
| `src/LizTerm.Core/HostFiles/HostServerInfo.cs` | What `/info` reports |
| `src/LizTerm.Core/HostFiles/IHostFileService.cs` | The service interface |
| `src/LizTerm.Core/HostFiles/TextUploadCheck.cs` | Upload pre-flight |
| `src/LizTerm.Core/HostFiles/HostFileTransfer.cs` | Download to file, upload from checked text, verify |
| `src/LizTerm.Core/Security/SslStreamCertificateFetcher.cs` | `SelectPresented` becomes public (Task 9) |
| `src/LizTerm.Backend.Mvsmf/LizTerm.Backend.Mvsmf.csproj` | New project |
| `src/LizTerm.Backend.Mvsmf/MvsmfOptions.cs` | Base URL + pin; URL normalisation |
| `src/LizTerm.Backend.Mvsmf/MvsmfJson.cs` | Wire DTOs, lenient string converter, JSON context |
| `src/LizTerm.Backend.Mvsmf/MvsmfErrors.cs` | Response → `HostFileException` |
| `src/LizTerm.Backend.Mvsmf/IdleTimeout.cs` | 30 s-without-data token |
| `src/LizTerm.Backend.Mvsmf/MvsmfCertificateCheck.cs` | TLS validation with pin |
| `src/LizTerm.Backend.Mvsmf/MvsmfFileService.cs` | `IHostFileService` over HTTP |
| `src/LizTerm.Backend.Mvsmf/CLAUDE.md` | Pitfalls |
| `tools/record-mvsmf-fixture.sh` | Records one exchange as a fixture |
| `tests/LizTerm.Backend.Mvsmf.Tests/*` | New test project, `RecordedHandler`, `Fixture`, `LoopbackHttpsServer`, fixtures |
| `tests/LizTerm.Core.Tests/HostFiles/*` | Core tests and `FakeHostFileService` |
| `tests/LizTerm.Integration.Tests/LiveMvsmfTests.cs` | Live tests |
| `docs/mvsmf-compatibility.md` | The compatibility log |
| `CLAUDE.md`, `src/LizTerm.Core/CLAUDE.md`, `tests/CLAUDE.md`, `docs/development.md` | Rules and docs |

---

### Task 1: Core paths and entries

**Files:**
- Create: `src/LizTerm.Core/HostFiles/HostPath.cs`, `src/LizTerm.Core/HostFiles/HostFileEntry.cs`, `src/LizTerm.Core/HostFiles/HostTransferMode.cs`
- Test: `tests/LizTerm.Core.Tests/HostFiles/HostPathTests.cs`, `tests/LizTerm.Core.Tests/HostFiles/DatasetAttributesTests.cs`

**Interfaces:**
- Produces: `HostPathKind { Dataset, Member }`; `HostPath` with `Kind`, `Dataset`, `Member`, `ForDataset(string)`, `ForMember(string, string)`, `WithMember(string)`, `TryParse(string?, out HostPath?, out string?)`, `DatasetNameError(string)`, `MemberNameError(string)`, `DatasetPatternError(string)`, `MaxDatasetLength`, `MaxMemberLength`, `ToString()`; `HostFileEntryKind { Dataset, Member }`; `RecordFormatFamily { Unknown, Fixed, Variable, Undefined }`; `DatasetAttributes(string? Dsorg, string? Recfm, int? Lrecl, int? Blksize, string? Volume)` with `IsPartitioned`, `IsSequential`, `IsSupported`, `RecordFormat`, `UsableLineLength`; `HostFileEntry(string Name, HostFileEntryKind Kind, DatasetAttributes? Attributes = null)`; `HostTransferMode { Text, Binary }`.

- [ ] **Step 1: Write the failing tests**

`tests/LizTerm.Core.Tests/HostFiles/HostPathTests.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.Core.HostFiles;

namespace LizTerm.Core.Tests.HostFiles;

public class HostPathTests
{
    [Fact]
    public void A_dataset_is_trimmed_and_folded_to_upper_case()
    {
        var path = HostPath.ForDataset("  sys1.proclib ");
        Assert.Equal(HostPathKind.Dataset, path.Kind);
        Assert.Equal("SYS1.PROCLIB", path.Dataset);
        Assert.Null(path.Member);
        Assert.Equal("SYS1.PROCLIB", path.ToString());
    }

    [Fact]
    public void A_member_prints_in_parentheses()
    {
        var path = HostPath.ForMember("sys1.proclib", "jes2");
        Assert.Equal(HostPathKind.Member, path.Kind);
        Assert.Equal("JES2", path.Member);
        Assert.Equal("SYS1.PROCLIB(JES2)", path.ToString());
        Assert.Equal(path, HostPath.ForDataset("SYS1.PROCLIB").WithMember("JES2"));
    }

    [Theory]
    [InlineData("sys1.proclib", "SYS1.PROCLIB")]
    [InlineData(" sys1.proclib(jes2) ", "SYS1.PROCLIB(JES2)")]
    [InlineData("#$@.A-B(@A1)", "#$@.A-B(@A1)")]
    public void Parses_datasets_and_members(string text, string expected)
    {
        Assert.True(HostPath.TryParse(text, out var path, out var error), error);
        Assert.Equal(expected, path!.ToString());
    }

    [Theory]
    [InlineData("", "Enter a dataset name.")]
    [InlineData("SYS1.PROCLIB(JES2", "A member name must end with ')'.")]
    [InlineData("AAAAAAAA.BBBBBBBB.CCCCCCCC.DDDDDDDD.EEEEEEEE.F", "A dataset name is at most 44 characters.")]
    [InlineData("SYS1..X", "A dataset name cannot have an empty qualifier.")]
    [InlineData("SYS1.ABCDEFGHI", "Qualifier 'ABCDEFGHI' is longer than 8 characters.")]
    [InlineData("SYS1.123", "Qualifier '123' must start with a letter or # $ @.")]
    [InlineData("SYS1.A_B", "Qualifier 'A_B' contains '_', which a dataset name cannot.")]
    [InlineData("SYS1.X()", "Enter a member name.")]
    [InlineData("SYS1.X(TOOLONGNM)", "A member name is at most 8 characters.")]
    [InlineData("SYS1.X(1ABC)", "A member name must start with a letter or # $ @.")]
    [InlineData("SYS1.X(AB-C)", "A member name cannot contain '-'.")]
    public void Rejects_bad_names_with_a_reason(string text, string expected)
    {
        Assert.False(HostPath.TryParse(text, out var path, out var error));
        Assert.Null(path);
        Assert.Equal(expected, error);
    }

    [Fact]
    public void The_factories_throw_the_same_reason()
    {
        var ex = Assert.Throws<ArgumentException>(() => HostPath.ForMember("SYS1.X", "1ABC"));
        Assert.Equal("A member name must start with a letter or # $ @.", ex.Message);
    }

    [Theory]
    [InlineData("sys1.**", null)]
    [InlineData("MVSCE02.C%ST", null)]
    [InlineData("", "Enter a dataset filter.")]
    [InlineData("SYS1.A B", "A filter cannot contain ' '.")]
    [InlineData("AAAAAAAA.BBBBBBBB.CCCCCCCC.DDDDDDDD.EEEEEEEE.*", "A filter is at most 44 characters.")]
    public void Checks_dataset_filters(string pattern, string? expected) =>
        Assert.Equal(expected, HostPath.DatasetPatternError(pattern));
}
```

`tests/LizTerm.Core.Tests/HostFiles/DatasetAttributesTests.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.Core.HostFiles;

namespace LizTerm.Core.Tests.HostFiles;

public class DatasetAttributesTests
{
    [Theory]
    [InlineData("FB", 80, 19040, 80)]
    [InlineData("F", 133, 133, 133)]
    [InlineData("VB", 255, 6233, 251)]
    [InlineData("U", 0, 19069, 19069)]
    [InlineData(null, 80, 800, null)]
    [InlineData("VB", 4, 100, null)]
    [InlineData("FB", 0, 100, null)]
    public void Usable_line_length_follows_the_record_format(string? recfm, int lrecl, int blksize, int? expected) =>
        Assert.Equal(expected, new DatasetAttributes("PS", recfm, lrecl, blksize, null).UsableLineLength);

    [Theory]
    [InlineData("PO", true, false, true)]
    [InlineData("PS", false, true, true)]
    [InlineData("DA", false, false, false)]
    [InlineData(null, false, false, false)]
    public void Only_partitioned_and_sequential_datasets_are_supported(string? dsorg, bool partitioned, bool sequential, bool supported)
    {
        var attributes = new DatasetAttributes(dsorg, "FB", 80, 800, null);
        Assert.Equal(partitioned, attributes.IsPartitioned);
        Assert.Equal(sequential, attributes.IsSequential);
        Assert.Equal(supported, attributes.IsSupported);
    }

    [Fact]
    public void An_unknown_record_format_is_reported_as_such() =>
        Assert.Equal(RecordFormatFamily.Unknown, new DatasetAttributes("PS", "?", 80, 80, null).RecordFormat);
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~HostFiles"`
Expected: build FAILS with `The type or namespace name 'HostFiles' does not exist`.

- [ ] **Step 3: Implement**

`src/LizTerm.Core/HostFiles/HostTransferMode.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

namespace LizTerm.Core.HostFiles;

/// <summary>Text converts between the host's EBCDIC records and local lines; Binary moves the record bytes as they
/// are.</summary>
public enum HostTransferMode { Text, Binary }
```

`src/LizTerm.Core/HostFiles/HostPath.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

namespace LizTerm.Core.HostFiles;

public enum HostPathKind { Dataset, Member }

/// <summary>Where a file lives on the host: a dataset, or a member of a partitioned one. Names are trimmed, folded to
/// upper case and checked against the MVS naming rules when the path is made, so a path that exists is always one
/// the host could accept. A UNIX file system path will arrive as a further kind and factory on this type.</summary>
public sealed record HostPath
{
    public const int MaxDatasetLength = 44;
    public const int MaxMemberLength = 8;
    private const int MaxQualifierLength = 8;

    private HostPath(HostPathKind kind, string dataset, string? member)
    {
        Kind = kind;
        Dataset = dataset;
        Member = member;
    }

    public HostPathKind Kind { get; }
    public string Dataset { get; }
    public string? Member { get; }

    /// <exception cref="ArgumentException">The name breaks a naming rule; the message says which.</exception>
    public static HostPath ForDataset(string dataset) => new(HostPathKind.Dataset, Checked(dataset, DatasetNameError), null);

    /// <exception cref="ArgumentException">Either name breaks a naming rule; the message says which.</exception>
    public static HostPath ForMember(string dataset, string member) =>
        new(HostPathKind.Member, Checked(dataset, DatasetNameError), Checked(member, MemberNameError));

    public HostPath WithMember(string member) => ForMember(Dataset, member);

    /// <summary>Reads <c>DSN</c> or <c>DSN(MEMBER)</c>.</summary>
    public static bool TryParse(string? text, out HostPath? path, out string? error)
    {
        path = null;
        var trimmed = (text ?? "").Trim();
        try
        {
            var open = trimmed.IndexOf('(');
            if (open < 0)
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

    /// <summary>Why <paramref name="name"/> is not a dataset name, or null when it is one. Case and surrounding blanks
    /// are ignored.</summary>
    public static string? DatasetNameError(string name)
    {
        var folded = Fold(name);
        if (folded.Length == 0) return "Enter a dataset name.";
        if (folded.Length > MaxDatasetLength) return $"A dataset name is at most {MaxDatasetLength} characters.";
        foreach (var qualifier in folded.Split('.'))
        {
            if (qualifier.Length == 0) return "A dataset name cannot have an empty qualifier.";
            if (qualifier.Length > MaxQualifierLength) return $"Qualifier '{qualifier}' is longer than {MaxQualifierLength} characters.";
            if (!IsNameStart(qualifier[0])) return $"Qualifier '{qualifier}' must start with a letter or # $ @.";
            foreach (var c in qualifier)
            {
                if (!IsNameStart(c) && !char.IsAsciiDigit(c) && c != '-')
                    return $"Qualifier '{qualifier}' contains '{c}', which a dataset name cannot.";
            }
        }
        return null;
    }

    /// <summary>Why <paramref name="name"/> is not a member name, or null when it is one.</summary>
    public static string? MemberNameError(string name)
    {
        var folded = Fold(name);
        if (folded.Length == 0) return "Enter a member name.";
        if (folded.Length > MaxMemberLength) return $"A member name is at most {MaxMemberLength} characters.";
        if (!IsNameStart(folded[0])) return "A member name must start with a letter or # $ @.";
        foreach (var c in folded)
        {
            if (!IsNameStart(c) && !char.IsAsciiDigit(c)) return $"A member name cannot contain '{c}'.";
        }
        return null;
    }

    /// <summary>Why <paramref name="pattern"/> cannot filter a dataset list, or null when it can. <c>*</c>, <c>**</c>
    /// and <c>%</c> are wildcards.</summary>
    public static string? DatasetPatternError(string pattern)
    {
        var folded = Fold(pattern);
        if (folded.Length == 0) return "Enter a dataset filter.";
        if (folded.Length > MaxDatasetLength) return $"A filter is at most {MaxDatasetLength} characters.";
        foreach (var c in folded)
        {
            if (!IsNameStart(c) && !char.IsAsciiDigit(c) && c is not ('.' or '-' or '*' or '%'))
                return $"A filter cannot contain '{c}'.";
        }
        return null;
    }

    public override string ToString() => Member is null ? Dataset : $"{Dataset}({Member})";

    private static string Fold(string name) => (name ?? "").Trim().ToUpperInvariant();

    private static bool IsNameStart(char c) => char.IsAsciiLetterUpper(c) || c is '#' or '$' or '@';

    private static string Checked(string name, Func<string, string?> rule) =>
        rule(name) is { } error ? throw new ArgumentException(error) : Fold(name);
}
```

`src/LizTerm.Core/HostFiles/HostFileEntry.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

namespace LizTerm.Core.HostFiles;

public enum HostFileEntryKind { Dataset, Member }

public enum RecordFormatFamily { Unknown, Fixed, Variable, Undefined }

/// <summary>One row of a host listing. <paramref name="Attributes"/> is set for datasets only.</summary>
public sealed record HostFileEntry(string Name, HostFileEntryKind Kind, DatasetAttributes? Attributes = null);

/// <summary>What a dataset listing says about a dataset. Every field is optional because hosts leave fields out.</summary>
public sealed record DatasetAttributes(string? Dsorg, string? Recfm, int? Lrecl, int? Blksize, string? Volume)
{
    public bool IsPartitioned => Dsorg is "PO" or "PO-E";

    public bool IsSequential => Dsorg == "PS";

    /// <summary>Whether this release can list, read or write it. VSAM, direct-access and unknown organisations
    /// cannot be.</summary>
    public bool IsSupported => IsPartitioned || IsSequential;

    public RecordFormatFamily RecordFormat => Recfm is { Length: > 0 } recfm
        ? recfm[0] switch
        {
            'F' => RecordFormatFamily.Fixed,
            'V' => RecordFormatFamily.Variable,
            'U' => RecordFormatFamily.Undefined,
            _ => RecordFormatFamily.Unknown,
        }
        : RecordFormatFamily.Unknown;

    /// <summary>The longest text line one record can hold: LRECL for fixed records, LRECL less the four-byte record
    /// descriptor for variable ones, BLKSIZE for undefined ones. Null when the listing does not say.</summary>
    public int? UsableLineLength => RecordFormat switch
    {
        RecordFormatFamily.Fixed => Lrecl is > 0 ? Lrecl : null,
        RecordFormatFamily.Variable => Lrecl is > 4 ? Lrecl - 4 : null,
        RecordFormatFamily.Undefined => Blksize is > 0 ? Blksize : null,
        _ => null,
    };
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~HostFiles"`
Expected: PASS (all).

- [ ] **Step 5: Commit**

```bash
git add src/LizTerm.Core/HostFiles tests/LizTerm.Core.Tests/HostFiles
git commit -m "Add host file paths and dataset attributes to Core

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 2: Core service contract

**Files:**
- Create: `src/LizTerm.Core/HostFiles/HostCredentials.cs`, `src/LizTerm.Core/HostFiles/HostFileException.cs`, `src/LizTerm.Core/HostFiles/HostServerInfo.cs`, `src/LizTerm.Core/HostFiles/IHostFileService.cs`
- Test: `tests/LizTerm.Core.Tests/HostFiles/HostCredentialsTests.cs`

**Interfaces:**
- Consumes: Task 1 types; `LizTerm.Core.Security.PresentedCertificate`.
- Produces:
  - `sealed class HostCredentials(string userid, string password)` with `Userid`, `Password`.
  - `sealed record HostCredentialRequest(bool IsRetry)`.
  - `delegate ValueTask<HostCredentials?> HostCredentialProvider(HostCredentialRequest request, CancellationToken cancellationToken)` — null means the user cancelled.
  - `enum HostFileErrorKind { NotFound, CannotOpen, NotAuthorized, InvalidRequest, Unauthenticated, CertificateRejected, ServerError, Unreachable }`.
  - `sealed class HostFileException(HostFileErrorKind kind, string message, int? reason = null, string? serverMessage = null, PresentedCertificate? certificate = null, Exception? inner = null)` with `Kind`, `Reason`, `ServerMessage`, `Certificate`.
  - `sealed record HostServerInfo(string Product, string ProductVersion, string SystemVersion)`.
  - `interface IHostFileService : IDisposable` with:
    - `Task<HostServerInfo> GetServerInfoAsync(CancellationToken cancellationToken = default)`
    - `Task<IReadOnlyList<HostFileEntry>> ListDatasetsAsync(string pattern, CancellationToken cancellationToken = default)`
    - `Task<IReadOnlyList<HostFileEntry>> ListMembersAsync(HostPath dataset, CancellationToken cancellationToken = default)`
    - `Task<IReadOnlyList<string>> ReadTextAsync(HostPath path, IProgress<long>? progress = null, CancellationToken cancellationToken = default)`
    - `Task<long> ReadBinaryAsync(HostPath path, Stream destination, IProgress<long>? progress = null, CancellationToken cancellationToken = default)`
    - `Task WriteTextAsync(HostPath path, IReadOnlyList<string> lines, CancellationToken cancellationToken = default)`
    - `Task WriteBinaryAsync(HostPath path, Stream source, CancellationToken cancellationToken = default)`
    - `Task DeleteAsync(HostPath path, CancellationToken cancellationToken = default)`

- [ ] **Step 1: Write the failing test**

`tests/LizTerm.Core.Tests/HostFiles/HostCredentialsTests.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.Core.HostFiles;

namespace LizTerm.Core.Tests.HostFiles;

public class HostCredentialsTests
{
    [Fact]
    public void ToString_never_shows_the_password()
    {
        var credentials = new HostCredentials("MVSCE02", "s3cret-pw");
        Assert.Equal("MVSCE02", credentials.Userid);
        Assert.Equal("s3cret-pw", credentials.Password);
        Assert.DoesNotContain("s3cret-pw", credentials.ToString());
        Assert.Contains("MVSCE02", credentials.ToString());
    }

    [Fact]
    public void An_exception_carries_its_kind_reason_and_server_text()
    {
        var ex = new HostFileException(HostFileErrorKind.CannotOpen, "SYS1.X(Y): not found.", 3, "Cannot open dataset member");
        Assert.Equal(HostFileErrorKind.CannotOpen, ex.Kind);
        Assert.Equal(3, ex.Reason);
        Assert.Equal("Cannot open dataset member", ex.ServerMessage);
        Assert.Equal("SYS1.X(Y): not found.", ex.Message);
        Assert.Null(ex.Certificate);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~HostCredentialsTests"`
Expected: build FAILS (`HostCredentials` not found).

- [ ] **Step 3: Implement**

`src/LizTerm.Core/HostFiles/HostCredentials.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

namespace LizTerm.Core.HostFiles;

/// <summary>A userid and password held in memory only. A class rather than a record so that no generated
/// <c>ToString</c> can print the password.</summary>
public sealed class HostCredentials(string userid, string password)
{
    public string Userid { get; } = userid;

    public string Password { get; } = password;

    public override string ToString() => $"HostCredentials({Userid}, password hidden)";
}

/// <param name="IsRetry">True when the host has just rejected the credentials the provider last gave, so a cached
/// pair must be dropped and the user asked again.</param>
public sealed record HostCredentialRequest(bool IsRetry);

/// <summary>How a service asks for credentials. Answers null when the user cancels.</summary>
public delegate ValueTask<HostCredentials?> HostCredentialProvider(HostCredentialRequest request, CancellationToken cancellationToken);
```

`src/LizTerm.Core/HostFiles/HostFileException.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.Core.Security;

namespace LizTerm.Core.HostFiles;

public enum HostFileErrorKind
{
    NotFound,
    /// <summary>The host could not open it: missing, not authorised, or in use. Some hosts cannot say which.</summary>
    CannotOpen,
    NotAuthorized,
    InvalidRequest,
    /// <summary>The credentials were rejected, or the user cancelled the prompt.</summary>
    Unauthenticated,
    /// <summary>TLS: the certificate is neither trusted nor pinned. <see cref="HostFileException.Certificate"/> says
    /// what was presented.</summary>
    CertificateRejected,
    ServerError,
    /// <summary>No connection, a dropped connection, or no data for too long.</summary>
    Unreachable,
}

/// <summary>A host outcome that is not success. <see cref="Exception.Message"/> is plain words fit to show;
/// <see cref="ServerMessage"/> is the host's own text, when it sent one.</summary>
public sealed class HostFileException(
    HostFileErrorKind kind,
    string message,
    int? reason = null,
    string? serverMessage = null,
    PresentedCertificate? certificate = null,
    Exception? inner = null) : Exception(message, inner)
{
    public HostFileErrorKind Kind { get; } = kind;

    public int? Reason { get; } = reason;

    public string? ServerMessage { get; } = serverMessage;

    public PresentedCertificate? Certificate { get; } = certificate;
}
```

`src/LizTerm.Core/HostFiles/HostServerInfo.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

namespace LizTerm.Core.HostFiles;

/// <summary>What a file service reports about itself, for a connection test.</summary>
public sealed record HostServerInfo(string Product, string ProductVersion, string SystemVersion);
```

`src/LizTerm.Core/HostFiles/IHostFileService.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

namespace LizTerm.Core.HostFiles;

/// <summary>File access to one host outside the 3270 session. Every call may run concurrently with the others.
/// Host outcomes other than success throw <see cref="HostFileException"/>; a cancelled token throws
/// <see cref="OperationCanceledException"/>. Text crosses this interface as .NET strings, one per record, so the
/// wire encoding is the implementation's business.</summary>
public interface IHostFileService : IDisposable
{
    Task<HostServerInfo> GetServerInfoAsync(CancellationToken cancellationToken = default);

    /// <summary>Every dataset matching <paramref name="pattern"/> (see <see cref="HostPath.DatasetPatternError"/>),
    /// in the host's order.</summary>
    Task<IReadOnlyList<HostFileEntry>> ListDatasetsAsync(string pattern, CancellationToken cancellationToken = default);

    /// <summary>The members of a partitioned dataset. Some hosts answer an empty list for a dataset that does not
    /// exist or is not partitioned, so confirm the dataset from <see cref="ListDatasetsAsync"/>.</summary>
    Task<IReadOnlyList<HostFileEntry>> ListMembersAsync(HostPath dataset, CancellationToken cancellationToken = default);

    /// <summary>The records as lines, trailing blanks as the host sent them. <paramref name="progress"/> reports
    /// bytes received.</summary>
    Task<IReadOnlyList<string>> ReadTextAsync(HostPath path, IProgress<long>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>Copies the record bytes to <paramref name="destination"/>; returns the byte count.</summary>
    Task<long> ReadBinaryAsync(HostPath path, Stream destination, IProgress<long>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>Replaces the dataset's or member's records, creating a member that does not exist. Every line must
    /// already have passed <see cref="TextUploadCheck"/>. A failure may leave the target partly written.</summary>
    Task WriteTextAsync(HostPath path, IReadOnlyList<string> lines, CancellationToken cancellationToken = default);

    Task WriteBinaryAsync(HostPath path, Stream source, CancellationToken cancellationToken = default);

    /// <summary>Deletes a member. Deleting a whole dataset is not supported in this release.</summary>
    Task DeleteAsync(HostPath path, CancellationToken cancellationToken = default);
}
```

(`TextUploadCheck` in the `<see cref>` is created in Task 3; until then the build warns CS1574 only if XML docs are generated, which this repo does not do. If the build reports it, write `<c>TextUploadCheck</c>` instead.)

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~HostCredentialsTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/LizTerm.Core/HostFiles tests/LizTerm.Core.Tests/HostFiles
git commit -m "Add the host file service contract to Core

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---
### Task 3: Upload pre-flight (`TextUploadCheck`)

**Files:**
- Create: `src/LizTerm.Core/HostFiles/TextUploadCheck.cs`
- Test: `tests/LizTerm.Core.Tests/HostFiles/TextUploadCheckTests.cs`

**Interfaces:**
- Consumes: `DatasetAttributes.UsableLineLength` (Task 1).
- Produces: `TextUploadOptions(bool ExpandTabs = true, int TabWidth = 8)`; `TextUploadProblemKind { InvalidEncoding, UnsupportedCharacter, LineTooLong, TabsPresent }`; `TextUploadProblem(TextUploadProblemKind Kind, int Line, string Message)` (`Line` is 1-based, 0 for a whole-file or summary problem); `TextUploadResult(IReadOnlyList<string> Lines, IReadOnlyList<TextUploadProblem> Errors, IReadOnlyList<TextUploadProblem> Warnings)` with `CanUpload`; `static TextUploadCheck.Run(ReadOnlySpan<byte> file, DatasetAttributes target, TextUploadOptions? options = null)`; `TextUploadCheck.MaxReportedPerKind = 5`; internal `SplitLines(string)`, `ExpandTabs(string, int)`.

Spec §5.2. The host silently truncates long lines and cannot store characters above U+00FF, so these are errors that block the upload. Blank lines are *not* handled here (the backend deals with them).

- [ ] **Step 1: Write the failing tests**

`tests/LizTerm.Core.Tests/HostFiles/TextUploadCheckTests.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Text;
using LizTerm.Core.HostFiles;

namespace LizTerm.Core.Tests.HostFiles;

public class TextUploadCheckTests
{
    private static readonly DatasetAttributes Fb80 = new("PO", "FB", 80, 19040, "PUB000");

    private static TextUploadResult Run(string text, DatasetAttributes? target = null, TextUploadOptions? options = null) =>
        TextUploadCheck.Run(Encoding.UTF8.GetBytes(text), target ?? Fb80, options);

    [Fact]
    public void Plain_lines_pass_unchanged()
    {
        var result = Run("//HELLO JOB\n//STEP EXEC PGM=IEFBR14\n");
        Assert.True(result.CanUpload);
        Assert.Equal(new[] { "//HELLO JOB", "//STEP EXEC PGM=IEFBR14" }, result.Lines);
        Assert.Empty(result.Errors);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void A_utf8_byte_order_mark_is_dropped()
    {
        var result = TextUploadCheck.Run([0xEF, 0xBB, 0xBF, (byte)'A', (byte)'\n'], Fb80);
        Assert.Equal(new[] { "A" }, result.Lines);
    }

    [Theory]
    [InlineData("A\nB", new[] { "A", "B" })]
    [InlineData("A\r\nB\r\n", new[] { "A", "B" })]
    [InlineData("A\rB\r", new[] { "A", "B" })]
    [InlineData("A\n\nB\n", new[] { "A", "", "B" })]
    [InlineData("A\n\n", new[] { "A", "" })]
    [InlineData("", new string[0])]
    [InlineData("A\fB", new[] { "A\fB" })]
    public void Splits_on_lf_crlf_and_cr_only_and_keeps_blank_lines(string text, string[] expected) =>
        Assert.Equal(expected, TextUploadCheck.SplitLines(text));

    [Fact]
    public void Invalid_utf8_blocks_and_suggests_binary()
    {
        var result = TextUploadCheck.Run([(byte)'A', 0xFF, (byte)'\n'], Fb80);
        Assert.False(result.CanUpload);
        var problem = Assert.Single(result.Errors);
        Assert.Equal(TextUploadProblemKind.InvalidEncoding, problem.Kind);
        Assert.Equal(0, problem.Line);
        Assert.Equal("The file is not UTF-8 text. Choose Binary to send its bytes unchanged.", problem.Message);
        Assert.Empty(result.Lines);
    }

    [Fact]
    public void A_character_above_latin1_blocks_and_is_named()
    {
        var result = Run("OK\nPRICE 5€\n");
        var problem = Assert.Single(result.Errors);
        Assert.Equal(TextUploadProblemKind.UnsupportedCharacter, problem.Kind);
        Assert.Equal(2, problem.Line);
        Assert.Equal("Line 2 contains “€” (U+20AC), which the host can't store.", problem.Message);
    }

    [Fact]
    public void A_character_outside_the_basic_plane_is_named_whole()
    {
        var problem = Assert.Single(Run("A😀\n").Errors);
        Assert.Equal("Line 1 contains “😀” (U+1F600), which the host can't store.", problem.Message);
    }

    [Fact]
    public void Latin1_characters_pass() => Assert.True(Run("¬ ¢ é | ~ \\ [ ]\n").CanUpload);

    [Fact]
    public void A_line_longer_than_the_record_blocks()
    {
        var result = Run(new string('X', 80) + "\n" + new string('Y', 81) + "\n");
        var problem = Assert.Single(result.Errors);
        Assert.Equal(TextUploadProblemKind.LineTooLong, problem.Kind);
        Assert.Equal(2, problem.Line);
        Assert.Equal("Line 2 is 81 characters; the limit is 80.", problem.Message);
    }

    [Fact]
    public void Variable_records_allow_lrecl_less_four()
    {
        var vb84 = new DatasetAttributes("PS", "VB", 84, 6233, null);
        Assert.True(Run(new string('X', 80), vb84).CanUpload);
        Assert.Equal("Line 1 is 81 characters; the limit is 80.", Assert.Single(Run(new string('X', 81), vb84).Errors).Message);
    }

    [Fact]
    public void Undefined_records_use_the_block_size()
    {
        var u100 = new DatasetAttributes("PS", "U", 0, 100, null);
        Assert.True(Run(new string('X', 100), u100).CanUpload);
        Assert.False(Run(new string('X', 101), u100).CanUpload);
    }

    [Fact]
    public void An_unknown_record_format_skips_the_length_check() =>
        Assert.True(Run(new string('X', 500), new DatasetAttributes("PS", null, null, null, null)).CanUpload);

    [Fact]
    public void Tabs_are_expanded_by_default_and_reported()
    {
        var result = Run("A\tB\nC\n\tD\n");
        Assert.True(result.CanUpload);
        Assert.Equal(new[] { "A       B", "C", "        D" }, result.Lines);
        var warning = Assert.Single(result.Warnings);
        Assert.Equal(TextUploadProblemKind.TabsPresent, warning.Kind);
        Assert.Equal(1, warning.Line);
        Assert.Equal("2 lines contain tab characters.", warning.Message);
    }

    [Fact]
    public void Tabs_can_be_left_alone()
    {
        var result = Run("A\tB\n", options: new TextUploadOptions(ExpandTabs: false));
        Assert.Equal(new[] { "A\tB" }, result.Lines);
        Assert.Equal("1 line contains tab characters.", Assert.Single(result.Warnings).Message);
    }

    [Fact]
    public void Expanded_tabs_count_towards_the_length()
    {
        var result = Run(new string('X', 73) + "\tY\n");
        Assert.Equal("Line 1 is 81 characters; the limit is 80.", Assert.Single(result.Errors).Message);
    }

    [Theory]
    [InlineData("a\tb", 8, "a       b")]
    [InlineData("12345678\tX", 8, "12345678        X")]
    [InlineData("\t", 4, "    ")]
    public void ExpandTabs_moves_to_the_next_stop(string line, int width, string expected) =>
        Assert.Equal(expected, TextUploadCheck.ExpandTabs(line, width));

    [Fact]
    public void Only_five_problems_of_a_kind_are_listed_then_a_count()
    {
        var text = string.Concat(Enumerable.Repeat(new string('X', 81) + "\n", 8)) + "€\n";
        var result = Run(text);
        Assert.Equal(7, result.Errors.Count);
        Assert.Equal(new[] { 1, 2, 3, 4, 5, 0 }, result.Errors.Take(6).Select(p => p.Line));
        Assert.Equal("3 more lines have the same problem.", result.Errors[5].Message);
        Assert.Equal(TextUploadProblemKind.LineTooLong, result.Errors[5].Kind);
        Assert.Equal(TextUploadProblemKind.UnsupportedCharacter, result.Errors[6].Kind);
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~TextUploadCheckTests"`
Expected: build FAILS (`TextUploadCheck` not found).

- [ ] **Step 3: Implement**

`src/LizTerm.Core/HostFiles/TextUploadCheck.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Text;

namespace LizTerm.Core.HostFiles;

public sealed record TextUploadOptions(bool ExpandTabs = true, int TabWidth = 8);

public enum TextUploadProblemKind { InvalidEncoding, UnsupportedCharacter, LineTooLong, TabsPresent }

/// <param name="Line">1-based; 0 for a problem with the whole file, or a count of further lines.</param>
public sealed record TextUploadProblem(TextUploadProblemKind Kind, int Line, string Message);

/// <param name="Lines">What to send, tabs already expanded when asked.</param>
/// <param name="Errors">Problems that block the upload.</param>
/// <param name="Warnings">Problems the user may accept.</param>
public sealed record TextUploadResult(
    IReadOnlyList<string> Lines,
    IReadOnlyList<TextUploadProblem> Errors,
    IReadOnlyList<TextUploadProblem> Warnings)
{
    public bool CanUpload => Errors.Count == 0;
}

/// <summary>Checks a local text file against the record rules of the dataset it is going to, before anything is
/// sent. A host may shorten an over-long line without saying so, and an EBCDIC code page holds only the Latin-1
/// repertoire, so both block the upload rather than lose data.</summary>
public static class TextUploadCheck
{
    public const int MaxReportedPerKind = 5;

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private static ReadOnlySpan<byte> Utf8Bom => [0xEF, 0xBB, 0xBF];

    public static TextUploadResult Run(ReadOnlySpan<byte> file, DatasetAttributes target, TextUploadOptions? options = null)
    {
        options ??= new TextUploadOptions();
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
        var limit = target.UsableLineLength;

        for (var i = 0; i < lines.Count; i++)
        {
            var number = i + 1;
            if (lines[i].Contains('\t'))
            {
                tabLines.Add(number);
                if (options.ExpandTabs) lines[i] = ExpandTabs(lines[i], options.TabWidth);
            }
            foreach (var rune in lines[i].EnumerateRunes())
            {
                if (rune.Value <= 0xFF) continue;
                unsupported.Add(number, $"Line {number} contains “{rune}” (U+{rune.Value:X4}), which the host can't store.");
                break;
            }
            if (limit is int max && lines[i].Length > max)
                tooLong.Add(number, $"Line {number} is {lines[i].Length} characters; the limit is {max}.");
        }

        var warnings = new List<TextUploadProblem>();
        if (tabLines.Count > 0)
        {
            warnings.Add(new TextUploadProblem(TextUploadProblemKind.TabsPresent, tabLines[0],
                tabLines.Count == 1 ? "1 line contains tab characters." : $"{tabLines.Count} lines contain tab characters."));
        }
        return new TextUploadResult(lines, [.. tooLong.All(), .. unsupported.All()], warnings);
    }

    /// <summary>Splits at CR LF, LF or CR only — never at form feed or the Unicode separators, which are data. A
    /// final line ending adds no empty line.</summary>
    internal static List<string> SplitLines(string text)
    {
        var lines = new List<string>();
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] is not ('\r' or '\n')) continue;
            lines.Add(text[start..i]);
            if (text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
            start = i + 1;
        }
        if (start < text.Length) lines.Add(text[start..]);
        return lines;
    }

    internal static string ExpandTabs(string line, int width)
    {
        var expanded = new StringBuilder(line.Length + width);
        foreach (var c in line)
        {
            if (c == '\t') expanded.Append(' ', width - expanded.Length % width);
            else expanded.Append(c);
        }
        return expanded.ToString();
    }

    private sealed class Reporter(TextUploadProblemKind kind)
    {
        private readonly List<TextUploadProblem> _problems = [];
        private int _extra;

        public void Add(int line, string message)
        {
            if (_problems.Count < MaxReportedPerKind) _problems.Add(new TextUploadProblem(kind, line, message));
            else _extra++;
        }

        public IEnumerable<TextUploadProblem> All() => _extra == 0
            ? _problems
            : _problems.Append(new TextUploadProblem(kind, 0,
                _extra == 1 ? "1 more line has the same problem." : $"{_extra} more lines have the same problem."));
    }
}
```

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~TextUploadCheckTests"`
Expected: PASS. (If `Only_five_problems...` fails on the count: 8 long lines give 5 + 1 summary, and the `€` line is the 9th, giving 7 errors.)

- [ ] **Step 5: Commit**

```bash
git add src/LizTerm.Core/HostFiles/TextUploadCheck.cs tests/LizTerm.Core.Tests/HostFiles/TextUploadCheckTests.cs
git commit -m "Check text uploads against record length and Latin-1 before sending

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 4: File side of transfers (`HostFileTransfer`)

**Files:**
- Create: `src/LizTerm.Core/HostFiles/HostFileTransfer.cs`
- Create: `tests/LizTerm.Core.Tests/HostFiles/FakeHostFileService.cs`
- Test: `tests/LizTerm.Core.Tests/HostFiles/HostFileTransferTests.cs`

**Interfaces:**
- Consumes: `IHostFileService`, `HostPath`, `HostTransferMode`, `TextUploadResult` (Tasks 1–3).
- Produces:
  - `DownloadOptions(HostTransferMode Mode, bool TrimTrailingBlanks = true, string? LineEnding = null)` (null line ending = `Environment.NewLine`).
  - `UploadVerification { NotChecked, Matches, Differs }`; `UploadOutcome(UploadVerification Verification, int? DiffersAtLine)` with statics `NotChecked`, `Matches`.
  - `static HostFileTransfer.DownloadAsync(IHostFileService service, HostPath path, string destinationFile, DownloadOptions options, IProgress<long>? progress = null, CancellationToken cancellationToken = default) : Task<long>` (bytes written locally).
  - `static HostFileTransfer.CheckTextFile(string sourceFile, DatasetAttributes target, TextUploadOptions? options = null) : TextUploadResult`.
  - `static HostFileTransfer.UploadTextAsync(IHostFileService service, HostPath path, TextUploadResult checkedText, bool verify, CancellationToken cancellationToken = default) : Task<UploadOutcome>`.
  - `static HostFileTransfer.UploadBinaryAsync(IHostFileService service, HostPath path, string sourceFile, CancellationToken cancellationToken = default) : Task`.
  - Internal `FormatText(IReadOnlyList<string>, DownloadOptions)`, `FirstDifference(IReadOnlyList<string>, IReadOnlyList<string>) : int?`.
  - Test helper `FakeHostFileService` (Core.Tests only).

Spec §5.1 and §5.3. A download never leaves a half-written file under the real name; verify-after-upload compares with trailing blanks trimmed, because fixed records come back padded.

- [ ] **Step 1: Write the fake and the failing tests**

`tests/LizTerm.Core.Tests/HostFiles/FakeHostFileService.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.Core.HostFiles;

namespace LizTerm.Core.Tests.HostFiles;

/// <summary>An in-memory host keyed by <see cref="HostPath.ToString"/>.</summary>
internal sealed class FakeHostFileService : IHostFileService
{
    public Dictionary<string, List<string>> Text { get; } = [];
    public Dictionary<string, byte[]> Binary { get; } = [];
    public List<string> Calls { get; } = [];

    /// <summary>Applied to what a text write stores, to play a host that alters data.</summary>
    public Func<IReadOnlyList<string>, List<string>> StoreTransform { get; set; } = lines => [.. lines];

    /// <summary>Thrown by reads after <see cref="BytesBeforeFailure"/> bytes have been written.</summary>
    public Exception? ReadFailure { get; set; }
    public int BytesBeforeFailure { get; set; }

    public Task<HostServerInfo> GetServerInfoAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new HostServerInfo("fake", "1", "test"));

    public Task<IReadOnlyList<HostFileEntry>> ListDatasetsAsync(string pattern, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<HostFileEntry>>([]);

    public Task<IReadOnlyList<HostFileEntry>> ListMembersAsync(HostPath dataset, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<HostFileEntry>>([]);

    public Task<IReadOnlyList<string>> ReadTextAsync(HostPath path, IProgress<long>? progress = null, CancellationToken cancellationToken = default)
    {
        Calls.Add($"readtext:{path}");
        cancellationToken.ThrowIfCancellationRequested();
        if (ReadFailure is not null) throw ReadFailure;
        var lines = Text[path.ToString()];
        progress?.Report(lines.Sum(l => l.Length + 1));
        return Task.FromResult<IReadOnlyList<string>>(lines);
    }

    public async Task<long> ReadBinaryAsync(HostPath path, Stream destination, IProgress<long>? progress = null, CancellationToken cancellationToken = default)
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
        return bytes.Length;
    }

    public Task WriteTextAsync(HostPath path, IReadOnlyList<string> lines, CancellationToken cancellationToken = default)
    {
        Calls.Add($"writetext:{path}:{lines.Count}");
        Text[path.ToString()] = StoreTransform(lines);
        return Task.CompletedTask;
    }

    public async Task WriteBinaryAsync(HostPath path, Stream source, CancellationToken cancellationToken = default)
    {
        Calls.Add($"writebinary:{path}");
        using var copy = new MemoryStream();
        await source.CopyToAsync(copy, cancellationToken);
        Binary[path.ToString()] = copy.ToArray();
    }

    public Task DeleteAsync(HostPath path, CancellationToken cancellationToken = default)
    {
        Calls.Add($"delete:{path}");
        Text.Remove(path.ToString());
        Binary.Remove(path.ToString());
        return Task.CompletedTask;
    }

    public void Dispose() => Calls.Add("dispose");
}
```

`tests/LizTerm.Core.Tests/HostFiles/HostFileTransferTests.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Text;
using LizTerm.Core.HostFiles;

namespace LizTerm.Core.Tests.HostFiles;

public sealed class HostFileTransferTests : IDisposable
{
    private static readonly HostPath Jes2 = HostPath.ForMember("SYS1.PROCLIB", "JES2");
    private static readonly DatasetAttributes Fb80 = new("PO", "FB", 80, 19040, null);
    private readonly string _directory = Directory.CreateTempSubdirectory("lizterm-hostfiles-").FullName;
    private readonly FakeHostFileService _host = new();

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private string Local(string name) => Path.Combine(_directory, name);

    [Fact]
    public async Task A_text_download_trims_trailing_blanks_and_uses_the_line_ending_asked_for()
    {
        _host.Text[Jes2.ToString()] = ["//JES2    PROC   ", "", "//  END  "];

        var written = await HostFileTransfer.DownloadAsync(_host, Jes2, Local("jes2.txt"),
            new DownloadOptions(HostTransferMode.Text, LineEnding: "\n"), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("//JES2    PROC\n\n//  END\n", await File.ReadAllTextAsync(Local("jes2.txt"), TestContext.Current.CancellationToken));
        Assert.Equal(24, written);
    }

    [Fact]
    public async Task Trailing_blanks_can_be_kept_and_the_file_is_utf8_without_a_mark()
    {
        _host.Text[Jes2.ToString()] = ["¬ ¢  "];

        await HostFileTransfer.DownloadAsync(_host, Jes2, Local("keep.txt"),
            new DownloadOptions(HostTransferMode.Text, TrimTrailingBlanks: false, LineEnding: "\r\n"), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(Encoding.UTF8.GetBytes("¬ ¢  \r\n"), await File.ReadAllBytesAsync(Local("keep.txt"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public void The_default_line_ending_is_the_platform_one() =>
        Assert.Equal("A" + Environment.NewLine, HostFileTransfer.FormatText(["A"], new DownloadOptions(HostTransferMode.Text)));

    [Fact]
    public async Task A_binary_download_copies_the_bytes()
    {
        _host.Binary[Jes2.ToString()] = [0x61, 0x61, 0xD1];

        var written = await HostFileTransfer.DownloadAsync(_host, Jes2, Local("jes2.bin"),
            new DownloadOptions(HostTransferMode.Binary), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(3, written);
        Assert.Equal(new byte[] { 0x61, 0x61, 0xD1 }, await File.ReadAllBytesAsync(Local("jes2.bin"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_failed_download_leaves_the_existing_file_and_no_partial_file()
    {
        await File.WriteAllTextAsync(Local("keep.bin"), "old", TestContext.Current.CancellationToken);
        _host.ReadFailure = new HostFileException(HostFileErrorKind.Unreachable, "dropped");
        _host.BytesBeforeFailure = 100;

        await Assert.ThrowsAsync<HostFileException>(() => HostFileTransfer.DownloadAsync(_host, Jes2, Local("keep.bin"),
            new DownloadOptions(HostTransferMode.Binary), cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal("old", await File.ReadAllTextAsync(Local("keep.bin"), TestContext.Current.CancellationToken));
        Assert.Equal(new[] { Local("keep.bin") }, Directory.GetFiles(_directory));
    }

    [Fact]
    public async Task A_cancelled_download_leaves_no_file()
    {
        _host.Text[Jes2.ToString()] = ["A"];
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => HostFileTransfer.DownloadAsync(_host, Jes2, Local("gone.txt"),
            new DownloadOptions(HostTransferMode.Text), cancellationToken: cancelled.Token));

        Assert.Empty(Directory.GetFiles(_directory));
    }

    [Fact]
    public async Task A_verified_upload_matches_when_the_host_pads_records()
    {
        _host.StoreTransform = lines => [.. lines.Select(l => l.PadRight(80))];
        var checkedText = HostFileTransfer.CheckTextFile(await WriteLocal("up.jcl", "//A JOB\n\n//B\n"), Fb80);

        var outcome = await HostFileTransfer.UploadTextAsync(_host, Jes2, checkedText, verify: true, TestContext.Current.CancellationToken);

        Assert.Equal(UploadOutcome.Matches, outcome);
        Assert.Equal(new[] { "writetext:SYS1.PROCLIB(JES2):3", "readtext:SYS1.PROCLIB(JES2)" }, _host.Calls);
    }

    [Fact]
    public async Task A_verified_upload_reports_the_first_line_that_differs()
    {
        _host.StoreTransform = lines => [.. lines.Where(l => l.Length > 0)];
        var checkedText = HostFileTransfer.CheckTextFile(await WriteLocal("up.jcl", "//A JOB\n\n//B\n"), Fb80);

        var outcome = await HostFileTransfer.UploadTextAsync(_host, Jes2, checkedText, verify: true, TestContext.Current.CancellationToken);

        Assert.Equal(new UploadOutcome(UploadVerification.Differs, 2), outcome);
    }

    [Fact]
    public async Task An_unverified_upload_does_not_read_back()
    {
        var checkedText = HostFileTransfer.CheckTextFile(await WriteLocal("up.jcl", "//A JOB\n"), Fb80);

        var outcome = await HostFileTransfer.UploadTextAsync(_host, Jes2, checkedText, verify: false, TestContext.Current.CancellationToken);

        Assert.Equal(UploadOutcome.NotChecked, outcome);
        Assert.Equal(new[] { "writetext:SYS1.PROCLIB(JES2):1" }, _host.Calls);
    }

    [Fact]
    public async Task Text_that_failed_its_check_is_never_sent()
    {
        var checkedText = HostFileTransfer.CheckTextFile(await WriteLocal("long.txt", new string('X', 81)), Fb80);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            HostFileTransfer.UploadTextAsync(_host, Jes2, checkedText, verify: true, TestContext.Current.CancellationToken));
        Assert.Empty(_host.Calls);
    }

    [Fact]
    public async Task A_binary_upload_sends_the_file_bytes()
    {
        var source = Local("load.bin");
        await File.WriteAllBytesAsync(source, [1, 2, 3], TestContext.Current.CancellationToken);

        await HostFileTransfer.UploadBinaryAsync(_host, Jes2, source, TestContext.Current.CancellationToken);

        Assert.Equal(new byte[] { 1, 2, 3 }, _host.Binary[Jes2.ToString()]);
    }

    [Theory]
    [InlineData(new[] { "A", "B" }, new[] { "A  ", "B" }, null)]
    [InlineData(new[] { "A", "B" }, new[] { "A", "C" }, 2)]
    [InlineData(new[] { "A", "B" }, new[] { "A" }, 2)]
    [InlineData(new[] { "A" }, new[] { "A", "B" }, 2)]
    [InlineData(new string[0], new string[0], null)]
    public void FirstDifference_trims_trailing_blanks_and_counts_missing_lines(string[] sent, string[] back, int? expected) =>
        Assert.Equal(expected, HostFileTransfer.FirstDifference(sent, back));

    private async Task<string> WriteLocal(string name, string text)
    {
        await File.WriteAllTextAsync(Local(name), text, TestContext.Current.CancellationToken);
        return Local(name);
    }
}
```

(The two `Assert.Equal(new[] { ... }, _host.Calls)` and `Assert.Equal(new[] { Local(...) }, ...)` lines use collection expressions; if the compiler cannot infer the type, write `new[] { ... }`.)

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~HostFileTransferTests"`
Expected: build FAILS (`HostFileTransfer` not found).

- [ ] **Step 3: Implement**

`src/LizTerm.Core/HostFiles/HostFileTransfer.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Text;

namespace LizTerm.Core.HostFiles;

/// <param name="TrimTrailingBlanks">Fixed-length records arrive padded with blanks; trimming them is what a local
/// editor expects.</param>
/// <param name="LineEnding">Null for the platform's own.</param>
public sealed record DownloadOptions(HostTransferMode Mode, bool TrimTrailingBlanks = true, string? LineEnding = null);

public enum UploadVerification { NotChecked, Matches, Differs }

/// <param name="DiffersAtLine">1-based, set when <paramref name="Verification"/> is Differs.</param>
public sealed record UploadOutcome(UploadVerification Verification, int? DiffersAtLine)
{
    public static readonly UploadOutcome NotChecked = new(UploadVerification.NotChecked, null);
    public static readonly UploadOutcome Matches = new(UploadVerification.Matches, null);
}

/// <summary>The local-file side of a transfer, over any <see cref="IHostFileService"/>.</summary>
public static class HostFileTransfer
{
    private static readonly UTF8Encoding Utf8NoMark = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Writes to a hidden temporary file beside <paramref name="destinationFile"/> and renames it into place
    /// only on success, so a failed or cancelled download never leaves a partial file under the real name and never
    /// damages the file it would have replaced.</summary>
    /// <returns>The bytes written locally.</returns>
    public static async Task<long> DownloadAsync(IHostFileService service, HostPath path, string destinationFile,
        DownloadOptions options, IProgress<long>? progress = null, CancellationToken cancellationToken = default)
    {
        var full = Path.GetFullPath(destinationFile);
        var temporary = Path.Combine(Path.GetDirectoryName(full)!, $".{Path.GetFileName(full)}.{Guid.NewGuid():N}.part");
        try
        {
            long written;
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                if (options.Mode == HostTransferMode.Binary)
                {
                    written = await service.ReadBinaryAsync(path, stream, progress, cancellationToken);
                }
                else
                {
                    var lines = await service.ReadTextAsync(path, progress, cancellationToken);
                    var bytes = Utf8NoMark.GetBytes(FormatText(lines, options));
                    await stream.WriteAsync(bytes, cancellationToken);
                    written = bytes.Length;
                }
            }
            File.Move(temporary, full, overwrite: true);
            return written;
        }
        catch
        {
            TryDelete(temporary);
            throw;
        }
    }

    public static TextUploadResult CheckTextFile(string sourceFile, DatasetAttributes target, TextUploadOptions? options = null) =>
        TextUploadCheck.Run(File.ReadAllBytes(sourceFile), target, options);

    /// <summary>Sends text that passed its check and, with <paramref name="verify"/>, reads it back and compares.</summary>
    /// <exception cref="InvalidOperationException">The check found errors.</exception>
    public static async Task<UploadOutcome> UploadTextAsync(IHostFileService service, HostPath path,
        TextUploadResult checkedText, bool verify, CancellationToken cancellationToken = default)
    {
        if (!checkedText.CanUpload) throw new InvalidOperationException("The text did not pass its upload check.");
        await service.WriteTextAsync(path, checkedText.Lines, cancellationToken);
        if (!verify) return UploadOutcome.NotChecked;
        var stored = await service.ReadTextAsync(path, null, cancellationToken);
        return FirstDifference(checkedText.Lines, stored) is { } line
            ? new UploadOutcome(UploadVerification.Differs, line)
            : UploadOutcome.Matches;
    }

    public static async Task UploadBinaryAsync(IHostFileService service, HostPath path, string sourceFile,
        CancellationToken cancellationToken = default)
    {
        await using var source = File.OpenRead(sourceFile);
        await service.WriteBinaryAsync(path, source, cancellationToken);
    }

    internal static string FormatText(IReadOnlyList<string> lines, DownloadOptions options)
    {
        var ending = options.LineEnding ?? Environment.NewLine;
        var text = new StringBuilder();
        foreach (var line in lines) text.Append(options.TrimTrailingBlanks ? line.TrimEnd(' ') : line).Append(ending);
        return text.ToString();
    }

    /// <summary>The first 1-based line where the two differ once trailing blanks are ignored, counting a missing line
    /// as a difference; null when they match.</summary>
    internal static int? FirstDifference(IReadOnlyList<string> sent, IReadOnlyList<string> stored)
    {
        var common = Math.Min(sent.Count, stored.Count);
        for (var i = 0; i < common; i++)
        {
            if (!string.Equals(sent[i].TrimEnd(' '), stored[i].TrimEnd(' '), StringComparison.Ordinal)) return i + 1;
        }
        return sent.Count == stored.Count ? null : common + 1;
    }

    private static void TryDelete(string file)
    {
        try
        {
            File.Delete(file);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
```

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~HostFiles"`
Expected: PASS (Tasks 1–4).

- [ ] **Step 5: Commit**

```bash
git add src/LizTerm.Core/HostFiles tests/LizTerm.Core.Tests/HostFiles
git commit -m "Download host files through a temporary file and verify text uploads

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 5: Backend project, fixture recorder and test harness

**Files:**
- Create: `src/LizTerm.Backend.Mvsmf/LizTerm.Backend.Mvsmf.csproj`, `src/LizTerm.Backend.Mvsmf/MvsmfOptions.cs`
- Create: `tests/LizTerm.Backend.Mvsmf.Tests/LizTerm.Backend.Mvsmf.Tests.csproj`, `tests/LizTerm.Backend.Mvsmf.Tests/Fixture.cs`, `tests/LizTerm.Backend.Mvsmf.Tests/RecordedHandler.cs`, `tests/LizTerm.Backend.Mvsmf.Tests/FixtureTests.cs`, `tests/LizTerm.Backend.Mvsmf.Tests/MvsmfOptionsTests.cs`, `tests/LizTerm.Backend.Mvsmf.Tests/Fixtures/README.md`, `tests/LizTerm.Backend.Mvsmf.Tests/Fixtures/*.http`
- Create: `tools/record-mvsmf-fixture.sh`
- Modify: `LizTerm.slnx`

**Interfaces:**
- Produces:
  - `public sealed record MvsmfOptions(Uri BaseUrl, CertificatePin? PinnedCertificate = null)` with `public static bool TryNormalizeBaseUrl(string? text, out Uri? url, out string? error)`.
  - Tests: `Fixture.Load(string name) : HttpResponseMessage`, `Fixture.Path(string name)`; `RecordedHandler` with `Then(string fixture)`, `Then(HttpStatusCode status, string body = "", string contentType = "application/json")`, `Then(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)`, `Requests : List<RecordedRequest>`; `RecordedRequest(HttpMethod Method, Uri Uri, string? Authorization, string? DataType, string? ContentType, byte[]? Body, IReadOnlyList<string> HeaderNames)` with `BodyText` (Latin-1).

A fixture is one recorded exchange: the response headers with CRs removed (and `Date`, `Jobname`, `Jobid`, `Node` dropped), a blank line, then the body bytes exactly as received.

- [ ] **Step 1: Create the projects and add them to the solution**

`src/LizTerm.Backend.Mvsmf/LizTerm.Backend.Mvsmf.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <ProjectReference Include="../LizTerm.Core/LizTerm.Core.csproj" />
  </ItemGroup>
  <ItemGroup>
    <InternalsVisibleTo Include="LizTerm.Backend.Mvsmf.Tests" />
    <InternalsVisibleTo Include="LizTerm.Integration.Tests" />
  </ItemGroup>
</Project>
```

`tests/LizTerm.Backend.Mvsmf.Tests/LizTerm.Backend.Mvsmf.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit.v3" />
    <PackageReference Include="xunit.runner.visualstudio" />
    <!-- Compiled into this assembly too rather than duplicated: the TLS tests serve the certificates the Core tests
         already build. -->
    <Compile Include="../LizTerm.Core.Tests/Security/TestCertificates.cs" Link="Security/TestCertificates.cs" />
  </ItemGroup>
  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="../../src/LizTerm.Backend.Mvsmf/LizTerm.Backend.Mvsmf.csproj" />
  </ItemGroup>
  <ItemGroup>
    <None Include="Fixtures/**" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
</Project>
```

In `LizTerm.slnx`, add `<Project Path="src/LizTerm.Backend.Mvsmf/LizTerm.Backend.Mvsmf.csproj" />` after the B3270 line in `/src/`, and `<Project Path="tests/LizTerm.Backend.Mvsmf.Tests/LizTerm.Backend.Mvsmf.Tests.csproj" />` after the B3270 tests line in `/tests/`.

- [ ] **Step 2: Write the recorder**

`tools/record-mvsmf-fixture.sh` (then `chmod +x tools/record-mvsmf-fixture.sh`):

```bash
#!/usr/bin/env bash
# This file is part of LizTerm.
# Copyright 2026 by CoffeeMuse
# SPDX-License-Identifier: BSD-3-Clause
#
# Records one mvsMF exchange as a test fixture in tests/LizTerm.Backend.Mvsmf.Tests/Fixtures/<name>.http: the
# response headers with CRs removed (Date, Jobname, Jobid and Node dropped), a blank line, then the body bytes
# untouched. The body is never passed through tr, because binary records can hold 0x0D.
#
# Needs LIZTERM_MVSMF_URL (the base, ending in /zosmf), LIZTERM_MVSMF_USER and LIZTERM_MVSMF_PASSWORD. The
# credentials reach curl through a netrc on a file descriptor, never the command line. MVSMF_BAD_PASSWORD=1 sends a
# wrong password instead, to record a 401.
#
#   tools/record-mvsmf-fixture.sh <name> <METHOD> <path-under-zosmf> [extra curl args...]
set -euo pipefail

if [[ $# -lt 3 ]]; then
  echo "usage: $0 <name> <METHOD> <path-under-zosmf> [curl args...]" >&2
  exit 2
fi
name=$1 method=$2 path=$3
shift 3
: "${LIZTERM_MVSMF_URL:?set LIZTERM_MVSMF_URL}" "${LIZTERM_MVSMF_USER:?set LIZTERM_MVSMF_USER}" "${LIZTERM_MVSMF_PASSWORD:?set LIZTERM_MVSMF_PASSWORD}"

out="$(cd "$(dirname "$0")/.." && pwd)/tests/LizTerm.Backend.Mvsmf.Tests/Fixtures/$name.http"
host=$(printf '%s' "$LIZTERM_MVSMF_URL" | sed -E 's#^[A-Za-z]+://([^/:]+).*#\1#')
password=$LIZTERM_MVSMF_PASSWORD
if [[ -n "${MVSMF_BAD_PASSWORD:-}" ]]; then password=not-the-password; fi

tmp=$(mktemp -d)
trap 'rm -rf "$tmp"' EXIT
touch "$tmp/body"
curl -sS --netrc-file <(printf 'machine %s login %s password %s\n' "$host" "$LIZTERM_MVSMF_USER" "$password") \
  -X "$method" -D "$tmp/headers" -o "$tmp/body" "$@" "${LIZTERM_MVSMF_URL%/}/$path"
{ tr -d '\r' < "$tmp/headers" | grep -viE '^(date|jobname|jobid|node):'; cat "$tmp/body"; } > "$out"
echo "$name: $(head -1 "$out")"
```

- [ ] **Step 3: Record the fixtures from the live host**

Needs the Prerequisites variables. Run from the repository root, in this order (the write is deleted by the two deletes):

```bash
source ~/.config/lizterm-test.env
S="$LIZTERM_MVSMF_SCRATCH_PDS"
tools/record-mvsmf-fixture.sh info-200 GET info
MVSMF_BAD_PASSWORD=1 tools/record-mvsmf-fixture.sh info-401 GET info
tools/record-mvsmf-fixture.sh ds-list-sys1 GET 'restfiles/ds?dslevel=SYS1.**'
tools/record-mvsmf-fixture.sh ds-list-empty GET 'restfiles/ds?dslevel=NOSUCH.HLQ'
tools/record-mvsmf-fixture.sh members-proclib GET 'restfiles/ds/SYS1.PROCLIB/member'
tools/record-mvsmf-fixture.sh members-missing-dataset GET "restfiles/ds/${S%%.*}.NOSUCH/member"
tools/record-mvsmf-fixture.sh read-text-jes2 GET 'restfiles/ds/SYS1.PROCLIB(JES2)' -H 'X-IBM-Data-Type: text'
tools/record-mvsmf-fixture.sh read-binary-jes2 GET 'restfiles/ds/SYS1.PROCLIB(JES2)' -H 'X-IBM-Data-Type: binary'
tools/record-mvsmf-fixture.sh read-missing-member GET 'restfiles/ds/SYS1.PROCLIB(NOSUCHMB)'
tools/record-mvsmf-fixture.sh read-pds-as-sequential GET 'restfiles/ds/SYS1.PROCLIB'
tools/record-mvsmf-fixture.sh name-too-long GET 'restfiles/ds/SYS1.PROCLIB(TOOLONGNAME)'
tools/record-mvsmf-fixture.sh write-204 PUT "restfiles/ds/$S(LIZFIXT)" -H 'Content-Type: text/plain' --data-binary 'LIZFIXT'
tools/record-mvsmf-fixture.sh delete-204 DELETE "restfiles/ds/$S(LIZFIXT)"
tools/record-mvsmf-fixture.sh delete-missing DELETE "restfiles/ds/$S(LIZFIXT)"
```

Expected first lines, as observed on 2026-09-16: `HTTP/1.1 200 OK` for info-200, ds-list-*, members-*, read-text-jes2, read-binary-jes2; `401 Unauthorized` for info-401; `500 Internal Server Error` for read-missing-member; `400 Bad Request` for read-pds-as-sequential and name-too-long; `204 No Content` for write-204 and delete-204; `404 Not Found` for delete-missing. If any differs, stop and report it: the host has changed and `docs/mvsmf-compatibility.md` needs a new entry.

Check nothing secret was captured: `grep -il "authorization\|password" tests/LizTerm.Backend.Mvsmf.Tests/Fixtures/*.http` must print nothing.

Write `tests/LizTerm.Backend.Mvsmf.Tests/Fixtures/README.md`:

```markdown
# mvsMF fixtures

Each `.http` file is one exchange recorded from a live mvsMF by `tools/record-mvsmf-fixture.sh`: the response
headers without CRs (Date, Jobname, Jobid and Node dropped), a blank line, then the body bytes as received.
`Fixture.Load` turns one into an `HttpResponseMessage`; `Transfer-Encoding`, `Content-Length` and `Connection` are
ignored because the body is already whole.

Recorded 2026-09-16 from an MVS/CE host running a pre-release mvsMF reporting `1.0.0-dev`. See
`docs/mvsmf-compatibility.md` before re-recording against a newer build: a changed fixture is a changed behaviour.

| Fixture | Request |
|---|---|
| `info-200` | `GET info` |
| `info-401` | `GET info` with a wrong password |
| `ds-list-sys1` | `GET restfiles/ds?dslevel=SYS1.**` |
| `ds-list-empty` | `GET restfiles/ds?dslevel=NOSUCH.HLQ` |
| `members-proclib` | `GET restfiles/ds/SYS1.PROCLIB/member` |
| `members-missing-dataset` | `GET restfiles/ds/<hlq>.NOSUCH/member` — answered 200 with no items |
| `read-text-jes2` | `GET restfiles/ds/SYS1.PROCLIB(JES2)`, text |
| `read-binary-jes2` | the same, binary |
| `read-missing-member` | `GET restfiles/ds/SYS1.PROCLIB(NOSUCHMB)` — 500, reason 3 |
| `read-pds-as-sequential` | `GET restfiles/ds/SYS1.PROCLIB` — 400, reason 1 |
| `name-too-long` | `GET restfiles/ds/SYS1.PROCLIB(TOOLONGNAME)` — 400, reason 1 |
| `write-204` | `PUT` of a scratch member |
| `delete-204` | `DELETE` of that member |
| `delete-missing` | the same `DELETE` again — 404, reason 5 |
```

- [ ] **Step 4: Write the harness and failing tests**

`tests/LizTerm.Backend.Mvsmf.Tests/Fixture.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace LizTerm.Backend.Mvsmf.Tests;

/// <summary>Loads a recorded exchange (see Fixtures/README.md) as the response a handler returns.</summary>
internal static class Fixture
{
    private static readonly HashSet<string> Ignored = new(StringComparer.OrdinalIgnoreCase)
    {
        "Transfer-Encoding", "Content-Length", "Connection",
    };

    public static string Path(string name) => System.IO.Path.Combine(AppContext.BaseDirectory, "Fixtures", name + ".http");

    public static byte[] Body(string name) => Split(File.ReadAllBytes(Path(name))).Body;

    public static HttpResponseMessage Load(string name)
    {
        var (head, body) = Split(File.ReadAllBytes(Path(name)));
        var lines = Encoding.ASCII.GetString(head).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var status = int.Parse(lines[0].Split(' ')[1]);
        var response = new HttpResponseMessage((HttpStatusCode)status) { Content = new ByteArrayContent(body) };
        foreach (var line in lines.Skip(1))
        {
            var colon = line.IndexOf(':');
            var header = line[..colon].Trim();
            var value = line[(colon + 1)..].Trim();
            if (Ignored.Contains(header)) continue;
            if (header.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
                response.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(value);
            else
                response.Headers.TryAddWithoutValidation(header, value);
        }
        return response;
    }

    private static (byte[] Head, byte[] Body) Split(byte[] file)
    {
        var separator = file.AsSpan().IndexOf("\n\n"u8);
        if (separator < 0) throw new InvalidDataException("A fixture needs a blank line after its headers.");
        return (file[..separator], file[(separator + 2)..]);
    }
}
```

`tests/LizTerm.Backend.Mvsmf.Tests/RecordedHandler.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Net;
using System.Text;

namespace LizTerm.Backend.Mvsmf.Tests;

internal sealed record RecordedRequest(HttpMethod Method, Uri Uri, string? Authorization, string? DataType, string? ContentType, byte[]? Body, IReadOnlyList<string> HeaderNames)
{
    public string BodyText => Body is null ? "" : Encoding.Latin1.GetString(Body);
}

/// <summary>Answers each request with the next queued response and keeps what was asked.</summary>
internal sealed class RecordedHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>> _responses = new();

    public List<RecordedRequest> Requests { get; } = [];

    public RecordedHandler Then(string fixture) => Then((_, _) => Task.FromResult(Fixture.Load(fixture)));

    public RecordedHandler Then(HttpStatusCode status, string body = "", string contentType = "application/json") =>
        Then((_, _) => Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, contentType),
        }));

    public RecordedHandler Then(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
    {
        _responses.Enqueue(respond);
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsByteArrayAsync(cancellationToken);
        Requests.Add(new RecordedRequest(
            request.Method,
            request.RequestUri!,
            request.Headers.Authorization?.ToString(),
            request.Headers.TryGetValues("X-IBM-Data-Type", out var types) ? string.Join(",", types) : null,
            request.Content?.Headers.ContentType?.ToString(),
            body,
            [.. request.Headers.Select(h => h.Key)]));
        if (_responses.Count == 0) throw new InvalidOperationException($"No response queued for {request.Method} {request.RequestUri}");
        return await _responses.Dequeue()(request, cancellationToken);
    }
}
```

`tests/LizTerm.Backend.Mvsmf.Tests/FixtureTests.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Net;

namespace LizTerm.Backend.Mvsmf.Tests;

public class FixtureTests
{
    [Fact]
    public async Task A_fixture_loads_its_status_type_and_body()
    {
        using var response = Fixture.Load("info-200");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType!.MediaType);
        Assert.Contains("\"zosmf_version\"", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public void A_binary_body_keeps_every_byte()
    {
        var body = Fixture.Body("read-binary-jes2");
        Assert.Equal(0, body.Length % 80);
        Assert.Equal(new byte[] { 0x61, 0x61, 0xD1, 0xC5, 0xE2, 0xF2 }, body[..6]);
    }

    [Fact]
    public void No_fixture_holds_credentials()
    {
        foreach (var file in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Fixtures"), "*.http"))
        {
            var text = File.ReadAllText(file);
            Assert.DoesNotContain("Authorization", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("password", text, StringComparison.OrdinalIgnoreCase);
        }
    }
}
```

`tests/LizTerm.Backend.Mvsmf.Tests/MvsmfOptionsTests.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

namespace LizTerm.Backend.Mvsmf.Tests;

public class MvsmfOptionsTests
{
    [Theory]
    [InlineData("http://10.42.37.209:8080", "http://10.42.37.209:8080/zosmf")]
    [InlineData(" http://10.42.37.209:8080/ ", "http://10.42.37.209:8080/zosmf")]
    [InlineData("http://h:8080/zosmf/", "http://h:8080/zosmf")]
    [InlineData("https://proxy.example/mvs/zosmf", "https://proxy.example/mvs/zosmf")]
    public void Normalizes_the_base_url(string text, string expected)
    {
        Assert.True(MvsmfOptions.TryNormalizeBaseUrl(text, out var url, out var error), error);
        Assert.Equal(expected, url!.ToString());
    }

    [Theory]
    [InlineData(null, "Enter the mvsMF URL.")]
    [InlineData("  ", "Enter the mvsMF URL.")]
    [InlineData("ftp://h/zosmf", "Enter an http:// or https:// URL.")]
    [InlineData("h:8080", "Enter an http:// or https:// URL.")]
    [InlineData("10.42.37.209:8080", "Enter an http:// or https:// URL.")]
    [InlineData("http://h:8080/zosmf?x=1", "The URL cannot have a query or a fragment.")]
    public void Rejects_what_is_not_an_http_base(string? text, string expected)
    {
        Assert.False(MvsmfOptions.TryNormalizeBaseUrl(text, out var url, out var error));
        Assert.Null(url);
        Assert.Equal(expected, error);
    }
}
```

- [ ] **Step 5: Run to verify they fail**

Run: `dotnet test tests/LizTerm.Backend.Mvsmf.Tests`
Expected: build FAILS (`MvsmfOptions` not found).

- [ ] **Step 6: Implement `MvsmfOptions`**

`src/LizTerm.Backend.Mvsmf/MvsmfOptions.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.Core.Session;

namespace LizTerm.Backend.Mvsmf;

/// <param name="BaseUrl">The z/OSMF root, for example <c>http://host:8080/zosmf</c>.</param>
/// <param name="PinnedCertificate">For https: the only certificate accepted. Null means the system's trust.</param>
public sealed record MvsmfOptions(Uri BaseUrl, CertificatePin? PinnedCertificate = null)
{
    /// <summary>Reads what a user typed. An empty path becomes <c>/zosmf</c> and a trailing slash is dropped; the
    /// scheme is honoured, never changed.</summary>
    public static bool TryNormalizeBaseUrl(string? text, out Uri? url, out string? error)
    {
        url = null;
        var trimmed = (text ?? "").Trim();
        if (trimmed.Length == 0)
        {
            error = "Enter the mvsMF URL.";
            return false;
        }
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var parsed)
            || parsed.Scheme is not ("http" or "https")
            || parsed.Host.Length == 0)
        {
            error = "Enter an http:// or https:// URL.";
            return false;
        }
        if (parsed.Query.Length > 0 || parsed.Fragment.Length > 0)
        {
            error = "The URL cannot have a query or a fragment.";
            return false;
        }
        var path = parsed.AbsolutePath.TrimEnd('/');
        if (path.Length == 0) path = "/zosmf";
        url = new UriBuilder(parsed) { Path = path }.Uri;
        error = null;
        return true;
    }
}
```

(`10.42.37.209:8080` fails `TryCreate` as absolute, or parses with scheme `10.42.37.209`; either way the scheme check rejects it. If `UriBuilder` renders the default port, the test for `http://h:8080` still holds because 8080 is not a default port; `https://proxy.example/...` keeps no port because 443 is the default.)

- [ ] **Step 7: Run to verify they pass**

Run: `dotnet test tests/LizTerm.Backend.Mvsmf.Tests`
Expected: PASS. `dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~RepositoryHeadersTests"` also PASS (the script and new files carry headers).

- [ ] **Step 8: Commit**

```bash
git add LizTerm.slnx src/LizTerm.Backend.Mvsmf tests/LizTerm.Backend.Mvsmf.Tests tools/record-mvsmf-fixture.sh
git commit -m "Add the mvsMF backend project, its fixture recorder and recorded exchanges

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 6: Service core — requests, credentials, errors, server info

**Files:**
- Create: `src/LizTerm.Backend.Mvsmf/MvsmfJson.cs`, `src/LizTerm.Backend.Mvsmf/MvsmfErrors.cs`, `src/LizTerm.Backend.Mvsmf/IdleTimeout.cs`, `src/LizTerm.Backend.Mvsmf/MvsmfFileService.cs`
- Test: `tests/LizTerm.Backend.Mvsmf.Tests/MvsmfErrorsTests.cs`, `tests/LizTerm.Backend.Mvsmf.Tests/MvsmfAuthTests.cs`

**Interfaces:**
- Consumes: Core `HostFiles` (Tasks 1–2), `MvsmfOptions`, `RecordedHandler`, `Fixture` (Task 5).
- Produces:
  - `public sealed class MvsmfFileService : IHostFileService` with `public MvsmfFileService(MvsmfOptions options, HostCredentialProvider credentials)` and `internal MvsmfFileService(HttpMessageHandler handler, Uri baseUrl, HostCredentialProvider credentials, TimeSpan? idleTimeout = null, MvsmfCertificateCheck? certificates = null)`.
  - Private helpers later tasks use inside the same class: `Url(string relative)`, `DatasetPath(HostPath path)`, `EscapeName(string name)`, `SendAsync(Func<HttpRequestMessage> build, string what, IdleTimeout idle, CancellationToken ct) : Task<HttpResponseMessage>`, `CopyBodyAsync(HttpResponseMessage response, Stream destination, IdleTimeout idle, IProgress<long>? progress, string what, CancellationToken ct) : Task<long>`, `ReadJsonAsync<T>(HttpResponseMessage response, JsonTypeInfo<T> type, string what, IdleTimeout idle, CancellationToken ct) : Task<T>`.
  - `internal static class MvsmfErrors` with `FromResponse(HttpStatusCode status, byte[] body, string what) : HostFileException` and `Classify(HttpStatusCode status, int? category, int? rc, int? reason) : HostFileErrorKind`.
  - `internal sealed class IdleTimeout(TimeSpan idle, CancellationToken outer) : IDisposable` with `Token`, `Reset()`, `Expired(CancellationToken outer)`.
  - `MvsmfCertificateCheck` is referenced here and created in Task 10; until then create it as the minimal class shown in Step 3.
  - JSON DTOs: `MvsmfInfo`, `MvsmfDatasetList`, `MvsmfDataset`, `MvsmfMemberList`, `MvsmfMember`, `MvsmfError`, `MvsmfJsonContext`, `LenientStringConverter`.

Credentials (spec §3.2): the service stores none. It asks the provider before every request with `IsRetry: false` (the App's holder answers from its cache), and after a 401 asks once with `IsRetry: true` and repeats the request once. Every request sends Basic auth, because this build returns no session cookie.

- [ ] **Step 1: Write the failing tests**

`tests/LizTerm.Backend.Mvsmf.Tests/MvsmfErrorsTests.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Net;
using System.Text;
using LizTerm.Core.HostFiles;

namespace LizTerm.Backend.Mvsmf.Tests;

public class MvsmfErrorsTests
{
    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, null, null, null, HostFileErrorKind.Unauthenticated)]
    [InlineData(HttpStatusCode.InternalServerError, 6, 8, 3, HostFileErrorKind.CannotOpen)]
    [InlineData(HttpStatusCode.NotFound, 6, 8, 5, HostFileErrorKind.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError, 6, 8, 4, HostFileErrorKind.NotFound)]
    [InlineData(HttpStatusCode.NotFound, 4, 6, 7, HostFileErrorKind.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError, 4, 8, 0, HostFileErrorKind.NotAuthorized)]
    [InlineData(HttpStatusCode.Forbidden, null, null, null, HostFileErrorKind.NotAuthorized)]
    [InlineData(HttpStatusCode.BadRequest, 6, 8, 1, HostFileErrorKind.InvalidRequest)]
    [InlineData(HttpStatusCode.InternalServerError, 8, 900, 7, HostFileErrorKind.ServerError)]
    [InlineData(HttpStatusCode.BadGateway, null, null, null, HostFileErrorKind.ServerError)]
    public void Classifies_by_reason_before_status(HttpStatusCode status, int? category, int? rc, int? reason, HostFileErrorKind expected) =>
        Assert.Equal(expected, MvsmfErrors.Classify(status, category, rc, reason));

    [Fact]
    public async Task Missing_read_is_500_is_reported_as_cannot_open()
    {
        using var response = Fixture.Load("read-missing-member");
        var body = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);

        var ex = MvsmfErrors.FromResponse(response.StatusCode, body, "SYS1.PROCLIB(NOSUCHMB)");

        Assert.Equal(HostFileErrorKind.CannotOpen, ex.Kind);
        Assert.Equal(3, ex.Reason);
        Assert.Equal("Cannot open dataset member", ex.ServerMessage);
        Assert.Equal("SYS1.PROCLIB(NOSUCHMB): not found, not authorized, or cannot be opened.", ex.Message);
    }

    [Fact]
    public async Task A_pds_read_as_sequential_is_an_invalid_request_quoting_the_host()
    {
        using var response = Fixture.Load("read-pds-as-sequential");
        var body = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);

        var ex = MvsmfErrors.FromResponse(response.StatusCode, body, "SYS1.PROCLIB");

        Assert.Equal(HostFileErrorKind.InvalidRequest, ex.Kind);
        Assert.Equal(1, ex.Reason);
        Assert.StartsWith("SYS1.PROCLIB: the host refused the request (Dataset is a partitioned dataset", ex.Message);
    }

    [Fact]
    public async Task A_second_delete_is_not_found()
    {
        using var response = Fixture.Load("delete-missing");
        var body = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);

        var ex = MvsmfErrors.FromResponse(response.StatusCode, body, "X.Y(Z)");

        Assert.Equal(HostFileErrorKind.NotFound, ex.Kind);
        Assert.Equal(5, ex.Reason);
        Assert.Equal("X.Y(Z): not found.", ex.Message);
    }

    [Theory]
    [InlineData("", "Server information: server error (HTTP 502).")]
    [InlineData("<html>gateway</html>", "Server information: server error (HTTP 502).")]
    [InlineData("""{"rc":8,"category":9,"reason":12,"message":"odd"}""", "Server information: server error (reason 12).")]
    public void A_body_that_is_not_an_mvsmf_error_still_gives_a_message(string body, string expected) =>
        Assert.Equal(expected, MvsmfErrors.FromResponse(HttpStatusCode.BadGateway, Encoding.UTF8.GetBytes(body), "Server information").Message);

    [Fact]
    public void Authorization_is_500_is_reported_as_not_authorized()
    {
        var body = Encoding.UTF8.GetBytes("""{"rc":8,"category":4,"reason":0,"message":"LMOPEN error"}""");
        var ex = MvsmfErrors.FromResponse(HttpStatusCode.InternalServerError, body, "SYS1.SECRET(X)");
        Assert.Equal(HostFileErrorKind.NotAuthorized, ex.Kind);
        Assert.Equal("SYS1.SECRET(X): not authorized.", ex.Message);
    }

    [Fact]
    public void A_401_says_the_credentials_were_rejected() =>
        Assert.Equal("The host rejected the userid or password.",
            MvsmfErrors.FromResponse(HttpStatusCode.Unauthorized, [], "anything").Message);
}
```

`tests/LizTerm.Backend.Mvsmf.Tests/MvsmfAuthTests.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Net.Http.Headers;
using System.Text;
using LizTerm.Core.HostFiles;

namespace LizTerm.Backend.Mvsmf.Tests;

public class MvsmfAuthTests
{
    internal static readonly Uri Base = new("http://mvs.test:8080/zosmf");

    internal static HostCredentialProvider Answering(List<HostCredentialRequest> asked, params HostCredentials?[] answers)
    {
        var queue = new Queue<HostCredentials?>(answers);
        return (request, _) =>
        {
            asked.Add(request);
            return ValueTask.FromResult(queue.Count > 1 ? queue.Dequeue() : queue.Peek());
        };
    }

    private static string Basic(string userid, string password) =>
        new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.Latin1.GetBytes($"{userid}:{password}"))).ToString();

    [Fact]
    public async Task Info_requires_auth_so_server_info_is_asked_with_credentials()
    {
        var handler = new RecordedHandler().Then("info-200");
        var asked = new List<HostCredentialRequest>();
        using var service = new MvsmfFileService(handler, Base, Answering(asked, new HostCredentials("MVSCE02", "pw")));

        var info = await service.GetServerInfoAsync(TestContext.Current.CancellationToken);

        Assert.Equal(new HostServerInfo("mvsMF", "1.0.0-dev", "MVS 3.8j"), info);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("http://mvs.test:8080/zosmf/info", request.Uri.ToString());
        Assert.Equal(Basic("MVSCE02", "pw"), request.Authorization);
        Assert.Equal(new[] { new HostCredentialRequest(false) }, asked);
    }

    [Fact]
    public async Task Basic_auth_every_request_sends_credentials_each_time()
    {
        var handler = new RecordedHandler().Then("info-200").Then("info-200");
        using var service = new MvsmfFileService(handler, Base, Answering([], new HostCredentials("MVSCE02", "pw")));

        await service.GetServerInfoAsync(TestContext.Current.CancellationToken);
        await service.GetServerInfoAsync(TestContext.Current.CancellationToken);

        Assert.All(handler.Requests, r => Assert.Equal(Basic("MVSCE02", "pw"), r.Authorization));
    }

    [Fact]
    public async Task A_401_asks_again_and_repeats_the_request_once()
    {
        var handler = new RecordedHandler().Then("info-401").Then("info-200");
        var asked = new List<HostCredentialRequest>();
        using var service = new MvsmfFileService(handler, Base,
            Answering(asked, new HostCredentials("MVSCE02", "wrong"), new HostCredentials("MVSCE02", "right")));

        var info = await service.GetServerInfoAsync(TestContext.Current.CancellationToken);

        Assert.Equal("1.0.0-dev", info.ProductVersion);
        Assert.Equal(new[] { new HostCredentialRequest(false), new HostCredentialRequest(true) }, asked);
        Assert.Equal(new[] { Basic("MVSCE02", "wrong"), Basic("MVSCE02", "right") }, handler.Requests.Select(r => r.Authorization!));
    }

    [Fact]
    public async Task A_second_401_fails_as_unauthenticated_without_the_password_in_the_message()
    {
        var handler = new RecordedHandler().Then("info-401").Then("info-401");
        using var service = new MvsmfFileService(handler, Base, Answering([], new HostCredentials("MVSCE02", "hunter22")));

        var ex = await Assert.ThrowsAsync<HostFileException>(() => service.GetServerInfoAsync(TestContext.Current.CancellationToken));

        Assert.Equal(HostFileErrorKind.Unauthenticated, ex.Kind);
        Assert.Equal(2, handler.Requests.Count);
        Assert.DoesNotContain("hunter22", ex.ToString());
    }

    [Fact]
    public async Task A_cancelled_prompt_sends_nothing()
    {
        var handler = new RecordedHandler();
        using var service = new MvsmfFileService(handler, Base, Answering([], (HostCredentials?)null));

        var ex = await Assert.ThrowsAsync<HostFileException>(() => service.GetServerInfoAsync(TestContext.Current.CancellationToken));

        Assert.Equal(HostFileErrorKind.Unauthenticated, ex.Kind);
        Assert.Equal("Sign-in was cancelled.", ex.Message);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task A_refused_connection_is_unreachable()
    {
        var handler = new RecordedHandler().Then((_, _) => throw new HttpRequestException("Connection refused (mvs.test:8080)"));
        using var service = new MvsmfFileService(handler, Base, Answering([], new HostCredentials("U", "p")));

        var ex = await Assert.ThrowsAsync<HostFileException>(() => service.GetServerInfoAsync(TestContext.Current.CancellationToken));

        Assert.Equal(HostFileErrorKind.Unreachable, ex.Kind);
        Assert.Equal("Server information: cannot reach the host (Connection refused (mvs.test:8080)).", ex.Message);
    }

    [Fact]
    public async Task An_unreadable_answer_is_a_server_error()
    {
        var handler = new RecordedHandler().Then(System.Net.HttpStatusCode.OK, "not json");
        using var service = new MvsmfFileService(handler, Base, Answering([], new HostCredentials("U", "p")));

        var ex = await Assert.ThrowsAsync<HostFileException>(() => service.GetServerInfoAsync(TestContext.Current.CancellationToken));

        Assert.Equal(HostFileErrorKind.ServerError, ex.Kind);
        Assert.Equal("Server information: the host's answer could not be read.", ex.Message);
    }

    [Fact]
    public void The_production_client_never_times_out_a_whole_transfer()
    {
        using var service = new MvsmfFileService(new MvsmfOptions(Base), Answering([], new HostCredentials("U", "p")));
        Assert.Equal(Timeout.InfiniteTimeSpan, service.HttpClientTimeout);
    }

    [Fact]
    public void The_production_handler_connects_within_ten_seconds_and_keeps_no_cookies()
    {
        using var handler = MvsmfFileService.CreateHandler(new MvsmfCertificateCheck(null));
        Assert.Equal(TimeSpan.FromSeconds(10), handler.ConnectTimeout);
        Assert.False(handler.UseCookies);
        Assert.False(handler.AllowAutoRedirect);
    }
}
```

(Replace any `Assert.Equal(new[] {  ...  }, x)` the compiler cannot type with `Assert.Equal(new[] { ... }, x)`.)

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/LizTerm.Backend.Mvsmf.Tests`
Expected: build FAILS (`MvsmfErrors`, `MvsmfFileService` not found).

- [ ] **Step 3: Implement**

`src/LizTerm.Backend.Mvsmf/MvsmfJson.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Text.Json;
using System.Text.Json.Serialization;

namespace LizTerm.Backend.Mvsmf;

internal sealed record MvsmfInfo(
    [property: JsonPropertyName("zosmf_version")] string? ZosmfVersion,
    [property: JsonPropertyName("zos_version")] string? ZosVersion);

/// <summary>mvsMF sends every attribute as a string ("lrecl":"80"); z/OSMF may send numbers, so attribute fields
/// read either.</summary>
internal sealed record MvsmfDataset(
    [property: JsonPropertyName("dsname")] string? Dsname,
    [property: JsonPropertyName("dsorg"), JsonConverter(typeof(LenientStringConverter))] string? Dsorg,
    [property: JsonPropertyName("recfm"), JsonConverter(typeof(LenientStringConverter))] string? Recfm,
    [property: JsonPropertyName("lrecl"), JsonConverter(typeof(LenientStringConverter))] string? Lrecl,
    [property: JsonPropertyName("blksz"), JsonConverter(typeof(LenientStringConverter))] string? Blksz,
    [property: JsonPropertyName("vol"), JsonConverter(typeof(LenientStringConverter))] string? Vol);

internal sealed record MvsmfDatasetList(
    [property: JsonPropertyName("items")] List<MvsmfDataset>? Items,
    [property: JsonPropertyName("moreRows")] bool? MoreRows);

internal sealed record MvsmfMember([property: JsonPropertyName("member")] string? Member);

internal sealed record MvsmfMemberList([property: JsonPropertyName("items")] List<MvsmfMember>? Items);

internal sealed record MvsmfError(
    [property: JsonPropertyName("rc")] int? Rc,
    [property: JsonPropertyName("category")] int? Category,
    [property: JsonPropertyName("reason")] int? Reason,
    [property: JsonPropertyName("message")] string? Message);

internal sealed class LenientStringConverter : JsonConverter<string?>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.TokenType switch
    {
        JsonTokenType.String => reader.GetString(),
        JsonTokenType.Number => reader.TryGetInt64(out var whole) ? whole.ToString(System.Globalization.CultureInfo.InvariantCulture) : reader.GetDouble().ToString(System.Globalization.CultureInfo.InvariantCulture),
        JsonTokenType.True => "true",
        JsonTokenType.False => "false",
        _ => SkipAndNull(ref reader),
    };

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options) => writer.WriteStringValue(value);

    private static string? SkipAndNull(ref Utf8JsonReader reader)
    {
        reader.Skip();
        return null;
    }
}

[JsonSerializable(typeof(MvsmfInfo))]
[JsonSerializable(typeof(MvsmfDatasetList))]
[JsonSerializable(typeof(MvsmfMemberList))]
[JsonSerializable(typeof(MvsmfError))]
internal partial class MvsmfJsonContext : JsonSerializerContext;
```

`src/LizTerm.Backend.Mvsmf/MvsmfErrors.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Net;
using System.Text.Json;
using LizTerm.Core.HostFiles;

namespace LizTerm.Backend.Mvsmf;

/// <summary>Turns an mvsMF failure into a <see cref="HostFileException"/>. The JSON body's category and reason
/// decide before the HTTP status does, because mvsMF uses 500 for outcomes that are not server faults.</summary>
internal static class MvsmfErrors
{
    private const int DatasetCategory = 6;
    private const int SecurityCategory = 4;

    public static HostFileException FromResponse(HttpStatusCode status, byte[] body, string what)
    {
        var error = TryParse(body);
        var kind = Classify(status, error?.Category, error?.Rc, error?.Reason);
        return new HostFileException(kind, Describe(kind, status, what, error), error?.Reason, error?.Message);
    }

    internal static HostFileErrorKind Classify(HttpStatusCode status, int? category, int? rc, int? reason)
    {
        if (status == HttpStatusCode.Unauthorized) return HostFileErrorKind.Unauthenticated;
        if (category == DatasetCategory && reason is 4 or 5) return HostFileErrorKind.NotFound;
        // mvsMF-compat: missing-read-is-500 — a missing member or dataset on read answers 500, reason 3, and the
        // same reason covers a dataset that exists but cannot be opened, so the two cannot be told apart.
        if (category == DatasetCategory && reason == 3) return HostFileErrorKind.CannotOpen;
        if (status == HttpStatusCode.NotFound) return HostFileErrorKind.NotFound;
        // mvsMF-compat: authorization-is-500 — a refused open is 500, category 4, rc 8, reason 0 ("LMOPEN error").
        if (category == SecurityCategory && rc == 8 && reason == 0) return HostFileErrorKind.NotAuthorized;
        if (status == HttpStatusCode.Forbidden) return HostFileErrorKind.NotAuthorized;
        if (status == HttpStatusCode.BadRequest) return HostFileErrorKind.InvalidRequest;
        return HostFileErrorKind.ServerError;
    }

    private static string Describe(HostFileErrorKind kind, HttpStatusCode status, string what, MvsmfError? error) => kind switch
    {
        HostFileErrorKind.Unauthenticated => "The host rejected the userid or password.",
        HostFileErrorKind.NotFound => $"{what}: not found.",
        HostFileErrorKind.CannotOpen => $"{what}: not found, not authorized, or cannot be opened.",
        HostFileErrorKind.NotAuthorized => $"{what}: not authorized.",
        HostFileErrorKind.InvalidRequest => $"{what}: the host refused the request ({error?.Message ?? "bad request"}).",
        _ => error?.Reason is { } reason
            ? $"{what}: server error (reason {reason})."
            : $"{what}: server error (HTTP {(int)status}).",
    };

    private static MvsmfError? TryParse(byte[] body)
    {
        if (body.Length == 0) return null;
        try
        {
            return JsonSerializer.Deserialize(body, MvsmfJsonContext.Default.MvsmfError);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
```

`src/LizTerm.Backend.Mvsmf/IdleTimeout.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

namespace LizTerm.Backend.Mvsmf;

/// <summary>A token that fires when nothing has arrived for <paramref name="idle"/>, or when the caller cancels. A
/// large download is never cut off while bytes keep coming.</summary>
internal sealed class IdleTimeout(TimeSpan idle, CancellationToken outer) : IDisposable
{
    private readonly TimeSpan _idle = idle;
    private readonly CancellationTokenSource _source = Start(idle, outer);

    public CancellationToken Token => _source.Token;

    /// <summary>Something arrived (or a new wait begins): restart the clock.</summary>
    public void Reset()
    {
        if (!_source.IsCancellationRequested) _source.CancelAfter(_idle);
    }

    /// <summary>Whether the token fired for idleness rather than because the caller cancelled.</summary>
    public bool Expired(CancellationToken caller) => _source.IsCancellationRequested && !caller.IsCancellationRequested;

    public void Dispose() => _source.Dispose();

    private static CancellationTokenSource Start(TimeSpan idle, CancellationToken outer)
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(outer);
        source.CancelAfter(idle);
        return source;
    }
}
```

Temporary `src/LizTerm.Backend.Mvsmf/MvsmfCertificateCheck.cs` (Task 10 replaces the body):

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using LizTerm.Core.Security;
using LizTerm.Core.Session;

namespace LizTerm.Backend.Mvsmf;

internal sealed class MvsmfCertificateCheck(CertificatePin? pin)
{
    public CertificatePin? Pin { get; } = pin;

    public PresentedCertificate? LastRejected => null;

    public bool Validate(object sender, X509Certificate? certificate, X509Chain? chain, SslPolicyErrors errors) =>
        errors == SslPolicyErrors.None;
}
```

`src/LizTerm.Backend.Mvsmf/MvsmfFileService.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Net;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using LizTerm.Core.HostFiles;

namespace LizTerm.Backend.Mvsmf;

/// <summary><see cref="IHostFileService"/> over mvsMF's z/OSMF REST subset. Stores no credentials: it asks the
/// provider before each request and once more after a 401. Every workaround for the build this was written against
/// carries an <c>mvsMF-compat</c> tag matching an entry in docs/mvsmf-compatibility.md.</summary>
public sealed class MvsmfFileService : IHostFileService
{
    internal static readonly TimeSpan DefaultIdleTimeout = TimeSpan.FromSeconds(30);
    internal static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(10);
    private const int CopyBufferSize = 81920;
    private static readonly string ProductVersion = typeof(MvsmfFileService).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    private readonly HttpClient _http;
    private readonly Uri _base;
    private readonly HostCredentialProvider _credentials;
    private readonly MvsmfCertificateCheck? _certificates;
    private readonly TimeSpan _idle;

    public MvsmfFileService(MvsmfOptions options, HostCredentialProvider credentials)
        : this(new MvsmfCertificateCheck(options.PinnedCertificate), options.BaseUrl, credentials)
    {
    }

    private MvsmfFileService(MvsmfCertificateCheck certificates, Uri baseUrl, HostCredentialProvider credentials)
        : this(CreateHandler(certificates), baseUrl, credentials, null, certificates)
    {
    }

    internal MvsmfFileService(HttpMessageHandler handler, Uri baseUrl, HostCredentialProvider credentials,
        TimeSpan? idleTimeout = null, MvsmfCertificateCheck? certificates = null)
    {
        _http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        _base = new Uri(baseUrl.AbsoluteUri.TrimEnd('/') + "/");
        _credentials = credentials;
        _idle = idleTimeout ?? DefaultIdleTimeout;
        _certificates = certificates;
    }

    internal TimeSpan HttpClientTimeout => _http.Timeout;

    internal static SocketsHttpHandler CreateHandler(MvsmfCertificateCheck certificates) => new()
    {
        ConnectTimeout = ConnectTimeout,
        // mvsMF-compat: basic-auth-every-request — this build sets no session cookie, so none is kept and every
        // request carries Basic credentials.
        UseCookies = false,
        AllowAutoRedirect = false,
        SslOptions = new SslClientAuthenticationOptions
        {
            RemoteCertificateValidationCallback = certificates.Validate,
            CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
        },
    };

    public async Task<HostServerInfo> GetServerInfoAsync(CancellationToken cancellationToken = default)
    {
        const string what = "Server information";
        using var idle = new IdleTimeout(_idle, cancellationToken);
        // mvsMF-compat: info-requires-auth — the docs say /info needs no credentials; this build demands them, so it
        // goes through the same authenticated path as everything else.
        using var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, Url("info")), what, idle, cancellationToken);
        var info = await ReadJsonAsync(response, MvsmfJsonContext.Default.MvsmfInfo, what, idle, cancellationToken);
        return new HostServerInfo("mvsMF", info.ZosmfVersion ?? "unknown", info.ZosVersion ?? "unknown");
    }

    public Task<IReadOnlyList<HostFileEntry>> ListDatasetsAsync(string pattern, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException("Task 7");

    public Task<IReadOnlyList<HostFileEntry>> ListMembersAsync(HostPath dataset, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException("Task 7");

    public Task<IReadOnlyList<string>> ReadTextAsync(HostPath path, IProgress<long>? progress = null, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException("Task 8");

    public Task<long> ReadBinaryAsync(HostPath path, Stream destination, IProgress<long>? progress = null, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException("Task 8");

    public Task WriteTextAsync(HostPath path, IReadOnlyList<string> lines, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException("Task 9");

    public Task WriteBinaryAsync(HostPath path, Stream source, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException("Task 9");

    public Task DeleteAsync(HostPath path, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException("Task 9");

    public void Dispose() => _http.Dispose();

    private Uri Url(string relative) => new(_base, relative);

    /// <summary><c>restfiles/ds/DSN</c> or <c>restfiles/ds/DSN(MEMBER)</c>.</summary>
    private static string DatasetPath(HostPath path) => path.Member is null
        ? $"restfiles/ds/{EscapeName(path.Dataset)}"
        : $"restfiles/ds/{EscapeName(path.Dataset)}({EscapeName(path.Member)})";

    /// <summary>Validated names hold only A-Z 0-9 . - # $ @ and, in filters, * and %. Of those only # and % mean
    /// something in a URL; the rest go as they are, exactly as curl sends them.</summary>
    internal static string EscapeName(string name) => name.Replace("%", "%25").Replace("#", "%23");

    private async Task<HttpResponseMessage> SendAsync(Func<HttpRequestMessage> build, string what, IdleTimeout idle, CancellationToken cancellationToken)
    {
        var credentials = await AskAsync(isRetry: false, cancellationToken);
        var response = await SendOnceAsync(build, credentials, what, idle, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            response.Dispose();
            credentials = await AskAsync(isRetry: true, cancellationToken);
            response = await SendOnceAsync(build, credentials, what, idle, cancellationToken);
        }
        if (response.IsSuccessStatusCode) return response;
        using (response)
        {
            using var body = new MemoryStream();
            await CopyBodyAsync(response, body, idle, null, what, cancellationToken);
            throw MvsmfErrors.FromResponse(response.StatusCode, body.ToArray(), what);
        }
    }

    private async Task<HostCredentials> AskAsync(bool isRetry, CancellationToken cancellationToken) =>
        await _credentials(new HostCredentialRequest(isRetry), cancellationToken)
        ?? throw new HostFileException(HostFileErrorKind.Unauthenticated, "Sign-in was cancelled.");

    private async Task<HttpResponseMessage> SendOnceAsync(Func<HttpRequestMessage> build, HostCredentials credentials,
        string what, IdleTimeout idle, CancellationToken cancellationToken)
    {
        using var request = build();
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.Latin1.GetBytes($"{credentials.Userid}:{credentials.Password}")));
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("LizTerm", ProductVersion));
        try
        {
            idle.Reset();
            return await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, idle.Token);
        }
        catch (OperationCanceledException) when (idle.Expired(cancellationToken))
        {
            throw TimedOut(what);
        }
        catch (HttpRequestException ex)
        {
            throw Unreachable(what, ex);
        }
    }

    /// <summary>Copies the body, restarting the idle clock on every chunk.</summary>
    private async Task<long> CopyBodyAsync(HttpResponseMessage response, Stream destination, IdleTimeout idle,
        IProgress<long>? progress, string what, CancellationToken cancellationToken)
    {
        var buffer = new byte[CopyBufferSize];
        long total = 0;
        try
        {
            await using var body = await response.Content.ReadAsStreamAsync(idle.Token);
            while (true)
            {
                idle.Reset();
                int read;
                try
                {
                    read = await body.ReadAsync(buffer, idle.Token);
                }
                catch (Exception ex) when (ex is HttpRequestException or IOException)
                {
                    // A host I/O error mid-stream drops the connection rather than ending the body cleanly.
                    throw new HostFileException(HostFileErrorKind.Unreachable, $"{what}: the connection dropped during the transfer.", inner: ex);
                }
                if (read == 0) return total;
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                total += read;
                progress?.Report(total);
            }
        }
        catch (OperationCanceledException) when (idle.Expired(cancellationToken))
        {
            throw TimedOut(what);
        }
    }

    private async Task<T> ReadJsonAsync<T>(HttpResponseMessage response, JsonTypeInfo<T> type, string what,
        IdleTimeout idle, CancellationToken cancellationToken)
    {
        using var body = new MemoryStream();
        await CopyBodyAsync(response, body, idle, null, what, cancellationToken);
        try
        {
            return JsonSerializer.Deserialize(body.ToArray(), type)
                ?? throw new JsonException("empty");
        }
        catch (JsonException ex)
        {
            throw new HostFileException(HostFileErrorKind.ServerError, $"{what}: the host's answer could not be read.", inner: ex);
        }
    }

    private HostFileException Unreachable(string what, HttpRequestException ex)
    {
        if (IsCertificateFailure(ex) && _certificates?.LastRejected is { } presented)
        {
            return new HostFileException(HostFileErrorKind.CertificateRejected,
                $"{what}: the host's certificate is not trusted.", certificate: presented, inner: ex);
        }
        return new HostFileException(HostFileErrorKind.Unreachable, $"{what}: cannot reach the host ({ex.Message}).", inner: ex);
    }

    private static bool IsCertificateFailure(Exception ex)
    {
        for (Exception? current = ex; current is not null; current = current.InnerException)
        {
            if (current is AuthenticationException) return true;
        }
        return false;
    }

    private static HostFileException TimedOut(string what) =>
        new(HostFileErrorKind.Unreachable, $"{what}: the host stopped answering (no data for {DefaultIdleTimeout.TotalSeconds:0} s).");
}
```

Note on `TimedOut`: the message states the production 30 s even when a test shortens the timeout; tests assert only that it starts with `{what}: the host stopped answering`.

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test tests/LizTerm.Backend.Mvsmf.Tests`
Expected: PASS. If `Info_requires_auth...` fails on the URI, check `_base` keeps `/zosmf/` so `new Uri(_base, "info")` gives `/zosmf/info`.

- [ ] **Step 5: Commit**

```bash
git add src/LizTerm.Backend.Mvsmf tests/LizTerm.Backend.Mvsmf.Tests
git commit -m "Send mvsMF requests with Basic auth, one re-prompt on 401, and reason-based errors

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 7: Dataset and member lists

**Files:**
- Modify: `src/LizTerm.Backend.Mvsmf/MvsmfFileService.cs` (replace the `ListDatasetsAsync` and `ListMembersAsync` stubs; add `ToEntry`, `Blank`, `Number`)
- Test: `tests/LizTerm.Backend.Mvsmf.Tests/MvsmfListTests.cs`

**Interfaces:**
- Consumes: `SendAsync`, `ReadJsonAsync`, `Url`, `DatasetPath`, `EscapeName` (Task 6); `MvsmfAuthTests.Base`, `MvsmfAuthTests.Answering` (Task 6).
- Produces: working `ListDatasetsAsync(string pattern, CancellationToken)` and `ListMembersAsync(HostPath dataset, CancellationToken)`.

- [ ] **Step 1: Write the failing tests**

`tests/LizTerm.Backend.Mvsmf.Tests/MvsmfListTests.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Net;
using LizTerm.Core.HostFiles;

namespace LizTerm.Backend.Mvsmf.Tests;

public class MvsmfListTests
{
    private static MvsmfFileService Service(RecordedHandler handler) =>
        new(handler, MvsmfAuthTests.Base, MvsmfAuthTests.Answering([], new HostCredentials("MVSCE02", "pw")));

    [Fact]
    public async Task Dataset_list_reads_names_and_attributes()
    {
        var handler = new RecordedHandler().Then("ds-list-sys1");
        using var service = Service(handler);

        var entries = await service.ListDatasetsAsync("sys1.**", TestContext.Current.CancellationToken);

        Assert.True(entries.Count > 3);
        Assert.Equal("SYS1.ACMDLIB", entries[0].Name);
        Assert.All(entries, e => Assert.Equal(HostFileEntryKind.Dataset, e.Kind));
        var proclib = entries.Single(e => e.Name == "SYS1.PROCLIB").Attributes!;
        Assert.Equal("PO", proclib.Dsorg);
        Assert.Equal("FB", proclib.Recfm);
        Assert.Equal(80, proclib.Lrecl);
        Assert.True(proclib.IsPartitioned);
        Assert.Equal("/zosmf/restfiles/ds?dslevel=SYS1.**", handler.Requests[0].Uri.PathAndQuery);
    }

    [Fact]
    public async Task Dataset_list_ignores_start_so_the_whole_list_is_asked_for()
    {
        var handler = new RecordedHandler().Then("ds-list-sys1");
        using var service = Service(handler);

        await service.ListDatasetsAsync("SYS1.**", TestContext.Current.CancellationToken);

        var request = Assert.Single(handler.Requests);
        Assert.DoesNotContain("X-IBM-Max-Items", request.HeaderNames, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("start=", request.Uri.Query);
    }

    [Fact]
    public async Task Dataset_list_morerows_false_is_a_complete_empty_list()
    {
        using var service = Service(new RecordedHandler().Then("ds-list-empty"));
        Assert.Empty(await service.ListDatasetsAsync("NOSUCH.HLQ", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task The_filter_is_folded_and_its_hash_and_percent_are_escaped()
    {
        var handler = new RecordedHandler().Then(HttpStatusCode.OK, """{"items":[],"moreRows":false}""");
        using var service = Service(handler);

        await service.ListDatasetsAsync(" sys1.#a% ", TestContext.Current.CancellationToken);

        Assert.Equal("/zosmf/restfiles/ds?dslevel=SYS1.%23A%25", handler.Requests[0].Uri.PathAndQuery);
    }

    [Fact]
    public async Task A_bad_filter_is_refused_without_a_request()
    {
        var handler = new RecordedHandler();
        using var service = Service(handler);

        var ex = await Assert.ThrowsAsync<HostFileException>(() => service.ListDatasetsAsync("SYS1.A B", TestContext.Current.CancellationToken));

        Assert.Equal(HostFileErrorKind.InvalidRequest, ex.Kind);
        Assert.Equal("A filter cannot contain ' '.", ex.Message);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Numeric_attributes_and_nameless_items_are_tolerated()
    {
        var handler = new RecordedHandler().Then(HttpStatusCode.OK,
            """{"items":[{"dsname":"A.B","dsorg":"PS","recfm":"VB","lrecl":255,"blksz":6233,"vol":"V1"},{"dsorg":"PO"},{"dsname":"C.D","lrecl":"","dsorg":""}]}""");
        using var service = Service(handler);

        var entries = await service.ListDatasetsAsync("A.**", TestContext.Current.CancellationToken);

        Assert.Equal(new[] { "A.B", "C.D" }, entries.Select(e => e.Name));
        Assert.Equal(new DatasetAttributes("PS", "VB", 255, 6233, "V1"), entries[0].Attributes);
        Assert.Equal(new DatasetAttributes(null, null, null, null, null), entries[1].Attributes);
    }

    [Fact]
    public async Task Member_list_reads_every_name()
    {
        var handler = new RecordedHandler().Then("members-proclib");
        using var service = Service(handler);

        var members = await service.ListMembersAsync(HostPath.ForDataset("SYS1.PROCLIB"), TestContext.Current.CancellationToken);

        Assert.Equal("ASMFC", members[0].Name);
        Assert.Contains(members, m => m.Name == "JES2");
        Assert.All(members, m => Assert.Equal(HostFileEntryKind.Member, m.Kind));
        Assert.All(members, m => Assert.Null(m.Attributes));
        Assert.Equal("/zosmf/restfiles/ds/SYS1.PROCLIB/member", handler.Requests[0].Uri.PathAndQuery);
    }

    [Fact]
    public async Task Member_list_ignores_max_items_so_none_is_sent()
    {
        var handler = new RecordedHandler().Then("members-proclib");
        using var service = Service(handler);

        await service.ListMembersAsync(HostPath.ForDataset("SYS1.PROCLIB"), TestContext.Current.CancellationToken);

        Assert.DoesNotContain("X-IBM-Max-Items", handler.Requests[0].HeaderNames, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Member_list_empty_for_missing_dataset_is_not_an_error()
    {
        using var service = Service(new RecordedHandler().Then("members-missing-dataset"));
        Assert.Empty(await service.ListMembersAsync(HostPath.ForDataset("MVSCE02.NOSUCH"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_member_path_cannot_be_listed()
    {
        using var service = Service(new RecordedHandler());
        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.ListMembersAsync(HostPath.ForMember("SYS1.PROCLIB", "JES2"), TestContext.Current.CancellationToken));
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/LizTerm.Backend.Mvsmf.Tests --filter "FullyQualifiedName~MvsmfListTests"`
Expected: FAIL with `NotImplementedException: Task 7` (the bad-filter and member-path tests fail too, for the same reason).

- [ ] **Step 3: Implement**

In `MvsmfFileService.cs`, add `using System.Globalization;` and replace the two stubs with:

```csharp
    public async Task<IReadOnlyList<HostFileEntry>> ListDatasetsAsync(string pattern, CancellationToken cancellationToken = default)
    {
        if (HostPath.DatasetPatternError(pattern) is { } error) throw new HostFileException(HostFileErrorKind.InvalidRequest, error);
        const string what = "Dataset list";
        var filter = pattern.Trim().ToUpperInvariant();
        using var idle = new IdleTimeout(_idle, cancellationToken);
        // mvsMF-compat: dataset-list-ignores-start — this build ignores start, so a list cannot be paged; the whole
        // list is asked for, with no X-IBM-Max-Items.
        using var response = await SendAsync(
            () => new HttpRequestMessage(HttpMethod.Get, Url($"restfiles/ds?dslevel={EscapeName(filter)}")), what, idle, cancellationToken);
        var list = await ReadJsonAsync(response, MvsmfJsonContext.Default.MvsmfDatasetList, what, idle, cancellationToken);
        // mvsMF-compat: dataset-list-morerows-false — moreRows arrives as false rather than absent; with no item limit
        // it is never true, so it is not read.
        return [.. (list.Items ?? Enumerable.Empty<MvsmfDataset>()).Where(d => !string.IsNullOrWhiteSpace(d.Dsname)).Select(ToEntry)];
    }

    public async Task<IReadOnlyList<HostFileEntry>> ListMembersAsync(HostPath dataset, CancellationToken cancellationToken = default)
    {
        if (dataset.Kind != HostPathKind.Dataset) throw new ArgumentException("Only a dataset has members.", nameof(dataset));
        var what = dataset.ToString();
        using var idle = new IdleTimeout(_idle, cancellationToken);
        // mvsMF-compat: member-list-ignores-max-items — the host returns every member whatever limit is asked, so
        // none is sent.
        using var response = await SendAsync(
            () => new HttpRequestMessage(HttpMethod.Get, Url(DatasetPath(dataset) + "/member")), what, idle, cancellationToken);
        var list = await ReadJsonAsync(response, MvsmfJsonContext.Default.MvsmfMemberList, what, idle, cancellationToken);
        // mvsMF-compat: member-list-empty-for-missing-dataset — a missing or sequential dataset answers 200 with no
        // items, so an empty list is passed on as it is; IHostFileService tells callers to confirm the dataset.
        return [.. (list.Items ?? Enumerable.Empty<MvsmfMember>())
            .Where(m => !string.IsNullOrWhiteSpace(m.Member))
            .Select(m => new HostFileEntry(m.Member!.Trim(), HostFileEntryKind.Member))];
    }

    private static HostFileEntry ToEntry(MvsmfDataset dataset) => new(
        dataset.Dsname!.Trim(),
        HostFileEntryKind.Dataset,
        new DatasetAttributes(Blank(dataset.Dsorg), Blank(dataset.Recfm), Number(dataset.Lrecl), Number(dataset.Blksz), Blank(dataset.Vol)));

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static int? Number(string? value) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? number : null;
```

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test tests/LizTerm.Backend.Mvsmf.Tests --filter "FullyQualifiedName~MvsmfListTests"`
Expected: PASS. (.NET keeps `%23` and `%25` escaped in `PathAndQuery`.)

- [ ] **Step 5: Commit**

```bash
git add src/LizTerm.Backend.Mvsmf/MvsmfFileService.cs tests/LizTerm.Backend.Mvsmf.Tests/MvsmfListTests.cs
git commit -m "List mvsMF datasets and members as whole lists

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 8: Reads, with the idle timeout

**Files:**
- Modify: `src/LizTerm.Backend.Mvsmf/MvsmfFileService.cs` (replace the `ReadTextAsync` and `ReadBinaryAsync` stubs; add `Get`, `SplitRecords`)
- Test: `tests/LizTerm.Backend.Mvsmf.Tests/MvsmfReadTests.cs`, `tests/LizTerm.Backend.Mvsmf.Tests/TestStreams.cs`

**Interfaces:**
- Consumes: Task 6 helpers.
- Produces: working reads; `internal static List<string> SplitRecords(ReadOnlySpan<byte> body)`; test helpers `StallingStream`, `FailingStream`, `ListProgress`.

- [ ] **Step 1: Write the failing tests**

`tests/LizTerm.Backend.Mvsmf.Tests/TestStreams.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

namespace LizTerm.Backend.Mvsmf.Tests;

/// <summary>A response body that never delivers a byte until its read is cancelled.</summary>
internal sealed class StallingStream : ReadOnlyTestStream
{
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        await Task.Delay(Timeout.Infinite, cancellationToken);
        return 0;
    }
}

/// <summary>A response body whose connection drops on the first read.</summary>
internal sealed class FailingStream : ReadOnlyTestStream
{
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        ValueTask.FromException<int>(new IOException("connection reset"));
}

internal abstract class ReadOnlyTestStream : Stream
{
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

/// <summary>Records every report synchronously, unlike <see cref="Progress{T}"/>, which posts.</summary>
internal sealed class ListProgress : IProgress<long>
{
    public List<long> Values { get; } = [];
    public void Report(long value) => Values.Add(value);
}
```

`tests/LizTerm.Backend.Mvsmf.Tests/MvsmfReadTests.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Net;
using LizTerm.Core.HostFiles;

namespace LizTerm.Backend.Mvsmf.Tests;

public class MvsmfReadTests
{
    private static readonly HostPath Jes2 = HostPath.ForMember("SYS1.PROCLIB", "JES2");

    private static MvsmfFileService Service(RecordedHandler handler, TimeSpan? idle = null) =>
        new(handler, MvsmfAuthTests.Base, MvsmfAuthTests.Answering([], new HostCredentials("MVSCE02", "pw")), idle);

    private static RecordedHandler Answering(byte[] body) => new RecordedHandler().Then((_, _) =>
        Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) }));

    private static RecordedHandler Answering(Stream body) => new RecordedHandler().Then((_, _) =>
        Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(body) }));

    [Fact]
    public async Task A_text_read_returns_one_line_per_record()
    {
        var handler = new RecordedHandler().Then("read-text-jes2");
        using var service = Service(handler);

        var lines = await service.ReadTextAsync(Jes2, cancellationToken: TestContext.Current.CancellationToken);

        Assert.StartsWith("//JES2    PROC M=JES2PM00,", lines[0]);
        Assert.Equal(80, lines[0].Length);
        Assert.EndsWith("00000010", lines[0]);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("text", request.DataType);
        Assert.Equal("/zosmf/restfiles/ds/SYS1.PROCLIB(JES2)", request.Uri.PathAndQuery);
    }

    [Fact]
    public async Task Text_body_is_latin1()
    {
        using var service = Service(Answering([0x41, 0xAC, 0xA2, 0x42, 0x0A]));
        Assert.Equal(new[] { "A¬¢B" }, await service.ReadTextAsync(Jes2, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Text_read_keeps_trailing_blanks_and_blank_records()
    {
        using var service = Service(Answering("AB  \n\nC\n"u8.ToArray()));
        Assert.Equal(new[] { "AB  ", "", "C" }, await service.ReadTextAsync(Jes2, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("", new string[0])]
    [InlineData("A", new[] { "A" })]
    [InlineData("A\n", new[] { "A" })]
    [InlineData("A\r\n", new[] { "A\r" })]
    public void SplitRecords_splits_at_lf_only(string body, string[] expected) =>
        Assert.Equal(expected, MvsmfFileService.SplitRecords(System.Text.Encoding.Latin1.GetBytes(body)));

    [Fact]
    public async Task A_binary_read_copies_every_byte_and_reports_progress()
    {
        var handler = new RecordedHandler().Then("read-binary-jes2");
        using var service = Service(handler);
        using var destination = new MemoryStream();
        var progress = new ListProgress();

        var count = await service.ReadBinaryAsync(Jes2, destination, progress, TestContext.Current.CancellationToken);

        var expected = Fixture.Body("read-binary-jes2");
        Assert.Equal(expected, destination.ToArray());
        Assert.Equal(expected.Length, count);
        Assert.Equal(expected.Length, progress.Values[^1]);
        Assert.Equal("binary", handler.Requests[0].DataType);
    }

    [Fact]
    public async Task A_missing_member_cannot_be_opened()
    {
        using var service = Service(new RecordedHandler().Then("read-missing-member"));
        var ex = await Assert.ThrowsAsync<HostFileException>(() =>
            service.ReadTextAsync(HostPath.ForMember("SYS1.PROCLIB", "NOSUCHMB"), cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(HostFileErrorKind.CannotOpen, ex.Kind);
    }

    [Fact]
    public async Task A_host_that_stops_sending_times_out()
    {
        using var service = Service(Answering(new StallingStream()), idle: TimeSpan.FromMilliseconds(200));

        var ex = await Assert.ThrowsAsync<HostFileException>(() =>
            service.ReadBinaryAsync(Jes2, new MemoryStream(), cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(HostFileErrorKind.Unreachable, ex.Kind);
        Assert.StartsWith("SYS1.PROCLIB(JES2): the host stopped answering", ex.Message);
    }

    [Fact]
    public async Task Cancelling_is_not_reported_as_a_timeout()
    {
        using var service = Service(Answering(new StallingStream()));
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancel.CancelAfter(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ReadBinaryAsync(Jes2, new MemoryStream(), cancellationToken: cancel.Token));
    }

    [Fact]
    public async Task A_dropped_connection_is_unreachable()
    {
        using var service = Service(Answering(new FailingStream()));

        var ex = await Assert.ThrowsAsync<HostFileException>(() =>
            service.ReadTextAsync(Jes2, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(HostFileErrorKind.Unreachable, ex.Kind);
        Assert.Equal("SYS1.PROCLIB(JES2): the connection dropped during the transfer.", ex.Message);
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/LizTerm.Backend.Mvsmf.Tests --filter "FullyQualifiedName~MvsmfReadTests"`
Expected: build FAILS (`SplitRecords` not found).

- [ ] **Step 3: Implement**

Replace the two read stubs in `MvsmfFileService.cs` with:

```csharp
    public async Task<IReadOnlyList<string>> ReadTextAsync(HostPath path, IProgress<long>? progress = null, CancellationToken cancellationToken = default)
    {
        var what = path.ToString();
        using var idle = new IdleTimeout(_idle, cancellationToken);
        using var response = await SendAsync(() => Get(path, "text"), what, idle, cancellationToken);
        using var body = new MemoryStream();
        await CopyBodyAsync(response, body, idle, progress, what, cancellationToken);
        // mvsMF-compat: text-read-keeps-trailing-blanks — fixed records arrive padded; HostFileTransfer trims them.
        return SplitRecords(body.GetBuffer().AsSpan(0, (int)body.Length));
    }

    public async Task<long> ReadBinaryAsync(HostPath path, Stream destination, IProgress<long>? progress = null, CancellationToken cancellationToken = default)
    {
        var what = path.ToString();
        using var idle = new IdleTimeout(_idle, cancellationToken);
        using var response = await SendAsync(() => Get(path, "binary"), what, idle, cancellationToken);
        return await CopyBodyAsync(response, destination, idle, progress, what, cancellationToken);
    }

    private HttpRequestMessage Get(HostPath path, string dataType)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, Url(DatasetPath(path)));
        request.Headers.Add("X-IBM-Data-Type", dataType);
        return request;
    }

    /// <summary>One string per record: records end in LF, and a CR is data.</summary>
    internal static List<string> SplitRecords(ReadOnlySpan<byte> body)
    {
        // mvsMF-compat: text-body-is-latin1 — the body is ISO-8859-1 whatever the headers say.
        var lines = Encoding.Latin1.GetString(body).Split('\n').ToList();
        if (lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
        return lines;
    }
```

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test tests/LizTerm.Backend.Mvsmf.Tests --filter "FullyQualifiedName~MvsmfReadTests"`
Expected: PASS, the timeout test in well under a second.

- [ ] **Step 5: Commit**

```bash
git add src/LizTerm.Backend.Mvsmf/MvsmfFileService.cs tests/LizTerm.Backend.Mvsmf.Tests
git commit -m "Read mvsMF text as Latin-1 records and binary as bytes, with a 30 s idle limit

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 9: Writes and member delete

**Files:**
- Modify: `src/LizTerm.Backend.Mvsmf/MvsmfFileService.cs` (replace the `WriteTextAsync`, `WriteBinaryAsync`, `DeleteAsync` stubs; add `PutAsync`, `EncodeText`)
- Test: `tests/LizTerm.Backend.Mvsmf.Tests/MvsmfWriteTests.cs`

**Interfaces:**
- Consumes: Task 6 helpers.
- Produces: working writes and delete; `internal static byte[] EncodeText(IReadOnlyList<string> lines)`.

The request body is built once and reused, so the repeat after a 401 sends the same bytes; a binary source is therefore read into memory first (MVS members are small).

- [ ] **Step 1: Write the failing tests**

`tests/LizTerm.Backend.Mvsmf.Tests/MvsmfWriteTests.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Net;
using System.Text;
using LizTerm.Core.HostFiles;

namespace LizTerm.Backend.Mvsmf.Tests;

public class MvsmfWriteTests
{
    private static readonly HostPath NewMember = HostPath.ForMember("MVSCE02.CNTL", "NEWMEM");

    private static MvsmfFileService Service(RecordedHandler handler) =>
        new(handler, MvsmfAuthTests.Base, MvsmfAuthTests.Answering([], new HostCredentials("MVSCE02", "pw")));

    [Fact]
    public async Task A_text_write_puts_latin1_records_ending_in_lf()
    {
        var handler = new RecordedHandler().Then("write-204");
        using var service = Service(handler);

        await service.WriteTextAsync(NewMember, ["//A JOB", "¬¢"], TestContext.Current.CancellationToken);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Put, request.Method);
        Assert.Equal("/zosmf/restfiles/ds/MVSCE02.CNTL(NEWMEM)", request.Uri.PathAndQuery);
        Assert.Equal("text", request.DataType);
        Assert.Equal("text/plain", request.ContentType);
        Assert.Equal(Encoding.Latin1.GetBytes("//A JOB\n¬¢\n"), request.Body);
    }

    [Fact]
    public async Task Text_write_drops_empty_lines_so_each_is_sent_as_one_blank()
    {
        var handler = new RecordedHandler().Then("write-204");
        using var service = Service(handler);

        await service.WriteTextAsync(NewMember, ["A", "", "B"], TestContext.Current.CancellationToken);

        Assert.Equal("A\n \nB\n", handler.Requests[0].BodyText);
    }

    [Fact]
    public async Task Put_json_is_rename_so_no_write_ever_sends_json()
    {
        var handler = new RecordedHandler().Then("write-204").Then("write-204");
        using var service = Service(handler);

        await service.WriteTextAsync(NewMember, ["A"], TestContext.Current.CancellationToken);
        await service.WriteBinaryAsync(NewMember, new MemoryStream([1, 2]), TestContext.Current.CancellationToken);

        Assert.All(handler.Requests, r => Assert.DoesNotContain("json", r.ContentType ?? "", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task No_lines_send_an_empty_body()
    {
        var handler = new RecordedHandler().Then("write-204");
        using var service = Service(handler);

        await service.WriteTextAsync(NewMember, [], TestContext.Current.CancellationToken);

        Assert.Empty(handler.Requests[0].Body!);
    }

    [Theory]
    [InlineData("price 5€")]
    [InlineData("two\nlines")]
    [InlineData("two\rlines")]
    public async Task A_line_the_host_cannot_store_is_refused_before_sending(string line)
    {
        var handler = new RecordedHandler();
        using var service = Service(handler);

        await Assert.ThrowsAsync<ArgumentException>(() => service.WriteTextAsync(NewMember, [line], TestContext.Current.CancellationToken));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task A_binary_write_puts_the_bytes_as_an_octet_stream()
    {
        var handler = new RecordedHandler().Then("write-204");
        using var service = Service(handler);

        await service.WriteBinaryAsync(NewMember, new MemoryStream([0x61, 0x61, 0x00, 0xFF]), TestContext.Current.CancellationToken);

        var request = handler.Requests[0];
        Assert.Equal("binary", request.DataType);
        Assert.Equal("application/octet-stream", request.ContentType);
        Assert.Equal(new byte[] { 0x61, 0x61, 0x00, 0xFF }, request.Body);
    }

    [Fact]
    public async Task The_repeat_after_a_401_sends_the_same_body()
    {
        var handler = new RecordedHandler().Then(HttpStatusCode.Unauthorized).Then("write-204")
            .Then(HttpStatusCode.Unauthorized).Then("write-204");
        using var service = Service(handler);

        await service.WriteTextAsync(NewMember, ["A", "B"], TestContext.Current.CancellationToken);
        await service.WriteBinaryAsync(NewMember, new MemoryStream([9, 8, 7]), TestContext.Current.CancellationToken);

        Assert.Equal(handler.Requests[0].Body, handler.Requests[1].Body);
        Assert.Equal(handler.Requests[2].Body, handler.Requests[3].Body);
        Assert.Equal(new byte[] { 9, 8, 7 }, handler.Requests[3].Body);
    }

    [Fact]
    public async Task A_member_is_deleted()
    {
        var handler = new RecordedHandler().Then("delete-204");
        using var service = Service(handler);

        await service.DeleteAsync(NewMember, TestContext.Current.CancellationToken);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Delete, request.Method);
        Assert.Equal("/zosmf/restfiles/ds/MVSCE02.CNTL(NEWMEM)", request.Uri.PathAndQuery);
    }

    [Fact]
    public async Task Deleting_a_missing_member_is_not_found()
    {
        using var service = Service(new RecordedHandler().Then("delete-missing"));

        var ex = await Assert.ThrowsAsync<HostFileException>(() => service.DeleteAsync(NewMember, TestContext.Current.CancellationToken));

        Assert.Equal(HostFileErrorKind.NotFound, ex.Kind);
        Assert.Equal("MVSCE02.CNTL(NEWMEM): not found.", ex.Message);
    }

    [Fact]
    public async Task A_whole_dataset_is_never_deleted()
    {
        var handler = new RecordedHandler();
        using var service = Service(handler);

        await Assert.ThrowsAsync<NotSupportedException>(() =>
            service.DeleteAsync(HostPath.ForDataset("MVSCE02.CNTL"), TestContext.Current.CancellationToken));

        Assert.Empty(handler.Requests);
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/LizTerm.Backend.Mvsmf.Tests --filter "FullyQualifiedName~MvsmfWriteTests"`
Expected: FAIL with `NotImplementedException: Task 9`.

- [ ] **Step 3: Implement**

Replace the three stubs in `MvsmfFileService.cs` with:

```csharp
    public Task WriteTextAsync(HostPath path, IReadOnlyList<string> lines, CancellationToken cancellationToken = default) =>
        PutAsync(path, EncodeText(lines), "text", "text/plain", cancellationToken);

    public async Task WriteBinaryAsync(HostPath path, Stream source, CancellationToken cancellationToken = default)
    {
        // Held in memory so the repeat after a 401 can send the same bytes.
        using var copy = new MemoryStream();
        await source.CopyToAsync(copy, cancellationToken);
        await PutAsync(path, copy.ToArray(), "binary", "application/octet-stream", cancellationToken);
    }

    public async Task DeleteAsync(HostPath path, CancellationToken cancellationToken = default)
    {
        if (path.Kind != HostPathKind.Member) throw new NotSupportedException("Deleting a whole dataset is not supported in this release.");
        var what = path.ToString();
        using var idle = new IdleTimeout(_idle, cancellationToken);
        using var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Delete, Url(DatasetPath(path))), what, idle, cancellationToken);
    }

    private async Task PutAsync(HostPath path, byte[] body, string dataType, string contentType, CancellationToken cancellationToken)
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
            return request;
        }, what, idle, cancellationToken);
    }

    /// <summary>The wire form of text: each line, then LF, in ISO-8859-1.</summary>
    /// <exception cref="ArgumentException">A line holds a line break or a character outside Latin-1.</exception>
    internal static byte[] EncodeText(IReadOnlyList<string> lines)
    {
        var text = new StringBuilder();
        foreach (var line in lines)
        {
            if (line.AsSpan().IndexOfAny('\r', '\n') >= 0)
                throw new ArgumentException("A line cannot hold a line break.", nameof(lines));
            if (line.AsSpan().IndexOfAnyExceptInRange('\0', 'ÿ') >= 0)
                throw new ArgumentException("A line holds a character outside Latin-1; check the text with TextUploadCheck first.", nameof(lines));
            // mvsMF-compat: text-write-drops-empty-lines — the host drops an empty line but stores a single blank as
            // a blank record.
            text.Append(line.Length == 0 ? " " : line).Append('\n');
        }
        // mvsMF-compat: text-body-is-latin1 — the host reads the body as ISO-8859-1 whatever charset says.
        // mvsMF-compat: text-write-truncates-silently — an over-long line is cut to the record length and still
        // answered 204; TextUploadCheck refuses such lines before they reach this method.
        return Encoding.Latin1.GetBytes(text.ToString());
    }
```

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test tests/LizTerm.Backend.Mvsmf.Tests`
Expected: PASS (every backend test so far). `grep -n NotImplementedException src/LizTerm.Backend.Mvsmf/MvsmfFileService.cs` prints nothing.

- [ ] **Step 5: Commit**

```bash
git add src/LizTerm.Backend.Mvsmf/MvsmfFileService.cs tests/LizTerm.Backend.Mvsmf.Tests/MvsmfWriteTests.cs
git commit -m "Write mvsMF members as Latin-1 text or bytes and delete members

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 10: TLS trust and pins

**Files:**
- Modify: `src/LizTerm.Backend.Mvsmf/MvsmfCertificateCheck.cs` (replace the temporary body)
- Modify: `src/LizTerm.Core/Security/SslStreamCertificateFetcher.cs` (`SelectPresented`: `internal` → `public`)
- Create: `tests/LizTerm.Backend.Mvsmf.Tests/LoopbackHttpsServer.cs`
- Test: `tests/LizTerm.Backend.Mvsmf.Tests/MvsmfTlsTests.cs`

**Interfaces:**
- Consumes: `CertificateReader.Fingerprint`, `CertificateReader.SameFingerprint`, `CertificateReader.Read`, `SslStreamCertificateFetcher.SelectPresented`, `CertificatePin(string Sha256, string Subject, string Pem)`; test `TestCertificates.SelfSigned`, `TestCertificates.WithUsableKey`.
- Produces: `MvsmfCertificateCheck(CertificatePin? pin)` with `Pin`, `LastRejected`, `Validate(object sender, X509Certificate? certificate, X509Chain? chain, SslPolicyErrors errors)`; test `LoopbackHttpsServer(X509Certificate2 certificate, string json)` with `Port`, `DisposeAsync()`.

Spec §3.4. With a pin, the leaf's SHA-256 must equal the pin and nothing else is checked (the engine's pins likewise accept any host name). Without one, the system's verdict stands. A rejected certificate is kept so the service can report what was presented, for the App's certificate window (PR 2).

- [ ] **Step 1: Write the failing tests**

`tests/LizTerm.Backend.Mvsmf.Tests/LoopbackHttpsServer.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace LizTerm.Backend.Mvsmf.Tests;

/// <summary>An HTTPS server on loopback that answers every request with one JSON body.</summary>
internal sealed class LoopbackHttpsServer : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _loop;

    public LoopbackHttpsServer(X509Certificate2 certificate, string json)
    {
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _loop = Task.Run(() => ServeAsync(certificate, json));
    }

    public int Port { get; }

    private async Task ServeAsync(X509Certificate2 certificate, string json)
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_stop.Token);
            }
            catch (Exception) when (_stop.IsCancellationRequested)
            {
                return;
            }
            _ = Task.Run(() => AnswerAsync(client, certificate, json));
        }
    }

    private static async Task AnswerAsync(TcpClient client, X509Certificate2 certificate, string json)
    {
        using (client)
        {
            try
            {
                await using var tls = new SslStream(client.GetStream());
                await tls.AuthenticateAsServerAsync(certificate, clientCertificateRequired: false, checkCertificateRevocation: false);
                var seen = new List<byte>();
                var buffer = new byte[4096];
                while (seen.Count < 4 || !seen.TakeLast(4).SequenceEqual("\r\n\r\n"u8.ToArray()))
                {
                    var read = await tls.ReadAsync(buffer);
                    if (read == 0) return;
                    seen.AddRange(buffer.Take(read));
                }
                var body = Encoding.UTF8.GetBytes(json);
                var head = Encoding.ASCII.GetBytes(
                    $"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
                await tls.WriteAsync(head);
                await tls.WriteAsync(body);
                await tls.FlushAsync();
            }
            catch (Exception)
            {
                // A client that rejects the certificate hangs up mid-handshake; that is the point of some tests.
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        _listener.Stop();
        await _loop;
        _stop.Dispose();
    }
}
```

`tests/LizTerm.Backend.Mvsmf.Tests/MvsmfTlsTests.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Net.Security;
using LizTerm.Core.HostFiles;
using LizTerm.Core.Security;
using LizTerm.Core.Session;
using LizTerm.Core.Tests.Security;

namespace LizTerm.Backend.Mvsmf.Tests;

public class MvsmfTlsTests
{
    private const string Info = """{"zosmf_version":"tls-test","zos_version":"MVS 3.8j"}""";

    private static MvsmfFileService Service(int port, CertificatePin? pin) => new(
        new MvsmfOptions(new Uri($"https://localhost:{port}/zosmf"), pin),
        MvsmfAuthTests.Answering([], new HostCredentials("U", "p")));

    private static CertificatePin PinFor(System.Security.Cryptography.X509Certificates.X509Certificate2 certificate) =>
        new(CertificateReader.Fingerprint(certificate), certificate.Subject, certificate.ExportCertificatePem());

    [Fact]
    public async Task An_untrusted_certificate_is_rejected_and_described()
    {
        using var certificate = TestCertificates.WithUsableKey(TestCertificates.SelfSigned());
        await using var server = new LoopbackHttpsServer(certificate, Info);
        using var service = Service(server.Port, pin: null);

        var ex = await Assert.ThrowsAsync<HostFileException>(() => service.GetServerInfoAsync(TestContext.Current.CancellationToken));

        Assert.Equal(HostFileErrorKind.CertificateRejected, ex.Kind);
        Assert.Equal("Server information: the host's certificate is not trusted.", ex.Message);
        Assert.Equal(CertificateReader.Fingerprint(certificate), ex.Certificate!.Sha256);
        Assert.True(ex.Certificate.Pinnable, ex.Certificate.NotPinnableReason);
    }

    [Fact]
    public async Task A_pinned_certificate_is_trusted()
    {
        using var certificate = TestCertificates.WithUsableKey(TestCertificates.SelfSigned());
        await using var server = new LoopbackHttpsServer(certificate, Info);
        using var service = Service(server.Port, PinFor(certificate));

        var info = await service.GetServerInfoAsync(TestContext.Current.CancellationToken);

        Assert.Equal("tls-test", info.ProductVersion);
    }

    [Fact]
    public async Task A_pin_for_another_certificate_is_rejected()
    {
        using var certificate = TestCertificates.WithUsableKey(TestCertificates.SelfSigned());
        using var other = TestCertificates.SelfSigned("CN=someone-else");
        await using var server = new LoopbackHttpsServer(certificate, Info);
        using var service = Service(server.Port, PinFor(other));

        var ex = await Assert.ThrowsAsync<HostFileException>(() => service.GetServerInfoAsync(TestContext.Current.CancellationToken));

        Assert.Equal(HostFileErrorKind.CertificateRejected, ex.Kind);
        Assert.Equal(CertificateReader.Fingerprint(certificate), ex.Certificate!.Sha256);
    }

    [Fact]
    public void No_certificate_is_never_trusted()
    {
        var check = new MvsmfCertificateCheck(null);
        Assert.False(check.Validate(new object(), null, null, SslPolicyErrors.RemoteCertificateNotAvailable));
        Assert.Null(check.LastRejected);
    }

    [Fact]
    public void A_pin_ignores_name_and_chain_errors_when_the_fingerprint_matches()
    {
        using var certificate = TestCertificates.SelfSigned();
        var check = new MvsmfCertificateCheck(PinFor(certificate));
        Assert.True(check.Validate(new object(), certificate, null,
            SslPolicyErrors.RemoteCertificateNameMismatch | SslPolicyErrors.RemoteCertificateChainErrors));
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/LizTerm.Backend.Mvsmf.Tests --filter "FullyQualifiedName~MvsmfTlsTests"`
Expected: FAIL — `An_untrusted...` gets `Unreachable` (the temporary check keeps no rejected certificate), the pin tests fail the handshake, and `A_pin_ignores...` returns false.

- [ ] **Step 3: Implement**

In `src/LizTerm.Core/Security/SslStreamCertificateFetcher.cs`, change `internal static List<X509Certificate2> SelectPresented(` to `public static List<X509Certificate2> SelectPresented(` and add to its summary: "Public so every TLS client in the app pins the same certificates; the caller disposes the list."

Replace `src/LizTerm.Backend.Mvsmf/MvsmfCertificateCheck.cs` with:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using LizTerm.Core.Security;
using LizTerm.Core.Session;

namespace LizTerm.Backend.Mvsmf;

/// <summary>Decides whether an https host is trusted. With a pin, the leaf's SHA-256 must match it and nothing else
/// is consulted — the same trust a pinned 3270 connection gives, which also accepts any host name. Without one, the
/// system's verdict stands. The last certificate refused is kept for the error the service raises; a service talks
/// to one host, so one slot is enough.</summary>
internal sealed class MvsmfCertificateCheck(CertificatePin? pin)
{
    private PresentedCertificate? _lastRejected;

    public CertificatePin? Pin { get; } = pin;

    public PresentedCertificate? LastRejected => Volatile.Read(ref _lastRejected);

    public bool Validate(object sender, X509Certificate? certificate, X509Chain? chain, SslPolicyErrors errors)
    {
        if (certificate is null) return false;
        var presented = SslStreamCertificateFetcher.SelectPresented(certificate, chain);
        try
        {
            var trusted = Pin is not null
                ? CertificateReader.SameFingerprint(CertificateReader.Fingerprint(presented[0]), Pin.Sha256)
                : errors == SslPolicyErrors.None;
            if (!trusted) Volatile.Write(ref _lastRejected, CertificateReader.Read(presented));
            return trusted;
        }
        finally
        {
            foreach (var copy in presented) copy.Dispose();
        }
    }
}
```

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test tests/LizTerm.Backend.Mvsmf.Tests` and `dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~Security"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/LizTerm.Backend.Mvsmf/MvsmfCertificateCheck.cs src/LizTerm.Core/Security/SslStreamCertificateFetcher.cs tests/LizTerm.Backend.Mvsmf.Tests
git commit -m "Trust mvsMF https hosts by system roots or a pinned fingerprint

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 11: Live tests and the developer docs for them

**Files:**
- Create: `tests/LizTerm.Integration.Tests/LiveMvsmfTests.cs`
- Modify: `tests/LizTerm.Integration.Tests/LizTerm.Integration.Tests.csproj` (reference the new backend)
- Modify: `docs/development.md` (environment table, the Tests list, a "Live mvsMF tests" subsection, the fixtures paragraph)

**Interfaces:**
- Consumes: `MvsmfFileService`, `MvsmfOptions`, `HostFileTransfer`, `HostPath`, `DatasetAttributes`.

The live tests skip unless `LIZTERM_MVSMF_URL`, `LIZTERM_MVSMF_USER`, `LIZTERM_MVSMF_PASSWORD` and `LIZTERM_MVSMF_SCRATCH_PDS` are all set. They create, verify and delete the member `LIZITEST` in the scratch PDS.

- [ ] **Step 1: Reference the backend**

In `tests/LizTerm.Integration.Tests/LizTerm.Integration.Tests.csproj`, add to the `ProjectReference` item group:

```xml
    <ProjectReference Include="../../src/LizTerm.Backend.Mvsmf/LizTerm.Backend.Mvsmf.csproj" />
```

- [ ] **Step 2: Write the live tests**

`tests/LizTerm.Integration.Tests/LiveMvsmfTests.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.Backend.Mvsmf;
using LizTerm.Core.HostFiles;

namespace LizTerm.Integration.Tests;

/// <summary>Runs only when LIZTERM_MVSMF_URL, LIZTERM_MVSMF_USER, LIZTERM_MVSMF_PASSWORD and
/// LIZTERM_MVSMF_SCRATCH_PDS are all set. Writes, verifies and deletes the member LIZITEST in the scratch PDS.</summary>
public class LiveMvsmfTests
{
    private const int LiveTimeout = 120_000;
    private const string ScratchMember = "LIZITEST";

    private sealed record Live(Uri Url, HostCredentials Credentials, string ScratchPds);

    private static Live Require()
    {
        var url = Environment.GetEnvironmentVariable("LIZTERM_MVSMF_URL");
        var user = Environment.GetEnvironmentVariable("LIZTERM_MVSMF_USER");
        var password = Environment.GetEnvironmentVariable("LIZTERM_MVSMF_PASSWORD");
        var scratch = Environment.GetEnvironmentVariable("LIZTERM_MVSMF_SCRATCH_PDS");
        Assert.SkipWhen(new[] { url, user, password, scratch }.Any(string.IsNullOrWhiteSpace),
            "LIZTERM_MVSMF_URL, LIZTERM_MVSMF_USER, LIZTERM_MVSMF_PASSWORD and LIZTERM_MVSMF_SCRATCH_PDS are not all set");
        Assert.True(MvsmfOptions.TryNormalizeBaseUrl(url, out var baseUrl, out var error), error);
        return new Live(baseUrl!, new HostCredentials(user!, password!), scratch!);
    }

    private static MvsmfFileService Connect(Live live) =>
        new(new MvsmfOptions(live.Url), (_, _) => ValueTask.FromResult<HostCredentials?>(live.Credentials));

    [Fact(Timeout = LiveTimeout)]
    public async Task Reports_the_server_and_lists_the_scratch_pds()
    {
        var live = Require();
        var ct = TestContext.Current.CancellationToken;
        using var service = Connect(live);

        var info = await service.GetServerInfoAsync(ct);
        var datasets = await service.ListDatasetsAsync(live.ScratchPds, ct);

        Assert.Equal("mvsMF", info.Product);
        Assert.False(string.IsNullOrWhiteSpace(info.ProductVersion));
        var scratch = Assert.Single(datasets, d => d.Name == live.ScratchPds.ToUpperInvariant());
        Assert.True(scratch.Attributes!.IsPartitioned, $"{live.ScratchPds} is {scratch.Attributes.Dsorg}, not a PDS");
    }

    [Fact(Timeout = LiveTimeout)]
    public async Task Round_trips_a_scratch_member_and_deletes_it()
    {
        var live = Require();
        var ct = TestContext.Current.CancellationToken;
        using var service = Connect(live);
        var target = (await service.ListDatasetsAsync(live.ScratchPds, ct)).Single(d => d.Name == live.ScratchPds.ToUpperInvariant()).Attributes!;
        var path = HostPath.ForMember(live.ScratchPds, ScratchMember);
        var local = Path.Combine(Path.GetTempPath(), $"lizitest-{Guid.NewGuid():N}.jcl");
        await File.WriteAllTextAsync(local, "//LIZITEST JOB (ACCT),LIZTERM\n\n//* ¬ ¢ | ~ end\n\tTABBED\n", ct);
        try
        {
            var checkedText = HostFileTransfer.CheckTextFile(local, target);
            Assert.True(checkedText.CanUpload, string.Join(" | ", checkedText.Errors.Select(e => e.Message)));

            var outcome = await HostFileTransfer.UploadTextAsync(service, path, checkedText, verify: true, ct);
            Assert.Equal(UploadOutcome.Matches, outcome);

            var members = await service.ListMembersAsync(HostPath.ForDataset(live.ScratchPds), ct);
            Assert.Contains(members, m => m.Name == ScratchMember);

            var back = Path.Combine(Path.GetTempPath(), $"lizitest-{Guid.NewGuid():N}.txt");
            try
            {
                await HostFileTransfer.DownloadAsync(service, path, back, new DownloadOptions(HostTransferMode.Text, LineEnding: "\n"), cancellationToken: ct);
                Assert.Equal("//LIZITEST JOB (ACCT),LIZTERM\n\n//* ¬ ¢ | ~ end\n        TABBED\n", await File.ReadAllTextAsync(back, ct));
            }
            finally
            {
                File.Delete(back);
            }

            await service.DeleteAsync(path, ct);
            var gone = await Assert.ThrowsAsync<HostFileException>(() => service.ReadTextAsync(path, cancellationToken: ct));
            Assert.Contains(gone.Kind, new[] { HostFileErrorKind.NotFound, HostFileErrorKind.CannotOpen });
        }
        finally
        {
            File.Delete(local);
            try
            {
                await service.DeleteAsync(path, CancellationToken.None);
            }
            catch (HostFileException)
            {
                // Already gone, which is the expected case.
            }
        }
    }

    [Fact(Timeout = LiveTimeout)]
    public async Task A_rejected_password_is_asked_for_again_then_fails()
    {
        var live = Require();
        var asked = new List<bool>();
        using var service = new MvsmfFileService(new MvsmfOptions(live.Url), (request, _) =>
        {
            asked.Add(request.IsRetry);
            return ValueTask.FromResult<HostCredentials?>(new HostCredentials(live.Credentials.Userid, "not-the-password"));
        });

        var ex = await Assert.ThrowsAsync<HostFileException>(() => service.GetServerInfoAsync(TestContext.Current.CancellationToken));

        Assert.Equal(HostFileErrorKind.Unauthenticated, ex.Kind);
        Assert.Equal(new[] { false, true }, asked);
    }
}
```

- [ ] **Step 3: Run without the variables, then with them**

Run: `dotnet test tests/LizTerm.Integration.Tests --filter "FullyQualifiedName~LiveMvsmfTests"`
Expected: 3 skipped.

Run (in a shell that has done `source ~/.config/lizterm-test.env`):
`dotnet test tests/LizTerm.Integration.Tests --filter "FullyQualifiedName~LiveMvsmfTests"`
Expected: 3 passed. A failure here is a real host behaviour: stop, report it, and add it to the compatibility log in Task 12 rather than loosening the test.

Note: the wrong-password test can count against the userid's failed-logon limit if RAKF enforces one; it makes two attempts per run.

- [ ] **Step 4: Document the lane**

In `docs/development.md`:

1. In the environment-variable table, after the `LIZTERM_TEST_USER`, `LIZTERM_TEST_PASSWORD` row, add:

```markdown
| `LIZTERM_MVSMF_URL` | The mvsMF base, for example `http://host:8080/zosmf`; with the three below, enables the live mvsMF tests. |
| `LIZTERM_MVSMF_USER`, `LIZTERM_MVSMF_PASSWORD` | Credentials for the live mvsMF tests and for `tools/record-mvsmf-fixture.sh`. |
| `LIZTERM_MVSMF_SCRATCH_PDS` | A PDS the live mvsMF tests may write the member `LIZITEST` into and delete it from. |
```

2. Change "`dotnet test LizTerm.slnx` runs four projects:" to "five projects:" and add after the Backend.B3270 bullet:

```markdown
- **`LizTerm.Backend.Mvsmf.Tests`**: the mvsMF client against recorded HTTP exchanges, and its TLS trust against a
  loopback server.
```

and extend the Integration bullet to read "…and the live tests against a real host and a real mvsMF."

3. After the "Live host tests" subsection add:

```markdown
### Live mvsMF tests

Set all four `LIZTERM_MVSMF_*` variables. The tests read the server information, list the scratch PDS, upload a
small text member through the same checks the app uses, verify and download it, delete it, and make two sign-in
attempts with a wrong password. Keep the variables in the same file outside the repository as the other live-test
variables.
```

4. After the "Replay fixtures" subsection's first paragraph add:

```markdown
`tests/LizTerm.Backend.Mvsmf.Tests/Fixtures/` holds recorded mvsMF exchanges, one per file, made by
`tools/record-mvsmf-fixture.sh`; its [README](../tests/LizTerm.Backend.Mvsmf.Tests/Fixtures/README.md) lists them.
Re-record them when checking a new mvsMF build against [the compatibility log](mvsmf-compatibility.md).
```

(`docs/mvsmf-compatibility.md` is created in Task 12; the link resolves once it lands.)

- [ ] **Step 5: Commit**

```bash
git add tests/LizTerm.Integration.Tests docs/development.md
git commit -m "Add live mvsMF tests and document their variables

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 12: Compatibility log, project notes and final verification

**Files:**
- Create: `docs/mvsmf-compatibility.md`, `src/LizTerm.Backend.Mvsmf/CLAUDE.md`
- Modify: `CLAUDE.md`, `src/LizTerm.Core/CLAUDE.md`, `tests/CLAUDE.md`

**Interfaces:**
- Consumes: every `mvsMF-compat` tag from Tasks 6–9.

- [ ] **Step 1: Write the compatibility log**

`docs/mvsmf-compatibility.md`:

````markdown
# mvsMF compatibility log

LizTerm's dataset browser talks to [mvsMF](https://github.com/mvslovers/mvsmf), a z/OSMF REST subset for MVS 3.8j.
This log is the one place that records where mvsMF's documentation, its source and the build LizTerm was tested
against disagree, and what LizTerm does about each. When a newer mvsMF is available, work through it top to bottom.

## Tested build

| | |
|---|---|
| Reported version | `zosmf_version: 1.0.0-dev` (a pre-release) |
| Host | MVS/CE, HTTPD `STC 99`, probed 2026-09-16 |
| Source read alongside | mvsMF v1.1.0 (2026-09-14), `src/` and `docs/endpoints/` |

## How to re-check a new build

1. Point `LIZTERM_MVSMF_*` at the new build (see `docs/development.md`).
2. Re-record every fixture listed in `tests/LizTerm.Backend.Mvsmf.Tests/Fixtures/README.md` with
   `tools/record-mvsmf-fixture.sh`, and run `dotnet test tests/LizTerm.Backend.Mvsmf.Tests`. A test that now fails
   is named after the entry below whose behaviour changed.
3. Run the live tests: `dotnet test tests/LizTerm.Integration.Tests --filter "FullyQualifiedName~LiveMvsmfTests"`.
4. For each entry, probe the behaviour by hand where no test covers it, then update the entry. When a workaround is
   no longer needed, remove the code at its tag (`grep -rn "mvsMF-compat: <tag>" src`), its test, and the entry.

Each entry's tag appears in the backend code as `// mvsMF-compat: <tag>` and in the name of the test that pins it.
Entries marked *log only* change nothing in the code.

## Entries

### `basic-auth-every-request`

- **Docs and source:** any Basic-authenticated request is answered with `Set-Cookie: LtpaToken2=…`, and a client
  holding the cookie need not resend credentials.
- **Observed:** no `Set-Cookie` at all.
- **LizTerm:** keeps no cookies (`UseCookies = false`) and sends Basic credentials on every request. Once the cookie
  works, sessions could use it instead; nothing depends on it now.

### `info-requires-auth`

- **Docs:** `GET /zosmf/info` needs no authentication.
- **Observed, and in the source:** 401 without credentials.
- **LizTerm:** `/info` goes through the same authenticated path as everything else.

### `no-www-authenticate` (log only)

- **Source:** a 401 carries `WWW-Authenticate: Basic realm="<SMF ID>"` unless the client sends `X-MVSMF-Client`.
- **Observed:** no `WWW-Authenticate` header.
- **LizTerm:** sends credentials up front and never waits for a challenge, so either behaviour works. It does not
  send `X-MVSMF-Client`, which is meant for browser pages only.

### `dataset-list-ignores-start`

- **Docs and source:** `start` names the first dataset of the page, so a list can be paged with
  `X-IBM-Max-Items`.
- **Observed:** every `start` value — an existing name, a partial one, lower case — returns the first page.
- **LizTerm:** never pages. It sends no `X-IBM-Max-Items` and no `start`, and takes the whole list (`SYS1.**`,
  96 entries, arrives at once). The browser has no Load more row. If `start` works in a newer build, paging can come
  back for very large catalogues.

### `dataset-list-morerows-false`

- **Source:** `moreRows` appears only when true.
- **Observed:** `"moreRows": false` on a complete list.
- **LizTerm:** does not read `moreRows` (with no item limit it is never true).

### `dslevel-is-a-prefix` (log only)

- **Observed:** `dslevel=MVSCE02` lists every `MVSCE02.*` dataset, as z/OSMF does.
- **LizTerm:** a lookup of one dataset by name picks the exact name out of the list.

### `member-list-ignores-max-items`

- **Docs:** `X-IBM-Max-Items` limits the member list.
- **Observed:** a request for 3 members of `SYS1.MACLIB` returned all 742.
- **LizTerm:** sends no limit and expects whole lists.

### `member-list-empty-for-missing-dataset`

- **Source:** a missing dataset is 404; a dataset that is not partitioned is 400.
- **Observed:** both answer 200 with an empty list.
- **LizTerm:** passes the empty list on; callers confirm the dataset from the dataset list, whose attributes say
  whether it is partitioned.

### `missing-read-is-500`

- **Source:** a missing dataset or member on read is 404 (reason 4 or 5).
- **Observed:** 500 with category 6, reason 3, "Cannot open dataset" or "Cannot open dataset member".
- **LizTerm:** classifies by category and reason, and reports reason 3 as "not found, not authorized, or cannot be
  opened", because it cannot tell which.

### `authorization-is-500`

- **Source:** a refused open is 500 with category 4, rc 8, reason 0 ("LMOPEN error"), never 403.
- **Observed:** not reproduced (IBMUSER could read everything tried). MVSCE02's own libraries listed empty for
  IBMUSER, which may be a hidden refusal; see `member-list-empty-for-missing-dataset`.
- **LizTerm:** reports that shape as "not authorized".

### `text-body-is-latin1`

- **Docs:** silent on the body's character set.
- **Observed:** text bodies are ISO-8859-1 in both directions, whatever `charset` says. UTF-8 `¬` (`C2 AC`) was
  stored as two characters, `Â¬` (`62 5F`); Latin-1 `AC` was stored as CP037 `5F`. Responses carry no charset.
- **LizTerm:** encodes to and decodes from Latin-1 in the backend. Local files are UTF-8; `TextUploadCheck` refuses
  characters above U+00FF.

### `text-read-keeps-trailing-blanks`

- **Source:** trailing blanks are stripped from F and FB records on read.
- **Observed:** they are not; an 80-column member returns 80-character lines, sequence numbers included.
- **LizTerm:** the backend passes lines on as received; `HostFileTransfer` trims trailing blanks on download
  (`DownloadOptions.TrimTrailingBlanks`, on by default) and ignores them when verifying an upload.

### `text-write-drops-empty-lines`

- **Source:** a blank line becomes a record of blanks.
- **Observed:** an empty line is dropped (with LF and CRLF endings alike); a line holding one space is stored as a
  blank record.
- **LizTerm:** the backend sends each empty line as a single space.

### `text-write-truncates-silently`

- **Source:** an over-long line is truncated, the rest is written, and the request answers 500 "Record truncated to
  the record length of the data set".
- **Observed:** a 100-character line to an LRECL 80 member was truncated to 80 and answered **204**.
- **LizTerm:** `TextUploadCheck` refuses any line longer than the record allows (LRECL for F, LRECL−4 for V,
  BLKSIZE for U) before anything is sent; pinned by `TextUploadCheckTests`, since Core does not name mvsMF.

### `put-json-is-rename`

- **Source:** a `PUT` with `Content-Type: application/json` is a rename request, not a write.
- **LizTerm:** writes send only `text/plain` or `application/octet-stream`.

### `binary-fixed-padding` (log only)

- **Observed:** a 100-byte binary write to an FB 80 member reads back as 160 bytes, the last record zero-padded.
- **LizTerm:** says in the browser that binary transfers to fixed-length datasets are padded to whole records.

### `record-write-broken` (log only)

- **Source:** record-mode writes are broken (mvsMF issue #245); record-mode reads prefix each record with a 4-byte
  length.
- **LizTerm:** offers Text and Binary only.

### `no-etag` (log only)

- **Source:** `X-IBM-Return-Etag`, `If-Match` and `If-None-Match` are supported.
- **Observed:** no `ETag` header is returned.
- **LizTerm:** no conflict detection in the preview.

### `host-date-unreliable` (log only)

- **Observed:** the `Date` header said `Sat, 15 Sep 2096`: it is the MVS clock.
- **LizTerm:** never uses host dates for anything that matters.

### `docs-omit-routes` (log only)

- **Docs:** the endpoint table lists no dataset `POST` or `DELETE` and no member `DELETE`.
- **Source and observed:** member `DELETE` works (204, then 404 reason 5); the source also routes dataset create and
  delete, which the preview does not use.

### `hash-in-names-untested` (log only)

- **Source:** the router percent-decodes the path.
- **Observed:** not tested; no dataset or member with `#` was available.
- **LizTerm:** escapes `#` as `%23` and `%` as `%25` and sends every other name character as it is.

### `uss-limits` (log only, for later)

- **Source:** USS files are limited to 64 KB, use IBM-1047, and USS create answers 400 for an existing file.
- **LizTerm:** no USS support yet.
````

- [ ] **Step 2: Write the backend's notes**

`src/LizTerm.Backend.Mvsmf/CLAUDE.md`:

```markdown
# LizTerm.Backend.Mvsmf

Notes for working in this project. The root `CLAUDE.md` has the rules that apply everywhere. This project depends on
Core only, is the only one that knows mvsMF exists, and never references `LizTerm.Backend.B3270`.

- `MvsmfFileService` implements `IHostFileService` (Core) over one `HttpClient`. It stores no credentials: it asks
  the `HostCredentialProvider` before every request (`IsRetry: false`), and once more after a 401
  (`IsRetry: true`) before repeating the request once. The App's holder is the only store. Never put the userid's
  password in a message, a log or a `ToString()`.
- **Every workaround carries `// mvsMF-compat: <tag>`** matching an entry in `docs/mvsmf-compatibility.md`, and a
  test named after the tag pins it. Add all three together, and read that log before changing any behaviour that
  looks odd: it is probably deliberate.
- **Never send `Content-Type: application/json` on a `PUT`.** mvsMF treats it as a rename.
- **Text is ISO-8859-1 on the wire** in both directions. Empty lines go out as one space, because the host drops
  empty ones. `EncodeText` throws for a line break or a character above U+00FF; `TextUploadCheck` (Core) should have
  refused those first.
- **Classify errors by the JSON `category`/`reason`, then the status.** mvsMF answers 500 for a missing member and
  for a refused open. `MvsmfErrors` is the one place that mapping lives.
- **No paging.** This build ignores `start` and, for members, `X-IBM-Max-Items`; lists are fetched whole.
- **Timeouts:** 10 s to connect (`SocketsHttpHandler.ConnectTimeout`), 30 s without data (`IdleTimeout`, reset on
  every chunk), and no `HttpClient.Timeout`, so a long download is never cut off while bytes arrive.
  `HttpCompletionOption.ResponseHeadersRead` everywhere, so bodies stream.
- **Bodies are built once per call**, so the repeat after a 401 sends the same bytes; `WriteBinaryAsync` therefore
  reads its source into memory first.
- **TLS:** `MvsmfCertificateCheck` trusts a pinned leaf fingerprint and nothing else, or, without a pin, the system's
  verdict. It keeps the last refused certificate (one slot: a service talks to one host) so the service can raise
  `CertificateRejected` with the `PresentedCertificate`. It uses Core's `SslStreamCertificateFetcher.SelectPresented`,
  so a pin covers exactly what was on the wire.
- Names are escaped by `EscapeName`: `#` and `%` only. Everything else a validated `HostPath` or filter can hold goes
  as it is, as curl sends it.

## Tests

- `RecordedHandler` answers queued responses, usually `Fixture.Load(name)` from `Fixtures/`, and records each
  request (method, URI, Authorization, `X-IBM-Data-Type`, content type, body, header names).
- Fixtures are real exchanges recorded by `tools/record-mvsmf-fixture.sh`; `Fixtures/README.md` lists them. The body
  bytes are kept exactly, so never run a fixture through a CR-stripping tool.
- `LoopbackHttpsServer` serves one JSON body over TLS with a `TestCertificates` certificate (linked from
  Core.Tests), for the pin tests.
- `StallingStream`, `FailingStream` and `ListProgress` (`TestStreams.cs`) drive the idle timeout, a dropped
  connection and synchronous progress.
```

- [ ] **Step 3: Update the repository rules**

In `CLAUDE.md`:

1. In the documentation table, after the `docs/engines.md` row, add:

```markdown
| mvsMF: where its docs, source and the tested build disagree, and LizTerm's workarounds | `docs/mvsmf-compatibility.md` |
```

2. Replace the **Dependency rule** bullet's first two sentences so it reads:

```markdown
- **Dependency rule** (enforced in review): `LizTerm.Core` depends only on the BCL and never mentions Avalonia,
  b3270 or mvsMF. `LizTerm.Backend.B3270` depends on Core and is the only project that knows b3270 exists;
  `LizTerm.Backend.Mvsmf` depends on Core and is the only project that knows mvsMF exists; the two backends never
  reference each other. `LizTerm.App` names the b3270 backend in exactly one place, `src/LizTerm.App/SessionFactory.cs`;
  everything else in App talks to `IEmulatorSession`.
```

(keep the rest of that bullet as it is).

3. Under **Commands**, after the one-project line, add:

```bash
dotnet test tests/LizTerm.Backend.Mvsmf.Tests  # the mvsMF client against recorded exchanges
```

In `src/LizTerm.Core/CLAUDE.md`, add a section before `## Profiles`:

```markdown
## Host files

- `LizTerm.Core.HostFiles` is file access outside the 3270 session, host-neutral: it never names a product.
  `IHostFileService` is the contract; `LizTerm.Backend.Mvsmf` implements it.
- `HostPath` folds names to upper case and checks the MVS rules when it is made, so a `HostPath` is always a name
  the host could accept. `DatasetPatternError` is the filter rule.
- Text crosses the interface as one string per record; the wire encoding is the backend's business.
- `TextUploadCheck` runs before any upload: invalid UTF-8, a character above U+00FF, or a line longer than the
  record (`DatasetAttributes.UsableLineLength`) blocks it; tabs are a warning and are expanded by default. It is
  pure, and the file-reading wrapper is `HostFileTransfer.CheckTextFile`.
- `HostFileTransfer.DownloadAsync` writes a hidden `.part` file beside the destination and renames it only on
  success. Text downloads trim trailing blanks by default and end lines with the platform's newline unless told
  otherwise. `UploadTextAsync` refuses text that failed its check and, when asked, reads it back and compares with
  trailing blanks ignored.
- `HostCredentials` is a class, not a record, so no generated `ToString` can print the password.
```

In `tests/CLAUDE.md`:

1. In the Core tests section, change "It stays `internal` and is compiled into the integration project by a linked
   `<Compile Include=...>` item" to "It stays `internal` and is compiled into the integration and mvsMF backend
   projects by a linked `<Compile Include=...>` item".
2. Add at the end of the Core tests section: "`FakeHostFileService` (`HostFiles/`) is an in-memory host keyed by
   `HostPath.ToString()`, with `StoreTransform` to play a host that alters what it stores and `ReadFailure` /
   `BytesBeforeFailure` for a read that dies part-way."
3. Add a section before `## Integration tests`:

```markdown
## mvsMF backend tests

See `src/LizTerm.Backend.Mvsmf/CLAUDE.md`, "Tests". Tests that pin an mvsMF workaround are named after its tag in
`docs/mvsmf-compatibility.md`.
```

4. In the Integration tests section, add: "`LiveMvsmfTests` skip unless all four `LIZTERM_MVSMF_*` variables are set;
   they write, verify and delete the member `LIZITEST` in `LIZTERM_MVSMF_SCRATCH_PDS`, and make two sign-ins with a
   wrong password."

- [ ] **Step 4: Check the tags against the log and the tests**

Run:

```bash
for tag in $(grep -rhoE 'mvsMF-compat: [a-z0-9-]+' src/LizTerm.Backend.Mvsmf | sed 's/.*: //' | sort -u); do
  grep -q "### \`$tag\`" docs/mvsmf-compatibility.md || echo "no log entry: $tag"
  name=$(python3 -c 'import sys; t=sys.argv[1].replace("-","_"); print(t[0].upper()+t[1:])' "$tag")
  grep -rqE "(void|Task) $name" tests/LizTerm.Backend.Mvsmf.Tests || echo "no test: $tag"
done
grep -rniE 'mvsmf' src/LizTerm.Core --include='*.cs' && echo "Core code names mvsMF" || true
```

Expected output: exactly `no test: text-write-truncates-silently` (pinned in Core, as the log says). Anything else: add the missing entry or test.

- [ ] **Step 5: Full verification**

Run:

```bash
dotnet build LizTerm.slnx --no-incremental 2>&1 | grep -c " warning "
dotnet test LizTerm.slnx
```

Expected: `0` warnings; every test passes or skips (the live lanes skip without their variables). Then, with `source ~/.config/lizterm-test.env`, run `dotnet test tests/LizTerm.Integration.Tests --filter "FullyQualifiedName~LiveMvsmfTests"`: 3 passed.

- [ ] **Step 6: Commit**

```bash
git add docs/mvsmf-compatibility.md src/LizTerm.Backend.Mvsmf/CLAUDE.md CLAUDE.md src/LizTerm.Core/CLAUDE.md tests/CLAUDE.md
git commit -m "Add the mvsMF compatibility log and the new project's notes

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

- [ ] **Step 7: Hand back**

Push the branch and open PR 1 against `main` only when Robert asks. The PR description names issue #17, says this is PR 1 of 3 with no user-visible change, and lists the compatibility-log entries.
