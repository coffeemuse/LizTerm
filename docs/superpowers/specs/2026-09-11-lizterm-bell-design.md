# LizTerm: the terminal bell

Date: 2026-09-11. Parent spec: `2026-09-03-lizterm-v1-design.md`. Predecessor:
`2026-09-11-lizterm-settings-store-design.md` (#19/#55, merged as PR #67), which built the store this spec adds
fields to. Issue: #47. The v0.3.1 spec (`2026-09-09-lizterm-v031-fix-and-polish-design.md` §1) cut the bell
from polish with the words "it earns its own plan"; this is that plan's spec.
Status: approved in discussion on 2026-09-11; awaiting review of this text.

## 1. Purpose

b3270 reports the 3270 alarm as `{"bell":{}}`. LizTerm parses it into `UnknownIndication`, `Handle` falls
through to `HandleStateIndication`, which has no case for it, and nothing rings, flashes or logs. A test even
pins the gap: `IndicationParserTests.Unknown_indications_are_reported_by_name` asserts that `bell` is unknown.
The gap is not hypothetical — line 30 of `tests/LizTerm.Backend.B3270.Tests/Fixtures/ibmlink-help.jsonl`, a
real session against a real host, rings it.

The engine cannot help. b3270 is headless: none of the 63 settings in its `initialize` block is a bell or a
sound, and emitting the indication and leaving the noise to the UI is the only correct thing it could do.
Ringing is entirely ours, and there is no single cross-platform call to do it with. Avalonia 12 has no audio
API, `System.Media.SoundPlayer` is Windows-only and outside the shared framework, and Linux has no guaranteed
audio path without a library dependency (issue #47, second comment, measured all three).

Decisions taken in the brainstorm on 2026-09-11, each justified in the section named:

- **A visual bell by default, and it is a screen flash** (§4). It works everywhere, needs no platform code,
  and serves users who cannot hear a sound at all.
- **Audible is opt-in, and the first option is the system alert sound** (§3.3, §5): `NSBeep` on macOS,
  `MessageBeep` on Windows, nothing on Linux. It plays the sound the user chose at the volume they chose, and
  stays silent when they have turned interface sounds off. No bundled WAV: it would override all three.
- **The sound is an enum, not a bool, so a user-chosen file can join later** (§2.1). That round is deferred to
  #19; this one ships two members.
- **One rate limit in front of both the flash and the sound** (§3.4): 500 ms minimum interval, later bells
  dropped rather than queued.
- **The bell paints and sounds its own window only** (§7). No Dock badge, no taskbar flash.
- **The settings are global** (§2), in the store #67 built, not per profile.

## 2. Settings

### 2.1 The fields

`AppSettings` (`src/LizTerm.Core/Settings/AppSettings.cs`) gains two positional parameters, each with a default,
flat as the settings spec requires:

```csharp
public sealed record AppSettings(
    [property: JsonConverter(typeof(JsonStringEnumConverter<CrosshairMode>))] CrosshairMode Crosshair = CrosshairMode.None,
    bool Blink = true,
    bool VisualBell = true,
    [property: JsonConverter(typeof(JsonStringEnumConverter<BellSound>))] BellSound BellSound = BellSound.None);
```

`BellSound` is a new enum in `src/LizTerm.Core/Settings/BellSound.cs`, beside `CrosshairMode`:

```csharp
public enum BellSound { None, SystemAlert }
```

Written by name, as `CrosshairMode` is, so the file is hand-readable and reordering can never change a saved
meaning. `SettingsJsonContext` needs no change: the generator follows the record's properties, as it does for
`CrosshairMode` today.

**Why an enum for the sound.** "Which sound" is one choice among alternatives. The WAV round (§8) adds a
`SoundFile` member and a `BellSoundFile` string field holding the path; nothing existing changes shape. The
store already handles that growth in both directions: an older file lacks the keys and reads as the defaults,
and `SettingsLayers.Read` drops a key it cannot parse on its own, so a future file saying `"bellSound":
"SoundFile"` read by this build is a silent `None` with every other key intact, not a failed load. That is the
same guarantee the settings spec designed for `Crosshair` (§3.3 there, as built).

**Why visual is a separate bool.** The two are independent: the flash with a sound, the flash alone, a sound
alone, or neither are all reasonable. Folding them into one enum forces a "Both" member now and a "Both, with
file" member later.

### 2.2 The view model

`SettingsViewModel` gains `VisualBell` and `BellSound` properties with the same shape as `Blink`: a setter that
returns early when nothing changed, otherwise `Apply` with the property name and a `with` expression. Nothing
else in the class changes.

## 3. The path from the engine to the window

### 3.1 Backend

`BellIndication`, a record with no fields, joins `src/LizTerm.Backend.B3270/Protocol/Indications.cs`.
`IndicationParser.Parse` maps `"bell"` to it. The body is `{}` today; a future field would be added to the record
and read with the existing helpers, as every other indication does.

`B3270Session.HandleStateIndication` gains one case:

```csharp
case BellIndication:
    BellRang?.Invoke(this, EventArgs.Empty);
    break;
```

It raises on the reader thread, in order with every other event, exactly as `PopupIndication` raises
`HostMessage` two cases below it. There is no session state to update: a bell is an event, not a condition.

### 3.2 Core

`IEmulatorSession` gains its sixth event:

```csharp
/// <summary>The host rang the 3270 alarm. Carries nothing: the bell has no text and no state.</summary>
event EventHandler? BellRang;
```

Non-generic `EventHandler`, because the other five carry data and this one does not, and inventing an empty
argument type would say otherwise. `HostMessage` was considered and rejected in the issue: it carries popup and
`ui-error` text for the error banner, and a bell has no text.

`FakeEmulatorSession` (`tests/LizTerm.App.Tests/Fakes/`) gains `RaiseBell()`, beside `RaiseHostMessage`.

### 3.3 The ringer seam

A new folder `src/LizTerm.App/Bell/`, beside `Clipboard/`, `Dialogs/` and `Files/`, holding three files.

**`IBellRinger`**, one method:

```csharp
/// <summary>Makes the bell audible. Called on the UI thread, never with BellSound.None: "None means silence" is
/// the view model's rule, and a ringer only knows how to make sounds.</summary>
public interface IBellRinger
{
    void Ring(BellSound sound);
}
```

Taking the enum rather than being parameterless is the extensibility hook. The WAV round adds a path parameter
(or a small request record) and a branch in the implementation; no caller changes shape.

**`SystemBellRinger`**, the only file in the App that P/Invokes anything:

- macOS: `NSBeep()` from `/System/Library/Frameworks/AppKit.framework/AppKit`.
- Windows: `MessageBeep(MB_OK)` from `user32.dll`, `MB_OK` being `0`.
- Linux, and any `BellSound` value the ringer does not know: nothing.

Each call sits behind `OperatingSystem.IsMacOS()` or `OperatingSystem.IsWindows()`, which the CA1416 platform
compatibility analyzer recognises as a guard, so the Release build with `-warnaserror` stays clean. The
P/Invokes are `DllImport` with no marshalling to speak of — a void with no arguments and a bool with one
unsigned integer — so the App needs no `AllowUnsafeBlocks` and the single-file publish is unaffected. Neither
call needs a file, which is what makes the system alert the right first sound for an app that publishes with
`PublishSingleFile`.

**`BellThrottle`**, pure:

```csharp
/// <summary>At most one bell per interval. Later bells inside the interval are refused, not queued.</summary>
public sealed class BellThrottle(TimeSpan minimum, Func<long> timestamp)
{
    public BellThrottle(TimeSpan minimum) : this(minimum, Stopwatch.GetTimestamp) { }
    public bool TryAdmit();
}
```

The timestamp source is injected so its tests use explicit times and never sleep. The first call is always
admitted. A call exactly at the boundary is admitted: "at most two per second" means the second may land at
500 ms.

**`FakeBellRinger`** (`tests/LizTerm.App.Tests/Fakes/`) records every `Ring` argument in a list.

### 3.4 The session view model

`SessionViewModel` takes an optional `IBellRinger? bellRinger = null`, last in the constructor after `settings`,
following the pattern every other seam uses: null in tests that do not care, `FakeBellRinger` in the ones that
do. `App.OpenSession` passes `new SystemBellRinger()`.

It subscribes to `BellRang` with a stored delegate, marshals through `dispatch` like the other five, and
unsubscribes in `DisposeAsync`. On the UI thread it does three things in order:

1. Return if disposed. Every marshalled handler does this; a bell arriving as the window closes must not touch a
   control that is going away.
2. Ask the throttle. If refused, stop.
3. Act on the settings, each independently: if `Settings.VisualBell` is on, raise the view model's own
   `BellRang` event (also a plain `EventHandler`) for the window; if `Settings.BellSound` is not `None` and a
   ringer was injected, call `Ring(Settings.BellSound)`.

A ringer that throws — a P/Invoke failure on an exotic platform — is caught and put in `ErrorMessage` once, the
way the clipboard's failures are, rather than left to take down the dispatcher. The visual half has nothing
that can throw.

**The rate limit.** The throttle's interval is a `public static readonly TimeSpan BellInterval` of 500 ms on the
view model, beside `ConnectTimeout`. It sits in front of both the flash and the sound, as one gate, so a host in
a loop cannot turn the session into a strobe or the speaker into a buzzer. 500 ms caps the flash at two per
second, under WCAG 2.3.1's limit of three, and is long enough that a host ringing per rejected keystroke still
reads as "the bell is ringing" rather than as one long noise. Bells inside the interval are dropped, not queued:
a queued bell would ring for something the user has already moved past.

The throttle lives in the view model, not the control or the ringer, because it is one decision that governs
both outputs. A throttle per output could let the flash and the sound drift out of step.

## 4. The flash

`TerminalScreen` gains:

- `public static readonly TimeSpan BellFlashDuration = TimeSpan.FromMilliseconds(120);` beside `BlinkInterval`.
- `public void Flash()`: sets an internal `BellFlashing` flag, calls `InvalidateVisual()`, and starts (or
  restarts) a one-shot `DispatcherTimer` of `BellFlashDuration` whose tick clears the flag and invalidates again.
  A second `Flash()` during a flash restarts the timer; the throttle makes that unreachable from the bell, but
  the method should not misbehave if something else calls it.
- In `Render`, after everything else has been drawn — text, crosshair, selection, find highlights — a
  translucent light overlay filled across the control's bounds while the flag is set. A new `Palette.BellFlash`
  brush, white at roughly 35% opacity, so the screen visibly lights up without inverting to a white slab.

The flag is exposed internally like `BlinkTimerRunning` and `BlinkHidden`, so a headless test can call
`Flash()`, see the flag, and wait for it to clear.

Detaching from the visual tree stops the timer and clears the flag, as `OnDetachedFromVisualTree` already does
for the blink timer, so a window closing mid-flash leaves nothing ticking.

`SessionWindow` subscribes to the view model's `BellRang` when its `DataContext` becomes a `SessionViewModel`
and calls `Screen.Flash()`. It already reaches into the screen for focus after Dismiss; this is the same reach.
The handler is stored and unsubscribed when the data context changes or the window closes, so a view model
outliving a window cannot flash a control that is gone.

**Why a flash, not a status-bar indicator.** The flash is what a terminal user expects: xterm and x3270's
`visualBell` both do it, and it is hard to miss while looking at the screen. A lingering indicator for someone
who glanced away was considered and deferred (§8); it would be an addition to this design, not a change to it.

**Why the flash is in the control and not the view model.** A bound `IsFlashing` bool with a timer in the view
model would couple the view model to UI timing when the control already owns a `DispatcherTimer` for blink and
already knows how to invalidate itself.

## 5. The Preferences window

`PreferencesWindow.axaml` gains a "Bell" group under Blink, in the same visual shape:

- A `TextBlock` heading, "Bell".
- A `CheckBox`, "Flash the screen when the host rings the bell", two-way bound to `VisualBell`, like the Blink
  box.
- Two `RadioButton`s in a `BellSound` group, "No sound" and "System alert sound", one-way `IsChecked` through a
  new `BellSoundConverter` with `Click` handlers writing the enum — the Crosshair radios' shape, for the reason
  `CrosshairModeConverter` documents: two-way bools over one enum cannot drive each other. `BellSoundConverter`
  sits beside `CrosshairModeConverter` in `ViewModels/` and follows it exactly, including throwing on a
  misspelled parameter.
- A `TextBlock` beneath the radios, "The system alert sound is not available on Linux.", visible only when the
  radio is disabled.

**Platform rule.** `BellSupport.SystemAlertAvailable(bool isMacOS, bool isWindows)` — a pure static method in
`Bell/`, the shape `MenuStrategy.AboutInHelpMenu(bool isMacOS)` uses so every combination is testable on every
machine. It answers `isMacOS || isWindows`. `PreferencesWindow`'s constructor applies it: on a platform where it
is false, the "System alert sound" radio is disabled and the note is shown. The radio still renders the saved
value when disabled, so a Linux user whose file says `SystemAlert` (one exported from a Mac later, under #55)
sees what the file says, hears nothing, and gets no error. The setting is not rewritten: their file is theirs.

The WAV round adds a third radio and a Choose... button to the same group, and nothing above moves.

## 6. Testing and verification

### 6.1 Backend

- `IndicationParserTests`: `{"bell":{}}` parses to `BellIndication`. `Unknown_indications_are_reported_by_name`
  keeps `stats` and loses its bell line, deliberately; the commit message says why.
- `ReplayTests`: replaying `ibmlink-help.jsonl` raises `BellRang` exactly once. This is the end-to-end proof
  against a real recorded host, and it costs one counter and one assertion.

### 6.2 Core

- `SettingsStoreTests`: both new fields round-trip; a file without them reads as the defaults.
- `SettingsLayersTests`: the existing unknown-enum test (`"crosshair":"Diagonal"`) gains a sibling for
  `"bellSound":"SoundFile"`, asserting `None` for that key and the other keys intact. That is the test that
  proves the WAV round is safe to ship as a file-format change.

### 6.3 App

- `SettingsViewModelTests`: `VisualBell` and `BellSound` write through and skip unchanged values, as `Blink`
  does.
- `BellThrottleTests`: with an injected clock, the first call is admitted, a second inside the interval is
  refused, one exactly at the boundary is admitted, and one after it is admitted.
- `SessionViewModelBellTests`, with `FakeEmulatorSession.RaiseBell()` and `FakeBellRinger`: visual on raises
  the view model's `BellRang`; `SystemAlert` calls the ringer with `SystemAlert`; `None` never calls it; visual
  off never raises; a second bell inside the interval is dropped; nothing happens after `DisposeAsync`; a
  ringer that throws puts its message in `ErrorMessage`.
- `TerminalScreenBellTests`, headless: `Flash()` sets `BellFlashing`, and it clears within `BellFlashDuration`
  plus a margin; `BellFlashDuration` is 120 ms, the way `TerminalScreenBlinkTests` pins `BlinkInterval`.
- `BellSoundConverterTests`: the shape of `CrosshairModeConverterTests`.
- `BellSupportTests`: the four platform combinations.
- `PreferencesWindowTests`: the checkbox and the radios drive the settings by click (raising `Button.ClickEvent`,
  per `tests/CLAUDE.md`), and the radio for the saved value is checked on open.
- `SessionWindowTests`: a view model `BellRang` sets the screen's `BellFlashing`.
- `SystemBellRingerTests`: `Ring(SystemAlert)` does not throw on the current platform. There is no way to assert
  a sound, and that is the point of the seam.

### 6.4 Manual, on macOS

Connect to a host that rings (the ibmlink session in the fixture did; a rejected key on many hosts does), confirm
the flash; turn on the system
alert in Preferences, confirm the Mac's alert sound at the Mac's alert volume; turn off interface sounds in
System Settings, confirm silence; hold a key in a protected field, confirm the flash stays readable rather than
strobing.

## 7. What does not change

`SessionProfile`, the profile editor, and the picker. The engine and the wire log, apart from the one new line
they already carry. The View menu. The error banner: a bell never puts text there unless the ringer itself
fails. Background windows: a session that is not the active window flashes its own screen and rings its own
sound and nothing else, which is the cheaper and safer first version the issue asked for.

## 8. Out of scope

- **A user-chosen sound file.** The `SoundFile` enum member, the `BellSoundFile` field, the Choose... button,
  and a player per platform (`afplay`, `PlaySound`, and whatever a Linux desktop offers). Tracked on #19 with the
  other future preferences; §2.1 and §3.3 are the hooks it plugs into.
- **A lingering status-bar indicator** after the flash.
- **Attention outside the window**: Dock badge, taskbar flash, notification.
- **A per-profile override** of either setting.
- **A View menu entry** for the bell.
- **Linux audio** of any kind, until a route exists that a tarball user is guaranteed to have.

## 9. Documentation and bookkeeping

Each fact in its home:

- `docs/user-guide.md`: a paragraph under "The session window" saying the screen flashes when the host rings
  the bell and that a sound can be turned on in Preferences; two lines under "Preferences" for the new controls,
  with the Linux note.
- `src/LizTerm.App/CLAUDE.md`: `IBellRinger` joins the list of injected seams in "Session view model", and a
  short "Bell" entry covers the throttle, the flash and the None rule.
- `src/LizTerm.Backend.B3270/CLAUDE.md`: the "Protocol" line listing what `popup` and `ui-error` become gains
  `bell` raising `BellRang`.
- `tests/CLAUDE.md`: `RaiseBell` joins `FakeEmulatorSession`'s list and `FakeBellRinger` joins the other fakes.
- This spec gains an "As built" section when the work lands, as the settings spec did.

The work tracks as #47 and closes it. The WAV option becomes a comment on #19. The version stays 0.4.1 in the
PR; the bump happens at release time.
