// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

namespace LizTerm.App.Files;

/// <summary>Local file selection for transfers, injected into the dialog view model so tests can fake it, like
/// <see cref="LizTerm.App.Clipboard.ITextClipboard"/>.</summary>
public interface IFilePicker
{
    /// <summary>OS Open dialog. Null when cancelled or when the choice has no local path.</summary>
    Task<string?> PickFileToSendAsync();

    /// <summary>OS Save dialog, which asks before overwriting an existing file. Null when cancelled. The title
    /// is the caller's because the two callers save different things: a received file, and a screen capture.</summary>
    Task<string?> PickSaveLocationAsync(string suggestedFileName, string title);
}
