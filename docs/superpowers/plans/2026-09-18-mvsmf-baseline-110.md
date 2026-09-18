# mvsMF 1.1.0 baseline (PR 1 of the token-auth phase) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Re-baseline `LizTerm.Backend.Mvsmf` on mvsMF 1.1.0: re-record every fixture, follow the host where it changed, retire the workarounds it no longer needs, and rewrite `docs/mvsmf-compatibility.md` top to bottom. No authentication change.

**Architecture:** The backend keeps its shape. Seven compatibility entries close (the host now matches its source), six are rewritten, and one is added for the version fields. The code changes are small and local: error classification follows the 404s, the server version reads `zosmf_full_version`, empty lines go out as they are, member lists get the same partial-list guard datasets have, and the paging tags merge into one. The fixture recorder learns to redact the session cookie the new build sets on every response.

**Tech Stack:** .NET 10, C# latest, `System.Net.Http`, `System.Text.Json` source generation, xunit.v3, curl, `gh`.

**Spec:** `docs/superpowers/specs/2026-09-18-mvsmf-token-auth-design.md` (read §1, §2, §3 and §5 before starting; §4, §6, §7 are PR 2 and are **not** built here).

## Global Constraints

- Every hand-written `.cs` and `.sh` file starts with the three licence lines (after the shebang in a script):
  `This file is part of LizTerm.` / `Copyright 2026 by CoffeeMuse` / `SPDX-License-Identifier: BSD-3-Clause`.
- `LizTerm.Core` depends on the BCL only and never names mvsMF, Avalonia or b3270, not even in comments.
- `LizTerm.Backend.Mvsmf` depends on Core only and never references `LizTerm.Backend.B3270`.
- Every mvsMF workaround in backend code carries `// mvsMF-compat: <tag>` with a matching entry in
  `docs/mvsmf-compatibility.md`, and a backend test named after the tag (dashes become underscores, first letter
  capitalised) pins it. The one exception is `text-write-truncates`, pinned by Core's `TextUploadCheckTests`.
- **No authentication change.** Basic credentials still go on every request; `UseCookies` stays false. The cookie
  the host now sets is redacted in fixtures and otherwise ignored. That is PR 2.
- No new operations, no paging, no ETag use. Where 1.1.0 makes those possible, the log says so and an issue is filed.
- Credentials never appear in logs, exception messages, `ToString()`, fixtures or a process command line.
- Zero warnings: `dotnet build LizTerm.slnx --no-incremental 2>&1 | grep -c " warning "` prints `0`.
- Commits end with `Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>`.

## Prerequisites

- Work on a branch `claude/mvsmf-baseline-110` created from `claude/issue-17-review-dd7c85` (which carries the spec
  and this plan), in a worktree of its own.
- Tasks 2 and 8 need the live host, which now runs mvsMF 1.1.0 at `http://10.42.37.209:8080/zosmf`. The four
  `LIZTERM_MVSMF_*` variables are **not** in `~/.config/lizterm-test.env` at the time of writing. The credentials
  are in `~/.mvsmf-netrc` (`machine 10.42.37.209 login IBMUSER password …`). Set them in the shell that runs those
  tasks, without echoing the password:

  ```bash
  export LIZTERM_MVSMF_URL=http://10.42.37.209:8080/zosmf
  export LIZTERM_MVSMF_USER=IBMUSER
  export LIZTERM_MVSMF_PASSWORD=$(awk '{print $6}' ~/.mvsmf-netrc)
  export LIZTERM_MVSMF_SCRATCH_PDS=MVSCE02.CNTL
  ```

  If `~/.mvsmf-netrc` is missing, stop and ask Robert. Never print `LIZTERM_MVSMF_PASSWORD`.
- Task 3 creates a GitHub issue with `gh`. Robert approved filing the paging issue in spec §5.

## What the probes found (2026-09-18, mvsMF 1.1.0, `zosmf_full_version` 1.1.0)

This table is the plan's input. Every entry of the current log has been probed by hand; the outcome column is what
the tasks below implement.

| Entry today | Observed on 1.1.0 | Outcome |
|---|---|---|
| `basic-auth-every-request` | `Set-Cookie: LtpaToken2=…` on every authenticated response | **Rewrite**: cookie issued, LizTerm still ignores it (PR 2 uses it). Fixtures must redact it. |
| `info-requires-auth` | 401 without credentials, now with `WWW-Authenticate`; the source's `samplib/mvsmfprm` says `/info` is authenticated by design since mvsMF #324; `docs/endpoints/info.md` still says "Not required" | **Rewrite**: by design; docs stale |
| *(none)* | `zosmf_version` is now `"1"`; the full version is `zosmf_full_version: "1.1.0"` (1.0.0-dev put `1.0.0-dev` in `zosmf_version`) | **New** `info-version-fields`: prefer the full version |
| `no-www-authenticate` | `WWW-Authenticate: Basic realm="MVSC"` present | **Close** |
| `dataset-list-ignores-start` | `start=SYS1.PARMLIB` + `X-IBM-Max-Items: 5` → 5 items from `SYS1.PARMLIB`, `returnedRows: 5`, `moreRows: true` | **Merge** into `no-paging`; file the paging issue |
| `dataset-list-morerows-false` | complete list: no `moreRows` key (as the source says) | **Close**; keep the partial-list guard as plain code |
| `dslevel-is-a-prefix` | `dslevel=MVSCE02` lists every `MVSCE02.*` | Unchanged |
| `member-list-ignores-max-items` | `X-IBM-Max-Items: 3` on `SYS1.MACLIB` → 3 of 742, `moreRows: true` | **Merge** into `no-paging`; add the guard members lacked |
| `member-list-empty-for-missing-dataset` | missing dataset → 404 `{"rc":8,"category":6,"reason":4,"message":"Dataset not found"}`; sequential dataset → 400 reason 1 "Dataset is not partitioned (use the dataset endpoint instead)" | **Close**; the code follows |
| `missing-read-is-500` | member → 404 reason 5 "PDS member not found"; dataset → 404 reason 4 "Dataset not found" | **Close**; the code follows |
| `authorization-is-500` | still not reproduced. The empty `MVSCE02.CNTL` listing of 2026-09-16 was an **empty PDS**, not a hidden refusal: a member written by IBMUSER listed at once | **Rewrite** the observation; keep the mapping |
| `text-body-is-latin1` | Latin-1 `AC` round-trips as `AC`; UTF-8 `C2 AC` stored as two characters even with `charset=UTF-8` declared; `Content-Type: text/plain` with no charset on read | Unchanged (charset observation added) |
| `text-read-keeps-trailing-blanks` | `SYS1.PROCLIB(JES2)`: every line 80 characters | Unchanged |
| `text-write-drops-empty-lines` | `A\n\nB\n \nC\n` reads back as `A`, blank, `B`, blank, `C` | **Close**; empty lines go out as they are |
| `text-write-truncates-silently` | 100-character line to LRECL 80 → **500** `{"category":6,"reason":3,"message":"Record truncated to the record length of the data set"}`, and the member holds the 80-character line | **Rewrite** as `text-write-truncates`: reported now, but still partly written, so the pre-flight stays |
| `put-json-is-rename` | `docs/endpoints/datasets/authorization.md` documents `PUT … + request:rename` | Unchanged |
| `binary-fixed-padding` | 100 bytes to FB 80 reads back as 160 | Unchanged |
| `record-write-broken` | 1.1.0 changelog, Known limitations: record mode wrong on RECFM=V for reads, unimplemented for writes (#361, #245), binary write mis-frames V (#244) | Unchanged; cite the changelog |
| `no-etag` | `X-IBM-Return-Etag: true` → `ETag: 5B6CB3B700000960`; no ETag without the header; wrong `If-Match` → 412 reason 10 | **Rewrite** as `etag-unused` |
| `host-date-unreliable` | `Date: Fri, 18 Sep 2026 …`, correct | **Close** (the MVS clock, not mvsMF) |
| `docs-omit-routes` | `docs/endpoints/datasets/` now has `create.md`, `delete.md`, `members-delete.md`, `put.md` | **Close** |
| `hash-in-names-untested` | no dataset with `#` on the host | Unchanged |
| `uss-limits` | not probed (no USS code) | Unchanged |

Source read alongside: the local checkout `/Users/robert/mvslovers/mvsmf` at commit `cf4d6d5` (`1.1.1-dev`, its
`docs/endpoints/auth/authenticate.md` identical to the public repository).

## File Structure

| File | Responsibility |
|---|---|
| `tools/record-mvsmf-fixture.sh` | Records one exchange; **gains** `Set-Cookie` token redaction |
| `tests/LizTerm.Backend.Mvsmf.Tests/Fixtures/*.http` | Sixteen recorded exchanges (fourteen re-recorded, two new) |
| `tests/LizTerm.Backend.Mvsmf.Tests/Fixtures/README.md` | The fixture table and the recorded-from line |
| `tests/LizTerm.Backend.Mvsmf.Tests/FixtureTests.cs` | Pins that no fixture holds a real token |
| `src/LizTerm.Backend.Mvsmf/MvsmfJson.cs` | `MvsmfInfo` gains `zosmf_full_version`; `MvsmfMemberList` gains `moreRows` |
| `src/LizTerm.Backend.Mvsmf/MvsmfFileService.cs` | Version choice, list guards and comments, empty-line encoding |
| `src/LizTerm.Backend.Mvsmf/MvsmfErrors.cs` | Drops the reason-3 special case; quotes the host on server errors |
| `src/LizTerm.Backend.Mvsmf/CLAUDE.md` | Bullets on empty lines, paging and errors |
| `src/LizTerm.Core/HostFiles/IHostFileService.cs` | `ListMembersAsync` doc comment |
| `tests/LizTerm.Backend.Mvsmf.Tests/Mvsmf{Auth,List,Read,Write,Errors}Tests.cs` | Renamed and changed pins |
| `tests/LizTerm.Integration.Tests/LiveMvsmfTests.cs` | A deleted member is `NotFound`, nothing looser |
| `docs/mvsmf-compatibility.md` | Rewritten top to bottom |
| `docs/user-guide.md` | The Test example says `1.1.0` |

---

### Task 1: The recorder redacts the session cookie, and a test pins it

The 1.1.0 host sets `Set-Cookie: LtpaToken2=<base64>; Path=/; HttpOnly; SameSite=Strict` on every authenticated
response. A fixture is a response, so without this the re-recording in Task 2 would commit live session tokens.
(The spec lists this under PR 2; it has to land here because PR 1 is the one that re-records.)

**Files:**
- Modify: `tools/record-mvsmf-fixture.sh`
- Modify: `tests/LizTerm.Backend.Mvsmf.Tests/FixtureTests.cs`

**Interfaces:**
- Produces: fixtures whose only cookie line is `Set-Cookie: LtpaToken2=<token>; Path=/; HttpOnly; SameSite=Strict`.

- [ ] **Step 1: Extend the fixture test so a real token would fail it**

In `FixtureTests.cs`, replace `No_fixture_holds_credentials` with:

```csharp
    [Fact]
    public void No_fixture_holds_credentials_or_a_session_token()
    {
        foreach (var file in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Fixtures"), "*.http"))
        {
            var text = File.ReadAllText(file);
            Assert.DoesNotContain("Authorization", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("password", text, StringComparison.OrdinalIgnoreCase);
            foreach (var line in text.Split('\n').Where(l => l.StartsWith("Set-Cookie:", StringComparison.OrdinalIgnoreCase)))
                Assert.StartsWith("Set-Cookie: LtpaToken2=<token>;", line);
        }
    }
```

- [ ] **Step 2: Run it; it passes today (no fixture has a cookie yet)**

Run: `dotnet test tests/LizTerm.Backend.Mvsmf.Tests --filter "FullyQualifiedName~FixtureTests"`
Expected: PASS. The red step is Task 2's first recording, which would fail this test without Step 3.

- [ ] **Step 3: Redact in the recorder**

In `tools/record-mvsmf-fixture.sh`, change the header comment and the output line. The comment's first paragraph
becomes:

```bash
# Records one mvsMF exchange as a test fixture in tests/LizTerm.Backend.Mvsmf.Tests/Fixtures/<name>.http: the
# response headers with CRs removed (Date, Jobname, Jobid and Node dropped; the LtpaToken2 cookie's value replaced
# by <token>), a blank line, then the body bytes untouched. The body is never passed through tr, because binary
# records can hold 0x0D.
```

and the line that writes the file becomes:

```bash
{ tr -d '\r' < "$tmp/headers" | grep -viE '^(date|jobname|jobid|node):' | sed -E 's/^(Set-Cookie: LtpaToken2=)[^;]*/\1<token>/I'; cat "$tmp/body"; } > "$out"
```

- [ ] **Step 4: Prove the redaction on a header sample without the host**

Run:

```bash
printf 'HTTP/1.1 200 OK\r\nSet-Cookie: LtpaToken2=abc123==; Path=/; HttpOnly\r\nContent-Type: application/json\r\n' | tr -d '\r' | sed -E 's/^(Set-Cookie: LtpaToken2=)[^;]*/\1<token>/I'
```

Expected: the second line reads `Set-Cookie: LtpaToken2=<token>; Path=/; HttpOnly`.

- [ ] **Step 5: Commit**

```bash
git add tools/record-mvsmf-fixture.sh tests/LizTerm.Backend.Mvsmf.Tests/FixtureTests.cs
git commit -m "Redact the session cookie when recording an mvsMF fixture

mvsMF 1.1.0 sets LtpaToken2 on every authenticated response, and a
fixture is a response.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 2: Re-record every fixture against 1.1.0, plus two new ones

**Files:**
- Modify: `tests/LizTerm.Backend.Mvsmf.Tests/Fixtures/*.http` (fourteen)
- Create: `tests/LizTerm.Backend.Mvsmf.Tests/Fixtures/members-not-partitioned.http`
- Create: `tests/LizTerm.Backend.Mvsmf.Tests/Fixtures/write-truncated.http`
- Modify: `tests/LizTerm.Backend.Mvsmf.Tests/Fixtures/README.md`

**Interfaces:**
- Produces: the sixteen fixtures every later task's tests load by name. After this task the suite is **red** in
  exactly the tests Tasks 4 to 7 change; that is expected and listed in Step 4.

- [ ] **Step 1: Set the environment (see Prerequisites) and record the read-only fixtures**

Run, from the repository root, in the shell that has the four variables:

```bash
tools/record-mvsmf-fixture.sh info-200 GET info
MVSMF_BAD_PASSWORD=1 tools/record-mvsmf-fixture.sh info-401 GET info
tools/record-mvsmf-fixture.sh ds-list-sys1 GET 'restfiles/ds?dslevel=SYS1.**'
tools/record-mvsmf-fixture.sh ds-list-empty GET 'restfiles/ds?dslevel=NOSUCH.HLQ'
tools/record-mvsmf-fixture.sh members-proclib GET restfiles/ds/SYS1.PROCLIB/member
tools/record-mvsmf-fixture.sh members-missing-dataset GET restfiles/ds/MVSCE02.NOSUCH/member
tools/record-mvsmf-fixture.sh members-not-partitioned GET restfiles/ds/MVSCE02.LIZT.SAMPLIB2.XMIT/member
tools/record-mvsmf-fixture.sh read-text-jes2 GET 'restfiles/ds/SYS1.PROCLIB(JES2)' -H 'X-IBM-Data-Type: text'
tools/record-mvsmf-fixture.sh read-binary-jes2 GET 'restfiles/ds/SYS1.PROCLIB(JES2)' -H 'X-IBM-Data-Type: binary'
tools/record-mvsmf-fixture.sh read-missing-member GET 'restfiles/ds/SYS1.PROCLIB(NOSUCHMB)'
tools/record-mvsmf-fixture.sh read-pds-as-sequential GET restfiles/ds/SYS1.PROCLIB
tools/record-mvsmf-fixture.sh name-too-long GET 'restfiles/ds/SYS1.PROCLIB(TOOLONGNAME)'
```

Expected first lines, as the tool prints them: `info-200: HTTP/1.1 200 OK`, `info-401: HTTP/1.1 401 Unauthorized`,
`ds-list-sys1: … 200`, `ds-list-empty: … 200`, `members-proclib: … 200`, `members-missing-dataset: HTTP/1.1 404 Not Found`,
`members-not-partitioned: HTTP/1.1 400 Bad Request`, `read-text-jes2: … 200`, `read-binary-jes2: … 200`,
`read-missing-member: HTTP/1.1 404 Not Found`, `read-pds-as-sequential: … 400`, `name-too-long: … 400`.
If `MVSCE02.LIZT.SAMPLIB2.XMIT` no longer exists, pick any dataset the `dslevel=MVSCE02` list shows with `dsorg`
`PS` and note the name in the README row.

- [ ] **Step 2: Record the write fixtures, in this order, against the scratch member**

```bash
python3 -c "print('X'*100)" > /tmp/liz-100.txt
tools/record-mvsmf-fixture.sh write-truncated PUT 'restfiles/ds/MVSCE02.CNTL(LIZTEST)' -H 'Content-Type: text/plain' -H 'X-IBM-Data-Type: text' --data-binary @/tmp/liz-100.txt
printf '//LIZTEST JOB (ACCT),LIZTERM\n//* recorded fixture\n' > /tmp/liz-ok.txt
tools/record-mvsmf-fixture.sh write-204 PUT 'restfiles/ds/MVSCE02.CNTL(LIZTEST)' -H 'Content-Type: text/plain' -H 'X-IBM-Data-Type: text' --data-binary @/tmp/liz-ok.txt
tools/record-mvsmf-fixture.sh delete-204 DELETE 'restfiles/ds/MVSCE02.CNTL(LIZTEST)'
tools/record-mvsmf-fixture.sh delete-missing DELETE 'restfiles/ds/MVSCE02.CNTL(LIZTEST)'
rm -f /tmp/liz-100.txt /tmp/liz-ok.txt
```

Expected: `write-truncated: HTTP/1.1 500 Internal Server Error`, `write-204: HTTP/1.1 204 No Content`,
`delete-204: HTTP/1.1 204 No Content`, `delete-missing: HTTP/1.1 404 Not Found`. The scratch member is gone afterwards.

- [ ] **Step 3: Check the recordings hold no token and have the expected shapes**

Run:

```bash
grep -l 'LtpaToken2=' tests/LizTerm.Backend.Mvsmf.Tests/Fixtures/*.http | wc -l
grep -h 'LtpaToken2=' tests/LizTerm.Backend.Mvsmf.Tests/Fixtures/*.http | sort -u
head -c 300 tests/LizTerm.Backend.Mvsmf.Tests/Fixtures/info-200.http; echo
tail -c 120 tests/LizTerm.Backend.Mvsmf.Tests/Fixtures/members-missing-dataset.http; echo
tail -c 120 tests/LizTerm.Backend.Mvsmf.Tests/Fixtures/write-truncated.http; echo
```

Expected: the count is the number of 2xx fixtures (the 401 and the 404s carry no cookie; count them, do not
assume); the `sort -u` prints exactly one line, `Set-Cookie: LtpaToken2=<token>; Path=/; HttpOnly; SameSite=Strict`;
`info-200` ends in a body holding `"zosmf_full_version":"1.1.0"`; `members-missing-dataset` ends in
`{"rc":8,"category":6,"reason":4,"message":"Dataset not found"}`; `write-truncated` ends in
`{"rc":8,"category":6,"reason":3,"message":"Record truncated to the record length of the data set"}`.

- [ ] **Step 4: Run the backend suite and list the failures**

Run: `dotnet test tests/LizTerm.Backend.Mvsmf.Tests 2>&1 | grep -E "Failed |Passed!|Failed!"`
Expected failures, and only these (each is fixed by the task named):

| Test | Fixed in |
|---|---|
| `Info_requires_auth_so_server_info_is_asked_with_credentials` (expects `1.0.0-dev`) | Task 4 |
| `Missing_read_is_500_is_reported_as_cannot_open` | Task 5 |
| `A_missing_member_cannot_be_opened` | Task 5 |
| `Member_list_empty_for_missing_dataset_is_not_an_error` | Task 6 |

If a test outside this table fails, the host changed something the probes missed: stop, read the fixture, and add
the entry to the "What the probes found" table before going on.

- [ ] **Step 5: Update the fixture README**

Replace the "Recorded …" paragraph with:

```markdown
Recorded 2026-09-18 from an MVS/CE host running mvsMF 1.1.0 (`zosmf_full_version`). The `LtpaToken2` cookie value
is replaced by `<token>` at recording time. See `docs/mvsmf-compatibility.md` before re-recording against a newer
build: a changed fixture is a changed behaviour.
```

Change the `members-missing-dataset` row and add two rows, keeping the table's order:

```markdown
| `members-missing-dataset` | `GET restfiles/ds/<hlq>.NOSUCH/member` — 404, reason 4 |
| `members-not-partitioned` | `GET restfiles/ds/<a sequential dataset>/member` — 400, reason 1 |
```

and after `write-204`:

```markdown
| `write-truncated` | `PUT` of a 100-character line to an LRECL 80 member — 500, reason 3, after writing the truncated record |
```

- [ ] **Step 6: Commit (the suite is red in the four tests above; say so)**

```bash
git add tests/LizTerm.Backend.Mvsmf.Tests/Fixtures
git commit -m "Re-record the mvsMF fixtures against mvsMF 1.1.0

Fourteen re-recorded and two new (a member list of a sequential
dataset, a truncated write). Four tests now fail; the next commits
follow the host.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 3: File the paging issue

1.1.0 honours `start` and `X-IBM-Max-Items` on both lists. Paging is a feature, not this PR; the log entry and the
code comments cite the issue by number.

**Files:** none.

**Interfaces:**
- Produces: an issue number, `#<N>`, used verbatim in Tasks 6 and 9.

- [ ] **Step 1: Create the issue**

```bash
gh issue create --title "mvsMF Browser: page long dataset and member lists" --label enhancement --body "$(cat <<'EOF'
mvsMF 1.1.0 honours \`start\` and \`X-IBM-Max-Items\` on \`GET /zosmf/restfiles/ds\` and on the member list, answering \`returnedRows\` and \`moreRows: true\` for a partial page (probed 2026-09-18; the 1.0.0-dev build ignored both, compatibility entry \`no-paging\`).

The browser still asks for whole lists, which is fine for a hobbyist catalogue (\`SYS1.**\` is under a hundred datasets) but not for a large one. This issue is the Load more row from the dataset browser spec, on both lists, once #17's token-auth phase is in.
EOF
)"
```

Expected: the URL of the new issue. Note its number as `#<N>` for Tasks 6 and 9.

---

### Task 4: The server version comes from `zosmf_full_version`

**Files:**
- Modify: `src/LizTerm.Backend.Mvsmf/MvsmfJson.cs:10-12`
- Modify: `src/LizTerm.Backend.Mvsmf/MvsmfFileService.cs:70-79`
- Modify: `tests/LizTerm.Backend.Mvsmf.Tests/MvsmfAuthTests.cs:29-43`

**Interfaces:**
- Consumes: the re-recorded `info-200` fixture (`zosmf_version: "1"`, `zosmf_full_version: "1.1.0"`).
- Produces: `HostServerInfo.ProductVersion` is `1.1.0` against 1.1.0 and `1.0.0-dev` against a body that has only
  `zosmf_version`.

- [ ] **Step 1: Change the existing expectation and add the pin**

In `MvsmfAuthTests.cs`, in `Info_requires_auth_so_server_info_is_asked_with_credentials`, change
`new HostServerInfo("mvsMF", "1.0.0-dev", "MVS 3.8j")` to `new HostServerInfo("mvsMF", "1.1.0", "MVS 3.8j")`.
Then add, after that test:

```csharp
    [Theory]
    [InlineData("""{"zosmf_version":"1","zosmf_full_version":"1.1.0","zos_version":"MVS 3.8j"}""", "1.1.0")]
    [InlineData("""{"zosmf_version":"1.0.0-dev","zos_version":"MVS 3.8j"}""", "1.0.0-dev")]
    [InlineData("""{"zosmf_version":"1","zosmf_full_version":"  ","zos_version":"MVS 3.8j"}""", "1")]
    [InlineData("""{"zos_version":"MVS 3.8j"}""", "unknown")]
    public async Task Info_version_fields_prefer_the_full_version(string body, string expected)
    {
        using var service = new MvsmfFileService(new RecordedHandler().Then(HttpStatusCode.OK, body), Base,
            Answering([], new HostCredentials("MVSCE02", "pw")));

        var info = await service.GetServerInfoAsync(TestContext.Current.CancellationToken);

        Assert.Equal(expected, info.ProductVersion);
        Assert.Equal("MVS 3.8j", info.SystemVersion);
    }
```

(`HostServerInfo` is `record HostServerInfo(string Product, string ProductVersion, string SystemVersion)`.)

- [ ] **Step 2: Run; both fail**

Run: `dotnet test tests/LizTerm.Backend.Mvsmf.Tests --filter "FullyQualifiedName~MvsmfAuthTests.Info"`
Expected: FAIL. The existing test sees `1`; the theory's first row sees `1`.

- [ ] **Step 3: Read the field and prefer it**

In `MvsmfJson.cs`, replace the `MvsmfInfo` record with:

```csharp
internal sealed record MvsmfInfo(
    [property: JsonPropertyName("zosmf_version")] string? ZosmfVersion,
    [property: JsonPropertyName("zosmf_full_version")] string? ZosmfFullVersion,
    [property: JsonPropertyName("zos_version")] string? ZosVersion);
```

In `MvsmfFileService.GetServerInfoAsync`, replace the `return` line with:

```csharp
        // mvsMF-compat: info-version-fields — 1.0.0-dev put the whole version in zosmf_version; 1.1.0 puts the major
        // there and the full version in zosmf_full_version, as z/OSMF does, so the full one is read first.
        var version = Blank(info.ZosmfFullVersion) ?? Blank(info.ZosmfVersion) ?? "unknown";
        return new HostServerInfo("mvsMF", version, Blank(info.ZosVersion) ?? "unknown");
```

`Blank` already exists in the class (`private static string? Blank(string? value)`).

- [ ] **Step 4: Run; both pass**

Run: `dotnet test tests/LizTerm.Backend.Mvsmf.Tests --filter "FullyQualifiedName~MvsmfAuthTests"`
Expected: PASS, all tests in the class.

- [ ] **Step 5: Commit**

```bash
git add src/LizTerm.Backend.Mvsmf/MvsmfJson.cs src/LizTerm.Backend.Mvsmf/MvsmfFileService.cs tests/LizTerm.Backend.Mvsmf.Tests/MvsmfAuthTests.cs
git commit -m "Read the mvsMF version from zosmf_full_version

mvsMF 1.1.0 reports \"1\" in zosmf_version and the full version beside
it, as z/OSMF does.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 5: Errors follow the 404s, and a server error quotes the host

1.1.0 answers 404 reason 4 or 5 for a missing dataset or member, as its source always said, so the reason-3 special
case goes. Reason 3 now arrives on a truncated write (500), which must not read as "cannot be opened": a server
error carries the host's message instead.

**Files:**
- Modify: `src/LizTerm.Backend.Mvsmf/MvsmfErrors.cs:21-46`
- Modify: `tests/LizTerm.Backend.Mvsmf.Tests/MvsmfErrorsTests.cs`
- Modify: `tests/LizTerm.Backend.Mvsmf.Tests/MvsmfReadTests.cs:79-86`
- Modify: `tests/LizTerm.Integration.Tests/LiveMvsmfTests.cs:86`

**Interfaces:**
- Produces: `MvsmfErrors.Classify` never returns `CannotOpen`; a `ServerError` message is
  `"<what>: <host message> (reason N)."` when the host sent one.

- [ ] **Step 1: Rewrite the pins**

In `MvsmfErrorsTests.cs`:

Change the theory rows: replace
`[InlineData(HttpStatusCode.InternalServerError, 6, 8, 3, HostFileErrorKind.CannotOpen)]` with
`[InlineData(HttpStatusCode.InternalServerError, 6, 8, 3, HostFileErrorKind.ServerError)]` and add
`[InlineData(HttpStatusCode.NotFound, 6, 8, 4, HostFileErrorKind.NotFound)]` after the reason-5 row.

Replace `Missing_read_is_500_is_reported_as_cannot_open` with:

```csharp
    [Fact]
    public async Task A_missing_member_is_not_found()
    {
        using var response = Fixture.Load("read-missing-member");
        var body = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);

        var ex = MvsmfErrors.FromResponse(response.StatusCode, body, "SYS1.PROCLIB(NOSUCHMB)");

        Assert.Equal(HostFileErrorKind.NotFound, ex.Kind);
        Assert.Equal(5, ex.Reason);
        Assert.Equal("PDS member not found", ex.ServerMessage);
        Assert.Equal("SYS1.PROCLIB(NOSUCHMB): not found.", ex.Message);
    }

    [Fact]
    public async Task A_truncated_write_is_a_server_error_quoting_the_host()
    {
        using var response = Fixture.Load("write-truncated");
        var body = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);

        var ex = MvsmfErrors.FromResponse(response.StatusCode, body, "MVSCE02.CNTL(LIZTEST)");

        Assert.Equal(HostFileErrorKind.ServerError, ex.Kind);
        Assert.Equal(3, ex.Reason);
        Assert.Equal("MVSCE02.CNTL(LIZTEST): Record truncated to the record length of the data set (reason 3).", ex.Message);
    }
```

In `A_body_that_is_not_an_mvsmf_error_still_gives_a_message`, change the third row's expectation from
`"Server information: server error (reason 12)."` to `"Server information: odd (reason 12)."`.

In `MvsmfReadTests.cs`, rename `A_missing_member_cannot_be_opened` to `A_missing_member_is_not_found` and change
its assertion to `Assert.Equal(HostFileErrorKind.NotFound, ex.Kind);`.

In `LiveMvsmfTests.cs`, replace
`Assert.Contains(gone.Kind, new[] { HostFileErrorKind.NotFound, HostFileErrorKind.CannotOpen });` with
`Assert.Equal(HostFileErrorKind.NotFound, gone.Kind);`.

- [ ] **Step 2: Run; the new and changed tests fail**

Run: `dotnet test tests/LizTerm.Backend.Mvsmf.Tests --filter "FullyQualifiedName~MvsmfErrorsTests|FullyQualifiedName~MvsmfReadTests"`
Expected: FAIL in `Classifies_by_reason_before_status` (reason-3 row), `A_missing_member_is_not_found` (both
classes: kind is `CannotOpen`), `A_truncated_write_is_a_server_error_quoting_the_host`, and the third row of
`A_body_that_is_not_an_mvsmf_error_still_gives_a_message`.

- [ ] **Step 3: Change the classifier and the description**

In `MvsmfErrors.cs`, replace `Classify` and `Describe` with:

```csharp
    internal static HostFileErrorKind Classify(HttpStatusCode status, int? category, int? rc, int? reason)
    {
        if (status == HttpStatusCode.Unauthorized) return HostFileErrorKind.Unauthenticated;
        if (category == DatasetCategory && reason is 4 or 5) return HostFileErrorKind.NotFound;
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
        HostFileErrorKind.NotAuthorized => $"{what}: not authorized.",
        HostFileErrorKind.InvalidRequest => $"{what}: the host refused the request ({error?.Message ?? "bad request"}).",
        _ => error switch
        {
            { Reason: { } reason, Message: { } message } when !string.IsNullOrWhiteSpace(message)
                => $"{what}: {message.Trim().TrimEnd('.')} (reason {reason}).",
            { Reason: { } reason } => $"{what}: server error (reason {reason}).",
            _ => $"{what}: server error (HTTP {(int)status}).",
        },
    };
```

Also update the class summary's second sentence to: `mvsMF uses 500 for a refused open and for a truncated write,
so the JSON body's category and reason decide before the HTTP status does, and a server error repeats the host's
message.`

- [ ] **Step 4: Run; all pass**

Run: `dotnet test tests/LizTerm.Backend.Mvsmf.Tests --filter "FullyQualifiedName~MvsmfErrorsTests|FullyQualifiedName~MvsmfReadTests"`
Expected: PASS.

- [ ] **Step 5: Check nothing else in the solution depended on the old wording**

Run: `grep -rn "CannotOpen\|cannot be opened" src/LizTerm.Backend.Mvsmf tests/LizTerm.Backend.Mvsmf.Tests tests/LizTerm.Integration.Tests`
Expected: no output. `HostFileErrorKind.CannotOpen` stays in Core and in the App's `HostFileMessages` (another host
may still need it); nothing is deleted there.

- [ ] **Step 6: Commit**

```bash
git add src/LizTerm.Backend.Mvsmf/MvsmfErrors.cs tests/LizTerm.Backend.Mvsmf.Tests/MvsmfErrorsTests.cs tests/LizTerm.Backend.Mvsmf.Tests/MvsmfReadTests.cs tests/LizTerm.Integration.Tests/LiveMvsmfTests.cs
git commit -m "Follow mvsMF 1.1.0's 404s and quote the host on a server error

A missing dataset or member is 404 reason 4 or 5 now, so the reason-3
special case goes; reason 3 arrives on a truncated write instead, and
a server error repeats the host's message rather than a bare reason.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 6: Lists: one `no-paging` entry, a guard for members, and the closed entries

**Files:**
- Modify: `src/LizTerm.Backend.Mvsmf/MvsmfJson.cs:30`
- Modify: `src/LizTerm.Backend.Mvsmf/MvsmfFileService.cs:81-113`
- Modify: `src/LizTerm.Core/HostFiles/IHostFileService.cs:19-21`
- Modify: `tests/LizTerm.Backend.Mvsmf.Tests/MvsmfListTests.cs`

**Interfaces:**
- Consumes: `#<N>` from Task 3; the `members-missing-dataset` (404) and `members-not-partitioned` (400) fixtures.
- Produces: `ListMembersAsync` throws `NotFound` for a missing dataset, `InvalidRequest` for a sequential one, and
  `ServerError` for `moreRows: true`.

- [ ] **Step 1: Rename and rewrite the pins**

In `MvsmfListTests.cs`:

Rename `Dataset_list_ignores_start_so_the_whole_list_is_asked_for` to
`No_paging_so_the_whole_dataset_list_is_asked_for` (body unchanged).

Rename `Dataset_list_morerows_false_is_a_complete_empty_list` to `An_empty_dataset_list_is_complete` (body unchanged).

Rename `Member_list_ignores_max_items_so_none_is_sent` to `No_paging_so_the_whole_member_list_is_asked_for` (body
unchanged).

Replace `Member_list_empty_for_missing_dataset_is_not_an_error` with:

```csharp
    [Fact]
    public async Task A_missing_dataset_has_no_member_list()
    {
        using var service = Service(new RecordedHandler().Then("members-missing-dataset"));

        var ex = await Assert.ThrowsAsync<HostFileException>(() =>
            service.ListMembersAsync(HostPath.ForDataset("MVSCE02.NOSUCH"), TestContext.Current.CancellationToken));

        Assert.Equal(HostFileErrorKind.NotFound, ex.Kind);
        Assert.Equal("MVSCE02.NOSUCH: not found.", ex.Message);
    }

    [Fact]
    public async Task A_sequential_dataset_has_no_member_list()
    {
        using var service = Service(new RecordedHandler().Then("members-not-partitioned"));

        var ex = await Assert.ThrowsAsync<HostFileException>(() =>
            service.ListMembersAsync(HostPath.ForDataset("MVSCE02.LIZT.SAMPLIB2.XMIT"), TestContext.Current.CancellationToken));

        Assert.Equal(HostFileErrorKind.InvalidRequest, ex.Kind);
        Assert.StartsWith("MVSCE02.LIZT.SAMPLIB2.XMIT: the host refused the request (Dataset is not partitioned", ex.Message);
    }

    [Fact]
    public async Task Member_list_morerows_true_is_an_error()
    {
        using var service = Service(new RecordedHandler().Then(HttpStatusCode.OK, """{"items":[{"member":"A"}],"moreRows":true}"""));

        var ex = await Assert.ThrowsAsync<HostFileException>(() =>
            service.ListMembersAsync(HostPath.ForDataset("SYS1.MACLIB"), TestContext.Current.CancellationToken));

        Assert.Equal(HostFileErrorKind.ServerError, ex.Kind);
        Assert.Equal("SYS1.MACLIB: the host returned only part of the list.", ex.Message);
    }
```

(Use the dataset name the README row records if Task 2 chose a different sequential dataset.)

- [ ] **Step 2: Run; two fail**

Run: `dotnet test tests/LizTerm.Backend.Mvsmf.Tests --filter "FullyQualifiedName~MvsmfListTests"`
Expected: `A_missing_dataset_has_no_member_list` and `A_sequential_dataset_has_no_member_list` already pass (the
404 and 400 go through `SendAsync`'s error path); `Member_list_morerows_true_is_an_error` FAILS (the flag is not
read). The renamed tests pass.

- [ ] **Step 3: Read `moreRows` on members and rewrite the comments**

In `MvsmfJson.cs`, replace the `MvsmfMemberList` record with:

```csharp
internal sealed record MvsmfMemberList(
    [property: JsonPropertyName("items")] List<MvsmfMember>? Items,
    [property: JsonPropertyName("moreRows")] bool? MoreRows);
```

In `MvsmfFileService.ListDatasetsAsync`, replace the two tagged comments and the guard with:

```csharp
        // mvsMF-compat: no-paging — start and X-IBM-Max-Items work since mvsMF 1.1.0, but LizTerm asks for the whole
        // list with neither (paging is #<N>).
        using var response = await SendAsync(
            () => new HttpRequestMessage(HttpMethod.Get, Url($"restfiles/ds?dslevel={EscapeName(filter)}")), what, idle, cancellationToken);
        var list = await ReadJsonAsync(response, MvsmfJsonContext.Default.MvsmfDatasetList, what, idle, cancellationToken);
        // No item limit is ever sent, so a true moreRows means the host returned a partial list: refuse it.
        if (list.MoreRows == true)
            throw new HostFileException(HostFileErrorKind.ServerError, $"{what}: the host returned only part of the list.");
```

In `ListMembersAsync`, replace from the `// mvsMF-compat: member-list-ignores-max-items` comment to the `return`
with:

```csharp
        // mvsMF-compat: no-paging — as for datasets: whole list, no X-IBM-Max-Items (paging is #<N>).
        using var response = await SendAsync(
            () => new HttpRequestMessage(HttpMethod.Get, Url(DatasetPath(dataset) + "/member")), what, idle, cancellationToken);
        var list = await ReadJsonAsync(response, MvsmfJsonContext.Default.MvsmfMemberList, what, idle, cancellationToken);
        if (list.MoreRows == true)
            throw new HostFileException(HostFileErrorKind.ServerError, $"{what}: the host returned only part of the list.");
        return [.. (list.Items ?? Enumerable.Empty<MvsmfMember>())
            .Where(m => !string.IsNullOrWhiteSpace(m.Member))
            .Select(m => new HostFileEntry(m.Member!.Trim(), HostFileEntryKind.Member))];
```

Write the real issue number in place of `#<N>` in both comments.

In `IHostFileService.cs`, replace the `ListMembersAsync` summary with:

```csharp
    /// <summary>The members of a partitioned dataset. A dataset that does not exist is <see cref="HostFileErrorKind.NotFound"/>;
    /// one that is not partitioned is <see cref="HostFileErrorKind.InvalidRequest"/>.</summary>
```

- [ ] **Step 4: Run the list tests, then the App tests that browse**

Run: `dotnet test tests/LizTerm.Backend.Mvsmf.Tests --filter "FullyQualifiedName~MvsmfListTests"`
Expected: PASS.

Run: `grep -rn "member-list-empty\|not partitioned\|confirm the dataset" src/LizTerm.App tests/LizTerm.App.Tests`
Expected: no hit that cites the closed entry. If a comment does, reword it to the new interface contract (a missing
dataset throws `NotFound`); no behaviour in the App changes, because the browser only lists members of datasets it
already listed as partitioned.

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~MvsmfBrowser"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/LizTerm.Backend.Mvsmf/MvsmfJson.cs src/LizTerm.Backend.Mvsmf/MvsmfFileService.cs src/LizTerm.Core/HostFiles/IHostFileService.cs tests/LizTerm.Backend.Mvsmf.Tests/MvsmfListTests.cs
git commit -m "Merge the two paging workarounds into no-paging and guard member lists

mvsMF 1.1.0 honours start and X-IBM-Max-Items on both lists, answers
404 for a missing dataset's members and 400 for a sequential one, and
omits moreRows on a complete list. LizTerm still fetches whole lists;
paging is #<N>.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 7: Writes: empty lines go out as they are; truncation is reported but still partial

**Files:**
- Modify: `src/LizTerm.Backend.Mvsmf/MvsmfFileService.cs:200-217`
- Modify: `tests/LizTerm.Backend.Mvsmf.Tests/MvsmfWriteTests.cs:34-43`
- Modify: `src/LizTerm.Backend.Mvsmf/CLAUDE.md`

**Interfaces:**
- Produces: `EncodeText(["A", "", "B"])` is `A\n\nB\n`.

- [ ] **Step 1: Change the pin**

In `MvsmfWriteTests.cs`, replace `Text_write_drops_empty_lines_so_each_is_sent_as_one_blank` with:

```csharp
    [Fact]
    public async Task An_empty_line_is_sent_as_an_empty_record()
    {
        var handler = new RecordedHandler().Then("write-204");
        using var service = Service(handler);

        await service.WriteTextAsync(NewMember, ["A", "", "B"], TestContext.Current.CancellationToken);

        Assert.Equal("A\n\nB\n", handler.Requests[0].BodyText);
    }
```

- [ ] **Step 2: Run; it fails**

Run: `dotnet test tests/LizTerm.Backend.Mvsmf.Tests --filter "FullyQualifiedName~An_empty_line_is_sent_as_an_empty_record"`
Expected: FAIL, actual `A\n \nB\n`.

- [ ] **Step 3: Send the line as it is, and reword the truncation tag**

In `MvsmfFileService.EncodeText`, replace the loop body's last statement and the two comments before it with:

```csharp
            text.Append(line).Append('\n');
```

(delete the `// mvsMF-compat: text-write-drops-empty-lines` comment and the `line.Length == 0 ? " " : line`
expression), and replace the two comments before the `return` with:

```csharp
        // mvsMF-compat: text-body-is-latin1 — the host reads the body as ISO-8859-1 whatever charset says.
        // mvsMF-compat: text-write-truncates — an over-long line is cut to the record length and written before the
        // host answers 500, so TextUploadCheck refuses such lines and nothing is ever partly written.
```

- [ ] **Step 4: Run the write tests and the Core pin**

Run: `dotnet test tests/LizTerm.Backend.Mvsmf.Tests --filter "FullyQualifiedName~MvsmfWriteTests"`
Expected: PASS.

Run: `dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~TextUploadCheckTests"`
Expected: PASS (unchanged; it pins `text-write-truncates`).

- [ ] **Step 5: Update the backend notes**

In `src/LizTerm.Backend.Mvsmf/CLAUDE.md`:

Replace the bullet starting `- **Text is ISO-8859-1 on the wire**` with:

```markdown
- **Text is ISO-8859-1 on the wire** in both directions, whatever `charset` says. Lines go out as they are, LF
  ended; an empty line is stored as a blank record. `EncodeText` throws for a line break or a character above
  U+00FF; `TextUploadCheck` (Core) should have refused those first, and it also refuses over-long lines, because
  the host truncates them, writes the rest, and only then answers 500.
```

Replace the bullet starting `- **Classify errors by the JSON `category`/`reason`, then the status.**` with:

```markdown
- **Classify errors by the JSON `category`/`reason`, then the status.** A missing dataset or member is 404 with
  reason 4 or 5; a refused open is 500 in category 4; a truncated write is 500 in category 6, reason 3, and reaches
  the caller as a server error carrying the host's message. `MvsmfErrors` is the one place that mapping lives.
```

Replace the bullet starting `- **No paging.**` with:

```markdown
- **No paging.** mvsMF 1.1.0 honours `start` and `X-IBM-Max-Items`, but lists are still fetched whole (`no-paging`,
  #<N>); a `moreRows: true` on either list is refused as a partial answer.
```

- [ ] **Step 6: Commit**

```bash
git add src/LizTerm.Backend.Mvsmf/MvsmfFileService.cs src/LizTerm.Backend.Mvsmf/CLAUDE.md tests/LizTerm.Backend.Mvsmf.Tests/MvsmfWriteTests.cs
git commit -m "Send empty lines as empty records; mvsMF 1.1.0 keeps them

The host no longer drops an empty line, so the single-blank workaround
goes. A truncated write is reported now, but still written, so the
pre-flight refusal stays under a tag that says so.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 8: Confirm the log-only entries by probe

Spec §5 asks for a hand probe of every entry no test covers. The plan's table already holds the results; this task
re-runs them so the log's "probed 2026-09-18" line is the executor's, not the planner's, and catches a host that
changed between planning and execution. Read-only except W1 to W4, which write the scratch member and delete it.

**Files:** none (a script in the scratchpad).

- [ ] **Step 1: Run the probes**

Save as `probe.sh` in the scratchpad and run it with the environment of the Prerequisites (`bash probe.sh`):

```bash
#!/usr/bin/env bash
set -u
H=${LIZTERM_MVSMF_URL%/}
M="$H/restfiles/ds/${LIZTERM_MVSMF_SCRATCH_PDS}(LIZTEST)"
c() { curl -s -m 20 -u "$LIZTERM_MVSMF_USER:$LIZTERM_MVSMF_PASSWORD" "$@"; }
echo "== dslevel prefix"; c "$H/restfiles/ds?dslevel=MVSCE02" | python3 -c "import sys,json; print(sorted({i['dsname'].split('.')[0] for i in json.load(sys.stdin)['items']}))"
echo "== date header"; c -D - -o /dev/null "$H/info" | grep -i '^date:'
echo "== hash names"; c "$H/restfiles/ds?dslevel=SYS1.**" | python3 -c "import sys,json; print([i['dsname'] for i in json.load(sys.stdin)['items'] if '#' in i['dsname']])"
echo "== etag without/with header"; c -D - -o /dev/null "$H/restfiles/ds/SYS1.PROCLIB(JES2)" | grep -ic '^etag:'; c -D - -o /dev/null -H 'X-IBM-Return-Etag: true' "$H/restfiles/ds/SYS1.PROCLIB(JES2)" | grep -ic '^etag:'
echo "== trailing blanks"; c "$H/restfiles/ds/SYS1.PROCLIB(JES2)" | python3 -c "import sys; print(sorted({len(l) for l in sys.stdin.buffer.read().split(b'\n') if l}))"
echo "== W1 latin1 vs utf8"; printf 'L\xacL\nU\xc2\xacU\n' | c -X PUT -H 'Content-Type: text/plain; charset=UTF-8' -H 'X-IBM-Data-Type: text' --data-binary @- -o /dev/null -w '%{http_code}\n' "$M"; c "$M" | python3 -c "import sys; print([l[:6].hex() for l in sys.stdin.buffer.read().split(b'\n') if l])"
echo "== W2 empty lines"; printf 'A\n\nB\n' | c -X PUT -H 'Content-Type: text/plain' -H 'X-IBM-Data-Type: text' --data-binary @- -o /dev/null -w '%{http_code}\n' "$M"; c "$M" | python3 -c "import sys; print([l.rstrip() for l in sys.stdin.buffer.read().split(b'\n')])"
echo "== W3 binary padding"; head -c 100 /dev/zero | c -X PUT -H 'Content-Type: application/octet-stream' -H 'X-IBM-Data-Type: binary' --data-binary @- -o /dev/null -w '%{http_code}\n' "$M"; c -H 'X-IBM-Data-Type: binary' "$M" | wc -c
echo "== W4 If-Match wrong"; printf 'Z\n' | c -X PUT -H 'Content-Type: text/plain' -H 'If-Match: 0000000000000000' --data-binary @- -w ' %{http_code}\n' "$M"
echo "== cleanup"; c -X DELETE -o /dev/null -w '%{http_code}\n' "$M"
```

Expected output, line for line:

```
== dslevel prefix
['MVSCE02']
== date header
Date: <today's date, correct>
== hash names
[]
== etag without/with header
0
1
== trailing blanks
[80]
== W1 latin1 vs utf8
204
['4cac4c', '55c2ac55']
== W2 empty lines
204
[b'A', b'', b'B', b'']
== W3 binary padding
204
     160
== W4 If-Match wrong
{"rc":8,"category":6,"reason":10,"message":"The resource was modified since the supplied ETag was created"} 412
== cleanup
204
```

If any line differs, the entry it belongs to changed after planning: update the table at the top of this plan and
the entry in Task 9 to what was observed, and say so in the PR.

---

### Task 9: Rewrite the compatibility log and the user guide line

**Files:**
- Modify: `docs/mvsmf-compatibility.md` (whole file)
- Modify: `docs/user-guide.md:294`
- Modify: `src/LizTerm.Backend.Mvsmf/MvsmfFileService.cs:59-61,74-75` (two comments)
- Modify: `src/LizTerm.Backend.Mvsmf/CLAUDE.md` (the sentence on tags is unchanged; check the "Tests" section)

**Interfaces:**
- Consumes: `#<N>` from Task 3.

- [ ] **Step 1: Update the two auth comments in the service**

In `MvsmfFileService.CreateHandler`, replace the comment above `UseCookies = false,` with:

```csharp
        // mvsMF-compat: basic-auth-every-request — the host sets an LtpaToken2 cookie since 1.1.0, but this release
        // still sends Basic credentials on every request and keeps no cookie; token sign-in is the next phase of #17.
```

In `GetServerInfoAsync`, replace the comment above the `SendAsync` call with:

```csharp
        // mvsMF-compat: info-requires-auth — the docs say /info needs no credentials; the host demands them by design
        // (mvsMF #324), so it goes through the same authenticated path as everything else.
```

- [ ] **Step 2: Write the new log**

Replace the whole of `docs/mvsmf-compatibility.md` with:

````markdown
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

## How to re-check a new build

1. Point `LIZTERM_MVSMF_*` at the new build (see `docs/development.md`).
2. Re-record every fixture listed in `tests/LizTerm.Backend.Mvsmf.Tests/Fixtures/README.md` with
   `tools/record-mvsmf-fixture.sh`, and run `dotnet test tests/LizTerm.Backend.Mvsmf.Tests`. A test that now fails
   is named after the entry below whose behaviour changed.
3. Run the live tests: `dotnet test tests/LizTerm.Integration.Tests --filter "FullyQualifiedName~LiveMvsmfTests"`.
4. For each entry, probe the behaviour by hand where no test covers it, then update the entry. When a workaround is
   no longer needed, remove the code at its tag (`grep -rn "mvsMF-compat: <tag>" src`), its test, and the entry,
   and add one line under *Resolved*.

Each entry's tag appears in the backend code as `// mvsMF-compat: <tag>` and in the name of the test that pins it.
Entries marked *log only* change nothing in the code.

## Entries

### `basic-auth-every-request`

- **Docs and source:** any Basic-authenticated request is answered with `Set-Cookie: LtpaToken2=…`, and a client
  holding the cookie need not resend credentials; `POST /zosmf/services/authenticate` issues one and `DELETE`
  invalidates it.
- **Observed:** 1.1.0 does all of that (1.0.0-dev set no cookie).
- **LizTerm:** this release still keeps no cookies (`UseCookies = false`) and sends Basic credentials on every
  request. Token sign-in is the next phase of #17. Fixtures record the cookie line with its value replaced by
  `<token>`.

### `info-requires-auth`

- **Docs:** `docs/endpoints/info.md` says `GET /zosmf/info` needs no authentication.
- **Source and observed:** 401 without credentials, with `WWW-Authenticate: Basic realm="<SMF ID>"`. This is by
  design: `samplib/mvsmfprm` says `/info` has been authenticated like every other route since mvsMF #324 and that
  the anonymous liveness probe never existed. The doc is stale.
- **LizTerm:** `/info` goes through the same authenticated path as everything else. An unauthenticated 401 still
  proves the URL reaches an mvsMF, which the next phase uses.

### `info-version-fields`

- **Docs:** `zosmf_version` is the API level and `zosmf_full_version` the release, as in z/OSMF.
- **Observed:** 1.0.0-dev put `1.0.0-dev` in `zosmf_version` and had no `zosmf_full_version`; 1.1.0 answers
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
  Paging is #<N>.

### `dslevel-is-a-prefix` (log only)

- **Observed:** `dslevel=MVSCE02` lists every `MVSCE02.*` dataset, as z/OSMF does.
- **LizTerm:** the browser shows everything the filter matches, as z/OSMF would.

### `authorization-is-500`

- **Source:** a refused open is 500 with category 4, rc 8, reason 0 ("LMOPEN error"), never 403.
- **Observed:** not reproduced; IBMUSER could read everything tried. (On 2026-09-16 MVSCE02's own libraries
  listed empty for IBMUSER, which looked like a hidden refusal; on 2026-09-18 a member IBMUSER wrote into
  `MVSCE02.CNTL` listed at once, so the PDS was simply empty.)
- **LizTerm:** reports that shape as "not authorized".

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
  (LRECL for F, LRECL−4 for V, BLKSIZE for U) before anything is sent; pinned by `TextUploadCheckTests`, since Core
  does not name mvsMF. Should a 500 reason 3 arrive anyway, it is reported as a server error with that message.

### `put-json-is-rename` (log only)

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

Entries the 1.0.0-dev baseline needed and 1.1.0 does not. Each was removed with its code and test on 2026-09-18.

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
````

Write the real issue number in place of `#<N>`.

- [ ] **Step 3: The user guide's Test example**

In `docs/user-guide.md`, change `**✓ Connected: mvsMF 1.0.0-dev on MVS 3.8j**` to
`**✓ Connected: mvsMF 1.1.0 on MVS 3.8j**`.

- [ ] **Step 4: Every tag in code has an entry, and every code entry has a tag and a test**

Save as `check-tags.py` in the scratchpad and run `python3 check-tags.py` from the repository root:

```python
import re, subprocess, pathlib
code = subprocess.run(["grep", "-rhoE", r"mvsMF-compat: [a-z0-9-]+", "src"], capture_output=True, text=True).stdout
tags = {m.split(": ")[1] for m in code.split("\n") if m}
log = pathlib.Path("docs/mvsmf-compatibility.md").read_text()
entries = {m.group(1): "log only" in m.group(2) for m in re.finditer(r"^### `([a-z0-9-]+)`(.*)$", log, re.M)}
print("tags without an entry:", sorted(tags - entries.keys()))
print("code entries without a tag:", sorted(e for e, log_only in entries.items() if not log_only and e not in tags))
tests = "".join(p.read_text() for p in pathlib.Path("tests/LizTerm.Backend.Mvsmf.Tests").glob("*.cs"))
pinned = {e for e, log_only in entries.items() if not log_only}
def name(tag): return tag.replace("-", "_").capitalize()
print("code entries without a test named after them:", sorted(e for e in pinned if name(e) not in tests and e != "text-write-truncates"))
```

Expected output, three lines, every list empty:

```
tags without an entry: []
code entries without a tag: []
code entries without a test named after them: []
```

(`text-write-truncates` is tagged in the backend and pinned in Core's `TextUploadCheckTests`, which is the
documented exception, so the script skips it.)

- [ ] **Step 5: Commit**

```bash
git add docs/mvsmf-compatibility.md docs/user-guide.md src/LizTerm.Backend.Mvsmf/MvsmfFileService.cs
git commit -m "Rewrite the mvsMF compatibility log for mvsMF 1.1.0

Seven entries resolved, six rewritten, one added; the tested-build
table moves to 1.1.0.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 10: Full verification, live lane, and the pull request

**Files:** none new.

- [ ] **Step 1: Zero warnings and the full suite**

Run: `dotnet build LizTerm.slnx --no-incremental 2>&1 | grep -c " warning "`
Expected: `0`.

Run: `dotnet test LizTerm.slnx 2>&1 | grep -E "Passed!|Failed!|Skipped"`
Expected: every project `Passed!`; the live tests skip unless the variables are set.

- [ ] **Step 2: The live lane against 1.1.0**

In the shell with the four variables:

Run: `dotnet test tests/LizTerm.Integration.Tests --filter "FullyQualifiedName~LiveMvsmfTests"`
Expected: 3 passed, 0 skipped. The round-trip test writes and reads back an empty line, which now travels as an
empty record, and asserts `NotFound` after the delete.

- [ ] **Step 3: Header check and dependency check**

Run: `dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~RepositoryHeadersTests"`
Expected: PASS.

Run: `grep -rn "mvsMF\|Mvsmf" src/LizTerm.Core --include='*.cs'`
Expected: no output (Core never names mvsMF).

- [ ] **Step 4: Push and open the PR**

Follow `superpowers:finishing-a-development-branch`. Push `claude/mvsmf-baseline-110` and open a PR against
`main` titled `Re-baseline the mvsMF backend on mvsMF 1.1.0` with this body:

```markdown
PR 1 of the token-auth phase of #17 (spec: `docs/superpowers/specs/2026-09-18-mvsmf-token-auth-design.md`, §5). No authentication change.

The test host now runs mvsMF 1.1.0. Every fixture is re-recorded against it (plus two new ones), every entry in `docs/mvsmf-compatibility.md` was probed by hand, and the code follows the host where it changed:

- **Resolved:** a missing dataset or member is 404 now, a sequential dataset's member list is 400, an empty line is stored as a blank record, `moreRows` is absent on a complete list, `WWW-Authenticate` is back, the `Date` header is right, and the dataset docs list their routes. Each workaround went with its tag and test.
- **Rewritten:** the cookie is now issued (still ignored until PR 2), `/info` needs auth by design (mvsMF #324, doc stale), paging works on the host (`no-paging`, filed as #<N>), a truncated write is reported but still partial (the pre-flight stays), ETags work (unused), and the 2026-09-16 "hidden refusal" was an empty PDS.
- **Added:** `info-version-fields`; the version now comes from `zosmf_full_version`, so Test says `mvsMF 1.1.0`.
- **Fixtures** redact the `LtpaToken2` cookie at recording time, pinned by `FixtureTests`.

The `/info` and cookie behaviour are what PR 2 builds on.

Verified: zero warnings, full suite green, live lane 3/3 against 1.1.0.

🤖 Generated with [Claude Code](https://claude.com/claude-code)
```

Write the real issue number in place of `#<N>`.

- [ ] **Step 5: Hand over**

Report the PR URL and the paging issue URL to Robert. The status comment on #17 is posted after he merges (spec §6).

## Self-review notes

- Spec §5 coverage: re-record (Task 2), hand-probe of uncovered entries (Task 8, with the planner's results in the
  table), per-entry outcomes (Tasks 4 to 7 and 9), paging logged and filed (Tasks 3, 6, 9), tested-build table and
  fixture README (Tasks 2, 9), user guide line (Task 9). The token redaction moved here from §6 with a stated reason.
- Not in this PR, on purpose: any change under `src/LizTerm.App`, `CHANGELOG.md` (the user-visible wording changes
  are within the existing preview line's "not yet complete"), the minimum-version statement (§7, PR 2).
