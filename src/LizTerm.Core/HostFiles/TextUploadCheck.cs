// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Globalization;
using System.Text;

namespace LizTerm.Core.HostFiles;

public sealed record TextUploadOptions(bool ExpandTabs = true, int TabWidth = 8);

public enum TextUploadProblemKind { InvalidEncoding, UnsupportedCharacter, LineTooLong, TabsPresent, FileTooLarge }

/// <param name="Line">1-based; 0 for a problem with the whole file, or a count of further lines.</param>
public sealed record TextUploadProblem(TextUploadProblemKind Kind, int Line, string Message);

/// <param name="Lines">What to send, tabs already expanded when asked.</param>
/// <param name="Errors">Problems that block the upload.</param>
/// <param name="Warnings">Problems the user may accept.</param>
public sealed record TextUploadResult(
    IReadOnlyList<string> Lines,
    IReadOnlyList<TextUploadProblem> Errors,
    IReadOnlyList<TextUploadProblem> Warnings)
{
    public bool CanUpload => Errors.Count == 0;
}

/// <summary>Checks a local text file against the record rules of the dataset it is going to, before anything is
/// sent. A host may shorten an over-long line without saying so, and an EBCDIC code page holds only the Latin-1
/// repertoire, so both block the upload rather than lose data.</summary>
public static class TextUploadCheck
{
    public const int MaxReportedPerKind = 5;

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private static ReadOnlySpan<byte> Utf8Bom => [0xEF, 0xBB, 0xBF];

    /// <exception cref="ArgumentOutOfRangeException"><see cref="TextUploadOptions.TabWidth"/> is less than 1.</exception>
    public static TextUploadResult Run(ReadOnlySpan<byte> file, DatasetAttributes target, TextUploadOptions? options = null) =>
        Run(file, target.UsableLineLength, null, warnTabs: true, options);

    /// <summary>For a byte-stream target: no record length, tabs kept without a warning when not expanded (the host
    /// stores them), and with <paramref name="maxBytes"/> the bytes the lines will take on the host, one per
    /// character plus one per line, since every line is written with a line ending whether or not the source's
    /// last line had one, counted against it as <see cref="TextUploadProblemKind.FileTooLarge"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><see cref="TextUploadOptions.TabWidth"/> is less than 1.</exception>
    public static TextUploadResult RunForUnixFile(ReadOnlySpan<byte> file, long? maxBytes = null, TextUploadOptions? options = null) =>
        Run(file, null, maxBytes, warnTabs: false, options);

    private static TextUploadResult Run(ReadOnlySpan<byte> file, int? lineLimit, long? maxBytes, bool warnTabs, TextUploadOptions? options)
    {
        options ??= new TextUploadOptions();
        ArgumentOutOfRangeException.ThrowIfLessThan(options.TabWidth, 1, nameof(options));
        if (file.StartsWith(Utf8Bom)) file = file[Utf8Bom.Length..];

        string text;
        try
        {
            text = StrictUtf8.GetString(file);
        }
        catch (DecoderFallbackException)
        {
            return new TextUploadResult(
                [],
                [new TextUploadProblem(TextUploadProblemKind.InvalidEncoding, 0, "The file is not UTF-8 text. Choose Binary to send its bytes unchanged.")],
                []);
        }

        var lines = SplitLines(text);
        var tooLong = new Reporter(TextUploadProblemKind.LineTooLong);
        var unsupported = new Reporter(TextUploadProblemKind.UnsupportedCharacter);
        var tabLines = new List<int>();
        var limit = lineLimit;

        for (var i = 0; i < lines.Count; i++)
        {
            var number = i + 1;
            if (lines[i].Contains('\t'))
            {
                tabLines.Add(number);
                if (options.ExpandTabs) lines[i] = ExpandTabs(lines[i], options.TabWidth);
            }
            foreach (var rune in lines[i].EnumerateRunes())
            {
                if (rune.Value <= 0xFF) continue;
                unsupported.Add(number, $"Line {number} contains “{rune}” (U+{rune.Value:X4}), which the host can't store.");
                break;
            }
            if (limit is int max && lines[i].Length > max)
                tooLong.Add(number, $"Line {number} is {lines[i].Length} characters; the limit is {max}.");
        }

        var warnings = new List<TextUploadProblem>();
        if (warnTabs && tabLines.Count > 0)
        {
            warnings.Add(new TextUploadProblem(TextUploadProblemKind.TabsPresent, tabLines[0],
                tabLines.Count == 1 ? "1 line contains tab characters." : $"{tabLines.Count} lines contain tab characters."));
        }
        var errors = new List<TextUploadProblem>([.. tooLong.All(), .. unsupported.All()]);
        if (maxBytes is long cap)
        {
            long bytes = 0;
            foreach (var line in lines) bytes += line.Length + 1;
            if (bytes > cap) errors.Add(new TextUploadProblem(TextUploadProblemKind.FileTooLarge, 0, TooLarge(bytes, cap)));
        }
        return new TextUploadResult(lines, errors, warnings);
    }

    /// <summary>The one sentence for a file past the host's size, shared with the binary check.</summary>
    internal static string TooLarge(long bytes, long cap) =>
        $"The file is {bytes.ToString("N0", CultureInfo.InvariantCulture)} bytes; the host holds at most {cap.ToString("N0", CultureInfo.InvariantCulture)}.";

    /// <summary>Splits at CR LF, LF or CR only — never at form feed or the Unicode separators, which are data. A
    /// final line ending adds no empty line.</summary>
    internal static List<string> SplitLines(string text)
    {
        var lines = new List<string>();
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] is not ('\r' or '\n')) continue;
            lines.Add(text[start..i]);
            if (text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
            start = i + 1;
        }
        if (start < text.Length) lines.Add(text[start..]);
        return lines;
    }

    internal static string ExpandTabs(string line, int width)
    {
        var expanded = new StringBuilder(line.Length + width);
        foreach (var c in line)
        {
            if (c == '\t') expanded.Append(' ', width - expanded.Length % width);
            else expanded.Append(c);
        }
        return expanded.ToString();
    }

    private sealed class Reporter(TextUploadProblemKind kind)
    {
        private readonly List<TextUploadProblem> _problems = [];
        private int _extra;

        public void Add(int line, string message)
        {
            if (_problems.Count < MaxReportedPerKind) _problems.Add(new TextUploadProblem(kind, line, message));
            else _extra++;
        }

        public IEnumerable<TextUploadProblem> All() => _extra == 0
            ? _problems
            : _problems.Append(new TextUploadProblem(kind, 0,
                _extra == 1 ? "1 more line has the same problem." : $"{_extra} more lines have the same problem."));
    }
}
