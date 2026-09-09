// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace LizTerm.Core.Security;

/// <summary>Turns certificates into the PEM an engine's caFile wants. Separated from the store read so the part
/// with decisions in it is testable without depending on whatever roots the machine happens to hold.</summary>
public static class TrustAnchorPem
{
    /// <summary>Roughly what one root's PEM block costs, so the ~240 KB a real store yields is not reached by
    /// growing a 16-character builder one doubling at a time.</summary>
    private const int PemBlockEstimate = 2048;

    /// <returns>The concatenated PEM, or null when <paramref name="certificates"/> yields nothing usable.</returns>
    public static string? Build(IReadOnlyCollection<X509Certificate2> certificates)
    {
        // The same root is commonly in more than one store; a duplicate anchor is harmless to OpenSSL but makes
        // the file bigger for no reason. Fingerprint is the project's one spelling of certificate identity.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var builder = new StringBuilder(certificates.Count * PemBlockEstimate);
        foreach (var certificate in certificates)
        {
            try
            {
                if (!seen.Add(CertificateReader.Fingerprint(certificate))) continue;
                builder.Append(certificate.ExportCertificatePem()).Append('\n');
            }
            catch (Exception)
            {
                // One certificate the platform will enumerate but not hash or export — a truncated or
                // unsupported-algorithm entry someone installed — costs that one anchor. Letting it out would
                // abandon every other root in the store and leave the engine with no trust at all, which is the
                // exact failure this type exists to prevent.
            }
        }
        return builder.Length == 0 ? null : builder.ToString();
    }
}
