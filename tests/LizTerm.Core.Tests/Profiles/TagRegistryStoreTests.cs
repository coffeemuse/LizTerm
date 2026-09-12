// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.Core.Profiles;

namespace LizTerm.Core.Tests.Profiles;

public class TagRegistryStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lizterm-tags-" + Guid.NewGuid().ToString("N"));

    private string File_ => Path.Combine(_dir, "tags.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void A_missing_file_loads_as_an_empty_registry()
    {
        Assert.Empty(new TagRegistryStore(File_).Load().Stored);
    }

    [Fact]
    public void Save_then_Load_round_trips_every_definition_and_writes_the_colour_by_name()
    {
        var store = new TagRegistryStore(File_);
        var (registry, _) = TagRegistry.Empty.Register(["PROD", "MVS"]);
        store.Save(registry);

        var json = File.ReadAllText(File_);
        Assert.Contains($"\"color\": \"{registry.ColorOf("PROD")}\"", json);

        var loaded = store.Load();
        Assert.Equal(["MVS", "PROD"], loaded.Stored.Select(d => d.Name));
        Assert.Equal(registry.ColorOf("PROD"), loaded.ColorOf("PROD"));
        Assert.Equal(registry.ColorOf("MVS"), loaded.ColorOf("MVS"));
    }

    /// <summary>The reserved tag is synthesised on load, so writing it would let a hand-edit recolour it.</summary>
    [Fact]
    public void The_reserved_tag_is_never_written_to_the_file()
    {
        var store = new TagRegistryStore(File_);
        store.Save(TagRegistry.Empty);
        Assert.DoesNotContain(TagRegistry.FavoriteName, File.ReadAllText(File_));
    }

    /// <summary>One unrecognised colour costs that tag its colour, not the whole registry — losing every
    /// definition to a single typo would be harsh where losing one is invisible, because reconciliation
    /// re-registers the name with a fresh colour on the next load.</summary>
    [Fact]
    public void An_unrecognised_colour_drops_that_entry_alone()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(File_, """
            { "tags": [ { "name": "PROD", "color": "Chartreuse" }, { "name": "MVS", "color": "Blue" } ] }
            """);

        var loaded = new TagRegistryStore(File_).Load();
        Assert.Equal(["MVS"], loaded.Stored.Select(d => d.Name));
        Assert.Equal(TagColor.Blue, loaded.ColorOf("MVS"));
    }

    /// <summary>Enum.TryParse accepts any number in range of the underlying type, so "8" parses as the undefined
    /// (TagColor)8 rather than failing. Left in, it reached TagPalette's dictionary during rendering and made the
    /// picker unopenable with a KeyNotFoundException naming neither the tag nor this file (spec 4.3, which asks
    /// for per-entry repair).</summary>
    [Theory]
    [InlineData("8")]
    [InlineData("99")]
    [InlineData("-1")]
    public void An_out_of_range_numeric_colour_drops_that_entry_alone(string colour)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(File_, $$"""
            { "tags": [ { "name": "PROD", "color": "{{colour}}" }, { "name": "MVS", "color": "Blue" } ] }
            """);

        var loaded = new TagRegistryStore(File_).Load();
        Assert.Equal(["MVS"], loaded.Stored.Select(d => d.Name));
        Assert.All(loaded.Stored, d => Assert.True(Enum.IsDefined(d.Color)));
    }

    [Fact]
    public void A_colour_name_reads_back_whatever_its_casing()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(File_, """{ "tags": [ { "name": "PROD", "color": "blue" } ] }""");
        Assert.Equal(TagColor.Blue, new TagRegistryStore(File_).Load().ColorOf("PROD"));
    }

    [Fact]
    public void An_unreadable_file_loads_as_an_empty_registry_rather_than_throwing()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(File_, "not json");
        Assert.Empty(new TagRegistryStore(File_).Load().Stored);
    }

    /// <summary>Written through a sibling temp file renamed over the target, as ProfileStore and SettingsStore
    /// both are, so a reader never sees a partial file and a crash mid-write leaves the old one.</summary>
    [Fact]
    public void Save_leaves_no_temp_file_behind()
    {
        var store = new TagRegistryStore(File_);
        var (registry, _) = TagRegistry.Empty.Register(["PROD"]);
        store.Save(registry);
        Assert.Equal(["tags.json"], Directory.GetFiles(_dir).Select(Path.GetFileName).Order());
    }

    [Fact]
    public void Save_creates_the_directory_when_it_is_missing()
    {
        var store = new TagRegistryStore(Path.Combine(_dir, "nested", "tags.json"));
        var (registry, _) = TagRegistry.Empty.Register(["PROD"]);
        store.Save(registry);
        Assert.Equal(["PROD"], store.Load().Stored.Select(d => d.Name));
    }

    [Fact]
    public void The_default_file_sits_beside_the_settings_file()
    {
        Assert.Equal(Path.Combine(AppPaths.ConfigRoot(), "tags.json"), AppPaths.TagsFile());
        Assert.Equal(AppPaths.TagsFile(), TagRegistryStore.DefaultFile());
    }
}
