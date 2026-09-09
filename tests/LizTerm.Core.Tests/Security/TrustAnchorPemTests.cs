// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

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
