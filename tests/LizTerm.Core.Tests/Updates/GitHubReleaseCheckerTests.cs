// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Net;
using System.Text.Json;
using LizTerm.Core.Updates;

namespace LizTerm.Core.Tests.Updates;

public class GitHubReleaseCheckerTests
{
    private sealed class FakeHandler(HttpStatusCode status, string? body) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            var response = new HttpResponseMessage(status);
            if (body is not null) response.Content = new StringContent(body);
            return Task.FromResult(response);
        }
    }

    private static (GitHubReleaseChecker Checker, FakeHandler Handler) Create(HttpStatusCode status, string? body)
    {
        var handler = new FakeHandler(status, body);
        return (new GitHubReleaseChecker(new HttpClient(handler)), handler);
    }

    [Fact]
    public async Task Requests_the_exact_url_and_the_three_headers()
    {
        var (checker, handler) = Create(HttpStatusCode.OK,
            """{"tag_name":"v0.6.0","html_url":"https://github.com/coffeemuse/LizTerm/releases/tag/v0.6.0"}""");

        await checker.GetLatestReleaseAsync(CancellationToken.None);

        Assert.Equal("https://api.github.com/repos/coffeemuse/LizTerm/releases/latest", handler.LastRequest!.RequestUri!.ToString());
        Assert.Equal("application/vnd.github+json", handler.LastRequest.Headers.Accept.Single().MediaType);
        Assert.Equal("2022-11-28", handler.LastRequest.Headers.GetValues("X-GitHub-Api-Version").Single());
        var product = handler.LastRequest.Headers.UserAgent.Single().Product!;
        Assert.Equal("LizTerm", product.Name);
        Assert.Equal(typeof(GitHubReleaseChecker).Assembly.GetName().Version!.ToString(3), product.Version);
    }

    /// <summary>The bound on a check with no route to the internet. A handler never sees HttpClient.Timeout, so it
    /// is read off the client production uses.</summary>
    [Fact]
    public void The_production_client_gives_up_after_ten_seconds() =>
        Assert.Equal(TimeSpan.FromSeconds(10), GitHubReleaseChecker.CreateHttpClient().Timeout);

    [Fact]
    public async Task Parses_the_tag_and_strips_its_leading_v()
    {
        var (checker, _) = Create(HttpStatusCode.OK,
            """{"tag_name":"v0.6.0","html_url":"https://github.com/coffeemuse/LizTerm/releases/tag/v0.6.0"}""");

        var release = await checker.GetLatestReleaseAsync(CancellationToken.None);

        Assert.Equal("0.6.0", release.Version);
        Assert.Equal("https://github.com/coffeemuse/LizTerm/releases/tag/v0.6.0", release.HtmlUrl);
    }

    [Fact]
    public async Task A_non_success_status_throws_HttpRequestException()
    {
        var (checker, _) = Create(HttpStatusCode.NotFound, null);

        await Assert.ThrowsAsync<HttpRequestException>(() => checker.GetLatestReleaseAsync(CancellationToken.None));
    }

    [Fact]
    public async Task A_malformed_body_throws_JsonException()
    {
        var (checker, _) = Create(HttpStatusCode.OK, "not json");

        await Assert.ThrowsAsync<JsonException>(() => checker.GetLatestReleaseAsync(CancellationToken.None));
    }

    /// <summary>JSON null reads back as no record at all, which would otherwise be a NullReferenceException that the
    /// App's failure handling does not expect.</summary>
    [Fact]
    public async Task A_null_body_throws_JsonException()
    {
        var (checker, _) = Create(HttpStatusCode.OK, "null");

        await Assert.ThrowsAsync<JsonException>(() => checker.GetLatestReleaseAsync(CancellationToken.None));
    }

    [Fact]
    public async Task A_body_missing_tag_name_throws_JsonException()
    {
        var (checker, _) = Create(HttpStatusCode.OK, """{"html_url":"https://github.com/coffeemuse/LizTerm/releases/tag/v0.6.0"}""");

        await Assert.ThrowsAsync<JsonException>(() => checker.GetLatestReleaseAsync(CancellationToken.None));
    }

    [Fact]
    public async Task A_body_missing_html_url_throws_JsonException()
    {
        var (checker, _) = Create(HttpStatusCode.OK, """{"tag_name":"v0.6.0"}""");

        await Assert.ThrowsAsync<JsonException>(() => checker.GetLatestReleaseAsync(CancellationToken.None));
    }
}
