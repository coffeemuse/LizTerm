# LizTerm Milestone 3, plan 3b: default trust for a bundled engine

Date: 2026-09-06. Parent spec: `2026-09-03-lizterm-v1-design.md`. Predecessors:
`2026-09-05-lizterm-m2-hardening-design.md`, whose section 10 deferred this to Milestone 3, and
`2026-09-06-lizterm-m3-ci-design.md`, which built the engine job this plan's test rides on. Status: approved
in discussion; awaiting review of this text.

## 1. Purpose

A statically linked b3270 carries the OpenSSL trust directory that was compiled into it at build time. Our
macOS engine is linked against Homebrew's static archives, so it carries Homebrew's:

```
$ strings native/out/osx-arm64/b3270 | grep openssl@3
/opt/homebrew/etc/openssl@3
/opt/homebrew/etc/openssl@3/cert.pem
/opt/homebrew/etc/openssl@3/certs
```

`cert.pem` there is a symlink into Homebrew's `ca-certificates` formula. It exists on the build machine, which
is why every test to date has had working default trust. It does not exist on a user's Mac. The shipped engine
therefore has **no trust anchors at all**, and every correctly issued host certificate fails verification. The
user's only ways forward are Connect Anyway, which turns verification off, or pinning a certificate that was
never suspect — both of which quietly train the user to bypass exactly the check that protects them.

This plan gives the engine the trust anchors the operating system already holds. It renumbers the rest of
Milestone 3: Linux engines become 3c, Windows 3d, publish and release 3e, the scheduled integration lane 3f.
Section 1 of the plan 3a spec is updated to match.

## 2. Facts that shape the design

Established by a spike on 2026-09-06 (throwaway; findings only). Every probe below ran against a loopback
`openssl s_server` holding a leaf signed by a private CA, so no result depends on SNI, DNS, or the public
internet. "Ours" is the binary `native/build/build-macos.sh` produces.

| Probe | Engine | Result |
|---|---|---|
| `caFile` = the signing CA | brew 4.5ga6 | `{"tls":{"secure":true,"verified":true,...}}` |
| `caFile` = an unrelated CA | brew 4.5ga6 | `verify failed: unable to get local issuer certificate (20)` |
| runtime `Set caFile`, 159-cert / 239 KB bundle (the machine's 158 roots plus the test CA) | brew 4.5ga6 | `verified:true` |
| runtime `Set caFile`, same bundle | **ours** | `verified:true` |
| runtime `Set caFile` = an unrelated CA | **ours** | error 20 |

1. b3270 builds chains past depth 0 from a `caFile` we supply. Until now the only CA path proven end to end
   was a depth-0 self-signed pin, which does not exercise chain building at all.
2. A 239 KB, 159-certificate bundle is fine, and the runtime `Set caFile` action — the path
   `B3270Session.ConnectAsync` already uses for pins — carries it. No new protocol surface is needed.
3. Our statically linked engine behaves exactly like Homebrew's. The fix does not depend on how the engine
   was built.
4. .NET reads the macOS keychain: `X509Store(StoreName.Root, StoreLocation.LocalMachine)` returned 158
   certificates in 325 ms, exporting to a 238 KB PEM in 4 ms. `CurrentUser` held none on that machine but is
   where user-added roots land on Windows, so both are read.
5. **An empty CA file is fatal.** b3270 answers a `caFile` naming a zero-byte or unparsable file with
   `Connection failed: TLS: CA database load (file "...") failed`, and the connect never happens. A trust
   source that yields nothing must therefore fall back to the engine's own default, never to an empty file.
6. A `caFile` naming a path that does not exist fails the same way, so the file must outlive the Connect run —
   the lifetime `WritePinFile` already establishes.
7. OpenSSL treats every certificate in `caFile` as a trust anchor. That is why `ConnectAsync` already checks
   `CertificateReader.CountCertificates` before relaxing `acceptHostname`, and it is why this plan exports
   roots only, never the intermediate store.

## 3. Behaviour

One rule, evaluated in `B3270Session.ConnectAsync`, replacing the current two-way one:

| Effective setting | `caFile` | `acceptHostname` |
|---|---|---|
| Verification off | empty | empty |
| Verification on, pin in force | the pin file, as today | `any` for a lone self-signed pin, else empty |
| Verification on, no pin, trust anchors available | **the roots file** | empty |
| Verification on, no pin, no trust anchors | empty (engine default) | empty |

Nothing the user sees changes except that hosts with correctly issued certificates now connect without a
prompt. The certificate prompt, pinning, `ConnectOptions`, and the profile format are untouched. The last row
preserves today's behaviour exactly, so a machine whose store cannot be read is no worse off than it is now.

## 4. Design

### 4.1 `LizTerm.Core.Security` (BCL only, so the dependency rule allows it)

`ITrustAnchorSource` with one member, `string? ExportPem()`: the concatenated PEM of the trust anchors, or
null when there are none. Null rather than an empty string, so fact 5's fallback is impossible to get wrong by
accident — an empty string is a value a caller might pass through, null is not.

`SystemTrustAnchors : ITrustAnchorSource` reads `StoreName.Root` from both `LocalMachine` and `CurrentUser`,
opening each `ReadOnly` inside its own try/catch (a store that throws contributes nothing rather than failing
the connect), unions them, drops duplicates by thumbprint, and concatenates `ExportCertificatePem()`. The
result is cached for the process lifetime behind a `Lazy<string?>`: the read costs 325 ms, the OS root set does
not change meaningfully inside one session, and a restart picks up any change. A static `Default` instance is
what production uses.

`TrustAnchorPem.Build(IEnumerable<X509Certificate2>)` is the pure half — dedupe, concatenate, return null when
empty — separated from the OS read so the interesting behaviour is unit-testable without depending on whatever
roots the test machine happens to hold.

### 4.2 `B3270Session`

A new init property, `TrustAnchors { get; init; } = NoTrustAnchors.Instance`, following `StartupTimeout`'s
pattern. The default yields nothing and `SessionFactory` — the one place the App names the backend — injects
`SystemTrustAnchors.Default`. Defaulting to the real store instead would make every backend test that connects
with a verifying profile read whatever roots the test machine happens to hold, which is both non-deterministic
and a behaviour no test asked for; it also matches how `ICertificateFetcher` is already wired, where the App
supplies the real implementation and the backend assumes nothing. `ConnectAsync` gains one branch: when verification is on and there
is no pin, ask the source for a PEM and, if it is non-null, write it with the existing `WritePinFile` mechanism as
`lizterm-roots-<guid>.pem`, owner-only on Unix. That method is renamed `WriteCaFile` as part of this plan,
since it now serves two callers and its name should say what it writes rather than who asked. The file is deleted in the same `finally` that already deletes
pin files, so it lives exactly as long as the Connect run and no longer.

Writing 239 KB per connect is deliberate over writing it once per session: it reuses a lifetime that is already
proven and already has tests, and it leaves nothing behind if the process dies. A connect is a human-scale
event; the write does not show up next to the TCP handshake.

The roots file is supplied whatever the engine's provenance. A `LIZTERM_B3270_PATH` override pointing at a
distro build with working system trust gets the same anchors it would have found anyway, and consistency is
worth more here than honouring a difference the user cannot see.

## 5. Testing and verification

Test-first throughout.

1. **`TrustAnchorPem` unit tests** (Core): certificates round-trip, duplicates collapse by thumbprint, an empty
   input returns null. Pure, no OS dependency.
2. **`SystemTrustAnchors` smoke test** (Core): reading does not throw and, when it returns non-null, the result
   parses back to at least one certificate. Deliberately not asserting a count — that is the machine's business,
   and the integration test below is what proves the real store works.
3. **Backend tests** against a fake source, asserting the `Set` action's arguments for each row of section 3's
   table: roots file when unpinned and verifying, pin file when pinned, empty when verification is off, empty
   when the source yields null. `LastCaFile` extends today's `LastPinFile` seam.
4. **Integration test — the one that would have caught this.** A loopback `SslStream` server holding a
   CA-signed leaf, a real b3270 from the test output, and an assertion that the `tls` indication reports
   `verified:true` with the CA supplied as the trust anchor, plus a negative control with an unrelated CA that
   must fail. It skips without a bundled engine and fails under `LIZTERM_REQUIRE_ENGINE`, exactly as
   `EngineSmokeTests` does, so the macOS CI job runs it.

   `TestCertificates.CaSigned()` in the Core tests already produces the pair, but its leaf carries no private
   key and the type is `internal`. The integration project links the file
   (`<Compile Include="../LizTerm.Core.Tests/Security/TestCertificates.cs" Link="Security/TestCertificates.cs" />`)
   rather than duplicating it, and gains an overload returning a leaf with a usable server key via the existing
   `WithUsableKey`.
5. `dotnet build LizTerm.slnx --no-incremental 2>&1 | grep -c " warning "` reports zero before this is called done.

## 6. Known gap, recorded here and not fixed by this plan

x3270 4.5ga6 **never sends SNI**: no file in the suite references `tlsext_host_name`, and
`Common/sio_openssl.c` sets up its context without it. A TLS host that decides which certificate to present
from the SNI name — anything behind a name-based front end or a shared TLS terminator — will hand b3270 a
certificate that cannot verify, no matter how its trust is configured. This is what made the first round of
spike probes against public hosts fail, and it is why every probe in section 2 uses a loopback server instead.

It does not block this plan: the gap is orthogonal to having trust anchors, and both of the hosts LizTerm is
tested against are reached by address. It is worth a bug report upstream, and if it needs fixing sooner it is a
few lines in `sio_openssl.c` that our own build could carry — a decision to take on its own evidence, not
folded in here.

Also noted for 3d: the `hello` indication reports version, build and copyright, but not the TLS provider. A
Windows engine built against Schannel does not take a `caFile` the way an OpenSSL build does, so the Windows
plan must decide how this rule applies there rather than assuming it carries over. The specific wrinkle to
weigh when it does: the Windows Root/`LocalMachine` store is populated on demand by the auto-root-update
program, not shipped complete, so an export taken at one moment can hold materially fewer anchors than Schannel
or .NET would actually trust a moment later. A host whose root has not yet been downloaded to this machine
would fail against our exported bundle while succeeding in a browser that triggers the download itself — a
false negative this plan's macOS keychain read does not have, because Apple ships that store complete.

## 7. Out of scope

Linux and Windows engines; anything about publishing or releases; shipping a CA bundle of our own, which this
design exists to avoid; a profile field for extra CA certificates, which pinning already covers; the SNI gap;
revocation checking; and any change to the certificate prompt, the pin format, or the profile file.

## 8. Deviations from this spec (as-built)

Rulings made in planning and execution, recorded here rather than edited into the sections above:

1. `TestCertificates.CaSignedServable()` (Task 5) could not sign the leaf the way `CaSigned()` does: an ECDSA
   root signing an RSA leaf is unsupported through the `CertificateRequest.Create(X509Certificate2, ...)`
   convenience overload, which only infers a signer from the issuer certificate when the two keys share an
   algorithm. It uses the explicit `Create(X500DistinguishedName, X509SignatureGenerator, ...)` overload instead,
   with `X509SignatureGenerator.CreateForECDsa(rootKey)` standing in for the inferred signer.
2. The positive integration test (Task 5) originally read `session.Tls` after cancelling the connect, which threw
   `NullReferenceException` every time: `ConnectAsync`'s cancellation path waits for the Disconnected state before
   returning, and that state clears `Tls`. The test now captures `Tls` into a local before cancelling.
3. The milestone renumbering this plan performs (Linux becomes 3c, Windows 3d, publish and release 3e, the
   integration lane 3f) is applied throughout the plan 3a spec, not just its section 1 — every other place in
   that document naming a milestone letter is corrected to match.
4. A whole-branch review found that `ConnectAsync` decided the trust settings and wrote the CA file on whatever
   context called it — the Avalonia UI thread for every session, since `App.OpenSession` starts the connect
   there and nothing in the codebase uses `ConfigureAwait(false)`. `TrustAnchors.ExportPem()` (210 ms on first
   read) and the 238 KB synchronous write therefore ran on the UI thread on every connect, TLS or not. The
   decide-and-write step (`B3270Session.DecideCaFile`) now runs inside a `Task.Run`, still honouring the pin
   short-circuit (a pin in force never calls `ExportPem()` at all) and still leaving the written path reachable
   by the existing `finally` that deletes it. The same review added a guard against a trust source returning an
   empty or whitespace-only PEM (treated the same as null, never written to `caFile`, and deliberately not
   applied to a pin's own PEM, which must keep failing loudly), made a failed `WriteCaFile` write delete its own
   partial file before rethrowing, and made `SystemTrustAnchors`'s cached read fail safe — returning null rather
   than caching an exception forever — against one malformed certificate in the store.
5. The same review found the test suite could not detect the regression this plan exists to prevent: mutating
   `SystemTrustAnchors.ReadStore` to always return `[]` (byte-for-byte the shipped bug) left every test passing.
   `SystemTrustAnchorsTests`'s store-read test now asserts a non-null PEM parses to at least five certificates
   (every platform LizTerm supports ships a root store at least that large) instead of returning silently on
   null, deliberately with no `Assert.Skip` for a bare container with no store at all: a skip keyed on "the PEM
   came back null" is indistinguishable from the exact regression the test exists to catch, so an escape hatch
   there would turn the guard back into a silent pass. `SystemTrustAnchorsTests` needs no engine and runs on all
   three CI legs, making it the only cross-platform guard against this bug. The positive case of
   `TrustAnchorVerificationTests` now injects the real OS-store bundle (`SystemTrustAnchors.Default.ExportPem()`)
   plus the test CA, rather than a hand-built PEM holding only the test CA, so CI proves the actual bundle
   `SessionFactory` hands the engine in production is OpenSSL-loadable end to end; the negative control is
   unchanged, since it exists to prove the opposite.
