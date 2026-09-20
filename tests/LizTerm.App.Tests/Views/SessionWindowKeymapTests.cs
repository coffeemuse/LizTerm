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
        bool destructiveBackspace, KeymapViewModel? keymap, bool isMacOS = false, bool nativeGestures = false)
    {
        var session = new FakeEmulatorSession
        {
            Profile = new SessionProfile { Name = "TSO", Host = "tk5.local", Port = 3270, DestructiveBackspace = destructiveBackspace },
        };
        var vm = new SessionViewModel(session, action => action(), new FakeTextClipboard());
        var window = new SessionWindow(MenuStyle.InWindow, isMacOS) { NativeGesturesAllowed = nativeGestures, DataContext = vm };
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

    /// <summary>A Keys item by the key it sends, through the seam that reaches a native item stashed under InWindow
    /// (the menu notes in src/LizTerm.App/CLAUDE.md say why a MenuLookup cannot).</summary>
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

    private static readonly KeyGesture Alt1 = new(Key.D1, KeyModifiers.Alt);

    /// <summary>Keys menu shortcuts spec §3.3: the chord is the native item's Gesture and the classic item's
    /// display-only InputGesture, the header is the bare name, and a rebind that sorts first replaces it on both.
    /// The window is InWindow, so the native top-level items are stashed out of the menu while this runs, and the
    /// Keys items still have to follow, because a later switch to Native puts those same objects back.</summary>
    [AvaloniaFact]
    public void A_rebind_reaches_the_Keys_menu_shortcut_on_both_menus()
    {
        var keymap = new KeymapViewModel();
        var (window, _, _) = Show(destructiveBackspace: true, keymap, nativeGestures: true);
        var (native, classic) = KeysItems(window, TerminalKey.PA1);
        Assert.Equal("PA1", native.Header);
        Assert.Equal("PA1", classic.Header);
        Assert.Equal(Alt1, native.Gesture);
        Assert.Equal(Alt1, classic.InputGesture);

        keymap.Bind(new KeyChord(Key.F9), new KeymapAction.SendKey(TerminalKey.PA1));

        Assert.Equal(new KeyGesture(Key.F9), native.Gesture);
        Assert.Equal(new KeyGesture(Key.F9), classic.InputGesture);
        Assert.Equal("PA1", native.Header);
    }

    /// <summary>Without the override installed a native gesture would be a key equivalent that eats the key, so the
    /// native item gets none; the classic InputGesture dispatches nothing and is always shown.</summary>
    [AvaloniaFact]
    public void Without_the_override_the_native_item_has_no_gesture_and_the_classic_one_still_does()
    {
        var (window, _, _) = Show(destructiveBackspace: true, new KeymapViewModel(), nativeGestures: false);
        var (native, classic) = KeysItems(window, TerminalKey.PA1);

        Assert.Null(native.Gesture);
        Assert.Equal(Alt1, classic.InputGesture);
    }

    /// <summary>The platform decides which chord: Apple keyboards have no Pause and no Insert (spec §3.2).</summary>
    [AvaloniaFact]
    public void The_chord_shown_follows_the_platform()
    {
        var (mac, _, _) = Show(destructiveBackspace: true, new KeymapViewModel(), isMacOS: true);
        var (other, _, _) = Show(destructiveBackspace: true, new KeymapViewModel(), isMacOS: false);

        Assert.Equal(new KeyGesture(Key.Escape, KeyModifiers.Control), KeysItems(mac, TerminalKey.Clear).Classic.InputGesture);
        Assert.Equal(new KeyGesture(Key.Pause), KeysItems(other, TerminalKey.Clear).Classic.InputGesture);
        Assert.Equal(new KeyGesture(Key.I, KeyModifiers.Control), KeysItems(mac, TerminalKey.Insert).Classic.InputGesture);
        Assert.Equal(new KeyGesture(Key.Insert), KeysItems(other, TerminalKey.Insert).Classic.InputGesture);
        Assert.Equal(new KeyGesture(Key.F1, KeyModifiers.Shift), KeysItems(mac, TerminalKey.PF13).Classic.InputGesture);
    }

    /// <summary>The other half of the InWindow guarantee: a rebind made while the native items were stashed is what
    /// the exported menu shows once a style change puts them back, read through the menu root as the platform
    /// would, not through the seam.</summary>
    [AvaloniaFact]
    public void A_rebind_made_under_InWindow_is_on_the_exported_Keys_menu_after_a_switch_to_Native()
    {
        var keymap = new KeymapViewModel();
        var (window, _, _) = Show(destructiveBackspace: true, keymap, isMacOS: true, nativeGestures: true);
        Assert.Empty(NativeMenu.GetMenu(window)!.Items);
        keymap.Bind(new KeyChord(Key.F9), new KeymapAction.SendKey(TerminalKey.PA1));

        ((SessionViewModel)window.DataContext!).Settings.MenuStyle = MenuStyle.Native;

        var exported = MenuLookup.Item(NativeMenu.GetMenu(window), "_Keys")!.Menu!.Items.OfType<NativeMenuItem>()
            .Single(item => Equals(item.CommandParameter, TerminalKey.PA1));
        Assert.Equal(new KeyGesture(Key.F9), exported.Gesture);
        Assert.Equal("PA1", exported.Header);
    }

    /// <summary>A key the user has taken every chord away from shows no shortcut, on both menus, and gets it back
    /// on Reset.</summary>
    [AvaloniaFact]
    public void A_key_left_with_no_chord_shows_no_shortcut_on_the_Keys_menu()
    {
        var keymap = new KeymapViewModel();
        var (window, _, _) = Show(destructiveBackspace: true, keymap, nativeGestures: true);
        var (native, classic) = KeysItems(window, TerminalKey.PA3);
        Assert.NotNull(native.Gesture);

        keymap.Unbind(new KeyChord(Key.D3, KeyModifiers.Alt));
        keymap.Unbind(new KeyChord(Key.PageUp, KeyModifiers.Control));

        Assert.Null(native.Gesture);
        Assert.Null(classic.InputGesture);
        Assert.Equal("PA3", native.Header);

        keymap.ResetToDefaults();

        Assert.Equal(new KeyGesture(Key.D3, KeyModifiers.Alt), native.Gesture);
        Assert.Equal(native.Gesture, classic.InputGesture);
    }
}
