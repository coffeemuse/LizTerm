// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.Core.Session;

namespace LizTerm.Core.Profiles;

/// <summary>Every tag definition, and every profile that might carry one, read together.</summary>
public sealed record TagSnapshot(TagRegistry Registry, IReadOnlyList<SessionProfile> Profiles);

/// <summary>The one place a tag is changed across profiles: Manage Tags' rename (merges and case-only renames
/// included), recolour and delete (spec 4). Each action reads the profiles and tags.json fresh, writes the profiles
/// that carry the tag and then tags.json, and returns what it did. A plain rename first defines the new name
/// beside the old one in tags.json, so that wherever it stops both names are one colour and a retry is a merge.
///
/// Synchronous, for the UI thread, like every other picker command: a rename touches a handful of small files.
/// It works from a fresh LoadAll rather than ProfileStore.Update, whose fallback would save back a profile deleted
/// since the snapshot was read (spec 4.4).</summary>
public sealed class TagMaintenance(ProfileStore profiles, TagRegistryStore tags)
{
    /// <summary>Null when <paramref name="name"/> can be a rename target, else the message to show. In Core so the
    /// view model's message and <see cref="Rename"/>'s guard cannot disagree (spec 5).</summary>
    public static string? RenameProblem(string name)
    {
        var normalized = TagSet.Normalize(name);
        if (normalized.Length == 0) return "A tag name can't be blank.";
        if (normalized.Length > TagSet.MaxNameLength) return $"Tag names can be at most {TagSet.MaxNameLength} characters.";
        // The profile editor's Tags box splits on commas, so such a name would come back as two tags (spec 2.3).
        if (normalized.Contains(',')) return "A tag name can't contain a comma.";
        if (TagRegistry.IsReserved(normalized)) return $"{TagRegistry.FavoriteName} is reserved.";
        return null;
    }

    /// <summary>Every profile and the registry, from disk, with any tag name a profile carries registered — the rule
    /// the picker's reconciliation uses — but in memory only: a load must not write (Core CLAUDE.md). The list can
    /// show the name at once, and the next action, which writes the registry anyway, is what persists it.</summary>
    public TagSnapshot Load()
    {
        var all = profiles.LoadAll();
        var (registry, _) = tags.Load().Register(all.SelectMany(p => p.Tags.Names));
        return new TagSnapshot(registry, all);
    }

    /// <summary>One tags.json write, or none: TagRegistry.Recolour answers the same instance for an unknown name
    /// and for the colour the tag already has, and throws for the reserved tag and colour.</summary>
    public TagChangeResult Recolour(string name, TagColor color)
    {
        TagRegistry.ThrowIfReserved(name, nameof(name));
        var snapshot = Load();
        var recoloured = snapshot.Registry.Recolour(name, color);
        if (ReferenceEquals(recoloured, snapshot.Registry)) return TagChangeResult.Nothing;
        return tags.TrySave(recoloured) is { } error
            ? new TagChangeResult(0, [], null, error, snapshot)
            : new TagChangeResult(0, [], null, null, snapshot with { Registry = recoloured });
    }

    /// <summary>Renames <paramref name="from"/> to <paramref name="to"/> across the registry and every profile
    /// that carries it -- a plain rename, a case-only rename, or a merge onto an existing definition, decided by
    /// what <paramref name="to"/> already is. Pass the STORED spelling of <paramref name="from"/>: the
    /// identical-name no-op below compares it as given, ordinally, against <paramref name="to"/>, so
    /// <c>Rename("DEV", "DEV")</c> against a registry that actually holds "dev" would not be recognised as a
    /// no-op. The view model always passes the stored name; a future caller (#55's import) might not.</summary>
    public TagChangeResult Rename(string from, string to)
    {
        TagRegistry.ThrowIfReserved(from, nameof(from));
        if (RenameProblem(to) is { } problem) throw new ArgumentException(problem, nameof(to));
        var target = TagSet.Normalize(to);
        if (TagSet.Normalize(from).Equals(target, StringComparison.Ordinal)) return TagChangeResult.Nothing;

        var snapshot = Load();
        var registry = snapshot.Registry;
        if (!registry.Contains(from)) return TagChangeResult.Nothing;
        // A merge writes the existing tag as the registry spells it, not as the user typed it (spec 2.3), so the
        // profiles and the file cannot end up holding three spellings of one definition. A case-only rename is
        // the one time the typed spelling IS the point.
        var caseOnly = target.Equals(TagSet.Normalize(from), StringComparison.OrdinalIgnoreCase);
        var existing = registry.Stored.FirstOrDefault(d => d.Name.Equals(target, StringComparison.OrdinalIgnoreCase));
        var written = existing is not null && !caseOnly ? existing.Name : target;
        // A PLAIN rename defines to beside from, in from's colour, before any profile is touched. Every point it
        // can then stop at -- a carrier that fails, a crash between two writes, a final save that fails -- leaves
        // both names in one colour, and the retry is a merge that keeps it. (Spec 4.2 wrote the pair only after a
        // partial failure, which left a crash, or a failed fallback save, with two colours for one tag.) A merge or
        // a case-only rename (Contains is true for both) has nothing to prepare.
        var prepare = registry.Contains(target)
            ? null
            : new TagRegistry([.. registry.Stored, new TagDefinition(target, registry.ColorOf(from))]);
        return Apply(snapshot, from, set => set.Rename(from, written), registry.Rename(from, target), prepare);
    }

    public TagChangeResult Delete(string name)
    {
        TagRegistry.ThrowIfReserved(name, nameof(name));
        var snapshot = Load();
        if (!snapshot.Registry.Contains(name)) return TagChangeResult.Nothing;
        return Apply(snapshot, name, set => set.Without(name), snapshot.Registry.Remove(name), prepare: null);
    }

    /// <summary>Spec 4.2 steps 3 to 5, with one change: when there is a <paramref name="prepare"/> registry and a
    /// carrier to write, it is saved first, and a failure there stops the action before any profile changed.
    /// Carriers are then written in LoadAll's order, stopping at the first failure, and <paramref name="done"/>
    /// is written only when every carrier was.</summary>
    private TagChangeResult Apply(TagSnapshot snapshot, string name, Func<TagSet, TagSet> change, TagRegistry done,
        TagRegistry? prepare)
    {
        var carriers = snapshot.Profiles.Where(p => p.Tags.Contains(name)).ToList();
        // What tags.json holds as the action goes, so the result can say how it was left.
        var onDisk = snapshot.Registry;
        if (carriers.Count > 0 && prepare is not null)
        {
            if (tags.TrySave(prepare) is { } notPrepared) return new TagChangeResult(carriers.Count, [], null, notPrepared, snapshot);
            onDisk = prepare;
        }
        var changed = new List<string>();
        var written = new Dictionary<string, SessionProfile>();
        TagSnapshot After(TagRegistry registry) =>
            new(registry, [.. snapshot.Profiles.Select(p => written.GetValueOrDefault(p.Name, p))]);
        foreach (var profile in carriers)
        {
            var updated = change(profile.Tags);
            // Ordinal, never TagSet.Equals: that ignores case, so a case-only rename would look unchanged and never
            // be written (spec 4.3).
            if (updated.Names.SequenceEqual(profile.Tags.Names, StringComparer.Ordinal)) continue;
            var saved = profile with { Tags = updated };
            try
            {
                profiles.Save(saved);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return new TagChangeResult(carriers.Count, changed, profile.Name, ex.Message, After(onDisk));
            }
            changed.Add(profile.Name);
            written[profile.Name] = saved;
        }
        var error = tags.TrySave(done);
        return new TagChangeResult(carriers.Count, changed, null, error, After(error is null ? done : onDisk));
    }
}
