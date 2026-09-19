# Editable keymap, PR 1: model, store and live wiring — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A hand-editable `keymap.json` that every session window's screen and keypad follow the moment it changes, with the model, store, refusal policy and live view model the Keyboard tab (PR 2) and the Keys menu hints (PR 3) will build on.

**Architecture:** Core owns the file as strings (`KeymapEntry`, `KeymapFile`, `KeymapStore`, an atomic `JsonFiles` helper shared with `SettingsStore`). App parses chords and actions (`ChordSyntax`, `KeymapAction`), holds the user's differences as an immutable `KeymapOverlay` that composes the profile's default under them, judges chords with a pure `KeymapPolicy`, and keeps one live `KeymapViewModel` per process, shaped like `SettingsViewModel`. `TerminalScreen` gets a `Keymap` property in place of its private default; `SessionWindow.AttachKeymap` composes and pushes the map to the screen and the keypad on every change.

**Tech Stack:** .NET 10, Avalonia 12 (headless tests via `[AvaloniaFact]`), xunit.v3, System.Text.Json nodes (no source-generated context: the file is read as a `JsonObject`), CommunityToolkit.Mvvm `ObservableObject`.

**Spec:** `docs/superpowers/specs/2026-09-19-lizterm-editable-keymap-design.md` — this plan implements §3, §4, §5.1, §5.2 and the file docs of §6.1 (delivery item 1 of §8). PR 2 (the Keyboard tab) and PR 3 (Keys menu hints, changelog) get their own plans once this one is merged.

## Global Constraints

- **Dependency rule.** `LizTerm.Core` depends only on the BCL and never mentions Avalonia; a chord is an Avalonia `Key`, so everything that names a `Key` or a `TerminalKey` by type lives in `LizTerm.App`.
- **Licence headers.** Every new `.cs` file starts with exactly these three lines, then a blank line: `// This file is part of LizTerm.` / `// Copyright 2026 by CoffeeMuse` / `// SPDX-License-Identifier: BSD-3-Clause`. `RepositoryHeadersTests` fails the suite otherwise.
- **Zero warnings.** CI builds with `-warnaserror`. Before calling the PR done: `dotnet build LizTerm.slnx --no-incremental 2>&1 | grep -c " warning "` must print `0`.
- **Tests.** xunit.v3 in VSTest mode; run one class with `dotnet test <project> --filter "FullyQualifiedName~<Class>"`. App control tests are `[AvaloniaFact]` and drive keys with `window.KeyPressQwerty(PhysicalKey, RawInputModifiers)`.
- **Docs have one home.** When a change makes a documented sentence untrue, fix that sentence where it lives. `CHANGELOG.md` entries go under `## Unreleased`.
- **Chord spelling (spec §3.1).** Modifiers in the fixed order `Ctrl`, `Alt`, `Shift`, joined by `+` to an Avalonia `Key` name; taps as `Tap:LeftCtrl` / `Tap:RightCtrl`. Case-insensitive on the way in. `Cmd` never parses.
- **Sparse rule (spec §3.1, §3.3).** An entry equal to `DefaultKeymap.Create(destructiveBackspace: true)`'s answer for its chord is dropped, except an entry for unmodified `Back`, which is always kept.
- **Unreadable entries (spec §3.1).** Skipped on their own, kept verbatim on the next save, counted for the tab. Reset to defaults drops them too.
- **No em dashes in new prose** that ends up in the user guide, the changelog or the privacy doc; use a full stop or a comma.
- Run every command from the worktree root: `/Users/robert/ClaudeSandbox/LizTerm/.claude/worktrees/issue-18-review-8a083f`.

---

## File map

| File | Responsibility |
|---|---|
| Create `src/LizTerm.Core/Settings/JsonFiles.cs` | Lenient read, strict read, parse, atomic write; shared by `SettingsStore` and `KeymapStore` |
| Modify `src/LizTerm.Core/Settings/SettingsStore.cs` | Delegate its private file helpers to `JsonFiles` |
| Create `src/LizTerm.Core/Settings/KeymapEntry.cs` | A binding's value as strings: `SendKey`, `TypeText`, `Unbound` |
| Create `src/LizTerm.Core/Settings/KeymapFile.cs` | `keymap.json` as a document: read, write, unreadable pass-through |
| Create `src/LizTerm.Core/Settings/KeymapStore.cs` | `Load` never throws; `Update` re-reads and writes atomically |
| Modify `src/LizTerm.Core/Profiles/AppPaths.cs` | `KeymapFile()` |
| Create `src/LizTerm.App/Keyboard/KeymapAction.cs` | A binding's value as types: `SendKey(TerminalKey)`, `TypeText`, `Unbound`; to/from `KeymapEntry` |
| Create `src/LizTerm.App/Keyboard/ChordSyntax.cs` | Chord spelling: `Format`, `TryParse` |
| Modify `src/LizTerm.App/Keyboard/Keymap.cs` | `Without`; doc comment |
| Modify `src/LizTerm.App/Keyboard/DefaultKeymap.cs` | Doc comment only |
| Create `src/LizTerm.App/Keyboard/KeymapOverlay.cs` | The user's differences: parse, write, bind, unbind, prune, compose |
| Create `src/LizTerm.App/Keyboard/KeymapPolicy.cs` | `PlatformHotkeys`, `KeymapVerdict`, `KeymapPolicy.Check` |
| Create `src/LizTerm.App/ViewModels/KeymapViewModel.cs` | The process's one live keymap, write-through, row queries |
| Modify `src/LizTerm.App/Controls/TerminalScreen.cs` | `Keymap` styled property replaces `DestructiveBackspace` |
| Modify `src/LizTerm.App/Views/SessionWindow.axaml` | Drop the `DestructiveBackspace` binding |
| Modify `src/LizTerm.App/Views/SessionWindow.axaml.cs` | `AttachKeymap`, `ApplyKeymap`, subscription lifetime |
| Modify `src/LizTerm.App/App.axaml.cs` | `Keymap` property; attach in `OpenSession` |
| Tests | `tests/LizTerm.Core.Tests/Settings/KeymapFileTests.cs`, `KeymapStoreTests.cs`; `tests/LizTerm.App.Tests/Keyboard/ChordSyntaxTests.cs`, `KeymapActionTests.cs`, `KeymapOverlayTests.cs`, `KeymapPolicyTests.cs`; `tests/LizTerm.App.Tests/ViewModels/KeymapViewModelTests.cs`; `tests/LizTerm.App.Tests/Views/SessionWindowKeymapTests.cs`; one edit in `tests/LizTerm.App.Tests/Controls/TerminalScreenInputTests.cs` |
| Docs | `docs/user-guide.md`, `docs/privacy.md`, `CHANGELOG.md`, `src/LizTerm.App/CLAUDE.md`, `tests/CLAUDE.md` |

---

### Task 1: `JsonFiles`, the file discipline `SettingsStore` and `KeymapStore` share

**Files:**
- Create: `src/LizTerm.Core/Settings/JsonFiles.cs`
- Modify: `src/LizTerm.Core/Settings/SettingsStore.cs` (the `Indented` field at line 16, `ReadLenient`, `ReadStrict`, `Parse` and `Write` at lines 44–98)
- Test: existing `tests/LizTerm.Core.Tests/Settings/SettingsStoreTests.cs` (no new tests; this is a refactor the existing class covers)

**Interfaces:**
- Produces: `internal static class JsonFiles` with `JsonSerializerOptions Indented`, `JsonObject? ReadLenient(string path)`, `JsonObject? ReadStrict(string path)`, `JsonObject? Parse(string text)`, `void Write(string path, string text)`.

- [ ] **Step 1: Run the settings store tests to record the baseline**

Run: `dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~SettingsStoreTests"`
Expected: all PASS.

- [ ] **Step 2: Create `JsonFiles.cs`**

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Text.Json;
using System.Text.Json.Nodes;

namespace LizTerm.Core.Settings;

/// <summary>The file discipline SettingsStore and KeymapStore share: a lenient read for Load, a strict read for
/// Update, one parser, and a write through a sibling temp file renamed over the target.</summary>
internal static class JsonFiles
{
    public static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    /// <summary>The file as a JSON object, or null when it is missing, unreadable, not JSON or not an object.</summary>
    public static JsonObject? ReadLenient(string path)
    {
        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
        return Parse(text);
    }

    /// <summary>Null when the file is missing. Throws InvalidDataException, naming the file, when it exists and is
    /// not a JSON object, rather than let a caller replace something the user may be editing by hand. IO and
    /// permission errors propagate.</summary>
    public static JsonObject? ReadStrict(string path)
    {
        if (!File.Exists(path)) return null;
        return Parse(File.ReadAllText(path))
               ?? throw new InvalidDataException($"{Path.GetFileName(path)} is not valid JSON; fix or delete it: {path}");
    }

    /// <summary>The text as a JSON object, or null for anything else: not JSON, JSON that is not an object, or an
    /// object with a duplicated key.</summary>
    public static JsonObject? Parse(string text)
    {
        try
        {
            return JsonNode.Parse(text, documentOptions: new() { AllowDuplicateProperties = false }) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Through a sibling temp file renamed over the target, so a reader never sees a partial file, and
    /// a write interrupted by a crash or a full disk leaves the old file rather than a broken one. The temp file
    /// is a sibling on purpose: File.Move across a filesystem is a copy, which is not atomic.</summary>
    public static void Write(string path, string text)
    {
        if (Path.GetDirectoryName(path) is { Length: > 0 } directory) Directory.CreateDirectory(directory);
        var temp = path + ".tmp";
        try
        {
            File.WriteAllText(temp, text);
            File.Move(temp, path, overwrite: true);
        }
        catch
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { /* the write already failed; this is cleanup */ }
            throw;
        }
    }
}
```

- [ ] **Step 3: Point `SettingsStore` at it**

In `SettingsStore.cs`: delete the `Indented` field, and the private `ReadLenient`, `ReadStrict`, `Parse` and `Write` methods (keep `Layers`). Change the two call sites:

```csharp
    public AppSettings Load() => SettingsLayers.Read(SettingsLayers.Merge(Layers(JsonFiles.ReadLenient(FilePath))));

    public AppSettings Update(Func<AppSettings, AppSettings> change)
    {
        var existing = JsonFiles.ReadStrict(FilePath);
        var next = change(SettingsLayers.Read(SettingsLayers.Merge(Layers(existing))));
        JsonFiles.Write(FilePath, SettingsLayers.UserDocument(next, Beneath, existing).ToJsonString(JsonFiles.Indented));
        return next;
    }
```

Remove the now-unused `using System.Text.Json;` if the compiler reports it unused (keep `System.Text.Json.Nodes`, `Layers` uses `JsonObject`).

- [ ] **Step 4: Run the settings store tests again**

Run: `dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~SettingsStoreTests"`
Expected: all PASS, same count as Step 1.

- [ ] **Step 5: Commit**

```bash
git add src/LizTerm.Core/Settings/JsonFiles.cs src/LizTerm.Core/Settings/SettingsStore.cs
git commit -m "Core: lift SettingsStore's file discipline into JsonFiles for a second store to share

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 2: `KeymapEntry` and `KeymapFile`, the document as strings

**Files:**
- Create: `src/LizTerm.Core/Settings/KeymapEntry.cs`
- Create: `src/LizTerm.Core/Settings/KeymapFile.cs`
- Test: `tests/LizTerm.Core.Tests/Settings/KeymapFileTests.cs`

**Interfaces:**
- Produces: `abstract record KeymapEntry` with nested `SendKey(string KeyName)`, `TypeText(string Text)`, `Unbound` (`Unbound.Instance`). `sealed class KeymapFile` with `static KeymapFile Empty`, constructor `KeymapFile(IEnumerable<KeyValuePair<string, KeymapEntry>> bindings, IEnumerable<KeyValuePair<string, JsonNode?>> unreadable)`, `IReadOnlyDictionary<string, KeymapEntry> Bindings`, `IReadOnlyDictionary<string, JsonNode?> Unreadable`, `static KeymapFile FromJson(JsonObject? document)`, `JsonObject ToJson()`.

- [ ] **Step 1: Write the failing tests**

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Text.Json.Nodes;
using LizTerm.Core.Settings;

namespace LizTerm.Core.Tests.Settings;

/// <summary>keymap.json's shape (editable keymap spec §3.1): three readable value shapes, everything else carried
/// through untouched.</summary>
public class KeymapFileTests
{
    private static JsonObject Doc(string bindings) => JsonNode.Parse("{\"bindings\": " + bindings + "}")!.AsObject();

    [Fact]
    public void Reads_a_key_name_a_text_object_and_null()
    {
        var file = KeymapFile.FromJson(Doc("""{"Ctrl+Home": "PA1", "Ctrl+D6": {"text": "¢"}, "Alt+2": null}"""));

        Assert.Equal(new KeymapEntry.SendKey("PA1"), file.Bindings["Ctrl+Home"]);
        Assert.Equal(new KeymapEntry.TypeText("¢"), file.Bindings["Ctrl+D6"]);
        Assert.Same(KeymapEntry.Unbound.Instance, file.Bindings["Alt+2"]);
        Assert.Empty(file.Unreadable);
    }

    [Fact]
    public void A_value_of_any_other_shape_is_unreadable_and_written_back_as_it_was()
    {
        var file = KeymapFile.FromJson(Doc("""{"F1": 7, "F2": {"key": "PF2"}, "F3": ["PF3"], "F4": {"text": "x", "more": 1}, "F5": "PF5"}"""));

        Assert.Equal(["F5"], file.Bindings.Keys);
        Assert.Equal(["F1", "F2", "F3", "F4"], file.Unreadable.Keys.Order());
        var written = file.ToJson()["bindings"]!.AsObject();
        Assert.Equal("7", written["F1"]!.ToJsonString());
        Assert.Equal("""{"key":"PF2"}""", written["F2"]!.ToJsonString());
        Assert.Equal("""["PF3"]""", written["F3"]!.ToJsonString());
        Assert.Equal("PF5", written["F5"]!.GetValue<string>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("{}")]
    [InlineData("""{"bindings": 5}""")]
    [InlineData("""{"bindings": ["F1"]}""")]
    [InlineData("""{"other": {}}""")]
    public void No_bindings_object_is_an_empty_file(string? json)
    {
        var document = json is null ? null : JsonNode.Parse(json)!.AsObject();

        var file = KeymapFile.FromJson(document);

        Assert.Empty(file.Bindings);
        Assert.Empty(file.Unreadable);
    }

    [Fact]
    public void ToJson_then_FromJson_round_trips_and_writes_chords_in_order()
    {
        var file = new KeymapFile(
            [
                KeyValuePair.Create("Tap:LeftCtrl", (KeymapEntry)new KeymapEntry.SendKey("Enter")),
                KeyValuePair.Create("Ctrl+D6", (KeymapEntry)new KeymapEntry.TypeText("¢")),
                KeyValuePair.Create("Alt+2", (KeymapEntry)KeymapEntry.Unbound.Instance),
            ],
            [KeyValuePair.Create("Zzz", (JsonNode?)JsonValue.Create(1))]);

        var json = file.ToJson();
        var back = KeymapFile.FromJson(json);

        Assert.Equal(["Alt+2", "Ctrl+D6", "Tap:LeftCtrl", "Zzz"], json["bindings"]!.AsObject().Select(p => p.Key));
        Assert.Equal(file.Bindings.OrderBy(b => b.Key), back.Bindings.OrderBy(b => b.Key));
        Assert.Equal("1", back.Unreadable["Zzz"]!.ToJsonString());
        Assert.Null(json["bindings"]!["Alt+2"]);
    }

    [Fact]
    public void Empty_writes_an_empty_bindings_object()
    {
        Assert.Equal("""{"bindings":{}}""", KeymapFile.Empty.ToJson().ToJsonString());
    }
}
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~KeymapFileTests"`
Expected: build FAILS, `KeymapFile` and `KeymapEntry` do not exist.

- [ ] **Step 3: Create `KeymapEntry.cs`**

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

namespace LizTerm.Core.Settings;

/// <summary>One binding's value in keymap.json, as strings: Core knows neither Avalonia's key names nor the 3270
/// keys (LizTerm.App reads a SendKey's name into TerminalKey). SendKey names a 3270 key, TypeText is text to type,
/// and Unbound takes the chord away from the default (editable keymap spec §3.1).</summary>
public abstract record KeymapEntry
{
    private KeymapEntry() { }

    public sealed record SendKey(string KeyName) : KeymapEntry;

    public sealed record TypeText(string Text) : KeymapEntry;

    public sealed record Unbound : KeymapEntry
    {
        public static readonly Unbound Instance = new();
    }
}
```

- [ ] **Step 4: Create `KeymapFile.cs`**

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Text.Json.Nodes;

namespace LizTerm.Core.Settings;

/// <summary>keymap.json as a document: <c>{"bindings": {"&lt;chord&gt;": "&lt;key&gt;" | {"text": "…"} | null}}</c>
/// (editable keymap spec §3.1). Chords and key names are strings here; LizTerm.App parses them. A value of any
/// other shape is kept in Unreadable and written back verbatim, so one hand-edited mistake costs one binding and a
/// newer build's value survives an older build saving. Written with the chords in ordinal order, so a file diff is
/// stable. Immutable.</summary>
public sealed class KeymapFile
{
    private const string BindingsKey = "bindings";
    private const string TextKey = "text";

    public static KeymapFile Empty { get; } = new([], []);

    public IReadOnlyDictionary<string, KeymapEntry> Bindings { get; }

    /// <summary>Values this build cannot read, by chord spelling, cloned so the file's document stays untouched.</summary>
    public IReadOnlyDictionary<string, JsonNode?> Unreadable { get; }

    public KeymapFile(IEnumerable<KeyValuePair<string, KeymapEntry>> bindings, IEnumerable<KeyValuePair<string, JsonNode?>> unreadable)
    {
        Bindings = new Dictionary<string, KeymapEntry>(bindings);
        Unreadable = new Dictionary<string, JsonNode?>(unreadable);
    }

    /// <summary>Reads a document. No "bindings" object, or no document at all, is the empty file.</summary>
    public static KeymapFile FromJson(JsonObject? document)
    {
        if (document?[BindingsKey] is not JsonObject entries) return Empty;
        var bindings = new Dictionary<string, KeymapEntry>();
        var unreadable = new Dictionary<string, JsonNode?>();
        foreach (var (chord, value) in entries)
        {
            switch (value)
            {
                case null:
                    bindings[chord] = KeymapEntry.Unbound.Instance;
                    break;
                case JsonValue scalar when scalar.TryGetValue<string>(out var keyName):
                    bindings[chord] = new KeymapEntry.SendKey(keyName);
                    break;
                case JsonObject { Count: 1 } holder when holder[TextKey] is JsonValue textValue && textValue.TryGetValue<string>(out var text):
                    bindings[chord] = new KeymapEntry.TypeText(text);
                    break;
                default:
                    unreadable[chord] = value!.DeepClone();   // the null case is above; flow analysis does not carry it into default
                    break;
            }
        }
        return new(bindings, unreadable);
    }

    public JsonObject ToJson()
    {
        var entries = new JsonObject();
        var spellings = Bindings.Keys.Concat(Unreadable.Keys).Order(StringComparer.Ordinal);
        foreach (var chord in spellings)
        {
            entries[chord] = Bindings.TryGetValue(chord, out var entry)
                ? entry switch
                {
                    KeymapEntry.SendKey send => JsonValue.Create(send.KeyName),
                    KeymapEntry.TypeText type => new JsonObject { [TextKey] = type.Text },
                    _ => null,
                }
                : Unreadable[chord]?.DeepClone();
        }
        return new JsonObject { [BindingsKey] = entries };
    }
}
```

- [ ] **Step 5: Run the tests**

Run: `dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~KeymapFileTests"`
Expected: 5 test methods PASS (the theory counts as one method, five cases).

- [ ] **Step 6: Commit**

```bash
git add src/LizTerm.Core/Settings/KeymapEntry.cs src/LizTerm.Core/Settings/KeymapFile.cs tests/LizTerm.Core.Tests/Settings/KeymapFileTests.cs
git commit -m "Core: keymap.json as a document of strings, unreadable values carried through (#18)

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 3: `KeymapStore` and `AppPaths.KeymapFile`

**Files:**
- Create: `src/LizTerm.Core/Settings/KeymapStore.cs`
- Modify: `src/LizTerm.Core/Profiles/AppPaths.cs` (after `RecentHostsFile()`, line 53)
- Test: `tests/LizTerm.Core.Tests/Settings/KeymapStoreTests.cs`

**Interfaces:**
- Consumes: `KeymapFile`, `JsonFiles`.
- Produces: `sealed class KeymapStore(string filePath)` with `string FilePath`, `static string DefaultFile()`, `KeymapFile Load()`, `KeymapFile Update(Func<KeymapFile, KeymapFile> change)`. `AppPaths.KeymapFile()`.

- [ ] **Step 1: Write the failing tests**

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Text.Json.Nodes;
using LizTerm.Core.Profiles;
using LizTerm.Core.Settings;

namespace LizTerm.Core.Tests.Settings;

public class KeymapStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lizterm-tests-" + Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_dir, "keymap.json");
    private KeymapStore Store => new(FilePath);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private void WriteFile(string content)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, content);
    }

    private JsonObject ReadBindings() => JsonNode.Parse(File.ReadAllText(FilePath))!["bindings"]!.AsObject();

    private static KeymapFile Plus(KeymapFile file, string chord, KeymapEntry entry) =>
        new(file.Bindings.Append(KeyValuePair.Create(chord, entry)), file.Unreadable);

    [Fact]
    public void A_missing_file_loads_empty_and_creates_nothing()
    {
        Assert.Empty(Store.Load().Bindings);
        Assert.False(Directory.Exists(_dir));
    }

    [Fact]
    public void Update_writes_and_Load_reads_it_back()
    {
        Store.Update(f => Plus(f, "Ctrl+Home", new KeymapEntry.SendKey("PA1")));

        Assert.Equal(new KeymapEntry.SendKey("PA1"), Store.Load().Bindings["Ctrl+Home"]);
        Assert.Equal("PA1", ReadBindings()["Ctrl+Home"]!.GetValue<string>());
    }

    [Fact]
    public void Update_applies_the_change_to_what_is_on_disk_now()
    {
        WriteFile("""{"bindings": {"Alt+2": null}}""");

        Store.Update(f => Plus(f, "Ctrl+Home", new KeymapEntry.SendKey("PA1")));

        Assert.Equal(["Alt+2", "Ctrl+Home"], ReadBindings().Select(p => p.Key));
    }

    [Fact]
    public void An_unreadable_entry_survives_an_update()
    {
        WriteFile("""{"bindings": {"F1": 7}}""");

        Store.Update(f => Plus(f, "Ctrl+Home", new KeymapEntry.SendKey("PA1")));

        Assert.Equal("7", ReadBindings()["F1"]!.ToJsonString());
    }

    [Fact]
    public void A_file_that_is_not_json_loads_empty_but_refuses_an_update()
    {
        WriteFile("not json");

        Assert.Empty(Store.Load().Bindings);
        var ex = Assert.Throws<InvalidDataException>(() => Store.Update(f => f));
        Assert.Contains("keymap.json", ex.Message);
        Assert.Equal("not json", File.ReadAllText(FilePath));
    }

    [Fact]
    public void The_default_file_is_keymap_json_beside_settings_json()
    {
        Assert.Equal(Path.Combine(Path.GetDirectoryName(AppPaths.SettingsFile())!, "keymap.json"), KeymapStore.DefaultFile());
    }
}
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~KeymapStoreTests"`
Expected: build FAILS, `KeymapStore` and `AppPaths.KeymapFile` do not exist.

- [ ] **Step 3: Add `AppPaths.KeymapFile()`**

After `RecentHostsFile()` in `AppPaths.cs`:

```csharp
    /// <summary>The user's keyboard bindings, one file beside settings.json (#18). Holds only the chords the user
    /// changed (see LizTerm.Core.Settings.KeymapFile), so deleting it restores every default.</summary>
    public static string KeymapFile() => Path.Combine(ConfigRoot(), "keymap.json");
```

- [ ] **Step 4: Create `KeymapStore.cs`**

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.Core.Profiles;

namespace LizTerm.Core.Settings;

/// <summary>One JSON file of the user's keyboard bindings (#18). The file-backed half of KeymapFile: Load never
/// throws, and Update applies a change to what is on disk <em>now</em>, as SettingsStore.Update does, so two LizTerm
/// processes cannot lose each other's bindings.</summary>
public sealed class KeymapStore(string filePath)
{
    public string FilePath { get; } = filePath;

    public static string DefaultFile() => AppPaths.KeymapFile();

    /// <summary>The bindings on disk. A file that is missing, unreadable, not JSON or not a JSON object is the
    /// empty file; a value that will not read is carried in Unreadable. Nothing is written here.</summary>
    public KeymapFile Load() => KeymapFile.FromJson(JsonFiles.ReadLenient(FilePath));

    /// <summary>Applies <paramref name="change"/> to the file as it is on disk now and writes the result; returns
    /// what it wrote. Throws InvalidDataException, naming the file, when the file exists but is not a JSON object,
    /// rather than replace something the user may be editing by hand. IO and permission errors propagate.</summary>
    public KeymapFile Update(Func<KeymapFile, KeymapFile> change)
    {
        var next = change(KeymapFile.FromJson(JsonFiles.ReadStrict(FilePath)));
        JsonFiles.Write(FilePath, next.ToJson().ToJsonString(JsonFiles.Indented));
        return next;
    }
}
```

- [ ] **Step 5: Run the tests**

Run: `dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~KeymapStoreTests"`
Expected: 6 PASS.

- [ ] **Step 6: Commit**

```bash
git add src/LizTerm.Core/Settings/KeymapStore.cs src/LizTerm.Core/Profiles/AppPaths.cs tests/LizTerm.Core.Tests/Settings/KeymapStoreTests.cs
git commit -m "Core: KeymapStore over keymap.json beside settings.json (#18)

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 4: `KeymapAction` and `ChordSyntax`, the App-side spelling

**Files:**
- Create: `src/LizTerm.App/Keyboard/KeymapAction.cs`
- Create: `src/LizTerm.App/Keyboard/ChordSyntax.cs`
- Test: `tests/LizTerm.App.Tests/Keyboard/KeymapActionTests.cs`, `tests/LizTerm.App.Tests/Keyboard/ChordSyntaxTests.cs`

**Interfaces:**
- Consumes: `KeymapEntry` (Core), `KeyChord` (App).
- Produces: `abstract record KeymapAction` with nested `SendKey(TerminalKey Key)`, `TypeText(string Text)`, `Unbound` (`Unbound.Instance`); `static bool TryFrom(KeymapEntry entry, out KeymapAction? action)`; `KeymapEntry ToEntry()`. `static class ChordSyntax` with `string Format(KeyChord chord)` and `bool TryParse(string text, out KeyChord chord)`.

- [ ] **Step 1: Write the failing tests**

`KeymapActionTests.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.App.Keyboard;
using LizTerm.Core.Session;
using LizTerm.Core.Settings;

namespace LizTerm.App.Tests.Keyboard;

public class KeymapActionTests
{
    [Theory]
    [InlineData("PA1", TerminalKey.PA1)]
    [InlineData("pf13", TerminalKey.PF13)]
    [InlineData("EraseEof", TerminalKey.EraseEof)]
    public void A_3270_key_name_reads_ignoring_case(string name, TerminalKey expected)
    {
        Assert.True(KeymapAction.TryFrom(new KeymapEntry.SendKey(name), out var action));
        Assert.Equal(new KeymapAction.SendKey(expected), action);
    }

    [Theory]
    [InlineData("")]
    [InlineData("3")]
    [InlineData("PF25")]
    [InlineData("Copy")]
    public void A_name_this_build_does_not_know_does_not_read(string name)
    {
        Assert.False(KeymapAction.TryFrom(new KeymapEntry.SendKey(name), out _));
    }

    [Fact]
    public void Text_and_unbound_read_and_empty_text_does_not()
    {
        Assert.True(KeymapAction.TryFrom(new KeymapEntry.TypeText("¬"), out var text));
        Assert.Equal(new KeymapAction.TypeText("¬"), text);
        Assert.True(KeymapAction.TryFrom(KeymapEntry.Unbound.Instance, out var unbound));
        Assert.Same(KeymapAction.Unbound.Instance, unbound);
        Assert.False(KeymapAction.TryFrom(new KeymapEntry.TypeText(""), out _));
    }

    [Fact]
    public void ToEntry_writes_the_key_by_name()
    {
        Assert.Equal(new KeymapEntry.SendKey("PA1"), new KeymapAction.SendKey(TerminalKey.PA1).ToEntry());
        Assert.Equal(new KeymapEntry.TypeText("¢"), new KeymapAction.TypeText("¢").ToEntry());
        Assert.Same(KeymapEntry.Unbound.Instance, KeymapAction.Unbound.Instance.ToEntry());
    }
}
```

`ChordSyntaxTests.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Input;
using LizTerm.App.Keyboard;

namespace LizTerm.App.Tests.Keyboard;

/// <summary>keymap.json's chord spelling (editable keymap spec §3.1): LizTerm's own, case-insensitive in, fixed
/// modifier order out, and never Cmd.</summary>
public class ChordSyntaxTests
{
    public static TheoryData<string, Key, KeyModifiers> Spellings => new()
    {
        { "F1", Key.F1, KeyModifiers.None },
        { "Ctrl+Home", Key.Home, KeyModifiers.Control },
        { "ctrl+shift+f1", Key.F1, KeyModifiers.Control | KeyModifiers.Shift },
        { "Shift+Ctrl+F1", Key.F1, KeyModifiers.Control | KeyModifiers.Shift },
        { " Alt + D1 ", Key.D1, KeyModifiers.Alt },
        { "Ctrl+OemOpenBrackets", Key.OemOpenBrackets, KeyModifiers.Control },
        { "Ctrl+Alt+Shift+A", Key.A, KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Shift },
    };

    [Theory]
    [MemberData(nameof(Spellings))]
    public void Parses_a_chord(string text, Key key, KeyModifiers modifiers)
    {
        Assert.True(ChordSyntax.TryParse(text, out var chord));
        Assert.Equal(new KeyChord(key, modifiers), chord);
    }

    [Theory]
    [InlineData("Tap:LeftCtrl", Key.LeftCtrl)]
    [InlineData("tap:rightctrl", Key.RightCtrl)]
    public void Parses_a_tap(string text, Key key)
    {
        Assert.True(ChordSyntax.TryParse(text, out var chord));
        Assert.Equal(KeyChord.TapOf(key), chord);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Ctrl")]
    [InlineData("Ctrl+")]
    [InlineData("+F1")]
    [InlineData("Cmd+K")]
    [InlineData("Meta+K")]
    [InlineData("Win+K")]
    [InlineData("Ctrl+Ctrl+A")]
    [InlineData("Ctrl+3")]
    [InlineData("Ctrl+None")]
    [InlineData("Ctrl+Nonsense")]
    [InlineData("LeftCtrl")]
    [InlineData("Ctrl+LeftShift")]
    [InlineData("Tap:A")]
    [InlineData("Tap:LeftShift")]
    [InlineData("Tap:")]
    public void Rejects_what_is_not_a_chord(string text)
    {
        Assert.False(ChordSyntax.TryParse(text, out _));
    }

    [Fact]
    public void Formats_modifiers_in_a_fixed_order()
    {
        Assert.Equal("Ctrl+Alt+Shift+A", ChordSyntax.Format(new KeyChord(Key.A, KeyModifiers.Shift | KeyModifiers.Alt | KeyModifiers.Control)));
        Assert.Equal("Home", ChordSyntax.Format(new KeyChord(Key.Home)));
        Assert.Equal("Tap:RightCtrl", ChordSyntax.Format(KeyChord.TapOf(Key.RightCtrl)));
    }

    [Fact]
    public void A_Cmd_chord_formats_but_never_parses()
    {
        var formatted = ChordSyntax.Format(new KeyChord(Key.K, KeyModifiers.Meta));

        Assert.Equal("Cmd+K", formatted);
        Assert.False(ChordSyntax.TryParse(formatted, out _));
    }

    [Fact]
    public void Every_default_chord_round_trips()
    {
        var map = DefaultKeymap.Create(destructiveBackspace: true);
        foreach (var chord in map.Keys.Keys.Concat(map.Text.Keys))
        {
            Assert.True(ChordSyntax.TryParse(ChordSyntax.Format(chord), out var back), ChordSyntax.Format(chord));
            Assert.Equal(chord, back);
        }
    }
}
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~ChordSyntaxTests|FullyQualifiedName~KeymapActionTests"`
Expected: build FAILS, the types do not exist.

- [ ] **Step 3: Create `KeymapAction.cs`**

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Diagnostics.CodeAnalysis;
using LizTerm.Core.Session;
using LizTerm.Core.Settings;

namespace LizTerm.App.Keyboard;

/// <summary>What a chord does once the file's strings are read (editable keymap spec §3.2): send a 3270 key, type
/// text, or nothing, the chord having been taken away from the default. The typed twin of KeymapEntry.</summary>
public abstract record KeymapAction
{
    private KeymapAction() { }

    public sealed record SendKey(TerminalKey Key) : KeymapAction;

    public sealed record TypeText(string Text) : KeymapAction;

    public sealed record Unbound : KeymapAction
    {
        public static readonly Unbound Instance = new();
    }

    /// <summary>Reads a file entry. False for a 3270 key name this build does not know (a newer build's, or a
    /// typo; the caller keeps the entry verbatim) and for empty text.</summary>
    public static bool TryFrom(KeymapEntry entry, [NotNullWhen(true)] out KeymapAction? action)
    {
        action = entry switch
        {
            KeymapEntry.SendKey send when TryKey(send.KeyName, out var key) => new SendKey(key),
            KeymapEntry.TypeText type when type.Text.Length > 0 => new TypeText(type.Text),
            KeymapEntry.Unbound => Unbound.Instance,
            _ => null,
        };
        return action is not null;
    }

    public KeymapEntry ToEntry() => this switch
    {
        SendKey send => new KeymapEntry.SendKey(send.Key.ToString()),
        TypeText type => new KeymapEntry.TypeText(type.Text),
        _ => KeymapEntry.Unbound.Instance,
    };

    /// <summary>By name only: Enum.TryParse would also accept "3", which no one wrote on purpose.</summary>
    private static bool TryKey(string name, out TerminalKey key)
    {
        key = default;
        return name.Length > 0 && !char.IsDigit(name[0])
               && Enum.TryParse(name, ignoreCase: true, out key) && Enum.IsDefined(key);
    }
}
```

- [ ] **Step 4: Create `ChordSyntax.cs`**

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Input;

namespace LizTerm.App.Keyboard;

/// <summary>keymap.json's spelling of a chord (editable keymap spec §3.1): the modifiers in the fixed order Ctrl,
/// Alt, Shift joined with "+" to an Avalonia Key name (Ctrl+Shift+F1, Ctrl+OemOpenBrackets), or "Tap:" and a Ctrl
/// key (Tap:LeftCtrl). Case does not matter on the way in. LizTerm's own rather than KeyGesture.Parse: Avalonia's
/// spelling is platform-flavoured and admits Cmd, which KeymapPolicy refuses, so a Meta chord formats (as Cmd, for
/// a message) but never parses.</summary>
public static class ChordSyntax
{
    private const string TapPrefix = "Tap:";

    public static string Format(KeyChord chord)
    {
        if (chord.Tap) return TapPrefix + chord.Key;
        var parts = new List<string>(4);
        if (chord.Modifiers.HasFlag(KeyModifiers.Control)) parts.Add("Ctrl");
        if (chord.Modifiers.HasFlag(KeyModifiers.Alt)) parts.Add("Alt");
        if (chord.Modifiers.HasFlag(KeyModifiers.Shift)) parts.Add("Shift");
        if (chord.Modifiers.HasFlag(KeyModifiers.Meta)) parts.Add("Cmd");
        parts.Add(chord.Key.ToString());
        return string.Join('+', parts);
    }

    public static bool TryParse(string text, out KeyChord chord)
    {
        chord = default;
        var trimmed = text.Trim();
        if (trimmed.StartsWith(TapPrefix, StringComparison.OrdinalIgnoreCase))
        {
            if (!TryKey(trimmed[TapPrefix.Length..], out var tapped) || !IsTappable(tapped)) return false;
            chord = KeyChord.TapOf(tapped);
            return true;
        }

        var parts = trimmed.Split('+');
        var modifiers = KeyModifiers.None;
        for (var i = 0; i < parts.Length - 1; i++)
        {
            var modifier = parts[i].Trim().ToLowerInvariant() switch
            {
                "ctrl" => KeyModifiers.Control,
                "alt" => KeyModifiers.Alt,
                "shift" => KeyModifiers.Shift,
                _ => KeyModifiers.None,
            };
            if (modifier == KeyModifiers.None || modifiers.HasFlag(modifier)) return false;
            modifiers |= modifier;
        }
        if (!TryKey(parts[^1].Trim(), out var key) || IsModifierKey(key)) return false;
        chord = new KeyChord(key, modifiers);
        return true;
    }

    /// <summary>A Key by name: defined, not None, and not a number (Enum.TryParse would accept "3").</summary>
    private static bool TryKey(string name, out Key key)
    {
        key = Key.None;
        return name.Length > 0 && !char.IsDigit(name[0])
               && Enum.TryParse(name, ignoreCase: true, out key) && Enum.IsDefined(key) && key != Key.None;
    }

    /// <summary>The keys ModifierTapDetector watches; a tap of anything else is not a chord.</summary>
    private static bool IsTappable(Key key) => key is Key.LeftCtrl or Key.RightCtrl;

    private static bool IsModifierKey(Key key) =>
        key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin;
}
```

- [ ] **Step 5: Run the tests**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~ChordSyntaxTests|FullyQualifiedName~KeymapActionTests"`
Expected: all PASS. Avalonia's `Key.Enter` and `Key.Return` share one value, as do `Oem4` and `OemOpenBrackets`, so `Format` prints whichever name `Enum.ToString` picks; `TryParse` reads either back to the same value, which is what the round-trip test asserts.

- [ ] **Step 6: Commit**

```bash
git add src/LizTerm.App/Keyboard/KeymapAction.cs src/LizTerm.App/Keyboard/ChordSyntax.cs tests/LizTerm.App.Tests/Keyboard/KeymapActionTests.cs tests/LizTerm.App.Tests/Keyboard/ChordSyntaxTests.cs
git commit -m "App: the keymap file's chord spelling and typed actions (#18)

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 5: `Keymap.Without` and `KeymapOverlay`

**Files:**
- Modify: `src/LizTerm.App/Keyboard/Keymap.cs` (add `Without`; rewrite the class doc comment)
- Modify: `src/LizTerm.App/Keyboard/DefaultKeymap.cs` (doc comment's last sentence only)
- Create: `src/LizTerm.App/Keyboard/KeymapOverlay.cs`
- Test: `tests/LizTerm.App.Tests/Keyboard/KeymapOverlayTests.cs` (and one new fact in the existing `KeymapTests.cs`)

**Interfaces:**
- Consumes: `KeymapFile`, `KeymapEntry` (Core); `ChordSyntax`, `KeymapAction`, `Keymap`, `DefaultKeymap`.
- Produces: `Keymap Keymap.Without(IEnumerable<KeyChord> chords)`. `sealed class KeymapOverlay` with `static KeymapOverlay Empty`, `IReadOnlyDictionary<KeyChord, KeymapAction> Entries`, `KeymapFile Ignored`, `int IgnoredCount`, `static KeymapOverlay Parse(KeymapFile file)`, `KeymapFile ToFile()`, `KeymapOverlay Bind(KeyChord chord, KeymapAction action)`, `KeymapOverlay Unbind(KeyChord chord)`, `KeymapOverlay Cleared()`, `Keymap Compose(bool destructiveBackspace)`.

- [ ] **Step 1: Write the failing tests**

Add to `KeymapTests.cs`, after the existing `With` test (the class already has `Map` and the usings it needs):

```csharp
    [Fact]
    public void Without_removes_a_chord_from_both_tables()
    {
        var map = Map.Without([new KeyChord(Key.Home, KeyModifiers.Control), new KeyChord(Key.OemOpenBrackets, KeyModifiers.Control)]);

        Assert.False(map.TryMap(new KeyChord(Key.Home, KeyModifiers.Control), out _));
        Assert.False(map.TryText(new KeyChord(Key.OemOpenBrackets, KeyModifiers.Control), out _));
        Assert.True(map.TryMap(new KeyChord(Key.D2, KeyModifiers.Alt), out var pa2));
        Assert.Equal(TerminalKey.PA2, pa2);
    }
```

`KeymapOverlayTests.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Text.Json.Nodes;
using Avalonia.Input;
using LizTerm.App.Keyboard;
using LizTerm.Core.Session;
using LizTerm.Core.Settings;

namespace LizTerm.App.Tests.Keyboard;

/// <summary>The user's differences from the default (editable keymap spec §3.1–3.3): parsed, sparse, composed.</summary>
public class KeymapOverlayTests
{
    private static readonly KeyChord CtrlHome = new(Key.Home, KeyModifiers.Control);
    private static readonly KeyChord CtrlOpenBracket = new(Key.OemOpenBrackets, KeyModifiers.Control);
    private static readonly KeyChord Backspace = new(Key.Back);
    private static readonly KeymapAction Pa1 = new KeymapAction.SendKey(TerminalKey.PA1);
    private static readonly KeymapAction Pa2 = new KeymapAction.SendKey(TerminalKey.PA2);

    private static KeymapFile File(string bindings) =>
        KeymapFile.FromJson(JsonNode.Parse("{\"bindings\": " + bindings + "}")!.AsObject());

    [Fact]
    public void Parse_keeps_what_it_can_read_and_carries_the_rest()
    {
        var overlay = KeymapOverlay.Parse(File("""{"ctrl+home": "PA1", "Cmd+K": "PA1", "F1": "PF99", "F2": 7}"""));

        Assert.Equal(Pa1, overlay.Entries[CtrlHome]);
        Assert.Single(overlay.Entries);
        Assert.Equal(["Cmd+K", "F1"], overlay.Ignored.Bindings.Keys.Order());
        Assert.Equal(["F2"], overlay.Ignored.Unreadable.Keys);
        Assert.Equal(3, overlay.IgnoredCount);
    }

    [Fact]
    public void ToFile_writes_its_entries_in_the_canonical_spelling_and_the_ignored_ones_as_they_were()
    {
        var overlay = KeymapOverlay.Parse(File("""{"ctrl+home": "PA1", "F1": "PF99", "F2": 7}"""));

        var file = overlay.ToFile();

        Assert.Equal(new KeymapEntry.SendKey("PA1"), file.Bindings["Ctrl+Home"]);
        Assert.Equal(new KeymapEntry.SendKey("PF99"), file.Bindings["F1"]);
        Assert.Equal("7", file.Unreadable["F2"]!.ToJsonString());
        Assert.Equal(2, file.Bindings.Count);
    }

    [Fact]
    public void Binding_a_chord_to_its_default_leaves_no_entry()
    {
        var overlay = KeymapOverlay.Empty.Bind(CtrlHome, Pa1).Bind(CtrlHome, Pa2);

        Assert.Empty(overlay.Entries);
    }

    [Fact]
    public void Unbinding_a_default_is_kept_and_unbinding_a_chord_the_default_lacks_is_not()
    {
        var overlay = KeymapOverlay.Empty.Unbind(CtrlHome).Unbind(new KeyChord(Key.X, KeyModifiers.Control));

        Assert.Same(KeymapAction.Unbound.Instance, Assert.Single(overlay.Entries).Value);
        Assert.Equal(CtrlHome, overlay.Entries.Keys.Single());
    }

    [Fact]
    public void Backspace_is_kept_even_at_the_erasing_default()
    {
        var overlay = KeymapOverlay.Empty.Bind(Backspace, new KeymapAction.SendKey(TerminalKey.Erase));

        Assert.Equal(new KeymapAction.SendKey(TerminalKey.Erase), overlay.Entries[Backspace]);
        Assert.Equal(TerminalKey.Erase, Send(overlay.Compose(destructiveBackspace: false), Backspace));
    }

    [Fact]
    public void Without_a_Backspace_entry_the_profile_decides()
    {
        Assert.Equal(TerminalKey.Backspace, Send(KeymapOverlay.Empty.Compose(destructiveBackspace: false), Backspace));
        Assert.Equal(TerminalKey.Erase, Send(KeymapOverlay.Empty.Compose(destructiveBackspace: true), Backspace));
    }

    [Fact]
    public void Compose_applies_a_binding_an_unbinding_and_a_move_from_text_to_key()
    {
        var overlay = KeymapOverlay.Empty
            .Bind(CtrlHome, Pa1)
            .Unbind(new KeyChord(Key.D2, KeyModifiers.Alt))
            .Bind(CtrlOpenBracket, Pa1);

        var map = overlay.Compose(destructiveBackspace: true);

        Assert.Equal(TerminalKey.PA1, Send(map, CtrlHome));
        Assert.False(map.TryMap(new KeyChord(Key.D2, KeyModifiers.Alt), out _));
        Assert.Equal(TerminalKey.PA1, Send(map, CtrlOpenBracket));
        Assert.False(map.TryText(CtrlOpenBracket, out _));
    }

    [Fact]
    public void A_text_binding_replaces_a_key_and_a_text_default_stays_sparse()
    {
        var overlay = KeymapOverlay.Empty
            .Bind(CtrlHome, new KeymapAction.TypeText("€"))
            .Bind(CtrlOpenBracket, new KeymapAction.TypeText("¬"));

        Assert.Single(overlay.Entries);
        var map = overlay.Compose(destructiveBackspace: true);
        Assert.False(map.TryMap(CtrlHome, out _));
        Assert.True(map.TryText(CtrlHome, out var text));
        Assert.Equal("€", text);
    }

    [Fact]
    public void Cleared_drops_everything_ignored_entries_included()
    {
        var overlay = KeymapOverlay.Parse(File("""{"Ctrl+Home": "PA1", "F2": 7}""")).Cleared();

        Assert.Empty(overlay.Entries);
        Assert.Equal(0, overlay.IgnoredCount);
        Assert.Equal("""{"bindings":{}}""", overlay.ToFile().ToJson().ToJsonString());
    }

    private static TerminalKey Send(Keymap map, KeyChord chord)
    {
        Assert.True(map.TryMap(chord, out var key));
        return key;
    }
}
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~KeymapOverlayTests|FullyQualifiedName~KeymapTests"`
Expected: build FAILS, `Without` and `KeymapOverlay` do not exist.

- [ ] **Step 3: Add `Without` to `Keymap.cs` and update its doc comment**

Replace the class summary with:

```csharp
/// <summary>A chord-to-key table plus a chord-to-text table (spec 6.1). Immutable. KeymapOverlay applies the user's
/// keymap.json over the default with <see cref="Without"/> then <see cref="With"/> (#18).</summary>
```

After `With`, add:

```csharp
    /// <summary>A copy with the given chords removed from both tables, so a user's unbinding takes a default away
    /// rather than shadowing it, and a chord can move from one table to the other.</summary>
    public Keymap Without(IEnumerable<KeyChord> chords)
    {
        var keys = new Dictionary<KeyChord, TerminalKey>(_keys);
        var text = new Dictionary<KeyChord, string>(_text);
        foreach (var chord in chords)
        {
            keys.Remove(chord);
            text.Remove(chord);
        }
        return new Keymap(keys, text);
    }
```

In `DefaultKeymap.cs`, replace the summary's last sentence `Not user-editable yet; see <see cref="Keymap.With"/>.` with `The user's keymap.json is applied over this table by <see cref="KeymapOverlay"/> (#18).`

- [ ] **Step 4: Create `KeymapOverlay.cs`**

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Input;
using LizTerm.Core.Session;
using LizTerm.Core.Settings;

namespace LizTerm.App.Keyboard;

/// <summary>The user's differences from the default (editable keymap spec §3), parsed: each chord to the action it
/// now has, or to Unbound. Immutable; Bind and Unbind answer a new overlay, pruned so the file stays sparse.
/// Entries this build cannot read (a chord that does not parse, a 3270 key name it does not know) ride along in
/// Ignored and go back to the file verbatim, the rule KeymapFile applies to values of the wrong JSON shape. Two
/// spellings of one chord in a file ("ctrl+home" and "Ctrl+Home") are one entry, the later winning.</summary>
public sealed class KeymapOverlay
{
    /// <summary>Sparseness is measured against this table (§3.1). Back is exempt because its default is the
    /// profile's choice, so a binding of it is a choice too, whichever value it has (§3.3).</summary>
    private static readonly Keymap Baseline = DefaultKeymap.Create(destructiveBackspace: true);
    private static readonly KeyChord Backspace = new(Key.Back);

    public static KeymapOverlay Empty { get; } = new(new Dictionary<KeyChord, KeymapAction>(), KeymapFile.Empty);

    public IReadOnlyDictionary<KeyChord, KeymapAction> Entries { get; }

    /// <summary>The file's entries this build left alone: unparsable chords and key names in Bindings, values of
    /// the wrong shape in Unreadable. Written back as they were.</summary>
    public KeymapFile Ignored { get; }

    public int IgnoredCount => Ignored.Bindings.Count + Ignored.Unreadable.Count;

    private KeymapOverlay(IReadOnlyDictionary<KeyChord, KeymapAction> entries, KeymapFile ignored)
    {
        Entries = entries;
        Ignored = ignored;
    }

    public static KeymapOverlay Parse(KeymapFile file)
    {
        var entries = new Dictionary<KeyChord, KeymapAction>();
        var ignored = new Dictionary<string, KeymapEntry>();
        foreach (var (spelling, entry) in file.Bindings)
        {
            if (ChordSyntax.TryParse(spelling, out var chord) && KeymapAction.TryFrom(entry, out var action))
                entries[chord] = action;
            else
                ignored[spelling] = entry;
        }
        return new(entries, new KeymapFile(ignored, file.Unreadable));
    }

    /// <summary>Entries in the canonical spelling, then the ignored ones. The two cannot collide: an ignored
    /// spelling is one that did not parse, and a canonical one always does.</summary>
    public KeymapFile ToFile() =>
        new(Entries.Select(e => KeyValuePair.Create(ChordSyntax.Format(e.Key), e.Value.ToEntry())).Concat(Ignored.Bindings),
            Ignored.Unreadable);

    /// <summary>With <paramref name="chord"/> doing <paramref name="action"/>; an entry that says what the
    /// baseline already says is dropped instead, Back excepted.</summary>
    public KeymapOverlay Bind(KeyChord chord, KeymapAction action)
    {
        var entries = new Dictionary<KeyChord, KeymapAction>(Entries);
        if (chord == Backspace || !MatchesBaseline(chord, action)) entries[chord] = action;
        else entries.Remove(chord);
        return new(entries, Ignored);
    }

    public KeymapOverlay Unbind(KeyChord chord) => Bind(chord, KeymapAction.Unbound.Instance);

    /// <summary>Everything gone, Ignored included: Reset to defaults is the user throwing the file away.</summary>
    public KeymapOverlay Cleared() => Empty;

    /// <summary>The map in force for a window: the profile's default, less every chord the user touched, plus the
    /// user's bindings. Removing first is what lets a chord move from the text table to the key table.</summary>
    public Keymap Compose(bool destructiveBackspace) =>
        DefaultKeymap.Create(destructiveBackspace)
            .Without(Entries.Keys)
            .With(
                Entries.Where(e => e.Value is KeymapAction.SendKey)
                    .Select(e => KeyValuePair.Create(e.Key, ((KeymapAction.SendKey)e.Value).Key)),
                Entries.Where(e => e.Value is KeymapAction.TypeText)
                    .Select(e => KeyValuePair.Create(e.Key, ((KeymapAction.TypeText)e.Value).Text)));

    private static bool MatchesBaseline(KeyChord chord, KeymapAction action) => action switch
    {
        KeymapAction.SendKey send => Baseline.TryMap(chord, out var key) && key == send.Key,
        KeymapAction.TypeText type => Baseline.TryText(chord, out var text) && text == type.Text,
        _ => !Baseline.TryMap(chord, out _) && !Baseline.TryText(chord, out _),
    };
}
```

- [ ] **Step 5: Run the tests**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~KeymapOverlayTests|FullyQualifiedName~KeymapTests"`
Expected: all PASS.

- [ ] **Step 6: Commit**

```bash
git add src/LizTerm.App/Keyboard/Keymap.cs src/LizTerm.App/Keyboard/DefaultKeymap.cs src/LizTerm.App/Keyboard/KeymapOverlay.cs tests/LizTerm.App.Tests/Keyboard/KeymapTests.cs tests/LizTerm.App.Tests/Keyboard/KeymapOverlayTests.cs
git commit -m "App: KeymapOverlay, the user's sparse differences composed over the profile's default (#18)

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 6: `KeymapPolicy`, what the tab will refuse

**Files:**
- Create: `src/LizTerm.App/Keyboard/KeymapPolicy.cs`
- Test: `tests/LizTerm.App.Tests/Keyboard/KeymapPolicyTests.cs`

**Interfaces:**
- Consumes: `KeyChord`; Avalonia's `KeyGesture`, `PlatformHotkeyConfiguration`.
- Produces: `sealed record PlatformHotkeys(IReadOnlyList<KeyGesture> Copy, IReadOnlyList<KeyGesture> Paste, IReadOnlyList<KeyGesture> SelectAll, KeyModifiers CommandModifiers)` with `static PlatformHotkeys Fallback`, `static PlatformHotkeys MacOS`, `static PlatformHotkeys From(PlatformHotkeyConfiguration? configuration)`. `abstract record KeymapVerdict` with nested `Allowed` (`Allowed.Instance`) and `Refused(string Reason)`. `static class KeymapPolicy` with `KeymapVerdict Check(KeyChord chord, PlatformHotkeys hotkeys)`.

- [ ] **Step 1: Write the failing tests**

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Input;
using LizTerm.App.Keyboard;

namespace LizTerm.App.Tests.Keyboard;

/// <summary>Every row of the refusal table (editable keymap spec §4), on a Windows-shaped platform (Fallback: Ctrl
/// everywhere) and a macOS-shaped one (Cmd everywhere, Ctrl+Insert still Copy).</summary>
public class KeymapPolicyTests
{
    private const KeyModifiers CtrlShift = KeyModifiers.Control | KeyModifiers.Shift;

    public static TheoryData<string, Key, KeyModifiers, string> Refusals => new()
    {
        { "windows", Key.C, KeyModifiers.Control, "LizTerm uses this for Copy" },
        { "windows", Key.V, KeyModifiers.Control, "LizTerm uses this for Paste" },
        { "windows", Key.A, KeyModifiers.Control, "LizTerm uses this for Select All" },
        { "windows", Key.F, KeyModifiers.Control, "LizTerm uses this for Find" },
        { "windows", Key.K, KeyModifiers.Control, "LizTerm uses this for Switch Session" },
        { "windows", Key.A, KeyModifiers.Meta, "The system sees Windows key shortcuts before the screen does" },
        { "mac", Key.C, KeyModifiers.Meta, "LizTerm uses this for Copy" },
        { "mac", Key.Insert, KeyModifiers.Control, "LizTerm uses this for Copy" },
        { "mac", Key.F, KeyModifiers.Meta, "LizTerm uses this for Find" },
        { "mac", Key.K, KeyModifiers.Meta, "LizTerm uses this for Switch Session" },
        { "mac", Key.OemComma, KeyModifiers.Meta, "The menu bar sees Cmd shortcuts before the screen does" },
        { "mac", Key.Q, KeyModifiers.Meta | KeyModifiers.Shift, "The menu bar sees Cmd shortcuts before the screen does" },
        { "windows", Key.A, KeyModifiers.None, "This would take away typing that character" },
        { "windows", Key.A, KeyModifiers.Shift, "This would take away typing that character" },
        { "windows", Key.D1, KeyModifiers.None, "This would take away typing that character" },
        { "windows", Key.Space, KeyModifiers.None, "This would take away typing that character" },
        { "windows", Key.OemOpenBrackets, KeyModifiers.Shift, "This would take away typing that character" },
        { "windows", Key.NumPad5, KeyModifiers.None, "This would take away typing that character" },
        { "mac", Key.OemMinus, KeyModifiers.None, "This would take away typing that character" },
    };

    [Theory]
    [MemberData(nameof(Refusals))]
    public void Refuses_with_the_reason_the_tab_shows(string platform, Key key, KeyModifiers modifiers, string reason)
    {
        var verdict = KeymapPolicy.Check(new KeyChord(key, modifiers), Hotkeys(platform));

        Assert.Equal(new KeymapVerdict.Refused(reason), verdict);
    }

    public static TheoryData<string, Key, KeyModifiers> Allowed => new()
    {
        { "windows", Key.F1, KeyModifiers.None },
        { "windows", Key.Escape, KeyModifiers.None },
        { "windows", Key.Insert, KeyModifiers.None },
        { "windows", Key.D1, KeyModifiers.Alt },
        { "windows", Key.A, KeyModifiers.Alt },
        { "windows", Key.R, KeyModifiers.Control },
        { "windows", Key.OemOpenBrackets, KeyModifiers.Control },
        { "windows", Key.A, CtrlShift },
        { "mac", Key.C, KeyModifiers.Control },
        { "mac", Key.A, KeyModifiers.Control },
        { "mac", Key.F, KeyModifiers.Control },
        { "mac", Key.K, KeyModifiers.Control },
        { "mac", Key.Home, KeyModifiers.Control },
    };

    [Theory]
    [MemberData(nameof(Allowed))]
    public void Allows_the_rest(string platform, Key key, KeyModifiers modifiers)
    {
        Assert.Same(KeymapVerdict.Allowed.Instance, KeymapPolicy.Check(new KeyChord(key, modifiers), Hotkeys(platform)));
    }

    [Fact]
    public void A_tap_is_always_allowed()
    {
        Assert.Same(KeymapVerdict.Allowed.Instance, KeymapPolicy.Check(KeyChord.TapOf(Key.LeftCtrl), PlatformHotkeys.Fallback));
        Assert.Same(KeymapVerdict.Allowed.Instance, KeymapPolicy.Check(KeyChord.TapOf(Key.RightCtrl), PlatformHotkeys.MacOS));
    }

    [Fact]
    public void A_missing_platform_configuration_is_the_screens_fallback()
    {
        Assert.Same(PlatformHotkeys.Fallback, PlatformHotkeys.From(null));
    }

    private static PlatformHotkeys Hotkeys(string platform) => platform == "mac" ? PlatformHotkeys.MacOS : PlatformHotkeys.Fallback;
}
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~KeymapPolicyTests"`
Expected: build FAILS.

- [ ] **Step 3: Create `KeymapPolicy.cs`**

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Input;
using Avalonia.Input.Platform;

namespace LizTerm.App.Keyboard;

/// <summary>The platform gestures TerminalScreen checks ahead of the keymap, as data, so the policy is the same
/// pure function on a Mac, on Windows and in a test.</summary>
public sealed record PlatformHotkeys(
    IReadOnlyList<KeyGesture> Copy,
    IReadOnlyList<KeyGesture> Paste,
    IReadOnlyList<KeyGesture> SelectAll,
    KeyModifiers CommandModifiers)
{
    /// <summary>What TerminalScreen.Matches falls back to when the platform answers nothing: Ctrl+C, Ctrl+V,
    /// Ctrl+A, and Ctrl as the command modifier. Windows- and Linux-shaped.</summary>
    public static PlatformHotkeys Fallback { get; } = new(
        [new KeyGesture(Key.C, KeyModifiers.Control)],
        [new KeyGesture(Key.V, KeyModifiers.Control)],
        [new KeyGesture(Key.A, KeyModifiers.Control)],
        KeyModifiers.Control);

    /// <summary>What macOS answers, for tests: Cmd on everything, and Ctrl+Insert still a Copy gesture (which is
    /// why PA1 is not on it; see DefaultKeymap).</summary>
    public static PlatformHotkeys MacOS { get; } = new(
        [new KeyGesture(Key.C, KeyModifiers.Meta), new KeyGesture(Key.Insert, KeyModifiers.Control)],
        [new KeyGesture(Key.V, KeyModifiers.Meta)],
        [new KeyGesture(Key.A, KeyModifiers.Meta)],
        KeyModifiers.Meta);

    public static PlatformHotkeys From(PlatformHotkeyConfiguration? configuration) =>
        configuration is null
            ? Fallback
            : new(configuration.Copy, configuration.Paste, configuration.SelectAll, configuration.CommandModifiers);
}

/// <summary>The answer to "may this chord be bound": Allowed, or Refused with the reason the tab shows verbatim.</summary>
public abstract record KeymapVerdict
{
    private KeymapVerdict() { }

    public sealed record Allowed : KeymapVerdict
    {
        public static readonly Allowed Instance = new();
    }

    public sealed record Refused(string Reason) : KeymapVerdict;
}

/// <summary>Editable keymap spec §4: what the Keyboard tab will not bind. The platform gestures the screen checks
/// first, because a binding there would never fire; any Cmd or Windows-key chord, because the menu bar or the
/// system sees it first; and a printable key with no modifier or Shift alone, because there would be no way to
/// type that character afterwards. Everything else is allowed: a chord another action holds moves (the tab says
/// from where), and unbinding a default is silent. "Printable" is decided by the Key value alone, not by asking
/// the platform what it would type: the answer has to be the same in a test as on a Mac.</summary>
public static class KeymapPolicy
{
    private static readonly HashSet<Key> Printable =
    [
        .. Enumerable.Range((int)Key.A, 26).Select(i => (Key)i),
        .. Enumerable.Range((int)Key.D0, 10).Select(i => (Key)i),
        .. Enumerable.Range((int)Key.NumPad0, 10).Select(i => (Key)i),
        Key.Space, Key.Decimal, Key.Add, Key.Subtract, Key.Multiply, Key.Divide,
        Key.OemSemicolon, Key.OemPlus, Key.OemComma, Key.OemMinus, Key.OemPeriod, Key.OemQuestion, Key.OemTilde,
        Key.OemOpenBrackets, Key.OemPipe, Key.OemCloseBrackets, Key.OemQuotes, Key.Oem8, Key.OemBackslash,
    ];

    public static KeymapVerdict Check(KeyChord chord, PlatformHotkeys hotkeys)
    {
        if (chord.Tap) return KeymapVerdict.Allowed.Instance;
        if (Uses(hotkeys.Copy, chord)) return Reserved("Copy");
        if (Uses(hotkeys.Paste, chord)) return Reserved("Paste");
        if (Uses(hotkeys.SelectAll, chord)) return Reserved("Select All");
        if (chord.Modifiers == hotkeys.CommandModifiers && chord.Key == Key.F) return Reserved("Find");
        if (chord.Modifiers == hotkeys.CommandModifiers && chord.Key == Key.K) return Reserved("Switch Session");
        if (chord.Modifiers.HasFlag(KeyModifiers.Meta))
        {
            return new KeymapVerdict.Refused(hotkeys.CommandModifiers.HasFlag(KeyModifiers.Meta)
                ? "The menu bar sees Cmd shortcuts before the screen does"
                : "The system sees Windows key shortcuts before the screen does");
        }
        if (Printable.Contains(chord.Key) && chord.Modifiers is KeyModifiers.None or KeyModifiers.Shift)
            return new KeymapVerdict.Refused("This would take away typing that character");
        return KeymapVerdict.Allowed.Instance;
    }

    private static KeymapVerdict Reserved(string use) => new KeymapVerdict.Refused($"LizTerm uses this for {use}");

    private static bool Uses(IReadOnlyList<KeyGesture> gestures, KeyChord chord) =>
        gestures.Any(gesture => gesture.Key == chord.Key && gesture.KeyModifiers == chord.Modifiers);
}
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~KeymapPolicyTests"`
Expected: all PASS.

- [ ] **Step 5: Commit**

```bash
git add src/LizTerm.App/Keyboard/KeymapPolicy.cs tests/LizTerm.App.Tests/Keyboard/KeymapPolicyTests.cs
git commit -m "App: KeymapPolicy, the chords a keymap editor refuses and why (#18)

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 7: `KeymapViewModel`, the process's one live keymap

**Files:**
- Create: `src/LizTerm.App/ViewModels/KeymapViewModel.cs`
- Test: `tests/LizTerm.App.Tests/ViewModels/KeymapViewModelTests.cs`

**Interfaces:**
- Consumes: `KeymapStore`, `KeymapOverlay`, `KeymapAction`, `KeyChord`, `Keymap`.
- Produces: `sealed class KeymapViewModel : ObservableObject` with constructors `()` (in-memory) and `(KeymapStore store)`; `KeymapOverlay Overlay`; `event EventHandler? Changed`; `event EventHandler<string>? SaveFailed`; `string? LastSaveError`; `int UnreadableEntries`; `Keymap Compose(bool destructiveBackspace)`; `IReadOnlyList<KeyChord> ChordsFor(KeymapAction action)`; `KeymapAction? ActionOf(KeyChord chord)`; `void Bind(KeyChord chord, KeymapAction action)`; `void Unbind(KeyChord chord)`; `void ResetToDefaults()`.

- [ ] **Step 1: Write the failing tests**

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Input;
using LizTerm.App.Keyboard;
using LizTerm.App.ViewModels;
using LizTerm.Core.Session;
using LizTerm.Core.Settings;

namespace LizTerm.App.Tests.ViewModels;

/// <summary>Editable keymap spec §5.1: applies in memory first, raises Changed, then writes through.</summary>
public class KeymapViewModelTests : IDisposable
{
    private static readonly KeyChord CtrlHome = new(Key.Home, KeyModifiers.Control);
    private static readonly KeymapAction Pa1 = new KeymapAction.SendKey(TerminalKey.PA1);

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lizterm-tests-" + Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_dir, "keymap.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Bind_raises_Changed_and_the_composed_map_follows()
    {
        var vm = new KeymapViewModel();
        var changes = 0;
        vm.Changed += (_, _) => changes++;

        vm.Bind(CtrlHome, Pa1);

        Assert.Equal(1, changes);
        Assert.True(vm.Compose(destructiveBackspace: true).TryMap(CtrlHome, out var key));
        Assert.Equal(TerminalKey.PA1, key);
    }

    [Fact]
    public void Row_queries_answer_from_the_composed_map()
    {
        var vm = new KeymapViewModel();

        vm.Bind(CtrlHome, Pa1);

        Assert.Equal(Pa1, vm.ActionOf(CtrlHome));
        Assert.Equal(new KeymapAction.TypeText("¬"), vm.ActionOf(new KeyChord(Key.OemOpenBrackets, KeyModifiers.Control)));
        Assert.Null(vm.ActionOf(new KeyChord(Key.X, KeyModifiers.Control)));
        Assert.Equal([new KeyChord(Key.D2, KeyModifiers.Alt)], vm.ChordsFor(new KeymapAction.SendKey(TerminalKey.PA2)));
        Assert.Contains(CtrlHome, vm.ChordsFor(Pa1));
        Assert.Empty(vm.ChordsFor(KeymapAction.Unbound.Instance));
    }

    [Fact]
    public void A_store_is_written_through_and_read_at_construction()
    {
        new KeymapViewModel(new KeymapStore(FilePath)).Bind(CtrlHome, Pa1);

        var reopened = new KeymapViewModel(new KeymapStore(FilePath));

        Assert.Equal(Pa1, reopened.Overlay.Entries[CtrlHome]);
        Assert.Null(reopened.LastSaveError);
    }

    [Fact]
    public void Unbind_and_ResetToDefaults_write_through_too()
    {
        var vm = new KeymapViewModel(new KeymapStore(FilePath));
        vm.Unbind(CtrlHome);
        Assert.Contains("\"Ctrl+Home\": null", File.ReadAllText(FilePath));

        vm.ResetToDefaults();

        Assert.Empty(new KeymapStore(FilePath).Load().Bindings);
        Assert.True(vm.Compose(destructiveBackspace: true).TryMap(CtrlHome, out _));
    }

    [Fact]
    public void A_failed_save_keeps_the_change_in_memory_and_reports_it()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, "not json");
        var vm = new KeymapViewModel(new KeymapStore(FilePath));
        string? reported = null;
        vm.SaveFailed += (_, message) => reported = message;

        vm.Bind(CtrlHome, Pa1);

        Assert.Equal(Pa1, vm.Overlay.Entries[CtrlHome]);
        Assert.NotNull(vm.LastSaveError);
        Assert.Equal(vm.LastSaveError, reported);
        Assert.Contains("keymap.json", reported);
        Assert.Equal("not json", File.ReadAllText(FilePath));
    }

    [Fact]
    public void Unreadable_entries_are_counted_and_notified()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, """{"bindings": {"Cmd+K": "PA1", "F2": 7}}""");
        var vm = new KeymapViewModel(new KeymapStore(FilePath));
        var notified = new List<string?>();
        vm.PropertyChanged += (_, e) => notified.Add(e.PropertyName);

        Assert.Equal(2, vm.UnreadableEntries);
        vm.ResetToDefaults();

        Assert.Equal(0, vm.UnreadableEntries);
        Assert.Contains(nameof(KeymapViewModel.UnreadableEntries), notified);
    }
}
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~KeymapViewModelTests"`
Expected: build FAILS.

- [ ] **Step 3: Create `KeymapViewModel.cs`**

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using CommunityToolkit.Mvvm.ComponentModel;
using LizTerm.App.Keyboard;
using LizTerm.Core.Settings;

namespace LizTerm.App.ViewModels;

/// <summary>The user's keymap as one live object (editable keymap spec §5.1), one per process and owned by App as
/// SettingsViewModel is: every session window composes its map from it, and the Keyboard tab edits it. A change
/// applies in memory first and raises Changed, then writes through the store, so the in-memory change always wins;
/// a save that throws sets LastSaveError and raises SaveFailed, the settings object's contract, so the tab's banner
/// is the same banner. The row queries answer from the erasing default so a view never reads the dictionary; that
/// is the seam a chord-centric view would sit on. UI thread only: the windows and the tab that use it are.</summary>
public sealed class KeymapViewModel : ObservableObject
{
    private readonly KeymapStore? _store;
    private string? _lastSaveError;

    /// <summary>In-memory only: every default, nothing written.</summary>
    public KeymapViewModel() => Overlay = KeymapOverlay.Empty;

    /// <summary>Loaded from the store now, and written through on every change.</summary>
    public KeymapViewModel(KeymapStore store)
    {
        _store = store;
        Overlay = KeymapOverlay.Parse(store.Load());
    }

    /// <summary>The differences as this process sees them. Another process's later write is seen at the next
    /// launch; KeymapStore.Update keeps that process's entries, this object keeps its own view.</summary>
    public KeymapOverlay Overlay { get; private set; }

    /// <summary>After every Bind, Unbind and ResetToDefaults, before the save.</summary>
    public event EventHandler? Changed;

    /// <summary>Every failed save, with the message the banner shows.</summary>
    public event EventHandler<string>? SaveFailed;

    /// <summary>The last save's failure, or null once a save succeeds.</summary>
    public string? LastSaveError
    {
        get => _lastSaveError;
        private set => SetProperty(ref _lastSaveError, value);
    }

    /// <summary>Entries in the file this build left alone, for the tab's note.</summary>
    public int UnreadableEntries => Overlay.IgnoredCount;

    /// <summary>The map in force for one window: the profile's Backspace choice under the user's entries.</summary>
    public Keymap Compose(bool destructiveBackspace) => Overlay.Compose(destructiveBackspace);

    /// <summary>Every chord that does <paramref name="action"/>, from the erasing default; none for Unbound.</summary>
    public IReadOnlyList<KeyChord> ChordsFor(KeymapAction action)
    {
        var map = Compose(destructiveBackspace: true);
        return action switch
        {
            KeymapAction.SendKey send => [.. map.Keys.Where(pair => pair.Value == send.Key).Select(pair => pair.Key)],
            KeymapAction.TypeText type => [.. map.Text.Where(pair => pair.Value == type.Text).Select(pair => pair.Key)],
            _ => [],
        };
    }

    /// <summary>What <paramref name="chord"/> does in the erasing default, or null when nothing.</summary>
    public KeymapAction? ActionOf(KeyChord chord)
    {
        var map = Compose(destructiveBackspace: true);
        if (map.TryMap(chord, out var key)) return new KeymapAction.SendKey(key);
        if (map.TryText(chord, out var text)) return new KeymapAction.TypeText(text);
        return null;
    }

    public void Bind(KeyChord chord, KeymapAction action) => Apply(overlay => overlay.Bind(chord, action));

    public void Unbind(KeyChord chord) => Apply(overlay => overlay.Unbind(chord));

    public void ResetToDefaults() => Apply(overlay => overlay.Cleared());

    private void Apply(Func<KeymapOverlay, KeymapOverlay> change)
    {
        Overlay = change(Overlay);
        OnPropertyChanged(nameof(UnreadableEntries));
        try
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            Save(change);
        }
    }

    /// <summary>The same change applied to what is on disk now, the discipline SettingsViewModel follows.</summary>
    private void Save(Func<KeymapOverlay, KeymapOverlay> change)
    {
        if (_store is null) return;
        try
        {
            _store.Update(file => change(KeymapOverlay.Parse(file)).ToFile());
            LastSaveError = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            LastSaveError = "Could not save the keymap: " + ex.Message;
            SaveFailed?.Invoke(this, LastSaveError);
        }
    }
}
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~KeymapViewModelTests"`
Expected: 6 PASS.

- [ ] **Step 5: Commit**

```bash
git add src/LizTerm.App/ViewModels/KeymapViewModel.cs tests/LizTerm.App.Tests/ViewModels/KeymapViewModelTests.cs
git commit -m "App: KeymapViewModel, the process's one live keymap, written through like the settings (#18)

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 8: `TerminalScreen.Keymap` in place of `DestructiveBackspace`

**Files:**
- Modify: `src/LizTerm.App/Controls/TerminalScreen.cs` (lines 28–31, 71, 176–180)
- Modify: `src/LizTerm.App/Views/SessionWindow.axaml` (line 449)
- Test: `tests/LizTerm.App.Tests/Controls/TerminalScreenInputTests.cs` (line 48)

**Interfaces:**
- Produces: `StyledProperty<Keymap> TerminalScreen.KeymapProperty` and `Keymap TerminalScreen.Keymap { get; set; }`, defaulting to `DefaultKeymap.Create(destructiveBackspace: true)`. `TerminalScreen.DestructiveBackspace` no longer exists.

- [ ] **Step 1: Change the test to the new property**

In `TerminalScreenInputTests.Backspace_erases_by_default_and_moves_left_when_the_profile_says_so`, replace

```csharp
        screen.DestructiveBackspace = false;
```

with

```csharp
        screen.Keymap = DefaultKeymap.Create(destructiveBackspace: false);
```

and add `using LizTerm.App.Keyboard;` to the file's usings. Rename the test to `Backspace_erases_by_default_and_moves_left_with_the_cursor_left_table`.

- [ ] **Step 2: Run it to see it fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~TerminalScreenInputTests"`
Expected: build FAILS, `Keymap` has no setter on `TerminalScreen`.

- [ ] **Step 3: Replace the property**

In `TerminalScreen.cs`, replace the `DestructiveBackspaceProperty` declaration (with its comment) by:

```csharp
    /// <summary>The table in force. The window composes it from the profile's Backspace choice and the user's
    /// keymap.json (#18) and sets it here, on open and on every change; a screen shown on its own (the tests) has
    /// the erasing default, which is what it always had.</summary>
    public static readonly StyledProperty<Keymap> KeymapProperty =
        AvaloniaProperty.Register<TerminalScreen, Keymap>(nameof(Keymap), DefaultKeymap.Create(destructiveBackspace: true));
```

Delete the line `private Keymap Keymap => DefaultKeymap.Create(DestructiveBackspace);`.

Replace the `DestructiveBackspace` property (getter and setter) by:

```csharp
    public Keymap Keymap
    {
        get => GetValue(KeymapProperty);
        set => SetValue(KeymapProperty, value);
    }
```

In `SessionWindow.axaml`, delete the attribute line `DestructiveBackspace="{Binding Profile.DestructiveBackspace}"` from the `<controls:TerminalScreen x:Name="Screen"` element (the window will set `Keymap` in code in Task 9; until then a session window has the erasing default for every profile, which Task 9's test covers).

- [ ] **Step 4: Run the input tests and the whole App project**

Run: `dotnet test tests/LizTerm.App.Tests`
Expected: all PASS. (Nothing else referenced `DestructiveBackspace` on the screen; `ProfileEditorViewModel` and `SessionProfile` keep theirs.)

- [ ] **Step 5: Commit**

```bash
git add src/LizTerm.App/Controls/TerminalScreen.cs src/LizTerm.App/Views/SessionWindow.axaml tests/LizTerm.App.Tests/Controls/TerminalScreenInputTests.cs
git commit -m "App: TerminalScreen takes its Keymap as a property; the window will compose it (#18)

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 9: `SessionWindow.AttachKeymap` and App wiring

**Files:**
- Modify: `src/LizTerm.App/Views/SessionWindow.axaml.cs` (constructor's `Opened` handler around line 88, fields near line 142, `OnDataContextChanged` around line 740, `OnClosed` around line 770, and a new `AttachKeymap` beside `AttachSessions` at line 565)
- Modify: `src/LizTerm.App/App.axaml.cs` (fields at line 59, `Settings` property at line 63, `OpenSession` after line 207)
- Test: `tests/LizTerm.App.Tests/Views/SessionWindowKeymapTests.cs`

**Interfaces:**
- Consumes: `KeymapViewModel`, `DefaultKeymap`, `TerminalScreen.Keymap`, `Keypad.Keymap` (exists: a styled property whose change rebuilds the tooltips).
- Produces: `internal void SessionWindow.AttachKeymap(KeymapViewModel keymap)`; `internal KeymapViewModel App.Keymap`.

- [ ] **Step 1: Write the failing test**

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using LizTerm.App.Controls;
using LizTerm.App.Keyboard;
using LizTerm.App.Tests.Fakes;
using LizTerm.App.ViewModels;
using LizTerm.App.Views;
using LizTerm.Core.Session;
using LizTerm.Core.Settings;

namespace LizTerm.App.Tests.Views;

/// <summary>Editable keymap spec §5.2: the window composes the profile's Backspace choice under the process's
/// keymap and pushes it to the screen and the keypad, on open and on every change.</summary>
public class SessionWindowKeymapTests
{
    private static (SessionWindow Window, FakeEmulatorSession Session, TerminalScreen Screen) Show(bool destructiveBackspace, KeymapViewModel? keymap)
    {
        var session = new FakeEmulatorSession
        {
            Profile = new SessionProfile { Name = "TSO", Host = "tk5.local", Port = 3270, DestructiveBackspace = destructiveBackspace },
        };
        var vm = new SessionViewModel(session, action => action(), new FakeTextClipboard());
        var window = new SessionWindow(MenuStyle.InWindow, isMacOS: false) { DataContext = vm };
        if (keymap is not null) window.AttachKeymap(keymap);
        window.Show();
        var screen = window.FindControl<TerminalScreen>("Screen")!;
        screen.Focus();
        return (window, session, screen);
    }

    private static Button KeypadButton(SessionWindow window, TerminalKey key)
    {
        window.UpdateLayout();
        return window.FindControl<Keypad>("KeypadPanel")!.GetVisualDescendants().OfType<Button>().First(b => Equals(b.Tag, key));
    }

    [AvaloniaFact]
    public void A_window_without_a_keymap_follows_the_profiles_Backspace_choice()
    {
        var (window, session, _) = Show(destructiveBackspace: false, keymap: null);

        window.KeyPressQwerty(PhysicalKey.Backspace, RawInputModifiers.None);

        Assert.Equal(["key:Backspace"], session.Calls);
    }

    [AvaloniaFact]
    public void A_rebind_reaches_the_screen_and_the_keypad_tooltip()
    {
        var keymap = new KeymapViewModel();
        var (window, session, _) = Show(destructiveBackspace: true, keymap);
        Assert.DoesNotContain("Home", (string?)ToolTip.GetTip(KeypadButton(window, TerminalKey.PA1)) ?? "");

        keymap.Bind(new KeyChord(Key.Home, KeyModifiers.Control), new KeymapAction.SendKey(TerminalKey.PA1));
        window.KeyPressQwerty(PhysicalKey.Home, RawInputModifiers.Control);
        window.KeyPressQwerty(PhysicalKey.Backspace, RawInputModifiers.None);

        Assert.Equal(["key:PA1", "key:Erase"], session.Calls);
        Assert.Contains("Home", (string)ToolTip.GetTip(KeypadButton(window, TerminalKey.PA1))!);
        Assert.DoesNotContain("Home", (string)ToolTip.GetTip(KeypadButton(window, TerminalKey.PA2))!);
    }

    [AvaloniaFact]
    public void A_keymap_attached_before_the_data_context_still_composes_with_the_profile()
    {
        var keymap = new KeymapViewModel();
        keymap.Bind(new KeyChord(Key.Home, KeyModifiers.Control), new KeymapAction.SendKey(TerminalKey.PA1));
        var session = new FakeEmulatorSession
        {
            Profile = new SessionProfile { Name = "TSO", Host = "tk5.local", Port = 3270, DestructiveBackspace = false },
        };
        var window = new SessionWindow(MenuStyle.InWindow, isMacOS: false);
        window.AttachKeymap(keymap);
        window.DataContext = new SessionViewModel(session, action => action(), new FakeTextClipboard());
        window.Show();
        window.FindControl<TerminalScreen>("Screen")!.Focus();

        window.KeyPressQwerty(PhysicalKey.Home, RawInputModifiers.Control);
        window.KeyPressQwerty(PhysicalKey.Backspace, RawInputModifiers.None);

        Assert.Equal(["key:PA1", "key:Backspace"], session.Calls);
    }

    [AvaloniaFact]
    public void A_closed_window_no_longer_listens()
    {
        var keymap = new KeymapViewModel();
        var (window, _, screen) = Show(destructiveBackspace: true, keymap);
        window.Close();

        keymap.Bind(new KeyChord(Key.Home, KeyModifiers.Control), new KeymapAction.SendKey(TerminalKey.PA1));

        Assert.True(screen.Keymap.TryMap(new KeyChord(Key.Home, KeyModifiers.Control), out var key));
        Assert.Equal(TerminalKey.PA2, key);
    }
}
```

`FakeEmulatorSession.Calls` records each key as `key:` followed by the `TerminalKey` name, and `SessionViewModel.SendKeyAsync` sends whether or not the session is connected, so the calls are the assertion. The keypad is hidden by default but stays in the visual tree, and it builds its buttons and tooltips on attach, so `GetVisualDescendants` finds them after `Show()`.

- [ ] **Step 2: Run it to see it fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~SessionWindowKeymapTests"`
Expected: build FAILS, `AttachKeymap` does not exist.

- [ ] **Step 3: Add the window side**

In `SessionWindow.axaml.cs`, next to the `_styleSource` field (line 142) add:

```csharp
    /// <summary>The process's keymap, once App has attached it; null for a window built without one, as most
    /// tests build it, which then composes the profile's default and never changes it.</summary>
    private KeymapViewModel? _keymap;
```

Beside `AttachSessions` add:

```csharp
    /// <summary>Joins this window to the process's keymap (editable keymap spec §5.2). Called by App once, before
    /// Show(). The subscription's lifetime is the settings subscription's: taken in Opened (or here, when attached
    /// after it), released in OnClosed, so the process-wide object never calls into a window that is gone.</summary>
    internal void AttachKeymap(KeymapViewModel keymap)
    {
        if (_keymap is not null) throw new InvalidOperationException("This window already has a keymap.");
        _keymap = keymap;
        if (_opened) keymap.Changed += OnKeymapChanged;
        ApplyKeymap();
    }

    private void OnKeymapChanged(object? sender, EventArgs e) => ApplyKeymap();

    /// <summary>The map in force for this window: the profile's Backspace choice under the user's keymap.json. Set
    /// on the screen and on the keypad, whose tooltips follow it (keypad spec §4.4).</summary>
    private void ApplyKeymap()
    {
        var destructive = ViewModel?.Profile.DestructiveBackspace ?? true;
        var map = _keymap?.Compose(destructive) ?? DefaultKeymap.Create(destructive);
        Screen.Keymap = map;
        KeypadPanel.Keymap = map;
    }
```

In the constructor's `Opened` handler, after the `_styleSource` subscription line, add:

```csharp
            if (_keymap is not null) _keymap.Changed += OnKeymapChanged;
```

At the end of `OnDataContextChanged`, add:

```csharp
        ApplyKeymap();
```

In `OnClosed`, after `_styleSource = null;`, add:

```csharp
        if (_keymap is not null) _keymap.Changed -= OnKeymapChanged;
        _keymap = null;
```

Add `using LizTerm.App.Keyboard;` to the file if it is not already there.

- [ ] **Step 4: Add the App side**

In `App.axaml.cs`, after `private SettingsViewModel? _settings;` add `private KeymapViewModel? _keymap;`, and after the `Settings` property add:

```csharp
    /// <summary>The process's one keymap object (#18), for every session window and, in time, for Preferences.
    /// Lazy for the reason Settings is.</summary>
    internal KeymapViewModel Keymap => _keymap ??= new KeymapViewModel(new KeymapStore(AppPaths.KeymapFile()));
```

In `OpenSession`, directly after `window.AttachSessions(_sessions, entry);` add:

```csharp
        window.AttachKeymap(Keymap);
```

Add `using LizTerm.Core.Settings;` if the file lacks it.

- [ ] **Step 5: Run the new tests, then the whole App project**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~SessionWindowKeymapTests"`
Expected: 4 PASS.

Run: `dotnet test tests/LizTerm.App.Tests`
Expected: all PASS.

- [ ] **Step 6: Commit**

```bash
git add src/LizTerm.App/Views/SessionWindow.axaml.cs src/LizTerm.App/App.axaml.cs tests/LizTerm.App.Tests/Views/SessionWindowKeymapTests.cs
git commit -m "App: every session window composes its keymap from the process's KeymapViewModel (#18)

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 10: Documentation, notes for the next Claude, and the release gates

**Files:**
- Modify: `docs/user-guide.md` (the Keyboard section's opening line at line 161; the "Where LizTerm keeps its files" paragraph)
- Modify: `docs/privacy.md` (the file table at lines 31–36; the permissions bullet at line 100)
- Modify: `CHANGELOG.md` (under `## Unreleased`)
- Modify: `src/LizTerm.App/CLAUDE.md` (the Keyboard bullet that says `Keymap.With` is the seam)
- Modify: `tests/CLAUDE.md` (the App tests bullet on `SettingsViewModel`)

- [ ] **Step 1: The user guide**

In the Keyboard section, replace the line

```
The default layout follows Vista TN3270, cross-checked against wc3270. It cannot be changed yet.
```

with

```
The default layout follows Vista TN3270, cross-checked against wc3270. To change a binding, edit `keymap.json` by
hand; see [Where LizTerm keeps its files](#where-lizterm-keeps-its-files).
```

In "Where LizTerm keeps its files", after the sentence ending `deleting it empties the drop-down.`, add this text (the inner block is an ordinary three-backtick `json` fence in the guide):

````markdown
`keymap.json` holds your keyboard bindings, only the ones that differ from the table under
[Keyboard](#keyboard). Each entry is a chord, such as `Ctrl+Home`, `Shift+F1` or `Tap:LeftCtrl`, set to a 3270 key
name such as `PA1`, to `{"text": "¬"}` for text to type, or to `null` to take that key away:

```json
{
  "bindings": {
    "Ctrl+Home": "PA1",
    "Alt+2": null
  }
}
```

Key names are Avalonia's (`Home`, `PageUp`, `D1` for the 1 key, `OemOpenBrackets` for `[`); a chord LizTerm cannot
read is left alone and does nothing. Deleting the file restores the defaults. Backspace follows the profile's
Backspace setting unless the file binds `Back` itself.
````

- [ ] **Step 2: The privacy doc**

In the file table, after the `Recent hosts` row, add:

```
| Keymap | `keymap.json` | Keyboard bindings | default |
```

In the bullet that begins `**`profiles/`, `settings.json`, `tags.json` and `recent-hosts.json` are created with default permissions**`, change the list to `` `profiles/`, `settings.json`, `tags.json`, `recent-hosts.json` and `keymap.json` ``.

- [ ] **Step 3: The changelog**

Under `## Unreleased`, add as the first bullet:

```
- **Keyboard bindings.** A `keymap.json` beside `settings.json` changes what a key sends, and the on-screen keypad's
  tooltips follow it. Hand-edited for now; the user guide's "Where LizTerm keeps its files" has the format
  ([#18](https://github.com/coffeemuse/LizTerm/issues/18)).
```

- [ ] **Step 4: The two CLAUDE.md files**

In `src/LizTerm.App/CLAUDE.md`, in the Keyboard section's bullet about `Keymap`, replace the sentence

```
`Keymap.With` is the seam for future user remapping, and nothing else about remapping exists.
```

with

```
`KeymapOverlay` (`Keyboard/`) is the user's `keymap.json` parsed (`KeymapStore` and `KeymapFile` in Core hold it as
strings; `ChordSyntax` and `KeymapAction` read them), composed over the profile's default with `Without` then `With`.
`KeymapViewModel` is the process's one live copy, write-through like `SettingsViewModel`;
`SessionWindow.AttachKeymap` composes it for the screen and the keypad on every change. `KeymapPolicy` is what the
Keyboard tab (#18, PR 2) refuses, with the reason.
```

In `tests/CLAUDE.md`, in the App tests bullet, after `a test that needs a failing save points a `SettingsStore` at a temp file holding `not json`.` add:

```
A window built without `AttachKeymap` composes the profile's default and never changes it; one that needs a failing
keymap save points a `KeymapStore` at a temp file holding `not json`.
```

- [ ] **Step 5: The release gates**

Run: `dotnet build LizTerm.slnx --no-incremental 2>&1 | grep -c " warning "`
Expected: `0`. Fix anything else before going on (an unused `using` in `SettingsStore.cs` is the likely one).

Run: `dotnet test LizTerm.slnx`
Expected: all PASS; the live-host tests skip themselves. `RepositoryHeadersTests` passing confirms every new file has its header.

- [ ] **Step 6: Commit**

```bash
git add docs/user-guide.md docs/privacy.md CHANGELOG.md src/LizTerm.App/CLAUDE.md tests/CLAUDE.md
git commit -m "Docs: keymap.json in the guide, the privacy table, the changelog and the project notes (#18)

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

- [ ] **Step 7: Live check before the PR**

Run the app against a profile (`dotnet run --project src/LizTerm.App -- <profile-name>`), then with it running write this to `keymap.json` under the config root and reopen the session window:

```json
{ "bindings": { "Ctrl+Home": "PA1", "Alt+2": null } }
```

Confirm Ctrl+Home sends PA1 (the OIA or the host shows it), Alt+2 does nothing, and the keypad's PA1 tooltip names Ctrl+Home. Then delete the file, reopen, and confirm the defaults are back. Record the result in the PR description.
