// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LizTerm.App.Rendering;
using LizTerm.Core.Profiles;
using LizTerm.Core.Session;

namespace LizTerm.App.ViewModels;

/// <summary>Manage Tags (#88): every tag definition in a list, and the selected one's name, colour and profiles
/// beside it, where it can be renamed, merged, recoloured or deleted (spec 6). Every action runs through
/// TagMaintenance and applies at once; a merge or a delete asks first. UI thread only.</summary>
public partial class ManageTagsViewModel : ObservableObject
{
    private readonly TagMaintenance _maintenance;
    private TagSnapshot _snapshot;
    private Action? _confirmed;

    public ManageTagsViewModel(TagMaintenance maintenance)
    {
        _maintenance = maintenance;
        _snapshot = maintenance.Load();
        Rebuild();
    }

    public ObservableCollection<TagListRow> Rows { get; } = [];

    /// <summary>The seven assignable colours for the selected tag; empty for none, or for the reserved tag.</summary>
    public ObservableCollection<SwatchOption> Swatches { get; } = [];

    /// <summary>Nullable because it really is null at times: clearing Rows makes a bound ListBox null its selection,
    /// and the two-way binding writes that back (src/LizTerm.App/CLAUDE.md, "The session picker's tags"). So every
    /// refresh is told the name to select BEFORE it clears, and reselects by name.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection), nameof(IsTagSelected), nameof(IsReservedSelected), nameof(CanRename),
        nameof(ShowDeleteButton))]
    [NotifyCanExecuteChangedFor(nameof(RenameCommand), nameof(DeleteCommand), nameof(RecolourCommand))]
    private TagListRow? _selectedRow;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRename))]
    [NotifyCanExecuteChangedFor(nameof(RenameCommand))]
    private string _nameText = "";

    [ObservableProperty] private string? _validationMessage;

    /// <summary>The question the panel is waiting on, or null. What a yes runs is kept beside it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPendingConfirmation), nameof(ShowDeleteButton))]
    private string? _pendingConfirmation;

    [ObservableProperty] private string _confirmLabel = "";

    /// <summary>The last action's failure, or null. Survives a change of selection; the next action replaces it.</summary>
    [ObservableProperty] private string? _statusMessage;

    public bool HasSelection => SelectedRow is not null;

    public bool IsTagSelected => SelectedRow is { IsReserved: false };

    public bool IsReservedSelected => SelectedRow is { IsReserved: true };

    public bool HasPendingConfirmation => PendingConfirmation is not null;

    /// <summary>The confirmation strip takes the Delete button's place while it is up.</summary>
    public bool ShowDeleteButton => IsTagSelected && !HasPendingConfirmation;

    /// <summary>A valid name that differs from the stored one BY ORDINAL COMPARISON, so a case-only change counts.</summary>
    public bool CanRename =>
        SelectedRow is { IsReserved: false } row
        && TagMaintenance.RenameProblem(NameText) is null
        && !TagSet.Normalize(NameText).Equals(row.Name, StringComparison.Ordinal);

    partial void OnSelectedRowChanged(TagListRow? value)
    {
        CancelPending();
        NameText = value?.Name ?? "";
        ValidationMessage = null;
        Swatches.Clear();
        if (value is not { IsReserved: false }) return;
        foreach (var color in TagRegistry.AssignableColors)
            Swatches.Add(new SwatchOption(color, TagPalette.Brush(color), color == value.Color));
    }

    partial void OnNameTextChanged(string value)
    {
        CancelPending();
        ValidationMessage = SelectedRow is { IsReserved: false } row
                            && !TagSet.Normalize(value).Equals(row.Name, StringComparison.Ordinal)
            ? TagMaintenance.RenameProblem(value)
            : null;
    }

    [RelayCommand(CanExecute = nameof(CanRename))]
    private void Rename()
    {
        if (SelectedRow is not { IsReserved: false } row || !CanRename) return;
        // Read fresh rather than trust the cached snapshot: another window (File > Save as Profile..., spec 7.1)
        // may have defined a tag or saved a carrier while this dialog was open, and a merge cannot be undone
        // (spec 2.3), so whether this is one, and which profiles it names, come from the files as they are now. A
        // local, not _snapshot: Rows is not rebuilt here -- that would reset the selection and the name the user
        // just typed -- and _snapshot is what Rows were built from.
        var now = _maintenance.Load();
        var from = row.Name;
        var target = TagSet.Normalize(NameText);
        var merge = !target.Equals(from, StringComparison.OrdinalIgnoreCase) && now.Registry.Contains(target);
        if (!merge)
        {
            RunRename(from, target, merge: false);
            return;
        }

        var shownFrom = from.ToUpperInvariant();
        var shownTo = target.ToUpperInvariant();
        var carriers = Carriers(now, from);
        Ask(carriers.Count == 0
                ? $"{shownTo} already exists. Merge {shownFrom} into it? No profile uses {shownFrom}, so only its colour is dropped."
                : $"{shownTo} already exists. Merge {shownFrom} into it? {ProfileList(carriers)} will carry {shownTo} instead, and {shownFrom}'s colour is dropped.",
            "Merge", () => RunRename(from, target, merge: true));
    }

    [RelayCommand(CanExecute = nameof(IsTagSelected))]
    private void Delete()
    {
        if (SelectedRow is not { IsReserved: false } row) return;
        var name = row.Name;
        var shown = name.ToUpperInvariant();
        // Fresh for the same reason Rename reads fresh: the question names what the delete will touch.
        var carriers = Carriers(_maintenance.Load(), name);
        Ask(carriers.Count == 0 ? $"Delete {shown}? No profile uses it." : $"Delete {shown}? It is removed from {ProfileList(carriers)}.",
            "Delete", () => RunDelete(name));
    }

    private static List<string> Carriers(TagSnapshot snapshot, string tag) =>
        snapshot.Profiles.Where(p => p.Tags.Contains(tag)).Select(p => p.Name).ToList();

    [RelayCommand(CanExecute = nameof(IsTagSelected))]
    private void Recolour(TagColor color)
    {
        if (SelectedRow is not { IsReserved: false } row) return;
        CancelPending();
        var name = row.Name;
        var result = _maintenance.Recolour(name, color);
        Refresh(result, name);
        StatusMessage = result.Succeeded ? null : $"Could not save the colour: {result.Error}";
    }

    [RelayCommand]
    private void Confirm()
    {
        var run = _confirmed;
        CancelPending();
        run?.Invoke();
    }

    [RelayCommand]
    private void CancelConfirmation() => CancelPending();

    private void RunRename(string from, string target, bool merge)
    {
        CancelPending();
        var result = _maintenance.Rename(from, target);
        // A rename that stopped partway through a profile says "Rename FROM to TARGET again to finish" (spec
        // 6.1), which needs FROM selected -- so prefer it there (controller ruling, F7). Every other outcome
        // (success, or a tags.json-only failure) keeps preferring the new name, as before.
        if (result.FailedProfile is not null) Refresh(result, from, target);
        else Refresh(result, target, from);
        StatusMessage = RenameStatus(result, from, target, merge);
    }

    private void RunDelete(string name)
    {
        CancelPending();
        var result = _maintenance.Delete(name);
        Refresh(result, name);
        var shown = name.ToUpperInvariant();
        StatusMessage = result switch
        {
            { Succeeded: true } => null,
            { FailedProfile: { } failed } =>
                $"Removed from {result.Changed.Count} of {result.Carriers} profiles. Could not write {failed}: {result.Error}\nDelete {shown} again to finish.",
            _ => $"{RegistryNotSaved(result)}\n{shown} may still be listed.",
        };
    }

    /// <summary>Spec 6.2. Tag names are uppercased as the chips are, except in a case-only rename, whose casing is the
    /// whole change — "Rename DEV to DEV" would say nothing.</summary>
    private static string? RenameStatus(TagChangeResult result, string from, string target, bool merge)
    {
        if (result.Succeeded) return null;
        var caseOnly = from.Equals(target, StringComparison.OrdinalIgnoreCase);
        var shownFrom = caseOnly ? from : from.ToUpperInvariant();
        var shownTo = caseOnly ? target : target.ToUpperInvariant();
        if (result.FailedProfile is { } failed)
            return $"Renamed on {result.Changed.Count} of {result.Carriers} profiles. Could not write {failed}: {result.Error}\nRename {shownFrom} to {shownTo} again to finish.";
        if (caseOnly) return RegistryNotSaved(result);
        // A plain rename prepares tags.json before it touches a profile, so a registry failure with carriers left
        // unwritten stopped it before anything changed. Otherwise every carrier was written and both names are
        // still defined, the same state a merge's failed final save leaves.
        if (!merge && result.Changed.Count < result.Carriers)
            return $"Nothing was renamed: tags.json could not be saved: {result.Error}";
        return $"{RegistryNotSaved(result)}\n{shownFrom} may still be listed.";
    }

    private static string RegistryNotSaved(TagChangeResult result) =>
        $"Every profile was updated, but tags.json could not be saved: {result.Error}";

    private void Ask(string message, string confirmLabel, Action run)
    {
        ConfirmLabel = confirmLabel;
        _confirmed = run;
        PendingConfirmation = message;
    }

    private void CancelPending()
    {
        _confirmed = null;
        PendingConfirmation = null;
    }

    /// <summary>Redraws from the state the action left on disk -- or reads everything again when it had nothing to
    /// do -- and selects the first of <paramref name="preferred"/> still listed, or nothing.</summary>
    private void Refresh(TagChangeResult result, params string[] preferred)
    {
        _snapshot = result.After ?? _maintenance.Load();
        Rebuild(preferred);
    }

    private void Rebuild(params string[] preferred)
    {
        Rows.Clear();
        foreach (var definition in _snapshot.Registry.All)
            Rows.Add(new TagListRow(definition, Carriers(_snapshot, definition.Name)));
        SelectedRow = preferred
            .Select(name => Rows.FirstOrDefault(r => r.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            .FirstOrDefault(row => row is not null);
    }

    /// <summary>"mvsce", "mvsce and gateway", "gateway, mvsce and tk5", and past three "4 profiles" (spec 6.2).</summary>
    private static string ProfileList(IReadOnlyList<string> names) => names.Count switch
    {
        1 => names[0],
        2 => $"{names[0]} and {names[1]}",
        3 => $"{names[0]}, {names[1]} and {names[2]}",
        _ => $"{names.Count} profiles",
    };
}
