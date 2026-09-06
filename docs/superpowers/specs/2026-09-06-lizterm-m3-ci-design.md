# LizTerm Milestone 3, plan 3a: continuous integration for what exists

Date: 2026-09-06. Parent spec: `2026-09-03-lizterm-v1-design.md` (section 8.5 sketches CI). Predecessor:
`2026-09-05-lizterm-m2-hardening-design.md`, whose section 10 notes the CA-file question for a static engine
(deferred to plan 3b). Status: approved in discussion; awaiting review of this text.

## 1. Purpose

Milestone 2 is merged in full and Milestone 3 (distribution) has no infrastructure yet: no `.github/workflows`,
one native build script (macOS), and no automated proof that a b3270 built by the script can be spawned by
LizTerm. Milestone 3 was split on 2026-09-06 into five plans, each with its own spec and PR:

- **3a (this spec):** GitHub Actions for what exists. Build and test on every push and pull request; build the
  macOS engine with the existing script and gate, prove LizTerm can spawn it, upload it, and run the suite on
  Windows, on pushes to main.
- **3b:** Linux x64 and arm64 engine builds (static OpenSSL, `ldd` gate, oldest supported glibc, CA file for a
  static OpenSSL's trust directory).
- **3c:** Windows engine builds (upstream MinGW cross build from Linux, Schannel, `b3270.exe`; Windows arm64
  ships the x64 binary).
- **3d:** Publish and release (self-contained `dotnet publish` for six runtime identifiers, macOS app bundle,
  tarball and zip layouts, version stamping, tag-triggered release job). Code signing and notarization are a
  separate later task, gated on paying for the Apple Developer Program and a Windows certificate.
- **3e:** Scheduled integration lane. Rule set on 2026-09-06: full CI must not rely on anything on Robert's LAN;
  the lane spins up a dockerized TK4-/TK5 or MVS/CE inside the Action (MVS/CE lacks IND$FILE out of the box:
  `RX MVP INSTALL IND$FILE` as `IBMUSER` installs it).

Decisions taken in the brainstorm on 2026-09-06:

- The repo is private today on a Pro plan and will go public once a license is assigned, so Actions minutes are
  not a design constraint. Even so, the cheap Linux job is the one that runs on every commit; the macOS and
  Windows jobs run on pushes to main and on demand, because a three-OS matrix on every commit adds wait time
  rather than confidence.
- The macOS job does more than link-check the engine: a smoke test starts the CI-built b3270 through
  `B3270Session` and must run (not skip) on that job.
- Two workflow files split by trigger set rather than one file with job-level conditions, so a pull request
  never shows skipped platform jobs and the later plans add jobs to the file whose trigger already fits.

## 2. Facts that shape the design

- Nothing in the Core, backend, or App test projects needs a real b3270: backend tests drive
  `FakeB3270Process`, App tests use `FakeEmulatorSession` on Avalonia's headless platform. Only the opt-in
  integration lane spawns the engine, and it skips itself without `LIZTERM_TEST_HOST`. The test job can
  therefore run on a Linux runner with no engine at all.
- The App and integration test projects copy `native/out/<host-rid>/b3270*` into their output as
  `runtimes/<rid>/native/b3270` only when that directory exists at build time. On a runner that has just run
  `build-macos.sh`, an ordinary `dotnet build` picks the binary up with no workflow-side copying.
- `native/build/fetch-source.sh` downloads the pinned x3270 4.5ga6 tarball from SourceForge into
  `native/cache` and checks its SHA-256. The v1 spec's "git submodule" wording is superseded by this; the
  tarball stays. SourceForge downloads are slow and occasionally fail, so the cache directory is worth keeping
  between runs.
- `verify-macos.sh` already fails the build on any dynamic dependency outside `/usr/lib` and
  `/System/Library` and prints `b3270 --version`. It is the native gate the v1 spec asked for; the workflow
  only has to run the script.
- `B3270Locator.Find(string? overridePath, string baseDirectory)` is public and looks at
  `runtimes/<rid>/native/b3270` then `b3270` beside the base directory when the override is null. Passing null
  is how a test asks for the bundled engine only.
- `B3270Session.StartProcessAsync` is internal; the backend and integration test projects see internals.
  After the hello, `Engine.Version` carries the reported version and `Engine.Source` the provenance
  (`Bundled` for anything inside the app's own tree).
- xunit v3 supports dynamic skips (`Assert.SkipWhen`, `Assert.SkipUnless`), which the live tests already use.
- GitHub cannot run a workflow through `workflow_dispatch` until the workflow file exists on the default branch,
  so a workflow that only had push-to-main and dispatch triggers could not be exercised before its own PR merged.
- GitHub-hosted `macos-15` runners are Apple Silicon and ship Homebrew `openssl@3`; `build-macos.sh` needs
  nothing else beyond the Xcode command line tools the image has. The existing script builds the host
  architecture only, so this plan produces `osx-arm64`. macOS x64 belongs to a later plan (3b or 3d).
- The headless App tests have only ever run on macOS. A font or text-shaping difference on Linux or Windows is
  possible and is treated as a finding to fix, not a reason to drop the runner.

## 3. `ci.yml`

Triggers: `push` (every branch), `pull_request` (targeting any branch), `workflow_dispatch`.

Concurrency: group `ci-${{ github.ref }}`, `cancel-in-progress: true`, so a newer push to the same branch cancels
the run it supersedes.

One job, `test`, on `ubuntu-latest`:

1. `actions/checkout`.
2. `actions/setup-dotnet` reading `global.json` (section 6).
3. `dotnet build LizTerm.slnx --configuration Release -warnaserror`. This is the CLAUDE.md zero-warning rule as
   a hard gate; CI builds are always clean, so the incremental-build blind spot does not apply. Release is the
   configuration a user will run, and it also keeps `AvaloniaUI.DiagnosticsSupport` (Debug-only) out of the
   build the gate measures.
4. `dotnet test LizTerm.slnx --configuration Release --no-build --blame-hang-timeout 5m --logger trx
   --results-directory TestResults`. The five integration tests skip as they do locally. The hang timeout turns
   a wedged test into a failure with a dump instead of a runner that sits until GitHub's six-hour limit.
5. `actions/upload-artifact` of `TestResults/**/*.trx` with `if: failure()`, named `test-results-linux`.

The check a branch-protection rule would require is this job's name, `test`.

## 4. `platforms.yml`

Triggers: `push` to `main`; `workflow_dispatch`; and `pull_request` filtered with `paths` to
`.github/workflows/platforms.yml`, `native/**`, `tests/LizTerm.Integration.Tests/**`,
`src/LizTerm.App/LizTerm.App.csproj`, and `tests/LizTerm.Integration.Tests/LizTerm.Integration.Tests.csproj`. The
path filter is what lets this PR (and any later change to the engine build) exercise the platform jobs before
merge, and it is the permanent rule that a change to the native build gets checked before it lands. The smoke
test and the two csproj files carry the b3270 copy rule, so a PR changing either must be proven on macOS before
merge too.

Concurrency: group `platforms-${{ github.ref }}`, `cancel-in-progress: true`.

Two independent jobs.

### 4.1 `engine-macos` on `macos-15`

1. `actions/checkout`.
2. `actions/cache` on `native/cache` with key `x3270-src-${{ hashFiles('native/build/fetch-source.sh') }}` (the
   script's own content hash; bumping the pinned version in the script invalidates the cache on its own, with no
   second edit to the workflow). A miss just downloads as the script does today.
3. `native/build/build-macos.sh`. Its own `verify-macos.sh` step is the gate: a non-system dynamic dependency
   fails the job. The script leaves the binary at `native/out/osx-arm64/b3270`.
4. `actions/upload-artifact` of `native/out/osx-arm64/b3270` as `b3270-osx-arm64`. Nothing in this plan consumes
   it; it exists for plan 3d's publish job and for downloading a CI-built engine by hand.
5. `actions/setup-dotnet`, then the same build and test steps as section 3, with `LIZTERM_REQUIRE_ENGINE=1` in
   the job environment. Because `native/out/osx-arm64` now exists, the csproj copy rules place the fresh binary
   in the integration test output and the smoke test (section 5) runs against it. If the copy rule ever stops
   matching, the test fails rather than skips, so the job cannot go green without having spawned the engine.
6. Upload TRX on failure as `test-results-macos`.

### 4.2 `test-windows` on `windows-latest`

The section 3 steps unchanged, with the artifact named `test-results-windows`. No engine: plan 3c adds it.

## 5. The engine smoke test

`tests/LizTerm.Integration.Tests/EngineSmokeTests.cs`, one test, `Bundled_engine_starts_and_reports_its_version`:

1. Resolve the engine with `B3270Locator.Find(null, AppContext.BaseDirectory)` inside a try: the null override
   means `LIZTERM_B3270_PATH` on a developer's Mac can never make a Homebrew b3270 stand in for the one CI
   built. A `BackendUnavailableException` means no bundled engine.
2. With no bundled engine: if `LIZTERM_REQUIRE_ENGINE` is set to a non-empty value, fail with the locator's
   message prefixed "LIZTERM_REQUIRE_ENGINE is set but"; otherwise `Assert.Skip` with the same message. Locally
   without a native build the test skips like the live tests do.
3. With one: construct `B3270Session` against a throwaway profile (any host name; nothing connects) with a
   `B3270ChildProcess` for the located path, call `StartProcessAsync`, then assert `Engine.Version` is at least
   `B3270Session.MinimumVersion` and `Engine.Source` is `EngineSource.Bundled`. Dispose, which sends Quit and
   ends the process.

It never connects, needs no host, and finishes well under a second. On a developer Mac that has run
`build-macos.sh` it runs and passes locally, which also catches a stale binary in the test output.

Tests for the gate itself: the outcome logic (skip, fail, run) is factored into a small pure helper
(`EngineRequirement.Decide(bool found, string? requireVariable)`, returning Run, Skip, or Fail) with unit tests in the same
project, so the three outcomes are asserted without spawning anything. The `EnvironmentCollection` pattern is not
needed: the test reads the variable, it does not set it.

## 6. Repository changes

- `global.json`: `{"sdk": {"version": "10.0.400", "rollForward": "latestPatch"}}`. The runner and Robert's
  Mac resolve to the same feature band; a future 11.0 preview on either side cannot change the
  build. Toolchain policy (Robert, 2026-09-06): officially supported builds stay on the current LTS (.NET 10)
  and move to the next LTS when it ships; a non-LTS release such as .NET 11 may be used to *test* against, but
  never for a supported build unless there is a compelling reason, for example a security fix applied only
  there. `global.json` is where that policy is enforced, so a test lane on a newer SDK would use its own
  override rather than a change to the file.
- `README.md`: a CI badge for `ci.yml` at the top, and a "Continuous integration" section: what each workflow
  proves, when each runs, how to dispatch `platforms.yml` by hand, and that the macOS run's artifact is a
  CI-built engine.
- `CLAUDE.md`: the workflow split and triggers under Commands; `LIZTERM_REQUIRE_ENGINE` beside the other
  environment variables; the smoke test under Tests (bundled engine only, skip versus fail).
- Branch protection requiring `test` on `main` is a repository setting only Robert can make; this spec names the
  check and leaves the setting to him.

## 7. Testing and verification

1. Test-first for section 5: the helper's three outcomes, then the smoke test run locally three ways: with a
   bundled engine in the output (runs, passes), without one (skips), and without one with the variable set
   (fails with the prefixed message).
2. Workflow files validated locally for YAML shape (`actionlint` if present on the machine, otherwise a YAML
   parse), because a syntax error surfaces only when GitHub tries to run the file.
3. Push the branch and open the PR. `ci.yml` runs because it is a pull request; `platforms.yml` runs because the
   PR touches its own file and `native/**` is not required for that (the workflow path alone matches).
4. Watch the first Linux and Windows runs for headless test differences; fix what they find as part of this
   plan.
5. Done when `test`, `engine-macos`, and `test-windows` are green on the PR and `b3270-osx-arm64` is
   downloadable from the macOS run.

## 8. Out of scope

Linux and Windows engines (3b, 3c); macOS x64; `dotnet publish`, bundles, version stamping, and releases (3d);
the integration lane and its container (3e); code signing and notarization; Dependabot or other dependency
automation; caching NuGet packages (restore is fast enough and a cache adds a failure mode); running the live
tests in CI.

## 9. Deviations from this spec (as-built)

- Every job carries `timeout-minutes`: 20 for `test` and `test-windows`, 30 for `engine-macos`. The hang timeout
  bounds only the test step; a hung SourceForge download or brew step would otherwise run to GitHub's six-hour
  default, at ten times the cost on a macOS runner.
- Each TRX upload also collects the hang evidence. Corrected after review: a blame-hang kill writes a process
  dump (`*_hangdump.dmp`) and *no* sequence file at all — the collector logs "All tests finished running, Sequence
  file will not be generated" — and the crash-mode file is `<guid>_Sequence.xml`, so the original `Sequence_*.xml`
  glob matched nothing in either mode. The uploads take `*.trx`, `*.dmp` and `*Sequence*.xml`, and are conditioned
  on `failure() || cancelled()`, because `timeout-minutes` and `cancel-in-progress` both cancel rather than fail
  and `failure()` alone would skip the upload on exactly those runs.
- The `engine-macos` cache key is the content hash of `fetch-source.sh`, not the version string written into
  the spec above; section 4.1 already reflects this.
- The `platforms.yml` path filter is wider than section 4 first proposed: the integration test project and the
  two csproj files carrying the b3270 copy rule are included, for the reason given there.
- `permissions: contents: read` is set at the top of both workflow files.
- The first runs found fixes this plan folds in rather than deferring: the App csproj's copy item flows into
  the App test output too, so `SessionFactoryTests` use an internal `SessionFactory.Create(profile,
  overridePath, baseDirectory)` seam to keep a stray bundled engine out of its assertions; the OIA-lock test
  waits on the event's capture rather than a fixed delay; `Wait` defaults to 5 s; the unreadable-profile test
  opens the file with an exclusive handle so Windows honours the denial; and the `caFile` assertion escapes the
  pin path the way the wire does.
- Recorded decision, **reversed after review**: `ci.yml` first kept bare `push` plus `pull_request`, on the view
  that the two-report cost was worth checking direct pushes to non-default branches. That priced only the duplicate
  minutes. The two runs also test *different trees* — the push run the branch tip, the pull_request run the merge
  with `main` — and both land a check run named `test` on the same head SHA, which a required check cannot tell
  apart. A PR on a branch behind `main` could therefore go green on a tree that does not build when merged.
  `ci.yml` now uses `push: branches: [main]`; an unmerged branch is checked by its PR.

- Also corrected after review: the `pull_request` path filter on `platforms.yml` was too narrow. Both jobs run the
  whole solution, so their coverage depends on every source and test file, not the native build alone — and the
  Windows-only fixes this plan itself made (the unreadable-profile test in `LizTerm.Core.Tests`, the `caFile`
  escaping in `LizTerm.Backend.B3270.Tests`) lived outside the original list. The filter now covers `src/**`,
  `tests/**`, `global.json` and the `Directory.*.props` files.

- Also corrected after review: `Upload b3270` now runs *after* the test step, so an engine that links cleanly but
  cannot be spawned is never published; `engine-macos` caches the built engine as well as the source tarball
  (keyed on every `native/build/*.sh`, so any change to the build forces a real build and a fresh gate run); and a
  failure dumps `native/build-tmp/*/{configure,make}.log`, which `build-macos.sh` otherwise redirects out of the
  job log, leaving nothing but an exit code.

- Also corrected after review: `EngineRequirement.Decide` takes a `present` flag as well as `found`.
  `B3270Locator.Find` throws the same exception type for "no binary anywhere" and "a binary is there without its
  executable bit", and collapsing them meant a downloaded artifact missing its `chmod +x` — the state §6's own
  README text warns about — produced a green *skip* from the one test whose job is proving that binary runs. A
  present-but-unresolved engine now always fails. The smoke test's other two assertions were dropped as
  unfalsifiable (`Engine.Source` echoes a constructor argument; the version floor is already enforced inside
  `StartProcessAsync`) and replaced with one that actually proves the csproj copy rule: the resolved path is under
  `runtimes/<rid>/native`.
