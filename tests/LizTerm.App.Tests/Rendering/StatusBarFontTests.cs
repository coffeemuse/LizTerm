// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.App.Rendering;

namespace LizTerm.App.Tests.Rendering;

/// <summary>The status bar's glyphs take the screen's cell size, but a taller bar shrinks the screen, which can
/// re-fit the cells a pixel smaller, which shrinks the bar, which re-fits them a pixel larger: a layout that never
/// settles. So the bar follows the cells only when they move by more than a pixel.</summary>
public class StatusBarFontTests
{
    [Fact]
    public void Follows_a_cell_size_that_moved_by_more_than_a_pixel()
    {
        Assert.Equal(20, StatusBarFont.Follow(current: 14, cellFontSize: 20));
        Assert.Equal(18, StatusBarFont.Follow(current: 20, cellFontSize: 18));
    }

    [Fact]
    public void Holds_through_a_one_pixel_wobble()
    {
        Assert.Equal(20, StatusBarFont.Follow(current: 20, cellFontSize: 21));
        Assert.Equal(20, StatusBarFont.Follow(current: 20, cellFontSize: 19));
        Assert.Equal(20, StatusBarFont.Follow(current: 20, cellFontSize: 20));
    }

    [Fact]
    public void Ignores_a_screen_with_no_geometry_yet()
    {
        Assert.Equal(14, StatusBarFont.Follow(current: 14, cellFontSize: 0));
        Assert.Equal(14, StatusBarFont.Default);
    }
}
