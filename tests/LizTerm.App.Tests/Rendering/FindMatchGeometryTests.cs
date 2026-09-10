// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia;
using LizTerm.App.Rendering;
using LizTerm.Core.Screen;

namespace LizTerm.App.Tests.Rendering;

public class FindMatchGeometryTests
{
    // 10-wide, 20-high cells at the origin: a 24x80 screen is 800x480. Same grid as CrosshairGeometryTests.
    private const int Rows = 24;
    private const int Columns = 80;
    private static readonly CellGeometry Geometry = new(10, 20, 16, 0, 0);

    [Fact]
    public void A_fully_in_bounds_region_gives_the_exact_rectangle()
    {
        var region = ScreenRegion.FromCorners(5, 10, 5, 15);

        var rect = FindMatchGeometry.Rect(region, Geometry, Rows, Columns);

        // Row 5 starts at y = 5 * 20 = 100; columns 10..15 span x = 100 .. 160 (six cells, 10 wide each).
        Assert.Equal(new Rect(100, 100, 60, 20), rect);
    }

    [Fact]
    public void A_region_partly_off_the_screen_gives_the_clamped_rectangle()
    {
        // Row 23 is the last row; columns 75..85 run five past the last column (79).
        var region = ScreenRegion.FromCorners(23, 75, 23, 85);

        var rect = FindMatchGeometry.Rect(region, Geometry, Rows, Columns);

        // Clamped to columns 75..79: x = 750 .. 800, y = 23 * 20 = 460 .. 480.
        Assert.Equal(new Rect(750, 460, 50, 20), rect);
    }

    /// <summary>A match list computed against a larger screen outlives it by one repaint whenever the host
    /// changes screen size. This is the same off-screen region the deleted TerminalScreen-level test used.</summary>
    [Fact]
    public void A_region_entirely_off_the_screen_gives_null()
    {
        var region = ScreenRegion.FromCorners(90, 90, 95, 99);

        var rect = FindMatchGeometry.Rect(region, Geometry, Rows, Columns);

        Assert.Null(rect);
    }

    [Fact]
    public void An_unmeasured_geometry_gives_null()
    {
        var region = ScreenRegion.FromCorners(1, 1, 1, 4);

        var rect = FindMatchGeometry.Rect(region, default, Rows, Columns);

        Assert.Null(rect);
    }
}
