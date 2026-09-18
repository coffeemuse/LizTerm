# mvsMF compatibility log

LizTerm's dataset browser talks to [mvsMF](https://github.com/mvslovers/mvsmf), a z/OSMF REST subset for MVS 3.8j.
This log is the one place that records where mvsMF's documentation, its source and the build LizTerm was tested
against disagree, and what LizTerm does about each. When a newer mvsMF is available, work through it top to bottom.

## Tested build

| | |
|---|---|
| Reported version | `zosmf_full_version: 1.1.0` (`zosmf_version: 1`) |
| Host | MVS/CE, HTTPD, probed 2026-09-18 |
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

### `no-paging`

- **Docs and source:** `start` names the first item of a page and `X-IBM-Max-Items` its size, on the dataset list
  and the member list; a partial page answers `returnedRows` and `moreRows: true`.
- **Observed:** 1.1.0 honours both (5 datasets from `SYS1.PARMLIB`; 3 of `SYS1.MACLIB`'s 742 members). 1.0.0-dev
  ignored both.
- **LizTerm:** still asks for whole lists, with neither `start` nor `X-IBM-Max-Items` (`SYS1.**`, 97 entries,
  arrives at once). A `moreRows: true` on either list is refused as a partial answer, since none was asked for.
  Paging is #144.

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
  characters above U+00FF.

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
- **LizTerm:** writes send only `text/plain` or `application/octet-stream`.

### `binary-fixed-padding` (log only)

- **Observed:** a 100-byte binary write to an FB 80 member reads back as 160 bytes, the last record zero-padded.
- **LizTerm:** the browser's bottom bar says so while Binary is chosen for a fixed-length dataset.

### `record-write-broken` (log only)

- **Source:** the 1.1.0 changelog's known limitations: `X-IBM-Data-Type: record` is wrong on RECFM=V for reads and
  unimplemented for writes (mvsMF #361, #245), and the binary write path mis-frames V records (#244).
- **LizTerm:** offers Text and Binary only.

### `etag-unused` (log only)

- **Source and observed:** `X-IBM-Return-Etag: true` returns an `ETag` (none is sent without the header), and a
  wrong `If-Match` answers 412 `{"category":6,"reason":10,"message":"The resource was modified since the supplied
  ETag was created"}`. (1.0.0-dev returned no ETag.)
- **LizTerm:** no conflict detection in the preview; the header is not requested.

### `hash-in-names-untested` (log only)

- **Source:** the router percent-decodes the path.
- **Observed:** not tested; no dataset or member with `#` was available.
- **LizTerm:** escapes `#` as `%23` and `%` as `%25` and sends every other name character as it is.

### `uss-limits` (log only, for later)

- **Source:** USS files are limited to 64 KB, use IBM-1047, and USS create answers 400 for an existing file.
- **LizTerm:** no USS support yet.

## Resolved on 1.1.0

Entries the 1.0.0-dev baseline needed and 1.1.0 does not. Each code entry was removed with its code and test on
2026-09-18; the log-only ones were simply struck.

- `basic-auth-every-request`: LizTerm now signs in once with `POST /zosmf/services/authenticate`, holds the
  `LtpaToken2` cookie, and sends it (never `Authorization`) on every request; the tag is gone from the code.
- `no-www-authenticate`: a 401 now carries `WWW-Authenticate`.
- `dataset-list-morerows-false`: `moreRows` is now absent on a complete list, as the source says. The refusal of a
  `moreRows: true` stays as plain code under `no-paging`.
- `member-list-ignores-max-items` and `dataset-list-ignores-start`: merged into `no-paging`, since the host honours
  both now.
- `member-list-empty-for-missing-dataset`: a missing dataset answers 404 reason 4 and a sequential one 400 reason 1,
  as the source says; LizTerm reports not found and an invalid request.
- `missing-read-is-500`: a missing member or dataset on read answers 404 reason 5 or 4; the reason-3 special case
  is gone.
- `text-write-drops-empty-lines`: an empty line is now stored as a blank record; LizTerm sends it as it is.
- `host-date-unreliable`: the `Date` header was correct on 2026-09-18. It is the MVS clock, which was wrong before;
  LizTerm never used it.
- `docs-omit-routes`: `docs/endpoints/datasets/` now documents dataset create and delete and member delete.
