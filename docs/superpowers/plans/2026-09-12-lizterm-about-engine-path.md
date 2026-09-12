# Drop the engine path from About — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove the b3270 binary's full path from the About window, leaving the engine line — `b3270 4.5.6, bundled` or `b3270 4.5.6, from LIZTERM_B3270_PATH` — as the whole of what About says about the engine.

**Architecture:** Three sites, all in About: a `TextBlock` in the XAML, the line in the code-behind that fills it, and one assertion in `AboutWindowTests`. `EngineInfo.Path` stays — the backend needs it to start the process — and `StatusFormatter.Engine` is untouched, because it already reports provenance without a path. The assertion is replaced rather than deleted, so the removal stays guarded.

**Tech Stack:** .NET 10, C# 14, Avalonia 12.1.2 (headless XUnit lane), xunit.v3 in VSTest mode.

**Spec:** `docs/superpowers/specs/2026-09-12-lizterm-about-engine-path-design.md`

## Global Constraints

- **Worktree only.** All work happens in `/Users/robert/ClaudeSandbox/LizTerm/.claude/worktrees/issue-75-review-797673`, on branch `claude/issue-75-review-797673`. Never run git in, or write to, the main checkout at `/Users/robert/ClaudeSandbox/LizTerm`.
- **Delegated approval.** Robert delegated this slice on 2026-09-12: self-approve the spec and plan, implement, review, commit, merge, and triage CI. The usual gate on pushing and merging does not apply to this slice and to no other.
- **Dependency rule.** Nothing here crosses a project boundary. `LizTerm.Core` keeps `EngineInfo.Path`; `LizTerm.App` keeps naming the backend only in `SessionFactory.cs`.
- **Licence headers.** No file is created, so no new header is needed. Do not disturb the three-line headers on the files being modified; `RepositoryHeadersTests` fails the suite for a missing one.
- **Test commands.** Full suite: `dotnet test LizTerm.slnx` (the live-host tests skip themselves; the About tests are headless and need no engine, so `LIZTERM_B3270_PATH` is not required for this slice). One class: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~AboutWindowTests"`.
- **Zero warnings.** Before the task is called done: `dotnet build LizTerm.slnx --no-incremental 2>&1 | grep -c " warning "` prints `0`. CI builds with `-warnaserror`.
- **Design history is a record.** Edit nothing under `docs/superpowers/` except this plan's own checkboxes. In particular the v1 design spec's §8 keeps its wording; it is the test comment that stops citing it, not the spec that changes.
- **Exact strings.** The engine line's format is fixed by `StatusFormatter.Engine` and does not change: `b3270 4.5.6 (fake), from LIZTERM_B3270_PATH` for the override fixture used in the tests.
- **Commits** end with the line `Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>`.

---

## File Structure

**Created**

None.

**Modified**

| File | Change |
|---|---|
| `src/LizTerm.App/Views/AboutWindow.axaml` | Delete the `EnginePathText` `TextBlock` (line 21). |
| `src/LizTerm.App/Views/AboutWindow.axaml.cs` | Delete `EnginePathText.Text = engine.Path;` (line 23). |
| `tests/LizTerm.App.Tests/Views/AboutWindowTests.cs` | Replace the path assertion with a no-path guard; rewrite the `SizeToContent` comment. |

---

### Task 1: About names the engine without its path

**Files:**
- Modify: `src/LizTerm.App/Views/AboutWindow.axaml:21`
- Modify: `src/LizTerm.App/Views/AboutWindow.axaml.cs:23`
- Test: `tests/LizTerm.App.Tests/Views/AboutWindowTests.cs:16-26`

**Interfaces:**
- Consumes: `EngineInfo(string Name, string? Version, string Path, EngineSource Source)` from `LizTerm.Core.Session`; `AboutWindow(string version, EngineInfo engine, string overrideOrigin)`; `StatusFormatter.Engine(EngineInfo, string)` which yields `"b3270 4.5.6 (fake), from LIZTERM_B3270_PATH"` for the fixture below.
- Produces: nothing new. `EngineInfo.Path` keeps its meaning and every other consumer of it is unchanged.

- [ ] **Step 1: Rewrite the failing test**

In `tests/LizTerm.App.Tests/Views/AboutWindowTests.cs`, replace the whole of `Shows_version_engine_and_licenses` with this. The fixture stays on `EngineSource.Override` deliberately: that is the case where showing a path is most defensible, so it is the case worth pinning.

```csharp
    [AvaloniaFact]
    public void Shows_version_engine_and_licenses()
    {
        const string path = "/opt/homebrew/bin/b3270";
        var engine = new EngineInfo("b3270", "4.5.6 (fake)", path, EngineSource.Override);
        var window = new AboutWindow("0.3.0", engine, "LIZTERM_B3270_PATH");
        window.Show();
        // The engine line and the copyright both wrap, above a fixed 220-high licences box, so the window
        // grows with its content instead of clipping at a fixed height.
        Assert.Equal(SizeToContent.Height, window.SizeToContent);
        Assert.Equal(220, window.FindControl<TextBox>("LicensesText")!.Height);
        Assert.Equal("Version 0.3.0", window.FindControl<TextBlock>("VersionText")!.Text);
        Assert.Equal("b3270 4.5.6 (fake), from LIZTERM_B3270_PATH", window.FindControl<TextBlock>("EngineText")!.Text);
    }

    /// <summary>The binary's full path was shown under the engine name from before the engine was built and
    /// bundled on all six RIDs, when "which binary is this actually running?" was live. It is not: the engine
    /// line already says bundled or names the override, and the path is a string the user cannot act on.</summary>
    [AvaloniaFact]
    public void Names_the_engine_without_showing_its_path()
    {
        const string path = "/opt/homebrew/bin/b3270";
        var window = new AboutWindow("0.3.0", new EngineInfo("b3270", "4.5.6 (fake)", path, EngineSource.Override), "LIZTERM_B3270_PATH");
        window.Show();
        Assert.All(window.GetVisualDescendants().OfType<TextBlock>(),
            block => Assert.DoesNotContain(path, block.Text ?? ""));
    }
```

Add `using Avalonia.VisualTree;` to the file's usings, beside `using Avalonia.Controls;`, for `GetVisualDescendants()`. `System.Linq` arrives through `ImplicitUsings`.

- [ ] **Step 2: Run the tests to verify the new one fails**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~AboutWindowTests"`

Expected: FAIL. `Names_the_engine_without_showing_its_path` reports that a `TextBlock` contains `/opt/homebrew/bin/b3270` — that is `EnginePathText`, still on screen. `Shows_version_engine_and_licenses` passes already; it lost an assertion rather than gaining one.

- [ ] **Step 3: Delete the path line from the view**

In `src/LizTerm.App/Views/AboutWindow.axaml`, delete this line entirely:

```xml
    <TextBlock DockPanel.Dock="Top" x:Name="EnginePathText" Foreground="#A0A0A0" FontSize="12" TextWrapping="Wrap" />
```

Change no margin on the lines around it. `EngineText` keeps `Margin="0,12,0,0"` and `CopyrightText` keeps `Margin="0,12,0,0"`; the deleted line carried no margin of its own, so the spacing on screen is unchanged.

In `src/LizTerm.App/Views/AboutWindow.axaml.cs`, delete this line from the constructor:

```csharp
        EnginePathText.Text = engine.Path;
```

Leave the design-time constructor alone — `new EngineInfo("b3270", "4.5.6", "/path/to/b3270", EngineSource.Bundled)` still needs a path because `EngineInfo` is a positional record, and the preview simply stops showing it.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~AboutWindowTests"`

Expected: PASS, six tests.

- [ ] **Step 5: Run the full suite and the zero-warning check**

Run: `dotnet test LizTerm.slnx`

Expected: PASS. The live-host tests skip themselves.

Run: `dotnet build LizTerm.slnx --no-incremental 2>&1 | grep -c " warning "`

Expected: `0`. A stale `x:Name` left in the code-behind would surface here as a compile error rather than a warning, so a clean build is also the proof that no reference to `EnginePathText` survives.

- [ ] **Step 6: Confirm the name is gone from the repository**

Run: `grep -rn "EnginePathText" src tests docs`

Expected: no matches. (The spec and this plan describe the removal in prose and in the tables above; neither contains the bare identifier in a form this grep would hit outside a code fence. A hit under `docs/superpowers/` from an older document is history and stays.)

- [ ] **Step 7: Commit**

```bash
git add src/LizTerm.App/Views/AboutWindow.axaml src/LizTerm.App/Views/AboutWindow.axaml.cs tests/LizTerm.App.Tests/Views/AboutWindowTests.cs
git commit -m "Drop the engine path from About, and say why the window still grows

Closes #75"
```

---

## Self-Review

**Spec coverage.** §2 (remove unconditionally) → Step 3, which deletes the `TextBlock` outright with no `IsVisible`. §3 (what stays) → Step 3's instruction to leave the design-time constructor and every other consumer of `EngineInfo.Path` alone, and Step 5's full suite, which covers `StatusFormatter` and the status bar. §4 (layout) → Step 3's explicit "change no margin". §5 (testing) → Steps 1, 2 and 4, including the rewritten `SizeToContent` comment and the dropped Spec 8 citation. §6 (sites) → the File Structure table. §7 (documentation: none) → no documentation step, deliberately.

**Placeholders.** None. Every code step carries the actual text to write or delete.

**Type consistency.** `EngineInfo` is used with the same four positional arguments the record declares; `FindControl<TextBlock>` and `FindControl<TextBox>` match the control types in the XAML; `GetVisualDescendants()` needs the `Avalonia.VisualTree` using that Step 1 adds.
