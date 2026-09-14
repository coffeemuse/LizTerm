# LizTerm Release Check Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a release check that compares the running LizTerm version against the latest published GitHub
release, with an automatic startup check (on by default, silent unless there is something new), a manual
Help > Check for Updates... menu item that always reports something, and a Preferences toggle on a new General
tab.

**Architecture:** A BCL-only `IReleaseChecker`/`GitHubReleaseChecker` in `LizTerm.Core` (the app's first HTTP
call) feeds a pure `UpdateChecker.CheckAsync` in `LizTerm.App` that turns the network result into one of three
outcomes (`UpToDate` / `NewerAvailable` / `Failed`). A pure `UpdateNotificationPolicy` decides whether an
automatic check is allowed to interrupt the user. `App` owns one `UpdateCheckWindow` at a time, shown from
startup (silent unless newer-and-unskipped) or from the Help menu (always shown), following the existing
`_about`/`_preferences` single-instance pattern.

**Tech Stack:** .NET 10, Avalonia 12, CommunityToolkit.Mvvm, `System.Net.Http.HttpClient`, source-generated
`System.Text.Json`, xunit.v3 (VSTest mode), Avalonia.Headless.XUnit.

**Spec:** [`docs/superpowers/specs/2026-09-14-lizterm-release-check-design.md`](../specs/2026-09-14-lizterm-release-check-design.md)

## Global Constraints

- Every new hand-written `.cs` and `.axaml` file starts with the three-line licence header: `// This file is part
  of LizTerm.`, `// Copyright 2026 by CoffeeMuse`, `// SPDX-License-Identifier: BSD-3-Clause` (`.axaml` uses the
  XML comment form; see any existing file for the exact shape). `RepositoryHeadersTests` fails the suite for a
  missing one.
- `LizTerm.Core` depends on the BCL only — never Avalonia, never b3270. Everything under `src/LizTerm.Core/Updates/`
  must compile with no reference beyond what is already in that project.
- No new package reference is needed anywhere in this plan: `HttpClient` and `System.Text.Json` are BCL, and JSON
  goes through a source-generated `JsonSerializerContext`, the `SettingsJsonContext`/`ProfileJsonContext` shape.
- GitHub API contract (spec §3): `GET https://api.github.com/repos/coffeemuse/LizTerm/releases/latest`, headers
  `Accept: application/vnd.github+json`, `X-GitHub-Api-Version: 2022-11-28`, `User-Agent: LizTerm/<version>`; the
  production `HttpClient` has a 10 s timeout.
- Every task's tests must pass before moving to the next task, and the whole suite must still pass at the end:
  `dotnet test LizTerm.slnx`.
- Before the final task is considered done, the zero-warning gate must print `0`:
  `dotnet build LizTerm.slnx --no-incremental 2>&1 | grep -c " warning "`.
- Drive an Avalonia `Button` in a test by raising its click event — `button.RaiseEvent(new
  RoutedEventArgs(Button.ClickEvent))` — never by setting `IsChecked`/calling the handler directly; that only
  proves a binding renders. Anything touching a `Window` or `Control` needs `[AvaloniaFact]`, not `[Fact]`.
- New `.cs`/`.axaml` files are picked up by the existing SDK-style project globs; no `.csproj` edits are needed
  anywhere in this plan.

---

## Task 1: `AppSettings` gains the two new fields

**Files:**
- Modify: `src/LizTerm.Core/Settings/AppSettings.cs`
- Test: `tests/LizTerm.Core.Tests/Settings/SettingsStoreTests.cs`

**Interfaces:**
- Produces: `AppSettings.CheckForUpdatesAutomatically` (`bool`, default `true`), `AppSettings.SkippedUpdateVersion`
  (`string?`, default `null`) — both plain positional record parameters, the shape every other field in this
  record already has.

- [ ] **Step 1: Write the failing tests**

Append to `tests/LizTerm.Core.Tests/Settings/SettingsStoreTests.cs`, after `The_pf_keys_flag_round_trips_and_defaults_on`:

```csharp
    /// <summary>The automatic-check flag (#107): on by default, so an upgrade starts checking, and turning it off
    /// is what writes the key.</summary>
    [Fact]
    public void The_check_for_updates_flag_round_trips_and_defaults_on()
    {
        Assert.True(Store.Load().CheckForUpdatesAutomatically);

        Store.Update(s => s with { CheckForUpdatesAutomatically = false });

        Assert.False(Store.Load().CheckForUpdatesAutomatically);
        Assert.False(ReadFile()["checkForUpdatesAutomatically"]!.GetValue<bool>());
    }

    /// <summary>The per-version Skip (#107): null until the user skips a release, holding the exact version
    /// string rather than a bool so a later release is never suppressed by an old skip.</summary>
    [Fact]
    public void The_skipped_update_version_round_trips_and_defaults_to_null()
    {
        Assert.Null(Store.Load().SkippedUpdateVersion);

        Store.Update(s => s with { SkippedUpdateVersion = "0.6.0" });

        Assert.Equal("0.6.0", Store.Load().SkippedUpdateVersion);
        Assert.Equal("0.6.0", ReadFile()["skippedUpdateVersion"]!.GetValue<string>());
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~SettingsStoreTests"`
Expected: build failure (`CS1739`/`CS0117`-style errors — no such constructor parameter or record member).

- [ ] **Step 3: Add the two fields**

In `src/LizTerm.Core/Settings/AppSettings.cs`, change the record's parameter list from ending at `KeypadPfKeys`
to:

```csharp
public sealed record AppSettings(
    [property: JsonConverter(typeof(JsonStringEnumConverter<CrosshairMode>))] CrosshairMode Crosshair = CrosshairMode.None,
    bool Blink = true,
    bool VisualBell = true,
    [property: JsonConverter(typeof(JsonStringEnumConverter<BellSound>))] BellSound BellSound = BellSound.None,
    bool Keypad = false,
    [property: JsonConverter(typeof(JsonStringEnumConverter<KeypadDock>))] KeypadDock KeypadDock = KeypadDock.Bottom,
    [property: JsonConverter(typeof(JsonStringEnumConverter<MenuStyle>))] MenuStyle MenuStyle = MenuStyle.Auto,
    bool ShowTagsInStatusBar = false,
    bool KeypadPfKeys = true,
    bool CheckForUpdatesAutomatically = true,
    string? SkippedUpdateVersion = null);
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~SettingsStoreTests"`
Expected: PASS (all tests in the class, including the two new ones).

- [ ] **Step 5: Commit**

```bash
git add src/LizTerm.Core/Settings/AppSettings.cs tests/LizTerm.Core.Tests/Settings/SettingsStoreTests.cs
git commit -m "Add CheckForUpdatesAutomatically and SkippedUpdateVersion to AppSettings"
```

---

## Task 2: `SettingsViewModel` gains the matching bindable properties

**Files:**
- Modify: `src/LizTerm.App/ViewModels/SettingsViewModel.cs`
- Test: `tests/LizTerm.App.Tests/ViewModels/SettingsViewModelTests.cs`

**Interfaces:**
- Consumes: `AppSettings.CheckForUpdatesAutomatically`, `AppSettings.SkippedUpdateVersion` (Task 1).
- Produces: `SettingsViewModel.CheckForUpdatesAutomatically` (`bool`, get/set), `SettingsViewModel.SkippedUpdateVersion`
  (`string?`, get/set) — the three-line `Apply`-based pattern every other property here uses.

- [ ] **Step 1: Write the failing tests**

Append to `tests/LizTerm.App.Tests/ViewModels/SettingsViewModelTests.cs`, after the PF-keys test:

```csharp
    [Fact]
    public void The_check_for_updates_flag_writes_through_and_skips_an_unchanged_value()
    {
        var settings = new SettingsViewModel(new SettingsStore(FilePath));
        var changes = Changes(settings);

        settings.CheckForUpdatesAutomatically = true;
        Assert.Empty(changes);
        Assert.False(File.Exists(FilePath));

        settings.CheckForUpdatesAutomatically = false;

        Assert.Equal(["CheckForUpdatesAutomatically"], changes);
        Assert.False(new SettingsViewModel(new SettingsStore(FilePath)).CheckForUpdatesAutomatically);
    }

    /// <summary>Not bound to any Preferences control — App writes it from the Skip button's callback — but it
    /// follows the same write-through shape as every other setting.</summary>
    [Fact]
    public void The_skipped_update_version_writes_through_and_skips_an_unchanged_value()
    {
        var settings = new SettingsViewModel(new SettingsStore(FilePath));
        var changes = Changes(settings);

        settings.SkippedUpdateVersion = null;
        Assert.Empty(changes);
        Assert.False(File.Exists(FilePath));

        settings.SkippedUpdateVersion = "0.6.0";

        Assert.Equal(["SkippedUpdateVersion"], changes);
        Assert.Equal("0.6.0", new SettingsViewModel(new SettingsStore(FilePath)).SkippedUpdateVersion);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~SettingsViewModelTests"`
Expected: build failure — `SettingsViewModel` has no `CheckForUpdatesAutomatically`/`SkippedUpdateVersion` member.

- [ ] **Step 3: Add the two properties**

In `src/LizTerm.App/ViewModels/SettingsViewModel.cs`, after the `ShowTagsInStatusBar` property and before
`MenuStyle`:

```csharp
    /// <summary>Automatic release checking at startup (#107). On by default: the check is one unauthenticated
    /// GET to a public API, carries no personal data, and this preference turns it off.</summary>
    public bool CheckForUpdatesAutomatically
    {
        get => Current.CheckForUpdatesAutomatically;
        set
        {
            if (Current.CheckForUpdatesAutomatically != value) Apply(nameof(CheckForUpdatesAutomatically), s => s with { CheckForUpdatesAutomatically = value });
        }
    }

    /// <summary>The exact release version the user chose "Skip This Version" for, or null (#107). Not bound to
    /// any Preferences control: App writes it from the update dialog's Skip button. Storing the version string,
    /// not a bool, is what makes a later release un-suppressed for free.</summary>
    public string? SkippedUpdateVersion
    {
        get => Current.SkippedUpdateVersion;
        set
        {
            if (Current.SkippedUpdateVersion != value) Apply(nameof(SkippedUpdateVersion), s => s with { SkippedUpdateVersion = value });
        }
    }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~SettingsViewModelTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/LizTerm.App/ViewModels/SettingsViewModel.cs tests/LizTerm.App.Tests/ViewModels/SettingsViewModelTests.cs
git commit -m "Add SettingsViewModel properties for the release-check preferences"
```

---

## Task 3: `ReleaseVersion.IsNewer` — the pure comparison

**Files:**
- Create: `src/LizTerm.Core/Updates/ReleaseVersion.cs`
- Test: Create `tests/LizTerm.Core.Tests/Updates/ReleaseVersionTests.cs`

**Interfaces:**
- Produces: `LizTerm.Core.Updates.ReleaseVersion.IsNewer(string latest, string current)` : `bool`. Throws
  `FormatException` when either argument does not parse as a `System.Version`.

- [ ] **Step 1: Write the failing test file**

Create `tests/LizTerm.Core.Tests/Updates/ReleaseVersionTests.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.Core.Updates;

namespace LizTerm.Core.Tests.Updates;

public class ReleaseVersionTests
{
    [Theory]
    [InlineData("0.6.0", "0.5.2", true)]
    [InlineData("0.5.2", "0.5.2", false)]
    [InlineData("0.5.1", "0.5.2", false)]
    [InlineData("1.0.0", "0.9.9", true)]
    [InlineData("0.5.10", "0.5.9", true)]
    public void Compares_two_plain_version_strings(string latest, string current, bool expected) =>
        Assert.Equal(expected, ReleaseVersion.IsNewer(latest, current));

    [Theory]
    [InlineData("not-a-version", "0.5.2")]
    [InlineData("0.5.2", "not-a-version")]
    [InlineData("", "0.5.2")]
    public void Throws_for_a_version_string_that_will_not_parse(string latest, string current) =>
        Assert.Throws<FormatException>(() => ReleaseVersion.IsNewer(latest, current));
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~ReleaseVersionTests"`
Expected: build failure — no `LizTerm.Core.Updates` namespace yet.

- [ ] **Step 3: Write the implementation**

Create `src/LizTerm.Core/Updates/ReleaseVersion.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

namespace LizTerm.Core.Updates;

/// <summary>Compares two plain version strings — the shape AppVersion.Current and a GitHub release tag with its
/// leading "v" already stripped (GitHubReleaseChecker's job) both share. No caller here ever sees a tag.</summary>
public static class ReleaseVersion
{
    public static bool IsNewer(string latest, string current) => Parse(latest) > Parse(current);

    private static Version Parse(string value) =>
        Version.TryParse(value, out var version) ? version : throw new FormatException($"'{value}' is not a version LizTerm understands.");
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~ReleaseVersionTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/LizTerm.Core/Updates/ReleaseVersion.cs tests/LizTerm.Core.Tests/Updates/ReleaseVersionTests.cs
git commit -m "Add ReleaseVersion.IsNewer, the pure release-check comparison"
```

---

## Task 4: `IReleaseChecker` and `GitHubReleaseChecker`

**Files:**
- Create: `src/LizTerm.Core/Updates/ReleaseInfo.cs`
- Create: `src/LizTerm.Core/Updates/IReleaseChecker.cs`
- Create: `src/LizTerm.Core/Updates/GitHubReleaseChecker.cs`
- Modify: `src/LizTerm.Core/CLAUDE.md`
- Test: Create `tests/LizTerm.Core.Tests/Updates/GitHubReleaseCheckerTests.cs`

**Interfaces:**
- Produces: `ReleaseInfo(string Version, string HtmlUrl)`; `IReleaseChecker.GetLatestReleaseAsync(CancellationToken)`
  : `Task<ReleaseInfo>`; `GitHubReleaseChecker(HttpClient)` (public constructor, the test seam);
  `GitHubReleaseChecker.Create()` : `IReleaseChecker` (the production factory).

- [ ] **Step 1: Write the failing test file**

Create `tests/LizTerm.Core.Tests/Updates/GitHubReleaseCheckerTests.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Net;
using System.Text.Json;
using LizTerm.Core.Updates;

namespace LizTerm.Core.Tests.Updates;

public class GitHubReleaseCheckerTests
{
    private sealed class FakeHandler(HttpStatusCode status, string? body) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            var response = new HttpResponseMessage(status);
            if (body is not null) response.Content = new StringContent(body);
            return Task.FromResult(response);
        }
    }

    private static (GitHubReleaseChecker Checker, FakeHandler Handler) Create(HttpStatusCode status, string? body)
    {
        var handler = new FakeHandler(status, body);
        return (new GitHubReleaseChecker(new HttpClient(handler)), handler);
    }

    [Fact]
    public async Task Requests_the_exact_url_and_the_three_headers()
    {
        var (checker, handler) = Create(HttpStatusCode.OK,
            """{"tag_name":"v0.6.0","html_url":"https://github.com/coffeemuse/LizTerm/releases/tag/v0.6.0"}""");

        await checker.GetLatestReleaseAsync(CancellationToken.None);

        Assert.Equal("https://api.github.com/repos/coffeemuse/LizTerm/releases/latest", handler.LastRequest!.RequestUri!.ToString());
        Assert.Equal("application/vnd.github+json", handler.LastRequest.Headers.Accept.Single().MediaType);
        Assert.Equal("2022-11-28", handler.LastRequest.Headers.GetValues("X-GitHub-Api-Version").Single());
        Assert.Equal("LizTerm", handler.LastRequest.Headers.UserAgent.Single().Product!.Name);
    }

    [Fact]
    public async Task Parses_the_tag_and_strips_its_leading_v()
    {
        var (checker, _) = Create(HttpStatusCode.OK,
            """{"tag_name":"v0.6.0","html_url":"https://github.com/coffeemuse/LizTerm/releases/tag/v0.6.0"}""");

        var release = await checker.GetLatestReleaseAsync(CancellationToken.None);

        Assert.Equal("0.6.0", release.Version);
        Assert.Equal("https://github.com/coffeemuse/LizTerm/releases/tag/v0.6.0", release.HtmlUrl);
    }

    [Fact]
    public async Task A_non_success_status_throws_HttpRequestException()
    {
        var (checker, _) = Create(HttpStatusCode.NotFound, null);

        await Assert.ThrowsAsync<HttpRequestException>(() => checker.GetLatestReleaseAsync(CancellationToken.None));
    }

    [Fact]
    public async Task A_malformed_body_throws_JsonException()
    {
        var (checker, _) = Create(HttpStatusCode.OK, "not json");

        await Assert.ThrowsAsync<JsonException>(() => checker.GetLatestReleaseAsync(CancellationToken.None));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~GitHubReleaseCheckerTests"`
Expected: build failure — none of the three new types exist yet.

- [ ] **Step 3: Write the implementation**

Create `src/LizTerm.Core/Updates/ReleaseInfo.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

namespace LizTerm.Core.Updates;

/// <summary>One published release, as far as a caller needs to know: a plain version string (no "v" prefix —
/// GitHubReleaseChecker strips it) and the page to send someone to.</summary>
public sealed record ReleaseInfo(string Version, string HtmlUrl);
```

Create `src/LizTerm.Core/Updates/IReleaseChecker.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

namespace LizTerm.Core.Updates;

public interface IReleaseChecker
{
    Task<ReleaseInfo> GetLatestReleaseAsync(CancellationToken cancellationToken);
}
```

Create `src/LizTerm.Core/Updates/GitHubReleaseChecker.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LizTerm.Core.Updates;

internal sealed record GitHubReleaseResponse(
    [property: JsonPropertyName("tag_name")] string TagName,
    [property: JsonPropertyName("html_url")] string HtmlUrl);

[JsonSerializable(typeof(GitHubReleaseResponse))]
internal partial class GitHubReleaseJsonContext : JsonSerializerContext;

/// <summary>The app's first HTTP call of any kind — everything else goes through b3270. The headers and the 10 s
/// timeout are set per request/client here, not assumed, so a test using a fake handler sees exactly what
/// production sends.</summary>
public sealed class GitHubReleaseChecker(HttpClient httpClient) : IReleaseChecker
{
    private const string LatestReleaseUrl = "https://api.github.com/repos/coffeemuse/LizTerm/releases/latest";
    private static readonly string ProductVersion = typeof(GitHubReleaseChecker).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    /// <summary>The production instance. A launch must never hang behind a check with no route to the internet,
    /// hence the timeout.</summary>
    public static IReleaseChecker Create() => new GitHubReleaseChecker(new HttpClient { Timeout = TimeSpan.FromSeconds(10) });

    public async Task<ReleaseInfo> GetLatestReleaseAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseUrl);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("LizTerm", ProductVersion));

        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
        var release = await JsonSerializer.DeserializeAsync(body, GitHubReleaseJsonContext.Default.GitHubReleaseResponse, cancellationToken)
            ?? throw new JsonException("The releases API returned an empty body.");

        var version = release.TagName.StartsWith('v') ? release.TagName[1..] : release.TagName;
        return new ReleaseInfo(version, release.HtmlUrl);
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~GitHubReleaseCheckerTests"`
Expected: PASS.

- [ ] **Step 5: Document it in `src/LizTerm.Core/CLAUDE.md`**

Add a new subsection after "## Security (`LizTerm.Core.Security`)"'s bullet list, before "## `IEmulatorSession`" —
or, simpler, immediately after the Security section's closing bullet, add:

```markdown
## Updates (`LizTerm.Core.Updates`)

BCL-only, same reason Security is here: the App and any future test can both reach it.

- `IReleaseChecker` / `GitHubReleaseChecker`: one `HttpClient` call to GitHub's `/releases/latest` — the app's only
  HTTP call outside b3270. Headers and the 10 s timeout are set on the request/client the checker is given, not
  assumed, so a test with a fake `HttpMessageHandler` sees exactly what production sends.
- `ReleaseVersion.IsNewer`: the pure comparison, over plain version strings only — no caller here ever sees a
  GitHub tag.
```

- [ ] **Step 6: Commit**

```bash
git add src/LizTerm.Core/Updates/ src/LizTerm.Core/CLAUDE.md tests/LizTerm.Core.Tests/Updates/GitHubReleaseCheckerTests.cs
git commit -m "Add IReleaseChecker and GitHubReleaseChecker (Core)"
```

---

## Task 5: `UpdateCheckResult` and `UpdateChecker.CheckAsync`

**Files:**
- Create: `tests/LizTerm.App.Tests/Fakes/FakeReleaseChecker.cs`
- Create: `src/LizTerm.App/Updates/UpdateCheckResult.cs`
- Create: `src/LizTerm.App/Updates/UpdateChecker.cs`
- Modify: `tests/CLAUDE.md`
- Test: Create `tests/LizTerm.App.Tests/Updates/UpdateCheckerTests.cs`

**Interfaces:**
- Consumes: `IReleaseChecker`, `ReleaseInfo` (Task 4).
- Produces: `UpdateCheckResult` (abstract record) with nested `UpToDate`, `NewerAvailable(string Version, string
  HtmlUrl)`, `Failed(string Reason)`; `UpdateChecker.CheckAsync(IReleaseChecker, string currentVersion,
  CancellationToken)` : `Task<UpdateCheckResult>`; `FakeReleaseChecker` with settable `Result` (`ReleaseInfo`) and
  `Exception` (`Exception?`).

- [ ] **Step 1: Write the fake and the failing tests**

Create `tests/LizTerm.App.Tests/Fakes/FakeReleaseChecker.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.Core.Updates;

namespace LizTerm.App.Tests.Fakes;

/// <summary>Returns Result, or throws Exception when set — the FakeUriOpener shape.</summary>
public sealed class FakeReleaseChecker : IReleaseChecker
{
    public ReleaseInfo Result { get; set; } = new("0.5.2", "https://github.com/coffeemuse/LizTerm/releases/tag/v0.5.2");
    public Exception? Exception { get; set; }

    public Task<ReleaseInfo> GetLatestReleaseAsync(CancellationToken cancellationToken) =>
        Exception is null ? Task.FromResult(Result) : Task.FromException<ReleaseInfo>(Exception);
}
```

Create `tests/LizTerm.App.Tests/Updates/UpdateCheckerTests.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Net.Http;
using System.Text.Json;
using LizTerm.App.Tests.Fakes;
using LizTerm.App.Updates;
using LizTerm.Core.Updates;

namespace LizTerm.App.Tests.Updates;

public class UpdateCheckerTests
{
    [Fact]
    public async Task Reports_a_newer_release_when_the_latest_version_is_higher()
    {
        var checker = new FakeReleaseChecker { Result = new ReleaseInfo("0.6.0", "https://example/release") };

        var result = await UpdateChecker.CheckAsync(checker, "0.5.2", CancellationToken.None);

        var newer = Assert.IsType<UpdateCheckResult.NewerAvailable>(result);
        Assert.Equal("0.6.0", newer.Version);
        Assert.Equal("https://example/release", newer.HtmlUrl);
    }

    [Fact]
    public async Task Reports_up_to_date_when_the_latest_version_is_not_higher()
    {
        var checker = new FakeReleaseChecker { Result = new ReleaseInfo("0.5.2", "https://example/release") };

        var result = await UpdateChecker.CheckAsync(checker, "0.5.2", CancellationToken.None);

        Assert.IsType<UpdateCheckResult.UpToDate>(result);
    }

    [Theory]
    [InlineData(typeof(HttpRequestException))]
    [InlineData(typeof(TaskCanceledException))]
    [InlineData(typeof(JsonException))]
    [InlineData(typeof(FormatException))]
    public async Task Reports_failure_with_a_reason_for_each_caught_exception_type(Type exceptionType)
    {
        var checker = new FakeReleaseChecker { Exception = (Exception)Activator.CreateInstance(exceptionType)! };

        var result = await UpdateChecker.CheckAsync(checker, "0.5.2", CancellationToken.None);

        var failed = Assert.IsType<UpdateCheckResult.Failed>(result);
        Assert.False(string.IsNullOrWhiteSpace(failed.Reason));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~UpdateCheckerTests"`
Expected: build failure — `LizTerm.App.Updates` does not exist yet.

- [ ] **Step 3: Write the implementation**

Create `src/LizTerm.App/Updates/UpdateCheckResult.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

namespace LizTerm.App.Updates;

/// <summary>One of three outcomes a release check can have — the StartupPlan shape: a closed set matched with a
/// switch, rather than a nullable ReleaseInfo and a separate error string.</summary>
public abstract record UpdateCheckResult
{
    public sealed record UpToDate : UpdateCheckResult;
    public sealed record NewerAvailable(string Version, string HtmlUrl) : UpdateCheckResult;
    public sealed record Failed(string Reason) : UpdateCheckResult;
}
```

Create `src/LizTerm.App/Updates/UpdateChecker.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Net.Http;
using System.Text.Json;
using LizTerm.Core.Updates;

namespace LizTerm.App.Updates;

public static class UpdateChecker
{
    public static async Task<UpdateCheckResult> CheckAsync(IReleaseChecker checker, string currentVersion, CancellationToken cancellationToken)
    {
        try
        {
            var latest = await checker.GetLatestReleaseAsync(cancellationToken);
            return ReleaseVersion.IsNewer(latest.Version, currentVersion)
                ? new UpdateCheckResult.NewerAvailable(latest.Version, latest.HtmlUrl)
                : new UpdateCheckResult.UpToDate();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or FormatException)
        {
            return new UpdateCheckResult.Failed(FriendlyReason(ex));
        }
    }

    /// <summary>Never the exception's own message: OpenLinkAsync's "fall through and say so" shape, not a new
    /// error-reporting idiom.</summary>
    private static string FriendlyReason(Exception ex) => ex switch
    {
        TaskCanceledException => "The request timed out.",
        HttpRequestException => "Could not reach GitHub.",
        _ => "The release information could not be read.",
    };
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~UpdateCheckerTests"`
Expected: PASS.

- [ ] **Step 5: Add `FakeReleaseChecker` to `tests/CLAUDE.md`**

In the "App tests" section's "Other fakes:" bullet, append to the list (after `FakeBellRinger`'s clause):
`; and `FakeReleaseChecker` (`Result`, an optional `Exception`).`

- [ ] **Step 6: Commit**

```bash
git add src/LizTerm.App/Updates/UpdateCheckResult.cs src/LizTerm.App/Updates/UpdateChecker.cs \
        tests/LizTerm.App.Tests/Fakes/FakeReleaseChecker.cs tests/LizTerm.App.Tests/Updates/UpdateCheckerTests.cs \
        tests/CLAUDE.md
git commit -m "Add UpdateCheckResult and UpdateChecker.CheckAsync (App)"
```

---

## Task 6: `UpdateNotificationPolicy`

**Files:**
- Create: `src/LizTerm.App/Updates/UpdateNotificationPolicy.cs`
- Test: Create `tests/LizTerm.App.Tests/Updates/UpdateNotificationPolicyTests.cs`

**Interfaces:**
- Consumes: `UpdateCheckResult` (Task 5).
- Produces: `UpdateNotificationPolicy.ShouldShowAutomatically(UpdateCheckResult, string? skippedVersion)` : `bool`.

- [ ] **Step 1: Write the failing test file**

Create `tests/LizTerm.App.Tests/Updates/UpdateNotificationPolicyTests.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.App.Updates;

namespace LizTerm.App.Tests.Updates;

public class UpdateNotificationPolicyTests
{
    [Fact]
    public void Shows_a_newer_release_with_no_skipped_version()
    {
        var result = new UpdateCheckResult.NewerAvailable("0.6.0", "https://example/release");
        Assert.True(UpdateNotificationPolicy.ShouldShowAutomatically(result, null));
    }

    [Fact]
    public void Does_not_show_the_exact_version_that_was_skipped()
    {
        var result = new UpdateCheckResult.NewerAvailable("0.6.0", "https://example/release");
        Assert.False(UpdateNotificationPolicy.ShouldShowAutomatically(result, "0.6.0"));
    }

    [Fact]
    public void Shows_a_later_release_even_when_an_older_one_was_skipped()
    {
        var result = new UpdateCheckResult.NewerAvailable("0.6.1", "https://example/release");
        Assert.True(UpdateNotificationPolicy.ShouldShowAutomatically(result, "0.6.0"));
    }

    [Fact]
    public void Never_shows_up_to_date_or_a_failure_whatever_the_skipped_version_is()
    {
        Assert.False(UpdateNotificationPolicy.ShouldShowAutomatically(new UpdateCheckResult.UpToDate(), null));
        Assert.False(UpdateNotificationPolicy.ShouldShowAutomatically(new UpdateCheckResult.Failed("x"), null));
        Assert.False(UpdateNotificationPolicy.ShouldShowAutomatically(new UpdateCheckResult.UpToDate(), "0.6.0"));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~UpdateNotificationPolicyTests"`
Expected: build failure — `UpdateNotificationPolicy` does not exist yet.

- [ ] **Step 3: Write the implementation**

Create `src/LizTerm.App/Updates/UpdateNotificationPolicy.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

namespace LizTerm.App.Updates;

/// <summary>Whether an *automatic* check is allowed to interrupt the user. A manual check ignores this and always
/// shows its result — see App.CheckForUpdatesManuallyAsync.</summary>
public static class UpdateNotificationPolicy
{
    public static bool ShouldShowAutomatically(UpdateCheckResult result, string? skippedVersion) =>
        result is UpdateCheckResult.NewerAvailable newer && newer.Version != skippedVersion;
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~UpdateNotificationPolicyTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/LizTerm.App/Updates/UpdateNotificationPolicy.cs tests/LizTerm.App.Tests/Updates/UpdateNotificationPolicyTests.cs
git commit -m "Add UpdateNotificationPolicy, the automatic-check suppression rule"
```

---

## Task 7: `UpdateCheckWindow`

**Files:**
- Create: `src/LizTerm.App/Views/UpdateCheckWindow.axaml`
- Create: `src/LizTerm.App/Views/UpdateCheckWindow.axaml.cs`
- Test: Create `tests/LizTerm.App.Tests/Views/UpdateCheckWindowTests.cs`

**Interfaces:**
- Consumes: `UpdateCheckResult` (Task 5).
- Produces: `UpdateCheckWindow(UpdateCheckResult result, string currentVersion, Func<string, Task<bool>>? onDownload,
  Action<string>? onSkip)` — a `Window` whose content depends on `result`; named controls `MessageText`,
  `FallbackText`, `DownloadButton`, `RemindButton`, `SkipButton`, `OkButton`.

- [ ] **Step 1: Write the failing test file**

Create `tests/LizTerm.App.Tests/Views/UpdateCheckWindowTests.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using LizTerm.App.Updates;
using LizTerm.App.Views;

namespace LizTerm.App.Tests.Views;

public class UpdateCheckWindowTests
{
    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    [AvaloniaFact]
    public void A_newer_release_shows_its_version_and_three_buttons()
    {
        var window = new UpdateCheckWindow(new UpdateCheckResult.NewerAvailable("0.6.0", "https://example/release"), "0.5.2", null, null);
        window.Show();

        Assert.Equal("LizTerm 0.6.0 is available. You have 0.5.2.", window.FindControl<TextBlock>("MessageText")!.Text);
        Assert.True(window.FindControl<Button>("DownloadButton")!.IsVisible);
        Assert.True(window.FindControl<Button>("RemindButton")!.IsVisible);
        Assert.True(window.FindControl<Button>("SkipButton")!.IsVisible);
        Assert.False(window.FindControl<Button>("OkButton")!.IsVisible);
    }

    [AvaloniaFact]
    public void Up_to_date_shows_the_current_version_and_only_ok()
    {
        var window = new UpdateCheckWindow(new UpdateCheckResult.UpToDate(), "0.5.2", null, null);
        window.Show();

        Assert.Equal("You're up to date (0.5.2).", window.FindControl<TextBlock>("MessageText")!.Text);
        Assert.True(window.FindControl<Button>("OkButton")!.IsVisible);
        Assert.False(window.FindControl<Button>("DownloadButton")!.IsVisible);
    }

    [AvaloniaFact]
    public void A_failure_shows_its_reason_and_only_ok()
    {
        var window = new UpdateCheckWindow(new UpdateCheckResult.Failed("Could not reach GitHub."), "0.5.2", null, null);
        window.Show();

        Assert.Equal("Couldn't check for updates: Could not reach GitHub.", window.FindControl<TextBlock>("MessageText")!.Text);
        Assert.True(window.FindControl<Button>("OkButton")!.IsVisible);
    }

    [AvaloniaFact]
    public void Download_closes_on_success_without_showing_the_fallback_text()
    {
        var opened = new List<string>();
        var window = new UpdateCheckWindow(new UpdateCheckResult.NewerAvailable("0.6.0", "https://example/release"), "0.5.2",
            onDownload: url => { opened.Add(url); return Task.FromResult(true); }, onSkip: null);
        window.Show();
        var closed = false;
        window.Closed += (_, _) => closed = true;

        Click(window.FindControl<Button>("DownloadButton")!);

        Assert.Equal(["https://example/release"], opened);
        Assert.True(closed);
    }

    [AvaloniaFact]
    public void Download_stays_open_and_names_the_url_when_opening_fails()
    {
        var window = new UpdateCheckWindow(new UpdateCheckResult.NewerAvailable("0.6.0", "https://example/release"), "0.5.2",
            onDownload: _ => Task.FromResult(false), onSkip: null);
        window.Show();
        var closed = false;
        window.Closed += (_, _) => closed = true;

        Click(window.FindControl<Button>("DownloadButton")!);

        Assert.False(closed);
        var fallback = window.FindControl<TextBlock>("FallbackText")!;
        Assert.True(fallback.IsVisible);
        Assert.Equal("Could not open a browser. The release is at https://example/release", fallback.Text);
    }

    [AvaloniaFact]
    public void Skip_reports_the_version_and_closes()
    {
        var skipped = new List<string>();
        var window = new UpdateCheckWindow(new UpdateCheckResult.NewerAvailable("0.6.0", "https://example/release"), "0.5.2",
            onDownload: null, onSkip: skipped.Add);
        window.Show();
        var closed = false;
        window.Closed += (_, _) => closed = true;

        Click(window.FindControl<Button>("SkipButton")!);

        Assert.Equal(["0.6.0"], skipped);
        Assert.True(closed);
    }

    [AvaloniaFact]
    public void Remind_me_later_closes_without_reporting_anything()
    {
        var window = new UpdateCheckWindow(new UpdateCheckResult.NewerAvailable("0.6.0", "https://example/release"), "0.5.2", null, null);
        window.Show();
        var closed = false;
        window.Closed += (_, _) => closed = true;

        Click(window.FindControl<Button>("RemindButton")!);

        Assert.True(closed);
    }

    [AvaloniaFact]
    public void Ok_closes_the_up_to_date_window()
    {
        var window = new UpdateCheckWindow(new UpdateCheckResult.UpToDate(), "0.5.2", null, null);
        window.Show();
        var closed = false;
        window.Closed += (_, _) => closed = true;

        Click(window.FindControl<Button>("OkButton")!);

        Assert.True(closed);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~UpdateCheckWindowTests"`
Expected: build failure — `LizTerm.App.Views.UpdateCheckWindow` does not exist yet.

- [ ] **Step 3: Write the view**

Create `src/LizTerm.App/Views/UpdateCheckWindow.axaml`:

```xml
<!--
  This file is part of LizTerm.
  Copyright 2026 by CoffeeMuse
  SPDX-License-Identifier: BSD-3-Clause
-->
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        x:Class="LizTerm.App.Views.UpdateCheckWindow"
        Icon="avares://LizTerm.App/Assets/Icons/lizterm-256.png"
        Title="Check for Updates" Width="420" SizeToContent="Height" CanResize="False"
        WindowStartupLocation="CenterOwner">
  <!-- Content varies by the result the constructor is given, not by a bound view model: three fixed states, no
       live data. IsDefault/IsCancel are set in code, on whichever button the constructor makes visible, rather
       than in markup, since the hidden buttons for the other two states must never compete for them. -->
  <DockPanel Margin="16">
    <StackPanel DockPanel.Dock="Bottom" Orientation="Horizontal" HorizontalAlignment="Right" Spacing="8" Margin="0,16,0,0">
      <Button x:Name="SkipButton" Content="Skip This Version" Click="OnSkipClick" IsVisible="False" />
      <Button x:Name="RemindButton" Content="Remind Me Later" Click="OnRemindClick" IsVisible="False" />
      <Button x:Name="DownloadButton" Content="Download" Click="OnDownloadClick" IsVisible="False" />
      <Button x:Name="OkButton" Content="OK" Click="OnOkClick" IsVisible="False" />
    </StackPanel>
    <TextBlock x:Name="MessageText" DockPanel.Dock="Top" TextWrapping="Wrap" />
    <TextBlock x:Name="FallbackText" DockPanel.Dock="Top" Margin="0,8,0,0" TextWrapping="Wrap" FontSize="12"
               Foreground="#A0A0A0" IsVisible="False" />
  </DockPanel>
</Window>
```

Create `src/LizTerm.App/Views/UpdateCheckWindow.axaml.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Controls;
using Avalonia.Interactivity;
using LizTerm.App.Updates;

namespace LizTerm.App.Views;

/// <summary>Reports one release-check outcome — newer available, up to date, or a failure. App owns the one
/// instance at a time (App.axaml.cs's _updateCheck), the _about/_preferences shape.</summary>
public partial class UpdateCheckWindow : Window
{
    private readonly UpdateCheckResult _result;
    private readonly Func<string, Task<bool>>? _onDownload;
    private readonly Action<string>? _onSkip;

    /// <summary>Design-time only.</summary>
    public UpdateCheckWindow() : this(
        new UpdateCheckResult.NewerAvailable("0.6.0", "https://github.com/coffeemuse/LizTerm/releases/tag/v0.6.0"),
        "0.5.2", null, null)
    { }

    public UpdateCheckWindow(UpdateCheckResult result, string currentVersion, Func<string, Task<bool>>? onDownload, Action<string>? onSkip)
    {
        InitializeComponent();
        _result = result;
        _onDownload = onDownload;
        _onSkip = onSkip;

        switch (result)
        {
            case UpdateCheckResult.NewerAvailable newer:
                MessageText.Text = $"LizTerm {newer.Version} is available. You have {currentVersion}.";
                DownloadButton.IsVisible = true;
                RemindButton.IsVisible = true;
                SkipButton.IsVisible = true;
                DownloadButton.IsDefault = true;
                RemindButton.IsCancel = true;
                break;
            case UpdateCheckResult.UpToDate:
                MessageText.Text = $"You're up to date ({currentVersion}).";
                OkButton.IsVisible = true;
                OkButton.IsDefault = true;
                OkButton.IsCancel = true;
                break;
            case UpdateCheckResult.Failed failed:
                MessageText.Text = "Couldn't check for updates: " + failed.Reason;
                OkButton.IsVisible = true;
                OkButton.IsDefault = true;
                OkButton.IsCancel = true;
                break;
        }
    }

    private async void OnDownloadClick(object? sender, RoutedEventArgs e)
    {
        var newer = (UpdateCheckResult.NewerAvailable)_result;
        if (_onDownload is null || await _onDownload(newer.HtmlUrl))
        {
            Close();
            return;
        }
        FallbackText.Text = "Could not open a browser. The release is at " + newer.HtmlUrl;
        FallbackText.IsVisible = true;
    }

    private void OnSkipClick(object? sender, RoutedEventArgs e)
    {
        _onSkip?.Invoke(((UpdateCheckResult.NewerAvailable)_result).Version);
        Close();
    }

    private void OnRemindClick(object? sender, RoutedEventArgs e) => Close();
    private void OnOkClick(object? sender, RoutedEventArgs e) => Close();
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~UpdateCheckWindowTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/LizTerm.App/Views/UpdateCheckWindow.axaml src/LizTerm.App/Views/UpdateCheckWindow.axaml.cs \
        tests/LizTerm.App.Tests/Views/UpdateCheckWindowTests.cs
git commit -m "Add UpdateCheckWindow"
```

---

## Task 8: Wire the checker into `App` — startup and manual entry points

**Files:**
- Modify: `src/LizTerm.App/App.axaml.cs`
- Modify: `src/LizTerm.App/CLAUDE.md`
- Test: Create `tests/LizTerm.App.Tests/Updates/AppUpdateCheckTests.cs`

**Interfaces:**
- Consumes: `IReleaseChecker`, `GitHubReleaseChecker.Create()` (Task 4); `UpdateCheckResult`, `UpdateChecker.CheckAsync`,
  `UpdateNotificationPolicy.ShouldShowAutomatically` (Tasks 5–6); `UpdateCheckWindow` (Task 7);
  `SettingsViewModel.CheckForUpdatesAutomatically`/`SkippedUpdateVersion` (Task 2); `AvaloniaUriOpener` (existing,
  `src/LizTerm.App/Files/AvaloniaUriOpener.cs`).
- Produces: `App.CheckForUpdatesOnStartupAsync()` : `Task<UpdateCheckWindow?>` (public, real dependencies) and its
  internal overload `CheckForUpdatesOnStartupAsync(IReleaseChecker, SettingsViewModel)` : `Task<UpdateCheckWindow?>`
  (the test seam, `ShowPreferences(SettingsViewModel)`'s shape); `App.CheckForUpdatesManuallyAsync(Window? owner)`
  : `Task<UpdateCheckWindow>` (public) and its internal overload `CheckForUpdatesManuallyAsync(Window?,
  IReleaseChecker, SettingsViewModel)` : `Task<UpdateCheckWindow>`.

**Why two overloads of each, mirroring `ShowPreferences`:** `App.Settings` is lazy and, once touched, is backed by
the *real* per-OS settings file (`AppPaths.SettingsFile()`). `PreferencesWindowTests.The_app_shows_one_preferences_
window_at_a_time` avoids that by calling the internal overload with its own in-memory `SettingsViewModel`. This
task's tests do the same, and additionally never touch the network, by supplying a `FakeReleaseChecker`.

- [ ] **Step 1: Write the failing test file**

Create `tests/LizTerm.App.Tests/Updates/AppUpdateCheckTests.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using LizTerm.App.Tests.Fakes;
using LizTerm.App.ViewModels;
using LizTerm.Core.Updates;

namespace LizTerm.App.Tests.Updates;

public class AppUpdateCheckTests
{
    private static App CurrentApp => (App)Application.Current!;

    [AvaloniaFact]
    public async Task Startup_check_does_nothing_when_automatic_checking_is_off()
    {
        var settings = new SettingsViewModel();
        settings.CheckForUpdatesAutomatically = false;
        var checker = new FakeReleaseChecker { Result = new ReleaseInfo("0.6.0", "https://example/release") };

        var window = await CurrentApp.CheckForUpdatesOnStartupAsync(checker, settings);

        Assert.Null(window);
    }

    [AvaloniaFact]
    public async Task Startup_check_shows_a_dialog_for_a_newer_unskipped_release()
    {
        var settings = new SettingsViewModel();
        var checker = new FakeReleaseChecker { Result = new ReleaseInfo("0.6.0", "https://example/release") };

        var window = await CurrentApp.CheckForUpdatesOnStartupAsync(checker, settings);

        Assert.NotNull(window);
        Assert.Equal($"LizTerm 0.6.0 is available. You have {AppVersion.Current}.",
            window!.FindControl<TextBlock>("MessageText")!.Text);
        window.Close();
    }

    [AvaloniaFact]
    public async Task Startup_check_stays_silent_for_the_version_the_user_already_skipped()
    {
        var settings = new SettingsViewModel();
        settings.SkippedUpdateVersion = "0.6.0";
        var checker = new FakeReleaseChecker { Result = new ReleaseInfo("0.6.0", "https://example/release") };

        var window = await CurrentApp.CheckForUpdatesOnStartupAsync(checker, settings);

        Assert.Null(window);
    }

    [AvaloniaFact]
    public async Task Startup_check_stays_silent_when_already_up_to_date()
    {
        var settings = new SettingsViewModel();
        var checker = new FakeReleaseChecker { Result = new ReleaseInfo(AppVersion.Current, "https://example/release") };

        var window = await CurrentApp.CheckForUpdatesOnStartupAsync(checker, settings);

        Assert.Null(window);
    }

    [AvaloniaFact]
    public async Task Startup_check_stays_silent_and_does_not_throw_when_the_checker_fails()
    {
        var settings = new SettingsViewModel();
        var checker = new FakeReleaseChecker { Exception = new HttpRequestException("no network") };

        var window = await CurrentApp.CheckForUpdatesOnStartupAsync(checker, settings);

        Assert.Null(window);
    }

    [AvaloniaFact]
    public async Task Manual_check_always_shows_a_dialog_even_for_a_previously_skipped_version()
    {
        var settings = new SettingsViewModel();
        settings.SkippedUpdateVersion = "0.6.0";
        var checker = new FakeReleaseChecker { Result = new ReleaseInfo("0.6.0", "https://example/release") };

        var window = await CurrentApp.CheckForUpdatesManuallyAsync(null, checker, settings);

        Assert.True(window.FindControl<Button>("DownloadButton")!.IsVisible);
        window.Close();
    }

    [AvaloniaFact]
    public async Task Manual_check_reports_up_to_date()
    {
        var settings = new SettingsViewModel();
        var checker = new FakeReleaseChecker { Result = new ReleaseInfo(AppVersion.Current, "https://example/release") };

        var window = await CurrentApp.CheckForUpdatesManuallyAsync(null, checker, settings);

        Assert.Equal($"You're up to date ({AppVersion.Current}).", window.FindControl<TextBlock>("MessageText")!.Text);
        window.Close();
    }

    [AvaloniaFact]
    public async Task The_app_shows_one_update_check_window_at_a_time()
    {
        var settings = new SettingsViewModel();
        var checker = new FakeReleaseChecker { Result = new ReleaseInfo("0.6.0", "https://example/release") };

        var first = await CurrentApp.CheckForUpdatesManuallyAsync(null, checker, settings);
        var again = await CurrentApp.CheckForUpdatesManuallyAsync(null, checker, settings);
        Assert.Same(first, again);

        first.Close();
        var third = await CurrentApp.CheckForUpdatesManuallyAsync(null, checker, settings);
        Assert.NotSame(first, third);
        third.Close();
    }

    [AvaloniaFact]
    public async Task Skip_writes_only_to_the_settings_object_the_call_was_given()
    {
        var settings = new SettingsViewModel();
        var checker = new FakeReleaseChecker { Result = new ReleaseInfo("0.6.0", "https://example/release") };

        var window = await CurrentApp.CheckForUpdatesManuallyAsync(null, checker, settings);
        window.FindControl<Button>("SkipButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.Equal("0.6.0", settings.SkippedUpdateVersion);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~AppUpdateCheckTests"`
Expected: build failure — `App` has no `CheckForUpdatesOnStartupAsync`/`CheckForUpdatesManuallyAsync` overloads yet.

- [ ] **Step 3: Add the usings**

In `src/LizTerm.App/App.axaml.cs`, add two usings to the existing block (alphabetical among the others):

```csharp
using LizTerm.App.Updates;
using LizTerm.Core.Updates;
```

- [ ] **Step 4: Add the fields**

Beside the existing `private readonly SystemBellRinger _bellRinger = new();` field, add:

```csharp
    /// <summary>The process's one release checker (#107).</summary>
    private readonly IReleaseChecker _releaseChecker = GitHubReleaseChecker.Create();
```

Beside `private PreferencesWindow? _preferences;` (or anywhere among the other nullable single-instance fields),
add:

```csharp
    private UpdateCheckWindow? _updateCheck;
```

- [ ] **Step 5: Fire the startup check from `Execute`**

In `src/LizTerm.App/App.axaml.cs`, change:

```csharp
                case StartupPlan.OpenSession open:
                    OpenSession(open.Profile, open.FromStore);
                    break;
                default:
                    ShowPicker();
                    break;
```

to:

```csharp
                case StartupPlan.OpenSession open:
                    OpenSession(open.Profile, open.FromStore);
                    _ = CheckForUpdatesOnStartupAsync();
                    break;
                default:
                    ShowPicker();
                    _ = CheckForUpdatesOnStartupAsync();
                    break;
```

Fire-and-forget, and deliberately absent from the `ShowError` case and from the `catch` block below it: a launch
must never wait on a network call, and a run that could not even open its first window has nothing to check
updates *for*.

- [ ] **Step 6: Add the four new methods**

Add these after the `ShowPreferences(SettingsViewModel settings)` method and before `AboutEngine`:

```csharp
    /// <summary>Runs once at startup (fired from Execute, never after ShowError or a failed open). Silent unless
    /// CheckForUpdatesAutomatically is on, the check finds a newer release, and that release is not the one the
    /// user already skipped.</summary>
    public Task<UpdateCheckWindow?> CheckForUpdatesOnStartupAsync() => CheckForUpdatesOnStartupAsync(_releaseChecker, Settings);

    /// <summary>The rule with the checker and settings as arguments, so a test can exercise it with a
    /// FakeReleaseChecker and an in-memory SettingsViewModel and never touch the network or the real settings
    /// file — ShowPreferences(SettingsViewModel)'s shape.</summary>
    internal async Task<UpdateCheckWindow?> CheckForUpdatesOnStartupAsync(IReleaseChecker checker, SettingsViewModel settings)
    {
        if (!settings.Current.CheckForUpdatesAutomatically) return null;
        try
        {
            var result = await UpdateChecker.CheckAsync(checker, AppVersion.Current, CancellationToken.None);
            if (!UpdateNotificationPolicy.ShouldShowAutomatically(result, settings.Current.SkippedUpdateVersion)) return null;
            return await ShowUpdateCheckResultAsync(result, owner: null, settings);
        }
        catch
        {
            // An unattended check is not worth a crash.
            return null;
        }
    }

    /// <summary>Help &gt; Check for Updates..., always reporting something — newer, up to date, or the failure
    /// reason — and ignoring any skipped version, because the user asked directly.</summary>
    public Task<UpdateCheckWindow> CheckForUpdatesManuallyAsync(Window? owner) => CheckForUpdatesManuallyAsync(owner, _releaseChecker, Settings);

    internal async Task<UpdateCheckWindow> CheckForUpdatesManuallyAsync(Window? owner, IReleaseChecker checker, SettingsViewModel settings)
    {
        var result = await UpdateChecker.CheckAsync(checker, AppVersion.Current, CancellationToken.None);
        return await ShowUpdateCheckResultAsync(result, owner, settings);
    }

    /// <summary>One at a time, the _about/_preferences shape. Download opens the result's release page through an
    /// AvaloniaUriOpener built on the dialog itself (there is no app-wide opener; it is always tied to whichever
    /// window it acts through, as SessionWindow's own is). Skip writes SkippedUpdateVersion through the settings
    /// object this call was given, so a test's in-memory settings are what change, never the real file.</summary>
    private async Task<UpdateCheckWindow> ShowUpdateCheckResultAsync(UpdateCheckResult result, Window? owner, SettingsViewModel settings)
    {
        if (_updateCheck is { } showing)
        {
            showing.Activate();
            return showing;
        }
        UpdateCheckWindow? window = null;
        window = new UpdateCheckWindow(result, AppVersion.Current,
            onDownload: url => new AvaloniaUriOpener(window!).OpenAsync(new Uri(url)),
            onSkip: version => settings.SkippedUpdateVersion = version);
        _updateCheck = window;
        window.Closed += (_, _) => { if (ReferenceEquals(_updateCheck, window)) _updateCheck = null; };
        var target = owner ?? ActiveWindow();
        if (target is null) window.Show(); else await window.ShowDialog(target);
        return window;
    }
```

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~AppUpdateCheckTests"`
Expected: PASS.

- [ ] **Step 8: Document it in `src/LizTerm.App/CLAUDE.md`**

In the "### Settings and Preferences" section, after the bullet that ends "...the Crosshair shape; both converters
are subclasses of `EnumIsConverter<TEnum>`, which holds the rule.", add a new bullet:

```markdown
- **The release check (#107).** `App._releaseChecker` (`GitHubReleaseChecker.Create()`) is the process's one
  checker. `CheckForUpdatesOnStartupAsync()` fires once from `Execute`, after a session or the picker has actually
  opened — never after `ShowError`, never when opening either one threw — and stays silent unless
  `Settings.CheckForUpdatesAutomatically` is on, the check finds a newer release, and that release is not
  `Settings.SkippedUpdateVersion` (`UpdateNotificationPolicy.ShouldShowAutomatically`). Help's own
  `CheckForUpdatesManuallyAsync(Window?)` always shows a result, ignoring any skip. Both funnel through
  `ShowUpdateCheckResultAsync`, one `UpdateCheckWindow` at a time (`_updateCheck`, the `_about`/`_preferences`
  shape); its Download button opens the release page through an `AvaloniaUriOpener` built on the dialog itself,
  and its Skip button writes `SkippedUpdateVersion`. Each public entry point has an internal overload taking the
  checker and the `SettingsViewModel` explicitly, `ShowPreferences(SettingsViewModel)`'s shape, so tests never
  touch the network or the real settings file.
```

- [ ] **Step 9: Commit**

```bash
git add src/LizTerm.App/App.axaml.cs src/LizTerm.App/CLAUDE.md tests/LizTerm.App.Tests/Updates/AppUpdateCheckTests.cs
git commit -m "Wire the release check into App: startup and manual entry points"
```

---

## Task 9: The Help menu's "Check for Updates..." item

**Files:**
- Modify: `src/LizTerm.App/Views/SessionWindow.axaml`
- Modify: `src/LizTerm.App/Views/SessionWindow.axaml.cs`
- Modify: `src/LizTerm.App/CLAUDE.md`
- Modify: `docs/user-guide.md`

No new test file: the existing `NativeMenuTests`' generic parity walk (`AssertMenusMatch`) and activation walk
(`Every_native_item_can_actually_be_activated`) already cover every Help item structurally — both recurse over
whatever is declared, and this item's classic/native pair (matching headers, both `Command == null`, both wired
by a `Click` handler) needs no new case in either. Verified by re-running the existing suite in Step 3.

**Interfaces:**
- Consumes: `App.CheckForUpdatesManuallyAsync(Window?)` (Task 8).

- [ ] **Step 1: Add the native menu item**

In `src/LizTerm.App/Views/SessionWindow.axaml`, change:

```xml
            <NativeMenuItem Header="R_eleases" Command="{Binding OpenLinkCommand}"
                            CommandParameter="{x:Static app:ProjectLinks.Releases}" />
            <NativeMenuItemSeparator />
            <NativeMenuItem Header="_Wire Log" ToggleType="CheckBox" IsChecked="{Binding IsWireLogging, Mode=OneWay}"
                            Click="OnWireLogClickNative" />
```

to:

```xml
            <NativeMenuItem Header="R_eleases" Command="{Binding OpenLinkCommand}"
                            CommandParameter="{x:Static app:ProjectLinks.Releases}" />
            <NativeMenuItem Header="_Check for Updates..." Click="OnCheckForUpdatesClickNative" />
            <NativeMenuItemSeparator />
            <NativeMenuItem Header="_Wire Log" ToggleType="CheckBox" IsChecked="{Binding IsWireLogging, Mode=OneWay}"
                            Click="OnWireLogClickNative" />
```

- [ ] **Step 2: Add the matching classic menu item**

In the same file, change:

```xml
        <MenuItem Header="R_eleases" Command="{Binding OpenLinkCommand}"
                  CommandParameter="{x:Static app:ProjectLinks.Releases}" />
        <Separator />
        <MenuItem x:Name="WireLogMenuItem" Header="_Wire Log" ToggleType="CheckBox" IsChecked="{Binding IsWireLogging, Mode=TwoWay}" />
```

to:

```xml
        <MenuItem Header="R_eleases" Command="{Binding OpenLinkCommand}"
                  CommandParameter="{x:Static app:ProjectLinks.Releases}" />
        <MenuItem Header="_Check for Updates..." Click="OnCheckForUpdatesClick" />
        <Separator />
        <MenuItem x:Name="WireLogMenuItem" Header="_Wire Log" ToggleType="CheckBox" IsChecked="{Binding IsWireLogging, Mode=TwoWay}" />
```

- [ ] **Step 3: Verify the parity/activation tests still pass with no changes to them**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~NativeMenuTests"`
Expected: build failure at this point (`OnCheckForUpdatesClickNative`/`OnCheckForUpdatesClick` do not exist yet in
the code-behind) — continue to Step 4, then re-run this same command and expect PASS.

- [ ] **Step 4: Add the code-behind handlers**

In `src/LizTerm.App/Views/SessionWindow.axaml.cs`, beside `OnPreferencesClick`/`OnPreferencesClickNative`
(this item follows the same "reach App directly" shape as About and Preferences, not `OpenLinkCommand`, because
it needs *this* window as the dialog's owner):

```csharp
    private async void OnCheckForUpdatesClickNative(object? sender, EventArgs e) => await CheckForUpdatesAsync();
    private async void OnCheckForUpdatesClick(object? sender, RoutedEventArgs e) => await CheckForUpdatesAsync();

    private async Task CheckForUpdatesAsync()
    {
        if (Avalonia.Application.Current is App app) await app.CheckForUpdatesManuallyAsync(this);
    }
```

- [ ] **Step 5: Run the full menu test class to verify it passes**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~NativeMenuTests"`
Expected: PASS (every test in the class, including the parity and activation walks over the new item).

- [ ] **Step 6: Document it in `src/LizTerm.App/CLAUDE.md`**

In the "### Wiring rules" section, after the bullet beginning "**Wire Log is the one item whose two menus differ
on purpose.**", add:

```markdown
- **Check for Updates... follows About and Preferences' wiring, not `OpenLinkCommand`'s** (#107): a `Click`
  handler on both menus, `SessionWindow.CheckForUpdatesAsync`, which reaches `App.CheckForUpdatesManuallyAsync`
  with `this` as the dialog's owner. `OpenLinkCommand` has no way to pass a window along, which this item needs
  and a plain Help link does not.
```

- [ ] **Step 7: Document it in `docs/user-guide.md`**

In the "## Menus" section, after the `**Help > Releases**` bullet, add:

```markdown
- **Help > Check for Updates...** checks now, and always tells you the result — a newer version, or that you are
  up to date, or why the check failed. See [Preferences](#preferences) for the automatic version of this check.
```

- [ ] **Step 8: Commit**

```bash
git add src/LizTerm.App/Views/SessionWindow.axaml src/LizTerm.App/Views/SessionWindow.axaml.cs \
        src/LizTerm.App/CLAUDE.md docs/user-guide.md
git commit -m "Add Help > Check for Updates..."
```

---

## Task 10: The Preferences window's new General tab

**Files:**
- Modify: `src/LizTerm.App/Views/PreferencesWindow.axaml`
- Modify: `tests/LizTerm.App.Tests/Views/PreferencesWindowTests.cs`
- Modify: `docs/user-guide.md`

**Interfaces:**
- Consumes: `SettingsViewModel.CheckForUpdatesAutomatically` (Task 2).

- [ ] **Step 1: Update the failing tab-order test**

In `tests/LizTerm.App.Tests/Views/PreferencesWindowTests.cs`, the existing test asserts the pre-General tab order
and picks the Display tab's own radio by `.First()`. Change:

```csharp
    /// <summary>The tabs are the window's structure: Display, Bell and Window in that order, the last read top of
    /// the window to bottom (menu bar, status bar, keypad). Every control keeps its name, so the other tests here
    /// find it whichever tab is selected.</summary>
    [AvaloniaFact]
    public void The_settings_sit_on_display_bell_and_window_tabs_in_that_order()
    {
        var (window, _) = Show();
        var tabs = window.FindControl<TabControl>("Tabs")!;

        Assert.Equal(["Display", "Bell", "Window"], tabs.Items.Cast<TabItem>().Select(t => (string)t.Header!));
        Assert.Equal(0, tabs.SelectedIndex);
        Assert.Same(window.FindControl<RadioButton>("CrosshairNone"), tabs.Items.Cast<TabItem>().First().FindLogicalDescendantOfType<RadioButton>());
    }
```

to:

```csharp
    /// <summary>The tabs are the window's structure: General first (#107 — not about the screen, the bell, or
    /// the window's own chrome), then Display, Bell and Window in that order, the last read top of the window to
    /// bottom (menu bar, status bar, keypad). Every control keeps its name, so the other tests here find it
    /// whichever tab is selected.</summary>
    [AvaloniaFact]
    public void The_settings_sit_on_general_display_bell_and_window_tabs_in_that_order()
    {
        var (window, _) = Show();
        var tabs = window.FindControl<TabControl>("Tabs")!;

        Assert.Equal(["General", "Display", "Bell", "Window"], tabs.Items.Cast<TabItem>().Select(t => (string)t.Header!));
        Assert.Equal(0, tabs.SelectedIndex);
        Assert.Same(window.FindControl<RadioButton>("CrosshairNone"), tabs.Items.Cast<TabItem>().ElementAt(1).FindLogicalDescendantOfType<RadioButton>());
    }
```

Then add a new test for the checkbox itself, mirroring `StatusBarTagsBox`'s test elsewhere in the file (search
the file for how it drives `StatusBarTagsBox` to match the exact assertion shape used there):

```csharp
    [AvaloniaFact]
    public void Clicking_the_check_for_updates_box_sets_the_shared_settings()
    {
        var (window, settings) = Show();
        var box = window.FindControl<CheckBox>("CheckForUpdatesBox")!;

        Assert.True(box.IsChecked);

        Click(box);

        Assert.False(settings.CheckForUpdatesAutomatically);
        Assert.False(box.IsChecked);
    }
```

(If the file's existing `Click` helper is typed to `Button` only, as seen in the file's current form, add a
one-line overload beside it: `private static void Click(CheckBox box) => box.RaiseEvent(new
RoutedEventArgs(Button.ClickEvent));` — a `CheckBox` is a `Button`-derived `ToggleButton` in Avalonia and raises
the same `ClickEvent`.)

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~PreferencesWindowTests"`
Expected: FAIL — the tab list does not yet include "General", and `CheckForUpdatesBox` does not exist.

- [ ] **Step 3: Add the General tab**

In `src/LizTerm.App/Views/PreferencesWindow.axaml`, change:

```xml
    <TabControl x:Name="Tabs" Padding="0,12,0,0">
      <!-- Each row is its own two-column grid inside a StackPanel rather than a row of one shared grid, so a row
           can be hidden as a unit (the menu bar) and every tab has the same shape. -->
      <TabItem Header="Display">
```

to:

```xml
    <TabControl x:Name="Tabs" Padding="0,12,0,0">
      <!-- Each row is its own two-column grid inside a StackPanel rather than a row of one shared grid, so a row
           can be hidden as a unit (the menu bar) and every tab has the same shape. General is first: it is where
           a setting that is not about the screen, the bell, or the window's own chrome belongs (#107), and it is
           where the next such setting goes too, rather than reopening the question of where it belongs. -->
      <TabItem Header="General">
        <StackPanel Spacing="12">
          <Grid Classes="settings" ColumnDefinitions="120,*">
            <TextBlock Text="Updates" />
            <CheckBox x:Name="CheckForUpdatesBox" Grid.Column="1" Content="Automatically check for updates on launch"
                      IsChecked="{Binding CheckForUpdatesAutomatically, Mode=TwoWay}" />
          </Grid>
        </StackPanel>
      </TabItem>
      <TabItem Header="Display">
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~PreferencesWindowTests"`
Expected: PASS (every test in the class — including `Every_tab_fits_the_fixed_size`, which iterates by
`tabs.ItemCount` and so needs no change to cover the new tab, and the window's fixed `Height="446"`, which stays
correct since Window remains the tallest tab).

- [ ] **Step 5: Document it in `docs/user-guide.md`**

In the "## Preferences" section, change "The settings sit on three tabs:" to "The settings sit on four tabs:",
and insert a new subsection before "**Display**":

```markdown
**General**

- **Updates** — whether LizTerm checks for a newer release automatically when it starts (on by default). A check
  never interrupts you unless there is something new, and skipping a version keeps it quiet only until the next
  one is published. Help > Check for Updates... checks on demand regardless of this setting, and always tells you
  the result.
```

- [ ] **Step 6: Run the full test suite**

Run: `dotnet test LizTerm.slnx`
Expected: PASS, every project.

- [ ] **Step 7: Run the zero-warning gate**

Run: `dotnet build LizTerm.slnx --no-incremental 2>&1 | grep -c " warning "`
Expected: `0`. If not, fix the warning(s) before committing.

- [ ] **Step 8: Commit**

```bash
git add src/LizTerm.App/Views/PreferencesWindow.axaml tests/LizTerm.App.Tests/Views/PreferencesWindowTests.cs docs/user-guide.md
git commit -m "Add the Preferences General tab with the check-for-updates toggle"
```

---

## Self-Review Notes

- **Spec coverage:** §2–§4 (Core: `IReleaseChecker`/`GitHubReleaseChecker`/`ReleaseVersion`) → Tasks 3–4. §4
  (`UpdateCheckResult`, `UpdateChecker`, `UpdateNotificationPolicy`) → Tasks 5–6. §5 (settings) → Tasks 1–2. §6
  (App coordinator, `UpdateCheckWindow`) → Tasks 7–8. §7 (Help menu) → Task 9. §8 (Preferences General tab) →
  Task 10. §9 testing plan → covered per-task above (Core: Task 3–4; App: Tasks 5–10). §12 documentation →
  folded into the task whose deliverable needs it (Task 4: Core CLAUDE.md; Task 5: tests/CLAUDE.md; Task 8: App
  CLAUDE.md Settings section; Task 9: App CLAUDE.md Menus section + user-guide.md Help line; Task 10:
  user-guide.md Preferences section).
- **Design refinement during planning, not a spec change:** the spec's App-wiring sketch (§6) reads
  `Settings.Current...`/`Settings.SkippedUpdateVersion = ...` directly inside `CheckForUpdatesOnStartupAsync`.
  Task 8 instead threads a `SettingsViewModel` parameter through both entry points (mirroring
  `ShowPreferences(SettingsViewModel)`), because `App.Settings` is lazily backed by the real settings file the
  moment anything touches it, and a test that flipped `CheckForUpdatesAutomatically` on the real singleton would
  write to the developer's actual `settings.json`. The behavior described in the spec is unchanged — only the
  test seam is more explicit than the spec's inline sketch.
- **Type consistency check:** `UpdateCheckResult.NewerAvailable(string Version, string HtmlUrl)` (Task 5) is used
  identically in Tasks 6, 7, 8 and their tests. `IReleaseChecker.GetLatestReleaseAsync(CancellationToken)` (Task
  4) is called with that exact signature from `UpdateChecker.CheckAsync` (Task 5) and from
  `FakeReleaseChecker` (Task 5). `SettingsViewModel.CheckForUpdatesAutomatically`/`SkippedUpdateVersion` (Task 2)
  are referenced with those exact names in Tasks 8 and 10. No placeholders remain in any step.

## Execution Handoff

Plan complete and saved to `docs/superpowers/plans/2026-09-14-lizterm-release-check.md`. Two execution options:

**1. Subagent-Driven (recommended)** - I dispatch a fresh subagent per task, review between tasks, fast iteration

**2. Inline Execution** - Execute tasks in this session using executing-plans, batch execution with checkpoints

**Which approach?**
