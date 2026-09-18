// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Threading;
using LizTerm.App.Startup;

namespace LizTerm.App.Views;

/// <summary>Spec 7: closes on the first click or key, never before the minimum, and at the maximum on its own.</summary>
public partial class SplashWindow : Window
{
    private readonly SplashTiming _timing;
    /// <summary>Monotonic and started at Opened, so neither a slow cold start before the window appears nor a
    /// system clock step can make the splash close the moment it is shown.</summary>
    private readonly Stopwatch _shownFor = new();
    private readonly DispatcherTimer _timer = new();
    private bool _dismissed;

    public SplashWindow() : this(AppVersion.Display, SplashTiming.Default) { }

    public SplashWindow(string version, SplashTiming timing)
    {
        InitializeComponent();
        _timing = timing;
        VersionText.Text = "Version " + version;
        CopyrightText.Text = AppLicense.Notice;
        _timer.Tick += (_, _) => { _timer.Stop(); Close(); };
        Opened += (_, _) => { _shownFor.Restart(); Arm(null); };
        PointerPressed += (_, _) => Dismiss();
        KeyDown += (_, _) => Dismiss();
        Closed += (_, _) => _timer.Stop();
    }

    private void Dismiss()
    {
        if (_dismissed) return;
        _dismissed = true;
        Arm(_shownFor.Elapsed);
    }

    private void Arm(TimeSpan? dismissRequestedAt)
    {
        _timer.Stop();
        var delay = _timing.CloseAfter(_shownFor.Elapsed, dismissRequestedAt);
        if (delay <= TimeSpan.Zero)
        {
            Close();
            return;
        }
        _timer.Interval = delay;
        _timer.Start();
    }
}
