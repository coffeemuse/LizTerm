// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using LizTerm.Core.Security;
using LizTerm.Core.Session;

namespace LizTerm.Backend.Mvsmf;

/// <summary>Decides whether an https host is trusted. With a pin, the leaf's SHA-256 must match it and nothing else
/// is consulted — the same trust a pinned 3270 connection gives, which also accepts any host name. Without one, the
/// system's verdict stands. The last certificate refused is kept for the error the service raises; a service talks
/// to one host, so one slot is enough.</summary>
internal sealed class MvsmfCertificateCheck(CertificatePin? pin)
{
    private PresentedCertificate? _lastRejected;

    public CertificatePin? Pin { get; } = pin;

    /// <summary>The certificate refused since the last call, if any; each refusal is reported once.</summary>
    public PresentedCertificate? TakeRejected() => Interlocked.Exchange(ref _lastRejected, null);

    public bool Validate(object sender, X509Certificate? certificate, X509Chain? chain, SslPolicyErrors errors)
    {
        if (certificate is null) return false;
        var presented = SslStreamCertificateFetcher.SelectPresented(certificate, chain);
        try
        {
            var trusted = Pin is not null
                ? CertificateReader.SameFingerprint(CertificateReader.Fingerprint(presented[0]), Pin.Sha256)
                : errors == SslPolicyErrors.None;
            Volatile.Write(ref _lastRejected, trusted ? null : CertificateReader.Read(presented));
            return trusted;
        }
        finally
        {
            foreach (var copy in presented) copy.Dispose();
        }
    }
}
