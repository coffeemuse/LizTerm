// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

namespace LizTerm.Core.Session;

/// <summary>Whether a profile is a colour 3279 or a monochrome 3278. It changes what the host is told (the
/// terminal name, and whether the capability reply mentions colour) and how the screen is drawn: a 3278 has one
/// phosphor, so the renderer draws every cell in it and only intensity varies. An enum rather than a bool so a
/// file that lacks the field reads as the colour default by name, and a third kind could join without a
/// migration (#123).</summary>
public enum TerminalDisplay
{
    Color,
    Mono,
}
