// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

namespace LizTerm.Backend.B3270.Tests;

[Collection(EnvironmentCollection.Name)]
public class WireLogTests
{
    [Fact]
    public void TryFromEnvironment_returns_null_and_the_error_for_an_unwritable_path()
    {
        var original = Environment.GetEnvironmentVariable(WireLog.EnvironmentVariable);
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString(), "wire.log");
        try
        {
            Environment.SetEnvironmentVariable(WireLog.EnvironmentVariable, path);
            var log = WireLog.TryFromEnvironment(out var error);
            Assert.Null(log);
            Assert.NotNull(error);
        }
        finally
        {
            Environment.SetEnvironmentVariable(WireLog.EnvironmentVariable, original);
        }
    }

    [Fact]
    public void TryFromEnvironment_returns_null_without_error_when_unset()
    {
        var original = Environment.GetEnvironmentVariable(WireLog.EnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(WireLog.EnvironmentVariable, null);
            Assert.Null(WireLog.TryFromEnvironment(out var error));
            Assert.Null(error);
        }
        finally
        {
            Environment.SetEnvironmentVariable(WireLog.EnvironmentVariable, original);
        }
    }

    /// <summary>A wire log holds every keystroke and every screen the host painted, so it is the most sensitive
    /// file LizTerm writes and must not be readable by other accounts on a shared machine. The CA file next door
    /// is already owner-only and holds nothing but public certificates, which is the wrong way round (#139).</summary>
    [Fact]
    public void Path_constructor_creates_an_owner_only_file_on_unix()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Unix file modes do not apply on Windows.");
        var directory = Directory.CreateTempSubdirectory().FullName;
        var path = Path.Combine(directory, "wire.log");
        try
        {
            using (var log = new WireLog(path)) log.Outbound("{}");
            // The SkipWhen above already ended the test on Windows; CA1416 cannot see that, and CI builds
            // with -warnaserror, so the platform check is spelled out where the analyzer can read it.
            if (!OperatingSystem.IsWindows())
                Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Path_constructor_appends_both_directions_with_prefixes()
    {
        var path = Path.Combine(Path.GetTempPath(), "lizterm-wire-" + Guid.NewGuid().ToString("N") + ".log");
        try
        {
            using (var log = new WireLog(path))
            {
                Assert.Equal(path, log.Path);
                log.Outbound("{\"run\":1}");
                log.Inbound("{\"hello\":1}");
            }
            var lines = File.ReadAllLines(path);
            Assert.Equal(2, lines.Length);
            Assert.EndsWith(" > {\"run\":1}", lines[0]);
            Assert.EndsWith(" < {\"hello\":1}", lines[1]);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
