// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Controls;
using Avalonia.Interactivity;
using LizTerm.App.Dialogs;
using LizTerm.Core.HostFiles;

namespace LizTerm.App.Views;

/// <summary>The REST sign-in (spec §3.2). Cancel and the title bar both answer null. The password lives only in the
/// text box and the <see cref="HostCredentials"/> this returns, which the backend trades for a session token.</summary>
public partial class SignInWindow : Window
{
    /// <summary>Design-time only.</summary>
    public SignInWindow() : this(new CredentialPromptRequest("MVS/CE", "http://mvs.example:8080/zosmf", "MVSCE02", SignInReason.Expired)) { }

    public SignInWindow(CredentialPromptRequest request)
    {
        InitializeComponent();
        // The profile is named in the title as well as in the header, because the window list and the taskbar show
        // only the title, and two session windows on different hosts can each have a prompt up.
        Title = $"Sign in to mvsMF — {request.ProfileName}";
        ProfileText.Text = request.ProfileName;
        UrlText.Text = request.Url;
        // Every line leads with its own mark, so the three registers are told apart without their colours. The
        // first ask carries a line of its own rather than a gap: opened from the profile editor's Test button, the
        // window otherwise appears with no word about who wants a password. It names mvsMF, not what mvsMF is used
        // for today, since jobs and USS are still to come (#17).
        var (reason, severity) = request.Reason switch
        {
            SignInReason.Rejected => ("✗ mvsMF did not accept that userid and password.", "error"),
            SignInReason.Expired => ("⚠ Your mvsMF session timed out. Sign in to pick up where you left off.", "warning"),
            _ => ("Sign in to access this host via mvsMF.", "note"),
        };
        ReasonText.Text = reason;
        ReasonText.Classes.Add(severity);
        UseridBox.Text = request.Userid ?? "";
        // The first box the user has to type in takes the keyboard.
        Opened += (_, _) => (string.IsNullOrEmpty(UseridBox.Text) ? UseridBox : PasswordBox).Focus();
    }

    internal void SignIn()
    {
        var userid = (UseridBox.Text ?? "").Trim().ToUpperInvariant();
        var password = PasswordBox.Text ?? "";
        // Which box is empty, rather than one message for both: the window has two, and the user has just filled in
        // one of them.
        var missing = (userid.Length == 0, password.Length == 0) switch
        {
            (true, true) => "✗ Enter your userid and password.",
            (true, false) => "✗ Enter your userid.",
            (false, true) => "✗ Enter your password.",
            _ => null,
        };
        if (missing is not null)
        {
            MissingText.Text = missing;
            MissingText.IsVisible = true;
            return;
        }
        Close(new HostCredentials(userid, password));
    }

    private void OnSignInClick(object? sender, RoutedEventArgs e) => SignIn();

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(null);
}
