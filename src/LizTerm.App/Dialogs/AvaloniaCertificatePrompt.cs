// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Controls;
using LizTerm.App.Views;

namespace LizTerm.App.Dialogs;

/// <summary>Opens <see cref="CertificateWindow"/> modally over the session window.</summary>
public sealed class AvaloniaCertificatePrompt(Window owner) : ICertificatePrompt
{
    public async Task<CertificateDecision> AskAsync(CertificatePromptRequest request)
    {
        var result = await new CertificateWindow(request).ShowDialogAbove<CertificateDecision?>(owner);
        return result ?? CertificateDecision.Declined;
    }
}
