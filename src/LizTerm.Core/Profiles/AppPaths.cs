// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

namespace LizTerm.Core.Profiles;

/// <summary>Per-OS locations of LizTerm's own files: profiles, wire logs, the settings file and the tag registry
/// live side by side under one root.</summary>
public static class AppPaths
{
    public static string ConfigRoot()
    {
        string root;
        if (OperatingSystem.IsMacOS())
            root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support");
        else if (OperatingSystem.IsWindows())
            root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        else
            root = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME")
                   ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        return Path.Combine(root, "LizTerm");
    }

    public static string ProfilesDirectory() => Path.Combine(ConfigRoot(), "profiles");

    public static string LogsDirectory() => Path.Combine(ConfigRoot(), "logs");

    /// <summary>App-wide settings, one file beside profiles/ and logs/. Holds only the keys the user has set
    /// (see LizTerm.Core.Settings.SettingsLayers), so deleting it restores every default.</summary>
    public static string SettingsFile() => Path.Combine(ConfigRoot(), "settings.json");

    /// <summary>The tag registry, one file beside settings.json. Holds a colour per tag name; FAVORITE is
    /// synthesised rather than stored, so deleting this file loses only the chosen colours — every tag name
    /// travels in its profiles and re-registers with a fresh colour.</summary>
    public static string TagsFile() => Path.Combine(ConfigRoot(), "tags.json");
}
