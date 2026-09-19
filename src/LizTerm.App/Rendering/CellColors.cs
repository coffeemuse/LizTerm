// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Media;
using LizTerm.Core.Screen;

namespace LizTerm.App.Rendering;

/// <summary>Which colours one cell's style is drawn with, for a run, for the cursor block and for a capture. A
/// colour (3279) screen draws the host's colours; a mono (3278) screen draws everything in
/// <see cref="Palette.Phosphor"/> and varies only intensity, ignoring the colour a cell carries — b3270 never sends
/// one for a 3278, so what is there is only the last erase's fill (#123). Reverse video swaps the two in both.
/// Pure so the choice is testable: TerminalScreen asserts geometry, never pixels. ScreenHtml asks the same rule,
/// so a capture never disagrees with the screen.</summary>
public static class CellColors
{
    /// <summary>The text and background colours at plain intensity, the one rule every caller draws from.</summary>
    public static (Color Foreground, Color Background) Colors(in Cell style, bool monochrome)
    {
        var black = Palette.ColorOf(HostColor.NeutralBlack);
        if (monochrome)
            return Reverse(style) ? (black, Palette.Phosphor) : (Palette.Phosphor, black);
        return (Palette.ColorOf(ForegroundOf(style)), Palette.ColorOf(BackgroundOf(style)));
    }

    /// <summary>The text brush, intensified when the cell is highlighted.</summary>
    public static IBrush Foreground(in Cell style, bool monochrome)
    {
        var bright = style.Rendition.HasFlag(CellRendition.Highlight);
        if (monochrome)
            return Reverse(style) ? Palette.Brush(HostColor.NeutralBlack, false) : Palette.PhosphorBrush(bright);
        return Palette.Brush(ForegroundOf(style), bright);
    }

    /// <summary>The run's background, or null when it is the screen's own black and needs no painting.</summary>
    public static IBrush? Background(in Cell style, bool monochrome)
    {
        if (monochrome)
            return Reverse(style) ? Palette.PhosphorBrush(false) : null;
        var bg = BackgroundOf(style);
        return bg == HostColor.NeutralBlack ? null : Palette.Brush(bg, false);
    }

    /// <summary>The underline's pen brush: the text colour at plain intensity, so it stays visible on a reversed
    /// cell's block.</summary>
    public static IBrush Underline(in Cell style, bool monochrome)
    {
        if (monochrome)
            return Reverse(style) ? Palette.Brush(HostColor.NeutralBlack, false) : Palette.PhosphorBrush(false);
        return Palette.Brush(ForegroundOf(style), false);
    }

    /// <summary>The block the cursor paints over its cell: the cell's text colour, so on a reversed cell, whose
    /// block is already that colour, the cursor is the cell's background colour instead and stays visible.</summary>
    public static IBrush CursorBlock(in Cell cell, bool monochrome)
    {
        if (monochrome)
            return Reverse(cell) ? Palette.Brush(HostColor.NeutralBlack, false) : Palette.PhosphorBrush(false);
        return Palette.Brush(ForegroundOf(cell), false);
    }

    /// <summary>The glyph cut out of the cursor block: the other of the cell's two colours, black on a plain
    /// cell.</summary>
    public static IBrush CursorGlyph(in Cell cell, bool monochrome)
    {
        if (monochrome)
            return Reverse(cell) ? Palette.PhosphorBrush(false) : Palette.Brush(HostColor.NeutralBlack, false);
        return Reverse(cell) ? Palette.Brush(ResolveForeground(cell.Foreground), false) : Palette.Brush(HostColor.NeutralBlack, false);
    }

    private static bool Reverse(in Cell style) => style.Rendition.HasFlag(CellRendition.Reverse);

    private static HostColor ForegroundOf(in Cell style) =>
        ResolveForeground(Reverse(style) ? style.Background : style.Foreground);

    private static HostColor BackgroundOf(in Cell style) =>
        ResolveBackground(Reverse(style) ? style.Foreground : style.Background);

    private static HostColor ResolveForeground(HostColor color) => color == HostColor.Default ? HostColor.NeutralWhite : color;
    private static HostColor ResolveBackground(HostColor color) => color == HostColor.Default ? HostColor.NeutralBlack : color;
}
