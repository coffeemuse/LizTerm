// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.App.HostFiles;
using LizTerm.Core.HostFiles;

namespace LizTerm.App.Tests.HostFiles;

public class HostFileMessagesTests
{
    [Theory]
    [InlineData(HostFileErrorKind.NotFound, null, null, "X(Y): not found.", "Not found.")]
    [InlineData(HostFileErrorKind.CannotOpen, 3, null, "x", "Not found, not authorized, or cannot be opened.")]
    [InlineData(HostFileErrorKind.NotAuthorized, 0, null, "x", "Not authorized.")]
    [InlineData(HostFileErrorKind.Conflict, 10, "The resource was modified since the supplied ETag was created", "A.B(C): changed on the host since it was read.", "Changed on the host since you downloaded it.")]
    [InlineData(HostFileErrorKind.AlreadyExists, 7, "Rename target already exists", "Rename A.B(ONE) to TWO: a member of that name already exists.", "Rename A.B(ONE) to TWO: a member of that name already exists.")]
    [InlineData(HostFileErrorKind.CannotAllocate, 7, "Dynamic allocation Error", "x", "The host could not allocate it: it may already exist, there may be no space, or you may not be authorized.")]
    [InlineData(HostFileErrorKind.InvalidRequest, 1, "Dataset or member name too long", "x", "The host refused the request: Dataset or member name too long")]
    [InlineData(HostFileErrorKind.InvalidRequest, null, null, "x", "The host refused the request.")]
    [InlineData(HostFileErrorKind.Unauthenticated, null, null, "Sign-in was cancelled.", "Sign-in was cancelled.")]
    [InlineData(HostFileErrorKind.CertificateRejected, null, null, "x", "The host's certificate is not trusted.")]
    [InlineData(HostFileErrorKind.Unreachable, null, null, "Dataset list: cannot reach the host (refused).", "Dataset list: cannot reach the host (refused).")]
    [InlineData(HostFileErrorKind.ServerError, 7, null, "x", "Server error (reason 7).")]
    [InlineData(HostFileErrorKind.ServerError, null, null, "x", "Server error.")]
    [InlineData(HostFileErrorKind.ServerError, 3, "Record truncated to the record length of the data set", "x", "Server error: Record truncated to the record length of the data set (reason 3).")]
    [InlineData(HostFileErrorKind.ServerError, null, "Error writing record.", "x", "Server error: Error writing record.")]
    [InlineData(HostFileErrorKind.ServerError, 3, "...", "x", "Server error (reason 3).")]
    public void Describes_each_kind_in_plain_words(HostFileErrorKind kind, int? reason, string? server, string message, string expected) =>
        Assert.Equal(expected, HostFileMessages.Describe(new HostFileException(kind, message, reason, server)));

    [Fact]
    public void Local_and_other_failures_are_described_too()
    {
        Assert.Equal("Local file: disk full", HostFileMessages.Describe(new IOException("disk full")));
        Assert.Equal("Local file: denied", HostFileMessages.Describe(new UnauthorizedAccessException("denied")));
        Assert.Equal("Cancelled.", HostFileMessages.Describe(new OperationCanceledException()));
        Assert.Equal("boom", HostFileMessages.Describe(new InvalidOperationException("boom")));
    }

    [Fact]
    public void An_upload_that_failed_mid_write_warns_about_a_partial_member()
    {
        Assert.Equal("Server error (reason 3). The member may be partly written.",
            HostFileMessages.DescribeUploadFailure(new HostFileException(HostFileErrorKind.ServerError, "x", 3)));
        Assert.Equal("x: gone The member may be partly written.",
            HostFileMessages.DescribeUploadFailure(new HostFileException(HostFileErrorKind.Unreachable, "x: gone")));
        Assert.Equal("Not authorized.",
            HostFileMessages.DescribeUploadFailure(new HostFileException(HostFileErrorKind.NotAuthorized, "x")));
        Assert.Equal("Not found, not authorized, or cannot be opened.",
            HostFileMessages.DescribeUploadFailure(new HostFileException(HostFileErrorKind.CannotOpen, "x", 3, "Cannot open dataset for writing")));
        // A conflict is refused before anything is written, so nothing may be partly written.
        Assert.Equal("Changed on the host since you downloaded it.",
            HostFileMessages.DescribeUploadFailure(new HostFileException(HostFileErrorKind.Conflict, "x", 10)));
    }

    [Fact]
    public void A_sequential_dataset_is_named_as_the_dataset_that_may_be_partly_written()
    {
        Assert.Equal("Server error (reason 3). The dataset may be partly written.",
            HostFileMessages.DescribeUploadFailure(new HostFileException(HostFileErrorKind.ServerError, "x", 3), dataset: true));
        Assert.Equal("Not authorized.",
            HostFileMessages.DescribeUploadFailure(new HostFileException(HostFileErrorKind.NotAuthorized, "x"), dataset: true));
    }
}
