// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Globalization;
using LizTerm.App.ViewModels;
using LizTerm.Core.Settings;

namespace LizTerm.App.Tests.ViewModels;

/// <summary>The rules are EnumIsConverter's, asserted once each in CrosshairModeConverterTests and
/// BellSoundConverterTests. All a subclass can get wrong is which enum it is over, so that is what this asserts:
/// a MenuStyle name is understood, and a name belonging to one of the other settings enums is the loud failure a
/// typo gets rather than a quiet false.</summary>
public class MenuStyleConverterTests
{
    private static readonly MenuStyleConverter Converter = MenuStyleConverter.Instance;

    [Fact]
    public void A_matching_style_and_parameter_convert_to_true() =>
        Assert.Equal(true, Converter.Convert(MenuStyle.Both, typeof(bool), "Both", CultureInfo.InvariantCulture));

    [Fact]
    public void A_non_matching_style_converts_to_false() =>
        Assert.Equal(false, Converter.Convert(MenuStyle.Both, typeof(bool), "InWindow", CultureInfo.InvariantCulture));

    [Fact]
    public void A_parameter_from_another_settings_enum_throws()
    {
        var ex = Assert.Throws<ArgumentException>(
            () => Converter.Convert(MenuStyle.Native, typeof(bool), nameof(BellSound.SystemAlert), CultureInfo.InvariantCulture));
        Assert.Contains(nameof(MenuStyle.InWindow), ex.Message);
    }
}
