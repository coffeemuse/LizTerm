// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

namespace LizTerm.Core.HostFiles;

public enum DatasetOrganization { Sequential, Partitioned }

public enum SpaceUnit { Tracks, Cylinders }

/// <summary>The fields of a <see cref="DatasetAllocation"/> that can fail its local rules, so a form can mark each.</summary>
public enum AllocationField { Recfm, Lrecl, Blksize, Primary, Secondary, DirectoryBlocks }

/// <summary>What <see cref="IHostFileService.CreateDatasetAsync"/> sends. <see cref="Problems"/> holds the local
/// rules: the ranges a host cannot take at all. The DCB combinations a host may still refuse (a block size that
/// is not a multiple of a fixed record length, say) are the host's to judge.</summary>
public sealed record DatasetAllocation(
    DatasetOrganization Organization,
    string Recfm,
    int Lrecl,
    int Blksize,
    SpaceUnit Unit,
    int Primary,
    int Secondary,
    int DirectoryBlocks)
{
    public const int MaxRecordLength = 32760;

    /// <summary>The record format as it is sent: trimmed, upper case.</summary>
    public string FoldedRecfm => (Recfm ?? "").Trim().ToUpperInvariant();

    /// <summary>RECFM=U, whose LRECL may be 0.</summary>
    public bool IsUndefinedLength => FoldedRecfm.StartsWith('U');

    public bool IsValid => Problems().Count == 0;

    /// <summary>One sentence per field that fails a local rule; empty when every field passes.</summary>
    public IReadOnlyDictionary<AllocationField, string> Problems()
    {
        var problems = new Dictionary<AllocationField, string>();
        if (RecfmError(Recfm) is { } recfm) problems[AllocationField.Recfm] = recfm;
        if (IsUndefinedLength)
        {
            if (Lrecl < 0 || Lrecl > MaxRecordLength)
                problems[AllocationField.Lrecl] = $"LRECL must be between 0 and {MaxRecordLength} for undefined-length records.";
        }
        else if (Lrecl < 1 || Lrecl > MaxRecordLength)
        {
            problems[AllocationField.Lrecl] = $"LRECL must be between 1 and {MaxRecordLength}.";
        }
        if (Blksize < 1 || Blksize > MaxRecordLength) problems[AllocationField.Blksize] = $"BLKSIZE must be between 1 and {MaxRecordLength}.";
        if (Primary < 1) problems[AllocationField.Primary] = "Primary space must be at least 1.";
        if (Secondary < 0) problems[AllocationField.Secondary] = "Secondary space cannot be negative.";
        if (Organization == DatasetOrganization.Partitioned && DirectoryBlocks < 1)
            problems[AllocationField.DirectoryBlocks] = "A partitioned dataset needs at least 1 directory block.";
        return problems;
    }

    /// <summary>Why <paramref name="recfm"/> is not a record format, or null: a first letter of F, V or U, then any
    /// of B, S, A and M, each at most once.</summary>
    public static string? RecfmError(string recfm)
    {
        var folded = (recfm ?? "").Trim().ToUpperInvariant();
        if (folded.Length == 0) return "Enter a record format.";
        if (folded[0] is not ('F' or 'V' or 'U')) return "A record format starts with F, V or U.";
        var seen = new HashSet<char>();
        foreach (var c in folded.AsSpan(1))
        {
            if (c is not ('B' or 'S' or 'A' or 'M')) return $"A record format cannot contain '{c}'.";
            if (!seen.Add(c)) return $"A record format cannot repeat '{c}'.";
        }
        return null;
    }
}
