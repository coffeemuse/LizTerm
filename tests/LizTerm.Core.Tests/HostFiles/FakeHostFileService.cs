// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.Core.HostFiles;

namespace LizTerm.Core.Tests.HostFiles;

/// <summary>An in-memory host keyed by <see cref="HostPath.ToString"/>.</summary>
internal sealed class FakeHostFileService : IHostFileService
{
    public Dictionary<string, List<string>> Text { get; } = [];
    public Dictionary<string, byte[]> Binary { get; } = [];
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

    public Task<IReadOnlyList<string>> ReadTextAsync(HostPath path, IProgress<long>? progress = null, CancellationToken cancellationToken = default)
    {
        Calls.Add($"readtext:{path}");
        cancellationToken.ThrowIfCancellationRequested();
        if (ReadFailure is not null) throw ReadFailure;
        var lines = Text[path.ToString()];
        progress?.Report(lines.Sum(l => l.Length + 1));
        return Task.FromResult<IReadOnlyList<string>>(lines);
    }

    public async Task<long> ReadBinaryAsync(HostPath path, Stream destination, IProgress<long>? progress = null, CancellationToken cancellationToken = default)
    {
        Calls.Add($"readbinary:{path}");
        if (ReadFailure is not null)
        {
            await destination.WriteAsync(new byte[BytesBeforeFailure], cancellationToken);
            throw ReadFailure;
        }
        var bytes = Binary[path.ToString()];
        await destination.WriteAsync(bytes, cancellationToken);
        progress?.Report(bytes.Length);
        return bytes.Length;
    }

    public Task WriteTextAsync(HostPath path, IReadOnlyList<string> lines, CancellationToken cancellationToken = default)
    {
        Calls.Add($"writetext:{path}:{lines.Count}");
        Text[path.ToString()] = StoreTransform(lines);
        return Task.CompletedTask;
    }

    public async Task WriteBinaryAsync(HostPath path, Stream source, CancellationToken cancellationToken = default)
    {
        Calls.Add($"writebinary:{path}");
        using var copy = new MemoryStream();
        await source.CopyToAsync(copy, cancellationToken);
        Binary[path.ToString()] = copy.ToArray();
    }

    public Task DeleteAsync(HostPath path, CancellationToken cancellationToken = default)
    {
        Calls.Add($"delete:{path}");
        Text.Remove(path.ToString());
        Binary.Remove(path.ToString());
        return Task.CompletedTask;
    }

    public void Dispose() => Calls.Add("dispose");
}
