# LizTerm: mvsMF dataset browser (feature preview)

Date: 2026-09-16. Issue: #17. Status: approved in discussion on 2026-09-16; awaiting review of this text.

## 1. Purpose

File transfer today is IND$FILE typed through the 3270 session: it needs a TSO logon at READY and a quiet screen,
and it cannot list anything. [mvsMF](https://github.com/mvslovers/mvsmf) gives classic MVS 3.8j a subset of the
z/OSMF REST API, and hobbyists already use it from VS Code through Zowe. Offering the same access from inside the
terminal emulator, as an alternative to IND$FILE, is something almost no other TN3270 client does.

It ships as a **feature preview**: usable, clearly labelled, refined in later releases.

Decisions taken in discussion on 2026-09-16, each justified in the section named:

- **mvsMF is the only target.** If it also works against z/OSMF on z/OS, that is a bonus; nothing is designed or
  tested for it (§2).
- **A dataset browser window, not a second transport in the File Transfer window** (§4). The File Transfer
  window stays IND$FILE only.
- **The browser belongs to a session window**, opened from its File menu when the profile has an mvsMF URL; it
  does not need the 3270 connection to be up, and it is not openable from the picker (§3.3).
- **Preview operations**: list datasets, list members, download (including several members at once), upload
  (including as a new member), delete members. Not allocate, not delete datasets (§8).
- **USS is designed for and not built** (§3.1).
- **A new project, `LizTerm.Backend.Mvsmf`**, with an engine-neutral interface in Core (§3.1). The issue's
  suggestion of putting the client in Core was considered and rejected: the server's quirks would land there.
- **Discrepancies between mvsMF's docs and the build we test against are logged in one place,**
  `docs/mvsmf-compatibility.md`, and tagged in code, so a newer mvsMF can be re-checked quickly (§3.5).
- **Three PRs** to one preview release (§9).

## 2. What the host actually does

Probed on 2026-09-16 against Robert's MVS/CE host (`http://10.42.37.209:8080/zosmf`), which runs a pre-release
build reporting `zosmf_version: 1.0.0-dev`. The published source (v1.1.0, 2026-09-14) was read alongside; where
the build differs from the source or docs it is marked **RC**. These findings seed `docs/mvsmf-compatibility.md`.

| Area | Observed | Consequence |
|---|---|---|
| Auth | Every route, `/zosmf/info` included, needs Basic auth; the docs say `/info` needs none. **RC**: no `Set-Cookie` on Basic requests (source sends `LtpaToken2`) and no `WWW-Authenticate` on 401 (source sends it) | Send Basic on every request; ignore cookies in the preview |
| `/zosmf/info` | `{"zosmf_version":"1.0.0-dev","zos_version":"MVS 3.8j",…}` | Used by the profile editor's Test button |
| Host clock | `Date: Sat, 15 Sep 2096` | Never trust host dates for anything that matters |
| Dataset list | `GET /restfiles/ds?dslevel=`; `X-IBM-Max-Items` honoured; every item value is a string; `blksz` not `blksize`; dates `YYYY/MM/DD`; `dsntp` `PDS`/`BASIC`; dsorgs seen `PO`, `PS`, `DA`. **RC**: `moreRows` present when `false` (source omits it). **RC**: `start` is ignored — every value returns the first page (source honours it) | No paging in the preview: request the whole list without `X-IBM-Max-Items` (`SYS1.**`, 96 entries, is instant) |
| Member list | Items are `{"member":"NAME"}` only. **RC**: `X-IBM-Max-Items` ignored (742 members for a request of 3). **RC**: a dataset that does not exist, and a sequential dataset, both answer 200 with no items | Expect whole lists; an empty list is ambiguous, so existence comes from the dataset list |
| Text read | CP037; records end in LF; `¬ ¢ [ ] { } \| ~ \` round-trip. **RC**: trailing blanks not stripped (source strips for F/FB) | Strip on our side |
| Text write | CR, LF and CRLF all end a record. **RC**: an empty line is dropped; a line of one space is stored as a blank record. **RC**: a 100-character line to an LRECL 80 member is truncated to 80 and answered **204** (source answers 500). A tab is stored as EBCDIC `05` | Pre-flight checks (§5.2); blank lines sent as one space |
| Text encoding | The body is ISO-8859-1 in both directions, whatever `charset` says: UTF-8 `¬` (`C2 AC`) is stored as two characters, `Â¬`; Latin-1 `AC` is stored as CP037 `5F`. The response carries no charset. Undocumented | Encode to and decode from Latin-1 in the backend; local files are UTF-8 |
| Binary | Read returns raw records. A 100-byte write to FB 80 reads back as 160 bytes, zero-padded | Not byte-exact for fixed-length datasets; say so |
| Record mode | Read prefixes each record with a 4-byte length; writes broken (mvsMF #245) | Not offered |
| New member | `PUT …/ds/DSN(MEMBER)` to an absent member creates it: 204 | Upload-as-new is free |
| Delete member | `DELETE …/ds/DSN(MEMBER)`: 204, then 404 reason 5 | |
| Errors | Body `{"rc","category","reason","message"}`. Missing member or dataset on read: **500** reason 3 "Cannot open…". PDS read as sequential: 400 reason 1. Name too long: 400 reason 1. Authorisation refusals are 500 in the source, never 403 | Classify by `category`/`reason`, not status |
| ETag | **RC**: `X-IBM-Return-Etag` ignored (source supports it) | No conflict detection in the preview |
| Rename trap | `PUT` with `Content-Type: application/json` is a rename, not a write | Never send it on an upload; pinned by a test |
| Speed | 272 KB in 0.6 s; four parallel reads fine | Two concurrent downloads |
| USS | Source: 64 KB per-file limit, IBM-1047 | Recorded for later |
| Docs drift | README omits dataset POST/DELETE and member DELETE, which the source routes | Source is the authority |

## 3. Architecture

### 3.1 Projects and the dependency rule

- **`LizTerm.Core`** gains the namespace `LizTerm.Core.HostFiles`, BCL-only and never naming mvsMF:
  - `IHostFileService`: `GetServerInfoAsync`, `ListDatasetsAsync(pattern)`, `ListMembersAsync(dataset)`,
    `ReadTextAsync(path)` (lines), `ReadBinaryAsync(path, destination stream)`, `WriteTextAsync(path, lines)`,
    `WriteBinaryAsync(path, source stream)`, `DeleteAsync(path)`, each with a `CancellationToken`; the reads take an
    `IProgress<long>`. Text crosses the interface as lines of .NET strings, so the encoding is the backend's
    business.
  - `HostFileTransfer`: the file side — download to a temporary file and rename, trailing-blank trimming, local
    line endings, upload through `TextUploadCheck`, verify after upload.
  - `HostCredentials` and a `HostCredentialProvider` callback (`HostCredentialRequest(IsRetry)`), through which a
    service asks for credentials; the App's `CredentialHolder` (§3.2) is the provider and the only store.
  - `HostPath`: a dataset, or a dataset member, with a `Kind`; USS arrives as a new kind and factory on the same
    type rather than a new interface. Parsing folds to upper case and enforces the
    44-character dataset and 8-character member limits and the member-name rule (1-8 characters, first
    `A-Z $ # @`, rest also `0-9`).
  - `HostFileEntry`: name, kind, and nullable dataset attributes (dsorg, recfm, lrecl, blksize, volume).
  - `HostTransferMode`: `Text`, `Binary`.
  - `HostFileException` with a `HostFileErrorKind` (`NotFound`, `CannotOpen`, `NotAuthorized`, `InvalidRequest`,
    `Unauthenticated`, `CertificateRejected`, `ServerError`, `Unreachable`), the server's reason code and message
    when there are any, and, for `CertificateRejected`, the `PresentedCertificate`.
  - `TextUploadCheck` (§5.2): host-neutral MVS record rules.
- **`LizTerm.Backend.Mvsmf`** (new) depends on Core only. `MvsmfFileService` implements `IHostFileService` on an
  `HttpClient`; JSON types match what the server sends; error classification and every compat workaround live
  here and nowhere else. It is the only project that knows mvsMF exists.
- **`LizTerm.App`** names it in exactly one place, a new `HostFileServiceFactory` beside `SessionFactory`.
- **New rule for `CLAUDE.md`:** the two backends never reference each other.

### 3.2 Credentials

- `ICredentialPrompt` in `LizTerm.App/Dialogs`, shaped like `ICertificatePrompt`: `AskAsync(CredentialPromptRequest)`
  returning the userid and password or null for cancel. The request carries the profile name, URL, a prefill
  userid and whether this is a retry after a rejection.
- A `CredentialHolder`, owned by the session window, keeps the userid and password in memory. It is never logged,
  never part of an exception message or `ToString()`, never written to disk, and is cleared when the window closes.
  A .NET string cannot be reliably wiped; the user guide says so.
- The service asks through a callback on first need. A 401 asks again (the request marked as a retry) and retries
  the operation once; a cancelled prompt fails the operation as `Unauthenticated`.
- `FakeCredentialPrompt` in the App tests, beside `FakeCertificatePrompt`.

### 3.3 Session window integration

- `SessionProfile` gains optional `HostFilesUrl`, `HostFilesUserid` and `HostFilesPinnedCertificate` (Core's host-neutral
  names; Core never names mvsMF, decided while planning PR 2). The pin is separate from the 3270 `PinnedCertificate`.
- When `HostFilesUrl` is set, **mvsMF Browser...** appears in the File menu, native macOS and in-window alike. It has
  no keyboard shortcut: the app's menu rule keeps shortcuts off every menu outside Edit (bar Switch Session and
  Minimize), because on macOS a menu shortcut is taken before the 3270 screen sees the key (decided with Robert
  while planning PR 2, 2026-09-16).
- One `MvsmfBrowserWindow` per session window, owned by it and shown without blocking it; the menu item again
  fronts it. The session window owns the `CredentialHolder`; closing it closes the browser and forgets the
  credentials.
- The browser stays above its session window and follows that window's Keep on Top. It is not listed in the Window
  menu in the preview; **mvsMF Browser...** is the way back to it (decided 2026-09-16; a nested Window-menu entry can
  come later).

### 3.4 Transport security

The URL's scheme decides. `http` goes direct. `https` validates through the handler's
`ServerCertificateCustomValidationCallback` with the existing pin model in `LizTerm.Core.Security`; an untrusted
certificate opens the existing certificate window, and "Remember" stores `HostFilesPinnedCertificate`. A pin trusts
exactly the pinned leaf while it is in date (decided 2026-09-16 in the PR 1 review); it is not a trust store, so a
pinned chain does not extend to other leaves. No warning is shown for `http`; the user guide suggests a TLS reverse
proxy.

### 3.5 Compatibility log

`docs/mvsmf-compatibility.md` is the single home for differences between mvsMF's docs, its source and the build we
test against. It records the tested build and, per behaviour: what the docs or source say, what was observed, and
our workaround. Every workaround in code carries a matching tag, for example
`// mvsMF-compat: member-list-ignores-max-items`, and the test that pins it carries the same name. A workaround
that lives in Core because it suits any host (trailing-blank trimming, the pre-flight checks) carries no tag, so Core
never names mvsMF; its log entry names the Core member instead. The log is added to the documentation table in
`CLAUDE.md`.

## 4. The browser window

- **Title** "mvsMF Browser — {profile} (Preview)". A one-line strip under the filter says the feature is a preview and
  links to the user guide.
- **Filter** field defaulting to `{userid}.**`, with **List**.
- **Column headers are in capitals**, as ISPF shows them: `NAME`, `DSORG`, `RECFM`, `LRECL` in the dataset list,
  and `MEMBER`, `STATUS` in the member list (Robert, 2026-09-16, after the mockup review).
- **Left pane**: datasets with NAME, DSORG, RECFM, LRECL. VSAM and `DA` entries are dimmed *and* suffixed
  "(not supported)", so dimming never carries the meaning alone. The whole list is fetched at once; there is no paging in the preview (§2).
- **Right pane**: for a PDS, its members with multi-select and a client-side type-ahead filter; for a sequential
  dataset, a line saying actions apply to the dataset itself.
- **Bottom bar**: **Text / Binary** (Binary preselected for `RECFM=U`, overridable), **Download…**, **Upload…**,
  **Delete…**, and a progress area.
- **Download**: one item opens a save dialog suggesting the member name plus an editable extension (`.txt` for
  text, none for binary). Several members open a folder picker and run two at a time; each row shows status as an
  icon plus a word (✓ Done, ✗ Failed, ⟳ Running). An existing local file asks Replace / Skip / apply to all.
  **Cancel** stops queued items and aborts running ones.
- **Upload**: to a PDS, one or more local files; the member name is the file name up to the first dot, upper-cased
  and checked; an invalid name is shown for editing before anything is sent; an existing member asks Replace /
  Skip / apply to all. To a sequential dataset, one file, with a confirmation that its contents are replaced.
- **Delete**: members only; the confirmation names them and its button reads **Delete N members**; the member list
  is fetched again afterwards.
- **Errors**: per item in its row or the progress area, in plain words ("Member not found", "Not authorized or
  cannot open", "Server error (reason 3)"). A connection failure shows once, with **Retry**.
- **Keyboard**: every control labelled and reachable; Tab moves between panes, Enter downloads, Delete deletes.

## 5. Data flow

### 5.1 Download

The response is streamed (`ResponseHeadersRead`) to a temporary file beside the destination and renamed into place
on success; failure or cancellation deletes it. Progress is bytes received on an indeterminate bar, since the
server sends no `Content-Length`. In text mode, trailing blanks are stripped by default (log entry
`text-read-keeps-trailing-blanks`; a checkbox keeps them) and lines end LF on macOS and Linux, CRLF on Windows. In
binary mode against a fixed-length dataset, the bar notes "padded to whole records".

### 5.2 Upload pre-flight

`TextUploadCheck` takes the local file and the target's attributes from the dataset list and runs before any
request:

1. **Encoding.** Read as UTF-8, honouring a BOM. Invalid UTF-8 blocks the upload, suggesting Binary. Any character
   above U+00FF blocks it, since CP037 covers exactly Latin-1; the first few offenders are listed with line
   numbers.
2. **Line length.** Usable length is LRECL for F, LRECL−4 for V, BLKSIZE for U. A longer line blocks the upload;
   the first few line numbers and lengths are listed. No "truncate anyway" in the preview (log entry
   `text-write-truncates-silently`).
3. **Tabs** are reported, with **Expand tabs (8)** on by default. Unexpanded, a tab is stored as EBCDIC `05`.
4. **Blank lines** are sent as a single space, which the host stores as a blank record (tag
   `text-write-drops-empty-lines`; confirmed on the host 2026-09-16).

### 5.3 Upload

The backend sends the transformed text with `Content-Type: text/plain` and an explicit `Content-Length`; never
`application/json` (tag `put-json-is-rename`). Binary sends `X-IBM-Data-Type: binary` with
`application/octet-stream`. **Verify after upload** is on by default: a text upload is read back and compared line
by line after trimming trailing blanks, and a mismatch reports "Uploaded, but the host copy differs at line N". A
failed upload may leave the member partly written, since the server does not roll back; the error says so.

### 5.4 Timeouts and cancellation

10 s to connect and 30 s without data; no overall timeout. Every operation takes a `CancellationToken`, used by the
window's Cancel and by window close.

## 6. Profile editor

A new **mvsMF (Preview)** group: **URL** (absolute `http`/`https`; an empty path becomes `/zosmf`), **Userid**
(optional, upper-cased, 1-8 characters) and **Test**, which calls `/zosmf/info` and shows "Connected: mvsMF
{version} on {zos_version}" or the error. Credentials entered for Test are used once and forgotten. The new
profile properties are optional; existing profiles load unchanged and a profile without a URL behaves as today. An
older LizTerm drops the fields when it saves the profile; the changelog notes it.

## 7. Testing

- **`LizTerm.Core.Tests`**: `HostPath` parsing and validation; every `TextUploadCheck` outcome, usable lengths for
  F, V and U, tab expansion, BOM, invalid UTF-8.
- **`LizTerm.Backend.Mvsmf.Tests`** (new): a fake `HttpMessageHandler` serving **recorded exchanges** from fixture
  files, trimmed from real responses on the host — the HTTP counterpart of the replay fixtures. Each compat
  workaround has a test named after its tag. Pinned: no `application/json` on upload; error classification by
  reason; 401 → prompt → one retry; no `X-IBM-Max-Items` or `start` sent; Latin-1 both ways; trailing-blank trimming; blank line
  sent as a space; temporary file removed on cancel; credentials never in exception text.
- **`LizTerm.App.Tests`**: the browser view model against `FakeHostFileService` and `FakeCredentialPrompt` —
  selection, the multi-item queue, cancel, Replace/Skip, delete confirmation, the menu item present only with a
  URL. `HostFileServiceFactory` is the one place the App tests name the new backend (`tests/CLAUDE.md`).
- **`LizTerm.Integration.Tests`**: live tests that skip unless `LIZTERM_MVSMF_URL`, `LIZTERM_MVSMF_USER` and
  `LIZTERM_MVSMF_PASSWORD` are set; they list datasets, then create, read back, verify and delete a scratch member
  in `LIZTERM_MVSMF_SCRATCH_PDS`.
- **Manual QA** on Robert's host before the last PR merges, published as a checklist like the 0.5.0 sheet.

## 8. Out of scope

Dataset allocation and deletion, rename, jobs, console, USS (designed for only), ETag conflict detection, record
mode, member statistics, cookie sessions, opening the browser from the picker, any change to IND$FILE transfer.

## 9. Delivery

1. **PR 1**: Core `HostFiles` types and `TextUploadCheck`, `LizTerm.Backend.Mvsmf` with tests and fixtures, the
   live integration tests, `docs/mvsmf-compatibility.md`, the `CLAUDE.md` rule and a
   `src/LizTerm.Backend.Mvsmf/CLAUDE.md` of pitfalls.
2. **PR 2**: profile fields and editor group, `ICredentialPrompt` and its window, `CredentialHolder`,
   `HostFileServiceFactory`, the browser window and view model, menu items, App tests.
3. **PR 3**: `docs/user-guide.md` section, `CHANGELOG.md` preview entry under `## Unreleased`,
   `docs/architecture.md`, `docs/development.md` (live-test variables), one `README.md` feature line, and the
   manual QA pass.

All three land before the release that carries the preview.
