// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

namespace LizTerm.App.Status;

/// <summary>The status bar's message area: x3270's symbols for the bar, the plain sentence for the tooltip, and
/// whether it is an operator error, which x3270 paints red.</summary>
public readonly record struct OiaMessage(string Text, bool IsError, string Tip);
