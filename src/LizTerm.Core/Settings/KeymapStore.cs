// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Text.Json.Nodes;
using LizTerm.Core.Profiles;

namespace LizTerm.Core.Settings;

/// <summary>One JSON file of the user's keyboard bindings (#18). The file-backed half of KeymapFile: Load never
/// throws, and Update applies a change to what is on disk <em>now</em>, as SettingsStore.Update does, so two LizTerm
/// processes cannot lose each other's bindings.</summary>
public sealed class KeymapStore(string filePath)
{
    public string FilePath { get; } = filePath;

    public static string DefaultFile() => AppPaths.KeymapFile();

    /// <summary>The bindings on disk, and why they are not the file's when they are not (#168). A file that is
    /// missing, unreadable, not JSON or not a JSON object is the empty file; a value that will not read is carried
    /// in Unreadable. Nothing is written here, so a file the user is still fixing is left as it is.</summary>
    public KeymapLoad Load()
    {
        var document = JsonFiles.ReadLenient(FilePath, out var problem);
        problem ??= BindingsProblem(document);
        return problem is null
            ? new(KeymapFile.FromJson(document), null)
            : new(KeymapFile.Empty, $"{Path.GetFileName(FilePath)} could not be read. {problem}");
    }

    /// <summary>Moves a file this build cannot use out of the way, so the next load finds none and every default
    /// applies; answers where it went, or null when there was no file. The bindings are kept rather than deleted:
    /// the file is hand-written, and a stray comma is a repair, not a reason to lose the work. An earlier set-aside
    /// file is replaced, the newer break being the one still worth reading. IO and permission errors propagate.</summary>
    public string? MoveAside()
    {
        if (!File.Exists(FilePath)) return null;
        var aside = FilePath + ".bad";
        File.Move(FilePath, aside, overwrite: true);
        return aside;
    }

    /// <summary>What is wrong with <paramref name="document"/>'s "bindings", or null when there is nothing wrong:
    /// a key that is absent is an empty keymap, which is what a fresh file and a newer build's file both look like.
    /// One home for the rule, so Load and Update cannot disagree about which files they will touch.</summary>
    internal static string? BindingsProblem(JsonObject? document) =>
        document?[KeymapFile.BindingsKey] is null or JsonObject ? null : "Its \"bindings\" is not a JSON object.";

    /// <summary>Applies <paramref name="change"/> to the file as it is on disk now and writes the result; returns
    /// what it wrote. Throws InvalidDataException, naming the file, when the file exists but is not a JSON object or
    /// its "bindings" is not, rather than replace something the user may be editing by hand. IO and permission
    /// errors propagate.</summary>
    public KeymapFile Update(Func<KeymapFile, KeymapFile> change)
    {
        var existing = JsonFiles.ReadStrict(FilePath);
        // A "bindings" that is not an object reads as the empty file; writing over it would replace what the user
        // may be editing by hand, so it is refused like a file that is not JSON, in the same words.
        if (BindingsProblem(existing) is { } problem)
            throw new InvalidDataException($"{Path.GetFileName(FilePath)} could not be read. {problem} Fix or delete it: {FilePath}");
        var next = change(KeymapFile.FromJson(existing));
        JsonFiles.Write(FilePath, next.ToJson(existing).ToJsonString(JsonFiles.Indented));
        return next;
    }
}
