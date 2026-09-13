# Manage Tags Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A Tags... button in the session picker opens a Manage Tags window where a tag can be renamed (including merged into another), recoloured or deleted across every profile that carries it.

**Architecture:** Pure, immutable additions to `TagSet` (`Rename`) and `TagRegistry` (`Contains`, `Recolour`, `Rename`, `Remove`) do the arithmetic. A Core service, `TagMaintenance`, reads profiles and `tags.json` fresh for every action, writes profiles first and `tags.json` last, stops at the first failed write and returns a `TagChangeResult` saying exactly what happened. The App's `ManageTagsViewModel` shows a list plus a selected-tag panel (layout B), asks before merges and deletes, and turns results into messages; the picker gains the button and re-reads `tags.json` on every `Reload()`.

**Tech Stack:** .NET 10, C# with `Nullable`/`ImplicitUsings` solution-wide, Avalonia 12 + FluentTheme (dark), CommunityToolkit.Mvvm (`[ObservableProperty]`, `[RelayCommand]`), xunit.v3 in VSTest mode, Avalonia headless tests (`[AvaloniaFact]`).

**Spec:** `docs/superpowers/specs/2026-09-12-lizterm-manage-tags-design.md`

## Global Constraints

- **Licence headers.** Every new `.cs` and `.axaml` file under `src/` and `tests/` starts with three lines in its comment syntax — `This file is part of LizTerm.`, `Copyright 2026 by CoffeeMuse`, `SPDX-License-Identifier: BSD-3-Clause`; in `.axaml` inside a `<!-- -->` comment before the root element. `RepositoryHeadersTests` fails the suite for a missing one.
- **Dependency rule.** `LizTerm.Core` depends only on the BCL. `TagMaintenance`, `TagChangeResult` and `TagSnapshot` mention no Avalonia type.
- **`ProfileStore.cs` is not modified** (spec 2.6).
- **Zero warnings.** CI builds with `-warnaserror`. Before calling anything done: `dotnet build LizTerm.slnx --no-incremental 2>&1 | grep -c " warning "` prints `0`.
- **No new packages.**
- **Reserved tag:** exactly `FAVORITE`; never renamed, recoloured or deleted; `TagColor.Gold` is never chosen for another tag.
- **Caps:** `TagSet.MaxNameLength = 16`. Assignable colours are `TagRegistry.AssignableColors` (seven: Red, Amber, Green, Blue, Purple, Teal, Grey).
- **User-visible text is exact** — copy the strings in each task verbatim; tests assert them.
- **Failure types caught** by `TagMaintenance`: `IOException` and `UnauthorizedAccessException`, nothing broader.
- **Forcing a write to fail in tests:** create a *directory* at the file's `<file>.tmp` path. Both stores write a sibling `.tmp` first, and a directory there makes that write throw `UnauthorizedAccessException` (verified on macOS, .NET 10, 2026-09-12). No mocks.
- **Test commands.** Whole suite: `dotnet test LizTerm.slnx`. One class: `dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~TagMaintenanceTests"`. One test: append `.Test_method_name` to the `~` pattern.
- **Branch:** `claude/issue-88-manage-tags`, which already contains #89's row menu (PR #90). Commit after every task.

## Deviations from the spec, decided while planning

- **D1. `SwatchOption` gets its own file** (`src/LizTerm.App/ViewModels/SwatchOption.cs`), beside `ScopeOption.cs`. Spec 10 did not list it; the swatch row needs an item type carrying colour, brush and whether it is selected.
- **D2. Two-line messages use `\n`.** Spec 6.2 writes ` / ` between the two lines of one message; the strings hold a newline and the `TextBlock` wraps.
- **D3. `StatusMessage` sits under the panel, not inside it**, so a failure stays visible when the selection moves or clears — spec 6.1 already says selection changes must not clear it.
- **D4. After a delete, the selection follows the tag if it is still listed.** Spec 6.1 says a delete clears the selection; that is what happens when the delete succeeds. When it fails the tag is still there, and the panel that shows its Used by list is the useful thing to leave on screen.
- **D5. The picker's constructor stops loading `tags.json` itself.** Its `Reload()` now does, and the constructor already calls `Reload()`.
- **D6. The swatch keeps its colour under the pointer** through a window style on `ContentPresenter#PART_ContentPresenter` bound to the swatch's brush — Fluent's `Button` theme otherwise swaps the background on `:pointerover` and `:pressed`.

---

## File Structure

**New — Core**

| File | Responsibility |
|---|---|
| `src/LizTerm.Core/Profiles/TagChangeResult.cs` | What one action did: carriers, profiles written, the failure |
| `src/LizTerm.Core/Profiles/TagMaintenance.cs` | `TagSnapshot`, `RenameProblem`, `Load`, and the three actions |

**New — App**

| File | Responsibility |
|---|---|
| `src/LizTerm.App/ViewModels/TagListRow.cs` | One row of the tag list: definition, chip, users, count text |
| `src/LizTerm.App/ViewModels/SwatchOption.cs` | One colour swatch: colour, brush, selected |
| `src/LizTerm.App/ViewModels/ManageTagsViewModel.cs` | List, selection, validation, confirmations, actions, messages |
| `src/LizTerm.App/Views/ManageTagsWindow.axaml` + `.axaml.cs` | Layout B |

**New — tests**

| File | Covers |
|---|---|
| `tests/LizTerm.Core.Tests/Profiles/TagMaintenanceTests.cs` | Tasks 3 and 4 |
| `tests/LizTerm.App.Tests/ViewModels/ManageTagsViewModelTests.cs` | Tasks 6 and 7 |
| `tests/LizTerm.App.Tests/Views/ManageTagsWindowTests.cs` | Task 8 |

**Changed**

| File | Task |
|---|---|
| `src/LizTerm.Core/Session/TagSet.cs`, `tests/LizTerm.Core.Tests/Session/TagSetTests.cs` | 1 |
| `src/LizTerm.Core/Profiles/TagRegistry.cs`, `tests/LizTerm.Core.Tests/Profiles/TagRegistryTests.cs` | 2 |
| `src/LizTerm.Core/CLAUDE.md` | 4 |
| `src/LizTerm.App/ViewModels/ProfilePickerViewModel.cs`, `tests/LizTerm.App.Tests/ViewModels/ProfileViewModelsTests.cs`, `src/LizTerm.App/CLAUDE.md` | 5, 9 |
| `src/LizTerm.App/Views/ProfilePickerWindow.axaml` + `.axaml.cs`, `tests/LizTerm.App.Tests/Views/ProfilePickerWindowTests.cs` | 9 |
| `docs/user-guide.md`, `src/LizTerm.App/Assets/Docs/user-guide.html` | 10 |

---

### Task 1: `TagSet.Rename`

**Files:**
- Modify: `src/LizTerm.Core/Session/TagSet.cs` (add one method after `Without`)
- Test: `tests/LizTerm.Core.Tests/Session/TagSetTests.cs` (append to the class)

**Interfaces:**
- Consumes: `TagSet.From`, `TagSet.Normalize`, `TagSet.Contains`, `TagSet.Names` (all existing).
- Produces: `public TagSet Rename(string from, string to)` — used by Task 3.

- [ ] **Step 1: Write the failing tests**

Append inside `public class TagSetTests`:

```csharp
    [Fact]
    public void Rename_replaces_a_name_in_place()
    {
        var renamed = TagSet.From(["DEV", "MVS", "LAB"]).Rename("mvs", "TEST");
        Assert.Equal(["DEV", "TEST", "LAB"], renamed.Names);
    }

    /// <summary>A merge: the set already carried the name being renamed to. One copy survives, at the earlier of
    /// the two positions.</summary>
    [Fact]
    public void Rename_onto_a_name_already_carried_keeps_one_copy_at_the_first_position()
    {
        Assert.Equal(["PROD", "MVS"], TagSet.From(["PRDO", "MVS", "PROD"]).Rename("PRDO", "PROD").Names);
        Assert.Equal(["PROD"], TagSet.From(["PROD", "PRDO"]).Rename("PRDO", "PROD").Names);
    }

    [Fact]
    public void Rename_of_a_name_the_set_does_not_carry_changes_nothing()
    {
        Assert.Equal(["MVS"], TagSet.From(["MVS"]).Rename("PRDO", "PROD").Names);
        TagSet none = default;
        Assert.True(none.Rename("A", "B").IsEmpty);
    }

    /// <summary>Equality ignores case, so a case-only rename is "equal" to the set it came from and only Names
    /// shows the change. TagMaintenance compares Names ordinally for exactly this reason (spec 4.3).</summary>
    [Fact]
    public void A_case_only_rename_changes_the_stored_casing_while_equality_still_holds()
    {
        var before = TagSet.From(["dev", "MVS"]);
        var after = before.Rename("dev", "DEV");
        Assert.Equal(["DEV", "MVS"], after.Names);
        Assert.Equal(before, after);
    }
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~TagSetTests"`
Expected: build error `CS1061: 'TagSet' does not contain a definition for 'Rename'`.

- [ ] **Step 3: Implement**

In `src/LizTerm.Core/Session/TagSet.cs`, directly after the `Without` method:

```csharp
    /// <summary>This set with <paramref name="from"/> replaced by <paramref name="to"/> in place, then normalised as
    /// <see cref="From"/> normalises — so when the set already carries <paramref name="to"/>, the later copy goes
    /// and the first position wins, which is what merging one tag into another needs. Unchanged when the set does
    /// not carry <paramref name="from"/>. Never adds a name, so it can never pass <see cref="MaxTags"/>.
    ///
    /// <paramref name="to"/> must already be a valid name: From drops an over-long one, which would delete the tag
    /// rather than rename it. TagMaintenance validates before calling this.</summary>
    public TagSet Rename(string from, string to)
    {
        if (!Contains(from)) return this;
        var old = Normalize(from);
        return From(Names.Select(name => name.Equals(old, StringComparison.OrdinalIgnoreCase) ? to : name));
    }
```

- [ ] **Step 4: Run them to see them pass**

Run: `dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~TagSetTests"`
Expected: all `TagSetTests` pass.

- [ ] **Step 5: Commit**

```bash
git add src/LizTerm.Core/Session/TagSet.cs tests/LizTerm.Core.Tests/Session/TagSetTests.cs
git commit -m "Add TagSet.Rename for renaming and merging a tag in place"
```

---

### Task 2: `TagRegistry.Contains`, `Recolour`, `Rename`, `Remove`

**Files:**
- Modify: `src/LizTerm.Core/Profiles/TagRegistry.cs` (add methods after `ColorOf`)
- Test: `tests/LizTerm.Core.Tests/Profiles/TagRegistryTests.cs` (append to the class)

**Interfaces:**
- Consumes: the existing `TagRegistry(IEnumerable<TagDefinition>)` constructor, `Stored`, `IsReserved`, `FavoriteName`, `FavoriteColor`, `TagSet.Normalize`.
- Produces, each returning a new registry except `Contains`:
  - `public bool Contains(string name)`
  - `public TagRegistry Recolour(string name, TagColor color)`
  - `public TagRegistry Rename(string from, string to)`
  - `public TagRegistry Remove(string name)`

- [ ] **Step 1: Write the failing tests**

Append inside `public class TagRegistryTests`:

```csharp
    private static TagRegistry Defined(params (string Name, TagColor Color)[] definitions) =>
        new(definitions.Select(d => new TagDefinition(d.Name, d.Color)));

    [Fact]
    public void Contains_ignores_case_and_a_hash_and_always_knows_the_reserved_tag()
    {
        var registry = Defined(("PROD", TagColor.Red));
        Assert.True(registry.Contains("prod"));
        Assert.True(registry.Contains("#PROD"));
        Assert.False(registry.Contains("MVS"));
        Assert.True(TagRegistry.Empty.Contains("FAVORITE"));
    }

    [Fact]
    public void Recolour_changes_one_definition_and_leaves_the_rest()
    {
        var registry = Defined(("PROD", TagColor.Red), ("MVS", TagColor.Blue)).Recolour("prod", TagColor.Green);
        Assert.Equal(TagColor.Green, registry.ColorOf("PROD"));
        Assert.Equal(TagColor.Blue, registry.ColorOf("MVS"));
    }

    [Fact]
    public void Recolour_Rename_and_Remove_of_an_unknown_tag_return_the_same_registry()
    {
        var registry = Defined(("PROD", TagColor.Red));
        Assert.Same(registry, registry.Recolour("MVS", TagColor.Green));
        Assert.Same(registry, registry.Rename("DEV", "TEST"));
        Assert.Same(registry, registry.Remove("LAB"));
    }

    [Fact]
    public void Recolour_refuses_the_reserved_tag_the_reserved_colour_and_an_undefined_one()
    {
        var registry = Defined(("PROD", TagColor.Red));
        Assert.Throws<ArgumentException>(() => registry.Recolour("FAVORITE", TagColor.Red));
        Assert.Throws<ArgumentException>(() => registry.Recolour("PROD", TagColor.Gold));
        Assert.Throws<ArgumentException>(() => registry.Recolour("PROD", (TagColor)99));
    }

    [Fact]
    public void A_plain_rename_carries_the_colour_to_the_new_name()
    {
        var registry = Defined(("DEV", TagColor.Teal), ("MVS", TagColor.Blue)).Rename("DEV", "TEST");
        Assert.Equal(["MVS", "TEST"], registry.Stored.Select(d => d.Name));
        Assert.Equal(TagColor.Teal, registry.ColorOf("TEST"));
    }

    [Fact]
    public void A_case_only_rename_keeps_the_colour_and_takes_the_new_casing()
    {
        var registry = Defined(("dev", TagColor.Teal)).Rename("dev", "DEV");
        Assert.Equal(["DEV"], registry.Stored.Select(d => d.Name));
        Assert.Equal(TagColor.Teal, registry.ColorOf("DEV"));
    }

    [Fact]
    public void Renaming_onto_another_definition_merges_and_keeps_the_targets_colour()
    {
        var registry = Defined(("PRDO", TagColor.Purple), ("PROD", TagColor.Amber)).Rename("PRDO", "PROD");
        Assert.Equal(["PROD"], registry.Stored.Select(d => d.Name));
        Assert.Equal(TagColor.Amber, registry.ColorOf("PROD"));
    }

    [Fact]
    public void Rename_and_Remove_refuse_the_reserved_tag()
    {
        var registry = Defined(("PROD", TagColor.Red));
        Assert.Throws<ArgumentException>(() => registry.Rename("FAVORITE", "STAR"));
        Assert.Throws<ArgumentException>(() => registry.Rename("PROD", "favorite"));
        Assert.Throws<ArgumentException>(() => registry.Remove("FAVORITE"));
    }

    [Fact]
    public void Remove_drops_one_definition()
    {
        var registry = Defined(("PROD", TagColor.Red), ("MVS", TagColor.Blue));
        Assert.Equal(["MVS"], registry.Remove("prod").Stored.Select(d => d.Name));
    }
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~TagRegistryTests"`
Expected: build errors `CS1061` for `Contains`, `Recolour`, `Rename` and `Remove`.

- [ ] **Step 3: Implement**

In `src/LizTerm.Core/Profiles/TagRegistry.cs`, directly after `ColorOf`:

```csharp
    /// <summary>Whether a definition of that name exists, ignoring case and a leading '#'. Always true for the
    /// reserved tag, which is always present.</summary>
    public bool Contains(string name) => IsReserved(name) || _byName.ContainsKey(TagSet.Normalize(name));

    /// <summary>This registry with one tag's colour changed; an unknown name changes nothing. The reserved tag, the
    /// reserved colour and a value outside the enum throw — Manage Tags never offers them, so reaching one is a
    /// bug rather than a user error.</summary>
    public TagRegistry Recolour(string name, TagColor color)
    {
        ThrowIfReserved(name, nameof(name));
        if (color == FavoriteColor || !Enum.IsDefined(color))
            throw new ArgumentException($"{color} cannot be chosen for a tag.", nameof(color));
        var key = TagSet.Normalize(name);
        if (!_byName.ContainsKey(key)) return this;
        return new TagRegistry(Stored.Select(d => Same(d.Name, key) ? d with { Color = color } : d));
    }

    /// <summary>This registry with <paramref name="from"/> renamed, in one of three ways decided by what
    /// <paramref name="to"/> is: the same tag in a new casing keeps its colour and takes the new casing; another
    /// definition is a merge, so <paramref name="from"/> goes and <paramref name="to"/> keeps its own colour; any
    /// other name takes <paramref name="from"/>'s colour. An unknown <paramref name="from"/> changes nothing.
    /// <paramref name="to"/> must be valid (TagMaintenance.RenameProblem): the constructor drops an over-long name,
    /// which would lose the definition.</summary>
    public TagRegistry Rename(string from, string to)
    {
        ThrowIfReserved(from, nameof(from));
        ThrowIfReserved(to, nameof(to));
        if (!_byName.TryGetValue(TagSet.Normalize(from), out var source)) return this;
        var target = TagSet.Normalize(to);
        var others = Stored.Where(d => !Same(d.Name, source.Name)).ToList();
        // Same() first: the dictionary ignores case, so a case-only rename would otherwise look like a merge.
        return Same(target, source.Name) || !_byName.ContainsKey(target)
            ? new TagRegistry([.. others, new TagDefinition(target, source.Color)])
            : new TagRegistry(others);
    }

    /// <summary>This registry without that definition; an unknown name changes nothing.</summary>
    public TagRegistry Remove(string name)
    {
        ThrowIfReserved(name, nameof(name));
        var key = TagSet.Normalize(name);
        return _byName.ContainsKey(key) ? new TagRegistry(Stored.Where(d => !Same(d.Name, key))) : this;
    }

    private static bool Same(string a, string b) => a.Equals(b, StringComparison.OrdinalIgnoreCase);

    private static void ThrowIfReserved(string name, string parameter)
    {
        if (IsReserved(name)) throw new ArgumentException($"{FavoriteName} is reserved and cannot be changed.", parameter);
    }
```

- [ ] **Step 4: Run them to see them pass**

Run: `dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~TagRegistryTests"`
Expected: all `TagRegistryTests` pass.

- [ ] **Step 5: Commit**

```bash
git add src/LizTerm.Core/Profiles/TagRegistry.cs tests/LizTerm.Core.Tests/Profiles/TagRegistryTests.cs
git commit -m "Add Contains, Recolour, Rename and Remove to TagRegistry"
```

---

### Task 3: `TagMaintenance` — validation, `Load`, and the three actions

The failure handling of the actions is Task 4. Here a failed profile or registry write simply throws.

**Files:**
- Create: `src/LizTerm.Core/Profiles/TagChangeResult.cs`
- Create: `src/LizTerm.Core/Profiles/TagMaintenance.cs`
- Test: `tests/LizTerm.Core.Tests/Profiles/TagMaintenanceTests.cs`

**Interfaces:**
- Consumes: `TagSet.Rename` (Task 1); `TagRegistry.Contains`, `Recolour`, `Rename`, `Remove` (Task 2); existing `ProfileStore.LoadAll`, `ProfileStore.Save`, `ProfileStore.FileNameFor`, `TagRegistryStore.Load`, `TagRegistryStore.Save`, `TagRegistry.Register`.
- Produces (used by Tasks 4, 6, 7, 8, 9):
  - `public sealed record TagSnapshot(TagRegistry Registry, IReadOnlyList<SessionProfile> Profiles)`
  - `public sealed record TagChangeResult(int Carriers, IReadOnlyList<string> Changed, string? FailedProfile, string? Error)` with `static TagChangeResult Nothing` and `bool Succeeded`
  - `public sealed class TagMaintenance(ProfileStore profiles, TagRegistryStore tags)` with
    `static string? RenameProblem(string name)`, `TagSnapshot Load()`,
    `TagChangeResult Recolour(string name, TagColor color)`, `TagChangeResult Rename(string from, string to)`,
    `TagChangeResult Delete(string name)`

- [ ] **Step 1: Write the failing tests**

Create `tests/LizTerm.Core.Tests/Profiles/TagMaintenanceTests.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.Core.Profiles;
using LizTerm.Core.Session;

namespace LizTerm.Core.Tests.Profiles;

public class TagMaintenanceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lizterm-maintenance-" + Guid.NewGuid().ToString("N"));
    private readonly ProfileStore _profiles;
    private readonly TagRegistryStore _tags;
    private readonly TagMaintenance _maintenance;

    public TagMaintenanceTests()
    {
        _profiles = new ProfileStore(Path.Combine(_dir, "profiles"));
        _tags = new TagRegistryStore(Path.Combine(_dir, "tags.json"));
        _maintenance = new TagMaintenance(_profiles, _tags);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private string TagsFile => Path.Combine(_dir, "tags.json");

    private string ProfileFile(string name) => Path.Combine(_dir, "profiles", ProfileStore.FileNameFor(name));

    /// <summary>Makes every later save of one file fail, without a mock: ProfileStore and TagRegistryStore both
    /// write a sibling "&lt;file&gt;.tmp" first, and a directory squatting on that path makes the write throw
    /// UnauthorizedAccessException. A test that blocks a file it expects to be left alone turns "was it touched?"
    /// into a failure you cannot miss.</summary>
    private static void Block(string file) => Directory.CreateDirectory(file + ".tmp");

    private void Save(string name, params string[] tags) =>
        _profiles.Save(new SessionProfile { Name = name, Host = "h", Tags = TagSet.From(tags) });

    private void Define(params (string Name, TagColor Color)[] definitions) =>
        _tags.Save(new TagRegistry(definitions.Select(d => new TagDefinition(d.Name, d.Color))));

    private IReadOnlyList<string> TagsOf(string profile) => _profiles.Load(profile)!.Tags.Names;

    [Theory]
    [InlineData("", "A tag name can't be blank.")]
    [InlineData("  # ", "A tag name can't be blank.")]
    [InlineData("ABCDEFGHIJKLMNOPQ", "Tag names can be at most 16 characters.")]
    [InlineData("PROD, MVS", "A tag name can't contain a comma.")]
    [InlineData("favorite", "FAVORITE is reserved.")]
    public void RenameProblem_names_the_rule_a_target_breaks(string name, string message) =>
        Assert.Equal(message, TagMaintenance.RenameProblem(name));

    [Theory]
    [InlineData("PROD")]
    [InlineData("#PROD")]
    [InlineData("ABCDEFGHIJKLMNOP")]
    public void RenameProblem_accepts_a_valid_name(string name) => Assert.Null(TagMaintenance.RenameProblem(name));

    [Fact]
    public void Load_registers_a_tag_only_a_profile_knows_and_saves_the_registry()
    {
        Save("a", "PROD");

        var snapshot = _maintenance.Load();

        Assert.True(snapshot.Registry.Contains("PROD"));
        Assert.Equal(["a"], snapshot.Profiles.Select(p => p.Name));
        Assert.True(_tags.Load().Contains("PROD"));
    }

    /// <summary>Asserted through formatting: the store writes indented JSON, so a rewrite of this hand-written
    /// one-line file would change its text.</summary>
    [Fact]
    public void Load_does_not_rewrite_the_registry_when_every_tag_is_known()
    {
        Save("a", "PROD");
        const string handWritten = """{"tags":[{"name":"PROD","color":"Red"}]}""";
        Directory.CreateDirectory(_dir);
        File.WriteAllText(TagsFile, handWritten);

        _maintenance.Load();

        Assert.Equal(handWritten, File.ReadAllText(TagsFile));
    }

    [Fact]
    public void Load_survives_a_registry_it_cannot_save()
    {
        Save("a", "PROD");
        Block(TagsFile);

        var snapshot = _maintenance.Load();

        Assert.True(snapshot.Registry.Contains("PROD"));
        Assert.False(File.Exists(TagsFile));
    }

    [Fact]
    public void Rename_rewrites_every_carrier_in_place_and_leaves_their_other_fields_and_other_profiles_alone()
    {
        var pin = new CertificatePin("8C:13:6A:01", "CN = localhost", "-----BEGIN CERTIFICATE-----\nZmFrZQ==\n-----END CERTIFICATE-----\n");
        _profiles.Save(new SessionProfile
        {
            Name = "gateway", Host = "gw", UseTls = true, PinnedCertificate = pin, Note = "no live data",
            Tags = TagSet.From(["TLS", "DEV"]),
        });
        Save("tk5", "DEV");
        Save("mvsce", "MVS");
        Define(("DEV", TagColor.Teal), ("MVS", TagColor.Blue), ("TLS", TagColor.Green));
        Block(ProfileFile("mvsce"));

        var result = _maintenance.Rename("dev", "TEST");

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Carriers);
        Assert.Equal(["gateway", "tk5"], result.Changed);
        Assert.Equal(["TLS", "TEST"], TagsOf("gateway"));
        Assert.Equal(["TEST"], TagsOf("tk5"));
        var gateway = _profiles.Load("gateway")!;
        Assert.Equal("no live data", gateway.Note);
        Assert.Equal(pin, gateway.PinnedCertificate);
        var registry = _tags.Load();
        Assert.False(registry.Contains("DEV"));
        Assert.Equal(TagColor.Teal, registry.ColorOf("TEST"));
    }

    [Fact]
    public void Renaming_onto_an_existing_tag_merges_and_keeps_its_colour()
    {
        Save("mvsce", "PRDO", "MVS", "PROD");
        Save("gateway", "PROD");
        Define(("MVS", TagColor.Blue), ("PRDO", TagColor.Purple), ("PROD", TagColor.Amber));
        Block(ProfileFile("gateway"));

        var result = _maintenance.Rename("PRDO", "PROD");

        Assert.True(result.Succeeded);
        Assert.Equal(["mvsce"], result.Changed);
        Assert.Equal(["PROD", "MVS"], TagsOf("mvsce"));
        var registry = _tags.Load();
        Assert.False(registry.Contains("PRDO"));
        Assert.Equal(TagColor.Amber, registry.ColorOf("PROD"));
    }

    /// <summary>Spec 4.3's regression: a "did it change?" check built on TagSet.Equals, which ignores case, would
    /// skip this write and the rename would silently do nothing.</summary>
    [Fact]
    public void A_case_only_rename_really_writes_the_new_casing()
    {
        Save("a", "dev");
        Define(("dev", TagColor.Teal));

        var result = _maintenance.Rename("dev", "DEV");

        Assert.Equal(["a"], result.Changed);
        Assert.Equal(["DEV"], TagsOf("a"));
        Assert.Equal(["DEV"], _tags.Load().Stored.Select(d => d.Name));
        Assert.Equal(TagColor.Teal, _tags.Load().ColorOf("DEV"));
    }

    [Fact]
    public void Renaming_to_the_identical_name_or_renaming_an_unknown_tag_writes_nothing()
    {
        Save("a", "DEV");
        Define(("DEV", TagColor.Teal));
        Block(ProfileFile("a"));
        Block(TagsFile);

        foreach (var result in new[] { _maintenance.Rename("DEV", "#DEV"), _maintenance.Rename("LAB", "TEST") })
        {
            Assert.True(result.Succeeded);
            Assert.Equal(0, result.Carriers);
            Assert.Empty(result.Changed);
        }
    }

    [Fact]
    public void Delete_strips_the_tag_from_every_carrier_and_drops_its_definition()
    {
        Save("gateway", "PROD", "TLS");
        Save("mvsce", "FAVORITE", "PROD");
        Save("tk5", "MVS");
        Define(("MVS", TagColor.Blue), ("PROD", TagColor.Amber), ("TLS", TagColor.Green));
        Block(ProfileFile("tk5"));

        var result = _maintenance.Delete("prod");

        Assert.True(result.Succeeded);
        Assert.Equal(["gateway", "mvsce"], result.Changed);
        Assert.Equal(["TLS"], TagsOf("gateway"));
        Assert.Equal(["FAVORITE"], TagsOf("mvsce"));
        Assert.Equal(["MVS", "TLS"], _tags.Load().Stored.Select(d => d.Name));
    }

    [Fact]
    public void Deleting_an_unused_tag_only_touches_the_registry()
    {
        Save("a", "MVS");
        Define(("LAB", TagColor.Teal), ("MVS", TagColor.Blue));
        Block(ProfileFile("a"));

        var result = _maintenance.Delete("LAB");

        Assert.True(result.Succeeded);
        Assert.Equal(0, result.Carriers);
        Assert.False(_tags.Load().Contains("LAB"));
    }

    [Fact]
    public void Recolour_writes_the_registry_and_no_profile()
    {
        Save("a", "MVS");
        Define(("MVS", TagColor.Blue));
        Block(ProfileFile("a"));

        var result = _maintenance.Recolour("mvs", TagColor.Green);

        Assert.True(result.Succeeded);
        Assert.Equal(TagColor.Green, _tags.Load().ColorOf("MVS"));
    }

    [Fact]
    public void Each_action_reads_the_profiles_as_they_are_on_disk_now()
    {
        Save("a", "DEV");
        Define(("DEV", TagColor.Teal));
        _ = _maintenance.Load();
        Save("b", "DEV");

        var result = _maintenance.Rename("DEV", "TEST");

        Assert.Equal(["a", "b"], result.Changed);
    }

    [Fact]
    public void The_reserved_tag_an_invalid_target_and_the_reserved_colour_throw_and_change_nothing()
    {
        Save("a", "FAVORITE", "DEV");
        Define(("DEV", TagColor.Teal));

        Assert.Throws<ArgumentException>(() => _maintenance.Rename("FAVORITE", "STAR"));
        Assert.Throws<ArgumentException>(() => _maintenance.Rename("DEV", "favorite"));
        Assert.Throws<ArgumentException>(() => _maintenance.Rename("DEV", "A, B"));
        Assert.Throws<ArgumentException>(() => _maintenance.Delete("FAVORITE"));
        Assert.Throws<ArgumentException>(() => _maintenance.Recolour("FAVORITE", TagColor.Red));
        Assert.Throws<ArgumentException>(() => _maintenance.Recolour("DEV", TagColor.Gold));

        Assert.Equal(["FAVORITE", "DEV"], TagsOf("a"));
        Assert.Equal(TagColor.Teal, _tags.Load().ColorOf("DEV"));
    }
}
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~TagMaintenanceTests"`
Expected: build errors — `TagMaintenance` does not exist.

- [ ] **Step 3: Create the result record**

Create `src/LizTerm.Core/Profiles/TagChangeResult.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

namespace LizTerm.Core.Profiles;

/// <summary>What one Manage Tags action did — enough to say it exactly: "Renamed on 3 of 5 profiles. Could not
/// write gateway: ...". Never compared whole; <see cref="Changed"/> is a list.</summary>
/// <param name="Carriers">Profiles that carried the tag when the action started.</param>
/// <param name="Changed">Profile names written, in the order they were written.</param>
/// <param name="FailedProfile">The profile whose write failed, or null.</param>
/// <param name="Error">The failure's message, or null on success. Non-null with a null
/// <paramref name="FailedProfile"/> means every profile was written and tags.json was not.</param>
public sealed record TagChangeResult(int Carriers, IReadOnlyList<string> Changed, string? FailedProfile, string? Error)
{
    /// <summary>An action that had nothing to do and wrote nothing.</summary>
    public static TagChangeResult Nothing { get; } = new(0, [], null, null);

    public bool Succeeded => Error is null;
}
```

- [ ] **Step 4: Create the service**

Create `src/LizTerm.Core/Profiles/TagMaintenance.cs`:

```csharp
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
        if (!ReferenceEquals(recoloured, registry)) tags.Save(recoloured);
        return TagChangeResult.Nothing;
    }

    public TagChangeResult Rename(string from, string to)
    {
        Guard(from, nameof(from));
        if (RenameProblem(to) is { } problem) throw new ArgumentException(problem, nameof(to));
        var target = TagSet.Normalize(to);
        if (TagSet.Normalize(from).Equals(target, StringComparison.Ordinal)) return TagChangeResult.Nothing;

        var snapshot = Load();
        if (!snapshot.Registry.Contains(from)) return TagChangeResult.Nothing;
        return Apply(snapshot, from, set => set.Rename(from, target), snapshot.Registry.Rename(from, target));
    }

    public TagChangeResult Delete(string name)
    {
        Guard(name, nameof(name));
        var snapshot = Load();
        if (!snapshot.Registry.Contains(name)) return TagChangeResult.Nothing;
        return Apply(snapshot, name, set => set.Without(name), snapshot.Registry.Remove(name));
    }

    private TagChangeResult Apply(TagSnapshot snapshot, string name, Func<TagSet, TagSet> change, TagRegistry done)
    {
        var carriers = snapshot.Profiles.Where(p => p.Tags.Contains(name)).ToList();
        var changed = new List<string>();
        foreach (var profile in carriers)
        {
            var updated = change(profile.Tags);
            // Ordinal, never TagSet.Equals: that ignores case, so a case-only rename would look unchanged and never
            // be written (spec 4.3).
            if (updated.Names.SequenceEqual(profile.Tags.Names, StringComparer.Ordinal)) continue;
            profiles.Save(profile with { Tags = updated });
            changed.Add(profile.Name);
        }
        tags.Save(done);
        return new TagChangeResult(carriers.Count, changed, null, null);
    }

    private static void Guard(string name, string parameter)
    {
        if (TagRegistry.IsReserved(name))
            throw new ArgumentException($"{TagRegistry.FavoriteName} is reserved and cannot be changed.", parameter);
    }
}
```

- [ ] **Step 5: Run the tests to see them pass**

Run: `dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~TagMaintenanceTests"`
Expected: all pass. If `Load_does_not_rewrite_the_registry_when_every_tag_is_known` fails, check the one-line JSON still matches `TagRegistryJsonContext`'s camelCase names before touching `Load`.

- [ ] **Step 6: Commit**

```bash
git add src/LizTerm.Core/Profiles/TagChangeResult.cs src/LizTerm.Core/Profiles/TagMaintenance.cs tests/LizTerm.Core.Tests/Profiles/TagMaintenanceTests.cs
git commit -m "Add TagMaintenance: rename, merge, recolour and delete a tag across profiles"
```

---

### Task 4: `TagMaintenance` — a failed write stops the action and says what changed

**Files:**
- Modify: `src/LizTerm.Core/Profiles/TagMaintenance.cs` (replace `Recolour`, `Rename`, `Delete` and `Apply`; add `TrySave`)
- Modify: `src/LizTerm.Core/CLAUDE.md`
- Test: `tests/LizTerm.Core.Tests/Profiles/TagMaintenanceTests.cs` (append)

**Interfaces:**
- Consumes: Task 3's `TagMaintenance`, the test class's `Block`, `Save`, `Define`, `TagsOf`, `ProfileFile`, `TagsFile` helpers.
- Produces: no new members. `TagChangeResult.FailedProfile` and `Error` are now populated as spec 4.2 describes.

- [ ] **Step 1: Write the failing tests**

Append inside `TagMaintenanceTests`:

```csharp
    [Fact]
    public void A_rename_that_fails_partway_reports_what_changed_and_defines_both_names_in_one_colour()
    {
        Save("alpha", "DEV");
        Save("beta", "DEV");
        Save("gamma", "DEV");
        Define(("DEV", TagColor.Teal));
        Block(ProfileFile("beta"));

        var result = _maintenance.Rename("DEV", "TEST");

        Assert.False(result.Succeeded);
        Assert.Equal(3, result.Carriers);
        Assert.Equal(["alpha"], result.Changed);
        Assert.Equal("beta", result.FailedProfile);
        Assert.NotNull(result.Error);
        Assert.Equal(["TEST"], TagsOf("alpha"));
        Assert.Equal(["DEV"], TagsOf("beta"));
        Assert.Equal(["DEV"], TagsOf("gamma"));
        var registry = _tags.Load();
        Assert.Equal(TagColor.Teal, registry.ColorOf("DEV"));
        Assert.Equal(TagColor.Teal, registry.ColorOf("TEST"));
    }

    /// <summary>The retry is a merge, because the partial rename defined the new name; merging finishes it.</summary>
    [Fact]
    public void Retrying_a_partial_rename_merges_the_rest_across()
    {
        Save("alpha", "DEV");
        Save("beta", "DEV");
        Save("gamma", "DEV");
        Define(("DEV", TagColor.Teal));
        Block(ProfileFile("beta"));
        _ = _maintenance.Rename("DEV", "TEST");
        Directory.Delete(ProfileFile("beta") + ".tmp");

        var retry = _maintenance.Rename("DEV", "TEST");

        Assert.True(retry.Succeeded);
        Assert.Equal(["beta", "gamma"], retry.Changed);
        Assert.All(new[] { "alpha", "beta", "gamma" }, name => Assert.Equal(["TEST"], TagsOf(name)));
        var registry = _tags.Load();
        Assert.False(registry.Contains("DEV"));
        Assert.Equal(TagColor.Teal, registry.ColorOf("TEST"));
    }

    [Theory]
    [InlineData("merge")]
    [InlineData("delete")]
    public void A_merge_or_delete_that_fails_partway_leaves_the_registry_alone(string action)
    {
        Save("alpha", "PRDO");
        Save("beta", "PRDO");
        Define(("PRDO", TagColor.Purple), ("PROD", TagColor.Amber));
        Block(ProfileFile("beta"));

        var result = action == "merge" ? _maintenance.Rename("PRDO", "PROD") : _maintenance.Delete("PRDO");

        Assert.Equal(["alpha"], result.Changed);
        Assert.Equal("beta", result.FailedProfile);
        Assert.Equal(["PRDO", "PROD"], _tags.Load().Stored.Select(d => d.Name));
    }

    [Fact]
    public void A_rename_that_fails_on_its_first_carrier_changes_neither_profiles_nor_registry()
    {
        Save("alpha", "DEV");
        Save("beta", "DEV");
        Define(("DEV", TagColor.Teal));
        Block(ProfileFile("alpha"));

        var result = _maintenance.Rename("DEV", "TEST");

        Assert.Empty(result.Changed);
        Assert.Equal("alpha", result.FailedProfile);
        Assert.Equal(["DEV"], TagsOf("beta"));
        Assert.Equal(["DEV"], _tags.Load().Stored.Select(d => d.Name));
    }

    [Fact]
    public void A_registry_that_cannot_be_saved_after_every_profile_is_reported_without_a_profile()
    {
        Save("alpha", "DEV");
        Save("beta", "DEV");
        Define(("DEV", TagColor.Teal));
        Block(TagsFile);

        var result = _maintenance.Rename("DEV", "TEST");

        Assert.False(result.Succeeded);
        Assert.Null(result.FailedProfile);
        Assert.Equal(["alpha", "beta"], result.Changed);
        Assert.Equal(["TEST"], TagsOf("beta"));
    }

    [Fact]
    public void A_recolour_that_cannot_be_saved_is_reported()
    {
        Save("a", "MVS");
        Define(("MVS", TagColor.Blue));
        Block(TagsFile);

        var result = _maintenance.Recolour("MVS", TagColor.Green);

        Assert.False(result.Succeeded);
        Assert.Null(result.FailedProfile);
        Assert.Equal(TagColor.Blue, _tags.Load().ColorOf("MVS"));
    }
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~TagMaintenanceTests"`
Expected: the six new tests fail with `System.UnauthorizedAccessException` thrown out of `TagMaintenance`; Task 3's tests still pass.

- [ ] **Step 3: Implement**

In `src/LizTerm.Core/Profiles/TagMaintenance.cs`, replace the `Recolour`, `Rename`, `Delete` and `Apply` methods with these, and add `TrySave` after `Apply` (leave `RenameProblem`, `Load` and `Guard` as they are):

```csharp
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
```

- [ ] **Step 4: Run the tests to see them pass**

Run: `dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~TagMaintenanceTests"`
Expected: all pass.

- [ ] **Step 5: Write the Core note**

In `src/LizTerm.Core/CLAUDE.md`, directly after the bullet that ends `` because a load must not write. ``, add:

```markdown
- `TagMaintenance` (#88) is the one place a tag changes across profiles. Each action reads the profiles and
  `tags.json` fresh, writes the carriers first and `tags.json` last, and stops at the first failed write, leaving
  the registry as the Manage Tags spec's table (4.2) says. It compares `TagSet.Names` **ordinally**, never with
  `TagSet.Equals`: equality ignores case, so a case-only rename would look unchanged and never be written. Its
  tests force a failed write by putting a directory at the file's `.tmp` path, which both stores write first.
```

- [ ] **Step 6: Commit**

```bash
git add src/LizTerm.Core/Profiles/TagMaintenance.cs src/LizTerm.Core/CLAUDE.md tests/LizTerm.Core.Tests/Profiles/TagMaintenanceTests.cs
git commit -m "Stop a tag change at the first failed write and report exactly what changed"
```

---

### Task 5: The picker re-reads `tags.json` on every reload

**Files:**
- Modify: `src/LizTerm.App/ViewModels/ProfilePickerViewModel.cs` (constructor and `Reload`)
- Modify: `src/LizTerm.App/CLAUDE.md`
- Test: `tests/LizTerm.App.Tests/ViewModels/ProfileViewModelsTests.cs` (append; add a using)

**Interfaces:**
- Consumes: existing `ProfilePickerViewModel(ProfileStore, Action<SessionProfile,bool>, Func<SessionProfile?,Task<ProfileEdit?>>, Action, TagRegistryStore?)`, the test class's `Picker(TagRegistryStore? tags = null)` helper, `TagPalette.Brush`.
- Produces: no new members; `Reload()` now reads the registry from its store.

- [ ] **Step 1: Write the failing tests**

Add `using LizTerm.App.Rendering;` to the usings of `ProfileViewModelsTests.cs`, then append inside the class:

```csharp
    /// <summary>Manage Tags writes tags.json while this picker waits behind it, so Reload must read the file again
    /// rather than keep the registry it read when it opened.</summary>
    [Fact]
    public void Reload_shows_a_colour_changed_on_disk()
    {
        _store.Save(new SessionProfile { Name = "a", Host = "h", Tags = TagSet.From(["PROD"]) });
        var tags = new TagRegistryStore(Path.Combine(_dir, "tags.json"));
        tags.Save(new TagRegistry([new TagDefinition("PROD", TagColor.Red)]));
        var vm = Picker(tags);

        tags.Save(new TagRegistry([new TagDefinition("PROD", TagColor.Green)]));
        vm.Reload();

        Assert.Same(TagPalette.Brush(TagColor.Green), vm.VisibleRows.Single().Chips.Single().Background);
    }

    /// <summary>The other half: a registry held from construction would write a definition deleted on disk back the
    /// next time the picker registered anything new.</summary>
    [Fact]
    public void Reload_does_not_write_back_a_definition_deleted_on_disk()
    {
        _store.Save(new SessionProfile { Name = "a", Host = "h", Tags = TagSet.From(["PROD"]) });
        var tags = new TagRegistryStore(Path.Combine(_dir, "tags.json"));
        tags.Save(new TagRegistry([new TagDefinition("LAB", TagColor.Teal), new TagDefinition("PROD", TagColor.Red)]));
        var vm = Picker(tags);

        tags.Save(new TagRegistry([new TagDefinition("PROD", TagColor.Red)]));
        _store.Save(new SessionProfile { Name = "b", Host = "h", Tags = TagSet.From(["MVS"]) });
        vm.Reload();

        Assert.Equal(["MVS", "PROD"], tags.Load().Stored.Select(d => d.Name));
    }
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~ProfileViewModelsTests.Reload_"`
Expected: both fail — the first on `Assert.Same` (the brush is still red), the second with `LAB` still in the list.

- [ ] **Step 3: Implement**

In `ProfilePickerViewModel`'s constructor, delete the line

```csharp
        _registry = tags?.Load() ?? TagRegistry.Empty;
```

(the field already starts as `TagRegistry.Empty`, and the constructor's `Reload()` now loads it). In `Reload()`, replace

```csharp
        foreach (var profile in _store.LoadAll()) Profiles.Add(profile);

        Reconcile();
```

with

```csharp
        foreach (var profile in _store.LoadAll()) Profiles.Add(profile);

        // Every time, not only at construction: Manage Tags writes tags.json while this picker waits behind it, and
        // a registry kept from construction would give a renamed tag a fresh colour and save over the one carried
        // across, write a deleted definition back the next time anything registers, and show an old colour until
        // the picker reopened. With no store — the in-memory registry tests use — there is nothing to re-read.
        if (_tags is not null) _registry = _tags.Load();
        Reconcile();
```

- [ ] **Step 4: Run the picker tests to see them pass**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~ProfileViewModelsTests|FullyQualifiedName~ProfilePickerWindowTests"`
Expected: all pass.

- [ ] **Step 5: Write the App note**

In `src/LizTerm.App/CLAUDE.md`, in "The session picker's tags", replace the end of the reconciliation bullet

```markdown
  `Reload` runs on every window activation, so an unconditional save would rewrite the file constantly.
```

with

```markdown
  `Reload` runs on every window activation, so an unconditional save would rewrite the file constantly. It also
  re-reads `tags.json` before reconciling, because Manage Tags (#88) writes the file while the picker waits
  behind the dialog: a registry kept from construction would recolour renamed tags and write deleted ones back.
```

- [ ] **Step 6: Commit**

```bash
git add src/LizTerm.App/ViewModels/ProfilePickerViewModel.cs src/LizTerm.App/CLAUDE.md tests/LizTerm.App.Tests/ViewModels/ProfileViewModelsTests.cs
git commit -m "Re-read tags.json on every picker reload"
```

---

### Task 6: `ManageTagsViewModel` — list, selection, validation, confirmations and actions

Failure messages are Task 7; here every action leaves `StatusMessage` null.

**Files:**
- Create: `src/LizTerm.App/ViewModels/TagListRow.cs`
- Create: `src/LizTerm.App/ViewModels/SwatchOption.cs`
- Create: `src/LizTerm.App/ViewModels/ManageTagsViewModel.cs`
- Test: `tests/LizTerm.App.Tests/ViewModels/ManageTagsViewModelTests.cs`

**Interfaces:**
- Consumes: `TagMaintenance`, `TagSnapshot`, `TagChangeResult` (Tasks 3–4); `TagRegistry.Contains`, `AssignableColors`, `IsReserved`; `TagChip` (declared in `src/LizTerm.App/ViewModels/ProfileRow.cs`); `TagPalette.Brush`.
- Produces (used by Tasks 7–9):
  - `public sealed class TagListRow(TagDefinition definition, IReadOnlyList<string> usedBy)` with `Name`, `Color`, `IsReserved`, `IsNotReserved`, `Chip`, `UsedBy`, `IsUnused`, `CountText`
  - `public sealed record SwatchOption(TagColor Color, IBrush Brush, bool IsSelected)` with `Name`
  - `public partial class ManageTagsViewModel(TagMaintenance maintenance)` with `Rows`, `Swatches`, `SelectedRow`, `NameText`, `ValidationMessage`, `PendingConfirmation`, `ConfirmLabel`, `StatusMessage`, `HasSelection`, `IsTagSelected`, `IsReservedSelected`, `HasPendingConfirmation`, `ShowDeleteButton`, `CanRename`, and commands `RenameCommand`, `DeleteCommand`, `RecolourCommand` (`IRelayCommand<TagColor>`), `ConfirmCommand`, `CancelConfirmationCommand`

- [ ] **Step 1: Write the failing tests**

Create `tests/LizTerm.App.Tests/ViewModels/ManageTagsViewModelTests.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Collections.Specialized;
using LizTerm.App.ViewModels;
using LizTerm.Core.Profiles;
using LizTerm.Core.Session;

namespace LizTerm.App.Tests.ViewModels;

public class ManageTagsViewModelTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lizterm-managetags-" + Guid.NewGuid().ToString("N"));
    private readonly ProfileStore _profiles;
    private readonly TagRegistryStore _tags;

    public ManageTagsViewModelTests()
    {
        _profiles = new ProfileStore(Path.Combine(_dir, "profiles"));
        _tags = new TagRegistryStore(Path.Combine(_dir, "tags.json"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private void Save(string name, params string[] tags) =>
        _profiles.Save(new SessionProfile { Name = name, Host = "h", Tags = TagSet.From(tags) });

    private void Define(params (string Name, TagColor Color)[] definitions) =>
        _tags.Save(new TagRegistry(definitions.Select(d => new TagDefinition(d.Name, d.Color))));

    private IReadOnlyList<string> TagsOf(string profile) => _profiles.Load(profile)!.Tags.Names;

    private ManageTagsViewModel Open() => new(new TagMaintenance(_profiles, _tags));

    private static TagListRow Row(ManageTagsViewModel vm, string name) => vm.Rows.Single(r => r.Name == name);

    /// <summary>The spec's and the mockups' sample set: a starred profile, an unused LAB, a PRDO typo.</summary>
    private void Seed()
    {
        Save("gateway", "PROD", "TLS");
        Save("mvsce", "FAVORITE", "PRDO", "MVS", "PROD");
        Define(("LAB", TagColor.Teal), ("MVS", TagColor.Blue), ("PRDO", TagColor.Purple), ("PROD", TagColor.Amber),
            ("TLS", TagColor.Green));
    }

    [Fact]
    public void The_list_is_the_reserved_tag_then_the_rest_alphabetically_unused_ones_included()
    {
        Seed();
        var vm = Open();

        Assert.Equal(["FAVORITE", "LAB", "MVS", "PRDO", "PROD", "TLS"], vm.Rows.Select(r => r.Name));
        Assert.Equal(["1", "unused", "1", "1", "2", "1"], vm.Rows.Select(r => r.CountText));
        Assert.True(Row(vm, "FAVORITE").IsReserved);
        Assert.Equal(["gateway", "mvsce"], Row(vm, "PROD").UsedBy);
        Assert.Equal("PROD", Row(vm, "PROD").Chip.Text);
    }

    [Fact]
    public void It_opens_with_nothing_selected()
    {
        Seed();
        var vm = Open();

        Assert.Null(vm.SelectedRow);
        Assert.False(vm.HasSelection);
        Assert.Empty(vm.Swatches);
    }

    [Fact]
    public void Rename_is_offered_only_for_a_valid_name_that_differs_from_the_stored_one()
    {
        Seed();
        var vm = Open();
        vm.SelectedRow = Row(vm, "PROD");
        Assert.Equal("PROD", vm.NameText);
        Assert.False(vm.CanRename);

        vm.NameText = "#PROD";
        Assert.False(vm.CanRename);
        Assert.Null(vm.ValidationMessage);

        vm.NameText = "prod";
        Assert.True(vm.CanRename);

        vm.NameText = "PROD, MVS";
        Assert.False(vm.CanRename);
        Assert.Equal("A tag name can't contain a comma.", vm.ValidationMessage);

        vm.NameText = "LIVE";
        Assert.True(vm.RenameCommand.CanExecute(null));
        Assert.Null(vm.ValidationMessage);
    }

    [Fact]
    public void A_rename_to_a_new_name_applies_at_once_and_the_selection_follows_it()
    {
        Seed();
        var vm = Open();
        vm.SelectedRow = Row(vm, "TLS");
        vm.NameText = "SSL";

        vm.RenameCommand.Execute(null);

        Assert.Null(vm.PendingConfirmation);
        Assert.Equal(["PROD", "SSL"], TagsOf("gateway"));
        Assert.Equal("SSL", vm.SelectedRow?.Name);
        Assert.DoesNotContain(vm.Rows, r => r.Name == "TLS");
    }

    [Fact]
    public void A_merge_asks_first_and_writes_nothing_until_confirmed()
    {
        Seed();
        var vm = Open();
        vm.SelectedRow = Row(vm, "PRDO");
        vm.NameText = "PROD";

        vm.RenameCommand.Execute(null);

        Assert.Equal("PROD already exists. Merge PRDO into it? mvsce will carry PROD instead, and PRDO's colour is dropped.",
            vm.PendingConfirmation);
        Assert.Equal("Merge", vm.ConfirmLabel);
        Assert.Equal(["FAVORITE", "PRDO", "MVS", "PROD"], TagsOf("mvsce"));

        vm.ConfirmCommand.Execute(null);

        Assert.Null(vm.PendingConfirmation);
        Assert.Equal(["FAVORITE", "PROD", "MVS"], TagsOf("mvsce"));
        Assert.Equal("PROD", vm.SelectedRow?.Name);
        Assert.DoesNotContain(vm.Rows, r => r.Name == "PRDO");
    }

    [Fact]
    public void Merging_an_unused_tag_says_only_its_colour_goes()
    {
        Seed();
        var vm = Open();
        vm.SelectedRow = Row(vm, "LAB");
        vm.NameText = "mvs";

        vm.RenameCommand.Execute(null);

        Assert.Equal("MVS already exists. Merge LAB into it? No profile uses LAB, so only its colour is dropped.",
            vm.PendingConfirmation);
    }

    [Fact]
    public void Delete_asks_first_naming_the_profiles_it_will_change()
    {
        Seed();
        var vm = Open();
        vm.SelectedRow = Row(vm, "PROD");

        vm.DeleteCommand.Execute(null);

        Assert.Equal("Delete PROD? It is removed from gateway and mvsce.", vm.PendingConfirmation);
        Assert.Equal("Delete", vm.ConfirmLabel);
        Assert.False(vm.ShowDeleteButton);
        Assert.Contains("PROD", TagsOf("gateway"));

        vm.ConfirmCommand.Execute(null);

        Assert.Equal(["TLS"], TagsOf("gateway"));
        Assert.Null(vm.SelectedRow);
        Assert.DoesNotContain(vm.Rows, r => r.Name == "PROD");
    }

    [Fact]
    public void Deleting_an_unused_tag_still_asks()
    {
        Seed();
        var vm = Open();
        vm.SelectedRow = Row(vm, "LAB");

        vm.DeleteCommand.Execute(null);

        Assert.Equal("Delete LAB? No profile uses it.", vm.PendingConfirmation);
    }

    [Fact]
    public void Three_profiles_are_named_and_a_fourth_turns_the_list_into_a_count()
    {
        Save("a", "DEV");
        Save("b", "DEV");
        Save("c", "DEV");
        Define(("DEV", TagColor.Teal));
        var vm = Open();
        vm.SelectedRow = Row(vm, "DEV");
        vm.DeleteCommand.Execute(null);
        Assert.Equal("Delete DEV? It is removed from a, b and c.", vm.PendingConfirmation);

        Save("d", "DEV");
        vm = Open();
        vm.SelectedRow = Row(vm, "DEV");
        vm.DeleteCommand.Execute(null);
        Assert.Equal("Delete DEV? It is removed from 4 profiles.", vm.PendingConfirmation);
    }

    [Fact]
    public void A_pending_question_is_withdrawn_by_selecting_editing_or_acting()
    {
        Seed();
        var vm = Open();

        vm.SelectedRow = Row(vm, "PROD");
        vm.DeleteCommand.Execute(null);
        vm.SelectedRow = Row(vm, "MVS");
        Assert.Null(vm.PendingConfirmation);

        vm.DeleteCommand.Execute(null);
        vm.NameText = "MVS2";
        Assert.Null(vm.PendingConfirmation);

        vm.DeleteCommand.Execute(null);
        vm.RecolourCommand.Execute(TagColor.Red);
        Assert.Null(vm.PendingConfirmation);

        vm.ConfirmCommand.Execute(null);
        Assert.Equal(["FAVORITE", "PRDO", "MVS", "PROD"], TagsOf("mvsce"));
        Assert.Contains("PROD", TagsOf("gateway"));
    }

    [Fact]
    public void Recolour_applies_at_once_moves_the_swatch_and_keeps_the_selection()
    {
        Seed();
        var vm = Open();
        vm.SelectedRow = Row(vm, "MVS");
        Assert.Equal(7, vm.Swatches.Count);
        Assert.DoesNotContain(vm.Swatches, s => s.Color == TagColor.Gold);
        Assert.Equal(TagColor.Blue, vm.Swatches.Single(s => s.IsSelected).Color);

        vm.RecolourCommand.Execute(TagColor.Green);

        Assert.Equal(TagColor.Green, _tags.Load().ColorOf("MVS"));
        Assert.Equal("MVS", vm.SelectedRow?.Name);
        Assert.Equal(TagColor.Green, vm.Swatches.Single(s => s.IsSelected).Color);
    }

    [Fact]
    public void The_reserved_row_offers_no_action_but_still_lists_its_profiles()
    {
        Seed();
        var vm = Open();

        vm.SelectedRow = Row(vm, "FAVORITE");

        Assert.True(vm.IsReservedSelected);
        Assert.False(vm.IsTagSelected);
        Assert.False(vm.CanRename);
        Assert.False(vm.DeleteCommand.CanExecute(null));
        Assert.False(vm.RecolourCommand.CanExecute(TagColor.Red));
        Assert.False(vm.ShowDeleteButton);
        Assert.Empty(vm.Swatches);
        Assert.Equal(["mvsce"], vm.SelectedRow!.UsedBy);
    }

    /// <summary>What a SelectingItemsControl bound two-way to SelectedItem does when its items are cleared: it nulls
    /// its own selection and the binding writes that back. A refresh that read the selection after clearing would
    /// lose it.</summary>
    [Fact]
    public void The_selection_survives_a_refresh_even_when_the_bound_list_nulls_it()
    {
        Seed();
        var vm = Open();
        vm.SelectedRow = Row(vm, "PROD");
        vm.Rows.CollectionChanged += (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Reset) vm.SelectedRow = null;
        };

        vm.RecolourCommand.Execute(TagColor.Red);

        Assert.Equal("PROD", vm.SelectedRow?.Name);
    }
}
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~ManageTagsViewModelTests"`
Expected: build errors — `ManageTagsViewModel` and `TagListRow` do not exist.

- [ ] **Step 3: Create the row and the swatch**

Create `src/LizTerm.App/ViewModels/TagListRow.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.App.Rendering;
using LizTerm.Core.Profiles;

namespace LizTerm.App.ViewModels;

/// <summary>One row of Manage Tags' list: a definition, how it draws, and which profiles carry it. Immutable and
/// rebuilt on every refresh, as ProfileRow is.</summary>
public sealed class TagListRow
{
    public TagListRow(TagDefinition definition, IReadOnlyList<string> usedBy)
    {
        Name = definition.Name;
        Color = definition.Color;
        IsReserved = TagRegistry.IsReserved(definition.Name);
        Chip = new TagChip(definition.Name.ToUpperInvariant(), TagPalette.Brush(definition.Color));
        UsedBy = usedBy;
    }

    /// <summary>As stored, casing included: the name box starts from this, and a case-only rename is judged
    /// against it.</summary>
    public string Name { get; }

    public TagColor Color { get; }

    /// <summary>FAVORITE: drawn as the star, and offered no action.</summary>
    public bool IsReserved { get; }

    public bool IsNotReserved => !IsReserved;

    public TagChip Chip { get; }

    /// <summary>The profiles carrying this tag, in the store's order.</summary>
    public IReadOnlyList<string> UsedBy { get; }

    public bool IsUnused => UsedBy.Count == 0;

    public string CountText => IsUnused ? "unused" : $"{UsedBy.Count}";
}
```

Create `src/LizTerm.App/ViewModels/SwatchOption.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Media;
using LizTerm.Core.Profiles;

namespace LizTerm.App.ViewModels;

/// <summary>One colour swatch in Manage Tags' panel. <see cref="Name"/> is both its tooltip and its accessible
/// name, so the choice is never carried by colour alone.</summary>
public sealed record SwatchOption(TagColor Color, IBrush Brush, bool IsSelected)
{
    public string Name => Color.ToString();
}
```

- [ ] **Step 4: Create the view model**

Create `src/LizTerm.App/ViewModels/ManageTagsViewModel.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LizTerm.App.Rendering;
using LizTerm.Core.Profiles;
using LizTerm.Core.Session;

namespace LizTerm.App.ViewModels;

/// <summary>Manage Tags (#88): every tag definition in a list, and the selected one's name, colour and profiles
/// beside it, where it can be renamed, merged, recoloured or deleted (spec 6). Every action runs through
/// TagMaintenance and applies at once; a merge or a delete asks first. UI thread only.</summary>
public partial class ManageTagsViewModel : ObservableObject
{
    private readonly TagMaintenance _maintenance;
    private TagSnapshot _snapshot;
    private Action? _confirmed;

    public ManageTagsViewModel(TagMaintenance maintenance)
    {
        _maintenance = maintenance;
        _snapshot = maintenance.Load();
        Rebuild();
    }

    public ObservableCollection<TagListRow> Rows { get; } = [];

    /// <summary>The seven assignable colours for the selected tag; empty for none, or for the reserved tag.</summary>
    public ObservableCollection<SwatchOption> Swatches { get; } = [];

    /// <summary>Nullable because it really is null at times: clearing Rows makes a bound ListBox null its selection,
    /// and the two-way binding writes that back (src/LizTerm.App/CLAUDE.md, "The session picker's tags"). So every
    /// refresh is told the name to select BEFORE it clears, and reselects by name.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection), nameof(IsTagSelected), nameof(IsReservedSelected), nameof(CanRename),
        nameof(ShowDeleteButton))]
    [NotifyCanExecuteChangedFor(nameof(RenameCommand), nameof(DeleteCommand), nameof(RecolourCommand))]
    private TagListRow? _selectedRow;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRename))]
    [NotifyCanExecuteChangedFor(nameof(RenameCommand))]
    private string _nameText = "";

    [ObservableProperty] private string? _validationMessage;

    /// <summary>The question the panel is waiting on, or null. What a yes runs is kept beside it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPendingConfirmation), nameof(ShowDeleteButton))]
    private string? _pendingConfirmation;

    [ObservableProperty] private string _confirmLabel = "";

    /// <summary>The last action's failure, or null. Survives a change of selection; the next action replaces it.</summary>
    [ObservableProperty] private string? _statusMessage;

    public bool HasSelection => SelectedRow is not null;

    public bool IsTagSelected => SelectedRow is { IsReserved: false };

    public bool IsReservedSelected => SelectedRow is { IsReserved: true };

    public bool HasPendingConfirmation => PendingConfirmation is not null;

    /// <summary>The confirmation strip takes the Delete button's place while it is up.</summary>
    public bool ShowDeleteButton => IsTagSelected && !HasPendingConfirmation;

    /// <summary>A valid name that differs from the stored one BY ORDINAL COMPARISON, so a case-only change counts.</summary>
    public bool CanRename =>
        SelectedRow is { IsReserved: false } row
        && TagMaintenance.RenameProblem(NameText) is null
        && !TagSet.Normalize(NameText).Equals(row.Name, StringComparison.Ordinal);

    partial void OnSelectedRowChanged(TagListRow? value)
    {
        CancelPending();
        NameText = value?.Name ?? "";
        ValidationMessage = null;
        Swatches.Clear();
        if (value is not { IsReserved: false }) return;
        foreach (var color in TagRegistry.AssignableColors)
            Swatches.Add(new SwatchOption(color, TagPalette.Brush(color), color == value.Color));
    }

    partial void OnNameTextChanged(string value)
    {
        CancelPending();
        ValidationMessage = SelectedRow is { IsReserved: false } row
                            && !TagSet.Normalize(value).Equals(row.Name, StringComparison.Ordinal)
            ? TagMaintenance.RenameProblem(value)
            : null;
    }

    [RelayCommand(CanExecute = nameof(CanRename))]
    private void Rename()
    {
        if (SelectedRow is not { IsReserved: false } row || !CanRename) return;
        var from = row.Name;
        var target = TagSet.Normalize(NameText);
        var merge = !target.Equals(from, StringComparison.OrdinalIgnoreCase) && _snapshot.Registry.Contains(target);
        if (!merge)
        {
            RunRename(from, target);
            return;
        }

        var shownFrom = from.ToUpperInvariant();
        var shownTo = target.ToUpperInvariant();
        Ask(row.IsUnused
                ? $"{shownTo} already exists. Merge {shownFrom} into it? No profile uses {shownFrom}, so only its colour is dropped."
                : $"{shownTo} already exists. Merge {shownFrom} into it? {ProfileList(row.UsedBy)} will carry {shownTo} instead, and {shownFrom}'s colour is dropped.",
            "Merge", () => RunRename(from, target));
    }

    [RelayCommand(CanExecute = nameof(IsTagSelected))]
    private void Delete()
    {
        if (SelectedRow is not { IsReserved: false } row) return;
        var name = row.Name;
        var shown = name.ToUpperInvariant();
        Ask(row.IsUnused ? $"Delete {shown}? No profile uses it." : $"Delete {shown}? It is removed from {ProfileList(row.UsedBy)}.",
            "Delete", () => RunDelete(name));
    }

    [RelayCommand(CanExecute = nameof(IsTagSelected))]
    private void Recolour(TagColor color)
    {
        if (SelectedRow is not { IsReserved: false } row) return;
        CancelPending();
        var name = row.Name;
        _maintenance.Recolour(name, color);
        Refresh(name);
        StatusMessage = null;
    }

    [RelayCommand]
    private void Confirm()
    {
        var run = _confirmed;
        CancelPending();
        run?.Invoke();
    }

    [RelayCommand]
    private void CancelConfirmation() => CancelPending();

    private void RunRename(string from, string target)
    {
        CancelPending();
        _maintenance.Rename(from, target);
        Refresh(target, from);
        StatusMessage = null;
    }

    private void RunDelete(string name)
    {
        CancelPending();
        _maintenance.Delete(name);
        Refresh(name);
        StatusMessage = null;
    }

    private void Ask(string message, string confirmLabel, Action run)
    {
        ConfirmLabel = confirmLabel;
        _confirmed = run;
        PendingConfirmation = message;
    }

    private void CancelPending()
    {
        _confirmed = null;
        PendingConfirmation = null;
    }

    /// <summary>Reads everything again and selects the first of <paramref name="preferred"/> still listed, or
    /// nothing.</summary>
    private void Refresh(params string[] preferred)
    {
        _snapshot = _maintenance.Load();
        Rebuild(preferred);
    }

    private void Rebuild(params string[] preferred)
    {
        Rows.Clear();
        foreach (var definition in _snapshot.Registry.All)
        {
            var usedBy = _snapshot.Profiles.Where(p => p.Tags.Contains(definition.Name)).Select(p => p.Name).ToList();
            Rows.Add(new TagListRow(definition, usedBy));
        }
        SelectedRow = preferred
            .Select(name => Rows.FirstOrDefault(r => r.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            .FirstOrDefault(row => row is not null);
    }

    /// <summary>"mvsce", "mvsce and gateway", "gateway, mvsce and tk5", and past three "4 profiles" (spec 6.2).</summary>
    private static string ProfileList(IReadOnlyList<string> names) => names.Count switch
    {
        1 => names[0],
        2 => $"{names[0]} and {names[1]}",
        3 => $"{names[0]}, {names[1]} and {names[2]}",
        _ => $"{names.Count} profiles",
    };
}
```

- [ ] **Step 5: Run the tests to see them pass**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~ManageTagsViewModelTests"`
Expected: all pass.

- [ ] **Step 6: Commit**

```bash
git add src/LizTerm.App/ViewModels/TagListRow.cs src/LizTerm.App/ViewModels/SwatchOption.cs src/LizTerm.App/ViewModels/ManageTagsViewModel.cs tests/LizTerm.App.Tests/ViewModels/ManageTagsViewModelTests.cs
git commit -m "Add the Manage Tags view model: list, selection, confirmations and actions"
```

---

### Task 7: `ManageTagsViewModel` — failure messages

**Files:**
- Modify: `src/LizTerm.App/ViewModels/ManageTagsViewModel.cs` (replace `Rename`, `Recolour`, `RunRename`, `RunDelete`; add `RenameStatus`, `RegistryNotSaved`)
- Test: `tests/LizTerm.App.Tests/ViewModels/ManageTagsViewModelTests.cs` (append)

**Interfaces:**
- Consumes: Task 6's view model; `TagChangeResult.Carriers`, `Changed`, `FailedProfile`, `Error`, `Succeeded`.
- Produces: `StatusMessage` text exactly as spec 6.2, with `\n` between a message's two lines (deviation D2).

- [ ] **Step 1: Write the failing tests**

Append inside `ManageTagsViewModelTests`. The error text itself comes from the operating system, so those assertions pin our wording on both sides of it with `StartsWith` and `EndsWith`:

```csharp
    private string TagsFile => Path.Combine(_dir, "tags.json");

    private string ProfileFile(string name) => Path.Combine(_dir, "profiles", ProfileStore.FileNameFor(name));

    /// <summary>A directory at the file's .tmp path makes its next save fail (see TagMaintenanceTests.Block).</summary>
    private static void Block(string file) => Directory.CreateDirectory(file + ".tmp");

    [Fact]
    public void A_rename_that_fails_partway_says_how_far_it_got_and_how_to_finish()
    {
        Save("alpha", "DEV");
        Save("beta", "DEV");
        Save("gamma", "DEV");
        Define(("DEV", TagColor.Teal));
        Block(ProfileFile("beta"));
        var vm = Open();
        vm.SelectedRow = Row(vm, "DEV");
        vm.NameText = "test";

        vm.RenameCommand.Execute(null);

        Assert.StartsWith("Renamed on 1 of 3 profiles. Could not write beta: ", vm.StatusMessage);
        Assert.EndsWith("\nRename DEV to TEST again to finish.", vm.StatusMessage);
        Assert.Equal("test", vm.SelectedRow?.Name);
        Assert.Contains(vm.Rows, r => r.Name == "DEV");
    }

    [Fact]
    public void A_case_only_rename_that_fails_partway_shows_both_names_as_typed()
    {
        Save("alpha", "dev");
        Save("beta", "dev");
        Define(("dev", TagColor.Teal));
        Block(ProfileFile("beta"));
        var vm = Open();
        vm.SelectedRow = Row(vm, "dev");
        vm.NameText = "DEV";

        vm.RenameCommand.Execute(null);

        Assert.StartsWith("Renamed on 1 of 2 profiles. Could not write beta: ", vm.StatusMessage);
        Assert.EndsWith("\nRename dev to DEV again to finish.", vm.StatusMessage);
    }

    [Fact]
    public void A_registry_that_cannot_be_saved_after_a_rename_or_a_merge_says_what_that_costs()
    {
        Seed();
        Block(TagsFile);
        var vm = Open();

        vm.SelectedRow = Row(vm, "TLS");
        vm.NameText = "SSL";
        vm.RenameCommand.Execute(null);
        Assert.StartsWith("Every profile was updated, but tags.json could not be saved: ", vm.StatusMessage);
        Assert.EndsWith("\nSSL may show a different colour next time.", vm.StatusMessage);

        vm.SelectedRow = Row(vm, "PRDO");
        vm.NameText = "PROD";
        vm.RenameCommand.Execute(null);
        vm.ConfirmCommand.Execute(null);
        Assert.StartsWith("Every profile was updated, but tags.json could not be saved: ", vm.StatusMessage);
        Assert.EndsWith("\nPRDO may still be listed.", vm.StatusMessage);
    }

    [Fact]
    public void A_registry_that_cannot_be_saved_after_a_case_only_rename_says_only_that()
    {
        Save("a", "dev");
        Define(("dev", TagColor.Teal));
        Block(TagsFile);
        var vm = Open();
        vm.SelectedRow = Row(vm, "dev");
        vm.NameText = "DEV";

        vm.RenameCommand.Execute(null);

        Assert.StartsWith("Every profile was updated, but tags.json could not be saved: ", vm.StatusMessage);
        Assert.DoesNotContain("\n", vm.StatusMessage);
    }

    [Fact]
    public void A_delete_that_fails_says_how_far_it_got_or_what_it_costs()
    {
        Save("alpha", "LAB");
        Save("beta", "LAB");
        Define(("LAB", TagColor.Teal));
        Block(ProfileFile("beta"));
        var vm = Open();
        vm.SelectedRow = Row(vm, "LAB");

        vm.DeleteCommand.Execute(null);
        vm.ConfirmCommand.Execute(null);

        Assert.StartsWith("Removed from 1 of 2 profiles. Could not write beta: ", vm.StatusMessage);
        Assert.EndsWith("\nDelete LAB again to finish.", vm.StatusMessage);
        Assert.Equal("LAB", vm.SelectedRow?.Name);

        Directory.Delete(ProfileFile("beta") + ".tmp");
        Block(TagsFile);
        vm.DeleteCommand.Execute(null);
        vm.ConfirmCommand.Execute(null);

        Assert.StartsWith("Every profile was updated, but tags.json could not be saved: ", vm.StatusMessage);
        Assert.EndsWith("\nLAB may still be listed.", vm.StatusMessage);
    }

    [Fact]
    public void A_recolour_that_cannot_be_saved_says_so_and_the_swatch_stays()
    {
        Save("a", "MVS");
        Define(("MVS", TagColor.Blue));
        Block(TagsFile);
        var vm = Open();
        vm.SelectedRow = Row(vm, "MVS");

        vm.RecolourCommand.Execute(TagColor.Green);

        Assert.StartsWith("Could not save the colour: ", vm.StatusMessage);
        Assert.Equal(TagColor.Blue, vm.Swatches.Single(s => s.IsSelected).Color);
    }

    [Fact]
    public void A_failure_message_outlasts_a_change_of_selection_and_goes_with_the_next_action()
    {
        Save("a", "LAB", "MVS");
        Define(("LAB", TagColor.Teal), ("MVS", TagColor.Blue));
        Block(TagsFile);
        var vm = Open();
        vm.SelectedRow = Row(vm, "MVS");
        vm.RecolourCommand.Execute(TagColor.Green);
        Assert.NotNull(vm.StatusMessage);

        vm.SelectedRow = Row(vm, "LAB");
        Assert.NotNull(vm.StatusMessage);

        Directory.Delete(TagsFile + ".tmp");
        vm.RecolourCommand.Execute(TagColor.Red);
        Assert.Null(vm.StatusMessage);
    }
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~ManageTagsViewModelTests"`
Expected: the seven new tests fail, each on a null `StatusMessage`; Task 6's tests still pass.

- [ ] **Step 3: Implement**

In `ManageTagsViewModel.cs`, replace the `Rename` method, the `Recolour` method, `RunRename` and `RunDelete` with the following, and add `RenameStatus` and `RegistryNotSaved` after `RunDelete`:

```csharp
    [RelayCommand(CanExecute = nameof(CanRename))]
    private void Rename()
    {
        if (SelectedRow is not { IsReserved: false } row || !CanRename) return;
        var from = row.Name;
        var target = TagSet.Normalize(NameText);
        var merge = !target.Equals(from, StringComparison.OrdinalIgnoreCase) && _snapshot.Registry.Contains(target);
        if (!merge)
        {
            RunRename(from, target, merge: false);
            return;
        }

        var shownFrom = from.ToUpperInvariant();
        var shownTo = target.ToUpperInvariant();
        Ask(row.IsUnused
                ? $"{shownTo} already exists. Merge {shownFrom} into it? No profile uses {shownFrom}, so only its colour is dropped."
                : $"{shownTo} already exists. Merge {shownFrom} into it? {ProfileList(row.UsedBy)} will carry {shownTo} instead, and {shownFrom}'s colour is dropped.",
            "Merge", () => RunRename(from, target, merge: true));
    }

    [RelayCommand(CanExecute = nameof(IsTagSelected))]
    private void Recolour(TagColor color)
    {
        if (SelectedRow is not { IsReserved: false } row) return;
        CancelPending();
        var name = row.Name;
        var result = _maintenance.Recolour(name, color);
        Refresh(name);
        StatusMessage = result.Succeeded ? null : $"Could not save the colour: {result.Error}";
    }

    private void RunRename(string from, string target, bool merge)
    {
        CancelPending();
        var result = _maintenance.Rename(from, target);
        Refresh(target, from);
        StatusMessage = RenameStatus(result, from, target, merge);
    }

    private void RunDelete(string name)
    {
        CancelPending();
        var result = _maintenance.Delete(name);
        Refresh(name);
        var shown = name.ToUpperInvariant();
        StatusMessage = result switch
        {
            { Succeeded: true } => null,
            { FailedProfile: { } failed } =>
                $"Removed from {result.Changed.Count} of {result.Carriers} profiles. Could not write {failed}: {result.Error}\nDelete {shown} again to finish.",
            _ => $"{RegistryNotSaved(result)}\n{shown} may still be listed.",
        };
    }

    /// <summary>Spec 6.2. Tag names are uppercased as the chips are, except in a case-only rename, whose casing is the
    /// whole change — "Rename DEV to DEV" would say nothing.</summary>
    private static string? RenameStatus(TagChangeResult result, string from, string target, bool merge)
    {
        if (result.Succeeded) return null;
        var caseOnly = from.Equals(target, StringComparison.OrdinalIgnoreCase);
        var shownFrom = caseOnly ? from : from.ToUpperInvariant();
        var shownTo = caseOnly ? target : target.ToUpperInvariant();
        if (result.FailedProfile is { } failed)
            return $"Renamed on {result.Changed.Count} of {result.Carriers} profiles. Could not write {failed}: {result.Error}\nRename {shownFrom} to {shownTo} again to finish.";
        if (caseOnly) return RegistryNotSaved(result);
        return merge
            ? $"{RegistryNotSaved(result)}\n{shownFrom} may still be listed."
            : $"{RegistryNotSaved(result)}\n{shownTo} may show a different colour next time.";
    }

    private static string RegistryNotSaved(TagChangeResult result) =>
        $"Every profile was updated, but tags.json could not be saved: {result.Error}";
```

- [ ] **Step 4: Run the tests to see them pass**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~ManageTagsViewModelTests"`
Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add src/LizTerm.App/ViewModels/ManageTagsViewModel.cs tests/LizTerm.App.Tests/ViewModels/ManageTagsViewModelTests.cs
git commit -m "Say exactly what a failed tag change did, and how to finish it"
```

---

### Task 8: `ManageTagsWindow` — layout B

**Files:**
- Create: `src/LizTerm.App/Views/ManageTagsWindow.axaml`
- Create: `src/LizTerm.App/Views/ManageTagsWindow.axaml.cs`
- Modify: `src/LizTerm.App/CLAUDE.md`
- Test: `tests/LizTerm.App.Tests/Views/ManageTagsWindowTests.cs`

**Interfaces:**
- Consumes: Task 6–7's `ManageTagsViewModel`, `TagListRow`, `SwatchOption`; `TagPalette.Star`, `TagPalette.ChipText`.
- Produces: `public ManageTagsWindow(ManageTagsViewModel viewModel)` (and a parameterless constructor for the XAML loader) — used by Task 9. Named controls tests rely on: `TagList`, `EmptyHint`, `TagPanel`, `NameBox`, `RenameButton`, `ReservedNameBox`, `ValidationText`, `SwatchList`, `UsedByList`, `DeleteButton`, `ConfirmationStrip`, `ConfirmationText`, `StatusText`, `DoneButton`.

- [ ] **Step 1: Write the failing tests**

Create `tests/LizTerm.App.Tests/Views/ManageTagsWindowTests.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using LizTerm.App.ViewModels;
using LizTerm.App.Views;
using LizTerm.Core.Profiles;
using LizTerm.Core.Session;

namespace LizTerm.App.Tests.Views;

public class ManageTagsWindowTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lizterm-managetags-window-" + Guid.NewGuid().ToString("N"));
    private readonly ProfileStore _profiles;
    private readonly TagRegistryStore _tags;

    public ManageTagsWindowTests()
    {
        _profiles = new ProfileStore(Path.Combine(_dir, "profiles"));
        _tags = new TagRegistryStore(Path.Combine(_dir, "tags.json"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
        GC.SuppressFinalize(this);
    }

    private (ManageTagsWindow Window, ManageTagsViewModel Vm) Show(bool atMinimumWidth = false)
    {
        _profiles.Save(new SessionProfile { Name = "gateway", Host = "h", Tags = TagSet.From(["PROD", "TLS"]) });
        _profiles.Save(new SessionProfile { Name = "mvsce", Host = "h", Tags = TagSet.From(["FAVORITE", "PROD"]) });
        _tags.Save(new TagRegistry([new TagDefinition("PROD", TagColor.Amber), new TagDefinition("TLS", TagColor.Green)]));
        var vm = new ManageTagsViewModel(new TagMaintenance(_profiles, _tags));
        var window = new ManageTagsWindow(vm);
        if (atMinimumWidth) window.Width = window.MinWidth;
        window.Show();
        window.UpdateLayout();
        return (window, vm);
    }

    /// <summary>Walks the realised controls, so the template's own bindings are what is asserted.</summary>
    private static IEnumerable<T> Descendants<T>(Visual root) where T : Visual => root.GetVisualDescendants().OfType<T>();

    private static void Select(ManageTagsWindow window, ManageTagsViewModel vm, string name)
    {
        vm.SelectedRow = vm.Rows.Single(r => r.Name == name);
        window.UpdateLayout();
    }

    [AvaloniaFact]
    public void The_list_draws_the_star_and_uppercase_chips_with_their_counts()
    {
        var (window, _) = Show();

        var list = window.FindControl<ListBox>("TagList")!;
        var texts = Descendants<TextBlock>(list).Where(t => t.IsEffectivelyVisible).Select(t => t.Text).ToList();

        Assert.Contains("★", texts);
        Assert.Contains("FAVORITE", texts);
        Assert.Contains("PROD", texts);
        Assert.Contains("TLS", texts);
        Assert.Contains("2", texts);
        Assert.Equal(2, Descendants<Border>(list).Count(b => b.Name == "Chip" && b.IsEffectivelyVisible));
    }

    [AvaloniaFact]
    public void Nothing_selected_shows_the_hint_and_no_panel()
    {
        var (window, _) = Show();

        Assert.True(window.FindControl<TextBlock>("EmptyHint")!.IsEffectivelyVisible);
        Assert.False(window.FindControl<DockPanel>("TagPanel")!.IsEffectivelyVisible);
    }

    /// <summary>Done is deliberately not the default button, and the box handles Enter itself, so a rename can never
    /// close the window instead.</summary>
    [AvaloniaFact]
    public void Enter_in_the_name_box_renames_and_leaves_the_window_open()
    {
        var (window, vm) = Show();
        Select(window, vm, "TLS");
        var box = window.FindControl<TextBox>("NameBox")!;
        box.Text = "SSL";
        box.Focus();

        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);

        Assert.Equal(["PROD", "SSL"], _profiles.Load("gateway")!.Tags.Names);
        Assert.True(window.IsVisible);
    }

    [AvaloniaFact]
    public void A_swatch_click_recolours_the_tag()
    {
        var (window, vm) = Show();
        Select(window, vm, "TLS");
        var teal = Descendants<Button>(window.FindControl<ItemsControl>("SwatchList")!)
            .Single(b => ToolTip.GetTip(b) as string == "Teal");

        teal.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.Equal(TagColor.Teal, _tags.Load().ColorOf("TLS"));
        Assert.Equal("TLS", vm.SelectedRow?.Name);
    }

    [AvaloniaFact]
    public void The_reserved_row_shows_its_profiles_but_no_rename_swatches_or_delete()
    {
        var (window, vm) = Show();

        Select(window, vm, "FAVORITE");

        Assert.True(window.FindControl<TextBox>("ReservedNameBox")!.IsEffectivelyVisible);
        Assert.False(window.FindControl<Button>("RenameButton")!.IsEffectivelyVisible);
        Assert.False(window.FindControl<Button>("DeleteButton")!.IsEffectivelyVisible);
        Assert.Empty(Descendants<Button>(window.FindControl<ItemsControl>("SwatchList")!));
        Assert.Contains(Descendants<TextBlock>(window.FindControl<ItemsControl>("UsedByList")!), t => t.Text == "mvsce");
    }

    [AvaloniaFact]
    public void Delete_puts_the_confirmation_strip_in_the_buttons_place()
    {
        var (window, vm) = Show();
        Select(window, vm, "TLS");

        vm.DeleteCommand.Execute(null);
        window.UpdateLayout();

        Assert.True(window.FindControl<Border>("ConfirmationStrip")!.IsEffectivelyVisible);
        Assert.False(window.FindControl<Button>("DeleteButton")!.IsEffectivelyVisible);
        Assert.Equal("Delete TLS? It is removed from gateway.", window.FindControl<TextBlock>("ConfirmationText")!.Text);
    }

    /// <summary>The panel gets 276px at the window's 520px MinWidth, once the margins, the 200px list and the gap
    /// take theirs; seven 22px swatches and the name row must both fit in it.</summary>
    [AvaloniaFact]
    public void The_panel_fits_at_the_windows_minimum_width()
    {
        var (window, vm) = Show(atMinimumWidth: true);
        Select(window, vm, "PROD");

        var rename = window.FindControl<Button>("RenameButton")!;
        var lastSwatch = Descendants<Button>(window.FindControl<ItemsControl>("SwatchList")!).Last();
        foreach (var control in new Control[] { rename, lastSwatch })
        {
            var right = control.TranslatePoint(new Point(control.Bounds.Width, 0), window)!.Value.X;
            Assert.True(right <= window.Bounds.Width - 16 + 0.5,
                $"{control.Name ?? "the last swatch"} ends at {right} in a {window.Bounds.Width}px window");
        }
    }

    [AvaloniaFact]
    public void Done_closes_the_window()
    {
        var (window, _) = Show();

        window.FindControl<Button>("DoneButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.False(window.IsVisible);
    }
}
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~ManageTagsWindowTests"`
Expected: build errors — `ManageTagsWindow` does not exist.

- [ ] **Step 3: Create the window**

Create `src/LizTerm.App/Views/ManageTagsWindow.axaml`:

```xml
<!--
  This file is part of LizTerm.
  Copyright 2026 by CoffeeMuse
  SPDX-License-Identifier: BSD-3-Clause
-->
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:vm="using:LizTerm.App.ViewModels"
        xmlns:render="using:LizTerm.App.Rendering"
        x:Class="LizTerm.App.Views.ManageTagsWindow"
        x:DataType="vm:ManageTagsViewModel"
        Icon="avares://LizTerm.App/Assets/Icons/lizterm-256.png"
        Title="Tags" Width="560" Height="420" MinWidth="520" MinHeight="360"
        WindowStartupLocation="CenterOwner">
  <Window.Styles>
    <Style Selector="Button.swatch">
      <Setter Property="Width" Value="22" />
      <Setter Property="Height" Value="22" />
      <Setter Property="Padding" Value="0" />
      <Setter Property="CornerRadius" Value="3" />
      <Setter Property="BorderThickness" Value="2" />
      <Setter Property="BorderBrush" Value="Transparent" />
    </Style>
    <Style Selector="Button.swatch.selected">
      <Setter Property="BorderBrush" Value="White" />
    </Style>
    <!-- A swatch keeps its own colour under the pointer and while pressed. Fluent's Button theme swaps the content
         presenter's background and border for theme brushes on :pointerover and :pressed, which would turn every
         swatch grey exactly when it is being chosen (plan D6). -->
    <Style Selector="Button.swatch:pointerover /template/ ContentPresenter#PART_ContentPresenter, Button.swatch:pressed /template/ ContentPresenter#PART_ContentPresenter"
           x:DataType="vm:SwatchOption">
      <Setter Property="Background" Value="{Binding Brush}" />
      <Setter Property="BorderBrush" Value="Transparent" />
    </Style>
    <Style Selector="Button.swatch.selected:pointerover /template/ ContentPresenter#PART_ContentPresenter, Button.swatch.selected:pressed /template/ ContentPresenter#PART_ContentPresenter">
      <Setter Property="BorderBrush" Value="White" />
    </Style>
  </Window.Styles>
  <DockPanel Margin="16">
    <TextBlock DockPanel.Dock="Top" Text="Tags" FontSize="20" Margin="0,0,0,10" />
    <!-- Done closes and does nothing else. IsCancel, so Escape closes too; deliberately NOT IsDefault, so Enter in
         the name box renames rather than closing the window (spec 6.3). -->
    <Button x:Name="DoneButton" DockPanel.Dock="Bottom" Content="Done" MinWidth="70" HorizontalAlignment="Right"
            Margin="0,12,0,0" IsCancel="True" Click="OnDoneClick" />
    <ListBox x:Name="TagList" DockPanel.Dock="Left" Width="200" ItemsSource="{Binding Rows}" SelectedItem="{Binding SelectedRow}">
      <ListBox.ItemTemplate>
        <DataTemplate x:DataType="vm:TagListRow">
          <!-- The session list's 15px gutter: the star and its name for the reserved tag, a chip for every other. -->
          <Grid ColumnDefinitions="15,*,Auto">
            <TextBlock Grid.Column="0" Text="★" Foreground="{x:Static render:TagPalette.Star}" FontSize="13"
                       VerticalAlignment="Center" IsVisible="{Binding IsReserved}" />
            <TextBlock Grid.Column="1" Text="{Binding Name}" FontSize="11" FontWeight="Bold" VerticalAlignment="Center"
                       IsVisible="{Binding IsReserved}" />
            <Border x:Name="Chip" Grid.Column="1" HorizontalAlignment="Left" VerticalAlignment="Center"
                    Background="{Binding Chip.Background}" CornerRadius="3" Padding="5,1" IsVisible="{Binding IsNotReserved}">
              <TextBlock Text="{Binding Chip.Text}" Foreground="{x:Static render:TagPalette.ChipText}" FontSize="10" FontWeight="Bold" />
            </Border>
            <TextBlock Grid.Column="2" Text="{Binding CountText}" Foreground="#A0A0A0" FontSize="12" VerticalAlignment="Center" />
          </Grid>
        </DataTemplate>
      </ListBox.ItemTemplate>
    </ListBox>
    <DockPanel Margin="12,0,0,0">
      <!-- Under the panel rather than in it, so a failure stays readable when the selection moves or clears (plan D3). -->
      <TextBlock x:Name="StatusText" DockPanel.Dock="Bottom" Text="{Binding StatusMessage}" Foreground="#FF8080"
                 FontSize="12" TextWrapping="Wrap" Margin="0,8,0,0"
                 IsVisible="{Binding StatusMessage, Converter={x:Static ObjectConverters.IsNotNull}}" />
      <Panel>
        <TextBlock x:Name="EmptyHint" IsVisible="{Binding !HasSelection}" HorizontalAlignment="Center"
                   VerticalAlignment="Center" TextAlignment="Center" TextWrapping="Wrap" Foreground="#8F8F8F"
                   FontStyle="Italic" FontSize="12"
                   Text="Select a tag to rename, recolour or delete it.&#10;Tags are added to a profile in its editor." />
        <DockPanel x:Name="TagPanel" IsVisible="{Binding HasSelection}">
          <Button x:Name="DeleteButton" DockPanel.Dock="Bottom" Content="Delete..." HorizontalAlignment="Right"
                  Command="{Binding DeleteCommand}" IsVisible="{Binding ShowDeleteButton}" />
          <Border x:Name="ConfirmationStrip" DockPanel.Dock="Bottom" IsVisible="{Binding HasPendingConfirmation}"
                  Background="#33291A" BorderBrush="#8A5A0F" BorderThickness="1" CornerRadius="4" Padding="10,8">
            <StackPanel Spacing="6">
              <TextBlock x:Name="ConfirmationText" Text="{Binding PendingConfirmation}" FontSize="12" TextWrapping="Wrap" />
              <StackPanel Orientation="Horizontal" HorizontalAlignment="Right" Spacing="6">
                <Button x:Name="CancelConfirmationButton" Content="Cancel" Command="{Binding CancelConfirmationCommand}" />
                <Button x:Name="ConfirmButton" Content="{Binding ConfirmLabel}" Command="{Binding ConfirmCommand}" />
              </StackPanel>
            </StackPanel>
          </Border>
          <StackPanel Spacing="4">
            <TextBlock Text="Name" Foreground="#A0A0A0" FontSize="12" />
            <DockPanel IsVisible="{Binding IsTagSelected}">
              <Button x:Name="RenameButton" DockPanel.Dock="Right" Content="Rename" Margin="6,0,0,0" Command="{Binding RenameCommand}" />
              <TextBox x:Name="NameBox" Text="{Binding NameText}" KeyDown="OnNameBoxKeyDown" />
            </DockPanel>
            <TextBox x:Name="ReservedNameBox" Text="{Binding NameText, Mode=OneWay}" IsReadOnly="True"
                     IsVisible="{Binding IsReservedSelected}" />
            <TextBlock x:Name="ValidationText" Text="{Binding ValidationMessage}" Foreground="#FF8080" FontSize="12"
                       TextWrapping="Wrap" IsVisible="{Binding ValidationMessage, Converter={x:Static ObjectConverters.IsNotNull}}" />
            <TextBlock Text="Colour" Foreground="#A0A0A0" FontSize="12" Margin="0,8,0,0" />
            <ItemsControl x:Name="SwatchList" ItemsSource="{Binding Swatches}" IsVisible="{Binding IsTagSelected}">
              <ItemsControl.ItemsPanel>
                <ItemsPanelTemplate>
                  <StackPanel Orientation="Horizontal" Spacing="6" />
                </ItemsPanelTemplate>
              </ItemsControl.ItemsPanel>
              <ItemsControl.ItemTemplate>
                <!-- The colour's name is the tooltip AND the accessible name, so the choice is not carried by colour
                     alone. A Click handler, not a Command binding, as Preferences' radios are. -->
                <DataTemplate x:DataType="vm:SwatchOption">
                  <Button Classes="swatch" Classes.selected="{Binding IsSelected}" Background="{Binding Brush}"
                          ToolTip.Tip="{Binding Name}" AutomationProperties.Name="{Binding Name}" Click="OnSwatchClick" />
                </DataTemplate>
              </ItemsControl.ItemTemplate>
            </ItemsControl>
            <StackPanel Orientation="Horizontal" Spacing="6" IsVisible="{Binding IsReservedSelected}">
              <TextBlock Text="★" Foreground="{x:Static render:TagPalette.Star}" FontSize="16" VerticalAlignment="Center" />
              <TextBlock Text="Reserved: always a gold star." Foreground="#8F8F8F" FontStyle="Italic" FontSize="12"
                         VerticalAlignment="Center" />
            </StackPanel>
            <TextBlock Text="Used by" Foreground="#A0A0A0" FontSize="12" Margin="0,8,0,0" />
            <TextBlock Text="No profile uses this tag." Foreground="#8F8F8F" FontStyle="Italic" FontSize="12"
                       IsVisible="{Binding SelectedRow.IsUnused, FallbackValue=False}" />
            <ScrollViewer MaxHeight="120">
              <ItemsControl x:Name="UsedByList" ItemsSource="{Binding SelectedRow.UsedBy}" />
            </ScrollViewer>
          </StackPanel>
        </DockPanel>
      </Panel>
    </DockPanel>
  </DockPanel>
</Window>
```

Create `src/LizTerm.App/Views/ManageTagsWindow.axaml.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using LizTerm.App.ViewModels;

namespace LizTerm.App.Views;

public partial class ManageTagsWindow : Window
{
    /// <summary>For the XAML loader; the app always passes a view model.</summary>
    public ManageTagsWindow() => InitializeComponent();

    public ManageTagsWindow(ManageTagsViewModel viewModel) : this() => DataContext = viewModel;

    /// <summary>Enter renames, and is handled here so nothing else in the window acts on it — the same shape as the
    /// picker's Quick Connect box.</summary>
    private void OnNameBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Return)) return;
        e.Handled = true;
        if (DataContext is ManageTagsViewModel vm && vm.RenameCommand.CanExecute(null)) vm.RenameCommand.Execute(null);
    }

    /// <summary>The swatch's data context is its SwatchOption, so the colour is read straight off it.</summary>
    private void OnSwatchClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ManageTagsViewModel vm && (sender as Button)?.DataContext is SwatchOption swatch
            && vm.RecolourCommand.CanExecute(swatch.Color))
        {
            vm.RecolourCommand.Execute(swatch.Color);
        }
    }

    private void OnDoneClick(object? sender, RoutedEventArgs e) => Close();
}
```

- [ ] **Step 4: Run the tests to see them pass**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~ManageTagsWindowTests"`
Expected: all pass. Then run the zero-warning check (`dotnet build LizTerm.slnx --no-incremental 2>&1 | grep -c " warning "` → `0`): compiled bindings report a misspelt property as a build error, and an unreachable `x:Class` as an `AVLN` warning.

- [ ] **Step 5: Write the App note**

In `src/LizTerm.App/CLAUDE.md`, in "The session picker's tags", directly after the bullet that ends `` container it right-clicked, for the same reason. ``, add:

```markdown
- **Manage Tags** (`Views/ManageTagsWindow`, #88) is modal over the picker. Its swatches are `Button`s, and a
  window style on `ContentPresenter#PART_ContentPresenter` keeps each one's own colour on `:pointerover` and
  `:pressed`, which Fluent's `Button` theme would otherwise swap for a grey. Done is `IsCancel` but not
  `IsDefault`, so Enter in the name box renames instead of closing the window. The failure line sits under the
  panel, not in it, so it survives a change of selection.
```

- [ ] **Step 6: Commit**

```bash
git add src/LizTerm.App/Views/ManageTagsWindow.axaml src/LizTerm.App/Views/ManageTagsWindow.axaml.cs src/LizTerm.App/CLAUDE.md tests/LizTerm.App.Tests/Views/ManageTagsWindowTests.cs
git commit -m "Add the Manage Tags window: the tag list and the selected tag's panel"
```

---

### Task 9: The picker's Tags... button

**Files:**
- Modify: `src/LizTerm.App/ViewModels/ProfilePickerViewModel.cs` (field, constructor parameter, command, `RebuildScopes` comment)
- Modify: `src/LizTerm.App/Views/ProfilePickerWindow.axaml` (the button)
- Modify: `src/LizTerm.App/Views/ProfilePickerWindow.axaml.cs` (the wiring)
- Test: `tests/LizTerm.App.Tests/ViewModels/ProfileViewModelsTests.cs` (append; fix one summary)
- Test: `tests/LizTerm.App.Tests/Views/ProfilePickerWindowTests.cs` (append)

**Interfaces:**
- Consumes: `ManageTagsWindow(ManageTagsViewModel)` (Task 8), `ManageTagsViewModel(TagMaintenance)` (Task 6), `TagMaintenance(ProfileStore, TagRegistryStore)` (Task 3).
- Produces: `ProfilePickerViewModel(..., TagRegistryStore? tags = null, Func<Task>? manageTags = null)` and `ManageTagsCommand` (`IAsyncRelayCommand`).

- [ ] **Step 1: Write the failing tests**

Append inside `ProfileViewModelsTests`:

```csharp
    [Fact]
    public async Task Tags_opens_manage_tags_and_reloads_when_it_closes()
    {
        _store.Save(new SessionProfile { Name = "a", Host = "h" });
        var opened = 0;
        var vm = new ProfilePickerViewModel(_store, (_, _) => { }, _ => Task.FromResult<ProfileEdit?>(null), () => { }, null,
            () =>
            {
                opened++;
                _store.Save(new SessionProfile { Name = "b", Host = "h" });
                return Task.CompletedTask;
            });

        await vm.ManageTagsCommand.ExecuteAsync(null);

        Assert.Equal(1, opened);
        Assert.Equal(["a", "b"], vm.VisibleRows.Select(r => r.Name));
    }

    [Fact]
    public void Tags_is_unavailable_without_a_way_to_open_it()
    {
        Assert.False(Picker().ManageTagsCommand.CanExecute(null));
    }
```

Append inside `ProfilePickerWindowTests`:

```csharp
    [AvaloniaFact]
    public void Tags_opens_manage_tags_over_the_picker()
    {
        var store = new ProfileStore(_dir);
        store.Save(new SessionProfile { Name = "a", Host = "h", Tags = TagSet.From(["PROD"]) });
        var window = new ProfilePickerWindow(store, (_, _) => { }, () => { }, new TagRegistryStore(Path.Combine(_dir, "tags.json")));
        window.Show();
        var vm = (ProfilePickerViewModel)window.DataContext!;

        var button = window.FindControl<Button>("TagsButton")!;
        Assert.Same(vm.ManageTagsCommand, button.Command);
        Assert.True(button.IsEffectivelyEnabled);

        // Through the command, because raising Button.ClickEvent runs Click handlers but never a bound Command.
        vm.ManageTagsCommand.Execute(null);

        var dialog = Assert.IsType<ManageTagsWindow>(Assert.Single(window.OwnedWindows));
        Assert.Contains(((ManageTagsViewModel)dialog.DataContext!).Rows, r => r.Name == "PROD");
        dialog.Close();
    }
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~ProfileViewModelsTests|FullyQualifiedName~ProfilePickerWindowTests"`
Expected: build errors — `ManageTagsCommand` does not exist, and the constructor takes no sixth argument.

- [ ] **Step 3: Implement the view model half**

In `ProfilePickerViewModel.cs`:

1. Beside `private readonly TagRegistryStore? _tags;`, add:

```csharp
    private readonly Func<Task>? _manageTags;
```

2. In the constructor's XML doc, after the `<param name="tags">` element, add:

```csharp
    /// <param name="manageTags">Shows Manage Tags and completes when it closes, or null where there is no tag file
    /// to manage — which is also what a test that does not care wants.</param>
```

3. Change the constructor's signature and body to:

```csharp
    public ProfilePickerViewModel(ProfileStore store, Action<SessionProfile, bool> openSession,
        Func<SessionProfile?, Task<ProfileEdit?>> editProfile, Action quit, TagRegistryStore? tags = null,
        Func<Task>? manageTags = null)
    {
        _store = store;
        _openSession = openSession;
        _editProfile = editProfile;
        _quit = quit;
        _tags = tags;
        _manageTags = manageTags;
        Reload();
    }
```

4. Directly after the `Delete` command, add:

```csharp
    /// <summary>Tags...: Manage Tags over this picker, then a reload, since it may have renamed, recoloured or deleted
    /// any tag on any profile.</summary>
    [RelayCommand(CanExecute = nameof(CanManageTags))]
    private async Task ManageTagsAsync()
    {
        await _manageTags!();
        Reload();
    }

    private bool CanManageTags() => _manageTags is not null;
```

5. In `RebuildScopes`, replace the comment

```csharp
        // Tags some profile actually carries, not every registered tag (spec 5.3 says registered). Deliberate:
        // Manage Tags is a later issue, so nothing can delete a definition yet, and a scope whose tag no longer
        // exists anywhere filters to nothing with no way to clear it from the list.
```

with

```csharp
        // Tags some profile actually carries, not every registered tag (the tags spec's 5.3 says registered).
        // Deliberate, and kept when Manage Tags arrived (#88, its spec 2.7): a scope for a tag no profile carries
        // filters the list to nothing, and Manage Tags is where an unused definition is found and deleted.
```

6. In `ProfileViewModelsTests.cs`, replace the summary of `A_scope_whose_tag_no_longer_exists_falls_back_to_all_sessions`

```csharp
    /// <summary>A scope whose tag has vanished would otherwise filter the list to nothing with no way back —
    /// there is no Manage Tags window in this phase to remove the definition.</summary>
```

with

```csharp
    /// <summary>A scope whose tag no profile carries any more would otherwise filter the list to nothing, with no
    /// way back from the list itself; Manage Tags is where the unused definition gets deleted.</summary>
```

- [ ] **Step 4: Implement the window half**

In `ProfilePickerWindow.axaml`, directly after

```xml
      <Button Content="Delete" Command="{Binding DeleteCommand}" HorizontalAlignment="Stretch" />
```

add

```xml
      <Button x:Name="TagsButton" Content="Tags..." Command="{Binding ManageTagsCommand}" HorizontalAlignment="Stretch" />
```

In `ProfilePickerWindow.axaml.cs`, replace the store-taking constructor with:

```csharp
    public ProfilePickerWindow(ProfileStore store, Action<SessionProfile, bool> openSession, Action quit, TagRegistryStore? tags = null) : this()
    {
        DataContext = new ProfilePickerViewModel(
            store,
            openSession,
            existing => new ProfileEditorWindow(existing).ShowDialog<ProfileEdit?>(this),
            quit,
            tags,
            // Modal over this picker, so it cannot be open at the same time as the picker's own editor (spec 2.1).
            tags is null
                ? null
                : () => new ManageTagsWindow(new ManageTagsViewModel(new TagMaintenance(store, tags))).ShowDialog(this));
    }
```

- [ ] **Step 5: Run the tests to see them pass**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~ProfileViewModelsTests|FullyQualifiedName~ProfilePickerWindowTests"`
Expected: all pass.

- [ ] **Step 6: Commit**

```bash
git add src/LizTerm.App/ViewModels/ProfilePickerViewModel.cs src/LizTerm.App/Views/ProfilePickerWindow.axaml src/LizTerm.App/Views/ProfilePickerWindow.axaml.cs tests/LizTerm.App.Tests/ViewModels/ProfileViewModelsTests.cs tests/LizTerm.App.Tests/Views/ProfilePickerWindowTests.cs
git commit -m "Open Manage Tags from a Tags... button in the session picker"
```

---

### Task 10: The user guide, and the whole-tree checks

**Files:**
- Modify: `docs/user-guide.md`
- Regenerate: `src/LizTerm.App/Assets/Docs/user-guide.html`

**Interfaces:**
- Consumes: everything above.
- Produces: nothing new in code.

- [ ] **Step 1: Describe Manage Tags**

In `docs/user-guide.md`, directly after the filter paragraph that ends

```markdown
the scope, the box searches inside it. Whatever the filter shows, **Quick Connect** still reaches every saved
profile by name.
```

add a blank line and:

```markdown
**Tags...** lists every tag, including ones no profile uses any more. Select one to rename it, give it another
colour, or delete it: each change applies at once to every profile carrying the tag, and the tag's **Used by** list
shows which those are. Renaming a tag to one that already exists merges the two, and both a merge and a delete ask
first. **FAVORITE** is fixed: it can't be renamed, recoloured or deleted.
```

In the profile settings table's **Tags** row, replace

```markdown
A tag's colour is picked automatically the first time you use it, and is the same everywhere that tag appears.
```

with

```markdown
A tag's colour is picked automatically the first time you use it, and is the same everywhere that tag appears; **Tags...** in the Sessions list changes it.
```

- [ ] **Step 2: Regenerate the bundled guide**

Run: `LIZTERM_UPDATE_DOCS=1 dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~UserGuideAssetTests"`
Expected: passes, and `git diff --stat` shows `src/LizTerm.App/Assets/Docs/user-guide.html` changed.

- [ ] **Step 3: Run the whole suite**

Run: `dotnet test LizTerm.slnx`
Expected: every project passes; the live-host tests skip themselves.

- [ ] **Step 4: Run the zero-warning check**

Run: `dotnet build LizTerm.slnx --no-incremental 2>&1 | grep -c " warning "`
Expected: `0`.

- [ ] **Step 5: Commit**

```bash
git add docs/user-guide.md src/LizTerm.App/Assets/Docs/user-guide.html
git commit -m "Document Manage Tags"
```

---

## Manual verification

Headless tests cannot show hover styling or a real dialog, so after Task 10, check once in the running app, on throwaway profiles so the real ones are never touched:

1. `dotnet build src/LizTerm.App`, then seed `<scratch>/home/Library/Application Support/LizTerm/profiles/` with camelCase profile JSON: `mvsce` carrying `["FAVORITE", "PRDO", "MVS", "PROD"]`, `gateway` carrying `["PROD", "TLS"]`, and a `tags.json` beside `profiles/` that also defines an unused `LAB`.
2. Launch the apphost with `HOME=<scratch>/home LIZTERM_B3270_PATH=/opt/homebrew/bin/b3270 LIZTERM_MENU=classic src/LizTerm.App/bin/Debug/net10.0/LizTerm.App` (no profile argument, so the picker opens), and attach the Avalonia DevTools MCP by pid (`src/LizTerm.App/CLAUDE.md`, "Driving the app").
3. Click **Tags...**; screenshot the dialog. It lists FAVORITE, LAB (unused), MVS, PRDO, PROD, TLS, with nothing selected and the hint showing.
4. Select PROD, hover a swatch: it keeps its colour (plan D6). Resize the window to its minimum: nothing clips.
5. Select PRDO, type `PROD`, press Enter: the merge strip asks; press Merge. PRDO leaves the list; mvsce's Used by entry moves to PROD.
6. Select MVS, click Green. Select LAB, Delete..., Delete.
7. Done. The picker's rows show the merged PROD and a green MVS chip without reopening the picker.
8. Kill the app.

## Execution notes

- Tasks 1–4 are Core only and can be reviewed without running the app. Tasks 6–8 depend on 3–4 and Task 9 on 8. Task 5 uses only APIs that already exist, so it can run at any point.
- #90 (the row menu) is not merged yet. When it merges, rebase `claude/issue-88-manage-tags` onto `main` before opening the PR, and target `main`.

