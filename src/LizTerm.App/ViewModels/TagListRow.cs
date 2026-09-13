// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.App.Rendering;
using LizTerm.Core.Profiles;

namespace LizTerm.App.ViewModels;

/// <summary>One row of Manage Tags' list: a definition, how it draws, and which profiles carry it. Immutable and
/// rebuilt on every refresh, as ProfileRow is.</summary>
public sealed class TagListRow
{
    public TagListRow(TagDefinition definition, IReadOnlyList<string> usedBy)
    {
        Name = definition.Name;
        Color = definition.Color;
        IsReserved = TagRegistry.IsReserved(definition.Name);
        Chip = new TagChip(definition.Name.ToUpperInvariant(), TagPalette.Brush(definition.Color));
        UsedBy = usedBy;
    }

    /// <summary>As stored, casing included: the name box starts from this, and a case-only rename is judged
    /// against it.</summary>
    public string Name { get; }

    public TagColor Color { get; }

    /// <summary>FAVORITE: drawn as the star, and offered no action.</summary>
    public bool IsReserved { get; }

    public bool IsNotReserved => !IsReserved;

    public TagChip Chip { get; }

    /// <summary>The profiles carrying this tag, in the store's order.</summary>
    public IReadOnlyList<string> UsedBy { get; }

    public bool IsUnused => UsedBy.Count == 0;

    public string CountText => IsUnused ? "unused" : $"{UsedBy.Count}";
}
