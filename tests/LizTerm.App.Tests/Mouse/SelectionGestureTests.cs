// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.App.Mouse;
using LizTerm.Core.Screen;

namespace LizTerm.App.Tests.Mouse;

public class SelectionGestureTests
{
    [Fact]
    public void Press_and_release_on_one_cell_is_a_click_with_no_region()
    {
        var g = new SelectionGesture();
        g.Press(3, 4);
        Assert.Equal(ReleaseResult.Click, g.Release());
        Assert.Null(g.Region);
    }

    [Fact]
    public void Press_move_release_is_a_drag_with_a_normalized_region()
    {
        var g = new SelectionGesture();
        g.Press(5, 10);
        g.Move(5, 12);
        g.Move(2, 3);
        Assert.Equal(ScreenRegion.FromCorners(2, 3, 5, 10), g.Region);
        Assert.Equal(ReleaseResult.Drag, g.Release());
        Assert.Equal(ScreenRegion.FromCorners(2, 3, 5, 10), g.Region);
    }

    [Fact]
    public void Moving_back_to_the_anchor_after_a_drag_keeps_a_one_cell_region()
    {
        var g = new SelectionGesture();
        g.Press(1, 1);
        g.Move(1, 5);
        g.Move(1, 1);
        Assert.Equal(ScreenRegion.FromCorners(1, 1, 1, 1), g.Region);
        Assert.Equal(ReleaseResult.Drag, g.Release());
    }

    [Fact]
    public void Jitter_inside_the_anchor_cell_is_still_a_click()
    {
        var g = new SelectionGesture();
        g.Press(1, 1);
        g.Move(1, 1);
        Assert.Null(g.Region);
        Assert.Equal(ReleaseResult.Click, g.Release());
    }

    [Fact]
    public void Move_and_release_without_a_press_are_ignored()
    {
        var g = new SelectionGesture();
        g.Move(2, 2);
        Assert.Null(g.Region);
        Assert.Equal(ReleaseResult.None, g.Release());
    }

    [Fact]
    public void Press_clears_an_existing_region()
    {
        var g = new SelectionGesture();
        g.Press(0, 0);
        g.Move(2, 2);
        g.Release();
        g.Press(9, 9);
        Assert.Null(g.Region);
    }

    [Fact]
    public void DoubleClick_selects_the_word_or_nothing_and_is_not_a_click()
    {
        var buffer = new ScreenBuffer(2, 20);
        buffer.SetText(1, 3, "hello world", null, null, null);
        var snap = buffer.Snapshot();
        var g = new SelectionGesture();

        g.DoubleClick(1, 5, snap);
        Assert.Equal(ScreenRegion.FromCorners(1, 3, 1, 7), g.Region);
        Assert.Equal(ReleaseResult.None, g.Release());

        g.DoubleClick(1, 8, snap);
        Assert.Null(g.Region);
    }
}
