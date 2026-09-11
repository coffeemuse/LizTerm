# LizTerm settings store and Preferences window — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give LizTerm an app-wide settings file that layers per key, one live settings object every window follows, and a Preferences window reached from the macOS application menu with Cmd-comma and from Edit elsewhere — holding, in this first cut, the crosshair mode and a blink switch.

**Architecture:** Core gains `LizTerm.Core.Settings`: a flat positional `AppSettings` record, `CrosshairMode` moved in from the App, a pure `SettingsLayers` (document-level merge, sparse user document) and a file-backed `SettingsStore` whose `Update` re-reads before it writes. The App gains `SettingsViewModel`, one per process, injected into every `SessionViewModel` the way the clipboard and prompts are, defaulting to an in-memory instance so tests never touch a file. `TerminalScreen` gets a `BlinkEnabled` property. A modeless, one-at-a-time `PreferencesWindow` binds to the settings object; `App.ShowPreferences` is its one route, from the application menu on macOS and from a platform-conditional Edit item elsewhere.

**Tech Stack:** .NET 10, C# 14, Avalonia 12.1.2 (headless XUnit lane), CommunityToolkit.Mvvm 8.4.2, System.Text.Json source generation and `System.Text.Json.Nodes`, xunit.v3.

**Spec:** `docs/superpowers/specs/2026-09-11-lizterm-settings-store-design.md`

## Global Constraints

- **Worktree only.** All work happens in `/Users/robert/ClaudeSandbox/LizTerm/.claude/worktrees/post-0-4-1-roadmap-893af3`, on branch `claude/post-0-4-1-roadmap-893af3`. Never run git in, or write to, the main checkout at `/Users/robert/ClaudeSandbox/LizTerm`.
- **Robert's gates.** Pushing, opening or changing a pull request, and bumping the version each wait for Robert's explicit go-ahead in chat. The version stays 0.4.1 in this plan (spec §7).
- **Dependency rule.** `LizTerm.Core` names nothing from Avalonia or b3270. Nothing in this plan touches `LizTerm.Backend.B3270`, `IEmulatorSession` or `SessionProfile`.
- **Licence header.** Every new `.cs` and `.axaml` file starts with the three lines `This file is part of LizTerm.`, `Copyright 2026 by CoffeeMuse`, `SPDX-License-Identifier: BSD-3-Clause` in the file's comment syntax (`//` for C#, inside `<!-- -->` before the root element for XAML). `RepositoryHeadersTests` fails the suite otherwise.
- **Test commands.** Full suite: `LIZTERM_B3270_PATH=/opt/homebrew/bin/b3270 dotnet test LizTerm.slnx` (this worktree has no `native/out`; the live-host tests skip themselves). One class: `dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~SettingsLayersTests"`. The App tests run on Avalonia's headless platform, whose application is the real `LizTerm.App.App`.
- **Zero warnings.** Before any task is called done: `dotnet build LizTerm.slnx --no-incremental 2>&1 | grep -c " warning "` prints `0`. CI builds with `-warnaserror`.
- **Menus.** Every native menu item carries a `Command` or a `Click` handler. The native and classic menus of `SessionWindow` stay item-for-item identical, separators included (`NativeMenuTests.The_native_menu_matches_the_classic_menu_item_for_item`). No gesture on any window menu outside Edit. The one gesture this plan adds is Cmd-comma on the *application* menu (spec §5.4).
- **Design history is a record.** Edit nothing under `docs/superpowers/` except the "As built" section Task 9 appends to this slice's spec.
- **Each fact has one home** (root `CLAUDE.md`). Task 9 updates the user guide, the architecture overview and the three `CLAUDE.md` notes; earlier tasks change no documentation.
- **Commits** end with the line `Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>`.

---

## File Structure

**Created**

| File | Responsibility |
|---|---|
| `src/LizTerm.Core/Settings/CrosshairMode.cs` | The enum, moved from `src/LizTerm.App/Rendering/` (Task 1). |
| `src/LizTerm.Core/Settings/AppSettings.cs` | The flat positional record with a default per parameter (Task 2). |
| `src/LizTerm.Core/Settings/SettingsJsonContext.cs` | Source-generated JSON context over `AppSettings` (Task 2). |
| `src/LizTerm.Core/Settings/SettingsLayers.cs` | Pure: `Merge`, `Read`, `UserDocument` (Task 2). |
| `src/LizTerm.Core/Settings/SettingsStore.cs` | File-backed: `Load`, `Update`, atomic write (Task 3). |
| `src/LizTerm.App/ViewModels/SettingsViewModel.cs` | One observable settings object per process; write-through with `LastSaveError` and `SaveFailed` (Task 4). |
| `src/LizTerm.App/Views/PreferencesWindow.axaml` + `.axaml.cs` | The window: crosshair radios, blink box, save-error line, Done (Task 7). |
| `tests/LizTerm.Core.Tests/Settings/SettingsLayersTests.cs` | The pure rules (Task 2). |
| `tests/LizTerm.Core.Tests/Settings/SettingsStoreTests.cs` | Temp-file store tests (Task 3). |
| `tests/LizTerm.App.Tests/ViewModels/SettingsViewModelTests.cs` | Write-through, failure, no-op (Task 4). |
| `tests/LizTerm.App.Tests/Views/PreferencesWindowTests.cs` | Headless window tests and the one-window rule (Task 7). |

**Modified**

| File | Change |
|---|---|
| `src/LizTerm.App/Rendering/CrosshairMode.cs` | Deleted by the move (Task 1). |
| Nine `.cs` files that name `CrosshairMode` | `using` directives (Task 1). |
| `src/LizTerm.Core/Profiles/AppPaths.cs` | `SettingsFile()` (Task 3). |
| `tests/LizTerm.Core.Tests/Profiles/AppPathsTests.cs` | The settings file path (Task 3). |
| `src/LizTerm.App/ViewModels/SessionViewModel.cs` | `Settings` property replaces the `Crosshair` field; `SaveFailed` reaches the banner (Task 5). |
| `src/LizTerm.App/Views/SessionWindow.axaml` | Bindings to `Settings.Crosshair` and `Settings.Blink`; Edit > Preferences in both menus (Tasks 5, 6, 8). |
| `src/LizTerm.App/Views/SessionWindow.axaml.cs` | `SetCrosshair` writes the settings; Preferences handlers and visibility (Tasks 5, 8). |
| `src/LizTerm.App/App.axaml` | The application menu's Preferences item with Cmd-comma (Task 7). |
| `src/LizTerm.App/App.axaml.cs` | Owns the `SettingsViewModel`, passes it to every session, `ShowPreferences` (Tasks 5, 7). |
| `src/LizTerm.App/Controls/TerminalScreen.cs` | `BlinkEnabled` (Task 6). |
| `src/LizTerm.App/Menus/MenuStrategy.cs` | `PreferencesInEditMenu` (Task 8). |
| `tests/LizTerm.App.Tests/Views/NativeMenuTests.cs` | Crosshair path, blink reaches the screen, application-menu and Edit-menu assertions (Tasks 5–8). |
| `tests/LizTerm.App.Tests/ViewModels/SessionViewModelTests.cs` | Settings injection, banner, live propagation (Task 5). |
| `tests/LizTerm.App.Tests/Controls/TerminalScreenBlinkTests.cs` | Blink disabled (Task 6). |
| `tests/LizTerm.App.Tests/Menus/MenuStrategyTests.cs` | The new rule (Task 8). |
| `docs/user-guide.md`, `docs/architecture.md`, `src/LizTerm.Core/CLAUDE.md`, `src/LizTerm.App/CLAUDE.md`, `tests/CLAUDE.md` | Task 9. |
| `docs/superpowers/specs/2026-09-11-lizterm-settings-store-design.md` | "As built" section (Task 9). |

## Two refinements to the spec, decided while planning

Both are recorded in the spec's "As built" section by Task 9; implementers follow the plan.

1. **`SettingsStore.Update(Func<AppSettings, AppSettings>)` instead of `Save(AppSettings)`.** Spec §3.4 wants a key written by a second LizTerm process between this process's load and save to survive. With a whole-record `Save`, the re-read could only preserve *which* keys are pinned; this process's stale in-memory value would overwrite the other's. `Update` applies the change to what is on disk now, exactly as `ProfileStore.Update` does, so only the key the user touched changes. `SettingsViewModel` keeps its own in-memory record as the view; a disk value changed by another process is seen at the next launch.
2. **`SettingsLayers.Read` drops a bad key alone, not the whole file.** Spec §3.3 says a value that will not deserialise gives the run every default. That would also make the next save rewrite every *other* key from defaults, losing a user's good settings over one hand-edited typo. `Read` probes each top-level key on its own and keeps the ones that read; the offending key alone falls to its default, and the next save repairs it (§3.4 unchanged).

The spec's class `Settings` in a `Settings/` folder is named `SettingsViewModel` in `ViewModels/`: a class called `Settings` inside a namespace ending in `.Settings` cannot be named from `namespace LizTerm.App` without qualifying it, and the object is an `ObservableObject` that a window binds to, which is what the `ViewModels/` folder holds.

---

### Task 1: Move `CrosshairMode` into Core

The settings record names the enum and Core cannot see the App, so the enum moves first. Pure refactor: nothing changes behaviour, and the whole suite is the test.

**Files:**
- Move: `src/LizTerm.App/Rendering/CrosshairMode.cs` → `src/LizTerm.Core/Settings/CrosshairMode.cs`
- Modify (`using` only): `src/LizTerm.App/Controls/TerminalScreen.cs`, `src/LizTerm.App/Rendering/CrosshairGeometry.cs`, `src/LizTerm.App/ViewModels/CrosshairModeConverter.cs`, `src/LizTerm.App/ViewModels/SessionViewModel.cs`, `src/LizTerm.App/Views/SessionWindow.axaml.cs`, `tests/LizTerm.App.Tests/Controls/TerminalScreenCrosshairTests.cs`, `tests/LizTerm.App.Tests/Rendering/CrosshairGeometryTests.cs`, `tests/LizTerm.App.Tests/ViewModels/CrosshairModeConverterTests.cs`, `tests/LizTerm.App.Tests/Views/NativeMenuTests.cs`

**Interfaces:**
- Produces: `LizTerm.Core.Settings.CrosshairMode { None, Horizontal, Vertical, Both }`, unchanged members.

- [ ] **Step 1: Move the file and change its namespace**

```bash
mkdir -p src/LizTerm.Core/Settings
git mv src/LizTerm.App/Rendering/CrosshairMode.cs src/LizTerm.Core/Settings/CrosshairMode.cs
sed -i '' 's/^namespace LizTerm.App.Rendering;$/namespace LizTerm.Core.Settings;/' src/LizTerm.Core/Settings/CrosshairMode.cs
```

The file's licence header and doc comment stay as they are. `LizTerm.Core.csproj` has no explicit `Compile` items, so the new folder is picked up.

- [ ] **Step 2: Fix the `using` directives**

Six files imported `LizTerm.App.Rendering` *only* for the enum. Replace the directive, then move the new line into alphabetical order among that file's `using`s (Avalonia first, then `LizTerm.App.*`, then `LizTerm.Core.*`):

```bash
for f in src/LizTerm.App/ViewModels/CrosshairModeConverter.cs \
         src/LizTerm.App/ViewModels/SessionViewModel.cs \
         src/LizTerm.App/Views/SessionWindow.axaml.cs \
         tests/LizTerm.App.Tests/Controls/TerminalScreenCrosshairTests.cs \
         tests/LizTerm.App.Tests/ViewModels/CrosshairModeConverterTests.cs \
         tests/LizTerm.App.Tests/Views/NativeMenuTests.cs; do
  sed -i '' 's/^using LizTerm.App.Rendering;$/using LizTerm.Core.Settings;/' "$f"
done
```

For example `SessionViewModel.cs`'s block becomes:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LizTerm.App.Capture;
using LizTerm.App.Clipboard;
using LizTerm.App.Dialogs;
using LizTerm.App.Files;
using LizTerm.App.Status;
using LizTerm.Core.Profiles;
using LizTerm.Core.Screen;
using LizTerm.Core.Security;
using LizTerm.Core.Session;
using LizTerm.Core.Settings;
```

Three files still need `LizTerm.App.Rendering` for `CellGeometry`, `CrosshairGeometry` or `Palette`; *add* `using LizTerm.Core.Settings;` to them in alphabetical position: `src/LizTerm.App/Controls/TerminalScreen.cs`, `src/LizTerm.App/Rendering/CrosshairGeometry.cs` (which has no `LizTerm.*` using yet; add one), `tests/LizTerm.App.Tests/Rendering/CrosshairGeometryTests.cs`.

- [ ] **Step 3: Confirm nothing else names the old namespace for the enum**

Run: `grep -rn "Rendering.CrosshairMode" src tests; grep -rn "CrosshairMode" src --include='*.axaml'`
Expected: the first prints nothing; the second prints only `ConverterParameter=` lines (a string, not a type reference) or nothing.

- [ ] **Step 4: Build and run the whole suite**

Run: `dotnet build LizTerm.slnx --no-incremental 2>&1 | grep -c " warning "` — Expected: `0`.
Run: `LIZTERM_B3270_PATH=/opt/homebrew/bin/b3270 dotnet test LizTerm.slnx` — Expected: every test passes (the integration tests skip). `RepositoryHeadersTests` proves the moved file kept its header.

- [ ] **Step 5: Commit**

```bash
git add -A src/LizTerm.Core/Settings src/LizTerm.App tests/LizTerm.App.Tests
git commit -m "Move CrosshairMode into Core, where the settings record will name it

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 2: `AppSettings`, its JSON context, and the pure `SettingsLayers`

**Files:**
- Create: `src/LizTerm.Core/Settings/AppSettings.cs`, `src/LizTerm.Core/Settings/SettingsJsonContext.cs`, `src/LizTerm.Core/Settings/SettingsLayers.cs`
- Test: `tests/LizTerm.Core.Tests/Settings/SettingsLayersTests.cs`

**Interfaces:**
- Consumes: `LizTerm.Core.Settings.CrosshairMode` (Task 1).
- Produces: `public sealed record AppSettings(CrosshairMode Crosshair = CrosshairMode.None, bool Blink = true)`; `internal partial class SettingsJsonContext : JsonSerializerContext` with `Default.AppSettings`; `public static class SettingsLayers` with `JsonObject Merge(IEnumerable<JsonObject> layers)`, `AppSettings Read(JsonObject merged)` (never null), `JsonObject UserDocument(AppSettings next, AppSettings beneath, JsonObject? existing)`.

- [ ] **Step 1: Write the failing tests**

`tests/LizTerm.Core.Tests/Settings/SettingsLayersTests.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Text.Json.Nodes;
using LizTerm.Core.Settings;

namespace LizTerm.Core.Tests.Settings;

/// <summary>The two rules of the settings file, with no disk: how layers merge and which keys a save writes.</summary>
public class SettingsLayersTests
{
    private static JsonObject Doc(string json) => JsonNode.Parse(json)!.AsObject();

    [Fact]
    public void A_later_layer_wins_per_key_and_an_absent_key_is_the_default()
    {
        var merged = SettingsLayers.Merge([Doc("""{"crosshair":"Both","blink":false}"""), Doc("""{"crosshair":"Vertical"}""")]);

        Assert.Equal(new AppSettings(CrosshairMode.Vertical, Blink: false), SettingsLayers.Read(merged));
    }

    [Fact]
    public void No_layers_at_all_read_as_every_default()
    {
        Assert.Equal(new AppSettings(), SettingsLayers.Read(SettingsLayers.Merge([])));
    }

    /// <summary>One hand-edited typo costs that key alone, not the whole file: the other keys keep their values.</summary>
    [Fact]
    public void A_key_whose_value_will_not_read_is_dropped_and_the_rest_are_kept()
    {
        Assert.Equal(new AppSettings(Blink: false), SettingsLayers.Read(Doc("""{"crosshair":"Diagonal","blink":false}""")));
        Assert.Equal(new AppSettings(CrosshairMode.Both), SettingsLayers.Read(Doc("""{"crosshair":"Both","blink":"yes"}""")));
    }

    [Fact]
    public void An_unknown_key_is_ignored_on_read()
    {
        Assert.Equal(new AppSettings(Blink: false), SettingsLayers.Read(Doc("""{"blink":false,"fontSize":14}""")));
    }

    [Fact]
    public void The_user_document_holds_a_changed_key_and_omits_an_untouched_one()
    {
        var doc = SettingsLayers.UserDocument(new AppSettings(Blink: false), new AppSettings(), existing: null);

        Assert.Equal("""{"blink":false}""", doc.ToJsonString());
    }

    /// <summary>Set once, it stays written, even back to the base value: that was a choice, and a later change of
    /// built-in default must not move it.</summary>
    [Fact]
    public void A_key_already_in_the_user_document_stays_pinned_when_set_back_to_the_base_value()
    {
        var doc = SettingsLayers.UserDocument(new AppSettings(), new AppSettings(), Doc("""{"crosshair":"Both"}"""));

        Assert.Equal("""{"crosshair":"None"}""", doc.ToJsonString());
    }

    [Fact]
    public void A_known_key_with_a_bad_value_is_rewritten_from_the_record()
    {
        var doc = SettingsLayers.UserDocument(new AppSettings(Blink: false), new AppSettings(), Doc("""{"crosshair":"Diagonal"}"""));

        Assert.Equal("""{"crosshair":"None","blink":false}""", doc.ToJsonString());
    }

    [Fact]
    public void An_unknown_key_is_copied_through_verbatim()
    {
        var doc = SettingsLayers.UserDocument(new AppSettings(), new AppSettings(), Doc("""{"fontSize":14,"theme":{"name":"green"}}"""));

        Assert.Equal("""{"fontSize":14,"theme":{"name":"green"}}""", doc.ToJsonString());
    }

    /// <summary>The base beneath the user file (a future system layer) says Both; the user chose None, which is
    /// also the built-in default. It differs from the base, so it is written, or the choice would be lost.</summary>
    [Fact]
    public void A_difference_from_the_base_is_written_even_when_it_is_the_built_in_default()
    {
        var doc = SettingsLayers.UserDocument(new AppSettings(), new AppSettings(CrosshairMode.Both), existing: null);

        Assert.Equal("""{"crosshair":"None"}""", doc.ToJsonString());
    }

    [Fact]
    public void Merge_does_not_share_nodes_with_its_inputs()
    {
        var layer = Doc("""{"blink":false}""");
        var merged = SettingsLayers.Merge([layer]);
        merged["blink"] = true;

        Assert.False(layer["blink"]!.GetValue<bool>());
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~SettingsLayersTests"`
Expected: build errors naming `AppSettings` and `SettingsLayers` as unknown.

- [ ] **Step 3: Write the record and the context**

`src/LizTerm.Core/Settings/AppSettings.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Text.Json.Serialization;

namespace LizTerm.Core.Settings;

/// <summary>App-wide settings: what the user chose, as opposed to how to reach a host (that is SessionProfile).
/// Positional with a default on every parameter, as SessionProfile is, so a field added later reads as its
/// default from an older file and an older build ignores a newer file's extra key. Flat by policy:
/// SettingsLayers merges top-level keys and would replace a nested object whole. The enum is written by name so
/// the file is hand-readable and a reordering of the enum can never change a saved meaning.</summary>
public sealed record AppSettings(
    [property: JsonConverter(typeof(JsonStringEnumConverter<CrosshairMode>))] CrosshairMode Crosshair = CrosshairMode.None,
    bool Blink = true);
```

`src/LizTerm.Core/Settings/SettingsJsonContext.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Text.Json.Serialization;

namespace LizTerm.Core.Settings;

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(AppSettings))]
internal partial class SettingsJsonContext : JsonSerializerContext;
```

- [ ] **Step 4: Write `SettingsLayers`**

`src/LizTerm.Core/Settings/SettingsLayers.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Text.Json;
using System.Text.Json.Nodes;

namespace LizTerm.Core.Settings;

/// <summary>The two rules of the settings file, kept pure so every ordering and pinning case is a plain unit
/// test: how layers merge into one record, and which keys a save writes back. SettingsStore does the file I/O
/// and calls these.</summary>
public static class SettingsLayers
{
    /// <summary>Shallow overlay of top-level keys, base first, later wins per key. Nested values are replaced
    /// whole, which is why AppSettings stays flat. Nodes are cloned: a JsonNode has one parent, and the inputs
    /// stay usable.</summary>
    public static JsonObject Merge(IEnumerable<JsonObject> layers)
    {
        var merged = new JsonObject();
        foreach (var layer in layers)
            foreach (var (key, value) in layer)
                merged[key] = value?.DeepClone();
        return merged;
    }

    /// <summary>The merged document as a record. A key whose value will not read — a crosshair that is not a
    /// mode name, a blink that is not a boolean — is dropped on its own and falls to its default; every other
    /// key keeps its value, so one hand-edited typo does not cost the whole file (and the next save repairs it,
    /// see UserDocument). Unknown keys are ignored, so an older build opens a newer file.</summary>
    public static AppSettings Read(JsonObject merged)
    {
        var readable = new JsonObject();
        foreach (var (key, value) in merged)
        {
            var probe = new JsonObject { [key] = value?.DeepClone() };
            try
            {
                JsonSerializer.Deserialize(probe, SettingsJsonContext.Default.AppSettings);
                readable[key] = value?.DeepClone();
            }
            catch (JsonException)
            {
                // This key alone is unreadable; leave it out.
            }
        }
        return JsonSerializer.Deserialize(readable, SettingsJsonContext.Default.AppSettings) ?? new AppSettings();
    }

    /// <summary>The sparse document a save writes: exactly the keys <paramref name="existing"/> already holds,
    /// plus every key whose value in <paramref name="next"/> differs from <paramref name="beneath"/> — the
    /// record the layers under the user file merge to. A known key takes its value from next, so a bad value
    /// heals on the first save; an unknown key is copied from existing verbatim, so a newer build's setting
    /// survives an older build saving. A key the user never touched stays absent and keeps following the
    /// default; one they set once stays pinned, even set back to the base value, because that was a choice.</summary>
    public static JsonObject UserDocument(AppSettings next, AppSettings beneath, JsonObject? existing)
    {
        var nextDoc = JsonSerializer.SerializeToNode(next, SettingsJsonContext.Default.AppSettings)!.AsObject();
        var beneathDoc = JsonSerializer.SerializeToNode(beneath, SettingsJsonContext.Default.AppSettings)!.AsObject();
        var document = new JsonObject();
        if (existing is not null)
        {
            foreach (var (key, value) in existing)
                document[key] = nextDoc.ContainsKey(key) ? nextDoc[key]?.DeepClone() : value?.DeepClone();
        }
        foreach (var (key, value) in nextDoc)
        {
            if (document.ContainsKey(key)) continue;
            if (!JsonNode.DeepEquals(value, beneathDoc[key])) document[key] = value?.DeepClone();
        }
        return document;
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~SettingsLayersTests"`
Expected: 10 passed. If `A_key_whose_value_will_not_read_is_dropped_and_the_rest_are_kept` fails on the enum case, the converter attribute is not being honoured by the source generator; check that the attribute target is `property:` and that `JsonStringEnumConverter<CrosshairMode>` (the generic form) is used.

- [ ] **Step 6: Zero-warning check, then commit**

Run: `dotnet build LizTerm.slnx --no-incremental 2>&1 | grep -c " warning "` — Expected: `0`.

```bash
git add src/LizTerm.Core/Settings tests/LizTerm.Core.Tests/Settings
git commit -m "Add the settings record and the pure layering rules

AppSettings is flat and positional with a default per parameter. SettingsLayers
merges layers per top-level key, reads a merged document dropping only the keys
that will not deserialise, and computes the sparse user document a save writes:
the keys already there plus the ones that differ from the layers beneath.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 3: `SettingsStore` and `AppPaths.SettingsFile()`

**Files:**
- Create: `src/LizTerm.Core/Settings/SettingsStore.cs`
- Modify: `src/LizTerm.Core/Profiles/AppPaths.cs`
- Test: `tests/LizTerm.Core.Tests/Settings/SettingsStoreTests.cs`, `tests/LizTerm.Core.Tests/Profiles/AppPathsTests.cs`

**Interfaces:**
- Consumes: `SettingsLayers`, `AppSettings` (Task 2).
- Produces: `public sealed class SettingsStore(string filePath)` with `string FilePath`, `static string DefaultFile()`, `AppSettings Load()` (never throws), `AppSettings Update(Func<AppSettings, AppSettings> change)` (throws `InvalidDataException` naming the file when it is not a JSON object; `IOException`/`UnauthorizedAccessException` propagate); `AppPaths.SettingsFile()` = `<ConfigRoot>/settings.json`.

- [ ] **Step 1: Write the failing tests**

Add to `tests/LizTerm.Core.Tests/Profiles/AppPathsTests.cs`, after the `LogsDirectory` assertion in `Profiles_and_logs_are_siblings_under_the_config_root` (and add `using LizTerm.Core.Settings;` at the top):

```csharp
        Assert.Equal(Path.Combine(root, "settings.json"), AppPaths.SettingsFile());
        Assert.Equal(AppPaths.SettingsFile(), SettingsStore.DefaultFile());
```

Create `tests/LizTerm.Core.Tests/Settings/SettingsStoreTests.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Text.Json.Nodes;
using LizTerm.Core.Settings;

namespace LizTerm.Core.Tests.Settings;

public class SettingsStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lizterm-tests-" + Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_dir, "settings.json");
    private SettingsStore Store => new(FilePath);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private void WriteFile(string content)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, content);
    }

    private JsonObject ReadFile() => JsonNode.Parse(File.ReadAllText(FilePath))!.AsObject();

    [Fact]
    public void A_missing_file_loads_every_default_and_creates_nothing()
    {
        Assert.Equal(new AppSettings(), Store.Load());
        Assert.False(Directory.Exists(_dir));
    }

    [Fact]
    public void Update_then_Load_round_trips_and_writes_the_enum_by_name()
    {
        Store.Update(s => s with { Crosshair = CrosshairMode.Both, Blink = false });

        Assert.Equal(new AppSettings(CrosshairMode.Both, Blink: false), Store.Load());
        Assert.Equal("Both", ReadFile()["crosshair"]!.GetValue<string>());
    }

    [Fact]
    public void A_first_change_writes_only_that_key()
    {
        Store.Update(s => s with { Blink = false });

        Assert.Equal(["blink"], ReadFile().Select(p => p.Key));
    }

    [Fact]
    public void Update_returns_what_it_wrote()
    {
        var written = Store.Update(s => s with { Crosshair = CrosshairMode.Vertical });

        Assert.Equal(new AppSettings(CrosshairMode.Vertical), written);
    }

    [Fact]
    public void A_file_with_a_bad_value_loads_the_rest_and_the_next_update_repairs_it()
    {
        WriteFile("""{"crosshair":"Diagonal","blink":false}""");
        Assert.Equal(new AppSettings(Blink: false), Store.Load());

        Store.Update(s => s with { Blink = true });

        Assert.Equal(new AppSettings(), Store.Load());
        Assert.Equal("None", ReadFile()["crosshair"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("")]
    [InlineData("[1, 2, 3]")]
    public void A_file_that_is_not_a_json_object_loads_defaults_and_refuses_to_be_overwritten(string content)
    {
        WriteFile(content);
        Assert.Equal(new AppSettings(), Store.Load());

        var ex = Assert.Throws<InvalidDataException>(() => Store.Update(s => s with { Blink = false }));

        Assert.Contains(FilePath, ex.Message);
        Assert.Contains("fix or delete it", ex.Message);
        Assert.Equal(content, File.ReadAllText(FilePath));
        Assert.Equal(["settings.json"], Directory.GetFiles(_dir).Select(Path.GetFileName));
    }

    /// <summary>Two LizTerm processes: the second saved a crosshair between the first's load and its save. The
    /// first changes only blink, so the crosshair it never touched survives — ProfileStore.Update's rule.</summary>
    [Fact]
    public void Update_applies_the_change_to_what_is_on_disk_now()
    {
        var first = Store;
        first.Load();
        new SettingsStore(FilePath).Update(s => s with { Crosshair = CrosshairMode.Horizontal });

        first.Update(s => s with { Blink = false });

        Assert.Equal(new AppSettings(CrosshairMode.Horizontal, Blink: false), first.Load());
    }

    [Fact]
    public void An_unknown_key_survives_an_update()
    {
        WriteFile("""{"fontSize":14}""");

        Store.Update(s => s with { Blink = false });

        var doc = ReadFile();
        Assert.Equal(14, doc["fontSize"]!.GetValue<int>());
        Assert.False(doc["blink"]!.GetValue<bool>());
    }

    [Fact]
    public void Update_never_leaves_a_temp_file_behind()
    {
        Store.Update(s => s with { Blink = false });
        Store.Update(s => s with { Blink = true });

        Assert.Equal(["settings.json"], Directory.GetFiles(_dir).Select(Path.GetFileName));
        Assert.Equal("""{"blink":true}""", ReadFile().ToJsonString());
    }

    [Fact]
    public void The_file_is_written_indented_for_hand_editing()
    {
        Store.Update(s => s with { Blink = false });

        Assert.Contains("\n", File.ReadAllText(FilePath));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~SettingsStoreTests|FullyQualifiedName~AppPathsTests"`
Expected: build errors naming `SettingsStore` and `AppPaths.SettingsFile`.

- [ ] **Step 3: Add the path**

In `src/LizTerm.Core/Profiles/AppPaths.cs`, after `LogsDirectory()`:

```csharp
    /// <summary>App-wide settings, one file beside profiles/ and logs/. Holds only the keys the user has set
    /// (see LizTerm.Core.Settings.SettingsLayers), so deleting it restores every default.</summary>
    public static string SettingsFile() => Path.Combine(ConfigRoot(), "settings.json");
```

and change the class summary to `/// <summary>Per-OS locations of LizTerm's own files: profiles, wire logs and the settings file live side by side under one root.</summary>`.

- [ ] **Step 4: Write the store**

`src/LizTerm.Core/Settings/SettingsStore.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Text.Json;
using System.Text.Json.Nodes;
using LizTerm.Core.Profiles;

namespace LizTerm.Core.Settings;

/// <summary>One JSON file of app-wide settings holding only the keys the user has set (SettingsLayers has the
/// rules). This is the file-backed half: Load never throws, and Update applies a change to what is on disk
/// *now*, as ProfileStore.Update does, so two LizTerm processes cannot lose each other's keys.</summary>
public sealed class SettingsStore(string filePath)
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    /// <summary>What the layers under the user file merge to. Every default until a system layer exists (#54,
    /// #55); when one does, it is one more entry in the list Merge takes, ahead of the user file.</summary>
    private static readonly AppSettings Beneath = new();

    public string FilePath { get; } = filePath;

    public static string DefaultFile() => AppPaths.SettingsFile();

    /// <summary>The settings in force. A file that is missing, unreadable, not JSON or not a JSON object is
    /// skipped; a key whose value will not read falls to its default alone. Nothing is written here.</summary>
    public AppSettings Load() => SettingsLayers.Read(SettingsLayers.Merge(Layers(ReadLenient())));

    /// <summary>Applies <paramref name="change"/> to the settings as they are on disk now and writes the sparse
    /// user document for the result; returns what it wrote. Throws InvalidDataException, naming the file, when
    /// the file exists but is not a JSON object — rather than replace something the user may be editing by hand.
    /// IO and permission errors, from the re-read or the write, propagate.</summary>
    public AppSettings Update(Func<AppSettings, AppSettings> change)
    {
        var existing = ReadStrict();
        var next = change(SettingsLayers.Read(SettingsLayers.Merge(Layers(existing))));
        Write(SettingsLayers.UserDocument(next, Beneath, existing).ToJsonString(Indented));
        return next;
    }

    private static IEnumerable<JsonObject> Layers(JsonObject? user) => user is null ? [] : [user];

    private JsonObject? ReadLenient()
    {
        string text;
        try
        {
            text = File.ReadAllText(FilePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
        return Parse(text);
    }

    private JsonObject? ReadStrict()
    {
        if (!File.Exists(FilePath)) return null;
        return Parse(File.ReadAllText(FilePath))
               ?? throw new InvalidDataException($"{Path.GetFileName(FilePath)} is not valid JSON; fix or delete it: {FilePath}");
    }

    /// <summary>The text as a JSON object, or null for anything else: not JSON, or JSON that is not an object.</summary>
    private static JsonObject? Parse(string text)
    {
        try
        {
            return JsonNode.Parse(text) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Through a sibling temp file renamed over the target, so a reader never sees a partial file, and
    /// a write interrupted by a crash or a full disk leaves the old file rather than a broken one. The temp file
    /// is a sibling on purpose: File.Move across a filesystem is a copy, which is not atomic.</summary>
    private void Write(string json)
    {
        if (Path.GetDirectoryName(FilePath) is { Length: > 0 } directory) Directory.CreateDirectory(directory);
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

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~SettingsStoreTests|FullyQualifiedName~AppPathsTests"`
Expected: all pass (12 in `SettingsStoreTests`, counting the theory's three rows, plus `AppPathsTests`).

- [ ] **Step 6: Zero-warning check, then commit**

Run: `dotnet build LizTerm.slnx --no-incremental 2>&1 | grep -c " warning "` — Expected: `0`.

```bash
git add src/LizTerm.Core tests/LizTerm.Core.Tests
git commit -m "Add SettingsStore: a sparse settings.json under the config root

Load never throws; Update re-reads the file and applies the change to what is
on disk now, writes only the keys already there plus the ones that changed,
and refuses to overwrite a file that is not a JSON object.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 4: `SettingsViewModel`, the one live settings object

**Files:**
- Create: `src/LizTerm.App/ViewModels/SettingsViewModel.cs`
- Test: `tests/LizTerm.App.Tests/ViewModels/SettingsViewModelTests.cs`

**Interfaces:**
- Consumes: `SettingsStore`, `AppSettings`, `CrosshairMode` (Tasks 1–3).
- Produces: `public sealed class SettingsViewModel : ObservableObject` with constructors `()` (in-memory) and `(SettingsStore store)`; `AppSettings Current { get; }`; `CrosshairMode Crosshair { get; set; }`; `bool Blink { get; set; }`; `string? LastSaveError { get; }` (observable); `event EventHandler<string>? SaveFailed`.

- [ ] **Step 1: Write the failing tests**

`tests/LizTerm.App.Tests/ViewModels/SettingsViewModelTests.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.ComponentModel;
using LizTerm.App.ViewModels;
using LizTerm.Core.Settings;

namespace LizTerm.App.Tests.ViewModels;

public class SettingsViewModelTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lizterm-tests-" + Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_dir, "settings.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private static List<string> Changes(INotifyPropertyChanged source)
    {
        var names = new List<string>();
        source.PropertyChanged += (_, e) => names.Add(e.PropertyName!);
        return names;
    }

    [Fact]
    public void An_in_memory_instance_starts_at_every_default_and_raises_changes()
    {
        var settings = new SettingsViewModel();
        var changes = Changes(settings);
        Assert.Equal(new AppSettings(), settings.Current);

        settings.Crosshair = CrosshairMode.Both;
        settings.Blink = false;

        Assert.Equal(new AppSettings(CrosshairMode.Both, Blink: false), settings.Current);
        Assert.Equal(["Crosshair", "Blink"], changes);
        Assert.Null(settings.LastSaveError);
    }

    [Fact]
    public void The_same_value_again_raises_nothing_and_writes_nothing()
    {
        var settings = new SettingsViewModel(new SettingsStore(FilePath));
        var changes = Changes(settings);

        settings.Blink = true;
        settings.Crosshair = CrosshairMode.None;

        Assert.Empty(changes);
        Assert.False(File.Exists(FilePath));
    }

    [Fact]
    public void A_store_backed_instance_writes_through_and_a_fresh_one_reads_it_back()
    {
        _ = new SettingsViewModel(new SettingsStore(FilePath)) { Crosshair = CrosshairMode.Vertical, Blink = false };

        var reloaded = new SettingsViewModel(new SettingsStore(FilePath));

        Assert.Equal(CrosshairMode.Vertical, reloaded.Crosshair);
        Assert.False(reloaded.Blink);
    }

    /// <summary>The in-memory change always wins: the UI must show what the user chose even when the disk
    /// refuses it. The failure is reported twice over — an event for banners that need every failure, a
    /// property for the window that shows the latest.</summary>
    [Fact]
    public void A_failed_save_keeps_the_change_and_reports_it()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, "not json");
        var settings = new SettingsViewModel(new SettingsStore(FilePath));
        string? reported = null;
        settings.SaveFailed += (_, message) => reported = message;

        settings.Blink = false;

        Assert.False(settings.Blink);
        Assert.StartsWith("Could not save settings: ", reported);
        Assert.Contains(FilePath, reported);
        Assert.Equal(reported, settings.LastSaveError);
    }

    [Fact]
    public void The_next_successful_save_clears_the_error()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, "not json");
        var settings = new SettingsViewModel(new SettingsStore(FilePath));
        settings.Blink = false;
        Assert.NotNull(settings.LastSaveError);
        var changes = Changes(settings);

        File.Delete(FilePath);
        settings.Crosshair = CrosshairMode.Both;

        Assert.Null(settings.LastSaveError);
        Assert.Equal(["Crosshair", "LastSaveError"], changes);
    }

    [Fact]
    public void A_failed_save_raises_the_error_property_change_after_the_value_change()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, "not json");
        var settings = new SettingsViewModel(new SettingsStore(FilePath));
        var changes = Changes(settings);

        settings.Blink = false;

        Assert.Equal(["Blink", "LastSaveError"], changes);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~SettingsViewModelTests"`
Expected: build errors naming `SettingsViewModel`.

- [ ] **Step 3: Write the view model**

`src/LizTerm.App/ViewModels/SettingsViewModel.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using CommunityToolkit.Mvvm.ComponentModel;
using LizTerm.Core.Settings;

namespace LizTerm.App.ViewModels;

/// <summary>The app-wide settings as one live object: every session window and the Preferences window bind to
/// the same instance, which is the whole of how a change in one reaches the others. One per process, owned by
/// App; a view model built without one gets an in-memory instance, which is what every test wants.
///
/// A setter that changes nothing returns early. One that does applies the change in memory first and raises
/// the property change, then writes through the store — the in-memory change always wins, because the UI
/// must show what the user chose even when the disk refuses it. A save that throws sets LastSaveError and
/// raises SaveFailed; the event fires on every failure, where a property whose text has not changed would not
/// notify a banner the user had dismissed. UI thread only: the menus and the window that set it are.</summary>
public sealed class SettingsViewModel : ObservableObject
{
    private readonly SettingsStore? _store;
    private string? _lastSaveError;

    /// <summary>In-memory only: every default, nothing written.</summary>
    public SettingsViewModel() => Current = new AppSettings();

    /// <summary>Loaded from the store now, and written through on every change.</summary>
    public SettingsViewModel(SettingsStore store)
    {
        _store = store;
        Current = store.Load();
    }

    /// <summary>The record as this process sees it. Another process's later write is seen at the next launch;
    /// SettingsStore.Update keeps that process's keys, this object keeps its own view.</summary>
    public AppSettings Current { get; private set; }

    /// <summary>The last save's failure, or null once a save succeeds. The Preferences window shows it.</summary>
    public string? LastSaveError
    {
        get => _lastSaveError;
        private set => SetProperty(ref _lastSaveError, value);
    }

    /// <summary>Every failed save, with the message the banner shows.</summary>
    public event EventHandler<string>? SaveFailed;

    public CrosshairMode Crosshair
    {
        get => Current.Crosshair;
        set
        {
            if (Current.Crosshair != value) Apply(nameof(Crosshair), s => s with { Crosshair = value });
        }
    }

    public bool Blink
    {
        get => Current.Blink;
        set
        {
            if (Current.Blink != value) Apply(nameof(Blink), s => s with { Blink = value });
        }
    }

    private void Apply(string property, Func<AppSettings, AppSettings> change)
    {
        Current = change(Current);
        OnPropertyChanged(property);
        if (_store is null) return;
        try
        {
            _store.Update(change);
            LastSaveError = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            LastSaveError = "Could not save settings: " + ex.Message;
            SaveFailed?.Invoke(this, LastSaveError);
        }
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~SettingsViewModelTests"`
Expected: 6 passed.

- [ ] **Step 5: Zero-warning check, then commit**

Run: `dotnet build LizTerm.slnx --no-incremental 2>&1 | grep -c " warning "` — Expected: `0`.

```bash
git add src/LizTerm.App/ViewModels/SettingsViewModel.cs tests/LizTerm.App.Tests/ViewModels/SettingsViewModelTests.cs
git commit -m "Add SettingsViewModel, the one live settings object per process

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 5: The session view model and window take the shared settings

The crosshair stops being per-window state. Every session view model holds the process's `SettingsViewModel`, the window binds to it, and the View menu writes through to it.

**Files:**
- Modify: `src/LizTerm.App/ViewModels/SessionViewModel.cs`, `src/LizTerm.App/Views/SessionWindow.axaml`, `src/LizTerm.App/Views/SessionWindow.axaml.cs`, `src/LizTerm.App/App.axaml.cs`
- Test: `tests/LizTerm.App.Tests/ViewModels/SessionViewModelTests.cs`, `tests/LizTerm.App.Tests/Views/NativeMenuTests.cs`

**Interfaces:**
- Consumes: `SettingsViewModel` (Task 4).
- Produces: `SessionViewModel(..., Func<SessionProfile, Task>? saveAsProfile = null, SettingsViewModel? settings = null)`; `public SettingsViewModel Settings { get; }`; `SessionViewModel.Crosshair` is gone. `App.Settings` (internal, lazily created from `AppPaths.SettingsFile()`).

- [ ] **Step 1: Write the failing tests**

In `tests/LizTerm.App.Tests/ViewModels/SessionViewModelTests.cs`, add `using LizTerm.Core.Settings;` and these tests at the end of the class:

```csharp
    [Fact]
    public void Without_a_settings_object_the_view_model_makes_an_in_memory_one()
    {
        var (vm, _) = Create();

        Assert.Equal(new AppSettings(), vm.Settings.Current);
        vm.Settings.Crosshair = CrosshairMode.Both; // nothing on disk anywhere; must not throw
        Assert.Equal(CrosshairMode.Both, vm.Settings.Crosshair);
    }

    /// <summary>Every window's view model holds the one SettingsViewModel; property change notification on it
    /// is the whole of the live-propagation mechanism.</summary>
    [Fact]
    public void Two_view_models_on_one_settings_object_see_each_others_crosshair()
    {
        var settings = new SettingsViewModel();
        var a = new SessionViewModel(new FakeEmulatorSession(), x => x(), new FakeTextClipboard(), settings: settings);
        var b = new SessionViewModel(new FakeEmulatorSession(), x => x(), new FakeTextClipboard(), settings: settings);
        var raised = 0;
        b.Settings.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(SettingsViewModel.Crosshair)) raised++; };

        a.Settings.Crosshair = CrosshairMode.Horizontal;

        Assert.Equal(CrosshairMode.Horizontal, b.Settings.Crosshair);
        Assert.Equal(1, raised);
    }

    private static (string Dir, SettingsViewModel Settings) FailingSettings()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lizterm-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "settings.json");
        File.WriteAllText(file, "not json");
        return (dir, new SettingsViewModel(new SettingsStore(file)));
    }

    [Fact]
    public void A_failed_settings_save_reaches_the_error_banner_and_the_change_still_shows()
    {
        var (dir, settings) = FailingSettings();
        try
        {
            var vm = new SessionViewModel(new FakeEmulatorSession(), a => a(), new FakeTextClipboard(), settings: settings);

            settings.Crosshair = CrosshairMode.Both;

            Assert.StartsWith("Could not save settings: ", vm.ErrorMessage);
            Assert.Contains("settings.json", vm.ErrorMessage);
            Assert.Equal(CrosshairMode.Both, vm.Settings.Crosshair);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task A_disposed_view_model_no_longer_reports_failed_saves()
    {
        var (dir, settings) = FailingSettings();
        try
        {
            var vm = new SessionViewModel(new FakeEmulatorSession(), a => a(), new FakeTextClipboard(), settings: settings);
            await vm.DisposeAsync();

            settings.Blink = false;

            Assert.Null(vm.ErrorMessage);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
```

In `tests/LizTerm.App.Tests/Views/NativeMenuTests.cs`, change the two references to the old property: in `Choosing_a_crosshair_mode_checks_exactly_that_item`, `Assert.Equal(modes[chosen], vm.Crosshair);` becomes `Assert.Equal(modes[chosen], vm.Settings.Crosshair);`, and in `The_crosshair_reaches_the_terminal_screen`, `vm.Crosshair = CrosshairMode.Both;` becomes `vm.Settings.Crosshair = CrosshairMode.Both;`.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~SessionViewModelTests|FullyQualifiedName~NativeMenuTests"`
Expected: build errors — `SessionViewModel` has no `Settings` and no `settings:` parameter.

- [ ] **Step 3: Change the view model**

In `src/LizTerm.App/ViewModels/SessionViewModel.cs`:

Delete the `Crosshair` field and its comment (the three lines beginning `/// <summary>Which crosshair lines follow the cursor, for this window only.` through `[ObservableProperty] private CrosshairMode _crosshair;`). Delete `using LizTerm.Core.Settings;` if nothing else in the file uses it after that.

Add a field beside `_onHostMessage`:

```csharp
    private readonly EventHandler<string> _onSettingsSaveFailed;
```

Add the property, after `public TimeSpan ConnectTimeout { get; set; } = DefaultConnectTimeout;`:

```csharp
    /// <summary>The app-wide settings, shared with every other window and with Preferences (spec §4.2). The
    /// crosshair used to be a field here, per window; it is display preference, and now lives where the
    /// screen spec said it eventually would.</summary>
    public SettingsViewModel Settings { get; }
```

Change the constructor signature and body. The new parameter goes last, with its doc comment:

```csharp
    /// <param name="settings">The process's settings object; null builds an in-memory one, which is what tests
    /// want and what keeps them off the settings file.</param>
    public SessionViewModel(IEmulatorSession session, Action<Action> dispatch, ITextClipboard clipboard,
        ICertificatePrompt? certificatePrompt = null, Action<SessionProfile>? saveProfile = null,
        IFolderOpener? folderOpener = null, ICertificateFetcher? certificateFetcher = null,
        Func<SessionProfile, Task>? saveAsProfile = null, SettingsViewModel? settings = null)
    {
        _session = session;
        _dispatch = dispatch;
        _clipboard = clipboard;
        _certificatePrompt = certificatePrompt;
        _saveProfile = saveProfile;
        _folderOpener = folderOpener;
        _certificateFetcher = certificateFetcher;
        _saveAsProfile = saveAsProfile;
        Settings = settings ?? new SettingsViewModel();

        // Already on the UI thread: the settings are only ever set from a menu or the Preferences window.
        _onSettingsSaveFailed = (_, message) =>
        {
            if (!_disposed) ErrorMessage = message;
        };
        Settings.SaveFailed += _onSettingsSaveFailed;
```

(the rest of the constructor continues unchanged from `_onScreenUpdated = ...`).

In `DisposeAsync`, after `_session.HostMessage -= _onHostMessage;`, add:

```csharp
        Settings.SaveFailed -= _onSettingsSaveFailed;
```

- [ ] **Step 4: Rebind the window**

In `src/LizTerm.App/Views/SessionWindow.axaml`, every `{Binding Crosshair` becomes `{Binding Settings.Crosshair`: the four native radio items, the four classic radio items, and the screen element:

```bash
grep -c "{Binding Crosshair" src/LizTerm.App/Views/SessionWindow.axaml   # expected: 9
sed -i '' 's/{Binding Crosshair/{Binding Settings.Crosshair/g' src/LizTerm.App/Views/SessionWindow.axaml
grep -c "{Binding Settings.Crosshair" src/LizTerm.App/Views/SessionWindow.axaml   # expected: 9
```

In `src/LizTerm.App/Views/SessionWindow.axaml.cs`, `SetCrosshair` becomes:

```csharp
    private void SetCrosshair(CrosshairMode mode)
    {
        if (ViewModel is { } vm) vm.Settings.Crosshair = mode;
    }
```

- [ ] **Step 5: Give the app one settings object and pass it to every session**

In `src/LizTerm.App/App.axaml.cs`, add `using LizTerm.Core.Settings;` to the usings. Add beside `private ProfileStore? _store;`:

```csharp
    private SettingsViewModel? _settings;

    /// <summary>The process's one settings object, for every session window and for Preferences. Lazy with ??=
    /// for the same reason _store is: the headless test lifetime never runs OnFrameworkInitializationCompleted.</summary>
    internal SettingsViewModel Settings => _settings ??= new SettingsViewModel(new SettingsStore(AppPaths.SettingsFile()));
```

In `OnFrameworkInitializationCompleted`, after `_store = new ProfileStore(AppPaths.ProfilesDirectory());`, add:

```csharp
            _settings = new SettingsViewModel(new SettingsStore(AppPaths.SettingsFile()));
```

In `OpenSession`, the `new SessionViewModel(` call gains a last argument after the `saveAsProfile` lambda's closing `}`:

```csharp
            },
            settings: Settings);
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/LizTerm.App.Tests`
Expected: all pass, including the four new `SessionViewModelTests` and the two adjusted `NativeMenuTests`. `Choosing_a_crosshair_mode_checks_exactly_that_item` passing proves the native radio items still render from the new binding path.

- [ ] **Step 7: Zero-warning check, then commit**

Run: `dotnet build LizTerm.slnx --no-incremental 2>&1 | grep -c " warning "` — Expected: `0`.

```bash
git add src/LizTerm.App tests/LizTerm.App.Tests
git commit -m "Share one SettingsViewModel across every session window

The crosshair leaves SessionViewModel for the app-wide settings object, which
the app creates once and hands to every session; the View menu writes through
to it, and a failed save reaches the error banner.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 6: `TerminalScreen.BlinkEnabled`

**Files:**
- Modify: `src/LizTerm.App/Controls/TerminalScreen.cs`, `src/LizTerm.App/Views/SessionWindow.axaml`
- Test: `tests/LizTerm.App.Tests/Controls/TerminalScreenBlinkTests.cs`, `tests/LizTerm.App.Tests/Views/NativeMenuTests.cs`

**Interfaces:**
- Consumes: `SettingsViewModel.Blink` (Task 4), `SessionViewModel.Settings` (Task 5).
- Produces: `TerminalScreen.BlinkEnabledProperty` (`StyledProperty<bool>`, default `true`) and `bool BlinkEnabled`.

- [ ] **Step 1: Write the failing tests**

Add to `tests/LizTerm.App.Tests/Controls/TerminalScreenBlinkTests.cs`:

```csharp
    [AvaloniaFact]
    public void It_blinks_by_default()
    {
        Assert.True(new TerminalScreen().BlinkEnabled);
    }

    /// <summary>Off means steady: the timer never runs, so the hidden phase never comes, and a new blinking
    /// screen while disabled starts nothing. Re-enabling picks the current screen up again.</summary>
    [AvaloniaFact]
    public void Disabling_blink_stops_the_timer_and_shows_the_text_steady()
    {
        var screen = new TerminalScreen { Snapshot = WithBlink() };
        var window = new Window { Width = 800, Height = 600, Content = screen };
        window.Show();
        Assert.True(screen.BlinkTimerRunning);

        screen.BlinkEnabled = false;
        Assert.False(screen.BlinkTimerRunning);
        Assert.False(screen.BlinkHidden);

        screen.Snapshot = WithBlink();
        Assert.False(screen.BlinkTimerRunning);

        screen.BlinkEnabled = true;
        Assert.True(screen.BlinkTimerRunning);
    }
```

Add to `tests/LizTerm.App.Tests/Views/NativeMenuTests.cs`, after `The_crosshair_reaches_the_terminal_screen`:

```csharp
    [AvaloniaFact]
    public void The_blink_setting_reaches_the_terminal_screen()
    {
        var (window, vm, _, _) = Show();
        var screen = window.FindControl<TerminalScreen>("Screen")!;
        Assert.True(screen.BlinkEnabled);

        vm.Settings.Blink = false;

        Assert.False(screen.BlinkEnabled);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~TerminalScreenBlinkTests|FullyQualifiedName~The_blink_setting_reaches"`
Expected: build errors — `TerminalScreen` has no `BlinkEnabled`.

- [ ] **Step 3: Add the property**

In `src/LizTerm.App/Controls/TerminalScreen.cs`, after `CurrentMatchProperty`'s declaration:

```csharp
    /// <summary>Whether text the host marks as blinking actually blinks. Off draws it steady: the timer never
    /// runs, so the hidden phase never comes. A preference (SettingsViewModel.Blink), not a snapshot property —
    /// the snapshot says what the host asked for, this says whether the user wants to see it.</summary>
    public static readonly StyledProperty<bool> BlinkEnabledProperty =
        AvaloniaProperty.Register<TerminalScreen, bool>(nameof(BlinkEnabled), defaultValue: true);
```

In the static constructor, add `BlinkEnabledProperty` to the `AffectsRender` list:

```csharp
        AffectsRender<TerminalScreen>(SnapshotProperty, SelectionProperty, CrosshairProperty,
            FindMatchesProperty, CurrentMatchProperty, BlinkEnabledProperty);
```

Beside the `Crosshair` property accessor (around line 144), add:

```csharp
    public bool BlinkEnabled
    {
        get => GetValue(BlinkEnabledProperty);
        set => SetValue(BlinkEnabledProperty, value);
    }
```

In `UpdateBlinkTimer`, the first line becomes:

```csharp
        var wanted = _attached && BlinkEnabled && snapshot is { HasBlink: true };
```

In `OnPropertyChanged`, before `if (change.Property != SnapshotProperty) return;`, add:

```csharp
        if (change.Property == BlinkEnabledProperty)
        {
            // AffectsRender repaints; this decides whether the timer runs against the screen already showing.
            UpdateBlinkTimer(Snapshot);
            return;
        }
```

- [ ] **Step 4: Bind it**

In `src/LizTerm.App/Views/SessionWindow.axaml`, the screen element gains one attribute, after `Crosshair="{Binding Settings.Crosshair}"`:

```xml
                             BlinkEnabled="{Binding Settings.Blink}"
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/LizTerm.App.Tests`
Expected: all pass. The existing `Timer_runs_only_while_the_snapshot_has_blinking_cells` and `Leaving_the_tree_stops_the_timer_and_returning_restarts_it` still pass, since the default is `true`.

- [ ] **Step 6: Zero-warning check, then commit**

Run: `dotnet build LizTerm.slnx --no-incremental 2>&1 | grep -c " warning "` — Expected: `0`.

```bash
git add src/LizTerm.App tests/LizTerm.App.Tests
git commit -m "Let the blink setting switch the terminal's blink timer off

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 7: The Preferences window, `App.ShowPreferences`, and the application menu

**Files:**
- Create: `src/LizTerm.App/Views/PreferencesWindow.axaml`, `src/LizTerm.App/Views/PreferencesWindow.axaml.cs`
- Modify: `src/LizTerm.App/App.axaml`, `src/LizTerm.App/App.axaml.cs`
- Test: `tests/LizTerm.App.Tests/Views/PreferencesWindowTests.cs`, `tests/LizTerm.App.Tests/Views/NativeMenuTests.cs`

**Interfaces:**
- Consumes: `SettingsViewModel`, `CrosshairModeConverter` (existing, one-way), `App.Settings` (Task 5).
- Produces: `PreferencesWindow(SettingsViewModel settings)` with named controls `CrosshairNone`, `CrosshairHorizontal`, `CrosshairVertical`, `CrosshairBoth` (`RadioButton`), `BlinkBox` (`CheckBox`), `SaveErrorText` (`TextBlock`), `DoneButton` (`Button`); `App.ShowPreferences()` (public, uses `App.Settings`) and `internal PreferencesWindow ShowPreferences(SettingsViewModel settings)`; the application menu item "Preferences..." with gesture Cmd+OemComma.

- [ ] **Step 1: Write the failing tests**

`tests/LizTerm.App.Tests/Views/PreferencesWindowTests.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using LizTerm.App.ViewModels;
using LizTerm.App.Views;
using LizTerm.Core.Settings;

namespace LizTerm.App.Tests.Views;

public class PreferencesWindowTests
{
    private static (PreferencesWindow Window, SettingsViewModel Settings) Show(SettingsViewModel? settings = null)
    {
        settings ??= new SettingsViewModel();
        var window = new PreferencesWindow(settings);
        window.Show();
        return (window, settings);
    }

    /// <summary>Raised through Button.ClickEvent, which is what the Click handlers listen to; assigning
    /// IsChecked would only prove that the one-way binding renders.</summary>
    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    [AvaloniaFact]
    public void Clicking_a_crosshair_radio_sets_the_shared_settings_and_checks_exactly_that_radio()
    {
        var (window, settings) = Show();
        var radios = new[] { "CrosshairNone", "CrosshairHorizontal", "CrosshairVertical", "CrosshairBoth" }
            .Select(name => window.FindControl<RadioButton>(name)!).ToArray();
        var modes = new[] { CrosshairMode.None, CrosshairMode.Horizontal, CrosshairMode.Vertical, CrosshairMode.Both };
        Assert.Equal([true, false, false, false], radios.Select(r => r.IsChecked == true));

        for (var chosen = 0; chosen < modes.Length; chosen++)
        {
            Click(radios[chosen]);

            Assert.Equal(modes[chosen], settings.Crosshair);
            Assert.Equal(Enumerable.Range(0, modes.Length).Select(i => i == chosen), radios.Select(r => r.IsChecked == true));
        }
    }

    [AvaloniaFact]
    public void The_radios_follow_a_change_made_elsewhere()
    {
        var (window, settings) = Show();

        settings.Crosshair = CrosshairMode.Vertical;

        Assert.True(window.FindControl<RadioButton>("CrosshairVertical")!.IsChecked);
        Assert.False(window.FindControl<RadioButton>("CrosshairNone")!.IsChecked);
    }

    [AvaloniaFact]
    public void The_blink_box_writes_through_and_follows_the_settings()
    {
        var (window, settings) = Show();
        var box = window.FindControl<CheckBox>("BlinkBox")!;
        Assert.True(box.IsChecked);

        box.IsChecked = false;
        Assert.False(settings.Blink);

        settings.Blink = true;
        Assert.True(box.IsChecked);
    }

    [AvaloniaFact]
    public void A_failed_save_shows_its_message_in_the_window()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lizterm-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "settings.json");
        File.WriteAllText(file, "not json");
        try
        {
            var (window, _) = Show(new SettingsViewModel(new SettingsStore(file)));
            var text = window.FindControl<TextBlock>("SaveErrorText")!;
            Assert.True(string.IsNullOrEmpty(text.Text));

            window.FindControl<CheckBox>("BlinkBox")!.IsChecked = false;

            Assert.StartsWith("Could not save settings: ", text.Text);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [AvaloniaFact]
    public void Done_closes_the_window()
    {
        var (window, _) = Show();

        Click(window.FindControl<Button>("DoneButton")!);

        Assert.False(window.IsVisible);
    }

    [AvaloniaFact]
    public void It_is_titled_preferences_and_sizes_to_its_content()
    {
        var (window, _) = Show();

        Assert.Equal("Preferences", window.Title);
        Assert.Equal(SizeToContent.Height, window.SizeToContent);
    }

    /// <summary>App's one-at-a-time rule, through the internal seam that takes a settings object so the test
    /// never touches the real settings file. The public ShowPreferences() uses App.Settings.</summary>
    [AvaloniaFact]
    public void The_app_shows_one_preferences_window_at_a_time()
    {
        var app = (App)Application.Current!;
        var settings = new SettingsViewModel();

        var first = app.ShowPreferences(settings);
        Assert.Same(first, app.ShowPreferences(settings));

        first.Close();
        var again = app.ShowPreferences(settings);
        Assert.NotSame(first, again);
        again.Close();
    }
}
```

If `The_app_shows_one_preferences_window_at_a_time` or `Done_closes_the_window` fails because `Closed` has not fired yet under headless, add `Dispatcher.UIThread.RunJobs();` (from `Avalonia.Threading`) after each `Close()`; the other window tests in this project have not needed it.

In `tests/LizTerm.App.Tests/Views/NativeMenuTests.cs`, replace the two application-menu tests at the top of the class (`The_application_menu_declares_about_and_no_quit_of_its_own` and `The_application_menu_carries_no_gesture`) with:

```csharp
    /// <summary>What the application menu *declares*, which on macOS is not what a user sees. Avalonia's
    /// AvaloniaNativeMenuExporter.SetMenu appends AppKit's standard block — Services, Hide, Hide Others, Show All
    /// and Quit — to this very NativeMenu instance unless MacOSPlatformOptions.DisableDefaultApplicationMenuItems
    /// is set, which Program.cs does not set. The standard block is therefore Avalonia's to supply and is
    /// invisible to this test: the native exporter never runs under headless, so nothing here can observe it.
    ///
    /// Which is why there is no Quit of our own. Measured on macOS 15 / Avalonia 12.1.2, declaring one shipped
    /// two Quit items with the same Cmd+Q and opposite behaviour: ours called App.Quit() → Shutdown() (forced),
    /// Avalonia's calls TryShutdown(0), which a running IND$FILE transfer correctly refuses. The non-forcing one
    /// is the semantic this app wants.</summary>
    [AvaloniaFact]
    public void The_application_menu_declares_about_and_preferences_and_no_quit_of_its_own()
    {
        var menu = NativeMenu.GetMenu(Application.Current!);

        Assert.NotNull(menu);
        var headers = menu!.Items.OfType<NativeMenuItem>().Where(i => i is not NativeMenuItemSeparator).Select(i => i.Header!).ToArray();
        Assert.Equal(["About LizTerm", "Preferences..."], headers);
    }

    /// <summary>The one gesture outside Edit, deliberately (settings spec §5.4): Cmd-comma is where every macOS
    /// user looks for Preferences, the application menu exists only on macOS, and DefaultKeymap binds no Cmd
    /// chord, so nothing is taken from the host. About stays bare, and so does everything AppKit appends.</summary>
    [AvaloniaFact]
    public void The_application_menu_carries_cmd_comma_on_preferences_and_nothing_else()
    {
        var menu = NativeMenu.GetMenu(Application.Current!)!;
        var about = MenuLookup.Item(menu, "About LizTerm")!;
        var preferences = MenuLookup.Item(menu, "Preferences...")!;

        Assert.Null(about.Gesture);
        Assert.Equal(new KeyGesture(Key.OemComma, KeyModifiers.Meta), preferences.Gesture);
        Assert.True(preferences.HasClickHandlers);
        Assert.True(about.HasClickHandlers);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~PreferencesWindowTests|FullyQualifiedName~The_application_menu"`
Expected: build errors naming `PreferencesWindow` and `ShowPreferences`.

- [ ] **Step 3: Write the window**

`src/LizTerm.App/Views/PreferencesWindow.axaml`:

```xml
<!--
  This file is part of LizTerm.
  Copyright 2026 by CoffeeMuse
  SPDX-License-Identifier: BSD-3-Clause
-->
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:vm="using:LizTerm.App.ViewModels"
        x:Class="LizTerm.App.Views.PreferencesWindow"
        x:DataType="vm:SettingsViewModel"
        Icon="avares://LizTerm.App/Assets/Icons/lizterm-256.png"
        Title="Preferences" Width="440" SizeToContent="Height" CanResize="False"
        WindowStartupLocation="CenterScreen">
  <!-- Every control applies the moment it changes, to every open session window: the data context is the
       process's one SettingsViewModel. No OK or Cancel, only Done. Adding a group later is adding controls. -->
  <StackPanel Margin="16" Spacing="6">
    <TextBlock Text="Crosshair" FontWeight="SemiBold" />
    <!-- One-way check marks plus Click handlers, the same shape as View > Crosshair: four two-way bools over
         one enum cannot drive each other (CrosshairModeConverter says why). -->
    <RadioButton x:Name="CrosshairNone" GroupName="Crosshair" Content="None" Click="OnCrosshairNoneClick"
                 IsChecked="{Binding Crosshair, Converter={x:Static vm:CrosshairModeConverter.Instance}, ConverterParameter=None, Mode=OneWay}" />
    <RadioButton x:Name="CrosshairHorizontal" GroupName="Crosshair" Content="Horizontal" Click="OnCrosshairHorizontalClick"
                 IsChecked="{Binding Crosshair, Converter={x:Static vm:CrosshairModeConverter.Instance}, ConverterParameter=Horizontal, Mode=OneWay}" />
    <RadioButton x:Name="CrosshairVertical" GroupName="Crosshair" Content="Vertical" Click="OnCrosshairVerticalClick"
                 IsChecked="{Binding Crosshair, Converter={x:Static vm:CrosshairModeConverter.Instance}, ConverterParameter=Vertical, Mode=OneWay}" />
    <RadioButton x:Name="CrosshairBoth" GroupName="Crosshair" Content="Both" Click="OnCrosshairBothClick"
                 IsChecked="{Binding Crosshair, Converter={x:Static vm:CrosshairModeConverter.Instance}, ConverterParameter=Both, Mode=OneWay}" />

    <TextBlock Text="Blink" FontWeight="SemiBold" Margin="0,10,0,0" />
    <CheckBox x:Name="BlinkBox" Content="Blink text the host marks as blinking" IsChecked="{Binding Blink, Mode=TwoWay}" />

    <!-- Empty until a save fails; the session windows show the same message in their banners. -->
    <TextBlock x:Name="SaveErrorText" Text="{Binding LastSaveError}" Foreground="#FF8080" TextWrapping="Wrap" Margin="0,10,0,0" />

    <Button x:Name="DoneButton" Content="Done" HorizontalAlignment="Right" Margin="0,10,0,0" IsDefault="True" IsCancel="True" Click="OnDoneClick" />
  </StackPanel>
</Window>
```

`src/LizTerm.App/Views/PreferencesWindow.axaml.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Controls;
using Avalonia.Interactivity;
using LizTerm.App.ViewModels;
using LizTerm.Core.Settings;

namespace LizTerm.App.Views;

/// <summary>The app-wide preferences, bound to the process's one SettingsViewModel. Modeless and one at a time;
/// App.ShowPreferences is the route (settings spec §5).</summary>
public partial class PreferencesWindow : Window
{
    /// <summary>Design-time only.</summary>
    public PreferencesWindow() : this(new SettingsViewModel()) { }

    public PreferencesWindow(SettingsViewModel settings)
    {
        InitializeComponent();
        DataContext = settings;
    }

    private SettingsViewModel Settings => (SettingsViewModel)DataContext!;

    private void OnCrosshairNoneClick(object? sender, RoutedEventArgs e) => Settings.Crosshair = CrosshairMode.None;
    private void OnCrosshairHorizontalClick(object? sender, RoutedEventArgs e) => Settings.Crosshair = CrosshairMode.Horizontal;
    private void OnCrosshairVerticalClick(object? sender, RoutedEventArgs e) => Settings.Crosshair = CrosshairMode.Vertical;
    private void OnCrosshairBothClick(object? sender, RoutedEventArgs e) => Settings.Crosshair = CrosshairMode.Both;

    private void OnDoneClick(object? sender, RoutedEventArgs e) => Close();
}
```

- [ ] **Step 4: Add `ShowPreferences` to the app**

In `src/LizTerm.App/App.axaml.cs`, after the `ShowAboutAsync` method and before `AboutEngine`:

```csharp
    private void OnPreferencesClick(object? sender, EventArgs e) => ShowPreferences();

    private PreferencesWindow? _preferences;

    /// <summary>The one route to Preferences, for the macOS application menu and a session's Edit item alike.
    /// Modeless and unowned so the user keeps working while it is open, and one at a time: a second request
    /// activates the first. Works with only the picker open, since the settings live on the app.</summary>
    public void ShowPreferences() => ShowPreferences(Settings);

    /// <summary>The rule with the settings object as an argument, so a test can exercise it without the real
    /// settings file.</summary>
    internal PreferencesWindow ShowPreferences(SettingsViewModel settings)
    {
        if (_preferences is { } showing)
        {
            showing.Activate();
            return showing;
        }
        var window = new PreferencesWindow(settings);
        _preferences = window;
        window.Closed += (_, _) => { if (ReferenceEquals(_preferences, window)) _preferences = null; };
        window.Show();
        return window;
    }
```

- [ ] **Step 5: Add the application menu item**

In `src/LizTerm.App/App.axaml`, the `NativeMenu` becomes:

```xml
  <NativeMenu.Menu>
    <NativeMenu>
      <NativeMenuItem Header="About LizTerm" Click="OnAboutClick" />
      <NativeMenuItemSeparator />
      <!-- The one gesture outside Edit, on purpose, and the one item AppKit does not supply: see "Gestures" in
           CLAUDE.md. Cmd-comma cannot be a 3270 keystroke (DefaultKeymap binds no Cmd chord). -->
      <NativeMenuItem Header="Preferences..." Gesture="Cmd+OemComma" Click="OnPreferencesClick" />
    </NativeMenu>
  </NativeMenu.Menu>
```

In the comment above it, change `About is the whole of what we declare, deliberately.` to `About and Preferences are the whole of what we declare, deliberately.`

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/LizTerm.App.Tests`
Expected: all pass. If `The_application_menu_carries_cmd_comma_on_preferences_and_nothing_else` fails on the gesture, `Cmd+OemComma` did not parse as expected; `KeyGesture.Parse` accepts `Cmd+,` as an alternative spelling — use whichever yields `Key.OemComma` with `KeyModifiers.Meta`.

- [ ] **Step 7: Zero-warning check, then commit**

Run: `dotnet build LizTerm.slnx --no-incremental 2>&1 | grep -c " warning "` — Expected: `0`.

```bash
git add src/LizTerm.App tests/LizTerm.App.Tests
git commit -m "Add the Preferences window, reached from the macOS application menu

Modeless, one at a time, bound to the process's settings object; the
application menu gains Preferences... with Cmd-comma, the one deliberate
gesture outside Edit.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 8: Edit > Preferences on Windows and Linux

**Files:**
- Modify: `src/LizTerm.App/Menus/MenuStrategy.cs`, `src/LizTerm.App/Views/SessionWindow.axaml`, `src/LizTerm.App/Views/SessionWindow.axaml.cs`
- Test: `tests/LizTerm.App.Tests/Menus/MenuStrategyTests.cs`, `tests/LizTerm.App.Tests/Views/NativeMenuTests.cs`

**Interfaces:**
- Consumes: `App.ShowPreferences()` (Task 7).
- Produces: `MenuStrategy.PreferencesInEditMenu(bool isMacOS)`; native item `_Edit > P_references...`; classic `PreferencesMenuItem` and `PreferencesSeparator`.

- [ ] **Step 1: Write the failing tests**

Add to `tests/LizTerm.App.Tests/Menus/MenuStrategyTests.cs`:

```csharp
    /// <summary>macOS has Preferences in the application menu, with Cmd-comma; Edit carries one everywhere else.</summary>
    [Fact]
    public void Preferences_belongs_in_the_edit_menu_everywhere_except_macOS()
    {
        Assert.False(MenuStrategy.PreferencesInEditMenu(isMacOS: true));
        Assert.True(MenuStrategy.PreferencesInEditMenu(isMacOS: false));
    }
```

Add to `tests/LizTerm.App.Tests/Views/NativeMenuTests.cs`, after `The_separator_above_about_is_hidden_with_it`:

```csharp
    /// <summary>The same shape as About in Help: on a CI machine this covers the visible-in-Edit branch only,
    /// and the macOS branch is covered by running the app on the Mac.</summary>
    [AvaloniaFact]
    public void Preferences_is_in_the_edit_menu_on_this_platform_exactly_when_the_strategy_says_so()
    {
        var (window, _, _, _) = Show();
        var expected = MenuStrategy.PreferencesInEditMenu(OperatingSystem.IsMacOS());

        Assert.Equal(expected, Item(window, "_Edit", "P_references...").IsVisible);
        Assert.Equal(expected, window.FindControl<MenuItem>("PreferencesMenuItem")!.IsVisible);
    }

    [AvaloniaFact]
    public void The_separator_above_preferences_is_hidden_with_it()
    {
        var (window, _, _, _) = Show();
        var expected = MenuStrategy.PreferencesInEditMenu(OperatingSystem.IsMacOS());

        var separator = MenuLookup.SeparatorAbove(Item(window, "_Edit", "P_references..."));
        Assert.NotNull(separator);
        Assert.Equal(expected, separator!.IsVisible);
        Assert.Equal(expected, window.FindControl<Separator>("PreferencesSeparator")!.IsVisible);
    }

    [AvaloniaFact]
    public void Preferences_is_the_last_edit_item_in_both_menus()
    {
        var (window, _, _, _) = Show();

        Assert.Equal("P_references...", Item(window, "_Edit", "P_references...").Parent!.Items.OfType<NativeMenuItem>().Last().Header);
        var classicEdit = window.FindControl<Menu>("ClassicMenu")!.Items.OfType<MenuItem>().Single(m => (string)m.Header! == "_Edit");
        Assert.Equal("P_references...", (string)classicEdit.Items.OfType<MenuItem>().Last().Header!);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~MenuStrategyTests|FullyQualifiedName~Preferences_is|FullyQualifiedName~The_separator_above_preferences"`
Expected: a build error for `PreferencesInEditMenu`, then the menu tests failing on a missing item.

- [ ] **Step 3: The rule**

In `src/LizTerm.App/Menus/MenuStrategy.cs`, after `AboutInHelpMenu`:

```csharp
    /// <summary>macOS puts Preferences in the application menu, with Cmd-comma, so the Edit menu carries one
    /// only elsewhere — the same shape as About in Help.</summary>
    public static bool PreferencesInEditMenu(bool isMacOS) => !isMacOS;
```

- [ ] **Step 4: The items, in both menus**

In `src/LizTerm.App/Views/SessionWindow.axaml`, the native Edit menu gains two entries after `_Find...`:

```xml
            <NativeMenuItem Header="_Find..." Click="OnFindClickNative" IsEnabled="{Binding CanFind}" />
            <!-- Hidden with its separator on macOS, where Preferences lives in the application menu. -->
            <NativeMenuItemSeparator />
            <NativeMenuItem Header="P_references..." Click="OnPreferencesClickNative" />
```

and the classic Edit menu gains the matching pair after `FindMenuItem`:

```xml
        <MenuItem x:Name="FindMenuItem" Header="_Find..." Click="OnFindClick" IsEnabled="{Binding CanFind}" />
        <!-- Hidden with the item below it on macOS, as AboutSeparator is. -->
        <Separator x:Name="PreferencesSeparator" />
        <MenuItem x:Name="PreferencesMenuItem" Header="P_references..." Click="OnPreferencesClick" />
```

The mnemonic is the `r`: Edit already uses C, P, A, H and F.

In `src/LizTerm.App/Views/SessionWindow.axaml.cs`, at the end of `ApplyMenuStrategy`, after the About block:

```csharp
        // macOS puts Preferences in the application menu, with Cmd-comma, so Edit carries one only elsewhere.
        // The same shape as About above: both renderers, the separator included.
        var preferencesInEdit = MenuStrategy.PreferencesInEditMenu(OperatingSystem.IsMacOS());
        PreferencesMenuItem.IsVisible = preferencesInEdit;
        PreferencesSeparator.IsVisible = preferencesInEdit;
        if (MenuLookup.Required(ExportedMenu, "_Edit", "P_references...") is { } nativePreferences)
        {
            nativePreferences.IsVisible = preferencesInEdit;
            if (MenuLookup.SeparatorAbove(nativePreferences) is { } separator) separator.IsVisible = preferencesInEdit;
        }
```

And beside `OnAboutClickNative`:

```csharp
    private void OnPreferencesClick(object? sender, RoutedEventArgs e) => ShowPreferences();
    private void OnPreferencesClickNative(object? sender, EventArgs e) => ShowPreferences();

    private static void ShowPreferences() => (Avalonia.Application.Current as App)?.ShowPreferences();
```

Update the comment above `OnCloseClickNative` that counts the handlers ("All seven live here") only if it would now be wrong; it counts handlers of an earlier task and can be left as history.

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/LizTerm.App.Tests`
Expected: all pass — including the parity guard (both menus gained a separator and an item at the same position), `Every_native_item_can_actually_be_activated` (the native item has a Click handler) and `Only_the_edit_menu_carries_gestures` (the new item has none).

- [ ] **Step 6: Zero-warning check, then commit**

Run: `dotnet build LizTerm.slnx --no-incremental 2>&1 | grep -c " warning "` — Expected: `0`.

```bash
git add src/LizTerm.App tests/LizTerm.App.Tests
git commit -m "Put Preferences... at the bottom of Edit on Windows and Linux

Hidden with its separator on macOS, where the application menu carries it,
the shape About in Help already has.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 9: Documentation, the spec's As-built section, and the final checks

**Files:**
- Modify: `docs/user-guide.md`, `docs/architecture.md`, `src/LizTerm.Core/CLAUDE.md`, `src/LizTerm.App/CLAUDE.md`, `tests/CLAUDE.md`, `docs/superpowers/specs/2026-09-11-lizterm-settings-store-design.md`

- [ ] **Step 1: The user guide**

In `docs/user-guide.md`:

Replace the two lines

```
**View > Crosshair** draws a horizontal line, a vertical line, or both through the cursor, for lining up columns.
It is set per window.
```

with

```
**View > Crosshair** draws a horizontal line, a vertical line, or both through the cursor, for lining up columns.
The choice is remembered, and applies to every session window; it is also in Preferences.
```

In the **Menus** section, change `and About is in the LizTerm application menu` to `and About and Preferences are in the LizTerm application menu`, and `The macOS LizTerm application menu (About, and Apple's standard
items) stays either way.` to `The macOS LizTerm application menu (About, Preferences, and Apple's standard items) stays either way.` (rewrap to the file's width).

Add a new section between **Menus** and **Where LizTerm keeps its files**:

```
## Preferences

**Preferences...** is in the LizTerm application menu on macOS (Cmd-comma), and at the bottom of a session
window's **Edit** menu on Windows and Linux. Changes apply as you make them, to every open session window:

- **Crosshair** — the same choice as View > Crosshair.
- **Blink** — whether text the host marks as blinking actually blinks. Off draws it steady.
```

In **Where LizTerm keeps its files**, replace the paragraph after the table with:

```
Profiles are in `profiles/`, one JSON file each, and wire logs in `logs/`, named
`wire-<profile>-<date>-<time>.log`. `settings.json` holds your preferences — only the ones you have changed, so
deleting it puts everything back to the defaults.
```

- [ ] **Step 2: The architecture overview**

In `docs/architecture.md`, the Core row's role column gains "and settings" after "profiles", so it reads: The domain model: screen snapshots, connection and keyboard state, profiles and settings, the IEmulatorSession interface, certificate utilities.

- [ ] **Step 3: Core's notes**

In `src/LizTerm.Core/CLAUDE.md`, change the `AppPaths` bullet under **Model** to:

```
- `AppPaths` owns the per-OS config root, with `profiles`, `logs` and `settings.json` beneath it.
```

and add a section before **Security**:

```
## Settings (`LizTerm.Core.Settings`)

- `AppSettings` is the app-wide record: positional, a default on every parameter, and **flat by policy**, because
  `SettingsLayers.Merge` overlays top-level keys and would replace a nested object whole. `CrosshairMode` lives
  here because the record names it. The enum is written by name.
- The user file is a **sparse overlay**. `SettingsStore.Update(change)` applies the change to what is on disk
  *now* (re-read first, as `ProfileStore.Update` does) and writes exactly the keys the file already held plus the
  keys that now differ from the layers beneath (`SettingsLayers.UserDocument`). A key never touched stays absent
  and follows the default; one set once stays pinned, even set back to the default. Known keys are rewritten
  from the record, so a bad value heals on the first save; unknown keys are copied verbatim, so a newer build's
  key survives an older build saving.
- `SettingsLayers.Read` drops a key whose value will not deserialise and keeps the rest, so a hand-edited typo
  costs that key alone. `Load` never throws. `Update` throws `InvalidDataException` naming the file when the
  file is not a JSON object, rather than overwrite something the user may be editing.
- `SettingsLayers` is pure; every merge and pinning case is a plain unit test. Only the user file is wired: a
  system layer is one more entry in `Merge`'s list, and an environment variable would be the topmost layer.
```

- [ ] **Step 4: The App's notes**

In `src/LizTerm.App/CLAUDE.md`:

Under **Session view model**, the first bullet ends with the list of injected seams. Append this sentence to it:

```
  The process's `SettingsViewModel` is injected the same way, last, and defaults to an in-memory one, so no
  view-model test touches the settings file; its `SaveFailed` lands in `ErrorMessage`.
```

Add a subsection after **Wire log, About and the engine**:

```
### Settings and Preferences

- `SettingsViewModel` is the app-wide settings as one live object, created once by `App` (`App.Settings`, lazy
  with `??=` like `_store`) and shared by every session window and the Preferences window; property change
  notification on it is the whole live-propagation mechanism. A setter applies the change in memory, raises
  the change, then writes through `SettingsStore.Update` with the same change function — the in-memory value
  always wins. A failed save sets `LastSaveError` (the window shows it) and raises `SaveFailed` (every session
  banner shows it; the event fires on every failure, where an unchanged property text would not re-notify a
  dismissed banner).
- `TerminalScreen.BlinkEnabled` (bound to `Settings.Blink`) stops the blink timer and clears the hidden phase;
  the snapshot still says what the host asked for.
- `App.ShowPreferences` is the one route to `PreferencesWindow`: modeless, unowned, one at a time in
  `_preferences` the way About is in `_about`. The internal overload taking a `SettingsViewModel` is the test seam.
  The window's crosshair radios are one-way check marks plus Click handlers, exactly the View menu's shape.
```

Under **Menus**, change `holding **About and nothing else**` to `holding **About and Preferences**`; in **Strategy**, `MenuStrategy.Decide` and `AboutInHelpMenu` are pure` becomes `MenuStrategy.Decide`, `AboutInHelpMenu` and `PreferencesInEditMenu` are pure`.

Replace the first paragraph of **Gestures** with:

```
**No window menu item outside Edit ever carries a `Gesture`.** On macOS a `NativeMenuItem` gesture becomes an
AppKit key equivalent that `NSApplication.sendEvent:` dispatches before the key window's responder chain, so
`Gesture="F1"` would silently swallow PF1 — `TerminalScreen` would never see the key. So View, File > Save Screen
As... and Edit > Copy Screen as HTML carry none. **The one exception is Preferences... on the application menu**,
with Cmd-comma (settings spec §5.4): the application menu exists only on macOS, `DefaultKeymap` binds no Cmd
chord, and Edit's own Cmd+C, V, A and F are already key equivalents of exactly this class.
`NativeMenuTests.The_application_menu_carries_cmd_comma_on_preferences_and_nothing_else` holds it to that one.
```

In **The application menu**, `It declares no Quit, on purpose.` becomes `It declares About and Preferences, and no Quit, on purpose.`

- [ ] **Step 5: The tests' notes**

In `tests/CLAUDE.md`, under **App tests**, extend the bullet ending `View-model tests use plain `[Fact]` with `FakeEmulatorSession`.` with: `A view model built without a `SettingsViewModel` gets an in-memory one; a test that needs a failing save points a `SettingsStore` at a temp file holding `not json`.` Add a bullet: `Drive the Preferences radios by raising `Button.ClickEvent`; assigning `IsChecked` only proves the one-way binding renders.` Under **Core tests**, add: `SettingsStoreTests` uses a temp directory per test like `ProfileStoreTests`; `SettingsLayersTests` needs no disk.`

- [ ] **Step 6: The spec's As-built section**

Append to `docs/superpowers/specs/2026-09-11-lizterm-settings-store-design.md`:

```
## 9. As built (2026-09-11)

Three refinements, decided while planning; the plan is `docs/superpowers/plans/2026-09-11-lizterm-settings-store.md`.

- **`SettingsStore.Update(Func<AppSettings, AppSettings>)` replaces `Save(AppSettings)`** (§3.4). A whole-record
  save re-reading the file could preserve only *which* keys were pinned; this process's stale value would still
  overwrite a key another process had just written. `Update` applies the change to what is on disk now, as
  `ProfileStore.Update` does, so only the key the user touched changes. `SettingsViewModel` keeps its own
  in-memory record as the view.
- **`SettingsLayers.Read` drops a bad key alone** (§3.3). Defaults for the whole run would also make the next save
  rewrite every other key from defaults. `Read` probes each top-level key on its own; the offending key falls to
  its default and the next save repairs it.
- **The App class is `SettingsViewModel` in `ViewModels/`**, not `Settings` in a `Settings/` folder (§4.1): a class
  named for a namespace it sits in cannot be named from `namespace LizTerm.App` unqualified, and it is an
  `ObservableObject` a window binds to.
- The one-window rule is tested after all (§6.2): the headless application is the real `App`, and an internal
  `ShowPreferences(SettingsViewModel)` overload keeps the test off the real settings file.
```

- [ ] **Step 7: The whole suite and the zero-warning check**

Run: `dotnet build LizTerm.slnx --no-incremental 2>&1 | grep -c " warning "` — Expected: `0`.
Run: `LIZTERM_B3270_PATH=/opt/homebrew/bin/b3270 dotnet test LizTerm.slnx` — Expected: every test passes; the live-host tests skip.
Run: `grep -rn "It is set per window" docs/ src/` — Expected: nothing.

- [ ] **Step 8: Commit**

```bash
git add docs src/LizTerm.Core/CLAUDE.md src/LizTerm.App/CLAUDE.md tests/CLAUDE.md
git commit -m "Document the settings store and Preferences, and record what was built

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

- [ ] **Step 9: Hand over for the manual pass**

Report to Robert, who runs the app on the Mac (`dotnet run --project src/LizTerm.App`, with `LIZTERM_B3270_PATH=/opt/homebrew/bin/b3270`) and checks, per spec §6.3: Cmd-comma opens Preferences from the picker and from a session; a second Cmd-comma activates the same window; a crosshair change in Preferences shows in two open sessions at once; with blink off, a screen carrying blink draws steady (the ibmlink-help fixture host or any host with a blinking field); and `~/Library/Application Support/LizTerm/settings.json` after one change holds exactly one key. Pushing and opening the PR wait for his go-ahead; the PR description, and a comment on #19, note that #55's constraints are honoured (sparse per-key layering, no secrets, no machine-specific keys) and list what remains on #19 (spec §7).
