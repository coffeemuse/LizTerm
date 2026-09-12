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
    private readonly TagRegistryStore? _tags;
    private TagRegistry _registry = TagRegistry.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand), nameof(EditCommand), nameof(DeleteCommand))]
    private ProfileRow? _selectedRow;

    /// <summary>The selected profile, which is the row's. Derived rather than stored so the list's selection
    /// and the commands' subject can never disagree.</summary>
    public SessionProfile? SelectedProfile => SelectedRow?.Profile;

    /// <summary>Every saved profile, in ProfileStore's own order. Quick Connect resolves against THIS rather
    /// than the filtered view, and depends on its instances' identity for pin write-back — see QuickConnect.</summary>
    public ObservableCollection<SessionProfile> Profiles { get; } = [];

    /// <summary>What the list draws: Profiles narrowed by the scope and then by the filter text, projected
    /// against the registry so each row carries resolved chips (see ProfileRow).</summary>
    public ObservableCollection<ProfileRow> VisibleRows { get; } = [];

    public ObservableCollection<ScopeOption> Scopes { get; } = [];

    /// <summary>Every profile, whatever its tags. Held as a field so the drop-down's rebuilt list can select
    /// the same option object back.</summary>
    public static readonly ScopeOption AllSessions = new("All sessions", null);

    [ObservableProperty] private ScopeOption _selectedScope = AllSessions;

    [ObservableProperty] private string _filterText = "";

    partial void OnSelectedScopeChanged(ScopeOption value) => Refilter();

    partial void OnFilterTextChanged(string value) => Refilter();

    /// <param name="openSession">Opens a session. The bool is whether the profile came from the store: a pin
    /// chosen in that window can be written back only for a saved profile, and Quick Connect's ad hoc profiles
    /// are not saved.</param>
    /// <param name="editProfile">Shows the editor for an existing profile (or null for a new one); returns null when cancelled.</param>
    /// <param name="tags">The tag registry's file, or null for an in-memory registry that is never written,
    /// which is what a test wants.</param>
    public ProfilePickerViewModel(ProfileStore store, Action<SessionProfile, bool> openSession,
        Func<SessionProfile?, Task<ProfileEdit?>> editProfile, Action quit, TagRegistryStore? tags = null)
    {
        _store = store;
        _openSession = openSession;
        _editProfile = editProfile;
        _quit = quit;
        _tags = tags;
        _registry = tags?.Load() ?? TagRegistry.Empty;
        Reload();
    }

    public bool HasSelection => SelectedRow is not null;

    public void Reload()
    {
        var selectedName = SelectedProfile?.Name;
        Profiles.Clear();
        foreach (var profile in _store.LoadAll()) Profiles.Add(profile);

        Reconcile();
        // RebuildScopes may assign SelectedScope, whose handler calls Refilter() on its own; the explicit call
        // below then runs a second time with the remembered name. Harmless and deliberate — do not "fix" it by
        // dropping either call, because the scope may legitimately change here and the selection must survive.
        RebuildScopes();
        Refilter(selectedName);
    }

    /// <summary>Registers any tag name the profiles carry that the registry does not know, and writes the file
    /// only when something was added. Here rather than in ProfileStore.LoadAll on purpose: a load must not
    /// write, and Core's store stays a pure reader. It is also what makes a profile copied from another machine
    /// gain colours locally — the names travel in the profile, the colours are assigned on first sight.</summary>
    private void Reconcile()
    {
        var (registry, changed) = _registry.Register(Profiles.SelectMany(p => p.Tags.Names));
        _registry = registry;
        if (changed) _tags?.Save(registry);
    }

    private void RebuildScopes()
    {
        var wanted = _registry.All
            .Where(definition => Profiles.Any(p => p.Tags.Contains(definition.Name)) || TagRegistry.IsReserved(definition.Name))
            .Select(definition => TagRegistry.IsReserved(definition.Name)
                ? new ScopeOption(definition.Name, definition.Name)
                : new ScopeOption($"#{definition.Name}", definition.Name))
            .ToList();

        Scopes.Clear();
        Scopes.Add(AllSessions);
        foreach (var scope in wanted) Scopes.Add(scope);

        // A scope whose tag no longer exists anywhere would filter to nothing with no way back.
        if (Scopes.FirstOrDefault(s => s.TagName == SelectedScope.TagName) is { } still) SelectedScope = still;
        else SelectedScope = AllSessions;
    }

    /// <summary>Scope first, then the typed text, then a selection that is still on screen.</summary>
    private void Refilter(string? preferName = null)
    {
        var preferred = preferName ?? SelectedProfile?.Name;
        var text = FilterText.Trim();

        VisibleRows.Clear();
        foreach (var profile in Profiles.Where(InScope).Where(p => Matches(p, text)))
        {
            VisibleRows.Add(new ProfileRow(profile, _registry));
        }

        SelectedRow = VisibleRows.FirstOrDefault(r => r.Name == preferred) ?? VisibleRows.FirstOrDefault();
    }

    private bool InScope(SessionProfile profile) =>
        SelectedScope.TagName is not { } tag || profile.Tags.Contains(tag);

    /// <summary>Name or any tag name, ignoring case. Not the host: Quick Connect is where a host is typed.</summary>
    private static bool Matches(SessionProfile profile, string text) =>
        text.Length == 0
        || profile.Name.Contains(text, StringComparison.OrdinalIgnoreCase)
        || profile.Tags.Names.Any(name => name.Contains(text, StringComparison.OrdinalIgnoreCase));

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Connect() => _openSession(SelectedProfile!, true);

    [RelayCommand]
    private async Task NewAsync()
    {
        var edit = await _editProfile(null);
        if (edit is null) return;
        _store.Save(edit.Profile);
        Reload();
        SelectedRow = VisibleRows.FirstOrDefault(r => r.Name == edit.Profile.Name) ?? SelectedRow;
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
        SelectedRow = VisibleRows.FirstOrDefault(r => r.Name == merged.Name) ?? SelectedRow;
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Delete()
    {
        _store.Delete(SelectedProfile!.Name);
        SelectedRow = null;
        Reload();
    }

    [RelayCommand]
    private void Quit() => _quit();

    [ObservableProperty] private string _quickConnectText = "";
    [ObservableProperty] private string? _quickConnectError;

    /// <summary>The message is about the text that was in the box when Connect was pressed, so the first
    /// keystroke that changes it makes the message stale — and a red line under a box the user has since
    /// retyped reads as a complaint about what they are typing now.</summary>
    partial void OnQuickConnectTextChanged(string value) => QuickConnectError = null;

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
