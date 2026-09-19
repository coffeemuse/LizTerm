// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using LizTerm.App.Keyboard;
using LizTerm.App.ViewModels;
using LizTerm.Core.Settings;

namespace LizTerm.App.Views;

/// <summary>The app-wide preferences, bound to the process's one SettingsViewModel. Modeless and one at a time;
/// App.ShowPreferences is the route (settings spec §5).</summary>
public partial class PreferencesWindow : Window
{
    private readonly KeymapEditorViewModel _keyboard;

    /// <summary>Design-time only, in the full shape.</summary>
    public PreferencesWindow() : this(new SettingsViewModel(), new KeymapViewModel(), systemAlertAvailable: true, menuStyleChoosable: true) { }

    /// <summary>Whether the system alert can ring here, and whether the menu style is a choice at all, are both
    /// arguments (App passes its ringer's CanRing and the platform), so a test can see every shape of the window
    /// on any machine. The keymap is the process's one (App.Keymap) or, in a test, an in-memory one.</summary>
    internal PreferencesWindow(SettingsViewModel settings, KeymapViewModel keymap, bool systemAlertAvailable, bool menuStyleChoosable)
    {
        InitializeComponent();
        DataContext = settings;
        BellSoundSystemAlert.IsEnabled = systemAlertAvailable;
        BellSoundNote.IsVisible = !systemAlertAvailable;
        MenuStyleGroup.IsVisible = menuStyleChoosable;
        // The reserved gestures are asked at each capture, as TerminalScreen asks at each key, so they are the
        // platform's answer for this window and never a copy taken before it had a platform.
        _keyboard = new KeymapEditorViewModel(keymap, () => PlatformHotkeys.From(this.GetPlatformSettings()?.HotkeyConfiguration));
        KeyboardPanel.DataContext = _keyboard;
    }

    /// <summary>The editor listens to the process's keymap for as long as the tab exists.</summary>
    protected override void OnClosed(EventArgs e)
    {
        _keyboard.Dispose();
        base.OnClosed(e);
    }

    private SettingsViewModel Settings => (SettingsViewModel)DataContext!;

    private void OnCrosshairNoneClick(object? sender, RoutedEventArgs e) => Settings.Crosshair = CrosshairMode.None;
    private void OnCrosshairHorizontalClick(object? sender, RoutedEventArgs e) => Settings.Crosshair = CrosshairMode.Horizontal;
    private void OnCrosshairVerticalClick(object? sender, RoutedEventArgs e) => Settings.Crosshair = CrosshairMode.Vertical;
    private void OnCrosshairBothClick(object? sender, RoutedEventArgs e) => Settings.Crosshair = CrosshairMode.Both;

    private void OnBellSoundNoneClick(object? sender, RoutedEventArgs e) => Settings.BellSound = BellSound.None;
    private void OnBellSoundSystemAlertClick(object? sender, RoutedEventArgs e) => Settings.BellSound = BellSound.SystemAlert;

    private void OnKeypadBottomClick(object? sender, RoutedEventArgs e) => Settings.KeypadDock = KeypadDock.Bottom;
    private void OnKeypadRightClick(object? sender, RoutedEventArgs e) => Settings.KeypadDock = KeypadDock.Right;

    private void OnMenuStyleAutoClick(object? sender, RoutedEventArgs e) => Settings.MenuStyle = MenuStyle.Auto;
    private void OnMenuStyleNativeClick(object? sender, RoutedEventArgs e) => Settings.MenuStyle = MenuStyle.Native;
    private void OnMenuStyleInWindowClick(object? sender, RoutedEventArgs e) => Settings.MenuStyle = MenuStyle.InWindow;
    private void OnMenuStyleBothClick(object? sender, RoutedEventArgs e) => Settings.MenuStyle = MenuStyle.Both;

    private void OnDoneClick(object? sender, RoutedEventArgs e) => Close();
}
