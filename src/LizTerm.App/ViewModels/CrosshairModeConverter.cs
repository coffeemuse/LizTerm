// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Globalization;
using Avalonia.Data.Converters;
using LizTerm.App.Rendering;

namespace LizTerm.App.ViewModels;

/// <summary>"Is the crosshair in this mode?", for a menu item's IsChecked. One-way only: a radio group of four
/// cannot be driven by four independent two-way bools over one enum, because setting one true would leave the
/// other three true as well. The Click handlers write the enum; these bindings render it.</summary>
public sealed class CrosshairModeConverter : IValueConverter
{
    public static readonly CrosshairModeConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is CrosshairMode mode
        && parameter is string name
        && Enum.TryParse<CrosshairMode>(name, out var wanted)
        && mode == wanted;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("Crosshair menu items are one-way; the Click handlers set the mode.");
}
