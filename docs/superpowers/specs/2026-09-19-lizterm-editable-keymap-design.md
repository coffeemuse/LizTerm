# LizTerm: user-editable keymap (#18)

Design, 2026-09-19. Resolves issue #18 and, in its third PR, #23. Assumes the v1 design (§6, the keyboard), the M2
hardening design (§6.2, the default table), the keypad design (§4.4 and §5, the `Keymap` property and
`KeymapHints`), the settings-store design (the flat `AppSettings`, `SettingsLayers`, the write-through
`SettingsViewModel`) and the session-switching design (§6, the chord checked ahead of the keymap).

## 1. Purpose

The keymap is fixed: `DefaultKeymap.Create(destructiveBackspace)` is the only table there is. #18 was filed to
hold the shape of an editable one, and since then everything it waited for has landed: a Preferences window with
tabs, a settings store with a file-beside-`settings.json` precedent (`tags.json`, `recent-hosts.json`), a keypad
whose tooltips already follow a `Keymap` property that nothing binds, and native menus that deliberately carry no
gestures. What is left is the feature itself.

The decisions this spec records, taken with Robert on 2026-09-19:

- **One app-wide keymap**, not per profile. It matches how people think about a keyboard and how the settings
  store works. The one per-profile keyboard fact, destructive Backspace, stays on the profile.
- **Action-centric editor** over a view-agnostic view model, so a chord-centric view, or both at once, is a later
  view over the same data and not a redesign.
- **A Keyboard tab in Preferences**, not a window of its own.
- **A refusal policy** (§4) that the tests pin and the guide explains.
- **The guide describes the defaults, labelled as such**, pinned by a test. The live table is the Keyboard tab.
  This closes what #48 left open: the bundled guide is HTML rendered from Markdown at test time and committed, so
  there is no runtime step to generate a table into, and none is invented.

## 2. Scope

In: the file, the store, the live keymap in every session window and keypad, the Keyboard tab, the refusal
policy, the guide, the Keys menu hints (#23). Out, and stated so that they are not re-raised as gaps in this work:
per-key repeat, buffered versus interrupt behaviour, mouse buttons bound to functions, the scroll wheel as
PF7/PF8, CapsLock and NumLock, a chord-centric view, and adding new "type this text" actions from the tab (the
file allows them; the tab shows them). Each of those is a Vista TN3270 feature and each needs its own surface.

## 3. The file and its model

### 3.1 `keymap.json`

One file beside `settings.json`, `tags.json` and `recent-hosts.json`: `AppPaths.KeymapFile()`. It holds only the
user's differences from the default, so deleting it restores every default, the promise `settings.json` makes.

```json
{
  "bindings": {
    "Ctrl+Home": "PA1",
    "Alt+2": null,
    "Ctrl+OemOpenBrackets": { "text": "¬" },
    "Tap:LeftCtrl": "Enter"
  }
}
```

- A **chord** is the modifiers, in the fixed order `Ctrl`, `Alt`, `Shift`, joined with `+` to an Avalonia `Key`
  name, or `Tap:` and a modifier key name. Parsed case-insensitively. The spelling is LizTerm's own and not
  `KeyGesture.Parse`'s: Avalonia's is platform-flavoured, and it accepts `Cmd`, which §4 refuses.
- An **action** is a `TerminalKey` name, an object `{"text": "…"}` for text to type, or `null` to unbind the
  chord.
- **Sparse means different from the default.** The store drops an entry whose action equals
  `DefaultKeymap.Create(destructiveBackspace: true)`'s for that chord, with one exemption: an explicit binding
  of `Back` is always kept, because that key's default is the profile's to choose (§3.3).
- An entry that will not read, a chord or an action that does not parse, is **skipped on its own and kept
  verbatim on the next save**, the rule `SettingsLayers` applies per key: one hand-edited typo costs one binding,
  and a newer build's `TerminalKey` survives an older build saving. The tab counts them (§5.4).
- A file that is missing, unreadable, not JSON or not an object is an empty overlay. `Update` throws
  `InvalidDataException` naming the file when it exists and is not a JSON object, as `SettingsStore.Update` does,
  rather than replace something the user may be editing.

### 3.2 Where the code lives

The dependency rule decides it. Core never names Avalonia, and a chord is an Avalonia `Key` with Avalonia
`KeyModifiers`, so:

- **`LizTerm.Core.Settings.KeymapFile`** (Core): the document as strings. `IReadOnlyDictionary<string,
  KeymapEntry>` where `KeymapEntry` is the parsed JSON value: a key name, a text, or unbound. Plus the raw
  `JsonObject` of entries it could not read, for the write-back. Pure JSON shape; no key names are interpreted
  here.
- **`LizTerm.Core.Settings.KeymapStore`** (Core): `Load()` never throws; `Update(Func<KeymapFile, KeymapFile>)`
  re-reads, applies, writes through a sibling temp file renamed over the target. The same shape as
  `SettingsStore`, whose atomic `Write` becomes a shared internal helper rather than a third copy.
- **`LizTerm.App.Keyboard.KeymapOverlay`** (App): the parsed overlay. `TryParseChord(string, out KeyChord)`,
  `FormatChord(KeyChord)`, `TryParseAction(KeymapEntry, out KeymapAction)`, and the two projections between
  `KeymapFile` and a list of `(KeyChord, KeymapAction)` pairs. `KeymapAction` is a small discriminated record: a
  `TerminalKey`, a text, or unbound. Round-trip is a test.
- **`Keymap`** gains `Without(IEnumerable<KeyChord>)` beside `With`, so an unbound chord removes the default
  entry rather than shadowing it, and a `Compose` on the overlay: `DefaultKeymap.Create(destructiveBackspace)`
  with the user's bindings applied over it and the user's unbindings removed.

### 3.3 Backspace

`SessionProfile.DestructiveBackspace` keeps deciding what `Back` sends, through the default the overlay is
composed over. A user binding of `Back` in the overlay wins over it, for every profile, and the Backspace row
in the tab says so (§5.2). The sparse rule exempts `Back` (§3.1) so that a user who binds Backspace to Erase,
the erasing default, is still overriding a cursor-left profile and the file records that choice.

## 4. The refusal policy

`KeymapPolicy` (App, pure): `Check(KeyChord chord, PlatformHotkeys hotkeys) → Verdict`, where the hotkeys are
the platform's Copy, Paste and Select All gestures and its command modifier, passed in so a test can be the
platform. `Verdict` is `Allowed`, or `Refused(reason)` with a reason the tab shows verbatim.

| Chord | Verdict | Reason shown |
|---|---|---|
| A platform Copy, Paste or Select All gesture; the command modifier with F (Find) or K (the session switcher) | Refused | "LizTerm uses this for Copy" (Paste, Select All, Find, Switch Session) |
| Any chord with the Command key (macOS) or the Windows key (elsewhere), that is `KeyModifiers.Meta` | Refused | "The menu bar sees Cmd shortcuts before the screen does" (Windows key elsewhere) |
| A printable key with no modifier, or with Shift alone: letters, digits, punctuation, Space | Refused | "This would take away typing that character" |
| Everything else, taps included | Allowed | |

Cmd+comma on macOS falls out of the second row. A chord already bound to another action, or to text, is
**allowed**: it moves, and the tab names the row it left (§5.3). Ctrl+[ and Ctrl+6 are that case, not a refusal.
Unbinding any default is allowed silently, Escape included; Reset to defaults brings it back.

"Printable" is decided by the Avalonia `Key` value alone (`A` to `Z`, `D0` to `D9`, `NumPad0` to `NumPad9`, the
`Oem*` punctuation keys, `Space`, `Decimal`, `Add`, `Subtract`, `Multiply`, `Divide`), not by asking the
platform what it would type: the check has to give the same answer in a test as on a Mac, and a dead key on
some layout is an edge this policy does not try to see.

## 5. The live keymap and the Keyboard tab

### 5.1 `KeymapViewModel`

One per process, owned by `App` exactly as `SettingsViewModel` is (`internal KeymapViewModel Keymap`, built on a
`KeymapStore` on the real file, or in-memory with no store for tests and the design-time constructor). It holds
the parsed overlay and exposes:

- `Bind(KeyChord, KeymapAction)`, `Unbind(KeyChord)`, `ResetToDefaults()`. Each applies in memory first, raises
  `Changed`, then writes through; a save that throws sets `LastSaveError` and raises `SaveFailed`, the settings
  object's contract, so the tab's banner is the same banner.
- `Compose(bool destructiveBackspace) → Keymap`, the map in force for one window.
- `UnreadableEntries`, the count from the file, for the tab's note.
- The view-agnostic queries a row view needs: `ChordsFor(KeymapAction)` and `ActionOf(KeyChord)`, both answered
  from the composed erasing map, so a row never reads the dictionary itself.

It knows nothing about rows, tabs or capture. That is the seam a chord-centric view would sit on.

### 5.2 Wiring into the session window

`TerminalScreen.Keymap` becomes a styled property, defaulting to `DefaultKeymap.Create(destructiveBackspace:
true)` so that the input tests keep their default table; the private getter and the `DestructiveBackspace`
property go, and the one test that set the latter (`TerminalScreenInputTests`) sets the cursor-left default as
its `Keymap` instead. The session window composes `App.Keymap.Compose(profile.DestructiveBackspace)` on open and on every `Changed`, and
sets it on the screen and on the keypad's existing `Keymap` property. A keypad tooltip therefore follows a
rebind the moment it is saved, which is the property the keypad spec §4.4 reserved this hook for.

### 5.3 The Keyboard tab

A fifth `TabItem` in `PreferencesWindow`, after Window. The window keeps its fixed 520 by 446; the tab's content
is a `ScrollViewer` over a list of rows, one per action, in this order: Enter, Newline, Clear, Reset, Attn,
SysReq, PF1 to PF24, PA1 to PA3, Tab, BackTab, Insert, Home, EraseEof, EraseInput, Delete, Backspace, Dup,
FieldMark, Up, Down, Left, Right, then one "Type ¬"-style row per text action in the composed map.

A row is the action's name, its chords as chips each with a remove, and one **Add** slot. The Backspace row
carries the note "The profile's Backspace setting decides this until you bind it here." Below the list: **Reset
to defaults**, the save-error banner the other tabs share, and the unreadable-entries note when the count is
not zero.

**The Add slot** is a focusable control that reads "Press a key". While focused:

- A key press with a non-modifier key builds `new KeyChord(e.Key, e.KeyModifiers)`. A press of a modifier key
  alone is fed to a `ModifierTapDetector` of the slot's own, and the matching release is the tap chord. So a tap
  of Left or Right Ctrl is captured the way the screen detects it, and no picker is needed for the two tap rows.
- The chord goes to `KeymapPolicy.Check`. Refused: the slot shows the reason and stays armed. Allowed: the view
  model binds it; if `ActionOf` said it was another row's, the slot shows "Moved from PA2" for a moment.
- Focus loss cancels. Escape does not: Escape is a chord someone may want to bind.

The tab never touches the dictionary: chips come from `ChordsFor`, wording from `KeymapHints.Describe` on a
single chord so a chip reads exactly as the keypad tooltip does.

### 5.4 Preferences window plumbing

`PreferencesWindow` takes the `KeymapViewModel` beside the `SettingsViewModel`; the design-time constructor
builds an in-memory one. `App.ShowPreferences` passes its own. The tab is always present: unlike the menu-style
row, nothing about a platform makes it inapplicable.

## 6. Documentation and the Keys menu

### 6.1 The user guide

The **Keyboard** section keeps its table, retitled as the defaults, and loses "It cannot be changed yet". After
the table: how to change a binding (Preferences, Keyboard tab, click Add, press the chord), what the tab refuses
and why, that Backspace follows the profile until bound, and that Reset to defaults or deleting `keymap.json`
restores the table above. The **Preferences** section lists the Keyboard tab. **Where LizTerm keeps its files**
adds `keymap.json`. `docs/privacy.md`'s file table adds a row: keyboard bindings, default permissions.

A test in `LizTerm.App.Tests` reads `docs/user-guide.md`'s Keyboard table and asserts every row against
`DefaultKeymap.Create(true)` and `KeymapHints`: every default chord appears in the row for its key and no row
claims a chord the default lacks. That is the "test asserting the manual matches `DefaultKeymap`" #48 asked
for, and with the tab as the live table it is the one that stays true.

### 6.2 The Keys menu (#23)

Both menus put the keystroke in the **header text**, never in a gesture: on the native menu a gesture is a real
key equivalent that steals the keystroke from the screen (native-menus spec §2.1), and header text is the one
form both menus render alike, which keeps the parity test honest. The header is the action's name, two spaces,
then `KeymapHints.Describe` for that key in the window's composed map, so PA2 reads `PA2  Alt+2 or Ctrl+Home`
and follows a rebind. A key with no chord keeps its bare name. Insert's check mark is unchanged. The session
window rebuilds the headers on the same `Changed` it composes the keymap on.

### 6.3 `CHANGELOG.md`

Under `## Unreleased`: the Keyboard tab and `keymap.json`, the Keys menu hints, and that the guide's table is
now the defaults.

## 7. Testing

### 7.1 Pure (`[Fact]`)

- `KeymapFile` and `KeymapStore` (Core.Tests): round trip; sparse write drops defaults and keeps `Back`;
  unreadable entries skipped and preserved; missing, empty and non-object files; the `Update` re-read.
- `KeymapOverlay` (App.Tests, Keyboard): every chord spelling parses and formats back to itself; taps; case;
  rejects `Cmd+`; `Compose` over both Backspace defaults; `Without` removes, `With` shadows.
- `KeymapPolicy`: one test per table row of §4 on a macOS-shaped and a Windows-shaped `PlatformHotkeys`.
- `KeymapViewModel`: bind, unbind, reset raise `Changed` and write through; a throwing store sets
  `LastSaveError` and keeps the in-memory change; `ChordsFor` and `ActionOf` after a move.
- The guide table test of §6.1.

### 7.2 Headless (`[AvaloniaFact]`)

- The Add slot: a plain chord binds; a refused chord shows its reason; a Left Ctrl tap binds the tap chord; a
  Ctrl press followed by C is Ctrl+C, refused as Copy; focus loss cancels.
- A rebind reaches the screen's `Keymap`, the keypad tooltip and the Keys menu header in an open session window
  built on `FakeEmulatorSession`.
- The native and classic Keys menus agree on every header (the existing parity guard, now with hints).

### 7.3 Live checks, before release

On macOS: Option+1 still sends PA1 (the Alt row spells `Alt+1` in the file); a Right Ctrl tap captured in the
tab and then sending Enter on the screen; Cmd+comma refused. On Windows or Linux: the Windows-key refusal; a
hand-edited `keymap.json` with one bad line loses one binding and nothing else.

## 8. Delivery

1. **Model, store and live wiring.** §3, §4, §5.1, §5.2, `AppPaths.KeymapFile`, the file docs of §6.1. The
   keymap is editable by hand and every window follows it. No UI.
2. **The Keyboard tab.** §5.3, §5.4, the guide's Keyboard and Preferences text.
3. **The Keys menu hints and the changelog.** §6.2, §6.3, closing #23 and #18.

## 9. Follow-ups, each needing Robert's OK

- A chord-centric view, or a toggle between both, over the same `KeymapViewModel`.
- Adding text actions from the tab.
- Any of the §2 exclusions, each as its own issue against its own surface.
