// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using LizTerm.Core.HostFiles;

namespace LizTerm.App.ViewModels;

/// <summary>The New dataset form (spec §4.4): every field a string bound to a text box, two radio pairs, and a
/// problem line per field from <see cref="HostPath.DatasetNameError"/> and <see cref="DatasetAllocation.Problems"/>.
/// One instance lives as long as the browser window, so the space values it was last sent with are kept.</summary>
public sealed partial class NewDatasetFormViewModel : ObservableObject
{
    private static readonly string[] Derived =
    [
        nameof(IsSequential), nameof(IsCylinders), nameof(NameProblem), nameof(RecfmProblem), nameof(LreclProblem),
        nameof(BlksizeProblem), nameof(PrimaryProblem), nameof(SecondaryProblem), nameof(DirectoryBlocksProblem), nameof(CanCreate),
    ];

    [ObservableProperty] private string _name = "";
    [ObservableProperty] private bool _isPartitioned = true;
    [ObservableProperty] private string _recfm = "FB";
    [ObservableProperty] private string _lrecl = "80";
    [ObservableProperty] private string _blksize = "3120";
    [ObservableProperty] private bool _isTracks = true;
    [ObservableProperty] private string _primary = "5";
    [ObservableProperty] private string _secondary = "5";
    [ObservableProperty] private string _directoryBlocks = "20";

    /// <summary>The host's answer to the last Create, with its mark, or null. Not a field: it does not recheck.</summary>
    [ObservableProperty] private string? _message;

    public bool IsSequential
    {
        get => !IsPartitioned;
        set { if (value) IsPartitioned = false; }
    }

    public bool IsCylinders
    {
        get => !IsTracks;
        set { if (value) IsTracks = false; }
    }

    /// <summary>Type, RECFM, LRECL and BLKSIZE from a listed dataset, for a new one like it; a field the listing left
    /// out keeps its value.</summary>
    public void PrefillFrom(DatasetAttributes attributes)
    {
        if (attributes.IsPartitioned) IsPartitioned = true;
        else if (attributes.IsSequential) IsPartitioned = false;
        if (attributes.Recfm is { Length: > 0 } recfm) Recfm = recfm;
        if (attributes.Lrecl is { } lrecl) Lrecl = lrecl.ToString(CultureInfo.InvariantCulture);
        if (attributes.Blksize is { } blksize) Blksize = blksize.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>What Create sends. A numeric field that is not a whole number is sent as -1, which every rule
    /// refuses, and its own problem line wins (<see cref="NumberProblem"/>).</summary>
    public DatasetAllocation Allocation => new(
        IsPartitioned ? DatasetOrganization.Partitioned : DatasetOrganization.Sequential,
        Recfm, Number(Lrecl), Number(Blksize), IsTracks ? SpaceUnit.Tracks : SpaceUnit.Cylinders,
        Number(Primary), Number(Secondary), Number(DirectoryBlocks));

    public string? NameProblem => Mark(HostPath.DatasetNameError(Name));
    public string? RecfmProblem => Mark(Problems.GetValueOrDefault(AllocationField.Recfm));
    public string? LreclProblem => Mark(NumberProblem(Lrecl) ?? Problems.GetValueOrDefault(AllocationField.Lrecl));
    public string? BlksizeProblem => Mark(NumberProblem(Blksize) ?? Problems.GetValueOrDefault(AllocationField.Blksize));
    public string? PrimaryProblem => Mark(NumberProblem(Primary) ?? Problems.GetValueOrDefault(AllocationField.Primary));
    public string? SecondaryProblem => Mark(NumberProblem(Secondary) ?? Problems.GetValueOrDefault(AllocationField.Secondary));
    public string? DirectoryBlocksProblem =>
        IsPartitioned ? Mark(NumberProblem(DirectoryBlocks) ?? Problems.GetValueOrDefault(AllocationField.DirectoryBlocks)) : null;

    public bool CanCreate =>
        NameProblem is null && RecfmProblem is null && LreclProblem is null && BlksizeProblem is null
        && PrimaryProblem is null && SecondaryProblem is null && DirectoryBlocksProblem is null;

    private IReadOnlyDictionary<AllocationField, string> Problems => Allocation.Problems();

    private static string? Mark(string? problem) => problem is null ? null : "✗ " + problem;

    private static int Number(string text) => TryNumber(text, out var value) ? value : -1;

    private static string? NumberProblem(string text) => TryNumber(text, out _) ? null : "Enter a whole number.";

    /// <summary>Digits only: no sign, no blanks inside, no grouping.</summary>
    private static bool TryNumber(string text, out int value) =>
        int.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out value);

    /// <summary>Every field feeds several problems (RECFM decides LRECL's range, the type decides whether directory
    /// blocks count), so a change of any field raises them all.</summary>
    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is null || e.PropertyName == nameof(Message) || Derived.Contains(e.PropertyName)) return;
        foreach (var name in Derived) base.OnPropertyChanged(new PropertyChangedEventArgs(name));
    }
}
