// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

namespace LizTerm.App.Status;

/// <summary>The x3270 Operator Information Area symbols, as the bundled 3270 font encodes them. Upstream 3270font
/// carries these glyphs without code points; tools/patch-3270-oia-font.py assigns them the private-use block
/// U+E180 onward, in the font's own glyph order, and this class is the one place that block is spelled out
/// (docs/development.md, "The 3270 font").</summary>
public static class OiaGlyphs
{
    public const string BoxA = "";
    public const string Insert = "";
    public const string BoxB = "";
    public const string Box6 = "";
    public const string RightArrow = "";
    public const string UpShift = "";
    public const string Human = "";
    public const string UnderB = "";
    public const string DownShift = "";
    public const string BoxQuestion = "";
    public const string BoxSolid = "";
    public const string BadCommHi = "";
    public const string CommHi = "";
    public const string CommJag = "";
    public const string CommLo = "";
    public const string ClockLeft = "";
    public const string ClockRight = "";
    public const string Lock = "";
    public const string LeftArrow = "";
    public const string KeyLeft = "";
    public const string KeyRight = "";
    public const string Box4 = "";
    public const string UnderA = "";
    public const string MagCard = "";
    public const string BoxHuman = "";

    /// <summary>x3270's "no connection" symbol: a wire with a break in it.</summary>
    public const string NoConnection = CommHi + BadCommHi + CommHi + CommJag + CommLo;
    public const string Clock = ClockLeft + ClockRight;

    public static IReadOnlyList<int> All { get; } = Enumerable.Range(0xE180, 25).ToList();
}
