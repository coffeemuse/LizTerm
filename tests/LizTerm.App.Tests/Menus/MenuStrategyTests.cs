// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.App.Menus;
using LizTerm.Core.Settings;

namespace LizTerm.App.Tests.Menus;

/// <summary>The whole per-platform menu decision, asserted without an operating system or the environment,
/// the way EngineRequirement.Decide is: every combination is reachable on every machine.</summary>
public class MenuStrategyTests
{
    /// <summary>Auto is what an untouched settings file reads as, and it has to mean today's behaviour.</summary>
    [Fact]
    public void Auto_resolves_to_the_native_menu_on_macOS_and_the_in_window_one_elsewhere()
    {
        Assert.Equal(MenuStyle.Native, MenuStrategy.Resolve(MenuStyle.Auto, isMacOS: true));
        Assert.Equal(MenuStyle.InWindow, MenuStrategy.Resolve(MenuStyle.Auto, isMacOS: false));
    }

    [Theory]
    [InlineData(MenuStyle.Native)]
    [InlineData(MenuStyle.InWindow)]
    [InlineData(MenuStyle.Both)]
    public void A_chosen_style_survives_resolution_on_every_platform(MenuStyle style)
    {
        Assert.Equal(style, MenuStrategy.Resolve(style, isMacOS: true));
        Assert.Equal(style, MenuStrategy.Resolve(style, isMacOS: false));
    }

    [Theory]
    [InlineData("native", MenuStyle.Native)]
    [InlineData("NATIVE", MenuStyle.Native)]
    [InlineData("  native  ", MenuStyle.Native)]
    [InlineData("classic", MenuStyle.InWindow)]
    [InlineData("Classic", MenuStyle.InWindow)]
    [InlineData("\tclassic\n", MenuStyle.InWindow)]
    [InlineData("both", MenuStyle.Both)]
    [InlineData("BOTH", MenuStyle.Both)]
    public void The_variable_names_a_style(string variable, MenuStyle expected) =>
        Assert.Equal(expected, MenuStrategy.FromVariable(variable));

    /// <summary>"classic" is the name this variable has always used for the in-window menu, and it keeps working.
    /// A typo must not leave the app with no menu bar at all: naming no style means the saved preference decides,
    /// and the quiet fallback is the less damaging of the two failures. "auto" names no style either — Auto is the
    /// absence of a choice, so seeding it would be seeding nothing.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("banana")]
    [InlineData("1")]
    [InlineData("true")]
    [InlineData("auto")]
    public void An_absent_or_unrecognised_variable_names_no_style(string? variable) =>
        Assert.Null(MenuStrategy.FromVariable(variable));

    [Fact]
    public void About_belongs_in_the_help_menu_everywhere_except_macOS()
    {
        Assert.False(MenuStrategy.AboutInHelpMenu(isMacOS: true));
        Assert.True(MenuStrategy.AboutInHelpMenu(isMacOS: false));
    }

    /// <summary>macOS has Preferences in the application menu, with Cmd-comma; Edit carries one everywhere else.</summary>
    [Fact]
    public void Preferences_belongs_in_the_edit_menu_everywhere_except_macOS()
    {
        Assert.False(MenuStrategy.PreferencesInEditMenu(isMacOS: true));
        Assert.True(MenuStrategy.PreferencesInEditMenu(isMacOS: false));
    }
}
