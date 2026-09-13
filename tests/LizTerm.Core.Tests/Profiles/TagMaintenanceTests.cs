// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.Core.Profiles;
using LizTerm.Core.Session;

namespace LizTerm.Core.Tests.Profiles;

public class TagMaintenanceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lizterm-maintenance-" + Guid.NewGuid().ToString("N"));
    private readonly ProfileStore _profiles;
    private readonly TagRegistryStore _tags;
    private readonly TagMaintenance _maintenance;

    public TagMaintenanceTests()
    {
        _profiles = new ProfileStore(Path.Combine(_dir, "profiles"));
        _tags = new TagRegistryStore(Path.Combine(_dir, "tags.json"));
        _maintenance = new TagMaintenance(_profiles, _tags);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private string TagsFile => Path.Combine(_dir, "tags.json");

    private string ProfileFile(string name) => Path.Combine(_dir, "profiles", ProfileStore.FileNameFor(name));

    /// <summary>Makes every later save of one file fail, without a mock: ProfileStore and TagRegistryStore both
    /// write a sibling "&lt;file&gt;.tmp" first, and a directory squatting on that path makes the write throw
    /// UnauthorizedAccessException. A test that blocks a file it expects to be left alone turns "was it touched?"
    /// into a failure you cannot miss.</summary>
    private static void Block(string file) => Directory.CreateDirectory(file + ".tmp");

    private void Save(string name, params string[] tags) =>
        _profiles.Save(new SessionProfile { Name = name, Host = "h", Tags = TagSet.From(tags) });

    private void Define(params (string Name, TagColor Color)[] definitions) =>
        _tags.Save(new TagRegistry(definitions.Select(d => new TagDefinition(d.Name, d.Color))));

    private IReadOnlyList<string> TagsOf(string profile) => _profiles.Load(profile)!.Tags.Names;

    [Theory]
    [InlineData("", "A tag name can't be blank.")]
    [InlineData("  # ", "A tag name can't be blank.")]
    [InlineData("ABCDEFGHIJKLMNOPQ", "Tag names can be at most 16 characters.")]
    [InlineData("PROD, MVS", "A tag name can't contain a comma.")]
    [InlineData("favorite", "FAVORITE is reserved.")]
    public void RenameProblem_names_the_rule_a_target_breaks(string name, string message) =>
        Assert.Equal(message, TagMaintenance.RenameProblem(name));

    [Theory]
    [InlineData("PROD")]
    [InlineData("#PROD")]
    [InlineData("ABCDEFGHIJKLMNOP")]
    public void RenameProblem_accepts_a_valid_name(string name) => Assert.Null(TagMaintenance.RenameProblem(name));

    [Fact]
    public void Load_registers_a_tag_only_a_profile_knows_and_saves_the_registry()
    {
        Save("a", "PROD");

        var snapshot = _maintenance.Load();

        Assert.True(snapshot.Registry.Contains("PROD"));
        Assert.Equal(["a"], snapshot.Profiles.Select(p => p.Name));
        Assert.True(_tags.Load().Contains("PROD"));
    }

    /// <summary>Asserted through formatting: the store writes indented JSON, so a rewrite of this hand-written
    /// one-line file would change its text.</summary>
    [Fact]
    public void Load_does_not_rewrite_the_registry_when_every_tag_is_known()
    {
        Save("a", "PROD");
        const string handWritten = """{"tags":[{"name":"PROD","color":"Red"}]}""";
        Directory.CreateDirectory(_dir);
        File.WriteAllText(TagsFile, handWritten);

        _maintenance.Load();

        Assert.Equal(handWritten, File.ReadAllText(TagsFile));
    }

    [Fact]
    public void Load_survives_a_registry_it_cannot_save()
    {
        Save("a", "PROD");
        Block(TagsFile);

        var snapshot = _maintenance.Load();

        Assert.True(snapshot.Registry.Contains("PROD"));
        Assert.False(File.Exists(TagsFile));
    }

    [Fact]
    public void Rename_rewrites_every_carrier_in_place_and_leaves_their_other_fields_and_other_profiles_alone()
    {
        var pin = new CertificatePin("8C:13:6A:01", "CN = localhost", "-----BEGIN CERTIFICATE-----\nZmFrZQ==\n-----END CERTIFICATE-----\n");
        _profiles.Save(new SessionProfile
        {
            Name = "gateway", Host = "gw", UseTls = true, PinnedCertificate = pin, Note = "no live data",
            Tags = TagSet.From(["TLS", "DEV"]),
        });
        Save("tk5", "DEV");
        Save("mvsce", "MVS");
        Define(("DEV", TagColor.Teal), ("MVS", TagColor.Blue), ("TLS", TagColor.Green));
        Block(ProfileFile("mvsce"));

        var result = _maintenance.Rename("dev", "TEST");

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Carriers);
        Assert.Equal(["gateway", "tk5"], result.Changed);
        Assert.Equal(["TLS", "TEST"], TagsOf("gateway"));
        Assert.Equal(["TEST"], TagsOf("tk5"));
        var gateway = _profiles.Load("gateway")!;
        Assert.Equal("no live data", gateway.Note);
        Assert.Equal(pin, gateway.PinnedCertificate);
        var registry = _tags.Load();
        Assert.False(registry.Contains("DEV"));
        Assert.Equal(TagColor.Teal, registry.ColorOf("TEST"));
    }

    [Fact]
    public void Renaming_onto_an_existing_tag_merges_and_keeps_its_colour()
    {
        Save("mvsce", "PRDO", "MVS", "PROD");
        Save("gateway", "PROD");
        Define(("MVS", TagColor.Blue), ("PRDO", TagColor.Purple), ("PROD", TagColor.Amber));
        Block(ProfileFile("gateway"));

        var result = _maintenance.Rename("PRDO", "PROD");

        Assert.True(result.Succeeded);
        Assert.Equal(["mvsce"], result.Changed);
        Assert.Equal(["PROD", "MVS"], TagsOf("mvsce"));
        var registry = _tags.Load();
        Assert.False(registry.Contains("PRDO"));
        Assert.Equal(TagColor.Amber, registry.ColorOf("PROD"));
    }

    /// <summary>Spec 4.3's regression: a "did it change?" check built on TagSet.Equals, which ignores case, would
    /// skip this write and the rename would silently do nothing.</summary>
    [Fact]
    public void A_case_only_rename_really_writes_the_new_casing()
    {
        Save("a", "dev");
        Define(("dev", TagColor.Teal));

        var result = _maintenance.Rename("dev", "DEV");

        Assert.Equal(["a"], result.Changed);
        Assert.Equal(["DEV"], TagsOf("a"));
        Assert.Equal(["DEV"], _tags.Load().Stored.Select(d => d.Name));
        Assert.Equal(TagColor.Teal, _tags.Load().ColorOf("DEV"));
    }

    [Fact]
    public void Renaming_to_the_identical_name_or_renaming_an_unknown_tag_writes_nothing()
    {
        Save("a", "DEV");
        Define(("DEV", TagColor.Teal));
        Block(ProfileFile("a"));
        Block(TagsFile);

        foreach (var result in new[] { _maintenance.Rename("DEV", "#DEV"), _maintenance.Rename("LAB", "TEST") })
        {
            Assert.True(result.Succeeded);
            Assert.Equal(0, result.Carriers);
            Assert.Empty(result.Changed);
        }
    }

    [Fact]
    public void Delete_strips_the_tag_from_every_carrier_and_drops_its_definition()
    {
        Save("gateway", "PROD", "TLS");
        Save("mvsce", "FAVORITE", "PROD");
        Save("tk5", "MVS");
        Define(("MVS", TagColor.Blue), ("PROD", TagColor.Amber), ("TLS", TagColor.Green));
        Block(ProfileFile("tk5"));

        var result = _maintenance.Delete("prod");

        Assert.True(result.Succeeded);
        Assert.Equal(["gateway", "mvsce"], result.Changed);
        Assert.Equal(["TLS"], TagsOf("gateway"));
        Assert.Equal(["FAVORITE"], TagsOf("mvsce"));
        Assert.Equal(["MVS", "TLS"], _tags.Load().Stored.Select(d => d.Name));
    }

    [Fact]
    public void Deleting_an_unused_tag_only_touches_the_registry()
    {
        Save("a", "MVS");
        Define(("LAB", TagColor.Teal), ("MVS", TagColor.Blue));
        Block(ProfileFile("a"));

        var result = _maintenance.Delete("LAB");

        Assert.True(result.Succeeded);
        Assert.Equal(0, result.Carriers);
        Assert.False(_tags.Load().Contains("LAB"));
    }

    [Fact]
    public void Recolour_writes_the_registry_and_no_profile()
    {
        Save("a", "MVS");
        Define(("MVS", TagColor.Blue));
        Block(ProfileFile("a"));

        var result = _maintenance.Recolour("mvs", TagColor.Green);

        Assert.True(result.Succeeded);
        Assert.Equal(TagColor.Green, _tags.Load().ColorOf("MVS"));
    }

    [Fact]
    public void Each_action_reads_the_profiles_as_they_are_on_disk_now()
    {
        Save("a", "DEV");
        Define(("DEV", TagColor.Teal));
        _ = _maintenance.Load();
        Save("b", "DEV");

        var result = _maintenance.Rename("DEV", "TEST");

        Assert.Equal(["a", "b"], result.Changed);
    }

    [Fact]
    public void The_reserved_tag_an_invalid_target_and_the_reserved_colour_throw_and_change_nothing()
    {
        Save("a", "FAVORITE", "DEV");
        Define(("DEV", TagColor.Teal));

        Assert.Throws<ArgumentException>(() => _maintenance.Rename("FAVORITE", "STAR"));
        Assert.Throws<ArgumentException>(() => _maintenance.Rename("DEV", "favorite"));
        Assert.Throws<ArgumentException>(() => _maintenance.Rename("DEV", "A, B"));
        Assert.Throws<ArgumentException>(() => _maintenance.Delete("FAVORITE"));
        Assert.Throws<ArgumentException>(() => _maintenance.Recolour("FAVORITE", TagColor.Red));
        Assert.Throws<ArgumentException>(() => _maintenance.Recolour("DEV", TagColor.Gold));

        Assert.Equal(["FAVORITE", "DEV"], TagsOf("a"));
        Assert.Equal(TagColor.Teal, _tags.Load().ColorOf("DEV"));
    }

    [Fact]
    public void A_rename_that_fails_partway_reports_what_changed_and_defines_both_names_in_one_colour()
    {
        Save("alpha", "DEV");
        Save("beta", "DEV");
        Save("gamma", "DEV");
        Define(("DEV", TagColor.Teal));
        Block(ProfileFile("beta"));

        var result = _maintenance.Rename("DEV", "TEST");

        Assert.False(result.Succeeded);
        Assert.Equal(3, result.Carriers);
        Assert.Equal(["alpha"], result.Changed);
        Assert.Equal("beta", result.FailedProfile);
        Assert.NotNull(result.Error);
        Assert.Equal(["TEST"], TagsOf("alpha"));
        Assert.Equal(["DEV"], TagsOf("beta"));
        Assert.Equal(["DEV"], TagsOf("gamma"));
        var registry = _tags.Load();
        Assert.Equal(TagColor.Teal, registry.ColorOf("DEV"));
        Assert.Equal(TagColor.Teal, registry.ColorOf("TEST"));
    }

    /// <summary>The retry is a merge, because the partial rename defined the new name; merging finishes it.</summary>
    [Fact]
    public void Retrying_a_partial_rename_merges_the_rest_across()
    {
        Save("alpha", "DEV");
        Save("beta", "DEV");
        Save("gamma", "DEV");
        Define(("DEV", TagColor.Teal));
        Block(ProfileFile("beta"));
        _ = _maintenance.Rename("DEV", "TEST");
        Directory.Delete(ProfileFile("beta") + ".tmp");

        var retry = _maintenance.Rename("DEV", "TEST");

        Assert.True(retry.Succeeded);
        Assert.Equal(["beta", "gamma"], retry.Changed);
        Assert.All(new[] { "alpha", "beta", "gamma" }, name => Assert.Equal(["TEST"], TagsOf(name)));
        var registry = _tags.Load();
        Assert.False(registry.Contains("DEV"));
        Assert.Equal(TagColor.Teal, registry.ColorOf("TEST"));
    }

    [Theory]
    [InlineData("merge")]
    [InlineData("delete")]
    public void A_merge_or_delete_that_fails_partway_leaves_the_registry_alone(string action)
    {
        Save("alpha", "PRDO");
        Save("beta", "PRDO");
        Define(("PRDO", TagColor.Purple), ("PROD", TagColor.Amber));
        Block(ProfileFile("beta"));

        var result = action == "merge" ? _maintenance.Rename("PRDO", "PROD") : _maintenance.Delete("PRDO");

        Assert.Equal(["alpha"], result.Changed);
        Assert.Equal("beta", result.FailedProfile);
        Assert.Equal(["PRDO", "PROD"], _tags.Load().Stored.Select(d => d.Name));
    }

    [Fact]
    public void A_rename_that_fails_on_its_first_carrier_changes_neither_profiles_nor_registry()
    {
        Save("alpha", "DEV");
        Save("beta", "DEV");
        Define(("DEV", TagColor.Teal));
        Block(ProfileFile("alpha"));

        var result = _maintenance.Rename("DEV", "TEST");

        Assert.Empty(result.Changed);
        Assert.Equal("alpha", result.FailedProfile);
        Assert.Equal(["DEV"], TagsOf("beta"));
        Assert.Equal(["DEV"], _tags.Load().Stored.Select(d => d.Name));
    }

    [Fact]
    public void A_registry_that_cannot_be_saved_after_every_profile_is_reported_without_a_profile()
    {
        Save("alpha", "DEV");
        Save("beta", "DEV");
        Define(("DEV", TagColor.Teal));
        Block(TagsFile);

        var result = _maintenance.Rename("DEV", "TEST");

        Assert.False(result.Succeeded);
        Assert.Null(result.FailedProfile);
        Assert.Equal(["alpha", "beta"], result.Changed);
        Assert.Equal(["TEST"], TagsOf("beta"));
    }

    [Fact]
    public void A_recolour_that_cannot_be_saved_is_reported()
    {
        Save("a", "MVS");
        Define(("MVS", TagColor.Blue));
        Block(TagsFile);

        var result = _maintenance.Recolour("MVS", TagColor.Green);

        Assert.False(result.Succeeded);
        Assert.Null(result.FailedProfile);
        Assert.Equal(TagColor.Blue, _tags.Load().ColorOf("MVS"));
    }
}
