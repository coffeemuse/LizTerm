// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.Core.Updates;

namespace LizTerm.App.Tests.Fakes;

/// <summary>Returns Result, or throws Exception when set — the FakeUriOpener shape.</summary>
public sealed class FakeReleaseChecker : IReleaseChecker
{
    public ReleaseInfo Result { get; set; } = new("0.5.2", "https://github.com/coffeemuse/LizTerm/releases/tag/v0.5.2");
    public Exception? Exception { get; set; }

    public Task<ReleaseInfo> GetLatestReleaseAsync(CancellationToken cancellationToken) =>
        Exception is null ? Task.FromResult(Result) : Task.FromException<ReleaseInfo>(Exception);
}
