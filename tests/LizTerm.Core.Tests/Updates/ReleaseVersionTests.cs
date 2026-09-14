// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.Core.Updates;

namespace LizTerm.Core.Tests.Updates;

public class ReleaseVersionTests
{
    [Theory]
    [InlineData("0.6.0", "0.5.2", true)]
    [InlineData("0.5.2", "0.5.2", false)]
    [InlineData("0.5.1", "0.5.2", false)]
    [InlineData("1.0.0", "0.9.9", true)]
    [InlineData("0.5.10", "0.5.9", true)]
    public void Compares_two_plain_version_strings(string latest, string current, bool expected) =>
        Assert.Equal(expected, ReleaseVersion.IsNewer(latest, current));

    /// <summary>Version.TryParse alone would take the two- and four-part, spaced and signed ones, and a missing part
    /// reads as -1 there, so 0.5.2.0 would count as newer than the 0.5.2 it names.</summary>
    [Theory]
    [InlineData("not-a-version", "0.5.2")]
    [InlineData("0.5.2", "not-a-version")]
    [InlineData("", "0.5.2")]
    [InlineData("0.6", "0.5.2")]
    [InlineData("0.5.2.0", "0.5.2")]
    [InlineData(" 0.6.0", "0.5.2")]
    [InlineData("+0.6.0", "0.5.2")]
    [InlineData("0.6.0-rc.1", "0.5.2")]
    [InlineData("0.5.2", "0.6.0+abc")]
    public void Throws_for_anything_but_major_minor_patch_in_plain_digits(string latest, string current) =>
        Assert.Throws<FormatException>(() => ReleaseVersion.IsNewer(latest, current));

    [Theory]
    [InlineData("0.5.2", true)]
    [InlineData("00.06.00", true)]
    [InlineData("0.6", false)]
    [InlineData("0.6.0-dev", false)]
    public void Says_whether_a_version_is_one_it_can_compare(string value, bool expected) =>
        Assert.Equal(expected, ReleaseVersion.IsValid(value));
}
