// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using LizTerm.App.Controls;
using LizTerm.Core.Screen;

namespace LizTerm.App.Tests.Controls;

public class TerminalScreenMonochromeTests
{
    /// <summary>The run plan bakes brushes in and is kept for as long as the snapshot and geometry stand, so a
    /// change of Monochrome on a screen already showing has to rebuild it, or the old colours would stay up until
    /// the host next painted. The window binds the property once, but the binding lands after the control's first
    /// frame in some orders, and that is exactly this case (#123).</summary>
    [AvaloniaFact]
    public void Changing_monochrome_rebuilds_the_run_plan()
    {
        var buffer = new ScreenBuffer(24, 80);
        buffer.SetText(1, 2, "READY", HostColor.Red, null, null);
        var screen = new TerminalScreen { Snapshot = buffer.Snapshot() };
        var window = new Window { Width = 800, Height = 600, Content = screen };
        window.Show();
        TestRender.Repaint(window);
        var before = screen.RunPlanBuilds;
        Assert.True(before > 0);

        screen.Monochrome = true;
        TestRender.Repaint(window);

        Assert.Equal(before + 1, screen.RunPlanBuilds);
    }

    [AvaloniaFact]
    public void Monochrome_is_off_by_default()
    {
        Assert.False(new TerminalScreen().Monochrome);
    }
}
