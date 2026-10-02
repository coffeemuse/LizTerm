// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using LizTerm.App.Controls;
using LizTerm.App.Rendering;
using LizTerm.Core.Screen;
using LizTerm.Core.Session;

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

    /// <summary>#198: a model 4 starts on its 24x80 default screen, and TSO's logon switches it to the 43x80
    /// alternate one. The cells are fitted to the alternate size throughout, so the switch keeps their size, and the
    /// default screen sits at the top of the same area, where x3270 draws it.</summary>
    [AvaloniaFact]
    public void A_switch_between_the_default_and_alternate_screens_keeps_the_cell_size()
    {
        var screen = new TerminalScreen { AlternateSize = new ScreenSize(43, 80), Snapshot = ScreenSnapshot.Empty(24, 80) };
        var window = new Window { Width = 800, Height = 600, Content = screen };
        window.Show();
        var onDefault = screen.LastGeometry;

        screen.Snapshot = ScreenSnapshot.Empty(43, 80);
        window.UpdateLayout();

        Assert.Equal(onDefault, screen.LastGeometry);
        Assert.True(onDefault.CellHeight * 43 <= 600.01, $"height {onDefault.CellHeight * 43}");
        Assert.Equal((0, 0), onDefault.HitTest(onDefault.OriginX + 1, onDefault.OriginY + 1, 24, 80));
    }

    /// <summary>The alternate size is a floor, never a clip: a screen larger than it is still drawn whole.</summary>
    [AvaloniaFact]
    public void A_screen_larger_than_the_alternate_size_is_still_fitted_whole()
    {
        var screen = new TerminalScreen { AlternateSize = new ScreenSize(24, 80), Snapshot = ScreenSnapshot.Empty(27, 132) };
        var window = new Window { Width = 800, Height = 600, Content = screen };
        window.Show();

        var g = screen.LastGeometry;
        Assert.True(g.CellWidth * 132 <= 800.01, $"width {g.CellWidth * 132}");
        Assert.True(g.CellHeight * 27 <= 600.01, $"height {g.CellHeight * 27}");
    }

    /// <summary>A window sized to its content opens around the screen at the preferred size (#198), so the screen
    /// fills it with no bands.</summary>
    [AvaloniaFact]
    public void A_window_sized_to_the_screen_holds_the_alternate_screen_at_the_preferred_size()
    {
        var screen = new TerminalScreen { AlternateSize = new ScreenSize(27, 132), Snapshot = ScreenSnapshot.Empty(24, 80) };
        var window = new Window { SizeToContent = SizeToContent.WidthAndHeight, Content = screen };
        window.Show();
        // Show sizes the window after its first layout pass; the arrange at that size is the next pass.
        window.UpdateLayout();

        var g = screen.LastGeometry;
        Assert.Equal(TerminalScreen.PreferredFontSize, g.FontSize);
        Assert.Equal(Math.Ceiling(g.CellWidth * 132) + 1, window.ClientSize.Width);
        Assert.Equal(Math.Ceiling(g.CellHeight * 27) + 1, window.ClientSize.Height);
    }

    /// <summary>Offered less room than the preferred size needs, the screen asks for the largest size that fits,
    /// so a window capped at the display's height still opens snug around it.</summary>
    [AvaloniaFact]
    public void Offered_too_little_room_it_asks_for_the_largest_size_that_fits()
    {
        var screen = new TerminalScreen { AlternateSize = new ScreenSize(43, 80), Snapshot = ScreenSnapshot.Empty(24, 80) };
        var window = new Window { SizeToContent = SizeToContent.WidthAndHeight, MaxHeight = 400, Content = screen };
        window.Show();
        window.UpdateLayout();

        var g = screen.LastGeometry;
        Assert.True(g.FontSize < TerminalScreen.PreferredFontSize, $"font {g.FontSize}");
        Assert.True(g.CellHeight * 43 <= 400.01, $"height {g.CellHeight * 43}");
        Assert.Equal(Math.Ceiling(g.CellWidth * 80) + 1, window.ClientSize.Width);
    }

    /// <summary>While its window is sizing itself to it, the screen keeps the size it asked for whatever it is
    /// arranged at: Avalonia arranges a window's content once before showing it, at whatever size the platform gave
    /// the window first. Let go, it fits the arrange again.</summary>
    [AvaloniaFact]
    public void Keeping_the_requested_size_ignores_the_arrange_until_let_go()
    {
        var screen = new TerminalScreen { AlternateSize = new ScreenSize(43, 80), Snapshot = ScreenSnapshot.Empty(24, 80), KeepsRequestedSize = true };
        screen.Measure(Size.Infinity);
        screen.Arrange(new Rect(0, 0, 400, 300));
        Assert.Equal(TerminalScreen.PreferredFontSize, screen.LastGeometry.FontSize);
        Assert.Equal(TerminalScreen.PreferredFontSize, screen.OiaFontSize);

        screen.KeepsRequestedSize = false;
        screen.Arrange(new Rect(0, 0, 400, 300));
        Assert.True(screen.LastGeometry.FontSize < TerminalScreen.PreferredFontSize, $"font {screen.LastGeometry.FontSize}");
        Assert.True(screen.LastGeometry.CellHeight * 43 <= 300.01);
    }

    [AvaloniaFact]
    public void With_no_screen_it_asks_for_no_room()
    {
        var screen = new TerminalScreen();
        screen.Measure(Size.Infinity);
        Assert.Equal(default, screen.DesiredSize);
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

    /// <summary>Cell sizes are whole pixels, so a resize moves them one size at a time; the bar has to take every one
    /// of those steps. Holding any one-size move once left it a size off the screen for as long as the window kept
    /// its size. This window has no bar, so nothing here feeds back.</summary>
    [AvaloniaFact]
    public void The_status_bar_size_takes_every_step_the_cells_take()
    {
        // A headless window ignores a Height set after Show, so the resize is a sized host inside it.
        var screen = new TerminalScreen { Snapshot = ScreenSnapshot.Empty(24, 80) };
        var host = new Border { Height = 700, Child = screen };
        var window = new Window { Width = 2000, Height = 1000, Content = host };
        window.Show();
        var steps = new List<double>();
        var previous = screen.LastGeometry.FontSize;
        for (var height = 700; height >= 300; height -= 3)
        {
            host.Height = height;
            window.UpdateLayout();
            Assert.Equal(screen.LastGeometry.FontSize, screen.OiaFontSize);
            if (screen.LastGeometry.FontSize != previous) steps.Add(previous - screen.LastGeometry.FontSize);
            previous = screen.LastGeometry.FontSize;
        }
        Assert.Contains(1, steps);
    }

    /// <summary>With a bar whose height follows the size, as the session window's does, the fit feeds back on itself.
    /// Layout still settles at every height, and a host repaint — which re-arranges the screen at the same fit —
    /// leaves the bar where it is rather than flipping it. The bar may sit a size off the cells only where no size
    /// would settle: where a bar drawn at the cells' own size would re-fit them to another.</summary>
    [AvaloniaFact]
    public void A_bar_that_follows_the_size_settles_at_every_height_and_matches_wherever_it_can()
    {
        var screen = new TerminalScreen { Snapshot = ScreenSnapshot.Empty(24, 80) };
        var bar = new TextBlock { Text = "X", FontFamily = TerminalScreen.TerminalFont };
        bar.Bind(TextBlock.FontSizeProperty, screen.GetObservable(TerminalScreen.OiaFontSizeProperty));
        DockPanel.SetDock(bar, Dock.Bottom);
        var panel = new DockPanel { Height = 700, Children = { bar, screen } };
        var window = new Window { Width = 2000, Height = 1000, Content = panel };
        window.Show();
        for (var height = 700; height >= 300; height--)
        {
            panel.Height = height;
            window.UpdateLayout();
            var settled = screen.OiaFontSize;
            var cells = screen.LastGeometry;
            if (settled != cells.FontSize)
            {
                Assert.True(Math.Abs(settled - cells.FontSize) <= 1, $"height {height}: bar {settled}, cells {cells.FontSize}");
                Assert.True(FitWithBarAt(cells.FontSize, height, cells) != cells.FontSize,
                    $"height {height}: bar {settled}, but a bar at {cells.FontSize} would have settled");
            }

            screen.Snapshot = ScreenSnapshot.Empty(24, 80);
            window.UpdateLayout();
            Assert.Equal(settled, screen.OiaFontSize);
            Assert.Equal(settled, bar.FontSize);
        }
    }

    /// <summary>The cells' size if the bar were drawn at <paramref name="barFontSize"/>: the panel less that bar.</summary>
    private static double FitWithBarAt(double barFontSize, double panelHeight, CellGeometry cells)
    {
        var bar = new TextBlock { Text = "X", FontFamily = TerminalScreen.TerminalFont, FontSize = barFontSize };
        bar.Measure(Size.Infinity);
        return CellGeometry.Fit(2000, panelHeight - bar.DesiredSize.Height, 24, 80,
            cells.CellWidth / cells.FontSize, cells.CellHeight / cells.FontSize).FontSize;
    }
}
