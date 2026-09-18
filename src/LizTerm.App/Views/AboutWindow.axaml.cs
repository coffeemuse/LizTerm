// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Controls;
using Avalonia.Interactivity;
using LizTerm.App.Dialogs;
using LizTerm.App.Status;
using LizTerm.Core.Session;

namespace LizTerm.App.Views;

public partial class AboutWindow : Window
{
    /// <summary>Design-time only. A plausible located engine rather than a blank path calling itself bundled,
    /// which is the pairing <see cref="EngineSource.Unknown"/> exists to avoid.</summary>
    public AboutWindow() : this(AppVersion.Current, AppVersion.Commit, new EngineInfo("b3270", "4.5.6", "/path/to/b3270", EngineSource.Bundled), "") { }

    /// <summary><paramref name="commit"/> is the build's commit, or null for a release build (see
    /// <see cref="AppVersion.Commit"/>). A build that has one is not a release, so the version wears -DEV: that
    /// marker says so on its own, and the hash after it says which build, for a bug report to quote.</summary>
    public AboutWindow(string version, string? commit, EngineInfo engine, string overrideOrigin)
    {
        InitializeComponent();
        VersionText.Text = commit is null ? $"Version {version}" : $"Version {version}-DEV ({commit})";
        EngineText.Text = StatusFormatter.Engine(engine, overrideOrigin);
        CopyrightText.Text = AppLicense.Copyright;
        LicensesText.Text = AppLicense.All;
    }

    /// <summary>Modal over About, so About cannot close from under the photo and the link cannot open a second one.</summary>
    private void OnDedicationClick(object? sender, RoutedEventArgs e) => _ = new LizWindow().ShowDialogAbove(this);

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
