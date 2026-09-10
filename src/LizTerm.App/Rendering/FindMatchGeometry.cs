// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia;
using LizTerm.Core.Screen;

namespace LizTerm.App.Rendering;

/// <summary>Where one find match gets painted. Pure math with no Avalonia rendering in it, the same shape as
/// CrosshairGeometry.Rects, so the clamp rule is asserted on rectangles rather than on rendered pixels.</summary>
public static class FindMatchGeometry
{
    /// <summary>The rectangle to fill for <paramref name="region"/>, or null when it clamps away entirely.
    /// FindViewModel recomputes matches against the snapshot it just saw; a repaint can still land one turn
    /// after the host resizes the screen, so a match may arrive covering rows or columns the current grid no
    /// longer has. Clamping here is what keeps that from producing a rectangle off the grid, the same way
    /// CrosshairGeometry answers a cursor outside it. An unmeasured <paramref name="geometry"/> also gives null,
    /// since there is nowhere on screen yet to put the rectangle.</summary>
    public static Rect? Rect(ScreenRegion region, CellGeometry geometry, int rows, int columns)
    {
        if (geometry.CellWidth <= 0 || geometry.CellHeight <= 0) return null;
        if (region.Clamp(rows, columns) is not { } clamped) return null;

        var topLeft = geometry.CellRect(clamped.Top, clamped.Left);
        var bottomRight = geometry.CellRect(clamped.Bottom, clamped.Right);
        return new Rect(topLeft.TopLeft, bottomRight.BottomRight);
    }
}
