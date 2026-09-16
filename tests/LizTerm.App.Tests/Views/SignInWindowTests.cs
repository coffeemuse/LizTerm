// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using LizTerm.App.Dialogs;
using LizTerm.App.Views;
using LizTerm.Core.HostFiles;

namespace LizTerm.App.Tests.Views;

public class SignInWindowTests
{
    private static SignInWindow Show(string? userid = "MVSCE02", bool retry = false)
    {
        var window = new SignInWindow(new CredentialPromptRequest("MVS/CE", "http://mvs:8080/zosmf", userid, retry));
        window.Show();
        return window;
    }

    [AvaloniaFact]
    public void Shows_the_profile_and_url_and_prefills_the_userid()
    {
        var window = Show();
        Assert.Equal("MVS/CE · http://mvs:8080/zosmf", window.FindControl<TextBlock>("HostText")!.Text);
        Assert.Equal("MVSCE02", window.FindControl<TextBox>("UseridBox")!.Text);
        Assert.False(window.FindControl<TextBlock>("RetryText")!.IsVisible);
        Assert.Equal('•', window.FindControl<TextBox>("PasswordBox")!.PasswordChar);
        Assert.Equal("Sign in to mvsMF", window.Title);
    }

    [AvaloniaFact]
    public void A_retry_says_the_host_refused_with_a_mark_and_words()
    {
        var text = Show(retry: true).FindControl<TextBlock>("RetryText")!;
        Assert.True(text.IsVisible);
        Assert.Equal("✗ The host rejected the userid or password. Try again.", text.Text);
    }

    [AvaloniaFact]
    public async Task Sign_in_returns_the_upper_cased_userid_and_the_password()
    {
        var owner = new Window();
        owner.Show();
        var window = new SignInWindow(new CredentialPromptRequest("MVS/CE", "http://mvs/zosmf", null, false));
        var result = window.ShowDialogAbove<HostCredentials?>(owner);
        window.FindControl<TextBox>("UseridBox")!.Text = " ibmuser ";
        window.FindControl<TextBox>("PasswordBox")!.Text = "secret";

        window.SignIn();

        var credentials = await result;
        Assert.Equal("IBMUSER", credentials!.Userid);
        Assert.Equal("secret", credentials.Password);
    }

    [AvaloniaFact]
    public void A_blank_userid_or_password_keeps_the_window_open_and_says_why()
    {
        var window = Show(userid: null);
        window.FindControl<TextBox>("PasswordBox")!.Text = "secret";

        window.SignIn();

        Assert.True(window.IsVisible);
        Assert.True(window.FindControl<TextBlock>("MissingText")!.IsVisible);
        Assert.Equal("Enter the userid and the password.", window.FindControl<TextBlock>("MissingText")!.Text);
    }

    [AvaloniaFact]
    public async Task Cancel_and_the_title_bar_both_answer_null()
    {
        var owner = new Window();
        owner.Show();
        var cancelled = new SignInWindow(new CredentialPromptRequest("p", "u", "U", false));
        var first = cancelled.ShowDialogAbove<HostCredentials?>(owner);
        cancelled.FindControl<Button>("CancelButton")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Null(await first);

        var closed = new SignInWindow(new CredentialPromptRequest("p", "u", "U", false));
        var second = closed.ShowDialogAbove<HostCredentials?>(owner);
        closed.Close();
        Assert.Null(await second);
    }

    [AvaloniaFact]
    public async Task The_avalonia_prompt_opens_the_window_over_its_owner_and_maps_close_to_null()
    {
        var owner = new Window();
        owner.Show();
        var asking = new AvaloniaCredentialPrompt(owner).AskAsync(new CredentialPromptRequest("p", "u", "U", false));

        var dialog = Assert.IsType<SignInWindow>(Assert.Single(owner.OwnedWindows));
        dialog.Close();

        Assert.Null(await asking);
    }
}
