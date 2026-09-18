# LizTerm.Backend.Mvsmf

Notes for working in this project. The root `CLAUDE.md` has the rules that apply everywhere. This project depends on
Core only, is the only one that knows mvsMF exists, and never references `LizTerm.Backend.B3270`.

- `MvsmfFileService` implements `IHostFileService` (Core) over one `HttpClient`. It signs in once with
  `POST /zosmf/services/authenticate` (Basic, `X-CSRF-ZOSMF-HEADER: LizTerm`), takes the `LtpaToken2` cookie, and
  sends it (`Cookie: LtpaToken2=…`, never `Authorization`) on every later request. The `HostTokenProvider` (the
  App's `SignInHolder`) holds the token and calls the backend's `SignInAsync` when it needs one; a 401 asks the
  provider again with the refused token and repeats the request once. **The password may exist only inside
  `SignInAsync` and the App's prompt** — never in a field, a message, a log or a `ToString()`. Sign-in is the first
  request a service ever makes, so a host it cannot reach is reported with a `Sign-in:` prefix rather than the
  operation's own name. `ProbeAsync` is the separate, anonymous `GET /info` the Test button uses before it asks for
  a password: a 401 there proves the URL is an mvsMF without spending a sign-in.
- **Cookie, not Bearer.** LizTerm sends the token as the `LtpaToken2` cookie because real z/OSMF accepts only the
  cookie; `Authorization: Bearer` is an mvsMF convenience real z/OSMF does not honour. `UseCookies` stays false and
  the cookie is sent by hand.
- **Every workaround carries `// mvsMF-compat: <tag>`** matching an entry in `docs/mvsmf-compatibility.md`, and a
  test named after the tag pins it. Add all three together, and read that log before changing any behaviour that
  looks odd: it is probably deliberate.
- **Never send `Content-Type: application/json` on a `PUT`.** mvsMF treats it as a rename.
- **Text is ISO-8859-1 on the wire** in both directions, whatever `charset` says. Lines go out as they are, LF
  ended; an empty line is stored as a blank record. `EncodeText` throws for a line break or a character above
  U+00FF; `TextUploadCheck` (Core) should have refused those first, and it also refuses over-long lines when the
  listing gave it a record length, because the host truncates them, writes the rest, and only then answers 500.
  A listing with no usable record length leaves such lines to the host.
- **Classify errors by the JSON `category`/`reason`, then the status.** A missing dataset or member is 404 with
  reason 4 or 5; a refused open is 500 in category 4; a failed open is 500 in category 6, reason 3 with a message
  starting `Cannot open`; a truncated write is the same shape with another message and reaches the caller as a
  server error carrying it. `MvsmfErrors` is the one place that mapping lives.
- **No paging.** mvsMF 1.1.0 honours `start` and `X-IBM-Max-Items`, but lists are still fetched whole (`no-paging`,
  #144); a `moreRows: true` on either list is refused as a partial answer.
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
- Fixtures are real exchanges recorded by `tools/record-mvsmf-fixture.sh`; `Fixtures/README.md` lists them. The body
  bytes are kept exactly, so never run a fixture through a CR-stripping tool.
- `LoopbackHttpsServer` serves one JSON body over TLS with a `TestCertificates` certificate (linked from
  Core.Tests), for the pin tests. Its tests carry `[Fact(Timeout = 30000)]`, so a TLS regression fails instead of
  hanging the run.
- `StallingStream`, `FailingStream` and `ListProgress` (`TestStreams.cs`) drive the idle timeout, a dropped
  connection and synchronous progress.
