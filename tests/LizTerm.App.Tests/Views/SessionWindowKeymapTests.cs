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
    private static (SessionWindow Window, FakeEmulatorSession Session, TerminalScreen Screen) Show(bool destructiveBackspace, KeymapViewModel? keymap)
    {
        var session = new FakeEmulatorSession
        {
            Profile = new SessionProfile { Name = "TSO", Host = "tk5.local", Port = 3270, DestructiveBackspace = destructiveBackspace },
        };
        var vm = new SessionViewModel(session, action => action(), new FakeTextClipboard());
        var window = new SessionWindow(MenuStyle.InWindow, isMacOS: false) { DataContext = vm };
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
}
