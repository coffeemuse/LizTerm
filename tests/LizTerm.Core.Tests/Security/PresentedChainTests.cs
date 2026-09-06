using System.Security.Cryptography.X509Certificates;
using LizTerm.Core.Security;

namespace LizTerm.Core.Tests.Security;

/// <summary>Covers <see cref="SslStreamCertificateFetcher.SelectPresented"/>: what the fetcher pins must be limited
/// to what the host actually sent, not whatever the chain engine pulled in from the system trust store while
/// validating (final review, spec 11).</summary>
public class PresentedChainTests
{
    [Fact]
    public void A_root_the_host_did_not_send_is_left_out()
    {
        var (root, leaf) = TestCertificates.CaSigned();
        using (root)
        using (leaf)
        {
            using var chain = new X509Chain();
            chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            chain.ChainPolicy.CustomTrustStore.Add(root);
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            Assert.True(chain.Build(leaf));
            Assert.Equal(2, chain.ChainElements.Count);
            Assert.Empty(chain.ChainPolicy.ExtraStore);

            var presented = SslStreamCertificateFetcher.SelectPresented(leaf, chain);
            try
            {
                Assert.Single(presented);
                Assert.Equal(leaf.Thumbprint, presented[0].Thumbprint);
            }
            finally
            {
                foreach (var c in presented) c.Dispose();
            }
        }
    }

    [Fact]
    public void A_root_the_host_sent_is_kept()
    {
        var (root, leaf) = TestCertificates.CaSigned();
        using (root)
        using (leaf)
        {
            using var chain = new X509Chain();
            chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            chain.ChainPolicy.CustomTrustStore.Add(root);
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            chain.ChainPolicy.ExtraStore.Add(root);
            Assert.True(chain.Build(leaf));

            var presented = SslStreamCertificateFetcher.SelectPresented(leaf, chain);
            try
            {
                Assert.Equal(2, presented.Count);
                Assert.Equal(leaf.Thumbprint, presented[0].Thumbprint);
                Assert.Equal(root.Thumbprint, presented[1].Thumbprint);
            }
            finally
            {
                foreach (var c in presented) c.Dispose();
            }
        }
    }

    [Fact]
    public void Without_a_chain_only_the_leaf_is_returned()
    {
        var (root, leaf) = TestCertificates.CaSigned();
        using (root)
        using (leaf)
        {
            var presented = SslStreamCertificateFetcher.SelectPresented(leaf, null);
            try
            {
                Assert.Single(presented);
                Assert.Equal(leaf.Thumbprint, presented[0].Thumbprint);
            }
            finally
            {
                foreach (var c in presented) c.Dispose();
            }
        }
    }
}
