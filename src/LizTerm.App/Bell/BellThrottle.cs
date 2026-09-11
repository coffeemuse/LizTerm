// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Diagnostics;

namespace LizTerm.App.Bell;

/// <summary>At most one bell per interval, measured from the last bell that was admitted. Later bells inside the
/// interval are refused, not queued: a queued bell would ring for something the user has already moved past. A
/// bell exactly at the boundary is admitted, so "at most two per second" means the second may land at 500 ms.
/// The timestamp source is injected (Stopwatch ticks) so the tests use explicit times and never sleep. Not
/// thread-safe by design: SessionViewModel calls it on the UI thread only.</summary>
public sealed class BellThrottle(TimeSpan minimum, Func<long> timestamp)
{
    private readonly long _minimumTicks = (long)(minimum.TotalSeconds * Stopwatch.Frequency);
    private long? _lastAdmitted;

    public BellThrottle(TimeSpan minimum) : this(minimum, Stopwatch.GetTimestamp) { }

    public bool TryAdmit()
    {
        var now = timestamp();
        if (_lastAdmitted is { } last && now - last < _minimumTicks) return false;
        _lastAdmitted = now;
        return true;
    }
}
