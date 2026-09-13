// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.Core.Session;

namespace LizTerm.Core.Tests.Session;

public class TagSetTests
{
    /// <summary>Every existing profile file produces default(TagSet), whose backing ImmutableArray is itself
    /// default rather than empty — so every member has to guard IsDefaultOrEmpty or throw
    /// NullReferenceException on the most common value in the system.</summary>
    [Fact]
    public void The_default_value_behaves_as_an_empty_set()
    {
        TagSet none = default;
        Assert.Equal(0, none.Count);
        Assert.True(none.IsEmpty);
        Assert.Empty(none.Names);
        Assert.False(none.Contains("PROD"));
        Assert.Equal("", none.ToString());
        Assert.Equal(TagSet.Empty, none);
        Assert.Equal(TagSet.Empty.GetHashCode(), none.GetHashCode());
    }

    [Fact]
    public void From_trims_strips_a_leading_hash_and_drops_blanks()
    {
        var tags = TagSet.From(["  PROD ", "#MVS", "", "   ", "# TEST"]);
        Assert.Equal(["PROD", "MVS", "TEST"], tags.Names);
    }

    /// <summary>Robert writes tags as "#PROD"; the hash is a display convention, so "#PROD" and "PROD" must be
    /// one tag rather than two that look identical in the drop-down.</summary>
    [Fact]
    public void From_deduplicates_case_insensitively_keeping_the_first_casing_and_preserving_order()
    {
        var tags = TagSet.From(["Prod", "mvs", "#prod", "PROD", "MVS"]);
        Assert.Equal(["Prod", "mvs"], tags.Names);
    }

    [Fact]
    public void From_drops_an_over_long_name_rather_than_truncating_it()
    {
        var tooLong = new string('x', TagSet.MaxNameLength + 1);
        var justRight = new string('y', TagSet.MaxNameLength);
        var tags = TagSet.From([tooLong, justRight]);
        Assert.Equal([justRight], tags.Names);
    }

    [Fact]
    public void From_keeps_only_the_first_MaxTags_names()
    {
        var tags = TagSet.From(Enumerable.Range(0, TagSet.MaxTags + 3).Select(i => $"T{i}"));
        Assert.Equal(TagSet.MaxTags, tags.Count);
        Assert.Equal("T0", tags.Names[0]);
        Assert.DoesNotContain($"T{TagSet.MaxTags}", tags.Names);
    }

    [Fact]
    public void Equality_ignores_case_but_respects_order()
    {
        Assert.Equal(TagSet.From(["PROD", "MVS"]), TagSet.From(["prod", "mvs"]));
        Assert.True(TagSet.From(["PROD"]) == TagSet.From(["prod"]));
        Assert.True(TagSet.From(["PROD", "MVS"]) != TagSet.From(["MVS", "PROD"]));
        Assert.NotEqual(TagSet.From(["PROD"]), TagSet.From(["PROD", "MVS"]));
    }

    /// <summary>An Equals that says two values are the same while GetHashCode disagrees makes them behave as
    /// different keys in any dictionary or set, which is the bug this whole struct exists to avoid.</summary>
    [Fact]
    public void GetHashCode_agrees_with_Equals()
    {
        Assert.Equal(TagSet.From(["PROD", "MVS"]).GetHashCode(), TagSet.From(["prod", "MVS"]).GetHashCode());
        Assert.Single(new HashSet<TagSet> { TagSet.From(["PROD"]), TagSet.From(["prod"]) });
    }

    [Fact]
    public void Contains_With_and_Without_ignore_case_and_the_hash()
    {
        var tags = TagSet.From(["PROD"]);
        Assert.True(tags.Contains("prod"));
        Assert.True(tags.Contains("#PROD"));

        var added = tags.With("MVS");
        Assert.Equal(["PROD", "MVS"], added.Names);
        Assert.Equal(added, added.With("mvs"));

        Assert.Equal(["MVS"], added.Without("#prod").Names);
        Assert.Equal(added, added.Without("nothing"));
    }

    /// <summary>From drops a ninth name because it repairs a hand-edited file; With is a programmatic append,
    /// and one that silently vanished is what the picker's FAVORITE guard once had to work around.</summary>
    [Fact]
    public void With_refuses_a_name_the_cap_would_drop_and_CanAdd_says_so_first()
    {
        var full = TagSet.From(Enumerable.Range(0, TagSet.MaxTags).Select(i => $"T{i}"));
        Assert.False(full.CanAdd("MORE"));
        Assert.True(full.CanAdd("t0"));
        Assert.Equal(full, full.With("t0"));
        Assert.Throws<InvalidOperationException>(() => full.With("MORE"));

        var oneShort = TagSet.From(Enumerable.Range(1, TagSet.MaxTags - 1).Select(i => $"T{i}"));
        Assert.True(oneShort.CanAdd("T0"));
        Assert.Equal(TagSet.MaxTags, oneShort.With("T0").Count);

        Assert.False(TagSet.Empty.CanAdd(" "));
        Assert.False(TagSet.Empty.CanAdd(new string('x', TagSet.MaxNameLength + 1)));
    }

    [Fact]
    public void ToString_round_trips_through_Split_and_From()
    {
        var tags = TagSet.From(["PROD", "MVS"]);
        Assert.Equal("PROD, MVS", tags.ToString());
        Assert.Equal(tags, TagSet.From(TagSet.Split(tags.ToString())));
    }

    [Fact]
    public void Split_takes_the_editors_comma_separated_box_including_a_trailing_comma()
    {
        Assert.Equal(["PROD", "MVS"], TagSet.Split(" PROD , #MVS ,, "));
        Assert.Empty(TagSet.Split("   "));
    }

    [Fact]
    public void Rename_replaces_a_name_in_place()
    {
        var renamed = TagSet.From(["DEV", "MVS", "LAB"]).Rename("mvs", "TEST");
        Assert.Equal(["DEV", "TEST", "LAB"], renamed.Names);
    }

    /// <summary>A merge: the set already carried the name being renamed to. One copy survives, at the earlier of
    /// the two positions.</summary>
    [Fact]
    public void Rename_onto_a_name_already_carried_keeps_one_copy_at_the_first_position()
    {
        Assert.Equal(["PROD", "MVS"], TagSet.From(["PRDO", "MVS", "PROD"]).Rename("PRDO", "PROD").Names);
        Assert.Equal(["PROD"], TagSet.From(["PROD", "PRDO"]).Rename("PRDO", "PROD").Names);
    }

    [Fact]
    public void Rename_of_a_name_the_set_does_not_carry_changes_nothing()
    {
        Assert.Equal(["MVS"], TagSet.From(["MVS"]).Rename("PRDO", "PROD").Names);
        TagSet none = default;
        Assert.True(none.Rename("A", "B").IsEmpty);
    }

    /// <summary>Equality ignores case, so a case-only rename is "equal" to the set it came from and only Names
    /// shows the change. TagMaintenance compares Names ordinally for exactly this reason (spec 4.3).</summary>
    [Fact]
    public void A_case_only_rename_changes_the_stored_casing_while_equality_still_holds()
    {
        var before = TagSet.From(["dev", "MVS"]);
        var after = before.Rename("dev", "DEV");
        Assert.Equal(["DEV", "MVS"], after.Names);
        Assert.Equal(before, after);
    }
}
