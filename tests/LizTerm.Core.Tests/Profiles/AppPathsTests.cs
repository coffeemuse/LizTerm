// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.Core.Profiles;

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
