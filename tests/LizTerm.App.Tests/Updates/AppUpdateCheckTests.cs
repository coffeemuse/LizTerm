// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using LizTerm.App.Tests.Fakes;
using LizTerm.App.ViewModels;
using LizTerm.Core.Updates;

namespace LizTerm.App.Tests.Updates;

public class AppUpdateCheckTests
{
    private static App CurrentApp => (App)Application.Current!;

    [AvaloniaFact]
    public async Task Startup_check_does_nothing_when_automatic_checking_is_off()
    {
        var settings = new SettingsViewModel();
        settings.CheckForUpdatesAutomatically = false;
        var checker = new FakeReleaseChecker { Result = new ReleaseInfo("0.6.0", "https://example/release") };

        var window = await CurrentApp.CheckForUpdatesOnStartupAsync(checker, settings);

        Assert.Null(window);
    }

    [AvaloniaFact]
    public async Task Startup_check_shows_a_dialog_for_a_newer_unskipped_release()
    {
        var settings = new SettingsViewModel();
        var checker = new FakeReleaseChecker { Result = new ReleaseInfo("0.6.0", "https://example/release") };

        var window = await CurrentApp.CheckForUpdatesOnStartupAsync(checker, settings);

        Assert.NotNull(window);
        Assert.Equal($"LizTerm 0.6.0 is available. You have {AppVersion.Current}.",
            window!.FindControl<TextBlock>("MessageText")!.Text);
        window.Close();
    }

    [AvaloniaFact]
    public async Task Startup_check_stays_silent_for_the_version_the_user_already_skipped()
    {
        var settings = new SettingsViewModel();
        settings.SkippedUpdateVersion = "0.6.0";
        var checker = new FakeReleaseChecker { Result = new ReleaseInfo("0.6.0", "https://example/release") };

        var window = await CurrentApp.CheckForUpdatesOnStartupAsync(checker, settings);

        Assert.Null(window);
    }

    [AvaloniaFact]
    public async Task Startup_check_stays_silent_when_already_up_to_date()
    {
        var settings = new SettingsViewModel();
        var checker = new FakeReleaseChecker { Result = new ReleaseInfo(AppVersion.Current, "https://example/release") };

        var window = await CurrentApp.CheckForUpdatesOnStartupAsync(checker, settings);

        Assert.Null(window);
    }

    [AvaloniaFact]
    public async Task Startup_check_stays_silent_and_does_not_throw_when_the_checker_fails()
    {
        var settings = new SettingsViewModel();
        var checker = new FakeReleaseChecker { Exception = new HttpRequestException("no network") };

        var window = await CurrentApp.CheckForUpdatesOnStartupAsync(checker, settings);

        Assert.Null(window);
    }

    [AvaloniaFact]
    public async Task Manual_check_always_shows_a_dialog_even_for_a_previously_skipped_version()
    {
        var settings = new SettingsViewModel();
        settings.SkippedUpdateVersion = "0.6.0";
        var checker = new FakeReleaseChecker { Result = new ReleaseInfo("0.6.0", "https://example/release") };

        var window = await CurrentApp.CheckForUpdatesManuallyAsync(null, checker, settings);

        Assert.True(window.FindControl<Button>("DownloadButton")!.IsVisible);
        window.Close();
    }

    [AvaloniaFact]
    public async Task Manual_check_reports_up_to_date()
    {
        var settings = new SettingsViewModel();
        var checker = new FakeReleaseChecker { Result = new ReleaseInfo(AppVersion.Current, "https://example/release") };

        var window = await CurrentApp.CheckForUpdatesManuallyAsync(null, checker, settings);

        Assert.Equal($"You're up to date ({AppVersion.Current}).", window.FindControl<TextBlock>("MessageText")!.Text);
        window.Close();
    }

    [AvaloniaFact]
    public async Task The_app_shows_one_update_check_window_at_a_time()
    {
        var settings = new SettingsViewModel();
        var checker = new FakeReleaseChecker { Result = new ReleaseInfo("0.6.0", "https://example/release") };

        var first = await CurrentApp.CheckForUpdatesManuallyAsync(null, checker, settings);
        var again = await CurrentApp.CheckForUpdatesManuallyAsync(null, checker, settings);
        Assert.Same(first, again);

        first.Close();
        var third = await CurrentApp.CheckForUpdatesManuallyAsync(null, checker, settings);
        Assert.NotSame(first, third);
        third.Close();
    }

    [AvaloniaFact]
    public async Task Skip_writes_only_to_the_settings_object_the_call_was_given()
    {
        var settings = new SettingsViewModel();
        var checker = new FakeReleaseChecker { Result = new ReleaseInfo("0.6.0", "https://example/release") };

        var window = await CurrentApp.CheckForUpdatesManuallyAsync(null, checker, settings);
        window.FindControl<Button>("SkipButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.Equal("0.6.0", settings.SkippedUpdateVersion);
    }
}
