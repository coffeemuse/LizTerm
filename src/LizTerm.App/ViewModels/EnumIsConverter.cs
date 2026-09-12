// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Globalization;
using Avalonia.Data.Converters;

namespace LizTerm.App.ViewModels;

/// <summary>"Is the enum value this member?", for a menu item's or a radio's IsChecked. One-way only: a radio
/// group over one enum cannot be driven by independent two-way bools, because setting one true would leave the
/// others true as well. The Click handlers write the enum; these bindings render it.
///
/// The parameter is programmer input, not user data — it comes from a hand-written ConverterParameter in
/// SessionWindow.axaml and PreferencesWindow.axaml, never from anything a host or a user types — so the same rule
/// MenuLookup.Required applies to a header lookup applies here: something present but wrong throws, because a typo
/// (say "Horizantal") would otherwise build clean and pass every test while quietly leaving that item unable to
/// ever show a check mark, indistinguishable from "correctly not selected". A missing parameter still answers
/// false rather than throwing: XAML always supplies one here, so a null only shows up from a test or a future
/// binding that has none to give, and that is the legitimate "not this member" case, not a typo.
///
/// Abstract and generic: each enum gets a sealed subclass holding nothing but the <c>Instance</c> field that
/// <c>{x:Static}</c> needs, so the rule lives once.</summary>
public abstract class EnumIsConverter<TEnum> : IValueConverter where TEnum : struct, Enum
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // ParseParameter runs first, and unconditionally, so a bad parameter throws whether or not value
        // happens to be a TEnum — the whole point is to catch a typo that a real binding would never
        // exercise with a mismatched value type.
        var wanted = ParseParameter(parameter);
        return wanted is { } w && value is TEnum v && EqualityComparer<TEnum>.Default.Equals(v, w);
    }

    private static TEnum? ParseParameter(object? parameter) => parameter switch
    {
        null => null,
        string name when Enum.TryParse<TEnum>(name, out var wanted) => wanted,
        _ => throw new ArgumentException(
            $"'{parameter}' is not a {typeof(TEnum).Name}. Valid names: " +
            string.Join(", ", Enum.GetNames<TEnum>()) + ".",
            nameof(parameter)),
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException($"{typeof(TEnum).Name} check marks are one-way; the Click handlers set the value.");
}
