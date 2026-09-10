// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Globalization;
using LizTerm.App.Rendering;
using LizTerm.App.ViewModels;

namespace LizTerm.App.Tests.ViewModels;

/// <summary>CrosshairModeConverter.Convert backs every View menu item's IsChecked, keyed by a hand-written
/// ConverterParameter in SessionWindow.axaml. NativeMenuTests.Choosing_a_crosshair_mode_checks_exactly_that_item
/// covers the four real parameters end to end; this covers the converter's own contract in isolation, including
/// the case no XAML in the tree currently exercises: a parameter that does not parse as a CrosshairMode at
/// all.</summary>
public class CrosshairModeConverterTests
{
    private static readonly CrosshairModeConverter Converter = CrosshairModeConverter.Instance;

    [Fact]
    public void A_matching_mode_and_parameter_convert_to_true()
    {
        var result = Converter.Convert(CrosshairMode.Vertical, typeof(bool), "Vertical", CultureInfo.InvariantCulture);

        Assert.Equal(true, result);
    }

    [Fact]
    public void A_non_matching_mode_converts_to_false()
    {
        var result = Converter.Convert(CrosshairMode.Vertical, typeof(bool), "Horizontal", CultureInfo.InvariantCulture);

        Assert.Equal(false, result);
    }

    /// <summary>The gap the review closed: a mistyped ConverterParameter (a real "Horizantal" in the XAML would
    /// hit this) must fail loudly rather than just never matching, the same rule MenuLookup.Required applies to
    /// a header lookup that is present but wrong.</summary>
    [Fact]
    public void A_parameter_that_is_not_a_crosshair_mode_name_throws()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            Converter.Convert(CrosshairMode.Vertical, typeof(bool), "Horizantal", CultureInfo.InvariantCulture));

        Assert.Contains("Horizantal", ex.Message);
        foreach (var name in Enum.GetNames<CrosshairMode>())
        {
            Assert.Contains(name, ex.Message);
        }
    }

    [Fact]
    public void A_null_parameter_converts_to_false_rather_than_throwing()
    {
        var result = Converter.Convert(CrosshairMode.None, typeof(bool), null, CultureInfo.InvariantCulture);

        Assert.Equal(false, result);
    }

    [Fact]
    public void ConvertBack_is_unreachable()
    {
        Assert.Throws<NotSupportedException>(() =>
            Converter.ConvertBack(true, typeof(CrosshairMode), "Vertical", CultureInfo.InvariantCulture));
    }
}
