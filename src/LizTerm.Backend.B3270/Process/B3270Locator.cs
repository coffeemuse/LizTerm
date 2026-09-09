// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Runtime.InteropServices;
using LizTerm.Core.Session;

namespace LizTerm.Backend.B3270.Process;

/// <summary>A resolved b3270 binary. Anything shipped inside the app (the runtimes folder or a binary beside
/// the executable, which is also what an app bundle's contents will be) is Bundled; the environment variable is
/// an Override.</summary>
public sealed record B3270Location(string Path, EngineSource Source)
{
    /// <summary>No binary was located: the production not-found path in <c>SessionFactory.Create</c> as well as
    /// tests that never spawn a process. Its source says so rather than borrowing one it does not have.</summary>
    public static readonly B3270Location Unknown = new("", EngineSource.Unknown);
}

public static class B3270Locator
{
    public const string EnvironmentOverride = "LIZTERM_B3270_PATH";

    public static string FileName => OperatingSystem.IsWindows() ? "b3270.exe" : "b3270";

    public static B3270Location Find() =>
        Find(Environment.GetEnvironmentVariable(EnvironmentOverride), AppContext.BaseDirectory);

    /// <summary>Where <see cref="Find"/> looks, in order. Public so a caller can tell "no binary anywhere" apart
    /// from "a binary is there and unusable", which <see cref="Find"/> reports with the same exception type.</summary>
    public static IReadOnlyList<B3270Location> Candidates(string? overridePath, string baseDirectory)
    {
        var candidates = new List<B3270Location>();
        if (!string.IsNullOrWhiteSpace(overridePath)) candidates.Add(new B3270Location(overridePath, EngineSource.Override));
        candidates.Add(new B3270Location(Path.Combine(baseDirectory, "runtimes", RuntimeInformation.RuntimeIdentifier, "native", FileName), EngineSource.Bundled));
        candidates.Add(new B3270Location(Path.Combine(baseDirectory, FileName), EngineSource.Bundled));
        return candidates;
    }

    /// <summary>The bundled path the csproj copy rule fills: <c>runtimes/&lt;rid&gt;/native/</c> under the base directory.</summary>
    public static string BundledDirectory => Path.Combine("runtimes", RuntimeInformation.RuntimeIdentifier, "native");

    public static B3270Location Find(string? overridePath, string baseDirectory)
    {
        var candidates = Candidates(overridePath, baseDirectory);

        foreach (var candidate in candidates)
        {
            if (!File.Exists(candidate.Path)) continue;
            if (!OperatingSystem.IsWindows())
            {
                var mode = File.GetUnixFileMode(candidate.Path);
                if ((mode & UnixFileMode.UserExecute) == 0)
                    throw new BackendUnavailableException(
                        $"The emulator engine at {candidate.Path} is not executable. Run: chmod +x \"{candidate.Path}\"");
            }
            return candidate;
        }

        throw new BackendUnavailableException(
            "The emulator engine (b3270) was not found. Looked in:\n  " + string.Join("\n  ", candidates.Select(c => c.Path)) +
            $"\nSet {EnvironmentOverride} to a b3270 executable to override.");
    }
}
