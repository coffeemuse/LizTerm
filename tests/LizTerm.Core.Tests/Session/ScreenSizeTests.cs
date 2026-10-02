// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.Core.Session;

namespace LizTerm.Core.Tests.Session;

public class ScreenSizeTests
{
    [Theory]
    [InlineData(2, 24, 80)]
    [InlineData(3, 32, 80)]
    [InlineData(4, 43, 80)]
    [InlineData(5, 27, 132)]
    public void A_model_s_alternate_size_is_its_own_geometry(int model, int rows, int columns) =>
        Assert.Equal(new ScreenSize(rows, columns), ScreenSize.AlternateFor(new SessionProfile(Model: model)));

    /// <summary>b3270's screen-mode reports an oversize geometry as the screen's maximum, as the
    /// oversize-100x50 fixture records.</summary>
    [Fact]
    public void An_oversize_geometry_is_the_alternate_size() =>
        Assert.Equal(new ScreenSize(50, 100), ScreenSize.AlternateFor(new SessionProfile(Model: 2, Oversize: "100x50")));

    /// <summary>b3270 drops an oversize without the extended data stream, but a custom size keeps the stream on
    /// whatever Extended says (<see cref="TerminalType.IsExtended"/>, #200), so the oversize holds.</summary>
    [Fact]
    public void An_oversize_with_extended_unchecked_is_still_the_alternate_size() =>
        Assert.Equal(new ScreenSize(60, 132), ScreenSize.AlternateFor(new SessionProfile(Model: 2, Oversize: "132x60", Extended: false)));

    [Theory]
    [InlineData("0x0")]
    [InlineData("")]
    [InlineData("nonsense")]
    [InlineData("80x20")]
    public void An_oversize_the_engine_would_not_take_leaves_the_model_s_size(string oversize) =>
        Assert.Equal(new ScreenSize(43, 80), ScreenSize.AlternateFor(new SessionProfile(Model: 4, Oversize: oversize)));

    /// <summary>A hand-edited model outside the catalogue has no known geometry; every model's default screen is
    /// 24x80.</summary>
    [Fact]
    public void An_unknown_model_reads_as_24_by_80() =>
        Assert.Equal(new ScreenSize(24, 80), ScreenSize.AlternateFor(new SessionProfile(Model: 9)));
}
