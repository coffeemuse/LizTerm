// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.App.Controls;
using LizTerm.Core.Session;

namespace LizTerm.App.Tests.Controls;

/// <summary>The keypad's contents as data (keypad spec §3): three banks of twelve, every key once, the Keys menu's
/// own labels. The guard that the Keys menu is a subset lives in NativeMenuTests, beside the menu it reads.</summary>
public class KeypadLayoutTests
{
    private static IEnumerable<KeypadKey> All => KeypadLayout.Banks.SelectMany(bank => bank);

    [Fact]
    public void Three_banks_of_twelve()
    {
        Assert.Equal(12, KeypadLayout.BankSize);
        Assert.Equal(3, KeypadLayout.Banks.Count);
        Assert.All(KeypadLayout.Banks, bank => Assert.Equal(KeypadLayout.BankSize, bank.Count));
    }

    [Fact]
    public void The_first_two_banks_are_PF1_to_PF24_in_order()
    {
        Assert.Equal(Enumerable.Range(1, 12).Select(n => $"PF{n}"), KeypadLayout.Banks[0].Select(k => k.Label));
        Assert.Equal(Enumerable.Range(0, 12).Select(i => TerminalKey.PF1 + i), KeypadLayout.Banks[0].Select(k => k.Key));
        Assert.Equal(Enumerable.Range(13, 12).Select(n => $"PF{n}"), KeypadLayout.Banks[1].Select(k => k.Label));
        Assert.Equal(Enumerable.Range(0, 12).Select(i => TerminalKey.PF13 + i), KeypadLayout.Banks[1].Select(k => k.Key));
    }

    /// <summary>The issue's list plus Erase Input (bound by nothing else), Dup and Field Mark (on the Keys menu
    /// since #16), in the issue's order, Insert in Enter's place (#111) until the keypad is customisable.</summary>
    [Fact]
    public void The_third_bank_is_the_specials()
    {
        Assert.Equal(
            ["PA1", "PA2", "PA3", "Insert", "Clear", "Reset", "Attn", "SysReq", "Erase EOF", "Erase Input", "Dup",
             "Field Mark"],
            KeypadLayout.Banks[2].Select(k => k.Label));
        Assert.Equal(
            [TerminalKey.PA1, TerminalKey.PA2, TerminalKey.PA3, TerminalKey.Insert, TerminalKey.Clear,
             TerminalKey.Reset, TerminalKey.Attn, TerminalKey.SysReq, TerminalKey.EraseEof, TerminalKey.EraseInput,
             TerminalKey.Dup, TerminalKey.FieldMark],
            KeypadLayout.Banks[2].Select(k => k.Key));
    }

    [Fact]
    public void No_key_appears_twice_and_no_label_is_empty()
    {
        Assert.Equal(All.Count(), All.Select(k => k.Key).Distinct().Count());
        Assert.All(All, k => Assert.False(string.IsNullOrWhiteSpace(k.Label)));
    }
}
