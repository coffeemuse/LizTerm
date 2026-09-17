// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.App.Rendering;
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
        bool? openedFromStore = null;
        var quit = false;
        SessionProfile? toReturn = new SessionProfile { Name = "c", Host = "c.host" };
        var vm = new ProfilePickerViewModel(_store, (p, s) => { opened = p; openedFromStore = s; }, (_, _) => Task.FromResult<ProfileEdit?>(new ProfileEdit(toReturn, PinCleared: false)), () => quit = true);

        Assert.Equal(["a", "b"], vm.Profiles.Select(p => p.Name));
        Assert.False(vm.ConnectCommand.CanExecute(null));

        vm.SelectedRow = vm.VisibleRows[1];
        Assert.True(vm.ConnectCommand.CanExecute(null));
        vm.ConnectCommand.Execute(null);
        Assert.Equal("b", opened!.Name);
        // The list's Connect button always opens a saved profile: a pin the user accepts there is safe to write
        // back. Flipping this to false would let a Connect-button pin silently vanish instead of saving.
        Assert.True(openedFromStore);

        await vm.NewCommand.ExecuteAsync(null);
        Assert.Equal(["a", "b", "c"], vm.Profiles.Select(p => p.Name));
        Assert.Equal("c", vm.SelectedProfile!.Name);

        toReturn = new SessionProfile { Name = "c2", Host = "c.host" };
        await vm.EditCommand.ExecuteAsync(null);
        Assert.Equal(["a", "b", "c2"], vm.Profiles.Select(p => p.Name));
        Assert.Equal(3, _store.LoadAll().Count);

        vm.SelectedRow = vm.VisibleRows[0];
        vm.DeleteCommand.Execute(null);
        Assert.Equal(["b", "c2"], vm.Profiles.Select(p => p.Name));

        vm.QuitCommand.Execute(null);
        Assert.True(quit);
    }

    [Fact]
    public async Task Cancelled_editor_changes_nothing()
    {
        var vm = new ProfilePickerViewModel(_store, (_, _) => { }, (_, _) => Task.FromResult<ProfileEdit?>(null), () => { });
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

        var picker = new ProfilePickerViewModel(_store, (_, _) => { },
            (existing, _) =>
            {
                // Stands in for the session window pinning a certificate while the editor is open.
                _store.Update(existing!, p => p with { PinnedCertificate = pin });
                return Task.FromResult<ProfileEdit?>(new ProfileEdit(existing! with { Host = "mvs" }, PinCleared: false));
            },
            () => { });

        picker.SelectedRow = picker.VisibleRows.Single();
        await picker.EditCommand.ExecuteAsync(null);

        Assert.Equal(pin, _store.Load("MVS")!.PinnedCertificate);
    }

    [Fact]
    public async Task Editing_a_stale_profile_does_not_drop_a_rest_pin_written_since()
    {
        _store.Save(new SessionProfile { Name = "MVS", Host = "mvs", Port = 3270, HostFilesUrl = "http://mvs:8080/zosmf" });
        var pin = new CertificatePin("CC:DD", "CN=proxy", "pem");

        var picker = new ProfilePickerViewModel(_store, (_, _) => { },
            (existing, _) =>
            {
                // Stands in for a browser window remembering a certificate while the editor is open.
                _store.Update(existing!, p => p with { HostFilesPinnedCertificate = pin });
                return Task.FromResult<ProfileEdit?>(new ProfileEdit(existing!, PinCleared: false));
            },
            () => { });

        picker.SelectedRow = picker.VisibleRows.Single();
        await picker.EditCommand.ExecuteAsync(null);

        Assert.Equal(pin, _store.Load("MVS")!.HostFilesPinnedCertificate);
    }

    [Fact]
    public async Task Forget_still_clears_a_rest_pin_the_file_has()
    {
        _store.Save(new SessionProfile { Name = "MVS", Host = "mvs", HostFilesUrl = "http://mvs/zosmf", HostFilesPinnedCertificate = new CertificatePin("CC:DD", "CN=proxy", "pem") });

        var picker = new ProfilePickerViewModel(_store, (_, _) => { },
            (existing, _) => Task.FromResult<ProfileEdit?>(new ProfileEdit(existing! with { HostFilesPinnedCertificate = null }, PinCleared: false, HostFilesPinCleared: true)),
            () => { });

        picker.SelectedRow = picker.VisibleRows.Single();
        await picker.EditCommand.ExecuteAsync(null);

        Assert.Null(_store.Load("MVS")!.HostFilesPinnedCertificate);
    }

    [Fact]
    public async Task Forget_still_clears_a_pin_the_file_has()
    {
        _store.Save(new SessionProfile { Name = "MVS", Host = "mvs", Port = 3270, PinnedCertificate = new CertificatePin("AA:BB", "CN=mvs", "pem") });

        var picker = new ProfilePickerViewModel(_store, (_, _) => { },
            (existing, _) => Task.FromResult<ProfileEdit?>(new ProfileEdit(existing! with { PinnedCertificate = null }, PinCleared: true)),
            () => { });

        picker.SelectedRow = picker.VisibleRows.Single();
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

        var picker = new ProfilePickerViewModel(_store, (_, _) => { },
            (existing, _) => Task.FromResult<ProfileEdit?>(
                new ProfileEdit(existing! with { Name = "MVS-CE", PinnedCertificate = null }, PinCleared: false)),
            () => { });

        picker.SelectedRow = picker.VisibleRows.Single();
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
        Assert.Equal("CN=gw", vm.PinnedSubject);
        Assert.Equal("8C:13:6A:01", vm.PinnedFingerprint);
        Assert.Same(pin, vm.TryBuild()!.PinnedCertificate);

        var changes = new List<string?>();
        vm.PropertyChanged += (_, e) => changes.Add(e.PropertyName);
        Assert.False(vm.PinCleared);
        vm.ForgetPinCommand.Execute(null);
        Assert.True(vm.PinCleared);
        Assert.False(vm.HasPinnedCertificate);
        Assert.Null(vm.PinnedSubject);
        Assert.Null(vm.PinnedFingerprint);
        Assert.Null(vm.TryBuild()!.PinnedCertificate);
        Assert.Contains(nameof(vm.HasPinnedCertificate), changes);
        Assert.Contains(nameof(vm.PinnedSubject), changes);
        Assert.Contains(nameof(vm.PinnedFingerprint), changes);
    }

    /// <summary>A full SHA-256 is 95 characters with no space; left to wrap it breaks mid-byte.</summary>
    [Fact]
    public void A_full_fingerprint_shows_on_two_lines_broken_between_bytes()
    {
        var bytes = Enumerable.Range(0, 32).Select(i => i.ToString("X2")).ToArray();
        var pin = new CertificatePin(string.Join(':', bytes), "CN=gw", "pem");
        var vm = new ProfileEditorViewModel(new SessionProfile { Name = "gw", Host = "gw", UseTls = true, PinnedCertificate = pin });

        Assert.Equal(string.Join(':', bytes[..16]) + ":\n" + string.Join(':', bytes[16..]), vm.PinnedFingerprint);
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
        Assert.Null(vm.PinnedFingerprint);
        Assert.Null(vm.TryBuild()?.PinnedCertificate);
    }

    [Fact]
    public void Editor_offers_models_with_their_geometry()
    {
        var vm = new ProfileEditorViewModel(null);
        Assert.Equal(5, vm.ModelChoices.Count);
        Assert.Equal("3 — 32x80", vm.ModelChoices.Single(c => c.Model?.Number == 3).ToString());
        Assert.Same(ModelChoice.Other, vm.ModelChoices[^1]);
        Assert.Equal("Other (custom size)", ModelChoice.Other.ToString());
        Assert.Equal(2, vm.SelectedModelChoice.Model!.Number);
        Assert.False(vm.IsCustomSize);

        vm.Name = "n";
        vm.Host = "h";
        vm.SelectedModelChoice = vm.ModelChoices.Single(c => c.Model?.Number == 5);
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

        Assert.Equal(9, vm.SelectedModelChoice.Model!.Number);
        Assert.Equal("cp9999", vm.SelectedCodePage.Name);
        Assert.Contains(vm.ModelChoices, c => c.Model?.Number == 9);
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

    /// <summary>A profile with an oversize opens on Other, with the geometry split into the two boxes, and saves
    /// back to the same text.</summary>
    [Fact]
    public void An_oversize_profile_opens_on_other_and_round_trips()
    {
        var vm = new ProfileEditorViewModel(new SessionProfile { Name = "MVS", Host = "mvs", Oversize = "132x43" });

        Assert.True(vm.IsCustomSize);
        Assert.Same(ModelChoice.Other, vm.SelectedModelChoice);
        Assert.Equal("132", vm.ColumnsText);
        Assert.Equal("43", vm.RowsText);
        var built = vm.TryBuild()!;
        Assert.Equal("132x43", built.Oversize);
        Assert.Equal(2, built.Model);
    }

    /// <summary>With an oversize b3270 sends IBM-DYNAMIC and starts on 24x80 whatever the model, so the model's
    /// only remaining effect is the floor. Model 2 has the lowest, so Other always saves it — including for a
    /// profile that used to pair an oversize with another model.</summary>
    [Fact]
    public void Other_saves_model_2_whatever_the_profile_had()
    {
        var vm = new ProfileEditorViewModel(new SessionProfile { Name = "MVS", Host = "mvs", Model = 5, Oversize = "140x30" });

        var built = vm.TryBuild()!;
        Assert.Equal(2, built.Model);
        Assert.Equal("140x30", built.Oversize);
    }

    [Fact]
    public void Choosing_other_enables_the_boxes_and_saves_what_is_typed()
    {
        var vm = new ProfileEditorViewModel(null) { Name = "p", Host = "h" };
        vm.SelectedModelChoice = vm.ModelChoices.Single(c => c.Model?.Number == 5);
        Assert.False(vm.IsCustomSize);

        vm.SelectedModelChoice = ModelChoice.Other;
        vm.ColumnsDisplay = "100";
        vm.RowsDisplay = "30";

        Assert.True(vm.IsCustomSize);
        var built = vm.TryBuild()!;
        Assert.Equal(2, built.Model);
        Assert.Equal("100x30", built.Oversize);
    }

    /// <summary>Other starts from the size on screen, so the spinners have a number to count from; numbers already
    /// typed win.</summary>
    [Fact]
    public void Choosing_other_starts_from_the_models_size()
    {
        var vm = new ProfileEditorViewModel(null) { Name = "p", Host = "h" };
        vm.SelectedModelChoice = vm.ModelChoices.Single(c => c.Model?.Number == 4);

        vm.SelectedModelChoice = ModelChoice.Other;

        Assert.Equal("80", vm.ColumnsDisplay);
        Assert.Equal("43", vm.RowsDisplay);
        Assert.Equal("80x43", vm.TryBuild()!.Oversize);

        vm.ColumnsDisplay = "100";
        vm.SelectedModelChoice = vm.ModelChoices.Single(c => c.Model?.Number == 5);
        vm.SelectedModelChoice = ModelChoice.Other;
        Assert.Equal("100", vm.ColumnsDisplay);
        Assert.Equal("43", vm.RowsDisplay);
    }

    /// <summary>Outside Other the boxes are read-only and show the chosen model's own size, so they always say
    /// what the screen will be.</summary>
    [Fact]
    public void Outside_other_the_boxes_show_the_models_own_size_and_ignore_writes()
    {
        var vm = new ProfileEditorViewModel(null);
        Assert.Equal("80", vm.ColumnsDisplay);
        Assert.Equal("24", vm.RowsDisplay);

        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        vm.SelectedModelChoice = vm.ModelChoices.Single(c => c.Model?.Number == 5);

        Assert.Equal("132", vm.ColumnsDisplay);
        Assert.Equal("27", vm.RowsDisplay);
        Assert.Contains(nameof(vm.ColumnsDisplay), changed);
        Assert.Contains(nameof(vm.RowsDisplay), changed);

        vm.ColumnsDisplay = "999";
        Assert.Equal("132", vm.ColumnsDisplay);
        Assert.Equal("", vm.ColumnsText);
    }

    /// <summary>Leaving Other drops the custom size from the profile, but the typed numbers come back if the user
    /// returns to Other before closing the editor.</summary>
    [Fact]
    public void Leaving_other_saves_no_oversize_and_returning_restores_the_numbers()
    {
        var vm = new ProfileEditorViewModel(new SessionProfile { Name = "MVS", Host = "mvs", Oversize = "132x43" });

        vm.SelectedModelChoice = vm.ModelChoices.Single(c => c.Model?.Number == 4);
        Assert.Equal("80", vm.ColumnsDisplay);
        var built = vm.TryBuild()!;
        Assert.Null(built.Oversize);
        Assert.Equal(4, built.Model);

        vm.SelectedModelChoice = ModelChoice.Other;
        Assert.Equal("132", vm.ColumnsDisplay);
        Assert.Equal("43", vm.RowsDisplay);
        Assert.Equal("132x43", vm.TryBuild()!.Oversize);
    }

    [Theory]
    [InlineData("", "43")]
    [InlineData("132", "")]
    [InlineData("  ", "  ")]
    public void Other_with_an_empty_box_blocks_save(string columns, string rows)
    {
        var vm = new ProfileEditorViewModel(null) { Name = "p", Host = "h" };
        vm.SelectedModelChoice = ModelChoice.Other;
        vm.ColumnsDisplay = columns;
        vm.RowsDisplay = rows;

        Assert.Null(vm.TryBuild());
        Assert.Equal("Enter both a column count and a row count for the custom size.", vm.ValidationMessage);
    }

    [Theory]
    [InlineData("abc", "43", "Columns must be a whole number.")]
    [InlineData("-132", "43", "Columns must be a whole number.")]
    [InlineData("132", "4 3", "Rows must be a whole number.")]
    [InlineData("20000", "30", "Columns must be at most 16,383.")]
    [InlineData("132", "99999999999", "Rows must be at most 16,383.")]
    [InlineData("79", "43", "A custom size must be at least 80 columns and 24 rows.")]
    [InlineData("132", "23", "A custom size must be at least 80 columns and 24 rows.")]
    [InlineData("0", "0", "A custom size must be at least 80 columns and 24 rows.")]
    public void Other_with_a_bad_number_blocks_save(string columns, string rows, string message)
    {
        var vm = new ProfileEditorViewModel(null) { Name = "p", Host = "h" };
        vm.SelectedModelChoice = ModelChoice.Other;
        vm.ColumnsDisplay = columns;
        vm.RowsDisplay = rows;

        Assert.Null(vm.TryBuild());
        Assert.Equal(message, vm.ValidationMessage);
    }

    /// <summary>Stray spaces are forgiven the way the port's are.</summary>
    [Fact]
    public void Other_trims_the_boxes()
    {
        var vm = new ProfileEditorViewModel(null) { Name = "p", Host = "h" };
        vm.SelectedModelChoice = ModelChoice.Other;
        vm.ColumnsDisplay = " 132 ";
        vm.RowsDisplay = "43 ";

        Assert.Equal("132x43", vm.TryBuild()!.Oversize);
    }

    /// <summary>The engine's area limit stays OversizeGeometry's, message and all.</summary>
    [Fact]
    public void An_oversize_past_the_area_limit_blocks_save_with_the_rules_own_message()
    {
        var vm = new ProfileEditorViewModel(null) { Name = "p", Host = "h" };
        vm.SelectedModelChoice = ModelChoice.Other;
        vm.ColumnsDisplay = "200";
        vm.RowsDisplay = "200";

        Assert.Null(vm.TryBuild());
        Assert.StartsWith("200 columns by 200 rows is 40,000 cells", vm.ValidationMessage);
    }

    /// <summary>b3270's own spelling of "no oversize", which a hand-edited profile can carry, is not Other.</summary>
    [Fact]
    public void A_zero_by_zero_oversize_opens_on_the_profiles_model()
    {
        var vm = new ProfileEditorViewModel(new SessionProfile { Name = "p", Host = "h", Model = 3, Oversize = "0x0" });

        Assert.False(vm.IsCustomSize);
        Assert.Equal(3, vm.SelectedModelChoice.Model!.Number);
        Assert.Null(vm.TryBuild()!.Oversize);
    }

    /// <summary>A hand-edited oversize that is not two numbers still opens on Other, so it is not silently
    /// dropped; Save then says what is wrong with it rather than passing it through.</summary>
    [Fact]
    public void A_garbled_oversize_opens_on_other_and_blocks_save()
    {
        var vm = new ProfileEditorViewModel(new SessionProfile { Name = "p", Host = "h", Oversize = "132x4x3" });

        Assert.True(vm.IsCustomSize);
        Assert.Null(vm.TryBuild());
        Assert.NotNull(vm.ValidationMessage);
    }

    /// <summary>A verdict the user has since typed their way out of is a red line under numbers that are no longer
    /// there — the same rule the picker's Quick Connect box follows.</summary>
    [Fact]
    public void Correcting_a_box_withdraws_the_rules_own_message()
    {
        var vm = new ProfileEditorViewModel(null) { Name = "p", Host = "h" };
        vm.SelectedModelChoice = ModelChoice.Other;
        vm.ColumnsDisplay = "200";
        vm.RowsDisplay = "200";
        Assert.Null(vm.TryBuild());
        Assert.NotNull(vm.ValidationMessage);

        vm.RowsDisplay = "43";

        Assert.Null(vm.ValidationMessage);
        Assert.NotNull(vm.TryBuild());
    }

    /// <summary>Emptying a box mid-edit is not yet a mistake, so it withdraws the verdict rather than replacing it
    /// with "enter both"; Save still refuses it.</summary>
    [Fact]
    public void Emptying_a_box_withdraws_the_rules_own_message()
    {
        var vm = new ProfileEditorViewModel(null) { Name = "p", Host = "h" };
        vm.SelectedModelChoice = ModelChoice.Other;
        vm.ColumnsDisplay = "200";
        vm.RowsDisplay = "200";
        Assert.Null(vm.TryBuild());

        vm.RowsDisplay = "";

        Assert.Null(vm.ValidationMessage);
    }

    [Fact]
    public void Leaving_other_withdraws_the_rules_own_message()
    {
        var vm = new ProfileEditorViewModel(null) { Name = "p", Host = "h" };
        vm.SelectedModelChoice = ModelChoice.Other;
        vm.ColumnsDisplay = "";
        Assert.Null(vm.TryBuild());
        Assert.NotNull(vm.ValidationMessage);

        vm.SelectedModelChoice = vm.ModelChoices[0];

        Assert.Null(vm.ValidationMessage);
    }

    /// <summary>The size rule has no claim on a message another rule owns: "Give the profile a name." stays put
    /// whatever happens to the size, because Save will still refuse on it first.</summary>
    [Fact]
    public void Size_edits_leave_an_unrelated_message_alone()
    {
        var vm = new ProfileEditorViewModel(null) { Host = "h" };
        Assert.Null(vm.TryBuild());
        Assert.Equal("Give the profile a name.", vm.ValidationMessage);

        vm.SelectedModelChoice = ModelChoice.Other;
        vm.ColumnsDisplay = "200";
        vm.RowsDisplay = "200";
        Assert.Equal("Give the profile a name.", vm.ValidationMessage);

        vm.SelectedModelChoice = vm.ModelChoices[1];
        Assert.Equal("Give the profile a name.", vm.ValidationMessage);
    }

    /// <summary>The box inherits the command line's tie-break rule by calling the same Parse and Resolve, so
    /// text that exactly names a saved profile connects THAT profile rather than a host of the same name.</summary>
    [Fact]
    public void Quick_connect_prefers_a_saved_profile_of_the_same_name()
    {
        _store.Save(new SessionProfile { Name = "mvs.local", Host = "elsewhere", Port = 992 });
        SessionProfile? opened = null;
        var fromStore = false;
        var vm = NewPicker((p, s) => { opened = p; fromStore = s; });

        vm.QuickConnectText = "mvs.local";
        vm.QuickConnectCommand.Execute(null);

        Assert.Equal("elsewhere", opened!.Host);
        Assert.True(fromStore);
        Assert.Null(vm.QuickConnectError);
    }

    /// <summary>The ad hoc branch defaults a missing port (23 here), so its generated name can collide with an
    /// unrelated saved profile's name -- "mvs.example" typed with no port becomes "mvs.example:23", and a saved
    /// profile happens to be named exactly that while pointing somewhere else entirely. fromStore has to say false
    /// here: it is derived from which branch Resolve took (reference identity), not from a name lookup that would
    /// re-collide with the very name Resolve just generated. Getting this wrong lets a certificate pin accepted
    /// on this ad hoc connection get written into the unrelated saved profile's file (see App.WritePinBack).</summary>
    [Fact]
    public void Quick_connect_does_not_mistake_an_ad_hoc_host_for_a_saved_profile_of_the_same_generated_name()
    {
        _store.Save(new SessionProfile { Name = "mvs.example:23", Host = "totally-different-host" });
        SessionProfile? opened = null;
        var fromStore = true;
        var vm = NewPicker((p, s) => { opened = p; fromStore = s; });

        vm.QuickConnectText = "mvs.example";
        vm.QuickConnectCommand.Execute(null);

        Assert.Equal("mvs.example", opened!.Host);
        Assert.False(fromStore);
    }

    [Fact]
    public void Quick_connect_opens_an_ad_hoc_session_and_saves_nothing()
    {
        SessionProfile? opened = null;
        var fromStore = true;
        var vm = NewPicker((p, s) => { opened = p; fromStore = s; });

        vm.QuickConnectText = "mvs.example:3270";
        vm.QuickConnectCommand.Execute(null);

        Assert.Equal("mvs.example", opened!.Host);
        Assert.Equal(3270, opened.Port);
        Assert.False(fromStore);
        Assert.Empty(_store.LoadAll());
    }

    [Fact]
    public void Quick_connect_reports_a_syntax_error_inline_and_connects_nothing()
    {
        var opened = false;
        var vm = NewPicker((_, _) => opened = true);

        vm.QuickConnectText = "mvs.local:99999";
        vm.QuickConnectCommand.Execute(null);

        Assert.False(opened);
        Assert.Equal("Type host, host:port, or L:host for TLS. An IPv6 address goes in brackets.", vm.QuickConnectError);
    }

    /// <summary>A bare word is ambiguous with a profile name, so Parse only reads one as a host when it has a
    /// dot or is localhost. The message has to teach the way out rather than merely refuse (spec 7.4).</summary>
    [Fact]
    public void A_bare_word_that_is_neither_a_profile_nor_a_host_suggests_the_port_form()
    {
        var opened = false;
        var vm = NewPicker((_, _) => opened = true);

        vm.QuickConnectText = "tk5";
        vm.QuickConnectCommand.Execute(null);

        Assert.False(opened);
        Assert.Equal("\"tk5\" is not a saved session, and does not look like a host. Add a port to connect to it as a host, for example tk5:23.", vm.QuickConnectError);
    }

    [Fact]
    public void Quick_connect_with_an_empty_box_asks_for_something_to_connect_to()
    {
        var opened = false;
        var vm = NewPicker((_, _) => opened = true);

        vm.QuickConnectCommand.Execute(null);

        Assert.False(opened);
        Assert.Equal("Type a host name, or the name of a saved session.", vm.QuickConnectError);
    }

    /// <summary>The message describes the text that was in the box when Connect was pressed, so it goes stale on
    /// the next keystroke; leaving it there puts a red line under a box the user has since retyped.</summary>
    [Fact]
    public void Typing_in_the_box_clears_a_stale_quick_connect_error()
    {
        var vm = NewPicker((_, _) => { });

        vm.QuickConnectText = "tk5";
        vm.QuickConnectCommand.Execute(null);
        Assert.NotNull(vm.QuickConnectError);

        vm.QuickConnectText = "tk5:23";

        Assert.Null(vm.QuickConnectError);
    }

    private ProfilePickerViewModel NewPicker(Action<SessionProfile, bool> openSession) =>
        new(_store, openSession, (_, _) => Task.FromResult<ProfileEdit?>(null), () => { });

    /// <summary>In a subdirectory, so the profile store's directory read never meets it.</summary>
    private RecentHostsStore RecentStore() => new(Path.Combine(_dir, "config", "recent-hosts.json"));

    private ProfilePickerViewModel NewPicker(Action<SessionProfile, bool> openSession, RecentHostsStore recent) =>
        new(_store, openSession, (_, _) => Task.FromResult<ProfileEdit?>(null), () => { }, recentHosts: recent);

    [Fact]
    public void Quick_connect_remembers_ad_hoc_hosts_as_typed_newest_first()
    {
        var recent = RecentStore();
        var vm = NewPicker((_, _) => { }, recent);

        vm.QuickConnectText = " tk5:3270 ";
        vm.QuickConnectCommand.Execute(null);
        vm.QuickConnectText = "L:mvs.example";
        vm.QuickConnectCommand.Execute(null);

        Assert.Equal(["L:mvs.example", "tk5:3270"], vm.RecentEntries);
        Assert.Equal(["L:mvs.example", "tk5:3270"], recent.Load().Entries);
    }

    /// <summary>The list already recalls a saved profile, and a renamed or deleted one would leave a stale entry.</summary>
    [Fact]
    public void Quick_connect_does_not_remember_a_saved_profiles_name()
    {
        _store.Save(new SessionProfile { Name = "mvs.local", Host = "elsewhere" });
        var recent = RecentStore();
        var vm = NewPicker((_, _) => { }, recent);

        vm.QuickConnectText = "mvs.local";
        vm.QuickConnectCommand.Execute(null);

        Assert.Empty(vm.RecentEntries);
        Assert.Empty(recent.Load().Entries);
    }

    [Fact]
    public void Quick_connect_does_not_remember_text_that_opened_nothing()
    {
        var recent = RecentStore();
        var vm = NewPicker((_, _) => { }, recent);

        vm.QuickConnectText = "mvs.local:99999";
        vm.QuickConnectCommand.Execute(null);
        vm.QuickConnectText = "tk5";
        vm.QuickConnectCommand.Execute(null);

        Assert.Empty(vm.RecentEntries);
    }

    [Fact]
    public void The_picker_opens_with_the_saved_recent_hosts_and_forgets_one_on_request()
    {
        var recent = RecentStore();
        recent.Save(RecentHosts.From(["a.example", "b.example", "c.example"]));
        var vm = NewPicker((_, _) => { }, recent);
        Assert.Equal(["a.example", "b.example", "c.example"], vm.RecentEntries);

        vm.RemoveRecentHostCommand.Execute("b.example");

        Assert.Equal(["a.example", "c.example"], vm.RecentEntries);
        Assert.Equal(["a.example", "c.example"], recent.Load().Entries);
    }

    /// <summary>The editor draws its chips against the registry the picker just reconciled, so a tag's color in
    /// the editor is the one the list shows.</summary>
    [Fact]
    public async Task The_picker_hands_the_editor_its_registry()
    {
        var tags = new TagRegistryStore(Path.Combine(_dir, "tags.json"));
        tags.Save(new TagRegistry([new("PROD", TagColor.Teal)]));
        _store.Save(new SessionProfile { Name = "a", Host = "h", Tags = TagSet.From(["PROD", "MVS"]) });
        var seen = new List<TagRegistry>();
        var vm = new ProfilePickerViewModel(_store, (_, _) => { },
            (_, registry) => { seen.Add(registry); return Task.FromResult<ProfileEdit?>(null); }, () => { }, tags);

        await vm.NewCommand.ExecuteAsync(null);
        await vm.EditCommand.ExecuteAsync(vm.VisibleRows.Single());

        Assert.Equal(2, seen.Count);
        Assert.All(seen, registry =>
        {
            Assert.Equal(TagColor.Teal, registry.ColorOf("PROD"));
            Assert.True(registry.Contains("MVS"));
        });
    }

    [Fact]
    public void Editor_round_trips_tags_and_a_note()
    {
        var existing = new SessionProfile
        {
            Name = "mvsce", Host = "h", Tags = TagSet.From(["FAVORITE", "PROD", "MVS"]), Note = "no live data",
        };
        var vm = new ProfileEditorViewModel(existing);

        // FAVORITE belongs to the checkbox, so it must not also appear as a chip.
        Assert.True(vm.IsFavorite);
        Assert.Equal(["PROD", "MVS"], vm.TagNames);
        Assert.Equal(["PROD", "MVS"], vm.TagChips.Select(c => c.Text));
        Assert.Equal("no live data", vm.Note);

        var built = vm.TryBuild()!;
        Assert.Equal(existing.Tags, built.Tags);
        Assert.Equal("no live data", built.Note);
    }

    [Fact]
    public void Editor_defaults_to_no_tags_and_no_note()
    {
        var vm = new ProfileEditorViewModel(null) { Name = "n", Host = "h" };
        Assert.False(vm.IsFavorite);
        Assert.Empty(vm.TagChips);
        Assert.Equal("", vm.TagEntry);
        var built = vm.TryBuild()!;
        Assert.True(built.Tags.IsEmpty);
        Assert.Null(built.Note);
    }

    /// <summary>A comma ends a tag whether it is typed or pasted; what follows the last one stays in the box.</summary>
    [Fact]
    public void A_comma_turns_the_text_before_it_into_chips()
    {
        var vm = new ProfileEditorViewModel(null) { Name = "n", Host = "h" };

        vm.TagEntry = "prod,";
        Assert.Equal(["prod"], vm.TagNames);
        Assert.Equal("", vm.TagEntry);

        vm.TagEntry = " #mvs ,, prod , te";
        Assert.Equal(["prod", "mvs"], vm.TagNames);
        Assert.Equal("te", vm.TagEntry);
        Assert.Null(vm.TagMessage);
    }

    [Fact]
    public void Commit_adds_the_box_and_puts_the_checkbox_first_on_save()
    {
        var vm = new ProfileEditorViewModel(null) { Name = "n", Host = "h", IsFavorite = true };
        vm.TagEntry = " #prod ";
        Assert.True(vm.CommitTagEntry());
        Assert.Equal("", vm.TagEntry);

        // A name still in the box when Save is pressed is added, not dropped.
        vm.TagEntry = "mvs";
        var built = vm.TryBuild()!;
        Assert.Equal(["FAVORITE", "prod", "mvs"], built.Tags.Names);
        Assert.Equal("", vm.TagEntry);
    }

    /// <summary>Chips are drawn uppercase in the registry's color, and a name the registry does not know yet in the
    /// color the picker's reconciliation will give it — the least used one.</summary>
    [Fact]
    public void Chips_preview_the_color_a_new_tag_will_get()
    {
        var registry = new TagRegistry([new("PROD", TagColor.Blue)]);
        var vm = new ProfileEditorViewModel(new SessionProfile { Name = "n", Host = "h", Tags = TagSet.From(["prod"]) }, registry);
        vm.TagEntry = "mvs,";

        var expected = registry.Register(["prod", "mvs"]).Registry;
        Assert.Equal(["PROD", "MVS"], vm.TagChips.Select(c => c.Text));
        Assert.Equal(TagPalette.Brush(TagColor.Blue), vm.TagChips[0].Background);
        Assert.Equal(TagPalette.Brush(expected.ColorOf("MVS")), vm.TagChips[1].Background);
        Assert.NotEqual(TagColor.Blue, expected.ColorOf("MVS"));
    }

    /// <summary>The drop-down offers the known tags the profile does not carry, in the registry's order.</summary>
    [Fact]
    public void Suggestions_are_the_known_tags_not_yet_on_the_profile()
    {
        var registry = new TagRegistry([new("VM", TagColor.Red), new("PROD", TagColor.Blue), new("MVS", TagColor.Green)]);
        var vm = new ProfileEditorViewModel(new SessionProfile { Name = "n", Host = "h", Tags = TagSet.From(["prod"]) }, registry);
        Assert.Equal(["MVS", "VM"], vm.TagSuggestions.Select(c => c.Text));
        Assert.Equal(TagPalette.Brush(TagColor.Green), vm.TagSuggestions[0].Background);

        vm.TagEntry = "vm,";
        Assert.Equal(["MVS"], vm.TagSuggestions.Select(c => c.Text));

        vm.RemoveTagCommand.Execute(vm.TagChips[0]);
        Assert.Equal(["vm"], vm.TagNames);
        Assert.Equal(["MVS", "PROD"], vm.TagSuggestions.Select(c => c.Text));
    }

    [Fact]
    public void Remove_last_and_repeats_leave_the_rest_alone()
    {
        var vm = new ProfileEditorViewModel(null) { Name = "n", Host = "h" };
        vm.TagEntry = "a, b, A,";
        Assert.Equal(["a", "b"], vm.TagNames);
        Assert.Null(vm.TagMessage);

        vm.RemoveLastTag();
        Assert.Equal(["a"], vm.TagNames);
        vm.RemoveLastTag();
        vm.RemoveLastTag();
        Assert.Empty(vm.TagNames);
    }

    /// <summary>The checkbox owns the reserved tag, so typing it is forgiven rather than refused: it becomes no chip
    /// and the checkbox visibly turns on, which explains itself without a message.</summary>
    [Fact]
    public void Typing_the_reserved_tag_turns_the_checkbox_on_instead_of_adding_a_chip()
    {
        var vm = new ProfileEditorViewModel(null) { Name = "n", Host = "h" };
        vm.TagEntry = "favorite, PROD,";
        Assert.True(vm.IsFavorite);
        Assert.Equal(["PROD"], vm.TagNames);
        Assert.Null(vm.TagMessage);
        Assert.Equal(["FAVORITE", "PROD"], vm.TryBuild()!.Tags.Names);
    }

    /// <summary>An over-long name stays in the box with the reason under it, and the names after it wait there too,
    /// so nothing typed is lost.</summary>
    [Fact]
    public void An_over_long_name_stays_in_the_box_with_the_reason()
    {
        var tooLong = new string('x', TagSet.MaxNameLength + 1);
        var vm = new ProfileEditorViewModel(null) { Name = "n", Host = "h" };
        vm.TagEntry = $"ok, {tooLong}, after,";

        Assert.Equal(["ok"], vm.TagNames);
        Assert.Equal($"{tooLong}, after", vm.TagEntry);
        Assert.Contains($"{TagSet.MaxNameLength}", vm.TagMessage);

        // The next keystroke clears the reason.
        vm.TagEntry = "short";
        Assert.Null(vm.TagMessage);
    }

    [Fact]
    public void Save_refuses_an_over_long_name_left_in_the_box()
    {
        var vm = new ProfileEditorViewModel(null) { Name = "n", Host = "h" };
        vm.TagEntry = new string('x', TagSet.MaxNameLength + 1);
        Assert.Null(vm.TryBuild());
        Assert.Contains($"{TagSet.MaxNameLength}", vm.ValidationMessage);
        Assert.Equal(ProfileEditorField.Tags, vm.ValidationField);
    }

    /// <summary>The box refuses a chip past the cap as it is typed, FAVORITE counted, and Save refuses the one way
    /// left to pass it: turning FAVORITE on beside a full set.</summary>
    [Fact]
    public void Editor_refuses_more_tags_than_the_cap_including_the_reserved_one()
    {
        var vm = new ProfileEditorViewModel(null) { Name = "n", Host = "h" };
        vm.TagEntry = string.Join(",", Enumerable.Range(0, TagSet.MaxTags).Select(i => $"T{i}")) + ",";
        Assert.Equal(TagSet.MaxTags, vm.TagNames.Count);
        Assert.False(vm.CanAddTag);
        Assert.Equal($"{TagSet.MaxTags} tags at most", vm.TagPlaceholder);
        Assert.NotNull(vm.TryBuild());

        vm.TagEntry = "extra,";
        Assert.Equal(TagSet.MaxTags, vm.TagNames.Count);
        Assert.Contains($"{TagSet.MaxTags}", vm.TagMessage);

        vm.TagEntry = "";
        vm.IsFavorite = true;
        Assert.Null(vm.TryBuild());
        Assert.Contains($"{TagSet.MaxTags}", vm.ValidationMessage);
        Assert.Equal(ProfileEditorField.Tags, vm.ValidationField);

        vm.RemoveLastTag();
        Assert.NotNull(vm.TryBuild());
        Assert.Null(vm.ValidationField);
    }

    /// <summary>Each refusal names its field, which is how the window picks the tab to show.</summary>
    [Fact]
    public void Each_refusal_names_its_field()
    {
        var vm = new ProfileEditorViewModel(null);
        Assert.Null(vm.TryBuild());
        Assert.Equal(ProfileEditorField.Name, vm.ValidationField);

        vm.Name = "n";
        Assert.Null(vm.TryBuild());
        Assert.Equal(ProfileEditorField.Host, vm.ValidationField);

        vm.Host = "h";
        vm.PortText = "0";
        Assert.Null(vm.TryBuild());
        Assert.Equal(ProfileEditorField.Port, vm.ValidationField);

        vm.PortText = "23";
        vm.KeepAliveText = "x";
        Assert.Null(vm.TryBuild());
        Assert.Equal(ProfileEditorField.KeepAlive, vm.ValidationField);

        vm.KeepAliveText = "60";
        vm.SelectedModelChoice = ModelChoice.Other;
        vm.ColumnsDisplay = "10";
        Assert.Null(vm.TryBuild());
        Assert.Equal(ProfileEditorField.ScreenSize, vm.ValidationField);

        vm.ColumnsDisplay = "80";
        Assert.NotNull(vm.TryBuild());
        Assert.Null(vm.ValidationMessage);
        Assert.Null(vm.ValidationField);
    }

    [Fact]
    public void A_blank_code_page_names_its_field()
    {
        var vm = new ProfileEditorViewModel(new SessionProfile { Name = "p", Host = "h", CodePage = " " });
        Assert.Null(vm.TryBuild());
        Assert.Equal(ProfileEditorField.CodePage, vm.ValidationField);
    }

    [Fact]
    public void A_blank_note_becomes_null_and_a_typed_one_is_trimmed()
    {
        var vm = new ProfileEditorViewModel(null) { Name = "n", Host = "h", Note = "   " };
        Assert.Null(vm.TryBuild()!.Note);
        vm.Note = "  LAN only  ";
        Assert.Equal("LAN only", vm.TryBuild()!.Note);
    }

    private ProfilePickerViewModel Picker(TagRegistryStore? tags = null) =>
        new(_store, (_, _) => { }, (_, _) => Task.FromResult<ProfileEdit?>(null), () => { }, tags);

    [Fact]
    public void Picker_shows_every_profile_until_something_narrows_it()
    {
        _store.Save(new SessionProfile { Name = "alpha", Host = "h" });
        _store.Save(new SessionProfile { Name = "zeta", Host = "h" });

        var vm = Picker();
        Assert.Equal(["alpha", "zeta"], vm.VisibleRows.Select(r => r.Name));
        Assert.Null(vm.SelectedScope!.TagName);
        Assert.Equal("All sessions", vm.SelectedScope.Label);
    }

    /// <summary>Scope narrows first, then the text box filters what is left — Robert's ordering.</summary>
    [Fact]
    public void The_scope_narrows_before_the_filter_text_does()
    {
        _store.Save(new SessionProfile { Name = "mvsce", Host = "h", Tags = TagSet.From(["PROD", "MVS"]) });
        _store.Save(new SessionProfile { Name = "mvs-dev", Host = "h", Tags = TagSet.From(["DEV"]) });
        _store.Save(new SessionProfile { Name = "gateway", Host = "h", Tags = TagSet.From(["PROD"]) });

        var vm = Picker();
        vm.SelectedScope = vm.Scopes.Single(s => s.TagName == "PROD");
        Assert.Equal(["gateway", "mvsce"], vm.VisibleRows.Select(r => r.Name));

        vm.FilterText = "mvs";
        Assert.Equal(["mvsce"], vm.VisibleRows.Select(r => r.Name));
    }

    [Fact]
    public void The_filter_text_matches_a_name_or_a_tag_but_not_a_host()
    {
        _store.Save(new SessionProfile { Name = "alpha", Host = "prod.example" });
        _store.Save(new SessionProfile { Name = "beta", Host = "h", Tags = TagSet.From(["PROD"]) });

        var vm = Picker();
        vm.FilterText = "prod";
        Assert.Equal(["beta"], vm.VisibleRows.Select(r => r.Name));

        vm.FilterText = "ALP";
        Assert.Equal(["alpha"], vm.VisibleRows.Select(r => r.Name));
    }

    [Fact]
    public void The_favorite_scope_narrows_to_the_starred_profiles()
    {
        _store.Save(new SessionProfile { Name = "starred", Host = "h", Tags = TagSet.From(["FAVORITE"]) });
        _store.Save(new SessionProfile { Name = "plain", Host = "h" });

        var vm = Picker();
        vm.SelectedScope = vm.Scopes.Single(s => s.TagName == TagRegistry.FavoriteName);
        Assert.Equal(["starred"], vm.VisibleRows.Select(r => r.Name));
    }

    /// <summary>What makes the filter box need no Enter handler: Connect is the window's default button, so as
    /// long as the selection is always a visible row, typing and pressing Enter connects what you are looking
    /// at rather than something the filter has hidden.</summary>
    [Fact]
    public void Filtering_moves_a_hidden_selection_to_the_first_visible_row()
    {
        _store.Save(new SessionProfile { Name = "alpha", Host = "h" });
        _store.Save(new SessionProfile { Name = "zeta", Host = "h" });

        var vm = Picker();
        vm.SelectedRow = vm.VisibleRows.Single(r => r.Name == "zeta");

        vm.FilterText = "alpha";
        Assert.Equal("alpha", vm.SelectedRow!.Name);
        Assert.Equal("alpha", vm.SelectedProfile!.Name);

        vm.FilterText = "nothing matches";
        Assert.Empty(vm.VisibleRows);
        Assert.Null(vm.SelectedRow);
        Assert.False(vm.ConnectCommand.CanExecute(null));
    }

    /// <summary>Quick Connect resolves against every saved profile, not the filtered view: a name the filter
    /// has hidden must still connect by name, as it does from the command line.</summary>
    [Fact]
    public void Quick_connect_still_finds_a_profile_the_filter_has_hidden()
    {
        _store.Save(new SessionProfile { Name = "tk5", Host = "tk5.local", Port = 3270 });
        SessionProfile? opened = null;
        var vm = new ProfilePickerViewModel(_store, (p, _) => opened = p, (_, _) => Task.FromResult<ProfileEdit?>(null), () => { });

        vm.FilterText = "zzz";
        Assert.Empty(vm.VisibleRows);

        vm.QuickConnectText = "tk5";
        vm.QuickConnectCommand.Execute(null);
        Assert.Equal("tk5.local", opened?.Host);
    }

    [Fact]
    public void The_scopes_list_is_all_sessions_then_favorite_then_the_tags_alphabetically()
    {
        _store.Save(new SessionProfile { Name = "a", Host = "h", Tags = TagSet.From(["zeta", "MVS"]) });

        var vm = Picker();
        Assert.Equal(["All sessions", "FAVORITE", "#MVS", "#ZETA"], vm.Scopes.Select(s => s.Label));
    }

    /// <summary>A scope whose tag no profile carries any more would otherwise filter the list to nothing, with no
    /// way back from the list itself; Manage Tags is where the unused definition gets deleted.</summary>
    [Fact]
    public void A_scope_whose_tag_no_longer_exists_falls_back_to_all_sessions()
    {
        _store.Save(new SessionProfile { Name = "a", Host = "h", Tags = TagSet.From(["PROD"]) });
        _store.Save(new SessionProfile { Name = "b", Host = "h" });

        var vm = Picker();
        vm.SelectedScope = vm.Scopes.Single(s => s.TagName == "PROD");
        Assert.Equal(["a"], vm.VisibleRows.Select(r => r.Name));

        _store.Save(new SessionProfile { Name = "a", Host = "h" });
        vm.Reload();

        Assert.Null(vm.SelectedScope!.TagName);
        Assert.Equal(["a", "b"], vm.VisibleRows.Select(r => r.Name));
    }

    /// <summary>RebuildScopes finds the previous scope by tag name so the drop-down survives a reload; Manage
    /// Tags' case-only rename (dev -&gt; DEV) leaves the profile and the registry defining the same tag under a new
    /// casing, and the match has to ignore case or the scope falls back to All sessions.</summary>
    [Fact]
    public void A_case_only_rename_keeps_the_scope_selected()
    {
        _store.Save(new SessionProfile { Name = "a", Host = "h", Tags = TagSet.From(["dev"]) });
        var tags = new TagRegistryStore(Path.Combine(_dir, "tags.json"));
        tags.Save(new TagRegistry([new TagDefinition("dev", TagColor.Teal)]));

        var vm = Picker(tags);
        vm.SelectedScope = vm.Scopes.Single(s => s.TagName == "dev");

        _store.Save(new SessionProfile { Name = "a", Host = "h", Tags = TagSet.From(["DEV"]) });
        tags.Save(new TagRegistry([new TagDefinition("DEV", TagColor.Teal)]));
        vm.Reload();

        Assert.NotNull(vm.SelectedScope);
        Assert.Equal("DEV", vm.SelectedScope!.TagName, ignoreCase: true);
    }

    /// <summary>Reconciliation: a tag name seen on a profile but absent from the registry registers itself, so
    /// a profile copied from another machine gets colours locally rather than rendering colourless. Reload also
    /// re-reads tags.json every time, so a registry deleted out from under the picker (Manage Tags writes the
    /// file while the picker waits behind it) is recreated rather than staying gone.</summary>
    [Fact]
    public void An_unknown_tag_registers_itself_and_a_deleted_registry_is_recreated_on_reload()
    {
        _store.Save(new SessionProfile { Name = "a", Host = "h", Tags = TagSet.From(["PROD"]) });
        var tagFile = Path.Combine(_dir, "tags.json");
        var tags = new TagRegistryStore(tagFile);

        var vm = Picker(tags);
        Assert.Equal(["PROD"], tags.Load().Stored.Select(d => d.Name));

        // Reload re-reads tags.json, so a deleted file is recreated with its unknown tags: the registry must
        // forget and relearn them, because Manage Tags writes the file while the picker waits behind it.
        File.Delete(tagFile);
        vm.Reload();
        Assert.True(File.Exists(tagFile), "Reload should recreate tags.json when it is deleted");
    }

    /// <summary>Scopes.Clear() in RebuildScopes makes a bound ComboBox null its own selection, and the two-way
    /// binding writes that null back into SelectedScope — so the filter runs, through
    /// OnSelectedScopeChanged, against a null scope on every rebuild. The declared type says that cannot
    /// happen and the compiler agrees, which is exactly why this needs asserting: before the guard, a real
    /// NullReferenceException was thrown on every picker activation and only Avalonia's own binding
    /// exception handling kept it off the screen. `null!` is the point of the test, not a shortcut.</summary>
    [Fact]
    public void Filtering_survives_the_null_scope_a_bound_combo_box_writes_back()
    {
        _store.Save(new SessionProfile { Name = "a", Host = "h", Tags = TagSet.From(["PROD"]) });
        _store.Save(new SessionProfile { Name = "b", Host = "h" });

        var vm = Picker();
        vm.SelectedScope = null;

        // Refilter runs on the assignment above and again here; neither may throw, and a null scope admits
        // everything, exactly as "All sessions" does.
        vm.FilterText = "";
        Assert.Equal(["a", "b"], vm.VisibleRows.Select(r => r.Name));
    }

    [Fact]
    public void Marking_a_favorite_writes_the_tag_and_keeps_the_selection()
    {
        _store.Save(new SessionProfile { Name = "alpha", Host = "h" });
        _store.Save(new SessionProfile { Name = "zeta", Host = "h", Tags = TagSet.From(["PROD"]) });

        var vm = Picker();
        vm.SelectedRow = vm.VisibleRows.Single(r => r.Name == "zeta");
        vm.ToggleFavoriteCommand.Execute(vm.SelectedRow);

        Assert.Equal(["FAVORITE", "PROD"], _store.Load("zeta")!.Tags.Names);
        Assert.True(_store.Load("alpha")!.Tags.IsEmpty);
        Assert.True(vm.VisibleRows.Single(r => r.Name == "zeta").IsFavorite);
        Assert.Equal("zeta", vm.SelectedRow?.Name);
    }

    [Fact]
    public void Removing_a_favorite_keeps_the_profiles_other_tags()
    {
        _store.Save(new SessionProfile { Name = "a", Host = "h", Tags = TagSet.From(["FAVORITE", "PROD", "MVS"]) });

        var vm = Picker();
        vm.ToggleFavoriteCommand.Execute(vm.VisibleRows.Single());

        Assert.Equal(["PROD", "MVS"], _store.Load("a")!.Tags.Names);
        Assert.False(vm.VisibleRows.Single().IsFavorite);
    }

    /// <summary>The entry the user clicked said "Mark", so the result is marked — even when another window starred
    /// the file after the list was drawn. A blind flip of what is on disk would unmark it instead, doing the
    /// opposite of what the menu offered.</summary>
    [Fact]
    public void A_stale_row_applies_the_choice_it_showed_rather_than_flipping_the_file()
    {
        _store.Save(new SessionProfile { Name = "a", Host = "h" });
        var vm = Picker();
        var stale = vm.VisibleRows.Single();

        _store.Save(new SessionProfile { Name = "a", Host = "h", Tags = TagSet.From(["FAVORITE"]) });
        vm.ToggleFavoriteCommand.Execute(stale);

        Assert.Equal(["FAVORITE"], _store.Load("a")!.Tags.Names);
    }

    /// <summary>ProfileStore.Update would save its fallback copy here, bringing back a profile deleted since the
    /// list was drawn.</summary>
    [Fact]
    public void Toggling_a_profile_whose_file_has_gone_does_not_bring_it_back()
    {
        _store.Save(new SessionProfile { Name = "a", Host = "h" });
        var vm = Picker();
        var row = vm.VisibleRows.Single();

        _store.Delete("a");
        vm.ToggleFavoriteCommand.Execute(row);

        Assert.Null(_store.Load("a"));
        Assert.Empty(vm.VisibleRows);
    }

    [Fact]
    public void A_profile_at_the_tag_cap_cannot_be_marked()
    {
        _store.Save(new SessionProfile { Name = "a", Host = "h", Tags = TagSet.From(Enumerable.Range(0, TagSet.MaxTags).Select(i => $"T{i}")) });

        var vm = Picker();
        Assert.False(vm.ToggleFavoriteCommand.CanExecute(vm.VisibleRows.Single()));
    }

    /// <summary>#50 again, for the star: a session window can pin a certificate into the file after the list was
    /// drawn, and the toggle must write the file it re-read, not the row's copy.</summary>
    [Fact]
    public void Toggling_a_stale_row_keeps_a_pin_written_since_the_list_was_drawn()
    {
        _store.Save(new SessionProfile { Name = "a", Host = "h" });
        var vm = Picker();
        var stale = vm.VisibleRows.Single();

        var pin = new CertificatePin("AA:BB", "CN=a", "pem");
        _store.Save(new SessionProfile { Name = "a", Host = "h", PinnedCertificate = pin });
        vm.ToggleFavoriteCommand.Execute(stale);

        var saved = _store.Load("a")!;
        Assert.Equal(pin, saved.PinnedCertificate);
        Assert.True(saved.Tags.Contains("FAVORITE"));
    }

    /// <summary>A stale row whose choice the file already reflects has nothing to write, and must not rewrite the
    /// file from a copy that may be older than what is there.</summary>
    [Fact]
    public void A_toggle_the_file_already_agrees_with_does_not_rewrite_it()
    {
        _store.Save(new SessionProfile { Name = "a", Host = "h" });
        var vm = Picker();
        var stale = vm.VisibleRows.Single();

        _store.Save(new SessionProfile { Name = "a", Host = "h", Tags = TagSet.From(["FAVORITE"]) });
        var file = Path.Combine(_dir, ProfileStore.FileNameFor("a"));
        var written = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(file, written);
        vm.ToggleFavoriteCommand.Execute(stale);

        Assert.Equal(written, File.GetLastWriteTimeUtc(file));
        Assert.Equal(["FAVORITE"], _store.Load("a")!.Tags.Names);
    }

    /// <summary>The entry was enabled for a row with room, but the file has filled up since. TagSet.From would keep
    /// the first eight and drop the last of the file's own tags; the command must leave the file alone instead.</summary>
    [Fact]
    public void Marking_a_row_whose_file_has_since_filled_up_leaves_the_file_alone()
    {
        var seven = Enumerable.Range(0, TagSet.MaxTags - 1).Select(i => $"T{i}").ToList();
        _store.Save(new SessionProfile { Name = "a", Host = "h", Tags = TagSet.From(seven) });
        var vm = Picker();
        var stale = vm.VisibleRows.Single();
        Assert.True(vm.ToggleFavoriteCommand.CanExecute(stale));

        var eight = Enumerable.Range(0, TagSet.MaxTags).Select(i => $"T{i}").ToList();
        _store.Save(new SessionProfile { Name = "a", Host = "h", Tags = TagSet.From(eight) });
        vm.ToggleFavoriteCommand.Execute(stale);

        Assert.Equal(eight, _store.Load("a")!.Tags.Names);
        Assert.False(vm.VisibleRows.Single().CanToggleFavorite);
    }

    /// <summary>Every window activation reloads. When the store has not changed, the rows — and with them the
    /// list's containers — survive, so the click that activated the picker lands on the row it aimed at.</summary>
    [Fact]
    public void A_reload_that_finds_nothing_changed_keeps_the_rows()
    {
        _store.Save(new SessionProfile { Name = "a", Host = "h" });
        var vm = Picker();
        var before = vm.VisibleRows.Single();
        vm.SelectedRow = before;

        vm.Reload();
        Assert.Same(before, vm.VisibleRows.Single());
        Assert.Same(before, vm.SelectedRow);

        _store.Save(new SessionProfile { Name = "a", Host = "h", Note = "changed" });
        vm.Reload();
        Assert.NotSame(before, vm.VisibleRows.Single());
        Assert.Equal("changed", vm.VisibleRows.Single().Note);
    }

    /// <summary>The row leaves a FAVORITE-scoped list the moment it loses the star, and the selection follows the
    /// rule every other refilter uses: the first row still visible.</summary>
    [Fact]
    public void Unmarking_under_the_favorite_scope_hides_the_row_and_keeps_the_scope()
    {
        _store.Save(new SessionProfile { Name = "a", Host = "h", Tags = TagSet.From(["FAVORITE"]) });
        _store.Save(new SessionProfile { Name = "b", Host = "h", Tags = TagSet.From(["FAVORITE"]) });

        var vm = Picker();
        vm.SelectedScope = vm.Scopes.Single(s => s.TagName == TagRegistry.FavoriteName);
        vm.SelectedRow = vm.VisibleRows.Single(r => r.Name == "a");
        vm.ToggleFavoriteCommand.Execute(vm.SelectedRow);

        Assert.Equal(TagRegistry.FavoriteName, vm.SelectedScope?.TagName);
        Assert.Equal(["b"], vm.VisibleRows.Select(r => r.Name));
        Assert.Equal("b", vm.SelectedRow?.Name);
    }

    /// <summary>Manage Tags writes tags.json while this picker waits behind it, so Reload must read the file again
    /// rather than keep the registry it read when it opened.</summary>
    [Fact]
    public void Reload_shows_a_colour_changed_on_disk()
    {
        _store.Save(new SessionProfile { Name = "a", Host = "h", Tags = TagSet.From(["PROD"]) });
        var tags = new TagRegistryStore(Path.Combine(_dir, "tags.json"));
        tags.Save(new TagRegistry([new TagDefinition("PROD", TagColor.Red)]));
        var vm = Picker(tags);

        tags.Save(new TagRegistry([new TagDefinition("PROD", TagColor.Green)]));
        vm.Reload();

        Assert.Same(TagPalette.Brush(TagColor.Green), vm.VisibleRows.Single().Chips.Single().Background);
    }

    /// <summary>The other half: a registry held from construction would write a definition deleted on disk back the
    /// next time the picker registered anything new.</summary>
    [Fact]
    public void Reload_does_not_write_back_a_definition_deleted_on_disk()
    {
        _store.Save(new SessionProfile { Name = "a", Host = "h", Tags = TagSet.From(["PROD"]) });
        var tags = new TagRegistryStore(Path.Combine(_dir, "tags.json"));
        tags.Save(new TagRegistry([new TagDefinition("LAB", TagColor.Teal), new TagDefinition("PROD", TagColor.Red)]));
        var vm = Picker(tags);

        tags.Save(new TagRegistry([new TagDefinition("PROD", TagColor.Red)]));
        _store.Save(new SessionProfile { Name = "b", Host = "h", Tags = TagSet.From(["MVS"]) });
        vm.Reload();

        Assert.Equal(["MVS", "PROD"], tags.Load().Stored.Select(d => d.Name));
    }

    /// <summary>Reconcile saves only when TagRegistry.Register reports something changed. F1 made a blocked save
    /// survivable, so a throwing Save can no longer be observed at all -- this has to detect a write by its effect
    /// on the file's text instead, the way TagMaintenanceTests.Load_does_not_rewrite_the_registry_when_every_tag_is_known
    /// does: a profile whose every tag is already known must leave a hand-written registry file untouched, byte for
    /// byte. TagRegistryStore writes indented JSON, so any save reformats this one-line file.</summary>
    [Fact]
    public void Reload_does_not_rewrite_tags_json_when_every_tag_is_already_known()
    {
        _store.Save(new SessionProfile { Name = "a", Host = "h", Tags = TagSet.From(["PROD"]) });
        var tagFile = Path.Combine(_dir, "tags.json");
        const string handWritten = """{"tags":[{"name":"PROD","color":"Red"}]}""";
        Directory.CreateDirectory(_dir);
        File.WriteAllText(tagFile, handWritten);
        var tags = new TagRegistryStore(tagFile);

        var vm = Picker(tags);
        vm.Reload();

        Assert.Equal(handWritten, File.ReadAllText(tagFile));
    }

    /// <summary>Manage Tags reports a failed tags.json save as survivable ("... TEST may show a different colour
    /// next time"), so the picker's own reconciliation must swallow the same failure rather than crash: the
    /// constructor's Reload(), a second Reload(), and ManageTagsCommand's own reload all run Reconcile() against a
    /// tags.json this test has already made unwritable.</summary>
    [Fact]
    public async Task A_blocked_tags_file_does_not_crash_reconciliation()
    {
        _store.Save(new SessionProfile { Name = "a", Host = "h", Tags = TagSet.From(["TEST"]) });
        var tagFile = Path.Combine(_dir, "tags.json");
        Directory.CreateDirectory(tagFile + ".tmp");
        var tags = new TagRegistryStore(tagFile);

        var vm = new ProfilePickerViewModel(_store, (_, _) => { }, (_, _) => Task.FromResult<ProfileEdit?>(null), () => { },
            tags, () => Task.CompletedTask);
        vm.Reload();
        await vm.ManageTagsCommand.ExecuteAsync(null);

        Assert.Equal(["TEST"], vm.VisibleRows.Single().Chips.Select(c => c.Text));
    }

    [Fact]
    public async Task Tags_opens_manage_tags_and_reloads_when_it_closes()
    {
        _store.Save(new SessionProfile { Name = "a", Host = "h" });
        var opened = 0;
        var vm = new ProfilePickerViewModel(_store, (_, _) => { }, (_, _) => Task.FromResult<ProfileEdit?>(null), () => { }, null,
            () =>
            {
                opened++;
                _store.Save(new SessionProfile { Name = "b", Host = "h" });
                return Task.CompletedTask;
            });

        await vm.ManageTagsCommand.ExecuteAsync(null);

        Assert.Equal(1, opened);
        Assert.Equal(["a", "b"], vm.VisibleRows.Select(r => r.Name));
    }

    /// <summary>A recolour changes tags.json and no profile, so the reload's "nothing changed" shortcut must look
    /// at the registry too, or the picker keeps drawing the old colour until some profile file happens to change.</summary>
    [Fact]
    public async Task A_recolour_in_manage_tags_shows_in_the_picker_when_it_closes()
    {
        _store.Save(new SessionProfile { Name = "a", Host = "h", Tags = TagSet.From(["PROD"]) });
        var tags = new TagRegistryStore(Path.Combine(_dir, "tags.json"));
        tags.Save(new TagRegistry([new TagDefinition("PROD", TagColor.Red)]));
        var vm = new ProfilePickerViewModel(_store, (_, _) => { }, (_, _) => Task.FromResult<ProfileEdit?>(null), () => { }, tags,
            () =>
            {
                tags.Save(new TagRegistry([new TagDefinition("PROD", TagColor.Green)]));
                return Task.CompletedTask;
            });
        vm.Reload();

        await vm.ManageTagsCommand.ExecuteAsync(null);

        Assert.Same(TagPalette.Brush(TagColor.Green), vm.VisibleRows.Single().Chips.Single().Background);
    }

    [Fact]
    public void Tags_is_unavailable_without_a_way_to_open_it()
    {
        Assert.False(Picker().ManageTagsCommand.CanExecute(null));
    }
}
