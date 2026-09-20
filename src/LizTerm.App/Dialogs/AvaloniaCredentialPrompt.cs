// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Controls;
using LizTerm.App.Views;
using LizTerm.Core.HostFiles;

namespace LizTerm.App.Dialogs;

/// <summary>Opens <see cref="SignInWindow"/> modally over its owner, which is mvsMF Access or the profile
/// editor. Closing the window any way but Sign In answers null.</summary>
public sealed class AvaloniaCredentialPrompt(Window owner) : ICredentialPrompt
{
    public Task<HostCredentials?> AskAsync(CredentialPromptRequest request) =>
        new SignInWindow(request).ShowDialogAbove<HostCredentials?>(owner);
}
