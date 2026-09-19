// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.Core.Session;

namespace LizTerm.App.ViewModels;

/// <summary>One entry in the profile editor's display drop-down. The label names both the look and the terminal
/// the host is told about, so a user who knows only one of the two words finds the row (#123).</summary>
public sealed record DisplayChoice(TerminalDisplay Display)
{
    public static IReadOnlyList<DisplayChoice> All { get; } = [new(TerminalDisplay.Color), new(TerminalDisplay.Mono)];

    public override string ToString() => Display == TerminalDisplay.Mono ? "Mono (3278)" : "Colour (3279)";
}
