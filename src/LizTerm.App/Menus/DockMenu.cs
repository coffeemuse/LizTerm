// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia;
using Avalonia.Controls;
using LizTerm.App.Sessions;

namespace LizTerm.App.Menus;

/// <summary>The macOS Dock icon's menu (session switching spec §8): the open sessions, then New Session..., so one
/// click from inside another application lands on the right session. Avalonia.Native exports a dock menu from the
/// application (SetupApplicationDockMenuExporter), so it does not depend on which window is in front. macOS
/// appends its own Options, Show All Windows, Hide and Quit below. Windows' taskbar and Linux task lists already
/// list windows by title, so nothing is attached there.</summary>
internal static class DockMenu
{
    public const string NewSessionHeader = "New Session...";

    /// <summary>Builds the menu, keeps it current on every SessionList.Changed, and attaches it — on macOS only; off
    /// macOS it attaches nothing and answers null. The platform is an argument, MenuStrategy.Resolve's shape, so a
    /// test on any OS can take either branch. The subscription lives as long as the process, as the menu does.</summary>
    public static NativeMenu? Attach(Application app, SessionList sessions, Action newSession, bool isMacOS)
    {
        if (!isMacOS) return null;
        var menu = new NativeMenu();
        Rebuild(menu, sessions, newSession);
        sessions.Changed += (_, _) => Rebuild(menu, sessions, newSession);
        NativeDock.SetMenu(app, menu);
        return menu;
    }

    /// <summary>Refills the same instance, removing from the end one at a time (#60).</summary>
    public static void Rebuild(NativeMenu menu, SessionList sessions, Action newSession)
    {
        while (menu.Items.Count > 0) menu.Items.RemoveAt(menu.Items.Count - 1);
        foreach (var entry in sessions.Entries)
            menu.Items.Add(SessionMenuItems.Native(sessions, entry, chosen => chosen.Host.Bring()));
        if (sessions.Count > 0) menu.Items.Add(new NativeMenuItemSeparator());
        var newSessionItem = new NativeMenuItem { Header = NewSessionHeader };
        newSessionItem.Click += (_, _) => newSession();
        menu.Items.Add(newSessionItem);
    }
}
