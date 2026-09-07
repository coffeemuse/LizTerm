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

    /// <summary>A private CA and a leaf it signed. The leaf carries no private key; with
    /// <paramref name="caIssuersUrl"/> it also says where its issuer could be fetched from.</summary>
    public static (X509Certificate2 Root, X509Certificate2 Leaf) CaSigned(string? caIssuersUrl = null)
    {
        var (root, rootKey) = CreateRoot("CN=LizTerm Test CA");
        using var _ = rootKey;

        using var leafKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var leafRequest = new CertificateRequest("CN=localhost", leafKey, HashAlgorithmName.SHA256);
        leafRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        leafRequest.CertificateExtensions.Add(LocalhostNames());
        if (caIssuersUrl is not null)
            leafRequest.CertificateExtensions.Add(new X509AuthorityInformationAccessExtension(ocspUris: null, caIssuersUris: [caIssuersUrl]));
        var leaf = leafRequest.Create(root, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(10), Serial());
        return (root, leaf);
    }

    /// <summary>Just the CA, for a test that needs an anchor and no leaf to go with it — building one it will
    /// never read costs an RSA key pair per run.</summary>
    public static X509Certificate2 Ca(string subject = "CN=LizTerm Test CA")
    {
        var (root, key) = CreateRoot(subject);
        key.Dispose();
        return root;
    }

    /// <summary>The private CA every helper here issues from, so a change to its constraints, key usage, or
    /// validity is made once. The caller owns the returned key and disposes it.</summary>
    private static (X509Certificate2 Root, ECDsa Key) CreateRoot(string subject)
    {
        var rootKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var rootRequest = new CertificateRequest(subject, rootKey, HashAlgorithmName.SHA256);
        rootRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        rootRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        rootRequest.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(rootRequest.PublicKey, false));
        return (rootRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30)), rootKey);
    }

    /// <summary>A positive serial number, which is what a certificate is required to carry.</summary>
    private static byte[] Serial()
    {
        var serial = new byte[8];
        RandomNumberGenerator.Fill(serial);
        serial[0] &= 0x7F;
        return serial;
    }

    /// <summary>Re-imports through PKCS#12 so the private key is one the platform TLS stack can use as a server key:
    /// macOS refuses an ephemeral key for SslStream.AuthenticateAsServerAsync.</summary>
    public static X509Certificate2 WithUsableKey(X509Certificate2 certificate) =>
        X509CertificateLoader.LoadPkcs12(certificate.Export(X509ContentType.Pfx), null, X509KeyStorageFlags.DefaultKeySet);

    /// <summary>Like <see cref="CaSigned"/>, but the leaf keeps a private key the platform TLS stack will serve
    /// with. <see cref="CaSigned"/> disposes the leaf key because its callers only ever read the certificate; a
    /// loopback server needs it back, and macOS additionally refuses an ephemeral key for
    /// AuthenticateAsServerAsync, which is what WithUsableKey is for.</summary>
    /// <param name="subject">The CA's subject. A test that wants a CA genuinely unrelated to another one has to
    /// name it differently: OpenSSL looks an issuer up by subject name, so two CAs sharing a name exercise the
    /// wrong-signature path rather than the no-issuer-found one.</param>
    public static (X509Certificate2 Root, X509Certificate2 Leaf) CaSignedServable(string subject = "CN=LizTerm Test CA")
    {
        var (root, rootKey) = CreateRoot(subject);
        using var _ = rootKey;

        // RSA, not ECDSA: an RSA server key works on every TLS stack .NET runs on, which is the same reason
        // SelfSigned uses one.
        using var leafKey = RSA.Create(2048);
        var leafRequest = new CertificateRequest("CN=localhost", leafKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        leafRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        leafRequest.CertificateExtensions.Add(LocalhostNames());
        // The RSA leaf and ECDSA root are different key algorithms, so the Create(X509Certificate2, ...)
        // convenience overload (which CaSigned uses, ECDSA-on-ECDSA) cannot infer a signer from the issuer
        // certificate alone and throws; an explicit generator for the root's key is required instead.
        using var unkeyed = leafRequest.Create(root.SubjectName, X509SignatureGenerator.CreateForECDsa(rootKey),
            DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(10), Serial());
        using var withKey = unkeyed.CopyWithPrivateKey(leafKey);
        return (root, WithUsableKey(withKey));
    }

    private static X509Extension LocalhostNames()
    {
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName("localhost");
        names.AddIpAddress(IPAddress.Loopback);
        return names.Build();
    }
}
