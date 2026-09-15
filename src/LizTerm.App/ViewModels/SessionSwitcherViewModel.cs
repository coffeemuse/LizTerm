// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using CommunityToolkit.Mvvm.ComponentModel;
using LizTerm.App.Sessions;

namespace LizTerm.App.ViewModels;

/// <summary>The session switcher's state for one window (session switching spec §5). FindViewModel's shape: its
/// own lifetime and no controls, so every rule is a plain [Fact]. It never brings a window itself: TryDigit and
/// Choose return the entry, and the window calls Host.Bring() on it.</summary>
public sealed partial class SessionSwitcherViewModel : ObservableObject
{
    public const string JumpHint = "1–9, 0 jump · ↑↓ move · ↵ switch · Esc close";
    public const string FilterHint = "↑↓ move · ↵ switch · Esc close · digits type into the filter now";

    private readonly SessionList _sessions;
    private readonly SessionEntry _own;

    public SessionSwitcherViewModel(SessionList sessions, SessionEntry own)
    {
        _sessions = sessions;
        _own = own;
    }

    [ObservableProperty] private bool _isOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DigitsJump))]
    [NotifyPropertyChangedFor(nameof(Hint))]
    private string _term = "";

    [ObservableProperty] private IReadOnlyList<SwitcherRow> _rows = [];

    [ObservableProperty] private SwitcherRow? _selected;

    /// <summary>A digit jumps only while the filter is empty; after that it filters, because names like TK4 and
    /// VM370 contain digits.</summary>
    public bool DigitsJump => Term.Length == 0;

    public string Hint => DigitsJump ? JumpHint : FilterHint;

    /// <summary>Clears the filter, lists every session, and selects the previous one — so Cmd+K, Enter flips
    /// between two — or the current one when it is alone.</summary>
    public void Open()
    {
        if (IsOpen) return;
        Term = "";
        IsOpen = true;
        _sessions.Changed += OnSessionsChanged;
        Rebuild(_sessions.Previous ?? _sessions.Current);
    }

    public void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;
        _sessions.Changed -= OnSessionsChanged;
        Selected = null;
        Rows = [];
    }

    /// <summary>The session at a digit's position while digits jump (0 is the tenth), else null.</summary>
    public SessionEntry? TryDigit(char digit)
    {
        if (!IsOpen || !DigitsJump || digit is < '0' or > '9') return null;
        var position = digit == '0' ? SessionList.NumberedSessions : digit - '0';
        return position <= _sessions.Count ? _sessions.Entries[position - 1] : null;
    }

    public SessionEntry? Choose() => IsOpen ? Selected?.Entry : null;

    public void MoveUp() => Move(-1);

    public void MoveDown() => Move(1);

    private void Move(int delta)
    {
        if (Selected is not { } selected) return;
        var index = IndexOf(selected) + delta;
        if (index >= 0 && index < Rows.Count) Selected = Rows[index];
    }

    private int IndexOf(SwitcherRow row)
    {
        for (var i = 0; i < Rows.Count; i++)
            if (ReferenceEquals(Rows[i], row)) return i;
        return -1;
    }

    partial void OnTermChanged(string value)
    {
        if (IsOpen) Rebuild(null);
    }

    partial void OnSelectedChanged(SwitcherRow? oldValue, SwitcherRow? newValue)
    {
        if (oldValue is not null) oldValue.IsSelected = false;
        if (newValue is not null) newValue.IsSelected = true;
    }

    private void OnSessionsChanged(object? sender, EventArgs e) => Rebuild(Selected?.Entry);

    /// <summary>Rows for the current filter, keeping the preferred session selected when it is still listed and
    /// otherwise selecting the first row.</summary>
    private void Rebuild(SessionEntry? prefer)
    {
        var rows = _sessions.Entries
            .Where(entry => SwitcherFilter.Matches(entry, Term))
            .Select(entry => new SwitcherRow(entry, _sessions.PositionOf(entry), DigitsJump, ReferenceEquals(entry, _own)))
            .ToList();
        Selected = null;
        Rows = rows;
        Selected = rows.FirstOrDefault(row => ReferenceEquals(row.Entry, prefer)) ?? rows.FirstOrDefault();
    }
}
