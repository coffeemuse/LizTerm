// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;

namespace LizTerm.Core.Security;

/// <summary>One TLS-on-connect handshake whose validation callback captures the chain and accepts it, so the
/// handshake completes whatever the host presented; nothing is sent afterwards. Only TLS-on-connect hosts (a
/// profile with TLS on) are read this way; a STARTTLS upgrade on a plain profile is not attempted (spec 5.1).</summary>
public sealed class SslStreamCertificateFetcher : ICertificateFetcher
{
    /// <summary>Bound on the whole read: connect plus handshake.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(10);

    public async Task<PresentedCertificate> FetchAsync(string host, int port, CancellationToken token)
    {
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(token);
        bounded.CancelAfter(Timeout);
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(host, port, bounded.Token);
            var presented = new List<X509Certificate2>();
            await using var tls = new SslStream(client.GetStream(), leaveInnerStreamOpen: false, (_, certificate, chain, _) =>
            {
                if (certificate is null) return false;
                presented.AddRange(SelectPresented(certificate, chain));
                return true;
            });
            await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = host,
                CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
            }, bounded.Token);
            if (presented.Count == 0) throw new IOException("The host presented no certificate.");
            try
            {
                return CertificateReader.Read(presented);
            }
            finally
            {
                foreach (var c in presented) c.Dispose();
            }
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            throw new IOException($"No TLS answer from {host}:{port} within {Timeout.TotalSeconds:0} s.");
        }
    }

    /// <summary>The certificates the host actually sent: the leaf, plus the chain elements that came from the
    /// handshake, which SslStream places in the chain policy's ExtraStore before building. A root the chain engine
    /// pulled from the system store was never on the wire and must not be pinned: with acceptHostname any, pinning
    /// a public CA's root would trust every certificate that CA issued, for any name (final review, spec 11).
    /// Copies go through the DER bytes; the callback's objects die with the stream.</summary>
    internal static List<X509Certificate2> SelectPresented(X509Certificate certificate, X509Chain? chain)
    {
        var presented = new List<X509Certificate2> { X509CertificateLoader.LoadCertificate(certificate.Export(X509ContentType.Cert)) };
        if (chain is null) return presented;
        var sent = new HashSet<string>(chain.ChainPolicy.ExtraStore.Cast<X509Certificate2>().Select(c => c.Thumbprint), StringComparer.OrdinalIgnoreCase);
        foreach (var element in chain.ChainElements.Cast<X509ChainElement>().Skip(1))
        {
            if (sent.Contains(element.Certificate.Thumbprint))
                presented.Add(X509CertificateLoader.LoadCertificate(element.Certificate.Export(X509ContentType.Cert)));
        }
        return presented;
    }
}
