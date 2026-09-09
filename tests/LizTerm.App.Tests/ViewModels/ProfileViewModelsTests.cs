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
            existing => Task.FromResult<ProfileEdit?>(new ProfileEdit(existing! with { Name = "MVS-CE" }, PinCleared: false)),
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
        vm.ForgetPinCommand.Execute(null);
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

    [Fact]
    public void A_new_profile_has_no_pin()
    {
        var vm = new ProfileEditorViewModel(null);
        Assert.False(vm.HasPinnedCertificate);
        Assert.Null(vm.PinnedCertificateText);
        Assert.Null(vm.TryBuild()?.PinnedCertificate);
    }
}
