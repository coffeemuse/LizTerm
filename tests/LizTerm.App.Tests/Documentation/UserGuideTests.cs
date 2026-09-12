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
            // Longer than the real page, so a non-truncating open mode would still leave detectable trailing
            // content behind: writing "stale" wouldn't catch that, since the real page is long enough to
            // overwrite it regardless of truncation.
            File.WriteAllText(path, new string('x', 100_000));

            Assert.Equal(path, UserGuide.Extract("9.9.9", directory));
            Assert.StartsWith("<!doctype html>", File.ReadAllText(path));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>The banner's link and ProjectLinks.UserGuide are two copies of the same URL in projects that
    /// cannot see each other: Core.Tests bakes one into the shipped page, App names the other for the menu and
    /// the "couldn't open it" fallback. App.Tests is the only project that can see both, so this is where a
    /// repository rename that updates one and not the other gets caught — otherwise the suite stays green while
    /// the banner it ships points at a 404.</summary>
    [AvaloniaFact]
    public void The_banner_link_matches_the_one_the_app_uses_elsewhere()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"lizterm-guide-test-{Guid.NewGuid():N}");
        try
        {
            var path = UserGuide.Extract("9.9.9", directory);
            Assert.Contains(ProjectLinks.UserGuide, File.ReadAllText(path));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}
