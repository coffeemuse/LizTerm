// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Text.Json;

namespace LizTerm.Core.Profiles;

/// <summary>One JSON file holding a colour per tag name. Load never throws; Save writes through a sibling temp
/// file renamed over the target, the discipline ProfileStore and SettingsStore both use.</summary>
public sealed class TagRegistryStore(string filePath)
{
    public string FilePath { get; } = filePath;

    public static string DefaultFile() => AppPaths.TagsFile();

    /// <summary>The definitions on disk. A file that is missing, unreadable or not JSON loads as an empty
    /// registry rather than throwing: the names all travel in the profiles, so the cost of a lost registry is
    /// re-assigned colours, and refusing to start the picker over it would be worse.</summary>
    public TagRegistry Load()
    {
        TagRegistryFile? file;
        try
        {
            file = JsonSerializer.Deserialize(File.ReadAllText(FilePath), TagRegistryJsonContext.Default.TagRegistryFile);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return TagRegistry.Empty;
        }

        if (file?.Tags is null) return TagRegistry.Empty;

        var definitions = new List<TagDefinition>();
        foreach (var entry in file.Tags)
        {
            if (entry is null) continue;
            // Per entry, not per file: see TagEntry's remarks. IsDefined as well as TryParse, because TryParse
            // accepts any NUMBER in range of the enum's underlying type — "8" parses as the undefined
            // (TagColor)8 rather than failing — and an undefined member reaches TagPalette's dictionary while
            // rendering, where it is a KeyNotFoundException rather than a wrong colour.
            if (!Enum.TryParse<TagColor>(entry.Color, ignoreCase: true, out var color) || !Enum.IsDefined(color)) continue;
            definitions.Add(new TagDefinition(entry.Name, color));
        }
        return new TagRegistry(definitions);
    }

    /// <summary>Saves, answering null, or the failure's message when the file could not be written. The one home
    /// for the stance that a colour file which cannot be written is not worth a crash: the names all travel in the
    /// profiles, so the cost is re-assigned colours, and a caller either shows the message or carries on.</summary>
    public string? TrySave(TagRegistry registry)
    {
        try
        {
            Save(registry);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ex.Message;
        }
    }

    /// <summary>Writes <see cref="TagRegistry.Stored"/>, so the synthesised FAVORITE never reaches the file.</summary>
    public void Save(TagRegistry registry)
    {
        if (Path.GetDirectoryName(FilePath) is { Length: > 0 } directory) Directory.CreateDirectory(directory);
        var file = new TagRegistryFile([.. registry.Stored.Select(d => new TagEntry(d.Name, d.Color.ToString()))]);
        var json = JsonSerializer.Serialize(file, TagRegistryJsonContext.Default.TagRegistryFile);
        // Not ".json" beside a directory read for "*.json" anywhere, but the same rule as ProfileStore: a temp
        // file left by a crash must never be mistaken for the real one.
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
