# mvsMF compatibility log

LizTerm's mvsMF Access window talks to [mvsMF](https://github.com/mvslovers/mvsmf), a z/OSMF REST subset for MVS 3.8j.
This log is the one place that records where mvsMF's documentation, its source and the build LizTerm was tested
against disagree, and what LizTerm does about each. When a newer mvsMF is available, work through it top to bottom.

## Tested build

| | |
|---|---|
| Reported version | `zosmf_full_version: 1.1.0` (`zosmf_version: 1`) |
| Host | MVS/CE, HTTPD, probed 2026-09-18; the manage operations recorded 2026-09-19; the file system routes probed and recorded 2026-09-22 |
| Source read alongside | mvsMF at commit `cf4d6d5` (1.1.1-dev, after the 1.1.0 release of 2026-09-14), `src/`, `docs/endpoints/`, `samplib/` and `CHANGELOG.md` |
| Previous baseline | `1.0.0-dev`, probed 2026-09-16; the entries it needed are under *Resolved on 1.1.0* |
| Minimum supported | 1.1.0 — see the user guide's "Signing in" |

## How to re-check a new build

1. Point `LIZTERM_MVSMF_*` at the new build (see `docs/development.md`).
2. Re-record every recorded fixture listed in `tests/LizTerm.Backend.Mvsmf.Tests/Fixtures/README.md` with
   `tools/record-mvsmf-fixture.sh` (not the one the README marks hand-written), and run
   `dotnet test tests/LizTerm.Backend.Mvsmf.Tests`. A test that now fails
   is named after the entry below whose behaviour changed.
3. Run the live tests: `dotnet test tests/LizTerm.Integration.Tests --filter "FullyQualifiedName~LiveMvsmfTests"`.
4. For each entry, probe the behaviour by hand where no test covers it, then update the entry. When a workaround is
   no longer needed, remove the code at its tag (`grep -rn "mvsMF-compat: <tag>" src`), its test, and the entry,
   and add one line under *Resolved*.

Each entry's tag appears in the backend code as `// mvsMF-compat: <tag>` and in the name of the test that pins it.
Entries marked *log only* change nothing in the code.

## Entries

### `info-requires-auth`

- **Docs:** `docs/endpoints/info.md` says `GET /zosmf/info` needs no authentication.
- **Source and observed:** 401 without credentials, with `WWW-Authenticate: Basic realm="<SMF ID>"`. This is by
  design: `samplib/mvsmfprm` says `/info` has been authenticated like every other route since mvsMF #324 and that
  the anonymous liveness probe never existed. The doc is stale.
- **LizTerm:** `/info` goes through the same authenticated path as everything else. The Test button first sends an
  unauthenticated `GET /info`; a 401 proves the URL reaches an mvsMF, so it asks for a password only then.

### `session-idle-timeout` (log only)

- **Source:** httpd expires an idle session after `SESSION_TIMEOUT` (default 30 minutes), refreshed on every
  request; there is no fixed maximum age yet (mvsMF/httpd #118).
- **LizTerm:** holds the token for the session window. A 401 from an operation is treated as an expired session:
  LizTerm signs in again with the held userid (prompting "Your mvsMF session has expired") and retries the
  operation once.

### `info-version-fields`

- **Docs:** `zosmf_version` is the API level and `zosmf_full_version` the release, as in z/OSMF.
- **Observed:** 1.0.0-dev put `1.0.0-dev` in both `zosmf_version` and `zosmf_full_version`; 1.1.0 answers
  `zosmf_version: "1"` and `zosmf_full_version: "1.1.0"`.
- **LizTerm:** reads `zosmf_full_version` first and falls back to `zosmf_version`, so both builds report their
  release.

### `attributes-header-ignored`

- **Docs and source:** `X-IBM-Attributes` is not read; every dataset listing carries the base attributes (`dsorg`,
  `recfm`, `lrecl`, `blksz`, `vol`, the dates), and the member list carries names only. z/OSMF answers names alone
  unless `X-IBM-Attributes: base` is sent.
- **LizTerm:** sends `X-IBM-Attributes: base` on every dataset list, which mvsMF ignores and z/OSMF needs.

### `paging`

- **Docs and source:** `X-IBM-Max-Items` caps a page and `start` names its first entry, **inclusive**, on the
  dataset list and the member list; a partial page carries `moreRows: true` and a complete one no `moreRows` at
  all; the member list also takes `pattern=` (`*` for any run of characters, `%` for one, folded to upper case,
  sent with `%` as `%25`); entries skipped by `start` or rejected by `pattern` are not charged against the page.
- **Observed:** 1.1.0 honours all of it (three of `SYS1.MACLIB`'s 742 members for `X-IBM-Max-Items: 3`, recorded as
  `members-maclib-page`; 1.0.0-dev ignored `start` and `X-IBM-Max-Items`).
- **LizTerm:** both lists are fetched a page at a time with **Load more** for the rest (#144; the page size is in
  the user guide). Because `start` is
  inclusive, a continued page asks for one entry more than its size and drops the repeat; a page that no longer
  begins with that name (deleted meanwhile) is cut to the page size instead. A `moreRows: true` on a list asked for
  whole is still refused as a partial answer. The member filter goes to the host as `pattern=*TEXT*` once the host
  has more members than are shown.

### `dslevel-is-a-prefix` (log only)

- **Observed:** `dslevel=MVSCE02` lists every `MVSCE02.*` dataset, as z/OSMF does.
- **LizTerm:** the browser shows everything the filter matches, as z/OSMF would.

### `authorization-is-500`

- **Source:** a refused open is 500 with category 4, rc 8, reason 0 ("LMOPEN error"), never 403.
- **Observed:** not reproduced; IBMUSER could read everything tried. (On 2026-09-16 MVSCE02's own libraries
  listed empty for IBMUSER, which looked like a hidden refusal; on 2026-09-18 a member IBMUSER wrote into
  `MVSCE02.CNTL` listed at once, so the PDS was simply empty.)
- **LizTerm:** reports that shape as "not authorized".

### `cannot-open-is-500`

- **Source:** an open that fails before any record is read or written ("Cannot open dataset", "Cannot open
  dataset member", "Cannot open dataset for writing") answers 500 with category 6, reason 3, the same shape as a
  write that failed after the host had started writing; only the message differs.
- **Observed:** not reproduced on 2026-09-18; the shape is from `dsapi.c`.
- **LizTerm:** a reason 3 whose message starts `Cannot open` is "cannot be opened", so the browser does not warn
  that the member may be partly written; any other reason 3 is a server error quoting the host's message.

### `text-body-is-latin1`

- **Docs:** silent on the body's character set.
- **Observed:** text bodies are ISO-8859-1 in both directions, whatever `charset` says: a `PUT` declaring
  `charset=UTF-8` still stores UTF-8 `¬` (`C2 AC`) as two characters, and Latin-1 `AC` round-trips as `AC`.
  Responses say `text/plain` with no charset.
- **LizTerm:** encodes to and decodes from Latin-1 in the backend. Local files are UTF-8; `TextUploadCheck` refuses
  characters above U+00FF. The file system routes behave the same, and go through the same code.

### `text-read-keeps-trailing-blanks`

- **Source:** trailing blanks are stripped from F and FB records on read.
- **Observed:** they are not; an 80-column member returns 80-character lines, sequence numbers included.
- **LizTerm:** the backend passes lines on as received; `HostFileTransfer` trims trailing blanks on download
  (`DownloadOptions.TrimTrailingBlanks`, on by default) and ignores them when verifying an upload.

### `text-write-truncates`

- **Source and observed:** an over-long line is truncated, the rest is written, and the request answers 500
  `{"category":6,"reason":3,"message":"Record truncated to the record length of the data set"}`. (1.0.0-dev
  truncated the same way and answered 204.)
- **LizTerm:** the write is partial either way, so `TextUploadCheck` refuses any line longer than the record allows
  (LRECL for F, LRECL−4 for V, BLKSIZE for U) before anything is sent, when the listing gives it that length; a
  listing without one (unknown RECFM, no LRECL) leaves the line to the host. The refusal is pinned by
  `TextUploadCheckTests`, since Core does not name mvsMF; the 500, reported as a server error with the host's
  message, by `Text_write_truncates…` in `MvsmfErrorsTests`.

### `put-json-is-rename`

- **Source and docs:** a `PUT` with `Content-Type: application/json` and `"request":"rename"` is a rename
  (`docs/endpoints/datasets/authorization.md`), not a write.
- **LizTerm:** `RenameAsync` is the one method that sends `application/json` on a `PUT`, on purpose; writes send
  only `text/plain` or `application/octet-stream`, pinned by `Put_json_is_rename_so_no_write_ever_sends_json`.

### `binary-fixed-padding` (log only)

- **Observed:** a 100-byte binary write to an FB 80 member reads back as 160 bytes, the last record zero-padded.
- **LizTerm:** the status line says so when Binary is chosen on a fixed-length dataset; it is transient, and the
  next status write replaces it.

### `record-write-broken` (log only)

- **Source:** the 1.1.0 changelog's known limitations: `X-IBM-Data-Type: record` is wrong on RECFM=V for reads and
  unimplemented for writes (mvsMF #361, #245), and the binary write path mis-frames V records (#244).
- **LizTerm:** offers Text and Binary only.

### `etag`

- **Docs and source:** `X-IBM-Return-Etag: true` returns an `ETag` on a read and on a write (none is sent without
  the header); `If-Match` on a write is checked before the member is opened, and a mismatch is 412
  `{"category":6,"rc":8,"reason":10,"message":"The resource was modified since the supplied ETag was created"}`
  with nothing written. The stamp of the member *as written* is what the PUT answers, and it is what the next
  `If-Match` must carry: the pre-save stamp fails, because the write normalises what it stores.
- **Observed (2026-09-19):** 1.1.0 does all of it. The stamp is 16 hex digits, unquoted, the same for a text and
  a binary read, and a read after a write answers the write's stamp (`read-etag`, `write-etag-204`, `write-412`;
  the live round trip checks the write-then-read equality). The stamp costs the host a second full read of the
  member (`dataset_etag` in `dsapi.c` opens and reads it), so a read that asks for it does that work twice on
  the host; and an `If-Match` on a member that no longer exists answers 412, not 404, so a member
  deleted since it was read reads as "changed". On the file system routes (2026-09-22) the 412 body is category 4,
  reason 1, with the same message (`uss-write-412`); the status, not the reason, is what LizTerm reads.
- **LizTerm:** a read asks for the stamp only when its caller may write back (`withEtag`: a download does; the
  verify read-back after an upload does not), so the host's second pass is paid once per download rather than on
  every read; every write asks, since the write's answer is the stamp the next `If-Match` needs. The value is kept
  as the host sent it, quotes or `W/` included and never parsed, so a host that quotes its entity tags gets its own
  text back; a write sends the caller's `ifMatch` as `If-Match` verbatim, and a 412 is `HostFileErrorKind.Conflict`.
  mvsMF Access sends one when it replaces a member or dataset it downloaded or wrote in the same window, and turns
  the 412 into the **Replace anyway / Skip** question.

### `create-failure-is-one-500`

- **Source and observed:** `POST restfiles/ds/{name}` answers every allocation failure — the name already exists,
  no space, a DCB the volume cannot hold, no authority — with the same
  `500 {"category":8,"rc":900,"reason":7,"message":"Dynamic allocation Error"}`, byte for byte what real z/OSMF
  sends (mvsMF #317, #329; `create-dynalloc-500` is the "already exists" case). A missing field is 400 reason 3.
- **LizTerm:** reports it as `CannotAllocate`, whose sentence names the three causes, since the host cannot.

### `rename-target-exists-400`

- **Source and observed:** a member rename (`PUT …({new})` with the JSON rename body) onto a name that exists is
  400 reason 7 "Rename target already exists" (`rename-member-exists`); a missing source is 404 reason 5. A
  *dataset* rename onto an existing name is not checked first: IDCAMS ALTER refuses it and the host answers 500
  reason 8 "Rename operation failed", the same as any other rename failure.
- **LizTerm:** the member case is `AlreadyExists`; the dataset case is a server error quoting the host.

### `hash-in-names-untested` (log only)

- **Source:** the router percent-decodes the path.
- **Observed:** not tested; no dataset or member with `#` was available.
- **LizTerm:** escapes `#` as `%23` and `%` as `%25` and sends every other name character as it is.

### `uss-limits`

- **Docs and source:** `docs/endpoints/uss/put.md` and mvsMF's `CHANGELOG.md` say a UNIX file is capped at 64 KB by
  UFSD's direct-block layout, and that text is IBM-1047 on disk.
- **Observed (2026-09-22):** the 64 KB figure is stale on the tested build (mvsMF 1.1.0 built against ufsd 1.2.2):
  a 70,000-byte `PUT` answers 204 and the file is stored whole (`uss-write-70000-204`); a 1,048,576-byte file is
  stored and read back byte-identical; 2,000,000 bytes store. The ceiling is the request body, not the file: from
  about 2.1 MB a `PUT` answers 400 `{"rc":8,"category":2,"reason":1,"message":"Failed to read request body"}` with
  nothing written (`uss-write-too-large`, a 2,200,000-byte body; a `GET` of the path answers 404 afterwards).
  Separately, "No space left on device" (500, category 8, reason 1) can answer a write of any size when the file
  system is full, and it arrives **after** a partial file has been written. The text translation changes nothing on
  the wire: the body is Latin-1 both ways, as on the dataset routes (`text-body-is-latin1`).
- **LizTerm:** `HostFileLimits.MaxUnixFileBytes` (1,048,576, the measured size the host stores and reads back
  intact, comfortably under its body ceiling) is checked before any upload request: `TextUploadCheck.RunForUnixFile`
  counts the lines' Latin-1 bytes plus one per line, and `HostFileTransfer.BinaryUploadProblem` the file's length;
  a file over the cap is refused with its size and the limit, and nothing is sent. A full file system is the
  host's answer and can leave a partial file; the App's failure wording says so. Raise the constant when the host
  grows.

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
- **Observed (2026-09-22):** an existing name (`uss-mkdir-exists`) is 400 category 4 reason 1 "File or directory
  already exists"; a missing parent (`uss-mkdir-no-parent`) is 404 category 6 reason 1 "File or directory not
  found", not the 400 the docs list.
- **LizTerm:** a 400 whose message says "already exists" is `AlreadyExists`, whose sentence for a file system path
  reads "a file or directory of that name already exists"; the missing parent maps by its 404 to `NotFound`.

### `uss-owner-blank` (log only)

- **Docs:** each listing item carries `user` and `group`, the owner and group names.
- **Observed (2026-09-22):** both are `""` on every entry of `/`, `/u/mvsce01` and `/u/mvsce02`, and on `/tmp`
  and `/www`; only `/u/ibmuser` carries `IBMUSER` and `USER`, and the recorded stat of a file under it carries
  `IBMUSER` and `ADMIN`, so the blank is per-entry rather than per-host.
- **LizTerm:** neither field is read or shown; the file pane has no owner or permissions column.

## Resolved on 1.1.0

Entries the 1.0.0-dev baseline needed and 1.1.0 does not. Each code entry was removed with its code and test on
2026-09-18; the log-only ones were simply struck.

- `basic-auth-every-request`: LizTerm now signs in once with `POST /zosmf/services/authenticate`, holds the
  `LtpaToken2` cookie, and sends it (never `Authorization`) on every request; the tag is gone from the code.
- `no-www-authenticate`: a 401 now carries `WWW-Authenticate`.
- `dataset-list-morerows-false`: `moreRows` is now absent on a complete list, as the source says. The refusal of a
  `moreRows: true` on a list asked for whole stays, under `paging`.
- `member-list-ignores-max-items`, `dataset-list-ignores-start` and, on 2026-09-18 once the browser paged, the
  `no-paging` entry that merged them: the host honours `start` and `X-IBM-Max-Items`, and LizTerm uses them
  (`paging`).
- `member-list-empty-for-missing-dataset`: a missing dataset answers 404 reason 4 and a sequential one 400 reason 1,
  as the source says; LizTerm reports not found and an invalid request.
- `missing-read-is-500`: a missing member or dataset on read answers 404 reason 5 or 4; the reason-3 special case
  is gone.
- `text-write-drops-empty-lines`: an empty line is now stored as a blank record; LizTerm sends it as it is.
- `host-date-unreliable`: the `Date` header was correct on 2026-09-18. It is the MVS clock, which was wrong before;
  LizTerm never used it.
- `docs-omit-routes`: `docs/endpoints/datasets/` now documents dataset create and delete and member delete.
