// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Reflection;

namespace LizTerm.App;

/// <summary>The app's version as the build stamped it (the Version property in Directory.Build.props), without the
/// "+commit" suffix the SDK appends to the informational version inside a git checkout, plus that commit on its own
/// and whether this build is a release, which is what <see cref="Display"/> puts together.</summary>
public static class AppVersion
{
    /// <summary>The AssemblyMetadata key the release publish writes (LizTerm.App.csproj, LizTermReleaseBuild).
    /// A build says it is a release; nothing infers it from what a build happens to lack.</summary>
    internal const string ReleaseMarkerKey = "LizTerm.ReleaseBuild";

    public static string Current { get; } = Read();

    /// <summary>The short commit this build came from, or null when nothing stamped one -- which is every build
    /// made outside a git checkout, a source tarball included, since the stamp comes from the SDK's source-control
    /// query rather than from the release pipeline. It says *which* build, never whether it is a release
    /// (issue #141).</summary>
    public static string? Commit { get; } = ShortCommit(Informational());

    /// <summary>True only for a build the release pipeline marked as one. Everything else -- a local build, a
    /// build of a pull request, a rehearsal, a build from a downloaded source tree -- is a test build, because
    /// the marker is written rather than deduced.</summary>
    public static bool IsRelease { get; } = MarkedAsRelease();

    /// <summary>The version as About and the splash show it: a release is its bare version, and anything else
    /// wears -DEV, with the commit after it when the build carries one.</summary>
    public static string Display => Describe(Current, Commit, IsRelease);

    internal static string Describe(string version, string? commit, bool isRelease) =>
        isRelease ? version
        : commit is null ? version + "-DEV"
        : $"{version}-DEV ({commit})";

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
        // Lowercase only, the one spelling a git object id has, so the app, AppVersionTests and the release
        // workflow's own check all agree on what counts as a commit.
        return shortened.All(char.IsAsciiHexDigitLower) ? shortened : null;
    }

    /// <summary>Read through a method rather than a shared static field: a field would have to be declared above
    /// every initializer that reads it, and getting that order wrong fails silently, with Read falling back to the
    /// assembly's own version -- a string that looks exactly right.</summary>
    private static string? Informational() =>
        typeof(AppVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

    private static bool MarkedAsRelease() =>
        typeof(AppVersion).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Any(a => a.Key == ReleaseMarkerKey && a.Value == "true");

    private static string Read()
    {
        var informational = Informational();
        var version = string.IsNullOrWhiteSpace(informational) ? typeof(AppVersion).Assembly.GetName().Version?.ToString(3) ?? "0.0.0" : informational;
        var plus = version.IndexOf('+');
        return plus >= 0 ? version[..plus] : version;
    }
}
