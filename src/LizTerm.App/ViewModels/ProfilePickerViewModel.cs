// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LizTerm.App.Startup;
using LizTerm.Core.Profiles;
using LizTerm.Core.Session;

namespace LizTerm.App.ViewModels;

public partial class ProfilePickerViewModel : ObservableObject
{
    private readonly ProfileStore _store;
    private readonly Action<SessionProfile, bool> _openSession;
    private readonly Func<SessionProfile?, Task<ProfileEdit?>> _editProfile;
    private readonly Action _quit;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand), nameof(EditCommand), nameof(DeleteCommand))]
    private SessionProfile? _selectedProfile;

    public ObservableCollection<SessionProfile> Profiles { get; } = [];

    /// <param name="openSession">Opens a session. The bool is whether the profile came from the store: a pin
    /// chosen in that window can be written back only for a saved profile, and Quick Connect's ad hoc profiles
    /// are not saved.</param>
    /// <param name="editProfile">Shows the editor for an existing profile (or null for a new one); returns null when cancelled.</param>
    public ProfilePickerViewModel(ProfileStore store, Action<SessionProfile, bool> openSession, Func<SessionProfile?, Task<ProfileEdit?>> editProfile, Action quit)
    {
        _store = store;
        _openSession = openSession;
        _editProfile = editProfile;
        _quit = quit;
        Reload();
    }

    public bool HasSelection => SelectedProfile is not null;

    public void Reload()
    {
        var selectedName = SelectedProfile?.Name;
        Profiles.Clear();
        foreach (var profile in _store.LoadAll()) Profiles.Add(profile);
        SelectedProfile = Profiles.FirstOrDefault(p => p.Name == selectedName);
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Connect() => _openSession(SelectedProfile!, true);

    [RelayCommand]
    private async Task NewAsync()
    {
        var edit = await _editProfile(null);
        if (edit is null) return;
        _store.Save(edit.Profile);
        Reload();
        SelectedProfile = Profiles.FirstOrDefault(p => p.Name == edit.Profile.Name);
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task EditAsync()
    {
        var original = SelectedProfile!;
        Reload();
        // Read the file, not the copy this picker has been holding: a session window may have written a pin into
        // it since Reload last ran, and the editor was handed the older copy.
        var edit = await _editProfile(_store.Load(original.Name) ?? original);
        if (edit is null) return;

        // Under the ORIGINAL name. A rename deletes that file below, so looking the pin up under the new one
        // finds nothing and loses it exactly as the bug did.
        var onDisk = _store.Load(original.Name);
        var merged = edit.Profile with { PinnedCertificate = PinMerge.Resolve(edit.Profile, onDisk, edit.PinCleared) };

        if (!merged.Name.Equals(original.Name, StringComparison.OrdinalIgnoreCase)) _store.Delete(original.Name);
        _store.Save(merged);
        Reload();
        SelectedProfile = Profiles.FirstOrDefault(p => p.Name == merged.Name);
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Delete()
    {
        _store.Delete(SelectedProfile!.Name);
        SelectedProfile = null;
        Reload();
    }

    [RelayCommand]
    private void Quit() => _quit();

    [ObservableProperty] private string _quickConnectText = "";
    [ObservableProperty] private string? _quickConnectError;

    /// <summary>Connect to what the box names, without saving anything. The parse and the profile-name
    /// precedence are the command line's own — the same Parse and Resolve, so the box cannot drift from it —
    /// which is why a saved profile called "CONS01@tk5" stays reachable by its own name here too (spec 7.1).</summary>
    [RelayCommand]
    private void QuickConnect()
    {
        var text = QuickConnectText.Trim();
        if (text.Length == 0)
        {
            QuickConnectError = "Type a host name, or the name of a saved session.";
            return;
        }

        var parsed = StartupArguments.Parse([text]);
        if (parsed.Resolve([.. Profiles]) is { } profile)
        {
            QuickConnectError = null;
            // fromStore observes which branch Resolve took rather than re-deriving its rule: Resolve returns the
            // list's own instance for the saved branch and a freshly constructed record for the ad hoc branch, so
            // reference identity is exact. A name comparison here would be wrong: the ad hoc branch defaults a
            // missing port (23 or 992), so its generated name ("mvs.example:23") can collide with an unrelated
            // saved profile's name, which would report fromStore = true for a connection nothing ever saved and
            // let a pin get written into that unrelated profile's file. If Resolve ever returned a copy instead of
            // the list's own instance, this would fail safe -- no pin write-back -- rather than write into the
            // wrong file.
            _openSession(profile, Profiles.Any(p => ReferenceEquals(p, profile)));
            return;
        }

        QuickConnectError = parsed.Error is null
            ? $"\"{text}\" is not a saved session, and does not look like a host. Add a port to connect to it as a host, for example {text}:23."
            : "Type host, host:port, or L:host for TLS. An IPv6 address goes in brackets.";
    }
}
