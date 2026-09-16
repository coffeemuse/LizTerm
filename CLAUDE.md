# CLAUDE.md

Guidance for Claude Code in this repository.

## What this is

LizTerm is a cross-platform TN3270 client (.NET 10, Avalonia 12) for retro mainframe hobbyists. It does not implement
the 3270 data stream: it bundles `b3270` from the x3270 suite as a child process and speaks its newline-delimited
JSON protocol over stdin/stdout. Read the design spec, `docs/superpowers/specs/2026-09-03-lizterm-v1-design.md`,
before changing anything user-facing; later milestone specs sit beside it. Vista TN3270 is the behavioral reference
when the specs are silent. Open work is tracked in GitHub issues.

## Where things are documented

Each fact has one home. When a change makes a documented statement untrue, fix it where it lives, not in a copy.

| Topic | Home |
|---|---|
| End users: overview, download, first run, licensing | `README.md` — keep developer detail out of it |
| Using the app: keys, profiles, TLS, file transfer, capture | `docs/user-guide.md` |
| What changed in each release | `CHANGELOG.md` — new entries go under `## Unreleased`; the release notes copy each version's section |
| Building, testing, conventions, environment variables | `docs/development.md` |
| Engine builds and their gates | `docs/engines.md` |
| mvsMF: where its docs, source and the tested build disagree, and LizTerm's workarounds | `docs/mvsmf-compatibility.md` |
| Workflows, check names, caches, release, packaging, signing | `docs/ci-and-release.md` |
| Architecture overview for humans | `docs/architecture.md` |
| Implementation notes and pitfalls per project | `src/<Project>/CLAUDE.md`, `tests/CLAUDE.md` |
| Design history | `docs/superpowers/` — a record; do not rewrite old specs to match the code |

The nested `CLAUDE.md` files load automatically when you read a file in their folder. `native/CLAUDE.md` imports
`docs/engines.md`, and `.github/CLAUDE.md` imports `docs/ci-and-release.md` and `docs/engines.md`, so those arrive
the same way. Files at the repository root trigger no such load, so:

- Before touching `LizTerm.parcel` or the signing steps of `release.yml`'s macOS job, read `docs/ci-and-release.md`,
  "macOS signing and notarization". Signing is mandatory: without its four secrets the release fails by design, and
  an ad hoc macOS package would not launch.
- Before touching `global.json`, `Directory.Build.props` or `Directory.Packages.props`, read the conventions in
  `docs/development.md`.
- Before touching `.mcp.json` or `.claude/settings.local.json`, or driving the app through the Avalonia DevTools MCP,
  read the DevTools section of `src/LizTerm.App/CLAUDE.md`. In short: the licence key goes in
  `settings.local.json`'s `env`, never in `.mcp.json`.

## Commands

```bash
dotnet test LizTerm.slnx                       # full suite (~4 s warm); the live-host tests skip themselves
dotnet test tests/LizTerm.Backend.B3270.Tests  # one project
dotnet test tests/LizTerm.Backend.Mvsmf.Tests  # the mvsMF client against recorded exchanges
dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~ProfileStoreTests"                      # one class
dotnet test tests/LizTerm.Backend.B3270.Tests --filter "FullyQualifiedName~ReplayTests.Ibmlink_help_screen_replays_to_expected_state"  # one test
dotnet build LizTerm.slnx
dotnet run --project src/LizTerm.App           # picker; append "-- <profile-name>" or "-- host[:port]" to skip it
```

Tests use xunit.v3 in VSTest mode, so `--filter` takes the usual `FullyQualifiedName~` syntax. There is no lint or
format step. `Nullable` and `ImplicitUsings` are on solution-wide via `Directory.Build.props`, and every package
version lives only in `Directory.Packages.props` (`PackageReference` entries carry no `Version`).

Before calling anything done, run the zero-warning check; an incremental build hides warnings from projects it does
not recompile, and CI builds with `-warnaserror`:

```bash
dotnet build LizTerm.slnx --no-incremental 2>&1 | grep -c " warning "
```

A fresh worktree has no `native/out`, so the app and the engine-dependent tests need an engine; see
`docs/development.md`. A Homebrew b3270 set through `LIZTERM_B3270_PATH` runs the app, except ISPF (MVS) transfers,
and the live-host tests other than the ISPF one; the engine smoke, catalogue and trust-anchor tests and the ISPF live
test ignore the override and need a built engine.

## Rules that apply everywhere

- **Dependency rule** (enforced in review): `LizTerm.Core` depends only on the BCL and never mentions Avalonia,
  b3270 or mvsMF. `LizTerm.Backend.B3270` depends on Core and is the only project that knows b3270 exists;
  `LizTerm.Backend.Mvsmf` depends on Core and is the only project that knows mvsMF exists; the two backends never
  reference each other. `LizTerm.App` names the b3270 backend in exactly one place,
  `src/LizTerm.App/SessionFactory.cs`, and the mvsMF backend in exactly one place,
  `src/LizTerm.App/HostFileServiceFactory.cs`; everything else in App talks to `IEmulatorSession` and
  `IHostFileService`. That is what lets the App tests run against `FakeEmulatorSession` and `FakeHostFileService`
  and keeps a future managed engine possible. The App *tests* name each backend in exactly one place too (see
  `tests/CLAUDE.md`).
- **One window, one session, one profile.** `IEmulatorSession` is bound to its `SessionProfile` at construction.
- **Snapshots, never shared state.** The backend owns the mutable `ScreenBuffer` (single writer) and publishes
  immutable `ScreenSnapshot`s carrying the cursor, so screen and cursor never tear. The UI only ever sees snapshots.
- **Threading contract.** A backend raises all events on one dedicated thread, in order, and knows nothing about UI
  threads. The App layer marshals.
- **Zero-based coordinates.** Rows and columns are zero-based everywhere in Core and App. b3270 reports them
  one-based; the conversion happens only in `B3270Session.ApplyScreen`.
- **Licence headers.** Every hand-written `.cs`, `.axaml` and `.sh` file under `src/`, `tests/`, `native/build/` and
  `tools/` starts with three lines in its comment syntax — `This file is part of LizTerm.`, the copyright worded
  exactly as `LICENSE`'s first line, and `SPDX-License-Identifier: BSD-3-Clause` — after the shebang in a script,
  before the root element in `.axaml`. Build and configuration files are out of scope. `RepositoryHeadersTests`
  fails the suite for a missing one.
- **Field bugs get fixtures.** Every bug found against a real host should add a trimmed replay fixture
  (`docs/development.md#replay-fixtures`).
