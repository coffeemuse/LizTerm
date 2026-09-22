// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

namespace LizTerm.Core.HostFiles;

public enum HostFileEntryKind { Dataset, Member, Directory, File }

public enum RecordFormatFamily { Unknown, Fixed, Variable, Undefined }

/// <summary>What a directory listing says about an entry: its size in bytes (a directory's is whatever the host
/// reports) and when it last changed, null when the host does not say.</summary>
public sealed record UnixFileAttributes(long Size, DateTimeOffset? Modified);

/// <summary>One row of a host listing. <paramref name="Attributes"/> is set for datasets only; <paramref name="Unix"/>
/// for directories and files only.</summary>
public sealed record HostFileEntry(string Name, HostFileEntryKind Kind, DatasetAttributes? Attributes = null, UnixFileAttributes? Unix = null);

/// <summary>What a dataset listing says about a dataset. Every field is optional because hosts leave fields out.</summary>
public sealed record DatasetAttributes(string? Dsorg, string? Recfm, int? Lrecl, int? Blksize, string? Volume)
{
    public bool IsPartitioned => Dsorg is "PO" or "PO-E";

    public bool IsSequential => Dsorg == "PS";

    /// <summary>Whether this release can list, read or write it. VSAM, direct-access and unknown organisations
    /// cannot be.</summary>
    public bool IsSupported => IsPartitioned || IsSequential;

    public RecordFormatFamily RecordFormat => Recfm is { Length: > 0 } recfm
        ? recfm[0] switch
        {
            'F' => RecordFormatFamily.Fixed,
            'V' => RecordFormatFamily.Variable,
            'U' => RecordFormatFamily.Undefined,
            _ => RecordFormatFamily.Unknown,
        }
        : RecordFormatFamily.Unknown;

    /// <summary>The longest text line one record can hold: LRECL for fixed records, LRECL less the four-byte record
    /// descriptor for variable ones, BLKSIZE for undefined ones. Null when the listing does not say.</summary>
    public int? UsableLineLength => RecordFormat switch
    {
        RecordFormatFamily.Fixed => Lrecl is > 0 ? Lrecl : null,
        RecordFormatFamily.Variable => Lrecl is > 4 ? Lrecl - 4 : null,
        RecordFormatFamily.Undefined => Blksize is > 0 ? Blksize : null,
        _ => null,
    };
}
