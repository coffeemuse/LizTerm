// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using LizTerm.Core.HostFiles;

namespace LizTerm.App.ViewModels;

/// <summary>One subdirectory of the current directory (USS spec §4.6). <see cref="Path"/> is null for a name the
/// rules refuse (a host can list anything); such a row is shown, dimmed and marked, and cannot be opened or deleted.
/// The words carry the meaning, not the dimming.</summary>
public sealed class DirectoryRow(HostPath parent, HostFileEntry entry)
{
    public string Name { get; } = entry.Name;
    public HostPath? Path { get; } = UssBrowserViewModel.ChildOrNull(parent, entry.Name);
    public string Modified { get; } = UssBrowserViewModel.FormatModified(entry.Unix?.Modified);
    public bool IsUsable => Path is not null;
    public string DisplayName => IsUsable ? Name : $"{Name} (not usable)";
}

/// <summary>One entry of the current directory that is not a subdirectory. A regular file can be downloaded,
/// uploaded over, viewed and deleted; an entry of another kind (<see cref="HostFileEntryKind.Other"/>: a link, a
/// device) or one whose name the rules refuse is listed, dimmed and marked, and the verbs stay off for it.
/// <see cref="Status"/> is the per-file result of the last batch, always a mark and words.</summary>
public sealed partial class FileRow : ObservableObject
{
    public FileRow(HostPath parent, HostFileEntry entry)
    {
        Name = entry.Name;
        Path = UssBrowserViewModel.ChildOrNull(parent, entry.Name);
        IsFile = entry.Kind == HostFileEntryKind.File && Path is not null;
        Size = entry.Unix is { } unix ? unix.Size.ToString("N0", CultureInfo.InvariantCulture) : "";
        Modified = UssBrowserViewModel.FormatModified(entry.Unix?.Modified);
        DisplayName = entry.Kind != HostFileEntryKind.File ? $"{Name} (not a file)"
            : Path is null ? $"{Name} (not usable)"
            : Name;
    }

    public string Name { get; }
    public HostPath? Path { get; }
    public bool IsFile { get; }
    public string Size { get; }
    public string Modified { get; }
    public string DisplayName { get; }

    [ObservableProperty] private string _status = "";
}
