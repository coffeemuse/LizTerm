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

    [Fact]
    public void Horizontal_spans_the_full_width_at_the_cursor_row()
    {
        var (horizontal, vertical) = At(CrosshairMode.Horizontal, 5, 12);

        Assert.Equal(new Rect(0, 100, 800, 20), horizontal);
        Assert.Null(vertical);
    }

    [Fact]
    public void Vertical_spans_the_full_height_at_the_cursor_column()
    {
        var (horizontal, vertical) = At(CrosshairMode.Vertical, 5, 12);

        Assert.Null(horizontal);
        Assert.Equal(new Rect(120, 0, 10, 480), vertical);
    }

    [Fact]
    public void Both_draws_both()
    {
        var (horizontal, vertical) = At(CrosshairMode.Both, 5, 12);

        Assert.Equal(new Rect(0, 100, 800, 20), horizontal);
        Assert.Equal(new Rect(120, 0, 10, 480), vertical);
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

    [Fact]
    public void An_unmeasured_geometry_draws_nothing()
    {
        var rects = CrosshairGeometry.Rects(CrosshairMode.Both, new CursorPosition(1, 1, true), default, 24, 80);

        Assert.Null(rects.Horizontal);
        Assert.Null(rects.Vertical);
    }
}
