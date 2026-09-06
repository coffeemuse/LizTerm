using System.Text.Json;
using LizTerm.Core.Session;

namespace LizTerm.Core.Profiles;

/// <summary>One JSON file per profile in a directory.</summary>
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

    /// <summary>The saved profile of that name, or null when it has no readable file.</summary>
    public SessionProfile? Load(string name) => Read(Path.Combine(Directory, FileNameFor(name)));

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
        if (profile is null || profile.Name.Length == 0) return null;
        if (profile.PinnedCertificate is { } pin && (string.IsNullOrWhiteSpace(pin.Pem) || string.IsNullOrWhiteSpace(pin.Sha256)))
            profile = profile with { PinnedCertificate = null };
        return profile;
    }

    public void Save(SessionProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.Name)) throw new ArgumentException("Profile needs a name", nameof(profile));
        System.IO.Directory.CreateDirectory(Directory);
        var json = JsonSerializer.Serialize(profile, ProfileJsonContext.Default.SessionProfile);
        File.WriteAllText(Path.Combine(Directory, FileNameFor(profile.Name)), json);
    }

    public void Delete(string name)
    {
        var path = Path.Combine(Directory, FileNameFor(name));
        if (File.Exists(path)) File.Delete(path);
    }

    public static string FileNameFor(string name)
    {
        var invalid = Path.GetInvalidFileNameChars().Concat(['/', '\\', ':']).ToHashSet();
        var chars = name.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        return new string(chars) + ".json";
    }
}
