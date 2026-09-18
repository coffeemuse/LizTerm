# mvsMF token authentication (PR 2 of the token-auth phase) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Sign in to mvsMF once per session window with `POST /zosmf/services/authenticate`, hold only the session token, discard the password, re-prompt when the token expires, sign out when the window closes, and make mvsMF 1.1.0 the documented and enforced minimum.

**Architecture:** Core's credential contract changes shape from "give me credentials" to "give me a token, and here is how to get one": a `HostSessionToken`, a `HostTokenRequest`, a backend `HostSignIn` login function, and a `HostTokenProvider` the App implements. The backend logs in with Basic to get the `LtpaToken2` cookie, then sends that cookie (never `Authorization`) on every later request. The App's holder trades the password for a token and drops the password; an expired-token 401 re-prompts with an "expired" reason; the session window signs out on close. This is PR 2 of #17; PR 1 (the 1.1.0 baseline) is merged.

**Tech Stack:** .NET 10, C# latest, `System.Net.Http` (`SocketsHttpHandler`), `System.Text.Json` source generation, Avalonia, xunit.v3, curl, `gh`.

**Spec:** `docs/superpowers/specs/2026-09-18-mvsmf-token-auth-design.md` (read §3, §4, §6, §7, §8 before starting; §5 was PR 1 and is already merged).

## Global Constraints

- Every hand-written `.cs`, `.axaml` and `.sh` file starts with the three licence lines (after the shebang in a script, before the root element in `.axaml`): `This file is part of LizTerm.` / `Copyright 2026 by CoffeeMuse` / `SPDX-License-Identifier: BSD-3-Clause`. A new file needs them; `RepositoryHeadersTests` fails the suite for a missing one.
- **Dependency rule.** `LizTerm.Core` depends on the BCL only and never names mvsMF, Avalonia or b3270, not even in comments. `LizTerm.Backend.Mvsmf` depends on Core only and is the only project that knows mvsMF exists. `LizTerm.App` names the mvsMF backend only in `src/LizTerm.App/HostFileServiceFactory.cs`; everything else in App talks to `IHostFileService`.
- **The password may exist only inside the backend's `SignInAsync` stack frame and the sign-in prompt.** Never in a field, a log, an exception message, a `ToString()`, a fixture, or a process command line. Only the token is held after a successful login.
- **Cookie, not Bearer.** LizTerm sends `Cookie: LtpaToken2=<token>`, because real z/OSMF accepts only the cookie. `UseCookies` stays `false`; the token is sent by hand.
- **`X-CSRF-ZOSMF-HEADER: LizTerm`** on every request, including the login. mvsMF ignores it; real z/OSMF requires it on the login.
- **mvsMF 1.1.0 is the minimum supported version** (§7). Its one home is the user guide; the README, changelog and compat log point there. A host without the login route is reported as unsupported.
- Fixtures are recorded exchanges, binary-exact; only `tools/record-mvsmf-fixture.sh` writes them, and every `Set-Cookie: LtpaToken2=` line reads `…=<token>;` (PR 1's recorder redaction and `FixtureTests` pin this).
- Zero warnings: `dotnet build LizTerm.slnx --no-incremental 2>&1 | grep -c " warning "` prints `0`.
- Commits end with `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`.

## Decisions recorded in this plan

- **An untrusted `https` host asks for the password twice on its first connection, and that is accepted.** Today
  the credentials are cached before the first send, so a certificate refusal, Connect Anyway, and the retried
  operation reuse them. Under this plan the refusal happens inside `SignInAsync` (the login is the first request on
  the new service), the typed password is gone by design, and after Connect Anyway the retried operation prompts
  again. Holding the password across the certificate prompt is exactly what this PR removes, and the case is one
  extra prompt per unpinned host (per session, when Connect Anyway is chosen without Remember). Task 4 records it in
  the App notes so QA does not read it as a bug.

## Prerequisites

- Work on the branch `claude/mvsmf-token-auth`, already created from `origin/main` at `c1eb66f` (this worktree is on it). The spec and this plan are committed to it.
- Tasks 2 and 8 need the live host, mvsMF 1.1.0 at `http://10.42.37.209:8080/zosmf`, credentials in `~/.mvsmf-netrc` (user `IBMUSER`). Set the four variables in the recording/live shell without echoing the password:

  ```bash
  export LIZTERM_MVSMF_URL=http://10.42.37.209:8080/zosmf
  export LIZTERM_MVSMF_USER=IBMUSER
  export LIZTERM_MVSMF_PASSWORD=$(awk '{print $6}' ~/.mvsmf-netrc)
  export LIZTERM_MVSMF_SCRATCH_PDS=MVSCE02.CNTL
  ```

  If `~/.mvsmf-netrc` is missing, stop and ask.
- The local mvsMF source is at `/Users/robert/mvslovers/mvsmf` (commit `cf4d6d5`, 1.1.1-dev). `docs/endpoints/auth/authenticate.md` there documents the login/logout contract used below.

## What the host does (probed 2026-09-18, mvsMF 1.1.0)

| Request | Response |
|---|---|
| `POST /zosmf/services/authenticate` with Basic | 200, `Set-Cookie: LtpaToken2=<token>; Path=/; HttpOnly; SameSite=Strict`, body `{"returnCode":0,"reasonCode":0,"message":"Success."}` |
| the same with a bad password | 401, body `{"returnCode":8,"reasonCode":1,"message":"Login failed. …"}` |
| any request carrying only the cookie, no `Authorization` | 200 |
| a made-up cookie | 401 |
| `DELETE /zosmf/services/authenticate` with the cookie | 204; the cookie answers 401 afterwards |
| `DELETE` with no valid token | 401 |
| a host older than 1.1.0 | no `services/authenticate` route (answers other than 200/401) |

Token lifetime is httpd's sliding idle `SESSION_TIMEOUT`, default 30 minutes, refreshed by every request; no fixed maximum age.

## File Structure

| File | Responsibility |
|---|---|
| `src/LizTerm.Core/HostFiles/HostSessionToken.cs` | **New.** The token class (hides its value), `HostTokenRequest`, `HostSignIn`, `HostTokenProvider` |
| `src/LizTerm.Core/HostFiles/HostFileException.cs` | `HostFileErrorKind.Unsupported` |
| `src/LizTerm.Core/HostFiles/IHostFileService.cs` | `SignOutAsync` (default interface method) |
| `src/LizTerm.Backend.Mvsmf/MvsmfFileService.cs` | `SignInAsync`, `ProbeAsync`, token-carrying `SendAsync`, `SignOutAsync`, ctor takes `HostTokenProvider` |
| `src/LizTerm.Backend.Mvsmf/CLAUDE.md` | cookie-not-Bearer, password only in `SignInAsync` |
| `src/LizTerm.App/HostFiles/SignInHolder.cs` | **Renamed** from `CredentialHolder.cs`; holds the token, implements `HostTokenProvider` |
| `src/LizTerm.App/HostFiles/HostFileAccess.cs` | `SignOutAsync` replaces `Forget` |
| `src/LizTerm.App/HostFileServiceFactory.cs` | `Create` takes `HostTokenProvider`; `CreateTester` probes first |
| `src/LizTerm.App/Dialogs/ICredentialPrompt.cs` | `CredentialPromptRequest.Reason` (a `SignInReason`) replaces `IsRetry` |
| `src/LizTerm.App/Views/SignInWindow.axaml{,.cs}` | the expired-session line |
| `src/LizTerm.App/ViewModels/ProfileEditorViewModel.cs` | Test uses the probe-first tester; version and unsupported messages |
| `src/LizTerm.App/Views/SessionWindow.axaml.cs` | sign out on close (5 s cap) |
| `tests/LizTerm.Backend.Mvsmf.Tests/Fixtures/{login-200,login-401,login-404}.http` | login fixtures |
| `tests/LizTerm.Backend.Mvsmf.Tests/*` | token-path backend tests |
| `tests/LizTerm.App.Tests/*`, `tests/LizTerm.Core.Tests/*`, `tests/LizTerm.Integration.Tests/*` | migrated tests and fakes |
| `docs/mvsmf-compatibility.md`, `docs/user-guide.md`, `docs/privacy.md`, `README.md`, `CHANGELOG.md` | §6, §7 docs |

## Task graph and the one red boundary

Tasks 1, 2 keep every project green. **Task 3 changes `MvsmfFileService`'s constructor, which leaves `LizTerm.App` and `LizTerm.App.Tests` unable to compile** (they still pass a `HostCredentialProvider`); Task 4 restores them (and carries the prompt-reason change, which its holder needs to compile). This is the plan's one deliberate red boundary, stated in Task 3's commit. Tasks 5 and 6 keep green.

---

### Task 1: Core — the token contract

Add the new types alongside the credential ones. Nothing consumes them yet, so every project stays green. `SignOutAsync` is a **default interface method** returning `Task.CompletedTask`, so implementers and fakes need not change until they choose to.

**Files:**
- Create: `src/LizTerm.Core/HostFiles/HostSessionToken.cs`
- Modify: `src/LizTerm.Core/HostFiles/HostFileException.cs` (the enum)
- Modify: `src/LizTerm.Core/HostFiles/IHostFileService.cs`
- Test: `tests/LizTerm.Core.Tests/HostFiles/HostSessionTokenTests.cs`

**Interfaces:**
- Produces: `HostSessionToken(string value)` with `Value`; `HostTokenRequest(HostSessionToken? Rejected)`; `delegate Task<HostSessionToken> HostSignIn(HostCredentials credentials, CancellationToken ct)`; `delegate ValueTask<HostSessionToken?> HostTokenProvider(HostTokenRequest request, HostSignIn signIn, CancellationToken ct)`; `HostFileErrorKind.Unsupported`; `IHostFileService.SignOutAsync(HostSessionToken token, CancellationToken ct = default)`.

- [ ] **Step 1: Write the failing test**

Create `tests/LizTerm.Core.Tests/HostFiles/HostSessionTokenTests.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.Core.HostFiles;

namespace LizTerm.Core.Tests.HostFiles;

public class HostSessionTokenTests
{
    [Fact]
    public void ToString_never_shows_the_token()
    {
        var token = new HostSessionToken("LtpaToken2-abc123==");
        Assert.Equal("LtpaToken2-abc123==", token.Value);
        Assert.DoesNotContain("abc123", token.ToString());
    }

    [Fact]
    public void A_request_names_the_rejected_token_without_its_value()
    {
        var rejected = new HostSessionToken("LtpaToken2-abc123==");
        var request = new HostTokenRequest(rejected);
        Assert.Same(rejected, request.Rejected);
        Assert.DoesNotContain("abc123", request.ToString());
        Assert.Null(new HostTokenRequest(null).Rejected);
    }
}
```

- [ ] **Step 2: Run it; it fails to compile (types not defined)**

Run: `dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~HostSessionTokenTests"`
Expected: build error, `HostSessionToken`/`HostTokenRequest` not found.

- [ ] **Step 3: Add the types**

Create `src/LizTerm.Core/HostFiles/HostSessionToken.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

namespace LizTerm.Core.HostFiles;

/// <summary>An opaque session token held in memory only, traded for a password at sign-in. A class rather than a
/// record so that no generated <c>ToString</c> can print the value.</summary>
public sealed class HostSessionToken(string value)
{
    public string Value { get; } = value;

    public override string ToString() => "HostSessionToken(value hidden)";
}

/// <param name="Rejected">The token the host has just refused (an expired or reaped session), or null on first
/// need. A provider that still holds this instance obtains a new token; one that has moved on answers the newer
/// token.</param>
public sealed record HostTokenRequest(HostSessionToken? Rejected);

/// <summary>A service's own sign-in: credentials in, a session token out. Throws <see cref="HostFileException"/>
/// with <see cref="HostFileErrorKind.Unauthenticated"/> for a refused password and
/// <see cref="HostFileErrorKind.Unsupported"/> for a host with no sign-in service.</summary>
public delegate Task<HostSessionToken> HostSignIn(HostCredentials credentials, CancellationToken cancellationToken);

/// <summary>How a service asks for a session token. It is handed the service's own <paramref name="signIn"/> to call
/// when it needs a fresh one. Answers null when the user cancels. Parallel operations may call it concurrently, so
/// an implementation serialises, and on a request whose <see cref="HostTokenRequest.Rejected"/> is no longer the
/// token it holds, answers the newer token without signing in again.</summary>
public delegate ValueTask<HostSessionToken?> HostTokenProvider(HostTokenRequest request, HostSignIn signIn, CancellationToken cancellationToken);
```

In `src/LizTerm.Core/HostFiles/HostFileException.cs`, add to the `HostFileErrorKind` enum, after `Unauthenticated`:

```csharp
    /// <summary>The host is not an mvsMF that supports sign-in (no authenticate route); it is too old.</summary>
    Unsupported,
```

In `src/LizTerm.Core/HostFiles/IHostFileService.cs`, add after `DeleteAsync`:

```csharp
    /// <summary>Ends the session the token names. Best effort: a host that has already forgotten the token is a
    /// success. The default does nothing, for a service that holds no session.</summary>
    Task SignOutAsync(HostSessionToken token, CancellationToken cancellationToken = default) => Task.CompletedTask;
```

- [ ] **Step 4: Run it; it passes**

Run: `dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~HostSessionTokenTests"`
Expected: PASS. Then `dotnet build LizTerm.slnx --no-incremental 2>&1 | grep -c " warning "` prints `0`.

- [ ] **Step 5: Commit**

```bash
git add src/LizTerm.Core/HostFiles/HostSessionToken.cs src/LizTerm.Core/HostFiles/HostFileException.cs src/LizTerm.Core/HostFiles/IHostFileService.cs tests/LizTerm.Core.Tests/HostFiles/HostSessionTokenTests.cs
git commit -m "Add the mvsMF session-token contract to Core

A held token, a request that names a rejected one, the backend's own
sign-in, and the provider that trades a password for a token. Sign-out
is a default interface method, so nothing else changes yet.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 2: The login fixtures

The backend tests in Task 3 need the login exchanges. Two are recorded from the host; one (a pre-1.1.0 host) is written by hand because no such host is available. Logout, the stale-token 401 and the anonymous probe 401 are exercised with inline `RecordedHandler` responses in Task 3, not fixtures, so they are not recorded here.

**Files:**
- Create: `tests/LizTerm.Backend.Mvsmf.Tests/Fixtures/{login-200,login-401,login-404}.http`
- Modify: `tests/LizTerm.Backend.Mvsmf.Tests/Fixtures/README.md`
- Modify: `tests/LizTerm.Backend.Mvsmf.Tests/FixtureTests.cs` (the guard reads the header block only)

**Interfaces:**
- Produces: fixtures `login-200` (200 + `Set-Cookie: LtpaToken2=<token>` + success body), `login-401` (401 + z/OSMF login-failed body), `login-404` (404, synthetic, a host with no authenticate route).

- [ ] **Step 1: Record the two real login exchanges**

Set the environment (Prerequisites), then from the repository root:

```bash
tools/record-mvsmf-fixture.sh login-200 POST services/authenticate -H 'X-CSRF-ZOSMF-HEADER: LizTerm' -H 'Content-Type: application/x-www-form-urlencoded'
MVSMF_BAD_PASSWORD=1 tools/record-mvsmf-fixture.sh login-401 POST services/authenticate -H 'X-CSRF-ZOSMF-HEADER: LizTerm' -H 'Content-Type: application/x-www-form-urlencoded'
```

Expected first lines: `login-200: HTTP/1.1 200 OK`, `login-401: HTTP/1.1 401 Unauthorized`.

- [ ] **Step 2: Check the recordings**

Run:

```bash
grep -h 'LtpaToken2=' tests/LizTerm.Backend.Mvsmf.Tests/Fixtures/login-200.http
tail -c 80 tests/LizTerm.Backend.Mvsmf.Tests/Fixtures/login-200.http; echo
tail -c 120 tests/LizTerm.Backend.Mvsmf.Tests/Fixtures/login-401.http; echo
```

Expected: `login-200` has exactly `Set-Cookie: LtpaToken2=<token>; Path=/; HttpOnly; SameSite=Strict`, and its body ends `{"returnCode":0,"reasonCode":0,"message":"Success."}`; `login-401` ends with a body whose `"reasonCode":1` and a `"Login failed` message. If `login-401` carries a `Set-Cookie`, that is fine (the redaction already handled it). If either body differs in shape from this, stop and report it, since Task 3's parser depends on it.

- [ ] **Step 3: Make the fixture guard read the header block only**

`FixtureTests.No_fixture_holds_credentials_or_a_session_token` rejects any fixture containing the word "password",
and the host's login-failed body says "Check whether the user ID and password you use…", so `login-401` would fail
it as recorded. Anything LizTerm sent or was given (a credential, a token) can only be in the header block, so the
guard reads that block alone. In `tests/LizTerm.Backend.Mvsmf.Tests/FixtureTests.cs`, replace the body of that test
with:

```csharp
        foreach (var file in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Fixtures"), "*.http"))
        {
            var text = File.ReadAllText(file);
            // The body is the host's own words, and a failed login says "password"; anything LizTerm sent or was
            // given (a credential, a token) could only be in the header block.
            var head = text[..text.IndexOf("\n\n", StringComparison.Ordinal)];
            Assert.DoesNotContain("Authorization", head, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("password", head, StringComparison.OrdinalIgnoreCase);
            foreach (var line in head.Split('\n').Where(l => l.StartsWith("Set-Cookie:", StringComparison.OrdinalIgnoreCase)))
                Assert.StartsWith("Set-Cookie: LtpaToken2=<token>;", line, StringComparison.OrdinalIgnoreCase);
        }
```

Run: `dotnet test tests/LizTerm.Backend.Mvsmf.Tests --filter "FullyQualifiedName~FixtureTests"`
Expected: PASS (the existing fixtures have no "password" anywhere, so the loosened guard still holds them).

- [ ] **Step 4: Write the synthetic pre-1.1.0 fixture**

No pre-1.1.0 host is available, so write `login-404` by hand. Create `tests/LizTerm.Backend.Mvsmf.Tests/Fixtures/login-404.http` with exactly these bytes (a Unix-newline file, a blank line after the headers, then the body):

```
HTTP/1.1 404 Not Found
Server: HTTPD Server
Content-Type: text/html
Content-Language: en

<html><body>404 Not Found</body></html>
```

- [ ] **Step 5: Update the fixtures README**

In `tests/LizTerm.Backend.Mvsmf.Tests/Fixtures/README.md`, add rows after `info-401` (keep the table's order):

```markdown
| `login-200` | `POST services/authenticate` — 200 with `Set-Cookie: LtpaToken2` |
| `login-401` | the same with a wrong password — 401, `reasonCode 1` |
| `login-404` | a host with no authenticate route — 404 (hand-written; no pre-1.1.0 host was available) |
```

- [ ] **Step 6: Confirm the suite still builds and passes (no code references the new fixtures yet)**

Run: `dotnet test tests/LizTerm.Backend.Mvsmf.Tests 2>&1 | grep -E "Passed!|Failed!"`
Expected: `Passed!`. `FixtureTests` scans every `.http`; the three new files pass the header-block guard from Step 3 (`login-401`'s body says "password", which is why the guard reads the headers alone).

- [ ] **Step 7: Commit**

```bash
git add tests/LizTerm.Backend.Mvsmf.Tests/Fixtures tests/LizTerm.Backend.Mvsmf.Tests/FixtureTests.cs
git commit -m "Record the mvsMF login fixtures

login-200 and login-401 from the 1.1.0 host, login-404 hand-written
for a host with no authenticate route. The fixture guard now reads the
header block only, since the host's login-failed body says "password".

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 3: Backend — token authentication

The backend logs in with Basic to get the cookie, then sends the cookie on every request. **This changes `MvsmfFileService`'s public constructor, so `LizTerm.App` and `LizTerm.App.Tests` will not compile after this task; Task 4 restores them.** `LizTerm.Integration.Tests` is fixed here (its live tests are the backend's own).

**Files:**
- Modify: `src/LizTerm.Backend.Mvsmf/MvsmfFileService.cs`
- Modify: `tests/LizTerm.Backend.Mvsmf.Tests/MvsmfAuthTests.cs`
- Modify: `tests/LizTerm.Backend.Mvsmf.Tests/RecordedHandler.cs` (record the `Cookie` header)
- Modify: `tests/LizTerm.Backend.Mvsmf.Tests/{MvsmfListTests,MvsmfReadTests,MvsmfWriteTests,MvsmfErrorsTests}.cs` (the shared `Answering` helper moves to a token provider)
- Modify: `tests/LizTerm.Backend.Mvsmf.Tests/MvsmfTlsTests.cs` (construction; the refusal now happens at sign-in)
- Modify: `tests/LizTerm.Backend.Mvsmf.Tests/LoopbackHttpsServer.cs` (answers the login with a cookie)
- Modify: `tests/LizTerm.Integration.Tests/LiveMvsmfTests.cs`

**Interfaces:**
- Consumes: `HostSessionToken`, `HostTokenRequest`, `HostSignIn`, `HostTokenProvider`, `HostFileErrorKind.Unsupported` (Task 1); fixtures `login-200`, `login-401`, `login-404` (Task 2).
- Produces: `MvsmfFileService(MvsmfOptions options, HostTokenProvider tokens)` (public ctor) and the internal test ctor `MvsmfFileService(HttpMessageHandler handler, Uri baseUrl, HostTokenProvider tokens, TimeSpan? idleTimeout = null, MvsmfCertificateCheck? certificates = null)`; `Task<HostServerInfo?> ProbeAsync(CancellationToken)`; `SignInAsync`/`SignOutAsync` behaviour below. After login every request carries `Cookie: LtpaToken2=<token>` and `X-CSRF-ZOSMF-HEADER: LizTerm` and no `Authorization`.

- [ ] **Step 1: Record the Cookie and CSRF headers in the test handler**

In `tests/LizTerm.Backend.Mvsmf.Tests/RecordedHandler.cs`, extend `RecordedRequest` and its construction so tests can assert the cookie and that no `Authorization` is sent. Change the record to:

```csharp
internal sealed record RecordedRequest(HttpMethod Method, Uri Uri, string? Authorization, string? Cookie, string? Csrf, string? DataType, string? ContentType, byte[]? Body, IReadOnlyList<string> HeaderNames)
{
    public string BodyText => Body is null ? "" : Encoding.Latin1.GetString(Body);
}
```

and in `SendAsync`, build it with the two new values (place them in the same positions):

```csharp
        Requests.Add(new RecordedRequest(
            request.Method,
            request.RequestUri!,
            request.Headers.Authorization?.ToString(),
            request.Headers.TryGetValues("Cookie", out var cookies) ? string.Join("; ", cookies) : null,
            request.Headers.TryGetValues("X-CSRF-ZOSMF-HEADER", out var csrf) ? string.Join(",", csrf) : null,
            request.Headers.TryGetValues("X-IBM-Data-Type", out var types) ? string.Join(",", types) : null,
            request.Content?.Headers.ContentType?.ToString(),
            body,
            [.. request.Headers.Select(h => h.Key)]));
```

- [ ] **Step 2: Rewrite the auth tests (they fail to compile / fail)**

Replace the body of `tests/LizTerm.Backend.Mvsmf.Tests/MvsmfAuthTests.cs` from the `Answering` helper through the auth tests. The new shared helper is a token provider that trades a password for a token by calling `signIn`, caches it, and re-signs-in on a rejected token — the backend contract a real holder meets:

```csharp
    internal static readonly Uri Base = new("http://mvs.test:8080/zosmf");

    /// <summary>A token provider that signs in with <paramref name="credentials"/> the first time and after the host
    /// rejects the token it holds; it records each request's Rejected token for assertions.</summary>
    internal static HostTokenProvider Providing(List<HostSessionToken?> asked, HostCredentials credentials)
    {
        HostSessionToken? held = null;
        return async (request, signIn, ct) =>
        {
            asked.Add(request.Rejected);
            if (held is not null && !ReferenceEquals(request.Rejected, held)) return held;
            held = await signIn(credentials, ct);
            return held;
        };
    }

    private static string Basic(string userid, string password) =>
        new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.Latin1.GetBytes($"{userid}:{password}"))).ToString();
```

Then the tests (replace the existing auth tests, keeping the timeout/unreachable ones but updating their construction to `Providing`):

```csharp
    [Fact]
    public async Task Sign_in_posts_basic_and_holds_the_cookie_for_later_requests()
    {
        var handler = new RecordedHandler().Then("login-200").Then("info-200");
        using var service = new MvsmfFileService(handler, Base, Providing([], new HostCredentials("MVSCE02", "pw")));

        await service.GetServerInfoAsync(TestContext.Current.CancellationToken);

        var login = handler.Requests[0];
        Assert.Equal(HttpMethod.Post, login.Method);
        Assert.Equal("http://mvs.test:8080/zosmf/services/authenticate", login.Uri.ToString());
        Assert.Equal(Basic("MVSCE02", "pw"), login.Authorization);
        Assert.Equal("LizTerm", login.Csrf);
        var info = handler.Requests[1];
        Assert.Equal("http://mvs.test:8080/zosmf/info", info.Uri.ToString());
        Assert.Null(info.Authorization);
        Assert.Equal("LtpaToken2=<token>", info.Cookie);
        Assert.Equal("LizTerm", info.Csrf);
    }

    [Fact]
    public async Task A_bad_password_at_sign_in_is_unauthenticated()
    {
        var handler = new RecordedHandler().Then("login-401");
        using var service = new MvsmfFileService(handler, Base, Providing([], new HostCredentials("MVSCE02", "wrong")));

        var ex = await Assert.ThrowsAsync<HostFileException>(() => service.GetServerInfoAsync(TestContext.Current.CancellationToken));

        Assert.Equal(HostFileErrorKind.Unauthenticated, ex.Kind);
        Assert.DoesNotContain("wrong", ex.Message);
    }

    [Fact]
    public async Task A_host_with_no_authenticate_route_is_unsupported()
    {
        var handler = new RecordedHandler().Then("login-404");
        using var service = new MvsmfFileService(handler, Base, Providing([], new HostCredentials("MVSCE02", "pw")));

        var ex = await Assert.ThrowsAsync<HostFileException>(() => service.GetServerInfoAsync(TestContext.Current.CancellationToken));

        Assert.Equal(HostFileErrorKind.Unsupported, ex.Kind);
    }

    [Fact]
    public async Task An_expired_token_signs_in_again_and_repeats_the_request_once()
    {
        var handler = new RecordedHandler()
            .Then("login-200")
            .Then(HttpStatusCode.Unauthorized, """{"rc":8}""")
            .Then("login-200")
            .Then("info-200");
        var asked = new List<HostSessionToken?>();
        using var service = new MvsmfFileService(handler, Base, Providing(asked, new HostCredentials("MVSCE02", "pw")));

        await service.GetServerInfoAsync(TestContext.Current.CancellationToken);

        // First provider call has no rejected token; the second names the token the host refused.
        Assert.Equal(2, asked.Count);
        Assert.Null(asked[0]);
        Assert.NotNull(asked[1]);
        Assert.Equal(4, handler.Requests.Count);
        Assert.All(handler.Requests.Where(r => r.Uri.AbsolutePath.EndsWith("/info")), r => Assert.Null(r.Authorization));
    }

    [Fact]
    public async Task A_second_401_fails_as_unauthenticated()
    {
        var handler = new RecordedHandler()
            .Then("login-200")
            .Then(HttpStatusCode.Unauthorized, """{"rc":8}""")
            .Then("login-200")
            .Then(HttpStatusCode.Unauthorized, """{"rc":8}""");
        using var service = new MvsmfFileService(handler, Base, Providing([], new HostCredentials("MVSCE02", "pw")));

        var ex = await Assert.ThrowsAsync<HostFileException>(() => service.GetServerInfoAsync(TestContext.Current.CancellationToken));
        Assert.Equal(HostFileErrorKind.Unauthenticated, ex.Kind);
    }

    [Fact]
    public async Task A_cancelled_prompt_sends_nothing_after_login()
    {
        var handler = new RecordedHandler();
        HostTokenProvider cancels = (_, _, _) => ValueTask.FromResult<HostSessionToken?>(null);
        using var service = new MvsmfFileService(handler, Base, cancels);

        var ex = await Assert.ThrowsAsync<HostFileException>(() => service.GetServerInfoAsync(TestContext.Current.CancellationToken));

        Assert.Equal(HostFileErrorKind.Unauthenticated, ex.Kind);
        Assert.Equal("Sign-in was cancelled.", ex.Message);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Sign_out_deletes_the_session_and_a_dead_token_is_fine()
    {
        var handler = new RecordedHandler().Then(HttpStatusCode.NoContent).Then(HttpStatusCode.Unauthorized);
        using var service = new MvsmfFileService(handler, Base, Providing([], new HostCredentials("MVSCE02", "pw")));

        await service.SignOutAsync(new HostSessionToken("tok"), TestContext.Current.CancellationToken);
        await service.SignOutAsync(new HostSessionToken("tok"), TestContext.Current.CancellationToken); // 401, still fine

        Assert.Equal(2, handler.Requests.Count);
        Assert.All(handler.Requests, r => Assert.Equal(HttpMethod.Delete, r.Method));
        Assert.All(handler.Requests, r => Assert.Equal("services/authenticate", r.Uri.AbsolutePath.TrimStart('/')["zosmf/".Length..]));
        Assert.All(handler.Requests, r => Assert.Equal("LtpaToken2=tok", r.Cookie));
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, "info-200", true)]
    [InlineData(HttpStatusCode.Unauthorized, null, false)]
    public async Task Probe_answers_the_info_when_anonymous_works_and_null_on_401(HttpStatusCode status, string? fixture, bool hasInfo)
    {
        var handler = fixture is null ? new RecordedHandler().Then(status, "") : new RecordedHandler().Then(fixture);
        var asked = new List<HostSessionToken?>();
        using var service = new MvsmfFileService(handler, Base, Providing(asked, new HostCredentials("MVSCE02", "pw")));

        var info = await service.ProbeAsync(TestContext.Current.CancellationToken);

        Assert.Equal(hasInfo, info is not null);
        Assert.Empty(asked); // the probe never signs in
        var request = Assert.Single(handler.Requests);
        Assert.Null(request.Authorization);
        Assert.Null(request.Cookie);
        Assert.Equal("http://mvs.test:8080/zosmf/info", request.Uri.ToString());
    }
```

Keep the existing `Info_version_fields_prefer_the_full_version`, `The_production_client_never_times_out_a_whole_transfer`, `A_refused_connection_is_unreachable`, `An_unreadable_answer_is_a_server_error`, `A_connect_timeout_is_unreachable`, `A_slow_sign_in_is_not_a_host_timeout`, `A_slow_retry_prompt_is_not_a_host_timeout`, and `The_production_handler_connects_within_ten_seconds_and_keeps_no_cookies` tests, but change each construction from `Answering(...)` / `HostCredentialProvider` to `Providing(...)` / a `HostTokenProvider`, and prepend a `.Then("login-200")` to every handler whose service reaches the host (the slow-sign-in and version tests replay `info-200`, so they now need a preceding `login-200`; the connect-timeout and refused-connection tests fail before login, so they do not). For `A_slow_sign_in_is_not_a_host_timeout`, the slow step is the provider; wrap the delay in the `HostTokenProvider` body and have it call `signIn`.

Also update every other backend test file's shared service builder. In `MvsmfListTests`, `MvsmfReadTests`, `MvsmfWriteTests`, the `Service(handler)` helper currently calls `MvsmfAuthTests.Answering([], new HostCredentials(...))`; change it to `MvsmfAuthTests.Providing([], new HostCredentials("MVSCE02", "pw"))` and prepend `.Then("login-200")` to each handler chain those tests build (each operation now signs in first). Where a test asserts on `handler.Requests[0]` for the operation, change the index to account for the leading login (`Requests[1]`), or filter by method/path. `MvsmfErrorsTests` operates on `MvsmfErrors` directly and does not build a service, so it is unchanged except any `Answering` reference (there is none).

`MvsmfTlsTests` builds its services with `Answering` too, and its loopback server answers every request with the one
info body, which the login would read as "the host set no session cookie". Two changes:

In `tests/LizTerm.Backend.Mvsmf.Tests/LoopbackHttpsServer.cs`, `AnswerAsync` answers a `POST` to
`/zosmf/services/authenticate` with a login and everything else with the body it was given. Replace from
`var body = Encoding.UTF8.GetBytes(json);` through `await tls.WriteAsync(body);` with:

```csharp
                var requestLine = Encoding.ASCII.GetString(seen.ToArray()).Split("\r\n")[0];
                var isLogin = requestLine.StartsWith("POST ", StringComparison.Ordinal)
                    && requestLine.Contains("/services/authenticate", StringComparison.Ordinal);
                var body = Encoding.UTF8.GetBytes(isLogin ? """{"returnCode":0,"reasonCode":0,"message":"Success."}""" : json);
                var cookie = isLogin ? "Set-Cookie: LtpaToken2=loopback; Path=/\r\n" : "";
                var head = Encoding.ASCII.GetBytes(
                    $"HTTP/1.1 200 OK\r\n{cookie}Content-Type: application/json\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
                await tls.WriteAsync(head);
                await tls.WriteAsync(body);
```

and change the class summary to "answers a login with a cookie and every other request with one JSON body". The
`Connection: close` after the login means the info request opens a second TLS connection, which the pin check
validates again, so the pinned test still exercises the pin.

In `tests/LizTerm.Backend.Mvsmf.Tests/MvsmfTlsTests.cs`, change `Service(int port, CertificatePin? pin)` and the
construction in `A_later_handshake_failure_is_not_blamed_on_an_old_certificate` from
`MvsmfAuthTests.Answering([], new HostCredentials("U", "p"))` to `MvsmfAuthTests.Providing([], new HostCredentials("U", "p"))`.
The first request on a service is now the login, so a certificate refusal is raised from `SignInAsync`: in
`An_untrusted_certificate_is_rejected_and_described`, the expected message becomes
`"Sign-in: the host's certificate is not trusted."` (the user never sees that prefix; `HostFileMessages.Describe`
maps `CertificateRejected` to a fixed sentence). `A_pinned_certificate_is_trusted` passes once the loopback answers
the login; `A_pin_for_another_certificate_is_rejected` asserts the kind only and needs no other change.

- [ ] **Step 3: Run the auth tests; they fail (no token path yet)**

Run: `dotnet test tests/LizTerm.Backend.Mvsmf.Tests --filter "FullyQualifiedName~MvsmfAuthTests"`
Expected: build error or failures — `MvsmfFileService` has no `HostTokenProvider` ctor, no `ProbeAsync`, no token `SignOutAsync`.

- [ ] **Step 4: Implement the token path**

`SignInAsync` checks the status and reads the `LtpaToken2` cookie; it does not parse the login body, so no new JSON type is needed. In `src/LizTerm.Backend.Mvsmf/MvsmfFileService.cs`:

Change the field and constructors from `HostCredentialProvider` to `HostTokenProvider`:

```csharp
    private readonly HostTokenProvider _tokens;
```

```csharp
    public MvsmfFileService(MvsmfOptions options, HostTokenProvider tokens)
        : this(new MvsmfCertificateCheck(options.PinnedCertificate), options.BaseUrl, tokens)
    {
    }

    private MvsmfFileService(MvsmfCertificateCheck certificates, Uri baseUrl, HostTokenProvider tokens)
        : this(CreateHandler(certificates), baseUrl, tokens, null, certificates)
    {
    }

    internal MvsmfFileService(HttpMessageHandler handler, Uri baseUrl, HostTokenProvider tokens,
        TimeSpan? idleTimeout = null, MvsmfCertificateCheck? certificates = null)
    {
        _http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        _base = new Uri(baseUrl.AbsoluteUri.TrimEnd('/') + "/");
        _tokens = tokens;
        _idle = idleTimeout ?? DefaultIdleTimeout;
        _certificates = certificates;
    }
```

In `CreateHandler`, replace the `basic-auth-every-request` comment above `UseCookies = false,` with:

```csharp
        // The session token is sent by hand as a Cookie header (real z/OSMF accepts only the cookie, not Bearer),
        // so no CookieContainer holds or drops it.
        UseCookies = false,
```

Rewrite `SendAsync`, `AskAsync`, and `SendOnceAsync`, and add `SignInAsync`, `ProbeAsync`, `SignOutAsync`. Replace the `SendAsync`/`AskAsync`/`SendOnceAsync` block with:

```csharp
    private async Task<HttpResponseMessage> SendAsync(Func<HttpRequestMessage> build, string what, IdleTimeout idle, CancellationToken cancellationToken)
    {
        idle.Pause();
        var token = await AskAsync(new HostTokenRequest(null), cancellationToken);
        var response = await SendOnceAsync(build, token, what, idle, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            response.Dispose();
            idle.Pause();
            token = await AskAsync(new HostTokenRequest(token), cancellationToken);
            response = await SendOnceAsync(build, token, what, idle, cancellationToken);
        }
        if (response.IsSuccessStatusCode) return response;
        using (response)
        {
            using var body = new MemoryStream();
            await CopyBodyAsync(response, body, idle, null, what, cancellationToken);
            throw MvsmfErrors.FromResponse(response.StatusCode, body.ToArray(), what);
        }
    }

    private async Task<HostSessionToken> AskAsync(HostTokenRequest request, CancellationToken cancellationToken) =>
        await _tokens(request, SignInAsync, cancellationToken)
        ?? throw new HostFileException(HostFileErrorKind.Unauthenticated, "Sign-in was cancelled.");

    /// <summary>The provider's <see cref="HostSignIn"/>: POST the credentials, take the LtpaToken2 cookie.</summary>
    private async Task<HostSessionToken> SignInAsync(HostCredentials credentials, CancellationToken cancellationToken)
    {
        const string what = "Sign-in";
        using var idle = new IdleTimeout(_idle, cancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Post, Url("services/authenticate"))
        {
            Content = new ByteArrayContent([]) { Headers = { ContentType = new MediaTypeHeaderValue("application/x-www-form-urlencoded") } },
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.Latin1.GetBytes($"{credentials.Userid}:{credentials.Password}")));
        AddCommonHeaders(request);
        HttpResponseMessage response;
        try
        {
            idle.Reset();
            response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, idle.Token);
        }
        catch (OperationCanceledException) when (idle.Expired(cancellationToken)) { throw TimedOut(what); }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested && ex.InnerException is TimeoutException)
        {
            throw new HostFileException(HostFileErrorKind.Unreachable,
                $"{what}: cannot reach the host (no answer within {ConnectTimeout.TotalSeconds:0} s).", inner: ex);
        }
        catch (HttpRequestException ex) { throw Unreachable(what, ex); }
        using (response)
        {
            if (response.StatusCode == HttpStatusCode.Unauthorized)
                throw new HostFileException(HostFileErrorKind.Unauthenticated, "The host rejected the userid or password.");
            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed)
                throw new HostFileException(HostFileErrorKind.Unsupported,
                    "This host does not support sign-in; LizTerm needs mvsMF 1.1.0 or later.");
            if (!response.IsSuccessStatusCode)
            {
                using var body = new MemoryStream();
                await CopyBodyAsync(response, body, idle, null, what, cancellationToken);
                throw MvsmfErrors.FromResponse(response.StatusCode, body.ToArray(), what);
            }
            if (TokenFromCookies(response) is not { } token)
                throw new HostFileException(HostFileErrorKind.ServerError, "Sign-in: the host set no session cookie.");
            return token;
        }
    }

    /// <summary>The unauthenticated reachability check for the Test button: the info when the host answers it
    /// without credentials, null for a 401 (reachable, sign-in needed).</summary>
    public async Task<HostServerInfo?> ProbeAsync(CancellationToken cancellationToken = default)
    {
        const string what = "Server information";
        using var idle = new IdleTimeout(_idle, cancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Get, Url("info"));
        AddCommonHeaders(request);
        HttpResponseMessage response;
        try
        {
            idle.Reset();
            response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, idle.Token);
        }
        catch (OperationCanceledException) when (idle.Expired(cancellationToken)) { throw TimedOut(what); }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested && ex.InnerException is TimeoutException)
        {
            throw new HostFileException(HostFileErrorKind.Unreachable,
                $"{what}: cannot reach the host (no answer within {ConnectTimeout.TotalSeconds:0} s).", inner: ex);
        }
        catch (HttpRequestException ex) { throw Unreachable(what, ex); }
        using (response)
        {
            if (response.StatusCode == HttpStatusCode.Unauthorized) return null;
            if (!response.IsSuccessStatusCode)
                throw new HostFileException(HostFileErrorKind.Unsupported, $"{what}: this URL does not answer as mvsMF.");
            var info = await ReadJsonAsync(response, MvsmfJsonContext.Default.MvsmfInfo, what, idle, cancellationToken);
            return new HostServerInfo("mvsMF", Blank(info.ZosmfFullVersion) ?? Blank(info.ZosmfVersion) ?? "unknown", Blank(info.ZosVersion) ?? "unknown");
        }
    }

    public async Task SignOutAsync(HostSessionToken token, CancellationToken cancellationToken = default)
    {
        using var idle = new IdleTimeout(_idle, cancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Delete, Url("services/authenticate"));
        request.Headers.Add("Cookie", $"LtpaToken2={token.Value}");
        AddCommonHeaders(request);
        try
        {
            idle.Reset();
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, idle.Token);
            // 204 is a clean logout; 401 means the host already forgot the token. Both are success.
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            // Best effort: the caller (window close) ignores a failed sign-out.
        }
    }
```

Rewrite `SendOnceAsync` to send the token as a cookie and no `Authorization`, and factor the common headers:

```csharp
    private async Task<HttpResponseMessage> SendOnceAsync(Func<HttpRequestMessage> build, HostSessionToken token,
        string what, IdleTimeout idle, CancellationToken cancellationToken)
    {
        using var request = build();
        request.Headers.Add("Cookie", $"LtpaToken2={token.Value}");
        AddCommonHeaders(request);
        try
        {
            idle.Reset();
            return await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, idle.Token);
        }
        catch (OperationCanceledException) when (idle.Expired(cancellationToken)) { throw TimedOut(what); }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested && ex.InnerException is TimeoutException)
        {
            throw new HostFileException(HostFileErrorKind.Unreachable,
                $"{what}: cannot reach the host (no answer within {ConnectTimeout.TotalSeconds:0} s).", inner: ex);
        }
        catch (HttpRequestException ex) { throw Unreachable(what, ex); }
    }

    private static void AddCommonHeaders(HttpRequestMessage request)
    {
        request.Headers.Add("X-CSRF-ZOSMF-HEADER", "LizTerm");
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("LizTerm", ProductVersion));
    }

    private static HostSessionToken? TokenFromCookies(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var cookies)) return null;
        foreach (var cookie in cookies)
        {
            var span = cookie.AsSpan();
            const string name = "LtpaToken2=";
            var at = span.IndexOf(name);
            if (at < 0) continue;
            var rest = span[(at + name.Length)..];
            var end = rest.IndexOf(';');
            var value = (end < 0 ? rest : rest[..end]).ToString();
            if (value.Length > 0) return new HostSessionToken(value);
        }
        return null;
    }
```

Remove the now-unused `GetServerInfoAsync` comment about `info-requires-auth`? No — keep `GetServerInfoAsync` as it is (it goes through `SendAsync`, which now signs in first). Leave its `info-requires-auth` comment; that entry stays (§6). Confirm `Blank` is still present (it is, used by the version cascade).

- [ ] **Step 5: Run the backend suite; it passes**

Run: `dotnet test tests/LizTerm.Backend.Mvsmf.Tests 2>&1 | grep -E "Passed!|Failed!"`
Expected: `Passed!`. If any list/read/write test fails on a request index, it is the leading `login-200`; fix the index or filter as Step 2 described.

- [ ] **Step 6: Update the live tests so Integration.Tests compiles**

In `tests/LizTerm.Integration.Tests/LiveMvsmfTests.cs`, `Connect` builds the service with a `HostCredentialProvider`. Change it to a `HostTokenProvider` that signs in with the live credentials:

```csharp
    private static MvsmfFileService Connect(Live live) =>
        new(new MvsmfOptions(live.Url), async (request, signIn, ct) => await signIn(live.Credentials, ct));
```

and change `A_rejected_password_is_asked_for_again_then_fails` to drive the token provider with a wrong password and assert it never succeeds:

```csharp
    [Fact(Timeout = LiveTimeout)]
    public async Task A_rejected_password_fails_as_unauthenticated()
    {
        var live = Require();
        using var service = new MvsmfFileService(new MvsmfOptions(live.Url),
            async (request, signIn, ct) => await signIn(new HostCredentials(live.Credentials.Userid, "not-the-password"), ct));

        var ex = await Assert.ThrowsAsync<HostFileException>(() => service.GetServerInfoAsync(TestContext.Current.CancellationToken));

        Assert.Equal(HostFileErrorKind.Unauthenticated, ex.Kind);
    }
```

Add one live sign-out test:

```csharp
    [Fact(Timeout = LiveTimeout)]
    public async Task Signs_in_lists_and_signs_out()
    {
        var live = Require();
        var ct = TestContext.Current.CancellationToken;
        HostSessionToken? captured = null;
        using var service = new MvsmfFileService(new MvsmfOptions(live.Url), async (request, signIn, c) =>
        {
            captured = await signIn(live.Credentials, c);
            return captured;
        });

        await service.ListDatasetsAsync(live.ScratchPds, ct);
        Assert.NotNull(captured);
        await service.SignOutAsync(captured!, ct); // 204; a second call would be 401, still fine
    }
```

`ProbeAsync` is not on `IHostFileService`, so the live-test file can call it on the concrete `MvsmfFileService`. Add:

```csharp
    [Fact(Timeout = LiveTimeout)]
    public async Task Probe_reports_the_host_needs_sign_in()
    {
        var live = Require();
        using var service = new MvsmfFileService(new MvsmfOptions(live.Url),
            (_, _, _) => ValueTask.FromResult<HostSessionToken?>(null));

        var info = await service.ProbeAsync(TestContext.Current.CancellationToken);

        Assert.Null(info); // /zosmf/info needs auth on this build (compat: info-requires-auth)
    }
```

- [ ] **Step 7: Build Integration.Tests (App is still red — that is expected)**

Run: `dotnet build tests/LizTerm.Integration.Tests 2>&1 | grep -c " warning "`
Expected: `0`. Run: `dotnet build src/LizTerm.App 2>&1 | grep -c "error"` — expect a non-zero count (the App still passes a `HostCredentialProvider`); this is the stated red boundary, fixed in Task 4.

- [ ] **Step 8: Commit (App does not compile yet; say so)**

```bash
git add src/LizTerm.Backend.Mvsmf tests/LizTerm.Backend.Mvsmf.Tests tests/LizTerm.Integration.Tests
git commit -m "Sign in to mvsMF for a token and send it as a cookie

The backend logs in with Basic once, holds the LtpaToken2 cookie
through the provider, sends it (never Authorization) on every request,
signs out with DELETE, and probes /info anonymously for the Test
button. A host with no authenticate route is Unsupported. LizTerm.App
does not compile until the next commit migrates it to the token
provider.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 4: App — the sign-in holder, access, factory and window

Migrate the App from credentials to a token, restoring the build. The holder is renamed and now trades a password for a token through the backend's `signIn`; the window signs out on close. The prompt request's `IsRetry` becomes a `SignInReason` here, because the holder passes one and the sign-in window shows it, and the old credential provider leaves Core with its last use.

**Files:**
- Rename + rewrite: `src/LizTerm.App/HostFiles/CredentialHolder.cs` → `src/LizTerm.App/HostFiles/SignInHolder.cs`
- Modify: `src/LizTerm.App/HostFiles/HostFileAccess.cs`
- Modify: `src/LizTerm.App/HostFileServiceFactory.cs`
- Modify: `src/LizTerm.App/Views/SessionWindow.axaml.cs`
- Modify: `src/LizTerm.App/Dialogs/ICredentialPrompt.cs` (`Reason` replaces `IsRetry`)
- Modify: `src/LizTerm.App/Views/SignInWindow.axaml`, `SignInWindow.axaml.cs` (the expired-session line)
- Modify: `src/LizTerm.Core/HostFiles/HostCredentials.cs` (delete `HostCredentialRequest` and `HostCredentialProvider`)
- Modify: `tests/LizTerm.App.Tests/Fakes/FakeCredentialPrompt.cs` (record the reason)
- Modify: `tests/CLAUDE.md` (the `FakeCredentialPrompt` note)
- Modify: `tests/LizTerm.App.Tests/Fakes/FakeHostFileService.cs` (record `SignOutAsync`)
- Rename + rewrite: `tests/LizTerm.App.Tests/HostFiles/CredentialHolderTests.cs` → `SignInHolderTests.cs`
- Modify: `tests/LizTerm.App.Tests/HostFiles/{HostFileAccessTests,HostFileConnectionTests}.cs`, `tests/LizTerm.App.Tests/HostFileServiceFactoryTests.cs`, `tests/LizTerm.App.Tests/Views/SessionWindowMvsmfTests.cs`
- Modify: `src/LizTerm.App/CLAUDE.md`

**Interfaces:**
- Consumes: Task 1's types; Task 3's `MvsmfFileService(MvsmfOptions, HostTokenProvider)`, `ProbeAsync`, `SignInAsync`, `SignOutAsync`.
- Produces: `SignInHolder(string profileName, string url, string? userid)` with `IsSignedIn`, `ProviderFor(ICredentialPrompt) : HostTokenProvider`, `Task SignOutAsync(IHostFileService service)`; `HostFileAccess.SignOutAsync()`; `HostFileServiceFactory.Create(Uri, CertificatePin?, HostTokenProvider)`; `enum SignInReason { First, Rejected, Expired }`; `CredentialPromptRequest(string ProfileName, string Url, string? Userid, SignInReason Reason)`.

- [ ] **Step 1: Change the prompt request, the fake prompt and the sign-in window**

The holder written in Step 4 passes a `SignInReason`, and the sign-in window reads `IsRetry` today, so all three
change together, before the holder.

In `src/LizTerm.App/Dialogs/ICredentialPrompt.cs`, replace the `IsRetry` parameter:

```csharp
/// <summary>Why the sign-in window is open.</summary>
public enum SignInReason
{
    First,
    Rejected,
    Expired,
}

/// <param name="ProfileName">Whose sign-in this is.</param>
/// <param name="Url">The REST base URL, shown so the user knows which host is asking.</param>
/// <param name="Userid">The userid to start with, or null.</param>
/// <param name="Reason">First need, a refused password, or an expired session.</param>
public sealed record CredentialPromptRequest(string ProfileName, string Url, string? Userid, SignInReason Reason);
```

**The fake and its note.**

In `tests/LizTerm.App.Tests/Fakes/FakeCredentialPrompt.cs`, change the `Calls.Add` line to record the reason:

```csharp
            Calls.Add($"ask:{request.Userid}:{request.Reason}");
```

The holder tests in Step 2 expect `ask:MVSCE02:First`, `:Expired` and `:Rejected`. In `tests/CLAUDE.md`, change the `FakeCredentialPrompt` note's `ask:<userid>:<IsRetry>` to `ask:<userid>:<Reason>`.

**The sign-in window.**

`SignInWindow` is a view; it has no unit test today, so verify by construction. In `src/LizTerm.App/Views/SignInWindow.axaml`, replace the fixed `RetryText` block with two lines bound by reason, or one line whose text is set in code. Simplest: keep one `TextBlock x:Name="ReasonText"` and set its text and visibility in code:

```xml
    <TextBlock x:Name="ReasonText" Foreground="#FF8080" TextWrapping="Wrap" IsVisible="False" />
```

In `SignInWindow.axaml.cs`, set it from the reason, and update the design-time ctor:

```csharp
    public SignInWindow() : this(new CredentialPromptRequest("MVS/CE", "http://mvs.example:8080/zosmf", "MVSCE02", SignInReason.Expired)) { }

    public SignInWindow(CredentialPromptRequest request)
    {
        InitializeComponent();
        HostText.Text = $"{request.ProfileName} · {request.Url}";
        ReasonText.Text = request.Reason switch
        {
            SignInReason.Rejected => "✗ The userid or password was not accepted. Try again.",
            SignInReason.Expired => "Your mvsMF session has expired. Sign in again.",
            _ => "",
        };
        ReasonText.IsVisible = request.Reason != SignInReason.First;
        UseridBox.Text = request.Userid ?? "";
        Opened += (_, _) => (string.IsNullOrEmpty(UseridBox.Text) ? UseridBox : PasswordBox).Focus();
    }
```

- [ ] **Step 2: Rewrite the holder tests (they fail to compile)**

Rename `tests/LizTerm.App.Tests/HostFiles/CredentialHolderTests.cs` to `SignInHolderTests.cs` and rewrite it against the token provider. The holder is handed a `signIn` by the backend; in tests, pass a fake `signIn` that returns a token from the credentials. Keep the behaviours the old tests pinned: one prompt shared by concurrent first requests; a rejected-token retry re-prompts; a rejected token that is no longer current answers the newer one; cancel shared; prefill; sign-out drops the token.

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.App.HostFiles;
using LizTerm.App.Tests.Fakes;
using LizTerm.Core.HostFiles;

namespace LizTerm.App.Tests.HostFiles;

public class SignInHolderTests
{
    private static readonly HostTokenRequest First = new(null);

    /// <summary>A signIn that mints a token from the password it is given, and counts its calls.</summary>
    private sealed class Signer
    {
        public int Count;
        public HostSignIn SignIn => (credentials, _) =>
        {
            Interlocked.Increment(ref Count);
            return Task.FromResult(new HostSessionToken($"tok:{credentials.Userid}:{credentials.Password}"));
        };
    }

    private static (SignInHolder Holder, FakeCredentialPrompt Prompt, HostTokenProvider Provider, Signer Signer) Create(string? userid = "MVSCE02")
    {
        var holder = new SignInHolder("MVS/CE", "http://mvs:8080/zosmf", userid);
        var prompt = new FakeCredentialPrompt();
        var signer = new Signer();
        return (holder, prompt, holder.ProviderFor(prompt), signer);
    }

    [Fact]
    public async Task Signs_in_once_and_answers_the_same_token_afterwards()
    {
        var (holder, prompt, provider, signer) = Create();

        var first = await provider(First, signer.SignIn, CancellationToken.None);
        var second = await provider(First, signer.SignIn, CancellationToken.None);

        Assert.Same(first, second);
        Assert.Equal(1, prompt.AskCount);
        Assert.Equal(1, signer.Count);
        Assert.True(holder.IsSignedIn);
        Assert.Equal(new CredentialPromptRequest("MVS/CE", "http://mvs:8080/zosmf", "MVSCE02", SignInReason.First), prompt.LastRequest);
    }

    [Fact]
    public async Task A_rejected_token_signs_in_again_as_expired()
    {
        var (_, prompt, provider, signer) = Create();

        var first = await provider(First, signer.SignIn, CancellationToken.None);
        var again = await provider(new HostTokenRequest(first), signer.SignIn, CancellationToken.None);

        Assert.NotSame(first, again);
        Assert.Equal(2, signer.Count);
        Assert.Equal(new[] { "ask:MVSCE02:First", "ask:MVSCE02:Expired" }, prompt.Calls);
    }

    [Fact]
    public async Task A_rejected_token_already_replaced_answers_the_newer_one_without_asking()
    {
        var (_, prompt, provider, signer) = Create();
        var stale = await provider(First, signer.SignIn, CancellationToken.None);
        var current = await provider(new HostTokenRequest(stale), signer.SignIn, CancellationToken.None);

        var answer = await provider(new HostTokenRequest(stale), signer.SignIn, CancellationToken.None);

        Assert.Same(current, answer);
        Assert.Equal(2, prompt.AskCount);
    }

    [Fact]
    public async Task A_cancelled_prompt_answers_null_and_holds_no_token()
    {
        var (holder, prompt, provider, signer) = Create();
        prompt.Answer = null;

        Assert.Null(await provider(First, signer.SignIn, CancellationToken.None));
        Assert.False(holder.IsSignedIn);
        Assert.Equal(0, signer.Count);
    }

    [Fact]
    public async Task Sign_out_ends_the_session_once_and_drops_the_token()
    {
        var (holder, _, provider, signer) = Create();
        await provider(First, signer.SignIn, CancellationToken.None);
        var service = new FakeHostFileService();

        await holder.SignOutAsync(service);
        await holder.SignOutAsync(service); // no token now, no second call

        Assert.False(holder.IsSignedIn);
        Assert.Equal(1, service.CallsSnapshot().Count(c => c.StartsWith("signout")));
    }
```

Keep the remaining old holder tests (concurrent first requests share one prompt; concurrent refusals prompt once; operations waiting on a cancelled prompt fail with it; the userid typed last prefills; a cancelled wait throws before asking), adapting each to `provider(request, signer.SignIn, token)` and `SignInReason`.

- [ ] **Step 3: Run; fails to compile**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~SignInHolderTests"`
Expected: build error (`SignInHolder`, `IsSignedIn` not defined).

- [ ] **Step 4: Rewrite the holder**

Rename the file, then replace `src/LizTerm.App/HostFiles/CredentialHolder.cs`'s contents (new path `SignInHolder.cs`):

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.App.Dialogs;
using LizTerm.Core.HostFiles;

namespace LizTerm.App.HostFiles;

/// <summary>The mvsMF sign-in for one session window (spec §3.2, §4.3): the password is asked for once, traded for a
/// session token through the backend's <see cref="HostSignIn"/>, and dropped; only the token is held, and it is
/// forgotten when the window closes. Prompts are serialised, so parallel operations that start together, or are
/// refused together, show one prompt between them.</summary>
public sealed class SignInHolder(string profileName, string url, string? userid)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _lock = new();
    private HostSessionToken? _token;
    private string? _lastUserid = userid;
    private int _cancellations;

    public bool IsSignedIn
    {
        get { lock (_lock) return _token is not null; }
    }

    public HostTokenProvider ProviderFor(ICredentialPrompt prompt) => (request, signIn, token) => GetAsync(prompt, request, signIn, token);

    /// <summary>Ends the session through <paramref name="service"/> if one is held, and drops the token. Any failure
    /// is the service's to swallow (best effort on window close).</summary>
    public async Task SignOutAsync(IHostFileService service)
    {
        HostSessionToken? token;
        lock (_lock) { token = _token; _token = null; }
        if (token is not null) await service.SignOutAsync(token);
    }

    private async ValueTask<HostSessionToken?> GetAsync(ICredentialPrompt prompt, HostTokenRequest request, HostSignIn signIn, CancellationToken token)
    {
        int cancellationsSeen;
        lock (_lock) cancellationsSeen = _cancellations;
        await _gate.WaitAsync(token);
        try
        {
            string? prefill;
            lock (_lock)
            {
                var refusedIsCurrent = request.Rejected is not null && ReferenceEquals(_token, request.Rejected);
                if (_token is { } current && !refusedIsCurrent) return current;
                if (_cancellations != cancellationsSeen && _token is null) return null;
                _token = null;
                prefill = _lastUserid;
            }
            var reason = request.Rejected is not null ? SignInReason.Expired : SignInReason.First;
            var answer = await prompt.AskAsync(new CredentialPromptRequest(profileName, url, prefill, reason));
            if (answer is null)
            {
                lock (_lock) _cancellations++;
                return null;
            }
            HostSessionToken signedIn;
            try
            {
                signedIn = await signIn(answer, token);
            }
            catch (HostFileException ex) when (ex.Kind == HostFileErrorKind.Unauthenticated)
            {
                // The host refused the password: ask again, marked as a rejection, until it takes or the user cancels.
                return await RetryAfterRejection(prompt, signIn, token);
            }
            lock (_lock)
            {
                _token = signedIn;
                _lastUserid = answer.Userid;
            }
            return signedIn;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async ValueTask<HostSessionToken?> RetryAfterRejection(ICredentialPrompt prompt, HostSignIn signIn, CancellationToken token)
    {
        while (true)
        {
            string? prefill;
            lock (_lock) prefill = _lastUserid;
            var answer = await prompt.AskAsync(new CredentialPromptRequest(profileName, url, prefill, SignInReason.Rejected));
            if (answer is null)
            {
                lock (_lock) _cancellations++;
                return null;
            }
            try
            {
                var signedIn = await signIn(answer, token);
                lock (_lock) { _token = signedIn; _lastUserid = answer.Userid; }
                return signedIn;
            }
            catch (HostFileException ex) when (ex.Kind == HostFileErrorKind.Unauthenticated)
            {
                // Ask again.
            }
        }
    }
}
```

> Note on the retry: the backend's `SendAsync` no longer distinguishes a bad password from an expired token (a bad password is caught inside `SignInAsync`), so the holder owns the "refused password, ask again" loop. This keeps the prompt's *Rejected* reason meaningful and matches spec §4.3 ("`Unauthenticated` prompts again with reason *rejected*").

- [ ] **Step 5: Migrate access, factory, window and fake**

In `src/LizTerm.App/HostFiles/HostFileAccess.cs`: rename the property type and calls from `CredentialHolder` to `SignInHolder`, rename `Credentials` to `SignIn` (or keep the property name `Credentials` — pick `SignIn` for clarity and update call sites), and replace `Forget`:

```csharp
    public SignInHolder SignIn { get; }
    // ...ctor:
        SignIn = new SignInHolder(profile.Name, Url?.ToString() ?? profile.HostFilesUrl ?? "", profile.HostFilesUserid);
    // Connect():
            ? new HostFileConnection(this, url, SignIn.ProviderFor(credentials), certificates)
    // replace Forget():
    /// <summary>Ends the session, through a service built for the URL, and drops the token. Best effort.</summary>
    public async Task SignOutAsync()
    {
        if (Url is not { } url || !SignIn.IsSignedIn) return;
        using var service = CreateService(url, Pin, SignIn.ProviderFor(NullPrompt.Instance));
        await SignIn.SignOutAsync(service);
    }
```

`SignOutAsync` needs a service to call `SignOutAsync` on but must not prompt (the token is already held). Add a `NullPrompt` in `src/LizTerm.App/HostFiles/` that throws if asked (it never will be, because a token is held):

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.App.Dialogs;
using LizTerm.Core.HostFiles;

namespace LizTerm.App.HostFiles;

/// <summary>Used only for sign-out, where a token is already held so the prompt is never reached.</summary>
internal sealed class NullPrompt : ICredentialPrompt
{
    public static readonly NullPrompt Instance = new();
    public Task<HostCredentials?> AskAsync(CredentialPromptRequest request) =>
        throw new InvalidOperationException("Sign-out must not prompt.");
}
```

`HostFileConnection` takes a `HostCredentialProvider` today; change its parameter and field type to `HostTokenProvider` (the delegate is just passed to `HostFileServiceCreator`). Update `HostFileServiceCreator` in `HostFileAccess.cs`:

```csharp
public delegate IHostFileService HostFileServiceCreator(Uri baseUrl, CertificatePin? pin, HostTokenProvider tokens);
```

and `CreateService`'s parameter type to `HostTokenProvider`.

In `src/LizTerm.App/HostFileServiceFactory.cs`:

```csharp
    public static IHostFileService Create(Uri baseUrl, CertificatePin? pin, HostTokenProvider tokens) =>
        new MvsmfFileService(new MvsmfOptions(baseUrl, pin), tokens);
```

and rewrite `CreateTester` to probe first, then sign in through the prompt, read info, and sign out. Because `ProbeAsync` is a backend method, build a concrete `MvsmfFileService` here (the factory is the one place App names the backend):

```csharp
    public static HostFileTester CreateTester(ICredentialPrompt prompt) => async (profileName, url, userid, pin, token) =>
    {
        var holder = new SignInHolder(profileName, url.ToString(), userid);
        using var service = new MvsmfFileService(new MvsmfOptions(url, pin), holder.ProviderFor(prompt));
        var probed = await service.ProbeAsync(token);
        if (probed is not null) return probed;              // the host answers /info without a sign-in
        var info = await service.GetServerInfoAsync(token); // signs in through the prompt, then reads /info
        await holder.SignOutAsync(service);
        return info;
    };
```

In `tests/LizTerm.App.Tests/Fakes/FakeHostFileService.cs`, add a `SignOutAsync` that records the call:

```csharp
    public Task SignOutAsync(HostSessionToken token, CancellationToken cancellationToken = default)
    {
        lock (_lock) Calls.Add("signout");
        return Task.CompletedTask;
    }
```

In `src/LizTerm.App/Views/SessionWindow.axaml.cs`, change the close handler from `_hostFiles?.Forget();` to a best-effort sign-out with a five-second cap. Since `OnClosed` is not async, fire-and-forget with a timeout is acceptable here because the token is dropped synchronously inside `SignOutAsync` before the network call:

```csharp
        // Best effort: drop the token now, end the session on the host without blocking the close.
        if (_hostFiles is { } hostFiles)
            _ = hostFiles.SignOutAsync().WaitAsync(TimeSpan.FromSeconds(5)).ContinueWith(_ => { }, TaskScheduler.Default);
```

(The token is cleared under the lock at the top of `SignInHolder.SignOutAsync`, so `IsSignedIn` is false immediately; the awaited part is only the network DELETE.)

Finally, delete `HostCredentialRequest` and the `HostCredentialProvider` delegate from
`src/LizTerm.Core/HostFiles/HostCredentials.cs`, keeping `HostCredentials` (the login's input). Spec §4.1 retires
them with their last use, and this task removes it; the Core test project references neither. Task 7's grep for the
old names depends on this.

- [ ] **Step 6: Update the other App tests**

- `HostFileAccessTests.Forget_drops_the_sign_in` → drive `SignIn.ProviderFor` with a token provider and assert `SignIn.IsSignedIn`, then `await access.SignOutAsync()` and assert `!SignIn.IsSignedIn`. Use a fake `signIn` as in the holder tests.
- `HostFileConnectionTests`: its `Host.Create` signature becomes `(Uri, CertificatePin?, HostTokenProvider)`; the field `Provider` becomes `HostTokenProvider?`; assertions on `HasCredentials` become `IsSignedIn`.
- `HostFileServiceFactoryTests`: `Anyone` becomes a `HostTokenProvider`; `Create_builds_the_mvsmf_service` passes it. `The_tester_signs_in_through_the_prompt_and_reports_a_host_it_cannot_reach` now hits `ProbeAsync` first, which throws `Unreachable` before any prompt, so the expectation changes to `prompt.Calls` being empty (the probe fails before sign-in). Update its assertions accordingly.
- `SessionWindowMvsmfTests.Closing_the_session_window_closes_the_browser_and_forgets_the_sign_in`: drive `shown.Access.SignIn.ProviderFor(...)` with a token provider, assert `IsSignedIn`, and after close assert `!IsSignedIn`.
- Anywhere else that references `CredentialHolder`, `.Credentials`, `.Forget()` or `HasCredentials`, update to `SignInHolder`, `.SignIn`, `.SignOutAsync()`, `IsSignedIn`.

- [ ] **Step 7: Build and run the App tests**

Run: `dotnet build LizTerm.slnx --no-incremental 2>&1 | grep -c " warning "` (expect `0`).
Run: `dotnet test tests/LizTerm.App.Tests 2>&1 | grep -E "Passed!|Failed!"` (expect `Passed!`).

- [ ] **Step 8: Update the App notes**

In `src/LizTerm.App/CLAUDE.md`, change the `CredentialHolder` bullet(s) to `SignInHolder`: it holds a session token, not a password; the password lives only inside the prompt and the backend's `SignInAsync`; `SignOutAsync` on window close ends the session; the editor's tester probes `/info` before prompting. Add the accepted double prompt ("Decisions recorded in this plan"): an `https` host whose certificate is not yet trusted refuses inside the sign-in, so after Connect Anyway the retried operation asks for the password again; the password is not held across the certificate prompt by design.

- [ ] **Step 9: Commit**

```bash
git add src/LizTerm.App src/LizTerm.Core/HostFiles/HostCredentials.cs tests/LizTerm.App.Tests tests/CLAUDE.md
git commit -m "Trade the mvsMF password for a session token in the App

SignInHolder (was CredentialHolder) holds a token, signs in through
the backend once, re-prompts on an expired or rejected token, and
signs out when the session window closes. The sign-in window says
when the session expired. The Test button probes /info before it asks
for a password. The credential provider is gone from Core.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 5: App — the Test button messages

The Test button reports the version floor and the unsupported host. (The prompt reason and the expired-session line are in Task 4, whose holder needs them to compile.)

**Files:**
- Modify: `src/LizTerm.App/ViewModels/ProfileEditorViewModel.cs`
- Modify: `src/LizTerm.App/HostFiles/HostFileMessages.cs` (the `Unsupported` arm)
- Modify: `tests/LizTerm.App.Tests/ViewModels/ProfileEditorMvsmfTests.cs`

**Interfaces:**
- Consumes: `HostFileErrorKind.Unsupported` (Task 1); the tester from Task 4.
- Produces: `ProfileEditorViewModel.IsSupportedVersion`; the `Unsupported` arm of `HostFileMessages.Describe`.

- [ ] **Step 1: The Test button messages**

In `tests/LizTerm.App.Tests/ViewModels/ProfileEditorMvsmfTests.cs`, the `Tester` fake returns a `HostServerInfo`. Add tests for the two new messages. The version check lives in the view model, so make the tester return a `HostServerInfo` with a low version and assert the message; and add an `Unsupported` failure row to `Test_failures_are_reported_in_words`:

```csharp
    [Fact]
    public async Task Test_rejects_a_host_below_the_minimum_version()
    {
        var tester = new Tester { Info = new HostServerInfo("mvsMF", "1.0.0-dev", "MVS 3.8j") };
        var vm = new ProfileEditorViewModel(Rest(), tester: tester.TestAsync);

        await vm.TestMvsmfCommand.ExecuteAsync(null);

        Assert.Equal("✗ mvsMF 1.0.0-dev is not supported; LizTerm needs mvsMF 1.1.0 or later.", vm.MvsmfTestResult);
    }
```

Add an `Info` property to the test `Tester` (defaulting to the 1.1.0 value it returns today) and an `Unsupported` row:

```csharp
    [InlineData(HostFileErrorKind.Unsupported, "This host does not support sign-in; LizTerm needs mvsMF 1.1.0 or later.", "✗ This host does not support sign-in; LizTerm needs mvsMF 1.1.0 or later.")]
```

In `src/LizTerm.App/ViewModels/ProfileEditorViewModel.cs`, after a successful `_tester` call, compare the version before reporting success:

```csharp
            var info = await _tester!(name, url, userid, MvsmfPinnedCertificate, CancellationToken.None);
            result = IsSupportedVersion(info.ProductVersion)
                ? $"✓ Connected: {info.Product} {info.ProductVersion} on {info.SystemVersion}"
                : $"✗ mvsMF {info.ProductVersion} is not supported; LizTerm needs mvsMF 1.1.0 or later.";
```

and add the helper (host-neutral parse: major.minor, an unparsable version is unsupported):

```csharp
    private static bool IsSupportedVersion(string version)
    {
        var head = version.Split('-', 2)[0];
        var parts = head.Split('.');
        if (parts.Length < 2 || !int.TryParse(parts[0], out var major) || !int.TryParse(parts[1], out var minor))
            return false;
        return major > 1 || (major == 1 && minor >= 1);
    }
```

The `Unsupported` failure from the tester falls through the existing `catch (Exception ex)` to `"✗ " + HostFileMessages.Describe(ex)`; add an `Unsupported` arm to `HostFileMessages.Describe` so the sentence is right:

In `src/LizTerm.App/HostFiles/HostFileMessages.cs`, add to the `host.Kind switch`:

```csharp
            HostFileErrorKind.Unsupported => host.Message,
```

(The backend already sets that message to "This host does not support sign-in; LizTerm needs mvsMF 1.1.0 or later.")

- [ ] **Step 2: Run the App tests and the full suite**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~ProfileEditorMvsmfTests|FullyQualifiedName~SignInHolderTests"` (expect PASS).
Run: `dotnet build LizTerm.slnx --no-incremental 2>&1 | grep -c " warning "` (expect `0`).

- [ ] **Step 3: Commit**

```bash
git add src/LizTerm.App tests/LizTerm.App.Tests
git commit -m "Refuse mvsMF below 1.1.0 from the Test button

The Test button reports a host below 1.1.0 or with no sign-in service
as unsupported.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 6: Docs — the compat log, the minimum version, and the guide

**Files:**
- Modify: `docs/mvsmf-compatibility.md`
- Modify: `docs/user-guide.md` and its generated `src/LizTerm.App/Assets/Docs/user-guide.html`
- Modify: `docs/privacy.md`
- Modify: `README.md`
- Modify: `CHANGELOG.md`

- [ ] **Step 1: Compat log**

In `docs/mvsmf-compatibility.md`:

Retire the `basic-auth-every-request` entry (remove the whole `### \`basic-auth-every-request\`` section) and add a line under `## Resolved on 1.1.0`:

```markdown
- `basic-auth-every-request`: LizTerm now signs in once with `POST /zosmf/services/authenticate`, holds the
  `LtpaToken2` cookie, and sends it (never `Authorization`) on every request; the tag is gone from the code.
```

Rewrite the `info-requires-auth` entry's `LizTerm:` line to note the probe uses the anonymous 401:

```markdown
- **LizTerm:** `/info` goes through the same authenticated path as everything else. The Test button first sends an
  unauthenticated `GET /info`; a 401 proves the URL reaches an mvsMF, so it asks for a password only then.
```

Add a new log-only entry after `info-requires-auth`:

```markdown
### `session-idle-timeout` (log only)

- **Source:** httpd expires an idle session after `SESSION_TIMEOUT` (default 30 minutes), refreshed on every
  request; there is no fixed maximum age yet (mvsMF/httpd #118).
- **LizTerm:** holds the token for the session window. A 401 from an operation is treated as an expired session:
  LizTerm signs in again with the held userid (prompting "Your mvsMF session has expired") and retries the
  operation once.
```

- [ ] **Step 2: Backend CLAUDE.md**

In `src/LizTerm.Backend.Mvsmf/CLAUDE.md`, replace the first bullet (the `HostCredentialProvider` description) with the token contract, and add the cookie-not-Bearer decision:

```markdown
- `MvsmfFileService` implements `IHostFileService` (Core) over one `HttpClient`. It signs in once with
  `POST /zosmf/services/authenticate` (Basic, `X-CSRF-ZOSMF-HEADER: LizTerm`), takes the `LtpaToken2` cookie, and
  sends it (`Cookie: LtpaToken2=…`, never `Authorization`) on every later request. The `HostTokenProvider` (the
  App's `SignInHolder`) holds the token and calls the backend's `SignInAsync` when it needs one; a 401 asks the
  provider again with the refused token and repeats the request once. **The password may exist only inside
  `SignInAsync` and the App's prompt** — never in a field, a message, a log or a `ToString()`.
- **Cookie, not Bearer.** LizTerm sends the token as the `LtpaToken2` cookie because real z/OSMF accepts only the
  cookie; `Authorization: Bearer` is an mvsMF convenience real z/OSMF does not honour. `UseCookies` stays false and
  the cookie is sent by hand.
```

- [ ] **Step 3: privacy.md**

In `docs/privacy.md`, rewrite the password sentence:

```markdown
**LizTerm never writes a password anywhere.** The only one it handles is the mvsMF REST sign-in: `SignInHolder`
asks for it once, trades it for a session token, and drops it; only the token is held, and it is forgotten when the
window closes. Profiles store a *userid*, never a password.
```

- [ ] **Step 4: The user guide's Signing-in section and the minimum version**

In `docs/user-guide.md`, under `### Setting it up`, the **Test** bullet — note the pre-sign-in probe:

```markdown
- **Test** checks the server is reachable, then signs in and asks it what it is, showing the answer (for example
  **✓ Connected: mvsMF 1.1.0 on MVS 3.8j**) or what went wrong. If the URL cannot be reached, it says so without
  asking for a password. A sign-in made for **Test** is ended right after.
```

Rewrite the `### Signing in` section:

```markdown
### Signing in

**LizTerm needs mvsMF 1.1.0 or later.** Older builds have no sign-in service, and LizTerm says so rather than
connecting.

The first time the browser reaches the host, it asks for your userid and password. LizTerm uses them once to sign
in, then keeps only a session token in memory; the password is not stored. Every browser operation in that session
uses the token, so you sign in once. mvsMF forgets an idle session after about 30 minutes, and then LizTerm asks
you to sign in again. Closing the session window signs you out. LizTerm cannot guarantee the password is wiped from
memory, because .NET gives no way to erase a string.

Your password crosses the network once, at sign-in. Over `http://` it is unencrypted, so keep plain `http` to a
network you trust, or put mvsMF behind a TLS reverse proxy and use an `https://` URL.
```

Keep the following `https` certificate paragraph as it is.

- [ ] **Step 5: README and CHANGELOG**

In `README.md`, the mvsMF bullet — add the sign-in-once clause and the version floor:

```markdown
- **An mvsMF dataset browser** (feature preview) for MVS 3.8j hosts running mvsMF 1.1.0 or later: sign in once, then
  list, download, upload and delete dataset members without touching the 3270 screen.
```

In `CHANGELOG.md`, under `## Unreleased`, add a line (newest first, near the other mvsMF entry):

```markdown
- **mvsMF sign-in is now token-based.** LizTerm signs in to mvsMF once per session window and holds a session token
  instead of resending your password on every request; it asks again when the session expires, and needs mvsMF
  1.1.0 or later.
```

- [ ] **Step 6: Regenerate the bundled guide**

Run: `LIZTERM_UPDATE_DOCS=1 dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~UserGuideAssetTests"`, then the same without the variable (expect PASS). Confirm `git status --porcelain` shows `src/LizTerm.App/Assets/Docs/user-guide.html` among the changed files.

- [ ] **Step 7: Check the tag/entry consistency and commit**

Run the check from PR 1's Task 9 (it lives in the plan; re-create it in the scratchpad if needed): every `// mvsMF-compat:` tag has an entry, and every non-log-only entry has a tag and a test. `basic-auth-every-request` must now appear in neither the code nor as a live entry. Then:

```bash
git add docs src/LizTerm.Backend.Mvsmf/CLAUDE.md src/LizTerm.App/Assets/Docs/user-guide.html README.md CHANGELOG.md
git commit -m "Document token sign-in and the mvsMF 1.1.0 minimum

The compat log retires basic-auth-every-request and records the idle
timeout; the guide, README, changelog and privacy note say LizTerm
signs in once for a token and needs mvsMF 1.1.0 or later.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 7: Full verification and the pull request

**Files:** none new.

- [ ] **Step 1: Zero warnings and the full suite**

Run: `dotnet build LizTerm.slnx --no-incremental 2>&1 | grep -c " warning "` (expect `0`).
Run: `dotnet test LizTerm.slnx 2>&1 | grep -E "Passed!|Failed!"` (expect every project `Passed!`; live tests skip without the variables).

- [ ] **Step 2: The live lane against 1.1.0**

In the shell with the four variables:
Run: `dotnet test tests/LizTerm.Integration.Tests --filter "FullyQualifiedName~LiveMvsmfTests"`
Expected: all pass, 0 skipped. The sign-in/list/sign-out, the probe-needs-sign-in, and the rejected-password tests all run.

- [ ] **Step 3: Dependency and header checks**

Run: `grep -rn "mvsMF\|Mvsmf\|LtpaToken2\|Bearer" src/LizTerm.Core --include='*.cs'` (expect no output — Core never names mvsMF or the token transport).
Run: `dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~RepositoryHeadersTests"` (expect PASS).
Run: `git grep -n "CredentialHolder\|HasCredentials\|\.Forget()\|IsRetry\|HostCredentialProvider\|HostCredentialRequest" src tests` (expect no output — the old names are gone).

- [ ] **Step 4: Push and open the PR**

Follow `superpowers:finishing-a-development-branch`. Push `claude/mvsmf-token-auth` and open a PR against `main` titled `Sign in to mvsMF with a session token` with a body summarising: the token contract in Core; the backend login/cookie/logout/probe; the App holder, expired re-prompt, and sign-out on close; the Test button probing first; mvsMF 1.1.0 as the enforced minimum; and that jobs, USS, console and dataset create/delete/rename stay on #17. End the body with the attribution line `🤖 Generated with [Claude Code](https://claude.com/claude-code)`.

- [ ] **Step 5: Hand over**

Report the PR URL. The status comment on #17 is posted after Robert merges (spec §6).

## Self-review notes

- Spec §3 (decisions): password dropped after login (Task 3/4), two-PR split (this is PR 2), cookie-not-Bearer (Task 3, Task 6), no new UI beyond the expired line and Test messages (Tasks 4, 5), minimum 1.1.0 (Task 5 enforce, Task 6 document).
- Spec §4.1 Core contract (Task 1); §4.2 backend (Task 3); §4.3 holder (Task 4); §4.4 prompt reason (Task 4); §4.5 Test button (Task 4 tester + Task 5 messages); §4.6 expired-token walk-through is exercised by `An_expired_token_signs_in_again…` (Task 3) and the holder's `A_rejected_token_signs_in_again_as_expired` (Task 4).
- Spec §6 fixtures: login-200/401/404 as files (Task 2); logout, stale-token and anonymous-probe as inline responses (Task 3, a deviation from the spec's fixture list, recorded here because they carry no body worth a fixture); compat log and docs (Task 6).
- Spec §7 minimum version: enforced in the login (`Unsupported`, Task 3), the Test button version compare (Task 5), documented in the guide with README/changelog/log pointing there (Task 6).
- Spec §8 testing: backend recorded (Task 3), App fake (Task 4/5), live (Task 3 added, Task 7 runs).
- Deviation from spec, recorded: the spec put the bad-password re-prompt in the backend's `SendAsync`; because `SignInAsync` catches the 401, the re-prompt loop moved into the holder (Task 4), which is where the *Rejected* reason is chosen. Same user-visible behaviour.
- Reviewed 2026-09-18 (Fable 5.1): Task 4 now carries the prompt reason its holder needs and deletes `HostCredentialRequest`/`HostCredentialProvider` (spec §4.1); Task 3 moves the TLS tests and loopback server to the login-first flow; Task 2 loosens the fixture guard to the header block, because the host's login-failed body says "password".
- Decision recorded: the double password prompt on an untrusted `https` host's first connection ("Decisions recorded in this plan"; App notes in Task 4).
- Not in this PR: jobs, USS, console, dataset create/delete/rename, paging (#144), Bearer, remembering the token across windows or on disk, real z/OSMF testing.
