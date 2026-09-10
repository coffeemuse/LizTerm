// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.App.ViewModels;
using LizTerm.Core.Profiles;
using LizTerm.Core.Session;

namespace LizTerm.App.Tests.ViewModels;

public class ProfileViewModelsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lizterm-vm-" + Guid.NewGuid().ToString("N"));
    private readonly ProfileStore _store;

    public ProfileViewModelsTests() => _store = new ProfileStore(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    [Fact]
    public void Editor_defaults_and_tls_port_flip()
    {
        var vm = new ProfileEditorViewModel(null);
        Assert.Equal("23", vm.PortText);
        Assert.Equal(2, vm.Model);
        Assert.True(vm.Extended);
        Assert.Equal("cp037", vm.CodePage);
        Assert.True(vm.DestructiveBackspace);
        vm.UseTls = true;
        Assert.Equal("992", vm.PortText);
        vm.UseTls = false;
        Assert.Equal("23", vm.PortText);
    }

    [Fact]
    public void Editor_validates_required_fields_and_port()
    {
        var vm = new ProfileEditorViewModel(null);
        Assert.Null(vm.TryBuild());
        Assert.Contains("name", vm.ValidationMessage, StringComparison.OrdinalIgnoreCase);
        vm.Name = "x";
        Assert.Null(vm.TryBuild());
        Assert.Contains("host", vm.ValidationMessage, StringComparison.OrdinalIgnoreCase);
        vm.Host = "h";
        vm.PortText = "99999";
        Assert.Null(vm.TryBuild());
        Assert.Contains("port", vm.ValidationMessage, StringComparison.OrdinalIgnoreCase);
        vm.PortText = "3270";
        var built = vm.TryBuild();
        Assert.NotNull(built);
        Assert.Null(vm.ValidationMessage);
        Assert.Equal(3270, built!.Port);
    }

    [Fact]
    public void Editor_round_trips_an_existing_profile()
    {
        var original = new SessionProfile { Name = "TK5", Host = "mvs", Port = 3270, UseTls = true, VerifyCertificate = false, Model = 5, Extended = false, CodePage = "bracket", LuName = "LU1", DestructiveBackspace = true, PinnedCertificate = new CertificatePin("8C:13", "CN=mvs", "pem") };
        var vm = new ProfileEditorViewModel(original);
        Assert.Equal(original, vm.TryBuild());
    }

    [Fact]
    public async Task Picker_lists_creates_edits_and_deletes()
    {
        _store.Save(new SessionProfile { Name = "b", Host = "b.host" });
        _store.Save(new SessionProfile { Name = "a", Host = "a.host" });
        SessionProfile? opened = null;
        var quit = false;
        SessionProfile? toReturn = new SessionProfile { Name = "c", Host = "c.host" };
        var vm = new ProfilePickerViewModel(_store, p => opened = p, _ => Task.FromResult<ProfileEdit?>(new ProfileEdit(toReturn, PinCleared: false)), () => quit = true);

        Assert.Equal(["a", "b"], vm.Profiles.Select(p => p.Name));
        Assert.False(vm.ConnectCommand.CanExecute(null));

        vm.SelectedProfile = vm.Profiles[1];
        Assert.True(vm.ConnectCommand.CanExecute(null));
        vm.ConnectCommand.Execute(null);
        Assert.Equal("b", opened!.Name);

        await vm.NewCommand.ExecuteAsync(null);
        Assert.Equal(["a", "b", "c"], vm.Profiles.Select(p => p.Name));
        Assert.Equal("c", vm.SelectedProfile!.Name);

        toReturn = new SessionProfile { Name = "c2", Host = "c.host" };
        await vm.EditCommand.ExecuteAsync(null);
        Assert.Equal(["a", "b", "c2"], vm.Profiles.Select(p => p.Name));
        Assert.Equal(3, _store.LoadAll().Count);

        vm.SelectedProfile = vm.Profiles[0];
        vm.DeleteCommand.Execute(null);
        Assert.Equal(["b", "c2"], vm.Profiles.Select(p => p.Name));

        vm.QuitCommand.Execute(null);
        Assert.True(quit);
    }

    [Fact]
    public async Task Cancelled_editor_changes_nothing()
    {
        var vm = new ProfilePickerViewModel(_store, _ => { }, _ => Task.FromResult<ProfileEdit?>(null), () => { });
        await vm.NewCommand.ExecuteAsync(null);
        Assert.Empty(vm.Profiles);
    }

    /// <summary>#50. A picker opened via File > New Session sits alongside running sessions and reloads only from
    /// its own commands, so its copy can predate a pin a session window wrote. Editing that stale copy used to
    /// write the whole record back and take the pin with it.</summary>
    [Fact]
    public async Task Editing_a_stale_profile_does_not_drop_a_pin_written_since()
    {
        _store.Save(new SessionProfile { Name = "MVS", Host = "mvs", Port = 3270 });
        var pin = new CertificatePin("AA:BB", "CN=mvs", "pem");

        var picker = new ProfilePickerViewModel(_store, _ => { },
            existing =>
            {
                // Stands in for the session window pinning a certificate while the editor is open.
                _store.Update(existing!, p => p with { PinnedCertificate = pin });
                return Task.FromResult<ProfileEdit?>(new ProfileEdit(existing! with { Host = "mvs" }, PinCleared: false));
            },
            () => { });

        picker.SelectedProfile = picker.Profiles.Single();
        await picker.EditCommand.ExecuteAsync(null);

        Assert.Equal(pin, _store.Load("MVS")!.PinnedCertificate);
    }

    [Fact]
    public async Task Forget_still_clears_a_pin_the_file_has()
    {
        _store.Save(new SessionProfile { Name = "MVS", Host = "mvs", Port = 3270, PinnedCertificate = new CertificatePin("AA:BB", "CN=mvs", "pem") });

        var picker = new ProfilePickerViewModel(_store, _ => { },
            existing => Task.FromResult<ProfileEdit?>(new ProfileEdit(existing! with { PinnedCertificate = null }, PinCleared: true)),
            () => { });

        picker.SelectedProfile = picker.Profiles.Single();
        await picker.EditCommand.ExecuteAsync(null);

        Assert.Null(_store.Load("MVS")!.PinnedCertificate);
    }

    /// <summary>A rename deletes the old file, so the merge has to read the pin under the ORIGINAL name. Reading
    /// it under the new one finds nothing and loses the pin exactly as the bug does.</summary>
    [Fact]
    public async Task A_rename_carries_the_pin_across()
    {
        var pin = new CertificatePin("AA:BB", "CN=mvs", "pem");
        _store.Save(new SessionProfile { Name = "MVS", Host = "mvs", Port = 3270, PinnedCertificate = pin });

        var picker = new ProfilePickerViewModel(_store, _ => { },
            existing => Task.FromResult<ProfileEdit?>(
                new ProfileEdit(existing! with { Name = "MVS-CE", PinnedCertificate = null }, PinCleared: false)),
            () => { });

        picker.SelectedProfile = picker.Profiles.Single();
        await picker.EditCommand.ExecuteAsync(null);

        Assert.Null(_store.Load("MVS"));
        Assert.Equal(pin, _store.Load("MVS-CE")!.PinnedCertificate);
    }

    [Fact]
    public void Editor_shows_the_pin_and_forget_drops_it()
    {
        var pin = new CertificatePin("8C:13:6A:01", "CN=gw", "pem");
        var vm = new ProfileEditorViewModel(new SessionProfile { Name = "gw", Host = "gw", UseTls = true, PinnedCertificate = pin });
        Assert.True(vm.HasPinnedCertificate);
        Assert.Equal("Pinned certificate: SHA-256 8C:13:6A:01 (CN=gw)", vm.PinnedCertificateText);
        Assert.Same(pin, vm.TryBuild()!.PinnedCertificate);

        var changes = new List<string?>();
        vm.PropertyChanged += (_, e) => changes.Add(e.PropertyName);
        Assert.False(vm.PinCleared);
        vm.ForgetPinCommand.Execute(null);
        Assert.True(vm.PinCleared);
        Assert.False(vm.HasPinnedCertificate);
        Assert.Null(vm.PinnedCertificateText);
        Assert.Null(vm.TryBuild()!.PinnedCertificate);
        Assert.Contains(nameof(vm.HasPinnedCertificate), changes);
        Assert.Contains(nameof(vm.PinnedCertificateText), changes);
    }

    /// <summary>A pin was taken from one host and port; a profile pointed somewhere else must not carry it, and the
    /// engine's name check being off for a single-certificate pin makes that matter. Restoring the original endpoint
    /// before Save keeps the pin; Forget is final.</summary>
    [Fact]
    public void Editing_the_host_or_port_drops_the_pin_and_restoring_them_brings_it_back()
    {
        var pin = new CertificatePin("8C:13:6A:01", "CN=gw", "pem");
        var vm = new ProfileEditorViewModel(new SessionProfile { Name = "gw", Host = "gw", Port = 4270, UseTls = true, PinnedCertificate = pin });

        vm.Host = "other";
        Assert.False(vm.HasPinnedCertificate);
        Assert.Null(vm.TryBuild()!.PinnedCertificate);
        vm.Host = "gw";
        Assert.Same(pin, vm.TryBuild()!.PinnedCertificate);

        vm.PortText = "4271";
        Assert.Null(vm.TryBuild()!.PinnedCertificate);
        vm.PortText = "4270";
        Assert.Same(pin, vm.PinnedCertificate);

        vm.ForgetPinCommand.Execute(null);
        vm.Host = "x";
        vm.Host = "gw";
        Assert.Null(vm.PinnedCertificate);
    }

    /// <summary>Hostnames are case-insensitive, and PinMerge.Resolve compares them that way. RefreshPin has to
    /// agree, or a case-only edit blanks the pin panel while Save silently restores the pin from disk.</summary>
    [Fact]
    public void A_case_only_host_edit_keeps_the_pin_visible()
    {
        var pin = new CertificatePin("8C:13:6A:01", "CN=gw", "pem");
        var vm = new ProfileEditorViewModel(new SessionProfile { Name = "gw", Host = "mvs.example", Port = 3270, UseTls = true, PinnedCertificate = pin });

        vm.Host = "MVS.example";

        Assert.True(vm.HasPinnedCertificate);
        Assert.Same(pin, vm.PinnedCertificate);
    }

    /// <summary>PinMerge.Resolve compares Port as an int; RefreshPin has to agree there too, or a leading-zero
    /// port edit (a number that reads the same but is not the same string as the saved one) blanks the pin panel
    /// while Save silently restores the pin from disk.</summary>
    [Fact]
    public void A_leading_zero_port_edit_keeps_the_pin_visible()
    {
        var pin = new CertificatePin("8C:13:6A:01", "CN=gw", "pem");
        var vm = new ProfileEditorViewModel(new SessionProfile { Name = "gw", Host = "mvs.example", Port = 992, UseTls = true, PinnedCertificate = pin });

        vm.PortText = "0992";

        Assert.True(vm.HasPinnedCertificate);
        Assert.Same(pin, vm.PinnedCertificate);
    }

    [Fact]
    public void A_new_profile_has_no_pin()
    {
        var vm = new ProfileEditorViewModel(null);
        Assert.False(vm.HasPinnedCertificate);
        Assert.Null(vm.PinnedCertificateText);
        Assert.Null(vm.TryBuild()?.PinnedCertificate);
    }

    [Fact]
    public void Editor_offers_models_with_their_geometry()
    {
        var vm = new ProfileEditorViewModel(null);
        Assert.Equal(4, vm.TerminalModels.Count);
        Assert.Equal("3 — 32x80", vm.TerminalModels.Single(m => m.Number == 3).ToString());
        Assert.Equal(2, vm.SelectedModel.Number);

        vm.Name = "n";
        vm.Host = "h";
        vm.SelectedModel = vm.TerminalModels.Single(m => m.Number == 5);
        Assert.Equal(5, vm.Model);
        Assert.Equal(5, vm.TryBuild()!.Model);
    }

    [Fact]
    public void Editor_offers_code_pages_with_labels()
    {
        var vm = new ProfileEditorViewModel(null);
        Assert.Equal("bracket", vm.CodePages[0].Name);
        Assert.Equal("cp037", vm.SelectedCodePage.Name);

        vm.SelectedCodePage = vm.CodePages.Single(p => p.Name == "cp285");
        Assert.Equal("cp285", vm.CodePage);
    }

    /// <summary>#44 and #45's shared trap: a ComboBox bound SelectedItem has nothing to select when the saved
    /// value is outside ItemsSource. Opening the editor on a hand-edited profile must not silently drop its
    /// setting, so the list is seeded with whatever the profile actually holds.</summary>
    [Fact]
    public void A_value_outside_the_catalogue_survives_a_round_trip()
    {
        var odd = new SessionProfile { Name = "odd", Host = "h", Model = 9, CodePage = "cp9999" };
        var vm = new ProfileEditorViewModel(odd);

        Assert.Equal(9, vm.SelectedModel.Number);
        Assert.Equal("cp9999", vm.SelectedCodePage.Name);
        Assert.Contains(vm.TerminalModels, m => m.Number == 9);
        Assert.Contains(vm.CodePages, p => p.Name == "cp9999");

        var built = vm.TryBuild()!;
        Assert.Equal(9, built.Model);
        Assert.Equal("cp9999", built.CodePage);
    }

    /// <summary>The other half of the seeding rule: a hand-edited file whose codePage is blank -- or null, which
    /// System.Text.Json writes straight into the record whatever its annotation says -- is seeded and selected
    /// like any other outside value, so TryBuild is the last thing standing between it and a NullReferenceException
    /// thrown out of OnSaveClick, which has no catch. A saved "" is no better: b3270 warns on stderr, starts on a
    /// fallback, and the session connects normally with quietly wrong characters.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void A_blank_code_page_is_refused_rather_than_saved_or_thrown_on(string? codePage)
    {
        var vm = new ProfileEditorViewModel(new SessionProfile { Name = "p", Host = "h", CodePage = codePage! });

        Assert.Null(vm.TryBuild());
        Assert.Equal("Choose a code page.", vm.ValidationMessage);
    }

    [Fact]
    public void The_editor_round_trips_the_keep_alive_and_auto_reconnect()
    {
        var vm = new ProfileEditorViewModel(new SessionProfile
        {
            Name = "MVS", Host = "mvs", KeepAliveSeconds = 30, AutoReconnect = true,
        });

        Assert.Equal("30", vm.KeepAliveText);
        Assert.True(vm.AutoReconnect);

        var built = vm.TryBuild();
        Assert.NotNull(built);
        Assert.Equal(30, built.KeepAliveSeconds);
        Assert.True(built.AutoReconnect);
    }

    /// <summary>A new profile shows the record's own default, so the editor and the file agree about what
    /// "on at 60 seconds" means rather than the editor quietly proposing something else.</summary>
    [Fact]
    public void A_new_profile_offers_the_declared_keep_alive_default()
    {
        Assert.Equal("60", new ProfileEditorViewModel(null).KeepAliveText);
    }

    [Fact]
    public void Turning_the_keep_alive_off_saves_a_zero()
    {
        var vm = new ProfileEditorViewModel(null) { Name = "p", Host = "h", KeepAliveText = "0" };
        Assert.Equal(0, vm.TryBuild()!.KeepAliveSeconds);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("-1")]
    [InlineData("90000")]
    public void A_keep_alive_that_is_not_a_sane_number_of_seconds_blocks_save(string text)
    {
        var vm = new ProfileEditorViewModel(null) { Name = "p", Host = "h", KeepAliveText = text };
        Assert.Null(vm.TryBuild());
        Assert.Equal("Keep-alive must be a whole number of seconds, 0 to 86400 (0 turns it off).", vm.ValidationMessage);
    }

    [Fact]
    public void The_editor_round_trips_an_oversize_geometry()
    {
        var vm = new ProfileEditorViewModel(new SessionProfile { Name = "MVS", Host = "mvs", Oversize = "132x43" });
        Assert.Equal("132x43", vm.Oversize);
        Assert.Equal("132x43", vm.TryBuild()!.Oversize);
    }

    /// <summary>Blank means the model's own geometry, and must save as null rather than "": the argv check is
    /// IsNullOrWhiteSpace, but a "" in the file would still be a lie about what the user chose.</summary>
    [Fact]
    public void A_blank_oversize_saves_as_null()
    {
        var vm = new ProfileEditorViewModel(null) { Name = "p", Host = "h", Oversize = "   " };
        Assert.Null(vm.TryBuild()!.Oversize);
    }

    [Fact]
    public void An_illegal_oversize_blocks_save_with_the_rules_own_message()
    {
        var vm = new ProfileEditorViewModel(null) { Name = "p", Host = "h", Oversize = "200x200" };
        Assert.Null(vm.TryBuild());
        Assert.StartsWith("200 columns by 200 rows is 40,000 cells", vm.ValidationMessage);
    }

    /// <summary>100x30 clears model 2's floor (80 columns, 24 rows) but falls short of model 5's floor (132
    /// columns, 27 rows) on the column count, so switching the model has to re-run the check — otherwise the
    /// editor shows a stale verdict about the geometry in the box.
    ///
    /// NOTE: the brief's own example for this test was "132x43", described there as clearing model 2 and
    /// falling short of model 5's 27x132. That pair does not actually fail model 5's floor check: 132 columns
    /// meets model 5's 132-column floor exactly, and 43 rows clears its 27-row floor, so
    /// OversizeGeometry.TryParse legitimately returns valid for it (confirmed by running this test against the
    /// already-committed OversizeGeometry and TerminalModel catalog before changing the numbers — the
    /// implementation and wiring are correct; the brief's chosen numbers just do not demonstrate the failure
    /// they were meant to). Swapped in 100x30, which fails only on the column dimension, to actually exercise
    /// the re-validation path.</summary>
    [Fact]
    public void Changing_the_model_re_validates_the_oversize()
    {
        var vm = new ProfileEditorViewModel(null) { Name = "p", Host = "h", Oversize = "100x30" };
        Assert.NotNull(vm.TryBuild());

        vm.SelectedModel = TerminalModel.Find(5)!;
        Assert.Equal("Oversize must be at least 132 columns and 27 rows for model 5.", vm.ValidationMessage);
        Assert.Null(vm.TryBuild());

        vm.SelectedModel = TerminalModel.Find(2)!;
        Assert.Null(vm.ValidationMessage);
        Assert.NotNull(vm.TryBuild());
    }

    /// <summary>Only the oversize verdict moves with the model. A blank box has nothing to say about it, and
    /// clearing an unrelated message would be a second, invisible behaviour.</summary>
    [Fact]
    public void Changing_the_model_leaves_an_unrelated_message_alone()
    {
        var vm = new ProfileEditorViewModel(null) { Host = "h" };
        Assert.Null(vm.TryBuild());
        Assert.Equal("Give the profile a name.", vm.ValidationMessage);

        vm.SelectedModel = TerminalModel.Find(4)!;
        Assert.Equal("Give the profile a name.", vm.ValidationMessage);
    }
}
