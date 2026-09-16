// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.Core.Session;

namespace LizTerm.App.ViewModels;

/// <summary>One entry in the profile editor's Model drop-down: a catalogue model, or <see cref="Other"/> for a
/// custom (oversize) screen size. Other is an editor idea rather than a Core one: a profile still saves a model
/// number and an oversize text, and Other is what the editor shows for a profile whose oversize is set.</summary>
public sealed record ModelChoice(TerminalModel? Model)
{
    public static ModelChoice Other { get; } = new((TerminalModel?)null);

    public override string ToString() => Model?.ToString() ?? "Other (custom size)";
}
