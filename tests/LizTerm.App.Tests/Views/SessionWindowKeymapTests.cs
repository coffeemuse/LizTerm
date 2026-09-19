// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using LizTerm.App.Controls;
using LizTerm.App.Keyboard;
using LizTerm.App.Menus;
using LizTerm.App.Tests.Fakes;
using LizTerm.App.ViewModels;
using LizTerm.App.Views;
using LizTerm.Core.Session;
using LizTerm.Core.Settings;

namespace LizTerm.App.Tests.Views;

/// <summary>Editable keymap spec §5.2: the window composes the profile's Backspace choice under the process's
/// keymap and pushes it to the screen and the keypad, on open and on every change.</summary>
public class SessionWindowKeymapTests
{
    private static (SessionWindow Window, FakeEmulatorSession Session, TerminalScreen Screen) Show(
        bool destructiveBackspace, KeymapViewModel? keymap, bool isMacOS = false)
    {
        var session = new FakeEmulatorSession
        {
            Profile = new SessionProfile { Name = "TSO", Host = "tk5.local", Port = 3270, DestructiveBackspace = destructiveBackspace },
        };
        var vm = new SessionViewModel(session, action => action(), new FakeTextClipboard());
        var window = new SessionWindow(MenuStyle.InWindow, isMacOS) { DataContext = vm };
        if (keymap is not null) window.AttachKeymap(keymap);
        window.Show();
        var screen = window.FindControl<TerminalScreen>("Screen")!;
        screen.Focus();
        return (window, session, screen);
    }

    private static Button KeypadButton(SessionWindow window, TerminalKey key)
    {
        window.UpdateLayout();
        return window.FindControl<Keypad>("KeypadPanel")!.GetVisualDescendants().OfType<Button>().First(b => Equals(b.Tag, key));
    }

    /// <summary>A Keys item by the key it sends, never by header, through the seam that reaches a native item
    /// stashed under InWindow (the menu notes in src/LizTerm.App/CLAUDE.md say why a MenuLookup cannot).</summary>
    private static (NativeMenuItem Native, MenuItem Classic) KeysItems(SessionWindow window, TerminalKey key) => window.KeysRow(key);

    [AvaloniaFact]
    public void A_window_without_a_keymap_follows_the_profiles_Backspace_choice()
    {
        var (window, session, _) = Show(destructiveBackspace: false, keymap: null);

        window.KeyPressQwerty(PhysicalKey.Backspace, RawInputModifiers.None);

        Assert.Equal(["key:Backspace"], session.Calls);
    }

    [AvaloniaFact]
    public void A_rebind_reaches_the_screen_and_the_keypad_tooltip()
    {
        var keymap = new KeymapViewModel();
        var (window, session, _) = Show(destructiveBackspace: true, keymap);
        // The keypad builds its buttons on the first time it is actually shown (Controls/Keypad.axaml.cs), and it
        // is hidden by default (keypad spec §2.1), so the tooltip assertions below need it made visible first,
        // the same way every other keypad test does (see SessionWindowTests).
        ((SessionViewModel)window.DataContext!).Settings.Keypad = true;
        Assert.DoesNotContain("Home", (string?)ToolTip.GetTip(KeypadButton(window, TerminalKey.PA1)) ?? "");

        keymap.Bind(new KeyChord(Key.Home, KeyModifiers.Control), new KeymapAction.SendKey(TerminalKey.PA1));
        window.KeyPressQwerty(PhysicalKey.Home, RawInputModifiers.Control);
        window.KeyPressQwerty(PhysicalKey.Backspace, RawInputModifiers.None);

        Assert.Equal(["key:PA1", "key:Erase"], session.Calls);
        Assert.Contains("Home", (string)ToolTip.GetTip(KeypadButton(window, TerminalKey.PA1))!);
        Assert.DoesNotContain("Home", (string)ToolTip.GetTip(KeypadButton(window, TerminalKey.PA2))!);
    }

    [AvaloniaFact]
    public void A_keymap_attached_before_the_data_context_still_composes_with_the_profile()
    {
        var keymap = new KeymapViewModel();
        keymap.Bind(new KeyChord(Key.Home, KeyModifiers.Control), new KeymapAction.SendKey(TerminalKey.PA1));
        var session = new FakeEmulatorSession
        {
            Profile = new SessionProfile { Name = "TSO", Host = "tk5.local", Port = 3270, DestructiveBackspace = false },
        };
        var window = new SessionWindow(MenuStyle.InWindow, isMacOS: false);
        window.AttachKeymap(keymap);
        window.DataContext = new SessionViewModel(session, action => action(), new FakeTextClipboard());
        window.Show();
        window.FindControl<TerminalScreen>("Screen")!.Focus();

        window.KeyPressQwerty(PhysicalKey.Home, RawInputModifiers.Control);
        window.KeyPressQwerty(PhysicalKey.Backspace, RawInputModifiers.None);

        Assert.Equal(["key:PA1", "key:Backspace"], session.Calls);
    }

    [AvaloniaFact]
    public void A_closed_window_no_longer_listens()
    {
        var keymap = new KeymapViewModel();
        var (window, _, screen) = Show(destructiveBackspace: true, keymap);
        window.Close();

        keymap.Bind(new KeyChord(Key.Home, KeyModifiers.Control), new KeymapAction.SendKey(TerminalKey.PA1));

        Assert.True(screen.Keymap.TryMap(new KeyChord(Key.Home, KeyModifiers.Control), out var key));
        Assert.Equal(TerminalKey.PA2, key);
    }

    /// <summary>Editable keymap spec §6.2 and §7.2: a rebind reaches the Keys menu's header text on both renderers.
    /// The window is InWindow, so the native top-level items are stashed out of the menu while this runs, and the
    /// Keys items still have to follow, because a later switch to Native puts those same objects back.</summary>
    [AvaloniaFact]
    public void A_rebind_reaches_the_Keys_menu_headers_on_both_menus()
    {
        var keymap = new KeymapViewModel();
        var (window, _, screen) = Show(destructiveBackspace: true, keymap);
        var (native, classic) = KeysItems(window, TerminalKey.PA1);
        Assert.Equal("PA1  " + KeymapHints.Describe(screen.Keymap, TerminalKey.PA1), native.Header);
        Assert.Equal(native.Header, classic.Header as string);
        Assert.DoesNotContain("F9", native.Header);

        keymap.Bind(new KeyChord(Key.F9, KeyModifiers.Alt), new KeymapAction.SendKey(TerminalKey.PA1));

        Assert.Equal("PA1  " + KeymapHints.Describe(screen.Keymap, TerminalKey.PA1), native.Header);
        Assert.Contains("F9", native.Header);
        Assert.Equal(native.Header, classic.Header as string);
        Assert.Null(native.Gesture);
        Assert.Null(classic.InputGesture);
    }

    /// <summary>The other half of the InWindow guarantee: a rebind made while the native items were stashed is what
    /// the exported menu shows once a style change puts them back, read through the menu root as the platform
    /// would, not through the seam.</summary>
    [AvaloniaFact]
    public void A_rebind_made_under_InWindow_is_on_the_exported_Keys_menu_after_a_switch_to_Native()
    {
        var keymap = new KeymapViewModel();
        var (window, _, screen) = Show(destructiveBackspace: true, keymap, isMacOS: true);
        Assert.Empty(NativeMenu.GetMenu(window)!.Items);
        keymap.Bind(new KeyChord(Key.F9, KeyModifiers.Alt), new KeymapAction.SendKey(TerminalKey.PA1));

        ((SessionViewModel)window.DataContext!).Settings.MenuStyle = MenuStyle.Native;

        var exported = MenuLookup.Item(NativeMenu.GetMenu(window), "_Keys")!.Menu!.Items.OfType<NativeMenuItem>()
            .Single(item => Equals(item.CommandParameter, TerminalKey.PA1));
        Assert.Equal("PA1  " + KeymapHints.Describe(screen.Keymap, TerminalKey.PA1), exported.Header);
        Assert.Contains("F9", exported.Header);
        Assert.Null(exported.Gesture);
    }

    /// <summary>A key the user has taken every chord away from keeps its bare name, on both menus, and gets its hint
    /// back on Reset.</summary>
    [AvaloniaFact]
    public void A_key_left_with_no_chord_keeps_its_bare_name_on_the_Keys_menu()
    {
        var keymap = new KeymapViewModel();
        var (window, _, _) = Show(destructiveBackspace: true, keymap);
        var (native, classic) = KeysItems(window, TerminalKey.PA3);
        Assert.StartsWith("PA3  ", native.Header);

        keymap.Unbind(new KeyChord(Key.D3, KeyModifiers.Alt));
        keymap.Unbind(new KeyChord(Key.PageUp, KeyModifiers.Control));

        Assert.Equal("PA3", native.Header);
        Assert.Equal("PA3", classic.Header);

        keymap.ResetToDefaults();

        Assert.StartsWith("PA3  ", native.Header);
        Assert.Equal(native.Header, classic.Header as string);
    }
}
