// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Text;
using LizTerm.Core.Screen;

namespace LizTerm.Core.Tests.Screen;

public class CellTests
{
    [Fact]
    public void Cells_with_the_same_style_are_the_same_style_regardless_of_character()
    {
        var a = new Cell(new Rune('A'), HostColor.Green, HostColor.NeutralBlack, CellRendition.Underline);
        var b = new Cell(new Rune('B'), HostColor.Green, HostColor.NeutralBlack, CellRendition.Underline);

        Assert.True(a.SameStyleAs(b));
    }

    [Fact]
    public void A_differing_foreground_breaks_the_run()
    {
        var a = new Cell(new Rune('X'), HostColor.Green, HostColor.NeutralBlack, CellRendition.None);
        var b = new Cell(new Rune('X'), HostColor.Red, HostColor.NeutralBlack, CellRendition.None);

        Assert.False(a.SameStyleAs(b));
    }

    [Fact]
    public void A_differing_background_breaks_the_run()
    {
        var a = new Cell(new Rune('X'), HostColor.Green, HostColor.NeutralBlack, CellRendition.None);
        var b = new Cell(new Rune('X'), HostColor.Green, HostColor.Red, CellRendition.None);

        Assert.False(a.SameStyleAs(b));
    }

    [Fact]
    public void A_differing_rendition_breaks_the_run()
    {
        var a = new Cell(new Rune('X'), HostColor.Green, HostColor.NeutralBlack, CellRendition.None);
        var b = new Cell(new Rune('X'), HostColor.Green, HostColor.NeutralBlack, CellRendition.Underline);

        Assert.False(a.SameStyleAs(b));
    }
}
