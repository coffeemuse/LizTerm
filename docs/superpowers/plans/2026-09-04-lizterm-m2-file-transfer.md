# LizTerm Milestone 2, Plan 2: IND$FILE File Transfer Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Send a local file to the host and receive a host file locally with IND$FILE from one File Transfer dialog that moves through form, progress, and result, remembers its last values for the life of the session window, and is proven by a live round trip against an MVS 3.8j host.

**Architecture:** Core gains a `FileTransferRequest` record with `Validate()`, a `FileTransferResult`, and one `IEmulatorSession.TransferAsync(request, IProgress<long>?, CancellationToken)` that completes when the transfer ends. The b3270 backend maps the request to the `Transfer` action's `keyword=value` arguments through a pure `TransferMapper`, holds one in-flight transfer slot, feeds `ft` `running` indications to the progress reporter, and treats the run-result as the single source of truth for the outcome; cancelling the token sends `Transfer(Cancel)`. The app gains an `IFilePicker` seam (Avalonia implementation over `StorageProvider`, test fake), a `FileTransferViewModel` with Form, Running, and Done phases, a `FileTransferWindow` with three panels, a `CreateTransfer` factory plus `LastTransferRequest` memory on `SessionViewModel`, and a File menu item. The integration lane logs on to TSO with credentials from the environment and round-trips a file.

**Tech Stack:** .NET 10, Avalonia 12.1.2 (`TopLevel.StorageProvider`, `FilePickerOpenOptions`, `FilePickerSaveOptions`, `IStorageItem.TryGetLocalPath`, `ProgressBar.IsIndeterminate`, `Window.Closing` with `WindowClosingEventArgs`, `ReflectionBinding`), CommunityToolkit.Mvvm 8.4.2, xunit.v3 3.2.2 in VSTest mode, Avalonia.Headless.XUnit 12.1.2, b3270 4.5ga6 (`Transfer` action keywords verified in `Common/ft.c`; completion messages verified in the shipped binary).

**Spec:** `docs/superpowers/specs/2026-09-04-lizterm-m2-file-transfer-design.md` (parent: `docs/superpowers/specs/2026-09-03-lizterm-v1-design.md` sections 4.6, 5.2, 5.5, 6.2, 6.8, 7, 9)

## Global Constraints

- `LizTerm.Core` references only the BCL and never mentions Avalonia or b3270 names. `LizTerm.App` names `LizTerm.Backend.B3270` only in `src/LizTerm.App/SessionFactory.cs`. Only `LizTerm.Backend.B3270` knows the `Transfer` action and `ft` indications exist.
- No new packages. Avalonia stays at `12.1.2`, CommunityToolkit.Mvvm at `8.4.2`; versions live only in `Directory.Packages.props`.
- Rows and columns are zero-based in Core and App; nothing in this plan touches screen coordinates.
- `TransferAsync` contract (spec section 3): completes when the transfer ends; a refused, failed, or aborted transfer is a `FileTransferResult` with `Succeeded` false and the engine's or host's message verbatim; throws `InvalidOperationException` when the session is not started or a transfer is already running, `OperationCanceledException` when the token cancelled it and the engine then reported failure, `BackendUnavailableException` when the engine dies; a success that beats a cancel is returned as success; progress reports bytes on the backend thread.
- The run-result is the single source of truth for the outcome. `ft` indications feed progress only (`running` with `bytes`); `awaiting`, `aborting`, `complete`, and `cause` are ignored; an `ft` indication with no transfer in flight is dropped.
- Mapper argument order and omission rules are the spec's section 4.1 table, pinned exactly by tests. Binary mode omits `cr` and `remap`; receive omits every allocation keyword and adds `exist=replace` unless appending; `lrecl` and `blksize` need a record format; space fields need allocation units; VM never gets `blksize`, allocation, or the Undefined record format; CICS gets no allocation keywords at all.
- User-visible strings are asserted exactly: `"Choose a local file."`, `"Enter the host file name."`, `"<Field> must be a positive number."` with LRECL, BLKSIZE, Primary space, Secondary space, Average block size, `"Buffer size must be between 256 and 32768."`, `"Primary space is required when allocation units are set."`, `"Average block size is required for AVBLOCK allocation."`, `"<Field> must be a whole number."`, `"Waiting for the host..."`, `"<N,NNN> bytes"` (invariant culture), `"Cancelling..."`, `"Transfer cancelled."`, `"Could not open the file dialog: "` + message, `"A file transfer is already in progress."`, `"The session has not been started."`.
- Dialog memory: `SessionViewModel.LastTransferRequest` holds the last started request for the window's life; nothing is written to the profile.
- The live lane reads `LIZTERM_TEST_HOST`, `LIZTERM_TEST_USER`, `LIZTERM_TEST_PASSWORD` from the environment. Never write the password into a file, a command line, a commit, or a fixture. Wire logs from live runs are never committed; fixtures are the inbound lines only, checked with `grep -F "$LIZTERM_TEST_PASSWORD"` (which prints nothing when clean) before `git add`.
- Run tests with `dotnet test <project> --filter "FullyQualifiedName~<Class>"`; run the full suite with `dotnet test LizTerm.slnx` before every commit. If a `LizTerm.Backend.B3270.Tests` test fails once under load (`Oia_lock_maps_to_keyboard_lock` is the known flake), rerun before investigating.
- Work only inside this worktree: `/Users/robert/ClaudeSandbox/LizTerm/.claude/worktrees/indfile-integration-2b9124`. Never `cd` to the main checkout. Commit after every task with the message shown; end every commit message with `Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>`.
- This worktree has no `native/out`, so anything that spawns b3270 needs `LIZTERM_B3270_PATH=/opt/homebrew/bin/b3270` in the environment.

---

## File Structure

```
src/LizTerm.Core/Session/FileTransfer.cs                     NEW  enums, FileTransferRequest + Validate(), FileTransferResult
src/LizTerm.Core/Session/IEmulatorSession.cs                 MOD  TransferAsync
src/LizTerm.Backend.B3270/Protocol/TransferMapper.cs         NEW  request -> Transfer(keyword=value...) and CancelAction
src/LizTerm.Backend.B3270/B3270Session.cs                    MOD  TransferAsync, in-flight slot, ft routing, IsTransferInProgress
src/LizTerm.App/Files/IFilePicker.cs                         NEW  open/save seam
src/LizTerm.App/Files/AvaloniaFilePicker.cs                  NEW  wraps TopLevel.StorageProvider
src/LizTerm.App/Files/LocalFileNames.cs                      NEW  suggested local name for a received host file
src/LizTerm.App/ViewModels/FileTransferViewModel.cs          NEW  form, phases, commands
src/LizTerm.App/ViewModels/SessionViewModel.cs               MOD  LastTransferRequest, CreateTransfer(picker)
src/LizTerm.App/Views/TransferLabels.cs                      NEW  enum -> label converter for the combo boxes
src/LizTerm.App/Views/FileTransferWindow.axaml(.cs)          NEW  three panels, Closing cancels a running transfer
src/LizTerm.App/Views/SessionWindow.axaml(.cs)               MOD  File Transfer... menu item and click handler
tests/LizTerm.Core.Tests/Session/FileTransferRequestTests.cs NEW
tests/LizTerm.Backend.B3270.Tests/Protocol/TransferMapperTests.cs   NEW
tests/LizTerm.Backend.B3270.Tests/B3270SessionTransferTests.cs      NEW
tests/LizTerm.Backend.B3270.Tests/Protocol/IndicationParserTests.cs MOD  fixture ft-sequence test
tests/LizTerm.Backend.B3270.Tests/Fixtures/indfile-tso-roundtrip.jsonl NEW  from the live run (Task 4)
tests/LizTerm.Backend.B3270.Tests/Fixtures/README.md          MOD
tests/LizTerm.Integration.Tests/ScreenWaiter.cs              NEW  waits on screen text and keyboard unlock
tests/LizTerm.Integration.Tests/TsoNavigator.cs              NEW  logon to READY, cleanup, logoff, by screen text
tests/LizTerm.Integration.Tests/LiveHostTests.cs             MOD  Indfile_round_trip_matches
tests/LizTerm.App.Tests/Fakes/FakeEmulatorSession.cs         MOD  TransferAsync with completion, progress, token capture
tests/LizTerm.App.Tests/Fakes/FakeFilePicker.cs              NEW
tests/LizTerm.App.Tests/Files/LocalFileNamesTests.cs         NEW
tests/LizTerm.App.Tests/ViewModels/FileTransferViewModelTests.cs        NEW
tests/LizTerm.App.Tests/ViewModels/SessionViewModelTransferTests.cs     NEW
tests/LizTerm.App.Tests/Views/FileTransferWindowTests.cs     NEW
tests/LizTerm.App.Tests/Views/SessionWindowTests.cs          MOD  menu item follows connection state
CLAUDE.md                                                    MOD
docs/superpowers/specs/2026-09-04-lizterm-m2-file-transfer-design.md  MOD  status line and any as-built change
```

---

### Task 1: Core file transfer model

**Files:**
- Create: `src/LizTerm.Core/Session/FileTransfer.cs`
- Test: `tests/LizTerm.Core.Tests/Session/FileTransferRequestTests.cs`

**Interfaces:**
- Produces: `enum TransferDirection { Send, Receive }`, `enum TransferHostType { Tso, Vm, Cics }`, `enum TransferMode { Text, Binary }`, `enum RecordFormat { Default, Fixed, Variable, Undefined }`, `enum AllocationUnits { Default, Tracks, Cylinders, AvBlock }`, `sealed record FileTransferRequest` (properties listed in the code below) with `string? Validate()` and constants `MinBufferSize = 256`, `MaxBufferSize = 32768`, and `sealed record FileTransferResult(bool Succeeded, string Message, long Bytes)`, all in `LizTerm.Core.Session`. Every later task uses them. `IEmulatorSession` is not changed here; Task 3 adds the method together with both implementations so the solution never stops compiling.

- [ ] **Step 1: Write the failing tests**

`tests/LizTerm.Core.Tests/Session/FileTransferRequestTests.cs`:

```csharp
using LizTerm.Core.Session;

namespace LizTerm.Core.Tests.Session;

public class FileTransferRequestTests
{
    private static FileTransferRequest Send() =>
        new() { Direction = TransferDirection.Send, LocalPath = "/nonexistent/a.txt", HostFile = "A.B" };

    [Fact]
    public void Defaults_match_spec()
    {
        var r = Send();
        Assert.Equal(TransferHostType.Tso, r.HostType);
        Assert.Equal(TransferMode.Text, r.Mode);
        Assert.True(r.CrLf);
        Assert.True(r.Remap);
        Assert.False(r.Append);
        Assert.Equal(RecordFormat.Default, r.RecordFormat);
        Assert.Equal(AllocationUnits.Default, r.AllocationUnits);
        Assert.Null(r.Lrecl);
        Assert.Null(r.Blksize);
        Assert.Null(r.PrimarySpace);
        Assert.Null(r.SecondarySpace);
        Assert.Null(r.AverageBlock);
        Assert.Null(r.BufferSize);
        Assert.Null(r.ExtraOptions);
        Assert.Null(r.Validate());
    }

    [Fact]
    public void Blank_local_path_is_reported_before_a_blank_host_file() =>
        Assert.Equal("Choose a local file.", (Send() with { LocalPath = " ", HostFile = "" }).Validate());

    [Fact]
    public void Blank_host_file_is_rejected() =>
        Assert.Equal("Enter the host file name.", (Send() with { HostFile = " " }).Validate());

    [Fact]
    public void Non_positive_numbers_are_rejected_with_the_field_name()
    {
        Assert.Equal("LRECL must be a positive number.", (Send() with { Lrecl = 0 }).Validate());
        Assert.Equal("BLKSIZE must be a positive number.", (Send() with { Blksize = -1 }).Validate());
        Assert.Equal("Primary space must be a positive number.", (Send() with { PrimarySpace = 0 }).Validate());
        Assert.Equal("Secondary space must be a positive number.", (Send() with { SecondarySpace = 0 }).Validate());
        Assert.Equal("Average block size must be a positive number.", (Send() with { AverageBlock = 0 }).Validate());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(255)]
    [InlineData(32769)]
    public void Buffer_size_outside_the_range_is_rejected(int size) =>
        Assert.Equal("Buffer size must be between 256 and 32768.", (Send() with { BufferSize = size }).Validate());

    [Fact]
    public void Buffer_size_bounds_are_accepted()
    {
        Assert.Null((Send() with { BufferSize = 256 }).Validate());
        Assert.Null((Send() with { BufferSize = 32768 }).Validate());
    }

    [Fact]
    public void Tso_send_with_allocation_units_needs_primary_space()
    {
        var r = Send() with { AllocationUnits = AllocationUnits.Tracks };
        Assert.Equal("Primary space is required when allocation units are set.", r.Validate());
        Assert.Null((r with { PrimarySpace = 5 }).Validate());
    }

    [Fact]
    public void Avblock_needs_an_average_block_size()
    {
        var r = Send() with { AllocationUnits = AllocationUnits.AvBlock, PrimarySpace = 5 };
        Assert.Equal("Average block size is required for AVBLOCK allocation.", r.Validate());
        Assert.Null((r with { AverageBlock = 4096 }).Validate());
    }

    [Fact]
    public void Allocation_rules_apply_only_when_sending_to_tso()
    {
        var r = Send() with { AllocationUnits = AllocationUnits.AvBlock };
        Assert.Null((r with { Direction = TransferDirection.Receive }).Validate());
        Assert.Null((r with { HostType = TransferHostType.Vm }).Validate());
        Assert.Null((r with { HostType = TransferHostType.Cics }).Validate());
    }

    [Fact]
    public void Result_is_a_plain_record()
    {
        var result = new FileTransferResult(false, "TRANS17 Miscellaneous I/O error", 0);
        Assert.False(result.Succeeded);
        Assert.Equal("TRANS17 Miscellaneous I/O error", result.Message);
        Assert.Equal(0, result.Bytes);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~FileTransferRequestTests"`
Expected: build error, `FileTransferRequest` does not exist.

- [ ] **Step 3: Write the Core types**

`src/LizTerm.Core/Session/FileTransfer.cs`:

```csharp
namespace LizTerm.Core.Session;

public enum TransferDirection { Send, Receive }

public enum TransferHostType { Tso, Vm, Cics }

public enum TransferMode { Text, Binary }

/// <summary>Record format of a host file created by a send. Undefined applies to TSO only.</summary>
public enum RecordFormat { Default, Fixed, Variable, Undefined }

/// <summary>Units for the TSO space allocation of a host file created by a send.</summary>
public enum AllocationUnits { Default, Tracks, Cylinders, AvBlock }

/// <summary>One IND$FILE transfer. Fields that do not apply to the direction, mode, or host type are ignored
/// by the backend rather than rejected, so a dialog can keep them filled while the user switches.</summary>
public sealed record FileTransferRequest
{
    public const int MinBufferSize = 256;
    public const int MaxBufferSize = 32768;

    public required TransferDirection Direction { get; init; }
    public required string LocalPath { get; init; }
    public required string HostFile { get; init; }
    public TransferHostType HostType { get; init; } = TransferHostType.Tso;
    public TransferMode Mode { get; init; } = TransferMode.Text;
    /// <summary>Text mode only: strip newlines when sending, add them when receiving.</summary>
    public bool CrLf { get; init; } = true;
    /// <summary>Text mode only: remap between the workstation encoding and the host code page.</summary>
    public bool Remap { get; init; } = true;
    /// <summary>Append to the destination instead of replacing it.</summary>
    public bool Append { get; init; }
    // Sending only. Record format applies to TSO and VM; the rest to TSO only.
    public RecordFormat RecordFormat { get; init; } = RecordFormat.Default;
    public int? Lrecl { get; init; }
    public int? Blksize { get; init; }
    public AllocationUnits AllocationUnits { get; init; } = AllocationUnits.Default;
    public int? PrimarySpace { get; init; }
    public int? SecondarySpace { get; init; }
    public int? AverageBlock { get; init; }
    /// <summary>DFT buffer size, 256 to 32768; null lets the engine choose.</summary>
    public int? BufferSize { get; init; }
    /// <summary>Appended verbatim to the host's IND$FILE command, for ports with extra keywords.</summary>
    public string? ExtraOptions { get; init; }

    /// <summary>A user-facing message, or null when the request can be attempted. Checks only what would make
    /// the engine or host reject the request outright; inapplicable fields are not errors.</summary>
    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(LocalPath)) return "Choose a local file.";
        if (string.IsNullOrWhiteSpace(HostFile)) return "Enter the host file name.";
        if (Lrecl is <= 0) return "LRECL must be a positive number.";
        if (Blksize is <= 0) return "BLKSIZE must be a positive number.";
        if (PrimarySpace is <= 0) return "Primary space must be a positive number.";
        if (SecondarySpace is <= 0) return "Secondary space must be a positive number.";
        if (AverageBlock is <= 0) return "Average block size must be a positive number.";
        if (BufferSize is < MinBufferSize or > MaxBufferSize) return $"Buffer size must be between {MinBufferSize} and {MaxBufferSize}.";
        var tsoSend = Direction == TransferDirection.Send && HostType == TransferHostType.Tso;
        if (tsoSend && AllocationUnits != AllocationUnits.Default && PrimarySpace is null)
            return "Primary space is required when allocation units are set.";
        if (tsoSend && AllocationUnits == AllocationUnits.AvBlock && AverageBlock is null)
            return "Average block size is required for AVBLOCK allocation.";
        return null;
    }
}

/// <summary>Outcome of one transfer. Message is the engine's or host's final text, unaltered, on success and on
/// failure; Bytes is the last progress count seen, zero if none.</summary>
public sealed record FileTransferResult(bool Succeeded, string Message, long Bytes);
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~FileTransferRequestTests"`
Expected: 12 passed.

- [ ] **Step 5: Run the full suite and commit**

Run: `dotnet test LizTerm.slnx`
Expected: everything green (the interface is unchanged, so no other project is affected).

```bash
git add src/LizTerm.Core/Session/FileTransfer.cs tests/LizTerm.Core.Tests/Session/FileTransferRequestTests.cs
git commit -m "Add the Core file transfer request, validation, and result types

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 2: TransferMapper (backend, pure)

**Files:**
- Create: `src/LizTerm.Backend.B3270/Protocol/TransferMapper.cs`
- Test: `tests/LizTerm.Backend.B3270.Tests/Protocol/TransferMapperTests.cs`

**Interfaces:**
- Consumes: `FileTransferRequest` and the enums from Task 1; `B3270Action(string Name, params string[] Args)` and `RunOperation.Serialize` from `LizTerm.Backend.B3270.Protocol`.
- Produces: `static class TransferMapper` with `static B3270Action ToAction(FileTransferRequest request)` and `static readonly B3270Action CancelAction`. Task 3 uses both.

- [ ] **Step 1: Write the failing tests**

`tests/LizTerm.Backend.B3270.Tests/Protocol/TransferMapperTests.cs`:

```csharp
using LizTerm.Backend.B3270.Protocol;
using LizTerm.Core.Session;

namespace LizTerm.Backend.B3270.Tests.Protocol;

/// <summary>Pins the Transfer() argument list exactly. The omission rules mirror what b3270 rejects (cr and remap
/// in binary mode, allocation keywords on receive or on the wrong host type) and what its IND$FILE command
/// builder ignores (LRECL and BLKSIZE without a RECFM, SPACE without units).</summary>
public class TransferMapperTests
{
    private static FileTransferRequest Send(TransferHostType host = TransferHostType.Tso) =>
        new() { Direction = TransferDirection.Send, LocalPath = "/tmp/job.jcl", HostFile = "LIZTERM.JCL(JOB1)", HostType = host };

    private static FileTransferRequest Receive(TransferHostType host = TransferHostType.Tso) =>
        new() { Direction = TransferDirection.Receive, LocalPath = "/tmp/out.txt", HostFile = "LIZTERM.ITEST", HostType = host };

    private static string[] Args(FileTransferRequest request)
    {
        var action = TransferMapper.ToAction(request);
        Assert.Equal("Transfer", action.Name);
        return action.Args;
    }

    private static readonly string[] SendHead = ["direction=send", "hostfile=LIZTERM.JCL(JOB1)", "localfile=/tmp/job.jcl", "host=tso", "mode=ascii", "cr=remove", "remap=yes"];

    [Fact]
    public void Send_text_defaults() => Assert.Equal(SendHead, Args(Send()));

    [Fact]
    public void Receive_text_defaults_add_newlines_and_replace_the_local_file() =>
        Assert.Equal(["direction=receive", "hostfile=LIZTERM.ITEST", "localfile=/tmp/out.txt", "host=tso", "mode=ascii", "cr=add", "remap=yes", "exist=replace"], Args(Receive()));

    [Fact]
    public void Crlf_off_keeps_newlines_in_both_directions()
    {
        Assert.Contains("cr=keep", Args(Send() with { CrLf = false }));
        Assert.Contains("cr=keep", Args(Receive() with { CrLf = false }));
    }

    [Fact]
    public void Remap_off() => Assert.Contains("remap=no", Args(Send() with { Remap = false }));

    [Fact]
    public void Binary_omits_cr_and_remap_in_both_directions()
    {
        Assert.Equal(["direction=send", "hostfile=LIZTERM.JCL(JOB1)", "localfile=/tmp/job.jcl", "host=tso", "mode=binary"], Args(Send() with { Mode = TransferMode.Binary }));
        Assert.Equal(["direction=receive", "hostfile=LIZTERM.ITEST", "localfile=/tmp/out.txt", "host=tso", "mode=binary", "exist=replace"], Args(Receive() with { Mode = TransferMode.Binary }));
    }

    [Fact]
    public void Append_in_both_directions()
    {
        Assert.Equal("exist=append", Args(Send() with { Append = true })[^1]);
        var receive = Args(Receive() with { Append = true });
        Assert.Contains("exist=append", receive);
        Assert.DoesNotContain("exist=replace", receive);
    }

    [Fact]
    public void Tso_send_with_record_format_and_tracks()
    {
        var r = Send() with { RecordFormat = RecordFormat.Fixed, Lrecl = 80, Blksize = 3120, AllocationUnits = AllocationUnits.Tracks, PrimarySpace = 5, SecondarySpace = 2 };
        Assert.Equal([.. SendHead, "recfm=fixed", "lrecl=80", "blksize=3120", "allocation=tracks", "primaryspace=5", "secondaryspace=2"], Args(r));
    }

    [Fact]
    public void Undefined_record_format_and_cylinders()
    {
        var r = Send() with { RecordFormat = RecordFormat.Undefined, AllocationUnits = AllocationUnits.Cylinders, PrimarySpace = 1 };
        Assert.Equal(["recfm=undefined", "allocation=cylinders", "primaryspace=1"], Args(r)[7..]);
    }

    [Fact]
    public void Avblock_carries_the_average_block()
    {
        var r = Send() with { AllocationUnits = AllocationUnits.AvBlock, PrimarySpace = 100, AverageBlock = 4096 };
        Assert.Equal(["allocation=avblock", "primaryspace=100", "avblock=4096"], Args(r)[7..]);
    }

    [Fact]
    public void Average_block_is_dropped_for_other_units()
    {
        var r = Send() with { AllocationUnits = AllocationUnits.Cylinders, PrimarySpace = 1, AverageBlock = 4096 };
        Assert.Equal(["allocation=cylinders", "primaryspace=1"], Args(r)[7..]);
    }

    [Fact]
    public void Lrecl_and_blksize_need_a_record_format() =>
        Assert.Equal(SendHead, Args(Send() with { Lrecl = 80, Blksize = 3120 }));

    [Fact]
    public void Space_fields_need_allocation_units() =>
        Assert.Equal(SendHead, Args(Send() with { PrimarySpace = 5, SecondarySpace = 1, AverageBlock = 4096 }));

    [Fact]
    public void Vm_send_takes_record_format_and_lrecl_only()
    {
        var r = Send(TransferHostType.Vm) with { RecordFormat = RecordFormat.Variable, Lrecl = 255, Blksize = 3120, AllocationUnits = AllocationUnits.Tracks, PrimarySpace = 5 };
        Assert.Equal(["direction=send", "hostfile=LIZTERM.JCL(JOB1)", "localfile=/tmp/job.jcl", "host=vm", "mode=ascii", "cr=remove", "remap=yes", "recfm=variable", "lrecl=255"], Args(r));
    }

    [Fact]
    public void Vm_omits_the_undefined_record_format_and_its_lrecl()
    {
        var r = Send(TransferHostType.Vm) with { RecordFormat = RecordFormat.Undefined, Lrecl = 80 };
        Assert.Equal(7, Args(r).Length);
        Assert.Equal("host=vm", Args(r)[3]);
    }

    [Fact]
    public void Cics_send_has_no_allocation_keywords()
    {
        var r = Send(TransferHostType.Cics) with { RecordFormat = RecordFormat.Fixed, Lrecl = 80, AllocationUnits = AllocationUnits.Tracks, PrimarySpace = 5 };
        Assert.Equal(["direction=send", "hostfile=LIZTERM.JCL(JOB1)", "localfile=/tmp/job.jcl", "host=cics", "mode=ascii", "cr=remove", "remap=yes"], Args(r));
    }

    [Fact]
    public void Receive_ignores_every_allocation_field()
    {
        var r = Receive() with { RecordFormat = RecordFormat.Fixed, Lrecl = 80, Blksize = 3120, AllocationUnits = AllocationUnits.AvBlock, PrimarySpace = 5, SecondarySpace = 1, AverageBlock = 4096 };
        Assert.Equal(["direction=receive", "hostfile=LIZTERM.ITEST", "localfile=/tmp/out.txt", "host=tso", "mode=ascii", "cr=add", "remap=yes", "exist=replace"], Args(r));
    }

    [Fact]
    public void Buffer_size_and_extra_options_come_last()
    {
        var args = Args(Receive() with { BufferSize = 8192, ExtraOptions = "  NOTRUNC " });
        Assert.Equal(["buffersize=8192", "otheroptions=NOTRUNC"], args[^2..]);
        Assert.DoesNotContain(Args(Receive() with { ExtraOptions = "   " }), a => a.StartsWith("otheroptions", StringComparison.Ordinal));
    }

    [Fact]
    public void Paths_and_vm_names_with_spaces_pass_through_unchanged()
    {
        var r = Send(TransferHostType.Vm) with { LocalPath = "/Users/me/My Files/profile exec", HostFile = "PROFILE EXEC A" };
        var args = Args(r);
        Assert.Contains("localfile=/Users/me/My Files/profile exec", args);
        Assert.Contains("hostfile=PROFILE EXEC A", args);
        var json = RunOperation.Serialize("1", [TransferMapper.ToAction(r)]);
        Assert.Contains("\"hostfile=PROFILE EXEC A\"", json);
    }

    [Fact]
    public void Cancel_action()
    {
        Assert.Equal("Transfer", TransferMapper.CancelAction.Name);
        Assert.Equal(["Cancel"], TransferMapper.CancelAction.Args);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/LizTerm.Backend.B3270.Tests --filter "FullyQualifiedName~TransferMapperTests"`
Expected: build error, `TransferMapper` does not exist.

- [ ] **Step 3: Write the mapper**

`src/LizTerm.Backend.B3270/Protocol/TransferMapper.cs`:

```csharp
using LizTerm.Core.Session;

namespace LizTerm.Backend.B3270.Protocol;

/// <summary>Turns a Core request into b3270's Transfer(keyword=value,...) action. Keywords b3270 would reject for
/// the direction, mode, or host type are omitted rather than passed through, and keywords its IND$FILE command
/// builder would ignore (LRECL and BLKSIZE without a RECFM, SPACE without units) are omitted so the wire log
/// stays honest. Values are raw strings; RunOperation quotes them as JSON, so spaces need no escaping.</summary>
public static class TransferMapper
{
    public static readonly B3270Action CancelAction = new("Transfer", "Cancel");

    public static B3270Action ToAction(FileTransferRequest request)
    {
        var send = request.Direction == TransferDirection.Send;
        var text = request.Mode == TransferMode.Text;
        var tso = request.HostType == TransferHostType.Tso;
        var vm = request.HostType == TransferHostType.Vm;

        var args = new List<string>
        {
            "direction=" + (send ? "send" : "receive"),
            "hostfile=" + request.HostFile,
            "localfile=" + request.LocalPath,
            "host=" + HostKeyword(request.HostType),
            "mode=" + (text ? "ascii" : "binary"),
        };

        if (text)
        {
            args.Add("cr=" + (request.CrLf ? (send ? "remove" : "add") : "keep"));
            args.Add("remap=" + (request.Remap ? "yes" : "no"));
        }

        if (request.Append) args.Add("exist=append");
        else if (!send) args.Add("exist=replace");

        if (send && (tso || vm))
        {
            var recfm = RecordFormatKeyword(request.RecordFormat, tso);
            if (recfm is not null)
            {
                args.Add("recfm=" + recfm);
                if (request.Lrecl is { } lrecl) args.Add("lrecl=" + lrecl);
                if (tso && request.Blksize is { } blksize) args.Add("blksize=" + blksize);
            }
            if (tso && request.AllocationUnits != AllocationUnits.Default)
            {
                args.Add("allocation=" + UnitsKeyword(request.AllocationUnits));
                if (request.PrimarySpace is { } primary) args.Add("primaryspace=" + primary);
                if (request.SecondarySpace is { } secondary) args.Add("secondaryspace=" + secondary);
                if (request.AllocationUnits == AllocationUnits.AvBlock && request.AverageBlock is { } avblock) args.Add("avblock=" + avblock);
            }
        }

        if (request.BufferSize is { } buffer) args.Add("buffersize=" + buffer);
        if (!string.IsNullOrWhiteSpace(request.ExtraOptions)) args.Add("otheroptions=" + request.ExtraOptions.Trim());

        return new B3270Action("Transfer", args.ToArray());
    }

    private static string HostKeyword(TransferHostType type) => type switch
    {
        TransferHostType.Tso => "tso",
        TransferHostType.Vm => "vm",
        _ => "cics",
    };

    /// <summary>Null for Default, and for Undefined on VM, which has no undefined-length records.</summary>
    private static string? RecordFormatKeyword(RecordFormat format, bool tso) => format switch
    {
        RecordFormat.Fixed => "fixed",
        RecordFormat.Variable => "variable",
        RecordFormat.Undefined when tso => "undefined",
        _ => null,
    };

    private static string UnitsKeyword(AllocationUnits units) => units switch
    {
        AllocationUnits.Tracks => "tracks",
        AllocationUnits.Cylinders => "cylinders",
        _ => "avblock",
    };
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/LizTerm.Backend.B3270.Tests --filter "FullyQualifiedName~TransferMapperTests"`
Expected: 19 passed.

- [ ] **Step 5: Run the full suite and commit**

Run: `dotnet test LizTerm.slnx`
Expected: everything green.

```bash
git add src/LizTerm.Backend.B3270/Protocol/TransferMapper.cs tests/LizTerm.Backend.B3270.Tests/Protocol/TransferMapperTests.cs
git commit -m "Map a file transfer request to b3270's Transfer action keywords

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 3: TransferAsync on the session interface, the b3270 backend, and the app fake

**Files:**
- Modify: `src/LizTerm.Core/Session/IEmulatorSession.cs`
- Modify: `src/LizTerm.Backend.B3270/B3270Session.cs` (fields after `_disconnected`; `HandleStateIndication`; the actions section after `MoveCursorAsync`)
- Modify: `tests/LizTerm.App.Tests/Fakes/FakeEmulatorSession.cs`
- Test: `tests/LizTerm.Backend.B3270.Tests/B3270SessionTransferTests.cs`

**Interfaces:**
- Consumes: Task 1 types; `TransferMapper.ToAction` and `TransferMapper.CancelAction` from Task 2; `RunRawAsync`, `_pending`, `_process`, `FtIndication` in `B3270Session`.
- Produces: `Task<FileTransferResult> TransferAsync(FileTransferRequest request, IProgress<long>? progress = null, CancellationToken cancellationToken = default)` on `IEmulatorSession`; `internal bool IsTransferInProgress` on `B3270Session`; on `FakeEmulatorSession`: `FileTransferRequest? LastTransferRequest`, `IProgress<long>? TransferProgress`, `CancellationToken TransferToken`, `FileTransferResult TransferResult` (default success), `Exception? TransferException`, `TaskCompletionSource? TransferCompletion`, and calls recorded as `transfer:<Direction>:<HostFile>`. Tasks 6 to 10 use the fake's members.

- [ ] **Step 1: Add the method to the interface**

In `src/LizTerm.Core/Session/IEmulatorSession.cs`, after `Task MoveCursorAsync(int row, int column);` add:

```csharp
    /// <summary>Runs one IND$FILE transfer and completes when it ends. A transfer the engine or host refuses or
    /// aborts is a result with <c>Succeeded</c> false and the message to show verbatim. Throws
    /// <see cref="InvalidOperationException"/> when the session is not started or a transfer is already running,
    /// <see cref="OperationCanceledException"/> when the token cancelled it and the engine then reported failure,
    /// and <see cref="BackendUnavailableException"/> when the engine dies. A success that beats a cancel is
    /// returned as success. Progress reports bytes so far on the backend thread and may never be called.
    /// On receive an existing local file is replaced; the caller obtains consent.</summary>
    Task<FileTransferResult> TransferAsync(FileTransferRequest request, IProgress<long>? progress = null, CancellationToken cancellationToken = default);
```

- [ ] **Step 2: Implement the fake so the App tests keep compiling**

In `tests/LizTerm.App.Tests/Fakes/FakeEmulatorSession.cs`, after `public Exception? ActionException { get; set; }` add:

```csharp
    public FileTransferRequest? LastTransferRequest { get; private set; }
    public IProgress<long>? TransferProgress { get; private set; }
    public CancellationToken TransferToken { get; private set; }
    public FileTransferResult TransferResult { get; set; } = new(true, "Transfer complete, 12 bytes transferred", 12);
    /// <summary>When set, TransferAsync throws it (after TransferCompletion, if that is set too).</summary>
    public Exception? TransferException { get; set; }
    /// <summary>When set, TransferAsync waits for it before answering, so a test can push progress through
    /// TransferProgress and cancel through TransferToken while the dialog is in its Running phase.</summary>
    public TaskCompletionSource? TransferCompletion { get; set; }
```

and after `MoveCursorAsync` add:

```csharp
    public async Task<FileTransferResult> TransferAsync(FileTransferRequest request, IProgress<long>? progress = null, CancellationToken cancellationToken = default)
    {
        Calls.Add($"transfer:{request.Direction}:{request.HostFile}");
        LastTransferRequest = request;
        TransferProgress = progress;
        TransferToken = cancellationToken;
        if (TransferCompletion is { } completion) await completion.Task;
        if (TransferException is not null) throw TransferException;
        return TransferResult;
    }
```

- [ ] **Step 3: Write the failing backend tests**

`tests/LizTerm.Backend.B3270.Tests/B3270SessionTransferTests.cs`:

```csharp
using System.Text.RegularExpressions;
using LizTerm.Backend.B3270.Tests.Fakes;
using LizTerm.Core.Session;

namespace LizTerm.Backend.B3270.Tests;

/// <summary>The transfer state machine on the fake process. The fake answers every run at once except a Transfer
/// (not Cancel), which stays pending until the test emits its run-result, exactly as b3270 behaves.</summary>
public class B3270SessionTransferTests
{
    private static readonly SessionProfile Profile = new() { Name = "t", Host = "h", Port = 23 };
    private static readonly FileTransferRequest Request = new() { Direction = TransferDirection.Send, LocalPath = "/nonexistent/a.txt", HostFile = "A.B" };
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(2);

    private static async Task<(B3270Session Session, FakeB3270Process Fake)> StartAsync()
    {
        var fake = new FakeB3270Process();
        fake.RunResponder = line => IsTransferStart(line) ? Array.Empty<string>() : new[] { AutoResult(line) };
        var session = new B3270Session(Profile, () => fake);
        await session.StartProcessAsync(CancellationToken.None);
        return (session, fake);
    }

    private static bool IsTransferStart(string line) => line.Contains("\"action\":\"Transfer\"") && !line.Contains("\"Cancel\"");

    private static string Tag(string inputLine) => Regex.Match(inputLine, "\"r-tag\":\"([^\"]+)\"").Groups[1].Value;

    private static string AutoResult(string inputLine) => $$$"""{"run-result":{"r-tag":"{{{Tag(inputLine)}}}","success":true,"time":0}}""";

    private static Task<string> TransferLineAsync(FakeB3270Process fake) => fake.WaitForInputAsync(IsTransferStart, Wait);

    private static string SuccessResult(string transferLine, string text) =>
        $$$"""{"run-result":{"r-tag":"{{{Tag(transferLine)}}}","success":true,"text":["{{{text}}}"],"time":0.5}}""";

    private static string FailureResult(string transferLine, string text) =>
        $$$"""{"run-result":{"r-tag":"{{{Tag(transferLine)}}}","success":false,"text":["{{{text}}}"],"time":0.5}}""";

    private sealed class Reports : IProgress<long>
    {
        private readonly List<long> _values = [];
        public long[] Values { get { lock (_values) return _values.ToArray(); } }
        public void Report(long value) { lock (_values) _values.Add(value); }
    }

    private static async Task WaitUntilAsync(Func<bool> condition, string what)
    {
        var deadline = DateTime.UtcNow + Wait;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("Timed out waiting for " + what);
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task Sends_the_mapped_action_reports_progress_and_returns_text_and_count()
    {
        var (session, fake) = await StartAsync();
        var reports = new Reports();
        var transfer = session.TransferAsync(Request, reports, TestContext.Current.CancellationToken);
        var line = await TransferLineAsync(fake);
        Assert.Contains("\"action\":\"Transfer\",\"args\":[\"direction=send\",\"hostfile=A.B\",\"localfile=/nonexistent/a.txt\",\"host=tso\",\"mode=ascii\",\"cr=remove\",\"remap=yes\"]", line);
        Assert.True(session.IsTransferInProgress);

        fake.Emit("""{"ft":{"state":"awaiting","cause":"ui"}}""");
        fake.Emit("""{"ft":{"state":"running","bytes":0,"cause":"ui"}}""");
        fake.Emit("""{"ft":{"state":"running","bytes":2048,"cause":"ui"}}""");
        fake.Emit("""{"ft":{"state":"complete","success":true,"text":"Transfer complete, 2048 bytes transferred","cause":"ui"}}""");
        fake.Emit($$$"""{"run-result":{"r-tag":"{{{Tag(line)}}}","success":true,"text":["Transfer complete, 2048 bytes transferred","2.0 Kbytes/sec in DFT mode"],"time":1.5}}""");

        var result = await transfer.WaitAsync(Wait, TestContext.Current.CancellationToken);
        Assert.True(result.Succeeded);
        Assert.Equal("Transfer complete, 2048 bytes transferred\n2.0 Kbytes/sec in DFT mode", result.Message);
        Assert.Equal(2048, result.Bytes);
        Assert.Equal([0L, 2048L], reports.Values);
        Assert.False(session.IsTransferInProgress);
    }

    [Fact]
    public async Task A_failed_run_result_is_a_failed_result_with_the_host_text()
    {
        var (session, fake) = await StartAsync();
        var transfer = session.TransferAsync(Request, cancellationToken: TestContext.Current.CancellationToken);
        var line = await TransferLineAsync(fake);
        fake.Emit("""{"ft":{"state":"complete","success":false,"text":"TRANS17 Miscellaneous I/O error","cause":"ui"}}""");
        fake.Emit(FailureResult(line, "TRANS17 Miscellaneous I/O error"));

        var result = await transfer.WaitAsync(Wait, TestContext.Current.CancellationToken);
        Assert.False(result.Succeeded);
        Assert.Equal("TRANS17 Miscellaneous I/O error", result.Message);
        Assert.Equal(0, result.Bytes);
        Assert.False(session.IsTransferInProgress);
    }

    [Fact]
    public async Task Cancel_sends_the_cancel_action_and_the_failed_result_becomes_cancellation()
    {
        var (session, fake) = await StartAsync();
        using var cts = new CancellationTokenSource();
        var transfer = session.TransferAsync(Request, cancellationToken: cts.Token);
        var line = await TransferLineAsync(fake);
        fake.Emit("""{"ft":{"state":"running","bytes":512,"cause":"ui"}}""");

        cts.Cancel();
        var cancelLine = await fake.WaitForInputAsync(l => l.Contains("\"action\":\"Transfer\",\"args\":[\"Cancel\"]"), Wait);
        Assert.NotEqual(Tag(line), Tag(cancelLine));
        fake.Emit("""{"ft":{"state":"aborting","cause":"ui"}}""");
        fake.Emit("""{"ft":{"state":"complete","success":false,"text":"Transfer canceled by user","cause":"ui"}}""");
        fake.Emit(FailureResult(line, "Transfer canceled by user"));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => transfer.WaitAsync(Wait, TestContext.Current.CancellationToken));
        Assert.False(session.IsTransferInProgress);
    }

    [Fact]
    public async Task A_success_that_beats_the_cancel_is_still_a_success()
    {
        var (session, fake) = await StartAsync();
        using var cts = new CancellationTokenSource();
        var transfer = session.TransferAsync(Request, cancellationToken: cts.Token);
        var line = await TransferLineAsync(fake);
        cts.Cancel();
        await fake.WaitForInputAsync(l => l.Contains("\"Cancel\""), Wait);
        fake.Emit(SuccessResult(line, "Transfer complete, 10 bytes transferred"));

        var result = await transfer.WaitAsync(Wait, TestContext.Current.CancellationToken);
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task A_second_transfer_while_one_is_pending_throws_and_sends_nothing()
    {
        var (session, fake) = await StartAsync();
        var first = session.TransferAsync(Request, cancellationToken: TestContext.Current.CancellationToken);
        var line = await TransferLineAsync(fake);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => session.TransferAsync(Request, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal("A file transfer is already in progress.", ex.Message);
        Assert.Single(fake.InputLines, IsTransferStart);

        fake.Emit(SuccessResult(line, "Transfer complete, 1 bytes transferred"));
        Assert.True((await first.WaitAsync(Wait, TestContext.Current.CancellationToken)).Succeeded);
    }

    [Fact]
    public async Task Engine_exit_mid_transfer_throws_and_frees_the_slot()
    {
        var (session, fake) = await StartAsync();
        var transfer = session.TransferAsync(Request, cancellationToken: TestContext.Current.CancellationToken);
        await TransferLineAsync(fake);

        fake.Exit(1);

        await Assert.ThrowsAsync<BackendUnavailableException>(() => transfer.WaitAsync(Wait, TestContext.Current.CancellationToken));
        Assert.False(session.IsTransferInProgress);
    }

    [Fact]
    public async Task Ft_indications_with_no_transfer_in_flight_are_ignored()
    {
        var (session, fake) = await StartAsync();
        fake.Emit("""{"ft":{"state":"running","bytes":99,"cause":"ui"}}""");
        // An oia line behind it proves the reader thread has consumed the stray ft line before the transfer starts.
        fake.Emit("""{"oia":{"field":"insert","value":"true"}}""");
        await WaitUntilAsync(() => session.KeyboardStatus.InsertMode, "the oia line");

        var transfer = session.TransferAsync(Request, cancellationToken: TestContext.Current.CancellationToken);
        var line = await TransferLineAsync(fake);
        fake.Emit(SuccessResult(line, "Transfer complete, 0 bytes transferred"));

        var result = await transfer.WaitAsync(Wait, TestContext.Current.CancellationToken);
        Assert.Equal(0, result.Bytes);
    }

    [Fact]
    public async Task A_cancelled_token_throws_before_anything_is_sent()
    {
        var (session, fake) = await StartAsync();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.TransferAsync(Request, cancellationToken: cts.Token));
        Assert.DoesNotContain(fake.InputLines, l => l.Contains("Transfer"));
        Assert.False(session.IsTransferInProgress);
    }

    [Fact]
    public async Task Transfer_before_start_throws()
    {
        var session = new B3270Session(Profile, () => new FakeB3270Process());
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => session.TransferAsync(Request));
        Assert.Equal("The session has not been started.", ex.Message);
    }
}
```

- [ ] **Step 4: Run the tests to verify they fail**

Run: `dotnet test tests/LizTerm.Backend.B3270.Tests --filter "FullyQualifiedName~B3270SessionTransferTests"`
Expected: build error, `B3270Session` does not implement `TransferAsync`.

- [ ] **Step 5: Implement the backend**

In `src/LizTerm.Backend.B3270/B3270Session.cs`:

After `private volatile TaskCompletionSource? _disconnected;` add (not volatile: `Interlocked` needs a plain field):

```csharp
    private TransferContext? _transfer;
```

In `HandleStateIndication`, before `case PopupIndication popup:` add:

```csharp
            case FtIndication { Bytes: { } bytes } when _transfer is { } transfer:
                // Progress only. The outcome comes from the Transfer run's run-result, which carries the same
                // text as the "complete" indication; ft lines with no transfer in flight are dropped.
                transfer.Bytes = bytes;
                transfer.Progress?.Report(bytes);
                break;
```

After `MoveCursorAsync` (end of the class) add:

```csharp

    // ---- file transfer ----

    /// <summary>True while a Transfer run is pending. Tests use it to check the slot is freed.</summary>
    internal bool IsTransferInProgress => _transfer is not null;

    public async Task<FileTransferResult> TransferAsync(FileTransferRequest request, IProgress<long>? progress = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_process is null) throw new InvalidOperationException("The session has not been started.");
        var context = new TransferContext(progress);
        if (Interlocked.CompareExchange(ref _transfer, context, null) is not null)
            throw new InvalidOperationException("A file transfer is already in progress.");
        try
        {
            // b3270 does not answer the Transfer run until the transfer ends, so this run-result is the outcome.
            var run = RunRawAsync([TransferMapper.ToAction(request)]);
            using var registration = cancellationToken.Register(() => _ = TryCancelTransferAsync());
            var result = await run;
            if (!result.Success && cancellationToken.IsCancellationRequested)
                throw new OperationCanceledException("The file transfer was cancelled.", cancellationToken);
            return new FileTransferResult(result.Success, string.Join("\n", result.Text), context.Bytes);
        }
        finally
        {
            Interlocked.CompareExchange(ref _transfer, null, context);
        }
    }

    /// <summary>Transfer(Cancel) is fire-and-forget: b3270 answers "No transfer pending." if the transfer already
    /// ended, and a dead engine faults the pending Transfer run on its own.</summary>
    private async Task TryCancelTransferAsync()
    {
        try
        {
            await RunRawAsync([TransferMapper.CancelAction]);
        }
        catch (Exception)
        {
        }
    }

    private sealed class TransferContext(IProgress<long>? progress)
    {
        public IProgress<long>? Progress { get; } = progress;
        public long Bytes;
    }
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/LizTerm.Backend.B3270.Tests --filter "FullyQualifiedName~B3270SessionTransferTests"`
Expected: 9 passed.

- [ ] **Step 7: Run the full suite and commit**

Run: `dotnet test LizTerm.slnx`
Expected: everything green, including the App tests, which now compile against the extended fake.

```bash
git add src/LizTerm.Core/Session/IEmulatorSession.cs src/LizTerm.Backend.B3270/B3270Session.cs tests/LizTerm.App.Tests/Fakes/FakeEmulatorSession.cs tests/LizTerm.Backend.B3270.Tests/B3270SessionTransferTests.cs
git commit -m "Add TransferAsync: one in-flight IND\$FILE transfer over b3270's Transfer action

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 4: Live round trip against MVS/CE, the recorded fixture, and its parser test

**Files:**
- Create: `tests/LizTerm.Integration.Tests/ScreenWaiter.cs`, `tests/LizTerm.Integration.Tests/TsoNavigator.cs`, `tests/LizTerm.Backend.B3270.Tests/Fixtures/indfile-tso-roundtrip.jsonl`
- Modify: `tests/LizTerm.Integration.Tests/LiveHostTests.cs`, `tests/LizTerm.Backend.B3270.Tests/Protocol/IndicationParserTests.cs`, `tests/LizTerm.Backend.B3270.Tests/Fixtures/README.md`

**Interfaces:**
- Consumes: `B3270Session.TransferAsync` (Task 3), `TransferMapper` defaults (Task 2), `IEmulatorSession.ScreenUpdated`, `KeyboardStatus.Lock`, `ScreenSnapshot.ToText()`.
- Produces: `ScreenWaiter` and `TsoNavigator` (integration project only), the fixture, and possibly a changed mapper default (see Step 7). Nothing later depends on these types.

This task needs the live host. Before starting, check the environment without printing any value:

```bash
for v in LIZTERM_TEST_HOST LIZTERM_TEST_USER LIZTERM_TEST_PASSWORD; do printenv "$v" > /dev/null && echo "$v: set" || echo "$v: MISSING"; done
```

If any is MISSING, stop and report to Robert; do not guess, type, or look up credentials. `LIZTERM_TEST_HOST` is expected to be `10.42.37.209:3270` (plain TN3270, no TLS variables needed).

- [ ] **Step 1: Write the screen waiter**

`tests/LizTerm.Integration.Tests/ScreenWaiter.cs`:

```csharp
using LizTerm.Core.Screen;
using LizTerm.Core.Session;

namespace LizTerm.Integration.Tests;

/// <summary>Waits on host screens by text, never by coordinates. Every timeout throws with the last screen's
/// text, so a failing live run shows what the host actually displayed.</summary>
internal sealed class ScreenWaiter : IDisposable
{
    private readonly IEmulatorSession _session;
    private readonly object _lock = new();
    private ScreenSnapshot _latest;
    private TaskCompletionSource _changed = NewSignal();

    public ScreenWaiter(IEmulatorSession session)
    {
        _session = session;
        _latest = session.CurrentScreen;
        session.ScreenUpdated += OnScreen;
    }

    public ScreenSnapshot Latest { get { lock (_lock) return _latest; } }

    public string LatestText => Latest.ToText();

    /// <summary>Returns the first screen (the current one included) whose text satisfies the predicate.</summary>
    public async Task<ScreenSnapshot> WaitForAsync(Func<string, bool> predicate, TimeSpan timeout, string what)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            ScreenSnapshot screen;
            Task changed;
            lock (_lock) { screen = _latest; changed = _changed.Task; }
            if (predicate(screen.ToText())) return screen;
            var remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero) throw new TimeoutException($"Timed out waiting for {what}. Last screen:\n{screen.ToText()}");
            await Task.WhenAny(changed, Task.Delay(remaining));
        }
    }

    /// <summary>b3270 rejects String() while the keyboard is locked, so every keystroke waits for the unlock.</summary>
    public async Task WaitForUnlockedKeyboardAsync(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (_session.KeyboardStatus.Lock != KeyboardLock.Unlocked)
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException($"Keyboard still locked ({_session.KeyboardStatus.Lock}). Last screen:\n{LatestText}");
            await Task.Delay(50);
        }
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private void OnScreen(object? sender, ScreenSnapshot snapshot)
    {
        lock (_lock)
        {
            _latest = snapshot;
            _changed.TrySetResult();
            _changed = NewSignal();
        }
    }

    public void Dispose() => _session.ScreenUpdated -= OnScreen;
}
```

- [ ] **Step 2: Write the TSO navigator**

`tests/LizTerm.Integration.Tests/TsoNavigator.cs`:

```csharp
using LizTerm.Core.Session;

namespace LizTerm.Integration.Tests;

/// <summary>Drives an MVS 3.8j TSO session: logon to READY, a command that returns to READY, and logoff. Screens
/// are recognized by text only, so the small differences between TK4-, TK5, and MVS/CE logon screens do not
/// matter. READY means the last non-blank line of the screen is exactly READY, which is where IND$FILE must be
/// started from.</summary>
internal sealed class TsoNavigator(IEmulatorSession session, ScreenWaiter screens)
{
    private static readonly TimeSpan Step = TimeSpan.FromSeconds(30);

    public static bool IsAtReady(string text)
    {
        var last = text.Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.Length > 0);
        return last == "READY";
    }

    public async Task LogonAsync(string user, string password)
    {
        await screens.WaitForAsync(t => t.Contains("LOGON", StringComparison.OrdinalIgnoreCase), Step, "the logon screen");
        await TypeAndEnterAsync("LOGON " + user);
        await screens.WaitForAsync(t => t.Contains("PASSWORD", StringComparison.OrdinalIgnoreCase), Step, "the password prompt");
        await TypeAndEnterAsync(password);
        await ReachReadyAsync();
    }

    /// <summary>Presses Enter through "***" pauses and PF3 out of any menu (ISPF, RPF, a logon proc's panel)
    /// until the screen is at READY.</summary>
    public async Task ReachReadyAsync()
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var screen = await screens.WaitForAsync(t => IsAtReady(t) || t.Contains("***") || t.Contains("===>"), Step, "READY, a *** pause, or a menu");
            var text = screen.ToText();
            if (IsAtReady(text)) return;
            await screens.WaitForUnlockedKeyboardAsync(Step);
            await session.SendKeyAsync(text.Contains("***") ? TerminalKey.Enter : TerminalKey.PF3);
            await screens.WaitForAsync(t => t != text, Step, "the screen to change");
        }
        throw new InvalidOperationException("Could not reach READY. Last screen:\n" + screens.LatestText);
    }

    /// <summary>Types a TSO command at READY and waits for READY to come back.</summary>
    public async Task CommandAsync(string command)
    {
        await TypeAndEnterAsync(command);
        await ReachReadyAsync();
    }

    /// <summary>LOGOFF, then wait for the host to drop the line or show the logon screen again.</summary>
    public async Task LogoffAsync()
    {
        await TypeAndEnterAsync("LOGOFF");
        var deadline = DateTime.UtcNow + Step;
        while (DateTime.UtcNow < deadline)
        {
            if (!session.ConnectionState.IsConnected()) return;
            if (screens.LatestText.Contains("LOGON", StringComparison.OrdinalIgnoreCase) && !IsAtReady(screens.LatestText)) return;
            await Task.Delay(200);
        }
    }

    private async Task TypeAndEnterAsync(string text)
    {
        await screens.WaitForUnlockedKeyboardAsync(Step);
        await session.TypeTextAsync(text);
        await session.SendKeyAsync(TerminalKey.Enter);
    }
}
```

- [ ] **Step 3: Add the round trip to the live tests**

In `tests/LizTerm.Integration.Tests/LiveHostTests.cs`, add `using System.Text;` is not needed; add these members inside the class. First a shared profile helper, replacing the inline parsing in `Connects_and_receives_a_screen` (its lines from `var colon = ...` through the `profile` initializer) with a call `var profile = ProfileFor(target!);`:

```csharp
    private static SessionProfile ProfileFor(string target)
    {
        var colon = target.LastIndexOf(':');
        var host = colon > 0 ? target[..colon] : target;
        var port = colon > 0 ? int.Parse(target[(colon + 1)..]) : 23;
        return new SessionProfile
        {
            Name = "integration",
            Host = host,
            Port = port,
            UseTls = Flag("LIZTERM_TEST_TLS", fallback: false),
            VerifyCertificate = Flag("LIZTERM_TEST_VERIFY_CERT", fallback: true),
        };
    }

    private sealed class ProgressLog : IProgress<long>
    {
        private readonly List<long> _values = [];
        public long[] Values { get { lock (_values) return _values.ToArray(); } }
        public void Report(long value) { lock (_values) _values.Add(value); }
    }

    /// <summary>Sends a small text file to LIZTERM.ITEST under the TSO user's prefix, receives it back, and compares
    /// with trailing blanks trimmed per line (IND$FILE pads records to the record length). Needs the three
    /// LIZTERM_TEST_* variables; the credentials are typed through TypeTextAsync, so a wire log of this run
    /// contains the password on its outbound side and must never be committed.</summary>
    [Fact]
    public async Task Indfile_round_trip_matches()
    {
        var target = Environment.GetEnvironmentVariable("LIZTERM_TEST_HOST");
        var user = Environment.GetEnvironmentVariable("LIZTERM_TEST_USER");
        var password = Environment.GetEnvironmentVariable("LIZTERM_TEST_PASSWORD");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(target), "LIZTERM_TEST_HOST is not set");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(user), "LIZTERM_TEST_USER is not set");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(password), "LIZTERM_TEST_PASSWORD is not set");
        var ct = TestContext.Current.CancellationToken;

        var dir = Directory.CreateTempSubdirectory("lizterm-indfile-");
        var sent = Path.Combine(dir.FullName, "sent.txt");
        var received = Path.Combine(dir.FullName, "received.txt");
        await File.WriteAllTextAsync(sent, "LizTerm IND$FILE round trip\nsecond line with lowercase text\nthird line has trailing spaces   \n//JOB1 JOB (ACCT),'LIZTERM',CLASS=A\nEND\n", ct);

        await using var session = new B3270Session(ProfileFor(target!), () => new B3270ChildProcess(B3270Locator.Find()), WireLog.FromEnvironment());
        using var screens = new ScreenWaiter(session);
        var tso = new TsoNavigator(session, screens);
        var dataset = "LIZTERM.ITEST";

        await session.ConnectAsync(ct);
        await tso.LogonAsync(user!.Trim(), password!);
        try
        {
            var progress = new ProgressLog();
            var up = await session.TransferAsync(new FileTransferRequest { Direction = TransferDirection.Send, LocalPath = sent, HostFile = dataset }, progress, ct);
            Assert.True(up.Succeeded, "send failed: " + up.Message);
            await tso.ReachReadyAsync();

            var down = await session.TransferAsync(new FileTransferRequest { Direction = TransferDirection.Receive, LocalPath = received, HostFile = dataset }, progress, ct);
            Assert.True(down.Succeeded, "receive failed: " + down.Message);
            await tso.ReachReadyAsync();

            Assert.NotEmpty(progress.Values);
            Assert.True(down.Bytes > 0, "no bytes were reported for the receive");
            var expected = (await File.ReadAllLinesAsync(sent, ct)).Select(l => l.TrimEnd());
            var actual = (await File.ReadAllLinesAsync(received, ct)).Select(l => l.TrimEnd());
            Assert.Equal(expected, actual);
        }
        finally
        {
            try
            {
                await tso.CommandAsync($"DELETE '{user!.Trim()}.{dataset}'");
                await tso.LogoffAsync();
                await session.DisconnectAsync();
            }
            catch (Exception ex)
            {
                TestContext.Current.SendDiagnosticMessage("cleanup failed: " + ex.Message);
            }
            dir.Delete(recursive: true);
        }
    }
```

- [ ] **Step 4: Build, then run the round trip with a wire log**

```bash
dotnet build tests/LizTerm.Integration.Tests
S=$(mktemp -d)
LIZTERM_B3270_PATH=/opt/homebrew/bin/b3270 LIZTERM_WIRE_LOG="$S/indfile.log" dotnet test tests/LizTerm.Integration.Tests --filter "FullyQualifiedName~Indfile_round_trip_matches" --logger "console;verbosity=normal"
```

Keep `S` for the fixture step. Expected: 1 passed within about two minutes. `Connects_and_receives_a_screen` still passes when run without the filter, now through `ProfileFor`.

- [ ] **Step 5: If the run fails, adapt by text rules, not coordinates**

The failure message carries the last screen. Typical adaptations, each a one-line change in `TsoNavigator`:

- The logon screen has no word LOGON (for example it only says "Enter your user id"): extend the first predicate with the text actually shown.
- The host asks for the password with different wording: extend the second predicate the same way.
- The logon lands in a menu that PF3 does not leave (the screen after PF3 is unchanged and the loop ends with "Could not reach READY"): add a rule before the PF3 branch that types the menu's exit command, for example `X` or `END`, when the text contains that menu's title.
- READY appears but the keyboard stays locked: raise `Step` for this host.

If IND$FILE rejects the send, the message is in `up.Message` in the assertion output. Rerun with `--logger "console;verbosity=detailed"` and read the wire log's last `ft` and `run-result` lines:

```bash
grep -n '"ft"\|"run-result"' "$S/indfile.log" | tail -8
```

If the MVS 3.8j port needs an option for a new dataset, first try it through `ExtraOptions` in the test's send request. Only if the Text defaults themselves are wrong for this host (for example the port refuses `CRLF`) change the default in `TransferMapper`, update the pinned expectation in `TransferMapperTests`, and note the change in the spec's section 4.1 in the same commit. Never work around a failure by loosening the comparison in the test.

- [ ] **Step 6: Cut the fixture from the inbound side of the wire log**

```bash
tools/wirelog-to-fixture.sh "$S/indfile.log" "$S/all.jsonl"
FIRST=$(grep -n '"ft"' "$S/all.jsonl" | head -1 | cut -d: -f1)
LAST=$(grep -n '"ft"' "$S/all.jsonl" | tail -1 | cut -d: -f1)
END=$(awk -v l="$LAST" 'NR>l && /"run-result"/ {print NR; exit}' "$S/all.jsonl")
sed -n "$((FIRST-3)),${END}p" "$S/all.jsonl" > tests/LizTerm.Backend.B3270.Tests/Fixtures/indfile-tso-roundtrip.jsonl
wc -l tests/LizTerm.Backend.B3270.Tests/Fixtures/indfile-tso-roundtrip.jsonl
grep -c '"ft"' tests/LizTerm.Backend.B3270.Tests/Fixtures/indfile-tso-roundtrip.jsonl
grep -F -c "$LIZTERM_TEST_PASSWORD" tests/LizTerm.Backend.B3270.Tests/Fixtures/indfile-tso-roundtrip.jsonl
grep -c '10.42.37.209' tests/LizTerm.Backend.B3270.Tests/Fixtures/indfile-tso-roundtrip.jsonl
```

Expected: a few dozen lines, at least six `ft` lines (awaiting, running, complete, twice), and `0` from both of the last two greps. If either prints anything other than `0`, the range is wrong; fix it before going on. The three lines before the first `ft` are there to catch the `oia` `file-transfer` lock; if they are something else, keep them anyway, they are harmless screen or oia lines. Do not commit `$S/indfile.log` or `$S/all.jsonl`.

- [ ] **Step 7: Write the fixture's parser test**

Append to `tests/LizTerm.Backend.B3270.Tests/Protocol/IndicationParserTests.cs`, inside the class:

```csharp
    [Fact]
    public void Indfile_fixture_parses_with_the_expected_ft_sequence()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "indfile-tso-roundtrip.jsonl");
        var states = new List<FtIndication>();
        foreach (var line in File.ReadLines(path))
        {
            Assert.True(IndicationParser.TryParse(line, out var indication), "unparsable line: " + line);
            if (indication is FtIndication ft) states.Add(ft);
        }
        // One send and one receive, each awaiting -> running... -> complete, both successful.
        Assert.Equal("awaiting", states[0].State);
        Assert.Contains(states, s => s.State == "running" && s.Bytes > 0);
        var completes = states.Where(s => s.State == "complete").ToList();
        Assert.Equal(2, completes.Count);
        Assert.All(completes, c =>
        {
            Assert.True(c.Success);
            Assert.Contains("Transfer complete", c.Text);
        });
    }
```

Run: `dotnet test tests/LizTerm.Backend.B3270.Tests --filter "FullyQualifiedName~Indfile_fixture_parses_with_the_expected_ft_sequence"`
Expected: 1 passed. If a `complete` line's `text` reads differently on this host, the assertion on "Transfer complete" is the one to adjust, to the exact text observed.

- [ ] **Step 8: Document the fixture**

Append to `tests/LizTerm.Backend.B3270.Tests/Fixtures/README.md`:

```markdown
- `indfile-tso-roundtrip.jsonl`: inbound lines of a real LizTerm session on 2026-09-04 against an MVS 3.8j (MVS/CE)
  host over plain TN3270, model 3279-2-E, trimmed to the two IND$FILE transfers of the integration lane's
  round trip: a text-mode send of a five-line file to `LIZTERM.ITEST` from the TSO READY prompt, the READY that
  follows, and a text-mode receive of the same dataset. Shows the `oia` `file-transfer` lock, the `ft` sequence
  `awaiting`, `running` (with `bytes`), `complete` (with `success` and `text`), and the `run-result` of each
  Transfer run, which b3270 sends only when the transfer ends and which carries the same text as `complete`.
  The logon and logoff that surround the transfers were cut, so no credentials and no host address are in the
  file. The observed logon flow on this host: <what the run showed: the logon screen's prompt, the password
  prompt's wording, and whether READY appeared directly or after a menu>.
```

Replace the angle-bracket note with what the run actually showed; the README must not keep it.

- [ ] **Step 9: Run the full suite and commit**

Run: `dotnet test LizTerm.slnx`
Expected: everything green; the integration project reports its tests as skipped unless the variables are still in the environment, in which case they pass.

```bash
rm -rf "$S"
git status --short
git add tests/LizTerm.Integration.Tests/ScreenWaiter.cs tests/LizTerm.Integration.Tests/TsoNavigator.cs tests/LizTerm.Integration.Tests/LiveHostTests.cs tests/LizTerm.Backend.B3270.Tests/Fixtures/indfile-tso-roundtrip.jsonl tests/LizTerm.Backend.B3270.Tests/Fixtures/README.md tests/LizTerm.Backend.B3270.Tests/Protocol/IndicationParserTests.cs
git commit -m "Round-trip a file through IND\$FILE on a live MVS 3.8j host and keep its ft sequence as a fixture

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

`git status --short` must show nothing under `src/` or `tests/` that is not in the `git add` list above, apart from a `TransferMapper` change made deliberately in Step 5, which goes into this commit too with its test and spec note.

---

### Task 5: File picker seam and local name suggestion (App)

**Files:**
- Create: `src/LizTerm.App/Files/IFilePicker.cs`, `src/LizTerm.App/Files/AvaloniaFilePicker.cs`, `src/LizTerm.App/Files/LocalFileNames.cs`, `tests/LizTerm.App.Tests/Fakes/FakeFilePicker.cs`
- Test: `tests/LizTerm.App.Tests/Files/LocalFileNamesTests.cs`

**Interfaces:**
- Produces: `interface IFilePicker { Task<string?> PickFileToSendAsync(); Task<string?> PickSaveLocationAsync(string suggestedFileName); }` in `LizTerm.App.Files`; `sealed class AvaloniaFilePicker(TopLevel topLevel) : IFilePicker`; `sealed class FakeFilePicker : IFilePicker` with `string? Result { get; set; }`, `Exception? Exception { get; set; }`, `List<string> Calls` recording `open` and `save:<suggested>`; `static string LocalFileNames.Suggest(string hostFile, TransferHostType hostType)` with `const string Fallback = "received"`. Tasks 6 to 10 use them.

- [ ] **Step 1: Write the failing name tests**

`tests/LizTerm.App.Tests/Files/LocalFileNamesTests.cs`:

```csharp
using LizTerm.App.Files;
using LizTerm.Core.Session;

namespace LizTerm.App.Tests.Files;

public class LocalFileNamesTests
{
    [Theory]
    [InlineData("LIZTERM.JCL(JOB1)", TransferHostType.Tso, "JOB1")]
    [InlineData("'MVSCE02.LIZTERM.JCL(JOB1)'", TransferHostType.Tso, "JOB1")]
    [InlineData("LIZTERM.ITEST", TransferHostType.Tso, "ITEST")]
    [InlineData("'MVSCE02.LIZTERM.ITEST'", TransferHostType.Tso, "ITEST")]
    [InlineData("ITEST", TransferHostType.Tso, "ITEST")]
    [InlineData("A.B()", TransferHostType.Tso, "received")]
    [InlineData("A.B.", TransferHostType.Tso, "received")]
    [InlineData("PROFILE EXEC A", TransferHostType.Vm, "PROFILE.EXEC")]
    [InlineData("PROFILE  EXEC", TransferHostType.Vm, "PROFILE.EXEC")]
    [InlineData("PROFILE", TransferHostType.Vm, "PROFILE")]
    [InlineData("MYFILE", TransferHostType.Cics, "MYFILE")]
    [InlineData("my file", TransferHostType.Cics, "my file")]
    [InlineData("   ", TransferHostType.Tso, "received")]
    [InlineData("''", TransferHostType.Tso, "received")]
    public void Suggests_a_local_name_from_the_host_name(string hostFile, TransferHostType type, string expected) =>
        Assert.Equal(expected, LocalFileNames.Suggest(hostFile, type));
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~LocalFileNamesTests"`
Expected: build error, `LocalFileNames` does not exist.

- [ ] **Step 3: Write the seam, the Avalonia implementation, the fake, and the helper**

`src/LizTerm.App/Files/IFilePicker.cs`:

```csharp
namespace LizTerm.App.Files;

/// <summary>Local file selection for transfers, injected into the dialog view model so tests can fake it, like
/// <see cref="LizTerm.App.Clipboard.ITextClipboard"/>.</summary>
public interface IFilePicker
{
    /// <summary>OS Open dialog. Null when cancelled or when the choice has no local path.</summary>
    Task<string?> PickFileToSendAsync();

    /// <summary>OS Save dialog, which asks before overwriting an existing file. Null when cancelled.</summary>
    Task<string?> PickSaveLocationAsync(string suggestedFileName);
}
```

`src/LizTerm.App/Files/AvaloniaFilePicker.cs`:

```csharp
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace LizTerm.App.Files;

/// <summary>Wraps a top level's storage provider, resolved at each call so the dialog need not be open when the
/// picker is constructed.</summary>
public sealed class AvaloniaFilePicker(TopLevel topLevel) : IFilePicker
{
    public async Task<string?> PickFileToSendAsync()
    {
        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "File to send",
            AllowMultiple = false,
        });
        return files.Count == 0 ? null : files[0].TryGetLocalPath();
    }

    public async Task<string?> PickSaveLocationAsync(string suggestedFileName)
    {
        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save received file as",
            SuggestedFileName = suggestedFileName,
        });
        return file?.TryGetLocalPath();
    }
}
```

`src/LizTerm.App/Files/LocalFileNames.cs`:

```csharp
using LizTerm.Core.Session;

namespace LizTerm.App.Files;

/// <summary>Suggests a local file name for a received host file: the member name or last qualifier of a TSO
/// dataset, FN.FT for a VM file, the name as typed for CICS. Case is preserved.</summary>
public static class LocalFileNames
{
    public const string Fallback = "received";

    public static string Suggest(string hostFile, TransferHostType hostType)
    {
        var name = hostFile.Trim().Trim('\'').Trim();
        if (name.Length == 0) return Fallback;
        switch (hostType)
        {
            case TransferHostType.Tso:
                var open = name.IndexOf('(');
                if (open >= 0)
                {
                    var close = name.IndexOf(')', open + 1);
                    var member = (close > open ? name[(open + 1)..close] : name[(open + 1)..]).Trim();
                    return member.Length == 0 ? Fallback : member;
                }
                var qualifier = name[(name.LastIndexOf('.') + 1)..].Trim();
                return qualifier.Length == 0 ? Fallback : qualifier;
            case TransferHostType.Vm:
                var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                return parts.Length >= 2 ? parts[0] + "." + parts[1] : parts[0];
            default:
                return name;
        }
    }
}
```

`tests/LizTerm.App.Tests/Fakes/FakeFilePicker.cs`:

```csharp
using LizTerm.App.Files;

namespace LizTerm.App.Tests.Fakes;

public sealed class FakeFilePicker : IFilePicker
{
    /// <summary>What either dialog returns; null plays a cancelled dialog.</summary>
    public string? Result { get; set; }

    /// <summary>When set, both operations fail with this exception.</summary>
    public Exception? Exception { get; set; }

    /// <summary>"open" for the send dialog, "save:&lt;suggested name&gt;" for the receive dialog.</summary>
    public List<string> Calls { get; } = [];

    public Task<string?> PickFileToSendAsync()
    {
        Calls.Add("open");
        return Exception is not null ? Task.FromException<string?>(Exception) : Task.FromResult(Result);
    }

    public Task<string?> PickSaveLocationAsync(string suggestedFileName)
    {
        Calls.Add("save:" + suggestedFileName);
        return Exception is not null ? Task.FromException<string?>(Exception) : Task.FromResult(Result);
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~LocalFileNamesTests"`
Expected: 14 passed.

- [ ] **Step 5: Run the full suite and commit**

Run: `dotnet test LizTerm.slnx`
Expected: everything green.

```bash
git add src/LizTerm.App/Files tests/LizTerm.App.Tests/Fakes/FakeFilePicker.cs tests/LizTerm.App.Tests/Files/LocalFileNamesTests.cs
git commit -m "Add the IFilePicker seam, its Avalonia and fake implementations, and local name suggestions

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 6: FileTransferViewModel: the form, its derived flags, Browse, and validation

**Files:**
- Create: `src/LizTerm.App/ViewModels/FileTransferViewModel.cs`
- Test: `tests/LizTerm.App.Tests/ViewModels/FileTransferViewModelTests.cs`

**Interfaces:**
- Consumes: Task 1 types; `IFilePicker`, `LocalFileNames`, `FakeFilePicker` (Task 5); `FakeEmulatorSession` (Task 3).
- Produces: `partial class FileTransferViewModel : ObservableObject` constructed as `(IEmulatorSession session, IFilePicker picker, Action<Action> dispatch, FileTransferRequest? initial)`; form properties `IsSend`, `IsReceive`, `LocalPath`, `HostFile`, `HostType`, `HostTypes`, `IsText`, `IsBinary`, `CrLf`, `Remap`, `Append`, `RecordFormat`, `RecordFormats`, `LreclText`, `BlksizeText`, `AllocationUnits`, `AllocationUnitsList`, `PrimarySpaceText`, `SecondarySpaceText`, `AverageBlockText`, `BufferSizeText`, `ExtraOptions`, `ValidationMessage`; derived `IsTso`, `ShowAdvanced`, `HasRecordFormat`, `HasAllocation`, `IsAvBlock`, `CanSetBlksize`, `CanSetSpace`, `CanSetAverageBlock`; `BrowseCommand`; `FileTransferRequest? TryBuildRequest()`. Task 7 adds the phases to the same class; Tasks 8 to 10 use it.

- [ ] **Step 1: Write the failing tests**

`tests/LizTerm.App.Tests/ViewModels/FileTransferViewModelTests.cs`:

```csharp
using LizTerm.App.Tests.Fakes;
using LizTerm.App.ViewModels;
using LizTerm.Core.Session;

namespace LizTerm.App.Tests.ViewModels;

public class FileTransferViewModelTests
{
    private static (FileTransferViewModel Vm, FakeEmulatorSession Session, FakeFilePicker Picker) Create(FileTransferRequest? initial = null)
    {
        var session = new FakeEmulatorSession();
        var picker = new FakeFilePicker();
        return (new FileTransferViewModel(session, picker, action => action(), initial), session, picker);
    }

    [Fact]
    public void Defaults_are_send_tso_text_with_crlf_and_remap()
    {
        var (vm, _, _) = Create();
        Assert.True(vm.IsSend);
        Assert.False(vm.IsReceive);
        Assert.Equal(TransferHostType.Tso, vm.HostType);
        Assert.True(vm.IsTso);
        Assert.True(vm.IsText);
        Assert.False(vm.IsBinary);
        Assert.True(vm.CrLf);
        Assert.True(vm.Remap);
        Assert.False(vm.Append);
        Assert.Equal(RecordFormat.Default, vm.RecordFormat);
        Assert.False(vm.HasRecordFormat);
        Assert.Equal(AllocationUnits.Default, vm.AllocationUnits);
        Assert.False(vm.HasAllocation);
        Assert.False(vm.IsAvBlock);
        Assert.True(vm.ShowAdvanced);
        Assert.Equal("", vm.LocalPath);
        Assert.Equal("", vm.HostFile);
        Assert.Equal("", vm.LreclText);
        Assert.Equal("", vm.ExtraOptions);
        Assert.Null(vm.ValidationMessage);
        Assert.Equal([TransferHostType.Tso, TransferHostType.Vm, TransferHostType.Cics], vm.HostTypes);
        Assert.Equal([RecordFormat.Default, RecordFormat.Fixed, RecordFormat.Variable, RecordFormat.Undefined], vm.RecordFormats);
        Assert.Equal([AllocationUnits.Default, AllocationUnits.Tracks, AllocationUnits.Cylinders, AllocationUnits.AvBlock], vm.AllocationUnitsList);
    }

    [Fact]
    public void An_initial_request_fills_every_field_and_round_trips()
    {
        var initial = new FileTransferRequest
        {
            Direction = TransferDirection.Receive, LocalPath = "/tmp/x", HostFile = "A.B", HostType = TransferHostType.Vm,
            Mode = TransferMode.Binary, CrLf = false, Remap = false, Append = true, RecordFormat = RecordFormat.Variable,
            Lrecl = 255, Blksize = 3120, AllocationUnits = AllocationUnits.AvBlock, PrimarySpace = 5, SecondarySpace = 1,
            AverageBlock = 4096, BufferSize = 8192, ExtraOptions = "NOTRUNC",
        };
        var (vm, _, _) = Create(initial);
        Assert.False(vm.IsSend);
        Assert.True(vm.IsReceive);
        Assert.Equal("/tmp/x", vm.LocalPath);
        Assert.Equal("A.B", vm.HostFile);
        Assert.Equal(TransferHostType.Vm, vm.HostType);
        Assert.False(vm.IsText);
        Assert.False(vm.CrLf);
        Assert.False(vm.Remap);
        Assert.True(vm.Append);
        Assert.Equal(RecordFormat.Variable, vm.RecordFormat);
        Assert.Equal("255", vm.LreclText);
        Assert.Equal("3120", vm.BlksizeText);
        Assert.Equal(AllocationUnits.AvBlock, vm.AllocationUnits);
        Assert.Equal("5", vm.PrimarySpaceText);
        Assert.Equal("1", vm.SecondarySpaceText);
        Assert.Equal("4096", vm.AverageBlockText);
        Assert.Equal("8192", vm.BufferSizeText);
        Assert.Equal("NOTRUNC", vm.ExtraOptions);
        Assert.Equal(initial, vm.TryBuildRequest());
    }

    [Fact]
    public void Derived_flags_follow_the_form()
    {
        var (vm, _, _) = Create();
        vm.IsReceive = true;
        Assert.False(vm.IsSend);
        Assert.False(vm.ShowAdvanced);
        vm.IsSend = true;
        Assert.True(vm.ShowAdvanced);

        vm.IsBinary = true;
        Assert.False(vm.IsText);
        vm.IsText = true;
        Assert.False(vm.IsBinary);

        vm.RecordFormat = RecordFormat.Fixed;
        Assert.True(vm.HasRecordFormat);
        Assert.True(vm.CanSetBlksize);
        vm.AllocationUnits = AllocationUnits.AvBlock;
        Assert.True(vm.HasAllocation);
        Assert.True(vm.IsAvBlock);
        Assert.True(vm.CanSetSpace);
        Assert.True(vm.CanSetAverageBlock);

        vm.HostType = TransferHostType.Vm;
        Assert.False(vm.IsTso);
        Assert.False(vm.CanSetBlksize);
        Assert.False(vm.CanSetSpace);
        Assert.False(vm.CanSetAverageBlock);
        Assert.Equal([RecordFormat.Default, RecordFormat.Fixed, RecordFormat.Variable], vm.RecordFormats);
    }

    [Fact]
    public void Switching_to_vm_drops_an_undefined_record_format()
    {
        var (vm, _, _) = Create();
        vm.RecordFormat = RecordFormat.Undefined;
        vm.HostType = TransferHostType.Vm;
        Assert.Equal(RecordFormat.Default, vm.RecordFormat);
        vm.HostType = TransferHostType.Cics;
        vm.RecordFormat = RecordFormat.Undefined;
        Assert.Equal(RecordFormat.Undefined, vm.RecordFormat);
    }

    [Fact]
    public async Task Browse_opens_for_send_and_saves_with_a_suggested_name_for_receive()
    {
        var (vm, _, picker) = Create();
        picker.Result = "/tmp/job.jcl";
        await vm.BrowseCommand.ExecuteAsync(null);
        Assert.Equal(["open"], picker.Calls);
        Assert.Equal("/tmp/job.jcl", vm.LocalPath);

        vm.IsReceive = true;
        vm.HostFile = "'MVSCE02.LIZTERM.JCL(JOB1)'";
        picker.Result = null;
        await vm.BrowseCommand.ExecuteAsync(null);
        Assert.Equal(["open", "save:JOB1"], picker.Calls);
        Assert.Equal("/tmp/job.jcl", vm.LocalPath);
        Assert.Null(vm.ValidationMessage);
    }

    [Fact]
    public async Task Browse_failure_shows_a_message()
    {
        var (vm, _, picker) = Create();
        picker.Exception = new InvalidOperationException("no display");
        await vm.BrowseCommand.ExecuteAsync(null);
        Assert.Equal("Could not open the file dialog: no display", vm.ValidationMessage);
    }

    [Fact]
    public void Validation_reports_blank_fields_then_bad_numbers_then_core_rules()
    {
        var (vm, _, _) = Create();
        Assert.Null(vm.TryBuildRequest());
        Assert.Equal("Choose a local file.", vm.ValidationMessage);

        vm.LocalPath = "/tmp/a";
        Assert.Null(vm.TryBuildRequest());
        Assert.Equal("Enter the host file name.", vm.ValidationMessage);

        vm.HostFile = "A.B";
        vm.LreclText = "eighty";
        Assert.Null(vm.TryBuildRequest());
        Assert.Equal("LRECL must be a whole number.", vm.ValidationMessage);

        vm.LreclText = "80";
        vm.BufferSizeText = "1.5";
        Assert.Null(vm.TryBuildRequest());
        Assert.Equal("Buffer size must be a whole number.", vm.ValidationMessage);

        vm.BufferSizeText = "";
        vm.AllocationUnits = AllocationUnits.Tracks;
        Assert.Null(vm.TryBuildRequest());
        Assert.Equal("Primary space is required when allocation units are set.", vm.ValidationMessage);

        vm.PrimarySpaceText = " 5 ";
        vm.ExtraOptions = "  ";
        var request = vm.TryBuildRequest();
        Assert.NotNull(request);
        Assert.Null(vm.ValidationMessage);
        Assert.Equal(5, request!.PrimarySpace);
        Assert.Equal(80, request.Lrecl);
        Assert.Null(request.Blksize);
        Assert.Null(request.BufferSize);
        Assert.Null(request.ExtraOptions);
        Assert.Equal(TransferDirection.Send, request.Direction);
    }

    [Fact]
    public void Names_are_trimmed_into_the_request()
    {
        var (vm, _, _) = Create();
        vm.LocalPath = " /tmp/a ";
        vm.HostFile = " A.B ";
        var request = vm.TryBuildRequest();
        Assert.Equal("/tmp/a", request!.LocalPath);
        Assert.Equal("A.B", request.HostFile);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~FileTransferViewModelTests"`
Expected: build error, `FileTransferViewModel` does not exist.

- [ ] **Step 3: Write the view model's form half**

`src/LizTerm.App/ViewModels/FileTransferViewModel.cs`:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LizTerm.App.Files;
using LizTerm.Core.Session;

namespace LizTerm.App.ViewModels;

/// <summary>One File Transfer dialog: a form that becomes a progress view and then a result view, with the form
/// kept filled behind them. Owns the transfer call; <see cref="SessionViewModel"/> only remembers the last request.
/// Numeric fields are text: blank means unset, so the form can carry values the current direction or host type
/// does not use.</summary>
public partial class FileTransferViewModel : ObservableObject
{
    private readonly IEmulatorSession _session;
    private readonly IFilePicker _picker;
    private readonly Action<Action> _dispatch;

    /// <summary>Raised with the request each time Start passes validation, before the transfer begins.</summary>
    public event Action<FileTransferRequest>? Started;

    /// <param name="dispatch">Marshals a callback onto the UI thread. Tests pass <c>a => a()</c>.</param>
    /// <param name="initial">The last request started from this session window, or null for the defaults.</param>
    public FileTransferViewModel(IEmulatorSession session, IFilePicker picker, Action<Action> dispatch, FileTransferRequest? initial)
    {
        _session = session;
        _picker = picker;
        _dispatch = dispatch;
        if (initial is null) return;
        _isSend = initial.Direction == TransferDirection.Send;
        _localPath = initial.LocalPath;
        _hostFile = initial.HostFile;
        _hostType = initial.HostType;
        _isText = initial.Mode == TransferMode.Text;
        _crLf = initial.CrLf;
        _remap = initial.Remap;
        _append = initial.Append;
        _recordFormat = initial.RecordFormat;
        _lreclText = Text(initial.Lrecl);
        _blksizeText = Text(initial.Blksize);
        _allocationUnits = initial.AllocationUnits;
        _primarySpaceText = Text(initial.PrimarySpace);
        _secondarySpaceText = Text(initial.SecondarySpace);
        _averageBlockText = Text(initial.AverageBlock);
        _bufferSizeText = Text(initial.BufferSize);
        _extraOptions = initial.ExtraOptions ?? "";
    }

    private static string Text(int? value) => value?.ToString() ?? "";

    // ---- form ----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReceive), nameof(ShowAdvanced))]
    private bool _isSend = true;

    /// <summary>The other half of the direction radio pair; settable so both buttons can bind two-way.</summary>
    public bool IsReceive
    {
        get => !IsSend;
        set => IsSend = !value;
    }

    [ObservableProperty] private string _localPath = "";
    [ObservableProperty] private string _hostFile = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTso), nameof(RecordFormats), nameof(CanSetBlksize), nameof(CanSetSpace), nameof(CanSetAverageBlock))]
    private TransferHostType _hostType = TransferHostType.Tso;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBinary))]
    private bool _isText = true;

    /// <summary>The other half of the mode radio pair.</summary>
    public bool IsBinary
    {
        get => !IsText;
        set => IsText = !value;
    }

    [ObservableProperty] private bool _crLf = true;
    [ObservableProperty] private bool _remap = true;
    [ObservableProperty] private bool _append;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRecordFormat), nameof(CanSetBlksize))]
    private RecordFormat _recordFormat = RecordFormat.Default;

    [ObservableProperty] private string _lreclText = "";
    [ObservableProperty] private string _blksizeText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAllocation), nameof(IsAvBlock), nameof(CanSetSpace), nameof(CanSetAverageBlock))]
    private AllocationUnits _allocationUnits = AllocationUnits.Default;

    [ObservableProperty] private string _primarySpaceText = "";
    [ObservableProperty] private string _secondarySpaceText = "";
    [ObservableProperty] private string _averageBlockText = "";
    [ObservableProperty] private string _bufferSizeText = "";
    [ObservableProperty] private string _extraOptions = "";
    [ObservableProperty] private string? _validationMessage;

    public bool IsTso => HostType == TransferHostType.Tso;
    /// <summary>The Advanced expander describes the host file a send creates, so it hides on receive.</summary>
    public bool ShowAdvanced => IsSend;
    public bool HasRecordFormat => RecordFormat != RecordFormat.Default;
    public bool HasAllocation => AllocationUnits != AllocationUnits.Default;
    public bool IsAvBlock => AllocationUnits == AllocationUnits.AvBlock;
    /// <summary>b3270 emits LRECL and BLKSIZE only with a RECFM, and BLKSIZE and SPACE only for TSO.</summary>
    public bool CanSetBlksize => HasRecordFormat && IsTso;
    public bool CanSetSpace => HasAllocation && IsTso;
    public bool CanSetAverageBlock => IsAvBlock && IsTso;

    public TransferHostType[] HostTypes { get; } = [TransferHostType.Tso, TransferHostType.Vm, TransferHostType.Cics];
    /// <summary>VM has no undefined-length records, so the list shrinks for it.</summary>
    public RecordFormat[] RecordFormats => HostType == TransferHostType.Vm
        ? [RecordFormat.Default, RecordFormat.Fixed, RecordFormat.Variable]
        : [RecordFormat.Default, RecordFormat.Fixed, RecordFormat.Variable, RecordFormat.Undefined];
    public AllocationUnits[] AllocationUnitsList { get; } = [AllocationUnits.Default, AllocationUnits.Tracks, AllocationUnits.Cylinders, AllocationUnits.AvBlock];

    partial void OnHostTypeChanged(TransferHostType value)
    {
        if (value == TransferHostType.Vm && RecordFormat == RecordFormat.Undefined) RecordFormat = RecordFormat.Default;
    }

    [RelayCommand]
    private async Task BrowseAsync()
    {
        string? path;
        try
        {
            path = IsSend
                ? await _picker.PickFileToSendAsync()
                : await _picker.PickSaveLocationAsync(LocalFileNames.Suggest(HostFile, HostType));
        }
        catch (Exception ex)
        {
            ValidationMessage = "Could not open the file dialog: " + ex.Message;
            return;
        }
        if (path is not null) LocalPath = path;
    }

    /// <summary>The request the form describes, or null with <see cref="ValidationMessage"/> set. Non-integer text
    /// is reported before the Core rules run.</summary>
    public FileTransferRequest? TryBuildRequest()
    {
        if (!TryNumber(LreclText, "LRECL", out var lrecl)
            || !TryNumber(BlksizeText, "BLKSIZE", out var blksize)
            || !TryNumber(PrimarySpaceText, "Primary space", out var primary)
            || !TryNumber(SecondarySpaceText, "Secondary space", out var secondary)
            || !TryNumber(AverageBlockText, "Average block size", out var average)
            || !TryNumber(BufferSizeText, "Buffer size", out var buffer))
            return null;

        var request = new FileTransferRequest
        {
            Direction = IsSend ? TransferDirection.Send : TransferDirection.Receive,
            LocalPath = LocalPath.Trim(),
            HostFile = HostFile.Trim(),
            HostType = HostType,
            Mode = IsText ? TransferMode.Text : TransferMode.Binary,
            CrLf = CrLf,
            Remap = Remap,
            Append = Append,
            RecordFormat = RecordFormat,
            Lrecl = lrecl,
            Blksize = blksize,
            AllocationUnits = AllocationUnits,
            PrimarySpace = primary,
            SecondarySpace = secondary,
            AverageBlock = average,
            BufferSize = buffer,
            ExtraOptions = string.IsNullOrWhiteSpace(ExtraOptions) ? null : ExtraOptions.Trim(),
        };
        ValidationMessage = request.Validate();
        return ValidationMessage is null ? request : null;
    }

    private bool TryNumber(string text, string field, out int? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(text)) return true;
        if (int.TryParse(text.Trim(), out var number))
        {
            value = number;
            return true;
        }
        ValidationMessage = field + " must be a whole number.";
        return false;
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~FileTransferViewModelTests"`
Expected: 8 passed. The `_session` and `_dispatch` fields are assigned but unused until Task 7, which is a compiler warning, not an error (`Directory.Build.props` does not set `TreatWarningsAsErrors`). Do not add dummy uses to silence it.

- [ ] **Step 5: Run the full suite and commit**

Run: `dotnet test LizTerm.slnx`
Expected: everything green.

```bash
git add src/LizTerm.App/ViewModels/FileTransferViewModel.cs tests/LizTerm.App.Tests/ViewModels/FileTransferViewModelTests.cs
git commit -m "Add the File Transfer dialog view model: form, derived flags, Browse, and validation

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 7: FileTransferViewModel: phases, Start, progress, Cancel, Back

**Files:**
- Modify: `src/LizTerm.App/ViewModels/FileTransferViewModel.cs`
- Modify: `tests/LizTerm.App.Tests/ViewModels/FileTransferViewModelTests.cs`

**Interfaces:**
- Consumes: `IEmulatorSession.TransferAsync` (Task 3), the fake's `TransferCompletion`, `TransferProgress`, `TransferToken`, `TransferResult`, `TransferException`.
- Produces: `enum TransferPhase { Form, Running, Done }`; on the view model `Phase`, `IsForm`, `IsRunning`, `IsDone`, `StatusText`, `BytesTransferred`, `TotalBytes`, `IsProgressIndeterminate`, `ProgressValue`, `ProgressMaximum`, `IsCancelling`, `ResultMessage`, `Succeeded`, `Failed`, and commands `StartCommand` (async), `CancelTransferCommand`, `BackCommand`. Tasks 8 to 10 use them.

- [ ] **Step 1: Write the failing tests**

Append inside the class in `tests/LizTerm.App.Tests/ViewModels/FileTransferViewModelTests.cs`:

```csharp
    private static TaskCompletionSource Pending() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    [Fact]
    public async Task Start_records_the_request_runs_shows_progress_and_lands_in_done()
    {
        var (vm, session, _) = Create();
        vm.LocalPath = "/nonexistent/a.txt";
        vm.HostFile = "A.B";
        FileTransferRequest? started = null;
        vm.Started += r => started = r;
        session.TransferCompletion = Pending();

        var run = vm.StartCommand.ExecuteAsync(null);

        Assert.Equal(TransferPhase.Running, vm.Phase);
        Assert.True(vm.IsRunning);
        Assert.False(vm.IsForm);
        Assert.Equal("Waiting for the host...", vm.StatusText);
        Assert.Equal("A.B", started?.HostFile);
        Assert.Equal(["transfer:Send:A.B"], session.Calls);
        Assert.False(vm.StartCommand.CanExecute(null));
        Assert.True(vm.CancelTransferCommand.CanExecute(null));
        Assert.False(vm.BackCommand.CanExecute(null));
        Assert.True(vm.IsProgressIndeterminate);

        session.TransferProgress!.Report(2048);
        Assert.Equal("2,048 bytes", vm.StatusText);
        Assert.Equal(2048, vm.BytesTransferred);
        Assert.Equal(2048, vm.ProgressValue);

        session.TransferResult = new FileTransferResult(true, "Transfer complete, 2048 bytes transferred", 2048);
        session.TransferCompletion.SetResult();
        await run;

        Assert.Equal(TransferPhase.Done, vm.Phase);
        Assert.True(vm.IsDone);
        Assert.True(vm.Succeeded);
        Assert.False(vm.Failed);
        Assert.Equal("Transfer complete, 2048 bytes transferred", vm.ResultMessage);
        Assert.True(vm.BackCommand.CanExecute(null));
        Assert.False(vm.CancelTransferCommand.CanExecute(null));
    }

    [Fact]
    public async Task Sending_a_real_file_shows_a_determinate_bar()
    {
        var path = Path.GetTempFileName();
        await File.WriteAllTextAsync(path, "hello world", TestContext.Current.CancellationToken);
        try
        {
            var (vm, session, _) = Create();
            vm.LocalPath = path;
            vm.HostFile = "A.B";
            session.TransferCompletion = Pending();
            var run = vm.StartCommand.ExecuteAsync(null);
            Assert.Equal(11, vm.TotalBytes);
            Assert.False(vm.IsProgressIndeterminate);
            Assert.Equal(11, vm.ProgressMaximum);
            session.TransferCompletion.SetResult();
            await run;
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Receiving_shows_an_indeterminate_bar()
    {
        var (vm, session, _) = Create();
        vm.IsReceive = true;
        vm.LocalPath = "/tmp/out";
        vm.HostFile = "A.B";
        session.TransferCompletion = Pending();
        var run = vm.StartCommand.ExecuteAsync(null);
        Assert.Null(vm.TotalBytes);
        Assert.True(vm.IsProgressIndeterminate);
        Assert.Equal(1, vm.ProgressMaximum);
        Assert.Equal(["transfer:Receive:A.B"], session.Calls);
        session.TransferCompletion.SetResult();
        await run;
    }

    [Fact]
    public async Task Cancel_marks_cancelling_cancels_the_token_and_lands_in_done()
    {
        var (vm, session, _) = Create();
        vm.LocalPath = "/nonexistent/a.txt";
        vm.HostFile = "A.B";
        session.TransferCompletion = Pending();
        var run = vm.StartCommand.ExecuteAsync(null);

        vm.CancelTransferCommand.Execute(null);

        Assert.True(vm.IsCancelling);
        Assert.Equal("Cancelling...", vm.StatusText);
        Assert.True(session.TransferToken.IsCancellationRequested);
        Assert.False(vm.CancelTransferCommand.CanExecute(null));
        session.TransferProgress!.Report(4096);
        Assert.Equal("Cancelling...", vm.StatusText);
        Assert.Equal(4096, vm.BytesTransferred);

        session.TransferException = new OperationCanceledException();
        session.TransferCompletion.SetResult();
        await run;

        Assert.True(vm.IsDone);
        Assert.False(vm.Succeeded);
        Assert.Equal("Transfer cancelled.", vm.ResultMessage);
    }

    [Fact]
    public async Task A_failed_result_and_an_exception_both_land_in_done_as_failures_and_back_keeps_the_form()
    {
        var (vm, session, _) = Create();
        vm.LocalPath = "/nonexistent/a.txt";
        vm.HostFile = "A.B";
        session.TransferResult = new FileTransferResult(false, "TRANS17 Miscellaneous I/O error", 0);

        await vm.StartCommand.ExecuteAsync(null);
        Assert.True(vm.IsDone);
        Assert.False(vm.Succeeded);
        Assert.True(vm.Failed);
        Assert.Equal("TRANS17 Miscellaneous I/O error", vm.ResultMessage);

        vm.BackCommand.Execute(null);
        Assert.True(vm.IsForm);
        Assert.Equal("/nonexistent/a.txt", vm.LocalPath);
        Assert.Equal("A.B", vm.HostFile);
        Assert.True(vm.StartCommand.CanExecute(null));

        session.TransferException = new BackendUnavailableException("The emulator engine (b3270) exited unexpectedly.");
        await vm.StartCommand.ExecuteAsync(null);
        Assert.True(vm.IsDone);
        Assert.False(vm.Succeeded);
        Assert.Equal("The emulator engine (b3270) exited unexpectedly.", vm.ResultMessage);
        Assert.Equal(2, session.Calls.Count);
    }

    [Fact]
    public async Task Start_with_an_invalid_form_stays_in_form_and_calls_nothing()
    {
        var (vm, session, _) = Create();
        var started = 0;
        vm.Started += _ => started++;
        await vm.StartCommand.ExecuteAsync(null);
        Assert.True(vm.IsForm);
        Assert.Equal("Choose a local file.", vm.ValidationMessage);
        Assert.Empty(session.Calls);
        Assert.Equal(0, started);
    }

    [Fact]
    public async Task Progress_reports_are_marshalled_through_the_dispatcher()
    {
        var session = new FakeEmulatorSession();
        var dispatched = new List<Action>();
        var vm = new FileTransferViewModel(session, new FakeFilePicker(), dispatched.Add, null) { LocalPath = "/nonexistent/a.txt", HostFile = "A.B" };
        session.TransferCompletion = Pending();
        var run = vm.StartCommand.ExecuteAsync(null);

        session.TransferProgress!.Report(10);
        Assert.Equal("Waiting for the host...", vm.StatusText);
        Assert.Single(dispatched);
        dispatched[0]();
        Assert.Equal("10 bytes", vm.StatusText);

        session.TransferCompletion.SetResult();
        await run;
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~FileTransferViewModelTests"`
Expected: build error, `TransferPhase` and `StartCommand` do not exist.

- [ ] **Step 3: Add the phases and commands**

In `src/LizTerm.App/ViewModels/FileTransferViewModel.cs`, add `using System.Globalization;` at the top, add the enum before the class:

```csharp
public enum TransferPhase { Form, Running, Done }
```

add a field next to the other fields:

```csharp
    private CancellationTokenSource? _cts;
```

and add this section after `TryNumber`, before the class's closing brace:

```csharp

    // ---- phase ----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsForm), nameof(IsRunning), nameof(IsDone))]
    [NotifyCanExecuteChangedFor(nameof(StartCommand), nameof(CancelTransferCommand), nameof(BackCommand))]
    private TransferPhase _phase = TransferPhase.Form;

    public bool IsForm => Phase == TransferPhase.Form;
    public bool IsRunning => Phase == TransferPhase.Running;
    public bool IsDone => Phase == TransferPhase.Done;

    // ---- running ----

    [ObservableProperty] private string _statusText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressValue))]
    private long _bytesTransferred;

    /// <summary>The local file's length when sending; null when receiving or when it cannot be read.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsProgressIndeterminate), nameof(ProgressMaximum))]
    private long? _totalBytes;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CancelTransferCommand))]
    private bool _isCancelling;

    public bool IsProgressIndeterminate => TotalBytes is null;
    public double ProgressValue => BytesTransferred;
    public double ProgressMaximum => TotalBytes ?? 1;

    // ---- done ----

    [ObservableProperty] private string _resultMessage = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Failed))]
    private bool _succeeded;

    public bool Failed => !Succeeded;

    [RelayCommand(CanExecute = nameof(IsForm))]
    private async Task StartAsync()
    {
        if (!IsForm) return;
        if (TryBuildRequest() is not { } request) return;
        Started?.Invoke(request);

        TotalBytes = request.Direction == TransferDirection.Send ? TryFileLength(request.LocalPath) : null;
        BytesTransferred = 0;
        StatusText = "Waiting for the host...";
        IsCancelling = false;
        Phase = TransferPhase.Running;

        var cts = new CancellationTokenSource();
        _cts = cts;
        try
        {
            var result = await _session.TransferAsync(request, new DispatchedProgress(this), cts.Token);
            Finish(result.Succeeded, result.Message);
        }
        catch (OperationCanceledException)
        {
            Finish(false, "Transfer cancelled.");
        }
        catch (Exception ex)
        {
            Finish(false, ex.Message);
        }
        finally
        {
            _cts = null;
            cts.Dispose();
        }
    }

    private void Finish(bool succeeded, string message)
    {
        Succeeded = succeeded;
        ResultMessage = message;
        Phase = TransferPhase.Done;
    }

    private static long? TryFileLength(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private bool CanCancel => IsRunning && !IsCancelling;

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void CancelTransfer()
    {
        if (!CanCancel) return;
        IsCancelling = true;
        StatusText = "Cancelling...";
        _cts?.Cancel();
    }

    [RelayCommand(CanExecute = nameof(IsDone))]
    private void Back()
    {
        if (IsDone) Phase = TransferPhase.Form;
    }

    private void OnProgress(long bytes)
    {
        if (!IsRunning) return;
        BytesTransferred = bytes;
        if (!IsCancelling) StatusText = bytes.ToString("N0", CultureInfo.InvariantCulture) + " bytes";
    }

    /// <summary>The backend reports on its reader thread; every report goes through the dispatch delegate.</summary>
    private sealed class DispatchedProgress(FileTransferViewModel owner) : IProgress<long>
    {
        public void Report(long value) => owner._dispatch(() => owner.OnProgress(value));
    }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~FileTransferViewModelTests"`
Expected: 15 passed.

- [ ] **Step 5: Run the full suite and commit**

Run: `dotnet test LizTerm.slnx`
Expected: everything green.

```bash
git add src/LizTerm.App/ViewModels/FileTransferViewModel.cs tests/LizTerm.App.Tests/ViewModels/FileTransferViewModelTests.cs
git commit -m "Run, show progress for, cancel, and finish a transfer in the File Transfer view model

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 8: Session-lifetime memory and the dialog factory on SessionViewModel

**Files:**
- Modify: `src/LizTerm.App/ViewModels/SessionViewModel.cs`
- Test: `tests/LizTerm.App.Tests/ViewModels/SessionViewModelTransferTests.cs`

**Interfaces:**
- Consumes: `FileTransferViewModel` (Tasks 6 and 7), `IFilePicker` (Task 5).
- Produces: `public FileTransferRequest? LastTransferRequest { get; private set; }` and `public FileTransferViewModel CreateTransfer(IFilePicker picker)` on `SessionViewModel`. Task 10 uses `CreateTransfer`.

- [ ] **Step 1: Write the failing tests**

`tests/LizTerm.App.Tests/ViewModels/SessionViewModelTransferTests.cs`:

```csharp
using LizTerm.App.Tests.Fakes;
using LizTerm.App.ViewModels;
using LizTerm.Core.Session;

namespace LizTerm.App.Tests.ViewModels;

public class SessionViewModelTransferTests
{
    private static (SessionViewModel Vm, FakeEmulatorSession Session) Create()
    {
        var session = new FakeEmulatorSession();
        return (new SessionViewModel(session, action => action(), new FakeTextClipboard()), session);
    }

    [Fact]
    public async Task The_next_dialog_opens_as_the_last_one_was_left()
    {
        var (vm, _) = Create();
        var picker = new FakeFilePicker();
        var first = vm.CreateTransfer(picker);
        Assert.Null(vm.LastTransferRequest);
        Assert.True(first.IsSend);

        first.LocalPath = "/tmp/job.jcl";
        first.HostFile = "LIZTERM.JCL(JOB1)";
        first.IsBinary = true;
        first.RecordFormat = RecordFormat.Fixed;
        first.LreclText = "80";
        await first.StartCommand.ExecuteAsync(null);

        Assert.Equal("LIZTERM.JCL(JOB1)", vm.LastTransferRequest?.HostFile);
        var second = vm.CreateTransfer(picker);
        Assert.Equal("/tmp/job.jcl", second.LocalPath);
        Assert.Equal("LIZTERM.JCL(JOB1)", second.HostFile);
        Assert.False(second.IsText);
        Assert.Equal(RecordFormat.Fixed, second.RecordFormat);
        Assert.Equal("80", second.LreclText);
        Assert.True(second.IsForm);
    }

    [Fact]
    public async Task A_dialog_that_never_starts_leaves_the_memory_alone()
    {
        var (vm, _) = Create();
        var first = vm.CreateTransfer(new FakeFilePicker());
        first.LocalPath = "/tmp/a";
        first.HostFile = "A.B";
        await first.StartCommand.ExecuteAsync(null);

        var second = vm.CreateTransfer(new FakeFilePicker());
        second.HostFile = "CHANGED";
        var third = vm.CreateTransfer(new FakeFilePicker());
        Assert.Equal("A.B", third.HostFile);
    }

    [Fact]
    public async Task The_dialog_uses_the_given_picker_and_this_session()
    {
        var (vm, session) = Create();
        var picker = new FakeFilePicker { Result = "/tmp/x" };
        var transfer = vm.CreateTransfer(picker);
        await transfer.BrowseCommand.ExecuteAsync(null);
        Assert.Equal("/tmp/x", transfer.LocalPath);
        transfer.HostFile = "A.B";
        await transfer.StartCommand.ExecuteAsync(null);
        Assert.Contains("transfer:Send:A.B", session.Calls);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~SessionViewModelTransferTests"`
Expected: build error, `CreateTransfer` does not exist.

- [ ] **Step 3: Add the memory and the factory**

In `src/LizTerm.App/ViewModels/SessionViewModel.cs`, add `using LizTerm.App.Files;` and, after the `Title` property, add:

```csharp
    /// <summary>The last request a File Transfer dialog started from this window, so the next dialog opens as the
    /// user left it. Lives as long as the window; nothing is saved to the profile.</summary>
    public FileTransferRequest? LastTransferRequest { get; private set; }

    /// <summary>Builds the File Transfer dialog's view model around this session, pre-filled from
    /// <see cref="LastTransferRequest"/>. The window passes a picker over the dialog; tests pass a fake.</summary>
    public FileTransferViewModel CreateTransfer(IFilePicker picker)
    {
        var transfer = new FileTransferViewModel(_session, picker, _dispatch, LastTransferRequest);
        transfer.Started += request => LastTransferRequest = request;
        return transfer;
    }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~SessionViewModelTransferTests"`
Expected: 3 passed.

- [ ] **Step 5: Run the full suite and commit**

Run: `dotnet test LizTerm.slnx`
Expected: everything green.

```bash
git add src/LizTerm.App/ViewModels/SessionViewModel.cs tests/LizTerm.App.Tests/ViewModels/SessionViewModelTransferTests.cs
git commit -m "Remember the last transfer request per session window and build the dialog view model from it

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 9: FileTransferWindow with its three panels

**Files:**
- Create: `src/LizTerm.App/Views/TransferLabels.cs`, `src/LizTerm.App/Views/FileTransferWindow.axaml`, `src/LizTerm.App/Views/FileTransferWindow.axaml.cs`
- Test: `tests/LizTerm.App.Tests/Views/FileTransferWindowTests.cs`

**Interfaces:**
- Consumes: `FileTransferViewModel` (Tasks 6 and 7), `FakeEmulatorSession`, `FakeFilePicker`.
- Produces: `partial class FileTransferWindow : Window` with a parameterless constructor; the caller sets `DataContext`. Named controls `FormPanel`, `RunningPanel`, `DonePanel` (StackPanels), `StartButton`, `CancelButton`. `sealed class TransferLabels : IValueConverter` with `static readonly TransferLabels Converter`. Task 10 opens the window.

- [ ] **Step 1: Write the failing tests**

`tests/LizTerm.App.Tests/Views/FileTransferWindowTests.cs`:

```csharp
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using LizTerm.App.Tests.Fakes;
using LizTerm.App.ViewModels;
using LizTerm.App.Views;
using LizTerm.Core.Session;

namespace LizTerm.App.Tests.Views;

/// <summary>The dialog on the headless platform: panels follow the phase, Start reaches the view model, and
/// closing a running transfer cancels it instead of closing. The transfer itself is the fake session's.</summary>
public class FileTransferWindowTests
{
    private static (FileTransferWindow Window, FileTransferViewModel Vm, FakeEmulatorSession Session) Show()
    {
        var session = new FakeEmulatorSession();
        var vm = new FileTransferViewModel(session, new FakeFilePicker(), action => action(), null);
        var window = new FileTransferWindow { DataContext = vm };
        window.Show();
        return (window, vm, session);
    }

    private static (StackPanel Form, StackPanel Running, StackPanel Done) Panels(FileTransferWindow window) =>
        (window.FindControl<StackPanel>("FormPanel")!, window.FindControl<StackPanel>("RunningPanel")!, window.FindControl<StackPanel>("DonePanel")!);

    private static TaskCompletionSource Pending() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    [AvaloniaFact]
    public async Task Panels_follow_the_phase()
    {
        var (window, vm, session) = Show();
        var (form, running, done) = Panels(window);
        Assert.True(form.IsVisible);
        Assert.False(running.IsVisible);
        Assert.False(done.IsVisible);

        vm.LocalPath = "/nonexistent/a.txt";
        vm.HostFile = "A.B";
        session.TransferCompletion = Pending();
        var run = vm.StartCommand.ExecuteAsync(null);
        Assert.False(form.IsVisible);
        Assert.True(running.IsVisible);
        Assert.False(done.IsVisible);
        Assert.True(window.FindControl<Button>("CancelButton")!.IsEffectivelyEnabled);

        session.TransferCompletion.SetResult();
        await run;
        Assert.False(running.IsVisible);
        Assert.True(done.IsVisible);

        vm.BackCommand.Execute(null);
        Assert.True(form.IsVisible);
        Assert.False(done.IsVisible);
    }

    [AvaloniaFact]
    public void Start_on_an_empty_form_shows_the_validation_message_and_stays_in_form()
    {
        var (window, vm, session) = Show();
        var start = window.FindControl<Button>("StartButton")!;
        Assert.True(start.IsEffectivelyEnabled);
        start.Command!.Execute(null);
        Assert.Equal("Choose a local file.", vm.ValidationMessage);
        Assert.True(vm.IsForm);
        Assert.Empty(session.Calls);
    }

    [AvaloniaFact]
    public async Task Closing_while_running_cancels_instead_of_closing()
    {
        var (window, vm, session) = Show();
        vm.LocalPath = "/nonexistent/a.txt";
        vm.HostFile = "A.B";
        session.TransferCompletion = Pending();
        var run = vm.StartCommand.ExecuteAsync(null);
        var closed = false;
        window.Closed += (_, _) => closed = true;

        window.Close();

        Assert.False(closed);
        Assert.True(vm.IsCancelling);
        Assert.True(session.TransferToken.IsCancellationRequested);

        session.TransferException = new OperationCanceledException();
        session.TransferCompletion.SetResult();
        await run;
        Assert.True(vm.IsDone);
        Assert.Equal("Transfer cancelled.", vm.ResultMessage);

        window.Close();
        Assert.True(closed);
    }

    [Theory]
    [InlineData(TransferHostType.Tso, "TSO")]
    [InlineData(TransferHostType.Vm, "VM")]
    [InlineData(TransferHostType.Cics, "CICS")]
    [InlineData(AllocationUnits.AvBlock, "AVBLOCK")]
    [InlineData(AllocationUnits.Default, "Default")]
    [InlineData(AllocationUnits.Tracks, "Tracks")]
    [InlineData(RecordFormat.Default, "Default")]
    [InlineData(RecordFormat.Undefined, "Undefined")]
    public void Combo_box_labels(object value, string expected) =>
        Assert.Equal(expected, TransferLabels.Converter.Convert(value, typeof(string), null, CultureInfo.InvariantCulture));
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~FileTransferWindowTests"`
Expected: build error, `FileTransferWindow` and `TransferLabels` do not exist.

- [ ] **Step 3: Write the label converter**

`src/LizTerm.App/Views/TransferLabels.cs`:

```csharp
using System.Globalization;
using Avalonia.Data.Converters;
using LizTerm.Core.Session;

namespace LizTerm.App.Views;

/// <summary>Labels for the transfer enums in the dialog's combo boxes: TSO, VM, CICS, AVBLOCK; every other member
/// shows its own name (Default, Fixed, Tracks, ...).</summary>
public sealed class TransferLabels : IValueConverter
{
    public static readonly TransferLabels Converter = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        TransferHostType.Tso => "TSO",
        TransferHostType.Vm => "VM",
        TransferHostType.Cics => "CICS",
        AllocationUnits.AvBlock => "AVBLOCK",
        null => null,
        _ => value.ToString(),
    };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("Labels are display-only.");
}
```

- [ ] **Step 4: Write the window**

`src/LizTerm.App/Views/FileTransferWindow.axaml`:

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:vm="using:LizTerm.App.ViewModels"
        xmlns:views="using:LizTerm.App.Views"
        x:Class="LizTerm.App.Views.FileTransferWindow"
        x:DataType="vm:FileTransferViewModel"
        Title="File Transfer" Width="560" SizeToContent="Height" CanResize="False"
        WindowStartupLocation="CenterOwner">
  <Panel Margin="16">

    <!-- Form: what to transfer. Stays filled behind the other two panels. -->
    <StackPanel x:Name="FormPanel" Spacing="10" IsVisible="{Binding IsForm}">
      <StackPanel Orientation="Horizontal" Spacing="16">
        <RadioButton Content="Send to host" GroupName="Direction" IsChecked="{Binding IsSend}" />
        <RadioButton Content="Receive from host" GroupName="Direction" IsChecked="{Binding IsReceive}" />
      </StackPanel>
      <Grid ColumnDefinitions="110,*,Auto" RowDefinitions="Auto,Auto,Auto,Auto,Auto">
        <TextBlock Grid.Row="0" Text="Local file" VerticalAlignment="Center" />
        <TextBox Grid.Row="0" Grid.Column="1" Text="{Binding LocalPath}" Margin="0,0,8,0" />
        <Button Grid.Row="0" Grid.Column="2" Content="Browse..." Command="{Binding BrowseCommand}" />
        <TextBlock Grid.Row="1" Text="Host file" VerticalAlignment="Center" />
        <TextBox Grid.Row="1" Grid.Column="1" Grid.ColumnSpan="2" Text="{Binding HostFile}" PlaceholderText="LIZTERM.JCL(JOB1) or 'USER.DATA.SET'" />
        <TextBlock Grid.Row="2" Text="Host type" VerticalAlignment="Center" />
        <ComboBox Grid.Row="2" Grid.Column="1" ItemsSource="{Binding HostTypes}" SelectedItem="{Binding HostType}" Width="120" HorizontalAlignment="Left"
                  DisplayMemberBinding="{ReflectionBinding Converter={x:Static views:TransferLabels.Converter}}" />
        <TextBlock Grid.Row="3" Text="Mode" VerticalAlignment="Center" />
        <StackPanel Grid.Row="3" Grid.Column="1" Grid.ColumnSpan="2" Orientation="Horizontal" Spacing="16">
          <RadioButton Content="Text" GroupName="Mode" IsChecked="{Binding IsText}" />
          <RadioButton Content="Binary" GroupName="Mode" IsChecked="{Binding IsBinary}" />
        </StackPanel>
        <TextBlock Grid.Row="4" Text="Options" VerticalAlignment="Top" Margin="0,6,0,0" />
        <StackPanel Grid.Row="4" Grid.Column="1" Grid.ColumnSpan="2" Spacing="4">
          <CheckBox Content="Convert line endings (CRLF)" IsChecked="{Binding CrLf}" IsEnabled="{Binding IsText}" />
          <CheckBox Content="Remap the character set" IsChecked="{Binding Remap}" IsEnabled="{Binding IsText}" />
          <CheckBox Content="Append to an existing file" IsChecked="{Binding Append}" />
        </StackPanel>
      </Grid>
      <Expander Header="Advanced (host file allocation)" IsVisible="{Binding ShowAdvanced}">
        <Grid ColumnDefinitions="130,*" RowDefinitions="Auto,Auto,Auto,Auto,Auto,Auto,Auto,Auto,Auto" Margin="0,6,0,0">
          <TextBlock Grid.Row="0" Text="Record format" VerticalAlignment="Center" />
          <ComboBox Grid.Row="0" Grid.Column="1" ItemsSource="{Binding RecordFormats}" SelectedItem="{Binding RecordFormat}" Width="130" HorizontalAlignment="Left"
                    DisplayMemberBinding="{ReflectionBinding Converter={x:Static views:TransferLabels.Converter}}" />
          <TextBlock Grid.Row="1" Text="LRECL" VerticalAlignment="Center" />
          <TextBox Grid.Row="1" Grid.Column="1" Text="{Binding LreclText}" IsEnabled="{Binding HasRecordFormat}" Width="100" HorizontalAlignment="Left" />
          <TextBlock Grid.Row="2" Text="BLKSIZE (TSO)" VerticalAlignment="Center" />
          <TextBox Grid.Row="2" Grid.Column="1" Text="{Binding BlksizeText}" IsEnabled="{Binding CanSetBlksize}" Width="100" HorizontalAlignment="Left" />
          <TextBlock Grid.Row="3" Text="Allocation (TSO)" VerticalAlignment="Center" />
          <ComboBox Grid.Row="3" Grid.Column="1" ItemsSource="{Binding AllocationUnitsList}" SelectedItem="{Binding AllocationUnits}" IsEnabled="{Binding IsTso}" Width="130" HorizontalAlignment="Left"
                    DisplayMemberBinding="{ReflectionBinding Converter={x:Static views:TransferLabels.Converter}}" />
          <TextBlock Grid.Row="4" Text="Primary space" VerticalAlignment="Center" />
          <TextBox Grid.Row="4" Grid.Column="1" Text="{Binding PrimarySpaceText}" IsEnabled="{Binding CanSetSpace}" Width="100" HorizontalAlignment="Left" />
          <TextBlock Grid.Row="5" Text="Secondary space" VerticalAlignment="Center" />
          <TextBox Grid.Row="5" Grid.Column="1" Text="{Binding SecondarySpaceText}" IsEnabled="{Binding CanSetSpace}" Width="100" HorizontalAlignment="Left" />
          <TextBlock Grid.Row="6" Text="Average block" VerticalAlignment="Center" />
          <TextBox Grid.Row="6" Grid.Column="1" Text="{Binding AverageBlockText}" IsEnabled="{Binding CanSetAverageBlock}" Width="100" HorizontalAlignment="Left" />
          <TextBlock Grid.Row="7" Text="Buffer size" VerticalAlignment="Center" />
          <TextBox Grid.Row="7" Grid.Column="1" Text="{Binding BufferSizeText}" Width="100" HorizontalAlignment="Left" PlaceholderText="256-32768" />
          <TextBlock Grid.Row="8" Text="Extra options" VerticalAlignment="Center" />
          <TextBox Grid.Row="8" Grid.Column="1" Text="{Binding ExtraOptions}" PlaceholderText="appended to the IND$FILE command" />
        </Grid>
      </Expander>
      <TextBlock Text="The cursor must be at a TSO READY prompt or a command line before you start." Foreground="#A0A0A0" TextWrapping="Wrap" />
      <TextBlock Text="{Binding ValidationMessage}" Foreground="#FF8080" TextWrapping="Wrap"
                 IsVisible="{Binding ValidationMessage, Converter={x:Static ObjectConverters.IsNotNull}}" />
      <StackPanel Orientation="Horizontal" HorizontalAlignment="Right" Spacing="8">
        <Button Content="Close" Click="OnCloseClick" />
        <Button x:Name="StartButton" Content="Start" Command="{Binding StartCommand}" IsDefault="True" />
      </StackPanel>
    </StackPanel>

    <!-- Running: progress and Cancel. -->
    <StackPanel x:Name="RunningPanel" Spacing="12" IsVisible="{Binding IsRunning}">
      <TextBlock Text="{Binding StatusText}" FontSize="16" />
      <ProgressBar Minimum="0" Maximum="{Binding ProgressMaximum, Mode=OneWay}" Value="{Binding ProgressValue, Mode=OneWay}"
                   IsIndeterminate="{Binding IsProgressIndeterminate}" Height="18" />
      <StackPanel Orientation="Horizontal" HorizontalAlignment="Right">
        <Button x:Name="CancelButton" Content="Cancel" Command="{Binding CancelTransferCommand}" />
      </StackPanel>
    </StackPanel>

    <!-- Done: the engine's or host's message, verbatim. -->
    <StackPanel x:Name="DonePanel" Spacing="12" IsVisible="{Binding IsDone}">
      <TextBlock Text="{Binding ResultMessage}" TextWrapping="Wrap" Foreground="#80FF80" IsVisible="{Binding Succeeded}" />
      <TextBlock Text="{Binding ResultMessage}" TextWrapping="Wrap" Foreground="#FF8080" IsVisible="{Binding Failed}" />
      <StackPanel Orientation="Horizontal" HorizontalAlignment="Right" Spacing="8">
        <Button Content="Another transfer" Command="{Binding BackCommand}" />
        <Button Content="Close" Click="OnCloseClick" IsDefault="True" />
      </StackPanel>
    </StackPanel>
  </Panel>
</Window>
```

`src/LizTerm.App/Views/FileTransferWindow.axaml.cs`:

```csharp
using Avalonia.Controls;
using Avalonia.Interactivity;
using LizTerm.App.ViewModels;

namespace LizTerm.App.Views;

/// <summary>The File Transfer dialog. The caller sets DataContext to a <see cref="FileTransferViewModel"/> (from
/// <see cref="SessionViewModel.CreateTransfer"/>); the three panels switch on its phase.</summary>
public partial class FileTransferWindow : Window
{
    public FileTransferWindow()
    {
        InitializeComponent();
        Closing += OnClosing;
    }

    private FileTransferViewModel? ViewModel => DataContext as FileTransferViewModel;

    /// <summary>A running transfer is never orphaned behind a closed dialog: closing asks the engine to cancel and
    /// keeps the window until the Done panel shows the outcome.</summary>
    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (ViewModel is { IsRunning: true } vm)
        {
            e.Cancel = true;
            vm.CancelTransferCommand.Execute(null);
        }
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~FileTransferWindowTests"`
Expected: 11 passed. If the XAML compiler rejects `DisplayMemberBinding="{ReflectionBinding ...}"`, replace each of the three with an item template that disables compiled bindings for the item only:

```xml
<ComboBox.ItemTemplate>
  <DataTemplate x:CompileBindings="False">
    <TextBlock Text="{Binding Converter={x:Static views:TransferLabels.Converter}}" />
  </DataTemplate>
</ComboBox.ItemTemplate>
```

- [ ] **Step 6: Run the full suite and commit**

Run: `dotnet test LizTerm.slnx`
Expected: everything green.

```bash
git add src/LizTerm.App/Views/TransferLabels.cs src/LizTerm.App/Views/FileTransferWindow.axaml src/LizTerm.App/Views/FileTransferWindow.axaml.cs tests/LizTerm.App.Tests/Views/FileTransferWindowTests.cs
git commit -m "Add the File Transfer window with form, running, and done panels

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 10: File menu item and dialog wiring in SessionWindow

**Files:**
- Modify: `src/LizTerm.App/Views/SessionWindow.axaml` (File menu), `src/LizTerm.App/Views/SessionWindow.axaml.cs`
- Modify: `tests/LizTerm.App.Tests/Views/SessionWindowTests.cs`

**Interfaces:**
- Consumes: `SessionViewModel.CreateTransfer` (Task 8), `FileTransferWindow` (Task 9), `AvaloniaFilePicker` (Task 5), `SessionViewModel.IsConnected`.
- Produces: the `FileTransferMenuItem` named menu item and the `OnFileTransferClick` handler. Nothing later depends on them.

- [ ] **Step 1: Write the failing tests**

Append inside the class in `tests/LizTerm.App.Tests/Views/SessionWindowTests.cs`:

```csharp
    [AvaloniaFact]
    public void File_transfer_menu_item_follows_the_connection_state()
    {
        var (window, _, _, session, _) = Show();
        var item = window.FindControl<MenuItem>("FileTransferMenuItem")!;
        Assert.False(item.IsEnabled);
        session.RaiseConnection(ConnectionState.Connected3270);
        Assert.True(item.IsEnabled);
        session.RaiseConnection(ConnectionState.Disconnected);
        Assert.False(item.IsEnabled);
    }

    [AvaloniaFact]
    public void File_transfer_click_opens_the_dialog_over_the_session_window_only_while_connected()
    {
        var (window, _, vm, session, _) = Show();
        var item = window.FindControl<MenuItem>("FileTransferMenuItem")!;

        item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Assert.Empty(window.OwnedWindows);

        session.RaiseConnection(ConnectionState.Connected3270);
        item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        var dialog = Assert.Single(window.OwnedWindows);
        var transfer = Assert.IsType<FileTransferViewModel>(dialog.DataContext);
        Assert.True(transfer.IsForm);

        transfer.LocalPath = "/nonexistent/a.txt";
        transfer.HostFile = "A.B";
        transfer.StartCommand.Execute(null);
        Assert.Equal("A.B", vm.LastTransferRequest?.HostFile);
        dialog.Close();
        Assert.Empty(window.OwnedWindows);
    }
```

Add `using Avalonia.Interactivity;` to the file's usings if it is not there already.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~SessionWindowTests"`
Expected: the two new tests fail (`FindControl` returns null, so a `NullReferenceException`); the existing ones pass.

- [ ] **Step 3: Add the menu item and the handler**

In `src/LizTerm.App/Views/SessionWindow.axaml`, replace the File menu's Disconnect line and the separator that follows it:

```xml
        <MenuItem Header="_Disconnect" Command="{Binding DisconnectCommand}" />
        <Separator />
```

with:

```xml
        <MenuItem Header="_Disconnect" Command="{Binding DisconnectCommand}" />
        <Separator />
        <MenuItem x:Name="FileTransferMenuItem" Header="File _Transfer..." Click="OnFileTransferClick" IsEnabled="{Binding IsConnected}" />
        <Separator />
```

In `src/LizTerm.App/Views/SessionWindow.axaml.cs`, add `using LizTerm.App.Files;` and, after `OnNewSessionClick`, add:

```csharp
    /// <summary>Opens the File Transfer dialog modally over this window. The dialog's own picker parents the OS
    /// file dialogs; the view model comes from the session view model so the last request is remembered.</summary>
    private async void OnFileTransferClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { IsConnected: true } vm) return;
        try
        {
            var dialog = new FileTransferWindow();
            dialog.DataContext = vm.CreateTransfer(new AvaloniaFilePicker(dialog));
            await dialog.ShowDialog(this);
        }
        catch (Exception ex)
        {
            vm.ErrorMessage = "Could not open the File Transfer dialog: " + ex.Message;
        }
        Screen.Focus();
    }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~SessionWindowTests"`
Expected: all pass, including the two new ones.

- [ ] **Step 5: Run the full suite and commit**

Run: `dotnet test LizTerm.slnx`
Expected: everything green.

```bash
git add src/LizTerm.App/Views/SessionWindow.axaml src/LizTerm.App/Views/SessionWindow.axaml.cs tests/LizTerm.App.Tests/Views/SessionWindowTests.cs
git commit -m "Add File Transfer... to the File menu and open the dialog over the session window

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 11: Inspector check against the live host, documentation, and the as-built spec

**Files:**
- Modify: `CLAUDE.md`, `docs/superpowers/specs/2026-09-04-lizterm-m2-file-transfer-design.md`

**Interfaces:** none. This task looks at the real dialog with the real engine where the inspector allows, then documents the feature.

- [ ] **Step 1: Launch the app against the MVS/CE host with an isolated HOME**

No credentials are typed in this task: the app sits on the host's logon screen, which is connected and therefore enables the menu item. The JSON fields are the camelCase names of `SessionProfile`.

```bash
dotnet build src/LizTerm.App
S=$(mktemp -d); mkdir -p "$S/Library/Application Support/LizTerm/profiles"
cat > "$S/Library/Application Support/LizTerm/profiles/MVSCE.json" <<'JSON'
{"name":"MVSCE","host":"10.42.37.209","port":3270,"useTls":false,"verifyCertificate":true,"model":2,"extended":true,"codePage":"cp037","destructiveBackspace":false}
JSON
HOME="$S" LIZTERM_B3270_PATH=/opt/homebrew/bin/b3270 LIZTERM_WIRE_LOG="$S/wire.log" nohup src/LizTerm.App/bin/Debug/net10.0/LizTerm.App MVSCE > "$S/app.log" 2>&1 &
echo $!
```

Keep `S` and the pid for the later steps.

- [ ] **Step 2: Attach the inspector and check what it can reach**

Use the `avalonia_devtools` tools as documented in CLAUDE.md: `attach-to-app` with no arguments to list, then with `id` set to the pid. Wait for the logon screen (`screenshot`; the status bar reads connected).

1. `search` for `FileTransferMenuItem`, then `props` on it: `IsEnabled` must read `True` with `bindingExpression` naming `IsConnected`.
2. Menus never appear as inspector roots, so the item cannot be clicked through `input`. Check the `action` tool's schema once: if it can raise a routed event or invoke a click on a node, use it on the menu item; the dialog then appears as a new root (re-list roots with `tree`, `search` does not find windows opened after its first query). If it cannot, stop this step here and record "dialog not reachable through the inspector" in the task notes; the headless tests and Robert's manual run cover the dialog.
3. If the dialog opened: `screenshot` the Form panel; check the Advanced expander and the three combo boxes read TSO/VM/CICS, Default/Fixed/Variable/Undefined, Default/Tracks/Cylinders/AVBLOCK. Type a local path and a host name through `input` on the two text boxes, press Start. The cursor is in the logon field, so b3270 types the IND$FILE command there and waits: the Running panel shows "Waiting for the host..." and the status bar shows "File transfer in progress". Press Cancel: the Done panel shows `Transfer cancelled.` in red, LizTerm's own text for a cancel it asked for (b3270's `Transfer canceled by user` is folded into the cancellation and never shown). `screenshot` it. Press "Another transfer": the form is still filled. Press Start again and wait 30 seconds without cancelling: the Done panel shows `Transfer did not start within 30s`, red. Close the dialog; confirm with `props` on the `TerminalScreen` node that it has focus again (`IsFocused` True).

```bash
grep -c '"action":"Transfer"' "$S/wire.log"
```

Expected when the dialog was driven: 3 (two starts and one cancel). The logon screen's field now holds the IND$FILE text; that is the host's problem, not LizTerm's, and the connection is dropped at the end anyway.

- [ ] **Step 3: Stop the app**

```bash
kill <pid>; rm -rf "$S"
```

If anything in Step 2 contradicted an expectation, stop and fix it in the task that owns the behavior (window: Task 9; view model: Tasks 6 and 7; wiring: Task 10; backend: Task 3), rerun the suite, and repeat Steps 1 to 3. Do not paper over it here.

- [ ] **Step 4: Update CLAUDE.md**

In the `### The b3270 binary` section, extend the environment-variable sentence so that after `LIZTERM_TEST_VERIFY_CERT=0` for a TLS host with a self-signed certificate)` it continues:

```markdown
; `LIZTERM_TEST_USER` and `LIZTERM_TEST_PASSWORD` additionally enable the IND$FILE round trip in the same project,
which logs on to TSO, sends and receives `LIZTERM.ITEST` under the user's prefix, and deletes it; without them that
test skips. The credentials are typed through the session, so a wire log of that run holds the password on its
outbound side and is never committed.
```

In the `### Recording a replay fixture` section, after the sentence about `gateway-login-tls.jsonl`, add:

```markdown
`indfile-tso-roundtrip.jsonl` is the inbound side of the live IND$FILE round trip against MVS/CE, trimmed to the two
transfers; `IndicationParserTests` asserts its `ft` sequence.
```

In `### Core model (src/LizTerm.Core)`, add after the `IEmulatorSession` bullet:

```markdown
- File transfer is one call: `TransferAsync(FileTransferRequest, IProgress<long>?, CancellationToken)` completes when
  the transfer ends and returns a `FileTransferResult` whose `Message` is the engine's or host's final text
  verbatim, success or failure. It throws only for non-outcomes: `InvalidOperationException` (not started, or a
  transfer already running), `OperationCanceledException` (the token cancelled it and the engine confirmed), and
  `BackendUnavailableException`. `FileTransferRequest.Validate()` checks only what would be refused outright;
  fields that do not apply to the direction, mode, or host type are ignored downstream, never errors.
```

In `### Backend (src/LizTerm.Backend.B3270)`, add after the "Protocol details that are easy to get wrong" bullet:

```markdown
- `TransferMapper` builds the `Transfer` action's `keyword=value` arguments and omits what b3270 would reject
  (`cr`/`remap` in binary mode, allocation keywords on receive or on non-TSO hosts) or ignore (`lrecl`/`blksize`
  without a `recfm`, space fields without `allocation`); receive adds `exist=replace` unless appending. b3270 does
  not answer the Transfer run until the transfer ends, so that run-result is the outcome; `ft` indications only
  feed progress (`running` with `bytes`) to the one in-flight `TransferContext`, and stray `ft` lines are dropped.
  Cancel is a fire-and-forget `Transfer(Cancel)` registered on the token; a failed result after a cancel becomes
  `OperationCanceledException`, a success is still a success.
```

In `### App (src/LizTerm.App)`, add after the mouse-selection bullet:

```markdown
- File transfer: `SessionWindow`'s "File Transfer..." item (enabled while connected) opens `FileTransferWindow`
  modally with a `FileTransferViewModel` from `SessionViewModel.CreateTransfer(IFilePicker)`, which pre-fills it
  from `LastTransferRequest` (the last request started from that window; nothing goes to the profile). The view
  model has Form, Running, and Done phases in one window; Start validates through `TryBuildRequest`, progress is
  marshalled through the dispatch delegate, Cancel cancels the token, and closing a running dialog cancels instead
  of closing. OS file dialogs go through `IFilePicker` (`Files/`), injected like the clipboard; `LocalFileNames`
  suggests the save name (member or last qualifier, VM `FN.FT`). `TransferLabels` labels the combo boxes.
```

In `### Tests`, extend the App tests bullet with: `FakeFilePicker` returns `Result` and records `open` / `save:<name>`; `FakeEmulatorSession.TransferAsync` records `transfer:<Direction>:<HostFile>`, keeps `LastTransferRequest`, exposes `TransferProgress` and `TransferToken`, and waits on `TransferCompletion` when set so a test can drive the Running phase. Add a bullet:

```markdown
- The integration project's `ScreenWaiter` and `TsoNavigator` drive a TSO logon to READY by screen text only
  (LOGON, PASSWORD, `***` pauses answered with Enter, menus left with PF3, READY = last non-blank line); extend
  them with text rules, never coordinates, when a host's screens differ.
```

- [ ] **Step 5: Bring the spec to as-built**

In `docs/superpowers/specs/2026-09-04-lizterm-m2-file-transfer-design.md`, change the status line to `Status: approved 2026-09-04 and implemented on branch claude/indfile-integration-2b9124; this is the as-built spec` and, in section 7.3, replace the sentence starting "Its findings feed" with what the discovery actually found (the logon flow and whether any default changed). If Task 4 changed a mapper default, section 4.1's table must already say so; check it does.

- [ ] **Step 6: Final full run and commit**

Run: `dotnet test LizTerm.slnx`
Expected: everything green; note the totals per project for the final report.

```bash
git add CLAUDE.md docs/superpowers/specs/2026-09-04-lizterm-m2-file-transfer-design.md
git commit -m "Document IND\$FILE transfer in CLAUDE.md and bring the spec to as-built

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
git log --oneline main..HEAD
```

- [ ] **Step 7: Hand Robert the manual check**

The final report to Robert must include this recipe, because the dialog's end-to-end run needs his logon typed at the keyboard:

```bash
dotnet build src/LizTerm.App
S=$(mktemp -d); mkdir -p "$S/Library/Application Support/LizTerm/profiles"
cat > "$S/Library/Application Support/LizTerm/profiles/MVSCE.json" <<'JSON'
{"name":"MVSCE","host":"10.42.37.209","port":3270,"useTls":false,"verifyCertificate":true,"model":2,"extended":true,"codePage":"cp037","destructiveBackspace":false}
JSON
HOME="$S" LIZTERM_B3270_PATH=/opt/homebrew/bin/b3270 src/LizTerm.App/bin/Debug/net10.0/LizTerm.App MVSCE
```

Then: log on to TSO, get to READY, File > File Transfer..., send a small text file to `LIZTERM.MANUAL`, then "Another transfer", switch to Receive, Browse to a save location (the suggested name is `MANUAL`), Start, and compare the two files. Delete the dataset afterwards with `DELETE 'MVSCE02.LIZTERM.MANUAL'`.
