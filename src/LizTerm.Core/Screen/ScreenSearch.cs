// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Text;

namespace LizTerm.Core.Screen;

/// <summary>Finding a term on one screen. Pure and snapshot-scoped: a snapshot is immutable, so a search needs
/// no locking and cannot tear, and matches computed here are coordinates that the caller re-runs against the
/// next snapshot rather than trying to keep alive.</summary>
public static class ScreenSearch
{
    /// <summary>Every occurrence of <paramref name="term"/>, case-insensitively, in reading order: row
    /// ascending, then column. Matches never overlap and never span a row boundary. A blank term matches
    /// nothing rather than every position.</summary>
    public static IReadOnlyList<ScreenRegion> Find(ScreenSnapshot snapshot, string term)
    {
        if (string.IsNullOrWhiteSpace(term)) return [];

        var needle = term.EnumerateRunes().Select(Rune.ToLowerInvariant).ToArray();
        var matches = new List<ScreenRegion>();

        // One entry per character rather than per cell, so a column index is exact and a DBCS character is
        // indivisible. Reused across rows to keep this allocation-light on a 43x132 screen.
        var runes = new List<Rune>(snapshot.Columns);
        var starts = new List<int>(snapshot.Columns);
        var ends = new List<int>(snapshot.Columns);

        for (var row = 0; row < snapshot.Rows; row++)
        {
            runes.Clear();
            starts.Clear();
            ends.Clear();

            for (var column = 0; column < snapshot.Columns; column++)
            {
                var cell = snapshot[row, column];

                // The trailing half of the character before it: widen that character instead of adding one.
                if (cell.Rendition.HasFlag(CellRendition.RightHalf) && runes.Count > 0)
                {
                    ends[^1] = column;
                    continue;
                }

                runes.Add(Rune.ToLowerInvariant(cell.Character));
                starts.Add(column);
                ends.Add(column);
            }

            for (var i = 0; i + needle.Length <= runes.Count; i++)
            {
                var hit = true;
                for (var j = 0; j < needle.Length; j++)
                {
                    if (runes[i + j] == needle[j]) continue;
                    hit = false;
                    break;
                }
                if (!hit) continue;

                matches.Add(ScreenRegion.FromCorners(row, starts[i], row, ends[i + needle.Length - 1]));
                i += needle.Length - 1;
            }
        }

        return matches;
    }
}
