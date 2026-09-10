// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using LizTerm.App.Controls;
using LizTerm.Core.Screen;

namespace LizTerm.App.Tests.Controls;

public class TerminalScreenFindTests
{
    private static (Window Window, TerminalScreen Screen) Show()
    {
        var screen = new TerminalScreen { Snapshot = ScreenSnapshot.Empty(24, 80) };
        var window = new Window { Width = 800, Height = 600, Content = screen };
        window.Show();
        return (window, screen);
    }

    [AvaloniaFact]
    public void It_starts_with_no_matches()
    {
        var (_, screen) = Show();

        Assert.Null(screen.FindMatches);
        Assert.Null(screen.CurrentMatch);
    }

    /// <summary>The guard for the overlay rule (spec section 2). Matches are recomputed on every host repaint,
    /// so this is the hottest overlay in the app: if it re-segmented and re-shaped every cell it would do so on
    /// every keystroke echo.</summary>
    [AvaloniaFact]
    public void Setting_matches_does_not_rebuild_the_run_plan()
    {
        var (window, screen) = Show();
        TestRender.Repaint(window);
        var before = screen.RunPlanBuilds;
        Assert.True(before > 0, "the first render should have built a run plan");

        screen.FindMatches = [ScreenRegion.FromCorners(1, 1, 1, 4), ScreenRegion.FromCorners(2, 0, 2, 3)];
        screen.CurrentMatch = ScreenRegion.FromCorners(2, 0, 2, 3);
        TestRender.Repaint(window);

        Assert.Equal(before, screen.RunPlanBuilds);
    }

    /// <summary>A match list computed against a larger screen outlives it by one repaint whenever the host
    /// changes screen size. Clamping is what keeps that from throwing.</summary>
    [AvaloniaFact]
    public void Matches_outside_the_screen_are_clamped_rather_than_thrown_on()
    {
        var (window, screen) = Show();
        screen.FindMatches = [ScreenRegion.FromCorners(90, 90, 95, 99)];

        var thrown = Record.Exception(() => TestRender.Repaint(window));

        Assert.Null(thrown);
    }
}
