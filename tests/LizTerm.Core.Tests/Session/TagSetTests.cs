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
}
