# LizTerm: a settings store, and the Preferences window

Date: 2026-09-11. Parent spec: `2026-09-03-lizterm-v1-design.md`. Predecessor:
`2026-09-11-lizterm-macos-notarization-design.md` (v0.4.1, released 2026-09-11). Issues: #19 (a preferences
window), constrained by #55 (a store that can be exported and layered); closes out the crosshair persistence
that `2026-09-09-lizterm-the-screen-design.md` §4.3 deferred.
Status: approved in discussion on 2026-09-11; awaiting review of this text.

## 1. Purpose

LizTerm has no app-wide settings. `AppPaths` knows three paths — the config root, `profiles/` and `logs/` — and
everything configurable is either a `SessionProfile` field or one of three environment variables
(`LIZTERM_B3270_PATH`, `LIZTERM_MENU`, `LIZTERM_WIRE_LOG`). Two display settings already exist with nowhere to
live: the crosshair, a per-window View menu choice that the screen spec parked "until #19 gives a preference
somewhere to live", and blink, which `TerminalScreen` runs at a fixed 750 ms phase with no switch at all.

The next features queued (#47 the bell, #31 the keypad, later the engine path) each want a global setting, and
#55 makes the case that the store's *shape* — whether a key can be absent, whether the file can be layered
and exported — is nearly free to get right at creation and expensive to retrofit. So this milestone builds
the store and the window first, deliberately thin, and lets those features add fields to it.

This spec adds nothing to `IEmulatorSession`, `SessionProfile`, the backend, or the profile editor. Display
settings never reach the engine; the screen spec's ruling that the crosshair stays out of b3270 (§4.1) carries.

Decisions taken in the brainstorm on 2026-09-11, each justified in the section named:

- **Only settings with a consumer today**: crosshair mode and blink (§3.2). Bell and keypad add their own
  fields when they land.
- **The View menu writes through** (§4.2): the setting means "what I last chose", and Preferences shows the
  same value. There is no separate "default for new windows".
- **Layering is designed for and only the user file is wired** (§3.3, §3.4): the user file is a sparse
  per-key overlay, so a system directory later is one more path in a list, not a format change.
- **Every window follows live** (§4.1): one observable settings object per process.
- **Preferences opens from the macOS application menu with Cmd-comma, and from Edit elsewhere** (§5.4). The
  picker gets no entry off macOS in this cut.
- **Sparse overlay merged at the document level** (§3.4) over the two alternatives considered: writing every
  field as profiles do (pins every key after the first save, so a system layer could only ever supply
  first-run defaults), and a nullable-per-key record (absence in the type, but every consumer resolves nulls
  or the record needs a second resolved shape — more code per setting for the same behaviour).

## 2. What already exists, and is copied

- `ProfileStore` (`src/LizTerm.Core/Profiles/ProfileStore.cs`): one directory argument with a default, a
  source-generated JSON context over a positional record with a default on every parameter, a write through a
  sibling temp file renamed over the target, and unreadable files skipped rather than fatal. `Update` re-reads
  the file before writing so a caller holding an older copy does not discard edits saved since. Every one of
  those choices is copied.
- `MenuStrategy.AboutInHelpMenu(bool isMacOS)` (`src/LizTerm.App/Menus/MenuStrategy.cs`): a pure,
  platform-as-argument rule for an item that exists on one platform's menu and not another's, and
  `SessionWindow.axaml.cs:94` applies it by hiding the item and its separator. Preferences in Edit follows it.
- `App.ShowAboutAsync` and the `_about` field (`src/LizTerm.App/App.axaml.cs`): one window at a time, a second
  request activates the first. Preferences follows it.
- The injected seams on `SessionViewModel` — `ITextClipboard`, `ICertificatePrompt`, `IFolderOpener`,
  `ICertificateFetcher` — all optional constructor parameters with a fake or a null in tests. The settings
  object is injected the same way.

## 3. The Core side

### 3.1 Namespace and files

A new `LizTerm.Core.Settings` namespace, in `src/LizTerm.Core/Settings/`:

| File | Holds |
|---|---|
| `AppSettings.cs` | The record (§3.2) |
| `CrosshairMode.cs` | The enum, **moved** from `src/LizTerm.App/Rendering/CrosshairMode.cs` unchanged, since the record names it and Core cannot see App. Its App users (`TerminalScreen`, `CrosshairGeometry`, `CrosshairModeConverter`, `SessionWindow`) take a `using`. |
| `SettingsLayers.cs` | The merge and the sparse-document rule, pure (§3.3, §3.4) |
| `SettingsStore.cs` | The file-backed wrapper (§3.3–§3.5) |
| `SettingsJsonContext.cs` | The source-generated context, like `ProfileJsonContext` |

`AppPaths` gains `SettingsFile()`: `settings.json` directly under `ConfigRoot()`, beside `profiles/` and
`logs/`. `SettingsStore(string file)` takes the path, with `DefaultFile()` returning `AppPaths.SettingsFile()`,
the shape `ProfileStore(string directory)` and `DefaultDirectory()` already have.

### 3.2 The record

```csharp
public sealed record AppSettings(
    CrosshairMode Crosshair = CrosshairMode.None,
    bool Blink = true);
```

Positional, a default on every parameter, so schema growth is free in both directions: an older file lacks
the new key and reads as the default; a newer file's extra key is ignored on read (§3.3) and preserved on
write (§3.4). The record stays **flat** by policy — one top-level key per setting — because the merge in §3.3
is a shallow overlay and a nested object would be replaced whole rather than merged.

The enum is written as its **name**, through `JsonStringEnumConverter<CrosshairMode>` on the property, so the
file is hand-readable and reordering the enum can never change a saved meaning.

### 3.3 Loading: layers merged as documents

The store reads each layer as a `JsonObject` (`System.Text.Json.Nodes`). A layer that is missing, cannot be
read, is not parseable JSON, or parses to something other than an object is **skipped**. `SettingsLayers.Merge`
overlays the layers' top-level keys in order, base first, later wins per key, and the merged object is
deserialised once through the context into `AppSettings`. If that deserialisation fails — a crosshair value that is not a mode
name, a blink that is not a boolean — the run gets `new AppSettings()` and nothing is written until the user
changes something (§3.4 then repairs it).

Only one layer is wired in this cut: the user file. `Merge` takes a list so that a system directory (#54, #55)
is one more entry ahead of the user file and no format change. Unknown keys are ignored by the deserialiser,
so an older LizTerm opens a newer file.

`Load()` never throws. An unreadable file surfaces to the user only when a save fails (§3.5), which is when
they would look.

### 3.4 Saving: the sparse rule

`Save(AppSettings next)` re-reads the user file at that moment, as `ProfileStore.Update` does, so a key
written by a second LizTerm process between this process's load and save survives. It then writes the
document `SettingsLayers.UserDocument(next, beneath, existing)` computes, where `beneath` is the record the
layers under the user file merge to — in this cut, `new AppSettings()` — and `existing` is the user document
as re-read, or null when there is no file.

The document holds **exactly** these keys:

1. every key `existing` already holds, and
2. every key whose value in `next` differs from its value in `beneath` (`JsonNode.DeepEquals` over the two
   records serialised to nodes).

For each key: a **known** key (a property of the record) takes its value from `next`; an **unknown** key is
copied from `existing` verbatim.

What those two rules buy, each of which is a test in §6.1:

- A key the user never touched stays **absent** and keeps following the default, built-in or, later, system.
- A key the user set once stays **pinned**, even when set back to the base value, because that was a choice.
  A later change of built-in default does not move it. (Profiles reach the same property by writing every
  field; that is right for a record that is a set member and wrong for one that layers.)
- A known key with a **bad value** is rewritten from `next` on the first save, so a hand-edit typo heals
  itself instead of poisoning every later load.
- An **unknown** key from a newer version survives an older version saving.

The write goes through `settings.json.tmp` in the same directory and `File.Move(..., overwrite: true)`, and a
failed write deletes the temp file, exactly as `ProfileStore.Save` does and for the same reasons.

### 3.5 Failure on save

If the user file exists but is not parseable JSON, or parses to something other than an object, `Save` throws
`InvalidDataException` with the path in the message — "settings.json is not valid JSON; fix or delete it:
<path>" — rather than silently replacing a file the user may be editing by hand. `IOException` and
`UnauthorizedAccessException` from the re-read or the write propagate as they are. The App decides how to show
these (§4.4).

### 3.6 Environment variables

None of this cut's settings has one, so no overlay mechanism is built. The rule for when one arrives (the
engine path, #19): a variable is the **topmost** layer, applied after every file, because the variables are
debugging overrides and must keep winning. It is not written back. `MenuStrategy`, `B3270Locator` and
`WireLog` keep reading their variables directly until the setting they shadow exists.

### 3.7 What is pure

`SettingsLayers` is static functions over `JsonObject`s and records with no file access: `Merge`,
`UserDocument`, and the read of a merged object into a record. Every ordering, pinning and repair case is a
plain unit test with no temp directory. `SettingsStore` is the thin wrapper that reads and writes files and
calls them.

## 4. The App side

### 4.1 One `Settings` object per process

`Settings`, in a new `src/LizTerm.App/Settings/` folder, on CommunityToolkit's `ObservableObject` like the view
models:

```csharp
public sealed partial class Settings : ObservableObject
{
    public Settings();                       // in-memory; tests
    public Settings(SettingsStore store);    // loads now, saves on every change
    public AppSettings Current { get; }
    public CrosshairMode Crosshair { get; set; }
    public bool Blink { get; set; }
    public string? LastSaveError { get; }    // observable; null after a successful save
    public event EventHandler<string>? SaveFailed;
}
```

A setter whose value equals the current one returns early: no change notification, no write. One that changes
something does three things in this order: replaces `Current` with the changed record, raises the property
change, then saves through the store. **The in-memory change always wins**: the UI must reflect what the user
chose even when the disk refuses it. A save that throws sets `LastSaveError` and raises `SaveFailed` with the
message "Could not save settings: " plus the exception's; a save that succeeds clears `LastSaveError`.

Construction: `App` creates one at startup next to `_store`, from `new SettingsStore(AppPaths.SettingsFile())`,
with the same `??=` discipline the profile store has. The constructor's optional store is the test seam: with
none, the object is purely in-memory, so no new interface and no fake class. `Settings` is UI-thread only; the
menus and the window that set it are.

### 4.2 Into the session view model

`SessionViewModel` gains an optional `Settings? settings = null` constructor parameter and exposes it as
`Settings`, creating an in-memory one when none is passed so the App tests' 28 constructions stay as they are.
Its own `[ObservableProperty] CrosshairMode _crosshair` (`SessionViewModel.cs:63`) goes away with its doc
comment, which said exactly that this would happen.

`SessionWindow.axaml` rebinds: the screen's `Crosshair="{Binding Settings.Crosshair}"` (`:232`), and each View >
Crosshair radio item's `IsChecked` to `Settings.Crosshair` through the existing one-way `CrosshairModeConverter`
(`:69`–`:75`). The four Click handlers (`SessionWindow.axaml.cs:242`–`:253`) set `vm.Settings.Crosshair`. The
`CrosshairModeConverter` is unchanged: its comment explains why it is one-way, and that still holds.

Because every window's view model holds the one `Settings`, a change in any window's View menu or in
Preferences reaches every window through ordinary property change notification. That is the whole of the
live-propagation mechanism; no event bus.

### 4.3 Blink

`TerminalScreen` gets a `BlinkEnabled` styled property, default `true`, bound to `Settings.Blink` in the same
element. `UpdateBlinkTimer` (`TerminalScreen.cs:110`) adds it to its condition — the timer runs only while
attached, the snapshot has blink, *and* blink is enabled — and its stop branch already clears `BlinkHidden`, so
disabling blink mid-phase draws the text steady at once. A property change re-evaluates the timer against the
current snapshot the way a snapshot change does.

Off means **steady**. Vista's third state, italic for blinking text, is noted on #19 and not built here.

### 4.4 Save failure reaches the user

Each `SessionViewModel` subscribes to `Settings.SaveFailed` for its lifetime (unsubscribing in `DisposeAsync`)
and puts the message in `ErrorMessage`, the banner that already reports "Could not save the profile: ...". Every
open window shows it, which is right for a global fact: your settings are not being saved. The Preferences
window binds a text line to `LastSaveError` (§5.1) so a change made there is not reported only on a window
behind it. The event and the property serve those two consumers: the event fires on every failure, where a
property whose text has not changed would not notify a banner the user had dismissed.

### 4.5 What does not change

`SessionProfile`, `ProfileStore`, the backend, `IEmulatorSession`, the profile editor and the picker. The
crosshair's rendering, geometry and its View menu shape (the screen spec §4.2 and §6). `MenuStrategy.Decide`
and the classic/native staging.

## 5. The Preferences window

### 5.1 Contents and behaviour

`PreferencesWindow` (`src/LizTerm.App/Views/`), titled **Preferences**, in the Fluent dark style of the profile
editor and About, with `Settings` as its data context and a parameterless designer constructor like
`AboutWindow`'s. Two groups in this cut:

- **Crosshair**: four `RadioButton`s — None, Horizontal, Vertical, Both — each `IsChecked` bound one-way to
  `Crosshair` through `CrosshairModeConverter` with the mode as parameter, and each with a Click handler that
  sets `Settings.Crosshair`. The same shape as the View menu's radio items, for the reason the converter's own
  comment gives.
- **Blink**: one `CheckBox`, "Blink text the host marks as blinking", bound two-way to `Blink`.

Every change applies the moment it is made and shows in every session window behind. There is no OK, Cancel or
Apply: one **Done** button, `IsDefault` and `IsCancel`, closes the window. Below the controls, a `TextBlock`
bound to `LastSaveError`, empty until a save fails.

Adding a group later is adding controls, not restructuring.

### 5.2 Lifetime

Modeless and unowned, so it floats and the user keeps working in a session while it is open. One instance per
process: `App` holds a `_preferences` field the way it holds `_about`, and `App.ShowPreferences()` activates the
one already showing or creates it, clearing the field on `Closed`. It works with only the picker open, since
`Settings` lives on the app, not on a session.

### 5.3 Naming

"Preferences..." on every platform, and "Preferences" as the title. macOS 13 renamed its own item to Settings,
but many shipping apps still say Preferences, users find it by position under the app name and by Cmd-comma
either way, and one string keeps the item and the title the same everywhere. It also matches #19's title.

### 5.4 Menu entries, and the gesture exception

**macOS application menu**, `App.axaml`: a `NativeMenuItemSeparator` and "Preferences..." after About, with
`Gesture="Cmd+OemComma"` and `Click="OnPreferencesClick"`, which calls `ShowPreferences()`. Avalonia appends
AppKit's standard block after ours, so the menu reads About, divider, Preferences, divider, Services and the
rest — the order macOS users expect.

This is a **deliberate exception to the rule in `src/LizTerm.App/CLAUDE.md` "Gestures"** ("No menu item outside
Edit ever carries a Gesture, the application menu included"), and that note is reworded to name the exception
and its reasoning. The rule exists because a native gesture becomes an AppKit key equivalent dispatched before
the key window's responder chain, so a gesture that is also a terminal keystroke would steal it from the host.
Cmd-comma cannot be one: `DefaultKeymap` binds Alt, Control and Shift chords and no Cmd chord (measured:
`KeyModifiers.Alt`, `.Control`, `.Shift`, `.None` are the only modifiers in
`src/LizTerm.App/Keyboard/DefaultKeymap.cs`), and Edit's existing Cmd+C, V, A and F are already key equivalents
of exactly this class. The application menu exists only on macOS, so no other platform sees the gesture at
all.

**Windows and Linux**, the session window's Edit menu: a separator and "Preferences..." at the bottom, in
**both** the native `NativeMenu` and the classic `<Menu>` so the parity guard stays green, each with a Click
handler (a native item with only a binding is inert). No gesture, matching About. Shown only off macOS through
a new pure rule `MenuStrategy.PreferencesInEditMenu(bool isMacOS) => !isMacOS`, applied beside `AboutInHelpMenu`
in `SessionWindow`'s constructor: on macOS the item **and its separator** are hidden, natively through
`MenuLookup.SeparatorAbove` and classically through a named separator, exactly as About in Help is.

The window's handler reaches the app the way About's does: `(Application.Current as App)?.ShowPreferences()`.

## 6. Testing and verification

### 6.1 Core

`SettingsLayersTests`, no disk, in `tests/LizTerm.Core.Tests/Settings/`:

- a later layer wins per key; a key absent from every layer is the default;
- the user document holds a changed key and omits an untouched one;
- a key already in the user document stays pinned when set back to the base value;
- a known key with a bad value is rewritten from the new record;
- an unknown key is copied through verbatim;
- a merged object with a bad value reads as null, so the store falls back to defaults.

`SettingsStoreTests`, one temp directory per test deleted on dispose, the `ProfileStoreTests` shape:

- save then load round-trips both fields; the enum lands in the file as its name;
- a first save of one change writes only that key;
- a missing file loads defaults; a file with an unparseable value loads defaults and a later save repairs it;
- a file that is not JSON, and a file that is a JSON array, load defaults and make save throw with the path in
  the message, leaving the file untouched;
- save re-reads the file, so a key written between load and save survives;
- a failed write leaves no temp file behind.

`AppPathsTests` gains the settings file path.

### 6.2 App

`SettingsTests` (`tests/LizTerm.App.Tests/Settings/`), plain facts: setting a property raises the change; the
same value again raises nothing and, on a store-backed instance, writes nothing (file timestamp or content
unchanged); a store-backed instance writes through and a fresh instance on the same file reads it back; a store
pointed at a file that is not JSON raises `SaveFailed`, sets `LastSaveError`, and the in-memory value still
changed; the next successful save clears `LastSaveError`.

Consumers: the existing crosshair view-model tests move to the `Settings.Crosshair` path; a view model whose
settings fail to save shows the message in its banner; two view models built on one `Settings` see each other's
crosshair change; `TerminalScreen` with `BlinkEnabled` false keeps its timer stopped and never enters the hidden
phase while a blinking snapshot is displayed, and re-enabling it starts the timer — beside the existing
blink-timer tests in `tests/LizTerm.App.Tests/Controls/`.

Menus and window: `MenuStrategyTests` covers `PreferencesInEditMenu` both ways. `NativeMenuTests` gains the
Preferences item — present in both menus off macOS and absent, with its separator, on it; activatable through
the native bridge like every other item; and still passing the parity guard. A headless `PreferencesWindowTests`
clicks a radio and the check box and asserts the shared object changed, then changes the object and asserts
the controls followed, and shows `LastSaveError` when set.

One deviation from the test plan agreed in the brainstorm: "an App-level test opens Preferences twice and gets
one window" is not written, because no App test constructs `App` (the headless lane runs `TestAppBuilder`) and
About's identical one-at-a-time rule has no such test either. It is checked by hand (§6.3).

### 6.3 Manual, on macOS

Nothing here touches the engine or a host, so the integration lane is untouched. Once, by hand, on a Mac:
Cmd-comma opens Preferences from the picker and from a session; a second Cmd-comma activates the same window;
a crosshair change in Preferences shows in two open sessions at once; with blink off a screen carrying blink
draws steady; and `settings.json` after one change holds exactly one key.

## 7. Documentation and bookkeeping

Each fact in its one home:

- `docs/user-guide.md`: a **Preferences** section — where it opens on each platform, the two settings, that
  the View > Crosshair choice is remembered; the existing "It is set per window" sentence (`:71`) becomes
  "remembered across sessions"; the file-locations table (`:199`) gains `settings.json`, noting it holds only
  what you have changed and can be deleted to reset everything.
- `docs/architecture.md`: the Core row names settings beside profiles.
- `src/LizTerm.Core/CLAUDE.md`: a Settings entry — the sparse rule, pinning, `SettingsLayers` is pure,
  `AppPaths.SettingsFile()`.
- `src/LizTerm.App/CLAUDE.md`: `Settings` and its in-memory mode under "Session view model"; the Preferences
  window's one-instance lifetime; `PreferencesInEditMenu` and the reworded "Gestures" rule (§5.4) under "Menus";
  "About and nothing else" in the application-menu paragraph becomes "About and Preferences".
- `tests/CLAUDE.md`: view-model tests get an in-memory `Settings` by default; drive the Preferences radios by
  click, not by assigning `IsChecked`.
- Older specs are history and stay as written. The screen spec's §4.3 ruling that the crosshair resets on
  relaunch was right when written and is superseded here.

Bookkeeping: the work tracks as #19, with a comment when the PR opens noting that #55's constraints are
honoured (sparse per-key layering, no secrets, no machine-specific keys) and listing what remains on #19 —
engine path, wire-log defaults, colours, font sizing, cursor shape, keymap editor, italic as a third blink
state. #27's deferred persistence is done. The version stays 0.4.1 in this PR; the bump to 0.5.0 happens at
release time as it did for 0.4.0.

## 8. Out of scope

A system settings directory and the permission audit #54 asks for. An environment-variable overlay. Export and
import bundles (#55). The bell (#47) and keypad (#31) fields. A picker entry off macOS. Any per-profile display
setting. Window geometry, which is state rather than preference and would need its own file (#55 excludes it
from any bundle). Native managed-preferences mechanisms (macOS configuration profiles, Group Policy).
