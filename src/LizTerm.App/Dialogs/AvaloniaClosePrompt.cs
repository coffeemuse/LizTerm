// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Controls;
using LizTerm.App.Views;

namespace LizTerm.App.Dialogs;

/// <summary>Opens <see cref="ConfirmCloseWindow"/> modally over the window that is closing, or for Quit over the
/// current session window.</summary>
public sealed class AvaloniaClosePrompt(Window owner) : IClosePrompt
{
    public async Task<bool> ConfirmAsync(ClosePromptRequest request) =>
        await new ConfirmCloseWindow(request).ShowDialogAbove<bool>(owner);
}
