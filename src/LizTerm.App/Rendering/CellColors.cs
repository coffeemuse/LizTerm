// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Media;
using LizTerm.Core.Screen;

namespace LizTerm.App.Rendering;

/// <summary>Which brushes one cell's style is drawn with, for a run and for the cursor block. A colour (3279)
/// screen draws the host's colours; a mono (3278) screen draws everything in <see cref="Palette.Phosphor"/> and
/// varies only intensity, ignoring the colour a cell carries — b3270 never sends one for a 3278, so what is there
/// is only the last erase's fill (#123). Pure so the choice is testable: TerminalScreen asserts geometry, never
/// pixels.</summary>
public static class CellColors
{
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
        var bg = ResolveBackground(Reverse(style) ? style.Foreground : style.Background);
        return bg == HostColor.NeutralBlack ? null : Palette.Brush(bg, false);
    }

    /// <summary>The underline's pen brush: the foreground at plain intensity.</summary>
    public static IBrush Underline(in Cell style, bool monochrome) =>
        monochrome ? Palette.PhosphorBrush(false) : Palette.Brush(ForegroundOf(style), false);

    /// <summary>The block the cursor paints over its cell; the glyph is cut out of it in black.</summary>
    public static IBrush CursorBlock(in Cell cell, bool monochrome) =>
        monochrome ? Palette.PhosphorBrush(false) : Palette.Brush(ResolveForeground(cell.Foreground), false);

    private static bool Reverse(in Cell style) => style.Rendition.HasFlag(CellRendition.Reverse);

    private static HostColor ForegroundOf(in Cell style) =>
        ResolveForeground(Reverse(style) ? style.Background : style.Foreground);

    private static HostColor ResolveForeground(HostColor color) => color == HostColor.Default ? HostColor.NeutralWhite : color;
    private static HostColor ResolveBackground(HostColor color) => color == HostColor.Default ? HostColor.NeutralBlack : color;
}
