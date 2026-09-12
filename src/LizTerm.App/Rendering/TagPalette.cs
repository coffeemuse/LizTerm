// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Media;
using Avalonia.Media.Immutable;
using LizTerm.Core.Profiles;

namespace LizTerm.App.Rendering;

/// <summary>Chip colours for the session list's tags, the App-side half of <see cref="TagColor"/> — Core holds
/// the names, this holds the pixels, which is what keeps Avalonia out of Core.
///
/// Every value is deep enough that WHITE chip text clears 4.5:1 on it, asserted by TagPaletteTests, and light
/// enough to read as a filled shape on the dark list. Chosen that way so the chips survive #78 adding a light
/// theme without needing a second palette: white-on-deep reads on either ground.
///
/// Separate from <see cref="Palette"/> on purpose. That one maps the 16 IBM host colours for the terminal
/// screen and is tuned for a black background; these are UI chrome.</summary>
public static class TagPalette
{
    private static readonly Dictionary<TagColor, Color> Colors = new()
    {
        // Reserved for FAVORITE. Only ever seen as the star's fill, but mapped like the rest so the
        // exhaustiveness test covers it and a future gold chip needs no new value.
        [TagColor.Gold] = Color.FromRgb(0x8A, 0x64, 0x00),
        [TagColor.Red] = Color.FromRgb(0xA8, 0x3A, 0x37),
        [TagColor.Amber] = Color.FromRgb(0x8A, 0x5A, 0x0F),
        [TagColor.Green] = Color.FromRgb(0x2E, 0x6B, 0x3F),
        [TagColor.Blue] = Color.FromRgb(0x2A, 0x5F, 0x9E),
        [TagColor.Purple] = Color.FromRgb(0x6B, 0x4C, 0x9E),
        [TagColor.Teal] = Color.FromRgb(0x1F, 0x5F, 0x66),
        [TagColor.Grey] = Color.FromRgb(0x5E, 0x5E, 0x5E),
    };

    private static readonly Dictionary<TagColor, IBrush> Cache = [];

    /// <summary>Chip text, on every colour. One value rather than one per chip because the palette above is
    /// chosen to make one value enough.</summary>
    public static IBrush ChipText { get; } = new ImmutableSolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));

    /// <summary>The FAVORITE star. Brighter than <see cref="TagColor.Gold"/>'s chip fill: a glyph is thin
    /// strokes on the list background rather than a filled block under white text, so it needs the contrast the
    /// other direction.</summary>
    public static IBrush Star { get; } = new ImmutableSolidColorBrush(Color.FromRgb(0xE8, 0xB2, 0x30));

    /// <summary>The "+n" chip, which stands for tags of several colours and so belongs to none of them.</summary>
    public static IBrush Overflow { get; } = new ImmutableSolidColorBrush(Color.FromRgb(0x4A, 0x4A, 0x4A));

    public static Color ColorOf(TagColor color) => Colors[color];

    public static IBrush Brush(TagColor color)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(color, out var brush)) return brush;
            brush = new ImmutableSolidColorBrush(ColorOf(color));
            Cache[color] = brush;
            return brush;
        }
    }
}
