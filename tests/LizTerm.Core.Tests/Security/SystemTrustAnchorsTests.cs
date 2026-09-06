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
