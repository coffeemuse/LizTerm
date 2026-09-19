// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Input;
using Avalonia.Input.Platform;
using LizTerm.App.Keyboard;

namespace LizTerm.App.Tests.Keyboard;

/// <summary>Every row of the refusal table (editable keymap spec §4), on a Windows-shaped platform (Fallback: Ctrl
/// everywhere) and a macOS-shaped one (Cmd everywhere, Ctrl+Insert still Copy).</summary>
public class KeymapPolicyTests
{
    private const KeyModifiers CtrlShift = KeyModifiers.Control | KeyModifiers.Shift;

    public static TheoryData<string, Key, KeyModifiers, string> Refusals => new()
    {
        { "windows", Key.C, KeyModifiers.Control, "LizTerm uses this for Copy" },
        { "windows", Key.V, KeyModifiers.Control, "LizTerm uses this for Paste" },
        { "windows", Key.A, KeyModifiers.Control, "LizTerm uses this for Select All" },
        { "windows", Key.F, KeyModifiers.Control, "LizTerm uses this for Find" },
        { "windows", Key.K, KeyModifiers.Control, "LizTerm uses this for Switch Session" },
        { "windows", Key.A, KeyModifiers.Meta, "The system sees Windows key shortcuts before the screen does" },
        { "mac", Key.C, KeyModifiers.Meta, "LizTerm uses this for Copy" },
        { "mac", Key.Insert, KeyModifiers.Control, "LizTerm uses this for Copy" },
        { "mac", Key.F, KeyModifiers.Meta, "LizTerm uses this for Find" },
        { "mac", Key.K, KeyModifiers.Meta, "LizTerm uses this for Switch Session" },
        { "mac", Key.OemComma, KeyModifiers.Meta, "The menu bar sees Cmd shortcuts before the screen does" },
        { "mac", Key.Q, KeyModifiers.Meta | KeyModifiers.Shift, "The menu bar sees Cmd shortcuts before the screen does" },
        { "windows", Key.A, KeyModifiers.None, "This would take away typing that character" },
        { "windows", Key.Q, KeyModifiers.Control | KeyModifiers.Alt, "AltGr types this character on some keyboards" },
        { "windows", Key.A, KeyModifiers.Shift, "This would take away typing that character" },
        { "windows", Key.D1, KeyModifiers.None, "This would take away typing that character" },
        { "windows", Key.Space, KeyModifiers.None, "This would take away typing that character" },
        { "windows", Key.OemOpenBrackets, KeyModifiers.Shift, "This would take away typing that character" },
        { "windows", Key.NumPad5, KeyModifiers.None, "This would take away typing that character" },
        { "mac", Key.OemMinus, KeyModifiers.None, "This would take away typing that character" },
        { "windows", Key.CapsLock, KeyModifiers.None, "Caps Lock, Num Lock and Scroll Lock change the keyboard's state and cannot be bound" },
        { "mac", Key.NumLock, KeyModifiers.Shift, "Caps Lock, Num Lock and Scroll Lock change the keyboard's state and cannot be bound" },
        { "windows", Key.Scroll, KeyModifiers.Control, "Caps Lock, Num Lock and Scroll Lock change the keyboard's state and cannot be bound" },
    };

    [Theory]
    [MemberData(nameof(Refusals))]
    public void Refuses_with_the_reason_the_tab_shows(string platform, Key key, KeyModifiers modifiers, string reason)
    {
        var verdict = KeymapPolicy.Check(new KeyChord(key, modifiers), Hotkeys(platform));

        Assert.Equal(new KeymapVerdict.Refused(reason), verdict);
    }

    public static TheoryData<string, Key, KeyModifiers> Allowed => new()
    {
        { "windows", Key.F1, KeyModifiers.None },
        { "windows", Key.Escape, KeyModifiers.None },
        { "windows", Key.Insert, KeyModifiers.None },
        { "windows", Key.D1, KeyModifiers.Alt },
        { "windows", Key.A, KeyModifiers.Alt },
        { "windows", Key.R, KeyModifiers.Control },
        { "windows", Key.OemOpenBrackets, KeyModifiers.Control },
        { "windows", Key.A, CtrlShift },
        { "mac", Key.C, KeyModifiers.Control },
        { "mac", Key.A, KeyModifiers.Control },
        { "mac", Key.F, KeyModifiers.Control },
        { "mac", Key.K, KeyModifiers.Control },
        { "mac", Key.Home, KeyModifiers.Control },
        { "mac", Key.Q, KeyModifiers.Control | KeyModifiers.Alt },
    };

    [Theory]
    [MemberData(nameof(Allowed))]
    public void Allows_the_rest(string platform, Key key, KeyModifiers modifiers)
    {
        Assert.Same(KeymapVerdict.Allowed.Instance, KeymapPolicy.Check(new KeyChord(key, modifiers), Hotkeys(platform)));
    }

    [Fact]
    public void A_tap_is_always_allowed()
    {
        Assert.Same(KeymapVerdict.Allowed.Instance, KeymapPolicy.Check(KeyChord.TapOf(Key.LeftCtrl), PlatformHotkeys.Fallback));
        Assert.Same(KeymapVerdict.Allowed.Instance, KeymapPolicy.Check(KeyChord.TapOf(Key.RightCtrl), PlatformHotkeys.MacOS));
    }

    [Fact]
    public void A_missing_platform_configuration_is_the_screens_fallback()
    {
        Assert.Same(PlatformHotkeys.Fallback, PlatformHotkeys.From(null));
    }

    [Fact]
    public void An_empty_platform_list_falls_back_to_Ctrl_as_the_screen_does()
    {
        // PlatformHotkeyConfiguration's constructors are all [PrivateApi]: public in IL, so
        // Activator.CreateInstance reaches one, but no external `new` binds to it. Its gesture lists
        // are then cleared by hand to reproduce a platform that answers nothing, the case From must
        // still cover.
        var configuration = Activator.CreateInstance<PlatformHotkeyConfiguration>();
        configuration.Copy = [];
        configuration.Paste = [];
        configuration.SelectAll = [];
        configuration.CommandModifiers = KeyModifiers.Control;

        var hotkeys = PlatformHotkeys.From(configuration);

        Assert.Equal(new KeymapVerdict.Refused("LizTerm uses this for Copy"),
            KeymapPolicy.Check(new KeyChord(Key.C, KeyModifiers.Control), hotkeys));
    }

    private static PlatformHotkeys Hotkeys(string platform) => platform == "mac" ? PlatformHotkeys.MacOS : PlatformHotkeys.Fallback;
}
