// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using LizTerm.Core.Security;
using LizTerm.Core.Session;

namespace LizTerm.Backend.Mvsmf;

/// <summary>Decides whether an https host is trusted. A pin trusts exactly its leaf certificate while that
/// certificate is in date, whatever the system store and the host name say. Unlike the 3270 pin, which is a PEM trust
/// store, a pin holding a chain does not extend trust to other leaves. Without a pin, the system's verdict stands. The
/// refused certificate is kept until <see cref="TakeRejected"/> reports it once; a service talks to one host, so one
/// slot is enough.</summary>
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
                  && InDate(presented[0])
                : errors == SslPolicyErrors.None;
            Volatile.Write(ref _lastRejected, trusted ? null : CertificateReader.Read(presented));
            return trusted;
        }
        finally
        {
            foreach (var copy in presented) copy.Dispose();
        }
    }

    private static bool InDate(X509Certificate2 leaf)
    {
        var now = DateTime.Now;
        return now >= leaf.NotBefore && now <= leaf.NotAfter;
    }
}
