// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Text.Json.Nodes;
using LizTerm.Core.Profiles;

namespace LizTerm.Core.Settings;

/// <summary>One JSON file of app-wide settings holding only the keys the user has set (SettingsLayers has the
/// rules). This is the file-backed half: Load never throws, and Update applies a change to what is on disk
/// *now*, as ProfileStore.Update does, so two LizTerm processes cannot lose each other's keys.</summary>
public sealed class SettingsStore(string filePath)
{
    /// <summary>What the layers under the user file merge to. Every default until a system layer exists (#54,
    /// #55); when one does, it is one more entry in the list Merge takes, ahead of the user file.</summary>
    private static readonly AppSettings Beneath = new();

    public string FilePath { get; } = filePath;

    public static string DefaultFile() => AppPaths.SettingsFile();

    /// <summary>The settings in force. A file that is missing, unreadable, not JSON or not a JSON object is
    /// skipped; a key whose value will not read falls to its default alone. Nothing is written here.</summary>
    public AppSettings Load() => SettingsLayers.Read(SettingsLayers.Merge(Layers(JsonFiles.ReadLenient(FilePath))));

    /// <summary>Applies <paramref name="change"/> to the settings as they are on disk now and writes the sparse
    /// user document for the result; returns what it wrote. Throws InvalidDataException, naming the file, when
    /// the file exists but is not a JSON object — rather than replace something the user may be editing by hand.
    /// IO and permission errors, from the re-read or the write, propagate.</summary>
    public AppSettings Update(Func<AppSettings, AppSettings> change)
    {
        var existing = JsonFiles.ReadStrict(FilePath);
        var next = change(SettingsLayers.Read(SettingsLayers.Merge(Layers(existing))));
        JsonFiles.Write(FilePath, SettingsLayers.UserDocument(next, Beneath, existing).ToJsonString(JsonFiles.Indented));
        return next;
    }

    private static IEnumerable<JsonObject> Layers(JsonObject? user) => user is null ? [] : [user];
}
