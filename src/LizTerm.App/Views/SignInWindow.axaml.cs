// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Controls;
using Avalonia.Interactivity;
using LizTerm.App.Dialogs;
using LizTerm.Core.HostFiles;

namespace LizTerm.App.Views;

/// <summary>The REST sign-in (spec §3.2). Cancel and the title bar both answer null. The password lives only in the
/// text box and the <see cref="HostCredentials"/> this returns.</summary>
public partial class SignInWindow : Window
{
    /// <summary>Design-time only.</summary>
    public SignInWindow() : this(new CredentialPromptRequest("MVS/CE", "http://mvs.example:8080/zosmf", "MVSCE02", true)) { }

    public SignInWindow(CredentialPromptRequest request)
    {
        InitializeComponent();
        HostText.Text = $"{request.ProfileName} · {request.Url}";
        RetryText.IsVisible = request.IsRetry;
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
            MissingText.Text = "Enter the userid and the password.";
            MissingText.IsVisible = true;
            return;
        }
        Close(new HostCredentials(userid, password));
    }

    private void OnSignInClick(object? sender, RoutedEventArgs e) => SignIn();

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(null);
}
