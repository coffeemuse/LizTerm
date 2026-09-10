# LizTerm tier-1 connection milestone — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close the four remaining tier-1 issues — #29 Quick Connect, #30 oversize geometry, #37 keep-alive, #28 auto-reconnect — as one milestone that opens the profile schema once.

**Architecture:** Three flat fields on `SessionProfile` and one new pure Core type (`OversizeGeometry`). Two of the three settings reach the engine through `BuildArguments`' argv; `reconnect` is a runtime `Set`, armed only after a connect has succeeded and disarmed before every disconnect. The App gains two profile-editor rows, a Quick Connect row in the picker, and a Save as Profile item on the session window. No new interfaces: `B3270Session` reads `Profile.AutoReconnect` itself, so `IEmulatorSession` and `FakeEmulatorSession` are untouched.

**Tech Stack:** .NET 10, Avalonia 12.1.2, CommunityToolkit.Mvvm, xunit.v3 (VSTest mode), Avalonia headless test platform.

**Spec:** `docs/superpowers/specs/2026-09-10-lizterm-tier1-connection-design.md`

## Global Constraints

- **Dependency rule.** `LizTerm.Core` depends only on the BCL and never mentions Avalonia or b3270 names. `LizTerm.App` names `LizTerm.Backend.B3270` in exactly one place: `src/LizTerm.App/SessionFactory.cs`. Do not add a second.
- **License header.** Every hand-written `.cs` and `.axaml` file under `src/` and `tests/` carries three lines — `This file is part of LizTerm.`, `Copyright 2026 by CoffeeMuse`, `SPDX-License-Identifier: BSD-3-Clause` — in that file's comment syntax, within the first eight lines (before the root element in an `.axaml`). `RepositoryHeadersTests` fails the suite otherwise.
- **Zero warnings.** `dotnet build LizTerm.slnx --no-incremental 2>&1 | grep -c " warning "` must print `0` before anything is called done. An incremental build hides warnings from projects it did not recompile.
- **Status strings are exact.** Assertions on user-visible status text are exact string matches; change `StatusFormatter` and its tests in the same commit.
- **No `Gesture` on any menu item outside Edit.** A `NativeMenuItem` gesture becomes an AppKit key equivalent dispatched before the key window's responder chain, which silently swallows the keystroke from `TerminalScreen`.
- **Both menus change together.** `NativeMenuTests.The_native_menu_matches_the_classic_menu_item_for_item` compares headers, separator positions and — **for every menu except `_Edit`** — `Command` by reference. A File item driven by `Click` natively must be driven by `Click` classically too, so both sides read `Command == null`.
- **Every native item needs a `Command` or a `Click` handler.** `NativeMenuTests.Every_native_item_can_actually_be_activated` is the guard. A `Click`-driven item binds its own `IsEnabled`, since it gets none of the greying a command's `CanExecute` gives.
- **Rows and columns are zero-based** in Core and App; b3270 reports one-based and the conversion happens only in `B3270Session.ApplyScreen`.
- **Oversize is columns x rows; `TerminalModel.ToString()` is rows x columns.** Spec §4.3. Do not "fix" either to match the other. Every message names which number is which.
- **Test commands.** Full suite `dotnet test LizTerm.slnx`; one class `dotnet test tests/<project> --filter "FullyQualifiedName~<ClassName>"`.

---

## File Structure

**Created**

| File | Responsibility |
|---|---|
| `src/LizTerm.Core/Session/OversizeGeometry.cs` | The pure parse-and-validate rule for an oversize geometry, against a `TerminalModel`. |
| `tests/LizTerm.Core.Tests/Session/OversizeGeometryTests.cs` | Every rejection arm, asserted on its own message. |
| `tests/LizTerm.Backend.B3270.Tests/Fixtures/oversize-100x50.jsonl` | A replay fixture at a non-model geometry. |

**Modified**

| File | Change |
|---|---|
| `src/LizTerm.Core/Session/SessionProfile.cs` | Three appended parameters. |
| `src/LizTerm.Backend.B3270/B3270Session.cs` | `BuildArguments` gains two argv pairs; `reconnect` armed after success and disarmed before every disconnect. |
| `src/LizTerm.App/ViewModels/ProfileEditorViewModel.cs` | Keep-alive, auto-reconnect and oversize properties plus their validation. |
| `src/LizTerm.App/Views/ProfileEditorWindow.axaml` | A Connection row at 4 and an Oversize row at 6; existing rows re-indexed. |
| `src/LizTerm.App/ViewModels/ProfilePickerViewModel.cs` | Quick Connect text, error and command; `_openSession` gains a `fromStore` argument. |
| `src/LizTerm.App/Views/ProfilePickerWindow.axaml` (+`.cs`) | The Quick Connect row and its Enter handler. |
| `src/LizTerm.App/ViewModels/SessionViewModel.cs` | `IsReconnecting`, the two command guards, and Save as Profile. |
| `src/LizTerm.App/Views/SessionWindow.axaml` (+`.cs`) | Save as Profile in both menus. |
| `src/LizTerm.App/App.axaml.cs` | Wires the picker's two-argument delegate and the session's save-as-profile callback. |

---

## Task 1: `OversizeGeometry` in Core

**Files:**
- Create: `src/LizTerm.Core/Session/OversizeGeometry.cs`
- Test: `tests/LizTerm.Core.Tests/Session/OversizeGeometryTests.cs`

**Interfaces:**
- Consumes: `TerminalModel(int Number, int Rows, int Columns)` from `LizTerm.Core.Session`.
- Produces: `OversizeGeometry(int Columns, int Rows)` with `const int MaxCells = 0x3fff`, `override string ToString() => "{Columns}x{Rows}"`, and
  `static bool TryParse(string? text, TerminalModel model, out OversizeGeometry? geometry, out string? error)`.
  A blank `text` returns `true` with a null `geometry` — "no oversize" is valid, not an error.

- [ ] **Step 1: Write the failing tests**

Create `tests/LizTerm.Core.Tests/Session/OversizeGeometryTests.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.Core.Session;

namespace LizTerm.Core.Tests.Session;

public class OversizeGeometryTests
{
    private static readonly TerminalModel Model2 = TerminalModel.Find(2)!;   // 24x80
    private static readonly TerminalModel Model5 = TerminalModel.Find(5)!;   // 27x132

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_is_valid_and_means_no_oversize(string? text)
    {
        Assert.True(OversizeGeometry.TryParse(text, Model2, out var geometry, out var error));
        Assert.Null(geometry);
        Assert.Null(error);
    }

    /// <summary>b3270's own "no oversize" spelling, which a hand-edited profile can carry.</summary>
    [Fact]
    public void Zero_by_zero_is_valid_and_means_no_oversize()
    {
        Assert.True(OversizeGeometry.TryParse("0x0", Model2, out var geometry, out _));
        Assert.Null(geometry);
    }

    [Theory]
    [InlineData("132x43", 132, 43)]
    [InlineData("132X43", 132, 43)]
    [InlineData("  132x43  ", 132, 43)]
    public void A_legal_geometry_parses_columns_first(string text, int columns, int rows)
    {
        Assert.True(OversizeGeometry.TryParse(text, Model2, out var geometry, out _));
        Assert.Equal(new OversizeGeometry(columns, rows), geometry);
    }

    /// <summary>What BuildArguments passes to -oversize, so the round trip has to be exact.</summary>
    [Fact]
    public void ToString_is_the_engine_argument()
    {
        Assert.Equal("132x43", new OversizeGeometry(132, 43).ToString());
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("132")]
    [InlineData("132x43x2")]
    [InlineData("132x")]
    [InlineData("-5x10")]
    [InlineData("13 2x43")]
    public void Anything_that_is_not_two_plain_numbers_is_a_format_error(string text)
    {
        Assert.False(OversizeGeometry.TryParse(text, Model2, out var geometry, out var error));
        Assert.Null(geometry);
        Assert.Equal("Oversize must be columns by rows, for example 132x43.", error);
    }

    [Theory]
    [InlineData("0x50")]
    [InlineData("100x0")]
    public void One_dimension_zero_while_the_other_is_set_is_refused(string text)
    {
        Assert.False(OversizeGeometry.TryParse(text, Model2, out _, out var error));
        Assert.Equal("Oversize needs both a column count and a row count, for example 132x43.", error);
    }

    [Fact]
    public void Columns_above_the_ceiling_are_refused_by_name()
    {
        Assert.False(OversizeGeometry.TryParse("16384x1", Model2, out _, out var error));
        Assert.Equal("Oversize columns must be at most 16383.", error);
    }

    [Fact]
    public void Rows_above_the_ceiling_are_refused_by_name()
    {
        Assert.False(OversizeGeometry.TryParse("1x16384", Model2, out _, out var error));
        Assert.Equal("Oversize rows must be at most 16383.", error);
    }

    /// <summary>The rule that surprises: b3270 compares an AREA against the same linear constant, so 160
    /// columns allows only 102 rows and Vista's advertised 200x200 is rejected outright (spec 4.1).</summary>
    [Fact]
    public void The_area_limit_allows_160_by_102()
    {
        Assert.True(OversizeGeometry.TryParse("160x102", Model2, out _, out _));
    }

    [Fact]
    public void The_area_limit_refuses_160_by_103_and_says_what_fits()
    {
        Assert.False(OversizeGeometry.TryParse("160x103", Model2, out _, out var error));
        Assert.Equal("160 columns by 103 rows is 16,480 cells; b3270 allows 16,383. At 160 columns the most is 102 rows.", error);
    }

    [Fact]
    public void The_area_limit_refuses_Vistas_200_by_200()
    {
        Assert.False(OversizeGeometry.TryParse("200x200", Model2, out _, out var error));
        Assert.StartsWith("200 columns by 200 rows is 40,000 cells", error);
    }

    [Fact]
    public void Below_the_models_own_geometry_is_refused_naming_the_model()
    {
        Assert.False(OversizeGeometry.TryParse("70x43", Model2, out _, out var error));
        Assert.Equal("Oversize must be at least 80 columns and 24 rows for model 2.", error);
    }

    /// <summary>132x43 is legal on model 2 and too short on model 5, which is why the editor re-validates
    /// when the model changes (Task 5).</summary>
    [Fact]
    public void The_floor_is_the_chosen_models_floor_not_model_2s()
    {
        Assert.True(OversizeGeometry.TryParse("132x43", Model2, out _, out _));
        Assert.False(OversizeGeometry.TryParse("132x20", Model5, out _, out var error));
        Assert.Equal("Oversize must be at least 132 columns and 27 rows for model 5.", error);
    }

    /// <summary>A model number a hand-edited profile invented has no geometry (TerminalModel renders it as a
    /// bare number rather than lying with "9 — 0x0"), so there is no floor to compare against. Every other
    /// rule is a property of b3270 and still holds (spec 4.4).</summary>
    [Fact]
    public void An_unknown_model_skips_the_floor_and_keeps_every_other_rule()
    {
        var unknown = new TerminalModel(9, 0, 0);
        Assert.True(OversizeGeometry.TryParse("70x10", unknown, out _, out _));
        Assert.False(OversizeGeometry.TryParse("200x200", unknown, out _, out var error));
        Assert.StartsWith("200 columns by 200 rows", error);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~OversizeGeometryTests"`
Expected: FAIL to compile — `The name 'OversizeGeometry' does not exist in the current context`.

- [ ] **Step 3: Write the implementation**

Create `src/LizTerm.Core/Session/OversizeGeometry.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Globalization;

namespace LizTerm.Core.Session;

/// <summary>An oversize screen geometry, <b>columns by rows</b>, as b3270's <c>-oversize</c> takes it. Note that
/// <see cref="TerminalModel.ToString"/> renders the opposite order (<c>2 — 24x80</c> is rows by columns), which is
/// conventional for naming a 3270 model and is deliberately left disagreeing with this one: the resolution is
/// labelling, not reordering, so every message below names which number is which (spec 4.3).
///
/// The rules are b3270's, from <c>Common/ctlr.c</c>, and it refuses a bad one with a popup — which reaches LizTerm
/// as an unexplained HostMessage and no connection, with nothing tying it to the field the user typed. That is why
/// this validates in the editor instead (spec 4.2).</summary>
public sealed record OversizeGeometry(int Columns, int Rows)
{
    /// <summary>b3270's <c>MAX_ROWS_COLS</c>. It is both a per-dimension ceiling and, compared against
    /// <c>columns * rows</c>, an <b>area</b> limit — which is what caps a usable oversize far below Vista's
    /// advertised 200x200: at 160 columns it allows 102 rows.</summary>
    public const int MaxCells = 0x3fff;

    /// <summary>What <c>BuildArguments</c> passes to <c>-oversize</c>.</summary>
    public override string ToString() => $"{Columns}x{Rows}";

    /// <param name="model">The model the profile has chosen, whose own geometry is the floor. A model outside
    /// the catalogue arrives with zero rows and columns and skips that one rule.</param>
    /// <param name="geometry">Null when the text names no oversize, which is valid.</param>
    public static bool TryParse(string? text, TerminalModel model, out OversizeGeometry? geometry, out string? error)
    {
        geometry = null;
        error = null;
        if (string.IsNullOrWhiteSpace(text)) return true;

        var parts = text.Trim().Split('x', 'X');
        if (parts.Length != 2 || !TryParseDimension(parts[0], out var columns) || !TryParseDimension(parts[1], out var rows))
        {
            error = "Oversize must be columns by rows, for example 132x43.";
            return false;
        }

        // b3270's own spelling of "none", which a hand-edited profile can carry.
        if (columns == 0 && rows == 0) return true;
        if (columns == 0 || rows == 0)
        {
            error = "Oversize needs both a column count and a row count, for example 132x43.";
            return false;
        }
        if (columns > MaxCells) { error = $"Oversize columns must be at most {MaxCells}."; return false; }
        if (rows > MaxCells) { error = $"Oversize rows must be at most {MaxCells}."; return false; }
        // Both dimensions are now within the ceiling, so the product cannot overflow an int and the division
        // in the message cannot divide by zero.
        if ((long)columns * rows > MaxCells)
        {
            error = $"{columns} columns by {rows} rows is {columns * rows:N0} cells; b3270 allows {MaxCells:N0}. "
                + $"At {columns} columns the most is {MaxCells / columns} rows.";
            return false;
        }
        if (model.Columns > 0 && model.Rows > 0 && (columns < model.Columns || rows < model.Rows))
        {
            error = $"Oversize must be at least {model.Columns} columns and {model.Rows} rows for model {model.Number}.";
            return false;
        }

        geometry = new OversizeGeometry(columns, rows);
        return true;
    }

    /// <summary>Plain digits only. <see cref="NumberStyles.None"/> rejects a sign and surrounding space, so
    /// "-5", "+5" and " 5" are format errors rather than reaching the engine — the same rule
    /// <c>StartupArguments</c> applies to a port.</summary>
    private static bool TryParseDimension(string text, out int value) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~OversizeGeometryTests"`
Expected: PASS, all cases.

- [ ] **Step 5: Commit**

```bash
git add src/LizTerm.Core/Session/OversizeGeometry.cs tests/LizTerm.Core.Tests/Session/OversizeGeometryTests.cs
git commit -m "Add OversizeGeometry, the pure oversize rule, with b3270's area limit (#30)

b3270 compares columns * rows against MAX_ROWS_COLS, a linear constant,
so the real limit is an area: 160 columns allows 102 rows and Vista's
advertised 200x200 is rejected outright. Each rejection carries its own
message naming the actual limit, because b3270's own refusal is a popup
that reaches us as an unexplained HostMessage with nothing tying it to
the field the user typed.

A model outside the catalogue has no geometry, so the model floor is the
one rule skipped for it; the rest are properties of the engine and hold.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Task 2: The three `SessionProfile` fields

**Files:**
- Modify: `src/LizTerm.Core/Session/SessionProfile.cs`
- Test: `tests/LizTerm.Core.Tests/Profiles/ProfileStoreTests.cs:62` (extend the existing guard)

**Interfaces:**
- Produces: `SessionProfile.KeepAliveSeconds` (int, default 60), `SessionProfile.AutoReconnect` (bool, default false), `SessionProfile.Oversize` (string?, default null). Appended to the positional list, so every existing positional construction still compiles.

- [ ] **Step 1: Extend the failing test**

In `tests/LizTerm.Core.Tests/Profiles/ProfileStoreTests.cs`, replace the body of
`LoadAll_reads_a_file_missing_fields_with_every_declared_default_and_an_explicit_false_as_false` and its doc comment with:

```csharp
    /// <summary>A file written before a field existed has no such field and reads as the declared default: erase
    /// for DestructiveBackspace, which is what every x3270-family default keymap does (spec 2), and above all
    /// verification on, which the CLR default would silently turn off. A file that says false keeps false: the
    /// editor always writes the field, so a saved choice survives the default flip (spec 3.2).
    ///
    /// The three tier-1 fields are here for a stronger reason than coverage. The keep-alive default is
    /// retroactive by design — every profile already on disk gains a 60-second keep-alive the moment 0.4.0 runs,
    /// with no migration and no rewrite — and this assertion IS that decision (tier-1 spec 2.2). Without it the
    /// decision lives only in a constructor signature a later refactor could quietly change.</summary>
    [Fact]
    public void LoadAll_reads_a_file_missing_fields_with_every_declared_default_and_an_explicit_false_as_false()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "old.json"), """{"name":"old","host":"h"}""");
        File.WriteAllText(Path.Combine(_dir, "off.json"), """{"name":"off","host":"h","port":23,"destructiveBackspace":false,"verifyCertificate":false,"keepAliveSeconds":0,"autoReconnect":true,"oversize":"132x43"}""");
        var loaded = new ProfileStore(_dir).LoadAll();
        var old = loaded.Single(p => p.Name == "old");
        Assert.True(old.VerifyCertificate);
        Assert.True(old.DestructiveBackspace);
        Assert.Equal(23, old.Port);
        Assert.Equal(2, old.Model);
        Assert.True(old.Extended);
        Assert.Equal("cp037", old.CodePage);
        Assert.Null(old.PinnedCertificate);
        Assert.Equal(60, old.KeepAliveSeconds);
        Assert.False(old.AutoReconnect);
        Assert.Null(old.Oversize);
        var off = loaded.Single(p => p.Name == "off");
        Assert.False(off.DestructiveBackspace);
        Assert.False(off.VerifyCertificate);
        // A saved 0 must survive rather than reading back as the 60 default, or turning keep-alive off would
        // be impossible.
        Assert.Equal(0, off.KeepAliveSeconds);
        Assert.True(off.AutoReconnect);
        Assert.Equal("132x43", off.Oversize);
    }
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~ProfileStoreTests"`
Expected: FAIL to compile — `'SessionProfile' does not contain a definition for 'KeepAliveSeconds'`.

- [ ] **Step 3: Add the three parameters**

In `src/LizTerm.Core/Session/SessionProfile.cs`, change the final parameter line
`    bool DestructiveBackspace = true);`
to:

```csharp
    bool DestructiveBackspace = true,
    int KeepAliveSeconds = 60,
    bool AutoReconnect = false,
    string? Oversize = null);
```

And add these three lines to the record's XML doc, after the `DestructiveBackspace` param:

```csharp
/// <param name="KeepAliveSeconds">Seconds between TELNET NOPs (b3270's <c>nopSeconds</c>); 0 is off. On by default,
/// and that default is retroactive: a profile file written before this field existed reads 60 here and gains the
/// keep-alive without being rewritten. It keeps the network connection open and does NOT prevent a host idle
/// logoff — a NOP is not 3270 data, so TSO's own timer never sees it.</param>
/// <param name="AutoReconnect">Whether b3270's <c>reconnect</c> is armed after a connect succeeds, so a session the
/// host drops comes back. Off by default: a reconnect re-establishes the host session rather than just the socket,
/// so an LU can move.</param>
/// <param name="Oversize">An oversize geometry as <c>columns x rows</c>, or null for the model's own. Validated by
/// <see cref="OversizeGeometry"/>, whose order is the opposite of <see cref="TerminalModel.ToString"/>'s.</param>
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~ProfileStoreTests"`
Expected: PASS.

- [ ] **Step 5: Run the whole suite, to catch positional construction sites**

Run: `dotnet test LizTerm.slnx`
Expected: PASS. Appending parameters cannot break a positional caller, but this proves it rather than assuming it.

- [ ] **Step 6: Commit**

```bash
git add src/LizTerm.Core/Session/SessionProfile.cs tests/LizTerm.Core.Tests/Profiles/ProfileStoreTests.cs
git commit -m "Add KeepAliveSeconds, AutoReconnect and Oversize to SessionProfile (#37 #28 #30)

Appended to the positional list, so existing construction sites are
untouched, and flat rather than nested: the JSON context honours
constructor defaults for a missing field, which a nested record has no
equivalent of.

The 60-second keep-alive default is retroactive on purpose — every
profile already on disk gains it with no migration — so the round-trip
assertion in ProfileStoreTests is where that decision actually lives.
A saved 0 still reads back as 0, or turning it off would be impossible.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Task 3: The two argv arguments

**Files:**
- Modify: `src/LizTerm.Backend.B3270/B3270Session.cs:142-143` (`BuildArguments`)
- Test: `tests/LizTerm.Backend.B3270.Tests/B3270SessionLifecycleTests.cs`

**Interfaces:**
- Consumes: `SessionProfile.Oversize`, `SessionProfile.KeepAliveSeconds` from Task 2.
- Produces: `B3270Session.BuildArguments(SessionProfile)` returning `-oversize <geometry>` and `-set nopSeconds=<n>` appended after the existing six arguments, each omitted at its default.

- [ ] **Step 1: Write the failing tests**

Append to `tests/LizTerm.Backend.B3270.Tests/B3270SessionLifecycleTests.cs`, inside the class:

```csharp
    /// <summary>Both are omitted at their defaults, because argv is evaluated once per process and b3270's own
    /// defaults already match (oversize unset, nopSeconds 0). That is deliberately NOT the "send every toggle
    /// explicitly every time" rule the TLS options follow: that rule exists because a connect can inherit the
    /// previous connect's settings within one engine process, and argv cannot.</summary>
    [Fact]
    public void BuildArguments_omits_oversize_and_keep_alive_at_their_defaults()
    {
        var args = B3270Session.BuildArguments(new SessionProfile { Name = "p", Host = "h", KeepAliveSeconds = 0 });
        Assert.DoesNotContain("-oversize", args);
        Assert.DoesNotContain("-set", args);
    }

    [Fact]
    public void BuildArguments_passes_the_oversize_geometry_verbatim()
    {
        var args = B3270Session.BuildArguments(new SessionProfile { Name = "p", Host = "h", Oversize = "132x43", KeepAliveSeconds = 0 });
        var i = args.ToList().IndexOf("-oversize");
        Assert.True(i >= 0, "-oversize is missing");
        Assert.Equal("132x43", args[i + 1]);
    }

    /// <summary>"-set name=value" is the only form: "-nopSeconds 60" is rejected by the engine outright as
    /// "Unknown or incomplete option" (measured against 4.5ga6, spec 9.1).</summary>
    [Fact]
    public void BuildArguments_passes_the_keep_alive_as_a_set_assignment()
    {
        var args = B3270Session.BuildArguments(new SessionProfile { Name = "p", Host = "h", KeepAliveSeconds = 60 });
        var i = args.ToList().IndexOf("-set");
        Assert.True(i >= 0, "-set is missing");
        Assert.Equal("nopSeconds=60", args[i + 1]);
    }

    [Fact]
    public void BuildArguments_keeps_the_six_arguments_it_always_had()
    {
        var args = B3270Session.BuildArguments(new SessionProfile { Name = "p", Host = "h", Model = 3 });
        Assert.Equal(["-json", "-utf8", "-model", "3279-3-E", "-codepage", "cp037"], args.Take(6));
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/LizTerm.Backend.B3270.Tests --filter "FullyQualifiedName~BuildArguments"`
Expected: FAIL — the omission test passes trivially, the other two fail on a missing `-oversize` / `-set`.

- [ ] **Step 3: Write the implementation**

In `src/LizTerm.Backend.B3270/B3270Session.cs`, replace:

```csharp
    public static IReadOnlyList<string> BuildArguments(SessionProfile profile) =>
        ["-json", "-utf8", "-model", HostStringBuilder.ModelArgument(profile), "-codepage", profile.CodePage];
```

with:

```csharp
    /// <summary>The engine's command line. Oversize and the keep-alive ride here rather than as runtime Set
    /// actions: it needs no new protocol handling, and it sidesteps the unverified question of whether a runtime
    /// oversize change takes effect on the next connect, on the next process, or not at all. `reconnect` is the
    /// exception and must be a runtime Set, because it is armed only after a connect has succeeded.
    /// Both are omitted at their defaults, which are b3270's own.</summary>
    public static IReadOnlyList<string> BuildArguments(SessionProfile profile)
    {
        var arguments = new List<string>
        {
            "-json", "-utf8", "-model", HostStringBuilder.ModelArgument(profile), "-codepage", profile.CodePage,
        };
        if (!string.IsNullOrWhiteSpace(profile.Oversize))
        {
            arguments.Add("-oversize");
            arguments.Add(profile.Oversize.Trim());
        }
        if (profile.KeepAliveSeconds > 0)
        {
            // "-set name=value" is the only form the engine takes: "-nopSeconds 60" is not an option at all.
            arguments.Add("-set");
            arguments.Add($"nopSeconds={profile.KeepAliveSeconds.ToString(CultureInfo.InvariantCulture)}");
        }
        return arguments;
    }
```

If `System.Globalization` is not already imported in this file, add `using System.Globalization;` to its using block.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/LizTerm.Backend.B3270.Tests --filter "FullyQualifiedName~BuildArguments"`
Expected: PASS, all four.

- [ ] **Step 5: Commit**

```bash
git add src/LizTerm.Backend.B3270/B3270Session.cs tests/LizTerm.Backend.B3270.Tests/B3270SessionLifecycleTests.cs
git commit -m "Pass oversize and the keep-alive on the engine's command line (#30 #37)

Both omitted at their defaults, which are b3270's own, since argv is
evaluated once per process and cannot inherit a previous connect's
settings the way the TLS toggles can.

-set name=value is the only form the engine accepts; -nopSeconds 60 is
rejected as an unknown option, measured rather than assumed.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Task 4: The editor's Connection row

**Files:**
- Modify: `src/LizTerm.App/ViewModels/ProfileEditorViewModel.cs`
- Modify: `src/LizTerm.App/Views/ProfileEditorWindow.axaml`
- Test: `tests/LizTerm.App.Tests/ViewModels/ProfileViewModelsTests.cs`

**Interfaces:**
- Consumes: `SessionProfile.KeepAliveSeconds`, `SessionProfile.AutoReconnect` from Task 2.
- Produces: `ProfileEditorViewModel.KeepAliveText` (string), `ProfileEditorViewModel.AutoReconnect` (bool). `TryBuild()` returns null with `ValidationMessage` set for a keep-alive that is not 0..86400.

- [ ] **Step 1: Write the failing tests**

Append to `ProfileViewModelsTests` in `tests/LizTerm.App.Tests/ViewModels/ProfileViewModelsTests.cs` — one class holds both view models' tests, and it already has a `_store` field over a temp directory:

```csharp
    [Fact]
    public void The_editor_round_trips_the_keep_alive_and_auto_reconnect()
    {
        var vm = new ProfileEditorViewModel(new SessionProfile
        {
            Name = "MVS", Host = "mvs", KeepAliveSeconds = 30, AutoReconnect = true,
        });

        Assert.Equal("30", vm.KeepAliveText);
        Assert.True(vm.AutoReconnect);

        var built = vm.TryBuild();
        Assert.NotNull(built);
        Assert.Equal(30, built.KeepAliveSeconds);
        Assert.True(built.AutoReconnect);
    }

    /// <summary>A new profile shows the record's own default, so the editor and the file agree about what
    /// "on at 60 seconds" means rather than the editor quietly proposing something else.</summary>
    [Fact]
    public void A_new_profile_offers_the_declared_keep_alive_default()
    {
        Assert.Equal("60", new ProfileEditorViewModel(null).KeepAliveText);
    }

    [Fact]
    public void Turning_the_keep_alive_off_saves_a_zero()
    {
        var vm = new ProfileEditorViewModel(null) { Name = "p", Host = "h", KeepAliveText = "0" };
        Assert.Equal(0, vm.TryBuild()!.KeepAliveSeconds);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("-1")]
    [InlineData("90000")]
    public void A_keep_alive_that_is_not_a_sane_number_of_seconds_blocks_save(string text)
    {
        var vm = new ProfileEditorViewModel(null) { Name = "p", Host = "h", KeepAliveText = text };
        Assert.Null(vm.TryBuild());
        Assert.Equal("Keep-alive must be a whole number of seconds, 0 to 86400 (0 turns it off).", vm.ValidationMessage);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~ProfileViewModelsTests"`
Expected: FAIL to compile — `'ProfileEditorViewModel' does not contain a definition for 'KeepAliveText'`.

- [ ] **Step 3: Add the properties and validation**

In `src/LizTerm.App/ViewModels/ProfileEditorViewModel.cs`:

Add `using System.Globalization;` to the using block, and these two properties beside the others:

```csharp
    [ObservableProperty] private string _keepAliveText = "60";
    [ObservableProperty] private bool _autoReconnect;
```

In the constructor, in the block that copies `existing`, after the `_destructiveBackspace` line:

```csharp
        _keepAliveText = existing.KeepAliveSeconds.ToString(CultureInfo.InvariantCulture);
        _autoReconnect = existing.AutoReconnect;
```

In `TryBuild()`, after the code-page check and before `ValidationMessage = null;`:

```csharp
        // NumberStyles.None rejects a sign and surrounding space, so "-1", "+60" and " 60" are refused rather
        // than reaching the engine. The ceiling is a day: a larger one is a typo, not an intention.
        if (!int.TryParse(KeepAliveText.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var keepAlive) || keepAlive > 86400)
        {
            ValidationMessage = "Keep-alive must be a whole number of seconds, 0 to 86400 (0 turns it off).";
            return null;
        }
```

And in the returned `SessionProfile` initializer, after `DestructiveBackspace = DestructiveBackspace,`:

```csharp
            KeepAliveSeconds = keepAlive,
            AutoReconnect = AutoReconnect,
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~ProfileViewModelsTests"`
Expected: PASS.

- [ ] **Step 5: Add the Connection row to the editor window**

In `src/LizTerm.App/Views/ProfileEditorWindow.axaml`:

First change the grid's row list from nine `Auto` to ten:

```xml
    <Grid ColumnDefinitions="120,*" RowDefinitions="Auto,Auto,Auto,Auto,Auto,Auto,Auto,Auto,Auto,Auto" >
```

Then **re-index the existing rows below Security**: `Grid.Row="4"` (Model, both elements) becomes `"5"`, `"5"` (Code page, both) becomes `"7"`, `"6"` (LU name, both) becomes `"8"`, `"7"` (Keyboard, both) becomes `"9"`. Row 6 is left free for Task 5's Oversize row.

Then insert this immediately after the Security row's closing `</StackPanel>`:

```xml
      <TextBlock Grid.Row="4" Text="Connection" VerticalAlignment="Center" />
      <StackPanel Grid.Row="4" Grid.Column="1" Spacing="4">
        <StackPanel Orientation="Horizontal" Spacing="8">
          <TextBlock Text="Keep-alive every" VerticalAlignment="Center" />
          <TextBox x:Name="KeepAliveBox" Text="{Binding KeepAliveText}" Width="60" />
          <TextBlock Text="seconds (0 = off)" VerticalAlignment="Center" />
        </StackPanel>
        <!-- The caveat is inline in the row, the way the Keyboard row explains itself, because "keep-alive"
             is otherwise read as "stops the host timing me out". A NOP is not 3270 data, so TSO's own idle
             timer never sees it (spec 5.1). -->
        <TextBlock Text="Keeps the network connection open; does not prevent a host idle logoff."
                   Foreground="#A0A0A0" FontSize="12" TextWrapping="Wrap" />
        <CheckBox x:Name="AutoReconnectBox" Content="Reconnect automatically if the host drops the session"
                  IsChecked="{Binding AutoReconnect}" />
      </StackPanel>
```

- [ ] **Step 6: Run the window tests**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~ProfileEditorWindowTests"`
Expected: PASS. These reach controls by `x:Name`, so the re-index breaks nothing.

- [ ] **Step 7: Commit**

```bash
git add src/LizTerm.App/ViewModels/ProfileEditorViewModel.cs src/LizTerm.App/Views/ProfileEditorWindow.axaml tests/LizTerm.App.Tests/ViewModels/ProfileViewModelsTests.cs
git commit -m "Add the editor's Connection row: keep-alive and auto-reconnect (#37 #28)

Inserted at row 4, above Model, so the reach-the-host settings group
together and the terminal settings group below them; the four rows past
Security are re-indexed and the grid declares one more.

The keep-alive caveat is inline in the row rather than in a tooltip,
following the Keyboard row: without it the setting reads as a fix for a
host idle logoff, which a TELNET NOP cannot be.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Task 5: The editor's Oversize row

**Files:**
- Modify: `src/LizTerm.App/ViewModels/ProfileEditorViewModel.cs`
- Modify: `src/LizTerm.App/Views/ProfileEditorWindow.axaml`
- Test: `tests/LizTerm.App.Tests/ViewModels/ProfileViewModelsTests.cs`

**Interfaces:**
- Consumes: `OversizeGeometry.TryParse` (Task 1), `SessionProfile.Oversize` (Task 2), the existing `SelectedModel` view over `Model`.
- Produces: `ProfileEditorViewModel.Oversize` (string, "" for none).

- [ ] **Step 1: Write the failing tests**

Append to `ProfileViewModelsTests` (the same single class):

```csharp
    [Fact]
    public void The_editor_round_trips_an_oversize_geometry()
    {
        var vm = new ProfileEditorViewModel(new SessionProfile { Name = "MVS", Host = "mvs", Oversize = "132x43" });
        Assert.Equal("132x43", vm.Oversize);
        Assert.Equal("132x43", vm.TryBuild()!.Oversize);
    }

    /// <summary>Blank means the model's own geometry, and must save as null rather than "": the argv check is
    /// IsNullOrWhiteSpace, but a "" in the file would still be a lie about what the user chose.</summary>
    [Fact]
    public void A_blank_oversize_saves_as_null()
    {
        var vm = new ProfileEditorViewModel(null) { Name = "p", Host = "h", Oversize = "   " };
        Assert.Null(vm.TryBuild()!.Oversize);
    }

    [Fact]
    public void An_illegal_oversize_blocks_save_with_the_rules_own_message()
    {
        var vm = new ProfileEditorViewModel(null) { Name = "p", Host = "h", Oversize = "200x200" };
        Assert.Null(vm.TryBuild());
        Assert.StartsWith("200 columns by 200 rows is 40,000 cells", vm.ValidationMessage);
    }

    /// <summary>132x43 clears model 2's floor and is below model 5's 27x132, so switching the model has to
    /// re-run the check — otherwise the editor shows a stale verdict about the geometry in the box.</summary>
    [Fact]
    public void Changing_the_model_re_validates_the_oversize()
    {
        var vm = new ProfileEditorViewModel(null) { Name = "p", Host = "h", Oversize = "132x43" };
        Assert.NotNull(vm.TryBuild());

        vm.SelectedModel = TerminalModel.Find(5)!;
        Assert.Equal("Oversize must be at least 132 columns and 27 rows for model 5.", vm.ValidationMessage);
        Assert.Null(vm.TryBuild());

        vm.SelectedModel = TerminalModel.Find(2)!;
        Assert.Null(vm.ValidationMessage);
        Assert.NotNull(vm.TryBuild());
    }

    /// <summary>Only the oversize verdict moves with the model. A blank box has nothing to say about it, and
    /// clearing an unrelated message would be a second, invisible behaviour.</summary>
    [Fact]
    public void Changing_the_model_leaves_an_unrelated_message_alone()
    {
        var vm = new ProfileEditorViewModel(null) { Host = "h" };
        Assert.Null(vm.TryBuild());
        Assert.Equal("Give the profile a name.", vm.ValidationMessage);

        vm.SelectedModel = TerminalModel.Find(4)!;
        Assert.Equal("Give the profile a name.", vm.ValidationMessage);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~ProfileViewModelsTests"`
Expected: FAIL to compile — no `Oversize` property.

- [ ] **Step 3: Add the property, the validation and the re-check**

In `src/LizTerm.App/ViewModels/ProfileEditorViewModel.cs`, add beside the other properties:

```csharp
    [ObservableProperty] private string _oversize = "";
```

In the constructor's `existing` block, after `_autoReconnect`:

```csharp
        _oversize = existing.Oversize ?? "";
```

Add this partial method beside `OnUseTlsChanged`:

```csharp
    /// <summary>An oversize legal under one model can be below another's floor — 132x43 clears model 2 and is
    /// short of model 5's 27x132 — so a model change has to re-run the check rather than leave a stale verdict
    /// beside the box. Scoped to a non-blank box on purpose: a blank one says nothing about the geometry, and
    /// clearing an unrelated validation message here would be a second, invisible behaviour.</summary>
    partial void OnModelChanged(int value)
    {
        if (string.IsNullOrWhiteSpace(Oversize)) return;
        ValidationMessage = OversizeGeometry.TryParse(Oversize, SelectedModel, out _, out var error) ? null : error;
    }
```

In `TryBuild()`, immediately after the keep-alive check from Task 4:

```csharp
        if (!OversizeGeometry.TryParse(Oversize, SelectedModel, out var oversize, out var oversizeError))
        {
            ValidationMessage = oversizeError;
            return null;
        }
```

And in the returned initializer, after `AutoReconnect = AutoReconnect,`:

```csharp
            Oversize = oversize?.ToString(),
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~ProfileViewModelsTests"`
Expected: PASS.

- [ ] **Step 5: Add the Oversize row to the editor window**

In `src/LizTerm.App/Views/ProfileEditorWindow.axaml`, insert into the free row 6, after the Model row's closing `</StackPanel>`:

```xml
      <TextBlock Grid.Row="6" Text="Oversize" VerticalAlignment="Center" />
      <!-- Columns first, which is b3270's order for -oversize and the opposite of the Model drop-down's
           "2 — 24x80". Deliberately left disagreeing: 24x80 is the conventional way to name a model and is
           asserted exactly by CatalogueTests, while cols x rows is what every x3270 document a user might copy
           from uses. The placeholder is what resolves it (spec 4.3). -->
      <TextBox x:Name="OversizeBox" Grid.Row="6" Grid.Column="1" Text="{Binding Oversize}" Width="200"
               HorizontalAlignment="Left" PlaceholderText="columns x rows, e.g. 132x43" />
```

- [ ] **Step 6: Run the App test project**

Run: `dotnet test tests/LizTerm.App.Tests`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/LizTerm.App/ViewModels/ProfileEditorViewModel.cs src/LizTerm.App/Views/ProfileEditorWindow.axaml tests/LizTerm.App.Tests/ViewModels/ProfileViewModelsTests.cs
git commit -m "Add the editor's Oversize row, validated against the chosen model (#30)

Placed under Model, since that is what its floor comes from, and
re-validated when the model changes: 132x43 clears model 2 and is short
of model 5's 27x132, so a stale verdict beside the box would otherwise
outlive the choice that made it.

The placeholder names the order, because b3270 takes columns first and
the Model drop-down renders rows first. Neither is changed to match the
other; see the spec.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Task 6: Quick Connect in the picker view model

**Files:**
- Modify: `src/LizTerm.App/ViewModels/ProfilePickerViewModel.cs`
- Modify: `src/LizTerm.App/Views/ProfilePickerWindow.axaml.cs` (ctor delegate)
- Modify: `src/LizTerm.App/App.axaml.cs:154` (ShowPicker's lambda)
- Test: `tests/LizTerm.App.Tests/ViewModels/ProfileViewModelsTests.cs`

**Interfaces:**
- Consumes: `StartupArguments.Parse(IReadOnlyList<string>)` and `StartupArguments.Resolve(IReadOnlyList<SessionProfile>)` from `LizTerm.App.Startup`.
- Produces: `ProfilePickerViewModel.QuickConnectText` (string), `QuickConnectError` (string?), `QuickConnectCommand`.
- **Breaking:** the constructor's `openSession` parameter becomes `Action<SessionProfile, bool>` — the profile and whether it came from the store. Every call site must be updated (Step 3 lists all seven).

- [ ] **Step 1: Write the failing tests**

Append to `ProfileViewModelsTests` (the same single class, whose `_store` these use):

```csharp
    /// <summary>The box inherits the command line's tie-break rule by calling the same Parse and Resolve, so
    /// text that exactly names a saved profile connects THAT profile rather than a host of the same name.</summary>
    [Fact]
    public void Quick_connect_prefers_a_saved_profile_of_the_same_name()
    {
        _store.Save(new SessionProfile { Name = "mvs.local", Host = "elsewhere", Port = 992 });
        SessionProfile? opened = null;
        var fromStore = false;
        var vm = NewPicker((p, s) => { opened = p; fromStore = s; });

        vm.QuickConnectText = "mvs.local";
        vm.QuickConnectCommand.Execute(null);

        Assert.Equal("elsewhere", opened!.Host);
        Assert.True(fromStore);
        Assert.Null(vm.QuickConnectError);
    }

    [Fact]
    public void Quick_connect_opens_an_ad_hoc_session_and_saves_nothing()
    {
        SessionProfile? opened = null;
        var fromStore = true;
        var vm = NewPicker((p, s) => { opened = p; fromStore = s; });

        vm.QuickConnectText = "mvs.example:3270";
        vm.QuickConnectCommand.Execute(null);

        Assert.Equal("mvs.example", opened!.Host);
        Assert.Equal(3270, opened.Port);
        Assert.False(fromStore);
        Assert.Empty(_store.LoadAll());
    }

    [Fact]
    public void Quick_connect_reports_a_syntax_error_inline_and_connects_nothing()
    {
        var opened = false;
        var vm = NewPicker((_, _) => opened = true);

        vm.QuickConnectText = "mvs.local:99999";
        vm.QuickConnectCommand.Execute(null);

        Assert.False(opened);
        Assert.Equal("Type host, host:port, or L:host for TLS. An IPv6 address goes in brackets.", vm.QuickConnectError);
    }

    /// <summary>A bare word is ambiguous with a profile name, so Parse only reads one as a host when it has a
    /// dot or is localhost. The message has to teach the way out rather than merely refuse (spec 7.4).</summary>
    [Fact]
    public void A_bare_word_that_is_neither_a_profile_nor_a_host_suggests_the_port_form()
    {
        var opened = false;
        var vm = NewPicker((_, _) => opened = true);

        vm.QuickConnectText = "tk5";
        vm.QuickConnectCommand.Execute(null);

        Assert.False(opened);
        Assert.Equal("\"tk5\" is not a saved session, and does not look like a host. Add a port to connect to it as a host, for example tk5:23.", vm.QuickConnectError);
    }

    [Fact]
    public void Quick_connect_with_an_empty_box_asks_for_something_to_connect_to()
    {
        var opened = false;
        var vm = NewPicker((_, _) => opened = true);

        vm.QuickConnectCommand.Execute(null);

        Assert.False(opened);
        Assert.Equal("Type a host name, or the name of a saved session.", vm.QuickConnectError);
    }

    private ProfilePickerViewModel NewPicker(Action<SessionProfile, bool> openSession) =>
        new(_store, openSession, _ => Task.FromResult<ProfileEdit?>(null), () => { });
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~ProfileViewModelsTests"`
Expected: FAIL to compile — no `QuickConnectText`, and the `NewPicker` helper's delegate does not match the constructor.

- [ ] **Step 3: Change the delegate and add the command**

In `src/LizTerm.App/ViewModels/ProfilePickerViewModel.cs`:

Add `using LizTerm.App.Startup;` to the using block. Change the field and the constructor parameter:

```csharp
    private readonly Action<SessionProfile, bool> _openSession;
```

```csharp
    /// <param name="openSession">Opens a session. The bool is whether the profile came from the store: a pin
    /// chosen in that window can be written back only for a saved profile, and Quick Connect's ad hoc profiles
    /// are not saved.</param>
    /// <param name="editProfile">Shows the editor for an existing profile (or null for a new one); returns null when cancelled.</param>
    public ProfilePickerViewModel(ProfileStore store, Action<SessionProfile, bool> openSession, Func<SessionProfile?, Task<ProfileEdit?>> editProfile, Action quit)
```

Change `Connect()`:

```csharp
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Connect() => _openSession(SelectedProfile!, true);
```

Add beside the other properties:

```csharp
    [ObservableProperty] private string _quickConnectText = "";
    [ObservableProperty] private string? _quickConnectError;
```

And the command, at the end of the class:

```csharp
    /// <summary>Connect to what the box names, without saving anything. The parse and the profile-name
    /// precedence are the command line's own — the same Parse and Resolve, so the box cannot drift from it —
    /// which is why a saved profile called "CONS01@tk5" stays reachable by its own name here too (spec 7.1).</summary>
    [RelayCommand]
    private void QuickConnect()
    {
        var text = QuickConnectText.Trim();
        if (text.Length == 0)
        {
            QuickConnectError = "Type a host name, or the name of a saved session.";
            return;
        }

        var parsed = StartupArguments.Parse([text]);
        if (parsed.Resolve([.. Profiles]) is { } profile)
        {
            QuickConnectError = null;
            // fromStore is whether Resolve matched a saved name. It cannot mis-fire: an exact name match always
            // wins there, so the ad hoc branch can never produce a name that a saved profile also has.
            _openSession(profile, Profiles.Any(p => p.Name.Equals(profile.Name, StringComparison.OrdinalIgnoreCase)));
            return;
        }

        QuickConnectError = parsed.Error is null
            ? $"\"{text}\" is not a saved session, and does not look like a host. Add a port to connect to it as a host, for example {text}:23."
            : "Type host, host:port, or L:host for TLS. An IPv6 address goes in brackets.";
    }
```

Then update the six remaining call sites:

1. `src/LizTerm.App/Views/ProfilePickerWindow.axaml.cs` — the second constructor's parameter becomes
   `Action<SessionProfile, bool> openSession` (the body passes it through unchanged).
2. `src/LizTerm.App/App.axaml.cs:154` — `profile => OpenSession(profile, fromStore: true)` becomes
   `(profile, fromStore) => OpenSession(profile, fromStore)`.
3. In `tests/LizTerm.App.Tests/ViewModels/ProfileViewModelsTests.cs`, four existing constructions:
   `p => opened = p` becomes `(p, _) => opened = p`, and each of the three `_ => { }` becomes `(_, _) => { }`.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~ProfileViewModelsTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/LizTerm.App/ViewModels/ProfilePickerViewModel.cs src/LizTerm.App/Views/ProfilePickerWindow.axaml.cs src/LizTerm.App/App.axaml.cs tests/LizTerm.App.Tests/ViewModels/ProfileViewModelsTests.cs
git commit -m "Add Quick Connect to the picker view model (#29)

Nothing but StartupArguments.Parse and Resolve, which already carry the
ad hoc syntax and the rule that an exact profile-name match wins — so
the box cannot drift from the command line, and a profile called
CONS01@tk5 stays reachable by its own name.

openSession gains a fromStore argument rather than App inferring it: an
ad hoc profile is not saved, so a pin chosen in that window must not be
written back.

A bare word with no dot is ambiguous with a profile name and Parse
refuses it as a host; the message teaches the port form instead of just
saying no.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Task 7: The Quick Connect row in the picker window

**Files:**
- Modify: `src/LizTerm.App/Views/ProfilePickerWindow.axaml`
- Modify: `src/LizTerm.App/Views/ProfilePickerWindow.axaml.cs`
- Test: `tests/LizTerm.App.Tests/Views/` — create `ProfilePickerWindowTests.cs`

**Interfaces:**
- Consumes: `ProfilePickerViewModel.QuickConnectText`, `QuickConnectCommand` (Task 6).
- Produces: a `TextBox` named `QuickConnectBox` whose `KeyDown` handler consumes Enter.

- [ ] **Step 1: Write the failing test**

Create `tests/LizTerm.App.Tests/Views/ProfilePickerWindowTests.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using LizTerm.App.ViewModels;
using LizTerm.App.Views;
using LizTerm.Core.Profiles;
using LizTerm.Core.Session;

namespace LizTerm.App.Tests.Views;

public class ProfilePickerWindowTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lizterm-picker-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>The picker's Connect button is IsDefault, so without the box consuming Enter, typing a host and
    /// pressing Enter would connect the SELECTED PROFILE instead — a different host entirely. Handled at the box
    /// means the window-level default button never sees the key, the same shape SessionWindow.OnFindBoxKeyDown
    /// uses to keep the find bar's typing off the wire (spec 7.3).</summary>
    [AvaloniaFact]
    public void Enter_in_the_quick_connect_box_connects_the_typed_host_not_the_selected_profile()
    {
        var store = new ProfileStore(_dir);
        store.Save(new SessionProfile { Name = "saved", Host = "saved.host", Port = 23 });
        SessionProfile? opened = null;
        var window = new ProfilePickerWindow(store, (p, _) => opened = p, () => { });
        window.Show();

        var vm = (ProfilePickerViewModel)window.DataContext!;
        vm.SelectedProfile = vm.Profiles.Single();
        Assert.True(vm.ConnectCommand.CanExecute(null));

        var box = window.FindControl<TextBox>("QuickConnectBox")!;
        vm.QuickConnectText = "other.example:3270";
        box.Focus();
        window.KeyPressQwerty(Key.Enter, RawInputModifiers.None);

        Assert.NotNull(opened);
        Assert.Equal("other.example", opened.Host);
        Assert.Equal(3270, opened.Port);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~ProfilePickerWindowTests"`
Expected: FAIL — `FindControl<TextBox>("QuickConnectBox")` returns null, so the test throws on the `!`.

- [ ] **Step 3: Add the row**

In `src/LizTerm.App/Views/ProfilePickerWindow.axaml`, insert immediately after the `Sessions` `TextBlock` and before the `DockPanel.Dock="Right"` StackPanel (docked children take space in declaration order, so this must precede the button column to span the full width):

```xml
    <StackPanel DockPanel.Dock="Top" Margin="0,0,0,12" Spacing="4">
      <StackPanel Orientation="Horizontal" Spacing="8">
        <TextBox x:Name="QuickConnectBox" Text="{Binding QuickConnectText}" Width="300"
                 PlaceholderText="host, host:port, or L:host for TLS" KeyDown="OnQuickConnectKeyDown" />
        <Button Content="Quick Connect" Command="{Binding QuickConnectCommand}" />
      </StackPanel>
      <TextBlock Text="{Binding QuickConnectError}" Foreground="#FF8080" FontSize="12" TextWrapping="Wrap"
                 IsVisible="{Binding QuickConnectError, Converter={x:Static ObjectConverters.IsNotNull}}" />
    </StackPanel>
```

In `src/LizTerm.App/Views/ProfilePickerWindow.axaml.cs`, change the second constructor's signature to
`public ProfilePickerWindow(ProfileStore store, Action<SessionProfile, bool> openSession, Action quit) : this()`
(the body is unchanged — it already passes `openSession` straight through), and add this method:

```csharp
    /// <summary>Enter connects what is in the box. Handled here so the window's default button — Connect, for
    /// the profile selected in the list — never sees it: a user who types a host and presses Enter must not be
    /// connected somewhere else. Same shape as SessionWindow.OnFindBoxKeyDown.</summary>
    private void OnQuickConnectKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Return)) return;
        e.Handled = true;
        (DataContext as ProfilePickerViewModel)?.QuickConnectCommand.Execute(null);
    }
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~ProfilePickerWindowTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/LizTerm.App/Views/ProfilePickerWindow.axaml src/LizTerm.App/Views/ProfilePickerWindow.axaml.cs tests/LizTerm.App.Tests/Views/ProfilePickerWindowTests.cs
git commit -m "Add the Quick Connect row to the picker (#29)

Enter is handled at the box and marked handled, so the window's default
Connect button never sees it. Without that, typing a host and pressing
Enter connects the profile selected in the list instead — a different
host entirely, and the test is written around exactly that.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Task 8: Save as Profile in the session view model

**Files:**
- Modify: `src/LizTerm.App/ViewModels/SessionViewModel.cs`
- Modify: `src/LizTerm.App/App.axaml.cs` (`OpenSession`)
- Test: `tests/LizTerm.App.Tests/ViewModels/SessionViewModelTests.cs`

**Interfaces:**
- Produces: `SessionViewModel.CanSaveAsProfile` (bool) and `SessionViewModel.SaveAsProfileAsync()` (public `Task`), plus a `SaveAsProfileCommand` generated from it. A new optional last constructor parameter `Func<SessionProfile, Task>? saveAsProfile = null`; null means the item is disabled, which is what every existing test gets without changing.

- [ ] **Step 1: Write the failing tests**

Append to `tests/LizTerm.App.Tests/ViewModels/SessionViewModelTests.cs`:

```csharp
    /// <summary>The ad hoc connection becomes the start of a profile. The callback is handed the session's own
    /// profile so the editor opens pre-filled with every row, including the tier-1 ones.</summary>
    [Fact]
    public async Task Save_as_profile_hands_the_sessions_profile_to_the_callback()
    {
        var fake = new FakeEmulatorSession { Profile = new SessionProfile { Name = "mvs.example:3270", Host = "mvs.example", Port = 3270 } };
        SessionProfile? offered = null;
        var vm = new SessionViewModel(fake, a => a(), new FakeTextClipboard(),
            saveAsProfile: p => { offered = p; return Task.CompletedTask; });

        Assert.True(vm.CanSaveAsProfile);
        await vm.SaveAsProfileAsync();

        Assert.Equal("mvs.example", offered!.Host);
        Assert.Equal(3270, offered.Port);
    }

    /// <summary>Without a callback there is nothing the item could do, and a native menu item that is enabled is
    /// an offer the app cannot honour.</summary>
    [Fact]
    public void Save_as_profile_is_disabled_without_a_callback()
    {
        var vm = new SessionViewModel(new FakeEmulatorSession(), a => a(), new FakeTextClipboard());
        Assert.False(vm.CanSaveAsProfile);
    }

    /// <summary>A failing save reaches the error banner rather than faulting an async void menu handler.</summary>
    [Fact]
    public async Task A_failing_save_as_profile_reports_rather_than_throws()
    {
        var vm = new SessionViewModel(new FakeEmulatorSession(), a => a(), new FakeTextClipboard(),
            saveAsProfile: _ => throw new IOException("disk full"));

        await vm.SaveAsProfileAsync();

        Assert.Equal("Could not save the profile: disk full", vm.ErrorMessage);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~SessionViewModelTests"`
Expected: FAIL to compile — no `saveAsProfile` parameter and no `CanSaveAsProfile`.

- [ ] **Step 3: Add the callback, the guard and the method**

In `src/LizTerm.App/ViewModels/SessionViewModel.cs`, add the field beside `_saveProfile`:

```csharp
    private readonly Func<SessionProfile, Task>? _saveAsProfile;
```

Add the parameter as the **last** one on the constructor (so every existing call site is unaffected), with its doc line:

```csharp
    /// <param name="saveAsProfile">Turns this session into a saved profile — the app opens the profile editor
    /// pre-filled and writes the result; null disables the menu item.</param>
```

```csharp
        IFolderOpener? folderOpener = null, ICertificateFetcher? certificateFetcher = null,
        Func<SessionProfile, Task>? saveAsProfile = null)
```

and assign it in the body beside the others: `_saveAsProfile = saveAsProfile;`

Then add, near the other commands:

```csharp
    public bool CanSaveAsProfile => _saveAsProfile is not null;

    /// <summary>File &gt; Save as Profile. Public because both menus drive it through Click handlers rather than
    /// the command, the way Save Screen As does. A pin taken in THIS window lives in _pinOverride rather than in
    /// the session's profile, which is fixed at construction, so it is folded in here — otherwise a certificate
    /// the user deliberately trusted during an ad hoc session would be dropped by the profile it becomes.</summary>
    [RelayCommand(CanExecute = nameof(CanSaveAsProfile))]
    public async Task SaveAsProfileAsync()
    {
        if (_saveAsProfile is null) return;
        try
        {
            await _saveAsProfile(_pinOverride is null
                ? Profile
                : Profile with { PinnedCertificate = _pinOverride, VerifyCertificate = true });
        }
        catch (Exception ex)
        {
            ErrorMessage = "Could not save the profile: " + ex.Message;
        }
    }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~SessionViewModelTests"`
Expected: PASS.

- [ ] **Step 5: Wire it in the app**

In `src/LizTerm.App/App.axaml.cs`, in `OpenSession`, add the argument after `new SslStreamCertificateFetcher()`:

```csharp
            new SslStreamCertificateFetcher(),
            async profile =>
            {
                // The existing editor, pre-filled: it already carries every row, validates them, and knows the
                // model and code-page catalogues. Saving by name overwrites, exactly as the picker's New does.
                if (await new ProfileEditorWindow(profile).ShowDialog<ProfileEdit?>(window) is { } edit)
                    store.Save(edit.Profile);
            });
```

- [ ] **Step 6: Build to confirm the wiring compiles**

Run: `dotnet build src/LizTerm.App`
Expected: build succeeded, 0 warnings.

- [ ] **Step 7: Commit**

```bash
git add src/LizTerm.App/ViewModels/SessionViewModel.cs src/LizTerm.App/App.axaml.cs tests/LizTerm.App.Tests/ViewModels/SessionViewModelTests.cs
git commit -m "Let a session be saved as a profile (#29)

An ad hoc Quick Connect session is otherwise a dead end when one of the
record's defaults is wrong for that host. The callback opens the
existing profile editor pre-filled, so every row — model, code page,
oversize, keep-alive — is already there and already validated.

A pin taken in this window lives in _pinOverride rather than in the
session's profile, which is fixed at construction, so it is folded into
what the editor receives; otherwise a certificate the user deliberately
trusted would be dropped by the profile it becomes.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Task 9: Save as Profile in both menus

**Files:**
- Modify: `src/LizTerm.App/Views/SessionWindow.axaml`
- Modify: `src/LizTerm.App/Views/SessionWindow.axaml.cs`
- Test: `tests/LizTerm.App.Tests/Views/NativeMenuTests.cs` (existing guards cover it)

**Interfaces:**
- Consumes: `SessionViewModel.CanSaveAsProfile`, `SessionViewModel.SaveAsProfileAsync()` (Task 8).

**Both items use `Click`, not `Command`.** The parity guard compares `Command` by reference for every menu but `_Edit`, so a native `Click` item paired with a classic `Command` item fails it — both must read `Command == null`. This mirrors Save Screen As exactly.

- [ ] **Step 1: Add the item to both menus**

In `src/LizTerm.App/Views/SessionWindow.axaml`, in the **native** File menu, immediately after the `_Save Screen As...` line:

```xml
            <NativeMenuItem Header="Save as _Profile..." Click="OnSaveAsProfileClickNative" IsEnabled="{Binding CanSaveAsProfile}" />
```

and in the **classic** File menu, immediately after its `_Save Screen As...` line:

```xml
        <MenuItem Header="Save as _Profile..." Click="OnSaveAsProfileClick" IsEnabled="{Binding CanSaveAsProfile}" />
```

No `Gesture` on either: File is not Edit.

- [ ] **Step 2: Add the two handlers**

In `src/LizTerm.App/Views/SessionWindow.axaml.cs`, beside the Save Screen pair at line 126:

```csharp
    private void OnSaveAsProfileClick(object? sender, RoutedEventArgs e) => _ = ViewModel?.SaveAsProfileAsync();
    private void OnSaveAsProfileClickNative(object? sender, EventArgs e) => _ = ViewModel?.SaveAsProfileAsync();
```

- [ ] **Step 3: Run the menu guards**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~NativeMenuTests"`
Expected: PASS. `The_native_menu_matches_the_classic_menu_item_for_item` sees both items with a null `Command` and the same header, and `Every_native_item_can_actually_be_activated` sees the Click handler.

- [ ] **Step 4: Run the App project**

Run: `dotnet test tests/LizTerm.App.Tests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/LizTerm.App/Views/SessionWindow.axaml src/LizTerm.App/Views/SessionWindow.axaml.cs
git commit -m "Put Save as Profile on both File menus (#29)

Click on both sides rather than Click plus Command: the parity guard
compares Command by reference for every menu but Edit, so the two must
agree on null. Same shape as Save Screen As, and the native item binds
its own IsEnabled because a Click-driven item gets none of the greying
a command's CanExecute would give it.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Task 10: An oversize replay fixture

**Files:**
- Create: `tests/LizTerm.Backend.B3270.Tests/Fixtures/oversize-100x50.jsonl`
- Modify: `tests/LizTerm.Backend.B3270.Tests/Fixtures/README.md`
- Modify: `tests/LizTerm.Backend.B3270.Tests/ReplayTests.cs`

**Interfaces:**
- Consumes: the existing `ReplayTests` fixture-feeding helper.

No existing fixture uses a non-model geometry, so "`screen-mode` reports it and `ScreenBuffer` resizes" is an assumption until this exists.

- [ ] **Step 1: Record the fixture**

b3270 reports the oversize geometry in its own `initialize` block, before any host, so no host and no `playback` are needed (verified against 4.5ga6 on 2026-09-10: `screen-mode` came back `{"model": 2, "rows": 50, "columns": 100, "oversize": true, "extended": true}`).

```bash
b3270 -json -utf8 -model 3279-2-E -codepage cp037 -oversize 100x50 > /tmp/oversize.jsonl & sleep 2; kill %1
head -1 /tmp/oversize.jsonl > tests/LizTerm.Backend.B3270.Tests/Fixtures/oversize-100x50.jsonl
```

If `b3270` is not on the PATH, use `/opt/homebrew/bin/b3270` or `$LIZTERM_B3270_PATH`. Confirm the saved file is one line and contains `"columns": 100` and `"rows": 50`.

- [ ] **Step 2: Write the failing test**

Append to `tests/LizTerm.Backend.B3270.Tests/ReplayTests.cs`. This is the same shape the file's other replay tests use — feed every fixture line through `Emit`, `Exit(0)`, then wait for `Faulted` and read `CurrentScreen`. `Fixture(name)` is the existing path helper; `Fixtures/**` is already a glob in the csproj, so the new file needs no project change.

```csharp
    /// <summary>#30's one unproven claim: screen-mode already reports an oversize geometry and ScreenBuffer
    /// already resizes on it, so the UI needs no change. Every other fixture is a model geometry, so nothing
    /// established that until this one.</summary>
    [Fact]
    public async Task An_oversize_geometry_resizes_the_buffer()
    {
        var fake = new FakeB3270Process { AutoInitialize = false, RunResponder = _ => [] };
        foreach (var line in File.ReadLines(Fixture("oversize-100x50.jsonl"))) fake.Emit(line);
        fake.Exit(0);

        var profile = new SessionProfile { Name = "replay", Host = "127.0.0.1", Oversize = "100x50" };
        var session = new B3270Session(profile, () => fake);
        var ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        session.Faulted += (_, _) => ended.TrySetResult();

        await session.StartProcessAsync(TestContext.Current.CancellationToken);
        await ended.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        var screen = session.CurrentScreen;
        Assert.Equal(50, screen.Rows);
        Assert.Equal(100, screen.Columns);
    }
```

- [ ] **Step 3: Run the test to verify it fails, then passes**

Run: `dotnet test tests/LizTerm.Backend.B3270.Tests --filter "FullyQualifiedName~An_oversize_geometry_resizes_the_buffer"`
Expected: FAIL first if the fixture file is missing or mis-trimmed; PASS once it is in place. If it fails with the fixture present, that is #30's assumption being wrong and is a real finding — stop and report rather than adjusting the assertion.

- [ ] **Step 4: Document the fixture**

Add to `tests/LizTerm.Backend.B3270.Tests/Fixtures/README.md`, matching the existing entries' style:

```markdown
- `oversize-100x50.jsonl` — b3270's own `initialize` block started with `-oversize 100x50`, no host involved.
  Recorded by running the engine for two seconds and keeping the first line. The only fixture with a geometry
  that is not a model's, which is what proves `screen-mode` and `ScreenBuffer` handle oversize (#30).
```

- [ ] **Step 5: Commit**

```bash
git add tests/LizTerm.Backend.B3270.Tests/Fixtures/oversize-100x50.jsonl tests/LizTerm.Backend.B3270.Tests/Fixtures/README.md tests/LizTerm.Backend.B3270.Tests/ReplayTests.cs
git commit -m "Add a replay fixture at a non-model geometry (#30)

Every other fixture is a model geometry, so "screen-mode reports an
oversize and ScreenBuffer resizes on it" was an assumption. b3270 puts
the geometry in its own initialize block with no host attached, so the
fixture is one line and needs neither a trace nor playback.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Task 11: Connect and Disconnect during a reconnect

**Files:**
- Modify: `src/LizTerm.App/ViewModels/SessionViewModel.cs`
- Test: `tests/LizTerm.App.Tests/ViewModels/SessionViewModelConnectTests.cs`

**Interfaces:**
- Produces: `SessionViewModel.IsReconnecting` (bool), and `CanConnect` / `CanDisconnect` amended to account for it.

`ConnectionState.Reconnecting` becomes reachable for the first time in Task 12. Core's `HasSocket()` excludes it, so today `CanConnect => !HasSocket` is **true** mid-reconnect and `CanDisconnect => HasSocket || ConnectPending` is **false** — exactly backwards. Fixing it before the state can occur means the UI is never briefly wrong.

- [ ] **Step 1: Write the failing tests**

Append to `tests/LizTerm.App.Tests/ViewModels/SessionViewModelConnectTests.cs`:

```csharp
    /// <summary>Reconnecting is not a socket (Core's HasSocket excludes it), so without this the engine's own
    /// reconnect would leave Connect enabled — offering to start a second attempt over one already running —
    /// and Disconnect disabled, which is the one thing the user actually wants at that moment.</summary>
    [Fact]
    public void Connect_is_disabled_and_Disconnect_enabled_while_the_engine_reconnects()
    {
        var fake = new FakeEmulatorSession();
        var vm = new SessionViewModel(fake, a => a(), new FakeTextClipboard());

        fake.RaiseConnection(ConnectionState.Reconnecting);

        Assert.True(vm.IsReconnecting);
        Assert.False(vm.CanConnect);
        Assert.True(vm.CanDisconnect);
        Assert.False(vm.ConnectCommand.CanExecute(null));
        Assert.True(vm.DisconnectCommand.CanExecute(null));
    }

    [Fact]
    public void Leaving_the_reconnect_restores_the_two_commands()
    {
        var fake = new FakeEmulatorSession();
        var vm = new SessionViewModel(fake, a => a(), new FakeTextClipboard());

        fake.RaiseConnection(ConnectionState.Reconnecting);
        fake.RaiseConnection(ConnectionState.Disconnected);

        Assert.False(vm.IsReconnecting);
        Assert.True(vm.CanConnect);
        Assert.False(vm.CanDisconnect);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~SessionViewModelConnectTests"`
Expected: FAIL to compile — no `IsReconnecting`.

- [ ] **Step 3: Add the property and amend the guards**

In `src/LizTerm.App/ViewModels/SessionViewModel.cs`, add beside `_hasSocket`:

```csharp
    /// <summary>b3270 is reconnecting on its own initiative after the host dropped an established session. A
    /// third bool rather than the raw state, for the reason HasSocket is one: an [ObservableProperty] named
    /// ConnectionState would collide with the enum type. Reconnecting is deliberately NOT a socket in Core's
    /// HasSocket, so both command guards have to name it separately.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    [NotifyCanExecuteChangedFor(nameof(DisconnectCommand))]
    private bool _isReconnecting;
```

In `ApplyConnection`, after the `HasSocket` assignment:

```csharp
        IsReconnecting = state == ConnectionState.Reconnecting;
```

Amend the two guards:

```csharp
    /// <summary>Disabled once the engine holds a socket, and while it is reconnecting on its own: a second
    /// attempt over one already running is not something the app can honour. x3270's own File menu disables it
    /// too.</summary>
    public bool CanConnect => !HasSocket && !IsReconnecting;
```

```csharp
    /// <summary>Not the inverse of CanConnect: this also cancels a pending connect, and HasSocket is false
    /// through Resolving and TcpPending, which is exactly when a user wants to give up on one. Reconnecting is
    /// the same case — no socket yet, and the one moment Disconnect matters most.</summary>
    public bool CanDisconnect => HasSocket || ConnectPending || IsReconnecting;
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~SessionViewModelConnectTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/LizTerm.App/ViewModels/SessionViewModel.cs tests/LizTerm.App.Tests/ViewModels/SessionViewModelConnectTests.cs
git commit -m "Gate Connect and Disconnect on the reconnecting state too (#28)

Core's HasSocket deliberately excludes Reconnecting, so once the engine
can reconnect on its own the existing guards read exactly backwards:
Connect enabled over an attempt already running, Disconnect disabled at
the one moment it matters most. Landed before the state can occur, so
the UI is never briefly wrong.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Task 12: Arm `reconnect` after a successful connect

**Files:**
- Modify: `src/LizTerm.Backend.B3270/B3270Session.cs` (`ConnectAsync`)
- Test: `tests/LizTerm.Backend.B3270.Tests/B3270SessionConnectTests.cs`

**Interfaces:**
- Consumes: `SessionProfile.AutoReconnect` (Task 2), the existing `RunAsync(IReadOnlyList<B3270Action>, ...)` and `B3270Action(string action, params string[] args)`.
- Produces: no public surface. `IEmulatorSession` and `FakeEmulatorSession` are untouched — the session reads its own profile.

- [ ] **Step 1: Write the failing tests**

Append to `tests/LizTerm.Backend.B3270.Tests/B3270SessionConnectTests.cs`:

```csharp
    private static readonly SessionProfile Reconnecting =
        new() { Name = "t", Host = "h", Port = 23, AutoReconnect = true };

    /// <summary>Only after the Connect run succeeded, which is the whole design: every failure path — the error,
    /// the 30s timeout, the certificate prompt — reasons about an attempt that is over, and none of that holds
    /// with an engine already retrying behind it. The ordering assertion is the point, not the presence one
    /// (spec 6.1).</summary>
    [Fact]
    public async Task Reconnect_is_armed_only_after_the_connect_succeeds()
    {
        var fake = new FakeB3270Process();
        await using var session = new B3270Session(Reconnecting, () => fake);

        await session.ConnectAsync(cancellationToken: TestContext.Current.CancellationToken);

        var lines = fake.InputLines.ToList();
        var connect = lines.FindIndex(l => l.Contains("\"Connect\""));
        var arm = lines.FindIndex(l => l.Contains("\"reconnect\"") && l.Contains("\"true\""));
        Assert.True(connect >= 0, "no Connect was sent");
        Assert.True(arm > connect, "reconnect must be armed after the Connect run, never before it");
    }

    [Fact]
    public async Task Reconnect_is_not_armed_for_a_profile_that_did_not_ask_for_it()
    {
        var fake = new FakeB3270Process();
        await using var session = new B3270Session(Reconnecting with { AutoReconnect = false }, () => fake);

        await session.ConnectAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.DoesNotContain(fake.InputLines, l => l.Contains("\"reconnect\""));
    }

    /// <summary>A connect that failed leaves the engine alone: arming there is exactly the `retry` behaviour this
    /// milestone excluded, reached by the back door.</summary>
    [Fact]
    public async Task A_failed_connect_arms_nothing()
    {
        var fake = new FakeB3270Process();
        fake.RunResponder = line => line.Contains("\"Connect\"")
            ? [Failed(Tag(line), "Connection failed")]
            : [Ok(line)];
        await using var session = new B3270Session(Reconnecting, () => fake);

        await Assert.ThrowsAsync<ConnectionFailedException>(
            () => session.ConnectAsync(cancellationToken: TestContext.Current.CancellationToken));

        Assert.DoesNotContain(fake.InputLines, l => l.Contains("\"reconnect\""));
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/LizTerm.Backend.B3270.Tests --filter "FullyQualifiedName~B3270SessionConnectTests"`
Expected: the two "not armed" tests pass trivially; `Reconnect_is_armed_only_after_the_connect_succeeds` FAILS with `arm` at `-1`.

- [ ] **Step 3: Arm it**

In `src/LizTerm.Backend.B3270/B3270Session.cs`, in `ConnectAsync`, replace the line
`            await ConnectCoreAsync(cancellationToken);`
with:

```csharp
            await ConnectCoreAsync(cancellationToken);
            // Armed here and nowhere earlier. b3270's other toggle, `retry`, would keep retrying a connect that
            // failed, and that is what collides with everything ConnectAsync is built on: a failed Connect run
            // meaning the attempt is over is what raises ConnectionFailedException, what feeds the certificate
            // prompt, and what SessionViewModel's 30s timeout measures. `reconnect` armed after success touches
            // none of it (spec 6.1).
            //
            // Not throwOnFailure: the connect has already succeeded, and turning a live session into a thrown
            // exception and an error banner because a Set was refused would be a worse outcome than the
            // auto-reconnect simply not happening.
            if (Profile.AutoReconnect)
                await RunAsync([new B3270Action("Set", "reconnect", "true")], throwOnFailure: false, cancellationToken: cancellationToken);
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/LizTerm.Backend.B3270.Tests --filter "FullyQualifiedName~B3270SessionConnectTests"`
Expected: PASS, all three plus the existing ones.

- [ ] **Step 5: Commit**

```bash
git add src/LizTerm.Backend.B3270/B3270Session.cs tests/LizTerm.Backend.B3270.Tests/B3270SessionConnectTests.cs
git commit -m "Arm b3270's reconnect after a connect succeeds (#28)

reconnect only, never retry. Armed strictly after the Connect run
returned, so the first-connect path is untouched by construction rather
than by care: a failed Connect run still means the attempt is over,
which is what ConnectionFailedException, the certificate prompt and the
30-second timeout all rest on.

No interface change — the session reads its own profile, so
IEmulatorSession and FakeEmulatorSession are unaffected.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Task 13: Disarm before every disconnect

**Files:**
- Modify: `src/LizTerm.Backend.B3270/B3270Session.cs` (`DisconnectAsync`, `TryDisconnectQuietlyAsync`)
- Test: `tests/LizTerm.Backend.B3270.Tests/B3270SessionConnectTests.cs`

**Interfaces:**
- Produces: `private Task DisarmReconnectAsync()` on `B3270Session`, called by both disconnect paths.

**This is the task the milestone exists to get right.** Measured against 4.5ga6: b3270 reconnects from the user's own `Disconnect` action when `reconnect` is armed, so without this, File → Disconnect silently does not work for a profile with auto-reconnect on. Write the test first and watch it fail for that reason.

- [ ] **Step 1: Write the failing tests**

Append to `tests/LizTerm.Backend.B3270.Tests/B3270SessionConnectTests.cs`:

```csharp
    /// <summary>THE behaviour this task exists for. Measured against 4.5ga6: with reconnect armed, sending the
    /// Disconnect action alone moves the engine straight to `reconnecting` and the session is back up two
    /// seconds later — the user's Disconnect is silently undone, on the most ordinary path in the app. Only
    /// Set(reconnect,false) stops it, and it has to go out first (spec 6.2).</summary>
    [Fact]
    public async Task An_explicit_Disconnect_disarms_reconnect_before_it_sends_Disconnect()
    {
        var fake = new FakeB3270Process();
        await using var session = new B3270Session(Reconnecting, () => fake);
        await session.ConnectAsync(cancellationToken: TestContext.Current.CancellationToken);
        fake.Emit("""{"connection":{"state":"connected-3270","host":"h","cause":"ui"}}""");
        await Wait.UntilAsync(() => session.ConnectionState == ConnectionState.Connected3270, "the session to come up");

        var disconnecting = session.DisconnectAsync();
        // Emit the close only once the Disconnect has gone out: emitting it earlier would satisfy
        // DisconnectAsync's own early-out and the test would prove nothing.
        await Wait.UntilAsync(() => fake.InputLines.Any(l => l.Contains("\"Disconnect\"")), "the Disconnect to go out");
        fake.Emit("""{"connection":{"state":"not-connected"}}""");
        await disconnecting;

        var lines = fake.InputLines.ToList();
        var disarm = lines.FindIndex(l => l.Contains("\"reconnect\"") && l.Contains("\"false\""));
        var sent = lines.FindIndex(l => l.Contains("\"Disconnect\""));
        Assert.True(disarm >= 0, "no Set(reconnect,false) was sent, so the engine would reconnect from the Disconnect itself");
        Assert.True(disarm < sent, "the disarm must precede the Disconnect");
    }

    [Fact]
    public async Task A_profile_without_auto_reconnect_sends_no_disarm()
    {
        var fake = new FakeB3270Process();
        await using var session = new B3270Session(Reconnecting with { AutoReconnect = false }, () => fake);
        await session.ConnectAsync(cancellationToken: TestContext.Current.CancellationToken);
        fake.Emit("""{"connection":{"state":"connected-3270","host":"h","cause":"ui"}}""");
        await Wait.UntilAsync(() => session.ConnectionState == ConnectionState.Connected3270, "the session to come up");

        var disconnecting = session.DisconnectAsync();
        await Wait.UntilAsync(() => fake.InputLines.Any(l => l.Contains("\"Disconnect\"")), "the Disconnect to go out");
        fake.Emit("""{"connection":{"state":"not-connected"}}""");
        await disconnecting;

        Assert.DoesNotContain(fake.InputLines, l => l.Contains("\"reconnect\""));
    }

    /// <summary>The disarm is also what produces the state the wait is waiting for: an armed drop never reports
    /// not-connected on its own (spec 6.3), so a Disconnect that did not disarm would sit out the whole
    /// DisconnectTimeout. Here the engine answers normally and the call returns well inside it.</summary>
    [Fact]
    public async Task Disconnecting_an_armed_session_does_not_sit_out_the_disconnect_timeout()
    {
        var fake = new FakeB3270Process();
        await using var session = new B3270Session(Reconnecting, () => fake) { DisconnectTimeout = TimeSpan.FromSeconds(5) };
        await session.ConnectAsync(cancellationToken: TestContext.Current.CancellationToken);
        fake.Emit("""{"connection":{"state":"connected-3270","host":"h","cause":"ui"}}""");
        await Wait.UntilAsync(() => session.ConnectionState == ConnectionState.Connected3270, "the session to come up");

        var started = DateTime.UtcNow;
        var disconnecting = session.DisconnectAsync();
        await Wait.UntilAsync(() => fake.InputLines.Any(l => l.Contains("\"Disconnect\"")), "the Disconnect to go out");
        fake.Emit("""{"connection":{"state":"not-connected"}}""");
        await disconnecting;

        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(3), "DisconnectAsync waited out its timeout");
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/LizTerm.Backend.B3270.Tests --filter "FullyQualifiedName~B3270SessionConnectTests"`
Expected: `An_explicit_Disconnect_disarms_reconnect_before_it_sends_Disconnect` FAILS with "no Set(reconnect,false) was sent…". The other two pass already.

- [ ] **Step 3: Add the disarm to both paths**

In `src/LizTerm.Backend.B3270/B3270Session.cs`, replace `DisconnectAsync` with:

```csharp
    /// <summary>Completes once b3270 has reported the connection closed (or the process has ended), not
    /// merely once it has accepted the Disconnect action, so callers can rely on the state afterwards.</summary>
    public async Task DisconnectAsync()
    {
        if (_process is null) return;
        // Before the early-out, deliberately. Measured against 4.5ga6: with reconnect armed, b3270 reconnects
        // from the Disconnect action itself — the state goes straight to `reconnecting` and the session is back
        // two seconds later — so a Disconnect sent without this is silently undone. The early-out turns out to
        // be unreachable during a reconnect anyway, because the engine never reports not-connected while armed;
        // the disarm stays ahead of it because that is an engine behaviour we have measured once and cannot
        // enforce, and one extra action on this path costs nothing (spec 6.2).
        await DisarmReconnectAsync();
        if (ConnectionState == ConnectionState.Disconnected) return;
        await RunRawAsync([new B3270Action("Disconnect")]);
        await WaitForDisconnectedAsync();
    }

    /// <summary>Turns b3270's own reconnect off, which is the only thing that stops one: the Disconnect action
    /// does not clear the intent. It is also what makes the engine report `not-connected` during a reconnect —
    /// about 50 ms later, measured — which is the state <see cref="WaitForDisconnectedAsync"/> is waiting for.
    /// Quiet, and bounded: the caller is on its way to disconnecting, and neither a refused Set nor a wedged
    /// engine may be what stops it.</summary>
    private async Task DisarmReconnectAsync()
    {
        if (!Profile.AutoReconnect || _process is null) return;
        try
        {
            await RunRawAsync([new B3270Action("Set", "reconnect", "false")], DisconnectTimeout);
        }
        catch (Exception)
        {
            // The process may be gone, or the engine may accept the Set and never answer it. The Disconnect
            // that follows deals with both.
        }
    }
```

And in `TryDisconnectQuietlyAsync`, add the disarm as the first line of its `try`:

```csharp
    private async Task TryDisconnectQuietlyAsync()
    {
        try
        {
            // The cancel path disconnects too, so it needs the same disarm: a user pressing Connect again while
            // a reconnect is churning must not leave the old intent behind.
            await DisarmReconnectAsync();
            await RunRawAsync([new B3270Action("Disconnect")], DisconnectTimeout);
        }
```

(the existing `catch` and the rest of the method are unchanged).

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/LizTerm.Backend.B3270.Tests --filter "FullyQualifiedName~B3270SessionConnectTests"`
Expected: PASS.

- [ ] **Step 5: Run the whole backend project**

Run: `dotnet test tests/LizTerm.Backend.B3270.Tests`
Expected: PASS. The disarm is a no-op for every existing test, all of which use profiles with `AutoReconnect` at its `false` default.

- [ ] **Step 6: Commit**

```bash
git add src/LizTerm.Backend.B3270/B3270Session.cs tests/LizTerm.Backend.B3270.Tests/B3270SessionConnectTests.cs
git commit -m "Disarm reconnect before every disconnect (#28)

Measured against 4.5ga6, and it is the reason this milestone specified
#28 rather than shipping two toggles: b3270 reconnects from the user's
own Disconnect action when reconnect is armed. The state goes straight
to reconnecting and the session is back two seconds later, so without
this, File > Disconnect silently does not work — the most ordinary path
in the app, and one no test against a fake would have caught, because
the fake does what we assume rather than what the engine does.

The Set also produces the not-connected indication about 50ms later,
which is the state WaitForDisconnectedAsync waits for; an armed drop
never reports it on its own, so the disarm is what keeps that wait off
its five-second timeout.

Both disconnect paths take it, the cancel path included.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Task 14: Full verification

**Files:** none.

- [ ] **Step 1: Full suite**

Run: `dotnet test LizTerm.slnx`
Expected: every project passes; the live host tests skip unless `LIZTERM_TEST_HOST` is set.

- [ ] **Step 2: Zero warnings**

Run: `dotnet build LizTerm.slnx --no-incremental 2>&1 | grep -c " warning "`
Expected: `0`. An incremental build hides warnings from projects it did not recompile.

- [ ] **Step 3: License headers**

Run: `dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~RepositoryHeadersTests"`
Expected: PASS. The two files created by this plan — `OversizeGeometry.cs` and `ProfilePickerWindowTests.cs` — each carry the three-line header.

- [ ] **Step 4: Manual pass on macOS against a live host**

CI cannot answer any of these.

```bash
dotnet run --project src/LizTerm.App
```

In the picker:
- Type the live host into Quick Connect and press **Enter** with a profile also selected in the list. The typed host connects, not the selected profile.
- Type a saved profile's exact name. That profile connects.
- Type `tk5`. The inline message suggests `tk5:23` rather than just refusing.

In the editor (New...):
- Keep-alive shows `60`. Set it to `abc` and Save: the message names the range. Set `0` and Save: it saves.
- Oversize `200x200` and Save: the message names 40,000 cells and b3270's 16,383.
- Oversize `132x43` with model 2, then switch to model 5: the message appears without pressing Save. Switch back: it clears.

In a session:
- **File > Save as Profile...** on the Quick Connect session. The editor opens pre-filled with the host and port. Save, then connect the new profile from the picker.
- Save a profile with `Oversize 100x50` and connect: the window renders at the new geometry.

Auto-reconnect, the one that needs the engine (use the probe harness from the spec's §9.1 — a TCP proxy in front of the host — or stop and restart the Hercules listener):
- With auto-reconnect **on**, drop the connection. The status bar reads `Reconnecting to <host>` and the session comes back. Connect is greyed out and Disconnect is live throughout.
- With auto-reconnect **on** and the session up, press **File > Disconnect**. It stays disconnected. *This is the one that fails without Task 13.*
- With auto-reconnect **off**, drop the connection. It stays down.

- [ ] **Step 5: Commit anything the manual pass corrected**

```bash
git add -A
git commit -m "Correct <what the manual pass found>"
```

If the manual pass found nothing, skip this step.

---

## Notes for the executor

- **Do not `cd` out of the worktree.** All commands run from the worktree root.
- **Tasks 1-3 are ordered** (2 needs 1's type only at Task 5; 3 needs 2). Tasks 4-5 are ordered and both need 2. Tasks 6-7 are ordered. Tasks 8-9 are ordered. Task 11 needs nothing. Tasks 12-13 are ordered and need 2. Task 10 is independent of everything but Task 2.
- **#28 is deliberately last.** It is the only part of this milestone that changes existing behaviour rather than adding to it. If Tasks 12-13 have to be reverted, nothing else in the milestone goes with them.
- **Do not add b3270's `retry` toggle.** It is out of scope by an explicit decision (spec §6.1, §10) and it is the half that collides with `ConnectAsync`'s error, timeout and certificate-prompt paths. If a task seems to need it, re-read §6.1.
- **Do not "fix" the column/row order.** `TerminalModel.ToString()` is rows x columns and `-oversize` is columns x rows. Both are correct; the placeholder and the messages are what resolve it (spec §4.3). `CatalogueTests` asserts the drop-down's text exactly, so changing it fails the suite as well as the spec.
- **Do not add a member to `IEmulatorSession`.** #28 needs none: `B3270Session` is bound to one `SessionProfile` at construction and reads `Profile.AutoReconnect` itself. If a task seems to need one, re-read §6.1.
- **If a line number in this plan does not match**, the file has moved under you — find the symbol by name rather than trusting the number, and say so in the commit.
- **Never commit a wire log**: the live IND$FILE test types a password through the session.
- **The version bump is not part of this plan.** v0.4.0 is cut after this milestone lands, and `release.yml`'s `version` job requires `Directory.Build.props`, `LizTerm.parcel` and the tag to agree — all three still read 0.3.0 today.
