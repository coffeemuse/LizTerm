// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Reflection;

namespace LizTerm.App;

/// <summary>The app's version as the build stamped it (the Version property in Directory.Build.props), without the
/// "+commit" suffix the SDK appends to the informational version inside a git checkout, plus that commit on its own
/// for the builds that carry one.</summary>
public static class AppVersion
{
    private static readonly string? Informational =
        typeof(AppVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

    public static string Current { get; } = Read();

    /// <summary>The short commit this build came from, or null for a release build. The release job publishes with
    /// IncludeSourceRevisionInInformationalVersion=false, so a release stamps no revision and this is null; every
    /// other build -- a local one, a rehearsal, a build of a pull request -- is made in a git checkout and carries
    /// one. About shows it, which is what tells a bug report which build it was filed against (issue #141).</summary>
    public static string? Commit { get; } = ShortCommit(Informational);

    internal static string? ShortCommit(string? informational)
    {
        var plus = informational?.IndexOf('+') ?? -1;
        if (plus < 0)
        {
            return null;
        }

        var revision = informational![(plus + 1)..];
        const int shortLength = 7;
        if (revision.Length < shortLength)
        {
            return null;
        }

        var shortened = revision[..shortLength];
        return shortened.All(Uri.IsHexDigit) ? shortened : null;
    }

    private static string Read()
    {
        var version = string.IsNullOrWhiteSpace(Informational) ? typeof(AppVersion).Assembly.GetName().Version?.ToString(3) ?? "0.0.0" : Informational;
        var plus = version.IndexOf('+');
        return plus >= 0 ? version[..plus] : version;
    }
}
