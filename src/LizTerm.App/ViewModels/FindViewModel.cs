// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using CommunityToolkit.Mvvm.ComponentModel;
using LizTerm.Core.Screen;

namespace LizTerm.App.ViewModels;

/// <summary>The find bar's state, for one session window.
///
/// Its own view model rather than more weight on SessionViewModel, which already owns the session, the
/// connection lifecycle, the certificate prompt, the clipboard, the wire log and the transfer factory. Find has
/// its own lifetime — it opens, it holds a term and a match list, it closes — and it names no Avalonia type, so
/// every rule here is assertable with a plain [Fact].</summary>
public sealed partial class FindViewModel : ObservableObject
{
    private readonly Func<int, int, Task> _moveCursor;
    private ScreenSnapshot? _snapshot;

    /// <summary>Whether the host cursor has been moved to the current match. Typing must not move it — that
    /// would send a MoveCursor per character — so a fresh search highlights match 0 without visiting it, and
    /// the first Enter visits it instead of skipping to match 1. A repaint recompute leaves this alone.</summary>
    private bool _visited;

    public FindViewModel(Func<int, int, Task> moveCursor) => _moveCursor = moveCursor;

    [ObservableProperty] private bool _isOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CountText))]
    private string _term = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CountText))]
    [NotifyPropertyChangedFor(nameof(CurrentMatch))]
    private IReadOnlyList<ScreenRegion> _matches = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CountText))]
    [NotifyPropertyChangedFor(nameof(CurrentMatch))]
    private int _currentIndex = -1;

    /// <summary>The match the user is on, painted distinctly from the rest.</summary>
    public ScreenRegion? CurrentMatch =>
        CurrentIndex >= 0 && CurrentIndex < Matches.Count ? Matches[CurrentIndex] : null;

    public string CountText =>
        string.IsNullOrWhiteSpace(Term) ? ""
        : Matches.Count == 0 ? "No matches"
        : $"{CurrentIndex + 1} of {Matches.Count}";

    partial void OnTermChanged(string value)
    {
        _visited = false;
        Refresh();
    }

    /// <summary>A new snapshot. Matches recompute rather than clear: a search term stays meaningful where a
    /// selected rectangle does not, and a 3270 screen repaints on every keystroke echo (spec 5.5). Called from
    /// SessionViewModel.ApplyScreen, so this is already on the UI thread and never on the render path.</summary>
    public void OnScreen(ScreenSnapshot snapshot)
    {
        _snapshot = snapshot;
        Refresh();
    }

    public void Open()
    {
        _visited = false;
        IsOpen = true;
        Refresh();
    }

    public void Close()
    {
        IsOpen = false;
        Matches = [];
        CurrentIndex = -1;
    }

    public Task NextAsync() => MoveAsync(1);

    public Task PreviousAsync() => MoveAsync(-1);

    /// <summary>The one place matches are computed, so "closed means no work and no stale highlight" is a
    /// single rule rather than a condition repeated at each caller. A shut bar must not pay a search on every
    /// host repaint.</summary>
    private void Refresh()
    {
        if (!IsOpen)
        {
            Matches = [];
            CurrentIndex = -1;
            return;
        }

        var previous = CurrentMatch;
        IReadOnlyList<ScreenRegion> matches = _snapshot is null ? [] : ScreenSearch.Find(_snapshot, Term);
        Matches = matches;
        CurrentIndex = Reanchor(matches, previous);

        // Reanchor kept your place only when the new current match is the same position as the old one. Any
        // other outcome — a fallback to index 0, or no match at all — lands on a match the cursor has never
        // been moved to, so a repaint that does not keep your place must clear _visited the same way OnTermChanged
        // and Open do; otherwise the next Enter would skip past the newly-highlighted match instead of visiting
        // it (the same bug _visited exists to prevent, reached through a repaint instead of through typing).
        if (!SamePosition(previous, CurrentMatch))
            _visited = false;
    }

    private static bool SamePosition(ScreenRegion? a, ScreenRegion? b) =>
        a is { } x && b is { } y && x.Top == y.Top && x.Left == y.Left;

    /// <summary>Which match to be on after a recompute: the one starting where the last one did, else the
    /// first, else none. Pure, so the rule is tested without a screen.</summary>
    internal static int Reanchor(IReadOnlyList<ScreenRegion> matches, ScreenRegion? previous)
    {
        if (matches.Count == 0) return -1;
        if (previous is { } anchor)
        {
            for (var i = 0; i < matches.Count; i++)
                if (matches[i].Top == anchor.Top && matches[i].Left == anchor.Left)
                    return i;
        }
        return 0;
    }

    private Task MoveAsync(int delta)
    {
        if (Matches.Count == 0) return Task.CompletedTask;

        if (_visited)
            CurrentIndex = ((CurrentIndex + delta) % Matches.Count + Matches.Count) % Matches.Count;
        _visited = true;

        var match = Matches[CurrentIndex];
        return _moveCursor(match.Top, match.Left);
    }
}
