// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Text.RegularExpressions;

namespace LizTerm.App.Tests;

public class AppVersionTests
{
    [Fact]
    public void Version_is_a_plain_three_part_number()
    {
        Assert.Matches(new Regex(@"^\d+\.\d+\.\d+$"), AppVersion.Current);
        Assert.DoesNotContain("+", AppVersion.Current);
    }

    [Fact]
    public void Commit_is_the_first_seven_characters_of_the_stamped_revision()
        => Assert.Equal("dd62cc0", AppVersion.ShortCommit("0.6.1+dd62cc06106bcaa9d9088fd4cf63ebb557f3183f"));

    /// <summary>What a release build carries: the release job suppresses the revision stamp, so there is no
    /// "+" to read and About shows the bare version.</summary>
    [Theory]
    [InlineData("0.6.1")]
    [InlineData("0.6.1+")]
    [InlineData("")]
    [InlineData(null)]
    public void Commit_is_absent_without_a_stamped_revision(string? informational)
        => Assert.Null(AppVersion.ShortCommit(informational));

    /// <summary>Anything that is not a hash is not shown as one. Seven characters is the floor because that is
    /// what is displayed; a shorter suffix would be padded out with whatever followed it.</summary>
    [Theory]
    [InlineData("0.6.1+not-a-revision")]
    [InlineData("0.6.1+dd62c")]
    public void Commit_is_absent_when_the_suffix_is_not_a_hash(string informational)
        => Assert.Null(AppVersion.ShortCommit(informational));

    [Fact]
    public void Commit_of_this_build_is_absent_or_a_short_hash()
        => Assert.True(AppVersion.Commit is null || Regex.IsMatch(AppVersion.Commit, "^[0-9a-f]{7}$"),
            $"unexpected commit '{AppVersion.Commit}'");
}
