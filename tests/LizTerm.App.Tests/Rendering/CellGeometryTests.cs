using LizTerm.App.Rendering;

namespace LizTerm.App.Tests.Rendering;

public class CellGeometryTests
{
    // A hypothetical font: advance 0.6 em, line height 1.2 em.
    private const double Advance = 0.6, Line = 1.2;

    [Fact]
    public void Fit_is_limited_by_width_when_window_is_wide_and_short()
    {
        var g = CellGeometry.Fit(800, 600, 24, 80, Advance, Line);
        Assert.Equal(16, g.FontSize);                 // floor(min(800/48, 600/28.8)) = floor(min(16.67, 20.83))
        Assert.Equal(9.6, g.CellWidth, 6);
        Assert.Equal(19.2, g.CellHeight, 6);
        Assert.Equal(16, g.OriginX, 6);               // (800 - 768) / 2
        Assert.Equal(69.6, g.OriginY, 6);             // (600 - 460.8) / 2
    }

    [Fact]
    public void Fit_is_limited_by_height_when_window_is_tall()
    {
        var g = CellGeometry.Fit(400, 1000, 24, 80, Advance, Line);
        Assert.Equal(8, g.FontSize);                  // floor(min(400/48, 1000/28.8)) = floor(8.33)
    }

    [Fact]
    public void Fit_never_goes_below_one_point()
    {
        var g = CellGeometry.Fit(10, 10, 24, 80, Advance, Line);
        Assert.Equal(1, g.FontSize);
    }

    [Fact]
    public void Fit_with_invalid_input_is_default()
    {
        Assert.Equal(default, CellGeometry.Fit(800, 600, 0, 80, Advance, Line));
        Assert.Equal(default, CellGeometry.Fit(800, 600, 24, 80, 0, Line));
    }

    [Fact]
    public void HitTest_maps_points_to_cells_and_rejects_margins()
    {
        var g = CellGeometry.Fit(800, 600, 24, 80, Advance, Line);
        Assert.Equal((3, 5), g.HitTest(16 + 9.6 * 5 + 1, 69.6 + 19.2 * 3 + 1, 24, 80));
        Assert.Equal((0, 0), g.HitTest(16.5, 70, 24, 80));
        Assert.Null(g.HitTest(2, 300, 24, 80));       // left margin
        Assert.Null(g.HitTest(400, 5, 24, 80));       // top margin
        Assert.Null(g.HitTest(799, 599, 24, 80));     // bottom-right margin
    }

    [Fact]
    public void CellRect_places_cells_on_the_grid()
    {
        var g = CellGeometry.Fit(800, 600, 24, 80, Advance, Line);
        var rect = g.CellRect(2, 10);
        Assert.Equal(16 + 96, rect.X, 6);
        Assert.Equal(69.6 + 38.4, rect.Y, 6);
        Assert.Equal(9.6, rect.Width, 6);
    }
}
