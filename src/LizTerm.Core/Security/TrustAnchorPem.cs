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
