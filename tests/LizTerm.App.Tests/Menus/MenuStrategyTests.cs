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
    public void A_chosen_style_survives_resolution_on_macOS(MenuStyle style) =>
        Assert.Equal(style, MenuStrategy.Resolve(style, isMacOS: true));

    /// <summary>Off macOS the choice is not offered, so it must not be honoured either: Both would draw two bars
    /// stacked and Native the renderer nobody has reviewed (#22), and Preferences hides the group there, so a
    /// settings file carried from a Mac would strand a user in a state with no control to leave it.</summary>
    [Theory]
    [InlineData(MenuStyle.Auto)]
    [InlineData(MenuStyle.Native)]
    [InlineData(MenuStyle.InWindow)]
    [InlineData(MenuStyle.Both)]
    public void Every_style_resolves_to_the_in_window_menu_off_macOS(MenuStyle style) =>
        Assert.Equal(MenuStyle.InWindow, MenuStrategy.Resolve(style, isMacOS: false));

    /// <summary>A settings file is text and JsonStringEnumConverter accepts integers, so `"menuStyle": 9` reads
    /// back as a value that is not a member at all rather than falling to Auto the way an unknown name does.
    /// Resolve is the one place that is normalised; without it the value names no renderer and ApplyMenuStyle
    /// would have nothing to draw.</summary>
    [Fact]
    public void A_value_that_is_not_a_member_resolves_to_the_platform_default()
    {
        Assert.Equal(MenuStyle.Native, MenuStrategy.Resolve((MenuStyle)9, isMacOS: true));
        Assert.Equal(MenuStyle.InWindow, MenuStrategy.Resolve((MenuStyle)9, isMacOS: false));
    }

    /// <summary>The same rule as Resolve, seen from Preferences: the group is offered exactly where a style other
    /// than the platform's own is honoured.</summary>
    [Fact]
    public void The_menu_style_is_a_choice_only_on_macOS()
    {
        Assert.True(MenuStrategy.MenuStyleChoosable(isMacOS: true));
        Assert.False(MenuStrategy.MenuStyleChoosable(isMacOS: false));
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
