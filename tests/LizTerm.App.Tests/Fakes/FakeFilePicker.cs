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

    /// <summary>"open" for the send dialog, "save:<suggested name>" for the receive dialog.</summary>
    public List<string> Calls { get; } = [];

    public Task<string?> PickFileToSendAsync()
    {
        Calls.Add("open");
        return Exception is not null ? Task.FromException<string?>(Exception) : Task.FromResult(Result);
    }

    public Task<string?> PickSaveLocationAsync(string suggestedFileName, string title)
    {
        Calls.Add("save:" + suggestedFileName);
        return Exception is not null ? Task.FromException<string?>(Exception) : Task.FromResult(Result);
    }
}
