// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

namespace LizTerm.App.Status;

/// <summary>The x3270 Operator Information Area symbols, as the bundled 3270 font encodes them. Upstream 3270font
/// carries these glyphs without code points; tools/patch-3270-oia-font.py assigns them the private-use block
/// U+E180 onward, in the font's own glyph order, and this class is the one place that block is spelled out
/// (docs/development.md, "The 3270 font"). OiaGlyphsTests reads these constants to hold the script's list, the code
/// points and the shipped file to one another, so each single-character constant is one symbol of the block.</summary>
public static class OiaGlyphs
{
    public const string BoxA = "\uE180";
    public const string Insert = "\uE181";
    public const string BoxB = "\uE182";
    public const string Box6 = "\uE183";
    public const string RightArrow = "\uE184";
    public const string UpShift = "\uE185";
    public const string Human = "\uE186";
    public const string UnderB = "\uE187";
    public const string DownShift = "\uE188";
    public const string BoxQuestion = "\uE189";
    public const string BoxSolid = "\uE18A";
    public const string BadCommHi = "\uE18B";
    public const string CommHi = "\uE18C";
    public const string CommJag = "\uE18D";
    public const string CommLo = "\uE18E";
    public const string ClockLeft = "\uE18F";
    public const string ClockRight = "\uE190";
    public const string Lock = "\uE191";
    public const string LeftArrow = "\uE192";
    public const string KeyLeft = "\uE193";
    public const string KeyRight = "\uE194";
    public const string Box4 = "\uE195";
    public const string UnderA = "\uE196";
    public const string MagCard = "\uE197";
    public const string BoxHuman = "\uE198";

    /// <summary>x3270's "no connection" symbol: a wire with a break in it.</summary>
    public const string NoConnection = CommHi + BadCommHi + CommHi + CommJag + CommLo;
    public const string Clock = ClockLeft + ClockRight;
}
