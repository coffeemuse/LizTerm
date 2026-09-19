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

    /// <summary>Creates a directory owner-only on Unix, and does nothing when it is already there. Wire logs live
    /// in one of these: they hold every keystroke and every screen the host painted, and their file names carry
    /// profile names, so neither the contents nor the listing is other accounts' business (#139). Stub-created by
    /// .NET as 0755 otherwise. An existing directory keeps whatever mode it has — that one is the user's to set.
    /// Windows has no Unix mode and inherits the parent's ACL, which is what it did before.</summary>
    public static void EnsureDirectory(string path)
    {
        if (Directory.Exists(path)) return;
        if (OperatingSystem.IsWindows())
            Directory.CreateDirectory(path);
        else
            Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
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

    /// <summary>The hosts typed into Quick Connect, newest first, one file beside tags.json. A history rather than a
    /// preference, which is why it is not a key in settings.json; deleting it only empties the drop-down.</summary>
    public static string RecentHostsFile() => Path.Combine(ConfigRoot(), "recent-hosts.json");

    /// <summary>The user's keyboard bindings, one file beside settings.json (#18). Holds only the chords the user
    /// changed (see LizTerm.Core.Settings.KeymapFile), so deleting it restores every default.</summary>
    public static string KeymapFile() => Path.Combine(ConfigRoot(), "keymap.json");
}
