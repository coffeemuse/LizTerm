# mvsMF fixtures

Each `.http` file is one exchange recorded from a live mvsMF by `tools/record-mvsmf-fixture.sh`: the response
headers without CRs (Date, Jobname, Jobid and Node dropped), a blank line, then the body bytes as received.
`Fixture.Load` turns one into an `HttpResponseMessage`; `Transfer-Encoding`, `Content-Length` and `Connection` are
ignored because the body is already whole.

Recorded 2026-09-16 from an MVS/CE host running a pre-release mvsMF reporting `1.0.0-dev`. See
`docs/mvsmf-compatibility.md` before re-recording against a newer build: a changed fixture is a changed behaviour.

| Fixture | Request |
|---|---|
| `info-200` | `GET info` |
| `info-401` | `GET info` with a wrong password |
| `ds-list-sys1` | `GET restfiles/ds?dslevel=SYS1.**` |
| `ds-list-empty` | `GET restfiles/ds?dslevel=NOSUCH.HLQ` |
| `members-proclib` | `GET restfiles/ds/SYS1.PROCLIB/member` |
| `members-missing-dataset` | `GET restfiles/ds/<hlq>.NOSUCH/member` — answered 200 with no items |
| `read-text-jes2` | `GET restfiles/ds/SYS1.PROCLIB(JES2)`, text |
| `read-binary-jes2` | the same, binary |
| `read-missing-member` | `GET restfiles/ds/SYS1.PROCLIB(NOSUCHMB)` — 500, reason 3 |
| `read-pds-as-sequential` | `GET restfiles/ds/SYS1.PROCLIB` — 400, reason 1 |
| `name-too-long` | `GET restfiles/ds/SYS1.PROCLIB(TOOLONGNAME)` — 400, reason 1 |
| `write-204` | `PUT` of a scratch member |
| `delete-204` | `DELETE` of that member |
| `delete-missing` | the same `DELETE` again — 404, reason 5 |
