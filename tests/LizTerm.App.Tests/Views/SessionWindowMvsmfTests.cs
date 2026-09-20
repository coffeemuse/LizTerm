// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Controls;
using Avalonia.Controls.Platform;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using LizTerm.App.HostFiles;
using LizTerm.App.Menus;
using LizTerm.App.Tests.Fakes;
using LizTerm.App.Tests.ViewModels;
using LizTerm.App.ViewModels;
using LizTerm.App.Views;
using LizTerm.Core.HostFiles;
using LizTerm.Core.Session;
using LizTerm.Core.Settings;

namespace LizTerm.App.Tests.Views;

public class SessionWindowMvsmfTests
{
    private sealed record Shown(SessionWindow Window, SessionViewModel Vm, FakeHostFileService Host, HostFileAccess? Access);

    private static Shown Show(string? url = "http://mvs:8080", MenuStyle style = MenuStyle.InWindow, FakeUriOpener? opener = null)
    {
        var session = new FakeEmulatorSession();
        var vm = new SessionViewModel(session, action => action(), new FakeTextClipboard(), uriOpener: opener);
        var window = new SessionWindow(style, isMacOS: true) { DataContext = vm };
        var host = new FakeHostFileService();
        BrowserTestHost.Standard(host);
        HostFileAccess? access = null;
        if (url is not null)
        {
            // No userid, so the browser does not list on open and no sign-in window appears in these tests.
            access = new HostFileAccess(new SessionProfile { Name = "MVS/CE", Host = "mvs", HostFilesUrl = url }, (_, _, _) => host, savePin: null);
            window.AttachHostFiles(access);
        }
        window.Show();
        return new Shown(window, vm, host, access);
    }

    private static MenuItem Classic(SessionWindow window) => window.FindControl<MenuItem>("MvsmfBrowserMenuItem")!;

    private static NativeMenuItem Native(SessionWindow window) =>
        MenuLookup.Item(NativeMenu.GetMenu(window), "_File", "mvsMF _Browser...")!;

    private static void Click(MenuItem item) => item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

    [AvaloniaFact]
    public void Without_a_rest_url_neither_menu_shows_the_item()
    {
        var shown = Show(url: null);
        Assert.False(Classic(shown.Window).IsVisible);
        Assert.False(Native(Show(url: null, style: MenuStyle.Native).Window).IsVisible);
    }

    [AvaloniaFact]
    public void With_a_rest_url_both_menus_show_it_without_a_shortcut()
    {
        var classic = Show().Window;
        Assert.True(Classic(classic).IsVisible);
        Assert.Null(Classic(classic).InputGesture);

        var native = Native(Show(style: MenuStyle.Native).Window);
        Assert.True(native.IsVisible);
        Assert.True(native.HasClickHandlers);
        Assert.Null(native.Gesture);
    }

    [AvaloniaFact]
    public void Choosing_it_opens_one_browser_over_the_session_window_and_again_fronts_it()
    {
        var shown = Show();

        Click(Classic(shown.Window));

        var browser = Assert.IsType<MvsmfBrowserWindow>(Assert.Single(shown.Window.OwnedWindows));
        var vm = Assert.IsType<MvsmfBrowserViewModel>(browser.DataContext);
        Assert.Equal("mvsMF Access — MVS/CE (Preview)", vm.Title);
        Assert.Same(browser, shown.Window.MvsmfBrowser);

        Click(Classic(shown.Window));

        Assert.Same(browser, Assert.Single(shown.Window.OwnedWindows));
    }

    [AvaloniaFact]
    public void The_native_item_opens_it_too()
    {
        var shown = Show(style: MenuStyle.Native);

        ((INativeMenuItemExporterEventsImplBridge)Native(shown.Window)).RaiseClicked();

        Assert.IsType<MvsmfBrowserWindow>(Assert.Single(shown.Window.OwnedWindows));
    }

    [AvaloniaFact]
    public void The_browser_follows_the_session_windows_keep_on_top()
    {
        var shown = Show();
        Click(Classic(shown.Window));
        var browser = shown.Window.MvsmfBrowser!;
        Assert.False(browser.Topmost);

        Click(shown.Window.FindControl<MenuItem>("KeepOnTopMenuItem")!);

        Assert.True(shown.Window.Topmost);
        Assert.True(browser.Topmost);
    }

    [AvaloniaFact]
    public void Closing_the_browser_lets_the_menu_open_a_fresh_one()
    {
        var shown = Show();
        Click(Classic(shown.Window));
        var first = shown.Window.MvsmfBrowser!;

        first.Close();
        Assert.Null(shown.Window.MvsmfBrowser);
        Assert.True(shown.Host.Disposed);
        Click(Classic(shown.Window));

        Assert.NotSame(first, shown.Window.MvsmfBrowser);
        Assert.NotNull(shown.Window.MvsmfBrowser);
    }

    [AvaloniaFact]
    public async Task Closing_the_session_window_closes_the_browser_and_signs_out()
    {
        var shown = Show();
        await shown.Access!.SignIn.ProviderFor(new FakeCredentialPrompt())(
            new HostTokenRequest(null), (c, _) => Task.FromResult(new HostSessionToken($"tok:{c.Userid}")), CancellationToken.None);
        Assert.True(shown.Access.SignIn.IsSignedIn);
        Click(Classic(shown.Window));
        var browser = shown.Window.MvsmfBrowser!;
        var closed = false;
        browser.Closed += (_, _) => closed = true;

        shown.Window.Close();

        Assert.True(closed);
        Assert.False(shown.Access.SignIn.IsSignedIn);
    }

    /// <summary>The sign-out is best effort and fire-and-forget: a host that refuses it must not keep the window
    /// open or throw out of Close, and the token is gone either way.</summary>
    [AvaloniaFact]
    public async Task A_failing_sign_out_does_not_stop_the_window_closing()
    {
        var shown = Show();
        shown.Host.Failures["signout"] = new HostFileException(HostFileErrorKind.Unreachable, "Sign-out: cannot reach the host.");
        await shown.Access!.SignIn.ProviderFor(new FakeCredentialPrompt())(
            new HostTokenRequest(null), (c, _) => Task.FromResult(new HostSessionToken($"tok:{c.Userid}")), CancellationToken.None);
        Assert.True(shown.Access.SignIn.IsSignedIn);

        shown.Window.Close();

        Assert.False(shown.Access.SignIn.IsSignedIn);
        Assert.False(shown.Window.IsVisible);
        await Wait.UntilAsync(() => shown.Host.CallsSnapshot().Contains("signout"), "the sign-out to be attempted");
    }

    [AvaloniaFact]
    public void An_unusable_url_is_reported_in_the_banner()
    {
        var shown = Show(url: "ftp://mvs");

        Click(Classic(shown.Window));

        Assert.Empty(shown.Window.OwnedWindows);
        Assert.Equal("The profile's mvsMF URL cannot be used: Enter an http:// or https:// URL.", shown.Vm.ErrorMessage);
    }

    [AvaloniaFact]
    public async Task The_browser_opens_the_user_guide_through_the_session()
    {
        var opener = new FakeUriOpener();
        var shown = Show(opener: opener);
        Click(Classic(shown.Window));
        var vm = (MvsmfBrowserViewModel)shown.Window.MvsmfBrowser!.DataContext!;

        await vm.OpenGuideCommand.ExecuteAsync(null);

        Assert.Single(opener.Opened);
    }
}
