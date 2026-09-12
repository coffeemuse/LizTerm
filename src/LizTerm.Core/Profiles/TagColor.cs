// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

namespace LizTerm.Core.Profiles;

/// <summary>A tag chip's colour, by name. In Core and BCL-only, because Core never mentions Avalonia; the App
/// maps each member to a brush in <c>Rendering/TagPalette.cs</c>.
///
/// Named for the TAG rather than the profile, and living here rather than in <c>Core.Session</c>, because a
/// colour belongs to a tag's definition — <c>SessionProfile</c> never mentions this type. <c>HostColor</c> is
/// deliberately not reused: it is the 17-member 3270 screen model in b3270's naming order, tuned by
/// <c>Rendering/Palette.cs</c> for a black terminal ground.
///
/// <see cref="Gold"/> is reserved for the FAVORITE tag and is never auto-assigned. Written to <c>tags.json</c>
/// by name, so reordering this enum can never change a saved meaning.</summary>
public enum TagColor
{
    Gold,
    Red,
    Amber,
    Green,
    Blue,
    Purple,
    Teal,
    Grey,
}
