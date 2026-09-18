// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

namespace LizTerm.App.Dialogs;

/// <summary>Asks whether to start a wire log, having said what one records. Injected like
/// <see cref="ICertificatePrompt"/> so tests answer without a window. A null prompt on the view model declines:
/// no way to show the warning is not a reason to record the session anyway.</summary>
public interface IWireLogPrompt
{
    Task<bool> ConfirmAsync(WireLogPromptRequest request);
}

/// <param name="ProfileName">The session being recorded, so the dialog can name it.</param>
/// <param name="Directory">Where the file will be written, so the dialog can say where it lands.</param>
public sealed record WireLogPromptRequest(string ProfileName, string Directory);
