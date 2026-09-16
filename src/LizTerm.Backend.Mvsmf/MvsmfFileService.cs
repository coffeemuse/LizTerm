// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Net;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using LizTerm.Core.HostFiles;

namespace LizTerm.Backend.Mvsmf;

/// <summary><see cref="IHostFileService"/> over mvsMF's z/OSMF REST subset. Stores no credentials: it asks the
/// provider before each request and once more after a 401. Every workaround for the build this was written against
/// carries an <c>mvsMF-compat</c> tag matching an entry in docs/mvsmf-compatibility.md.</summary>
public sealed class MvsmfFileService : IHostFileService
{
    internal static readonly TimeSpan DefaultIdleTimeout = TimeSpan.FromSeconds(30);
    internal static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(10);
    private const int CopyBufferSize = 81920;
    private static readonly string ProductVersion = typeof(MvsmfFileService).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    private readonly HttpClient _http;
    private readonly Uri _base;
    private readonly HostCredentialProvider _credentials;
    private readonly MvsmfCertificateCheck? _certificates;
    private readonly TimeSpan _idle;

    public MvsmfFileService(MvsmfOptions options, HostCredentialProvider credentials)
        : this(new MvsmfCertificateCheck(options.PinnedCertificate), options.BaseUrl, credentials)
    {
    }

    private MvsmfFileService(MvsmfCertificateCheck certificates, Uri baseUrl, HostCredentialProvider credentials)
        : this(CreateHandler(certificates), baseUrl, credentials, null, certificates)
    {
    }

    internal MvsmfFileService(HttpMessageHandler handler, Uri baseUrl, HostCredentialProvider credentials,
        TimeSpan? idleTimeout = null, MvsmfCertificateCheck? certificates = null)
    {
        _http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        _base = new Uri(baseUrl.AbsoluteUri.TrimEnd('/') + "/");
        _credentials = credentials;
        _idle = idleTimeout ?? DefaultIdleTimeout;
        _certificates = certificates;
    }

    internal TimeSpan HttpClientTimeout => _http.Timeout;

    internal static SocketsHttpHandler CreateHandler(MvsmfCertificateCheck certificates) => new()
    {
        ConnectTimeout = ConnectTimeout,
        // mvsMF-compat: basic-auth-every-request — this build sets no session cookie, so none is kept and every
        // request carries Basic credentials.
        UseCookies = false,
        AllowAutoRedirect = false,
        SslOptions = new SslClientAuthenticationOptions
        {
            RemoteCertificateValidationCallback = certificates.Validate,
            CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
        },
    };

    public async Task<HostServerInfo> GetServerInfoAsync(CancellationToken cancellationToken = default)
    {
        const string what = "Server information";
        using var idle = new IdleTimeout(_idle, cancellationToken);
        // mvsMF-compat: info-requires-auth — the docs say /info needs no credentials; this build demands them, so it
        // goes through the same authenticated path as everything else.
        using var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, Url("info")), what, idle, cancellationToken);
        var info = await ReadJsonAsync(response, MvsmfJsonContext.Default.MvsmfInfo, what, idle, cancellationToken);
        return new HostServerInfo("mvsMF", info.ZosmfVersion ?? "unknown", info.ZosVersion ?? "unknown");
    }

    public Task<IReadOnlyList<HostFileEntry>> ListDatasetsAsync(string pattern, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException("Task 7");

    public Task<IReadOnlyList<HostFileEntry>> ListMembersAsync(HostPath dataset, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException("Task 7");

    public Task<IReadOnlyList<string>> ReadTextAsync(HostPath path, IProgress<long>? progress = null, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException("Task 8");

    public Task<long> ReadBinaryAsync(HostPath path, Stream destination, IProgress<long>? progress = null, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException("Task 8");

    public Task WriteTextAsync(HostPath path, IReadOnlyList<string> lines, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException("Task 9");

    public Task WriteBinaryAsync(HostPath path, Stream source, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException("Task 9");

    public Task DeleteAsync(HostPath path, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException("Task 9");

    public void Dispose() => _http.Dispose();

    private Uri Url(string relative) => new(_base, relative);

    /// <summary><c>restfiles/ds/DSN</c> or <c>restfiles/ds/DSN(MEMBER)</c>.</summary>
    private static string DatasetPath(HostPath path) => path.Member is null
        ? $"restfiles/ds/{EscapeName(path.Dataset)}"
        : $"restfiles/ds/{EscapeName(path.Dataset)}({EscapeName(path.Member)})";

    /// <summary>Validated names hold only A-Z 0-9 . - # $ @ and, in filters, * and %. Of those only # and % mean
    /// something in a URL; the rest go as they are, exactly as curl sends them.</summary>
    internal static string EscapeName(string name) => name.Replace("%", "%25").Replace("#", "%23");

    private async Task<HttpResponseMessage> SendAsync(Func<HttpRequestMessage> build, string what, IdleTimeout idle, CancellationToken cancellationToken)
    {
        var credentials = await AskAsync(isRetry: false, cancellationToken);
        var response = await SendOnceAsync(build, credentials, what, idle, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            response.Dispose();
            credentials = await AskAsync(isRetry: true, cancellationToken);
            response = await SendOnceAsync(build, credentials, what, idle, cancellationToken);
        }
        if (response.IsSuccessStatusCode) return response;
        using (response)
        {
            using var body = new MemoryStream();
            await CopyBodyAsync(response, body, idle, null, what, cancellationToken);
            throw MvsmfErrors.FromResponse(response.StatusCode, body.ToArray(), what);
        }
    }

    private async Task<HostCredentials> AskAsync(bool isRetry, CancellationToken cancellationToken) =>
        await _credentials(new HostCredentialRequest(isRetry), cancellationToken)
        ?? throw new HostFileException(HostFileErrorKind.Unauthenticated, "Sign-in was cancelled.");

    private async Task<HttpResponseMessage> SendOnceAsync(Func<HttpRequestMessage> build, HostCredentials credentials,
        string what, IdleTimeout idle, CancellationToken cancellationToken)
    {
        using var request = build();
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.Latin1.GetBytes($"{credentials.Userid}:{credentials.Password}")));
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("LizTerm", ProductVersion));
        try
        {
            idle.Reset();
            return await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, idle.Token);
        }
        catch (OperationCanceledException) when (idle.Expired(cancellationToken))
        {
            throw TimedOut(what);
        }
        catch (HttpRequestException ex)
        {
            throw Unreachable(what, ex);
        }
    }

    /// <summary>Copies the body, restarting the idle clock on every chunk.</summary>
    private async Task<long> CopyBodyAsync(HttpResponseMessage response, Stream destination, IdleTimeout idle,
        IProgress<long>? progress, string what, CancellationToken cancellationToken)
    {
        var buffer = new byte[CopyBufferSize];
        long total = 0;
        try
        {
            await using var body = await response.Content.ReadAsStreamAsync(idle.Token);
            while (true)
            {
                idle.Reset();
                int read;
                try
                {
                    read = await body.ReadAsync(buffer, idle.Token);
                }
                catch (Exception ex) when (ex is HttpRequestException or IOException)
                {
                    // A host I/O error mid-stream drops the connection rather than ending the body cleanly.
                    throw new HostFileException(HostFileErrorKind.Unreachable, $"{what}: the connection dropped during the transfer.", inner: ex);
                }
                if (read == 0) return total;
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                total += read;
                progress?.Report(total);
            }
        }
        catch (OperationCanceledException) when (idle.Expired(cancellationToken))
        {
            throw TimedOut(what);
        }
    }

    private async Task<T> ReadJsonAsync<T>(HttpResponseMessage response, JsonTypeInfo<T> type, string what,
        IdleTimeout idle, CancellationToken cancellationToken)
    {
        using var body = new MemoryStream();
        await CopyBodyAsync(response, body, idle, null, what, cancellationToken);
        try
        {
            return JsonSerializer.Deserialize(body.ToArray(), type)
                ?? throw new JsonException("empty");
        }
        catch (JsonException ex)
        {
            throw new HostFileException(HostFileErrorKind.ServerError, $"{what}: the host's answer could not be read.", inner: ex);
        }
    }

    private HostFileException Unreachable(string what, HttpRequestException ex)
    {
        if (IsCertificateFailure(ex) && _certificates?.LastRejected is { } presented)
        {
            return new HostFileException(HostFileErrorKind.CertificateRejected,
                $"{what}: the host's certificate is not trusted.", certificate: presented, inner: ex);
        }
        return new HostFileException(HostFileErrorKind.Unreachable, $"{what}: cannot reach the host ({ex.Message}).", inner: ex);
    }

    private static bool IsCertificateFailure(Exception ex)
    {
        for (Exception? current = ex; current is not null; current = current.InnerException)
        {
            if (current is AuthenticationException) return true;
        }
        return false;
    }

    private static HostFileException TimedOut(string what) =>
        new(HostFileErrorKind.Unreachable, $"{what}: the host stopped answering (no data for {DefaultIdleTimeout.TotalSeconds:0} s).");
}
