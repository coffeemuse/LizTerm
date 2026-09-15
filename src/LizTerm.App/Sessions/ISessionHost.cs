// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

namespace LizTerm.App.Sessions;

/// <summary>Whatever shows a session: today a SessionWindow, later possibly a tab in a tabbed window (#119). The
/// switcher, the Window menu and the Dock menu reach a session only through this, which is what lets a tab host
/// plug in without any of them changing (session switching spec §3).</summary>
public interface ISessionHost
{
    /// <summary>Restore if minimised, then activate. A tab host would select the tab first.</summary>
    void Bring();

    bool IsMinimized { get; }

    bool KeepOnTop { get; }

    event EventHandler? KeepOnTopChanged;
}
