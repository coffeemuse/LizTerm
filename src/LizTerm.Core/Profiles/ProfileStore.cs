// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Text.Json;
using LizTerm.Core.Session;

namespace LizTerm.Core.Profiles;

/// <summary>One JSON file per profile in a directory. A profile's file is the one that holds its name, not whatever
/// <see cref="FileNameFor"/> spells: a file renamed by hand or copied from a platform that sanitizes differently is
/// still that profile's, and Load, Save, Update and Delete all find it the same way (<see cref="Find"/>).</summary>
public sealed class ProfileStore(string directory)
{
    public string Directory { get; } = directory;

    public static string DefaultDirectory() => AppPaths.ProfilesDirectory();

    public IReadOnlyList<SessionProfile> LoadAll()
    {
        if (!System.IO.Directory.Exists(Directory)) return [];
        var profiles = new List<SessionProfile>();
        foreach (var file in System.IO.Directory.EnumerateFiles(Directory, "*.json"))
        {
            if (Read(file) is { } profile) profiles.Add(profile);
        }
        return profiles.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>The saved profile of that name, ignoring case, or null when no readable file holds it.</summary>
    public SessionProfile? Load(string name) => Find(name)?.Profile;

    /// <summary>Applies <paramref name="change"/> to the profile as it is on disk now, or to <paramref name="fallback"/>
    /// when its file is gone, and saves the result. A caller holding an older copy of the profile (a session window,
    /// whose profile is fixed at construction) writes one choice back this way without discarding edits saved since.</summary>
    public void Update(SessionProfile fallback, Func<SessionProfile, SessionProfile> change) =>
        Save(change(Load(fallback.Name) ?? fallback));

    /// <summary>One file, or null when it cannot be read; the user can delete such a file by hand. A pin without
    /// its PEM or fingerprint (a hand-edited or redacted file) is dropped rather than handed to the engine, which
    /// would refuse an empty trust file with an error naming a temp file that no longer exists.</summary>
    private static SessionProfile? Read(string file)
    {
        SessionProfile? profile;
        try
        {
            profile = JsonSerializer.Deserialize(File.ReadAllText(file), ProfileJsonContext.Default.SessionProfile);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
        // Whitespace as well as empty, because Save refuses the same: a profile that loads but can never be written
        // back would throw out of every path that rewrites profiles (TagMaintenance among them).
        if (profile is null || string.IsNullOrWhiteSpace(profile.Name)) return null;
        if (profile.PinnedCertificate is { } pin && (string.IsNullOrWhiteSpace(pin.Pem) || string.IsNullOrWhiteSpace(pin.Sha256)))
            profile = profile with { PinnedCertificate = null };
        return profile;
    }

    /// <summary>The file holding the profile of that name, ignoring case, because the picker treats a case-only rename
    /// as the same profile. <see cref="FileNameFor"/>'s file is tried first, since Save puts a new profile there; then
    /// every file in ordinal order, so that when two files hold one name (a copy made in a file manager) Load, Save
    /// and Delete all pick the same one.</summary>
    private (string FilePath, SessionProfile Profile)? Find(string name)
    {
        if (!System.IO.Directory.Exists(Directory)) return null;
        bool Holds(SessionProfile profile) => profile.Name.Equals(name, StringComparison.OrdinalIgnoreCase);

        var expected = Path.Combine(Directory, FileNameFor(name));
        if (File.Exists(expected) && Read(expected) is { } atExpected && Holds(atExpected)) return (expected, atExpected);
        foreach (var file in System.IO.Directory.EnumerateFiles(Directory, "*.json").Order(StringComparer.Ordinal))
        {
            if (file != expected && Read(file) is { } profile && Holds(profile)) return (file, profile);
        }
        return null;
    }

    /// <summary>Where a profile that no file holds yet is written: <see cref="FileNameFor"/>'s name, or "name (2).json"
    /// and on while that is taken. A file already there is not this profile's to overwrite: FileNameFor is not
    /// one-to-one (a/b and a:b are both a_b.json), a file can be renamed inside by hand, and one that does not read as
    /// a profile is still the user's to repair.</summary>
    private string NewFileFor(string name)
    {
        var stem = Sanitize(name);
        var path = Path.Combine(Directory, stem + ".json");
        for (var n = 2; File.Exists(path); n++) path = Path.Combine(Directory, $"{stem} ({n}).json");
        return path;
    }

    /// <summary>Writes through a temp file in the same directory and renames over the target, so a reader never
    /// sees a partial profile. Read swallows a JsonException and LoadAll skips the file, so a write interrupted
    /// by a crash, a full disk or a kill would otherwise leave a profile that looks deleted rather than broken.
    /// The temp file is a sibling on purpose: File.Move across a filesystem is a copy, which is not atomic.</summary>
    public void Save(SessionProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.Name)) throw new ArgumentException("Profile needs a name", nameof(profile));
        System.IO.Directory.CreateDirectory(Directory);
        var json = JsonSerializer.Serialize(profile, ProfileJsonContext.Default.SessionProfile);
        var path = Find(profile.Name)?.FilePath ?? NewFileFor(profile.Name);
        // Not ".json": LoadAll enumerates *.json, and a temp file left by a crash mid-write must not be read
        // back as a profile of its own.
        var temp = path + ".tmp";
        try
        {
            File.WriteAllText(temp, json);
            File.Move(temp, path, overwrite: true);
        }
        catch
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { /* the write already failed; this is cleanup */ }
            throw;
        }
    }

    /// <summary>Deletes the file holding that name (see <see cref="Find"/>), or nothing when no file does.</summary>
    public void Delete(string name)
    {
        if (Find(name) is { } found) File.Delete(found.FilePath);
    }

    /// <summary>The file name a new profile of that name is given when nothing is there already.</summary>
    public static string FileNameFor(string name) => Sanitize(name) + ".json";

    private static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars().Concat(['/', '\\', ':']).ToHashSet();
        return new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
    }
}
