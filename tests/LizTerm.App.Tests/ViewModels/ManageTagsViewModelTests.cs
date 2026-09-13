// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Collections.Specialized;
using LizTerm.App.ViewModels;
using LizTerm.Core.Profiles;
using LizTerm.Core.Session;

namespace LizTerm.App.Tests.ViewModels;

public class ManageTagsViewModelTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lizterm-managetags-" + Guid.NewGuid().ToString("N"));
    private readonly ProfileStore _profiles;
    private readonly TagRegistryStore _tags;

    public ManageTagsViewModelTests()
    {
        _profiles = new ProfileStore(Path.Combine(_dir, "profiles"));
        _tags = new TagRegistryStore(Path.Combine(_dir, "tags.json"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private void Save(string name, params string[] tags) =>
        _profiles.Save(new SessionProfile { Name = name, Host = "h", Tags = TagSet.From(tags) });

    private void Define(params (string Name, TagColor Color)[] definitions) =>
        _tags.Save(new TagRegistry(definitions.Select(d => new TagDefinition(d.Name, d.Color))));

    private IReadOnlyList<string> TagsOf(string profile) => _profiles.Load(profile)!.Tags.Names;

    private ManageTagsViewModel Open() => new(new TagMaintenance(_profiles, _tags));

    private static TagListRow Row(ManageTagsViewModel vm, string name) => vm.Rows.Single(r => r.Name == name);

    /// <summary>The spec's and the mockups' sample set: a starred profile, an unused LAB, a PRDO typo.</summary>
    private void Seed()
    {
        Save("gateway", "PROD", "TLS");
        Save("mvsce", "FAVORITE", "PRDO", "MVS", "PROD");
        Define(("LAB", TagColor.Teal), ("MVS", TagColor.Blue), ("PRDO", TagColor.Purple), ("PROD", TagColor.Amber),
            ("TLS", TagColor.Green));
    }

    [Fact]
    public void The_list_is_the_reserved_tag_then_the_rest_alphabetically_unused_ones_included()
    {
        Seed();
        var vm = Open();

        Assert.Equal(["FAVORITE", "LAB", "MVS", "PRDO", "PROD", "TLS"], vm.Rows.Select(r => r.Name));
        Assert.Equal(["1", "unused", "1", "1", "2", "1"], vm.Rows.Select(r => r.CountText));
        Assert.True(Row(vm, "FAVORITE").IsReserved);
        Assert.Equal(["gateway", "mvsce"], Row(vm, "PROD").UsedBy);
        Assert.Equal("PROD", Row(vm, "PROD").Chip.Text);
    }

    [Fact]
    public void It_opens_with_nothing_selected()
    {
        Seed();
        var vm = Open();

        Assert.Null(vm.SelectedRow);
        Assert.False(vm.HasSelection);
        Assert.Empty(vm.Swatches);
    }

    [Fact]
    public void Rename_is_offered_only_for_a_valid_name_that_differs_from_the_stored_one()
    {
        Seed();
        var vm = Open();
        vm.SelectedRow = Row(vm, "PROD");
        Assert.Equal("PROD", vm.NameText);
        Assert.False(vm.CanRename);

        vm.NameText = "#PROD";
        Assert.False(vm.CanRename);
        Assert.Null(vm.ValidationMessage);

        vm.NameText = "prod";
        Assert.True(vm.CanRename);

        vm.NameText = "PROD, MVS";
        Assert.False(vm.CanRename);
        Assert.Equal("A tag name can't contain a comma.", vm.ValidationMessage);

        vm.NameText = "LIVE";
        Assert.True(vm.RenameCommand.CanExecute(null));
        Assert.Null(vm.ValidationMessage);
    }

    [Fact]
    public void A_rename_to_a_new_name_applies_at_once_and_the_selection_follows_it()
    {
        Seed();
        var vm = Open();
        vm.SelectedRow = Row(vm, "TLS");
        vm.NameText = "SSL";

        vm.RenameCommand.Execute(null);

        Assert.Null(vm.PendingConfirmation);
        Assert.Equal(["PROD", "SSL"], TagsOf("gateway"));
        Assert.Equal("SSL", vm.SelectedRow?.Name);
        Assert.DoesNotContain(vm.Rows, r => r.Name == "TLS");
    }

    [Fact]
    public void A_merge_asks_first_and_writes_nothing_until_confirmed()
    {
        Seed();
        var vm = Open();
        vm.SelectedRow = Row(vm, "PRDO");
        vm.NameText = "PROD";

        vm.RenameCommand.Execute(null);

        Assert.Equal("PROD already exists. Merge PRDO into it? mvsce will carry PROD instead, and PRDO's colour is dropped.",
            vm.PendingConfirmation);
        Assert.Equal("Merge", vm.ConfirmLabel);
        Assert.Equal(["FAVORITE", "PRDO", "MVS", "PROD"], TagsOf("mvsce"));

        vm.ConfirmCommand.Execute(null);

        Assert.Null(vm.PendingConfirmation);
        Assert.Equal(["FAVORITE", "PROD", "MVS"], TagsOf("mvsce"));
        Assert.Equal("PROD", vm.SelectedRow?.Name);
        Assert.DoesNotContain(vm.Rows, r => r.Name == "PRDO");
    }

    /// <summary>Rename decides merge-or-not from the registry as it is NOW, not the snapshot cached when the
    /// dialog opened: another window (File > Save as Profile..., spec 7.1) can define a tag while Manage Tags is
    /// open, and a merge cannot be undone (spec 2.3), so it must still ask.</summary>
    [Fact]
    public void A_tag_defined_elsewhere_while_the_dialog_is_open_still_asks_before_merging()
    {
        Seed();
        var vm = Open();
        vm.SelectedRow = Row(vm, "TLS");

        // Stands in for another window defining a tag while this one is open: written straight through the
        // store, never through this view model, so the snapshot Open() read knows nothing about it.
        var current = _tags.Load();
        _tags.Save(new TagRegistry([.. current.Stored, new TagDefinition("LIVE", TagColor.Red)]));

        vm.NameText = "LIVE";
        vm.RenameCommand.Execute(null);

        Assert.Equal("LIVE already exists. Merge TLS into it? gateway will carry LIVE instead, and TLS's colour is dropped.",
            vm.PendingConfirmation);
        Assert.Contains("TLS", TagsOf("gateway"));
        Assert.DoesNotContain("LIVE", TagsOf("gateway"));
    }

    /// <summary>The question names the carriers the action will change, so it has to come from the same fresh read
    /// the merge decision does: a profile saved from a session window while the dialog is open is a carrier too.</summary>
    [Fact]
    public void The_merge_question_names_the_carriers_as_they_are_now()
    {
        Seed();
        var vm = Open();
        vm.SelectedRow = Row(vm, "TLS");
        Save("tk5", "TLS");

        vm.NameText = "PROD";
        vm.RenameCommand.Execute(null);

        Assert.Equal("PROD already exists. Merge TLS into it? gateway and tk5 will carry PROD instead, and TLS's colour is dropped.",
            vm.PendingConfirmation);
    }

    [Fact]
    public void The_delete_question_names_the_carriers_as_they_are_now()
    {
        Seed();
        var vm = Open();
        vm.SelectedRow = Row(vm, "LAB");
        Save("tk5", "LAB");

        vm.DeleteCommand.Execute(null);

        Assert.Equal("Delete LAB? It is removed from tk5.", vm.PendingConfirmation);
    }

    [Fact]
    public void Merging_an_unused_tag_says_only_its_colour_goes()
    {
        Seed();
        var vm = Open();
        vm.SelectedRow = Row(vm, "LAB");
        vm.NameText = "mvs";

        vm.RenameCommand.Execute(null);

        Assert.Equal("MVS already exists. Merge LAB into it? No profile uses LAB, so only its colour is dropped.",
            vm.PendingConfirmation);
    }

    [Fact]
    public void Delete_asks_first_naming_the_profiles_it_will_change()
    {
        Seed();
        var vm = Open();
        vm.SelectedRow = Row(vm, "PROD");

        vm.DeleteCommand.Execute(null);

        Assert.Equal("Delete PROD? It is removed from gateway and mvsce.", vm.PendingConfirmation);
        Assert.Equal("Delete", vm.ConfirmLabel);
        Assert.False(vm.ShowDeleteButton);
        Assert.Contains("PROD", TagsOf("gateway"));

        vm.ConfirmCommand.Execute(null);

        Assert.Equal(["TLS"], TagsOf("gateway"));
        Assert.Null(vm.SelectedRow);
        Assert.DoesNotContain(vm.Rows, r => r.Name == "PROD");
    }

    [Fact]
    public void Deleting_an_unused_tag_still_asks()
    {
        Seed();
        var vm = Open();
        vm.SelectedRow = Row(vm, "LAB");

        vm.DeleteCommand.Execute(null);

        Assert.Equal("Delete LAB? No profile uses it.", vm.PendingConfirmation);
    }

    [Fact]
    public void Three_profiles_are_named_and_a_fourth_turns_the_list_into_a_count()
    {
        Save("a", "DEV");
        Save("b", "DEV");
        Save("c", "DEV");
        Define(("DEV", TagColor.Teal));
        var vm = Open();
        vm.SelectedRow = Row(vm, "DEV");
        vm.DeleteCommand.Execute(null);
        Assert.Equal("Delete DEV? It is removed from a, b and c.", vm.PendingConfirmation);

        Save("d", "DEV");
        vm = Open();
        vm.SelectedRow = Row(vm, "DEV");
        vm.DeleteCommand.Execute(null);
        Assert.Equal("Delete DEV? It is removed from 4 profiles.", vm.PendingConfirmation);
    }

    [Fact]
    public void A_pending_question_is_withdrawn_by_selecting_editing_or_acting()
    {
        Seed();
        var vm = Open();

        vm.SelectedRow = Row(vm, "PROD");
        vm.DeleteCommand.Execute(null);
        vm.SelectedRow = Row(vm, "MVS");
        Assert.Null(vm.PendingConfirmation);

        vm.DeleteCommand.Execute(null);
        vm.NameText = "MVS2";
        Assert.Null(vm.PendingConfirmation);

        vm.DeleteCommand.Execute(null);
        vm.RecolourCommand.Execute(TagColor.Red);
        Assert.Null(vm.PendingConfirmation);

        vm.ConfirmCommand.Execute(null);
        Assert.Equal(["FAVORITE", "PRDO", "MVS", "PROD"], TagsOf("mvsce"));
        Assert.Contains("PROD", TagsOf("gateway"));
    }

    [Fact]
    public void Recolour_applies_at_once_moves_the_swatch_and_keeps_the_selection()
    {
        Seed();
        var vm = Open();
        vm.SelectedRow = Row(vm, "MVS");
        Assert.Equal(7, vm.Swatches.Count);
        Assert.DoesNotContain(vm.Swatches, s => s.Color == TagColor.Gold);
        Assert.Equal(TagColor.Blue, vm.Swatches.Single(s => s.IsSelected).Color);

        vm.RecolourCommand.Execute(TagColor.Green);

        Assert.Equal(TagColor.Green, _tags.Load().ColorOf("MVS"));
        Assert.Equal("MVS", vm.SelectedRow?.Name);
        Assert.Equal(TagColor.Green, vm.Swatches.Single(s => s.IsSelected).Color);
    }

    [Fact]
    public void The_reserved_row_offers_no_action_but_still_lists_its_profiles()
    {
        Seed();
        var vm = Open();

        vm.SelectedRow = Row(vm, "FAVORITE");

        Assert.True(vm.IsReservedSelected);
        Assert.False(vm.IsTagSelected);
        Assert.False(vm.CanRename);
        Assert.False(vm.DeleteCommand.CanExecute(null));
        Assert.False(vm.RecolourCommand.CanExecute(TagColor.Red));
        Assert.False(vm.ShowDeleteButton);
        Assert.Empty(vm.Swatches);
        Assert.Equal(["mvsce"], vm.SelectedRow!.UsedBy);
    }

    /// <summary>What a SelectingItemsControl bound two-way to SelectedItem does when its items are cleared: it nulls
    /// its own selection and the binding writes that back. A refresh that read the selection after clearing would
    /// lose it.</summary>
    [Fact]
    public void The_selection_survives_a_refresh_even_when_the_bound_list_nulls_it()
    {
        Seed();
        var vm = Open();
        vm.SelectedRow = Row(vm, "PROD");
        vm.Rows.CollectionChanged += (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Reset) vm.SelectedRow = null;
        };

        vm.RecolourCommand.Execute(TagColor.Red);

        Assert.Equal("PROD", vm.SelectedRow?.Name);
    }

    private string TagsFile => Path.Combine(_dir, "tags.json");

    private string ProfileFile(string name) => Path.Combine(_dir, "profiles", ProfileStore.FileNameFor(name));

    /// <summary>A directory at the file's .tmp path makes its next save fail (see TagMaintenanceTests.Block).</summary>
    private static void Block(string file) => Directory.CreateDirectory(file + ".tmp");

    [Fact]
    public void A_rename_that_fails_partway_says_how_far_it_got_and_how_to_finish()
    {
        Save("alpha", "DEV");
        Save("beta", "DEV");
        Save("gamma", "DEV");
        Define(("DEV", TagColor.Teal));
        Block(ProfileFile("beta"));
        var vm = Open();
        vm.SelectedRow = Row(vm, "DEV");
        vm.NameText = "test";

        vm.RenameCommand.Execute(null);

        Assert.StartsWith("Renamed on 1 of 3 profiles. Could not write beta: ", vm.StatusMessage);
        Assert.EndsWith("\nRename DEV to TEST again to finish.", vm.StatusMessage);
        // The message says "Rename DEV to TEST again to finish", which needs DEV selected, not the name the
        // rename stopped partway through (controller ruling, F7).
        Assert.Equal("DEV", vm.SelectedRow?.Name);
        Assert.Contains(vm.Rows, r => r.Name == "DEV");
    }

    [Fact]
    public void A_case_only_rename_that_fails_partway_shows_both_names_as_typed()
    {
        Save("alpha", "dev");
        Save("beta", "dev");
        Define(("dev", TagColor.Teal));
        Block(ProfileFile("beta"));
        var vm = Open();
        vm.SelectedRow = Row(vm, "dev");
        vm.NameText = "DEV";

        vm.RenameCommand.Execute(null);

        Assert.StartsWith("Renamed on 1 of 2 profiles. Could not write beta: ", vm.StatusMessage);
        Assert.EndsWith("\nRename dev to DEV again to finish.", vm.StatusMessage);
    }

    [Fact]
    public void A_registry_that_cannot_be_saved_after_a_rename_or_a_merge_says_what_that_costs()
    {
        Seed();
        Block(TagsFile);
        var vm = Open();

        vm.SelectedRow = Row(vm, "TLS");
        vm.NameText = "SSL";
        vm.RenameCommand.Execute(null);
        Assert.StartsWith("Every profile was updated, but tags.json could not be saved: ", vm.StatusMessage);
        Assert.EndsWith("\nSSL may show a different colour next time.", vm.StatusMessage);

        vm.SelectedRow = Row(vm, "PRDO");
        vm.NameText = "PROD";
        vm.RenameCommand.Execute(null);
        vm.ConfirmCommand.Execute(null);
        Assert.StartsWith("Every profile was updated, but tags.json could not be saved: ", vm.StatusMessage);
        Assert.EndsWith("\nPRDO may still be listed.", vm.StatusMessage);
    }

    [Fact]
    public void A_registry_that_cannot_be_saved_after_a_case_only_rename_says_only_that()
    {
        Save("a", "dev");
        Define(("dev", TagColor.Teal));
        Block(TagsFile);
        var vm = Open();
        vm.SelectedRow = Row(vm, "dev");
        vm.NameText = "DEV";

        vm.RenameCommand.Execute(null);

        Assert.StartsWith("Every profile was updated, but tags.json could not be saved: ", vm.StatusMessage);
        Assert.DoesNotContain("\n", vm.StatusMessage);
    }

    [Fact]
    public void A_delete_that_fails_says_how_far_it_got_or_what_it_costs()
    {
        Save("alpha", "LAB");
        Save("beta", "LAB");
        Define(("LAB", TagColor.Teal));
        Block(ProfileFile("beta"));
        var vm = Open();
        vm.SelectedRow = Row(vm, "LAB");

        vm.DeleteCommand.Execute(null);
        vm.ConfirmCommand.Execute(null);

        Assert.StartsWith("Removed from 1 of 2 profiles. Could not write beta: ", vm.StatusMessage);
        Assert.EndsWith("\nDelete LAB again to finish.", vm.StatusMessage);
        Assert.Equal("LAB", vm.SelectedRow?.Name);

        Directory.Delete(ProfileFile("beta") + ".tmp");
        Block(TagsFile);
        vm.DeleteCommand.Execute(null);
        vm.ConfirmCommand.Execute(null);

        Assert.StartsWith("Every profile was updated, but tags.json could not be saved: ", vm.StatusMessage);
        Assert.EndsWith("\nLAB may still be listed.", vm.StatusMessage);
    }

    [Fact]
    public void A_recolour_that_cannot_be_saved_says_so_and_the_swatch_stays()
    {
        Save("a", "MVS");
        Define(("MVS", TagColor.Blue));
        Block(TagsFile);
        var vm = Open();
        vm.SelectedRow = Row(vm, "MVS");

        vm.RecolourCommand.Execute(TagColor.Green);

        Assert.StartsWith("Could not save the colour: ", vm.StatusMessage);
        Assert.Equal(TagColor.Blue, vm.Swatches.Single(s => s.IsSelected).Color);
    }

    [Fact]
    public void A_failure_message_outlasts_a_change_of_selection_and_goes_with_the_next_action()
    {
        Save("a", "LAB", "MVS");
        Define(("LAB", TagColor.Teal), ("MVS", TagColor.Blue));
        Block(TagsFile);
        var vm = Open();
        vm.SelectedRow = Row(vm, "MVS");
        vm.RecolourCommand.Execute(TagColor.Green);
        Assert.NotNull(vm.StatusMessage);

        vm.SelectedRow = Row(vm, "LAB");
        Assert.NotNull(vm.StatusMessage);

        Directory.Delete(TagsFile + ".tmp");
        vm.RecolourCommand.Execute(TagColor.Red);
        Assert.Null(vm.StatusMessage);
    }
}
