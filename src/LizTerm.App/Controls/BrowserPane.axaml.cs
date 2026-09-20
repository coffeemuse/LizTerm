// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia;
using Avalonia.Controls;

namespace LizTerm.App.Controls;

/// <summary>One pane of the mvsMF Access window (pane-pattern spec §4): a title row with an optional header
/// control, a toolbar of the verbs that act on this pane's selection, a body (the column header and list) and a
/// footer (a count and an optional action). It owns the frame, the spacing and the verb style, and knows nothing
/// about hosts: the window fills the slots and binds their contents to its own view model. The Datasets and
/// Members panes are two uses of it; a USS browser's directory and file panes will be two more.</summary>
public partial class BrowserPane : UserControl
{
    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<BrowserPane, string>(nameof(Title), "");

    public static readonly StyledProperty<object?> HeaderContentProperty =
        AvaloniaProperty.Register<BrowserPane, object?>(nameof(HeaderContent));

    public static readonly StyledProperty<object?> ToolbarProperty =
        AvaloniaProperty.Register<BrowserPane, object?>(nameof(Toolbar));

    public static readonly StyledProperty<object?> BodyProperty =
        AvaloniaProperty.Register<BrowserPane, object?>(nameof(Body));

    public static readonly StyledProperty<string> FooterTextProperty =
        AvaloniaProperty.Register<BrowserPane, string>(nameof(FooterText), "");

    public static readonly StyledProperty<object?> FooterActionProperty =
        AvaloniaProperty.Register<BrowserPane, object?>(nameof(FooterAction));

    public BrowserPane() => InitializeComponent();

    /// <summary>The title row's text: the pane's name, or the dataset it shows.</summary>
    public string Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>The title row's right-hand control, such as a filter box.</summary>
    public object? HeaderContent
    {
        get => GetValue(HeaderContentProperty);
        set => SetValue(HeaderContentProperty, value);
    }

    /// <summary>The row of verbs under the title.</summary>
    public object? Toolbar
    {
        get => GetValue(ToolbarProperty);
        set => SetValue(ToolbarProperty, value);
    }

    /// <summary>The column header and the list. Named Body because a UserControl's Content is its own XAML.</summary>
    public object? Body
    {
        get => GetValue(BodyProperty);
        set => SetValue(BodyProperty, value);
    }

    /// <summary>The footer's text: "41 datasets · 1 selected".</summary>
    public string FooterText
    {
        get => GetValue(FooterTextProperty);
        set => SetValue(FooterTextProperty, value);
    }

    /// <summary>The footer's right-hand control, such as Load more; the cell collapses when null.</summary>
    public object? FooterAction
    {
        get => GetValue(FooterActionProperty);
        set => SetValue(FooterActionProperty, value);
    }
}
