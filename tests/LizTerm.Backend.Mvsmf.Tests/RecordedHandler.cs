// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Net;
using System.Text;

namespace LizTerm.Backend.Mvsmf.Tests;

internal sealed record RecordedRequest(HttpMethod Method, Uri Uri, string? Authorization, string? Cookie, string? Csrf, string? DataType, string? ContentType, byte[]? Body, IReadOnlyList<string> HeaderNames, IReadOnlyDictionary<string, string> Headers)
{
    public string BodyText => Body is null ? "" : Encoding.Latin1.GetString(Body);
}

/// <summary>Answers each request with the next queued response and keeps what was asked.</summary>
internal sealed class RecordedHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>> _responses = new();

    public List<RecordedRequest> Requests { get; } = [];

    public RecordedHandler Then(string fixture) => Then((_, _) => Task.FromResult(Fixture.Load(fixture)));

    public RecordedHandler Then(HttpStatusCode status, string body = "", string contentType = "application/json") =>
        Then((_, _) => Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, contentType),
        }));

    public RecordedHandler Then(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
    {
        _responses.Enqueue(respond);
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsByteArrayAsync(cancellationToken);
        Requests.Add(new RecordedRequest(
            request.Method,
            request.RequestUri!,
            request.Headers.Authorization?.ToString(),
            request.Headers.TryGetValues("Cookie", out var cookies) ? string.Join("; ", cookies) : null,
            request.Headers.TryGetValues("X-CSRF-ZOSMF-HEADER", out var csrf) ? string.Join(",", csrf) : null,
            request.Headers.TryGetValues("X-IBM-Data-Type", out var types) ? string.Join(",", types) : null,
            request.Content?.Headers.ContentType?.ToString(),
            body,
            [.. request.Headers.Select(h => h.Key)],
            request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase)));
        if (_responses.Count == 0) throw new InvalidOperationException($"No response queued for {request.Method} {request.RequestUri}");
        return await _responses.Dequeue()(request, cancellationToken);
    }
}
