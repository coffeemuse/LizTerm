// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

namespace LizTerm.Core.HostFiles;

/// <summary>How much of a list to ask for. <paramref name="MaxItems"/> 0 asks for the whole list;
/// <paramref name="Continuation"/> is the value an earlier <see cref="HostFileListing"/> handed back and is opaque
/// to the caller; <paramref name="NamePattern"/> narrows a member list to names matching the host's own wildcard
/// syntax (<c>*</c> for any run of characters, <c>%</c> for one) and is ignored by a dataset list, whose pattern is
/// its own argument.</summary>
public sealed record HostListRequest(int MaxItems = 0, string? Continuation = null, string? NamePattern = null)
{
    /// <summary>The whole list in one answer.</summary>
    public static readonly HostListRequest All = new();
}

/// <summary>One page of a listing. A null <paramref name="Continuation"/> means there is nothing to continue with;
/// otherwise passing it back in the next <see cref="HostListRequest"/> fetches the entries after these.
/// <paramref name="Truncated"/> says the host cut the list short and offers no way to continue it (a directory
/// listing); it is never set on a dataset or member listing.</summary>
public sealed record HostFileListing(IReadOnlyList<HostFileEntry> Entries, string? Continuation, bool Truncated = false)
{
    public bool IsComplete => Continuation is null && !Truncated;
}
