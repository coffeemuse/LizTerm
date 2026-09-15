// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.ComponentModel;
using LizTerm.App.ViewModels;

namespace LizTerm.App.Sessions;

/// <summary>The process's one record of open sessions (session switching spec §3). Two orders: Entries is opening
/// order, which gives the numbers 1-10; a private use order gives Current and Previous. Everything runs on the UI
/// thread: SessionViewModel marshals its backend events there, and hosts raise KeepOnTopChanged from UI code.</summary>
public sealed class SessionList
{
    /// <summary>Positions past this have no number.</summary>
    public const int NumberedSessions = 10;

    private readonly List<SessionEntry> _opened = [];
    private readonly List<SessionEntry> _used = [];
    private readonly Dictionary<SessionEntry, (PropertyChangedEventHandler Connection, EventHandler KeepOnTop)> _handlers = [];

    public IReadOnlyList<SessionEntry> Entries => _opened;

    public int Count => _opened.Count;

    /// <summary>The most recently activated session, or the first opened while none has been activated.</summary>
    public SessionEntry? Current => _used.Count > 0 ? _used[0] : null;

    public SessionEntry? Previous => _used.Count > 1 ? _used[1] : null;

    /// <summary>Raised after Add, Remove, an Activated that changes Current, a Connection change on any listed
    /// session, and any host's KeepOnTopChanged.</summary>
    public event EventHandler? Changed;

    public void Add(SessionEntry entry)
    {
        if (_opened.Contains(entry)) throw new InvalidOperationException("That session is already listed.");
        _opened.Add(entry);
        // At the end of use order: a session never activated is the least recent.
        _used.Add(entry);
        PropertyChangedEventHandler onConnection = (_, e) =>
        {
            if (e.PropertyName == nameof(SessionViewModel.Connection)) RaiseChanged();
        };
        EventHandler onKeepOnTop = (_, _) => RaiseChanged();
        entry.Session.PropertyChanged += onConnection;
        entry.Host.KeepOnTopChanged += onKeepOnTop;
        _handlers[entry] = (onConnection, onKeepOnTop);
        RaiseChanged();
    }

    public void Remove(SessionEntry entry)
    {
        if (!_opened.Remove(entry)) return;
        _used.Remove(entry);
        var (onConnection, onKeepOnTop) = _handlers[entry];
        _handlers.Remove(entry);
        entry.Session.PropertyChanged -= onConnection;
        entry.Host.KeepOnTopChanged -= onKeepOnTop;
        RaiseChanged();
    }

    public void Activated(SessionEntry entry)
    {
        if (!_used.Contains(entry) || ReferenceEquals(Current, entry)) return;
        _used.Remove(entry);
        _used.Insert(0, entry);
        RaiseChanged();
    }

    /// <summary>1 to 10 in opening order, or null past ten or for a session not listed.</summary>
    public int? PositionOf(SessionEntry entry)
    {
        var index = _opened.IndexOf(entry);
        return index is >= 0 and < NumberedSessions ? index + 1 : null;
    }

    /// <summary>Brings every session that is not minimised, least recently used first, so Current is raised last
    /// and ends in front. Each Bring activates its window, and activating in reverse use order replays the same use
    /// order, so Previous is unchanged and nothing needs suspending. The list is copied first because each Bring
    /// changes use order as it goes.</summary>
    public void BringAllToFront()
    {
        var raise = _used.Where(entry => !entry.Host.IsMinimized).Reverse().ToList();
        foreach (var entry in raise) entry.Host.Bring();
    }

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
