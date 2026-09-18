# LizTerm.Backend.Mvsmf

Notes for working in this project. The root `CLAUDE.md` has the rules that apply everywhere. This project depends on
Core only, is the only one that knows mvsMF exists, and never references `LizTerm.Backend.B3270`.

- `MvsmfFileService` implements `IHostFileService` (Core) over one `HttpClient`. It stores no credentials: it asks
  the `HostCredentialProvider` before every request (`IsRetry: false`), and once more after a 401
  (`IsRetry: true`, `Rejected` = the instance just refused, so a holder serving parallel requests prompts only once)
  before repeating the request once. The App's `CredentialHolder` is the only store. Never put the userid's password
  in a message, a log or a `ToString()`.
- **Every workaround carries `// mvsMF-compat: <tag>`** matching an entry in `docs/mvsmf-compatibility.md`, and a
  test named after the tag pins it. Add all three together, and read that log before changing any behaviour that
  looks odd: it is probably deliberate.
- **Never send `Content-Type: application/json` on a `PUT`.** mvsMF treats it as a rename.
- **Text is ISO-8859-1 on the wire** in both directions, whatever `charset` says. Lines go out as they are, LF
  ended; an empty line is stored as a blank record. `EncodeText` throws for a line break or a character above
  U+00FF; `TextUploadCheck` (Core) should have refused those first, and it also refuses over-long lines, because
  the host truncates them, writes the rest, and only then answers 500.
- **Classify errors by the JSON `category`/`reason`, then the status.** A missing dataset or member is 404 with
  reason 4 or 5; a refused open is 500 in category 4; a truncated write is 500 in category 6, reason 3, and reaches
  the caller as a server error carrying the host's message. `MvsmfErrors` is the one place that mapping lives.
- **No paging.** mvsMF 1.1.0 honours `start` and `X-IBM-Max-Items`, but lists are still fetched whole (`no-paging`,
  #144); a `moreRows: true` on either list is refused as a partial answer.
- **Timeouts:** 10 s to connect (`SocketsHttpHandler.ConnectTimeout`), 30 s without data (`IdleTimeout`, reset on
  every chunk), and no `HttpClient.Timeout`, so a long download is never cut off while bytes arrive.
  `HttpCompletionOption.ResponseHeadersRead` everywhere, so bodies stream. `SendAsync` calls `IdleTimeout.Pause()`
  before every credential ask, so a user taking their time at the prompt is not host silence; `SendOnceAsync`
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
  of the URL."), since credentials belong to the credential provider, never to the stored base URL. It also refuses
  a query or a fragment. `MvsmfOptions`' constructor refuses the same URLs with an `ArgumentException`, as
  `TryNormalizeBaseUrl` does, through one shared check (`CheckBaseUrl`), so no service is built on a URL the editor
  would have refused.

## Tests

- `RecordedHandler` answers queued responses, usually `Fixture.Load(name)` from `Fixtures/`, and records each
  request (method, URI, Authorization, `X-IBM-Data-Type`, content type, body, header names).
- Fixtures are real exchanges recorded by `tools/record-mvsmf-fixture.sh`; `Fixtures/README.md` lists them. The body
  bytes are kept exactly, so never run a fixture through a CR-stripping tool.
- `LoopbackHttpsServer` serves one JSON body over TLS with a `TestCertificates` certificate (linked from
  Core.Tests), for the pin tests. Its tests carry `[Fact(Timeout = 30000)]`, so a TLS regression fails instead of
  hanging the run.
- `StallingStream`, `FailingStream` and `ListProgress` (`TestStreams.cs`) drive the idle timeout, a dropped
  connection and synchronous progress.
