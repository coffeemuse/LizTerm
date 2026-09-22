// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using LizTerm.App.ViewModels;

namespace LizTerm.App.Views;

/// <summary>The read-only text viewer (viewer spec §6), owned by the mvsMF Access window and shown with
/// ShowAbove, so it stays above it, follows its Keep on Top and closes with it. One per browser window: a second
/// View replaces this DataContext rather than opening another window. It never refuses to close and holds nothing
/// to release — the view model is finished lines.</summary>
public partial class MvsmfViewerWindow : Window
{
    /// <summary>Pinned on the text box and the gutter together, so a line number sits beside its record and a
    /// scroll by line index is exact.</summary>
    private const double TextLineHeight = 18;

    private MvsmfViewerViewModel? _watched;
    private ScrollViewer? _scroller;
    private KeyModifiers _findModifiers = KeyModifiers.Control;

    public MvsmfViewerWindow()
    {
        InitializeComponent();
        TextArea.LineHeight = TextLineHeight;
        Gutter.LineHeight = TextLineHeight;
        // The platform's command modifier (Cmd on macOS, Ctrl elsewhere), as the browser window does for its own.
        _findModifiers = this.GetPlatformSettings()?.HotkeyConfiguration.CommandModifiers ?? KeyModifiers.Control;
        AddHandler(KeyDownEvent, OnKeyDownTunnel, RoutingStrategies.Tunnel);
        // The text box scrolls itself; the gutter has no scrollbars and is driven from that one.
        TextArea.TemplateApplied += (_, e) =>
        {
            _scroller = e.NameScope.Find<ScrollViewer>("PART_ScrollViewer")
                ?? TextArea.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
            if (_scroller is not null) _scroller.ScrollChanged += (_, _) => SyncGutter();
        };
        Opened += (_, _) => FindBox.Focus();
    }

    private void SyncGutter()
    {
        if (_scroller is not null) GutterScroller.Offset = new Vector(0, _scroller.Offset.Y);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        // The gutter is the window's dressing, not the document's: a second View into this window keeps whatever
        // the user last chose. A viewer that was closed and opened again starts with it on.
        var lineNumbers = _watched?.ShowLineNumbers;
        if (_watched is not null) _watched.PropertyChanged -= OnViewModelPropertyChanged;
        _watched = DataContext as MvsmfViewerViewModel;
        if (_watched is not null)
        {
            if (lineNumbers is { } chosen) _watched.ShowLineNumbers = chosen;
            _watched.PropertyChanged += OnViewModelPropertyChanged;
        }
        // A second View is another document: it starts at the top, not where the last one was left.
        if (_scroller is not null) _scroller.Offset = default;
        GutterScroller.Offset = default;
        base.OnDataContextChanged(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_watched is not null) _watched.PropertyChanged -= OnViewModelPropertyChanged;
        _watched = null;
        base.OnClosed(e);
    }

    /// <summary>The current match is selected in the text and scrolled to. Scrolling goes by line index times the
    /// pinned line height rather than through CaretIndex, because the text box does not have the focus while the
    /// user is typing in the find box.</summary>
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_watched is not { } vm) return;
        if (e.PropertyName is not nameof(MvsmfViewerViewModel.MatchStart)) return;
        if (vm.MatchStart is not { } start)
        {
            TextArea.SelectionEnd = TextArea.SelectionStart;
            return;
        }
        TextArea.SelectionStart = start;
        TextArea.SelectionEnd = start + vm.MatchLength;
        if (vm.MatchLine is { } line) ScrollToLine(line);
    }

    /// <summary>Leaves the view alone while the line is already in it; otherwise puts it a third of the way down,
    /// so the lines around it are readable. The horizontal offset is the user's.</summary>
    private void ScrollToLine(int line)
    {
        if (_scroller is null) return;
        var top = line * TextLineHeight;
        var viewport = _scroller.Viewport.Height;
        if (top >= _scroller.Offset.Y && top + TextLineHeight <= _scroller.Offset.Y + viewport) return;
        var furthest = Math.Max(0, _scroller.Extent.Height - viewport);
        _scroller.Offset = new Vector(_scroller.Offset.X, Math.Clamp(top - viewport / 3, 0, furthest));
    }

    /// <summary>Escape closes; the command modifier with F goes to the find box; Enter and Shift+Enter step the
    /// matches from anywhere but a button, which has its own Enter.</summary>
    private void OnKeyDownTunnel(object? sender, KeyEventArgs e)
    {
        if (_watched is not { } vm) return;
        switch (e.Key)
        {
            case Key.Escape:
                e.Handled = true;
                Close();
                break;
            case Key.F when e.KeyModifiers == _findModifiers:
                e.Handled = true;
                FindBox.Focus();
                FindBox.SelectAll();
                break;
            case Key.Enter when FocusManager?.GetFocusedElement() is not Button:
                e.Handled = true;
                var command = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? vm.FindPreviousCommand : vm.FindNextCommand;
                if (command.CanExecute(null)) command.Execute(null);
                break;
        }
    }
}
