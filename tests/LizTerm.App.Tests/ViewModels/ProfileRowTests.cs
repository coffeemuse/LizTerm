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

    [Fact]
    public void The_favorite_menu_entry_marks_an_unstarred_profile_and_removes_a_starred_one()
    {
        var plain = new ProfileRow(new SessionProfile { Name = "n", Host = "h" }, TagRegistry.Empty);
        Assert.Equal("Mark as FAVORITE", plain.FavoriteMenuText);
        Assert.True(plain.CanToggleFavorite);

        var starred = new ProfileRow(
            new SessionProfile { Name = "n", Host = "h", Tags = TagSet.From(["FAVORITE", "PROD"]) },
            Registry("PROD"));
        Assert.Equal("Remove from FAVORITE", starred.FavoriteMenuText);
        Assert.True(starred.CanToggleFavorite);
    }

    /// <summary>TagSet.With on a set already at the cap drops the name it was given, so an enabled entry on a
    /// full profile would click and silently do nothing. Removing needs no room, so a full starred profile can
    /// still be unmarked.</summary>
    [Fact]
    public void A_full_profile_cannot_be_marked_but_a_full_starred_one_can_still_be_unmarked()
    {
        var full = new ProfileRow(
            new SessionProfile { Name = "n", Host = "h", Tags = TagSet.From(Enumerable.Range(0, TagSet.MaxTags).Select(i => $"T{i}")) },
            TagRegistry.Empty);
        Assert.False(full.CanToggleFavorite);
        Assert.Equal($"Mark as FAVORITE (already {TagSet.MaxTags} tags)", full.FavoriteMenuText);

        var fullStarred = new ProfileRow(
            new SessionProfile { Name = "n", Host = "h", Tags = TagSet.From(["FAVORITE", .. Enumerable.Range(0, TagSet.MaxTags - 1).Select(i => $"T{i}")]) },
            TagRegistry.Empty);
        Assert.True(fullStarred.CanToggleFavorite);
        Assert.Equal("Remove from FAVORITE", fullStarred.FavoriteMenuText);
    }

    /// <summary>The row's second line (session switching spec §5.3): a saved profile's host and port.</summary>
    [Fact]
    public void A_saved_profile_shows_its_host_and_port_on_the_second_line()
    {
        var row = new ProfileRow(new SessionProfile { Name = "TSO", Host = "tk5.local", Port = 3270 }, TagRegistry.Empty);

        Assert.Equal("tk5.local:3270", row.SecondLine);
    }

    /// <summary>An ad hoc session is named host:port already, so its second line says what it is instead of
    /// repeating the host. HostPort is unchanged for its other readers.</summary>
    [Fact]
    public void An_unsaved_session_says_quick_connect_on_the_second_line()
    {
        var row = new ProfileRow(
            new SessionProfile { Name = "sdf.example:3270", Host = "sdf.example", Port = 3270 },
            TagRegistry.Empty, isSaved: false);

        Assert.Equal(ProfileRow.QuickConnectLine, row.SecondLine);
        Assert.Equal("Quick Connect", ProfileRow.QuickConnectLine);
        Assert.Equal("sdf.example:3270", row.HostPort);
    }
}
