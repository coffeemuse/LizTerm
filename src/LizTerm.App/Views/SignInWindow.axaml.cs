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
        HostText.Text = $"{request.ProfileName} · {request.Url}";
        ReasonText.Text = request.Reason switch
        {
            SignInReason.Rejected => "✗ The userid or password was not accepted. Try again.",
            // Both lines are drawn in the error red, so both lead with a mark: colour never carries meaning alone.
            SignInReason.Expired => "⚠ Your mvsMF session has expired. Sign in again.",
            _ => "",
        };
        ReasonText.IsVisible = request.Reason != SignInReason.First;
        UseridBox.Text = request.Userid ?? "";
        // The first box the user has to type in takes the keyboard.
        Opened += (_, _) => (string.IsNullOrEmpty(UseridBox.Text) ? UseridBox : PasswordBox).Focus();
    }

    internal void SignIn()
    {
        var userid = (UseridBox.Text ?? "").Trim().ToUpperInvariant();
        var password = PasswordBox.Text ?? "";
        if (userid.Length == 0 || password.Length == 0)
        {
            MissingText.Text = "✗ Enter the userid and the password.";
            MissingText.IsVisible = true;
            return;
        }
        Close(new HostCredentials(userid, password));
    }

    private void OnSignInClick(object? sender, RoutedEventArgs e) => SignIn();

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(null);
}
