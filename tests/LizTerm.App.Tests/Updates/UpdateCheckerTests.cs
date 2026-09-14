// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Net;
using System.Net.Http;
using System.Text.Json;
using LizTerm.App.Tests.Fakes;
using LizTerm.App.Updates;
using LizTerm.Core.Updates;

namespace LizTerm.App.Tests.Updates;

public class UpdateCheckerTests
{
    private const string ReleasePage = ProjectLinks.Releases + "/tag/v0.6.0";

    /// <summary>Every exception here carries the message "raw", so an exact reason also proves the exception's own
    /// message never reaches the user.</summary>
    private static async Task<string> FailureReason(Exception exception)
    {
        var result = await UpdateChecker.CheckAsync(new FakeReleaseChecker { Exception = exception }, "0.5.2", CancellationToken.None);
        return Assert.IsType<UpdateCheckResult.Failed>(result).Reason;
    }

    [Fact]
    public async Task Reports_a_newer_release_when_the_latest_version_is_higher()
    {
        var checker = new FakeReleaseChecker { Result = new ReleaseInfo("0.6.0", ReleasePage) };

        var result = await UpdateChecker.CheckAsync(checker, "0.5.2", CancellationToken.None);

        var newer = Assert.IsType<UpdateCheckResult.NewerAvailable>(result);
        Assert.Equal("0.6.0", newer.Version);
        Assert.Equal(ReleasePage, newer.HtmlUrl);
    }

    [Fact]
    public async Task Reports_up_to_date_when_the_latest_version_is_not_higher()
    {
        var checker = new FakeReleaseChecker { Result = new ReleaseInfo("0.5.2", ReleasePage) };

        var result = await UpdateChecker.CheckAsync(checker, "0.5.2", CancellationToken.None);

        Assert.IsType<UpdateCheckResult.UpToDate>(result);
    }

    /// <summary>Download hands the page to the platform launcher, which opens any scheme it is given.</summary>
    [Theory]
    [InlineData("file:///Applications/Calculator.app")]
    [InlineData("https://github.com/someone-else/LizTerm/releases/tag/v0.6.0")]
    [InlineData("https://example.com/coffeemuse/LizTerm/releases/tag/v0.6.0")]
    [InlineData("http://github.com/coffeemuse/LizTerm/releases/tag/v0.6.0")]
    [InlineData("")]
    public async Task A_release_page_outside_this_projects_releases_is_replaced_by_the_releases_index(string htmlUrl)
    {
        var checker = new FakeReleaseChecker { Result = new ReleaseInfo("0.6.0", htmlUrl) };

        var result = await UpdateChecker.CheckAsync(checker, "0.5.2", CancellationToken.None);

        Assert.Equal(ProjectLinks.Releases, Assert.IsType<UpdateCheckResult.NewerAvailable>(result).HtmlUrl);
    }

    [Fact]
    public async Task A_timeout_is_reported_as_one() =>
        Assert.Equal("The request timed out.", await FailureReason(new TaskCanceledException("raw")));

    [Fact]
    public async Task A_request_that_got_no_answer_is_blamed_on_reaching_github() =>
        Assert.Equal("Could not reach GitHub.", await FailureReason(new HttpRequestException("raw")));

    [Fact]
    public async Task A_proxy_that_refuses_the_tunnel_is_named() =>
        Assert.Equal("The proxy refused the connection to GitHub.",
            await FailureReason(new HttpRequestException(HttpRequestError.ProxyTunnelError, "raw")));

    /// <summary>GitHub or a proxy answered, so the network is not the problem and must not be named as it.</summary>
    [Theory]
    [InlineData(403, "GitHub is limiting requests from this network. Try again later.")]
    [InlineData(429, "GitHub is limiting requests from this network. Try again later.")]
    [InlineData(404, "GitHub has no published release to compare with.")]
    [InlineData(407, "The proxy refused the connection to GitHub.")]
    [InlineData(502, "GitHub answered with an error (HTTP 502).")]
    public async Task A_status_that_was_answered_is_named_rather_than_blamed_on_the_network(int status, string reason) =>
        Assert.Equal(reason, await FailureReason(new HttpRequestException("raw", null, (HttpStatusCode)status)));

    [Fact]
    public async Task An_unreadable_body_is_reported_as_unreadable_release_information() =>
        Assert.Equal("The release information could not be read.", await FailureReason(new JsonException("raw")));

    /// <summary>The real parse, not a FormatException handed in: a tag that is not a plain version is a failure to
    /// read, never "up to date".</summary>
    [Fact]
    public async Task A_release_version_that_will_not_parse_is_unreadable_not_up_to_date()
    {
        var checker = new FakeReleaseChecker { Result = new ReleaseInfo("0.7.0-rc1", ReleasePage) };

        var result = await UpdateChecker.CheckAsync(checker, "0.5.2", CancellationToken.None);

        Assert.Equal("The release information could not be read.", Assert.IsType<UpdateCheckResult.Failed>(result).Reason);
    }

    [Fact]
    public async Task A_build_version_that_will_not_parse_is_named_and_github_is_not_asked()
    {
        var checker = new FakeReleaseChecker { Result = new ReleaseInfo("0.6.0", ReleasePage) };

        var result = await UpdateChecker.CheckAsync(checker, "0.6.0-dev", CancellationToken.None);

        Assert.Equal("This build's version, 0.6.0-dev, cannot be compared with a release.",
            Assert.IsType<UpdateCheckResult.Failed>(result).Reason);
        Assert.Equal(0, checker.Calls);
    }
}
