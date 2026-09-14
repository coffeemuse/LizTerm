// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Diagnostics.CodeAnalysis;

namespace LizTerm.Core.Updates;

/// <summary>Compares two plain version strings — the shape AppVersion.Current and a GitHub release tag with its
/// leading "v" already stripped (GitHubReleaseChecker's job) both share. No caller here ever sees a tag.</summary>
public static class ReleaseVersion
{
    /// <summary>True when latest is a strictly higher version than current. Throws FormatException unless both are
    /// exactly Major.Minor.Patch in plain digits.</summary>
    public static bool IsNewer(string latest, string current) => Parse(latest) > Parse(current);

    /// <summary>Whether <paramref name="value"/> is a version IsNewer accepts.</summary>
    public static bool IsValid(string value) => TryParse(value, out _);

    private static Version Parse(string value) =>
        TryParse(value, out var version) ? version : throw new FormatException($"'{value}' is not a version LizTerm understands.");

    /// <summary>Stricter than Version.TryParse, which also takes two or four parts — the missing ones read as -1,
    /// so 0.5.2.0 would count as newer than 0.5.2 — and surrounding spaces and signs.</summary>
    private static bool TryParse(string value, [NotNullWhen(true)] out Version? version)
    {
        version = null;
        var parts = value.Split('.');
        return parts.Length == 3
            && parts.All(part => part.Length > 0 && part.All(char.IsAsciiDigit))
            && Version.TryParse(value, out version);
    }
}
