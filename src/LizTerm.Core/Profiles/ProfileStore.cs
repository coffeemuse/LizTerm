// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Text.Json;
using LizTerm.Core.Session;
using LizTerm.Core.Settings;

namespace LizTerm.Core.Profiles;

/// <summary>One JSON file per profile in a directory. A profile's file is the one that holds its name, not whatever
/// <see cref="FileNameFor"/> spells: a file renamed by hand or copied from a platform that sanitizes differently is
/// still that profile's, and Load, Save, Update and Delete all find it the same way (<see cref="Find"/>).</summary>
public sealed class ProfileStore(string directory)
{
    public string Directory { get; } = directory;

    public static string DefaultDirectory() => AppPaths.ProfilesDirectory();

    /// <summary>Every readable profile, each name once: when two files hold one name, the one <see cref="Find"/> picks.
    /// The picker's rows and Manage Tags act on a profile by its name, so a row for the other file would star, edit
    /// and delete the first, and a tag change would write both into one file.</summary>
    public IReadOnlyList<SessionProfile> LoadAll()
    {
        if (!System.IO.Directory.Exists(Directory)) return [];
        var profiles = new List<SessionProfile>();
        foreach (var file in System.IO.Directory.EnumerateFiles(Directory, "*.json"))
        {
            if (Read(file) is { } profile) profiles.Add(profile);
        }
        return profiles.GroupBy(p => p.Name, StringComparer.Ordinal)
            .Select(named => named.Skip(1).Any() && Find(named.Key)?.Profile is { } picked && picked.Name == named.Key
                ? picked
                : named.First())
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>The saved profile of that name (exactly, else ignoring case), or null when no readable file holds it.</summary>
    public SessionProfile? Load(string name) => Find(name)?.Profile;

    /// <summary>Applies <paramref name="change"/> to the profile as it is on disk now, or to <paramref name="fallback"/>
    /// when its file is gone, and saves the result. A caller holding an older copy of the profile (a session window,
    /// whose profile is fixed at construction) writes one choice back this way without discarding edits saved since.</summary>
    public void Update(SessionProfile fallback, Func<SessionProfile, SessionProfile> change) =>
        Save(change(Load(fallback.Name) ?? fallback));

    /// <summary>One file, or null when it cannot be read; the user can delete such a file by hand. A pin without
    /// its PEM or fingerprint (a hand-edited or redacted file) is dropped rather than handed to the engine, which
    /// would refuse an empty trust file with an error naming a temp file that no longer exists. With
    /// <paramref name="failOnIoError"/>, a file that is there but cannot be opened throws instead, which is what Save
    /// needs (see <see cref="Find"/>); one that does not parse is still null.</summary>
    private static SessionProfile? Read(string file, bool failOnIoError = false)
    {
        SessionProfile? profile;
        try
        {
            profile = JsonSerializer.Deserialize(File.ReadAllText(file), ProfileJsonContext.Default.SessionProfile);
        }
        // A file gone since it was listed holds nothing, whoever asks.
        catch (Exception ex) when (ex is JsonException or FileNotFoundException
            || (!failOnIoError && ex is IOException or UnauthorizedAccessException))
        {
            return null;
        }
        // Whitespace as well as empty, because Save refuses the same: a profile that loads but can never be written
        // back would throw out of every path that rewrites profiles (TagMaintenance among them).
        if (profile is null || string.IsNullOrWhiteSpace(profile.Name)) return null;
        if (profile.PinnedCertificate is { } pin && (string.IsNullOrWhiteSpace(pin.Pem) || string.IsNullOrWhiteSpace(pin.Sha256)))
            profile = profile with { PinnedCertificate = null };
        if (profile.HostFilesPinnedCertificate is { } restPin && (string.IsNullOrWhiteSpace(restPin.Pem) || string.IsNullOrWhiteSpace(restPin.Sha256)))
            profile = profile with { HostFilesPinnedCertificate = null };
        return profile;
    }

    /// <summary>The file holding the profile of that name. <see cref="FileNameFor"/>'s file is tried first, since Save
    /// puts a new profile there; then every file in ordinal order, so that when two files hold one name (a copy made
    /// in a file manager) Load, Save and Delete all pick the same one. A file holding the name exactly wins wherever
    /// it sits, and only then the first holding it ignoring case: the picker treats a case-only rename as the same
    /// profile, but "MVS" and "mvs" in two files are two rows that must not act on each other.
    /// <paramref name="failOnIoError"/> is Save's: a file it cannot open on the way may be the profile's own, and
    /// writing anywhere else would leave the edit behind that file once it opens again.</summary>
    private (string FilePath, SessionProfile Profile)? Find(string name, bool failOnIoError = false)
    {
        if (!System.IO.Directory.Exists(Directory)) return null;
        var expected = Path.Combine(Directory, FileNameFor(name));
        var others = System.IO.Directory.EnumerateFiles(Directory, "*.json")
            .Where(file => file != expected).Order(StringComparer.Ordinal);
        (string FilePath, SessionProfile Profile)? ignoringCase = null;
        foreach (var file in others.Prepend(expected))
        {
            if (!File.Exists(file) || Read(file, failOnIoError) is not { } profile) continue;
            if (profile.Name.Equals(name, StringComparison.Ordinal)) return (file, profile);
            if (ignoringCase is null && profile.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                ignoringCase = (file, profile);
        }
        return ignoringCase;
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
    /// The temp file is a sibling on purpose: File.Move across a filesystem is a copy, which is not atomic. A file
    /// that cannot be opened on the way to the profile's makes it throw before writing anything (see <see cref="Find"/>).</summary>
    public void Save(SessionProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.Name)) throw new ArgumentException("Profile needs a name", nameof(profile));
        System.IO.Directory.CreateDirectory(Directory);
        var json = JsonSerializer.Serialize(profile, ProfileJsonContext.Default.SessionProfile);
        var path = Find(profile.Name, failOnIoError: true)?.FilePath ?? NewFileFor(profile.Name);
        JsonFiles.Write(path, json);
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
