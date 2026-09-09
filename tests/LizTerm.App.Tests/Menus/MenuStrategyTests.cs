// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.App.Menus;

namespace LizTerm.App.Tests.Menus;

/// <summary>The whole per-platform menu decision, asserted without an operating system or the environment,
/// the way EngineRequirement.Decide is: every combination is reachable on every machine.</summary>
public class MenuStrategyTests
{
    [Theory]
    [InlineData("native")]
    [InlineData("NATIVE")]
    [InlineData("  native  ")]
    public void The_variable_can_force_the_native_menu_on_any_platform(string variable)
    {
        Assert.True(MenuStrategy.Decide(variable, isMacOS: true));
        Assert.True(MenuStrategy.Decide(variable, isMacOS: false));
    }

    [Theory]
    [InlineData("classic")]
    [InlineData("Classic")]
    [InlineData("\tclassic\n")]
    public void The_variable_can_force_the_classic_menu_on_any_platform(string variable)
    {
        Assert.False(MenuStrategy.Decide(variable, isMacOS: true));
        Assert.False(MenuStrategy.Decide(variable, isMacOS: false));
    }

    /// <summary>The default is the point of the staging device: native only where it has been watched to work.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Without_the_variable_only_macOS_gets_the_native_menu(string? variable)
    {
        Assert.True(MenuStrategy.Decide(variable, isMacOS: true));
        Assert.False(MenuStrategy.Decide(variable, isMacOS: false));
    }

    /// <summary>A typo must not leave the app with no menu bar at all: it is a development escape hatch, and
    /// the quiet fallback is the less damaging of the two failures.</summary>
    [Theory]
    [InlineData("banana")]
    [InlineData("1")]
    [InlineData("true")]
    public void An_unrecognised_value_falls_back_to_the_platform_default(string variable)
    {
        Assert.True(MenuStrategy.Decide(variable, isMacOS: true));
        Assert.False(MenuStrategy.Decide(variable, isMacOS: false));
    }

    [Fact]
    public void About_belongs_in_the_help_menu_everywhere_except_macOS()
    {
        Assert.False(MenuStrategy.AboutInHelpMenu(isMacOS: true));
        Assert.True(MenuStrategy.AboutInHelpMenu(isMacOS: false));
    }
}
