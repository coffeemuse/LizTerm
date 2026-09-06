# LizTerm Milestone 2 Plan 3b (Hardening) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close Milestone 2 with real certificate pinning, the Vista TN3270 default keymap built as a table, the parked session and dialog fixes, and the backend, test, and live-lane cleanups from the plan 1, 2, and 3a reviews.

**Architecture:** The pin lives in the profile (fingerprint, subject, PEM chain) and becomes an effective TLS setting per connect: `B3270Session` writes the PEM to an owner-only temp file for the duration of the Connect run and sends one `Set` with `verifyHostCert`, `caFile`, and `acceptHostname` all explicit. The certificate itself is read by a Core `SslStreamCertificateFetcher` behind `ICertificateFetcher` (BCL only, so Core may hold it, and the integration lane needs it), injected into `SessionViewModel` like the clipboard. The keymap becomes a `Keymap` table of `KeyChord` to `TerminalKey` plus chord to typed text, built by `DefaultKeymap.Create(destructiveBackspace)`, with `Keymap.With` as the seam for user remapping later; `ModifierTapDetector` turns a Ctrl key pressed and released alone into a tap chord.

**Tech Stack:** .NET 10, Avalonia 12.1.2 (headless tests through Avalonia.Headless.XUnit), CommunityToolkit.Mvvm, xunit.v3 3.2.2 in VSTest mode, System.Net.Security and System.Security.Cryptography.X509Certificates from the BCL, b3270 4.5ga6.

**Spec:** `docs/superpowers/specs/2026-09-05-lizterm-m2-hardening-design.md`. Read it first; sections are cited below as "spec N.M". The 3a spec (`2026-09-05-lizterm-m2-polish-design.md`) explains the code this plan changes.

## Global Constraints

- Dependency rule: `LizTerm.Core` depends only on the BCL and never mentions Avalonia or b3270 names; `LizTerm.App` names `LizTerm.Backend.B3270` only in `src/LizTerm.App/SessionFactory.cs`.
- Rows and columns are zero-based everywhere in Core and App.
- `Nullable` and `ImplicitUsings` are on solution-wide; every package version lives in `Directory.Packages.props`. This plan adds no package.
- Zero build warnings: `dotnet build LizTerm.slnx 2>&1 | grep -c " warning "` must print `0`. Two traps this plan walks past: `Interlocked` on a `volatile` field is CS0420 (drop `volatile`, use `Volatile.Read`), and the `X509Certificate2` byte and copy constructors are obsolete (SYSLIB0057): use `X509CertificateLoader`.
- The pin file: `Path.GetTempPath()/lizterm-pin-<guid>.pem`, owner-read-write on Unix, deleted once the Connect run has answered (spec 4.1).
- The Set action per attempt (spec 4.1): pinned `Set(verifyHostCert,true,caFile,<path>,acceptHostname,any)`; unpinned verify-on `Set(verifyHostCert,true,caFile,,acceptHostname,)`; verify-off `Set(verifyHostCert,false,caFile,,acceptHostname,)`.
- Fingerprints: SHA-256 of the DER encoding as colon-separated upper-case hex pairs, `8C:13:6A:...` (spec 3.1).
- User-visible strings are exact and tested; copy them verbatim: checkbox `Trust this certificate for this profile`; titles `Certificate not verified` and `Certificate changed`; editor checkbox `Backspace erases the previous character (off: Backspace only moves the cursor left)`; editor line `Pinned certificate: SHA-256 {fingerprint}` beside a `Forget` button.
- Keymap defaults are the table in spec 6.2. Blink, selection, and clipboard behavior are unchanged.
- Test conventions: backend tests drive `FakeB3270Process`; App control tests use `[AvaloniaFact]`; view-model tests use `[Fact]` with `FakeEmulatorSession`; `TestContext.Current.CancellationToken` is the token to pass. Test names are sentences with underscores.
- Run one project with `dotnet test tests/<project>`, one class with `--filter "FullyQualifiedName~<Class>"`, one test with `--filter "FullyQualifiedName~<Class>.<Method>"`.
- Commit after every task; messages are one imperative sentence describing the behavior (see the examples), ending with a blank line and `Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>`.
- Live-lane tests skip without `LIZTERM_TEST_HOST`. On Robert's Mac, `source ~/.config/lizterm-test.env` in the shell that runs `dotnet test tests/LizTerm.Integration.Tests`; the gateway there is TLS with a self-signed certificate (`LIZTERM_TEST_TLS=1`, `LIZTERM_TEST_VERIFY_CERT=0`). Never commit a wire log of a run that typed a password.
- Work only inside this worktree. Never `cd` to `/Users/robert/ClaudeSandbox/LizTerm` (the main checkout) and never run `git stash` without a unique `-m` tag.

## Planning deviations from the spec

Decided here; Task 15 records them in the spec's as-built section.

1. `ICertificateFetcher`, `PresentedCertificate`, `CertificateReader`, and `SslStreamCertificateFetcher` live in `src/LizTerm.Core/Security/`, not `src/LizTerm.App/Certificates/` (spec 5.1). They use only the BCL, so the dependency rule allows it, and the integration project, which references only the backend, needs the fetcher for the live pinning test. `FakeCertificateFetcher` stays in the App tests.
2. `_verifyOverride` (spec 5.3) is removed. With Remember meaning pin, nothing sets it any more; the one-time path passes `ConnectOptions(VerifyCertificate: false)` for that attempt only, as before.
3. The certificate fetch runs under a fresh `CancellationTokenSource(ConnectTimeout)` rather than the connect's token (spec 5.3 step 1), because the connect's source is disposed before the prompt runs.
4. Escape in the File Transfer dialog is handled in the window's `OnKeyDown` and calls `Close()` (spec 7 named `IsCancel` on the Close button): the Running panel has no Close button, so a button-bound Escape would do nothing exactly where the spec wants it to cancel. Both routes go through `Closing` and `TryClose`.
5. `TerminalScreen.DestructiveBackspace` (the styled property) also defaults to `true`, alongside the profile default (spec 3.2), so an unbound control behaves like a new profile.
6. Reading each plan 3a task named by the review ("coverage gaps in tasks 1, 3, 4, 5, 6, 9, 10, 11, 12, 16") against the test projects found every bullet of the 3a spec's section 9 covered. The two real gaps found go in Task 13: `StopWireLog` when no log is active, and a prompt answered after the window was disposed. "Cancel disables after the first click" and "`DisposeAsync` stops the log only after Quit" already have tests (`Cancel_marks_cancelling_cancels_the_token_and_lands_in_done`, `Dispose_logs_the_quit_before_it_closes_the_log`) and are not repeated. The splash `_shownAt` item is already done (the splash times on a `Stopwatch`).

## File map

Create:
- `src/LizTerm.Core/Session/CertificatePin.cs`: the persisted pin.
- `src/LizTerm.Core/Security/PresentedCertificate.cs`, `CertificateReader.cs`, `ICertificateFetcher.cs`, `SslStreamCertificateFetcher.cs`: reading what a host presents and whether it can be pinned.
- `src/LizTerm.App/Dialogs/CertificatePromptRequest.cs`: the prompt's input.
- `src/LizTerm.App/Keyboard/KeyChord.cs`, `Keymap.cs`, `ModifierTapDetector.cs`: the keymap table and the tap state machine.
- `src/LizTerm.App/CommandRouting.cs`: CanExecute-checked command execution for hotkeys.
- Tests and helpers: `tests/LizTerm.Core.Tests/Security/TestCertificates.cs`, `CertificateReaderTests.cs`, `SslStreamCertificateFetcherTests.cs`; `tests/LizTerm.Backend.B3270.Tests/Wait.cs`, `EnvironmentCollection.cs`, `Fixtures/gateway-pinned-login.jsonl`; `tests/LizTerm.App.Tests/Wait.cs`, `EnvironmentCollection.cs`, `CommandRoutingTests.cs`, `Fakes/FakeCertificateFetcher.cs`, `Keyboard/KeymapTests.cs`, `Keyboard/ModifierTapDetectorTests.cs`, `Views/ProfileEditorWindowTests.cs`.

Modify: `SessionProfile.cs`, `ConnectOptions.cs`, `B3270Session.cs`, `SessionViewModel.cs`, `ICertificatePrompt.cs`, `AvaloniaCertificatePrompt.cs`, `CertificateWindow.axaml` and `.cs`, `ProfileEditorViewModel.cs`, `ProfileEditorWindow.axaml`, `App.axaml.cs`, `DefaultKeymap.cs`, `TerminalScreen.cs`, `SessionWindow.axaml` and `.cs`, `FileTransferWindow.axaml.cs`, `FileTransferViewModel.cs`, `AboutWindow.axaml`, `StartupArguments.cs`, the three integration-lane files, the fakes, the affected tests, `CLAUDE.md`, the v1 spec's section 6.5, the 3b spec, and the Fixtures README.

Delete: `tests/LizTerm.App.Tests/Keyboard/DefaultKeymapTests.cs` (replaced by `KeymapTests.cs`).

---

### Task 1: Core pin record, profile field, connect option, and the Backspace default

**Files:**
- Create: `src/LizTerm.Core/Session/CertificatePin.cs`
- Modify: `src/LizTerm.Core/Session/SessionProfile.cs`, `src/LizTerm.Core/Session/ConnectOptions.cs`
- Test: `tests/LizTerm.Core.Tests/Profiles/ProfileStoreTests.cs`, `tests/LizTerm.Core.Tests/Session/ConnectTypesTests.cs`, `tests/LizTerm.Core.Tests/Session/SessionTypesTests.cs`, `tests/LizTerm.App.Tests/ViewModels/ProfileViewModelsTests.cs`

**Interfaces:**
- Produces: `public sealed record CertificatePin(string Sha256, string Subject, string Pem)` in `LizTerm.Core.Session`; `SessionProfile.PinnedCertificate : CertificatePin?` (JSON `pinnedCertificate`, written as `null` when absent); `SessionProfile.DestructiveBackspace` defaulting to `true`; `public sealed record ConnectOptions(bool? VerifyCertificate = null, CertificatePin? Pin = null)`.

- [ ] **Step 1: Write the failing tests**

In `tests/LizTerm.Core.Tests/Profiles/ProfileStoreTests.cs`, add after `Save_then_LoadAll_round_trips_every_field`:

```csharp
    [Fact]
    public void A_pinned_certificate_round_trips_and_an_unpinned_profile_reads_back_null()
    {
        var store = new ProfileStore(_dir);
        var pin = new CertificatePin("8C:13:6A:01", "O = tn3270proxy quick-start, CN = localhost",
            "-----BEGIN CERTIFICATE-----\nZmFrZQ==\n-----END CERTIFICATE-----\n");
        store.Save(new SessionProfile { Name = "pinned", Host = "gw", UseTls = true, PinnedCertificate = pin });
        store.Save(new SessionProfile { Name = "plain", Host = "h" });

        var loaded = store.LoadAll();
        Assert.Equal(pin, loaded.Single(p => p.Name == "pinned").PinnedCertificate);
        Assert.Null(loaded.Single(p => p.Name == "plain").PinnedCertificate);
        // The source-generated context writes every property, so the absent pin is an explicit null (spec 3.1).
        var plainJson = Directory.GetFiles(_dir, "*.json").Select(File.ReadAllText).Single(j => j.Contains("\"name\": \"plain\""));
        Assert.Contains("\"pinnedCertificate\": null", plainJson);
    }
```

Replace `LoadAll_reads_older_files_with_destructive_backspace_off` with:

```csharp
    /// <summary>A file written before DestructiveBackspace existed has no such field and now reads as the new
    /// default (erase), which is what every x3270-family default keymap does (spec 2). A file that says false keeps
    /// false: the editor always writes the field, so a saved choice survives the default flip (spec 3.2).</summary>
    [Fact]
    public void LoadAll_reads_older_files_with_destructive_backspace_on_and_an_explicit_false_as_false()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "old.json"), """{"name":"old","host":"h","port":23}""");
        File.WriteAllText(Path.Combine(_dir, "off.json"), """{"name":"off","host":"h","port":23,"destructiveBackspace":false}""");
        var loaded = new ProfileStore(_dir).LoadAll();
        Assert.True(loaded.Single(p => p.Name == "old").DestructiveBackspace);
        Assert.False(loaded.Single(p => p.Name == "off").DestructiveBackspace);
    }
```

In `tests/LizTerm.Core.Tests/Session/ConnectTypesTests.cs`, replace `ConnectOptions_defaults_to_the_profile` with:

```csharp
    [Fact]
    public void ConnectOptions_defaults_to_the_profile()
    {
        Assert.Null(new ConnectOptions().VerifyCertificate);
        Assert.Null(new ConnectOptions().Pin);
        Assert.False(new ConnectOptions(VerifyCertificate: false).VerifyCertificate);
        var pin = new CertificatePin("AA", "CN=x", "pem");
        Assert.Same(pin, new ConnectOptions(Pin: pin).Pin);
    }
```

In `tests/LizTerm.Core.Tests/Session/SessionTypesTests.cs`, add:

```csharp
    [Fact]
    public void Backspace_erases_by_default_and_a_new_profile_has_no_pin()
    {
        var profile = new SessionProfile();
        Assert.True(profile.DestructiveBackspace);
        Assert.Null(profile.PinnedCertificate);
    }
```

In `tests/LizTerm.App.Tests/ViewModels/ProfileViewModelsTests.cs`, in `Editor_defaults_and_tls_port_flip`, change `Assert.False(vm.DestructiveBackspace);` to `Assert.True(vm.DestructiveBackspace);`.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~ProfileStoreTests|FullyQualifiedName~ConnectTypesTests|FullyQualifiedName~SessionTypesTests"`
Expected: build errors naming `CertificatePin`, `PinnedCertificate`, and `Pin` (the tests do not compile yet).

- [ ] **Step 3: Implement**

Create `src/LizTerm.Core/Session/CertificatePin.cs`:

```csharp
namespace LizTerm.Core.Session;

/// <summary>A host certificate the user chose to trust for a profile (spec 3.1). <paramref name="Pem"/> holds
/// every certificate the host presented, leaf first, as concatenated PEM blocks, and is what the engine verifies
/// against; <paramref name="Sha256"/> is the leaf's fingerprint as colon-separated upper-case hex pairs, the way
/// openssl prints it; <paramref name="Subject"/> is the leaf's subject, for display.</summary>
public sealed record CertificatePin(string Sha256, string Subject, string Pem);
```

Replace `src/LizTerm.Core/Session/SessionProfile.cs` with:

```csharp
namespace LizTerm.Core.Session;

public sealed record SessionProfile
{
    public string Name { get; init; } = "";
    public string Host { get; init; } = "";
    public int Port { get; init; } = 23;
    public bool UseTls { get; init; }
    public bool VerifyCertificate { get; init; } = true;
    /// <summary>The certificate trusted for this host, or null for the engine's default trust. Independent of
    /// <see cref="VerifyCertificate"/>: a pinned profile verifies, against the pin only; verification off ignores
    /// the pin without removing it (spec 3.1).</summary>
    public CertificatePin? PinnedCertificate { get; init; }
    /// <summary>3278/3279 model number, 2 through 5.</summary>
    public int Model { get; init; } = 2;
    public bool Extended { get; init; } = true;
    public string CodePage { get; init; } = "cp037";
    public string? LuName { get; init; }
    /// <summary>When true (the default, as in x3270's and wc3270's own base keymaps and Vista TN3270), the Backspace
    /// key erases the character to the left of the cursor (x3270's Erase action); when false it only moves the
    /// cursor left (BackSpace). The editor writes the field explicitly, so a saved choice survives.</summary>
    public bool DestructiveBackspace { get; init; } = true;
}
```

Replace `src/LizTerm.Core/Session/ConnectOptions.cs` with:

```csharp
namespace LizTerm.Core.Session;

/// <summary>One-shot choices for a single connect attempt. A null field means "as the profile says". A non-null
/// <paramref name="Pin"/> is verified against instead of the profile's pin; it is ignored when the effective
/// verify setting is off (spec 3.1).</summary>
public sealed record ConnectOptions(bool? VerifyCertificate = null, CertificatePin? Pin = null);
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/LizTerm.Core.Tests` then `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~ProfileViewModelsTests"`
Expected: all pass. (`FakeEmulatorSession` and the editor still compile: the new fields have defaults.)

- [ ] **Step 5: Commit**

```bash
git add src/LizTerm.Core tests/LizTerm.Core.Tests tests/LizTerm.App.Tests/ViewModels/ProfileViewModelsTests.cs
git commit -m "Carry a pinned certificate on the profile and a one-shot pin in ConnectOptions, and erase on Backspace by default

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 2: Reading a presented certificate and deciding whether it can be pinned

**Files:**
- Create: `src/LizTerm.Core/Security/PresentedCertificate.cs`, `src/LizTerm.Core/Security/CertificateReader.cs`
- Test: `tests/LizTerm.Core.Tests/Security/TestCertificates.cs`, `tests/LizTerm.Core.Tests/Security/CertificateReaderTests.cs`

**Interfaces:**
- Produces: `public sealed record PresentedCertificate(string Sha256, string Subject, string Pem, bool Pinnable, string? NotPinnableReason)`; `public static class CertificateReader { string Fingerprint(X509Certificate2); PresentedCertificate Read(IReadOnlyList<X509Certificate2> chain /* leaf first */); }`; the test helper `TestCertificates` (`SelfSigned(subject, notBefore, notAfter)`, `CaSigned()` returning `(Root, Leaf)`, `WithUsableKey(cert)`), which Task 3 reuses.

- [ ] **Step 1: Write the test helper and the failing tests**

Create `tests/LizTerm.Core.Tests/Security/TestCertificates.cs`:

```csharp
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace LizTerm.Core.Tests.Security;

/// <summary>Certificates made on the fly for the reader and fetcher tests. The self-signed one uses RSA because the
/// fetcher test serves it from an SslStream, and RSA server keys work on every TLS stack .NET runs on; the CA pair
/// is only ever read, so it uses ECDSA, whose Create overload needs no signature padding.</summary>
internal static class TestCertificates
{
    public static X509Certificate2 SelfSigned(string subject = "CN=localhost", DateTimeOffset? notBefore = null, DateTimeOffset? notAfter = null)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest(subject, key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(LocalhostNames());
        return request.CreateSelfSigned(notBefore ?? DateTimeOffset.UtcNow.AddDays(-1), notAfter ?? DateTimeOffset.UtcNow.AddDays(30));
    }

    /// <summary>A private CA and a leaf it signed. The leaf carries no private key.</summary>
    public static (X509Certificate2 Root, X509Certificate2 Leaf) CaSigned()
    {
        using var rootKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var rootRequest = new CertificateRequest("CN=LizTerm Test CA", rootKey, HashAlgorithmName.SHA256);
        rootRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        rootRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        rootRequest.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(rootRequest.PublicKey, false));
        var root = rootRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));

        using var leafKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var leafRequest = new CertificateRequest("CN=localhost", leafKey, HashAlgorithmName.SHA256);
        leafRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        leafRequest.CertificateExtensions.Add(LocalhostNames());
        var serial = new byte[8];
        RandomNumberGenerator.Fill(serial);
        serial[0] &= 0x7F;
        var leaf = leafRequest.Create(root, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(10), serial);
        return (root, leaf);
    }

    /// <summary>Re-imports through PKCS#12 so the private key is one the platform TLS stack can use as a server key:
    /// macOS refuses an ephemeral key for SslStream.AuthenticateAsServerAsync.</summary>
    public static X509Certificate2 WithUsableKey(X509Certificate2 certificate) =>
        X509CertificateLoader.LoadPkcs12(certificate.Export(X509ContentType.Pfx), null, X509KeyStorageFlags.DefaultKeySet);

    private static X509Extension LocalhostNames()
    {
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName("localhost");
        names.AddIpAddress(IPAddress.Loopback);
        return names.Build();
    }
}
```

Create `tests/LizTerm.Core.Tests/Security/CertificateReaderTests.cs`:

```csharp
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using LizTerm.Core.Security;

namespace LizTerm.Core.Tests.Security;

public class CertificateReaderTests
{
    [Fact]
    public void A_self_signed_certificate_is_pinnable_with_its_fingerprint_subject_and_pem()
    {
        using var cert = TestCertificates.SelfSigned("CN=gateway.test");
        var presented = CertificateReader.Read([cert]);

        Assert.True(presented.Pinnable, presented.NotPinnableReason);
        Assert.Null(presented.NotPinnableReason);
        Assert.Equal("CN=gateway.test", presented.Subject);
        Assert.Matches("^([0-9A-F]{2}:){31}[0-9A-F]{2}$", presented.Sha256);
        Assert.Equal(Convert.ToHexString(cert.GetCertHash(HashAlgorithmName.SHA256)), presented.Sha256.Replace(":", ""));
        Assert.StartsWith("-----BEGIN CERTIFICATE-----", presented.Pem);
        Assert.EndsWith("-----END CERTIFICATE-----\n", presented.Pem);
        using var reloaded = X509Certificate2.CreateFromPem(presented.Pem);
        Assert.Equal(cert.Thumbprint, reloaded.Thumbprint);
    }

    [Fact]
    public void A_private_ca_chain_is_pinnable_only_when_the_root_is_presented()
    {
        var (root, leaf) = TestCertificates.CaSigned();
        using (root)
        using (leaf)
        {
            var withRoot = CertificateReader.Read([leaf, root]);
            Assert.True(withRoot.Pinnable, withRoot.NotPinnableReason);
            Assert.Equal(2, withRoot.Pem.Split("-----BEGIN CERTIFICATE-----").Length - 1);

            var leafOnly = CertificateReader.Read([leaf]);
            Assert.False(leafOnly.Pinnable);
            Assert.False(string.IsNullOrWhiteSpace(leafOnly.NotPinnableReason));
        }
    }

    [Fact]
    public void An_expired_certificate_is_not_pinnable_and_says_why()
    {
        using var expired = TestCertificates.SelfSigned(notBefore: DateTimeOffset.UtcNow.AddDays(-30), notAfter: DateTimeOffset.UtcNow.AddDays(-1));
        var presented = CertificateReader.Read([expired]);
        Assert.False(presented.Pinnable);
        Assert.Contains("valid", presented.NotPinnableReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void An_empty_chain_is_rejected() => Assert.Throws<ArgumentException>(() => CertificateReader.Read([]));
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~CertificateReaderTests"`
Expected: build errors for the missing `LizTerm.Core.Security` namespace.

- [ ] **Step 3: Implement**

Create `src/LizTerm.Core/Security/PresentedCertificate.cs`:

```csharp
namespace LizTerm.Core.Security;

/// <summary>What a host presented in a TLS handshake (spec 5.1). <paramref name="Pem"/> is every certificate in the
/// chain, leaf first; <paramref name="Pinnable"/> says whether an engine trusting only those certificates would
/// accept the leaf, and <paramref name="NotPinnableReason"/> says why not.</summary>
public sealed record PresentedCertificate(string Sha256, string Subject, string Pem, bool Pinnable, string? NotPinnableReason);
```

Create `src/LizTerm.Core/Security/CertificateReader.cs`:

```csharp
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace LizTerm.Core.Security;

/// <summary>Turns the certificates a host presented into what the prompt shows and the profile pins.</summary>
public static class CertificateReader
{
    /// <summary>SHA-256 of the DER encoding as colon-separated upper-case hex pairs, the way openssl prints it.</summary>
    public static string Fingerprint(X509Certificate2 certificate) =>
        string.Join(":", certificate.GetCertHash(HashAlgorithmName.SHA256).Select(b => b.ToString("X2")));

    /// <param name="chain">What the host presented, leaf first. Must not be empty.</param>
    public static PresentedCertificate Read(IReadOnlyList<X509Certificate2> chain)
    {
        if (chain.Count == 0) throw new ArgumentException("The host presented no certificate.", nameof(chain));
        var leaf = chain[0];
        var pem = string.Concat(chain.Select(certificate => certificate.ExportCertificatePem() + "\n"));
        var (pinnable, reason) = CheckPinnable(chain);
        return new PresentedCertificate(Fingerprint(leaf), leaf.Subject, pem, pinnable, reason);
    }

    /// <summary>Whether OpenSSL, given only these certificates as its trust store, would accept the leaf: a
    /// self-signed leaf or a chain that includes its own root passes; a chain missing its root or an expired
    /// certificate does not, and pinning could not fix either, so the prompt must not offer it (spec 5.1).
    /// Self-signed members are the trust anchors; the others are only available for chain building.</summary>
    private static (bool Pinnable, string? Reason) CheckPinnable(IReadOnlyList<X509Certificate2> chain)
    {
        using var check = new X509Chain();
        check.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        check.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        check.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;
        foreach (var certificate in chain)
        {
            if (IsSelfSigned(certificate)) check.ChainPolicy.CustomTrustStore.Add(certificate);
            else check.ChainPolicy.ExtraStore.Add(certificate);
        }
        if (check.Build(chain[0])) return (true, null);
        var reasons = check.ChainStatus
            .Select(status => status.StatusInformation.Trim().TrimEnd('.'))
            .Where(text => text.Length > 0)
            .Distinct();
        var reason = string.Join("; ", reasons);
        return (false, reason.Length > 0 ? reason : "the chain could not be built");
    }

    private static bool IsSelfSigned(X509Certificate2 certificate) =>
        certificate.SubjectName.RawData.AsSpan().SequenceEqual(certificate.IssuerName.RawData);
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~CertificateReaderTests"`
Expected: 4 pass. If `A_private_ca_chain_is_pinnable_only_when_the_root_is_presented` fails on the with-root case, print `check.ChainStatus` in the test and fix the chain (the usual cause is a missing `X509SubjectKeyIdentifierExtension` on the root or a serial with the top bit set); do not weaken the assertion.

- [ ] **Step 5: Commit**

```bash
git add src/LizTerm.Core/Security tests/LizTerm.Core.Tests/Security
git commit -m "Read a presented certificate chain into a fingerprint, subject, PEM, and a pinnable verdict

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---
### Task 3: The SslStream certificate fetcher

**Files:**
- Create: `src/LizTerm.Core/Security/ICertificateFetcher.cs`, `src/LizTerm.Core/Security/SslStreamCertificateFetcher.cs`
- Test: `tests/LizTerm.Core.Tests/Security/SslStreamCertificateFetcherTests.cs`

**Interfaces:**
- Consumes: `CertificateReader.Read`, `TestCertificates` (Task 2).
- Produces: `public interface ICertificateFetcher { Task<PresentedCertificate> FetchAsync(string host, int port, CancellationToken token); }` and `public sealed class SslStreamCertificateFetcher : ICertificateFetcher { TimeSpan Timeout { get; init; } = 10 s }`, which throws `IOException` on a timeout ("No TLS answer from {host}:{port} within {n} s.") and lets socket errors propagate.

- [ ] **Step 1: Write the failing tests**

Create `tests/LizTerm.Core.Tests/Security/SslStreamCertificateFetcherTests.cs`:

```csharp
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using LizTerm.Core.Security;

namespace LizTerm.Core.Tests.Security;

public class SslStreamCertificateFetcherTests
{
    private static (TcpListener Listener, int Port) Listen()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return (listener, ((IPEndPoint)listener.LocalEndpoint).Port);
    }

    [Fact]
    public async Task Reads_the_certificate_a_loopback_server_presents()
    {
        var ct = TestContext.Current.CancellationToken;
        using var serverCertificate = TestCertificates.WithUsableKey(TestCertificates.SelfSigned());
        var (listener, port) = Listen();
        var server = Task.Run(async () =>
        {
            using var accepted = await listener.AcceptTcpClientAsync(ct);
            await using var tls = new SslStream(accepted.GetStream());
            await tls.AuthenticateAsServerAsync(serverCertificate, clientCertificateRequired: false, checkCertificateRevocation: false);
            // Hold the session until the client hangs up; the fetcher closes as soon as the handshake is done.
            await tls.ReadAsync(new byte[1], ct);
        }, ct);
        try
        {
            var presented = await new SslStreamCertificateFetcher().FetchAsync("localhost", port, ct);
            Assert.Equal(CertificateReader.Fingerprint(serverCertificate), presented.Sha256);
            Assert.Equal(serverCertificate.Subject, presented.Subject);
            Assert.True(presented.Pinnable, presented.NotPinnableReason);
            Assert.Contains("-----BEGIN CERTIFICATE-----", presented.Pem);
        }
        finally
        {
            listener.Stop();
            try { await server; } catch (Exception) { /* the client hung up, as it should */ }
        }
    }

    [Fact]
    public async Task A_refused_port_throws()
    {
        var (listener, port) = Listen();
        listener.Stop();
        await Assert.ThrowsAnyAsync<Exception>(() => new SslStreamCertificateFetcher().FetchAsync("localhost", port, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_host_that_never_speaks_tls_times_out_with_a_plain_message()
    {
        var (listener, port) = Listen();
        try
        {
            var fetcher = new SslStreamCertificateFetcher { Timeout = TimeSpan.FromMilliseconds(300) };
            var ex = await Assert.ThrowsAsync<IOException>(() => fetcher.FetchAsync("localhost", port, TestContext.Current.CancellationToken));
            Assert.Equal($"No TLS answer from localhost:{port} within 0 s.", ex.Message);
        }
        finally
        {
            listener.Stop();
        }
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~SslStreamCertificateFetcherTests"`
Expected: build errors for `SslStreamCertificateFetcher`.

- [ ] **Step 3: Implement**

Create `src/LizTerm.Core/Security/ICertificateFetcher.cs`:

```csharp
namespace LizTerm.Core.Security;

/// <summary>Reads what a TLS host presents without trusting it (spec 5.1). The app passes
/// <see cref="SslStreamCertificateFetcher"/>; tests pass a fake.</summary>
public interface ICertificateFetcher
{
    /// <summary>Performs one TLS handshake to read the host's certificate chain, then closes the socket. Throws on
    /// any failure (refused, timed out, not TLS); callers treat every exception the same way.</summary>
    Task<PresentedCertificate> FetchAsync(string host, int port, CancellationToken token);
}
```

Create `src/LizTerm.Core/Security/SslStreamCertificateFetcher.cs`:

```csharp
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;

namespace LizTerm.Core.Security;

/// <summary>One TLS-on-connect handshake whose validation callback captures the chain and accepts it, so the
/// handshake completes whatever the host presented; nothing is sent afterwards. Only TLS-on-connect hosts (a
/// profile with TLS on) are read this way; a STARTTLS upgrade on a plain profile is not attempted (spec 5.1).</summary>
public sealed class SslStreamCertificateFetcher : ICertificateFetcher
{
    /// <summary>Bound on the whole read: connect plus handshake.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(10);

    public async Task<PresentedCertificate> FetchAsync(string host, int port, CancellationToken token)
    {
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(token);
        bounded.CancelAfter(Timeout);
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(host, port, bounded.Token);
            var presented = new List<X509Certificate2>();
            await using var tls = new SslStream(client.GetStream(), leaveInnerStreamOpen: false, (_, certificate, chain, _) =>
            {
                if (certificate is null) return false;
                // Copies through the DER bytes: the objects SslStream hands out are disposed with the stream, and
                // the byte-array X509Certificate2 constructors are obsolete (SYSLIB0057).
                presented.Add(X509CertificateLoader.LoadCertificate(certificate.Export(X509ContentType.Cert)));
                if (chain is not null)
                {
                    foreach (var element in chain.ChainElements.Cast<X509ChainElement>().Skip(1))
                        presented.Add(X509CertificateLoader.LoadCertificate(element.Certificate.Export(X509ContentType.Cert)));
                }
                return true;
            });
            await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = host,
                CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
            }, bounded.Token);
            if (presented.Count == 0) throw new IOException("The host presented no certificate.");
            return CertificateReader.Read(presented);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            throw new IOException($"No TLS answer from {host}:{port} within {Timeout.TotalSeconds:0} s.");
        }
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~SslStreamCertificateFetcherTests"`
Expected: 3 pass. If the loopback test fails with an "ephemeral key" or "private key is not accessible" message on macOS, the PKCS#12 re-import in `TestCertificates.WithUsableKey` is what fixes it; check that it is being used, and if the runtime still refuses, add `X509KeyStorageFlags.Exportable` to the flags there. Do not skip the test.

- [ ] **Step 5: Commit**

```bash
git add src/LizTerm.Core/Security tests/LizTerm.Core.Tests/Security
git commit -m "Read a TLS host's certificate chain with one SslStream handshake behind ICertificateFetcher

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 4: The backend pins through a temp caFile and one explicit Set per attempt

**Files:**
- Modify: `src/LizTerm.Backend.B3270/B3270Session.cs` (the `ConnectAsync` method, around line 562)
- Test: `tests/LizTerm.Backend.B3270.Tests/B3270SessionConnectTests.cs`

**Interfaces:**
- Consumes: `CertificatePin`, `ConnectOptions.Pin`, `SessionProfile.PinnedCertificate` (Task 1); `B3270Action(string Name, params string[] Args)`; `FakeB3270Process` (`InputLines`, `RunResponder`, `Exit`, `WaitForInputAsync`).
- Produces: `internal static B3270Action B3270Session.TlsSettings(bool verify, string? pinFile)`; `internal static string B3270Session.WritePinFile(string pem)`; `internal string? B3270Session.LastPinFile` (test seam: the last pin file path, deleted or not).

- [ ] **Step 1: Write the failing tests**

Add to `tests/LizTerm.Backend.B3270.Tests/B3270SessionConnectTests.cs`, inside the class, after the `Failed` helper:

```csharp
    private static readonly CertificatePin Pin = new("8C:13:6A:01", "CN=localhost",
        "-----BEGIN CERTIFICATE-----\nZmFrZQ==\n-----END CERTIFICATE-----\n");
    private static readonly SessionProfile Pinned = Verifying with { PinnedCertificate = Pin };

    private static string LastSetLine(FakeB3270Process fake) => fake.InputLines.Last(l => l.Contains("\"Set\""));

    [Fact]
    public void Tls_settings_are_explicit_for_all_three_cases()
    {
        Assert.Equal(["verifyHostCert", "true", "caFile", "/tmp/x.pem", "acceptHostname", "any"], B3270Session.TlsSettings(true, "/tmp/x.pem").Args);
        Assert.Equal(["verifyHostCert", "true", "caFile", "", "acceptHostname", ""], B3270Session.TlsSettings(true, null).Args);
        Assert.Equal(["verifyHostCert", "false", "caFile", "", "acceptHostname", ""], B3270Session.TlsSettings(false, null).Args);
        Assert.Equal("Set", B3270Session.TlsSettings(true, null).Name);
    }

    [Fact]
    public async Task A_pinned_profile_verifies_against_a_temp_file_that_lives_only_for_the_connect_run()
    {
        var fake = new FakeB3270Process();
        string? contentDuringConnect = null;
        var existedDuringConnect = false;
        var ownerOnly = true;
        await using var session = new B3270Session(Pinned, () => fake);
        fake.RunResponder = line =>
        {
            if (line.Contains("\"Connect\""))
            {
                var path = session.LastPinFile!;
                existedDuringConnect = File.Exists(path);
                contentDuringConnect = File.ReadAllText(path);
                if (!OperatingSystem.IsWindows()) ownerOnly = File.GetUnixFileMode(path) == (UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
            return [Ok(line)];
        };

        await session.ConnectAsync(cancellationToken: TestContext.Current.CancellationToken);

        var set = LastSetLine(fake);
        Assert.Contains("\"verifyHostCert\",\"true\"", set);
        Assert.Contains($"\"caFile\",\"{session.LastPinFile}\"", set);
        Assert.Contains("\"acceptHostname\",\"any\"", set);
        Assert.True(existedDuringConnect, "the pin file did not exist while the Connect run was pending");
        Assert.Equal(Pin.Pem, contentDuringConnect);
        Assert.True(ownerOnly, "the pin file is not owner-only");
        Assert.False(File.Exists(session.LastPinFile), "the pin file outlived the Connect run");
        Assert.StartsWith(Path.GetTempPath(), session.LastPinFile);
        Assert.StartsWith("lizterm-pin-", Path.GetFileName(session.LastPinFile!));
        Assert.EndsWith(".pem", session.LastPinFile);
    }

    [Fact]
    public async Task Verification_off_ignores_the_pin_and_clears_the_trust_settings()
    {
        var fake = new FakeB3270Process();
        await using var session = new B3270Session(Pinned, () => fake);
        await session.ConnectAsync(new ConnectOptions(VerifyCertificate: false), TestContext.Current.CancellationToken);
        Assert.Contains("\"verifyHostCert\",\"false\",\"caFile\",\"\",\"acceptHostname\",\"\"", LastSetLine(fake));
        Assert.Null(session.LastPinFile);
    }

    [Fact]
    public async Task An_unpinned_verifying_profile_clears_the_trust_settings()
    {
        var fake = new FakeB3270Process();
        await using var session = new B3270Session(Verifying, () => fake);
        await session.ConnectAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Contains("\"verifyHostCert\",\"true\",\"caFile\",\"\",\"acceptHostname\",\"\"", LastSetLine(fake));
        Assert.Null(session.LastPinFile);
    }

    [Fact]
    public async Task A_one_shot_pin_wins_over_the_profile_pin()
    {
        var fake = new FakeB3270Process();
        var oneShot = new CertificatePin("00:11", "CN=new", "-----BEGIN CERTIFICATE-----\nbmV3\n-----END CERTIFICATE-----\n");
        string? content = null;
        await using var session = new B3270Session(Pinned, () => fake);
        fake.RunResponder = line =>
        {
            if (line.Contains("\"Connect\"")) content = File.ReadAllText(session.LastPinFile!);
            return [Ok(line)];
        };
        await session.ConnectAsync(new ConnectOptions(Pin: oneShot), TestContext.Current.CancellationToken);
        Assert.Equal(oneShot.Pem, content);
    }

    [Fact]
    public async Task The_pin_file_is_deleted_after_a_failed_connect_and_after_engine_death()
    {
        var failing = new FakeB3270Process
        {
            RunResponder = line => line.Contains("\"Connect\"")
                ? [Failed(Tag(line), "Connection failed:", "TLS: Host certificate verification failed:", "self-signed certificate (18)")]
                : [Ok(line)],
        };
        await using (var session = new B3270Session(Pinned, () => failing))
        {
            await Assert.ThrowsAsync<ConnectionFailedException>(() => session.ConnectAsync(cancellationToken: TestContext.Current.CancellationToken));
            Assert.False(File.Exists(session.LastPinFile));
        }

        var dying = new FakeB3270Process();
        dying.RunResponder = line =>
        {
            if (line.Contains("\"Connect\"")) { dying.Exit(1); return []; }
            return [Ok(line)];
        };
        await using (var session = new B3270Session(Pinned, () => dying))
        {
            await Assert.ThrowsAsync<BackendUnavailableException>(() => session.ConnectAsync(cancellationToken: TestContext.Current.CancellationToken));
            Assert.False(File.Exists(session.LastPinFile));
        }
    }

    [Fact]
    public async Task The_pin_file_is_deleted_after_a_cancelled_connect()
    {
        string? connectTag = null;
        var fake = new FakeB3270Process();
        fake.RunResponder = line =>
        {
            if (line.Contains("\"Connect\"")) { connectTag = Tag(line); return []; }
            if (line.Contains("\"Disconnect\""))
                return [Ok(line), Failed(connectTag!, "Connection failed"), """{"connection":{"state":"not-connected"}}"""];
            return [Ok(line)];
        };
        await using var session = new B3270Session(Pinned, () => fake);
        using var cts = new CancellationTokenSource();
        var connect = session.ConnectAsync(cancellationToken: cts.Token);
        await fake.WaitForInputAsync(l => l.Contains("\"Connect\""), TimeSpan.FromSeconds(2));
        Assert.True(File.Exists(session.LastPinFile));
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => connect);
        Assert.False(File.Exists(session.LastPinFile));
    }
```

Add `using LizTerm.Core.Session;` if it is not already at the top (it is). Note `Failed(...)` builds a run-result with `success:false`; `Ok(...)` a success.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/LizTerm.Backend.B3270.Tests --filter "FullyQualifiedName~B3270SessionConnectTests"`
Expected: build errors for `TlsSettings` and `LastPinFile`.

- [ ] **Step 3: Implement**

In `src/LizTerm.Backend.B3270/B3270Session.cs`, replace the `ConnectAsync` method (from `public async Task ConnectAsync(` through the closing brace before `private async Task TryDisconnectQuietlyAsync()`) with the following. The body of `ConnectCoreAsync` is the current method's body from `using var runCts` onward, unchanged; only the head changes.

```csharp
    public async Task ConnectAsync(ConnectOptions? options = null, CancellationToken cancellationToken = default)
    {
        await StartProcessAsync(cancellationToken);
        var verify = options?.VerifyCertificate ?? Profile.VerifyCertificate;
        // Spec 3.1: verification off means no pin; otherwise the one-shot pin wins over the profile's.
        var pin = verify ? options?.Pin ?? Profile.PinnedCertificate : null;
        // The engine loads caFile when it builds the TLS context for this connection, inside the Connect run, so the
        // file has to outlive that run and nothing more (spec 4.1). It holds a public certificate, so a leftover
        // after a crash is harmless.
        var pinFile = pin is null ? null : WritePinFile(pin.Pem);
        LastPinFile = pinFile;
        try
        {
            // Bounded by the caller's token: b3270 answers Set at once, but a wedged engine must not hold the attempt
            // open before the Connect has even gone out. All three values are sent every time so an attempt never
            // inherits the previous one's trust settings (spec 2).
            await RunAsync([TlsSettings(verify, pinFile)], throwOnFailure: true, cancellationToken: cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            await ConnectCoreAsync(cancellationToken);
        }
        finally
        {
            if (pinFile is not null) TryDeletePinFile(pinFile);
        }
    }

    /// <summary>The Set action carrying one attempt's trust settings (spec 4.1). With a pin the name check adds
    /// nothing, so acceptHostname is "any".</summary>
    internal static B3270Action TlsSettings(bool verify, string? pinFile) =>
        new("Set", "verifyHostCert", verify ? "true" : "false", "caFile", pinFile ?? "", "acceptHostname", pinFile is null ? "" : "any");

    /// <summary>The path of the last pin file written, deleted or not. Test seam.</summary>
    internal string? LastPinFile { get; private set; }

    internal static string WritePinFile(string pem)
    {
        var path = Path.Combine(Path.GetTempPath(), $"lizterm-pin-{Guid.NewGuid():N}.pem");
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
        // Owner-only on Unix; the Windows temp directory is already per user.
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        using var stream = new FileStream(path, options);
        using var writer = new StreamWriter(stream);
        writer.Write(pem);
        return path;
    }

    private static void TryDeletePinFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception)
        {
            // A leftover temp file holds a public certificate; there is nothing better to do about it here.
        }
    }

    private async Task ConnectCoreAsync(CancellationToken cancellationToken)
    {
        // b3270 answers a Disconnect while a Connect is pending (verified against 4.5ga6): the Disconnect run
        // succeeds at once and the Connect run then fails with "Connection failed", which is the cancel's own
        // consequence rather than an error to report. Its own token lets that wait be given up on if the engine
        // never says so, which releases the pending slot rather than abandoning it.
        using var runCts = new CancellationTokenSource();
        var run = RunRawAsync([new B3270Action("Connect", HostStringBuilder.Build(Profile))], cancellationToken: runCts.Token);
        Task? disconnect = null;
        RunResultIndication? result = null;
        try
        {
            using (cancellationToken.Register(() =>
            {
                Volatile.Write(ref disconnect, Task.Run(TryDisconnectQuietlyAsync));
                // The Disconnect is what makes b3270 fail the pending Connect run, so allow that long for it to
                // arrive and no longer: a wedged engine must not hold the attempt open past its cancellation.
                runCts.CancelAfter(DisconnectTimeout);
            }))
                result = await run;
        }
        catch (OperationCanceledException) when (runCts.IsCancellationRequested)
        {
            // The engine never failed the Connect run after the cancel. The cancellation below is the outcome.
        }
        finally
        {
            // The registration is disposed by now, so the slot is final. Awaiting here rather than only on the
            // cancelled path below is what keeps that promise when the Connect run faults instead of returning —
            // the engine dying mid-cancel — which would otherwise let the Disconnect land on the next attempt.
            // TransferAsync holds its own cancel the same way.
            if (Volatile.Read(ref disconnect) is { } started) await started;
        }

        if (cancellationToken.IsCancellationRequested)
        {
            // A cancel that raced the outcome still wins: the run may even have succeeded, so make sure a Disconnect
            // went out and was answered before reporting the cancellation.
            if (Volatile.Read(ref disconnect) is null) await TryDisconnectQuietlyAsync();
            // b3270 answers the Connect run before it reports the connection closed; wait for that report so the
            // session is actually reusable by the time the caller sees the exception (spec 4.1).
            await WaitForDisconnectedAsync();
            throw new OperationCanceledException(cancellationToken);
        }

        // Not cancelled, so the run above answered.
        var outcome = result!;
        if (outcome.Success) return;
        // Same lag as above: the failing run-result arrives before b3270's own not-connected indication.
        await WaitForDisconnectedAsync();
        var certificate = outcome.Text.Any(line => line.StartsWith(CertificateFailurePrefix, StringComparison.Ordinal));
        throw new ConnectionFailedException(outcome.Text, certificate);
    }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/LizTerm.Backend.B3270.Tests`
Expected: all pass, including the existing `Options_override_the_profile_verify_setting` (its substring assertions still hold: `verifyHostCert` is the first pair) and `Connect_sets_verify_then_connects_with_host_string` in the state tests. If that last one asserts the exact old action `["Set","verifyHostCert","true"]`, update its expectation to the new six-argument form.

- [ ] **Step 5: Commit**

```bash
git add src/LizTerm.Backend.B3270/B3270Session.cs tests/LizTerm.Backend.B3270.Tests
git commit -m "Verify a pinned certificate through a temp caFile that lives only for the Connect run, with every trust setting explicit per attempt

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 5: Backend cleanup: shared disconnect waiters, one Wait helper, environment isolation

**Files:**
- Modify: `src/LizTerm.Backend.B3270/B3270Session.cs` (the `_disconnected` field, its reader-thread use, `WaitForDisconnectedAsync`)
- Create: `tests/LizTerm.Backend.B3270.Tests/Wait.cs`, `tests/LizTerm.Backend.B3270.Tests/EnvironmentCollection.cs`
- Modify: `tests/LizTerm.Backend.B3270.Tests/B3270SessionConnectTests.cs`, `B3270SessionWireLogTests.cs`, `B3270SessionStateTests.cs`, `B3270SessionTransferTests.cs` (drop their private `WaitUntilAsync` copies), and the test class that sets `LIZTERM_B3270_PATH`

**Interfaces:**
- Produces: `internal static class Wait { static Task UntilAsync(Func<bool> condition, string what, TimeSpan? timeout = null) }` in `LizTerm.Backend.B3270.Tests`; `EnvironmentCollection.Name`.

- [ ] **Step 1: Write the failing test**

Create `tests/LizTerm.Backend.B3270.Tests/Wait.cs`:

```csharp
namespace LizTerm.Backend.B3270.Tests;

/// <summary>Polls a condition until it holds or the timeout passes. The one copy for this project; the App tests
/// have their own.</summary>
internal static class Wait
{
    public static async Task UntilAsync(Func<bool> condition, string what, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(2));
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("Timed out waiting for " + what);
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }
    }
}
```

Add to `B3270SessionConnectTests`:

```csharp
    /// <summary>Regression: the wait slot was a single field, so a second caller's install dropped the first
    /// caller's completion source and the first waited out the whole DisconnectTimeout.</summary>
    [Fact]
    public async Task Two_overlapping_disconnect_waits_both_end_on_the_one_not_connected_report()
    {
        var fake = new FakeB3270Process();
        await using var session = new B3270Session(Verifying, () => fake);
        await session.ConnectAsync(cancellationToken: TestContext.Current.CancellationToken);
        fake.Emit("""{"connection":{"state":"connected-3270","host":"h","cause":"ui"}}""");
        await Wait.UntilAsync(() => session.ConnectionState == ConnectionState.Connected3270, "connected");

        var first = session.DisconnectAsync();
        var second = session.DisconnectAsync();
        await fake.WaitForInputAsync(l => l.Contains("\"Disconnect\""), TimeSpan.FromSeconds(2));
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.False(first.IsCompleted);
        Assert.False(second.IsCompleted);

        fake.Emit("""{"connection":{"state":"not-connected"}}""");
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
    }
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/LizTerm.Backend.B3270.Tests --filter "FullyQualifiedName~Two_overlapping_disconnect_waits"`
Expected: FAIL with a `TimeoutException` from `WaitAsync` (one caller waits out the 5 s `DisconnectTimeout`).

- [ ] **Step 3: Implement**

In `B3270Session.cs`:

1. Change the field `private volatile TaskCompletionSource? _disconnected;` to `private TaskCompletionSource? _disconnected;` (Interlocked on a volatile field is warning CS0420).
2. In the connection-state dispatch (the `finally` that reads `if (state == ConnectionState.Disconnected) _disconnected?.TrySetResult();`), change it to `if (state == ConnectionState.Disconnected) Volatile.Read(ref _disconnected)?.TrySetResult();`.
3. Replace `WaitForDisconnectedAsync` with:

```csharp
    /// <summary>Waits until b3270 reports the connection closed, or until <see cref="DisconnectTimeout"/> passes.
    /// The source is installed before the state is checked so a report that lands in between is not missed.
    /// Overlapping callers share one source: the first installs it, a concurrent caller awaits the same one, and
    /// the finally clears the slot only if it still holds that instance (spec 8).</summary>
    private async Task WaitForDisconnectedAsync()
    {
        var fresh = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var disconnected = Interlocked.CompareExchange(ref _disconnected, fresh, null) ?? fresh;
        try
        {
            if (ConnectionState == ConnectionState.Disconnected) return;
            try
            {
                await disconnected.Task.WaitAsync(DisconnectTimeout);
            }
            catch (TimeoutException)
            {
                // b3270 accepted the action but never reported the state; the ConnectionChanged event still
                // fires if it does later. Hanging the caller would be worse.
            }
        }
        finally
        {
            Interlocked.CompareExchange(ref _disconnected, null, disconnected);
        }
    }
```

Then the test-project cleanup:

4. Delete the private `WaitUntilAsync` methods from `B3270SessionConnectTests.cs`, `B3270SessionWireLogTests.cs`, `B3270SessionStateTests.cs`, and `B3270SessionTransferTests.cs`, and rewrite their call sites: `WaitUntilAsync(` becomes `Wait.UntilAsync(`. In `B3270SessionTransferTests.cs` the class has a `private static readonly TimeSpan Wait` field that the helper name would shadow: rename that field to `WaitTime` everywhere in the file first, and pass it as the third argument where its copy used it (`Wait.UntilAsync(condition, what, WaitTime)`).

```bash
sed -i '' 's/\bWait\b/WaitTime/g' tests/LizTerm.Backend.B3270.Tests/B3270SessionTransferTests.cs
sed -i '' 's/WaitUntilAsync(/Wait.UntilAsync(/g' tests/LizTerm.Backend.B3270.Tests/B3270SessionConnectTests.cs tests/LizTerm.Backend.B3270.Tests/B3270SessionWireLogTests.cs tests/LizTerm.Backend.B3270.Tests/B3270SessionStateTests.cs tests/LizTerm.Backend.B3270.Tests/B3270SessionTransferTests.cs
```

After the sed, open `B3270SessionTransferTests.cs`: the first sed also renamed the helper's own `Wait.UntilAsync` to `WaitTime.UntilAsync`; fix those back to `Wait.UntilAsync(..., WaitTime)`. Then delete the four private helper method bodies by hand.

5. Environment isolation. Find the class:

```bash
grep -rl "SetEnvironmentVariable" tests/LizTerm.Backend.B3270.Tests --include='*.cs' | grep -v /bin/ | grep -v /obj/
```

Create `tests/LizTerm.Backend.B3270.Tests/EnvironmentCollection.cs`:

```csharp
namespace LizTerm.Backend.B3270.Tests;

/// <summary>Tests that set LIZTERM_B3270_PATH must not run beside tests that read it (spec 8).</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class EnvironmentCollection
{
    public const string Name = "Environment";
}
```

Add `[Collection(EnvironmentCollection.Name)]` above each class the grep found, and check that every `SetEnvironmentVariable` there restores the original in a `finally` (add one where it is missing, following the shape in `tests/LizTerm.App.Tests/SessionFactoryTests.cs`).

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/LizTerm.Backend.B3270.Tests` and `dotnet build LizTerm.slnx 2>&1 | grep -c " warning "`
Expected: all pass; `0`.

- [ ] **Step 5: Commit**

```bash
git add src/LizTerm.Backend.B3270/B3270Session.cs tests/LizTerm.Backend.B3270.Tests
git commit -m "Share one disconnect wait between overlapping callers, hoist the backend tests' wait helper, and serialize the tests that set the engine path

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---
### Task 6: The view model fetches, asks, pins, and re-pins

**Files:**
- Create: `src/LizTerm.App/Dialogs/CertificatePromptRequest.cs`, `tests/LizTerm.App.Tests/Fakes/FakeCertificateFetcher.cs`
- Modify: `src/LizTerm.App/Dialogs/ICertificatePrompt.cs`, `src/LizTerm.App/ViewModels/SessionViewModel.cs`, `tests/LizTerm.App.Tests/Fakes/FakeCertificatePrompt.cs`, `tests/LizTerm.App.Tests/Fakes/FakeEmulatorSession.cs`, `src/LizTerm.App/Dialogs/AvaloniaCertificatePrompt.cs` (signature only; Task 7 finishes it)
- Test: `tests/LizTerm.App.Tests/ViewModels/SessionViewModelConnectTests.cs`

**Interfaces:**
- Consumes: `CertificatePin`, `ConnectOptions.Pin` (Task 1); `PresentedCertificate`, `ICertificateFetcher` (Tasks 2 and 3).
- Produces: `public sealed record CertificatePromptRequest(string Host, IReadOnlyList<string> Reason, PresentedCertificate? Presented, string? FetchError, CertificatePin? Previous, bool CanPin, string? CannotPinReason)`; `ICertificatePrompt.AskAsync(CertificatePromptRequest request)`; `SessionViewModel` constructor parameter `ICertificateFetcher? certificateFetcher = null` (last); `FakeEmulatorSession` records `connect:pin:<sha256>`; `FakeCertificatePrompt.LastRequest`; `FakeCertificateFetcher` (`Result`, `Exception`, `Calls` as `fetch:<host>:<port>`).

- [ ] **Step 1: Write the fakes and the failing tests**

Create `tests/LizTerm.App.Tests/Fakes/FakeCertificateFetcher.cs`:

```csharp
using LizTerm.Core.Security;

namespace LizTerm.App.Tests.Fakes;

public sealed class FakeCertificateFetcher : ICertificateFetcher
{
    public PresentedCertificate Result { get; set; } = new("AA:BB", "CN=fake", "-----BEGIN CERTIFICATE-----\nZmFrZQ==\n-----END CERTIFICATE-----\n", true, null);
    /// <summary>When set, FetchAsync throws it.</summary>
    public Exception? Exception { get; set; }
    /// <summary>"fetch:<host>:<port>" per call.</summary>
    public List<string> Calls { get; } = [];

    public Task<PresentedCertificate> FetchAsync(string host, int port, CancellationToken token)
    {
        Calls.Add($"fetch:{host}:{port}");
        if (Exception is not null) throw Exception;
        return Task.FromResult(Result);
    }
}
```

Replace `tests/LizTerm.App.Tests/Fakes/FakeCertificatePrompt.cs` with:

```csharp
using LizTerm.App.Dialogs;

namespace LizTerm.App.Tests.Fakes;

public sealed class FakeCertificatePrompt : ICertificatePrompt
{
    public CertificateDecision Decision { get; set; } = CertificateDecision.Declined;
    /// <summary>Runs before the decision is returned; tests use it to change the fake session between attempts.</summary>
    public Action? OnAsk { get; set; }
    /// <summary>"ask:<host>:<canPin>" per call.</summary>
    public List<string> Calls { get; } = [];
    public CertificatePromptRequest? LastRequest { get; private set; }
    public IReadOnlyList<string>? LastReason => LastRequest?.Reason;
    /// <summary>When set, AskAsync throws it, simulating ShowDialog over an owner that has gone away.</summary>
    public Exception? AskException { get; set; }

    public Task<CertificateDecision> AskAsync(CertificatePromptRequest request)
    {
        Calls.Add($"ask:{request.Host}:{request.CanPin}");
        LastRequest = request;
        OnAsk?.Invoke();
        if (AskException is not null) throw AskException;
        return Task.FromResult(Decision);
    }
}
```

In `tests/LizTerm.App.Tests/Fakes/FakeEmulatorSession.cs`, replace the `Calls.Add(...)` line in `ConnectAsync` with:

```csharp
        Calls.Add(options?.Pin is { } pin ? "connect:pin:" + pin.Sha256
            : options?.VerifyCertificate == false ? "connect:noverify" : "connect");
```

In `tests/LizTerm.App.Tests/ViewModels/SessionViewModelConnectTests.cs`, replace everything from the line `private static readonly ConnectionFailedException CertFailure = new(` to the class's closing brace with:

```csharp
    private static readonly ConnectionFailedException CertFailure = new(
        ["Connection failed:", "TLS: Host certificate verification failed:", "self-signed certificate (18)"], certificateVerificationFailed: true);

    private static (SessionViewModel Vm, FakeEmulatorSession Session, FakeCertificatePrompt Prompt, FakeCertificateFetcher Fetcher, List<SessionProfile> Saved)
        CreateWithPrompt(bool saveable, bool tls = true, CertificatePin? pinned = null)
    {
        var session = new FakeEmulatorSession { ConnectException = CertFailure };
        session.Profile = session.Profile with { UseTls = tls, Port = 4270, PinnedCertificate = pinned };
        var prompt = new FakeCertificatePrompt();
        var fetcher = new FakeCertificateFetcher();
        var saved = new List<SessionProfile>();
        var vm = new SessionViewModel(session, a => a(), new FakeTextClipboard(), prompt, saveable ? saved.Add : null,
            certificateFetcher: fetcher);
        return (vm, session, prompt, fetcher, saved);
    }

    [Fact]
    public async Task Declined_prompt_shows_the_failure_and_the_request_carries_the_presented_certificate()
    {
        var (vm, session, prompt, fetcher, _) = CreateWithPrompt(saveable: true);
        await vm.ConnectCommand.ExecuteAsync(null);
        Assert.Equal(["fetch:fake.host:4270"], fetcher.Calls);
        Assert.Equal(["ask:fake.host:True"], prompt.Calls);
        var request = prompt.LastRequest!;
        Assert.Equal(["TLS: Host certificate verification failed:", "self-signed certificate (18)"], request.Reason);
        Assert.Same(fetcher.Result, request.Presented);
        Assert.Null(request.FetchError);
        Assert.Null(request.Previous);
        Assert.Null(request.CannotPinReason);
        Assert.Equal(["connect"], session.Calls);
        Assert.Equal(CertFailure.Message, vm.ErrorMessage);
    }

    [Fact]
    public async Task Accepted_prompt_reconnects_without_verification_once()
    {
        var (vm, session, prompt, _, saved) = CreateWithPrompt(saveable: true);
        prompt.Decision = new CertificateDecision(ConnectAnyway: true, Remember: false);
        prompt.OnAsk = () => session.ConnectException = null;
        await vm.ConnectCommand.ExecuteAsync(null);
        Assert.Equal(["connect", "connect:noverify"], session.Calls);
        Assert.Null(vm.ErrorMessage);
        Assert.Empty(saved);

        // Not remembered: the next attempt verifies again and asks again.
        session.ConnectException = CertFailure;
        prompt.Decision = CertificateDecision.Declined;
        await vm.ConnectCommand.ExecuteAsync(null);
        Assert.Equal(["connect", "connect:noverify", "connect"], session.Calls);
        Assert.Equal(2, prompt.Calls.Count);
    }

    [Fact]
    public async Task Trusting_the_certificate_pins_it_saves_the_profile_and_stops_asking()
    {
        var (vm, session, prompt, fetcher, saved) = CreateWithPrompt(saveable: true);
        prompt.Decision = new CertificateDecision(ConnectAnyway: true, Remember: true);
        prompt.OnAsk = () => session.ConnectException = null;
        await vm.ConnectCommand.ExecuteAsync(null);

        var profile = Assert.Single(saved);
        Assert.True(profile.VerifyCertificate);
        Assert.Equal(new CertificatePin(fetcher.Result.Sha256, fetcher.Result.Subject, fetcher.Result.Pem), profile.PinnedCertificate);
        Assert.Equal(session.Profile.Name, profile.Name);
        Assert.Null(vm.ErrorMessage);

        await vm.ConnectCommand.ExecuteAsync(null);
        Assert.Equal(["connect", "connect:pin:AA:BB", "connect:pin:AA:BB"], session.Calls);
        Assert.Single(prompt.Calls);
    }

    [Fact]
    public async Task A_changed_certificate_prompts_with_the_previous_pin_and_accepting_re_pins()
    {
        var old = new CertificatePin("00:11", "CN=old", "old-pem");
        var (vm, session, prompt, _, saved) = CreateWithPrompt(saveable: true, pinned: old);
        prompt.Decision = new CertificateDecision(ConnectAnyway: true, Remember: true);
        prompt.OnAsk = () => session.ConnectException = null;
        await vm.ConnectCommand.ExecuteAsync(null);
        Assert.Equal(["ask:fake.host:True"], prompt.Calls);
        Assert.Same(old, prompt.LastRequest!.Previous);
        Assert.Equal(["connect", "connect:pin:AA:BB"], session.Calls);
        Assert.Equal("AA:BB", Assert.Single(saved).PinnedCertificate!.Sha256);
    }

    [Fact]
    public async Task A_failed_fetch_prompts_without_a_fingerprint_and_cannot_pin()
    {
        var (vm, session, prompt, fetcher, saved) = CreateWithPrompt(saveable: true);
        fetcher.Exception = new IOException("No TLS answer from fake.host:4270 within 10 s.");
        prompt.Decision = new CertificateDecision(ConnectAnyway: true, Remember: true);
        prompt.OnAsk = () => session.ConnectException = null;
        await vm.ConnectCommand.ExecuteAsync(null);
        var request = prompt.LastRequest!;
        Assert.Null(request.Presented);
        Assert.Equal(fetcher.Exception.Message, request.FetchError);
        Assert.False(request.CanPin);
        Assert.Null(request.CannotPinReason);
        // Remember cannot mean pin without a certificate, so the retry is the one-time allow.
        Assert.Equal(["connect", "connect:noverify"], session.Calls);
        Assert.Empty(saved);
    }

    [Fact]
    public async Task A_certificate_that_cannot_be_pinned_gets_the_one_time_allow_only()
    {
        var (vm, session, prompt, fetcher, saved) = CreateWithPrompt(saveable: true);
        fetcher.Result = fetcher.Result with { Pinnable = false, NotPinnableReason = "the chain is missing its root" };
        prompt.Decision = new CertificateDecision(ConnectAnyway: true, Remember: true);
        prompt.OnAsk = () => session.ConnectException = null;
        await vm.ConnectCommand.ExecuteAsync(null);
        var request = prompt.LastRequest!;
        Assert.False(request.CanPin);
        Assert.Equal("This certificate cannot be pinned: the chain is missing its root. Connect Anyway applies to this attempt only.", request.CannotPinReason);
        Assert.Equal(["connect", "connect:noverify"], session.Calls);
        Assert.Empty(saved);
    }

    [Fact]
    public async Task A_pin_the_engine_rejects_is_not_offered_again()
    {
        var current = new CertificatePin("AA:BB", "CN=fake", "pem");
        var (vm, _, prompt, _, saved) = CreateWithPrompt(saveable: true, pinned: current);
        await vm.ConnectCommand.ExecuteAsync(null);
        var request = prompt.LastRequest!;
        Assert.False(request.CanPin);
        Assert.Equal("The engine rejected the pinned certificate; connecting anyway applies to this attempt only.", request.CannotPinReason);
        Assert.Empty(saved);
    }

    [Fact]
    public async Task A_plain_profile_never_fetches_and_cannot_pin()
    {
        var (vm, _, prompt, fetcher, _) = CreateWithPrompt(saveable: true, tls: false);
        await vm.ConnectCommand.ExecuteAsync(null);
        Assert.Empty(fetcher.Calls);
        Assert.Equal(["ask:fake.host:False"], prompt.Calls);
        Assert.Null(prompt.LastRequest!.Presented);
        Assert.Null(prompt.LastRequest.CannotPinReason);
    }

    [Fact]
    public async Task Ad_hoc_profile_sees_the_certificate_but_cannot_pin()
    {
        var (vm, _, prompt, fetcher, _) = CreateWithPrompt(saveable: false);
        await vm.ConnectCommand.ExecuteAsync(null);
        Assert.Single(fetcher.Calls);
        Assert.Equal(["ask:fake.host:False"], prompt.Calls);
        Assert.NotNull(prompt.LastRequest!.Presented);
        Assert.Null(prompt.LastRequest.CannotPinReason);
    }

    [Fact]
    public async Task Without_a_prompt_the_failure_is_shown()
    {
        var session = new FakeEmulatorSession { ConnectException = CertFailure };
        var vm = new SessionViewModel(session, a => a(), new FakeTextClipboard());
        await vm.ConnectCommand.ExecuteAsync(null);
        Assert.Equal(["connect"], session.Calls);
        Assert.Equal(CertFailure.Message, vm.ErrorMessage);
    }

    [Fact]
    public async Task A_failure_after_a_one_time_allow_is_shown_and_not_asked_about()
    {
        var (vm, session, prompt, _, _) = CreateWithPrompt(saveable: true);
        prompt.Decision = new CertificateDecision(ConnectAnyway: true, Remember: false);
        prompt.OnAsk = () => session.ConnectException = new ConnectionFailedException(["Connection failed:", "Connection refused"]);
        await vm.ConnectCommand.ExecuteAsync(null);
        Assert.Single(prompt.Calls);
        Assert.Equal("Connection failed: Connection refused", vm.ErrorMessage);
    }

    /// <summary>The prompt is a modal window and the fetch is a socket; either can outlive the session window.</summary>
    [Fact]
    public async Task A_prompt_answered_after_the_window_closed_does_nothing()
    {
        var (vm, session, prompt, _, saved) = CreateWithPrompt(saveable: true);
        prompt.Decision = new CertificateDecision(ConnectAnyway: true, Remember: true);
        prompt.OnAsk = () => _ = vm.DisposeAsync().AsTask();
        await vm.ConnectCommand.ExecuteAsync(null);
        Assert.Equal(["connect", "dispose"], session.Calls);
        Assert.Empty(saved);
    }

    /// <summary>Regression: OfferConnectAnywayAsync was awaited from inside a catch clause, so a failing profile
    /// save escaped every sibling handler and faulted the command (an unhandled UI-thread exception from the
    /// File > Connect menu).</summary>
    [Fact]
    public async Task A_profile_save_that_fails_is_reported_and_does_not_fault_the_command()
    {
        var session = new FakeEmulatorSession { ConnectException = CertFailure };
        session.Profile = session.Profile with { UseTls = true, Port = 4270 };
        var prompt = new FakeCertificatePrompt { Decision = new CertificateDecision(ConnectAnyway: true, Remember: true) };
        prompt.OnAsk = () => session.ConnectException = null;
        var vm = new SessionViewModel(session, a => a(), new FakeTextClipboard(), prompt,
            _ => throw new IOException("disk full"), certificateFetcher: new FakeCertificateFetcher());
        await vm.ConnectCommand.ExecuteAsync(null);
        Assert.Equal(["connect", "connect:pin:AA:BB"], session.Calls);
        Assert.Equal("Could not save the profile: disk full", vm.ErrorMessage);
    }

    [Fact]
    public async Task A_prompt_that_fails_is_reported_and_does_not_fault_the_command()
    {
        var (vm, session, prompt, _, _) = CreateWithPrompt(saveable: true);
        prompt.AskException = new InvalidOperationException("owner closed");
        await vm.ConnectCommand.ExecuteAsync(null);
        Assert.Equal(["connect"], session.Calls);
        Assert.Equal("Could not ask about the certificate: owner closed", vm.ErrorMessage);
    }
}
```

Add `using LizTerm.App.Dialogs;` and `using LizTerm.App.Tests.Fakes;` at the top of the test file if they are not already there.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~SessionViewModelConnectTests"`
Expected: build errors (`CertificatePromptRequest`, `certificateFetcher`).

- [ ] **Step 3: Implement**

Create `src/LizTerm.App/Dialogs/CertificatePromptRequest.cs`:

```csharp
using LizTerm.Core.Security;
using LizTerm.Core.Session;

namespace LizTerm.App.Dialogs;

/// <summary>Everything the certificate prompt shows (spec 5.2).</summary>
/// <param name="Reason">The engine's lines, without the leading "Connection failed:".</param>
/// <param name="Presented">What the host presented, or null when the fetch failed or was not attempted.</param>
/// <param name="FetchError">The fetch exception's message when <paramref name="Presented"/> is null for that reason.</param>
/// <param name="Previous">The pin in force when the failure happened; non-null means "certificate changed".</param>
/// <param name="CanPin">Whether "Trust this certificate for this profile" is offered.</param>
/// <param name="CannotPinReason">Shown, instead of the checkbox, when a saved TLS profile cannot pin this certificate.</param>
public sealed record CertificatePromptRequest(
    string Host,
    IReadOnlyList<string> Reason,
    PresentedCertificate? Presented,
    string? FetchError,
    CertificatePin? Previous,
    bool CanPin,
    string? CannotPinReason);
```

Replace `src/LizTerm.App/Dialogs/ICertificatePrompt.cs` with:

```csharp
namespace LizTerm.App.Dialogs;

/// <summary>Asks whether to connect to a host whose certificate did not verify. Injected like the clipboard so
/// tests answer without a window. A null prompt on the view model declines.</summary>
public interface ICertificatePrompt
{
    Task<CertificateDecision> AskAsync(CertificatePromptRequest request);
}

/// <summary><paramref name="Remember"/> means "pin this certificate for the profile" and is only honoured when the
/// request offered it.</summary>
public sealed record CertificateDecision(bool ConnectAnyway, bool Remember)
{
    public static readonly CertificateDecision Declined = new(false, false);
}
```

In `src/LizTerm.App/Dialogs/AvaloniaCertificatePrompt.cs`, change the method to `public async Task<CertificateDecision> AskAsync(CertificatePromptRequest request)` and, for now, keep the window call compiling by passing the pieces the old window constructor takes: `new CertificateWindow(request.Host, request.Reason, request.CanPin)`. Task 7 replaces this.

In `src/LizTerm.App/ViewModels/SessionViewModel.cs`:

1. Add `using LizTerm.Core.Security;` to the usings.
2. Replace the field `private bool? _verifyOverride;` with:

```csharp
    private readonly ICertificateFetcher? _certificateFetcher;
    /// <summary>The pin chosen in this window. The session's profile is fixed at construction, so a pin made after
    /// the window opened travels as a one-shot option on every later connect from here (spec 5.3).</summary>
    private CertificatePin? _pinOverride;
```

3. Replace the constructor signature and the doc comment lines for `certificatePrompt`/`saveProfile` with:

```csharp
    /// <param name="dispatch">Marshals a callback onto the UI thread. Tests pass <c>a => a()</c>.</param>
    /// <param name="clipboard">Text clipboard; the app passes <see cref="AvaloniaTextClipboard"/>, tests a fake.</param>
    /// <param name="certificatePrompt">Asked on a certificate verification failure; null declines.</param>
    /// <param name="saveProfile">Persists the profile when the user pins its certificate; null for ad hoc profiles.</param>
    /// <param name="folderOpener">Opens the wire log directory for Help &gt; Show Wire Logs; null for tests that don't cover it.</param>
    /// <param name="certificateFetcher">Reads what a TLS host presented so the prompt can show and pin it; null shows the prompt without a fingerprint.</param>
    public SessionViewModel(IEmulatorSession session, Action<Action> dispatch, ITextClipboard clipboard,
        ICertificatePrompt? certificatePrompt = null, Action<SessionProfile>? saveProfile = null,
        IFolderOpener? folderOpener = null, ICertificateFetcher? certificateFetcher = null)
```

and add `_certificateFetcher = certificateFetcher;` after `_folderOpener = folderOpener;`.

4. Replace the Connect command with:

```csharp
    [RelayCommand]
    private Task ConnectAsync() => ConnectWithAsync(new ConnectOptions(Pin: _pinOverride));
```

5. Replace `OfferConnectAnywayAsync` (doc comment included) with:

```csharp
    /// <summary>Spec 5.3. Reads what the host presented (TLS profiles only), asks once, and then either connects
    /// without verification for this attempt only or pins the certificate: the profile is saved with the pin and
    /// verification on, and every later connect from this window passes the same pin. A pin the engine then rejects
    /// is not offered again (the request says so), so this never loops. The prompt (a modal window), the fetch (a
    /// socket), and the save (a file write) can all fail, and this runs after the connect's catch clauses rather
    /// than inside one, so those failures reach the error banner instead of faulting the command.</summary>
    private async Task OfferConnectAnywayAsync(ConnectionFailedException failure)
    {
        var reason = failure.Lines.Where(line => line != "Connection failed:").ToList();
        var previous = _pinOverride ?? Profile.PinnedCertificate;

        PresentedCertificate? presented = null;
        string? fetchError = null;
        if (Profile.UseTls && _certificateFetcher is not null)
        {
            // The fetcher only speaks TLS-on-connect, which is what a TLS profile is; a plain profile the host upgraded
            // through STARTTLS gets the one-time allow without a fingerprint (spec 5.1). The connect's own token source
            // is gone by now, so the fetch gets a fresh one with the same bound.
            using var fetchCts = new CancellationTokenSource(ConnectTimeout);
            try
            {
                presented = await _certificateFetcher.FetchAsync(Profile.Host, Profile.Port, fetchCts.Token);
            }
            catch (Exception ex)
            {
                fetchError = ex.Message;
            }
            if (_disposed) return;
        }

        var savedTlsProfile = _saveProfile is not null && Profile.UseTls;
        var canPin = savedTlsProfile && presented is { Pinnable: true } && presented.Sha256 != previous?.Sha256;
        string? cannotPinReason = null;
        if (savedTlsProfile && !canPin && presented is not null)
        {
            cannotPinReason = presented.Pinnable
                ? "The engine rejected the pinned certificate; connecting anyway applies to this attempt only."
                : $"This certificate cannot be pinned: {presented.NotPinnableReason}. Connect Anyway applies to this attempt only.";
        }
        var request = new CertificatePromptRequest(Profile.Host, reason, presented, fetchError, previous, canPin, cannotPinReason);

        CertificateDecision decision;
        try
        {
            decision = _certificatePrompt is null ? CertificateDecision.Declined : await _certificatePrompt.AskAsync(request);
        }
        catch (Exception ex)
        {
            ErrorMessage = "Could not ask about the certificate: " + ex.Message;
            return;
        }

        // The prompt is unbounded, so the window may have closed while it was open.
        if (_disposed) return;
        if (!decision.ConnectAnyway)
        {
            ErrorMessage = failure.Message;
            return;
        }

        string? saveError = null;
        ConnectOptions retry;
        if (decision.Remember && canPin)
        {
            var pin = new CertificatePin(presented!.Sha256, presented.Subject, presented.Pem);
            _pinOverride = pin;
            try
            {
                _saveProfile!(Profile with { PinnedCertificate = pin, VerifyCertificate = true });
            }
            catch (Exception ex)
            {
                // The choice still holds for this window; only writing it back failed, so the connect goes ahead.
                saveError = "Could not save the profile: " + ex.Message;
            }
            retry = new ConnectOptions(Pin: pin);
        }
        else
        {
            retry = new ConnectOptions(VerifyCertificate: false);
        }

        await ConnectWithAsync(retry);

        // ConnectWithAsync clears ErrorMessage on entry, so a save failure is reported after the retry.
        if (saveError is not null && !_disposed)
            ErrorMessage = ErrorMessage is null ? saveError : $"{ErrorMessage} {saveError}";
    }
```

The `catch (ConnectionFailedException ex) when (ex.CertificateVerificationFailed && options.VerifyCertificate != false)` clause in `ConnectWithAsync` stays as it is: a pinned retry that the engine rejects comes back through it, and the request then carries `CannotPinReason`, which is what stops the loop.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/LizTerm.App.Tests`
Expected: all pass. `SessionWindowTests` and the other view-model tests construct the view model with three arguments and are unaffected.

- [ ] **Step 5: Commit**

```bash
git add src/LizTerm.App tests/LizTerm.App.Tests
git commit -m "Read the presented certificate, ask with it, and pin it on Remember instead of switching verification off

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 7: The certificate window renders the request, and the app wires the fetcher

**Files:**
- Modify: `src/LizTerm.App/Views/CertificateWindow.axaml`, `src/LizTerm.App/Views/CertificateWindow.axaml.cs`, `src/LizTerm.App/Dialogs/AvaloniaCertificatePrompt.cs`, `src/LizTerm.App/App.axaml.cs`
- Test: `tests/LizTerm.App.Tests/Views/CertificateWindowTests.cs`

**Interfaces:**
- Consumes: `CertificatePromptRequest` (Task 6), `SslStreamCertificateFetcher` (Task 3).
- Produces: `CertificateWindow(CertificatePromptRequest request)`; named controls `HostText`, `TrustedText`, `PresentedText`, `SubjectText`, `FetchErrorText`, `ReasonText`, `RememberBox`, `CannotPinText`, `ConnectAnywayButton`, `CancelButton`.

- [ ] **Step 1: Write the failing tests**

Replace `tests/LizTerm.App.Tests/Views/CertificateWindowTests.cs` with:

```csharp
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using LizTerm.App.Dialogs;
using LizTerm.App.Views;
using LizTerm.Core.Security;
using LizTerm.Core.Session;

namespace LizTerm.App.Tests.Views;

public class CertificateWindowTests
{
    private static readonly string[] Reason = ["TLS: Host certificate verification failed:", "self-signed certificate (18)"];
    private static readonly PresentedCertificate Presented = new("8C:13:6A:01", "O = tn3270proxy quick-start, CN = localhost", "pem", true, null);

    private static CertificatePromptRequest Request(PresentedCertificate? presented = null, string? fetchError = null,
        CertificatePin? previous = null, bool canPin = false, string? cannotPinReason = null) =>
        new("mvs.local", Reason, presented, fetchError, previous, canPin, cannotPinReason);

    private static string Text(Window window, string name) => window.FindControl<TextBlock>(name)!.Text ?? "";
    private static bool Visible(Window window, string name) => window.FindControl<Control>(name)!.IsVisible;

    [AvaloniaFact]
    public void A_first_time_failure_shows_the_presented_certificate_and_offers_to_trust_it()
    {
        var window = new CertificateWindow(Request(Presented, canPin: true));
        window.Show();
        Assert.Equal("Certificate not verified", window.Title);
        Assert.Equal("mvs.local presented a certificate that could not be verified:", Text(window, "HostText"));
        Assert.False(Visible(window, "TrustedText"));
        Assert.True(Visible(window, "PresentedText"));
        Assert.Equal("Presented: SHA-256 8C:13:6A:01", Text(window, "PresentedText"));
        Assert.Equal("Subject: O = tn3270proxy quick-start, CN = localhost", Text(window, "SubjectText"));
        Assert.False(Visible(window, "FetchErrorText"));
        Assert.Equal(string.Join("\n", Reason), Text(window, "ReasonText"));
        var remember = window.FindControl<CheckBox>("RememberBox")!;
        Assert.True(remember.IsVisible);
        Assert.Equal("Trust this certificate for this profile", remember.Content);
        Assert.False(Visible(window, "CannotPinText"));
    }

    [AvaloniaFact]
    public void A_changed_certificate_shows_both_fingerprints()
    {
        var previous = new CertificatePin("00:11:22:33", "CN=old", "old");
        var window = new CertificateWindow(Request(Presented, previous: previous, canPin: true));
        window.Show();
        Assert.Equal("Certificate changed", window.Title);
        Assert.Equal("mvs.local presented a certificate that is not the one trusted for this profile.", Text(window, "HostText"));
        Assert.True(Visible(window, "TrustedText"));
        Assert.Equal("Trusted: SHA-256 00:11:22:33", Text(window, "TrustedText"));
        Assert.Equal("Presented: SHA-256 8C:13:6A:01", Text(window, "PresentedText"));
        Assert.True(window.FindControl<CheckBox>("RememberBox")!.IsVisible);
    }

    [AvaloniaFact]
    public void A_failed_fetch_explains_and_hides_the_checkbox()
    {
        var window = new CertificateWindow(Request(fetchError: "No TLS answer from mvs.local:992 within 10 s."));
        window.Show();
        Assert.False(Visible(window, "PresentedText"));
        Assert.False(Visible(window, "SubjectText"));
        Assert.True(Visible(window, "FetchErrorText"));
        Assert.Equal("The certificate could not be read: No TLS answer from mvs.local:992 within 10 s.", Text(window, "FetchErrorText"));
        Assert.False(window.FindControl<CheckBox>("RememberBox")!.IsVisible);
        Assert.False(Visible(window, "CannotPinText"));
    }

    [AvaloniaFact]
    public void A_certificate_that_cannot_be_pinned_says_so_instead_of_the_checkbox()
    {
        const string reason = "This certificate cannot be pinned: the chain is missing its root. Connect Anyway applies to this attempt only.";
        var notPinnable = Presented with { Pinnable = false, NotPinnableReason = "the chain is missing its root" };
        var window = new CertificateWindow(Request(notPinnable, cannotPinReason: reason));
        window.Show();
        Assert.False(window.FindControl<CheckBox>("RememberBox")!.IsVisible);
        Assert.True(Visible(window, "CannotPinText"));
        Assert.Equal(reason, Text(window, "CannotPinText"));
    }

    [AvaloniaFact]
    public void Escape_declines_and_the_checkbox_is_carried_on_connect_anyway()
    {
        var declined = new CertificateWindow(Request(Presented, canPin: true));
        declined.Show();
        declined.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Assert.Equal(CertificateDecision.Declined, declined.Decision);

        var trusted = new CertificateWindow(Request(Presented, canPin: true));
        trusted.Show();
        trusted.FindControl<CheckBox>("RememberBox")!.IsChecked = true;
        trusted.ConnectAnyway();
        Assert.Equal(new CertificateDecision(true, true), trusted.Decision);

        var once = new CertificateWindow(Request(Presented, canPin: true));
        once.Show();
        once.ConnectAnyway();
        Assert.Equal(new CertificateDecision(true, false), once.Decision);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~CertificateWindowTests"`
Expected: build error (no constructor taking a request).

- [ ] **Step 3: Implement**

Replace `src/LizTerm.App/Views/CertificateWindow.axaml` with:

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:controls="using:LizTerm.App.Controls"
        x:Class="LizTerm.App.Views.CertificateWindow"
        Title="Certificate not verified" Width="560" SizeToContent="Height" CanResize="False"
        WindowStartupLocation="CenterOwner">
  <StackPanel Margin="16" Spacing="12">
    <TextBlock x:Name="HostText" TextWrapping="Wrap" />
    <StackPanel Spacing="4">
      <TextBlock x:Name="TrustedText" FontFamily="{x:Static controls:TerminalScreen.TerminalFont}" TextWrapping="Wrap" />
      <TextBlock x:Name="PresentedText" FontFamily="{x:Static controls:TerminalScreen.TerminalFont}" TextWrapping="Wrap" />
      <TextBlock x:Name="SubjectText" TextWrapping="Wrap" Foreground="#A0A0A0" FontSize="12" />
      <TextBlock x:Name="FetchErrorText" TextWrapping="Wrap" Foreground="#FFC080" />
    </StackPanel>
    <Border Background="#101010" Padding="10">
      <TextBlock x:Name="ReasonText" FontFamily="{x:Static controls:TerminalScreen.TerminalFont}" Foreground="#F0F0F0" TextWrapping="Wrap" />
    </Border>
    <CheckBox x:Name="RememberBox" Content="Trust this certificate for this profile" />
    <TextBlock x:Name="CannotPinText" TextWrapping="Wrap" Foreground="#FFC080" />
    <StackPanel Orientation="Horizontal" HorizontalAlignment="Right" Spacing="8">
      <Button x:Name="ConnectAnywayButton" Content="Connect Anyway" Click="OnConnectAnywayClick" />
      <Button x:Name="CancelButton" Content="Cancel" IsDefault="True" IsCancel="True" Click="OnCancelClick" />
    </StackPanel>
  </StackPanel>
</Window>
```

Replace `src/LizTerm.App/Views/CertificateWindow.axaml.cs` with:

```csharp
using Avalonia.Controls;
using Avalonia.Interactivity;
using LizTerm.App.Dialogs;
using LizTerm.Core.Security;

namespace LizTerm.App.Views;

/// <summary>Spec 5.4. Cancel is the default and Escape maps to it; closing with the title bar also declines. The
/// result travels both as <see cref="Decision"/> and as the ShowDialog result.</summary>
public partial class CertificateWindow : Window
{
    /// <summary>Design-time only: a first-time failure with a pinnable certificate.</summary>
    public CertificateWindow() : this(new CertificatePromptRequest("mvs.example",
        ["TLS: Host certificate verification failed:", "self-signed certificate (18)"],
        new PresentedCertificate("8C:13:6A:01", "CN=mvs.example", "", true, null), null, null, true, null)) { }

    public CertificateWindow(CertificatePromptRequest request)
    {
        InitializeComponent();
        var changed = request.Previous is not null;
        Title = changed ? "Certificate changed" : "Certificate not verified";
        HostText.Text = changed
            ? $"{request.Host} presented a certificate that is not the one trusted for this profile."
            : $"{request.Host} presented a certificate that could not be verified:";
        TrustedText.IsVisible = changed;
        TrustedText.Text = changed ? $"Trusted: SHA-256 {request.Previous!.Sha256}" : "";
        PresentedText.IsVisible = request.Presented is not null;
        SubjectText.IsVisible = request.Presented is not null;
        if (request.Presented is { } presented)
        {
            PresentedText.Text = $"Presented: SHA-256 {presented.Sha256}";
            SubjectText.Text = $"Subject: {presented.Subject}";
        }
        FetchErrorText.IsVisible = request.Presented is null && request.FetchError is not null;
        FetchErrorText.Text = request.FetchError is null ? "" : $"The certificate could not be read: {request.FetchError}";
        ReasonText.Text = string.Join("\n", request.Reason);
        RememberBox.IsVisible = request.CanPin;
        CannotPinText.IsVisible = request.CannotPinReason is not null;
        CannotPinText.Text = request.CannotPinReason ?? "";
    }

    public CertificateDecision? Decision { get; private set; }

    internal void ConnectAnyway() => Finish(new CertificateDecision(true, RememberBox.IsVisible && RememberBox.IsChecked == true));

    private void OnConnectAnywayClick(object? sender, RoutedEventArgs e) => ConnectAnyway();

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Finish(CertificateDecision.Declined);

    private void Finish(CertificateDecision decision)
    {
        Decision = decision;
        Close(decision);
    }
}
```

Replace `src/LizTerm.App/Dialogs/AvaloniaCertificatePrompt.cs` with:

```csharp
using Avalonia.Controls;
using LizTerm.App.Views;

namespace LizTerm.App.Dialogs;

/// <summary>Opens <see cref="CertificateWindow"/> modally over the session window.</summary>
public sealed class AvaloniaCertificatePrompt(Window owner) : ICertificatePrompt
{
    public async Task<CertificateDecision> AskAsync(CertificatePromptRequest request)
    {
        var result = await new CertificateWindow(request).ShowDialog<CertificateDecision?>(owner);
        return result ?? CertificateDecision.Declined;
    }
}
```

In `src/LizTerm.App/App.axaml.cs`, add `using LizTerm.Core.Security;` and, in `OpenSession`, add the fetcher as the last constructor argument:

```csharp
        var viewModel = new SessionViewModel(
            SessionFactory.Create(profile),
            action => Dispatcher.UIThread.Post(action),
            new AvaloniaTextClipboard(window),
            new AvaloniaCertificatePrompt(window),
            fromStore ? store.Save : null,
            new AvaloniaFolderOpener(window),
            new SslStreamCertificateFetcher());
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/LizTerm.App.Tests`
Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add src/LizTerm.App tests/LizTerm.App.Tests
git commit -m "Show the presented and trusted fingerprints in the certificate prompt and read the certificate with SslStream in the app

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 8: The profile editor shows the pin, forgets it, and labels Backspace honestly

**Files:**
- Modify: `src/LizTerm.App/ViewModels/ProfileEditorViewModel.cs`, `src/LizTerm.App/Views/ProfileEditorWindow.axaml`
- Test: `tests/LizTerm.App.Tests/ViewModels/ProfileViewModelsTests.cs`, create `tests/LizTerm.App.Tests/Views/ProfileEditorWindowTests.cs`

**Interfaces:**
- Produces: `ProfileEditorViewModel.PinnedCertificate`, `HasPinnedCertificate`, `PinnedCertificateText`, `ForgetPinCommand`; named controls `PinPanel`, `PinText`, `ForgetButton`, `BackspaceBox`.

- [ ] **Step 1: Write the failing tests**

In `tests/LizTerm.App.Tests/ViewModels/ProfileViewModelsTests.cs`, change `Editor_round_trips_an_existing_profile` so `original` also carries `PinnedCertificate = new CertificatePin("8C:13", "CN=mvs", "pem")`, and add:

```csharp
    [Fact]
    public void Editor_shows_the_pin_and_forget_drops_it()
    {
        var pin = new CertificatePin("8C:13:6A:01", "CN=gw", "pem");
        var vm = new ProfileEditorViewModel(new SessionProfile { Name = "gw", Host = "gw", UseTls = true, PinnedCertificate = pin });
        Assert.True(vm.HasPinnedCertificate);
        Assert.Equal("Pinned certificate: SHA-256 8C:13:6A:01", vm.PinnedCertificateText);
        Assert.Same(pin, vm.TryBuild()!.PinnedCertificate);

        var changes = new List<string?>();
        vm.PropertyChanged += (_, e) => changes.Add(e.PropertyName);
        vm.ForgetPinCommand.Execute(null);
        Assert.False(vm.HasPinnedCertificate);
        Assert.Null(vm.PinnedCertificateText);
        Assert.Null(vm.TryBuild()!.PinnedCertificate);
        Assert.Contains(nameof(vm.HasPinnedCertificate), changes);
        Assert.Contains(nameof(vm.PinnedCertificateText), changes);
    }

    [Fact]
    public void A_new_profile_has_no_pin()
    {
        var vm = new ProfileEditorViewModel(null);
        Assert.False(vm.HasPinnedCertificate);
        Assert.Null(vm.PinnedCertificateText);
        Assert.Null(vm.TryBuild()?.PinnedCertificate);
    }
```

Create `tests/LizTerm.App.Tests/Views/ProfileEditorWindowTests.cs`:

```csharp
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using LizTerm.App.Views;
using LizTerm.Core.Session;

namespace LizTerm.App.Tests.Views;

public class ProfileEditorWindowTests
{
    [AvaloniaFact]
    public void The_pinned_line_shows_only_for_a_pinned_profile_and_forget_hides_it()
    {
        var pinned = new ProfileEditorWindow(new SessionProfile
        {
            Name = "gw", Host = "gw", UseTls = true, PinnedCertificate = new CertificatePin("8C:13", "CN=gw", "pem"),
        });
        pinned.Show();
        var panel = pinned.FindControl<StackPanel>("PinPanel")!;
        Assert.True(panel.IsVisible);
        Assert.Equal("Pinned certificate: SHA-256 8C:13", pinned.FindControl<TextBlock>("PinText")!.Text);
        pinned.FindControl<Button>("ForgetButton")!.Command!.Execute(null);
        Assert.False(panel.IsVisible);

        var plain = new ProfileEditorWindow(new SessionProfile { Name = "p", Host = "h" });
        plain.Show();
        Assert.False(plain.FindControl<StackPanel>("PinPanel")!.IsVisible);
        Assert.Equal("Backspace erases the previous character (off: Backspace only moves the cursor left)",
            plain.FindControl<CheckBox>("BackspaceBox")!.Content);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~ProfileViewModelsTests|FullyQualifiedName~ProfileEditorWindowTests"`
Expected: build errors (`HasPinnedCertificate`, `ForgetPinCommand`).

- [ ] **Step 3: Implement**

Replace `src/LizTerm.App/ViewModels/ProfileEditorViewModel.cs` with:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LizTerm.Core.Session;

namespace LizTerm.App.ViewModels;

public partial class ProfileEditorViewModel : ObservableObject
{
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _host = "";
    [ObservableProperty] private string _portText = "23";
    [ObservableProperty] private bool _useTls;
    [ObservableProperty] private bool _verifyCertificate = true;
    [ObservableProperty] private int _model = 2;
    [ObservableProperty] private bool _extended = true;
    [ObservableProperty] private string _codePage = "cp037";
    [ObservableProperty] private string _luName = "";
    [ObservableProperty] private bool _destructiveBackspace = true;
    [ObservableProperty] private string? _validationMessage;

    /// <summary>The pin the profile carries, shown read-only. Forget clears it and Save then writes the profile
    /// without it, which is the only way back from a pin to the engine's default trust (spec 5.5).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPinnedCertificate), nameof(PinnedCertificateText))]
    private CertificatePin? _pinnedCertificate;

    public bool HasPinnedCertificate => PinnedCertificate is not null;
    public string? PinnedCertificateText => PinnedCertificate is { } pin ? $"Pinned certificate: SHA-256 {pin.Sha256}" : null;

    public int[] Models { get; } = [2, 3, 4, 5];
    public bool IsNew { get; }

    public ProfileEditorViewModel(SessionProfile? existing)
    {
        IsNew = existing is null;
        if (existing is null) return;
        _name = existing.Name;
        _host = existing.Host;
        _portText = existing.Port.ToString();
        _useTls = existing.UseTls;
        _verifyCertificate = existing.VerifyCertificate;
        _model = existing.Model;
        _extended = existing.Extended;
        _codePage = existing.CodePage;
        _luName = existing.LuName ?? "";
        _destructiveBackspace = existing.DestructiveBackspace;
        _pinnedCertificate = existing.PinnedCertificate;
    }

    partial void OnUseTlsChanged(bool value)
    {
        if (value && PortText == "23") PortText = "992";
        else if (!value && PortText == "992") PortText = "23";
    }

    [RelayCommand]
    private void ForgetPin() => PinnedCertificate = null;

    public SessionProfile? TryBuild()
    {
        if (string.IsNullOrWhiteSpace(Name)) { ValidationMessage = "Give the profile a name."; return null; }
        if (string.IsNullOrWhiteSpace(Host)) { ValidationMessage = "Enter the host name or address."; return null; }
        if (!int.TryParse(PortText.Trim(), out var port) || port < 1 || port > 65535) { ValidationMessage = "Port must be a number from 1 to 65535."; return null; }
        if (string.IsNullOrWhiteSpace(CodePage)) { ValidationMessage = "Enter a code page, for example cp037."; return null; }
        ValidationMessage = null;
        return new SessionProfile
        {
            Name = Name.Trim(),
            Host = Host.Trim(),
            Port = port,
            UseTls = UseTls,
            VerifyCertificate = VerifyCertificate,
            PinnedCertificate = PinnedCertificate,
            Model = Model,
            Extended = Extended,
            CodePage = CodePage.Trim(),
            LuName = string.IsNullOrWhiteSpace(LuName) ? null : LuName.Trim(),
            DestructiveBackspace = DestructiveBackspace,
        };
    }
}
```

In `src/LizTerm.App/Views/ProfileEditorWindow.axaml`, replace the Security stack panel and the Keyboard checkbox:

```xml
      <StackPanel Grid.Row="3" Grid.Column="1" Spacing="4">
        <CheckBox Content="Use TLS" IsChecked="{Binding UseTls}" />
        <CheckBox Content="Verify host certificate" IsChecked="{Binding VerifyCertificate}" IsEnabled="{Binding UseTls}" />
        <StackPanel x:Name="PinPanel" Orientation="Horizontal" Spacing="8" IsVisible="{Binding HasPinnedCertificate}">
          <TextBlock x:Name="PinText" Text="{Binding PinnedCertificateText}" VerticalAlignment="Center" FontSize="12" TextWrapping="Wrap" MaxWidth="260" />
          <Button x:Name="ForgetButton" Content="Forget" Command="{Binding ForgetPinCommand}" />
        </StackPanel>
      </StackPanel>
```

```xml
      <CheckBox x:Name="BackspaceBox" Grid.Row="7" Grid.Column="1" Content="Backspace erases the previous character (off: Backspace only moves the cursor left)" IsChecked="{Binding DestructiveBackspace}" />
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~ProfileViewModelsTests|FullyQualifiedName~ProfileEditorWindowTests"`
Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add src/LizTerm.App tests/LizTerm.App.Tests
git commit -m "Show a profile's pinned certificate in the editor with a Forget button, and say what the Backspace option does when off

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---
### Task 9: The keymap as a table with the Vista defaults

**Files:**
- Create: `src/LizTerm.App/Keyboard/KeyChord.cs`, `src/LizTerm.App/Keyboard/Keymap.cs`
- Modify: `src/LizTerm.App/Keyboard/DefaultKeymap.cs`
- Test: create `tests/LizTerm.App.Tests/Keyboard/KeymapTests.cs`; delete `tests/LizTerm.App.Tests/Keyboard/DefaultKeymapTests.cs`

**Interfaces:**
- Produces: `public readonly record struct KeyChord(Key Key, KeyModifiers Modifiers = KeyModifiers.None, bool Tap = false)` with `KeyChord.TapOf(Key)`; `public sealed class Keymap` (`Keys`, `Text`, `TryMap(KeyChord, out TerminalKey)`, `TryText(KeyChord, out string)`, `With(keys, text)`); `DefaultKeymap.Create(bool destructiveBackspace)` returning a cached `Keymap`; and, until Task 10 removes it, the old `DefaultKeymap.TryMap(Key, KeyModifiers, bool, out TerminalKey)` as a shim over the table so `TerminalScreen` keeps compiling.

- [ ] **Step 1: Write the failing tests**

Delete `tests/LizTerm.App.Tests/Keyboard/DefaultKeymapTests.cs` and create `tests/LizTerm.App.Tests/Keyboard/KeymapTests.cs`:

```csharp
using Avalonia.Input;
using LizTerm.App.Keyboard;
using LizTerm.Core.Session;

namespace LizTerm.App.Tests.Keyboard;

/// <summary>Every row of the cross-check table in spec 6.2, plus the seam for remapping.</summary>
public class KeymapTests
{
    private static readonly Keymap Map = DefaultKeymap.Create(destructiveBackspace: true);

    public static TheoryData<Key, KeyModifiers, TerminalKey> Rows => new()
    {
        { Key.Enter, KeyModifiers.None, TerminalKey.Enter },
        { Key.Enter, KeyModifiers.Control, TerminalKey.Enter },
        { Key.Enter, KeyModifiers.Shift, TerminalKey.Newline },
        { Key.Escape, KeyModifiers.None, TerminalKey.Attn },
        { Key.Escape, KeyModifiers.Shift, TerminalKey.SysReq },
        { Key.Escape, KeyModifiers.Control, TerminalKey.Clear },
        { Key.Pause, KeyModifiers.None, TerminalKey.Clear },
        { Key.R, KeyModifiers.Control, TerminalKey.Reset },
        { Key.F1, KeyModifiers.None, TerminalKey.PF1 },
        { Key.F12, KeyModifiers.None, TerminalKey.PF12 },
        { Key.F1, KeyModifiers.Shift, TerminalKey.PF13 },
        { Key.F12, KeyModifiers.Shift, TerminalKey.PF24 },
        { Key.F1, KeyModifiers.Control, TerminalKey.PF13 },
        { Key.F12, KeyModifiers.Control, TerminalKey.PF24 },
        { Key.PageUp, KeyModifiers.None, TerminalKey.PF7 },
        { Key.PageDown, KeyModifiers.None, TerminalKey.PF8 },
        { Key.Insert, KeyModifiers.Control, TerminalKey.PA1 },
        { Key.Home, KeyModifiers.Control, TerminalKey.PA2 },
        { Key.PageUp, KeyModifiers.Control, TerminalKey.PA3 },
        { Key.D1, KeyModifiers.Alt, TerminalKey.PA1 },
        { Key.D2, KeyModifiers.Alt, TerminalKey.PA2 },
        { Key.D3, KeyModifiers.Alt, TerminalKey.PA3 },
        { Key.Tab, KeyModifiers.None, TerminalKey.Tab },
        { Key.Tab, KeyModifiers.Shift, TerminalKey.BackTab },
        { Key.Insert, KeyModifiers.None, TerminalKey.Insert },
        { Key.Home, KeyModifiers.None, TerminalKey.Home },
        { Key.End, KeyModifiers.None, TerminalKey.EraseEof },
        { Key.Delete, KeyModifiers.None, TerminalKey.Delete },
        { Key.Back, KeyModifiers.None, TerminalKey.Erase },
        { Key.Up, KeyModifiers.None, TerminalKey.Up },
        { Key.Down, KeyModifiers.None, TerminalKey.Down },
        { Key.Left, KeyModifiers.None, TerminalKey.Left },
        { Key.Right, KeyModifiers.None, TerminalKey.Right },
    };

    [Theory]
    [MemberData(nameof(Rows))]
    public void Vista_defaults(Key key, KeyModifiers modifiers, TerminalKey expected)
    {
        Assert.True(Map.TryMap(new KeyChord(key, modifiers), out var actual));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Modifier_taps_send_enter_and_reset()
    {
        Assert.True(Map.TryMap(KeyChord.TapOf(Key.RightCtrl), out var enter));
        Assert.Equal(TerminalKey.Enter, enter);
        Assert.True(Map.TryMap(KeyChord.TapOf(Key.LeftCtrl), out var reset));
        Assert.Equal(TerminalKey.Reset, reset);
        // The press of the modifier itself is not a chord.
        Assert.False(Map.TryMap(new KeyChord(Key.RightCtrl, KeyModifiers.Control), out _));
    }

    [Fact]
    public void Not_and_cent_signs_are_typed()
    {
        Assert.True(Map.TryText(new KeyChord(Key.OemOpenBrackets, KeyModifiers.Control), out var notSign));
        Assert.Equal("¬", notSign);
        Assert.True(Map.TryText(new KeyChord(Key.D6, KeyModifiers.Control), out var cent));
        Assert.Equal("¢", cent);
        Assert.False(Map.TryText(new KeyChord(Key.A), out _));
    }

    [Theory]
    [InlineData(Key.A, KeyModifiers.None)]
    [InlineData(Key.C, KeyModifiers.Control)]
    [InlineData(Key.V, KeyModifiers.Meta)]
    [InlineData(Key.Enter, KeyModifiers.Meta)]
    [InlineData(Key.LeftShift, KeyModifiers.Shift)]
    public void Text_and_platform_shortcut_keys_stay_unmapped(Key key, KeyModifiers modifiers)
    {
        Assert.False(Map.TryMap(new KeyChord(key, modifiers), out _));
        Assert.False(Map.TryText(new KeyChord(key, modifiers), out _));
    }

    [Fact]
    public void Backspace_follows_the_profile_and_the_two_tables_are_cached()
    {
        Assert.True(DefaultKeymap.Create(destructiveBackspace: false).TryMap(new KeyChord(Key.Back), out var cursorLeft));
        Assert.Equal(TerminalKey.Backspace, cursorLeft);
        Assert.Same(DefaultKeymap.Create(true), DefaultKeymap.Create(true));
        Assert.NotSame(DefaultKeymap.Create(true), DefaultKeymap.Create(false));
    }

    [Fact]
    public void With_overrides_entries_and_leaves_the_default_untouched()
    {
        var remapped = Map.With(
            [KeyValuePair.Create(new KeyChord(Key.Escape), TerminalKey.Reset), KeyValuePair.Create(new KeyChord(Key.F13), TerminalKey.Clear)],
            [KeyValuePair.Create(new KeyChord(Key.D6, KeyModifiers.Control), "6")]);

        Assert.True(remapped.TryMap(new KeyChord(Key.Escape), out var escape));
        Assert.Equal(TerminalKey.Reset, escape);
        Assert.True(remapped.TryMap(new KeyChord(Key.F13), out var f13));
        Assert.Equal(TerminalKey.Clear, f13);
        Assert.True(remapped.TryText(new KeyChord(Key.D6, KeyModifiers.Control), out var six));
        Assert.Equal("6", six);
        Assert.Equal(Map.Keys.Count + 1, remapped.Keys.Count);

        Assert.True(Map.TryMap(new KeyChord(Key.Escape), out var original));
        Assert.Equal(TerminalKey.Attn, original);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~KeymapTests"`
Expected: build errors (`Keymap`, `KeyChord`, `DefaultKeymap.Create`).

- [ ] **Step 3: Implement**

Create `src/LizTerm.App/Keyboard/KeyChord.cs`:

```csharp
using Avalonia.Input;

namespace LizTerm.App.Keyboard;

/// <summary>One keyboard gesture: a key with its modifiers, or a modifier key pressed and released alone
/// (<paramref name="Tap"/>). The dictionary key of a <see cref="Keymap"/>.</summary>
public readonly record struct KeyChord(Key Key, KeyModifiers Modifiers = KeyModifiers.None, bool Tap = false)
{
    public static KeyChord TapOf(Key key) => new(key, KeyModifiers.None, Tap: true);
}
```

Create `src/LizTerm.App/Keyboard/Keymap.cs`:

```csharp
using System.Diagnostics.CodeAnalysis;
using LizTerm.Core.Session;

namespace LizTerm.App.Keyboard;

/// <summary>A chord-to-key table plus a chord-to-text table (spec 6.1). Immutable; <see cref="With"/> is the seam
/// for user remapping later: a future profile field would be parsed into entries and applied over the default.</summary>
public sealed class Keymap
{
    private readonly Dictionary<KeyChord, TerminalKey> _keys;
    private readonly Dictionary<KeyChord, string> _text;

    public Keymap(IEnumerable<KeyValuePair<KeyChord, TerminalKey>> keys, IEnumerable<KeyValuePair<KeyChord, string>> text)
    {
        _keys = new Dictionary<KeyChord, TerminalKey>(keys);
        _text = new Dictionary<KeyChord, string>(text);
    }

    public IReadOnlyDictionary<KeyChord, TerminalKey> Keys => _keys;
    public IReadOnlyDictionary<KeyChord, string> Text => _text;

    public bool TryMap(KeyChord chord, out TerminalKey key) => _keys.TryGetValue(chord, out key);

    public bool TryText(KeyChord chord, [NotNullWhen(true)] out string? text) => _text.TryGetValue(chord, out text);

    /// <summary>A copy with the given entries added or replaced. Later entries win.</summary>
    public Keymap With(IEnumerable<KeyValuePair<KeyChord, TerminalKey>> keys, IEnumerable<KeyValuePair<KeyChord, string>> text)
    {
        var mergedKeys = new Dictionary<KeyChord, TerminalKey>(_keys);
        foreach (var (chord, key) in keys) mergedKeys[chord] = key;
        var mergedText = new Dictionary<KeyChord, string>(_text);
        foreach (var (chord, value) in text) mergedText[chord] = value;
        return new Keymap(mergedKeys, mergedText);
    }
}
```

Replace `src/LizTerm.App/Keyboard/DefaultKeymap.cs` with:

```csharp
using Avalonia.Input;
using LizTerm.Core.Session;

namespace LizTerm.App.Keyboard;

/// <summary>The built-in defaults: Vista TN3270's table, cross-checked against wc3270 (spec 6.2). Clear has a
/// second home on Ctrl+Escape because Mac keyboards have no Pause key, PA1 to PA3 a second home on Alt+1 to Alt+3
/// because Mac laptops have no Insert key, and Reset a second home on Ctrl+R (wc3270's) in case a platform never
/// reports the Left Ctrl tap. Copy, paste, and select-all are platform hotkeys checked before this table and are
/// deliberately absent from it. Not user-editable yet; see <see cref="Keymap.With"/>.</summary>
public static class DefaultKeymap
{
    private static readonly Keymap Erasing = Build(destructiveBackspace: true);
    private static readonly Keymap CursorLeft = Build(destructiveBackspace: false);

    /// <param name="destructiveBackspace">The profile's choice: Backspace as <see cref="TerminalKey.Erase"/> (true,
    /// the default) or as the cursor-left <see cref="TerminalKey.Backspace"/>.</param>
    public static Keymap Create(bool destructiveBackspace) => destructiveBackspace ? Erasing : CursorLeft;

    /// <summary>Shim for the pre-table callers; removed in the task that teaches the screen control taps.</summary>
    public static bool TryMap(Key key, KeyModifiers modifiers, bool destructiveBackspace, out TerminalKey terminalKey) =>
        Create(destructiveBackspace).TryMap(new KeyChord(key, modifiers), out terminalKey);

    private static Keymap Build(bool destructiveBackspace)
    {
        var keys = new List<KeyValuePair<KeyChord, TerminalKey>>();
        void Add(Key key, TerminalKey terminal, KeyModifiers modifiers = KeyModifiers.None) =>
            keys.Add(KeyValuePair.Create(new KeyChord(key, modifiers), terminal));
        void Tap(Key key, TerminalKey terminal) => keys.Add(KeyValuePair.Create(KeyChord.TapOf(key), terminal));

        Add(Key.Enter, TerminalKey.Enter);
        Add(Key.Enter, TerminalKey.Enter, KeyModifiers.Control);
        Tap(Key.RightCtrl, TerminalKey.Enter);
        Add(Key.Enter, TerminalKey.Newline, KeyModifiers.Shift);
        Add(Key.Escape, TerminalKey.Attn);
        Add(Key.Escape, TerminalKey.SysReq, KeyModifiers.Shift);
        Tap(Key.LeftCtrl, TerminalKey.Reset);
        Add(Key.R, TerminalKey.Reset, KeyModifiers.Control);
        Add(Key.Pause, TerminalKey.Clear);
        Add(Key.Escape, TerminalKey.Clear, KeyModifiers.Control);
        for (var i = 0; i < 12; i++)
        {
            Add(Key.F1 + i, TerminalKey.PF1 + i);
            Add(Key.F1 + i, TerminalKey.PF13 + i, KeyModifiers.Shift);
            Add(Key.F1 + i, TerminalKey.PF13 + i, KeyModifiers.Control);
        }
        Add(Key.PageUp, TerminalKey.PF7);
        Add(Key.PageDown, TerminalKey.PF8);
        Add(Key.Insert, TerminalKey.PA1, KeyModifiers.Control);
        Add(Key.Home, TerminalKey.PA2, KeyModifiers.Control);
        Add(Key.PageUp, TerminalKey.PA3, KeyModifiers.Control);
        Add(Key.D1, TerminalKey.PA1, KeyModifiers.Alt);
        Add(Key.D2, TerminalKey.PA2, KeyModifiers.Alt);
        Add(Key.D3, TerminalKey.PA3, KeyModifiers.Alt);
        Add(Key.Tab, TerminalKey.Tab);
        Add(Key.Tab, TerminalKey.BackTab, KeyModifiers.Shift);
        Add(Key.Insert, TerminalKey.Insert);
        Add(Key.Home, TerminalKey.Home);
        Add(Key.End, TerminalKey.EraseEof);
        Add(Key.Delete, TerminalKey.Delete);
        Add(Key.Back, destructiveBackspace ? TerminalKey.Erase : TerminalKey.Backspace);
        Add(Key.Up, TerminalKey.Up);
        Add(Key.Down, TerminalKey.Down);
        Add(Key.Left, TerminalKey.Left);
        Add(Key.Right, TerminalKey.Right);

        var text = new List<KeyValuePair<KeyChord, string>>
        {
            KeyValuePair.Create(new KeyChord(Key.OemOpenBrackets, KeyModifiers.Control), "¬"),   // Vista: Ctrl+[ is the NOT sign
            KeyValuePair.Create(new KeyChord(Key.D6, KeyModifiers.Control), "¢"),                // Vista: Ctrl+6 is the cent sign
        };
        return new Keymap(keys, text);
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/LizTerm.App.Tests`
Expected: all pass. `TerminalScreenInputTests.Function_keys_and_shift_tab_raise_KeyRequested` still passes through the shim; the Backspace test still passes because the control's default is unchanged until Task 10.

- [ ] **Step 5: Commit**

```bash
git add src/LizTerm.App/Keyboard tests/LizTerm.App.Tests/Keyboard
git commit -m "Build the keymap as a chord table with Vista TN3270's defaults and a seam for remapping

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 10: Modifier taps and the screen control's new key path

**Files:**
- Create: `src/LizTerm.App/Keyboard/ModifierTapDetector.cs`
- Modify: `src/LizTerm.App/Controls/TerminalScreen.cs` (the `DestructiveBackspace` property, `OnKeyDown`, new `OnKeyUp` and `OnLostFocus`), `src/LizTerm.App/Keyboard/DefaultKeymap.cs` (remove the shim)
- Test: create `tests/LizTerm.App.Tests/Keyboard/ModifierTapDetectorTests.cs`; modify `tests/LizTerm.App.Tests/Controls/TerminalScreenInputTests.cs`

**Interfaces:**
- Consumes: `Keymap`, `KeyChord.TapOf`, `DefaultKeymap.Create` (Task 9).
- Produces: `public sealed class ModifierTapDetector { void KeyDown(Key); Key? KeyUp(Key); void Reset(); }`; `TerminalScreen.DestructiveBackspace` default `true`.

- [ ] **Step 1: Write the failing tests**

Create `tests/LizTerm.App.Tests/Keyboard/ModifierTapDetectorTests.cs`:

```csharp
using Avalonia.Input;
using LizTerm.App.Keyboard;

namespace LizTerm.App.Tests.Keyboard;

public class ModifierTapDetectorTests
{
    [Fact]
    public void A_ctrl_key_pressed_and_released_alone_is_a_tap()
    {
        var taps = new ModifierTapDetector();
        taps.KeyDown(Key.RightCtrl);
        Assert.Equal(Key.RightCtrl, taps.KeyUp(Key.RightCtrl));
        taps.KeyDown(Key.LeftCtrl);
        Assert.Equal(Key.LeftCtrl, taps.KeyUp(Key.LeftCtrl));
    }

    [Fact]
    public void Any_key_between_the_press_and_the_release_cancels_the_tap()
    {
        var taps = new ModifierTapDetector();
        taps.KeyDown(Key.LeftCtrl);
        taps.KeyDown(Key.C);
        Assert.Null(taps.KeyUp(Key.C));
        Assert.Null(taps.KeyUp(Key.LeftCtrl));
    }

    [Fact]
    public void Auto_repeat_of_the_same_ctrl_key_keeps_the_tap()
    {
        var taps = new ModifierTapDetector();
        taps.KeyDown(Key.LeftCtrl);
        taps.KeyDown(Key.LeftCtrl);
        Assert.Equal(Key.LeftCtrl, taps.KeyUp(Key.LeftCtrl));
    }

    [Fact]
    public void Other_keys_are_never_taps_and_reset_clears_a_pending_one()
    {
        var taps = new ModifierTapDetector();
        taps.KeyDown(Key.LeftShift);
        Assert.Null(taps.KeyUp(Key.LeftShift));
        taps.KeyDown(Key.RightCtrl);
        taps.Reset();
        Assert.Null(taps.KeyUp(Key.RightCtrl));
    }
}
```

In `tests/LizTerm.App.Tests/Controls/TerminalScreenInputTests.cs`, replace `Backspace_raises_Erase_only_when_DestructiveBackspace_is_set` with the three tests below:

```csharp
    [AvaloniaFact]
    public void Backspace_erases_by_default_and_moves_left_when_the_profile_says_so()
    {
        var (window, screen) = Show();
        var keys = new List<TerminalKey>();
        screen.KeyRequested += (_, k) => keys.Add(k);

        window.KeyPressQwerty(PhysicalKey.Backspace, RawInputModifiers.None);
        screen.DestructiveBackspace = false;
        window.KeyPressQwerty(PhysicalKey.Backspace, RawInputModifiers.None);

        Assert.Equal([TerminalKey.Erase, TerminalKey.Backspace], keys);
    }

    [AvaloniaFact]
    public void Vista_keys_reach_the_host()
    {
        var (window, screen) = Show();
        var keys = new List<TerminalKey>();
        var text = new List<string>();
        screen.KeyRequested += (_, k) => keys.Add(k);
        screen.TextEntered += (_, t) => text.Add(t);

        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.Shift);
        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.Control);
        window.KeyPressQwerty(PhysicalKey.PageUp, RawInputModifiers.None);
        window.KeyPressQwerty(PhysicalKey.Insert, RawInputModifiers.Control);
        window.KeyPressQwerty(PhysicalKey.Digit1, RawInputModifiers.Alt);
        window.KeyPressQwerty(PhysicalKey.BracketLeft, RawInputModifiers.Control);

        Assert.Equal([TerminalKey.Attn, TerminalKey.SysReq, TerminalKey.Clear, TerminalKey.PF7, TerminalKey.PA1, TerminalKey.PA1], keys);
        Assert.Equal(["¬"], text);
    }

    [AvaloniaFact]
    public void A_right_ctrl_tap_sends_enter_and_a_ctrl_chord_does_not_reset()
    {
        var (window, screen) = Show();
        var keys = new List<TerminalKey>();
        screen.KeyRequested += (_, k) => keys.Add(k);

        window.KeyPressQwerty(PhysicalKey.ControlRight, RawInputModifiers.Control);
        window.KeyReleaseQwerty(PhysicalKey.ControlRight, RawInputModifiers.None);

        window.KeyPressQwerty(PhysicalKey.ControlLeft, RawInputModifiers.Control);
        window.KeyPressQwerty(PhysicalKey.F1, RawInputModifiers.Control);
        window.KeyReleaseQwerty(PhysicalKey.F1, RawInputModifiers.Control);
        window.KeyReleaseQwerty(PhysicalKey.ControlLeft, RawInputModifiers.None);

        window.KeyPressQwerty(PhysicalKey.ControlLeft, RawInputModifiers.Control);
        window.KeyReleaseQwerty(PhysicalKey.ControlLeft, RawInputModifiers.None);

        Assert.Equal([TerminalKey.Enter, TerminalKey.PF13, TerminalKey.Reset], keys);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~ModifierTapDetectorTests|FullyQualifiedName~TerminalScreenInputTests"`
Expected: build error for `ModifierTapDetector`; after stubbing nothing else, the three screen tests would fail on the old key path.

- [ ] **Step 3: Implement**

Create `src/LizTerm.App/Keyboard/ModifierTapDetector.cs`:

```csharp
using Avalonia.Input;

namespace LizTerm.App.Keyboard;

/// <summary>Spec 6.3. A tap is a Left or Right Ctrl key going down and the same key coming up with no other key
/// pressed in between, so Ctrl+C is never a Reset: the C press clears the record. Auto-repeat of the same Ctrl
/// key keeps it. Pure; the screen control feeds it from its key events and resets it on focus loss.</summary>
public sealed class ModifierTapDetector
{
    private Key? _candidate;

    public void KeyDown(Key key) => _candidate = key is Key.LeftCtrl or Key.RightCtrl ? key : null;

    /// <returns>The tapped key, or null when this release is not a tap.</returns>
    public Key? KeyUp(Key key)
    {
        var tapped = _candidate == key ? key : (Key?)null;
        _candidate = null;
        return tapped;
    }

    public void Reset() => _candidate = null;
}
```

In `src/LizTerm.App/Controls/TerminalScreen.cs`:

1. Change the `DestructiveBackspace` property registration to default to true and update its comment:

```csharp
    /// <summary>Mirrors the profile's DestructiveBackspace choice; the window binds it. True (erase) by default,
    /// like a new profile (spec 3.2).</summary>
    public static readonly StyledProperty<bool> DestructiveBackspaceProperty =
        AvaloniaProperty.Register<TerminalScreen, bool>(nameof(DestructiveBackspace), defaultValue: true);
```

2. Add fields next to `_gesture`:

```csharp
    private readonly ModifierTapDetector _taps = new();
    private Keymap Keymap => DefaultKeymap.Create(DestructiveBackspace);
```

3. Replace `OnKeyDown` with the following, and add `OnKeyUp` and `OnLostFocus` after it (`using Avalonia.Interactivity;` is needed for `RoutedEventArgs`):

```csharp
    /// <summary>Spec 6.2 ordering: the platform's copy, paste, and select-all hotkeys first (they are not in the
    /// table), then the key table, then the text table, then Avalonia's text input for everything else so dead
    /// keys and IMEs keep working.</summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        _taps.KeyDown(e.Key);
        if (TryHandleClipboardKey(e))
        {
            e.Handled = true;
            return;
        }
        var chord = new KeyChord(e.Key, e.KeyModifiers);
        if (Keymap.TryMap(chord, out var key))
        {
            KeyRequested?.Invoke(this, key);
            e.Handled = true;
            return;
        }
        if (Keymap.TryText(chord, out var text))
        {
            TextEntered?.Invoke(this, text);
            e.Handled = true;
            return;
        }
        base.OnKeyDown(e);
    }

    /// <summary>A Ctrl key released alone is a tap chord (Right Ctrl is Enter, Left Ctrl is Reset by default).</summary>
    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (_taps.KeyUp(e.Key) is { } tapped && Keymap.TryMap(KeyChord.TapOf(tapped), out var key))
        {
            KeyRequested?.Invoke(this, key);
            e.Handled = true;
            return;
        }
        base.OnKeyUp(e);
    }

    protected override void OnLostFocus(RoutedEventArgs e)
    {
        _taps.Reset();
        base.OnLostFocus(e);
    }
```

4. In `DefaultKeymap.cs`, delete the `TryMap` shim and its comment.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/LizTerm.App.Tests`
Expected: all pass. If `A_right_ctrl_tap_sends_enter_and_a_ctrl_chord_does_not_reset` reports `Reset` where `Enter` was expected, the headless QWERTY map folded `ControlRight` into `Key.LeftCtrl`: replace those two lines with `window.KeyPress(Key.RightCtrl, RawInputModifiers.Control, PhysicalKey.ControlRight, null)` and `window.KeyRelease(Key.RightCtrl, RawInputModifiers.None, PhysicalKey.ControlRight, null)`, and note it in the ledger for the live pass.

- [ ] **Step 5: Commit**

```bash
git add src/LizTerm.App tests/LizTerm.App.Tests
git commit -m "Route keys through the chord table with Ctrl taps for Enter and Reset, and erase on Backspace by default in the control

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 11: Escape in the transfer dialog, focus after Dismiss, guarded hotkeys, selection hygiene

**Files:**
- Create: `src/LizTerm.App/CommandRouting.cs`
- Modify: `src/LizTerm.App/Views/FileTransferWindow.axaml.cs`, `src/LizTerm.App/ViewModels/FileTransferViewModel.cs` (one comment), `src/LizTerm.App/Views/SessionWindow.axaml`, `src/LizTerm.App/Views/SessionWindow.axaml.cs`, `src/LizTerm.App/Controls/TerminalScreen.cs`
- Test: `tests/LizTerm.App.Tests/Views/FileTransferWindowTests.cs`, `tests/LizTerm.App.Tests/Views/SessionWindowTests.cs`, create `tests/LizTerm.App.Tests/CommandRoutingTests.cs`, `tests/LizTerm.App.Tests/Controls/TerminalScreenSelectionTests.cs`

**Interfaces:**
- Produces: `public static class CommandRouting { Task TryExecuteAsync(IAsyncRelayCommand, object? = null); Task TryExecuteAsync<T>(IAsyncRelayCommand<T>, T); void TryExecute(IRelayCommand, object? = null); }`; the session window's `DismissButton`.

- [ ] **Step 1: Write the failing tests**

Add to `tests/LizTerm.App.Tests/Views/FileTransferWindowTests.cs` (`using Avalonia.Headless;` and `using Avalonia.Input;` at the top):

```csharp
    [AvaloniaFact]
    public async Task Escape_closes_the_form_and_cancels_a_running_transfer_first()
    {
        var (form, _, _) = Show();
        var formClosed = false;
        form.Closed += (_, _) => formClosed = true;
        form.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Assert.True(formClosed);

        var (window, vm, session) = Show();
        var closed = false;
        window.Closed += (_, _) => closed = true;
        vm.LocalPath = "/nonexistent/a.txt";
        vm.HostFile = "A.B";
        session.TransferCompletion = Pending();
        var run = vm.StartCommand.ExecuteAsync(null);

        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Assert.False(closed);
        Assert.True(vm.IsCancelling);
        Assert.True(session.TransferToken.IsCancellationRequested);

        session.TransferResult = new FileTransferResult(false, "Transfer canceled by user");
        session.TransferCompletion.SetResult();
        await run;
        Assert.True(vm.IsDone);

        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Assert.True(closed);
    }
```

Add to `tests/LizTerm.App.Tests/Views/SessionWindowTests.cs`:

```csharp
    /// <summary>Regression: after Dismiss the button kept focus, so the next keystrokes never reached the host.</summary>
    [AvaloniaFact]
    public void Dismiss_returns_focus_to_the_screen()
    {
        var (window, screen, vm, _, _) = Show();
        vm.ErrorMessage = "boom";
        var dismiss = window.FindControl<Button>("DismissButton")!;
        dismiss.Focus();
        Assert.False(screen.IsFocused);

        dismiss.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.True(screen.IsFocused);
    }
```

Create `tests/LizTerm.App.Tests/CommandRoutingTests.cs`:

```csharp
using CommunityToolkit.Mvvm.Input;

namespace LizTerm.App.Tests;

/// <summary>CommunityToolkit's ExecuteAsync ignores CanExecute; hotkeys reach commands through this guard.</summary>
public class CommandRoutingTests
{
    [Fact]
    public async Task Async_commands_run_only_when_they_can()
    {
        var ran = 0;
        var blocked = new AsyncRelayCommand(() => { ran++; return Task.CompletedTask; }, () => false);
        var allowed = new AsyncRelayCommand(() => { ran++; return Task.CompletedTask; }, () => true);
        await CommandRouting.TryExecuteAsync(blocked);
        Assert.Equal(0, ran);
        await CommandRouting.TryExecuteAsync(allowed);
        Assert.Equal(1, ran);
    }

    [Fact]
    public async Task Parameterized_and_plain_commands_are_guarded_too()
    {
        var seen = new List<string>();
        var typed = new AsyncRelayCommand<string>(s => { seen.Add(s!); return Task.CompletedTask; }, s => s != "no");
        await CommandRouting.TryExecuteAsync(typed, "no");
        await CommandRouting.TryExecuteAsync(typed, "yes");
        var plain = new RelayCommand(() => seen.Add("plain"), () => false);
        CommandRouting.TryExecute(plain);
        Assert.Equal(["yes"], seen);
    }
}
```

Add to `tests/LizTerm.App.Tests/Controls/TerminalScreenSelectionTests.cs`:

```csharp
    /// <summary>The control writes its own Selection with SetCurrentValue, so a binding installed by a window
    /// keeps delivering values after a drag (spec 7).</summary>
    [AvaloniaFact]
    public void The_controls_own_selection_writes_do_not_break_a_binding()
    {
        var (window, screen) = Show();
        var source = new TerminalScreen { Snapshot = ScreenSnapshot.Empty(24, 80) };
        screen.Bind(TerminalScreen.SelectionProperty, source.GetObservable(TerminalScreen.SelectionProperty));

        window.MouseDown(Center(screen, 5, 10), MouseButton.Left);
        window.MouseMove(Center(screen, 2, 3));
        window.MouseUp(Center(screen, 2, 3), MouseButton.Left);
        Assert.Equal(ScreenRegion.FromCorners(2, 3, 5, 10), screen.Selection);

        source.Selection = ScreenRegion.FromCorners(0, 0, 1, 1);
        Assert.Equal(ScreenRegion.FromCorners(0, 0, 1, 1), screen.Selection);
    }

    [AvaloniaFact]
    public void Losing_pointer_capture_ends_the_drag()
    {
        var (window, screen) = Show();
        window.MouseDown(Center(screen, 2, 3), MouseButton.Left);
        window.MouseMove(Center(screen, 4, 6));
        var before = screen.Selection;
        Assert.NotNull(before);

        screen.RaiseEvent(new PointerCaptureLostEventArgs(screen, new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, isPrimary: true)));
        window.MouseMove(Center(screen, 10, 20));
        Assert.Equal(before, screen.Selection);

        var clicked = false;
        screen.CellClicked += (_, _) => clicked = true;
        window.MouseUp(Center(screen, 10, 20), MouseButton.Left);
        Assert.False(clicked);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~FileTransferWindowTests|FullyQualifiedName~SessionWindowTests|FullyQualifiedName~CommandRoutingTests|FullyQualifiedName~TerminalScreenSelectionTests"`
Expected: `CommandRoutingTests` fails to build; `Escape_closes...` fails at the first `Assert.True(formClosed)`; `Dismiss_returns_focus...` fails on `screen.IsFocused`; `Losing_pointer_capture...` fails on the selection growing. `The_controls_own_selection_writes...` may already pass on this Avalonia version; make the `SetCurrentValue` change regardless, it is what the spec asks for.

- [ ] **Step 3: Implement**

Create `src/LizTerm.App/CommandRouting.cs`:

```csharp
using CommunityToolkit.Mvvm.Input;

namespace LizTerm.App;

/// <summary>CommunityToolkit's ExecuteAsync ignores CanExecute, which a button honours but a hotkey handler does
/// not; every command reached from a keystroke goes through here (spec 7).</summary>
public static class CommandRouting
{
    public static Task TryExecuteAsync(IAsyncRelayCommand command, object? parameter = null) =>
        command.CanExecute(parameter) ? command.ExecuteAsync(parameter) : Task.CompletedTask;

    public static Task TryExecuteAsync<T>(IAsyncRelayCommand<T> command, T parameter) =>
        command.CanExecute(parameter) ? command.ExecuteAsync(parameter) : Task.CompletedTask;

    public static void TryExecute(IRelayCommand command, object? parameter = null)
    {
        if (command.CanExecute(parameter)) command.Execute(parameter);
    }
}
```

In `src/LizTerm.App/Views/FileTransferWindow.axaml.cs`, add `using Avalonia.Input;` and this override after `OnClosing`:

```csharp
    /// <summary>Escape closes the dialog in every phase, through Closing and so through TryClose: on a running
    /// transfer the first Escape cancels and the window stays until the outcome shows, exactly like Close. Handled
    /// here rather than with IsCancel on a button because the Running panel has no Close button.</summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && !e.Handled)
        {
            e.Handled = true;
            Close();
            return;
        }
        base.OnKeyDown(e);
    }
```

In `src/LizTerm.App/ViewModels/FileTransferViewModel.cs`, add above `BytesTransferred = bytes;` in `OnProgress`:

```csharp
        // A text send adds a carriage return before every line feed on its way to the host, so the count the engine
        // reports can pass TotalBytes (the local file's length). The bar clamps at its maximum; the count stays true.
```

In `src/LizTerm.App/Views/SessionWindow.axaml`, give the Dismiss button a name and a click handler:

```xml
        <Button x:Name="DismissButton" DockPanel.Dock="Right" Content="Dismiss" Command="{Binding DismissErrorCommand}" Click="OnDismissClick" />
```

In `src/LizTerm.App/Views/SessionWindow.axaml.cs`, replace the five `Screen.*Requested` subscriptions in the constructor with:

```csharp
        Screen.KeyRequested += (_, key) => { if (ViewModel is { } vm) _ = CommandRouting.TryExecuteAsync(vm.SendKeyCommand, key); };
        Screen.TextEntered += (_, text) => _ = ViewModel?.TypeTextAsync(text);
        Screen.CellClicked += (_, cell) => _ = ViewModel?.MoveCursorAsync(cell.Row, cell.Column);
        Screen.CopyRequested += (_, _) => { if (ViewModel is { } vm) _ = CommandRouting.TryExecuteAsync(vm.CopyCommand); };
        Screen.PasteRequested += (_, _) => { if (ViewModel is { } vm) _ = CommandRouting.TryExecuteAsync(vm.PasteCommand); };
        Screen.SelectAllRequested += (_, _) => { if (ViewModel is { } vm) CommandRouting.TryExecute(vm.SelectAllCommand); };
```

and add the handler:

```csharp
    /// <summary>The command clears the message; this puts the keyboard back on the screen, where the next keystroke
    /// belongs (spec 7).</summary>
    private void OnDismissClick(object? sender, RoutedEventArgs e) => Screen.Focus();
```

In `src/LizTerm.App/Controls/TerminalScreen.cs`:

1. In `PressAt` and `OnPointerMoved`, replace `Selection = _gesture.Region;` with `SetCurrentValue(SelectionProperty, _gesture.Region);`.
2. In `OnPropertyChanged`, replace `Selection = null;` with `SetCurrentValue(SelectionProperty, null);`.
3. Add after `OnPointerReleased`:

```csharp
    /// <summary>A capture lost mid-drag (a modal opened, the window deactivated) ends the gesture where it was:
    /// the next move must not extend it and the next release must not click.</summary>
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _gesture.Release();
    }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/LizTerm.App.Tests`
Expected: all pass, including the existing hotkey tests in `SessionWindowTests`.

- [ ] **Step 5: Commit**

```bash
git add src/LizTerm.App tests/LizTerm.App.Tests
git commit -m "Close the transfer dialog on Escape, refocus the screen after Dismiss, guard hotkey commands with CanExecute, and keep the selection binding intact

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---
### Task 12: App cleanup: numbered wire-log names, About sizing, shared waits, environment isolation, unbracketed IPv6

**Files:**
- Modify: `src/LizTerm.App/ViewModels/SessionViewModel.cs` (`OnIsWireLoggingChanged`, new `UniquePath`), `src/LizTerm.App/Views/AboutWindow.axaml`, `src/LizTerm.App/Startup/StartupArguments.cs`
- Create: `tests/LizTerm.App.Tests/Wait.cs`, `tests/LizTerm.App.Tests/EnvironmentCollection.cs`
- Test: `tests/LizTerm.App.Tests/ViewModels/SessionViewModelWireLogTests.cs`, `tests/LizTerm.App.Tests/Views/AboutWindowTests.cs`, `tests/LizTerm.App.Tests/Startup/StartupArgumentsTests.cs`, `tests/LizTerm.App.Tests/SessionFactoryTests.cs`, `tests/LizTerm.App.Tests/ViewModels/FileTransferViewModelTests.cs`

**Interfaces:**
- Produces: `internal static string SessionViewModel.UniquePath(string directory, string fileName)`; `internal static class Wait { static Task UntilAsync(Func<bool>, string, TimeSpan? = null) }` in `LizTerm.App.Tests`; `EnvironmentCollection.Name`.

- [ ] **Step 1: Write the failing tests**

Create `tests/LizTerm.App.Tests/Wait.cs`:

```csharp
namespace LizTerm.App.Tests;

/// <summary>Polls a condition until it holds or the timeout passes. The one copy for this project.</summary>
internal static class Wait
{
    public static async Task UntilAsync(Func<bool> condition, string what, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(2));
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("Timed out waiting for " + what);
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }
    }
}
```

Add to `tests/LizTerm.App.Tests/ViewModels/SessionViewModelWireLogTests.cs` (`using LizTerm.App.Tests.Fakes;` and `using LizTerm.App.ViewModels;` are already there):

```csharp
    [Fact]
    public void A_second_log_in_the_same_second_gets_a_numbered_name()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lizterm-wirelog-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            Assert.Equal(Path.Combine(dir, "wire-a-1.log"), SessionViewModel.UniquePath(dir, "wire-a-1.log"));
            File.WriteAllText(Path.Combine(dir, "wire-a-1.log"), "");
            Assert.Equal(Path.Combine(dir, "wire-a-1-2.log"), SessionViewModel.UniquePath(dir, "wire-a-1.log"));
            File.WriteAllText(Path.Combine(dir, "wire-a-1-2.log"), "");
            Assert.Equal(Path.Combine(dir, "wire-a-1-3.log"), SessionViewModel.UniquePath(dir, "wire-a-1.log"));

            // Through the toggle: whichever second the start lands in, a file with that name already exists.
            var session = new FakeEmulatorSession();
            var vm = new SessionViewModel(session, a => a(), new FakeTextClipboard()) { WireLogDirectory = dir };
            var now = DateTime.Now;
            File.WriteAllText(Path.Combine(dir, SessionViewModel.WireLogFileName(session.Profile.Name, now)), "");
            File.WriteAllText(Path.Combine(dir, SessionViewModel.WireLogFileName(session.Profile.Name, now.AddSeconds(1))), "");
            vm.IsWireLogging = true;
            Assert.EndsWith("-2.log", session.WireLogPath);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
```

In `tests/LizTerm.App.Tests/Views/AboutWindowTests.cs`, add to `Shows_version_engine_and_notices` after `window.Show();`:

```csharp
        // Spec 8: the engine path wraps, so the window grows with it instead of clipping at a fixed height.
        Assert.Equal(SizeToContent.Height, window.SizeToContent);
        Assert.Equal(220, window.FindControl<TextBox>("NoticesText")!.Height);
```

In `tests/LizTerm.App.Tests/Startup/StartupArgumentsTests.cs`, add three rows to `Bad_syntax_is_an_error_with_usage`:

```csharp
    [InlineData("fe80::1")]
    [InlineData("L:fe80::1")]
    [InlineData("::1:23")]
```

and this test:

```csharp
    [Fact]
    public void A_saved_profile_can_be_named_like_an_unbracketed_ipv6_address()
    {
        var profiles = new[] { new SessionProfile { Name = "fe80::1", Host = "mvs.local" } };
        Assert.Same(profiles[0], StartupArguments.Parse(["fe80::1"]).Resolve(profiles));
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~SessionViewModelWireLogTests|FullyQualifiedName~AboutWindowTests|FullyQualifiedName~StartupArgumentsTests"`
Expected: build error for `UniquePath`; the About assertion fails on `SizeToContent`; `Bad_syntax` fails for the three IPv6 rows (a bare `fe80::1` is not an error today, and a prefixed one becomes a host).

- [ ] **Step 3: Implement**

In `src/LizTerm.App/ViewModels/SessionViewModel.cs`, add after `WireLogFileName`:

```csharp
    /// <summary>The path for a new file of that name, or the first of name-2, name-3, ... that does not exist yet.
    /// Two logs started in the same second must not share a file (spec 8).</summary>
    internal static string UniquePath(string directory, string fileName)
    {
        var path = Path.Combine(directory, fileName);
        if (!File.Exists(path)) return path;
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        for (var n = 2; ; n++)
        {
            var candidate = Path.Combine(directory, $"{stem}-{n}{extension}");
            if (!File.Exists(candidate)) return candidate;
        }
    }
```

and in `OnIsWireLoggingChanged` replace the `StartWireLog` line with:

```csharp
                _session.StartWireLog(UniquePath(WireLogDirectory, WireLogFileName(Profile.Name, DateTime.Now)));
```

In `src/LizTerm.App/Views/AboutWindow.axaml`, change the Window attributes `Width="560" Height="480" CanResize="False"` to `Width="560" SizeToContent="Height" CanResize="False"`, and give the notices box a height: `<TextBox x:Name="NoticesText" Height="220" IsReadOnly="True" AcceptsReturn="True" TextWrapping="Wrap" FontSize="11" />`.

In `src/LizTerm.App/Startup/StartupArguments.cs`, in `ParseHostPort`, replace everything from `var lastColon = arg.LastIndexOf(':');` through the end of that `if` block with:

```csharp
        // More than one colon outside brackets can only be an unbracketed IPv6 address, which x3270 does not take
        // either: a usage error, never a hostname, and the usage line shows the bracketed form (spec 8). Before this,
        // a forcing prefix such as L: passed the text to the engine whole.
        if (arg.Count(c => c == ':') > 1)
        {
            malformed = true;
            return (null, null);
        }

        var lastColon = arg.LastIndexOf(':');
        if (lastColon > 0)
        {
            if (TryParsePort(arg[(lastColon + 1)..], out var port)) return (arg[..lastColon], port);
            malformed = true;
            return (null, null);
        }
```

Also update the `Usage` doc comment on the record: after "(IPv6 hosts bracketed)", add "; an unbracketed IPv6 address is a usage error".

Then the test-project cleanup:

1. Delete the private `WaitUntilAsync` in `tests/LizTerm.App.Tests/ViewModels/FileTransferViewModelTests.cs` and any other copy (`grep -rn "static async Task WaitUntilAsync" tests/LizTerm.App.Tests`), and rewrite their call sites to `Wait.UntilAsync(`.
2. Create `tests/LizTerm.App.Tests/EnvironmentCollection.cs`:

```csharp
namespace LizTerm.App.Tests;

/// <summary>Tests that set LIZTERM_B3270_PATH must not run beside tests that read it (spec 8).</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class EnvironmentCollection
{
    public const string Name = "Environment";
}
```

and put `[Collection(EnvironmentCollection.Name)]` on `SessionFactoryTests` (its two tests already restore the variable in a `finally`).

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/LizTerm.App.Tests`
Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add src/LizTerm.App tests/LizTerm.App.Tests
git commit -m "Number same-second wire logs, size About to its content, share one test wait helper, isolate the engine-path tests, and reject unbracketed IPv6 arguments

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 13: The coverage batch

**Files:**
- Test: `tests/LizTerm.Backend.B3270.Tests/Protocol/TransferMapperTests.cs`, `tests/LizTerm.Backend.B3270.Tests/B3270SessionWireLogTests.cs`, `tests/LizTerm.App.Tests/ViewModels/FileTransferViewModelTests.cs`, `tests/LizTerm.App.Tests/Views/FileTransferWindowTests.cs`, `tests/LizTerm.App.Tests/Views/SessionWindowTests.cs`, `tests/LizTerm.App.Tests/Controls/TerminalScreenSelectionTests.cs`

No production change is expected. If any test below fails, the failure is a real finding: fix the code in the same task and say so in the commit message.

- [ ] **Step 1: Add the tests**

`TransferMapperTests.cs`:

```csharp
    [Fact]
    public void Binary_tso_send_keeps_allocation_and_drops_cr_and_remap()
    {
        var args = Args(Send() with
        {
            Mode = TransferMode.Binary, RecordFormat = RecordFormat.Fixed, Lrecl = 80, Blksize = 3120,
            AllocationUnits = AllocationUnits.Tracks, PrimarySpace = 5, SecondarySpace = 1,
        });
        Assert.Contains("mode=binary", args);
        Assert.DoesNotContain(args, a => a.StartsWith("cr=") || a.StartsWith("remap="));
        Assert.Contains("recfm=fixed", args);
        Assert.Contains("lrecl=80", args);
        Assert.Contains("blksize=3120", args);
        Assert.Contains("allocation=tracks", args);
        Assert.Contains("primaryspace=5", args);
        Assert.Contains("secondaryspace=1", args);
    }

    [Theory]
    [InlineData(TransferHostType.Tso, "host=tso")]
    [InlineData(TransferHostType.Vm, "host=vm")]
    [InlineData(TransferHostType.Cics, "host=cics")]
    public void Every_host_type_has_a_keyword(TransferHostType host, string expected) => Assert.Contains(expected, Args(Send(host)));

    [Theory]
    [InlineData(RecordFormat.Fixed, "recfm=fixed")]
    [InlineData(RecordFormat.Variable, "recfm=variable")]
    [InlineData(RecordFormat.Undefined, "recfm=undefined")]
    public void Every_record_format_has_a_keyword_on_tso(RecordFormat format, string expected) =>
        Assert.Contains(expected, Args(Send() with { RecordFormat = format }));

    [Theory]
    [InlineData(AllocationUnits.Tracks, "allocation=tracks")]
    [InlineData(AllocationUnits.Cylinders, "allocation=cylinders")]
    [InlineData(AllocationUnits.AvBlock, "allocation=avblock")]
    public void Every_allocation_unit_has_a_keyword_on_tso(AllocationUnits units, string expected) =>
        Assert.Contains(expected, Args(Send() with { AllocationUnits = units, PrimarySpace = 1, AverageBlock = units == AllocationUnits.AvBlock ? 4096 : null }));

    [Fact]
    public void Default_record_format_and_units_emit_nothing()
    {
        var args = Args(Send());
        Assert.DoesNotContain(args, a => a.StartsWith("recfm=") || a.StartsWith("allocation="));
    }
```

`B3270SessionWireLogTests.cs`:

```csharp
    [Fact]
    public async Task Stop_when_no_log_is_active_is_a_no_op()
    {
        await using var session = new B3270Session(new SessionProfile { Name = "t", Host = "h" }, () => new FakeB3270Process());
        session.StopWireLog();
        Assert.Null(session.WireLogPath);
    }
```

`FileTransferViewModelTests.cs`:

```csharp
    /// <summary>The window binds the derived flags; each must be announced when its inputs change, not only computed.</summary>
    [Fact]
    public void Derived_flags_raise_property_changed()
    {
        var (vm, _, _) = Create();
        var changed = new List<string>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName!);

        vm.HostType = TransferHostType.Vm;
        Assert.Superset(new HashSet<string> { nameof(vm.IsTso), nameof(vm.RecordFormats), nameof(vm.CanSetRecordFormat), nameof(vm.CanSetLrecl), nameof(vm.CanSetBlksize), nameof(vm.CanSetSpace), nameof(vm.CanSetAverageBlock) }, changed.ToHashSet());
        changed.Clear();
        vm.RecordFormat = RecordFormat.Fixed;
        Assert.Superset(new HashSet<string> { nameof(vm.HasRecordFormat), nameof(vm.CanSetLrecl), nameof(vm.CanSetBlksize) }, changed.ToHashSet());
        changed.Clear();
        vm.AllocationUnits = AllocationUnits.AvBlock;
        Assert.Superset(new HashSet<string> { nameof(vm.HasAllocation), nameof(vm.IsAvBlock), nameof(vm.CanSetSpace), nameof(vm.CanSetAverageBlock) }, changed.ToHashSet());
        changed.Clear();
        vm.IsReceive = true;
        Assert.Superset(new HashSet<string> { nameof(vm.IsReceive), nameof(vm.ShowAdvanced) }, changed.ToHashSet());
        changed.Clear();
        vm.IsBinary = true;
        Assert.Contains(nameof(vm.IsBinary), changed);
    }
```

`FileTransferWindowTests.cs`:

```csharp
    [AvaloniaFact]
    public void Labels_pass_null_through_and_never_convert_back()
    {
        Assert.Null(TransferLabels.Converter.Convert(null, typeof(string), null, CultureInfo.InvariantCulture));
        Assert.Throws<NotSupportedException>(() => TransferLabels.Converter.ConvertBack("TSO", typeof(TransferHostType), null, CultureInfo.InvariantCulture));
    }
```

`SessionWindowTests.cs`:

```csharp
    /// <summary>OnFileTransferClick's catch: a dialog that cannot be shown is reported in the banner, not thrown
    /// from an async void handler. A window that was never shown is an owner ShowDialog refuses.</summary>
    [AvaloniaFact]
    public async Task A_file_transfer_dialog_that_cannot_open_is_reported_in_the_banner()
    {
        var session = new FakeEmulatorSession();
        var vm = new SessionViewModel(session, action => action(), new FakeTextClipboard());
        var window = new SessionWindow { DataContext = vm };
        session.RaiseConnection(ConnectionState.Connected3270);

        window.FindControl<MenuItem>("FileTransferMenuItem")!.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

        await Wait.UntilAsync(() => vm.ErrorMessage is not null, "the error banner");
        Assert.StartsWith("Could not open the File Transfer dialog:", vm.ErrorMessage);
    }
```

If this times out because `ShowDialog` accepted the never-shown owner on this Avalonia version, make the owner invalid the other way: `window.Show(); window.Close();` before raising the click.

`TerminalScreenSelectionTests.cs`:

```csharp
    /// <summary>A third click is a plain press: it drops the word selection rather than growing it.</summary>
    [AvaloniaFact]
    public void A_triple_click_does_not_extend_the_word_selection()
    {
        var buffer = new ScreenBuffer(24, 80);
        buffer.SetText(3, 10, "SYS1.PROCLIB", null, null, null);
        var (_, screen) = Show(buffer.Snapshot());
        screen.PressAt((3, 14), 2);
        Assert.Equal(ScreenRegion.FromCorners(3, 10, 3, 21), screen.Selection);
        screen.PressAt((3, 14), 3);
        Assert.Null(screen.Selection);
    }
```

- [ ] **Step 2: Run the tests**

Run: `dotnet test LizTerm.slnx`
Expected: all pass (4 integration skips without a host). A failure here is a finding: fix it in the code, keep the test, and name the fix in the commit message.

- [ ] **Step 3: Commit**

```bash
git add tests
git commit -m "Cover the transfer mapper's binary allocation path and every enum, derived-flag notifications, the label converter's edges, the transfer dialog's failure path, a stopped-when-inactive wire log, and triple-click

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 14: Live lane hardening, the live pinning test, and the pinned-login fixture

**Files:**
- Modify: `tests/LizTerm.Integration.Tests/ScreenWaiter.cs`, `tests/LizTerm.Integration.Tests/TsoNavigator.cs`, `tests/LizTerm.Integration.Tests/LiveHostTests.cs`, `tests/LizTerm.Backend.B3270.Tests/ReplayTests.cs`, `tests/LizTerm.Backend.B3270.Tests/Fixtures/README.md`
- Create: `tests/LizTerm.Backend.B3270.Tests/Fixtures/gateway-pinned-login.jsonl`

**Interfaces:**
- Consumes: `SslStreamCertificateFetcher`, `CertificateReader.Fingerprint` (Tasks 2 and 3); `ConnectOptions(VerifyCertificate, Pin)` (Task 1); `TlsInfo.Verified`.

- [ ] **Step 1: Rewrite `ScreenWaiter`**

Replace `tests/LizTerm.Integration.Tests/ScreenWaiter.cs` with:

```csharp
using System.Diagnostics;
using LizTerm.Core.Screen;
using LizTerm.Core.Session;

namespace LizTerm.Integration.Tests;

/// <summary>Waits on host screens by text, never by coordinates. Every timeout throws with the last screen's text,
/// so a failing live run shows what the host actually displayed. Time comes from one Stopwatch, so a clock step
/// during a run cannot stretch or cut a wait, and each wait is one WaitAsync on the next-change signal rather than
/// an abandoned Task.Delay per update (spec 8).</summary>
internal sealed class ScreenWaiter : IDisposable
{
    private readonly IEmulatorSession _session;
    private readonly object _lock = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private ScreenSnapshot _latest;
    private TimeSpan _lastChange;
    private TaskCompletionSource _changed = NewSignal();

    public ScreenWaiter(IEmulatorSession session)
    {
        _session = session;
        _latest = session.CurrentScreen;
        _lastChange = _clock.Elapsed;
        session.ScreenUpdated += OnScreen;
    }

    public ScreenSnapshot Latest { get { lock (_lock) return _latest; } }

    public string LatestText => Latest.ToText();

    /// <summary>Returns the first screen (the current one included) whose text satisfies the predicate.</summary>
    public async Task<ScreenSnapshot> WaitForAsync(Func<string, bool> predicate, TimeSpan timeout, string what)
    {
        var deadline = _clock.Elapsed + timeout;
        while (true)
        {
            ScreenSnapshot screen;
            Task changed;
            lock (_lock) { screen = _latest; changed = _changed.Task; }
            if (predicate(screen.ToText())) return screen;
            var remaining = deadline - _clock.Elapsed;
            if (remaining <= TimeSpan.Zero) throw new TimeoutException($"Timed out waiting for {what}. Last screen:\n{screen.ToText()}");
            await WaitOrTimeoutAsync(changed, remaining);
        }
    }

    /// <summary>b3270 rejects String() while the keyboard is locked, so every keystroke waits for the unlock.</summary>
    public async Task WaitForUnlockedKeyboardAsync(TimeSpan timeout)
    {
        var deadline = _clock.Elapsed + timeout;
        while (_session.KeyboardStatus.Lock != KeyboardLock.Unlocked)
        {
            if (_clock.Elapsed > deadline)
                throw new TimeoutException($"Keyboard still locked ({_session.KeyboardStatus.Lock}). Last screen:\n{LatestText}");
            await Task.Delay(50);
        }
    }

    /// <summary>Returns once no screen update has arrived for <paramref name="quiet"/>, or when the timeout expires,
    /// whichever is first; the wait never overshoots the timeout. A host paints a screen in several bursts and keeps
    /// writing after the text that identifies the screen has appeared, so a caller that types straight away sends
    /// its text into a layout the host is still changing; TSO answers that with "INVALID COMMAND NAME SYNTAX".</summary>
    public async Task WaitForQuietAsync(TimeSpan quiet, TimeSpan timeout)
    {
        var deadline = _clock.Elapsed + timeout;
        while (true)
        {
            TimeSpan last;
            lock (_lock) last = _lastChange;
            var now = _clock.Elapsed;
            var idle = now - last;
            if (idle >= quiet || now >= deadline) return;
            var untilQuiet = quiet - idle;
            var untilDeadline = deadline - now;
            await Task.Delay(untilQuiet < untilDeadline ? untilQuiet : untilDeadline);
        }
    }

    private static async Task WaitOrTimeoutAsync(Task signal, TimeSpan timeout)
    {
        try
        {
            await signal.WaitAsync(timeout);
        }
        catch (TimeoutException)
        {
            // The caller re-checks its deadline.
        }
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private void OnScreen(object? sender, ScreenSnapshot snapshot)
    {
        lock (_lock)
        {
            _latest = snapshot;
            _lastChange = _clock.Elapsed;
            _changed.TrySetResult();
            _changed = NewSignal();
        }
    }

    public void Dispose() => _session.ScreenUpdated -= OnScreen;
}
```

- [ ] **Step 2: Fix `TsoNavigator`'s documentation, gate, and logoff**

In `tests/LizTerm.Integration.Tests/TsoNavigator.cs`:

1. Replace the class summary with:

```csharp
/// <summary>Drives an MVS 3.8j TSO session: logon to READY, a command that returns to READY, and logoff. Screens
/// are recognized by text only, never by coordinates. The rules, which are all a host must satisfy: the Hercules
/// banner is a screen that says "hit ENTER" (any case) and has no "===>"; the logon screen is the first screen
/// with "===>"; the password prompt says "PASSWORD"; READY means the last non-blank line is exactly READY, which
/// is where IND$FILE must be started from; a "***" pause is answered with Enter and any other "===>" screen (a
/// menu) with PF3. A host whose screens carry none of these strings needs a rule added here, not coordinates.</summary>
```

2. In `ReachReadyAsync`, after `if (IsAtReady(text)) return;` add:

```csharp
            // The screen may have moved on during the quiet wait; act only on one of the gate screens, otherwise
            // wait for the next one rather than sending PF3 into whatever is there now.
            if (!(text.Contains("***") || text.Contains("===>"))) continue;
```

3. Replace `LogoffAsync` with (add `using System.Diagnostics;`):

```csharp
    /// <summary>LOGOFF, then wait for the host to drop the line or show the logon screen again. A host that does
    /// neither within a step is reported as a diagnostic rather than silently returning, because the next run is
    /// then refused with USERID IN USE.</summary>
    public async Task LogoffAsync()
    {
        await TypeAndEnterAsync("LOGOFF");
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < Step)
        {
            if (!session.ConnectionState.IsConnected()) return;
            if (screens.LatestText.Contains("LOGON", StringComparison.OrdinalIgnoreCase) && !IsAtReady(screens.LatestText)) return;
            await Task.Delay(200);
        }
        TestContext.Current.SendDiagnosticMessage($"LOGOFF: the host neither dropped the line nor showed the logon screen within {Step.TotalSeconds:0} s. Last screen:\n{screens.LatestText}");
    }
```

- [ ] **Step 3: Timeouts and the pinning test**

In `tests/LizTerm.Integration.Tests/LiveHostTests.cs`: add `using System.Security.Cryptography;`, `using System.Security.Cryptography.X509Certificates;`, and `using LizTerm.Core.Security;`; add `private const int LiveTimeout = 600_000;` to the class; change every `[Fact]` to `[Fact(Timeout = LiveTimeout)]`; and add:

```csharp
    /// <summary>Spec 9: a pin made from what the gateway presents verifies (the tls indication reports verified),
    /// and a decoy pin fails with the certificate flag on the same session. The profile from the environment has
    /// verification off, so both attempts force it on; a pin is ignored when verification is off (spec 3.1). Needs
    /// the TLS gateway (LIZTERM_TEST_TLS=1, LIZTERM_TEST_VERIFY_CERT=0). Run alone with LIZTERM_WIRE_LOG set to
    /// record the gateway-pinned-login fixture.</summary>
    [Fact(Timeout = LiveTimeout)]
    public async Task A_pinned_certificate_verifies_and_a_decoy_pin_fails()
    {
        var target = Environment.GetEnvironmentVariable("LIZTERM_TEST_HOST");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(target), "LIZTERM_TEST_HOST is not set");
        var profile = ProfileFor(target!);
        Assert.SkipUnless(profile is { UseTls: true, VerifyCertificate: false }, "needs LIZTERM_TEST_TLS=1 and LIZTERM_TEST_VERIFY_CERT=0");
        var ct = TestContext.Current.CancellationToken;

        var presented = await new SslStreamCertificateFetcher().FetchAsync(profile.Host, profile.Port, ct);
        Assert.True(presented.Pinnable, presented.NotPinnableReason);
        var pin = new CertificatePin(presented.Sha256, presented.Subject, presented.Pem);

        await using var session = new B3270Session(profile, () => new B3270ChildProcess(B3270Locator.Find().Path), WireLog.TryFromEnvironment(out _));
        await session.ConnectAsync(new ConnectOptions(VerifyCertificate: true, Pin: pin), ct);
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (session.Tls?.Verified != true && DateTime.UtcNow < deadline) await Task.Delay(50, ct);
        Assert.True(session.Tls?.Secure, "session is not secure");
        Assert.True(session.Tls?.Verified, "the pinned connect was not verified");
        await session.DisconnectAsync();

        using var decoyKey = RSA.Create(2048);
        using var decoy = new CertificateRequest("CN=localhost", decoyKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
            .CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        var decoyPin = new CertificatePin(CertificateReader.Fingerprint(decoy), decoy.Subject, decoy.ExportCertificatePem() + "\n");
        var ex = await Assert.ThrowsAsync<ConnectionFailedException>(() =>
            session.ConnectAsync(new ConnectOptions(VerifyCertificate: true, Pin: decoyPin), ct));
        Assert.True(ex.CertificateVerificationFailed, string.Join(" | ", ex.Lines));
        Assert.Equal(ConnectionState.Disconnected, session.ConnectionState);
    }
```

The integration project references only the backend project, which references Core, so `LizTerm.Core.Security` resolves transitively.

- [ ] **Step 4: Run the lane**

```bash
dotnet test tests/LizTerm.Integration.Tests
```

Expected without a host: 5 skipped. Then, in a shell where the live variables are loaded:

```bash
source ~/.config/lizterm-test.env && dotnet test tests/LizTerm.Integration.Tests
```

Expected: the four gateway tests pass (the IND$FILE round trip needs the MVS/CE host and its credentials and skips against the gateway). If the pinned connect is not verified, print `session.Tls` and the wire log: `Set` must show `verifyHostCert true`, the temp path, and `acceptHostname any`.

- [ ] **Step 5: Record the fixture**

```bash
source ~/.config/lizterm-test.env
rm -f /tmp/lizterm-pin-wire.log
LIZTERM_WIRE_LOG=/tmp/lizterm-pin-wire.log dotnet test tests/LizTerm.Integration.Tests --filter "FullyQualifiedName~A_pinned_certificate_verifies_and_a_decoy_pin_fails"
tools/wirelog-to-fixture.sh /tmp/lizterm-pin-wire.log tests/LizTerm.Backend.B3270.Tests/Fixtures/gateway-pinned-login.jsonl
# Keep the first, verified connection only: everything up to the not-connected that follows connected-3270.
awk '{ print } /connected-3270/ { seen = 1 } seen && /"state":"not-connected"/ { exit }' tests/LizTerm.Backend.B3270.Tests/Fixtures/gateway-pinned-login.jsonl > /tmp/pinned.jsonl && mv /tmp/pinned.jsonl tests/LizTerm.Backend.B3270.Tests/Fixtures/gateway-pinned-login.jsonl
grep -c . tests/LizTerm.Backend.B3270.Tests/Fixtures/gateway-pinned-login.jsonl
grep -o '"verified":[a-z]*' tests/LizTerm.Backend.B3270.Tests/Fixtures/gateway-pinned-login.jsonl
```

Expected: the last grep prints `"verified":true` once. Check that the file holds no `"run":` lines (inbound only) and that the fixture is picked up by the test project: `grep -n "Fixtures" tests/LizTerm.Backend.B3270.Tests/LizTerm.Backend.B3270.Tests.csproj` shows a glob; if the fixtures are listed one by one, add this one the same way.

- [ ] **Step 6: Replay test and README**

Add to `tests/LizTerm.Backend.B3270.Tests/ReplayTests.cs`:

```csharp
    /// <summary>Recorded from the live pinning test: a connect with verifyHostCert on and caFile pointing at the
    /// gateway's own certificate. The only difference from gateway-login-tls.jsonl that matters is verified:true.</summary>
    [Fact]
    public async Task Gateway_pinned_login_replays_to_a_verified_tls_connection()
    {
        var fake = new FakeB3270Process { AutoInitialize = false, RunResponder = _ => [] };
        foreach (var line in File.ReadLines(Fixture("gateway-pinned-login.jsonl"))) fake.Emit(line);
        fake.Exit(0);

        var profile = new SessionProfile { Name = "replay", Host = "gateway.test", Port = 4270, UseTls = true };
        var session = new B3270Session(profile, () => fake);
        var states = new List<ConnectionState>();
        TlsInfo? tlsWhileConnected = null;
        session.ConnectionChanged += (_, s) =>
        {
            states.Add(s);
            if (s == ConnectionState.Connected3270) tlsWhileConnected = session.Tls;
        };
        var ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        session.Faulted += (_, _) => ended.TrySetResult();

        await session.StartProcessAsync(TestContext.Current.CancellationToken);
        await ended.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Contains(ConnectionState.Connected3270, states);
        Assert.Equal(ConnectionState.Disconnected, states[^1]);
        Assert.NotNull(tlsWhileConnected);
        Assert.True(tlsWhileConnected!.Secure);
        Assert.True(tlsWhileConnected.Verified);
    }
```

Add to `tests/LizTerm.Backend.B3270.Tests/Fixtures/README.md`, in the same shape as the `gateway-login-tls.jsonl` entry:

```markdown
## gateway-pinned-login.jsonl

Inbound side of `LiveHostTests.A_pinned_certificate_verifies_and_a_decoy_pin_fails` against the TLS gateway
(2026-09-05), made with `tools/wirelog-to-fixture.sh` and trimmed to the first connection: `Set(verifyHostCert,
true, caFile, <pin>, acceptHostname, any)` then `Connect`. It is `gateway-login-tls.jsonl` with `verified:true`
in the `tls` indication, which is what `ReplayTests.Gateway_pinned_login_replays_to_a_verified_tls_connection`
asserts. The decoy attempt that followed in the live run was cut.
```

Run: `dotnet test tests/LizTerm.Backend.B3270.Tests --filter "FullyQualifiedName~ReplayTests"`
Expected: 4 pass.

- [ ] **Step 7: Commit**

```bash
git add tests/LizTerm.Integration.Tests tests/LizTerm.Backend.B3270.Tests
git commit -m "Harden the live lane's waits and logoff, time out every live test, prove a pinned connect verifies against the gateway, and replay it as a fixture

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---
### Task 15: Documentation: CLAUDE.md, the v1 spec pointer, and the as-built section of the 3b spec

**Files:**
- Modify: `CLAUDE.md`, `docs/superpowers/specs/2026-09-03-lizterm-v1-design.md` (section 6.5), `docs/superpowers/specs/2026-09-05-lizterm-m2-hardening-design.md`

- [ ] **Step 1: CLAUDE.md**

Make these edits, keeping the file's voice (facts an engineer needs, one paragraph each, no headings added):

1. In "Core model", add a bullet after the `TransferAsync` one:

```markdown
- `SessionProfile.PinnedCertificate` is a `CertificatePin` (SHA-256 fingerprint as colon-separated upper-case hex,
  subject, and the PEM chain leaf first) or null; `ConnectOptions(VerifyCertificate, Pin)` overrides either for one
  attempt. The effective rule is in `B3270Session.ConnectAsync`: verify off means no pin; otherwise the one-shot pin
  wins over the profile's; no pin means the engine's default trust. `DestructiveBackspace` defaults to true (every
  x3270-family default keymap erases; the old belief that x3270 defaults to cursor-left came from the `BackSpace()`
  action's name), and the profile JSON writes every field, so a saved false survives. `LizTerm.Core.Security` holds
  `ICertificateFetcher` and `SslStreamCertificateFetcher` (one handshake that captures and accepts the chain, 10 s
  bound, `IOException` "No TLS answer ..." on timeout) and `CertificateReader` (fingerprint, PEM, and whether the
  chain is pinnable: .NET validates it with its own self-signed members as the only trust roots, which is what
  OpenSSL will do with the pin file). They are BCL-only, so they live in Core and the integration lane can use them.
```

2. In "Backend", add a bullet after the `TransferMapper` one:

```markdown
- Every connect sends one `Set(verifyHostCert,…,caFile,…,acceptHostname,…)` with all three explicit, so an attempt
  never inherits the previous one's trust settings (verified: empty values clear them in the same engine). A pinned
  attempt writes the PEM to `Path.GetTempPath()/lizterm-pin-<guid>.pem` (owner-only on Unix) just before that Set
  and deletes it in a `finally` once the Connect run has answered, because x3270 loads `caFile` in `sio_init` for
  each connection; `acceptHostname` is `any` with a pin since the pin already names the certificate.
  `LastPinFile` is the test seam. `WaitForDisconnectedAsync` shares one completion source between overlapping
  callers (`CompareExchange` in, conditional clear out); the field is not `volatile` because `Interlocked` on a
  volatile field is CS0420.
```

3. In "App", replace the sentences from "Key events go through `DefaultKeymap` first" through "the control's `DestructiveBackspace` property carries that choice." with:

```markdown
Key events go through the platform copy, paste, and select-all hotkeys first, then `Keymap.TryMap`, then
`Keymap.TryText` (Ctrl+[ types `¬`, Ctrl+6 `¢`), then fall through to Avalonia's text input so dead keys and IMEs
work. `Keymap` (`Keyboard/`) is an immutable table of `KeyChord(Key, Modifiers, Tap)` to `TerminalKey`, built by
`DefaultKeymap.Create(destructiveBackspace)` (two cached instances) from Vista TN3270's defaults, cross-checked
against wc3270 in the 3b spec's section 6.2: Escape is Attn, Shift+Escape SysReq, Pause and Ctrl+Escape Clear,
Page Up/Down PF7/PF8, Ctrl+Insert/Home/PageUp and Alt+1/2/3 the PA keys, Ctrl+F1..12 and Shift+F1..12 PF13..24,
Shift+Enter Newline, Ctrl+R Reset. A Left Ctrl tap is Reset and a Right Ctrl tap is Enter: `ModifierTapDetector`
sees a Ctrl key down and the same key up with nothing between (`OnKeyUp` looks up `KeyChord.TapOf`), and focus
loss resets it. `Keymap.With` is the seam for user remapping later; nothing else about remapping exists. The
control's `DestructiveBackspace` property (default true, bound to the profile) picks which table.
`CommandRouting.TryExecute[Async]` runs a command only when it `CanExecute`, and every command reached from a
keystroke goes through it because CommunityToolkit's `ExecuteAsync` ignores the guard.
```

4. In "App", in the paragraph about the certificate prompt (`ICertificatePrompt`), replace "offers connect-anyway through `ICertificatePrompt` (`Dialogs/`, injected like the clipboard; `saveProfile` is null for ad hoc profiles so the checkbox is hidden;" with:

```markdown
offers connect-anyway through `ICertificatePrompt` (`Dialogs/`, injected like the clipboard, asked with a
`CertificatePromptRequest`: reason lines, what the host presented (read by the injected `ICertificateFetcher` for
TLS profiles only, under a fresh `ConnectTimeout` source), the previous pin, `CanPin`, and `CannotPinReason`;
`CanPin` needs a saved TLS profile, a pinnable certificate, and a fingerprint that differs from the pin in force,
which is what stops a rejected pin from being offered again; "Trust this certificate for this profile" pins: the
profile is saved with the pin and verification on, `_pinOverride` carries it for the window's life because the
session's profile is fixed, and Connect Anyway without it is one attempt with verification off; a changed
certificate reopens the same window titled "Certificate changed" with both fingerprints; the editor shows a pinned
profile's fingerprint with a Forget button, the only way back to default trust;
```

5. In "App", add to the File transfer paragraph: "Escape closes the dialog in every phase through the window's `OnKeyDown`, so it goes through `Closing` and `TryClose` like the Close button; the Running panel has no Close button, which is why it is not an `IsCancel` button." and to the session window description: "`SessionWindow` refocuses the screen after the error bar's Dismiss. `TerminalScreen` writes its own `Selection` with `SetCurrentValue` so a binding survives, and `OnPointerCaptureLost` ends a drag." Also note: "Wire log names gain `-2`, `-3` when two starts land in the same second (`SessionViewModel.UniquePath`)."

6. In "Tests", extend the new-fakes bullet: `FakeCertificateFetcher` (`Result`, `Exception`, `Calls` as `fetch:<host>:<port>`), `FakeCertificatePrompt.LastRequest`, `FakeEmulatorSession` records `connect:pin:<sha256>`; each test project has one `Wait.UntilAsync(condition, what, timeout?)`; `EnvironmentCollection` (a non-parallel xunit collection) holds every class that sets `LIZTERM_B3270_PATH`; `TestCertificates` in the Core tests makes self-signed and CA-signed certificates and re-imports through PKCS#12 so macOS accepts the key for a loopback SslStream server; the live tests carry a 10 minute xunit timeout; `gateway-pinned-login.jsonl` replays a verified pinned connect.

7. In "Recording a replay fixture", add `gateway-pinned-login.jsonl` to the list of fixtures with one line on how it was made (the pinning live test run alone with `LIZTERM_WIRE_LOG`, trimmed to the first connection).

- [ ] **Step 2: The v1 spec pointer**

In `docs/superpowers/specs/2026-09-03-lizterm-v1-design.md`, section 6.5, replace the sentence "The table is cross-checked against wc3270 and Vista TN3270 defaults during planning. Not user-editable in v1." with: "Superseded on 2026-09-05: the cross-check against wc3270 and Vista TN3270 is the table in `2026-09-05-lizterm-m2-hardening-design.md` section 6.2, which is what ships (Escape is Attn, Page Up/Down are PF7/PF8, Backspace erases by default, and the PA, Clear, SysReq, Newline, and modifier-tap rows are new). Not user-editable in v1."

- [ ] **Step 3: The 3b spec's as-built section**

Append to `docs/superpowers/specs/2026-09-05-lizterm-m2-hardening-design.md`:

```markdown
## 11. Deviations from this spec (as-built)

Rulings made in planning and execution, recorded here rather than edited into the sections above:

1. `ICertificateFetcher`, `PresentedCertificate`, `CertificateReader`, and `SslStreamCertificateFetcher` live in
   `src/LizTerm.Core/Security/` (section 5.1 said the App). They are BCL-only, so the dependency rule allows it,
   and the integration project, which references only the backend, needs the fetcher for the live pinning test.
2. `_verifyOverride` (section 5.3) was removed; nothing sets it once Remember means pin.
3. The certificate fetch runs under a fresh `CancellationTokenSource(ConnectTimeout)`, not the connect's token
   (section 5.3 step 1), because that source is disposed before the prompt runs.
4. Escape in the File Transfer dialog is handled in the window's `OnKeyDown` (section 7 said `IsCancel` on the
   Close button): the Running panel has no Close button. Both routes go through `Closing` and `TryClose`.
5. `TerminalScreen.DestructiveBackspace` (the styled property) also defaults to true (section 3.2 named only the
   profile), so an unbound control behaves like a new profile.
6. The plan 3a review's "coverage gaps in tasks 1, 3, 4, 5, 6, 9, 10, 11, 12, 16" (section 8) resolved, on reading
   each task against the tests, to two additions: `StopWireLog` when no log is active, and a prompt answered after
   the window was disposed. "Cancel disables after the first click" and "`DisposeAsync` stops the log only after
   Quit" already had tests.
7. `CertificateReader` puts only the chain's self-signed members in the custom trust store and the rest in the
   extra store when deciding `Pinnable` (section 5.1 said "the presented certificates as the only trust roots"),
   because a non-root certificate in the trust store would validate any leaf trivially.
8. The live pinning test forces `VerifyCertificate: true` in its options (section 9): the lane's profile has
   verification off, and section 3.1 makes a pin inert when verification is off.
```

Then add the rulings the executor made during Tasks 1 to 14 as further numbered items (each: what the spec said, what was done, why).

- [ ] **Step 4: Commit**

```bash
git add CLAUDE.md docs/superpowers/specs
git commit -m "Record the pinning, keymap, and hardening facts in CLAUDE.md and the as-built deviations in the specs

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 16: Whole-suite verification and the live UI pass

**Files:** none changed unless a step finds a bug, in which case the fix is its own commit with a test.

- [ ] **Step 1: The suite and the warning count**

```bash
dotnet build LizTerm.slnx 2>&1 | grep -c " warning "
dotnet test LizTerm.slnx
```

Expected: `0`; every project passes; without live variables the integration project reports 5 skipped. Then the live lane:

```bash
source ~/.config/lizterm-test.env && dotnet test tests/LizTerm.Integration.Tests
```

Expected: the four gateway tests pass; the IND$FILE round trip skips unless the MVS/CE host and credentials are in the environment file too, in which case it passes.

- [ ] **Step 2: Build the app and seed a scratch profile**

The app writes real profiles to the per-OS config directory, so the pass runs with an isolated HOME. `LIZTERM_TEST_HOST` from the environment file names the gateway as `host:port`.

```bash
source ~/.config/lizterm-test.env
dotnet build src/LizTerm.App
SCRATCH=$(mktemp -d /tmp/lizterm-ui-XXXX)
mkdir -p "$SCRATCH/Library/Application Support/LizTerm/profiles"
HOST=${LIZTERM_TEST_HOST%:*}; PORT=${LIZTERM_TEST_HOST##*:}
cat > "$SCRATCH/Library/Application Support/LizTerm/profiles/gateway.json" <<EOF
{ "name": "gateway", "host": "$HOST", "port": $PORT, "useTls": true, "verifyCertificate": true, "pinnedCertificate": null,
  "model": 2, "extended": true, "codePage": "cp037", "luName": null, "destructiveBackspace": true }
EOF
echo "$SCRATCH"
```

- [ ] **Step 3: First-time pin**

```bash
HOME="$SCRATCH" LIZTERM_B3270_PATH=/opt/homebrew/bin/b3270 nohup src/LizTerm.App/bin/Debug/net10.0/LizTerm.App gateway > "$SCRATCH/app.log" 2>&1 &
echo $!
```

With the Avalonia DevTools MCP: `attach-to-app` (list, then by pid); wait for the splash to close; `tree` shows the session window and, as a second root, the "Certificate not verified" window. Check with `props` that `PresentedText` reads `Presented: SHA-256 …`, `SubjectText` names the gateway's certificate, `TrustedText` is hidden, and `RememberBox` is visible with the content "Trust this certificate for this profile". `input` Click the checkbox, then `input` Click "Connect Anyway". Expect: the dialog closes, the session shows the gateway login screen, and the status bar's TLS text shows the verified padlock (compare with `StatusFormatter.Tls`). Kill the pid. Then:

```bash
grep -c '"sha256"' "$SCRATCH/Library/Application Support/LizTerm/profiles/gateway.json"
```

Expected: `1` (the profile now carries the pin; `verifyCertificate` is still `true`).

- [ ] **Step 4: Changed certificate and the editor**

Replace the pin with a decoy so the next connect fails the pin:

```bash
openssl req -x509 -newkey rsa:2048 -nodes -keyout "$SCRATCH/decoy.key" -out "$SCRATCH/decoy.pem" -days 1 -subj "/CN=localhost" 2>/dev/null
python3 - "$SCRATCH" <<'EOF'
import json, sys, pathlib
p = pathlib.Path(sys.argv[1]) / "Library/Application Support/LizTerm/profiles/gateway.json"
d = json.loads(p.read_text())
d["pinnedCertificate"] = {"sha256": "00:11:22:33", "subject": "CN=decoy", "pem": (pathlib.Path(sys.argv[1]) / "decoy.pem").read_text()}
p.write_text(json.dumps(d, indent=2))
EOF
```

Launch as in step 3. Expect the window titled "Certificate changed" with `TrustedText` reading `Trusted: SHA-256 00:11:22:33` and `PresentedText` the gateway's real fingerprint, checkbox visible. `input` Click Cancel; the session window shows the failure in the error bar. Kill the pid.

Launch a second instance with no argument (the picker), select "gateway", open the editor (the picker's Edit button), and check the tree for `PinPanel` visible with `PinText` reading `Pinned certificate: SHA-256 00:11:22:33` and the `ForgetButton`. Click Forget: the panel hides. Click Cancel (never Save; the scratch profile is disposable, but the habit matters). Kill the pid.

- [ ] **Step 5: Keys, Escape in the dialog, and the splash**

Restore the real pin (repeat step 3's connect with the checkbox, or delete `pinnedCertificate` and accept again), connect, and turn on Help > Wire Log (`input` Click on the Help header, then the item). With the session window focused, send keys through the DevTools `input` tool: Escape, Ctrl+Escape, Page Up, and a Right Ctrl press and release alone. Then read the wire log:

```bash
grep -o '"action":"[A-Za-z]*"' "$SCRATCH/Library/Application Support/LizTerm/logs/"wire-gateway-*.log | sort | uniq -c
```

Expected among the counts: `Attn`, `Clear`, `PF` (Page Up is PF7; the wire log shows `"action":"PF","args":["7"]`), and `Enter` for the tap. If the DevTools `input` tool cannot send a modifier press and release on its own, record that the tap was not observed live (the headless test covers it) in the spec's section 11.

Open File > File Transfer... (the item is enabled while connected), then send Escape: the dialog closes. Disconnect and kill the pid.

The splash: relaunch the app and take a DevTools `screenshot` within the first second (attach immediately after `nohup`; the splash stays at least 1 s and at most 2.5 s; retry the launch if it closed first). Save the image under `.superpowers/` (gitignored) and describe what it shows in the spec's section 11: this is the first time the rendered splash has been seen.

- [ ] **Step 6: Clean up and record**

```bash
rm -rf "$SCRATCH"
git status --short
```

Expected: only the spec's section 11 edits from steps 3 to 5 (if any). Commit them:

```bash
git add docs/superpowers/specs/2026-09-05-lizterm-m2-hardening-design.md
git commit -m "Record the live UI pass for the pinning prompts, the Vista keys, and the splash

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

## Self-review notes (already applied while writing this plan)

- Spec coverage: 3.1 and 3.2 (Task 1); 4.1 (Task 4); 4.2 (Task 14); 5.1 (Tasks 2 and 3); 5.2 and 5.3 (Task 6); 5.4 (Task 7); 5.5 (Task 8); 6.1 and 6.2 (Task 9); 6.3 (Task 10); 6.4 (Task 15); 7 (Task 11); 8 (Tasks 5, 12, 13, 14); 9 (spread over the tasks named, plus Task 16); 10 is out of scope by definition.
- The one item in section 8 with no task of its own, "the plan-3a tasks the review flagged", is resolved in planning deviation 6 and Task 13.
- Type consistency checked: `CertificatePin(Sha256, Subject, Pem)`, `PresentedCertificate(Sha256, Subject, Pem, Pinnable, NotPinnableReason)`, `CertificatePromptRequest(Host, Reason, Presented, FetchError, Previous, CanPin, CannotPinReason)`, `KeyChord(Key, Modifiers, Tap)`, `Keymap.TryMap/TryText/With`, `DefaultKeymap.Create(bool)`, `ModifierTapDetector.KeyDown/KeyUp/Reset`, `CommandRouting.TryExecuteAsync/TryExecute`, `B3270Session.TlsSettings/WritePinFile/LastPinFile`, `Wait.UntilAsync` (one per test project), `EnvironmentCollection.Name`, `SessionViewModel.UniquePath`, and the fake recordings `connect:pin:<sha256>`, `fetch:<host>:<port>`, `ask:<host>:<canPin>` are used with the same names in every task.
