// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.Core.Profiles;

namespace LizTerm.Core.Settings;

/// <summary>One JSON file of the user's keyboard bindings (#18). The file-backed half of KeymapFile: Load never
/// throws, and Update applies a change to what is on disk <em>now</em>, as SettingsStore.Update does, so two LizTerm
/// processes cannot lose each other's bindings.</summary>
public sealed class KeymapStore(string filePath)
{
    public string FilePath { get; } = filePath;

    public static string DefaultFile() => AppPaths.KeymapFile();

    /// <summary>The bindings on disk. A file that is missing, unreadable, not JSON or not a JSON object is the
    /// empty file; a value that will not read is carried in Unreadable. Nothing is written here.</summary>
    public KeymapFile Load() => KeymapFile.FromJson(JsonFiles.ReadLenient(FilePath));

    /// <summary>Applies <paramref name="change"/> to the file as it is on disk now and writes the result; returns
    /// what it wrote. Throws InvalidDataException, naming the file, when the file exists but is not a JSON object,
    /// rather than replace something the user may be editing by hand. IO and permission errors propagate.</summary>
    public KeymapFile Update(Func<KeymapFile, KeymapFile> change)
    {
        var next = change(KeymapFile.FromJson(JsonFiles.ReadStrict(FilePath)));
        JsonFiles.Write(FilePath, next.ToJson().ToJsonString(JsonFiles.Indented));
        return next;
    }
}
