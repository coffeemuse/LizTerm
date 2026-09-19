# LizTerm.Backend.Mvsmf

Notes for working in this project. The root `CLAUDE.md` has the rules that apply everywhere. This project depends on
Core only, is the only one that knows mvsMF exists, and never references `LizTerm.Backend.B3270`.

- `MvsmfFileService` implements `IHostFileService` (Core) over one `HttpClient`. It signs in once with
  `POST /zosmf/services/authenticate` (Basic, `X-CSRF-ZOSMF-HEADER: LizTerm`), takes the `LtpaToken2` cookie, and
  sends it (`Cookie: LtpaToken2=…`, never `Authorization`) on every later request. The `HostTokenProvider` (the
  App's `SignInHolder`) holds the token and calls the backend's `SignInAsync` when it needs one; a 401 asks the
  provider again with the refused token and repeats the request once. **The password may exist only inside
  `SignInAsync` and the App's prompt** — never in a field, a message, a log or a `ToString()`. Over `http` the
  sign-in is the first request a service makes, so a host it cannot reach is reported with a `Sign-in:` prefix
  rather than the operation's own name. Over `https` a fresh service's first contact is an anonymous `GET /info`
  (`CheckTrustAsync`, once per service, before any password is asked for), so an untrusted certificate is refused
  (`CertificateRejected`, under the operation's name) while the sign-in prompt is still closed, and the operation
  run again after Connect Anyway signs in once; the answer itself is ignored. A second 401 after the re-sign-in is
  `Unauthenticated` with its own sentence ("the host would not accept the session it had just issued"), never the
  password wording: that sign-in has just taken the password. `ProbeAsync` is the separate, anonymous `GET /info`
  the Test button uses before it asks for a password: a 401 there proves the URL is an mvsMF without spending a
  sign-in, a 404 is "Nothing at this URL answers as mvsMF", and any other failure is the host's own answer (a
  proxy's 403, a 503 while it starts), mapped by `MvsmfErrors` so a correct URL is not mistaken for a wrong one.
- **A login that does not answer JSON is `Unsupported`** (spec §4.2): 404, 405 or any other non-JSON answer — a
  proxy's or a web server's catch-all page replying 200 with HTML — means this host has no authenticate route, and
  `NoSignInRoute` says so in one sentence. Only a JSON 200 gets as far as the cookie check, where a missing
  `LtpaToken2` is a `ServerError` ("Sign-in: the host set no session cookie."). `SignOutAsync` is the one call that
  swallows rather than maps: a transport failure or the idle timeout is best effort on window close, but the
  caller's own cancellation propagates for its continuation to observe; the App's `SignInHolder.SignOutCap`
  (five seconds) is such a cancellation, applied to every sign-out, and swallowed there.
- **Cookie, not Bearer.** LizTerm sends the token as the `LtpaToken2` cookie because real z/OSMF accepts only the
  cookie; `Authorization: Bearer` is an mvsMF convenience real z/OSMF does not honour. `UseCookies` stays false and
  the cookie is sent by hand.
- **Every workaround carries `// mvsMF-compat: <tag>`** matching an entry in `docs/mvsmf-compatibility.md`, and a
  test named after the tag pins it. Add all three together, and read that log before changing any behaviour that
  looks odd: it is probably deliberate.
- **A `PUT` with `Content-Type: application/json` is a rename, never a write.** `RenameAsync` is the one method
  that sends it: the new name is the URL, the old one the `{"request":"rename","from-dataset":{…}}` body, `member`
  present for a member rename and absent for a dataset. Writes send only `text/plain` or `application/octet-stream`
  (`put-json-is-rename`). A member rename onto an existing name is 400 reason 7 (`AlreadyExists`,
  `rename-target-exists-400`); a dataset rename onto one is the host's 500 reason 8, a server error quoting it.
- **The stamp (`etag`).** A read sends `X-IBM-Return-Etag: true` only with `withEtag` (a download asks; the verify
  read-back does not: the stamp costs the host a second pass over the member); every write sends it, since the
  stamp of the member as written is the PUT's answer, not the pre-save one. `EtagOf` returns the `ETag` header as
  the host sent it, blanks trimmed and nothing else removed, so a quoted stamp goes back quoted. A write's `ifMatch`
  goes out as `If-Match` verbatim through `TryAddWithoutValidation` (`Add` would parse it as an entity tag and
  refuse mvsMF's unquoted one; the bool it returns is about the header name, not the value), and a 412 is
  `Conflict`.
- **A create posts the allocation as JSON** (`MvsmfAllocation`: `dsorg` `PS`/`PO`, `recfm` folded, `alcunit`
  `TRK`/`CYL`, `dirblk` only for `PO`) after `DatasetAllocation.Problems()` passes. The host answers every
  allocation failure with the same 500, category 8, rc 900 (`create-failure-is-one-500`), mapped to
  `CannotAllocate`. `DeleteAsync` takes a dataset path as well as a member.
- **Text is ISO-8859-1 on the wire** in both directions, whatever `charset` says. Lines go out as they are, LF
  ended; an empty line is stored as a blank record. `EncodeText` throws for a line break or a character above
  U+00FF; `TextUploadCheck` (Core) should have refused those first, and it also refuses over-long lines when the
  listing gave it a record length, because the host truncates them, writes the rest, and only then answers 500.
  A listing with no usable record length leaves such lines to the host.
- **Classify errors by the JSON `category`/`reason`, then the status.** A missing dataset or member is 404 with
  reason 4 or 5; a refused open is 500 in category 4; a failed open is 500 in category 6, reason 3 with a message
  starting `Cannot open`; a truncated write is the same shape with another message and reaches the caller as a
  server error carrying it. `MvsmfErrors` is the one place that mapping lives. A 412 is `Conflict`; category 8
  with rc 900 is `CannotAllocate`; a 400 in category 6 with reason 7 is `AlreadyExists`.
- **Paging is the caller's `HostListRequest`.** `MaxItems` becomes `X-IBM-Max-Items`, `NamePattern` the member
  list's `pattern=`, and `Continuation` (the last name of the page before, opaque to callers) `start=`. `start` is
  inclusive on mvsMF and z/OSMF, so a continued page asks for one more than its size and `Page` drops the repeat, or
  cuts the page to size when that name is gone. A `moreRows: true` on a list asked for whole (`MaxItems` 0) is
  refused as a partial answer. Every dataset list sends `X-IBM-Attributes: base`, which mvsMF ignores and z/OSMF
  needs (`attributes-header-ignored`).
- **Timeouts:** 10 s to connect (`SocketsHttpHandler.ConnectTimeout`), 30 s without data (`IdleTimeout`, reset on
  every chunk), and no `HttpClient.Timeout`, so a long download is never cut off while bytes arrive.
  `HttpCompletionOption.ResponseHeadersRead` everywhere, so bodies stream. `SendAsync` calls `IdleTimeout.Pause()`
  before every token ask (which usually returns the held token without a prompt), so a user taking their time at a
  sign-in prompt is not host silence; `SendOnceAsync`
  re-arms the clock (`Reset()`) once the request actually goes out. A
  `SocketsHttpHandler` connect timeout — a `TaskCanceledException` with an inner `TimeoutException` — is mapped to
  `HostFileErrorKind.Unreachable`, "cannot reach the host (no answer within 10 s)".
- **Bodies are built once per call**, so the repeat after a 401 sends the same bytes; `WriteBinaryAsync` therefore
  reads its source into memory first.
- **TLS:** `MvsmfCertificateCheck` trusts exactly the pinned leaf while it is in date (`NotBefore`..`NotAfter`),
  whatever the system store and the host name say, or, without a pin, the system's verdict. Unlike the 3270 pin (a
  PEM trust store verified by OpenSSL), a pin holding a chain does not extend trust to other leaves.
  `TakeRejected()` returns the certificate refused since the last call and clears the slot (one slot: a service
  talks to one host), so each refusal is reported once and an accepted handshake clears it; the service raises
  `CertificateRejected` with the `PresentedCertificate`. It uses Core's `SslStreamCertificateFetcher.SelectPresented`,
  so a pin covers exactly what was on the wire.
- Names are escaped by `EscapeName`: `#` and `%` only. Everything else a validated `HostPath` or filter can hold goes
  as it is, as curl sends it.
- `MvsmfOptions.TryNormalizeBaseUrl` refuses a URL carrying a userid or password ("Leave the userid and password out
  of the URL."), since the userid and password belong to the sign-in prompt and the backend's `SignInAsync`, never
  to the stored base URL. It also refuses a query or a fragment. `MvsmfOptions`' constructor refuses the same URLs
  with an `ArgumentException`, as `TryNormalizeBaseUrl` does, through one shared check (`CheckBaseUrl`), so no
  service is built on a URL the editor
  would have refused.

## Tests

- `RecordedHandler` answers queued responses, usually `Fixture.Load(name)` from `Fixtures/`, and records each
  request (method, URI, `Authorization`, `Cookie`, `X-CSRF-ZOSMF-HEADER`, `X-IBM-Data-Type`, content type, body,
  header names).
- Fixtures are real exchanges recorded by `tools/record-mvsmf-fixture.sh`, except the ones `Fixtures/README.md`
  marks hand-written (`login-404`, for a host nobody could record); the README lists them all. The body bytes are
  kept exactly, so never run a fixture through a CR-stripping tool.
- `LoopbackHttpsServer` serves one JSON body over TLS with a `TestCertificates` certificate (linked from
  Core.Tests), for the pin tests. Its tests carry `[Fact(Timeout = 30000)]`, so a TLS regression fails instead of
  hanging the run.
- `StallingStream`, `FailingStream` and `ListProgress` (`TestStreams.cs`) drive the idle timeout, a dropped
  connection and synchronous progress.
