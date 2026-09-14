// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using LizTerm.App.Tests.Fakes;
using LizTerm.App.Updates;
using LizTerm.App.Views;

namespace LizTerm.App.Tests.Views;

public class UpdateCheckWindowTests
{
    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    /// <summary>Every window here records both of its outputs, so a test that says nothing was opened or skipped
    /// can actually fail.</summary>
    private static (UpdateCheckWindow Window, FakeUriOpener Opener, List<string> Skipped) Show(UpdateCheckResult result)
    {
        var opener = new FakeUriOpener();
        var skipped = new List<string>();
        var window = new UpdateCheckWindow(result, "0.5.2", skipped.Add, opener);
        window.Show();
        return (window, opener, skipped);
    }

    private static (UpdateCheckWindow Window, FakeUriOpener Opener, List<string> Skipped) ShowNewer() =>
        Show(new UpdateCheckResult.NewerAvailable("0.6.0", "https://example/release"));

    private static Func<bool> ClosedFlag(Window window)
    {
        var closed = false;
        window.Closed += (_, _) => closed = true;
        return () => closed;
    }

    private static void AssertButtons(Window window, bool download, bool remind, bool skip, bool ok)
    {
        Assert.Equal(download, window.FindControl<Button>("DownloadButton")!.IsVisible);
        Assert.Equal(remind, window.FindControl<Button>("RemindButton")!.IsVisible);
        Assert.Equal(skip, window.FindControl<Button>("SkipButton")!.IsVisible);
        Assert.Equal(ok, window.FindControl<Button>("OkButton")!.IsVisible);
    }

    [AvaloniaFact]
    public void A_newer_release_shows_its_version_and_three_buttons()
    {
        var (window, _, _) = ShowNewer();

        Assert.Equal("LizTerm 0.6.0 is available. You have 0.5.2.", window.FindControl<TextBlock>("MessageText")!.Text);
        AssertButtons(window, download: true, remind: true, skip: true, ok: false);
    }

    [AvaloniaFact]
    public void Up_to_date_shows_the_current_version_and_only_ok()
    {
        var (window, _, _) = Show(new UpdateCheckResult.UpToDate());

        Assert.Equal("You're up to date (0.5.2).", window.FindControl<TextBlock>("MessageText")!.Text);
        AssertButtons(window, download: false, remind: false, skip: false, ok: true);
    }

    [AvaloniaFact]
    public void A_failure_shows_its_reason_and_only_ok()
    {
        var (window, _, _) = Show(new UpdateCheckResult.Failed("Could not reach GitHub."));

        Assert.Equal("Couldn't check for updates: Could not reach GitHub.", window.FindControl<TextBlock>("MessageText")!.Text);
        AssertButtons(window, download: false, remind: false, skip: false, ok: true);
    }

    [AvaloniaFact]
    public void Download_closes_on_success_without_showing_the_fallback_text()
    {
        var (window, opener, skipped) = ShowNewer();
        var closed = ClosedFlag(window);

        Click(window.FindControl<Button>("DownloadButton")!);

        Assert.Equal(["https://example/release"], opener.Opened);
        Assert.Empty(skipped);
        Assert.True(closed());
        Assert.False(window.FindControl<TextBlock>("FallbackText")!.IsVisible);
    }

    [AvaloniaFact]
    public void Download_stays_open_and_names_the_url_when_opening_fails()
    {
        var (window, opener, _) = ShowNewer();
        opener.Result = false;
        var closed = ClosedFlag(window);

        Click(window.FindControl<Button>("DownloadButton")!);

        Assert.False(closed());
        var fallback = window.FindControl<TextBlock>("FallbackText")!;
        Assert.True(fallback.IsVisible);
        Assert.Equal("Could not open a browser. The page is at https://example/release.", fallback.Text);
    }

    /// <summary>OnDownloadClick is async void, so an exception escaping it would never come out of the click: it
    /// would be posted to the dispatcher. That is where this looks for one.</summary>
    [AvaloniaFact]
    public void Download_stays_open_and_names_the_url_when_opening_throws()
    {
        var (window, opener, _) = ShowNewer();
        opener.Exception = new InvalidOperationException("no browser");
        var closed = ClosedFlag(window);
        Exception? unhandled = null;
        void Catch(object? sender, DispatcherUnhandledExceptionEventArgs e)
        {
            unhandled = e.Exception;
            e.Handled = true;
        }

        Dispatcher.UIThread.UnhandledException += Catch;
        try
        {
            Click(window.FindControl<Button>("DownloadButton")!);
            Dispatcher.UIThread.RunJobs();
        }
        finally
        {
            Dispatcher.UIThread.UnhandledException -= Catch;
        }

        Assert.Null(unhandled);
        Assert.False(closed());
        var fallback = window.FindControl<TextBlock>("FallbackText")!;
        Assert.True(fallback.IsVisible);
        Assert.Equal("Could not open a browser. The page is at https://example/release.", fallback.Text);
    }

    [AvaloniaFact]
    public void Skip_reports_the_version_and_closes()
    {
        var (window, opener, skipped) = ShowNewer();
        var closed = ClosedFlag(window);

        Click(window.FindControl<Button>("SkipButton")!);

        Assert.Equal(["0.6.0"], skipped);
        Assert.Empty(opener.Opened);
        Assert.True(closed());
    }

    [AvaloniaFact]
    public void Remind_me_later_closes_without_reporting_anything()
    {
        var (window, opener, skipped) = ShowNewer();
        var closed = ClosedFlag(window);

        Click(window.FindControl<Button>("RemindButton")!);

        Assert.True(closed());
        Assert.Empty(opener.Opened);
        Assert.Empty(skipped);
    }

    [AvaloniaFact]
    public void Ok_closes_the_up_to_date_window_without_reporting_anything()
    {
        var (window, opener, skipped) = Show(new UpdateCheckResult.UpToDate());
        var closed = ClosedFlag(window);

        Click(window.FindControl<Button>("OkButton")!);

        Assert.True(closed());
        Assert.Empty(opener.Opened);
        Assert.Empty(skipped);
    }

    /// <summary>The automatic check can open this over a terminal the user is typing into, where Enter is the 3270
    /// transmit key: it must close the notice for now, never open a browser.</summary>
    [AvaloniaFact]
    public void Enter_is_remind_me_later()
    {
        var (window, opener, skipped) = ShowNewer();
        var closed = ClosedFlag(window);

        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);

        Assert.True(closed());
        Assert.Empty(opener.Opened);
        Assert.Empty(skipped);
    }

    /// <summary>Tab is the 3270 field key, so the first tab stop must be harmless too: Skip there would let Tab and
    /// Enter keep a release from ever being announced again.</summary>
    [AvaloniaFact]
    public void The_first_tab_stop_is_remind_me_later()
    {
        var (window, opener, skipped) = ShowNewer();

        window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
        Assert.Same(window.FindControl<Button>("RemindButton"), window.FocusManager?.GetFocusedElement());

        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Assert.Empty(opener.Opened);
        Assert.Empty(skipped);
    }

    /// <summary>A page the platform could not open has to be copyable, or the user retypes a long URL.</summary>
    [AvaloniaFact]
    public void The_fallback_line_can_be_selected()
    {
        var (window, _, _) = ShowNewer();

        Assert.IsType<SelectableTextBlock>(window.FindControl<TextBlock>("FallbackText"));
    }
}
