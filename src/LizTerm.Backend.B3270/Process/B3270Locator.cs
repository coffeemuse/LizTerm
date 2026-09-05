using System.Runtime.InteropServices;
using LizTerm.Core.Session;

namespace LizTerm.Backend.B3270.Process;

/// <summary>A resolved b3270 binary. Anything shipped inside the app (the runtimes folder or a binary beside
/// the executable, which is also what an app bundle's contents will be) is Bundled; the environment variable is
/// an Override.</summary>
public sealed record B3270Location(string Path, EngineSource Source)
{
    /// <summary>For tests that never spawn a real process.</summary>
    public static readonly B3270Location Unknown = new("", EngineSource.Bundled);
}

public static class B3270Locator
{
    public const string EnvironmentOverride = "LIZTERM_B3270_PATH";

    public static string FileName => OperatingSystem.IsWindows() ? "b3270.exe" : "b3270";

    public static B3270Location Find() =>
        Find(Environment.GetEnvironmentVariable(EnvironmentOverride), AppContext.BaseDirectory);

    public static B3270Location Find(string? overridePath, string baseDirectory)
    {
        var candidates = new List<B3270Location>();
        if (!string.IsNullOrWhiteSpace(overridePath)) candidates.Add(new B3270Location(overridePath, EngineSource.Override));
        candidates.Add(new B3270Location(Path.Combine(baseDirectory, "runtimes", RuntimeInformation.RuntimeIdentifier, "native", FileName), EngineSource.Bundled));
        candidates.Add(new B3270Location(Path.Combine(baseDirectory, FileName), EngineSource.Bundled));

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
