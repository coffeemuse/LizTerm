# LizTerm: the on-screen keypad

Date: 2026-09-11. Parent spec: `2026-09-03-lizterm-v1-design.md`. Predecessors:
`2026-09-11-lizterm-settings-store-design.md` (#19/#55, merged as PR #67), which built the store this spec adds
fields to, and `2026-09-11-lizterm-bell-design.md` (#47, merged as PR #68), whose bool-plus-enum settings shape
this follows. Issue: #31.
Status: approved in discussion on 2026-09-11; awaiting review of this text.

## 1. Purpose

The Keys menu is the only route to the 3270 keys a keyboard cannot reach, and it is a menu: two clicks deep, and
not something anyone uses while actually working. The audience case is concrete. No laptop has F13 to F24, PA1 has
no obvious key at all, and the fact that Alt+1 is PA1 lives in `DefaultKeymap` and the user guide's keyboard table
and nowhere a user sees while typing. A visible keypad is the version that needs no teaching, which is the v1
success criterion: a hobbyist gets working without reading a manual.

Decisions taken in the brainstorm on 2026-09-11, each justified in the section named:

- **A docked panel, off by default, and a global preference** (§2, §6.3). View > Keypad in any session window
  shows it in every window and the choice survives relaunch, exactly as View > Crosshair does. The issue said
  persistence "belongs with #19"; #19's store now exists, so it belongs here.
- **Dockable at the bottom or on the right, chosen in Preferences** (§2, §4.3, §7). Which edge costs less depends
  on the display: a wide screen has width to spare beside an 80-column screen, a laptop has height to spare
  beneath it.
- **Thirty-six keys in three banks of twelve** (§3): the issue's list plus Erase Input, Dup and Field Mark.
- **A control that mirrors `TerminalScreen`'s contract** (§4): it raises `KeyRequested` and knows nothing about
  view models or sessions. Two shapes were rejected: buttons written into the session window, whose XAML and
  code-behind are already the largest in the project and which would leave nothing testable on its own; and a
  view-model-driven collection of button commands, which would put an Avalonia formatter into a view model and
  add a command shape the project deliberately avoids for keystrokes.
- **Buttons take no focus and call the view model's method, never the command** (§4.1).
- **Tooltips from the live keymap, through Avalonia's platform formatter** (§5). Tooltips on a `Button` are
  display only, so the gesture trap #23 documents does not apply, and deriving them from a `Keymap` means a
  future remap (#18) cannot make them lie.
- **A keypad click ends a modifier tap in progress** (§6.2).

## 2. Settings

### 2.1 The fields

`AppSettings` (`src/LizTerm.Core/Settings/AppSettings.cs`) gains two positional parameters, last, each with a
default, flat as the settings spec requires:

```csharp
public sealed record AppSettings(
    [property: JsonConverter(typeof(JsonStringEnumConverter<CrosshairMode>))] CrosshairMode Crosshair = CrosshairMode.None,
    bool Blink = true,
    bool VisualBell = true,
    [property: JsonConverter(typeof(JsonStringEnumConverter<BellSound>))] BellSound BellSound = BellSound.None,
    bool Keypad = false,
    [property: JsonConverter(typeof(JsonStringEnumConverter<KeypadDock>))] KeypadDock KeypadDock = KeypadDock.Bottom);
```

`KeypadDock` is a new enum in `src/LizTerm.Core/Settings/KeypadDock.cs`, beside `CrosshairMode` and `BellSound`:

```csharp
public enum KeypadDock { Bottom, Right }
```

Written by name, so the file reads `"keypadDock": "Right"` and a reordering of the enum can never change a saved
meaning. `SettingsJsonContext` needs no change: the generator follows the record's properties.

**Why two fields.** Whether the keypad is shown and where it goes are independent choices, as the bell's flash and
sound are. One enum with a `Hidden` member would forget the position every time the keypad was hidden, and View >
Keypad would have to remember it somewhere else.

**Growth in both directions.** An older file lacks both keys and reads as off, at the bottom. An older build
ignores both. A file saying a dock this build does not know, say `"Left"` from a later one, reads as `Bottom` for
that key alone with every other key intact, because `SettingsLayers.Read` drops a key it cannot parse on its own.
That is the guarantee the settings spec built and the bell spec relied on.

### 2.2 The view model

`SettingsViewModel` gains `Keypad` and `KeypadDock` properties in the shape `Blink` has: a setter that returns
early when nothing changed, otherwise `Apply` with the property name and a `with` expression.

`KeypadDockConverter` joins `CrosshairModeConverter` and `BellSoundConverter` in `ViewModels/`, a two-line
subclass of `EnumIsConverter<KeypadDock>` with an `Instance`, for the Preferences radios (§7).

## 3. The layout table

`src/LizTerm.App/Controls/KeypadLayout.cs`, pure data:

```csharp
public readonly record struct KeypadKey(string Label, TerminalKey Key);

public static class KeypadLayout
{
    public const int BankSize = 12;
    /// <summary>Three banks of BankSize: rows at the bottom, columns on the right (Keypad.Dock).</summary>
    public static IReadOnlyList<IReadOnlyList<KeypadKey>> Banks { get; }
}
```

| Bank | Keys |
|---|---|
| 1 | PF1 to PF12 |
| 2 | PF13 to PF24 |
| 3 | PA1, PA2, PA3, Enter, Clear, Reset, Attn, SysReq, Erase EOF, Erase Input, Dup, Field Mark |

That is the issue's list plus three. Dup and Field Mark joined the Keys menu after the issue was written (#16),
and a keypad offering less than the menu would be odd. Erase Input is the one member of `TerminalKey` that
nothing binds today, neither `DefaultKeymap` nor the menu, and it is the key that clears every unprotected field
on a screen; the keypad is where it becomes reachable. Twelve in the third bank is also what makes the grid
rectangular in both orientations.

Labels are the Keys menu's own words, "Field Mark" and "SysReq" included, so the two never disagree. A test walks a
real window's Keys menu and asserts every `TerminalKey` it offers is in a bank (§8.2), the same guard against
drift the project keeps between other pairs of lists.

## 4. The `Keypad` control

### 4.1 Structure

`src/LizTerm.App/Controls/Keypad.axaml` and `.axaml.cs`, a `UserControl` beside `TerminalScreen`. The XAML is a
`Border` in the status bar's dark grey (`#181818`, padding 6 by 4) holding a `UniformGrid` named `Grid`. The
constructor fills the grid from `KeypadLayout`, one `Button` per entry, so no button is written by hand anywhere.

```csharp
public partial class Keypad : UserControl
{
    public static readonly StyledProperty<KeypadDock> DockProperty;   // default Bottom (§4.3)
    public static readonly StyledProperty<Keymap> KeymapProperty;     // default DefaultKeymap.Create(true) (§4.4)
    /// <summary>The window routes this to the view model, exactly as it does TerminalScreen.KeyRequested.</summary>
    public event EventHandler<TerminalKey>? KeyRequested;
}
```

Four rules on every button:

- **`Focusable` is false.** A click never moves keyboard focus off the screen, so typing continues without a
  flicker and `ModifierTapDetector` keeps its state. The window refocuses the screen after every key regardless
  (§6.2), as the guarantee the issue asked for; the rule here is what makes that a no-op in the ordinary case.
  A consequence, and a deliberate one: the keypad cannot be tabbed to. It is a pointing device for the mouse.
- **One click handler for all of them**, reading the `TerminalKey` stored on the button and raising
  `KeyRequested`. Not a `Command`: a `[RelayCommand]` disables while it runs, which is right for a menu item and
  wrong for a keypad someone clicks twice in quick succession. This is the distinction the view model's own
  documentation draws between `SendKeyAsync` and `SendKeyCommand`, and the keypad takes the screen's side of it.
- **Compact.** Minimum height 24, padding 4 by 0, margin 1, font size 12 in the UI font, not the terminal font,
  content centred. Three rows come to roughly 90 pixels.
- **A tooltip naming the keyboard equivalent**, when there is one (§5). Erase Input, Dup and Field Mark have
  none in the default keymap and get no tooltip.

### 4.2 Sizing

At the bottom the grid stretches to the window's width, so each button is a twelfth of it. At the window's
minimum width of 480 that is 40 pixels and "Erase Input" clips; accepted, because the minimum is a floor rather
than a size anyone works at. On the right the control takes its natural width, three columns each as wide as the
widest label, roughly 240 pixels, and top-aligns its twelve rows beside the screen rather than stretching the
buttons to fill the height.

In either position the screen refits its font to what is left, through `CellGeometry.Fit`, as it does for the find
bar today. At the default window size an 80 by 24 screen is width-limited and leaves about 120 pixels of letterbox
above and below it, so the keypad at the bottom costs no font size at all there.

### 4.3 Dock

`Dock`, a styled property of type `KeypadDock`, lays the same grid out two ways:

| Dock | `UniformGrid.Columns` | Order of the children |
|---|---|---|
| `Bottom` | 12 | bank-major: each bank is a row |
| `Right` | 3 | index-major: each bank is a column, so PF1 to PF12 read down the first |

A change rebuilds the grid's children from the same 36 buttons in the other order. One arrangement rotated, so a
user learns it once.

The control also carries `public static readonly IValueConverter DockPanelDock`, a `FuncValueConverter` from
`KeypadDock` to Avalonia's `Dock`, for the window's `DockPanel.Dock` binding (§6.1). The control does not set its
own attached `DockPanel.Dock`: where it sits in the window is the window's decision.

### 4.4 Keymap

`Keymap`, a styled property defaulting to `DefaultKeymap.Create(destructiveBackspace: true)`. A change rebuilds
every tooltip. Nothing binds it today: the one thing that varies the default keymap is the profile's backspace
choice, and no keypad key depends on it. It is the hook for #18. When a live keymap exists, the window binds it
here and the tooltips follow, which is the property that stops them becoming lies.

`IsEnabled` is the window's to bind (§6.1); the control itself never reads a connection state.

## 5. Tooltips: `KeymapHints`

`src/LizTerm.App/Keyboard/KeymapHints.cs`, pure and static:

```csharp
public static class KeymapHints
{
    /// <summary>Every chord in the keymap that sends the key, as one line of text; null when none does.</summary>
    public static string? Describe(Keymap keymap, TerminalKey key, IFormatProvider? format = null);
}
```

It reverses the keymap, collecting every `KeyChord` whose value is `key`, and orders them without reference to the
table's insertion order: chords with no modifiers first, then modified chords in ascending order of their
`KeyModifiers` value (Alt, then Control, then Shift, combinations after), then taps; within a group, function keys
first, then by `Key`, so PF7 reads "F7 or PageUp" and not the reverse. The ordering is spelled out because `Keymap`
holds a `Dictionary`, whose enumeration order is an implementation detail the project does not lean on.

Each chord is formatted:

- An ordinary chord through Avalonia's own `new KeyGesture(chord.Key, chord.Modifiers).ToString("p", format)`.
  Every backend in the pinned Avalonia 12.1.2 registers a platform `KeyGestureFormatInfo`: glyphs on macOS, so
  PA1 reads "⌥+1" and PF13 "⌃+F1 or ⇧+F1", and words on Windows and Linux, "Alt+1" and "Ctrl+F1 or Shift+F1".
  Key names come from Avalonia's common overrides, so a digit key is "1" and not "D1", while Page Up stays "PageUp".
- A tap, which the formatter has no word for, by hand: "a tap of Left Ctrl", "a tap of Right Ctrl".

Two chords join with "or", three or more with commas and a final "or". Under the default keymap, on a platform that
formats with words: PF1 is "F1"; PF13 "Ctrl+F1 or Shift+F1"; PA2 "Alt+2 or Ctrl+Home"; Enter "Return, Ctrl+Return
or a tap of Right Ctrl", since Avalonia's name for the key is Return; Reset "Ctrl+R or a tap of Left Ctrl"; Clear
"Pause or Ctrl+Escape"; Attn "Escape"; Erase EOF "End"; Erase Input, Dup and Field Mark null. Measured against the
pinned Avalonia 12.1.2 before the plan was written, not taken from its documentation.

**The provider rule.** The control passes a null `format`, which `KeyGestureFormatInfo.GetInstance` resolves to
the platform's registration. The tests pass an explicit `KeyGestureFormatInfo` and assert against that, so their
expectations hold on every machine; the headless platform registers a format of its own, and what it says is not
ours to assert. `KeymapHints` is also the reverse lookup and formatter #23 needs for the Keys menu; that issue gets
a comment saying so.

## 6. The window and the menus

### 6.1 Docking

In `SessionWindow.axaml`'s `DockPanel`, declared after the find bar and before the screen:

```xml
<controls:Keypad x:Name="KeypadPanel"
                 DockPanel.Dock="{Binding Settings.KeypadDock, Converter={x:Static controls:Keypad.DockPanelDock}}"
                 Dock="{Binding Settings.KeypadDock}"
                 IsVisible="{Binding Settings.Keypad}"
                 IsEnabled="{Binding IsConnected}" />
```

Declared innermost, so at the bottom it sits directly beneath the screen with the find bar, error bar and status
bar below it, and on the right it stands beside the screen and above those three bars, spanning the screen's
height. Avalonia takes an invisible control out of layout, so off costs nothing. Two bindings to one setting
rather than the control docking itself: the grid's shape is the control's concern, its edge of the window is the
window's.

`IsEnabled` follows `IsConnected`, so a disconnected session shows a greyed keypad rather than buttons that
silently do nothing. The keyboard and the Keys menu do the latter today, because the engine answers a key sent
while disconnected with an action error that `Guard` swallows; the keypad invites more clicking than either, so it
says so.

### 6.2 Wiring

The constructor subscribes beside the screen's events:

```csharp
KeypadPanel.KeyRequested += (_, key) =>
{
    Screen.CancelTap();
    _ = ViewModel?.SendKeyAsync(key);
    Screen.Focus();
};
```

Three things, in order:

1. **End any modifier tap in progress**, through `TerminalScreen.CancelTap()`, a new one-line public method over
   the detector's `Reset`. The screen already applies "a pointer press ends a tap" to presses on itself; a click on
   the keypad is a pointer press the screen does not see. Without this, Right Ctrl held while clicking PF3 sends
   PF3 and then, on the release, Enter.
2. **Send through the view model's method**, never the command, as the screen's keystrokes do.
3. **Refocus the screen.** A no-op when focus never left, which §4.1 makes the ordinary case; the guarantee for
   when something else had it, such as the find box.

### 6.3 View > Keypad

A checkbox item beneath the Crosshair submenu, in both menus, with no gesture, per the rule that nothing outside
Edit carries one:

```xml
<NativeMenuItem Header="_Keypad" ToggleType="CheckBox" Click="OnKeypadClickNative"
                IsChecked="{Binding Settings.Keypad, Mode=OneWay}" />

<MenuItem x:Name="KeypadMenuItem" Header="_Keypad" ToggleType="CheckBox" Click="OnKeypadClick"
          IsChecked="{Binding Settings.Keypad, Mode=OneWay}" />
```

Both use the Crosshair items' shape: a one-way check mark plus a click handler that flips `Settings.Keypad` on the
view model. The native item needs that shape because a `NativeMenuItem` never toggles itself and is enabled only
when it carries a handler. The classic item takes the same shape rather than Wire Log's two-way binding so the
two menus stay alike, and Wire Log stays the one item that differs on purpose. The two handlers,
`OnKeypadClick` and `OnKeypadClickNative`, are one-liners over a shared `ToggleKeypad`, as every other paired
action is. The existing parity and activation guards pick the item up with no change.

## 7. The Preferences window

`PreferencesWindow.axaml` gains a **Keypad** group beneath Bell, in the same visual shape:

- A `TextBlock` heading, "Keypad".
- A `CheckBox`, "Show the keypad", two-way to `Keypad`, like the Blink box. It is here as well as in View so the
  group makes sense on its own: a position with no way to show the thing would be a puzzle.
- Two `RadioButton`s in a `KeypadDock` group, "At the bottom of the window" and "On the right of the window",
  one-way `IsChecked` through `KeypadDockConverter` with `Click` handlers writing the enum, the Crosshair radios'
  shape for the reason `EnumIsConverter` documents.

No platform rule: both positions work everywhere.

## 8. Testing and verification

### 8.1 Core

- `SettingsStoreTests`: both new fields round-trip; a file without them reads as the defaults.
- `SettingsLayersTests`: `"keypadDock":"Left"` reads as `Bottom` for that key with the other keys intact, the
  sibling of the crosshair `"Diagonal"` test.

### 8.2 App

- `SettingsViewModelTests`: `Keypad` and `KeypadDock` write through and skip unchanged values.
- `KeypadLayoutTests`: three banks of `BankSize`; no `TerminalKey` twice; no empty label. Plus, from a headless
  `SessionWindow`, every `CommandParameter` on the classic Keys menu is in a bank.
- `KeymapHintsTests`, with an explicit `KeyGestureFormatInfo`: the examples in §5; the same table fed in reverse
  gives the same text; a remap through `Keymap.With` changes the answer; a key nothing maps gives null.
- `KeypadTests`, headless: 36 buttons; bank order at the bottom (the second child is PF2) and index order on the
  right (the second child is PF13, and `Columns` is 3); raising a button's `ClickEvent` raises `KeyRequested` with
  its key; no button is focusable; PA1's tooltip follows a change of the `Keymap` property; Dup has none.
- `SessionWindowTests`: a keypad click reaches the fake session as `key:PF13` and leaves the screen focused; Right
  Ctrl held across a click sends the key and no Enter on release; the panel is hidden by default and visible once
  `Settings.Keypad` flips; its `DockPanel.Dock` follows `Settings.KeypadDock`; it is disabled while disconnected
  and enabled once connected; a click while a key is in flight still reaches the host, the keypad form of
  `A_key_pressed_while_the_previous_one_is_in_flight_still_reaches_the_host`.
- `NativeMenuTests`: clicking View > Keypad flips the setting and the check mark from the native item (through
  `RaiseClicked`) and from the classic one. Parity, activation and `Only_the_edit_menu_carries_gestures` cover the
  rest unchanged.
- `PreferencesWindowTests`: the checkbox drives `Keypad`; the radios drive `KeypadDock` by click (raising
  `Button.ClickEvent`, per `tests/CLAUDE.md`); the radio for the saved value is checked on open.
- `KeypadDockConverterTests`: the shape of `BellSoundConverterTests`.

### 8.3 Manual, on macOS

Against MVS/CE: View > Keypad, click PF and PA keys and confirm the host responds; type immediately after a click
and confirm the keystrokes land, so focus stayed; hold Right Ctrl, click a key, release, and confirm no Enter
reached the host (the wire log shows it); with two session windows open, switch the position in Preferences and
confirm both move; hover a button for the glyph tooltip; shrink the window and confirm the screen refits; launch
with `LIZTERM_MENU=classic` and confirm the classic View item toggles the panel.

## 9. What does not change

The Keys menu, in either renderer. The keyboard path: `TerminalScreen`'s key handling gains a public `CancelTap`
and nothing else. The backend, the profile, the picker, the status bar and the banners. `App.OpenSession`, which
passes nothing new: the control needs nothing the window does not already have.

## 10. Out of scope

- **Left and top docks**, a floating or popup keypad, per-window or per-profile position, drag to re-dock.
- **Vista's customizable toolbar**: user-defined buttons, bitmap editing, macros bound to buttons. The "assign
  any function" half depends on #18 and the "or macro" half on a macro facility that does not exist (#32 is the
  first step of that).
- **Keystroke hints in the Keys menu** (#23), though `KeymapHints` is built for it.
- **Keyboard navigation of the keypad**, deliberately impossible (§4.1).

## 11. Documentation and bookkeeping

Each fact in its home:

- `docs/user-guide.md`: under "The session window", a paragraph on View > Keypad, the Preferences position, and
  hovering for the shortcut; the Keyboard section's closing paragraph names the keypad beside the Keys menu; two
  lines under "Preferences" for the new group.
- `src/LizTerm.App/CLAUDE.md`: a "Keypad" entry covering the control's contract, the non-focusable rule, the
  `CancelTap` rule and `KeymapHints`' provider rule; under "Menus", View > Keypad follows the Crosshair shape and
  carries no gesture.
- `tests/CLAUDE.md`: `KeymapHintsTests` pass an explicit `KeyGestureFormatInfo`; drive keypad buttons by raising
  `Button.ClickEvent`.
- This spec gains an "As built" section when the work lands, as the bell spec did.

The work tracks as #31 and closes it. #23 gets a comment naming `KeymapHints`. The version stays 0.4.1 in the PR;
the bump to 0.5.0 happens at release time, as the settings and bell specs said.

## 12. As built (2026-09-12)

Built as specified; the plan is `docs/superpowers/plans/2026-09-11-lizterm-keypad.md`. Two details were measured
before the plan was written and are already in §5: Avalonia's name for the Enter key is "Return", and function keys
sort ahead of other keys within a group so PF7 reads "F7 or PageUp".

- **§5's parenthetical is loose.** "Alt, then Control, then Shift, combinations after" does not follow from the
  primary rule it glosses, ascending `KeyModifiers` value, under which Alt+Control (3) sorts before Shift (4).
  `KeymapHints` follows the primary rule; `DefaultKeymap` has no combination chord, so nothing observable differs
  today. Noted for #18, when combinations become possible. §5 itself stays as written: design history is a record.
