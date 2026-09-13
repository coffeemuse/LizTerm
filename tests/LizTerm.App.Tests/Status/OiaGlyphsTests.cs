// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Headless.XUnit;
using Avalonia.Platform;
using LizTerm.App.Status;
using SkiaSharp;

namespace LizTerm.App.Tests.Status;

/// <summary>The x3270 OIA symbols live in the bundled font as glyphs with no code point; tools/patch-3270-oia-font.py
/// gives them private-use ones. A font refresh that forgets the patch would silently turn the status bar into
/// boxes, so this pins every code point <see cref="OiaGlyphs"/> uses to a real glyph in the shipped file.</summary>
public class OiaGlyphsTests
{
    [AvaloniaFact]
    public void Every_oia_code_point_has_a_glyph_in_the_bundled_font()
    {
        using var stream = AssetLoader.Open(new Uri("avares://LizTerm.App/Assets/Fonts/3270-Regular.otf"));
        using var typeface = SKTypeface.FromStream(stream);
        Assert.NotNull(typeface);
        var missing = OiaGlyphs.All.Where(cp => !typeface.ContainsGlyph(cp)).Select(cp => $"U+{cp:X4}").ToList();
        Assert.Empty(missing);
    }

    [Fact]
    public void Code_points_are_the_documented_block()
    {
        // The block is contiguous from U+E180 in the font's own glyph order (docs/development.md, "The 3270 font").
        Assert.Equal(0xE180, OiaGlyphs.All.Min());
        Assert.Equal(0xE198, OiaGlyphs.All.Max());
        Assert.Equal(25, OiaGlyphs.All.Distinct().Count());
        Assert.Equal("", OiaGlyphs.Box4);
        Assert.Equal("", OiaGlyphs.NoConnection);
    }
}
