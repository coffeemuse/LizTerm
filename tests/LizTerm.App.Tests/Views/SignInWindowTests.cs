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
    private static SignInWindow Show(string? userid = "MVSCE02", SignInReason reason = SignInReason.First)
    {
        var window = new SignInWindow(new CredentialPromptRequest("MVS/CE", "http://mvs:8080/zosmf", userid, reason));
        window.Show();
        return window;
    }

    [AvaloniaFact]
    public void Names_the_profile_and_the_url_on_their_own_lines_and_prefills_the_userid()
    {
        var window = Show();
        Assert.Equal("MVS/CE", window.FindControl<TextBlock>("ProfileText")!.Text);
        Assert.Equal("http://mvs:8080/zosmf", window.FindControl<TextBlock>("UrlText")!.Text);
        Assert.Equal("MVSCE02", window.FindControl<TextBox>("UseridBox")!.Text);
        Assert.Equal('•', window.FindControl<TextBox>("PasswordBox")!.PasswordChar);
    }

    [AvaloniaFact]
    public void The_title_names_the_profile_so_the_window_list_tells_two_prompts_apart()
    {
        Assert.Equal("Sign in to mvsMF — MVS/CE", Show().Title);
    }

    [AvaloniaFact]
    public void The_first_ask_says_why_the_window_is_up_rather_than_showing_a_blank()
    {
        var text = Show().FindControl<TextBlock>("ReasonText")!;
        Assert.True(text.IsVisible);
        Assert.Equal("Sign in to access this host via mvsMF.", text.Text);
        // Neither a failure nor a warning: no mark, and the neutral colour that goes with it.
        Assert.Contains("note", text.Classes);
    }

    [AvaloniaFact]
    public void A_rejected_password_says_so_with_a_mark_and_words()
    {
        var text = Show(reason: SignInReason.Rejected).FindControl<TextBlock>("ReasonText")!;
        Assert.True(text.IsVisible);
        Assert.Equal("✗ mvsMF did not accept that userid and password.", text.Text);
        Assert.Contains("error", text.Classes);
    }

    [AvaloniaFact]
    public void An_expired_session_is_a_warning_not_an_error_and_never_blames_the_password()
    {
        var text = Show(reason: SignInReason.Expired).FindControl<TextBlock>("ReasonText")!;
        Assert.True(text.IsVisible);
        Assert.Equal("⚠ Your mvsMF session timed out. Sign in to pick up where you left off.", text.Text);
        // The routine idle timeout is amber, not the refused-password red, and the mark agrees with the colour.
        Assert.Contains("warning", text.Classes);
        Assert.DoesNotContain("error", text.Classes);
    }

    [AvaloniaFact]
    public async Task Sign_in_returns_the_upper_cased_userid_and_the_password()
    {
        var owner = new Window();
        owner.Show();
        var window = new SignInWindow(new CredentialPromptRequest("MVS/CE", "http://mvs/zosmf", null, SignInReason.First));
        var result = window.ShowDialogAbove<HostCredentials?>(owner);
        window.FindControl<TextBox>("UseridBox")!.Text = " ibmuser ";
        window.FindControl<TextBox>("PasswordBox")!.Text = "secret";

        window.SignIn();

        var credentials = await result;
        Assert.Equal("IBMUSER", credentials!.Userid);
        Assert.Equal("secret", credentials.Password);
    }

    [AvaloniaTheory]
    [InlineData(null, "", "✗ Enter your userid and password.")]
    [InlineData(null, "secret", "✗ Enter your userid.")]
    [InlineData("   ", "secret", "✗ Enter your userid.")]
    [InlineData("MVSCE02", "", "✗ Enter your password.")]
    public void A_blank_box_keeps_the_window_open_and_names_the_box(string? userid, string password, string expected)
    {
        var window = Show(userid: userid);
        window.FindControl<TextBox>("PasswordBox")!.Text = password;

        window.SignIn();

        Assert.True(window.IsVisible);
        var missing = window.FindControl<TextBlock>("MissingText")!;
        Assert.True(missing.IsVisible);
        Assert.Equal(expected, missing.Text);
    }

    [AvaloniaFact]
    public void A_userid_box_holding_only_spaces_is_emptied_so_it_agrees_with_the_message()
    {
        var window = Show(userid: "   ");
        window.FindControl<TextBox>("PasswordBox")!.Text = "secret";

        window.SignIn();

        Assert.Equal("✗ Enter your userid.", window.FindControl<TextBlock>("MissingText")!.Text);
        // Naming the box is worse than the old flat message if the box it names still looks filled.
        Assert.Empty(window.FindControl<TextBox>("UseridBox")!.Text!);
    }

    [AvaloniaFact]
    public void The_privacy_note_still_says_the_password_is_never_saved_to_disk()
    {
        // docs/privacy.md's guarantee is that LizTerm never writes a password anywhere, and this window is the one
        // screen where a password is typed. "Never saved to disk." has to stay its own sentence: joined to the
        // clause before it, it qualifies only the session token.
        Assert.Equal(
            "Used once to sign in, then discarded. Only the session token is kept, until this session window "
            + "closes. Never saved to disk.",
            Show().FindControl<TextBlock>("PrivacyText")!.Text);
    }

    [AvaloniaFact]
    public async Task Cancel_and_the_title_bar_both_answer_null()
    {
        var owner = new Window();
        owner.Show();
        var cancelled = new SignInWindow(new CredentialPromptRequest("p", "u", "U", SignInReason.First));
        var first = cancelled.ShowDialogAbove<HostCredentials?>(owner);
        cancelled.FindControl<Button>("CancelButton")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Null(await first);

        var closed = new SignInWindow(new CredentialPromptRequest("p", "u", "U", SignInReason.First));
        var second = closed.ShowDialogAbove<HostCredentials?>(owner);
        closed.Close();
        Assert.Null(await second);
    }

    [AvaloniaFact]
    public async Task The_avalonia_prompt_opens_the_window_over_its_owner_and_maps_close_to_null()
    {
        var owner = new Window();
        owner.Show();
        var asking = new AvaloniaCredentialPrompt(owner).AskAsync(new CredentialPromptRequest("p", "u", "U", SignInReason.First));

        var dialog = Assert.IsType<SignInWindow>(Assert.Single(owner.OwnedWindows));
        dialog.Close();

        Assert.Null(await asking);
    }
}
