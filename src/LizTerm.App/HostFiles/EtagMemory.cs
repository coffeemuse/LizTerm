// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.Core.HostFiles;

namespace LizTerm.App.HostFiles;

/// <summary>The stamps (<c>ETag</c> values) of the members, datasets and UNIX files this session window has downloaded
/// or written, keyed by <see cref="HostPath.ToString"/> (spec §5.1). An upload that replaces a remembered member sends
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

    /// <summary>Drops the path's own entry and everything under it: a deleted dataset and its members, or a deleted
    /// UNIX directory and every file below it.</summary>
    public void ForgetUnder(HostPath path)
    {
        lock (_lock)
        {
            foreach (var key in KeysUnder(path)) _stamps.Remove(key);
        }
    }

    /// <summary>A rename: a member's entry moves to its new name; a dataset's (or a UNIX path's) moves with
    /// everything under it.</summary>
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
            var moving = KeysUnder(from);
            foreach (var stale in KeysUnder(to)) _stamps.Remove(stale);
            var (fromKey, toKey) = (from.ToString(), to.ToString());
            foreach (var key in moving)
            {
                var stamp = _stamps[key];
                _stamps.Remove(key);
                _stamps[toKey + key[fromKey.Length..]] = stamp;
            }
        }
    }

    public int Count
    {
        get { lock (_lock) return _stamps.Count; }
    }

    /// <summary>The path's own key and those under it, never a longer name's: a dataset's members
    /// (<c>NAME</c> and <c>NAME(…)</c>), or a UNIX directory's descendants (<c>/a/b</c> and <c>/a/b/…</c>; every
    /// key at the root). Materialised, since the callers remove while they walk.</summary>
    private List<string> KeysUnder(HostPath path)
    {
        var own = path.ToString();
        var below = path.Kind == HostPathKind.Unix ? (own == "/" ? "/" : own + "/") : own + "(";
        return _stamps.Keys.Where(key => key == own || key.StartsWith(below, StringComparison.Ordinal)).ToList();
    }
}
