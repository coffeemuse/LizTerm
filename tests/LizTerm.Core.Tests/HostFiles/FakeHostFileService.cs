// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.Core.HostFiles;

namespace LizTerm.Core.Tests.HostFiles;

/// <summary>An in-memory host keyed by <see cref="HostPath.ToString"/>. Every write stamps its target
/// <c>write-N</c> (<see cref="Etags"/>) and records the <c>ifMatch</c> it was given (<see cref="IfMatches"/>); a
/// read logs <c>:etag</c> when it asked for the stamp, and gets one only then.</summary>
internal sealed class FakeHostFileService : IHostFileService
{
    private int _writes;

    public Dictionary<string, List<string>> Text { get; } = [];
    public Dictionary<string, byte[]> Binary { get; } = [];
    public Dictionary<string, string> Etags { get; } = [];
    public List<string?> IfMatches { get; } = [];
    public List<string> Calls { get; } = [];

    /// <summary>Applied to what a text write stores, to play a host that alters data.</summary>
    public Func<IReadOnlyList<string>, List<string>> StoreTransform { get; set; } = lines => [.. lines];

    /// <summary>Thrown by reads after <see cref="BytesBeforeFailure"/> bytes have been written.</summary>
    public Exception? ReadFailure { get; set; }
    public int BytesBeforeFailure { get; set; }

    public Task<HostServerInfo> GetServerInfoAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new HostServerInfo("fake", "1", "test"));

    public Task<HostFileListing> ListDatasetsAsync(string pattern, HostListRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult(new HostFileListing([], null));

    public Task<HostFileListing> ListMembersAsync(HostPath dataset, HostListRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult(new HostFileListing([], null));

    public Task<HostFileListing> ListDirectoryAsync(HostPath directory, HostListRequest request, CancellationToken cancellationToken = default)
    {
        Calls.Add($"listdir:{directory}");
        return Task.FromResult(new HostFileListing([], null));
    }

    public Task<HostTextRead> ReadTextAsync(HostPath path, IProgress<long>? progress = null, bool withEtag = false, CancellationToken cancellationToken = default)
    {
        Calls.Add($"readtext:{path}{(withEtag ? ":etag" : "")}");
        cancellationToken.ThrowIfCancellationRequested();
        if (ReadFailure is not null) throw ReadFailure;
        var lines = Text[path.ToString()];
        progress?.Report(lines.Sum(l => l.Length + 1));
        return Task.FromResult(new HostTextRead(lines, withEtag ? Etags.GetValueOrDefault(path.ToString()) : null));
    }

    public async Task<HostBinaryRead> ReadBinaryAsync(HostPath path, Stream destination, IProgress<long>? progress = null, bool withEtag = false, CancellationToken cancellationToken = default)
    {
        Calls.Add($"readbinary:{path}{(withEtag ? ":etag" : "")}");
        if (ReadFailure is not null)
        {
            await destination.WriteAsync(new byte[BytesBeforeFailure], cancellationToken);
            throw ReadFailure;
        }
        var bytes = Binary[path.ToString()];
        await destination.WriteAsync(bytes, cancellationToken);
        progress?.Report(bytes.Length);
        return new HostBinaryRead(bytes.Length, withEtag ? Etags.GetValueOrDefault(path.ToString()) : null);
    }

    public Task<string?> WriteTextAsync(HostPath path, IReadOnlyList<string> lines, string? ifMatch = null, CancellationToken cancellationToken = default)
    {
        Calls.Add($"writetext:{path}:{lines.Count}");
        IfMatches.Add(ifMatch);
        Text[path.ToString()] = StoreTransform(lines);
        return Task.FromResult<string?>(Stamp(path));
    }

    public async Task<string?> WriteBinaryAsync(HostPath path, Stream source, string? ifMatch = null, CancellationToken cancellationToken = default)
    {
        Calls.Add($"writebinary:{path}");
        IfMatches.Add(ifMatch);
        using var copy = new MemoryStream();
        await source.CopyToAsync(copy, cancellationToken);
        Binary[path.ToString()] = copy.ToArray();
        return Stamp(path);
    }

    public Task CreateDatasetAsync(HostPath dataset, DatasetAllocation allocation, CancellationToken cancellationToken = default)
    {
        Calls.Add($"create:{dataset}");
        return Task.CompletedTask;
    }

    public Task CreateDirectoryAsync(HostPath directory, CancellationToken cancellationToken = default)
    {
        Calls.Add($"mkdir:{directory}");
        return Task.CompletedTask;
    }

    public Task RenameAsync(HostPath from, string newName, CancellationToken cancellationToken = default)
    {
        Calls.Add($"rename:{from}:{newName}");
        return Task.CompletedTask;
    }

    public Task DeleteAsync(HostPath path, CancellationToken cancellationToken = default)
    {
        Calls.Add($"delete:{path}");
        Text.Remove(path.ToString());
        Binary.Remove(path.ToString());
        Etags.Remove(path.ToString());
        return Task.CompletedTask;
    }

    public void Dispose() => Calls.Add("dispose");

    private string Stamp(HostPath path) => Etags[path.ToString()] = $"write-{++_writes}";
}
