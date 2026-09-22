// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using LizTerm.App.Dialogs;
using LizTerm.App.Startup;
using LizTerm.App.Views;

namespace LizTerm.App.Tests.Views;

/// <summary>The launch notice for a keymap.json this build cannot read (#168). It only tells: the one way out is
/// in Preferences > Keyboard, so no stray Enter here can move a file.</summary>
public class KeymapNoticeWindowTests
{
    private static readonly KeymapNotice Notice =
        new("keymap.json could not be read. It is not valid JSON. Line 4: a trailing comma.", "/config/keymap.json");

    private static (KeymapNoticeWindow Dialog, Task Shown) Open()
    {
        var owner = new Window();
        owner.Show();
        var dialog = new KeymapNoticeWindow(Notice);
        return (dialog, dialog.ShowDialogAbove(owner));
    }

    [AvaloniaFact]
    public void It_says_what_is_wrong_names_the_file_and_points_at_the_way_out()
    {
        var (dialog, _) = Open();

        Assert.Equal(KeymapNotice.Title, dialog.Title);
        Assert.Equal(Notice.Message, dialog.FindControl<TextBlock>("MessageText")!.Text);
        Assert.Equal(Notice.Path, dialog.FindControl<SelectableTextBlock>("PathText")!.Text);
        Assert.Equal(KeymapNotice.Pointer, dialog.FindControl<TextBlock>("PointerText")!.Text);
        dialog.Close();
    }

    /// <summary>One button, and it is both the default and the Escape action: there is nothing here to get wrong.</summary>
    [AvaloniaFact]
    public async Task The_only_button_dismisses_it_and_answers_Enter_and_Escape_alike()
    {
        var (dialog, shown) = Open();
        var ok = dialog.FindControl<Button>("DismissButton")!;

        Assert.Equal(KeymapNotice.DismissLabel, ok.Content);
        Assert.True(ok.IsDefault);
        Assert.True(ok.IsCancel);

        ok.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        await shown;
    }
}
