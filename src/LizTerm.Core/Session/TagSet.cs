// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Collections.Immutable;

namespace LizTerm.Core.Session;

/// <summary>The tag names one profile carries: ordered, de-duplicated case-insensitively, and capped. A struct
/// with hand-written value equality rather than a list, because <see cref="SessionProfile"/> is a record and a
/// record's synthesised Equals compares a collection member by REFERENCE — two structurally identical profiles
/// read from disk would stop being equal, which ProfileStoreTests asserts they are. Hand-writing
/// <c>Equals</c> on the record itself was the alternative and was rejected: a field added later would be
/// silently absent from equality, and that record's whole design is about fields being added later.
///
/// Colours are deliberately absent. A tag's colour belongs to its definition in <c>TagRegistry</c>, so
/// <c>PROD</c> is one colour everywhere rather than one colour per profile that happens to use it.
///
/// <c>default(TagSet)</c> is the value every profile file written before tags existed produces, and its backing
/// array is <c>default</c> rather than empty — so every member here guards
/// <see cref="ImmutableArray{T}.IsDefaultOrEmpty"/>.</summary>
public readonly struct TagSet : IEquatable<TagSet>
{
    /// <summary>Caps against a pathological hand-edited file, not limits a user with two or three tags meets.
    /// <see cref="From"/> enforces both by discarding what does not fit; the profile editor validates what the
    /// user typed BEFORE calling From, because afterwards the evidence is gone.</summary>
    public const int MaxTags = 8;

    public const int MaxNameLength = 16;

    private readonly ImmutableArray<string> _names;

    private TagSet(ImmutableArray<string> names) => _names = names;

    public static TagSet Empty { get; } = new([]);

    /// <summary>Normalises and filters: trim, drop a leading '#', drop blanks, drop anything longer than
    /// <see cref="MaxNameLength"/>, de-duplicate ignoring case keeping the first casing seen, keep source order,
    /// and stop at <see cref="MaxTags"/>. An over-long name is dropped rather than truncated — truncating would
    /// silently invent a different tag.</summary>
    public static TagSet From(IEnumerable<string>? names)
    {
        if (names is null) return Empty;
        var kept = new List<string>(MaxTags);
        foreach (var raw in names)
        {
            if (raw is null) continue;
            var name = Normalize(raw);
            if (name.Length == 0 || name.Length > MaxNameLength) continue;
            if (kept.Any(k => k.Equals(name, StringComparison.OrdinalIgnoreCase))) continue;
            kept.Add(name);
            if (kept.Count == MaxTags) break;
        }
        return kept.Count == 0 ? Empty : new TagSet([.. kept]);
    }

    /// <summary>One name as it is stored: trimmed, without a leading '#'. The hash is a display convention — it
    /// is how Robert writes a tag and how the scope drop-down renders one — so "#PROD" and "PROD" must never
    /// become two tags that look identical wherever they are listed.</summary>
    public static string Normalize(string name)
    {
        var trimmed = name.Trim();
        return trimmed.StartsWith('#') ? trimmed.TrimStart('#').Trim() : trimmed;
    }

    /// <summary>The profile editor's comma-separated box, as names. Normalisation and the caps are
    /// <see cref="From"/>'s job; this only splits, so the editor can count what the user typed before any of it
    /// is discarded.</summary>
    public static IReadOnlyList<string> Split(string text) =>
        [.. text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Select(Normalize)
                .Where(name => name.Length > 0)];

    private ImmutableArray<string> Safe => _names.IsDefaultOrEmpty ? [] : _names;

    public IReadOnlyList<string> Names => Safe;

    public int Count => Safe.Length;

    public bool IsEmpty => Count == 0;

    public bool Contains(string name)
    {
        var wanted = Normalize(name);
        return Safe.Any(n => n.Equals(wanted, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>This set with <paramref name="name"/> appended, or this set unchanged when it is already there.
    /// The profile editor's FAVORITE checkbox is the caller.</summary>
    public TagSet With(string name) => Contains(name) ? this : From([.. Names, name]);

    public TagSet Without(string name)
    {
        var unwanted = Normalize(name);
        return Contains(name)
            ? From(Names.Where(n => !n.Equals(unwanted, StringComparison.OrdinalIgnoreCase)))
            : this;
    }

    public bool Equals(TagSet other)
    {
        var mine = Safe;
        var theirs = other.Safe;
        if (mine.Length != theirs.Length) return false;
        for (var i = 0; i < mine.Length; i++)
        {
            if (!mine[i].Equals(theirs[i], StringComparison.OrdinalIgnoreCase)) return false;
        }
        return true;
    }

    public override bool Equals(object? obj) => obj is TagSet other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var name in Safe) hash.Add(name.ToLowerInvariant());
        return hash.ToHashCode();
    }

    public static bool operator ==(TagSet left, TagSet right) => left.Equals(right);

    public static bool operator !=(TagSet left, TagSet right) => !left.Equals(right);

    /// <summary>What the profile editor's box shows. <see cref="Split"/> reads it back.</summary>
    public override string ToString() => string.Join(", ", Names);
}
