# mvsMF fixtures

Each `.http` file is one exchange recorded from a live mvsMF by `tools/record-mvsmf-fixture.sh` (the table marks
the one hand-written exception): the response
headers without CRs (Date, Jobname, Jobid and Node dropped), a blank line, then the body bytes as received.
`Fixture.Load` turns one into an `HttpResponseMessage`; `Transfer-Encoding`, `Content-Length` and `Connection` are
ignored because the body is already whole. `.gitattributes` marks the files `-text` so no checkout converts their line endings.

Recorded 2026-09-18 (`uss-*` on 2026-09-22) from an MVS/CE host running mvsMF 1.1.0 (`zosmf_full_version`). The
`LtpaToken2` cookie value is replaced by `<token>` at recording time. See `docs/mvsmf-compatibility.md` before
re-recording against a newer build: a changed fixture is a changed behaviour.

| Fixture | Request |
|---|---|
| `info-200` | `GET info` |
| `info-401` | `GET info` with a wrong password |
| `login-200` | `POST services/authenticate` — 200 with `Set-Cookie: LtpaToken2` |
| `login-401` | the same with a wrong password — 401, `reasonCode 1` |
| `login-404` | a host with no authenticate route — 404 (hand-written; no pre-1.1.0 host was available) |
| `ds-list-sys1` | `GET restfiles/ds?dslevel=SYS1.**` |
| `ds-list-empty` | `GET restfiles/ds?dslevel=NOSUCH.HLQ` |
| `members-proclib` | `GET restfiles/ds/SYS1.PROCLIB/member` |
| `members-maclib-page` | `GET restfiles/ds/SYS1.MACLIB/member` with `X-IBM-Max-Items: 3` — three of 742 members, `moreRows: true` |
| `members-missing-dataset` | `GET restfiles/ds/<hlq>.NOSUCH/member` — 404, reason 4 |
| `members-not-partitioned` | `GET restfiles/ds/<a sequential dataset>/member` — 400, reason 1 |
| `read-text-jes2` | `GET restfiles/ds/SYS1.PROCLIB(JES2)`, text |
| `read-binary-jes2` | the same, binary |
| `read-missing-member` | `GET restfiles/ds/SYS1.PROCLIB(NOSUCHMB)` — 404, reason 5 |
| `read-pds-as-sequential` | `GET restfiles/ds/SYS1.PROCLIB` — 400, reason 1 |
| `name-too-long` | `GET restfiles/ds/SYS1.PROCLIB(TOOLONGNAME)` — 400, reason 1 |
| `write-204` | `PUT` of a scratch member |
| `write-truncated` | `PUT` of a 100-character line to an LRECL 80 member — 500, reason 3, after writing the truncated record |
| `delete-204` | `DELETE` of that member |
| `delete-missing` | the same `DELETE` again — 404, reason 5 |
| `create-201` | `POST restfiles/ds/<hlq>.LIZITEST.FIX`, an FB 80 PDS of 1 track and 2 directory blocks |
| `create-dynalloc-500` | the same `POST` again — 500, category 8, rc 900, reason 7, the one answer for every allocation failure |
| `write-etag-204` | `PUT` of a member with `X-IBM-Return-Etag: true` — 204 with `ETag` |
| `read-etag` | `GET` of that member, text, with `X-IBM-Return-Etag: true` — 200 with the same `ETag` |
| `write-412` | `PUT` of that member with a stale `If-Match` — 412, reason 10, nothing written |
| `rename-member-204` | `PUT …(TWO)` with the JSON rename body naming `ONE` — 204 |
| `rename-member-missing` | the same again, `ONE` gone — 404, reason 5 |
| `rename-member-exists` | the same with `ONE` written again, `TWO` still there — 400, reason 7 |
| `rename-ds-204` | `PUT restfiles/ds/<hlq>.LIZITEST.FIX2` with the JSON rename body naming the old dataset — 204 |
| `delete-ds-204` | `DELETE` of that dataset — 204 |
| `delete-ds-missing` | the same again — 404, reason 4 |
| `uss-list-root` | `GET restfiles/fs?path=/` — `tmp`, `u`, `www`; `user` and `group` empty |
| `uss-list-empty` | `GET restfiles/fs?path=/u/ibmuser` — no items |
| `uss-list-missing` | `GET restfiles/fs?path=/u/nobody` — 404, category 6, reason 1 |
| `uss-list-truncated` | `GET restfiles/fs?path=/u` with `X-IBM-Max-Items: 1` — one of three, `moreRows: true` |
| `uss-stat-file` | `GET restfiles/fs?path=<a file>` — 200 with one item naming the full path |
| `uss-read-text` | `GET restfiles/fs/u/ibmuser/liztest-fix/hello.txt`, text, with `X-IBM-Return-Etag: true` |
| `uss-read-binary` | the same, binary — the same `ETag` |
| `uss-read-missing` | `GET` of a file that is not there — 404, reason 1 |
| `uss-read-directory` | `GET restfiles/fs/u` — 400, category 2, reason 1, "Is a directory" |
| `uss-write-204` | `PUT` of `hello.txt` |
| `uss-write-etag-204` | the same with `X-IBM-Return-Etag: true` — 204 with `ETag` |
| `uss-write-412` | the same with a stale `If-Match` — 412, category 4, reason 1, "The resource was modified since the supplied ETag was created" |
| `uss-write-too-large` | `PUT` of 70,000 bytes — 204; the host stored all 70,000 bytes, no error (this host enforces no USS size limit at that size) |
| `uss-mkdir-201` | `POST restfiles/fs/u/ibmuser/liztest-fix` with `{"type":"directory"}` — 201 |
| `uss-mkdir-exists` | the same again — 400, category 4, reason 1, "File or directory already exists" |
| `uss-mkdir-no-parent` | `POST restfiles/fs/u/nobody/child` — 404, category 6, reason 1, "File or directory not found" |
| `uss-delete-204` | `DELETE` of the directory with `X-IBM-Option: recursive` — 204 |
| `uss-delete-missing` | the same again — 404, reason 1 |
