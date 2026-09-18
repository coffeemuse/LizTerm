# mvsMF fixtures

Each `.http` file is one exchange recorded from a live mvsMF by `tools/record-mvsmf-fixture.sh`: the response
headers without CRs (Date, Jobname, Jobid and Node dropped), a blank line, then the body bytes as received.
`Fixture.Load` turns one into an `HttpResponseMessage`; `Transfer-Encoding`, `Content-Length` and `Connection` are
ignored because the body is already whole. `.gitattributes` marks the files `-text` so no checkout converts their line endings.

Recorded 2026-09-18 from an MVS/CE host running mvsMF 1.1.0 (`zosmf_full_version`). The `LtpaToken2` cookie value
is replaced by `<token>` at recording time. See `docs/mvsmf-compatibility.md` before re-recording against a newer
build: a changed fixture is a changed behaviour.

| Fixture | Request |
|---|---|
| `info-200` | `GET info` |
| `info-401` | `GET info` with a wrong password |
| `ds-list-sys1` | `GET restfiles/ds?dslevel=SYS1.**` |
| `ds-list-empty` | `GET restfiles/ds?dslevel=NOSUCH.HLQ` |
| `members-proclib` | `GET restfiles/ds/SYS1.PROCLIB/member` |
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
