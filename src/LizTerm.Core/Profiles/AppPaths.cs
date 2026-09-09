// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

namespace LizTerm.Core.Profiles;

/// <summary>Per-OS locations of LizTerm's own files: profiles and wire logs live side by side under one root.</summary>
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
}
