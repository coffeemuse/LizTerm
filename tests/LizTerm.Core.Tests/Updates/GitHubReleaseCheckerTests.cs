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
        Assert.Equal("LizTerm", handler.LastRequest.Headers.UserAgent.Single().Product!.Name);
    }

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

    [Fact]
    public async Task A_body_missing_tag_name_throws_JsonException()
    {
        var (checker, _) = Create(HttpStatusCode.OK, """{"html_url":"https://github.com/coffeemuse/LizTerm/releases/tag/v0.6.0"}""");

        await Assert.ThrowsAsync<JsonException>(() => checker.GetLatestReleaseAsync(CancellationToken.None));
    }
}
