// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.App.Menus;

namespace LizTerm.App.Tests.Menus;

/// <summary>The pure half of the override (Keys menu shortcuts spec §3.1). The AppKit half cannot run headless:
/// libAvaloniaNative is never loaded here, so Install finds no AvnMenu class and answers false without touching
/// anything, which is also what it must do under a future Avalonia that renames the class.</summary>
public class MacMenuKeyEquivalentsTests
{
    private const ulong Shift = 1UL << 17, Control = 1UL << 18, Option = 1UL << 19, Function = 1UL << 23;

    /// <summary>The rule: without ⌘ it is never a menu key equivalent in LizTerm, so it falls through to the window.</summary>
    [Theory]
    [InlineData(0UL)]
    [InlineData(Shift)]
    [InlineData(Control)]
    [InlineData(Option)]
    [InlineData(Shift | Function | 0x108UL)]
    public void A_chord_without_command_is_declined(ulong flags)
    {
        Assert.True(MacMenuKeyEquivalents.Declines(flags));
    }

    [Theory]
    [InlineData(MacMenuKeyEquivalents.CommandFlag)]
    [InlineData(MacMenuKeyEquivalents.CommandFlag | 0x108UL)]
    [InlineData(MacMenuKeyEquivalents.CommandFlag | Shift)]
    public void A_chord_with_command_goes_to_the_original(ulong flags)
    {
        Assert.False(MacMenuKeyEquivalents.Declines(flags));
    }

    [Fact]
    public void Off_macOS_nothing_is_installed()
    {
        Assert.False(MacMenuKeyEquivalents.Install(isMacOS: false));
        Assert.False(MacMenuKeyEquivalents.Installed);
    }

    /// <summary>On a Mac test runner AppKit's menu class is still absent (headless), so the answer is a quiet
    /// false; on the others the call must not even reach libobjc, which does not exist.</summary>
    [Fact]
    public void Without_the_menu_class_install_answers_false_and_does_not_throw()
    {
        if (!OperatingSystem.IsMacOS()) return;
        Assert.False(MacMenuKeyEquivalents.Install(isMacOS: true));
        Assert.False(MacMenuKeyEquivalents.Installed);
    }
}
