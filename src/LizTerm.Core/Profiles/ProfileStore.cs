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
            try
            {
                var profile = JsonSerializer.Deserialize(File.ReadAllText(file), ProfileJsonContext.Default.SessionProfile);
                if (profile is not null && profile.Name.Length > 0) profiles.Add(profile);
            }
            catch (JsonException)
            {
                // Skip unreadable files; the user can delete them by hand.
            }
            catch (IOException)
            {
                // Skip files that cannot be read due to I/O errors.
            }
            catch (UnauthorizedAccessException)
            {
                // Skip files that cannot be read due to permission issues.
            }
        }
        return profiles.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
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
