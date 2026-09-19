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
    /// <summary>Indented, and with the relaxed encoder: these are files a user reads and edits by hand, so a chord such as Ctrl+Home and a text such as ¬ are written as themselves rather than as + and ¬. Both forms read back identically; only the file's looks differ.</summary>
    public static readonly JsonSerializerOptions Indented = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>The file as a JSON object, or null when it is missing, unreadable, not JSON or not an object.</summary>
    public static JsonObject? ReadLenient(string path)
    {
        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
        return Parse(text);
    }

    /// <summary>Null when the file is missing. Throws InvalidDataException, naming the file, when it exists and is
    /// not a JSON object, rather than let a caller replace something the user may be editing by hand. IO and
    /// permission errors propagate.</summary>
    public static JsonObject? ReadStrict(string path)
    {
        if (!File.Exists(path)) return null;
        return Parse(File.ReadAllText(path))
               ?? throw new InvalidDataException($"{Path.GetFileName(path)} is not valid JSON; fix or delete it: {path}");
    }

    /// <summary>The text as a JSON object, or null for anything else: not JSON, JSON that is not an object, or an
    /// object with a duplicated key.</summary>
    public static JsonObject? Parse(string text)
    {
        try
        {
            return JsonNode.Parse(text, documentOptions: new() { AllowDuplicateProperties = false }) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Through a sibling temp file renamed over the target, so a reader never sees a partial file, and
    /// a write interrupted by a crash or a full disk leaves the old file rather than a broken one. The temp file
    /// is a sibling on purpose: File.Move across a filesystem is a copy, which is not atomic.</summary>
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
