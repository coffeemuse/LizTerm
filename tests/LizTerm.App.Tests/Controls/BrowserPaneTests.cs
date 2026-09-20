// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless.XUnit;
using LizTerm.App.Controls;

namespace LizTerm.App.Tests.Controls;

/// <summary>The pane frame on its own (pane-pattern spec §4): six slots, a footer whose right cell collapses when
/// there is no action, and the compact verb style. Nothing about hosts; the window's half is in
/// MvsmfBrowserWindowTests.</summary>
public class BrowserPaneTests
{
    private static (BrowserPane Pane, Window Window) Show(BrowserPane pane)
    {
        var window = new Window { Width = 480, Height = 360, Content = pane };
        window.Show();
        window.UpdateLayout();
        return (pane, window);
    }

    private static T Part<T>(BrowserPane pane, string name) where T : Control => pane.FindControl<T>(name)!;

    [AvaloniaFact]
    public void Every_slot_lands_in_its_presenter()
    {
        var filter = new TextBox();
        var toolbar = new StackPanel();
        var list = new ListBox();
        var more = new Button { Content = "Load more" };
        var (pane, _) = Show(new BrowserPane
        {
            Title = "Datasets", HeaderContent = filter, Toolbar = toolbar, Body = list,
            FooterText = "4 datasets · none selected", FooterAction = more,
        });

        Assert.Equal("Datasets", Part<TextBlock>(pane, "TitleText").Text);
        Assert.Same(filter, Part<ContentPresenter>(pane, "HeaderPresenter").Child);
        Assert.Same(toolbar, Part<ContentPresenter>(pane, "ToolbarPresenter").Child);
        Assert.Same(list, Part<ContentPresenter>(pane, "BodyPresenter").Child);
        Assert.Equal("4 datasets · none selected", Part<TextBlock>(pane, "FooterTextBlock").Text);
        Assert.Same(more, Part<ContentPresenter>(pane, "FooterActionPresenter").Child);
    }

    [AvaloniaFact]
    public void The_footer_action_cell_collapses_without_an_action()
    {
        var (pane, window) = Show(new BrowserPane { Title = "Members", FooterText = "No members" });
        var action = Part<ContentPresenter>(pane, "FooterActionPresenter");
        Assert.False(action.IsVisible);

        pane.FooterAction = new Button { Content = "Load more" };
        window.UpdateLayout();
        Assert.True(action.IsVisible);
    }

    [AvaloniaFact]
    public void A_verb_button_in_the_toolbar_is_compact()
    {
        var verb = new Button { Content = "New…", Classes = { "pane-verb" } };
        var (_, _) = Show(new BrowserPane { Toolbar = new StackPanel { Children = { verb } } });

        Assert.Equal(new Thickness(8, 3), verb.Padding);
        Assert.Equal(13, verb.FontSize);
    }
}
