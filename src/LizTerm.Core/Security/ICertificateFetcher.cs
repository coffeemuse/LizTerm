namespace LizTerm.Core.Security;

/// <summary>Reads what a TLS host presents without trusting it (spec 5.1). The app passes
/// <see cref="SslStreamCertificateFetcher"/>; tests pass a fake.</summary>
public interface ICertificateFetcher
{
    /// <summary>Performs one TLS handshake to read the host's certificate chain, then closes the socket. Throws on
    /// any failure (refused, timed out, not TLS); callers treat every exception the same way.</summary>
    Task<PresentedCertificate> FetchAsync(string host, int port, CancellationToken token);
}
