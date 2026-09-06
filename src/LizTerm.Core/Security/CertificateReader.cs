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
