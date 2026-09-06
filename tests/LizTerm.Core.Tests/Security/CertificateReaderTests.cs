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
        Assert.False(string.IsNullOrWhiteSpace(presented.NotPinnableReason));
        // The wording is the platform's: "An expired certificate was detected." on macOS, "...not within its
        // validity period..." on Windows and OpenSSL. Either names the cause.
        Assert.Matches("(?i)expired|valid", presented.NotPinnableReason);
    }

    [Fact]
    public void An_empty_chain_is_rejected() => Assert.Throws<ArgumentException>(() => CertificateReader.Read([]));
}
