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

    /// <summary>The datasets matching <paramref name="pattern"/> (see <see cref="HostPath.DatasetPatternError"/>),
    /// in the host's order, as much of them as <paramref name="request"/> asks for.</summary>
    Task<HostFileListing> ListDatasetsAsync(string pattern, HostListRequest request, CancellationToken cancellationToken = default);

    /// <summary>The members of a partitioned dataset, as much of them as <paramref name="request"/> asks for. A
    /// dataset that does not exist is <see cref="HostFileErrorKind.NotFound"/>; one that is not partitioned is
    /// <see cref="HostFileErrorKind.InvalidRequest"/>.</summary>
    Task<HostFileListing> ListMembersAsync(HostPath dataset, HostListRequest request, CancellationToken cancellationToken = default);

    /// <summary>The records as lines, trailing blanks as the host sent them. With <paramref name="withEtag"/>, the
    /// host's stamp of the content comes too, when it gives one; a stamp can cost the host a second pass over the
    /// content, so only a read whose caller may write back should ask. <paramref name="progress"/> reports bytes
    /// received.</summary>
    Task<HostTextRead> ReadTextAsync(HostPath path, IProgress<long>? progress = null, bool withEtag = false, CancellationToken cancellationToken = default);

    /// <summary>Copies the record bytes to <paramref name="destination"/>; returns the byte count and, with
    /// <paramref name="withEtag"/>, the stamp, as <see cref="ReadTextAsync"/>.</summary>
    Task<HostBinaryRead> ReadBinaryAsync(HostPath path, Stream destination, IProgress<long>? progress = null, bool withEtag = false, CancellationToken cancellationToken = default);

    /// <summary>Replaces the dataset's or member's records, creating a member that does not exist. Every line must
    /// already have passed <see cref="TextUploadCheck"/>. With <paramref name="ifMatch"/>, the stamp an earlier
    /// read or write returned, the write happens only while the target still holds that content; otherwise
    /// <see cref="HostFileErrorKind.Conflict"/> and nothing is written. Returns the stamp of the target as written,
    /// null when the host gives none. A failure without a conflict may leave the target partly written.</summary>
    Task<string?> WriteTextAsync(HostPath path, IReadOnlyList<string> lines, string? ifMatch = null, CancellationToken cancellationToken = default);

    /// <summary>As <see cref="WriteTextAsync"/>, for bytes.</summary>
    Task<string?> WriteBinaryAsync(HostPath path, Stream source, string? ifMatch = null, CancellationToken cancellationToken = default);

    /// <summary>Allocates a new dataset. <paramref name="allocation"/> must pass its own <see cref="DatasetAllocation.Problems"/>.
    /// A name the host could not allocate, for whatever reason it can name, is <see cref="HostFileErrorKind.CannotAllocate"/>.</summary>
    Task CreateDatasetAsync(HostPath dataset, DatasetAllocation allocation, CancellationToken cancellationToken = default);

    /// <summary>Renames a member within its library (<paramref name="newName"/> is the new member name) or a
    /// dataset (<paramref name="newName"/> is the new dataset name). A missing source is
    /// <see cref="HostFileErrorKind.NotFound"/>; a member name already in use is <see cref="HostFileErrorKind.AlreadyExists"/>.</summary>
    /// <exception cref="ArgumentException"><paramref name="newName"/> fails the naming rules.</exception>
    Task RenameAsync(HostPath from, string newName, CancellationToken cancellationToken = default);

    /// <summary>Deletes a member, or a whole dataset with everything in it.</summary>
    Task DeleteAsync(HostPath path, CancellationToken cancellationToken = default);

    /// <summary>Ends the session the token names. Best effort: a host that has already forgotten the token is a
    /// success. The default does nothing, for a service that holds no session.</summary>
    Task SignOutAsync(HostSessionToken token, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
