// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Text.Json;
using System.Text.Json.Nodes;
using LizTerm.Core.Profiles;

namespace LizTerm.Core.Settings;

/// <summary>One JSON file of app-wide settings holding only the keys the user has set (SettingsLayers has the
/// rules). This is the file-backed half: Load never throws, and Update applies a change to what is on disk
/// *now*, as ProfileStore.Update does, so two LizTerm processes cannot lose each other's keys.</summary>
public sealed class SettingsStore(string filePath)
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    /// <summary>What the layers under the user file merge to. Every default until a system layer exists (#54,
    /// #55); when one does, it is one more entry in the list Merge takes, ahead of the user file.</summary>
    private static readonly AppSettings Beneath = new();

    public string FilePath { get; } = filePath;

    public static string DefaultFile() => AppPaths.SettingsFile();

    /// <summary>The settings in force. A file that is missing, unreadable, not JSON or not a JSON object is
    /// skipped; a key whose value will not read falls to its default alone. Nothing is written here.</summary>
    public AppSettings Load() => SettingsLayers.Read(SettingsLayers.Merge(Layers(ReadLenient())));

    /// <summary>Applies <paramref name="change"/> to the settings as they are on disk now and writes the sparse
    /// user document for the result; returns what it wrote. Throws InvalidDataException, naming the file, when
    /// the file exists but is not a JSON object — rather than replace something the user may be editing by hand.
    /// IO and permission errors, from the re-read or the write, propagate.</summary>
    public AppSettings Update(Func<AppSettings, AppSettings> change)
    {
        var existing = ReadStrict();
        var next = change(SettingsLayers.Read(SettingsLayers.Merge(Layers(existing))));
        Write(SettingsLayers.UserDocument(next, Beneath, existing).ToJsonString(Indented));
        return next;
    }

    private static IEnumerable<JsonObject> Layers(JsonObject? user) => user is null ? [] : [user];

    private JsonObject? ReadLenient()
    {
        string text;
        try
        {
            text = File.ReadAllText(FilePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
        return Parse(text);
    }

    private JsonObject? ReadStrict()
    {
        if (!File.Exists(FilePath)) return null;
        return Parse(File.ReadAllText(FilePath))
               ?? throw new InvalidDataException($"{Path.GetFileName(FilePath)} is not valid JSON; fix or delete it: {FilePath}");
    }

    /// <summary>The text as a JSON object, or null for anything else: not JSON, or JSON that is not an object.</summary>
    private static JsonObject? Parse(string text)
    {
        try
        {
            return JsonNode.Parse(text) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Through a sibling temp file renamed over the target, so a reader never sees a partial file, and
    /// a write interrupted by a crash or a full disk leaves the old file rather than a broken one. The temp file
    /// is a sibling on purpose: File.Move across a filesystem is a copy, which is not atomic.</summary>
    private void Write(string json)
    {
        if (Path.GetDirectoryName(FilePath) is { Length: > 0 } directory) Directory.CreateDirectory(directory);
        var temp = FilePath + ".tmp";
        try
        {
            File.WriteAllText(temp, json);
            File.Move(temp, FilePath, overwrite: true);
        }
        catch
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { /* the write already failed; this is cleanup */ }
            throw;
        }
    }
}
