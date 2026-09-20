# Keys Menu Shortcuts Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Each Keys menu item shows one real shortcut, grey and right-aligned like Edit's, on the native macOS menu and the classic menu, without the shortcut stealing the keystroke from the screen.

**Architecture:** A macOS-only helper adds a `performKeyEquivalent:` override to Avalonia's `AvnMenu` class once at startup that declines every key-down without ⌘ and hands the rest to the inherited implementation. `KeymapHints.MenuChord` picks one chord per key from the keymap in force, and `SessionWindow.ApplyKeymap` writes it as the native item's `Gesture` (only when the override is installed) and the classic item's display-only `InputGesture`. Header-text hints go.

**Tech Stack:** .NET 10, Avalonia 12.1.2, xunit.v3 (VSTest mode, `--filter "FullyQualifiedName~..."`), `DllImport` on `libobjc` in the style of `src/LizTerm.App/Bell/SystemBellRinger.cs`.

**Spec:** `docs/superpowers/specs/2026-09-19-keys-menu-shortcuts-design.md`

## Global Constraints

- Licence header on every new `.cs`: `// This file is part of LizTerm.` / `// Copyright 2026 by CoffeeMuse` / `// SPDX-License-Identifier: BSD-3-Clause` (`RepositoryHeadersTests` fails the suite otherwise).
- Dependency rule: everything here is in `LizTerm.App` and `LizTerm.App.Tests`; Core is untouched.
- No `unsafe` in the App project: `DllImport`, not `LibraryImport`; function pointers via `Marshal.GetFunctionPointerForDelegate` / `GetDelegateForFunctionPointer`.
- Zero warnings: before the PR, `dotnet build LizTerm.slnx --no-incremental 2>&1 | grep -c " warning "` must print `0`.
- Commit trailer on every commit: `Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>`.
- Work in this worktree on branch `claude/issue-23-ui-ux-review-2c43ac`. The throwaway spike (`src/LizTerm.App/Spike/`, two guarded edits in `SessionWindow.axaml.cs`) is uncommitted and is removed in Task 1.
- Modifier order everywhere (`KeymapHints.Ordered`): unmodified, Shift, Alt, Control, then combinations; function keys ahead of other keys within a group; taps last.
- Platform-lacking keys dropped from the menu chord on macOS: `Key.Pause` and `Key.Insert`. Taps never qualify.
- The invariant the override relies on: no native menu item outside Keys carries a gesture without the platform's command modifier.

---

### Task 1: Remove the spike and rework `KeymapHints`

**Files:**
- Delete: `src/LizTerm.App/Spike/KeysMenuSpike.cs`
- Modify: `src/LizTerm.App/Views/SessionWindow.axaml.cs` (revert the two spike hooks; the ApplyKeymap body is rewritten in Task 3)
- Modify: `src/LizTerm.App/Keyboard/KeymapHints.cs`
- Test: `tests/LizTerm.App.Tests/Keyboard/KeymapHintsTests.cs`

**Interfaces:**
- Consumes: `KeyChord(Key Key, KeyModifiers Modifiers = None, bool Tap = false)` (`Keyboard/KeyChord.cs`); `Keymap.Keys` (pairs of chord to `TerminalKey`).
- Produces: `KeymapHints.MenuChord(IEnumerable<KeyChord> chords, bool isMacOS) : KeyChord?`; `KeymapHints.Ordered` with the new modifier order; `KeymapHints.Label` removed.

- [ ] **Step 1: Remove the spike**

```bash
cd /Users/robert/ClaudeSandbox/LizTerm/.claude/worktrees/pr-146-code-review-70afb7
rm -r src/LizTerm.App/Spike
git checkout -- src/LizTerm.App/Views/SessionWindow.axaml.cs
git status --short   # expected: clean apart from this plan file if not yet committed
dotnet build src/LizTerm.App 2>&1 | grep -E "error|Error\(s\)"
```
Expected: `0 Error(s)`.

- [ ] **Step 2: Write the failing tests**

In `tests/LizTerm.App.Tests/Keyboard/KeymapHintsTests.cs`:

Change the PF13 line of `Several_chords_are_ordered_and_joined` and its doc comment:
```csharp
    /// <summary>Unmodified first, then by modifier (Shift before Alt before Ctrl), function keys ahead of other
    /// keys within a group, taps last; two join with "or", three with a comma and "or".</summary>
    [Theory]
    [InlineData(TerminalKey.PF13, "Shift+F1 or Ctrl+F1")]
```
(the other `InlineData` lines stay as they are).

Change `Ordered_puts_unmodified_first_then_modifiers_then_taps` to pin Shift ahead of Alt ahead of Control:
```csharp
    [Fact]
    public void Ordered_puts_unmodified_first_then_shift_alt_control_then_taps()
    {
        var chords = new[]
        {
            KeyChord.TapOf(Key.LeftCtrl),
            new KeyChord(Key.Home, KeyModifiers.Control),
            new KeyChord(Key.D2, KeyModifiers.Alt),
            new KeyChord(Key.F1, KeyModifiers.Shift),
            new KeyChord(Key.F7),
            new KeyChord(Key.PageUp),
        };

        Assert.Equal(
            [new KeyChord(Key.F7), new KeyChord(Key.PageUp), new KeyChord(Key.F1, KeyModifiers.Shift),
             new KeyChord(Key.D2, KeyModifiers.Alt), new KeyChord(Key.Home, KeyModifiers.Control), KeyChord.TapOf(Key.LeftCtrl)],
            KeymapHints.Ordered(chords));
    }
```

Delete the two `Label` tests (`A_label_is_the_name_two_spaces_and_the_tooltips_line`, `A_label_with_no_chord_is_the_bare_name`) and add, at the end of the class:
```csharp
    private static string? MenuChord(TerminalKey key, bool isMacOS, Keymap? map = null) =>
        KeymapHints.MenuChord(KeymapHints.ByKey(map ?? Map)[key], isMacOS) is { } chord ? KeymapHints.Describe(chord, Words) : null;

    /// <summary>Keys menu shortcuts spec §3.2: one chord per item, the first in Ordered's order that is not a tap
    /// and whose key the platform's keyboard has. Both platforms are pinned from one machine: the platform is an
    /// argument.</summary>
    [Theory]
    [InlineData(TerminalKey.PF13, false, "Shift+F1")]
    [InlineData(TerminalKey.PF13, true, "Shift+F1")]
    [InlineData(TerminalKey.PF24, true, "Shift+F12")]
    [InlineData(TerminalKey.PA1, false, "Alt+1")]
    [InlineData(TerminalKey.PA2, true, "Alt+2")]
    [InlineData(TerminalKey.Reset, true, "Ctrl+R")]
    [InlineData(TerminalKey.Attn, true, "Escape")]
    [InlineData(TerminalKey.SysReq, true, "Shift+Escape")]
    [InlineData(TerminalKey.Clear, false, "Pause")]
    [InlineData(TerminalKey.Clear, true, "Ctrl+Escape")]
    [InlineData(TerminalKey.Insert, false, "Insert")]
    [InlineData(TerminalKey.Insert, true, "Ctrl+I")]
    public void The_menu_chord_is_the_first_the_platform_can_press(TerminalKey key, bool isMacOS, string expected)
    {
        Assert.Equal(expected, MenuChord(key, isMacOS));
    }

    [Theory]
    [InlineData(TerminalKey.Dup)]
    [InlineData(TerminalKey.FieldMark)]
    public void A_key_nothing_maps_has_no_menu_chord(TerminalKey key)
    {
        Assert.Null(MenuChord(key, isMacOS: false));
        Assert.Null(MenuChord(key, isMacOS: true));
    }

    [Fact]
    public void A_key_only_a_tap_sends_has_no_menu_chord()
    {
        Assert.Null(KeymapHints.MenuChord([KeyChord.TapOf(Key.LeftCtrl)], isMacOS: false));
    }

    /// <summary>A rebind wins when it sorts first: F9 is unmodified, so it beats Alt+1.</summary>
    [Fact]
    public void A_remapped_key_changes_the_menu_chord()
    {
        var remapped = Map.With([KeyValuePair.Create(new KeyChord(Key.F9), TerminalKey.PA1)], []);

        Assert.Equal("F9", MenuChord(TerminalKey.PA1, isMacOS: false, remapped));
    }

    /// <summary>The Dictionary order must not leak into which chord the menu shows.</summary>
    [Fact]
    public void The_same_table_in_reverse_order_gives_the_same_menu_chord()
    {
        var reversed = new Keymap(Map.Keys.Reverse(), Map.Text);

        Assert.Equal(MenuChord(TerminalKey.PF13, true), MenuChord(TerminalKey.PF13, true, reversed));
        Assert.Equal(MenuChord(TerminalKey.Clear, false), MenuChord(TerminalKey.Clear, false, reversed));
    }
```

- [ ] **Step 3: Run the tests to verify they fail**

```bash
dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~KeymapHintsTests" 2>&1 | tail -5
```
Expected: build error `'KeymapHints' does not contain a definition for 'MenuChord'` (a compile failure is the failing state here).

- [ ] **Step 4: Implement**

In `src/LizTerm.App/Keyboard/KeymapHints.cs`:

Replace the class doc comment's first sentence so it no longer names the Keys menu as a `Label` reader:
```csharp
/// <summary>The keyboard equivalents of a key as one line of text, for the keypad's tooltips (keypad spec §5) and
/// the Keyboard tab's chips, and as one chord for the Keys menu's shortcut (Keys menu shortcuts spec §3.2). Pure:
/// the keymap, the platform and the format are arguments, so a remap (#18) changes the answer and a test can pin
/// the words.</summary>
```

Delete the `Label` method and its doc comment entirely.

Replace `Ordered` and its doc comment, and add `MenuChord` and `ModifierRank`:
```csharp
    /// <summary>The order every list of chords is shown in, tooltips, the Keyboard tab and the Keys menu alike:
    /// unmodified chords first, then Shift, Alt, Control, then combinations; function keys ahead of other keys
    /// within a group; taps last. Shift+F1 ahead of Ctrl+F1 is the order every 3270 user is taught PF13 in.</summary>
    public static IEnumerable<KeyChord> Ordered(IEnumerable<KeyChord> chords) => chords
        .OrderBy(chord => chord.Tap ? 1 : 0)
        .ThenBy(chord => ModifierRank(chord.Modifiers))
        .ThenBy(chord => IsFunctionKey(chord.Key) ? 0 : 1)
        .ThenBy(chord => (int)chord.Key);

    /// <summary>The one chord a Keys menu item shows (Keys menu shortcuts spec §3.2): the first in Ordered's order
    /// that is not a tap and whose key the platform's keyboard has. Apple keyboards have no Pause and no Insert.
    /// Null when nothing qualifies, and the item shows no shortcut.</summary>
    public static KeyChord? MenuChord(IEnumerable<KeyChord> chords, bool isMacOS)
    {
        foreach (var chord in Ordered(chords.Where(chord => !chord.Tap && !(isMacOS && chord.Key is Key.Pause or Key.Insert))))
            return chord;
        return null;
    }

    private static int ModifierRank(KeyModifiers modifiers) => modifiers switch
    {
        KeyModifiers.None => 0,
        KeyModifiers.Shift => 1,
        KeyModifiers.Alt => 2,
        KeyModifiers.Control => 3,
        _ => 4 + (int)modifiers,
    };
```
Note the pattern `chord.Key is Key.Pause or Key.Insert` binds as `(Key.Pause or Key.Insert)`; that is the intent.

- [ ] **Step 5: Run the tests to verify they pass**

```bash
dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~KeymapHintsTests" 2>&1 | tail -3
```
Expected: `Passed!` with no failures. Then the whole App suite, because `KeymapRow` chips and the keypad tooltips share `Ordered`:
```bash
dotnet test tests/LizTerm.App.Tests 2>&1 | tail -3
```
Expected: `Passed!`. If `KeymapEditorViewModelTests` fails on a chip order, the expectations there (`["Alt+2", "Ctrl+Home"]`, `["Alt+F9", "Alt+1"]`) still hold under the new order, so a failure means `ModifierRank` is wrong, not the test. `SessionWindowKeymapTests` still passes at this point because `ApplyKeymap` is untouched until Task 3; if it fails, `git checkout` in Step 1 did not restore the file.

- [ ] **Step 6: Commit**

```bash
git add -A src/LizTerm.App/Spike src/LizTerm.App/Views/SessionWindow.axaml.cs src/LizTerm.App/Keyboard/KeymapHints.cs tests/LizTerm.App.Tests/Keyboard/KeymapHintsTests.cs
git commit -m "$(cat <<'EOF'
App: KeymapHints.MenuChord picks the one chord a Keys item shows, Shift ahead of Alt and Ctrl everywhere; the spike is gone (#23)

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
EOF
)"
```

---

### Task 2: `MacMenuKeyEquivalents`, the override

**Files:**
- Create: `src/LizTerm.App/Menus/MacMenuKeyEquivalents.cs`
- Modify: `src/LizTerm.App/App.axaml.cs:72-91` (call `Install` in `OnFrameworkInitializationCompleted`)
- Test: `tests/LizTerm.App.Tests/Menus/MacMenuKeyEquivalentsTests.cs` (new)

**Interfaces:**
- Produces: `internal static class MacMenuKeyEquivalents` with `static bool Installed { get; }`, `static bool Install(bool isMacOS)`, `internal static bool Declines(ulong modifierFlags)`, `internal const ulong CommandFlag`.

Spec clarification recorded here and in the spec (Task 4): the override is installed whenever the process runs on macOS, not only under the native menu style. The style is per window and can change to Native at run time through Preferences; an override that was skipped at startup would then leave the Keys gestures live. Under InWindow the exported menu is empty, so the override has nothing to decline and costs nothing.

- [ ] **Step 1: Write the failing tests**

Create `tests/LizTerm.App.Tests/Menus/MacMenuKeyEquivalentsTests.cs`:
```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.App.Menus;

namespace LizTerm.App.Tests.Menus;

/// <summary>The pure half of the override (Keys menu shortcuts spec §3.1). The AppKit half cannot run headless:
/// libAvaloniaNative is never loaded here, so Install finds no AvnMenu class and answers false without touching
/// anything, which is also what it must do under a future Avalonia that renames the class.</summary>
public class MacMenuKeyEquivalentsTests
{
    private const ulong Shift = 1UL << 17, Control = 1UL << 18, Option = 1UL << 19, Function = 1UL << 23;

    /// <summary>The rule: without ⌘ it is never a menu key equivalent in LizTerm, so it falls through to the window.</summary>
    [Theory]
    [InlineData(0UL)]
    [InlineData(Shift)]
    [InlineData(Control)]
    [InlineData(Option)]
    [InlineData(Shift | Function | 0x108UL)]
    public void A_chord_without_command_is_declined(ulong flags)
    {
        Assert.True(MacMenuKeyEquivalents.Declines(flags));
    }

    [Theory]
    [InlineData(MacMenuKeyEquivalents.CommandFlag)]
    [InlineData(MacMenuKeyEquivalents.CommandFlag | 0x108UL)]
    [InlineData(MacMenuKeyEquivalents.CommandFlag | Shift)]
    public void A_chord_with_command_goes_to_the_original(ulong flags)
    {
        Assert.False(MacMenuKeyEquivalents.Declines(flags));
    }

    [Fact]
    public void Off_macOS_nothing_is_installed()
    {
        Assert.False(MacMenuKeyEquivalents.Install(isMacOS: false));
        Assert.False(MacMenuKeyEquivalents.Installed);
    }

    /// <summary>On a Mac test runner AppKit's menu class is still absent (headless), so the answer is a quiet
    /// false; on the others the call must not even reach libobjc, which does not exist.</summary>
    [Fact]
    public void Without_the_menu_class_install_answers_false_and_does_not_throw()
    {
        if (!OperatingSystem.IsMacOS()) return;
        Assert.False(MacMenuKeyEquivalents.Install(isMacOS: true));
        Assert.False(MacMenuKeyEquivalents.Installed);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~MacMenuKeyEquivalentsTests" 2>&1 | tail -5
```
Expected: compile error, `MacMenuKeyEquivalents` not found.

- [ ] **Step 3: Implement the helper**

Create `src/LizTerm.App/Menus/MacMenuKeyEquivalents.cs`:
```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Runtime.InteropServices;

namespace LizTerm.App.Menus;

/// <summary>Keeps a native menu key equivalent from stealing a 3270 keystroke (Keys menu shortcuts spec §3.1).
/// On macOS a NativeMenuItem gesture is a real AppKit key equivalent, and NSApplication.sendEvent: offers every
/// key-down to the main menu's performKeyEquivalent: before the key window's responder chain, so a Keys item
/// with Gesture Shift+F1 would send PF13 through the menu and TerminalScreen would never see the key
/// (native menus spec §2.1, measured again on 2026-09-19). Install adds a performKeyEquivalent: to Avalonia's
/// AvnMenu class, the class of every main menu it builds, that answers NO for a key-down without ⌘ and hands the
/// rest to NSMenu's own implementation. So Edit's ⌘C and Window's ⌘M keep working and ⇧F1 falls through to the
/// screen, while AppKit still draws the shortcut column. The rule holds because no native item outside Keys
/// carries a ⌘-less gesture; NativeMenuTests pins that.
///
/// Installed is what SessionWindow.ApplyKeymap checks before giving a Keys item a gesture: a process where the
/// class or the method was not found gets a Keys menu without shortcuts, never one that eats keys. The imports
/// are DllImport rather than LibraryImport for the reason SystemBellRinger gives.</summary>
internal static class MacMenuKeyEquivalents
{
    private const string LibObjc = "/usr/lib/libobjc.A.dylib";
    private const string MenuClass = "AvnMenu";
    private const string PerformSelector = "performKeyEquivalent:";

    /// <summary>NSEventModifierFlagCommand.</summary>
    internal const ulong CommandFlag = 1UL << 20;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate byte PerformKeyEquivalent(IntPtr self, IntPtr selector, IntPtr theEvent);

    // Both kept for the life of the process: AppKit holds the replacement's pointer, and the original is the
    // inherited NSMenu implementation the replacement defers to.
    private static readonly PerformKeyEquivalent Replacement = Perform;
    private static PerformKeyEquivalent? _original;
    private static IntPtr _modifierFlags;
    private static readonly object Gate = new();

    public static bool Installed { get; private set; }

    /// <summary>Adds the override once. False off macOS, when libobjc or the class or the method cannot be found,
    /// or when the class already defines the selector itself (an Avalonia that started overriding it would need
    /// this code revisited, not silently wrapped).</summary>
    public static bool Install(bool isMacOS)
    {
        if (!isMacOS) return false;
        lock (Gate)
        {
            if (Installed) return true;
            try
            {
                var cls = objc_getClass(MenuClass);
                if (cls == IntPtr.Zero) return false;
                var selector = sel_registerName(PerformSelector);
                var inherited = class_getInstanceMethod(cls, selector);
                if (inherited == IntPtr.Zero) return false;
                var originalImp = method_getImplementation(inherited);
                if (originalImp == IntPtr.Zero) return false;
                _original = Marshal.GetDelegateForFunctionPointer<PerformKeyEquivalent>(originalImp);
                _modifierFlags = sel_registerName("modifierFlags");
                // BOOL is 'B' on arm64 and 'c' on x86_64; the rest is self, _cmd and the event.
                var encoding = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "B@:@" : "c@:@";
                if (!class_addMethod(cls, selector, Marshal.GetFunctionPointerForDelegate(Replacement), encoding))
                    return false;
                Installed = true;
                return true;
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
            {
                return false;
            }
        }
    }

    /// <summary>The rule, on its own so a test can pin it: a key-down without ⌘ is never a menu key equivalent.</summary>
    internal static bool Declines(ulong modifierFlags) => (modifierFlags & CommandFlag) == 0;

    private static byte Perform(IntPtr self, IntPtr selector, IntPtr theEvent)
    {
        var flags = theEvent == IntPtr.Zero ? 0UL : objc_msgSend_ulong(theEvent, _modifierFlags);
        if (Declines(flags)) return 0;
        return _original!(self, selector, theEvent);
    }

    [DllImport(LibObjc)] private static extern IntPtr objc_getClass(string name);
    [DllImport(LibObjc)] private static extern IntPtr sel_registerName(string name);
    [DllImport(LibObjc)] private static extern IntPtr class_getInstanceMethod(IntPtr cls, IntPtr selector);
    [DllImport(LibObjc)] private static extern IntPtr method_getImplementation(IntPtr method);
    [DllImport(LibObjc)] [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool class_addMethod(IntPtr cls, IntPtr selector, IntPtr imp, string types);
    [DllImport(LibObjc, EntryPoint = "objc_msgSend")] private static extern ulong objc_msgSend_ulong(IntPtr self, IntPtr selector);
}
```

`class_addMethod` on `AvnMenu` adds an override on that class only, leaving `NSMenu` itself and every other subclass as they were; `class_getInstanceMethod` walked up to `NSMenu` to find the implementation the override defers to. That is the class-level form of what the spike did per object.

- [ ] **Step 4: Call it at startup**

In `src/LizTerm.App/App.axaml.cs`, inside `OnFrameworkInitializationCompleted`, directly after the `DockMenu.Attach(...)` line:
```csharp
            // Before any session window exists (#23): every Keys item's shortcut is a real key equivalent on macOS,
            // and this is what keeps AppKit from dispatching it ahead of the screen. SessionWindow.ApplyKeymap gives
            // the items no gesture unless Installed says this succeeded.
            MacMenuKeyEquivalents.Install(OperatingSystem.IsMacOS());
```
`LizTerm.App.Menus` is already imported there.

- [ ] **Step 5: Run the tests to verify they pass**

```bash
dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~MacMenuKeyEquivalentsTests" 2>&1 | tail -3
```
Expected: `Passed!`.

- [ ] **Step 6: Commit**

```bash
git add src/LizTerm.App/Menus/MacMenuKeyEquivalents.cs src/LizTerm.App/App.axaml.cs tests/LizTerm.App.Tests/Menus/MacMenuKeyEquivalentsTests.cs
git commit -m "$(cat <<'EOF'
App: MacMenuKeyEquivalents declines every ⌘-less key equivalent on the main menu, so a Keys gesture can be drawn without being dispatched (#23)

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
EOF
)"
```

---

### Task 3: The Keys items carry the chord as a gesture, on both menus

**Files:**
- Modify: `src/LizTerm.App/Views/SessionWindow.axaml.cs` (`_keysRows` at ~151, `ApplyKeymap` at ~673, `CaptureKeysRows` at ~768, `KeysRow` at ~796)
- Modify: `src/LizTerm.App/Views/SessionWindow.axaml:242` (comment)
- Test: `tests/LizTerm.App.Tests/Views/SessionWindowKeymapTests.cs`
- Test: `tests/LizTerm.App.Tests/Views/NativeMenuTests.cs` (`Only_edit_and_two_window_items_carry_gestures` at ~858, `KeysItem` doc at ~101)

**Interfaces:**
- Consumes: `KeymapHints.MenuChord(chords, isMacOS)` (Task 1); `MacMenuKeyEquivalents.Installed` (Task 2); the window's existing `_isMacOS` field and `KeysRow(TerminalKey)` seam.
- Produces: `internal bool SessionWindow.NativeGesturesAllowed { get; set; }`, defaulting to `MacMenuKeyEquivalents.Installed` at construction, read by `ApplyKeymap`.

- [ ] **Step 1: Write the failing tests**

In `tests/LizTerm.App.Tests/Views/SessionWindowKeymapTests.cs`, change the `Show` helper to take the flag and set it before the data context (object initializers run in textual order):
```csharp
    private static (SessionWindow Window, FakeEmulatorSession Session, TerminalScreen Screen) Show(
        bool destructiveBackspace, KeymapViewModel? keymap, bool isMacOS = false, bool nativeGestures = false)
    {
        var session = new FakeEmulatorSession
        {
            Profile = new SessionProfile { Name = "TSO", Host = "tk5.local", Port = 3270, DestructiveBackspace = destructiveBackspace },
        };
        var vm = new SessionViewModel(session, action => action(), new FakeTextClipboard());
        var window = new SessionWindow(MenuStyle.InWindow, isMacOS) { NativeGesturesAllowed = nativeGestures, DataContext = vm };
        if (keymap is not null) window.AttachKeymap(keymap);
        window.Show();
        var screen = window.FindControl<TerminalScreen>("Screen")!;
        screen.Focus();
        return (window, session, screen);
    }
```

Replace the three header tests (`A_rebind_reaches_the_Keys_menu_headers_on_both_menus`,
`A_rebind_made_under_InWindow_is_on_the_exported_Keys_menu_after_a_switch_to_Native`,
`A_key_left_with_no_chord_keeps_its_bare_name_on_the_Keys_menu`) with:
```csharp
    private static readonly KeyGesture Alt1 = new(Key.D1, KeyModifiers.Alt);

    /// <summary>Keys menu shortcuts spec §3.3: the chord is the native item's Gesture and the classic item's
    /// display-only InputGesture, the header is the bare name, and a rebind that sorts first replaces it on both.
    /// The window is InWindow, so the native top-level items are stashed out of the menu while this runs, and the
    /// Keys items still have to follow, because a later switch to Native puts those same objects back.</summary>
    [AvaloniaFact]
    public void A_rebind_reaches_the_Keys_menu_shortcut_on_both_menus()
    {
        var keymap = new KeymapViewModel();
        var (window, _, _) = Show(destructiveBackspace: true, keymap, nativeGestures: true);
        var (native, classic) = KeysItems(window, TerminalKey.PA1);
        Assert.Equal("PA1", native.Header);
        Assert.Equal("PA1", classic.Header);
        Assert.Equal(Alt1, native.Gesture);
        Assert.Equal(Alt1, classic.InputGesture);

        keymap.Bind(new KeyChord(Key.F9), new KeymapAction.SendKey(TerminalKey.PA1));

        Assert.Equal(new KeyGesture(Key.F9), native.Gesture);
        Assert.Equal(new KeyGesture(Key.F9), classic.InputGesture);
        Assert.Equal("PA1", native.Header);
    }

    /// <summary>Without the override installed a native gesture would be a key equivalent that eats the key, so the
    /// native item gets none; the classic InputGesture dispatches nothing and is always shown.</summary>
    [AvaloniaFact]
    public void Without_the_override_the_native_item_has_no_gesture_and_the_classic_one_still_does()
    {
        var (window, _, _) = Show(destructiveBackspace: true, new KeymapViewModel(), nativeGestures: false);
        var (native, classic) = KeysItems(window, TerminalKey.PA1);

        Assert.Null(native.Gesture);
        Assert.Equal(Alt1, classic.InputGesture);
    }

    /// <summary>The platform decides which chord: Apple keyboards have no Pause and no Insert (spec §3.2).</summary>
    [AvaloniaFact]
    public void The_chord_shown_follows_the_platform()
    {
        var (mac, _, _) = Show(destructiveBackspace: true, new KeymapViewModel(), isMacOS: true);
        var (other, _, _) = Show(destructiveBackspace: true, new KeymapViewModel(), isMacOS: false);

        Assert.Equal(new KeyGesture(Key.Escape, KeyModifiers.Control), KeysItems(mac, TerminalKey.Clear).Classic.InputGesture);
        Assert.Equal(new KeyGesture(Key.Pause), KeysItems(other, TerminalKey.Clear).Classic.InputGesture);
        Assert.Equal(new KeyGesture(Key.I, KeyModifiers.Control), KeysItems(mac, TerminalKey.Insert).Classic.InputGesture);
        Assert.Equal(new KeyGesture(Key.Insert), KeysItems(other, TerminalKey.Insert).Classic.InputGesture);
        Assert.Equal(new KeyGesture(Key.F1, KeyModifiers.Shift), KeysItems(mac, TerminalKey.PF13).Classic.InputGesture);
    }

    /// <summary>The other half of the InWindow guarantee: a rebind made while the native items were stashed is what
    /// the exported menu shows once a style change puts them back, read through the menu root as the platform
    /// would, not through the seam.</summary>
    [AvaloniaFact]
    public void A_rebind_made_under_InWindow_is_on_the_exported_Keys_menu_after_a_switch_to_Native()
    {
        var keymap = new KeymapViewModel();
        var (window, _, _) = Show(destructiveBackspace: true, keymap, isMacOS: true, nativeGestures: true);
        Assert.Empty(NativeMenu.GetMenu(window)!.Items);
        keymap.Bind(new KeyChord(Key.F9), new KeymapAction.SendKey(TerminalKey.PA1));

        ((SessionViewModel)window.DataContext!).Settings.MenuStyle = MenuStyle.Native;

        var exported = MenuLookup.Item(NativeMenu.GetMenu(window), "_Keys")!.Menu!.Items.OfType<NativeMenuItem>()
            .Single(item => Equals(item.CommandParameter, TerminalKey.PA1));
        Assert.Equal(new KeyGesture(Key.F9), exported.Gesture);
        Assert.Equal("PA1", exported.Header);
    }

    /// <summary>A key the user has taken every chord away from shows no shortcut, on both menus, and gets it back
    /// on Reset.</summary>
    [AvaloniaFact]
    public void A_key_left_with_no_chord_shows_no_shortcut_on_the_Keys_menu()
    {
        var keymap = new KeymapViewModel();
        var (window, _, _) = Show(destructiveBackspace: true, keymap, nativeGestures: true);
        var (native, classic) = KeysItems(window, TerminalKey.PA3);
        Assert.NotNull(native.Gesture);

        keymap.Unbind(new KeyChord(Key.D3, KeyModifiers.Alt));
        keymap.Unbind(new KeyChord(Key.PageUp, KeyModifiers.Control));

        Assert.Null(native.Gesture);
        Assert.Null(classic.InputGesture);
        Assert.Equal("PA3", native.Header);

        keymap.ResetToDefaults();

        Assert.Equal(new KeyGesture(Key.D3, KeyModifiers.Alt), native.Gesture);
        Assert.Equal(native.Gesture, classic.InputGesture);
    }
```
Update the `KeysItems` doc comment's first clause to "A Keys item by the key it sends, through the seam that reaches a native item stashed under InWindow".

In `tests/LizTerm.App.Tests/Views/NativeMenuTests.cs`, replace `Only_edit_and_two_window_items_carry_gestures`, `GestureExceptions` and `AssertNoGestures` with:
```csharp
    /// <summary>Gestures come from the platform hotkey table, not a hardcoded modifier, so macOS shows Cmd and
    /// the others Ctrl from the one table ShowPlatformGestures already reads for the classic menu. Outside Edit
    /// and Keys exactly two items carry one, both Cmd chords no 3270 keystroke uses (session switching spec §6):
    /// Window &gt; Switch Session... and, on macOS, Window &gt; Minimize.
    ///
    /// The Keys items are the exception the Keys menu shortcuts spec makes (§3.1): their gestures are drawn by
    /// AppKit and declined by MacMenuKeyEquivalents, which refuses every key-down without ⌘. That rule is only
    /// safe while every other native gesture carries the command modifier, which the second walk pins: a
    /// Ctrl+W on File &gt; Close would be declined silently, and this is where that shows up.</summary>
    [AvaloniaFact]
    public void Only_edit_two_window_items_and_the_Keys_items_carry_gestures()
    {
        var (window, _, _, _) = Show();
        window.NativeGesturesAllowed = true;
        window.AttachKeymap(new KeymapViewModel());
        var hotkeys = window.GetPlatformSettings()!.HotkeyConfiguration;

        Assert.Equal(hotkeys.Copy.FirstOrDefault(), Item(window, "_Edit", "_Copy").Gesture);
        Assert.Equal(hotkeys.Paste.FirstOrDefault(), Item(window, "_Edit", "_Paste").Gesture);
        Assert.Equal(hotkeys.SelectAll.FirstOrDefault(), Item(window, "_Edit", "Select _All").Gesture);
        Assert.Equal(new KeyGesture(Key.K, hotkeys.CommandModifiers), Item(window, "_Window", "_Switch Session...").Gesture);
        Assert.Equal(new KeyGesture(Key.M, KeyModifiers.Meta), Item(window, "_Window", "_Minimize").Gesture);
        Assert.Equal(new KeyGesture(Key.F1, KeyModifiers.Shift), KeysItem(window, TerminalKey.PF13).Gesture);
        Assert.Null(KeysItem(window, TerminalKey.Dup).Gesture);

        // A ⌘-less gesture anywhere but Keys is a 3270 client that cannot send that key, silently, with nothing
        // in the wire log: MacMenuKeyEquivalents declines it before the key window ever sees it. Walked
        // exhaustively rather than from a list of examples — a list only covers the items someone remembered to
        // add to it, and View > Crosshair is precisely the submenu one would have missed.
        foreach (var top in NativeMenu.GetMenu(window)!.Items.OfType<NativeMenuItem>().Where(i => i.Header is not ("_Edit" or "_Keys")))
            AssertNoGestures(top.Header!, top.Menu!);
        foreach (var top in NativeMenu.GetMenu(window)!.Items.OfType<NativeMenuItem>().Where(i => i.Header != "_Keys"))
            AssertEveryGestureCarriesCommand(top.Header!, top.Menu!, hotkeys.CommandModifiers);
    }

    private static readonly string[] GestureExceptions = ["_Window > _Switch Session...", "_Window > _Minimize"];

    private static void AssertNoGestures(string path, NativeMenu menu)
    {
        foreach (var item in menu.Items.OfType<NativeMenuItem>().Where(i => i is not NativeMenuItemSeparator))
        {
            var itemPath = $"{path} > {item.Header}";
            if (GestureExceptions.Contains(itemPath)) continue;
            Assert.True(item.Gesture is null,
                $"{itemPath} carries a gesture, which takes that key away from the terminal");
            if (item.Menu is { } submenu) AssertNoGestures(itemPath, submenu);
        }
    }

    private static void AssertEveryGestureCarriesCommand(string path, NativeMenu menu, KeyModifiers command)
    {
        foreach (var item in menu.Items.OfType<NativeMenuItem>().Where(i => i is not NativeMenuItemSeparator))
        {
            var itemPath = $"{path} > {item.Header}";
            if (item.Gesture is { } gesture)
                Assert.True(gesture.KeyModifiers.HasFlag(command) || gesture.KeyModifiers.HasFlag(KeyModifiers.Meta),
                    $"{itemPath} carries {gesture} without the command modifier, which MacMenuKeyEquivalents would decline");
            if (item.Menu is { } submenu) AssertEveryGestureCarriesCommand(itemPath, submenu, command);
        }
    }
```
The `KeymapViewModel` type needs `using LizTerm.App.ViewModels;`, which the file already has. Also change the `KeysItem` doc comment at ~101 to:
```csharp
    /// <summary>A Keys item by the key it sends (#23): found by CommandParameter so the test never depends on a
    /// header or a gesture.</summary>
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~SessionWindowKeymapTests|FullyQualifiedName~NativeMenuTests.Only_edit" 2>&1 | tail -5
```
Expected: compile error, `NativeGesturesAllowed` not found.

- [ ] **Step 3: Implement in `SessionWindow.axaml.cs`**

Replace the `_keysRows` field and its doc comment:
```csharp
    /// <summary>Every Keys item on both menus with the key it sends, paired in declaration order at construction
    /// (the parity test holds the two menus to the same order). ApplyKeymap writes each pair's shortcut from the
    /// keymap in force, so the menu says what the keyboard does (#23).</summary>
    private readonly List<(NativeMenuItem Native, MenuItem Classic, TerminalKey Key)> _keysRows = [];

    /// <summary>Whether a Keys item may carry a native Gesture: only when MacMenuKeyEquivalents has installed the
    /// override that keeps AppKit from dispatching it (Keys menu shortcuts spec §3.1). Read at construction so a
    /// test can say yes on a platform where the override never installs; the classic InputGesture is shown
    /// regardless, being display-only.</summary>
    internal bool NativeGesturesAllowed { get; set; } = MacMenuKeyEquivalents.Installed;
```

Replace `ApplyKeymap` and its doc comment:
```csharp
    /// <summary>The map in force for this window: the profile's Backspace choice under the user's keymap.json. Set
    /// on the screen and on the keypad, whose tooltips follow it (keypad spec §4.4), and shown as one shortcut on
    /// every Keys item on both menus (Keys menu shortcuts spec §3.3): the keymap is reversed once for the 22 items,
    /// as the keypad reverses it once for its 36 buttons. The native Gesture is a real key equivalent and is set
    /// only when the override that declines it is installed; the classic InputGesture dispatches nothing.</summary>
    private void ApplyKeymap()
    {
        var destructive = ViewModel?.Profile.DestructiveBackspace ?? true;
        var map = _keymap?.Compose(destructive) ?? DefaultKeymap.Create(destructive);
        Screen.Keymap = map;
        KeypadPanel.Keymap = map;
        var chords = KeymapHints.ByKey(map);
        foreach (var (native, classic, key) in _keysRows)
        {
            var gesture = KeymapHints.MenuChord(chords[key], _isMacOS) is { } chord
                ? new KeyGesture(chord.Key, chord.Modifiers)
                : null;
            native.Gesture = NativeGesturesAllowed ? gesture : null;
            classic.InputGesture = gesture;
        }
    }
```

In `CaptureKeysRows`, change the `_keysRows.Add` line to `_keysRows.Add((nativeItem, classicItem, key));` and its doc comment's last sentence to "...rather than writing one menu's shortcut onto the other's item."

Add `using LizTerm.App.Menus;` if the file lacks it (it already references `MenuStrategy`, so it has it).

In `src/LizTerm.App/Views/SessionWindow.axaml`, line 242, replace the comment with:
```xml
      <!-- Named so the code-behind can pair its items with the native Keys items and write both shortcuts (#23). -->
```

- [ ] **Step 4: Run the tests to verify they pass**

```bash
dotnet test tests/LizTerm.App.Tests 2>&1 | tail -3
```
Expected: `Passed!`. Watch specifically for `NativeMenuTests.The_native_menu_matches_the_classic_menu_item_for_item` (headers are bare on both sides, so it holds) and for any test that computed a header with `KeymapHints.Describe(...)` (none should remain; grep `"PA1  "` in `tests/` to be sure).

- [ ] **Step 5: Commit**

```bash
git add src/LizTerm.App/Views/SessionWindow.axaml.cs src/LizTerm.App/Views/SessionWindow.axaml tests/LizTerm.App.Tests/Views/SessionWindowKeymapTests.cs tests/LizTerm.App.Tests/Views/NativeMenuTests.cs
git commit -m "$(cat <<'EOF'
App: each Keys item shows one chord as its shortcut, a native gesture only under the override and a classic InputGesture always; headers are bare names (#23)

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
EOF
)"
```

---

### Task 4: Documentation

**Files:**
- Modify: `docs/user-guide.md:204-207` and `:211-212`
- Modify: `CHANGELOG.md:14-21` (the `## Unreleased` keyboard bindings entry)
- Modify: `src/LizTerm.App/CLAUDE.md` (the "Gestures" section at ~466-484; lines ~285 and ~359-361 that describe the order)
- Modify: `tests/CLAUDE.md:112-116`
- Modify: `docs/superpowers/specs/2026-09-19-keys-menu-shortcuts-design.md` §3.1 (the install condition)

**Interfaces:** none; text only. `UserGuideKeyboardTableTests` reads the guide's table, which does not change.

- [ ] **Step 1: User guide**

In `docs/user-guide.md`, replace the Keys paragraph (the one starting `The **Keys** menu sends every key`) with:
```markdown
The **Keys** menu sends every key the keyboard might not reach: Clear, Reset, Attn, SysReq, Dup, Field Mark, Insert,
PA1 to PA3, and PF13 to PF24. Each item shows one keystroke that sends it, following your bindings, so the menu is
the place to look one up; a key with several bindings shows the first, and the keypad's tooltip and
**Preferences > Keyboard** list them all. On a Mac the keystroke shown is one an Apple keyboard has, so Clear shows
Ctrl+Escape rather than Pause. Insert has a check mark while insert mode is on. The on-screen keypad
(**View > Keypad > Show the Keypad**) offers all of those as buttons, plus PF1 to PF12, Erase EOF and Erase Input.
```
The two sentences under "Changing a binding" ("...the keypad's tooltips and the **Keys** menu follow them.") stay.

- [ ] **Step 2: Changelog**

In `CHANGELOG.md`, in the `## Unreleased` keyboard bindings entry, replace the clause
`So does the **Keys** menu, which now shows each key's keystrokes beside its name,` with
`So does the **Keys** menu, which now shows each key's keystroke as a menu shortcut,`.

- [ ] **Step 3: App `CLAUDE.md`**

In `src/LizTerm.App/CLAUDE.md`, in the "Gestures" section, replace the paragraph starting `The Keys items show their keystrokes in **header text**` through the end of the following paragraph (`...answers with the pair the constructor captured.`) with:
```markdown
**The Keys items are the one place a native gesture is allowed** (#23, Keys menu shortcuts spec). Each item's
`Gesture` is `KeymapHints.MenuChord` over `KeymapHints.ByKey`, one chord per key from the keymap in force, written
by `SessionWindow.ApplyKeymap` on both the native item and the classic item (`InputGesture`, display-only) from the
rows `CaptureKeysRows` paired at construction, on every keymap change. Headers are the bare names. What makes the
native gesture safe is `Menus/MacMenuKeyEquivalents`: at startup on macOS it adds a `performKeyEquivalent:` to
Avalonia's `AvnMenu` class that answers NO for any key-down without ⌘ and defers to NSMenu for the rest, so AppKit
draws the shortcut column and still hands ⇧F1 to the screen. The rule holds only while every other native gesture
carries the command modifier, which `NativeMenuTests.Only_edit_two_window_items_and_the_Keys_items_carry_gestures`
walks. `ApplyKeymap` sets no native gesture unless `MacMenuKeyEquivalents.Installed`, read into
`SessionWindow.NativeGesturesAllowed` at construction, which is also the test seam. Measured on 2026-09-19: the
submenu delegate's `menuHasKeyEquivalent:` is consulted only for ⌘ chords and the submenu's own
`performKeyEquivalent:` never, which is why the override is on the main menu's class. A key pressed while the
Keys menu is open fires the item through the menu's tracking loop, as on every macOS menu. Tests find a Keys item by
the key it sends; under InWindow the whole top-level `_Keys` native item is stashed, so a `MenuLookup` from the menu
root cannot reach it, and `SessionWindow.KeysRow(TerminalKey)` is the internal seam that answers with the pair the
constructor captured.
```
Also update the "Gestures" section's opening bold sentence to read **No window menu item outside Edit and Keys carries a `Gesture`, apart from Window's two Cmd chords (below).**

At ~285, change `(so a PA1 row reads Alt+F9 before Alt+1 once both are bound)` to `(unmodified, then Shift, Alt, Control; so a PA1 row reads F9 before Alt+1 once both are bound, and Alt+F9 before Alt+1)`.

At ~359, change `chords ordered unmodified first, then by modifier, function keys ahead within a group, taps last` to `chords ordered unmodified first, then Shift, Alt, Control, function keys ahead within a group, taps last`.

- [ ] **Step 4: `tests/CLAUDE.md`**

Replace the bullet starting `- A Keys menu item is found by the key it sends, never by header` with:
```markdown
- A Keys menu item is found by the key it sends (`NativeMenuTests.KeysItem(window, TerminalKey.Insert)` under the
  Native style; `SessionWindow.KeysRow(key)` under InWindow, where the top-level `_Keys` native item is stashed out
  of the menu and a `MenuLookup` from the root finds nothing). Its header is the bare name; its shortcut is the
  native `Gesture` and the classic `InputGesture`, from `KeymapHints.MenuChord`, and the native one is set only
  when `SessionWindow.NativeGesturesAllowed` is true, which a test sets in the object initializer before the data
  context because the override behind it never installs headless.
```

- [ ] **Step 5: Spec clarification**

In `docs/superpowers/specs/2026-09-19-keys-menu-shortcuts-design.md` §3.1, replace `replaces \`performKeyEquivalent:\` on the \`AvnMenu\` class once, at startup, under the native menu strategy only, and keeps the original implementation` with `adds a \`performKeyEquivalent:\` to the \`AvnMenu\` class once, at startup, whenever the process runs on macOS, keeping NSMenu's inherited implementation to defer to. It is not gated on the menu style: the style is per window and can switch to Native at run time, and under InWindow the exported menu is empty, so the override has nothing to decline`.

- [ ] **Step 6: Check the guide test and commit**

```bash
dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~UserGuide" 2>&1 | tail -3
git add docs/user-guide.md CHANGELOG.md src/LizTerm.App/CLAUDE.md tests/CLAUDE.md docs/superpowers/specs/2026-09-19-keys-menu-shortcuts-design.md
git commit -m "$(cat <<'EOF'
Docs: the Keys menu shows one keystroke as a shortcut; menu notes describe the override and its invariant (#23)

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
EOF
)"
```
Expected: `Passed!` before the commit.

---

### Task 5: Zero warnings, full suite, live check, PR

**Files:** none new. The live check is Robert's, on macOS, with the merged behaviour of Tasks 1 to 4.

- [ ] **Step 1: Zero-warning build and full suite**

```bash
dotnet build LizTerm.slnx --no-incremental 2>&1 | grep -c " warning "
dotnet test LizTerm.slnx 2>&1 | tail -5
```
Expected: `0`, then every project `Passed!` (live-host tests skip themselves).

- [ ] **Step 2: Live check on macOS (Robert at the keyboard)**

Launch from the worktree with the Homebrew engine and Help > Wire Log on, so each key sent to the host is visible:
```bash
LIZTERM_B3270_PATH=/opt/homebrew/bin/b3270 src/LizTerm.App/bin/Debug/net10.0/LizTerm.App
```
Connected, Keys menu closed, screen focused: press ⇧F1, ⌃R, ⌥1, ⎋. Expected: the wire log shows PF(13), Reset, PA(1), Attn; nothing in the menu bar reacts. Then ⌘C with a selection and ⌘M: both work. Then open Keys once: every item but Dup and Field Mark shows a grey, right-aligned shortcut; Clear shows ⌃⎋, PF13 shows ⇧F1, Insert shows ⌃I. Open Preferences > Keyboard, start a capture on PA1 and press ⇧F2: the capture receives it. Record the outcome, with the wire-log lines, in the PR description.

- [ ] **Step 3: Push and open the PR**

```bash
git push -u origin claude/issue-23-ui-ux-review-2c43ac
gh pr create --title "Keys menu shortcuts as real shortcuts (#23)" --body "$(cat <<'EOF'
Replaces the header-text hints on the Keys menu (unreleased) with one shortcut per item, drawn by AppKit on the native macOS menu and shown display-only on the classic menu. `MacMenuKeyEquivalents` adds a `performKeyEquivalent:` to Avalonia's `AvnMenu` class that declines every ⌘-less key-down, so the shortcut is drawn but never dispatched ahead of the screen. `KeymapHints.MenuChord` picks the chord per platform; `Ordered` now lists Shift before Alt before Control everywhere.

Spec: `docs/superpowers/specs/2026-09-19-keys-menu-shortcuts-design.md` (with the spike's measurements). Plan: `docs/superpowers/plans/2026-09-19-keys-menu-shortcuts.md`.

Live check on macOS: <outcome and wire-log lines from Task 5 step 2>

Related: #157 (⌃⎋ Clear does not work on macOS; the menu shows it per the keymap and the fix makes the hint true).

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
```
Replace the `<outcome ...>` placeholder with the live check's result before running the command.

---

## Self-review

**Spec coverage.** §3.1 override: Task 2. §3.2 selection and the `Ordered` change: Task 1. §3.3 both menus, bare headers, `Installed` gating: Task 3. §3.4 tooltips and the tab unchanged: no task needed, and Task 1 Step 5 runs the suite that covers them. §4 tests: `MenuChord` per platform (Task 1), window tests including the not-installed case (Task 3), the parity walk unchanged and the ⌘ invariant guard (Task 3), headers on new files (Task 2 template), the live check (Task 5). §5 docs: Task 4, including the spec clarification the install condition needed. §6 out of scope: #157 stays open; the PR body names it.

**Placeholders.** The one intentional placeholder is the live-check outcome in the PR body, filled from Task 5 Step 2 before the command runs.

**Type consistency.** `KeymapHints.MenuChord(IEnumerable<KeyChord>, bool) : KeyChord?` is the name in Task 1's code, Task 1's tests, Task 3's `ApplyKeymap` and Task 4's docs. `MacMenuKeyEquivalents.Installed`, `Install(bool)`, `Declines(ulong)`, `CommandFlag` match between Task 2's code and tests and Task 3's window. `SessionWindow.NativeGesturesAllowed` is the same name in Task 3's window, both test files and Task 4's notes. `_keysRows` is a 3-tuple everywhere after Task 3, and `KeysRow` still returns `(Native, Classic)`.
