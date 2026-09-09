// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

namespace LizTerm.Tests.Shared;

/// <summary>Polls a condition until it holds or the timeout passes. The one copy: every test project compiles this
/// file in (see each csproj) and imports the namespace globally, so a change to the default timeout — raised once
/// already for CI headroom — cannot land in one project and be missed in another.</summary>
internal static class Wait
{
    public static async Task UntilAsync(Func<bool> condition, string what, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(5));
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("Timed out waiting for " + what);
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }
    }
}
