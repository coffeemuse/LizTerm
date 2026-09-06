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
            TlsInfo? tls;
            try
            {
                await Wait.UntilAsync(() => session.Tls?.Verified == true, "the engine to report a verified certificate", TimeSpan.FromSeconds(20));
                // Captured before the cancel below: ConnectAsync's cancellation path waits for the Disconnected
                // state before returning (spec 4.1), and that state clears Tls, so reading session.Tls after the
                // finally block would always race a value that has already gone back to null.
                tls = session.Tls;
            }
            finally
            {
                await connecting.CancelAsync();
                try { await connect; } catch (Exception) { /* cancelled on purpose */ }
            }

            Assert.True(tls!.Secure);
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
