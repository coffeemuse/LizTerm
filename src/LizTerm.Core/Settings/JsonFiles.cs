// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LizTerm.Core.Settings;

/// <summary>The file discipline SettingsStore and KeymapStore share: a lenient read for Load, a strict read for
/// Update, one parser, and a write through a sibling temp file renamed over the target.</summary>
internal static class JsonFiles
{
    /// <summary>Indented, and with the relaxed encoder: these are files a user reads and edits by hand, so a chord such as Ctrl+Home and a text such as ¬ are written as themselves rather than as \u002B and \u00AC, which is what the default encoder writes. Both forms read back identically; only the file's looks differ. settings.json's one string, the skipped update version, contains nothing either encoder escapes.</summary>
    public static readonly JsonSerializerOptions Indented = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>The file as a JSON object, or null when it is missing, unreadable, not JSON or not an object.</summary>
    public static JsonObject? ReadLenient(string path) => ReadLenient(path, out _);

    /// <param name="problem">Why the file would not read, as sentences about the file ("It is not valid JSON.
    /// Line 4: …"), or null when it read, is missing, or could not be opened at all — a missing file is the empty
    /// file by design, and an IO error is not something the user can fix by editing.</param>
    public static JsonObject? ReadLenient(string path, out string? problem)
    {
        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            problem = null;
            return null;
        }
        return Parse(text, out problem);
    }

    /// <summary>Null when the file is missing. Throws InvalidDataException, naming the file and why it would not
    /// read, when it exists and is not a JSON object, rather than let a caller replace something the user may be
    /// editing by hand. IO and permission errors propagate.</summary>
    public static JsonObject? ReadStrict(string path)
    {
        if (!File.Exists(path)) return null;
        return Parse(File.ReadAllText(path), out var problem)
               ?? throw new InvalidDataException($"{Path.GetFileName(path)} could not be read. {problem} Fix or delete it: {path}");
    }

    /// <summary>The text as a JSON object, or null for anything else: not JSON, JSON that is not an object, or an
    /// object with a duplicated key.</summary>
    public static JsonObject? Parse(string text) => Parse(text, out _);

    /// <param name="problem">Null when the text is a JSON object; otherwise sentences about it, in the shape the
    /// stores pass on to the user.</param>
    public static JsonObject? Parse(string text, out string? problem)
    {
        try
        {
            if (JsonNode.Parse(text, documentOptions: new() { AllowDuplicateProperties = false }) is JsonObject document)
            {
                problem = null;
                return document;
            }
            problem = "It does not hold a JSON object.";
            return null;
        }
        catch (JsonException ex)
        {
            problem = "It is not valid JSON. " + Describe(ex);
            return null;
        }
    }

    /// <summary>What the reader said, for someone holding the file in an editor. Its message carries developer
    /// asides the user cannot act on — the position repeated in reader terms, an invitation to change the reader's
    /// options, and the mode and deserialization it was doing — so those come off, and the line goes back on
    /// counting from one, as an editor counts. The line only, never the column: BytePositionInLine counts bytes, so
    /// on a line holding a text binding such as ¬ it is not the column the editor shows, while the line always is.
    /// A duplicated property has no position at all, and names the property instead.</summary>
    private static string Describe(JsonException ex)
    {
        var text = ex.Message;
        var tail = text.IndexOf(" LineNumber:", StringComparison.Ordinal);
        if (tail >= 0) text = text[..tail];
        text = text.Replace(" which is not supported in this mode", string.Empty, StringComparison.Ordinal)
                   .Replace(" encountered during deserialization", string.Empty, StringComparison.Ordinal)
                   .Replace(" Change the reader options.", string.Empty, StringComparison.Ordinal)
                   // An unclosed brace leads with the reader's own bookkeeping; the sentence after it is the one
                   // that tells the user what to type.
                   .Replace("Expected depth to be zero at the end of the JSON payload. ", string.Empty, StringComparison.Ordinal)
                   .Trim();
        if (!text.EndsWith('.')) text += '.';
        if (ex.LineNumber is not { } line) return text;
        // "The JSON object…" becomes "the JSON object…" after "Line 4: ", while "JSON" and "'P' is…" stay as they are.
        if (text.Length > 1 && char.IsAsciiLetterUpper(text[0]) && char.IsAsciiLetterLower(text[1]))
            text = char.ToLowerInvariant(text[0]) + text[1..];
        return $"Line {line + 1}: {text}";
    }

    /// <summary>Through a sibling temp file renamed over the target, so a reader never sees a partial file, and
    /// a write interrupted by a crash or a full disk leaves the old file rather than a broken one. The temp file
    /// is a sibling on purpose: File.Move across a filesystem is a copy, which is not atomic. Its name ends in ".tmp", not
    /// ".json", so a directory read for *.json (the profiles) never takes a leftover for a file of its own.</summary>
    public static void Write(string path, string text)
    {
        if (Path.GetDirectoryName(path) is { Length: > 0 } directory) Directory.CreateDirectory(directory);
        var temp = path + ".tmp";
        try
        {
            File.WriteAllText(temp, text);
            File.Move(temp, path, overwrite: true);
        }
        catch
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { /* the write already failed; this is cleanup */ }
            throw;
        }
    }
}
