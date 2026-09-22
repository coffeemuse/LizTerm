// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Globalization;
using System.Runtime.ExceptionServices;
using LizTerm.App.HostFiles;
using LizTerm.Core.HostFiles;

namespace LizTerm.App.ViewModels;

/// <summary>One item of a download batch: where it comes from, where it goes, and where its words go (a row's
/// status, or the status line).</summary>
internal sealed record DownloadItem(HostPath Path, string File, Action<string> Show);

/// <summary>The download side both tabs share: one transfer reported where its caller says, and the batch of
/// them, two at a time, stopped as one by a connection failure. Knows nothing about rows or panes.</summary>
internal static class BrowserTransfers
{
    public const int ParallelDownloads = 2;

    /// <summary>One transfer. <paramref name="token"/> stops it; <paramref name="userToken"/> says whether the user
    /// asked for that, as opposed to a batch stopped by another item's connection failure. The stamp is remembered
    /// only once the file is in place (spec §5.2). A connection failure is rethrown for the banner; any other failure
    /// is the item's own words and false.</summary>
    public static async Task<bool> DownloadOneAsync(HostFileConnection connection, EtagMemory etags, Action<Action> dispatch,
        HostPath path, string file, DownloadOptions options, Action<string> show, CancellationToken token, CancellationToken userToken)
    {
        var progress = new RowProgress(dispatch, bytes => show($"⟳ Running · {Bytes(bytes)} bytes"));
        show("⟳ Running");
        try
        {
            var result = await connection.RunAsync(service => HostFileTransfer.DownloadAsync(service, path, file, options, progress, token));
            etags.Remember(path, result.Etag);
            progress.Close();
            show($"✓ Done · {Bytes(result.BytesWritten)} bytes");
            return true;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            progress.Close();
            show(userToken.IsCancellationRequested ? "– Cancelled" : "– Stopped");
            return false;
        }
        catch (HostFileException ex) when (BrowserOperations.IsConnectionFailure(ex))
        {
            progress.Close();
            show("– Stopped");
            throw;
        }
        catch (Exception ex)
        {
            progress.Close();
            show("✗ Failed: " + HostFileMessages.Describe(ex));
            return false;
        }
    }

    /// <summary>The batch: <see cref="ParallelDownloads"/> at a time. A connection failure stops the whole batch
    /// through a linked source and is rethrown once, so the banner offers Retry; the user's own cancel is still told
    /// apart through <paramref name="token"/>. Returns how many items finished.</summary>
    public static async Task<int> DownloadManyAsync(HostFileConnection connection, EtagMemory etags, Action<Action> dispatch,
        IReadOnlyList<DownloadItem> plan, DownloadOptions options, CancellationToken token)
    {
        using var batch = CancellationTokenSource.CreateLinkedTokenSource(token);
        ExceptionDispatchInfo? connectionFailure = null;
        using var slots = new SemaphoreSlim(ParallelDownloads);
        var results = await Task.WhenAll(plan.Select(async item =>
        {
            try
            {
                await slots.WaitAsync(batch.Token);
            }
            catch (OperationCanceledException)
            {
                item.Show(token.IsCancellationRequested ? "– Cancelled" : "– Stopped");
                return false;
            }
            try
            {
                return await DownloadOneAsync(connection, etags, dispatch, item.Path, item.File, options, item.Show, batch.Token, token);
            }
            catch (HostFileException ex) when (BrowserOperations.IsConnectionFailure(ex))
            {
                Interlocked.CompareExchange(ref connectionFailure, ExceptionDispatchInfo.Capture(ex), null);
                batch.Cancel();
                return false;
            }
            finally
            {
                slots.Release();
            }
        }));
        if (!token.IsCancellationRequested) connectionFailure?.Throw();
        return results.Count(ok => ok);
    }

    public static string Bytes(long count) => count.ToString("N0", CultureInfo.InvariantCulture);

    /// <summary>Progress arrives on a backend thread and goes through the dispatcher; once the transfer's result is
    /// shown, a report still in the queue must not overwrite it.</summary>
    public sealed class RowProgress(Action<Action> dispatch, Action<long> show) : IProgress<long>
    {
        private volatile bool _closed;

        public void Close() => _closed = true;

        public void Report(long value) => dispatch(() =>
        {
            if (!_closed) show(value);
        });
    }
}
