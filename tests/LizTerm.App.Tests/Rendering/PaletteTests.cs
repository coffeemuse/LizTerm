// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Media;
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

    /// <summary>The one phosphor a mono (3278) screen is drawn in: green, with intensified text brighter, as the
    /// colour path blends. #78's themes override Phosphor and nothing else (#123).</summary>
    [Fact]
    public void The_phosphor_is_green_and_its_bright_brush_is_lighter()
    {
        var plain = Assert.IsAssignableFrom<ISolidColorBrush>(Palette.PhosphorBrush(bright: false)).Color;
        var bright = Assert.IsAssignableFrom<ISolidColorBrush>(Palette.PhosphorBrush(bright: true)).Color;
        Assert.Equal(Palette.Phosphor, plain);
        Assert.True(plain.G > plain.R && plain.G > plain.B, "the phosphor is green");
        Assert.True(bright.R > plain.R && bright.B > plain.B, "intensified is blended toward white");
        Assert.Same(Palette.PhosphorBrush(true), Palette.PhosphorBrush(true));
    }
}
