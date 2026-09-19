# LizTerm: the mvsMF manage slice — create, rename, delete, and ETag conflict detection

Design for the next phase of #17, agreed 2026-09-19. It follows the dataset browser spec of 2026-09-16
(`2026-09-16-lizterm-mvsmf-dataset-browser-design.md`, "the browser spec" below) and the token-auth spec of
2026-09-18, and adds the operations the browser spec's §8 left out: dataset allocation and deletion, rename, and
ETag conflict detection.

## 1. Purpose

The browser can list, download, upload and delete members. It cannot create a dataset, delete one, or rename
anything, so the first thing a user does after uploading source is still done on the 3270 screen in ISPF 3.2 and
3.4. And a replace is last-write-wins: a member changed on the host since it was downloaded is overwritten without
a word. This slice:

1. adds **Create**, **Rename** (member and dataset) and **Delete** (dataset) to the browser, each pane owning the
   buttons for its kind of target (§4);
2. adds **ETag conflict detection**: a download remembers the member's stamp for the session window, an upload
   sends it back, and a member that changed meanwhile gets a question instead of an overwrite (§5);
3. records what mvsMF does for each of these in the compatibility log, with fixtures and tests to pin it.

Jobs, USS, console services, copy, a Sign Out control and the parked UI items stay on #17's list (§9).

## 2. What the host does (mvsMF 1.1.1-dev source at `cf4d6d5`, `src/dsapi.c` and `docs/endpoints/datasets/`)

| Operation | Request | Success | Failures |
|---|---|---|---|
| Create | `POST restfiles/ds/{name}`, JSON body `dsorg`, `recfm`, `lrecl`, `blksize`, `primary`, optional `secondary`, `dirblk`, `alcunit` (`TRK`, `CYL`, `BLK`; default `TRK`) | 201 | 400 reason 3 for a missing or invalid field; **500 `{"category":8,"rc":900,"reason":7,"message":"Dynamic allocation Error"}` for every allocation failure**: the name exists, no space, a DCB the volume cannot hold, or no authority. Byte for byte what real z/OSMF answers, by design (mvsMF #317, #329). |
| Delete a dataset | `DELETE restfiles/ds/{name}` | 204 | 404 reason 4 when not cataloged; 500 category 6 reason 11 when the scratch or uncatalog failed; authorization refusal is the 500 category 4 shape (`authorization-is-500`). Needs ALTER. |
| Rename a member | `PUT restfiles/ds/{dsn}({new})`, `Content-Type: application/json`, body `{"request":"rename","from-dataset":{"dsn":"{dsn}","member":"{old}"}}` | 204 | 404 reason 5 when the old member (or the library) is missing; **400 reason 7 "Rename target already exists"**; 500 reason 8 "Rename operation failed" otherwise. Needs UPDATE on the library. |
| Rename a dataset | `PUT restfiles/ds/{new}`, the same content type, body `{"request":"rename","from-dataset":{"dsn":"{old}"}}` | 204 | 404 reason 4 when the old name is not cataloged; 500 reason 8 "Rename operation failed" otherwise, **including a new name that already exists** (IDCAMS ALTER refuses it; the host does not check first). Needs ALTER on **both** names. |
| ETag on read | `GET` with `X-IBM-Return-Etag: true` | 200 with `ETag: <16 hex digits>`, unquoted, the same in text and binary mode | none is sent without the header |
| ETag on write | `PUT` with `If-Match: <stamp>`; `X-IBM-Return-Etag: true` returns the stamp of the member as written | 204 (with `ETag` when asked) | **412 `{"category":6,"reason":10,"message":"The resource was modified since the supplied ETag was created"}`**, nothing written. The stamp of the stored member is not the stamp of the body sent, so the next `If-Match` must be the PUT's answer. |

Names in the URL and in the rename body are folded to upper case and refused beyond their length by the host
(mvsMF #334); `HostPath` folds and refuses the same way on LizTerm's side first. There is no volume-addressed form
of any route (mvsMF #336). Real z/OSMF takes the same requests and headers.

## 3. Decisions

- **Explicit allocation fields, prefilled from the selection.** The create form shows every value that goes to the
  host; a selected dataset prefills the DCB fields from the listing, which is ISPF's "allocate like" without a
  second panel. mvsMF's `like` (copy the model's DCB and derive space from its total allocated tracks) is not used:
  its space can be large for a system library, nothing on screen would say what will be allocated, and the form
  needs the explicit fields anyway for the no-selection case.
- **Each pane owns its buttons.** The dataset pane gets New…, Rename…, Delete… for the selected dataset; the
  bottom bar keeps Download…, Upload…, Delete… for members and gains Rename… for one member. A button names one
  kind of target, so deleting a library can never be an accident of which list last had the keyboard.
- **Questions stay in the strip.** Rename asks in the existing confirmation strip, which gains an optional text
  box; there is no new dialog. Delete asks with the dataset's name in the button label.
- **ETag is a session-window memory, not a file attribute.** The stamp lives beside the sign-in on
  `HostFileAccess`, keyed by path, so it survives closing and reopening the browser and dies with the session
  window. Nothing is written into the downloaded file.
- **Conflict is a second question, asked only on a 412.** Folding it into the "already exists" question would
  cost the host a read pass per replaced member before asking; a 412 costs nothing and only appears on a real
  conflict.
- **Two PRs**, Core and backend first, then the browser, as the earlier slices did.

## 4. Architecture

### 4.1 Core: the contract

`IHostFileService` gains three operations and the stamp on the two it has. Every rule below is host-neutral;
z/OSMF sends the same requests.

```csharp
public enum DatasetOrganization { Sequential, Partitioned }
public enum SpaceUnit { Tracks, Cylinders }

/// What Create sends. Validated by DatasetAllocation.Problems before it is sent.
public sealed record DatasetAllocation(
    DatasetOrganization Organization, string Recfm, int Lrecl, int Blksize,
    SpaceUnit Unit, int Primary, int Secondary, int DirectoryBlocks);

/// A read's content and the host's stamp of it, null when the host sent none.
public sealed record HostTextRead(IReadOnlyList<string> Lines, string? Etag);
public sealed record HostBinaryRead(long Bytes, string? Etag);

Task CreateDatasetAsync(HostPath dataset, DatasetAllocation allocation, CancellationToken ct = default);
/// A member: newName is the new member name, in the same library. A dataset: newName is the new dataset name.
Task RenameAsync(HostPath from, string newName, CancellationToken ct = default);
/// A member or a whole dataset.
Task DeleteAsync(HostPath path, CancellationToken ct = default);
Task<HostTextRead> ReadTextAsync(HostPath path, IProgress<long>? progress = null, CancellationToken ct = default);
Task<HostBinaryRead> ReadBinaryAsync(HostPath path, Stream destination, IProgress<long>? progress = null, CancellationToken ct = default);
/// ifMatch: the stamp the target must still hold, else HostFileErrorKind.Conflict and nothing written.
/// Returns the stamp of the target as written, null when the host sent none.
Task<string?> WriteTextAsync(HostPath path, IReadOnlyList<string> lines, string? ifMatch = null, CancellationToken ct = default);
Task<string?> WriteBinaryAsync(HostPath path, Stream source, string? ifMatch = null, CancellationToken ct = default);
```

`DatasetAllocation.Problems(...)` returns the local rules' findings, one sentence per field: RECFM a first letter
of `F`, `V` or `U`, then any of `B`, `S`, `A`, `M` each at most once (`FB`, `VBA`, `FBS`, `U`); LRECL and BLKSIZE positive integers,
LRECL 0 allowed for `U`; Primary ≥ 1; Secondary ≥ 0; Directory blocks ≥ 1 for a partitioned dataset and ignored
for a sequential one. The DCB combinations the host may refuse (a BLKSIZE that is not a multiple of a fixed LRECL,
say) are left to the host: it is the judge, and its one answer is the same for every reason.

Three new `HostFileErrorKind`s, each with a sentence fit to show:

| Kind | mvsMF shape | Message |
|---|---|---|
| `Conflict` | 412, category 6, reason 10 | `{what}: changed on the host since it was read.` |
| `AlreadyExists` | 400, category 6, reason 7 (member rename only) | `{what}: a member of that name already exists.` A dataset renamed onto an existing name is the host's 500 reason 8, reported as a server error quoting its message. |
| `CannotAllocate` | 500, category 8, rc 900, reason 7 | `{what}: the host could not allocate it (it may already exist, there may be no space, or you may not be authorized).` |

`HostFileTransfer` (Core) carries the stamp through: `DownloadAsync` returns the read's stamp, and
`UploadTextAsync` / `UploadBinaryAsync` take an `ifMatch` and return the write's stamp. `HostPath` gains
nothing; the existing `DatasetNameError` and `MemberNameError` validate the new names.

### 4.2 Backend: `MvsmfFileService`

- **Create** is `POST` with a JSON body built by `MvsmfJson` (`dsorg` `PS`/`PO`, `recfm`, `lrecl`, `blksize`,
  `alcunit` `TRK`/`CYL`, `primary`, `secondary`, and `dirblk` only for `PO`), `Content-Type: application/json`.
  201 is success; anything else goes through `MvsmfErrors`.
- **Rename** is the one `PUT` that sends `application/json`. `MvsmfErrors` maps 400 reason 7 to `AlreadyExists`;
  404 reasons 4 and 5 are already `NotFound`. The `put-json-is-rename` tag stays on the write path as the reason
  writes never send that content type; the rename path carries a note pointing at it.
- **Delete** drops its `NotSupportedException` and sends `DELETE` for a dataset path too.
- **Reads** add `X-IBM-Return-Etag: true` and return the `ETag` header's value trimmed of quotes and a `W/`
  prefix, or null. **Writes** add the same header, send `If-Match` when given (the value as the host gave it,
  never parsed), and return the answer's `ETag`. A 412 is `Conflict`; `MvsmfErrors` reads it from the status and
  the reason.
- `MvsmfErrors.Classify` adds, in order after the existing rules: category 8 with rc 900 → `CannotAllocate`;
  412 → `Conflict`; 400 with category 6 reason 7 → `AlreadyExists`.
- Bodies are built once per call, as today, so the repeat after a 401 sends the same JSON.

### 4.3 Compatibility log

- `etag-unused` is rewritten as **`etag`**: honoured on 1.1.0; the stamp is 16 hex digits, unquoted, the same in
  both data types; a write's next `If-Match` must be the PUT's answer, not the pre-save stamp; LizTerm asks for
  it on every read and write and holds it per session window (§5).
- **`create-failure-is-one-500`** (log only): every allocation failure, including "already exists" and a RAKF
  refusal, is the same 500 (mvsMF #317); LizTerm reports it as `CannotAllocate` with the three possible causes.
- **`rename-target-exists-400`** (log only): a rename onto an existing member is 400 reason 7; LizTerm reports it
  as `AlreadyExists`. (Recorded because z/OSMF's own answer to this case is undocumented.)
- `put-json-is-rename` is reworded: the content type is sent on purpose by `RenameAsync` and by nothing else.

### 4.4 App: the browser

**Dataset pane.** A `WrapPanel` under the dataset list, above Load more datasets: `NewDatasetButton`,
`RenameDatasetButton`, `DeleteDatasetButton`. `CanCreate` is idle and no review or form open. `CanRenameDataset`
and `CanDeleteDataset` also need `SelectedDataset` whose name passes `HostPath.DatasetNameError`; an unsupported
organisation is allowed, since neither operation opens the dataset. The bottom bar gains `RenameMemberButton`
(`CanRenameMember`: idle, a partitioned dataset, exactly one selected member).

**Rename.** `ConfirmationRequest` gains an optional input: `InputLabel`, `Input` (observable, prefilled),
`InputProblem` (computed by a `Func<string, string?>` the asker supplies; null when the input is acceptable) and
`CanAnswerPrimary` (no problem, and the folded input differs from the original). The strip shows a text box
between the message and the buttons when `HasInput`; the window focuses it, selected, instead of the Cancel
button, and Enter in it is the primary. The message is `Rename MEMBER in LIBRARY to:` or `Rename DATASET to:`, the
input label is empty, and the primary label is `Rename`. The asker folds the answer with `HostPath` and calls
`RenameAsync`. A `NotFound` on a member rename means the member went meanwhile, so the member list is reloaded.

- A member rename reloads the member list (`LoadMembersCoreAsync`) and selects the new name if it is shown; the
  status line is `✓ Renamed OLD to NEW.` A `NotFound` or `AlreadyExists` answer is the status line with the
  message from `HostFileMessages`.
- A dataset rename re-lists the current filter (`ListCoreAsync`), then selects the new name if the filter shows it,
  else says `✓ Renamed OLD to NEW (not shown by the filter PATTERN).`
- Both move the ETag memory's entries (§5).

**Delete a dataset.** The question is `Delete NAME, a partitioned dataset with N members? This cannot be undone.`
where the count is the loaded count, `N+` while the host has more, "a sequential dataset" for PS, and the bare
name for an unsupported organisation; the primary label is `Delete NAME`. On success the row leaves `Datasets`,
the selection clears, and the status line is `✓ Deleted NAME.` The ETag memory drops every path under it.

**Create.** `NewDatasetForm` is a view model of its own (`NewDatasetFormViewModel`, in `ViewModels/`), shown in
the right pane's `Panel` beside the member pane and the review pane, under `IsCreating`; `ShowMemberPane` and
`CanChooseDataset` are false while it is open, as for a review. Its fields are strings bound to text boxes
(`Name`, `Recfm`, `Lrecl`, `Blksize`, `Primary`, `Secondary`, `DirectoryBlocks`), two radio pairs
(`IsSequential`/`IsPartitioned`, `IsTracks`/`IsCylinders`), and a per-field problem (`NameProblem`,
`RecfmProblem`, …) computed from `HostPath.DatasetNameError` and `DatasetAllocation.Problems`, each shown as a `✗`
line under its box. `CanCreate` is every problem null. Opening the form with a dataset selected prefills Type,
RECFM, LRECL and BLKSIZE from its attributes; the name starts as the filter's first qualifier followed by a dot
(`MVSCE02.`) when the filter has one. Space fields start at 5 / 5 / 20 and keep the last values the form was sent
with, for the life of the browser window. `CreateCommand` runs under `RunExclusiveAsync`: on success the form
closes, the filter is re-listed, the new dataset is selected if shown, and the status line is `✓ Created NAME.`
or `✓ Created NAME (not shown by the filter PATTERN).` A `CannotAllocate` or `InvalidRequest` answer stays in the
form's `Message` line, `✗` and the host's wording, so the values can be corrected and sent again; a connection
failure is the banner with Retry, whose retry sends the form again. `CloseFormCommand` and Escape close it.

**Keyboard.** Escape: a question, else the running operation, else a review or the form, else the window. The
window's focus memory treats the form's first box like the filter box.

**Marks.** Every outcome starts with `✓ ✗ ⚠ ⟳ –` and words; the dimmed unsupported rows are unchanged.

### 4.5 App: `HostFileMessages`

Three new kinds: `Conflict` → "Changed on the host since you downloaded it."; `AlreadyExists` → the host's
sentence; `CannotAllocate` → "The host could not allocate it: it may already exist, there may be no space, or you
may not be authorized." `DescribeUploadFailure` treats `Conflict` as nothing written, so no "may be partly
written" is added.

## 5. ETag conflict detection

### 5.1 The memory

`EtagMemory` (App, `HostFiles/`) is a thread-safe dictionary from `HostPath.ToString()` to the stamp, owned by
`HostFileAccess` and reached by every browser window of the session as `_access.Etags`. Operations:
`Remember(path, etag)` (null clears), `TryGet(path)`, `Forget(path)`, `ForgetUnder(dataset)`,
`Move(from, to)`. It is per host by construction, since an access is one profile's URL.

### 5.2 Data flow

- **Download** (`Downloads` slice): `HostFileTransfer.DownloadAsync` returns the stamp; the row's transfer
  remembers it once the file is in place. A cancelled or failed download remembers nothing.
- **Upload** (`Uploads` slice): before sending a row that replaces an existing member (the existing "already
  exists" question was answered Replace, or the sequential dataset's question was), the upload takes the
  remembered stamp and passes it as `ifMatch`. On `Conflict` the row asks `NAME changed on the host since you
  downloaded it.` with **Replace anyway** and **Skip**; Replace anyway sends again with no stamp, Skip marks the
  row `– Skipped: changed on the host`, Cancel stops the batch as today. Every successful write remembers the
  write's stamp. Verify after upload is unchanged; the stamp its read-back returns is the same one and is
  ignored.
- **Delete** forgets the path; a dataset delete forgets everything under it. **Rename** moves the entry.
- A member never downloaded or uploaded in this session window has no stamp, sends no `If-Match`, and gets today's
  behaviour exactly.

### 5.3 What is not done

No conditional reads (`If-None-Match`); no stamp in the downloaded file's name or metadata; no "Apply to all" on
the conflict question, since a conflict is per member and rare. A host that sends no `ETag` (an older z/OSMF
configuration, say) simply leaves the memory empty.

## 6. PR 1: Core and the backend

1. Core: the records, the three operations, the return types, the three error kinds, `DatasetAllocation.Problems`,
   `HostFileTransfer` carrying the stamp. Core tests for `Problems` and the transfer's stamp handling, and the
   Core fake updated.
2. Backend: §4.2, with `MvsmfErrorsTests` and new test classes `MvsmfCreateTests`, `MvsmfRenameTests`,
   `MvsmfDeleteTests` (dataset), `MvsmfEtagTests`, each named after its tag where one applies.
3. Fixtures recorded on the MVS/CE host with `tools/record-mvsmf-fixture.sh`: `create-201`,
   `create-dynalloc-500`, `delete-ds-204`, `delete-ds-missing`, `rename-member-204`, `rename-member-exists`,
   `rename-member-missing`, `rename-ds-204`, `read-etag`, `write-etag-204`, `write-412`. The README's table
   grows the same rows.
4. The compatibility log entries of §4.3.
5. `LiveMvsmfTests` gains `Creates_renames_and_deletes_a_scratch_dataset_with_etag_checks`: create
   `<USER>.LIZITEST.T<hhmmss>` as an FB 80 PDS (1 track, 1 directory block), write a member, download it and keep
   the stamp, upload with the stamp (accepted, a new stamp returned), upload with a stale stamp (`Conflict`),
   rename the member, rename the dataset to `<USER>.LIZITEST.R<hhmmss>`, delete the dataset, and confirm a list of
   it is empty. Both names are deleted in a `finally`, ignoring `NotFound`. `docs/development.md` says, beside
   `LIZTERM_MVSMF_SCRATCH_PDS`, that the live tests also allocate and delete datasets named
   `<LIZTERM_MVSMF_USER>.LIZITEST.*`.
6. `src/LizTerm.Backend.Mvsmf/CLAUDE.md`, `src/LizTerm.Core/CLAUDE.md` and `tests/CLAUDE.md` follow the code.
7. The App compiles against the new contract with the App fake (`FakeHostFileService`) implementing the three
   operations and holding a stamp per path (a counter per write), answering `Conflict` on a mismatch,
   `AlreadyExists` on a rename onto a member that exists, `NotFound` on a missing source, and `CannotAllocate` on
   a create of a name it already holds. The browser itself changes only as far as the contract forces it (the
   read and write return types); its behaviour is unchanged in PR 1.

## 7. PR 2: the browser

1. `ConfirmationRequest` input support and the strip's text box (§4.4).
2. The dataset pane's buttons and the member Rename… button; the rename, delete-dataset and create operations, as
   new partial files `MvsmfBrowserViewModel.Manage.cs` and `MvsmfBrowserViewModel.Create.cs` beside the existing
   slices; `NewDatasetFormViewModel`.
3. `EtagMemory` and the download and upload changes of §5.
4. `HostFileMessages` (§4.5).
5. App tests: `MvsmfBrowserManageTests` (rename member and dataset, each success and each failure shape, the
   selection after each, the memory moving), `MvsmfBrowserCreateTests` (prefill, every field problem, the
   remembered space values, success inside and outside the filter, `CannotAllocate` staying in the form, the
   connection-failure retry), `MvsmfBrowserEtagTests` (download then upload accepted; a change between them asks;
   Replace anyway, Skip and Cancel; a member never downloaded sends no stamp; delete and rename update the
   memory), and window tests for the strip's text box, the Escape order and the focus rules.
6. Docs: the user guide's browser section gets **Managing datasets** (New, Rename, Delete, what each asks, what
   the host can and cannot say about a failed allocation) and a paragraph under **Uploading** on the conflict
   question; its "Deleting" section covers datasets; the known-limitations line drops "create, rename or delete
   datasets". The changelog's mvsMF entry under `## Unreleased` adds "create, rename and delete datasets and
   members, and a warning before replacing a member that changed on the host since you downloaded it".
   `src/LizTerm.App/CLAUDE.md` follows the code.

Each PR gets the usual review pass before it is opened, and a hands-on pass against MVS/CE before PR 2 merges.

## 8. Testing summary

| Layer | What is pinned |
|---|---|
| Core | `DatasetAllocation.Problems` per rule; `HostFileTransfer` returning and passing the stamp |
| Backend, recorded | each fixture's mapping: 201, the one 500, 204s, 404s, 400 reason 7, the `ETag` header both ways, `If-Match` sent verbatim, 412 → `Conflict`, the rename body's shape and content type, `dirblk` only for `PO` |
| Backend, live | the scratch dataset round trip of §6.5 |
| App, view model | §7.5 |
| App, window | the strip's text box and focus, the pane buttons' enablement, Escape order |

## 9. Out of scope

Copy (no endpoint; composed as create-read-write later if wanted), `like` allocation, `BLK` as a space unit,
volume selection, the extra listing columns (Created, Referred, Volume), conditional reads, a Sign Out control,
jobs, USS, console services, and the UI items parked on #17.
