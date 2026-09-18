// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.Core.Profiles;
using LizTerm.Core.Settings;

namespace LizTerm.Core.Tests.Profiles;

public class AppPathsTests
{
    [Fact]
    public void Profiles_and_logs_are_siblings_under_the_config_root()
    {
        var root = AppPaths.ConfigRoot();
        Assert.Equal("LizTerm", Path.GetFileName(root));
        Assert.Equal(Path.Combine(root, "profiles"), AppPaths.ProfilesDirectory());
        Assert.Equal(Path.Combine(root, "logs"), AppPaths.LogsDirectory());
        Assert.Equal(AppPaths.ProfilesDirectory(), ProfileStore.DefaultDirectory());
        Assert.Equal(Path.Combine(root, "settings.json"), AppPaths.SettingsFile());
        Assert.Equal(AppPaths.SettingsFile(), SettingsStore.DefaultFile());
        Assert.Equal(Path.Combine(root, "recent-hosts.json"), AppPaths.RecentHostsFile());
        Assert.Equal(AppPaths.RecentHostsFile(), RecentHostsStore.DefaultFile());
    }

    [Fact]
    public void Root_follows_the_host_os_convention()
    {
        var root = AppPaths.ConfigRoot();
        if (OperatingSystem.IsMacOS())
            Assert.EndsWith(Path.Combine("Library", "Application Support", "LizTerm"), root);
        else if (OperatingSystem.IsWindows())
            Assert.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), root);
        else
            Assert.True(root.EndsWith(Path.Combine(".config", "LizTerm")) || root.StartsWith(Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") ?? "\0"), root);
    }

    /// <summary>Wire logs sit in this directory and hold every keystroke and every screen the host painted; their
    /// file names carry profile names. Other accounts on a shared machine have no business reading or listing
    /// either (#139).</summary>
    [Fact]
    public void EnsureDirectory_creates_an_owner_only_directory_on_unix()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Unix file modes do not apply on Windows.");
        var path = Path.Combine(Path.GetTempPath(), "lizterm-tests-" + Guid.NewGuid());
        try
        {
            AppPaths.EnsureDirectory(path);
            Assert.True(Directory.Exists(path));
            // The SkipWhen above already ended the test on Windows; CA1416 cannot see that, and CI builds
            // with -warnaserror, so the platform check is spelled out where the analyzer can read it.
            if (!OperatingSystem.IsWindows())
                Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(path));
        }
        finally
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
    }

    /// <summary>Called on every wire log start, so it must be happy with a directory that is already there —
    /// and must leave a mode the user chose for themselves alone.</summary>
    [Fact]
    public void EnsureDirectory_is_content_with_a_directory_that_already_exists()
    {
        var path = Directory.CreateTempSubdirectory().FullName;
        try
        {
            AppPaths.EnsureDirectory(path);
            AppPaths.EnsureDirectory(path);
            Assert.True(Directory.Exists(path));
        }
        finally
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
