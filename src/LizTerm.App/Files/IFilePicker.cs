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

    /// <summary>OS Open dialog allowing several files. Empty when cancelled; files with no local path are left out.</summary>
    Task<IReadOnlyList<string>> PickFilesToSendAsync(string title);

    /// <summary>OS folder chooser. Null when cancelled or when the choice has no local path.</summary>
    Task<string?> PickFolderAsync(string title);

    /// <summary>OS Save dialog, which asks before overwriting an existing file. Null when cancelled. The title
    /// is the caller's because the two callers save different things: a received file, and a screen capture.
    ///
    /// <paramref name="formats"/> fills the dialog's own File Format popup, so the user can see the choice and
    /// have the extension appended for them. Null offers none, which is right for a received file — it can be
    /// anything. A screen capture passes both of its formats, and the first is the one the dialog opens on, so
    /// it must agree with <paramref name="suggestedFileName"/>'s own extension.</summary>
    Task<string?> PickSaveLocationAsync(string suggestedFileName, string title, IReadOnlyList<SaveFormat>? formats = null);
}
