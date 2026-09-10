// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia;
using LizTerm.Core.Screen;

namespace LizTerm.App.Rendering;

/// <summary>Where the crosshair's bars go. Pure math with no Avalonia rendering in it, the same shape as
/// CellGeometry.Fit, so the rule is asserted on rectangles rather than on pixels.</summary>
public static class CrosshairGeometry
{
    /// <summary>The horizontal and vertical bars for <paramref name="mode"/>, either of which may be null.
    /// A hidden cursor still gets a crosshair; a cursor outside the grid, or a geometry that has not been
    /// measured yet, gets none.</summary>
    public static (Rect? Horizontal, Rect? Vertical) Rects(
        CrosshairMode mode, CursorPosition cursor, CellGeometry geometry, int rows, int columns)
    {
        if (mode == CrosshairMode.None) return (null, null);
        if (geometry.CellWidth <= 0 || geometry.CellHeight <= 0) return (null, null);
        if ((uint)cursor.Row >= (uint)rows || (uint)cursor.Column >= (uint)columns) return (null, null);

        var cell = geometry.CellRect(cursor.Row, cursor.Column);
        var width = columns * geometry.CellWidth;
        var height = rows * geometry.CellHeight;

        var horizontal = mode is CrosshairMode.Horizontal or CrosshairMode.Both
            ? new Rect(geometry.OriginX, cell.Y, width, geometry.CellHeight)
            : (Rect?)null;

        var vertical = mode is CrosshairMode.Vertical or CrosshairMode.Both
            ? new Rect(cell.X, geometry.OriginY, geometry.CellWidth, height)
            : (Rect?)null;

        return (horizontal, vertical);
    }
}
