// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.App.Sessions;

namespace LizTerm.App.ViewModels;

/// <summary>What the switcher's filter matches (session switching spec §5.4): an ordinal, case-insensitive
/// substring of the name, host:port, any tag name, or the note. The note is included on purpose (Robert's call,
/// 2026-09-15): "live" should find the session noted "No live data".</summary>
public static class SwitcherFilter
{
    public static bool Matches(SessionEntry entry, string term)
    {
        if (term.Length == 0) return true;
        var row = entry.Summary;
        return Has(row.Name)
            || Has(row.HostPort)
            || entry.Session.Profile.Tags.Names.Any(Has)
            || (row.Note is { } note && Has(note));

        bool Has(string text) => text.Contains(term, StringComparison.OrdinalIgnoreCase);
    }
}
