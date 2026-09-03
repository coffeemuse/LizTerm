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
}
