// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.App.Files;

namespace LizTerm.App.Tests.Fakes;

public sealed class FakeFilePicker : IFilePicker
{
    /// <summary>What either dialog returns; null plays a cancelled dialog.</summary>
    public string? Result { get; set; }

    /// <summary>When set, both operations fail with this exception.</summary>
    public Exception? Exception { get; set; }

    /// <summary>"open" for the send dialog, "save:<suggested name>" for the receive dialog,
    /// "open-many:<title>" for the several-files dialog, "folder:<title>" for the folder dialog.</summary>
    public List<string> Calls { get; } = [];

    public Task<string?> PickFileToSendAsync()
    {
        Calls.Add("open");
        return Exception is not null ? Task.FromException<string?>(Exception) : Task.FromResult(Result);
    }

    /// <summary>What the several-files dialog returns; empty plays a cancelled dialog.</summary>
    public IReadOnlyList<string> Results { get; set; } = [];

    /// <summary>What the folder dialog returns; null plays a cancelled dialog.</summary>
    public string? FolderResult { get; set; }

    public Task<IReadOnlyList<string>> PickFilesToSendAsync(string title)
    {
        Calls.Add("open-many:" + title);
        return Exception is not null ? Task.FromException<IReadOnlyList<string>>(Exception) : Task.FromResult(Results);
    }

    public Task<string?> PickFolderAsync(string title)
    {
        Calls.Add("folder:" + title);
        return Exception is not null ? Task.FromException<string?>(Exception) : Task.FromResult(FolderResult);
    }

    /// <summary>The formats the last save dialog was offered, or null when the caller offered none.</summary>
    public IReadOnlyList<SaveFormat>? LastSaveFormats { get; private set; }

    public Task<string?> PickSaveLocationAsync(string suggestedFileName, string title, IReadOnlyList<SaveFormat>? formats = null)
    {
        Calls.Add("save:" + suggestedFileName);
        LastSaveFormats = formats;
        return Exception is not null ? Task.FromException<string?>(Exception) : Task.FromResult(Result);
    }
}
