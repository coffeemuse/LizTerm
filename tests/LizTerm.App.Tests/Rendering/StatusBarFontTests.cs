// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.App.Rendering;

namespace LizTerm.App.Tests.Rendering;

/// <summary>The status bar's glyphs take the screen's cell size. The bar's own resize can re-fit the cells; that echo
/// is followed like any change, unless it returns the cells inside the same layout pass to the size the bar just
/// left — a fit with no consistent answer, and the one thing held.</summary>
public class StatusBarFontTests
{
    [Fact]
    public void Starts_at_the_default_and_follows_the_first_fit()
    {
        var font = new StatusBarFont();
        Assert.Equal(StatusBarFont.Default, font.Size);
        Assert.Equal(15, font.Follow(15));
    }

    [Fact]
    public void Follows_a_one_size_change_once_layout_has_settled()
    {
        var font = new StatusBarFont();
        font.Follow(16);
        font.LayoutSettled();
        Assert.Equal(15, font.Follow(15));
        font.LayoutSettled();
        Assert.Equal(16, font.Follow(16));
    }

    [Fact]
    public void Follows_an_echo_that_settles()
    {
        var font = new StatusBarFont();
        Assert.Equal(28, font.Follow(28)); // the first fit
        Assert.Equal(27, font.Follow(27)); // the bar's jump from the default re-fit the cells a size smaller, same pass
    }

    [Fact]
    public void Holds_a_bounce_back_to_the_size_it_just_left_and_keeps_holding_it_on_repaint()
    {
        var font = new StatusBarFont();
        font.Follow(16);
        font.LayoutSettled();
        Assert.Equal(17, font.Follow(17)); // the window grew a size
        Assert.Equal(17, font.Follow(16)); // the taller bar re-fit the cells back to 16 in the same pass: no answer settles
        font.LayoutSettled();
        Assert.Equal(17, font.Follow(16)); // a host repaint re-arranges at the same fit: no flip
    }

    [Fact]
    public void Lets_go_of_a_hold_once_the_cells_move_on()
    {
        var font = new StatusBarFont();
        font.Follow(16);
        font.LayoutSettled();
        font.Follow(17);
        font.Follow(16);
        font.LayoutSettled();
        Assert.Equal(15, font.Follow(15));
    }

    [Fact]
    public void Only_a_one_size_return_inside_the_pass_is_a_bounce()
    {
        var twoBack = new StatusBarFont();
        twoBack.Follow(16);
        twoBack.LayoutSettled();
        twoBack.Follow(18);
        Assert.Equal(16, twoBack.Follow(16));

        var afterSettling = new StatusBarFont();
        afterSettling.Follow(16);
        afterSettling.LayoutSettled();
        afterSettling.Follow(17);
        afterSettling.LayoutSettled();
        Assert.Equal(16, afterSettling.Follow(16)); // the window shrank back: a real change
    }

    [Fact]
    public void Ignores_a_screen_with_no_geometry_yet() =>
        Assert.Equal(StatusBarFont.Default, new StatusBarFont().Follow(0));
}
