# LizTerm Milestone 3, plan 3e: packaging and release

Date: 2026-09-08. Parent spec: `2026-09-03-lizterm-v1-design.md` (section 8.1 names all six targets; section
8.4 asks for `dotnet publish` self-contained per runtime identifier, a macOS app bundle, archives, and GitHub
Releases). Predecessors: `2026-09-06-lizterm-m3-ci-design.md` (plan 3a, whose `platforms.yml` this plan
restructures), `2026-09-07-lizterm-m3c-linux-engines-design.md` (plan 3c, whose pinned-source, static-link and
prove-the-gate-rejects discipline this plan extends to macOS) and
`2026-09-07-lizterm-m3d-windows-engine-design.md` (plan 3d, which settled that `win-arm64` ships the `win-x64`
engine and left placing it to this plan). Status: approved in discussion on 2026-09-08; awaiting review of
this text.

## 1. Purpose

Every engine LizTerm needs exists and is proven to start, except one: `osx-x64`. Nothing, however, is
*shipped*. There is no release workflow, no app bundle, no icon, no LICENSE, and the csproj rule that bundles
the engine can only ever bundle the build host's own. A hobbyist cannot download LizTerm; they can only clone
it and build it, which is the audience this project does not have.

This plan is the difference between "the code is done" and "a hobbyist can download LizTerm". It produces six
self-contained publishes with the right engine in each, packages them with Avalonia Parcel, and uploads them
to GitHub Releases on a tag.

Milestone 3 after this plan: **3f**, the scheduled integration lane against a Hercules TK4-/TK5 container.

Decisions taken in the brainstorm on 2026-09-08:

- **v1 ships unsigned.** An Apple Developer ID is available and will be used, but not for v1. Notarization
  becomes the first post-v1 item rather than an open-ended "later", because macOS 15 made the unsigned
  experience materially worse (section 2.3).
- **Parcel owns packaging, gated on a spike.** Parcel is the only route on which "add notarization later" is a
  configuration change rather than a new pipeline. Its cost — a proprietary licence key on the critical path
  of releasing an open-source project — is real, is confined to one workflow, and is checkable before this
  design is built (section 5.1). The hand-rolled alternative is recorded as a contingency, not maintained as a
  second path (section 5.5).
- **Archives are the headline download; installers are the second row.** A ZIP per platform is what the
  release notes point a first-time user at. DMG, DEB, RPM and NSIS sit below them for people who want them.
- **`osx-x64` is a cross build on an arm64 Mac, not an Intel runner.** `macos-13` is retired and
  `macos-15-intel` is scheduled to disappear in August 2027, so the runner route is a stopgap with a known
  expiry. Cross-building matches what `win-x64` and the Linux engines already do.
- **Both macOS engines link OpenSSL built from pinned source.** Homebrew cannot supply an x86_64 OpenSSL on an
  Apple Silicon Mac, so the cross build needs a source build regardless; giving only the new leg one would
  leave the two Mac engines linking different OpenSSL versions from different provenances, with only one
  pinned. Plan 3c's reasoning — a statically linked library never receives the distribution's security
  updates, so its version has to be ours to bump deliberately — applies to Homebrew exactly as it applies to
  AlmaLinux.
- **Packaging runs on each platform's native runner.** This is a deliberate non-use of a feature the licence
  pays for (section 5.2).

## 2. Facts that shape the design

### 2.1 What Parcel is, and what it costs

Parcel is a .NET tool (`dotnet tool install --global AvaloniaUI.Parcel`) with a GUI and a CLI. The CLI is
**not** in the free Community edition, which is additionally single-platform, GUI-only, capped at 200 MB of
output, and adds Parcel branding to installers. Everything this plan needs — the CLI, cross-platform
packaging, signing, notarization — is the paid tier, activated by `AVALONIA_TOOLS_LICENSE_KEY` or
`--license-key`.

Avalonia's Schedule 1A states that Plus, Pro and Enterprise licences allow installation on automated build
machines and CI servers without a separate Named User licence, provided the build server is used only for
automated compilation, packaging and deployment and every individual who interactively uses the software holds
a licence. A repository secret is therefore a licensed use, not a workaround.

Against that: `AvaloniaUI/AvaloniaPro#81` records a Plus subscriber whose build server rejected their portal
key with `This subscription doesn't provide online license keys` after it had previously worked. That is the
specific failure this plan's spike exists to rule out before anything is built on it.

The CLI is `parcel pack ./LizTerm.parcel -r <rid> -p <format> -o <dir>`, with `-r`/`-p` repeatable and
`--no-build` consuming an existing publish tree. The `.parcel` file's schema is not publicly documented; the
intended workflow is that the GUI authors it once and the CLI consumes it headlessly. This plan follows that
workflow: the file is generated on the developer's Mac, committed, and never touched by CI.

### 2.2 The engine copy is keyed on the wrong RID

`src/LizTerm.App/LizTerm.App.csproj` uses `$(NETCoreSdkRuntimeIdentifier)` — the *host's* RID — for both the
source directory and the link path. Publishing `-r linux-arm64` on an x64 runner therefore bundles nothing at
all, silently: the `Condition="Exists(...)"` simply fails and the copy does not happen.

Those two uses are also no longer the same value. `B3270Locator.Candidates` resolves
`runtimes/<RuntimeInformation.RuntimeIdentifier>/native/`, so a `win-arm64` app looks under
`runtimes/win-arm64/native/` — and plan 3d settled that what belongs there is the **x64** binary, run under
Windows 11's emulation. The source directory and the link path are two different RIDs.

### 2.3 macOS 15 closed the Gatekeeper bypass

The parent spec's plan was that "release notes carry the right-click Open instruction until then". That
instruction no longer works. macOS 15 removed the Control-click override for software that is not signed and
notarized, and 15.1 closed the remaining routes. A user must now launch the app, have it refused, open System
Settings > Privacy & Security, and click "Open Anyway" under the Security heading — a button that appears only
for roughly an hour after the failed launch, so a user who goes looking later has to trigger the refusal
again.

This does not change the decision to ship v1 unsigned, but it changes what the release notes must say, and it
is why notarization is named as the first post-v1 item rather than left undated.

### 2.4 Intel Mac runners are ending

`macos-13` is retired. `macos-15-intel` exists and is stated to be the last x86_64 macOS image on GitHub
Actions, available until August 2027. Any design that reaches `osx-x64` through a native Intel runner acquires
that date as its own expiry.

### 2.5 Two properties of GitHub Actions this plan relies on

**`actions/cache` is repository-scoped, not workflow-scoped.** The engine caches `platforms.yml` populates are
restorable by a release run, so a tagged release cut from a green `main` restores five engines rather than
building them. This is what makes calling the engine jobs from a second workflow affordable.

**`actions/download-artifact` does not preserve the executable bit.** The README already documents this for
humans downloading an engine by hand. Inside `platforms.yml` it has never mattered, because each engine job
builds and uses its binary without an artifact round trip. This plan introduces exactly that round trip, and a
missing `chmod +x` would ship macOS and Linux releases whose engine `B3270Locator.Find` rejects with "is not
executable" — a failure visible to a user at connect time and to nobody during the build.

## 3. The pipeline

### 3.1 `engines.yml`, a reusable workflow

The five engine builds and their gates move into `.github/workflows/engines.yml` with `on: workflow_call`.
`platforms.yml` becomes a trigger wrapper that calls it on its existing push, dispatch and path-filtered
pull-request events. `release.yml` calls the same workflow.

The alternative — a release workflow that rebuilds the engine logic — would give the repository two
definitions of "an engine that has been proven to start", which is precisely the drift plan 3d's two-name
artifact hop exists to prevent. One definition, two callers.

`engine-macos` gains a second matrix leg for `osx-x64`, mirroring `engine-linux`'s two-leg shape. Both legs
run on `macos-15`; the difference between them is the target architecture, not the runner.

The check names branch protection sees will change when jobs move under a reusable workflow. `ci.yml`'s `test`
is the only required check and is untouched, but the platform check names must be re-recorded in CLAUDE.md
after the first run, the way plan 3c recorded `engine-linux (ubuntu-24.04, linux-x64)`.

### 3.2 `release.yml`

Triggered on `push: tags: ['v*']`, plus `workflow_dispatch` for rehearsal against a non-tag ref (which
produces artifacts and no release).

```
tag v*  ->  release.yml
              engines      uses: ./.github/workflows/engines.yml     5 engines, gate-proven
              publish      dotnet publish -r <rid>  x6               engine copied in by target RID
              package      parcel pack              x6               archives + installers
              release      gh release create                          archives first, installers second
```

`publish` and `package` are one job per platform, on that platform's native runner, each handling its own
RIDs: `macos-15` does `osx-arm64` and `osx-x64`, `ubuntu-24.04` does `linux-x64`, `ubuntu-24.04-arm` does
`linux-arm64`, `windows-latest` does `win-x64` and `win-arm64`.

Each publish job downloads the engine artifacts `engines.yml` produced into `native/out/<rid>/` and, on macOS
and Linux, runs `chmod +x` over them before building — the fix for section 2.5's second property, applied at
the one point in the pipeline where an engine crosses an artifact boundary. The Windows job downloads
`b3270-win-x64` only: it feeds both `win-x64` and `win-arm64`, since section 4.1's source RID maps the latter
to the former, and a PE has no executable bit to restore.

A tag whose version does not match `Directory.Build.props`'s `Version` fails the job before anything is
built. A release built from a tag that disagrees with the tree is a release nobody can reproduce.

## 4. Build-side changes

### 4.1 `LizTerm.App.csproj`: a source RID and a target RID

Two properties replace the single `$(NETCoreSdkRuntimeIdentifier)`:

- **Target RID** — `$(RuntimeIdentifier)` when publishing for one, `$(NETCoreSdkRuntimeIdentifier)` otherwise
  (development builds, the test projects, `dotnet run`). This is the link path, and it must equal what
  `RuntimeInformation.RuntimeIdentifier` will report at run time.
- **Source RID** — the target RID, except `win-arm64`, which maps to `win-x64`. This is the directory under
  `native/out/` the copy reads from.

The `Condition="Exists(...)"` moves to the source RID. The existing behaviour — no engine directory, no copy,
`BackendUnavailableException` at connect time with `LIZTERM_B3270_PATH` as the documented override — is
unchanged for developers.

### 4.2 `build-macos.sh`: two architectures, pinned OpenSSL

The script stops deriving its RID from `uname -m` and takes it as an argument (defaulting to the host's, so
existing developer usage and `native/build/build-macos.sh` in the README keep working). For `osx-x64` on an
arm64 host it configures with `--host=x86_64-apple-darwin` and `CC="clang -arch x86_64"`.

OpenSSL comes from `fetch-openssl.sh` for both architectures, through `shared-fetch-tarball.sh` and
`shared-sha256.sh`, exactly as `build-linux.sh` uses them — configured per architecture (`darwin64-arm64-cc`
and `darwin64-x86_64-cc`) into a per-architecture prefix under `native/build-tmp/`. The staging directory that
currently exists to hide Homebrew's dylibs from the linker disappears with Homebrew: a source build with
`no-shared` produces only archives.

Each prefix gains the `.pin` stamp `build-linux.sh` already uses, holding the SHA-256 of `fetch-openssl.sh`
and of `build-macos.sh`, and reused only when both the archive and a matching stamp are present — for the
reason plan 3c gives: a prefix has two owners, the fetcher holding the version and checksum and the build
script holding the configure flags that shape what is built from them.

`shared-*.sh` is in both engine cache keys by construction, and `fetch-openssl.sh` is currently in the Linux
key only. It must be added to the macOS key by this plan, or a bumped OpenSSL would rebuild Linux and silently
reuse a cached Mac engine linking the old one.

Removing Homebrew also removes the "Ensure OpenSSL 3 is installed" step from the macOS CI job and reduces the
README's developer prerequisites to the Xcode command line tools.

### 4.3 `verify-macos.sh`: the architecture check

The gate gains a first check: `lipo -archs` must report exactly the expected architecture, passed as an
argument. It runs before the dependency scan, so a wrong-architecture binary is rejected by a message naming
the mistake.

This is plan 3d's lesson arriving on the platform that now needs it. A cross build's most plausible failure is
emitting the wrong architecture, and an x86_64 b3270 links exactly the same system libraries an arm64 one
does — `otool -L` cannot see the difference, just as the import allowlist could not see a 32-bit PE.

### 4.4 An icon

The app has no icon: there is no `Icon=` anywhere in `src/LizTerm.App`, and `Assets/` holds the 3270 font and
its licence and nothing else. One source image (1024px PNG or SVG) is committed and rendered to `.icns`,
`.ico`, and hicolor PNGs for the `.desktop` entry. `Window.Icon` is set so Linux and Windows title bars and
task switchers are not blank, and the file feeds Parcel's per-platform icon settings.

### 4.5 Version stamping

`AppVersion.Current` already reads the informational version and `AboutWindow` already renders it, so this is
`-p:Version=` derived from the tag plus the agreement check in section 3.2. No UI work.

## 5. Parcel

### 5.1 The spike, which gates this spec

Before implementation begins, one session establishes three things the public documentation does not answer:

1. **The licence key activates the CLI headlessly**, on a Linux runner-like environment, from an environment
   variable — the failure `AvaloniaPro#81` records.
2. **Which RID and format combinations actually work.** `linux-arm64` with DEB and RPM, and `win-arm64` with
   NSIS, are the two this plan depends on and the two the documentation does not confirm.
3. **The `.parcel` schema**, by authoring `LizTerm.parcel` in the GUI and reading the result.

Its output is a committed `LizTerm.parcel` and a recorded table of working combinations. If (1) fails, section
5.5 applies. If (2) fails for a combination, that platform ships its headline archive only, and the spec
records which.

### 5.2 Packaging runs on native runners

Parcel Plus can build a DMG from Linux and sign Apple bundles off-macOS through `rcodesign`. This plan does
not use that, because `engines.yml` already puts the pipeline on `macos-15`, `ubuntu-24.04`, `ubuntu-24.04-arm`
and `windows-latest`. Cross-packaging would buy nothing and would add variables — cross-platform Mach-O ad hoc
signing, Unix modes written by a Linux host into a macOS bundle — whose failures appear to a user opening a
download rather than to CI. The capability stays available for a future in which the runner set shrinks.

**Amended after the first packaging run: `linux-arm64` is the one forced exception.** Parcel's Linux CLI
ships an x64-only native binary (`tool-linux-x64/parcel`), so on an arm64 Linux runner it does not fail to
package — it fails to *start*, with `Exec format error`. Nothing about the configuration can change that. So
`linux-arm64` is packaged from the `ubuntu-24.04` runner, in the same job that handles `linux-x64` and
sequentially after it, because each single-RID restore replaces `project.assets.json`'s target and a publish
must be followed immediately by its own pack. Targeting the RID cross-architecture is well supported — the
spike packed `linux-arm64` DEB, RPM and ZIP from an arm64 Mac — it is only running Parcel *on* arm64 Linux
that is impossible.

### 5.3 Formats

| Platform | Headline | Second row |
| --- | --- | --- |
| macOS | ZIP containing the `.app` | DMG |
| Linux | ZIP | DEB, RPM |
| Windows | ZIP | NSIS |

The formats are chosen for what works unsigned. **PKG is skipped**: a macOS installer package that is neither
signed nor notarized is a worse experience than the DMG beside it, and the reason to want one — Mac App Store
distribution — brings the app sandbox with it (section 10). **MSIX is skipped**: an unsigned MSIX cannot be
installed at all without developer mode, so it is a format that does not function under this plan's premise.

RPM is included because the glibc floor is RHEL 8's: plan 3c chose the AlmaLinux 8 base precisely for that
distribution family, and shipping only a DEB would skip the audience the floor was designed around.

Parcel emits ZIP, not `.tar.gz`, so the Linux headline is a ZIP rather than the tarball the parent spec named.
Parcel preserves Unix modes in ZIPs, which is the property that matters for b3270; adding a tar step for one
platform would put a second archive mechanism in the pipeline to satisfy a convention.

### 5.4 The key is confined to one workflow

`AVALONIA_TOOLS_LICENSE_KEY` becomes a repository secret referenced only by `release.yml`, which is triggered
by tags on the main repository. `ci.yml` and `platforms.yml` remain key-free, so contributors, fork pull
requests and every ordinary check keep working with no licence at all. The proprietary dependency sits on
releases and nothing else, which is the narrowest place it can sit.

### 5.5 The contingency, recorded and not built

If the spike's first question fails, the fallback is a script that assembles `Contents/MacOS`,
`Contents/Resources` and an `Info.plist` into a `.app`, and archives the six publish trees directly. It is
written here so that it is a decision already taken rather than one made under a tag. It is explicitly **not**
built alongside Parcel: a maintained second packaging path would hand-roll the exact work Parcel exists to do
and then run Parcel beside it, paying for both.

## 6. The gate

Five engines now cross an artifact boundary into six publish trees. The mistake this pipeline can make
silently is putting a correctly-shaped **wrong** engine into a tree: it builds, packages, uploads, and fails
at connect time on a user's machine.

### 6.1 Against the extracted package, per RID

**Amended after the section 5.1 spike; the original text gated the publish tree, and that was wrong.** The
spike established that `parcel pack` always runs its *own* `dotnet publish` into a randomized temporary
directory, with or without `--no-build` — what `--no-build` consumes is the ordinary MSBuild build output
under `bin/Release/net10.0/<rid>/`, which a `dotnet publish -r <rid>` leaves behind whatever its `-o` says. A
gate on the `-o` tree would therefore verify a directory that is never shipped.

So the gate runs against the **extracted package**: `runtimes/<rid>/native/b3270` (or `b3270.exe`) must be
present inside it, exactly once, must be executable on Unix, and must report the architecture that RID
expects. It is searched at any depth, because the macOS layout nests it under `LizTerm.app/Contents/MacOS/`;
more than one match is its own rejection, since a package carrying two engines is not one anybody reasoned
about.

Machine type is read from the file header rather than with `lipo`, `file` or `objdump`: one script runs on all
three runner operating systems and those tools are not all present on all three.

Moving the gate here also answers a question the publish tree could not. `LizTerm.parcel` sets
`PublishSingleFile`, so whether b3270 survives as a loose file beside the executable — where
`AppContext.BaseDirectory` and `B3270Locator` will look for it — is a property of the packaged output, not of
a plain publish. The spike could not answer it either: it ran with no engine present at all.

The architecture check is the load-bearing one. It is the only check that distinguishes a wrong engine from a
right one, because a wrong engine of the right shape passes every other check in this pipeline.

### 6.2 After packaging

Section 6.1's gate runs on that extracted tree, and then `shared-verify-tls.sh` is run against the engine
inside it. Together they prove three things: the archive round trip preserved the executable bit, the binary still
starts, and — on macOS — the `.app` and ZIP round trip did not invalidate the ad hoc signature the SDK and the
linker applied.

Three combinations cannot run this check where they are built: `osx-x64`, because the runner is arm64;
`win-arm64`, because the runner is x64; and `linux-arm64`, because section 5.2's amendment moves its
packaging to an x64 runner that cannot execute an arm64 binary. Those get section 6.1's architecture
assertion only.

For `linux-arm64` the loss is smaller than it looks: `engines.yml` already starts that engine on a real arm64
runner, through `verify-linux-start.sh` and through the whole suite run with `LIZTERM_REQUIRE_ENGINE=1`. What
goes unproven is not that the engine runs, but that the archive round trip preserved it — and the executable
bit, which is the mode that would break it, is still checked by section 6.1. This is the same split plan 3d made between `engine-windows` and
`test-windows` — each machine proves the half it can — and the spec states it rather than implying six proofs
where there are four.

### 6.3 Proving the gate rejects

The architecture check gets one negative case: an engine placed in the wrong RID's tree must be rejected, and
by the message that check prints rather than by a bare non-zero exit. A guard that cannot fail is not a guard,
and this is the guard whose silent failure ships a broken release.

## 7. Repository changes

- `.github/workflows/engines.yml` — new; the engine jobs and gates, `on: workflow_call`.
- `.github/workflows/platforms.yml` — reduced to triggers calling `engines.yml`.
- `.github/workflows/release.yml` — new; publish, package, release.
- `src/LizTerm.App/LizTerm.App.csproj` — source RID and target RID.
- `src/LizTerm.App/Assets/` — the icon source and its derived forms; `Window.Icon` in the views.
- `native/build/build-macos.sh` — architecture argument, cross build, pinned OpenSSL, `.pin` stamps.
- `native/build/verify-macos.sh` — the architecture check.
- `native/build/verify-bundled-engine.sh` — new; section 6.1's gate, as a script rather than inline YAML,
  because three packaging jobs call it and a gate that cannot be run locally cannot be tested.
- `LizTerm.parcel` — new; authored in the spike (section 11), and amended by the packaging work to carry the
  version, since Parcel does not read the project's own.
- `native/build/fetch-openssl.sh` — added to the macOS engine cache key.
- `LizTerm.parcel` — new, generated by the GUI in the spike.
- `LICENSE` — new; the choice is the repository owner's.
- `README.md`, `CLAUDE.md` — developer prerequisites lose Homebrew; the release process, the new check names,
  and the corrected macOS first-run instruction are recorded.

## 8. Testing and verification

The existing suite is unaffected: no Core, Backend or App behaviour changes. `EngineSmokeTests` continues to
resolve the bundled engine from the test output, and the csproj change preserves the development path it
depends on.

What is new is verified by the pipeline itself, in section 6, plus one manual pass before the first tag: each
headline archive downloaded from a rehearsal run on the platform it targets, extracted, and used to connect to
a real host — the check no CI job in this plan performs, because none of them can run a GUI.

## 9. Prerequisites

Two items block the first tag and are the repository owner's to supply: the **LICENSE** choice, and an **icon
source**. Neither blocks starting the implementation.

## 10. Out of scope

Code signing, notarization and Authenticode — deferred by decision, and named as the first post-v1 item.
PKG and Mac App Store distribution: the App Store route requires the app sandbox, and a bundled child process
like b3270 is permitted there only inside the bundle, signed with the same team identifier, inheriting the
sandbox — real work rather than one more output format. Package managers (Homebrew cask, winget, Flatpak,
AUR). Auto-update. A native `win-arm64` engine, which still needs upstream host-detection patches. The
scheduled integration lane and its Hercules container, which is plan 3f.

## 11. Spike results

Run on Robert's Mac (arm64, macOS 25.6, .NET SDK 10.0.400) on 2026-09-08, from a clean worktree at commit
`ae6cc15`. `AvaloniaUI.Parcel` was already installed as a global tool; `parcel --version` reported
`1.1.1+9542be03f7658c15639cd5e96c7ad473a6185869`.

### 11.1 The licence key activates the CLI headlessly

```
env -i HOME="$HOME" PATH="$PATH" AVALONIA_TOOLS_LICENSE_KEY="$AVALONIA_TOOLS_LICENSE_KEY" parcel pack --help
```

printed the `pack` help text (usage, `-o`/`-r`/`-p`/`--no-build`/`--license-key`/`-v`) with no licensing
exception, in an environment stripped to `HOME`, `PATH` and the key alone — the condition CI is in. The
`AvaloniaPro#81` failure does not reproduce here. Section 5.5's contingency is not needed.

### 11.2 The `.parcel` schema

The GUI could not be driven (no display, no way to click Save As), so the brief's CLI-only path was taken.
Hand-authoring against error messages alone stalled: an empty `{}` and a guessed `{"Project": "..."}` /
`{"project": "..."}` both produced the same generic `System.IO.FileNotFoundException: Could not find the
project file`, with no field name in the message even at `-v diagnostic`. `ilspycmd` (already installed)
decompiles only `AvaloniaUI.Parcel.Runner.dll`, a thin launcher; the actual CLI logic ships as a ~132 MB
NativeAOT `AvaloniaUI.Parcel.app` extracted at first run, which is machine code, not IL, so it does not
decompile. `strings` against it turned up bare property names (`BundleIdentifier`, `Company`,
`SigningIdentity`, ...) but no structure connecting them.

The schema came from Avalonia's own published docs instead, which are server-rendered (a raw `curl` fetch
returns the full content, not a client-side app shell): `docs.avaloniaui.net/tools/parcel/configuration-reference`
documents five top-level sections — `GeneralSettings`, `PublishSettings`, `Win32Settings`, `MacOsSettings`,
`LinuxSettings` — with every property's exact JSON path, type, and default (for example
`GeneralSettings.NetProjectPath`, `MacOsSettings.BundleIdentifier`, `MacOsSettings.CreateBundle`). That page
supplied the field names below; every one of them was then confirmed by running `parcel pack` against them,
not taken on the docs' word alone. This is a legitimate way to answer "what does a genuinely GUI-authored file
look like": Parcel resolves and writes back relative paths from the `.parcel` file's own location the same way
regardless of which tool wrote the JSON, and the docs describe the one schema both the GUI and the CLI read.

The committed `LizTerm.parcel`:

```json
{
  "GeneralSettings": {
    "NetProjectPath": "src/LizTerm.App/LizTerm.App.csproj",
    "ApplicationName": "LizTerm",
    "Company": "CoffeeMuse"
  },
  "PublishSettings": {
    "PublishSingleFile": true
  },
  "MacOsSettings": {
    "CreateBundle": true,
    "BundleIdentifier": "dev.coffeemuse.lizterm"
  }
}
```

`PublishSingleFile` and `CreateBundle` are not in the brief's four bullet points, but both are documented as
"enabled for new Parcel projects" — the GUI's own default when it creates a config — and both are load-bearing
rather than cosmetic: packaging failed without them (`DMG packaging requires "Create Bundle" to be enabled`,
then `Application was published without PublishSingleFile=true, signing might fail` followed by a hard failure
signing the unbundled `.dll`/`.json` files next to the executable). No signing identity, no notarization
credentials, and no icon path are set anywhere in the file, per the task's constraints. This config was
verified twice: once driving every RID×format combination in 11.4 with `--no-build`, and once end-to-end with
`parcel pack ./LizTerm.parcel -r osx-arm64 -p zip -p dmg` and no prior publish at all, into a scratch directory,
which built, signed ad hoc, and packaged from nothing.

### 11.3 What `--no-build` actually reads

The brief's step 4 assumed `--no-build` needed pointing at a publish directory; `parcel pack --help` has no
such option, which is what made this a question rather than a formality. The answer, established by observing
Parcel's own `-v detailed` output and then forcing two failures:

**Parcel always runs its own `dotnet publish`, into its own directory, whether or not `--no-build` is given.**
Every invocation logs a generated MSBuild fragment and a command line of the shape

```
dotnet publish src/LizTerm.App/LizTerm.App.csproj --nologo --tl:off --runtime <rid> \
  -p:PublishProfileFullPath=<output>/temp/<rid>/profiles/<rid>.pubxml --disable-build-servers --framework net10.0 [--no-build]
```

targeting `<PublishDir>` = `<output>/temp/<rid>/<random-token>/` — a fresh random subdirectory under
Parcel's *own* `-o`, never the tree from any earlier `dotnet publish`. `--no-build` on `parcel pack` is
forwarded verbatim as `--no-build` on that inner `dotnet publish` call; it does not change *where* Parcel
publishes, only whether that inner command recompiles first.

What it does change is what has to already exist, because `dotnet publish --no-build` is `dotnet`'s own
contract, not Parcel's: it requires the ordinary MSBuild **build** output for that project, configuration,
target framework and RID to already be sitting at
`<project-dir>/bin/<Configuration>/<TargetFramework>/<RuntimeIdentifier>/` — here,
`src/LizTerm.App/bin/Release/net10.0/<rid>/`. Proven by moving that directory aside and re-running
`parcel pack --no-build -r osx-arm64 ...`: it failed with the *inner* `dotnet publish` reporting

```
error MSB3030: Could not copy the file ".../bin/Release/net10.0/osx-arm64/LizTerm.App.runtimeconfig.json" because it was not found.
error MSB3030: Could not copy the file ".../bin/Release/net10.0/osx-arm64/LizTerm.App.deps.json" because it was not found.
```

Restoring that directory with a plain `dotnet publish src/LizTerm.App -c Release -r osx-arm64 --self-contained
-o <anywhere>` — exactly the brief's step 4 command, `-o` and all — made `--no-build` succeed again. **The
`-o` value in that publish is irrelevant to Parcel.** A `dotnet publish -r <rid>` run always leaves the
ordinary build output behind in `bin/<Configuration>/<TargetFramework>/<RID>/` as a side effect of the Build
step that runs before Publish, regardless of what `-o` sends the *publish* output to; that side effect,
not the `-o` directory, is the only thing `--no-build` depends on. Later tasks do not need to make their
`dotnet publish` output land at any particular path — they only need to have run `dotnet publish -c Release
-r <rid> --self-contained` for that RID at all, with the default `Configuration`/`TargetFramework` Parcel's
generated profile also uses (`Release`, `net10.0`).

One consequence worth carrying into Task 9's script: **`LizTerm.App.csproj` here has no `<RuntimeIdentifiers>`
list**, so each single-RID `dotnet publish -r <rid>` does its own scoped restore, and that restore *replaces*
`src/LizTerm.App/obj/project.assets.json`'s target rather than adding to it. Publishing all six RIDs in a loop
and then packing all six afterward reproduces this failure directly: packing the first RID once the loop has
moved on fails with `error NETSDK1047: Assets file '.../obj/project.assets.json' doesn't have a target for
'net10.0/osx-arm64'` — the restore for the *last* RID published is the only one left standing. The working
order, and the one this spike used from 11.4 onward, is publish-then-immediately-pack per RID, not all
publishes followed by all packs.

### 11.4 RID × format matrix

Each RID was published fresh (`dotnet publish src/LizTerm.App -c Release -r <rid> --self-contained -o
<scratch>/<rid>`) immediately before packing it (`parcel pack ./LizTerm.parcel --no-build -r <rid> -p <formats>
-o <scratch>/out -v detailed`), per 11.3. Every combination the brief asked for succeeded — including both
combinations the public documentation leaves unconfirmed:

| RID | Formats requested | Result | Artifact files (under `<output>/<Platform>/`) |
| --- | --- | --- | --- |
| `osx-arm64` | zip, dmg | **both succeed** | `LizTerm.App.arm64.1.0.0.zip`, `LizTerm.App.arm64.1.0.0.dmg` |
| `osx-x64` | zip, dmg | **both succeed** | `LizTerm.App.x64.1.0.0.zip`, `LizTerm.App.x64.1.0.0.dmg` |
| `linux-x64` | zip, deb, rpm | **all three succeed** | `LizTerm.App.x64.1.0.0.zip`, `.deb`, `.rpm` |
| `linux-arm64` | zip, deb, rpm | **all three succeed** (undocumented combination) | `LizTerm.App.arm64.1.0.0.zip`, `.deb`, `.rpm` |
| `win-x64` | zip, nsis | **both succeed** | `LizTerm.App.x64.1.0.0.zip`, `LizTerm.App.x64.1.0.0.exe` |
| `win-arm64` | zip, nsis | **both succeed** (undocumented combination) | `LizTerm.App.arm64.1.0.0.zip`, `LizTerm.App.arm64.1.0.0.exe` |

No combination failed, so no platform is limited to its headline ZIP by this spike. Every listed file was
confirmed on disk: nonzero size (43–54 MB, self-contained single-file publishes), and `file(1)` reports the
expected container in every case — `Zip archive data` for every `.zip`, `Debian binary package (format 2.0)`
for `.deb`, `RPM v4.0 bin` for `.rpm`, `PE32 executable ... Nullsoft Installer self-extracting archive` for
`.exe`, and `.dmg` mounts as a UDIF disk image containing the signed `.app`. NSIS produces a plain `.exe`, not
`-setup.exe` or `.msi`. `MacOsSettings.SignDmg` defaulting to true ad hoc-signs the DMG itself, separately
from the `.app` inside it (`[WARN] Dmg Packaging: Using ad-hoc signing`, once per DMG, beside the app-signing
warning). Windows and Linux packaging produced no warnings at all.

**Naming collision this table exposes for Task 10:** Parcel's artifact filename is
`{ApplicationName}.{arch}.{Version}.{ext}` — architecture and version, with no OS or platform token. Because
the ZIP is the headline format on all three platforms, `LizTerm.App.x64.1.0.0.zip` and
`LizTerm.App.arm64.1.0.0.zip` are each produced **three times**, once per platform, identical in name and
distinct in content. They land in separate `macOS/`, `Linux/`, `Windows/` subdirectories under Parcel's own
`-o`, so nothing collides on disk here — but a GitHub Release's assets share one flat namespace, so uploading
all three platforms' ZIPs unmodified to the same release will silently overwrite two of the three, or be
rejected outright. Task 9 or Task 10 must rename these on upload (for example
`LizTerm-macos-arm64-1.0.0.zip`); the installer formats (`.dmg`, `.deb`, `.rpm`, `.exe`) do not collide, since
each is produced by exactly one platform.

### 11.5 Other observations

- **This worktree had a real `native/out/osx-arm64/b3270`** at spike time (built minutes earlier, for Tasks
  2–4's fixtures, per this session's pre-flight ruling R2 — not built by this task and not something this task
  altered). That let one concrete finding confirm section 2.2 directly rather than by inspection alone:
  publishing for *every* other RID (`linux-x64`, `linux-arm64`, `win-x64`, `win-arm64`) still copied
  `runtimes/osx-arm64/native/b3270` — the *host's* engine, wrong architecture and wrong OS — into each of
  those publish trees, because `LizTerm.App.csproj` keys the copy on `$(NETCoreSdkRuntimeIdentifier)` (the
  build host) rather than the target `-r` RID, exactly as section 2.2 describes. This is Task 4's fix to make,
  not this task's; it is recorded here because the spike happened to produce a live, disk-verified example of
  it. Parcel packaged whatever `dotnet publish` gave it in every case; none of the RID×format failures or
  successes above depend on which engine (if any) was present.
- Where no `native/out/<rid>` existed at all (every RID besides `osx-arm64`, absent the pre-flight fixture
  above), the `Condition="Exists(...)"` in the csproj is false and the `runtimes/` folder is simply omitted
  from that publish tree — confirmed by inspecting the Linux and Windows publish outputs directly, not just
  Parcel's packaged archives. Packaging still succeeds; a v1 build with a genuinely empty `native/out` (this
  task's assumption before the pre-flight fixture appeared) would produce archives with no bundled engine,
  which is expected and not a Parcel defect.
- `GeneralSettings.Version` was left unset, so every artifact carries the schema's documented default,
  `1.0.0`. Task 8/9 will need to set `PARCEL_GENERAL_VERSION` (or the equivalent `-p:` / env override) from the
  release tag; nothing in this spike exercises that path.
- `MacOsSettings.TeamId`, all `SigningCredentialsType`/`NotaryCredentialsType` settings, and every `Win32Settings`
  signing field are absent from the committed config, which is what produces ad hoc signing and the two
  "Notarization credentials are not set — skipping" warnings seen on every macOS pack. That is the intended v1
  state, not an oversight.
