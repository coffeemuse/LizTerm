// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.Core.Profiles;

namespace LizTerm.Core.Tests.Profiles;

public class TagRegistryTests
{
    [Fact]
    public void The_reserved_tag_is_always_present_always_gold_and_never_stored()
    {
        var registry = TagRegistry.Empty;
        Assert.Equal(TagColor.Gold, registry.ColorOf("FAVORITE"));
        Assert.Equal(TagColor.Gold, registry.ColorOf("favorite"));
        Assert.Contains(TagRegistry.Favorite, registry.All);
        Assert.Empty(registry.Stored);
    }

    /// <summary>"Immutable definition, just changing what items are tagged with it": a hand-edit cannot
    /// recolour or rename it, so an entry for it in a file is ignored rather than honoured.</summary>
    [Fact]
    public void A_file_entry_for_the_reserved_tag_is_ignored()
    {
        var registry = new TagRegistry([new TagDefinition("FAVORITE", TagColor.Red), new TagDefinition("PROD", TagColor.Red)]);
        Assert.Equal(TagColor.Gold, registry.ColorOf("FAVORITE"));
        Assert.Equal(["PROD"], registry.Stored.Select(d => d.Name));
    }

    [Fact]
    public void Gold_is_reserved_and_never_auto_assigned()
    {
        Assert.DoesNotContain(TagColor.Gold, TagRegistry.AssignableColors);
        var (registry, _) = TagRegistry.Empty.Register(Enumerable.Range(0, 20).Select(i => $"T{i}"));
        Assert.DoesNotContain(TagColor.Gold, registry.Stored.Select(d => d.Color));
    }

    [Fact]
    public void Register_gives_each_new_tag_the_least_used_colour_breaking_ties_by_enum_order()
    {
        var (registry, changed) = TagRegistry.Empty.Register(["PROD", "MVS", "TEST"]);
        Assert.True(changed);
        Assert.Equal(
            [TagRegistry.AssignableColors[0], TagRegistry.AssignableColors[1], TagRegistry.AssignableColors[2]],
            new[] { registry.ColorOf("PROD"), registry.ColorOf("MVS"), registry.ColorOf("TEST") });

        // Every assignable colour used once, so the next tag wraps to the first again.
        var (full, _) = TagRegistry.Empty.Register(TagRegistry.AssignableColors.Select((_, i) => $"T{i}"));
        var (wrapped, _) = full.Register(["ONE MORE"]);
        Assert.Equal(TagRegistry.AssignableColors[0], wrapped.ColorOf("ONE MORE"));
    }

    [Fact]
    public void Register_keeps_an_existing_definition_and_reports_no_change_when_every_name_is_known()
    {
        var (first, _) = TagRegistry.Empty.Register(["PROD"]);
        var colour = first.ColorOf("PROD");

        var (second, changed) = first.Register(["prod", "#PROD", "FAVORITE"]);
        Assert.False(changed);
        Assert.Same(first, second);
        Assert.Equal(colour, second.ColorOf("PROD"));
    }

    [Fact]
    public void Register_ignores_blank_and_over_long_names()
    {
        var (registry, changed) = TagRegistry.Empty.Register(["", "   ", new string('x', 17)]);
        Assert.False(changed);
        Assert.Empty(registry.Stored);
    }

    /// <summary>Reconciliation registers before anything renders, so this is a defensive answer rather than a
    /// path a user reaches — but it must be a colour rather than a throw.</summary>
    [Fact]
    public void ColorOf_an_unregistered_tag_is_the_documented_fallback()
    {
        Assert.Equal(TagRegistry.UnregisteredColor, TagRegistry.Empty.ColorOf("NEVER SEEN"));
    }

    [Fact]
    public void All_lists_the_reserved_tag_first_then_the_rest_alphabetically_ignoring_case()
    {
        var registry = new TagRegistry(
            [new TagDefinition("zeta", TagColor.Red), new TagDefinition("Alpha", TagColor.Blue), new TagDefinition("mvs", TagColor.Green)]);
        Assert.Equal(["FAVORITE", "Alpha", "mvs", "zeta"], registry.All.Select(d => d.Name));
    }

    private static TagRegistry Defined(params (string Name, TagColor Color)[] definitions) =>
        new(definitions.Select(d => new TagDefinition(d.Name, d.Color)));

    [Fact]
    public void Contains_ignores_case_and_a_hash_and_always_knows_the_reserved_tag()
    {
        var registry = Defined(("PROD", TagColor.Red));
        Assert.True(registry.Contains("prod"));
        Assert.True(registry.Contains("#PROD"));
        Assert.False(registry.Contains("MVS"));
        Assert.True(TagRegistry.Empty.Contains("FAVORITE"));
    }

    [Fact]
    public void Recolour_changes_one_definition_and_leaves_the_rest()
    {
        var registry = Defined(("PROD", TagColor.Red), ("MVS", TagColor.Blue)).Recolour("prod", TagColor.Green);
        Assert.Equal(TagColor.Green, registry.ColorOf("PROD"));
        Assert.Equal(TagColor.Blue, registry.ColorOf("MVS"));
    }

    [Fact]
    public void Recolour_Rename_and_Remove_of_an_unknown_tag_return_the_same_registry()
    {
        var registry = Defined(("PROD", TagColor.Red));
        Assert.Same(registry, registry.Recolour("MVS", TagColor.Green));
        Assert.Same(registry, registry.Rename("DEV", "TEST"));
        Assert.Same(registry, registry.Remove("LAB"));
    }

    [Fact]
    public void Recolour_refuses_the_reserved_tag_the_reserved_colour_and_an_undefined_one()
    {
        var registry = Defined(("PROD", TagColor.Red));
        Assert.Throws<ArgumentException>(() => registry.Recolour("FAVORITE", TagColor.Red));
        Assert.Throws<ArgumentException>(() => registry.Recolour("PROD", TagColor.Gold));
        Assert.Throws<ArgumentException>(() => registry.Recolour("PROD", (TagColor)99));
    }

    [Fact]
    public void A_plain_rename_carries_the_colour_to_the_new_name()
    {
        var registry = Defined(("DEV", TagColor.Teal), ("MVS", TagColor.Blue)).Rename("DEV", "TEST");
        Assert.Equal(["MVS", "TEST"], registry.Stored.Select(d => d.Name));
        Assert.Equal(TagColor.Teal, registry.ColorOf("TEST"));
    }

    [Fact]
    public void A_case_only_rename_keeps_the_colour_and_takes_the_new_casing()
    {
        var registry = Defined(("dev", TagColor.Teal)).Rename("dev", "DEV");
        Assert.Equal(["DEV"], registry.Stored.Select(d => d.Name));
        Assert.Equal(TagColor.Teal, registry.ColorOf("DEV"));
    }

    [Fact]
    public void Renaming_onto_another_definition_merges_and_keeps_the_targets_colour()
    {
        var registry = Defined(("PRDO", TagColor.Purple), ("PROD", TagColor.Amber)).Rename("PRDO", "PROD");
        Assert.Equal(["PROD"], registry.Stored.Select(d => d.Name));
        Assert.Equal(TagColor.Amber, registry.ColorOf("PROD"));
    }

    [Fact]
    public void Rename_and_Remove_refuse_the_reserved_tag()
    {
        var registry = Defined(("PROD", TagColor.Red));
        Assert.Throws<ArgumentException>(() => registry.Rename("FAVORITE", "STAR"));
        Assert.Throws<ArgumentException>(() => registry.Rename("PROD", "favorite"));
        Assert.Throws<ArgumentException>(() => registry.Remove("FAVORITE"));
    }

    [Fact]
    public void Remove_drops_one_definition()
    {
        var registry = Defined(("PROD", TagColor.Red), ("MVS", TagColor.Blue));
        Assert.Equal(["MVS"], registry.Remove("prod").Stored.Select(d => d.Name));
    }
}
