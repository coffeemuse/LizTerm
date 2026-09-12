# LizTerm terminal bell — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make b3270's `{"bell":{}}` indication ring: a 120 ms screen flash by default, the platform's own alert sound as an opt-in, both behind one 500 ms throttle, with two new fields in the settings store and a Bell group in Preferences.

**Architecture:** The backend parses `bell` into a `BellIndication` and raises a new sixth `IEmulatorSession` event, `BellRang`, on its reader thread. `SessionViewModel` marshals it, asks a pure `BellThrottle`, then acts on `Settings.VisualBell` (raise its own `BellRang` for the window) and `Settings.BellSound` (call an injected `IBellRinger`). `TerminalScreen.Flash()` paints a translucent overlay for `BellFlashDuration`; `SystemBellRinger` P/Invokes `NSBeep` or `MessageBeep` and does nothing on Linux. `AppSettings` gains `VisualBell` and a `BellSound` enum written by name, so a later sound-file option is one enum member and one field.

**Tech Stack:** .NET 10, C# 14, Avalonia 12.1.2 (headless XUnit lane), CommunityToolkit.Mvvm, System.Text.Json source generation, xunit.v3, `DllImport` P/Invoke.

**Spec:** `docs/superpowers/specs/2026-09-11-lizterm-bell-design.md`

## Global Constraints

- **Worktree only.** All work happens in `/Users/robert/ClaudeSandbox/LizTerm/.claude/worktrees/meshtastic-adoption-maturity-0653f9`, on branch `claude/issue-47-e7b54d`. Never run git in, or write to, the main checkout at `/Users/robert/ClaudeSandbox/LizTerm`.
- **Robert's gates.** Pushing, opening or changing a pull request, and bumping the version each wait for Robert's explicit go-ahead in chat. The version stays 0.4.1 (spec §9).
- **Dependency rule.** `LizTerm.Core` names nothing from Avalonia or b3270. `LizTerm.Backend.B3270` is the only project that knows b3270 exists. The App names the backend only in `SessionFactory.cs`. Nothing in this plan touches `SessionProfile`.
- **Threading contract.** The backend raises `BellRang` on its reader thread, in order with the other events. The view model marshals through `dispatch`, as it does for the other five.
- **Licence header.** Every new `.cs` and `.axaml` file starts with the three lines `This file is part of LizTerm.`, `Copyright 2026 by CoffeeMuse`, `SPDX-License-Identifier: BSD-3-Clause` in the file's comment syntax (`//` for C#, inside `<!-- -->` before the root element for XAML). `RepositoryHeadersTests` fails the suite otherwise.
- **Test commands.** Full suite: `LIZTERM_B3270_PATH=/opt/homebrew/bin/b3270 dotnet test LizTerm.slnx` (this worktree has no `native/out`; the live-host tests skip themselves). One class: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~BellThrottleTests"`. The App tests run on Avalonia's headless platform; control and window tests use `[AvaloniaFact]`, view-model tests plain `[Fact]`.
- **Zero warnings.** Before any task is called done: `dotnet build LizTerm.slnx --no-incremental 2>&1 | grep -c " warning "` prints `0`. CI builds with `-warnaserror`.
- **Menus.** This plan adds no menu items. `NativeMenuTests.The_native_menu_matches_the_classic_menu_item_for_item` must keep passing untouched.
- **Design history is a record.** Edit nothing under `docs/superpowers/` except the "As built" section Task 10 appends to this slice's spec.
- **Each fact has one home** (root `CLAUDE.md`). Task 10 updates the user guide and the three `CLAUDE.md` notes; earlier tasks change no documentation.
- **Exact strings.** The Preferences copy is fixed by spec §5: heading `Bell`; checkbox `Flash the screen when the host rings the bell`; radios `No sound` and `System alert sound`; note `The system alert sound is not available on Linux.`
- **Commits** end with the line `Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>`.

---

## File Structure

**Created**

| File | Responsibility |
|---|---|
| `src/LizTerm.Core/Settings/BellSound.cs` | The enum: `None`, `SystemAlert` (Task 1). |
| `src/LizTerm.App/Bell/IBellRinger.cs` | The seam: `void Ring(BellSound sound)` (Task 4). |
| `src/LizTerm.App/Bell/SystemBellRinger.cs` | The only P/Invokes in the App: `NSBeep`, `MessageBeep`; no-op elsewhere (Task 4). |
| `src/LizTerm.App/Bell/BellThrottle.cs` | Pure: at most one admission per interval over an injected timestamp source (Task 4). |
| `src/LizTerm.App/Bell/BellSupport.cs` | Pure platform rule: is the system alert available here (Task 8). |
| `src/LizTerm.App/ViewModels/BellSoundConverter.cs` | "Is the sound this one?" for a radio's one-way `IsChecked` (Task 8). |
| `tests/LizTerm.App.Tests/Fakes/FakeBellRinger.cs` | Records every `Ring` argument; optional exception (Task 4). |
| `tests/LizTerm.App.Tests/Bell/BellThrottleTests.cs` | Task 4. |
| `tests/LizTerm.App.Tests/Bell/SystemBellRingerTests.cs` | Task 4. |
| `tests/LizTerm.App.Tests/Bell/BellSupportTests.cs` | Task 8. |
| `tests/LizTerm.App.Tests/ViewModels/SessionViewModelBellTests.cs` | Task 5. |
| `tests/LizTerm.App.Tests/ViewModels/BellSoundConverterTests.cs` | Task 8. |
| `tests/LizTerm.App.Tests/Controls/TerminalScreenBellTests.cs` | Task 6. |

**Modified**

| File | Change |
|---|---|
| `src/LizTerm.Core/Settings/AppSettings.cs` | Two positional parameters, `VisualBell` and `BellSound` (Task 1). |
| `src/LizTerm.App/ViewModels/SettingsViewModel.cs` | `VisualBell` and `BellSound` properties (Task 2). |
| `src/LizTerm.Backend.B3270/Protocol/Indications.cs` | `BellIndication` (Task 3). |
| `src/LizTerm.Backend.B3270/Protocol/IndicationParser.cs` | `"bell"` case (Task 3). |
| `src/LizTerm.Core/Session/IEmulatorSession.cs` | `event EventHandler? BellRang` (Task 3). |
| `src/LizTerm.Backend.B3270/B3270Session.cs` | Declares the event, raises it in `HandleStateIndication` (Task 3). |
| `tests/LizTerm.App.Tests/Fakes/FakeEmulatorSession.cs` | Declares the event, `RaiseBell()` (Task 3). |
| `src/LizTerm.App/ViewModels/SessionViewModel.cs` | `IBellRinger?` parameter, `BellInterval`, subscription, `OnBell`, own `BellRang` event (Task 5). |
| `src/LizTerm.App/Controls/TerminalScreen.cs` | `BellFlashDuration`, `Flash()`, `BellFlashing`, overlay in `Render` (Task 6). |
| `src/LizTerm.App/Rendering/Palette.cs` | `BellFlash` brush (Task 6). |
| `src/LizTerm.App/Views/SessionWindow.axaml.cs` | Subscribes to the view model's `BellRang`, calls `Screen.Flash()` (Task 7). |
| `src/LizTerm.App/App.axaml.cs` | Passes `new SystemBellRinger()` (Task 7). |
| `src/LizTerm.App/Views/PreferencesWindow.axaml` + `.axaml.cs` | The Bell group, platform rule (Task 8). |
| `tests/LizTerm.Backend.B3270.Tests/Protocol/IndicationParserTests.cs` | Bell parses; unknown test loses its bell line (Task 3). |
| `tests/LizTerm.Backend.B3270.Tests/ReplayTests.cs` | ibmlink raises `BellRang` once (Task 3). |
| `tests/LizTerm.Core.Tests/Settings/SettingsLayersTests.cs`, `SettingsStoreTests.cs` | New fields (Task 1). |
| `tests/LizTerm.App.Tests/ViewModels/SettingsViewModelTests.cs` | New properties (Task 2). |
| `tests/LizTerm.App.Tests/Views/SessionWindowTests.cs` | Bell flashes the screen (Task 7). |
| `tests/LizTerm.App.Tests/Views/PreferencesWindowTests.cs` | The Bell group (Task 8). |
| `docs/user-guide.md`, `src/LizTerm.App/CLAUDE.md`, `src/LizTerm.Backend.B3270/CLAUDE.md`, `tests/CLAUDE.md`, the spec | Task 10. |

---

### Task 1: `BellSound` and the two `AppSettings` fields

**Files:**
- Create: `src/LizTerm.Core/Settings/BellSound.cs`
- Modify: `src/LizTerm.Core/Settings/AppSettings.cs`
- Test: `tests/LizTerm.Core.Tests/Settings/SettingsLayersTests.cs`, `tests/LizTerm.Core.Tests/Settings/SettingsStoreTests.cs`

**Interfaces:**
- Produces: `enum BellSound { None, SystemAlert }` in `LizTerm.Core.Settings`; `AppSettings(CrosshairMode Crosshair = None, bool Blink = true, bool VisualBell = true, BellSound BellSound = BellSound.None)`. Every later task uses these two names.

- [ ] **Step 1: Write the failing tests**

Add to `tests/LizTerm.Core.Tests/Settings/SettingsLayersTests.cs`, after `A_key_whose_value_will_not_read_is_dropped_and_the_rest_are_kept`:

```csharp
    /// <summary>The bell's sound is an enum so a later build can add a sound-file member (bell spec §2.1). This is
    /// the test that makes that safe: a name this build does not know costs that key alone.</summary>
    [Fact]
    public void A_bell_sound_this_build_does_not_know_falls_to_None_and_the_rest_are_kept()
    {
        Assert.Equal(new AppSettings(VisualBell: false), SettingsLayers.Read(Doc("""{"visualBell":false,"bellSound":"SoundFile"}""")));
    }

    [Fact]
    public void The_bell_fields_read_by_name_and_default_on()
    {
        Assert.Equal(new AppSettings(VisualBell: false, BellSound: BellSound.SystemAlert),
            SettingsLayers.Read(Doc("""{"visualBell":false,"bellSound":"SystemAlert"}""")));
        Assert.Equal(new AppSettings(), SettingsLayers.Read(Doc("""{}""")));
    }
```

Add to `tests/LizTerm.Core.Tests/Settings/SettingsStoreTests.cs`, after `Update_then_Load_round_trips_and_writes_the_enum_by_name` (copy its shape for constructing the store; it uses a `FilePath` field in a temp directory):

```csharp
    [Fact]
    public void The_bell_fields_round_trip_and_the_sound_is_written_by_name()
    {
        var store = new SettingsStore(FilePath);

        store.Update(s => s with { VisualBell = false, BellSound = BellSound.SystemAlert });

        Assert.Equal(new AppSettings(VisualBell: false, BellSound: BellSound.SystemAlert), store.Load());
        Assert.Contains("\"bellSound\": \"SystemAlert\"", File.ReadAllText(FilePath));
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~Settings"`
Expected: build error, `BellSound` and `VisualBell` do not exist.

- [ ] **Step 3: Create the enum**

`src/LizTerm.Core/Settings/BellSound.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

namespace LizTerm.Core.Settings;

/// <summary>What the bell sounds like. An enum rather than a bool so a user-chosen sound file can join later as one
/// more member and one more path field, with no change to the existing ones (bell spec §2.1). Written to the
/// settings file by name; a member an older build does not know falls to None for that key alone.</summary>
public enum BellSound
{
    None,
    /// <summary>The platform's own alert sound: NSBeep on macOS, MessageBeep on Windows, nothing on Linux.</summary>
    SystemAlert,
}
```

- [ ] **Step 4: Extend the record**

Replace the record in `src/LizTerm.Core/Settings/AppSettings.cs` (keep the file header and the existing summary comment):

```csharp
public sealed record AppSettings(
    [property: JsonConverter(typeof(JsonStringEnumConverter<CrosshairMode>))] CrosshairMode Crosshair = CrosshairMode.None,
    bool Blink = true,
    bool VisualBell = true,
    [property: JsonConverter(typeof(JsonStringEnumConverter<BellSound>))] BellSound BellSound = BellSound.None);
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~Settings"`
Expected: all pass, including the pre-existing ones (the record's `Equals` still compares every field).

- [ ] **Step 6: Commit**

```bash
git add src/LizTerm.Core/Settings/BellSound.cs src/LizTerm.Core/Settings/AppSettings.cs tests/LizTerm.Core.Tests/Settings/
git commit -m "Add VisualBell and BellSound to the settings record

The sound is an enum written by name so a sound-file member can join
later without changing what exists (bell spec §2.1).

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 2: `SettingsViewModel.VisualBell` and `.BellSound`

**Files:**
- Modify: `src/LizTerm.App/ViewModels/SettingsViewModel.cs`
- Test: `tests/LizTerm.App.Tests/ViewModels/SettingsViewModelTests.cs`

**Interfaces:**
- Consumes: `AppSettings.VisualBell`, `AppSettings.BellSound` (Task 1).
- Produces: `bool SettingsViewModel.VisualBell { get; set; }`, `BellSound SettingsViewModel.BellSound { get; set; }`, each raising `PropertyChanged` with its own name and writing through the store.

- [ ] **Step 1: Write the failing test**

Add to `tests/LizTerm.App.Tests/ViewModels/SettingsViewModelTests.cs`, after `A_store_backed_instance_writes_through_and_a_fresh_one_reads_it_back`:

```csharp
    [Fact]
    public void The_bell_settings_write_through_raise_their_own_names_and_skip_unchanged_values()
    {
        var settings = new SettingsViewModel(new SettingsStore(FilePath));
        var changes = Changes(settings);

        settings.VisualBell = true;                 // already the default: nothing
        settings.BellSound = BellSound.None;        // already the default: nothing
        Assert.Empty(changes);
        Assert.False(File.Exists(FilePath));

        settings.VisualBell = false;
        settings.BellSound = BellSound.SystemAlert;

        Assert.Equal(["VisualBell", "BellSound"], changes);
        var reloaded = new SettingsViewModel(new SettingsStore(FilePath));
        Assert.False(reloaded.VisualBell);
        Assert.Equal(BellSound.SystemAlert, reloaded.BellSound);
    }
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~SettingsViewModelTests"`
Expected: build error, `VisualBell` is not a member of `SettingsViewModel`.

- [ ] **Step 3: Add the properties**

In `src/LizTerm.App/ViewModels/SettingsViewModel.cs`, after the `Blink` property and before `Apply`:

```csharp
    public bool VisualBell
    {
        get => Current.VisualBell;
        set
        {
            if (Current.VisualBell != value) Apply(nameof(VisualBell), s => s with { VisualBell = value });
        }
    }

    public BellSound BellSound
    {
        get => Current.BellSound;
        set
        {
            if (Current.BellSound != value) Apply(nameof(BellSound), s => s with { BellSound = value });
        }
    }
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~SettingsViewModelTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/LizTerm.App/ViewModels/SettingsViewModel.cs tests/LizTerm.App.Tests/ViewModels/SettingsViewModelTests.cs
git commit -m "Expose VisualBell and BellSound on the settings view model

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 3: `bell` becomes a real indication and a sixth session event

**Files:**
- Modify: `src/LizTerm.Backend.B3270/Protocol/Indications.cs`, `src/LizTerm.Backend.B3270/Protocol/IndicationParser.cs`, `src/LizTerm.Core/Session/IEmulatorSession.cs`, `src/LizTerm.Backend.B3270/B3270Session.cs:137-141` (event declarations) and `:535-575` (`HandleStateIndication`), `tests/LizTerm.App.Tests/Fakes/FakeEmulatorSession.cs`
- Test: `tests/LizTerm.Backend.B3270.Tests/Protocol/IndicationParserTests.cs:202-208`, `tests/LizTerm.Backend.B3270.Tests/ReplayTests.cs:16-49`

**Interfaces:**
- Produces: `sealed record BellIndication : Indication`; `event EventHandler? BellRang` on `IEmulatorSession`, `B3270Session` and `FakeEmulatorSession`; `FakeEmulatorSession.RaiseBell()`.

- [ ] **Step 1: Change the parser tests**

In `tests/LizTerm.Backend.B3270.Tests/Protocol/IndicationParserTests.cs`, replace `Unknown_indications_are_reported_by_name` (lines 202-208) with:

```csharp
    /// <summary>stats stays here because it is still genuinely unhandled. bell used to sit beside it; it is a
    /// real indication now (Parses_bell, below), which is what #47 was about.</summary>
    [Fact]
    public void Unknown_indications_are_reported_by_name()
    {
        var u = Parse<UnknownIndication>("""{"stats":{"bytes-received":107,"records-received":1}}""");
        Assert.Equal("stats", u.Name);
    }

    [Fact]
    public void Parses_bell()
    {
        Parse<BellIndication>("""{"bell":{}}""");
    }
```

- [ ] **Step 2: Add the replay assertion**

In `tests/LizTerm.Backend.B3270.Tests/ReplayTests.cs`, inside `Ibmlink_help_screen_replays_to_expected_state`, add a counter beside `screens`:

```csharp
        var screens = 0;
        var bells = 0;
        session.ConnectionChanged += (_, s) => states.Add(s);
        session.ScreenUpdated += (_, _) => screens++;
        session.BellRang += (_, _) => bells++;
```

and, after `Assert.True(screens > 0);`:

```csharp
        // Line 30 of the fixture is {"bell":{}}: a real host rang it, and this is the end-to-end proof it reaches
        // the session's subscribers (#47).
        Assert.Equal(1, bells);
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/LizTerm.Backend.B3270.Tests --filter "FullyQualifiedName~IndicationParserTests|FullyQualifiedName~ReplayTests"`
Expected: build error, `BellIndication` and `BellRang` do not exist.

- [ ] **Step 4: The indication and its parse**

In `src/LizTerm.Backend.B3270/Protocol/Indications.cs`, after `PopupIndication`:

```csharp
/// <summary>The host rang the 3270 alarm. b3270 sends an empty body; there is nothing to carry.</summary>
public sealed record BellIndication : Indication;
```

In `src/LizTerm.Backend.B3270/Protocol/IndicationParser.cs`, in the `Parse` switch, after the `"popup"` line:

```csharp
        "bell" => new BellIndication(),
```

- [ ] **Step 5: The event on the interface, the backend and the fake**

In `src/LizTerm.Core/Session/IEmulatorSession.cs`, after `HostMessage`:

```csharp
    /// <summary>The host rang the 3270 alarm. Carries nothing: the bell has no text and no state, which is why it
    /// is not a HostMessage.</summary>
    event EventHandler? BellRang;
```

In `src/LizTerm.Backend.B3270/B3270Session.cs`, after `public event EventHandler<string>? HostMessage;` (line 141):

```csharp
    public event EventHandler? BellRang;
```

and in `HandleStateIndication`, after the `PopupIndication` case:

```csharp
            case BellIndication:
                BellRang?.Invoke(this, EventArgs.Empty);
                break;
```

In `tests/LizTerm.App.Tests/Fakes/FakeEmulatorSession.cs`, after `public event EventHandler<string>? HostMessage;`:

```csharp
    public event EventHandler? BellRang;
```

and after `RaiseHostMessage`:

```csharp
    public void RaiseBell() => BellRang?.Invoke(this, EventArgs.Empty);
```

- [ ] **Step 6: Run the tests to verify they pass, and that nothing else broke**

Run: `LIZTERM_B3270_PATH=/opt/homebrew/bin/b3270 dotnet test LizTerm.slnx`
Expected: all pass. (`grep -rn "IEmulatorSession" src tests --include='*.cs' -l` should show no other implementation than `B3270Session` and `FakeEmulatorSession`; if one exists, add the event there too.)

- [ ] **Step 7: Commit**

```bash
git add src/LizTerm.Backend.B3270/Protocol/Indications.cs src/LizTerm.Backend.B3270/Protocol/IndicationParser.cs src/LizTerm.Core/Session/IEmulatorSession.cs src/LizTerm.Backend.B3270/B3270Session.cs tests/LizTerm.App.Tests/Fakes/FakeEmulatorSession.cs tests/LizTerm.Backend.B3270.Tests/
git commit -m "Parse b3270's bell and raise it as IEmulatorSession.BellRang

The parser test that pinned bell as unknown now pins it as a real
indication; stats stays unknown because it still is. The ibmlink replay
asserts the recorded host's one bell reaches subscribers.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 4: The ringer seam: `IBellRinger`, `SystemBellRinger`, `BellThrottle`, `FakeBellRinger`

**Files:**
- Create: `src/LizTerm.App/Bell/IBellRinger.cs`, `src/LizTerm.App/Bell/SystemBellRinger.cs`, `src/LizTerm.App/Bell/BellThrottle.cs`, `tests/LizTerm.App.Tests/Fakes/FakeBellRinger.cs`
- Test: `tests/LizTerm.App.Tests/Bell/BellThrottleTests.cs`, `tests/LizTerm.App.Tests/Bell/SystemBellRingerTests.cs`

**Interfaces:**
- Consumes: `BellSound` (Task 1).
- Produces: `interface IBellRinger { void Ring(BellSound sound); }`; `sealed class SystemBellRinger : IBellRinger`; `sealed class BellThrottle(TimeSpan minimum, Func<long> timestamp)` with `BellThrottle(TimeSpan minimum)` and `bool TryAdmit()`; `FakeBellRinger` with `List<BellSound> Rings` and `Exception? Exception`.

- [ ] **Step 1: Write the failing throttle tests**

`tests/LizTerm.App.Tests/Bell/BellThrottleTests.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Diagnostics;
using LizTerm.App.Bell;

namespace LizTerm.App.Tests.Bell;

/// <summary>The one gate in front of both the flash and the sound (bell spec §3.4), driven by an injected clock so
/// nothing here sleeps.</summary>
public class BellThrottleTests
{
    private static readonly TimeSpan Half = TimeSpan.FromMilliseconds(500);
    private static long Ticks(double milliseconds) => (long)(milliseconds / 1000 * Stopwatch.Frequency);

    [Fact]
    public void The_first_bell_is_always_admitted()
    {
        var throttle = new BellThrottle(Half, () => Ticks(0));

        Assert.True(throttle.TryAdmit());
    }

    [Fact]
    public void A_bell_inside_the_interval_is_refused_and_one_at_the_boundary_is_admitted()
    {
        var now = 0.0;
        var throttle = new BellThrottle(Half, () => Ticks(now));
        Assert.True(throttle.TryAdmit());

        now = 499;
        Assert.False(throttle.TryAdmit());

        now = 500;
        Assert.True(throttle.TryAdmit());
    }

    /// <summary>Refused bells do not move the clock: the interval runs from the last bell that rang, so a host
    /// ringing every 100 ms still gets one bell through every 500 ms rather than none at all.</summary>
    [Fact]
    public void A_refused_bell_does_not_restart_the_interval()
    {
        var now = 0.0;
        var throttle = new BellThrottle(Half, () => Ticks(now));
        Assert.True(throttle.TryAdmit());

        for (now = 100; now < 500; now += 100) Assert.False(throttle.TryAdmit());

        now = 500;
        Assert.True(throttle.TryAdmit());
        now = 900;
        Assert.False(throttle.TryAdmit());
        now = 1000;
        Assert.True(throttle.TryAdmit());
    }

    [Fact]
    public void The_default_clock_is_the_stopwatch()
    {
        var throttle = new BellThrottle(Half);

        Assert.True(throttle.TryAdmit());
        Assert.False(throttle.TryAdmit());
    }
}
```

- [ ] **Step 2: Write the failing ringer smoke test**

`tests/LizTerm.App.Tests/Bell/SystemBellRingerTests.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.App.Bell;
using LizTerm.Core.Settings;

namespace LizTerm.App.Tests.Bell;

/// <summary>There is no way to assert a sound, which is the point of the IBellRinger seam. What can be asserted is
/// that the platform call is reachable and does not throw here: on macOS that is NSBeep, on Windows MessageBeep,
/// on Linux the deliberate no-op.</summary>
public class SystemBellRingerTests
{
    [Fact]
    public void Ring_does_not_throw_on_this_platform()
    {
        var ringer = new SystemBellRinger();

        ringer.Ring(BellSound.SystemAlert);
        ringer.Ring(BellSound.None);
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~LizTerm.App.Tests.Bell"`
Expected: build error, namespace `LizTerm.App.Bell` does not exist.

- [ ] **Step 4: The interface**

`src/LizTerm.App/Bell/IBellRinger.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.Core.Settings;

namespace LizTerm.App.Bell;

/// <summary>Makes the bell audible. Injected into SessionViewModel like the clipboard and the prompts, so the
/// platform calls stay out of the view model and "the host rang and we responded" is assertable without a
/// speaker. Called on the UI thread, and never with BellSound.None: "None means silence" is the view model's rule,
/// and a ringer only knows how to make sounds. Taking the sound rather than being parameterless is the hook for a
/// user-chosen file later (bell spec §3.3).</summary>
public interface IBellRinger
{
    void Ring(BellSound sound);
}
```

- [ ] **Step 5: The system ringer**

`src/LizTerm.App/Bell/SystemBellRinger.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Runtime.InteropServices;
using LizTerm.Core.Settings;

namespace LizTerm.App.Bell;

/// <summary>The platform's own alert sound, and the only P/Invokes in the App. NSBeep and MessageBeep play the sound
/// the user chose at the alert volume they chose, and stay silent when they have turned interface sounds off — a
/// bundled WAV would override all three (#47). Neither needs a file, which matters for a single-file publish.
/// Linux has no guaranteed audio path without a library dependency, so SystemAlert does nothing there; the
/// Preferences window says so (BellSupport). The imports are DllImport rather than LibraryImport on purpose: the
/// signatures are blittable, and LibraryImport's generated stub is unsafe code the App otherwise has no need
/// of.</summary>
public sealed class SystemBellRinger : IBellRinger
{
    private const uint MB_OK = 0;

    public void Ring(BellSound sound)
    {
        if (sound != BellSound.SystemAlert) return;
        if (OperatingSystem.IsMacOS()) NSBeep();
        else if (OperatingSystem.IsWindows()) MessageBeep(MB_OK);
    }

    [DllImport("/System/Library/Frameworks/AppKit.framework/AppKit", EntryPoint = "NSBeep")]
    private static extern void NSBeep();

    [DllImport("user32.dll", EntryPoint = "MessageBeep")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MessageBeep(uint uType);
}
```

- [ ] **Step 6: The throttle**

`src/LizTerm.App/Bell/BellThrottle.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Diagnostics;

namespace LizTerm.App.Bell;

/// <summary>At most one bell per interval, measured from the last bell that was admitted. Later bells inside the
/// interval are refused, not queued: a queued bell would ring for something the user has already moved past. A
/// bell exactly at the boundary is admitted, so "at most two per second" means the second may land at 500 ms.
/// The timestamp source is injected (Stopwatch ticks) so the tests use explicit times and never sleep. Not
/// thread-safe by design: SessionViewModel calls it on the UI thread only.</summary>
public sealed class BellThrottle(TimeSpan minimum, Func<long> timestamp)
{
    private readonly long _minimumTicks = (long)(minimum.TotalSeconds * Stopwatch.Frequency);
    private long? _lastAdmitted;

    public BellThrottle(TimeSpan minimum) : this(minimum, Stopwatch.GetTimestamp) { }

    public bool TryAdmit()
    {
        var now = timestamp();
        if (_lastAdmitted is { } last && now - last < _minimumTicks) return false;
        _lastAdmitted = now;
        return true;
    }
}
```

- [ ] **Step 7: The fake**

`tests/LizTerm.App.Tests/Fakes/FakeBellRinger.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.App.Bell;
using LizTerm.Core.Settings;

namespace LizTerm.App.Tests.Fakes;

public sealed class FakeBellRinger : IBellRinger
{
    public List<BellSound> Rings { get; } = [];
    /// <summary>When set, Ring throws it after recording the call.</summary>
    public Exception? Exception { get; set; }

    public void Ring(BellSound sound)
    {
        Rings.Add(sound);
        if (Exception is not null) throw Exception;
    }
}
```

- [ ] **Step 8: Run the tests to verify they pass**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~LizTerm.App.Tests.Bell"`
Expected: 5 pass. On this Mac the smoke test beeps once; that is the test working.

- [ ] **Step 9: Zero-warning check**

Run: `dotnet build LizTerm.slnx --no-incremental 2>&1 | grep -c " warning "`
Expected: `0`. If a `SYSLIB1054` (prefer LibraryImport) warning appears, it has been raised from its default Info severity somewhere; suppress it on the two imports with `#pragma warning disable SYSLIB1054` and a comment pointing at the class summary, rather than switching to `LibraryImport`.

- [ ] **Step 10: Commit**

```bash
git add src/LizTerm.App/Bell/ tests/LizTerm.App.Tests/Bell/ tests/LizTerm.App.Tests/Fakes/FakeBellRinger.cs
git commit -m "Add the bell seam: IBellRinger, SystemBellRinger and BellThrottle

NSBeep on macOS, MessageBeep on Windows, nothing on Linux; one throttle
over an injected clock so its tests never sleep.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 5: The session view model rings

**Files:**
- Modify: `src/LizTerm.App/ViewModels/SessionViewModel.cs` — usings (line 6-15), fields (line 21-38), `ConnectTimeout` region (line 40-44), constructor (line 100-162), `DisposeAsync` (line 668-680)
- Test: `tests/LizTerm.App.Tests/ViewModels/SessionViewModelBellTests.cs`

**Interfaces:**
- Consumes: `IEmulatorSession.BellRang`, `FakeEmulatorSession.RaiseBell()` (Task 3); `IBellRinger`, `BellThrottle`, `FakeBellRinger` (Task 4); `SettingsViewModel.VisualBell`, `.BellSound` (Task 2).
- Produces: constructor parameter `IBellRinger? bellRinger = null` after `settings`; `public static readonly TimeSpan BellInterval`; `public event EventHandler? BellRang` on the view model (post-throttle, only when `VisualBell` is on).

- [ ] **Step 1: Write the failing tests**

`tests/LizTerm.App.Tests/ViewModels/SessionViewModelBellTests.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.App.Tests.Fakes;
using LizTerm.App.ViewModels;
using LizTerm.Core.Settings;

namespace LizTerm.App.Tests.ViewModels;

/// <summary>The host rang and we responded, assertable without a speaker or a screen: the view model's own BellRang
/// stands for the flash, FakeBellRinger for the sound (bell spec §3.4).</summary>
public class SessionViewModelBellTests
{
    private static (SessionViewModel Vm, FakeEmulatorSession Session, FakeBellRinger Ringer, SettingsViewModel Settings, List<int> Flashes) Create()
    {
        var session = new FakeEmulatorSession();
        var ringer = new FakeBellRinger();
        var settings = new SettingsViewModel();
        var vm = new SessionViewModel(session, action => action(), new FakeTextClipboard(), settings: settings, bellRinger: ringer);
        var flashes = new List<int>();
        vm.BellRang += (_, _) => flashes.Add(flashes.Count);
        return (vm, session, ringer, settings, flashes);
    }

    [Fact]
    public void By_default_a_bell_flashes_and_makes_no_sound()
    {
        var (_, session, ringer, _, flashes) = Create();

        session.RaiseBell();

        Assert.Single(flashes);
        Assert.Empty(ringer.Rings);
    }

    [Fact]
    public void With_the_system_alert_chosen_a_bell_rings_it()
    {
        var (_, session, ringer, settings, flashes) = Create();
        settings.BellSound = BellSound.SystemAlert;

        session.RaiseBell();

        Assert.Single(flashes);
        Assert.Equal([BellSound.SystemAlert], ringer.Rings);
    }

    [Fact]
    public void With_the_flash_off_a_bell_raises_nothing_for_the_window()
    {
        var (_, session, ringer, settings, flashes) = Create();
        settings.VisualBell = false;
        settings.BellSound = BellSound.SystemAlert;

        session.RaiseBell();

        Assert.Empty(flashes);
        Assert.Equal([BellSound.SystemAlert], ringer.Rings);
    }

    /// <summary>One gate in front of both outputs: two bells in the same instant are one bell.</summary>
    [Fact]
    public void A_second_bell_inside_the_interval_is_dropped_for_both_outputs()
    {
        var (_, session, ringer, settings, flashes) = Create();
        settings.BellSound = BellSound.SystemAlert;

        session.RaiseBell();
        session.RaiseBell();

        Assert.Single(flashes);
        Assert.Single(ringer.Rings);
    }

    [Fact]
    public void The_interval_is_photosensitive_safe()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(500), SessionViewModel.BellInterval);
        Assert.True(SessionViewModel.BellInterval >= TimeSpan.FromMilliseconds(500), "WCAG 2.3.1: never more than three flashes per second");
    }

    [Fact]
    public async Task After_dispose_a_bell_does_nothing()
    {
        var (vm, session, ringer, settings, flashes) = Create();
        settings.BellSound = BellSound.SystemAlert;
        await vm.DisposeAsync();

        session.RaiseBell();

        Assert.Empty(flashes);
        Assert.Empty(ringer.Rings);
    }

    [Fact]
    public void A_ringer_that_throws_puts_its_message_in_the_banner_and_the_flash_still_happens()
    {
        var (vm, session, ringer, settings, flashes) = Create();
        settings.BellSound = BellSound.SystemAlert;
        ringer.Exception = new InvalidOperationException("no speaker");

        session.RaiseBell();

        Assert.Single(flashes);
        Assert.Equal("Could not play the bell: no speaker", vm.ErrorMessage);
    }

    [Fact]
    public void Without_a_ringer_the_sound_setting_is_ignored_and_the_flash_still_happens()
    {
        var session = new FakeEmulatorSession();
        var settings = new SettingsViewModel { BellSound = BellSound.SystemAlert };
        var vm = new SessionViewModel(session, action => action(), new FakeTextClipboard(), settings: settings);
        var flashes = 0;
        vm.BellRang += (_, _) => flashes++;

        session.RaiseBell();

        Assert.Equal(1, flashes);
        Assert.Null(vm.ErrorMessage);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~SessionViewModelBellTests"`
Expected: build error, no `bellRinger` parameter and no `BellRang` on `SessionViewModel`.

- [ ] **Step 3: The view model**

In `src/LizTerm.App/ViewModels/SessionViewModel.cs`:

Add the using, alphabetically among the `LizTerm.App.*` ones:

```csharp
using LizTerm.App.Bell;
```

Add fields after `private readonly Func<SessionProfile, Task>? _saveAsProfile;`:

```csharp
    private readonly IBellRinger? _bellRinger;
    private readonly BellThrottle _bellThrottle = new(BellInterval);
```

Add a delegate field after `_onHostMessage`:

```csharp
    private readonly EventHandler _onBellRang;
```

Add the constant after `ConnectTimeout`:

```csharp
    /// <summary>Minimum time between bells, one gate in front of both the flash and the sound (bell spec §3.4).
    /// 500 ms caps the flash at two per second, under WCAG 2.3.1's three; never take it below that.</summary>
    public static readonly TimeSpan BellInterval = TimeSpan.FromMilliseconds(500);
```

Add the event after the `Settings` property:

```csharp
    /// <summary>The host rang the bell, the throttle admitted it, and the visual bell is on: the window flashes the
    /// screen. Raised on the UI thread. Sound is not the window's business; the view model calls the ringer itself.</summary>
    public event EventHandler? BellRang;
```

Extend the constructor signature and doc:

```csharp
    /// <param name="bellRinger">Makes the bell audible when Settings.BellSound asks for it; null (tests, or a
    /// platform with nothing to ring) means the flash is the whole bell.</param>
    public SessionViewModel(IEmulatorSession session, Action<Action> dispatch, ITextClipboard clipboard,
        ICertificatePrompt? certificatePrompt = null, Action<SessionProfile>? saveProfile = null,
        IFolderOpener? folderOpener = null, ICertificateFetcher? certificateFetcher = null,
        Func<SessionProfile, Task>? saveAsProfile = null, SettingsViewModel? settings = null,
        IBellRinger? bellRinger = null)
```

and after `_saveAsProfile = saveAsProfile;`:

```csharp
        _bellRinger = bellRinger;
```

After the `_onHostMessage = ...;` assignment:

```csharp
        _onBellRang = (_, _) => _dispatch(OnBell);
```

After `session.HostMessage += _onHostMessage;`:

```csharp
        session.BellRang += _onBellRang;
```

Add the handler as a private method, near `DismissError`:

```csharp
    /// <summary>On the UI thread. Disposed first, throttle second, settings third, each output independently: the
    /// flash has nothing that can throw, and a ringer that does must not take the dispatcher down with it.</summary>
    private void OnBell()
    {
        if (_disposed) return;
        if (!_bellThrottle.TryAdmit()) return;
        if (Settings.VisualBell) BellRang?.Invoke(this, EventArgs.Empty);
        if (Settings.BellSound == BellSound.None || _bellRinger is null) return;
        try
        {
            _bellRinger.Ring(Settings.BellSound);
        }
        catch (Exception ex)
        {
            ErrorMessage = "Could not play the bell: " + ex.Message;
        }
    }
```

`BellSound` here is the enum in `LizTerm.Core.Settings`; add `using LizTerm.Core.Settings;` if the file does not already import it (it does not: check the usings block).

In `DisposeAsync`, after `_session.HostMessage -= _onHostMessage;`:

```csharp
        _session.BellRang -= _onBellRang;
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~SessionViewModel"`
Expected: all pass, the eight new ones and every existing `SessionViewModel*` class.

- [ ] **Step 5: Commit**

```bash
git add src/LizTerm.App/ViewModels/SessionViewModel.cs tests/LizTerm.App.Tests/ViewModels/SessionViewModelBellTests.cs
git commit -m "Ring the bell from the session view model

Marshalled like the other five events, throttled once for both outputs,
then the flash for the window and the sound through IBellRinger.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 6: `TerminalScreen.Flash()`

**Files:**
- Modify: `src/LizTerm.App/Controls/TerminalScreen.cs:76-131` (timer fields, constructor, attach/detach) and `:374-400` (`Render`); `src/LizTerm.App/Rendering/Palette.cs:17-26`
- Test: `tests/LizTerm.App.Tests/Controls/TerminalScreenBellTests.cs`

**Interfaces:**
- Produces: `public static readonly TimeSpan TerminalScreen.BellFlashDuration` (120 ms); `public void Flash()`; `internal bool BellFlashing { get; }`; `internal bool FlashTimerRunning`; `Palette.BellFlash` brush.

- [ ] **Step 1: Write the failing tests**

`tests/LizTerm.App.Tests/Controls/TerminalScreenBellTests.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using LizTerm.App.Controls;
using LizTerm.Core.Screen;

namespace LizTerm.App.Tests.Controls;

/// <summary>The visual bell: a flash the control owns end to end, like blink (bell spec §4).</summary>
public class TerminalScreenBellTests
{
    private static (TerminalScreen Screen, Window Window) Show()
    {
        var screen = new TerminalScreen { Snapshot = ScreenSnapshot.Empty(24, 80) };
        var window = new Window { Width = 800, Height = 600, Content = screen };
        window.Show();
        return (screen, window);
    }

    [AvaloniaFact]
    public void The_flash_is_brief()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(120), TerminalScreen.BellFlashDuration);
    }

    [AvaloniaFact]
    public async Task Flash_lights_the_screen_and_clears_on_its_own()
    {
        var (screen, window) = Show();
        Assert.False(screen.BellFlashing);

        screen.Flash();
        Assert.True(screen.BellFlashing);
        Assert.True(screen.FlashTimerRunning);
        TestRender.Repaint(window);

        for (var i = 0; i < 40 && screen.BellFlashing; i++)
        {
            await Task.Delay(25, TestContext.Current.CancellationToken);
            Dispatcher.UIThread.RunJobs();
        }
        Assert.False(screen.BellFlashing, "the flash must clear on its own");
        Assert.False(screen.FlashTimerRunning);
        TestRender.Repaint(window);
    }

    [AvaloniaFact]
    public void Leaving_the_tree_ends_a_flash_in_progress()
    {
        var (screen, window) = Show();
        screen.Flash();

        window.Content = null;

        Assert.False(screen.BellFlashing);
        Assert.False(screen.FlashTimerRunning);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~TerminalScreenBellTests"`
Expected: build error, `Flash`, `BellFlashing`, `FlashTimerRunning`, `BellFlashDuration` do not exist.

- [ ] **Step 3: The brush**

In `src/LizTerm.App/Rendering/Palette.cs`, after `FindCurrent`:

```csharp
    /// <summary>The visual bell: painted over the whole screen for TerminalScreen.BellFlashDuration. White at about
    /// 35% so the screen visibly lights up without inverting to a white slab (bell spec §4).</summary>
    public static readonly IBrush BellFlash = new ImmutableSolidColorBrush(Color.FromArgb(0x59, 0xFF, 0xFF, 0xFF));
```

- [ ] **Step 4: The control**

In `src/LizTerm.App/Controls/TerminalScreen.cs`:

After `BlinkInterval`:

```csharp
    /// <summary>How long the visual bell lights the screen. Brief enough to read as a flash, long enough to be
    /// seen; SessionViewModel.BellInterval keeps successive flashes at most two per second.</summary>
    public static readonly TimeSpan BellFlashDuration = TimeSpan.FromMilliseconds(120);
```

After `private readonly DispatcherTimer _blinkTimer = ...;`:

```csharp
    private readonly DispatcherTimer _flashTimer = new() { Interval = BellFlashDuration };
```

After `internal bool BlinkHidden { get; private set; }`:

```csharp
    /// <summary>True while the bell overlay is painted. Test seam, like BlinkHidden.</summary>
    internal bool BellFlashing { get; private set; }
    internal bool FlashTimerRunning => _flashTimer.IsEnabled;
```

In the constructor, after the blink tick subscription:

```csharp
        _flashTimer.Tick += (_, _) => EndFlash();
```

Add two methods after `UpdateBlinkTimer`:

```csharp
    /// <summary>The visual bell: lights the screen for BellFlashDuration, then clears it. A second call during a
    /// flash restarts the timer; the view model's throttle makes that unreachable from the bell, but the method must
    /// not misbehave if something else calls it.</summary>
    public void Flash()
    {
        BellFlashing = true;
        InvalidateVisual();
        _flashTimer.Stop();
        _flashTimer.Start();
    }

    private void EndFlash()
    {
        _flashTimer.Stop();
        if (!BellFlashing) return;
        BellFlashing = false;
        InvalidateVisual();
    }
```

In `OnDetachedFromVisualTree`, after `UpdateBlinkTimer(null);`:

```csharp
        EndFlash();
```

In `Render`, after `DrawCursor(context, snapshot, g);` (the last draw call in the method body):

```csharp
        if (BellFlashing) context.FillRectangle(Palette.BellFlash, bounds);
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~TerminalScreen"`
Expected: all pass, the three new ones and every existing `TerminalScreen*` class.

- [ ] **Step 6: Commit**

```bash
git add src/LizTerm.App/Controls/TerminalScreen.cs src/LizTerm.App/Rendering/Palette.cs tests/LizTerm.App.Tests/Controls/TerminalScreenBellTests.cs
git commit -m "Give TerminalScreen a Flash() for the visual bell

A translucent overlay for 120 ms on the control's own one-shot timer,
ended early by detaching, painted after every other overlay.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 7: The window flashes, and the app rings

**Files:**
- Modify: `src/LizTerm.App/Views/SessionWindow.axaml.cs:17-39` (constructor) and near `ViewModel` (line 309); `src/LizTerm.App/App.axaml.cs:113-140` (`OpenSession`)
- Test: `tests/LizTerm.App.Tests/Views/SessionWindowTests.cs`

**Interfaces:**
- Consumes: `SessionViewModel.BellRang` (Task 5), `TerminalScreen.Flash()` and `.BellFlashing` (Task 6), `SystemBellRinger` (Task 4).

- [ ] **Step 1: Write the failing test**

Add to `tests/LizTerm.App.Tests/Views/SessionWindowTests.cs`, after the `Center` helper:

```csharp
    /// <summary>The window's half of the bell: the view model's BellRang reaches Screen.Flash(). The sound never
    /// passes through the window (bell spec §4).</summary>
    [AvaloniaFact]
    public void A_bell_from_the_view_model_flashes_the_screen()
    {
        var (_, screen, _, session, _) = Show();
        Assert.False(screen.BellFlashing);

        session.RaiseBell();

        Assert.True(screen.BellFlashing);
    }

    /// <summary>A view model that outlives its window must not flash a control that is gone: swapping the data
    /// context unsubscribes from the old one.</summary>
    [AvaloniaFact]
    public void A_replaced_view_model_no_longer_reaches_the_screen()
    {
        var (window, screen, _, oldSession, _) = Show();
        var newVm = new SessionViewModel(new FakeEmulatorSession(), action => action(), new FakeTextClipboard());

        window.DataContext = newVm;
        oldSession.RaiseBell();

        Assert.False(screen.BellFlashing);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~SessionWindowTests"`
Expected: `A_bell_from_the_view_model_flashes_the_screen` fails on `Assert.True(screen.BellFlashing)`. The second test passes already, because nothing is subscribed yet; it earns its place once Step 3 adds the subscription it guards.

- [ ] **Step 3: The window**

In `src/LizTerm.App/Views/SessionWindow.axaml.cs`:

Add a field beside `_useNativeMenu`:

```csharp
    /// <summary>The view model whose BellRang this window is subscribed to, so a data-context swap can unsubscribe
    /// from the old one before a bell from it flashes a screen it no longer owns.</summary>
    private SessionViewModel? _bellSource;
```

Add an override after the `ViewModel` property:

```csharp
    /// <summary>Follows the data context for the one view-model event the window handles itself. Every other
    /// binding is XAML; the flash is a method call on the screen, which XAML cannot express.</summary>
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_bellSource is not null) _bellSource.BellRang -= OnBellRang;
        _bellSource = ViewModel;
        if (_bellSource is not null) _bellSource.BellRang += OnBellRang;
    }

    private void OnBellRang(object? sender, EventArgs e) => Screen.Flash();
```

- [ ] **Step 4: The app**

In `src/LizTerm.App/App.axaml.cs`, `OpenSession`, change the last argument of the `SessionViewModel` construction from

```csharp
            settings: Settings);
```

to

```csharp
            settings: Settings,
            bellRinger: new SystemBellRinger());
```

and add `using LizTerm.App.Bell;` to the file's usings.

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~SessionWindowTests|FullyQualifiedName~NativeMenuTests"`
Expected: all pass. The native/classic parity test is unaffected because no menu item changed.

- [ ] **Step 6: Commit**

```bash
git add src/LizTerm.App/Views/SessionWindow.axaml.cs src/LizTerm.App/App.axaml.cs tests/LizTerm.App.Tests/Views/SessionWindowTests.cs
git commit -m "Flash the screen on the view model's bell, and ring through SystemBellRinger

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 8: The Bell group in Preferences

**Files:**
- Create: `src/LizTerm.App/Bell/BellSupport.cs`, `src/LizTerm.App/ViewModels/BellSoundConverter.cs`
- Modify: `src/LizTerm.App/Views/PreferencesWindow.axaml` (after the Blink box), `src/LizTerm.App/Views/PreferencesWindow.axaml.cs`
- Test: `tests/LizTerm.App.Tests/Bell/BellSupportTests.cs`, `tests/LizTerm.App.Tests/ViewModels/BellSoundConverterTests.cs`, `tests/LizTerm.App.Tests/Views/PreferencesWindowTests.cs`

**Interfaces:**
- Consumes: `SettingsViewModel.VisualBell`, `.BellSound` (Task 2).
- Produces: `static bool BellSupport.SystemAlertAvailable(bool isMacOS, bool isWindows)`; `BellSoundConverter.Instance`; `internal PreferencesWindow(SettingsViewModel settings, bool systemAlertAvailable)`; named controls `VisualBellBox`, `BellSoundNone`, `BellSoundSystemAlert`, `BellSoundNote`.

- [ ] **Step 1: Write the failing pure tests**

`tests/LizTerm.App.Tests/Bell/BellSupportTests.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.App.Bell;

namespace LizTerm.App.Tests.Bell;

/// <summary>The platform rule for the Preferences radio, pure and taking the platform as an argument the way
/// MenuStrategy does, so every combination is reachable on every machine.</summary>
public class BellSupportTests
{
    [Theory]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(false, false, false)]
    [InlineData(true, true, true)]
    public void The_system_alert_exists_on_macOS_and_Windows_only(bool isMacOS, bool isWindows, bool expected)
    {
        Assert.Equal(expected, BellSupport.SystemAlertAvailable(isMacOS, isWindows));
    }
}
```

`tests/LizTerm.App.Tests/ViewModels/BellSoundConverterTests.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Globalization;
using LizTerm.App.ViewModels;
using LizTerm.Core.Settings;

namespace LizTerm.App.Tests.ViewModels;

/// <summary>BellSoundConverter.Convert backs each Preferences sound radio's IsChecked, keyed by a hand-written
/// ConverterParameter; the same contract as CrosshairModeConverter, including the loud failure on a typo.</summary>
public class BellSoundConverterTests
{
    private static readonly BellSoundConverter Converter = BellSoundConverter.Instance;

    [Fact]
    public void A_matching_sound_and_parameter_convert_to_true()
    {
        Assert.Equal(true, Converter.Convert(BellSound.SystemAlert, typeof(bool), "SystemAlert", CultureInfo.InvariantCulture));
    }

    [Fact]
    public void A_non_matching_sound_converts_to_false()
    {
        Assert.Equal(false, Converter.Convert(BellSound.SystemAlert, typeof(bool), "None", CultureInfo.InvariantCulture));
    }

    [Fact]
    public void A_parameter_that_is_not_a_sound_name_throws()
    {
        var ex = Assert.Throws<ArgumentException>(() => Converter.Convert(BellSound.None, typeof(bool), "Sytsem", CultureInfo.InvariantCulture));
        Assert.Contains("SystemAlert", ex.Message);
    }

    [Fact]
    public void A_missing_parameter_is_false_not_an_error()
    {
        Assert.Equal(false, Converter.Convert(BellSound.None, typeof(bool), null, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void ConvertBack_is_not_supported()
    {
        Assert.Throws<NotSupportedException>(() => Converter.ConvertBack(true, typeof(BellSound), "None", CultureInfo.InvariantCulture));
    }
}
```

- [ ] **Step 2: Write the failing window tests**

Add to `tests/LizTerm.App.Tests/Views/PreferencesWindowTests.cs`, after `The_blink_box_writes_through_and_follows_the_settings`:

```csharp
    [AvaloniaFact]
    public void The_visual_bell_box_writes_through_and_follows_the_settings()
    {
        var (window, settings) = Show();
        var box = window.FindControl<CheckBox>("VisualBellBox")!;
        Assert.True(box.IsChecked);

        box.IsChecked = false;
        Assert.False(settings.VisualBell);

        settings.VisualBell = true;
        Assert.True(box.IsChecked);
    }

    [AvaloniaFact]
    public void Clicking_a_sound_radio_sets_the_shared_settings_and_checks_exactly_that_radio()
    {
        var (window, settings) = Show();
        var none = window.FindControl<RadioButton>("BellSoundNone")!;
        var alert = window.FindControl<RadioButton>("BellSoundSystemAlert")!;
        Assert.True(none.IsChecked);
        Assert.False(alert.IsChecked);

        Click(alert);
        Assert.Equal(BellSound.SystemAlert, settings.BellSound);
        Assert.False(none.IsChecked);
        Assert.True(alert.IsChecked);

        Click(none);
        Assert.Equal(BellSound.None, settings.BellSound);
        Assert.True(none.IsChecked);
        Assert.False(alert.IsChecked);
    }

    [AvaloniaFact]
    public void The_sound_radios_follow_a_change_made_elsewhere()
    {
        var (window, settings) = Show();

        settings.BellSound = BellSound.SystemAlert;

        Assert.True(window.FindControl<RadioButton>("BellSoundSystemAlert")!.IsChecked);
        Assert.False(window.FindControl<RadioButton>("BellSoundNone")!.IsChecked);
    }

    /// <summary>Linux: the radio is disabled and says why, but a saved SystemAlert (a file exported from a Mac, one
    /// day) still shows as the value it is and is not rewritten (bell spec §5).</summary>
    [AvaloniaFact]
    public void Where_the_system_alert_is_unavailable_the_radio_is_disabled_with_a_note_and_a_saved_value_stays()
    {
        var settings = new SettingsViewModel { BellSound = BellSound.SystemAlert };
        var window = new PreferencesWindow(settings, systemAlertAvailable: false);
        window.Show();
        var alert = window.FindControl<RadioButton>("BellSoundSystemAlert")!;
        var note = window.FindControl<TextBlock>("BellSoundNote")!;

        Assert.False(alert.IsEnabled);
        Assert.True(alert.IsChecked);
        Assert.True(note.IsVisible);
        Assert.Equal("The system alert sound is not available on Linux.", note.Text);
        Assert.Equal(BellSound.SystemAlert, settings.BellSound);
    }

    [AvaloniaFact]
    public void Where_the_system_alert_is_available_the_radio_is_enabled_and_the_note_hidden()
    {
        var window = new PreferencesWindow(new SettingsViewModel(), systemAlertAvailable: true);
        window.Show();

        Assert.True(window.FindControl<RadioButton>("BellSoundSystemAlert")!.IsEnabled);
        Assert.False(window.FindControl<TextBlock>("BellSoundNote")!.IsVisible);
    }
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~BellSupportTests|FullyQualifiedName~BellSoundConverterTests|FullyQualifiedName~PreferencesWindowTests"`
Expected: build errors for `BellSupport`, `BellSoundConverter` and the two-argument `PreferencesWindow` constructor.

- [ ] **Step 4: The platform rule**

`src/LizTerm.App/Bell/BellSupport.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

namespace LizTerm.App.Bell;

/// <summary>Which bell sounds this platform can make. Pure, taking the platform rather than reading it, the shape
/// MenuStrategy.AboutInHelpMenu uses so every combination is testable on every machine.</summary>
internal static class BellSupport
{
    /// <summary>NSBeep and MessageBeep always exist; Linux has no guaranteed audio path without a library
    /// dependency (#47), so the Preferences radio is disabled there and says why.</summary>
    public static bool SystemAlertAvailable(bool isMacOS, bool isWindows) => isMacOS || isWindows;

    public static bool SystemAlertAvailableHere => SystemAlertAvailable(OperatingSystem.IsMacOS(), OperatingSystem.IsWindows());
}
```

- [ ] **Step 5: The converter**

`src/LizTerm.App/ViewModels/BellSoundConverter.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Globalization;
using Avalonia.Data.Converters;
using LizTerm.Core.Settings;

namespace LizTerm.App.ViewModels;

/// <summary>"Is the bell sound this one?", for a Preferences radio's IsChecked. One-way only, for the reason
/// CrosshairModeConverter gives: a radio group over one enum cannot be driven by independent two-way bools. The
/// Click handlers write the enum; these bindings render it. The parameter is programmer input from a hand-written
/// ConverterParameter, so a name that is present but wrong throws (a typo would otherwise build clean and leave
/// that radio unable to ever show as checked), while a missing one answers false.</summary>
public sealed class BellSoundConverter : IValueConverter
{
    public static readonly BellSoundConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var wanted = ParseParameter(parameter);
        return wanted is not null && value is BellSound sound && sound == wanted;
    }

    private static BellSound? ParseParameter(object? parameter) => parameter switch
    {
        null => null,
        string name when Enum.TryParse<BellSound>(name, out var wanted) => wanted,
        _ => throw new ArgumentException(
            $"'{parameter}' is not a BellSound. Valid names: " + string.Join(", ", Enum.GetNames<BellSound>()) + ".",
            nameof(parameter)),
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("Bell sound radios are one-way; the Click handlers set the sound.");
}
```

- [ ] **Step 6: The window markup**

In `src/LizTerm.App/Views/PreferencesWindow.axaml`, after the `BlinkBox` line and before the `SaveErrorText` comment:

```xml
    <TextBlock Text="Bell" FontWeight="SemiBold" Margin="0,10,0,0" />
    <CheckBox x:Name="VisualBellBox" Content="Flash the screen when the host rings the bell" IsChecked="{Binding VisualBell, Mode=TwoWay}" />
    <!-- The same one-way-plus-Click shape as the Crosshair radios above, over BellSoundConverter. A third radio
         and a Choose... button for a sound file join this group later (bell spec §8). -->
    <RadioButton x:Name="BellSoundNone" GroupName="BellSound" Content="No sound" Click="OnBellSoundNoneClick"
                 IsChecked="{Binding BellSound, Converter={x:Static vm:BellSoundConverter.Instance}, ConverterParameter=None, Mode=OneWay}" />
    <RadioButton x:Name="BellSoundSystemAlert" GroupName="BellSound" Content="System alert sound" Click="OnBellSoundSystemAlertClick"
                 IsChecked="{Binding BellSound, Converter={x:Static vm:BellSoundConverter.Instance}, ConverterParameter=SystemAlert, Mode=OneWay}" />
    <!-- Shown only where the constructor disabled the radio above (BellSupport). -->
    <TextBlock x:Name="BellSoundNote" Text="The system alert sound is not available on Linux." Foreground="#A0A0A0" TextWrapping="Wrap" IsVisible="False" />
```

- [ ] **Step 7: The window code-behind**

Replace the constructors and add the handlers in `src/LizTerm.App/Views/PreferencesWindow.axaml.cs`:

```csharp
    /// <summary>Design-time only.</summary>
    public PreferencesWindow() : this(new SettingsViewModel()) { }

    public PreferencesWindow(SettingsViewModel settings) : this(settings, BellSupport.SystemAlertAvailableHere) { }

    /// <summary>The platform is an argument so a test can see the Linux shape of the window on any machine.</summary>
    internal PreferencesWindow(SettingsViewModel settings, bool systemAlertAvailable)
    {
        InitializeComponent();
        DataContext = settings;
        BellSoundSystemAlert.IsEnabled = systemAlertAvailable;
        BellSoundNote.IsVisible = !systemAlertAvailable;
    }
```

and, after `OnCrosshairBothClick`:

```csharp
    private void OnBellSoundNoneClick(object? sender, RoutedEventArgs e) => Settings.BellSound = BellSound.None;
    private void OnBellSoundSystemAlertClick(object? sender, RoutedEventArgs e) => Settings.BellSound = BellSound.SystemAlert;
```

Add `using LizTerm.App.Bell;` to the file's usings.

- [ ] **Step 8: Run the tests to verify they pass**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~BellSupportTests|FullyQualifiedName~BellSoundConverterTests|FullyQualifiedName~PreferencesWindowTests"`
Expected: all pass.

- [ ] **Step 9: Commit**

```bash
git add src/LizTerm.App/Bell/BellSupport.cs src/LizTerm.App/ViewModels/BellSoundConverter.cs src/LizTerm.App/Views/PreferencesWindow.axaml src/LizTerm.App/Views/PreferencesWindow.axaml.cs tests/LizTerm.App.Tests/Bell/BellSupportTests.cs tests/LizTerm.App.Tests/ViewModels/BellSoundConverterTests.cs tests/LizTerm.App.Tests/Views/PreferencesWindowTests.cs
git commit -m "Add the Bell group to Preferences

A flash checkbox and two sound radios, the Crosshair radios' one-way
shape; the system-alert radio is disabled with a note where the platform
has nothing to ring.

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

Connect to a host and provoke the bell (a rejected keystroke in a protected field does it on many hosts; the ibmlink recording rang on its help screen). Confirm, in order:

1. The screen flashes once per bell with the defaults; no sound.
2. Preferences > Bell: turn on "System alert sound"; the next bell plays the Mac's alert sound at the Mac's alert volume, and the flash still happens.
3. System Settings > Sound > turn "Play user interface sound effects" off; the next bell flashes and is silent.
4. Hold a key that the host rejects; the flash stays a readable pulse rather than a strobe.
5. Preferences > Bell: untick the flash; the bell is now sound only. Tick it back.

Report what was observed to Robert in chat, including anything that did not match. Fix before continuing if anything did not.

---

### Task 10: Documentation and the spec's As-built section

**Files:**
- Modify: `docs/user-guide.md:61-72` ("The session window") and `:199-206` ("Preferences"); `src/LizTerm.App/CLAUDE.md:42-48` ("Session view model") and `:101-115` ("Settings and Preferences"); `src/LizTerm.Backend.B3270/CLAUDE.md:35-38`; `tests/CLAUDE.md:41-48`; `docs/superpowers/specs/2026-09-11-lizterm-bell-design.md` (append)

- [ ] **Step 1: The user guide**

In `docs/user-guide.md`, under "The session window", after the crosshair paragraph (ends "it is also in Preferences."):

```markdown
When the host rings the terminal bell, the screen flashes briefly. A sound can be turned on in Preferences; by
default the bell is silent. A host that rings repeatedly is limited to two bells a second.
```

Under "Preferences", after the Blink bullet:

```markdown
- **Bell** — whether the screen flashes when the host rings the bell (on by default), and what sound plays: none, or
  the system alert sound, at the volume your system uses for alerts. The system alert sound is not available on
  Linux.
```

- [ ] **Step 2: `src/LizTerm.App/CLAUDE.md`**

In "Session view model", the first bullet ends with the sentence about `SettingsViewModel` being injected last. Add one sentence after it:

```markdown
  `IBellRinger` (`Bell/`; `SystemBellRinger` in the app, `FakeBellRinger` in tests) is injected after it, and is
  null when nothing should sound.
```

In "Settings and Preferences", add a bullet after the `TerminalScreen.BlinkEnabled` one:

```markdown
- **The bell.** `IEmulatorSession.BellRang` is marshalled like the other events, then `SessionViewModel.OnBell`
  runs: disposed check, one `BellThrottle` (`BellInterval`, 500 ms, one gate for both outputs, refused bells are
  dropped not queued), then `Settings.VisualBell` raises the view model's own `BellRang` (the window calls
  `TerminalScreen.Flash()`, a 120 ms `Palette.BellFlash` overlay on the control's one-shot timer) and
  `Settings.BellSound` other than `None` calls the ringer. "None means silence" is the view model's rule; the
  ringer only knows how to make sounds. `SystemBellRinger` is the App's only P/Invoke (`NSBeep`, `MessageBeep`)
  and does nothing on Linux; `BellSupport.SystemAlertAvailable` is the pure platform rule that disables the
  Preferences radio there. The bell's sound radios are one-way check marks plus Click handlers over
  `BellSoundConverter`, the Crosshair shape.
```

- [ ] **Step 3: `src/LizTerm.Backend.B3270/CLAUDE.md`**

In "Protocol", change

`connection` and `tls` update state; `popup` and `ui-error` surface as `HostMessage`.

to

`connection` and `tls` update state; `popup` and `ui-error` surface as `HostMessage`; `bell` raises `BellRang`, which carries nothing.

- [ ] **Step 4: `tests/CLAUDE.md`**

The `FakeEmulatorSession` bullet ends with "so a test can drive the transfer dialog's Running phase." Add one sentence after it:

```markdown
  Its `Raise*` methods (`RaiseScreen`, `RaiseStatus`, `RaiseConnection`, `RaiseFault`, `RaiseHostMessage`,
  `RaiseBell`) fire the corresponding events.
```

The "Other fakes" bullet ends with "`fetch:<host>:<port>`)." Change that closing to:

```markdown
  `fetch:<host>:<port>`); `FakeBellRinger` (`Rings`, the list of `BellSound` values asked for, and an optional
  `Exception`).
```

- [ ] **Step 5: The spec's As-built section**

Append to `docs/superpowers/specs/2026-09-11-lizterm-bell-design.md`:

```markdown
## 10. As built (2026-09-11)

Built as specified; the plan is `docs/superpowers/plans/2026-09-11-lizterm-bell.md`. Two details decided while
planning:

- **`BellThrottle` measures from the last admitted bell**, not the last attempt, so a host ringing every 100 ms
  still gets one bell through every 500 ms. A throttle that restarted on every refusal would silence it entirely.
- **`PreferencesWindow` gained an internal constructor taking the platform rule's answer**, the shape
  `SessionWindow(bool useNativeMenu)` already has, so the Linux form of the window is tested on macOS.
```

- [ ] **Step 6: Verify and commit**

Run: `LIZTERM_B3270_PATH=/opt/homebrew/bin/b3270 dotnet test LizTerm.slnx` and `dotnet build LizTerm.slnx --no-incremental 2>&1 | grep -c " warning "`
Expected: all pass; `0`.

```bash
git add docs/user-guide.md src/LizTerm.App/CLAUDE.md src/LizTerm.Backend.B3270/CLAUDE.md tests/CLAUDE.md docs/superpowers/specs/2026-09-11-lizterm-bell-design.md
git commit -m "Document the bell, and record what was built

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

- [ ] **Step 7: Hand back to Robert**

Do not push. Tell Robert in chat: the branch, the commit list, the manual-pass results from Task 9, and that pushing and opening the PR (which closes #47, and adds the WAV-option comment to #19) wait for his word.
