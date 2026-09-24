// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia;
using Avalonia.Controls;

namespace LizTerm.App.Controls;

/// <summary>Takes whatever width its cell gives it but asks for none, so its child can fill (and trim to) Auto
/// columns without widening them. The USS file list's per-file status spans the shared SIZE and MODIFIED columns;
/// without this, one long result would widen them for the header and every row (#186).</summary>
public class WidthlessDecorator : Decorator
{
    protected override Size MeasureOverride(Size availableSize) =>
        base.MeasureOverride(availableSize).WithWidth(0);
}
