// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Globalization;
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

    public async Task<IReadOnlyList<HostFileEntry>> ListDatasetsAsync(string pattern, CancellationToken cancellationToken = default)
    {
        if (HostPath.DatasetPatternError(pattern) is { } error) throw new HostFileException(HostFileErrorKind.InvalidRequest, error);
        const string what = "Dataset list";
        var filter = pattern.Trim().ToUpperInvariant();
        using var idle = new IdleTimeout(_idle, cancellationToken);
        // mvsMF-compat: dataset-list-ignores-start — this build ignores start, so a list cannot be paged; the whole
        // list is asked for, with no X-IBM-Max-Items.
        using var response = await SendAsync(
            () => new HttpRequestMessage(HttpMethod.Get, Url($"restfiles/ds?dslevel={EscapeName(filter)}")), what, idle, cancellationToken);
        var list = await ReadJsonAsync(response, MvsmfJsonContext.Default.MvsmfDatasetList, what, idle, cancellationToken);
        // mvsMF-compat: dataset-list-morerows-false — moreRows arrives as false rather than absent; with no item limit
        // it is never true, so it is not read.
        return [.. (list.Items ?? Enumerable.Empty<MvsmfDataset>()).Where(d => !string.IsNullOrWhiteSpace(d.Dsname)).Select(ToEntry)];
    }

    public async Task<IReadOnlyList<HostFileEntry>> ListMembersAsync(HostPath dataset, CancellationToken cancellationToken = default)
    {
        if (dataset.Kind != HostPathKind.Dataset) throw new ArgumentException("Only a dataset has members.", nameof(dataset));
        var what = dataset.ToString();
        using var idle = new IdleTimeout(_idle, cancellationToken);
        // mvsMF-compat: member-list-ignores-max-items — the host returns every member whatever limit is asked, so
        // none is sent.
        using var response = await SendAsync(
            () => new HttpRequestMessage(HttpMethod.Get, Url(DatasetPath(dataset) + "/member")), what, idle, cancellationToken);
        var list = await ReadJsonAsync(response, MvsmfJsonContext.Default.MvsmfMemberList, what, idle, cancellationToken);
        // mvsMF-compat: member-list-empty-for-missing-dataset — a missing or sequential dataset answers 200 with no
        // items, so an empty list is passed on as it is; IHostFileService tells callers to confirm the dataset.
        return [.. (list.Items ?? Enumerable.Empty<MvsmfMember>())
            .Where(m => !string.IsNullOrWhiteSpace(m.Member))
            .Select(m => new HostFileEntry(m.Member!.Trim(), HostFileEntryKind.Member))];
    }

    private static HostFileEntry ToEntry(MvsmfDataset dataset) => new(
        dataset.Dsname!.Trim(),
        HostFileEntryKind.Dataset,
        new DatasetAttributes(Blank(dataset.Dsorg), Blank(dataset.Recfm), Number(dataset.Lrecl), Number(dataset.Blksz), Blank(dataset.Vol)));

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static int? Number(string? value) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? number : null;

    public async Task<IReadOnlyList<string>> ReadTextAsync(HostPath path, IProgress<long>? progress = null, CancellationToken cancellationToken = default)
    {
        var what = path.ToString();
        using var idle = new IdleTimeout(_idle, cancellationToken);
        using var response = await SendAsync(() => Get(path, "text"), what, idle, cancellationToken);
        using var body = new MemoryStream();
        await CopyBodyAsync(response, body, idle, progress, what, cancellationToken);
        // mvsMF-compat: text-read-keeps-trailing-blanks — fixed records arrive padded; HostFileTransfer trims them.
        return SplitRecords(body.GetBuffer().AsSpan(0, (int)body.Length));
    }

    public async Task<long> ReadBinaryAsync(HostPath path, Stream destination, IProgress<long>? progress = null, CancellationToken cancellationToken = default)
    {
        var what = path.ToString();
        using var idle = new IdleTimeout(_idle, cancellationToken);
        using var response = await SendAsync(() => Get(path, "binary"), what, idle, cancellationToken);
        return await CopyBodyAsync(response, destination, idle, progress, what, cancellationToken);
    }

    private HttpRequestMessage Get(HostPath path, string dataType)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, Url(DatasetPath(path)));
        request.Headers.Add("X-IBM-Data-Type", dataType);
        return request;
    }

    /// <summary>One string per record: records end in LF, and a CR is data.</summary>
    internal static List<string> SplitRecords(ReadOnlySpan<byte> body)
    {
        // mvsMF-compat: text-body-is-latin1 — the body is ISO-8859-1 whatever the headers say.
        var lines = Encoding.Latin1.GetString(body).Split('\n').ToList();
        if (lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
        return lines;
    }

    public Task WriteTextAsync(HostPath path, IReadOnlyList<string> lines, CancellationToken cancellationToken = default) =>
        PutAsync(path, EncodeText(lines), "text", "text/plain", cancellationToken);

    public async Task WriteBinaryAsync(HostPath path, Stream source, CancellationToken cancellationToken = default)
    {
        // Held in memory so the repeat after a 401 can send the same bytes.
        using var copy = new MemoryStream();
        await source.CopyToAsync(copy, cancellationToken);
        await PutAsync(path, copy.ToArray(), "binary", "application/octet-stream", cancellationToken);
    }

    public async Task DeleteAsync(HostPath path, CancellationToken cancellationToken = default)
    {
        if (path.Kind != HostPathKind.Member) throw new NotSupportedException("Deleting a whole dataset is not supported in this release.");
        var what = path.ToString();
        using var idle = new IdleTimeout(_idle, cancellationToken);
        using var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Delete, Url(DatasetPath(path))), what, idle, cancellationToken);
    }

    private async Task PutAsync(HostPath path, byte[] body, string dataType, string contentType, CancellationToken cancellationToken)
    {
        var what = path.ToString();
        using var idle = new IdleTimeout(_idle, cancellationToken);
        using var response = await SendAsync(() =>
        {
            var request = new HttpRequestMessage(HttpMethod.Put, Url(DatasetPath(path)))
            {
                // mvsMF-compat: put-json-is-rename — Content-Type application/json turns a PUT into a rename, so a
                // write only ever sends text/plain or application/octet-stream.
                Content = new ByteArrayContent(body) { Headers = { ContentType = new MediaTypeHeaderValue(contentType) } },
            };
            request.Headers.Add("X-IBM-Data-Type", dataType);
            return request;
        }, what, idle, cancellationToken);
    }

    /// <summary>The wire form of text: each line, then LF, in ISO-8859-1.</summary>
    /// <exception cref="ArgumentException">A line holds a line break or a character outside Latin-1.</exception>
    internal static byte[] EncodeText(IReadOnlyList<string> lines)
    {
        var text = new StringBuilder();
        foreach (var line in lines)
        {
            if (line.AsSpan().IndexOfAny('\r', '\n') >= 0)
                throw new ArgumentException("A line cannot hold a line break.", nameof(lines));
            if (line.AsSpan().IndexOfAnyExceptInRange('\0', 'ÿ') >= 0)
                throw new ArgumentException("A line holds a character outside Latin-1; check the text with TextUploadCheck first.", nameof(lines));
            // mvsMF-compat: text-write-drops-empty-lines — the host drops an empty line but stores a single blank as
            // a blank record.
            text.Append(line.Length == 0 ? " " : line).Append('\n');
        }
        // mvsMF-compat: text-body-is-latin1 — the host reads the body as ISO-8859-1 whatever charset says.
        // mvsMF-compat: text-write-truncates-silently — an over-long line is cut to the record length and still
        // answered 204; TextUploadCheck refuses such lines before they reach this method.
        return Encoding.Latin1.GetBytes(text.ToString());
    }

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
        idle.Pause();
        var credentials = await AskAsync(isRetry: false, cancellationToken);
        var response = await SendOnceAsync(build, credentials, what, idle, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            response.Dispose();
            idle.Pause();
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
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested && ex.InnerException is TimeoutException)
        {
            throw new HostFileException(HostFileErrorKind.Unreachable,
                $"{what}: cannot reach the host (no answer within {ConnectTimeout.TotalSeconds:0} s).", inner: ex);
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
        if (IsCertificateFailure(ex) && _certificates?.TakeRejected() is { } presented)
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
