// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Text;

namespace LizTerm.Core.HostFiles;

/// <param name="TrimTrailingBlanks">Fixed-length records arrive padded with blanks; trimming them is what a local
/// editor expects.</param>
/// <param name="LineEnding">Null for the platform's own.</param>
public sealed record DownloadOptions(HostTransferMode Mode, bool TrimTrailingBlanks = true, string? LineEnding = null);

public enum UploadVerification { NotChecked, Matches, Differs }

/// <param name="DiffersAtLine">1-based, set when <paramref name="Verification"/> is Differs.</param>
/// <param name="Etag">The host's stamp of the target as written, for the next write's <c>ifMatch</c>; null when
/// the host gives none.</param>
public sealed record UploadOutcome(UploadVerification Verification, int? DiffersAtLine, string? Etag);

/// <param name="BytesWritten">The bytes written locally.</param>
/// <param name="Etag">The host's stamp of what was read, for a later write's <c>ifMatch</c>; null when the host
/// gives none.</param>
public sealed record DownloadResult(long BytesWritten, string? Etag);

/// <summary>The local-file side of a transfer, over any <see cref="IHostFileService"/>.</summary>
public static class HostFileTransfer
{
    private static readonly UTF8Encoding Utf8NoMark = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Writes to a dot-prefixed <c>.part</c> file (hidden on macOS and Linux; its name does not embed the
    /// destination's, so any name the file system allows can be downloaded to) beside
    /// <paramref name="destinationFile"/> and renames it into place only on success, so a failed or cancelled download
    /// never leaves a partial file under the real name and never damages the file it would have replaced.</summary>
    /// <returns>The bytes written locally and the host's stamp.</returns>
    /// <exception cref="IOException">The local file could not be written or renamed; not a
    /// <see cref="HostFileException"/>.</exception>
    /// <exception cref="UnauthorizedAccessException">The local folder or file is not writable.</exception>
    public static async Task<DownloadResult> DownloadAsync(IHostFileService service, HostPath path, string destinationFile,
        DownloadOptions options, IProgress<long>? progress = null, CancellationToken cancellationToken = default)
    {
        var full = Path.GetFullPath(destinationFile);
        var temporary = Path.Combine(Path.GetDirectoryName(full)!, $".lizterm-{Guid.NewGuid():N}.part");
        try
        {
            long written;
            string? etag;
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                if (options.Mode == HostTransferMode.Binary)
                {
                    var read = await service.ReadBinaryAsync(path, stream, progress, withEtag: true, cancellationToken);
                    (written, etag) = (read.Bytes, read.Etag);
                }
                else
                {
                    var read = await service.ReadTextAsync(path, progress, withEtag: true, cancellationToken);
                    var bytes = Utf8NoMark.GetBytes(FormatText(read.Lines, options));
                    await stream.WriteAsync(bytes, cancellationToken);
                    (written, etag) = (bytes.Length, read.Etag);
                }
            }
            File.Move(temporary, full, overwrite: true);
            return new DownloadResult(written, etag);
        }
        catch
        {
            TryDelete(temporary);
            throw;
        }
    }

    /// <summary>Reads <paramref name="sourceFile"/> and runs <see cref="TextUploadCheck"/> on it.</summary>
    /// <exception cref="IOException">The local file could not be read; not a <see cref="HostFileException"/>.</exception>
    /// <exception cref="UnauthorizedAccessException">The local file is not readable.</exception>
    public static TextUploadResult CheckTextFile(string sourceFile, DatasetAttributes target, TextUploadOptions? options = null) =>
        TextUploadCheck.Run(File.ReadAllBytes(sourceFile), target, options);

    /// <summary>Sends text that passed its check and, with <paramref name="verify"/>, reads it back and compares.
    /// <paramref name="ifMatch"/> is the stamp the target must still hold (see <see cref="IHostFileService.WriteTextAsync"/>).
    /// The outcome carries the write's stamp, not the read-back's: the write's is the one the host promises for
    /// the next <c>ifMatch</c>.</summary>
    /// <exception cref="InvalidOperationException">The check found errors.</exception>
    public static async Task<UploadOutcome> UploadTextAsync(IHostFileService service, HostPath path,
        TextUploadResult checkedText, bool verify, string? ifMatch = null, CancellationToken cancellationToken = default)
    {
        if (!checkedText.CanUpload) throw new InvalidOperationException("The text did not pass its upload check.");
        var etag = await service.WriteTextAsync(path, checkedText.Lines, ifMatch, cancellationToken);
        if (!verify) return new UploadOutcome(UploadVerification.NotChecked, null, etag);
        // Without the stamp: the write's is the one that matters, and asking would cost the host another pass.
        var stored = await service.ReadTextAsync(path, cancellationToken: cancellationToken);
        return FirstDifference(checkedText.Lines, stored.Lines) is { } line
            ? new UploadOutcome(UploadVerification.Differs, line, etag)
            : new UploadOutcome(UploadVerification.Matches, null, etag);
    }

    /// <summary>Sends <paramref name="sourceFile"/> as it is; returns the write's stamp.</summary>
    /// <exception cref="IOException">The local file could not be read; not a <see cref="HostFileException"/>.</exception>
    /// <exception cref="UnauthorizedAccessException">The local file is not readable.</exception>
    public static async Task<string?> UploadBinaryAsync(IHostFileService service, HostPath path, string sourceFile,
        string? ifMatch = null, CancellationToken cancellationToken = default)
    {
        await using var source = File.OpenRead(sourceFile);
        return await service.WriteBinaryAsync(path, source, ifMatch, cancellationToken);
    }

    internal static string FormatText(IReadOnlyList<string> lines, DownloadOptions options)
    {
        var ending = options.LineEnding ?? Environment.NewLine;
        var text = new StringBuilder();
        foreach (var line in lines) text.Append(options.TrimTrailingBlanks ? line.TrimEnd(' ') : line).Append(ending);
        return text.ToString();
    }

    /// <summary>The first 1-based line where the two differ once trailing blanks are ignored, counting a missing line
    /// as a difference; null when they match.</summary>
    internal static int? FirstDifference(IReadOnlyList<string> sent, IReadOnlyList<string> stored)
    {
        var common = Math.Min(sent.Count, stored.Count);
        for (var i = 0; i < common; i++)
        {
            if (!string.Equals(sent[i].TrimEnd(' '), stored[i].TrimEnd(' '), StringComparison.Ordinal)) return i + 1;
        }
        return sent.Count == stored.Count ? null : common + 1;
    }

    private static void TryDelete(string file)
    {
        try
        {
            File.Delete(file);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
