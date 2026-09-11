// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Controls;
using Avalonia.Interactivity;
using LizTerm.App.Bell;
using LizTerm.App.ViewModels;
using LizTerm.Core.Settings;

namespace LizTerm.App.Views;

/// <summary>The app-wide preferences, bound to the process's one SettingsViewModel. Modeless and one at a time;
/// App.ShowPreferences is the route (settings spec §5).</summary>
public partial class PreferencesWindow : Window
{
    /// <summary>Design-time only.</summary>
    public PreferencesWindow() : this(new SettingsViewModel()) { }

    public PreferencesWindow(SettingsViewModel settings) : this(settings, BellSupport.SystemAlertAvailableHere) { }

    /// <summary>The platform is an argument so a test can see the Linux shape of the window on any machine.</summary>
    internal PreferencesWindow(SettingsViewModel settings, bool systemAlertAvailable)
    {
        InitializeComponent();
        DataContext = settings;
        BellSoundSystemAlert.IsEnabled = systemAlertAvailable;
        BellSoundNote.IsVisible = !systemAlertAvailable;
    }

    private SettingsViewModel Settings => (SettingsViewModel)DataContext!;

    private void OnCrosshairNoneClick(object? sender, RoutedEventArgs e) => Settings.Crosshair = CrosshairMode.None;
    private void OnCrosshairHorizontalClick(object? sender, RoutedEventArgs e) => Settings.Crosshair = CrosshairMode.Horizontal;
    private void OnCrosshairVerticalClick(object? sender, RoutedEventArgs e) => Settings.Crosshair = CrosshairMode.Vertical;
    private void OnCrosshairBothClick(object? sender, RoutedEventArgs e) => Settings.Crosshair = CrosshairMode.Both;

    private void OnBellSoundNoneClick(object? sender, RoutedEventArgs e) => Settings.BellSound = BellSound.None;
    private void OnBellSoundSystemAlertClick(object? sender, RoutedEventArgs e) => Settings.BellSound = BellSound.SystemAlert;

    private void OnDoneClick(object? sender, RoutedEventArgs e) => Close();
}
