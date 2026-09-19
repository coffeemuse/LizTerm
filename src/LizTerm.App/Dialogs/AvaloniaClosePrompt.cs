// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Controls;
using Avalonia.Threading;
using LizTerm.App.Views;

namespace LizTerm.App.Dialogs;

/// <summary>Opens <see cref="ConfirmCloseWindow"/> modally over the window that is closing, or for Quit over the
/// current session window.</summary>
public sealed class AvaloniaClosePrompt(Window owner) : IClosePrompt
{
    public async Task<bool> ConfirmAsync(ClosePromptRequest request)
    {
        var disconnect = await new ConfirmCloseWindow(request).ShowDialogAbove<bool>(owner);
        // The answer lands from the dialog's Closed, before the routed WindowClosedEvent has taken the dialog off
        // the lifetime's window list; a quit repeated from here would count it and be refused (App.Quit says why).
        // One dispatcher turn later it is gone.
        await Dispatcher.UIThread.InvokeAsync(static () => { });
        return disconnect;
    }
}
