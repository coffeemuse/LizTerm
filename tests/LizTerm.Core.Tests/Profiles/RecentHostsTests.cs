// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.Core.Profiles;

namespace LizTerm.Core.Tests.Profiles;

public class RecentHostsTests
{
    [Fact]
    public void With_puts_the_newest_entry_first()
    {
        var recent = RecentHosts.Empty.With("a.example").With("b.example");
        Assert.Equal(["b.example", "a.example"], recent.Entries);
    }

    /// <summary>Host names ignore case, so MVS.EXAMPLE and mvs.example are one entry; the spelling kept is the one
    /// typed last, because that is what the user will recognise.</summary>
    [Fact]
    public void An_entry_already_there_moves_to_the_top_in_its_new_spelling()
    {
        var recent = RecentHosts.Empty.With("mvs.example").With("tk5:3270").With("MVS.EXAMPLE");
        Assert.Equal(["MVS.EXAMPLE", "tk5:3270"], recent.Entries);
    }

    [Fact]
    public void The_list_keeps_only_the_most_recent_entries()
    {
        var recent = RecentHosts.Empty;
        for (var i = 0; i < RecentHosts.Max + 3; i++) recent = recent.With($"h{i}:23");

        Assert.Equal(RecentHosts.Max, recent.Entries.Count);
        Assert.Equal($"h{RecentHosts.Max + 2}:23", recent.Entries[0]);
        Assert.DoesNotContain("h2:23", recent.Entries);
    }

    [Fact]
    public void Without_removes_an_entry_ignoring_case_and_keeps_the_order_of_the_rest()
    {
        var recent = RecentHosts.From(["a:23", "L:b.example", "c:23"]).Without("l:B.EXAMPLE");
        Assert.Equal(["a:23", "c:23"], recent.Entries);
    }

    /// <summary>The file is text a user can edit, so what comes back from it is held to the same rules as what the
    /// picker adds: trimmed, no blanks, no duplicates, no more than Max.</summary>
    [Fact]
    public void From_trims_and_drops_blanks_duplicates_and_the_overflow()
    {
        var recent = RecentHosts.From([" a:23 ", null, "", "   ", "A:23", .. Enumerable.Range(0, 20).Select(i => $"h{i}:23")]);

        Assert.Equal("a:23", recent.Entries[0]);
        Assert.Equal(RecentHosts.Max, recent.Entries.Count);
        Assert.Single(recent.Entries, e => e.Equals("a:23", StringComparison.OrdinalIgnoreCase));
    }
}
