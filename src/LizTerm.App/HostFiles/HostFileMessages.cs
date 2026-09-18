// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.Core.HostFiles;

namespace LizTerm.App.HostFiles;

/// <summary>What the browser and the profile editor say about a failure (spec §4). The backend's own message is
/// used where it carries the detail the user needs: which sign-in failed, what could not be reached.</summary>
public static class HostFileMessages
{
    public static string Describe(Exception ex) => ex switch
    {
        HostFileException host => host.Kind switch
        {
            HostFileErrorKind.NotFound => "Not found.",
            HostFileErrorKind.CannotOpen => "Not found, not authorized, or cannot be opened.",
            HostFileErrorKind.NotAuthorized => "Not authorized.",
            HostFileErrorKind.InvalidRequest => host.ServerMessage is { Length: > 0 } said
                ? $"The host refused the request: {said}"
                : "The host refused the request.",
            HostFileErrorKind.Unauthenticated => host.Message,
            HostFileErrorKind.CertificateRejected => "The host's certificate is not trusted.",
            HostFileErrorKind.Unreachable => host.Message,
            _ => (host.ServerMessage?.Trim().TrimEnd('.') is { Length: > 0 } said ? $"Server error: {said}" : "Server error")
                + (host.Reason is { } reason ? $" (reason {reason})." : "."),
        },
        IOException or UnauthorizedAccessException => $"Local file: {ex.Message}",
        OperationCanceledException => "Cancelled.",
        _ => ex.Message,
    };

    /// <summary>The host does not roll back a failed write (spec §5.3), so a failure that can happen mid-write says so.</summary>
    public static string DescribeUploadFailure(Exception ex) => DescribeUploadFailure(ex, dataset: false);

    /// <summary>As <see cref="DescribeUploadFailure(Exception)"/>; <paramref name="dataset"/> names a sequential
    /// dataset, rather than a member, as what may be partly written.</summary>
    public static string DescribeUploadFailure(Exception ex, bool dataset) =>
        ex is HostFileException { Kind: HostFileErrorKind.ServerError or HostFileErrorKind.Unreachable }
            ? Describe(ex) + (dataset ? " The dataset may be partly written." : " The member may be partly written.")
            : Describe(ex);
}
