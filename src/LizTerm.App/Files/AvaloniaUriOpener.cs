// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace LizTerm.App.Files;

public sealed class AvaloniaUriOpener(TopLevel topLevel) : IUriOpener
{
    public Task<bool> OpenAsync(Uri uri) => topLevel.Launcher.LaunchUriAsync(uri);

    public async Task<bool> OpenFileAsync(string path)
    {
        var file = await topLevel.StorageProvider.TryGetFileFromPathAsync(path);
        return file is not null && await topLevel.Launcher.LaunchFileAsync(file);
    }
}
