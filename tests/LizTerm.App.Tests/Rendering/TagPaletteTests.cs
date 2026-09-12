// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Media;
using LizTerm.App.Rendering;
using LizTerm.Core.Profiles;

namespace LizTerm.App.Tests.Rendering;

public class TagPaletteTests
{
    /// <summary>Driven by Enum.GetValues so a colour added to TagColor without a brush fails the suite rather
    /// than rendering an invisible chip. Palette's HostColor map has the same shape and the same risk.</summary>
    [Fact]
    public void Every_TagColor_member_maps_to_a_brush()
    {
        foreach (var color in Enum.GetValues<TagColor>())
        {
            Assert.NotNull(TagPalette.Brush(color));
        }
    }

    [Fact]
    public void The_same_colour_returns_the_same_cached_brush()
    {
        Assert.Same(TagPalette.Brush(TagColor.Red), TagPalette.Brush(TagColor.Red));
    }

    /// <summary>The spec claims white chip text clears 4.5:1 on every chip, and an accessibility claim nothing
    /// checks is a claim that rots — especially once #78 adds a light theme and someone retunes these values.
    /// WCAG 2.1 relative luminance and contrast ratio.</summary>
    [Fact]
    public void Every_chip_colour_clears_4_5_to_1_against_white_text()
    {
        foreach (var color in Enum.GetValues<TagColor>())
        {
            var ratio = Contrast(Colors.White, TagPalette.ColorOf(color));
            Assert.True(ratio >= 4.5, $"{color} gives white text only {ratio:F2}:1");
        }
    }

    private static double Contrast(Color a, Color b)
    {
        var (high, low) = (Math.Max(Luminance(a), Luminance(b)), Math.Min(Luminance(a), Luminance(b)));
        return (high + 0.05) / (low + 0.05);
    }

    private static double Luminance(Color c) =>
        0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);

    private static double Channel(byte value)
    {
        var v = value / 255.0;
        return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
    }
}
