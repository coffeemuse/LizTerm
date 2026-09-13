// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Media;
using LizTerm.App.Rendering;
using LizTerm.Core.Profiles;
using LizTerm.Core.Session;

namespace LizTerm.App.ViewModels;

/// <summary>One chip, ready to draw.</summary>
public sealed record TagChip(string Text, IBrush Background);

/// <summary>One row of the session list. The ListBox binds these rather than <see cref="SessionProfile"/>
/// itself, because a chip needs a COLOUR and a profile does not know its tags' colours — only the registry
/// does. Resolving that inside a DataTemplate would mean a multi-binding against the registry or static mutable
/// state; resolving it here is one plain class with plain properties, which is also what makes the rendering
/// rules testable without a window.
///
/// Immutable and rebuilt on every reload: a row is a snapshot of one profile against one registry, exactly as
/// the screen is a snapshot of the buffer.</summary>
public sealed class ProfileRow
{
    /// <summary>Chips drawn before the rest collapse into <see cref="OverflowText"/>. Three fits the 356px the
    /// list has once the button column takes its share, and Robert expects two or three in practice.</summary>
    public const int MaxChips = 3;

    public ProfileRow(SessionProfile profile, TagRegistry registry)
    {
        Profile = profile;
        Name = profile.Name;
        HostPort = $"{profile.Host}:{profile.Port}";
        Note = string.IsNullOrWhiteSpace(profile.Note) ? null : profile.Note.Trim();
        IsFavorite = profile.Tags.Contains(TagRegistry.FavoriteName);

        // The reserved tag is the star, never a chip.
        var names = profile.Tags.Names.Where(name => !TagRegistry.IsReserved(name)).ToList();
        Chips = [.. names.Take(MaxChips)
                         .Select(name => new TagChip(name.ToUpperInvariant(), TagPalette.Brush(registry.ColorOf(name))))];

        var hidden = names.Skip(MaxChips).ToList();
        OverflowText = hidden.Count > 0 ? $"+{hidden.Count}" : null;
        OverflowTip = hidden.Count > 0 ? string.Join(", ", hidden.Select(name => name.ToUpperInvariant())) : null;
    }

    /// <summary>The store's own instance, unchanged. Connect, Edit and Delete act on this.</summary>
    public SessionProfile Profile { get; }

    public string Name { get; }

    public string HostPort { get; }

    /// <summary>Null when there is nothing to show, so the third line collapses and an un-noted profile stays
    /// two lines tall.</summary>
    public string? Note { get; }

    public bool HasNote => Note is not null;

    public bool IsFavorite { get; }

    public IReadOnlyList<TagChip> Chips { get; }

    public string? OverflowText { get; }

    public string? OverflowTip { get; }

    public bool HasOverflow => OverflowText is not null;

    /// <summary>Whether the row menu's FAVORITE entry is enabled. Removing always is; adding needs room under
    /// <see cref="TagSet.MaxTags"/>, because TagSet.With on a full set drops the name it was given and the click
    /// would silently do nothing.</summary>
    public bool CanToggleFavorite => IsFavorite || Profile.Tags.Count < TagSet.MaxTags;

    /// <summary>The row menu's FAVORITE entry, which says why it is disabled when it is.</summary>
    public string FavoriteMenuText =>
        IsFavorite ? "Remove from FAVORITE"
        : CanToggleFavorite ? "Mark as FAVORITE"
        : $"Mark as FAVORITE (already {TagSet.MaxTags} tags)";
}
