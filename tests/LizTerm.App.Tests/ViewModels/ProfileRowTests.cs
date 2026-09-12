// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.App.Rendering;
using LizTerm.App.ViewModels;
using LizTerm.Core.Profiles;
using LizTerm.Core.Session;

namespace LizTerm.App.Tests.ViewModels;

public class ProfileRowTests
{
    private static TagRegistry Registry(params string[] names) => TagRegistry.Empty.Register(names).Registry;

    [Fact]
    public void A_row_shows_the_name_the_host_and_the_note()
    {
        var row = new ProfileRow(
            new SessionProfile { Name = "mvsce", Host = "10.42.37.209", Port = 3270, Note = "no live data" },
            TagRegistry.Empty);

        Assert.Equal("mvsce", row.Name);
        Assert.Equal("10.42.37.209:3270", row.HostPort);
        Assert.Equal("no live data", row.Note);
        Assert.True(row.HasNote);
    }

    [Fact]
    public void A_profile_with_no_note_has_nothing_to_draw_on_the_third_line()
    {
        var row = new ProfileRow(new SessionProfile { Name = "tk5", Host = "h" }, TagRegistry.Empty);
        Assert.False(row.HasNote);
        Assert.False(row.IsFavorite);
        Assert.Empty(row.Chips);
        Assert.False(row.HasOverflow);
    }

    /// <summary>FAVORITE is the star, so it must never also be a chip.</summary>
    [Fact]
    public void The_reserved_tag_becomes_the_star_and_not_a_chip()
    {
        var row = new ProfileRow(
            new SessionProfile { Name = "n", Host = "h", Tags = TagSet.From(["FAVORITE", "PROD"]) },
            Registry("PROD"));

        Assert.True(row.IsFavorite);
        Assert.Equal(["PROD"], row.Chips.Select(c => c.Text));
    }

    [Fact]
    public void Chip_text_is_uppercase_whatever_the_stored_casing()
    {
        var row = new ProfileRow(
            new SessionProfile { Name = "n", Host = "h", Tags = TagSet.From(["prod", "mvs"]) },
            Registry("prod", "mvs"));
        Assert.Equal(["PROD", "MVS"], row.Chips.Select(c => c.Text));
    }

    [Fact]
    public void A_chip_takes_its_colour_from_the_registry()
    {
        var registry = Registry("PROD");
        var row = new ProfileRow(new SessionProfile { Name = "n", Host = "h", Tags = TagSet.From(["PROD"]) }, registry);
        Assert.Same(TagPalette.Brush(registry.ColorOf("PROD")), row.Chips.Single().Background);
    }

    /// <summary>The list is only 356px wide, so past three chips the rest collapse into a +n whose tooltip
    /// names them — Robert's own suggestion for the overflow.</summary>
    [Fact]
    public void More_than_three_tags_collapse_into_an_overflow_chip_with_a_tooltip()
    {
        var names = new[] { "DEV", "PROD", "MVS", "TLS", "LAB" };
        var row = new ProfileRow(
            new SessionProfile { Name = "n", Host = "h", Tags = TagSet.From(["FAVORITE", .. names]) },
            Registry(names));

        Assert.Equal(ProfileRow.MaxChips, row.Chips.Count);
        Assert.Equal(["DEV", "PROD", "MVS"], row.Chips.Select(c => c.Text));
        Assert.True(row.HasOverflow);
        Assert.Equal("+2", row.OverflowText);
        Assert.Equal("TLS, LAB", row.OverflowTip);
    }

    [Fact]
    public void Exactly_three_tags_need_no_overflow()
    {
        var row = new ProfileRow(
            new SessionProfile { Name = "n", Host = "h", Tags = TagSet.From(["A", "B", "C"]) },
            Registry("A", "B", "C"));
        Assert.Equal(3, row.Chips.Count);
        Assert.False(row.HasOverflow);
        Assert.Null(row.OverflowText);
    }
}
