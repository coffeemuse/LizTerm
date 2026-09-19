// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.App.Keyboard;
using LizTerm.Core.Session;
using LizTerm.Core.Settings;

namespace LizTerm.App.Tests.Keyboard;

public class KeymapActionTests
{
    [Theory]
    [InlineData("PA1", TerminalKey.PA1)]
    [InlineData("pf13", TerminalKey.PF13)]
    [InlineData("EraseEof", TerminalKey.EraseEof)]
    public void A_3270_key_name_reads_ignoring_case(string name, TerminalKey expected)
    {
        Assert.True(KeymapAction.TryFrom(new KeymapEntry.SendKey(name), out var action));
        Assert.Equal(new KeymapAction.SendKey(expected), action);
    }

    [Theory]
    [InlineData("")]
    [InlineData("3")]
    [InlineData(" 3")]
    [InlineData("+1")]
    [InlineData("PF1,PF2")]
    [InlineData(" PA1")]
    [InlineData("PF25")]
    [InlineData("Copy")]
    public void A_name_this_build_does_not_know_does_not_read(string name)
    {
        Assert.False(KeymapAction.TryFrom(new KeymapEntry.SendKey(name), out _));
    }

    [Fact]
    public void Text_and_unbound_read_and_empty_text_does_not()
    {
        Assert.True(KeymapAction.TryFrom(new KeymapEntry.TypeText("¬"), out var text));
        Assert.Equal(new KeymapAction.TypeText("¬"), text);
        Assert.True(KeymapAction.TryFrom(KeymapEntry.Unbound.Instance, out var unbound));
        Assert.Same(KeymapAction.Unbound.Instance, unbound);
        Assert.False(KeymapAction.TryFrom(new KeymapEntry.TypeText(""), out _));
    }

    [Fact]
    public void ToEntry_writes_the_key_by_name()
    {
        Assert.Equal(new KeymapEntry.SendKey("PA1"), new KeymapAction.SendKey(TerminalKey.PA1).ToEntry());
        Assert.Equal(new KeymapEntry.TypeText("¢"), new KeymapAction.TypeText("¢").ToEntry());
        Assert.Same(KeymapEntry.Unbound.Instance, KeymapAction.Unbound.Instance.ToEntry());
    }
}
