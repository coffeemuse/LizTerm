// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

namespace LizTerm.Core.HostFiles;

/// <summary>File access to one host outside the 3270 session. Every call may run concurrently with the others.
/// Host outcomes other than success throw <see cref="HostFileException"/>; a cancelled token throws
/// <see cref="OperationCanceledException"/>. Text crosses this interface as .NET strings, one per record, so the
/// wire encoding is the implementation's business.</summary>
public interface IHostFileService : IDisposable
{
    Task<HostServerInfo> GetServerInfoAsync(CancellationToken cancellationToken = default);

    /// <summary>Every dataset matching <paramref name="pattern"/> (see <see cref="HostPath.DatasetPatternError"/>),
    /// in the host's order.</summary>
    Task<IReadOnlyList<HostFileEntry>> ListDatasetsAsync(string pattern, CancellationToken cancellationToken = default);

    /// <summary>The members of a partitioned dataset. Some hosts answer an empty list for a dataset that does not
    /// exist or is not partitioned, so confirm the dataset from <see cref="ListDatasetsAsync"/>.</summary>
    Task<IReadOnlyList<HostFileEntry>> ListMembersAsync(HostPath dataset, CancellationToken cancellationToken = default);

    /// <summary>The records as lines, trailing blanks as the host sent them. <paramref name="progress"/> reports
    /// bytes received.</summary>
    Task<IReadOnlyList<string>> ReadTextAsync(HostPath path, IProgress<long>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>Copies the record bytes to <paramref name="destination"/>; returns the byte count.</summary>
    Task<long> ReadBinaryAsync(HostPath path, Stream destination, IProgress<long>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>Replaces the dataset's or member's records, creating a member that does not exist. Every line must
    /// already have passed <c>TextUploadCheck</c>. A failure may leave the target partly written.</summary>
    Task WriteTextAsync(HostPath path, IReadOnlyList<string> lines, CancellationToken cancellationToken = default);

    Task WriteBinaryAsync(HostPath path, Stream source, CancellationToken cancellationToken = default);

    /// <summary>Deletes a member. Deleting a whole dataset is not supported in this release.</summary>
    Task DeleteAsync(HostPath path, CancellationToken cancellationToken = default);
}
