// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.App.Updates;

namespace LizTerm.App.Tests.Updates;

public class UpdateNotificationPolicyTests
{
    [Fact]
    public void Shows_a_newer_release_with_no_skipped_version()
    {
        var result = new UpdateCheckResult.NewerAvailable("0.6.0", "https://example/release");
        Assert.True(UpdateNotificationPolicy.ShouldShowAutomatically(result, null));
    }

    [Fact]
    public void Does_not_show_the_exact_version_that_was_skipped()
    {
        var result = new UpdateCheckResult.NewerAvailable("0.6.0", "https://example/release");
        Assert.False(UpdateNotificationPolicy.ShouldShowAutomatically(result, "0.6.0"));
    }

    [Fact]
    public void Shows_a_later_release_even_when_an_older_one_was_skipped()
    {
        var result = new UpdateCheckResult.NewerAvailable("0.6.1", "https://example/release");
        Assert.True(UpdateNotificationPolicy.ShouldShowAutomatically(result, "0.6.0"));
    }

    [Fact]
    public void Never_shows_up_to_date_or_a_failure_whatever_the_skipped_version_is()
    {
        Assert.False(UpdateNotificationPolicy.ShouldShowAutomatically(new UpdateCheckResult.UpToDate(), null));
        Assert.False(UpdateNotificationPolicy.ShouldShowAutomatically(new UpdateCheckResult.Failed("x"), null));
        Assert.False(UpdateNotificationPolicy.ShouldShowAutomatically(new UpdateCheckResult.UpToDate(), "0.6.0"));
    }
}
