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
public sealed record UploadOutcome(UploadVerification Verification, int? DiffersAtLine)
{
    public static readonly UploadOutcome NotChecked = new(UploadVerification.NotChecked, null);
    public static readonly UploadOutcome Matches = new(UploadVerification.Matches, null);
}

/// <summary>The local-file side of a transfer, over any <see cref="IHostFileService"/>.</summary>
public static class HostFileTransfer
{
    private static readonly UTF8Encoding Utf8NoMark = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Writes to a hidden temporary file beside <paramref name="destinationFile"/> and renames it into place
    /// only on success, so a failed or cancelled download never leaves a partial file under the real name and never
    /// damages the file it would have replaced.</summary>
    /// <returns>The bytes written locally.</returns>
    public static async Task<long> DownloadAsync(IHostFileService service, HostPath path, string destinationFile,
        DownloadOptions options, IProgress<long>? progress = null, CancellationToken cancellationToken = default)
    {
        var full = Path.GetFullPath(destinationFile);
        var temporary = Path.Combine(Path.GetDirectoryName(full)!, $".{Path.GetFileName(full)}.{Guid.NewGuid():N}.part");
        try
        {
            long written;
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                if (options.Mode == HostTransferMode.Binary)
                {
                    written = await service.ReadBinaryAsync(path, stream, progress, cancellationToken);
                }
                else
                {
                    var lines = await service.ReadTextAsync(path, progress, cancellationToken);
                    var bytes = Utf8NoMark.GetBytes(FormatText(lines, options));
                    await stream.WriteAsync(bytes, cancellationToken);
                    written = bytes.Length;
                }
            }
            File.Move(temporary, full, overwrite: true);
            return written;
        }
        catch
        {
            TryDelete(temporary);
            throw;
        }
    }

    public static TextUploadResult CheckTextFile(string sourceFile, DatasetAttributes target, TextUploadOptions? options = null) =>
        TextUploadCheck.Run(File.ReadAllBytes(sourceFile), target, options);

    /// <summary>Sends text that passed its check and, with <paramref name="verify"/>, reads it back and compares.</summary>
    /// <exception cref="InvalidOperationException">The check found errors.</exception>
    public static async Task<UploadOutcome> UploadTextAsync(IHostFileService service, HostPath path,
        TextUploadResult checkedText, bool verify, CancellationToken cancellationToken = default)
    {
        if (!checkedText.CanUpload) throw new InvalidOperationException("The text did not pass its upload check.");
        await service.WriteTextAsync(path, checkedText.Lines, cancellationToken);
        if (!verify) return UploadOutcome.NotChecked;
        var stored = await service.ReadTextAsync(path, null, cancellationToken);
        return FirstDifference(checkedText.Lines, stored) is { } line
            ? new UploadOutcome(UploadVerification.Differs, line)
            : UploadOutcome.Matches;
    }

    public static async Task UploadBinaryAsync(IHostFileService service, HostPath path, string sourceFile,
        CancellationToken cancellationToken = default)
    {
        await using var source = File.OpenRead(sourceFile);
        await service.WriteBinaryAsync(path, source, cancellationToken);
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
