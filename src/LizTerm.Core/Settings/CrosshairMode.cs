// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

namespace LizTerm.Core.Settings;

/// <summary>Which ruler lines follow the cursor. Vista offers all three shapes; horizontal alone is the one
/// that earns its keep on a wide panel, where the problem is tracking one row across 132 columns.</summary>
public enum CrosshairMode
{
    None,
    Horizontal,
    Vertical,
    Both,
}
