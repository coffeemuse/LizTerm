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
    private readonly Func<SessionProfile?, TagRegistry, Task<ProfileEdit?>> _editProfile;
    private readonly Action _quit;
    private readonly TagRegistryStore? _tags;
    private readonly Func<Task>? _manageTags;
    private readonly RecentHostsStore? _recentHosts;
    private RecentHosts _recent;
    private TagRegistry _registry = TagRegistry.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand), nameof(EditCommand), nameof(DeleteCommand))]
    [NotifyPropertyChangedFor(nameof(SelectedProfile), nameof(HasSelection))]
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

    [ObservableProperty] private ScopeOption? _selectedScope = AllSessions;

    [ObservableProperty] private string _filterText = "";

    partial void OnSelectedScopeChanged(ScopeOption? value) => Refilter();

    partial void OnFilterTextChanged(string value) => Refilter();

    /// <param name="openSession">Opens a session. The bool is whether the profile came from the store: a pin
    /// chosen in that window can be written back only for a saved profile, and Quick Connect's ad hoc profiles
    /// are not saved.</param>
    /// <param name="editProfile">Shows the editor for an existing profile (or null for a new one), drawing its tag
    /// chips against the registry passed; returns null when cancelled.</param>
    /// <param name="tags">The tag registry's file, or null for an in-memory registry that is never written,
    /// which is what a test wants.</param>
    /// <param name="manageTags">Shows Manage Tags and completes when it closes, or null where there is no tag file
    /// to manage — which is also what a test that does not care wants.</param>
    /// <param name="recentHosts">Quick Connect's history file, or null for a history kept in memory only.</param>
    public ProfilePickerViewModel(ProfileStore store, Action<SessionProfile, bool> openSession,
        Func<SessionProfile?, TagRegistry, Task<ProfileEdit?>> editProfile, Action quit, TagRegistryStore? tags = null,
        Func<Task>? manageTags = null, RecentHostsStore? recentHosts = null)
    {
        _store = store;
        _openSession = openSession;
        _editProfile = editProfile;
        _quit = quit;
        _tags = tags;
        _manageTags = manageTags;
        _recentHosts = recentHosts;
        // Once, not in Reload: this picker is the file's only writer (App keeps one picker at a time), and
        // rebuilding a collection an open drop-down is showing on every activation would buy nothing.
        _recent = recentHosts?.Load() ?? RecentHosts.Empty;
        foreach (var entry in _recent.Entries) RecentEntries.Add(entry);
        Reload();
    }

    public bool HasSelection => SelectedRow is not null;

    private bool _loadedOnce;

    public void Reload()
    {
        var loaded = _store.LoadAll();
        // Both files, every time, not only at construction: Manage Tags writes tags.json while this picker waits
        // behind it, and a registry kept from construction would give a renamed tag a fresh colour and save over
        // the one carried across, write a deleted definition back the next time anything registers, and show an
        // old colour until the picker reopened. With no store — the in-memory registry tests use — there is
        // nothing to re-read, so the registry in memory is the registry.
        var registry = _tags?.Load() ?? _registry;
        // Every window activation reloads, and most find nothing changed. Returning here keeps every ProfileRow
        // and every ListBoxItem, which is what lets the click that activates the picker land on the row it aimed
        // at (App CLAUDE.md, "The session picker's tags"). Exact, not a heuristic: SessionProfile is a record whose
        // only non-BCL members, TagSet and CertificatePin, carry value equality, and TagDefinition is a record.
        // The registry is part of the test, or a recolour (which changes no profile) would never be drawn. Never
        // on the first load, so an empty store still gets its scopes.
        if (_loadedOnce && loaded.SequenceEqual(Profiles) && registry.Stored.SequenceEqual(_registry.Stored)) return;
        _loadedOnce = true;

        var selectedName = SelectedProfile?.Name;
        Profiles.Clear();
        foreach (var profile in loaded) Profiles.Add(profile);

        _registry = registry;
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
        // TrySave, not Save: the registry in memory is still correct for drawing this list, the colours are only
        // lost if the file stays unwritable, and the next Reload re-reads the file and so retries the write.
        // Crashing the picker (and every open session window with it) over a colour file is worse.
        if (changed) _tags?.TrySave(registry);
    }

    private void RebuildScopes()
    {
        // Read before Clear(), not after: ScopeBox's SelectedItem is two-way bound to SelectedScope, and
        // Avalonia's SelectingItemsControl reacts to the Reset below by nulling its own selection, which the
        // two-way binding writes straight back into SelectedScope. Re-reading the property afterwards would see
        // that transient null instead of what the user actually had selected. The field is declared nullable
        // because the binding really does write null into it, so this read cannot be an ordering invariant that
        // a later edit could silently break.
        var previousTagName = SelectedScope?.TagName;

        // Tags some profile actually carries, not every registered tag (the tags spec's 5.3 says registered).
        // Deliberate, and kept when Manage Tags arrived (#88, its spec 2.7): a scope for a tag no profile carries
        // filters the list to nothing, and Manage Tags is where an unused definition is found and deleted.
        var wanted = _registry.All
            .Where(definition => Profiles.Any(p => p.Tags.Contains(definition.Name)) || TagRegistry.IsReserved(definition.Name))
            .Select(definition => TagRegistry.IsReserved(definition.Name)
                ? new ScopeOption(definition.Name, definition.Name)
                : new ScopeOption($"#{definition.Name.ToUpperInvariant()}", definition.Name))
            .ToList();

        Scopes.Clear();
        Scopes.Add(AllSessions);
        foreach (var scope in wanted) Scopes.Add(scope);

        // A scope whose tag no longer exists anywhere would filter to nothing with no way back. Ignoring case so
        // a case-only rename (Manage Tags, #88) keeps the scope selected: dev -> DEV is still "the same scope" to
        // a user, and string.Equals(string?, string?, StringComparison) already answers true for null and null,
        // which is what AllSessions' null TagName needs.
        if (Scopes.FirstOrDefault(s => string.Equals(s.TagName, previousTagName, StringComparison.OrdinalIgnoreCase)) is { } still)
            SelectedScope = still;
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

        // Keep a selection that is still visible, and move one the filter has hidden to the first visible row —
        // but never invent a selection where there was none. Connect is the window's IsDefault button, so
        // promoting the alphabetically-first profile on load would arm Enter to connect it the moment the picker
        // appears, including when it reappears by itself as the last session window closes (spec 5.2, 5.4).
        SelectedRow = preferred is null
            ? null
            : VisibleRows.FirstOrDefault(r => r.Name == preferred) ?? VisibleRows.FirstOrDefault();
    }

    /// <summary>Whether the scope drop-down admits this profile. A property PATTERN rather than a dereference,
    /// because SelectedScope is genuinely null for part of every RebuildScopes: Scopes.Clear() makes the bound
    /// ComboBox null its own selection and the two-way binding writes that back, which fires this method through
    /// OnSelectedScopeChanged before the rebuild reassigns a real scope. The declared type says non-nullable and
    /// the compiler believes it, so nothing warns — only the pattern keeps it from throwing. Its semantics are
    /// the ones wanted anyway: no scope means everything, which is what AllSessions already means.</summary>
    private bool InScope(SessionProfile profile) =>
        SelectedScope is not { TagName: { } tag } || profile.Tags.Contains(tag);

    /// <summary>Name or any tag name, ignoring case. Not the host: Quick Connect is where a host is typed.</summary>
    private static bool Matches(SessionProfile profile, string text) =>
        text.Length == 0
        || profile.Name.Contains(text, StringComparison.OrdinalIgnoreCase)
        || profile.Tags.Names.Any(name => name.Contains(text, StringComparison.OrdinalIgnoreCase));

    /// <summary>Whether Connect and Edit have a subject: the row they were handed (the row menu passes its own,
    /// so an entry acts on the row the menu belongs to even when a keyboard opened it on an unselected row), else
    /// the selection (the buttons and the double-tap pass null).</summary>
    private bool CanActOn(ProfileRow? row) => (row ?? SelectedRow) is not null;

    [RelayCommand(CanExecute = nameof(CanActOn))]
    private void Connect(ProfileRow? row) => _openSession((row ?? SelectedRow)!.Profile, true);

    [RelayCommand]
    private async Task NewAsync()
    {
        var edit = await _editProfile(null, _registry);
        if (edit is null) return;
        _store.Save(edit.Profile);
        Reload();
        SelectedRow = VisibleRows.FirstOrDefault(r => r.Name == edit.Profile.Name) ?? SelectedRow;
    }

    [RelayCommand(CanExecute = nameof(CanActOn))]
    private async Task EditAsync(ProfileRow? row)
    {
        var original = (row ?? SelectedRow)!.Profile;
        Reload();
        // Read the file, not the copy this picker has been holding: a session window may have written a pin into
        // it since Reload last ran, and the editor was handed the older copy.
        var edit = await _editProfile(_store.Load(original.Name) ?? original, _registry);
        if (edit is null) return;

        // Under the ORIGINAL name. A rename deletes that file below, so looking the pin up under the new one
        // finds nothing and loses it exactly as the bug did.
        var onDisk = _store.Load(original.Name);
        var merged = PinMerge.Apply(edit.Profile, onDisk, edit.PinCleared, edit.HostFilesPinCleared);

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

    /// <summary>Tags...: Manage Tags over this picker, then a reload, since it may have renamed, recoloured or deleted
    /// any tag on any profile.</summary>
    [RelayCommand(CanExecute = nameof(CanManageTags))]
    private async Task ManageTagsAsync()
    {
        await _manageTags!();
        Reload();
    }

    private bool CanManageTags() => _manageTags is not null;

    /// <summary>Stars or unstars the row's profile, from the row menu. The row, not the selection, because the
    /// menu belongs to the row it was opened on.
    ///
    /// Applies the choice the row SHOWED rather than flipping the file: another window may have written the
    /// profile since the list was drawn, and Starred leaves a file that already agrees alone. The file is re-read
    /// rather than using the row's copy, so that write is not lost either — and a file that has gone stays gone,
    /// which is why this is not ProfileStore.Update, whose fallback would save it back. A file that filled up
    /// since the row was drawn cannot take the star; the reload then shows why in the entry itself.</summary>
    [RelayCommand(CanExecute = nameof(CanToggleFavorite))]
    private void ToggleFavorite(ProfileRow? row)
    {
        if (row is null) return;
        if (_store.Load(row.Name) is { } onDisk)
        {
            var tags = Starred(onDisk.Tags, star: !row.IsFavorite);
            if (tags != onDisk.Tags) _store.Save(onDisk with { Tags = tags });
        }
        Reload();
    }

    private static bool CanToggleFavorite(ProfileRow? row) => row?.CanToggleFavorite == true;

    /// <summary>The tags with the star added or removed. FAVORITE goes first, as the editor writes it, so the two
    /// doors produce the same file; a set that already agrees, or has no room, comes back unchanged.</summary>
    private static TagSet Starred(TagSet tags, bool star)
    {
        if (!star) return tags.Without(TagRegistry.FavoriteName);
        if (tags.Contains(TagRegistry.FavoriteName) || !tags.CanAdd(TagRegistry.FavoriteName)) return tags;
        return TagSet.From([TagRegistry.FavoriteName, .. tags.Names]);
    }

    [RelayCommand]
    private void Quit() => _quit();

    [ObservableProperty] private string _quickConnectText = "";
    [ObservableProperty] private string? _quickConnectError;

    /// <summary>The message is about the text that was in the box when Connect was pressed, so the first
    /// keystroke that changes it makes the message stale — and a red line under a box the user has since
    /// retyped reads as a complaint about what they are typing now.</summary>
    partial void OnQuickConnectTextChanged(string value) => QuickConnectError = null;

    /// <summary>Quick Connect's drop-down: the recent ad hoc hosts, newest first, as typed.</summary>
    public ObservableCollection<string> RecentEntries { get; } = [];

    /// <summary>Moves the entry to the top with the fewest collection changes, rather than rebuilding: a Reset
    /// makes a bound ComboBox drop its selection, which an editable one can carry into its text.</summary>
    private void Remember(string text)
    {
        _recent = _recent.With(text);
        KeepingText(() =>
        {
            if (RecentEntries.FirstOrDefault(e => e.Equals(text, StringComparison.OrdinalIgnoreCase)) is { } older)
                RecentEntries.Remove(older);
            RecentEntries.Insert(0, text);
            while (RecentEntries.Count > RecentHosts.Max) RecentEntries.RemoveAt(RecentEntries.Count - 1);
        });
        // TrySave, as the tag registry's reconciliation does: an unwritable history costs retyping, not the picker.
        _recentHosts?.TrySave(_recent);
    }

    /// <summary>The × on a drop-down entry, and Delete on a highlighted one.</summary>
    [RelayCommand]
    private void RemoveRecentHost(string? entry)
    {
        if (entry is null) return;
        _recent = _recent.Without(entry);
        KeepingText(() =>
        {
            if (RecentEntries.FirstOrDefault(e => e.Equals(entry, StringComparison.OrdinalIgnoreCase)) is { } shown)
                RecentEntries.Remove(shown);
        });
        _recentHosts?.TrySave(_recent);
    }

    /// <summary>Changes the drop-down's entries without changing the box. Typing a remembered host makes the
    /// editable ComboBox select it, removing a selected item empties that ComboBox's text, and the two-way binding
    /// writes the empty text here.</summary>
    private void KeepingText(Action change)
    {
        var text = QuickConnectText;
        change();
        QuickConnectText = text;
    }

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
            var fromStore = Profiles.Any(p => ReferenceEquals(p, profile));
            _openSession(profile, fromStore);
            // Ad hoc hosts only: the list already recalls a saved profile, and a renamed one would leave a stale entry.
            if (!fromStore) Remember(text);
            return;
        }

        QuickConnectError = parsed.Error is null
            ? $"\"{text}\" is not a saved session, and does not look like a host. Add a port to connect to it as a host, for example {text}:23."
            : "Type host, host:port, or L:host for TLS. An IPv6 address goes in brackets.";
    }
}
