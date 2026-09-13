// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

namespace LizTerm.Core.Profiles;

/// <summary>What was typed into Quick Connect, newest first: trimmed, never blank, unique ignoring case (host names
/// ignore case, and so does the <c>L:</c> prefix), and at most <see cref="Max"/>. Immutable; every change answers a
/// new list. Entries are kept as typed, so recalling one keeps its TLS prefix, LU and port.</summary>
public sealed class RecentHosts
{
    public const int Max = 10;

    public static RecentHosts Empty { get; } = new([]);

    public IReadOnlyList<string> Entries { get; }

    private RecentHosts(IReadOnlyList<string> entries) => Entries = entries;

    /// <summary>Holds any sequence — a file a user may have edited included — to the list's rules. The first
    /// spelling of a duplicate wins, which is the newest.</summary>
    public static RecentHosts From(IEnumerable<string?> entries) =>
        new([.. entries
            .Select(e => e?.Trim())
            .OfType<string>()
            .Where(e => e.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(Max)]);

    /// <summary>The entry at the top, in the spelling given; an older copy of it is dropped rather than kept below.</summary>
    public RecentHosts With(string entry) => From([entry, .. Entries]);

    public RecentHosts Without(string entry) =>
        new([.. Entries.Where(e => !e.Equals(entry.Trim(), StringComparison.OrdinalIgnoreCase))]);
}
