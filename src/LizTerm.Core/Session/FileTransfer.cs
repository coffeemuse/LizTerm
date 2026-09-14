// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

namespace LizTerm.Core.Session;

public enum TransferDirection { Send, Receive }

/// <summary>The host a transfer talks to. <see cref="Ispf"/> is TSO reached from an ISPF command line: the command
/// goes to TSO through ISPF, so everything TSO allows applies. It is last so the other members keep their values.</summary>
public enum TransferHostType { Tso, Vm, Cics, Ispf }

public static class TransferHostTypes
{
    /// <summary>True where IND$FILE runs under TSO: TSO itself, and ISPF, which hands the command to TSO. The one
    /// place that says so, so no caller spells out both members.</summary>
    public static bool IsTso(this TransferHostType type) => type is TransferHostType.Tso or TransferHostType.Ispf;
}

public enum TransferMode { Text, Binary }

/// <summary>Record format of a host file created by a send. Undefined applies to TSO only.</summary>
public enum RecordFormat { Default, Fixed, Variable, Undefined }

/// <summary>Units for the TSO space allocation of a host file created by a send.</summary>
public enum AllocationUnits { Default, Tracks, Cylinders, AvBlock }

/// <summary>One IND$FILE transfer. Fields that do not apply to the direction, mode, or host type are ignored
/// by the backend rather than rejected, so a dialog can keep them filled while the user switches.</summary>
public sealed record FileTransferRequest
{
    public const int MinBufferSize = 256;
    public const int MaxBufferSize = 32768;

    public required TransferDirection Direction { get; init; }
    public required string LocalPath { get; init; }
    public required string HostFile { get; init; }
    public TransferHostType HostType { get; init; } = TransferHostType.Tso;
    public TransferMode Mode { get; init; } = TransferMode.Text;
    /// <summary>Text mode only: strip newlines when sending, add them when receiving.</summary>
    public bool CrLf { get; init; } = true;
    /// <summary>Text mode only: remap between the workstation encoding and the host code page.</summary>
    public bool Remap { get; init; } = true;
    /// <summary>Append to the destination instead of replacing it.</summary>
    public bool Append { get; init; }
    // Sending only. Record format applies to TSO and VM; the rest to TSO only.
    public RecordFormat RecordFormat { get; init; } = RecordFormat.Default;
    public int? Lrecl { get; init; }
    public int? Blksize { get; init; }
    public AllocationUnits AllocationUnits { get; init; } = AllocationUnits.Default;
    public int? PrimarySpace { get; init; }
    public int? SecondarySpace { get; init; }
    public int? AverageBlock { get; init; }
    /// <summary>DFT buffer size, 256 to 32768; null lets the engine choose.</summary>
    public int? BufferSize { get; init; }
    /// <summary>Appended verbatim to the host's IND$FILE command, for ports with extra keywords.</summary>
    public string? ExtraOptions { get; init; }

    /// <summary>A user-facing message, or null when the request can be attempted. Checks only what would make
    /// the engine or host reject the request outright; inapplicable fields are not errors.</summary>
    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(LocalPath)) return "Choose a local file.";
        if (string.IsNullOrWhiteSpace(HostFile)) return "Enter the host file name.";
        if (Lrecl is <= 0) return "LRECL must be a positive number.";
        if (Blksize is <= 0) return "BLKSIZE must be a positive number.";
        if (PrimarySpace is <= 0) return "Primary space must be a positive number.";
        if (SecondarySpace is <= 0) return "Secondary space must be a positive number.";
        if (AverageBlock is <= 0) return "Average block size must be a positive number.";
        if (BufferSize is < MinBufferSize or > MaxBufferSize) return $"Buffer size must be between {MinBufferSize} and {MaxBufferSize}.";
        var tsoSend = Direction == TransferDirection.Send && HostType.IsTso();
        if (tsoSend && AllocationUnits != AllocationUnits.Default && PrimarySpace is null)
            return "Primary space is required when allocation units are set.";
        if (tsoSend && AllocationUnits == AllocationUnits.AvBlock && AverageBlock is null)
            return "Average block size is required for AVBLOCK allocation.";
        return null;
    }
}

/// <summary>Outcome of one transfer. Message is the engine's or host's final text, unaltered, on success and on
/// failure (a cancel included), with one exception: a backend may fail a request the engine cannot perform, in its
/// own words, without sending it. The byte count travels through the progress callback only.</summary>
public sealed record FileTransferResult(bool Succeeded, string Message);
