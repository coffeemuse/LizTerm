# LizTerm Milestone 3, plan 3b: default trust for a bundled engine — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give the bundled b3270 the trust anchors the operating system already holds, so a correctly issued host certificate verifies instead of forcing the user to turn verification off or pin a certificate that was never suspect.

**Architecture:** A BCL-only `ITrustAnchorSource` in `LizTerm.Core.Security` exports the OS root store as a PEM. `B3270Session.ConnectAsync` writes that PEM to the same short-lived temp file the pin path already uses and names it in the `Set ... caFile ...` action it already sends. The backend defaults to a source that yields nothing, so tests stay deterministic; `SessionFactory` injects the real one.

**Tech Stack:** .NET 10, xunit.v3 (VSTest mode), Avalonia 12 (untouched here), b3270 4.5ga6.

**Spec:** `docs/superpowers/specs/2026-09-06-lizterm-m3-trust-design.md`

## Global Constraints

- `LizTerm.Core` depends only on the BCL: no Avalonia, no b3270 names. The new trust code is BCL-only and belongs there.
- `LizTerm.App` names `LizTerm.Backend.B3270` in exactly one file: `src/LizTerm.App/SessionFactory.cs`.
- Zero warnings: `dotnet build LizTerm.slnx --no-incremental 2>&1 | grep -c " warning "` must print `0` before the work is called done. An incremental build hides warnings from projects it did not recompile.
- Rows and columns are zero-based in Core and App. (Not touched here; stated because it is a project-wide rule.)
- Package versions live only in `Directory.Packages.props`; `PackageReference` entries carry no `Version`.
- Every connect sends `verifyHostCert`, `caFile` and `acceptHostname` explicitly, all three, every time — an attempt must never inherit the previous one's trust settings.
- An empty or unparsable `caFile` is fatal to the connect (`TLS: CA database load ... failed`), so "no anchors" must mean an empty `caFile` argument, never an empty file.
- OpenSSL treats every certificate in `caFile` as a trust anchor. Export roots only; never the intermediate store.
- Tests use xunit.v3 with `TestContext.Current.CancellationToken`; `--filter` takes `FullyQualifiedName~`.

---

### Task 1: The trust anchor source contract and its pure half

**Files:**
- Create: `src/LizTerm.Core/Security/ITrustAnchorSource.cs`
- Create: `src/LizTerm.Core/Security/TrustAnchorPem.cs`
- Test: `tests/LizTerm.Core.Tests/Security/TrustAnchorPemTests.cs`

**Interfaces:**
- Consumes: `CertificateReader.Fingerprint(X509Certificate2)` (existing, `src/LizTerm.Core/Security/CertificateReader.cs`); `TestCertificates.SelfSigned(...)` and `TestCertificates.CaSigned(...)` (existing, `tests/LizTerm.Core.Tests/Security/TestCertificates.cs`).
- Produces: `LizTerm.Core.Security.ITrustAnchorSource` with `string? ExportPem()`; `LizTerm.Core.Security.NoTrustAnchors` with `static readonly NoTrustAnchors Instance`; `LizTerm.Core.Security.TrustAnchorPem.Build(IEnumerable<X509Certificate2>) -> string?`.

- [ ] **Step 1: Write the failing tests**

Create `tests/LizTerm.Core.Tests/Security/TrustAnchorPemTests.cs`:

```csharp
using System.Security.Cryptography.X509Certificates;
using LizTerm.Core.Security;

namespace LizTerm.Core.Tests.Security;

public class TrustAnchorPemTests
{
    [Fact]
    public void Builds_a_pem_holding_every_certificate()
    {
        using var first = TestCertificates.SelfSigned("CN=one");
        using var second = TestCertificates.SelfSigned("CN=two");

        var pem = TrustAnchorPem.Build([first, second]);

        Assert.NotNull(pem);
        Assert.Equal(2, CertificateReader.CountCertificates(pem!));
        // Round-trips: what OpenSSL will parse is what we put in.
        var parsed = new X509Certificate2Collection();
        parsed.ImportFromPem(pem!);
        Assert.Equal(
            new[] { CertificateReader.Fingerprint(first), CertificateReader.Fingerprint(second) }.Order(),
            parsed.Select(CertificateReader.Fingerprint).Order());
    }

    [Fact]
    public void Drops_duplicates_so_one_certificate_in_two_stores_appears_once()
    {
        using var certificate = TestCertificates.SelfSigned();
        using var copy = X509CertificateLoader.LoadCertificate(certificate.RawData);

        var pem = TrustAnchorPem.Build([certificate, copy]);

        Assert.Equal(1, CertificateReader.CountCertificates(pem!));
    }

    /// <summary>Null, not "", because b3270 answers an empty caFile file with "CA database load ... failed" and
    /// never connects. Null is a value the caller cannot pass on to the engine by accident.</summary>
    [Fact]
    public void Yields_null_rather_than_an_empty_pem()
    {
        Assert.Null(TrustAnchorPem.Build([]));
    }

    [Fact]
    public void The_none_source_yields_null()
    {
        Assert.Null(NoTrustAnchors.Instance.ExportPem());
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~TrustAnchorPemTests"`
Expected: build failure — `TrustAnchorPem` and `NoTrustAnchors` do not exist.

- [ ] **Step 3: Write the interface**

Create `src/LizTerm.Core/Security/ITrustAnchorSource.cs`:

```csharp
namespace LizTerm.Core.Security;

/// <summary>Where the trust anchors an emulator engine should verify against come from. A statically linked engine
/// carries whatever OpenSSL directory was compiled into it, which is a path on the build machine and usually
/// nothing on the user's, so the anchors have to be supplied rather than assumed (spec 1).</summary>
public interface ITrustAnchorSource
{
    /// <summary>The anchors as a PEM, or null when there are none. Null rather than an empty string: an empty CA
    /// file makes b3270 fail the connect outright, so "nothing to offer" must stay distinguishable from "here is
    /// an empty list" all the way to the caller (spec 2, fact 5).</summary>
    string? ExportPem();
}

/// <summary>A source with no anchors, which leaves the engine on its own default trust. The backend's default, so
/// nothing silently depends on the machine's store unless a caller asked for it.</summary>
public sealed class NoTrustAnchors : ITrustAnchorSource
{
    public static readonly NoTrustAnchors Instance = new();
    public string? ExportPem() => null;
}
```

- [ ] **Step 4: Write the pure builder**

Create `src/LizTerm.Core/Security/TrustAnchorPem.cs`:

```csharp
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace LizTerm.Core.Security;

/// <summary>Turns certificates into the PEM an engine's caFile wants. Separated from the store read so the part
/// with decisions in it is testable without depending on whatever roots the machine happens to hold.</summary>
public static class TrustAnchorPem
{
    /// <returns>The concatenated PEM, or null when <paramref name="certificates"/> yields nothing.</returns>
    public static string? Build(IEnumerable<X509Certificate2> certificates)
    {
        // The same root is commonly in more than one store; a duplicate anchor is harmless to OpenSSL but makes
        // the file bigger for no reason. Fingerprint is the project's one spelling of certificate identity.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var builder = new StringBuilder();
        foreach (var certificate in certificates)
        {
            if (!seen.Add(CertificateReader.Fingerprint(certificate))) continue;
            builder.Append(certificate.ExportCertificatePem()).Append('\n');
        }
        return builder.Length == 0 ? null : builder.ToString();
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~TrustAnchorPemTests"`
Expected: PASS, 4 tests.

- [ ] **Step 6: Commit**

```bash
git add src/LizTerm.Core/Security/ITrustAnchorSource.cs src/LizTerm.Core/Security/TrustAnchorPem.cs tests/LizTerm.Core.Tests/Security/TrustAnchorPemTests.cs
git commit -m "Add the trust anchor source contract and its pure PEM builder

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 2: Reading the operating system's root store

**Files:**
- Create: `src/LizTerm.Core/Security/SystemTrustAnchors.cs`
- Test: `tests/LizTerm.Core.Tests/Security/SystemTrustAnchorsTests.cs`

**Interfaces:**
- Consumes: `ITrustAnchorSource`, `TrustAnchorPem.Build` (Task 1).
- Produces: `LizTerm.Core.Security.SystemTrustAnchors : ITrustAnchorSource` with `static SystemTrustAnchors Default { get; }` and a public parameterless constructor.

- [ ] **Step 1: Write the failing test**

Create `tests/LizTerm.Core.Tests/Security/SystemTrustAnchorsTests.cs`:

```csharp
using System.Security.Cryptography.X509Certificates;
using LizTerm.Core.Security;

namespace LizTerm.Core.Tests.Security;

public class SystemTrustAnchorsTests
{
    /// <summary>Deliberately asserts no count: how many roots a machine holds is the machine's business, and a
    /// container with none is a legitimate environment the caller already handles. What must hold is that reading
    /// never throws, and that whatever comes back is a PEM OpenSSL could load. The engine actually verifying
    /// against these anchors is proved by the integration test, not here.</summary>
    [Fact]
    public void Reading_the_store_does_not_throw_and_yields_a_loadable_pem_or_null()
    {
        var pem = new SystemTrustAnchors().ExportPem();

        if (pem is null) return;
        var parsed = new X509Certificate2Collection();
        parsed.ImportFromPem(pem);
        Assert.NotEmpty(parsed);
    }

    [Fact]
    public void The_export_is_cached_so_repeated_connects_do_not_re_read_the_store()
    {
        var anchors = new SystemTrustAnchors();

        Assert.Same(anchors.ExportPem(), anchors.ExportPem());
    }

    [Fact]
    public void Default_is_a_single_shared_instance()
    {
        Assert.Same(SystemTrustAnchors.Default, SystemTrustAnchors.Default);
    }
}
```

Note on `Assert.Same` in the caching test: it compares references, so it passes only if the second call returns the very same string instance. If the store legitimately holds no roots both calls return null and `Assert.Same(null, null)` passes, which is the correct outcome for that machine.

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~SystemTrustAnchorsTests"`
Expected: build failure — `SystemTrustAnchors` does not exist.

- [ ] **Step 3: Write the implementation**

Create `src/LizTerm.Core/Security/SystemTrustAnchors.cs`:

```csharp
using System.Security.Cryptography.X509Certificates;

namespace LizTerm.Core.Security;

/// <summary>The trust anchors the operating system already holds: the macOS keychain, the Windows root store, or
/// the distribution's CA bundle, whichever this machine has. Read once per instance, because the read costs a few
/// hundred milliseconds, the root set does not meaningfully change inside one session, and a restart picks up any
/// change that matters.</summary>
public sealed class SystemTrustAnchors : ITrustAnchorSource
{
    /// <summary>The instance the app uses, so one process reads the store once.</summary>
    public static SystemTrustAnchors Default { get; } = new();

    private readonly Lazy<string?> _pem = new(Read);

    public string? ExportPem() => _pem.Value;

    private static string? Read() =>
        TrustAnchorPem.Build([.. ReadStore(StoreLocation.LocalMachine), .. ReadStore(StoreLocation.CurrentUser)]);

    /// <summary>Both locations, because a root an administrator installed machine-wide and one the user added
    /// themselves are equally the answer to "what does this machine trust". A store that cannot be opened
    /// contributes nothing rather than failing the connect: having fewer anchors than hoped is recoverable and
    /// already handled, throwing here is not.</summary>
    private static List<X509Certificate2> ReadStore(StoreLocation location)
    {
        try
        {
            using var store = new X509Store(StoreName.Root, location);
            store.Open(OpenFlags.ReadOnly);
            // Materialised before the store closes.
            return [.. store.Certificates];
        }
        catch (Exception)
        {
            return [];
        }
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~SystemTrustAnchorsTests"`
Expected: PASS, 3 tests.

- [ ] **Step 5: Commit**

```bash
git add src/LizTerm.Core/Security/SystemTrustAnchors.cs tests/LizTerm.Core.Tests/Security/SystemTrustAnchorsTests.cs
git commit -m "Read the OS root store as trust anchors, once per instance

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 3: The backend rule — three ways to fill caFile

**Files:**
- Modify: `src/LizTerm.Backend.B3270/B3270Session.cs` (the `ConnectAsync` / `TlsSettings` / `WritePinFile` region, currently lines 563-628)
- Modify: `tests/LizTerm.Backend.B3270.Tests/B3270SessionConnectTests.cs` (rename `LastPinFile` at its existing call sites; add the new tests)
- Create: `tests/LizTerm.Backend.B3270.Tests/Fakes/FakeTrustAnchorSource.cs`

**Interfaces:**
- Consumes: `ITrustAnchorSource`, `NoTrustAnchors.Instance` (Task 1).
- Produces: `B3270Session.TrustAnchors { get; init; }` (type `ITrustAnchorSource`); `B3270Session.LastCaFile` (internal `string?`, replaces `LastPinFile`); `B3270Session.WriteCaFile(string pem, string kind)` (internal static, replaces `WritePinFile(string pem)`); `FakeTrustAnchorSource` with a settable `Pem` property.

- [ ] **Step 1: Write the fake**

Create `tests/LizTerm.Backend.B3270.Tests/Fakes/FakeTrustAnchorSource.cs`:

```csharp
using LizTerm.Core.Security;

namespace LizTerm.Backend.B3270.Tests.Fakes;

/// <summary>A trust source a test controls. Two PEM blocks by default, so a test can tell the roots file apart from
/// a pin file by content as well as by name.</summary>
internal sealed class FakeTrustAnchorSource : ITrustAnchorSource
{
    public const string TwoRoots =
        "-----BEGIN CERTIFICATE-----\ncm9vdDE=\n-----END CERTIFICATE-----\n" +
        "-----BEGIN CERTIFICATE-----\ncm9vdDI=\n-----END CERTIFICATE-----\n";

    public string? Pem { get; set; } = TwoRoots;
    public int Calls { get; private set; }

    public string? ExportPem()
    {
        Calls++;
        return Pem;
    }
}
```

- [ ] **Step 2: Write the failing tests**

Append to `tests/LizTerm.Backend.B3270.Tests/B3270SessionConnectTests.cs`, inside the class:

```csharp
    /// <summary>The row of spec section 3 this whole plan exists for: verifying, no pin, anchors available.</summary>
    [Fact]
    public async Task An_unpinned_verifying_connect_names_a_roots_file_holding_the_trust_anchors()
    {
        var fake = new FakeB3270Process();
        var trust = new FakeTrustAnchorSource();
        string? contentDuringConnect = null;
        var ownerOnly = true;
        await using var session = new B3270Session(Verifying, () => fake) { TrustAnchors = trust };
        fake.RunResponder = line =>
        {
            if (line.Contains("\"Connect\""))
            {
                contentDuringConnect = File.ReadAllText(session.LastCaFile!);
                if (!OperatingSystem.IsWindows())
                    ownerOnly = File.GetUnixFileMode(session.LastCaFile!) == (UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
            return [Ok(line)];
        };

        await session.ConnectAsync(cancellationToken: TestContext.Current.CancellationToken);

        var set = LastSetLine(fake);
        Assert.Contains("\"verifyHostCert\",\"true\"", set);
        Assert.Contains($"\"caFile\",{WireArg(session.LastCaFile!)}", set);
        // Not "any": these anchors sign certificates for hosts other than this one, so the engine's own name
        // check is the only thing keeping one of those from verifying here.
        Assert.Contains("\"acceptHostname\",\"\"", set);
        Assert.Equal(FakeTrustAnchorSource.TwoRoots, contentDuringConnect);
        Assert.True(ownerOnly, "the roots file is not owner-only");
        Assert.StartsWith("lizterm-roots-", Path.GetFileName(session.LastCaFile!));
        Assert.False(File.Exists(session.LastCaFile), "the roots file outlived the Connect run");
    }

    /// <summary>A pin is a deliberate answer to "trust exactly this"; adding the machine's roots beside it would
    /// widen it back out to every CA the machine trusts.</summary>
    [Fact]
    public async Task A_pin_wins_over_the_trust_anchors()
    {
        var fake = new FakeB3270Process();
        var trust = new FakeTrustAnchorSource();
        string? contentDuringConnect = null;
        await using var session = new B3270Session(Pinned, () => fake) { TrustAnchors = trust };
        fake.RunResponder = line =>
        {
            if (line.Contains("\"Connect\"")) contentDuringConnect = File.ReadAllText(session.LastCaFile!);
            return [Ok(line)];
        };

        await session.ConnectAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(Pin.Pem, contentDuringConnect);
        Assert.StartsWith("lizterm-pin-", Path.GetFileName(session.LastCaFile!));
        Assert.Contains("\"acceptHostname\",\"any\"", LastSetLine(fake));
    }

    [Fact]
    public async Task Verification_off_offers_no_trust_anchors()
    {
        var fake = new FakeB3270Process();
        var trust = new FakeTrustAnchorSource();
        await using var session = new B3270Session(Verifying, () => fake) { TrustAnchors = trust };

        await session.ConnectAsync(new ConnectOptions(VerifyCertificate: false), TestContext.Current.CancellationToken);

        Assert.Contains("\"verifyHostCert\",\"false\",\"caFile\",\"\",\"acceptHostname\",\"\"", LastSetLine(fake));
        Assert.Null(session.LastCaFile);
    }

    /// <summary>A machine whose store yields nothing must leave the engine on its own default trust. Writing the
    /// empty PEM instead would make b3270 answer "CA database load ... failed" and never connect at all — worse
    /// than the behaviour this plan set out to fix.</summary>
    [Fact]
    public async Task A_source_with_no_anchors_leaves_the_engine_on_its_own_default()
    {
        var fake = new FakeB3270Process();
        var trust = new FakeTrustAnchorSource { Pem = null };
        await using var session = new B3270Session(Verifying, () => fake) { TrustAnchors = trust };

        await session.ConnectAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Contains("\"verifyHostCert\",\"true\",\"caFile\",\"\",\"acceptHostname\",\"\"", LastSetLine(fake));
        Assert.Null(session.LastCaFile);
    }

    /// <summary>Nothing reads the machine's store unless a caller asked for it, so the backend's own tests and any
    /// embedder that has not chosen a source behave the same on every machine.</summary>
    [Fact]
    public async Task The_default_source_offers_nothing()
    {
        var fake = new FakeB3270Process();
        await using var session = new B3270Session(Verifying, () => fake);

        await session.ConnectAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Contains("\"caFile\",\"\"", LastSetLine(fake));
        Assert.Null(session.LastCaFile);
    }
```

The file already imports `LizTerm.Backend.B3270.Tests.Fakes`, where the new fake lives, so it needs no new using.

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/LizTerm.Backend.B3270.Tests --filter "FullyQualifiedName~B3270SessionConnectTests"`
Expected: build failure — `LastCaFile` and the `TrustAnchors` init property do not exist.

- [ ] **Step 4: Rename the existing members**

In `src/LizTerm.Backend.B3270/B3270Session.cs`, rename across the file:
- `LastPinFile` → `LastCaFile`
- `WritePinFile` → `WriteCaFile`
- `TryDeletePinFile` → `TryDeleteCaFile`

and in `tests/LizTerm.Backend.B3270.Tests/B3270SessionConnectTests.cs` rename `session.LastPinFile` → `session.LastCaFile` at its existing call sites. The name now describes what the file is rather than who asked for it, because two callers write one.

Update the doc comment on the seam:

```csharp
    /// <summary>The path of the last CA file written — a pin or the trust anchors — deleted or not. Test seam.</summary>
    internal string? LastCaFile { get; private set; }
```

- [ ] **Step 5: Give WriteCaFile the kind of file it is writing**

Replace the signature and the path line in `src/LizTerm.Backend.B3270/B3270Session.cs`:

```csharp
    /// <param name="kind">"pin" or "roots": the file name says which of the two callers wrote it, which is what a
    /// leftover in the temp directory and a failing test both have to be read by.</param>
    internal static string WriteCaFile(string pem, string kind)
    {
        var path = Path.Combine(Path.GetTempPath(), $"lizterm-{kind}-{Guid.NewGuid():N}.pem");
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
        // Owner-only on Unix; the Windows temp directory is already per user.
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        using var stream = new FileStream(path, options);
        using var writer = new StreamWriter(stream);
        writer.Write(pem);
        return path;
    }
```

- [ ] **Step 6: Add the init property**

In `src/LizTerm.Backend.B3270/B3270Session.cs`, beside `StartupTimeout` (currently line 56):

```csharp
    /// <summary>Where the engine's trust anchors come from when no pin is in force. Defaults to none, which leaves
    /// the engine on whatever trust it was built with; the App injects the real store in SessionFactory. Defaulting
    /// to the machine's store would make every test that connects with a verifying profile depend on the roots that
    /// machine happens to hold.</summary>
    public ITrustAnchorSource TrustAnchors { get; init; } = NoTrustAnchors.Instance;
```

Add `using LizTerm.Core.Security;` to the file's usings if it is not already there.

- [ ] **Step 7: Apply the rule in ConnectAsync**

In `src/LizTerm.Backend.B3270/B3270Session.cs`, replace the pin-file block of `ConnectAsync` (currently the `var pinFile = ...` and `LastPinFile = ...` lines) with:

```csharp
        // Spec 3: verification off means no CA file at all; otherwise a pin is the whole trust store, and without
        // one the machine's own anchors are, because a statically linked engine has none it can use (spec 1). A
        // source with nothing to offer leaves caFile empty: an empty *file* fails the connect outright.
        var pem = pin?.Pem ?? (verify ? TrustAnchors.ExportPem() : null);
        var caFile = pem is null ? null : WriteCaFile(pem, pin is null ? "roots" : "pin");
        LastCaFile = caFile;
```

Then replace the three later uses in the same method:

```csharp
        var anyName = pin is not null && CertificateReader.CountCertificates(pin.Pem) == 1;
        try
        {
            await RunAsync([TlsSettings(verify, caFile, anyName)], throwOnFailure: true, cancellationToken: cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            await ConnectCoreAsync(cancellationToken);
        }
        finally
        {
            if (caFile is not null) TryDeleteCaFile(caFile);
        }
```

Rename `TlsSettings`'s parameter from `pinFile` to `caFile` and update its doc comment's first clause to read "an empty caFile is the engine's own default trust":

```csharp
    /// <summary>The Set action carrying one attempt's trust settings (spec 4.1). <paramref name="acceptAnyName"/>
    /// turns the engine's host-name check off, which is right only for a pin that is a single self-signed
    /// certificate; an empty acceptHostname is the engine's normal check against the connect host. An empty
    /// caFile leaves the engine on its own default trust.</summary>
    internal static B3270Action TlsSettings(bool verify, string? caFile, bool acceptAnyName) =>
        new("Set", "verifyHostCert", verify ? "true" : "false", "caFile", caFile ?? "", "acceptHostname", caFile is not null && acceptAnyName ? "any" : "");
```

- [ ] **Step 8: Run the whole backend suite**

Run: `dotnet test tests/LizTerm.Backend.B3270.Tests`
Expected: PASS, including the five new tests and every pre-existing pin test under its renamed seam.

- [ ] **Step 9: Commit**

```bash
git add src/LizTerm.Backend.B3270/B3270Session.cs tests/LizTerm.Backend.B3270.Tests/B3270SessionConnectTests.cs tests/LizTerm.Backend.B3270.Tests/Fakes/FakeTrustAnchorSource.cs
git commit -m "Offer the engine trust anchors when a verifying connect has no pin

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 4: Wiring the real store into the app

**Files:**
- Modify: `src/LizTerm.App/SessionFactory.cs` (the `Create(profile, overridePath, baseDirectory)` return statement)
- Test: `tests/LizTerm.App.Tests/SessionFactoryTests.cs`

**Interfaces:**
- Consumes: `SystemTrustAnchors.Default` (Task 2); `B3270Session.TrustAnchors` (Task 3).
- Produces: nothing new; this is the production wiring that makes the feature live.

- [ ] **Step 1: Write the failing test**

Append to `tests/LizTerm.App.Tests/SessionFactoryTests.cs`, inside the class:

```csharp
    /// <summary>The backend defaults to no anchors on purpose, so the app is the thing that has to supply the real
    /// store. Without this the whole plan is inert in the shipping product while every test still passes.</summary>
    [Fact]
    public async Task The_session_it_builds_verifies_against_the_system_trust_anchors()
    {
        var profile = new SessionProfile(Name: "trust", Host: "h", UseTls: true);

        await using var session = (B3270Session)SessionFactory.Create(profile, overridePath: "/nonexistent/b3270", Path.GetTempPath());

        Assert.Same(SystemTrustAnchors.Default, session.TrustAnchors);
    }
```

Add `using LizTerm.Backend.B3270;` and `using LizTerm.Core.Security;` to the file's usings if they are not already present. The App test project references the App, which references the backend, so the concrete type is reachable; `SessionFactory.Create(profile, overridePath, baseDirectory)` is the existing internal seam, and a bogus override plus an empty base directory is how the other tests in this file keep a bundled engine out of the result. The test is `async` because `B3270Session` is `IAsyncDisposable`, not `IDisposable`, so `await using` is the only form that compiles.

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~SessionFactoryTests"`
Expected: FAIL — `TrustAnchors` is `NoTrustAnchors.Instance`, not `SystemTrustAnchors.Default`.

- [ ] **Step 3: Inject the real source**

In `src/LizTerm.App/SessionFactory.cs`, change the final return of the internal `Create` overload:

```csharp
        var wireLog = WireLog.TryFromEnvironment(out var wireLogError);
        // The backend assumes no trust anchors; the app is where the machine's own store enters, the same way it
        // supplies ICertificateFetcher rather than the backend reaching for one.
        return new B3270Session(profile, processFactory, wireLog, wireLogError, location)
        {
            TrustAnchors = SystemTrustAnchors.Default,
        };
```

Add `using LizTerm.Core.Security;` to the file's usings.

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~SessionFactoryTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/LizTerm.App/SessionFactory.cs tests/LizTerm.App.Tests/SessionFactoryTests.cs
git commit -m "Give app sessions the machine's trust anchors

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 5: The integration test — a real engine verifying a real chain

**Files:**
- Create: `tests/LizTerm.Integration.Tests/TrustAnchorVerificationTests.cs`
- Create: `tests/LizTerm.Integration.Tests/LoopbackTlsHost.cs`
- Modify: `tests/LizTerm.Core.Tests/Security/TestCertificates.cs` (add a servable-leaf overload)
- Modify: `tests/LizTerm.Integration.Tests/LizTerm.Integration.Tests.csproj` (link `TestCertificates.cs`)

**Interfaces:**
- Consumes: `B3270Locator.Find`, `B3270Locator.Candidates`, `B3270Locator.BundledDirectory`, `EngineRequirement.Decide`, `EngineRequirementOutcome` (existing, see `tests/LizTerm.Integration.Tests/EngineSmokeTests.cs`); `B3270Session.TrustAnchors` (Task 3); `ITrustAnchorSource` (Task 1); `TlsInfo.Verified` (existing, `src/LizTerm.Core/Session/TlsInfo.cs`); `ConnectionFailedException.CertificateVerificationFailed` (existing, `src/LizTerm.Core/Session/Exceptions.cs`).
- Produces: `TestCertificates.CaSignedServable()` returning `(X509Certificate2 Root, X509Certificate2 Leaf)` where the leaf carries a usable server key; `LoopbackTlsHost` with `static (LoopbackTlsHost Host, int Port) Start(X509Certificate2 serverCertificate, CancellationToken ct)` and `IAsyncDisposable`.

- [ ] **Step 1: Add the servable-leaf helper**

In `tests/LizTerm.Core.Tests/Security/TestCertificates.cs`, add:

```csharp
    /// <summary>Like <see cref="CaSigned"/>, but the leaf keeps a private key the platform TLS stack will serve
    /// with. <see cref="CaSigned"/> disposes the leaf key because its callers only ever read the certificate; a
    /// loopback server needs it back, and macOS additionally refuses an ephemeral key for
    /// AuthenticateAsServerAsync, which is what WithUsableKey is for.</summary>
    public static (X509Certificate2 Root, X509Certificate2 Leaf) CaSignedServable()
    {
        using var rootKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var rootRequest = new CertificateRequest("CN=LizTerm Test CA", rootKey, HashAlgorithmName.SHA256);
        rootRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        rootRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        rootRequest.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(rootRequest.PublicKey, false));
        var root = rootRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));

        // RSA, not ECDSA: an RSA server key works on every TLS stack .NET runs on, which is the same reason
        // SelfSigned uses one.
        using var leafKey = RSA.Create(2048);
        var leafRequest = new CertificateRequest("CN=localhost", leafKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        leafRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        leafRequest.CertificateExtensions.Add(LocalhostNames());
        var serial = new byte[8];
        RandomNumberGenerator.Fill(serial);
        serial[0] &= 0x7F;
        using var unkeyed = leafRequest.Create(root, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(10), serial);
        return (root, WithUsableKey(unkeyed.CopyWithPrivateKey(leafKey)));
    }
```

Change the class declaration from `internal static class TestCertificates` to `public static class TestCertificates` — the integration project compiles this file into a different assembly, and `internal` there would be a different accessibility domain than the Core tests' own use of it. (`InternalsVisibleTo` does not help: the file is compiled twice, not shared at runtime.)

- [ ] **Step 2: Link the file into the integration project**

In `tests/LizTerm.Integration.Tests/LizTerm.Integration.Tests.csproj`, inside the existing first `<ItemGroup>`:

```xml
    <!-- Compiled into this assembly too rather than duplicated: the certificate shapes these tests need are the
         ones the Core tests already build. -->
    <Compile Include="../LizTerm.Core.Tests/Security/TestCertificates.cs" Link="Security/TestCertificates.cs" />
```

- [ ] **Step 3: Write the loopback host**

Create `tests/LizTerm.Integration.Tests/LoopbackTlsHost.cs`:

```csharp
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;

namespace LizTerm.Integration.Tests;

/// <summary>A TLS listener on loopback that completes one handshake and then holds the connection. It never speaks
/// TN3270: the engine reports the certificate verdict as a tls indication during the handshake, long before any
/// telnet negotiation, and that verdict is the whole point of these tests.</summary>
internal sealed class LoopbackTlsHost : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly Task _serving;

    private LoopbackTlsHost(TcpListener listener, X509Certificate2 serverCertificate, CancellationToken ct)
    {
        _listener = listener;
        _serving = Task.Run(async () =>
        {
            try
            {
                using var accepted = await _listener.AcceptTcpClientAsync(ct);
                await using var tls = new SslStream(accepted.GetStream());
                await tls.AuthenticateAsServerAsync(serverCertificate, clientCertificateRequired: false, checkCertificateRevocation: false);
                // Hold the session open until the engine hangs up, so the connection does not drop before the
                // client has reported what it made of the certificate.
                _ = await tls.ReadAsync(new byte[1], ct);
            }
            catch (Exception)
            {
                // A rejected certificate means the engine hangs up mid-handshake. That is a result, not a fault.
            }
        }, ct);
    }

    public static (LoopbackTlsHost Host, int Port) Start(X509Certificate2 serverCertificate, CancellationToken ct)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        return (new LoopbackTlsHost(listener, serverCertificate, ct), port);
    }

    public async ValueTask DisposeAsync()
    {
        _listener.Stop();
        try { await _serving; } catch (Exception) { /* stopping the listener is how this task ends */ }
    }
}
```

- [ ] **Step 4: Write the failing tests**

Create `tests/LizTerm.Integration.Tests/TrustAnchorVerificationTests.cs`:

```csharp
using System.Security.Cryptography.X509Certificates;
using LizTerm.Backend.B3270;
using LizTerm.Backend.B3270.Process;
using LizTerm.Core.Security;
using LizTerm.Core.Session;
using LizTerm.Core.Tests.Security;

namespace LizTerm.Integration.Tests;

/// <summary>Proves the engine actually verifies a CA-signed chain against anchors we supply. Every other test of
/// this feature asserts on the arguments LizTerm sends; this one asserts that b3270 agrees. Until it existed, the
/// only CA path proven end to end was a depth-0 self-signed pin, which never exercises chain building — which is
/// how a shipped engine with no usable trust anchors at all went unnoticed.
///
/// Loopback, with our own CA: no SNI (x3270 4.5ga6 sends none — see the spec's section 6), no DNS, no internet.
/// Skips without a bundled engine and fails under LIZTERM_REQUIRE_ENGINE, exactly as EngineSmokeTests does.</summary>
public class TrustAnchorVerificationTests
{
    private sealed class Anchors(string? pem) : ITrustAnchorSource
    {
        public string? ExportPem() => pem;
    }

    private static B3270Location RequireEngine()
    {
        B3270Location? location = null;
        string? missing = null;
        try
        {
            location = B3270Locator.Find(overridePath: null, AppContext.BaseDirectory);
        }
        catch (BackendUnavailableException e)
        {
            missing = e.Message;
        }
        var present = B3270Locator.Candidates(overridePath: null, AppContext.BaseDirectory).Any(c => File.Exists(c.Path));
        var outcome = EngineRequirement.Decide(location is not null, present, Environment.GetEnvironmentVariable(EngineRequirement.Variable));
        if (outcome == EngineRequirementOutcome.Fail) Assert.Fail(missing ?? "no bundled engine");
        Assert.SkipWhen(outcome == EngineRequirementOutcome.Skip, missing ?? "no bundled engine");
        return location!;
    }

    private static SessionProfile Profile(int port) =>
        new(Name: "trust-anchors", Host: "localhost", Port: port, UseTls: true, VerifyCertificate: true);

    [Fact(Timeout = 60_000)]
    public async Task The_engine_verifies_a_ca_signed_host_against_the_anchors_we_supply()
    {
        var location = RequireEngine();
        var ct = TestContext.Current.CancellationToken;
        var (root, leaf) = TestCertificates.CaSignedServable();
        using (root)
        using (leaf)
        {
            var (host, port) = LoopbackTlsHost.Start(leaf, ct);
            await using var _ = host;
            await using var session = new B3270Session(Profile(port), () => new B3270ChildProcess(location.Path), location: location)
            {
                TrustAnchors = new Anchors(root.ExportCertificatePem() + "\n"),
            };

            // ConnectAsync does not return here: the handshake succeeds and the engine then waits out a telnet
            // negotiation this server never answers. The tls indication is what we came for, and it arrives
            // during the handshake.
            using var connecting = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var connect = session.ConnectAsync(cancellationToken: connecting.Token);
            try
            {
                await Wait.UntilAsync(() => session.Tls?.Verified == true, "the engine to report a verified certificate", TimeSpan.FromSeconds(20));
            }
            finally
            {
                await connecting.CancelAsync();
                try { await connect; } catch (Exception) { /* cancelled on purpose */ }
            }

            Assert.True(session.Tls!.Secure);
        }
    }

    /// <summary>The control. Without it the test above would pass just as well against an engine that verified
    /// nothing at all — which is precisely the bug this plan fixes.</summary>
    [Fact(Timeout = 60_000)]
    public async Task An_unrelated_anchor_does_not_verify_the_host()
    {
        var location = RequireEngine();
        var ct = TestContext.Current.CancellationToken;
        var (root, leaf) = TestCertificates.CaSignedServable();
        var (decoy, decoyLeaf) = TestCertificates.CaSignedServable();
        using (root)
        using (leaf)
        using (decoy)
        using (decoyLeaf)
        {
            var (host, port) = LoopbackTlsHost.Start(leaf, ct);
            await using var _ = host;
            await using var session = new B3270Session(Profile(port), () => new B3270ChildProcess(location.Path), location: location)
            {
                TrustAnchors = new Anchors(decoy.ExportCertificatePem() + "\n"),
            };

            var failure = await Assert.ThrowsAsync<ConnectionFailedException>(() => session.ConnectAsync(cancellationToken: ct));

            Assert.True(failure.CertificateVerificationFailed, string.Join(" ", failure.Lines));
        }
    }
}
```

`Wait` does not exist in this project yet — copy `tests/LizTerm.Backend.B3270.Tests/Wait.cs` to `tests/LizTerm.Integration.Tests/Wait.cs` and change its namespace to `LizTerm.Integration.Tests`. The two existing copies are a deliberate per-project helper, not shared code.

- [ ] **Step 5: Run the tests**

Run: `dotnet test tests/LizTerm.Integration.Tests --filter "FullyQualifiedName~TrustAnchorVerificationTests"`
Expected on a machine that has run `native/build/build-macos.sh`: PASS, 2 tests. On a machine with no bundled engine: both skipped with the locator's message.

Then prove the skip is not hiding a failure:

Run: `LIZTERM_REQUIRE_ENGINE=1 dotnet test tests/LizTerm.Integration.Tests --filter "FullyQualifiedName~TrustAnchorVerificationTests"`
Expected: PASS with a bundled engine; a clear failure without one.

If the first test times out waiting for `Tls?.Verified`, read the wire with `LIZTERM_WIRE_LOG=/tmp/trust.log` on the same command and look at the `tls` and `run-result` lines — the engine reports the reason verbatim.

- [ ] **Step 6: Commit**

```bash
git add tests/LizTerm.Integration.Tests/TrustAnchorVerificationTests.cs tests/LizTerm.Integration.Tests/LoopbackTlsHost.cs tests/LizTerm.Integration.Tests/Wait.cs tests/LizTerm.Integration.Tests/LizTerm.Integration.Tests.csproj tests/LizTerm.Core.Tests/Security/TestCertificates.cs
git commit -m "Prove a real engine verifies a CA-signed chain against supplied anchors

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 6: Documentation and the milestone renumbering

**Files:**
- Modify: `CLAUDE.md` (the Core model bullet on `SessionProfile.PinnedCertificate` / `LizTerm.Core.Security`; the Backend bullet beginning "Every connect sends one `Set(verifyHostCert,…)`"; the Tests section's list of fakes)
- Modify: `docs/superpowers/specs/2026-09-06-lizterm-m3-ci-design.md` (section 1's plan list)
- Modify: `docs/superpowers/specs/2026-09-06-lizterm-m3-trust-design.md` (add section 8, deviations)

**Interfaces:**
- Consumes: everything above.
- Produces: nothing code depends on.

- [ ] **Step 1: Renumber the milestone in the 3a spec**

In `docs/superpowers/specs/2026-09-06-lizterm-m3-ci-design.md` section 1, change the four bullets after 3a so the list reads:

```
- **3b:** Default trust for a bundled engine: the OS root store exported to the engine's caFile, because a
  statically linked b3270 carries a trust directory that exists only on the build machine.
- **3c:** Linux x64 and arm64 engine builds (static OpenSSL, `ldd` gate, oldest supported glibc).
- **3d:** Windows engine builds (upstream MinGW cross build from Linux, Schannel, `b3270.exe`; Windows arm64
  ships the x64 binary).
- **3e:** Publish and release (self-contained `dotnet publish` for six runtime identifiers, macOS app bundle,
  tarball and zip layouts, version stamping, tag-triggered release job). Code signing and notarization are a
  separate later task, gated on paying for the Apple Developer Program and a Windows certificate.
- **3f:** Scheduled integration lane. Rule set on 2026-09-06: full CI must not rely on anything on Robert's LAN;
  the lane spins up a dockerized TK4-/TK5 or MVS/CE inside the Action (MVS/CE lacks IND$FILE out of the box:
  `RX MVP INSTALL IND$FILE` as `IBMUSER` installs it).
```

Also drop the parenthetical "(CA file for a static OpenSSL's trust directory)" from what is now the 3c bullet — that work is this plan.

- [ ] **Step 2: Update CLAUDE.md**

In the Core model section, extend the sentence listing what `LizTerm.Core.Security` holds so it also names the new types:

```
`LizTerm.Core.Security` holds `ICertificateFetcher` and `SslStreamCertificateFetcher` (…), `CertificateReader`
(…), and `ITrustAnchorSource` with `SystemTrustAnchors` (the OS root store, both `LocalMachine` and
`CurrentUser`, read once per instance and exported as a PEM; a store that will not open contributes nothing) and
`NoTrustAnchors`, the default that leaves an engine on its own trust.
```

In the Backend section, replace the sentence beginning "Every connect sends one `Set(verifyHostCert,…)`" with:

```
Every connect sends one `Set(verifyHostCert,…,caFile,…,acceptHostname,…)` with all three explicit, so an attempt
never inherits the previous one's trust settings (verified: empty values clear them in the same engine). What
fills `caFile` is one rule: verification off means empty; a pin in force means the pin file; otherwise the trust
anchors `TrustAnchors` yields, written to `lizterm-roots-<guid>.pem`; and a source with no anchors means empty
again, because an empty *file* makes b3270 fail the connect with "CA database load … failed" rather than falling
back. `WriteCaFile(pem, kind)` writes both kinds, owner-only on Unix, deleted in the `finally` once the Connect
run has answered, because x3270 loads `caFile` in `sio_init` for each connection. `B3270Session.TrustAnchors`
defaults to `NoTrustAnchors.Instance`, so only `SessionFactory` — which injects `SystemTrustAnchors.Default` —
makes a session read the machine's store; a test that wants anchors supplies them. `LastCaFile` is the test seam
(it was `LastPinFile` until two callers shared it).
```

In the Tests section's list of fakes, add `FakeTrustAnchorSource` (`Pem`, `Calls`) to the backend fakes, and note that `TestCertificates` is `public` and compiled into the integration project by a `<Compile Include=... Link=...>` item, with `CaSignedServable()` for a leaf that can serve TLS.

Note for the executor: keep the surrounding wording; these are edits to existing sentences, not new sections.

- [ ] **Step 3: Record deviations in the spec**

Append to `docs/superpowers/specs/2026-09-06-lizterm-m3-trust-design.md`:

```markdown
## 8. Deviations from this spec (as-built)

Rulings made in planning and execution, recorded here rather than edited into the sections above.

1. (Fill in during execution, or delete this section if nothing deviated.)
```

Replace item 1 with what actually differed while the plan ran; delete the section if nothing did.

- [ ] **Step 4: Full verification**

```bash
dotnet build LizTerm.slnx --no-incremental 2>&1 | grep -c " warning "
```

Expected: `0`.

```bash
dotnet test LizTerm.slnx
```

Expected: every project passes. The live host tests skip without `LIZTERM_TEST_HOST`; the integration trust tests run if `native/out/<rid>/` exists and skip otherwise.

- [ ] **Step 5: Commit**

```bash
git add CLAUDE.md docs/superpowers/specs/2026-09-06-lizterm-m3-ci-design.md docs/superpowers/specs/2026-09-06-lizterm-m3-trust-design.md
git commit -m "Document default trust and renumber the rest of Milestone 3

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```
