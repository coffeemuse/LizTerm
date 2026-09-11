// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Globalization;
using Avalonia.Data.Converters;
using LizTerm.Core.Settings;

namespace LizTerm.App.ViewModels;

/// <summary>"Is the bell sound this one?", for a Preferences radio's IsChecked. One-way only, for the reason
/// CrosshairModeConverter gives: a radio group over one enum cannot be driven by independent two-way bools. The
/// Click handlers write the enum; these bindings render it. The parameter is programmer input from a hand-written
/// ConverterParameter, so a name that is present but wrong throws (a typo would otherwise build clean and leave
/// that radio unable to ever show as checked), while a missing one answers false.</summary>
public sealed class BellSoundConverter : IValueConverter
{
    public static readonly BellSoundConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var wanted = ParseParameter(parameter);
        return wanted is not null && value is BellSound sound && sound == wanted;
    }

    private static BellSound? ParseParameter(object? parameter) => parameter switch
    {
        null => null,
        string name when Enum.TryParse<BellSound>(name, out var wanted) => wanted,
        _ => throw new ArgumentException(
            $"'{parameter}' is not a BellSound. Valid names: " + string.Join(", ", Enum.GetNames<BellSound>()) + ".",
            nameof(parameter)),
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("Bell sound radios are one-way; the Click handlers set the sound.");
}
