// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.Core.Session;

namespace LizTerm.Core.Profiles;

/// <summary>Every tag definition, and every profile that might carry one, read together.</summary>
public sealed record TagSnapshot(TagRegistry Registry, IReadOnlyList<SessionProfile> Profiles);

/// <summary>The one place a tag is changed across profiles: Manage Tags' rename (merges and case-only renames
/// included), recolour and delete (spec 4). Each action reads the profiles and tags.json fresh, writes the profiles
/// that carry the tag first and tags.json last, and returns what it did.
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
    /// the picker's reconciliation uses. The registry is saved only when that added something, and a failed save
    /// is swallowed: a snapshot is for display, and the next action writes the registry anyway.</summary>
    public TagSnapshot Load()
    {
        var all = profiles.LoadAll();
        var (registry, changed) = tags.Load().Register(all.SelectMany(p => p.Tags.Names));
        if (changed)
        {
            try { tags.Save(registry); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* display only; see summary */ }
        }
        return new TagSnapshot(registry, all);
    }

    public TagChangeResult Recolour(string name, TagColor color)
    {
        Guard(name, nameof(name));
        if (color == TagRegistry.FavoriteColor || !Enum.IsDefined(color))
            throw new ArgumentException($"{color} cannot be chosen for a tag.", nameof(color));
        var registry = Load().Registry;
        var recoloured = registry.Recolour(name, color);
        if (ReferenceEquals(recoloured, registry)) return TagChangeResult.Nothing;
        return TrySave(recoloured) is { } error ? new TagChangeResult(0, [], null, error) : TagChangeResult.Nothing;
    }

    public TagChangeResult Rename(string from, string to)
    {
        Guard(from, nameof(from));
        if (RenameProblem(to) is { } problem) throw new ArgumentException(problem, nameof(to));
        var target = TagSet.Normalize(to);
        if (TagSet.Normalize(from).Equals(target, StringComparison.Ordinal)) return TagChangeResult.Nothing;

        var snapshot = Load();
        var registry = snapshot.Registry;
        if (!registry.Contains(from)) return TagChangeResult.Nothing;
        // After a partial PLAIN rename both names stay defined in from's colour, so the list stays consistent and
        // the retry is a merge. A merge or a case-only rename (Contains is true for both) leaves the registry alone.
        var partial = registry.Contains(target)
            ? null
            : new TagRegistry([.. registry.Stored, new TagDefinition(target, registry.ColorOf(from))]);
        return Apply(snapshot, from, set => set.Rename(from, target), registry.Rename(from, target), partial);
    }

    public TagChangeResult Delete(string name)
    {
        Guard(name, nameof(name));
        var snapshot = Load();
        if (!snapshot.Registry.Contains(name)) return TagChangeResult.Nothing;
        return Apply(snapshot, name, set => set.Without(name), snapshot.Registry.Remove(name), partial: null);
    }

    /// <summary>Spec 4.2 steps 3 to 5. Carriers are written in LoadAll's order, stopping at the first failure;
    /// the registry is then written as <paramref name="done"/> when every carrier was, as
    /// <paramref name="partial"/> when some were and there is one, and not at all otherwise.</summary>
    private TagChangeResult Apply(TagSnapshot snapshot, string name, Func<TagSet, TagSet> change, TagRegistry done,
        TagRegistry? partial)
    {
        var carriers = snapshot.Profiles.Where(p => p.Tags.Contains(name)).ToList();
        var changed = new List<string>();
        foreach (var profile in carriers)
        {
            var updated = change(profile.Tags);
            // Ordinal, never TagSet.Equals: that ignores case, so a case-only rename would look unchanged and never
            // be written (spec 4.3).
            if (updated.Names.SequenceEqual(profile.Tags.Names, StringComparer.Ordinal)) continue;
            try
            {
                profiles.Save(profile with { Tags = updated });
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A failure saving the partial registry is secondary to the one being reported, so it is not.
                if (changed.Count > 0 && partial is not null) TrySave(partial);
                return new TagChangeResult(carriers.Count, changed, profile.Name, ex.Message);
            }
            changed.Add(profile.Name);
        }
        return new TagChangeResult(carriers.Count, changed, null, TrySave(done));
    }

    /// <summary>Saves the registry, answering the failure's message, or null when it was saved.</summary>
    private string? TrySave(TagRegistry registry)
    {
        try
        {
            tags.Save(registry);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ex.Message;
        }
    }

    private static void Guard(string name, string parameter)
    {
        if (TagRegistry.IsReserved(name))
            throw new ArgumentException($"{TagRegistry.FavoriteName} is reserved and cannot be changed.", parameter);
    }
}
