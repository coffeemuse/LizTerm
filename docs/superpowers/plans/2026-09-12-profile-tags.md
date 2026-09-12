# Profile Tags and Notes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A profile can carry zero or more tags and one short note; the session list draws tags as uppercase colour chips with `FAVORITE` as a gold star, shows the note as a third line, and gains a filter box and scope drop-down.

**Architecture:** Tag *names* live on `SessionProfile` in a value-equality struct (`TagSet`); tag *colours* live in a registry file (`tags.json`) keyed by name, so one tag has one colour everywhere. The registry is reconciled — unknown names register themselves with an auto-assigned colour — by the picker view model on reload, never by `ProfileStore`, so Core's store stays a pure reader. The App projects each profile into a `ProfileRow` carrying already-resolved chips, because the row needs colour that the profile itself does not know.

**Tech Stack:** .NET 10, C# with `Nullable`/`ImplicitUsings` on solution-wide, Avalonia 12 + FluentTheme (dark, fixed), CommunityToolkit.Mvvm (`[ObservableProperty]`, `[RelayCommand]`), `System.Text.Json` source-generated contexts, xunit.v3 in VSTest mode.

**Spec:** `docs/superpowers/specs/2026-09-12-lizterm-profile-tags-design.md`

## Global Constraints

- **Licence headers.** Every hand-written `.cs` and `.axaml` file under `src/` and `tests/` starts with three lines in its comment syntax — `This file is part of LizTerm.`, then `Copyright 2026 by CoffeeMuse`, then `SPDX-License-Identifier: BSD-3-Clause`; in `.axaml` they go inside a comment before the root element. `RepositoryHeadersTests` fails the suite for a missing one.
- **Dependency rule.** `LizTerm.Core` depends only on the BCL and never mentions Avalonia or b3270. Nothing in this plan puts an `Avalonia.Media.Color` in Core — that is why `TagColor` exists.
- **Zero warnings.** CI builds with `-warnaserror`. Before calling anything done: `dotnet build LizTerm.slnx --no-incremental 2>&1 | grep -c " warning "` must print `0`.
- **Package versions** live only in `Directory.Packages.props`; no `Version` on a `PackageReference`. This plan adds no packages.
- **`ProfileStore.cs` is not modified.** Spec §2.5 — the scope drop-down does the organising, so `LoadAll()` keeps sorting by name alone.
- **Caps, copied from spec §3.2:** `MaxTags = 8`, `MaxNameLength = 16`, note `MaxLength = 120`, chips rendered before overflow `= 3`.
- **Reserved tag:** the name is exactly `FAVORITE` (singular), always `TagColor.Gold`, never written to `tags.json`.
- **Test commands.** Whole suite: `dotnet test LizTerm.slnx`. One class: `dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~TagSetTests"`. One test: append `.Test_method_name` to the `~` pattern.

## Deviations from the spec, decided while planning

Two, both flagged here because an executor should not discover them as surprises.

**D1. The list's item is a `ProfileRow`, not a `SessionProfile`.** Spec §5.1 describes the chips without naming the item type, and issue #43 had assumed the template would bind the record directly. It cannot: a chip needs a colour, and under §2.2 the profile does not know its tags' colours — only the registry does. Resolving colour inside a `DataTemplate` would mean either a multi-binding against the registry or static mutable state. So the picker projects each visible profile into a `ProfileRow` that carries resolved chips.

`Profiles` (the loaded `SessionProfile` set) **stays exactly as it is**, because `QuickConnect` resolves against it and depends on reference identity for pin write-back safety — see the long comment at `ProfilePickerViewModel.QuickConnect`. Only a new `VisibleRows` collection is bound to the `ListBox`.

**D2. `TagRegistry.Register` returns a tuple, not an `out` parameter.** Spec §4.1 says "returning both a registry and whether anything changed"; `(TagRegistry Registry, bool Changed)` is the readable form and the easier one to assert on.

---

## File Structure

**New — Core**

| File | Responsibility |
|---|---|
| `src/LizTerm.Core/Session/TagSet.cs` | An ordered, de-duplicated, capped set of tag names with value equality |
| `src/LizTerm.Core/Session/TagSetJsonConverter.cs` | `TagSet` ↔ a JSON string array, lenient on malformed input |
| `src/LizTerm.Core/Profiles/TagColor.cs` | The eight-member chip palette enum |
| `src/LizTerm.Core/Profiles/TagDefinition.cs` | One tag's definition: name and colour |
| `src/LizTerm.Core/Profiles/TagRegistry.cs` | Lookup, registration, least-used colour assignment, the reserved tag |
| `src/LizTerm.Core/Profiles/TagRegistryFile.cs` | The on-disk shape of `tags.json` |
| `src/LizTerm.Core/Profiles/TagRegistryJsonContext.cs` | Its source-generated serialiser context |
| `src/LizTerm.Core/Profiles/TagRegistryStore.cs` | Load and atomic save of `tags.json` |

**New — App**

| File | Responsibility |
|---|---|
| `src/LizTerm.App/Rendering/TagPalette.cs` | `TagColor` → brush, mirroring `Rendering/Palette.cs` |
| `src/LizTerm.App/ViewModels/ProfileRow.cs` | One list row: name, host, note, star, resolved chips, overflow |
| `src/LizTerm.App/ViewModels/ScopeOption.cs` | One drop-down entry: a label and the tag it narrows to |

**New — tests**

`tests/LizTerm.Core.Tests/Session/TagSetTests.cs`, `tests/LizTerm.Core.Tests/Profiles/TagRegistryTests.cs`, `tests/LizTerm.Core.Tests/Profiles/TagRegistryStoreTests.cs`, `tests/LizTerm.App.Tests/Rendering/TagPaletteTests.cs`, `tests/LizTerm.App.Tests/ViewModels/ProfileRowTests.cs`.

**Modified**

| File | Change |
|---|---|
| `src/LizTerm.Core/Session/SessionProfile.cs` | Two parameters and their doc comments |
| `src/LizTerm.Core/Profiles/AppPaths.cs` | `TagsFile()`, and the class doc comment it makes stale |
| `src/LizTerm.App/ViewModels/ProfileEditorViewModel.cs` | Tag and note state, parsing, validation, `TryBuild` |
| `src/LizTerm.App/Views/ProfileEditorWindow.axaml` | Two grid rows and two row definitions |
| `src/LizTerm.App/ViewModels/ProfilePickerViewModel.cs` | Registry, scopes, filter, `VisibleRows`, `SelectedRow` |
| `src/LizTerm.App/Views/ProfilePickerWindow.axaml` | Filter row, item template, window height |
| `src/LizTerm.App/Views/ProfilePickerWindow.axaml.cs` | Optional `TagRegistryStore` constructor parameter |
| `src/LizTerm.App/App.axaml.cs:190` | Pass the real registry store |
| `tests/LizTerm.Core.Tests/Profiles/ProfileStoreTests.cs` | Tags and note in the round trip; the equality regression |
| `tests/LizTerm.App.Tests/ViewModels/ProfileViewModelsTests.cs` | Editor and picker behaviour |
| `tests/LizTerm.App.Tests/Views/ProfileEditorWindowTests.cs` | The two new rows |
| `tests/LizTerm.App.Tests/Views/ProfilePickerWindowTests.cs` | Star, chips, overflow, note, fit at `MinWidth` |
| `docs/user-guide.md` + `src/LizTerm.App/Assets/Docs/user-guide.html` | Settings table and the filter row |

---

### Task 1: `TagSet`

The value type everything else depends on. No consumers yet, so it lands and is tested alone.

**Files:**
- Create: `src/LizTerm.Core/Session/TagSet.cs`
- Test: `tests/LizTerm.Core.Tests/Session/TagSetTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `LizTerm.Core.Session.TagSet` — `struct`, `IEquatable<TagSet>`. Statics: `Empty`, `From(IEnumerable<string>?)`, `Normalize(string)`, `Split(string)`, `const int MaxTags = 8`, `const int MaxNameLength = 16`. Instance: `Names` (`IReadOnlyList<string>`), `Count`, `IsEmpty`, `Contains(string)`, `With(string)`, `Without(string)`, `ToString()`, `==`, `!=`.

- [ ] **Step 1: Write the failing test**

Create `tests/LizTerm.Core.Tests/Session/TagSetTests.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.Core.Session;

namespace LizTerm.Core.Tests.Session;

public class TagSetTests
{
    /// <summary>Every existing profile file produces default(TagSet), whose backing ImmutableArray is itself
    /// default rather than empty — so every member has to guard IsDefaultOrEmpty or throw
    /// NullReferenceException on the most common value in the system.</summary>
    [Fact]
    public void The_default_value_behaves_as_an_empty_set()
    {
        TagSet none = default;
        Assert.Equal(0, none.Count);
        Assert.True(none.IsEmpty);
        Assert.Empty(none.Names);
        Assert.False(none.Contains("PROD"));
        Assert.Equal("", none.ToString());
        Assert.Equal(TagSet.Empty, none);
        Assert.Equal(TagSet.Empty.GetHashCode(), none.GetHashCode());
    }

    [Fact]
    public void From_trims_strips_a_leading_hash_and_drops_blanks()
    {
        var tags = TagSet.From(["  PROD ", "#MVS", "", "   ", "# TEST"]);
        Assert.Equal(["PROD", "MVS", "TEST"], tags.Names);
    }

    /// <summary>Robert writes tags as "#PROD"; the hash is a display convention, so "#PROD" and "PROD" must be
    /// one tag rather than two that look identical in the drop-down.</summary>
    [Fact]
    public void From_deduplicates_case_insensitively_keeping_the_first_casing_and_preserving_order()
    {
        var tags = TagSet.From(["Prod", "mvs", "#prod", "PROD", "MVS"]);
        Assert.Equal(["Prod", "mvs"], tags.Names);
    }

    [Fact]
    public void From_drops_an_over_long_name_rather_than_truncating_it()
    {
        var tooLong = new string('x', TagSet.MaxNameLength + 1);
        var justRight = new string('y', TagSet.MaxNameLength);
        var tags = TagSet.From([tooLong, justRight]);
        Assert.Equal([justRight], tags.Names);
    }

    [Fact]
    public void From_keeps_only_the_first_MaxTags_names()
    {
        var tags = TagSet.From(Enumerable.Range(0, TagSet.MaxTags + 3).Select(i => $"T{i}"));
        Assert.Equal(TagSet.MaxTags, tags.Count);
        Assert.Equal("T0", tags.Names[0]);
        Assert.DoesNotContain($"T{TagSet.MaxTags}", tags.Names);
    }

    [Fact]
    public void Equality_ignores_case_but_respects_order()
    {
        Assert.Equal(TagSet.From(["PROD", "MVS"]), TagSet.From(["prod", "mvs"]));
        Assert.True(TagSet.From(["PROD"]) == TagSet.From(["prod"]));
        Assert.True(TagSet.From(["PROD", "MVS"]) != TagSet.From(["MVS", "PROD"]));
        Assert.NotEqual(TagSet.From(["PROD"]), TagSet.From(["PROD", "MVS"]));
    }

    /// <summary>An Equals that says two values are the same while GetHashCode disagrees makes them behave as
    /// different keys in any dictionary or set, which is the bug this whole struct exists to avoid.</summary>
    [Fact]
    public void GetHashCode_agrees_with_Equals()
    {
        Assert.Equal(TagSet.From(["PROD", "MVS"]).GetHashCode(), TagSet.From(["prod", "MVS"]).GetHashCode());
        Assert.Single(new HashSet<TagSet> { TagSet.From(["PROD"]), TagSet.From(["prod"]) });
    }

    [Fact]
    public void Contains_With_and_Without_ignore_case_and_the_hash()
    {
        var tags = TagSet.From(["PROD"]);
        Assert.True(tags.Contains("prod"));
        Assert.True(tags.Contains("#PROD"));

        var added = tags.With("MVS");
        Assert.Equal(["PROD", "MVS"], added.Names);
        Assert.Equal(added, added.With("mvs"));

        Assert.Equal(["MVS"], added.Without("#prod").Names);
        Assert.Equal(added, added.Without("nothing"));
    }

    [Fact]
    public void ToString_round_trips_through_Split_and_From()
    {
        var tags = TagSet.From(["PROD", "MVS"]);
        Assert.Equal("PROD, MVS", tags.ToString());
        Assert.Equal(tags, TagSet.From(TagSet.Split(tags.ToString())));
    }

    [Fact]
    public void Split_takes_the_editors_comma_separated_box_including_a_trailing_comma()
    {
        Assert.Equal(["PROD", "MVS"], TagSet.Split(" PROD , #MVS ,, "));
        Assert.Empty(TagSet.Split("   "));
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~TagSetTests"`

Expected: FAIL to **compile** — `error CS0246: The type or namespace name 'TagSet' could not be found`. A compile failure is the correct red for a type that does not exist yet.

- [ ] **Step 3: Write the implementation**

Create `src/LizTerm.Core/Session/TagSet.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Collections.Immutable;

namespace LizTerm.Core.Session;

/// <summary>The tag names one profile carries: ordered, de-duplicated case-insensitively, and capped. A struct
/// with hand-written value equality rather than a list, because <see cref="SessionProfile"/> is a record and a
/// record's synthesised Equals compares a collection member by REFERENCE — two structurally identical profiles
/// read from disk would stop being equal, which ProfileStoreTests asserts they are. Hand-writing
/// <c>Equals</c> on the record itself was the alternative and was rejected: a field added later would be
/// silently absent from equality, and that record's whole design is about fields being added later.
///
/// Colours are deliberately absent. A tag's colour belongs to its definition in <c>TagRegistry</c>, so
/// <c>PROD</c> is one colour everywhere rather than one colour per profile that happens to use it.
///
/// <c>default(TagSet)</c> is the value every profile file written before tags existed produces, and its backing
/// array is <c>default</c> rather than empty — so every member here guards
/// <see cref="ImmutableArray{T}.IsDefaultOrEmpty"/>.</summary>
public readonly struct TagSet : IEquatable<TagSet>
{
    /// <summary>Caps against a pathological hand-edited file, not limits a user with two or three tags meets.
    /// <see cref="From"/> enforces both by discarding what does not fit; the profile editor validates what the
    /// user typed BEFORE calling From, because afterwards the evidence is gone.</summary>
    public const int MaxTags = 8;

    public const int MaxNameLength = 16;

    private readonly ImmutableArray<string> _names;

    private TagSet(ImmutableArray<string> names) => _names = names;

    public static TagSet Empty { get; } = new([]);

    /// <summary>Normalises and filters: trim, drop a leading '#', drop blanks, drop anything longer than
    /// <see cref="MaxNameLength"/>, de-duplicate ignoring case keeping the first casing seen, keep source order,
    /// and stop at <see cref="MaxTags"/>. An over-long name is dropped rather than truncated — truncating would
    /// silently invent a different tag.</summary>
    public static TagSet From(IEnumerable<string>? names)
    {
        if (names is null) return Empty;
        var kept = new List<string>(MaxTags);
        foreach (var raw in names)
        {
            if (raw is null) continue;
            var name = Normalize(raw);
            if (name.Length == 0 || name.Length > MaxNameLength) continue;
            if (kept.Any(k => k.Equals(name, StringComparison.OrdinalIgnoreCase))) continue;
            kept.Add(name);
            if (kept.Count == MaxTags) break;
        }
        return kept.Count == 0 ? Empty : new TagSet([.. kept]);
    }

    /// <summary>One name as it is stored: trimmed, without a leading '#'. The hash is a display convention — it
    /// is how Robert writes a tag and how the scope drop-down renders one — so "#PROD" and "PROD" must never
    /// become two tags that look identical wherever they are listed.</summary>
    public static string Normalize(string name)
    {
        var trimmed = name.Trim();
        return trimmed.StartsWith('#') ? trimmed.TrimStart('#').Trim() : trimmed;
    }

    /// <summary>The profile editor's comma-separated box, as names. Normalisation and the caps are
    /// <see cref="From"/>'s job; this only splits, so the editor can count what the user typed before any of it
    /// is discarded.</summary>
    public static IReadOnlyList<string> Split(string text) =>
        [.. text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Select(Normalize)
                .Where(name => name.Length > 0)];

    private ImmutableArray<string> Safe => _names.IsDefaultOrEmpty ? [] : _names;

    public IReadOnlyList<string> Names => Safe;

    public int Count => Safe.Length;

    public bool IsEmpty => Count == 0;

    public bool Contains(string name)
    {
        var wanted = Normalize(name);
        return Safe.Any(n => n.Equals(wanted, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>This set with <paramref name="name"/> appended, or this set unchanged when it is already there.
    /// The profile editor's FAVORITE checkbox is the caller.</summary>
    public TagSet With(string name) => Contains(name) ? this : From([.. Names, name]);

    public TagSet Without(string name)
    {
        var unwanted = Normalize(name);
        return Contains(name)
            ? From(Names.Where(n => !n.Equals(unwanted, StringComparison.OrdinalIgnoreCase)))
            : this;
    }

    public bool Equals(TagSet other)
    {
        var mine = Safe;
        var theirs = other.Safe;
        if (mine.Length != theirs.Length) return false;
        for (var i = 0; i < mine.Length; i++)
        {
            if (!mine[i].Equals(theirs[i], StringComparison.OrdinalIgnoreCase)) return false;
        }
        return true;
    }

    public override bool Equals(object? obj) => obj is TagSet other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var name in Safe) hash.Add(name.ToLowerInvariant());
        return hash.ToHashCode();
    }

    public static bool operator ==(TagSet left, TagSet right) => left.Equals(right);

    public static bool operator !=(TagSet left, TagSet right) => !left.Equals(right);

    /// <summary>What the profile editor's box shows. <see cref="Split"/> reads it back.</summary>
    public override string ToString() => string.Join(", ", Names);
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~TagSetTests"`

Expected: PASS, 10 tests.

- [ ] **Step 5: Commit**

```bash
git add src/LizTerm.Core/Session/TagSet.cs tests/LizTerm.Core.Tests/Session/TagSetTests.cs
git commit -m "Add TagSet, a value-equality set of tag names"
```

---

### Task 2: `TagColor`, `TagDefinition` and `TagRegistry`

The registry model, in memory, with no I/O.

**Files:**
- Create: `src/LizTerm.Core/Profiles/TagColor.cs`, `src/LizTerm.Core/Profiles/TagDefinition.cs`, `src/LizTerm.Core/Profiles/TagRegistry.cs`
- Test: `tests/LizTerm.Core.Tests/Profiles/TagRegistryTests.cs`

**Interfaces:**
- Consumes: `TagSet.Normalize`, `TagSet.MaxNameLength` from Task 1.
- Produces:
  - `LizTerm.Core.Profiles.TagColor` — `enum { Gold, Red, Amber, Green, Blue, Purple, Teal, Grey }`.
  - `LizTerm.Core.Profiles.TagDefinition(string Name, TagColor Color)` — `sealed record`.
  - `LizTerm.Core.Profiles.TagRegistry` — `sealed class`. Ctor `TagRegistry(IEnumerable<TagDefinition>)`. Statics: `Empty`, `const string FavoriteName = "FAVORITE"`, `const TagColor FavoriteColor = TagColor.Gold`, `const TagColor UnregisteredColor = TagColor.Grey`, `Favorite` (`TagDefinition`), `AssignableColors` (`IReadOnlyList<TagColor>`), `IsReserved(string)`. Instance: `All`, `Stored` (both `IReadOnlyList<TagDefinition>`), `ColorOf(string)`, `Register(IEnumerable<string>)` returning `(TagRegistry Registry, bool Changed)`.

- [ ] **Step 1: Write the failing test**

Create `tests/LizTerm.Core.Tests/Profiles/TagRegistryTests.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.Core.Profiles;

namespace LizTerm.Core.Tests.Profiles;

public class TagRegistryTests
{
    [Fact]
    public void The_reserved_tag_is_always_present_always_gold_and_never_stored()
    {
        var registry = TagRegistry.Empty;
        Assert.Equal(TagColor.Gold, registry.ColorOf("FAVORITE"));
        Assert.Equal(TagColor.Gold, registry.ColorOf("favorite"));
        Assert.Contains(TagRegistry.Favorite, registry.All);
        Assert.Empty(registry.Stored);
    }

    /// <summary>"Immutable definition, just changing what items are tagged with it": a hand-edit cannot
    /// recolour or rename it, so an entry for it in a file is ignored rather than honoured.</summary>
    [Fact]
    public void A_file_entry_for_the_reserved_tag_is_ignored()
    {
        var registry = new TagRegistry([new TagDefinition("FAVORITE", TagColor.Red), new TagDefinition("PROD", TagColor.Red)]);
        Assert.Equal(TagColor.Gold, registry.ColorOf("FAVORITE"));
        Assert.Equal(["PROD"], registry.Stored.Select(d => d.Name));
    }

    [Fact]
    public void Gold_is_reserved_and_never_auto_assigned()
    {
        Assert.DoesNotContain(TagColor.Gold, TagRegistry.AssignableColors);
        var (registry, _) = TagRegistry.Empty.Register(Enumerable.Range(0, 20).Select(i => $"T{i}"));
        Assert.DoesNotContain(TagColor.Gold, registry.Stored.Select(d => d.Color));
    }

    [Fact]
    public void Register_gives_each_new_tag_the_least_used_colour_breaking_ties_by_enum_order()
    {
        var (registry, changed) = TagRegistry.Empty.Register(["PROD", "MVS", "TEST"]);
        Assert.True(changed);
        Assert.Equal(
            [TagRegistry.AssignableColors[0], TagRegistry.AssignableColors[1], TagRegistry.AssignableColors[2]],
            new[] { registry.ColorOf("PROD"), registry.ColorOf("MVS"), registry.ColorOf("TEST") });

        // Every assignable colour used once, so the next tag wraps to the first again.
        var (full, _) = TagRegistry.Empty.Register(TagRegistry.AssignableColors.Select((_, i) => $"T{i}"));
        var (wrapped, _) = full.Register(["ONE MORE"]);
        Assert.Equal(TagRegistry.AssignableColors[0], wrapped.ColorOf("ONE MORE"));
    }

    [Fact]
    public void Register_keeps_an_existing_definition_and_reports_no_change_when_every_name_is_known()
    {
        var (first, _) = TagRegistry.Empty.Register(["PROD"]);
        var colour = first.ColorOf("PROD");

        var (second, changed) = first.Register(["prod", "#PROD", "FAVORITE"]);
        Assert.False(changed);
        Assert.Same(first, second);
        Assert.Equal(colour, second.ColorOf("PROD"));
    }

    [Fact]
    public void Register_ignores_blank_and_over_long_names()
    {
        var (registry, changed) = TagRegistry.Empty.Register(["", "   ", new string('x', 17)]);
        Assert.False(changed);
        Assert.Empty(registry.Stored);
    }

    /// <summary>Reconciliation registers before anything renders, so this is a defensive answer rather than a
    /// path a user reaches — but it must be a colour rather than a throw.</summary>
    [Fact]
    public void ColorOf_an_unregistered_tag_is_the_documented_fallback()
    {
        Assert.Equal(TagRegistry.UnregisteredColor, TagRegistry.Empty.ColorOf("NEVER SEEN"));
    }

    [Fact]
    public void All_lists_the_reserved_tag_first_then_the_rest_alphabetically_ignoring_case()
    {
        var registry = new TagRegistry(
            [new TagDefinition("zeta", TagColor.Red), new TagDefinition("Alpha", TagColor.Blue), new TagDefinition("mvs", TagColor.Green)]);
        Assert.Equal(["FAVORITE", "Alpha", "mvs", "zeta"], registry.All.Select(d => d.Name));
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~TagRegistryTests"`

Expected: FAIL to compile — `error CS0246: The type or namespace name 'TagRegistry' could not be found`.

- [ ] **Step 3: Write the implementation**

Create `src/LizTerm.Core/Profiles/TagColor.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

namespace LizTerm.Core.Profiles;

/// <summary>A tag chip's colour, by name. In Core and BCL-only, because Core never mentions Avalonia; the App
/// maps each member to a brush in <c>Rendering/TagPalette.cs</c>.
///
/// Named for the TAG rather than the profile, and living here rather than in <c>Core.Session</c>, because a
/// colour belongs to a tag's definition — <c>SessionProfile</c> never mentions this type. <c>HostColor</c> is
/// deliberately not reused: it is the 17-member 3270 screen model in b3270's naming order, tuned by
/// <c>Rendering/Palette.cs</c> for a black terminal ground.
///
/// <see cref="Gold"/> is reserved for the FAVORITE tag and is never auto-assigned. Written to <c>tags.json</c>
/// by name, so reordering this enum can never change a saved meaning.</summary>
public enum TagColor
{
    Gold,
    Red,
    Amber,
    Green,
    Blue,
    Purple,
    Teal,
    Grey,
}
```

Create `src/LizTerm.Core/Profiles/TagDefinition.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

namespace LizTerm.Core.Profiles;

/// <summary>One tag's definition, which exists independently of any profile that uses it. Profiles carry tag
/// NAMES; this is where the colour lives, so a tag has one colour everywhere rather than one per profile.</summary>
/// <param name="Name">Stored trimmed and without a leading '#', as <c>TagSet.Normalize</c> produces. Compared
/// ignoring case throughout, so <c>prod</c> and <c>PROD</c> are one tag.</param>
public sealed record TagDefinition(string Name, TagColor Color);
```

Create `src/LizTerm.Core/Profiles/TagRegistry.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.Core.Session;

namespace LizTerm.Core.Profiles;

/// <summary>Every tag definition in force, keyed by name ignoring case. Immutable: <see cref="Register"/>
/// returns a new registry rather than mutating this one, so the picker can hold one value and replace it.
///
/// FAVORITE is synthesised, never stored (spec 4.2): it is always present, always <see cref="FavoriteColor"/>,
/// and an entry for it found in a file is dropped. That is what makes its definition immutable while what is
/// tagged with it stays free to change.</summary>
public sealed class TagRegistry
{
    /// <summary>The reserved tag. Singular, this spelling, and drawn as a gold star rather than a text chip.</summary>
    public const string FavoriteName = "FAVORITE";

    public const TagColor FavoriteColor = TagColor.Gold;

    /// <summary>What <see cref="ColorOf"/> answers for a name no definition covers. Reconciliation registers
    /// every name it sees before anything renders, so this is a defensive answer rather than one a user meets —
    /// but a missing definition must be a quiet colour, not a throw.</summary>
    public const TagColor UnregisteredColor = TagColor.Grey;

    public static readonly TagDefinition Favorite = new(FavoriteName, FavoriteColor);

    /// <summary>The colours <see cref="Register"/> draws from: every member except the reserved
    /// <see cref="FavoriteColor"/>, in enum order, which is also the tie-break order.</summary>
    public static readonly IReadOnlyList<TagColor> AssignableColors =
        [.. Enum.GetValues<TagColor>().Where(color => color != FavoriteColor)];

    private readonly Dictionary<string, TagDefinition> _byName;

    public static TagRegistry Empty { get; } = new([]);

    /// <summary>Keeps the last definition for a repeated name, drops a blank or over-long one, and drops any
    /// entry for the reserved tag.</summary>
    public TagRegistry(IEnumerable<TagDefinition> definitions)
    {
        _byName = new Dictionary<string, TagDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var definition in definitions)
        {
            var name = TagSet.Normalize(definition.Name);
            if (name.Length == 0 || name.Length > TagSet.MaxNameLength || IsReserved(name)) continue;
            _byName[name] = new TagDefinition(name, definition.Color);
        }
    }

    public static bool IsReserved(string name) =>
        TagSet.Normalize(name).Equals(FavoriteName, StringComparison.OrdinalIgnoreCase);

    /// <summary>Every definition: the reserved one first, then the rest by name ignoring case. The scope
    /// drop-down renders this directly, so the order is alphabetical rather than whichever tag happened to be
    /// created first — insertion order is not something a user can predict.</summary>
    public IReadOnlyList<TagDefinition> All => [Favorite, .. Stored];

    /// <summary>What <c>TagRegistryStore</c> writes: <see cref="All"/> without the synthesised reserved tag.</summary>
    public IReadOnlyList<TagDefinition> Stored =>
        [.. _byName.Values.OrderBy(definition => definition.Name, StringComparer.OrdinalIgnoreCase)];

    public TagColor ColorOf(string name)
    {
        if (IsReserved(name)) return FavoriteColor;
        return _byName.TryGetValue(TagSet.Normalize(name), out var definition) ? definition.Color : UnregisteredColor;
    }

    /// <summary>This registry plus a definition for every name it does not already know, each taking the
    /// assignable colour fewest definitions currently use. Existing definitions are never touched, so a colour
    /// chosen in Manage Tags survives every later reconciliation.</summary>
    /// <returns>The registry to use, and whether anything was added — a caller writes the file only when
    /// something was, so merely opening the picker does not rewrite <c>tags.json</c>.</returns>
    public (TagRegistry Registry, bool Changed) Register(IEnumerable<string> names)
    {
        var counts = _byName.Values.GroupBy(definition => definition.Color)
            .ToDictionary(group => group.Key, group => group.Count());
        var known = new HashSet<string>(_byName.Keys, StringComparer.OrdinalIgnoreCase);
        var added = new List<TagDefinition>();

        foreach (var raw in names)
        {
            if (raw is null) continue;
            var name = TagSet.Normalize(raw);
            if (name.Length == 0 || name.Length > TagSet.MaxNameLength || IsReserved(name)) continue;
            if (!known.Add(name)) continue;
            var color = LeastUsed(counts);
            counts[color] = counts.GetValueOrDefault(color) + 1;
            added.Add(new TagDefinition(name, color));
        }

        return added.Count == 0 ? (this, false) : (new TagRegistry([.. Stored, .. added]), true);
    }

    /// <summary>The assignable colour fewest definitions use. OrderBy is stable and
    /// <see cref="AssignableColors"/> is already in enum order, so the ThenBy only makes the tie-break
    /// explicit.</summary>
    private static TagColor LeastUsed(IReadOnlyDictionary<TagColor, int> counts) =>
        AssignableColors.OrderBy(counts.GetValueOrDefault).ThenBy(color => (int)color).First();
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~TagRegistryTests"`

Expected: PASS, 8 tests.

- [ ] **Step 5: Commit**

```bash
git add src/LizTerm.Core/Profiles/TagColor.cs src/LizTerm.Core/Profiles/TagDefinition.cs \
        src/LizTerm.Core/Profiles/TagRegistry.cs tests/LizTerm.Core.Tests/Profiles/TagRegistryTests.cs
git commit -m "Add the tag registry, where a tag's colour lives"
```

---

### Task 3: `tags.json` — the registry's file

**Files:**
- Create: `src/LizTerm.Core/Profiles/TagRegistryFile.cs`, `src/LizTerm.Core/Profiles/TagRegistryJsonContext.cs`, `src/LizTerm.Core/Profiles/TagRegistryStore.cs`
- Modify: `src/LizTerm.Core/Profiles/AppPaths.cs`
- Test: `tests/LizTerm.Core.Tests/Profiles/TagRegistryStoreTests.cs`

**Interfaces:**
- Consumes: `TagRegistry`, `TagDefinition`, `TagColor` from Task 2.
- Produces:
  - `TagRegistryFile(IReadOnlyList<TagEntry>? Tags = null)` and `TagEntry(string Name = "", string Color = "")` — both `sealed record`, public.
  - `TagRegistryStore(string filePath)` — `sealed class`. `FilePath`, `static DefaultFile()`, `Load()` → `TagRegistry`, `Save(TagRegistry)`.
  - `AppPaths.TagsFile()` → `string`.

- [ ] **Step 1: Write the failing test**

Create `tests/LizTerm.Core.Tests/Profiles/TagRegistryStoreTests.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.Core.Profiles;

namespace LizTerm.Core.Tests.Profiles;

public class TagRegistryStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lizterm-tags-" + Guid.NewGuid().ToString("N"));

    private string File_ => Path.Combine(_dir, "tags.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void A_missing_file_loads_as_an_empty_registry()
    {
        Assert.Empty(new TagRegistryStore(File_).Load().Stored);
    }

    [Fact]
    public void Save_then_Load_round_trips_every_definition_and_writes_the_colour_by_name()
    {
        var store = new TagRegistryStore(File_);
        var (registry, _) = TagRegistry.Empty.Register(["PROD", "MVS"]);
        store.Save(registry);

        var json = File.ReadAllText(File_);
        Assert.Contains($"\"color\": \"{registry.ColorOf("PROD")}\"", json);

        var loaded = store.Load();
        Assert.Equal(["MVS", "PROD"], loaded.Stored.Select(d => d.Name));
        Assert.Equal(registry.ColorOf("PROD"), loaded.ColorOf("PROD"));
        Assert.Equal(registry.ColorOf("MVS"), loaded.ColorOf("MVS"));
    }

    /// <summary>The reserved tag is synthesised on load, so writing it would let a hand-edit recolour it.</summary>
    [Fact]
    public void The_reserved_tag_is_never_written_to_the_file()
    {
        var store = new TagRegistryStore(File_);
        store.Save(TagRegistry.Empty);
        Assert.DoesNotContain(TagRegistry.FavoriteName, File.ReadAllText(File_));
    }

    /// <summary>One unrecognised colour costs that tag its colour, not the whole registry — losing every
    /// definition to a single typo would be harsh where losing one is invisible, because reconciliation
    /// re-registers the name with a fresh colour on the next load.</summary>
    [Fact]
    public void An_unrecognised_colour_drops_that_entry_alone()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(File_, """
            { "tags": [ { "name": "PROD", "color": "Chartreuse" }, { "name": "MVS", "color": "Blue" } ] }
            """);

        var loaded = new TagRegistryStore(File_).Load();
        Assert.Equal(["MVS"], loaded.Stored.Select(d => d.Name));
        Assert.Equal(TagColor.Blue, loaded.ColorOf("MVS"));
    }

    [Fact]
    public void A_colour_name_reads_back_whatever_its_casing()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(File_, """{ "tags": [ { "name": "PROD", "color": "blue" } ] }""");
        Assert.Equal(TagColor.Blue, new TagRegistryStore(File_).Load().ColorOf("PROD"));
    }

    [Fact]
    public void An_unreadable_file_loads_as_an_empty_registry_rather_than_throwing()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(File_, "not json");
        Assert.Empty(new TagRegistryStore(File_).Load().Stored);
    }

    /// <summary>Written through a sibling temp file renamed over the target, as ProfileStore and SettingsStore
    /// both are, so a reader never sees a partial file and a crash mid-write leaves the old one.</summary>
    [Fact]
    public void Save_leaves_no_temp_file_behind()
    {
        var store = new TagRegistryStore(File_);
        var (registry, _) = TagRegistry.Empty.Register(["PROD"]);
        store.Save(registry);
        Assert.Equal(["tags.json"], Directory.GetFiles(_dir).Select(Path.GetFileName).Order());
    }

    [Fact]
    public void Save_creates_the_directory_when_it_is_missing()
    {
        var store = new TagRegistryStore(Path.Combine(_dir, "nested", "tags.json"));
        var (registry, _) = TagRegistry.Empty.Register(["PROD"]);
        store.Save(registry);
        Assert.Equal(["PROD"], store.Load().Stored.Select(d => d.Name));
    }

    [Fact]
    public void The_default_file_sits_beside_the_settings_file()
    {
        Assert.Equal(Path.Combine(AppPaths.ConfigRoot(), "tags.json"), AppPaths.TagsFile());
        Assert.Equal(AppPaths.TagsFile(), TagRegistryStore.DefaultFile());
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~TagRegistryStoreTests"`

Expected: FAIL to compile — `error CS0246: ... 'TagRegistryStore' could not be found`.

- [ ] **Step 3: Add `AppPaths.TagsFile()`**

In `src/LizTerm.Core/Profiles/AppPaths.cs`, update the class doc comment and add the method after `SettingsFile()`:

```csharp
/// <summary>Per-OS locations of LizTerm's own files: profiles, wire logs, the settings file and the tag registry
/// live side by side under one root.</summary>
```

```csharp
    /// <summary>The tag registry, one file beside settings.json. Holds a colour per tag name; FAVORITE is
    /// synthesised rather than stored, so deleting this file loses only the chosen colours — every tag name
    /// travels in its profiles and re-registers with a fresh colour.</summary>
    public static string TagsFile() => Path.Combine(ConfigRoot(), "tags.json");
```

- [ ] **Step 4: Write the file types and the store**

Create `src/LizTerm.Core/Profiles/TagRegistryFile.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

namespace LizTerm.Core.Profiles;

/// <summary>The on-disk shape of tags.json. An array of entries rather than a flat name-to-colour map, so a tag
/// can gain a field later without reshaping the file. Positional with a default on every parameter, as
/// SessionProfile and AppSettings are, so a key missing from a file reads as its default.</summary>
public sealed record TagRegistryFile(IReadOnlyList<TagEntry>? Tags = null);

/// <summary>One definition as written. <paramref name="Color"/> is a STRING rather than a
/// <see cref="TagColor"/>: JsonStringEnumConverter throws on a name it does not know, which would cost the
/// whole file for one typo, where reading it as text lets <c>TagRegistryStore.Load</c> drop that entry alone
/// and let reconciliation give the tag a fresh colour.</summary>
public sealed record TagEntry(string Name = "", string Color = "");
```

Create `src/LizTerm.Core/Profiles/TagRegistryJsonContext.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Text.Json.Serialization;

namespace LizTerm.Core.Profiles;

/// <summary>Its own context, not ProfileJsonContext's: one context per file, as SettingsJsonContext is separate
/// from ProfileJsonContext.</summary>
[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(TagRegistryFile))]
internal partial class TagRegistryJsonContext : JsonSerializerContext;
```

Create `src/LizTerm.Core/Profiles/TagRegistryStore.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Text.Json;

namespace LizTerm.Core.Profiles;

/// <summary>One JSON file holding a colour per tag name. Load never throws; Save writes through a sibling temp
/// file renamed over the target, the discipline ProfileStore and SettingsStore both use.</summary>
public sealed class TagRegistryStore(string filePath)
{
    public string FilePath { get; } = filePath;

    public static string DefaultFile() => AppPaths.TagsFile();

    /// <summary>The definitions on disk. A file that is missing, unreadable or not JSON loads as an empty
    /// registry rather than throwing: the names all travel in the profiles, so the cost of a lost registry is
    /// re-assigned colours, and refusing to start the picker over it would be worse.</summary>
    public TagRegistry Load()
    {
        TagRegistryFile? file;
        try
        {
            file = JsonSerializer.Deserialize(File.ReadAllText(FilePath), TagRegistryJsonContext.Default.TagRegistryFile);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return TagRegistry.Empty;
        }

        if (file?.Tags is null) return TagRegistry.Empty;

        var definitions = new List<TagDefinition>();
        foreach (var entry in file.Tags)
        {
            if (entry is null) continue;
            // Per entry, not per file: see TagEntry's remarks.
            if (!Enum.TryParse<TagColor>(entry.Color, ignoreCase: true, out var color)) continue;
            definitions.Add(new TagDefinition(entry.Name, color));
        }
        return new TagRegistry(definitions);
    }

    /// <summary>Writes <see cref="TagRegistry.Stored"/>, so the synthesised FAVORITE never reaches the file.</summary>
    public void Save(TagRegistry registry)
    {
        if (Path.GetDirectoryName(FilePath) is { Length: > 0 } directory) Directory.CreateDirectory(directory);
        var file = new TagRegistryFile([.. registry.Stored.Select(d => new TagEntry(d.Name, d.Color.ToString()))]);
        var json = JsonSerializer.Serialize(file, TagRegistryJsonContext.Default.TagRegistryFile);
        // Not ".json" beside a directory read for "*.json" anywhere, but the same rule as ProfileStore: a temp
        // file left by a crash must never be mistaken for the real one.
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
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~TagRegistryStoreTests"`

Expected: PASS, 9 tests.

- [ ] **Step 6: Commit**

```bash
git add src/LizTerm.Core/Profiles/TagRegistryFile.cs src/LizTerm.Core/Profiles/TagRegistryJsonContext.cs \
        src/LizTerm.Core/Profiles/TagRegistryStore.cs src/LizTerm.Core/Profiles/AppPaths.cs \
        tests/LizTerm.Core.Tests/Profiles/TagRegistryStoreTests.cs
git commit -m "Persist the tag registry as tags.json"
```

---

### Task 4: `SessionProfile.Tags` and `.Note`

The record change, and the equality regression the whole of Task 1 exists to prevent.

**Files:**
- Create: `src/LizTerm.Core/Session/TagSetJsonConverter.cs`
- Modify: `src/LizTerm.Core/Session/SessionProfile.cs`
- Test: `tests/LizTerm.Core.Tests/Profiles/ProfileStoreTests.cs`

**Interfaces:**
- Consumes: `TagSet` from Task 1.
- Produces: `SessionProfile.Tags` (`TagSet`, default `default`) and `SessionProfile.Note` (`string?`, default `null`); `TagSetJsonConverter : JsonConverter<TagSet>`.

- [ ] **Step 1: Write the failing tests**

In `tests/LizTerm.Core.Tests/Profiles/ProfileStoreTests.cs`, add `Tags` and `Note` to the existing round-trip test so its `Assert.Equal(profile, loaded)` actually exercises them. Replace `Save_then_LoadAll_round_trips_every_field` with:

```csharp
    /// <summary>The Assert.Equal is the point, not the fields: SessionProfile is a record, and a record
    /// compares a COLLECTION member by reference, so a Tags held as a list would make a profile read back from
    /// disk unequal to the one written. TagSet's value equality is what keeps this assertion true.</summary>
    [Fact]
    public void Save_then_LoadAll_round_trips_every_field()
    {
        var store = new ProfileStore(_dir);
        var profile = new SessionProfile
        {
            Name = "TK5", Host = "mvs.local", Port = 3270, UseTls = true, VerifyCertificate = false,
            Model = 4, Extended = false, CodePage = "bracket", LuName = "LU01", DestructiveBackspace = true,
            Tags = TagSet.From(["FAVORITE", "PROD", "MVS"]), Note = "IND$FILE test box, no live data",
        };
        store.Save(profile);
        var loaded = Assert.Single(store.LoadAll());
        Assert.Equal(profile, loaded);
        Assert.Equal(["FAVORITE", "PROD", "MVS"], loaded.Tags.Names);
        Assert.Equal("IND$FILE test box, no live data", loaded.Note);
    }

    [Fact]
    public void Tags_are_written_as_a_json_string_array()
    {
        var store = new ProfileStore(_dir);
        store.Save(new SessionProfile { Name = "p", Host = "h", Tags = TagSet.From(["PROD", "MVS"]) });
        var json = File.ReadAllText(Directory.GetFiles(_dir, "*.json").Single());
        Assert.Contains("\"PROD\"", json);
        Assert.Contains("\"MVS\"", json);
        Assert.DoesNotContain("\"color\"", json);
    }

    /// <summary>A profile file written before either field existed. Both must read as their declared defaults,
    /// which is what makes this change need no migration (spec 3.1).</summary>
    [Fact]
    public void A_file_without_tags_or_a_note_reads_as_an_empty_set_and_null()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "old.json"), """
            { "name": "old", "host": "h", "port": 23 }
            """);

        var old = Assert.Single(new ProfileStore(_dir).LoadAll());
        Assert.True(old.Tags.IsEmpty);
        Assert.Null(old.Note);
        Assert.Equal(default, old.Tags);
    }

    /// <summary>A hand-edited file is repaired on load rather than refused, the same choice Read already makes
    /// for a pin with no PEM. Nothing here should cost the profile.</summary>
    [Fact]
    public void A_hand_edited_tags_array_is_repaired_on_load()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "messy.json"), """
            { "name": "messy", "host": "h", "tags": ["#PROD", " prod ", "", "MVS"] }
            """);

        var messy = Assert.Single(new ProfileStore(_dir).LoadAll());
        Assert.Equal(["PROD", "MVS"], messy.Tags.Names);
    }

    /// <summary>Not an array at all. The whole profile must survive: losing a saved host because one key was
    /// mistyped by hand is the outcome LoadAll's leniency exists to avoid.</summary>
    [Fact]
    public void A_tags_key_that_is_not_an_array_costs_the_tags_not_the_profile()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "single.json"), """{ "name": "single", "host": "h", "tags": "PROD" }""");
        File.WriteAllText(Path.Combine(_dir, "object.json"), """{ "name": "object", "host": "h", "tags": { "a": 1 } }""");

        var loaded = new ProfileStore(_dir).LoadAll();
        Assert.Equal(["PROD"], loaded.Single(p => p.Name == "single").Tags.Names);
        Assert.True(loaded.Single(p => p.Name == "object").Tags.IsEmpty);
    }
```

Add `using LizTerm.Core.Session;` if it is not already there — it is.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~ProfileStoreTests"`

Expected: FAIL to compile — `error CS0117: 'SessionProfile' does not contain a definition for 'Tags'`.

- [ ] **Step 3: Write the converter**

Create `src/LizTerm.Core/Session/TagSetJsonConverter.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Text.Json;
using System.Text.Json.Serialization;

namespace LizTerm.Core.Session;

/// <summary>A <see cref="TagSet"/> as a JSON array of strings. Lenient on purpose, in both directions:
/// <see cref="TagSet.From"/> repairs duplicates, blanks, '#' prefixes and an over-long list, and a value that
/// is not an array at all yields an empty set rather than a JsonException. The leniency matters because
/// ProfileStore.Read catches JsonException and SKIPS the whole file — so a strict converter would turn one
/// mistyped key into a saved session that has silently vanished from the list.</summary>
public sealed class TagSetJsonConverter : JsonConverter<TagSet>
{
    public override TagSet Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String) return TagSet.From([reader.GetString()!]);
        if (reader.TokenType != JsonTokenType.StartArray)
        {
            // A scalar is already complete and Skip is a no-op; an object or a nested array is stepped over.
            reader.Skip();
            return TagSet.Empty;
        }

        var names = new List<string>();
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndArray) break;
            if (reader.TokenType == JsonTokenType.String) names.Add(reader.GetString()!);
            else reader.Skip();
        }
        return TagSet.From(names);
    }

    public override void Write(Utf8JsonWriter writer, TagSet value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var name in value.Names) writer.WriteStringValue(name);
        writer.WriteEndArray();
    }
}
```

- [ ] **Step 4: Add the two parameters**

In `src/LizTerm.Core/Session/SessionProfile.cs`, add `using System.Text.Json.Serialization;` at the top, two `<param>` doc comments alongside the existing ones, and the two parameters at the end of the positional list:

```csharp
/// <param name="Tags">The tag names this profile carries, or none. Names only: a tag's COLOUR belongs to its
/// definition in <c>TagRegistry</c>, so <c>PROD</c> is one colour everywhere rather than one per profile. A
/// <see cref="TagSet"/> rather than a list because this is a record, and a record compares a collection member
/// by reference — see TagSet's own remarks. <c>FAVORITE</c> is an ordinary member here and is drawn as a gold
/// star rather than a chip.</param>
/// <param name="Note">A short line the name cannot carry — "no live data", "LAN only" — shown under the host in
/// the session list, or null. Capped and single-line at the editor, so a pasted paragraph cannot reshape the
/// list.</param>
public sealed record SessionProfile(
    string Name = "",
    string Host = "",
    int Port = 23,
    bool UseTls = false,
    bool VerifyCertificate = true,
    CertificatePin? PinnedCertificate = null,
    int Model = 2,
    bool Extended = true,
    string CodePage = "cp037",
    string? LuName = null,
    bool DestructiveBackspace = true,
    int KeepAliveSeconds = 60,
    bool AutoReconnect = false,
    string? Oversize = null,
    [property: JsonConverter(typeof(TagSetJsonConverter))] TagSet Tags = default,
    string? Note = null);
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~ProfileStoreTests"`

Expected: PASS. Then the whole Core project, because `SessionProfile` is shared:

Run: `dotnet test tests/LizTerm.Core.Tests`

Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/LizTerm.Core/Session/SessionProfile.cs src/LizTerm.Core/Session/TagSetJsonConverter.cs \
        tests/LizTerm.Core.Tests/Profiles/ProfileStoreTests.cs
git commit -m "Give a profile tags and a note"
```

---

### Task 5: `TagPalette`

**Files:**
- Create: `src/LizTerm.App/Rendering/TagPalette.cs`
- Test: `tests/LizTerm.App.Tests/Rendering/TagPaletteTests.cs`

**Interfaces:**
- Consumes: `TagColor` from Task 2.
- Produces: `LizTerm.App.Rendering.TagPalette` — `static class`. `ColorOf(TagColor)` → `Avalonia.Media.Color`, `Brush(TagColor)` → `IBrush`, `ChipText` → `IBrush` (white), `Star` → `IBrush` (gold), `Overflow` → `IBrush`.

- [ ] **Step 1: Write the failing test**

Create `tests/LizTerm.App.Tests/Rendering/TagPaletteTests.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Media;
using LizTerm.App.Rendering;
using LizTerm.Core.Profiles;

namespace LizTerm.App.Tests.Rendering;

public class TagPaletteTests
{
    /// <summary>Driven by Enum.GetValues so a colour added to TagColor without a brush fails the suite rather
    /// than rendering an invisible chip. Palette's HostColor map has the same shape and the same risk.</summary>
    [Fact]
    public void Every_TagColor_member_maps_to_a_brush()
    {
        foreach (var color in Enum.GetValues<TagColor>())
        {
            Assert.NotNull(TagPalette.Brush(color));
        }
    }

    [Fact]
    public void The_same_colour_returns_the_same_cached_brush()
    {
        Assert.Same(TagPalette.Brush(TagColor.Red), TagPalette.Brush(TagColor.Red));
    }

    /// <summary>The spec claims white chip text clears 4.5:1 on every chip, and an accessibility claim nothing
    /// checks is a claim that rots — especially once #78 adds a light theme and someone retunes these values.
    /// WCAG 2.1 relative luminance and contrast ratio.</summary>
    [Fact]
    public void Every_chip_colour_clears_4_5_to_1_against_white_text()
    {
        foreach (var color in Enum.GetValues<TagColor>())
        {
            var ratio = Contrast(Colors.White, TagPalette.ColorOf(color));
            Assert.True(ratio >= 4.5, $"{color} gives white text only {ratio:F2}:1");
        }
    }

    private static double Contrast(Color a, Color b)
    {
        var (high, low) = (Math.Max(Luminance(a), Luminance(b)), Math.Min(Luminance(a), Luminance(b)));
        return (high + 0.05) / (low + 0.05);
    }

    private static double Luminance(Color c) =>
        0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);

    private static double Channel(byte value)
    {
        var v = value / 255.0;
        return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~TagPaletteTests"`

Expected: FAIL to compile — `error CS0246: ... 'TagPalette' could not be found`.

- [ ] **Step 3: Write the implementation**

Create `src/LizTerm.App/Rendering/TagPalette.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Media;
using Avalonia.Media.Immutable;
using LizTerm.Core.Profiles;

namespace LizTerm.App.Rendering;

/// <summary>Chip colours for the session list's tags, the App-side half of <see cref="TagColor"/> — Core holds
/// the names, this holds the pixels, which is what keeps Avalonia out of Core.
///
/// Every value is deep enough that WHITE chip text clears 4.5:1 on it, asserted by TagPaletteTests, and light
/// enough to read as a filled shape on the dark list. Chosen that way so the chips survive #78 adding a light
/// theme without needing a second palette: white-on-deep reads on either ground.
///
/// Separate from <see cref="Palette"/> on purpose. That one maps the 16 IBM host colours for the terminal
/// screen and is tuned for a black background; these are UI chrome.</summary>
public static class TagPalette
{
    private static readonly Dictionary<TagColor, Color> Colors = new()
    {
        // Reserved for FAVORITE. Only ever seen as the star's fill, but mapped like the rest so the
        // exhaustiveness test covers it and a future gold chip needs no new value.
        [TagColor.Gold] = Color.FromRgb(0x8A, 0x64, 0x00),
        [TagColor.Red] = Color.FromRgb(0xA8, 0x3A, 0x37),
        [TagColor.Amber] = Color.FromRgb(0x8A, 0x5A, 0x0F),
        [TagColor.Green] = Color.FromRgb(0x2E, 0x6B, 0x3F),
        [TagColor.Blue] = Color.FromRgb(0x2A, 0x5F, 0x9E),
        [TagColor.Purple] = Color.FromRgb(0x6B, 0x4C, 0x9E),
        [TagColor.Teal] = Color.FromRgb(0x1F, 0x5F, 0x66),
        [TagColor.Grey] = Color.FromRgb(0x5E, 0x5E, 0x5E),
    };

    private static readonly Dictionary<TagColor, IBrush> Cache = [];

    /// <summary>Chip text, on every colour. One value rather than one per chip because the palette above is
    /// chosen to make one value enough.</summary>
    public static IBrush ChipText { get; } = new ImmutableSolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));

    /// <summary>The FAVORITE star. Brighter than <see cref="TagColor.Gold"/>'s chip fill: a glyph is thin
    /// strokes on the list background rather than a filled block under white text, so it needs the contrast the
    /// other direction.</summary>
    public static IBrush Star { get; } = new ImmutableSolidColorBrush(Color.FromRgb(0xE8, 0xB2, 0x30));

    /// <summary>The "+n" chip, which stands for tags of several colours and so belongs to none of them.</summary>
    public static IBrush Overflow { get; } = new ImmutableSolidColorBrush(Color.FromRgb(0x4A, 0x4A, 0x4A));

    public static Color ColorOf(TagColor color) => Colors[color];

    public static IBrush Brush(TagColor color)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(color, out var brush)) return brush;
            brush = new ImmutableSolidColorBrush(ColorOf(color));
            Cache[color] = brush;
            return brush;
        }
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~TagPaletteTests"`

Expected: PASS, 3 tests. If `Every_chip_colour_clears_4_5_to_1_against_white_text` fails, darken the named colour until it passes — do not weaken the assertion.

- [ ] **Step 5: Commit**

```bash
git add src/LizTerm.App/Rendering/TagPalette.cs tests/LizTerm.App.Tests/Rendering/TagPaletteTests.cs
git commit -m "Map tag colours to chip brushes"
```

---

### Task 6: The profile editor

**Files:**
- Modify: `src/LizTerm.App/ViewModels/ProfileEditorViewModel.cs`, `src/LizTerm.App/Views/ProfileEditorWindow.axaml`
- Test: `tests/LizTerm.App.Tests/ViewModels/ProfileViewModelsTests.cs`, `tests/LizTerm.App.Tests/Views/ProfileEditorWindowTests.cs`

**Interfaces:**
- Consumes: `TagSet`, `TagRegistry.FavoriteName`, `SessionProfile.Tags`/`.Note`.
- Produces: `ProfileEditorViewModel.TagsText` (`string`), `.IsFavorite` (`bool`), `.Note` (`string`); `TryBuild` writing `Tags` and `Note`. Named controls for tests: `TagsBox`, `FavoriteBox`, `NoteBox`.

- [ ] **Step 1: Write the failing tests**

Append to `tests/LizTerm.App.Tests/ViewModels/ProfileViewModelsTests.cs`:

```csharp
    [Fact]
    public void Editor_round_trips_tags_and_a_note()
    {
        var existing = new SessionProfile
        {
            Name = "mvsce", Host = "h", Tags = TagSet.From(["FAVORITE", "PROD", "MVS"]), Note = "no live data",
        };
        var vm = new ProfileEditorViewModel(existing);

        // FAVORITE belongs to the checkbox, so it must not also appear in the box the user edits.
        Assert.True(vm.IsFavorite);
        Assert.Equal("PROD, MVS", vm.TagsText);
        Assert.Equal("no live data", vm.Note);

        var built = vm.TryBuild()!;
        Assert.Equal(existing.Tags, built.Tags);
        Assert.Equal("no live data", built.Note);
    }

    [Fact]
    public void Editor_defaults_to_no_tags_and_no_note()
    {
        var vm = new ProfileEditorViewModel(null) { Name = "n", Host = "h" };
        Assert.False(vm.IsFavorite);
        Assert.Equal("", vm.TagsText);
        var built = vm.TryBuild()!;
        Assert.True(built.Tags.IsEmpty);
        Assert.Null(built.Note);
    }

    [Fact]
    public void Editor_normalises_the_tag_box_and_puts_the_checkbox_first()
    {
        var vm = new ProfileEditorViewModel(null) { Name = "n", Host = "h", IsFavorite = true, TagsText = " #prod , mvs ,, prod " };
        var built = vm.TryBuild()!;
        Assert.Equal(["FAVORITE", "prod", "mvs"], built.Tags.Names);
    }

    /// <summary>The checkbox owns the reserved tag, so typing it is forgiven rather than refused: the box drops
    /// it and the checkbox visibly turns on, which explains itself without a validation message.</summary>
    [Fact]
    public void Typing_the_reserved_tag_turns_the_checkbox_on_and_drops_it_from_the_box()
    {
        var vm = new ProfileEditorViewModel(null) { Name = "n", Host = "h", TagsText = "favorite, PROD" };
        Assert.True(vm.IsFavorite);
        Assert.Equal("PROD", vm.TagsText);
        Assert.Equal(["FAVORITE", "PROD"], vm.TryBuild()!.Tags.Names);
    }

    [Fact]
    public void Editor_refuses_an_over_long_tag_name()
    {
        var vm = new ProfileEditorViewModel(null) { Name = "n", Host = "h", TagsText = new string('x', 17) };
        Assert.Null(vm.TryBuild());
        Assert.Contains("16", vm.ValidationMessage);
    }

    /// <summary>Counted before TagSet.From runs. Afterwards From has already discarded the surplus, so the
    /// check could never fire and nine tags would silently become eight.</summary>
    [Fact]
    public void Editor_refuses_more_tags_than_the_cap_including_the_reserved_one()
    {
        var eight = string.Join(",", Enumerable.Range(0, TagSet.MaxTags).Select(i => $"T{i}"));
        var vm = new ProfileEditorViewModel(null) { Name = "n", Host = "h", TagsText = eight };
        Assert.NotNull(vm.TryBuild());

        vm.IsFavorite = true;
        Assert.Null(vm.TryBuild());
        Assert.Contains($"{TagSet.MaxTags}", vm.ValidationMessage);
    }

    [Fact]
    public void A_blank_note_becomes_null_and_a_typed_one_is_trimmed()
    {
        var vm = new ProfileEditorViewModel(null) { Name = "n", Host = "h", Note = "   " };
        Assert.Null(vm.TryBuild()!.Note);
        vm.Note = "  LAN only  ";
        Assert.Equal("LAN only", vm.TryBuild()!.Note);
    }
```

And append to `tests/LizTerm.App.Tests/Views/ProfileEditorWindowTests.cs`:

```csharp
    [AvaloniaFact]
    public void The_tag_and_note_rows_render_the_profiles_values()
    {
        var window = new ProfileEditorWindow(new SessionProfile
        {
            Name = "mvsce", Host = "h", Tags = TagSet.From(["FAVORITE", "PROD"]), Note = "no live data",
        });
        window.Show();

        Assert.Equal("PROD", window.FindControl<TextBox>("TagsBox")!.Text);
        Assert.True(window.FindControl<CheckBox>("FavoriteBox")!.IsChecked);
        Assert.Equal("no live data", window.FindControl<TextBox>("NoteBox")!.Text);
    }

    /// <summary>A pasted paragraph must not reach the list, where it would reshape every row.</summary>
    [AvaloniaFact]
    public void The_note_box_is_single_line_and_capped()
    {
        var window = new ProfileEditorWindow(new SessionProfile { Name = "p", Host = "h" });
        window.Show();
        var note = window.FindControl<TextBox>("NoteBox")!;
        Assert.False(note.AcceptsReturn);
        Assert.Equal(120, note.MaxLength);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~ProfileViewModelsTests"`

Expected: FAIL to compile — `error CS0117: 'ProfileEditorViewModel' does not contain a definition for 'IsFavorite'`.

- [ ] **Step 3: Add the view-model state**

In `src/LizTerm.App/ViewModels/ProfileEditorViewModel.cs`, add `using LizTerm.Core.Profiles;` and, beside the other `[ObservableProperty]` fields:

```csharp
    /// <summary>The tag names as the user edits them, comma-separated, WITHOUT the reserved tag —
    /// <see cref="IsFavorite"/> owns that one.</summary>
    [ObservableProperty] private string _tagsText = "";

    /// <summary>Whether the profile carries <c>TagRegistry.FavoriteName</c>. A checkbox rather than a typed tag
    /// so the one name with a fixed meaning cannot be misspelled into an ordinary tag.</summary>
    [ObservableProperty] private bool _isFavorite;

    [ObservableProperty] private string _note = "";
```

Add the reserved-name interception after the other `partial void On…Changed` handlers:

```csharp
    /// <summary>Typing the reserved tag into the box turns the checkbox on and drops it from the text, rather
    /// than raising a validation message: the checkbox visibly moving explains what happened, and there is
    /// nothing for the user to go and fix. Re-entrant by construction — the assignment below re-enters this
    /// handler, whose Split then finds no reserved name and leaves the text alone.</summary>
    partial void OnTagsTextChanged(string value)
    {
        var names = TagSet.Split(value);
        if (!names.Any(TagRegistry.IsReserved)) return;
        IsFavorite = true;
        TagsText = string.Join(", ", names.Where(name => !TagRegistry.IsReserved(name)));
    }
```

In the constructor's seeding block, after `_oversize = existing.Oversize ?? "";`:

```csharp
        _isFavorite = existing.Tags.Contains(TagRegistry.FavoriteName);
        _tagsText = string.Join(", ", existing.Tags.Names.Where(name => !TagRegistry.IsReserved(name)));
        _note = existing.Note ?? "";
```

- [ ] **Step 4: Validate and build**

In `TryBuild`, after the oversize check and before `SetValidation(null)`:

```csharp
        // Counted from what the user typed, BEFORE TagSet.From runs: From enforces the caps by discarding what
        // does not fit, so a check afterwards could never fire and a ninth tag would vanish in silence.
        var typed = TagSet.Split(TagsText);
        if (typed.FirstOrDefault(name => name.Length > TagSet.MaxNameLength) is not null)
        {
            SetValidation($"Tag names can be at most {TagSet.MaxNameLength} characters.");
            return null;
        }
        // Explicitly typed, not var: a collection expression in a conditional needs a target type.
        IReadOnlyList<string> wanted = IsFavorite ? [TagRegistry.FavoriteName, .. typed] : typed;
        if (wanted.Distinct(StringComparer.OrdinalIgnoreCase).Count() > TagSet.MaxTags)
        {
            SetValidation($"A profile can carry at most {TagSet.MaxTags} tags.");
            return null;
        }
```

Then in the returned `SessionProfile` initialiser, after `Oversize = oversize?.ToString(),`:

```csharp
            Tags = TagSet.From(wanted),
            Note = string.IsNullOrWhiteSpace(Note) ? null : Note.Trim(),
```

- [ ] **Step 5: Add the two XAML rows**

In `src/LizTerm.App/Views/ProfileEditorWindow.axaml`, extend the grid's `RowDefinitions` by two (twelve `Auto` in total), then add after the Keyboard row:

```xml
      <TextBlock Grid.Row="10" Text="Tags" VerticalAlignment="Center" />
      <StackPanel Grid.Row="10" Grid.Column="1" Spacing="4">
        <!-- The checkbox owns FAVORITE rather than the box below: it is the one tag name with a fixed meaning,
             and a typo would otherwise turn it into an ordinary tag that merely looks the same. The label names
             the tag instead of spelling "favourite" in prose, which also keeps the reserved name visible. -->
        <CheckBox x:Name="FavoriteBox" Content="Mark as FAVORITE" IsChecked="{Binding IsFavorite}" />
        <TextBox x:Name="TagsBox" Text="{Binding TagsText}" PlaceholderText="PROD, MVS" />
        <TextBlock Text="Separate with commas; shown as uppercase chips in the session list."
                   Foreground="#A0A0A0" FontSize="12" TextWrapping="Wrap" />
      </StackPanel>
      <TextBlock Grid.Row="11" Text="Note" VerticalAlignment="Center" />
      <!-- AcceptsReturn false and a cap, because this line is drawn in the session list: a pasted paragraph
           would make every row tall and the visible list short. -->
      <TextBox x:Name="NoteBox" Grid.Row="11" Grid.Column="1" Text="{Binding Note}" AcceptsReturn="False"
               MaxLength="120" PlaceholderText="optional — shown under the host in the list" />
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~ProfileViewModelsTests"`
Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~ProfileEditorWindowTests"`

Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/LizTerm.App/ViewModels/ProfileEditorViewModel.cs src/LizTerm.App/Views/ProfileEditorWindow.axaml \
        tests/LizTerm.App.Tests/ViewModels/ProfileViewModelsTests.cs \
        tests/LizTerm.App.Tests/Views/ProfileEditorWindowTests.cs
git commit -m "Edit a profile's tags and note"
```

---

### Task 7: `ProfileRow`, `ScopeOption`, and the picker view model

**Files:**
- Create: `src/LizTerm.App/ViewModels/ProfileRow.cs`, `src/LizTerm.App/ViewModels/ScopeOption.cs`
- Modify: `src/LizTerm.App/ViewModels/ProfilePickerViewModel.cs`, `src/LizTerm.App/Views/ProfilePickerWindow.axaml.cs`, `src/LizTerm.App/App.axaml.cs:190`
- Test: `tests/LizTerm.App.Tests/ViewModels/ProfileRowTests.cs`, `tests/LizTerm.App.Tests/ViewModels/ProfileViewModelsTests.cs`, `tests/LizTerm.App.Tests/Views/ProfilePickerWindowTests.cs:40` (one assignment — see Step 7)

**Interfaces:**
- Consumes: `TagSet`, `TagRegistry`, `TagRegistryStore`, `TagPalette`.
- Produces:
  - `TagChip(string Text, IBrush Background)` — `sealed record`.
  - `ProfileRow(SessionProfile profile, TagRegistry registry)` — `sealed class`. `Profile`, `Name`, `HostPort`, `Note`, `HasNote`, `IsFavorite`, `Chips` (`IReadOnlyList<TagChip>`), `HasOverflow`, `OverflowText`, `OverflowTip`, `const int MaxChips = 3`.
  - `ScopeOption(string Label, string? TagName)` — `sealed record`; `TagName` null means every profile.
  - `ProfilePickerViewModel`: new last constructor parameter `TagRegistryStore? tags = null`; `FilterText`, `SelectedScope`, `Scopes`, `VisibleRows`, `SelectedRow`; `SelectedProfile` becomes a derived read-only property.
  - `ProfilePickerWindow(ProfileStore, Action<SessionProfile, bool>, Action, TagRegistryStore? tags = null)`.

- [ ] **Step 1: Write the failing `ProfileRow` test**

Create `tests/LizTerm.App.Tests/ViewModels/ProfileRowTests.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.App.Rendering;
using LizTerm.App.ViewModels;
using LizTerm.Core.Profiles;
using LizTerm.Core.Session;

namespace LizTerm.App.Tests.ViewModels;

public class ProfileRowTests
{
    private static TagRegistry Registry(params string[] names) => TagRegistry.Empty.Register(names).Registry;

    [Fact]
    public void A_row_shows_the_name_the_host_and_the_note()
    {
        var row = new ProfileRow(
            new SessionProfile { Name = "mvsce", Host = "10.42.37.209", Port = 3270, Note = "no live data" },
            TagRegistry.Empty);

        Assert.Equal("mvsce", row.Name);
        Assert.Equal("10.42.37.209:3270", row.HostPort);
        Assert.Equal("no live data", row.Note);
        Assert.True(row.HasNote);
    }

    [Fact]
    public void A_profile_with_no_note_has_nothing_to_draw_on_the_third_line()
    {
        var row = new ProfileRow(new SessionProfile { Name = "tk5", Host = "h" }, TagRegistry.Empty);
        Assert.False(row.HasNote);
        Assert.False(row.IsFavorite);
        Assert.Empty(row.Chips);
        Assert.False(row.HasOverflow);
    }

    /// <summary>FAVORITE is the star, so it must never also be a chip.</summary>
    [Fact]
    public void The_reserved_tag_becomes_the_star_and_not_a_chip()
    {
        var row = new ProfileRow(
            new SessionProfile { Name = "n", Host = "h", Tags = TagSet.From(["FAVORITE", "PROD"]) },
            Registry("PROD"));

        Assert.True(row.IsFavorite);
        Assert.Equal(["PROD"], row.Chips.Select(c => c.Text));
    }

    [Fact]
    public void Chip_text_is_uppercase_whatever_the_stored_casing()
    {
        var row = new ProfileRow(
            new SessionProfile { Name = "n", Host = "h", Tags = TagSet.From(["prod", "mvs"]) },
            Registry("prod", "mvs"));
        Assert.Equal(["PROD", "MVS"], row.Chips.Select(c => c.Text));
    }

    [Fact]
    public void A_chip_takes_its_colour_from_the_registry()
    {
        var registry = Registry("PROD");
        var row = new ProfileRow(new SessionProfile { Name = "n", Host = "h", Tags = TagSet.From(["PROD"]) }, registry);
        Assert.Same(TagPalette.Brush(registry.ColorOf("PROD")), row.Chips.Single().Background);
    }

    /// <summary>The list is only 356px wide, so past three chips the rest collapse into a +n whose tooltip
    /// names them — Robert's own suggestion for the overflow.</summary>
    [Fact]
    public void More_than_three_tags_collapse_into_an_overflow_chip_with_a_tooltip()
    {
        var names = new[] { "DEV", "PROD", "MVS", "TLS", "LAB" };
        var row = new ProfileRow(
            new SessionProfile { Name = "n", Host = "h", Tags = TagSet.From(["FAVORITE", .. names]) },
            Registry(names));

        Assert.Equal(ProfileRow.MaxChips, row.Chips.Count);
        Assert.Equal(["DEV", "PROD", "MVS"], row.Chips.Select(c => c.Text));
        Assert.True(row.HasOverflow);
        Assert.Equal("+2", row.OverflowText);
        Assert.Equal("TLS, LAB", row.OverflowTip);
    }

    [Fact]
    public void Exactly_three_tags_need_no_overflow()
    {
        var row = new ProfileRow(
            new SessionProfile { Name = "n", Host = "h", Tags = TagSet.From(["A", "B", "C"]) },
            Registry("A", "B", "C"));
        Assert.Equal(3, row.Chips.Count);
        Assert.False(row.HasOverflow);
        Assert.Null(row.OverflowText);
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~ProfileRowTests"`

Expected: FAIL to compile — `error CS0246: ... 'ProfileRow' could not be found`.

- [ ] **Step 3: Write `ProfileRow` and `ScopeOption`**

Create `src/LizTerm.App/ViewModels/ProfileRow.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Media;
using LizTerm.App.Rendering;
using LizTerm.Core.Profiles;
using LizTerm.Core.Session;

namespace LizTerm.App.ViewModels;

/// <summary>One chip, ready to draw.</summary>
public sealed record TagChip(string Text, IBrush Background);

/// <summary>One row of the session list. The ListBox binds these rather than <see cref="SessionProfile"/>
/// itself, because a chip needs a COLOUR and a profile does not know its tags' colours — only the registry
/// does. Resolving that inside a DataTemplate would mean a multi-binding against the registry or static mutable
/// state; resolving it here is one plain class with plain properties, which is also what makes the rendering
/// rules testable without a window.
///
/// Immutable and rebuilt on every reload: a row is a snapshot of one profile against one registry, exactly as
/// the screen is a snapshot of the buffer.</summary>
public sealed class ProfileRow
{
    /// <summary>Chips drawn before the rest collapse into <see cref="OverflowText"/>. Three fits the 356px the
    /// list has once the button column takes its share, and Robert expects two or three in practice.</summary>
    public const int MaxChips = 3;

    public ProfileRow(SessionProfile profile, TagRegistry registry)
    {
        Profile = profile;
        Name = profile.Name;
        HostPort = $"{profile.Host}:{profile.Port}";
        Note = string.IsNullOrWhiteSpace(profile.Note) ? null : profile.Note.Trim();
        IsFavorite = profile.Tags.Contains(TagRegistry.FavoriteName);

        // The reserved tag is the star, never a chip.
        var names = profile.Tags.Names.Where(name => !TagRegistry.IsReserved(name)).ToList();
        Chips = [.. names.Take(MaxChips)
                         .Select(name => new TagChip(name.ToUpperInvariant(), TagPalette.Brush(registry.ColorOf(name))))];

        var hidden = names.Skip(MaxChips).ToList();
        OverflowText = hidden.Count > 0 ? $"+{hidden.Count}" : null;
        OverflowTip = hidden.Count > 0 ? string.Join(", ", hidden.Select(name => name.ToUpperInvariant())) : null;
    }

    /// <summary>The store's own instance, unchanged. Connect, Edit and Delete act on this.</summary>
    public SessionProfile Profile { get; }

    public string Name { get; }

    public string HostPort { get; }

    /// <summary>Null when there is nothing to show, so the third line collapses and an un-noted profile stays
    /// two lines tall.</summary>
    public string? Note { get; }

    public bool HasNote => Note is not null;

    public bool IsFavorite { get; }

    public IReadOnlyList<TagChip> Chips { get; }

    public string? OverflowText { get; }

    public string? OverflowTip { get; }

    public bool HasOverflow => OverflowText is not null;
}
```

Create `src/LizTerm.App/ViewModels/ScopeOption.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

namespace LizTerm.App.ViewModels;

/// <summary>One entry in the session list's scope drop-down. A record rather than a bare string so the filter
/// reads the tag it was given instead of parsing a '#' back off a display label.</summary>
/// <param name="Label">What the drop-down shows: "All sessions", "FAVORITE", or "#PROD".</param>
/// <param name="TagName">The tag this narrows to, or null for every profile.</param>
public sealed record ScopeOption(string Label, string? TagName)
{
    public override string ToString() => Label;
}
```

- [ ] **Step 4: Run the `ProfileRow` test to verify it passes**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~ProfileRowTests"`

Expected: PASS, 7 tests.

- [ ] **Step 5: Write the failing picker view-model tests**

Append to `tests/LizTerm.App.Tests/ViewModels/ProfileViewModelsTests.cs`:

```csharp
    private ProfilePickerViewModel Picker(TagRegistryStore? tags = null) =>
        new(_store, (_, _) => { }, _ => Task.FromResult<ProfileEdit?>(null), () => { }, tags);

    [Fact]
    public void Picker_shows_every_profile_until_something_narrows_it()
    {
        _store.Save(new SessionProfile { Name = "alpha", Host = "h" });
        _store.Save(new SessionProfile { Name = "zeta", Host = "h" });

        var vm = Picker();
        Assert.Equal(["alpha", "zeta"], vm.VisibleRows.Select(r => r.Name));
        Assert.Null(vm.SelectedScope.TagName);
        Assert.Equal("All sessions", vm.SelectedScope.Label);
    }

    /// <summary>Scope narrows first, then the text box filters what is left — Robert's ordering.</summary>
    [Fact]
    public void The_scope_narrows_before_the_filter_text_does()
    {
        _store.Save(new SessionProfile { Name = "mvsce", Host = "h", Tags = TagSet.From(["PROD", "MVS"]) });
        _store.Save(new SessionProfile { Name = "mvs-dev", Host = "h", Tags = TagSet.From(["DEV"]) });
        _store.Save(new SessionProfile { Name = "gateway", Host = "h", Tags = TagSet.From(["PROD"]) });

        var vm = Picker();
        vm.SelectedScope = vm.Scopes.Single(s => s.TagName == "PROD");
        Assert.Equal(["gateway", "mvsce"], vm.VisibleRows.Select(r => r.Name));

        vm.FilterText = "mvs";
        Assert.Equal(["mvsce"], vm.VisibleRows.Select(r => r.Name));
    }

    [Fact]
    public void The_filter_text_matches_a_name_or_a_tag_but_not_a_host()
    {
        _store.Save(new SessionProfile { Name = "alpha", Host = "prod.example" });
        _store.Save(new SessionProfile { Name = "beta", Host = "h", Tags = TagSet.From(["PROD"]) });

        var vm = Picker();
        vm.FilterText = "prod";
        Assert.Equal(["beta"], vm.VisibleRows.Select(r => r.Name));

        vm.FilterText = "ALP";
        Assert.Equal(["alpha"], vm.VisibleRows.Select(r => r.Name));
    }

    [Fact]
    public void The_favorite_scope_narrows_to_the_starred_profiles()
    {
        _store.Save(new SessionProfile { Name = "starred", Host = "h", Tags = TagSet.From(["FAVORITE"]) });
        _store.Save(new SessionProfile { Name = "plain", Host = "h" });

        var vm = Picker();
        vm.SelectedScope = vm.Scopes.Single(s => s.TagName == TagRegistry.FavoriteName);
        Assert.Equal(["starred"], vm.VisibleRows.Select(r => r.Name));
    }

    /// <summary>What makes the filter box need no Enter handler: Connect is the window's default button, so as
    /// long as the selection is always a visible row, typing and pressing Enter connects what you are looking
    /// at rather than something the filter has hidden.</summary>
    [Fact]
    public void Filtering_moves_a_hidden_selection_to_the_first_visible_row()
    {
        _store.Save(new SessionProfile { Name = "alpha", Host = "h" });
        _store.Save(new SessionProfile { Name = "zeta", Host = "h" });

        var vm = Picker();
        vm.SelectedRow = vm.VisibleRows.Single(r => r.Name == "zeta");

        vm.FilterText = "alpha";
        Assert.Equal("alpha", vm.SelectedRow!.Name);
        Assert.Equal("alpha", vm.SelectedProfile!.Name);

        vm.FilterText = "nothing matches";
        Assert.Empty(vm.VisibleRows);
        Assert.Null(vm.SelectedRow);
        Assert.False(vm.ConnectCommand.CanExecute(null));
    }

    /// <summary>Quick Connect resolves against every saved profile, not the filtered view: a name the filter
    /// has hidden must still connect by name, as it does from the command line.</summary>
    [Fact]
    public void Quick_connect_still_finds_a_profile_the_filter_has_hidden()
    {
        _store.Save(new SessionProfile { Name = "tk5", Host = "tk5.local", Port = 3270 });
        SessionProfile? opened = null;
        var vm = new ProfilePickerViewModel(_store, (p, _) => opened = p, _ => Task.FromResult<ProfileEdit?>(null), () => { });

        vm.FilterText = "zzz";
        Assert.Empty(vm.VisibleRows);

        vm.QuickConnectText = "tk5";
        vm.QuickConnectCommand.Execute(null);
        Assert.Equal("tk5.local", opened?.Host);
    }

    [Fact]
    public void The_scopes_list_is_all_sessions_then_favorite_then_the_tags_alphabetically()
    {
        _store.Save(new SessionProfile { Name = "a", Host = "h", Tags = TagSet.From(["zeta", "MVS"]) });

        var vm = Picker();
        Assert.Equal(["All sessions", "FAVORITE", "#MVS", "#zeta"], vm.Scopes.Select(s => s.Label));
    }

    /// <summary>Reconciliation: a tag name seen on a profile but absent from the registry registers itself, so
    /// a profile copied from another machine gets colours locally rather than rendering colourless.</summary>
    [Fact]
    public void An_unknown_tag_registers_itself_and_the_registry_is_written_once()
    {
        _store.Save(new SessionProfile { Name = "a", Host = "h", Tags = TagSet.From(["PROD"]) });
        var tagFile = Path.Combine(_dir, "tags.json");
        var tags = new TagRegistryStore(tagFile);

        var vm = Picker(tags);
        Assert.Equal(["PROD"], tags.Load().Stored.Select(d => d.Name));

        // Nothing new to learn, so no second write. Asserted by deleting the file and checking that a reload
        // does not recreate it, rather than by comparing timestamps — two writes inside one filesystem tick
        // would compare equal and the test would pass while the bug was present.
        File.Delete(tagFile);
        vm.Reload();
        Assert.False(File.Exists(tagFile), "Reload rewrote tags.json with nothing new to register");
    }
```

Add `using LizTerm.Core.Profiles;` — already present — and `using LizTerm.Core.Session;` — already present.

- [ ] **Step 6: Run them to verify they fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~ProfileViewModelsTests"`

Expected: FAIL to compile — `error CS0117: 'ProfilePickerViewModel' does not contain a definition for 'VisibleRows'`.

- [ ] **Step 7: Change the picker view model**

In `src/LizTerm.App/ViewModels/ProfilePickerViewModel.cs`:

Replace the `SelectedProfile` observable property with a `SelectedRow` one plus a derived profile:

```csharp
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand), nameof(EditCommand), nameof(DeleteCommand))]
    private ProfileRow? _selectedRow;

    /// <summary>The selected profile, which is the row's. Derived rather than stored so the list's selection
    /// and the commands' subject can never disagree.</summary>
    public SessionProfile? SelectedProfile => SelectedRow?.Profile;
```

Add the new state and the registry beside `Profiles`:

```csharp
    private readonly TagRegistryStore? _tags;
    private TagRegistry _registry = TagRegistry.Empty;

    /// <summary>Every saved profile, in ProfileStore's own order. Quick Connect resolves against THIS rather
    /// than the filtered view, and depends on its instances' identity for pin write-back — see QuickConnect.</summary>
    public ObservableCollection<SessionProfile> Profiles { get; } = [];

    /// <summary>What the list draws: Profiles narrowed by the scope and then by the filter text, projected
    /// against the registry so each row carries resolved chips (see ProfileRow).</summary>
    public ObservableCollection<ProfileRow> VisibleRows { get; } = [];

    public ObservableCollection<ScopeOption> Scopes { get; } = [];

    /// <summary>Every profile, whatever its tags. Held as a field so the drop-down's rebuilt list can select
    /// the same option object back.</summary>
    public static readonly ScopeOption AllSessions = new("All sessions", null);

    [ObservableProperty] private ScopeOption _selectedScope = AllSessions;

    [ObservableProperty] private string _filterText = "";

    partial void OnSelectedScopeChanged(ScopeOption value) => Refilter();

    partial void OnFilterTextChanged(string value) => Refilter();
```

Add the registry store as the constructor's last, optional parameter — optional so the existing tests and call sites keep compiling, the same shape `SessionViewModel` uses for `SettingsViewModel`:

```csharp
    /// <param name="tags">The tag registry's file, or null for an in-memory registry that is never written,
    /// which is what a test wants.</param>
    public ProfilePickerViewModel(ProfileStore store, Action<SessionProfile, bool> openSession,
        Func<SessionProfile?, Task<ProfileEdit?>> editProfile, Action quit, TagRegistryStore? tags = null)
    {
        _store = store;
        _openSession = openSession;
        _editProfile = editProfile;
        _quit = quit;
        _tags = tags;
        _registry = tags?.Load() ?? TagRegistry.Empty;
        Reload();
    }
```

Replace `Reload` and add the two helpers:

```csharp
    public void Reload()
    {
        var selectedName = SelectedProfile?.Name;
        Profiles.Clear();
        foreach (var profile in _store.LoadAll()) Profiles.Add(profile);

        Reconcile();
        // RebuildScopes may assign SelectedScope, whose handler calls Refilter() on its own; the explicit call
        // below then runs a second time with the remembered name. Harmless and deliberate — do not "fix" it by
        // dropping either call, because the scope may legitimately change here and the selection must survive.
        RebuildScopes();
        Refilter(selectedName);
    }

    /// <summary>Registers any tag name the profiles carry that the registry does not know, and writes the file
    /// only when something was added. Here rather than in ProfileStore.LoadAll on purpose: a load must not
    /// write, and Core's store stays a pure reader. It is also what makes a profile copied from another machine
    /// gain colours locally — the names travel in the profile, the colours are assigned on first sight.</summary>
    private void Reconcile()
    {
        var (registry, changed) = _registry.Register(Profiles.SelectMany(p => p.Tags.Names));
        _registry = registry;
        if (changed) _tags?.Save(registry);
    }

    private void RebuildScopes()
    {
        var wanted = _registry.All
            .Where(definition => Profiles.Any(p => p.Tags.Contains(definition.Name)) || TagRegistry.IsReserved(definition.Name))
            .Select(definition => TagRegistry.IsReserved(definition.Name)
                ? new ScopeOption(definition.Name, definition.Name)
                : new ScopeOption($"#{definition.Name}", definition.Name))
            .ToList();

        Scopes.Clear();
        Scopes.Add(AllSessions);
        foreach (var scope in wanted) Scopes.Add(scope);

        // A scope whose tag no longer exists anywhere would filter to nothing with no way back.
        if (Scopes.FirstOrDefault(s => s.TagName == SelectedScope.TagName) is { } still) SelectedScope = still;
        else SelectedScope = AllSessions;
    }

    /// <summary>Scope first, then the typed text, then a selection that is still on screen.</summary>
    private void Refilter(string? preferName = null)
    {
        var preferred = preferName ?? SelectedProfile?.Name;
        var text = FilterText.Trim();

        VisibleRows.Clear();
        foreach (var profile in Profiles.Where(InScope).Where(p => Matches(p, text)))
        {
            VisibleRows.Add(new ProfileRow(profile, _registry));
        }

        SelectedRow = VisibleRows.FirstOrDefault(r => r.Name == preferred) ?? VisibleRows.FirstOrDefault();
    }

    private bool InScope(SessionProfile profile) =>
        SelectedScope.TagName is not { } tag || profile.Tags.Contains(tag);

    /// <summary>Name or any tag name, ignoring case. Not the host: Quick Connect is where a host is typed.</summary>
    private static bool Matches(SessionProfile profile, string text) =>
        text.Length == 0
        || profile.Name.Contains(text, StringComparison.OrdinalIgnoreCase)
        || profile.Tags.Names.Any(name => name.Contains(text, StringComparison.OrdinalIgnoreCase));
```

Then replace each remaining `SelectedProfile = …` assignment:

- in `NewAsync`: `Reload(); SelectedRow = VisibleRows.FirstOrDefault(r => r.Name == edit.Profile.Name) ?? SelectedRow;`
- in `EditAsync`: `Reload(); SelectedRow = VisibleRows.FirstOrDefault(r => r.Name == merged.Name) ?? SelectedRow;`
- in `Delete`: `_store.Delete(SelectedProfile!.Name); SelectedRow = null; Reload();`

`HasSelection` becomes `public bool HasSelection => SelectedRow is not null;`.

**Then fix the six existing assignments that this breaks.** `SelectedProfile` is now get-only, so every
`vm.SelectedProfile = …` is a `CS0200` compile error. Replace each with the equivalent row assignment — no
filter is active in any of these, so `VisibleRows` and `Profiles` are in the same order:

- `tests/LizTerm.App.Tests/ViewModels/ProfileViewModelsTests.cs:80` — `vm.SelectedProfile = vm.Profiles[1];` → `vm.SelectedRow = vm.VisibleRows[1];`
- `tests/LizTerm.App.Tests/ViewModels/ProfileViewModelsTests.cs:97` — `vm.SelectedProfile = vm.Profiles[0];` → `vm.SelectedRow = vm.VisibleRows[0];`
- `tests/LizTerm.App.Tests/ViewModels/ProfileViewModelsTests.cs:131, 146, 165` — `picker.SelectedProfile = picker.Profiles.Single();` → `picker.SelectedRow = picker.VisibleRows.Single();`
- `tests/LizTerm.App.Tests/Views/ProfilePickerWindowTests.cs:40` — `vm.SelectedProfile = vm.Profiles.Single();` → `vm.SelectedRow = vm.VisibleRows.Single();`

Reads of `SelectedProfile` (for example `Assert.Equal("c", vm.SelectedProfile!.Name)`) stay exactly as they
are — the derived property still answers them.

- [ ] **Step 8: Thread the store through the window and the app**

In `src/LizTerm.App/Views/ProfilePickerWindow.axaml.cs`, add `TagRegistryStore? tags = null` as the last constructor parameter and pass it last to the view model.

In `src/LizTerm.App/App.axaml.cs:190`, pass the real store:

```csharp
        _picker = new ProfilePickerWindow(_store ??= new ProfileStore(AppPaths.ProfilesDirectory()),
            (profile, fromStore) => OpenSession(profile, fromStore), Quit,
            _tags ??= new TagRegistryStore(TagRegistryStore.DefaultFile()));
```

Declare `private TagRegistryStore? _tags;` beside `_store`.

- [ ] **Step 9: Run the tests to verify they pass**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~ProfileViewModelsTests"`
Run: `dotnet test tests/LizTerm.App.Tests`

Expected: PASS, including `ProfilePickerWindowTests` — whose one `SelectedProfile` assignment Step 7 above
rewrites. Its two existing cases otherwise drive Quick Connect and layout, neither of which changed.

- [ ] **Step 10: Commit**

```bash
git add src/LizTerm.App/ViewModels/ProfileRow.cs src/LizTerm.App/ViewModels/ScopeOption.cs \
        src/LizTerm.App/ViewModels/ProfilePickerViewModel.cs src/LizTerm.App/Views/ProfilePickerWindow.axaml.cs \
        src/LizTerm.App/App.axaml.cs tests/LizTerm.App.Tests/ViewModels/ProfileRowTests.cs \
        tests/LizTerm.App.Tests/ViewModels/ProfileViewModelsTests.cs \
        tests/LizTerm.App.Tests/Views/ProfilePickerWindowTests.cs
git commit -m "Filter the session list by scope and text"
```

---

### Task 8: The session list's chrome

**Files:**
- Modify: `src/LizTerm.App/Views/ProfilePickerWindow.axaml`
- Test: `tests/LizTerm.App.Tests/Views/ProfilePickerWindowTests.cs`

**Interfaces:**
- Consumes: everything from Task 7. Named controls: `FilterBox`, `ScopeBox`.
- Produces: no new API.

- [ ] **Step 1: Write the failing tests**

Append to `tests/LizTerm.App.Tests/Views/ProfilePickerWindowTests.cs`:

```csharp
    /// <summary>Walks the realised row rather than the view model, so the template's own bindings are what is
    /// asserted: a correct ProfileRow bound to a broken DataTemplate renders nothing and passes every view
    /// model test.</summary>
    private static IEnumerable<T> Descendants<T>(Control root) where T : Control
    {
        foreach (var child in root.GetVisualDescendants().OfType<T>()) yield return child;
    }

    [AvaloniaFact]
    public void A_tagged_profile_draws_a_star_uppercase_chips_and_a_note()
    {
        var store = new ProfileStore(_dir);
        store.Save(new SessionProfile
        {
            Name = "mvsce", Host = "10.42.37.209", Port = 3270,
            Tags = TagSet.From(["FAVORITE", "prod", "mvs"]), Note = "no live data",
        });

        var window = new ProfilePickerWindow(store, (_, _) => { }, () => { });
        window.Show();
        window.UpdateLayout();

        var list = Descendants<ListBox>(window).Single();
        var item = Descendants<ListBoxItem>(list).Single();
        var texts = Descendants<TextBlock>(item).Select(t => t.Text).ToList();

        Assert.Contains("mvsce", texts);
        Assert.Contains("10.42.37.209:3270", texts);
        Assert.Contains("no live data", texts);
        Assert.Contains("PROD", texts);
        Assert.Contains("MVS", texts);
        Assert.DoesNotContain("FAVORITE", texts);
        Assert.Contains(Descendants<TextBlock>(item), t => t.Name == "StarGlyph" && t.IsVisible);
    }

    [AvaloniaFact]
    public void An_untagged_un_noted_profile_draws_neither_a_star_nor_a_third_line()
    {
        var store = new ProfileStore(_dir);
        store.Save(new SessionProfile { Name = "tk5", Host = "tk5.local", Port = 3270 });

        var window = new ProfilePickerWindow(store, (_, _) => { }, () => { });
        window.Show();
        window.UpdateLayout();

        var item = Descendants<ListBoxItem>(window).Single();
        Assert.DoesNotContain(Descendants<TextBlock>(item), t => t.Name == "StarGlyph" && t.IsVisible);
        Assert.DoesNotContain(Descendants<TextBlock>(item), t => t.Name == "NoteLine" && t.IsVisible);
    }

    [AvaloniaFact]
    public void A_fourth_tag_collapses_into_an_overflow_chip_carrying_the_rest_in_its_tooltip()
    {
        var store = new ProfileStore(_dir);
        store.Save(new SessionProfile
        {
            Name = "vm-dev", Host = "vm.example.org", Port = 992,
            Tags = TagSet.From(["DEV", "PROD", "MVS", "TLS", "LAB"]),
        });

        var window = new ProfilePickerWindow(store, (_, _) => { }, () => { });
        window.Show();
        window.UpdateLayout();

        var item = Descendants<ListBoxItem>(window).Single();
        var overflow = Descendants<TextBlock>(item).Single(t => t.Name == "OverflowChip");
        Assert.True(overflow.IsVisible);
        Assert.Equal("+2", overflow.Text);
        Assert.Equal("TLS, LAB", ToolTip.GetTip(overflow));
    }

    /// <summary>The list is 356px of a 520px window, and the row has to survive the 400px MinWidth too — the
    /// same measurement the quick connect row already carries, for the same reason.</summary>
    [AvaloniaFact]
    public void A_three_chip_row_fits_at_the_windows_minimum_width()
    {
        var store = new ProfileStore(_dir);
        store.Save(new SessionProfile
        {
            Name = "a-rather-long-profile-name", Host = "10.42.37.209", Port = 3270,
            Tags = TagSet.From(["FAVORITE", "PROD", "MVS", "TEST"]),
        });

        var window = new ProfilePickerWindow(store, (_, _) => { }, () => { });
        window.Width = window.MinWidth;
        window.Show();
        window.UpdateLayout();

        var item = Descendants<ListBoxItem>(window).Single();
        foreach (var chip in Descendants<Border>(item).Where(b => b.Name == "Chip"))
        {
            Assert.True(chip.Bounds.Width > 0, "a chip collapsed to nothing");
            Assert.True(chip.Bounds.Right <= item.Bounds.Width + 0.5,
                $"a chip ends at {chip.Bounds.Right} in a {item.Bounds.Width} row");
        }
    }

    [AvaloniaFact]
    public void The_filter_row_narrows_the_list()
    {
        var store = new ProfileStore(_dir);
        store.Save(new SessionProfile { Name = "alpha", Host = "h" });
        store.Save(new SessionProfile { Name = "zeta", Host = "h" });

        var window = new ProfilePickerWindow(store, (_, _) => { }, () => { });
        window.Show();
        window.UpdateLayout();

        var box = window.FindControl<TextBox>("FilterBox")!;
        box.Text = "zet";
        window.UpdateLayout();

        Assert.Equal(["zeta"], Descendants<ListBoxItem>(window).Select(i => ((ProfileRow)i.DataContext!).Name));
        Assert.NotNull(window.FindControl<ComboBox>("ScopeBox"));
    }
```

Add exactly these usings to the file: `using Avalonia.VisualTree;` (for `GetVisualDescendants`) and
`using LizTerm.App.ViewModels;` (for `ProfileRow`). `ToolTip`, `Border`, `ComboBox` and `ListBoxItem` all come
from `Avalonia.Controls`, which the file already imports.

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~ProfilePickerWindowTests"`

Expected: FAIL — the new cases cannot find `FilterBox`, and the item template renders no chips.

- [ ] **Step 3: Add the filter row and the item template**

In `src/LizTerm.App/Views/ProfilePickerWindow.axaml`: raise the window's size, add the filter row below the Quick Connect block, and replace the `ListBox`.

Window attributes become `Width="520" Height="480" MinWidth="400" MinHeight="360"`, with a comment:

```xml
        <!-- 480 rather than 400: the Quick Connect row, the filter row and three-line rows together left about
             four profiles visible at 400. -->
```

After the Quick Connect `StackPanel`, add:

```xml
    <!-- A second text box a few pixels from Quick Connect's, so the two are told apart by placeholder and by
         position: this one sits directly on the list it narrows. It deliberately has NO KeyDown handler, unlike
         Quick Connect — Refilter always leaves the selection on a visible row, so the window's default Connect
         button already does the right thing when you narrow to one profile and press Enter. -->
    <DockPanel DockPanel.Dock="Top" Margin="0,0,0,8" LastChildFill="True">
      <ComboBox x:Name="ScopeBox" DockPanel.Dock="Right" Width="130" Margin="8,0,0,0"
                ItemsSource="{Binding Scopes}" SelectedItem="{Binding SelectedScope}" />
      <TextBox x:Name="FilterBox" Text="{Binding FilterText}" PlaceholderText="Filter sessions" />
    </DockPanel>
```

Replace the `ListBox` with:

```xml
    <ListBox ItemsSource="{Binding VisibleRows}" SelectedItem="{Binding SelectedRow}" DoubleTapped="OnDoubleTapped">
      <ListBox.ItemTemplate>
        <DataTemplate x:DataType="vm:ProfileRow">
          <!-- A 15px gutter on all three lines, so every star lands on the same x and a column of them can be
               scanned. Right-aligned among the chips it would slide with the chip count, which is most of what
               a favourite marker is for. -->
          <Grid ColumnDefinitions="15,*" RowDefinitions="Auto,Auto,Auto">
            <TextBlock x:Name="StarGlyph" Grid.Row="0" Grid.Column="0" Text="★" Foreground="{x:Static render:TagPalette.Star}"
                       FontSize="13" VerticalAlignment="Center" IsVisible="{Binding IsFavorite}" />
            <!-- The name is what ellipsises; the chips never shrink. A truncated PRO… carries nothing, where a
                 truncated long name is still recognisable. -->
            <DockPanel Grid.Row="0" Grid.Column="1" LastChildFill="True">
              <StackPanel DockPanel.Dock="Right" Orientation="Horizontal" Spacing="3" VerticalAlignment="Center">
                <ItemsControl ItemsSource="{Binding Chips}">
                  <ItemsControl.ItemsPanel>
                    <ItemsPanelTemplate>
                      <StackPanel Orientation="Horizontal" Spacing="3" />
                    </ItemsPanelTemplate>
                  </ItemsControl.ItemsPanel>
                  <ItemsControl.ItemTemplate>
                    <DataTemplate x:DataType="vm:TagChip">
                      <Border x:Name="Chip" Background="{Binding Background}" CornerRadius="3" Padding="5,1">
                        <TextBlock Text="{Binding Text}" Foreground="{x:Static render:TagPalette.ChipText}"
                                   FontSize="10" FontWeight="Bold" />
                      </Border>
                    </DataTemplate>
                  </ItemsControl.ItemTemplate>
                </ItemsControl>
                <Border Background="{x:Static render:TagPalette.Overflow}" CornerRadius="3" Padding="5,1"
                        IsVisible="{Binding HasOverflow}">
                  <TextBlock x:Name="OverflowChip" Text="{Binding OverflowText}" FontSize="10" FontWeight="Bold"
                             Foreground="{x:Static render:TagPalette.ChipText}" ToolTip.Tip="{Binding OverflowTip}" />
                </Border>
              </StackPanel>
              <TextBlock Text="{Binding Name}" FontWeight="SemiBold" TextTrimming="CharacterEllipsis"
                         VerticalAlignment="Center" Margin="0,0,6,0" />
            </DockPanel>
            <TextBlock Grid.Row="1" Grid.Column="1" Text="{Binding HostPort}" Foreground="#A0A0A0" FontSize="12" />
            <!-- Only when there is one, so an un-noted profile stays two lines and the list stays long. -->
            <TextBlock x:Name="NoteLine" Grid.Row="2" Grid.Column="1" Text="{Binding Note}" Foreground="#8F8F8F"
                       FontSize="12" FontStyle="Italic" TextTrimming="CharacterEllipsis"
                       IsVisible="{Binding HasNote}" ToolTip.Tip="{Binding Note}" />
          </Grid>
        </DataTemplate>
      </ListBox.ItemTemplate>
    </ListBox>
```

Add `xmlns:render="using:LizTerm.App.Rendering"` to the `Window` element, and drop the now-unused `xmlns:core` if nothing else uses it.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~ProfilePickerWindowTests"`

Expected: PASS, 7 tests.

- [ ] **Step 5: Run the whole suite and the warning check**

Run: `dotnet test LizTerm.slnx`
Run: `dotnet build LizTerm.slnx --no-incremental 2>&1 | grep -c " warning "`

Expected: all tests pass; the warning count prints `0`.

- [ ] **Step 6: Commit**

```bash
git add src/LizTerm.App/Views/ProfilePickerWindow.axaml tests/LizTerm.App.Tests/Views/ProfilePickerWindowTests.cs
git commit -m "Draw tags, notes and the filter row in the session list"
```

---

### Task 9: The user guide

**Files:**
- Modify: `docs/user-guide.md`, `src/LizTerm.App/Assets/Docs/user-guide.html` (generated)

- [ ] **Step 1: Run the guard to see it pass before the edit**

Run: `dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~UserGuideAssetTests"`

Expected: PASS. This is the baseline; the edit below breaks it until the HTML is regenerated.

- [ ] **Step 2: Add the three settings rows**

In `docs/user-guide.md`, in the **Profile settings** table, after the `Backspace erases` row:

```markdown
| Mark as FAVORITE | Adds the reserved `FAVORITE` tag, shown as a gold star in the list. |
| Tags | Short labels, separated by commas (`PROD, MVS`), drawn as uppercase colour chips. A tag's colour is picked automatically the first time you use it, and is the same everywhere that tag appears. At most 8 per profile, 16 characters each. |
| Note | One short line — "no live data", "LAN only" — shown under the host in the list. |
```

- [ ] **Step 3: Describe the filter row**

In `docs/user-guide.md`, in **Sessions and profiles**, after the opening paragraph:

```markdown
Above the list, the **Filter** box narrows it to profiles whose name or tag contains what you type, and the
drop-down beside it narrows to **FAVORITE** or to one tag first. The two work together: the drop-down chooses
the scope, the box searches inside it. Whatever the filter shows, **Quick Connect** still reaches every saved
profile by name.
```

- [ ] **Step 4: Regenerate the bundled HTML**

Run: `LIZTERM_UPDATE_DOCS=1 dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~UserGuideAssetTests"`

Expected: PASS, with `src/LizTerm.App/Assets/Docs/user-guide.html` rewritten. If `The_guide_uses_no_construct_the_converter_cannot_render` fails, extend `tests/LizTerm.Core.Tests/Documentation/UserGuideHtml.cs` rather than simplifying the guide.

- [ ] **Step 5: Verify the whole suite and the warning count**

Run: `dotnet test LizTerm.slnx`
Run: `dotnet build LizTerm.slnx --no-incremental 2>&1 | grep -c " warning "`

Expected: all tests pass; the warning count prints `0`.

- [ ] **Step 6: Commit**

```bash
git add docs/user-guide.md src/LizTerm.App/Assets/Docs/user-guide.html
git commit -m "Document tags, notes and the session filter"
```

---

## Manual verification

Automated tests cover the rules; these check the thing Robert actually looked at. A fresh worktree has no
`native/out`, so set `LIZTERM_B3270_PATH` to a Homebrew `b3270` first.

- [ ] Run `dotnet run --project src/LizTerm.App` against an isolated `HOME` so real profiles are untouched.
- [ ] Create a profile, tick **Mark as FAVORITE**, type `PROD, MVS`, add a note. Confirm the row shows a gold star in the gutter, `PROD` and `MVS` right-aligned and uppercase, and the note as a trimmed third line.
- [ ] Add a second profile tagged `PROD` only. Confirm both chips are the **same colour**, and that `tags.json` in the config root holds one entry per tag with a colour name.
- [ ] Narrow the drop-down to `#PROD`, then type in the filter box. Confirm the scope applies first, that the selection stays on a visible row, and that pressing Enter connects the highlighted profile rather than something hidden.
- [ ] Give a profile five tags and confirm the `+2` chip's tooltip names the two it hides.
- [ ] Drag the window to its minimum width and confirm no chip is clipped.

## Follow-up issues to open

From spec §9, once this lands:

- **Manage Tags** — rename, recolour and delete a tag across every profile file, plus the drop-down entry and the `---` separator that fences it off.
- **Toggle FAVORITE from the list** — a click on the gutter star or a context menu, instead of Edit → checkbox → Save. Needs a decision about competing with row selection and double-tap-to-connect.
