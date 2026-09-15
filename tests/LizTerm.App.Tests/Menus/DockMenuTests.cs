// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Platform;
using Avalonia.Headless.XUnit;
using LizTerm.App.Menus;
using LizTerm.App.Sessions;
using LizTerm.App.Tests.Fakes;

namespace LizTerm.App.Tests.Menus;

/// <summary>The Dock menu (session switching spec §8). [AvaloniaFact] rather than [Fact] because NativeMenu is an
/// AvaloniaObject and belongs to the UI thread. macOS's own Options, Show All Windows, Hide and Quit are appended
/// by the Dock itself and never appear here.</summary>
public class DockMenuTests
{
    private static string?[] Headers(NativeMenu menu) =>
        [.. menu.Items.Select(item => item is NativeMenuItemSeparator ? "---" : ((NativeMenuItem)item).Header)];

    private static void Click(NativeMenuItemBase item) => ((INativeMenuItemExporterEventsImplBridge)item).RaiseClicked();

    [AvaloniaFact]
    public void It_lists_the_sessions_then_a_separator_then_new_session()
    {
        var list = new SessionList();
        list.Add(TestSessions.Create("TSO", "tk5.local").Entry);
        list.Add(TestSessions.Create("IMON", "mvs.local").Entry);
        var menu = new NativeMenu();

        DockMenu.Rebuild(menu, list, () => { });

        Assert.Equal(new string?[] { "_1  TSO - tk5.local", "_2  IMON - mvs.local", "---", "New Session..." }, Headers(menu));
        Assert.True(((NativeMenuItem)menu.Items[0]).IsChecked);
        Assert.False(((NativeMenuItem)menu.Items[1]).IsChecked);
    }

    [AvaloniaFact]
    public void With_no_sessions_it_holds_new_session_alone()
    {
        var menu = new NativeMenu();

        DockMenu.Rebuild(menu, new SessionList(), () => { });

        Assert.Equal(new string?[] { DockMenu.NewSessionHeader }, Headers(menu));
    }

    [AvaloniaFact]
    public void A_session_item_brings_its_session_and_new_session_opens_the_list()
    {
        var list = new SessionList();
        var (tso, _, tsoHost) = TestSessions.Create("TSO");
        list.Add(tso);
        var newSessions = 0;
        var menu = new NativeMenu();
        DockMenu.Rebuild(menu, list, () => newSessions++);

        Click(menu.Items[0]);
        Click(menu.Items[^1]);

        Assert.Equal(1, tsoHost.BringCount);
        Assert.Equal(1, newSessions);
    }

    /// <summary>Attached to the shared test Application, so the menu it had is put back afterwards.</summary>
    [AvaloniaFact]
    public void On_macOS_it_is_attached_and_follows_the_list()
    {
        var app = Application.Current!;
        var previous = NativeDock.GetMenu(app);
        try
        {
            var list = new SessionList();
            var menu = DockMenu.Attach(app, list, () => { }, isMacOS: true);

            Assert.NotNull(menu);
            Assert.Same(menu, NativeDock.GetMenu(app));
            list.Add(TestSessions.Create("TSO", "tk5.local").Entry);
            Assert.Equal(new string?[] { "_1  TSO - tk5.local", "---", "New Session..." }, Headers(menu!));
        }
        finally
        {
            NativeDock.SetMenu(app, previous!);
        }
    }

    [AvaloniaFact]
    public void Off_macOS_it_attaches_nothing()
    {
        var app = Application.Current!;
        var previous = NativeDock.GetMenu(app);

        var menu = DockMenu.Attach(app, new SessionList(), () => { }, isMacOS: false);

        Assert.Null(menu);
        Assert.Same(previous, NativeDock.GetMenu(app));
    }
}
