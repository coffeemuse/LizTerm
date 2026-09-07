using System.Security.Cryptography.X509Certificates;
using LizTerm.Core.Security;

namespace LizTerm.Core.Tests.Security;

public class SystemTrustAnchorsTests
{
    /// <summary>Reading must never throw, and the result must be a PEM OpenSSL could load holding a meaningful
    /// number of anchors. This does not assert a precise count — that is the machine's business — but every
    /// platform LizTerm builds and tests on (macOS, the Ubuntu and Windows CI runners) ships a root store of at
    /// least a few dozen certificates, so "non-null and at least 5" is deliberately non-vacuous: a store read that
    /// silently returns nothing — the exact shape of the bug this plan exists to fix — now fails this test outright
    /// instead of the old "return if null" shape, which passed either way. No `Assert.Skip` escape for a bare
    /// container with no store at all: every environment this suite actually runs in has one, and a skip keyed on
    /// "the PEM came back null" would be indistinguishable from the very regression this test exists to catch, so
    /// it would defeat the test rather than accommodate an edge case. The engine actually verifying against these
    /// anchors is proved by the integration test, not here.</summary>
    [Fact]
    public void Reading_the_store_does_not_throw_and_yields_a_loadable_pem_with_real_anchors()
    {
        var pem = new SystemTrustAnchors().ExportPem();

        Assert.NotNull(pem);
        var parsed = new X509Certificate2Collection();
        parsed.ImportFromPem(pem);
        Assert.True(parsed.Count >= 5, $"expected at least 5 anchors from the OS store, got {parsed.Count}");
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
