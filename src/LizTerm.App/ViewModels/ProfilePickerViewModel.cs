// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LizTerm.Core.Profiles;
using LizTerm.Core.Session;

namespace LizTerm.App.ViewModels;

public partial class ProfilePickerViewModel : ObservableObject
{
    private readonly ProfileStore _store;
    private readonly Action<SessionProfile> _openSession;
    private readonly Func<SessionProfile?, Task<SessionProfile?>> _editProfile;
    private readonly Action _quit;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand), nameof(EditCommand), nameof(DeleteCommand))]
    private SessionProfile? _selectedProfile;

    public ObservableCollection<SessionProfile> Profiles { get; } = [];

    /// <param name="editProfile">Shows the editor for an existing profile (or null for a new one); returns null when cancelled.</param>
    public ProfilePickerViewModel(ProfileStore store, Action<SessionProfile> openSession, Func<SessionProfile?, Task<SessionProfile?>> editProfile, Action quit)
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
    private void Connect() => _openSession(SelectedProfile!);

    [RelayCommand]
    private async Task NewAsync()
    {
        var created = await _editProfile(null);
        if (created is null) return;
        _store.Save(created);
        Reload();
        SelectedProfile = Profiles.FirstOrDefault(p => p.Name == created.Name);
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task EditAsync()
    {
        var original = SelectedProfile!;
        var edited = await _editProfile(original);
        if (edited is null) return;
        if (!edited.Name.Equals(original.Name, StringComparison.OrdinalIgnoreCase)) _store.Delete(original.Name);
        _store.Save(edited);
        Reload();
        SelectedProfile = Profiles.FirstOrDefault(p => p.Name == edited.Name);
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
}
