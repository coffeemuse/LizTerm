// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Globalization;
using Avalonia.Data.Converters;
using LizTerm.Core.Settings;

namespace LizTerm.App.ViewModels;

/// <summary>"Is the crosshair in this mode?", for a menu item's IsChecked. One-way only: a radio group of four
/// cannot be driven by four independent two-way bools over one enum, because setting one true would leave the
/// other three true as well. The Click handlers write the enum; these bindings render it.
///
/// The parameter is programmer input, not user data — it comes from a hand-written ConverterParameter in
/// SessionWindow.axaml and PreferencesWindow.axaml, never from anything a host or a user types — so the same rule
/// MenuLookup.Required applies to a header lookup applies here: something present but wrong throws, because a typo
/// (say "Horizantal") would otherwise build clean and pass every test while quietly leaving that item unable to
/// ever show a check mark, indistinguishable from "correctly not selected". A missing parameter still answers
/// false rather than throwing: XAML always supplies one here, so a null only shows up from a test or a future
/// binding that has none to give, and that is the legitimate "not this mode" case, not a typo.</summary>
public sealed class CrosshairModeConverter : IValueConverter
{
    public static readonly CrosshairModeConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // ParseParameter runs first, and unconditionally, so a bad parameter throws whether or not value
        // happens to be a CrosshairMode — the whole point is to catch a typo that a real binding would never
        // exercise with a mismatched value type.
        var wanted = ParseParameter(parameter);
        return wanted is not null && value is CrosshairMode mode && mode == wanted;
    }

    private static CrosshairMode? ParseParameter(object? parameter) => parameter switch
    {
        null => null,
        string name when Enum.TryParse<CrosshairMode>(name, out var wanted) => wanted,
        _ => throw new ArgumentException(
            $"'{parameter}' is not a CrosshairMode. Valid names: " +
            string.Join(", ", Enum.GetNames<CrosshairMode>()) + ".",
            nameof(parameter)),
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("Crosshair menu items are one-way; the Click handlers set the mode.");
}
