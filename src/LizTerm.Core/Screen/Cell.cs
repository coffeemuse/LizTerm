// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Text;

namespace LizTerm.Core.Screen;

public readonly record struct Cell(Rune Character, HostColor Foreground, HostColor Background, CellRendition Rendition)
{
    public static readonly Rune Space = new(' ');

    public static Cell Blank(HostColor foreground, HostColor background) =>
        new(Space, foreground, background, CellRendition.None);

    /// <summary>The run-segmentation rule shared by the renderer and the HTML capture: two cells belong to the
    /// same run when their foreground, background and rendition all match, regardless of character. Kept here
    /// so <c>TerminalScreen</c> and <c>ScreenHtml</c> cannot disagree about where a run ends.</summary>
    public bool SameStyleAs(in Cell other) =>
        Foreground == other.Foreground && Background == other.Background && Rendition == other.Rendition;
}
