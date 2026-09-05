# LizTerm Milestone 2, Plan 3a: Polish Bundle Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give LizTerm a connect timeout and cancel, TLS and LU names on the ad hoc command line, a certificate connect-anyway prompt, a Help menu with a live wire log toggle and an About box that names the engine and where it came from, a splash window with a startup engine check, and photosensitive-safe blinking cells.

**Architecture:** Core gains `ConnectOptions`, `EngineInfo`/`EngineSource`, a `CertificateVerificationFailed` flag on `ConnectionFailedException`, wire-log members and an `Engine` property on `IEmulatorSession`, and an `AppPaths` class for the per-OS config directories. The b3270 backend makes `ConnectAsync` cancellable (one `Disconnect` run), recognises the certificate-failure text, reports the engine's version from the hello, and turns its wire log into a swappable field. The app adds `ICertificatePrompt` and `IFolderOpener` seams (Avalonia implementations, test fakes), a timeout and cancel policy plus the prompt flow and wire log toggle in `SessionViewModel`, four small windows (certificate, about, splash, startup error), pure `SplashTiming` and `StartupPlan` classes that decide startup, x3270 host syntax in `StartupArguments`, and a blink timer in `TerminalScreen`.

**Tech Stack:** .NET 10, Avalonia 12.1.2 (`Window.ShowDialog<T>`, `IsDefault`/`IsCancel` buttons, `MenuItem.ToggleType`, `TopLevel.Launcher.LaunchDirectoryInfoAsync`, `DispatcherTimer`, `SystemDecorations`, `AssetLoader`), CommunityToolkit.Mvvm 8.4.2 (`[ObservableProperty]` with `On<Name>Changed` partials, `[RelayCommand]`), xunit.v3 3.2.2 in VSTest mode, Avalonia.Headless.XUnit 12.1.2, b3270 4.5ga6.

**Spec:** `docs/superpowers/specs/2026-09-05-lizterm-m2-polish-design.md` (parent: `docs/superpowers/specs/2026-09-03-lizterm-v1-design.md` sections 5.5, 6.1, 6.2, 6.3, 6.8, 7)

## Global Constraints

- `LizTerm.Core` references only the BCL and never mentions Avalonia or b3270 names. `LizTerm.App` names `LizTerm.Backend.B3270` only in `src/LizTerm.App/SessionFactory.cs`. Only the backend knows the string `"TLS: Host certificate verification failed"` and the `Disconnect` action.
- No new packages. Avalonia stays at `12.1.2`, CommunityToolkit.Mvvm at `8.4.2`; versions live only in `Directory.Packages.props`. `Directory.Build.props` gains exactly one property, `<Version>0.3.0</Version>`.
- Contract of `ConnectAsync(ConnectOptions? options = null, CancellationToken cancellationToken = default)` (spec sections 3 and 4.1): a cancelled token ends the attempt with `OperationCanceledException` and leaves the session disconnected and reusable; `options.VerifyCertificate` null means the profile's value; `ConnectionFailedException.CertificateVerificationFailed` is true when any run-result text line starts with `"TLS: Host certificate verification failed"`.
- Wire log contract (spec section 3): `WireLogPath` null when inactive; `StartWireLog(path)` throws `IOException` when the file cannot be opened and `InvalidOperationException` ("A wire log is already active.") when one is active; `StopWireLog()` is a no-op when none is active; a log outlives an engine restart and is closed by `DisposeAsync`.
- Blink: `TerminalScreen.BlinkInterval` is 750 ms and must never go below 500 ms (WCAG 2.3.1, spec section 8). Blinking cells hide their text and underline in the hidden phase and keep their background. The cursor never blinks.
- Splash: minimum 1 s, maximum 2.5 s, 480×300, borderless, centered, black. Nothing else opens until it has closed.
- User-visible strings are asserted exactly: `"Connection to {host}:{port} timed out after {N} seconds."`, `" The host may require TLS. Enable it in the profile."`, `"● wire log"`, `"Could not open the wire log: "` + message, `"Could not open the logs folder. Wire logs are in {dir}."`, `"A wire log is already active."`, `"Turn on Help > Wire Log and reproduce to capture a log."`, `"Certificate not verified"`, `"{host} presented a certificate that could not be verified:"`, `"Always allow for this profile"`, `"Connect Anyway"`, `"Cancel"`, `"Version {version}"`, `"{name} {version}, bundled"`, `"{name} {version}, from {origin}"`, `"{name}, not started, bundled"`, `"Usage: LizTerm [profile | [L:][Y:][lu@]host[:port] | [L:][Y:][lu@][ipv6][:port]]"`.
- Tests: `dotnet test <project> --filter "FullyQualifiedName~<Class>"` for one class, `dotnet test LizTerm.slnx` for the suite before every commit. If a `LizTerm.Backend.B3270.Tests` test fails once under load (`Oia_lock_maps_to_keyboard_lock` is the known flake), rerun before investigating. The integration project reports skipped tests without `LIZTERM_TEST_HOST`; that is expected.
- Work only inside this worktree: `/Users/robert/ClaudeSandbox/LizTerm/.claude/worktrees/test-avalonia-mcp-a35e7b` (branch `claude/project-status-next-234e6b`). Never `cd` to the main checkout. Commit after every task with the message shown; end every commit message with `Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>`.
- This worktree has no `native/out`, so anything that spawns b3270 needs `LIZTERM_B3270_PATH=/opt/homebrew/bin/b3270`. The live test gateway is `129.212.188.194:4270` (TLS, self-signed certificate); its address is replaced by `gateway.test` in any committed fixture.
- Never mutate a static from a test: `SessionViewModel.ConnectTimeout` is an instance property (default 30 s) precisely so parallel test classes cannot race on it.

---

## File Structure

```
src/LizTerm.Core/Session/ConnectOptions.cs                 NEW  ConnectOptions record
src/LizTerm.Core/Session/EngineInfo.cs                     NEW  EngineSource enum, EngineInfo record
src/LizTerm.Core/Session/Exceptions.cs                     MOD  CertificateVerificationFailed on ConnectionFailedException
src/LizTerm.Core/Session/IEmulatorSession.cs               MOD  ConnectAsync(options, ct), Engine, WireLogPath, StartWireLog, StopWireLog
src/LizTerm.Core/Profiles/AppPaths.cs                      NEW  ConfigRoot, ProfilesDirectory, LogsDirectory
src/LizTerm.Core/Profiles/ProfileStore.cs                  MOD  DefaultDirectory delegates to AppPaths
src/LizTerm.Backend.B3270/Process/B3270Locator.cs          MOD  returns B3270Location(Path, Source)
src/LizTerm.Backend.B3270/WireLog.cs                       MOD  Path, TryFromEnvironment(out error), no static
src/LizTerm.Backend.B3270/B3270Session.cs                  MOD  location ctor arg, Engine, wire log field, cancellable connect, cert flag
src/LizTerm.App/SessionFactory.cs                          MOD  location-aware Create, CheckBackend, OverrideOrigin
src/LizTerm.App/AppVersion.cs                              NEW  informational version without the +hash
src/LizTerm.App/Startup/StartupArguments.cs                MOD  L:, Y:, lu@, Error, usage
src/LizTerm.App/Startup/SplashTiming.cs                    NEW  pure close-time arithmetic
src/LizTerm.App/Startup/StartupPlan.cs                     NEW  ShowError | OpenSession | OpenPicker
src/LizTerm.App/Status/StatusFormatter.cs                  MOD  ConnectTimeout, WireLog, Engine, Fault advice
src/LizTerm.App/Dialogs/ICertificatePrompt.cs              NEW  seam + CertificateDecision
src/LizTerm.App/Dialogs/AvaloniaCertificatePrompt.cs       NEW  opens CertificateWindow modally
src/LizTerm.App/Files/IFolderOpener.cs                     NEW  seam
src/LizTerm.App/Files/AvaloniaFolderOpener.cs              NEW  TopLevel.Launcher
src/LizTerm.App/ViewModels/SessionViewModel.cs             MOD  timeout/cancel, prompt flow, wire log toggle, Engine
src/LizTerm.App/Views/CertificateWindow.axaml(.cs)         NEW
src/LizTerm.App/Views/AboutWindow.axaml(.cs)               NEW
src/LizTerm.App/Views/SplashWindow.axaml(.cs)              NEW
src/LizTerm.App/Views/StartupErrorWindow.axaml(.cs)        NEW
src/LizTerm.App/Views/SessionWindow.axaml(.cs)             MOD  Help menu, status bar column, About handler
src/LizTerm.App/Controls/TerminalScreen.cs                 MOD  blink timer
src/LizTerm.App/App.axaml.cs                               MOD  splash-first startup, OpenSession(profile, fromStore)
src/LizTerm.App/LizTerm.App.csproj                         MOD  embed THIRD-PARTY-NOTICES.txt
THIRD-PARTY-NOTICES.txt                                    NEW  x3270 + 3270font notices
Directory.Build.props                                      MOD  Version 0.3.0
tests/LizTerm.Core.Tests/Session/ConnectTypesTests.cs      NEW
tests/LizTerm.Core.Tests/Profiles/AppPathsTests.cs         NEW
tests/LizTerm.Backend.B3270.Tests/Process/B3270LocatorTests.cs   MOD
tests/LizTerm.Backend.B3270.Tests/WireLogTests.cs          MOD
tests/LizTerm.Backend.B3270.Tests/B3270SessionWireLogTests.cs    NEW
tests/LizTerm.Backend.B3270.Tests/B3270SessionConnectTests.cs    NEW
tests/LizTerm.Backend.B3270.Tests/B3270SessionLifecycleTests.cs  MOD  Engine
tests/LizTerm.Backend.B3270.Tests/ReplayTests.cs           MOD  certificate failure replay
tests/LizTerm.Backend.B3270.Tests/Fixtures/gateway-cert-failure.jsonl  NEW
tests/LizTerm.Backend.B3270.Tests/Fixtures/README.md       MOD
tests/LizTerm.App.Tests/Fakes/FakeEmulatorSession.cs       MOD
tests/LizTerm.App.Tests/Fakes/FakeCertificatePrompt.cs     NEW
tests/LizTerm.App.Tests/Fakes/FakeFolderOpener.cs          NEW
tests/LizTerm.App.Tests/Startup/StartupArgumentsTests.cs   MOD
tests/LizTerm.App.Tests/Startup/SplashTimingTests.cs       NEW
tests/LizTerm.App.Tests/Startup/StartupPlanTests.cs        NEW
tests/LizTerm.App.Tests/Status/StatusFormatterTests.cs     MOD
tests/LizTerm.App.Tests/ViewModels/SessionViewModelConnectTests.cs   NEW
tests/LizTerm.App.Tests/ViewModels/SessionViewModelWireLogTests.cs   NEW
tests/LizTerm.App.Tests/Views/CertificateWindowTests.cs    NEW
tests/LizTerm.App.Tests/Views/AboutWindowTests.cs          NEW
tests/LizTerm.App.Tests/Views/SplashWindowTests.cs         NEW
tests/LizTerm.App.Tests/Views/SessionWindowTests.cs        MOD  Help menu present
tests/LizTerm.App.Tests/Controls/TerminalScreenBlinkTests.cs NEW
tests/LizTerm.App.Tests/AppVersionTests.cs                 NEW
tests/LizTerm.Integration.Tests/LiveHostTests.cs           MOD  cert flag, timeout
CLAUDE.md, docs/superpowers/specs/2026-09-05-lizterm-m2-polish-design.md   MOD  as-built
```

---

### Task 1: Core types and AppPaths

**Files:**
- Create: `src/LizTerm.Core/Session/ConnectOptions.cs`, `src/LizTerm.Core/Session/EngineInfo.cs`, `src/LizTerm.Core/Profiles/AppPaths.cs`
- Modify: `src/LizTerm.Core/Session/Exceptions.cs`, `src/LizTerm.Core/Profiles/ProfileStore.cs:11-22`
- Test: `tests/LizTerm.Core.Tests/Session/ConnectTypesTests.cs`, `tests/LizTerm.Core.Tests/Profiles/AppPathsTests.cs`

**Interfaces:**
- Produces: `ConnectOptions(bool? VerifyCertificate = null)`; `enum EngineSource { Bundled, Override }`; `EngineInfo(string Name, string? Version, string Path, EngineSource Source)`; `ConnectionFailedException(IReadOnlyList<string> lines, bool certificateVerificationFailed = false)` with `CertificateVerificationFailed`; `AppPaths.ConfigRoot()`, `AppPaths.ProfilesDirectory()`, `AppPaths.LogsDirectory()`.

- [ ] **Step 1: Write the failing tests**

`tests/LizTerm.Core.Tests/Session/ConnectTypesTests.cs`:

```csharp
using LizTerm.Core.Session;

namespace LizTerm.Core.Tests.Session;

public class ConnectTypesTests
{
    [Fact]
    public void ConnectOptions_defaults_to_the_profile()
    {
        Assert.Null(new ConnectOptions().VerifyCertificate);
        Assert.False(new ConnectOptions(VerifyCertificate: false).VerifyCertificate);
    }

    [Fact]
    public void ConnectionFailedException_carries_the_certificate_flag()
    {
        var plain = new ConnectionFailedException(["Connection failed:", "refused"]);
        Assert.False(plain.CertificateVerificationFailed);
        Assert.Equal("Connection failed: refused", plain.Message);

        var cert = new ConnectionFailedException(["Connection failed:", "TLS: Host certificate verification failed:", "self-signed certificate (18)"], certificateVerificationFailed: true);
        Assert.True(cert.CertificateVerificationFailed);
        Assert.Equal(3, cert.Lines.Count);
    }

    [Fact]
    public void EngineInfo_is_a_value()
    {
        var a = new EngineInfo("b3270", null, "/x/b3270", EngineSource.Bundled);
        Assert.Equal(a, a with { });
        Assert.Equal("4.5.6", (a with { Version = "4.5.6" }).Version);
    }
}
```

`tests/LizTerm.Core.Tests/Profiles/AppPathsTests.cs`:

```csharp
using LizTerm.Core.Profiles;

namespace LizTerm.Core.Tests.Profiles;

public class AppPathsTests
{
    [Fact]
    public void Profiles_and_logs_are_siblings_under_the_config_root()
    {
        var root = AppPaths.ConfigRoot();
        Assert.Equal("LizTerm", Path.GetFileName(root));
        Assert.Equal(Path.Combine(root, "profiles"), AppPaths.ProfilesDirectory());
        Assert.Equal(Path.Combine(root, "logs"), AppPaths.LogsDirectory());
        Assert.Equal(AppPaths.ProfilesDirectory(), ProfileStore.DefaultDirectory());
    }

    [Fact]
    public void Root_follows_the_host_os_convention()
    {
        var root = AppPaths.ConfigRoot();
        if (OperatingSystem.IsMacOS())
            Assert.EndsWith(Path.Combine("Library", "Application Support", "LizTerm"), root);
        else if (OperatingSystem.IsWindows())
            Assert.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), root);
        else
            Assert.True(root.EndsWith(Path.Combine(".config", "LizTerm")) || root.StartsWith(Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") ?? "\0"), root);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~ConnectTypesTests|FullyQualifiedName~AppPathsTests"`
Expected: build errors naming `ConnectOptions`, `EngineInfo`, `AppPaths`, and the missing constructor parameter.

- [ ] **Step 3: Write the implementation**

`src/LizTerm.Core/Session/ConnectOptions.cs`:

```csharp
namespace LizTerm.Core.Session;

/// <summary>One-shot choices for a single connect attempt. A null field means "as the profile says".</summary>
public sealed record ConnectOptions(bool? VerifyCertificate = null);
```

`src/LizTerm.Core/Session/EngineInfo.cs`:

```csharp
namespace LizTerm.Core.Session;

/// <summary>Where the emulator engine binary came from: shipped inside the app (a runtimes folder or an app
/// bundle), or pointed to from outside it (today an environment variable; a preference later).</summary>
public enum EngineSource
{
    Bundled,
    Override,
}

/// <summary>The emulator engine binary a session uses. <see cref="Version"/> is null until the engine has started.</summary>
public sealed record EngineInfo(string Name, string? Version, string Path, EngineSource Source);
```

Replace the `ConnectionFailedException` class in `src/LizTerm.Core/Session/Exceptions.cs`:

```csharp
/// <summary>The host connection could not be established; Lines is the emulator's explanation.
/// <see cref="CertificateVerificationFailed"/> is true when the only obstacle was an unverifiable host
/// certificate, so the caller can offer to connect without verifying.</summary>
public sealed class ConnectionFailedException(IReadOnlyList<string> lines, bool certificateVerificationFailed = false)
    : Exception(string.Join(" ", lines))
{
    public IReadOnlyList<string> Lines { get; } = lines;
    public bool CertificateVerificationFailed { get; } = certificateVerificationFailed;
}
```

`src/LizTerm.Core/Profiles/AppPaths.cs`:

```csharp
namespace LizTerm.Core.Profiles;

/// <summary>Per-OS locations of LizTerm's own files: profiles and wire logs live side by side under one root.</summary>
public static class AppPaths
{
    public static string ConfigRoot()
    {
        string root;
        if (OperatingSystem.IsMacOS())
            root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support");
        else if (OperatingSystem.IsWindows())
            root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        else
            root = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME")
                   ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        return Path.Combine(root, "LizTerm");
    }

    public static string ProfilesDirectory() => Path.Combine(ConfigRoot(), "profiles");

    public static string LogsDirectory() => Path.Combine(ConfigRoot(), "logs");
}
```

Replace `ProfileStore.DefaultDirectory` (lines 11-22) with:

```csharp
    public static string DefaultDirectory() => AppPaths.ProfilesDirectory();
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/LizTerm.Core.Tests`
Expected: all pass (the two new classes plus the existing Core tests).

- [ ] **Step 5: Commit**

```bash
git add src/LizTerm.Core tests/LizTerm.Core.Tests
git commit -m "Add ConnectOptions, EngineInfo, the certificate flag, and AppPaths to Core

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 2: B3270Locator reports where the binary came from

**Files:**
- Modify: `src/LizTerm.Backend.B3270/Process/B3270Locator.cs`, `src/LizTerm.App/SessionFactory.cs`, `tests/LizTerm.Integration.Tests/LiveHostTests.cs` (two `B3270Locator.Find()` call sites)
- Test: `tests/LizTerm.Backend.B3270.Tests/Process/B3270LocatorTests.cs`

**Interfaces:**
- Consumes: `EngineSource` (Task 1).
- Produces: `public sealed record B3270Location(string Path, EngineSource Source)` with `static readonly B3270Location Unknown = new("", EngineSource.Bundled)`; `B3270Locator.Find()` and `Find(string? overridePath, string baseDirectory)` return `B3270Location`.

- [ ] **Step 1: Update the tests**

In `tests/LizTerm.Backend.B3270.Tests/Process/B3270LocatorTests.cs` change the three positive tests:

```csharp
    [Fact]
    public void Override_path_wins_and_is_reported_as_override()
    {
        var path = MakeExecutable("custom/b3270");
        Assert.Equal(new B3270Location(path, EngineSource.Override), B3270Locator.Find(path, _dir));
    }

    [Fact]
    public void Finds_runtime_native_folder_as_bundled()
    {
        var rid = System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier;
        var path = MakeExecutable(Path.Combine("runtimes", rid, "native", B3270Locator.FileName));
        Assert.Equal(new B3270Location(path, EngineSource.Bundled), B3270Locator.Find(null, _dir));
    }

    [Fact]
    public void Falls_back_to_base_directory_as_bundled()
    {
        var path = MakeExecutable(B3270Locator.FileName);
        Assert.Equal(new B3270Location(path, EngineSource.Bundled), B3270Locator.Find(null, _dir));
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/LizTerm.Backend.B3270.Tests --filter "FullyQualifiedName~B3270LocatorTests"`
Expected: build error, `B3270Location` not found.

- [ ] **Step 3: Write the implementation**

Replace `src/LizTerm.Backend.B3270/Process/B3270Locator.cs`:

```csharp
using System.Runtime.InteropServices;
using LizTerm.Core.Session;

namespace LizTerm.Backend.B3270.Process;

/// <summary>A resolved b3270 binary. Anything shipped inside the app (the runtimes folder or a binary beside
/// the executable, which is also what an app bundle's contents will be) is Bundled; the environment variable is
/// an Override.</summary>
public sealed record B3270Location(string Path, EngineSource Source)
{
    /// <summary>For tests that never spawn a real process.</summary>
    public static readonly B3270Location Unknown = new("", EngineSource.Bundled);
}

public static class B3270Locator
{
    public const string EnvironmentOverride = "LIZTERM_B3270_PATH";

    public static string FileName => OperatingSystem.IsWindows() ? "b3270.exe" : "b3270";

    public static B3270Location Find() =>
        Find(Environment.GetEnvironmentVariable(EnvironmentOverride), AppContext.BaseDirectory);

    public static B3270Location Find(string? overridePath, string baseDirectory)
    {
        var candidates = new List<B3270Location>();
        if (!string.IsNullOrWhiteSpace(overridePath)) candidates.Add(new B3270Location(overridePath, EngineSource.Override));
        candidates.Add(new B3270Location(Path.Combine(baseDirectory, "runtimes", RuntimeInformation.RuntimeIdentifier, "native", FileName), EngineSource.Bundled));
        candidates.Add(new B3270Location(Path.Combine(baseDirectory, FileName), EngineSource.Bundled));

        foreach (var candidate in candidates)
        {
            if (!File.Exists(candidate.Path)) continue;
            if (!OperatingSystem.IsWindows())
            {
                var mode = File.GetUnixFileMode(candidate.Path);
                if ((mode & UnixFileMode.UserExecute) == 0)
                    throw new BackendUnavailableException(
                        $"The emulator engine at {candidate.Path} is not executable. Run: chmod +x \"{candidate.Path}\"");
            }
            return candidate;
        }

        throw new BackendUnavailableException(
            "The emulator engine (b3270) was not found. Looked in:\n  " + string.Join("\n  ", candidates.Select(c => c.Path)) +
            $"\nSet {EnvironmentOverride} to a b3270 executable to override.");
    }
}
```

In `src/LizTerm.App/SessionFactory.cs` change `Create` to `new B3270ChildProcess(B3270Locator.Find().Path)` (the full rewrite comes in Task 3). In `tests/LizTerm.Integration.Tests/LiveHostTests.cs` change both `new B3270ChildProcess(B3270Locator.Find())` to `new B3270ChildProcess(B3270Locator.Find().Path)`.

- [ ] **Step 4: Run the suite**

Run: `dotnet test LizTerm.slnx`
Expected: all pass, integration tests skipped.

- [ ] **Step 5: Commit**

```bash
git add src tests
git commit -m "Make B3270Locator report whether the binary is bundled or an override

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 3: Wire log as a session capability

**Files:**
- Modify: `src/LizTerm.Backend.B3270/WireLog.cs`, `src/LizTerm.Backend.B3270/B3270Session.cs` (constructor, `StartProcessAsync` warning, `ReadLoop`, `WriteLine`, `DisposeAsync`), `src/LizTerm.Core/Session/IEmulatorSession.cs`, `src/LizTerm.App/SessionFactory.cs`, `tests/LizTerm.App.Tests/Fakes/FakeEmulatorSession.cs`, `tests/LizTerm.Integration.Tests/LiveHostTests.cs` (two `WireLog.FromEnvironment()` sites)
- Test: `tests/LizTerm.Backend.B3270.Tests/WireLogTests.cs`, `tests/LizTerm.Backend.B3270.Tests/B3270SessionWireLogTests.cs`

**Interfaces:**
- Consumes: `B3270Location` (Task 2).
- Produces: `WireLog(TextWriter writer, string? path = null)`, `WireLog(string path)` (opens for append; throws `IOException` or `UnauthorizedAccessException`), `WireLog.Path`, `static WireLog? TryFromEnvironment(out string? error)`; `B3270Session(SessionProfile profile, Func<IB3270Process> processFactory, WireLog? wireLog = null, string? wireLogError = null, B3270Location? location = null)`; on `IEmulatorSession`: `string? WireLogPath { get; }`, `void StartWireLog(string path)`, `void StopWireLog()`. `FakeEmulatorSession` gains `WireLogPath` (settable), `WireLogException`, and records `wirelog:start:<path>` / `wirelog:stop`.

- [ ] **Step 1: Write the failing tests**

Replace `tests/LizTerm.Backend.B3270.Tests/WireLogTests.cs`:

```csharp
namespace LizTerm.Backend.B3270.Tests;

public class WireLogTests
{
    [Fact]
    public void TryFromEnvironment_returns_null_and_the_error_for_an_unwritable_path()
    {
        var original = Environment.GetEnvironmentVariable(WireLog.EnvironmentVariable);
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString(), "wire.log");
        try
        {
            Environment.SetEnvironmentVariable(WireLog.EnvironmentVariable, path);
            var log = WireLog.TryFromEnvironment(out var error);
            Assert.Null(log);
            Assert.NotNull(error);
        }
        finally
        {
            Environment.SetEnvironmentVariable(WireLog.EnvironmentVariable, original);
        }
    }

    [Fact]
    public void TryFromEnvironment_returns_null_without_error_when_unset()
    {
        var original = Environment.GetEnvironmentVariable(WireLog.EnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(WireLog.EnvironmentVariable, null);
            Assert.Null(WireLog.TryFromEnvironment(out var error));
            Assert.Null(error);
        }
        finally
        {
            Environment.SetEnvironmentVariable(WireLog.EnvironmentVariable, original);
        }
    }

    [Fact]
    public void Path_constructor_appends_both_directions_with_prefixes()
    {
        var path = Path.Combine(Path.GetTempPath(), "lizterm-wire-" + Guid.NewGuid().ToString("N") + ".log");
        try
        {
            using (var log = new WireLog(path))
            {
                Assert.Equal(path, log.Path);
                log.Outbound("{\"run\":1}");
                log.Inbound("{\"hello\":1}");
            }
            var lines = File.ReadAllLines(path);
            Assert.Equal(2, lines.Length);
            Assert.EndsWith(" > {\"run\":1}", lines[0]);
            Assert.EndsWith(" < {\"hello\":1}", lines[1]);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
```

Create `tests/LizTerm.Backend.B3270.Tests/B3270SessionWireLogTests.cs`:

```csharp
using LizTerm.Backend.B3270.Tests.Fakes;
using LizTerm.Core.Session;

namespace LizTerm.Backend.B3270.Tests;

public class B3270SessionWireLogTests : IDisposable
{
    private static readonly SessionProfile Profile = new() { Name = "t", Host = "h", Port = 23 };
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lizterm-wl-" + Guid.NewGuid().ToString("N"));

    public B3270SessionWireLogTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string LogPath(string name = "wire.log") => Path.Combine(_dir, name);

    [Fact]
    public async Task Start_logs_both_directions_and_stop_closes_the_file()
    {
        var fake = new FakeB3270Process();
        await using var session = new B3270Session(Profile, () => fake);
        await session.StartProcessAsync(CancellationToken.None);
        Assert.Null(session.WireLogPath);

        session.StartWireLog(LogPath());
        Assert.Equal(LogPath(), session.WireLogPath);
        await session.SendKeyAsync(TerminalKey.Enter);
        fake.Emit("""{"oia":{"field":"insert","value":"true"}}""");
        await WaitUntilAsync(() => session.KeyboardStatus.InsertMode, "insert");
        session.StopWireLog();
        Assert.Null(session.WireLogPath);

        var text = File.ReadAllText(LogPath());
        Assert.Contains(" > {\"run\"", text);
        Assert.Contains(" < {\"oia\"", text);
        session.StopWireLog(); // no-op when none is active
    }

    [Fact]
    public async Task Start_while_active_throws_and_keeps_the_first_log()
    {
        var fake = new FakeB3270Process();
        await using var session = new B3270Session(Profile, () => fake);
        await session.StartProcessAsync(CancellationToken.None);
        session.StartWireLog(LogPath("a.log"));
        var ex = Assert.Throws<InvalidOperationException>(() => session.StartWireLog(LogPath("b.log")));
        Assert.Equal("A wire log is already active.", ex.Message);
        Assert.Equal(LogPath("a.log"), session.WireLogPath);
        Assert.False(File.Exists(LogPath("b.log")));
    }

    [Fact]
    public async Task Start_on_an_unopenable_path_throws_IOException_and_stays_inactive()
    {
        var fake = new FakeB3270Process();
        await using var session = new B3270Session(Profile, () => fake);
        var missingDir = Path.Combine(_dir, "missing", "wire.log");
        Assert.Throws<IOException>(() => session.StartWireLog(missingDir));
        Assert.Null(session.WireLogPath);
    }

    [Fact]
    public async Task Environment_log_is_active_from_construction_and_survives_a_restart()
    {
        var fake = new FakeB3270Process();
        var log = new WireLog(LogPath());
        var session = new B3270Session(Profile, () => fake, log);
        Assert.Equal(LogPath(), session.WireLogPath);
        await session.StartProcessAsync(CancellationToken.None);
        fake.Exit(1);
        await WaitUntilAsync(() => session.ConnectionState == ConnectionState.Disconnected, "fault");
        Assert.Equal(LogPath(), session.WireLogPath);
        await session.DisposeAsync();
        Assert.Null(session.WireLogPath);
    }

    [Fact]
    public async Task Open_error_warning_is_raised_once()
    {
        var first = new FakeB3270Process();
        var second = new FakeB3270Process();
        var processes = new Queue<FakeB3270Process>([first, second]);
        var session = new B3270Session(Profile, () => processes.Dequeue(), wireLogError: "boom");
        var messages = new List<string>();
        session.HostMessage += (_, m) => messages.Add(m);

        await session.StartProcessAsync(CancellationToken.None);
        first.Exit(1);
        await WaitUntilAsync(() => session.ConnectionState == ConnectionState.Disconnected, "first exit");
        await Task.Delay(50, TestContext.Current.CancellationToken);
        await session.StartProcessAsync(CancellationToken.None);

        Assert.Single(messages.Where(m => m == "Wire log disabled: boom"));
    }

    private static async Task WaitUntilAsync(Func<bool> condition, string what)
    {
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("Timed out waiting for " + what);
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }
    }
}
```

(In the last test the short delay lets `OnProcessEnded` clear the process slot before the second `StartProcessAsync`; that second start is what proves the warning does not repeat. Lengthen the delay if the start still sees the old process.)

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/LizTerm.Backend.B3270.Tests --filter "FullyQualifiedName~WireLog"`
Expected: build errors for `TryFromEnvironment`, `Path`, `WireLogPath`, `StartWireLog`, and the `wireLogError` argument.

- [ ] **Step 3: Rewrite WireLog**

Replace `src/LizTerm.Backend.B3270/WireLog.cs`:

```csharp
using System.Globalization;

namespace LizTerm.Backend.B3270;

/// <summary>Records every protocol line in both directions. Used for bug reports and as replay fixtures.</summary>
public sealed class WireLog(TextWriter writer, string? path = null) : IDisposable
{
    public const string EnvironmentVariable = "LIZTERM_WIRE_LOG";
    private readonly object _lock = new();
    private bool _disposed;

    /// <summary>Opens <paramref name="path"/> for appending. Throws <see cref="IOException"/> (a missing
    /// directory included) or <see cref="UnauthorizedAccessException"/> when it cannot.</summary>
    public WireLog(string path) : this(new StreamWriter(path, append: true), path)
    {
    }

    /// <summary>The file being written, when known.</summary>
    public string? Path { get; } = path;

    /// <summary>The log named by <see cref="EnvironmentVariable"/>, or null. <paramref name="error"/> is set only
    /// when the variable names a path that could not be opened, so the caller can say so once instead of
    /// silently running without a log.</summary>
    public static WireLog? TryFromEnvironment(out string? error)
    {
        error = null;
        var path = Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (string.IsNullOrWhiteSpace(path)) return null;
        try
        {
            return new WireLog(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            error = ex.Message;
            return null;
        }
    }

    public void Inbound(string line) => Write('<', line);
    public void Outbound(string line) => Write('>', line);

    private void Write(char direction, string line)
    {
        lock (_lock)
        {
            if (_disposed) return;
            writer.Write(DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            writer.Write(' ');
            writer.Write(direction);
            writer.Write(' ');
            writer.WriteLine(line);
            writer.Flush();
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            writer.Dispose();
        }
    }
}
```

- [ ] **Step 4: Add the interface members**

In `src/LizTerm.Core/Session/IEmulatorSession.cs`, after `KeyboardStatus KeyboardStatus { get; }` add:

```csharp
    /// <summary>Path of the active wire log, or null. Every protocol line in both directions is appended there,
    /// timestamped. The log belongs to the session, not to one engine process, so it survives an engine restart.</summary>
    string? WireLogPath { get; }

    /// <summary>Starts logging to <paramref name="path"/> (appending). Throws <see cref="IOException"/> when the
    /// file cannot be opened and <see cref="InvalidOperationException"/> when a log is already active.</summary>
    void StartWireLog(string path);

    /// <summary>Stops and closes the active log; does nothing when none is active.</summary>
    void StopWireLog();
```

- [ ] **Step 5: Change B3270Session**

Field and constructor: replace `private readonly WireLog? _wireLog;` with `private WireLog? _wireLog;` and add `private readonly string? _wireLogError;` and `private readonly B3270Location _location;`. Replace the constructor:

```csharp
    /// <param name="wireLog">A log already open (from the environment), or null.</param>
    /// <param name="wireLogError">Why the environment's log could not be opened; reported once as a HostMessage.</param>
    /// <param name="location">The binary this session's processes run; null (tests) reads as unknown.</param>
    public B3270Session(SessionProfile profile, Func<IB3270Process> processFactory, WireLog? wireLog = null, string? wireLogError = null, B3270Location? location = null)
    {
        Profile = profile;
        _processFactory = processFactory;
        _wireLog = wireLog;
        _wireLogError = wireLogError;
        _location = location ?? B3270Location.Unknown;
        CurrentScreen = _buffer.Snapshot();
    }
```

In `StartProcessAsync`, replace the warning block with:

```csharp
        if (_wireLogError is { } wireLogError && !_wireLogWarningRaised)
        {
            _wireLogWarningRaised = true;
            HostMessage?.Invoke(this, "Wire log disabled: " + wireLogError);
        }
```

In `ReadLoop`, replace `_wireLog?.Inbound(line);` with `Volatile.Read(ref _wireLog)?.Inbound(line);`. In `WriteLine`, move the `_wireLog?.Outbound(line);` line inside the `lock (_writeLock)` block (after `Flush()`), so a swap and a write never interleave. In `DisposeAsync`, replace `_wireLog?.Dispose();` with `StopWireLog();`.

Add, after `KeyboardStatus`'s property declaration:

```csharp
    public string? WireLogPath => Volatile.Read(ref _wireLog)?.Path;

    public void StartWireLog(string path)
    {
        lock (_writeLock)
        {
            if (_wireLog is not null) throw new InvalidOperationException("A wire log is already active.");
            try
            {
                _wireLog = new WireLog(path);
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new IOException(ex.Message, ex);
            }
        }
    }

    public void StopWireLog()
    {
        WireLog? old;
        lock (_writeLock)
        {
            old = _wireLog;
            _wireLog = null;
        }
        old?.Dispose();
    }
```

- [ ] **Step 6: Update the factory, the fake, and the integration tests**

`src/LizTerm.App/SessionFactory.cs`:

```csharp
using LizTerm.Backend.B3270;
using LizTerm.Backend.B3270.Process;
using LizTerm.Core.Session;

namespace LizTerm.App;

/// <summary>The only place the app names the b3270 backend.</summary>
public static class SessionFactory
{
    public static IEmulatorSession Create(SessionProfile profile)
    {
        var location = B3270Locator.Find();
        var wireLog = WireLog.TryFromEnvironment(out var wireLogError);
        return new B3270Session(profile, () => new B3270ChildProcess(location.Path), wireLog, wireLogError, location);
    }
}
```

In `tests/LizTerm.App.Tests/Fakes/FakeEmulatorSession.cs` add:

```csharp
    public string? WireLogPath { get; set; }
    /// <summary>When set, StartWireLog throws it.</summary>
    public Exception? WireLogException { get; set; }

    public void StartWireLog(string path)
    {
        Calls.Add("wirelog:start:" + path);
        if (WireLogException is not null) throw WireLogException;
        if (WireLogPath is not null) throw new InvalidOperationException("A wire log is already active.");
        WireLogPath = path;
    }

    public void StopWireLog()
    {
        Calls.Add("wirelog:stop");
        WireLogPath = null;
    }
```

In `tests/LizTerm.Integration.Tests/LiveHostTests.cs` replace both `WireLog.FromEnvironment()` arguments with `WireLog.TryFromEnvironment(out _)`.

- [ ] **Step 7: Run the suite**

Run: `dotnet test LizTerm.slnx`
Expected: all pass.

- [ ] **Step 8: Commit**

```bash
git add src tests
git commit -m "Turn the wire log into a session capability that can start and stop mid-session

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 4: Engine info on the session

**Files:**
- Modify: `src/LizTerm.Core/Session/IEmulatorSession.cs`, `src/LizTerm.Backend.B3270/B3270Session.cs`, `tests/LizTerm.App.Tests/Fakes/FakeEmulatorSession.cs`
- Test: `tests/LizTerm.Backend.B3270.Tests/B3270SessionLifecycleTests.cs`

**Interfaces:**
- Consumes: `EngineInfo`, `B3270Location`.
- Produces: `EngineInfo Engine { get; }` on `IEmulatorSession`; the fake's `Engine` is settable and defaults to `new("fake", null, "/fake/engine", EngineSource.Bundled)`.

- [ ] **Step 1: Write the failing test**

Add to `tests/LizTerm.Backend.B3270.Tests/B3270SessionLifecycleTests.cs`:

```csharp
    [Fact]
    public async Task Engine_reports_the_location_before_start_and_the_version_after_hello()
    {
        var fake = new FakeB3270Process();
        var location = new B3270Location("/opt/x/b3270", EngineSource.Override);
        await using var session = new B3270Session(Profile, () => fake, location: location);
        Assert.Equal(new EngineInfo("b3270", null, "/opt/x/b3270", EngineSource.Override), session.Engine);
        await session.StartProcessAsync(CancellationToken.None);
        Assert.Equal("4.5.6 (fake b3270)", session.Engine.Version);
        Assert.Equal(EngineSource.Override, session.Engine.Source);
    }
```

Add `using LizTerm.Backend.B3270.Process;` at the top of that file.

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/LizTerm.Backend.B3270.Tests --filter "FullyQualifiedName~Engine_reports"`
Expected: build error, `Engine` not found.

- [ ] **Step 3: Implement**

`IEmulatorSession`, after `KeyboardStatus`:

```csharp
    /// <summary>The engine binary in use; <see cref="EngineInfo.Version"/> fills in once the engine has started.</summary>
    EngineInfo Engine { get; }
```

`B3270Session`: add `public EngineInfo Engine { get; private set; }`; in the constructor after `_location = ...` add `Engine = new EngineInfo("b3270", null, _location.Path, _location.Source);`. In `StartProcessAsync`, right after the version check passes (before the wire-log warning), add:

```csharp
        Engine = Engine with { Version = $"{hello.Version} ({hello.Build})" };
```

`FakeEmulatorSession`: add `public EngineInfo Engine { get; set; } = new("fake", null, "/fake/engine", EngineSource.Bundled);`.

- [ ] **Step 4: Run the suite**

Run: `dotnet test LizTerm.slnx`
Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add src tests
git commit -m "Expose the engine binary, its source, and its version on the session

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 5: Cancellable connect with the certificate flag

**Files:**
- Modify: `src/LizTerm.Core/Session/IEmulatorSession.cs`, `src/LizTerm.Backend.B3270/B3270Session.cs` (`StartProcessAsync`, `ConnectAsync`), `tests/LizTerm.App.Tests/Fakes/FakeEmulatorSession.cs`, `tests/LizTerm.Backend.B3270.Tests/B3270SessionStateTests.cs` (the two `ConnectAsync(TestContext.Current.CancellationToken)` calls become `ConnectAsync(cancellationToken: TestContext.Current.CancellationToken)`), `tests/LizTerm.Integration.Tests/LiveHostTests.cs` (same change at each `ConnectAsync` call)
- Test: `tests/LizTerm.Backend.B3270.Tests/B3270SessionConnectTests.cs`

**Interfaces:**
- Consumes: `ConnectOptions`, `ConnectionFailedException(lines, certificateVerificationFailed)`.
- Produces: `Task ConnectAsync(ConnectOptions? options = null, CancellationToken cancellationToken = default)` on `IEmulatorSession`; `B3270Session.CertificateFailurePrefix` (`public const string`, value `"TLS: Host certificate verification failed"`). `FakeEmulatorSession` records `connect` or `connect:noverify`, exposes `ConnectToken`, and waits on `ConnectCompletion` when set, cancelling with the token.

- [ ] **Step 1: Write the failing tests**

Create `tests/LizTerm.Backend.B3270.Tests/B3270SessionConnectTests.cs`:

```csharp
using System.Text.RegularExpressions;
using LizTerm.Backend.B3270.Tests.Fakes;
using LizTerm.Core.Session;

namespace LizTerm.Backend.B3270.Tests;

public class B3270SessionConnectTests
{
    private static readonly SessionProfile Verifying = new() { Name = "t", Host = "h", Port = 4270, UseTls = true, VerifyCertificate = true };

    private static string Tag(string line) => Regex.Match(line, "\"r-tag\":\"([^\"]+)\"").Groups[1].Value;
    private static string Ok(string line) => $$$"""{"run-result":{"r-tag":"{{{Tag(line)}}}","success":true,"time":0}}""";
    private static string Failed(string tag, params string[] text) =>
        $$$"""{"run-result":{"r-tag":"{{{tag}}}","success":false,"text":[{{{string.Join(",", text.Select(t => "\"" + t + "\""))}}}],"time":0}}""";

    [Fact]
    public async Task Options_override_the_profile_verify_setting()
    {
        var fake = new FakeB3270Process();
        await using var session = new B3270Session(Verifying, () => fake);
        await session.ConnectAsync(new ConnectOptions(VerifyCertificate: false));
        Assert.Contains(fake.InputLines, l => l.Contains("\"verifyHostCert\",\"false\""));

        await session.ConnectAsync();
        Assert.Contains(fake.InputLines, l => l.Contains("\"verifyHostCert\",\"true\""));
    }

    [Fact]
    public async Task Certificate_failure_sets_the_flag_and_other_failures_do_not()
    {
        var fake = new FakeB3270Process
        {
            RunResponder = line => line.Contains("\"Connect\"")
                ? [Failed(Tag(line), "Connection failed:", "TLS: Host certificate verification failed:", "self-signed certificate (18)")]
                : [Ok(line)],
        };
        await using var session = new B3270Session(Verifying, () => fake);
        var ex = await Assert.ThrowsAsync<ConnectionFailedException>(() => session.ConnectAsync());
        Assert.True(ex.CertificateVerificationFailed);
        Assert.Equal("self-signed certificate (18)", ex.Lines[^1]);

        fake.RunResponder = line => line.Contains("\"Connect\"") ? [Failed(Tag(line), "Connection failed:", "Connection refused")] : [Ok(line)];
        var refused = await Assert.ThrowsAsync<ConnectionFailedException>(() => session.ConnectAsync());
        Assert.False(refused.CertificateVerificationFailed);
    }

    [Fact]
    public async Task Cancel_during_a_pending_connect_sends_disconnect_and_throws_cancellation()
    {
        string? connectTag = null;
        var fake = new FakeB3270Process();
        fake.RunResponder = line =>
        {
            if (line.Contains("\"Connect\"")) { connectTag = Tag(line); return []; }
            if (line.Contains("\"Disconnect\""))
                return [Ok(line), Failed(connectTag!, "Connection failed"), """{"connection":{"state":"not-connected"}}"""];
            return [Ok(line)];
        };
        await using var session = new B3270Session(Verifying, () => fake);
        using var cts = new CancellationTokenSource();

        var attempt = session.ConnectAsync(cancellationToken: cts.Token);
        await fake.WaitForInputAsync(l => l.Contains("\"Connect\""), TimeSpan.FromSeconds(1));
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => attempt);
        Assert.Contains(fake.InputLines, l => l.Contains("\"Disconnect\""));
        Assert.Equal(ConnectionState.Disconnected, session.ConnectionState);

        // The same process connects again.
        fake.RunResponder = null;
        await session.ConnectAsync();
        Assert.Equal(2, fake.InputLines.Count(l => l.Contains("\"Connect\"")));
    }

    [Fact]
    public async Task Cancelled_before_connect_throws_without_sending_connect()
    {
        var fake = new FakeB3270Process();
        await using var session = new B3270Session(Verifying, () => fake);
        await session.StartProcessAsync(CancellationToken.None);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.ConnectAsync(cancellationToken: cts.Token));
        Assert.DoesNotContain(fake.InputLines, l => l.Contains("\"Connect\""));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/LizTerm.Backend.B3270.Tests --filter "FullyQualifiedName~B3270SessionConnectTests"`
Expected: build errors (no `ConnectOptions` overload, no `CertificateFailurePrefix`).

- [ ] **Step 3: Implement**

`IEmulatorSession`: replace `Task ConnectAsync(CancellationToken cancellationToken = default);` with:

```csharp
    /// <summary>Connects as the profile says, with <paramref name="options"/> overriding it for this attempt only.
    /// Cancelling the token ends the attempt with <see cref="OperationCanceledException"/> and leaves the session
    /// disconnected and reusable. A refused connection throws <see cref="ConnectionFailedException"/>.</summary>
    Task ConnectAsync(ConnectOptions? options = null, CancellationToken cancellationToken = default);
```

`B3270Session`: add `public const string CertificateFailurePrefix = "TLS: Host certificate verification failed";` next to `MinimumVersion`. In `StartProcessAsync`, add a catch so a cancelled start does not leave a half-started process:

```csharp
        catch (OperationCanceledException)
        {
            TearDown();
            throw;
        }
```

Replace `ConnectAsync`:

```csharp
    public async Task ConnectAsync(ConnectOptions? options = null, CancellationToken cancellationToken = default)
    {
        await StartProcessAsync(cancellationToken);
        var verify = options?.VerifyCertificate ?? Profile.VerifyCertificate;
        await RunAsync(new B3270Action("Set", "verifyHostCert", verify ? "true" : "false"));
        cancellationToken.ThrowIfCancellationRequested();

        var run = RunRawAsync([new B3270Action("Connect", HostStringBuilder.Build(Profile))]);
        RunResultIndication result;
        // b3270 answers a Disconnect while a Connect is pending (verified against 4.5ga6): the Disconnect run
        // succeeds at once and the Connect run then fails with "Connection failed", which is the cancel's own
        // consequence rather than an error to report.
        using (cancellationToken.Register(() => _ = Task.Run(TryDisconnectQuietlyAsync)))
            result = await run;

        if (result.Success) return;
        cancellationToken.ThrowIfCancellationRequested();
        var certificate = result.Text.Any(line => line.StartsWith(CertificateFailurePrefix, StringComparison.Ordinal));
        throw new ConnectionFailedException(result.Text, certificate);
    }

    private async Task TryDisconnectQuietlyAsync()
    {
        try
        {
            await RunRawAsync([new B3270Action("Disconnect")]);
        }
        catch (Exception)
        {
            // The process may be gone; the pending Connect run faults on its own in that case.
        }
    }
```

`FakeEmulatorSession`: replace `ConnectAsync` with:

```csharp
    public CancellationToken ConnectToken { get; private set; }
    /// <summary>When set, ConnectAsync waits for it, faulting with the token's cancellation if that comes first,
    /// so a test can drive a pending attempt through the view model's timeout or Disconnect.</summary>
    public TaskCompletionSource? ConnectCompletion { get; set; }

    public async Task ConnectAsync(ConnectOptions? options = null, CancellationToken cancellationToken = default)
    {
        Calls.Add(options?.VerifyCertificate == false ? "connect:noverify" : "connect");
        ConnectToken = cancellationToken;
        cancellationToken.ThrowIfCancellationRequested();
        if (ConnectCompletion is { } completion)
        {
            using var registration = cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
            await completion.Task;
        }
        if (ConnectException is not null) throw ConnectException;
    }
```

Then fix the call sites listed under Files (`cancellationToken:` named argument). `SessionViewModel` calls `_session.ConnectAsync()` and needs no change yet.

- [ ] **Step 4: Run the suite**

Run: `dotnet test LizTerm.slnx`
Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add src tests
git commit -m "Make ConnectAsync cancellable through one Disconnect run and flag certificate failures

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 6: Certificate-failure replay fixture

**Files:**
- Create: `tests/LizTerm.Backend.B3270.Tests/Fixtures/gateway-cert-failure.jsonl`
- Modify: `tests/LizTerm.Backend.B3270.Tests/ReplayTests.cs`, `tests/LizTerm.Backend.B3270.Tests/Fixtures/README.md`

**Interfaces:**
- Consumes: `ConnectAsync`, `CertificateVerificationFailed` (Task 5).

- [ ] **Step 1: Record the fixture**

The fixture is raw b3270 stdout from a verify-on connect to the self-signed gateway, with the run tags named `set` and `connect` so the replay can substitute the session's own tags. Run from the worktree root:

```bash
( printf '%s\n' '{"run":{"r-tag":"set","actions":[{"action":"Set","args":["verifyHostCert","true"]}]}}' '{"run":{"r-tag":"connect","actions":[{"action":"Connect","args":["L:129.212.188.194:4270"]}]}}'; sleep 6 ) | /opt/homebrew/bin/b3270 -json -utf8 -model 3279-2-E -codepage cp037 | sed 's/129\.212\.188\.194/gateway.test/g' > tests/LizTerm.Backend.B3270.Tests/Fixtures/gateway-cert-failure.jsonl
grep -c "" tests/LizTerm.Backend.B3270.Tests/Fixtures/gateway-cert-failure.jsonl
grep -n "run-result\|connection" tests/LizTerm.Backend.B3270.Tests/Fixtures/gateway-cert-failure.jsonl
```

Expected: about 15 lines; a `run-result` for `set`, connection states `tcp-pending`, `telnet-pending`, `tls-pending`, then the `connect` run-result with `"TLS: Host certificate verification failed:"` and `not-connected`. Confirm `grep -c 129.212 <file>` prints `0`.

- [ ] **Step 2: Write the failing replay test**

Add to `tests/LizTerm.Backend.B3270.Tests/ReplayTests.cs` (add `using System.Text.RegularExpressions;`):

```csharp
    /// <summary>The fixture was recorded with run tags "set" and "connect"; the responder replays the engine's
    /// answers against the tags this session actually sends, so ConnectAsync sees the real failure text.</summary>
    [Fact]
    public async Task Gateway_certificate_failure_replays_to_a_flagged_connection_failure()
    {
        var lines = File.ReadAllLines(Fixture("gateway-cert-failure.jsonl"));
        var fake = new FakeB3270Process { AutoInitialize = false, RunResponder = input => Respond(input, lines) };
        fake.Emit(lines[0]);

        var profile = new SessionProfile { Name = "replay", Host = "gateway.test", Port = 4270, UseTls = true, VerifyCertificate = true };
        var session = new B3270Session(profile, () => fake);
        var states = new List<ConnectionState>();
        session.ConnectionChanged += (_, s) => states.Add(s);

        var ex = await Assert.ThrowsAsync<ConnectionFailedException>(() => session.ConnectAsync(cancellationToken: TestContext.Current.CancellationToken));
        Assert.True(ex.CertificateVerificationFailed);
        Assert.Contains("self-signed certificate", ex.Lines[^1]);
        Assert.Equal([ConnectionState.TcpPending, ConnectionState.TelnetPending, ConnectionState.TlsPending, ConnectionState.Disconnected], states);
        Assert.Null(session.Tls);
    }

    private static IReadOnlyList<string> Respond(string input, string[] fixture)
    {
        var tag = Regex.Match(input, "\"r-tag\":\"([^\"]+)\"").Groups[1].Value;
        if (input.Contains("\"Set\""))
            return fixture.Where(l => l.Contains("\"r-tag\":\"set\"")).Select(l => l.Replace("\"r-tag\":\"set\"", $"\"r-tag\":\"{tag}\"")).ToList();
        if (input.Contains("\"Connect\""))
            return fixture.SkipWhile(l => !l.Contains("connect-attempt")).Select(l => l.Replace("\"r-tag\":\"connect\"", $"\"r-tag\":\"{tag}\"")).ToList();
        return [];
    }
```

If the recorded state sequence differs from the assertion (for example an extra `telnet-pending` after `tls-pending`), correct the assertion to what the fixture holds and say so in the README entry: the fixture is the truth.

- [ ] **Step 3: Run the test**

Run: `dotnet test tests/LizTerm.Backend.B3270.Tests --filter "FullyQualifiedName~Gateway_certificate_failure"`
Expected: PASS.

- [ ] **Step 4: Document the fixture**

Append to `tests/LizTerm.Backend.B3270.Tests/Fixtures/README.md`:

```markdown
- `gateway-cert-failure.jsonl`: raw b3270 4.5ga6 stdout from a verify-on TLS connect to the same hobbyist
  gateway on 2026-09-05, recorded with run tags `set` and `connect` (the replay substitutes the session's own
  tags). The Connect run fails with `Connection failed:`, `TLS: Host certificate verification failed:`, and the
  OpenSSL reason `self-signed certificate (18)`; the states run `tcp-pending`, `telnet-pending`, `tls-pending`,
  `not-connected`, and no `tls` indication is ever sent. The address was replaced with `gateway.test`.
```

- [ ] **Step 5: Commit**

```bash
git add tests/LizTerm.Backend.B3270.Tests
git commit -m "Add the certificate-failure replay fixture

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 7: x3270 host syntax on the command line

**Files:**
- Modify: `src/LizTerm.App/Startup/StartupArguments.cs`
- Test: `tests/LizTerm.App.Tests/Startup/StartupArgumentsTests.cs`

**Interfaces:**
- Produces: `StartupArguments(string? ProfileName, string? Host, int? Port, bool UseTls = false, bool VerifyCertificate = true, string? LuName = null, string? Error = null)`; `StartupArguments.Usage` const; `Resolve` copies the three new fields into the ad hoc profile and names it `[lu@]host:port`.

- [ ] **Step 1: Write the failing tests**

Add to `tests/LizTerm.App.Tests/Startup/StartupArgumentsTests.cs`:

```csharp
    [Theory]
    [InlineData("L:mvs.local", true, true, null, "mvs.local", 992)]
    [InlineData("L:mvs.local:4270", true, true, null, "mvs.local", 4270)]
    [InlineData("Y:L:mvs.local:4270", true, false, null, "mvs.local", 4270)]
    [InlineData("L:Y:mvs.local", true, false, null, "mvs.local", 992)]
    [InlineData("Y:mvs.local", false, false, null, "mvs.local", 23)]
    [InlineData("CONS01@mvs.local:3270", false, true, "CONS01", "mvs.local", 3270)]
    [InlineData("LU1,LU2@mvs.local", false, true, "LU1,LU2", "mvs.local", 23)]
    [InlineData("L:CONS01@[fe80::1]:4270", true, true, "CONS01", "fe80::1", 4270)]
    public void Prefixes_and_lu_names_are_parsed(string arg, bool tls, bool verify, string? lu, string host, int port)
    {
        var parsed = StartupArguments.Parse([arg]);
        Assert.Null(parsed.Error);
        Assert.Null(parsed.ProfileName);
        Assert.Equal(tls, parsed.UseTls);
        Assert.Equal(verify, parsed.VerifyCertificate);
        Assert.Equal(lu, parsed.LuName);
        Assert.Equal(host, parsed.Host);
        var profile = parsed.Resolve([])!;
        Assert.Equal(port, profile.Port);
        Assert.Equal(tls, profile.UseTls);
        Assert.Equal(verify, profile.VerifyCertificate);
        Assert.Equal(lu, profile.LuName);
    }

    [Theory]
    [InlineData("X:mvs.local")]
    [InlineData("L:L:mvs.local")]
    [InlineData("@mvs.local")]
    [InlineData("L:@mvs.local:23")]
    public void Bad_syntax_is_an_error_with_usage(string arg)
    {
        var parsed = StartupArguments.Parse([arg]);
        Assert.Equal(StartupArguments.Usage, parsed.Error);
        Assert.Null(parsed.Host);
        Assert.Null(parsed.ProfileName);
        Assert.Null(parsed.Resolve([]));
    }

    [Fact]
    public void Ad_hoc_profile_name_is_the_address_without_prefixes()
    {
        Assert.Equal("mvs.local:4270", StartupArguments.Parse(["L:Y:mvs.local:4270"]).Resolve([])!.Name);
        Assert.Equal("CONS01@mvs.local:3270", StartupArguments.Parse(["CONS01@mvs.local:3270"]).Resolve([])!.Name);
        Assert.Equal("mvs.local:992", StartupArguments.Parse(["L:mvs.local"]).Resolve([])!.Name);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~StartupArgumentsTests"`
Expected: build errors (`Error`, `UseTls`, `Usage` missing).

- [ ] **Step 3: Implement**

Replace `src/LizTerm.App/Startup/StartupArguments.cs`:

```csharp
using LizTerm.Core.Session;

namespace LizTerm.App.Startup;

/// <summary>Command line: no argument opens the picker; a saved profile name connects to it; x3270's ad hoc host
/// syntax <c>[L:][Y:][lu@]host[:port]</c> (IPv6 hosts bracketed) connects without a profile. <c>L:</c> is TLS,
/// <c>Y:</c> turns certificate verification off, and the LU part is passed to the engine verbatim, comma lists
/// included. A syntax error sets <see cref="Error"/> to <see cref="Usage"/> and resolves to the picker.</summary>
public sealed record StartupArguments(
    string? ProfileName,
    string? Host,
    int? Port,
    bool UseTls = false,
    bool VerifyCertificate = true,
    string? LuName = null,
    string? Error = null)
{
    public const string Usage = "Usage: LizTerm [profile | [L:][Y:][lu@]host[:port] | [L:][Y:][lu@][ipv6][:port]]";

    private static readonly StartupArguments Invalid = new(null, null, null, Error: Usage);

    public static StartupArguments Parse(IReadOnlyList<string> args)
    {
        if (args.Count == 0 || string.IsNullOrWhiteSpace(args[0])) return new StartupArguments(null, null, null);
        var arg = args[0].Trim();

        var tls = false;
        var verify = true;
        var sawPrefix = false;
        while (arg.Length >= 2 && char.IsAsciiLetter(arg[0]) && arg[1] == ':')
        {
            switch (char.ToUpperInvariant(arg[0]))
            {
                case 'L' when !tls: tls = true; break;
                case 'Y' when verify: verify = false; break;
                default: return Invalid;
            }
            sawPrefix = true;
            arg = arg[2..];
        }

        string? lu = null;
        var at = arg.IndexOf('@');
        if (at >= 0)
        {
            if (at == 0) return Invalid;
            lu = arg[..at];
            arg = arg[(at + 1)..];
        }

        var (host, port) = ParseHostPort(arg, sawPrefix || lu is not null);
        if (host is null) return sawPrefix || lu is not null ? Invalid : new StartupArguments(arg, null, null);
        return new StartupArguments(null, host, port, tls, verify, lu);
    }

    /// <summary>Host and optional port. Without a prefix or LU, a bare word with no dot is a profile name, so this
    /// returns a null host for it; with one, any non-empty word is a host.</summary>
    private static (string? Host, int? Port) ParseHostPort(string arg, bool forceHost)
    {
        if (arg.Length == 0) return (null, null);
        if (arg.StartsWith('['))
        {
            var close = arg.IndexOf(']');
            if (close > 1)
            {
                var rest = arg[(close + 1)..];
                int? p = rest.StartsWith(':') && int.TryParse(rest[1..], out var parsed) ? parsed : null;
                return (arg[1..close], p);
            }
        }

        var lastColon = arg.LastIndexOf(':');
        if (lastColon > 0 && arg.IndexOf(':') == lastColon && int.TryParse(arg[(lastColon + 1)..], out var port))
            return (arg[..lastColon], port);

        if (forceHost || arg.Contains('.') || arg.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            return (arg, null);

        return (null, null);
    }

    public SessionProfile? Resolve(IReadOnlyList<SessionProfile> profiles)
    {
        if (Error is not null) return null;
        if (ProfileName is not null)
            return profiles.FirstOrDefault(p => p.Name.Equals(ProfileName, StringComparison.OrdinalIgnoreCase));
        if (Host is null) return null;
        var port = Port ?? (UseTls ? 992 : 23);
        var address = $"{Host}:{port}";
        return new SessionProfile
        {
            Name = LuName is null ? address : $"{LuName}@{address}",
            Host = Host,
            Port = port,
            UseTls = UseTls,
            VerifyCertificate = VerifyCertificate,
            LuName = LuName,
        };
    }
}
```

Note the existing test `Resolve_builds_an_ad_hoc_profile_for_hosts` expects the name `mvs.local:3270` for `mvs.local:3270` and port 23 for `mvs.local`; with the new naming `mvs.local` resolves to name `mvs.local:23`. Update that test's expectation only if it asserts the bare name; it asserts `"mvs.local:3270"` and `Port == 23`, both still true.

- [ ] **Step 4: Run the tests**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~StartupArgumentsTests"`
Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add src/LizTerm.App/Startup tests/LizTerm.App.Tests/Startup
git commit -m "Accept L:, Y:, and lu@ on the ad hoc command line

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 8: Status strings for timeout, wire log, engine, and the new fault advice

**Files:**
- Modify: `src/LizTerm.App/Status/StatusFormatter.cs`
- Test: `tests/LizTerm.App.Tests/Status/StatusFormatterTests.cs`

**Interfaces:**
- Produces: `StatusFormatter.ConnectTimeout(SessionProfile profile, TimeSpan timeout, bool reachedTelnet)`, `StatusFormatter.WireLog(bool active)`, `StatusFormatter.Engine(EngineInfo engine, string overrideOrigin)`; `Fault` ends with `"Turn on Help > Wire Log and reproduce to capture a log."`.

- [ ] **Step 1: Write the failing tests**

In `tests/LizTerm.App.Tests/Status/StatusFormatterTests.cs`, change the `Fault_text_mentions_exit_code_and_stderr` assertion `Assert.Contains("LIZTERM_WIRE_LOG", text);` to `Assert.EndsWith("Turn on Help > Wire Log and reproduce to capture a log.", text);` and add:

```csharp
    [Fact]
    public void Connect_timeout_names_the_host_and_hints_at_tls_only_when_it_applies()
    {
        var plain = new SessionProfile { Name = "p", Host = "mvs.local", Port = 4270 };
        Assert.Equal("Connection to mvs.local:4270 timed out after 30 seconds. The host may require TLS. Enable it in the profile.",
            StatusFormatter.ConnectTimeout(plain, TimeSpan.FromSeconds(30), reachedTelnet: true));
        Assert.Equal("Connection to mvs.local:4270 timed out after 30 seconds.",
            StatusFormatter.ConnectTimeout(plain, TimeSpan.FromSeconds(30), reachedTelnet: false));
        Assert.Equal("Connection to mvs.local:4270 timed out after 5 seconds.",
            StatusFormatter.ConnectTimeout(plain with { UseTls = true }, TimeSpan.FromSeconds(5), reachedTelnet: true));
    }

    [Fact]
    public void Wire_log_indicator()
    {
        Assert.Equal("● wire log", StatusFormatter.WireLog(true));
        Assert.Equal("", StatusFormatter.WireLog(false));
    }

    [Fact]
    public void Engine_line_names_version_and_source()
    {
        var bundled = new EngineInfo("b3270", "4.5.6 (b3270 v4.5ga6)", "/app/b3270", EngineSource.Bundled);
        Assert.Equal("b3270 4.5.6 (b3270 v4.5ga6), bundled", StatusFormatter.Engine(bundled, "LIZTERM_B3270_PATH"));
        var overridden = bundled with { Source = EngineSource.Override };
        Assert.Equal("b3270 4.5.6 (b3270 v4.5ga6), from LIZTERM_B3270_PATH", StatusFormatter.Engine(overridden, "LIZTERM_B3270_PATH"));
        Assert.Equal("b3270, not started, bundled", StatusFormatter.Engine(bundled with { Version = null }, "LIZTERM_B3270_PATH"));
        Assert.Equal("b3270, not started, from LIZTERM_B3270_PATH", StatusFormatter.Engine(overridden with { Version = null }, "LIZTERM_B3270_PATH"));
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~StatusFormatterTests"`
Expected: build errors for the three new methods.

- [ ] **Step 3: Implement**

In `StatusFormatter.Fault`, replace the trailing sentence with `Turn on Help > Wire Log and reproduce to capture a log.` and add:

```csharp
    /// <summary>The attempt never completed. A plain connect to a TLS listener reaches telnet-pending and then
    /// waits forever, so that exact signature earns the TLS hint.</summary>
    public static string ConnectTimeout(SessionProfile profile, TimeSpan timeout, bool reachedTelnet)
    {
        var text = $"Connection to {profile.Host}:{profile.Port} timed out after {timeout.TotalSeconds:0} seconds.";
        return reachedTelnet && !profile.UseTls ? text + " The host may require TLS. Enable it in the profile." : text;
    }

    public static string WireLog(bool active) => active ? "● wire log" : "";

    /// <param name="overrideOrigin">What pointed at an Override binary, named by the app (today the environment variable).</param>
    public static string Engine(EngineInfo engine, string overrideOrigin)
    {
        var name = engine.Version is null ? $"{engine.Name}, not started" : $"{engine.Name} {engine.Version}";
        return engine.Source == EngineSource.Bundled ? $"{name}, bundled" : $"{name}, from {overrideOrigin}";
    }
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~StatusFormatterTests"`
Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add src/LizTerm.App/Status tests/LizTerm.App.Tests/Status
git commit -m "Add the timeout, wire log, and engine status strings and point the fault advice at the Help menu

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 9: Connect timeout and cancel in the view model

**Files:**
- Modify: `src/LizTerm.App/ViewModels/SessionViewModel.cs`
- Test: `tests/LizTerm.App.Tests/ViewModels/SessionViewModelConnectTests.cs`

**Interfaces:**
- Consumes: `FakeEmulatorSession.ConnectCompletion`, `ConnectToken` (Task 5); `StatusFormatter.ConnectTimeout` (Task 8).
- Produces: `SessionViewModel.ConnectTimeout` (instance `TimeSpan`, default `DefaultConnectTimeout` = 30 s); `DisconnectCommand` cancels a pending connect; private `ConnectWithAsync(ConnectOptions)` that Task 10 extends.

- [ ] **Step 1: Write the failing tests**

Create `tests/LizTerm.App.Tests/ViewModels/SessionViewModelConnectTests.cs`:

```csharp
using LizTerm.App.Tests.Fakes;
using LizTerm.App.ViewModels;
using LizTerm.Core.Session;

namespace LizTerm.App.Tests.ViewModels;

public class SessionViewModelConnectTests
{
    private static (SessionViewModel Vm, FakeEmulatorSession Session) Create(SessionProfile? profile = null)
    {
        var session = new FakeEmulatorSession();
        if (profile is not null) session.Profile = profile;
        var vm = new SessionViewModel(session, action => action(), new FakeTextClipboard()) { ConnectTimeout = TimeSpan.FromMilliseconds(100) };
        return (vm, session);
    }

    private static TaskCompletionSource Pending() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    [Fact]
    public void Default_timeout_is_thirty_seconds()
    {
        var session = new FakeEmulatorSession();
        var vm = new SessionViewModel(session, a => a(), new FakeTextClipboard());
        Assert.Equal(TimeSpan.FromSeconds(30), vm.ConnectTimeout);
        Assert.Equal(SessionViewModel.DefaultConnectTimeout, vm.ConnectTimeout);
    }

    [Fact]
    public async Task Timeout_reports_the_host_with_the_tls_hint_after_telnet_pending()
    {
        var (vm, session) = Create();
        session.ConnectCompletion = Pending();
        var attempt = vm.ConnectCommand.ExecuteAsync(null);
        session.RaiseConnection(ConnectionState.TcpPending);
        session.RaiseConnection(ConnectionState.TelnetPending);
        await attempt;
        Assert.True(session.ConnectToken.IsCancellationRequested);
        Assert.Equal("Connection to fake.host:3270 timed out after 0 seconds. The host may require TLS. Enable it in the profile.", vm.ErrorMessage);
    }

    [Fact]
    public async Task Timeout_without_telnet_pending_has_no_hint()
    {
        var (vm, session) = Create();
        session.ConnectCompletion = Pending();
        var attempt = vm.ConnectCommand.ExecuteAsync(null);
        session.RaiseConnection(ConnectionState.TcpPending);
        await attempt;
        Assert.Equal("Connection to fake.host:3270 timed out after 0 seconds.", vm.ErrorMessage);
    }

    [Fact]
    public async Task Disconnect_during_a_pending_connect_cancels_it_silently()
    {
        var (vm, session) = Create();
        session.ConnectCompletion = Pending();
        var attempt = vm.ConnectCommand.ExecuteAsync(null);
        await vm.DisconnectCommand.ExecuteAsync(null);
        await attempt;
        Assert.True(session.ConnectToken.IsCancellationRequested);
        Assert.Null(vm.ErrorMessage);
        Assert.DoesNotContain("disconnect", session.Calls);
    }

    [Fact]
    public async Task Disconnect_while_connected_still_disconnects()
    {
        var (vm, session) = Create();
        await vm.ConnectCommand.ExecuteAsync(null);
        session.RaiseConnection(ConnectionState.Connected3270);
        await vm.DisconnectCommand.ExecuteAsync(null);
        Assert.Equal(["connect", "disconnect"], session.Calls);
    }

    [Fact]
    public async Task A_completed_connect_clears_the_pending_state_so_the_next_attempt_gets_its_own_token()
    {
        var (vm, session) = Create();
        await vm.ConnectCommand.ExecuteAsync(null);
        var first = session.ConnectToken;
        await vm.ConnectCommand.ExecuteAsync(null);
        Assert.NotEqual(first, session.ConnectToken);
        Assert.Null(vm.ErrorMessage);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~SessionViewModelConnectTests"`
Expected: build error, `ConnectTimeout` not found.

- [ ] **Step 3: Implement**

In `SessionViewModel` add fields and the property:

```csharp
    public static readonly TimeSpan DefaultConnectTimeout = TimeSpan.FromSeconds(30);

    /// <summary>How long one connect attempt may take before it is cancelled. An instance property so tests can
    /// shorten it without racing each other on a static.</summary>
    public TimeSpan ConnectTimeout { get; set; } = DefaultConnectTimeout;

    private CancellationTokenSource? _connectCts;
    private bool _connectCancelledByUser;
    private ConnectionState _furthestState;
```

In `ApplyConnection`, after `IsConnected = ...`, add `if (state > _furthestState) _furthestState = state;`.

Replace the `ConnectAsync` command and `DisconnectAsync`:

```csharp
    [RelayCommand]
    private Task ConnectAsync() => ConnectWithAsync(new ConnectOptions());

    private async Task ConnectWithAsync(ConnectOptions options)
    {
        ErrorMessage = null;
        _furthestState = ConnectionState.Disconnected;
        _connectCancelledByUser = false;
        using var cts = new CancellationTokenSource(ConnectTimeout);
        _connectCts = cts;
        try
        {
            await _session.ConnectAsync(options, cts.Token);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            if (!_connectCancelledByUser)
                ErrorMessage = StatusFormatter.ConnectTimeout(Profile, ConnectTimeout, _furthestState >= ConnectionState.TelnetPending);
        }
        catch (ConnectionFailedException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (BackendUnavailableException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (Exception ex)
        {
            ErrorMessage = "Unexpected error: " + ex.Message;
        }
        finally
        {
            if (ReferenceEquals(_connectCts, cts)) _connectCts = null;
        }
    }

    /// <summary>While a connect is pending this cancels it (the backend sends the Disconnect); otherwise it disconnects.</summary>
    [RelayCommand]
    private Task DisconnectAsync()
    {
        if (_connectCts is { } pending)
        {
            _connectCancelledByUser = true;
            pending.Cancel();
            return Task.CompletedTask;
        }
        return Guard(_session.DisconnectAsync());
    }
```

- [ ] **Step 4: Run the App tests**

Run: `dotnet test tests/LizTerm.App.Tests`
Expected: all pass (the existing `Connect_command_calls_session_and_reports_failure` still holds).

- [ ] **Step 5: Commit**

```bash
git add src/LizTerm.App/ViewModels tests/LizTerm.App.Tests/ViewModels
git commit -m "Time out a connect attempt after 30 seconds and let Disconnect cancel one in progress

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 10: Certificate prompt seam and the connect-anyway flow

**Files:**
- Create: `src/LizTerm.App/Dialogs/ICertificatePrompt.cs`, `tests/LizTerm.App.Tests/Fakes/FakeCertificatePrompt.cs`
- Modify: `src/LizTerm.App/ViewModels/SessionViewModel.cs` (constructor, `ConnectWithAsync`)
- Test: `tests/LizTerm.App.Tests/ViewModels/SessionViewModelConnectTests.cs`

**Interfaces:**
- Produces: `ICertificatePrompt.AskAsync(string host, IReadOnlyList<string> reason, bool canRemember)` returning `CertificateDecision(bool ConnectAnyway, bool Remember)`; `SessionViewModel(IEmulatorSession session, Action<Action> dispatch, ITextClipboard clipboard, ICertificatePrompt? certificatePrompt = null, Action<SessionProfile>? saveProfile = null)`; `FakeCertificatePrompt` with `Decision`, `OnAsk`, and `Calls` (`ask:<host>:<canRemember>`).

- [ ] **Step 1: Write the seam and the fake**

`src/LizTerm.App/Dialogs/ICertificatePrompt.cs`:

```csharp
namespace LizTerm.App.Dialogs;

/// <summary>Asks whether to connect without verifying the host certificate. Injected like the clipboard so tests
/// answer without a window. A null prompt on the view model declines.</summary>
public interface ICertificatePrompt
{
    /// <param name="reason">The engine's explanation, one line each, without the leading "Connection failed:".</param>
    /// <param name="canRemember">Whether "Always allow for this profile" can be offered (the profile is saved).</param>
    Task<CertificateDecision> AskAsync(string host, IReadOnlyList<string> reason, bool canRemember);
}

public sealed record CertificateDecision(bool ConnectAnyway, bool Remember)
{
    public static readonly CertificateDecision Declined = new(false, false);
}
```

`tests/LizTerm.App.Tests/Fakes/FakeCertificatePrompt.cs`:

```csharp
using LizTerm.App.Dialogs;

namespace LizTerm.App.Tests.Fakes;

public sealed class FakeCertificatePrompt : ICertificatePrompt
{
    public CertificateDecision Decision { get; set; } = CertificateDecision.Declined;
    /// <summary>Runs before the decision is returned; tests use it to change the fake session between attempts.</summary>
    public Action? OnAsk { get; set; }
    /// <summary>"ask:<host>:<canRemember>" per call.</summary>
    public List<string> Calls { get; } = [];
    public IReadOnlyList<string>? LastReason { get; private set; }

    public Task<CertificateDecision> AskAsync(string host, IReadOnlyList<string> reason, bool canRemember)
    {
        Calls.Add($"ask:{host}:{canRemember}");
        LastReason = reason;
        OnAsk?.Invoke();
        return Task.FromResult(Decision);
    }
}
```

- [ ] **Step 2: Write the failing tests**

Add to `SessionViewModelConnectTests`:

```csharp
    private static readonly ConnectionFailedException CertFailure = new(
        ["Connection failed:", "TLS: Host certificate verification failed:", "self-signed certificate (18)"], certificateVerificationFailed: true);

    private static (SessionViewModel Vm, FakeEmulatorSession Session, FakeCertificatePrompt Prompt, List<SessionProfile> Saved) CreateWithPrompt(bool saveable)
    {
        var session = new FakeEmulatorSession { ConnectException = CertFailure };
        var prompt = new FakeCertificatePrompt();
        var saved = new List<SessionProfile>();
        var vm = new SessionViewModel(session, a => a(), new FakeTextClipboard(), prompt, saveable ? saved.Add : null);
        return (vm, session, prompt, saved);
    }

    [Fact]
    public async Task Declined_prompt_shows_the_failure()
    {
        var (vm, session, prompt, _) = CreateWithPrompt(saveable: true);
        await vm.ConnectCommand.ExecuteAsync(null);
        Assert.Equal(["ask:fake.host:True"], prompt.Calls);
        Assert.Equal(["TLS: Host certificate verification failed:", "self-signed certificate (18)"], prompt.LastReason);
        Assert.Equal(["connect"], session.Calls);
        Assert.Equal(CertFailure.Message, vm.ErrorMessage);
    }

    [Fact]
    public async Task Accepted_prompt_reconnects_without_verification_once()
    {
        var (vm, session, prompt, saved) = CreateWithPrompt(saveable: true);
        prompt.Decision = new CertificateDecision(ConnectAnyway: true, Remember: false);
        prompt.OnAsk = () => session.ConnectException = null;
        await vm.ConnectCommand.ExecuteAsync(null);
        Assert.Equal(["connect", "connect:noverify"], session.Calls);
        Assert.Null(vm.ErrorMessage);
        Assert.Empty(saved);

        // Not remembered: the next attempt verifies again and asks again.
        session.ConnectException = CertFailure;
        prompt.Decision = CertificateDecision.Declined;
        await vm.ConnectCommand.ExecuteAsync(null);
        Assert.Equal(["connect", "connect:noverify", "connect"], session.Calls);
        Assert.Equal(2, prompt.Calls.Count);
    }

    [Fact]
    public async Task Remembered_prompt_saves_the_profile_and_stops_asking()
    {
        var (vm, session, prompt, saved) = CreateWithPrompt(saveable: true);
        prompt.Decision = new CertificateDecision(ConnectAnyway: true, Remember: true);
        prompt.OnAsk = () => session.ConnectException = null;
        await vm.ConnectCommand.ExecuteAsync(null);
        var profile = Assert.Single(saved);
        Assert.False(profile.VerifyCertificate);
        Assert.Equal(session.Profile.Name, profile.Name);

        await vm.ConnectCommand.ExecuteAsync(null);
        Assert.Equal(["connect", "connect:noverify", "connect:noverify"], session.Calls);
        Assert.Single(prompt.Calls);
    }

    [Fact]
    public async Task Ad_hoc_profile_cannot_remember()
    {
        var (vm, _, prompt, _) = CreateWithPrompt(saveable: false);
        await vm.ConnectCommand.ExecuteAsync(null);
        Assert.Equal(["ask:fake.host:False"], prompt.Calls);
    }

    [Fact]
    public async Task Without_a_prompt_the_failure_is_shown()
    {
        var session = new FakeEmulatorSession { ConnectException = CertFailure };
        var vm = new SessionViewModel(session, a => a(), new FakeTextClipboard());
        await vm.ConnectCommand.ExecuteAsync(null);
        Assert.Equal(CertFailure.Message, vm.ErrorMessage);
    }

    [Fact]
    public async Task A_failure_after_connect_anyway_does_not_ask_again()
    {
        var (vm, session, prompt, _) = CreateWithPrompt(saveable: true);
        prompt.Decision = new CertificateDecision(ConnectAnyway: true, Remember: false);
        prompt.OnAsk = () => session.ConnectException = new ConnectionFailedException(["Connection failed:", "Connection refused"]);
        await vm.ConnectCommand.ExecuteAsync(null);
        Assert.Single(prompt.Calls);
        Assert.Equal("Connection failed: Connection refused", vm.ErrorMessage);
    }
```

Add `using LizTerm.App.Dialogs;` to the test file.

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~SessionViewModelConnectTests"`
Expected: build error on the five-argument constructor.

- [ ] **Step 4: Implement**

In `SessionViewModel`: add `using LizTerm.App.Dialogs;`, fields `private readonly ICertificatePrompt? _certificatePrompt; private readonly Action<SessionProfile>? _saveProfile; private bool? _verifyOverride;`, and change the constructor signature to:

```csharp
    /// <param name="certificatePrompt">Asked on a certificate verification failure; null declines.</param>
    /// <param name="saveProfile">Persists the profile when the user chooses "Always allow"; null for ad hoc profiles.</param>
    public SessionViewModel(IEmulatorSession session, Action<Action> dispatch, ITextClipboard clipboard,
        ICertificatePrompt? certificatePrompt = null, Action<SessionProfile>? saveProfile = null)
```

assigning both fields. Change the command to pass the remembered override: `ConnectWithAsync(new ConnectOptions(_verifyOverride))`. In `ConnectWithAsync`, replace the `ConnectionFailedException` catch with:

```csharp
        catch (ConnectionFailedException ex) when (ex.CertificateVerificationFailed && options.VerifyCertificate != false)
        {
            await OfferConnectAnywayAsync(ex);
        }
        catch (ConnectionFailedException ex)
        {
            ErrorMessage = ex.Message;
        }
```

and add:

```csharp
    /// <summary>Spec 5.3: ask once; on yes reconnect with verification off for this attempt, and with Remember also
    /// save the profile and keep the override for the window's life. The reconnect cannot fail on verification,
    /// so this never loops.</summary>
    private async Task OfferConnectAnywayAsync(ConnectionFailedException failure)
    {
        var reason = failure.Lines.Where(line => line != "Connection failed:").ToList();
        var decision = _certificatePrompt is null
            ? CertificateDecision.Declined
            : await _certificatePrompt.AskAsync(Profile.Host, reason, _saveProfile is not null);
        if (!decision.ConnectAnyway)
        {
            ErrorMessage = failure.Message;
            return;
        }
        if (decision.Remember)
        {
            _verifyOverride = false;
            _saveProfile?.Invoke(Profile with { VerifyCertificate = false });
        }
        await ConnectWithAsync(new ConnectOptions(VerifyCertificate: false));
    }
```

`ConnectWithAsync`'s `finally` already guards with `ReferenceEquals`, so the nested attempt's own cleanup does not clear the outer attempt's slot early.

- [ ] **Step 5: Run the App tests**

Run: `dotnet test tests/LizTerm.App.Tests`
Expected: all pass.

- [ ] **Step 6: Commit**

```bash
git add src/LizTerm.App tests/LizTerm.App.Tests
git commit -m "Offer to connect anyway after a certificate failure, once or remembered on the profile

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 11: CertificateWindow, its prompt, and app wiring

**Files:**
- Create: `src/LizTerm.App/Views/CertificateWindow.axaml`, `src/LizTerm.App/Views/CertificateWindow.axaml.cs`, `src/LizTerm.App/Dialogs/AvaloniaCertificatePrompt.cs`
- Modify: `src/LizTerm.App/App.axaml.cs` (`OpenSession`, `ShowPicker`)
- Test: `tests/LizTerm.App.Tests/Views/CertificateWindowTests.cs`

**Interfaces:**
- Consumes: `ICertificatePrompt`, `CertificateDecision` (Task 10).
- Produces: `CertificateWindow(string host, IReadOnlyList<string> reason, bool canRemember)` with `CertificateDecision? Decision`; `AvaloniaCertificatePrompt(Window owner)`; `App.OpenSession(SessionProfile profile, bool fromStore)`.

- [ ] **Step 1: Write the failing test**

Create `tests/LizTerm.App.Tests/Views/CertificateWindowTests.cs`:

```csharp
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using LizTerm.App.Dialogs;
using LizTerm.App.Views;

namespace LizTerm.App.Tests.Views;

public class CertificateWindowTests
{
    private static readonly string[] Reason = ["TLS: Host certificate verification failed:", "self-signed certificate (18)"];

    [AvaloniaFact]
    public void Shows_host_and_reason_and_hides_remember_for_ad_hoc_profiles()
    {
        var window = new CertificateWindow("mvs.local", Reason, canRemember: false);
        window.Show();
        Assert.Equal("Certificate not verified", window.Title);
        Assert.Equal("mvs.local presented a certificate that could not be verified:", window.FindControl<TextBlock>("HostText")!.Text);
        Assert.Equal(string.Join("\n", Reason), window.FindControl<TextBlock>("ReasonText")!.Text);
        Assert.False(window.FindControl<CheckBox>("RememberBox")!.IsVisible);
    }

    [AvaloniaFact]
    public void Escape_declines_and_remember_is_carried_on_connect_anyway()
    {
        var declined = new CertificateWindow("h", Reason, canRemember: true);
        declined.Show();
        Assert.True(declined.FindControl<CheckBox>("RememberBox")!.IsVisible);
        declined.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Assert.Equal(CertificateDecision.Declined, declined.Decision);

        var accepted = new CertificateWindow("h", Reason, canRemember: true);
        accepted.Show();
        accepted.FindControl<CheckBox>("RememberBox")!.IsChecked = true;
        accepted.ConnectAnyway();
        Assert.Equal(new CertificateDecision(true, true), accepted.Decision);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~CertificateWindowTests"`
Expected: build error, `CertificateWindow` not found.

- [ ] **Step 3: Implement the window**

`src/LizTerm.App/Views/CertificateWindow.axaml`:

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:controls="using:LizTerm.App.Controls"
        x:Class="LizTerm.App.Views.CertificateWindow"
        Title="Certificate not verified" Width="520" SizeToContent="Height" CanResize="False"
        WindowStartupLocation="CenterOwner">
  <StackPanel Margin="16" Spacing="12">
    <TextBlock x:Name="HostText" TextWrapping="Wrap" />
    <Border Background="#101010" Padding="10">
      <TextBlock x:Name="ReasonText" FontFamily="{x:Static controls:TerminalScreen.TerminalFont}" Foreground="#F0F0F0" TextWrapping="Wrap" />
    </Border>
    <CheckBox x:Name="RememberBox" Content="Always allow for this profile" />
    <StackPanel Orientation="Horizontal" HorizontalAlignment="Right" Spacing="8">
      <Button x:Name="ConnectAnywayButton" Content="Connect Anyway" Click="OnConnectAnywayClick" />
      <Button x:Name="CancelButton" Content="Cancel" IsDefault="True" IsCancel="True" Click="OnCancelClick" />
    </StackPanel>
  </StackPanel>
</Window>
```

`src/LizTerm.App/Views/CertificateWindow.axaml.cs`:

```csharp
using Avalonia.Controls;
using Avalonia.Interactivity;
using LizTerm.App.Dialogs;

namespace LizTerm.App.Views;

/// <summary>Spec 5.3. Cancel is the default and Escape maps to it; closing with the title bar also declines.
/// The result travels both as <see cref="Decision"/> and as the ShowDialog result.</summary>
public partial class CertificateWindow : Window
{
    public CertificateWindow() : this("", [], false) { }

    public CertificateWindow(string host, IReadOnlyList<string> reason, bool canRemember)
    {
        InitializeComponent();
        HostText.Text = $"{host} presented a certificate that could not be verified:";
        ReasonText.Text = string.Join("\n", reason);
        RememberBox.IsVisible = canRemember;
    }

    public CertificateDecision? Decision { get; private set; }

    internal void ConnectAnyway() => Finish(new CertificateDecision(true, RememberBox.IsChecked == true));

    private void OnConnectAnywayClick(object? sender, RoutedEventArgs e) => ConnectAnyway();

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Finish(CertificateDecision.Declined);

    private void Finish(CertificateDecision decision)
    {
        Decision = decision;
        Close(decision);
    }
}
```

`src/LizTerm.App/Dialogs/AvaloniaCertificatePrompt.cs`:

```csharp
using Avalonia.Controls;
using LizTerm.App.Views;

namespace LizTerm.App.Dialogs;

/// <summary>Opens <see cref="CertificateWindow"/> modally over the session window.</summary>
public sealed class AvaloniaCertificatePrompt(Window owner) : ICertificatePrompt
{
    public async Task<CertificateDecision> AskAsync(string host, IReadOnlyList<string> reason, bool canRemember)
    {
        var result = await new CertificateWindow(host, reason, canRemember).ShowDialog<CertificateDecision?>(owner);
        return result ?? CertificateDecision.Declined;
    }
}
```

- [ ] **Step 4: Wire the app**

In `App.axaml.cs`, change `OpenSession` to take `bool fromStore` and build the view model with the prompt and the save delegate:

```csharp
    /// <param name="fromStore">True for a saved profile, whose "Always allow" choice can be written back; false for
    /// an ad hoc command-line profile.</param>
    public void OpenSession(SessionProfile profile, bool fromStore)
    {
        var window = new SessionWindow();
        var store = _store ??= new ProfileStore(AppPaths.ProfilesDirectory());
        var viewModel = new SessionViewModel(
            SessionFactory.Create(profile),
            action => Dispatcher.UIThread.Post(action),
            new AvaloniaTextClipboard(window),
            new AvaloniaCertificatePrompt(window),
            fromStore ? store.Save : null);
        // ... rest unchanged
```

Add `using LizTerm.App.Dialogs;` and `using LizTerm.Core.Profiles;`. The startup call becomes `OpenSession(profile, fromStore: StartupArguments.Parse(desktop.Args ?? []).ProfileName is not null)` for now (Task 15 replaces this block), and `ShowPicker` passes `profile => OpenSession(profile, fromStore: true)`. Replace `ProfileStore.DefaultDirectory()` with `AppPaths.ProfilesDirectory()` in both places it appears.

- [ ] **Step 5: Run the App tests and the app**

Run: `dotnet test tests/LizTerm.App.Tests`
Expected: all pass.

Then a live check of the dialog against the TLS gateway with verification on (one minute):

```bash
dotnet build src/LizTerm.App && LIZTERM_B3270_PATH=/opt/homebrew/bin/b3270 dotnet run --project src/LizTerm.App --no-build -- L:129.212.188.194:4270
```

Expected: after the splash-less startup (splash comes in Task 15) the session window opens, the prompt appears with the self-signed reason and no checkbox (ad hoc), Escape shows the failure in the error bar, and Connect from the File menu then "Connect Anyway" reaches the gateway's login screen with "TLS, certificate not verified" in the status bar. Quit the app.

- [ ] **Step 6: Commit**

```bash
git add src/LizTerm.App tests/LizTerm.App.Tests
git commit -m "Add the certificate prompt window and wire it into the session window

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 12: Wire log toggle, Show Wire Logs, and the Help menu

**Files:**
- Create: `src/LizTerm.App/Files/IFolderOpener.cs`, `src/LizTerm.App/Files/AvaloniaFolderOpener.cs`, `tests/LizTerm.App.Tests/Fakes/FakeFolderOpener.cs`
- Modify: `src/LizTerm.App/ViewModels/SessionViewModel.cs`, `src/LizTerm.App/Views/SessionWindow.axaml`, `src/LizTerm.App/App.axaml.cs` (`OpenSession` passes the opener)
- Test: `tests/LizTerm.App.Tests/ViewModels/SessionViewModelWireLogTests.cs`, `tests/LizTerm.App.Tests/Views/SessionWindowTests.cs`

**Interfaces:**
- Consumes: `IEmulatorSession.WireLogPath/StartWireLog/StopWireLog` (Task 3), `StatusFormatter.WireLog` (Task 8), `AppPaths.LogsDirectory()` (Task 1).
- Produces: `IFolderOpener.OpenAsync(string directory)` returning `Task<bool>`; `SessionViewModel` constructor gains a sixth optional parameter `IFolderOpener? folderOpener = null`; properties `IsWireLogging` (two-way), `WireLogText`, `WireLogDirectory` (settable, defaults to `AppPaths.LogsDirectory()`), `ShowWireLogsCommand`; static `SessionViewModel.WireLogFileName(string profileName, DateTime now)`.

- [ ] **Step 1: Write the seam and the fake**

`src/LizTerm.App/Files/IFolderOpener.cs`:

```csharp
namespace LizTerm.App.Files;

/// <summary>Opens a directory in the OS file manager. Injected like <see cref="IFilePicker"/>.</summary>
public interface IFolderOpener
{
    /// <returns>False when the platform could not open it; the caller then shows the path instead.</returns>
    Task<bool> OpenAsync(string directory);
}
```

`src/LizTerm.App/Files/AvaloniaFolderOpener.cs`:

```csharp
using Avalonia.Controls;

namespace LizTerm.App.Files;

public sealed class AvaloniaFolderOpener(TopLevel topLevel) : IFolderOpener
{
    public Task<bool> OpenAsync(string directory) =>
        topLevel.Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(directory));
}
```

`tests/LizTerm.App.Tests/Fakes/FakeFolderOpener.cs`:

```csharp
using LizTerm.App.Files;

namespace LizTerm.App.Tests.Fakes;

public sealed class FakeFolderOpener : IFolderOpener
{
    public bool Result { get; set; } = true;
    public Exception? Exception { get; set; }
    public List<string> Opened { get; } = [];

    public Task<bool> OpenAsync(string directory)
    {
        Opened.Add(directory);
        return Exception is null ? Task.FromResult(Result) : Task.FromException<bool>(Exception);
    }
}
```

- [ ] **Step 2: Write the failing tests**

Create `tests/LizTerm.App.Tests/ViewModels/SessionViewModelWireLogTests.cs`:

```csharp
using System.Text.RegularExpressions;
using LizTerm.App.Tests.Fakes;
using LizTerm.App.ViewModels;

namespace LizTerm.App.Tests.ViewModels;

public class SessionViewModelWireLogTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lizterm-logs-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private (SessionViewModel Vm, FakeEmulatorSession Session, FakeFolderOpener Opener) Create()
    {
        var session = new FakeEmulatorSession();
        var opener = new FakeFolderOpener();
        var vm = new SessionViewModel(session, a => a(), new FakeTextClipboard(), folderOpener: opener) { WireLogDirectory = _dir };
        return (vm, session, opener);
    }

    [Fact]
    public void File_name_comes_from_the_profile_and_the_clock()
    {
        var name = SessionViewModel.WireLogFileName("MVS/CE: test", new DateTime(2026, 9, 5, 14, 3, 9));
        Assert.Equal("wire-MVS_CE__test-20260905-140309.log", name);
    }

    [Fact]
    public void Toggling_on_starts_a_log_in_the_directory_and_lights_the_indicator()
    {
        var (vm, session, _) = Create();
        Assert.False(vm.IsWireLogging);
        Assert.Equal("", vm.WireLogText);

        vm.IsWireLogging = true;
        Assert.True(Directory.Exists(_dir));
        var call = Assert.Single(session.Calls);
        Assert.Matches(new Regex("^wirelog:start:" + Regex.Escape(Path.Combine(_dir, "wire-Fake-")) + @"\d{8}-\d{6}\.log$"), call);
        Assert.Equal("● wire log", vm.WireLogText);
        Assert.Null(vm.ErrorMessage);

        vm.IsWireLogging = false;
        Assert.Equal("wirelog:stop", session.Calls[^1]);
        Assert.Equal("", vm.WireLogText);
        Assert.Null(session.WireLogPath);
    }

    [Fact]
    public void An_environment_log_shows_as_on_from_the_start()
    {
        var session = new FakeEmulatorSession { WireLogPath = "/tmp/env.log" };
        var vm = new SessionViewModel(session, a => a(), new FakeTextClipboard());
        Assert.True(vm.IsWireLogging);
        Assert.Equal("● wire log", vm.WireLogText);
        vm.IsWireLogging = false;
        Assert.Equal(["wirelog:stop"], session.Calls);
    }

    [Fact]
    public void An_unopenable_log_reports_and_stays_off()
    {
        var (vm, session, _) = Create();
        session.WireLogException = new IOException("disk full");
        vm.IsWireLogging = true;
        Assert.False(vm.IsWireLogging);
        Assert.Equal("Could not open the wire log: disk full", vm.ErrorMessage);
        Assert.Equal("", vm.WireLogText);
    }

    [Fact]
    public async Task Show_wire_logs_creates_and_opens_the_directory()
    {
        var (vm, _, opener) = Create();
        await vm.ShowWireLogsCommand.ExecuteAsync(null);
        Assert.True(Directory.Exists(_dir));
        Assert.Equal([_dir], opener.Opened);
        Assert.Null(vm.ErrorMessage);
    }

    [Fact]
    public async Task Show_wire_logs_falls_back_to_naming_the_path()
    {
        var (vm, _, opener) = Create();
        opener.Result = false;
        await vm.ShowWireLogsCommand.ExecuteAsync(null);
        Assert.Equal($"Could not open the logs folder. Wire logs are in {_dir}.", vm.ErrorMessage);

        opener.Exception = new InvalidOperationException("no launcher");
        await vm.ShowWireLogsCommand.ExecuteAsync(null);
        Assert.Equal($"Could not open the logs folder. Wire logs are in {_dir}.", vm.ErrorMessage);
    }
}
```

Add to `tests/LizTerm.App.Tests/Views/SessionWindowTests.cs`:

```csharp
    [AvaloniaFact]
    public void Help_menu_has_the_wire_log_toggle_bound_to_the_view_model()
    {
        var (window, _, vm, _, _) = Show();
        var item = window.FindControl<MenuItem>("WireLogMenuItem")!;
        Assert.Equal(MenuItemToggleType.CheckBox, item.ToggleType);
        Assert.False(item.IsChecked);
        vm.IsWireLogging = true;
        Assert.True(item.IsChecked);
        Assert.Equal("● wire log", window.FindControl<TextBlock>("WireLogStatus")!.Text);
    }
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~WireLog"`
Expected: build errors (`folderOpener`, `WireLogDirectory`, `IsWireLogging`).

- [ ] **Step 4: Implement the view model**

Constructor: add `IFolderOpener? folderOpener = null` after `saveProfile`, store it in `_folderOpener`, and at the end of the constructor set `_isWireLogging = session.WireLogPath is not null; WireLogText = StatusFormatter.WireLog(_isWireLogging);`. Add:

```csharp
    private readonly IFolderOpener? _folderOpener;

    /// <summary>Where new wire logs go; the app uses the per-OS logs folder, tests a temp directory.</summary>
    public string WireLogDirectory { get; set; } = AppPaths.LogsDirectory();

    /// <summary>Help > Wire Log. On starts a new timestamped log for this session; off closes it.</summary>
    [ObservableProperty] private bool _isWireLogging;

    [ObservableProperty] private string _wireLogText = "";

    public static string WireLogFileName(string profileName, DateTime now)
    {
        var safe = new string(profileName.Select(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-' ? c : '_').ToArray());
        return $"wire-{safe}-{now:yyyyMMdd-HHmmss}.log";
    }

    partial void OnIsWireLoggingChanged(bool value)
    {
        var active = _session.WireLogPath is not null;
        if (value && !active)
        {
            try
            {
                Directory.CreateDirectory(WireLogDirectory);
                _session.StartWireLog(Path.Combine(WireLogDirectory, WireLogFileName(Profile.Name, DateTime.Now)));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                ErrorMessage = "Could not open the wire log: " + ex.Message;
                IsWireLogging = false;
                return;
            }
        }
        else if (!value && active)
        {
            _session.StopWireLog();
        }
        WireLogText = StatusFormatter.WireLog(_session.WireLogPath is not null);
    }

    [RelayCommand]
    private async Task ShowWireLogsAsync()
    {
        var directory = WireLogDirectory;
        try
        {
            Directory.CreateDirectory(directory);
            if (_folderOpener is not null && await _folderOpener.OpenAsync(directory)) return;
        }
        catch (Exception)
        {
            // Fall through to naming the path.
        }
        ErrorMessage = $"Could not open the logs folder. Wire logs are in {directory}.";
    }
```

Add `using LizTerm.Core.Profiles;`. (`IsWireLogging = false` inside the handler re-enters `OnIsWireLoggingChanged(false)` with `active` false, which takes neither branch and refreshes the text.)

- [ ] **Step 5: The window**

In `SessionWindow.axaml` add after the Keys menu:

```xml
      <MenuItem Header="_Help">
        <MenuItem x:Name="WireLogMenuItem" Header="_Wire Log" ToggleType="CheckBox" IsChecked="{Binding IsWireLogging, Mode=TwoWay}" />
        <MenuItem Header="Show Wire _Logs..." Command="{Binding ShowWireLogsCommand}" />
        <Separator />
        <MenuItem Header="_About LizTerm..." Click="OnAboutClick" />
      </MenuItem>
```

Change the status bar grid to `ColumnDefinitions="Auto,16,Auto,*,Auto,16,Auto,16,Auto,16,Auto,16,Auto"` and add after the model text:

```xml
        <TextBlock x:Name="WireLogStatus" Grid.Column="12" Text="{Binding WireLogText}" FontFamily="{x:Static controls:TerminalScreen.TerminalFont}" Foreground="#FF8080" />
```

In `SessionWindow.axaml.cs` add a placeholder handler that Task 13 fills in:

```csharp
    private void OnAboutClick(object? sender, RoutedEventArgs e) { }
```

In `App.OpenSession` pass `new AvaloniaFolderOpener(window)` as the sixth constructor argument (add `using LizTerm.App.Files;`).

- [ ] **Step 6: Run the App tests**

Run: `dotnet test tests/LizTerm.App.Tests`
Expected: all pass.

- [ ] **Step 7: Commit**

```bash
git add src/LizTerm.App tests/LizTerm.App.Tests
git commit -m "Add the Help menu with a live Wire Log toggle, Show Wire Logs, and a status bar indicator

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 13: About box with version, engine provenance, and third-party notices

**Files:**
- Create: `src/LizTerm.App/AppVersion.cs`, `src/LizTerm.App/Views/AboutWindow.axaml`, `src/LizTerm.App/Views/AboutWindow.axaml.cs`, `THIRD-PARTY-NOTICES.txt`
- Modify: `Directory.Build.props`, `src/LizTerm.App/LizTerm.App.csproj`, `src/LizTerm.App/SessionFactory.cs` (`OverrideOrigin`), `src/LizTerm.App/ViewModels/SessionViewModel.cs` (`Engine`), `src/LizTerm.App/Views/SessionWindow.axaml.cs` (`OnAboutClick`)
- Test: `tests/LizTerm.App.Tests/AppVersionTests.cs`, `tests/LizTerm.App.Tests/Views/AboutWindowTests.cs`

**Interfaces:**
- Consumes: `StatusFormatter.Engine` (Task 8), `IEmulatorSession.Engine` (Task 4).
- Produces: `AppVersion.Current` (string, no `+hash`); `SessionFactory.OverrideOrigin` (`"LIZTERM_B3270_PATH"`); `SessionViewModel.Engine`; `AboutWindow(string version, EngineInfo engine, string overrideOrigin)` with named controls `VersionText`, `EngineText`, `EnginePathText`, `NoticesText`.

- [ ] **Step 1: Write the failing tests**

`tests/LizTerm.App.Tests/AppVersionTests.cs`:

```csharp
using System.Text.RegularExpressions;

namespace LizTerm.App.Tests;

public class AppVersionTests
{
    [Fact]
    public void Version_is_a_plain_three_part_number()
    {
        Assert.Matches(new Regex(@"^\d+\.\d+\.\d+$"), AppVersion.Current);
        Assert.DoesNotContain("+", AppVersion.Current);
    }
}
```

`tests/LizTerm.App.Tests/Views/AboutWindowTests.cs`:

```csharp
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using LizTerm.App.Views;
using LizTerm.Core.Session;

namespace LizTerm.App.Tests.Views;

public class AboutWindowTests
{
    [AvaloniaFact]
    public void Shows_version_engine_and_notices()
    {
        var engine = new EngineInfo("b3270", "4.5.6 (fake)", "/opt/homebrew/bin/b3270", EngineSource.Override);
        var window = new AboutWindow("0.3.0", engine, "LIZTERM_B3270_PATH");
        window.Show();
        Assert.Equal("Version 0.3.0", window.FindControl<TextBlock>("VersionText")!.Text);
        Assert.Equal("b3270 4.5.6 (fake), from LIZTERM_B3270_PATH", window.FindControl<TextBlock>("EngineText")!.Text);
        Assert.Equal("/opt/homebrew/bin/b3270", window.FindControl<TextBlock>("EnginePathText")!.Text);
        var notices = window.FindControl<TextBox>("NoticesText")!.Text!;
        Assert.Contains("Paul Mattes", notices);
        Assert.Contains("3270font", notices);
        Assert.DoesNotContain("LizTerm is licensed", notices);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~AppVersionTests|FullyQualifiedName~AboutWindowTests"`
Expected: build errors.

- [ ] **Step 3: Version and notices**

Add `<Version>0.3.0</Version>` to the `PropertyGroup` in `Directory.Build.props`.

`src/LizTerm.App/AppVersion.cs`:

```csharp
using System.Reflection;

namespace LizTerm.App;

/// <summary>The app's version as the build stamped it (the Version property in Directory.Build.props), without the
/// "+commit" suffix the SDK appends to the informational version inside a git checkout.</summary>
public static class AppVersion
{
    public static string Current { get; } = Read();

    private static string Read()
    {
        var informational = typeof(AppVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var version = string.IsNullOrWhiteSpace(informational) ? typeof(AppVersion).Assembly.GetName().Version?.ToString(3) ?? "0.0.0" : informational;
        var plus = version.IndexOf('+');
        return plus >= 0 ? version[..plus] : version;
    }
}
```

`THIRD-PARTY-NOTICES.txt` at the repository root: a heading line `LizTerm bundles the following third-party software.`, a blank line, the heading `x3270 (b3270), BSD-3-Clause`, then the copyright and license text exactly as b3270 prints it in its hello (the `copyright` string in line 1 of `tests/LizTerm.Backend.B3270.Tests/Fixtures/gateway-login-tls.jsonl`, with `\n` turned into line breaks), a blank line, the heading `IBM 3270 font`, and the full contents of `src/LizTerm.App/Assets/Fonts/LICENSE-3270font.txt`. Build it with:

```bash
{ printf 'LizTerm bundles the following third-party software.\n\nx3270 (b3270), BSD-3-Clause\n\n'; python3 -c "import json,sys; print(json.loads(open('tests/LizTerm.Backend.B3270.Tests/Fixtures/gateway-login-tls.jsonl').readline())['initialize'][0]['hello']['copyright'])"; printf '\nIBM 3270 font\n\n'; cat src/LizTerm.App/Assets/Fonts/LICENSE-3270font.txt; } > THIRD-PARTY-NOTICES.txt
```

In `src/LizTerm.App/LizTerm.App.csproj` add to the `AvaloniaResource` item group:

```xml
    <AvaloniaResource Include="../../THIRD-PARTY-NOTICES.txt" Link="Assets/THIRD-PARTY-NOTICES.txt" />
```

- [ ] **Step 4: The window**

`src/LizTerm.App/Views/AboutWindow.axaml`:

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:controls="using:LizTerm.App.Controls"
        x:Class="LizTerm.App.Views.AboutWindow"
        Title="About LizTerm" Width="560" Height="480" CanResize="False"
        WindowStartupLocation="CenterOwner">
  <DockPanel Margin="16">
    <TextBlock DockPanel.Dock="Top" Text="LizTerm" FontFamily="{x:Static controls:TerminalScreen.TerminalFont}" FontSize="40" Foreground="#50FF50" />
    <TextBlock DockPanel.Dock="Top" x:Name="VersionText" Margin="0,4,0,0" />
    <TextBlock DockPanel.Dock="Top" x:Name="EngineText" Margin="0,12,0,0" />
    <TextBlock DockPanel.Dock="Top" x:Name="EnginePathText" Foreground="#A0A0A0" FontSize="12" TextWrapping="Wrap" />
    <TextBlock DockPanel.Dock="Top" Text="Third-party notices" Margin="0,16,0,4" FontWeight="SemiBold" />
    <Button DockPanel.Dock="Bottom" Content="Close" HorizontalAlignment="Right" Margin="0,12,0,0" IsDefault="True" IsCancel="True" Click="OnCloseClick" />
    <TextBox x:Name="NoticesText" IsReadOnly="True" AcceptsReturn="True" TextWrapping="Wrap" FontSize="11" />
  </DockPanel>
</Window>
```

`src/LizTerm.App/Views/AboutWindow.axaml.cs`:

```csharp
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform;
using LizTerm.App.Status;
using LizTerm.Core.Session;

namespace LizTerm.App.Views;

public partial class AboutWindow : Window
{
    private static readonly Uri NoticesUri = new("avares://LizTerm.App/Assets/THIRD-PARTY-NOTICES.txt");

    public AboutWindow() : this(AppVersion.Current, new EngineInfo("b3270", null, "", EngineSource.Bundled), "") { }

    public AboutWindow(string version, EngineInfo engine, string overrideOrigin)
    {
        InitializeComponent();
        VersionText.Text = "Version " + version;
        EngineText.Text = StatusFormatter.Engine(engine, overrideOrigin);
        EnginePathText.Text = engine.Path;
        using var stream = AssetLoader.Open(NoticesUri);
        using var reader = new StreamReader(stream);
        NoticesText.Text = reader.ReadToEnd();
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
```

`SessionFactory`: add `public static string OverrideOrigin => B3270Locator.EnvironmentOverride;`. `SessionViewModel`: add `public EngineInfo Engine => _session.Engine;`. `SessionWindow.axaml.cs`, replace the placeholder:

```csharp
    private async void OnAboutClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } vm) return;
        try
        {
            await new AboutWindow(AppVersion.Current, vm.Engine, SessionFactory.OverrideOrigin).ShowDialog(this);
        }
        catch (Exception ex)
        {
            vm.ErrorMessage = "Could not open About: " + ex.Message;
        }
        Screen.Focus();
    }
```

- [ ] **Step 5: Run the suite**

Run: `dotnet test LizTerm.slnx`
Expected: all pass.

- [ ] **Step 6: Commit**

```bash
git add Directory.Build.props THIRD-PARTY-NOTICES.txt src tests
git commit -m "Add About with the version, the engine and where it came from, and third-party notices

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 14: SplashTiming and StartupPlan

**Files:**
- Create: `src/LizTerm.App/Startup/SplashTiming.cs`, `src/LizTerm.App/Startup/StartupPlan.cs`
- Test: `tests/LizTerm.App.Tests/Startup/SplashTimingTests.cs`, `tests/LizTerm.App.Tests/Startup/StartupPlanTests.cs`

**Interfaces:**
- Consumes: `StartupArguments` (Task 7).
- Produces: `SplashTiming(TimeSpan minimum, TimeSpan maximum)` with `Default` (1 s, 2.5 s) and `DateTime CloseAt(DateTime shownAt, DateTime? dismissRequestedAt)`; `StartupPlan` with nested records `ShowError(string Message)`, `OpenSession(SessionProfile Profile, bool FromStore)`, `OpenPicker`, and `static StartupPlan Decide(string? backendError, StartupArguments arguments, IReadOnlyList<SessionProfile> profiles)`.

- [ ] **Step 1: Write the failing tests**

`tests/LizTerm.App.Tests/Startup/SplashTimingTests.cs`:

```csharp
using LizTerm.App.Startup;

namespace LizTerm.App.Tests.Startup;

public class SplashTimingTests
{
    private static readonly DateTime Shown = new(2026, 9, 5, 12, 0, 0);
    private static readonly SplashTiming Timing = SplashTiming.Default;

    [Fact]
    public void Defaults_are_one_and_two_and_a_half_seconds()
    {
        Assert.Equal(TimeSpan.FromSeconds(1), Timing.Minimum);
        Assert.Equal(TimeSpan.FromSeconds(2.5), Timing.Maximum);
    }

    [Fact]
    public void Without_a_dismissal_it_closes_at_the_maximum() =>
        Assert.Equal(Shown.AddSeconds(2.5), Timing.CloseAt(Shown, null));

    [Fact]
    public void An_early_dismissal_waits_for_the_minimum() =>
        Assert.Equal(Shown.AddSeconds(1), Timing.CloseAt(Shown, Shown.AddMilliseconds(200)));

    [Fact]
    public void A_late_dismissal_closes_at_once() =>
        Assert.Equal(Shown.AddSeconds(1.7), Timing.CloseAt(Shown, Shown.AddSeconds(1.7)));
}
```

`tests/LizTerm.App.Tests/Startup/StartupPlanTests.cs`:

```csharp
using LizTerm.App.Startup;
using LizTerm.Core.Session;

namespace LizTerm.App.Tests.Startup;

public class StartupPlanTests
{
    private static readonly SessionProfile[] Profiles = [new() { Name = "TK5", Host = "mvs.local" }];

    [Fact]
    public void Backend_error_wins_over_everything()
    {
        var plan = StartupPlan.Decide("b3270 missing", StartupArguments.Parse(["TK5"]), Profiles);
        Assert.Equal(new StartupPlan.ShowError("b3270 missing"), plan);
    }

    [Fact]
    public void A_resolved_argument_opens_a_session()
    {
        var plan = Assert.IsType<StartupPlan.OpenSession>(StartupPlan.Decide(null, StartupArguments.Parse(["tk5"]), Profiles));
        Assert.Same(Profiles[0], plan.Profile);
        Assert.True(plan.FromStore);

        var adHoc = Assert.IsType<StartupPlan.OpenSession>(StartupPlan.Decide(null, StartupArguments.Parse(["L:mvs.local"]), Profiles));
        Assert.Equal("mvs.local:992", adHoc.Profile.Name);
        Assert.False(adHoc.FromStore);
    }

    [Theory]
    [InlineData("")]
    [InlineData("missing")]
    [InlineData("X:bad")]
    public void Otherwise_the_picker_opens(string arg)
    {
        var args = arg.Length == 0 ? Array.Empty<string>() : [arg];
        Assert.Equal(new StartupPlan.OpenPicker(), StartupPlan.Decide(null, StartupArguments.Parse(args), Profiles));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~SplashTimingTests|FullyQualifiedName~StartupPlanTests"`
Expected: build errors.

- [ ] **Step 3: Implement**

`src/LizTerm.App/Startup/SplashTiming.cs`:

```csharp
namespace LizTerm.App.Startup;

/// <summary>When the splash closes: at the first click or key, but never before <see cref="Minimum"/> after it
/// was shown, and at <see cref="Maximum"/> with no input at all (spec 7).</summary>
public sealed class SplashTiming(TimeSpan minimum, TimeSpan maximum)
{
    public static readonly SplashTiming Default = new(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2.5));

    public TimeSpan Minimum { get; } = minimum;
    public TimeSpan Maximum { get; } = maximum;

    public DateTime CloseAt(DateTime shownAt, DateTime? dismissRequestedAt)
    {
        if (dismissRequestedAt is not { } requested) return shownAt + Maximum;
        var earliest = shownAt + Minimum;
        return requested > earliest ? requested : earliest;
    }
}
```

`src/LizTerm.App/Startup/StartupPlan.cs`:

```csharp
using LizTerm.Core.Session;

namespace LizTerm.App.Startup;

/// <summary>What the app opens once the splash has closed. Pure so it can be tested without windows.</summary>
public abstract record StartupPlan
{
    public sealed record ShowError(string Message) : StartupPlan;
    /// <param name="FromStore">True when the profile is a saved one, whose certificate choice can be written back.</param>
    public sealed record OpenSession(SessionProfile Profile, bool FromStore) : StartupPlan;
    public sealed record OpenPicker : StartupPlan;

    public static StartupPlan Decide(string? backendError, StartupArguments arguments, IReadOnlyList<SessionProfile> profiles)
    {
        if (backendError is not null) return new ShowError(backendError);
        var profile = arguments.Resolve(profiles);
        if (profile is null) return new OpenPicker();
        return new OpenSession(profile, FromStore: arguments.ProfileName is not null);
    }
}
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~SplashTimingTests|FullyQualifiedName~StartupPlanTests"`
Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add src/LizTerm.App/Startup tests/LizTerm.App.Tests/Startup
git commit -m "Add the pure splash timing and startup plan

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 15: Splash window, startup error window, and the startup flow

**Files:**
- Create: `src/LizTerm.App/Views/SplashWindow.axaml`, `src/LizTerm.App/Views/SplashWindow.axaml.cs`, `src/LizTerm.App/Views/StartupErrorWindow.axaml`, `src/LizTerm.App/Views/StartupErrorWindow.axaml.cs`
- Modify: `src/LizTerm.App/SessionFactory.cs` (`CheckBackend`), `src/LizTerm.App/App.axaml.cs` (`OnFrameworkInitializationCompleted`)
- Test: `tests/LizTerm.App.Tests/Views/SplashWindowTests.cs`

**Interfaces:**
- Consumes: `SplashTiming`, `StartupPlan` (Task 14), `AppVersion` (Task 13), `B3270Locator.Find()` (Task 2).
- Produces: `SplashWindow(string version, SplashTiming timing)` with named `SplashMark` and `VersionText`; `StartupErrorWindow(string message)`; `SessionFactory.CheckBackend()` returning `EngineInfo` or throwing `BackendUnavailableException`.

- [ ] **Step 1: Write the failing test**

`tests/LizTerm.App.Tests/Views/SplashWindowTests.cs`:

```csharp
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using LizTerm.App.Startup;
using LizTerm.App.Views;

namespace LizTerm.App.Tests.Views;

public class SplashWindowTests
{
    [AvaloniaFact]
    public void Shows_the_mark_and_version_without_decorations()
    {
        var window = new SplashWindow("0.3.0", new SplashTiming(TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(2)));
        window.Show();
        Assert.Equal(SystemDecorations.None, window.SystemDecorations);
        Assert.Equal(480d, window.Width);
        Assert.Equal(300d, window.Height);
        Assert.Equal("Version 0.3.0", window.FindControl<TextBlock>("VersionText")!.Text);
        Assert.NotNull(window.FindControl<ContentControl>("SplashMark"));
        window.Close();
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~SplashWindowTests"`
Expected: build error.

- [ ] **Step 3: The windows**

`src/LizTerm.App/Views/SplashWindow.axaml`:

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:controls="using:LizTerm.App.Controls"
        x:Class="LizTerm.App.Views.SplashWindow"
        Title="LizTerm" Width="480" Height="300" CanResize="False"
        SystemDecorations="None" WindowStartupLocation="CenterScreen" Background="Black"
        ShowInTaskbar="False" Topmost="True">
  <!-- The mark. To replace it with an image later, put the file at Assets/Splash/liz.png and swap the
       StackPanel below for <Image Source="avares://LizTerm.App/Assets/Splash/liz.png" Stretch="Uniform" />,
       keeping the 480x300 window; the version line moves below or over the image as the picture allows. -->
  <Grid RowDefinitions="*,Auto" Margin="24">
    <ContentControl x:Name="SplashMark" HorizontalAlignment="Center" VerticalAlignment="Center">
      <StackPanel>
        <TextBlock Text="LizTerm" FontFamily="{x:Static controls:TerminalScreen.TerminalFont}" FontSize="72" Foreground="#50FF50" HorizontalAlignment="Center" />
        <TextBlock Text="TN3270 terminal" FontFamily="{x:Static controls:TerminalScreen.TerminalFont}" FontSize="20" Foreground="#50FF50" HorizontalAlignment="Center" />
      </StackPanel>
    </ContentControl>
    <TextBlock Grid.Row="1" x:Name="VersionText" FontFamily="{x:Static controls:TerminalScreen.TerminalFont}" FontSize="16" Foreground="#A0A0A0" HorizontalAlignment="Center" />
  </Grid>
</Window>
```

`src/LizTerm.App/Views/SplashWindow.axaml.cs`:

```csharp
using Avalonia.Controls;
using Avalonia.Threading;
using LizTerm.App.Startup;

namespace LizTerm.App.Views;

/// <summary>Spec 7: closes on the first click or key, never before the minimum, and at the maximum on its own.</summary>
public partial class SplashWindow : Window
{
    private readonly SplashTiming _timing;
    private readonly DateTime _shownAt = DateTime.UtcNow;
    private readonly DispatcherTimer _timer = new();
    private bool _dismissed;

    public SplashWindow() : this(AppVersion.Current, SplashTiming.Default) { }

    public SplashWindow(string version, SplashTiming timing)
    {
        InitializeComponent();
        _timing = timing;
        VersionText.Text = "Version " + version;
        _timer.Tick += (_, _) => { _timer.Stop(); Close(); };
        Opened += (_, _) => Arm(null);
        PointerPressed += (_, _) => Dismiss();
        KeyDown += (_, _) => Dismiss();
    }

    private void Dismiss()
    {
        if (_dismissed) return;
        _dismissed = true;
        Arm(DateTime.UtcNow);
    }

    private void Arm(DateTime? dismissRequestedAt)
    {
        _timer.Stop();
        var delay = _timing.CloseAt(_shownAt, dismissRequestedAt) - DateTime.UtcNow;
        if (delay <= TimeSpan.Zero)
        {
            Close();
            return;
        }
        _timer.Interval = delay;
        _timer.Start();
    }
}
```

`src/LizTerm.App/Views/StartupErrorWindow.axaml`:

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        x:Class="LizTerm.App.Views.StartupErrorWindow"
        Title="LizTerm cannot start" Width="560" SizeToContent="Height" CanResize="False"
        WindowStartupLocation="CenterScreen">
  <StackPanel Margin="16" Spacing="12">
    <TextBlock Text="The emulator engine could not be used." FontWeight="SemiBold" />
    <TextBox x:Name="MessageText" IsReadOnly="True" AcceptsReturn="True" TextWrapping="Wrap" />
    <Button Content="Quit" HorizontalAlignment="Right" IsDefault="True" IsCancel="True" Click="OnQuitClick" />
  </StackPanel>
</Window>
```

`src/LizTerm.App/Views/StartupErrorWindow.axaml.cs`:

```csharp
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace LizTerm.App.Views;

/// <summary>Spec 7: when the engine is missing or not executable the splash gives way to this, not the picker.</summary>
public partial class StartupErrorWindow : Window
{
    public StartupErrorWindow() : this("") { }

    public StartupErrorWindow(string message)
    {
        InitializeComponent();
        MessageText.Text = message;
    }

    private void OnQuitClick(object? sender, RoutedEventArgs e) => Close();
}
```

- [ ] **Step 4: The startup flow**

`SessionFactory`: add

```csharp
    /// <summary>Locates the engine without starting it. Throws <see cref="BackendUnavailableException"/> with the
    /// locator's explanation when it is missing or not executable.</summary>
    public static EngineInfo CheckBackend()
    {
        var location = B3270Locator.Find();
        return new EngineInfo("b3270", null, location.Path, location.Source);
    }
```

In `App.axaml.cs` replace `OnFrameworkInitializationCompleted`:

```csharp
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Closing the last session window returns to the picker; only Quit ends the process.
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            var splash = new SplashWindow();
            splash.Show();
            splash.Activate();

            _store = new ProfileStore(AppPaths.ProfilesDirectory());
            string? backendError = null;
            try
            {
                SessionFactory.CheckBackend();
            }
            catch (BackendUnavailableException ex)
            {
                backendError = ex.Message;
            }
            var arguments = StartupArguments.Parse(desktop.Args ?? []);
            if (arguments.Error is not null) Console.Error.WriteLine(arguments.Error);
            var plan = StartupPlan.Decide(backendError, arguments, _store.LoadAll());

            // Nothing else opens until the splash has closed, so no window renders under it.
            splash.Closed += (_, _) => Execute(plan);
        }
        base.OnFrameworkInitializationCompleted();
    }

    private void Execute(StartupPlan plan)
    {
        switch (plan)
        {
            case StartupPlan.ShowError error:
                var window = new StartupErrorWindow(error.Message);
                window.Closed += (_, _) => Quit();
                window.Show();
                break;
            case StartupPlan.OpenSession open:
                OpenSession(open.Profile, open.FromStore);
                break;
            default:
                ShowPicker();
                break;
        }
    }
```

Remove the earlier temporary `OpenSession(profile, fromStore: ...)` startup line from Task 11.

- [ ] **Step 5: Run the suite and the app**

Run: `dotnet test LizTerm.slnx`
Expected: all pass.

Then, without the engine override, confirm the error path (this worktree has no bundled b3270):

```bash
dotnet build src/LizTerm.App && dotnet run --project src/LizTerm.App --no-build
```

Expected: the splash for up to 2.5 s (a click closes it after 1 s), then "LizTerm cannot start" listing the paths looked in; Quit exits. Then with `LIZTERM_B3270_PATH=/opt/homebrew/bin/b3270` the splash gives way to the picker.

- [ ] **Step 6: Commit**

```bash
git add src/LizTerm.App tests/LizTerm.App.Tests
git commit -m "Show a splash first, check the engine behind it, and open the error, session, or picker when it closes

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 16: Blinking cells

**Files:**
- Modify: `src/LizTerm.App/Controls/TerminalScreen.cs`
- Test: `tests/LizTerm.App.Tests/Controls/TerminalScreenBlinkTests.cs`

**Interfaces:**
- Produces: `TerminalScreen.BlinkInterval` (`static readonly TimeSpan`, 750 ms); `internal bool BlinkTimerRunning`; `internal bool BlinkHidden`.

- [ ] **Step 1: Write the failing tests**

`tests/LizTerm.App.Tests/Controls/TerminalScreenBlinkTests.cs`:

```csharp
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using LizTerm.App.Controls;
using LizTerm.Core.Screen;

namespace LizTerm.App.Tests.Controls;

public class TerminalScreenBlinkTests
{
    private static ScreenSnapshot WithBlink()
    {
        var buffer = new ScreenBuffer(24, 80);
        buffer.SetText(1, 2, "ALERT", HostColor.Red, null, CellRendition.Blink);
        return buffer.Snapshot();
    }

    [AvaloniaFact]
    public void Interval_is_photosensitive_safe()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(750), TerminalScreen.BlinkInterval);
        Assert.True(TerminalScreen.BlinkInterval >= TimeSpan.FromMilliseconds(500), "WCAG 2.3.1: never more than three flashes per second");
    }

    [AvaloniaFact]
    public void Timer_runs_only_while_the_snapshot_has_blinking_cells()
    {
        var screen = new TerminalScreen { Snapshot = ScreenSnapshot.Empty(24, 80) };
        var window = new Window { Width = 800, Height = 600, Content = screen };
        window.Show();
        Assert.False(screen.BlinkTimerRunning);

        screen.Snapshot = WithBlink();
        Assert.True(screen.BlinkTimerRunning);

        screen.Snapshot = ScreenSnapshot.Empty(24, 80);
        Assert.False(screen.BlinkTimerRunning);
        Assert.False(screen.BlinkHidden);
    }

    [AvaloniaFact]
    public void Leaving_the_tree_stops_the_timer_and_returning_restarts_it()
    {
        var screen = new TerminalScreen { Snapshot = WithBlink() };
        var window = new Window { Width = 800, Height = 600, Content = screen };
        window.Show();
        Assert.True(screen.BlinkTimerRunning);

        window.Content = null;
        Assert.False(screen.BlinkTimerRunning);

        window.Content = screen;
        Assert.True(screen.BlinkTimerRunning);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~TerminalScreenBlinkTests"`
Expected: build errors.

- [ ] **Step 3: Implement**

In `TerminalScreen` add `using Avalonia.Threading;` and the members:

```csharp
    /// <summary>Half a blink cycle. 750 ms is two flashes every three seconds, well under WCAG 2.3.1's limit of
    /// three per second; never take it below 500 ms without revisiting that (spec section 8).</summary>
    public static readonly TimeSpan BlinkInterval = TimeSpan.FromMilliseconds(750);

    private readonly DispatcherTimer _blinkTimer = new() { Interval = BlinkInterval };
    private bool _attached;

    internal bool BlinkTimerRunning => _blinkTimer.IsEnabled;
    /// <summary>True during the phase in which blinking text is not drawn.</summary>
    internal bool BlinkHidden { get; private set; }

    public TerminalScreen()
    {
        _blinkTimer.Tick += (_, _) =>
        {
            BlinkHidden = !BlinkHidden;
            InvalidateVisual();
        };
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        UpdateBlinkTimer(Snapshot);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _attached = false;
        UpdateBlinkTimer(null);
    }

    private void UpdateBlinkTimer(ScreenSnapshot? snapshot)
    {
        var wanted = _attached && snapshot is not null && HasBlink(snapshot);
        if (wanted == _blinkTimer.IsEnabled) return;
        if (wanted)
        {
            _blinkTimer.Start();
        }
        else
        {
            _blinkTimer.Stop();
            BlinkHidden = false;
        }
    }

    private static bool HasBlink(ScreenSnapshot snapshot)
    {
        for (var row = 0; row < snapshot.Rows; row++)
            foreach (var cell in snapshot.Row(row))
                if (cell.Rendition.HasFlag(CellRendition.Blink)) return true;
        return false;
    }
```

Replace `OnPropertyChanged` so the blink update runs for every snapshot change and the selection rule stays as it was:

```csharp
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != SnapshotProperty) return;
        var (oldValue, newValue) = change.GetOldAndNewValue<ScreenSnapshot?>();
        UpdateBlinkTimer(newValue);
        if (Selection is null) return;
        if (oldValue is null || newValue is null || oldValue.Rows != newValue.Rows || oldValue.Columns != newValue.Columns)
            Selection = null;
    }
``` In `DrawRun`, after computing `bg`, add `var hidden = BlinkHidden && style.Rendition.HasFlag(CellRendition.Blink);` and wrap the text draw in `if (!hidden && !string.IsNullOrWhiteSpace(text))` and the underline draw in `if (!hidden && style.Rendition.HasFlag(CellRendition.Underline))`.

- [ ] **Step 4: Run the App tests**

Run: `dotnet test tests/LizTerm.App.Tests`
Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add src/LizTerm.App/Controls tests/LizTerm.App.Tests/Controls
git commit -m "Blink cells the host marks, at a photosensitive-safe 750 ms phase

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 17: Integration lane and as-built documentation

**Files:**
- Modify: `tests/LizTerm.Integration.Tests/LiveHostTests.cs`, `CLAUDE.md`, `docs/superpowers/specs/2026-09-05-lizterm-m2-polish-design.md`

- [ ] **Step 1: Add the live tests**

Add to `LiveHostTests`:

```csharp
    /// <summary>Needs a TLS host whose certificate cannot be verified (LIZTERM_TEST_TLS=1, LIZTERM_TEST_VERIFY_CERT=0);
    /// connects with verification forced on and expects the flagged failure.</summary>
    [Fact]
    public async Task Verify_on_connect_to_a_self_signed_host_is_flagged()
    {
        var target = Environment.GetEnvironmentVariable("LIZTERM_TEST_HOST");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(target), "LIZTERM_TEST_HOST is not set");
        var profile = ProfileFor(target!);
        Assert.SkipUnless(profile is { UseTls: true, VerifyCertificate: false }, "needs LIZTERM_TEST_TLS=1 and LIZTERM_TEST_VERIFY_CERT=0");
        await using var session = new B3270Session(profile, () => new B3270ChildProcess(B3270Locator.Find().Path), WireLog.TryFromEnvironment(out _));
        var ex = await Assert.ThrowsAsync<ConnectionFailedException>(() =>
            session.ConnectAsync(new ConnectOptions(VerifyCertificate: true), TestContext.Current.CancellationToken));
        Assert.True(ex.CertificateVerificationFailed, string.Join(" | ", ex.Lines));
        Assert.Equal(ConnectionState.Disconnected, session.ConnectionState);
    }

    /// <summary>A plain connect to a TLS listener never completes; the token must end it and leave the session usable.</summary>
    [Fact]
    public async Task Plain_connect_to_a_tls_port_is_cancelled_by_the_token_and_the_session_recovers()
    {
        var target = Environment.GetEnvironmentVariable("LIZTERM_TEST_HOST");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(target), "LIZTERM_TEST_HOST is not set");
        var profile = ProfileFor(target!);
        Assert.SkipUnless(profile.UseTls, "needs LIZTERM_TEST_TLS=1");
        await using var session = new B3270Session(profile with { UseTls = false }, () => new B3270ChildProcess(B3270Locator.Find().Path), WireLog.TryFromEnvironment(out _));
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(2));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.ConnectAsync(cancellationToken: cts.Token));
            Assert.Equal(ConnectionState.Disconnected, session.ConnectionState);
        }
        Assert.NotNull(session.Engine.Version);
    }
```

Run them against the gateway (the variables live in `~/.config/lizterm-test.env`; source it in the shell, then override the host for the TLS gateway if the file points at MVS/CE):

```bash
source ~/.config/lizterm-test.env; LIZTERM_TEST_HOST=129.212.188.194:4270 LIZTERM_TEST_TLS=1 LIZTERM_TEST_VERIFY_CERT=0 LIZTERM_B3270_PATH=/opt/homebrew/bin/b3270 dotnet test tests/LizTerm.Integration.Tests --filter "FullyQualifiedName~Verify_on_connect|FullyQualifiedName~Plain_connect"
```

Expected: both pass. Without the variables, `dotnet test LizTerm.slnx` now reports four skipped integration tests.

- [ ] **Step 2: Bring CLAUDE.md to as-built**

In `CLAUDE.md`:

- In the b3270 binary paragraph, after the `LIZTERM_WIRE_LOG` description, add: "The same log can be started from Help > Wire Log in a session window; files go to `<config>/logs/wire-<profile>-<timestamp>.log`, and Show Wire Logs opens that folder." Change the sentence "the fault message tells users to set it" to "the fault message points users at Help > Wire Log".
- In the environment variables sentence, note that the integration project now reports four skipped tests without a host.
- In the Core model bullets add: "`ConnectAsync(ConnectOptions?, CancellationToken)`: a cancelled token ends the attempt with `OperationCanceledException` (the backend sends one `Disconnect`; b3270 then fails the pending Connect run, which is not reported) and leaves the session reusable; `ConnectOptions.VerifyCertificate` overrides the profile for one attempt; `ConnectionFailedException.CertificateVerificationFailed` marks the text b3270 sends for an unverifiable certificate. `Engine` names the binary, its source (`Bundled` or `Override`), and after the hello its version. `WireLogPath`, `StartWireLog`, `StopWireLog` make the wire log a session capability that survives an engine restart. `AppPaths` owns the per-OS config root with `profiles` and `logs` beneath it."
- In the Backend bullets, replace the `WireLog` bullet with: "`WireLog` is the bug-report mechanism and the fixture recorder: one file, every line, both directions, timestamped. `WireLog.TryFromEnvironment(out error)` returns null when the variable is unset or the file cannot be opened; the session raises the open error once as a `HostMessage`. The log is a swappable field on the session, written under the write lock; `B3270Locator.Find` returns a `B3270Location` with the source."
- In the App bullets add: "`App` shows `SplashWindow` first (1 s minimum, 2.5 s maximum, click or key dismisses), checks the engine through `SessionFactory.CheckBackend`, and when the splash closes executes a `StartupPlan`: `StartupErrorWindow` when the engine is missing, else the session for a resolved argument, else the picker. `StartupArguments` accepts `[L:][Y:][lu@]host[:port]`; a syntax error prints the usage line and opens the picker. `SessionViewModel` times out a connect after `ConnectTimeout` (30 s; the Disconnect item cancels a pending one), offers connect-anyway through `ICertificatePrompt` (`Dialogs/`, injected like the clipboard; `saveProfile` is null for ad hoc profiles so the checkbox is hidden), and owns the Help menu's wire log toggle (`IsWireLogging`, `ShowWireLogsCommand` through `IFolderOpener`) and `Engine` for `AboutWindow`. `TerminalScreen` blinks cells with the Blink rendition at a 750 ms phase, never below 500 ms."
- In the Tests bullets add the new fakes: `FakeCertificatePrompt` (`Decision`, `OnAsk`, `Calls`), `FakeFolderOpener`, and `FakeEmulatorSession`'s `ConnectCompletion`, `ConnectToken`, `connect:noverify`, `wirelog:start:<path>` / `wirelog:stop`, `WireLogException`, `Engine`.
- Update the DevTools driving recipe: the splash appears as a root for up to 2.5 s before the picker or session window; wait for it to close before `tree`.

- [ ] **Step 3: Bring the spec to as-built**

In `docs/superpowers/specs/2026-09-05-lizterm-m2-polish-design.md` change the Status line to "approved 2026-09-05 and implemented on branch claude/project-status-next-234e6b; this is the as-built spec", and record the deviations: `ConnectTimeout` is an instance property, not a static (parallel tests); the About engine line ends in `from LIZTERM_B3270_PATH` with the path on its own line (section 6.3); `StartupPlan.OpenSession` carries `FromStore`; the integration timeout test reuses the same session for two attempts rather than switching to TLS (a session is bound to one profile); anything else the executor had to rule on.

- [ ] **Step 4: Full suite, then commit**

Run: `dotnet test LizTerm.slnx`
Expected: all pass, four integration tests skipped, zero build warnings (`dotnet build LizTerm.slnx 2>&1 | grep -c "warning"` prints 0).

```bash
git add tests/LizTerm.Integration.Tests CLAUDE.md docs
git commit -m "Add live tests for the certificate flag and connect cancellation and bring the docs to as-built

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

## Self-review notes

Spec coverage: section 3 (Task 1, 3, 4, 5), 4.1 (Task 5), 4.2 (Tasks 2, 4), 4.3 (Task 3), 4.4 (Task 6), 5.1 (Task 9), 5.2 (Task 7), 5.3 (Tasks 10, 11), 6.1 and 6.2 (Task 12), 6.3 (Task 13), 7 (Tasks 14, 15), 8 (Task 16), 9 (every task's tests plus Task 17), 10 and 11 (nothing to build). Type names used across tasks: `ConnectOptions`, `EngineInfo`, `EngineSource`, `B3270Location`, `CertificateDecision`, `ICertificatePrompt`, `IFolderOpener`, `SplashTiming`, `StartupPlan`, `AppVersion`, `StatusFormatter.ConnectTimeout/WireLog/Engine`, `SessionViewModel.ConnectTimeout/IsWireLogging/WireLogText/WireLogDirectory/ShowWireLogsCommand/Engine`, `FakeEmulatorSession.ConnectCompletion/ConnectToken/WireLogException`.
