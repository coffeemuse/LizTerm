// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.Core.HostFiles;

namespace LizTerm.App.Tests.Fakes;

/// <summary>An in-memory mvsMF for the App tests. Every call is logged as "op:target" — list:&lt;pattern&gt;,
/// members:&lt;dsn&gt;, readtext:&lt;path&gt;, readbinary:&lt;path&gt;, writetext:&lt;path&gt;:&lt;lines&gt;,
/// writebinary:&lt;path&gt;, delete:&lt;path&gt;, info — and a <see cref="Failures"/> entry under the same key (without
/// the line count) makes that call throw.</summary>
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

    public async Task<HostServerInfo> GetServerInfoAsync(CancellationToken cancellationToken = default)
    {
        await EnterAsync("info", "info", cancellationToken);
        try { return Info; }
        finally { Leave(); }
    }

    public async Task<IReadOnlyList<HostFileEntry>> ListDatasetsAsync(string pattern, CancellationToken cancellationToken = default)
    {
        await EnterAsync($"list:{pattern}", $"list:{pattern}", cancellationToken);
        try { lock (_lock) return [.. Datasets]; }
        finally { Leave(); }
    }

    public async Task<IReadOnlyList<HostFileEntry>> ListMembersAsync(HostPath dataset, CancellationToken cancellationToken = default)
    {
        await EnterAsync($"members:{dataset}", $"members:{dataset}", cancellationToken);
        try
        {
            lock (_lock)
            {
                if (Members.TryGetValue(dataset.Dataset, out var names))
                    return [.. names.Select(n => new HostFileEntry(n, HostFileEntryKind.Member))];
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
