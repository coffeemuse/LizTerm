# LizTerm: mvsMF token authentication and the 1.1.0 baseline

Design for the next phase of #17, agreed 2026-09-18. It follows the dataset browser spec of 2026-09-16
(`2026-09-16-lizterm-mvsmf-dataset-browser-design.md`, "the browser spec" below) and changes only what that spec
left to a later host: how LizTerm signs in.

## 1. Purpose

The browser preview sends HTTP Basic credentials on every request, because the mvsMF build it was written against
(`1.0.0-dev`) never issued a session token (compatibility entry `basic-auth-every-request`). mvsMF 1.1.0 does, and
the test host now runs it. This phase:

1. re-baselines LizTerm on mvsMF 1.1.0, working `docs/mvsmf-compatibility.md` top to bottom (PR 1);
2. moves sign-in to the token: one login per session window, the password discarded once the token is held, a
   re-prompt when the token expires (PR 2);
3. makes mvsMF 1.1.0 the minimum supported version, documented in one place and enforced by the sign-in.

No new operations. Jobs, USS, console services, dataset create/delete/rename, paging and the parked UI items stay on
#17's list.

## 2. What the host does (probed 2026-09-18, mvsMF 1.1.0 on MVS/CE)

| Behaviour | Observed |
|---|---|
| `GET /zosmf/info`, no credentials | 401 with `WWW-Authenticate: Basic realm="MVSC"`. Intentional: the sample parmlib in the mvsMF source says the anonymous liveness probe "never existed" and `/info` has been authenticated since mvsMF #324. `docs/endpoints/info.md` still says "Not required". |
| Any Basic-authenticated request | 200 and `Set-Cookie: LtpaToken2=<token>; Path=/; HttpOnly; SameSite=Strict` |
| `POST /zosmf/services/authenticate` with Basic | 200, the cookie, body `{"returnCode":0,"reasonCode":0,"message":"Success."}`; a bad password is 401 with `returnCode 8, reasonCode 1` |
| The cookie alone, no `Authorization` | 200 on `/info` and on a dataset list |
| A made-up cookie | 401 |
| `DELETE /zosmf/services/authenticate` with the cookie | 204; the cookie answers 401 afterwards |
| `DELETE` without a valid token | 401 |

The token is httpd's opaque session id, base64-encoded. httpd accepts it as the `LtpaToken2` cookie (the z/OSMF
contract, which real z/OSMF honours) or as `Authorization: Bearer` (an mvsMF convenience real z/OSMF does not).
It lives until httpd's sliding idle `SESSION_TIMEOUT`, default 30 minutes, refreshed by every request; there is no
fixed maximum age yet. `X-CSRF-ZOSMF-HEADER` is required by real z/OSMF on the login call and ignored by mvsMF.

A host older than 1.1.0 has no `services/authenticate` route, so its login answers something other than 200 or 401.

## 3. Decisions

- **The password dies on a successful login.** Only the token is held. After the idle timeout the next operation
  is answered 401, the sign-in prompt appears again with the userid prefilled and a "session expired" line, and the
  operation is retried once. (Chosen over holding the password for silent re-login: the token is what the password
  was traded for, and Zowe and mvsMF's own desktop client do the same.)
- **Two PRs, baseline first**, so a changed fixture has one cause: PR 1 follows the host under today's auth; PR 2
  changes the auth on that clean baseline.
- **Cookie, not Bearer.** LizTerm sends `Cookie: LtpaToken2=<token>` because real z/OSMF accepts only the cookie.
  Recorded in `src/LizTerm.Backend.Mvsmf/CLAUDE.md`; it is a design choice, not a host discrepancy.
- **No new UI.** No Sign Out item or button; sign-out happens when the session window closes. The only visible
  changes are the expired-session line in the prompt, the Test button's new messages, and the docs.
- **Minimum supported mvsMF is 1.1.0.** See §7.

## 4. Architecture

### 4.1 Core: the token contract

The token belongs to the session window, as the credentials did (browser spec §3.2): every browser window of a
session shares it, and the backend service is remade whenever the certificate pin changes (browser spec §3.4), so
the service cannot own it. Core's contract changes from "give me credentials" to "give me a token, and here is how
to get one":

```
public sealed class HostSessionToken(string value)   // ToString hides the value, like HostCredentials
public sealed record HostTokenRequest(HostSessionToken? Rejected);   // null: first need; set: the host refused this one
public delegate Task<HostSessionToken> HostSignIn(HostCredentials credentials, CancellationToken cancellationToken);
public delegate ValueTask<HostSessionToken?> HostTokenProvider(HostTokenRequest request, HostSignIn signIn, CancellationToken cancellationToken);
```

- `HostSignIn` is the backend's login. It throws `HostFileException` with `Unauthenticated` for a refused password
  and `Unsupported` (new kind) for a host without the login route (§7); other failures are the usual kinds.
- `HostTokenProvider` answers the held token, or obtains one through `signIn`, or null when the user cancels.
  Parallel operations may call it concurrently; an implementation serialises its prompts and, on a request whose
  `Rejected` is no longer the token it holds, answers the newer token without prompting (the same rule the
  credential provider had).
- `HostCredentials` and `HostCredentialProvider` remain: `HostCredentials` is the login's input, and the provider
  delegate is deleted with its last use in PR 2.
- `IHostFileService` gains `Task SignOutAsync(HostSessionToken token, CancellationToken cancellationToken = default)`.
  Best effort: a 401 for an already-dead token is success.

### 4.2 Backend: `MvsmfFileService`

- `SignInAsync` (the `HostSignIn` it hands to the provider): `POST services/authenticate` with
  `Authorization: Basic`, `X-CSRF-ZOSMF-HEADER: LizTerm`, `Content-Type: application/x-www-form-urlencoded`, empty
  body. 200: the token is the `LtpaToken2` value from `Set-Cookie`, read from the header, not a `CookieContainer`;
  a 200 without it is a server error. 401: `Unauthenticated`. 404, 405 or any other non-JSON answer: `Unsupported`
  (§7). The credentials are not stored; the method's stack frame is their only holder in the backend.
- Every other request carries `Cookie: LtpaToken2=<token>`, `X-CSRF-ZOSMF-HEADER: LizTerm` and the existing
  `User-Agent`, and no `Authorization` header. `UseCookies` stays false; its comment now says the token is sent by
  hand so that no container can hold or drop it.
- `SendAsync` keeps its shape: ask the provider (`Rejected: null`), send, and on a 401 ask once more with the token
  the host refused, then send once more. A second 401 is `Unauthenticated`. Because the login is the provider's
  business, a 401 on an operation always means an expired or reaped token, never a bad password.
- `SignOutAsync`: `DELETE services/authenticate` with the cookie; 204 and 401 both succeed; other failures throw
  as usual so the caller can ignore them.
- `GetServerInfoAsync` is unchanged (it goes through the token path). A new
  `Task<HostServerInfo?> ProbeAsync(CancellationToken)` sends `GET info` with no credentials and answers: the info
  when the host answered 200 (a host that does not gate `/info`), null for a 401 (reachable, sign-in needed), and
  throws `Unreachable`, `CertificateRejected` or `Unsupported` (a non-401, non-mvsMF answer) otherwise.

### 4.3 App: the sign-in holder

`CredentialHolder` becomes `SignInHolder` in `LizTerm.App/HostFiles`, still owned by `HostFileAccess`, one per
session window:

- Holds `HostSessionToken? _token` and `_lastUserid`, under the same gate and with the same cancellation-sharing
  rule as today (one Cancel answers every operation that shared the prompt).
- `ProviderFor(ICredentialPrompt)` returns a `HostTokenProvider`. Under the gate: if a token is held and the
  request's `Rejected` is not that instance, answer it. Otherwise clear the token, prompt (reason from §4.4),
  call `signIn`; `Unauthenticated` prompts again with reason *rejected*; any other exception propagates to the
  operation; success stores the token and drops the credentials.
- `IsSignedIn` replaces `HasCredentials`. `SignOutAsync(IHostFileService, CancellationToken)` takes the token out
  under the lock and, if there was one, calls the service's `SignOutAsync`.
- `HostFileAccess.Forget` becomes `SignOutAsync()`: it signs out through any live connection's current service,
  capped at five seconds, and swallows every failure. The session window calls it on close, as it called `Forget`.
  The Test tester signs out through its own service before disposing it.

### 4.4 App: the prompt

`CredentialPromptRequest.IsRetry` becomes `Reason`, an enum `SignInReason { First, Rejected, Expired }`.
`SignInWindow` shows no line for *First*, the existing "The userid or password was not accepted" for *Rejected*,
and "Your mvsMF session has expired. Sign in again." for *Expired*. The holder passes *Expired* when the request
carries a `Rejected` token and *Rejected* after a refused login.

### 4.5 App: the Test button

`HostFileServiceFactory.CreateTester` becomes: create the service; `ProbeAsync`; on null, sign in through the
prompt (a fresh holder, as today), `GetServerInfoAsync`, `SignOutAsync`; on an info answered anonymously, report it
without a prompt. Reported lines, in the profile editor's existing result slot:

| Outcome | Line |
|---|---|
| Signed in, version ≥ 1.1.0 | `✓ Connected: mvsMF 1.1.0 on MVS 3.8j` (unchanged) |
| Signed in, version < 1.1.0 | `✗ mvsMF 1.0.0-dev is not supported; LizTerm needs mvsMF 1.1.0 or later` |
| Login answered `Unsupported` | `✗ This host does not support sign-in; LizTerm needs mvsMF 1.1.0 or later` |
| Probe: not mvsMF | `✗ Nothing at this URL answers as mvsMF` |
| Unreachable, certificate | unchanged |

A mistyped URL therefore never shows a sign-in prompt. The version compare reads `zosmf_full_version` as
`major.minor[.patch][-suffix]`; an unparsable version is reported as not supported with the string shown.

### 4.6 Data flow: an expired token

1. The browser lists a PDS 40 minutes after its last request. `SendAsync` asks the provider; the holder answers the
   held token; the host answers 401.
2. `SendAsync` asks again with `Rejected: <that token>`. The holder, under its gate, sees it still holds that
   instance, clears it, prompts with *Expired* and the userid prefilled.
3. A second browser window of the same session was listing too and was also answered 401. Its request waits on the
   gate; by the time it enters, the holder has a new token whose instance differs from its `Rejected`, so it gets the
   new token without a second prompt.
4. The user signs in; the holder calls `signIn`; the backend logs in and returns the token; the holder stores it and
   the credentials go out of scope. `SendAsync` resends the list with the new cookie.
5. Cancel instead: the provider answers null, `SendAsync` throws `Unauthenticated` ("Sign-in was cancelled."), the
   browser shows it as today, and the waiting window's request also fails without prompting (the cancellation rule).

## 5. PR 1: the 1.1.0 baseline

- Re-record all fourteen fixtures in `tests/LizTerm.Backend.Mvsmf.Tests/Fixtures` with
  `tools/record-mvsmf-fixture.sh`; run the suite; run the live lane. A failing test names its entry.
- Hand-probe every entry no test covers: `dataset-list-ignores-start`, `member-list-ignores-max-items`,
  `text-write-truncates-silently`, `hash-in-names-untested`, `host-date-unreliable`, `no-etag`,
  `binary-fixed-padding`, `record-write-broken`, `authorization-is-500`.
- For each of the twenty-two entries, one outcome: unchanged; fixed on the host, so the workaround, its
  `// mvsMF-compat:` tag, its test and the entry go; or changed, so the code follows and the entry is rewritten.
  Expected from the probes: `basic-auth-every-request` is rewritten ("the cookie is now issued; LizTerm ignores it
  until the token phase"), `info-requires-auth` records mvsMF #324 and the stale `info.md`, `no-www-authenticate`
  closes.
- If `start` or `X-IBM-Max-Items` now work, the entry records it and paging is filed as its own issue; PR 1 builds
  no feature.
- The "Tested build" table moves to 1.1.0 (`zosmf_full_version`) and names the source commit read alongside; the
  fixtures README's recorded-from line follows.
- The user guide's Test example line changes from `1.0.0-dev` to `1.1.0`.

## 6. PR 2: token authentication

Sections 3 and 4, plus:

- **Fixtures:** `login-200`, `login-401` (bad password), `login-404` (a pre-1.1.0 host, recorded by hand from the
  observed shape if no such host is available, and said so in the README), `logout-204`, `logout-401` (stale
  token), `info-401-anonymous`, and an operation answered 401 for a stale token (`ds-list-401-stale`).
- **Compatibility log:** `basic-auth-every-request` is retired and its tag removed. New log-only entry
  `session-idle-timeout`: 30 minutes sliding, refreshed by every request, no maximum age; LizTerm re-prompts on
  expiry. `info-requires-auth` stays, now "by design" with the docs discrepancy.
- **Docs:** user guide "Signing in" rewritten (§7); README mvsMF line and the `Unreleased` changelog line gain
  "mvsMF 1.1.0 or later" and "sign in once per session"; `src/LizTerm.Backend.Mvsmf/CLAUDE.md` gains the
  cookie-not-Bearer decision and the rule that the password may exist only inside `SignInAsync`; `docs/privacy.md`'s
  sentence that the mvsMF password is held in memory until the window closes is rewritten to say it is used once
  and discarded.
- A status comment on #17 after each PR merges.

## 7. Minimum supported version

**Home:** `docs/user-guide.md`, "mvsMF Browser (preview)": "LizTerm needs mvsMF 1.1.0 or later. Older builds have
no sign-in service." The README line, the changelog and the compat log's "Tested build" table point there.

**Teeth:** the login's `Unsupported` answer (§4.2) reaches the browser as "This host does not support sign-in;
LizTerm needs mvsMF 1.1.0 or later" through the App's existing `HostFileErrorKind` mapping, and the Test button says
the same (§4.5). The browser does not compare versions itself; a 1.1.0-or-later host that answers the login is
supported by definition.

**User guide "Signing in"** says: the first operation asks for the userid and password; LizTerm uses them once to
sign in and then discards them, keeping only the session token in memory until the window closes; mvsMF forgets an
idle session after 30 minutes, after which LizTerm asks you to sign in again; the password crosses the network once
per sign-in, unencrypted over `http://`, so the reverse-proxy advice stands.

## 8. Testing

- **Backend (recorded):** login success stores the cookie value; bad password is `Unauthenticated`; 404 on login is
  `Unsupported`; a stale-token 401 asks the provider once with `Rejected` set and resends; a second 401 is
  `Unauthenticated`; after login no request carries `Authorization` and every request carries `Cookie` and
  `X-CSRF-ZOSMF-HEADER`; logout accepts 204 and 401; `ProbeAsync` maps 200, 401, and a non-mvsMF answer.
- **App (fake service):** the holder answers one token to concurrent first requests with one prompt; a `Rejected`
  token that is still current re-prompts with *Expired* and the prefill, one that is not current answers the newer
  token without a prompt; a refused login re-prompts with *Rejected*; a cancelled prompt fails the waiting
  operations too; the session window's close signs out exactly once and only when signed in; the tester prompts
  only after an anonymous 401 and reports the three new lines; `FakeHostFileService` grows `SignOutAsync`,
  `ProbeAsync` and a scripted login.
- **Live (`LIZTERM_MVSMF_*`):** sign in, list, sign out, and the signed-out token refused. Idle expiry is not
  live-tested; the recorded stale-token fixture covers it.
- **PR 1:** the suite green on re-recorded fixtures plus the live lane, and every entry's outcome written into the
  log.
- A fixture records only the response, so the token can appear only in `Set-Cookie`. The recording tool, which
  already drops `Date`, `Jobname`, `Jobid` and `Node`, replaces the `LtpaToken2` value with `<token>`, and the
  login test asserts on that placeholder.

## 9. Out of scope

Jobs, USS, console, dataset create/delete/rename, paging, a Sign Out control, Bearer transport, remembering the
token across windows or on disk, real z/OSMF testing, and the UI items parked on #17.
