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
}
