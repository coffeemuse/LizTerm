// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Text;
using LizTerm.App.Rendering;
using LizTerm.Core.Screen;

namespace LizTerm.App.Tests.Rendering;

/// <summary>The colour decisions TerminalScreen makes for a run and for the cursor, reachable here because the
/// control asserts geometry rather than pixels, so nothing else in the suite can see which brush a run got.</summary>
public class CellColorsTests
{
    private static Cell Cell(HostColor fg, HostColor bg = HostColor.NeutralBlack, CellRendition rendition = CellRendition.None) =>
        new(new Rune('X'), fg, bg, rendition);

    [Fact]
    public void A_colour_screen_draws_the_host_colours()
    {
        var cell = Cell(HostColor.Red, HostColor.Blue, CellRendition.Highlight);
        Assert.Same(Palette.Brush(HostColor.Red, bright: true), CellColors.Foreground(cell, monochrome: false));
        Assert.Same(Palette.Brush(HostColor.Blue, bright: false), CellColors.Background(cell, monochrome: false));
        Assert.Same(Palette.Brush(HostColor.Red, bright: false), CellColors.Underline(cell, monochrome: false));
    }

    [Fact]
    public void A_colour_screen_leaves_a_black_background_unpainted()
    {
        Assert.Null(CellColors.Background(Cell(HostColor.Green), monochrome: false));
    }

    /// <summary>b3270 never sends a colour for a 3278, so a cell's colour is whatever the last erase or resize
    /// filled in (neutral white, or the blue the screen-mode resize uses). The mono renderer never reads it,
    /// which is what makes that fill unable to leak (#123).</summary>
    [Fact]
    public void A_mono_screen_draws_every_foreground_in_the_phosphor()
    {
        Assert.Same(Palette.PhosphorBrush(false), CellColors.Foreground(Cell(HostColor.Blue), monochrome: true));
        Assert.Same(Palette.PhosphorBrush(false), CellColors.Foreground(Cell(HostColor.NeutralWhite), monochrome: true));
        Assert.Same(Palette.PhosphorBrush(true), CellColors.Foreground(Cell(HostColor.Red, rendition: CellRendition.Highlight), monochrome: true));
        Assert.Same(Palette.PhosphorBrush(false), CellColors.Underline(Cell(HostColor.Red, rendition: CellRendition.Highlight), monochrome: true));
    }

    [Fact]
    public void A_mono_screen_has_no_coloured_backgrounds()
    {
        Assert.Null(CellColors.Background(Cell(HostColor.Green, HostColor.Blue), monochrome: true));
    }

    /// <summary>A 3278 does reverse video: a block of phosphor with the glyph cut out of it in black.</summary>
    [Fact]
    public void A_mono_screen_reverses_into_a_phosphor_block_with_black_text()
    {
        var cell = Cell(HostColor.Green, HostColor.Blue, CellRendition.Reverse);
        Assert.Same(Palette.PhosphorBrush(false), CellColors.Background(cell, monochrome: true));
        Assert.Same(Palette.Brush(HostColor.NeutralBlack, false), CellColors.Foreground(cell, monochrome: true));
        // The underline follows the text, or it would be phosphor on the phosphor block and vanish.
        Assert.Same(Palette.Brush(HostColor.NeutralBlack, false), CellColors.Underline(cell, monochrome: true));
    }

    /// <summary>On a reversed cell the block is already the text colour, so the cursor takes the cell's other
    /// colour and cuts the glyph out in the first, or it would paint exactly what is there and disappear.</summary>
    [Fact]
    public void The_cursor_inverts_a_reversed_cell_on_both_screens()
    {
        var cell = Cell(HostColor.Red, HostColor.Blue, CellRendition.Reverse);
        Assert.Same(Palette.Brush(HostColor.NeutralBlack, false), CellColors.CursorBlock(cell, monochrome: true));
        Assert.Same(Palette.PhosphorBrush(false), CellColors.CursorGlyph(cell, monochrome: true));
        Assert.Same(Palette.Brush(HostColor.Blue, false), CellColors.CursorBlock(cell, monochrome: false));
        Assert.Same(Palette.Brush(HostColor.Red, false), CellColors.CursorGlyph(cell, monochrome: false));

        Assert.Same(Palette.Brush(HostColor.NeutralBlack, false), CellColors.CursorGlyph(Cell(HostColor.Red), monochrome: false));
        Assert.Same(Palette.Brush(HostColor.NeutralBlack, false), CellColors.CursorGlyph(Cell(HostColor.Red), monochrome: true));
    }

    /// <summary>The colour pair a capture writes is the pair the screen draws, reverse video included.</summary>
    [Fact]
    public void The_colour_pair_is_the_screen_rule_for_a_capture()
    {
        Assert.Equal((Palette.ColorOf(HostColor.Red), Palette.ColorOf(HostColor.Blue)), CellColors.Colors(Cell(HostColor.Red, HostColor.Blue), monochrome: false));
        Assert.Equal((Palette.ColorOf(HostColor.Blue), Palette.ColorOf(HostColor.Red)), CellColors.Colors(Cell(HostColor.Red, HostColor.Blue, CellRendition.Reverse), monochrome: false));
        Assert.Equal((Palette.ColorOf(HostColor.NeutralWhite), Palette.ColorOf(HostColor.NeutralBlack)), CellColors.Colors(Cell(HostColor.Default, HostColor.Default), monochrome: false));
        Assert.Equal((Palette.Phosphor, Palette.ColorOf(HostColor.NeutralBlack)), CellColors.Colors(Cell(HostColor.Red, HostColor.Blue), monochrome: true));
        Assert.Equal((Palette.ColorOf(HostColor.NeutralBlack), Palette.Phosphor), CellColors.Colors(Cell(HostColor.Red, HostColor.Blue, CellRendition.Reverse), monochrome: true));
    }

    [Fact]
    public void The_cursor_block_is_the_cell_colour_on_a_colour_screen_and_the_phosphor_on_a_mono_one()
    {
        Assert.Same(Palette.Brush(HostColor.Red, false), CellColors.CursorBlock(Cell(HostColor.Red), monochrome: false));
        Assert.Same(Palette.Brush(HostColor.NeutralWhite, false), CellColors.CursorBlock(Cell(HostColor.Default), monochrome: false));
        Assert.Same(Palette.PhosphorBrush(false), CellColors.CursorBlock(Cell(HostColor.Red), monochrome: true));
    }
}
