# Help links and the bundled offline user guide — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Put the user guide and three project links in LizTerm's Help menu, with the guide shipped inside the
application as HTML and opened in the user's own browser, offline.

**Architecture:** `docs/user-guide.md` is converted to one self-contained HTML file by a converter that lives in
the test project and never ships. The generated file is committed at `src/LizTerm.App/Assets/Docs/user-guide.html`,
picked up by the existing `Assets\**` `AvaloniaResource` glob, and held to its Markdown source by a golden-file
test. At run time the app reads the resource through `AssetLoader`, writes it to the temp directory and hands it
to the platform launcher. A new `IUriOpener` beside the existing `IFolderOpener` carries both that and the three
GitHub links.

**Tech Stack:** .NET 10, Avalonia 12.1.2, CommunityToolkit.Mvvm `[RelayCommand]`, xunit.v3 (VSTest mode),
Avalonia.Headless.XUnit. No new package references in any project.

**Spec:** `docs/superpowers/specs/2026-09-12-lizterm-offline-docs-design.md`

## Global Constraints

- **Licence headers.** Every new `.cs` and `.axaml` file starts with three lines in comment syntax:
  `This file is part of LizTerm.`, `Copyright 2026 by CoffeeMuse`, `SPDX-License-Identifier: BSD-3-Clause`.
  `RepositoryHeadersTests` fails the suite for a missing one. Generated files are exempt: the HTML is not
  hand-written and is not a scanned file type.
- **Dependency rule.** `LizTerm.Core` depends only on the BCL. `LizTerm.App` gains no package reference in this
  work. The converter is test-only code and is never referenced by any `src/` project.
- **Zero warnings.** CI builds with `-warnaserror`. Before calling any task done:
  `dotnet build LizTerm.slnx --no-incremental 2>&1 | grep -c " warning "` must print `0`.
- **Test filters** use `FullyQualifiedName~`, e.g.
  `dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~UserGuideHtmlTests"`.
- **Ordering is load-bearing.** Tasks 1–3 are the links; Tasks 4–10 are the bundle. Spec §12 keeps "ship the
  links, defer the bundle" available as a reduction, and that is only true if the links land first. Do not
  reorder.
- **A fresh worktree has no `native/out`,** so running the app needs `LIZTERM_B3270_PATH` (a Homebrew `b3270`
  works). None of the tests in this plan need the engine.

## File Structure

**Created**

| File | Responsibility |
|---|---|
| `src/LizTerm.App/ProjectLinks.cs` | The project's URLs, as constants both XAML and tests bind to |
| `src/LizTerm.App/Files/IUriOpener.cs` | Opens a URI or a local file through the platform launcher |
| `src/LizTerm.App/Files/AvaloniaUriOpener.cs` | The `TopLevel.Launcher` implementation |
| `src/LizTerm.App/Documentation/UserGuide.cs` | Reads the embedded HTML and writes it to a temp file |
| `src/LizTerm.App/Assets/Docs/user-guide.html` | **Generated.** Never hand-edited |
| `tests/LizTerm.App.Tests/Fakes/FakeUriOpener.cs` | Records targets, returns a settable result |
| `tests/LizTerm.Core.Tests/Documentation/UserGuideHtml.cs` | The converter. Test-only; never ships |
| `tests/LizTerm.Core.Tests/Documentation/UserGuideHtmlTests.cs` | Converter unit tests |
| `tests/LizTerm.Core.Tests/Documentation/UserGuideAssetTests.cs` | Golden file, construct guard, anchor guard |
| `tests/LizTerm.App.Tests/Documentation/UserGuideTests.cs` | Extraction |
| `tests/LizTerm.App.Tests/ViewModels/SessionViewModelLinkTests.cs` | The command and its failure path |

**Modified**

| File | Change |
|---|---|
| `src/LizTerm.App/ViewModels/SessionViewModel.cs` | `IUriOpener` parameter, `OpenLinkCommand`, `ShowUserGuideCommand` |
| `src/LizTerm.App/Views/SessionWindow.axaml` | Four items in both Help menus (native ~line 138, classic ~line 230) |
| `src/LizTerm.App/App.axaml.cs:132` | Pass `new AvaloniaUriOpener(window)` |
| `tests/LizTerm.App.Tests/Views/NativeMenuTests.cs` | Compare `CommandParameter` for `_Help`; the new items |
| `docs/user-guide.md` | Its Menus section gains the four Help items |
| `docs/development.md` | The regeneration command and the never-hand-edit rule |
| `src/LizTerm.App/CLAUDE.md`, `tests/CLAUDE.md` | The new seam and where the converter lives |

### One deviation from the spec, flagged for approval

Spec §4.1 places the converter in `tests/LizTerm.App.Tests/`. This plan puts it in
`tests/LizTerm.Core.Tests/Documentation/` instead, for the reason `tests/CLAUDE.md` already gives for
`RepositoryHeadersTests`: *"It lives in Core.Tests because that project builds on every run, so a new file is
caught locally rather than in CI."* The same applies exactly — editing `docs/user-guide.md` without
regenerating should fail on the developer's machine, immediately. The converter is pure BCL, so it fits Core's
constraints, and `LicenseHeader.Root()` is already in that project and is reused to find the repository root.
The App-side tests (extraction, menus) stay in `LizTerm.App.Tests`, where they need Avalonia.

---

## Task 1: The link seam

**Files:**
- Create: `src/LizTerm.App/ProjectLinks.cs`
- Create: `src/LizTerm.App/Files/IUriOpener.cs`
- Create: `src/LizTerm.App/Files/AvaloniaUriOpener.cs`
- Create: `tests/LizTerm.App.Tests/Fakes/FakeUriOpener.cs`
- Modify: `src/LizTerm.App/ViewModels/SessionViewModel.cs`
- Test: `tests/LizTerm.App.Tests/ViewModels/SessionViewModelLinkTests.cs`

**Interfaces:**
- Produces: `LizTerm.App.ProjectLinks.Repository`, `.NewIssue`, `.Releases` (`const string`);
  `LizTerm.App.Files.IUriOpener` with `Task<bool> OpenAsync(Uri uri)` and `Task<bool> OpenFileAsync(string path)`;
  `SessionViewModel.OpenLinkCommand` (parameter `string`); `FakeUriOpener` with `Opened` (`List<string>`),
  `Result` (`bool`), `Exception` (`Exception?`).

- [ ] **Step 1: Write the failing test**

Create `tests/LizTerm.App.Tests/Fakes/FakeUriOpener.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.App.Files;

namespace LizTerm.App.Tests.Fakes;

public sealed class FakeUriOpener : IUriOpener
{
    public bool Result { get; set; } = true;
    public Exception? Exception { get; set; }
    public List<string> Opened { get; } = [];

    public Task<bool> OpenAsync(Uri uri)
    {
        Opened.Add(uri.ToString());
        return Exception is null ? Task.FromResult(Result) : Task.FromException<bool>(Exception);
    }

    public Task<bool> OpenFileAsync(string path)
    {
        Opened.Add(path);
        return Exception is null ? Task.FromResult(Result) : Task.FromException<bool>(Exception);
    }
}
```

Create `tests/LizTerm.App.Tests/ViewModels/SessionViewModelLinkTests.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.App;
using LizTerm.App.Tests.Fakes;
using LizTerm.App.ViewModels;

namespace LizTerm.App.Tests.ViewModels;

public class SessionViewModelLinkTests
{
    private static (SessionViewModel Vm, FakeUriOpener Opener) Build()
    {
        var opener = new FakeUriOpener();
        var vm = new SessionViewModel(new FakeEmulatorSession(), action => action(), new FakeTextClipboard(),
            uriOpener: opener);
        return (vm, opener);
    }

    [Fact]
    public async Task Opening_a_link_hands_the_url_to_the_opener()
    {
        var (vm, opener) = Build();

        await vm.OpenLinkCommand.ExecuteAsync(ProjectLinks.NewIssue);

        Assert.Equal([ProjectLinks.NewIssue], opener.Opened);
        Assert.Null(vm.ErrorMessage);
    }

    [Fact]
    public async Task A_platform_that_cannot_open_the_url_names_it_in_the_banner()
    {
        var (vm, opener) = Build();
        opener.Result = false;

        await vm.OpenLinkCommand.ExecuteAsync(ProjectLinks.Repository);

        Assert.Equal($"Could not open a browser. The page is at {ProjectLinks.Repository}.", vm.ErrorMessage);
    }

    [Fact]
    public async Task An_opener_that_throws_is_treated_as_a_failure_to_open()
    {
        var (vm, opener) = Build();
        opener.Exception = new InvalidOperationException("no browser");

        await vm.OpenLinkCommand.ExecuteAsync(ProjectLinks.Releases);

        Assert.Equal($"Could not open a browser. The page is at {ProjectLinks.Releases}.", vm.ErrorMessage);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~SessionViewModelLinkTests"`
Expected: FAIL — `IUriOpener`, `ProjectLinks` and `OpenLinkCommand` do not exist; the build breaks.

- [ ] **Step 3: Write the implementation**

Create `src/LizTerm.App/ProjectLinks.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

namespace LizTerm.App;

/// <summary>The project's own pages, as the Help menu offers them. Constants rather than literals in XAML so
/// that the two menus bind the same string: the native-menu parity guard compares CommandParameter by
/// equality, and two spellings of one URL would pass it while sending a user somewhere else.</summary>
public static class ProjectLinks
{
    private const string Repo = "https://github.com/coffeemuse/LizTerm";

    public const string Repository = Repo;

    /// <summary>The chooser, not a blank issue: it lands on the bug-report and feature-request forms, which ask
    /// for the operating system, the LizTerm version, the host, TLS and the engine source.</summary>
    public const string NewIssue = Repo + "/issues/new/choose";

    public const string Releases = Repo + "/releases";
}
```

Create `src/LizTerm.App/Files/IUriOpener.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

namespace LizTerm.App.Files;

/// <summary>Opens a web page or a local file in whatever the platform has associated with it. Injected like
/// <see cref="IFolderOpener"/>, which it sits beside.</summary>
public interface IUriOpener
{
    /// <returns>False when the platform could not open it; the caller then shows the URL instead.</returns>
    Task<bool> OpenAsync(Uri uri);

    /// <summary>A path rather than a file:// URI: the extracted user guide lives under the system temp
    /// directory, which on Windows routinely contains a space, and handing the platform a FileInfo avoids URI
    /// escaping rather than getting it right.</summary>
    Task<bool> OpenFileAsync(string path);
}
```

Create `src/LizTerm.App/Files/AvaloniaUriOpener.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Controls;

namespace LizTerm.App.Files;

public sealed class AvaloniaUriOpener(TopLevel topLevel) : IUriOpener
{
    public Task<bool> OpenAsync(Uri uri) => topLevel.Launcher.LaunchUriAsync(uri);

    public Task<bool> OpenFileAsync(string path) => topLevel.Launcher.LaunchFileInfoAsync(new FileInfo(path));
}
```

In `src/LizTerm.App/ViewModels/SessionViewModel.cs`, add the field beside `_folderOpener` (line 29):

```csharp
    private readonly IUriOpener? _uriOpener;
```

Append the parameter to the constructor — **last**, so every positional caller keeps working — with its doc
comment beside the others:

```csharp
    /// <param name="uriOpener">Opens the Help menu's pages and the extracted user guide; null for tests that
    /// don't cover them.</param>
```

```csharp
        IBellRinger? bellRinger = null, BellThrottle? bellThrottle = null, IUriOpener? uriOpener = null)
```

and assign it beside `_folderOpener = folderOpener;`:

```csharp
        _uriOpener = uriOpener;
```

Add the command next to `ShowWireLogsAsync`:

```csharp
    /// <summary>Help's project links. The failure path is ShowWireLogsAsync's: when the platform cannot open
    /// the target, name it in the banner so a user with no browser association can still read and copy it.</summary>
    [RelayCommand]
    private async Task OpenLinkAsync(string url)
    {
        try
        {
            if (_uriOpener is not null && await _uriOpener.OpenAsync(new Uri(url))) return;
        }
        catch (Exception)
        {
            // Fall through to naming the URL.
        }
        ErrorMessage = $"Could not open a browser. The page is at {url}.";
    }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~SessionViewModelLinkTests"`
Expected: PASS, 3 tests.

Then the whole suite, because the constructor changed:
Run: `dotnet test LizTerm.slnx`
Expected: PASS.

Then: `dotnet build LizTerm.slnx --no-incremental 2>&1 | grep -c " warning "` → `0`.

- [ ] **Step 5: Commit**

```bash
git add src/LizTerm.App/ProjectLinks.cs src/LizTerm.App/Files/IUriOpener.cs src/LizTerm.App/Files/AvaloniaUriOpener.cs src/LizTerm.App/ViewModels/SessionViewModel.cs tests/LizTerm.App.Tests/Fakes/FakeUriOpener.cs tests/LizTerm.App.Tests/ViewModels/SessionViewModelLinkTests.cs
git commit -m "Give the view model a way to open a page, and somewhere to send it"
```

---

## Task 2: Close the parity guard's blind spot

**Files:**
- Modify: `tests/LizTerm.App.Tests/Views/NativeMenuTests.cs` (the `rootHeader == "_Keys"` block in
  `AssertMenusMatch`, ~line 744)

**Interfaces:**
- Consumes: nothing from Task 1.
- Produces: a parity guard that compares `CommandParameter` for `_Help`, which Task 3 relies on to catch a
  wrong URL on one of the two menus.

**Why this is its own task:** the guard must be widened *before* the items that need guarding exist, otherwise
Task 3 lands three URLs on two menus with nothing comparing them.
`The_native_menu_matches_the_classic_menu_item_for_item` compares headers and commands, and every Help link
binds the same `OpenLinkCommand` instance — so the URL lives entirely in `CommandParameter`, which the walk
currently inspects only under `_Keys`. That is the same hazard the existing comment there describes for `PA1`
versus `PA2`.

- [ ] **Step 1: Write the failing test**

First extract the walk so it can be invoked on a window under test. Change the body of
`The_native_menu_matches_the_classic_menu_item_for_item` to call a new private helper, moving its existing code
verbatim:

```csharp
    [AvaloniaFact]
    public void The_native_menu_matches_the_classic_menu_item_for_item() => AssertParity(Show().Window);

    private static void AssertParity(SessionWindow window)
    {
        var classicTop = window.FindControl<Menu>("ClassicMenu")!.Items.OfType<MenuItem>().ToArray();
        var nativeTop = NativeMenu.GetMenu(window)!.Items.OfType<NativeMenuItem>().ToArray();
        Assert.Equal(classicTop.Length, nativeTop.Length);

        for (var i = 0; i < classicTop.Length; i++)
        {
            var topHeader = (string)classicTop[i].Header!;
            Assert.Equal(topHeader, nativeTop[i].Header);
            AssertMenusMatch(topHeader, topHeader, classicTop[i].Items.Cast<object>().ToArray(), [.. nativeTop[i].Menu!.Items]);
        }
    }
```

Then add:

```csharp
    /// <summary>The parity walk compares CommandParameter, which for Help is the whole of an item's meaning:
    /// every link binds the same OpenLinkCommand, so a native item pointed at the wrong page differs from its
    /// classic twin in nothing else. This breaks a Help item deliberately, because a guard that cannot fail is
    /// not a guard.</summary>
    [AvaloniaFact]
    public void The_parity_walk_compares_command_parameters_outside_the_keys_menu()
    {
        var (window, _, _, _) = Show();
        Item(window, "_Help", "_Wire Log").CommandParameter = "deliberately different";

        // Record.Exception rather than Assert.Throws<EqualException>: what matters is that the walk rejects
        // this, not which assertion inside it happened to fire first.
        Assert.NotNull(Record.Exception(() => AssertParity(window)));
    }
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~The_parity_walk_compares_command_parameters_outside_the_keys_menu"`
Expected: FAIL — the walk ignores `CommandParameter` outside `_Keys`, so nothing is thrown and
`Record.Exception` returns null.

- [ ] **Step 3: Write the implementation**

In `AssertMenusMatch`, replace the `_Keys`-only comparison with one that also covers `_Help`:

```csharp
            // Every Keys item binds SendKeyCommand and every Help link binds OpenLinkCommand, so header text
            // alone cannot tell "PA1" wired to TerminalKey.PA1 apart from "PA1" wired to TerminalKey.PA2, nor
            // "Releases" pointed at the issue tracker. Only the CommandParameter can, and getting either wrong
            // is invisible in the menu itself.
            if (rootHeader is "_Keys" or "_Help")
            {
                Assert.Equal(classicItem.CommandParameter, nativeItem.CommandParameter);
            }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~NativeMenuTests"`
Expected: PASS. The existing parity test is unaffected — today's Help items carry no `CommandParameter` on
either side, so both are null and equal.

- [ ] **Step 5: Commit**

```bash
git add tests/LizTerm.App.Tests/Views/NativeMenuTests.cs
git commit -m "Make the parity walk read Help's command parameters before Help has any"
```

---

## Task 3: The three links in both menus

**Files:**
- Modify: `src/LizTerm.App/Views/SessionWindow.axaml` (native menu ~line 138, classic menu ~line 230)
- Modify: `src/LizTerm.App/App.axaml.cs:132`
- Test: `tests/LizTerm.App.Tests/Views/NativeMenuTests.cs`

**Interfaces:**
- Consumes: `ProjectLinks.*` and `SessionViewModel.OpenLinkCommand` from Task 1; the widened guard from Task 2.
- Produces: three Help items on both menus. Task 9 adds a fourth above them.

- [ ] **Step 1: Write the failing test**

Add to `NativeMenuTests`:

```csharp
    [AvaloniaFact]
    public void Help_offers_the_three_project_links_on_both_menus()
    {
        var (window, _, _, _) = Show();
        var classic = window.FindControl<Menu>("ClassicMenu")!.Items.OfType<MenuItem>()
            .Single(m => (string)m.Header! == "_Help");

        foreach (var (header, url) in new[]
                 {
                     ("Project on _GitHub", ProjectLinks.Repository),
                     ("_Report an Issue...", ProjectLinks.NewIssue),
                     ("R_eleases", ProjectLinks.Releases),
                 })
        {
            var native = Item(window, "_Help", header);
            Assert.Equal(url, native.CommandParameter);
            Assert.NotNull(native.Command);

            var classicItem = classic.Items.OfType<MenuItem>().Single(m => (string)m.Header! == header);
            Assert.Equal(url, classicItem.CommandParameter);
        }
    }

    [AvaloniaFact]
    public async Task Choosing_a_project_link_opens_it()
    {
        var opener = new FakeUriOpener();
        var vm = new SessionViewModel(new FakeEmulatorSession(), action => action(), new FakeTextClipboard(),
            uriOpener: opener);
        var window = new SessionWindow(MenuStyle.Native, isMacOS: true) { DataContext = vm };
        window.Show();

        ((INativeMenuItemExporterEventsImplBridge)Item(window, "_Help", "_Report an Issue...")).RaiseClicked();
        await Wait.UntilAsync(() => opener.Opened.Count == 1, "the link to be opened");

        Assert.Equal([ProjectLinks.NewIssue], opener.Opened);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~NativeMenuTests"`
Expected: FAIL — `MenuLookup.Item` throws `no native menu item _Help > Project on _GitHub`.

- [ ] **Step 3: Write the implementation**

In `SessionWindow.axaml`, add `xmlns:app="using:LizTerm.App"` to the root element if it is not already declared.

In the **native** Help menu, before the `_Wire Log` item at line 143:

```xml
            <NativeMenuItem Header="Project on _GitHub" Command="{Binding OpenLinkCommand}"
                            CommandParameter="{x:Static app:ProjectLinks.Repository}" />
            <NativeMenuItem Header="_Report an Issue..." Command="{Binding OpenLinkCommand}"
                            CommandParameter="{x:Static app:ProjectLinks.NewIssue}" />
            <NativeMenuItem Header="R_eleases" Command="{Binding OpenLinkCommand}"
                            CommandParameter="{x:Static app:ProjectLinks.Releases}" />
            <NativeMenuItemSeparator />
```

In the **classic** Help menu, before `WireLogMenuItem` at line 231, the same four in their classic spelling:

```xml
        <MenuItem Header="Project on _GitHub" Command="{Binding OpenLinkCommand}"
                  CommandParameter="{x:Static app:ProjectLinks.Repository}" />
        <MenuItem Header="_Report an Issue..." Command="{Binding OpenLinkCommand}"
                  CommandParameter="{x:Static app:ProjectLinks.NewIssue}" />
        <MenuItem Header="R_eleases" Command="{Binding OpenLinkCommand}"
                  CommandParameter="{x:Static app:ProjectLinks.Releases}" />
        <Separator />
```

No `Gesture` on any of them: a native gesture is an AppKit key equivalent dispatched ahead of the window's
responder chain and would take the keystroke from the terminal. `Only_the_edit_menu_carries_gestures` enforces
it.

In `src/LizTerm.App/App.axaml.cs`, in the `SessionViewModel` construction whose
`new AvaloniaFolderOpener(window),` argument is at line 132, add the new argument by name at the end of the
call, leaving the existing positional arguments untouched:

```csharp
            uriOpener: new AvaloniaUriOpener(window),
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~NativeMenuTests"`
Expected: PASS — including `The_native_menu_matches_the_classic_menu_item_for_item`,
`Every_native_item_can_actually_be_activated` and `Only_the_edit_menu_carries_gestures`.

Run: `dotnet test LizTerm.slnx` → PASS.
Run: `dotnet build LizTerm.slnx --no-incremental 2>&1 | grep -c " warning "` → `0`.

- [ ] **Step 5: Commit**

```bash
git add src/LizTerm.App/Views/SessionWindow.axaml src/LizTerm.App/App.axaml.cs tests/LizTerm.App.Tests/Views/NativeMenuTests.cs
git commit -m "Put the project's own pages in the Help menu"
```

> **Reduction point.** Spec §12: everything above ships on its own. If the remaining tasks prove larger than
> expected, stop here, record it on issue #48, and the release keeps its theme — the guide stays reachable on
> GitHub, and only the offline case is lost.

---

## Task 4: The converter — block structure

**Files:**
- Create: `tests/LizTerm.Core.Tests/Documentation/UserGuideHtml.cs`
- Test: `tests/LizTerm.Core.Tests/Documentation/UserGuideHtmlTests.cs`

**Interfaces:**
- Produces: `LizTerm.Core.Tests.Documentation.UserGuideHtml.Convert(string markdown, string version)` returning
  `string` (the body), and `UserGuideHtml.Slug(string headingText)` returning `string`. Tasks 5–8 extend and
  consume both.

- [ ] **Step 1: Write the failing test**

Create `tests/LizTerm.Core.Tests/Documentation/UserGuideHtmlTests.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

namespace LizTerm.Core.Tests.Documentation;

public class UserGuideHtmlTests
{
    private static string Body(string markdown) => UserGuideHtml.Convert(markdown, "9.9.9");

    [Fact]
    public void Headings_carry_the_id_their_anchors_use()
    {
        Assert.Contains("<h2 id=\"tls-and-certificates\">TLS and certificates</h2>", Body("## TLS and certificates"));
        Assert.Contains("<h3 id=\"profile-settings\">Profile settings</h3>", Body("### Profile settings"));
    }

    [Theory]
    [InlineData("File transfer (IND$FILE)", "file-transfer-indfile")]
    [InlineData("Mouse, selection and clipboard", "mouse-selection-and-clipboard")]
    [InlineData("Where LizTerm keeps its files", "where-lizterm-keeps-its-files")]
    public void Slugs_match_the_ids_GitHub_would_have_produced(string heading, string expected) =>
        Assert.Equal(expected, UserGuideHtml.Slug(heading));

    [Fact]
    public void Consecutive_lines_become_one_paragraph()
    {
        var html = Body("one line\nand its continuation\n\na second paragraph");

        Assert.Contains("<p>one line\nand its continuation</p>", html);
        Assert.Contains("<p>a second paragraph</p>", html);
    }

    [Fact]
    public void Consecutive_dashes_become_one_list()
    {
        var html = Body("- first\n- second\n");

        Assert.Contains("<ul>\n<li>first</li>\n<li>second</li>\n</ul>", html);
    }

    [Fact]
    public void A_fenced_block_keeps_its_lines_and_drops_its_language()
    {
        var html = Body("```text\nREADY\nLOGON\n```");

        Assert.Contains("<pre><code>READY\nLOGON\n</code></pre>", html);
        Assert.DoesNotContain("text", html);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~UserGuideHtmlTests"`
Expected: FAIL — `UserGuideHtml` does not exist.

- [ ] **Step 3: Write the implementation**

Create `tests/LizTerm.Core.Tests/Documentation/UserGuideHtml.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Text;

namespace LizTerm.Core.Tests.Documentation;

/// <summary>Converts docs/user-guide.md to the single self-contained page the app embeds. Test-only code: no
/// src/ project references it, so the shipped binary carries the HTML and none of this.
///
/// It is not a Markdown implementation. It handles exactly the constructs the guide uses, and
/// UserGuideAssetTests fails if the guide grows one it does not — which is what makes a converter this small
/// safe to own, because the failure mode of a partial converter is silence.</summary>
public static partial class UserGuideHtml
{
    /// <summary>The document body. <see cref="Page"/>, added in a later task, wraps it.</summary>
    public static string Convert(string markdown, string version)
    {
        // Line endings are normalized on the way in and emitted as \n throughout. The committed HTML is
        // compared against this output on runners whose checkout may have rewritten either file, and this
        // repository has no .gitattributes.
        var lines = markdown.ReplaceLineEndings("\n").Split('\n');
        var body = new StringBuilder();

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.Length == 0) continue;

            if (line.StartsWith("```", StringComparison.Ordinal))
            {
                var code = new StringBuilder();
                for (i++; i < lines.Length && !lines[i].StartsWith("```", StringComparison.Ordinal); i++)
                {
                    code.Append(Escape(lines[i])).Append('\n');
                }
                body.Append("<pre><code>").Append(code).Append("</code></pre>\n");
                continue;
            }

            if (line.StartsWith('#'))
            {
                var level = line.Length - line.TrimStart('#').Length;
                var text = line[level..].Trim();
                body.Append($"<h{level} id=\"{Slug(text)}\">").Append(Inline(text, version)).Append($"</h{level}>\n");
                continue;
            }

            if (line.StartsWith("- ", StringComparison.Ordinal))
            {
                body.Append("<ul>\n");
                for (; i < lines.Length && lines[i].StartsWith("- ", StringComparison.Ordinal); i++)
                {
                    body.Append("<li>").Append(Inline(lines[i][2..], version)).Append("</li>\n");
                }
                i--;
                body.Append("</ul>\n");
                continue;
            }

            var paragraph = new List<string>();
            for (; i < lines.Length && lines[i].Length > 0; i++) paragraph.Add(lines[i]);
            i--;
            body.Append("<p>").Append(Inline(string.Join('\n', paragraph), version)).Append("</p>\n");
        }

        return body.ToString();
    }

    /// <summary>The id GitHub would have given the heading, so that the guide's own table of contents resolves
    /// inside this page: lowercased, punctuation dropped, spaces hyphenated.</summary>
    public static string Slug(string heading)
    {
        var slug = new StringBuilder();
        foreach (var c in heading.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c)) slug.Append(c);
            else if (c is ' ' or '-') slug.Append('-');
        }
        return slug.ToString();
    }

    private static string Escape(string text) =>
        text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    // Task 5 replaces this with real inline handling.
    private static string Inline(string text, string version) => Escape(text);
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~UserGuideHtmlTests"`
Expected: PASS, 7 tests (the `[Theory]` contributes three).

- [ ] **Step 5: Commit**

```bash
git add tests/LizTerm.Core.Tests/Documentation/
git commit -m "Turn the guide's blocks into HTML"
```

---

## Task 5: The converter — inline spans, and the escaping that matters

**Files:**
- Modify: `tests/LizTerm.Core.Tests/Documentation/UserGuideHtml.cs`
- Test: `tests/LizTerm.Core.Tests/Documentation/UserGuideHtmlTests.cs`

**Interfaces:**
- Consumes: `Convert`, `Slug`, `Escape` from Task 4.
- Produces: inline handling used by every later task. `Inline(string, string)` stays private.

- [ ] **Step 1: Write the failing test**

Add to `UserGuideHtmlTests`:

```csharp
    [Fact]
    public void Bold_and_italic_and_code_become_their_elements()
    {
        Assert.Contains("<strong>Connect</strong>", Body("Choose **Connect** now."));
        Assert.Contains("<em>pin</em>", Body("to *pin* the certificate"));
        Assert.Contains("<code>settings.json</code>", Body("`settings.json` holds your preferences"));
    }

    /// <summary>The guide's line 252 carries `wire-&lt;profile&gt;-&lt;date&gt;-&lt;time&gt;.log`. A converter
    /// that emits a code span verbatim hands the browser an unknown element, which renders as nothing, and the
    /// user is shown "wire-.log" — wrong, plausible, and shipped offline where nobody can correct it.</summary>
    [Fact]
    public void A_code_span_escapes_its_contents()
    {
        var html = Body("`wire-<profile>-<date>.log` names the file");

        Assert.Contains("<code>wire-&lt;profile&gt;-&lt;date&gt;.log</code>", html);
        Assert.DoesNotContain("<profile>", html);
    }

    [Fact]
    public void An_ampersand_outside_a_code_span_is_escaped_too() =>
        Assert.Contains("Edit &amp; View", Body("Edit & View"));

    [Fact]
    public void Emphasis_inside_a_code_span_is_left_alone() =>
        Assert.Contains("<code>a*b*c</code>", Body("`a*b*c`"));

    [Fact]
    public void Links_keep_their_target()
    {
        Assert.Contains("<a href=\"#keyboard\">Keyboard</a>", Body("[Keyboard](#keyboard)"));
        Assert.Contains("<a href=\"https://example.invalid/x\">there</a>", Body("[there](https://example.invalid/x)"));
    }

    [Fact]
    public void A_relative_readme_link_becomes_an_absolute_one_at_the_release_tag()
    {
        var html = Body("see the [README](../README.md#first-run)");

        Assert.Contains("href=\"https://github.com/coffeemuse/LizTerm/blob/v9.9.9/README.md#first-run\"", html);
        Assert.DoesNotContain("../README.md", html);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~UserGuideHtmlTests"`
Expected: FAIL — `Inline` escapes everything and emits no elements.

- [ ] **Step 3: Write the implementation**

Add `using System.Text.RegularExpressions;` to `UserGuideHtml.cs` and replace the placeholder `Inline` with:

```csharp
    private const string Repo = "https://github.com/coffeemuse/LizTerm";

    /// <summary>One alternation over every inline construct, matched left to right. A single pass rather than
    /// four sequential replacements: sequential passes would have to hide code spans behind a placeholder to
    /// stop emphasis being applied inside them, and a placeholder is a token that can be collided with. Here
    /// a code span simply wins its own match, and nothing else looks inside it.</summary>
    [GeneratedRegex(@"`([^`]*)`|\[([^\]]+)\]\(([^)]+)\)|\*\*([^*]+)\*\*|(?<!\*)\*([^*]+)\*(?!\*)")]
    private static partial Regex Spans();

    private static string Inline(string text, string version)
    {
        var html = new StringBuilder();
        var at = 0;

        foreach (Match m in Spans().Matches(text))
        {
            // Text between constructs is escaped; each construct escapes its own content below.
            html.Append(Escape(text[at..m.Index]));

            if (m.Groups[1].Success) html.Append("<code>").Append(Escape(m.Groups[1].Value)).Append("</code>");
            else if (m.Groups[2].Success)
            {
                html.Append($"<a href=\"{Href(m.Groups[3].Value, version)}\">")
                    .Append(Escape(m.Groups[2].Value)).Append("</a>");
            }
            else if (m.Groups[4].Success) html.Append("<strong>").Append(Escape(m.Groups[4].Value)).Append("</strong>");
            else html.Append("<em>").Append(Escape(m.Groups[5].Value)).Append("</em>");

            at = m.Index + m.Length;
        }

        return html.Append(Escape(text[at..])).ToString();
    }

    /// <summary>Spec §5.1: a link that is part of the document's own content points at the tag, so it describes
    /// the release the reader is running. The banner's link — the one asking "has this changed?" — is the
    /// deliberate exception and points at main; it is written in Page, not here.</summary>
    private static string Href(string target, string version) => target switch
    {
        "../README.md" => $"{Repo}/blob/v{version}/README.md",
        "../README.md#first-run" => $"{Repo}/blob/v{version}/README.md#first-run",
        _ => target,
    };
```

`Escape` and `Inline` must stay ordered so that a construct's content is escaped exactly once — the text
between constructs in the loop, and each construct's own groups. Nothing is escaped twice.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~UserGuideHtmlTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add tests/LizTerm.Core.Tests/Documentation/
git commit -m "Render the guide's inline spans, escaping what goes inside code"
```

---

## Task 6: The converter — tables

**Files:**
- Modify: `tests/LizTerm.Core.Tests/Documentation/UserGuideHtml.cs`
- Test: `tests/LizTerm.Core.Tests/Documentation/UserGuideHtmlTests.cs`

**Interfaces:**
- Consumes: `Inline` from Task 5.
- Produces: table handling. The guide carries 42 table rows across Profile settings, Keyboard, Preferences and
  the file locations, so this is the single largest construct in the document.

- [ ] **Step 1: Write the failing test**

Add to `UserGuideHtmlTests`:

```csharp
    [Fact]
    public void A_table_becomes_a_head_and_a_body()
    {
        var html = Body("| Setting | Meaning |\n|---|---|\n| Name | How it appears. |\n| Host | The server. |\n");

        Assert.Contains("<table>\n<thead>\n<tr><th>Setting</th><th>Meaning</th></tr>\n</thead>", html);
        Assert.Contains("<tbody>\n<tr><td>Name</td><td>How it appears.</td></tr>", html);
        Assert.Contains("<tr><td>Host</td><td>The server.</td></tr>\n</tbody>\n</table>", html);
        Assert.DoesNotContain("---", html);
    }

    [Fact]
    public void A_table_cell_gets_the_same_inline_treatment_as_prose()
    {
        var html = Body("| Key | Action |\n|---|---|\n| `Escape` | Sends **Attn** |\n");

        Assert.Contains("<td><code>Escape</code></td>", html);
        Assert.Contains("<td>Sends <strong>Attn</strong></td>", html);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~UserGuideHtmlTests"`
Expected: FAIL — the rows fall through to the paragraph branch and render as literal pipes.

- [ ] **Step 3: Write the implementation**

In `Convert`, add this branch immediately before the `- ` list branch:

```csharp
            if (line.StartsWith('|'))
            {
                var rows = new List<string[]>();
                for (; i < lines.Length && lines[i].StartsWith('|'); i++) rows.Add(Cells(lines[i]));
                i--;

                body.Append("<table>\n<thead>\n<tr>");
                foreach (var cell in rows[0]) body.Append("<th>").Append(Inline(cell, version)).Append("</th>");
                body.Append("</tr>\n</thead>\n<tbody>\n");
                // rows[1] is the |---|---| separator, which carries no content.
                foreach (var row in rows.Skip(2))
                {
                    body.Append("<tr>");
                    foreach (var cell in row) body.Append("<td>").Append(Inline(cell, version)).Append("</td>");
                    body.Append("</tr>\n");
                }
                body.Append("</tbody>\n</table>\n");
                continue;
            }
```

And the helper, beside `Slug`:

```csharp
    /// <summary>The cells of one row. The guide escapes no pipes — UserGuideAssetTests holds it to that — so a
    /// plain split is correct here, and a cell that grew a literal pipe would be caught there rather than
    /// silently split into two.</summary>
    private static string[] Cells(string row) =>
        row.Trim().Trim('|').Split('|').Select(c => c.Trim()).ToArray();
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~UserGuideHtmlTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add tests/LizTerm.Core.Tests/Documentation/
git commit -m "Render the guide's tables, which are a third of it"
```

---

## Task 7: The page — banner, style, document

**Files:**
- Modify: `tests/LizTerm.Core.Tests/Documentation/UserGuideHtml.cs`
- Test: `tests/LizTerm.Core.Tests/Documentation/UserGuideHtmlTests.cs`

**Interfaces:**
- Consumes: everything from Tasks 4–6.
- Produces: `UserGuideHtml.Page(string markdown, string version)` returning the complete document. Task 8's
  golden file is `Page`'s output; `Convert` stays the body-only entry point the unit tests use.

- [ ] **Step 1: Write the failing test**

Add to `UserGuideHtmlTests`:

```csharp
    private static string Page(string markdown) => UserGuideHtml.Page(markdown, "9.9.9");

    [Fact]
    public void The_page_is_one_self_contained_document()
    {
        var html = Page("# LizTerm user guide\n");

        Assert.StartsWith("<!doctype html>", html);
        Assert.Contains("<style>", html);
        Assert.Contains("prefers-color-scheme: dark", html);
        Assert.DoesNotContain("<script", html);
        Assert.DoesNotContain("<link", html);
        Assert.EndsWith("</html>\n", html);
    }

    /// <summary>Spec §6: the banner exists only here. docs/user-guide.md is the live copy, and a note telling
    /// its reader to go and find the live copy would be false there.</summary>
    [Fact]
    public void The_banner_names_the_version_and_links_to_main()
    {
        var html = Page("# LizTerm user guide\n");

        Assert.Contains("Offline copy, shipped with LizTerm 9.9.9.", html);
        Assert.Contains("https://github.com/coffeemuse/LizTerm/blob/main/docs/user-guide.md", html);
    }

    [Fact]
    public void The_banner_sits_above_the_title()
    {
        var html = Page("# LizTerm user guide\n");

        Assert.True(html.IndexOf("Offline copy", StringComparison.Ordinal)
                    < html.IndexOf("<h1", StringComparison.Ordinal));
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~UserGuideHtmlTests"`
Expected: FAIL — `UserGuideHtml.Page` does not exist.

- [ ] **Step 3: Write the implementation**

Add to `UserGuideHtml.cs`. Note that the raw string literal doubles every CSS brace; check the rendered output
has single ones.

```csharp
    private const string Guide = Repo + "/blob/main/docs/user-guide.md";

    /// <summary>The whole document: one file, an inline stylesheet, no scripts, no web fonts, no external
    /// references of any kind. It is read offline, and anything it would have to fetch is a blank space.</summary>
    public static string Page(string markdown, string version) =>
        $$"""
          <!doctype html>
          <html lang="en">
          <head>
          <meta charset="utf-8">
          <meta name="viewport" content="width=device-width, initial-scale=1">
          <title>LizTerm user guide</title>
          <style>
          :root { color-scheme: light dark; }
          body { margin: 0 auto; max-width: 46rem; padding: 2rem 1.25rem 4rem;
                 font: 16px/1.6 -apple-system, "Segoe UI", system-ui, sans-serif;
                 color: #1c1c1c; background: #fff; }
          h1, h2, h3 { line-height: 1.25; margin: 2rem 0 0.75rem; }
          h2 { border-bottom: 1px solid #d8d8d8; padding-bottom: 0.3rem; }
          code { font-family: ui-monospace, SFMono-Regular, Consolas, monospace; font-size: 0.9em;
                 background: #f2f2f2; padding: 0.1em 0.3em; border-radius: 3px; }
          pre { background: #f2f2f2; padding: 0.9rem; overflow-x: auto; border-radius: 4px; }
          pre code { background: none; padding: 0; }
          table { border-collapse: collapse; width: 100%; margin: 1rem 0; display: block; overflow-x: auto; }
          th, td { border: 1px solid #d8d8d8; padding: 0.45rem 0.6rem; text-align: left; vertical-align: top; }
          th { background: #f6f6f6; }
          a { color: #0b5cab; }
          .offline { font-size: 0.9em; color: #4a4a4a; background: #f6f6f6;
                     border: 1px solid #e0e0e0; border-radius: 4px; padding: 0.6rem 0.8rem; }
          @media (prefers-color-scheme: dark) {
            body { color: #e4e4e4; background: #1b1b1b; }
            h2 { border-bottom-color: #3a3a3a; }
            code, pre { background: #262626; }
            th, td { border-color: #3a3a3a; }
            th { background: #242424; }
            a { color: #6fb3ff; }
            .offline { color: #b6b6b6; background: #242424; border-color: #3a3a3a; }
          }
          </style>
          </head>
          <body>
          <p class="offline">Offline copy, shipped with LizTerm {{version}}. The manual may have been updated
          since this release — <a href="{{Guide}}">the current version is on GitHub</a>.</p>
          {{Convert(markdown, version)}}</body>
          </html>

          """;
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~UserGuideHtmlTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add tests/LizTerm.Core.Tests/Documentation/
git commit -m "Wrap the guide in a page that says which copy it is"
```

---

## Task 8: The guards, and the committed file

**Files:**
- Create: `tests/LizTerm.Core.Tests/Documentation/UserGuideAssetTests.cs`
- Create: `src/LizTerm.App/Assets/Docs/user-guide.html` (generated in Step 3)

**Interfaces:**
- Consumes: `UserGuideHtml.Page`, `UserGuideHtml.Slug`; `LicenseHeader.Root()` from
  `tests/LizTerm.Core.Tests/Repository/LicenseHeader.cs`.
- Produces: the committed HTML that Task 9 embeds.

- [ ] **Step 1: Write the failing test**

Create `tests/LizTerm.Core.Tests/Documentation/UserGuideAssetTests.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Text.RegularExpressions;
using LizTerm.Core.Tests.Repository;

namespace LizTerm.Core.Tests.Documentation;

/// <summary>Holds the committed HTML to its Markdown source, and the Markdown source to what the converter can
/// actually render. Lives in Core.Tests for the reason RepositoryHeadersTests does: this project builds on
/// every run, so a guide edited without regenerating fails on the developer's machine rather than in CI.</summary>
public class UserGuideAssetTests
{
    private static string Root => LicenseHeader.Root();
    private static string MarkdownPath => Path.Combine(Root, "docs", "user-guide.md");
    private static string HtmlPath =>
        Path.Combine(Root, "src", "LizTerm.App", "Assets", "Docs", "user-guide.html");

    private static string Markdown() => File.ReadAllText(MarkdownPath).ReplaceLineEndings("\n");

    /// <summary>The version the release pipeline will agree with: release.yml's version job reads this same
    /// element and fails the run unless the tag and LizTerm.parcel match it.</summary>
    private static string Version()
    {
        var match = Regex.Match(File.ReadAllText(Path.Combine(Root, "Directory.Build.props")),
            @"<Version>([^<]+)</Version>");
        Assert.True(match.Success, "no <Version> in Directory.Build.props");
        return match.Groups[1].Value;
    }

    [Fact]
    public void The_committed_html_is_what_the_converter_produces()
    {
        var expected = UserGuideHtml.Page(Markdown(), Version());

        if (Environment.GetEnvironmentVariable("LIZTERM_UPDATE_DOCS") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(HtmlPath)!);
            File.WriteAllText(HtmlPath, expected);
            return;
        }

        Assert.True(File.Exists(HtmlPath),
            $"{HtmlPath} is missing. Regenerate with LIZTERM_UPDATE_DOCS=1 (see docs/development.md).");
        // Normalized on both sides: this repository has no .gitattributes and the suite runs on windows-latest.
        Assert.Equal(expected, File.ReadAllText(HtmlPath).ReplaceLineEndings("\n"));
    }

    /// <summary>The converter handles exactly what the guide uses today. Its failure mode is silence — an
    /// unsupported construct is dropped or emitted as literal text — so the source is held to the supported set
    /// rather than the output inspected afterwards for damage.</summary>
    [Fact]
    public void The_guide_uses_no_construct_the_converter_cannot_render()
    {
        var unsupported = new (string What, Regex Pattern)[]
        {
            ("an ordered list", new Regex(@"^\d+\. ", RegexOptions.Multiline)),
            ("a blockquote", new Regex("^> ", RegexOptions.Multiline)),
            // [ \t] and not \s: \s matches \n, so under Multiline `^\s+` consumes a blank line's
            // newline and matches the TOP-LEVEL bullet after it. Against this guide the \s form
            // matched 9 times with no nested list present anywhere.
            ("a nested list", new Regex(@"^[ \t]+[-*] ", RegexOptions.Multiline)),
            ("a horizontal rule", new Regex("^---+$", RegexOptions.Multiline)),
            ("an asterisk bullet", new Regex(@"^\* ", RegexOptions.Multiline)),
            ("an image", new Regex(@"!\[")),
            ("raw HTML", new Regex("^<", RegexOptions.Multiline)),
            ("an escaped pipe in a table", new Regex(@"\\\|")),
            ("a reference link", new Regex(@"^\[[^\]]+\]:", RegexOptions.Multiline)),
            // The inline alternation matches left to right, so bold wrapping a link would win the match and
            // the link inside it would render as literal brackets.
            // [^*\n] and [^\]\n], not [^*] and [^\]]: without excluding \n these spans cross paragraph,
            // list and table boundaries and pair a ** with an unrelated link far below it. The trailing \*\*
            // is what distinguishes an opening delimiter from a closing one — without it,
            // "**bold** text [link](url)" false-matches on the CLOSING **.
            ("a link inside bold", new Regex(@"\*\*[^*\n]*\[[^\]\n]*\]\([^)\n]*\)[^*\n]*\*\*")),
        };

        var markdown = Markdown();
        foreach (var (what, pattern) in unsupported)
        {
            Assert.False(pattern.IsMatch(markdown),
                $"docs/user-guide.md now uses {what}, which UserGuideHtml does not render. Extend the converter and its tests, then regenerate.");
        }
    }

    [Fact]
    public void Every_internal_link_resolves_to_a_heading_the_page_emits()
    {
        var markdown = Markdown();
        var ids = Regex.Matches(markdown, @"^#{1,6} (.+)$", RegexOptions.Multiline)
            .Select(m => UserGuideHtml.Slug(m.Groups[1].Value.Trim()))
            .ToHashSet();

        foreach (Match link in Regex.Matches(markdown, @"\]\(#([^)]+)\)"))
        {
            Assert.Contains(link.Groups[1].Value, ids);
        }
    }

    /// <summary>Every link in the guide must have become an anchor. A link the converter mishandled survives
    /// into the page as a literal "](", which is unambiguous in a way that pattern-matching the Markdown source
    /// for hazards is not — the source-side guards above are necessarily approximations, and this is not.</summary>
    /// <summary>Every bullet in the source must become a list item. A count rather than a pattern, because the
    /// failure it guards against is not a construct the converter refuses — it is a wrapped bullet's
    /// continuation falling into the paragraph branch, which consumes to the next blank line and so swallows
    /// the bullets after it too. Sixteen of forty-five bullets rendered as prose before this was found, and no
    /// construct-shaped guard noticed.</summary>
    [Fact]
    public void Every_bullet_in_the_source_becomes_a_list_item()
    {
        var bullets = Markdown().Split('\n').Count(l => l.StartsWith("- ", StringComparison.Ordinal));
        var items = Regex.Matches(File.ReadAllText(HtmlPath).ReplaceLineEndings("\n"), "<li>").Count;

        Assert.Equal(bullets, items);
    }

    [Fact]
    public void No_link_survives_unrendered_in_the_page() =>
        Assert.DoesNotContain("](", File.ReadAllText(HtmlPath).ReplaceLineEndings("\n"));

    [Fact]
    public void The_banner_names_the_version_the_release_will_carry() =>
        Assert.Contains($"Offline copy, shipped with LizTerm {Version()}.",
            File.ReadAllText(HtmlPath).ReplaceLineEndings("\n"));
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~UserGuideAssetTests"`
Expected: FAIL — `src/LizTerm.App/Assets/Docs/user-guide.html` does not exist yet.

If `The_guide_uses_no_construct_the_converter_cannot_render` fails, that is a real finding: the guide has grown
a construct since this plan was written. Extend `UserGuideHtml` and `UserGuideHtmlTests` to handle it before
going on — do not weaken the guard.

- [ ] **Step 3: Generate the file, and read it**

```bash
LIZTERM_UPDATE_DOCS=1 dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~UserGuideAssetTests.The_committed_html_is_what_the_converter_produces"
```

Then open `src/LizTerm.App/Assets/Docs/user-guide.html` in a browser. This is the one step the tests cannot do
for you. Check: the banner reads correctly and its link works; the table of contents jumps to each section; the
tables have borders and are readable; the fenced block is monospaced; `wire-<profile>-<date>-<time>.log` appears
intact under "Where LizTerm keeps its files"; and the page is legible with the system in dark mode.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~UserGuideAssetTests"`
Expected: PASS, 4 tests.

Run: `dotnet test LizTerm.slnx` → PASS. `RepositoryHeadersTests` must still pass: the generated `.html` is not
a scanned file type, so it needs no licence header.

- [ ] **Step 5: Commit**

```bash
git add tests/LizTerm.Core.Tests/Documentation/UserGuideAssetTests.cs src/LizTerm.App/Assets/Docs/user-guide.html
git commit -m "Commit the generated guide, and hold it to its source"
```

---

## Task 9: Ship it, and open it

**Files:**
- Create: `src/LizTerm.App/Documentation/UserGuide.cs`
- Create: `tests/LizTerm.App.Tests/Documentation/UserGuideTests.cs`
- Modify: `src/LizTerm.App/ProjectLinks.cs`, `src/LizTerm.App/ViewModels/SessionViewModel.cs`,
  `src/LizTerm.App/Views/SessionWindow.axaml`, `tests/LizTerm.App.Tests/Views/NativeMenuTests.cs`

**Interfaces:**
- Consumes: the committed HTML from Task 8; `IUriOpener` from Task 1; `AppVersion.Current`.
- Produces: `LizTerm.App.Documentation.UserGuide.Extract(string version, string directory)` returning the
  written path; `SessionViewModel.ShowUserGuideCommand`; `ProjectLinks.UserGuide`.

**Note:** no csproj change is needed. `<AvaloniaResource Include="Assets\**" />` already covers
`Assets/Docs/user-guide.html`.

- [ ] **Step 1: Write the failing test**

Create `tests/LizTerm.App.Tests/Documentation/UserGuideTests.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Headless.XUnit;
using LizTerm.App.Documentation;

namespace LizTerm.App.Tests.Documentation;

public class UserGuideTests
{
    [AvaloniaFact]
    public void The_guide_is_written_where_it_says_it_is()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"lizterm-guide-test-{Guid.NewGuid():N}");
        try
        {
            var path = UserGuide.Extract("9.9.9", directory);

            Assert.Equal(Path.Combine(directory, "lizterm-user-guide-9.9.9.html"), path);
            var written = File.ReadAllText(path);
            Assert.StartsWith("<!doctype html>", written);
            Assert.Contains("Offline copy, shipped with LizTerm", written);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Reopening Help overwrites one file rather than accumulating them, and a leftover that was
    /// truncated or edited is replaced rather than served.</summary>
    [AvaloniaFact]
    public void A_second_extraction_replaces_whatever_was_there()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"lizterm-guide-test-{Guid.NewGuid():N}");
        try
        {
            var path = UserGuide.Extract("9.9.9", directory);
            File.WriteAllText(path, "stale");

            Assert.Equal(path, UserGuide.Extract("9.9.9", directory));
            Assert.StartsWith("<!doctype html>", File.ReadAllText(path));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}
```

Add to `NativeMenuTests`:

```csharp
    [AvaloniaFact]
    public void Help_offers_the_user_guide_above_the_links_on_both_menus()
    {
        var (window, _, _, _) = Show();
        var classic = window.FindControl<Menu>("ClassicMenu")!.Items.OfType<MenuItem>()
            .Single(m => (string)m.Header! == "_Help");

        Assert.Equal("_User Guide", (string)classic.Items.OfType<MenuItem>().First().Header!);
        Assert.NotNull(Item(window, "_Help", "_User Guide").Command);
    }

    [AvaloniaFact]
    public async Task Choosing_the_user_guide_opens_the_extracted_file()
    {
        var opener = new FakeUriOpener();
        var vm = new SessionViewModel(new FakeEmulatorSession(), action => action(), new FakeTextClipboard(),
            uriOpener: opener);
        var window = new SessionWindow(MenuStyle.Native, isMacOS: true) { DataContext = vm };
        window.Show();

        ((INativeMenuItemExporterEventsImplBridge)Item(window, "_Help", "_User Guide")).RaiseClicked();
        await Wait.UntilAsync(() => opener.Opened.Count == 1, "the guide to be opened");

        Assert.EndsWith($"lizterm-user-guide-{AppVersion.Current}.html", opener.Opened[0]);
        Assert.True(File.Exists(opener.Opened[0]));
    }

    [AvaloniaFact]
    public async Task A_platform_that_cannot_open_the_guide_names_the_page_on_GitHub()
    {
        var opener = new FakeUriOpener { Result = false };
        var vm = new SessionViewModel(new FakeEmulatorSession(), action => action(), new FakeTextClipboard(),
            uriOpener: opener);
        var window = new SessionWindow(MenuStyle.Native, isMacOS: true) { DataContext = vm };
        window.Show();

        ((INativeMenuItemExporterEventsImplBridge)Item(window, "_Help", "_User Guide")).RaiseClicked();
        await Wait.UntilAsync(() => vm.ErrorMessage is not null, "the banner");

        Assert.Contains(ProjectLinks.UserGuide, vm.ErrorMessage!);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~UserGuideTests"`
Expected: FAIL — `UserGuide`, `ProjectLinks.UserGuide` and the menu item do not exist.

- [ ] **Step 3: Write the implementation**

Add to `src/LizTerm.App/ProjectLinks.cs`:

```csharp
    /// <summary>Where the guide lives when the bundled copy cannot be opened. `main`, not the tag: a reader who
    /// could not open the offline copy wants the manual, and the current one is the best answer available.</summary>
    public const string UserGuide = Repo + "/blob/main/docs/user-guide.md";
```

Create `src/LizTerm.App/Documentation/UserGuide.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Platform;

namespace LizTerm.App.Documentation;

/// <summary>The bundled user guide. It is an embedded application resource for the reason LICENSE and
/// THIRD-PARTY-NOTICES.txt are: nothing this pipeline packages carries loose files beside the app, and a
/// resource travels into all five package formats with no packaging work at all.
///
/// A browser cannot read an avares:// URI, so opening it means writing it out first. The HTML is generated
/// from docs/user-guide.md by UserGuideHtml in LizTerm.Core.Tests and committed; never hand-edited.</summary>
public static class UserGuide
{
    private static readonly Uri ResourceUri = new("avares://LizTerm.App/Assets/Docs/user-guide.html");

    /// <summary>Writes the guide into <paramref name="directory"/> and returns its path. The name carries the
    /// version so that an upgraded LizTerm cannot serve the previous release's page out of a temp directory the
    /// system has not yet cleaned, and is otherwise stable so that reopening Help overwrites one file rather
    /// than accumulating them.</summary>
    public static string Extract(string version, string directory)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"lizterm-user-guide-{version}.html");

        using var resource = AssetLoader.Open(ResourceUri);
        using var file = File.Create(path);
        resource.CopyTo(file);

        return path;
    }
}
```

Add `using LizTerm.App.Documentation;` to `SessionViewModel.cs`, and the command beside `OpenLinkAsync`:

```csharp
    /// <summary>Help &gt; User Guide. Writes the bundled copy out and hands it to the platform. When that
    /// fails, the banner names the page on GitHub rather than the temp path: the URL is what a person can act
    /// on, where the path is an artifact of how we shipped it.</summary>
    [RelayCommand]
    private async Task ShowUserGuideAsync()
    {
        try
        {
            var path = UserGuide.Extract(AppVersion.Current, Path.GetTempPath());
            if (_uriOpener is not null && await _uriOpener.OpenFileAsync(path)) return;
        }
        catch (Exception)
        {
            // Fall through to naming the page.
        }
        ErrorMessage = $"Could not open the user guide. It is also at {ProjectLinks.UserGuide}.";
    }
```

In `SessionWindow.axaml`, add the item at the **top** of each Help menu, above `Project on _GitHub`:

```xml
            <NativeMenuItem Header="_User Guide" Command="{Binding ShowUserGuideCommand}" />
```

```xml
        <MenuItem Header="_User Guide" Command="{Binding ShowUserGuideCommand}" />
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test LizTerm.slnx`
Expected: PASS, including the parity walk and `Every_native_item_can_actually_be_activated`.

Run: `dotnet build LizTerm.slnx --no-incremental 2>&1 | grep -c " warning "` → `0`.

Then run the app and choose Help > User Guide, which is the only step that exercises the real launcher:

```bash
LIZTERM_B3270_PATH=$(which b3270) dotnet run --project src/LizTerm.App
```

- [ ] **Step 5: Commit**

```bash
git add src/LizTerm.App/Documentation/ src/LizTerm.App/ProjectLinks.cs src/LizTerm.App/ViewModels/SessionViewModel.cs src/LizTerm.App/Views/SessionWindow.axaml tests/LizTerm.App.Tests/Documentation/ tests/LizTerm.App.Tests/Views/NativeMenuTests.cs
git commit -m "Open the bundled guide from the Help menu"
```

---

## Task 10: The documentation this changed

**Files:**
- Modify: `docs/user-guide.md` (its Menus section)
- Modify: `docs/development.md`
- Modify: `src/LizTerm.App/CLAUDE.md`, `tests/CLAUDE.md`
- Modify: `docs/superpowers/specs/2026-09-12-lizterm-v050-release-scope.md`

**Note:** editing `docs/user-guide.md` makes `The_committed_html_is_what_the_converter_produces` fail until the
HTML is regenerated. That is the mechanism working. Regenerate in Step 2.

- [ ] **Step 1: Update the prose**

In `docs/user-guide.md`, in the Menus section's Help entry, add the four items: User Guide (opens this guide in
your browser), Project on GitHub, Report an Issue, Releases.

In `docs/development.md`, add to the conventions:

> **The bundled user guide.** `src/LizTerm.App/Assets/Docs/user-guide.html` is generated from
> `docs/user-guide.md` and is never hand-edited. Editing the guide fails
> `UserGuideAssetTests.The_committed_html_is_what_the_converter_produces` until it is regenerated:
>
> ```bash
> LIZTERM_UPDATE_DOCS=1 dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~UserGuideAssetTests"
> ```
>
> The converter is `tests/LizTerm.Core.Tests/Documentation/UserGuideHtml.cs` and handles only the constructs the
> guide uses; `The_guide_uses_no_construct_the_converter_cannot_render` fails if the guide grows another. Extend
> the converter rather than the guard.

In `src/LizTerm.App/CLAUDE.md`, note `IUriOpener` beside the existing `IFolderOpener` note, and that the guide
is an embedded resource read by `Documentation/UserGuide.cs`.

In `tests/CLAUDE.md`, add `FakeUriOpener` to the App tests' list of fakes, and a Core tests note that
`UserGuideAssetTests` lives there for the reason `RepositoryHeadersTests` does.

In `docs/superpowers/specs/2026-09-12-lizterm-v050-release-scope.md`, tick §4.1 and §9 step 3 as done, in the
style §4.2 and §4.3 already use.

- [ ] **Step 2: Regenerate and run everything**

```bash
LIZTERM_UPDATE_DOCS=1 dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~UserGuideAssetTests"
dotnet test LizTerm.slnx
dotnet build LizTerm.slnx --no-incremental 2>&1 | grep -c " warning "
```

Expected: PASS, and `0`.

- [ ] **Step 3: Commit**

```bash
git add docs src/LizTerm.App/CLAUDE.md tests/CLAUDE.md src/LizTerm.App/Assets/Docs/user-guide.html
git commit -m "Record where the bundled guide comes from and how to regenerate it"
```

- [ ] **Step 4: Open the pull request**

The body names issue #48 and the spec, and asks the reviewer for the two things the tests cannot check: that
the rendered page reads correctly in both colour schemes, and that Help > User Guide opens it on their machine.
