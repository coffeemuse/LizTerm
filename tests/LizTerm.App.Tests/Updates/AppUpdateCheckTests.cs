// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using LizTerm.App.Tests.Fakes;
using LizTerm.App.ViewModels;
using LizTerm.App.Views;
using LizTerm.Core.Updates;

namespace LizTerm.App.Tests.Updates;

public class AppUpdateCheckTests
{
    private static App CurrentApp => (App)Application.Current!;

    /// <summary>Newer than this build whatever Directory.Build.props says. A literal stops being newer on the day the
    /// version reaches it, and the tests expecting a dialog then fail, or pass for the wrong reason.</summary>
    private static readonly string Newer = $"{Version.Parse(AppVersion.Current).Major + 1}.0.0";

    private static FakeReleaseChecker NewerRelease() => new() { Result = new ReleaseInfo(Newer, "https://example/release") };

    [AvaloniaFact]
    public async Task Startup_check_does_nothing_when_automatic_checking_is_off()
    {
        var settings = new SettingsViewModel();
        settings.CheckForUpdatesAutomatically = false;
        var checker = NewerRelease();

        var window = await CurrentApp.CheckForUpdatesOnStartupAsync(checker, settings);

        Assert.Null(window);
        // What the preference promises is that GitHub is not asked, not merely that nothing is shown.
        Assert.Equal(0, checker.Calls);
    }

    [AvaloniaFact]
    public async Task Startup_check_stays_silent_when_automatic_checking_is_turned_off_while_it_waits()
    {
        var settings = new SettingsViewModel();
        var checker = NewerRelease();
        checker.Gate = new TaskCompletionSource();

        var check = CurrentApp.CheckForUpdatesOnStartupAsync(checker, settings);
        settings.CheckForUpdatesAutomatically = false;
        checker.Gate.SetResult();

        Assert.Null(await check);
    }

    [AvaloniaFact]
    public async Task Startup_check_shows_a_dialog_for_a_newer_unskipped_release()
    {
        var settings = new SettingsViewModel();

        var window = await CurrentApp.CheckForUpdatesOnStartupAsync(NewerRelease(), settings);

        Assert.NotNull(window);
        Assert.Equal($"LizTerm {Newer} is available. You have {AppVersion.Current}.",
            window!.FindControl<TextBlock>("MessageText")!.Text);
        window.Close();
    }

    [AvaloniaFact]
    public async Task Startup_check_stays_silent_for_the_version_the_user_already_skipped()
    {
        var settings = new SettingsViewModel();
        settings.SkippedUpdateVersion = Newer;

        var window = await CurrentApp.CheckForUpdatesOnStartupAsync(NewerRelease(), settings);

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
        settings.SkippedUpdateVersion = Newer;

        var window = await CurrentApp.CheckForUpdatesManuallyAsync(null, NewerRelease(), settings);

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

    /// <summary>Help's own item passes its session window, so the result is modal over it — the branch every real
    /// check takes, which a null owner never reaches here, since the headless lifetime has no windows to fall back
    /// on. About's test shape.</summary>
    [AvaloniaFact]
    public void The_manual_check_is_modal_over_the_window_that_asked()
    {
        var owner = new Window();
        owner.Show();

        _ = CurrentApp.CheckForUpdatesManuallyAsync(owner, NewerRelease(), new SettingsViewModel());

        var dialog = Assert.IsType<UpdateCheckWindow>(Assert.Single(owner.OwnedWindows));
        dialog.Close();
        Assert.Empty(owner.OwnedWindows);
    }

    /// <summary>The owner is chosen before the request goes out. Closing it while GitHub is slow used to make
    /// ShowDialog throw and leave the one-at-a-time slot holding a window that never opened, so no later check could
    /// show anything until LizTerm restarted.</summary>
    [AvaloniaFact]
    public async Task Closing_the_asking_window_during_a_check_still_shows_the_result_and_later_checks_work()
    {
        var settings = new SettingsViewModel();
        var checker = NewerRelease();
        checker.Gate = new TaskCompletionSource();
        var owner = new Window();
        owner.Show();

        var check = CurrentApp.CheckForUpdatesManuallyAsync(owner, checker, settings);
        owner.Close();
        checker.Gate.SetResult();
        var window = await check;

        Assert.True(window.IsVisible);
        window.Close();
        var next = await CurrentApp.CheckForUpdatesManuallyAsync(null, NewerRelease(), settings);
        Assert.NotSame(window, next);
        Assert.True(next.IsVisible);
        next.Close();
    }

    [AvaloniaFact]
    public async Task The_app_shows_one_update_check_window_at_a_time_and_does_not_ask_again_for_it()
    {
        var settings = new SettingsViewModel();
        var checker = NewerRelease();

        var first = await CurrentApp.CheckForUpdatesManuallyAsync(null, checker, settings);
        var again = await CurrentApp.CheckForUpdatesManuallyAsync(null, checker, settings);
        Assert.Same(first, again);
        Assert.Equal(1, checker.Calls);

        first.Close();
        var third = await CurrentApp.CheckForUpdatesManuallyAsync(null, checker, settings);
        Assert.NotSame(first, third);
        Assert.Equal(2, checker.Calls);
        third.Close();
    }

    /// <summary>A click during the startup check, or a second click while GitHub is slow, joins the request already
    /// out instead of sending another and opening a second dialog when it lands.</summary>
    [AvaloniaFact]
    public async Task Checks_that_overlap_share_one_request_and_one_window()
    {
        var settings = new SettingsViewModel();
        var checker = NewerRelease();
        checker.Gate = new TaskCompletionSource();

        var startup = CurrentApp.CheckForUpdatesOnStartupAsync(checker, settings);
        var manual = CurrentApp.CheckForUpdatesManuallyAsync(null, checker, settings);
        checker.Gate.SetResult();

        var shown = await manual;
        Assert.Same(shown, await startup);
        Assert.Equal(1, checker.Calls);
        shown.Close();
    }

    [AvaloniaFact]
    public async Task Skip_writes_only_to_the_settings_object_the_call_was_given()
    {
        var settings = new SettingsViewModel();

        var window = await CurrentApp.CheckForUpdatesManuallyAsync(null, NewerRelease(), settings);
        window.FindControl<Button>("SkipButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.Equal(Newer, settings.SkippedUpdateVersion);
    }

    /// <summary>App's own Download wiring: the dialog's opener on itself. The headless launcher opens nothing, so the
    /// page it was handed comes back in the fallback line.</summary>
    [AvaloniaFact]
    public async Task Download_hands_the_release_page_to_the_platform()
    {
        var page = $"{ProjectLinks.Releases}/tag/v{Newer}";
        var checker = new FakeReleaseChecker { Result = new ReleaseInfo(Newer, page) };
        var window = await CurrentApp.CheckForUpdatesManuallyAsync(null, checker, new SettingsViewModel());

        window.FindControl<Button>("DownloadButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.Equal($"Could not open a browser. The page is at {page}.", window.FindControl<TextBlock>("FallbackText")!.Text);
        window.Close();
    }
}
