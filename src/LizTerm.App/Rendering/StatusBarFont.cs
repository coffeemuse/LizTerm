// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

namespace LizTerm.App.Rendering;

/// <summary>What size the status bar draws its OIA glyphs at: the screen's cell font size, as a real 3270's OIA is
/// one more row of the same cells. Pure logic; no Avalonia.</summary>
/// <remarks>The bar's height is part of the space the screen fits its cells into, so following the cells feeds back:
/// a bar resized to the cells shrinks or grows the screen, which can re-fit the cells a size away. Usually that echo
/// settles when followed like any other change — a bar that jumps from 14 to 28 can re-fit the cells at 27, and with
/// the bar at 27 they stay 27. What cannot settle is a fit with no consistent answer, where a bar at N fits the cells
/// at N-1 and a bar at N-1 fits them at N: following would flip for ever. So the one thing held is a bounce, the
/// cells returning inside the same layout pass to the size the bar has just left, and it stays held on later passes
/// at the same fit (a host repaint re-arranges the screen) until the cells move on. A change after the pass has
/// settled is always followed, one size included: <c>CellGeometry.Fit</c> sizes cells in whole pixels, and holding
/// every one-size move, as this first did, left the bar a size off the screen for as long as the window kept its
/// size.</remarks>
public sealed class StatusBarFont
{
    /// <summary>Before the screen has geometry (no snapshot yet), the bar's ordinary text size.</summary>
    public const double Default = 14;

    private double _left;
    private bool _changedThisPass;
    private double? _held;

    public double Size { get; private set; } = Default;

    /// <summary>Takes the cells' font size from a layout pass and answers the bar's.</summary>
    public double Follow(double cellFontSize)
    {
        if (cellFontSize <= 0) return Size;
        if (cellFontSize == Size)
        {
            _held = null;
            return Size;
        }
        if (cellFontSize == _held || IsBounce(cellFontSize))
        {
            _held = cellFontSize;
            return Size;
        }
        _left = Size;
        Size = cellFontSize;
        _changedThisPass = true;
        _held = null;
        return Size;
    }

    /// <summary>The layout pass is over, so whatever arrives next was not caused by this pass's change.</summary>
    public void LayoutSettled() => _changedThisPass = false;

    private bool IsBounce(double cellFontSize) =>
        _changedThisPass && cellFontSize == _left && Math.Abs(cellFontSize - Size) <= 1;
}
