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
        var from = row.Name;
        var target = TagSet.Normalize(NameText);
        var merge = !target.Equals(from, StringComparison.OrdinalIgnoreCase) && _snapshot.Registry.Contains(target);
        if (!merge)
        {
            RunRename(from, target);
            return;
        }

        var shownFrom = from.ToUpperInvariant();
        var shownTo = target.ToUpperInvariant();
        Ask(row.IsUnused
                ? $"{shownTo} already exists. Merge {shownFrom} into it? No profile uses {shownFrom}, so only its colour is dropped."
                : $"{shownTo} already exists. Merge {shownFrom} into it? {ProfileList(row.UsedBy)} will carry {shownTo} instead, and {shownFrom}'s colour is dropped.",
            "Merge", () => RunRename(from, target));
    }

    [RelayCommand(CanExecute = nameof(IsTagSelected))]
    private void Delete()
    {
        if (SelectedRow is not { IsReserved: false } row) return;
        var name = row.Name;
        var shown = name.ToUpperInvariant();
        Ask(row.IsUnused ? $"Delete {shown}? No profile uses it." : $"Delete {shown}? It is removed from {ProfileList(row.UsedBy)}.",
            "Delete", () => RunDelete(name));
    }

    [RelayCommand(CanExecute = nameof(IsTagSelected))]
    private void Recolour(TagColor color)
    {
        if (SelectedRow is not { IsReserved: false } row) return;
        CancelPending();
        var name = row.Name;
        _maintenance.Recolour(name, color);
        Refresh(name);
        StatusMessage = null;
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

    private void RunRename(string from, string target)
    {
        CancelPending();
        _maintenance.Rename(from, target);
        Refresh(target, from);
        StatusMessage = null;
    }

    private void RunDelete(string name)
    {
        CancelPending();
        _maintenance.Delete(name);
        Refresh(name);
        StatusMessage = null;
    }

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

    /// <summary>Reads everything again and selects the first of <paramref name="preferred"/> still listed, or
    /// nothing.</summary>
    private void Refresh(params string[] preferred)
    {
        _snapshot = _maintenance.Load();
        Rebuild(preferred);
    }

    private void Rebuild(params string[] preferred)
    {
        Rows.Clear();
        foreach (var definition in _snapshot.Registry.All)
        {
            var usedBy = _snapshot.Profiles.Where(p => p.Tags.Contains(definition.Name)).Select(p => p.Name).ToList();
            Rows.Add(new TagListRow(definition, usedBy));
        }
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
