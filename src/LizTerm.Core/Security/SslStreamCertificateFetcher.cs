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
                // Copies through the DER bytes: the objects SslStream hands out are disposed with the stream, and
                // the byte-array X509Certificate2 constructors are obsolete (SYSLIB0057).
                presented.Add(X509CertificateLoader.LoadCertificate(certificate.Export(X509ContentType.Cert)));
                if (chain is not null)
                {
                    foreach (var element in chain.ChainElements.Cast<X509ChainElement>().Skip(1))
                        presented.Add(X509CertificateLoader.LoadCertificate(element.Certificate.Export(X509ContentType.Cert)));
                }
                return true;
            });
            await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = host,
                CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
            }, bounded.Token);
            if (presented.Count == 0) throw new IOException("The host presented no certificate.");
            return CertificateReader.Read(presented);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            throw new IOException($"No TLS answer from {host}:{port} within {Timeout.TotalSeconds:0} s.");
        }
    }
}
