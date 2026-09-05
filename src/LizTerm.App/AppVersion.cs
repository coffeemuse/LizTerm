using System.Reflection;

namespace LizTerm.App;

/// <summary>The app's version as the build stamped it (the Version property in Directory.Build.props), without the
/// "+commit" suffix the SDK appends to the informational version inside a git checkout.</summary>
public static class AppVersion
{
    public static string Current { get; } = Read();

    private static string Read()
    {
        var informational = typeof(AppVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var version = string.IsNullOrWhiteSpace(informational) ? typeof(AppVersion).Assembly.GetName().Version?.ToString(3) ?? "0.0.0" : informational;
        var plus = version.IndexOf('+');
        return plus >= 0 ? version[..plus] : version;
    }
}
