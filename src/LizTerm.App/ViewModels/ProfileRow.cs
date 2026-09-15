// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Media;
using LizTerm.App.Rendering;
using LizTerm.Core.Profiles;
using LizTerm.Core.Session;

namespace LizTerm.App.ViewModels;

/// <summary>One chip, ready to draw.</summary>
public sealed record TagChip(string Text, IBrush Background)
{
    /// <summary>The rendering rules, in one place for the picker's rows and the session window's status bar and
    /// banner (#93): FAVORITE is the star and never a chip, the text is uppercase whatever the file holds, and
    /// the colour is the registry's — unregistered names draw grey.</summary>
    public static IReadOnlyList<TagChip> For(TagSet tags, TagRegistry registry) =>
        [.. tags.Names.Where(name => !TagRegistry.IsReserved(name))
                      .Select(name => new TagChip(name.ToUpperInvariant(), TagPalette.Brush(registry.ColorOf(name))))];
}

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

    /// <param name="isSaved">False for an ad hoc session (Quick Connect or a host on the command line), whose
    /// second line says so rather than repeating the host its name already carries.</param>
    public ProfileRow(SessionProfile profile, TagRegistry registry, bool isSaved = true)
    {
        Profile = profile;
        Name = profile.Name;
        HostPort = $"{profile.Host}:{profile.Port}";
        SecondLine = isSaved ? HostPort : QuickConnectLine;
        Note = string.IsNullOrWhiteSpace(profile.Note) ? null : profile.Note.Trim();
        IsFavorite = profile.Tags.Contains(TagRegistry.FavoriteName);

        var all = TagChip.For(profile.Tags, registry);
        Chips = [.. all.Take(MaxChips)];

        var hidden = all.Skip(MaxChips).ToList();
        OverflowText = hidden.Count > 0 ? $"+{hidden.Count}" : null;
        OverflowTip = hidden.Count > 0 ? string.Join(", ", hidden.Select(chip => chip.Text)) : null;
    }

    /// <summary>What an unsaved session's second line reads.</summary>
    public const string QuickConnectLine = "Quick Connect";

    /// <summary>The store's own instance, unchanged. Connect, Edit and Delete act on this.</summary>
    public SessionProfile Profile { get; }

    public string Name { get; }

    public string HostPort { get; }

    /// <summary>The row's second line, which ProfileSummaryTemplate draws: HostPort for a saved profile,
    /// QuickConnectLine for an ad hoc session (session switching spec §5.3).</summary>
    public string SecondLine { get; }

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
    /// <see cref="TagSet.MaxTags"/>, which <see cref="TagSet.CanAdd"/> decides. A snapshot like the rest of the
    /// row: the command asks the file again before it writes.</summary>
    public bool CanToggleFavorite => IsFavorite || Profile.Tags.CanAdd(TagRegistry.FavoriteName);

    /// <summary>The row menu's FAVORITE entry, which says why it is disabled when it is.</summary>
    public string FavoriteMenuText =>
        IsFavorite ? "Remove from FAVORITE"
        : CanToggleFavorite ? "Mark as FAVORITE"
        : $"Mark as FAVORITE (already {TagSet.MaxTags} tags)";
}
