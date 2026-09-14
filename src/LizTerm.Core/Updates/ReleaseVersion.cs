// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

namespace LizTerm.Core.Updates;

/// <summary>Compares two plain version strings — the shape AppVersion.Current and a GitHub release tag with its
/// leading "v" already stripped (GitHubReleaseChecker's job) both share. No caller here ever sees a tag.</summary>
public static class ReleaseVersion
{
    public static bool IsNewer(string latest, string current) => Parse(latest) > Parse(current);

    private static Version Parse(string value) =>
        Version.TryParse(value, out var version) ? version : throw new FormatException($"'{value}' is not a version LizTerm understands.");
}
