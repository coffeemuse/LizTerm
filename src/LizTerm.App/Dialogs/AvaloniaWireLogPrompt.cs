// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Controls;
using LizTerm.App.Views;

namespace LizTerm.App.Dialogs;

/// <summary>Opens <see cref="WireLogWindow"/> modally over the session window.</summary>
public sealed class AvaloniaWireLogPrompt(Window owner) : IWireLogPrompt
{
    public async Task<bool> ConfirmAsync(WireLogPromptRequest request) =>
        await new WireLogWindow(request).ShowDialogAbove<bool>(owner);
}
