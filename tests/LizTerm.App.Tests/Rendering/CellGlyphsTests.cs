// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.App.Rendering;

namespace LizTerm.App.Tests.Rendering;

/// <summary>The headless text stub does not shape text, so the shift a soft hyphen caused (#191) cannot be seen in an
/// App test. What can be pinned is that no soft hyphen reaches the shaper.</summary>
public class CellGlyphsTests
{
    [Fact]
    public void A_soft_hyphen_mid_run_is_drawn_as_a_hyphen()
    {
        Assert.Equal("AB-CD", CellGlyphs.ForDrawing("AB\u00ADCD"));
    }

    [Fact]
    public void Every_soft_hyphen_in_a_run_is_replaced()
    {
        Assert.Equal(" ---XYZ", CellGlyphs.ForDrawing(" \u00AD\u00AD\u00ADXYZ"));
    }

    [Fact]
    public void A_lone_soft_hyphen_is_drawn_as_a_hyphen()
    {
        Assert.Equal("-", CellGlyphs.ForDrawing("\u00AD"));
    }

    [Fact]
    public void Text_without_a_soft_hyphen_is_returned_as_is()
    {
        const string text = "READY";
        Assert.Same(text, CellGlyphs.ForDrawing(text));
    }

    [Fact]
    public void The_result_is_as_long_as_the_input()
    {
        var text = "A\u00ADB\u00AD";
        Assert.Equal(text.Length, CellGlyphs.ForDrawing(text).Length);
    }
}
