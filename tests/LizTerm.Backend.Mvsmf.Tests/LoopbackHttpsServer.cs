// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace LizTerm.Backend.Mvsmf.Tests;

/// <summary>An HTTPS server on loopback that answers a login with a cookie and every other request with one JSON
/// body.</summary>
internal sealed class LoopbackHttpsServer : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _loop;

    public LoopbackHttpsServer(X509Certificate2 certificate, string json)
    {
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _loop = Task.Run(() => ServeAsync(certificate, json));
    }

    public int Port { get; }

    private async Task ServeAsync(X509Certificate2 certificate, string json)
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_stop.Token);
            }
            catch (Exception) when (_stop.IsCancellationRequested)
            {
                return;
            }
            _ = Task.Run(() => AnswerAsync(client, certificate, json));
        }
    }

    private static async Task AnswerAsync(TcpClient client, X509Certificate2 certificate, string json)
    {
        using (client)
        {
            try
            {
                await using var tls = new SslStream(client.GetStream());
                await tls.AuthenticateAsServerAsync(certificate, clientCertificateRequired: false, checkCertificateRevocation: false);
                var seen = new List<byte>();
                var buffer = new byte[4096];
                while (seen.Count < 4 || !seen.TakeLast(4).SequenceEqual("\r\n\r\n"u8.ToArray()))
                {
                    var read = await tls.ReadAsync(buffer);
                    if (read == 0) return;
                    seen.AddRange(buffer.Take(read));
                }
                var requestLine = Encoding.ASCII.GetString(seen.ToArray()).Split("\r\n")[0];
                var isLogin = requestLine.StartsWith("POST ", StringComparison.Ordinal)
                    && requestLine.Contains("/services/authenticate", StringComparison.Ordinal);
                var body = Encoding.UTF8.GetBytes(isLogin ? """{"returnCode":0,"reasonCode":0,"message":"Success."}""" : json);
                var cookie = isLogin ? "Set-Cookie: LtpaToken2=loopback; Path=/\r\n" : "";
                var head = Encoding.ASCII.GetBytes(
                    $"HTTP/1.1 200 OK\r\n{cookie}Content-Type: application/json\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
                await tls.WriteAsync(head);
                await tls.WriteAsync(body);
                await tls.FlushAsync();
            }
            catch (Exception)
            {
                // A client that rejects the certificate hangs up mid-handshake; that is the point of some tests.
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        _listener.Stop();
        await _loop;
        _stop.Dispose();
    }
}
