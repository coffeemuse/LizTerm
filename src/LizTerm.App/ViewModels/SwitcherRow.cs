// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using LizTerm.App.Sessions;
using LizTerm.Core.Session;

namespace LizTerm.App.ViewModels;

/// <summary>One switcher row, a snapshot of its session when the rows were built (session switching spec §5.3).
/// Only the selection moves while it is on screen; anything else that changes rebuilds the rows.</summary>
public sealed partial class SwitcherRow : ObservableObject
{
    public SwitcherRow(SessionEntry entry, int? position, bool numberActive, bool isThisWindow)
    {
        Entry = entry;
        Number = position is { } p ? (p % 10).ToString(CultureInfo.InvariantCulture) : null;
        NumberActive = numberActive;
        IsDisconnected = entry.Session.Connection == ConnectionState.Disconnected;
        IsThisWindow = isThisWindow;
        IsOnTop = entry.Host.KeepOnTop;
    }

    public SessionEntry Entry { get; }

    /// <summary>The Sessions list's row, which ProfileSummaryTemplate draws.</summary>
    public ProfileRow Summary => Entry.Summary;

    /// <summary>"1" to "9", "0" for the tenth, null past ten: the position in opening order, kept when filtered.</summary>
    public string? Number { get; }

    public bool HasNumber => Number is not null;

    /// <summary>A solid badge while a digit jumps; a dashed outline once the filter holds text.</summary>
    public bool NumberActive { get; }

    public bool IsDisconnected { get; }

    /// <summary>The window the switcher opened in.</summary>
    public bool IsThisWindow { get; }

    public bool IsOnTop { get; }

    /// <summary>Shape, not colour: the flag adds the word.</summary>
    public string ConnectionMark => IsDisconnected ? "○" : "●";

    /// <summary>The first that applies, as words so no flag depends on colour or a glyph a UI font might lack.</summary>
    public string? Flag =>
        IsDisconnected ? "Disconnected"
        : IsThisWindow ? "This window"
        : IsOnTop ? "On top"
        : null;

    [ObservableProperty] private bool _isSelected;
}
