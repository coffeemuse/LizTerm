// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.App.Files;

namespace LizTerm.App.Tests.Fakes;

public sealed class FakeFolderOpener : IFolderOpener
{
    public bool Result { get; set; } = true;
    public Exception? Exception { get; set; }
    public List<string> Opened { get; } = [];

    public Task<bool> OpenAsync(string directory)
    {
        Opened.Add(directory);
        return Exception is null ? Task.FromResult(Result) : Task.FromException<bool>(Exception);
    }
}
