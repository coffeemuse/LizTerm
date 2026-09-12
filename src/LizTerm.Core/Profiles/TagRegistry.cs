// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.Core.Session;

namespace LizTerm.Core.Profiles;

/// <summary>Every tag definition in force, keyed by name ignoring case. Immutable: <see cref="Register"/>
/// returns a new registry rather than mutating this one, so the picker can hold one value and replace it.
///
/// FAVORITE is synthesised, never stored (spec 4.2): it is always present, always <see cref="FavoriteColor"/>,
/// and an entry for it found in a file is dropped. That is what makes its definition immutable while what is
/// tagged with it stays free to change.</summary>
public sealed class TagRegistry
{
    /// <summary>The reserved tag. Singular, this spelling, and drawn as a gold star rather than a text chip.</summary>
    public const string FavoriteName = "FAVORITE";

    public const TagColor FavoriteColor = TagColor.Gold;

    /// <summary>What <see cref="ColorOf"/> answers for a name no definition covers. Reconciliation registers
    /// every name it sees before anything renders, so this is a defensive answer rather than one a user meets —
    /// but a missing definition must be a quiet colour, not a throw.</summary>
    public const TagColor UnregisteredColor = TagColor.Grey;

    public static readonly TagDefinition Favorite = new(FavoriteName, FavoriteColor);

    /// <summary>The colours <see cref="Register"/> draws from: every member except the reserved
    /// <see cref="FavoriteColor"/>, in enum order, which is also the tie-break order.</summary>
    public static readonly IReadOnlyList<TagColor> AssignableColors =
        [.. Enum.GetValues<TagColor>().Where(color => color != FavoriteColor)];

    private readonly Dictionary<string, TagDefinition> _byName;

    public static TagRegistry Empty { get; } = new([]);

    /// <summary>Keeps the last definition for a repeated name, drops a blank or over-long one, and drops any
    /// entry for the reserved tag.</summary>
    public TagRegistry(IEnumerable<TagDefinition> definitions)
    {
        _byName = new Dictionary<string, TagDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var definition in definitions)
        {
            var name = TagSet.Normalize(definition.Name);
            if (name.Length == 0 || name.Length > TagSet.MaxNameLength || IsReserved(name)) continue;
            _byName[name] = new TagDefinition(name, definition.Color);
        }
    }

    public static bool IsReserved(string name) =>
        TagSet.Normalize(name).Equals(FavoriteName, StringComparison.OrdinalIgnoreCase);

    /// <summary>Every definition: the reserved one first, then the rest by name ignoring case. The scope
    /// drop-down renders this directly, so the order is alphabetical rather than whichever tag happened to be
    /// created first — insertion order is not something a user can predict.</summary>
    public IReadOnlyList<TagDefinition> All => [Favorite, .. Stored];

    /// <summary>What <c>TagRegistryStore</c> writes: <see cref="All"/> without the synthesised reserved tag.</summary>
    public IReadOnlyList<TagDefinition> Stored =>
        [.. _byName.Values.OrderBy(definition => definition.Name, StringComparer.OrdinalIgnoreCase)];

    public TagColor ColorOf(string name)
    {
        if (IsReserved(name)) return FavoriteColor;
        return _byName.TryGetValue(TagSet.Normalize(name), out var definition) ? definition.Color : UnregisteredColor;
    }

    /// <summary>This registry plus a definition for every name it does not already know, each taking the
    /// assignable colour fewest definitions currently use. Existing definitions are never touched, so a colour
    /// chosen in Manage Tags survives every later reconciliation.</summary>
    /// <returns>The registry to use, and whether anything was added — a caller writes the file only when
    /// something was, so merely opening the picker does not rewrite <c>tags.json</c>.</returns>
    public (TagRegistry Registry, bool Changed) Register(IEnumerable<string> names)
    {
        var counts = _byName.Values.GroupBy(definition => definition.Color)
            .ToDictionary(group => group.Key, group => group.Count());
        var known = new HashSet<string>(_byName.Keys, StringComparer.OrdinalIgnoreCase);
        var added = new List<TagDefinition>();

        foreach (var raw in names)
        {
            if (raw is null) continue;
            var name = TagSet.Normalize(raw);
            if (name.Length == 0 || name.Length > TagSet.MaxNameLength || IsReserved(name)) continue;
            if (!known.Add(name)) continue;
            var color = LeastUsed(counts);
            counts[color] = counts.GetValueOrDefault(color) + 1;
            added.Add(new TagDefinition(name, color));
        }

        return added.Count == 0 ? (this, false) : (new TagRegistry([.. Stored, .. added]), true);
    }

    /// <summary>The assignable colour fewest definitions use. OrderBy is stable and
    /// <see cref="AssignableColors"/> is already in enum order, so the ThenBy only makes the tie-break
    /// explicit.</summary>
    private static TagColor LeastUsed(IReadOnlyDictionary<TagColor, int> counts) =>
        AssignableColors.OrderBy(counts.GetValueOrDefault).ThenBy(color => (int)color).First();
}
