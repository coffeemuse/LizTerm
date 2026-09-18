# Development

How to build, test and run LizTerm from source. For what the app does, see the [user guide](user-guide.md); for
how the pieces fit together, [architecture](architecture.md).

## Prerequisites

- The .NET 10 SDK, 10.0.4xx band. `global.json` pins it and rolls forward only within that band.
- A b3270 engine (next section).
- To build engines: the Xcode command line tools for macOS, Docker for Linux and Windows.

## Getting an engine

The app runs b3270 as a child process and cannot connect without one. LizTerm ships its own build, with the patches
in `native/patches` applied ([engines](engines.md#patches)), and that is the engine to develop and test against:

- Build one with `native/build/build-macos.sh`, `native/build/build-linux-docker.sh` or
  `native/build/build-windows-docker.sh` (see [engines](engines.md)), then rebuild the .NET projects so the engine
  is copied into their output.
- Download a CI-built one ([engines](engines.md#using-a-ci-built-engine)).

For quick work, `LIZTERM_B3270_PATH` can point at another b3270 4.2 or later, such as a Homebrew `x3270` install. It
lacks LizTerm's patches, so ISPF (MVS) file transfers are refused with it, and it is never what users run.

Without an engine the app shows an error instead of the profile picker, and the tests that need one skip.

## Commands

```bash
dotnet build LizTerm.slnx
dotnet test LizTerm.slnx
dotnet run --project src/LizTerm.App
```

Append `-- <profile-name>` or `-- host[:port]` to `dotnet run` to skip the picker.

Tests use xunit.v3 in VSTest mode, so `--filter` takes the usual syntax:

```bash
dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~ProfileStoreTests"
```

Before calling a change done, check for warnings with a non-incremental build; an incremental one hides warnings
from projects it does not recompile. CI builds in Release with `-warnaserror`, so this must print `0`:

```bash
dotnet build LizTerm.slnx --no-incremental 2>&1 | grep -c " warning "
```

## Conventions

- `Nullable` and `ImplicitUsings` are on solution-wide via `Directory.Build.props`. There is no separate lint or
  format step.
- Every package version lives in `Directory.Packages.props` (central package management); `PackageReference`
  entries carry no `Version`.
- **`global.json`**: supported builds stay on the current .NET LTS. When an SDK update replaces the pinned version,
  bump `version` — never widen `rollForward`.
  CI and developer machines must share one analyzer set for `-warnaserror` to mean the same thing everywhere. Keep
  the pin on the *current* 10.0 band: a stale one means a fresh .NET 10 SDK install cannot run `dotnet` in this
  repository at all.
- The version lives in both `Directory.Build.props` and `LizTerm.parcel`'s `GeneralSettings.Version`; the release
  workflow fails if they, and on a real release the tag, disagree.
- A change a user would notice adds a line under `## Unreleased` in `CHANGELOG.md`, in the same PR. Keep it to a
  headline and a sentence or two, linking the user guide or the issue for detail — the release notes are this text
  verbatim, and a reader scans them. The version bump renames that heading to `## <version>`, which a tagged release
  requires (see [CI and release](ci-and-release.md#the-version-job)).

### Licence headers

Every hand-written `.cs`, `.axaml` and `.sh` file under `src/`, `tests/`, `native/build/` and `tools/` starts with
these three lines, in that file's comment syntax — after the shebang in a script, before the root element in an
`.axaml` file:

```text
This file is part of LizTerm.
Copyright 2026 by CoffeeMuse
SPDX-License-Identifier: BSD-3-Clause
```

The copyright line is worded exactly as `LICENSE`'s own first line. Build and configuration files (csproj, the
props files, `LizTerm.slnx`, `LizTerm.parcel`, the workflow YAML) are deliberately out of scope.
`RepositoryHeadersTests` fails the suite for a file that is missing its header, so a new file is caught locally.

### The bundled user guide

`src/LizTerm.App/Assets/Docs/user-guide.html` is generated from `docs/user-guide.md` and is never hand-edited.
Editing the guide fails `UserGuideAssetTests.The_committed_html_is_what_the_converter_produces` until it is
regenerated:

```bash
LIZTERM_UPDATE_DOCS=1 dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~UserGuideAssetTests"
```

The converter is `tests/LizTerm.Core.Tests/Documentation/UserGuideHtml.cs` and handles only the constructs the
guide uses; `The_guide_uses_no_construct_the_converter_cannot_render` fails if the guide grows another. Extend the
converter rather than the guard.

### The 3270 font

`src/LizTerm.App/Assets/Fonts/3270-Regular.otf` is upstream [3270font](https://github.com/rbanffy/3270font)'s OTF
with one change. The font carries x3270's Operator Information Area glyphs — the boxed 4, the underlined A and B,
the broken wire, the clock, the human figure, the lock X and the rest — but upstream ships them without code points,
so no text can reach them. `tools/patch-3270-oia-font.py` maps them to U+E180 through U+E198, in the font's own
glyph order; `OiaGlyphs` in the App project names each code point, spelled as `\u` escapes because the characters
themselves are invisible in an editor or a diff. `OiaGlyphsTests` fails the suite if the script's list, those names
and code points, and the shipped file disagree. After a font refresh, run the script on the new file:

```bash
pip install fonttools
tools/patch-3270-oia-font.py src/LizTerm.App/Assets/Fonts/3270-Regular.otf
```

It is idempotent, and it refuses a file in which that block already maps to something else, because `OiaGlyphs`
would then draw the wrong symbols.

### The app icon

`src/LizTerm.App/Assets/Icons/` holds two masters: `lizterm.png`, with "3270" lettered above Liz, and
`lizterm-small.png`, the same tile unlettered, since the lettering is a green smear below 64 px. The other files
are built from them:

- `lizterm.icns` and `lizterm.ico`, the packaged icons, with every image of 48 px or less from the small master;
- `lizterm-256.png`, the lettered mark on the splash, in About and in the README;
- `lizterm-window.ico`, every window's title bar and taskbar icon, set once by a style in `App.axaml`. It is
  unlettered at every size, from 16 to 128 px, because Windows draws it at 16 to 32 px, choosing the nearest
  entry, and X11 takes the largest entry.

After changing either master, rebuild them on a Mac with ImageMagick installed:

```bash
tools/build-app-icons.sh
```

## Environment variables

| Variable | Effect |
|---|---|
| `LIZTERM_B3270_PATH` | Development only: use this b3270 instead of the bundled one. It lacks LizTerm's [patches](engines.md#patches). |
| `LIZTERM_WIRE_LOG` | Append every protocol line, in both directions, to this file. Help > Wire Log does the same from inside the app. |
| `LIZTERM_MENU` | `native`, `classic` (the in-window menu) or `both`, seeding the launched instance's menu style on macOS (see the [user guide](user-guide.md#menus)); any other value, and any value at all off macOS, leaves the saved preference to decide. It seeds rather than overrides. The macOS application menu is not affected. |
| `LIZTERM_TEST_HOST` | `host[:port]`; enables the live integration tests, which otherwise skip. |
| `LIZTERM_TEST_TLS` | `1` if the test host speaks TLS. |
| `LIZTERM_TEST_VERIFY_CERT` | `0` to accept the test host's self-signed certificate. |
| `LIZTERM_TEST_USER`, `LIZTERM_TEST_PASSWORD` | Additionally enable the IND$FILE round-trip test. |
| `LIZTERM_MVSMF_URL` | The mvsMF base, for example `http://host:8080/zosmf`; with the three below, enables the live mvsMF tests. |
| `LIZTERM_MVSMF_USER`, `LIZTERM_MVSMF_PASSWORD` | Credentials for the live mvsMF tests and for `tools/record-mvsmf-fixture.sh`. |
| `LIZTERM_MVSMF_SCRATCH_PDS` | A PDS the live mvsMF tests may write the member `LIZITEST` into and delete it from. |
| `LIZTERM_TEST_BELL` | Any non-blank value runs the system-alert ring test on macOS and Windows, where it is audible; unset, it skips there. On Linux the ring is a no-op and the test always runs. |
| `LIZTERM_REQUIRE_ENGINE` | Any non-blank value makes the engine smoke test fail, instead of skip, when the test output has no bundled engine. CI sets it; leave it unset locally. |

## Tests

`dotnet test LizTerm.slnx` runs five projects:

- **`LizTerm.Core.Tests`**: the domain model, profiles, certificates, and the repository-wide licence header check.
- **`LizTerm.Backend.B3270.Tests`**: the b3270 protocol against a fake engine process, plus replay tests that feed
  recorded engine output through a session and assert the resulting screens.
- **`LizTerm.Backend.Mvsmf.Tests`**: the mvsMF client against recorded HTTP exchanges, and its TLS trust against a
  loopback server.
- **`LizTerm.App.Tests`**: view models against a fake session, and controls on Avalonia's headless platform.
- **`LizTerm.Integration.Tests`**: the engine smoke test, which starts the bundled engine, and the live tests
  against a real host and a real mvsMF.

The engine smoke test skips when no bundled engine is in the test output — unless `LIZTERM_REQUIRE_ENGINE` is set —
but an engine that is present and not executable always fails it, so a forgotten `chmod +x` cannot pass as a skip.

### Live host tests

Set `LIZTERM_TEST_HOST`, plus `LIZTERM_TEST_TLS` and `LIZTERM_TEST_VERIFY_CERT` as the host needs. With
`LIZTERM_TEST_USER` and `LIZTERM_TEST_PASSWORD` as well, the IND$FILE test logs on to TSO, sends and receives
`LIZTERM.ITEST` under the user's prefix, and deletes it.

The password is typed through the session, so a wire log of that run holds it on its outbound side — never commit
one. Keep these variables in a file outside the repository and `source` it in the shell that runs `dotnet test`,
rather than exporting them on a command line.

### Live mvsMF tests

Set all four `LIZTERM_MVSMF_*` variables. The tests read the server information, list the scratch PDS, upload a
small text member through the same checks the app uses, verify and download it, delete it, and make two sign-in
attempts with a wrong password. The wrong-password test fails two sign-ins per run; on a host whose security
product revokes a userid after failed attempts, point it at a userid that can take that. Keep them with the other
live-test variables, in the file outside the repository that you `source`.

### Replay fixtures

`tests/LizTerm.Backend.B3270.Tests/Fixtures/` holds raw b3270 output recorded from real sessions and host traces;
its [README](../tests/LizTerm.Backend.B3270.Tests/Fixtures/README.md) documents each one. Every bug found in the
field should add a trimmed fixture. b3270 fixtures have two recorders:

- `tools/wirelog-to-fixture.sh <wire.log> <out.jsonl>` turns a `LIZTERM_WIRE_LOG` file from a real session into a
  fixture: inbound lines only, timestamps stripped.
- `tools/record-fixture.sh <trace.trc> <out.jsonl> [model] [playback-step]` replays an x3270 `.trc` host trace
  through b3270 and saves its output. It needs x3270's `playback` tool, built by `native/build/build-playback.sh`,
  which applies LizTerm's patches and so needs `patch` installed (macOS has it).

`tests/LizTerm.Backend.Mvsmf.Tests/Fixtures/` holds recorded mvsMF exchanges, one per file, made by
`tools/record-mvsmf-fixture.sh`; its [README](../tests/LizTerm.Backend.Mvsmf.Tests/Fixtures/README.md) lists them.
Re-record them when checking a new mvsMF build against [the compatibility log](mvsmf-compatibility.md).

Before committing a fixture, cut any logon from it and replace real host addresses.

## CI

Every pull request must pass `ci.yml`'s `test` job: a Release build with warnings as errors, then the full suite, on
Linux. Pull requests that touch code or the native build also run the Platforms workflow on macOS, Linux and
Windows. See [CI and release](ci-and-release.md).
