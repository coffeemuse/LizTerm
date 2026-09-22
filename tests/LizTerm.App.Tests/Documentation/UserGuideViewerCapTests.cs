// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Globalization;
using System.Runtime.CompilerServices;
using LizTerm.App.ViewModels;

namespace LizTerm.App.Tests.Documentation;

/// <summary>Holds the user guide's stated viewer cap to MvsmfViewerViewModel.MaxLines (viewer spec §6.4), the
/// UserGuideKeyboardTableTests precedent for a number written out in two homes with nothing tying them together.</summary>
public class UserGuideViewerCapTests
{
    [Fact]
    public void The_guide_names_the_same_cap_as_MaxLines()
    {
        var cap = MvsmfViewerViewModel.MaxLines.ToString("N0", CultureInfo.InvariantCulture);
        Assert.Contains($"first {cap} lines", File.ReadAllText(GuidePath()));
    }

    private static string GuidePath([CallerFilePath] string path = "")
    {
        var dir = Path.GetDirectoryName(path);
        while (dir is not null && !File.Exists(Path.Combine(dir, "LizTerm.slnx"))) dir = Path.GetDirectoryName(dir);
        return Path.Combine(dir ?? throw new InvalidOperationException($"No LizTerm.slnx above '{path}'."), "docs", "user-guide.md");
    }
}
