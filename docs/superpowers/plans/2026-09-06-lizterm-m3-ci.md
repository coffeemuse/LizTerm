# LizTerm Milestone 3, plan 3a: CI for what exists — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** GitHub Actions that build and test LizTerm on every push and pull request, and on pushes to main build the macOS b3270 engine with the existing script, prove LizTerm can spawn it, upload it, and run the suite on Windows.

**Architecture:** Two workflow files split by trigger set: `ci.yml` (Linux `test` job on every push, PR, and dispatch) and `platforms.yml` (`engine-macos` and `test-windows` on pushes to main, dispatch, and PRs that touch the workflow or `native/**`). A new engine smoke test in the integration test project starts the bundled b3270 through `B3270Session`; it skips without a bundled engine unless `LIZTERM_REQUIRE_ENGINE` is set, in which case it fails, so the macOS job cannot go green without having spawned the engine it built. A `global.json` pins the SDK feature band.

**Tech Stack:** GitHub Actions (`actions/checkout`, `actions/setup-dotnet`, `actions/cache`, `actions/upload-artifact`), .NET 10 SDK, xunit v3 (dynamic skips via `Assert.SkipWhen`), the existing `native/build/build-macos.sh` and `verify-macos.sh`.

**Spec:** `docs/superpowers/specs/2026-09-06-lizterm-m3-ci-design.md`

## Global Constraints

- .NET 10 is the toolchain; `global.json` pins `10.0.200` with `rollForward: latestPatch`. Supported builds stay on the current LTS (spec section 6).
- No new NuGet packages. Central package management: a `PackageReference` never carries a `Version`.
- Zero build warnings. CI builds with `-warnaserror`; locally, `dotnet build LizTerm.slnx --no-incremental 2>&1 | grep -c " warning "` must print `0` before a task is called done.
- Dependency rule: `LizTerm.Core` never mentions Avalonia or b3270; only `LizTerm.Backend.B3270` knows b3270 exists; App names the backend only in `SessionFactory.cs`. Nothing in this plan touches `src/`.
- The smoke test resolves the **bundled** engine only: `B3270Locator.Find(null, AppContext.BaseDirectory)`. `LIZTERM_B3270_PATH` must never satisfy it.
- CI runs the Release configuration. Workflow runners: `ubuntu-latest`, `macos-15`, `windows-latest`. Artifact names: `b3270-osx-arm64`, `test-results-linux`, `test-results-macos`, `test-results-windows`. Job names: `test`, `engine-macos`, `test-windows`.
- Actions are referenced by major version tag (`@v4` style, resolved to the current major in Task 4), never by `@main`.
- Commit subjects are sentence-style, as the repo's history shows (`git log --oneline -5`), and every commit ends with `Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>`.
- Work in this worktree only. Never `cd` to the main checkout at `/Users/robert/ClaudeSandbox/LizTerm`.
- Nothing is pushed before Task 7, and Task 7's push is the one the spec authorises (section 7, step 3).

---

## File structure

| Path | Responsibility |
|---|---|
| `global.json` (create) | Pin the SDK feature band for runner and developer alike. |
| `tests/LizTerm.Integration.Tests/EngineRequirement.cs` (create) | Pure gate: found or not, variable set or not, gives Run / Skip / Fail. |
| `tests/LizTerm.Integration.Tests/EngineRequirementTests.cs` (create) | The gate's three outcomes, no process spawned. |
| `tests/LizTerm.Integration.Tests/EngineSmokeTests.cs` (create) | Start the bundled engine, check version and provenance, quit. |
| `.github/workflows/ci.yml` (create) | Linux build and test on every push, PR, dispatch. |
| `.github/workflows/platforms.yml` (create) | macOS engine build + gated suite, Windows suite. |
| `README.md` (modify) | Badge and a "Continuous integration" section. |
| `CLAUDE.md` (modify) | Workflow split, `LIZTERM_REQUIRE_ENGINE`, smoke test, skip counts. |

The integration test project already copies `native/out/<host-rid>/b3270*` into its output as `runtimes/<rid>/native/b3270` when that directory exists at build time (`tests/LizTerm.Integration.Tests/LizTerm.Integration.Tests.csproj`, the `None Include` item group), and already sees the backend's internals (`InternalsVisibleTo` in `src/LizTerm.Backend.B3270/LizTerm.Backend.B3270.csproj`). Neither changes.

---

### Task 1: Pin the SDK feature band

**Files:**
- Create: `global.json`

**Interfaces:**
- Consumes: nothing.
- Produces: the file `actions/setup-dotnet` reads in Tasks 4 and 5 (`global-json-file: global.json`).

- [ ] **Step 1: Record the SDK the machine resolves today**

Run: `dotnet --version`
Expected: `10.0.201` (or a later 10.0.2xx patch). Note the value.

- [ ] **Step 2: Create `global.json`**

```json
{
  "sdk": {
    "version": "10.0.200",
    "rollForward": "latestPatch"
  }
}
```

`latestPatch` resolves to the highest installed patch of the 10.0.2xx feature band and refuses anything else, so a preview 11.0 SDK on the runner or the developer's Mac cannot be picked up (spec section 6 and its toolchain policy).

- [ ] **Step 3: Verify the pin resolves to the same SDK**

Run: `dotnet --version`
Expected: the same value as Step 1. If it prints an error naming `global.json`, the installed SDK is not in the 10.0.2xx band: stop and report, do not widen the pin.

- [ ] **Step 4: Verify the build and suite are unaffected**

Run: `dotnet test LizTerm.slnx`
Expected: all tests pass, five skipped (the integration lane without a host).

- [ ] **Step 5: Commit**

```bash
git add global.json
git commit -m "Pin the .NET SDK to the 10.0.2xx feature band

Runner and developer resolve the same toolchain; a newer preview on either side cannot change the build.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 2: The smoke test's gate, as a pure helper

**Files:**
- Create: `tests/LizTerm.Integration.Tests/EngineRequirement.cs`
- Create: `tests/LizTerm.Integration.Tests/EngineRequirementTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `enum EngineRequirementOutcome { Run, Skip, Fail }` and `static EngineRequirementOutcome EngineRequirement.Decide(bool found, string? requireVariable)`, both `public`, namespace `LizTerm.Integration.Tests`. Task 3 calls `Decide`.

- [ ] **Step 1: Write the failing tests**

`tests/LizTerm.Integration.Tests/EngineRequirementTests.cs`:

```csharp
namespace LizTerm.Integration.Tests;

public class EngineRequirementTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1")]
    public void A_found_engine_runs_whatever_the_variable_says(string? variable) =>
        Assert.Equal(EngineRequirementOutcome.Run, EngineRequirement.Decide(found: true, variable));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_missing_engine_skips_while_the_variable_is_unset_or_blank(string? variable) =>
        Assert.Equal(EngineRequirementOutcome.Skip, EngineRequirement.Decide(found: false, variable));

    [Theory]
    [InlineData("1")]
    [InlineData("true")]
    [InlineData("0")]
    public void A_missing_engine_fails_once_the_variable_holds_any_non_blank_value(string variable) =>
        Assert.Equal(EngineRequirementOutcome.Fail, EngineRequirement.Decide(found: false, variable));
}
```

The `"0"` case is deliberate: the spec says "set to a non-empty value", so the variable's presence is the switch, not its spelling. CI sets it to `1`.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/LizTerm.Integration.Tests --filter "FullyQualifiedName~EngineRequirementTests"`
Expected: build error, `EngineRequirement` and `EngineRequirementOutcome` do not exist.

- [ ] **Step 3: Write the helper**

`tests/LizTerm.Integration.Tests/EngineRequirement.cs`:

```csharp
namespace LizTerm.Integration.Tests;

public enum EngineRequirementOutcome
{
    Run,
    Skip,
    Fail,
}

/// <summary>The engine smoke test's gate, kept pure so its three outcomes are asserted without spawning anything:
/// a bundled engine runs the test; none skips it, unless LIZTERM_REQUIRE_ENGINE holds any non-blank value, which
/// turns the skip into a failure so a CI job that just built the engine cannot go green by skipping.</summary>
public static class EngineRequirement
{
    public const string Variable = "LIZTERM_REQUIRE_ENGINE";

    public static EngineRequirementOutcome Decide(bool found, string? requireVariable)
    {
        if (found) return EngineRequirementOutcome.Run;
        return string.IsNullOrWhiteSpace(requireVariable) ? EngineRequirementOutcome.Skip : EngineRequirementOutcome.Fail;
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/LizTerm.Integration.Tests --filter "FullyQualifiedName~EngineRequirementTests"`
Expected: 9 passed.

- [ ] **Step 5: Commit**

```bash
git add tests/LizTerm.Integration.Tests/EngineRequirement.cs tests/LizTerm.Integration.Tests/EngineRequirementTests.cs
git commit -m "Add the engine smoke test's gate as a pure helper

Found runs; missing skips, or fails when LIZTERM_REQUIRE_ENGINE holds any non-blank value.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 3: The engine smoke test

**Files:**
- Create: `tests/LizTerm.Integration.Tests/EngineSmokeTests.cs`

**Interfaces:**
- Consumes: `EngineRequirement.Decide`, `EngineRequirement.Variable`, `EngineRequirementOutcome` (Task 2); `B3270Locator.Find(string? overridePath, string baseDirectory)` returning `B3270Location(string Path, EngineSource Source)` and throwing `BackendUnavailableException` when nothing is found (`src/LizTerm.Backend.B3270/Process/B3270Locator.cs`); `B3270ChildProcess(string executablePath)`; `B3270Session(SessionProfile, Func<IB3270Process>, WireLog? = null, string? = null, B3270Location? = null)`; internal `B3270Session.StartProcessAsync(CancellationToken)`; `B3270Session.MinimumVersion` (a `Version`, 4.2.0); `B3270Session.Engine` (`EngineInfo(string Name, string? Version, string Path, EngineSource Source)`, where `Version` after the hello reads `"4.5.6 (b3270 v4.5ga6 ...)"`, the numeric version first, a space, then the build text in parentheses); `SessionProfile` positional record with defaults (`Name`, `Host`, ...).
- Produces: the test `EngineSmokeTests.Bundled_engine_starts_and_reports_its_version`, which Task 5's macOS job requires to run.

- [ ] **Step 1: Write the test**

`tests/LizTerm.Integration.Tests/EngineSmokeTests.cs`:

```csharp
using LizTerm.Backend.B3270;
using LizTerm.Backend.B3270.Process;
using LizTerm.Core.Session;

namespace LizTerm.Integration.Tests;

/// <summary>Proves the engine bundled in this project's output (runtimes/&lt;rid&gt;/native/b3270, copied from native/out
/// by the csproj at build time) can be spawned and spoken to. Never connects to anything. Resolves the bundled engine
/// only: LIZTERM_B3270_PATH is ignored, so a Homebrew b3270 cannot stand in for the one CI built. Without a bundled
/// engine it skips, unless LIZTERM_REQUIRE_ENGINE is set, in which case it fails (see EngineRequirement).</summary>
public class EngineSmokeTests
{
    [Fact(Timeout = 60_000)]
    public async Task Bundled_engine_starts_and_reports_its_version()
    {
        B3270Location? location = null;
        string? missing = null;
        try
        {
            location = B3270Locator.Find(overridePath: null, AppContext.BaseDirectory);
        }
        catch (BackendUnavailableException e)
        {
            missing = e.Message;
        }

        var outcome = EngineRequirement.Decide(location is not null, Environment.GetEnvironmentVariable(EngineRequirement.Variable));
        if (outcome == EngineRequirementOutcome.Fail) Assert.Fail($"{EngineRequirement.Variable} is set but {missing}");
        Assert.SkipWhen(outcome == EngineRequirementOutcome.Skip, missing ?? "no bundled engine");

        var profile = new SessionProfile(Name: "engine-smoke", Host: "engine-smoke.invalid");
        await using var session = new B3270Session(profile, () => new B3270ChildProcess(location!.Path), location: location);
        await session.StartProcessAsync(TestContext.Current.CancellationToken);

        Assert.Equal(EngineSource.Bundled, session.Engine.Source);
        var reported = session.Engine.Version;
        Assert.NotNull(reported);
        var number = Version.Parse(reported.Split(' ', 2)[0]);
        Assert.True(number >= B3270Session.MinimumVersion, $"engine {reported} is older than {B3270Session.MinimumVersion}");
    }
}
```

Disposal (`await using`) sends Quit and ends the process, as every backend test relies on.

- [ ] **Step 2: Run it without a bundled engine and see it skip**

This worktree has no `native/out`, so the output has no bundled engine.

Run: `dotnet test tests/LizTerm.Integration.Tests --filter "FullyQualifiedName~EngineSmokeTests"`
Expected: 1 skipped, reason starting `The emulator engine (b3270) was not found. Looked in:`.

- [ ] **Step 3: Run it with the variable set and see it fail**

Run: `LIZTERM_REQUIRE_ENGINE=1 dotnet test tests/LizTerm.Integration.Tests --filter "FullyQualifiedName~EngineSmokeTests"`
Expected: 1 failed, message starting `LIZTERM_REQUIRE_ENGINE is set but The emulator engine (b3270) was not found`.

- [ ] **Step 4: Confirm the override cannot satisfy it**

Run: `LIZTERM_B3270_PATH=/opt/homebrew/bin/b3270 dotnet test tests/LizTerm.Integration.Tests --filter "FullyQualifiedName~EngineSmokeTests"`
Expected: still 1 skipped. (If `/opt/homebrew/bin/b3270` does not exist on this machine the outcome is the same; the point is that the variable is not consulted.)

- [ ] **Step 5: Build the engine locally and see the test run**

Run: `native/build/build-macos.sh`
Expected: ends with `OK: .../native/out/osx-arm64/b3270 links only system libraries`, the `otool -L` listing, and a `b3270 v4.5ga6` version line. Takes a few minutes; needs Homebrew `openssl@3` and the Xcode command line tools, both present on this Mac. The outputs land under the gitignored `native/cache`, `native/build-tmp`, and `native/out`.

Then rebuild so the csproj copy rule sees the new directory, and run the test:

Run: `dotnet build tests/LizTerm.Integration.Tests && ls tests/LizTerm.Integration.Tests/bin/Debug/net10.0/runtimes/osx-arm64/native/ && dotnet test tests/LizTerm.Integration.Tests --no-build --filter "FullyQualifiedName~EngineSmokeTests"`
Expected: `b3270` listed, then 1 passed.

- [ ] **Step 6: Run the whole suite and the warning check**

Run: `dotnet test LizTerm.slnx`
Expected: all pass; five skipped (the live tests), the smoke test now runs on this machine.

Run: `dotnet build LizTerm.slnx --no-incremental 2>&1 | grep -c " warning "`
Expected: `0`.

- [ ] **Step 7: Commit**

```bash
git add tests/LizTerm.Integration.Tests/EngineSmokeTests.cs
git commit -m "Add an engine smoke test that starts the bundled b3270

Bundled engine only, never LIZTERM_B3270_PATH; skips without one unless LIZTERM_REQUIRE_ENGINE is set, then fails.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 4: `ci.yml`

**Files:**
- Create: `.github/workflows/ci.yml`

**Interfaces:**
- Consumes: `global.json` (Task 1).
- Produces: the job `test`, the check name a branch-protection rule would require; the artifact `test-results-linux` on failure.

- [ ] **Step 1: Resolve the current major of each action**

Run:

```bash
for a in actions/checkout actions/setup-dotnet actions/upload-artifact actions/cache; do
  printf '%s %s\n' "$a" "$(gh api repos/$a/releases/latest --jq .tag_name)"
done
```

Expected: four lines such as `actions/checkout v4.2.2`. Use each one's major (`v4`, `v5`, ...) in the `uses:` lines below in place of the `v4` written here; `actions/cache` is used in Task 5, note its major now.

- [ ] **Step 2: Write the workflow**

`.github/workflows/ci.yml`:

```yaml
name: CI

on:
  push:
  pull_request:
  workflow_dispatch:

concurrency:
  group: ci-${{ github.ref }}
  cancel-in-progress: true

permissions:
  contents: read

jobs:
  test:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4

      - uses: actions/setup-dotnet@v4
        with:
          global-json-file: global.json

      - name: Build (warnings are errors)
        run: dotnet build LizTerm.slnx --configuration Release -warnaserror

      - name: Test
        run: dotnet test LizTerm.slnx --configuration Release --no-build --blame-hang-timeout 5m --logger trx --results-directory TestResults

      - name: Upload test results
        if: failure()
        uses: actions/upload-artifact@v4
        with:
          name: test-results-linux
          path: TestResults/**/*.trx
          if-no-files-found: ignore
```

Notes for the implementer: `push` with no branch filter is deliberate (spec section 3: every branch), so a branch that never gets a PR is still built; a PR therefore runs twice, once per event, and the PR run is the one a required check reads. `permissions: contents: read` is the least the job needs. `if-no-files-found: ignore` keeps a build failure (no TRX written yet) from failing a second time in the upload step.

- [ ] **Step 3: Validate the YAML shape**

Run: `python3 -c "import yaml; d = yaml.safe_load(open('.github/workflows/ci.yml')); print(sorted(d['jobs']))"`
Expected: `['test']`. (PyYAML reads the bare `on` key as boolean `True`; that is a YAML 1.1 quirk, not an error, and GitHub reads it correctly.) If `actionlint` is installed (`which actionlint`), also run `actionlint .github/workflows/ci.yml` and expect no output; do not install it.

- [ ] **Step 4: Run the exact CI commands locally, in Release, to catch a difference from the Debug builds the suite normally gets**

Run: `dotnet build LizTerm.slnx --configuration Release -warnaserror && dotnet test LizTerm.slnx --configuration Release --no-build --blame-hang-timeout 5m --logger trx --results-directory TestResults`
Expected: build succeeds with no warnings; tests pass with the smoke test running (Task 3 left a bundled engine) and five live tests skipped; `TestResults/` holds one `.trx` per test project.

Run: `rm -rf TestResults && git status --short`
Expected: only `.github/workflows/ci.yml` untracked. (`TestResults/` and `*.trx` are already gitignored by the `[Tt]est[Rr]esult*/` and `*.trx` rules; it is removed so a stale local results directory cannot confuse the next rehearsal.)

- [ ] **Step 5: Commit**

```bash
git add .github/workflows/ci.yml
git commit -m "Add the CI workflow: Linux build with warnings as errors and the full suite on every push and PR

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 5: `platforms.yml`

**Files:**
- Create: `.github/workflows/platforms.yml`

**Interfaces:**
- Consumes: `global.json` (Task 1); `native/build/build-macos.sh` (existing; writes `native/out/osx-arm64/b3270` and runs `verify-macos.sh`); `native/build/fetch-source.sh` (existing; caches the tarball under `native/cache`); the smoke test and `LIZTERM_REQUIRE_ENGINE` (Tasks 2 and 3).
- Produces: jobs `engine-macos` and `test-windows`; artifacts `b3270-osx-arm64`, `test-results-macos`, `test-results-windows`.

- [ ] **Step 1: Write the workflow**

`.github/workflows/platforms.yml` (use the action majors resolved in Task 4, Step 1):

```yaml
name: Platforms

on:
  push:
    branches: [main]
  pull_request:
    paths:
      - .github/workflows/platforms.yml
      - native/**
  workflow_dispatch:

concurrency:
  group: platforms-${{ github.ref }}
  cancel-in-progress: true

permissions:
  contents: read

jobs:
  engine-macos:
    runs-on: macos-15
    env:
      LIZTERM_REQUIRE_ENGINE: "1"
    steps:
      - uses: actions/checkout@v4

      - name: Cache the pinned x3270 source tarball
        uses: actions/cache@v4
        with:
          path: native/cache
          key: x3270-src-${{ hashFiles('native/build/fetch-source.sh') }}

      - name: Ensure OpenSSL 3 is installed
        run: brew list openssl@3 >/dev/null 2>&1 || brew install openssl@3

      - name: Build b3270 (verify-macos.sh is the gate)
        run: native/build/build-macos.sh

      - name: Upload b3270
        uses: actions/upload-artifact@v4
        with:
          name: b3270-osx-arm64
          path: native/out/osx-arm64/b3270
          if-no-files-found: error

      - uses: actions/setup-dotnet@v4
        with:
          global-json-file: global.json

      - name: Build (warnings are errors)
        run: dotnet build LizTerm.slnx --configuration Release -warnaserror

      - name: Test (the engine smoke test must run, not skip)
        run: dotnet test LizTerm.slnx --configuration Release --no-build --blame-hang-timeout 5m --logger trx --results-directory TestResults

      - name: Upload test results
        if: failure()
        uses: actions/upload-artifact@v4
        with:
          name: test-results-macos
          path: TestResults/**/*.trx
          if-no-files-found: ignore

  test-windows:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v4

      - uses: actions/setup-dotnet@v4
        with:
          global-json-file: global.json

      - name: Build (warnings are errors)
        run: dotnet build LizTerm.slnx --configuration Release -warnaserror

      - name: Test
        run: dotnet test LizTerm.slnx --configuration Release --no-build --blame-hang-timeout 5m --logger trx --results-directory TestResults

      - name: Upload test results
        if: failure()
        uses: actions/upload-artifact@v4
        with:
          name: test-results-windows
          path: TestResults/**/*.trx
          if-no-files-found: ignore
```

Notes for the implementer: the cache key hashes `fetch-source.sh` rather than spelling the version, so bumping `VERSION` or `SHA256` in that script invalidates the cache without a second edit (the spec's "keyed on the pinned version", by content). The OpenSSL step is a guard: the `macos-15` image ships `openssl@3`, and the step is a no-op then. The b3270 build happens before `setup-dotnet` so a native failure is reported before any .NET work. The `dotnet build` after the native build is what copies the fresh binary into the integration test output (the csproj `Exists('../../native/out/$(NETCoreSdkRuntimeIdentifier)')` rule); with `LIZTERM_REQUIRE_ENGINE` in the job environment the smoke test then must run.

- [ ] **Step 2: Validate the YAML shape**

Run: `python3 -c "import yaml; d = yaml.safe_load(open('.github/workflows/platforms.yml')); print(sorted(d['jobs']), d['jobs']['engine-macos']['env'])"`
Expected: `['engine-macos', 'test-windows'] {'LIZTERM_REQUIRE_ENGINE': '1'}`. Run `actionlint` too if it is installed.

- [ ] **Step 3: Rehearse the macOS job's sequence locally**

The job's steps after checkout are exactly what a developer Mac can run. Rehearse them in order from a clean native tree so the sequence itself is proven, not just each command:

Run: `rm -rf native/out native/build-tmp && native/build/build-macos.sh && ls -l native/out/osx-arm64/b3270`
Expected: the gate's `OK:` line, then the binary listed (the cached tarball under `native/cache` is reused, so no download).

Run: `dotnet build LizTerm.slnx --configuration Release -warnaserror && LIZTERM_REQUIRE_ENGINE=1 dotnet test LizTerm.slnx --configuration Release --no-build --blame-hang-timeout 5m --logger trx --results-directory TestResults`
Expected: build succeeds; tests pass; the smoke test is among the passed tests (confirm with `grep -l "Bundled_engine_starts" TestResults/*.trx` listing one file whose entry carries `outcome="Passed"`).

Run: `rm -rf TestResults`

- [ ] **Step 4: Commit**

```bash
git add .github/workflows/platforms.yml
git commit -m "Add the platforms workflow: macOS engine build with a required smoke test, and the suite on Windows

Runs on pushes to main, on dispatch, and on PRs that touch the workflow or the native build.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 6: README and CLAUDE.md

**Files:**
- Modify: `README.md` (title block at the top; the "Environment variables" list under "Developer setup (macOS)"; a new section before "Recording protocol fixtures")
- Modify: `CLAUDE.md` (the "Commands" section's closing paragraph; "The b3270 binary" subsection's environment-variable paragraph; the "Tests" section)

**Interfaces:**
- Consumes: the workflow names, job names, artifact names, and `LIZTERM_REQUIRE_ENGINE` from Tasks 2 to 5.
- Produces: documentation only.

- [ ] **Step 1: README badge**

Change the first lines of `README.md` from:

```markdown
# LizTerm

A cross-platform TN3270 client for retro mainframe hobbyists: macOS, Linux, and Windows, one UI, no install
```

to:

```markdown
# LizTerm

[![CI](https://github.com/coffeemuse/LizTerm/actions/workflows/ci.yml/badge.svg)](https://github.com/coffeemuse/LizTerm/actions/workflows/ci.yml)

A cross-platform TN3270 client for retro mainframe hobbyists: macOS, Linux, and Windows, one UI, no install
```

- [ ] **Step 2: README environment variable**

In the "Environment variables" list, after the `LIZTERM_TEST_HOST` bullet, add:

```markdown
- `LIZTERM_REQUIRE_ENGINE`: any non-blank value makes the engine smoke test fail instead of skip when no bundled
  b3270 is in the test output. CI sets it on the macOS job; leave it unset locally.
```

- [ ] **Step 3: README section**

Insert before `## Recording protocol fixtures`:

```markdown
## Continuous integration

Two GitHub Actions workflows under `.github/workflows`:

- `ci.yml` runs on every push, pull request, and manual dispatch: one Linux job, `test`, that builds the solution in
  Release with warnings as errors and runs the full suite. No engine is needed; the integration lane skips itself.
- `platforms.yml` runs on pushes to `main`, on manual dispatch, and on pull requests that touch the workflow or
  `native/`: `engine-macos` builds b3270 with `native/build/build-macos.sh` (whose `verify-macos.sh` fails the job on
  any non-system dynamic dependency), uploads it as the `b3270-osx-arm64` artifact, then runs the suite with
  `LIZTERM_REQUIRE_ENGINE=1` so the engine smoke test must start the freshly built binary; `test-windows` runs the
  suite on Windows.

To run the platform jobs by hand: Actions, Platforms, "Run workflow", or `gh workflow run platforms.yml`. A failed
run uploads its `.trx` files as `test-results-<os>`. The macOS run's `b3270-osx-arm64` artifact is a CI-built engine
you can download and drop into `native/out/osx-arm64/`.
```

- [ ] **Step 4: CLAUDE.md Commands section**

After the paragraph ending `an incremental build hides warnings from projects it does not recompile.` add:

```markdown
CI is two workflows under `.github/workflows`: `ci.yml` (job `test`, `ubuntu-latest`, every push and PR: Release build
with `-warnaserror`, then the suite with a 5 minute blame hang timeout) and `platforms.yml` (pushes to `main`, dispatch,
and PRs touching the workflow or `native/**`: `engine-macos` on `macos-15` runs `build-macos.sh`, uploads
`b3270-osx-arm64`, and runs the suite with `LIZTERM_REQUIRE_ENGINE=1`; `test-windows` runs the suite). Failed runs
upload `test-results-<os>` with the `.trx` files. `global.json` pins the SDK to the 10.0.2xx band; supported builds
stay on the current LTS.
```

- [ ] **Step 5: CLAUDE.md environment variables**

In "The b3270 binary" subsection, change `which otherwise reports five skipped tests` to `whose five live tests otherwise skip`, and after the sentence ending `exporting values on a command line.` add:

```markdown
`LIZTERM_REQUIRE_ENGINE` (any non-blank value) makes the engine smoke test in the same project fail rather than skip when
the test output has no bundled b3270; CI sets it on the macOS job only.
```

- [ ] **Step 6: CLAUDE.md Tests section**

Add a bullet at the end of the "Tests" section:

```markdown
- `EngineSmokeTests` (integration project) starts the **bundled** engine from the test output through `B3270Session`,
  checks `Engine.Source` is `Bundled` and the version is at least `MinimumVersion`, and quits; it resolves with
  `B3270Locator.Find(null, AppContext.BaseDirectory)` so `LIZTERM_B3270_PATH` can never satisfy it. Without a bundled
  engine it skips, unless `LIZTERM_REQUIRE_ENGINE` is set, when it fails; `EngineRequirement.Decide` is that gate, unit
  tested on its own. On a Mac that has run `build-macos.sh` the test runs locally and catches a stale binary in the output.
```

- [ ] **Step 7: Check the rendered text**

Run: `grep -c "LIZTERM_REQUIRE_ENGINE" README.md CLAUDE.md`
Expected: `README.md:2` (the bullet and the CI section) and `CLAUDE.md:3` (Commands, environment variables, Tests).

- [ ] **Step 8: Commit**

```bash
git add README.md CLAUDE.md
git commit -m "Document the CI workflows, the engine smoke test, and LIZTERM_REQUIRE_ENGINE

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 7: Push, open the PR, and make the three jobs green

**Files:**
- Modify: whatever the first Linux, macOS, and Windows runs reveal (expected: nothing, possibly a headless font or text-shaping difference in the App tests).

**Interfaces:**
- Consumes: everything above.
- Produces: a green PR with the `test`, `engine-macos`, and `test-windows` checks, and a downloadable `b3270-osx-arm64` artifact.

- [ ] **Step 1: Final local gate before pushing**

Run: `dotnet build LizTerm.slnx --no-incremental 2>&1 | grep -c " warning "`
Expected: `0`.

Run: `dotnet test LizTerm.slnx`
Expected: all pass, five skipped.

- [ ] **Step 2: Push and open the PR**

Run:

```bash
git push -u origin HEAD
gh pr create --base main --title "Milestone 3 plan 3a: CI for what exists" --body "$(cat <<'BODY'
Two GitHub Actions workflows and an engine smoke test, per docs/superpowers/specs/2026-09-06-lizterm-m3-ci-design.md.

- ci.yml: Linux build with warnings as errors and the full suite on every push and PR.
- platforms.yml: macOS b3270 build through the existing script and gate, uploaded as b3270-osx-arm64, then the suite with the engine smoke test required to run; the suite on Windows. Runs on pushes to main, dispatch, and PRs touching the workflow or native/.
- EngineSmokeTests starts the bundled engine only (never LIZTERM_B3270_PATH); skips without one unless LIZTERM_REQUIRE_ENGINE is set, then fails.
- global.json pins the SDK to the 10.0.2xx band.

This PR touches .github/workflows/platforms.yml, so both workflows run on it.

🤖 Generated with [Claude Code](https://claude.com/claude-code)
BODY
)"
```

Expected: the PR URL. Note it.

- [ ] **Step 3: Watch both workflows**

Run: `gh run list --branch "$(git branch --show-current)" --limit 6`
Expected: runs for `CI` and `Platforms` (the PR touches `platforms.yml`, so its path filter matches). Then for each run id:

Run: `gh run watch <run-id> --exit-status`
Expected: exit 0 for all three jobs. A cancelled `CI` run beside a completed one is the concurrency group cancelling the branch-push run in favour of the PR run, or the reverse; only the completed one matters.

- [ ] **Step 4: If a job fails, fix it here, test-first where it touches code**

Read the log: `gh run view <run-id> --log-failed`. Download the results if the failure is a test: `gh run download <run-id> --name test-results-<os> --dir TestResults-<os>` and read the `.trx`. Likely findings and their fixes:

- A headless App test that differs on Linux or Windows (font metrics, text shaping): fix the test's assumption, never widen it to "anything passes"; a `[AvaloniaFact]` that depends on a platform font falls back to the embedded IBM 3270 font (`avares://LizTerm.App/Assets/Fonts#IBM 3270`) rather than a system one.
- An analyzer warning that only fires on Windows or Linux and now fails `-warnaserror`: fix the code, do not suppress it.
- `brew install openssl@3` needed on the runner: the guard step handles it; if the build still cannot find OpenSSL, set `OPENSSL_PREFIX` in that step's environment from `brew --prefix openssl@3`.
- The smoke test failing with "LIZTERM_REQUIRE_ENGINE is set but ..." on macOS: the copy rule did not fire, which means `native/out/osx-arm64` was not there when `dotnet build` ran; check the build step's log for the `OK:` gate line and the `find obj -type f -name b3270` result.

Commit each fix on its own with a subject that names the finding, push, and watch again. Remove any `TestResults-<os>` directory before committing.

- [ ] **Step 5: Confirm the engine artifact**

Run, with `$SCRATCH` set to the session's scratchpad directory:

```bash
gh run download <platforms-run-id> --name b3270-osx-arm64 --dir "$SCRATCH/ci-engine" && chmod +x "$SCRATCH/ci-engine/b3270" && file "$SCRATCH/ci-engine/b3270" && "$SCRATCH/ci-engine/b3270" --version | head -1
```

Expected: `Mach-O 64-bit executable arm64` and a `b3270 v4.5ga6` line. (Downloaded artifacts lose the executable bit, hence the `chmod`.)

- [ ] **Step 6: Report**

The plan is complete when all three jobs are green on the PR, the artifact downloads and runs, and `git status` is clean. Robert merges and sets branch protection on `main` requiring the `test` check (spec section 6); both are his.
