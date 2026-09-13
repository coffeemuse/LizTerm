// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

namespace LizTerm.App.Rendering;

/// <summary>What size the status bar draws its OIA glyphs at: the screen's cell font size, as a real 3270's OIA is
/// one more row of the same cells. Pure math; no Avalonia.</summary>
public static class StatusBarFont
{
    /// <summary>Before the screen has geometry (no snapshot yet), the bar's ordinary text size.</summary>
    public const double Default = 14;

    /// <summary>The bar's height feeds back into the screen's: a taller bar shrinks the screen, which can re-fit
    /// the cells a pixel smaller, which shrinks the bar, which re-fits them a pixel larger, and layout never
    /// settles. A one-pixel move in the cells changes the bar by about a pixel and the screen's fit by a
    /// fraction of one, so the wobble is never more than a pixel; holding through it breaks the loop.</summary>
    public static double Follow(double current, double cellFontSize)
    {
        if (cellFontSize <= 0) return current;
        return Math.Abs(cellFontSize - current) <= 1 ? current : cellFontSize;
    }
}
