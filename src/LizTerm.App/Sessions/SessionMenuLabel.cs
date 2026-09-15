// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Text;
using LizTerm.Core.Session;

namespace LizTerm.App.Sessions;

/// <summary>One session as one line of menu text, for the Window menu and the Dock menu alike (session switching
/// spec §4): <c>[_position][two spaces][★ ][name][ - host][ (Disconnected)]</c>. A menu cannot draw chips or
/// colour, so this is plain text.</summary>
public static class SessionMenuLabel
{
    public static string For(SessionEntry entry, int? position)
    {
        var profile = entry.Session.Profile;
        var label = new StringBuilder();
        // The underscore makes the digit the in-window menu's access key: Alt, W, 3 reaches session 3.
        if (position is { } number) label.Append('_').Append(number % 10).Append("  ");
        if (entry.Summary.IsFavorite) label.Append("★ ");
        label.Append(Escape(profile.Name));
        // An ad hoc session is named host:port or LU@host:port (StartupArguments.Resolve) and carries its host.
        if (!profile.Name.Contains(profile.Host, StringComparison.OrdinalIgnoreCase))
            label.Append(" - ").Append(Escape(profile.Host));
        if (entry.Session.Connection == ConnectionState.Disconnected) label.Append(" (Disconnected)");
        return label.ToString();
    }

    /// <summary>A doubled underscore is a literal one in both renderers' headers.</summary>
    private static string Escape(string text) => text.Replace("_", "__");
}
