// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia;
using LizTerm.Core.Screen;
using LizTerm.Core.Settings;

namespace LizTerm.App.Rendering;

/// <summary>Where the crosshair's lines go. Pure math with no Avalonia rendering in it, the same shape as
/// CellGeometry.Fit, so the rule is asserted on rectangles rather than on pixels.</summary>
public static class CrosshairGeometry
{
    /// <summary>How thick a ruler line is, in device-independent pixels. A constant rather than a fraction of the
    /// cell: a hairline should stay a hairline at any font size, and a shaded cell is what #180 replaced.</summary>
    public const double Thickness = 1;

    /// <summary>The horizontal and vertical lines for <paramref name="mode"/>, either of which may be null.
    /// A hidden cursor still gets a crosshair; a cursor outside the grid, or a geometry that has not been
    /// measured yet, gets none.</summary>
    public static (Rect? Horizontal, Rect? Vertical) Rects(
        CrosshairMode mode, CursorPosition cursor, CellGeometry geometry, int rows, int columns)
    {
        if (mode == CrosshairMode.None) return (null, null);
        if (geometry.CellWidth <= 0 || geometry.CellHeight <= 0) return (null, null);
        if ((uint)cursor.Row >= (uint)rows || (uint)cursor.Column >= (uint)columns) return (null, null);

        var cell = geometry.CellRect(cursor.Row, cursor.Column);

        // Every edge is rounded to a whole pixel. A cell is a fraction of one tall — CellGeometry.Fit multiplies
        // the font size by the font's own ratios — and a hairline spread across two rows of pixels at partial
        // coverage draws as a soft grey smear rather than a line. The run plan's underline rounds for the same
        // reason.
        var left = Math.Round(geometry.OriginX);
        var top = Math.Round(geometry.OriginY);
        var right = Math.Round(geometry.OriginX + columns * geometry.CellWidth);
        var bottom = Math.Round(geometry.OriginY + rows * geometry.CellHeight);

        // Both lines sit on a cell boundary rather than through the middle of the cursor cell: a line down the
        // centre of a row would strike through every glyph on it, and a boundary is what lining up a column wants.
        var horizontal = mode is CrosshairMode.Horizontal or CrosshairMode.Both
            ? new Rect(left, Math.Round(cell.Bottom) - Thickness, right - left, Thickness)
            : (Rect?)null;

        var vertical = mode is CrosshairMode.Vertical or CrosshairMode.Both
            ? new Rect(Math.Round(cell.X), top, Thickness, bottom - top)
            : (Rect?)null;

        return (horizontal, vertical);
    }
}
