// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.App.Rendering;

namespace LizTerm.App.Tests.Rendering;

public class PaletteTests
{
    /// <summary>Guards the choice TerminalScreen.DrawFindMatches makes for each match. Nothing else in the suite
    /// can see it: matches are asserted through FindMatchGeometry's rectangles, never through pixels, so
    /// inverting the ternary that used to live inline there, or assigning the same brush to both branches, would
    /// pass every other test unnoticed. FindMatchBrush exists so the choice itself is reachable.</summary>
    [Fact]
    public void The_current_match_gets_a_different_brush_from_the_rest()
    {
        Assert.Same(Palette.FindCurrent, Palette.FindMatchBrush(isCurrent: true));
        Assert.Same(Palette.FindMatch, Palette.FindMatchBrush(isCurrent: false));
        Assert.NotSame(Palette.FindMatchBrush(isCurrent: true), Palette.FindMatchBrush(isCurrent: false));
    }
}
