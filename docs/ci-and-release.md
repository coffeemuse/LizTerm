# CI and release

Four GitHub Actions workflows live under `.github/workflows`. How each engine is built and gated is in
[engines](engines.md); this page covers how CI runs those builds, what it tests, and how a release is packaged.

## The workflows

| Workflow | Triggers | What it does |
|---|---|---|
| `ci.yml` | Pushes to `main`, every pull request, manual dispatch | One Linux job, `test`: a Release build with `-warnaserror`, then the full suite with a 5-minute blame hang timeout. No engine; the live-host tests and the engine smoke test skip. |
| `engines.yml` | Only when called (`workflow_call`), never on its own | Builds, gates and starts the five engines: the one definition anywhere of "an engine proven to start". |
| `platforms.yml` | Manual dispatch; pull requests touching the workflow, `engines.yml`, `native/**`, `src/**`, `tests/**`, `global.json` or `Directory.*.props`; pushes to `main` touching the workflow, `engines.yml` or `native/**` | Calls `engines.yml` as its one job, `engines`. |
| `release.yml` | A pushed `v*` tag; manual dispatch as a rehearsal that produces every artifact and no release | Calls `engines.yml`, then publishes and packages six RIDs and, for a tag, drafts a GitHub Release. |

To run the platform jobs by hand: Actions → Platforms → Run workflow, or `gh workflow run platforms.yml`.

`ci.yml`'s `push` trigger is limited to `main` on purpose. A bare `push:` beside `pull_request:` gives a PR's head
commit two check runs named `test` — one over the branch tip, one over the merge with `main` — and a required check
cannot tell them apart, so a PR could go green for a tree that does not build once merged.

The shape of `engines.yml` — one macOS job, the push-to-`main` path filter, tight timeouts — was set while the
repository was private and Actions minutes were billed, macOS at ten times the Linux rate. It holds now that the
repository is public, because none of it costs coverage. The run-by-run measurements behind each change are in
the git history of `CLAUDE.md`, where they used to be recorded.

## `engines.yml`

Every job publishes an engine only after something has started it: `engine-macos` and `engine-linux` within one
job each, Windows across two.

**`engine-macos`** is one job on one arm64 `macos-15` runner that builds both `osx-arm64` and `osx-x64`, the latter
cross-built. A native Intel job is not an option worth taking: `macos-13` is retired, and `macos-15-intel`, the last
x86_64 image GitHub will offer, ends in August 2027, so a native job would inherit that expiry. The job:

1. restores both engine caches;
2. installs Rosetta (idempotent). `verify-macos.sh`'s TLS check executes the finished x86_64 binary, so without
   Rosetta the *build itself* cannot finish its gate on this runner, not only the suite;
3. runs `build-macos.sh <rid>` for whichever engine missed its cache — sequentially, since both fetch into one
   source-tarball directory — and gates each with `verify-macos.sh`;
4. builds and tests the solution once with `LIZTERM_REQUIRE_ENGINE=1`. The host RID is `osx-arm64`, so that is the
   engine the suite spawns. This runner cannot start the `osx-x64` binary, so `verify-macos.sh`'s machine-type
   check is what proves that one — the same trade the release pipeline makes for its cross-packaged RIDs;
5. only then uploads `b3270-osx-arm64` and `b3270-osx-x64`, so a binary that links but cannot be spawned is never
   published.

It used to be two jobs on this same runner, and the `osx-x64` one proved nothing the other did not: same OS, SDK
and tree, and a RID-less solution build does not even copy the x64 engine. What one job costs against two: a cold
run builds the engines one after the other, and one engine's failure also costs the other's upload. `timeout-minutes` is 20, about 2.5 times the worst cold build.

**`engine-linux`** is a two-leg matrix, `ubuntu-24.04` and `ubuntu-24.04-arm` — pinned rather than
`ubuntu-latest`, so the legs differ only in architecture — uploading `b3270-linux-x64` and `b3270-linux-arm64`.
It sets `fail-fast: false`, because one architecture failing is information about that architecture, and
cancelling the other leg throws that information away.

- The arm64 leg runs the whole solution. The x64 leg runs only `tests/LizTerm.Integration.Tests`: `ci.yml`'s
  `test` already runs the same solution on the same OS and architecture, so the engine smoke test is the one thing
  this leg adds. That difference is an expression on the `dotnet test` line, not a third `matrix.include`
  property, because GitHub renders every include property in the check name, and a new one would rename both
  `engine-linux` checks.
- The gate step — the built binary must pass `verify-linux.sh`, and each fixture must be rejected by its own arm's
  message (see [engines](engines.md#linux)) — and the start check run whether or not the engine came from the
  cache. On a cache hit the build step never runs, so those two steps are all that stands between a stale cached
  binary and an upload.
- `timeout-minutes` is 15, about 3.3 times the slowest cold leg measured with every cache missed; warm runs finish
  in under three minutes.

**`engine-windows`**, on `ubuntu-24.04`, cross-builds `win-x64` inside the pinned Debian container, gates it on
machine type and DLL imports — the half of the gate a Linux runner can check about a binary it cannot start — and
uploads it as `b3270-win-x64-unverified`.

**`test-windows`** (`needs: engine-windows`) downloads that artifact, runs `shared-verify-tls.sh` against it to prove
it starts and reports `Windows Schannel`, runs the suite with `LIZTERM_REQUIRE_ENGINE=1`, and republishes the same
bytes as `b3270-win-x64`. Two names for one binary, so an `-unverified` download can never be mistaken for one a
Windows machine has actually started.

`engine-macos`, `test-windows` and `engine-linux`'s arm64 leg run the whole solution, which is why
`platforms.yml`'s pull-request filter covers all of `src/` and `tests/` rather than the native build alone.

### Evidence from failed runs

Runs that fail **or are cancelled** upload `test-results-<os>`: the `.trx` files, any `*.dmp` (a blame-hang kill
writes a hang dump, not a sequence file), and any `*Sequence*.xml`. The engine jobs also dump their build logs:
`native/build-tmp/*/{openssl,configure,make}.log` on macOS and `{openssl,expat,configure,make}.log` on Linux.

Every one of these uploads is `if: ${{ failure() || cancelled() }}`. `timeout-minutes` and `cancel-in-progress`
both *cancel*, so plain `failure()` would drop the evidence on exactly the runs that need it — and a build wedged
past its timeout has no other way to get its logs out of `native/build-tmp`.

### Caches

- `engine-macos` caches each built engine separately (`native/out/<rid>`, keyed on the RID, because one job restores
  both and each must hit or miss independently), plus one source-tarball entry shared by both.
- `engine-linux` caches the same two things per leg: `native/out/<rid>`, and one `native/cache` entry shared by both
  legs, since source tarballs are architecture-independent.
- Each engine cache key hashes only the scripts that feed **that** platform — `*macos*.sh` plus `fetch-source.sh`
  and `fetch-openssl.sh` for macOS; `*linux*.sh` plus `fetch-*.sh` for Linux — with `shared-*.sh` in both. A
  Linux-only edit therefore does not force a cold macOS rebuild that cannot change its binary, and new shared
  machinery cannot be added to one key and forgotten in the other.

**Why `platforms.yml` runs on pushes to `main`, and only for native changes.** `actions/cache` scopes an entry to the
branch that saved it plus the default branch: a PR run restores what `main` saved, but `main` never restores what a
PR saved. The run on `main` turns one PR's cold engine build into the warm cache every later PR hits, and that is
needed only when a cache key's inputs changed. On every other merge, the PR run has already exercised the same tree
on every platform (`pull_request` runs against the merge with `main`). `ci.yml`'s `test` still runs on every push to
`main`, and `release.yml` calls `engines.yml` itself, so a tag is gated on every platform regardless.

An entry unused for seven days is evicted. After an idle stretch, the first PR builds cold in its own scope, and
`main` stays cold until a `native/` change lands — or until someone dispatches `platforms.yml` on `main` by hand,
which is the cheap way to re-warm it.

## Check names and branch protection

Branch protection on `main` requires `ci.yml`'s `test` only. The platform jobs are path-filtered on pull requests
and never report on a docs-only PR, so a rule requiring them directly would leave such PRs waiting forever. If that
ever matters, the fix is a `platforms-gate` job in `platforms.yml` with `needs: engines` and `if: always()` that
passes whether `engines.yml` ran or was skipped by the path filter. It needs only one dependency, because a job
cannot `needs:` an individual job inside a reusable workflow it calls — only the call itself.

A rule naming one of `engines.yml`'s jobs directly has to spell the check exactly as GitHub renders it: the calling
job's name, then the called job's, then **every** `matrix.include` property, not just the one that varies
meaningfully. A short form matches nothing and waits forever. `platforms.yml` and `release.yml` both name their
calling job `engines`, which is what keeps these names valid for both; keep it that way. As rendered:

```text
engines / engine-macos
engines / engine-linux (ubuntu-24.04, linux-x64)
engines / engine-linux (ubuntu-24.04-arm, linux-arm64)
engines / engine-windows
engines / test-windows
```

## Release

`release.yml` calls the same `engines.yml` — a tag cut from a green `main` restores every engine from `actions/cache`,
which is repository-scoped rather than workflow-scoped — then publishes and packages six self-contained builds:
`osx-arm64`, `osx-x64`, `linux-x64`, `linux-arm64`, `win-x64` and `win-arm64`. The last ships the `win-x64` engine
under Windows 11's emulation, the same mapping `LizTerm.App.csproj`'s `LizTermEngineRid` and
`native/build/verify-bundled-engine.sh` both encode.

### The `version` job

Parcel does not read the project's version: `LizTerm.parcel` carries its own `GeneralSettings.Version`. The
`version` job fails the run if that, `Directory.Build.props`, and — on an actual tag — the tag disagree, rather than
silently building different version numbers into one release.

Parcel also treats a missing or misnamed icon as a warning, not an error, so a typo in `LizTerm.parcel`'s icon paths
would ship an icon-less installer with a green build. The same job asserts that every icon path `LizTerm.parcel`
names (`Win32Settings.InstallerIcon`, `MacOsSettings.AppIcon`, `LinuxSettings.AppIcon`) exists.

### Packaging with Parcel

- `parcel pack` always runs its own `dotnet publish` into a randomised temp directory, `--no-build` or not. What
  `--no-build` actually consumes is the ordinary build output at `src/LizTerm.App/bin/Release/net10.0/<rid>/` left by
  the job's own `dotnet publish` step — not that step's `-o publish/<rid>` tree. So every gate runs against the
  **extracted package**, never against a publish tree Parcel never reads.
- Packaging always runs on a native runner for its target OS — `macos-15`, `ubuntu-24.04`, `windows-latest` — even
  for RIDs that runner cannot execute, because the installer formats (`.dmg`, `.deb`/`.rpm`, NSIS `.exe`) need that
  OS's own tooling whatever CPU the binary targets.
- Parcel's Linux CLI ships an x64-only native binary and cannot start on an arm64 Linux runner (`Exec format error`,
  not a packaging failure). So `linux-arm64` is packaged after `linux-x64` on the same `ubuntu-24.04` runner, in one
  job, **sequentially**: `LizTerm.App.csproj` declares no `<RuntimeIdentifiers>`, so each single-RID restore
  overwrites `project.assets.json`'s target, and packing both after publishing both fails the first with NETSDK1047.
- `AVALONIA_TOOLS_LICENSE_KEY`, the repository secret Parcel needs to run at all, is scoped to the `parcel pack`
  steps alone, by `env:` on each step rather than job- or workflow-wide. `ci.yml` and `platforms.yml` carry no
  secret, so a fork's pull request still runs both in full.
- Parcel names its output `{App}.{arch}.{Version}.{ext}`, with no OS token, so every package is renamed to
  `LizTerm-<rid>-<version>.<ext>` before upload. A GitHub Release has one flat asset namespace, and three platforms
  would otherwise each produce an identically named ZIP.
- `GeneralSettings.PackageName` makes the bundle, the Dock icon and the DMG read `LizTerm.app` rather than
  `LizTerm.App.app`, which Parcel otherwise derives from the assembly name. It renames only the outer bundle; the
  executable inside stays `LizTerm.App` (`LizTerm.App.exe` on Windows). **Do not "fix" the name by renaming the
  assembly**: Avalonia's resource URIs (`avares://LizTerm.App/...`) are keyed on it, and a rename breaks the
  terminal font, the window icons and the licence text at run time rather than at build time.

### Gates on each package

Each publish job extracts its archive and runs, against that extracted tree:

1. **`verify-bundled-engine.sh`**, which fails on a missing, duplicate or wrong-machine-type engine. The engine
   survives `PublishSingleFile` at `runtimes/<rid>/native/b3270[.exe]`, nested inside `LizTerm.app/Contents/MacOS/`
   on macOS, which is why the script searches at any depth and requires exactly one match.
2. **An engine start check** (`shared-verify-tls.sh`).
3. **`verify-app-launches.sh`**, which launches the real, extracted executable the way a user does — no headless
   platform and no self-test flag, since either would step around Skia initialisation, which is where the bug it
   exists for lived (see below). It sleeps five real seconds (that crash took about one, and a tight polling loop
   would pass a process still mid-death), checks the process is still alive, then kills it so the job does not hang
   on a GUI event loop, printing the captured stderr if it had already died. Linux runs it under `xvfb-run`, because
   Avalonia needs a display to get through Skia initialisation at all.

Steps 2 and 3 run only for `osx-arm64`, `linux-x64` and `win-x64`. The other three are cross-packaged on runners
that cannot execute them: `osx-x64` would need Rosetta on the publish runner, `linux-arm64` is packaged on an x64
runner, and `win-arm64`'s app is arm64 on an x64 runner. That limit is structural, and adding Rosetta to a publish
job is deliberately not done. For those three, `verify-bundled-engine.sh` is the only CI gate, and launching them is
left to the manual pass before a release: each headline archive, downloaded from a rehearsal run, extracted on the
platform it targets and used to connect to a real host
([packaging spec, section 8](superpowers/specs/2026-09-08-lizterm-m3e-packaging-design.md#8-testing-and-verification)).

### macOS signing and `Entitlements.plist`

Builds ship unsigned for now, by decision; notarization is named as the first post-v1 item. Parcel ad-hoc signs the
packaged executable with the hardened runtime (`codesign -dvvv` reports `flags=0x10002(adhoc,runtime)`) but signs
the bundled `libSkiaSharp.dylib`, `libHarfBuzzSharp.dylib` and `libAvaloniaNative.dylib` plain ad-hoc
(`flags=0x2(adhoc)`). An ad-hoc signature carries no Team ID, and the hardened runtime's library validation refuses
to `dlopen` any sibling library whose Team ID does not match the process's. With none on either side, every load of
a bundled dylib fails and Avalonia dies before it can open a window (`DllNotFoundException: libSkiaSharp`, "different
Team IDs"). A packaged build like that once passed every gate and launched clean in every rehearsal — until someone
actually ran it.

`Entitlements.plist`, at the repository root, fixes it with one entry: `com.apple.security.cs.disable-library-validation`.
Parcel discovers the file by convention next to `LizTerm.parcel` and merges it with the default entitlements it
always synthesizes (`network.client`, `network.server`, `files.user-selected.read-write`,
`files.bookmarks.document-scope`, `cs.allow-jit`) rather than replacing them; `codesign -d --entitlements -` on a
packed bundle shows all five beside it. **Treat it as load-bearing, not unexplained cruft.**

- Dropping the hardened runtime would also fix the crash and looks like the smaller change, but it is the wrong one:
  notarization requires the hardened runtime, so that path would have to be undone. The entitlement disables only
  the one check that was rejecting ad-hoc siblings and leaves the rest of hardened-runtime validation in place.
- `codesign --verify --deep --strict` passes on the broken bundle, because the failure is a runtime
  library-validation refusal, not a signature-integrity one. That gap is why `verify-app-launches.sh` exists.
- The bundled b3270 is signed `(adhoc,runtime)` like the executable, not like the dylibs. It escapes this failure not
  by being signed differently but by being a child process rather than something the app loads — which is also why
  none of the engine gates could have caught it.

### Publishing

The `release` job runs only when the trigger was a pushed tag: `github.event_name == 'push' && github.ref_type ==
'tag'`, because `ref_type` alone would also fire on a dispatch rehearsal run against an existing tag ref. It
downloads every `packages-*` artifact into one flat directory and passes archives before installers to
`gh release create`, publishing a **draft** rather than a live release. The ZIP is what a first-time visitor should
reach for; `gh release create` uploads assets concurrently, so passing archives first makes them likely, not
certain, to be listed first.
