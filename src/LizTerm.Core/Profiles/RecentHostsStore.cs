// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Text.Json;

namespace LizTerm.Core.Profiles;

/// <summary>The on-disk shape of recent-hosts.json. Nullable entries because the file is text a user can edit, and
/// <see cref="RecentHosts.From"/> is what cleans them.</summary>
internal sealed record RecentHostsFile(List<string?>? Hosts);

/// <summary>One JSON file holding Quick Connect's history. Load never throws; Save writes through a sibling temp
/// file renamed over the target, the discipline TagRegistryStore, ProfileStore and SettingsStore all use.</summary>
public sealed class RecentHostsStore(string filePath)
{
    public string FilePath { get; } = filePath;

    public static string DefaultFile() => AppPaths.RecentHostsFile();

    /// <summary>The entries on disk. A file that is missing, unreadable or not JSON loads as an empty list: a lost
    /// history costs some retyping, and refusing to open the picker over it would cost far more.</summary>
    public RecentHosts Load()
    {
        try
        {
            var file = JsonSerializer.Deserialize(File.ReadAllText(FilePath), RecentHostsJsonContext.Default.RecentHostsFile);
            return file?.Hosts is { } hosts ? RecentHosts.From(hosts) : RecentHosts.Empty;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return RecentHosts.Empty;
        }
    }

    /// <summary>Saves, answering null, or the failure's message when the file could not be written.</summary>
    public string? TrySave(RecentHosts recent)
    {
        try
        {
            Save(recent);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ex.Message;
        }
    }

    public void Save(RecentHosts recent)
    {
        if (Path.GetDirectoryName(FilePath) is { Length: > 0 } directory) Directory.CreateDirectory(directory);
        var json = JsonSerializer.Serialize(new RecentHostsFile([.. recent.Entries]), RecentHostsJsonContext.Default.RecentHostsFile);
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
