# LizTerm v0.3.1 fix and polish — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close the two bugs found by using v0.3.0, five polish items, the profile editor's two unconstrained fields, and the macOS engine gate's missing negative fixtures.

**Architecture:** No new subsystems. Three small pure additions to `LizTerm.Core` (a terminal-type spelling, a pin-merge rule, and the model and code-page catalogues) that App and the integration tests both consume; everything else is edits to existing view models, XAML and one CI job. The dependency rule is the constraint that shapes several of these: `LizTerm.App` names `LizTerm.Backend.B3270` in exactly one place, `SessionFactory.cs`, so anything App and the backend both need lives in Core.

**Tech Stack:** .NET 10, Avalonia 12.1.2, CommunityToolkit.Mvvm, xunit.v3 (VSTest mode), Avalonia headless test platform, GitHub Actions, bash.

**Spec:** `docs/superpowers/specs/2026-09-09-lizterm-v031-fix-and-polish-design.md`

## Global Constraints

- **Dependency rule.** `LizTerm.Core` depends only on the BCL and never mentions Avalonia or b3270 names. `LizTerm.App` names `LizTerm.Backend.B3270` in exactly one place: `src/LizTerm.App/SessionFactory.cs`. Do not add a second.
- **License header.** Every hand-written `.cs`, `.axaml` and `.sh` file under `src/`, `tests/`, `native/build/` and `tools/` carries three lines: `This file is part of LizTerm.`, `Copyright 2026 by CoffeeMuse`, `SPDX-License-Identifier: BSD-3-Clause`, in that file's comment syntax, within the first eight lines (after the shebang in a script, before the root element in an `.axaml`). `RepositoryHeadersTests` fails the suite otherwise.
- **Zero warnings.** `dotnet build LizTerm.slnx --no-incremental 2>&1 | grep -c " warning "` must print `0` before anything is called done. An incremental build hides warnings from projects it did not recompile.
- **Status strings are exact.** Assertions on user-visible status text are exact string matches; change `StatusFormatter` and its tests in the same commit.
- **No `Gesture` on any menu item outside Edit.** A `NativeMenuItem` gesture becomes an AppKit key equivalent dispatched before the key window's responder chain, which silently swallows the keystroke from `TerminalScreen`.
- **Both menus change together.** `SessionWindow.axaml` declares a `NativeMenu` and a classic `<Menu>`; `NativeMenuTests.The_native_menu_matches_the_classic_menu_item_for_item` compares them header for header.
- **Every native item needs a `Command` or a `Click` handler.** Avalonia's macOS exporter validates `(Command != null || HasClickHandlers) && IsEnabled`; an item with only a binding is greyed out on macOS and inert everywhere. `NativeMenuTests.Every_native_item_can_actually_be_activated` is the guard.
- **Test commands.** Full suite `dotnet test LizTerm.slnx`; one class `dotnet test tests/<project> --filter "FullyQualifiedName~<ClassName>"`.

---

## File Structure

**Created**

| File | Responsibility |
|---|---|
| `src/LizTerm.Core/Session/TerminalType.cs` | The one spelling of a profile's 3270 terminal type (`3279-2-E`). |
| `src/LizTerm.Core/Session/TerminalModel.cs` | The model catalogue: number, rows, columns. |
| `src/LizTerm.Core/Session/CodePage.cs` | The code-page catalogue: engine name plus a human label. |
| `src/LizTerm.Core/Profiles/PinMerge.cs` | The pure rule deciding which certificate pin a profile save keeps. |
| `tests/LizTerm.Core.Tests/Session/TerminalTypeTests.cs` | Terminal type. |
| `tests/LizTerm.Core.Tests/Session/CatalogueTests.cs` | Model and code-page catalogue shape. |
| `tests/LizTerm.Core.Tests/Profiles/PinMergeTests.cs` | Every branch of the merge rule. |
| `tests/LizTerm.Integration.Tests/EngineCatalogueTests.cs` | The catalogues against the live engine's `initialize` block. |

**Modified**

| File | Change |
|---|---|
| `src/LizTerm.Backend.B3270/Protocol/HostStringBuilder.cs` | `ModelArgument` delegates to Core. |
| `src/LizTerm.App/Status/StatusFormatter.cs` | `Model` renders the terminal type. |
| `src/LizTerm.App/Views/AboutWindow.axaml{,.cs}` | Copyright line; app icon. |
| `src/LizTerm.App/Views/SplashWindow.axaml` | App icon above the wordmark. |
| `src/LizTerm.App/Views/SessionWindow.axaml` | Transfer item rename; Dup and FieldMark; both menus. |
| `src/LizTerm.App/Views/{CertificateWindow,FileTransferWindow,ProfileEditorWindow}.axaml` | `Icon=`. |
| `src/LizTerm.App/ViewModels/SessionViewModel.cs` | `_hasSocket`, `_connectPending`, command `CanExecute`. |
| `src/LizTerm.App/ViewModels/ProfileEditorViewModel.cs` | `PinCleared`; `TerminalModels`; `CodePages`; seeding. |
| `src/LizTerm.App/ViewModels/ProfilePickerViewModel.cs` | Reload on activate/before edit; merge on save. |
| `src/LizTerm.App/Views/ProfilePickerWindow.axaml.cs` | `Activated` handler. |
| `src/LizTerm.App/Views/ProfileEditorWindow.axaml` | Two combo boxes. |
| `src/LizTerm.Core/Profiles/ProfileStore.cs` | Atomic `Save`. |
| `.github/workflows/engines.yml` | The macOS gate's negative fixtures. |

---

## Task 1: The terminal type moves to Core (#38)

**Files:**
- Create: `src/LizTerm.Core/Session/TerminalType.cs`
- Create: `tests/LizTerm.Core.Tests/Session/TerminalTypeTests.cs`
- Modify: `src/LizTerm.Backend.B3270/Protocol/HostStringBuilder.cs:24-25`
- Modify: `src/LizTerm.App/Status/StatusFormatter.cs:62-66`
- Test: `tests/LizTerm.App.Tests/Status/StatusFormatterTests.cs:59-64`, `tests/LizTerm.Backend.B3270.Tests/Protocol/MappersTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `LizTerm.Core.Session.TerminalType.For(SessionProfile profile) -> string`.

**Why Core:** the issue proposes calling `HostStringBuilder.ModelArgument` from `StatusFormatter`, which would make App name the backend in a second place. The terminal type is what the profile *is*; the backend happens to put it on a command line and the status bar happens to render it, and neither owns it.

- [ ] **Step 1: Write the failing test**

Create `tests/LizTerm.Core.Tests/Session/TerminalTypeTests.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.Core.Session;

namespace LizTerm.Core.Tests.Session;

public class TerminalTypeTests
{
    [Fact]
    public void Extended_profiles_carry_the_E_suffix()
    {
        Assert.Equal("3279-2-E", TerminalType.For(new SessionProfile { Model = 2, Extended = true }));
        Assert.Equal("3279-5-E", TerminalType.For(new SessionProfile { Model = 5, Extended = true }));
    }

    [Fact]
    public void Non_extended_profiles_do_not()
    {
        Assert.Equal("3279-4", TerminalType.For(new SessionProfile { Model = 4, Extended = false }));
    }
}
```

- [ ] **Step 2: Run it and watch it fail**

Run: `dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~TerminalTypeTests"`
Expected: FAIL — `TerminalType` does not exist (CS0103).

- [ ] **Step 3: Write the implementation**

Create `src/LizTerm.Core/Session/TerminalType.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

namespace LizTerm.Core.Session;

/// <summary>The one spelling of a profile's 3270 terminal type. The backend puts it on b3270's -model command
/// line and the status bar shows it to the user; both read it from here so the engine argument and the status
/// bar cannot drift apart, and so App never has to name the backend to render it.</summary>
public static class TerminalType
{
    /// <summary>For example <c>3279-2-E</c>. The 3279 family is colour; the -E suffix is the extended data
    /// stream. "Model 2" on its own is ambiguous in a way this is not.</summary>
    public static string For(SessionProfile profile) =>
        $"3279-{profile.Model}{(profile.Extended ? "-E" : "")}";
}
```

- [ ] **Step 4: Run it and watch it pass**

Run: `dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~TerminalTypeTests"`
Expected: PASS.

- [ ] **Step 5: Point the backend at it**

In `src/LizTerm.Backend.B3270/Protocol/HostStringBuilder.cs`, replace:

```csharp
    public static string ModelArgument(SessionProfile profile) =>
        $"3279-{profile.Model}{(profile.Extended ? "-E" : "")}";
```

with:

```csharp
    /// <summary>b3270's -model argument. The spelling lives in Core because the status bar shows the same
    /// string; this stays as the name the protocol code calls it by.</summary>
    public static string ModelArgument(SessionProfile profile) => TerminalType.For(profile);
```

- [ ] **Step 6: Point the status bar at it**

In `src/LizTerm.App/Status/StatusFormatter.cs`, replace the body of `Model`:

```csharp
    public static string Model(SessionProfile profile, string? luName)
    {
        var model = $"Model {profile.Model}{(profile.Extended ? "-E" : "")}";
        return luName is null ? model : $"{model}  LU {luName}";
    }
```

with:

```csharp
    /// <summary>The full terminal type, the way a 3270 user writes one. "Model 2" dropped the family, so it did
    /// not say 3278 or 3279 — the colour distinction — in the one place a user can read what terminal they are.</summary>
    public static string Model(SessionProfile profile, string? luName)
    {
        var model = TerminalType.For(profile);
        return luName is null ? model : $"{model}  LU {luName}";
    }
```

- [ ] **Step 7: Update the exact-string assertions**

In `tests/LizTerm.App.Tests/Status/StatusFormatterTests.cs`, replace lines 62-63:

```csharp
        Assert.Equal("Model 4-E", StatusFormatter.Model(profile, null));
        Assert.Equal("Model 4-E  LU IBM0TEQO", StatusFormatter.Model(profile, "IBM0TEQO"));
```

with:

```csharp
        Assert.Equal("3279-4-E", StatusFormatter.Model(profile, null));
        Assert.Equal("3279-4-E  LU IBM0TEQO", StatusFormatter.Model(profile, "IBM0TEQO"));
```

- [ ] **Step 8: Run the whole suite**

Run: `dotnet test LizTerm.slnx`
Expected: PASS. `MappersTests` covers `ModelArgument` and must still pass unchanged — the delegation produces the identical string.

- [ ] **Step 9: Commit**

```bash
git add src/LizTerm.Core/Session/TerminalType.cs tests/LizTerm.Core.Tests/Session/TerminalTypeTests.cs \
        src/LizTerm.Backend.B3270/Protocol/HostStringBuilder.cs src/LizTerm.App/Status/StatusFormatter.cs \
        tests/LizTerm.App.Tests/Status/StatusFormatterTests.cs
git commit -m "Show the terminal type in the status bar, spelled once in Core

Closes #38. The status bar said "Model 4-E", which drops the 3278/3279
family -- the colour distinction -- in the one place a user can read what
terminal they are. It now shows 3279-4-E, the same string b3270 gets on
its -model command line.

The issue proposed calling HostStringBuilder.ModelArgument from
StatusFormatter, but that lives in the backend and App names the backend
in exactly one place. The spelling moves to Core instead, and both read
it from there.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Task 2: About stops repeating its own licence (#42)

**Files:**
- Modify: `src/LizTerm.App/Views/AboutWindow.axaml.cs:24`
- Test: `tests/LizTerm.App.Tests/Views/AboutWindowTests.cs:28-35`

**Interfaces:**
- Consumes: `AppLicense.Copyright` (already exists, `src/LizTerm.App/AppLicense.cs:19`).
- Produces: nothing.

**Note this reverses a documented decision.** `AboutWindowTests.Names_its_own_copyright_and_license_without_scrolling` carries a doc comment arguing the credit line should carry "who owns the work and under what terms". That was written before the Licenses box held LizTerm's own terms. Now the box's first line is `LizTerm, BSD-3-Clause` three lines below, and the embedded LICENSE repeats the copyright a few lines after that. Rewrite the comment as well as the assertion — do not leave a comment arguing for behaviour the test no longer asserts. This is the same deliberate-flip precedent as the licence-header work.

- [ ] **Step 1: Change the test to the new expectation**

In `tests/LizTerm.App.Tests/Views/AboutWindowTests.cs`, replace lines 28-35 entirely:

```csharp
    /// <summary>The credit line is the one piece of this that is readable without scrolling, so it carries the
    /// two facts a user is most likely to want: who owns the work and under what terms.</summary>
    [AvaloniaFact]
    public void Names_its_own_copyright_and_license_without_scrolling()
    {
        var window = Shown();
        Assert.Equal("Copyright 2026 by CoffeeMuse - BSD-3-Clause", window.FindControl<TextBlock>("CopyrightText")!.Text);
    }
```

with:

```csharp
    /// <summary>The credit line names the copyright and stops there. It used to carry the licence too, from when
    /// the box below held third-party notices only and LizTerm's own terms appeared nowhere else; the box now
    /// opens with "LizTerm, BSD-3-Clause" three lines under this one and the embedded LICENSE repeats the
    /// copyright a few lines after that, so the suffix was restating what is already on screen twice.</summary>
    [AvaloniaFact]
    public void Names_its_own_copyright_without_repeating_the_license_below_it()
    {
        var window = Shown();
        var credit = window.FindControl<TextBlock>("CopyrightText")!.Text;
        Assert.Equal("Copyright 2026 by CoffeeMuse", credit);
        Assert.DoesNotContain("BSD-3-Clause", credit);
    }
```

- [ ] **Step 2: Run it and watch it fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~AboutWindowTests"`
Expected: FAIL — actual is `Copyright 2026 by CoffeeMuse - BSD-3-Clause`.

- [ ] **Step 3: Make the change**

In `src/LizTerm.App/Views/AboutWindow.axaml.cs`, line 24, replace:

```csharp
        CopyrightText.Text = AppLicense.Notice;
```

with:

```csharp
        CopyrightText.Text = AppLicense.Copyright;
```

Leave `AppLicense.Notice` in place: the splash still uses it, and it is deliberately ASCII because the splash renders it in the 3270 font.

- [ ] **Step 4: Run it and watch it pass**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~AboutWindowTests"`
Expected: PASS, all four tests in the class.

- [ ] **Step 5: Commit**

```bash
git add src/LizTerm.App/Views/AboutWindow.axaml.cs tests/LizTerm.App.Tests/Views/AboutWindowTests.cs
git commit -m "Drop the licence suffix from About's copyright line

Closes #42. The line read "Copyright 2026 by CoffeeMuse - BSD-3-Clause"
three lines above a Licenses box whose first line is "LizTerm,
BSD-3-Clause" and whose embedded LICENSE repeats the copyright.

The test's own comment argued for the old behaviour, from when the box
held third-party notices only; it is rewritten rather than left standing
over a changed assertion.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Task 3: Name the transfer mechanism (#41)

**Files:**
- Modify: `src/LizTerm.App/Views/SessionWindow.axaml:27` (native), `:99` (classic)
- Test: `tests/LizTerm.App.Tests/Views/NativeMenuTests.cs:86`

**Interfaces:**
- Consumes: nothing.
- Produces: the menu header string `IND$FILE _Transfer...`, which `NativeMenuTests` looks items up by.

Everything behind this item is IND$FILE typed through the 3270 session. #17 adds z/OSMF REST as a second transport and FTP is a possible third, so it is named before there are two. The jargon is right for this audience.

Keep the `_T` mnemonic where it is. `$` needs no XAML escaping.

- [ ] **Step 1: Update the test's lookup**

In `tests/LizTerm.App.Tests/Views/NativeMenuTests.cs`, line 86, replace:

```csharp
        var item = Item(window, "_File", "File _Transfer...");
```

with:

```csharp
        var item = Item(window, "_File", "IND$FILE _Transfer...");
```

- [ ] **Step 2: Run it and watch it fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~NativeMenuTests"`
Expected: FAIL — the lookup finds no such item.

- [ ] **Step 3: Rename in both menus**

In `src/LizTerm.App/Views/SessionWindow.axaml`, line 27:

```xml
            <NativeMenuItem Header="IND$FILE _Transfer..." Click="OnFileTransferClickNative" IsEnabled="{Binding IsConnected}" />
```

and line 99:

```xml
        <MenuItem x:Name="FileTransferMenuItem" Header="IND$FILE _Transfer..." Click="OnFileTransferClick" IsEnabled="{Binding IsConnected}" />
```

- [ ] **Step 4: Run the App tests**

Run: `dotnet test tests/LizTerm.App.Tests`
Expected: PASS, including `The_native_menu_matches_the_classic_menu_item_for_item` — both headers changed together.

- [ ] **Step 5: Commit**

```bash
git add src/LizTerm.App/Views/SessionWindow.axaml tests/LizTerm.App.Tests/Views/NativeMenuTests.cs
git commit -m "Rename File Transfer to IND\$FILE Transfer

Closes #41. The item named the category, not the mechanism. #17 adds
z/OSMF REST as a second transport and FTP is a possible third, so it says
which one it is before there are two. Both menu declarations change
together, as the parity guard requires.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Task 4: Dup and FieldMark reach the Keys menu (#16)

**Files:**
- Modify: `src/LizTerm.App/Views/SessionWindow.axaml` (native Keys menu at `:48`, classic at `:109`)
- Test: `tests/LizTerm.App.Tests/Views/NativeMenuTests.cs`

**Interfaces:**
- Consumes: `TerminalKey.Dup`, `TerminalKey.FieldMark` (already in `src/LizTerm.Core/Session/TerminalKey.cs`), `SessionViewModel.SendKeyCommand`.
- Produces: nothing.

`ActionMap.cs:31-32` already maps both to b3270's `Dup` and `FieldMark` actions; nothing in App referenced either. DUP writes X'1C' and tabs to the next field, FIELD MARK writes X'1E'; both are real data-entry keys on the CICS and TSO panels this project's users run.

**Menu half only.** Keymap chords are out of scope — choosing them is the hardening spec's §6.2 exercise (Vista's defaults cross-checked against wc3270), and whatever is chosen has to clear the platform copy/paste/select-all gestures that `TerminalScreen` checks before the keymap.

Place them after SysReq and before the separator that precedes PA1, so the group stays "keys that are not PF or PA".

- [ ] **Step 1: Write the failing test**

Append to `tests/LizTerm.App.Tests/Views/NativeMenuTests.cs`, inside the class:

```csharp
    /// <summary>#16: both are mapped in ActionMap and were reachable only from C#. The menu is the whole fix —
    /// keymap chords are a separate decision, since any chord has to clear the copy/paste/select-all gestures
    /// TerminalScreen checks before the keymap.</summary>
    [AvaloniaFact]
    public void Keys_menu_offers_Dup_and_FieldMark()
    {
        var (window, _, _, _) = Show();
        foreach (var header in new[] { "Dup", "Field Mark" })
        {
            Assert.NotNull(Item(window, "_Keys", header).Command);
        }
    }
```

`Show()` (`:56`) returns `(SessionWindow, SessionViewModel, FakeEmulatorSession, FakeTextClipboard)` and `Item(window, top, child)` (`:69`) is the non-null `MenuLookup` wrapper — the same pair every test in this file uses.

- [ ] **Step 2: Run it and watch it fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~NativeMenuTests"`
Expected: FAIL — no item headed `Dup`.

- [ ] **Step 3: Add both items to the native menu**

In `src/LizTerm.App/Views/SessionWindow.axaml`, after the SysReq line (`:52`) and before the `<NativeMenuItemSeparator />`:

```xml
            <NativeMenuItem Header="Dup" Command="{Binding SendKeyCommand}" CommandParameter="{x:Static core:TerminalKey.Dup}" />
            <NativeMenuItem Header="Field Mark" Command="{Binding SendKeyCommand}" CommandParameter="{x:Static core:TerminalKey.FieldMark}" />
```

- [ ] **Step 4: Add both items to the classic menu**

After the classic SysReq line (`:113`) and before its `<Separator />`:

```xml
        <MenuItem Header="Dup" Command="{Binding SendKeyCommand}" CommandParameter="{x:Static core:TerminalKey.Dup}" />
        <MenuItem Header="Field Mark" Command="{Binding SendKeyCommand}" CommandParameter="{x:Static core:TerminalKey.FieldMark}" />
```

- [ ] **Step 5: Run the App tests**

Run: `dotnet test tests/LizTerm.App.Tests`
Expected: PASS, including the parity guard and `Every_native_item_can_actually_be_activated` — both new items carry a `Command`.

- [ ] **Step 6: Commit**

```bash
git add src/LizTerm.App/Views/SessionWindow.axaml tests/LizTerm.App.Tests/Views/NativeMenuTests.cs
git commit -m "Put Dup and Field Mark in the Keys menu

Closes #16 (menu half). Both are in TerminalKey and mapped in ActionMap
to b3270's own actions, and nothing in App referenced either -- they were
reachable only by writing C# against IEmulatorSession. DUP writes X'1C'
and Field Mark X'1E'; both are ordinary data-entry keys on the CICS and
TSO panels this project's users run.

Keymap chords stay open: any chord has to clear the platform
copy/paste/select-all gestures TerminalScreen checks before the keymap,
which is the constraint that kept Ctrl+Insert from being PA1.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Task 5: The app icon reaches the splash, About, and four dialogs (#40)

**Files:**
- Modify: `src/LizTerm.App/Views/SplashWindow.axaml:14-23`
- Modify: `src/LizTerm.App/Views/AboutWindow.axaml:12-17`
- Modify: `src/LizTerm.App/Views/AboutWindow.axaml` (add `Icon=`), `CertificateWindow.axaml`, `FileTransferWindow.axaml`, `ProfileEditorWindow.axaml`
- Test: `tests/LizTerm.App.Tests/Views/SplashWindowTests.cs`, `tests/LizTerm.App.Tests/Views/AboutWindowTests.cs`

**Interfaces:**
- Consumes: `avares://LizTerm.App/Assets/Icons/lizterm-256.png` (already an `AvaloniaResource`; the csproj `Remove`s only `lizterm.icns` and `lizterm.ico`).
- Produces: nothing.

`lizterm-256.png` is 256x256 and is what every window already setting `Icon=` points at. `lizterm.png` is the 1254x1254 master — the csproj comment says not to resize it; do not use it here.

Keep `x:Name="SplashMark"` on the `ContentControl`: `SplashWindowTests` finds it by name at `:27` and `:60`.

- [ ] **Step 1: Write the failing tests**

Append to `tests/LizTerm.App.Tests/Views/SplashWindowTests.cs`, inside the class:

```csharp
    /// <summary>#40: the two windows whose whole job is identity showed a text wordmark and no image, while the
    /// mark itself already reached the Dock, the taskbar and the installers.</summary>
    [AvaloniaFact]
    public void Splash_shows_the_app_icon_above_the_wordmark()
    {
        var window = new SplashWindow("0.3.0", new SplashTiming(TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(2)));
        window.Show();
        var mark = window.FindControl<ContentControl>("SplashMark")!;
        var image = Assert.IsType<Image>(((StackPanel)mark.Content!).Children[0]);
        Assert.NotNull(image.Source);
        Assert.Equal(Stretch.Uniform, image.Stretch);
        window.Close();
    }
```

The long `SplashTiming` is what the neighbouring test at `:21` uses, so the splash does not close itself mid-assertion. Add `using Avalonia.Media;` for `Stretch`.

Append to `tests/LizTerm.App.Tests/Views/AboutWindowTests.cs`, inside the class:

```csharp
    [AvaloniaFact]
    public void About_shows_the_app_icon_beside_the_name()
    {
        var image = Shown().FindControl<Image>("AboutMark");
        Assert.NotNull(image);
        Assert.NotNull(image!.Source);
    }
```

Add `using Avalonia.Media;` if the `Stretch` type is referenced.

- [ ] **Step 2: Run them and watch them fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~SplashWindowTests|FullyQualifiedName~AboutWindowTests"`
Expected: FAIL — the splash's first child is a `TextBlock`, and `AboutMark` does not exist.

- [ ] **Step 3: Put the icon on the splash**

In `src/LizTerm.App/Views/SplashWindow.axaml`, replace the comment at `:14-16` and the `StackPanel` inside `SplashMark`:

```xml
  <!-- The mark: the app icon above the wordmark. To replace the whole thing with artwork later, put the file at
       Assets/Splash/liz.png and swap the StackPanel below for <Image Source="avares://LizTerm.App/Assets/Splash/liz.png"
       Stretch="Uniform" />, keeping the 480x300 window and the SplashMark name that SplashWindowTests finds. -->
  <Grid RowDefinitions="*,Auto" Margin="24">
    <ContentControl x:Name="SplashMark" HorizontalAlignment="Center" VerticalAlignment="Center">
      <StackPanel>
        <Image Source="avares://LizTerm.App/Assets/Icons/lizterm-256.png" Width="96" Height="96"
               Stretch="Uniform" HorizontalAlignment="Center" Margin="0,0,0,8" />
        <TextBlock Text="LizTerm" FontFamily="{x:Static controls:TerminalScreen.TerminalFont}" FontSize="72" Foreground="#50FF50" HorizontalAlignment="Center" />
        <TextBlock Text="TN3270 terminal" FontFamily="{x:Static controls:TerminalScreen.TerminalFont}" FontSize="20" Foreground="#50FF50" HorizontalAlignment="Center" />
      </StackPanel>
    </ContentControl>
```

- [ ] **Step 4: Put the icon in About**

In `src/LizTerm.App/Views/AboutWindow.axaml`, add the window icon to the `<Window>` element (after the `x:Class` line):

```xml
        Icon="avares://LizTerm.App/Assets/Icons/lizterm-256.png"
```

and replace the wordmark `TextBlock` at `:13` with an icon-plus-name row:

```xml
    <StackPanel DockPanel.Dock="Top" Orientation="Horizontal" Spacing="12">
      <Image x:Name="AboutMark" Source="avares://LizTerm.App/Assets/Icons/lizterm-256.png"
             Width="56" Height="56" Stretch="Uniform" VerticalAlignment="Center" />
      <TextBlock Text="LizTerm" FontFamily="{x:Static controls:TerminalScreen.TerminalFont}" FontSize="40" Foreground="#50FF50" VerticalAlignment="Center" />
    </StackPanel>
```

- [ ] **Step 5: Give the other three dialogs an icon**

Add the same attribute to the `<Window>` element of `src/LizTerm.App/Views/CertificateWindow.axaml`, `FileTransferWindow.axaml` and `ProfileEditorWindow.axaml`:

```xml
        Icon="avares://LizTerm.App/Assets/Icons/lizterm-256.png"
```

macOS ignores `Window.Icon` entirely — the Dock icon comes from the bundle — which is why this gap was invisible on the platform it was noticed on. On Windows and Linux these four dialogs showed Avalonia's default icon in the title bar and taskbar.

- [ ] **Step 6: Run the App tests**

Run: `dotnet test tests/LizTerm.App.Tests`
Expected: PASS. Watch `SplashWindowTests` at `:60`, which reads `SplashMark` — confirm it still passes; if it asserts on the `StackPanel`'s children by index, adjust that test for the inserted `Image` and say so in the commit message.

- [ ] **Step 7: Commit**

```bash
git add src/LizTerm.App/Views/SplashWindow.axaml src/LizTerm.App/Views/AboutWindow.axaml \
        src/LizTerm.App/Views/CertificateWindow.axaml src/LizTerm.App/Views/FileTransferWindow.axaml \
        src/LizTerm.App/Views/ProfileEditorWindow.axaml \
        tests/LizTerm.App.Tests/Views/SplashWindowTests.cs tests/LizTerm.App.Tests/Views/AboutWindowTests.cs
git commit -m "Show the app icon on the splash, in About, and in four dialogs

Closes #40. The mark reaches the Dock, the taskbar and the installers,
and the two windows whose whole job is identity still showed a text
wordmark and no image. lizterm-256.png is already an AvaloniaResource, so
no new file is needed.

AboutWindow, CertificateWindow, FileTransferWindow and
ProfileEditorWindow also set no Icon at all. macOS ignores Window.Icon,
which is why that was invisible here; on Windows and Linux those four
showed Avalonia's default icon.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Task 6: Connect greys out while connected (#39)

**Files:**
- Modify: `src/LizTerm.App/ViewModels/SessionViewModel.cs:62-64` (fields), `:232-238` (`ApplyConnection`), `:241` (`ConnectCommand`), `:256`/`:284` (`_connectCts` set and clear), `:401` (`DisconnectCommand`)
- Test: `tests/LizTerm.App.Tests/ViewModels/SessionViewModelConnectTests.cs`

**Interfaces:**
- Consumes: `ConnectionStateExtensions.HasSocket()` (`src/LizTerm.Core/Session/ConnectionState.cs`).
- Produces: `SessionViewModel.HasSocket` (bool), `SessionViewModel.ConnectPending` (bool), `CanConnect`, `CanDisconnect`.

**The predicate is `HasSocket`, not `IsConnected`.** `HasSocket()` is false for `Disconnected`, `Reconnecting`, `Resolving` and `TcpPending` and true for everything past the TCP connect — the engine's own rule for refusing `Set verifyHostCert`. `IsConnected` is the `Connected*` group only and leaves a real window open: a connect whose cancel-or-timeout `Disconnect` wait expired returns with the engine still holding a socket, the command re-enabled, and `IsConnected` false.

**Add `_hasSocket`; do not replace `_isConnected`.** `IsConnected` is public contract — `IsEnabled` on the transfer and paste items, and `PasteCommand`'s `CanExecute`. An `[ObservableProperty]` named `ConnectionState` would also collide with the enum type of that name.

**Disconnect is not the inverse.** Its doc comment says it also cancels a *pending* connect, so it stays enabled whenever there is a socket **or** an attempt in flight. `_connectCts` is a plain field and raises nothing when assigned, so the pending half needs its own observable. Do not use `ConnectCommand.IsRunning`: it stays true through the certificate prompt and profile save that deliberately run after the catch clauses.

- [ ] **Step 1: Write the failing tests**

Append to `tests/LizTerm.App.Tests/ViewModels/SessionViewModelConnectTests.cs`, inside the class:

```csharp
    /// <summary>#39. Connect was a bare RelayCommand, so it re-enabled the moment a connect finished and clicking
    /// it put "Unexpected error: verifyHostCert cannot change while connected" in the banner. The boundary is
    /// HasSocket, not IsConnected: b3270 refuses the Set whenever it has a host session, which begins before the
    /// 3270 session does.</summary>
    [Fact]
    public async Task Connect_is_disabled_once_the_engine_holds_a_socket()
    {
        var session = new FakeEmulatorSession();
        var vm = new SessionViewModel(session, a => a(), new FakeTextClipboard());

        Assert.True(vm.ConnectCommand.CanExecute(null));

        session.RaiseConnection(ConnectionState.TelnetPending);
        Assert.False(vm.ConnectCommand.CanExecute(null));
        Assert.False(vm.IsConnected);

        session.RaiseConnection(ConnectionState.Connected3270);
        Assert.False(vm.ConnectCommand.CanExecute(null));

        session.RaiseConnection(ConnectionState.Disconnected);
        Assert.True(vm.ConnectCommand.CanExecute(null));

        await vm.DisposeAsync();
    }

    /// <summary>TcpPending is deliberately on the enabled side of HasSocket, and Disconnect must still be live
    /// there, because that is exactly when a user wants to cancel a connect that is going nowhere.</summary>
    [Fact]
    public async Task Disconnect_stays_live_for_a_pending_connect_and_greys_when_idle()
    {
        var session = new FakeEmulatorSession();
        var vm = new SessionViewModel(session, a => a(), new FakeTextClipboard());

        Assert.False(vm.DisconnectCommand.CanExecute(null));

        session.RaiseConnection(ConnectionState.Connected3270);
        Assert.True(vm.DisconnectCommand.CanExecute(null));

        session.RaiseConnection(ConnectionState.Disconnected);
        Assert.False(vm.DisconnectCommand.CanExecute(null));

        await vm.DisposeAsync();
    }
```

`FakeEmulatorSession.RaiseConnection(ConnectionState, TlsInfo? = null)` (`tests/LizTerm.App.Tests/Fakes/FakeEmulatorSession.cs:114`) is the existing raiser; it sets `ConnectionState` and fires the event. Do not add another.

- [ ] **Step 2: Run them and watch them fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~SessionViewModelConnectTests"`
Expected: FAIL — `CanExecute` returns true throughout, because neither command has a `CanExecute`.

- [ ] **Step 3: Add the two observable fields**

In `src/LizTerm.App/ViewModels/SessionViewModel.cs`, after the `_isConnected` block at `:62-64`:

```csharp
    /// <summary>b3270 has a socket to the host. Connect's boundary, and not IsConnected: b3270 refuses
    /// `Set verifyHostCert` whenever it has a host session, which begins before the 3270 session comes up.
    /// A second bool rather than the raw state, because IsConnected is bound in XAML and an [ObservableProperty]
    /// named ConnectionState would collide with the enum type.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    [NotifyCanExecuteChangedFor(nameof(DisconnectCommand))]
    private bool _hasSocket;

    /// <summary>A connect attempt is in flight. Kept beside _connectCts, which is a plain field and raises
    /// nothing when assigned. Not ConnectCommand.IsRunning: that stays true through the certificate prompt and
    /// the profile save, which deliberately run after the connect's catch clauses.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DisconnectCommand))]
    private bool _connectPending;
```

- [ ] **Step 4: Set them**

In `ApplyConnection` (`:232`), after `IsConnected = state.IsConnected();`:

```csharp
        HasSocket = state.HasSocket();
```

In `ConnectWithAsync`, at the `_connectCts = cts;` assignment (`:256`):

```csharp
            _connectCts = cts;
            ConnectPending = true;
```

and in that method's `finally` (`:284`), beside the existing clear:

```csharp
            finally
            {
                if (ReferenceEquals(_connectCts, cts)) _connectCts = null;
                ConnectPending = false;
            }
```

- [ ] **Step 5: Gate the two commands**

Replace `:241-242`:

```csharp
    [RelayCommand]
    private Task ConnectAsync() => ConnectWithAsync(new ConnectOptions(Pin: _pinOverride));
```

with:

```csharp
    /// <summary>Disabled once the engine holds a socket. x3270's own File menu disables it too; turning it into
    /// a reconnect is a larger behaviour that overlaps #28 and is not smuggled in here.</summary>
    public bool CanConnect => !HasSocket;

    [RelayCommand(CanExecute = nameof(CanConnect))]
    private Task ConnectAsync() => ConnectWithAsync(new ConnectOptions(Pin: _pinOverride));
```

and at `:400-401`, add the predicate above `DisconnectCommand` and gate it:

```csharp
    /// <summary>Not the inverse of CanConnect: this also cancels a pending connect, and HasSocket is false
    /// through Resolving and TcpPending, which is exactly when a user wants to give up on one.</summary>
    public bool CanDisconnect => HasSocket || ConnectPending;

    /// <summary>While a connect is pending this cancels it (the backend sends the Disconnect); otherwise it disconnects.</summary>
    [RelayCommand(CanExecute = nameof(CanDisconnect))]
    private Task DisconnectAsync()
```

Add `[NotifyPropertyChangedFor(nameof(CanConnect))]` to `_hasSocket` and `[NotifyPropertyChangedFor(nameof(CanDisconnect))]` to both `_hasSocket` and `_connectPending` if the generated `CanExecute` does not refresh without it — the `[NotifyCanExecuteChangedFor]` attributes added in Step 3 are what actually drive the commands, so add these only if a test fails without them.

- [ ] **Step 6: Run the tests**

Run: `dotnet test tests/LizTerm.App.Tests`
Expected: PASS. Both Connect items in `SessionWindow.axaml` (`:24` native, `:96` classic) bind `Command`, so both grey out from this one change, and `Every_native_item_can_actually_be_activated` still passes because the items still carry a `Command`.

- [ ] **Step 7: Commit**

```bash
git add src/LizTerm.App/ViewModels/SessionViewModel.cs tests/LizTerm.App.Tests/ViewModels/SessionViewModelConnectTests.cs
git commit -m "Grey out Connect while the engine holds a socket

Closes #39. ConnectCommand was a bare RelayCommand, so it came back the
moment a connect finished; clicking it sent Set verifyHostCert to a
connected engine, which refuses it, and the resulting
EmulatorActionException fell through to the generic catch and its
"Unexpected error: " prefix -- a prefix reserved for what we did not
anticipate.

The boundary is HasSocket rather than IsConnected, matching the engine's
own rule. DisconnectCommand is gated too, but not on the inverse: it also
cancels a pending connect, and HasSocket is false through Resolving and
TcpPending.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Task 7: The pin merge rule (#50, part 1)

**Files:**
- Create: `src/LizTerm.Core/Profiles/PinMerge.cs`
- Create: `tests/LizTerm.Core.Tests/Profiles/PinMergeTests.cs`

**Interfaces:**
- Consumes: `SessionProfile`, `CertificatePin` (`LizTerm.Core.Session`).
- Produces: `LizTerm.Core.Profiles.PinMerge.Resolve(SessionProfile edited, SessionProfile? onDisk, bool pinCleared) -> CertificatePin?`.

Three conditions interact and none of them belongs inline in a command handler:

1. The editor's copy of the pin may be **stale** — that is the bug. A null pin there can mean "there is no pin" or "there was one and this copy predates it".
2. **Forget** must still work. It is expressed today by nulling the editor's pin, which is the same null as (1).
3. The editor **deliberately drops** a pin when Host or Port is edited away from the values it was taken from (`_pinnedFor` plus `RefreshPin`). A blind carry-forward would resurrect a pin the user invalidated by repointing the profile.

- [ ] **Step 1: Write the failing tests**

Create `tests/LizTerm.Core.Tests/Profiles/PinMergeTests.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.Core.Profiles;
using LizTerm.Core.Session;

namespace LizTerm.Core.Tests.Profiles;

public class PinMergeTests
{
    private static readonly CertificatePin Fresh = new("AA:BB", "CN=mvs", "pem-fresh");
    private static readonly CertificatePin Stale = new("CC:DD", "CN=mvs", "pem-stale");

    private static SessionProfile Profile(string host = "mvs", int port = 3270, CertificatePin? pin = null) =>
        new() { Name = "MVS", Host = host, Port = port, PinnedCertificate = pin };

    /// <summary>The reported bug: the picker's copy predated a pin a session window wrote, so the editor showed
    /// none and Save wrote the whole record back over it.</summary>
    [Fact]
    public void A_pin_written_since_the_editor_opened_is_carried_forward()
    {
        var result = PinMerge.Resolve(Profile(pin: null), Profile(pin: Fresh), pinCleared: false);
        Assert.Equal(Fresh, result);
    }

    [Fact]
    public void Forget_clears_the_pin_even_when_disk_still_has_one()
    {
        var result = PinMerge.Resolve(Profile(pin: null), Profile(pin: Fresh), pinCleared: true);
        Assert.Null(result);
    }

    /// <summary>A pin belongs to the host and port it was taken from. Repointing the profile invalidates it, and
    /// the merge must not undo the editor's deliberate drop.</summary>
    [Fact]
    public void Repointing_the_host_drops_the_pin()
    {
        var result = PinMerge.Resolve(Profile(host: "other", pin: null), Profile(host: "mvs", pin: Fresh), pinCleared: false);
        Assert.Null(result);
    }

    [Fact]
    public void Repointing_the_port_drops_the_pin()
    {
        var result = PinMerge.Resolve(Profile(port: 992, pin: null), Profile(port: 3270, pin: Fresh), pinCleared: false);
        Assert.Null(result);
    }

    /// <summary>The editor never invents a pin, so a non-null one there is the copy it loaded. Disk wins when it
    /// has a newer one -- a session that re-pinned after a certificate change.</summary>
    [Fact]
    public void The_disk_pin_wins_over_the_editors_older_copy()
    {
        var result = PinMerge.Resolve(Profile(pin: Stale), Profile(pin: Fresh), pinCleared: false);
        Assert.Equal(Fresh, result);
    }

    /// <summary>A rename deletes the old file after this runs, so a missing file is also the normal path for a
    /// profile being created. Neither may invent a pin.</summary>
    [Fact]
    public void No_file_on_disk_keeps_whatever_the_editor_held()
    {
        Assert.Equal(Stale, PinMerge.Resolve(Profile(pin: Stale), onDisk: null, pinCleared: false));
        Assert.Null(PinMerge.Resolve(Profile(pin: null), onDisk: null, pinCleared: false));
        Assert.Null(PinMerge.Resolve(Profile(pin: Stale), onDisk: null, pinCleared: true));
    }

    [Fact]
    public void No_pin_anywhere_stays_no_pin()
    {
        Assert.Null(PinMerge.Resolve(Profile(pin: null), Profile(pin: null), pinCleared: false));
    }
}
```

- [ ] **Step 2: Run them and watch them fail**

Run: `dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~PinMergeTests"`
Expected: FAIL — `PinMerge` does not exist (CS0103).

- [ ] **Step 3: Write the implementation**

Create `src/LizTerm.Core/Profiles/PinMerge.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.Core.Session;

namespace LizTerm.Core.Profiles;

/// <summary>Which certificate pin a profile save keeps. The profile editor is handed a copy of the profile and
/// may hold it open across a pin written from a session window, so its idea of the pin can be older than the
/// file's; but it also drops a pin on purpose when the host or port is repointed, and Forget clears one
/// outright. Those three are all expressed as "the editor has no pin", so they are separated here rather than
/// guessed at by the caller.</summary>
public static class PinMerge
{
    /// <param name="edited">What the editor produced. It never invents a pin, so a non-null one here is the copy
    /// it loaded — possibly older than the file's.</param>
    /// <param name="onDisk">The profile as it is on disk now, read under its *original* name (a rename deletes
    /// the old file only after the save), or null when there is no file: a new profile, or a renamed one.</param>
    /// <param name="pinCleared">The user pressed Forget. Distinguishes a deliberate clear from an editor copy
    /// that simply predates the pin.</param>
    public static CertificatePin? Resolve(SessionProfile edited, SessionProfile? onDisk, bool pinCleared)
    {
        if (pinCleared) return null;
        if (onDisk?.PinnedCertificate is not { } stored) return edited.PinnedCertificate;

        // A pin belongs to the host and port it was taken from (see SessionProfile). Repointing either
        // invalidates it, and that is the editor's own rule -- carrying the stored pin forward regardless would
        // resurrect a pin the user dropped by pointing the profile somewhere else.
        var samePlace = string.Equals(edited.Host, onDisk.Host, StringComparison.OrdinalIgnoreCase)
                        && edited.Port == onDisk.Port;
        return samePlace ? stored : null;
    }
}
```

- [ ] **Step 4: Run them and watch them pass**

Run: `dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~PinMergeTests"`
Expected: PASS, all seven.

- [ ] **Step 5: Commit**

```bash
git add src/LizTerm.Core/Profiles/PinMerge.cs tests/LizTerm.Core.Tests/Profiles/PinMergeTests.cs
git commit -m "Add the pin merge rule, so a profile save cannot drop a pin

Part of #50. Three conditions all present as "the editor has no pin": a
stale copy that predates a pin a session wrote, the Forget button, and
the editor's own deliberate drop when the host or port is repointed.
Separating them is more than belongs in a command handler, so it is one
pure function with a table of tests.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Task 8: Profile writes become atomic (#50, part 2)

**Files:**
- Modify: `src/LizTerm.Core/Profiles/ProfileStore.cs:57-63`
- Test: `tests/LizTerm.Core.Tests/Profiles/ProfileStoreTests.cs`

**Interfaces:**
- Consumes: nothing new.
- Produces: no signature change — `Save` keeps its shape.

`Save` is a plain `File.WriteAllText`, and `Read` swallows `JsonException`/`IOException`/`UnauthorizedAccessException` and returns null, which `LoadAll` skips silently. A write interrupted partway therefore leaves a truncated file that the picker renders as a profile that simply is not there — indistinguishable from one the user deleted.

- [ ] **Step 1: Write the failing test**

Append to `tests/LizTerm.Core.Tests/Profiles/ProfileStoreTests.cs`, inside the class:

```csharp
    /// <summary>#50. Read swallows a JsonException and LoadAll skips the file, so a half-written profile is
    /// indistinguishable from a deleted one. A temp-file-and-rename never leaves a reader a partial file.</summary>
    [Fact]
    public void Save_never_leaves_a_partial_file_behind()
    {
        var store = new ProfileStore(_dir);
        store.Save(new SessionProfile { Name = "MVS", Host = "first.host" });
        store.Save(new SessionProfile { Name = "MVS", Host = "second.host" });

        var loaded = Assert.Single(store.LoadAll());
        Assert.Equal("second.host", loaded.Host);

        // The rename is within one directory, so nothing else may be left lying around for LoadAll to trip on.
        Assert.Equal(["MVS.json"], Directory.GetFiles(_dir).Select(Path.GetFileName).Order());
    }
```

- [ ] **Step 2: Run it and watch it behave**

Run: `dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~ProfileStoreTests"`
Expected: PASS today (the current `WriteAllText` also leaves one file). This test is the regression guard for the rewrite in Step 3 — it fails if the temp file is written into the same directory and not cleaned up, which is the mistake this shape invites. Note that in the commit message.

- [ ] **Step 3: Make the write atomic**

In `src/LizTerm.Core/Profiles/ProfileStore.cs`, replace `Save`:

```csharp
    public void Save(SessionProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.Name)) throw new ArgumentException("Profile needs a name", nameof(profile));
        System.IO.Directory.CreateDirectory(Directory);
        var json = JsonSerializer.Serialize(profile, ProfileJsonContext.Default.SessionProfile);
        File.WriteAllText(Path.Combine(Directory, FileNameFor(profile.Name)), json);
    }
```

with:

```csharp
    /// <summary>Writes through a temp file in the same directory and renames over the target, so a reader never
    /// sees a partial profile. Read swallows a JsonException and LoadAll skips the file, so a write interrupted
    /// by a crash, a full disk or a kill would otherwise leave a profile that looks deleted rather than broken.
    /// The temp file is a sibling on purpose: File.Move across a filesystem is a copy, which is not atomic.</summary>
    public void Save(SessionProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.Name)) throw new ArgumentException("Profile needs a name", nameof(profile));
        System.IO.Directory.CreateDirectory(Directory);
        var json = JsonSerializer.Serialize(profile, ProfileJsonContext.Default.SessionProfile);
        var path = Path.Combine(Directory, FileNameFor(profile.Name));
        // Not ".json": LoadAll enumerates *.json, and a temp file left by a crash mid-write must not be read
        // back as a profile of its own.
        var temp = path + ".tmp";
        try
        {
            File.WriteAllText(temp, json);
            File.Move(temp, path, overwrite: true);
        }
        catch
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { /* the write already failed; this is cleanup */ }
            throw;
        }
    }
```

- [ ] **Step 4: Run the Core tests**

Run: `dotnet test tests/LizTerm.Core.Tests`
Expected: PASS, including the existing round-trip and skip-unreadable tests.

- [ ] **Step 5: Commit**

```bash
git add src/LizTerm.Core/Profiles/ProfileStore.cs tests/LizTerm.Core.Tests/Profiles/ProfileStoreTests.cs
git commit -m "Write profiles through a temp file and a rename

Part of #50. Save was a plain WriteAllText, and Read swallows a
JsonException while LoadAll skips the file -- so a write interrupted by a
crash or a full disk left a truncated profile that the picker rendered as
a profile that simply is not there, indistinguishable from a deleted one.

The temp file is a sibling, because File.Move across a filesystem is a
copy and not atomic, and it is not named .json, because LoadAll
enumerates *.json and must not read a leftover back as a profile. The new
test guards exactly that.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Task 9: The picker stops overwriting pins (#50, part 3)

**Files:**
- Modify: `src/LizTerm.App/ViewModels/ProfileEditorViewModel.cs:75-80` (`ForgetPin`), plus a new `PinCleared` property
- Modify: `src/LizTerm.App/ViewModels/ProfilePickerViewModel.cs:59-69` (`EditAsync`)
- Modify: `src/LizTerm.App/Views/ProfilePickerWindow.axaml.cs`
- Test: `tests/LizTerm.App.Tests/ViewModels/ProfileViewModelsTests.cs`

**Interfaces:**
- Consumes: `PinMerge.Resolve` (Task 7).
- Produces: `ProfileEditorViewModel.PinCleared` (bool, read-only from outside).

`_editProfile` is a `Func<SessionProfile?, Task<SessionProfile?>>` and returns only the built profile, so `PinCleared` cannot travel back through it as things stand. Carry it on the returned profile's absence of a pin plus a second channel: change the picker's stored delegate type is **not** wanted — instead, have the editor window set `PinCleared` on the view model and the picker read it from a new `Func` return shape. The smallest change that keeps the delegate signature is to make the editor return the profile and expose the flag through an out-of-band property on a small record.

Use this shape: change `_editProfile` to `Func<SessionProfile?, Task<ProfileEdit?>>` where `ProfileEdit` is a two-field record. That is a localised type change inside App with three call sites (`NewAsync`, `EditAsync`, and `App`'s lambda).

- [ ] **Step 1: Write the failing test**

Append to `tests/LizTerm.App.Tests/ViewModels/ProfileViewModelsTests.cs`, inside the class:

```csharp
    /// <summary>#50. A picker opened via File > New Session sits alongside running sessions and reloads only from
    /// its own commands, so its copy can predate a pin a session window wrote. Editing that stale copy used to
    /// write the whole record back and take the pin with it.</summary>
    [Fact]
    public async Task Editing_a_stale_profile_does_not_drop_a_pin_written_since()
    {
        _store.Save(new SessionProfile { Name = "MVS", Host = "mvs", Port = 3270 });
        var pin = new CertificatePin("AA:BB", "CN=mvs", "pem");

        var picker = new ProfilePickerViewModel(_store, _ => { },
            existing =>
            {
                // Stands in for the session window pinning a certificate while the editor is open.
                _store.Update(existing!, p => p with { PinnedCertificate = pin });
                return Task.FromResult<ProfileEdit?>(new ProfileEdit(existing! with { Host = "mvs" }, PinCleared: false));
            },
            () => { });

        picker.SelectedProfile = picker.Profiles.Single();
        await picker.EditCommand.ExecuteAsync(null);

        Assert.Equal(pin, _store.Load("MVS")!.PinnedCertificate);
    }

    [Fact]
    public async Task Forget_still_clears_a_pin_the_file_has()
    {
        _store.Save(new SessionProfile { Name = "MVS", Host = "mvs", Port = 3270, PinnedCertificate = new CertificatePin("AA:BB", "CN=mvs", "pem") });

        var picker = new ProfilePickerViewModel(_store, _ => { },
            existing => Task.FromResult<ProfileEdit?>(new ProfileEdit(existing! with { PinnedCertificate = null }, PinCleared: true)),
            () => { });

        picker.SelectedProfile = picker.Profiles.Single();
        await picker.EditCommand.ExecuteAsync(null);

        Assert.Null(_store.Load("MVS")!.PinnedCertificate);
    }

    /// <summary>A rename deletes the old file, so the merge has to read the pin under the ORIGINAL name. Reading
    /// it under the new one finds nothing and loses the pin exactly as the bug does.</summary>
    [Fact]
    public async Task A_rename_carries_the_pin_across()
    {
        var pin = new CertificatePin("AA:BB", "CN=mvs", "pem");
        _store.Save(new SessionProfile { Name = "MVS", Host = "mvs", Port = 3270, PinnedCertificate = pin });

        var picker = new ProfilePickerViewModel(_store, _ => { },
            existing => Task.FromResult<ProfileEdit?>(new ProfileEdit(existing! with { Name = "MVS-CE" }, PinCleared: false)),
            () => { });

        picker.SelectedProfile = picker.Profiles.Single();
        await picker.EditCommand.ExecuteAsync(null);

        Assert.Null(_store.Load("MVS"));
        Assert.Equal(pin, _store.Load("MVS-CE")!.PinnedCertificate);
    }
```

The existing `Picker_lists_creates_edits_and_deletes` test builds a `ProfilePickerViewModel` with the old delegate shape and will stop compiling; update its lambda to return a `ProfileEdit` in the same step.

- [ ] **Step 2: Run them and watch them fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~ProfileViewModelsTests"`
Expected: FAIL to compile — `ProfileEdit` does not exist.

- [ ] **Step 3: Add the result record and the editor flag**

Create the record beside the picker view model — put it in `src/LizTerm.App/ViewModels/ProfileEdit.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.Core.Session;

namespace LizTerm.App.ViewModels;

/// <summary>What the profile editor hands back. The profile alone is not enough: the editor expresses "no pin"
/// three different ways (never had one, the user pressed Forget, the host or port was repointed) and only it
/// knows which, while only the picker can see the file. <see cref="LizTerm.Core.Profiles.PinMerge"/> resolves
/// the two.</summary>
public sealed record ProfileEdit(SessionProfile Profile, bool PinCleared);
```

In `src/LizTerm.App/ViewModels/ProfileEditorViewModel.cs`, add the flag and set it in `ForgetPin`:

```csharp
    /// <summary>The user pressed Forget. The picker needs this because a null PinnedCertificate here can also
    /// mean the editor's copy of the profile simply predates a pin written from a session window.</summary>
    public bool PinCleared { get; private set; }

    [RelayCommand]
    private void ForgetPin()
    {
        _pinnedFor = null;
        PinnedCertificate = null;
        PinCleared = true;
    }
```

- [ ] **Step 4: Merge on save in the picker**

In `src/LizTerm.App/ViewModels/ProfilePickerViewModel.cs`, change the delegate field and constructor parameter type from `Func<SessionProfile?, Task<SessionProfile?>>` to `Func<SessionProfile?, Task<ProfileEdit?>>`, then replace `NewAsync` and `EditAsync`:

```csharp
    [RelayCommand]
    private async Task NewAsync()
    {
        var edit = await _editProfile(null);
        if (edit is null) return;
        _store.Save(edit.Profile);
        Reload();
        SelectedProfile = Profiles.FirstOrDefault(p => p.Name == edit.Profile.Name);
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task EditAsync()
    {
        var original = SelectedProfile!;
        // Read the file, not the copy this picker has been holding: a session window may have written a pin into
        // it since Reload last ran, and the editor was handed the older copy.
        Reload();
        var edit = await _editProfile(_store.Load(original.Name) ?? original);
        if (edit is null) return;

        // Under the ORIGINAL name. A rename deletes that file below, so looking the pin up under the new one
        // finds nothing and loses it exactly as the bug did.
        var onDisk = _store.Load(original.Name);
        var merged = edit.Profile with { PinnedCertificate = PinMerge.Resolve(edit.Profile, onDisk, edit.PinCleared) };

        if (!merged.Name.Equals(original.Name, StringComparison.OrdinalIgnoreCase)) _store.Delete(original.Name);
        _store.Save(merged);
        Reload();
        SelectedProfile = Profiles.FirstOrDefault(p => p.Name == merged.Name);
    }
```

Add `using LizTerm.Core.Profiles;` if it is not already there (it is — `ProfileStore` lives in that namespace).

- [ ] **Step 5: Update the editor window and App to the new return type**

There are exactly two, both one line.

`src/LizTerm.App/Views/ProfileEditorWindow.axaml.cs:25` — the dialog result becomes a `ProfileEdit`:

```csharp
        if (DataContext is ProfileEditorViewModel vm && vm.TryBuild() is { } profile)
            Close(new ProfileEdit(profile, vm.PinCleared));
```

`src/LizTerm.App/Views/ProfilePickerWindow.axaml.cs:25` — the dialog's result type follows:

```csharp
            existing => new ProfileEditorWindow(existing).ShowDialog<ProfileEdit?>(this),
```

`OnCancelClick`'s `Close(null)` needs no change: null still means cancelled.

- [ ] **Step 6: Reload the picker when it is focused**

In `src/LizTerm.App/Views/ProfilePickerWindow.axaml.cs`, subscribe in the constructor after `InitializeComponent()`:

```csharp
        // A picker opened from File > New Session sits alongside running sessions, and nothing tells it when one
        // writes a pin into a profile. Reload() already preserves the selection by name; the cost is a directory
        // read on focus.
        Activated += (_, _) => (DataContext as ProfilePickerViewModel)?.Reload();
```

Add `using LizTerm.App.ViewModels;` if absent.

- [ ] **Step 7: Run the whole suite**

Run: `dotnet test LizTerm.slnx`
Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add src/LizTerm.App/ViewModels/ProfileEdit.cs src/LizTerm.App/ViewModels/ProfileEditorViewModel.cs \
        src/LizTerm.App/ViewModels/ProfilePickerViewModel.cs src/LizTerm.App/Views/ProfilePickerWindow.axaml.cs \
        src/LizTerm.App/Views/ProfileEditorWindow.axaml.cs \
        tests/LizTerm.App.Tests/ViewModels/ProfileViewModelsTests.cs
git commit -m "Stop the picker overwriting a pin a session just saved

Closes #50. ProfileStore.Update exists because a session window's profile
is fixed at construction, and App.WritePinBack was its only caller: the
picker called Save(edited), a whole-record overwrite of whatever is on
disk now. A picker opened from File > New Session reloads only from its
own commands, so its copy predates any pin written since -- and the
editor's pin panel is bound to HasPinnedCertificate, so nothing rendered
and nothing looked missing.

The editor now returns a ProfileEdit carrying PinCleared beside the
profile, because "no pin" there has three meanings and only the picker
can see the file. The merge reads the file under the ORIGINAL name, since
a rename deletes it afterwards. The picker also reloads on Activated and
before opening the editor.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Task 10: The model and code-page catalogues (#44, #45, part 1)

**Files:**
- Create: `src/LizTerm.Core/Session/TerminalModel.cs`
- Create: `src/LizTerm.Core/Session/CodePage.cs`
- Create: `tests/LizTerm.Core.Tests/Session/CatalogueTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `LizTerm.Core.Session.TerminalModel(int Number, int Rows, int Columns)`, `TerminalModel.All -> IReadOnlyList<TerminalModel>`, `TerminalModel.Find(int number) -> TerminalModel?`, and `ToString()` rendering `3 — 32x80`.
  - `LizTerm.Core.Session.CodePage(string Name, string Label)`, `CodePage.All -> IReadOnlyList<CodePage>`, `CodePage.Find(string name) -> CodePage?`, and `ToString()` rendering `cp285 — United Kingdom`.

Both lists are what b3270's `initialize` block reports, but the editor opens from the picker with no engine running, so they are static tables here and Task 12 makes them falsifiable.

**They live in Core, not App**, because `tests/LizTerm.Integration.Tests` references `LizTerm.Backend.B3270` and not App, and Task 12's test has to reach them.

**Ordering:** the engine's own order, which is ascending numeric, with `bracket` moved to the front. Alphabetical is wrong — these are strings, so it sorts `cp1026` ahead of `cp273` and scatters the numeric families.

The values below were measured against b3270 4.5ga6 on 2026-09-09.

- [ ] **Step 1: Write the failing test**

Create `tests/LizTerm.Core.Tests/Session/CatalogueTests.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.Core.Session;

namespace LizTerm.Core.Tests.Session;

public class CatalogueTests
{
    [Fact]
    public void Models_carry_their_geometry_and_render_it()
    {
        Assert.Equal(4, TerminalModel.All.Count);
        Assert.Equal("3 — 32x80", TerminalModel.Find(3)!.ToString());
        Assert.Equal("5 — 27x132", TerminalModel.Find(5)!.ToString());
        Assert.Null(TerminalModel.Find(9));

        // A model seeded from a hand-edited profile carries no geometry, and "9 — 0x0" would be a lie.
        Assert.Equal("9", new TerminalModel(9, 0, 0).ToString());
    }

    [Fact]
    public void Every_model_the_profile_default_can_hold_is_present()
    {
        foreach (var number in new[] { 2, 3, 4, 5 }) Assert.NotNull(TerminalModel.Find(number));
    }

    /// <summary>41 as reported by b3270 4.5ga6. Task 12 asserts this against a live engine; this only pins the
    /// count so an accidental deletion is loud.</summary>
    [Fact]
    public void Code_pages_are_the_full_engine_list()
    {
        Assert.Equal(41, CodePage.All.Count);
        Assert.Equal("cp285 — United Kingdom", CodePage.Find("cp285")!.ToString());
        Assert.Null(CodePage.Find("bogus-page"));
    }

    /// <summary>bracket is what this project's own TK5 sample profile uses and is named nothing like a code
    /// page, so it leads rather than trailing the list as it does in the engine's own ordering.</summary>
    [Fact]
    public void Bracket_leads_the_list_and_the_rest_stay_in_numeric_order()
    {
        Assert.Equal("bracket", CodePage.All[0].Name);
        Assert.Equal("cp037", CodePage.All[1].Name);
        Assert.Equal("cp1399", CodePage.All[^1].Name);
    }

    [Fact]
    public void Every_code_page_has_a_label_and_a_unique_name()
    {
        Assert.All(CodePage.All, p => Assert.False(string.IsNullOrWhiteSpace(p.Label)));
        Assert.Equal(CodePage.All.Count, CodePage.All.Select(p => p.Name).Distinct().Count());
    }
}
```

- [ ] **Step 2: Run it and watch it fail**

Run: `dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~CatalogueTests"`
Expected: FAIL — neither type exists.

- [ ] **Step 3: Write the model catalogue**

Create `src/LizTerm.Core/Session/TerminalModel.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

namespace LizTerm.Core.Session;

/// <summary>A 3270 model and the screen geometry it means. The geometry is what a user is actually choosing — a
/// model 5 is picked because it is 27x132, not because it is 5 — so it travels as data rather than as display
/// text, which is also what #30 (oversize geometry) will want.
///
/// b3270 reports this in its initialize block, but the profile editor opens from the picker with no engine
/// running, so the table is static and an integration test asserts it against a live engine.
/// Measured against b3270 4.5ga6 on 2026-09-09.</summary>
public sealed record TerminalModel(int Number, int Rows, int Columns)
{
    public static IReadOnlyList<TerminalModel> All { get; } =
    [
        new(2, 24, 80),
        new(3, 32, 80),
        new(4, 43, 80),
        new(5, 27, 132),
    ];

    public static TerminalModel? Find(int number) => All.FirstOrDefault(m => m.Number == number);

    /// <summary>What the editor's drop-down shows. A model seeded from a hand-edited profile has no geometry
    /// (see ProfileEditorViewModel), and "9 — 0x0" would be a lie, so an unknown one renders as the bare
    /// number.</summary>
    public override string ToString() => Rows == 0 || Columns == 0 ? Number.ToString() : $"{Number} — {Rows}x{Columns}";
}
```

- [ ] **Step 4: Write the code-page catalogue**

Create `src/LizTerm.Core/Session/CodePage.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

namespace LizTerm.Core.Session;

/// <summary>A code page b3270 knows, with a human label. The engine name leads the display because that is what
/// goes on the command line and what appears in a wire log; the label is curated from the engine's own aliases
/// rather than shown raw, because cp1047 has no alias at all and several others are terse (uk, us-intl, oldibm).
///
/// A mistyped code page does not fail: b3270 warns on stderr, starts anyway on a fallback, and echoes the bad
/// name back in its settings — and LizTerm surfaces its stderr tail only on a startup timeout or process death.
/// So a typo gave a session that connected normally with quietly wrong characters. That is what the drop-down
/// is for, over and above friendliness.
///
/// Order is the engine's own — ascending numeric — with bracket moved to the front: it is what this project's
/// TK5 sample profile uses and is named nothing like a code page. Alphabetical order would sort cp1026 ahead of
/// cp273 and scatter the families, since these are strings.
/// Measured against b3270 4.5ga6 on 2026-09-09: 41 entries.</summary>
public sealed record CodePage(string Name, string Label)
{
    public static IReadOnlyList<CodePage> All { get; } =
    [
        new("bracket", "US English (3270 brackets) — TK4-/TK5"),
        new("cp037", "US / Canada"),
        new("cp273", "German"),
        new("cp275", "Brazilian"),
        new("cp277", "Norwegian"),
        new("cp278", "Finnish / Swedish"),
        new("cp280", "Italian"),
        new("cp284", "Spanish"),
        new("cp285", "United Kingdom"),
        new("cp297", "French"),
        new("cp424", "Hebrew"),
        new("cp500", "Belgian"),
        new("cp803", "Hebrew (old)"),
        new("cp870", "Polish / Slovenian"),
        new("cp871", "Icelandic"),
        new("cp875", "Greek"),
        new("cp880", "Russian"),
        new("cp930", "Japanese (katakana)"),
        new("cp933", "Korean"),
        new("cp935", "Simplified Chinese"),
        new("cp937", "Traditional Chinese"),
        new("cp939", "Japanese (Latin)"),
        new("cp1026", "Turkish"),
        new("cp1047", "Latin-1 / Open Systems"),
        new("cp1123", "Ukrainian"),
        new("cp1140", "US / Canada (Euro)"),
        new("cp1141", "German (Euro)"),
        new("cp1142", "Norwegian (Euro)"),
        new("cp1143", "Finnish / Swedish (Euro)"),
        new("cp1144", "Italian (Euro)"),
        new("cp1145", "Spanish (Euro)"),
        new("cp1146", "United Kingdom (Euro)"),
        new("cp1147", "French (Euro)"),
        new("cp1148", "Belgian (Euro)"),
        new("cp1149", "Icelandic (Euro)"),
        new("cp1158", "Ukrainian (Euro)"),
        new("cp1160", "Thai"),
        new("cp1364", "Korean (Euro)"),
        new("cp1388", "Chinese (GB18030)"),
        new("cp1390", "Japanese (katakana, extended)"),
        new("cp1399", "Japanese (Latin, extended)"),
    ];

    public static CodePage? Find(string name) =>
        All.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>What the editor's drop-down shows.</summary>
    public override string ToString() => $"{Name} — {Label}";
}
```

- [ ] **Step 5: Run it and watch it pass**

Run: `dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~CatalogueTests"`
Expected: PASS, all five.

- [ ] **Step 6: Commit**

```bash
git add src/LizTerm.Core/Session/TerminalModel.cs src/LizTerm.Core/Session/CodePage.cs \
        tests/LizTerm.Core.Tests/Session/CatalogueTests.cs
git commit -m "Add the model and code page catalogues

Part of #44 and #45. b3270 reports both in its initialize block, but the
profile editor opens from the picker with no engine running, so these are
static tables; the next commits bind the editor to them and a later one
asserts them against a live engine so a bumped x3270 turns drift into a
red test.

They live in Core rather than App because the integration project
references the backend and not App, and its falsifying test has to reach
them.

Order is the engine's own, ascending numeric, with bracket moved to the
front: it is what the TK5 sample profile uses and is named nothing like a
code page. Alphabetical would sort cp1026 ahead of cp273.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Task 11: Both editor fields become drop-downs (#44, #45, part 2)

**Files:**
- Modify: `src/LizTerm.App/ViewModels/ProfileEditorViewModel.cs:39` and constructor
- Modify: `src/LizTerm.App/Views/ProfileEditorWindow.axaml:32` and `:36`
- Test: `tests/LizTerm.App.Tests/ViewModels/ProfileViewModelsTests.cs`, `tests/LizTerm.App.Tests/Views/ProfileEditorWindowTests.cs`

**Interfaces:**
- Consumes: `TerminalModel`, `CodePage` (Task 10).
- Produces: `ProfileEditorViewModel.TerminalModels` (`IReadOnlyList<TerminalModel>`), `SelectedModel` (`TerminalModel`), `CodePages` (`IReadOnlyList<CodePage>`), `SelectedCodePage` (`CodePage`).

**The trap:** `Model` gets no validation in `TryBuild` at all, and `CodePage` is validated only as non-empty. A `ComboBox` bound `SelectedItem` has nothing to select when the saved value is outside `ItemsSource` — a hand-edited file, or a code page a newer engine adds. **A working profile must not lose its setting because the user opened the editor.** Each list is seeded with the profile's current value when it is not already there. Seeding is preferred over `IsEditable="True"`, which would reintroduce the free-text typo this exists to remove.

Keep `Model` and `CodePage` as the properties `TryBuild` reads, so the built `SessionProfile` shape does not change; the selected items are a view over them.

- [ ] **Step 1: Write the failing tests**

Append to `tests/LizTerm.App.Tests/ViewModels/ProfileViewModelsTests.cs`, inside the class:

```csharp
    [Fact]
    public void Editor_offers_models_with_their_geometry()
    {
        var vm = new ProfileEditorViewModel(null);
        Assert.Equal(4, vm.TerminalModels.Count);
        Assert.Equal("3 — 32x80", vm.TerminalModels.Single(m => m.Number == 3).ToString());
        Assert.Equal(2, vm.SelectedModel.Number);

        vm.Name = "n";
        vm.Host = "h";
        vm.SelectedModel = vm.TerminalModels.Single(m => m.Number == 5);
        Assert.Equal(5, vm.Model);
        Assert.Equal(5, vm.TryBuild()!.Model);
    }

    [Fact]
    public void Editor_offers_code_pages_with_labels()
    {
        var vm = new ProfileEditorViewModel(null);
        Assert.Equal("bracket", vm.CodePages[0].Name);
        Assert.Equal("cp037", vm.SelectedCodePage.Name);

        vm.SelectedCodePage = vm.CodePages.Single(p => p.Name == "cp285");
        Assert.Equal("cp285", vm.CodePage);
    }

    /// <summary>#44 and #45's shared trap: a ComboBox bound SelectedItem has nothing to select when the saved
    /// value is outside ItemsSource. Opening the editor on a hand-edited profile must not silently drop its
    /// setting, so the list is seeded with whatever the profile actually holds.</summary>
    [Fact]
    public void A_value_outside_the_catalogue_survives_a_round_trip()
    {
        var odd = new SessionProfile { Name = "odd", Host = "h", Model = 9, CodePage = "cp9999" };
        var vm = new ProfileEditorViewModel(odd);

        Assert.Equal(9, vm.SelectedModel.Number);
        Assert.Equal("cp9999", vm.SelectedCodePage.Name);
        Assert.Contains(vm.TerminalModels, m => m.Number == 9);
        Assert.Contains(vm.CodePages, p => p.Name == "cp9999");

        var built = vm.TryBuild()!;
        Assert.Equal(9, built.Model);
        Assert.Equal("cp9999", built.CodePage);
    }
```

- [ ] **Step 2: Run them and watch them fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~ProfileViewModelsTests"`
Expected: FAIL to compile — `TerminalModels`, `SelectedModel`, `CodePages`, `SelectedCodePage` do not exist.

- [ ] **Step 3: Add the lists and selections**

In `src/LizTerm.App/ViewModels/ProfileEditorViewModel.cs`, replace line 39:

```csharp
    public int[] Models { get; } = [2, 3, 4, 5];
```

with:

```csharp
    /// <summary>The catalogue, seeded with this profile's own value when it falls outside it — a hand-edited
    /// file, or a model a newer engine adds. A ComboBox bound SelectedItem has nothing to select otherwise, and
    /// merely opening the editor would drop a working profile's setting. Model gets no validation in TryBuild,
    /// so nothing else would catch it either.</summary>
    public IReadOnlyList<TerminalModel> TerminalModels { get; }

    /// <summary>Same seeding rule as <see cref="TerminalModels"/>.</summary>
    public IReadOnlyList<CodePage> CodePages { get; }

    /// <summary>A view over <see cref="Model"/>, which stays the property TryBuild reads.</summary>
    public TerminalModel SelectedModel
    {
        get => TerminalModels.FirstOrDefault(m => m.Number == Model) ?? TerminalModels[0];
        set { if (value is not null) Model = value.Number; OnPropertyChanged(); }
    }

    /// <summary>A view over <see cref="CodePage"/>, which stays the property TryBuild reads.</summary>
    public CodePage SelectedCodePage
    {
        get => CodePages.FirstOrDefault(p => string.Equals(p.Name, CodePage, StringComparison.OrdinalIgnoreCase)) ?? CodePages[0];
        set { if (value is not null) CodePage = value.Name; OnPropertyChanged(); }
    }
```

In the constructor, build both lists. Insert this at the top of the constructor body, before `IsNew = existing is null;`, then fill from `existing` afterwards:

```csharp
    public ProfileEditorViewModel(SessionProfile? existing)
    {
        IsNew = existing is null;

        var models = TerminalModel.All.ToList();
        if (existing is not null && TerminalModel.Find(existing.Model) is null)
            models.Add(new TerminalModel(existing.Model, 0, 0));
        TerminalModels = models;

        var pages = CodePage.All.ToList();
        if (existing is not null && CodePage.Find(existing.CodePage) is null)
            pages.Add(new CodePage(existing.CodePage, "not in this engine's list"));
        CodePages = pages;

        if (existing is null) return;
        // ... the existing assignments, unchanged
    }
```

A seeded `TerminalModel` carries `Rows`/`Columns` of 0; Task 10's `ToString()` already renders that as the bare number rather than `9 — 0x0`, so nothing more is needed here.

- [ ] **Step 4: Bind both combos**

In `src/LizTerm.App/Views/ProfileEditorWindow.axaml`, replace line 32:

```xml
        <ComboBox ItemsSource="{Binding TerminalModels}" SelectedItem="{Binding SelectedModel}" Width="160" />
```

and line 36:

```xml
      <ComboBox Grid.Row="5" Grid.Column="1" ItemsSource="{Binding CodePages}" SelectedItem="{Binding SelectedCodePage}" Width="280" HorizontalAlignment="Left" />
```

Both render through the records' `ToString()`, so no `DisplayMemberBinding` or converter is needed.

- [ ] **Step 5: Update the window test**

`ProfileEditorWindowTests` has one test, `The_pinned_line_shows_only_for_a_pinned_profile_and_forget_hides_it`, and it touches only `PinPanel`, `PinText`, `ForgetButton` and `BackspaceBox` — none of them a combo. **It needs no change.** Give the two new combos `x:Name="ModelBox"` and `x:Name="CodePageBox"` anyway, so a later test can find them by name rather than by a type lookup that would now match two controls.

- [ ] **Step 6: Run the App tests**

Run: `dotnet test tests/LizTerm.App.Tests`
Expected: PASS, including `Editor_round_trips_an_existing_profile`, whose sample uses `CodePage = "bracket"` and `Model = 5` — both in the catalogue.

- [ ] **Step 7: Commit**

```bash
git add src/LizTerm.App/ViewModels/ProfileEditorViewModel.cs src/LizTerm.App/Views/ProfileEditorWindow.axaml \
        src/LizTerm.Core/Session/TerminalModel.cs \
        tests/LizTerm.App.Tests/ViewModels/ProfileViewModelsTests.cs tests/LizTerm.App.Tests/Views/ProfileEditorWindowTests.cs
git commit -m "Make Model and Code page annotated drop-downs

Closes #44 and #45. Code page was a free-text box validated only as
non-empty, and a typo does not fail loudly: b3270 warns on stderr, starts
on a fallback, and LizTerm surfaces its stderr tail only on a startup
timeout or process death -- so a mistyped page gave a session that
connected normally with quietly wrong characters. Model showed bare
numbers where the geometry is what the user is choosing.

Both lists seed themselves with the profile's own value when it falls
outside the catalogue. Model gets no validation in TryBuild at all, so
without that, merely opening the editor on a hand-edited profile would
drop its setting.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Task 12: The catalogues are falsifiable against a live engine (#44, #45, part 3)

**Files:**
- Create: `tests/LizTerm.Integration.Tests/EngineCatalogueTests.cs`

**Interfaces:**
- Consumes: `TerminalModel.All`, `CodePage.All` (Task 10); `BundledEngine.Require()` (`tests/LizTerm.Integration.Tests/BundledEngine.cs`).
- Produces: nothing.

A static table drifts silently when x3270 is bumped. This turns that into a red test. It skips without a bundled engine on the same terms as the rest of the project, fails under `LIZTERM_REQUIRE_ENGINE`, and always fails for an engine that is present but did not resolve — `BundledEngine.Require()` is the one place that decision lives.

The `initialize` block is the engine's first stdout line. The simplest way to read it without adding a seam to `B3270Session` is to start the located binary directly and parse that line.

- [ ] **Step 1: Write the failing test**

Create `tests/LizTerm.Integration.Tests/EngineCatalogueTests.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Diagnostics;
using System.Text.Json;
using LizTerm.Core.Session;

namespace LizTerm.Integration.Tests;

/// <summary>The profile editor's model and code page lists are static tables, because the editor opens from the
/// picker with no engine running. This is what stops them drifting: an x3270 bump that adds, removes or renames
/// an entry fails here instead of silently offering a list the engine does not share.</summary>
public class EngineCatalogueTests
{
    [Fact(Timeout = 60_000)]
    public void Our_model_table_is_the_engines()
    {
        var initialize = ReadInitialize();
        var models = Block(initialize, "models").EnumerateArray()
            .Select(m => new TerminalModel(m.GetProperty("model").GetInt32(), m.GetProperty("rows").GetInt32(), m.GetProperty("columns").GetInt32()))
            .ToList();

        Assert.Equal(models.OrderBy(m => m.Number), TerminalModel.All.OrderBy(m => m.Number));
    }

    [Fact(Timeout = 60_000)]
    public void Our_code_page_names_are_the_engines()
    {
        var initialize = ReadInitialize();
        var names = Block(initialize, "code-pages").EnumerateArray()
            .Select(p => p.GetProperty("name").GetString()!)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        // Names only. The labels are ours -- curated from the engine's aliases, which are terse or absent -- so
        // they are deliberately not asserted here.
        Assert.Equal(names, CodePage.All.Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal));
    }

    /// <summary>b3270's initialize block is its first stdout line. Read directly rather than through
    /// B3270Session, which parses out hello and tls-hello and keeps neither of these.</summary>
    private static JsonElement ReadInitialize()
    {
        var location = BundledEngine.Require();
        using var process = Process.Start(new ProcessStartInfo(location.Path, "-json")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        })!;
        try
        {
            var line = process.StandardOutput.ReadLine();
            Assert.False(string.IsNullOrWhiteSpace(line), "the engine produced no initialize line");
            return JsonDocument.Parse(line!).RootElement.Clone();
        }
        finally
        {
            try { process.StandardInput.WriteLine("""{"run":{"r-tag":"q","actions":[{"action":"Quit"}]}}"""); } catch { /* already gone */ }
            if (!process.WaitForExit(5_000)) process.Kill(entireProcessTree: true);
        }
    }

    /// <summary>The initialize payload is an array of single-key objects, so a named block is found rather than
    /// indexed.</summary>
    private static JsonElement Block(JsonElement root, string name)
    {
        var payload = root.TryGetProperty("initialize", out var wrapped) ? wrapped : root;
        if (payload.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in payload.EnumerateArray())
                if (entry.TryGetProperty(name, out var found)) return found;
        }
        else if (payload.TryGetProperty(name, out var direct)) return direct;

        Assert.Fail($"the engine's initialize block has no \"{name}\"");
        return default;
    }
}
```

- [ ] **Step 2: Run it**

Run: `dotnet test tests/LizTerm.Integration.Tests --filter "FullyQualifiedName~EngineCatalogueTests"`
Expected on a Mac that has run `native/build/build-macos.sh`: PASS. Without a bundled engine: SKIP with "no bundled engine".

If it fails on a name mismatch, **the table is wrong, not the test** — correct `CodePage.All` or `TerminalModel.All` from the engine's output and note the change in the commit message.

- [ ] **Step 3: Confirm the skip path**

Run: `dotnet test tests/LizTerm.Integration.Tests --filter "FullyQualifiedName~EngineCatalogueTests"` on a checkout with no `native/out/`.
Expected: SKIP, not FAIL — unless `LIZTERM_REQUIRE_ENGINE` is set, which CI sets on the macOS job.

- [ ] **Step 4: Commit**

```bash
git add tests/LizTerm.Integration.Tests/EngineCatalogueTests.cs
git commit -m "Assert the model and code page tables against a live engine

Part of #44 and #45. The tables are static because the profile editor
opens with no engine running; this is what makes them falsifiable, so an
x3270 bump that adds, removes or renames an entry fails here rather than
silently offering a list the engine does not share.

Names only for code pages: the labels are ours, curated because the
engine's aliases are terse or, for cp1047, absent.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Task 13: The macOS engine gate is proven to reject (#13)

**Files:**
- Modify: `.github/workflows/engines.yml` (the `engine-macos` job, after the existing "The gate accepts the engine" step at `:78`)

**Interfaces:**
- Consumes: `native/build/verify-macos.sh` (unchanged).
- Produces: nothing.

`engine-linux` runs its gate against three binaries every run; `verify-macos.sh` runs against the binary it just built and nothing else. A gate that has silently stopped rejecting passes `engine-macos` exactly as a working one does, and the job then uploads `b3270-osx-arm64` on its say-so. This is the project's own recorded lesson — plan 3b deviation 5, restated as plan 3c deviation 10.

**Both fixtures the issue proposes are unusable.** Measured on an arm64 Mac on 2026-09-09:

- `/opt/homebrew/bin/openssl` rests on "`build-macos.sh` requires Homebrew `openssl@3`", which stopped being true — the script's header now reads "Homebrew is NOT required".
- `/bin/echo` is killed by the **architecture** check, which runs first: `lipo -archs /bin/echo` reports `x86_64 arm64e` against a `uname -m` of `arm64`. It never reaches the TLS arm it was chosen to prove. This generalises — every macOS platform binary is universal with an `arm64e` slice, so none can serve as a TLS-arm fixture.

Build them with `cc` instead, which Xcode CLT already guarantees. `engine-windows` already does exactly this for its own import-arm fixture, so this is the established shape in this file rather than a new idea.

The three arms of `verify-macos.sh` and the strings they print:
- architecture — `is $ACTUAL, expected $EXPECTED` (must **not** fire for either fixture)
- dependency — `non-system dynamic dependencies`
- TLS — `does not report the expected TLS provider`

- [ ] **Step 1: Replace the gate step**

In `.github/workflows/engines.yml`, replace the `engine-macos` step named `The gate accepts the engine` (at `:78`) with:

```yaml
      # Runs whether or not the engine came from the cache. On a cache hit the build step never executes, so
      # this is the only thing between a stale cached binary and an artifact upload -- the reason engine-linux
      # and engine-windows each run their gate unconditionally too.
      #
      # Both fixtures are compiled here rather than picked off the disk. #13 proposed /opt/homebrew/bin/openssl
      # for the dependency arm, on the grounds that build-macos.sh requires Homebrew openssl@3 -- that stopped
      # being true when OpenSSL moved to the pinned tarball. It proposed /bin/echo for the TLS arm, but
      # verify-macos.sh checks the machine type FIRST and lipo reports /bin/echo as "x86_64 arm64e" against a
      # uname of arm64, so it dies at the architecture arm and never reaches the one it was chosen to prove.
      # That is not specific to echo: every macOS platform binary is universal with an arm64e slice, so none
      # can serve here. cc is guaranteed instead, because Xcode CLT is already a hard requirement of
      # build-macos.sh -- and engine-windows builds its own import fixture the same way.
      - name: The gate accepts the engine and rejects both fixtures
        run: |
          set -euo pipefail
          ARCH='${{ matrix.arch }}'
          FIX=native/build-tmp/fixtures
          mkdir -p "$FIX"

          # Built for the leg's own architecture so both fixtures clear the machine-type arm and reach the arm
          # each is meant to prove. On osx-x64 these are x86_64 binaries on an arm64 runner; the Rosetta step
          # above is what lets the TLS arm execute one.
          cat > "$FIX/clean.c" <<'EOF'
          int main(void) { return 0; }
          EOF
          cat > "$FIX/lib.c" <<'EOF'
          int fixture(void) { return 0; }
          EOF
          cat > "$FIX/uses-lib.c" <<'EOF'
          int fixture(void);
          int main(void) { return fixture(); }
          EOF

          # clean: links only libSystem and exits 0 with no banner -- clears architecture and dependency, dies at TLS.
          cc -arch "$ARCH" -o "$FIX/clean" "$FIX/clean.c"
          # dirty: an install_name outside /usr/lib and /System/Library -- clears architecture, dies at dependency.
          cc -arch "$ARCH" -dynamiclib -install_name "@executable_path/libfixture.dylib" -o "$FIX/libfixture.dylib" "$FIX/lib.c"
          cc -arch "$ARCH" -o "$FIX/dirty" "$FIX/uses-lib.c" -L"$FIX" -lfixture

          echo "--- the gate must accept the engine it is gating"
          native/build/verify-macos.sh native/out/${{ matrix.rid }}/b3270 "$ARCH"

          # Capture and match; do not pipe. grep -q exits on its first match and SIGPIPEs the writer, which
          # under pipefail becomes 141 -- the trap this script family has already paid for once.
          reject() {
            echo "--- the gate must reject $2 by its $1"
            if output=$(native/build/verify-macos.sh "$2" "$ARCH" 2>&1); then
              printf "%s\n" "$output"
              echo "FAILED: verify-macos.sh accepted $2; its $1 is not working" >&2
              exit 1
            fi
            printf "%s\n" "$output"
            case "$output" in
              *"$3"*) echo "OK: rejected by the $1" ;;
              *) echo "FAILED: verify-macos.sh rejected $2, but not by its $1 (nothing matched: $3)" >&2
                 exit 1 ;;
            esac
          }

          reject "dependency allowlist" "$FIX/dirty" "non-system dynamic dependencies"
          reject "TLS provider check"   "$FIX/clean" "does not report the expected TLS provider"
```

- [ ] **Step 2: Prove both fixtures locally before pushing**

On the Mac, run the same three invocations by hand:

```bash
ARCH=$(uname -m); FIX=native/build-tmp/fixtures; mkdir -p "$FIX"
printf 'int main(void) { return 0; }\n' > "$FIX/clean.c"
printf 'int fixture(void) { return 0; }\n' > "$FIX/lib.c"
printf 'int fixture(void);\nint main(void) { return fixture(); }\n' > "$FIX/uses-lib.c"
cc -arch "$ARCH" -o "$FIX/clean" "$FIX/clean.c"
cc -arch "$ARCH" -dynamiclib -install_name "@executable_path/libfixture.dylib" -o "$FIX/libfixture.dylib" "$FIX/lib.c"
cc -arch "$ARCH" -o "$FIX/dirty" "$FIX/uses-lib.c" -L"$FIX" -lfixture
native/build/verify-macos.sh "$FIX/dirty" "$ARCH" 2>&1 | tail -3
native/build/verify-macos.sh "$FIX/clean" "$ARCH" 2>&1 | tail -3
```

Expected: `dirty` prints `non-system dynamic dependencies`; `clean` prints `does not report the expected TLS provider`. **Neither may print `expected` from the architecture arm** — that means it died at the wrong check and the fixture is not proving what it claims. If `dirty` clears the dependency arm instead, check `otool -L "$FIX/dirty"`: the install name must not begin `/usr/lib` or `/System/Library`.

- [ ] **Step 3: Confirm the workflow parses**

Run: `python3 -c "import yaml,sys; yaml.safe_load(open('.github/workflows/engines.yml')); print('ok')"`
Expected: `ok`.

- [ ] **Step 4: Commit**

```bash
git add .github/workflows/engines.yml
git commit -m "Prove the macOS engine gate rejects, with fixtures it builds itself

Closes #13. engine-linux runs its gate against three binaries every run;
verify-macos.sh ran against the binary it had just built and nothing
else, so a gate that had silently stopped rejecting passed engine-macos
exactly as a working one does, and the job uploaded the artifact on its
say-so.

Both fixtures the issue proposed are unusable.
/opt/homebrew/bin/openssl rested on build-macos.sh requiring Homebrew
openssl@3, which stopped being true when OpenSSL moved to the pinned
tarball. /bin/echo is killed by the machine-type arm, which runs first --
lipo reports x86_64 arm64e against a uname of arm64 -- so it never
reaches the TLS arm it was chosen to prove, and that applies to every
macOS platform binary.

Both are compiled with cc instead, guaranteed because Xcode CLT is
already required to build the engine at all, and the same shape
engine-windows already uses for its import fixture. Each rejection is
asserted on the message its own arm prints, not on a bare non-zero exit.

Note this edits native/build/'s neighbourhood in the workflow only, not
the cache keys, so it does not force a cold engine rebuild.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Task 14: Full verification

**Files:** none.

- [ ] **Step 1: Full suite**

Run: `dotnet test LizTerm.slnx`
Expected: every project passes; live host tests skip unless `LIZTERM_TEST_HOST` is set.

- [ ] **Step 2: Zero warnings**

Run: `dotnet build LizTerm.slnx --no-incremental 2>&1 | grep -c " warning "`
Expected: `0`.

- [ ] **Step 3: Manual pass on macOS**

```bash
dotnet run --project src/LizTerm.App
```

Check, in one session:
- The splash shows the icon above the wordmark.
- The picker opens; Edit shows Model as `2 — 24x80` and Code page as `cp037 — US / Canada`; both drop-downs list their full catalogue with `bracket` first. Cancel rather than Save.
- Connect to the live host. The status bar reads `3279-2-E`, not `Model 2-E`.
- File > Connect is greyed out while connected; Disconnect is live.
- File shows `IND$FILE Transfer...`.
- Keys shows Dup and Field Mark, and both send.
- Help > About shows the icon beside the name and a copyright line with no ` - BSD-3-Clause`.

- [ ] **Step 4: Push and watch CI**

```bash
git push -u origin claude/backlog-issues-review-2d6bdb
```

`platforms.yml` is path-filtered on PRs and this branch touches `src/`, `tests/` and `.github/workflows/`, so `engines / engine-macos` will run both legs and exercise Task 13's new step. Note that editing `engines.yml` does **not** rotate the engine cache keys — those hash `native/build/*.sh` — so this should be a warm run.

---

## Notes for the executor

- **Do not `cd` out of the worktree.** All commands run from the worktree root.
- **Tasks 1-6 are independent** of each other and of 7-13; 7 → 9 and 10 → 11 → 12 are ordered.
- **If a task's line numbers do not match**, the file has moved under you — find the symbol by name rather than trusting the number, and say so in the commit.
- **Task 9 changes a delegate signature** used by `App`. If `grep -rn "_editProfile" src/ tests/` finds a call site this plan did not list, update it and note it.
- **Never commit a wire log**: the live IND$FILE test types a password through the session.
