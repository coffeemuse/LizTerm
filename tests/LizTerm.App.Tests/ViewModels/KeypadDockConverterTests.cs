// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Globalization;
using LizTerm.App.ViewModels;
using LizTerm.Core.Settings;

namespace LizTerm.App.Tests.ViewModels;

/// <summary>KeypadDockConverter.Convert backs each dock radio's IsChecked — two in Preferences and two on each
/// menu (#71) — keyed by a hand-written ConverterParameter; the same contract as the crosshair and bell
/// converters, including the loud failure on a typo.</summary>
public class KeypadDockConverterTests
{
    private static readonly KeypadDockConverter Converter = KeypadDockConverter.Instance;

    [Fact]
    public void A_matching_dock_and_parameter_convert_to_true()
    {
        Assert.Equal(true, Converter.Convert(KeypadDock.Right, typeof(bool), "Right", CultureInfo.InvariantCulture));
    }

    [Fact]
    public void A_non_matching_dock_converts_to_false()
    {
        Assert.Equal(false, Converter.Convert(KeypadDock.Right, typeof(bool), "Bottom", CultureInfo.InvariantCulture));
    }

    [Fact]
    public void A_parameter_that_is_not_a_dock_name_throws()
    {
        var ex = Assert.Throws<ArgumentException>(() => Converter.Convert(KeypadDock.Bottom, typeof(bool), "Rigth", CultureInfo.InvariantCulture));
        Assert.Contains("Right", ex.Message);
    }

    [Fact]
    public void A_missing_parameter_is_false_not_an_error()
    {
        Assert.Equal(false, Converter.Convert(KeypadDock.Bottom, typeof(bool), null, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void ConvertBack_is_not_supported()
    {
        Assert.Throws<NotSupportedException>(() => Converter.ConvertBack(true, typeof(KeypadDock), "Bottom", CultureInfo.InvariantCulture));
    }
}
