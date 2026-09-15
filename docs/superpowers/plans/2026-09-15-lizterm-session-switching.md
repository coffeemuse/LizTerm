# LizTerm Session Switching Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let a user with many LizTerm sessions reach the right one fast: a Cmd/Ctrl+K switcher, a Window menu, a
macOS Dock menu and a per-window Keep on Top (#46).

**Architecture:** One `SessionList` owned by `App` records open sessions in opening order and use order and raises
`Changed`. Everything that shows sessions — the switcher overlay in each `SessionWindow`, the generated rows of the
Window menu, and the Dock menu — reads that list and reaches a session only through `ISessionHost.Bring()`, which a
future tabbed window can implement (#119). Pure rules (`SessionMenuLabel`, `SwitcherFilter`,
`SessionSwitcherViewModel`) carry the behaviour and are tested with plain `[Fact]`s.

**Tech Stack:** .NET 10, Avalonia 12.1.2, CommunityToolkit.Mvvm, xunit.v3 (VSTest mode), Avalonia.Headless.XUnit.

**Spec:** [`docs/superpowers/specs/2026-09-15-lizterm-session-switching-design.md`](../specs/2026-09-15-lizterm-session-switching-design.md)

## Global Constraints

- Every new hand-written `.cs` and `.axaml` file starts with the three-line licence header: `// This file is part
  of LizTerm.`, `// Copyright 2026 by CoffeeMuse`, `// SPDX-License-Identifier: BSD-3-Clause` (`.axaml` uses the
  XML comment form, before the root element, as `src/LizTerm.App/Views/SessionWindow.axaml` does).
  `RepositoryHeadersTests` fails the suite for a missing one.
- `LizTerm.Core` does not change. Nothing in this plan names b3270 or the backend; `SessionFactory.cs` stays the
  App's only mention of it.
- No package references and no `.csproj` edits: new files are picked up by the SDK globs, and `LizTerm.App.csproj`
  already has `<InternalsVisibleTo Include="LizTerm.App.Tests" />`.
- Menu headers use three dots (`Switch Session...`), never the `…` character, like every existing header.
- Every native menu item gets a `Command` or a `Click` handler (the macOS exporter greys out anything else). Never
  replace a `NativeMenu` instance a window was given, and remove items from the end one at a time, never `Clear()`
  (#60; see "Menus" in `src/LizTerm.App/CLAUDE.md`).
- Nothing typed while the switcher is open may reach the host. Headless tests prove it with
  `FakeEmulatorSession.Calls` staying empty.
- Colour never carries meaning alone: the connection mark is a shape (`●`/`○`) and the flags are words.
- Anything touching a `Window` or `Control` is `[AvaloniaFact]`/`[AvaloniaTheory]`; pure rules are `[Fact]`. Drive
  native menu items with `((INativeMenuItemExporterEventsImplBridge)item).RaiseClicked()`.
- Run a single test class with `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~<ClassName>"`.
  Every task ends with the whole App test project green: `dotnet test tests/LizTerm.App.Tests`.
- Final gate (Task 10): `dotnet test LizTerm.slnx` green and
  `dotnet build LizTerm.slnx --no-incremental 2>&1 | grep -c " warning "` printing `0`.
- Commit messages are a plain imperative sentence (the repository's style, e.g. "Add a preference to skip the splash
  screen on launch (#108)"), ending with the line `Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>`.

## File Map

Create:

| File | Responsibility |
|---|---|
| `src/LizTerm.App/Sessions/ISessionHost.cs` | What shows a session: `Bring()`, `IsMinimized`, `KeepOnTop`, `KeepOnTopChanged` |
| `src/LizTerm.App/Sessions/SessionEntry.cs` | One open session: view model, summary row, saved or not, host |
| `src/LizTerm.App/Sessions/SessionList.cs` | Opening order, use order, `Changed`, `BringAllToFront` |
| `src/LizTerm.App/Sessions/SessionMenuLabel.cs` | One session as one line of menu text |
| `src/LizTerm.App/ViewModels/SwitcherFilter.cs` | The filter rule: name, host, tags, note |
| `src/LizTerm.App/ViewModels/SwitcherRow.cs` | One switcher row, ready to draw |
| `src/LizTerm.App/ViewModels/SessionSwitcherViewModel.cs` | The switcher's state and keys, no controls |
| `src/LizTerm.App/Controls/SessionSwitcher.axaml` + `.axaml.cs` | The overlay: dim, palette, filter box, rows |
| `src/LizTerm.App/Menus/SessionMenuItems.cs` | Builds a session's native and classic menu items |
| `src/LizTerm.App/Menus/DockMenu.cs` | Builds and attaches the macOS Dock menu |
| `tests/LizTerm.App.Tests/Fakes/FakeSessionHost.cs` | A host that records `Bring()` |
| `tests/LizTerm.App.Tests/Fakes/TestSessions.cs` | Builds a `SessionEntry` over fakes |
| `tests/LizTerm.App.Tests/Sessions/SessionListTests.cs` | |
| `tests/LizTerm.App.Tests/Sessions/SessionMenuLabelTests.cs` | |
| `tests/LizTerm.App.Tests/ViewModels/SessionSwitcherViewModelTests.cs` | Includes the filter rule |
| `tests/LizTerm.App.Tests/Views/SessionSwitcherTests.cs` | The overlay in a real headless window |
| `tests/LizTerm.App.Tests/Views/WindowMenuTests.cs` | The Window menu in a real headless window |
| `tests/LizTerm.App.Tests/Menus/DockMenuTests.cs` | |

Modify: `src/LizTerm.App/ViewModels/ProfileRow.cs`, `src/LizTerm.App/App.axaml`,
`src/LizTerm.App/Views/ProfilePickerWindow.axaml`, `src/LizTerm.App/Controls/TerminalScreen.cs`,
`src/LizTerm.App/Views/SessionWindow.axaml` + `.axaml.cs`, `src/LizTerm.App/App.axaml.cs`,
`tests/LizTerm.App.Tests/ViewModels/ProfileRowTests.cs`, `tests/LizTerm.App.Tests/Controls/TerminalScreenInputTests.cs`,
`tests/LizTerm.App.Tests/Views/NativeMenuTests.cs`, `docs/user-guide.md`, `CHANGELOG.md`, `README.md`,
`src/LizTerm.App/CLAUDE.md`, `tests/CLAUDE.md`.

---

## Task 1: `ProfileRow` gains a second line, and the Sessions list's row becomes a shared template

**Files:**
- Modify: `src/LizTerm.App/ViewModels/ProfileRow.cs`
- Modify: `src/LizTerm.App/App.axaml`
- Modify: `src/LizTerm.App/Views/ProfilePickerWindow.axaml:69-99`
- Test: `tests/LizTerm.App.Tests/ViewModels/ProfileRowTests.cs`

**Interfaces:**
- Produces: `ProfileRow(SessionProfile profile, TagRegistry registry, bool isSaved = true)`;
  `ProfileRow.SecondLine` (`string`); `ProfileRow.QuickConnectLine` (`const string`, `"Quick Connect"`);
  an `App.axaml` resource `ProfileSummaryTemplate` (`DataTemplate`, `x:DataType="vm:ProfileRow"`).

- [ ] **Step 1: Write the failing tests**

Append inside `ProfileRowTests` (after `A_full_profile_cannot_be_marked_but_a_full_starred_one_can_still_be_unmarked`):

```csharp
    /// <summary>The row's second line (session switching spec §5.3): a saved profile's host and port.</summary>
    [Fact]
    public void A_saved_profile_shows_its_host_and_port_on_the_second_line()
    {
        var row = new ProfileRow(new SessionProfile { Name = "TSO", Host = "tk5.local", Port = 3270 }, TagRegistry.Empty);

        Assert.Equal("tk5.local:3270", row.SecondLine);
    }

    /// <summary>An ad hoc session is named host:port already, so its second line says what it is instead of
    /// repeating the host. HostPort is unchanged for its other readers.</summary>
    [Fact]
    public void An_unsaved_session_says_quick_connect_on_the_second_line()
    {
        var row = new ProfileRow(
            new SessionProfile { Name = "sdf.example:3270", Host = "sdf.example", Port = 3270 },
            TagRegistry.Empty, isSaved: false);

        Assert.Equal(ProfileRow.QuickConnectLine, row.SecondLine);
        Assert.Equal("Quick Connect", ProfileRow.QuickConnectLine);
        Assert.Equal("sdf.example:3270", row.HostPort);
    }
```

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~ProfileRowTests"`
Expected: build FAILS — `ProfileRow` has no `SecondLine`, no `QuickConnectLine`, and no `isSaved` parameter.

- [ ] **Step 3: Implement the second line**

In `src/LizTerm.App/ViewModels/ProfileRow.cs`, replace the constructor's signature line and add the new members.
The constructor becomes:

```csharp
    /// <param name="isSaved">False for an ad hoc session (Quick Connect or a host on the command line), whose
    /// second line says so rather than repeating the host its name already carries.</param>
    public ProfileRow(SessionProfile profile, TagRegistry registry, bool isSaved = true)
    {
        Profile = profile;
        Name = profile.Name;
        HostPort = $"{profile.Host}:{profile.Port}";
        SecondLine = isSaved ? HostPort : QuickConnectLine;
        Note = string.IsNullOrWhiteSpace(profile.Note) ? null : profile.Note.Trim();
        IsFavorite = profile.Tags.Contains(TagRegistry.FavoriteName);

        var all = TagChip.For(profile.Tags, registry);
        Chips = [.. all.Take(MaxChips)];

        var hidden = all.Skip(MaxChips).ToList();
        OverflowText = hidden.Count > 0 ? $"+{hidden.Count}" : null;
        OverflowTip = hidden.Count > 0 ? string.Join(", ", hidden.Select(chip => chip.Text)) : null;
    }

    /// <summary>What an unsaved session's second line reads.</summary>
    public const string QuickConnectLine = "Quick Connect";
```

and directly after `public string HostPort { get; }` add:

```csharp
    /// <summary>The row's second line, which ProfileSummaryTemplate draws: HostPort for a saved profile,
    /// QuickConnectLine for an ad hoc session (session switching spec §5.3).</summary>
    public string SecondLine { get; }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~ProfileRowTests"`
Expected: PASS, all `ProfileRowTests`.

- [ ] **Step 5: Move the row template into `App.axaml`**

In `src/LizTerm.App/App.axaml`, inside `<Application.Resources>` directly after the `TagChipStrip`
`ItemsPanelTemplate`, add (this is the picker's current template, moved, with the host line bound to
`SecondLine`):

```xml
    <!-- One profile's summary, drawn once: the Sessions list's rows and the session switcher's rows (#46) both use
         it, for the reason TagChipTemplate is shared. A 15px gutter on all three lines, so every star lands on the
         same x and a column of them can be scanned. Right-aligned among the chips it would slide with the chip
         count, which is most of what a favourite marker is for. -->
    <DataTemplate x:Key="ProfileSummaryTemplate" x:DataType="vm:ProfileRow">
      <Grid ColumnDefinitions="15,*" RowDefinitions="Auto,Auto,Auto">
        <TextBlock x:Name="StarGlyph" Grid.Row="0" Grid.Column="0" Text="★" Foreground="{x:Static render:TagPalette.Star}"
                   FontSize="13" VerticalAlignment="Center" IsVisible="{Binding IsFavorite}" />
        <!-- The name is what ellipsises; the chips never shrink. A truncated PRO… carries nothing, where a
             truncated long name is still recognisable. -->
        <DockPanel Grid.Row="0" Grid.Column="1" LastChildFill="True">
          <StackPanel DockPanel.Dock="Right" Orientation="Horizontal" Spacing="3" VerticalAlignment="Center">
            <ItemsControl ItemsSource="{Binding Chips}" ItemsPanel="{StaticResource TagChipStrip}"
                          ItemTemplate="{StaticResource TagChipTemplate}" />
            <Border Background="{x:Static render:TagPalette.Overflow}" CornerRadius="3" Padding="5,1"
                    IsVisible="{Binding HasOverflow}">
              <TextBlock x:Name="OverflowChip" Text="{Binding OverflowText}" FontSize="10" FontWeight="Bold"
                         Foreground="{x:Static render:TagPalette.ChipText}" ToolTip.Tip="{Binding OverflowTip}" />
            </Border>
          </StackPanel>
          <TextBlock Text="{Binding Name}" FontWeight="SemiBold" TextTrimming="CharacterEllipsis"
                     VerticalAlignment="Center" Margin="0,0,6,0" />
        </DockPanel>
        <TextBlock Grid.Row="1" Grid.Column="1" Text="{Binding SecondLine}" Foreground="#A0A0A0" FontSize="12" />
        <!-- Only when there is one, so an un-noted profile stays two lines and the list stays long. -->
        <TextBlock x:Name="NoteLine" Grid.Row="2" Grid.Column="1" Text="{Binding Note}" Foreground="#8F8F8F"
                   FontSize="12" FontStyle="Italic" TextTrimming="CharacterEllipsis"
                   IsVisible="{Binding HasNote}" ToolTip.Tip="{Binding Note}" />
      </Grid>
    </DataTemplate>
```

In `src/LizTerm.App/Views/ProfilePickerWindow.axaml`:
1. On the `<ListBox Grid.Row="2" ...` start tag, add the attribute
   `ItemTemplate="{StaticResource ProfileSummaryTemplate}"`.
2. Delete the whole `<ListBox.ItemTemplate>` element (from `<ListBox.ItemTemplate>` through its closing
   `</ListBox.ItemTemplate>`, currently lines 69–99), leaving `<ListBox.Styles>` and `</ListBox>` in place.

- [ ] **Step 6: Run the picker's tests to prove the move changed nothing**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~ProfilePickerWindowTests"`
Expected: PASS, unchanged. They find `StarGlyph`, `NoteLine` and `OverflowChip` inside each row by name.

- [ ] **Step 7: Run the App tests and commit**

Run: `dotnet test tests/LizTerm.App.Tests`
Expected: PASS.

```bash
git add src/LizTerm.App/ViewModels/ProfileRow.cs src/LizTerm.App/App.axaml src/LizTerm.App/Views/ProfilePickerWindow.axaml tests/LizTerm.App.Tests/ViewModels/ProfileRowTests.cs
git commit -m "Share the Sessions list's row template, with a second line for unsaved sessions" -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Task 2: `SessionList`, the one record of open sessions

**Files:**
- Create: `src/LizTerm.App/Sessions/ISessionHost.cs`
- Create: `src/LizTerm.App/Sessions/SessionEntry.cs`
- Create: `src/LizTerm.App/Sessions/SessionList.cs`
- Create: `tests/LizTerm.App.Tests/Fakes/FakeSessionHost.cs`
- Create: `tests/LizTerm.App.Tests/Fakes/TestSessions.cs`
- Test: `tests/LizTerm.App.Tests/Sessions/SessionListTests.cs`

**Interfaces:**
- Consumes: `ProfileRow(SessionProfile, TagRegistry, bool isSaved)` (Task 1); `SessionViewModel.Connection`.
- Produces:
  - `interface ISessionHost { void Bring(); bool IsMinimized { get; } bool KeepOnTop { get; } event EventHandler? KeepOnTopChanged; }`
  - `sealed record SessionEntry(SessionViewModel Session, ProfileRow Summary, bool IsSaved, ISessionHost Host)`
  - `sealed class SessionList`: `IReadOnlyList<SessionEntry> Entries`, `int Count`, `SessionEntry? Current`,
    `SessionEntry? Previous`, `void Add(SessionEntry)`, `void Remove(SessionEntry)`, `void Activated(SessionEntry)`,
    `int? PositionOf(SessionEntry)`, `void BringAllToFront()`, `event EventHandler? Changed`.
  - Tests: `FakeSessionHost` (`BringCount`, `OnBring`, settable `IsMinimized` and `KeepOnTop`);
    `TestSessions.Create(string name, string host = "host.local", ConnectionState state = ConnectionState.Connected3270, bool isSaved = true, string? note = null, string[]? tags = null)`
    returning `(SessionEntry Entry, FakeEmulatorSession Session, FakeSessionHost Host)`.

- [ ] **Step 1: Write the test fakes**

`tests/LizTerm.App.Tests/Fakes/FakeSessionHost.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.App.Sessions;

namespace LizTerm.App.Tests.Fakes;

/// <summary>A host that records Bring() and lets a test simulate what bringing a real window causes, through
/// OnBring (typically calling SessionList.Activated, as a window's Activated event would).</summary>
public sealed class FakeSessionHost : ISessionHost
{
    private bool _keepOnTop;

    public int BringCount { get; private set; }
    public Action? OnBring { get; set; }
    public bool IsMinimized { get; set; }

    public bool KeepOnTop
    {
        get => _keepOnTop;
        set
        {
            if (_keepOnTop == value) return;
            _keepOnTop = value;
            KeepOnTopChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? KeepOnTopChanged;

    public void Bring()
    {
        BringCount++;
        OnBring?.Invoke();
    }
}
```

`tests/LizTerm.App.Tests/Fakes/TestSessions.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.App.Sessions;
using LizTerm.App.ViewModels;
using LizTerm.Core.Profiles;
using LizTerm.Core.Session;

namespace LizTerm.App.Tests.Fakes;

/// <summary>A SessionEntry over a FakeEmulatorSession and a FakeSessionHost, for everything that reads the
/// session list without a real window.</summary>
public static class TestSessions
{
    public static (SessionEntry Entry, FakeEmulatorSession Session, FakeSessionHost Host) Create(
        string name, string host = "host.local", ConnectionState state = ConnectionState.Connected3270,
        bool isSaved = true, string? note = null, string[]? tags = null)
    {
        var session = new FakeEmulatorSession
        {
            Profile = new SessionProfile { Name = name, Host = host, Port = 3270, Note = note, Tags = TagSet.From(tags) },
            ConnectionState = state,
        };
        var viewModel = new SessionViewModel(session, action => action(), new FakeTextClipboard());
        var sessionHost = new FakeSessionHost();
        var entry = new SessionEntry(viewModel, new ProfileRow(session.Profile, TagRegistry.Empty, isSaved), isSaved, sessionHost);
        return (entry, session, sessionHost);
    }
}
```

- [ ] **Step 2: Write the failing tests**

`tests/LizTerm.App.Tests/Sessions/SessionListTests.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.App.Sessions;
using LizTerm.App.Tests.Fakes;
using LizTerm.Core.Session;

namespace LizTerm.App.Tests.Sessions;

public class SessionListTests
{
    [Fact]
    public void Positions_follow_opening_order_and_closing_moves_later_sessions_up()
    {
        var list = new SessionList();
        var (a, _, _) = TestSessions.Create("A");
        var (b, _, _) = TestSessions.Create("B");
        var (c, _, _) = TestSessions.Create("C");
        list.Add(a);
        list.Add(b);
        list.Add(c);

        Assert.Equal([a, b, c], list.Entries);
        Assert.Equal(2, list.PositionOf(b));

        list.Remove(a);

        Assert.Equal(2, list.Count);
        Assert.Equal(1, list.PositionOf(b));
        Assert.Equal(2, list.PositionOf(c));
        Assert.Null(list.PositionOf(a));
    }

    [Fact]
    public void The_tenth_session_is_position_ten_and_the_eleventh_has_none()
    {
        var list = new SessionList();
        var entries = Enumerable.Range(1, 11).Select(i => TestSessions.Create($"S{i}").Entry).ToList();
        entries.ForEach(list.Add);

        Assert.Equal(10, list.PositionOf(entries[9]));
        Assert.Null(list.PositionOf(entries[10]));
    }

    /// <summary>A session never activated is the least recent, so a new window does not become Previous before
    /// anyone has used it.</summary>
    [Fact]
    public void Current_and_previous_follow_activation()
    {
        var list = new SessionList();
        var (a, _, _) = TestSessions.Create("A");
        var (b, _, _) = TestSessions.Create("B");
        var (c, _, _) = TestSessions.Create("C");
        list.Add(a);
        Assert.Same(a, list.Current);
        Assert.Null(list.Previous);

        list.Add(b);
        list.Add(c);
        list.Activated(c);
        list.Activated(b);

        Assert.Same(b, list.Current);
        Assert.Same(c, list.Previous);

        list.Remove(b);

        Assert.Same(c, list.Current);
        Assert.Same(a, list.Previous);
    }

    [Fact]
    public void Changed_fires_on_add_remove_activation_connection_and_keep_on_top()
    {
        var list = new SessionList();
        var (a, _, _) = TestSessions.Create("A");
        var (b, bSession, bHost) = TestSessions.Create("B");
        var changes = 0;
        list.Changed += (_, _) => changes++;

        list.Add(a);
        list.Add(b);
        Assert.Equal(2, changes);

        list.Activated(b);
        Assert.Equal(3, changes);
        list.Activated(b); // already Current: nothing changed
        Assert.Equal(3, changes);

        bSession.RaiseConnection(ConnectionState.Disconnected);
        Assert.Equal(4, changes);

        bHost.KeepOnTop = true;
        Assert.Equal(5, changes);

        list.Remove(b);
        Assert.Equal(6, changes);
    }

    [Fact]
    public void A_removed_session_raises_nothing_more()
    {
        var list = new SessionList();
        var (a, aSession, aHost) = TestSessions.Create("A");
        list.Add(a);
        list.Remove(a);
        var changes = 0;
        list.Changed += (_, _) => changes++;

        aSession.RaiseConnection(ConnectionState.Disconnected);
        aHost.KeepOnTop = true;
        list.Activated(a);
        list.Remove(a);

        Assert.Equal(0, changes);
    }

    [Fact]
    public void Adding_the_same_session_twice_throws()
    {
        var list = new SessionList();
        var (a, _, _) = TestSessions.Create("A");
        list.Add(a);

        Assert.Throws<InvalidOperationException>(() => list.Add(a));
    }

    /// <summary>Bringing each window activates it, so the hosts here call Activated exactly as a real window's
    /// Activated event would. Reverse use order replays the same use order: Current ends in front and Previous is
    /// unchanged. The minimised session (here the least recent, so no order it held is disturbed) is skipped.</summary>
    [Fact]
    public void Bring_all_to_front_raises_in_reverse_use_order_skipping_minimised_and_keeps_previous()
    {
        var list = new SessionList();
        var (a, _, aHost) = TestSessions.Create("A");
        var (b, _, bHost) = TestSessions.Create("B");
        var (c, _, cHost) = TestSessions.Create("C");
        var (d, _, dHost) = TestSessions.Create("D");
        var order = new List<string>();
        foreach (var (entry, host) in new[] { (a, aHost), (b, bHost), (c, cHost), (d, dHost) })
        {
            list.Add(entry);
            host.OnBring = () =>
            {
                order.Add(entry.Session.Profile.Name);
                list.Activated(entry);
            };
        }
        list.Activated(d);
        list.Activated(c);
        list.Activated(b); // use order: B, C, D, A
        aHost.IsMinimized = true;

        list.BringAllToFront();

        Assert.Equal(["D", "C", "B"], order);
        Assert.Same(b, list.Current);
        Assert.Same(c, list.Previous);
        Assert.Equal(0, aHost.BringCount);
    }
}
```

- [ ] **Step 3: Run them to verify they fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~SessionListTests"`
Expected: build FAILS — the namespace `LizTerm.App.Sessions` does not exist.

- [ ] **Step 4: Implement the three types**

`src/LizTerm.App/Sessions/ISessionHost.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

namespace LizTerm.App.Sessions;

/// <summary>Whatever shows a session: today a SessionWindow, later possibly a tab in a tabbed window (#119). The
/// switcher, the Window menu and the Dock menu reach a session only through this, which is what lets a tab host
/// plug in without any of them changing (session switching spec §3).</summary>
public interface ISessionHost
{
    /// <summary>Restore if minimised, then activate. A tab host would select the tab first.</summary>
    void Bring();

    bool IsMinimized { get; }

    bool KeepOnTop { get; }

    event EventHandler? KeepOnTopChanged;
}
```

`src/LizTerm.App/Sessions/SessionEntry.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.App.ViewModels;

namespace LizTerm.App.Sessions;

/// <summary>One open session. Summary is the Sessions list's row for its profile, built once when the session
/// opens from the same tag colours the window got, so the switcher draws a row exactly as the list does.</summary>
public sealed record SessionEntry(SessionViewModel Session, ProfileRow Summary, bool IsSaved, ISessionHost Host);
```

`src/LizTerm.App/Sessions/SessionList.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.ComponentModel;
using LizTerm.App.ViewModels;

namespace LizTerm.App.Sessions;

/// <summary>The process's one record of open sessions (session switching spec §3). Two orders: Entries is opening
/// order, which gives the numbers 1-10; a private use order gives Current and Previous. Everything runs on the UI
/// thread: SessionViewModel marshals its backend events there, and hosts raise KeepOnTopChanged from UI code.</summary>
public sealed class SessionList
{
    /// <summary>Positions past this have no number.</summary>
    public const int NumberedSessions = 10;

    private readonly List<SessionEntry> _opened = [];
    private readonly List<SessionEntry> _used = [];
    private readonly Dictionary<SessionEntry, (PropertyChangedEventHandler Connection, EventHandler KeepOnTop)> _handlers = [];

    public IReadOnlyList<SessionEntry> Entries => _opened;

    public int Count => _opened.Count;

    /// <summary>The most recently activated session, or the first opened while none has been activated.</summary>
    public SessionEntry? Current => _used.Count > 0 ? _used[0] : null;

    public SessionEntry? Previous => _used.Count > 1 ? _used[1] : null;

    /// <summary>Raised after Add, Remove, an Activated that changes Current, a Connection change on any listed
    /// session, and any host's KeepOnTopChanged.</summary>
    public event EventHandler? Changed;

    public void Add(SessionEntry entry)
    {
        if (_opened.Contains(entry)) throw new InvalidOperationException("That session is already listed.");
        _opened.Add(entry);
        // At the end of use order: a session never activated is the least recent.
        _used.Add(entry);
        PropertyChangedEventHandler onConnection = (_, e) =>
        {
            if (e.PropertyName == nameof(SessionViewModel.Connection)) RaiseChanged();
        };
        EventHandler onKeepOnTop = (_, _) => RaiseChanged();
        entry.Session.PropertyChanged += onConnection;
        entry.Host.KeepOnTopChanged += onKeepOnTop;
        _handlers[entry] = (onConnection, onKeepOnTop);
        RaiseChanged();
    }

    public void Remove(SessionEntry entry)
    {
        if (!_opened.Remove(entry)) return;
        _used.Remove(entry);
        var (onConnection, onKeepOnTop) = _handlers[entry];
        _handlers.Remove(entry);
        entry.Session.PropertyChanged -= onConnection;
        entry.Host.KeepOnTopChanged -= onKeepOnTop;
        RaiseChanged();
    }

    public void Activated(SessionEntry entry)
    {
        if (!_used.Contains(entry) || ReferenceEquals(Current, entry)) return;
        _used.Remove(entry);
        _used.Insert(0, entry);
        RaiseChanged();
    }

    /// <summary>1 to 10 in opening order, or null past ten or for a session not listed.</summary>
    public int? PositionOf(SessionEntry entry)
    {
        var index = _opened.IndexOf(entry);
        return index is >= 0 and < NumberedSessions ? index + 1 : null;
    }

    /// <summary>Brings every session that is not minimised, least recently used first, so Current is raised last
    /// and ends in front. Each Bring activates its window, and activating in reverse use order replays the same use
    /// order, so Previous is unchanged and nothing needs suspending. The list is copied first because each Bring
    /// changes use order as it goes.</summary>
    public void BringAllToFront()
    {
        var raise = _used.Where(entry => !entry.Host.IsMinimized).Reverse().ToList();
        foreach (var entry in raise) entry.Host.Bring();
    }

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~SessionListTests"`
Expected: PASS, 7 tests.

- [ ] **Step 6: Run the App tests and commit**

Run: `dotnet test tests/LizTerm.App.Tests`
Expected: PASS.

```bash
git add src/LizTerm.App/Sessions tests/LizTerm.App.Tests/Fakes/FakeSessionHost.cs tests/LizTerm.App.Tests/Fakes/TestSessions.cs tests/LizTerm.App.Tests/Sessions/SessionListTests.cs
git commit -m "Add SessionList, the one record of open sessions (#46)" -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Task 3: `SessionMenuLabel`, one session as one line of menu text

**Files:**
- Create: `src/LizTerm.App/Sessions/SessionMenuLabel.cs`
- Test: `tests/LizTerm.App.Tests/Sessions/SessionMenuLabelTests.cs`

**Interfaces:**
- Consumes: `SessionEntry` (Task 2); `ProfileRow.IsFavorite`; `SessionViewModel.Connection`.
- Produces: `static string SessionMenuLabel.For(SessionEntry entry, int? position)`.

- [ ] **Step 1: Write the failing tests**

`tests/LizTerm.App.Tests/Sessions/SessionMenuLabelTests.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.App.Sessions;
using LizTerm.App.Tests.Fakes;
using LizTerm.Core.Session;

namespace LizTerm.App.Tests.Sessions;

/// <summary>Session switching spec §4's table, row for row.</summary>
public class SessionMenuLabelTests
{
    [Fact]
    public void A_connected_favourite_gets_its_access_key_a_star_and_its_host()
    {
        var (entry, _, _) = TestSessions.Create("TSO", "tk5.local", tags: ["FAVORITE"]);

        Assert.Equal("_3  ★ TSO - tk5.local", SessionMenuLabel.For(entry, 3));
    }

    [Fact]
    public void A_disconnected_session_says_so()
    {
        var (entry, _, _) = TestSessions.Create("CICS", "zxplore.example", ConnectionState.Disconnected);

        Assert.Equal("_4  CICS - zxplore.example (Disconnected)", SessionMenuLabel.For(entry, 4));
    }

    /// <summary>Position ten prints as 0, and a name that already carries its host does not repeat it — the
    /// window title's "sdf.example:3270 - sdf.example" is not copied.</summary>
    [Fact]
    public void The_tenth_session_is_zero_and_an_ad_hoc_name_does_not_repeat_its_host()
    {
        var (entry, _, _) = TestSessions.Create("sdf.example:3270", "sdf.example", isSaved: false);

        Assert.Equal("_0  sdf.example:3270", SessionMenuLabel.For(entry, 10));
    }

    [Fact]
    public void An_lu_at_host_name_does_not_repeat_its_host_either()
    {
        var (entry, _, _) = TestSessions.Create("CONS01@mvs.local:3270", "MVS.LOCAL", isSaved: false);

        Assert.Equal("_1  CONS01@mvs.local:3270", SessionMenuLabel.For(entry, 1));
    }

    /// <summary>Past ten there is no number and no leading spaces, and an underscore in the name is doubled so it
    /// cannot become an access key.</summary>
    [Fact]
    public void Past_ten_there_is_no_number_and_underscores_are_doubled()
    {
        var (entry, _, _) = TestSessions.Create("MVS_PROD", "mvs.local");

        Assert.Equal("MVS__PROD - mvs.local", SessionMenuLabel.For(entry, null));
    }
}
```

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~SessionMenuLabelTests"`
Expected: build FAILS — `SessionMenuLabel` does not exist.

- [ ] **Step 3: Implement it**

`src/LizTerm.App/Sessions/SessionMenuLabel.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Text;
using LizTerm.Core.Session;

namespace LizTerm.App.Sessions;

/// <summary>One session as one line of menu text, for the Window menu and the Dock menu alike (session switching
/// spec §4): <c>[_position][two spaces][★ ][name][ - host][ (Disconnected)]</c>. A menu cannot draw chips or
/// colour, so this is plain text.</summary>
public static class SessionMenuLabel
{
    public static string For(SessionEntry entry, int? position)
    {
        var profile = entry.Session.Profile;
        var label = new StringBuilder();
        // The underscore makes the digit the in-window menu's access key: Alt, W, 3 reaches session 3.
        if (position is { } number) label.Append('_').Append(number % 10).Append("  ");
        if (entry.Summary.IsFavorite) label.Append("★ ");
        label.Append(Escape(profile.Name));
        // An ad hoc session is named host:port or LU@host:port (StartupArguments.Resolve) and carries its host.
        if (!profile.Name.Contains(profile.Host, StringComparison.OrdinalIgnoreCase))
            label.Append(" - ").Append(Escape(profile.Host));
        if (entry.Session.Connection == ConnectionState.Disconnected) label.Append(" (Disconnected)");
        return label.ToString();
    }

    /// <summary>A doubled underscore is a literal one in both renderers' headers.</summary>
    private static string Escape(string text) => text.Replace("_", "__");
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~SessionMenuLabelTests"`
Expected: PASS, 5 tests.

- [ ] **Step 5: Commit**

```bash
git add src/LizTerm.App/Sessions/SessionMenuLabel.cs tests/LizTerm.App.Tests/Sessions/SessionMenuLabelTests.cs
git commit -m "Add the menu label for an open session" -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Task 4: The switcher's view model, rows and filter

**Files:**
- Create: `src/LizTerm.App/ViewModels/SwitcherFilter.cs`
- Create: `src/LizTerm.App/ViewModels/SwitcherRow.cs`
- Create: `src/LizTerm.App/ViewModels/SessionSwitcherViewModel.cs`
- Test: `tests/LizTerm.App.Tests/ViewModels/SessionSwitcherViewModelTests.cs`

**Interfaces:**
- Consumes: `SessionList`, `SessionEntry` (Task 2); `ProfileRow.Name`, `.HostPort`, `.Note` (Task 1).
- Produces:
  - `static bool SwitcherFilter.Matches(SessionEntry entry, string term)`
  - `sealed partial class SwitcherRow : ObservableObject` — ctor `(SessionEntry entry, int? position, bool numberActive, bool isThisWindow)`;
    `Entry`, `Summary` (`ProfileRow`), `Number` (`string?`), `HasNumber`, `NumberActive`, `IsDisconnected`,
    `IsThisWindow`, `IsOnTop`, `ConnectionMark` (`"●"`/`"○"`), `Flag` (`string?`), observable `IsSelected`.
    (The spec's §5.4 calls `IsThisWindow` "IsCurrent"; renamed so it cannot be confused with `SessionList.Current`.)
  - `sealed partial class SessionSwitcherViewModel : ObservableObject` — ctor `(SessionList sessions, SessionEntry own)`;
    observable `IsOpen`, `Term`, `Rows` (`IReadOnlyList<SwitcherRow>`), `Selected` (`SwitcherRow?`); `DigitsJump`;
    `Hint`; `const string JumpHint`, `const string FilterHint`; `void Open()`, `void Close()`, `void MoveUp()`,
    `void MoveDown()`, `SessionEntry? TryDigit(char digit)`, `SessionEntry? Choose()`.

- [ ] **Step 1: Write the failing tests**

`tests/LizTerm.App.Tests/ViewModels/SessionSwitcherViewModelTests.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.App.Sessions;
using LizTerm.App.Tests.Fakes;
using LizTerm.App.ViewModels;
using LizTerm.Core.Session;

namespace LizTerm.App.Tests.ViewModels;

/// <summary>Session switching spec §5.2 and §5.4, without a window.</summary>
public class SessionSwitcherViewModelTests
{
    /// <summary>A list of A (this window), B and C, with A the current session and C the previous one.</summary>
    private static (SessionList List, SessionEntry A, SessionEntry B, SessionEntry C, SessionSwitcherViewModel Vm) Three()
    {
        var list = new SessionList();
        var (a, _, _) = TestSessions.Create("TSO", "tk5.local");
        var (b, _, _) = TestSessions.Create("CICS", "zxplore.example", note: "No live data", tags: ["ZOS"]);
        var (c, _, _) = TestSessions.Create("VM370", "vm370.local");
        list.Add(a);
        list.Add(b);
        list.Add(c);
        list.Activated(c);
        list.Activated(a);
        return (list, a, b, c, new SessionSwitcherViewModel(list, a));
    }

    [Fact]
    public void Opening_lists_every_session_and_selects_the_previous_one()
    {
        var (_, a, b, c, vm) = Three();

        vm.Open();

        Assert.True(vm.IsOpen);
        Assert.Equal([a, b, c], vm.Rows.Select(row => row.Entry));
        Assert.Same(c, vm.Selected!.Entry);
        Assert.True(vm.Selected.IsSelected);
        Assert.True(vm.DigitsJump);
        Assert.Equal(SessionSwitcherViewModel.JumpHint, vm.Hint);
    }

    [Fact]
    public void Opening_with_one_session_selects_it()
    {
        var list = new SessionList();
        var (only, _, _) = TestSessions.Create("TSO");
        list.Add(only);
        var vm = new SessionSwitcherViewModel(list, only);

        vm.Open();

        Assert.Same(only, vm.Selected!.Entry);
    }

    [Fact]
    public void A_digit_jumps_only_while_the_filter_is_empty()
    {
        var (_, _, b, _, vm) = Three();
        vm.Open();

        Assert.Same(b, vm.TryDigit('2'));

        vm.Term = "v";
        Assert.False(vm.DigitsJump);
        Assert.Equal(SessionSwitcherViewModel.FilterHint, vm.Hint);
        Assert.Null(vm.TryDigit('2'));

        vm.Term = "";
        Assert.Same(b, vm.TryDigit('2'));
    }

    [Fact]
    public void A_digit_with_no_session_at_its_position_does_nothing()
    {
        var (_, _, _, _, vm) = Three();
        vm.Open();

        Assert.Null(vm.TryDigit('4'));
        Assert.Null(vm.TryDigit('0'));
        Assert.Null(vm.TryDigit('x'));
    }

    [Fact]
    public void Zero_is_the_tenth_session_and_the_eleventh_has_no_number()
    {
        var list = new SessionList();
        var entries = Enumerable.Range(1, 11).Select(i => TestSessions.Create($"S{i}").Entry).ToList();
        entries.ForEach(list.Add);
        var vm = new SessionSwitcherViewModel(list, entries[0]);
        vm.Open();

        Assert.Same(entries[9], vm.TryDigit('0'));
        Assert.Equal("0", vm.Rows[9].Number);
        Assert.Null(vm.Rows[10].Number);
        Assert.False(vm.Rows[10].HasNumber);
    }

    [Fact]
    public void A_digit_is_ignored_while_closed()
    {
        var (_, _, _, _, vm) = Three();

        Assert.Null(vm.TryDigit('1'));
        Assert.Null(vm.Choose());
    }

    [Theory]
    [InlineData("cics", true)]    // name
    [InlineData("ZXPLORE", true)] // host
    [InlineData("zos", true)]     // tag
    [InlineData("LIVE", true)]    // note
    [InlineData("", true)]
    [InlineData("vm370", false)]
    public void The_filter_matches_name_host_tags_and_note_ignoring_case(string term, bool expected)
    {
        var (entry, _, _) = TestSessions.Create("CICS", "zxplore.example", note: "No live data", tags: ["ZOS"]);

        Assert.Equal(expected, SwitcherFilter.Matches(entry, term));
    }

    [Fact]
    public void Changing_the_filter_selects_the_first_match_and_dims_the_numbers()
    {
        var (_, _, _, c, vm) = Three();
        vm.Open();

        vm.Term = "vm";

        Assert.Equal([c], vm.Rows.Select(row => row.Entry));
        Assert.Same(c, vm.Selected!.Entry);
        Assert.False(vm.Rows[0].NumberActive);
        Assert.Equal("3", vm.Rows[0].Number); // its place in opening order, not in the filtered list
    }

    [Fact]
    public void Up_and_down_stop_at_the_ends()
    {
        var (_, a, b, c, vm) = Three();
        vm.Open(); // on C, the last row

        vm.MoveDown();
        Assert.Same(c, vm.Selected!.Entry);

        vm.MoveUp();
        Assert.Same(b, vm.Selected!.Entry);
        Assert.False(vm.Rows[2].IsSelected);
        vm.MoveUp();
        vm.MoveUp();
        Assert.Same(a, vm.Selected!.Entry);
    }

    [Fact]
    public void Enter_chooses_the_selected_session_and_nothing_when_no_row_matches()
    {
        var (_, _, _, c, vm) = Three();
        vm.Open();

        Assert.Same(c, vm.Choose());

        vm.Term = "zzz";
        Assert.Empty(vm.Rows);
        Assert.Null(vm.Choose());
    }

    [Fact]
    public void Rows_carry_the_connection_mark_and_the_first_flag_that_applies()
    {
        var list = new SessionList();
        var (own, _, _) = TestSessions.Create("TSO");
        var (down, _, _) = TestSessions.Create("CICS", state: ConnectionState.Disconnected);
        var (pinned, _, pinnedHost) = TestSessions.Create("IMON");
        var (plain, _, _) = TestSessions.Create("VM370");
        pinnedHost.KeepOnTop = true;
        foreach (var entry in new[] { own, down, pinned, plain }) list.Add(entry);
        var vm = new SessionSwitcherViewModel(list, own);

        vm.Open();

        Assert.Equal(["●", "○", "●", "●"], vm.Rows.Select(row => row.ConnectionMark));
        Assert.Equal(new string?[] { "This window", "Disconnected", "On top", null }, vm.Rows.Select(row => row.Flag));
        Assert.All(vm.Rows, row => Assert.True(row.NumberActive));
    }

    [Fact]
    public void A_change_while_open_keeps_the_selected_session_or_falls_to_the_first_row()
    {
        var (list, a, b, c, vm) = Three();
        vm.Open();
        vm.MoveUp(); // B

        list.Remove(c);
        Assert.Same(b, vm.Selected!.Entry);

        list.Remove(b);
        Assert.Same(a, vm.Selected!.Entry);
    }

    [Fact]
    public void Closing_stops_listening_and_empties_the_rows()
    {
        var (list, _, _, _, vm) = Three();
        vm.Open();

        vm.Close();
        list.Add(TestSessions.Create("NEW").Entry);

        Assert.False(vm.IsOpen);
        Assert.Empty(vm.Rows);
        Assert.Null(vm.Selected);
    }

    [Fact]
    public void Reopening_clears_the_filter()
    {
        var (_, _, _, _, vm) = Three();
        vm.Open();
        vm.Term = "vm";
        vm.Close();

        vm.Open();

        Assert.Equal("", vm.Term);
        Assert.Equal(3, vm.Rows.Count);
    }
}
```

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~SessionSwitcherViewModelTests"`
Expected: build FAILS — `SessionSwitcherViewModel`, `SwitcherRow` and `SwitcherFilter` do not exist.

- [ ] **Step 3: Implement the filter**

`src/LizTerm.App/ViewModels/SwitcherFilter.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.App.Sessions;

namespace LizTerm.App.ViewModels;

/// <summary>What the switcher's filter matches (session switching spec §5.4): an ordinal, case-insensitive
/// substring of the name, host:port, any tag name, or the note. The note is included on purpose (Robert's call,
/// 2026-09-15): "live" should find the session noted "No live data".</summary>
public static class SwitcherFilter
{
    public static bool Matches(SessionEntry entry, string term)
    {
        if (term.Length == 0) return true;
        var row = entry.Summary;
        return Has(row.Name)
            || Has(row.HostPort)
            || entry.Session.Profile.Tags.Names.Any(Has)
            || (row.Note is { } note && Has(note));

        bool Has(string text) => text.Contains(term, StringComparison.OrdinalIgnoreCase);
    }
}
```

- [ ] **Step 4: Implement the row**

`src/LizTerm.App/ViewModels/SwitcherRow.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using LizTerm.App.Sessions;
using LizTerm.Core.Session;

namespace LizTerm.App.ViewModels;

/// <summary>One switcher row, a snapshot of its session when the rows were built (session switching spec §5.3).
/// Only the selection moves while it is on screen; anything else that changes rebuilds the rows.</summary>
public sealed partial class SwitcherRow : ObservableObject
{
    public SwitcherRow(SessionEntry entry, int? position, bool numberActive, bool isThisWindow)
    {
        Entry = entry;
        Number = position is { } p ? (p % 10).ToString(CultureInfo.InvariantCulture) : null;
        NumberActive = numberActive;
        IsDisconnected = entry.Session.Connection == ConnectionState.Disconnected;
        IsThisWindow = isThisWindow;
        IsOnTop = entry.Host.KeepOnTop;
    }

    public SessionEntry Entry { get; }

    /// <summary>The Sessions list's row, which ProfileSummaryTemplate draws.</summary>
    public ProfileRow Summary => Entry.Summary;

    /// <summary>"1" to "9", "0" for the tenth, null past ten: the position in opening order, kept when filtered.</summary>
    public string? Number { get; }

    public bool HasNumber => Number is not null;

    /// <summary>A solid badge while a digit jumps; a dashed outline once the filter holds text.</summary>
    public bool NumberActive { get; }

    public bool IsDisconnected { get; }

    /// <summary>The window the switcher opened in.</summary>
    public bool IsThisWindow { get; }

    public bool IsOnTop { get; }

    /// <summary>Shape, not colour: the flag adds the word.</summary>
    public string ConnectionMark => IsDisconnected ? "○" : "●";

    /// <summary>The first that applies, as words so no flag depends on colour or a glyph a UI font might lack.</summary>
    public string? Flag =>
        IsDisconnected ? "Disconnected"
        : IsThisWindow ? "This window"
        : IsOnTop ? "On top"
        : null;

    [ObservableProperty] private bool _isSelected;
}
```

- [ ] **Step 5: Implement the view model**

`src/LizTerm.App/ViewModels/SessionSwitcherViewModel.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using CommunityToolkit.Mvvm.ComponentModel;
using LizTerm.App.Sessions;

namespace LizTerm.App.ViewModels;

/// <summary>The session switcher's state for one window (session switching spec §5). FindViewModel's shape: its
/// own lifetime and no controls, so every rule is a plain [Fact]. It never brings a window itself: TryDigit and
/// Choose return the entry, and the window calls Host.Bring() on it.</summary>
public sealed partial class SessionSwitcherViewModel : ObservableObject
{
    public const string JumpHint = "1–9, 0 jump · ↑↓ move · ↵ switch · Esc close";
    public const string FilterHint = "↑↓ move · ↵ switch · Esc close · digits type into the filter now";

    private readonly SessionList _sessions;
    private readonly SessionEntry _own;

    public SessionSwitcherViewModel(SessionList sessions, SessionEntry own)
    {
        _sessions = sessions;
        _own = own;
    }

    [ObservableProperty] private bool _isOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DigitsJump))]
    [NotifyPropertyChangedFor(nameof(Hint))]
    private string _term = "";

    [ObservableProperty] private IReadOnlyList<SwitcherRow> _rows = [];

    [ObservableProperty] private SwitcherRow? _selected;

    /// <summary>A digit jumps only while the filter is empty; after that it filters, because names like TK4 and
    /// VM370 contain digits.</summary>
    public bool DigitsJump => Term.Length == 0;

    public string Hint => DigitsJump ? JumpHint : FilterHint;

    /// <summary>Clears the filter, lists every session, and selects the previous one — so Cmd+K, Enter flips
    /// between two — or the current one when it is alone.</summary>
    public void Open()
    {
        if (IsOpen) return;
        Term = "";
        IsOpen = true;
        _sessions.Changed += OnSessionsChanged;
        Rebuild(_sessions.Previous ?? _sessions.Current);
    }

    public void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;
        _sessions.Changed -= OnSessionsChanged;
        Selected = null;
        Rows = [];
    }

    /// <summary>The session at a digit's position while digits jump (0 is the tenth), else null.</summary>
    public SessionEntry? TryDigit(char digit)
    {
        if (!IsOpen || !DigitsJump || digit is < '0' or > '9') return null;
        var position = digit == '0' ? SessionList.NumberedSessions : digit - '0';
        return position <= _sessions.Count ? _sessions.Entries[position - 1] : null;
    }

    public SessionEntry? Choose() => IsOpen ? Selected?.Entry : null;

    public void MoveUp() => Move(-1);

    public void MoveDown() => Move(1);

    private void Move(int delta)
    {
        if (Selected is not { } selected) return;
        var index = IndexOf(selected) + delta;
        if (index >= 0 && index < Rows.Count) Selected = Rows[index];
    }

    private int IndexOf(SwitcherRow row)
    {
        for (var i = 0; i < Rows.Count; i++)
            if (ReferenceEquals(Rows[i], row)) return i;
        return -1;
    }

    partial void OnTermChanged(string value)
    {
        if (IsOpen) Rebuild(null);
    }

    partial void OnSelectedChanged(SwitcherRow? oldValue, SwitcherRow? newValue)
    {
        if (oldValue is not null) oldValue.IsSelected = false;
        if (newValue is not null) newValue.IsSelected = true;
    }

    private void OnSessionsChanged(object? sender, EventArgs e) => Rebuild(Selected?.Entry);

    /// <summary>Rows for the current filter, keeping the preferred session selected when it is still listed and
    /// otherwise selecting the first row.</summary>
    private void Rebuild(SessionEntry? prefer)
    {
        var rows = _sessions.Entries
            .Where(entry => SwitcherFilter.Matches(entry, Term))
            .Select(entry => new SwitcherRow(entry, _sessions.PositionOf(entry), DigitsJump, ReferenceEquals(entry, _own)))
            .ToList();
        Selected = null;
        Rows = rows;
        Selected = rows.FirstOrDefault(row => ReferenceEquals(row.Entry, prefer)) ?? rows.FirstOrDefault();
    }
}
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~SessionSwitcherViewModelTests"`
Expected: PASS, 19 test cases (13 facts and the 6 cases of the filter theory).

- [ ] **Step 7: Commit**

```bash
git add src/LizTerm.App/ViewModels/SwitcherFilter.cs src/LizTerm.App/ViewModels/SwitcherRow.cs src/LizTerm.App/ViewModels/SessionSwitcherViewModel.cs tests/LizTerm.App.Tests/ViewModels/SessionSwitcherViewModelTests.cs
git commit -m "Add the session switcher's view model" -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Task 5: `TerminalScreen` raises `SwitcherRequested` for Cmd/Ctrl+K

**Files:**
- Modify: `src/LizTerm.App/Controls/TerminalScreen.cs` (the `FindRequested` declaration near line 233, and
  `TryHandlePlatformGesture` near line 297)
- Test: `tests/LizTerm.App.Tests/Controls/TerminalScreenInputTests.cs`

**Interfaces:**
- Produces: `public event EventHandler? SwitcherRequested` on `TerminalScreen`, raised for `Key.K` with the
  platform's `CommandModifiers` (Cmd on macOS, Ctrl elsewhere), ahead of the keymap.

- [ ] **Step 1: Write the failing test**

Append inside `TerminalScreenInputTests`:

```csharp
    /// <summary>The session switcher's chord (session switching spec §6), beside Find's: the platform's command
    /// modifier plus K. It is checked before the keymap, so it can never become a key or text for the host. The
    /// headless platform's CommandModifiers is Control, as the Find chord's tests rely on.</summary>
    [AvaloniaFact]
    public void The_command_modifier_with_k_raises_SwitcherRequested_and_sends_nothing()
    {
        var (window, screen) = Show();
        var requests = 0;
        var keys = new List<TerminalKey>();
        var text = new List<string>();
        screen.SwitcherRequested += (_, _) => requests++;
        screen.KeyRequested += (_, key) => keys.Add(key);
        screen.TextEntered += (_, typed) => text.Add(typed);

        window.KeyPressQwerty(PhysicalKey.K, RawInputModifiers.Control);

        Assert.Equal(1, requests);
        Assert.Empty(keys);
        Assert.Empty(text);
    }
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~TerminalScreenInputTests"`
Expected: build FAILS — `TerminalScreen` has no `SwitcherRequested`.

- [ ] **Step 3: Implement it**

In `src/LizTerm.App/Controls/TerminalScreen.cs`, directly after `public event EventHandler? FindRequested;` add:

```csharp
    /// <summary>The session switcher's chord, Cmd+K on macOS and Ctrl+K elsewhere (#46). Like FindRequested it is
    /// a platform gesture, checked ahead of the keymap, so a future user keymap (#18) cannot take Ctrl+K.</summary>
    public event EventHandler? SwitcherRequested;
```

In `TryHandlePlatformGesture`, directly after the Find block's closing brace and before `return false;`, add:

```csharp
        // The switcher's chord, built the same way as Find's.
        if (e.Key == Key.K && e.KeyModifiers == (hotkeys?.CommandModifiers ?? KeyModifiers.Control))
        {
            SwitcherRequested?.Invoke(this, EventArgs.Empty);
            return true;
        }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~TerminalScreenInputTests"`
Expected: PASS.

- [ ] **Step 5: Run the App tests and commit**

Run: `dotnet test tests/LizTerm.App.Tests`
Expected: PASS.

```bash
git add src/LizTerm.App/Controls/TerminalScreen.cs tests/LizTerm.App.Tests/Controls/TerminalScreenInputTests.cs
git commit -m "Raise SwitcherRequested from the terminal for Cmd/Ctrl+K" -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Task 6: The switcher overlay in the session window

**Files:**
- Create: `src/LizTerm.App/Controls/SessionSwitcher.axaml`
- Create: `src/LizTerm.App/Controls/SessionSwitcher.axaml.cs`
- Modify: `src/LizTerm.App/Views/SessionWindow.axaml` (the `TerminalScreen` element at the end of the `DockPanel`)
- Modify: `src/LizTerm.App/Views/SessionWindow.axaml.cs`
- Test: `tests/LizTerm.App.Tests/Views/SessionSwitcherTests.cs`

**Interfaces:**
- Consumes: `SessionList`, `SessionEntry`, `ISessionHost` (Task 2); `SessionSwitcherViewModel`, `SwitcherRow`
  (Task 4); `TerminalScreen.SwitcherRequested` (Task 5); `ProfileSummaryTemplate` (Task 1).
- Produces:
  - `SessionSwitcher : UserControl` — `event EventHandler<SessionEntry>? Chosen`, `event EventHandler? Dismissed`,
    `void FocusBox()`, `internal TextBox Box`. Named parts: `SwitcherBox`, `Dim`, `Palette`, `RowsHost`, and per
    row `RowBorder`.
  - `SessionWindow : Window, ISessionHost` — `internal void AttachSessions(SessionList sessions, SessionEntry own)`,
    `internal SessionSwitcherViewModel? Switcher`, `private void ToggleSwitcher()` (Task 7's menu item calls it),
    `public void Bring()`, `public bool IsMinimized`, `public bool KeepOnTop`, `public event EventHandler? KeepOnTopChanged`,
    and an `OnPropertyChanged` override that Task 7 extends.
  - The window's named overlay: `SwitcherPanel` (`SessionSwitcher`).

**Two traps this task is shaped around:**
- **A digit is taken from `TextInput`, never from `KeyDown`.** Handling the key-down would close the switcher and
  focus the screen before the platform delivers the digit's text input, which would then reach the terminal and
  go to the host. Taking the `TextInput` event itself leaves nothing behind.
- **The overlay's visibility is set in code, never bound.** Until `App` attaches the session list, the overlay has
  no view model; a failed `IsVisible` binding falls back to `true`, which would dim every window. So the window
  declares it `IsVisible="False"` with a null `DataContext`, and only `ToggleSwitcher`/`CloseSwitcher` change it.

- [ ] **Step 1: Write the failing tests**

`tests/LizTerm.App.Tests/Views/SessionSwitcherTests.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Platform;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using LizTerm.App.Controls;
using LizTerm.App.Menus;
using LizTerm.App.Sessions;
using LizTerm.App.Tests.Fakes;
using LizTerm.App.ViewModels;
using LizTerm.App.Views;
using LizTerm.Core.Profiles;
using LizTerm.Core.Session;
using LizTerm.Core.Settings;

namespace LizTerm.App.Tests.Views;

/// <summary>The switcher overlay in a real headless SessionWindow (session switching spec §5, §6). The window's
/// own session is TSO; a second, VM370, sits behind a FakeSessionHost and is the previous session.</summary>
public class SessionSwitcherTests
{
    private sealed record Rig(SessionWindow Window, FakeEmulatorSession Session, SessionList List, SessionEntry Own,
        SessionEntry Second, FakeSessionHost SecondHost);

    private static Rig Show(MenuStyle style = MenuStyle.Native)
    {
        var session = new FakeEmulatorSession { Profile = new SessionProfile { Name = "TSO", Host = "tk5.local", Port = 3270 } };
        var vm = new SessionViewModel(session, action => action(), new FakeTextClipboard { Text = "typed-into-the-host-by-mistake" });
        var window = new SessionWindow(style, isMacOS: true) { DataContext = vm };
        var list = new SessionList();
        var own = new SessionEntry(vm, new ProfileRow(session.Profile, TagRegistry.Empty), true, window);
        var (second, _, secondHost) = TestSessions.Create("VM370", "vm370.local");
        list.Add(own);
        list.Add(second);
        window.AttachSessions(list, own);
        window.Show();
        list.Activated(second);
        list.Activated(own);
        window.FindControl<TerminalScreen>("Screen")!.Focus();
        return new Rig(window, session, list, own, second, secondHost);
    }

    private static SessionSwitcher Panel(SessionWindow window) => window.FindControl<SessionSwitcher>("SwitcherPanel")!;

    private static bool ScreenFocused(SessionWindow window) => window.FindControl<TerminalScreen>("Screen")!.IsFocused;

    private static void OpenWithChord(SessionWindow window) => window.KeyPressQwerty(PhysicalKey.K, RawInputModifiers.Control);

    private static Point CentreOf(SessionWindow window, Control control) =>
        control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;

    /// <summary>The only dispatch path under InWindow, and on Windows and Linux: the screen's chord. Over all three
    /// styles, modelled on NativeMenuTests.The_find_gesture_opens_the_bar_via_TerminalScreen.</summary>
    [AvaloniaTheory]
    [InlineData(MenuStyle.Native)]
    [InlineData(MenuStyle.InWindow)]
    [InlineData(MenuStyle.Both)]
    public void The_chord_opens_the_switcher_via_the_screen(MenuStyle style)
    {
        var rig = Show(style);

        OpenWithChord(rig.Window);

        Assert.True(rig.Window.Switcher!.IsOpen);
        Assert.True(Panel(rig.Window).IsVisible);
        Assert.True(Panel(rig.Window).Box.IsFocused);
        Assert.Same(rig.Second, rig.Window.Switcher.Selected!.Entry);
    }

    [AvaloniaFact]
    public void The_chord_again_closes_it_and_returns_focus_to_the_screen()
    {
        var rig = Show();
        OpenWithChord(rig.Window);

        OpenWithChord(rig.Window);

        Assert.False(rig.Window.Switcher!.IsOpen);
        Assert.False(Panel(rig.Window).IsVisible);
        Assert.True(ScreenFocused(rig.Window));
    }

    /// <summary>The highest-consequence rule of the feature, Find's invariant: nothing typed while the switcher is
    /// open reaches the host — letters, a keymap chord (Alt+1 is PA1), Enter with no match, and Escape.</summary>
    [AvaloniaFact]
    public void Nothing_typed_in_the_switcher_reaches_the_host()
    {
        var rig = Show();
        rig.Session.RaiseConnection(ConnectionState.Connected3270);
        OpenWithChord(rig.Window);

        rig.Window.KeyTextInput("zzz");
        rig.Window.KeyPressQwerty(PhysicalKey.Digit1, RawInputModifiers.Alt);
        rig.Window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        rig.Window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);

        Assert.Empty(rig.Session.Calls);
        Assert.Equal(0, rig.SecondHost.BringCount);
        Assert.False(rig.Window.Switcher!.IsOpen);
        Assert.True(ScreenFocused(rig.Window));
    }

    [AvaloniaFact]
    public void A_digit_with_the_filter_empty_brings_that_session_and_closes()
    {
        var rig = Show();
        OpenWithChord(rig.Window);

        rig.Window.KeyTextInput("2");

        Assert.Equal(1, rig.SecondHost.BringCount);
        Assert.False(rig.Window.Switcher!.IsOpen);
        Assert.Equal("", Panel(rig.Window).Box.Text ?? "");
        Assert.Empty(rig.Session.Calls);
    }

    [AvaloniaFact]
    public void A_digit_after_a_letter_goes_into_the_filter()
    {
        var rig = Show();
        OpenWithChord(rig.Window);

        rig.Window.KeyTextInput("v");
        rig.Window.KeyTextInput("3");

        Assert.Equal("v3", rig.Window.Switcher!.Term);
        Assert.Equal(0, rig.SecondHost.BringCount);
        Assert.True(rig.Window.Switcher.IsOpen);
    }

    [AvaloniaFact]
    public void Enter_brings_the_previous_session()
    {
        var rig = Show();
        OpenWithChord(rig.Window);

        rig.Window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);

        Assert.Equal(1, rig.SecondHost.BringCount);
        Assert.False(rig.Window.Switcher!.IsOpen);
    }

    /// <summary>A real pointer, not a raised event: the row's Border must have a background to be hit at all (the
    /// Manage Tags swatch lesson, 2026-09-13).</summary>
    [AvaloniaFact]
    public void Clicking_a_row_brings_its_session()
    {
        var rig = Show();
        OpenWithChord(rig.Window);
        rig.Window.UpdateLayout();
        var rows = rig.Window.GetVisualDescendants().OfType<Border>().Where(b => b.Name == "RowBorder").ToList();
        Assert.Equal(2, rows.Count);
        var centre = CentreOf(rig.Window, rows[1]);

        rig.Window.MouseDown(centre, MouseButton.Left);
        rig.Window.MouseUp(centre, MouseButton.Left);

        Assert.Equal(1, rig.SecondHost.BringCount);
        Assert.False(rig.Window.Switcher!.IsOpen);
    }

    [AvaloniaFact]
    public void Clicking_the_dimmed_screen_closes_without_bringing_anything()
    {
        var rig = Show();
        OpenWithChord(rig.Window);
        rig.Window.UpdateLayout();
        var dim = rig.Window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "Dim");
        var nearBottom = dim.TranslatePoint(new Point(dim.Bounds.Width / 2, dim.Bounds.Height - 10), rig.Window)!.Value;

        rig.Window.MouseDown(nearBottom, MouseButton.Left);
        rig.Window.MouseUp(nearBottom, MouseButton.Left);

        Assert.False(rig.Window.Switcher!.IsOpen);
        Assert.Equal(0, rig.SecondHost.BringCount);
    }

    /// <summary>The Find box's guard, extended (spec §5.5): on macOS the native Edit items are key equivalents
    /// dispatched ahead of the focused control, so with the switcher's box focused Cmd+V must paste into the box,
    /// never type the clipboard into the host.</summary>
    [AvaloniaFact]
    public async Task The_native_paste_item_pastes_into_the_switcher_box_when_it_is_focused()
    {
        var rig = Show();
        rig.Session.RaiseConnection(ConnectionState.Connected3270);
        OpenWithChord(rig.Window);
        await rig.Window.Clipboard!.SetTextAsync("vm");

        var paste = MenuLookup.Item(NativeMenu.GetMenu(rig.Window), "_Edit", "_Paste")!;
        ((INativeMenuItemExporterEventsImplBridge)paste).RaiseClicked();

        Assert.Equal("vm", Panel(rig.Window).Box.Text);
        Assert.DoesNotContain(rig.Session.Calls, call => call.StartsWith("paste:"));
    }

    [AvaloniaFact]
    public void A_window_with_no_session_list_ignores_the_chord()
    {
        var window = new SessionWindow(MenuStyle.Native, isMacOS: true)
        {
            DataContext = new SessionViewModel(new FakeEmulatorSession(), action => action(), new FakeTextClipboard()),
        };
        window.Show();
        window.FindControl<TerminalScreen>("Screen")!.Focus();

        OpenWithChord(window);

        Assert.Null(window.Switcher);
        Assert.False(Panel(window).IsVisible);
    }

    [AvaloniaFact]
    public void Closing_the_window_removes_its_session_from_the_list()
    {
        var rig = Show();

        rig.Window.Close();

        Assert.Equal([rig.Second], rig.List.Entries);
    }

    [AvaloniaFact]
    public void Bring_restores_a_minimised_window()
    {
        var rig = Show();
        rig.Window.WindowState = WindowState.Minimized;
        Assert.True(rig.Window.IsMinimized);

        rig.Window.Bring();

        Assert.Equal(WindowState.Normal, rig.Window.WindowState);
        Assert.False(rig.Window.IsMinimized);
    }

    [AvaloniaFact]
    public void Topmost_is_keep_on_top_and_announces_its_changes()
    {
        var rig = Show();
        var changes = 0;
        rig.Window.KeepOnTopChanged += (_, _) => changes++;

        rig.Window.Topmost = true;

        Assert.True(rig.Window.KeepOnTop);
        Assert.Equal(1, changes);
    }
}
```

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~SessionSwitcherTests"`
Expected: build FAILS — no `SessionSwitcher`, no `AttachSessions`, and `SessionWindow` is not an `ISessionHost`.

- [ ] **Step 3: Create the overlay control**

`src/LizTerm.App/Controls/SessionSwitcher.axaml`:

```xml
<!--
  This file is part of LizTerm.
  Copyright 2026 by CoffeeMuse
  SPDX-License-Identifier: BSD-3-Clause
-->
<!-- The session switcher (#46): a palette at the top centre over a dimmed screen. Session switching spec §5.1 and
     §5.3. Its visibility belongs to SessionWindow's code-behind, never a binding here: see Task 6's note. -->
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:LizTerm.App.ViewModels"
             x:Class="LizTerm.App.Controls.SessionSwitcher"
             x:DataType="vm:SessionSwitcherViewModel">
  <UserControl.Styles>
    <!-- A background on every row, transparent when not selected: a Border with a null background is nothing to
         hit, and a click on the row would fall through to the dim. -->
    <Style Selector="Border.row">
      <Setter Property="Background" Value="Transparent" />
      <Setter Property="BorderBrush" Value="Transparent" />
    </Style>
    <!-- The selection is the bar on the left and the ↵ mark as well as the fill, so colour never carries it. -->
    <Style Selector="Border.row.selected">
      <Setter Property="Background" Value="#2F3D55" />
      <Setter Property="BorderBrush" Value="#8FB0FF" />
    </Style>
    <Style Selector="Rectangle.badge">
      <Setter Property="Fill" Value="#4A4A4A" />
    </Style>
    <!-- Once the filter holds text, digits filter instead of jumping, and the badge says so by its outline. -->
    <Style Selector="Rectangle.badge.idle">
      <Setter Property="Fill" Value="Transparent" />
      <Setter Property="Stroke" Value="#6A6A6A" />
      <Setter Property="StrokeDashArray" Value="2,2" />
    </Style>
  </UserControl.Styles>
  <Panel>
    <Border x:Name="Dim" Background="#80000000" PointerPressed="OnDimPressed" />
    <!-- 88% of the screen area's width, capped at 520 px, centred: star columns give the 88%, MaxWidth the cap. -->
    <Grid ColumnDefinitions="6*,88*,6*">
      <Border x:Name="Palette" Grid.Column="1" HorizontalAlignment="Stretch" VerticalAlignment="Top" MaxWidth="520"
              Margin="0,12" Background="#262626" BorderBrush="#4A4A4A" BorderThickness="1" CornerRadius="8">
        <DockPanel>
          <TextBox x:Name="SwitcherBox" DockPanel.Dock="Top" Margin="10,8,10,6"
                   Text="{Binding Term, Mode=TwoWay}" PlaceholderText="Switch to a session: type to filter" />
          <TextBlock x:Name="HintLine" DockPanel.Dock="Bottom" Margin="10,5,10,7" FontSize="11" Foreground="#8F8F8F"
                     Text="{Binding Hint}" />
          <ScrollViewer HorizontalScrollBarVisibility="Disabled">
            <ItemsControl x:Name="RowsHost" ItemsSource="{Binding Rows}">
              <ItemsControl.ItemTemplate>
                <DataTemplate x:DataType="vm:SwitcherRow">
                  <Border x:Name="RowBorder" Classes="row" Classes.selected="{Binding IsSelected}"
                          BorderThickness="3,0,0,0" Padding="7,5,10,5" PointerReleased="OnRowReleased">
                    <Grid ColumnDefinitions="18,8,12,8,*,8,Auto,6,12">
                      <Panel Grid.Column="0" Width="18" Height="18" VerticalAlignment="Center" IsVisible="{Binding HasNumber}">
                        <Rectangle Classes="badge" Classes.idle="{Binding !NumberActive}" RadiusX="4" RadiusY="4" />
                        <TextBlock x:Name="NumberBadge" Text="{Binding Number}" FontSize="11" FontWeight="Bold"
                                   HorizontalAlignment="Center" VerticalAlignment="Center" />
                      </Panel>
                      <TextBlock x:Name="ConnectionMark" Grid.Column="2" Text="{Binding ConnectionMark}"
                                 HorizontalAlignment="Center" VerticalAlignment="Center" />
                      <ContentControl Grid.Column="4" Content="{Binding Summary}"
                                      ContentTemplate="{StaticResource ProfileSummaryTemplate}" />
                      <TextBlock x:Name="FlagText" Grid.Column="6" Text="{Binding Flag}" FontSize="11"
                                 Foreground="#B5B5B5" VerticalAlignment="Center" />
                      <TextBlock x:Name="SelectionMark" Grid.Column="8" Text="↵" FontWeight="SemiBold"
                                 Foreground="#CFE0FF" VerticalAlignment="Center" IsVisible="{Binding IsSelected}" />
                    </Grid>
                  </Border>
                </DataTemplate>
              </ItemsControl.ItemTemplate>
            </ItemsControl>
          </ScrollViewer>
        </DockPanel>
      </Border>
    </Grid>
  </Panel>
</UserControl>
```

`src/LizTerm.App/Controls/SessionSwitcher.axaml.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using LizTerm.App.Sessions;
using LizTerm.App.ViewModels;

namespace LizTerm.App.Controls;

/// <summary>The switcher overlay. Keypad's contract: it raises Chosen and Dismissed and brings nothing itself;
/// SessionWindow decides what closing and choosing mean. Keys are taken on the tunnel, ahead of the TextBox, and a
/// jumping digit is taken from TextInput — see Task 6's note on why never from KeyDown.</summary>
public partial class SessionSwitcher : UserControl
{
    private SessionSwitcherViewModel? _viewModel;

    public SessionSwitcher()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnTunnelKeyDown, RoutingStrategies.Tunnel);
        AddHandler(TextInputEvent, OnTunnelTextInput, RoutingStrategies.Tunnel);
    }

    /// <summary>A session was picked: by a jumping digit, Enter, or a click on its row.</summary>
    public event EventHandler<SessionEntry>? Chosen;

    /// <summary>Escape, Cmd/Ctrl+K, or a click on the dimmed screen.</summary>
    public event EventHandler? Dismissed;

    /// <summary>The filter box, for SessionWindow's clipboard guards: it lives in this control's name scope, so
    /// the window cannot reach it by name.</summary>
    internal TextBox Box => SwitcherBox;

    public void FocusBox() => SwitcherBox.Focus();

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_viewModel is not null) _viewModel.PropertyChanged -= OnViewModelChanged;
        _viewModel = DataContext as SessionSwitcherViewModel;
        if (_viewModel is not null) _viewModel.PropertyChanged += OnViewModelChanged;
    }

    /// <summary>Keeps the selected row in view when the arrow keys move past the scrolled part of a long list.</summary>
    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SessionSwitcherViewModel.Selected) && _viewModel?.Selected is { } row)
            RowsHost.ContainerFromItem(row)?.BringIntoView();
    }

    private void OnTunnelKeyDown(object? sender, KeyEventArgs e)
    {
        if (_viewModel is not { IsOpen: true } vm) return;
        var command = this.GetPlatformSettings()?.HotkeyConfiguration.CommandModifiers ?? KeyModifiers.Control;
        switch (e.Key)
        {
            case Key.Up:
                vm.MoveUp();
                break;
            case Key.Down:
                vm.MoveDown();
                break;
            case Key.Enter:
                if (vm.Choose() is { } chosen) Chosen?.Invoke(this, chosen);
                break;
            case Key.Escape:
                Dismissed?.Invoke(this, EventArgs.Empty);
                break;
            case Key.K when e.KeyModifiers == command:
                Dismissed?.Invoke(this, EventArgs.Empty);
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    private void OnTunnelTextInput(object? sender, TextInputEventArgs e)
    {
        if (_viewModel is not { IsOpen: true, DigitsJump: true } vm) return;
        if (e.Text is not { Length: 1 } text || !char.IsAsciiDigit(text[0])) return;
        e.Handled = true;
        if (vm.TryDigit(text[0]) is { } chosen) Chosen?.Invoke(this, chosen);
    }

    private void OnRowReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != MouseButton.Left || sender is not Control { DataContext: SwitcherRow row }) return;
        e.Handled = true;
        Chosen?.Invoke(this, row.Entry);
    }

    private void OnDimPressed(object? sender, PointerPressedEventArgs e)
    {
        e.Handled = true;
        Dismissed?.Invoke(this, EventArgs.Empty);
    }
}
```

- [ ] **Step 4: Put the overlay over the screen**

In `src/LizTerm.App/Views/SessionWindow.axaml`, replace the `<controls:TerminalScreen x:Name="Screen" ... />` element
(the last child of the `DockPanel`) with a `Panel` holding that same element, unchanged, and the overlay:

```xml
    <!-- The screen and, over it, the session switcher (#46): an overlay rather than a docked bar, so it covers the
         top of the screen and never the bottom rows, where hosts put command lines. Visibility and DataContext are
         the code-behind's: until App attaches the session list there is no view model, and a failed IsVisible
         binding would fall back to true and dim the window. -->
    <Panel>
      <controls:TerminalScreen x:Name="Screen"
                               Snapshot="{Binding Screen}"
                               Selection="{Binding Selection, Mode=TwoWay}"
                               Crosshair="{Binding Settings.Crosshair}"
                               BlinkEnabled="{Binding Settings.Blink}"
                               FindMatches="{Binding Find.Matches}"
                               CurrentMatch="{Binding Find.CurrentMatch}"
                               DestructiveBackspace="{Binding Profile.DestructiveBackspace}" />
      <controls:SessionSwitcher x:Name="SwitcherPanel" IsVisible="False" DataContext="{x:Null}" />
    </Panel>
```

- [ ] **Step 5: Wire the window**

In `src/LizTerm.App/Views/SessionWindow.axaml.cs`:

1. Add `using Avalonia;` and `using LizTerm.App.Sessions;` to the usings, and change the class declaration to
   `public partial class SessionWindow : Window, ISessionHost`.

2. In the constructor, directly after `Screen.FindRequested += (_, _) => ShowFind();`, add:

```csharp
        Screen.SwitcherRequested += (_, _) => ToggleSwitcher();
        SwitcherPanel.Chosen += (_, entry) => ChooseSession(entry);
        SwitcherPanel.Dismissed += (_, _) => CloseSwitcher();
        // A switcher left open over a window the user has moved away from would greet them with a stale list.
        Deactivated += (_, _) =>
        {
            if (Switcher is { IsOpen: true }) CloseSwitcher();
        };
```

3. Add these members directly after the `ViewModel` property (`private SessionViewModel? ViewModel => ...`):

```csharp
    private SessionList? _sessions;
    private SessionEntry? _ownEntry;

    /// <summary>This window's switcher, once App has attached the session list; null for a window built without
    /// one, as most tests build it.</summary>
    internal SessionSwitcherViewModel? Switcher { get; private set; }

    /// <summary>Joins this window to the process's session list (session switching spec §7.2, §9). Called by App
    /// once, before Show(). Activation is reported here so the list's use order follows the user; removal is in
    /// OnClosed, ahead of the Closed event App's shutdown test reads the count from.</summary>
    internal void AttachSessions(SessionList sessions, SessionEntry own)
    {
        if (_sessions is not null) throw new InvalidOperationException("This window already has a session list.");
        _sessions = sessions;
        _ownEntry = own;
        Switcher = new SessionSwitcherViewModel(sessions, own);
        SwitcherPanel.DataContext = Switcher;
        Activated += (_, _) => sessions.Activated(own);
    }

    /// <summary>Cmd/Ctrl+K from the screen, and Window &gt; Switch Session... (Task 7).</summary>
    private void ToggleSwitcher()
    {
        if (Switcher is not { } switcher) return;
        if (switcher.IsOpen)
        {
            CloseSwitcher();
            return;
        }
        switcher.Open();
        SwitcherPanel.IsVisible = true;
        SwitcherPanel.FocusBox();
    }

    /// <summary>Always hands the keyboard back to the screen, as Find's Escape does.</summary>
    private void CloseSwitcher()
    {
        Switcher?.Close();
        SwitcherPanel.IsVisible = false;
        Screen.Focus();
    }

    /// <summary>Closes first: bringing another window deactivates this one, whose handler would otherwise close a
    /// switcher that is mid-choice.</summary>
    private void ChooseSession(SessionEntry entry)
    {
        CloseSwitcher();
        entry.Host.Bring();
    }

    /// <summary>ISessionHost: restore a minimised window, then activate it.</summary>
    public void Bring()
    {
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    public bool IsMinimized => WindowState == WindowState.Minimized;

    /// <summary>Keep on Top is the window's Topmost (spec §7.3).</summary>
    public bool KeepOnTop => Topmost;

    public event EventHandler? KeepOnTopChanged;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TopmostProperty) KeepOnTopChanged?.Invoke(this, EventArgs.Empty);
    }
```

4. Replace the three native clipboard handlers with versions that also check the switcher's box. Add this sentence
   to the end of the comment block above them: `The session switcher's box (#46) is the second such field, guarded
   the same way.`

```csharp
    private void OnCopyClickNative(object? sender, EventArgs e)
    {
        if (FindBox.IsFocused) { FindBox.Copy(); return; }
        if (SwitcherPanel.Box.IsFocused) { SwitcherPanel.Box.Copy(); return; }
        _ = ViewModel?.CopyAsync();
    }

    private void OnPasteClickNative(object? sender, EventArgs e)
    {
        if (FindBox.IsFocused) { FindBox.Paste(); return; }
        if (SwitcherPanel.Box.IsFocused) { SwitcherPanel.Box.Paste(); return; }
        _ = ViewModel?.PasteAsync();
    }

    private void OnSelectAllClickNative(object? sender, EventArgs e)
    {
        if (FindBox.IsFocused) { FindBox.SelectAll(); return; }
        if (SwitcherPanel.Box.IsFocused) { SwitcherPanel.Box.SelectAll(); return; }
        ViewModel?.SelectAll();
    }
```

5. In `OnClosed`, directly before `base.OnClosed(e);`, add:

```csharp
        // Before base.OnClosed raises Closed: App's handler asks ShutdownPolicy with the count of sessions left.
        if (Switcher is { IsOpen: true }) Switcher.Close();
        if (_sessions is not null && _ownEntry is not null) _sessions.Remove(_ownEntry);
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~SessionSwitcherTests"`
Expected: PASS, 15 test cases (12 facts and the 3 cases of the chord theory).

If `Clicking_a_row_brings_its_session` finds no rows, the `ItemsControl` has not realised its containers: call
`Avalonia.Threading.Dispatcher.UIThread.RunJobs()` and then `rig.Window.UpdateLayout()` again before looking them
up (the ComboBox recipe in `tests/CLAUDE.md`), and keep that line in the test.

- [ ] **Step 7: Run the App tests and commit**

Run: `dotnet test tests/LizTerm.App.Tests`
Expected: PASS — including every existing `SessionWindowTests` and `NativeMenuTests` test, which build windows with
no session list and must see no overlay.

```bash
git add src/LizTerm.App/Controls/SessionSwitcher.axaml src/LizTerm.App/Controls/SessionSwitcher.axaml.cs src/LizTerm.App/Views/SessionWindow.axaml src/LizTerm.App/Views/SessionWindow.axaml.cs tests/LizTerm.App.Tests/Views/SessionSwitcherTests.cs
git commit -m "Add the session switcher overlay to the session window (#46)" -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Task 7: The Window menu

**Files:**
- Create: `src/LizTerm.App/Menus/SessionMenuItems.cs`
- Modify: `src/LizTerm.App/Views/SessionWindow.axaml` (a `_Window` menu between `_Keys` and `_Help`, in both menus)
- Modify: `src/LizTerm.App/Views/SessionWindow.axaml.cs`
- Modify: `tests/LizTerm.App.Tests/Fakes/TestSessions.cs`
- Modify: `tests/LizTerm.App.Tests/Views/NativeMenuTests.cs`
- Test: `tests/LizTerm.App.Tests/Views/WindowMenuTests.cs`

**Interfaces:**
- Consumes: `SessionList`, `SessionEntry` (Task 2); `SessionMenuLabel.For` (Task 3); `SessionWindow.AttachSessions`,
  `ToggleSwitcher`, `Switcher`, the `OnPropertyChanged` override (Task 6).
- Produces:
  - `internal static class SessionMenuItems` — `NativeMenuItem Native(SessionList sessions, SessionEntry entry, Action<SessionEntry> choose)`,
    `MenuItem Classic(SessionList sessions, SessionEntry entry, Action<SessionEntry> choose)`. Task 8's Dock menu uses `Native`.
  - Declared Window menu headers: `_Minimize`, `_Zoom`, `_Keep on Top`, `_Switch Session...`, `_Bring All to Front`.
    Classic names: `WindowMenuItem`, `MinimizeMenuItem`, `ZoomMenuItem`, `MinimizeSeparator`, `KeepOnTopMenuItem`,
    `SwitchSessionMenuItem`, `SessionsSeparator`.
  - Tests: `TestSessions.Attach(SessionWindow window, SessionViewModel vm, params string[] others)` returning
    `(SessionList List, SessionEntry Own, List<(SessionEntry Entry, FakeEmulatorSession Session, FakeSessionHost Host)> Others)`.

- [ ] **Step 1: Add the attach helper to `TestSessions`**

In `tests/LizTerm.App.Tests/Fakes/TestSessions.cs`, add `using LizTerm.App.Views;` and this method inside the class:

```csharp
    /// <summary>Joins a real window to a new list as its first session, followed by fake-hosted sessions named
    /// <paramref name="others"/>, in that opening order. Works before or after Show(), as App's order is before.</summary>
    public static (SessionList List, SessionEntry Own, List<(SessionEntry Entry, FakeEmulatorSession Session, FakeSessionHost Host)> Others) Attach(
        SessionWindow window, SessionViewModel vm, params string[] others)
    {
        var list = new SessionList();
        var own = new SessionEntry(vm, new ProfileRow(vm.Profile, TagRegistry.Empty), true, window);
        list.Add(own);
        var created = others.Select(name => Create(name)).ToList();
        foreach (var other in created) list.Add(other.Entry);
        window.AttachSessions(list, own);
        return (list, own, created);
    }
```

- [ ] **Step 2: Write the failing Window menu tests**

`tests/LizTerm.App.Tests/Views/WindowMenuTests.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Controls;
using Avalonia.Controls.Platform;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using LizTerm.App.Menus;
using LizTerm.App.Sessions;
using LizTerm.App.Tests.Fakes;
using LizTerm.App.ViewModels;
using LizTerm.App.Views;
using LizTerm.Core.Session;
using LizTerm.Core.Settings;

namespace LizTerm.App.Tests.Views;

/// <summary>The Window menu (session switching spec §7) in a real headless window: TSO is this window, CONSOLE and
/// IMON sit behind fake hosts. Native style on macOS unless a test names another shape, because only there are the
/// native items attached to the window's menu.</summary>
public class WindowMenuTests
{
    private sealed record Rig(SessionWindow Window, SessionList List, SessionEntry Own,
        SessionEntry Console, FakeEmulatorSession ConsoleSession, FakeSessionHost ConsoleHost,
        SessionEntry Imon, FakeSessionHost ImonHost);

    private static Rig Show(MenuStyle style = MenuStyle.Native, bool isMacOS = true)
    {
        var session = new FakeEmulatorSession
        {
            Profile = new SessionProfile { Name = "TSO", Host = "tk5.local", Port = 3270 },
            ConnectionState = ConnectionState.Connected3270,
        };
        var vm = new SessionViewModel(session, action => action(), new FakeTextClipboard());
        var window = new SessionWindow(style, isMacOS) { DataContext = vm };
        var (list, own, others) = TestSessions.Attach(window, vm, "CONSOLE", "IMON");
        window.Show();
        return new Rig(window, list, own, others[0].Entry, others[0].Session, others[0].Host, others[1].Entry, others[1].Host);
    }

    private static NativeMenu WindowMenu(SessionWindow window) =>
        MenuLookup.Item(NativeMenu.GetMenu(window), "_Window")?.Menu
        ?? throw new InvalidOperationException("no native Window menu");

    private static NativeMenuItem Native(SessionWindow window, string header) =>
        MenuLookup.Item(WindowMenu(window), header) ?? throw new InvalidOperationException($"no native _Window > {header}");

    /// <summary>The generated rows: everything after the menu's last separator.</summary>
    private static List<NativeMenuItem> NativeRows(SessionWindow window)
    {
        var items = WindowMenu(window).Items.ToList();
        var last = items.FindLastIndex(item => item is NativeMenuItemSeparator);
        return [.. items.Skip(last + 1).OfType<NativeMenuItem>()];
    }

    private static List<MenuItem> ClassicRows(SessionWindow window)
    {
        var menu = window.FindControl<MenuItem>("WindowMenuItem")!;
        var separator = window.FindControl<Separator>("SessionsSeparator")!;
        return [.. menu.Items.Cast<object>().SkipWhile(item => !ReferenceEquals(item, separator)).Skip(1).OfType<MenuItem>()];
    }

    private static void Click(NativeMenuItem item) => ((INativeMenuItemExporterEventsImplBridge)item).RaiseClicked();

    [AvaloniaFact]
    public void The_window_menu_declares_its_fixed_items_in_order()
    {
        var rig = Show();

        var fixedHeaders = WindowMenu(rig.Window).Items
            .TakeWhile(item => !ReferenceEquals(item, WindowMenu(rig.Window).Items.OfType<NativeMenuItemSeparator>().Last()))
            .OfType<NativeMenuItem>().Where(item => item is not NativeMenuItemSeparator).Select(item => item.Header);

        Assert.Equal(["_Minimize", "_Zoom", "_Keep on Top", "_Switch Session...", "_Bring All to Front"], fixedHeaders);
    }

    [AvaloniaFact]
    public void Each_open_session_gets_a_numbered_row_in_opening_order_in_both_menus()
    {
        var rig = Show();
        string[] expected = ["_1  TSO - tk5.local", "_2  CONSOLE - host.local", "_3  IMON - host.local"];

        Assert.Equal(expected, NativeRows(rig.Window).Select(item => item.Header));
        Assert.Equal(expected, ClassicRows(rig.Window).Select(item => item.Header as string));
    }

    [AvaloniaFact]
    public void The_check_mark_follows_the_current_session()
    {
        var rig = Show();

        rig.List.Activated(rig.Imon);

        Assert.Equal([false, false, true], NativeRows(rig.Window).Select(item => item.IsChecked));
        Assert.Equal([false, false, true], ClassicRows(rig.Window).Select(item => item.IsChecked));
    }

    [AvaloniaFact]
    public void Clicking_a_row_brings_its_session()
    {
        var rig = Show();

        Click(NativeRows(rig.Window)[1]);

        Assert.Equal(1, rig.ConsoleHost.BringCount);
    }

    /// <summary>The in-window renderer toggles a check box's mark before the click arrives, and bringing the window
    /// that is already current changes nothing that would rebuild the rows — so the handler puts the marks back.
    /// A raised ClickEvent skips the toggle, so the test does the toggle itself first.</summary>
    [AvaloniaFact]
    public void Clicking_the_current_row_leaves_its_mark_on()
    {
        var rig = Show();
        rig.List.Activated(rig.Own);
        var row = ClassicRows(rig.Window)[0];
        row.IsChecked = false;

        row.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

        Assert.True(ClassicRows(rig.Window)[0].IsChecked);
    }

    [AvaloniaFact]
    public void The_rows_rebuild_when_a_session_disconnects_or_closes()
    {
        var rig = Show();

        rig.ConsoleSession.RaiseConnection(ConnectionState.Disconnected);
        Assert.Equal("_2  CONSOLE - host.local (Disconnected)", NativeRows(rig.Window)[1].Header);

        rig.List.Remove(rig.Console);
        Assert.Equal(["_1  TSO - tk5.local", "_2  IMON - host.local"], NativeRows(rig.Window).Select(item => item.Header));
        Assert.Equal(["_1  TSO - tk5.local", "_2  IMON - host.local"], ClassicRows(rig.Window).Select(item => item.Header as string));
    }

    [AvaloniaFact]
    public void A_window_with_no_session_list_hides_the_sessions_separator()
    {
        var window = new SessionWindow(MenuStyle.Native, isMacOS: true)
        {
            DataContext = new SessionViewModel(new FakeEmulatorSession(), action => action(), new FakeTextClipboard()),
        };
        window.Show();

        Assert.Empty(NativeRows(window));
        Assert.False(WindowMenu(window).Items.OfType<NativeMenuItemSeparator>().Last().IsVisible);
        Assert.False(window.FindControl<Separator>("SessionsSeparator")!.IsVisible);
    }

    [AvaloniaFact]
    public void Keep_on_top_toggles_topmost_and_both_marks()
    {
        var rig = Show();
        var classic = rig.Window.FindControl<MenuItem>("KeepOnTopMenuItem")!;

        Click(Native(rig.Window, "_Keep on Top"));
        Assert.True(rig.Window.Topmost);
        Assert.True(Native(rig.Window, "_Keep on Top").IsChecked);
        Assert.True(classic.IsChecked);

        classic.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Assert.False(rig.Window.Topmost);
        Assert.False(Native(rig.Window, "_Keep on Top").IsChecked);
        Assert.False(classic.IsChecked);
    }

    [AvaloniaFact]
    public void Bring_all_to_front_brings_every_other_session()
    {
        var rig = Show();

        Click(Native(rig.Window, "_Bring All to Front"));

        Assert.Equal(1, rig.ConsoleHost.BringCount);
        Assert.Equal(1, rig.ImonHost.BringCount);
    }

    [AvaloniaFact]
    public void Switch_session_opens_the_switcher()
    {
        var rig = Show();

        Click(Native(rig.Window, "_Switch Session..."));

        Assert.True(rig.Window.Switcher!.IsOpen);
    }

    [AvaloniaFact]
    public void Minimize_and_zoom_change_the_window_state()
    {
        var rig = Show();

        Click(Native(rig.Window, "_Minimize"));
        Assert.Equal(WindowState.Minimized, rig.Window.WindowState);

        rig.Window.Bring();
        Click(Native(rig.Window, "_Zoom"));
        Assert.Equal(WindowState.Maximized, rig.Window.WindowState);

        Click(Native(rig.Window, "_Zoom"));
        Assert.Equal(WindowState.Normal, rig.Window.WindowState);
    }

    /// <summary>Off macOS the title bar has both, so the in-window menu hides them and their separator. The native
    /// items are only attached on macOS, so they are checked there.</summary>
    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void Minimize_and_zoom_are_shown_only_on_macos(bool isMacOS)
    {
        var rig = Show(MenuStyle.Both, isMacOS);

        Assert.Equal(isMacOS, rig.Window.FindControl<MenuItem>("MinimizeMenuItem")!.IsVisible);
        Assert.Equal(isMacOS, rig.Window.FindControl<MenuItem>("ZoomMenuItem")!.IsVisible);
        Assert.Equal(isMacOS, rig.Window.FindControl<Separator>("MinimizeSeparator")!.IsVisible);
        if (isMacOS)
        {
            Assert.True(Native(rig.Window, "_Minimize").IsVisible);
            Assert.True(Native(rig.Window, "_Zoom").IsVisible);
        }
    }

    /// <summary>Switch Session... carries the command chord in both menus (Find's arrangement); Minimize carries Cmd+M
    /// on the native item only, since nothing else would dispatch it (spec §6, §7.4).</summary>
    [AvaloniaFact]
    public void Switch_session_carries_the_command_chord_and_minimize_carries_cmd_m()
    {
        var rig = Show();
        var command = rig.Window.GetPlatformSettings()!.HotkeyConfiguration.CommandModifiers;

        Assert.Equal(new KeyGesture(Key.K, command), Native(rig.Window, "_Switch Session...").Gesture);
        Assert.Equal(new KeyGesture(Key.K, command), rig.Window.FindControl<MenuItem>("SwitchSessionMenuItem")!.InputGesture);
        Assert.Equal(new KeyGesture(Key.M, KeyModifiers.Meta), Native(rig.Window, "_Minimize").Gesture);
        Assert.Null(rig.Window.FindControl<MenuItem>("MinimizeMenuItem")!.InputGesture);
    }
}
```

- [ ] **Step 3: Update `NativeMenuTests` for the new menu**

In `tests/LizTerm.App.Tests/Views/NativeMenuTests.cs`:

1. Rename `The_window_menu_has_the_same_five_top_level_menus_as_the_classic_one` to
   `The_window_menu_has_the_same_six_top_level_menus_as_the_classic_one`, and change its expected array to
   `["_File", "_Edit", "_View", "_Keys", "_Window", "_Help"]`.

2. Replace `Only_the_edit_menu_carries_gestures` and `AssertNoGestures` with:

```csharp
    /// <summary>Gestures come from the platform hotkey table, not a hardcoded modifier, so macOS shows Cmd and
    /// the others Ctrl from the one table ShowPlatformGestures already reads for the classic menu. Outside Edit
    /// exactly two items carry one, both Cmd chords no 3270 keystroke uses (session switching spec §6):
    /// Window &gt; Switch Session... and, on macOS, Window &gt; Minimize.</summary>
    [AvaloniaFact]
    public void Only_edit_and_two_window_items_carry_gestures()
    {
        var (window, _, _, _) = Show();
        var hotkeys = window.GetPlatformSettings()!.HotkeyConfiguration;

        Assert.Equal(hotkeys.Copy.FirstOrDefault(), Item(window, "_Edit", "_Copy").Gesture);
        Assert.Equal(hotkeys.Paste.FirstOrDefault(), Item(window, "_Edit", "_Paste").Gesture);
        Assert.Equal(hotkeys.SelectAll.FirstOrDefault(), Item(window, "_Edit", "Select _All").Gesture);
        Assert.Equal(new KeyGesture(Key.K, hotkeys.CommandModifiers), Item(window, "_Window", "_Switch Session...").Gesture);
        Assert.Equal(new KeyGesture(Key.M, KeyModifiers.Meta), Item(window, "_Window", "_Minimize").Gesture);

        // A gesture anywhere else is a 3270 client that cannot send that key, silently, with nothing in the wire
        // log: an AppKit key equivalent is dispatched ahead of the key window's responder chain, so TerminalScreen
        // never sees it. Walked exhaustively rather than from a list of examples — a list only covers the items
        // someone remembered to add to it, and View > Crosshair is precisely the submenu one would have missed.
        foreach (var top in NativeMenu.GetMenu(window)!.Items.OfType<NativeMenuItem>().Where(i => i.Header != "_Edit"))
            AssertNoGestures(top.Header!, top.Menu!);
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
```

3. Directly after `The_native_menu_matches_the_classic_menu_item_for_item`, add:

```csharp
    /// <summary>The session rows are built in code, in both menus from one list; with sessions open the item-for-item
    /// walk must still hold (session switching spec §10.2).</summary>
    [AvaloniaFact]
    public void The_native_menu_matches_the_classic_menu_item_for_item_with_sessions_open()
    {
        var (window, vm, _, _) = Show();
        TestSessions.Attach(window, vm, "CONSOLE", "IMON");

        AssertParity(window);
    }
```

4. Directly after `Every_native_item_can_actually_be_activated`, add:

```csharp
    /// <summary>The generated session rows are leaves too, and each needs its Click handler.</summary>
    [AvaloniaFact]
    public void Every_native_item_can_actually_be_activated_with_sessions_open()
    {
        var (window, vm, _, _) = Show();
        TestSessions.Attach(window, vm, "CONSOLE", "IMON");

        foreach (var top in NativeMenu.GetMenu(window)!.Items.OfType<NativeMenuItem>())
            AssertEveryLeafIsActivatable(top.Header!, top.Menu!);
    }
```

- [ ] **Step 4: Run the tests to verify they fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~WindowMenuTests|FullyQualifiedName~NativeMenuTests"`
Expected: FAIL — `WindowMenuTests` does not compile against the missing `WindowMenuItem`, and the renamed
`NativeMenuTests` find no `_Window` menu.

- [ ] **Step 5: Create the menu item builder**

`src/LizTerm.App/Menus/SessionMenuItems.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Controls;
using LizTerm.App.Sessions;

namespace LizTerm.App.Menus;

/// <summary>One open session as a menu item, for the Window menu's two renderers and the Dock menu (session
/// switching spec §7.2, §8). Both shapes come from here so they cannot drift: the same label, a check box marked
/// on the current session, and a Click handler — which the macOS exporter requires, since these carry no Command.</summary>
internal static class SessionMenuItems
{
    public static NativeMenuItem Native(SessionList sessions, SessionEntry entry, Action<SessionEntry> choose)
    {
        var item = new NativeMenuItem
        {
            Header = SessionMenuLabel.For(entry, sessions.PositionOf(entry)),
            ToggleType = MenuItemToggleType.CheckBox,
            IsChecked = ReferenceEquals(entry, sessions.Current),
        };
        item.Click += (_, _) => choose(entry);
        return item;
    }

    public static MenuItem Classic(SessionList sessions, SessionEntry entry, Action<SessionEntry> choose)
    {
        var item = new MenuItem
        {
            Header = SessionMenuLabel.For(entry, sessions.PositionOf(entry)),
            ToggleType = MenuItemToggleType.CheckBox,
            IsChecked = ReferenceEquals(entry, sessions.Current),
        };
        item.Click += (_, _) => choose(entry);
        return item;
    }
}
```

- [ ] **Step 6: Declare the Window menu in both renderers**

In `src/LizTerm.App/Views/SessionWindow.axaml`, in the `NativeMenu`, insert between the `_Keys` `NativeMenuItem`'s
closing `</NativeMenuItem>` and `<NativeMenuItem Header="_Help">`:

```xml
      <!-- The Window menu (#46; session switching spec §7). The fixed items are declared here in both renderers, so
           the parity walk covers them; one numbered row per open session is added after the last separator by
           RebuildSessionRows. Minimize and Zoom are macOS's and hidden elsewhere with their separator
           (ApplyPlatformMenuRules). Every item has a Click handler: the exporter greys out anything else. No
           gestures here in XAML: ShowPlatformGestures sets Switch Session's command chord and Minimize's Cmd+M,
           the two exceptions to "no gesture outside Edit". -->
      <NativeMenuItem Header="_Window">
        <NativeMenuItem.Menu>
          <NativeMenu>
            <NativeMenuItem Header="_Minimize" Click="OnMinimizeClickNative" />
            <NativeMenuItem Header="_Zoom" Click="OnZoomClickNative" />
            <NativeMenuItemSeparator />
            <NativeMenuItem Header="_Keep on Top" ToggleType="CheckBox" Click="OnKeepOnTopClickNative" />
            <NativeMenuItem Header="_Switch Session..." Click="OnSwitchSessionClickNative" />
            <NativeMenuItemSeparator />
            <NativeMenuItem Header="_Bring All to Front" Click="OnBringAllToFrontClickNative" />
            <NativeMenuItemSeparator />
          </NativeMenu>
        </NativeMenuItem.Menu>
      </NativeMenuItem>
```

In the classic `Menu`, insert between the `_Keys` `MenuItem`'s closing `</MenuItem>` and `<MenuItem Header="_Help">`:

```xml
      <MenuItem x:Name="WindowMenuItem" Header="_Window">
        <MenuItem x:Name="MinimizeMenuItem" Header="_Minimize" Click="OnMinimizeClick" />
        <MenuItem x:Name="ZoomMenuItem" Header="_Zoom" Click="OnZoomClick" />
        <Separator x:Name="MinimizeSeparator" />
        <MenuItem x:Name="KeepOnTopMenuItem" Header="_Keep on Top" ToggleType="CheckBox" Click="OnKeepOnTopClick" />
        <MenuItem x:Name="SwitchSessionMenuItem" Header="_Switch Session..." Click="OnSwitchSessionClick" />
        <Separator />
        <MenuItem Header="_Bring All to Front" Click="OnBringAllToFrontClick" />
        <Separator x:Name="SessionsSeparator" />
      </MenuItem>
```

- [ ] **Step 7: Wire the menu in code-behind**

In `src/LizTerm.App/Views/SessionWindow.axaml.cs`:

1. Add these fields beside `_stashedMenuItems`:

```csharp
    /// <summary>The native Window menu's own NativeMenu and the items the code changes, held by reference because
    /// under InWindow the top-level items are stashed out of the window's menu (MenuLookup cannot reach them) and
    /// must still be kept current for when they come back. NativeMenuItem takes no x:Name, so they are found by
    /// header once, in the constructor.</summary>
    private readonly NativeMenu _nativeWindowMenu;
    private readonly NativeMenuItem _nativeMinimize;
    private readonly NativeMenuItem _nativeZoom;
    private readonly NativeMenuItemSeparator _nativeMinimizeSeparator;
    private readonly NativeMenuItem _nativeKeepOnTop;
    private readonly NativeMenuItemSeparator _nativeSessionsSeparator;

    /// <summary>The generated session rows, both renderers' items with the session each stands for.</summary>
    private readonly List<(NativeMenuItem Native, MenuItem Classic, SessionEntry Entry)> _sessionRows = [];
```

2. In the constructor, directly after `InitializeComponent();`, add:

```csharp
        _nativeWindowMenu = MenuLookup.Item(NativeMenu.GetMenu(this), "_Window")!.Menu!;
        _nativeMinimize = MenuLookup.Item(_nativeWindowMenu, "_Minimize")!;
        _nativeZoom = MenuLookup.Item(_nativeWindowMenu, "_Zoom")!;
        _nativeMinimizeSeparator = _nativeWindowMenu.Items.OfType<NativeMenuItemSeparator>().First();
        _nativeKeepOnTop = MenuLookup.Item(_nativeWindowMenu, "_Keep on Top")!;
        _nativeSessionsSeparator = _nativeWindowMenu.Items.OfType<NativeMenuItemSeparator>().Last();
        RebuildSessionRows();
```

3. In the constructor's `Opened` handler, directly after `if (_styleSource is not null) _styleSource.PropertyChanged += OnSettingsChanged;`, add:

```csharp
            // Subscribed here for the reason the settings are: a window never shown never raises Closed.
            if (_sessions is not null)
            {
                _sessions.Changed += OnSessionsChanged;
                RebuildSessionRows();
            }
```

4. In `AttachSessions` (Task 6), directly before `Activated += (_, _) => sessions.Activated(own);`, add:

```csharp
        RebuildSessionRows();
        // Attached after Opened (a test can do this), the Opened handler has already run.
        if (_opened) sessions.Changed += OnSessionsChanged;
```

5. At the end of `ApplyPlatformMenuRules`, add:

```csharp
        // Minimize and Zoom are macOS's (session switching spec §7.4); elsewhere the title bar has both. Unlike About
        // and Preferences above this reads _isMacOS, the constructor's platform, so a test can build either shape on
        // any machine. Set on the held references, so it holds for stashed native items too.
        foreach (var item in new NativeMenuItem[] { _nativeMinimize, _nativeZoom, _nativeMinimizeSeparator }) item.IsVisible = _isMacOS;
        MinimizeMenuItem.IsVisible = _isMacOS;
        ZoomMenuItem.IsVisible = _isMacOS;
        MinimizeSeparator.IsVisible = _isMacOS;
```

6. At the end of `ShowPlatformGestures`, directly before the local `static void Gesture(...)` function, add:

```csharp
        // The session switcher's chord (spec §6), Find's arrangement: a real key equivalent on the native item, display
        // text on the classic one, and TerminalScreen.SwitcherRequested wherever no key equivalent is installed. A Cmd
        // chord cannot be a 3270 keystroke, which is why this and Minimize are the two exceptions outside Edit.
        var switcher = new KeyGesture(Key.K, hotkeys.CommandModifiers);
        SwitchSessionMenuItem.InputGesture = switcher;
        if (MenuLookup.Required(menu, "_Window", "_Switch Session...") is { } nativeSwitch) nativeSwitch.Gesture = switcher;
        // Cmd+M on macOS, native only: nothing else dispatches it, so the in-window item names no chord it cannot keep.
        if (_isMacOS && MenuLookup.Required(menu, "_Window", "_Minimize") is { } nativeMinimize)
            nativeMinimize.Gesture = new KeyGesture(Key.M, KeyModifiers.Meta);
```

   And in that method's summary, replace `is why nothing on File, Keys or Help carries one.` with
   `is why nothing on File, View, Keys or Help carries one, and Window only its two Cmd chords.`

7. Extend the `OnPropertyChanged` override from Task 6 so the marks follow `Topmost` however it changed:

```csharp
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != TopmostProperty) return;
        ShowKeepOnTopMarks();
        KeepOnTopChanged?.Invoke(this, EventArgs.Empty);
    }
```

8. Add the handlers and helpers after `ChooseSession` (Task 6):

```csharp
    private void OnSessionsChanged(object? sender, EventArgs e) => RebuildSessionRows();

    /// <summary>One row per open session after the sessions separator, in both menus from one list (spec §7.2).
    /// Removed from the end one at a time and added to the same NativeMenu instance: never Clear(), never a new
    /// menu (#60).</summary>
    private void RebuildSessionRows()
    {
        while (_nativeWindowMenu.Items.Count > 0 && !ReferenceEquals(_nativeWindowMenu.Items[^1], _nativeSessionsSeparator))
            _nativeWindowMenu.Items.RemoveAt(_nativeWindowMenu.Items.Count - 1);
        while (WindowMenuItem.Items.Count > 0 && !ReferenceEquals(WindowMenuItem.Items[^1], SessionsSeparator))
            WindowMenuItem.Items.RemoveAt(WindowMenuItem.Items.Count - 1);
        _sessionRows.Clear();

        if (_sessions is { } sessions)
        {
            foreach (var entry in sessions.Entries)
            {
                var native = SessionMenuItems.Native(sessions, entry, BringFromMenu);
                var classic = SessionMenuItems.Classic(sessions, entry, BringFromMenu);
                _nativeWindowMenu.Items.Add(native);
                WindowMenuItem.Items.Add(classic);
                _sessionRows.Add((native, classic, entry));
            }
        }

        // Nothing collapses a trailing separator, so it goes when there is nothing under it.
        _nativeSessionsSeparator.IsVisible = _sessionRows.Count > 0;
        SessionsSeparator.IsVisible = _sessionRows.Count > 0;
    }

    /// <summary>Brings the session, then puts every mark back: both renderers write IsChecked before the click
    /// arrives, and bringing the session that is already current raises no Changed to rebuild them (Keys &gt;
    /// Insert's pattern).</summary>
    private void BringFromMenu(SessionEntry entry)
    {
        entry.Host.Bring();
        var current = _sessions?.Current;
        foreach (var (native, classic, rowEntry) in _sessionRows)
        {
            native.IsChecked = ReferenceEquals(rowEntry, current);
            classic.IsChecked = ReferenceEquals(rowEntry, current);
        }
    }

    // Two one-line handlers per action: MenuItem.Click and NativeMenuItem.Click have different delegate shapes.
    private void OnMinimizeClick(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void OnMinimizeClickNative(object? sender, EventArgs e) => WindowState = WindowState.Minimized;
    private void OnZoomClick(object? sender, RoutedEventArgs e) => ToggleZoom();
    private void OnZoomClickNative(object? sender, EventArgs e) => ToggleZoom();
    private void OnKeepOnTopClick(object? sender, RoutedEventArgs e) => ToggleKeepOnTop();
    private void OnKeepOnTopClickNative(object? sender, EventArgs e) => ToggleKeepOnTop();
    private void OnSwitchSessionClick(object? sender, RoutedEventArgs e) => ToggleSwitcher();
    private void OnSwitchSessionClickNative(object? sender, EventArgs e) => ToggleSwitcher();
    private void OnBringAllToFrontClick(object? sender, RoutedEventArgs e) => _sessions?.BringAllToFront();
    private void OnBringAllToFrontClickNative(object? sender, EventArgs e) => _sessions?.BringAllToFront();

    /// <summary>macOS's Zoom: between maximised and normal.</summary>
    private void ToggleZoom() =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    /// <summary>Per window and never saved (spec §7.3). The marks are put back after the flip because both renderers
    /// toggle them before the click arrives; OnPropertyChanged keeps them in step with Topmost from anywhere else.</summary>
    private void ToggleKeepOnTop()
    {
        Topmost = !Topmost;
        ShowKeepOnTopMarks();
    }

    private void ShowKeepOnTopMarks()
    {
        KeepOnTopMenuItem.IsChecked = Topmost;
        _nativeKeepOnTop.IsChecked = Topmost;
    }
```

9. In `OnClosed`, change the two lines Task 6 added so the window stops listening before it removes itself:

```csharp
        // Before base.OnClosed raises Closed: App's handler asks ShutdownPolicy with the count of sessions left.
        if (Switcher is { IsOpen: true }) Switcher.Close();
        if (_sessions is not null && _ownEntry is not null)
        {
            _sessions.Changed -= OnSessionsChanged;
            _sessions.Remove(_ownEntry);
        }
```

- [ ] **Step 8: Run the tests to verify they pass**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~WindowMenuTests|FullyQualifiedName~NativeMenuTests|FullyQualifiedName~SessionSwitcherTests"`
Expected: PASS.

- [ ] **Step 9: Run the App tests and commit**

Run: `dotnet test tests/LizTerm.App.Tests`
Expected: PASS.

```bash
git add src/LizTerm.App/Menus/SessionMenuItems.cs src/LizTerm.App/Views/SessionWindow.axaml src/LizTerm.App/Views/SessionWindow.axaml.cs tests/LizTerm.App.Tests/Fakes/TestSessions.cs tests/LizTerm.App.Tests/Views/NativeMenuTests.cs tests/LizTerm.App.Tests/Views/WindowMenuTests.cs
git commit -m "Add a Window menu listing the open sessions (#46)" -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Task 8: The macOS Dock menu

**Files:**
- Create: `src/LizTerm.App/Menus/DockMenu.cs`
- Test: `tests/LizTerm.App.Tests/Menus/DockMenuTests.cs`

**Interfaces:**
- Consumes: `SessionList`, `SessionEntry` (Task 2); `SessionMenuItems.Native` (Task 7).
- Produces: `internal static class DockMenu` — `const string NewSessionHeader` (`"New Session..."`);
  `NativeMenu? Attach(Application app, SessionList sessions, Action newSession, bool isMacOS)`;
  `void Rebuild(NativeMenu menu, SessionList sessions, Action newSession)`.

- [ ] **Step 1: Write the failing tests**

`tests/LizTerm.App.Tests/Menus/DockMenuTests.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Platform;
using Avalonia.Headless.XUnit;
using LizTerm.App.Menus;
using LizTerm.App.Sessions;
using LizTerm.App.Tests.Fakes;

namespace LizTerm.App.Tests.Menus;

/// <summary>The Dock menu (session switching spec §8). [AvaloniaFact] rather than [Fact] because NativeMenu is an
/// AvaloniaObject and belongs to the UI thread. macOS's own Options, Show All Windows, Hide and Quit are appended
/// by the Dock itself and never appear here.</summary>
public class DockMenuTests
{
    private static string?[] Headers(NativeMenu menu) =>
        [.. menu.Items.Select(item => item is NativeMenuItemSeparator ? "---" : ((NativeMenuItem)item).Header)];

    private static void Click(NativeMenuItemBase item) => ((INativeMenuItemExporterEventsImplBridge)item).RaiseClicked();

    [AvaloniaFact]
    public void It_lists_the_sessions_then_a_separator_then_new_session()
    {
        var list = new SessionList();
        list.Add(TestSessions.Create("TSO", "tk5.local").Entry);
        list.Add(TestSessions.Create("IMON", "mvs.local").Entry);
        var menu = new NativeMenu();

        DockMenu.Rebuild(menu, list, () => { });

        Assert.Equal(new string?[] { "_1  TSO - tk5.local", "_2  IMON - mvs.local", "---", "New Session..." }, Headers(menu));
        Assert.True(((NativeMenuItem)menu.Items[0]).IsChecked);
        Assert.False(((NativeMenuItem)menu.Items[1]).IsChecked);
    }

    [AvaloniaFact]
    public void With_no_sessions_it_holds_new_session_alone()
    {
        var menu = new NativeMenu();

        DockMenu.Rebuild(menu, new SessionList(), () => { });

        Assert.Equal(new string?[] { DockMenu.NewSessionHeader }, Headers(menu));
    }

    [AvaloniaFact]
    public void A_session_item_brings_its_session_and_new_session_opens_the_list()
    {
        var list = new SessionList();
        var (tso, _, tsoHost) = TestSessions.Create("TSO");
        list.Add(tso);
        var newSessions = 0;
        var menu = new NativeMenu();
        DockMenu.Rebuild(menu, list, () => newSessions++);

        Click(menu.Items[0]);
        Click(menu.Items[^1]);

        Assert.Equal(1, tsoHost.BringCount);
        Assert.Equal(1, newSessions);
    }

    /// <summary>Attached to the shared test Application, so the menu it had is put back afterwards.</summary>
    [AvaloniaFact]
    public void On_macOS_it_is_attached_and_follows_the_list()
    {
        var app = Application.Current!;
        var previous = NativeDock.GetMenu(app);
        try
        {
            var list = new SessionList();
            var menu = DockMenu.Attach(app, list, () => { }, isMacOS: true);

            Assert.NotNull(menu);
            Assert.Same(menu, NativeDock.GetMenu(app));
            list.Add(TestSessions.Create("TSO", "tk5.local").Entry);
            Assert.Equal(new string?[] { "_1  TSO - tk5.local", "---", "New Session..." }, Headers(menu!));
        }
        finally
        {
            NativeDock.SetMenu(app, previous!);
        }
    }

    [AvaloniaFact]
    public void Off_macOS_it_attaches_nothing()
    {
        var app = Application.Current!;
        var previous = NativeDock.GetMenu(app);

        var menu = DockMenu.Attach(app, new SessionList(), () => { }, isMacOS: false);

        Assert.Null(menu);
        Assert.Same(previous, NativeDock.GetMenu(app));
    }
}
```

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~DockMenuTests"`
Expected: build FAILS — `DockMenu` does not exist.

- [ ] **Step 3: Implement it**

`src/LizTerm.App/Menus/DockMenu.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia;
using Avalonia.Controls;
using LizTerm.App.Sessions;

namespace LizTerm.App.Menus;

/// <summary>The macOS Dock icon's menu (session switching spec §8): the open sessions, then New Session..., so one
/// click from inside another application lands on the right session. Avalonia.Native exports a dock menu from the
/// application (SetupApplicationDockMenuExporter), so it does not depend on which window is in front. macOS
/// appends its own Options, Show All Windows, Hide and Quit below. Windows' taskbar and Linux task lists already
/// list windows by title, so nothing is attached there.</summary>
internal static class DockMenu
{
    public const string NewSessionHeader = "New Session...";

    /// <summary>Builds the menu, keeps it current on every SessionList.Changed, and attaches it — on macOS only; off
    /// macOS it attaches nothing and answers null. The platform is an argument, MenuStrategy.Resolve's shape, so a
    /// test on any OS can take either branch. The subscription lives as long as the process, as the menu does.</summary>
    public static NativeMenu? Attach(Application app, SessionList sessions, Action newSession, bool isMacOS)
    {
        if (!isMacOS) return null;
        var menu = new NativeMenu();
        Rebuild(menu, sessions, newSession);
        sessions.Changed += (_, _) => Rebuild(menu, sessions, newSession);
        NativeDock.SetMenu(app, menu);
        return menu;
    }

    /// <summary>Refills the same instance, removing from the end one at a time (#60).</summary>
    public static void Rebuild(NativeMenu menu, SessionList sessions, Action newSession)
    {
        while (menu.Items.Count > 0) menu.Items.RemoveAt(menu.Items.Count - 1);
        foreach (var entry in sessions.Entries)
            menu.Items.Add(SessionMenuItems.Native(sessions, entry, chosen => chosen.Host.Bring()));
        if (sessions.Count > 0) menu.Items.Add(new NativeMenuItemSeparator());
        var newSessionItem = new NativeMenuItem { Header = NewSessionHeader };
        newSessionItem.Click += (_, _) => newSession();
        menu.Items.Add(newSessionItem);
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~DockMenuTests"`
Expected: PASS, 5 tests.

- [ ] **Step 5: Commit**

```bash
git add src/LizTerm.App/Menus/DockMenu.cs tests/LizTerm.App.Tests/Menus/DockMenuTests.cs
git commit -m "Add a macOS Dock menu listing the open sessions (#46)" -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Task 9: `App` owns the session list

**Files:**
- Modify: `src/LizTerm.App/App.axaml.cs`

**Interfaces:**
- Consumes: `SessionList`, `SessionEntry` (Task 2); `ProfileRow(profile, registry, isSaved)` (Task 1);
  `SessionWindow.AttachSessions` (Task 6); `DockMenu.Attach` (Task 8).
- Produces: nothing new for other tasks. `App._sessions` becomes a `SessionList`; `App._lastActiveSession` is gone.

`App.OpenSession` builds a real backend through `SessionFactory`, so it has no headless test of its own. What it
wires together is covered piece by piece in Tasks 2 and 6–8, the existing `App` tests keep About and the update
check honest, and Task 11's manual pass runs the whole route.

- [ ] **Step 1: Replace the two fields**

In `src/LizTerm.App/App.axaml.cs`, add `using LizTerm.App.Sessions;`, then replace

```csharp
    private readonly List<SessionWindow> _sessions = [];
```

with

```csharp
    /// <summary>The process's one record of open sessions (#46): opening order for the Window and Dock menus' numbers,
    /// use order for the switcher and for "the session the user was last in", which About and the update check read.
    /// Each window adds itself through AttachSessions and removes itself in OnClosed, before Closed is raised.</summary>
    private readonly SessionList _sessions = new();
```

and delete the `_lastActiveSession` field together with its `<summary>` comment.

- [ ] **Step 2: Attach the Dock menu at startup**

In `OnFrameworkInitializationCompleted`, directly after `desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;`, add:

```csharp
            // The Dock icon's menu, on macOS only (#46): the open sessions and New Session..., current for the life
            // of the process.
            DockMenu.Attach(this, _sessions, ShowPicker, OperatingSystem.IsMacOS());
```

- [ ] **Step 3: Register each session as it opens**

In `OpenSession`:

1. Directly after `var store = _store ??= new ProfileStore(AppPaths.ProfilesDirectory());`, add:

```csharp
        // One snapshot of the tag colours for both the window's chips and its row in the switcher, so the two agree.
        // Load never throws (an unreadable file is an empty registry, and every chip draws grey).
        var tags = (_tags ??= new TagRegistryStore(TagRegistryStore.DefaultFile())).Load();
```

2. Replace the view model's last argument and the comment above it:

```csharp
            // Read now, like the profile: the chips' colours are a snapshot of tags.json at open, and a Manage
            // Tags recolour reaches the next window rather than this one. Load never throws (an unreadable file
            // is an empty registry, and every chip draws grey).
            tags: (_tags ??= new TagRegistryStore(TagRegistryStore.DefaultFile())).Load());
```

with

```csharp
            // A snapshot, like the profile: a Manage Tags recolour reaches the next window rather than this one.
            tags: tags);
```

3. Replace these three lines:

```csharp
        _sessions.Add(window);
        _lastActiveSession ??= window;
        window.Activated += (_, _) => _lastActiveSession = window;
```

with

```csharp
        var entry = new SessionEntry(viewModel, new ProfileRow(profile, tags, isSaved: fromStore), fromStore, window);
        _sessions.Add(entry);
        window.AttachSessions(_sessions, entry);
```

4. In the `window.Closed` handler, delete these lines, which the window's own `OnClosed` now does through
   `SessionList.Remove` (and a closed session can no longer be `Current`):

```csharp
            _sessions.Remove(window);
            // A closed window must not keep answering for About; fall back to whichever session is left.
            if (ReferenceEquals(_lastActiveSession, window)) _lastActiveSession = _sessions.LastOrDefault();
```

   The handler's `ShutdownPolicy.UserClosedLastWindow(_quitting, shutdownClose, _sessions.Count)` stays exactly as
   it is: `SessionList.Count` already excludes this window.

- [ ] **Step 4: Read the last session from the list**

1. In `CheckForUpdatesOnStartupAsync(IReleaseChecker, SettingsViewModel)`, replace
   `(Window?)_lastActiveSession ?? _picker` with `(_sessions.Current?.Host as Window) ?? _picker`.

2. Replace the private `AboutEngine(Window? preferredOwner)` overload's body line

```csharp
        AboutEngine(preferredOwner?.DataContext, _lastActiveSession?.DataContext, SessionFactory.CheckBackendOrUnknown);
```

with

```csharp
        AboutEngine(preferredOwner?.DataContext, _sessions.Current?.Session, SessionFactory.CheckBackendOrUnknown);
```

3. Search the file for `_lastActiveSession`; the only matches left should be in comments. Reword each to "the
   session the user was last in (`_sessions.Current`)".

Run: `grep -n "_lastActiveSession" src/LizTerm.App/App.axaml.cs`
Expected: no output.

- [ ] **Step 5: Build, run the App tests, and commit**

Run: `dotnet build LizTerm.slnx`
Expected: Build succeeded.

Run: `dotnet test tests/LizTerm.App.Tests`
Expected: PASS, including `AppUpdateCheckTests` and the `AboutEngine` tests.

```bash
git add src/LizTerm.App/App.axaml.cs
git commit -m "Track open sessions in one SessionList and attach the Dock menu (#46)" -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Task 10: Documentation, and the final gates

Each fact in its one home (root `CLAUDE.md`, "Where things are documented").

**Files:**
- Modify: `docs/user-guide.md`
- Regenerate: `src/LizTerm.App/Assets/Docs/user-guide.html` (never hand-edited)
- Modify: `CHANGELOG.md`
- Modify: `README.md`
- Modify: `src/LizTerm.App/CLAUDE.md`
- Modify: `tests/CLAUDE.md`

**Interfaces:** none; this task names what Tasks 1–9 built.

- [ ] **Step 1: The user guide**

In `docs/user-guide.md`:

1. In the contents list, directly after `- [The session window](#the-session-window)`, add
   `- [Several sessions](#several-sessions)`.

2. Directly before `## Keyboard`, add this section:

```markdown
## Several sessions

Each session has its own window, so with several open they mix in with every other application's windows. There
are three ways back to the one you want:

- **Cmd+K** on macOS, or **Ctrl+K** on Windows and Linux, opens a list of every open session over the current
  window; so does **Window > Switch Session...**. Sessions are numbered in the order you opened them, 1 to 9 and
  then 0 for the tenth: press a number to go straight there. Or type part of a session's name, host, tag or note
  to narrow the list, then use the arrow keys and **Enter**, or click a session. The session you were last in is
  highlighted when the list opens, so **Cmd+K** then **Enter** flips between two sessions. **Escape** closes the
  list. Nothing you type while it is open is sent to the host.
- The **Window** menu lists the same numbered sessions, with a check mark on the one you were last in. On Windows
  and Linux each number is also the item's access key, so **Alt**, **W**, **3** reaches session 3. **Bring All to
  Front** raises every LizTerm session above other applications' windows. On macOS the menu also has **Minimize**
  (Cmd+M) and **Zoom**.
- On macOS, right-click LizTerm's icon in the Dock to see the sessions there too, and choose one to go straight to
  it from any application. **New Session...** in the same menu opens the Sessions list.

In the list, a session shows its star if it is a favourite, then its tags, host and note, a filled circle ● while
it is connected or an open circle ○ and the word **Disconnected** when it is not, and **This window** or **On top**
where they apply. A Quick Connect session shows **Quick Connect** under its name.

**Window > Keep on Top** keeps that window above other applications' windows, for a session you want in view while
you work in another, such as an operator console. It is not remembered: a new window starts without it.
```

3. In *Keyboard*, replace

```markdown
Copy, Paste, Select All and Find use the platform's own shortcuts — Cmd+C, Cmd+V, Cmd+A and Cmd+F on macOS, Ctrl
elsewhere. Ctrl+Insert copies, on every platform, which is why PA1 lives on Alt+1 rather than Vista's Ctrl+Insert.
```

with

```markdown
Copy, Paste, Select All and Find use the platform's own shortcuts — Cmd+C, Cmd+V, Cmd+A and Cmd+F on macOS, Ctrl
elsewhere. Ctrl+Insert copies, on every platform, which is why PA1 lives on Alt+1 rather than Vista's Ctrl+Insert.
The session switcher is Cmd+K on macOS and Ctrl+K elsewhere; see [Several sessions](#several-sessions).
```

4. In *Menus*, directly after the Help list's last item (the two lines beginning
   `- **Help > Check for Updates...** checks now`), add a blank line and:

```markdown
The **Window** menu lists the open sessions and holds **Keep on Top**, **Switch Session...** and **Bring All to
Front**, and on macOS **Minimize** and **Zoom**; see [Several sessions](#several-sessions).
```

- [ ] **Step 2: Regenerate the bundled guide and check it**

Run: `LIZTERM_UPDATE_DOCS=1 dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~UserGuideAssetTests"`
Expected: PASS, and `src/LizTerm.App/Assets/Docs/user-guide.html` now contains `Several sessions`.

Run: `dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~UserGuideAssetTests"`
Expected: PASS without the variable, including `The_guide_uses_no_construct_the_converter_cannot_render`. If that
one fails, the new section used a construct the converter lacks: reword the section with the constructs the rest
of the guide already uses (paragraphs, bullets, bold, in-page links), rather than extending the converter.

- [ ] **Step 3: The changelog**

In `CHANGELOG.md`, directly before `## 0.6.0`, add:

```markdown
## Unreleased

- **Switching between sessions.** Cmd+K on macOS, or Ctrl+K on Windows and Linux, lists every open session over the
  current window: press its number, or type part of its name, host, tag or note. A new **Window** menu lists the
  sessions too, with **Keep on Top** for a session you want in view and **Bring All to Front**, and on macOS the Dock
  icon's menu lists them as well.

```

- [ ] **Step 4: The README**

In `README.md`'s *Features* list, directly after the line
`- **Find on screen**, a **crosshair** cursor, and **screen capture** to text or HTML.`, add:

```markdown
- **A session switcher** (Cmd/Ctrl+K), a **Window** menu and, on macOS, a Dock menu for finding the right session
  among many, with **Keep on Top** for a console you want in view.
```

- [ ] **Step 5: `src/LizTerm.App/CLAUDE.md`**

1. In *Wire log, About and the engine*, replace
   `(`_lastActiveSession`, tracked on `Activated`)` with `(`SessionList.Current`, kept by each window's `Activated`)`.

2. In *Keyboard*, replace

```markdown
- `TryHandlePlatformGesture` checks the platform's copy, paste, select-all **and Find** hotkeys, ahead of the keymap.
  Do not add a Ctrl+F chord to `DefaultKeymap`: it would silently shadow Find on Windows and Linux, where the classic
  menu makes this control the only dispatch path for it.
```

   with

```markdown
- `TryHandlePlatformGesture` checks the platform's copy, paste, select-all **and Find** hotkeys, and the session
  switcher's Cmd/Ctrl+K (#46), ahead of the keymap. Do not add a Ctrl+F or Ctrl+K chord to `DefaultKeymap`: it would
  silently shadow Find or the switcher on Windows and Linux, where the classic menu makes this control the only
  dispatch path for them.
```

3. In *Gestures*, replace `**No window menu item outside Edit ever carries a `Gesture`.**` with
   `**No window menu item outside Edit carries a `Gesture`, apart from Window's two Cmd chords (below).**`

4. In *Gestures*, directly after the Find bullet's last line, `  `PlatformHotkeyConfiguration` has no Find to read.`,
   add:

```markdown
- Window > Switch Session... and Window > Minimize are the window menu's two exceptions outside Edit (#46), both Cmd
  chords no 3270 keystroke uses. Switch Session is Find's arrangement: `ShowPlatformGestures` sets
  `new KeyGesture(Key.K, hotkeys.CommandModifiers)` as the native `Gesture` and the classic `InputGesture`, and
  `TerminalScreen.SwitcherRequested` handles the chord wherever no key equivalent is installed. Minimize's Cmd+M is
  native and macOS only; nothing else dispatches it, so the in-window item names no chord.
  `NativeMenuTests.Only_edit_and_two_window_items_carry_gestures` allows exactly those two.
```

5. Directly before `## Screen capture`, add:

```markdown
## Session switching (#46)

- `SessionList` (`Sessions/`) is the process's one record of open sessions, owned by `App`: opening order for the
  numbers 1–10 in the switcher, the Window menu and the Dock menu, and use order for `Current` and `Previous`. It
  raises one `Changed` on add, remove, a change of `Current`, any listed view model's `Connection`, and any host's
  `KeepOnTopChanged`. Everything that reads it reaches a session only through `ISessionHost.Bring()`, the seam a
  tabbed window would implement (#119). A consumer that casts a host to `SessionWindow` closes that door.
- A window joins through `SessionWindow.AttachSessions(list, entry)` before `Show()`, reports `Activated` to the
  list, and removes itself in `OnClosed` *before* `base.OnClosed` raises `Closed`, because App's `Closed` handler
  asks `ShutdownPolicy` with `_sessions.Count`. `App.AboutEngine` and the startup update check read
  `_sessions.Current`.
- **The switcher overlay's visibility and `DataContext` belong to code-behind.** A window has no
  `SessionSwitcherViewModel` until it is attached, and a failed compiled `IsVisible` binding falls back to true,
  which would dim every window. So `SwitcherPanel` is declared `IsVisible="False"` with a null `DataContext`, and
  only `ToggleSwitcher` and `CloseSwitcher` change it.
- **A jumping digit is taken from the tunnelled `TextInput`, never from `KeyDown`.** Handling the key-down closes the
  switcher and focuses the screen before the platform delivers the digit's text input, which would then type it
  into the host. Up, Down, Enter, Escape and Cmd/Ctrl+K are tunnelled `KeyDown`s.
  `SessionSwitcherTests.Nothing_typed_in_the_switcher_reaches_the_host` is the invariant.
- **`SwitcherBox` is the window's second text field**, and the native Copy, Paste and Select All handlers guard it
  as they guard `FindBox`. It lives in the `SessionSwitcher`'s name scope, so the window reaches it as
  `SwitcherPanel.Box`. Each row's `Border` takes a transparent background from the `row` style: with none it is
  nothing to hit, and a click falls through to the dim.
- **The Window menu's session rows are built in code** (`RebuildSessionRows`, over `Menus/SessionMenuItems`), after
  the last separator in both menus, from one list, on every `Changed`. The window holds references to the native
  Window submenu and its items, found by header in the constructor, because under InWindow the top-level items are
  stashed out of the window's menu where `MenuLookup` cannot see them. Rows come off the end one at a time from the
  same `NativeMenu` (#60), and `BringFromMenu` puts the check marks back after a click, Keys > Insert's pattern.
- Minimize and Zoom are hidden off macOS by `ApplyPlatformMenuRules` reading `_isMacOS`, the constructor's
  platform — unlike About and Preferences, which read the running OS — so a test can build either shape.
- **The Dock menu** is `Menus/DockMenu`, attached to the application with `NativeDock.SetMenu` on macOS only and
  rebuilt on `Changed`. macOS appends Options, Show All Windows, Hide and Quit, which no test sees.
- The Sessions list's row is `App.axaml`'s `ProfileSummaryTemplate`, shared by the picker and the switcher so one
  profile cannot be drawn two ways. It binds `ProfileRow.SecondLine`: the host and port, or `Quick Connect` for an
  unsaved session.
- Only a run on the real app checks these (session switching spec §10.3): Keep on Top above other applications, a
  Dock-menu choice bringing LizTerm forward, Cmd+K firing once under every menu style, a doubled underscore in a
  macOS menu, access keys on Windows and Linux, and Zoom.
```

- [ ] **Step 6: `tests/CLAUDE.md`**

In *App tests*, directly after the bullet
`- Drive native menu items through `((INativeMenuItemExporterEventsImplBridge)item).RaiseClicked()`; the menu notes in`
(and its second line, `  `src/LizTerm.App/CLAUDE.md` say why.`), add:

```markdown
- Session switching tests seed a `SessionList` with `TestSessions`. `Create(name, ...)` builds an entry over a
  `FakeEmulatorSession` and a `FakeSessionHost`, which counts `Bring()` and runs `OnBring`, where a test calls
  `SessionList.Activated` as a real window's activation would. `Attach(window, vm, others...)` joins a real headless
  `SessionWindow` as the first session. The switcher's filter box is in its control's name scope, so reach it as
  `window.FindControl<SessionSwitcher>("SwitcherPanel")!.Box`, and type a jumping digit with `KeyTextInput`, since
  the switcher takes digits from `TextInput`.
```

- [ ] **Step 7: The final gates**

Run: `dotnet test LizTerm.slnx`
Expected: PASS (the live-host tests skip themselves).

Run: `dotnet build LizTerm.slnx --no-incremental 2>&1 | grep -c " warning "`
Expected: `0`. Any warning is fixed in the file it names, not suppressed.

- [ ] **Step 8: Commit**

```bash
git add docs/user-guide.md src/LizTerm.App/Assets/Docs/user-guide.html CHANGELOG.md README.md src/LizTerm.App/CLAUDE.md tests/CLAUDE.md
git commit -m "Document switching between sessions (#46)" -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Task 11: The manual pass on the real app

The headless platform cannot show these (session switching spec §10.3). They are Robert's to run, on the real app;
the executor prepares the build and hands this list over rather than claiming any of it. Nothing here is committed.

**Files:** none.

- [ ] **Step 1: Build and launch the app**

Run: `dotnet run --project src/LizTerm.App`
Expected: the splash, then the Sessions list. A fresh worktree needs an engine first; see `docs/development.md`.

- [ ] **Step 2: Hand Robert this checklist**

Open three sessions from saved profiles, at least one marked FAVORITE and one with a note. Each check names what
passing looks like.

1. **The switcher.** In a session press Cmd+K (macOS) or Ctrl+K (Windows, Linux). Pass: a palette at the top centre
   over a dimmed screen lists all three, numbered 1–3 in the order opened, with star, chips, host and note; the
   previously used session is highlighted; Enter goes to it; a digit jumps; typing part of a note filters; Escape
   returns to the screen; nothing typed reached the host (the host's screen is unchanged).
2. **Keep on Top floats above other applications.** Window > Keep on Top in one session, then click into another
   application. Pass on macOS: the LizTerm window stays above that application's windows. On Linux, record what the
   window manager did (GNOME at least); some ignore it.
3. **The Dock menu brings LizTerm forward.** On macOS, with another application frontmost and one LizTerm session
   minimised, right-click LizTerm in the Dock and choose that session. Pass: LizTerm comes forward and the minimised
   session is restored in front.
4. **Cmd+K fires once under every menu style.** On macOS, in Preferences > Window > Menu bar, try In the system menu
   bar, Inside the window, and Both. Pass: under each, Cmd+K opens the switcher and a second Cmd+K closes it, and
   nothing reaches the host.
5. **A doubled underscore.** Rename a profile to include `_` (for example `MVS_PROD`) and open it. Pass: the macOS
   Window menu and the Dock menu show one underscore, with no letter underlined or lost.
6. **Access keys.** On Windows or Linux, press Alt, W, then 2. Pass: session 2 comes forward.
7. **Zoom.** On macOS, Window > Zoom. Pass: the window zooms as the green button's zoom does, and Zoom again returns it.
8. **Bring All to Front.** Put an application's window over all three sessions, then Window > Bring All to Front.
   Pass: all three LizTerm sessions come above it, with the one you were in on top.

- [ ] **Step 3: Record the results**

Record each result, and any failure with what was seen, as a comment on the pull request. A failure in 2, 3 or 6
that is the platform's behaviour rather than LizTerm's goes into `docs/user-guide.md`'s *Known limitations*; any
other failure becomes a fix, with a test where headless can reach it.
