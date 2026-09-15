// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Controls;
using LizTerm.App.Sessions;

namespace LizTerm.App.Menus;

/// <summary>One open session as a menu item, for the Window menu's two renderers and the Dock menu (session
/// switching spec §7.2, §8). Both shapes come from here so they cannot drift: the same label, a check box marked
/// on the current session, and a Click handler — which the macOS exporter requires, since these carry no Command.</summary>
internal static class SessionMenuItems
{
    public static NativeMenuItem Native(SessionList sessions, SessionEntry entry, Action<SessionEntry> choose)
    {
        var item = new NativeMenuItem
        {
            Header = SessionMenuLabel.For(entry, sessions.PositionOf(entry)),
            ToggleType = MenuItemToggleType.CheckBox,
            IsChecked = ReferenceEquals(entry, sessions.Current),
        };
        item.Click += (_, _) => choose(entry);
        return item;
    }

    public static MenuItem Classic(SessionList sessions, SessionEntry entry, Action<SessionEntry> choose)
    {
        var item = new MenuItem
        {
            Header = SessionMenuLabel.For(entry, sessions.PositionOf(entry)),
            ToggleType = MenuItemToggleType.CheckBox,
            IsChecked = ReferenceEquals(entry, sessions.Current),
        };
        item.Click += (_, _) => choose(entry);
        return item;
    }
}
