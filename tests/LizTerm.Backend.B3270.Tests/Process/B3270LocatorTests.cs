// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.Backend.B3270.Process;
using LizTerm.Core.Session;

namespace LizTerm.Backend.B3270.Tests.Process;

public class B3270LocatorTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lizterm-loc-" + Guid.NewGuid().ToString("N"));

    public B3270LocatorTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string MakeExecutable(string relative)
    {
        var path = Path.Combine(_dir, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "#!/bin/sh\n");
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return path;
    }

    [Fact]
    public void Override_path_wins_and_is_reported_as_override()
    {
        var path = MakeExecutable("custom/b3270");
        Assert.Equal(new B3270Location(path, EngineSource.Override), B3270Locator.Find(path, _dir));
    }

    [Fact]
    public void Finds_runtime_native_folder_as_bundled()
    {
        var rid = System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier;
        var path = MakeExecutable(Path.Combine("runtimes", rid, "native", B3270Locator.FileName));
        Assert.Equal(new B3270Location(path, EngineSource.Bundled), B3270Locator.Find(null, _dir));
    }

    [Fact]
    public void Falls_back_to_base_directory_as_bundled()
    {
        var path = MakeExecutable(B3270Locator.FileName);
        Assert.Equal(new B3270Location(path, EngineSource.Bundled), B3270Locator.Find(null, _dir));
    }

    [Fact]
    public void Missing_binary_reports_where_it_looked()
    {
        var ex = Assert.Throws<BackendUnavailableException>(() => B3270Locator.Find(null, _dir));
        Assert.Contains(_dir, ex.Message);
        Assert.Contains("LIZTERM_B3270_PATH", ex.Message);
    }

    [Fact]
    public void Non_executable_binary_is_reported_distinctly()
    {
        if (OperatingSystem.IsWindows()) return;
        var path = Path.Combine(_dir, "b3270");
        File.WriteAllText(path, "");
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        var ex = Assert.Throws<BackendUnavailableException>(() => B3270Locator.Find(null, _dir));
        Assert.Contains("chmod +x", ex.Message);
    }
}
