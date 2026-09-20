// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.App.Keyboard;

namespace LizTerm.App.Tests.Keyboard;

/// <summary>The pure half of the override (#157). The AppKit half cannot run headless: libAvaloniaNative is never
/// loaded here, so Install finds no AvnView class and answers false without touching anything, which is also what
/// it must do under a future Avalonia that renames the class or gives the view a cancelOperation: of its own.</summary>
public class MacEscapeChordsTests
{
    private const ulong KeyUp = 11, FlagsChanged = 12, LeftMouseDown = 1;
    private const ushort KeyR = 15, KeyPeriod = 47;

    /// <summary>The rule: only the Escape key-down AppKit diverted is handed back to keyDown:.</summary>
    [Fact]
    public void An_escape_key_down_is_forwarded()
    {
        Assert.True(MacEscapeChords.Forwards(MacEscapeChords.KeyDownEventType, MacEscapeChords.EscapeKeyCode));
    }

    /// <summary>Any other current event means cancelOperation: arrived some other way (⌘. is one, a programmatic
    /// send another), and none of those is a key the screen should see.</summary>
    [Theory]
    [InlineData(KeyUp, MacEscapeChords.EscapeKeyCode)]
    [InlineData(FlagsChanged, MacEscapeChords.EscapeKeyCode)]
    [InlineData(LeftMouseDown, MacEscapeChords.EscapeKeyCode)]
    [InlineData(MacEscapeChords.KeyDownEventType, KeyR)]
    [InlineData(MacEscapeChords.KeyDownEventType, KeyPeriod)]
    public void Anything_else_is_left_alone(ulong eventType, ushort keyCode)
    {
        Assert.False(MacEscapeChords.Forwards(eventType, keyCode));
    }

    [Fact]
    public void Off_macOS_nothing_is_installed()
    {
        Assert.False(MacEscapeChords.Install(isMacOS: false));
        Assert.False(MacEscapeChords.Installed);
    }

    /// <summary>On a Mac test runner Avalonia's view class is still absent (headless), so the answer is a quiet
    /// false. Skipped rather than passed on the others, where libobjc does not exist: a green run there must not
    /// read as this path having been exercised.</summary>
    [Fact]
    public void Without_the_view_class_install_answers_false_and_does_not_throw()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), "libobjc exists only on macOS.");
        Assert.False(MacEscapeChords.Install(isMacOS: true));
        Assert.False(MacEscapeChords.Installed);
    }
}
