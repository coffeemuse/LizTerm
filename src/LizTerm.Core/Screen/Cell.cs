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
}
