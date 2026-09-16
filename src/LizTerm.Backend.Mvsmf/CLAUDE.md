# LizTerm.Backend.Mvsmf

Notes for working in this project. The root `CLAUDE.md` has the rules that apply everywhere. This project depends on
Core only, is the only one that knows mvsMF exists, and never references `LizTerm.Backend.B3270`.

- `MvsmfFileService` implements `IHostFileService` (Core) over one `HttpClient`. It stores no credentials: it asks
  the `HostCredentialProvider` before every request (`IsRetry: false`), and once more after a 401
  (`IsRetry: true`) before repeating the request once. The App's credential holder (next PR) will be the only
  store. Never put the userid's password in a message, a log or a `ToString()`.
- **Every workaround carries `// mvsMF-compat: <tag>`** matching an entry in `docs/mvsmf-compatibility.md`, and a
  test named after the tag pins it. Add all three together, and read that log before changing any behaviour that
  looks odd: it is probably deliberate.
- **Never send `Content-Type: application/json` on a `PUT`.** mvsMF treats it as a rename.
- **Text is ISO-8859-1 on the wire** in both directions. Empty lines go out as one space, because the host drops
  empty ones. `EncodeText` throws for a line break or a character above U+00FF; `TextUploadCheck` (Core) should have
  refused those first.
- **Classify errors by the JSON `category`/`reason`, then the status.** mvsMF answers 500 for a missing member and
  for a refused open. `MvsmfErrors` is the one place that mapping lives.
- **No paging.** This build ignores `start` and, for members, `X-IBM-Max-Items`; lists are fetched whole.
- **Timeouts:** 10 s to connect (`SocketsHttpHandler.ConnectTimeout`), 30 s without data (`IdleTimeout`, reset on
  every chunk), and no `HttpClient.Timeout`, so a long download is never cut off while bytes arrive.
  `HttpCompletionOption.ResponseHeadersRead` everywhere, so bodies stream. `SendAsync` calls `IdleTimeout.Pause()`
  before every credential ask, so a user taking their time at the prompt is not host silence; `SendOnceAsync`
  re-arms the clock (`Reset()`) once the request actually goes out. A
  `SocketsHttpHandler` connect timeout — a `TaskCanceledException` with an inner `TimeoutException` — is mapped to
  `HostFileErrorKind.Unreachable`, "cannot reach the host (no answer within 10 s)".
- **Bodies are built once per call**, so the repeat after a 401 sends the same bytes; `WriteBinaryAsync` therefore
  reads its source into memory first.
- **TLS:** `MvsmfCertificateCheck` trusts a pinned leaf fingerprint and nothing else, or, without a pin, the system's
  verdict. `TakeRejected()` returns the certificate refused since the last call and clears the slot (one slot: a
  service talks to one host), so each refusal is reported once and an accepted handshake clears it; the service
  raises `CertificateRejected` with the `PresentedCertificate`. It uses Core's
  `SslStreamCertificateFetcher.SelectPresented`, so a pin covers exactly what was on the wire.
- Names are escaped by `EscapeName`: `#` and `%` only. Everything else a validated `HostPath` or filter can hold goes
  as it is, as curl sends it.
- `MvsmfOptions.TryNormalizeBaseUrl` refuses a URL carrying a userid or password ("Leave the userid and password out
  of the URL."), since credentials belong to the credential provider, never to the stored base URL.

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
