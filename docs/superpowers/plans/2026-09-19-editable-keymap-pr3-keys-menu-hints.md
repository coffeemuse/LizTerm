# Editable keymap, PR 3: the Keys menu hints — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Every item on the session window's **Keys** menu names the keystrokes that send it (`PA2  Alt+2 or Ctrl+Home`), in both menu renderers alike, following the user's keymap the moment it changes; this closes #23 and, with it, #18.

**Architecture:** The hint goes in the item's **header text**, never in a gesture: on the native menu a `NativeMenuItem.Gesture` is a real AppKit key equivalent that steals the keystroke from the screen (native-menus spec §2.1, issue #23), and header text is the one form both renderers draw alike, which keeps the parity test honest. A tiny pure helper, `Menus/KeysMenuHints.Header`, joins a name and `KeymapHints.Describe`'s line with two spaces. `SessionWindow` captures each Keys item's declared name and `TerminalKey` once in its constructor (native and classic side by side, as `RebuildSessionRows` pairs the Window menu's rows) and rewrites both headers inside `ApplyKeymap`, the method that already runs on open and on every `KeymapViewModel.Changed`. Nothing new is stored: the keymap is the truth and the headers are derived from it.

**Tech Stack:** .NET 10, Avalonia 12.1.2 (headless tests via `[AvaloniaFact]`, compiled bindings on), xunit.v3.

**Spec:** `docs/superpowers/specs/2026-09-19-lizterm-editable-keymap-design.md` — this plan implements §6.2 (the Keys menu), §6.3 (the changelog) and the "Keys menu header" half of §7.2's second bullet. PR 1 (`docs/superpowers/plans/2026-09-19-editable-keymap-pr1-model-store-wiring.md`) is on `main` as cc7080f; PR 2 (`docs/superpowers/plans/2026-09-19-editable-keymap-pr2-keyboard-tab.md`) as 1b703f2. Read issue #23 before starting: it records why a gesture is the trap.

## Global Constraints

- **Dependency rule.** Everything here is `LizTerm.App` and its tests. `LizTerm.Core` is untouched. App code never names a backend.
- **Licence headers.** Every new `.cs` file starts with `// This file is part of LizTerm.` / `// Copyright 2026 by CoffeeMuse` / `// SPDX-License-Identifier: BSD-3-Clause`, then a blank line. `RepositoryHeadersTests` fails the suite otherwise.
- **No gesture on a Keys item, ever.** Not `NativeMenuItem.Gesture`, and not the classic `MenuItem.InputGesture` either (display-only, but it would make the two menus differ and the parity walk fail, correctly). `NativeMenuTests.Only_edit_and_two_window_items_carry_gestures` guards the first; the parity walk guards the second.
- **Both menus, always together.** Every header write lands on the native item and the classic item in the same statement block. `NativeMenuTests.The_native_menu_matches_the_classic_menu_item_for_item` compares headers item for item.
- **Never `Clear()` or replace a `NativeMenu`** (#60). This plan only changes `Header` on items that already exist, which is safe in every menu style: under `InWindow`, `ApplyMenuStyle` stashes the top-level native items but the Keys submenu object and its items survive, so a header written while stashed is what comes back on a refill.
- **Zero warnings.** CI builds with `-warnaserror`. Before calling the PR done: `dotnet build LizTerm.slnx --no-incremental 2>&1 | grep -c " warning "` must print `0`.
- **Tests.** xunit.v3 in VSTest mode; run one class with `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~<Class>"`. Window tests are `[AvaloniaFact]`; pure tests are `[Fact]` and format every expectation with an explicit `KeyGestureFormatInfo` (the headless platform registers a format of its own, and what it says is not ours to assert). A test that compares a header against the platform's own words computes the expectation with `KeymapHints.Describe(keymap, key)` and a null format, exactly as the window does.
- **Commits.** Small, one per task. End every commit message with `Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>` (the attribution line from the session's system reminder, whichever model is executing).
- **Docs have one home.** When a change makes a documented sentence untrue, fix that sentence where it lives. `CHANGELOG.md` entries go under `## Unreleased`. `src/LizTerm.App/Assets/Docs/user-guide.html` is generated from `docs/user-guide.md` (Task 3 says how) and never hand-edited.
- **No em dashes in new prose** that ends up in the user guide or the changelog; use a full stop or a comma.
- Run every command from the root of the worktree you are executing in (create one with `superpowers:using-git-worktrees` off `main`; this plan's branch is `claude/issue-18-pr3-keys-menu-hints`).

## Where this plan deliberately differs from the spec, and why

1. **The classic menu carries the hint in header text too, not in `InputGesture` (spec §6.2 says header text for both; issue #23 asked whether classic should differ).** Header text on both is what the spec chose and what keeps the parity walk a real guard. A right-aligned shortcut column on the classic menu would look more native on Windows and Linux, and it is deliberately not done: two renderers that draw the same definition differently is the kind of drift the parity test exists to catch.
2. **The hint uses the platform's own key wording (a null `IFormatProvider`), as the keypad tooltips do.** On macOS that is glyphs (`⌥1`), elsewhere words (`Alt+1`). The spec's example `PA2  Alt+2 or Ctrl+Home` is the Windows and Linux rendering. One rule for tooltips, chips and menu; a menu that spelled chords differently from the tooltip beside it would be the bug.
3. **Nothing in the spec says what a Keys item reads while the window has no data context yet.** The headers are written once at the end of the constructor from the default keymap, so they are never bare between construction and the first `ApplyKeymap`; the design-time preview shows the defaults.

---

## File map

| File | Responsibility |
|---|---|
| Create `src/LizTerm.App/Menus/KeysMenuHints.cs` | `Header(name, chords, format)`: the name alone, or the name, two spaces and `KeymapHints.Describe`'s line |
| Modify `src/LizTerm.App/Views/SessionWindow.axaml` | `x:Name="KeysMenuItem"` on the classic `_Keys` item (the native one is found by header, as `_Window` is) |
| Modify `src/LizTerm.App/Views/SessionWindow.axaml.cs` | Constructor captures the Keys rows (native item, classic item, declared name, key); `ApplyKeymap` rewrites every header |
| Tests | Create `tests/LizTerm.App.Tests/Menus/KeysMenuHintsTests.cs`; modify `tests/LizTerm.App.Tests/Views/SessionWindowKeymapTests.cs` and `tests/LizTerm.App.Tests/Views/NativeMenuTests.cs` |
| Docs | `docs/user-guide.md` (Keyboard section's Keys paragraph), regenerated `src/LizTerm.App/Assets/Docs/user-guide.html`, `CHANGELOG.md`, `src/LizTerm.App/CLAUDE.md`, `tests/CLAUDE.md` |

The Keys menu today (`src/LizTerm.App/Views/SessionWindow.axaml`, the native block near line 112 and the classic block near line 242) declares, in this order on both menus: Clear, Reset, Attn, SysReq, Dup, Field Mark, Insert, a separator, PA1, PA2, PA3, a separator, PF13 to PF24. Every item binds `SendKeyCommand` with the `TerminalKey` as `CommandParameter`, which is how this plan tells items apart. Two of them, Dup and Field Mark, have **no default chord** (the guide's table has no row for them; they were added to the menu for #16 precisely because nothing on the keyboard reaches them), so they keep their bare names under the default keymap. That is the natural "no chord" case the tests use.

---

### Task 1: `KeysMenuHints.Header`, the one rule for a Keys header

**Files:**
- Create: `src/LizTerm.App/Menus/KeysMenuHints.cs`
- Test: `tests/LizTerm.App.Tests/Menus/KeysMenuHintsTests.cs` (the test project's existing `Menus/` folder; the namespace is `LizTerm.App.Tests.Menus`)

**Interfaces:**
- Consumes: `KeymapHints.Describe(IEnumerable<KeyChord> chords, IFormatProvider? format = null)` (`src/LizTerm.App/Keyboard/KeymapHints.cs`), which returns null for no chords, one chord as is, several joined with a comma list and "or", in tooltip order.
- Produces: `internal static string KeysMenuHints.Header(string name, IEnumerable<KeyChord> chords, IFormatProvider? format = null)`. Task 2 calls it with each item's declared name and the chords the composed keymap maps to that item's key.

- [ ] **Step 1: Write the failing tests**

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Input;
using Avalonia.Input.Platform;
using LizTerm.App.Keyboard;
using LizTerm.App.Menus;
using LizTerm.Core.Session;

namespace LizTerm.App.Tests.Menus;

/// <summary>Editable keymap spec §6.2: a Keys item's header is its name, two spaces, then the keypad tooltip's own
/// line for that key; a key with no chord keeps its bare name. Formatted with an explicit KeyGestureFormatInfo for
/// the reason KeymapHintsTests gives: the headless platform's own wording is not ours to assert.</summary>
public class KeysMenuHintsTests
{
    private static readonly Keymap Map = DefaultKeymap.Create(destructiveBackspace: true);
    private static readonly KeyGestureFormatInfo Words = new(new Dictionary<Key, string>());

    private static IEnumerable<KeyChord> ChordsFor(TerminalKey key) =>
        Map.Keys.Where(pair => pair.Value == key).Select(pair => pair.Key);

    [Fact]
    public void A_key_with_chords_reads_its_name_two_spaces_and_the_tooltips_line()
    {
        Assert.Equal("PA2  Alt+2 or Ctrl+Home", KeysMenuHints.Header("PA2", ChordsFor(TerminalKey.PA2), Words));
        Assert.Equal("Attn  Escape", KeysMenuHints.Header("Attn", ChordsFor(TerminalKey.Attn), Words));
    }

    [Fact]
    public void A_key_with_no_chord_keeps_its_bare_name()
    {
        Assert.Equal("Field Mark", KeysMenuHints.Header("Field Mark", ChordsFor(TerminalKey.FieldMark), Words));
        Assert.Equal("Dup", KeysMenuHints.Header("Dup", [], Words));
    }

    /// <summary>The wording is KeymapHints' and nothing else, so a chip, a tooltip and a menu item cannot disagree.</summary>
    [Fact]
    public void The_hint_is_the_tooltips_own_wording()
    {
        var expected = "Insert  " + KeymapHints.Describe(Map, TerminalKey.Insert, Words);

        Assert.Equal(expected, KeysMenuHints.Header("Insert", ChordsFor(TerminalKey.Insert), Words));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~KeysMenuHintsTests"`
Expected: a build error, `The type or namespace name 'KeysMenuHints' does not exist`.

- [ ] **Step 3: Write the helper**

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.App.Keyboard;

namespace LizTerm.App.Menus;

/// <summary>What a Keys menu item reads (editable keymap spec §6.2, #23): the action's name, two spaces, then the
/// keystrokes that send it in KeymapHints' wording, the keypad tooltip's own line, so a menu item, a tooltip and a
/// Keyboard tab chip cannot disagree. A key no chord sends keeps its bare name. In the header text on purpose, never
/// a gesture: on the native menu a gesture is an AppKit key equivalent that takes the keystroke away from the screen,
/// and header text is the one form both renderers draw alike, which is what keeps the parity test honest.</summary>
internal static class KeysMenuHints
{
    public static string Header(string name, IEnumerable<KeyChord> chords, IFormatProvider? format = null) =>
        KeymapHints.Describe(chords, format) is { } hint ? name + "  " + hint : name;
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~KeysMenuHintsTests"`
Expected: `Passed! - Failed: 0, Passed: 3`.

- [ ] **Step 5: Commit**

```bash
git add src/LizTerm.App/Menus/KeysMenuHints.cs tests/LizTerm.App.Tests/Menus/KeysMenuHintsTests.cs
git commit -m "App: KeysMenuHints.Header, a Keys item's name with its keystrokes in the tooltip's wording (#23)

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 2: The session window writes every Keys header from the keymap, on both menus

**Files:**
- Modify: `src/LizTerm.App/Views/SessionWindow.axaml` (the classic `<MenuItem Header="_Keys">`, near line 242)
- Modify: `src/LizTerm.App/Views/SessionWindow.axaml.cs` (fields near line 122–140; the constructor near line 40–50; `ApplyKeymap` near line 598)
- Test: `tests/LizTerm.App.Tests/Views/SessionWindowKeymapTests.cs`, `tests/LizTerm.App.Tests/Views/NativeMenuTests.cs`

**Interfaces:**
- Consumes: `KeysMenuHints.Header(string name, IEnumerable<KeyChord> chords, IFormatProvider? format = null)` from Task 1; `Keymap.Keys` (`IReadOnlyDictionary<KeyChord, TerminalKey>`); `MenuLookup.Item(NativeMenu?, string header)` (`src/LizTerm.App/Menus/MenuLookup.cs`); the existing `ApplyKeymap()`.
- Produces: the classic Keys menu is reachable as `window.FindControl<MenuItem>("KeysMenuItem")`; every Keys item's `Header` (native `string?`, classic `object?` holding a string) equals `KeysMenuHints.Header(declaredName, chordsForItsKey)` under the window's composed keymap, from construction on and after every `KeymapViewModel.Changed`. Tests find a Keys item by its `CommandParameter`, never by header.

- [ ] **Step 1: Write the failing window tests**

In `tests/LizTerm.App.Tests/Views/SessionWindowKeymapTests.cs`, add `using LizTerm.App.Menus;` to the usings, then add this helper below `KeypadButton` and these two tests at the end of the class (before its closing brace):

```csharp
    /// <summary>A Keys item by the key it sends, never by header: the header carries the keystroke hint and follows
    /// the keymap. The classic side through the named menu, the native side through MenuLookup, which reaches the
    /// declared submenu whether or not the style has stashed the top-level items.</summary>
    private static (NativeMenuItem Native, MenuItem Classic) KeysItems(SessionWindow window, TerminalKey key)
    {
        var native = MenuLookup.Item(NativeMenu.GetMenu(window), "_Keys")!.Menu!.Items.OfType<NativeMenuItem>()
            .Single(item => Equals(item.CommandParameter, key));
        var classic = window.FindControl<MenuItem>("KeysMenuItem")!.Items.OfType<MenuItem>()
            .Single(item => Equals(item.CommandParameter, key));
        return (native, classic);
    }

    /// <summary>Editable keymap spec §6.2 and §7.2: a rebind reaches the Keys menu's header text on both renderers.
    /// The window is InWindow, so the native top-level items are stashed out of the menu while this runs, and the
    /// Keys items still have to follow, because a later switch to Native puts those same objects back.</summary>
    [AvaloniaFact]
    public void A_rebind_reaches_the_Keys_menu_headers_on_both_menus()
    {
        var keymap = new KeymapViewModel();
        var (window, _, screen) = Show(destructiveBackspace: true, keymap);
        var (native, classic) = KeysItems(window, TerminalKey.PA1);
        Assert.Equal("PA1  " + KeymapHints.Describe(screen.Keymap, TerminalKey.PA1), native.Header);
        Assert.Equal(native.Header, classic.Header as string);
        Assert.DoesNotContain("F9", native.Header);

        keymap.Bind(new KeyChord(Key.F9, KeyModifiers.Alt), new KeymapAction.SendKey(TerminalKey.PA1));

        Assert.Equal("PA1  " + KeymapHints.Describe(screen.Keymap, TerminalKey.PA1), native.Header);
        Assert.Contains("F9", native.Header);
        Assert.Equal(native.Header, classic.Header as string);
        Assert.Null(native.Gesture);
        Assert.Null(classic.InputGesture);
    }

    /// <summary>A key the user has taken every chord away from keeps its bare name, on both menus, and gets its hint
    /// back on Reset.</summary>
    [AvaloniaFact]
    public void A_key_left_with_no_chord_keeps_its_bare_name_on_the_Keys_menu()
    {
        var keymap = new KeymapViewModel();
        var (window, _, _) = Show(destructiveBackspace: true, keymap);
        var (native, classic) = KeysItems(window, TerminalKey.PA3);
        Assert.StartsWith("PA3  ", native.Header);

        keymap.Unbind(new KeyChord(Key.D3, KeyModifiers.Alt));
        keymap.Unbind(new KeyChord(Key.PageUp, KeyModifiers.Control));

        Assert.Equal("PA3", native.Header);
        Assert.Equal("PA3", classic.Header);

        keymap.ResetToDefaults();

        Assert.StartsWith("PA3  ", native.Header);
        Assert.Equal(native.Header, classic.Header as string);
    }
```

In `tests/LizTerm.App.Tests/Views/NativeMenuTests.cs`:

a) Add this helper next to `KeypadItem` (near line 95):

```csharp
    /// <summary>A Keys item by the key it sends (#23): the header carries the keystroke hint and follows the keymap,
    /// so "Insert" is a prefix of what the item reads, not the whole of it.</summary>
    private static NativeMenuItem KeysItem(SessionWindow window, TerminalKey key) =>
        MenuLookup.Item(NativeMenu.GetMenu(window), "_Keys")!.Menu!.Items.OfType<NativeMenuItem>()
            .SingleOrDefault(item => Equals(item.CommandParameter, key))
        ?? throw new InvalidOperationException($"no native menu item _Keys > {key}");
```

b) Replace the three lookups `Item(window, "_Keys", "Insert")` in `Keys_menu_insert_sends_the_insert_toggle`, `Keys_menu_insert_is_checked_while_the_host_reports_insert_mode_on_both_menus` and `Clicking_keys_menu_insert_leaves_the_check_mark_to_the_host_on_both_menus` (near lines 931–990) with `KeysItem(window, TerminalKey.Insert)`. Nothing else in those tests changes.

c) Replace `Keys_menu_offers_Dup_and_FieldMark` (near line 919) with:

```csharp
    /// <summary>#16: both are mapped in ActionMap and were reachable only from C#. The menu is the whole fix —
    /// keymap chords are a separate decision, since any chord has to clear the copy/paste/select-all gestures
    /// TerminalScreen checks before the keymap. Neither has a default chord, so under the default keymap each reads
    /// its bare name (#23): the "no chord" case, on the real window.</summary>
    [AvaloniaFact]
    public void Keys_menu_offers_Dup_and_FieldMark()
    {
        var (window, _, _, _) = Show();

        Assert.NotNull(KeysItem(window, TerminalKey.Dup).Command);
        Assert.NotNull(KeysItem(window, TerminalKey.FieldMark).Command);
        Assert.Equal("Dup", KeysItem(window, TerminalKey.Dup).Header);
        Assert.Equal("Field Mark", KeysItem(window, TerminalKey.FieldMark).Header);
    }
```

d) Add this test after `Keys_menu_offers_Dup_and_FieldMark`:

```csharp
    /// <summary>#23: every Keys item names the keystrokes that send it, in its header text and never in a gesture (a
    /// native gesture is a key equivalent that would take the keystroke from the screen; see
    /// Only_edit_and_two_window_items_carry_gestures), in both menus alike so the parity walk stays a real guard.
    /// The expectation is computed with the window's own keymap and the platform's own wording, exactly as the
    /// window computes the header, because the headless platform's key names are not ours to assert.</summary>
    [AvaloniaFact]
    public void Every_keys_item_carries_its_keystrokes_in_its_header_on_both_menus()
    {
        var (window, _, _, _) = Show();
        var map = window.FindControl<TerminalScreen>("Screen")!.Keymap;
        var native = MenuLookup.Item(NativeMenu.GetMenu(window), "_Keys")!.Menu!.Items.OfType<NativeMenuItem>()
            .Where(item => item is not NativeMenuItemSeparator).ToList();
        var classic = window.FindControl<MenuItem>("KeysMenuItem")!.Items.OfType<MenuItem>().ToList();
        Assert.Equal(native.Count, classic.Count);
        Assert.Equal(22, native.Count);

        foreach (var (nativeItem, classicItem) in native.Zip(classic))
        {
            var key = (TerminalKey)nativeItem.CommandParameter!;
            var hint = KeymapHints.Describe(map, key);
            if (hint is not null) Assert.EndsWith("  " + hint, nativeItem.Header);
            else Assert.DoesNotContain("  ", nativeItem.Header);
            Assert.Equal(nativeItem.Header, classicItem.Header as string);
            Assert.Null(nativeItem.Gesture);
            Assert.Null(classicItem.InputGesture);
        }
        Assert.Equal("PA2  " + KeymapHints.Describe(map, TerminalKey.PA2), KeysItem(window, TerminalKey.PA2).Header);
        Assert.Equal("Insert  " + KeymapHints.Describe(map, TerminalKey.Insert), KeysItem(window, TerminalKey.Insert).Header);
    }
```

`TerminalScreen` is already imported there (`using LizTerm.App.Controls;`); `KeymapHints` needs `using LizTerm.App.Keyboard;` added to that file's usings if it is not there yet.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~SessionWindowKeymapTests|FullyQualifiedName~NativeMenuTests"`
Expected: the new tests fail. `A_rebind_reaches_the_Keys_menu_headers_on_both_menus` fails on `FindControl<MenuItem>("KeysMenuItem")` returning null (a `NullReferenceException`), or, once the name exists, on `Assert.Equal("PA1  Alt+1", "PA1")`-shaped mismatches. The three Insert tests and `Keys_menu_offers_Dup_and_FieldMark` pass already (they only changed how they look the item up). The parity walk still passes: both menus still say `PA1`.

- [ ] **Step 3: Name the classic Keys menu**

In `src/LizTerm.App/Views/SessionWindow.axaml`, change the classic menu's line (near line 242)

```xml
      <MenuItem Header="_Keys">
```

to

```xml
      <!-- Named so the code-behind can pair its items with the native Keys items and write both headers (#23). -->
      <MenuItem x:Name="KeysMenuItem" Header="_Keys">
```

- [ ] **Step 4: Capture the rows and write the headers**

In `src/LizTerm.App/Views/SessionWindow.axaml.cs`:

a) Add a field next to `_sessionRows` (near line 140):

```csharp
    /// <summary>Every Keys item on both menus with the name it was declared with and the key it sends, paired in
    /// declaration order at construction (the parity test holds the two menus to the same order). ApplyKeymap
    /// writes each pair's headers from the keymap in force, so the menu says what the keyboard does (#23).</summary>
    private readonly List<(NativeMenuItem Native, MenuItem Classic, string Name, TerminalKey Key)> _keysRows = [];
```

b) In the constructor, after `_nativeMvsmfBrowser = ...;` and before `RebuildSessionRows();` (near line 49), add:

```csharp
        CaptureKeysRows();
```

and, at the very end of the constructor body, after the `Opened += ...` handler's closing `};`, add:

```csharp
        // The headers are never bare: the default keymap until a data context or an attached keymap composes another.
        ApplyKeymap();
```

c) Add the method after `RebuildSessionRows` (near line 690):

```csharp
    /// <summary>Pairs the declared Keys items of the two menus, the native side found through the declared submenu
    /// (which survives InWindow's stashing of the top-level items, as _nativeWindowMenu does) and the classic side
    /// through KeysMenuItem. A menu whose items differ in count or key is a declaration error and throws here, at
    /// construction, rather than writing one menu's hint onto the other's item.</summary>
    private void CaptureKeysRows()
    {
        var native = MenuLookup.Item(NativeMenu.GetMenu(this), "_Keys")!.Menu!.Items.OfType<NativeMenuItem>()
            .Where(item => item is not NativeMenuItemSeparator).ToList();
        var classic = KeysMenuItem.Items.OfType<MenuItem>().ToList();
        if (native.Count != classic.Count)
            throw new InvalidOperationException($"The native Keys menu declares {native.Count} items and the classic one {classic.Count}.");
        foreach (var (nativeItem, classicItem) in native.Zip(classic))
        {
            var key = (TerminalKey)nativeItem.CommandParameter!;
            if (!Equals(classicItem.CommandParameter, key))
                throw new InvalidOperationException($"Keys > {nativeItem.Header} sends {key} natively and {classicItem.CommandParameter} in the window.");
            _keysRows.Add((nativeItem, classicItem, nativeItem.Header!, key));
        }
    }
```

d) Replace `ApplyKeymap` (near line 598) with:

```csharp
    /// <summary>The map in force for this window: the profile's Backspace choice under the user's keymap.json. Set
    /// on the screen and on the keypad, whose tooltips follow it (keypad spec §4.4), and written into every Keys
    /// item's header on both menus (editable keymap spec §6.2): the keymap is reversed once for the 22 items, as
    /// the keypad reverses it once for its 36 buttons. A null format is the platform's own wording, the tooltips'.</summary>
    private void ApplyKeymap()
    {
        var destructive = ViewModel?.Profile.DestructiveBackspace ?? true;
        var map = _keymap?.Compose(destructive) ?? DefaultKeymap.Create(destructive);
        Screen.Keymap = map;
        KeypadPanel.Keymap = map;
        var chords = map.Keys.ToLookup(pair => pair.Value, pair => pair.Key);
        foreach (var (native, classic, name, key) in _keysRows)
        {
            var header = KeysMenuHints.Header(name, chords[key]);
            native.Header = header;
            classic.Header = header;
        }
    }
```

`using LizTerm.App.Menus;` is already in that file (it uses `MenuLookup`); confirm, and add it if not. `TerminalKey` comes from `LizTerm.Core.Session`, also already imported.

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~SessionWindowKeymapTests|FullyQualifiedName~NativeMenuTests|FullyQualifiedName~WindowMenuTests|FullyQualifiedName~SessionWindowTests"`
Expected: all pass, the parity walk included (both menus now say `PA1  Alt+1`, or the platform's glyph for it, on both sides). If `The_native_menu_matches_the_classic_menu_item_for_item` fails on a Keys header, one side was not written: check that both `native.Header` and `classic.Header` are assigned in the same loop.

Then the whole App suite: `dotnet test tests/LizTerm.App.Tests`. Expected: 0 failed. A test elsewhere that looked a Keys item up by header (`"Insert"`, `"PA1"`) is one this plan missed; change it to the `KeysItem` shape rather than weakening the header.

- [ ] **Step 6: Commit**

```bash
git add src/LizTerm.App/Views/SessionWindow.axaml src/LizTerm.App/Views/SessionWindow.axaml.cs tests/LizTerm.App.Tests/Views/SessionWindowKeymapTests.cs tests/LizTerm.App.Tests/Views/NativeMenuTests.cs
git commit -m "App: the Keys menu names each key's keystrokes in its header text, on both menus, following the keymap (#23)

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 3: The guide, the changelog, the notes, the gates and the PR

**Files:**
- Modify: `docs/user-guide.md` (the Keys paragraph in the Keyboard section, near line 195)
- Regenerate: `src/LizTerm.App/Assets/Docs/user-guide.html`
- Modify: `CHANGELOG.md` (the **Keyboard bindings** bullet under `## Unreleased`)
- Modify: `src/LizTerm.App/CLAUDE.md` (the "Keyboard" bullet that begins "`KeymapPolicy` is what the Keyboard tab refuses", and the "Gestures" section under "Menus")
- Modify: `tests/CLAUDE.md` (the native-menu bullet near line 107)

**Interfaces:**
- Consumes: everything from Tasks 1 and 2 as shipped.
- Produces: a PR against `main` that closes #18 and #23.

- [ ] **Step 1: The user guide**

In `docs/user-guide.md`, replace the paragraph that begins `The **Keys** menu sends every key the keyboard might not reach` (near line 195) with:

```markdown
The **Keys** menu sends every key the keyboard might not reach: Clear, Reset, Attn, SysReq, Dup, Field Mark, Insert,
PA1 to PA3, and PF13 to PF24. Each item also shows the keys that send it, following your bindings, so the menu is
the place to look one up. Insert has a check mark while insert mode is on. The on-screen keypad
(**View > Keypad > Show the Keypad**) offers all of those as buttons, plus PF1 to PF12, Erase EOF and Erase Input.
```

Then regenerate the bundled copy (this is the only way `user-guide.html` changes; `UserGuideAssetTests` fails the suite until it is done):

```bash
LIZTERM_UPDATE_DOCS=1 dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~UserGuideAssetTests"
```

Expected: `Passed!` and `git status` shows `src/LizTerm.App/Assets/Docs/user-guide.html` modified.

- [ ] **Step 2: The changelog**

In `CHANGELOG.md`, under `## Unreleased`, replace the **Keyboard bindings** bullet with:

```markdown
- **Keyboard bindings.** **Preferences > Keyboard** lists every 3270 key with the keys that send it: add a key by
  pressing it, remove one with its **×**, move one to another row, or reset to the defaults. LizTerm refuses the keys
  it needs itself, such as Copy and Paste or a plain letter, and says why. Your changes are kept in `keymap.json`
  beside `settings.json`, and the on-screen keypad's tooltips follow them. So does the **Keys** menu, which now
  shows each key's keystrokes beside its name
  ([#18](https://github.com/coffeemuse/LizTerm/issues/18), [#23](https://github.com/coffeemuse/LizTerm/issues/23)).
```

- [ ] **Step 3: The notes for the next Claude**

In `src/LizTerm.App/CLAUDE.md`:

a) In the "Keyboard" section, the bullet that contains "`SessionWindow.AttachKeymap` composes it for the screen and the keypad on every change." Change that sentence to: "`SessionWindow.AttachKeymap` composes it for the screen, the keypad and the Keys menu's headers on every change (`ApplyKeymap`)."

b) In the "Menus" section, under "### Gestures", after the paragraph that begins "**No window menu item outside Edit carries a `Gesture`**", add a new paragraph:

```markdown
The Keys items show their keystrokes in **header text**, `PA2  Alt+2 or Ctrl+Home` (#23): `Menus/KeysMenuHints.Header`
joins the declared name and `KeymapHints.Describe`'s line, and `SessionWindow.ApplyKeymap` writes it onto the native
item and the classic item together, from the rows `CaptureKeysRows` paired at construction, on every keymap change.
Never a `Gesture` (a key equivalent that steals the keystroke from the screen), and never a classic `InputGesture`
either: the two menus would then differ, and the parity walk would fail, correctly. A null format is the platform's
wording, the keypad tooltips' own, so on macOS the hint is glyphs. Tests find a Keys item by its `CommandParameter`,
never by header, because the header follows the keymap.
```

In `tests/CLAUDE.md`, after the bullet that begins "Drive native menu items through `((INativeMenuItemExporterEventsImplBridge)item).RaiseClicked()`" (near line 107), add:

```markdown
- A Keys menu item is found by the key it sends (`NativeMenuTests.KeysItem(window, TerminalKey.Insert)`,
  `SessionWindowKeymapTests.KeysItems`), never by header: the header is `Insert  Insert or Ctrl+I` in the platform's
  wording and follows the keymap. Compute a header expectation with `KeymapHints.Describe(screen.Keymap, key)` and a
  null format, as the window does; only Dup and Field Mark read a bare name under the defaults.
```

- [ ] **Step 4: The gates**

```bash
dotnet build LizTerm.slnx --no-incremental 2>&1 | grep -c " warning "
```

Expected: `0`.

```bash
dotnet test LizTerm.slnx
```

Expected: every project `Failed: 0` (the live-host and engine tests skip themselves without a built engine).

- [ ] **Step 5: Commit**

```bash
git add docs/user-guide.md src/LizTerm.App/Assets/Docs/user-guide.html CHANGELOG.md src/LizTerm.App/CLAUDE.md tests/CLAUDE.md
git commit -m "Docs: the Keys menu shows each key's keystrokes; changelog and notes for #18 and #23

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

- [ ] **Step 6: The PR**

Push the branch and open the PR against `main` with `gh pr create`. The description leads with what the menu now reads and why it is header text (link issue #23's trap), lists the three deliberate differences from "Where this plan differs from the spec" for Robert to accept or reverse, carries `Closes #18` and `Closes #23` on their own lines, lists the hands-on checks below with what was and was not observed, and ends with `🤖 Generated with [Claude Code](https://claude.com/claude-code)`. Review fixes made after opening go in a PR **comment**, never into the description.

Hands-on checks (headless tests cannot see these; say in the PR which were done):

1. macOS, native menu: open Keys; each item reads its name, two spaces, then glyphs (`PA1  ⌥1`). Rebind PA1 in Preferences > Keyboard while the session window is open; reopen Keys and the item follows without reopening the window. This is the one check that matters most: it proves Avalonia's macOS exporter pushes a `Header` change to the live `NSMenuItem`. If it does not, the fix is to force a refresh the way `RebuildSessionRows` does for the Window menu (remove and re-add the same items to the same `NativeMenu`, never `Clear()`), not to give up on header text.
2. macOS, `LIZTERM_MENU=classic`, then Preferences > Menu bar > In the system menu bar on the open window: the refilled native Keys items carry the hints (they were written while stashed).
3. Windows or Linux: the in-window Keys menu reads `PA1  Alt+1`; Dup and Field Mark read their bare names; after Reset to defaults every item is back.
4. The two owed passes from PR 2's description (macOS Cmd chords in an armed slot, the armed border, the five-tab fit, the wrapped Escape text in a 180 px slot) and the spec's §7.3 live checks, if Robert wants them folded into this PR's review rather than done separately. Say which were done.

---

## Self-review

**Spec coverage.** §6.2: header text on both menus (Task 2, `ApplyKeymap`); name, two spaces, `KeymapHints.Describe` (Task 1); a key with no chord keeps its bare name (Task 1's second test, Task 2's PA3 test, Dup and Field Mark on the window); Insert's check mark unchanged (the three Insert tests still pass, only their lookup changed); rebuilt on the same `Changed` (`ApplyKeymap` is what `OnKeymapChanged` calls). §6.3: the changelog bullet (Task 3). §7.2's "Keys menu header" (Task 2's `A_rebind_reaches_the_Keys_menu_headers_on_both_menus`) and "parity guard, now with hints" (the existing walk, run in Task 2 step 5). Closing #18 and #23 (Task 3 step 6).

**Placeholders.** None: every code step carries its code, every doc step its words.

**Type consistency.** `KeysMenuHints.Header(string, IEnumerable<KeyChord>, IFormatProvider?)` is called in Task 2 with `(name, chords[key])`, where `chords` is an `ILookup<TerminalKey, KeyChord>` and `chords[key]` is `IEnumerable<KeyChord>` (empty for a key with none, which is what `Describe` turns into null). `_keysRows` is a list of `(NativeMenuItem, MenuItem, string, TerminalKey)` in both the field and the `foreach` deconstruction. `KeysMenuItem` is the classic `MenuItem` the `x:Name` declares. `MenuLookup` is `internal` and the test project already uses it, so `InternalsVisibleTo` is in place. `KeysItem` in `NativeMenuTests` returns a `NativeMenuItem`; `KeysItems` in `SessionWindowKeymapTests` returns the pair.
