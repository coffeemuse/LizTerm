// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

namespace LizTerm.App.Rendering;

/// <summary>What TerminalScreen hands the text shaper for a cell's character, which is not always the character
/// itself. Drawing only: the snapshot, Copy and Find keep the real character.</summary>
public static class CellGlyphs
{
    private const char SoftHyphen = '­';

    /// <summary><paramref name="text"/> with every U+00AD SOFT HYPHEN drawn as U+002D HYPHEN-MINUS (#191). A host's
    /// X'CA' arrives as U+00AD in every Latin code page b3270 ships, and HarfBuzz shapes that default-ignorable
    /// character with no advance even though the 3270 font has a full-width glyph for it, so a run of cells drawn as
    /// one FormattedText came out a cell short and everything after it sat one column left. The font's two glyphs
    /// are nearly identical. The result has one character per cell, exactly as the input had.</summary>
    public static string ForDrawing(string text) =>
        text.Contains(SoftHyphen) ? text.Replace(SoftHyphen, '-') : text;
}
