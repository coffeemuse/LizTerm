// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia;
using LizTerm.App.Rendering;
using LizTerm.Core.Screen;
using LizTerm.Core.Settings;

namespace LizTerm.App.Tests.Rendering;

public class CrosshairGeometryTests
{
    // 10-wide, 20-high cells at the origin: a 24x80 screen is 800x480.
    private static readonly CellGeometry Geometry = new(10, 20, 16, 0, 0);

    private static (Rect? Horizontal, Rect? Vertical) At(CrosshairMode mode, int row, int column, bool visible = true) =>
        CrosshairGeometry.Rects(mode, new CursorPosition(row, column, visible), Geometry, 24, 80);

    [Fact]
    public void None_draws_nothing()
    {
        var (horizontal, vertical) = At(CrosshairMode.None, 5, 12);

        Assert.Null(horizontal);
        Assert.Null(vertical);
    }

    /// <summary>A hairline on the cursor row's bottom edge, not a shaded cell: cell 5 spans y 100..120, so the
    /// line sits at 119. Full width still, because the ruler's job is to reach across all 80 columns.</summary>
    [Fact]
    public void Horizontal_is_a_hairline_on_the_cursor_row_s_bottom_edge()
    {
        var (horizontal, vertical) = At(CrosshairMode.Horizontal, 5, 12);

        Assert.Equal(new Rect(0, 119, 800, 1), horizontal);
        Assert.Null(vertical);
    }

    /// <summary>The vertical hairline sits on the cursor column's left edge — a cell boundary, which is what
    /// lining a column up wants — so column 12 of a 10-wide cell puts it at x 120.</summary>
    [Fact]
    public void Vertical_is_a_hairline_on_the_cursor_column_s_left_edge()
    {
        var (horizontal, vertical) = At(CrosshairMode.Vertical, 5, 12);

        Assert.Null(horizontal);
        Assert.Equal(new Rect(120, 0, 1, 480), vertical);
    }

    [Fact]
    public void Both_draws_both()
    {
        var (horizontal, vertical) = At(CrosshairMode.Both, 5, 12);

        Assert.Equal(new Rect(0, 119, 800, 1), horizontal);
        Assert.Equal(new Rect(120, 0, 1, 480), vertical);
    }

    /// <summary>A hairline stays a hairline: the thickness is a constant, not a fraction of the cell, so a
    /// full-screen 4K window does not get a fat bar back by another route.</summary>
    [Fact]
    public void The_lines_stay_one_thick_in_a_much_larger_cell()
    {
        var huge = new CellGeometry(40, 80, 64, 0, 0);

        var (horizontal, vertical) = CrosshairGeometry.Rects(
            CrosshairMode.Both, new CursorPosition(5, 12, true), huge, 24, 80);

        Assert.Equal(new Rect(0, 479, 3200, 1), horizontal);
        Assert.Equal(new Rect(480, 0, 1, 1920), vertical);
    }

    /// <summary>The origin is not always zero — a window wider than the grid centres it — and the lines have to
    /// start there rather than at the control's corner.</summary>
    [Fact]
    public void The_lines_start_at_the_grid_origin()
    {
        var offset = new CellGeometry(10, 20, 16, 7, 3);

        var (horizontal, vertical) = CrosshairGeometry.Rects(
            CrosshairMode.Both, new CursorPosition(5, 12, true), offset, 24, 80);

        Assert.Equal(new Rect(7, 122, 800, 1), horizontal);
        Assert.Equal(new Rect(127, 3, 1, 480), vertical);
    }

    /// <summary>The ruler's job is column alignment, not showing where input will land, so a hidden cursor
    /// still carries one. Vista draws its ruler regardless. See spec section 4.2.</summary>
    [Fact]
    public void It_follows_a_hidden_cursor()
    {
        var (horizontal, vertical) = At(CrosshairMode.Both, 5, 12, visible: false);

        Assert.NotNull(horizontal);
        Assert.NotNull(vertical);
    }

    /// <summary>A cursor past the edge of the grid is a screen that has just resized under a stale snapshot.
    /// Draw nothing rather than a bar off the side.</summary>
    [Theory]
    [InlineData(24, 12)]
    [InlineData(5, 80)]
    public void A_cursor_outside_the_grid_draws_nothing(int row, int column)
    {
        var (horizontal, vertical) = At(CrosshairMode.Both, row, column);

        Assert.Null(horizontal);
        Assert.Null(vertical);
    }

    /// <summary>A real cell is a fraction of a pixel tall — CellGeometry.Fit multiplies the font size by the
    /// font's own ratios — so an unrounded hairline straddles two rows of pixels at partial coverage and draws as
    /// a soft grey smear rather than a line. The run plan's underline rounds for the same reason.</summary>
    [Fact]
    public void The_lines_land_on_whole_pixels_when_the_cells_do_not()
    {
        var fractional = new CellGeometry(9.6, 19.4, 16, 0, 0);

        var (horizontal, vertical) = CrosshairGeometry.Rects(
            CrosshairMode.Both, new CursorPosition(5, 12, true), fractional, 24, 80);

        Assert.Equal(new Rect(0, 115, 768, 1), horizontal);
        Assert.Equal(new Rect(115, 0, 1, 466), vertical);
    }

    /// <summary>A whole DIP is a whole device pixel only where the render scaling is an integer, so on a 150%
    /// Windows display the snapping has to happen in device pixels or the smear it exists to prevent comes back.
    /// At 1.5, y 116.4 snaps to 116.667 (device row 175) and the line is two device pixels thick rather than one
    /// and a half; x 115.2 snaps to 115.333 (device column 173).</summary>
    [Fact]
    public void The_lines_land_on_whole_device_pixels_at_a_fractional_render_scaling()
    {
        var fractional = new CellGeometry(9.6, 19.4, 16, 0, 0);

        var (horizontal, vertical) = CrosshairGeometry.Rects(
            CrosshairMode.Both, new CursorPosition(5, 12, true), fractional, 24, 80, scaling: 1.5);

        Assert.NotNull(horizontal);
        Assert.NotNull(vertical);
        foreach (var edge in new[]
                 {
                     horizontal!.Value.Left, horizontal.Value.Top, horizontal.Value.Right, horizontal.Value.Bottom,
                     vertical!.Value.Left, vertical.Value.Top, vertical.Value.Right, vertical.Value.Bottom,
                 })
            Assert.Equal(Math.Round(edge * 1.5), edge * 1.5, precision: 9);

        Assert.Equal(2 / 1.5, horizontal.Value.Height, precision: 9);
        Assert.Equal(2 / 1.5, vertical.Value.Width, precision: 9);
    }

    /// <summary>The default scaling is the unscaled display's, so the rectangles above stay exactly what they
    /// were, and a nonsense scaling from a root that cannot answer is taken as that rather than dividing by it.
    /// </summary>
    [Fact]
    public void A_scaling_that_is_not_a_positive_number_is_taken_as_one()
    {
        var geometry = new CellGeometry(10, 20, 16, 0, 0);
        var cursor = new CursorPosition(5, 12, true);

        foreach (var scaling in new[] { 0, -1, double.NaN })
            Assert.Equal(
                CrosshairGeometry.Rects(CrosshairMode.Both, cursor, geometry, 24, 80),
                CrosshairGeometry.Rects(CrosshairMode.Both, cursor, geometry, 24, 80, scaling));
    }

    [Fact]
    public void An_unmeasured_geometry_draws_nothing()
    {
        var rects = CrosshairGeometry.Rects(CrosshairMode.Both, new CursorPosition(1, 1, true), default, 24, 80);

        Assert.Null(rects.Horizontal);
        Assert.Null(rects.Vertical);
    }
}
