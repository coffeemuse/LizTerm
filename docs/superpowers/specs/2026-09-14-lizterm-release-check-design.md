# LizTerm: checking for a new release

Date: 2026-09-14. Issue: #107. Status: approved in discussion on 2026-09-14; awaiting review of this text.

## 1. Purpose

A user downloaded 0.5.1 after 0.5.2 had already been published. Nothing in the app told them a newer release
existed — releases go to GitHub Releases (`docs/ci-and-release.md`), and a running LizTerm has no way to learn
that. This spec adds a release check: compare the running version against the latest published GitHub release
for `coffeemuse/LizTerm`, and tell the user when theirs is behind.

Decisions taken in discussion on 2026-09-14, each justified in the section named:

- **The automatic check runs once, at startup, and nowhere else** (§4). No background timer, no re-check while
  the app stays open. TN3270 sessions can run for days, but a user who wants to check mid-session has the menu
  item.
- **Automatic checking defaults to on** (§3). It is one unauthenticated GET to a public API, carries no personal
  data, and a preference turns it off.
- **The result is a modal dialog, not the session window's banner pattern** (§6). Robert's stated reason: LizTerm
  can launch straight into a session, bypassing the picker, so a per-session-window banner risks never being
  seen by someone who launched that way. A dialog is shown regardless of which window came up first.
- **Three outcomes reach the user differently.** Automatic checks are silent unless a newer release exists, and
  even then only once per version (§5) — a check that finds nothing new, or fails outright, never interrupts an
  unattended launch. A manual check (Help > Check for Updates...) always reports something, because the user
  asked directly (§7).
- **The newer-version dialog has three buttons — Download, Remind Me Later, Skip This Version — and Skip is
  per-version** (§5, §6). Remind Me Later changes nothing, so the same dialog reappears next launch. Skip
  remembers exactly that version string; a later release is a different string and is never suppressed by an
  old skip.
- **Download opens the release's GitHub page, not a direct asset link** (§6). One `IUriOpener.OpenAsync` call,
  the existing pattern Help's own links use; the user picks their platform's asset the way they already do from
  Help > Releases.
- **The Preferences checkbox gets a new "General" tab**, not a row on Window (§8). Robert asked for this
  explicitly, with a second item already planned for the same tab, so General starts today rather than being
  invented as a one-off later.

## 2. Where the pieces live

`GET https://api.github.com/repos/coffeemuse/LizTerm/releases/latest` and JSON parsing are pure BCL, so the HTTP
call lives in **`LizTerm.Core`** (`src/LizTerm.Core/Updates/`), beside `Security/`'s `ICertificateFetcher` —
BCL-only network code the App and any future test can both reach, per the root `CLAUDE.md` dependency rule.
Everything that knows this is *LizTerm's own* version, that a check just ran, or that a window should appear is
App-only and lives in `src/LizTerm.App/Updates/` and `Views/`.

This is the app's first HTTP call of any kind — every existing network path goes through b3270 as a child
process. No new package reference is needed: `HttpClient` and `System.Text.Json` are both BCL, and the project
already uses a source-generated `JsonSerializerContext` per settings/profile JSON, which this follows.

## 3. The check itself (Core)

```csharp
namespace LizTerm.Core.Updates;

public sealed record ReleaseInfo(string Version, string HtmlUrl);

public interface IReleaseChecker
{
    Task<ReleaseInfo> GetLatestReleaseAsync(CancellationToken cancellationToken);
}
```

`GitHubReleaseChecker : IReleaseChecker` is the real implementation: a `GET` to `/repos/coffeemuse/LizTerm/releases/latest`
with `Accept: application/vnd.github+json`, `X-GitHub-Api-Version: 2022-11-28`, and `User-Agent: LizTerm/<version>`
(GitHub rejects a request with no User-Agent). `/releases/latest` already excludes drafts and prereleases, which
is exactly the "actually published" the issue asked for — release.yml's draft stays invisible to every running
LizTerm until a human publishes it. The response's `tag_name` (e.g. `"v0.6.0"`) has its leading `v` stripped
before it becomes `ReleaseInfo.Version`, so nothing downstream parses a tag — only a plain version string,
matching `AppVersion.Current`'s own shape. `html_url` becomes `ReleaseInfo.HtmlUrl` unchanged.

```csharp
public static class GitHubReleaseChecker
{
    public static IReleaseChecker Create() => new GitHubReleaseCheckerImpl(new HttpClient
    {
        BaseAddress = new Uri("https://api.github.com"),
        Timeout = TimeSpan.FromSeconds(10),
    });
}
```

The 10 s timeout bounds both the automatic and the manual check; a host with no internet reachable at all must
not hang a launch behind it. `GitHubReleaseCheckerImpl` takes the `HttpClient` by constructor, which is the test
seam: production gets `Create()`'s configured client, tests build one over a fake `HttpMessageHandler`.

Deserialization is a small internal DTO (`GitHubReleaseResponse`, `tag_name` and `html_url` only — everything
else in the response is ignored) read through a source-generated `GitHubReleaseJsonContext`, the shape
`SettingsJsonContext` and `ProfileJsonContext` already use. `EnsureSuccessStatusCode` throws `HttpRequestException`
for a non-2xx response (a private repo returning 404, say); a malformed body throws `JsonException`; a timeout
throws `TaskCanceledException`. None of these are caught here — the App layer turns them into the `Failed`
outcome (§4).

**Version comparison is a pure function, not a method on the checker**, so it is testable with plain strings and
never touches `AppVersion`, which is an App type:

```csharp
namespace LizTerm.Core.Updates;

public static class ReleaseVersion
{
    /// <summary>True when latest is a strictly higher three-part version than current. Throws FormatException
    /// if either string does not parse as Major.Minor.Patch.</summary>
    public static bool IsNewer(string latest, string current);
}
```

**Rate limits.** GitHub's unauthenticated limit is 60 requests/hour per IP. One check per launch, with no
periodic re-check, stays far under it for any single machine; no token, no authentication.

## 4. The App-side result and the decision to show it

```csharp
namespace LizTerm.App.Updates;

public abstract record UpdateCheckResult
{
    public sealed record UpToDate : UpdateCheckResult;
    public sealed record NewerAvailable(string Version, string HtmlUrl) : UpdateCheckResult;
    public sealed record Failed(string Reason) : UpdateCheckResult;
}
```

The `StartupPlan.ShowError` / `OpenSession` shape already in `Startup/` — one closed set of outcomes, matched
with a switch, rather than a nullable `ReleaseInfo` and a separate error string.

```csharp
public static class UpdateChecker
{
    public static async Task<UpdateCheckResult> CheckAsync(
        IReleaseChecker checker, string currentVersion, CancellationToken cancellationToken)
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
}
```

`FriendlyReason` turns a timeout into "The request timed out" and anything else into a short, non-technical
line; it never surfaces an exception's raw message to the user. This mirrors `OpenLinkAsync`'s catch-all
"fall through and say so" shape rather than inventing a new error-reporting idiom.

**Whether an automatic check is allowed to interrupt the user** is a second pure function, separate from the
network call, so the suppression rule is testable with no fakes at all:

```csharp
public static class UpdateNotificationPolicy
{
    /// <summary>True only for a genuinely newer release the user has not asked to skip. A manual check ignores
    /// this and always shows its result — see App.CheckForUpdatesManuallyAsync.</summary>
    public static bool ShouldShowAutomatically(UpdateCheckResult result, string? skippedVersion) =>
        result is UpdateCheckResult.NewerAvailable newer && newer.Version != skippedVersion;
}
```

## 5. Settings

`AppSettings` (`src/LizTerm.Core/Settings/AppSettings.cs`) gains two parameters, appended after `KeypadPfKeys`
with the record's existing flat, all-defaulted, positional shape:

```csharp
bool CheckForUpdatesAutomatically = true,
string? SkippedUpdateVersion = null);
```

`SettingsViewModel` gains `CheckForUpdatesAutomatically`, the three-line bindable-property shape every other
bool follows. `SkippedUpdateVersion` gets the same shape but is not bound to any Preferences control — it is
app-managed state, written only from the Skip button's callback (§6):

```csharp
public string? SkippedUpdateVersion
{
    get => Current.SkippedUpdateVersion;
    set
    {
        if (Current.SkippedUpdateVersion != value) Apply(nameof(SkippedUpdateVersion), s => s with { SkippedUpdateVersion = value });
    }
}
```

Storing the exact version string, rather than a bool or a timestamp, is what makes Skip per-version for free:
0.6.0 skipped and 0.6.1 later published compares `"0.6.1" != "0.6.0"` and is shown, with no expiry logic and no
extra field.

## 6. The App-side coordinator and the result window

`App` gets one more constructed-once field, beside `_bellRinger`:

```csharp
private readonly IReleaseChecker _releaseChecker = GitHubReleaseChecker.Create();
private UpdateCheckWindow? _updateCheck;
```

**Startup.** `Execute(StartupPlan plan)` fires the check only from the two branches that actually open a window
— `OpenSession` and the default `ShowPicker` — never from `ShowError`, and never when opening either one threw
and the catch block fell back to `StartupErrorWindow` instead:

```csharp
case StartupPlan.OpenSession open:
    OpenSession(open.Profile, open.FromStore);
    _ = CheckForUpdatesOnStartupAsync(_releaseChecker);
    break;
default:
    ShowPicker();
    _ = CheckForUpdatesOnStartupAsync(_releaseChecker);
    break;
```

Fire-and-forget: a launch must never wait on a network call, successful or not.

```csharp
internal async Task CheckForUpdatesOnStartupAsync(IReleaseChecker checker)
{
    if (!Settings.Current.CheckForUpdatesAutomatically) return;
    try
    {
        var result = await UpdateChecker.CheckAsync(checker, AppVersion.Current, CancellationToken.None);
        if (UpdateNotificationPolicy.ShouldShowAutomatically(result, Settings.Current.SkippedUpdateVersion))
            await ShowUpdateCheckResultAsync(result, owner: null);
    }
    catch { /* an unattended check is not worth a crash */ }
}
```

Taking `IReleaseChecker` as a parameter (rather than reading `_releaseChecker` directly) is the test seam —
`ShowPreferences(SettingsViewModel settings)`'s shape — so a test drives the whole startup path with a
`FakeReleaseChecker` and never touches the network.

**Manual.** One method, called from the Help menu (§7) with the requesting window as owner, that always shows a
result:

```csharp
public async Task CheckForUpdatesManuallyAsync(Window owner)
{
    var result = await UpdateChecker.CheckAsync(_releaseChecker, AppVersion.Current, CancellationToken.None);
    await ShowUpdateCheckResultAsync(result, owner);
}
```

**The window**, one at a time like `_about` and `_preferences`:

```csharp
private async Task ShowUpdateCheckResultAsync(UpdateCheckResult result, Window? owner)
{
    if (_updateCheck is { } showing) { showing.Activate(); return; }
    var window = new UpdateCheckWindow(result, AppVersion.Current, OnDownloadAsync, OnSkip);
    _updateCheck = window;
    window.Closed += (_, _) => { if (ReferenceEquals(_updateCheck, window)) _updateCheck = null; };
    var target = owner ?? ActiveWindow();
    if (target is null) window.Show(); else await window.ShowDialog(target);
}

private Task<bool> OnDownloadAsync(string url) => new AvaloniaUriOpener(_updateCheck!).OpenAsync(new Uri(url));
private void OnSkip(string version) => Settings.SkippedUpdateVersion = version;
```

`AvaloniaUriOpener` needs a `TopLevel`; the window being shown is one, the same relationship `SessionWindow`
has with its own `AvaloniaUriOpener(window)` — there is no app-wide instance because the opener is tied to
whichever window it is asked to act through.

`UpdateCheckWindow` (`Views/`) is sized and centred the way `AboutWindow` is (fixed width, `SizeToContent="Height"`,
`WindowStartupLocation="CenterOwner"`), and its content depends on the result it was built with:

| Result | Text | Buttons |
|---|---|---|
| `NewerAvailable` | "LizTerm `{Version}` is available. You have `{currentVersion}`." | Download, Remind Me Later, Skip This Version |
| `UpToDate` | "You're up to date (`{currentVersion}`)." | OK |
| `Failed` | "Couldn't check for updates: `{Reason}`" | OK |

Download calls `onDownload(url)`; on `true` the window closes, on `false` (the platform could not open a
browser) the window stays open and a line under the buttons names the URL instead — `OpenLinkAsync`'s own
fallback wording, "The page is at ...". Remind Me Later and OK just close the window; neither touches settings.
Skip calls `onSkip(result.Version)` and then closes. This is the one window in the app whose content varies by
constructor argument rather than by a bound view model, which fits its size: three fixed states, no live data,
no reason to add a `ViewModels/` class for it.

## 7. The Help menu

The item goes in the Help menu, after Releases and before the Wire Log separator, in both the `NativeMenu` and
classic `Menu` copies of `SessionWindow.axaml`:

```xml
<NativeMenuItem Header="R_eleases" ... />
<NativeMenuItem Header="_Check for Updates..." Click="OnCheckForUpdatesClickNative" />
<NativeMenuItemSeparator />
```

and the classic mirror with `Click="OnCheckForUpdatesClick"`. **This follows About and Preferences' wiring, not
`OpenLinkCommand`'s** — it needs the requesting window as the dialog's owner, which only the code-behind has:

```csharp
private async void OnCheckForUpdatesClickNative(object? sender, EventArgs e) => await CheckForUpdatesAsync();
private async void OnCheckForUpdatesClick(object? sender, RoutedEventArgs e) => await CheckForUpdatesAsync();

private async Task CheckForUpdatesAsync()
{
    if (Avalonia.Application.Current is App app) await app.CheckForUpdatesManuallyAsync(this);
}
```

The existing native-item wiring rules apply unchanged: a `Click` handler alone satisfies the "every native item
needs a Command or a Click" rule, and `NativeMenuTests.Every_native_item_can_actually_be_activated` covers it
along with every other item, with no test written for it specifically.

## 8. The Preferences window's new General tab

A fourth tab, **General**, added first — ahead of Display, Bell and Window — since it is where an item that is
not about the screen, the bell or the window's own chrome belongs, and Robert has a second such item planned for
it already. The existing three tabs, their order among themselves, and the window's fixed size (set by the
tallest tab, currently Window) do not change.

```xml
<TabItem Header="General">
  <StackPanel Spacing="12">
    <Grid Classes="settings" ColumnDefinitions="120,*">
      <TextBlock Text="Updates" />
      <CheckBox Grid.Column="1" Content="Automatically check for updates on launch"
                IsChecked="{Binding CheckForUpdatesAutomatically, Mode=TwoWay}" />
    </Grid>
  </StackPanel>
</TabItem>
```

One row today; the next item Robert mentioned joins this tab rather than opening a debate about where it goes.

## 9. Testing and verification

### 9.1 Core (`tests/LizTerm.Core.Tests/Updates/`)

- `GitHubReleaseCheckerTests`, against a fake `HttpMessageHandler`: requests the exact URL and the three headers;
  parses `tag_name`/`html_url` and strips the leading `v`; a 404 throws `HttpRequestException`; a body that is
  not the expected shape throws `JsonException`.
- `ReleaseVersionTests`: equal, older, newer, and each malformed-input case throwing `FormatException`.

### 9.2 App

- `tests/LizTerm.App.Tests/Fakes/FakeReleaseChecker.cs`: returns a canned `ReleaseInfo` or throws whatever the
  test hands it, the `FakeUriOpener`/`FakeBellRinger` shape.
- `tests/LizTerm.App.Tests/Updates/UpdateCheckerTests.cs`: `NewerAvailable` when the fake's version is higher,
  `UpToDate` when equal, `Failed` (with a friendly reason) when the fake throws each of the four caught exception
  types.
- `tests/LizTerm.App.Tests/Updates/UpdateNotificationPolicyTests.cs`: `NewerAvailable` with no skip shows;
  `NewerAvailable` matching `SkippedUpdateVersion` does not; `NewerAvailable` for a *different* version than the
  one skipped shows; `UpToDate` and `Failed` never show, skip value or not.
- `tests/LizTerm.App.Tests/ViewModels/SettingsViewModelTests.cs`: `CheckForUpdatesAutomatically` writes through
  and skips an unchanged value, like every other bool; `SkippedUpdateVersion` round-trips including back to
  `null`.
- `tests/LizTerm.App.Tests/Views/UpdateCheckWindowTests.cs`: the three result types render the right text and
  button set; Download calls the callback with the result's URL and closes on `true`, stays open and shows the
  URL text on `false`; Skip calls the callback with the result's version and closes; Remind Me Later / OK close
  without calling either callback.
- `tests/LizTerm.App.Tests/Views/PreferencesWindowTests.cs`: the General tab's checkbox drives
  `CheckForUpdatesAutomatically` by click, `PreferencesWindow`'s established recipe.
- `App`'s own startup wiring (`CheckForUpdatesOnStartupAsync`, `CheckForUpdatesManuallyAsync`): exercised with a
  `FakeReleaseChecker` passed to the internal seam — automatic path silent on `Failed`/`UpToDate`/a skipped
  version, shows on a new unskipped version, never runs when `CheckForUpdatesAutomatically` is off; manual path
  always shows, including for `UpToDate` and a previously skipped version.
- `NativeMenuTests`: the parity guard (every native item exists in both menus and can be activated) covers the
  new item with no dedicated test, as it does for every other Help item.

### 9.3 Manual

Run the app with `Directory.Build.props`'s `<Version>` temporarily lowered below the latest published tag (or,
simpler, against a test fork's releases), confirm the startup dialog appears once, Remind Me Later brings it
back on the next launch, Skip suppresses it until the version changes, and Download opens the release page in
the default browser. Run Help > Check for Updates... while already on the latest version and confirm "You're up
to date" appears; run it with networking disabled and confirm the failure message appears and names something a
person can act on.

## 10. What does not change

`SessionProfile`, the profile editor, the picker's own list and Quick Connect. The bell, Find, file transfer,
and every other Help item. The Preferences window's Display, Bell and Window tabs, and their relative order.
`ProjectLinks` — the Releases menu item keeps pointing at the releases index; this feature reads a specific
release's URL from the API response instead.

## 11. Out of scope

- **A periodic re-check while the app stays open.** Decided against in §1; the menu item is the answer for a
  long-lived session.
- **A direct per-platform asset download.** Decided against in §1; Download always opens the release page.
- **Anything beyond a bare newer/not-newer comparison** — no changelog text, no release notes rendered in the
  dialog, no pre-release or beta channel. `/releases/latest` already excludes both, and there is no setting to
  opt into them.
- **A picker-level indicator** (a badge, a row) — the dialog is the whole of the UI.
- **Telemetry of any kind.** The GET carries nothing about the user, the host they connect to, or how LizTerm is
  used; it is one anonymous request for a version number.

## 12. Documentation and bookkeeping

Each fact in its home:

- `docs/user-guide.md`: a short paragraph under "Preferences" for the General tab's checkbox, and a line under
  the Help menu's description for "Check for Updates...".
- `src/LizTerm.App/CLAUDE.md`: the Settings/Preferences section gains a line naming the General tab and the
  update check's shape (`IReleaseChecker`, the startup/manual split, the single-instance dialog); the Menus
  section's Help-item list gains the new entry, wired like About and Preferences rather than `OpenLinkCommand`.
- `src/LizTerm.Core/CLAUDE.md`: a short "Updates" entry beside "Security", naming `IReleaseChecker` and
  `ReleaseVersion` as the BCL-only network code Core holds for the same reason the certificate fetcher does.
- `tests/CLAUDE.md`: `FakeReleaseChecker` joins the list of fakes.

The work tracks as #107 and closes it. The version stays whatever it is in the PR; the bump happens at release
time, as it does for every other feature.
