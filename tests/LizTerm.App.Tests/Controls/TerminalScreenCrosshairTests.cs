// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using LizTerm.App.Controls;
using LizTerm.App.Rendering;
using LizTerm.Core.Screen;

namespace LizTerm.App.Tests.Controls;

public class TerminalScreenCrosshairTests
{
    private static (Window Window, TerminalScreen Screen) Show()
    {
        var screen = new TerminalScreen { Snapshot = ScreenSnapshot.Empty(24, 80) };
        var window = new Window { Width = 800, Height = 600, Content = screen };
        window.Show();
        return (window, screen);
    }

    /// <summary>Forces a frame. The headless render loop draws on demand (see TerminalScreenBlinkTests.Repaint),
    /// so asserting on RunPlanBuilds needs one after every change that should, or should not, cause a repaint.</summary>
    private static void Repaint(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();
    }

    [AvaloniaFact]
    public void It_defaults_to_none()
    {
        var (_, screen) = Show();

        Assert.Equal(CrosshairMode.None, screen.Crosshair);
    }

    /// <summary>The guard for the overlay rule (spec section 2). The crosshair is an overlay drawn after the
    /// cached run plan; changing it must not re-segment and re-shape every cell on the screen.</summary>
    [AvaloniaFact]
    public void Changing_the_crosshair_does_not_rebuild_the_run_plan()
    {
        var (window, screen) = Show();
        screen.Measure(new Size(800, 600));
        screen.Arrange(new Rect(0, 0, 800, 600));
        Repaint(window);
        var before = screen.RunPlanBuilds;
        Assert.True(before > 0, "the first render should have built a run plan");

        foreach (var mode in new[] { CrosshairMode.Horizontal, CrosshairMode.Vertical, CrosshairMode.Both })
        {
            screen.Crosshair = mode;
            screen.Measure(new Size(800, 600));
            screen.Arrange(new Rect(0, 0, 800, 600));
            Repaint(window);
        }

        Assert.Equal(before, screen.RunPlanBuilds);
    }
}
