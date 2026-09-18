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

    /// <summary>What a build made outside a git checkout carries -- a source archive, a distro packaging tree --
    /// since the stamp comes from the SDK's source-control query. It still says -DEV: only the release marker
    /// decides that.</summary>
    [Theory]
    [InlineData("0.6.1")]
    [InlineData("0.6.1+")]
    [InlineData("")]
    [InlineData(null)]
    public void Commit_is_absent_without_a_stamped_revision(string? informational)
        => Assert.Null(AppVersion.ShortCommit(informational));

    /// <summary>Anything that is not a hash is not shown as one. Seven characters is the floor because that is
    /// what is displayed; a shorter suffix would be padded out with whatever followed it. Uppercase is rejected
    /// too: a git object id is lowercase, and this test, ShortCommit and the release workflow's own check have to
    /// agree on what a commit looks like.</summary>
    [Theory]
    [InlineData("0.6.1+not-a-revision")]
    [InlineData("0.6.1+dd62c")]
    [InlineData("0.6.1+DD62CC06106BCAA9D9088FD4CF63EBB557F3183F")]
    public void Commit_is_absent_when_the_suffix_is_not_a_hash(string informational)
        => Assert.Null(AppVersion.ShortCommit(informational));

    [Fact]
    public void Commit_of_this_build_is_absent_or_a_short_hash()
        => Assert.True(AppVersion.Commit is null || Regex.IsMatch(AppVersion.Commit, "^[0-9a-f]{7}$"),
            $"unexpected commit '{AppVersion.Commit}'");

    /// <summary>The marker is written by the release publish and by nothing else, so a build of the tests --
    /// local, CI, or one made from a source archive with no commit to stamp -- is never a release. This is what
    /// stops "no commit" from quietly meaning "release" (issue #141).</summary>
    [Fact]
    public void This_build_is_not_a_release()
        => Assert.False(AppVersion.IsRelease);

    [Fact]
    public void A_release_shows_its_bare_version()
        => Assert.Equal("0.6.1", AppVersion.Describe("0.6.1", "dd62cc0", isRelease: true));

    /// <summary>The -DEV marker says "not a release" on its own, so the commit after it is the detail rather than
    /// the signal -- and a build with no commit to show still wears it.</summary>
    [Theory]
    [InlineData("a1b2c3d", "0.6.1-DEV (a1b2c3d)")]
    [InlineData(null, "0.6.1-DEV")]
    public void Anything_else_wears_DEV(string? commit, string expected)
        => Assert.Equal(expected, AppVersion.Describe("0.6.1", commit, isRelease: false));

    /// <summary>What About and the splash actually show, so the two cannot drift apart.</summary>
    [Fact]
    public void Display_is_what_the_parts_describe()
        => Assert.Equal(AppVersion.Describe(AppVersion.Current, AppVersion.Commit, AppVersion.IsRelease),
            AppVersion.Display);
}
