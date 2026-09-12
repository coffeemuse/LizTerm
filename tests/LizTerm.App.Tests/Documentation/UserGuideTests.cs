// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Headless.XUnit;
using LizTerm.App.Documentation;

namespace LizTerm.App.Tests.Documentation;

public class UserGuideTests
{
    [AvaloniaFact]
    public void The_guide_is_written_where_it_says_it_is()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"lizterm-guide-test-{Guid.NewGuid():N}");
        try
        {
            var path = UserGuide.Extract("9.9.9", directory);

            Assert.Equal(Path.Combine(directory, "lizterm-user-guide-9.9.9.html"), path);
            var written = File.ReadAllText(path);
            Assert.StartsWith("<!doctype html>", written);
            Assert.Contains("Offline copy, shipped with LizTerm", written);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Reopening Help overwrites one file rather than accumulating them, and a leftover that was
    /// truncated or edited is replaced rather than served.</summary>
    [AvaloniaFact]
    public void A_second_extraction_replaces_whatever_was_there()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"lizterm-guide-test-{Guid.NewGuid():N}");
        try
        {
            var path = UserGuide.Extract("9.9.9", directory);
            File.WriteAllText(path, "stale");

            Assert.Equal(path, UserGuide.Extract("9.9.9", directory));
            Assert.StartsWith("<!doctype html>", File.ReadAllText(path));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}
