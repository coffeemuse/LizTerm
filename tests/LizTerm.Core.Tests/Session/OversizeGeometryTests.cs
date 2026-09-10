// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.Core.Session;

namespace LizTerm.Core.Tests.Session;

public class OversizeGeometryTests
{
    private static readonly TerminalModel Model2 = TerminalModel.Find(2)!;   // 24x80
    private static readonly TerminalModel Model5 = TerminalModel.Find(5)!;   // 27x132

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_is_valid_and_means_no_oversize(string? text)
    {
        Assert.True(OversizeGeometry.TryParse(text, Model2, out var geometry, out var error));
        Assert.Null(geometry);
        Assert.Null(error);
    }

    /// <summary>b3270's own "no oversize" spelling, which a hand-edited profile can carry.</summary>
    [Fact]
    public void Zero_by_zero_is_valid_and_means_no_oversize()
    {
        Assert.True(OversizeGeometry.TryParse("0x0", Model2, out var geometry, out _));
        Assert.Null(geometry);
    }

    [Theory]
    [InlineData("132x43", 132, 43)]
    [InlineData("132X43", 132, 43)]
    [InlineData("  132x43  ", 132, 43)]
    public void A_legal_geometry_parses_columns_first(string text, int columns, int rows)
    {
        Assert.True(OversizeGeometry.TryParse(text, Model2, out var geometry, out _));
        Assert.Equal(new OversizeGeometry(columns, rows), geometry);
    }

    /// <summary>What BuildArguments passes to -oversize, so the round trip has to be exact.</summary>
    [Fact]
    public void ToString_is_the_engine_argument()
    {
        Assert.Equal("132x43", new OversizeGeometry(132, 43).ToString());
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("132")]
    [InlineData("132x43x2")]
    [InlineData("132x")]
    [InlineData("-5x10")]
    [InlineData("13 2x43")]
    public void Anything_that_is_not_two_plain_numbers_is_a_format_error(string text)
    {
        Assert.False(OversizeGeometry.TryParse(text, Model2, out var geometry, out var error));
        Assert.Null(geometry);
        Assert.Equal("Oversize must be columns by rows, for example 132x43.", error);
    }

    [Theory]
    [InlineData("0x50")]
    [InlineData("100x0")]
    public void One_dimension_zero_while_the_other_is_set_is_refused(string text)
    {
        Assert.False(OversizeGeometry.TryParse(text, Model2, out _, out var error));
        Assert.Equal("Oversize needs both a column count and a row count, for example 132x43.", error);
    }

    [Fact]
    public void Columns_above_the_ceiling_are_refused_by_name()
    {
        Assert.False(OversizeGeometry.TryParse("16384x1", Model2, out _, out var error));
        Assert.Equal("Oversize columns must be at most 16383.", error);
    }

    [Fact]
    public void Rows_above_the_ceiling_are_refused_by_name()
    {
        Assert.False(OversizeGeometry.TryParse("1x16384", Model2, out _, out var error));
        Assert.Equal("Oversize rows must be at most 16383.", error);
    }

    /// <summary>The rule that surprises: b3270 compares an AREA against the same linear constant, so 160
    /// columns allows only 102 rows and Vista's advertised 200x200 is rejected outright (spec 4.1).</summary>
    [Fact]
    public void The_area_limit_allows_160_by_102()
    {
        Assert.True(OversizeGeometry.TryParse("160x102", Model2, out _, out _));
    }

    [Fact]
    public void The_area_limit_refuses_160_by_103_and_says_what_fits()
    {
        Assert.False(OversizeGeometry.TryParse("160x103", Model2, out _, out var error));
        Assert.Equal("160 columns by 103 rows is 16,480 cells; b3270 allows 16,383. At 160 columns the most is 102 rows.", error);
    }

    [Fact]
    public void The_area_limit_refuses_Vistas_200_by_200()
    {
        Assert.False(OversizeGeometry.TryParse("200x200", Model2, out _, out var error));
        Assert.StartsWith("200 columns by 200 rows is 40,000 cells", error);
    }

    [Fact]
    public void Below_the_models_own_geometry_is_refused_naming_the_model()
    {
        Assert.False(OversizeGeometry.TryParse("70x43", Model2, out _, out var error));
        Assert.Equal("Oversize must be at least 80 columns and 24 rows for model 2.", error);
    }

    /// <summary>132x43 is legal on model 2, but 132x20 falls short of model 5's 27-row floor, which is why the
    /// editor re-validates when the model changes (Task 5).</summary>
    [Fact]
    public void The_floor_is_the_chosen_models_floor_not_model_2s()
    {
        Assert.True(OversizeGeometry.TryParse("132x43", Model2, out _, out _));
        Assert.False(OversizeGeometry.TryParse("132x20", Model5, out _, out var error));
        Assert.Equal("Oversize must be at least 132 columns and 27 rows for model 5.", error);
    }

    /// <summary>A model number a hand-edited profile invented has no geometry (TerminalModel renders it as a
    /// bare number rather than lying with "9 — 0x0"), so there is no floor to compare against. Every other
    /// rule is a property of b3270 and still holds (spec 4.4).</summary>
    [Fact]
    public void An_unknown_model_skips_the_floor_and_keeps_every_other_rule()
    {
        var unknown = new TerminalModel(9, 0, 0);
        Assert.True(OversizeGeometry.TryParse("70x10", unknown, out _, out _));
        Assert.False(OversizeGeometry.TryParse("200x200", unknown, out _, out var error));
        Assert.StartsWith("200 columns by 200 rows", error);
    }
}
