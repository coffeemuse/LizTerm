// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

namespace LizTerm.Backend.Mvsmf;

/// <summary>A token that fires when nothing has arrived for <paramref name="idle"/>, or when the caller cancels. A
/// large download is never cut off while bytes keep coming.</summary>
internal sealed class IdleTimeout(TimeSpan idle, CancellationToken outer) : IDisposable
{
    private readonly TimeSpan _idle = idle;
    private readonly CancellationTokenSource _source = Start(idle, outer);

    public CancellationToken Token => _source.Token;

    /// <summary>Something arrived (or a new wait begins): restart the clock.</summary>
    public void Reset()
    {
        if (!_source.IsCancellationRequested) _source.CancelAfter(_idle);
    }

    /// <summary>Waiting on the user, not the host: stop the clock until the next <see cref="Reset"/>.</summary>
    public void Pause()
    {
        if (!_source.IsCancellationRequested) _source.CancelAfter(Timeout.InfiniteTimeSpan);
    }

    /// <summary>Whether the token fired for idleness rather than because the caller cancelled.</summary>
    public bool Expired(CancellationToken caller) => _source.IsCancellationRequested && !caller.IsCancellationRequested;

    public void Dispose() => _source.Dispose();

    private static CancellationTokenSource Start(TimeSpan idle, CancellationToken outer)
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(outer);
        source.CancelAfter(idle);
        return source;
    }
}
