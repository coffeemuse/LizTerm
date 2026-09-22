// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LizTerm.Core.HostFiles;

namespace LizTerm.App.ViewModels;

/// <summary>The read-only text viewer (viewer spec §6). It is handed finished lines: no connection, no host call
/// and no Avalonia type, so every rule here is assertable with a plain [Fact]. One instance per View; the window
/// showing it is reused.</summary>
public sealed partial class MvsmfViewerViewModel : ObservableObject
{
    /// <summary>The most lines the text box is asked to lay out. mvsMF has no ranged read, so the whole member
    /// arrives whatever this is; the cap is only what Avalonia's TextBox, which builds one layout for the lot,
    /// is asked to draw (spec §6.4).</summary>
    public const int MaxLines = 2_000;

    private readonly int _totalLines;

    public MvsmfViewerViewModel(string path, IReadOnlyList<string> lines, bool trimTrailingBlanks)
    {
        Path = path;
        _totalLines = lines.Count;
        TrimmedTrailingBlanks = trimTrailingBlanks;
        // Text mode is the only mode a viewer has; the option that matters is the trimming, and Format is the
        // rule a download writes to a file with (Core's HostFileTransfer).
        var options = new DownloadOptions(HostTransferMode.Text, trimTrailingBlanks);
        LineCount = Math.Min(_totalLines, MaxLines);
        Text = string.Join("\n", lines.Take(MaxLines).Select(options.Format));
        // Plain digits, explicitly invariant: a gutter is not a count, so it carries no thousands separator,
        // and a comma-decimal locale must not reach it (#170's lesson).
        LineNumbers = string.Join("\n", Enumerable.Range(1, LineCount).Select(n => n.ToString(CultureInfo.InvariantCulture)));
    }

    /// <summary>The host path as display text, never a HostPath: a USS file uses this window unchanged (spec §12).</summary>
    public string Path { get; }

    public string Title => $"{Path} — mvsMF Access";

    public string Text { get; }

    /// <summary>The lines shown, which the cap may make fewer than the host sent.</summary>
    public int LineCount { get; }

    /// <summary>The gutter's contents: one number per line of <see cref="Text"/>, in the same order.</summary>
    public string LineNumbers { get; }

    public bool TrimmedTrailingBlanks { get; }

    public bool IsTruncated => _totalLines > LineCount;

    public string FooterText => IsTruncated
        ? $"⚠ Showing the first {Count(MaxLines)} lines of {Count(_totalLines)}. Download the member to read it all."
        : (LineCount == 1 ? "1 line" : $"{Count(LineCount)} lines")
          + (TrimmedTrailingBlanks ? " · trailing blanks trimmed" : "");

    [ObservableProperty] private bool _showLineNumbers = true;

    /// <summary>The find term. Typing recomputes the matches and lands on the first; the window scrolls to it.
    /// FindViewModel's shape without its screen types — that one searches a ScreenSnapshot.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CountText), nameof(MatchLength))]
    private string _term = "";

    /// <summary>Where each match starts in <see cref="Text"/>, in order, never overlapping.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CountText), nameof(MatchStart), nameof(MatchLine))]
    private IReadOnlyList<int> _matches = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CountText), nameof(MatchStart), nameof(MatchLine))]
    private int _currentIndex = -1;

    public int? MatchStart => CurrentIndex >= 0 && CurrentIndex < Matches.Count ? Matches[CurrentIndex] : null;

    public int MatchLength => Term.Length;

    /// <summary>The 0-based line the current match starts on, so the window can scroll by line rather than by
    /// caret: the text box is not focused while the user types in the find box.</summary>
    public int? MatchLine => MatchStart is { } start ? Text.Take(start).Count(c => c == '\n') : null;

    public string CountText => Term.Length == 0 ? ""
        : Matches.Count == 0 ? "No matches"
        : $"{Count(CurrentIndex + 1)} of {Count(Matches.Count)}";

    [RelayCommand]
    private void FindNext() => Step(1);

    [RelayCommand]
    private void FindPrevious() => Step(-1);

    /// <summary>Wraps, so the last match's Next is the first.</summary>
    private void Step(int by)
    {
        if (Matches.Count == 0) return;
        CurrentIndex = ((CurrentIndex + by) % Matches.Count + Matches.Count) % Matches.Count;
    }

    partial void OnTermChanged(string value)
    {
        Matches = FindAll(Text, value);
        CurrentIndex = Matches.Count > 0 ? 0 : -1;
    }

    private static IReadOnlyList<int> FindAll(string text, string term)
    {
        if (term.Length == 0) return [];
        var found = new List<int>();
        for (var at = 0; at <= text.Length - term.Length;)
        {
            var next = text.IndexOf(term, at, StringComparison.OrdinalIgnoreCase);
            if (next < 0) break;
            found.Add(next);
            at = next + term.Length;
        }
        return found;
    }

    /// <summary>A count for the footer, grouped: 2,000. The gutter does not use it.</summary>
    private static string Count(int value) => value.ToString("N0", CultureInfo.InvariantCulture);
}
