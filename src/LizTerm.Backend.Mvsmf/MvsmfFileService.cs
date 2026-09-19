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
/// <see cref="HostTokenProvider"/> for a session token before each request and once more, naming the refused token,
/// after a 401. The password exists only inside <see cref="SignInAsync"/>. Every workaround for the build this was
/// written against carries an <c>mvsMF-compat</c> tag matching an entry in docs/mvsmf-compatibility.md.</summary>
public sealed class MvsmfFileService : IHostFileService
{
    internal static readonly TimeSpan DefaultIdleTimeout = TimeSpan.FromSeconds(30);
    internal static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(10);
    private const int CopyBufferSize = 81920;
    private const string SessionCookie = "LtpaToken2";
    private const string ReturnEtagHeader = "X-IBM-Return-Etag";
    private const string JsonContentType = "application/json";
    private static readonly string ProductVersion = typeof(MvsmfFileService).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    private readonly HttpClient _http;
    private readonly Uri _base;
    private readonly HostTokenProvider _tokens;
    private readonly MvsmfCertificateCheck? _certificates;
    private readonly TimeSpan _idle;
    private readonly SemaphoreSlim _trustGate = new(1, 1);
    private volatile bool _trustChecked;

    public MvsmfFileService(MvsmfOptions options, HostTokenProvider tokens)
        : this(new MvsmfCertificateCheck(options.PinnedCertificate), options.BaseUrl, tokens)
    {
    }

    private MvsmfFileService(MvsmfCertificateCheck certificates, Uri baseUrl, HostTokenProvider tokens)
        : this(CreateHandler(certificates), baseUrl, tokens, null, certificates)
    {
    }

    internal MvsmfFileService(HttpMessageHandler handler, Uri baseUrl, HostTokenProvider tokens,
        TimeSpan? idleTimeout = null, MvsmfCertificateCheck? certificates = null)
    {
        _http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        _base = new Uri(baseUrl.AbsoluteUri.TrimEnd('/') + "/");
        _tokens = tokens;
        _idle = idleTimeout ?? DefaultIdleTimeout;
        _certificates = certificates;
    }

    internal TimeSpan HttpClientTimeout => _http.Timeout;

    internal static SocketsHttpHandler CreateHandler(MvsmfCertificateCheck certificates) => new()
    {
        ConnectTimeout = ConnectTimeout,
        // The session token is sent by hand as a Cookie header (real z/OSMF accepts only the cookie, not Bearer),
        // so no CookieContainer holds or drops it.
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
        // mvsMF-compat: info-requires-auth — the docs say /info needs no credentials; the host authenticates it like
        // every other route by design (mvsMF #324), so it goes through the same token-authenticated path as
        // everything else. ProbeAsync (the Test button) sends an anonymous GET first and reads its 401 instead.
        using var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, Url("info")), what, idle, cancellationToken);
        var info = await ReadJsonAsync(response, MvsmfJsonContext.Default.MvsmfInfo, what, idle, cancellationToken);
        return ToServerInfo(InfoFields(info));
    }

    /// <summary>The product version and the system /info names, null for one it leaves blank. The one reading of
    /// those fields, for the token path and the anonymous probe alike.</summary>
    private static (string? Version, string? System) InfoFields(MvsmfInfo info)
    {
        // mvsMF-compat: info-version-fields — 1.0.0-dev put the whole version in both fields; 1.1.0 puts the major in
        // zosmf_version and the release in zosmf_full_version, as z/OSMF does, so the full one is read first.
        return (Blank(info.ZosmfFullVersion) ?? Blank(info.ZosmfVersion), Blank(info.ZosVersion));
    }

    private static HostServerInfo ToServerInfo((string? Version, string? System) fields) =>
        new("mvsMF", fields.Version ?? "unknown", fields.System ?? "unknown");

    public async Task<HostFileListing> ListDatasetsAsync(string pattern, HostListRequest request, CancellationToken cancellationToken = default)
    {
        if (HostPath.DatasetPatternError(pattern) is { } error) throw new HostFileException(HostFileErrorKind.InvalidRequest, error);
        const string what = "Dataset list";
        var filter = pattern.Trim().ToUpperInvariant();
        using var idle = new IdleTimeout(_idle, cancellationToken);
        // mvsMF-compat: attributes-header-ignored — mvsMF answers with the base attributes whether or not they are
        // asked for; z/OSMF answers names only without X-IBM-Attributes: base, so it is always sent.
        using var response = await SendAsync(
            () => ListRequest($"restfiles/ds?dslevel={EscapeName(filter)}{Start(request, "&")}", request, attributes: true),
            what, idle, cancellationToken);
        var list = await ReadJsonAsync(response, MvsmfJsonContext.Default.MvsmfDatasetList, what, idle, cancellationToken);
        return Page((list.Items ?? []).Where(d => !string.IsNullOrWhiteSpace(d.Dsname)).Select(ToEntry), list.MoreRows, request, what);
    }

    public async Task<HostFileListing> ListMembersAsync(HostPath dataset, HostListRequest request, CancellationToken cancellationToken = default)
    {
        if (dataset.Kind != HostPathKind.Dataset) throw new ArgumentException("Only a dataset has members.", nameof(dataset));
        if (request.NamePattern is { } namePattern && HostPath.MemberPatternError(namePattern) is { } error)
            throw new HostFileException(HostFileErrorKind.InvalidRequest, error);
        var what = dataset.ToString();
        using var idle = new IdleTimeout(_idle, cancellationToken);
        var query = request.NamePattern is { } given ? $"?pattern={EscapeQueryValue(given.Trim().ToUpperInvariant())}" : "";
        query += Start(request, query.Length == 0 ? "?" : "&");
        using var response = await SendAsync(
            () => ListRequest(DatasetPath(dataset) + "/member" + query, request, attributes: false), what, idle, cancellationToken);
        var list = await ReadJsonAsync(response, MvsmfJsonContext.Default.MvsmfMemberList, what, idle, cancellationToken);
        return Page((list.Items ?? [])
            .Where(m => !string.IsNullOrWhiteSpace(m.Member))
            .Select(m => new HostFileEntry(m.Member!.Trim(), HostFileEntryKind.Member)), list.MoreRows, request, what);
    }

    /// <summary>A continued page names the entry it follows. <c>start=</c> is inclusive on mvsMF and z/OSMF alike,
    /// so the page asks for one item more than its size and <see cref="Page"/> drops the repeat.</summary>
    // mvsMF-compat: paging — start= is inclusive, so ListRequest asks for one more than the page and Page drops the
    // repeat, or cuts the page to size when that name is gone.
    private static string Start(HostListRequest request, string separator) =>
        request.Continuation is { } after ? $"{separator}start={EscapeQueryValue(after)}" : "";

    /// <summary>A query value the host must read back exactly: a pattern, or a continuation that is whatever name
    /// the host last answered, checked by nobody. Everything a query cannot carry raw is percent-encoded; <c>*</c>
    /// stays, since it is the pattern wildcard and the host takes it as it is.</summary>
    private static string EscapeQueryValue(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var b in Encoding.UTF8.GetBytes(value))
        {
            var c = (char)b;
            if (char.IsAsciiLetterOrDigit(c) || c is '-' or '.' or '_' or '~' or '*' or '$' or '@') builder.Append(c);
            else builder.Append('%').Append(b.ToString("X2", CultureInfo.InvariantCulture));
        }
        return builder.ToString();
    }

    private HttpRequestMessage ListRequest(string relative, HostListRequest request, bool attributes)
    {
        var message = new HttpRequestMessage(HttpMethod.Get, Url(relative));
        if (attributes) message.Headers.Add("X-IBM-Attributes", "base");
        if (request.MaxItems > 0)
        {
            var asked = request.Continuation is null ? request.MaxItems : request.MaxItems + 1;
            message.Headers.Add("X-IBM-Max-Items", asked.ToString(CultureInfo.InvariantCulture));
        }
        return message;
    }

    /// <summary>Cuts a host answer to the page that was asked for. Without a limit, a true <c>moreRows</c> means the
    /// host returned only part of the list: refuse it.</summary>
    private static HostFileListing Page(IEnumerable<HostFileEntry> entries, bool? moreRows, HostListRequest request, string what)
    {
        var page = entries.ToList();
        if (request.Continuation is { } after && page.Count > 0 && page[0].Name == after) page.RemoveAt(0);
        var more = moreRows == true;
        if (request.MaxItems <= 0)
        {
            if (more) throw new HostFileException(HostFileErrorKind.ServerError, $"{what}: the host returned only part of the list.");
            return new HostFileListing(page, null);
        }
        if (page.Count > request.MaxItems)
        {
            page.RemoveRange(request.MaxItems, page.Count - request.MaxItems);
            more = true;
        }
        return new HostFileListing(page, more && page.Count > 0 ? page[^1].Name : null);
    }

    private static HostFileEntry ToEntry(MvsmfDataset dataset) => new(
        dataset.Dsname!.Trim(),
        HostFileEntryKind.Dataset,
        new DatasetAttributes(Blank(dataset.Dsorg), Blank(dataset.Recfm), Number(dataset.Lrecl), Number(dataset.Blksz), Blank(dataset.Vol)));

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static int? Number(string? value) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? number : null;

    public async Task<HostTextRead> ReadTextAsync(HostPath path, IProgress<long>? progress = null, CancellationToken cancellationToken = default)
    {
        var what = path.ToString();
        using var idle = new IdleTimeout(_idle, cancellationToken);
        using var response = await SendAsync(() => Get(path, "text"), what, idle, cancellationToken);
        using var body = new MemoryStream();
        await CopyBodyAsync(response, body, idle, progress, what, cancellationToken);
        // mvsMF-compat: text-read-keeps-trailing-blanks — fixed records arrive padded; HostFileTransfer trims them.
        return new HostTextRead(SplitRecords(body.GetBuffer().AsSpan(0, (int)body.Length)), EtagOf(response));
    }

    public async Task<HostBinaryRead> ReadBinaryAsync(HostPath path, Stream destination, IProgress<long>? progress = null, CancellationToken cancellationToken = default)
    {
        var what = path.ToString();
        using var idle = new IdleTimeout(_idle, cancellationToken);
        using var response = await SendAsync(() => Get(path, "binary"), what, idle, cancellationToken);
        var bytes = await CopyBodyAsync(response, destination, idle, progress, what, cancellationToken);
        return new HostBinaryRead(bytes, EtagOf(response));
    }

    private HttpRequestMessage Get(HostPath path, string dataType)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, Url(DatasetPath(path)));
        request.Headers.Add("X-IBM-Data-Type", dataType);
        // mvsMF-compat: etag — the stamp comes only when asked for; it costs the host a second pass over the member.
        request.Headers.Add(ReturnEtagHeader, "true");
        return request;
    }

    /// <summary>The <c>ETag</c> header's value as bare text: quotes and a weak-validator prefix removed, blanks
    /// trimmed, null when absent or empty. Never parsed beyond that: it is echoed back as <c>If-Match</c>.</summary>
    internal static string? EtagOf(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("ETag", out var values)) return null;
        var value = values.FirstOrDefault()?.Trim() ?? "";
        if (value.StartsWith("W/", StringComparison.OrdinalIgnoreCase)) value = value[2..].Trim();
        value = value.Trim('"').Trim();
        return value.Length > 0 ? value : null;
    }

    /// <summary>One string per record: records end in LF, and a CR is data.</summary>
    internal static List<string> SplitRecords(ReadOnlySpan<byte> body)
    {
        // mvsMF-compat: text-body-is-latin1 — the body is ISO-8859-1 whatever the headers say.
        var lines = Encoding.Latin1.GetString(body).Split('\n').ToList();
        if (lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
        return lines;
    }

    public Task<string?> WriteTextAsync(HostPath path, IReadOnlyList<string> lines, string? ifMatch = null, CancellationToken cancellationToken = default) =>
        PutAsync(path, EncodeText(lines), "text", "text/plain", ifMatch, cancellationToken);

    public async Task<string?> WriteBinaryAsync(HostPath path, Stream source, string? ifMatch = null, CancellationToken cancellationToken = default)
    {
        // Held in memory so the repeat after a 401 can send the same bytes.
        using var copy = new MemoryStream();
        await source.CopyToAsync(copy, cancellationToken);
        return await PutAsync(path, copy.ToArray(), "binary", "application/octet-stream", ifMatch, cancellationToken);
    }

    public async Task CreateDatasetAsync(HostPath dataset, DatasetAllocation allocation, CancellationToken cancellationToken = default)
    {
        if (dataset.Kind != HostPathKind.Dataset) throw new ArgumentException("Only a dataset can be created.", nameof(dataset));
        if (allocation.Problems() is { Count: > 0 } problems)
            throw new ArgumentException(string.Join(" ", problems.Values), nameof(allocation));
        var what = dataset.ToString();
        var partitioned = allocation.Organization == DatasetOrganization.Partitioned;
        var body = JsonSerializer.SerializeToUtf8Bytes(new MvsmfAllocation(
            partitioned ? "PO" : "PS",
            allocation.FoldedRecfm,
            allocation.Lrecl,
            allocation.Blksize,
            allocation.Unit == SpaceUnit.Cylinders ? "CYL" : "TRK",
            allocation.Primary,
            allocation.Secondary,
            partitioned ? allocation.DirectoryBlocks : null), MvsmfJsonContext.Default.MvsmfAllocation);
        using var idle = new IdleTimeout(_idle, cancellationToken);
        // mvsMF-compat: create-failure-is-one-500 — the host answers every allocation failure the same way;
        // MvsmfErrors maps it to CannotAllocate, whose sentence names the three possible causes.
        using var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Post, Url(DatasetPath(dataset)))
        {
            Content = new ByteArrayContent(body) { Headers = { ContentType = new MediaTypeHeaderValue(JsonContentType) } },
        }, what, idle, cancellationToken);
    }

    public async Task RenameAsync(HostPath from, string newName, CancellationToken cancellationToken = default)
    {
        // ForMember and ForDataset fold the name and throw ArgumentException for one the rules refuse, so nothing
        // the host would fold differently is ever sent.
        var target = from.Kind == HostPathKind.Member ? HostPath.ForMember(from.Dataset, newName) : HostPath.ForDataset(newName);
        var what = $"Rename {from} to {(from.Kind == HostPathKind.Member ? target.Member : target.Dataset)}";
        var body = JsonSerializer.SerializeToUtf8Bytes(
            new MvsmfRename("rename", new MvsmfRenameSource(from.Dataset, from.Member)), MvsmfJsonContext.Default.MvsmfRename);
        using var idle = new IdleTimeout(_idle, cancellationToken);
        // mvsMF-compat: put-json-is-rename — this is the one PUT that sends application/json, and it is a rename
        // on purpose: the new name is the URL, the old one the body. A write never sends this content type.
        // mvsMF-compat: rename-target-exists-400 — a member rename onto an existing name is 400 reason 7.
        using var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Put, Url(DatasetPath(target)))
        {
            Content = new ByteArrayContent(body) { Headers = { ContentType = new MediaTypeHeaderValue(JsonContentType) } },
        }, what, idle, cancellationToken);
    }

    public async Task DeleteAsync(HostPath path, CancellationToken cancellationToken = default)
    {
        var what = path.ToString();
        using var idle = new IdleTimeout(_idle, cancellationToken);
        using var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Delete, Url(DatasetPath(path))), what, idle, cancellationToken);
    }

    private async Task<string?> PutAsync(HostPath path, byte[] body, string dataType, string contentType, string? ifMatch, CancellationToken cancellationToken)
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
            // mvsMF-compat: etag — the stamp of the member as written is the one the next If-Match must carry (the
            // pre-save stamp fails), so every write asks for it. If-Match goes as the host gave it, unquoted.
            request.Headers.Add(ReturnEtagHeader, "true");
            if (ifMatch is { Length: > 0 } && !request.Headers.TryAddWithoutValidation("If-Match", ifMatch))
                throw new HostFileException(HostFileErrorKind.InvalidRequest, $"{what}: the host's stamp cannot be sent back.");
            return request;
        }, what, idle, cancellationToken);
        return EtagOf(response);
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
            text.Append(line).Append('\n');
        }
        // mvsMF-compat: text-body-is-latin1 — the host reads the body as ISO-8859-1 whatever charset says.
        // mvsMF-compat: text-write-truncates — an over-long line is cut to the record length and written before the
        // host answers 500. TextUploadCheck refuses such lines when the listing gave it a record length; when it did
        // not (unknown RECFM, no LRECL), the line goes out and the host may truncate.
        return Encoding.Latin1.GetBytes(text.ToString());
    }

    public void Dispose()
    {
        _http.Dispose();
        _trustGate.Dispose();
    }

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
        await CheckTrustAsync(what, idle, cancellationToken);
        idle.Pause();
        var token = await AskAsync(new HostTokenRequest(null), cancellationToken);
        var response = await SendOnceAsync(build, token, what, idle, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            response.Dispose();
            idle.Pause();
            token = await AskAsync(new HostTokenRequest(token), cancellationToken);
            response = await SendOnceAsync(build, token, what, idle, cancellationToken);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                // The sign-in that minted this token has just taken the password, so what the host refused is the
                // session, not the password: a 401 on an operation never means a bad password. The generic mapping
                // would blame the password.
                response.Dispose();
                throw new HostFileException(HostFileErrorKind.Unauthenticated,
                    $"{what}: the host would not accept the session it had just issued.");
            }
        }
        if (response.IsSuccessStatusCode) return response;
        using (response) throw await FailureAsync(response, what, idle, cancellationToken);
    }

    /// <summary>Over https, a fresh service's first contact with the host is this anonymous <c>GET info</c>, made
    /// before any password is asked for, so that an untrusted certificate is refused (<see
    /// cref="HostFileErrorKind.CertificateRejected"/>) while the sign-in prompt is still closed. Without it the
    /// login POST would be the first handshake, and the operation run again after Connect Anyway, on a service that
    /// trusts the certificate, would have to ask for the password a second time. The answer itself is ignored; a 401
    /// is the expected one. Over http there is no handshake to fail, so nothing is sent.</summary>
    private async Task CheckTrustAsync(string what, IdleTimeout idle, CancellationToken cancellationToken)
    {
        if (_trustChecked || _base.Scheme != Uri.UriSchemeHttps) return;
        await _trustGate.WaitAsync(cancellationToken);
        try
        {
            if (_trustChecked) return;
            using var request = new HttpRequestMessage(HttpMethod.Get, Url("info"));
            AddCommonHeaders(request);
            using var response = await SendRawAsync(request, what, idle, cancellationToken);
            _trustChecked = true;
        }
        finally
        {
            _trustGate.Release();
        }
    }

    /// <summary>The exception for a non-success answer: the body is drained so the error JSON, if any, can say
    /// what went wrong. The caller disposes the response.</summary>
    private async Task<HostFileException> FailureAsync(HttpResponseMessage response, string what, IdleTimeout idle, CancellationToken cancellationToken)
    {
        using var body = new MemoryStream();
        await CopyBodyAsync(response, body, idle, null, what, cancellationToken);
        return MvsmfErrors.FromResponse(response.StatusCode, body.ToArray(), what);
    }

    private async Task<HostSessionToken> AskAsync(HostTokenRequest request, CancellationToken cancellationToken) =>
        await _tokens(request, SignInAsync, cancellationToken)
        ?? throw new HostFileException(HostFileErrorKind.Unauthenticated, "Sign-in was cancelled.");

    /// <summary>The provider's <see cref="HostSignIn"/>: POST the credentials, take the LtpaToken2 cookie.</summary>
    private async Task<HostSessionToken> SignInAsync(HostCredentials credentials, CancellationToken cancellationToken)
    {
        const string what = "Sign-in";
        using var idle = new IdleTimeout(_idle, cancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Post, Url("services/authenticate"))
        {
            Content = new ByteArrayContent([]) { Headers = { ContentType = new MediaTypeHeaderValue("application/x-www-form-urlencoded") } },
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.Latin1.GetBytes($"{credentials.Userid}:{credentials.Password}")));
        AddCommonHeaders(request);
        using (var response = await SendRawAsync(request, what, idle, cancellationToken))
        {
            if (response.StatusCode == HttpStatusCode.Unauthorized)
                throw new HostFileException(HostFileErrorKind.Unauthenticated, "The host rejected the userid or password.");
            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed) throw NoSignInRoute();
            if (!response.IsSuccessStatusCode) throw await FailureAsync(response, what, idle, cancellationToken);
            // A success that is not JSON is a proxy's or a web server's catch-all page answering a route this host
            // does not have (spec §4.2) — not an mvsMF that forgot its cookie, which is what the check below would
            // otherwise report.
            if (!string.Equals(response.Content.Headers.ContentType?.MediaType, "application/json", StringComparison.OrdinalIgnoreCase))
                throw NoSignInRoute();
            if (TokenFromCookies(response) is not { } token)
                throw new HostFileException(HostFileErrorKind.ServerError, "Sign-in: the host set no session cookie.");
            return token;
        }
    }

    /// <summary>A host with no usable sign-in route (spec §4.2): 404, 405, or any other non-JSON answer.</summary>
    private static HostFileException NoSignInRoute() =>
        new(HostFileErrorKind.Unsupported, "This host does not support sign-in; LizTerm needs mvsMF 1.1.0 or later.");

    /// <summary>The unauthenticated reachability check for the Test button: the info when the host answers it
    /// without credentials, null for a 401 (reachable, sign-in needed). A 404 is not an mvsMF; any other failure is
    /// the host's own answer (a proxy's 403, a 503 while it starts) and is reported as such, so a correct URL is not
    /// mistaken for a wrong one.</summary>
    public async Task<HostServerInfo?> ProbeAsync(CancellationToken cancellationToken = default)
    {
        const string what = "Server information";
        using var idle = new IdleTimeout(_idle, cancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Get, Url("info"));
        AddCommonHeaders(request);
        using (var response = await SendRawAsync(request, what, idle, cancellationToken))
        {
            _trustChecked = true; // the handshake succeeded, so a later sign-in on this service need not probe again
            if (response.StatusCode == HttpStatusCode.Unauthorized) return null;
            if (response.StatusCode == HttpStatusCode.NotFound) throw NotMvsmf();
            if (!response.IsSuccessStatusCode) throw await FailureAsync(response, what, idle, cancellationToken);
            MvsmfInfo info;
            try
            {
                info = await ReadJsonAsync(response, MvsmfJsonContext.Default.MvsmfInfo, what, idle, cancellationToken);
            }
            catch (HostFileException ex) when (ex.Kind == HostFileErrorKind.ServerError)
            {
                // A 200 that is not mvsMF's info (a web server's home page, say) is the same answer as a 404.
                throw NotMvsmf(ex);
            }
            // Every MvsmfInfo field is an optional string, so another product's JSON /info (or a bare {}) parses
            // happily into all-null. An answer naming neither a product version nor a system is not an mvsMF's.
            var fields = InfoFields(info);
            if (fields is (null, null)) throw NotMvsmf();
            return ToServerInfo(fields);
        }
    }

    /// <summary>The Test button's line for a URL that is reachable but is not an mvsMF (spec §4.5).</summary>
    private static HostFileException NotMvsmf(Exception? inner = null) =>
        new(HostFileErrorKind.Unsupported, "Nothing at this URL answers as mvsMF.", inner: inner);

    public async Task SignOutAsync(HostSessionToken token, CancellationToken cancellationToken = default)
    {
        using var idle = new IdleTimeout(_idle, cancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Delete, Url("services/authenticate"));
        AddSessionCookie(request, token);
        AddCommonHeaders(request);
        try
        {
            // Not through SendRawAsync: that turns every failure into a HostFileException naming the operation,
            // and a best-effort sign-out swallows instead of mapping. The certificate check is not the reason —
            // its one refusal slot is filled by MvsmfCertificateCheck.Validate during the handshake, and this
            // DELETE runs on a service built for it and disposed with it, so its check is a fresh one anyway.
            idle.Reset();
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, idle.Token);
            // 204 is a clean logout; 401 means the host already forgot the token. Both are success.
        }
        catch (Exception ex) when (ex is HttpRequestException ||
            (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            // Best effort: a transport failure or the idle timeout is swallowed, since the caller (window close)
            // can do nothing about it. The caller's OWN cancellation — the close-time cap — is not swallowed: it
            // says the DELETE was given up on, and the caller's continuation observes it.
        }
    }

    private async Task<HttpResponseMessage> SendOnceAsync(Func<HttpRequestMessage> build, HostSessionToken token,
        string what, IdleTimeout idle, CancellationToken cancellationToken)
    {
        using var request = build();
        AddSessionCookie(request, token);
        AddCommonHeaders(request);
        return await SendRawAsync(request, what, idle, cancellationToken);
    }

    /// <summary>The one place a request goes out: re-arm the idle clock, send, and turn every transport failure
    /// into a <see cref="HostFileException"/> naming <paramref name="what"/>. The caller owns the request and the
    /// answer.</summary>
    private async Task<HttpResponseMessage> SendRawAsync(HttpRequestMessage request, string what, IdleTimeout idle,
        CancellationToken cancellationToken)
    {
        try
        {
            idle.Reset();
            return await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, idle.Token);
        }
        catch (OperationCanceledException) when (idle.Expired(cancellationToken)) { throw TimedOut(what); }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested && ex.InnerException is TimeoutException)
        {
            throw new HostFileException(HostFileErrorKind.Unreachable,
                $"{what}: cannot reach the host (no answer within {ConnectTimeout.TotalSeconds:0} s).", inner: ex);
        }
        catch (HttpRequestException ex) { throw Unreachable(what, ex); }
    }

    private static void AddCommonHeaders(HttpRequestMessage request)
    {
        request.Headers.Add("X-CSRF-ZOSMF-HEADER", "LizTerm");
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("LizTerm", ProductVersion));
    }

    private static void AddSessionCookie(HttpRequestMessage request, HostSessionToken token) =>
        request.Headers.Add("Cookie", $"{SessionCookie}={token.Value}");

    /// <summary>The <c>LtpaToken2</c> cookie's value, matched on the cookie's name and nothing else: a proxy's cookie
    /// whose name merely ends the same way, or whose value quotes the name, is not the session.</summary>
    private static HostSessionToken? TokenFromCookies(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var cookies)) return null;
        foreach (var cookie in cookies)
        {
            var span = cookie.AsSpan();
            var end = span.IndexOf(';');
            var pair = (end < 0 ? span : span[..end]).Trim();
            var equals = pair.IndexOf('=');
            if (equals < 0 || !pair[..equals].Trim().SequenceEqual(SessionCookie)) continue;
            var value = pair[(equals + 1)..].Trim().ToString();
            if (value.Length > 0) return new HostSessionToken(value);
        }
        return null;
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
