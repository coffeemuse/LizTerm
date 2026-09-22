# LizTerm: mvsMF Access — a USS tab, for the host's UNIX file system

Design for #176, agreed 2026-09-22. It builds on the pane-pattern spec of 2026-09-19
(`2026-09-19-mvsmf-access-pane-pattern-design.md`, whose §11 recorded the layout this spec now fills in), the manage
slice spec of the same day (the runner, the confirmation strip, the stamp memory) and the text viewer spec of
2026-09-22. It is the first work under #176; #17 keeps jobs, the console and its parked UI items.

## 1. Purpose

mvsMF Access browses datasets and members. mvsMF also serves the UNIX file system at `/zosmf/restfiles/fs`, with
list, read, write, create and delete, the same ETag optimistic locking as the dataset routes, and none of it is
reachable from LizTerm. This slice:

1. adds a **USS** tab beside **Datasets** in the mvsMF Access window: a path row, a Directories pane and a Files
   pane, each with its own view model and verbs, over the window's one status line, progress bar, Cancel, error
   banner and confirmation strip (§4.5, §4.6);
2. gives Core a UNIX path kind and the listing and creation verbs the host-file contract lacks, and the backend
   the routes behind them (§4.1, §4.2);
3. moves the browser's operation runner into one shared object so both tabs run one operation at a time through
   the same status line (§4.4);
4. records what mvsMF does for each route in the compatibility log, pinned by fixtures and tests (§4.3).

## 2. What the host does (mvsMF 1.1.0 on the MVS/CE test host; source at `cf4d6d5`, `src/ussapi.c` and `docs/endpoints/uss/`)

| Operation | Request | Success | Failures |
|---|---|---|---|
| List | `GET restfiles/fs?path=<dir>`, optional `X-IBM-Max-Items` (default 1000, 0 = all) | 200, `items` of `name`, `mode`, `size`, `user`, `group`, `links`, `mtime` (ISO 8601 Z), `inode`; `returnedRows`, `totalRows`; `moreRows: true` only when cut short | 404 reason 1 "File not found"; a file path answers a one-item stat listing with the full path as `name` |
| Read | `GET restfiles/fs/<path>`, `X-IBM-Data-Type` text (default) or binary, `X-IBM-Return-Etag`, `If-None-Match` | 200, the bytes (text: IBM-1047 translated to ASCII, LF ended) | 404 not found; 400 "Is a directory" |
| Write | `PUT restfiles/fs/<path>` with `text/plain` or `application/octet-stream`, `If-Match`, `X-IBM-Return-Etag` | 204, creating the file when absent; `ETag` of the state written | 412 on a stale `If-Match` (including a file no longer there); 400 for a directory; 500 "No space left on device" past 64 KB, after writing what fit |
| Create | `POST restfiles/fs/<path>`, `application/json`, `{"type":"directory"}` or `{"type":"file"}`, optional `mode` | 201, no body | 400 for an existing name, a missing parent or a path too long |
| Delete | `DELETE restfiles/fs/<path>`, `X-IBM-Option: recursive` for a non-empty directory | 204 | 404 not found; 400 for a non-empty directory without the option |

Facts to design around, all probed on 2026-09-22:

- **There is no rename.** `ussapi.c` has the five handlers above and nothing else; mvsMF's own `docs/uss-spec.md`
  lists rename and move as unbuilt. A `PUT` with a JSON body is a utility request (`chtag` only), never a rename.
- **A listing cannot be continued.** `X-IBM-Max-Items` cuts it and `moreRows` says so, but there is no `start=`
  on this route, so a cut listing cannot be resumed.
- **Owner and group are blank** on every entry the test host lists except `/u/ibmuser` (`IBMUSER`, `USER`).
- **There are no `.` or `..` entries**; `totalRows` excludes them too.
- **Files are capped at 64 KB** by UFSD's direct-block layout. The host writes what fits, then answers 500.
- **A path of 252 characters or more** overflows the host's path buffer (`uss_build_path`, `UFS_PATH_MAX`) and is
  answered badly; the local rule stops short of it.
- **The wire encoding is the dataset side's.** Text mode translates between IBM-1047 on disk and the same ASCII
  the dataset routes send, so `text-body-is-latin1` holds here too; only the host's code page differs, and that is
  invisible to LizTerm.
- **The test host's file system is nearly empty**: `/` holds `tmp`, `u` and `www`; `/u` the three home
  directories; no files anywhere. Fixtures, the live lane and the hands-on pass seed their own.

## 3. Decisions

- **Navigation is one level, anchored on the path row.** The path box names the current directory; the
  Directories pane lists its subdirectories, the Files pane its files. Enter or a double-click on a directory
  descends, **↑ Up** ascends, **Go** lists a typed path. No tree, no mixed pane.
- **An oversize upload is refused locally**, before any request: the limit lives in one Core constant and the
  check names the file and the limit. Nothing is ever left partly written by LizTerm.
- **Columns are NAME, SIZE and MODIFIED for files, NAME and MODIFIED for directories.** Mode, owner, group, links
  and inode are not shown: nothing in this slice acts on them, and a permissions column beside a blank owner
  raises a question the window cannot answer. The mode string's first character decides which pane an entry
  lands in.
- **The existing contract is extended, not doubled.** `IHostFileService` gains the two USS verbs it lacks and the
  others take a UNIX path unchanged; there is no second interface.
- **The runner moves out of the dataset view model into a shared object**, and the USS tab gets its own view
  model over it. The dataset view model keeps its public surface by forwarding, so the window's bindings and the
  existing tests do not change.
- **A refused upload is named on the status line, not as a row**: the Files pane lists only what the host lists.
- **The Files pane's title is the current path**, the same text as the box.
- **No Rename… anywhere on the USS tab** (the host has none); no New… on the Files pane (Upload creates files).
- **The Transfer drop-down on the USS tab holds Text, Binary and Verify after upload only.** A byte-stream file
  has no records to pad, so Trim trailing blanks does not apply, and IBM-1047 has a tab, so tabs are sent as they
  are.
- **The whole directory is listed in one answer** (`MaxItems` 0). A directory the host still cuts short says so
  on the status line, with no Load more.
- **The start path is `/u/<userid>` in lower case**, listed the first time the tab is shown. A host without it
  answers "File not found" on the status line and the user types another.
- **Two PRs**, as for the manage slice: Core, backend, fixtures and the live lane first; the runner, the tab, the
  view model and the docs second. The second gets Robert's hands-on pass against MVS/CE, with seeded files, as
  its merge gate.

## 4. Architecture

### 4.1 Core: the contract

`HostPath` gains the kind its doc comment promised:

```csharp
public enum HostPathKind { Dataset, Member, Unix }

public static HostPath ForUnix(string path);          // ArgumentException with UnixPathError's message
public static string? UnixPathError(string path);     // null when acceptable
public string? UnixPath { get; }                      // null for a dataset or member
public HostPath? Parent { get; }                      // /u/ibmuser/notes → /u/ibmuser; null at the root
public string Name { get; }                           // the last segment; "/" at the root
public HostPath Child(string name);                   // ForUnix(UnixPath + "/" + name), root-aware
```

`UnixPathError` refuses an empty path, one that does not start with `/`, an empty segment other than the root's
(`/u//x`, a trailing `/` except on `/` itself), a `.` or `..` segment, a control character, and a path longer than
`MaxUnixPathLength` (251, one under the host's overflow). Case is kept as typed: `/u/IBMUSER` and `/u/ibmuser` are
two paths, as they are on the host. `ToString()` returns the path. `TryParse` reads a leading `/` as a UNIX path
and otherwise behaves as today, so one entry point serves both kinds. `Dataset` and `Member` are null on a UNIX
path; `WithMember` throws for one.

`HostFileEntry` gains two kinds and a second attribute record:

```csharp
public enum HostFileEntryKind { Dataset, Member, Directory, File }

public sealed record FileAttributes(long Size, DateTimeOffset? Modified);

public sealed record HostFileEntry(string Name, HostFileEntryKind Kind,
    DatasetAttributes? Attributes = null, FileAttributes? File = null);
```

`IHostFileService` gains two verbs and widens the rest:

- `Task<HostFileListing> ListDirectoryAsync(HostPath directory, HostListRequest request, CancellationToken)`:
  the entries of one directory, directories and files together, in the host's order, each a `Directory` or `File`
  entry with `File` set. A missing path is `NotFound`; a file path is `InvalidRequest` ("… is a file, not a
  directory", detected by the backend from the stat-shaped answer). `MaxItems` means what it does elsewhere; this
  listing never hands back a continuation, and a host that cut it short is reported through a new
  `HostFileListing.Truncated` flag (false on every dataset and member listing), with `IsComplete` now
  `Continuation is null && !Truncated`, so nothing changes for the callers that exist.
- `Task CreateDirectoryAsync(HostPath directory, CancellationToken)`: creates one directory under an existing
  parent. An existing name is `AlreadyExists`; a missing parent is `NotFound`.
- `ReadTextAsync`, `ReadBinaryAsync`, `WriteTextAsync`, `WriteBinaryAsync` and `DeleteAsync` accept a UNIX path
  with their present meaning; `DeleteAsync` on a directory removes everything under it, as it does for a dataset.
  A read of a directory is `InvalidRequest`.
- `ListMembersAsync`, `CreateDatasetAsync` and `RenameAsync` throw `ArgumentException` for a UNIX path before any
  request, as `ListMembersAsync` does today for a member path.

`HostFileLimits` is new, one static class holding `MaxUnixFileBytes = 65_536` with a doc comment naming the
UFSD reason and the compatibility entry. `TextUploadCheck.Run` gains an overload for a byte-stream target:

```csharp
public static TextUploadResult Run(ReadOnlySpan<byte> file, long? maxBytes, TextUploadOptions? options = null);
```

It applies the encoding rules (UTF-8 in, Latin-1 repertoire), no record length, and counts the Latin-1 bytes of
the lines plus one LF each against `maxBytes`, reporting `FileTooLarge` (a new `TextUploadProblemKind`) as an
error naming the size and the limit. Tabs are neither warned about nor expanded when `ExpandTabs` is false, which
is what the USS tab passes. `HostFileTransfer.CheckTextFile` gains the matching overload, and
`UploadBinaryAsync` takes an optional `maxBytes` and refuses a source longer than it with a `HostFileException`
of kind `InvalidRequest` before opening a request, so the binary path is checked in the same place as the text one.

### 4.2 Backend: `MvsmfFileService`

- `ListDirectoryAsync` sends `GET restfiles/fs?path=<escaped>` with `X-IBM-Max-Items` from the request. Each
  item becomes a `Directory` entry when `mode` starts with `d`, otherwise a `File` entry, with `size` and `mtime`
  (parsed as ISO 8601; unparseable is null). A one-item answer whose `name` is the full path is the stat shape:
  the target was a file, and the call fails `InvalidRequest`. `moreRows: true` is returned as a listing with
  `Truncated` set and no continuation, never as a partial answer refused: the `Page` rule that refuses `moreRows`
  on a whole-list request applies to the dataset routes, which can be continued, not to this one.
- Reads, writes and deletes take `restfiles/fs/<path>`, the path escaped segment by segment by `EscapeUnixSegment`
  (RFC 3986 unreserved characters and `/` kept, everything else percent-encoded, so a name with a space, `#` or
  `%` round-trips). Headers, `If-Match`, `X-IBM-Return-Etag`, the Latin-1 text form and the stamp handling are the
  dataset side's, through the same `PutAsync` and `ReadAsync` paths, chosen by `path.Kind`.
- `CreateDirectoryAsync` posts `{"type":"directory"}`. The host's 400 for an existing name maps to
  `AlreadyExists`; its 400 for a missing parent maps to `NotFound`; both by the message text, since the reason
  code is the same (`uss-create-errors-400`).
- `DeleteAsync` on a UNIX path always sends `X-IBM-Option: recursive`, so a directory is removed with its
  contents in one request; the App's question carries that fact.
- `RenameAsync` refuses a UNIX path with an `ArgumentException` ("The host cannot rename a UNIX file."), so the
  contract's promise that a rename never reaches the host as a write holds.
- `MvsmfErrors` learns the USS shapes: 404 category 6 reason 1 is `NotFound`; 400 category 2 reason 1 is
  `InvalidRequest` with the host's sentence ("Is a directory", "Path name too long"); the create 400s as above.

### 4.3 Compatibility log

New entries, each tagged `// mvsMF-compat: <tag>` at the code and pinned by a test named after it:

- `uss-limits` becomes a code entry: the 64 KB cap is `HostFileLimits.MaxUnixFileBytes`, checked before any
  upload; the entry keeps the note that USS create answers 400 for an existing file.
- `uss-list-no-continuation`: the file system listing has `X-IBM-Max-Items` and `moreRows` but no `start=`, so
  LizTerm asks for the whole directory and reports a cut listing without a Load more.
- `uss-create-errors-400`: an existing name and a missing parent are both 400 reason 1, told apart by the
  message.
- `uss-stat-for-file-path`: a directory listing of a file path answers a one-item stat listing, which LizTerm
  reads as `InvalidRequest`.
- `uss-owner-blank` (log only): owner and group are empty strings on the tested build for all but one entry.

The `text-body-is-latin1` entry gains a sentence saying the USS routes behave the same.

### 4.4 App: the shared runner

`BrowserOperations` (new, `ViewModels/`) is what `MvsmfBrowserViewModel` carries today for running one operation
at a time: `IsBusy`, `IsIdle`, `StatusText`, `ErrorText`, `HasError`, `CanRetry`, `Confirmation`,
`HasConfirmation`, `CancelCommand`, `RetryCommand`, `RunExclusiveAsync`, `AskAsync`, `DropRetry` and the pin-save
warning. The dataset view model still owns the connection and disposes the runner with it. It raises `Idle` when the busy flag drops (what `SignalIdle` serves today) and
`StateChanged` so each view model can notify its commands. The dataset view model takes one in its constructor,
forwards the properties and commands under their present names and binds nothing differently, so
`MvsmfBrowserWindow.axaml`'s bindings and every existing test stay as they are. The window's bottom bar, banner
and confirmation strip bind to the same names, now reaching the runner through the forwarding.

### 4.5 App: the window

- Under the preview strip the window is a `TabControl` with **Datasets** and **USS**. The Datasets tab holds the
  filter row and the two panes exactly as today. The USS tab holds the path row (**Path**, a monospace box, **Go**,
  **↑ Up**) and two `BrowserPane`s on a splitter, `DirectoriesPane` and `FilesPane`.
- The bottom bar, progress, Cancel, the error banner and the confirmation strip are outside the `TabControl` and
  serve both tabs. `ConfirmInputBox`'s `MaxLength` becomes `HostPath.MaxUnixPathLength`, since the strip now also
  asks for a directory name; each question's rule still bounds what it accepts.
- The USS tab lists its start path the first time it is selected (`UssBrowserViewModel.EnsureListedAsync`), never
  at window open. Switching tabs during an operation is allowed: the runner owns the operation, and its result
  lands on the status line whichever tab is in front.
- Keys on the USS tab, mirroring §7 of the pane-pattern spec: Enter or a double-click in the Directories list
  descends; Enter or a double-click in the Files list downloads; Delete or Backspace deletes the selection in
  the focused list; ⌘R / Ctrl+R refreshes; ⌘N / Ctrl+N opens New directory; Enter in the path box is Go;
  ⌘⏎ / Ctrl+Enter in the Files list is View; Escape's ladder is unchanged. Each list has a context menu with its
  toolbar's verbs, and Refresh.
- The viewer window and the new-dataset dialog stay owned by the window as today; the viewer is shared, so a USS
  file and a member never open two.
- `SessionWindow` and `HostFileAccess` do not change: the tab lives inside the one mvsMF Access window.

### 4.6 App: `UssBrowserViewModel`

State: `Path` (the box's text), `Current` (the listed `HostPath`, null until a listing lands), `Directories` and
`Files` (`DirectoryRow`, `FileRow`: name, size, modified, `HostPath`, a `Status` on the file row), the selected
directory, the selected files (pushed by the window as the member selection is), `Mode`, `VerifyUploads`, the
footers, and `Truncated` (the host cut the listing).

- **List** (`GoCommand`): `UnixPathError` on the box's text is a status line and no request. Otherwise
  `⟳ Listing /u/ibmuser…`, then `ListDirectoryAsync(path, HostListRequest.All)`; on success `Current` and the box
  take the listed path, the rows are filled, the selection cleared, the footers raised once, and the status line
  reads `✓ Listed /u/ibmuser · 2 directories, 3 files` (with ` · more on the host` when cut). On `NotFound` the
  box keeps its text, `Current` and the rows keep the last good listing, and the status line carries the host's
  sentence. `Refresh` lists `Current` again and keeps the selection by name.
- **Descend** (`OpenDirectoryCommand`, exactly one selected directory): sets the box to `Current.Child(name)` and
  lists. **Up** (`UpCommand`, off at `/`): sets the box to `Current.Parent` and lists.
- **New…** (`NewDirectoryCommand`, needs `Current`): asks in the confirmation strip with a text box, the rule
  being a single segment (no `/`, not `.` or `..`, no control character, not a name already listed, and such that
  `Current.Child(name)` passes `UnixPathError`); on Create,
  `CreateDirectoryAsync(Current.Child(name))`, then a re-list and `SelectDirectoryRequested` so the window selects
  it. `AlreadyExists` is the status line and the rows are re-listed anyway, since the host has it.
- **Delete…** on a directory: one question, `Delete directory drafts and everything in it?`, the primary button
  carrying the name; then `DeleteAsync`, the row dropped at once, a re-list. **Delete…** on files: one question
  naming up to five, one `DeleteAsync` per file in order, each row's status updated, the rows dropped as they go,
  a cancelled batch stops between files and says how many went. A `NotFound` drops the row with the host's
  sentence, as the manage slice does for a dataset.
- **Download…** (one or more selected files): the picker asks for a folder when several are selected or a file
  name for one, the local name is the host name with case kept, and `HostFileTransfer.DownloadAsync` runs with
  `DownloadOptions(Mode, TrimTrailingBlanks: false)`, the `.part` flow, two at a time, each row's status the
  member download's words. The stamp is remembered in `EtagMemory` under the path.
- **Upload…** (needs `Current`): the picker returns local files; each goes under its own name in `Current`. Before
  any request each file is checked: Text by the byte-stream `TextUploadCheck` with `HostFileLimits.MaxUnixFileBytes`
  and `ExpandTabs: false`, Binary by size; a file that fails is set aside. A name already listed asks before it is
  replaced (`Replace README.txt?`, with Apply to all as the PDS upload offers); a remembered stamp goes out as
  `ifMatch`, and a 412 asks the conflict question (Replace anyway / Skip). A file that replaced a listed one puts the
  upload's words and, with Verify, the read-back result on that row; a file new to the directory has no row until
  the re-list after the batch lands, so its result lives on the status line. The status line sums up: `✓ 3 files sent`, or `⚠ 2 of 3 files sent · big.bin not sent: larger than 64 KB`, or the
  failure that stopped the batch, with up to five names as `Delete` names them.
- **View** (exactly one selected file): `MvsmfViewerViewModel` on `ReadTextAsync(path)`, always Text, `withEtag`
  false, through the window's one viewer, as for a member.
- **Transfer drop-down**: `Mode` and `VerifyUploads` on this view model, independent of the Datasets tab's. Mode
  starts Text and is never changed by a selection, since a UNIX file carries no record format.
- **Footers**: `2 directories · 1 selected`; `3 files · 1 selected`; empty until a listing lands; `No directories`
  and `No files` for an empty directory once listed.
- Every command is off while the runner is busy or a question is up, through `StateChanged`.

### 4.7 App: `HostFileMessages`

New arms, in the voice of the existing ones: `NotFound` on a UNIX path is `File not found: /u/nobody`;
`InvalidRequest` quotes the host's sentence (`/u is a directory`, `Path name too long`); `AlreadyExists` on a
create reads `A file or directory named drafts already exists`; a `FileTooLarge` check problem reads
`big.bin is 71,204 bytes; the host holds at most 65,536`. `DescribeUploadFailure` adds the may-be-partly-written
clause for a UNIX file as it does for a sequential dataset.

## 5. Testing

- **Core.Tests:** `HostPathTests` for every `UnixPathError` rule, `ForUnix`, `TryParse` on a leading `/`,
  `Parent`, `Name`, `Child`, the root, `WithMember` refusing; `TextUploadCheckTests` for the byte-stream overload
  (the cap counted in Latin-1 bytes plus LF, tabs kept, the `FileTooLarge` message); `HostFileTransferTests` for
  the binary cap and a download with trimming off.
- **Backend.Mvsmf.Tests:** one recorded fixture per row of §2's table and per failure shape (`uss-list-root`,
  `uss-list-empty`, `uss-list-missing`, `uss-list-truncated`, `uss-stat-file`, `uss-read-text`, `uss-read-binary`,
  `uss-read-missing`, `uss-read-directory`, `uss-write-204`, `uss-write-etag-204`, `uss-write-412`,
  `uss-write-too-large`, `uss-mkdir-201`, `uss-mkdir-exists`, `uss-mkdir-no-parent`, `uss-delete-204`,
  `uss-delete-missing`), recorded with `tools/record-mvsmf-fixture.sh` against a scratch tree under
  `/u/ibmuser/liztest` and listed in the fixtures README; request-shape tests for the query string, the escaped
  path, `X-IBM-Option: recursive`, the create body, and the headers a read and a write send; the compat-tagged
  tests of §4.3; `RenameAsync` and the dataset verbs refusing a UNIX path.
- **Integration.Tests:** one `LiveMvsmfTests` case that creates `/u/ibmuser/liztest-<stamp>`, writes a text file
  and a binary file, lists and finds both with their sizes, reads them back equal, sees a 412 on a stale stamp,
  gets the local refusal for a 65,537-byte file, deletes the tree recursively and checks the parent no longer
  lists it. `LIZTERM_MVSMF_SCRATCH_DIR` names the parent, defaulting to `/u/<user>`.
- **App.Tests:** `FakeHostFileService` grows a directory tree keyed by path (`AddDirectory`, `AddFile`, the
  `Calls` log with `listdir:<path>`, `mkdir:<path>`, and the read, write and delete keys already used, failing the
  way the host does); `BrowserOperationsTests` for the runner alone; every existing browser test unchanged and
  green through the forwarding; `UssBrowserViewModelTests` for listing, the start path, a refused path, a missing
  path keeping the last listing, descend, Up and Up at the root, Refresh keeping the selection, New with an
  existing name, Delete on a directory and on files with a cancel between files, Download naming and the stamp
  memory, Upload with a replace question, a conflict, a refused oversize text file and binary file, Verify, View,
  and the mode drop-down staying put; `MvsmfBrowserWindowTests` for the tab, the path row's keys, the context
  menus, the lists' keys, the confirmation strip serving a USS question, and the status line after an operation
  started on the other tab.
- The zero-warning check before either PR is called done.

## 6. PR 1: Core, backend, fixtures, live lane

1. Core: `HostPath` (§4.1), `HostFileEntry`, `HostFileLimits`, `TextUploadCheck` and `HostFileTransfer`
   overloads, the contract's new verbs and the `ArgumentException`s, with tests.
2. Fixtures recorded against MVS/CE, the README rows, and the scratch tree removed afterwards.
3. Backend (§4.2) with `MvsmfErrors`, `MvsmfJson` types for the listing, tests named after the compat tags.
4. Compatibility log entries (§4.3), the tested-build table's probe date, the backend's `CLAUDE.md`.
5. The App fake and Core.Tests fake grow the UNIX contract so App still compiles and its suite stays green; no UI.
6. The live-lane test, run against MVS/CE, host left clean.
7. CHANGELOG line under `## Unreleased` deferred to PR 2, which is what a user sees.

## 7. PR 2: the tab

1. `BrowserOperations` extracted, the dataset view model forwarding, every existing test green.
2. `UssBrowserViewModel` and its rows, test-first against the fake (§4.6).
3. The window: `TabControl`, the path row, the two panes, keys, context menus, the strip's `MaxLength` (§4.5).
4. `HostFileMessages` arms (§4.7).
5. Docs: user guide **USS** subsection under mvsMF Access (browsing, the cap, no rename, Text and Binary meaning
   the same as for datasets); CHANGELOG under `## Unreleased`; `src/LizTerm.App/CLAUDE.md` and `tests/CLAUDE.md`
   notes; the guide HTML regenerated with `LIZTERM_UPDATE_DOCS=1`.
6. Robert's hands-on pass against MVS/CE with seeded files, as the merge gate: descend and Up, a typed missing
   path, New and Delete on a directory, upload with a replace and a refused oversize file, download, View, a
   real 412, a question answered while the Datasets tab is in front.

## 8. Out of scope

Rename and move (the host has none), `chmod`, `chown`, `chtag`, symlinks, in-place editing, copying between USS
and datasets, a New file verb, a tree view, a filter on either pane, paging a cut listing, and owner or
permission columns. Jobs and console services stay on #17.
