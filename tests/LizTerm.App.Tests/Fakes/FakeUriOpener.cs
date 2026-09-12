// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.App.Files;

namespace LizTerm.App.Tests.Fakes;

public sealed class FakeUriOpener : IUriOpener
{
    public bool Result { get; set; } = true;
    public Exception? Exception { get; set; }
    public List<string> Opened { get; } = [];

    public Task<bool> OpenAsync(Uri uri)
    {
        Opened.Add(uri.ToString());
        return Exception is null ? Task.FromResult(Result) : Task.FromException<bool>(Exception);
    }

    public Task<bool> OpenFileAsync(string path)
    {
        Opened.Add(path);
        return Exception is null ? Task.FromResult(Result) : Task.FromException<bool>(Exception);
    }
}
