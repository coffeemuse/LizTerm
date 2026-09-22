// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
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
    public const int MaxLines = 10_000;

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

    /// <summary>A count for the footer, grouped: 10,000. The gutter does not use it.</summary>
    private static string Count(int value) => value.ToString("N0", CultureInfo.InvariantCulture);
}
