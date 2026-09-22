// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.Core.HostFiles;

namespace LizTerm.App.HostFiles;

/// <summary>The stamps (<c>ETag</c> values) of the members and datasets this session window has downloaded or
/// written, keyed by <see cref="HostPath.ToString"/> (spec §5.1). An upload that replaces a remembered member sends
/// its stamp, so a change on the host since the download is caught. One per <see cref="HostFileAccess"/>, so per
/// host by construction. Thread-safe: a download batch remembers from two transfers at once.</summary>
public sealed class EtagMemory
{
    private readonly Dictionary<string, string> _stamps = new(StringComparer.Ordinal);
    private readonly object _lock = new();

    /// <summary>Keeps <paramref name="etag"/> for the path, as given; null (a host that sent none) forgets it.</summary>
    public void Remember(HostPath path, string? etag)
    {
        lock (_lock)
        {
            if (etag is null) _stamps.Remove(path.ToString());
            else _stamps[path.ToString()] = etag;
        }
    }

    public string? TryGet(HostPath path)
    {
        lock (_lock) return _stamps.GetValueOrDefault(path.ToString());
    }

    public void Forget(HostPath path)
    {
        lock (_lock) _stamps.Remove(path.ToString());
    }

    /// <summary>Drops the dataset's own entry and every member's: a deleted dataset.</summary>
    public void ForgetUnder(HostPath dataset)
    {
        lock (_lock)
        {
            foreach (var key in KeysUnder(dataset.Dataset!)) _stamps.Remove(key);
        }
    }

    /// <summary>A rename: a member's entry moves to its new name; a dataset's moves with everything under it.</summary>
    public void Move(HostPath from, HostPath to)
    {
        // A rename onto its own name moves nothing, and must not clear the stamps as stale ones.
        if (from.ToString() == to.ToString()) return;
        lock (_lock)
        {
            // The host refused nothing, so nothing stood at the new name: a stamp left there is a stale one.
            if (from.Kind == HostPathKind.Member)
            {
                var had = _stamps.Remove(from.ToString(), out var stamp);
                _stamps.Remove(to.ToString());
                if (had) _stamps[to.ToString()] = stamp!;
                return;
            }
            var moving = KeysUnder(from.Dataset!);
            foreach (var stale in KeysUnder(to.Dataset!)) _stamps.Remove(stale);
            foreach (var key in moving)
            {
                var stamp = _stamps[key];
                _stamps.Remove(key);
                _stamps[to.Dataset + key[from.Dataset!.Length..]] = stamp;
            }
        }
    }

    public int Count
    {
        get { lock (_lock) return _stamps.Count; }
    }

    /// <summary>The dataset's own key and its members' (<c>NAME</c> and <c>NAME(…)</c>), never a longer name's.
    /// Materialised, since the callers remove while they walk.</summary>
    private List<string> KeysUnder(string dataset) =>
        _stamps.Keys.Where(key => key == dataset || key.StartsWith(dataset + "(", StringComparison.Ordinal)).ToList();
}
