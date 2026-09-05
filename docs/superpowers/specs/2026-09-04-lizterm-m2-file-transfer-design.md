# LizTerm Milestone 2, Plan 2: IND$FILE File Transfer

Date: 2026-09-04
Status: approved 2026-09-04 and implemented on branch claude/indfile-integration-2b9124; this is the as-built spec
Parent: `2026-09-03-lizterm-v1-design.md` sections 4.6, 5.2, 5.5, 6.2, 6.8, 7, and 9

## 1. Purpose

Let a user send a local file to the host and receive a host file locally with IND$FILE, from one
dialog, without reading a manual. This is the second of the three Milestone 2 plans (selection and
clipboard; IND$FILE transfer; polish bundle). It touches all three projects: `IEmulatorSession`
gains one method, the b3270 backend maps it to the `Transfer` action and its `ft` indications, and
the app gains a File menu item and a dialog. It also adds a live round trip against an MVS 3.8j host
to the integration lane.

Decisions made in discussion on 2026-09-04:

- One dialog, not two: the form turns into a progress view when Start is pressed and then into a
  result view, with the form kept filled behind it so a rejected request can be corrected and sent
  again. Hobbyist IND$FILE ports reject requests often enough that this matters.
- The dialog remembers its last-used values for the life of the session window. Nothing is saved
  to the profile; a fresh session starts from defaults (TSO, text, CRLF on, remap on).
- Core exposes a transfer as one idiomatic call that completes when the transfer ends, with
  `IProgress<long>` for bytes and a `CancellationToken` for Cancel (section 2). The spec's original
  `IFileTransfer` handle object was dropped as surface nothing needs.
- The MVS/CE host at `10.42.37.209:3270` (plain TN3270, TSO user `MVSCE02`) is the live target. The
  integration lane reads the TSO credentials from `LIZTERM_TEST_USER` and `LIZTERM_TEST_PASSWORD`,
  so they never appear in the repository or in a command line.

## 2. Approach

b3270 already implements the protocol. Its `Transfer` action takes `keyword=value` arguments, its
run does not complete until the transfer ends, and the run-result carries the same final message
that the `ft` `complete` indication carries. Progress arrives as `ft` indications with `state`
`awaiting`, `running` (with `bytes`), `aborting`, and `complete`; `Transfer(Cancel)` aborts. The
`file-transfer` keyboard lock is already modeled and rendered. The whole feature is therefore a
thin mapping in the backend, a Core contract, and a dialog.

Two Core shapes were considered. A handle object (`StartTransferAsync` returning an object with
progress and completion events and a `Cancel` method) gives the view model a second event
marshalling path and an object lifetime to manage. The chosen shape is one method:

```csharp
Task<FileTransferResult> TransferAsync(FileTransferRequest request, IProgress<long>? progress = null,
                                       CancellationToken cancellationToken = default);
```

It completes when b3270's run-result arrives, a refused or failed transfer is a normal result
whose message the user must read verbatim, cancelling the token sends `Transfer(Cancel)`, and the
fake session records it as one call string like every other method.

The `ft` `complete` text duplicates the run-result text, so the run-result is the single source of
truth for the outcome and `ft` indications feed progress only.

## 3. LizTerm.Core

New file `src/LizTerm.Core/Session/FileTransfer.cs`. No b3270 names, no Avalonia names.

```csharp
public enum TransferDirection { Send, Receive }
public enum TransferHostType { Tso, Vm, Cics }
public enum TransferMode { Text, Binary }
public enum RecordFormat { Default, Fixed, Variable, Undefined }
public enum AllocationUnits { Default, Tracks, Cylinders, AvBlock }

public sealed record FileTransferRequest
{
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

    /// <summary>Returns a user-facing message, or null when the request can be attempted.</summary>
    public string? Validate();
}

public sealed record FileTransferResult(bool Succeeded, string Message, long Bytes);
```

`Validate()` checks, in this order, first failure wins:

1. `LocalPath` blank: "Choose a local file."
2. `HostFile` blank: "Enter the host file name."
3. Any of `Lrecl`, `Blksize`, `PrimarySpace`, `SecondarySpace`, `AverageBlock` set and not
   positive: "<Field> must be a positive number." with the field's display name (LRECL, BLKSIZE,
   Primary space, Secondary space, Average block size).
4. `BufferSize` set and outside 256 to 32768: "Buffer size must be between 256 and 32768."
5. Sending to TSO with `AllocationUnits` other than Default and `PrimarySpace` null: "Primary
   space is required when allocation units are set."
6. Sending to TSO with `AllocationUnits.AvBlock` and `AverageBlock` null: "Average block size is
   required for AVBLOCK allocation."

Fields that do not apply to the direction, mode, or host type are not errors; the backend omits
them (section 4.1). This lets the dialog keep its Advanced fields filled when the user switches
from Send to Receive or from TSO to VM.

`FileTransferResult.Message` is the engine's or host's final text, unaltered, on success and on
failure. `Bytes` is the last progress count seen, zero if none.

`IEmulatorSession` gains `TransferAsync` as shown in section 2. Contract:

- Completes when the transfer ends. A transfer the engine or host refuses or aborts is a result
  with `Succeeded` false.
- Throws `InvalidOperationException` when the session has not been started or a transfer is
  already in progress on this session; `OperationCanceledException` when the token was cancelled
  and the engine then reported the transfer as failed; `BackendUnavailableException` when the
  engine dies during the transfer. A success result that arrives after a cancel request is
  returned as success.
- `progress.Report(bytes)` is called on the backend thread, in order, like every event. It may be
  called zero times.
- On receive, the request always tells the engine to replace an existing local file. Overwrite
  consent is the caller's job: the dialog gets it from the OS Save dialog and refuses to start a
  receive into an existing file from any other path (typed, remembered, or edited after browsing)
  unless Append is on.

## 4. LizTerm.Backend.B3270

### 4.1 TransferMapper

`Protocol/TransferMapper.cs`, static and pure, unit-tested like `HostStringBuilder`.

`ToAction(FileTransferRequest)` returns one `B3270Action("Transfer", args)` with `keyword=value`
arguments in this order. Values are raw strings; the JSON serializer quotes them, so paths with
spaces and VM names such as `PROFILE EXEC A` need no escaping.

| Argument | Emitted | Value |
| --- | --- | --- |
| `direction` | always | `send` or `receive` |
| `hostfile` | always | `HostFile` as typed |
| `localfile` | always | `LocalPath` |
| `host` | always | `tso`, `vm`, or `cics` |
| `mode` | always | `ascii` for Text, `binary` for Binary |
| `cr` | Text only | `remove` (send, CrLf on), `add` (receive, CrLf on), `keep` (CrLf off) |
| `remap` | Text only | `yes` or `no` |
| `exist` | Append, or Receive | `append` when Append; otherwise `replace` on receive; omitted on send |
| `recfm` | Send, TSO or VM, not Default | `fixed`, `variable`, `undefined` (Undefined is TSO only; for VM it is omitted) |
| `lrecl` | Send, TSO or VM, `recfm` emitted, set | number |
| `blksize` | Send, TSO, `recfm` emitted, set | number |
| `allocation` | Send, TSO, not Default | `tracks`, `cylinders`, `avblock` |
| `primaryspace` | Send, TSO, `allocation` emitted | number |
| `secondaryspace` | Send, TSO, `allocation` emitted, set | number |
| `avblock` | Send, TSO, `allocation=avblock` | number |
| `buffersize` | set | number |
| `otheroptions` | non-blank | `ExtraOptions` trimmed |

The dependencies (`lrecl` and `blksize` need a record format; space fields need units) mirror how
b3270 builds the IND$FILE command: without a RECFM it never emits LRECL or BLKSIZE, and without
units it never emits SPACE. Emitting them anyway would be accepted and ignored, but the table keeps
the wire log honest. b3270 rejects `cr` and `remap` for binary mode and every allocation keyword
on receive or on the wrong host type, which is why they are omitted rather than passed through.

`CancelAction` is `B3270Action("Transfer", "Cancel")`.

### 4.2 Session state

`B3270Session` holds one nullable in-flight transfer context (`IProgress<long>? Progress`,
`long Bytes`). `TransferAsync`:

1. `ThrowIfCancellationRequested()`. Throw `InvalidOperationException("The session has not been
   started.")` if there is no process, and `InvalidOperationException("A file transfer is already in
   progress.")` if the slot is taken; the slot is claimed atomically.
2. Send `TransferMapper.ToAction(request)` through `RunRawAsync`. The run-result arrives when the
   transfer ends.
3. Register on the token: send `CancelAction` through a fire-and-forget `RunRawAsync`, any
   exception swallowed. b3270 answers "No transfer pending." when the transfer already ended, which
   is harmless.
4. Await the run-result. If it failed and the token is cancelled, throw
   `OperationCanceledException(token)`. Otherwise return `new FileTransferResult(result.Success,
   string.Join("\n", result.Text), context.Bytes)`.
5. Clear the slot in `finally`. Engine death already faults every pending run with
   `BackendUnavailableException` (see `OnProcessEnded`), so the exception propagates and the slot
   is freed on the same path.

`HandleStateIndication` gains a case for `FtIndication`: when a transfer is in flight and the
indication carries `Bytes`, store it and call `Progress?.Report`. Everything else about the
indication (`awaiting`, `aborting`, `complete`, `Cause`) is ignored, and an `ft` indication with no
transfer in flight is dropped. `FtIndication` is already parsed by `IndicationParser`.

### 4.3 Sequence on the wire

Typical send, tags abbreviated:

```
> {"run":{"r-tag":"7","actions":[{"action":"Transfer","args":["direction=send","hostfile=LIZTERM.ITEST","localfile=/tmp/a.txt","host=tso","mode=ascii","cr=remove","remap=yes"]}]}}
< {"oia":{"field":"lock","value":"file-transfer"}}
< {"ft":{"state":"awaiting","cause":"ui"}}
< {"ft":{"state":"running","bytes":0,"cause":"ui"}}
< {"ft":{"state":"running","bytes":2048,"cause":"ui"}}
< {"ft":{"state":"complete","success":true,"text":"Transfer complete, 2048 bytes transferred\n1.2 Kbytes/sec in DFT mode","cause":"ui"}}
< {"oia":{"field":"lock"}}
< {"run-result":{"r-tag":"7","success":true,"text":["Transfer complete, 2048 bytes transferred","1.2 Kbytes/sec in DFT mode"]}}
```

The exact `complete` text and the row layout are confirmed by the discovery run (section 7.3); the
messages themselves (`Transfer complete, %i bytes transferred`, `Transfer canceled by user`,
`Transfer did not start within 30s`, `Host disconnected, transfer canceled`, `Transfer canceled by
host`, `Not in 3270 mode, transfer canceled`) were verified in the 4.5ga6 binary.

## 5. LizTerm.App

### 5.1 File picking seam

`src/LizTerm.App/Files/IFilePicker.cs`:

```csharp
public interface IFilePicker
{
    /// <summary>OS Open dialog. Null when cancelled or the choice has no local path.</summary>
    Task<string?> PickFileToSendAsync();
    /// <summary>OS Save dialog, which confirms overwrite. Null when cancelled.</summary>
    Task<string?> PickSaveLocationAsync(string suggestedFileName);
}
```

`AvaloniaFilePicker(TopLevel owner)` wraps `owner.StorageProvider` (`OpenFilePickerAsync` with
`AllowMultiple = false`, `SaveFilePickerAsync` with `SuggestedFileName`) and returns
`TryGetLocalPath()`. `FakeFilePicker` in the tests returns a preset path or null and records
which method was called and the suggested name. The pair mirrors `ITextClipboard`.

### 5.2 Session view model

`SessionViewModel` gains:

- `public FileTransferRequest? LastTransferRequest { get; private set; }`, the session-lifetime
  memory.
- `public FileTransferViewModel CreateTransfer(IFilePicker picker)`, which constructs the dialog
  view model around the same `IEmulatorSession` and dispatch delegate, pre-filled from
  `LastTransferRequest`, and subscribes to its `Started` event to store each started request.

The session view model does not track the transfer's progress or outcome; the dialog does.

### 5.3 Menu and window wiring

`SessionWindow.axaml` File menu, after Disconnect and before a separator and Close:

```xml
<MenuItem x:Name="FileTransferMenuItem" Header="File _Transfer..." Click="OnFileTransferClick"
          IsEnabled="{Binding IsConnected}" />
```

The click handler returns unless the view model reports connected, creates a `FileTransferWindow`,
sets its `DataContext` to `ViewModel.CreateTransfer(new AvaloniaFilePicker(dialog))`, awaits
`ShowDialog(this)`, then calls `Screen.Focus()`. The dialog is modal to its session window only;
other session windows keep working, each with its own engine.

### 5.4 FileTransferViewModel

`ViewModels/FileTransferViewModel.cs`, constructed as
`(IEmulatorSession session, IFilePicker picker, Action<Action> dispatch, FileTransferRequest? initial)`,
with `event Action<FileTransferRequest>? Started`.

Form properties (all observable): `IsSend` (radio; receive is its negation), `LocalPath`,
`HostFile`, `HostType` with a `HostTypes` list, `IsText` (radio; binary is its negation), `CrLf`,
`Remap`, `Append`, `RecordFormat` with a `RecordFormats` list, `LreclText`, `BlksizeText`,
`AllocationUnits` with a list, `PrimarySpaceText`, `SecondarySpaceText`, `AverageBlockText`,
`BufferSizeText`, `ExtraOptions`, `ValidationMessage`. Derived read-only properties drive enabling
and mirror what `TransferMapper` puts on the wire, so the form never accepts a value the mapper
would drop: `CanSetRecordFormat` (TSO and VM; CICS has no RECFM), `CanSetLrecl` (a record format
on TSO or VM), `CanSetBlksize` (a record format on TSO), `CanSetSpace` and `CanSetAverageBlock`
(allocation units on TSO). The Advanced expander is visible only when sending; a value entered for
one host type stays in its disabled control when another is chosen; for VM the Undefined record
format is not offered.
Numeric fields are text boxes: blank means unset, a non-integer produces "<Field> must be a whole
number." before Core validation runs.

`initial`, when present, fills every form field, including the file names. Without it the defaults
are Send, TSO, Text, CrLf on, Remap on, everything else empty.

Phase: `TransferPhase { Form, Running, Done }` with `IsForm`, `IsRunning`, `IsDone` for bindings.

Running properties: `StatusText` ("Waiting for the host..." until the first progress report, then
the byte count formatted with thousands separators, "Cancelling..." after Cancel), `BytesTransferred`,
`TotalBytes` (the local file's length when sending, null otherwise or when it cannot be read),
`IsProgressIndeterminate` (true when `TotalBytes` is null), `IsCancelling`.

Done properties: `ResultMessage`, `Succeeded`.

Commands:

- `BrowseCommand`: sending calls `PickFileToSendAsync`; receiving calls `PickSaveLocationAsync`
  with `LocalFileNames.Suggest(HostFile, HostType)`. A non-null result replaces `LocalPath`, and on
  receive it is remembered as the one path with overwrite consent (the Save dialog asked).
- `StartCommand` (can execute in Form): builds the request (`TryBuildRequest`), shows the first
  validation message inline and stops; on a non-append receive into an existing local file whose
  path is not the remembered consented one, shows "<name> already exists. Choose it with Browse...
  to replace it, or turn on Append." and stops; otherwise raises `Started`, enters Running with a fresh
  `CancellationTokenSource`, and awaits `session.TransferAsync(request, progress, token)` where
  `progress` marshals each report through the dispatch delegate. The result enters Done with its
  message and success flag. `OperationCanceledException` enters Done failed with "Transfer
  cancelled."; any other exception enters Done failed with its message.
- `CancelTransferCommand` (can execute in Running and not already cancelling): sets `IsCancelling`
  and cancels the token.
- `BackCommand` (can execute in Done): returns to Form with every field as it was.

`Files/LocalFileNames.cs` is a pure helper: strip surrounding quotes; TSO `A.B.C(MEM)` gives
`MEM`, `A.B.C` gives `C`; VM `FN FT [FM]` gives `FN.FT`; CICS gives the name as typed; blank gives
`received`. Case is preserved.

### 5.5 FileTransferWindow

`Views/FileTransferWindow.axaml`, title "File Transfer", a non-resizable dialog sized to its content, with
three panels toggled by `IsVisible` bindings on the phase flags:

- Form: the fields of section 5.4 laid out top to bottom, direction first, then local file with a
  Browse button, host file, host type, mode, the three checkboxes, the Advanced expander, a hint
  line "The cursor must be at a TSO READY prompt or a command line before you start.", the
  validation message in the same style as the profile editor, and Start and Close buttons.
- Running: the status text, a `ProgressBar` bound to `BytesTransferred`, `TotalBytes`, and
  `IsProgressIndeterminate`, and a Cancel button.
- Done: the result message, wrapped, in green for success and the error bar's red for failure,
  with "Another transfer" and Close buttons.

Close calls `Close()`. The window's `Closing` handler defers to the view model's `TryClose()`: while
Running and not yet cancelling it cancels the close and executes `CancelTransferCommand`, so a
transfer is never orphaned by a careless close; a second close while the engine has still not
answered the cancel is allowed, because b3270 aborts a running transfer only on the host's next turn
and a host that has stalled would otherwise pin the dialog, the session window, and Quit (the
dialog is modal, so Disconnect is out of reach). The cancel is already on its way; the backend frees
its transfer slot when the run finally ends or the session disconnects.

## 6. Error handling

Every failure has one home. The dialog's Done panel shows anything about the transfer itself; the
session window's error bar keeps showing session-level faults only.

- Refused before it starts (bad option, "Not connected in 3270 mode", a local file b3270 cannot
  open, the command line too short for the IND$FILE command, a transfer already running on the
  engine side): failed run-result, shown verbatim in Done; "Another transfer" returns to the filled
  form.
- Host never answers (cursor not at a command prompt, IND$FILE not installed): b3270 gives up after
  30 seconds with "Transfer did not start within 30s". Cancel works immediately during that wait.
- Host rejects or aborts: IND$FILE's own message is the failed result's text.
- Disconnect mid-transfer: "Host disconnected, transfer canceled" in Done; the status bar shows the
  disconnect as today.
- Engine death: `BackendUnavailableException` reaches Done as a failure; the session window's
  Faulted path shows the stderr tail in its error bar as today.
- Cancel: "Transfer cancelled." in Done. A success that beats the cancel is shown as success.
- Client-side file handling: b3270 reads and writes the local file. LizTerm only reads its length
  for the send progress bar and falls back to an indeterminate bar if that throws.
- Keyboard lock: b3270 locks the keyboard for the transfer's duration and the status bar renders
  "File transfer in progress", so the lock is explained even past the modal dialog.
- No duplicate messages: errors inside a run go to the run-result, not to a `popup` indication, so
  the error bar stays quiet during a transfer.
- Two transfers: one modal dialog per session window prevents it in the UI; the session's slot
  guard makes a second call an `InvalidOperationException`, which Done shows as a failure.

## 7. Testing

### 7.1 Unit tests

Core (`tests/LizTerm.Core.Tests/Session/FileTransferRequestTests.cs`): each `Validate()` rule and
its message, the default values, and that inapplicable fields are not errors.

Backend:

- `Protocol/TransferMapperTests.cs` pins the full argument list for: send text defaults; receive
  text (gets `cr=add` and `exist=replace`); binary in both directions (no `cr`, no `remap`); append
  in both directions; TSO send with record format, LRECL, BLKSIZE, tracks with primary and secondary
  space; AVBLOCK with average block; VM send with fixed record format and LRECL and no BLKSIZE or
  allocation; VM with Undefined record format omitted; CICS with no allocation keywords; receive with
  every Advanced field set emits none of them; buffer size and extra options; a path with spaces
  passes through unchanged; `CancelAction`.
- `B3270SessionTransferTests.cs` on `FakeB3270Process` with `RunResponder` holding the Transfer
  run open: `running` indications drive `progress` in order and the result carries the last count;
  a failed run-result with host text is a failed result with that text; cancelling the token sends
  `Transfer(Cancel)` and a following failed run-result becomes `OperationCanceledException`; a
  success result after a cancel is returned as success; a second `TransferAsync` while one is
  pending throws and sends nothing; process exit mid-transfer throws `BackendUnavailableException`
  and a later transfer is accepted; `ft` indications with no transfer in flight are ignored; a
  cancelled token before the call throws without sending.
- `ReplayTests` or `IndicationParserTests` gains a case that feeds `indfile-tso-roundtrip.jsonl`
  through the parser and asserts the `ft` state sequence `awaiting`, `running`..., `complete` with
  `success` true, and that every line parses.

App:

- `FakeEmulatorSession` gains `TransferAsync`: records `transfer:<direction>:<hostFile>` and keeps
  the full `LastTransferRequest`; `TransferResult` (default success), `TransferException`, and
  `TransferCompletion` (a `TaskCompletionSource`; when set, the fake awaits it, so a test can push
  progress through the captured `IProgress<long>` and observe the captured token).
- `ViewModels/FileTransferViewModelTests.cs`: defaults; pre-fill from `initial`; Browse in both
  directions with the suggested name; validation messages for blank fields, non-integer text, and
  each Core rule; derived enabling flags; Start records the request and raises `Started`; the
  Running phase shows waiting text then the byte count and a determinate bar for send; Cancel sets
  cancelling and the token; result, cancellation, and exception each land in Done correctly; Back
  keeps the fields; `TryClose` cancels first and allows the second close; a receive into an existing
  file is refused for a typed, remembered, or edited path, and allowed after the Save dialog chose it
  or when appending.
- `ViewModels/SessionViewModelTransferTests.cs`: `CreateTransfer` twice, the second is pre-filled
  from the first's started request; the picker instance reaches the dialog view model.
- `Files/LocalFileNamesTests.cs`: each suggestion rule.
- `Views/FileTransferWindowTests.cs` (headless): the three panels switch with the phase; Start is
  reachable and shows a validation message on an empty form; Closing while Running cancels instead
  of closing, and a second Closing while the cancel is unanswered closes the window.
- `Views/SessionWindowTests.cs`: the File Transfer menu item is enabled only while connected.

### 7.2 Live round trip

`tests/LizTerm.Integration.Tests/LiveHostTests.cs` gains `Indfile_round_trip_matches`, skipped
unless `LIZTERM_TEST_HOST`, `LIZTERM_TEST_USER`, and `LIZTERM_TEST_PASSWORD` are all set (the skip
reason names the missing variable). A `ScreenWaiter` helper in the integration project polls
`ScreenUpdated` snapshots for a predicate with a timeout.

1. Connect; wait for the logon screen; log on with the credentials; reach READY. If the user's
   logon lands in a menu or ISPF, back out to READY first. The exact screens come from the discovery
   run (7.3).
2. Write a temporary local text file of a few lines, including lowercase and a line with trailing
   spaces.
3. `TransferAsync` Send, Text, defaults, to `LIZTERM.ITEST` (the user's prefix makes it
   `MVSCE02.LIZTERM.ITEST`), collecting progress reports.
4. `TransferAsync` Receive the same dataset to a second temporary file.
5. Assert both results succeeded, at least one progress report arrived, the received bytes are
   positive, and the two files match with trailing blanks trimmed per line, since the host pads
   records to the record length.
6. In `finally`: type `DELETE 'MVSCE02.LIZTERM.ITEST'` and Enter, then `LOGOFF` and Enter, then
   disconnect. Cleanup failures are reported but do not mask the assertion.

The credentials are typed through `TypeTextAsync`, so the outbound side of a wire log contains
the password. Wire logs from live runs are never committed.

### 7.3 Discovery run and fixture

Before the mapper defaults were finalized, a discovery run connected to the MVS/CE host with the
wire log on, recorded the screens from connect through logon to READY, and ran one send and one
receive with the Text defaults. Its findings: the Hercules TN3270 server answers the connection with its own banner (dismissed with Enter; its
help text contains the word "logon", so the logon predicate has to require `===>` too) before the real
`TSO Logon ===>` screen; `LOGON <user>` and the password prompt lead straight to READY with no menu and no `***`
pause; and the host keeps repainting for a moment after `READY` appears, so the navigator waits 500 ms of screen
quiet before keystrokes and before trusting READY, because IND$FILE typed into a half-painted field fails with
`INVALID COMMAND NAME SYNTAX`. The host's IND$FILE 2.0.5 accepted the Text defaults (`ASCII CRLF`, remap)
unchanged, so no `TransferMapper` default changed. The inbound lines of that wire log, trimmed to the transfer's `ft` sequence with the
surrounding `oia` and run-result lines, become
`tests/LizTerm.Backend.B3270.Tests/Fixtures/indfile-tso-roundtrip.jsonl`, documented in the
fixtures README.

### 7.4 Manual check

The live lane proves Core and the backend end to end. The dialog is verified headless and through
the Avalonia DevTools inspector against the fake for layout and phase switching. Its final run
against MVS/CE is Robert's, with a seeded profile under an isolated HOME as described in
CLAUDE.md, because it needs the logon typed at the keyboard.

## 8. Documentation

- CLAUDE.md: the `LIZTERM_TEST_USER` and `LIZTERM_TEST_PASSWORD` variables, the transfer seam
  (`TransferAsync`, `TransferMapper`, the in-flight slot, `IFilePicker`), the new fixture, and the
  fact that the File Transfer dialog is opened by the window and remembered by the session view
  model.
- This spec, updated as built if discovery changes anything.
- The fixtures README entry for `indfile-tso-roundtrip.jsonl`.

## 9. Out of scope

Per-profile transfer defaults; a transfer queue, batch, or history; drag-and-drop onto the screen;
a keyboard shortcut for the dialog; Windows code page selection (`windowscodepage`); live testing
of VM and CICS hosts (no host available; the mapper is unit-tested for them); a Core-level notion
of transfer phases beyond progress (the `awaiting` and `aborting` states are not surfaced); any
change to the profile model, store, or editor.
