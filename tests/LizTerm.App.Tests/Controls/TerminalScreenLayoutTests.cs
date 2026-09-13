// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using LizTerm.App.Controls;
using LizTerm.Core.Screen;

namespace LizTerm.App.Tests.Controls;

public class TerminalScreenLayoutTests
{
    [AvaloniaFact]
    public void Geometry_fits_the_grid_inside_the_window()
    {
        var screen = new TerminalScreen { Snapshot = ScreenSnapshot.Empty(24, 80) };
        var window = new Window { Width = 800, Height = 600, Content = screen };
        window.Show();

        var g = screen.LastGeometry;
        Assert.True(g.FontSize > 0);
        Assert.True(g.CellWidth * 80 <= 800.01, $"width {g.CellWidth * 80}");
        Assert.True(g.CellHeight * 24 <= 600.01, $"height {g.CellHeight * 24}");
    }

    [AvaloniaFact]
    public void Changing_snapshot_size_recomputes_geometry()
    {
        var screen = new TerminalScreen { Snapshot = ScreenSnapshot.Empty(24, 80) };
        var window = new Window { Width = 800, Height = 600, Content = screen };
        window.Show();
        var before = screen.LastGeometry;
        screen.Snapshot = ScreenSnapshot.Empty(43, 80);
        window.UpdateLayout();
        Assert.True(screen.LastGeometry.CellHeight * 43 <= 600.01);
        Assert.NotEqual(before, screen.LastGeometry);
    }

    /// <summary>The status bar draws its OIA glyphs at the screen's cell size (a real 3270's OIA is one more row
    /// of the same cells), so the control publishes it; the bar binds to it.</summary>
    [AvaloniaFact]
    public void Publishes_its_cell_font_size_for_the_status_bar()
    {
        var screen = new TerminalScreen { Snapshot = ScreenSnapshot.Empty(24, 80) };
        var window = new Window { Width = 800, Height = 600, Content = screen };
        window.Show();
        Assert.Equal(screen.LastGeometry.FontSize, screen.OiaFontSize);

        screen.Snapshot = ScreenSnapshot.Empty(43, 80);
        window.UpdateLayout();
        Assert.Equal(screen.LastGeometry.FontSize, screen.OiaFontSize);
    }
}
