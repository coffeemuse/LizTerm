// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Globalization;

namespace LizTerm.Core;

/// <summary>The one rule for a number this app will hand to the engine or write into a profile: plain ASCII
/// digits in range, and nothing else. It was spelled out three times — a port, an oversize dimension, the
/// keep-alive — with three copies of the same justification, one of which had drifted out of step with its
/// own code.
///
/// <para><see cref="NumberStyles.None"/> is what refuses a sign and surrounding space, so "+60", "-1" and
/// " 60" are format errors rather than values, and <see cref="CultureInfo.InvariantCulture"/> keeps a
/// thousands separator or a digit shape from another culture from being read as one.</para>
///
/// <para>It deliberately does NOT trim. A caller parsing a fragment of a larger string — a port section, one
/// side of <c>132x43</c> — must not, or "13 2x43" becomes a geometry; a caller forgiving a text box's stray
/// spaces trims at the call site, where that choice is visible.</para></summary>
public static class PlainNumber
{
    /// <param name="value">The parsed number, meaningful only when this returns true.</param>
    public static bool TryParse(string? text, int min, int max, out int value) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value) && value >= min && value <= max;
}
