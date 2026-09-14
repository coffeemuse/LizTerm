// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using LizTerm.App.Updates;
using LizTerm.App.Views;

namespace LizTerm.App.Tests.Views;

public class UpdateCheckWindowTests
{
    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    [AvaloniaFact]
    public void A_newer_release_shows_its_version_and_three_buttons()
    {
        var window = new UpdateCheckWindow(new UpdateCheckResult.NewerAvailable("0.6.0", "https://example/release"), "0.5.2", null, null);
        window.Show();

        Assert.Equal("LizTerm 0.6.0 is available. You have 0.5.2.", window.FindControl<TextBlock>("MessageText")!.Text);
        Assert.True(window.FindControl<Button>("DownloadButton")!.IsVisible);
        Assert.True(window.FindControl<Button>("RemindButton")!.IsVisible);
        Assert.True(window.FindControl<Button>("SkipButton")!.IsVisible);
        Assert.False(window.FindControl<Button>("OkButton")!.IsVisible);
    }

    [AvaloniaFact]
    public void Up_to_date_shows_the_current_version_and_only_ok()
    {
        var window = new UpdateCheckWindow(new UpdateCheckResult.UpToDate(), "0.5.2", null, null);
        window.Show();

        Assert.Equal("You're up to date (0.5.2).", window.FindControl<TextBlock>("MessageText")!.Text);
        Assert.True(window.FindControl<Button>("OkButton")!.IsVisible);
        Assert.False(window.FindControl<Button>("DownloadButton")!.IsVisible);
    }

    [AvaloniaFact]
    public void A_failure_shows_its_reason_and_only_ok()
    {
        var window = new UpdateCheckWindow(new UpdateCheckResult.Failed("Could not reach GitHub."), "0.5.2", null, null);
        window.Show();

        Assert.Equal("Couldn't check for updates: Could not reach GitHub.", window.FindControl<TextBlock>("MessageText")!.Text);
        Assert.True(window.FindControl<Button>("OkButton")!.IsVisible);
    }

    [AvaloniaFact]
    public void Download_closes_on_success_without_showing_the_fallback_text()
    {
        var opened = new List<string>();
        var window = new UpdateCheckWindow(new UpdateCheckResult.NewerAvailable("0.6.0", "https://example/release"), "0.5.2",
            onDownload: url => { opened.Add(url); return Task.FromResult(true); }, onSkip: null);
        window.Show();
        var closed = false;
        window.Closed += (_, _) => closed = true;

        Click(window.FindControl<Button>("DownloadButton")!);

        Assert.Equal(["https://example/release"], opened);
        Assert.True(closed);
    }

    [AvaloniaFact]
    public void Download_stays_open_and_names_the_url_when_opening_fails()
    {
        var window = new UpdateCheckWindow(new UpdateCheckResult.NewerAvailable("0.6.0", "https://example/release"), "0.5.2",
            onDownload: _ => Task.FromResult(false), onSkip: null);
        window.Show();
        var closed = false;
        window.Closed += (_, _) => closed = true;

        Click(window.FindControl<Button>("DownloadButton")!);

        Assert.False(closed);
        var fallback = window.FindControl<TextBlock>("FallbackText")!;
        Assert.True(fallback.IsVisible);
        Assert.Equal("Could not open a browser. The release is at https://example/release", fallback.Text);
    }

    [AvaloniaFact]
    public void Skip_reports_the_version_and_closes()
    {
        var skipped = new List<string>();
        var window = new UpdateCheckWindow(new UpdateCheckResult.NewerAvailable("0.6.0", "https://example/release"), "0.5.2",
            onDownload: null, onSkip: skipped.Add);
        window.Show();
        var closed = false;
        window.Closed += (_, _) => closed = true;

        Click(window.FindControl<Button>("SkipButton")!);

        Assert.Equal(["0.6.0"], skipped);
        Assert.True(closed);
    }

    [AvaloniaFact]
    public void Remind_me_later_closes_without_reporting_anything()
    {
        var window = new UpdateCheckWindow(new UpdateCheckResult.NewerAvailable("0.6.0", "https://example/release"), "0.5.2", null, null);
        window.Show();
        var closed = false;
        window.Closed += (_, _) => closed = true;

        Click(window.FindControl<Button>("RemindButton")!);

        Assert.True(closed);
    }

    [AvaloniaFact]
    public void Ok_closes_the_up_to_date_window()
    {
        var window = new UpdateCheckWindow(new UpdateCheckResult.UpToDate(), "0.5.2", null, null);
        window.Show();
        var closed = false;
        window.Closed += (_, _) => closed = true;

        Click(window.FindControl<Button>("OkButton")!);

        Assert.True(closed);
    }
}
