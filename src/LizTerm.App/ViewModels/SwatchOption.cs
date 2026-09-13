// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Media;
using LizTerm.Core.Profiles;

namespace LizTerm.App.ViewModels;

/// <summary>One colour swatch in Manage Tags' panel. <see cref="Name"/> is both its tooltip and its accessible
/// name, so the choice is never carried by colour alone.</summary>
public sealed record SwatchOption(TagColor Color, IBrush Brush, bool IsSelected)
{
    public string Name => Color.ToString();
}
