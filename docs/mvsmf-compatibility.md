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
  96 entries, arrives at once). The dataset browser, arriving in the next PR, will have no Load more row. If
  `start` works in a newer build, paging can come back for very large catalogues.

### `dataset-list-morerows-false`

- **Source:** `moreRows` appears only when true.
- **Observed:** `"moreRows": false` on a complete list.
- **LizTerm:** sends no item limit, so `moreRows` should never be true; a true means the host changed behaviour and
  returned a partial list, which LizTerm reports as a server error rather than showing.

### `dslevel-is-a-prefix` (log only)

- **Observed:** `dslevel=MVSCE02` lists every `MVSCE02.*` dataset, as z/OSMF does.
- **LizTerm:** the dataset browser, arriving in the next PR, will pick the exact name out of the list for a lookup
  of one dataset by name.

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
- **LizTerm:** the dataset browser, arriving in the next PR, will say that binary transfers to fixed-length
  datasets are padded to whole records.

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
