// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Text.RegularExpressions;
using LizTerm.Core.HostFiles;

namespace LizTerm.App.Tests.Fakes;

/// <summary>An in-memory mvsMF for the App tests. <c>list:&lt;pattern&gt;</c> returns the datasets the pattern
/// matches. Every call is logged as "op:target" — list:&lt;pattern&gt;,
/// members:&lt;dsn&gt;, readtext:&lt;path&gt;, readbinary:&lt;path&gt;, writetext:&lt;path&gt;:&lt;lines&gt;,
/// writebinary:&lt;path&gt;, delete:&lt;path&gt;, create:&lt;dsn&gt;, rename:&lt;from&gt;:&lt;new&gt;, info, signout — and a
/// <see cref="Failures"/> entry under the same key (without the line count) makes that call throw. It fails the way
/// the host does: a read or delete of what is not there is <see cref="HostFileErrorKind.NotFound"/>, a create or
/// rename the local rules refuse throws <see cref="ArgumentException"/> before it is logged, and a stale
/// <c>ifMatch</c> is <see cref="HostFileErrorKind.Conflict"/>.</summary>
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
    /// <summary>HostPath.ToString() → the stamp of the last write, <c>stamp-N</c>; a read returns it and a write
    /// with another <c>ifMatch</c> is a conflict.</summary>
    public Dictionary<string, string> Etags { get; } = [];
    /// <summary>Every write's <c>ifMatch</c>, in order.</summary>
    public List<string?> IfMatches { get; } = [];
    private int _writes;
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
        try { lock (_lock) return PageOf(Datasets.Where(DatasetPattern(pattern)), request with { NamePattern = null }); }
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

    public async Task<HostTextRead> ReadTextAsync(HostPath path, IProgress<long>? progress = null, bool withEtag = false, CancellationToken cancellationToken = default)
    {
        await EnterAsync($"readtext:{path}", $"readtext:{path}", cancellationToken);
        try
        {
            List<string> lines;
            string? etag;
            lock (_lock)
            {
                lines = Text.TryGetValue(path.ToString(), out var found) ? [.. found] : throw Missing(path);
                etag = withEtag ? Etags.GetValueOrDefault(path.ToString()) : null;
            }
            progress?.Report(lines.Sum(l => l.Length + 1));
            return new HostTextRead(lines, etag);
        }
        finally { Leave(); }
    }

    public async Task<HostBinaryRead> ReadBinaryAsync(HostPath path, Stream destination, IProgress<long>? progress = null, bool withEtag = false, CancellationToken cancellationToken = default)
    {
        await EnterAsync($"readbinary:{path}", $"readbinary:{path}", cancellationToken);
        try
        {
            byte[] bytes;
            string? etag;
            lock (_lock)
            {
                bytes = Binary.TryGetValue(path.ToString(), out var found) ? found : throw Missing(path);
                etag = withEtag ? Etags.GetValueOrDefault(path.ToString()) : null;
            }
            await destination.WriteAsync(bytes, cancellationToken);
            progress?.Report(bytes.Length);
            return new HostBinaryRead(bytes.Length, etag);
        }
        finally { Leave(); }
    }

    public async Task<string?> WriteTextAsync(HostPath path, IReadOnlyList<string> lines, string? ifMatch = null, CancellationToken cancellationToken = default)
    {
        await EnterAsync($"writetext:{path}:{lines.Count}", $"writetext:{path}", cancellationToken);
        try
        {
            lock (_lock)
            {
                CheckStamp(path, ifMatch);
                Text[path.ToString()] = StoreTransform?.Invoke(path.ToString(), lines) ?? [.. lines];
                AddMember(path);
                return Stamp(path);
            }
        }
        finally { Leave(); }
    }

    public async Task<string?> WriteBinaryAsync(HostPath path, Stream source, string? ifMatch = null, CancellationToken cancellationToken = default)
    {
        await EnterAsync($"writebinary:{path}", $"writebinary:{path}", cancellationToken);
        try
        {
            using var copy = new MemoryStream();
            await source.CopyToAsync(copy, cancellationToken);
            lock (_lock)
            {
                CheckStamp(path, ifMatch);
                Binary[path.ToString()] = copy.ToArray();
                AddMember(path);
                return Stamp(path);
            }
        }
        finally { Leave(); }
    }

    public async Task CreateDatasetAsync(HostPath dataset, DatasetAllocation allocation, CancellationToken cancellationToken = default)
    {
        // As the backend: refused by the local rules before anything is sent, so nothing is logged.
        if (dataset.Kind != HostPathKind.Dataset) throw new ArgumentException("Only a dataset can be created.", nameof(dataset));
        if (allocation.Problems() is { Count: > 0 } problems)
            throw new ArgumentException(string.Join(" ", problems.Values), nameof(allocation));
        await EnterAsync($"create:{dataset}", $"create:{dataset}", cancellationToken);
        try
        {
            lock (_lock)
            {
                if (Datasets.Any(d => d.Name == dataset.Dataset))
                    throw new HostFileException(HostFileErrorKind.CannotAllocate, $"{dataset}: the host could not allocate it (it may already exist, there may be no space, or you may not be authorized).", 7, "Dynamic allocation Error");
                var partitioned = allocation.Organization == DatasetOrganization.Partitioned;
                Datasets.Add(new HostFileEntry(dataset.Dataset, HostFileEntryKind.Dataset,
                    new DatasetAttributes(partitioned ? "PO" : "PS", allocation.FoldedRecfm, allocation.Lrecl, allocation.Blksize, "PUB000")));
                if (partitioned) Members[dataset.Dataset] = [];
            }
        }
        finally { Leave(); }
    }

    public async Task RenameAsync(HostPath from, string newName, CancellationToken cancellationToken = default)
    {
        // As the backend: a new name the rules refuse throws ArgumentException before anything is sent or logged.
        var target = from.Kind == HostPathKind.Member ? HostPath.ForMember(from.Dataset, newName) : HostPath.ForDataset(newName);
        await EnterAsync($"rename:{from}:{newName}", $"rename:{from}:{newName}", cancellationToken);
        try
        {
            lock (_lock)
            {
                if (from.Member is { } member) RenameMember(from, member, target);
                else RenameDataset(from, target);
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
                // Nothing to remove is the host's 404 (Missing: reason 5 for a member, 4 for a dataset).
                if (path.Member is { } member)
                {
                    var listed = Members.TryGetValue(path.Dataset, out var names) && names.Remove(member);
                    var held = Forget(path.ToString());
                    if (!listed && !held) throw Missing(path);
                }
                else
                {
                    var listed = Datasets.RemoveAll(d => d.Name == path.Dataset) > 0;
                    listed |= Members.Remove(path.Dataset);
                    var keys = KeysUnder(path.Dataset);
                    foreach (var key in keys) Forget(key);
                    if (!listed && keys.Count == 0) throw Missing(path);
                }
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

    /// <summary>A dataset pattern as the host reads it: <c>**</c> any run of qualifiers, <c>*</c> any run within a
    /// qualifier, <c>%</c> one character; case ignored.</summary>
    private static Func<HostFileEntry, bool> DatasetPattern(string pattern)
    {
        var regex = new Regex("^" + Regex.Escape(pattern.Trim().ToUpperInvariant())
            .Replace("\\*\\*", ".*").Replace("\\*", "[^.]*").Replace("%", "[^.]") + "$");
        return entry => regex.IsMatch(entry.Name);
    }

    /// <summary>A write's ifMatch must be the path's current stamp; a stamp the fake never issued, or an old one,
    /// is a conflict, as the host answers 412.</summary>
    private void CheckStamp(HostPath path, string? ifMatch)
    {
        IfMatches.Add(ifMatch);
        if (ifMatch is null) return;
        if (Etags.GetValueOrDefault(path.ToString()) != ifMatch)
            throw new HostFileException(HostFileErrorKind.Conflict, $"{path}: changed on the host since it was read.", 10);
    }

    private string Stamp(HostPath path) => Etags[path.ToString()] = $"stamp-{++_writes}";

    private void RenameMember(HostPath from, string member, HostPath to)
    {
        if (!Members.TryGetValue(from.Dataset, out var names) || !names.Contains(member))
            throw new HostFileException(HostFileErrorKind.NotFound, $"Rename {from} to {to.Member}: not found.", 5);
        if (names.Contains(to.Member!))
            throw new HostFileException(HostFileErrorKind.AlreadyExists, $"Rename {from} to {to.Member}: a member of that name already exists.", 7);
        names[names.IndexOf(member)] = to.Member!;
        Move(from.ToString(), to.ToString());
    }

    private void RenameDataset(HostPath from, HostPath to)
    {
        var index = Datasets.FindIndex(d => d.Name == from.Dataset);
        if (index < 0) throw new HostFileException(HostFileErrorKind.NotFound, $"Rename {from} to {to.Dataset}: not found.", 4);
        // The host does not check the new name first: IDCAMS ALTER refuses it, and the answer is the rename
        // failure's own 500, reason 8, a server error quoting it (rename-target-exists-400 in the compatibility log).
        if (Datasets.Any(d => d.Name == to.Dataset))
            throw new HostFileException(HostFileErrorKind.ServerError, $"Rename {from} to {to.Dataset}: Rename operation failed (reason 8).", 8, "Rename operation failed");
        Datasets[index] = Datasets[index] with { Name = to.Dataset };
        if (Members.Remove(from.Dataset, out var names)) Members[to.Dataset] = names;
        // KeysUnder includes the dataset's own key (a sequential dataset's content), so one loop moves everything.
        foreach (var key in KeysUnder(from.Dataset)) Move(key, to.Dataset + key[from.Dataset.Length..]);
    }

    /// <summary>Every content or stamp key that is the dataset itself or one of its members.</summary>
    private List<string> KeysUnder(string dataset) =>
        Text.Keys.Concat(Binary.Keys).Concat(Etags.Keys).Distinct()
            .Where(key => key == dataset || key.StartsWith(dataset + "(", StringComparison.Ordinal)).ToList();

    private void Move(string from, string to)
    {
        if (Text.Remove(from, out var text)) Text[to] = text;
        if (Binary.Remove(from, out var bytes)) Binary[to] = bytes;
        if (Etags.Remove(from, out var etag)) Etags[to] = etag;
    }

    /// <summary>Drops every content and stamp entry under <paramref name="key"/>; true when there was one.</summary>
    private bool Forget(string key) => Text.Remove(key) | Binary.Remove(key) | Etags.Remove(key);

    private void AddMember(HostPath path)
    {
        if (path.Member is { } member && Members.TryGetValue(path.Dataset, out var names) && !names.Contains(member))
            names.Add(member);
    }

    private static HostFileException Missing(HostPath path) => new(HostFileErrorKind.NotFound, $"{path}: not found.", path.Member is null ? 4 : 5);

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
