// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using LizTerm.Core.HostFiles;

namespace LizTerm.App.ViewModels;

/// <summary>One dataset in the browser's left pane. A name the naming rules refuse (a host can list anything) is
/// treated like a dataset organisation the preview cannot open.</summary>
public sealed class DatasetRow(HostFileEntry entry)
{
    private static readonly DatasetAttributes Unknown = new(null, null, null, null, null);

    public string Name => entry.Name;
    public DatasetAttributes Attributes => entry.Attributes ?? Unknown;
    public string Dsorg => Attributes.Dsorg ?? "";
    public string Recfm => Attributes.Recfm ?? "";
    public string Lrecl => Attributes.Lrecl?.ToString(CultureInfo.InvariantCulture) ?? "";
    public bool IsSupported => Attributes.IsSupported && HostPath.DatasetNameError(Name) is null;
    public bool IsPartitioned => IsSupported && Attributes.IsPartitioned;
    public bool IsSequential => IsSupported && Attributes.IsSequential;
    /// <summary>Dimmed in the list as well; the words carry the meaning, not the dimming.</summary>
    public string DisplayName => IsSupported ? Name : $"{Name} (not supported)";
    /// <summary>Only for a supported row.</summary>
    public HostPath Path => HostPath.ForDataset(Name);
}

/// <summary>One member in the right pane. <see cref="Status"/> is the per-member result of the last batch, always
/// a mark and words.</summary>
public sealed partial class MemberRow(string dataset, string name) : ObservableObject
{
    public string Name { get; } = name;
    public HostPath Path { get; } = HostPath.ForMember(dataset, name);

    [ObservableProperty] private string _status = "";
}
