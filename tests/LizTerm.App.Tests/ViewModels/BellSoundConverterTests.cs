// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Globalization;
using LizTerm.App.ViewModels;
using LizTerm.Core.Settings;

namespace LizTerm.App.Tests.ViewModels;

/// <summary>BellSoundConverter.Convert backs each Preferences sound radio's IsChecked, keyed by a hand-written
/// ConverterParameter; the same contract as CrosshairModeConverter, including the loud failure on a typo.</summary>
public class BellSoundConverterTests
{
    private static readonly BellSoundConverter Converter = BellSoundConverter.Instance;

    [Fact]
    public void A_matching_sound_and_parameter_convert_to_true()
    {
        Assert.Equal(true, Converter.Convert(BellSound.SystemAlert, typeof(bool), "SystemAlert", CultureInfo.InvariantCulture));
    }

    [Fact]
    public void A_non_matching_sound_converts_to_false()
    {
        Assert.Equal(false, Converter.Convert(BellSound.SystemAlert, typeof(bool), "None", CultureInfo.InvariantCulture));
    }

    [Fact]
    public void A_parameter_that_is_not_a_sound_name_throws()
    {
        var ex = Assert.Throws<ArgumentException>(() => Converter.Convert(BellSound.None, typeof(bool), "Sytsem", CultureInfo.InvariantCulture));
        Assert.Contains("SystemAlert", ex.Message);
    }

    [Fact]
    public void A_missing_parameter_is_false_not_an_error()
    {
        Assert.Equal(false, Converter.Convert(BellSound.None, typeof(bool), null, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void ConvertBack_is_not_supported()
    {
        Assert.Throws<NotSupportedException>(() => Converter.ConvertBack(true, typeof(BellSound), "None", CultureInfo.InvariantCulture));
    }
}
