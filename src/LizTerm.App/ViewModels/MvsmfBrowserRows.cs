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

/// <summary>One local file in the upload review. The member name starts as the file name up to its first dot,
/// upper-cased, and stays editable; <see cref="IsBlocked"/> rows are not sent.</summary>
public sealed partial class UploadRow : ObservableObject
{
    public UploadRow(string localPath)
    {
        LocalPath = localPath;
        FileName = System.IO.Path.GetFileName(localPath);
        _memberName = FileName.Split('.')[0].ToUpperInvariant();
    }

    public string LocalPath { get; }
    public string FileName { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NameProblem), nameof(IsBlocked))]
    private string _memberName;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Problems), nameof(IsBlocked))]
    private TextUploadResult? _check;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Problems), nameof(IsBlocked))]
    private string? _readProblem;

    [ObservableProperty] private string _status = "";

    /// <summary>This row reached the host in an earlier run of the same review, so a retry does not send it again,
    /// and its member name is no longer editable.</summary>
    [ObservableProperty] private bool _sent;

    /// <summary>The read-back found the host copy different; the batch summary is a warning then.</summary>
    public bool HostCopyDiffers { get; set; }

    public string UploadName => MemberName.Trim().ToUpperInvariant();

    public string? NameProblem => HostPath.MemberNameError(MemberName) is { } error ? "✗ " + error : null;

    public string Problems
    {
        get
        {
            var lines = new List<string>();
            if (ReadProblem is not null) lines.Add("✗ " + ReadProblem);
            if (Check is not null)
            {
                lines.AddRange(Check.Errors.Select(problem => "✗ " + problem.Message));
                lines.AddRange(Check.Warnings.Select(problem => "⚠ " + problem.Message));
            }
            return string.Join("\n", lines);
        }
    }

    public bool IsBlocked => NameProblem is not null || ReadProblem is not null || Check is { CanUpload: false };
}
