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

    /// <summary>The same reachability argument as FindMatchBrush: the crosshair is asserted through
    /// CrosshairGeometry's rectangles, which cannot see which brush painted them. A mono (3278) screen draws its
    /// ruler in the phosphor rather than the colour screen's yellow, which matters now that the line is a
    /// near-opaque hairline instead of a barely-there wash (#180).</summary>
    [Fact]
    public void A_mono_screen_draws_the_crosshair_in_the_phosphor()
    {
        Assert.Same(Palette.Crosshair, Palette.CrosshairBrush(monochrome: false));

        var mono = Assert.IsAssignableFrom<ISolidColorBrush>(Palette.CrosshairBrush(monochrome: true)).Color;
        Assert.Equal(Palette.Phosphor.R, mono.R);
        Assert.Equal(Palette.Phosphor.G, mono.G);
        Assert.Equal(Palette.Phosphor.B, mono.B);
    }

    /// <summary>A hairline at the old 0x30 alpha would be all but invisible; the line no longer covers text, so
    /// it can afford to be nearly opaque (#180).</summary>
    [Fact]
    public void The_crosshair_is_opaque_enough_to_read_as_a_hairline()
    {
        foreach (var brush in new[] { Palette.CrosshairBrush(monochrome: false), Palette.CrosshairBrush(monochrome: true) })
        {
            var color = Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;
            Assert.True(color.A >= 0xA0, $"a hairline needs alpha, got 0x{color.A:X2}");
        }
    }
}
