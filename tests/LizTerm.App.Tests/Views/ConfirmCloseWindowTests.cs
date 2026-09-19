// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using LizTerm.App.Dialogs;
using LizTerm.App.Views;

namespace LizTerm.App.Tests.Views;

/// <summary>The question asked before a connected session is closed (#151). Every way out but the Disconnect
/// button answers Keep Connected, and Keep Connected is the default, so a stray Enter confirms nothing.</summary>
public class ConfirmCloseWindowTests
{
    private static readonly ClosePromptRequest Request = ClosePromptRequest.ForWindow("MVS/CE");

    private static (ConfirmCloseWindow Dialog, Task<bool> Answer) Open(ClosePromptRequest? request = null)
    {
        var owner = new Window();
        owner.Show();
        var dialog = new ConfirmCloseWindow(request ?? Request);
        return (dialog, dialog.ShowDialogAbove<bool>(owner));
    }

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    [AvaloniaFact]
    public void The_dialog_shows_the_request_and_names_its_buttons_with_verbs()
    {
        var (dialog, _) = Open();

        Assert.Equal("Close MVS/CE?", dialog.Title);
        Assert.Equal(Request.Message, dialog.FindControl<TextBlock>("MessageText")!.Text);
        Assert.Equal("Disconnect", dialog.FindControl<Button>("DisconnectButton")!.Content);
        Assert.Equal("Keep Connected", dialog.FindControl<Button>("KeepButton")!.Content);
        dialog.Close();
    }

    [AvaloniaFact]
    public void Keep_Connected_is_the_default_and_the_escape_and_Disconnect_is_neither()
    {
        var (dialog, _) = Open();
        var keep = dialog.FindControl<Button>("KeepButton")!;
        var disconnect = dialog.FindControl<Button>("DisconnectButton")!;

        Assert.True(keep.IsDefault);
        Assert.True(keep.IsCancel);
        Assert.False(disconnect.IsDefault);
        Assert.False(disconnect.IsCancel);
        dialog.Close();
    }

    [AvaloniaFact]
    public async Task Disconnect_answers_true()
    {
        var (dialog, answer) = Open();

        Click(dialog.FindControl<Button>("DisconnectButton")!);

        Assert.True(await answer);
    }

    [AvaloniaFact]
    public async Task Keep_Connected_answers_false()
    {
        var (dialog, answer) = Open();

        Click(dialog.FindControl<Button>("KeepButton")!);

        Assert.False(await answer);
    }

    /// <summary>The title bar's close button is not an answer; it is treated as the safe one.</summary>
    [AvaloniaFact]
    public async Task Closing_the_dialog_any_other_way_answers_false()
    {
        var (dialog, answer) = Open();

        dialog.Close();

        Assert.False(await answer);
    }

    [AvaloniaFact]
    public void The_quit_request_puts_its_own_verb_on_the_button()
    {
        var (dialog, _) = Open(ClosePromptRequest.ForQuit(2));

        Assert.Equal("Quit LizTerm?", dialog.Title);
        Assert.Equal("Disconnect and Quit", dialog.FindControl<Button>("DisconnectButton")!.Content);
        dialog.Close();
    }
}
