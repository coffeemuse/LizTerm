// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Net.Http;
using System.Text.Json;
using LizTerm.App.Tests.Fakes;
using LizTerm.App.Updates;
using LizTerm.Core.Updates;

namespace LizTerm.App.Tests.Updates;

public class UpdateCheckerTests
{
    [Fact]
    public async Task Reports_a_newer_release_when_the_latest_version_is_higher()
    {
        var checker = new FakeReleaseChecker { Result = new ReleaseInfo("0.6.0", "https://example/release") };

        var result = await UpdateChecker.CheckAsync(checker, "0.5.2", CancellationToken.None);

        var newer = Assert.IsType<UpdateCheckResult.NewerAvailable>(result);
        Assert.Equal("0.6.0", newer.Version);
        Assert.Equal("https://example/release", newer.HtmlUrl);
    }

    [Fact]
    public async Task Reports_up_to_date_when_the_latest_version_is_not_higher()
    {
        var checker = new FakeReleaseChecker { Result = new ReleaseInfo("0.5.2", "https://example/release") };

        var result = await UpdateChecker.CheckAsync(checker, "0.5.2", CancellationToken.None);

        Assert.IsType<UpdateCheckResult.UpToDate>(result);
    }

    [Theory]
    [InlineData(typeof(HttpRequestException))]
    [InlineData(typeof(TaskCanceledException))]
    [InlineData(typeof(JsonException))]
    [InlineData(typeof(FormatException))]
    public async Task Reports_failure_with_a_reason_for_each_caught_exception_type(Type exceptionType)
    {
        var checker = new FakeReleaseChecker { Exception = (Exception)Activator.CreateInstance(exceptionType)! };

        var result = await UpdateChecker.CheckAsync(checker, "0.5.2", CancellationToken.None);

        var failed = Assert.IsType<UpdateCheckResult.Failed>(result);
        Assert.False(string.IsNullOrWhiteSpace(failed.Reason));
    }
}
