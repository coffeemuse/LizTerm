// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.Core.Updates;

namespace LizTerm.App.Tests.Fakes;

/// <summary>Returns Result, or throws Exception when set — the FakeUriOpener shape. Calls counts the requests made;
/// a Gate holds every answer back until the test completes it, for what happens while a request is out.</summary>
public sealed class FakeReleaseChecker : IReleaseChecker
{
    public ReleaseInfo Result { get; set; } = new("0.5.2", "https://github.com/coffeemuse/LizTerm/releases/tag/v0.5.2");
    public Exception? Exception { get; set; }
    public TaskCompletionSource? Gate { get; set; }
    public int Calls { get; private set; }

    public async Task<ReleaseInfo> GetLatestReleaseAsync(CancellationToken cancellationToken)
    {
        Calls++;
        if (Gate is not null) await Gate.Task;
        return Exception is null ? Result : throw Exception;
    }
}
