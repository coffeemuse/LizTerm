using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;

namespace LizTerm.Integration.Tests;

/// <summary>A TLS listener on loopback that completes one handshake and then holds the connection. It never speaks
/// TN3270: the engine reports the certificate verdict as a tls indication during the handshake, long before any
/// telnet negotiation, and that verdict is the whole point of these tests.</summary>
internal sealed class LoopbackTlsHost : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly Task _serving;

    private LoopbackTlsHost(TcpListener listener, X509Certificate2 serverCertificate, CancellationToken ct)
    {
        _listener = listener;
        _serving = Task.Run(async () =>
        {
            try
            {
                using var accepted = await _listener.AcceptTcpClientAsync(ct);
                await using var tls = new SslStream(accepted.GetStream());
                await tls.AuthenticateAsServerAsync(serverCertificate, clientCertificateRequired: false, checkCertificateRevocation: false);
                // Hold the session open until the engine hangs up, so the connection does not drop before the
                // client has reported what it made of the certificate.
                _ = await tls.ReadAsync(new byte[1], ct);
            }
            catch (Exception)
            {
                // A rejected certificate means the engine hangs up mid-handshake. That is a result, not a fault.
            }
        }, ct);
    }

    public static (LoopbackTlsHost Host, int Port) Start(X509Certificate2 serverCertificate, CancellationToken ct)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        return (new LoopbackTlsHost(listener, serverCertificate, ct), port);
    }

    public async ValueTask DisposeAsync()
    {
        _listener.Stop();
        try { await _serving; } catch (Exception) { /* stopping the listener is how this task ends */ }
    }
}
