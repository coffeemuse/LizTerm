using System.Runtime.InteropServices;
using LizTerm.Core.Session;

namespace LizTerm.Backend.B3270.Process;

public static class B3270Locator
{
    public const string EnvironmentOverride = "LIZTERM_B3270_PATH";

    public static string FileName => OperatingSystem.IsWindows() ? "b3270.exe" : "b3270";

    public static string Find() =>
        Find(Environment.GetEnvironmentVariable(EnvironmentOverride), AppContext.BaseDirectory);

    public static string Find(string? overridePath, string baseDirectory)
    {
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(overridePath)) candidates.Add(overridePath);
        candidates.Add(Path.Combine(baseDirectory, "runtimes", RuntimeInformation.RuntimeIdentifier, "native", FileName));
        candidates.Add(Path.Combine(baseDirectory, FileName));

        foreach (var candidate in candidates)
        {
            if (!File.Exists(candidate)) continue;
            if (!OperatingSystem.IsWindows())
            {
                var mode = File.GetUnixFileMode(candidate);
                if ((mode & UnixFileMode.UserExecute) == 0)
                    throw new BackendUnavailableException(
                        $"The emulator engine at {candidate} is not executable. Run: chmod +x \"{candidate}\"");
            }
            return candidate;
        }

        throw new BackendUnavailableException(
            "The emulator engine (b3270) was not found. Looked in:\n  " + string.Join("\n  ", candidates) +
            $"\nSet {EnvironmentOverride} to a b3270 executable to override.");
    }
}
