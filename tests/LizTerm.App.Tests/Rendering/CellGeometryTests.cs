// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

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

    [Fact]
    public void NearestCell_matches_HitTest_inside_and_clamps_to_the_edge_outside()
    {
        var g = CellGeometry.Fit(800, 600, 24, 80, Advance, Line);
        Assert.Equal((3, 5), g.NearestCell(16 + 9.6 * 5 + 1, 69.6 + 19.2 * 3 + 1, 24, 80));
        Assert.Equal((11, 0), g.NearestCell(2, 69.6 + 19.2 * 11 + 5, 24, 80));   // left margin -> column 0
        Assert.Equal((0, 39), g.NearestCell(16 + 9.6 * 39 + 3, 5, 24, 80));      // top margin -> row 0
        Assert.Equal((23, 79), g.NearestCell(799, 599, 24, 80));                 // bottom-right margin
        Assert.Equal((0, 0), g.NearestCell(-50, -50, 24, 80));
        Assert.Null(default(CellGeometry).NearestCell(1, 1, 24, 80));
        Assert.Null(g.NearestCell(1, 1, 0, 80));
    }

    /// <summary>A grid at a given size is placed exactly as Fit places the size it chose, and one too large for the
    /// area starts at its top left rather than off it.</summary>
    [Fact]
    public void At_places_a_given_size_as_Fit_places_its_own()
    {
        Assert.Equal(CellGeometry.Fit(800, 600, 24, 80, Advance, Line), CellGeometry.At(16, 800, 600, 24, 80, Advance, Line));
        var tooLarge = CellGeometry.At(40, 800, 600, 24, 80, Advance, Line);
        Assert.Equal(40, tooLarge.FontSize);
        Assert.Equal(0, tooLarge.OriginX);
        Assert.Equal(0, tooLarge.OriginY);
        Assert.Equal(default, CellGeometry.At(0, 800, 600, 24, 80, Advance, Line));
    }

    /// <summary>What a screen asks the layout for (#198): the grid at that font size, rounded up to whole DIPs with
    /// one to spare, so the area it is given fits the same size back rather than the one below it, even once the
    /// platform has taken up to a device pixel off it.</summary>
    [Theory]
    [InlineData(24, 80, 22, 0.6, 1.2)]
    [InlineData(43, 80, 22, 0.6, 1.2)]
    [InlineData(27, 132, 22, 0.6, 1.2)]
    [InlineData(24, 80, 22, 0.54, 1.09)]
    [InlineData(43, 80, 17, 0.54, 1.09)]
    [InlineData(27, 132, 13, 0.54, 1.09)]
    [InlineData(50, 100, 9, 0.54, 1.09)]
    public void A_grid_s_size_at_a_font_size_fits_back_at_that_font_size(int rows, int columns, double fontSize, double advance, double line)
    {
        var size = CellGeometry.GridSize(rows, columns, fontSize, advance, line);
        Assert.Equal(Math.Ceiling(columns * advance * fontSize) + 1, size.Width);
        Assert.Equal(Math.Ceiling(rows * line * fontSize) + 1, size.Height);
        Assert.Equal(fontSize, CellGeometry.Fit(size.Width, size.Height, rows, columns, advance, line).FontSize);
        // A device pixel short at 125%, the platform truncating.
        Assert.Equal(fontSize, CellGeometry.Fit(size.Width - 0.8, size.Height - 0.8, rows, columns, advance, line).FontSize);
    }

    /// <summary>Products that land on a whole number exactly: the division back must not come out a hair under
    /// it and floor to the size below.</summary>
    [Fact]
    public void Fit_does_not_lose_a_size_to_floating_point_noise()
    {
        // The width is multiplied out in one order and divided back in another, as a layout pass does.
        for (var font = 1; font <= 72; font++)
        {
            var g = CellGeometry.Fit(80 * 0.54 * font, 24 * 1.09 * font, 24, 80, 0.54, 1.09);
            Assert.Equal(font, g.FontSize);
        }
    }
}
