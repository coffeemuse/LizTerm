# LizTerm on-screen keypad — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A docked panel of 36 buttons (PF1 to PF24, PA1 to PA3, Enter, Clear, Reset, Attn, SysReq, Erase EOF, Erase Input, Dup, Field Mark) in every session window, off by default, shown by View > Keypad, docked at the bottom or on the right from Preferences, with each button's keyboard equivalent as a tooltip.

**Architecture:** A `Keypad` control in `Controls/` mirrors `TerminalScreen`'s contract: it builds its buttons from a pure `KeypadLayout` table, raises `KeyRequested`, and knows nothing about view models. `SessionWindow` docks it innermost in its `DockPanel`, binds its visibility, dock and enablement to the shared `SettingsViewModel` and `IsConnected`, and routes each key through `TerminalScreen.CancelTap()`, `SendKeyAsync` (the method, never the command) and `Screen.Focus()`. Tooltips come from a pure `KeymapHints` that reverses the live `Keymap` and formats chords through Avalonia's platform `KeyGestureFormatInfo`. `AppSettings` gains `Keypad` (bool) and `KeypadDock` (enum written by name), following the bell's bool-plus-enum shape.

**Tech Stack:** .NET 10, C# 14, Avalonia 12.1.2 (headless XUnit lane), CommunityToolkit.Mvvm, System.Text.Json source generation, xunit.v3.

**Spec:** `docs/superpowers/specs/2026-09-11-lizterm-keypad-design.md`

## Global Constraints

- **Worktree only.** All work happens in `/Users/robert/ClaudeSandbox/LizTerm/.claude/worktrees/backlog-issues-review-c14dca`, on branch `claude/backlog-issues-review-c14dca`. Never run git in, or write to, the main checkout at `/Users/robert/ClaudeSandbox/LizTerm`. Renaming the branch to an issue-31 name before it is pushed is Robert's call, not a task here.
- **Robert's gates.** Pushing, opening or changing a pull request, and bumping the version each wait for Robert's explicit go-ahead in chat. The version stays 0.4.1 (spec §11).
- **Dependency rule.** `LizTerm.Core` names nothing from Avalonia or b3270; `KeypadDock` is the only Core change and is a plain enum. `LizTerm.Backend.B3270` is untouched. The App names the backend only in `SessionFactory.cs`. Nothing in this plan touches `SessionProfile`, `IEmulatorSession` or the Keys menu.
- **The keypad's route is the screen's route.** Buttons call `SessionViewModel.SendKeyAsync` (the method), never `SendKeyCommand`, and every button is `Focusable = false` (spec §4.1).
- **No gesture on the new menu item.** Nothing outside Edit carries a `Gesture`; `NativeMenuTests.Only_the_edit_menu_carries_gestures` walks every menu.
- **Licence header.** Every new `.cs` and `.axaml` file starts with the three lines `This file is part of LizTerm.`, `Copyright 2026 by CoffeeMuse`, `SPDX-License-Identifier: BSD-3-Clause` in the file's comment syntax (`//` for C#, inside `<!-- -->` before the root element for XAML). `RepositoryHeadersTests` fails the suite otherwise.
- **Test commands.** Full suite: `LIZTERM_B3270_PATH=/opt/homebrew/bin/b3270 dotnet test LizTerm.slnx` (this worktree has no `native/out`; the live-host tests skip themselves). One class: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~KeypadTests"`. The App tests run on Avalonia's headless platform; control and window tests use `[AvaloniaFact]`, pure tests plain `[Fact]`.
- **Zero warnings.** Before any task is called done: `dotnet build LizTerm.slnx --no-incremental 2>&1 | grep -c " warning "` prints `0`. CI builds with `-warnaserror`.
- **Menus.** View gains one item, `_Keypad`, in *both* menus, in the same position, so `NativeMenuTests.The_native_menu_matches_the_classic_menu_item_for_item` keeps passing. `The_crosshair_modes_are_grouped_under_their_own_submenu` asserts View has one child today and is updated in Task 7.
- **Design history is a record.** Edit nothing under `docs/superpowers/` except the "As built" section Task 10 appends to this slice's spec.
- **Each fact has one home** (root `CLAUDE.md`). Task 10 updates the user guide and the two `CLAUDE.md` notes; earlier tasks change no documentation.
- **Exact strings.** Menu item header `_Keypad` in both menus. Preferences copy (spec §7): heading `Keypad`; checkbox `Show the keypad`; radios `At the bottom of the window` and `On the right of the window`. Button labels (spec §3): `PF1`..`PF24`, `PA1`, `PA2`, `PA3`, `Enter`, `Clear`, `Reset`, `Attn`, `SysReq`, `Erase EOF`, `Erase Input`, `Dup`, `Field Mark`. Tap wording (spec §5): `a tap of Left Ctrl`, `a tap of Right Ctrl`.
- **Commits** end with the line `Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>`.

---

## File Structure

**Created**

| File | Responsibility |
|---|---|
| `src/LizTerm.Core/Settings/KeypadDock.cs` | The enum: `Bottom`, `Right` (Task 1). |
| `src/LizTerm.App/ViewModels/KeypadDockConverter.cs` | "Is the dock this one?" for a radio's one-way `IsChecked` (Task 2). |
| `src/LizTerm.App/Controls/KeypadLayout.cs` | `KeypadKey` and the three banks of twelve, pure data (Task 3). |
| `src/LizTerm.App/Keyboard/KeymapHints.cs` | Pure: every chord that sends a key, as one line of text (Task 4). |
| `src/LizTerm.App/Controls/Keypad.axaml` + `.axaml.cs` | The control: buttons from the layout, `Dock`, `Keymap`, `KeyRequested`, `DockPanelDock` (Task 5). |
| `tests/LizTerm.App.Tests/ViewModels/KeypadDockConverterTests.cs` | Task 2. |
| `tests/LizTerm.App.Tests/Controls/KeypadLayoutTests.cs` | Task 3. |
| `tests/LizTerm.App.Tests/Keyboard/KeymapHintsTests.cs` | Task 4. |
| `tests/LizTerm.App.Tests/Controls/KeypadTests.cs` | Task 5. |

**Modified**

| File | Change |
|---|---|
| `src/LizTerm.Core/Settings/AppSettings.cs` | Two positional parameters, `Keypad` and `KeypadDock` (Task 1). |
| `src/LizTerm.App/ViewModels/SettingsViewModel.cs` | `Keypad` and `KeypadDock` properties (Task 2). |
| `src/LizTerm.App/Controls/TerminalScreen.cs` | `CancelTap()` (Task 6). |
| `src/LizTerm.App/Views/SessionWindow.axaml` | The docked `Keypad` (Task 6); `_Keypad` under View in both menus (Task 7). |
| `src/LizTerm.App/Views/SessionWindow.axaml.cs` | Wires `KeypadPanel.KeyRequested` (Task 6); `OnKeypadClick`, `OnKeypadClickNative`, `ToggleKeypad` (Task 7). |
| `src/LizTerm.App/Views/PreferencesWindow.axaml` + `.axaml.cs` | The Keypad group (Task 8). |
| `tests/LizTerm.Core.Tests/Settings/SettingsLayersTests.cs`, `SettingsStoreTests.cs` | New fields (Task 1). |
| `tests/LizTerm.App.Tests/ViewModels/SettingsViewModelTests.cs` | New properties (Task 2). |
| `tests/LizTerm.App.Tests/Views/SessionWindowTests.cs` | The docked keypad's six behaviours (Task 6). |
| `tests/LizTerm.App.Tests/Views/NativeMenuTests.cs` | View > Keypad; the View-children assertion; the Keys-menu guard (Task 7). |
| `tests/LizTerm.App.Tests/Views/PreferencesWindowTests.cs` | The Keypad group (Task 8). |
| `docs/user-guide.md`, `src/LizTerm.App/CLAUDE.md`, `tests/CLAUDE.md`, the spec | Task 10. |

---

### Task 1: `KeypadDock` and the two `AppSettings` fields

**Files:**
- Create: `src/LizTerm.Core/Settings/KeypadDock.cs`
- Modify: `src/LizTerm.Core/Settings/AppSettings.cs`
- Test: `tests/LizTerm.Core.Tests/Settings/SettingsLayersTests.cs`, `tests/LizTerm.Core.Tests/Settings/SettingsStoreTests.cs`

**Interfaces:**
- Produces: `enum KeypadDock { Bottom, Right }` in `LizTerm.Core.Settings`; `AppSettings(..., BellSound BellSound = BellSound.None, bool Keypad = false, KeypadDock KeypadDock = KeypadDock.Bottom)`. Every later task uses these two names.

- [ ] **Step 1: Write the failing tests**

Add to `tests/LizTerm.Core.Tests/Settings/SettingsLayersTests.cs`, after `The_bell_fields_read_by_name_and_default_on`:

```csharp
    /// <summary>A dock this build does not know (a later build's Left, say) costs that key alone (keypad spec §2.1),
    /// the same guarantee the bell's sound relies on.</summary>
    [Fact]
    public void A_keypad_dock_this_build_does_not_know_falls_to_Bottom_and_the_rest_are_kept()
    {
        Assert.Equal(new AppSettings(Keypad: true), SettingsLayers.Read(Doc("""{"keypad":true,"keypadDock":"Left"}""")));
    }

    [Fact]
    public void The_keypad_fields_read_by_name_and_default_to_hidden_at_the_bottom()
    {
        Assert.Equal(new AppSettings(Keypad: true, KeypadDock: KeypadDock.Right),
            SettingsLayers.Read(Doc("""{"keypad":true,"keypadDock":"Right"}""")));
        Assert.Equal(new AppSettings(), SettingsLayers.Read(Doc("""{}""")));
        Assert.False(new AppSettings().Keypad);
        Assert.Equal(KeypadDock.Bottom, new AppSettings().KeypadDock);
    }
```

Add to `tests/LizTerm.Core.Tests/Settings/SettingsStoreTests.cs`, after `The_bell_fields_round_trip_and_the_sound_is_written_by_name` (the class has a `Store` property and a `ReadFile()` helper):

```csharp
    [Fact]
    public void The_keypad_fields_round_trip_and_the_dock_is_written_by_name()
    {
        Store.Update(s => s with { Keypad = true, KeypadDock = KeypadDock.Right });

        Assert.Equal(new AppSettings(Keypad: true, KeypadDock: KeypadDock.Right), Store.Load());
        Assert.Equal("Right", ReadFile()["keypadDock"]!.GetValue<string>());
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~Settings"`
Expected: build error, `KeypadDock` and `Keypad` do not exist.

- [ ] **Step 3: Create the enum**

`src/LizTerm.Core/Settings/KeypadDock.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

namespace LizTerm.Core.Settings;

/// <summary>Which edge of the session window the on-screen keypad docks to (keypad spec §2.1). Written by name in
/// settings.json, so a reordering can never change a saved meaning; a name this build does not know reads as
/// Bottom for that key alone (SettingsLayers.Read drops the one key it cannot parse).</summary>
public enum KeypadDock { Bottom, Right }
```

- [ ] **Step 4: Add the two parameters**

In `src/LizTerm.Core/Settings/AppSettings.cs`, change the record to:

```csharp
public sealed record AppSettings(
    [property: JsonConverter(typeof(JsonStringEnumConverter<CrosshairMode>))] CrosshairMode Crosshair = CrosshairMode.None,
    bool Blink = true,
    bool VisualBell = true,
    [property: JsonConverter(typeof(JsonStringEnumConverter<BellSound>))] BellSound BellSound = BellSound.None,
    bool Keypad = false,
    [property: JsonConverter(typeof(JsonStringEnumConverter<KeypadDock>))] KeypadDock KeypadDock = KeypadDock.Bottom);
```

and in its doc comment change "The enum is written by name" to "The enums are written by name". `SettingsJsonContext` needs no change: the generator follows the record's properties.

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~Settings"`
Expected: PASS, the three new tests included.

- [ ] **Step 6: Commit**

```bash
git add src/LizTerm.Core/Settings/KeypadDock.cs src/LizTerm.Core/Settings/AppSettings.cs tests/LizTerm.Core.Tests/Settings/SettingsLayersTests.cs tests/LizTerm.Core.Tests/Settings/SettingsStoreTests.cs
git commit -m "Add Keypad and KeypadDock to the settings record

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 2: `SettingsViewModel.Keypad`, `.KeypadDock` and `KeypadDockConverter`

**Files:**
- Create: `src/LizTerm.App/ViewModels/KeypadDockConverter.cs`
- Modify: `src/LizTerm.App/ViewModels/SettingsViewModel.cs` (after the `BellSound` property)
- Test: `tests/LizTerm.App.Tests/ViewModels/SettingsViewModelTests.cs`, `tests/LizTerm.App.Tests/ViewModels/KeypadDockConverterTests.cs`

**Interfaces:**
- Consumes: `AppSettings.Keypad`, `.KeypadDock` (Task 1).
- Produces: `bool SettingsViewModel.Keypad { get; set; }`, `KeypadDock SettingsViewModel.KeypadDock { get; set; }`, each raising `PropertyChanged` with its own name; `KeypadDockConverter.Instance`.

- [ ] **Step 1: Write the failing tests**

Add to `tests/LizTerm.App.Tests/ViewModels/SettingsViewModelTests.cs`, after `A_store_backed_instance_writes_through_and_a_fresh_one_reads_it_back`:

```csharp
    [Fact]
    public void The_keypad_properties_write_through_and_skip_unchanged_values()
    {
        var settings = new SettingsViewModel(new SettingsStore(FilePath));
        var changes = Changes(settings);

        settings.Keypad = false;
        settings.KeypadDock = KeypadDock.Bottom;
        Assert.Empty(changes);
        Assert.False(File.Exists(FilePath));

        settings.Keypad = true;
        settings.KeypadDock = KeypadDock.Right;

        Assert.Equal(["Keypad", "KeypadDock"], changes);
        var reloaded = new SettingsViewModel(new SettingsStore(FilePath));
        Assert.True(reloaded.Keypad);
        Assert.Equal(KeypadDock.Right, reloaded.KeypadDock);
    }
```

Create `tests/LizTerm.App.Tests/ViewModels/KeypadDockConverterTests.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Globalization;
using LizTerm.App.ViewModels;
using LizTerm.Core.Settings;

namespace LizTerm.App.Tests.ViewModels;

/// <summary>KeypadDockConverter.Convert backs each Preferences dock radio's IsChecked, keyed by a hand-written
/// ConverterParameter; the same contract as the crosshair and bell converters, including the loud failure on a
/// typo.</summary>
public class KeypadDockConverterTests
{
    private static readonly KeypadDockConverter Converter = KeypadDockConverter.Instance;

    [Fact]
    public void A_matching_dock_and_parameter_convert_to_true()
    {
        Assert.Equal(true, Converter.Convert(KeypadDock.Right, typeof(bool), "Right", CultureInfo.InvariantCulture));
    }

    [Fact]
    public void A_non_matching_dock_converts_to_false()
    {
        Assert.Equal(false, Converter.Convert(KeypadDock.Right, typeof(bool), "Bottom", CultureInfo.InvariantCulture));
    }

    [Fact]
    public void A_parameter_that_is_not_a_dock_name_throws()
    {
        var ex = Assert.Throws<ArgumentException>(() => Converter.Convert(KeypadDock.Bottom, typeof(bool), "Rigth", CultureInfo.InvariantCulture));
        Assert.Contains("Right", ex.Message);
    }

    [Fact]
    public void A_missing_parameter_is_false_not_an_error()
    {
        Assert.Equal(false, Converter.Convert(KeypadDock.Bottom, typeof(bool), null, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void ConvertBack_is_not_supported()
    {
        Assert.Throws<NotSupportedException>(() => Converter.ConvertBack(true, typeof(KeypadDock), "Bottom", CultureInfo.InvariantCulture));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~SettingsViewModelTests|FullyQualifiedName~KeypadDockConverterTests"`
Expected: build error, `Keypad`, `KeypadDock` and `KeypadDockConverter` do not exist.

- [ ] **Step 3: Add the properties**

In `src/LizTerm.App/ViewModels/SettingsViewModel.cs`, after the `BellSound` property and before `Apply`:

```csharp
    public bool Keypad
    {
        get => Current.Keypad;
        set
        {
            if (Current.Keypad != value) Apply(nameof(Keypad), s => s with { Keypad = value });
        }
    }

    public KeypadDock KeypadDock
    {
        get => Current.KeypadDock;
        set
        {
            if (Current.KeypadDock != value) Apply(nameof(KeypadDock), s => s with { KeypadDock = value });
        }
    }
```

- [ ] **Step 4: Create the converter**

`src/LizTerm.App/ViewModels/KeypadDockConverter.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.Core.Settings;

namespace LizTerm.App.ViewModels;

/// <summary>"Is the dock this one?" for a Preferences radio's one-way IsChecked (keypad spec §7). EnumIsConverter
/// holds the rule, as it does for the crosshair and the bell.</summary>
public sealed class KeypadDockConverter : EnumIsConverter<KeypadDock>
{
    public static readonly KeypadDockConverter Instance = new();
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~SettingsViewModelTests|FullyQualifiedName~KeypadDockConverterTests"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/LizTerm.App/ViewModels/SettingsViewModel.cs src/LizTerm.App/ViewModels/KeypadDockConverter.cs tests/LizTerm.App.Tests/ViewModels/SettingsViewModelTests.cs tests/LizTerm.App.Tests/ViewModels/KeypadDockConverterTests.cs
git commit -m "Expose Keypad and KeypadDock on the settings view model

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 3: `KeypadLayout`, the three banks

**Files:**
- Create: `src/LizTerm.App/Controls/KeypadLayout.cs`
- Test: `tests/LizTerm.App.Tests/Controls/KeypadLayoutTests.cs`

**Interfaces:**
- Produces: `readonly record struct KeypadKey(string Label, TerminalKey Key)`; `static class KeypadLayout { const int BankSize = 12; IReadOnlyList<IReadOnlyList<KeypadKey>> Banks }`, three banks of twelve, in `LizTerm.App.Controls`. Tasks 5 and 7 read `Banks`.

- [ ] **Step 1: Write the failing tests**

`tests/LizTerm.App.Tests/Controls/KeypadLayoutTests.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.App.Controls;
using LizTerm.Core.Session;

namespace LizTerm.App.Tests.Controls;

/// <summary>The keypad's contents as data (keypad spec §3): three banks of twelve, every key once, the Keys menu's
/// own labels. The guard that the Keys menu is a subset lives in NativeMenuTests, beside the menu it reads.</summary>
public class KeypadLayoutTests
{
    private static IEnumerable<KeypadKey> All => KeypadLayout.Banks.SelectMany(bank => bank);

    [Fact]
    public void Three_banks_of_twelve()
    {
        Assert.Equal(12, KeypadLayout.BankSize);
        Assert.Equal(3, KeypadLayout.Banks.Count);
        Assert.All(KeypadLayout.Banks, bank => Assert.Equal(KeypadLayout.BankSize, bank.Count));
    }

    [Fact]
    public void The_first_two_banks_are_PF1_to_PF24_in_order()
    {
        Assert.Equal(Enumerable.Range(1, 12).Select(n => $"PF{n}"), KeypadLayout.Banks[0].Select(k => k.Label));
        Assert.Equal(Enumerable.Range(0, 12).Select(i => TerminalKey.PF1 + i), KeypadLayout.Banks[0].Select(k => k.Key));
        Assert.Equal(Enumerable.Range(13, 12).Select(n => $"PF{n}"), KeypadLayout.Banks[1].Select(k => k.Label));
        Assert.Equal(Enumerable.Range(0, 12).Select(i => TerminalKey.PF13 + i), KeypadLayout.Banks[1].Select(k => k.Key));
    }

    /// <summary>The issue's list plus Erase Input (bound by nothing else), Dup and Field Mark (on the Keys menu
    /// since #16), in the issue's order.</summary>
    [Fact]
    public void The_third_bank_is_the_specials()
    {
        Assert.Equal(
            ["PA1", "PA2", "PA3", "Enter", "Clear", "Reset", "Attn", "SysReq", "Erase EOF", "Erase Input", "Dup", "Field Mark"],
            KeypadLayout.Banks[2].Select(k => k.Label));
        Assert.Equal(
            [TerminalKey.PA1, TerminalKey.PA2, TerminalKey.PA3, TerminalKey.Enter, TerminalKey.Clear, TerminalKey.Reset,
             TerminalKey.Attn, TerminalKey.SysReq, TerminalKey.EraseEof, TerminalKey.EraseInput, TerminalKey.Dup, TerminalKey.FieldMark],
            KeypadLayout.Banks[2].Select(k => k.Key));
    }

    [Fact]
    public void No_key_appears_twice_and_no_label_is_empty()
    {
        Assert.Equal(All.Count(), All.Select(k => k.Key).Distinct().Count());
        Assert.All(All, k => Assert.False(string.IsNullOrWhiteSpace(k.Label)));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~KeypadLayoutTests"`
Expected: build error, `KeypadLayout` and `KeypadKey` do not exist.

- [ ] **Step 3: Create the layout**

`src/LizTerm.App/Controls/KeypadLayout.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.Core.Session;

namespace LizTerm.App.Controls;

/// <summary>One keypad button: the label the user reads and the key the host receives.</summary>
public readonly record struct KeypadKey(string Label, TerminalKey Key);

/// <summary>What the keypad holds, as data (keypad spec §3): three banks of BankSize, which the control lays out as
/// rows at the bottom of the window and as columns on its right. The Keys menu's labels are used where the two
/// overlap ("Field Mark", "SysReq") so the menu and the keypad never disagree; NativeMenuTests holds the menu to a
/// subset of this table.</summary>
public static class KeypadLayout
{
    public const int BankSize = 12;

    /// <summary>PF1 to PF12; PF13 to PF24; then the issue's list plus Erase Input, which nothing else binds, and
    /// Dup and Field Mark, which joined the Keys menu after the issue was written. Twelve in the third bank is what
    /// keeps the grid rectangular both ways.</summary>
    public static IReadOnlyList<IReadOnlyList<KeypadKey>> Banks { get; } =
    [
        [.. Enumerable.Range(0, BankSize).Select(i => new KeypadKey($"PF{i + 1}", TerminalKey.PF1 + i))],
        [.. Enumerable.Range(0, BankSize).Select(i => new KeypadKey($"PF{i + 13}", TerminalKey.PF13 + i))],
        [
            new("PA1", TerminalKey.PA1), new("PA2", TerminalKey.PA2), new("PA3", TerminalKey.PA3),
            new("Enter", TerminalKey.Enter), new("Clear", TerminalKey.Clear), new("Reset", TerminalKey.Reset),
            new("Attn", TerminalKey.Attn), new("SysReq", TerminalKey.SysReq), new("Erase EOF", TerminalKey.EraseEof),
            new("Erase Input", TerminalKey.EraseInput), new("Dup", TerminalKey.Dup), new("Field Mark", TerminalKey.FieldMark),
        ],
    ];
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~KeypadLayoutTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/LizTerm.App/Controls/KeypadLayout.cs tests/LizTerm.App.Tests/Controls/KeypadLayoutTests.cs
git commit -m "Add KeypadLayout: three banks of twelve keys as data

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 4: `KeymapHints`, the tooltip text

**Files:**
- Create: `src/LizTerm.App/Keyboard/KeymapHints.cs`
- Test: `tests/LizTerm.App.Tests/Keyboard/KeymapHintsTests.cs`

**Interfaces:**
- Consumes: `Keymap.Keys` (`IReadOnlyDictionary<KeyChord, TerminalKey>`), `KeyChord(Key, Modifiers, Tap)`, `DefaultKeymap.Create(bool)`, all existing in `LizTerm.App.Keyboard`.
- Produces: `static string? KeymapHints.Describe(Keymap keymap, TerminalKey key, IFormatProvider? format = null)`. Task 5 calls it with a null format.

- [ ] **Step 1: Write the failing tests**

`tests/LizTerm.App.Tests/Keyboard/KeymapHintsTests.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Input;
using Avalonia.Input.Platform;
using LizTerm.App.Keyboard;
using LizTerm.Core.Session;

namespace LizTerm.App.Tests.Keyboard;

/// <summary>The keypad's tooltips (keypad spec §5). Every expectation is formatted with an explicit
/// KeyGestureFormatInfo — Avalonia's common key names with the default modifier words — so it holds on every
/// machine; the headless platform registers a format of its own, and what it says is not ours to assert. The key
/// names ("Return", "PageUp") are Avalonia 12.1.2's, measured; a package bump that renames one shows up here.</summary>
public class KeymapHintsTests
{
    private static readonly Keymap Map = DefaultKeymap.Create(destructiveBackspace: true);
    private static readonly KeyGestureFormatInfo Words = new(new Dictionary<Key, string>());

    private static string? Hint(TerminalKey key, Keymap? map = null) => KeymapHints.Describe(map ?? Map, key, Words);

    [Theory]
    [InlineData(TerminalKey.PF1, "F1")]
    [InlineData(TerminalKey.PA1, "Alt+1")]
    [InlineData(TerminalKey.Attn, "Escape")]
    [InlineData(TerminalKey.EraseEof, "End")]
    public void A_key_with_one_chord_is_that_chord(TerminalKey key, string expected)
    {
        Assert.Equal(expected, Hint(key));
    }

    /// <summary>Unmodified first, then by modifier (Alt before Ctrl before Shift), function keys ahead of other
    /// keys within a group, taps last; two join with "or", three with a comma and "or".</summary>
    [Theory]
    [InlineData(TerminalKey.PF13, "Ctrl+F1 or Shift+F1")]
    [InlineData(TerminalKey.PA2, "Alt+2 or Ctrl+Home")]
    [InlineData(TerminalKey.PF7, "F7 or PageUp")]
    [InlineData(TerminalKey.Clear, "Pause or Ctrl+Escape")]
    [InlineData(TerminalKey.Reset, "Ctrl+R or a tap of Left Ctrl")]
    [InlineData(TerminalKey.Enter, "Return, Ctrl+Return or a tap of Right Ctrl")]
    public void Several_chords_are_ordered_and_joined(TerminalKey key, string expected)
    {
        Assert.Equal(expected, Hint(key));
    }

    [Theory]
    [InlineData(TerminalKey.EraseInput)]
    [InlineData(TerminalKey.Dup)]
    [InlineData(TerminalKey.FieldMark)]
    public void A_key_nothing_maps_has_no_hint(TerminalKey key)
    {
        Assert.Null(Hint(key));
    }

    /// <summary>Keymap holds a Dictionary, whose enumeration order is an implementation detail; the text must not
    /// depend on it.</summary>
    [Fact]
    public void The_same_table_in_reverse_order_gives_the_same_text()
    {
        var reversed = new Keymap(Map.Keys.Reverse(), Map.Text);

        Assert.Equal(Hint(TerminalKey.Enter), Hint(TerminalKey.Enter, reversed));
        Assert.Equal(Hint(TerminalKey.PF13), Hint(TerminalKey.PF13, reversed));
        Assert.Equal(Hint(TerminalKey.PA2), Hint(TerminalKey.PA2, reversed));
    }

    /// <summary>The hook for #18: a remap through Keymap.With changes the answer, so a tooltip can never describe a
    /// binding that is gone.</summary>
    [Fact]
    public void A_remapped_key_changes_the_hint()
    {
        var remapped = Map.With([KeyValuePair.Create(new KeyChord(Key.F9), TerminalKey.PA1)], []);

        Assert.Equal("F9 or Alt+1", Hint(TerminalKey.PA1, remapped));
        Assert.Null(Hint(TerminalKey.PF9, remapped));
    }

    /// <summary>A null format means the platform's registration — under a plain [Fact] there is none, and
    /// Avalonia falls back to its invariant names. Only that it answers is asserted here.</summary>
    [Fact]
    public void A_null_format_still_answers()
    {
        Assert.NotNull(KeymapHints.Describe(Map, TerminalKey.PA1));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~KeymapHintsTests"`
Expected: build error, `KeymapHints` does not exist.

- [ ] **Step 3: Create the formatter**

`src/LizTerm.App/Keyboard/KeymapHints.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Input;
using LizTerm.Core.Session;

namespace LizTerm.App.Keyboard;

/// <summary>The keyboard equivalents of a key as one line of text, for the keypad's tooltips (keypad spec §5) and,
/// one day, the Keys menu (#23). Pure: the keymap and the format are arguments, so a remap (#18) changes the
/// answer and a test can pin the words.</summary>
public static class KeymapHints
{
    /// <summary>Every chord in the keymap that sends the key, or null when none does. Ordered without reference to
    /// the table's insertion order (Keymap holds a Dictionary): unmodified chords first, then by KeyModifiers value
    /// (Alt, Control, Shift, combinations after), function keys ahead of other keys within a group, taps last.
    /// Ordinary chords are formatted by Avalonia's own platform formatter — glyphs on macOS, words elsewhere — and a
    /// null format means the platform's registration; taps, which it has no word for, are worded here.</summary>
    public static string? Describe(Keymap keymap, TerminalKey key, IFormatProvider? format = null)
    {
        var chords = keymap.Keys
            .Where(pair => pair.Value == key)
            .Select(pair => pair.Key)
            .OrderBy(chord => chord.Tap ? 1 : 0)
            .ThenBy(chord => (int)chord.Modifiers)
            .ThenBy(chord => IsFunctionKey(chord.Key) ? 0 : 1)
            .ThenBy(chord => (int)chord.Key)
            .Select(chord => Format(chord, format))
            .ToList();
        return chords.Count switch
        {
            0 => null,
            1 => chords[0],
            _ => string.Join(", ", chords.Take(chords.Count - 1)) + " or " + chords[^1],
        };
    }

    private static bool IsFunctionKey(Key key) => key is >= Key.F1 and <= Key.F24;

    private static string Format(KeyChord chord, IFormatProvider? format) => chord switch
    {
        { Tap: true, Key: Key.LeftCtrl } => "a tap of Left Ctrl",
        { Tap: true, Key: Key.RightCtrl } => "a tap of Right Ctrl",
        { Tap: true } => "a tap of " + new KeyGesture(chord.Key).ToString("p", format),
        _ => new KeyGesture(chord.Key, chord.Modifiers).ToString("p", format),
    };
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~KeymapHintsTests"`
Expected: PASS. If `Several_chords_are_ordered_and_joined` fails on a key *name* only (say "Return" is now "Enter"), Avalonia's common-override table has changed since it was measured against 12.1.2; check `Directory.Packages.props` has not moved the pin before touching anything, then update the expectation and say so in the commit.

- [ ] **Step 5: Commit**

```bash
git add src/LizTerm.App/Keyboard/KeymapHints.cs tests/LizTerm.App.Tests/Keyboard/KeymapHintsTests.cs
git commit -m "Add KeymapHints: a key's chords as tooltip text, formatted by the platform

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 5: The `Keypad` control

**Files:**
- Create: `src/LizTerm.App/Controls/Keypad.axaml`, `src/LizTerm.App/Controls/Keypad.axaml.cs`
- Test: `tests/LizTerm.App.Tests/Controls/KeypadTests.cs`

**Interfaces:**
- Consumes: `KeypadLayout.Banks`, `KeypadLayout.BankSize` (Task 3); `KeymapHints.Describe` (Task 4); `KeypadDock` (Task 1); `DefaultKeymap.Create`, `Keymap` (existing).
- Produces: `public partial class Keypad : UserControl` in `LizTerm.App.Controls` with `StyledProperty<KeypadDock> DockProperty` (CLR `Dock`, default `Bottom`), `StyledProperty<Keymap> KeymapProperty` (CLR `Keymap`), `event EventHandler<TerminalKey>? KeyRequested`, `public static readonly IValueConverter DockPanelDock`; a `UniformGrid` named `ButtonGrid` whose children are `Button`s with `Tag` set to the button's `TerminalKey` and class `keypad`. Task 6 binds and wires all of these.

- [ ] **Step 1: Write the failing tests**

`tests/LizTerm.App.Tests/Controls/KeypadTests.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using LizTerm.App.Controls;
using LizTerm.App.Keyboard;
using LizTerm.Core.Session;
using LizTerm.Core.Settings;

namespace LizTerm.App.Tests.Controls;

/// <summary>The control on its own (keypad spec §4): buttons from the layout table, one event, and nothing about
/// sessions. The window's half is in SessionWindowTests.</summary>
public class KeypadTests
{
    private static (Keypad Keypad, Window Window) Show(KeypadDock dock = KeypadDock.Bottom)
    {
        var keypad = new Keypad { Dock = dock };
        var window = new Window { Width = 960, Height = 680, Content = keypad };
        window.Show();
        return (keypad, window);
    }

    private static UniformGrid Grid(Keypad keypad) => keypad.FindControl<UniformGrid>("ButtonGrid")!;

    private static TerminalKey KeyOf(Control child) => (TerminalKey)((Button)child).Tag!;

    [AvaloniaFact]
    public void One_button_per_layout_entry_in_bank_order_at_the_bottom()
    {
        var (keypad, _) = Show();
        var grid = Grid(keypad);

        Assert.Equal(36, grid.Children.Count);
        Assert.Equal(KeypadLayout.BankSize, grid.Columns);
        Assert.Equal(KeypadLayout.Banks.SelectMany(b => b).Select(k => k.Key), grid.Children.Select(KeyOf));
        Assert.Equal("PF2", ((Button)grid.Children[1]).Content);
    }

    /// <summary>Index-major: the same 36 buttons, each bank a column, so PF1 to PF12 read down the first.</summary>
    [AvaloniaFact]
    public void On_the_right_each_bank_is_a_column()
    {
        var (keypad, _) = Show(KeypadDock.Right);
        var grid = Grid(keypad);

        Assert.Equal(3, grid.Columns);
        Assert.Equal(TerminalKey.PF1, KeyOf(grid.Children[0]));
        Assert.Equal(TerminalKey.PF13, KeyOf(grid.Children[1]));
        Assert.Equal(TerminalKey.PA1, KeyOf(grid.Children[2]));
        Assert.Equal(TerminalKey.PF2, KeyOf(grid.Children[3]));
        Assert.Equal(Avalonia.Layout.VerticalAlignment.Top, keypad.VerticalAlignment);
    }

    [AvaloniaFact]
    public void Changing_the_dock_relays_the_same_buttons()
    {
        var (keypad, _) = Show();
        var grid = Grid(keypad);
        var before = grid.Children.ToArray();

        keypad.Dock = KeypadDock.Right;
        Assert.Equal(3, grid.Columns);
        Assert.Equal(before.ToHashSet(), grid.Children.ToHashSet());

        keypad.Dock = KeypadDock.Bottom;
        Assert.Equal(KeypadLayout.BankSize, grid.Columns);
        Assert.Equal(before, grid.Children.ToArray());
        Assert.Equal(Avalonia.Layout.VerticalAlignment.Stretch, keypad.VerticalAlignment);
    }

    /// <summary>Two clicks, two keys: no command disables in between (spec §4.1). And no button can take the
    /// keyboard: Focusable is false on every one.</summary>
    [AvaloniaFact]
    public void A_click_raises_the_button_key_and_no_button_is_focusable()
    {
        var (keypad, _) = Show();
        var raised = new List<TerminalKey>();
        keypad.KeyRequested += (_, key) => raised.Add(key);
        var pf13 = Grid(keypad).Children.Cast<Button>().Single(b => KeyOf(b) == TerminalKey.PF13);

        pf13.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        pf13.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.Equal([TerminalKey.PF13, TerminalKey.PF13], raised);
        Assert.All(Grid(keypad).Children, child => Assert.False(((Button)child).Focusable));
    }

    /// <summary>The #18 hook: the tooltips are rebuilt from whatever Keymap the property holds.</summary>
    [AvaloniaFact]
    public void Tooltips_follow_the_keymap_property()
    {
        var (keypad, _) = Show();
        var buttons = Grid(keypad).Children.Cast<Button>().ToArray();
        var pa1 = buttons.Single(b => KeyOf(b) == TerminalKey.PA1);
        var dup = buttons.Single(b => KeyOf(b) == TerminalKey.Dup);
        Assert.NotNull(ToolTip.GetTip(pa1));
        Assert.Null(ToolTip.GetTip(dup));

        keypad.Keymap = DefaultKeymap.Create(destructiveBackspace: true)
            .With([KeyValuePair.Create(new KeyChord(Key.F9), TerminalKey.Dup)], []);

        Assert.NotNull(ToolTip.GetTip(dup));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~KeypadTests"`
Expected: build error, `Keypad` does not exist.

- [ ] **Step 3: Create the XAML**

`src/LizTerm.App/Controls/Keypad.axaml`:

```xml
<!--
  This file is part of LizTerm.
  Copyright 2026 by CoffeeMuse
  SPDX-License-Identifier: BSD-3-Clause
-->
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             x:Class="LizTerm.App.Controls.Keypad">
  <UserControl.Styles>
    <!-- Compact (keypad spec §4.1): three rows come to about 90 px at the bottom. The UI font, not the
         terminal's. Stretch so a bottom row fills its twelfth of the window; on the right the grid takes its
         natural width and the cells are as wide as the widest label. -->
    <Style Selector="Button.keypad">
      <Setter Property="MinHeight" Value="24" />
      <Setter Property="Padding" Value="4,0" />
      <Setter Property="Margin" Value="1" />
      <Setter Property="FontSize" Value="12" />
      <Setter Property="HorizontalAlignment" Value="Stretch" />
      <Setter Property="HorizontalContentAlignment" Value="Center" />
    </Style>
  </UserControl.Styles>
  <!-- The status bar's grey. The grid is filled in code from KeypadLayout: no button is written by hand. -->
  <Border Background="#181818" Padding="6,4">
    <UniformGrid x:Name="ButtonGrid" />
  </Border>
</UserControl>
```

- [ ] **Step 4: Create the code-behind**

`src/LizTerm.App/Controls/Keypad.axaml.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Interactivity;
using LizTerm.App.Keyboard;
using LizTerm.Core.Session;
using LizTerm.Core.Settings;

namespace LizTerm.App.Controls;

/// <summary>The on-screen keypad (keypad spec §4): 36 buttons built from KeypadLayout, raising KeyRequested the way
/// TerminalScreen does and knowing nothing about view models or sessions. The window routes the key (spec §6.2)
/// and binds IsVisible, IsEnabled and the two dock bindings (spec §6.1).</summary>
public partial class Keypad : UserControl
{
    /// <summary>Which way the grid is laid out: each bank a row (Bottom) or a column (Right).</summary>
    public static readonly StyledProperty<KeypadDock> DockProperty =
        AvaloniaProperty.Register<Keypad, KeypadDock>(nameof(Dock), KeypadDock.Bottom);

    /// <summary>The table the tooltips describe. Defaults to the built-in keymap; nothing binds it yet, since no
    /// keypad key depends on the one thing that varies the default (the backspace choice). The hook for #18.</summary>
    public static readonly StyledProperty<Keymap> KeymapProperty =
        AvaloniaProperty.Register<Keypad, Keymap>(nameof(Keymap), DefaultKeymap.Create(destructiveBackspace: true));

    /// <summary>For the window's DockPanel.Dock binding: where the panel sits is the window's decision, so the
    /// control converts rather than docking itself.</summary>
    public static readonly IValueConverter DockPanelDock =
        new FuncValueConverter<KeypadDock, Avalonia.Controls.Dock>(dock =>
            dock == KeypadDock.Right ? Avalonia.Controls.Dock.Right : Avalonia.Controls.Dock.Bottom);

    /// <summary>In bank order, whatever the dock; Arrange reorders the grid's children, never this list.</summary>
    private readonly List<Button> _buttons = [];

    public Keypad()
    {
        InitializeComponent();
        foreach (var bank in KeypadLayout.Banks)
        {
            foreach (var entry in bank)
            {
                // Focusable = false is the rule that keeps the keyboard on the screen through a click (spec §4.1);
                // the window's refocus after each key is the guarantee behind it. Tag carries the key so one
                // handler serves every button.
                var button = new Button { Content = entry.Label, Tag = entry.Key, Focusable = false };
                button.Classes.Add("keypad");
                button.Click += OnButtonClick;
                _buttons.Add(button);
            }
        }
        Arrange(Dock);
        Describe(Keymap);
    }

    public KeypadDock Dock
    {
        get => GetValue(DockProperty);
        set => SetValue(DockProperty, value);
    }

    public Keymap Keymap
    {
        get => GetValue(KeymapProperty);
        set => SetValue(KeymapProperty, value);
    }

    /// <summary>A button was clicked. The window sends it through SendKeyAsync, the method, never the command.</summary>
    public event EventHandler<TerminalKey>? KeyRequested;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == DockProperty) Arrange(change.GetNewValue<KeypadDock>());
        else if (change.Property == KeymapProperty) Describe(change.GetNewValue<Keymap>());
    }

    /// <summary>Bottom: BankSize columns, bank-major, stretched to the window's width. Right: one column per bank,
    /// index-major so PF1 to PF12 read down the first, top-aligned so the twelve rows keep their natural height
    /// beside the screen instead of stretching to fill it (spec §4.2, §4.3).</summary>
    private void Arrange(KeypadDock dock)
    {
        var banks = KeypadLayout.Banks;
        ButtonGrid.Children.Clear();
        if (dock == KeypadDock.Right)
        {
            ButtonGrid.Columns = banks.Count;
            for (var i = 0; i < KeypadLayout.BankSize; i++)
            {
                for (var b = 0; b < banks.Count; b++) ButtonGrid.Children.Add(_buttons[b * KeypadLayout.BankSize + i]);
            }
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
        }
        else
        {
            ButtonGrid.Columns = KeypadLayout.BankSize;
            foreach (var button in _buttons) ButtonGrid.Children.Add(button);
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch;
        }
    }

    /// <summary>A null format is the platform's registration: glyphs on macOS, words elsewhere (spec §5).</summary>
    private void Describe(Keymap keymap)
    {
        foreach (var button in _buttons) ToolTip.SetTip(button, KeymapHints.Describe(keymap, (TerminalKey)button.Tag!));
    }

    private void OnButtonClick(object? sender, RoutedEventArgs e) =>
        KeyRequested?.Invoke(this, (TerminalKey)((Button)sender!).Tag!);
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~KeypadTests"`
Expected: PASS. If `On_the_right_each_bank_is_a_column` reports 36 children but `Columns` of 12, `Arrange` ran before the constructor's `Dock` initialiser was applied: Avalonia sets object-initialiser properties *after* the constructor, and `OnPropertyChanged` handles that case, so check the `DockProperty` branch fires.

- [ ] **Step 6: Commit**

```bash
git add src/LizTerm.App/Controls/Keypad.axaml src/LizTerm.App/Controls/Keypad.axaml.cs tests/LizTerm.App.Tests/Controls/KeypadTests.cs
git commit -m "Add the Keypad control: 36 non-focusable buttons from the layout, docked two ways

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 6: `TerminalScreen.CancelTap()`, and the keypad docked in the session window

**Files:**
- Modify: `src/LizTerm.App/Controls/TerminalScreen.cs` (after `OnWindowDeactivated`), `src/LizTerm.App/Views/SessionWindow.axaml` (after the find bar `Border`, before `controls:TerminalScreen`), `src/LizTerm.App/Views/SessionWindow.axaml.cs` (the constructor, after `Screen.FindRequested`)
- Test: `tests/LizTerm.App.Tests/Views/SessionWindowTests.cs`

**Interfaces:**
- Consumes: `Keypad` with `Dock`, `KeyRequested`, `DockPanelDock`, `ButtonGrid` (Task 5); `SettingsViewModel.Keypad`, `.KeypadDock` (Task 2); `SessionViewModel.SendKeyAsync`, `.IsConnected`, `.Settings` (existing).
- Produces: `public void TerminalScreen.CancelTap()`; a `Keypad` named `KeypadPanel` in every `SessionWindow`, hidden until `Settings.Keypad`, docked per `Settings.KeypadDock`, enabled per `IsConnected`.

- [ ] **Step 1: Write the failing tests**

Add `using Avalonia.Controls.Primitives;` and `using LizTerm.Core.Settings;` to the usings of `tests/LizTerm.App.Tests/Views/SessionWindowTests.cs`. Then add, after `Dismiss_returns_focus_to_the_screen`:

```csharp
    private static Keypad KeypadOf(SessionWindow window) => window.FindControl<Keypad>("KeypadPanel")!;

    private static Button KeypadButton(SessionWindow window, TerminalKey key) =>
        KeypadOf(window).FindControl<UniformGrid>("ButtonGrid")!.Children.Cast<Button>().Single(b => (TerminalKey)b.Tag! == key);

    /// <summary>The whole route, by a real pointer press: the button's key reaches the host through SendKeyAsync,
    /// and the screen still has the keyboard afterwards — the buttons take no focus and the handler refocuses
    /// regardless (keypad spec §4.1, §6.2).</summary>
    [AvaloniaFact]
    public void A_keypad_click_reaches_the_host_and_leaves_the_screen_focused()
    {
        var (window, screen, vm, session, _) = Show();
        session.RaiseConnection(ConnectionState.Connected3270);
        vm.Settings.Keypad = true;
        window.UpdateLayout();
        var button = KeypadButton(window, TerminalKey.PF3);
        var centre = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;

        window.MouseDown(centre, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(centre, MouseButton.Left, RawInputModifiers.None);

        Assert.Equal(["key:PF3"], session.Calls);
        Assert.True(screen.IsFocused);
    }

    /// <summary>Right Ctrl held across a keypad click: the key goes, and the release is not a tap. Without
    /// CancelTap the detector would see Ctrl down, nothing, Ctrl up, and send Enter (keypad spec §6.2).</summary>
    [AvaloniaFact]
    public void Right_ctrl_held_across_a_keypad_click_sends_the_key_and_no_enter()
    {
        var (window, _, vm, session, _) = Show();
        session.RaiseConnection(ConnectionState.Connected3270);
        vm.Settings.Keypad = true;

        window.KeyPressQwerty(PhysicalKey.ControlRight, RawInputModifiers.Control);
        KeypadButton(window, TerminalKey.PF3).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        window.KeyReleaseQwerty(PhysicalKey.ControlRight, RawInputModifiers.None);

        Assert.Equal(["key:PF3"], session.Calls);
    }

    [AvaloniaFact]
    public void The_keypad_is_hidden_by_default_and_follows_the_setting()
    {
        var (window, _, vm, _, _) = Show();
        var keypad = KeypadOf(window);
        Assert.False(keypad.IsVisible);

        vm.Settings.Keypad = true;
        Assert.True(keypad.IsVisible);

        vm.Settings.Keypad = false;
        Assert.False(keypad.IsVisible);
    }

    /// <summary>Two bindings to one setting: the panel's edge of the window and the control's own grid shape.</summary>
    [AvaloniaFact]
    public void The_keypad_docks_where_the_setting_says()
    {
        var (window, _, vm, _, _) = Show();
        var keypad = KeypadOf(window);
        Assert.Equal(Dock.Bottom, DockPanel.GetDock(keypad));
        Assert.Equal(KeypadDock.Bottom, keypad.Dock);

        vm.Settings.KeypadDock = KeypadDock.Right;

        Assert.Equal(Dock.Right, DockPanel.GetDock(keypad));
        Assert.Equal(KeypadDock.Right, keypad.Dock);
    }

    /// <summary>Greyed rather than silently inert: the keyboard and the Keys menu send nothing visible while
    /// disconnected (the engine's action error is swallowed), and the keypad invites more clicking than either.</summary>
    [AvaloniaFact]
    public void The_keypad_is_enabled_only_while_connected()
    {
        var (window, _, _, session, _) = Show();
        var keypad = KeypadOf(window);
        Assert.False(keypad.IsEnabled);

        session.RaiseConnection(ConnectionState.Connected3270);
        Assert.True(keypad.IsEnabled);

        session.RaiseConnection(ConnectionState.Disconnected);
        Assert.False(keypad.IsEnabled);
    }

    /// <summary>The keypad form of A_key_pressed_while_the_previous_one_is_in_flight_still_reaches_the_host: the
    /// method, not the command, so a second click while the first round trip is open still reaches the host.</summary>
    [AvaloniaFact]
    public async Task A_keypad_click_while_the_previous_key_is_in_flight_still_reaches_the_host()
    {
        var (window, _, vm, session, _) = Show();
        session.RaiseConnection(ConnectionState.Connected3270);
        vm.Settings.Keypad = true;
        session.SendKeyCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        KeypadButton(window, TerminalKey.PF1).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        KeypadButton(window, TerminalKey.PF2).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.Equal(["key:PF1", "key:PF2"], session.Calls);
        session.SendKeyCompletion.SetResult();
        await Task.Yield();
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~SessionWindowTests"`
Expected: the six new tests fail (`KeypadPanel` is not found, so `KeypadOf` returns null and throws); every existing test still passes.

- [ ] **Step 3: `CancelTap` on the screen**

In `src/LizTerm.App/Controls/TerminalScreen.cs`, after `OnWindowDeactivated`:

```csharp
    /// <summary>Ends a modifier tap in progress, for a pointer press this control does not see: a keypad button is
    /// clicked elsewhere in the window, and Right Ctrl held across that click must not become Enter on its release
    /// (keypad spec §6.2). The same rule OnPointerPressed applies to presses on the screen itself.</summary>
    public void CancelTap() => _taps.Reset();
```

- [ ] **Step 4: Dock the control**

In `src/LizTerm.App/Views/SessionWindow.axaml`, between the find bar's closing `</Border>` and `<controls:TerminalScreen`:

```xml
    <!-- Declared innermost (keypad spec §6.1): at the bottom it sits directly under the screen, above the find,
         error and status bars; on the right it stands beside the screen and above those three, spanning the
         screen's height. Two bindings to one setting: the grid's shape is the control's, its edge of the window is
         this panel's. Invisible costs no layout. IsEnabled follows the connection so a disconnected session shows
         a greyed keypad rather than buttons that silently do nothing. -->
    <controls:Keypad x:Name="KeypadPanel"
                     DockPanel.Dock="{Binding Settings.KeypadDock, Converter={x:Static controls:Keypad.DockPanelDock}}"
                     Dock="{Binding Settings.KeypadDock}"
                     IsVisible="{Binding Settings.Keypad}"
                     IsEnabled="{Binding IsConnected}" />
```

- [ ] **Step 5: Wire the event**

In `src/LizTerm.App/Views/SessionWindow.axaml.cs`, in the constructor after `Screen.FindRequested += (_, _) => ShowFind();`:

```csharp
        // The keypad's keys take the screen's route: the method, never the command. CancelTap first, because a
        // click here is a pointer press the screen does not see (keypad spec §6.2); Focus last, a no-op while the
        // non-focusable buttons leave the keyboard alone, and the guarantee when something else (the find box) had it.
        KeypadPanel.KeyRequested += (_, key) =>
        {
            Screen.CancelTap();
            _ = ViewModel?.SendKeyAsync(key);
            Screen.Focus();
        };
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~SessionWindowTests"`
Expected: PASS. If `A_keypad_click_reaches_the_host_and_leaves_the_screen_focused` finds no call, the pointer landed outside the button: print `button.Bounds` and `centre`, and check `window.UpdateLayout()` ran after `Keypad` became visible.

- [ ] **Step 7: Run the whole App test project**

Run: `dotnet test tests/LizTerm.App.Tests`
Expected: PASS. `NativeMenuTests` is untouched by this task (no menu item yet).

- [ ] **Step 8: Commit**

```bash
git add src/LizTerm.App/Controls/TerminalScreen.cs src/LizTerm.App/Views/SessionWindow.axaml src/LizTerm.App/Views/SessionWindow.axaml.cs tests/LizTerm.App.Tests/Views/SessionWindowTests.cs
git commit -m "Dock the keypad in the session window, routed through CancelTap, SendKeyAsync and Focus

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 7: View > Keypad, in both menus

**Files:**
- Modify: `src/LizTerm.App/Views/SessionWindow.axaml` (View, in both menus), `src/LizTerm.App/Views/SessionWindow.axaml.cs` (after `SetCrosshair`)
- Test: `tests/LizTerm.App.Tests/Views/NativeMenuTests.cs`

**Interfaces:**
- Consumes: `SettingsViewModel.Keypad` (Task 2); `KeypadLayout.Banks` (Task 3).
- Produces: a `_Keypad` check-box item under View in both menus; classic `x:Name="KeypadMenuItem"`; handlers `OnKeypadClick`, `OnKeypadClickNative` over `ToggleKeypad`.

- [ ] **Step 1: Write the failing tests, and update the View-children assertion**

Add `using Avalonia.Interactivity;` to the usings of `tests/LizTerm.App.Tests/Views/NativeMenuTests.cs` if it is not already there (for `RoutedEventArgs`).

In `The_crosshair_modes_are_grouped_under_their_own_submenu`, replace the body from `var nativeView = ...` to the end with:

```csharp
        var nativeView = MenuLookup.Item(NativeMenu.GetMenu(window), "_View")!;
        var nativeChildren = nativeView.Menu!.Items.OfType<NativeMenuItem>().ToArray();
        Assert.Equal(["_Crosshair", "_Keypad"], nativeChildren.Select(i => i.Header));
        Assert.Equal(expected, nativeChildren[0].Menu!.Items.OfType<NativeMenuItem>().Select(i => i.Header));

        var classicView = window.FindControl<Menu>("ClassicMenu")!.Items.OfType<MenuItem>()
            .Single(i => (string)i.Header! == "_View");
        var classicChildren = classicView.Items.OfType<MenuItem>().ToArray();
        Assert.Equal(["_Crosshair", "_Keypad"], classicChildren.Select(i => (string)i.Header!));
        Assert.Equal(expected, classicChildren[0].Items.OfType<MenuItem>().Select(i => (string)i.Header!));
```

Then add, after `The_crosshair_reaches_the_terminal_screen`:

```csharp
    /// <summary>View > Keypad is a check box in the Crosshair items' shape on both menus (keypad spec §6.3):
    /// RaiseClicked is the entry point both real renderers use, the handler flips the setting, and the one-way
    /// bindings carry the mark back to both items. The classic item is driven through its own Click for the same
    /// reason. No gesture: nothing outside Edit carries one.</summary>
    [AvaloniaFact]
    public void Clicking_view_keypad_flips_the_setting_and_the_check_mark_on_both_menus()
    {
        var (window, vm, _, _) = Show();
        var native = Item(window, "_View", "_Keypad");
        var classic = window.FindControl<MenuItem>("KeypadMenuItem")!;
        Assert.Equal(MenuItemToggleType.CheckBox, native.ToggleType);
        Assert.Null(native.Gesture);
        Assert.False(native.IsChecked);
        Assert.False(classic.IsChecked);

        ((INativeMenuItemExporterEventsImplBridge)native).RaiseClicked();
        Assert.True(vm.Settings.Keypad);
        Assert.True(native.IsChecked);
        Assert.True(classic.IsChecked);

        classic.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Assert.False(vm.Settings.Keypad);
        Assert.False(native.IsChecked);
        Assert.False(classic.IsChecked);
    }

    /// <summary>The keypad offers at least what the menu does (keypad spec §3), read from the menu itself so the
    /// two cannot drift: a key added to the Keys menu and not to KeypadLayout fails here.</summary>
    [AvaloniaFact]
    public void Every_key_on_the_Keys_menu_is_on_the_keypad()
    {
        var (window, _, _, _) = Show();
        var onKeypad = KeypadLayout.Banks.SelectMany(bank => bank).Select(k => k.Key).ToHashSet();
        var onMenu = MenuLookup.Item(NativeMenu.GetMenu(window), "_Keys")!.Menu!.Items
            .OfType<NativeMenuItem>().Where(i => i is not NativeMenuItemSeparator)
            .Select(i => (TerminalKey)i.CommandParameter!).ToArray();

        Assert.NotEmpty(onMenu);
        Assert.All(onMenu, key => Assert.Contains(key, onKeypad));
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~NativeMenuTests"`
Expected: `Clicking_view_keypad...` fails (no `_View > _Keypad`), `The_crosshair_modes_are_grouped_under_their_own_submenu` fails (View has one child), `Every_key_on_the_Keys_menu_is_on_the_keypad` passes already (it reads Task 3's table). Everything else passes.

- [ ] **Step 3: Add the native item**

In `src/LizTerm.App/Views/SessionWindow.axaml`, in the native `_View` menu, after the `_Crosshair` item's closing `</NativeMenuItem>` (the one that ends the submenu) and before `</NativeMenu>`:

```xml
            <!-- One-way plus Click, the Crosshair items' shape: a NativeMenuItem never toggles itself, and the
                 handler flips the setting on the view model, whose change reaches every window's panel and check
                 mark. No Gesture: nothing outside Edit carries one (keypad spec §6.3). -->
            <NativeMenuItem Header="_Keypad" ToggleType="CheckBox" Click="OnKeypadClickNative"
                            IsChecked="{Binding Settings.Keypad, Mode=OneWay}" />
```

- [ ] **Step 4: Add the classic item**

In the classic `_View` `MenuItem`, after the `_Crosshair` item's closing `</MenuItem>`:

```xml
        <!-- The same shape as the native item and the Crosshair radios above, so the two menus stay alike and
             Wire Log stays the one item whose two menus differ. -->
        <MenuItem x:Name="KeypadMenuItem" Header="_Keypad" ToggleType="CheckBox" Click="OnKeypadClick"
                  IsChecked="{Binding Settings.Keypad, Mode=OneWay}" />
```

- [ ] **Step 5: Add the handlers**

In `src/LizTerm.App/Views/SessionWindow.axaml.cs`, after `SetCrosshair`:

```csharp
    private void OnKeypadClick(object? sender, RoutedEventArgs e) => ToggleKeypad();
    private void OnKeypadClickNative(object? sender, EventArgs e) => ToggleKeypad();

    /// <summary>Flips the setting; the one-way bindings carry the check mark back on both menus and the panel into
    /// or out of every window (keypad spec §6.3).</summary>
    private void ToggleKeypad()
    {
        if (ViewModel is { } vm) vm.Settings.Keypad = !vm.Settings.Keypad;
    }
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~NativeMenuTests|FullyQualifiedName~SessionWindowTests"`
Expected: PASS, the parity guard, `Every_native_item_can_actually_be_activated` and `Only_the_edit_menu_carries_gestures` included.

- [ ] **Step 7: Commit**

```bash
git add src/LizTerm.App/Views/SessionWindow.axaml src/LizTerm.App/Views/SessionWindow.axaml.cs tests/LizTerm.App.Tests/Views/NativeMenuTests.cs
git commit -m "Add View > Keypad to both menus

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 8: The Keypad group in Preferences

**Files:**
- Modify: `src/LizTerm.App/Views/PreferencesWindow.axaml` (after `BellSoundNote`, before `SaveErrorText`), `src/LizTerm.App/Views/PreferencesWindow.axaml.cs`
- Test: `tests/LizTerm.App.Tests/Views/PreferencesWindowTests.cs`

**Interfaces:**
- Consumes: `SettingsViewModel.Keypad`, `.KeypadDock`, `KeypadDockConverter.Instance` (Task 2).
- Produces: named controls `KeypadBox`, `KeypadBottom`, `KeypadRight`; handlers `OnKeypadBottomClick`, `OnKeypadRightClick`.

- [ ] **Step 1: Write the failing tests**

Add to `tests/LizTerm.App.Tests/Views/PreferencesWindowTests.cs`, after `The_sound_radios_follow_a_change_made_elsewhere`:

```csharp
    /// <summary>The show box is here as well as in View so the group makes sense on its own (keypad spec §7).</summary>
    [AvaloniaFact]
    public void The_keypad_box_writes_through_and_follows_the_settings()
    {
        var (window, settings) = Show();
        var box = window.FindControl<CheckBox>("KeypadBox")!;
        Assert.False(box.IsChecked);

        box.IsChecked = true;
        Assert.True(settings.Keypad);

        settings.Keypad = false;
        Assert.False(box.IsChecked);
    }

    [AvaloniaFact]
    public void Clicking_a_dock_radio_sets_the_shared_settings_and_checks_exactly_that_radio()
    {
        var (window, settings) = Show();
        var bottom = window.FindControl<RadioButton>("KeypadBottom")!;
        var right = window.FindControl<RadioButton>("KeypadRight")!;
        Assert.True(bottom.IsChecked);
        Assert.False(right.IsChecked);

        Click(right);
        Assert.Equal(KeypadDock.Right, settings.KeypadDock);
        Assert.False(bottom.IsChecked);
        Assert.True(right.IsChecked);

        Click(bottom);
        Assert.Equal(KeypadDock.Bottom, settings.KeypadDock);
        Assert.True(bottom.IsChecked);
        Assert.False(right.IsChecked);
    }

    [AvaloniaFact]
    public void The_dock_radios_show_the_saved_value_on_open()
    {
        var (window, _) = Show(new SettingsViewModel { KeypadDock = KeypadDock.Right });

        Assert.True(window.FindControl<RadioButton>("KeypadRight")!.IsChecked);
        Assert.False(window.FindControl<RadioButton>("KeypadBottom")!.IsChecked);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~PreferencesWindowTests"`
Expected: the three new tests fail (the controls are not found); the rest pass.

- [ ] **Step 3: Add the group**

In `src/LizTerm.App/Views/PreferencesWindow.axaml`, after the `BellSoundNote` `TextBlock` and before the `SaveErrorText` one:

```xml
    <TextBlock Text="Keypad" FontWeight="SemiBold" Margin="0,10,0,0" />
    <!-- The show box is here as well as in View so the group makes sense on its own: a position with no way to
         show the thing would be a puzzle (keypad spec §7). -->
    <CheckBox x:Name="KeypadBox" Content="Show the keypad" IsChecked="{Binding Keypad, Mode=TwoWay}" />
    <!-- The Crosshair radios' shape over KeypadDockConverter: one-way check marks plus Click handlers. -->
    <RadioButton x:Name="KeypadBottom" GroupName="KeypadDock" Content="At the bottom of the window" Click="OnKeypadBottomClick"
                 IsChecked="{Binding KeypadDock, Converter={x:Static vm:KeypadDockConverter.Instance}, ConverterParameter=Bottom, Mode=OneWay}" />
    <RadioButton x:Name="KeypadRight" GroupName="KeypadDock" Content="On the right of the window" Click="OnKeypadRightClick"
                 IsChecked="{Binding KeypadDock, Converter={x:Static vm:KeypadDockConverter.Instance}, ConverterParameter=Right, Mode=OneWay}" />
```

- [ ] **Step 4: Add the handlers**

In `src/LizTerm.App/Views/PreferencesWindow.axaml.cs`, after `OnBellSoundSystemAlertClick`:

```csharp
    private void OnKeypadBottomClick(object? sender, RoutedEventArgs e) => Settings.KeypadDock = KeypadDock.Bottom;
    private void OnKeypadRightClick(object? sender, RoutedEventArgs e) => Settings.KeypadDock = KeypadDock.Right;
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~PreferencesWindowTests"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/LizTerm.App/Views/PreferencesWindow.axaml src/LizTerm.App/Views/PreferencesWindow.axaml.cs tests/LizTerm.App.Tests/Views/PreferencesWindowTests.cs
git commit -m "Add the Keypad group to Preferences

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 9: Full suite, zero warnings, and a manual pass on macOS

**Files:** none changed unless a check fails.

- [ ] **Step 1: The full suite**

Run: `LIZTERM_B3270_PATH=/opt/homebrew/bin/b3270 dotnet test LizTerm.slnx`
Expected: every project passes; the live-host tests skip themselves. `RepositoryHeadersTests` passes, which proves every new file carries the licence header.

- [ ] **Step 2: Zero warnings**

Run: `dotnet build LizTerm.slnx --no-incremental 2>&1 | grep -c " warning "`
Expected: `0`.

- [ ] **Step 3: Run the app against a host**

Run: `LIZTERM_B3270_PATH=/opt/homebrew/bin/b3270 dotnet run --project src/LizTerm.App`

Connect to the MVS/CE test host (the live lane in `docs/development.md`; Robert's environment file outside the repo holds its address and credentials) and log on. Confirm, in order:

1. View > Keypad shows a three-row strip under the screen; the check mark appears on the menu; the screen's font did not shrink at the default window size.
2. Click PF3, PA1 and Enter; the host responds to each as it does to the keyboard. Immediately after a click, type a few characters; they land on the screen, so focus stayed.
3. Hold Right Ctrl, click PF3, release; with Help > Wire Log on, the log shows `PF(3)` and no `Enter()`.
4. Hover a button: the tooltip shows glyphs on macOS ("⌥+1" on PA1, "⌃+F1 or ⇧+F1" on PF13); Dup shows none.
5. File > Disconnect: the keypad greys out. Reconnect: it comes back.
6. Open a second session window. Preferences > Keypad > "On the right of the window": both windows move the keypad to the right, columns reading PF1 to PF12 down the first. Back to "At the bottom of the window": both move back.
7. Shrink the window until the screen refits under the keypad; nothing overlaps.
8. Quit, relaunch: the keypad is where it was left. View > Keypad turns it off; `settings.json` holds `"keypad": false` and the dock key.
9. Relaunch with `LIZTERM_MENU=classic`: the in-window View menu shows Keypad and toggles the panel.

Report what was observed to Robert in chat, including anything that did not match. Fix before continuing if anything did not.

---

### Task 10: Documentation and the spec's As-built section

**Files:**
- Modify: `docs/user-guide.md` ("The session window" after the bell paragraph; the last paragraph of "Keyboard"; "Preferences"); `src/LizTerm.App/CLAUDE.md` (a new "Keypad" section before "## Menus"; "Gestures"; "Wiring rules"; "Settings and Preferences"); `tests/CLAUDE.md` (App tests); `docs/superpowers/specs/2026-09-11-lizterm-keypad-design.md` (append)

- [ ] **Step 1: The user guide**

In `docs/user-guide.md`, under "The session window", after the bell paragraph (ends "limited to two bells a second."):

```markdown
**View > Keypad** shows a panel of buttons for PF1 to PF24, PA1 to PA3, Enter, Clear, Reset, Attn, SysReq, Erase
EOF, Erase Input, Dup and Field Mark, for the keys a keyboard cannot reach. It is off by default, remembered, and
applies to every session window; Preferences chooses whether it sits below the screen or to its right. Hold the
pointer over a button to see its keyboard shortcut. The buttons never take the keyboard away from the screen, and
they are greyed out while the session is disconnected.
```

In "Keyboard", change the closing paragraph

```markdown
The **Keys** menu sends every key the keyboard might not reach: Clear, Reset, Attn, SysReq, Dup, Field Mark, PA1 to
PA3, and PF13 to PF24.
```

to

```markdown
The **Keys** menu sends every key the keyboard might not reach: Clear, Reset, Attn, SysReq, Dup, Field Mark, PA1 to
PA3, and PF13 to PF24. The on-screen keypad (**View > Keypad**) offers all of those as buttons, plus PF1 to PF12,
Enter, Erase EOF and Erase Input.
```

Under "Preferences", after the Bell bullet:

```markdown
- **Keypad** — whether the on-screen keypad is shown (the same as View > Keypad), and whether it docks below the
  screen or to its right.
```

- [ ] **Step 2: `src/LizTerm.App/CLAUDE.md`**

Insert a new section immediately before `## Menus`:

```markdown
## Keypad (`Controls/Keypad.axaml`)

- A `UserControl` that mirrors `TerminalScreen`'s contract: it raises `KeyRequested` and knows nothing about view
  models. Its buttons are built in the constructor from `KeypadLayout.Banks` (three banks of twelve; `KeypadKey` is
  a label plus a `TerminalKey`), so no button is written by hand. `Dock` (`KeypadDock`) lays the same `UniformGrid`
  out bank-per-row at the bottom or bank-per-column on the right; `Keypad.DockPanelDock` is the converter the
  window's `DockPanel.Dock` binding uses, two bindings to one setting because the grid's shape is the control's and
  its edge of the window is the window's. The window binds `IsVisible` to `Settings.Keypad` and `IsEnabled` to
  `IsConnected`. `NativeMenuTests.Every_key_on_the_Keys_menu_is_on_the_keypad` holds the menu to a subset of the
  table.
- **Every button is `Focusable = false`**, so a click never moves the keyboard off the screen; the window still calls
  `Screen.Focus()` after each key as the guarantee. A click goes to `SendKeyAsync`, never the command (see Keyboard
  above for why), and first to `TerminalScreen.CancelTap()`: a keypad click is a pointer press the screen does not
  see, and Right Ctrl held across it must not become Enter on release.
- Tooltips come from `KeymapHints.Describe(keymap, key, format)` (`Keyboard/`), the reverse of a `Keymap`: chords
  ordered unmodified first, then by modifier, function keys ahead within a group, taps last; ordinary chords through
  Avalonia's `KeyGesture.ToString("p", format)`, taps worded by hand. The control passes a null format, the
  platform's registration (glyphs on macOS, words elsewhere); tests pass an explicit `KeyGestureFormatInfo`. The
  control's `Keymap` property is the #18 hook, and nothing binds it yet.
```

In "Gestures", change

`So View, File > Save Screen As... and Edit > Copy Screen as HTML carry none.`

to

`So View (Crosshair and Keypad), File > Save Screen As... and Edit > Copy Screen as HTML carry none.`

In "Wiring rules", after the "Wire Log is the one item whose two menus differ on purpose" bullet, add:

```markdown
- View > Keypad is a check box in the Crosshair items' shape on *both* menus: a one-way `IsChecked` plus a Click
  handler (`ToggleKeypad`) that flips `Settings.Keypad`, so Wire Log stays the only item whose two menus differ.
```

In "Settings and Preferences", after the `App.ShowPreferences` bullet, add:

```markdown
- The Preferences **Keypad** group is a two-way `KeypadBox` for `Settings.Keypad` (also View > Keypad) plus the two
  dock radios over `KeypadDockConverter`, the Crosshair shape.
```

- [ ] **Step 3: `tests/CLAUDE.md`**

In "App tests", after the "Drive the Preferences radios" bullet, add:

```markdown
- `KeymapHintsTests` format every expectation with an explicit `KeyGestureFormatInfo` (Avalonia's common key names,
  the default modifier words): the headless platform registers a format of its own, and what it says is not ours to
  assert. Keypad buttons are driven by raising `Button.ClickEvent`, or by a headless mouse press at the button's
  centre (after `window.UpdateLayout()`) when focus is the question.
```

- [ ] **Step 4: The spec's As-built section**

Append to `docs/superpowers/specs/2026-09-11-lizterm-keypad-design.md` a section `## 12. As built (<date>)` that names the plan, `docs/superpowers/plans/2026-09-11-lizterm-keypad.md`, and records every detail decided while implementing that the spec did not fix, in the bell spec's "As built" shape. If nothing was decided beyond the spec, the section reads:

```markdown
## 12. As built (2026-09-11)

Built as specified; the plan is `docs/superpowers/plans/2026-09-11-lizterm-keypad.md`. Two details were measured
before the plan was written and are already in §5: Avalonia's name for the Enter key is "Return", and function keys
sort ahead of other keys within a group so PF7 reads "F7 or PageUp".
```

- [ ] **Step 5: Verify and commit**

Run: `LIZTERM_B3270_PATH=/opt/homebrew/bin/b3270 dotnet test LizTerm.slnx` and `dotnet build LizTerm.slnx --no-incremental 2>&1 | grep -c " warning "`
Expected: all pass; `0`.

```bash
git add docs/user-guide.md src/LizTerm.App/CLAUDE.md tests/CLAUDE.md docs/superpowers/specs/2026-09-11-lizterm-keypad-design.md
git commit -m "Document the keypad, and record what was built

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

- [ ] **Step 6: Hand back to Robert**

Do not push. Tell Robert in chat: the branch (and that renaming it to an issue-31 name before pushing is his call), the commit list, the manual-pass results from Task 9, and that pushing and opening the PR (which closes #31, and adds the comment to #23 naming `KeymapHints`) wait for his word.
