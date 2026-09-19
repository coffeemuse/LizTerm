# Editable keymap, PR 2: the Keyboard tab — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A **Keyboard** tab in Preferences where a user adds a key by pressing it, removes one with its ×, moves one between rows, and resets to the defaults, every change applying at once to every open session window and keypad tooltip.

**Architecture:** PR 1's `KeymapViewModel` stays the view-agnostic model. A new `KeymapEditorViewModel` (one `KeymapRow` per action, one `KeymapChip` per chord) turns it into rows and judges every captured chord with `KeymapPolicy.Check` before `Bind`. A small `ChordCaptureBox` button captures a chord or a Ctrl tap and reports it through a delegate, so the control knows nothing about the model. A `KeyboardTab` `UserControl` lays the rows out; `PreferencesWindow` gains it as a fifth tab and `App.ShowPreferences` passes the process's keymap in.

**Tech Stack:** .NET 10, Avalonia 12 (headless tests via `[AvaloniaFact]`, compiled bindings on by default), xunit.v3, CommunityToolkit.Mvvm (`ObservableObject`, `RelayCommand`).

**Spec:** `docs/superpowers/specs/2026-09-19-lizterm-editable-keymap-design.md` — this plan implements §5.3, §5.4, the guide half of §6.1 and its table test (delivery item 2 of §8). PR 1 (`docs/superpowers/plans/2026-09-19-editable-keymap-pr1-model-store-wiring.md`) is merged on `main` as cc7080f. PR 3 (Keys menu hints, changelog, closing #18 and #23) gets its own plan after this one merges.

## Global Constraints

- **Dependency rule.** Everything here is `LizTerm.App` (and its tests). `LizTerm.Core` is untouched. App code never names a backend.
- **Licence headers.** Every new `.cs` and `.axaml` file starts with the three-line header in its comment syntax: `// This file is part of LizTerm.` / `// Copyright 2026 by CoffeeMuse` / `// SPDX-License-Identifier: BSD-3-Clause`, then a blank line. In `.axaml`, an XML comment before the root element with the same three lines (copy the shape from `src/LizTerm.App/Views/PreferencesWindow.axaml`). `RepositoryHeadersTests` fails the suite otherwise.
- **Zero warnings.** CI builds with `-warnaserror`. Before calling the PR done: `dotnet build LizTerm.slnx --no-incremental 2>&1 | grep -c " warning "` must print `0`.
- **Tests.** xunit.v3 in VSTest mode; run one class with `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~<Class>"`. Control and window tests are `[AvaloniaFact]` and drive keys with `window.KeyPressQwerty(PhysicalKey, RawInputModifiers)` and `window.KeyReleaseQwerty(...)`. View-model tests are plain `[Fact]`.
- **Commits.** Small, one per task, end every commit message with the attribution line from the session's system reminder (`Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>` at the time of writing).
- **Colour never carries meaning alone (Robert has some colour blindness).** Everything the tab shows in colour is also a word or a shape: the armed slot reads "Press a key" and has a thicker border; a refusal is text; a chip has a × button.
- **Docs have one home.** When a change makes a documented sentence untrue, fix that sentence where it lives. `CHANGELOG.md` entries go under `## Unreleased`.
- **No em dashes in new prose** that ends up in the user guide, the changelog or the privacy doc; use a full stop or a comma.
- **Chord spelling, refusal table, sparse rule** are PR 1's and unchanged: `ChordSyntax`, `KeymapPolicy.Check(chord, hotkeys)`, `KeymapViewModel.Bind/Unbind/ResetToDefaults/ChordsFor/ActionOf`. The tab must call `KeymapPolicy.Check` before every `Bind`; that is what keeps a Meta chord out of the file.
- Run every command from the worktree root: `/Users/robert/ClaudeSandbox/LizTerm/.claude/worktrees/loving-driscoll-79c86e`.

## Where this plan deliberately differs from the spec, and why

Each is small and each is announced in the PR description for Robert to accept or reverse.

1. **The Add slot is armed by activating it, not by focusing it (spec §5.3).** The spec arms on focus. A slot that swallows every key while focused also swallows Tab and Shift+Tab, so a keyboard user who tabs into one can never tab out (or reach Reset to defaults or Done), which is a keyboard trap. Here the slot is an ordinary tab stop reading **Add**; clicking it, or pressing Enter or Space on it, arms it ("Press a key"); the next chord or tap is captured (Tab, Escape and Enter included); an accepted capture disarms it; a refusal leaves it armed showing the reason; focus loss or a second click disarms. Everything else in §5.3 stands. Spike-verified in Avalonia 12.1.2 headless: an armed `Button` subclass that handles `KeyDown` receives Enter, Escape, Tab, Shift+Tab, chords and Ctrl taps, the window's `IsDefault`/`IsCancel` Done button does not fire, and focus stays. Still open (a follow-up, not this PR): a keyboard-only user who armed a slot and changed their mind can leave only by binding something or clicking away; an Escape-cancel would take Escape away from binding.
2. **Two Backspace rows, not one (spec §5.3's list).** `TerminalKey` has two Backspace actions, `Erase` (destructive, the default) and `Backspace` (cursor left). One row cannot say which an Add binds, so there are two, titled "Backspace, erasing" and "Backspace, moving left", both carrying the spec's note (reworded to name both).
3. **"Moved from PA2" clears after three seconds, or on the next arm or focus loss (spec: "for a moment").** The slot owns the message and a `DispatcherTimer` clears it.
4. **A "Type ¬" row stays for the life of the tab once shown**, even after its last chord is removed, so the removal can be undone with Add. Reset to defaults still restores everything; adding brand-new text actions stays out of scope (spec §2).
5. **The guide table test compares chord sets, not `KeymapHints` strings (spec §6.1).** `KeymapHints.Describe` joins with "or" and orders by modifier value, while the guide is hand-ordered with an Oxford comma, so string equality would be noise. The stronger claim, "every default chord is in the row for its key and no row claims a chord the default lacks", is a set comparison.
6. **PR 2 rewrites the existing CHANGELOG line** ("Hand-edited for now") because this PR makes it untrue. PR 3 still adds the Keys menu line and closes the issues.

---

## File map

| File | Responsibility |
|---|---|
| Modify `src/LizTerm.App/Keyboard/KeymapHints.cs` | Public `Ordered(chords)` and `Describe(KeyChord)`, so a chip reads as a tooltip does |
| Modify `src/LizTerm.App/Keyboard/ChordSyntax.cs` | `IsModifierKey` becomes public (the slot needs it) |
| Create `src/LizTerm.App/Keyboard/CaptureResult.cs` | What a row answers a captured chord: accepted or not, and a message |
| Create `src/LizTerm.App/ViewModels/KeymapChip.cs` | One chord on a row: its text and its remove command |
| Create `src/LizTerm.App/ViewModels/KeymapRow.cs` | One action: title, note, chips, and `TryCapture` |
| Create `src/LizTerm.App/ViewModels/KeymapEditorViewModel.cs` | The row list, Reset, the notes and the save error |
| Create `src/LizTerm.App/Controls/ChordCaptureBox.cs` | The Add slot |
| Create `src/LizTerm.App/Views/KeyboardTab.axaml` and `.axaml.cs` | The tab's layout |
| Modify `src/LizTerm.App/Views/PreferencesWindow.axaml` and `.axaml.cs` | Fifth tab; constructor takes the `KeymapViewModel`; disposes the editor on close |
| Modify `src/LizTerm.App/App.axaml.cs` | `ShowPreferences` passes `Keymap` |
| Tests | `tests/LizTerm.App.Tests/Keyboard/KeymapHintsTests.cs` (edit), `ChordSyntaxTests.cs` (edit); `tests/LizTerm.App.Tests/ViewModels/KeymapEditorViewModelTests.cs`; `tests/LizTerm.App.Tests/Controls/ChordCaptureBoxTests.cs`; `tests/LizTerm.App.Tests/Views/KeyboardTabTests.cs`; `tests/LizTerm.App.Tests/Views/PreferencesWindowTests.cs` (edit); `tests/LizTerm.App.Tests/Documentation/UserGuideKeyboardTableTests.cs` |
| Docs | `docs/user-guide.md`, regenerated `src/LizTerm.App/Assets/Docs/user-guide.html`, `CHANGELOG.md`, `src/LizTerm.App/CLAUDE.md`, `tests/CLAUDE.md` |

---

### Task 1: Two small public seams in `KeymapHints` and `ChordSyntax`

**Files:**
- Modify: `src/LizTerm.App/Keyboard/KeymapHints.cs`
- Modify: `src/LizTerm.App/Keyboard/ChordSyntax.cs:70-71` (the private `IsModifierKey`)
- Test: `tests/LizTerm.App.Tests/Keyboard/KeymapHintsTests.cs`, `tests/LizTerm.App.Tests/Keyboard/ChordSyntaxTests.cs`

**Interfaces:**
- Produces: `public static IEnumerable<KeyChord> KeymapHints.Ordered(IEnumerable<KeyChord> chords)`; `public static string KeymapHints.Describe(KeyChord chord, IFormatProvider? format = null)`; `public static bool ChordSyntax.IsModifierKey(Key key)`.

- [ ] **Step 1: Write the failing tests**

Append to the class in `KeymapHintsTests.cs` (it already has `Words`, a `KeyGestureFormatInfo`):

```csharp
    [Fact]
    public void Ordered_puts_unmodified_first_then_modifiers_then_taps()
    {
        var chords = new[]
        {
            KeyChord.TapOf(Key.LeftCtrl),
            new KeyChord(Key.Home, KeyModifiers.Control),
            new KeyChord(Key.D2, KeyModifiers.Alt),
            new KeyChord(Key.F7),
            new KeyChord(Key.PageUp),
        };

        Assert.Equal(
            [new KeyChord(Key.F7), new KeyChord(Key.PageUp), new KeyChord(Key.D2, KeyModifiers.Alt),
             new KeyChord(Key.Home, KeyModifiers.Control), KeyChord.TapOf(Key.LeftCtrl)],
            KeymapHints.Ordered(chords));
    }

    [Fact]
    public void One_chord_reads_as_it_does_in_a_tooltip()
    {
        Assert.Equal("Alt+2", KeymapHints.Describe(new KeyChord(Key.D2, KeyModifiers.Alt), Words));
        Assert.Equal("a tap of Right Ctrl", KeymapHints.Describe(KeyChord.TapOf(Key.RightCtrl), Words));
    }
```

Append to `ChordSyntaxTests.cs` (inside the class; add `using Avalonia.Input;` if it is not there):

```csharp
    [Theory]
    [InlineData(Key.LeftCtrl, true)]
    [InlineData(Key.RightShift, true)]
    [InlineData(Key.LeftAlt, true)]
    [InlineData(Key.LWin, true)]
    [InlineData(Key.Home, false)]
    [InlineData(Key.A, false)]
    public void IsModifierKey_names_the_keys_that_are_only_modifiers(Key key, bool expected)
    {
        Assert.Equal(expected, ChordSyntax.IsModifierKey(key));
    }
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~KeymapHintsTests|FullyQualifiedName~ChordSyntaxTests"`
Expected: build error, `KeymapHints` has no `Ordered`, `Describe(KeyChord, ...)` does not exist, `IsModifierKey` is inaccessible.

- [ ] **Step 3: Implement**

In `KeymapHints.cs`, replace the body of `Describe(IEnumerable<KeyChord> chords, IFormatProvider? format = null)` and add the two new members. The full replacement for that method and its neighbours:

```csharp
    public static string? Describe(IEnumerable<KeyChord> chords, IFormatProvider? format = null)
    {
        var formatted = Ordered(chords).Select(chord => Format(chord, format)).ToList();
        return formatted.Count switch
        {
            0 => null,
            1 => formatted[0],
            _ => string.Join(", ", formatted.Take(formatted.Count - 1)) + " or " + formatted[^1],
        };
    }

    /// <summary>One chord as text, worded exactly as it is inside a tooltip's line, for the Keyboard tab's chips.</summary>
    public static string Describe(KeyChord chord, IFormatProvider? format = null) => Format(chord, format);

    /// <summary>The order every list of chords is shown in, tooltips and the Keyboard tab alike: unmodified chords
    /// first, then by KeyModifiers value, function keys ahead of other keys within a group, taps last.</summary>
    public static IEnumerable<KeyChord> Ordered(IEnumerable<KeyChord> chords) => chords
        .OrderBy(chord => chord.Tap ? 1 : 0)
        .ThenBy(chord => (int)chord.Modifiers)
        .ThenBy(chord => IsFunctionKey(chord.Key) ? 0 : 1)
        .ThenBy(chord => (int)chord.Key);
```

Keep the existing `Describe(Keymap keymap, TerminalKey key, ...)`, `IsFunctionKey` and `Format` unchanged. Update the class summary's "and, one day, the Keys menu (#23)" wording to "the Keyboard tab's chips and the Keys menu (#23)".

In `ChordSyntax.cs`, change `private static bool IsModifierKey` to `public static bool IsModifierKey` and give it a summary: `/// <summary>The keys that are modifiers and nothing else; a chord's key is never one of them.</summary>`.

- [ ] **Step 4: Run them to see them pass**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~KeymapHintsTests|FullyQualifiedName~ChordSyntaxTests"`
Expected: PASS (the existing tests in both classes still pass).

- [ ] **Step 5: Commit**

```bash
git add src/LizTerm.App/Keyboard/KeymapHints.cs src/LizTerm.App/Keyboard/ChordSyntax.cs tests/LizTerm.App.Tests/Keyboard/KeymapHintsTests.cs tests/LizTerm.App.Tests/Keyboard/ChordSyntaxTests.cs
git commit -m "App: KeymapHints.Ordered and Describe(KeyChord), ChordSyntax.IsModifierKey public, for the Keyboard tab (#18)

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

### Task 2: `KeymapRow`, `KeymapChip` and the editor's row list

**Files:**
- Create: `src/LizTerm.App/Keyboard/CaptureResult.cs`
- Create: `src/LizTerm.App/ViewModels/KeymapChip.cs`
- Create: `src/LizTerm.App/ViewModels/KeymapRow.cs`
- Create: `src/LizTerm.App/ViewModels/KeymapEditorViewModel.cs`
- Test: `tests/LizTerm.App.Tests/ViewModels/KeymapEditorViewModelTests.cs`

**Interfaces:**
- Consumes: `KeymapViewModel` (`ChordsFor(KeymapAction)`, `Compose(bool)`, `Bind`, `Unbind`, `ResetToDefaults`, `Changed`, `PropertyChanged`), `KeymapHints.Ordered/Describe`, `PlatformHotkeys`.
- Produces:
  - `public sealed record CaptureResult(bool Accepted, string? Message)` in `LizTerm.App.Keyboard`.
  - `public sealed class KeymapChip { KeyChord Chord; string Text; string RemoveName; ICommand RemoveCommand }`, ctor `(KeyChord chord, string text, Action<KeyChord> remove)`.
  - `public sealed class KeymapRow : ObservableObject` with `KeymapAction Target`, `string Title`, `string? Note`, `bool HasNote`, `string AddName`, `ObservableCollection<KeymapChip> Chips`, `Func<KeyChord, CaptureResult> CaptureHandler`, `void Refresh()`, `static string TitleOf(KeymapAction)`. Ctor `(KeymapAction target, KeymapViewModel keymap, Func<PlatformHotkeys> hotkeys, IFormatProvider? format)`. (`TryCapture` arrives in Task 3.)
  - `public sealed class KeymapEditorViewModel : ObservableObject, IDisposable` with `ObservableCollection<KeymapRow> Rows`, ctor `(KeymapViewModel keymap, Func<PlatformHotkeys> hotkeys, IFormatProvider? format = null)`. (Reset, notes and save error arrive in Task 3.)

- [ ] **Step 1: Write the failing tests**

Create `tests/LizTerm.App.Tests/ViewModels/KeymapEditorViewModelTests.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Input;
using LizTerm.App.Keyboard;
using LizTerm.App.ViewModels;
using LizTerm.Core.Session;

namespace LizTerm.App.Tests.ViewModels;

/// <summary>Editable keymap spec §5.3: one row per action over KeymapViewModel's queries, chips that follow every
/// change. Plain [Fact]s: nothing here touches Avalonia's platform, so chips are formatted with an explicit format.</summary>
public class KeymapEditorViewModelTests
{
    private static readonly KeyGestureFormatInfo Words = new(new Dictionary<Key, string>());
    private static readonly KeyChord CtrlHome = new(Key.Home, KeyModifiers.Control);
    private static readonly KeyChord Alt1 = new(Key.D1, KeyModifiers.Alt);
    private static readonly KeyChord Alt2 = new(Key.D2, KeyModifiers.Alt);

    private static (KeymapEditorViewModel Editor, KeymapViewModel Keymap) Build(Func<PlatformHotkeys>? hotkeys = null)
    {
        var keymap = new KeymapViewModel();
        return (new KeymapEditorViewModel(keymap, hotkeys ?? (() => PlatformHotkeys.Fallback), Words), keymap);
    }

    private static KeymapRow Row(KeymapEditorViewModel editor, string title) => editor.Rows.Single(row => row.Title == title);

    private static string[] Texts(KeymapRow row) => [.. row.Chips.Select(chip => chip.Text)];

    [Fact]
    public void Every_3270_key_has_exactly_one_row()
    {
        var (editor, _) = Build();

        var keys = editor.Rows.Select(row => row.Target).OfType<KeymapAction.SendKey>().Select(send => send.Key).ToList();

        Assert.Equal(Enum.GetValues<TerminalKey>().Order(), keys.Order());
        Assert.Equal(keys.Count, keys.Distinct().Count());
    }

    [Fact]
    public void Rows_are_in_the_spec_order_with_the_text_rows_last()
    {
        var (editor, _) = Build();
        var titles = editor.Rows.Select(row => row.Title).ToList();

        Assert.Equal(["Enter", "Newline", "Clear", "Reset", "Attn", "SysReq", "PF1", "PF2"], titles.Take(8));
        Assert.Equal(["PF23", "PF24", "PA1", "PA2", "PA3", "Tab", "Back Tab", "Insert", "Home", "Erase EOF", "Erase Input",
                      "Delete", "Backspace, erasing", "Backspace, moving left", "Dup", "Field Mark", "Up", "Down", "Left", "Right",
                      "Type ¢", "Type ¬"],
                     titles.Skip(titles.IndexOf("PF23")));
    }

    [Fact]
    public void Chips_show_the_defaults_in_tooltip_order()
    {
        var (editor, _) = Build();

        Assert.Equal(["Alt+2", "Ctrl+Home"], Texts(Row(editor, "PA2")));
        Assert.Equal(["F1"], Texts(Row(editor, "PF1")));
        Assert.Equal("a tap of Right Ctrl", Texts(Row(editor, "Enter")).Last());
        Assert.Single(Row(editor, "Backspace, erasing").Chips);
        Assert.Empty(Row(editor, "Backspace, moving left").Chips);
        Assert.Single(Row(editor, "Type ¬").Chips);
    }

    [Fact]
    public void Chips_follow_a_bind_and_an_unbind()
    {
        var (editor, keymap) = Build();

        keymap.Bind(CtrlHome, new KeymapAction.SendKey(TerminalKey.PA1));

        Assert.Equal(["Alt+1", "Ctrl+Home"], Texts(Row(editor, "PA1")));
        Assert.Equal(["Alt+2"], Texts(Row(editor, "PA2")));

        keymap.Unbind(Alt1);

        Assert.Equal(["Ctrl+Home"], Texts(Row(editor, "PA1")));
    }

    [Fact]
    public void A_row_whose_chords_did_not_change_keeps_its_chip_objects()
    {
        var (editor, keymap) = Build();
        var before = Row(editor, "PF1").Chips.Single();

        keymap.Bind(CtrlHome, new KeymapAction.SendKey(TerminalKey.PA1));

        Assert.Same(before, Row(editor, "PF1").Chips.Single());
    }

    [Fact]
    public void A_text_row_stays_after_its_chords_are_removed()
    {
        var (editor, keymap) = Build();

        keymap.Unbind(new KeyChord(Key.OemOpenBrackets, KeyModifiers.Control));

        Assert.Empty(Row(editor, "Type ¬").Chips);
    }

    [Fact]
    public void A_text_binding_the_file_added_gets_its_own_row()
    {
        var (editor, keymap) = Build();

        keymap.Bind(new KeyChord(Key.F9, KeyModifiers.Alt), new KeymapAction.TypeText("§"));

        Assert.Equal(["Alt+F9"], Texts(Row(editor, "Type §")));
        Assert.Equal("Type §", editor.Rows[^1].Title);
    }

    [Fact]
    public void The_two_backspace_rows_carry_the_profile_note_and_no_other_row_does()
    {
        var (editor, _) = Build();

        var withNotes = editor.Rows.Where(row => row.HasNote).Select(row => row.Title).ToList();

        Assert.Equal(["Backspace, erasing", "Backspace, moving left"], withNotes);
        Assert.Contains("profile's Backspace setting", Row(editor, "Backspace, erasing").Note);
    }

    [Fact]
    public void A_row_names_its_slot_for_a_screen_reader()
    {
        var (editor, _) = Build();

        Assert.Equal("Add a key to PA2", Row(editor, "PA2").AddName);
    }

    [Fact]
    public void Disposing_stops_the_rows_following()
    {
        var (editor, keymap) = Build();

        editor.Dispose();
        keymap.Bind(CtrlHome, new KeymapAction.SendKey(TerminalKey.PA1));

        Assert.Equal(["Alt+1"], Texts(Row(editor, "PA1")));
    }
}
```

- [ ] **Step 2: Run to see it fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~KeymapEditorViewModelTests"`
Expected: build error, `KeymapEditorViewModel`, `KeymapRow` do not exist.

- [ ] **Step 3: Implement**

`src/LizTerm.App/Keyboard/CaptureResult.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

namespace LizTerm.App.Keyboard;

/// <summary>What a Keyboard tab row answers the Add slot when it offers a captured chord (spec §5.3): Accepted means
/// the chord was bound (the slot disarms), otherwise it was refused and <paramref name="Message"/> is the reason,
/// shown verbatim with the slot still armed. An accepted result may carry a note ("Moved from PA2").</summary>
public sealed record CaptureResult(bool Accepted, string? Message);
```

`src/LizTerm.App/ViewModels/KeymapChip.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using LizTerm.App.Keyboard;

namespace LizTerm.App.ViewModels;

/// <summary>One chord on a Keyboard tab row: the text it reads as (KeymapHints' wording, so a chip matches the
/// keypad tooltip) and the command behind its × button.</summary>
public sealed class KeymapChip
{
    public KeymapChip(KeyChord chord, string text, Action<KeyChord> remove)
    {
        Chord = chord;
        Text = text;
        RemoveName = "Remove " + text;
        RemoveCommand = new RelayCommand(() => remove(chord));
    }

    public KeyChord Chord { get; }
    public string Text { get; }

    /// <summary>The × button's accessible name; the glyph alone says nothing to a screen reader.</summary>
    public string RemoveName { get; }

    public ICommand RemoveCommand { get; }
}
```

`src/LizTerm.App/ViewModels/KeymapRow.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using LizTerm.App.Keyboard;
using LizTerm.Core.Session;

namespace LizTerm.App.ViewModels;

/// <summary>One action on the Keyboard tab (spec §5.3): its name, the chords that do it and the answer to a chord
/// the Add slot captured for it. Rows are created once and refreshed in place, so a control in a row (the slot)
/// survives every change.</summary>
public sealed class KeymapRow : ObservableObject
{
    private const string BackspaceNote =
        "The profile's Backspace setting decides which of these the Backspace key does, until you bind it here.";

    private readonly KeymapViewModel _keymap;
    private readonly IFormatProvider? _format;

    public KeymapRow(KeymapAction target, KeymapViewModel keymap, Func<PlatformHotkeys> hotkeys, IFormatProvider? format)
    {
        Target = target;
        _keymap = keymap;
        _hotkeys = hotkeys;
        _format = format;
        Title = TitleOf(target);
        AddName = "Add a key to " + Title;
        Note = target is KeymapAction.SendKey { Key: TerminalKey.Erase or TerminalKey.Backspace } ? BackspaceNote : null;
        Refresh();
    }

    private readonly Func<PlatformHotkeys> _hotkeys;

    public KeymapAction Target { get; }
    public string Title { get; }

    /// <summary>The slot's accessible name; "Add" alone is one of forty.</summary>
    public string AddName { get; }

    public string? Note { get; }
    public bool HasNote => Note is not null;
    public ObservableCollection<KeymapChip> Chips { get; } = [];

    /// <summary>The name a row, and a "Moved from" message, uses for an action.</summary>
    public static string TitleOf(KeymapAction action) => action switch
    {
        KeymapAction.SendKey send => send.Key switch
        {
            TerminalKey.BackTab => "Back Tab",
            TerminalKey.EraseEof => "Erase EOF",
            TerminalKey.EraseInput => "Erase Input",
            TerminalKey.FieldMark => "Field Mark",
            TerminalKey.Erase => "Backspace, erasing",
            TerminalKey.Backspace => "Backspace, moving left",
            var other => other.ToString(),
        },
        KeymapAction.TypeText type => "Type " + type.Text,
        _ => "Nothing",
    };

    /// <summary>Recomputes the chips from the model; a row whose chords did not change is left alone, so its
    /// buttons keep their focus.</summary>
    public void Refresh()
    {
        var chords = KeymapHints.Ordered(_keymap.ChordsFor(Target)).ToList();
        if (chords.SequenceEqual(Chips.Select(chip => chip.Chord))) return;
        Chips.Clear();
        foreach (var chord in chords) Chips.Add(new KeymapChip(chord, KeymapHints.Describe(chord, _format), _keymap.Unbind));
    }
}
```

(Move the `_hotkeys` field up beside the other two if the compiler or your taste prefers; it is only used from Task 3.)

`src/LizTerm.App/ViewModels/KeymapEditorViewModel.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using LizTerm.App.Keyboard;
using LizTerm.Core.Session;

namespace LizTerm.App.ViewModels;

/// <summary>The Keyboard tab's data (editable keymap spec §5.3): a row per action over KeymapViewModel's queries.
/// It never reads the keymap's dictionary; that is the seam a chord-centric view would sit on. Subscribed to the
/// process's KeymapViewModel for as long as the tab is open: Dispose it when the window closes. UI thread only.</summary>
public sealed class KeymapEditorViewModel : ObservableObject, IDisposable
{
    /// <summary>The spec's order. Both Backspace actions sit where the spec's one Backspace row does.</summary>
    private static readonly TerminalKey[] Order =
    [
        TerminalKey.Enter, TerminalKey.Newline, TerminalKey.Clear, TerminalKey.Reset, TerminalKey.Attn, TerminalKey.SysReq,
        .. Enumerable.Range((int)TerminalKey.PF1, 24).Select(i => (TerminalKey)i),
        .. Enumerable.Range((int)TerminalKey.PA1, 3).Select(i => (TerminalKey)i),
        TerminalKey.Tab, TerminalKey.BackTab, TerminalKey.Insert, TerminalKey.Home, TerminalKey.EraseEof,
        TerminalKey.EraseInput, TerminalKey.Delete, TerminalKey.Erase, TerminalKey.Backspace, TerminalKey.Dup,
        TerminalKey.FieldMark, TerminalKey.Up, TerminalKey.Down, TerminalKey.Left, TerminalKey.Right,
    ];

    private readonly KeymapViewModel _keymap;
    private readonly Func<PlatformHotkeys> _hotkeys;
    private readonly IFormatProvider? _format;
    private readonly HashSet<string> _seenText = [];

    /// <param name="hotkeys">The platform's reserved gestures, asked at each capture: the view passes a function over
    /// the window's platform settings, a test passes PlatformHotkeys.MacOS or Fallback.</param>
    /// <param name="format">Passed to KeymapHints for the chips; null is the platform's own words.</param>
    public KeymapEditorViewModel(KeymapViewModel keymap, Func<PlatformHotkeys> hotkeys, IFormatProvider? format = null)
    {
        _keymap = keymap;
        _hotkeys = hotkeys;
        _format = format;
        Rows = new ObservableCollection<KeymapRow>(Order.Select(key => NewRow(new KeymapAction.SendKey(key))));
        AddNewTextRows();
        keymap.Changed += OnKeymapChanged;
    }

    public ObservableCollection<KeymapRow> Rows { get; }

    public void Dispose() => _keymap.Changed -= OnKeymapChanged;

    private KeymapRow NewRow(KeymapAction target) => new(target, _keymap, _hotkeys, _format);

    private void OnKeymapChanged(object? sender, EventArgs e)
    {
        foreach (var row in Rows) row.Refresh();
        AddNewTextRows();
    }

    /// <summary>One "Type ¬" row per text action the composed map holds, and, once shown, for as long as the tab
    /// is open: removing the last chord must leave the row so Add can undo it. Sorted so the order does not depend
    /// on the map's dictionary.</summary>
    private void AddNewTextRows()
    {
        var texts = _keymap.Compose(destructiveBackspace: true).Text.Values.Distinct().Order(StringComparer.Ordinal);
        foreach (var text in texts)
            if (_seenText.Add(text)) Rows.Add(NewRow(new KeymapAction.TypeText(text)));
    }
}
```

- [ ] **Step 4: Run to see it pass**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~KeymapEditorViewModelTests"`
Expected: PASS, 9 tests.

- [ ] **Step 5: Commit**

```bash
git add src/LizTerm.App/Keyboard/CaptureResult.cs src/LizTerm.App/ViewModels/KeymapChip.cs src/LizTerm.App/ViewModels/KeymapRow.cs src/LizTerm.App/ViewModels/KeymapEditorViewModel.cs tests/LizTerm.App.Tests/ViewModels/KeymapEditorViewModelTests.cs
git commit -m "App: KeymapEditorViewModel, one row per action with chips that follow the keymap (#18)

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

### Task 3: Capture, remove, reset, the notes and the save error

**Files:**
- Modify: `src/LizTerm.App/ViewModels/KeymapRow.cs`
- Modify: `src/LizTerm.App/ViewModels/KeymapEditorViewModel.cs`
- Test: `tests/LizTerm.App.Tests/ViewModels/KeymapEditorViewModelTests.cs`

**Interfaces:**
- Consumes: Task 2's types; `KeymapPolicy.Check(KeyChord, PlatformHotkeys) → KeymapVerdict` (`KeymapVerdict.Refused(string Reason)`); `KeymapViewModel.ActionOf`, `LastSaveError`, `UnreadableEntries`.
- Produces: `CaptureResult KeymapRow.TryCapture(KeyChord chord)` and `Func<KeyChord, CaptureResult> KeymapRow.CaptureHandler`; on the editor `ICommand ResetCommand`, `string? SaveError`, `bool HasSaveError`, `string? UnreadableNote`, `bool HasUnreadable`.

- [ ] **Step 1: Write the failing tests**

Add `using LizTerm.Core.Settings;` to the test file, make the class `IDisposable` for a temp directory (copy the `_dir`/`FilePath`/`Dispose` pattern from `KeymapViewModelTests`), and append:

```csharp
    // ---- capture --------------------------------------------------------------------------------------------

    [Fact]
    public void Capturing_a_free_chord_binds_it_and_says_nothing()
    {
        var (editor, keymap) = Build();
        var row = Row(editor, "PA1");

        var result = row.TryCapture(new KeyChord(Key.F9, KeyModifiers.Alt));

        Assert.Equal(new CaptureResult(true, null), result);
        Assert.Equal(new KeymapAction.SendKey(TerminalKey.PA1), keymap.ActionOf(new KeyChord(Key.F9, KeyModifiers.Alt)));
        Assert.Equal(["Alt+1", "Alt+F9"], Texts(row));
    }

    [Fact]
    public void Capturing_a_chord_another_row_holds_moves_it_and_names_the_row()
    {
        var (editor, keymap) = Build();

        var result = Row(editor, "PA1").TryCapture(Alt2);

        Assert.Equal(new CaptureResult(true, "Moved from PA2"), result);
        Assert.Equal(new KeymapAction.SendKey(TerminalKey.PA1), keymap.ActionOf(Alt2));
        Assert.Equal(["Ctrl+Home"], Texts(Row(editor, "PA2")));
    }

    [Fact]
    public void Capturing_a_chord_that_types_text_names_the_text_row()
    {
        var (editor, _) = Build();

        var result = Row(editor, "PA1").TryCapture(new KeyChord(Key.OemOpenBrackets, KeyModifiers.Control));

        Assert.Equal(new CaptureResult(true, "Moved from Type ¬"), result);
    }

    [Fact]
    public void Capturing_a_chord_onto_the_text_row_types_that_text()
    {
        var (editor, keymap) = Build();
        var chord = new KeyChord(Key.F9, KeyModifiers.Alt);

        Row(editor, "Type ¬").TryCapture(chord);

        Assert.Equal(new KeymapAction.TypeText("¬"), keymap.ActionOf(chord));
    }

    [Fact]
    public void Capturing_the_chord_a_row_already_has_writes_nothing()
    {
        var (editor, keymap) = Build();
        var changes = 0;
        keymap.Changed += (_, _) => changes++;

        var result = Row(editor, "PA2").TryCapture(Alt2);

        Assert.Equal(new CaptureResult(true, "Already does this"), result);
        Assert.Equal(0, changes);
    }

    [Fact]
    public void Capturing_Backspace_onto_the_erasing_row_is_written_though_it_is_already_the_default()
    {
        var (editor, keymap) = Build();
        var backspace = new KeyChord(Key.Back);

        var result = Row(editor, "Backspace, erasing").TryCapture(backspace);

        Assert.Equal(new CaptureResult(true, null), result);
        Assert.True(keymap.Overlay.Entries.ContainsKey(backspace));
    }

    [Fact]
    public void A_refused_chord_binds_nothing_and_gives_the_policys_reason()
    {
        var (editor, keymap) = Build();
        var changes = 0;
        keymap.Changed += (_, _) => changes++;

        var result = Row(editor, "PA1").TryCapture(new KeyChord(Key.C, KeyModifiers.Control));

        Assert.Equal(new CaptureResult(false, "LizTerm uses this for Copy"), result);
        Assert.Equal(0, changes);
    }

    [Fact]
    public void The_refusal_uses_the_platform_the_editor_was_given()
    {
        var (editor, _) = Build(() => PlatformHotkeys.MacOS);

        var result = Row(editor, "PA1").TryCapture(new KeyChord(Key.OemComma, KeyModifiers.Meta));

        Assert.Equal(new CaptureResult(false, "The menu bar sees Cmd shortcuts before the screen does"), result);
    }

    [Fact]
    public void A_tap_is_allowed_and_moves_from_the_row_that_had_it()
    {
        var (editor, keymap) = Build();

        var result = Row(editor, "Enter").TryCapture(KeyChord.TapOf(Key.LeftCtrl));

        Assert.Equal(new CaptureResult(true, "Moved from Reset"), result);
        Assert.Equal(new KeymapAction.SendKey(TerminalKey.Enter), keymap.ActionOf(KeyChord.TapOf(Key.LeftCtrl)));
    }

    [Fact]
    public void The_capture_handler_is_the_capture()
    {
        var (editor, _) = Build();

        var result = Row(editor, "PA1").CaptureHandler(new KeyChord(Key.F9, KeyModifiers.Alt));

        Assert.True(result.Accepted);
    }

    // ---- remove, reset ----------------------------------------------------------------------------------------

    [Fact]
    public void A_chips_remove_command_unbinds_its_chord()
    {
        var (editor, keymap) = Build();

        Row(editor, "PA2").Chips.Single(chip => chip.Text == "Alt+2").RemoveCommand.Execute(null);

        Assert.Null(keymap.ActionOf(Alt2));
        Assert.Equal(["Ctrl+Home"], Texts(Row(editor, "PA2")));
    }

    [Fact]
    public void Reset_to_defaults_brings_every_default_back()
    {
        var (editor, keymap) = Build();
        keymap.Bind(CtrlHome, new KeymapAction.SendKey(TerminalKey.PA1));
        keymap.Unbind(Alt2);

        editor.ResetCommand.Execute(null);

        Assert.Equal(["Alt+2", "Ctrl+Home"], Texts(Row(editor, "PA2")));
        Assert.Equal(["Alt+1"], Texts(Row(editor, "PA1")));
    }

    // ---- the notes under the list -----------------------------------------------------------------------------

    [Fact]
    public void The_unreadable_note_appears_only_when_the_file_has_entries_this_build_skipped()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, """{"bindings": {"Bogus+Home": "PA1", "Ctrl+F9": "NoSuchKey"}}""");
        var keymap = new KeymapViewModel(new KeymapStore(FilePath));
        var editor = new KeymapEditorViewModel(keymap, () => PlatformHotkeys.Fallback, Words);
        var raised = new List<string?>();
        editor.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        Assert.True(editor.HasUnreadable);
        Assert.Equal("2 entries in keymap.json could not be read. They are kept as written, and Reset to defaults removes them.",
                     editor.UnreadableNote);

        editor.ResetCommand.Execute(null);

        Assert.False(editor.HasUnreadable);
        Assert.Contains(nameof(KeymapEditorViewModel.HasUnreadable), raised);
        Assert.Contains(nameof(KeymapEditorViewModel.UnreadableNote), raised);
    }

    [Fact]
    public void One_unreadable_entry_is_worded_in_the_singular()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, """{"bindings": {"Bogus+Home": "PA1"}}""");
        var editor = new KeymapEditorViewModel(new KeymapViewModel(new KeymapStore(FilePath)), () => PlatformHotkeys.Fallback, Words);

        Assert.Equal("1 entry in keymap.json could not be read. It is kept as written, and Reset to defaults removes it.",
                     editor.UnreadableNote);
    }

    [Fact]
    public void A_failed_save_reaches_the_tabs_banner_and_the_change_still_stands()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, "not json");
        var keymap = new KeymapViewModel(new KeymapStore(FilePath));
        var editor = new KeymapEditorViewModel(keymap, () => PlatformHotkeys.Fallback, Words);
        var raised = new List<string?>();
        editor.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        Assert.False(editor.HasSaveError);

        Row(editor, "PA1").TryCapture(new KeyChord(Key.F9, KeyModifiers.Alt));

        Assert.True(editor.HasSaveError);
        Assert.StartsWith("Could not save the keymap", editor.SaveError);
        Assert.Contains(nameof(KeymapEditorViewModel.SaveError), raised);
        Assert.Contains(nameof(KeymapEditorViewModel.HasSaveError), raised);
        Assert.Equal(["Alt+1", "Alt+F9"], Texts(Row(editor, "PA1")));
    }
```

Note: the tests above use `keymap.Overlay`, which is `internal` in `KeymapViewModel`; `InternalsVisibleTo` for the App test project already covers it.

- [ ] **Step 2: Run to see them fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~KeymapEditorViewModelTests"`
Expected: build error, `TryCapture`, `CaptureHandler`, `ResetCommand`, `HasUnreadable`, `UnreadableNote`, `SaveError`, `HasSaveError` do not exist.

- [ ] **Step 3: Implement**

In `KeymapRow.cs` add `using Avalonia.Input;` and a field/property and method (place the property with the others, the method after `Refresh`):

```csharp
    /// <summary>Unmodified Backspace is the one chord the sparse rule always keeps (spec §3.3), so capturing it
    /// onto its own default row is a real choice, not a no-op.</summary>
    private static readonly KeyChord BackspaceChord = new(Key.Back);

    /// <summary>What the Add slot calls with a chord it captured. Bound as a delegate so the control knows nothing
    /// about this class.</summary>
    public Func<KeyChord, CaptureResult> CaptureHandler { get; }

    /// <summary>Judges the chord (KeymapPolicy first: that is what keeps a Cmd chord out of the file), then binds
    /// it to this row's action. A chord another action held moves, and the answer says from where.</summary>
    public CaptureResult TryCapture(KeyChord chord)
    {
        if (KeymapPolicy.Check(chord, _hotkeys()) is KeymapVerdict.Refused refused) return new CaptureResult(false, refused.Reason);
        var previous = _keymap.ActionOf(chord);
        if (previous == Target && chord != BackspaceChord) return new CaptureResult(true, "Already does this");
        _keymap.Bind(chord, Target);
        return new CaptureResult(true, previous is null || previous == Target ? null : "Moved from " + TitleOf(previous));
    }
```

and in the constructor add `CaptureHandler = TryCapture;` before `Refresh();`.

In `KeymapEditorViewModel.cs` add `using System.ComponentModel;`, `using System.Windows.Input;`, `using CommunityToolkit.Mvvm.Input;`, then:

```csharp
    // in the constructor, after the existing subscription:
    ResetCommand = new RelayCommand(keymap.ResetToDefaults);
    keymap.PropertyChanged += OnKeymapPropertyChanged;

    // Dispose becomes:
    public void Dispose()
    {
        _keymap.Changed -= OnKeymapChanged;
        _keymap.PropertyChanged -= OnKeymapPropertyChanged;
    }

    public ICommand ResetCommand { get; }

    /// <summary>The last save's failure, the same message every session window's banner shows.</summary>
    public string? SaveError => _keymap.LastSaveError;
    public bool HasSaveError => SaveError is not null;

    public bool HasUnreadable => _keymap.UnreadableEntries > 0;

    /// <summary>What the tab tells someone whose keymap.json holds entries this build skipped: they are safe, and
    /// Reset is what discards them.</summary>
    public string? UnreadableNote => _keymap.UnreadableEntries switch
    {
        0 => null,
        1 => "1 entry in keymap.json could not be read. It is kept as written, and Reset to defaults removes it.",
        var n => $"{n} entries in keymap.json could not be read. They are kept as written, and Reset to defaults removes them.",
    };

    private void OnKeymapPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(KeymapViewModel.LastSaveError):
                OnPropertyChanged(nameof(SaveError));
                OnPropertyChanged(nameof(HasSaveError));
                break;
            case nameof(KeymapViewModel.UnreadableEntries):
                OnPropertyChanged(nameof(UnreadableNote));
                OnPropertyChanged(nameof(HasUnreadable));
                break;
        }
    }
```


- [ ] **Step 4: Run to see them pass**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~KeymapEditorViewModelTests"`
Expected: PASS, 24 tests.

- [ ] **Step 5: Commit**

```bash
git add src/LizTerm.App/ViewModels/KeymapRow.cs src/LizTerm.App/ViewModels/KeymapEditorViewModel.cs tests/LizTerm.App.Tests/ViewModels/KeymapEditorViewModelTests.cs
git commit -m "App: Keyboard tab rows capture chords through KeymapPolicy, remove, reset and report unsaved changes (#18)

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

### Task 4: `ChordCaptureBox`, the Add slot

**Files:**
- Create: `src/LizTerm.App/Controls/ChordCaptureBox.cs`
- Test: `tests/LizTerm.App.Tests/Controls/ChordCaptureBoxTests.cs`

**Interfaces:**
- Consumes: `ModifierTapDetector`, `ChordSyntax.IsModifierKey`, `KeyChord`, `CaptureResult`.
- Produces: `public sealed class ChordCaptureBox : Button` with `Func<KeyChord, CaptureResult>? CaptureHandler` (styled property `CaptureHandlerProperty`), `bool IsArmed`, `string Text` (what the slot reads), `TimeSpan MessageDuration` (default 3 s), constants `IdleText = "Add"` and `ArmedText = "Press a key"`, style class `chord-slot` and pseudo-class `:armed`.

Behaviour (spec §5.3 as refined by "Where this plan differs" 1): idle, it is an ordinary button, so Tab moves through it and Enter, Space or a click arms it. Armed, every key is captured: a non-modifier key with the event's modifiers is offered as a chord; a Ctrl key pressed and released alone is offered as a tap (the slot's own `ModifierTapDetector`); a Shift, Alt or Windows key alone is waited on. The handler's answer decides: accepted disarms and shows the message (if any) for `MessageDuration`; refused stays armed showing the reason. Focus loss, or a second click, disarms. Two Avalonia facts the tests guard: a `Button` subclass needs `StyleKeyOverride => typeof(Button)` or it renders with no template (16 px tall, unclickable), and because of that override a style selector cannot name the subclass, so styling hangs off a class and a pseudo-class.

- [ ] **Step 1: Write the failing tests**

Create `tests/LizTerm.App.Tests/Controls/ChordCaptureBoxTests.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using LizTerm.App.Controls;
using LizTerm.App.Keyboard;

namespace LizTerm.App.Tests.Controls;

/// <summary>The Add slot (editable keymap spec §5.3, refined: armed by activating it, not by focusing it).</summary>
public class ChordCaptureBoxTests
{
    private static readonly CaptureResult Accept = new(true, null);
    private static readonly CaptureResult Refuse = new(false, "This would take away typing that character");

    private static (Window Window, ChordCaptureBox Box, Button Other, List<KeyChord> Offered) Show(CaptureResult? answer = null)
    {
        var offered = new List<KeyChord>();
        var box = new ChordCaptureBox { CaptureHandler = chord => { offered.Add(chord); return answer ?? Accept; } };
        var other = new Button { Content = "Other" };
        // A stop after the slot, so "Tab moves on" has somewhere to go rather than wrapping or staying.
        var after = new Button { Content = "After" };
        var window = new Window { Content = new StackPanel { Children = { other, box, after } } };
        window.Show();
        box.Focus();
        return (window, box, other, offered);
    }

    private static void Arm(Window window)
    {
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);
    }

    private static Point Centre(Window window, Control control) =>
        control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;

    [AvaloniaFact]
    public void An_idle_slot_reads_Add_and_offers_nothing()
    {
        var (window, box, _, offered) = Show();

        window.KeyPressQwerty(PhysicalKey.F9, RawInputModifiers.None);

        Assert.Equal("Add", box.Text);
        Assert.False(box.IsArmed);
        Assert.Empty(offered);
    }

    [AvaloniaFact]
    public void Enter_arms_a_focused_slot_and_is_not_itself_a_chord()
    {
        var (window, box, _, offered) = Show();

        Arm(window);

        Assert.True(box.IsArmed);
        Assert.Equal("Press a key", box.Text);
        Assert.Empty(offered);
    }

    [AvaloniaFact]
    public void A_chord_is_offered_with_its_modifiers_and_an_accepted_one_disarms()
    {
        var (window, box, _, offered) = Show();
        Arm(window);

        window.KeyPressQwerty(PhysicalKey.Home, RawInputModifiers.Control);

        Assert.Equal([new KeyChord(Key.Home, KeyModifiers.Control)], offered);
        Assert.False(box.IsArmed);
        Assert.Equal("Add", box.Text);
    }

    [AvaloniaFact]
    public void A_refusal_shows_its_reason_and_stays_armed()
    {
        var (window, box, _, offered) = Show(Refuse);
        Arm(window);

        window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.None);

        Assert.True(box.IsArmed);
        Assert.Equal("This would take away typing that character", box.Text);
        Assert.Single(offered);
    }

    [AvaloniaFact]
    public async Task An_accepted_note_shows_for_a_moment_and_then_goes()
    {
        var (window, box, _, _) = Show(new CaptureResult(true, "Moved from PA2"));
        box.MessageDuration = TimeSpan.FromMilliseconds(30);
        Arm(window);

        window.KeyPressQwerty(PhysicalKey.F9, RawInputModifiers.Alt);

        Assert.False(box.IsArmed);
        Assert.Equal("Moved from PA2", box.Text);
        for (var i = 0; i < 100 && box.Text != "Add"; i++)
        {
            await Task.Delay(20, TestContext.Current.CancellationToken);
            Dispatcher.UIThread.RunJobs();
        }
        Assert.Equal("Add", box.Text);
    }

    [AvaloniaFact]
    public void A_Ctrl_key_pressed_and_released_alone_is_offered_as_a_tap()
    {
        var (window, box, _, offered) = Show();
        Arm(window);

        window.KeyPressQwerty(PhysicalKey.ControlLeft, RawInputModifiers.Control);
        Assert.Empty(offered);
        window.KeyReleaseQwerty(PhysicalKey.ControlLeft, RawInputModifiers.None);

        Assert.Equal([KeyChord.TapOf(Key.LeftCtrl)], offered);
        Assert.False(box.IsArmed);
    }

    [AvaloniaFact]
    public void Ctrl_then_C_is_the_chord_and_its_releases_are_not_a_tap()
    {
        var (window, box, _, offered) = Show(new CaptureResult(false, "LizTerm uses this for Copy"));
        Arm(window);

        window.KeyPressQwerty(PhysicalKey.ControlLeft, RawInputModifiers.Control);
        window.KeyPressQwerty(PhysicalKey.C, RawInputModifiers.Control);
        window.KeyReleaseQwerty(PhysicalKey.C, RawInputModifiers.Control);
        window.KeyReleaseQwerty(PhysicalKey.ControlLeft, RawInputModifiers.None);

        Assert.Equal([new KeyChord(Key.C, KeyModifiers.Control)], offered);
        Assert.True(box.IsArmed);
    }

    [AvaloniaFact]
    public void Shift_pressed_alone_is_waited_on_not_offered()
    {
        var (window, box, _, offered) = Show();
        Arm(window);

        window.KeyPressQwerty(PhysicalKey.ShiftLeft, RawInputModifiers.Shift);
        window.KeyReleaseQwerty(PhysicalKey.ShiftLeft, RawInputModifiers.None);

        Assert.Empty(offered);
        Assert.True(box.IsArmed);
    }

    [AvaloniaFact]
    public void Escape_Tab_and_Enter_are_capturable_and_neither_move_focus_nor_close_the_window()
    {
        var (window, box, _, offered) = Show(Refuse);
        var done = new Button { Content = "Done", IsDefault = true, IsCancel = true };
        var doneClicks = 0;
        done.Click += (_, _) => doneClicks++;
        ((StackPanel)window.Content!).Children.Add(done);
        Arm(window);

        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
        window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.Shift);
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);

        Assert.Equal(
            [new KeyChord(Key.Escape), new KeyChord(Key.Tab), new KeyChord(Key.Tab, KeyModifiers.Shift), new KeyChord(Key.Enter)],
            offered);
        Assert.True(box.IsFocused);
        Assert.True(window.IsVisible);
        Assert.Equal(0, doneClicks);
    }

    [AvaloniaFact]
    public void Space_that_binds_does_not_reopen_the_slot_on_its_release()
    {
        var (window, box, _, offered) = Show();
        Arm(window);

        window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);

        Assert.Equal([new KeyChord(Key.Space)], offered);
        Assert.False(box.IsArmed);
    }

    [AvaloniaFact]
    public void Losing_focus_disarms()
    {
        var (window, box, other, _) = Show();
        Arm(window);

        other.Focus();

        Assert.False(box.IsArmed);
        Assert.Equal("Add", box.Text);
    }

    [AvaloniaFact]
    public void An_idle_slot_lets_Tab_move_focus_on()
    {
        var (window, box, _, offered) = Show();

        window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);

        Assert.False(box.IsFocused);
        Assert.Empty(offered);
    }

    [AvaloniaFact]
    public void A_click_arms_and_a_second_click_disarms()
    {
        var (window, box, other, _) = Show();
        other.Focus();
        window.UpdateLayout();
        var point = Centre(window, box);

        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Assert.True(box.IsArmed);
        Assert.True(box.IsFocused);

        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Assert.False(box.IsArmed);
    }

    [AvaloniaFact]
    public void The_slot_takes_the_button_theme_and_marks_the_armed_state_with_a_pseudo_class()
    {
        var (window, box, _, _) = Show();
        window.UpdateLayout();

        Assert.True(box.Bounds.Height > 20, "a Button subclass without StyleKeyOverride has no template");
        Assert.False(box.Classes.Contains(":armed"));
        Arm(window);
        Assert.True(box.Classes.Contains(":armed"));
    }
}
```

- [ ] **Step 2: Run to see them fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~ChordCaptureBoxTests"`
Expected: build error, `ChordCaptureBox` does not exist.

- [ ] **Step 3: Implement**

`src/LizTerm.App/Controls/ChordCaptureBox.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using LizTerm.App.Keyboard;

namespace LizTerm.App.Controls;

/// <summary>The Keyboard tab's Add slot (editable keymap spec §5.3). Idle it is an ordinary button, so Tab passes
/// through it (an armed-on-focus slot would swallow Tab and trap a keyboard user); a click, or Enter or Space,
/// arms it. Armed, it captures the next chord: a key with its modifiers, or a Ctrl key pressed and released alone
/// as a tap (its own <see cref="ModifierTapDetector"/>, the screen's rule), and offers it to
/// <see cref="CaptureHandler"/>. Accepted disarms; refused stays armed with the reason as its text. Focus loss or a
/// second click disarms. Escape and Tab are chords like the rest, since both are bindable.
/// The control knows nothing about keymaps: the handler is the row's.</summary>
public sealed class ChordCaptureBox : Button
{
    public const string IdleText = "Add";
    public const string ArmedText = "Press a key";

    public static readonly StyledProperty<Func<KeyChord, CaptureResult>?> CaptureHandlerProperty =
        AvaloniaProperty.Register<ChordCaptureBox, Func<KeyChord, CaptureResult>?>(nameof(CaptureHandler));

    private readonly ModifierTapDetector _taps = new();
    private readonly TextBlock _label = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private DispatcherTimer? _expiry;
    private string? _message;

    public ChordCaptureBox()
    {
        Classes.Add("chord-slot");
        Content = _label;
        Refresh();
    }

    /// <summary>A Button subclass is a different style key and would render with no template at all. It also means a
    /// style selector cannot name this type, so the tab styles it by the class and pseudo-class set here.</summary>
    protected override Type StyleKeyOverride => typeof(Button);

    public Func<KeyChord, CaptureResult>? CaptureHandler
    {
        get => GetValue(CaptureHandlerProperty);
        set => SetValue(CaptureHandlerProperty, value);
    }

    public bool IsArmed { get; private set; }

    /// <summary>How long an accepted note ("Moved from PA2") shows before the slot reads Add again.</summary>
    public TimeSpan MessageDuration { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>What the slot reads now; the words carry the state, colour and border only echo it.</summary>
    public string Text => _label.Text ?? "";

    protected override void OnClick()
    {
        if (IsArmed) Disarm();
        else Arm();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (!IsArmed)
        {
            base.OnKeyDown(e);
            return;
        }
        e.Handled = true;
        _taps.KeyDown(e.Key);
        if (e.Key == Key.None || ChordSyntax.IsModifierKey(e.Key)) return;
        Offer(new KeyChord(e.Key, e.KeyModifiers));
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (!IsArmed)
        {
            base.OnKeyUp(e);
            return;
        }
        e.Handled = true;
        if (_taps.KeyUp(e.Key) is { } tapped) Offer(KeyChord.TapOf(tapped));
    }

    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        if (IsArmed || _message is not null) Disarm();
        base.OnLostFocus(e);
    }

    private void Arm()
    {
        IsArmed = true;
        _taps.Reset();
        Show(null, expires: false);
        Focus();
    }

    private void Disarm()
    {
        IsArmed = false;
        _taps.Reset();
        Show(null, expires: false);
    }

    private void Offer(KeyChord chord)
    {
        var result = CaptureHandler?.Invoke(chord) ?? new CaptureResult(false, null);
        if (result.Accepted)
        {
            IsArmed = false;
            _taps.Reset();
            Show(result.Message, expires: true);
        }
        else
        {
            Show(result.Message, expires: false);
        }
    }

    private void Show(string? message, bool expires)
    {
        _message = message;
        _expiry?.Stop();
        _expiry = null;
        if (expires && message is not null)
        {
            _expiry = new DispatcherTimer(MessageDuration, DispatcherPriority.Normal, (_, _) =>
            {
                _expiry?.Stop();
                _expiry = null;
                _message = null;
                Refresh();
            });
            _expiry.Start();
        }
        Refresh();
    }

    private void Refresh()
    {
        _label.Text = _message ?? (IsArmed ? ArmedText : IdleText);
        PseudoClasses.Set(":armed", IsArmed);
    }
}
```

- [ ] **Step 4: Run to see them pass**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~ChordCaptureBoxTests"`
Expected: PASS, 13 tests. If `A_click_arms_and_a_second_click_disarms` fails on the first assertion, the pointer press did not reach the button: check the button's bounds (`window.UpdateLayout()` first) and that `StyleKeyOverride` is in place; do not switch the test to raising `Button.ClickEvent`, which would prove nothing about the pointer path. If the `Escape_Tab_and_Enter...` test shows `doneClicks` or focus movement, arming is leaking keys to the window: handle the key in a tunnelling handler on the box instead, and record why in the class summary.

- [ ] **Step 5: Commit**

```bash
git add src/LizTerm.App/Controls/ChordCaptureBox.cs tests/LizTerm.App.Tests/Controls/ChordCaptureBoxTests.cs
git commit -m "App: ChordCaptureBox, an Add slot armed by activation that captures a chord or a Ctrl tap (#18)

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

### Task 5: `KeyboardTab`, the tab's layout

**Files:**
- Create: `src/LizTerm.App/Views/KeyboardTab.axaml`
- Create: `src/LizTerm.App/Views/KeyboardTab.axaml.cs`
- Test: `tests/LizTerm.App.Tests/Views/KeyboardTabTests.cs`

**Interfaces:**
- Consumes: `KeymapEditorViewModel` (as the control's `DataContext`), `KeymapRow`, `KeymapChip`, `ChordCaptureBox`.
- Produces: `public partial class KeyboardTab : UserControl` in `LizTerm.App.Views`, parameterless, with named parts `RowList` (`ItemsControl`), `ResetButton`, `SaveErrorText`, `UnreadableText`. The data context is set by whoever hosts it.

- [ ] **Step 1: Write the failing tests**

Create `tests/LizTerm.App.Tests/Views/KeyboardTabTests.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using LizTerm.App.Controls;
using LizTerm.App.Keyboard;
using LizTerm.App.ViewModels;
using LizTerm.App.Views;
using LizTerm.Core.Session;

namespace LizTerm.App.Tests.Views;

/// <summary>The Keyboard tab on its own, in a bare window (PreferencesWindowTests covers it inside Preferences).</summary>
public class KeyboardTabTests
{
    private static readonly KeyGestureFormatInfo Words = new(new Dictionary<Key, string>());

    private static (Window Window, KeyboardTab Tab, KeymapEditorViewModel Editor, KeymapViewModel Keymap) Show()
    {
        var keymap = new KeymapViewModel();
        var editor = new KeymapEditorViewModel(keymap, () => PlatformHotkeys.Fallback, Words);
        var tab = new KeyboardTab { DataContext = editor };
        var window = new Window { Width = 520, Height = 400, Content = tab };
        window.Show();
        window.UpdateLayout();
        return (window, tab, editor, keymap);
    }

    private static Control RowContainer(KeyboardTab tab, KeymapEditorViewModel editor, string title) =>
        tab.FindControl<ItemsControl>("RowList")!.ContainerFromIndex(editor.Rows.ToList().FindIndex(row => row.Title == title))!;

    private static ChordCaptureBox SlotOf(Control container) => container.GetVisualDescendants().OfType<ChordCaptureBox>().Single();

    [AvaloniaFact]
    public void There_is_a_row_and_a_slot_for_every_action()
    {
        var (_, tab, editor, _) = Show();

        Assert.Equal(Enum.GetValues<TerminalKey>().Length + 2, tab.FindControl<ItemsControl>("RowList")!.ItemCount);
        Assert.Equal(editor.Rows.Count, tab.GetVisualDescendants().OfType<ChordCaptureBox>().Count());
    }

    [AvaloniaFact]
    public void A_row_shows_its_title_and_its_chips()
    {
        var (_, tab, editor, _) = Show();

        var texts = RowContainer(tab, editor, "PA2").GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();

        Assert.Contains("PA2", texts);
        Assert.Contains("Alt+2", texts);
        Assert.Contains("Ctrl+Home", texts);
    }

    [AvaloniaFact]
    public void A_chords_slot_binds_it_to_its_own_row_and_the_chip_appears()
    {
        var (window, tab, editor, keymap) = Show();
        var slot = SlotOf(RowContainer(tab, editor, "PA1"));
        slot.Focus();

        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        window.KeyPressQwerty(PhysicalKey.F9, RawInputModifiers.Alt);
        window.UpdateLayout();

        Assert.Equal(new KeymapAction.SendKey(TerminalKey.PA1), keymap.ActionOf(new KeyChord(Key.F9, KeyModifiers.Alt)));
        var texts = RowContainer(tab, editor, "PA1").GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains("Alt+F9", texts);
        Assert.False(slot.IsArmed);
    }

    [AvaloniaFact]
    public void A_refused_chord_shows_the_reason_in_the_slot()
    {
        var (window, tab, editor, keymap) = Show();
        var slot = SlotOf(RowContainer(tab, editor, "PA1"));
        slot.Focus();

        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.None);

        Assert.True(slot.IsArmed);
        Assert.Equal("This would take away typing that character", slot.Text);
        Assert.Null(keymap.ActionOf(new KeyChord(Key.A)));
    }

    [AvaloniaFact]
    public void A_chips_remove_button_unbinds_the_chord()
    {
        var (window, tab, editor, keymap) = Show();
        var remove = RowContainer(tab, editor, "PA2").GetVisualDescendants().OfType<Button>()
            .Single(button => Avalonia.Automation.AutomationProperties.GetName(button) == "Remove Alt+2");

        remove.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        window.UpdateLayout();

        Assert.Null(keymap.ActionOf(new KeyChord(Key.D2, KeyModifiers.Alt)));
        var texts = RowContainer(tab, editor, "PA2").GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.DoesNotContain("Alt+2", texts);
    }

    [AvaloniaFact]
    public void Reset_to_defaults_restores_what_was_changed()
    {
        var (window, tab, _, keymap) = Show();
        keymap.Unbind(new KeyChord(Key.D2, KeyModifiers.Alt));

        tab.FindControl<Button>("ResetButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.Equal(new KeymapAction.SendKey(TerminalKey.PA2), keymap.ActionOf(new KeyChord(Key.D2, KeyModifiers.Alt)));
    }

    [AvaloniaFact]
    public void The_two_notes_show_only_when_there_is_something_to_say()
    {
        var (_, tab, _, _) = Show();

        Assert.False(tab.FindControl<TextBlock>("SaveErrorText")!.IsVisible);
        Assert.False(tab.FindControl<TextBlock>("UnreadableText")!.IsVisible);
    }
}
```

The chips' remove buttons are found by their automation name: give each `AutomationProperties.Name="{Binding RemoveName}"` in the template below.

- [ ] **Step 2: Run to see them fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~KeyboardTabTests"`
Expected: build error, `KeyboardTab` does not exist.

- [ ] **Step 3: Implement**

`src/LizTerm.App/Views/KeyboardTab.axaml`:

```xml
<!--
  This file is part of LizTerm.
  Copyright 2026 by CoffeeMuse
  SPDX-License-Identifier: BSD-3-Clause
-->
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:LizTerm.App.ViewModels"
             xmlns:controls="using:LizTerm.App.Controls"
             x:Class="LizTerm.App.Views.KeyboardTab"
             x:DataType="vm:KeymapEditorViewModel">
  <!-- The Keyboard tab (#18). Its data context is the KeymapEditorViewModel, not the Preferences window's
       SettingsViewModel, so PreferencesWindow sets it. One row per action: the name on the left, its chords as
       chips (each with a × that removes it) and the Add slot on the right. Colour never carries meaning alone: the
       armed slot reads "Press a key" and has a thicker border, a refusal is text, a chip has its ×. -->
  <UserControl.Styles>
    <Style Selector="TextBlock.hint">
      <Setter Property="Foreground" Value="#A0A0A0" />
      <Setter Property="FontSize" Value="12" />
      <Setter Property="TextWrapping" Value="Wrap" />
    </Style>
    <!-- ChordCaptureBox is a Button under another type, so it is styled by class, not by type. -->
    <Style Selector="Button.chord-slot">
      <Setter Property="MinWidth" Value="84" />
      <Setter Property="MaxWidth" Value="180" />
      <Setter Property="MinHeight" Value="24" />
      <Setter Property="Padding" Value="8,0" />
      <Setter Property="FontSize" Value="12" />
      <Setter Property="VerticalAlignment" Value="Top" />
      <Setter Property="Margin" Value="6,0,0,4" />
    </Style>
    <Style Selector="Button.chord-slot:armed">
      <Setter Property="BorderThickness" Value="2" />
    </Style>
    <Style Selector="Button.remove">
      <Setter Property="MinWidth" Value="0" />
      <Setter Property="MinHeight" Value="0" />
      <Setter Property="Padding" Value="5,0" />
      <Setter Property="Background" Value="Transparent" />
      <Setter Property="FontSize" Value="13" />
    </Style>
  </UserControl.Styles>
  <DockPanel>
    <StackPanel DockPanel.Dock="Bottom" Spacing="6" Margin="0,8,0,0">
      <!-- Hidden until a save fails; the session windows show the same message in their banners. -->
      <TextBlock x:Name="SaveErrorText" Text="{Binding SaveError}" IsVisible="{Binding HasSaveError}"
                 Foreground="#FF8080" TextWrapping="Wrap" />
      <TextBlock x:Name="UnreadableText" Classes="hint" Text="{Binding UnreadableNote}" IsVisible="{Binding HasUnreadable}" />
      <Button x:Name="ResetButton" Content="Reset to defaults" Command="{Binding ResetCommand}" />
    </StackPanel>
    <ScrollViewer x:Name="RowScroller" VerticalScrollBarVisibility="Auto">
      <ItemsControl x:Name="RowList" ItemsSource="{Binding Rows}" Margin="0,0,12,0">
        <ItemsControl.ItemTemplate>
          <DataTemplate x:DataType="vm:KeymapRow">
            <Grid ColumnDefinitions="150,*" RowDefinitions="Auto,Auto" Margin="0,4">
              <TextBlock Grid.Column="0" Text="{Binding Title}" FontWeight="SemiBold" Margin="0,3,8,0" TextWrapping="Wrap" />
              <DockPanel Grid.Column="1">
                <controls:ChordCaptureBox DockPanel.Dock="Right" CaptureHandler="{Binding CaptureHandler}"
                                          AutomationProperties.Name="{Binding AddName}" />
                <ItemsControl ItemsSource="{Binding Chips}">
                  <ItemsControl.ItemsPanel>
                    <ItemsPanelTemplate>
                      <WrapPanel />
                    </ItemsPanelTemplate>
                  </ItemsControl.ItemsPanel>
                  <ItemsControl.ItemTemplate>
                    <DataTemplate x:DataType="vm:KeymapChip">
                      <Border Background="#303030" CornerRadius="3" Padding="6,0,1,0" Margin="0,0,4,4">
                        <StackPanel Orientation="Horizontal" Spacing="1">
                          <TextBlock Text="{Binding Text}" FontSize="12" VerticalAlignment="Center" />
                          <Button Classes="remove" Content="×" Command="{Binding RemoveCommand}"
                                  AutomationProperties.Name="{Binding RemoveName}" />
                        </StackPanel>
                      </Border>
                    </DataTemplate>
                  </ItemsControl.ItemTemplate>
                </ItemsControl>
              </DockPanel>
              <TextBlock Grid.Row="1" Grid.ColumnSpan="2" Classes="hint" Text="{Binding Note}" IsVisible="{Binding HasNote}" />
            </Grid>
          </DataTemplate>
        </ItemsControl.ItemTemplate>
      </ItemsControl>
    </ScrollViewer>
  </DockPanel>
</UserControl>
```

`src/LizTerm.App/Views/KeyboardTab.axaml.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Controls;

namespace LizTerm.App.Views;

/// <summary>The Preferences window's Keyboard tab (editable keymap spec §5.3). Pure layout: the host sets the
/// data context, a <see cref="ViewModels.KeymapEditorViewModel"/>, and everything else is binding.</summary>
public partial class KeyboardTab : UserControl
{
    public KeyboardTab() => InitializeComponent();
}
```

- [ ] **Step 4: Run to see them pass**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~KeyboardTabTests"`
Expected: PASS, 7 tests. If `AutomationProperties` does not resolve in the XAML, add `xmlns:automation`-free usage is fine: `AutomationProperties.Name` is in the default Avalonia namespace; in the C# test the class is `Avalonia.Automation.AutomationProperties`.

- [ ] **Step 5: Commit**

```bash
git add src/LizTerm.App/Views/KeyboardTab.axaml src/LizTerm.App/Views/KeyboardTab.axaml.cs tests/LizTerm.App.Tests/Views/KeyboardTabTests.cs
git commit -m "App: KeyboardTab, the rows, chips and Add slots laid out over KeymapEditorViewModel (#18)

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

### Task 6: The fifth tab in Preferences, and `App.ShowPreferences`

**Files:**
- Modify: `src/LizTerm.App/Views/PreferencesWindow.axaml`
- Modify: `src/LizTerm.App/Views/PreferencesWindow.axaml.cs`
- Modify: `src/LizTerm.App/App.axaml.cs:330-349`
- Test: `tests/LizTerm.App.Tests/Views/PreferencesWindowTests.cs` (edit), `tests/LizTerm.App.Tests/Views/KeyboardTabTests.cs` (append)

**Interfaces:**
- Consumes: `KeyboardTab`, `KeymapEditorViewModel`, `KeymapViewModel`, `PlatformHotkeys.From`.
- Produces: `internal PreferencesWindow(SettingsViewModel settings, KeymapViewModel keymap, bool systemAlertAvailable, bool menuStyleChoosable)` (replaces the three-argument one); `internal PreferencesWindow ShowPreferences(SettingsViewModel settings, KeymapViewModel? keymap = null)` (null means an in-memory keymap, so a test never touches `keymap.json`); a `TabItem Header="Keyboard"` holding `KeyboardTab x:Name="KeyboardPanel"`.

- [ ] **Step 1: Write the failing tests**

In `PreferencesWindowTests.cs`, change the `Show` helper to build the window with a keymap:

```csharp
    private static (PreferencesWindow Window, SettingsViewModel Settings) Show(
        SettingsViewModel? settings = null, bool systemAlertAvailable = true, bool menuStyleChoosable = true,
        KeymapViewModel? keymap = null)
    {
        settings ??= new SettingsViewModel();
        var window = new PreferencesWindow(settings, keymap ?? new KeymapViewModel(), systemAlertAvailable, menuStyleChoosable);
        window.Show();
        return (window, settings);
    }
```

and append these tests to the class (add `using LizTerm.App.Keyboard;` and `using Avalonia.VisualTree;` if missing):

```csharp
    [AvaloniaFact]
    public void The_tabs_are_five_and_Keyboard_is_last()
    {
        var (window, _) = Show();

        var headers = window.FindControl<TabControl>("Tabs")!.Items.OfType<TabItem>().Select(tab => tab.Header).ToList();

        Assert.Equal(["General", "Display", "Bell", "Window", "Keyboard"], headers);
    }

    [AvaloniaFact]
    public void The_Keyboard_tab_edits_the_keymap_it_was_given()
    {
        var keymap = new KeymapViewModel();
        var (window, _) = Show(keymap: keymap);
        var tab = window.FindControl<KeyboardTab>("KeyboardPanel")!;

        var editor = Assert.IsType<KeymapEditorViewModel>(tab.DataContext);

        keymap.Unbind(new KeyChord(Key.D2, KeyModifiers.Alt));

        Assert.Equal([new KeyChord(Key.Home, KeyModifiers.Control)],
                     editor.Rows.Single(row => row.Title == "PA2").Chips.Select(chip => chip.Chord));
    }

    [AvaloniaFact]
    public void Closing_the_window_stops_its_editor_following_the_keymap()
    {
        var keymap = new KeymapViewModel();
        var (window, _) = Show(keymap: keymap);
        var editor = (KeymapEditorViewModel)window.FindControl<KeyboardTab>("KeyboardPanel")!.DataContext!;
        var pa1 = editor.Rows.Single(row => row.Title == "PA1");

        window.Close();
        keymap.Bind(new KeyChord(Key.F9, KeyModifiers.Alt), new KeymapAction.SendKey(TerminalKey.PA1));

        Assert.Single(pa1.Chips);
    }

    [AvaloniaFact]
    public void Preferences_opened_through_the_seam_gets_an_in_memory_keymap()
    {
        var app = (App)Avalonia.Application.Current!;
        var window = app.ShowPreferences(new SettingsViewModel());
        try
        {
            Assert.NotNull(window.FindControl<KeyboardTab>("KeyboardPanel")!.DataContext);
        }
        finally
        {
            window.Close();
        }
    }
```

Match the surrounding tests' style for obtaining `app` and cleaning up (see the existing `ShowPreferences` tests near the end of the file, around lines 334–370); reuse their pattern rather than the sketch above if it differs. `using LizTerm.Core.Session;` is needed for `TerminalKey`.
Append to `KeyboardTabTests.cs` the integration test (add `using LizTerm.App.Tests.Fakes;`, `using LizTerm.Core.Settings;`):

```csharp
    [AvaloniaFact]
    public void A_binding_made_in_the_tab_reaches_an_open_session_window_the_screen_and_the_keypad()
    {
        var keymap = new KeymapViewModel();
        var session = new FakeEmulatorSession
        {
            Profile = new SessionProfile { Name = "TSO", Host = "tk5.local", Port = 3270 },
        };
        var sessionWindow = new SessionWindow(MenuStyle.InWindow, isMacOS: false);
        sessionWindow.AttachKeymap(keymap);
        sessionWindow.DataContext = new SessionViewModel(session, action => action(), new FakeTextClipboard());
        sessionWindow.Show();
        ((SessionViewModel)sessionWindow.DataContext).Settings.Keypad = true;

        var preferences = new PreferencesWindow(new SettingsViewModel(), keymap, systemAlertAvailable: true, menuStyleChoosable: true);
        preferences.Show();
        var tab = preferences.FindControl<KeyboardTab>("KeyboardPanel")!;
        var editor = (KeymapEditorViewModel)tab.DataContext!;
        preferences.UpdateLayout();
        var slot = SlotOf(RowContainer(tab, editor, "PA1"));
        slot.Focus();
        preferences.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        preferences.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        preferences.KeyPressQwerty(PhysicalKey.Home, RawInputModifiers.Control);

        var screen = sessionWindow.FindControl<TerminalScreen>("Screen")!;
        Assert.True(screen.Keymap.TryMap(new KeyChord(Key.Home, KeyModifiers.Control), out var key));
        Assert.Equal(TerminalKey.PA1, key);
        Assert.True(preferences.IsVisible);
    }
```

Check the exact names in `SessionWindowKeymapTests.cs` for `FakeEmulatorSession`, the `SessionProfile` initializer and how the keypad's tooltip is read, and copy that; extend the assertion with the keypad tooltip check from `A_rebind_reaches_the_screen_and_the_keypad_tooltip` (same file) if the helper is reusable.

- [ ] **Step 2: Run to see them fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~PreferencesWindowTests|FullyQualifiedName~KeyboardTabTests"`
Expected: build error on the four-argument `PreferencesWindow` constructor.

- [ ] **Step 3: Implement**

`PreferencesWindow.axaml`: add `xmlns:views="using:LizTerm.App.Views"` to the root; update the comment block's "Four tabs" to "Five tabs" and its last sentence to "A new setting joins the tab it belongs to; a new area (logging) is a new tab. The Keyboard tab is the exception to the rows-of-grids shape: a `KeyboardTab` with its own data context."; after the Window `TabItem` and before `</TabControl>` add:

```xml
      <TabItem Header="Keyboard">
        <!-- Its own data context, the KeymapEditorViewModel the constructor builds (#18). -->
        <views:KeyboardTab x:Name="KeyboardPanel" />
      </TabItem>
```

`PreferencesWindow.axaml.cs`: add `using Avalonia;`-free imports for `LizTerm.App.Keyboard` and the `GetPlatformSettings` extension (mirror `SessionSwitcher.axaml.cs`'s usings: `Avalonia.Controls`, `Avalonia.VisualTree`; if `GetPlatformSettings` does not resolve, look at `TerminalScreen.cs`'s usings). Replace the constructors and add the close hook:

```csharp
    private readonly KeymapEditorViewModel _keyboard;

    /// <summary>Design-time only, in the full shape.</summary>
    public PreferencesWindow() : this(new SettingsViewModel(), new KeymapViewModel(), systemAlertAvailable: true, menuStyleChoosable: true) { }

    /// <summary>Whether the system alert can ring here, and whether the menu style is a choice at all, are both
    /// arguments (App passes its ringer's CanRing and the platform), so a test can see every shape of the window
    /// on any machine. The keymap is the process's one (App.Keymap) or, in a test, an in-memory one.</summary>
    internal PreferencesWindow(SettingsViewModel settings, KeymapViewModel keymap, bool systemAlertAvailable, bool menuStyleChoosable)
    {
        InitializeComponent();
        DataContext = settings;
        BellSoundSystemAlert.IsEnabled = systemAlertAvailable;
        BellSoundNote.IsVisible = !systemAlertAvailable;
        MenuStyleGroup.IsVisible = menuStyleChoosable;
        // The reserved gestures are asked at each capture, as TerminalScreen asks at each key, so they are the
        // platform's answer for this window and never a copy taken before it had a platform.
        _keyboard = new KeymapEditorViewModel(keymap, () => PlatformHotkeys.From(this.GetPlatformSettings()?.HotkeyConfiguration));
        KeyboardPanel.DataContext = _keyboard;
    }

    /// <summary>The editor listens to the process's keymap for as long as the tab exists.</summary>
    protected override void OnClosed(EventArgs e)
    {
        _keyboard.Dispose();
        base.OnClosed(e);
    }
```

`App.axaml.cs`: change the two methods:

```csharp
    public void ShowPreferences() => ShowPreferences(Settings, Keymap);

    /// <summary>The rule with the settings and the keymap as arguments, so a test can exercise it without the real
    /// files; a null keymap is an in-memory one, so a test that does not care never opens keymap.json.</summary>
    internal PreferencesWindow ShowPreferences(SettingsViewModel settings, KeymapViewModel? keymap = null)
    {
        ...
        var window = new PreferencesWindow(
            settings,
            keymap ?? new KeymapViewModel(),
            _bellRinger.CanRing(BellSound.SystemAlert),
            MenuStrategy.MenuStyleChoosable(OperatingSystem.IsMacOS()));
        ...
    }
```

(leave the rest of the body as it is).

- [ ] **Step 4: Run to see them pass**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~PreferencesWindowTests|FullyQualifiedName~KeyboardTabTests"`
Expected: PASS. Then run the whole App suite once: `dotnet test tests/LizTerm.App.Tests` (the engine-dependent live tests skip themselves; a fresh worktree has no `native/out`, see `docs/development.md`, so only engine-dependent tests may fail for that reason, and they must fail the same way on `main`).

- [ ] **Step 5: Commit**

```bash
git add src/LizTerm.App/Views/PreferencesWindow.axaml src/LizTerm.App/Views/PreferencesWindow.axaml.cs src/LizTerm.App/App.axaml.cs tests/LizTerm.App.Tests/Views/PreferencesWindowTests.cs tests/LizTerm.App.Tests/Views/KeyboardTabTests.cs
git commit -m "App: Preferences gets a Keyboard tab over the process's keymap (#18)

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

### Task 7: The guide, its table test, the changelog and the notes for the next Claude

**Files:**
- Create: `tests/LizTerm.App.Tests/Documentation/UserGuideKeyboardTableTests.cs`
- Modify: `docs/user-guide.md`
- Regenerate: `src/LizTerm.App/Assets/Docs/user-guide.html`
- Modify: `CHANGELOG.md`
- Modify: `src/LizTerm.App/CLAUDE.md`, `tests/CLAUDE.md`

**Interfaces:**
- Consumes: `DefaultKeymap.Create(bool)`, `ChordSyntax.Format`, `KeyChord`.
- Produces: `UserGuideKeyboardTableTests`, the spec §6.1 test (as adjusted in "Where this plan differs" 5).

- [ ] **Step 1: Write the guide table test**

It is expected to pass at once: the guide's table already matches `DefaultKeymap` today. Its job is to fail the day either drifts. To prove it can fail, Step 2 breaks the guide on purpose.

`tests/LizTerm.App.Tests/Documentation/UserGuideKeyboardTableTests.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Avalonia.Input;
using LizTerm.App.Keyboard;
using LizTerm.Core.Session;

namespace LizTerm.App.Tests.Documentation;

/// <summary>Holds the user guide's Keyboard table to DefaultKeymap (editable keymap spec §6.1, #48): every default
/// chord appears in the row for its key, and no row claims a chord the default lacks. The guide is hand-ordered
/// prose in a table, so the table is read with a small fixed vocabulary; a row that says something this reader does
/// not know fails with the row's own words, and the fix is to extend the vocabulary or reword the row. The live
/// table is the Keyboard tab; this is what keeps the guide's copy of the defaults honest.</summary>
public partial class UserGuideKeyboardTableTests
{
    private static readonly Dictionary<string, Key> KeyWords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Enter"] = Key.Enter, ["Escape"] = Key.Escape, ["Pause"] = Key.Pause, ["Page Up"] = Key.PageUp,
        ["Page Down"] = Key.PageDown, ["Home"] = Key.Home, ["End"] = Key.End, ["Insert"] = Key.Insert,
        ["Delete"] = Key.Delete, ["Tab"] = Key.Tab, ["R"] = Key.R, ["I"] = Key.I,
        ["1"] = Key.D1, ["2"] = Key.D2, ["3"] = Key.D3, ["6"] = Key.D6, ["["] = Key.OemOpenBrackets,
    };

    private static readonly KeyChord Backspace = new(Key.Back);

    [GeneratedRegex(@"^F(\d{1,2})$")]
    private static partial Regex FunctionKey();

    [Fact]
    public void The_guides_keyboard_table_is_the_default_keymap()
    {
        var keyClaims = new Dictionary<KeyChord, TerminalKey>();
        var textClaims = new Dictionary<KeyChord, string>();
        var sawBackspace = false;

        foreach (var (keys, action) in Rows())
        {
            if (keys == "Backspace")
            {
                sawBackspace = true;
                continue;
            }
            var groups = Groups(keys);
            if (action.StartsWith("Types `", StringComparison.Ordinal))
            {
                var text = action.Split('`')[1];
                foreach (var chord in groups.SelectMany(g => g))
                    Assert.True(textClaims.TryAdd(chord, text), $"The guide lists {ChordSyntax.Format(chord)} twice.");
                continue;
            }
            var actions = Actions(action, keys);
            foreach (var group in groups)
            {
                Assert.True(actions.Count == 1 || group.Count == actions.Count,
                    $"Guide row '{keys}': {group.Count} keys against {actions.Count} 3270 keys.");
                for (var i = 0; i < group.Count; i++)
                    Assert.True(keyClaims.TryAdd(group[i], actions.Count == 1 ? actions[0] : actions[i]),
                        $"The guide lists {ChordSyntax.Format(group[i])} twice.");
            }
        }

        var erasing = DefaultKeymap.Create(destructiveBackspace: true);
        var cursorLeft = DefaultKeymap.Create(destructiveBackspace: false);
        Assert.True(sawBackspace, "The guide's Keyboard table has no Backspace row.");
        Assert.Equal(Lines(erasing.Keys.Where(pair => pair.Key != Backspace)), Lines(keyClaims));
        Assert.Equal(TextLines(erasing.Text), TextLines(textClaims));
        // Backspace is the profile's choice, so the guide's one row stands for both defaults.
        Assert.Equal(TerminalKey.Erase, erasing.Keys[Backspace]);
        Assert.Equal(TerminalKey.Backspace, cursorLeft.Keys[Backspace]);
        Assert.Equal(Lines(erasing.Keys.Where(pair => pair.Key != Backspace)), Lines(cursorLeft.Keys.Where(pair => pair.Key != Backspace)));
    }

    private static IEnumerable<string> Lines(IEnumerable<KeyValuePair<KeyChord, TerminalKey>> pairs) =>
        pairs.Select(pair => $"{ChordSyntax.Format(pair.Key)} = {pair.Value}").Order(StringComparer.Ordinal);

    private static IEnumerable<string> TextLines(IEnumerable<KeyValuePair<KeyChord, string>> pairs) =>
        pairs.Select(pair => $"{ChordSyntax.Format(pair.Key)} types {pair.Value}").Order(StringComparer.Ordinal);

    /// <summary>The table's rows as (left cell, right cell), header and separator dropped.</summary>
    private static List<(string Keys, string Action)> Rows()
    {
        var lines = File.ReadAllText(GuidePath()).ReplaceLineEndings("\n").Split('\n');
        var start = Array.IndexOf(lines, "## Keyboard");
        Assert.True(start >= 0, "docs/user-guide.md has no '## Keyboard' section.");
        var rows = new List<(string, string)>();
        var inTable = false;
        foreach (var line in lines.Skip(start + 1))
        {
            if (line.StartsWith("## ", StringComparison.Ordinal)) break;
            if (!line.StartsWith('|'))
            {
                if (inTable) break;
                continue;
            }
            inTable = true;
            var cells = line.Trim().Trim('|').Split('|').Select(cell => cell.Trim()).ToArray();
            if (cells[0] == "Key" || cells[0].StartsWith("---", StringComparison.Ordinal)) continue;
            rows.Add((cells[0], cells[1]));
        }
        Assert.NotEmpty(rows);
        return rows;
    }

    /// <summary>A left cell as groups of chords: each comma or "or" item is a group; a range ("F1 – F12"), a slash
    /// pair ("Tab / Shift+Tab") and "Arrow keys" are one group of several chords, paired position by position with
    /// the right cell.</summary>
    private static List<List<KeyChord>> Groups(string cell)
    {
        var items = cell.Replace(", or ", ", ")
            .Split([", ", " or "], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return [.. items.Select(item => Group(item, cell))];
    }

    private static List<KeyChord> Group(string item, string row)
    {
        if (item == "Arrow keys") return [new(Key.Up), new(Key.Down), new(Key.Left), new(Key.Right)];
        if (item.Contains(" – ", StringComparison.Ordinal))
        {
            var ends = item.Split(" – ");
            var first = Chord(ends[0], row);
            var last = KeyOf(ends[1], row);
            return [.. Enumerable.Range((int)first.Key, (int)last - (int)first.Key + 1).Select(k => new KeyChord((Key)k, first.Modifiers))];
        }
        return [.. item.Split(" / ").Select(part => Chord(part, row))];
    }

    private static KeyChord Chord(string spelling, string row)
    {
        var text = spelling.Trim();
        if (text.StartsWith("a tap of ", StringComparison.OrdinalIgnoreCase))
        {
            return KeyChord.TapOf(text["a tap of ".Length..] switch
            {
                "Left Ctrl" => Key.LeftCtrl,
                "Right Ctrl" => Key.RightCtrl,
                var other => throw Unreadable(row, other),
            });
        }
        var parts = text.Split('+');
        var modifiers = KeyModifiers.None;
        foreach (var part in parts[..^1])
        {
            modifiers |= part switch
            {
                "Ctrl" => KeyModifiers.Control,
                "Alt" => KeyModifiers.Alt,
                "Shift" => KeyModifiers.Shift,
                _ => throw Unreadable(row, part),
            };
        }
        return new KeyChord(KeyOf(parts[^1], row), modifiers);
    }

    private static Key KeyOf(string word, string row)
    {
        if (KeyWords.TryGetValue(word.Trim(), out var key)) return key;
        if (FunctionKey().Match(word.Trim()) is { Success: true } match) return Key.F1 + int.Parse(match.Groups[1].Value) - 1;
        throw Unreadable(row, word);
    }

    /// <summary>A right cell as 3270 keys, in order: a range, a slash pair, "Move the cursor", or one name.</summary>
    private static List<TerminalKey> Actions(string cell, string row)
    {
        if (cell == "Move the cursor") return [TerminalKey.Up, TerminalKey.Down, TerminalKey.Left, TerminalKey.Right];
        if (cell.Contains(" – ", StringComparison.Ordinal))
        {
            var ends = cell.Split(" – ").Select(name => NameOf(name, row)).ToArray();
            return [.. Enumerable.Range((int)ends[0], (int)ends[1] - (int)ends[0] + 1).Select(k => (TerminalKey)k)];
        }
        return [.. cell.Split(" / ").Select(name => NameOf(name, row))];
    }

    private static TerminalKey NameOf(string name, string row)
    {
        var text = name.Trim();
        return text switch
        {
            "Back Tab" => TerminalKey.BackTab,
            "Toggle insert mode" => TerminalKey.Insert,
            "Erase EOF" => TerminalKey.EraseEof,
            _ when text.All(char.IsAsciiLetterOrDigit) && Enum.TryParse<TerminalKey>(text, out var key) && Enum.IsDefined(key) => key,
            _ => throw Unreadable(row, text),
        };
    }

    private static InvalidOperationException Unreadable(string row, string what) =>
        new($"Guide Keyboard table, row '{row}': cannot read '{what}'. Extend UserGuideKeyboardTableTests' vocabulary or reword the row.");

    private static string GuidePath([CallerFilePath] string path = "")
    {
        var dir = Path.GetDirectoryName(path);
        while (dir is not null && !File.Exists(Path.Combine(dir, "LizTerm.slnx"))) dir = Path.GetDirectoryName(dir);
        return Path.Combine(dir ?? throw new InvalidOperationException($"No LizTerm.slnx above '{path}'."), "docs", "user-guide.md");
    }
}
```

- [ ] **Step 2: Run it, then prove it can fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~UserGuideKeyboardTableTests"`
Expected: PASS today. Then temporarily change the guide's `| Alt+1 | PA1 |` row to `| Alt+9 | PA1 |`, rerun, and expect FAIL naming `Alt+1 = PA1` and `Alt+9 = PA1`. Revert that edit before Step 3.

- [ ] **Step 3: The user guide**

Edit `docs/user-guide.md` (all new prose without em dashes; the guide's HTML converter handles paragraphs, bullets, bold, inline code, links, tables and fences, which is everything below).

a) The Keyboard section's opening paragraph (currently "The default layout follows Vista TN3270, cross-checked against wc3270. To change a binding, edit `keymap.json` by hand; see [Where LizTerm keeps its files](#where-lizterm-keeps-its-files).") becomes:

```
The table below is the default layout. It follows Vista TN3270, cross-checked against wc3270. You can change any
binding in **Preferences > Keyboard**; see [Changing a binding](#changing-a-binding) below the table.
```

b) After the paragraph "On a Mac, Alt is the Option key. A "tap" means pressing and releasing the key on its own, with nothing else in between." and before "Copy, Paste, Select All and Find use...", nothing changes. After the paragraph that ends "...see [Several sessions](#several-sessions)." and before "The **Keys** menu sends...", insert nothing. After the **Keys** menu paragraph (the last one in the section), add:

```
### Changing a binding

**Preferences > Keyboard** lists every 3270 key with the keys that send it. Changes apply at once, to every open
session window, and the keypad's tooltips follow them.

- **Add a key**: click **Add** on the row, or Tab to it and press Enter or Space, then press the key you want. A tap
  of Left or Right Ctrl is captured the same way: press and release the Ctrl key on its own.
- **Remove a key**: click the **×** on its chip.
- **Move a key**: add it to another row. It leaves the row it was on, and the slot says where it came from.
- **Reset to defaults** puts back the table above and clears every change you made.

LizTerm will not bind a key it needs, and says why in the slot: the platform's Copy, Paste and Select All shortcuts,
Find and Switch Session; any key with Cmd, or the Windows key; a letter, digit, punctuation mark or Space on its own
or with Shift, which would stop you typing it; and, on Windows and Linux, Ctrl+Alt with one of those, which is how
some keyboards type characters such as @. Everything else can be bound, Escape and Tab included.

Backspace follows the profile's Backspace setting until you bind it here. The two Backspace rows are the two things
it can do: erase the character to the left, or move the cursor left.
```

Check that the heading level renders: the guide's converter supports `###` (it is used elsewhere in the guide; `grep -n '^### ' docs/user-guide.md`). If it does not, use a bold lead-in line instead and drop the `#changing-a-binding` link.

c) The Preferences section: change "The settings sit on four tabs:" to "The settings sit on five tabs:", and after the Window tab's last bullet (the **Keypad** one) add:

```

**Keyboard**

- One row for each 3270 key, with the keys that send it and an **Add** slot. See [Changing a binding](#changing-a-binding).
- **Reset to defaults** clears every change. The tab also says how many entries in `keymap.json` it could not read,
  and keeps them as written.
```

d) "Where LizTerm keeps its files": change the first sentence of the `keymap.json` paragraph to "`keymap.json` holds your keyboard bindings, written by Preferences > Keyboard, only the ones that differ from the table under [Keyboard](#keyboard). You can edit it by hand too, in this form: each entry is a chord, ..." (keep the rest, including "LizTerm reads the file once, when the first session window opens, so restart it after editing by hand"; that is still true of hand edits).

e) Known limitations: delete the bullet beginning `- **No in-app keymap editor yet**`.

- [ ] **Step 4: Regenerate the bundled guide and gate it**

```bash
LIZTERM_UPDATE_DOCS=1 dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~UserGuideAssetTests"
dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~UserGuideAssetTests"
dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~UserGuide"
```

Expected: all three green; `git diff --stat` shows `docs/user-guide.md` and `src/LizTerm.App/Assets/Docs/user-guide.html` changed. If `The_guide_uses_no_construct_the_converter_cannot_render` fails, reword the offending line (docs/development.md: extend the converter only if the construct is worth having).

- [ ] **Step 5: Changelog and notes**

`CHANGELOG.md`, under `## Unreleased`, replace the "Keyboard bindings." bullet with:

```
- **Keyboard bindings.** **Preferences > Keyboard** lists every 3270 key with the keys that send it: add a key by
  pressing it, remove one with its **×**, move one to another row, or reset to the defaults. LizTerm refuses the keys
  it needs itself, such as Copy and Paste or a plain letter, and says why. Your changes are kept in `keymap.json`
  beside `settings.json`, and the on-screen keypad's tooltips follow them
  ([#18](https://github.com/coffeemuse/LizTerm/issues/18)).
```

`src/LizTerm.App/CLAUDE.md`:
- In "Settings and Preferences", change "**The window is four tabs** — General, Display, Bell, Window —" to "**The window is five tabs** — General, Display, Bell, Window, Keyboard —", change "and #79's logging and #18's keyboard are next" to "and #79's logging is next", and append this sentence to the bullet: "Keyboard is the one tab that is not rows of grids: `Views/KeyboardTab` is a `UserControl` whose data context is a `KeymapEditorViewModel`, not the window's `SettingsViewModel`, so the constructor sets it and `OnClosed` disposes it (the editor listens to the process's keymap for as long as it exists)."
- In "Keyboard", replace "`KeymapPolicy` is what the Keyboard tab (#18, PR 2) refuses, with the reason." with:

```
  `KeymapPolicy` is what the Keyboard tab refuses, with the reason. The tab is `KeymapEditorViewModel` (a
  `KeymapRow` per action, a `KeymapChip` per chord, all answered from `KeymapViewModel`'s `ChordsFor`/`ActionOf`,
  never its dictionary) laid out by `Views/KeyboardTab`. A row's `TryCapture` calls `KeymapPolicy.Check` before every
  `Bind`, which is what keeps a Cmd chord out of the file, and answers the slot with a `CaptureResult`. There are two
  Backspace rows because `TerminalKey` has two Backspace actions. A "Type ¬" row stays for the tab's life once shown.
  `Controls/ChordCaptureBox` is the Add slot, a `Button` subclass: it needs `StyleKeyOverride => typeof(Button)` or
  it has no template, which also means a style selector cannot name it (the tab styles the `chord-slot` class and
  `:armed` pseudo-class). It is armed by a click or Enter or Space and not by focus, because an armed slot swallows
  Tab and a slot armed on focus would trap a keyboard user; armed, it captures Escape, Tab and Enter like any other
  chord, and a Ctrl key pressed and released alone through its own `ModifierTapDetector`.
  `UserGuideKeyboardTableTests` holds the guide's Keyboard table to `DefaultKeymap` as chord sets.
```

`tests/CLAUDE.md`, in the App tests bullets, add:

```
- A Keyboard tab test finds a row's slot with `RowList.ContainerFromIndex` then `GetVisualDescendants().OfType<ChordCaptureBox>()`,
  focuses it, arms it with Enter (press and release), and presses the chord to bind with `KeyPressQwerty`. Use a chord
  every platform accepts (`Alt+F9`, or a plain `A` for a refusal): the headless platform's hotkey configuration is not
  ours to assert. Editor tests pass an explicit `KeyGestureFormatInfo` and a `Func<PlatformHotkeys>`.
```

- [ ] **Step 6: Commit**

```bash
git add tests/LizTerm.App.Tests/Documentation/UserGuideKeyboardTableTests.cs docs/user-guide.md src/LizTerm.App/Assets/Docs/user-guide.html CHANGELOG.md src/LizTerm.App/CLAUDE.md tests/CLAUDE.md
git commit -m "Docs: the guide describes the Keyboard tab, a test holds its table to DefaultKeymap, changelog and notes (#18)

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

### Task 8: Release gates and the hand-off

**Files:** none new.

- [ ] **Step 1: The whole suite**

```bash
dotnet test LizTerm.slnx
```

Expected: green apart from tests that need a built engine in `native/out` (a fresh worktree has none; `docs/development.md` says how to seed one, and those tests must fail identically on `main`). Report the counts.

- [ ] **Step 2: The zero-warning gate**

```bash
dotnet build LizTerm.slnx --no-incremental 2>&1 | grep -c " warning "
```

Expected: `0`. A common hit is CS8618/CS8625 nullability in the new view models and an unused `using`.

- [ ] **Step 3: Drive the tab in the real app (headless tests cannot see this)**

Run `dotnet run --project src/LizTerm.App` (a Homebrew `b3270` through `LIZTERM_B3270_PATH` is enough for this; no host is needed for the picker), open Preferences (Cmd+, on macOS), choose **Keyboard**, and check, on macOS:

1. The window is not clipped; the list scrolls; **Reset to defaults** and the banner area sit under it.
2. Tab to an **Add** slot with the keyboard alone, press Enter, press `Alt+F9`: a chip appears and Tab moves on. (This is the trap the spec's arm-on-focus would have had.)
3. Click an **Add** slot, then click it again: it disarms. Click it and click somewhere else: it disarms.
4. Press a plain letter while armed: the slot says why and stays armed. Press Cmd+comma: refused as a Cmd shortcut. Press Cmd+C: refused as Copy.
5. Press and release Right Ctrl alone while armed on a row: a "a tap of Right Ctrl" chip appears (and "Moved from Enter" if you used another row).
6. Open a session window: a rebind made in the tab changes what the key sends, and the keypad's tooltip, at once.
7. Option+1 still sends PA1 after an unrelated edit (the Alt row spells `Alt+1` in the file).

If the Avalonia DevTools MCP is used for this, read the DevTools section of `src/LizTerm.App/CLAUDE.md` first. If the display is asleep and the app refuses to start (`RenderTimer -6661`), hand the checklist to Robert instead of retrying.

- [ ] **Step 4: The PR**

Branch off the current head, push, open the PR against `main`. The description (recorded review fixes go in a PR comment, not here) must lead with the six deviations from "Where this plan differs from the spec" so Robert can accept or reverse each, list the seven manual checks above with what was and was not observed, and end with `🤖 Generated with [Claude Code](https://claude.com/claude-code)`. Do not close #18 or #23: PR 3 does. Note that Robert's PR 1 live check (keymap.json read once at the first session window, so a hand edit needs a restart; PR 1's description says "reopen", which is wrong) is still outstanding.

---

## Self-review

**Spec coverage.** §5.3: rows in order (Task 2, with the Backspace deviation), row anatomy (Task 5), the Add slot with chord capture, taps, refusal display, "Moved from" and focus loss (Tasks 3 and 4, with arming as a deviation), Reset, banner and unreadable note (Tasks 3 and 5), the tab never touching the dictionary (Task 2 uses `ChordsFor`/`ActionOf`), chips through `KeymapHints` (Task 1). §5.4: `PreferencesWindow` takes the `KeymapViewModel`, design-time constructor, `App.ShowPreferences` (Task 6). §6.1: the guide's Keyboard and Preferences text, the limitation bullet, the file docs (already in PR 1) and the table test (Task 7). §7.1 `KeymapViewModel` and policy tests were PR 1; the tab's are Tasks 2 to 5. §7.2 headless: the slot (Task 4), the rebind reaching screen and keypad (Task 6). §7.2's Keys-menu parity guard is PR 3. §7.3 live checks: Task 8, macOS items; the Windows and Linux items wait for a machine, and the PR must say so.

**Placeholder scan.** No TBD. Two steps name a check instead of a fixed string because the code cannot be known without opening a file: Task 6 Step 1 (copy the neighbouring `ShowPreferences` test's app/cleanup pattern and `SessionWindowKeymapTests`' fake setup) and Task 7 Step 3b (whether `###` renders); each says exactly what to look at and what to do either way.

**Type consistency.** `CaptureResult(bool Accepted, string? Message)` (Task 2) is what `KeymapRow.TryCapture` returns and `ChordCaptureBox.CaptureHandler` takes (Tasks 3, 4). `KeymapRow.CaptureHandler` is `Func<KeyChord, CaptureResult>`, bound to `ChordCaptureBox.CaptureHandler` (Task 5). `KeymapEditorViewModel(KeymapViewModel, Func<PlatformHotkeys>, IFormatProvider? = null)` is used identically in Tasks 2, 3, 5 and 6. `KeymapRow.Title`/`AddName`/`Note`/`HasNote`/`Chips`, `KeymapChip.Text`/`RemoveName`/`RemoveCommand`/`Chord`, editor `Rows`/`ResetCommand`/`SaveError`/`HasSaveError`/`UnreadableNote`/`HasUnreadable` match between the view models, the XAML bindings and the tests. `PreferencesWindow(SettingsViewModel, KeymapViewModel, bool, bool)` matches its callers in `App` and the test helper.
