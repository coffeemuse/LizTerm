// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace LizTerm.App.Files;

/// <summary>Wraps a top level's storage provider, resolved at each call so the dialog need not be open when the
/// picker is constructed.</summary>
public sealed class AvaloniaFilePicker(TopLevel topLevel) : IFilePicker
{
    public async Task<string?> PickFileToSendAsync()
    {
        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "File to send",
            AllowMultiple = false,
        });
        return files.Count == 0 ? null : files[0].TryGetLocalPath();
    }

    public async Task<IReadOnlyList<string>> PickFilesToSendAsync(string title)
    {
        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = true,
        });
        return [.. files.Select(f => f.TryGetLocalPath()).OfType<string>()];
    }

    public async Task<string?> PickFolderAsync(string title)
    {
        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
        });
        return folders.Count == 0 ? null : folders[0].TryGetLocalPath();
    }

    public async Task<string?> PickSaveLocationAsync(string suggestedFileName, string title, IReadOnlyList<SaveFormat>? formats = null)
    {
        var options = new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedFileName,
        };

        // Left unset when the caller offers no formats, rather than set to an empty list: the platforms differ
        // on what an empty FileTypeChoices means, and "no popup at all" is what a received file wants.
        if (formats is { Count: > 0 })
        {
            options.FileTypeChoices = [.. formats.Select(f => new FilePickerFileType(f.Label) { Patterns = [$"*.{f.Extension}"] })];
            options.DefaultExtension = formats[0].Extension;
        }

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(options);
        return file?.TryGetLocalPath();
    }
}
