using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using LizTerm.Core.Security;

namespace LizTerm.Core.Tests.Security;

public class SslStreamCertificateFetcherTests
{
    private static (TcpListener Listener, int Port) Listen()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return (listener, ((IPEndPoint)listener.LocalEndpoint).Port);
    }

    [Fact]
    public async Task Reads_the_certificate_a_loopback_server_presents()
    {
        var ct = TestContext.Current.CancellationToken;
        using var serverCertificate = TestCertificates.WithUsableKey(TestCertificates.SelfSigned());
        var (listener, port) = Listen();
        var server = Task.Run(async () =>
        {
            using var accepted = await listener.AcceptTcpClientAsync(ct);
            await using var tls = new SslStream(accepted.GetStream());
            await tls.AuthenticateAsServerAsync(serverCertificate, clientCertificateRequired: false, checkCertificateRevocation: false);
            // Hold the session until the client hangs up; the fetcher closes as soon as the handshake is done.
            _ = await tls.ReadAsync(new byte[1], ct);
        }, ct);
        try
        {
            var presented = await new SslStreamCertificateFetcher().FetchAsync("localhost", port, ct);
            Assert.Equal(CertificateReader.Fingerprint(serverCertificate), presented.Sha256);
            Assert.Equal(serverCertificate.Subject, presented.Subject);
            Assert.True(presented.Pinnable, presented.NotPinnableReason);
            Assert.Contains("-----BEGIN CERTIFICATE-----", presented.Pem);
        }
        finally
        {
            listener.Stop();
            try { await server; } catch (Exception) { /* the client hung up, as it should */ }
        }
    }

    [Fact]
    public async Task A_refused_port_throws()
    {
        var (listener, port) = Listen();
        listener.Stop();
        await Assert.ThrowsAnyAsync<Exception>(() => new SslStreamCertificateFetcher().FetchAsync("localhost", port, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_host_that_never_speaks_tls_times_out_with_a_plain_message()
    {
        var (listener, port) = Listen();
        try
        {
            var fetcher = new SslStreamCertificateFetcher { Timeout = TimeSpan.FromMilliseconds(300) };
            var ex = await Assert.ThrowsAsync<IOException>(() => fetcher.FetchAsync("localhost", port, TestContext.Current.CancellationToken));
            Assert.Equal($"No TLS answer from localhost:{port} within 0 s.", ex.Message);
        }
        finally
        {
            listener.Stop();
        }
    }
}
