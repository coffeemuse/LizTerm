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

    [Theory]
    [InlineData("not-a-version", "0.5.2")]
    [InlineData("0.5.2", "not-a-version")]
    [InlineData("", "0.5.2")]
    public void Throws_for_a_version_string_that_will_not_parse(string latest, string current) =>
        Assert.Throws<FormatException>(() => ReleaseVersion.IsNewer(latest, current));
}
