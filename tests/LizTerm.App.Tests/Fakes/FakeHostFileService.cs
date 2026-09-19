// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Text.RegularExpressions;
using LizTerm.Core.HostFiles;

namespace LizTerm.App.Tests.Fakes;

/// <summary>An in-memory mvsMF for the App tests. Every call is logged as "op:target" — list:&lt;pattern&gt;,
/// members:&lt;dsn&gt;, readtext:&lt;path&gt;, readbinary:&lt;path&gt;, writetext:&lt;path&gt;:&lt;lines&gt;,
/// writebinary:&lt;path&gt;, delete:&lt;path&gt;, info, signout — and a <see cref="Failures"/> entry under the same key
/// (without the line count) makes that call throw.</summary>
public sealed class FakeHostFileService : IHostFileService
{
    private readonly object _lock = new();
    private int _running;

    public List<HostFileEntry> Datasets { get; } = [];
    /// <summary>Dataset name → member names, in host order.</summary>
    public Dictionary<string, List<string>> Members { get; } = [];
    /// <summary>HostPath.ToString() → records.</summary>
    public Dictionary<string, List<string>> Text { get; } = [];
    public Dictionary<string, byte[]> Binary { get; } = [];
    public Dictionary<string, Exception> Failures { get; } = [];
    /// <summary>When set, a text write stores what this returns for (path, lines) instead of the lines: a host that
    /// alters what it stores.</summary>
    public Func<string, IReadOnlyList<string>, List<string>>? StoreTransform { get; set; }
    public HostServerInfo Info { get; set; } = new("mvsMF", "1.1.0", "MVS 3.8j");
    /// <summary>When set, every call waits for it (and for its token) after being logged.</summary>
    public TaskCompletionSource? Gate { get; set; }
    public List<string> Calls { get; } = [];
    /// <summary>Every list call's request, in order, beside its "list:" or "members:" entry in <see cref="Calls"/>.</summary>
    public List<HostListRequest> ListRequests { get; } = [];
    public int MaxConcurrent { get; private set; }
    public bool Disposed { get; private set; }

    public void AddDataset(string name, string dsorg = "PO", string recfm = "FB", int lrecl = 80, int blksize = 19040, params string[] members)
    {
        Datasets.Add(new HostFileEntry(name, HostFileEntryKind.Dataset, new DatasetAttributes(dsorg, recfm, lrecl, blksize, "PUB000")));
        if (dsorg == "PO") Members[name] = [.. members];
    }

    public string[] CallsSnapshot()
    {
        lock (_lock) return [.. Calls];
    }

    public async Task SignOutAsync(HostSessionToken token, CancellationToken cancellationToken = default)
    {
        // Through EnterAsync like every other operation, so Failures["signout"] throws and Gate holds it.
        await EnterAsync("signout", "signout", cancellationToken);
        Leave();
    }

    public async Task<HostServerInfo> GetServerInfoAsync(CancellationToken cancellationToken = default)
    {
        await EnterAsync("info", "info", cancellationToken);
        try { return Info; }
        finally { Leave(); }
    }

    public async Task<HostFileListing> ListDatasetsAsync(string pattern, HostListRequest request, CancellationToken cancellationToken = default)
    {
        await EnterAsync($"list:{pattern}", $"list:{pattern}", cancellationToken);
        try { lock (_lock) return PageOf(Datasets, request with { NamePattern = null }); }
        finally { Leave(); }
    }

    public async Task<HostFileListing> ListMembersAsync(HostPath dataset, HostListRequest request, CancellationToken cancellationToken = default)
    {
        await EnterAsync($"members:{dataset}", $"members:{dataset}", cancellationToken);
        try
        {
            lock (_lock)
            {
                if (Members.TryGetValue(dataset.Dataset, out var names))
                    return PageOf(names.Select(n => new HostFileEntry(n, HostFileEntryKind.Member)), request);
                throw Datasets.Any(d => d.Name == dataset.Dataset)
                    ? new HostFileException(HostFileErrorKind.InvalidRequest,
                        $"{dataset}: the host refused the request (Dataset is not partitioned).", 1, "Dataset is not partitioned")
                    : Missing(dataset);
            }
        }
        finally { Leave(); }
    }

    public async Task<IReadOnlyList<string>> ReadTextAsync(HostPath path, IProgress<long>? progress = null, CancellationToken cancellationToken = default)
    {
        await EnterAsync($"readtext:{path}", $"readtext:{path}", cancellationToken);
        try
        {
            List<string> lines;
            lock (_lock)
                lines = Text.TryGetValue(path.ToString(), out var found) ? [.. found] : throw Missing(path);
            progress?.Report(lines.Sum(l => l.Length + 1));
            return lines;
        }
        finally { Leave(); }
    }

    public async Task<long> ReadBinaryAsync(HostPath path, Stream destination, IProgress<long>? progress = null, CancellationToken cancellationToken = default)
    {
        await EnterAsync($"readbinary:{path}", $"readbinary:{path}", cancellationToken);
        try
        {
            byte[] bytes;
            lock (_lock) bytes = Binary.TryGetValue(path.ToString(), out var found) ? found : throw Missing(path);
            await destination.WriteAsync(bytes, cancellationToken);
            progress?.Report(bytes.Length);
            return bytes.Length;
        }
        finally { Leave(); }
    }

    public async Task WriteTextAsync(HostPath path, IReadOnlyList<string> lines, CancellationToken cancellationToken = default)
    {
        await EnterAsync($"writetext:{path}:{lines.Count}", $"writetext:{path}", cancellationToken);
        try
        {
            lock (_lock)
            {
                Text[path.ToString()] = StoreTransform?.Invoke(path.ToString(), lines) ?? [.. lines];
                AddMember(path);
            }
        }
        finally { Leave(); }
    }

    public async Task WriteBinaryAsync(HostPath path, Stream source, CancellationToken cancellationToken = default)
    {
        await EnterAsync($"writebinary:{path}", $"writebinary:{path}", cancellationToken);
        try
        {
            using var copy = new MemoryStream();
            await source.CopyToAsync(copy, cancellationToken);
            lock (_lock)
            {
                Binary[path.ToString()] = copy.ToArray();
                AddMember(path);
            }
        }
        finally { Leave(); }
    }

    public async Task DeleteAsync(HostPath path, CancellationToken cancellationToken = default)
    {
        await EnterAsync($"delete:{path}", $"delete:{path}", cancellationToken);
        try
        {
            lock (_lock)
            {
                if (path.Member is { } member && Members.TryGetValue(path.Dataset, out var names)) names.Remove(member);
                Text.Remove(path.ToString());
                Binary.Remove(path.ToString());
            }
        }
        finally { Leave(); }
    }

    public void Dispose() => Disposed = true;

    /// <summary>Pages the way the backend over a host does: the pattern narrows, the continuation (the last name of
    /// the page before) skips past that name, or resumes at the next one when it has gone meanwhile, and the limit
    /// cuts, with the last name handed back while anything is left.</summary>
    private HostFileListing PageOf(IEnumerable<HostFileEntry> all, HostListRequest request)
    {
        ListRequests.Add(request);
        var list = all.ToList();
        if (request.NamePattern is { } pattern)
        {
            var regex = new Regex("^" + Regex.Escape(pattern.ToUpperInvariant()).Replace("\\*", ".*").Replace("%", ".") + "$");
            list = list.Where(e => regex.IsMatch(e.Name)).ToList();
        }
        if (request.Continuation is { } after)
        {
            var at = list.FindIndex(e => e.Name == after);
            list = at >= 0 ? list.Skip(at + 1).ToList() : list.SkipWhile(e => string.CompareOrdinal(e.Name, after) < 0).ToList();
        }
        if (request.MaxItems > 0 && list.Count > request.MaxItems)
            return new HostFileListing(list.Take(request.MaxItems).ToList(), list[request.MaxItems - 1].Name);
        return new HostFileListing(list, null);
    }

    private void AddMember(HostPath path)
    {
        if (path.Member is { } member && Members.TryGetValue(path.Dataset, out var names) && !names.Contains(member))
            names.Add(member);
    }

    private static HostFileException Missing(HostPath path) => new(HostFileErrorKind.NotFound, $"{path}: not found.", 5);

    private async Task EnterAsync(string call, string failureKey, CancellationToken token)
    {
        Exception? failure;
        lock (_lock)
        {
            Calls.Add(call);
            _running++;
            MaxConcurrent = Math.Max(MaxConcurrent, _running);
            Failures.TryGetValue(failureKey, out failure);
        }
        try
        {
            if (Gate is { } gate) await gate.Task.WaitAsync(token);
            token.ThrowIfCancellationRequested();
            if (failure is not null) throw failure;
        }
        catch
        {
            Leave();
            throw;
        }
    }

    private void Leave()
    {
        lock (_lock) _running--;
    }
}
