// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Controls;
using Avalonia.Interactivity;
using LizTerm.App.Status;
using LizTerm.Core.Session;

namespace LizTerm.App.Views;

public partial class AboutWindow : Window
{
    /// <summary>Design-time only. A plausible located engine rather than a blank path calling itself bundled,
    /// which is the pairing <see cref="EngineSource.Unknown"/> exists to avoid.</summary>
    public AboutWindow() : this(AppVersion.Current, new EngineInfo("b3270", "4.5.6", "/path/to/b3270", EngineSource.Bundled), "") { }

    public AboutWindow(string version, EngineInfo engine, string overrideOrigin)
    {
        InitializeComponent();
        VersionText.Text = "Version " + version;
        EngineText.Text = StatusFormatter.Engine(engine, overrideOrigin);
        EnginePathText.Text = engine.Path;
        CopyrightText.Text = AppLicense.Notice;
        LicensesText.Text = AppLicense.All;
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
