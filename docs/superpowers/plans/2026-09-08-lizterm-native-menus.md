# Native Menus and the macOS Application Menu Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give macOS a real system menu bar and application menu, without changing what any keystroke does to the host, and without deleting the in-window menu that works today on Windows and Linux.

**Architecture:** `SessionWindow` carries two renderers over one `NativeMenu` definition — today's classic `<Menu>` and a new `<NativeMenuBar>` — and a `MenuStrategy` picks between them per platform, defaulting to native on macOS and classic elsewhere. An application-level `NativeMenu` in `App.axaml` supplies About and Quit, which is what finally gives the profile picker a menu bar on macOS.

**Tech Stack:** .NET 10, Avalonia 12.1.2 (`NativeMenu`, `NativeMenuItem`, `NativeMenuItemSeparator`, `NativeMenuBar`), CommunityToolkit.Mvvm, xunit.v3 in VSTest mode, Avalonia headless platform for UI tests.

**Spec:** `docs/superpowers/specs/2026-09-08-lizterm-native-menus-design.md`

## Global Constraints

- **No gesture on any File, Keys or Help menu item, ever.** Measured: a `NativeMenuItem` gesture is a real AppKit key equivalent, and `NSApplication.sendEvent:` offers the key to the main menu before the key window's responder chain — `TerminalScreen` never sees it. `Gesture="F1"` on a PF1 item produces a 3270 client that cannot send PF1, silently. The single exception is the application menu's `Cmd+Q`, which is macOS-only by construction and which `Keymap` claims no Meta chord against.
- **Edit gestures activate the async methods, never the `[RelayCommand]`s.** `CopyAsync()`, `PasteAsync()`, `SelectAll()` carry their own guards; the commands keep CommunityToolkit's default of disabling while running, which is safe for a click and wrong for a keystroke.
- **`MenuItem.Click` is `EventHandler<RoutedEventArgs>`; `NativeMenuItem.Click` is `EventHandler<EventArgs>`.** The two menus cannot share a handler. Every shared action is two one-line handlers over one private method.
- **`LizTerm.Core` and `LizTerm.Backend.B3270` are untouched by this plan.** `LizTerm.App` names the backend in exactly one place, `src/LizTerm.App/SessionFactory.cs`; Task 2 is the only task allowed to add a backend name, and only there.
- **User-visible wording is copied verbatim, never re-worded.** Menu headers keep their exact text and `_` mnemonics; `StatusFormatter`'s "b3270, not found" and "b3270, not started" are reused rather than re-spelled.
- **Zero warnings before anything is called done:** `dotnet build LizTerm.slnx --no-incremental 2>&1 | grep -c " warning "` must print `0`. An incremental build hides warnings from projects it does not recompile.
- Tests are xunit.v3 in VSTest mode, so `--filter` takes `FullyQualifiedName~`.

---

## File Structure

**Created:**

- `src/LizTerm.App/Menus/MenuStrategy.cs` — the two pure per-platform decisions and the `LIZTERM_MENU` override. No Avalonia types.
- `src/LizTerm.App/Menus/MenuLookup.cs` — one `NativeMenuItem` lookup by header path, shared by the window's code-behind and the tests, so production and test agree on how an item is found.
- `tests/LizTerm.App.Tests/Menus/MenuStrategyTests.cs` — plain `[Fact]`/`[Theory]`, no platform branch.
- `tests/LizTerm.App.Tests/Views/NativeMenuTests.cs` — `[AvaloniaFact]`, the native menu's structure, bindings and gestures.

**Modified:**

- `src/LizTerm.App/SessionFactory.cs` — a non-throwing `CheckBackendOrUnknown` for a session-less About.
- `src/LizTerm.App/App.axaml` — `Name="LizTerm"` and the application `NativeMenu`.
- `src/LizTerm.App/App.axaml.cs` — `ShowAboutAsync`, and the application menu's two handlers.
- `src/LizTerm.App/Views/SessionWindow.axaml` — the native menu definition and `NativeMenuBar` beside the untouched classic `<Menu>`; `x:Name` on the classic About item.
- `src/LizTerm.App/Views/SessionWindow.axaml.cs` — the strategy seam, the native handlers, gesture assignment, About delegation.
- `tests/LizTerm.App.Tests/SessionFactoryTests.cs` — coverage for the new factory method.
- `README.md`, `CLAUDE.md` — the new environment variable and the menu architecture.

**Deliberately not modified:** `src/LizTerm.App/Views/ProfilePickerWindow.axaml` (it inherits the application menu on macOS; a `NativeMenuBar` would give Windows and Linux a menu bar the picker has never had), and the five existing `FindControl<MenuItem>` assertions in `tests/LizTerm.App.Tests/Views/SessionWindowTests.cs` (while both menus exist, both are covered).

---

### Task 1: MenuStrategy

**Files:**
- Create: `src/LizTerm.App/Menus/MenuStrategy.cs`
- Test: `tests/LizTerm.App.Tests/Menus/MenuStrategyTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `internal static class LizTerm.App.Menus.MenuStrategy` with `const string Variable = "LIZTERM_MENU"`, `static bool UseNativeMenu { get; }`, `static bool Decide(string? variable, bool isMacOS)`, `static bool AboutInHelpMenu(bool isMacOS)`.

- [ ] **Step 1: Write the failing tests**

Create `tests/LizTerm.App.Tests/Menus/MenuStrategyTests.cs`:

```csharp
using LizTerm.App.Menus;

namespace LizTerm.App.Tests.Menus;

/// <summary>The whole per-platform menu decision, asserted without an operating system or the environment,
/// the way EngineRequirement.Decide is: every combination is reachable on every machine.</summary>
public class MenuStrategyTests
{
    [Theory]
    [InlineData("native")]
    [InlineData("NATIVE")]
    [InlineData("  native  ")]
    public void The_variable_can_force_the_native_menu_on_any_platform(string variable)
    {
        Assert.True(MenuStrategy.Decide(variable, isMacOS: true));
        Assert.True(MenuStrategy.Decide(variable, isMacOS: false));
    }

    [Theory]
    [InlineData("classic")]
    [InlineData("Classic")]
    [InlineData("\tclassic\n")]
    public void The_variable_can_force_the_classic_menu_on_any_platform(string variable)
    {
        Assert.False(MenuStrategy.Decide(variable, isMacOS: true));
        Assert.False(MenuStrategy.Decide(variable, isMacOS: false));
    }

    /// <summary>The default is the point of the staging device: native only where it has been watched to work.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Without_the_variable_only_macOS_gets_the_native_menu(string? variable)
    {
        Assert.True(MenuStrategy.Decide(variable, isMacOS: true));
        Assert.False(MenuStrategy.Decide(variable, isMacOS: false));
    }

    /// <summary>A typo must not leave the app with no menu bar at all: it is a development escape hatch, and
    /// the quiet fallback is the less damaging of the two failures.</summary>
    [Theory]
    [InlineData("banana")]
    [InlineData("1")]
    [InlineData("true")]
    public void An_unrecognised_value_falls_back_to_the_platform_default(string variable)
    {
        Assert.True(MenuStrategy.Decide(variable, isMacOS: true));
        Assert.False(MenuStrategy.Decide(variable, isMacOS: false));
    }

    [Fact]
    public void About_belongs_in_the_help_menu_everywhere_except_macOS()
    {
        Assert.False(MenuStrategy.AboutInHelpMenu(isMacOS: true));
        Assert.True(MenuStrategy.AboutInHelpMenu(isMacOS: false));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~MenuStrategyTests"`
Expected: build failure — `MenuStrategy` does not exist, and `LizTerm.App.Menus` is not a namespace.

- [ ] **Step 3: Write the implementation**

Create `src/LizTerm.App/Menus/MenuStrategy.cs`:

```csharp
namespace LizTerm.App.Menus;

/// <summary>Which menu renderer a platform gets, and where About belongs on it.
///
/// The classic in-window menu and the NativeMenuBar both render the same definition, and this picks between
/// them. The default is native on macOS and classic on Windows and Linux, because NativeMenuBar's in-window
/// rendering has not been looked at on either of those platforms — this project has no Windows or Linux GUI,
/// and CI has no GUI at all — and replacing a menu that works with one nobody has seen is the wrong default.
/// LIZTERM_MENU=native is how that gets looked at, without a rebuild. This whole class is a staging device;
/// see section 8 of the spec for what has to be true before the classic menu is deleted.</summary>
internal static class MenuStrategy
{
    public const string Variable = "LIZTERM_MENU";

    public static bool UseNativeMenu =>
        Decide(Environment.GetEnvironmentVariable(Variable), OperatingSystem.IsMacOS());

    /// <summary>Kept pure, taking the platform rather than reading it, so every combination is testable on
    /// every machine — the shape EngineRequirement.Decide uses for the same reason.</summary>
    public static bool Decide(string? variable, bool isMacOS) => variable?.Trim().ToLowerInvariant() switch
    {
        "native" => true,
        "classic" => false,
        // Anything else, blank included: the platform default. A typo that left the app with no menu bar
        // would be a worse outcome than one that quietly draws the usual one.
        _ => isMacOS,
    };

    /// <summary>macOS puts About in the application menu, so the Help item must not also carry one.</summary>
    public static bool AboutInHelpMenu(bool isMacOS) => !isMacOS;
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~MenuStrategyTests"`
Expected: PASS, 15 cases.

- [ ] **Step 5: Commit**

```bash
git add src/LizTerm.App/Menus/MenuStrategy.cs tests/LizTerm.App.Tests/Menus/MenuStrategyTests.cs
git commit -m "Add MenuStrategy: which renderer a platform gets, as a pure decision"
```

---

### Task 2: A session-less engine for About

**Files:**
- Modify: `src/LizTerm.App/SessionFactory.cs`
- Test: `tests/LizTerm.App.Tests/SessionFactoryTests.cs`

**Interfaces:**
- Consumes: nothing from Task 1.
- Produces: `SessionFactory.CheckBackendOrUnknown()` returning `EngineInfo`, and the internal seam `SessionFactory.CheckBackendOrUnknown(string? overridePath, string baseDirectory)`.

About is currently only reachable from a session window, which always has an `EngineInfo`. The application menu adds a caller with no session, and `CheckBackend()` *throws* `BackendUnavailableException` when the engine is missing. This is the one task permitted to add a backend name, and only in `SessionFactory.cs`, per the dependency rule.

- [ ] **Step 1: Write the failing tests**

Append to `tests/LizTerm.App.Tests/SessionFactoryTests.cs` (inside the existing class; keep the file's existing `using` directives and add `using LizTerm.Core.Session;` only if it is not already there):

```csharp
    /// <summary>About must render something for every outcome, so the not-found case is a value rather than an
    /// exception — and it says Unknown rather than borrowing a provenance it does not have, which is what
    /// StatusFormatter renders as "b3270, not found".</summary>
    [Fact]
    public void CheckBackendOrUnknown_reports_a_missing_engine_as_unknown_rather_than_throwing()
    {
        var empty = Directory.CreateTempSubdirectory("lizterm-factory-").FullName;
        try
        {
            var engine = SessionFactory.CheckBackendOrUnknown("/nonexistent/b3270", empty);

            Assert.Equal(EngineSource.Unknown, engine.Source);
            Assert.Equal("b3270", engine.Name);
            Assert.Null(engine.Version);
            Assert.Equal("", engine.Path);
        }
        finally
        {
            Directory.Delete(empty, recursive: true);
        }
    }
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~SessionFactoryTests"`
Expected: build failure — no overload of `CheckBackendOrUnknown` taking two arguments.

- [ ] **Step 3: Write the implementation**

In `src/LizTerm.App/SessionFactory.cs`, add immediately below the existing `CheckBackend()`:

```csharp
    /// <summary>CheckBackend's non-throwing sibling, for About, which must render something whatever the
    /// outcome. A binary that was never located becomes EngineSource.Unknown, which StatusFormatter renders
    /// as "b3270, not found" — the same value SessionFactory.Create already hands a session that has nothing
    /// to run, so the two paths cannot disagree about what a missing engine looks like.</summary>
    public static EngineInfo CheckBackendOrUnknown()
    {
        var (overridePath, baseDirectory) = DefaultLocation;
        return CheckBackendOrUnknown(overridePath, baseDirectory);
    }

    /// <summary>Test seam, matching Create's: resolves from an explicit override and base directory instead of
    /// the process environment and the app's own directory.</summary>
    internal static EngineInfo CheckBackendOrUnknown(string? overridePath, string baseDirectory)
    {
        try
        {
            var location = B3270Locator.Find(overridePath, baseDirectory);
            return new EngineInfo("b3270", null, location.Path, location.Source);
        }
        catch (BackendUnavailableException)
        {
            return new EngineInfo("b3270", null, B3270Location.Unknown.Path, B3270Location.Unknown.Source);
        }
    }
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~SessionFactoryTests"`
Expected: PASS, including the pre-existing tests in that class.

- [ ] **Step 5: Commit**

```bash
git add src/LizTerm.App/SessionFactory.cs tests/LizTerm.App.Tests/SessionFactoryTests.cs
git commit -m "Add CheckBackendOrUnknown: an engine value for About with no session"
```

---

### Task 3: One spelling of About

**Files:**
- Modify: `src/LizTerm.App/App.axaml.cs`
- Modify: `src/LizTerm.App/Views/SessionWindow.axaml.cs:48-59` (the existing `OnAboutClick`)
- Test: `tests/LizTerm.App.Tests/Views/SessionWindowTests.cs`

**Interfaces:**
- Consumes: `SessionFactory.CheckBackendOrUnknown()` from Task 2.
- Produces: `public Task App.ShowAboutAsync(Window? preferredOwner)`, which Task 4's application-menu handler calls with `null`; and `SessionWindow.ShowAboutAsync()`, the private body Task 5's native About handler reuses.

- [ ] **Step 1: Write the failing test**

Append to `tests/LizTerm.App.Tests/Views/SessionWindowTests.cs` (the file already has `using Avalonia.Controls;` and `using LizTerm.App.Views;`; add `using Avalonia;` if absent):

```csharp
    /// <summary>About from a session window describes that session's engine and is modal to it. One method
    /// serves this and the macOS application menu, so this is also the test that the shared spelling works.</summary>
    [AvaloniaFact]
    public void About_opens_over_the_session_window_that_asked_for_it()
    {
        var (window, _, _, _, _) = Show();
        var app = (LizTerm.App.App)Application.Current!;

        _ = app.ShowAboutAsync(window);

        var dialog = Assert.Single(window.OwnedWindows);
        Assert.IsType<AboutWindow>(dialog);
        dialog.Close();
        Assert.Empty(window.OwnedWindows);
    }
```

Do not `await` the call: `ShowDialog` completes only when the dialog closes, exactly as the existing File Transfer test relies on.

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~About_opens_over_the_session_window"`
Expected: build failure — `App` has no `ShowAboutAsync`.

- [ ] **Step 3: Write the implementation**

In `src/LizTerm.App/App.axaml.cs`, add to the `App` class (it already has `using Avalonia.Controls;`, `using Avalonia.Controls.ApplicationLifetimes;`, `using LizTerm.App.ViewModels;` and `using LizTerm.App.Views;`):

```csharp
    /// <summary>The one spelling of About, shared by a session window's Help item and the macOS application
    /// menu. The application menu may fire with no session at all, which is why the engine has a session-less
    /// fallback and the owner has a null one.</summary>
    public async Task ShowAboutAsync(Window? preferredOwner)
    {
        // A session's own Help item stays modal to that session even if another window is active; the
        // application menu passes null and takes whatever the user is looking at.
        var owner = preferredOwner ?? ActiveWindow();
        // About describes the session being looked at, version and all, when there is one; otherwise the
        // located binary, which has never been started and so reports no version.
        var engine = (owner?.DataContext as SessionViewModel)?.Engine ?? SessionFactory.CheckBackendOrUnknown();
        var about = new AboutWindow(AppVersion.Current, engine, SessionFactory.OverrideOrigin);
        if (owner is null) about.Show();
        else await about.ShowDialog(owner);
    }

    private Window? ActiveWindow()
    {
        var windows = (ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Windows;
        return windows?.FirstOrDefault(w => w.IsActive) ?? windows?.FirstOrDefault();
    }
```

Then replace the body of `OnAboutClick` in `src/LizTerm.App/Views/SessionWindow.axaml.cs` with:

```csharp
    private async void OnAboutClick(object? sender, RoutedEventArgs e) => await ShowAboutAsync();

    /// <summary>The shared body. Task 5 adds the native menu's About handler over this same method — the two
    /// menus' Click events have different delegate shapes, so neither can reuse the other's handler.</summary>
    private async Task ShowAboutAsync()
    {
        if (ViewModel is not { } vm) return;
        try
        {
            if (Avalonia.Application.Current is App app) await app.ShowAboutAsync(this);
        }
        catch (Exception ex)
        {
            vm.ErrorMessage = "Could not open About: " + ex.Message;
        }
        Screen.Focus();
    }
```

This keeps the session window's existing behaviour exactly: a failure reaches the error banner, and focus returns to the screen afterwards. From the application menu there is no banner to use, so a failure there is not caught — About is not worth taking the process down for, and the next step's handler is where that is handled.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~SessionWindowTests"`
Expected: PASS, the new test and all pre-existing ones in that class.

- [ ] **Step 5: Commit**

```bash
git add src/LizTerm.App/App.axaml.cs src/LizTerm.App/Views/SessionWindow.axaml.cs tests/LizTerm.App.Tests/Views/SessionWindowTests.cs
git commit -m "Give About one spelling, shared by the Help item and the coming application menu"
```

---

### Task 4: The application menu

**Files:**
- Modify: `src/LizTerm.App/App.axaml`
- Modify: `src/LizTerm.App/App.axaml.cs`
- Test: `tests/LizTerm.App.Tests/Views/NativeMenuTests.cs` (create)

**Interfaces:**
- Consumes: `App.ShowAboutAsync(Window?)` from Task 3; the existing public `App.Quit()`.
- Produces: an application-level `NativeMenu` reachable as `NativeMenu.GetMenu(Application.Current)`.

- [ ] **Step 1: Write the failing test**

Create `tests/LizTerm.App.Tests/Views/NativeMenuTests.cs`:

```csharp
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;

namespace LizTerm.App.Tests.Views;

/// <summary>The native menu definition, reached through NativeMenu.GetMenu rather than the visual tree:
/// NativeMenuItem is not a Control, so FindControl cannot see it. Bindings on it do resolve under the
/// headless platform, two-way write-back included, which is what makes these assertions possible.</summary>
public class NativeMenuTests
{
    [AvaloniaFact]
    public void The_application_menu_carries_about_and_quit()
    {
        var menu = NativeMenu.GetMenu(Application.Current!);

        Assert.NotNull(menu);
        var headers = menu!.Items.OfType<NativeMenuItem>().Select(i => i.Header).ToArray();
        Assert.Equal(["About LizTerm", "Quit LizTerm"], headers);
    }

    /// <summary>The one gesture outside the Edit menu in this whole feature. The application menu is macOS-only
    /// by construction and Keymap claims no Meta chord, so Cmd+Q cannot swallow a key the host needed; omitting
    /// it is the riskier choice, since a replaced application menu may not inherit AppKit's own Quit item.</summary>
    [AvaloniaFact]
    public void Quit_carries_cmd_q_and_about_carries_no_gesture()
    {
        var menu = NativeMenu.GetMenu(Application.Current!)!;
        var items = menu.Items.OfType<NativeMenuItem>().ToArray();

        Assert.Null(items.Single(i => i.Header == "About LizTerm").Gesture);
        Assert.Equal(new KeyGesture(Key.Q, KeyModifiers.Meta), items.Single(i => i.Header == "Quit LizTerm").Gesture);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~NativeMenuTests"`
Expected: FAIL — `Assert.NotNull(menu)` fails, because `App.axaml` declares no `NativeMenu`.

- [ ] **Step 3: Write the implementation**

Replace `src/LizTerm.App/App.axaml` with:

```xml
<Application xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             x:Class="LizTerm.App.App"
             Name="LizTerm"
             RequestedThemeVariant="Dark">
  <Application.Styles>
    <FluentTheme />
  </Application.Styles>

  <!-- The macOS application menu. It hangs off the application rather than a window, which is what gives the
       profile picker a menu bar: the picker is the app's startup state and the state it returns to when the
       last session closes, and until now macOS showed nothing there. Other platforms render none of this. -->
  <NativeMenu.Menu>
    <NativeMenu>
      <NativeMenuItem Header="About LizTerm" Click="OnAboutClick" />
      <NativeMenuItem Header="Quit LizTerm" Gesture="Cmd+Q" Click="OnQuitClick" />
    </NativeMenu>
  </NativeMenu.Menu>
</Application>
```

Add the two handlers to `src/LizTerm.App/App.axaml.cs`:

```csharp
    /// <summary>The application menu fires with no owner of its own: ShowAboutAsync takes the active window.
    /// A failure here has no error banner to reach, and About is not worth taking the process down for.</summary>
    private async void OnAboutClick(object? sender, EventArgs e)
    {
        try { await ShowAboutAsync(null); }
        catch { /* nothing to report it on, and nothing about About is worth a crash */ }
    }

    private void OnQuitClick(object? sender, EventArgs e) => Quit();
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~NativeMenuTests"`
Expected: PASS, 2 tests.

- [ ] **Step 5: Commit**

```bash
git add src/LizTerm.App/App.axaml src/LizTerm.App/App.axaml.cs tests/LizTerm.App.Tests/Views/NativeMenuTests.cs
git commit -m "Add the macOS application menu, which is what gives the picker a menu bar"
```

---

### Task 5: SessionWindow's native menu beside the classic one

**Files:**
- Create: `src/LizTerm.App/Menus/MenuLookup.cs`
- Modify: `src/LizTerm.App/Views/SessionWindow.axaml`
- Modify: `src/LizTerm.App/Views/SessionWindow.axaml.cs`
- Test: `tests/LizTerm.App.Tests/Views/NativeMenuTests.cs`

**Interfaces:**
- Consumes: `MenuStrategy.UseNativeMenu` and `MenuStrategy.AboutInHelpMenu(bool)` from Task 1; `SessionWindow.ShowAboutAsync()` from Task 3.
- Produces: `internal static NativeMenuItem? MenuLookup.Item(NativeMenu? menu, string top, string child)`; `internal SessionWindow(bool useNativeMenu)`, the seam Task 6's two-strategy test needs.

`x:Name` is not relied upon for `NativeMenuItem`: it is not a `Control`, `FindControl` cannot reach it, and whether the XAML compiler generates a usable field for it is not something this plan needs to depend on. One lookup helper serves the window's code-behind and the tests, so both agree on how an item is found.

- [ ] **Step 1: Write the failing tests**

Append to `tests/LizTerm.App.Tests/Views/NativeMenuTests.cs` (add `using LizTerm.App.Tests.Fakes;`, `using LizTerm.App.ViewModels;`, `using LizTerm.App.Views;`, `using LizTerm.Core.Screen;`, `using LizTerm.Core.Session;`, `using LizTerm.App.Menus;`):

```csharp
    private static (SessionWindow Window, SessionViewModel Vm, FakeEmulatorSession Session, FakeTextClipboard Clipboard) Show(bool useNativeMenu = true)
    {
        var session = new FakeEmulatorSession();
        var buffer = new ScreenBuffer(24, 80);
        buffer.SetText(2, 3, "hello", null, null, null);
        session.CurrentScreen = buffer.Snapshot();
        var clipboard = new FakeTextClipboard();
        var vm = new SessionViewModel(session, action => action(), clipboard);
        var window = new SessionWindow(useNativeMenu) { DataContext = vm };
        window.Show();
        return (window, vm, session, clipboard);
    }

    private static NativeMenuItem Item(SessionWindow window, string top, string child) =>
        MenuLookup.Item(NativeMenu.GetMenu(window), top, child)
        ?? throw new InvalidOperationException($"no native menu item {top} > {child}");

    [AvaloniaFact]
    public void The_window_menu_has_the_same_four_top_level_menus_as_the_classic_one()
    {
        var (window, _, _, _) = Show();

        var headers = NativeMenu.GetMenu(window)!.Items.OfType<NativeMenuItem>().Select(i => i.Header).ToArray();
        Assert.Equal(["_File", "_Edit", "_Keys", "_Help"], headers);
    }

    [AvaloniaFact]
    public void File_transfer_follows_the_connection_state()
    {
        var (window, _, session, _) = Show();
        var item = Item(window, "_File", "File _Transfer...");

        Assert.False(item.IsEnabled);
        session.RaiseConnection(ConnectionState.Connected3270);
        Assert.True(item.IsEnabled);
        session.RaiseConnection(ConnectionState.Disconnected);
        Assert.False(item.IsEnabled);
    }

    /// <summary>The delicate one. A wire log value corrected from inside its own change notification is
    /// invisible to a two-way binding mid-write, which is why SessionViewModel marshals its correction through
    /// dispatch; that behaviour has to survive the move to a menu the visual tree cannot see.</summary>
    [AvaloniaFact]
    public void The_wire_log_item_is_a_checkbox_bound_two_way()
    {
        var (window, vm, _, _) = Show();
        var item = Item(window, "_Help", "_Wire Log");

        Assert.Equal(MenuItemToggleType.CheckBox, item.ToggleType);
        Assert.False(item.IsChecked);

        var directory = Path.Combine(Path.GetTempPath(), "lizterm-native-" + Guid.NewGuid().ToString("N"));
        vm.WireLogDirectory = directory;
        try
        {
            vm.IsWireLogging = true;
            Assert.True(item.IsChecked);

            item.IsChecked = false;
            Assert.False(vm.IsWireLogging);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>On a CI machine this covers the visible-in-Help branch only; the macOS branch is covered by
    /// running the app on the Mac, which the spec's section 8 requires anyway. No mutable platform static is
    /// introduced to close that gap — it would make every menu test order-dependent.</summary>
    [AvaloniaFact]
    public void About_is_in_the_help_menu_on_this_platform_exactly_when_the_strategy_says_so()
    {
        var (window, _, _, _) = Show();
        var expected = MenuStrategy.AboutInHelpMenu(OperatingSystem.IsMacOS());

        Assert.Equal(expected, Item(window, "_Help", "_About LizTerm...").IsVisible);
        Assert.Equal(expected, window.FindControl<MenuItem>("AboutMenuItem")!.IsVisible);
    }

    /// <summary>Measured: with a NativeMenu installed and the classic Menu still visible, macOS drew both — an
    /// in-window bar beneath a system bar. Hiding the classic one under the native strategy is required, not tidy.</summary>
    [AvaloniaFact]
    public void Exactly_one_renderer_is_visible_under_each_strategy()
    {
        var (native, _, _, _) = Show(useNativeMenu: true);
        Assert.False(native.FindControl<Menu>("ClassicMenu")!.IsVisible);
        Assert.True(native.FindControl<NativeMenuBar>("NativeBar")!.IsVisible);

        var (classic, _, _, _) = Show(useNativeMenu: false);
        Assert.True(classic.FindControl<Menu>("ClassicMenu")!.IsVisible);
        Assert.False(classic.FindControl<NativeMenuBar>("NativeBar")!.IsVisible);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~NativeMenuTests"`
Expected: build failure — `MenuLookup` does not exist and `SessionWindow` has no `(bool)` constructor.

- [ ] **Step 3: Write the lookup helper**

Create `src/LizTerm.App/Menus/MenuLookup.cs`:

```csharp
using Avalonia.Controls;

namespace LizTerm.App.Menus;

/// <summary>Finding a NativeMenuItem by its header path. NativeMenuItem is not a Control, so FindControl
/// cannot reach it and the visual tree does not contain it; this is the one way the window's code-behind and
/// the tests both look one up, so they cannot disagree about what "the Wire Log item" means.</summary>
internal static class MenuLookup
{
    public static NativeMenuItem? Item(NativeMenu? menu, string top, string child) =>
        Item(Item(menu, top)?.Menu, child);

    public static NativeMenuItem? Item(NativeMenu? menu, string header) =>
        menu?.Items.OfType<NativeMenuItem>().FirstOrDefault(i => i.Header == header);
}
```

- [ ] **Step 4: Add the native definition to the XAML**

In `src/LizTerm.App/Views/SessionWindow.axaml`, add `x:Name="ClassicMenu"` to the existing `<Menu DockPanel.Dock="Top">` and `x:Name="AboutMenuItem"` to its `_About LizTerm...` item. Change nothing else about it.

Immediately after the `Background="Black">` line and before `<DockPanel>`, add the native definition. Headers are copied character for character from the classic menu, mnemonic underscores included:

```xml
  <NativeMenu.Menu>
    <NativeMenu>
      <NativeMenuItem Header="_File">
        <NativeMenuItem.Menu>
          <NativeMenu>
            <NativeMenuItem Header="_New Session..." Click="OnNewSessionClickNative" />
            <NativeMenuItemSeparator />
            <NativeMenuItem Header="_Connect" Command="{Binding ConnectCommand}" />
            <NativeMenuItem Header="_Disconnect" Command="{Binding DisconnectCommand}" />
            <NativeMenuItemSeparator />
            <NativeMenuItem Header="File _Transfer..." Click="OnFileTransferClickNative" IsEnabled="{Binding IsConnected}" />
            <NativeMenuItemSeparator />
            <NativeMenuItem Header="C_lose" Click="OnCloseClickNative" />
          </NativeMenu>
        </NativeMenuItem.Menu>
      </NativeMenuItem>
      <NativeMenuItem Header="_Edit">
        <NativeMenuItem.Menu>
          <NativeMenu>
            <NativeMenuItem Header="_Copy" Click="OnCopyClickNative" />
            <NativeMenuItem Header="_Paste" Click="OnPasteClickNative" />
            <NativeMenuItemSeparator />
            <NativeMenuItem Header="Select _All" Click="OnSelectAllClickNative" />
          </NativeMenu>
        </NativeMenuItem.Menu>
      </NativeMenuItem>
      <NativeMenuItem Header="_Keys">
        <NativeMenuItem.Menu>
          <NativeMenu>
            <NativeMenuItem Header="Clear" Command="{Binding SendKeyCommand}" CommandParameter="{x:Static core:TerminalKey.Clear}" />
            <NativeMenuItem Header="Reset" Command="{Binding SendKeyCommand}" CommandParameter="{x:Static core:TerminalKey.Reset}" />
            <NativeMenuItem Header="Attn" Command="{Binding SendKeyCommand}" CommandParameter="{x:Static core:TerminalKey.Attn}" />
            <NativeMenuItem Header="SysReq" Command="{Binding SendKeyCommand}" CommandParameter="{x:Static core:TerminalKey.SysReq}" />
            <NativeMenuItemSeparator />
            <NativeMenuItem Header="PA1" Command="{Binding SendKeyCommand}" CommandParameter="{x:Static core:TerminalKey.PA1}" />
            <NativeMenuItem Header="PA2" Command="{Binding SendKeyCommand}" CommandParameter="{x:Static core:TerminalKey.PA2}" />
            <NativeMenuItem Header="PA3" Command="{Binding SendKeyCommand}" CommandParameter="{x:Static core:TerminalKey.PA3}" />
            <NativeMenuItemSeparator />
            <NativeMenuItem Header="PF13" Command="{Binding SendKeyCommand}" CommandParameter="{x:Static core:TerminalKey.PF13}" />
            <NativeMenuItem Header="PF14" Command="{Binding SendKeyCommand}" CommandParameter="{x:Static core:TerminalKey.PF14}" />
            <NativeMenuItem Header="PF15" Command="{Binding SendKeyCommand}" CommandParameter="{x:Static core:TerminalKey.PF15}" />
            <NativeMenuItem Header="PF16" Command="{Binding SendKeyCommand}" CommandParameter="{x:Static core:TerminalKey.PF16}" />
            <NativeMenuItem Header="PF17" Command="{Binding SendKeyCommand}" CommandParameter="{x:Static core:TerminalKey.PF17}" />
            <NativeMenuItem Header="PF18" Command="{Binding SendKeyCommand}" CommandParameter="{x:Static core:TerminalKey.PF18}" />
            <NativeMenuItem Header="PF19" Command="{Binding SendKeyCommand}" CommandParameter="{x:Static core:TerminalKey.PF19}" />
            <NativeMenuItem Header="PF20" Command="{Binding SendKeyCommand}" CommandParameter="{x:Static core:TerminalKey.PF20}" />
            <NativeMenuItem Header="PF21" Command="{Binding SendKeyCommand}" CommandParameter="{x:Static core:TerminalKey.PF21}" />
            <NativeMenuItem Header="PF22" Command="{Binding SendKeyCommand}" CommandParameter="{x:Static core:TerminalKey.PF22}" />
            <NativeMenuItem Header="PF23" Command="{Binding SendKeyCommand}" CommandParameter="{x:Static core:TerminalKey.PF23}" />
            <NativeMenuItem Header="PF24" Command="{Binding SendKeyCommand}" CommandParameter="{x:Static core:TerminalKey.PF24}" />
          </NativeMenu>
        </NativeMenuItem.Menu>
      </NativeMenuItem>
      <NativeMenuItem Header="_Help">
        <NativeMenuItem.Menu>
          <NativeMenu>
            <NativeMenuItem Header="_Wire Log" ToggleType="CheckBox" IsChecked="{Binding IsWireLogging, Mode=TwoWay}" />
            <NativeMenuItem Header="Show Wire _Logs..." Command="{Binding ShowWireLogsCommand}" />
            <NativeMenuItemSeparator />
            <NativeMenuItem Header="_About LizTerm..." Click="OnAboutClickNative" />
          </NativeMenu>
        </NativeMenuItem.Menu>
      </NativeMenuItem>
    </NativeMenu>
  </NativeMenu.Menu>
```

Note the Keys menu carries **no** `Gesture` anywhere — see Global Constraints.

Then add the second renderer as the first child inside `<DockPanel>`, immediately above the existing `<Menu ...>`:

```xml
    <NativeMenuBar x:Name="NativeBar" DockPanel.Dock="Top" />
```

- [ ] **Step 5: Wire the strategy and the native handlers**

In `src/LizTerm.App/Views/SessionWindow.axaml.cs`, add `using LizTerm.App.Menus;` and `using Avalonia.Controls;` (the latter is already present). Replace the constructor and add the native handlers:

```csharp
    public SessionWindow() : this(MenuStrategy.UseNativeMenu) { }

    /// <summary>The strategy is a constructor argument rather than a static read, so a test can build a window
    /// in either mode without touching the process environment — the once-only paste test needs both.</summary>
    internal SessionWindow(bool useNativeMenu)
    {
        InitializeComponent();
        // The screen's events call the view model's methods, not its commands: each method carries its own guard,
        // and a keystroke must never be dropped for arriving while the previous one's round trip is still open.
        Screen.KeyRequested += (_, key) => _ = ViewModel?.SendKeyAsync(key);
        Screen.TextEntered += (_, text) => _ = ViewModel?.TypeTextAsync(text);
        Screen.CellClicked += (_, cell) => _ = ViewModel?.MoveCursorAsync(cell.Row, cell.Column);
        Screen.CopyRequested += (_, _) => _ = ViewModel?.CopyAsync();
        Screen.PasteRequested += (_, _) => _ = ViewModel?.PasteAsync();
        Screen.SelectAllRequested += (_, _) => ViewModel?.SelectAll();
        ApplyMenuStrategy(useNativeMenu);
        Opened += (_, _) =>
        {
            ShowPlatformGestures();
            Screen.Focus();
        };
    }

    /// <summary>One definition, two renderers, exactly one of them visible. Hiding the classic menu under the
    /// native strategy is required rather than tidy: measured on macOS, a NativeMenu installed while the classic
    /// Menu was still visible drew both — an in-window bar beneath the system bar.</summary>
    private void ApplyMenuStrategy(bool useNativeMenu)
    {
        ClassicMenu.IsVisible = !useNativeMenu;
        NativeBar.IsVisible = useNativeMenu;

        // macOS puts About in the application menu, so neither renderer's Help item may also carry one. Both get
        // the rule: LIZTERM_MENU=classic on macOS is reachable, and there the classic bar renders in-window while
        // the application menu still supplies its own About.
        var aboutInHelp = MenuStrategy.AboutInHelpMenu(OperatingSystem.IsMacOS());
        AboutMenuItem.IsVisible = aboutInHelp;
        var nativeAbout = MenuLookup.Item(NativeMenu.GetMenu(this), "_Help", "_About LizTerm...");
        if (nativeAbout is not null) nativeAbout.IsVisible = aboutInHelp;
    }

    // MenuItem.Click is EventHandler<RoutedEventArgs> and NativeMenuItem.Click is EventHandler<EventArgs>, so
    // each shared action is two one-line handlers over one method rather than two implementations. All seven
    // live here, in the task that adds the XAML referencing them, so nothing is defined without a caller.
    private void OnCloseClickNative(object? sender, EventArgs e) => Close();

    private void OnNewSessionClickNative(object? sender, EventArgs e) =>
        (Avalonia.Application.Current as App)?.ShowPicker();

    private async void OnFileTransferClickNative(object? sender, EventArgs e) => await ShowFileTransferAsync();

    private async void OnAboutClickNative(object? sender, EventArgs e) => await ShowAboutAsync();

    // Deliberately the view model's methods, never the [RelayCommand]s. Each method carries its own guard; the
    // commands keep CommunityToolkit's default of disabling while running, which is fine for a click and wrong
    // for a keystroke — and on macOS Task 6's gestures make these keystrokes, activated by the OS.
    private void OnCopyClickNative(object? sender, EventArgs e) => _ = ViewModel?.CopyAsync();

    private void OnPasteClickNative(object? sender, EventArgs e) => _ = ViewModel?.PasteAsync();

    private void OnSelectAllClickNative(object? sender, EventArgs e) => ViewModel?.SelectAll();
```

Refactor the two existing handlers to share their bodies rather than duplicating them:

```csharp
    private void OnNewSessionClick(object? sender, RoutedEventArgs e) =>
        (Avalonia.Application.Current as App)?.ShowPicker();

    private async void OnFileTransferClick(object? sender, RoutedEventArgs e) => await ShowFileTransferAsync();

    /// <summary>Opens the File Transfer dialog modally over this window. The dialog's own picker parents the OS
    /// file dialogs; the view model comes from the session view model so the last request is remembered.</summary>
    private async Task ShowFileTransferAsync()
    {
        if (ViewModel is not { IsConnected: true } vm) return;
        try
        {
            var dialog = new FileTransferWindow();
            dialog.DataContext = vm.CreateTransfer(new AvaloniaFilePicker(dialog));
            await dialog.ShowDialog(this);
        }
        catch (Exception ex)
        {
            vm.ErrorMessage = "Could not open the File Transfer dialog: " + ex.Message;
        }
        Screen.Focus();
    }
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~NativeMenuTests"`
Expected: PASS, 7 tests.

Then the whole App project, because Task 5 touched the window every existing test builds:

Run: `dotnet test tests/LizTerm.App.Tests`
Expected: PASS, including the five untouched `FindControl<MenuItem>` assertions in `SessionWindowTests`.

- [ ] **Step 7: Commit**

```bash
git add src/LizTerm.App/Menus/MenuLookup.cs src/LizTerm.App/Views/SessionWindow.axaml src/LizTerm.App/Views/SessionWindow.axaml.cs tests/LizTerm.App.Tests/Views/NativeMenuTests.cs
git commit -m "Add the native window menu beside the classic one, behind MenuStrategy"
```

---

### Task 6: Edit gestures, and proof they fire exactly once

Task 5 added the Edit menu items and their `Click` handlers. This task gives those items their platform gesture, which is the change that makes them reachable from the keyboard — and on macOS hands the keystroke to the OS.

**Files:**
- Modify: `src/LizTerm.App/Views/SessionWindow.axaml.cs`
- Test: `tests/LizTerm.App.Tests/Views/NativeMenuTests.cs`

**Interfaces:**
- Consumes: `MenuLookup.Item`, `internal SessionWindow(bool)`, the four-tuple `Show` test helper, and the `OnCopyClickNative`/`OnPasteClickNative`/`OnSelectAllClickNative` handlers, all from Task 5.
- Produces: nothing later tasks depend on.

- [ ] **Step 1: Write the failing tests**

Append to `tests/LizTerm.App.Tests/Views/NativeMenuTests.cs`:

```csharp
    /// <summary>Gestures come from the platform hotkey table, not a hardcoded modifier, so macOS shows Cmd and
    /// the others Ctrl from the one table ShowPlatformGestures already reads for the classic menu.</summary>
    [AvaloniaFact]
    public void Only_the_edit_menu_carries_gestures()
    {
        var (window, _, _, _) = Show();
        var hotkeys = window.GetPlatformSettings()!.HotkeyConfiguration;

        Assert.Equal(hotkeys.Copy.FirstOrDefault(), Item(window, "_Edit", "_Copy").Gesture);
        Assert.Equal(hotkeys.Paste.FirstOrDefault(), Item(window, "_Edit", "_Paste").Gesture);
        Assert.Equal(hotkeys.SelectAll.FirstOrDefault(), Item(window, "_Edit", "Select _All").Gesture);

        // A gesture here would be a 3270 client that cannot send the key, silently, with nothing in the wire log.
        foreach (var (top, child) in new[]
                 {
                     ("_File", "_Connect"), ("_File", "C_lose"),
                     ("_Keys", "PA1"), ("_Keys", "PF13"), ("_Keys", "Clear"),
                     ("_Help", "_Wire Log"),
                 })
        {
            Assert.Null(Item(window, top, child).Gesture);
        }
    }

    /// <summary>The double-dispatch guard. On macOS the OS takes the keystroke before TerminalScreen exists, so
    /// nothing can fire twice; on Windows and Linux the gesture reaches NativeMenuBar instead, and whether that
    /// dispatches as well as displays cannot be checked from a Mac. A second paste is a real bug, not a cosmetic
    /// one, so it is asserted on every CI push under both strategies rather than reasoned about.</summary>
    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_paste_hotkey_reaches_the_host_exactly_once(bool useNativeMenu)
    {
        var (window, _, session, clipboard) = Show(useNativeMenu);
        clipboard.Text = "claude";
        session.RaiseConnection(ConnectionState.Connected3270);

        window.KeyPressQwerty(PhysicalKey.V, RawInputModifiers.Control);

        Assert.Equal(["paste:claude"], session.Calls);
    }
```

`Show` already returns the clipboard (Task 5), so nothing about the helper changes here. Add `using Avalonia.Headless;` for `KeyPressQwerty`; `using Avalonia.Input;` is already present for the gesture assertions.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~Only_the_edit_menu_carries_gestures"`
Expected: FAIL — the Edit gestures are null, because nothing assigns them yet.

- [ ] **Step 3: Assign the gestures**

In `src/LizTerm.App/Views/SessionWindow.axaml.cs`, extend `ShowPlatformGestures`. The three Edit `Click` handlers already exist from Task 5; this task only gives their items a gesture:

```csharp
    /// <summary>Menu gesture text from the platform table, so macOS shows Cmd and the others show Ctrl.
    /// The native items take a real Gesture rather than display text: on macOS that is an AppKit key
    /// equivalent, dispatched by the OS before the focused screen sees the key. That is safe for exactly these
    /// three, which TerminalScreen already routes away from the host, and is why nothing on File, Keys or Help
    /// carries one.</summary>
    private void ShowPlatformGestures()
    {
        var hotkeys = this.GetPlatformSettings()?.HotkeyConfiguration;
        if (hotkeys is null) return;
        CopyMenuItem.InputGesture = hotkeys.Copy.FirstOrDefault();
        PasteMenuItem.InputGesture = hotkeys.Paste.FirstOrDefault();
        SelectAllMenuItem.InputGesture = hotkeys.SelectAll.FirstOrDefault();

        var menu = NativeMenu.GetMenu(this);
        Gesture(menu, "_Copy", hotkeys.Copy.FirstOrDefault());
        Gesture(menu, "_Paste", hotkeys.Paste.FirstOrDefault());
        Gesture(menu, "Select _All", hotkeys.SelectAll.FirstOrDefault());

        static void Gesture(NativeMenu? menu, string child, KeyGesture? gesture)
        {
            var item = MenuLookup.Item(menu, "_Edit", child);
            if (item is not null) item.Gesture = gesture;
        }
    }

```

Add `using Avalonia.Input;` for `KeyGesture` if it is not already there.

`ShowPlatformGestures` runs from `Opened`, which `window.Show()` raises in the headless tests, so the gestures are assigned by the time any assertion reads them.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~NativeMenuTests"`
Expected: PASS, 10 tests.

If `The_paste_hotkey_reaches_the_host_exactly_once(useNativeMenu: true)` fails with **two** `paste:claude` entries, that is the double dispatch this test exists to catch: `NativeMenuBar` dispatches gestures as well as displaying them, and both it and `TerminalScreen` acted. **Stop and report it rather than weakening the assertion** — the fix is to assign the native Edit gestures only when `OperatingSystem.IsMacOS()`, which changes the design and needs a spec amendment.

- [ ] **Step 5: Commit**

```bash
git add src/LizTerm.App/Views/SessionWindow.axaml.cs tests/LizTerm.App.Tests/Views/NativeMenuTests.cs
git commit -m "Give the Edit menu its platform gestures, wired to the methods not the commands"
```

---

### Task 7: Documentation and the follow-up

**Files:**
- Modify: `README.md`
- Modify: `CLAUDE.md`
- Test: the full suite, plus the zero-warning check

**Interfaces:**
- Consumes: everything above.
- Produces: nothing.

- [ ] **Step 1: Document the environment variable in the README**

In `README.md`, in the "Environment variables" list, add after the `LIZTERM_WIRE_LOG` entry:

```markdown
- `LIZTERM_MENU`: `native` or `classic`, overriding which menu renderer this platform uses. The default is
  native on macOS (a real system menu bar and application menu) and classic in-window elsewhere. Anything else
  falls back to the default.
```

- [ ] **Step 2: Document the architecture in CLAUDE.md**

In `CLAUDE.md`, in the `### App (src/LizTerm.App)` section, add a bullet after the one describing `TerminalScreen`'s key handling:

```markdown
- Menus: one `NativeMenu` definition per window plus an application-level one in `App.axaml` (About, Quit —
  the only thing that gives the picker a menu bar on macOS), rendered by either `NativeMenuBar` or the classic
  in-window `<Menu>`, which is still present. `MenuStrategy` (`Menus/`) picks: `LIZTERM_MENU=native|classic`,
  else native on macOS and classic elsewhere, because `NativeMenuBar`'s in-window rendering has never been
  looked at on Windows or Linux. `MenuStrategy.Decide` and `AboutInHelpMenu` are pure and take the platform as
  an argument, as `EngineRequirement.Decide` does, so every combination is testable anywhere. **No menu item
  outside Edit ever carries a `Gesture`**: measured on macOS, a `NativeMenuItem` gesture is an AppKit key
  equivalent that `NSApplication.sendEvent:` dispatches before the key window's responder chain, so
  `Gesture="F1"` would silently swallow PF1 — `TerminalScreen` never sees the key. Edit's Cmd/Ctrl+C, V and A
  come from `GetPlatformSettings().HotkeyConfiguration` and activate `CopyAsync`/`PasteAsync`/`SelectAll`
  directly, never the `[RelayCommand]`s, which disable while running. `MenuItem.Click` and
  `NativeMenuItem.Click` have different delegate shapes, so each shared action is two one-line handlers over
  one method. `MenuLookup` (`Menus/`) is how both the code-behind and the tests find a `NativeMenuItem`, which
  `FindControl` cannot reach.
```

Also add `LIZTERM_MENU` to the existing "Environment variables:" paragraph in the b3270 section, beside `LIZTERM_WIRE_LOG`.

- [ ] **Step 3: Run the full suite**

Run: `dotnet test LizTerm.slnx`
Expected: every project passes; the live host tests skip themselves.

- [ ] **Step 4: Run the zero-warning check**

Run: `dotnet build LizTerm.slnx --no-incremental 2>&1 | grep -c " warning "`
Expected: `0`. An incremental build hides warnings from projects it does not recompile, so `--no-incremental` is not optional.

- [ ] **Step 5: Verify on the Mac by hand**

Build and run the app (`native/out` may be absent in a worktree, hence the override):

```bash
LIZTERM_B3270_PATH=/opt/homebrew/bin/b3270 dotnet run --project src/LizTerm.App
```

Confirm, in this order: the picker opens with a **LizTerm** application menu carrying About and Quit; About from it opens and reports the engine; Quit ends the app. Then reopen with a profile or `-- localhost:3270` and confirm the session window shows File / Edit / Keys / Help in the **system** menu bar and **no** in-window menu bar; that Help has no About item; that Edit shows ⌘C, ⌘V, ⌘A; and that F1 still reaches the host rather than doing nothing.

This is the step that covers the macOS branch of the About visibility wiring, which CI on Linux cannot reach.

- [ ] **Step 6: Commit**

```bash
git add README.md CLAUDE.md
git commit -m "Document the menu strategy and LIZTERM_MENU"
```

- [ ] **Step 7: File the follow-up issue**

The classic menu exists to be deleted; filed at landing so a staging device does not quietly become a permanent duplicate.

```bash
gh issue create --title "Flip the menu default and delete the classic in-window menu" --label enhancement --body "$(cat <<'EOF'
`MenuStrategy` currently defaults to the native menu on macOS and the classic in-window `<Menu>` on Windows and
Linux, because `NativeMenuBar`'s in-window rendering has never been looked at on either. Both menus are
maintained, and both are covered by tests, which is the intended cost of that staging and not a permanent state.

Two questions have to be answered by running the app with `LIZTERM_MENU=native` on a real Windows machine and a
real Linux one:

1. **Mnemonics.** Headers are `_File`, `_Copy`. Does `NativeMenuBar` render the underscore as an Alt-mnemonic,
   or print it literally? Losing them is a real regression for keyboard users on those platforms.
2. **Appearance.** Is the in-window rendering presentable beside `Menu`'s? The tests cover structure and
   behaviour; they cannot say it looks right.

When both are satisfactory: change `MenuStrategy`'s default, delete `ClassicMenu` and the `NativeMenuBar`
visibility switch, delete the classic-menu tests in `SessionWindowTests` (the five `FindControl<MenuItem>`
sites), retire `LIZTERM_MENU` from `README.md` and `CLAUDE.md`, and drop the `internal SessionWindow(bool)`
seam along with the `[InlineData]` pair on the once-only paste test.

Spec: `docs/superpowers/specs/2026-09-08-lizterm-native-menus-design.md` section 8.
EOF
)"
```

---

## Self-Review

**Spec coverage.** §2.1 → the Global Constraints and Task 6's `Only_the_edit_menu_carries_gestures`. §2.2 → Task 5's binding tests. §3 → Task 1 and Task 5's `ApplyMenuStrategy`. §4.1 → Task 1. §4.2 → Task 4. §4.3 → Tasks 2 and 3. §4.4 → Task 5. §4.5 → Task 6. §4.6 → Task 5's About visibility, both renderers. §4.7 → the File Structure's "deliberately not modified". §5 → Task 2 (not-found), Task 3 (banner), Task 4 (swallowed), Task 1 (unrecognised value). §6 → Tasks 1, 5, 6. §8 → Task 7's issue.

**Type consistency.** `MenuStrategy.Decide(string?, bool)` and `AboutInHelpMenu(bool)` are used with those signatures in Tasks 1, 5 and 6. `MenuLookup.Item(NativeMenu?, string, string)` is defined in Task 5 and used in Tasks 5 and 6. `SessionFactory.CheckBackendOrUnknown()` is defined in Task 2 and consumed in Task 3. `App.ShowAboutAsync(Window?)` is defined in Task 3 and consumed in Tasks 3 and 4. `internal SessionWindow(bool)` is defined in Task 5 and consumed in Tasks 5 and 6. The native handler names in Task 5's XAML — `OnNewSessionClickNative`, `OnFileTransferClickNative`, `OnCloseClickNative`, `OnAboutClickNative`, `OnCopyClickNative`, `OnPasteClickNative`, `OnSelectAllClickNative` — are each defined once, in Task 3 (About) or Tasks 5 and 6.

**One deliberate stop condition.** `MenuItem` and `NativeMenuItem` share one `MenuItemToggleType` enum (`Avalonia.Controls.MenuItemToggleType`, confirmed in the 12.1.2 reference docs and exercised by the spike), so Task 5's wire-log assertion needs no hedge. The only place this plan tells the implementer to halt rather than adapt is Task 6 Step 4: a doubled paste means `NativeMenuBar` dispatches gestures, which changes the design and needs a spec amendment, and weakening the assertion to get past it would ship the bug it exists to catch.
